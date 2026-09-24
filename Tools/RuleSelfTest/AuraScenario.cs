using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// M15 · 免疫光环场景自测（2026-09-18 规则改动）。
    ///
    /// <para><b>为什么要单独写这一条</b>：<see cref="SimpleAiAgent.UseAuras"/> 默认 <c>false</c>
    /// —— 万局随机对局里 AI 从来不用光环，所以「免疫光环消耗后会发生什么」这条路径
    /// 在「跑一万局」里<strong>一次都不会被走到</strong>，统计全绿也证明不了它是对的。
    /// 这里用脚本化的双方把局面<strong>逼</strong>出来：攻击方出一张低力量牌、
    /// 防御方冷却区里塞一枚「免疫力量 ≤3」的指示物，然后消耗它。</para>
    ///
    /// <para><b>断言的就是这次规则改动的全部内容</b>：消耗免疫光环 = 整个攻击被免疫，
    /// <b>并且不需要再打出一张防御牌</b>。</para>
    /// <list type="bullet">
    /// <item>必须有 <c>DefenseResolvedEvent{ UsedImmune = true, Success = true, Cards 为空 }</c>；</item>
    /// <item>此后不得再出现给防御方的 <c>ChooseDefense</c> 请求；</item>
    /// <item>防御方不因这次攻击掉血；</item>
    /// <item>防御方的手牌张数在这场免疫前后<strong>不变</strong>（一张都没交）。</item>
    /// </list>
    /// </summary>
    internal static class AuraScenario
    {
        /// <summary>要收集几次「命中」才算这条断言真的被跑到。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可成立的局面。</summary>
        private const int MaxSeeds = 800;

        public static bool Run(List<string> report)
        {
            int hits = 0;
            int scanned = 0;
            var failures = new List<string>();
            var notes = new List<string>();

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;

                Hit hit = RunOne(seed);
                if (hit == null)
                {
                    continue;   // 这一局没逼出免疫局面（力量不在免疫区间等），换种子
                }

                if (!hit.Useful)
                {
                    // 与本条规则无关的杂音（状态机卡住 / 抛异常）—— 记一笔但不计入命中
                    if (notes.Count < 5)
                    {
                        notes.Add("seed " + seed + " → "
                                  + (hit.Error != null ? hit.Error : "推进无进展（疑似状态机卡住）"));
                    }

                    if (hit.Stalled || hit.Error != null)
                    {
                        scanned--;
                    }

                    continue;
                }

                hits++;

                if (!hit.Ok)
                {
                    failures.Add("seed " + seed + " → " + hit.Why);
                }
            }

            bool ok = hits >= NeededHits && failures.Count == 0;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "规则 §6.5 改动：免疫光环消耗后整个攻击被免疫、且不需要再打防御牌"
                       + "（命中 " + hits + " 次 / 扫了 " + scanned + " 个种子）");

            for (int i = 0; i < failures.Count; i++)
            {
                report.Add("      · " + failures[i]);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                report.Add("      · 杂音：" + notes[i]);
            }

            if (hits < NeededHits)
            {
                report.Add("      · 只逼出 " + hits + " 次免疫局面，样本不足以证明（检查测试设置）");
            }

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  一次尝试
        // ══════════════════════════════════════════════════════

        private sealed class Hit
        {
            public bool UsedAura;
            public bool ImmuneResolved;
            public bool ImmuneResolveOk;
            public bool DefenseAskedAfterImmune;
            public bool DamagedAfterImmune;
            public int HandBefore;
            public int HandAfter = -1;

            /// <summary>状态机中途卡住（本局不算有效样本，但要在报告里留痕）。</summary>
            public bool Stalled;

            /// <summary>抛异常了（同上）。</summary>
            public string Error;

            /// <summary>这一局到底有没有真的走到「免疫被消耗」那一步（没走到就不算样本）。</summary>
            public bool Useful
            {
                get { return UsedAura || ImmuneResolved; }
            }

            public bool Ok
            {
                get
                {
                    return UsedAura
                           && ImmuneResolved
                           && ImmuneResolveOk
                           && !DefenseAskedAfterImmune
                           && !DamagedAfterImmune
                           && HandAfter == HandBefore;
                }
            }
            public string Why
            {
                get
                {
                    if (!UsedAura)
                    {
                        return "没有任何一方消耗免疫光环";
                    }

                    if (!ImmuneResolved)
                    {
                        return "消耗了免疫光环却没有发出 DefenseResolvedEvent";
                    }

                    if (!ImmuneResolveOk)
                    {
                        return "免疫结算不是「成功且零张防御牌」";
                    }

                    if (DefenseAskedAfterImmune)
                    {
                        return "免疫之后仍向防御方索要防御牌（旧推论 P1 的行为）";
                    }

                    if (DamagedAfterImmune)
                    {
                        return "免疫之后防御方仍然掉血";
                    }

                    if (HandAfter != HandBefore)
                    {
                        return "免疫期间防御方手牌从 " + HandBefore + " 变成 " + HandAfter + "（应当不交牌）";
                    }

                    return "未知";
                }
            }
        }

        private static Hit RunOne(int seed)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine);

            engine.OnEvent += run.OnEvent;

            // ⚠ 必须先 Start()：引擎初始停在「未开赛」的相位，
            //   不 Start 直接 Advance 会一路推到 Phase=Finished 然后触发死循环保护
            //   （表现为每一次都抛「引擎推进次数超限」）。
            engine.Start();

            int guard = 0;

            try
            {
                while (!engine.IsOver && !run.Stop)
                {
                    engine.Advance();

                    if (engine.IsOver)
                    {
                        break;
                    }

                    if (++guard > 20000)
                    {
                        run.Hit.Stalled = true;
                        break;
                    }

                    if (engine.Pending == null)
                    {
                        run.Hit.Stalled = true;
                        break;
                    }

                    engine.Submit(run.Decide(engine.Pending));
                }
            }
            catch (Exception ex)
            {
                run.Hit.Error = ex.GetType().Name + ": " + ex.Message;
            }

            engine.OnEvent -= run.OnEvent;

            return run.Hit.Useful || run.Hit.Stalled || run.Hit.Error != null ? run.Hit : null;
        }

        // ══════════════════════════════════════════════════════
        //  脚本化双方 + 事件观察
        // ══════════════════════════════════════════════════════

        private sealed class Runner
        {
            private readonly BattleEngine _engine;

            public readonly Hit Hit = new Hit();

            /// <summary>已经布好免疫局面的攻击方座位（−1 = 还没布）。</summary>
            private int _armedAttacker = -1;

            /// <summary>被布了免疫源的防御方座位。</summary>
            private int _defender = -1;

            /// <summary>
            /// 「这一半场刚刚消耗了免疫光环、还没结算完」——
            /// ⚠ 必须按半场清零：<see cref="Hit.UsedAura"/> 是一局里的事实，
            /// 拿它去判「后面那一拍防御」会把**下一回合的正常防御**误报成违规。
            /// </summary>
            private bool _immunePending;

            /// <summary>免疫已经结算完，可以收摊了。</summary>
            public bool Stop { get; private set; }

            public Runner(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                // ⚠ 必须按半场清零：_immunePending 描述的是「这一半场刚消耗了免疫光环」，
                //   不清的话下一回合的正常防御会被误报成违规。
                if (e is TurnStartedEvent)
                {
                    _immunePending = false;
                }

                var resolved = e as DefenseResolvedEvent;
                if (resolved != null && resolved.UsedImmune)
                {
                    Hit.ImmuneResolved = true;
                    Hit.ImmuneResolveOk = resolved.Success && resolved.Cards.Count == 0;

                    if (Hit.HandBefore > 0)
                    {
                        Hit.HandAfter = _engine.State.Of(resolved.DefenderSeat).Hand.Count;
                    }

                    Stop = true;   // 被测行为已经发生，本局到此为止
                }

                // 免疫之后还掉血 = 免疫没生效
                var dmg = e as DamageTakenEvent;
                if (dmg != null && _immunePending && dmg.Seat == _defender)
                {
                    Hit.DamagedAfterImmune = true;
                }
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        // 不替换（空回填 = 一次都不换）
                        return DecisionResponse.Of(req.Seat);

                    case RequestKind.ChooseAttackCard:
                        return DecideAttack(req);

                    case RequestKind.ChooseDefense:
                        if (_immunePending && req.Seat == _defender)
                        {
                            Hit.DefenseAskedAfterImmune = true;
                        }

                        return DecideDefense(req);

                    default:
                        return First(req);
                }
            }

            /// <summary>
            /// 进攻：第一次叫牌时顺手<strong>布好免疫局面</strong> ——
            /// 往防御方冷却区塞一枚阈值合适的免疫指示物、并且挑一张力量刚好落在免疫区间里的牌。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                int attackerSeat = req.Seat;
                int defenderSeat = 1 - attackerSeat;

                if (_armedAttacker < 0 && defenderSeat >= 0)
                {
                    // 手里力量最小的那张（免疫低位最容易命中）
                    Option weakest = null;
                    for (int i = 0; i < req.Options.Count; i++)
                    {
                        Option o = req.Options[i];
                        if (o.IsSkip || o.Card == null)
                        {
                            continue;
                        }

                        if (weakest == null || o.Card.EffectivePower < weakest.Card.EffectivePower)
                        {
                            weakest = o;
                        }
                    }

                    if (weakest != null && weakest.Card.EffectivePower <= AuraScenario.ImmuneLowThreshold)
                    {
                        CardInstance src = InjectImmuneSource(defenderSeat, AuraKind.ImmuneLow);
                        if (src != null)
                        {
                            _armedAttacker = attackerSeat;
                            _defender = defenderSeat;
                            Hit.HandBefore = _engine.State.Of(defenderSeat).Hand.Count;
                        }
                    }

                    return weakest == null ? First(req) : Of(req, weakest);
                }

                return First(req);
            }

            /// <summary>
            /// 防御：<strong>必须把免疫光环拖进判定区</strong>（这就是被测行为）。
            ///
            /// <para>2026-09-18 起光环不再单独占一拍 —— 免疫选项就在防御那一拍的
            /// <c>Options</c> 里，回填方式是「只给光环、不给主选择」
            /// （<see cref="DecisionResponse.WithAuras"/> + 空的 optionIndices），
            /// 效果是：整个攻击（含双发）被免疫，且不需要交任何防御牌。</para>
            /// </summary>
            private DecisionResponse DecideDefense(DecisionRequest req)
            {
                if (req.Seat != _defender || _defender < 0 || _immunePending)
                {
                    return First(req);
                }

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Kind != OptionKind.UseAura || o.AuraSource == null)
                    {
                        continue;
                    }

                    if (!AuraResolver.IsImmune(o.AuraKind))
                    {
                        continue;
                    }

                    Hit.UsedAura = true;
                    _immunePending = true;

                    if (Hit.HandBefore <= 0)
                    {
                        Hit.HandBefore = _engine.State.Of(_defender).Hand.Count;
                    }

                    return DecisionResponse.WithAuras(req.Seat, null, new[] { o.Index });
                }

                return First(req);
            }

            /// <summary>往某方冷却区塞一枚「该类型的免疫指示物」—— 测试专用，直接改核心状态。</summary>
            private CardInstance InjectImmuneSource(int seat, AuraKind kind)
            {
                CardDef def = FindAuraCard(kind);
                if (def == null)
                {
                    return null;
                }

                var inst = new CardInstance(def, seat);
                var sink = new List<CooldownChange>();
                if (!CooldownOps.PutIntoCooldown(_engine.State.Of(seat), inst, 3, sink))
                {
                    return null;
                }

                inst.AuraTokens = 1;
                inst.AuraLive = true;
                return inst;
            }

            private static CardDef FindAuraCard(AuraKind kind)
            {
                IReadOnlyList<CardDef> all = CardLibrary.All;
                for (int i = 0; i < all.Count; i++)
                {
                    CardDef def = all[i];
                    for (int e = 0; e < def.Effects.Count; e++)
                    {
                        EffectDef ef = def.Effects[e];
                        if (ef.Op == EffectOp.Aura && ef.Aura == kind)
                        {
                            return def;
                        }
                    }
                }

                return null;
            }

            private static DecisionResponse First(DecisionRequest req)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    if (!req.Options[i].IsSkip)
                    {
                        return Of(req, req.Options[i]);
                    }
                }

                return DecisionResponse.Of(req.Seat);
            }

            private static DecisionResponse Of(DecisionRequest req, Option o)
            {
                return DecisionResponse.Of(req.Seat, o.Index);
            }
        }

        /// <summary>「免疫力量 ≤3」那张卡的门槛；挑攻击牌时用它当上限。</summary>
        private const int ImmuneLowThreshold = 3;
    }
}
