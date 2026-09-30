using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    public sealed partial class BattleEngine
    {
        private readonly EffectRegistry _effectRegistry;
        private readonly List<EffectActivation> _effects = new List<EffectActivation>();
        private readonly Dictionary<int, EffectContext> _effectContexts = new Dictionary<int, EffectContext>();
        private readonly List<EffectActivation> _pendingSpecial = new List<EffectActivation>();
        private readonly Stack<EffectWindow> _specialWindows = new Stack<EffectWindow>();
        private EffectWindow _effectWindow;
        private EffectWindow _pendingEffectWindow;
        private EffectStage _runningStage;
        private long _requestSequence;
        internal bool EffectDefenseSucceeded { get { return _defenseSuccess; } }

        private void AddCardEffects(CardInstance card, EffectTrigger trigger, int opponent, int handCount)
        {
            var context = new EffectContext(this, card, opponent, trigger, _atk, handCount);
            _effectContexts[card.Uid] = context;
            EnqueueEffects(context, card.Def.Effects, trigger);
        }

        /// <summary>
        /// 把「一组效果」里某个触发时机的项按<b>合并后的顺序</b>入队。
        ///
        /// <para><b>为什么需要这一层</b>（2026-09-27 用户口径）：充能（<c>am</c>）有两条 α 效果 ——
        /// <c>Haste(1)</c> 与 <c>CoolHandForHaste(1)</c>。按卡表顺序入队的话，玩家会先被问
        /// 「把这一次加速给谁」，之后才去挑要送入冷却的手牌 —— 也就是<b>在还不知道总共能加速几次
        /// 的时候就得做加速的分配</b>。用户要的节奏是反过来的：<b>先挑手牌、算出总的加速次数，
        /// 再一次性把加速目标问完</b>。</para>
        ///
        /// <para><b>实现</b>：只要这张牌上同时有 <see cref="EffectOp.CoolHandForHaste"/>，
        /// 就把同卡同触发的 <see cref="EffectOp.Haste"/> <b>并进它</b>：
        /// 那条 Haste 不入队（否则次数会算两遍），它给的次数改由
        /// <c>CoolHandForHaste</c> 一并发放 —— 于是「选牌 → 算总次数 → 问加速目标」
        /// 成为一条连续流程，而不需要引擎侧再排序。</para>
        ///
        /// <para><b>为什么把吸收来的次数写进 <see cref="EffectDef.Arguments"/> 而不是回头去读卡表</b>：
        /// 这张效果表不一定是卡表来的 —— 模仿（<c>Copy</c>）会把冷却区某张牌的进攻效果
        /// <b>原样复制</b>一份进来（见 <see cref="EffectCopy"/>）。次数在入队这一刻就算好，
        /// 两种情况（本体 / 被复制）自动一致，不必在效果处理器里分辨「我是被复制来的吗」。</para>
        /// </summary>
        private void EnqueueEffects(EffectContext context, IReadOnlyList<EffectDef> effects, EffectTrigger trigger)
        {
            bool absorbHaste = false;
            foreach (EffectDef effect in effects)
                if (effect.Trigger == trigger && effect.Op == EffectOp.CoolHandForHaste) { absorbHaste = true; break; }

            int absorbedHaste = 0;
            if (absorbHaste)
                foreach (EffectDef effect in effects)
                    if (effect.Trigger == trigger && effect.Op == EffectOp.Haste) absorbedHaste += effect.Arg("count", 1);

            foreach (EffectDef effect in effects)
            {
                if (effect.Trigger != trigger) continue;
                if (absorbHaste && effect.Op == EffectOp.Haste) continue;
                if (absorbHaste && effect.Op == EffectOp.CoolHandForHaste && absorbedHaste > 0)
                {
                    AddEffect(context, WithAbsorbedHaste(effect, absorbedHaste));
                    continue;
                }
                AddEffect(context, effect);
            }
        }

        /// <summary>复制一份效果定义，额外带上「被它吸收的加速次数」（见 <see cref="EnqueueEffects"/>）。</summary>
        private static EffectDef WithAbsorbedHaste(EffectDef source, int absorbed)
        {
            var arguments = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> pair in source.Arguments) arguments[pair.Key] = pair.Value;
            arguments["absorbedHaste"] = absorbed;
            return new EffectDef(source.Trigger, source.HandlerId, arguments, source.Aura, source.Text,
                source.SpecialEvent, source.Targets, source.DistinctTargetGroup, source.Conditions, source.Mandatory);
        }
        private void AddEffect(EffectContext context, EffectDef effect)
        {
            _effectRegistry.Validate(effect);
            if (_effects.Count >= 512) throw new InvalidOperationException("Effect expansion exceeded 512 entries.");
            var activation = new EffectActivation { Context = context, Definition = effect };
            _effects.Add(activation);
            if (_effectWindow != null && StageOf(activation) == _runningStage) _effectWindow.Add(activation);
        }
        private EffectStage StageOf(EffectActivation activation)
        {
            EffectStage stage = _effectRegistry.Get(activation.Definition.HandlerId).Stage;
            return stage == EffectStage.Power && activation.Definition.Trigger == EffectTrigger.Defend ? EffectStage.Defense : stage;
        }
        private void RunEffects(EffectStage stage, Action completed)
        {
            if (_effectWindow == null)
            {
                _runningStage = stage;
                _effectWindow = new EffectWindow(_effectRegistry, _effects.FindAll(e => StageOf(e) == stage));
            }
            DecisionRequest request = _effectWindow.Tick();
            if (request != null) { Pending = request; _pendingEffectWindow = _effectWindow; }
            if (!_effectWindow.Complete) return;
            _effectWindow.Dispose(); _effectWindow = null;
            completed();
        }
        private bool AdvanceSpecialEffects()
        {
            if (_pendingSpecial.Count > 0)
            {
                if (_specialWindows.Count >= 64) throw new InvalidOperationException("Special effect recursion exceeded 64 events.");
                _specialWindows.Push(new EffectWindow(_effectRegistry, _pendingSpecial));
                _pendingSpecial.Clear();
            }
            if (_specialWindows.Count == 0) return false;
            EffectWindow window = _specialWindows.Peek();
            DecisionRequest request = window.Tick();
            if (request != null) { Pending = request; _pendingEffectWindow = window; }
            if (window.Complete) { _specialWindows.Pop(); window.Dispose(); }
            return true;
        }

        public void PublishEffectEvent(string eventId, CardInstance source)
        {
            if (source == null || source.RemovedFromGame || State.IsOver) return;
            var context = new EffectContext(this, source, State.Mode.SelectDefender(State, source.OwnerSeat),
                EffectTrigger.Special, _atk, State.Of(source.OwnerSeat).Hand.Count);
            foreach (EffectDef effect in source.Def.Effects)
                if (effect.Trigger == EffectTrigger.Special && effect.SpecialEvent == eventId)
                {
                    _effectRegistry.Validate(effect);
                    _pendingSpecial.Add(new EffectActivation { Context = context, Definition = effect });
                }
        }
        internal void EffectCopy(EffectContext context, CardDef definition)
        {
            if (context.Trigger != EffectTrigger.Attack || context.Attack == null)
                throw new InvalidOperationException("CopyAttack requires an attack context.");
            context.Attack.CopySource = definition;
            context.Attack.BasePower = definition.Power;
            // 与 AddCardEffects 共用同一套入队口径：复制来的效果表里若同时有 Haste 与
            // CoolHandForHaste，也要合并成「先选牌、再一次性问加速」，别把次数算两遍。
            EnqueueEffects(context, definition.Effects, EffectTrigger.Attack);
        }
        internal void EffectFlush(List<CooldownChange> changes)
        { _cooldownBuffer.AddRange(changes); FlushCooldown(); }
        internal void EffectEmit(BattleEvent e) { Emit(e); }
        internal void EffectHeal(int seat, int amount) { if (amount > 0) Heal(seat, amount); }
        internal void EffectDamage(int seat, int amount, string source) { if (amount > 0) ApplyDamage(seat, amount, source); }
        internal void EffectCutMaxHp(int seat, int amount) { if (amount > 0 && !State.IsOver) CutMaxHp(seat, amount); }

        public CardInstance GenerateCard(CardDef definition, int seat)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (State.IsOver) throw new InvalidOperationException("The battle has ended.");
            PlayerState player = State.Of(seat);
            foreach (EffectDef effect in definition.Effects) _effectRegistry.Validate(effect);
            CardInstance card = State.CreateCard(definition, seat);
            card.ToHand(); player.Hand.Add(card);
            Emit(new CardDrawnEvent { Seat = seat, Card = card, Reason = "生成卡牌" });
            return card;
        }
    }
}
