using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-29 · 两张新卡的行为自测：毒刺（<c>ao</c>）的「虚弱」与击穿（<c>ap</c>）的「快速回填光环」。
    ///
    /// <para><b>为什么必须脚本化逼出来</b>：这两条都属于「卡面写了、引擎里没接上」的高危改动 ——</para>
    /// <list type="bullet">
    /// <item><b>虚弱</b>是内核里<b>全新</b>的一类状态（在此之前没有任何 per-player 减益载体）。
    /// 漏接的表现是「毒刺打出去、对面下回合力量一点没少」：不报错、不崩、
    /// 万局统计里连一条不变量都不会红。</item>
    /// <item><b>快速回填光环</b>是 <see cref="AuraKind"/> 里的第 7 种类型。它既不是力量加值
    /// （不能进防御预算）又不是免疫（不能即消耗），走的是「准备 → 随出牌提交」那条路 ——
    /// 接错一侧的表现是「光环亮了但按下去什么也没发生」。</item>
    /// </list>
    ///
    /// <para><b>两层验证</b>：</para>
    /// <list type="number">
    /// <item><b>静态</b>：取整口径（×50% <em>之后</em>向上取整，7→4 而不是 3）与两张卡的卡表结构；</item>
    /// <item><b>动态</b>：真打。分别在若干随机种子里把「手上正好有毒刺 / 击穿」的局面逼出来，
    /// 强制打出它，然后在事件流上核对 —— 施加层数、进攻力量被折半、进攻结束 −1 层、
    /// 光环点亮 → 消耗 → 那张牌进冷却区时剩余冷却 −1。</item>
    /// </list>
    ///
    /// <para><b>样本不足也算失败</b>：只逼出 0 次样本却报 PASS 的断言等于没有断言
    /// （旧版 <c>ValidateAuraTriggers</c> 就吃过这个亏），所以命中数不足会直接 FAIL。</para>
    /// </summary>
    internal static class WeakenScenario
    {
        /// <summary>毒刺。</summary>
        private const string PoisonId = "ao";

        /// <summary>击穿。</summary>
        private const string PierceId = "ap";

        /// <summary>每条要逼出几次完整样本才算证明。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可布置的局面。</summary>
        private const int MaxSeeds = 500;

        public static bool Run(List<string> report)
        {
            bool ok = true;

            ok &= CheckRounding(report);
            ok &= CheckCardDefs(report);
            ok &= Scan(report, "毒刺（ao）的虚弱：施加 2 层 → 被施加者进攻力量折半（向上取整）→ 它进攻结束 −1 层",
                RunOneWeaken);
            ok &= Scan(report, "击穿（ap）的快速回填光环：进冷却区点亮 → 消耗 → 那张牌进冷却区时剩余冷却 −1",
                RunOneRefill);

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  1) 静态：取整口径
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 取整口径（用户 2026-09-29 明确指定）：<b>×50% 之后</b>向上取整。
        ///
        /// <para>⚠ 另一种读法「砍掉一半、砍的那份向上取整」只在奇数上有差别（7 → 3 而不是 4），
        /// 但直接改变「这一刀挡不挡得住」—— 所以钉死成一张表。</para>
        /// </summary>
        private static bool CheckRounding(List<string> report)
        {
            int[] power = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] expect = { 1, 1, 2, 2, 3, 3, 4, 4, 5 };
            var bad = new List<string>();

            for (int i = 0; i < power.Length; i++)
            {
                int got = PlayerState.Weakened(power[i], 1);
                if (got != expect[i])
                {
                    bad.Add(power[i] + "→" + got + "（期望 " + expect[i] + "）");
                }
            }

            if (PlayerState.Weakened(7, 0) != 7)
            {
                bad.Add("0 层不该改力量（7→" + PlayerState.Weakened(7, 0) + "）");
            }

            if (PlayerState.Weakened(0, 3) != 0)
            {
                bad.Add("力量 0 不该受影响（0→" + PlayerState.Weakened(0, 3) + "）");
            }

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "虚弱取整：最终力量 ×50% 后向上取整（7→4 · 9→5 · 5→3 · 2→1 · 无层数不变）"
                       + (bad.Count == 0 ? string.Empty : "：" + string.Join(" / ", bad.ToArray())));
            return bad.Count == 0;
        }

        // ══════════════════════════════════════════════════════
        //  2) 静态：卡表结构
        // ══════════════════════════════════════════════════════

        private static bool CheckCardDefs(List<string> report)
        {
            var bad = new List<string>();

            // ── 毒刺：草 · 力量 7 / 冷却 4，α 给**对手**挂 2 层虚弱 ──
            CardDef poison = CardLibrary.Get(PoisonId);
            if (poison.Power != 7) bad.Add("毒刺力量应为 7，实为 " + poison.Power);
            if (poison.Cooldown != 4) bad.Add("毒刺冷却应为 4，实为 " + poison.Cooldown);
            if (poison.Element != CardElement.Grass) bad.Add("毒刺元素应为草，实为 " + poison.Element);

            EffectDef weaken = null;
            for (int i = 0; i < poison.Effects.Count; i++)
            {
                if (poison.Effects[i].Op == EffectOp.Weaken)
                {
                    weaken = poison.Effects[i];
                }
            }

            if (weaken == null)
            {
                bad.Add("毒刺缺 α 虚弱效果（EffectOp.Weaken）");
            }
            else
            {
                if (weaken.Trigger != EffectTrigger.Attack) bad.Add("毒刺的虚弱应挂在 α 上，实为 " + weaken.Trigger);
                if (weaken.A != 2) bad.Add("毒刺应施加 2 层，实为 " + weaken.A);
                if (weaken.Targets != EffectTargetScope.Opponent)
                {
                    bad.Add("毒刺的虚弱目标必须是 Opponent（实为 " + weaken.Targets
                            + "）—— 写成 Participants 会连自己一起削");
                }
            }

            // ── 击穿：电 · 力量 3 / 冷却 4，α 光环（快速回填）+ α 连击 ──
            CardDef pierce = CardLibrary.Get(PierceId);
            if (pierce.Power != 3) bad.Add("击穿力量应为 3，实为 " + pierce.Power);
            if (pierce.Cooldown != 4) bad.Add("击穿冷却应为 4，实为 " + pierce.Cooldown);
            if (pierce.Element != CardElement.Electric) bad.Add("击穿元素应为电，实为 " + pierce.Element);

            bool hasRefillAura = false;
            bool hasCombo = false;
            for (int i = 0; i < pierce.Effects.Count; i++)
            {
                EffectDef ef = pierce.Effects[i];
                if (ef.Op == EffectOp.Aura && ef.Aura == AuraKind.QuickRefill && ef.Trigger == EffectTrigger.Attack)
                {
                    hasRefillAura = true;
                }

                if (ef.Op == EffectOp.Combo && ef.Trigger == EffectTrigger.Attack)
                {
                    hasCombo = true;
                }
            }

            if (!hasRefillAura) bad.Add("击穿缺 α 光环：快速回填（AuraKind.QuickRefill）");
            if (!hasCombo) bad.Add("击穿缺 α 连击");
            if (pierce.AuraTokenCount != 1) bad.Add("击穿应带 1 枚光环指示物，实为 " + pierce.AuraTokenCount);
            if (!AuraResolver.UsableIn(AuraKind.QuickRefill, AuraContext.Attack))
            {
                bad.Add("快速回填光环在进攻场合不可用 —— 击穿打出去会是一枚用不掉的死指示物");
            }

            if (AuraResolver.IsPowerBonus(AuraKind.QuickRefill))
            {
                bad.Add("快速回填光环被当成了力量加值 —— 它会污染防御侧的「还差几点补值」预算");
            }

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "新卡卡表结构：毒刺（草 7/4 · α 虚弱 ×2 给对手）、"
                       + "击穿（电 3/4 · α 光环快速回填 + α 连击）"
                       + (bad.Count == 0 ? string.Empty : "：" + string.Join(" / ", bad.ToArray())));
            return bad.Count == 0;
        }

        // ══════════════════════════════════════════════════════
        //  扫描框架
        // ══════════════════════════════════════════════════════

        /// <summary>一次扫描的结果。<see cref="Sampled"/> = false 表示这一局没构成样本（换种子）。</summary>
        private sealed class Outcome
        {
            public bool Sampled;
            public bool Ok;
            public string Why;
        }

        private static bool Scan(List<string> report, string label, Func<int, Outcome> runOne)
        {
            int hits = 0;
            int scanned = 0;
            var failures = new List<string>();

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;
                Outcome outcome = runOne(seed);

                if (outcome == null || !outcome.Sampled)
                {
                    scanned--;
                    continue;
                }

                hits++;

                if (!outcome.Ok && failures.Count < 6)
                {
                    failures.Add("seed " + seed + " → " + outcome.Why);
                }
            }

            bool ok = hits >= NeededHits && failures.Count == 0;
            report.Add((ok ? "  [PASS] " : "  [FAIL] ") + label
                       + "（命中 " + hits + " 次 / 扫了 " + scanned + " 个种子）");

            for (int i = 0; i < failures.Count; i++)
            {
                report.Add("      · " + failures[i]);
            }

            if (hits < NeededHits)
            {
                report.Add("      · 只逼出 " + hits + " 次完整样本，不足以证明（样本不足一律算失败）");
            }

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  3) 动态：毒刺的虚弱
        // ══════════════════════════════════════════════════════

        private static Outcome RunOneWeaken(int seed)
        {
            var engine = BattleEngine.Create(seed);
            var run = new WeakenRun();
            engine.OnEvent += run.OnEvent;

            try
            {
                Pump(engine, run);
            }
            catch (Exception ex)
            {
                return new Outcome { Sampled = true, Ok = false, Why = "异常 " + ex.GetType().Name + " " + ex.Message };
            }
            finally
            {
                engine.OnEvent -= run.OnEvent;
            }

            return run.Result();
        }

        /// <summary>推进对局直到终局 / Runner 说停 / 步数超限。</summary>
        private static void Pump(BattleEngine engine, IRunner run)
        {
            engine.Start();
            int guard = 0;

            while (!engine.IsOver && !run.Stop)
            {
                engine.Advance();
                if (engine.IsOver)
                {
                    break;
                }

                if (engine.Pending == null || ++guard > 20000)
                {
                    return;
                }

                engine.Submit(run.Decide(engine.Pending));
            }
        }

        private interface IRunner
        {
            bool Stop { get; }
            DecisionResponse Decide(DecisionRequest request);
        }

        /// <summary>毒刺一局的观察器。</summary>
        private sealed class WeakenRun : IRunner
        {
            private readonly SimpleAiAgent _brain = new SimpleAiAgent();

            private bool _poisonPlayed;
            private int _weakenedSeat = -1;

            public bool Stop { get; private set; }

            // ── 观察到的量 ──
            private bool _sawApplied;
            private int _appliedAmount;
            private int _stacksAfterApply;
            private bool _sawStrike;
            private int _strikeStacks;
            private int _strikeBase;
            private int _strikeBonus;
            private int _strikeFinal;
            private bool _sawDecay;
            private int _stacksAfterDecay;

            public void OnEvent(BattleEvent e)
            {
                var applied = e as WeakenAppliedEvent;
                if (applied != null && !_sawApplied)
                {
                    _sawApplied = true;
                    _appliedAmount = applied.Amount;
                    _stacksAfterApply = applied.Stacks;
                    _weakenedSeat = applied.Seat;
                }

                var power = e as AttackPowerResolvedEvent;
                if (power != null && _sawApplied && power.Seat == _weakenedSeat && !_sawStrike)
                {
                    _sawStrike = true;
                    _strikeStacks = power.WeakenStacks;
                    _strikeBase = power.BasePower;
                    _strikeBonus = power.BonusPower;
                    _strikeFinal = power.FinalPower;
                }

                var decay = e as WeakenDecayedEvent;
                if (decay != null && _sawApplied && decay.Seat == _weakenedSeat && !_sawDecay)
                {
                    _sawDecay = true;
                    _stacksAfterDecay = decay.Stacks;
                    Stop = true;
                }
            }

            public DecisionResponse Decide(DecisionRequest request)
            {
                if (request.Kind == RequestKind.ChooseAttackCard && !_poisonPlayed)
                {
                    int index = FindCardOption(request, PoisonId);
                    if (index >= 0)
                    {
                        _poisonPlayed = true;
                        return DecisionResponse.Of(request.Seat, index);
                    }
                }

                return _brain.Decide(request);
            }

            public Outcome Result()
            {
                // 没真的打出毒刺 → 这一局不构成样本（换种子）。
                if (!_sawApplied)
                {
                    return new Outcome { Sampled = false };
                }

                // 观察窗口没走完（被施加者还没进攻 / 还没递减）→ 也不构成完整样本。
                if (!_sawStrike || !_sawDecay)
                {
                    return new Outcome { Sampled = false };
                }

                var bad = new List<string>();

                if (_appliedAmount != 2)
                {
                    bad.Add("毒刺应施加 2 层，实为 " + _appliedAmount);
                }

                if (_stacksAfterApply != 2)
                {
                    bad.Add("施加后应共 2 层，实为 " + _stacksAfterApply);
                }

                if (_strikeStacks != 2)
                {
                    bad.Add("被施加者进攻时应带 2 层虚弱，实为 " + _strikeStacks);
                }

                int expect = PlayerState.Weakened(_strikeBase + _strikeBonus, 2);
                if (_strikeFinal != expect)
                {
                    bad.Add("被施加者进攻力量应为 " + expect
                            + "（基础 " + _strikeBase + " + " + _strikeBonus + " 后折半上取整），实为 " + _strikeFinal);
                }

                if (_stacksAfterDecay != 1)
                {
                    bad.Add("被施加者进攻结束后应余 1 层，实为 " + _stacksAfterDecay);
                }

                return new Outcome { Sampled = true, Ok = bad.Count == 0, Why = string.Join(" / ", bad.ToArray()) };
            }
        }

        // ══════════════════════════════════════════════════════
        //  4) 动态：击穿的快速回填光环
        // ══════════════════════════════════════════════════════

        private static Outcome RunOneRefill(int seed)
        {
            var engine = BattleEngine.Create(seed);
            var run = new RefillRun();
            engine.OnEvent += run.OnEvent;

            try
            {
                Pump(engine, run);
            }
            catch (Exception ex)
            {
                return new Outcome { Sampled = true, Ok = false, Why = "异常 " + ex.GetType().Name + " " + ex.Message };
            }
            finally
            {
                engine.OnEvent -= run.OnEvent;
            }

            return run.Result();
        }

        /// <summary>击穿一局的观察器。</summary>
        private sealed class RefillRun : IRunner
        {
            private readonly SimpleAiAgent _brain = new SimpleAiAgent();

            private bool _piercePlayed;
            private CardInstance _pierce;
            private int _pierceSeat = -1;
            private bool _haloUsed;
            private CardInstance _target;

            public bool Stop { get; private set; }

            // ── 观察到的量 ──
            private bool _sawActivated;
            private int _tokenCount = -1;
            private AuraKind _tokenKind = AuraKind.None;
            private bool _sawConsumed;
            private bool _sawTargetCooldown;
            private int _targetCooldown = -1;

            public void OnEvent(BattleEvent e)
            {
                var activated = e as AuraActivatedEvent;
                if (activated != null && activated.Card == _pierce && !_sawActivated)
                {
                    _sawActivated = true;
                    _tokenCount = activated.Card.AuraTokens;

                    if (activated.Card.ActiveAuras.Count > 0)
                    {
                        _tokenKind = activated.Card.ActiveAuras[0].Definition.Aura;
                    }
                }

                var consumed = e as AuraConsumedEvent;
                if (consumed != null && consumed.Kind == AuraKind.QuickRefill)
                {
                    _sawConsumed = true;
                }

                var cooldown = e as CooldownChangedEvent;
                if (cooldown != null && cooldown.Change.Card == _target && _target != null
                    && cooldown.Change.Reason == "本次进攻牌进入冷却区" && !_sawTargetCooldown)
                {
                    _sawTargetCooldown = true;
                    _targetCooldown = cooldown.Change.To;
                    Stop = true;
                }
            }

            public DecisionResponse Decide(DecisionRequest request)
            {
                if (request.Kind == RequestKind.ChooseAttackCard)
                {
                    // ① 先把击穿打出去（它一进冷却区就会点亮那枚快速回填指示物）。
                    if (!_piercePlayed)
                    {
                        int index = FindCardOption(request, PierceId);
                        if (index >= 0)
                        {
                            _piercePlayed = true;
                            _pierce = request.Options[index].Card;
                            _pierceSeat = request.Seat;
                            return DecisionResponse.Of(request.Seat, index);
                        }
                    }
                    else if (!_haloUsed && request.Seat == _pierceSeat)
                    {
                        // ② 击穿还在冷却区 → 把它的指示物「准备使用」，配一张本身不带快速回填的牌。
                        int aura = FindAuraOption(request, AuraKind.QuickRefill);
                        int card = FindCardWithoutQuickRefill(request, _pierce);

                        if (aura >= 0 && card >= 0)
                        {
                            _haloUsed = true;
                            _target = request.Options[card].Card;
                            return DecisionResponse.WithAuras(request.Seat, new[] { card }, new[] { aura });
                        }
                    }
                }

                return _brain.Decide(request);
            }

            public Outcome Result()
            {
                if (!_piercePlayed || !_sawActivated)
                {
                    return new Outcome { Sampled = false };
                }

                if (!_haloUsed || !_sawTargetCooldown)
                {
                    return new Outcome { Sampled = false };
                }

                var bad = new List<string>();

                if (_tokenCount != 1)
                {
                    bad.Add("击穿进冷却区后应有 1 枚指示物，实为 " + _tokenCount);
                }

                if (_tokenKind != AuraKind.QuickRefill)
                {
                    bad.Add("点亮的光环类型应为 QuickRefill，实为 " + _tokenKind);
                }

                if (!_sawConsumed)
                {
                    bad.Add("消耗那枚光环时没有发出 QuickRefill 的 AuraConsumedEvent");
                }

                int expect = _target.Def.Cooldown - 1;
                if (_targetCooldown != expect)
                {
                    bad.Add("被赋予快速回填的「" + _target.Def.Name + "」进冷却区时剩余应为 " + expect
                            + "（基础 " + _target.Def.Cooldown + " − 1），实为 " + _targetCooldown);
                }

                return new Outcome { Sampled = true, Ok = bad.Count == 0, Why = string.Join(" / ", bad.ToArray()) };
            }

        }

        // ══════════════════════════════════════════════════════
        //  选项查找
        // ══════════════════════════════════════════════════════

        /// <summary>找「打出某张卡」的选项序号（光环选项也带 Card，必须排掉）；没有返回 −1。</summary>
        private static int FindCardOption(DecisionRequest request, string cardId)
        {
            for (int i = 0; i < request.Options.Count; i++)
            {
                Option o = request.Options[i];
                if (o.AuraSource == null && !o.IsSkip && o.Card != null && o.Card.Def.Id == cardId)
                {
                    return o.Index;
                }
            }

            return -1;
        }

        /// <summary>找某种光环的选项序号；没有返回 −1。</summary>
        private static int FindAuraOption(DecisionRequest request, AuraKind kind)
        {
            for (int i = 0; i < request.Options.Count; i++)
            {
                Option o = request.Options[i];
                if (o.AuraSource != null && o.AuraKind == kind)
                {
                    return o.Index;
                }
            }

            return -1;
        }

        /// <summary>找一张「本身不带快速回填」的出牌选项（否则说不清 −1 是谁给的）；没有返回 −1。</summary>
        private static int FindCardWithoutQuickRefill(DecisionRequest request, CardInstance exclude)
        {
            for (int i = 0; i < request.Options.Count; i++)
            {
                Option o = request.Options[i];
                if (o.AuraSource != null || o.IsSkip || o.Card == null)
                {
                    continue;
                }

                if (ReferenceEquals(o.Card, exclude) || HasOwnQuickRefillStatic(o.Card) || o.PairCard != null)
                {
                    continue;
                }

                return o.Index;
            }

            return -1;
        }

        private static bool HasOwnQuickRefillStatic(CardInstance card)
        {
            if (card == null)
            {
                return false;
            }

            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                if (card.Def.Effects[i].Op == EffectOp.QuickRefill && card.Def.Effects[i].Trigger == EffectTrigger.Attack)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
