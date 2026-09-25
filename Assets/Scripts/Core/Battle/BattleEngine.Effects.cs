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
            foreach (EffectDef effect in card.Def.Effects)
                if (effect.Trigger == trigger) AddEffect(context, effect);
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
            foreach (EffectDef effect in definition.Effects)
                if (effect.Trigger == EffectTrigger.Attack) AddEffect(context, effect);
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
