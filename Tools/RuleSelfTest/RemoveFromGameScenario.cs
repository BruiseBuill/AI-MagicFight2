using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-22 · 漩涡（<c>EffectOp.RemoveFromGame</c>）那一拍的形状自测。
    ///
    /// <para><b>为什么要专门测这一条</b>：用户把漩涡的口径改成了「从冷却区当中选、
    /// <b>强制选择一张</b>」。界面上「强制」这件事只有两个来源 ——
    /// <c>MinSelect</c> / <c>MaxSelect</c>，以及<b>有没有那条 Skip 选项</b>。
    /// 界面自己不做任何规则判断（铁律 3），所以这三个字段一旦写错，
    /// 表现就是「确认键该亮不亮」「能一张都不选」「候选里混进了手牌」，
    /// 而<b>万局统计里一个异常都不会有</b>（事件数、守恒、胜负分布全不变）。</para>
    ///
    /// <para><b>这条断言钉住的四件事</b></para>
    /// <list type="number">
    /// <item>候选<b>全部</b>来自冷却区（<c>Card.Zone == Cooling</c>）——
    /// 若有手牌混进来，玩家就能把一张好牌白白移出游戏之外（规则上是错的）。</item>
    /// <item><b>没有 Skip</b> —— 有一条「不移出（放弃）」就等于「强制」没落实。</item>
    /// <item><c>MinSelect == 1 &amp;&amp; MaxSelect == 1</c> —— 恰好一张，不再多也不再少。</item>
    /// <item>标题照旧要短、要用效果命名（title band 平直段只有 ~257 px）。</item>
    /// </list>
    ///
    /// <para>另外还反证一条：<b>冷却区空着时引擎不发这一拍</b>
    /// （<c>IssueRemoveFromGame</c> 返回 false，由 <c>StartStage1Decision</c> 继续往下走）。
    /// 实测口径是「本局问过这拍、且每次问到时冷却区都非空」；真正空冷却区的那一局
    /// 会表现为「这局从头到尾没问到这拍」，与「这局根本没打出漩涡」不可区分 ——
    /// 所以这条不做强断言，只在报告里记一笔。</para>
    /// </summary>
    internal static class RemoveFromGameScenario
    {
        /// <summary>要收集几次「真的问到漩涡选牌」的样本才算这条断言被跑到。</summary>
        private const int NeededHits = 4;

        /// <summary>最多扫多少个种子。</summary>
        private const int MaxSeeds = 4000;

        public static bool Run(List<string> report)
        {
            var failures = new List<string>();
            int hits = 0;
            int scanned = 0;
            int sawMultiCandidate = 0;      // 「冷却区里不止一张」的样本数
            int sawHandCardAsCandidate = 0; // 候选里混了手牌的次数（必须为 0）
            int sawSkipOption = 0;          // 出现 Skip 的次数（必须为 0）

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;

                RemoveProbe probe = RunOne(seed);
                if (probe == null || !probe.Asked)
                {
                    continue;
                }

                hits++;

                if (probe.Candidates > 1)
                {
                    sawMultiCandidate++;
                }

                if (probe.HandCandidates > 0)
                {
                    sawHandCardAsCandidate++;
                }

                if (probe.HasSkip)
                {
                    sawSkipOption++;
                }

                if (!probe.Ok)
                {
                    failures.Add("seed " + seed + " → " + probe.Reason);
                }
            }

            bool ok = hits >= NeededHits && failures.Count == 0;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "漩涡移出游戏：候选全在冷却区、无 Skip、MinSelect = MaxSelect = 1"
                       + "（命中 " + hits + " 次 / 扫了 " + scanned + " 个种子；"
                       + "候选 > 1 的样本 " + sawMultiCandidate + " 次；"
                       + "手牌混入候选 " + sawHandCardAsCandidate + " 次；"
                       + "出现 Skip " + sawSkipOption + " 次）");

            for (int i = 0; i < failures.Count; i++)
            {
                report.Add("      · " + failures[i]);
            }

            if (hits < NeededHits)
            {
                report.Add("      · 只逼出 " + hits + " 次「漩涡选牌」局面，样本不足"
                           + "（M31 改成强制选一张之前，这一拍出现频率更低）");
            }

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  一次尝试
        // ══════════════════════════════════════════════════════

        private sealed class RemoveProbe
        {
            /// <summary>本局确实问到了「漩涡：永久移出游戏」这一拍（= 有效样本）。</summary>
            public bool Asked;

            /// <summary>引擎给的候选牌数（不含 Skip）。</summary>
            public int Candidates;

            /// <summary>候选里落在<b>手牌</b>的个数（必须为 0）。</summary>
            public int HandCandidates;

            /// <summary>选项里是否存在 Skip（必须为 false）。</summary>
            public bool HasSkip;

            /// <summary>引擎给的最少 / 最多选择数（应当都是 1）。</summary>
            public int MinSelect;
            public int MaxSelect;

            /// <summary>引擎给的短标题（界面拿它当弹窗大字）。</summary>
            public string Title = string.Empty;

            public int TitleLength;

            /// <summary>标题里是否出现了卡名（应该用「效果」命名，不是卡名）。</summary>
            public bool TitleIsCardName;

            public string Why = "未知";

            /// <summary>由 RunOne 填：结构与语义是否对得上。</summary>
            public bool? _shapeOk;

            public bool Ok
            {
                get
                {
                    if (Candidates <= 0 || HandCandidates > 0 || HasSkip)
                    {
                        return false;
                    }

                    // 强制选一张：最少 / 最多都是 1。
                    if (MinSelect != 1 || MaxSelect != 1)
                    {
                        return false;
                    }

                    if (_shapeOk == false || TitleIsCardName)
                    {
                        return false;
                    }

                    // 标题长度预算同 M27：title band 平直段约 257 px，≤ 11 字才不缩号。
                    if (TitleLength > 11)
                    {
                        return false;
                    }

                    return true;
                }
            }

            public string Reason
            {
                get
                {
                    if (Candidates <= 0)
                    {
                        return "这一拍一张候选都没有（冷却区空）—— 引擎不该发这个决策";
                    }

                    if (HandCandidates > 0)
                    {
                        return "候选里有 " + HandCandidates + " 张是手牌 —— 漩涡只能从冷却区移出";
                    }

                    if (HasSkip)
                    {
                        return "选项里还有 Skip（「不移出（放弃）」）—— 用户要的是强制选一张";
                    }

                    if (MinSelect != 1 || MaxSelect != 1)
                    {
                        return "MinSelect / MaxSelect = " + MinSelect + " / " + MaxSelect
                               + "，强制选一张时应当都是 1";
                    }

                    if (TitleIsCardName)
                    {
                        return "标题用了卡名「" + Title + "」，应当用「效果」命名";
                    }

                    if (TitleLength > 11)
                    {
                        return "标题「" + Title + "」有 " + TitleLength
                               + " 字，超出 title band 的预算（≤ 11 字）";
                    }

                    return Why;
                }
            }
        }

        private static RemoveProbe RunOne(int seed)
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

            public readonly RemoveProbe Probe = new RemoveProbe();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                // 本次不需要事件 —— 但保留钩子，便于以后加断言。
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                if (req == null)
                {
                    return DecisionResponse.Of(0);
                }

                if (req.Kind == RequestKind.ChooseRemoveFromGame)
                {
                    return HandleRemove(req);
                }

                // 其余决策：能选就选第一条非 Skip（尽量把对局推下去）
                return First(req);
            }

            /// <summary>这一拍：取样 + 恰选一张（强制一张的链路真跑一遍）。</summary>
            private DecisionResponse HandleRemove(DecisionRequest req)
            {
                int candidates = 0;
                int handCandidates = 0;
                bool hasSkip = false;
                int firstCandidateIndex = -1;

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];

                    if (o == null)
                    {
                        continue;
                    }

                    if (o.IsSkip)
                    {
                        hasSkip = true;
                        continue;
                    }

                    if (o.Card == null)
                    {
                        continue;
                    }

                    candidates++;

                    if (o.Card.Zone != CardZone.Cooling)
                    {
                        handCandidates++;
                    }

                    if (firstCandidateIndex < 0)
                    {
                        firstCandidateIndex = o.Index;
                    }
                }

                Probe.Asked = true;
                Probe.Candidates = candidates;
                Probe.HandCandidates = handCandidates;
                Probe.HasSkip = hasSkip;
                Probe.MinSelect = req.MinSelect;
                Probe.MaxSelect = req.MaxSelect;
                Probe.Title = req.Title;
                Probe.TitleLength = req.Title != null ? req.Title.Length : 0;
                Probe.TitleIsCardName = LooksLikeCardName(req.Title);

                if (candidates <= 0)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "候选为 0";
                }
                else if (handCandidates > 0)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "候选里混进手牌 " + handCandidates + " 张";
                }
                else if (hasSkip)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "仍有 Skip 选项";
                }
                else if (req.MinSelect != 1 || req.MaxSelect != 1)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "MinSelect / MaxSelect = " + req.MinSelect + " / " + req.MaxSelect
                                + "，应当是 1 / 1（强制选一张）";
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

                // 取样只取一次 —— 选完这一拍就结束了，不会反复进来。
                Stop = true;

                if (firstCandidateIndex < 0)
                {
                    return DecisionResponse.Of(req.Seat);   // 没有候选（理论上不会）
                }

                return DecisionResponse.Of(req.Seat, firstCandidateIndex);
            }

            // ── 小工具 ───────────────────────────────────────

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
