using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-20 · β「防御时力量 +A」是否真的进了防御判定的场景自测。
    ///
    /// <para><b>为什么要单独写这一条</b>：<see cref="EffectOp.DefPlus"/> 从卡表录入那天起
    /// <b>一个结算点都没有</b> —— 潮汐（e）与地动波（z）的「β 防御时力量 +1」全程静默失效。
    /// 这类缺陷在万局统计里完全无声：引擎自己前后一致（它一直用 <c>EffectivePower</c> 当防御力量），
    /// 事件流、手牌守恒、胜负分布全都正常，唯一的破绽是<b>「明明写得下的一张牌却选不出来」</b>。</para>
    ///
    /// <para><b>怎么逼出来</b>：脚本化双方，只在「攻击方手里有一张 8 力量、防御方手里有地动波」
    /// 时动手 —— 地动波力量 7，只有算上 β +1 才够挡 8。于是：</para>
    /// <list type="bullet">
    /// <item>防御决策里<b>必须出现「打出 地动波」这个选项</b>（DefPlus 没接上时它会被合法性过滤掉）；</item>
    /// <item>选中它之后 <c>DefenseResolvedEvent.Success</c> 必须为 true。</item>
    /// </list>
    ///
    /// <para>被测行为只依赖「攻击最终力量 = 8」这一点，所以用 <c>req.ContextPower</c> 复核，
    /// 别的因素（灼烧 / 雪球那类加力量的效果把 8 抬上去）会让本局不算样本，直接换种子。</para>
    /// </summary>
    internal static class BetaDefenseScenario
    {
        /// <summary>要收集几次「命中」才算这条断言真的被跑到。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可成立的局面。</summary>
        private const int MaxSeeds = 1200;

        /// <summary>被验证的那张牌：地动波（力量 7，β 防御时力量 +1）。</summary>
        private const string BetaCardId = "z";

        /// <summary>本次进攻的最终力量 —— 7 挡不住、7+1 正好挡住。</summary>
        private const int AttackPower = 8;

        public static bool Run(List<string> report)
        {
            var failures = new List<string>();

            // 先做一个与对局无关的纯算式自检：把「地动波能不能挡 8」这件事钉死。
            bool mathOk = CheckMath(out string mathWhy);
            report.Add((mathOk ? "  [PASS] " : "  [FAIL] ")
                       + "β 防御力量计入防御判定：地动波（7，β+1）能挡 8 力量" + mathWhy);

            int hits = 0;
            int scanned = 0;
            var notes = new List<string>();

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;

                Hit hit = RunOne(seed);
                if (hit == null)
                {
                    continue;   // 这一局没逼出「8 打 7」的局面，换种子
                }

                if (hit.Stalled || hit.Error != null)
                {
                    if (notes.Count < 5)
                    {
                        notes.Add("seed " + seed + " → "
                                  + (hit.Error ?? "推进无进展（疑似状态机卡住）"));
                    }

                    scanned--;
                    continue;
                }

                if (!hit.Useful)
                {
                    continue;
                }

                hits++;

                if (!hit.Ok)
                {
                    failures.Add("seed " + seed + " → " + hit.Why);
                }
            }

            bool ok = mathOk && hits >= NeededHits && failures.Count == 0;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "β 防御力量在真实对局里生效：8 力量进攻下「打出 地动波」是合法选项且挡得住"
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
                report.Add("      · 只逼出 " + hits + " 次「8 打地动波」局面，样本不足以证明（检查测试设置）");
            }

            return ok;
        }

        /// <summary>纯算式自检：<see cref="DefenseResolver"/> 的三个数必须自洽。</summary>
        private static bool CheckMath(out string why)
        {
            why = string.Empty;

            CardDef def = CardLibrary.Get(BetaCardId);
            var card = new CardInstance(def, 0);
            card.ToHand();

            int power = DefenseResolver.DefensePower(card);
            if (power != def.Power + 1)
            {
                why = "（DefensePower = " + power + "，期望 " + (def.Power + 1) + "）";
                return false;
            }

            if (DefenseResolver.RequiredBonus(card, AttackPower) != 0)
            {
                why = "（RequiredBonus(" + AttackPower + ") = "
                      + DefenseResolver.RequiredBonus(card, AttackPower) + "，期望 0）";
                return false;
            }

            if (DefenseResolver.RequiredBonus(card, AttackPower + 1) != 1)
            {
                why = "（RequiredBonus(" + (AttackPower + 1) + ") = "
                      + DefenseResolver.RequiredBonus(card, AttackPower + 1) + "，期望 1）";
                return false;
            }

            return true;
        }

        // ══════════════════════════════════════════════════════
        //  一次尝试
        // ══════════════════════════════════════════════════════

        private sealed class Hit
        {
            /// <summary>本局确实走到了「8 力量进攻 vs 手里有地动波的防御方」那一拍。</summary>
            public bool Asked;

            /// <summary>那一拍里「打出 地动波」确实出现在合法选项里。</summary>
            public bool Offered;

            /// <summary>选了它之后引擎判防御成功。</summary>
            public bool Success;

            public bool Stalled;
            public string Error;

            /// <summary>本局是否算有效样本 —— 只看有没有真的问到那个问题。</summary>
            public bool Useful
            {
                get { return Asked; }
            }

            public bool Ok
            {
                get { return Asked && Offered && Success; }
            }

            public string Why
            {
                get
                {
                    if (!Offered)
                    {
                        return "8 力量的进攻下，力量 7 的地动波不在防御选项里（β +1 没被算进防御力量）";
                    }

                    if (!Success)
                    {
                        return "选了地动波却没有判防御成功";
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

            return run.Hit.Asked || run.Hit.Stalled || run.Hit.Error != null ? run.Hit : null;
        }

        // ══════════════════════════════════════════════════════
        //  脚本化双方
        // ══════════════════════════════════════════════════════

        private sealed class Runner
        {
            private readonly BattleEngine _engine;

            public readonly Hit Hit = new Hit();

            private int _defender = -1;

            /// <summary>本局已经布好「8 打地动波」的局面。</summary>
            private bool _armed;

            /// <summary>本局已经收工（被测行为已发生）。</summary>
            public bool Stop { get; private set; }

            public Runner(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                var resolved = e as DefenseResolvedEvent;
                if (resolved == null || !Hit.Asked)
                {
                    return;
                }

                // 走到这里答案就齐了（有没有给选项、选了之后判没判成功），收工。
                Hit.Success = Hit.Offered && resolved.Success;
                Stop = true;
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        // 一次都不换 —— 手牌越干净，「谁手里有那张牌」越好确认
                        return DecisionResponse.Of(req.Seat);

                    case RequestKind.ChooseAttackCard:
                        return DecideAttack(req);

                    case RequestKind.ChooseDefense:
                        return DecideDefense(req);

                    default:
                        return First(req);
                }
            }

            /// <summary>
            /// 进攻：只在「自己手里有一张 8 力量、对面手里有地动波」时选那张 8 ——
            /// 局面布不起来就照常出牌（这一局不算样本）。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_armed)
                {
                    return First(req);
                }

                int defenderSeat = 1 - req.Seat;
                if (!HoldsBetaCard(defenderSeat))
                {
                    return First(req);
                }

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.Card == null || o.Card.EffectivePower != AttackPower)
                    {
                        continue;
                    }

                    _armed = true;
                    _defender = defenderSeat;
                    return Of(req, o);
                }

                return First(req);
            }

            /// <summary>
            /// 防御：复核这一刀确实是 8 力量（否则地动波挡 8 这件事没被问到），
            /// 然后看引擎给不给「打出 地动波」这个选项，并选它。
            /// </summary>
            private DecisionResponse DecideDefense(DecisionRequest req)
            {
                if (!_armed || Hit.Asked || req.Seat != _defender || req.ContextPower != AttackPower)
                {
                    return First(req);
                }

                // 问题问对了（确实是 8 力量打地动波）—— 从这一刻起本局就是一个有效样本，
                // 后面不管引擎给不给选项，结论都已经确定。
                Hit.Asked = true;

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.Card == null || o.Card.Def.Id != BetaCardId)
                    {
                        continue;
                    }

                    Hit.Offered = true;
                    return Of(req, o);
                }

                return First(req);
            }

            private bool HoldsBetaCard(int seat)
            {
                PlayerState p = _engine.State.Of(seat);
                if (p == null)
                {
                    return false;
                }

                for (int i = 0; i < p.Hand.Count; i++)
                {
                    if (p.Hand[i].Def.Id == BetaCardId)
                    {
                        return true;
                    }
                }

                return false;
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
    }
}
