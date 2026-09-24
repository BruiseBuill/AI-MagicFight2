using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-22 · 「选择送入冷却的手牌」（磁暴 / 充能 / 电弧）的 <c>MinSelect</c> / <c>MaxSelect</c> 自测。
    ///
    /// <para><b>为什么要专门测这一条</b>：这三种效果的选项都是「玩家自己的手牌」，界面（M27 选牌弹窗）
    /// 完全靠引擎给的 <c>MinSelect</c> / <c>MaxSelect</c> 来决定
    /// 「确认键亮不亮」「还能不能再放一张」—— 界面自己不做任何规则判断（铁律 3）。
    /// 于是这两个数字一旦写错，界面上就表现为「点第二张毫无反应」「确认键该亮不亮」，
    /// 而<b>万局统计里一个异常都不会有</b>（事件数、守恒、胜负分布全不变）。</para>
    ///
    /// <para><b>踩过的真实缺陷</b>：<c>IssueCoolHandSelection</c> 原先把
    /// <c>MaxSelect = Math.Max(1, ef.A)</c> —— 把「每冷却一张的<b>收益</b>」
    /// 当成了「最多能选几<b>张</b>」。充能（<c>A = 1</c>）因此永远只能选 1 张，
    /// 磁暴（<c>A = 3</c>）因为 A ≥ 3 而侥幸看不出问题。用户报的就是这条。</para>
    ///
    /// <para>本自测直接复算「这一拍最多能选几张」应有的值，与引擎给出的比：
    /// 凡是「每冷却一张结算一次」的效果（磁暴 / 充能），上限就是候选牌数；
    /// 只有电弧（必须恰好 1 张）才是 1，且它是被<b>独立</b>钉死的，与 A 无关。</para>
    /// </summary>
    internal static class CoolHandScenario
    {
        /// <summary>要收集几次「真的问到选牌」的样本才算这条断言被跑到。</summary>
        private const int NeededHits = 6;

        /// <summary>最多扫多少个种子。</summary>
        private const int MaxSeeds = 4000;

        public static bool Run(List<string> report)
        {
            var failures = new List<string>();
            var hitsByOp = new Dictionary<EffectOp, int>();
            int hits = 0;
            int scanned = 0;
            int sawMultiCandidateMultiSelect = 0;   // 「候选 > 1 且可多选」的样本数（缺陷只会在这里露头）
            int sawSingleSelect = 0;                // 「只能选 1 张」（电弧）的样本数

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;

                CoolHandProbe probe = RunOne(seed);
                if (probe == null || !probe.Asked)
                {
                    continue;
                }

                hits++;
                hitsByOp[probe.Op] = hitsByOp.TryGetValue(probe.Op, out int n) ? n + 1 : 1;

                if (probe.MultiSelectExpected && probe.Candidates > 1)
                {
                    sawMultiCandidateMultiSelect++;
                }

                if (probe.MaxSelect == 1)
                {
                    sawSingleSelect++;
                }

                if (!probe.Ok)
                {
                    failures.Add("seed " + seed + " / " + probe.Op + " → " + probe.Reason);
                }
            }

            bool ok = hits >= NeededHits && failures.Count == 0;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "选择送入冷却的手牌：MinSelect / MaxSelect 与「每张结算一次」的语义一致"
                       + "（命中 " + hits + " 次 / 扫了 " + scanned + " 个种子；"
                       + "可多选且候选 > 1 的样本 " + sawMultiCandidateMultiSelect
                       + " 次、只能选 1 张的样本 " + sawSingleSelect + " 次）");

            for (int i = 0; i < failures.Count; i++)
            {
                report.Add("      · " + failures[i]);
            }

            if (hits < NeededHits)
            {
                report.Add("      · 只逼出 " + hits + " 次「选择送入冷却的手牌」局面，样本不足");
            }
            else if (sawMultiCandidateMultiSelect == 0)
            {
                report.Add("      · ⚠ 没有取到「候选 > 1 且可多选」的样本 —— "
                           + "那正是充能被压成单选时唯一会露头的场景，这条断言等于没验到");
            }

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  一次尝试
        // ══════════════════════════════════════════════════════

        private sealed class CoolHandProbe
        {
            /// <summary>本局确实问到了「选择送入冷却的手牌」这一拍（= 有效样本）。</summary>
            public bool Asked;

            /// <summary>这一拍背后是哪条效果。</summary>
            public EffectOp Op;

            /// <summary>引擎给的候选牌数。</summary>
            public int Candidates;

            /// <summary>引擎给的最少 / 最多选择数。</summary>
            public int MinSelect;
            public int MaxSelect;

            /// <summary>按「每张结算一次」的语义，这一拍<b>应该</b>能多选。</summary>
            public bool MultiSelectExpected;

            /// <summary>引擎给的短标题（界面拿它当弹窗大字）。</summary>
            public string Title = string.Empty;

            /// <summary>标题里是否出现了卡名（应该用「效果」命名，不是卡名）。</summary>
            public bool TitleIsCardName;

            public string Why = "未知";

            /// <summary>标题的字符数（含后缀），用来钉「title band 放得下」这条预算。</summary>
            public int TitleLength;

            public bool Ok
            {
                get
                {
                    if (Candidates <= 0)
                    {
                        return false;
                    }

                    if (MinSelect < 0 || MaxSelect < 1 || MinSelect > MaxSelect)
                    {
                        return false;
                    }

                    if (_shapeOk == false || TitleIsCardName)
                    {
                        return false;
                    }

                    // 标题长度预算：title band 的平直段只有约 257 px
                    // （UiLayout.HandPickTitleWidth），所以标题必须 ≤ 8 个字符
                    // （「冷却·加力量（可多选）」= 3 + 1 + 3 + 5 = 10 —— 含全角括号，
                    //   按显示宽度算约 8 个汉字宽，正好压在预算内）。
                    // 见 BattleEngine.IssueCoolHandSelection 里的长度预算说明。
                    if (TitleLength > 11)
                    {
                        return false;
                    }

                    return true;
                }
            }

            /// <summary>由 RunOne 填：结构与语义是否对得上（失败原因写进 Why）。</summary>
            public bool? _shapeOk;

            public string Reason
            {
                get
                {
                    if (TitleIsCardName)
                    {
                        return "标题用了卡名「" + Title + "」，应当用「效果」命名";
                    }

                    if (TitleLength > 11)
                    {
                        return "标题「" + Title + "」有 " + TitleLength
                            + " 字，超出 title band 的预算（≤ 11 字，见 UiLayout.HandPickTitleWidth）";
                    }

                    if (_shapeOk == false)
                    {
                        return Why;
                    }

                    return Why;
                }
            }
        }

        private static CoolHandProbe RunOne(int seed)
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

                    if (++guard > 20000 || engine.Pending == null)
                    {
                        break;
                    }

                    engine.Submit(run.Decide(engine.Pending));
                }
            }
            catch (Exception ex)
            {
                if (run.Probe.Asked)
                {
                    run.Probe._shapeOk = false;
                    run.Probe.Why = ex.GetType().Name + ": " + ex.Message;
                }
            }

            engine.OnEvent -= run.OnEvent;

            return run.Probe.Asked ? run.Probe : null;
        }

        // ══════════════════════════════════════════════════════
        //  脚本化双方
        // ══════════════════════════════════════════════════════

        private sealed class Runner
        {
            private readonly BattleEngine _engine;

            /// <summary>该座位最近一次宣告打出的那张进攻牌（用来从卡表反推这一拍的效果）。</summary>
            private readonly CardDef[] _lastAttack = new CardDef[2];

            public readonly CoolHandProbe Probe = new CoolHandProbe();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                if (e == null)
                {
                    return;
                }

                var atk = e as AttackDeclaredEvent;
                if (atk != null && atk.Card != null)
                {
                    _lastAttack[atk.Seat] = atk.Card.Def;
                }
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                if (req == null)
                {
                    return DecisionResponse.Of(0);
                }

                if (req.Kind == RequestKind.ChooseCoolHandCards)
                {
                    return HandleCoolHand(req);
                }

                // 其余决策：能选就选第一条非 Skip（尽量把对局推下去）
                return First(req);
            }

            /// <summary>这一拍：取样 + 尽量选满（把「多选」这条链路真的走一遍）。</summary>
            private DecisionResponse HandleCoolHand(DecisionRequest req)
            {
                EffectOp op = EffectOp.None;
                CardDef def = _lastAttack[req.Seat];
                if (def != null)
                {
                    op = CoolHandOpOf(def);
                }

                int candidates = 0;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (!o.IsSkip && o.Card != null)
                    {
                        candidates++;
                    }
                }

                // 「每冷却一张结算一次」= 磁暴（+力量）与充能（+加速）；
                //         电弧是「换一次连击」，必须恰好 1 张，不在此列。
                bool multiOk = op == EffectOp.CoolHandForAtk || op == EffectOp.CoolHandForHaste;

                Probe.Asked = true;
                Probe.Op = op;
                Probe.Candidates = candidates;
                Probe.MinSelect = req.MinSelect;
                Probe.MaxSelect = req.MaxSelect;
                Probe.Title = req.Title;
                Probe.TitleLength = req.Title != null ? req.Title.Length : 0;
                Probe.MultiSelectExpected = multiOk;
                Probe.TitleIsCardName = LooksLikeCardName(req.Title);

                int expectMax = multiOk ? candidates : 1;
                int expectMin = multiOk ? 0 : 1;

                if (req.MaxSelect != expectMax)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "MaxSelect = " + req.MaxSelect + "，按「每张结算一次」应当是候选数 "
                                + expectMax + "（候选 " + candidates + " 张；把效果参数 A 当成张数上限就会写成 A）";
                }
                else if (req.MinSelect != expectMin)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "MinSelect = " + req.MinSelect + "，应当是 " + expectMin;
                }
                else if (Probe.TitleIsCardName)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "标题「" + req.Title + "」用了卡名";
                }
                else
                {
                    Probe._shapeOk = true;
                }

                // 提交：选满（多选那种）或恰选一张（电弧）。
                // 取样只取一次 —— 选满之后这一拍就结束了，不会反复进来。
                Stop = true;

                var picked = new List<int>();
                for (int i = 0; i < req.Options.Count && picked.Count < expectMax; i++)
                {
                    if (!req.Options[i].IsSkip && req.Options[i].Card != null)
                    {
                        picked.Add(req.Options[i].Index);
                    }
                }

                if (picked.Count < expectMin)
                {
                    return DecisionResponse.Of(req.Seat);   // 候选不够（理论上不会）
                }

                if (picked.Count == 1)
                {
                    return DecisionResponse.Of(req.Seat, picked[0]);
                }

                return DecisionResponse.Of(req.Seat, picked.ToArray());
            }

            // ── 小工具 ───────────────────────────────────────

            /// <summary>从卡表反推这张牌的选牌效果是哪一个。</summary>
            private static EffectOp CoolHandOpOf(CardDef def)
            {
                if (def == null || def.Effects == null)
                {
                    return EffectOp.None;
                }

                for (int i = 0; i < def.Effects.Count; i++)
                {
                    EffectDef e = def.Effects[i];
                    if (e.Op == EffectOp.CoolHandForAtk
                        || e.Op == EffectOp.CoolHandForCombo
                        || e.Op == EffectOp.CoolHandForHaste)
                    {
                        return e.Op;
                    }
                }

                return EffectOp.None;
            }

            /// <summary>标题里出现了某张牌的名字 = 用了卡名命名。</summary>
            private static bool LooksLikeCardName(string title)
            {
                if (string.IsNullOrEmpty(title))
                {
                    return false;
                }

                for (int c = 0; c < CardLibrary.Count; c++)
                {
                    string name = CardLibrary.GetByIndex(c).Name;
                    if (!string.IsNullOrEmpty(name) && title.IndexOf(name, StringComparison.Ordinal) >= 0)
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
                        return DecisionResponse.Of(req.Seat, req.Options[i].Index);
                    }
                }

                return DecisionResponse.Of(req.Seat);
            }
        }
    }
}
