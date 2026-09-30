using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-22 · 漩涡（<c>EffectOp.RemoveFromGame</c>）那一拍的形状自测。
    ///
    /// <para><b>为什么要专门测这一条</b>：漩涡的口径改过两次 —— M31（2026-09-22）是
    /// 「从冷却区当中选、<b>强制选择一张</b>」，2026-09-28 按卡面字面意思
    /// （「<b>可</b>将冷却区中的一张法术永久移出游戏」）改回「<b>可以一张都不移出</b>」
    /// （不移出 = 也拿不到加速）。
    /// 界面上「选几张」这件事只有两个来源 ——
    /// <c>MinSelect</c> / <c>MaxSelect</c>，以及<b>有没有那条 Skip 选项</b>。
    /// 界面自己不做任何规则判断（铁律 3），所以这几个字段一旦写错，
    /// 表现就是「确认键该亮不亮」「能一张都不选」「候选里混进了手牌」，
    /// 而<b>万局统计里一个异常都不会有</b>（事件数、守恒、胜负分布全不变）。</para>
    ///
    /// <para><b>这条断言钉住的四件事</b></para>
    /// <list type="number">
    /// <item>候选<b>全部</b>来自冷却区（<c>Card.Zone == Cooling</c>）——
    /// 若有手牌混进来，玩家就能把一张好牌白白移出游戏之外（规则上是错的）。</item>
    /// <item><b>没有 Skip</b> —— 「可以空着确认」这条只由 <c>MinSelect = 0</c> 表达；
    /// 再摆一条「跳过」会多出一个语义重复的按钮（口径同磁暴 / 充能）。</item>
    /// <item><c>MinSelect == 0 &amp;&amp; MaxSelect == 1</c> —— 最多一张，但可以一张都不选。</item>
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
            int sawEmptySubmit = 0;         // 「一张不选、直接确认」的样本数（2026-09-28 起）

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;

                // ⚠ 交替两种提交：偶数次命中「空提交（一张不移出）」、奇数次「选第一张」。
                //   空提交这一半才是 2026-09-28 改动的要害 ——
                //   只断言决策形状（MinSelect=0）不够，得让引擎真收下一次空数组。
                bool submitEmpty = hits % 2 == 0;

                RemoveProbe probe = RunOne(seed, submitEmpty);
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

                if (probe.EmptyMode)
                {
                    sawEmptySubmit++;
                }

                if (!probe.Ok)
                {
                    failures.Add("seed " + seed + " → " + probe.Reason);
                }
            }

            bool ok = hits >= NeededHits && failures.Count == 0
                      && sawEmptySubmit > 0 && sawEmptySubmit < hits;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "漩涡移出游戏：候选全在冷却区、无 Skip、MinSelect = 0 / MaxSelect = 1；"
                       + "「一张不选直接确认」引擎真收下且牌没被移出"
                       + "（命中 " + hits + " 次 / 扫了 " + scanned + " 个种子；"
                       + "候选 > 1 的样本 " + sawMultiCandidate + " 次；"
                       + "手牌混入候选 " + sawHandCardAsCandidate + " 次；"
                       + "出现 Skip " + sawSkipOption + " 次；"
                       + "空提交 " + sawEmptySubmit + " / 选一张 " + (hits - sawEmptySubmit) + "）");

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

            /// <summary>引擎给的最少 / 最多选择数（应当是 0 / 1 —— 可选一张）。</summary>
            public int MinSelect;
            public int MaxSelect;

            /// <summary>引擎给的短标题（界面拿它当弹窗大字）。</summary>
            public string Title = string.Empty;

            public int TitleLength;

            /// <summary>标题里是否出现了卡名（应该用「效果」命名，不是卡名）。</summary>
            public bool TitleIsCardName;

            /// <summary>这一局是「空提交」模式吗（一张都不选、直接确认）。</summary>
            public bool EmptyMode;

            /// <summary>
            /// 空提交之后：被取样那张牌是否<b>仍然躺在冷却区里</b>（= 引擎真的没移出）。
            /// 非空提交模式恒 true。
            /// </summary>
            public bool EmptyOk = true;

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

                    // 可选：最多一张、可以一张都不选（MinSelect = 0）。
                    if (MinSelect != 0 || MaxSelect != 1)
                    {
                        return false;
                    }

                    if (_shapeOk == false || TitleIsCardName)
                    {
                        return false;
                    }

                    // 「一张不选」提交后牌必须还在冷却区（2026-09-28 改动的要害）。
                    if (EmptyMode && !EmptyOk)
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
                        return "选项里还有 Skip（「不移出（放弃）」）—— 「可以空着确认」只该由 MinSelect = 0 表达";
                    }

                    if (MinSelect != 0 || MaxSelect != 1)
                    {
                        return "MinSelect / MaxSelect = " + MinSelect + " / " + MaxSelect
                               + "，可选一张时应当是 0 / 1（可以不移出）";
                    }

                    if (TitleIsCardName)
                    {
                        return "标题用了卡名「" + Title + "」，应当用「效果」命名";
                    }

                    if (EmptyMode && !EmptyOk)
                    {
                        return "空提交（一张不选直接确认）之后，那张牌不在冷却区里了 —— 引擎把它移出了";
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

        private static RemoveProbe RunOne(int seed, bool submitEmpty)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine, submitEmpty);

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

                    DecisionRequest pending = engine.Pending;
                    engine.Submit(run.Decide(pending));

                    // 「一张不选直接确认」的那一半：提交完之后要复核
                    //   ① 引擎真的收下了（Pending 已经翻页，而不是被 EffectWindow 拒掉）；
                    //   ② 那张候选牌还在冷却区里（= 真没移出）。
                    run.VerifyEmptySubmit(pending, engine.Pending);
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

            /// <summary>本局是不是走「空提交」那一半。</summary>
            private readonly bool _submitEmpty;

            /// <summary>待复核的空提交：座位 / 被取样的那张牌（−1 / null = 没有待复核）。</summary>
            private int _pendingEmptySeat = -1;
            private CardInstance _pendingEmptyCard;

            public Runner(BattleEngine engine, bool submitEmpty)
            {
                _engine = engine;
                _submitEmpty = submitEmpty;
            }

            /// <summary>
            /// 空提交之后立刻复核：引擎接受了空数组（`Pending` 已翻页，而不是被 EffectWindow 拒掉），
            /// 而且那张候选牌<b>还在原来的冷却区里</b>（= 真没移出）。
            /// </summary>
            public void VerifyEmptySubmit(DecisionRequest before, DecisionRequest after)
            {
                if (_pendingEmptySeat < 0)
                {
                    return;
                }

                int seat = _pendingEmptySeat;
                CardInstance card = _pendingEmptyCard;
                _pendingEmptySeat = -1;
                _pendingEmptyCard = null;

                // ⚠ BattleEngine.Submit 返回 void：EffectWindow 拒掉时**静默 return**，
                //   唯一的信号是 Pending 仍指向同一个对象。
                if (ReferenceEquals(before, after))
                {
                    Probe.EmptyOk = false;
                    Probe._shapeOk = false;
                    Probe.Why = "空提交被引擎拒掉了（Pending 没有翻页）";
                    return;
                }

                PlayerState owner = _engine.State.Of(seat);
                bool stillThere = card != null && owner.CoolingZone.Contains(card);
                Probe.EmptyOk = stillThere;
                if (!stillThere)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "空提交后那张牌已不在冷却区（引擎应当不移出）";
                }
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

            /// <summary>这一拍：取样；然后按本局的模式<b>选一张</b>或<b>一张不选</b>。</summary>
            private DecisionResponse HandleRemove(DecisionRequest req)
            {
                int candidates = 0;
                int handCandidates = 0;
                bool hasSkip = false;
                int firstCandidateIndex = -1;
                CardInstance firstCandidateCard = null;

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
                        firstCandidateCard = o.Card;
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
                Probe.EmptyMode = _submitEmpty;

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
                else if (req.MinSelect != 0 || req.MaxSelect != 1)
                {
                    Probe._shapeOk = false;
                    Probe.Why = "MinSelect / MaxSelect = " + req.MinSelect + " / " + req.MaxSelect
                                + "，应当是 0 / 1（可选一张：可以不移出）";
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

                if (_submitEmpty)
                {
                    // 「一张都不移出，直接确认」= 回一个<b>没有任何序号</b>的响应。
                    // ⚠ 这里刻意不返回 Skip 选项的序号 —— 界面上根本没有 Skip 可点，
                    //   玩家点确认时回填的就是空数组（`HandPickView.OnConfirmClicked`）。
                    _pendingEmptySeat = req.Seat;
                    _pendingEmptyCard = firstCandidateCard;
                    return DecisionResponse.Of(req.Seat);
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
