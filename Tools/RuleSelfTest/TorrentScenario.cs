using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-23 · 瀑流（y）「同一张卡不能被加速第 2 次」的行为自测。
    ///
    /// <para><b>为什么必须单独写一条</b>：这条规则涉及的是「③ 阶段候选表的<b>生存期</b>」——
    /// 旧实现每处理一个效果就清空排除名单，于是瀑流的第 2 条 α
    /// （「每损失 1 点生命加速一个<em>不同</em>的法术」）里面确实互斥，
    /// 但第 1 条 α 已经加速过的那张牌，在第 2 条 α 里又回到了候选里。
    /// 这类「规则只生效一半」的缺陷在万局统计里<b>完全无声</b>：
    /// 手牌守恒、事件条数、胜负分布全都正常，唯一的表现是「同一张牌被加速了两次」。</para>
    ///
    /// <para><b>怎么逼出来</b>：只在合理的局面上插手 —— 某一方手上正好有瀑流、
    /// 双方冷却区合计 ≥ 2 张牌、且他<b>已损失 ≥ 2 点生命</b>（第 2 条 α 至少问 2 次）、
    /// 还存在一张「加速一次之后仍留在冷却区」的牌（剩余 ≥ 2）时，强制打出瀑流，
    /// 并把那一张当第一目标。之后每次都<b>故意挑候选表里的第一张</b>
    /// —— 也就是最容易撞上重复的那种打法。</para>
    ///
    /// <para><b>断言不是「结果里没有重复」而是「候选表逐位对得上」</b>：
    /// 结果层的重复可能因为某张牌恰好回手而<b>侥幸不发生</b>；候选表则能直接判出
    /// 「引擎有没有把已经加速过的牌重新列出来」。所以对每一次请求都比对
    /// 「候选 uid 集合 = 该侧冷却区 uid 集合 − 本串已经加速过的那几张」。</para>
    ///
    /// <para><b>对照组（湍流 ab，加速 ×2）同样重要</b>：算子表写明
    /// 「<c>Haste</c> 的 A 次<b>可分配给同一张或不同张</b>」（`engineering/04-架构与接口.md`）。
    /// 所以对照组断言的是<b>相反</b>的一件事 —— 候选表<b>不得</b>被过滤：
    /// 第一次加速过的那张牌，只要还在冷却区里，就必须重新出现。少了这一条，
    /// 「把所有加速都改成不能重复」这种改过头也能全绿通过。</para>
    /// </summary>
    internal static class TorrentScenario
    {
        /// <summary>瀑流。</summary>
        private const string TorrentId = "y";

        /// <summary>湍流 —— 对照组：加速 ×2，允许落在同一张。</summary>
        private const string TurbulenceId = "ab";

        /// <summary>每条要收集几次「可成立的样本」才算真的被跑到。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可成立的局面。</summary>
        private const int MaxSeeds = 2000;

        public static bool Run(List<string> report)
        {
            bool tableOk = CheckCardTable(out string tableWhy);
            report.Add((tableOk ? "  [PASS] " : "  [FAIL] ")
                       + "卡表：只有瀑流（y）带「你每损失 1 点生命值，加速一个不同的法术」这条算子"
                       + tableWhy);

            bool mustDiffer = RunCase(true, out CaseResult torrent);
            report.Add((mustDiffer ? "  [PASS] " : "  [FAIL] ")
                       + "瀑流：整张牌内加速目标互不相同（第 1 条 α 加速过的那张，"
                       + "在第 2 条 α 的候选表里不再出现）"
                       + "（命中 " + torrent.Hits + " 次 / 扫了 " + torrent.Scanned + " 个种子）");
            AppendNotes(report, torrent);

            bool repeatable = RunCase(false, out CaseResult turb);
            report.Add((repeatable ? "  [PASS] " : "  [FAIL] ")
                       + "对照组 · 湍流（ab）：同一张牌仍可被加速第二次（候选表不得被过滤）"
                       + "（命中 " + turb.Hits + " 次 / 扫了 " + turb.Scanned + " 个种子）");
            AppendNotes(report, turb);

            return tableOk && mustDiffer && repeatable;
        }

        private static void AppendNotes(List<string> report, CaseResult r)
        {
            for (int i = 0; i < r.Failures.Count; i++)
            {
                report.Add("      · " + r.Failures[i]);
            }

            for (int i = 0; i < r.Notes.Count; i++)
            {
                report.Add("      · 杂音：" + r.Notes[i]);
            }
        }

        // ══════════════════════════════════════════════════════
        //  卡表自检
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 钉住「只有瀑流需要不同目标」这件事。
        ///
        /// <para><b>为什么这条静态检查值得写</b>：引擎只在「这次进攻的 ③ 阶段队列里
        /// 出现过 <c>HastePerHpLoss</c>」时才要求加速目标互不相同
        /// （见 <c>BattleEngine.HasDistinctHasteEffect</c>）。哪天有人又给别的牌加上这条算子，
        /// 那条牌也会跟着变成「必须不同张」—— 这未必是错的，但必须是<b>有意</b>的，
        /// 所以在这里显式列一份名单。</para>
        /// </summary>
        private static bool CheckCardTable(out string why)
        {
            var withDistinct = new List<string>();

            IReadOnlyList<CardDef> all = CardLibrary.All;
            for (int i = 0; i < all.Count; i++)
            {
                CardDef def = all[i];
                for (int e = 0; e < def.Effects.Count; e++)
                {
                    if (def.Effects[e].Op == EffectOp.HastePerHpLoss)
                    {
                        withDistinct.Add(def.Id);
                        break;
                    }
                }
            }

            if (withDistinct.Count != 1 || withDistinct[0] != TorrentId)
            {
                why = "：带该算子的牌是 [" + string.Join(",", withDistinct.ToArray())
                      + "]，期望只有 [" + TorrentId + "] —— 新加了的话请显式更新本断言";
                return false;
            }

            CardDef torrent = CardLibrary.Get(TorrentId);
            var ops = new List<string>();
            for (int i = 0; i < torrent.Effects.Count; i++)
            {
                ops.Add(torrent.Effects[i].Op.ToString());
            }

            why = "（" + TorrentId + " 的 α 队列 = " + string.Join(" + ", ops.ToArray()) + "）";
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  一次扫描
        // ══════════════════════════════════════════════════════

        private sealed class CaseResult
        {
            public int Hits;
            public int Scanned;

            /// <summary>这一局的观察有没有发生（没发生的按原因分桶，用来解释「为什么一个样本都没采到」）。</summary>
            public readonly Dictionary<string, int> Rejects = new Dictionary<string, int>();

            /// <summary>真实违规（算样本、算命中，但要报错）。</summary>
            public readonly List<string> Failures = new List<string>();

            /// <summary>与本条规则无关的杂音（不算样本）。</summary>
            public readonly List<string> Notes = new List<string>();

            public bool Ok
            {
                get { return Hits >= NeededHits && Failures.Count == 0; }
            }

            public void Reject(string reason)
            {
                int n;
                Rejects.TryGetValue(reason, out n);
                Rejects[reason] = n + 1;
            }

            public string RejectSummary()
            {
                if (Rejects.Count == 0)
                {
                    return "（没有可统计的局）";
                }

                var parts = new List<string>();
                foreach (KeyValuePair<string, int> pair in Rejects)
                {
                    parts.Add(pair.Key + " " + pair.Value);
                }

                return string.Join(" · ", parts.ToArray());
            }
        }

        private static bool RunCase(bool mustDiffer, out CaseResult result)
        {
            result = new CaseResult();
            string cardId = mustDiffer ? TorrentId : TurbulenceId;

            for (int seed = 1; seed <= MaxSeeds && result.Hits < NeededHits; seed++)
            {
                result.Scanned++;

                Sample s = RunOne(seed, cardId, mustDiffer);

                if (s.Error != null)
                {
                    if (result.Notes.Count < 3)
                    {
                        result.Notes.Add("seed " + seed + " → " + s.Error);
                    }

                    result.Reject("推进异常");
                    continue;
                }

                if (!s.Armed)
                {
                    result.Reject(s.Reject ?? "没布置成（原因未知）");
                    continue;   // 这一局没能布置成「可证明」的局面，换种子
                }

                if (!s.HasEvidence)
                {
                    result.Reject("布置成了但没观察到加速串");
                    continue;
                }

                result.Hits++;

                string why;
                if (!s.Validate(mustDiffer, out why))
                {
                    result.Failures.Add("seed " + seed + " → " + why);
                }
            }

            if (result.Hits < NeededHits)
            {
                result.Notes.Add("只逼出 " + result.Hits + " 次样本，不足以证明（检查测试设置）");
            }

            result.Notes.Add("被拒局面：" + result.RejectSummary());

            return result.Ok;
        }

        private static Sample RunOne(int seed, string cardId, bool mustDiffer)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine, cardId, mustDiffer);

            engine.OnEvent += run.OnEvent;
            engine.Start();

            int guard = 0;

            try
            {
                // ⚠ Advance() 内部会一直 Step 到「有 Pending 或已终局」为止 ——
                //   所以这里读到的 Pending 必然非空（除非终局）。
                while (!engine.IsOver && !run.Stop)
                {
                    engine.Advance();

                    if (engine.IsOver)
                    {
                        break;
                    }

                    if (++guard > 20000)
                    {
                        run.Sample.Error = "推进次数超限（疑似状态机卡住）";
                        break;
                    }

                    if (engine.Pending == null)
                    {
                        run.Sample.Error = "Advance 之后仍然没有待决策";
                        break;
                    }

                    engine.Submit(run.Decide(engine.Pending));
                }

                run.CloseHasteRun();
            }
            catch (Exception ex)
            {
                run.Sample.Error = ex.GetType().Name + ": " + ex.Message;
            }

            engine.OnEvent -= run.OnEvent;
            return run.Sample;
        }

        // ══════════════════════════════════════════════════════
        //  样本
        // ══════════════════════════════════════════════════════

        /// <summary>一次「加速串」的观察记录。</summary>
        private sealed class Sample
        {
            /// <summary>真的布置成局面了（打出了目标牌）。</summary>
            public bool Armed;

            /// <summary>布置那一刻进攻方已损失的生命（瀑流第 2 条 α 的次数来源）。</summary>
            public int HpLostAtPlay = -1;

            /// <summary>本串每一次请求的候选 uid（不含 Skip）。</summary>
            public readonly List<List<int>> Offers = new List<List<int>>();

            /// <summary>本串每一次请求发起时，双方冷却区的 uid 全集。</summary>
            public readonly List<List<int>> Cooling = new List<List<int>>();

            /// <summary>本串实际提交的加速目标 uid（按先后）。</summary>
            public readonly List<int> Picked = new List<int>();

            /// <summary>候选表里出现过「本串已经加速过的那张牌」的次数（对照组要 &gt; 0）。</summary>
            public int RepeatsOffered;

            /// <summary>没布置成的原因（给 <see cref="CaseResult.RejectSummary"/> 分桶用）。</summary>
            public string Reject;

            public string Error;

            /// <summary>这一局的观察是否足以证明规则（见 <see cref="RunCase"/>）。</summary>
            public bool HasEvidence
            {
                get { return Offers.Count >= 2; }
            }

            private static List<int> Except(List<int> from, List<int> remove)
            {
                var kept = new List<int>();
                for (int i = 0; i < from.Count; i++)
                {
                    if (!remove.Contains(from[i]))
                    {
                        kept.Add(from[i]);
                    }
                }

                return kept;
            }

            private static string Join(List<int> list)
            {
                var parts = new List<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    parts.Add(list[i].ToString());
                }

                return string.Join(",", parts.ToArray());
            }

            /// <summary>
            /// 逐次请求比对「候选表 == 期望表」。
            ///
            /// <list type="bullet">
            /// <item><b>瀑流</b>：期望 = 该次请求时双方冷却区 uid 全集 <b>减去</b>本串已经加速成功的那几张
            /// —— 多出一张 = 旧的「只互斥一半」缺陷；少一张 = 过滤过头。</item>
            /// <item><b>对照组（湍流）</b>：期望 = 冷却区全集本身，<b>一张都不该被抹掉</b>
            /// —— 少一张就说明「可重复加速」这条规则被我这次改动误伤。</item>
            /// </list>
            /// </summary>
            public bool Validate(bool mustDiffer, out string why)
            {
                why = null;

                for (int i = 0; i < Offers.Count; i++)
                {
                    var expected = new List<int>(Cooling[i]);

                    if (mustDiffer)
                    {
                        var done = new List<int>();
                        for (int k = 0; k < i && k < Picked.Count; k++)
                        {
                            done.Add(Picked[k]);
                        }

                        expected = Except(expected, done);
                    }

                    var extra = new List<int>();
                    for (int k = 0; k < Offers[i].Count; k++)
                    {
                        if (!expected.Contains(Offers[i][k]))
                        {
                            extra.Add(Offers[i][k]);
                        }
                    }

                    var missing = new List<int>();
                    for (int k = 0; k < expected.Count; k++)
                    {
                        if (!Offers[i].Contains(expected[k]))
                        {
                            missing.Add(expected[k]);
                        }
                    }

                    if (extra.Count == 0 && missing.Count == 0)
                    {
                        continue;
                    }

                    string head = "第 " + (i + 1) + " 次选目标（该次冷却区 " + Cooling[i].Count
                                  + " 张、已加速 " + Math.Min(i, Picked.Count) + " 张）";

                    if (extra.Count > 0)
                    {
                        why = head + " 的候选里多出了 uid [" + Join(extra) + "]"
                              + (mustDiffer ? " —— 它已经被这张瀑流加速过一次，不该再出现" : " —— 不该多出冷却区里没有的牌");
                    }
                    else
                    {
                        why = head + " 的候选里少了 uid [" + Join(missing) + "]"
                              + (mustDiffer ? " —— 过滤过头了（它并没有被本串加速过）"
                                            : " —— 同一张牌本来可以被加速第二次（湍流允许重复），却不见了");
                    }

                    return false;
                }

                // 结果层再兜一道：本串提交过的目标里不得出现重复（瀑流）。
                if (mustDiffer)
                {
                    for (int i = 1; i < Picked.Count; i++)
                    {
                        for (int k = 0; k < i; k++)
                        {
                            if (Picked[i] == Picked[k])
                            {
                                why = "同一次瀑流里 uid " + Picked[i] + " 被加速了两次（第 "
                                      + (k + 1) + " 次与第 " + (i + 1) + " 次）";
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
        }

        // ══════════════════════════════════════════════════════
        //  脚本化双方
        // ══════════════════════════════════════════════════════

        private sealed class Runner
        {
            private readonly BattleEngine _engine;
            private readonly string _cardId;
            private readonly bool _mustDiffer;

            /// <summary>已经布置过（只布置一次）。</summary>
            private bool _armed;

            /// <summary>布置的那张牌已经打出 → 正在观察它的加速串。</summary>
            private bool _inRun;

            /// <summary>
            /// 已经收到过本串的第一次「选加速目标」请求。
            ///
            /// <para><b>为什么不能用 <see cref="_inRun"/> 当「串开始了」</b>：攻击牌一打出，
            /// 紧接着来的是<b>防御</b>决策（② 阶段），加速决策在它<b>之后</b>（③ 阶段）。
            /// 拿 <c>_inRun</c> 判断「这一拍不是选加速目标 = 串结束」的话，
            /// 防御那一拍就会把观察整个掐断（实测：2000 个种子一个样本都没采到）。</para>
            /// </summary>
            private bool _runStarted;

            public readonly Sample Sample = new Sample();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine, string cardId, bool mustDiffer)
            {
                _engine = engine;
                _cardId = cardId;
                _mustDiffer = mustDiffer;
            }

            public void OnEvent(BattleEvent e)
            {
                // 打出的牌不是我们要观察的那张 → 观察结束（后面的请求不属于本串）
                if (e is AttackDeclaredEvent && _armed && !_inRun)
                {
                    var ad = (AttackDeclaredEvent)e;
                    if (ad.Card != null && ad.Card.Def.Id == _cardId)
                    {
                        _inRun = true;
                    }
                }
            }

            /// <summary>串结束（下一条请求不是选加速目标 / 对局结束）时收尾一次。</summary>
            public void CloseHasteRun()
            {
                _inRun = false;
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        return DecisionResponse.Of(req.Seat);   // 一次都不换，保住手牌

                    case RequestKind.ChooseAttackCard:
                        return DecideAttack(req);

                    case RequestKind.ChooseHasteTarget:
                        if (_inRun)
                        {
                            return ObserveHaste(req);
                        }

                        return First(req);

                    default:
                        // ⚠ 只有「串已经真的开始过」才谈得上结束 —— 见 _runStarted 的说明。
                        if (_runStarted)
                        {
                            Stop = true;
                        }

                        return First(req);
                }
            }

            /// <summary>
            /// 挑一次进攻机会布置局面。条件（全部满足才动手）：
            /// 手上恰好有目标牌 · 双方冷却区合计 ≥ 2 张 · 已损失 ≥ 2 点生命（瀑流需要）
            /// · 存在一张剩余 ≥ 2、加速一次之后仍留在冷却区里的牌（当第一目标）。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_armed)
                {
                    return First(req);
                }

                PlayerState attacker = _engine.State.Of(req.Seat);

                Option play = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Card != null && o.Card.Def.Id == _cardId)
                    {
                        play = o;
                        break;
                    }
                }

                if (play == null)
                {
                    Sample.Reject = "手上没有 " + _cardId;
                    return First(req);
                }

                if (!AnySurvivingAnchor(req.Seat))
                {
                    Sample.Reject = "冷却区不足 2 张或没有剩余 ≥2 的牌";
                    return First(req);
                }

                if (_mustDiffer && attacker.HpLost < 2)
                {
                    Sample.Reject = "已损失生命 < 2";
                    return First(req);   // 第 2 条 α 至少问 2 次才看得出「不同目标」
                }

                _armed = true;
                Sample.Armed = true;
                Sample.Reject = null;
                Sample.HpLostAtPlay = attacker.HpLost;

                return DecisionResponse.Of(req.Seat, play.Index);
            }

            /// <summary>
            /// 双方冷却区合计 ≥ 2 张，且存在一张剩余 ≥ 2 的牌
            /// （加速一次之后它还剩 ≥ 1，仍然留在冷却区 —— 这才看得出「能不能被重复加速」）。
            /// </summary>
            private bool AnySurvivingAnchor(int attackerSeat)
            {
                int total = 0;
                bool anchor = false;

                for (int seat = 0; seat < _engine.State.Players.Count; seat++)
                {
                    List<CardInstance> zone = _engine.State.Of(seat).CoolingZone;
                    total += zone.Count;

                    for (int i = 0; i < zone.Count; i++)
                    {
                        if (zone[i].RemainingCooldown >= 2)
                        {
                            anchor = true;
                        }
                    }
                }

                return total >= 2 && anchor && attackerSeat >= 0;
            }

            /// <summary>记一次请求，并把「第一目标」优先钉成那张加一次也还在冷却区的牌。</summary>
            private DecisionResponse ObserveHaste(DecisionRequest req)
            {
                var offered = new List<int>();
                var cooling = new List<int>();

                for (int seat = 0; seat < _engine.State.Players.Count; seat++)
                {
                    List<CardInstance> zone = _engine.State.Of(seat).CoolingZone;
                    for (int i = 0; i < zone.Count; i++)
                    {
                        cooling.Add(zone[i].Uid);
                    }
                }

                Option pick = null;

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.Card == null)
                    {
                        continue;
                    }

                    offered.Add(o.Card.Uid);

                    // 第一次（本串）优先挑「加一次之后还留在冷却区」的那张 —— 这样
                    // 「能不能被重复加速」才在后面的候选表里看得见。
                    if (pick == null && (Sample.Picked.Count > 0 || o.Card.RemainingCooldown >= 2))
                    {
                        pick = o;
                    }
                }

                // 候选里出现过「本串已经加速过的那张牌」→ 对照组要有这个证据
                for (int i = 0; i < offered.Count; i++)
                {
                    if (Sample.Picked.Contains(offered[i]))
                    {
                        Sample.RepeatsOffered++;
                    }
                }

                Sample.Offers.Add(offered);
                Sample.Cooling.Add(cooling);
                _runStarted = true;

                if (pick == null)
                {
                    // 一个目标都没有（只剩 Skip）→ 这一串提前结束，交回默认分支
                    Stop = true;
                    return First(req);
                }

                Sample.Picked.Add(pick.Card.Uid);
                return DecisionResponse.Of(req.Seat, pick.Index);
            }

            private static DecisionResponse First(DecisionRequest req)
            {
                if (req.Options != null && req.Options.Count > 0)
                {
                    return DecisionResponse.Of(req.Seat, req.Options[0].Index);
                }

                return DecisionResponse.Of(req.Seat);
            }
        }
    }
}
