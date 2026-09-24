using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-23 · 地震（i）改成「若这是你的最后两张手牌，减速」之后的行为自测。
    ///
    /// <para><b>为什么必须单独写一条</b>：这条改动是「同一个效果加了进门条件」——
    /// 条件判错（判反 / 判在错的时机 / 归类落到 Stage1 静默不结算）时，
    /// 万局统计全绿：手牌守恒、事件流、胜负分布都正常，唯一的表现是
    /// <b>「该减速的时候没减速」或「不该减速的时候减了」</b>，两者都无声。</para>
    ///
    /// <para><b>怎么逼出来</b>：不打乱引擎的随机发牌，只在<b>场上真的有一张能被减速的牌</b>
    /// （见 <c>AnySlowable</c> —— 否则候选表是空的，引擎压根不会问，问了也看不见）的那一拍，
    /// 把进攻方手牌<b>裁到指定张数</b>并强制打出地震，然后看第 ③ 步有没有发出减速决策：</para>
    /// <list type="bullet">
    /// <item>裁成「地震 + 1 张」→ 出牌后手上只剩 1 张 → <b>必须</b>出现
    /// <c>RequestKind.ChooseSlowTarget</c>；</item>
    /// <item>裁成「地震 + 2 张」→ 出牌后还剩 2 张 → <b>不得</b>出现（对照组）。</item>
    /// </list>
    ///
    /// <para>两条都跑通，才说明条件卡的是「最后两张」而不是「>= 1 张」之类的近似。</para>
    /// </summary>
    internal static class EarthquakeScenario
    {
        /// <summary>地震的卡 ID。</summary>
        private const string QuakeId = "i";

        /// <summary>每条要收集几次「命中」才算真的被跑到。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可成立的局面。</summary>
        private const int MaxSeeds = 1200;

        public static bool Run(List<string> report)
        {
            bool defOk = CheckCardDef(out string defWhy);
            report.Add((defOk ? "  [PASS] " : "  [FAIL] ")
                       + "地震（i）的 α 效果是「最后两张手牌才减速」的条件算子" + defWhy);

            bool holdTwo = RunCase(2, true, out CaseResult two);
            report.Add((holdTwo ? "  [PASS] " : "  [FAIL] ")
                       + "地震在手牌只剩 2 张（它是其中之一）时生效：打出后出现减速目标决策"
                       + "（命中 " + two.Hits + " 次 / 扫了 " + two.Scanned + " 个种子）");
            AppendNotes(report, two);

            bool holdMore = RunCase(3, false, out CaseResult more);
            report.Add((holdMore ? "  [PASS] " : "  [FAIL] ")
                       + "对照组：打出后手上还剩 2 张时地震不减速，不得出现减速目标决策"
                       + "（命中 " + more.Hits + " 次 / 扫了 " + more.Scanned + " 个种子）");
            AppendNotes(report, more);

            return defOk && holdTwo && holdMore;
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
        //  卡面 / 结构自检
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 卡表这一处必须钉住：算子是条件减速、参数是 1 次、卡面文字说清条件。
        ///
        /// <para><b>为什么不在这里断言「归类到 ③ 阶段」</b>：<c>EffectClassifier</c> 是
        /// <c>internal</c>，自测是另一个程序集，看不见它。归类写错的后果由上面那两条
        /// 行为断言兜住 —— 落到 Stage1 时第 ③ 步根本不会发减速决策，条件成立那条会直接失败。</para>
        /// </summary>
        private static bool CheckCardDef(out string why)
        {
            CardDef def = CardLibrary.Get(QuakeId);

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef ef = def.Effects[i];
                if (ef.Trigger != EffectTrigger.Attack || ef.Op != EffectOp.SlowIfLastTwoHand)
                {
                    continue;
                }

                if (ef.A != 1)
                {
                    why = "：次数 A 应为 1，实为 " + ef.A;
                    return false;
                }

                why = "（A = " + ef.A + "，卡面「" + ef.Text + "」）";
                return true;
            }

            why = "：没找到 α 的 SlowIfLastTwoHand 效果 —— 卡表被改回去了？";
            return false;
        }

        // ══════════════════════════════════════════════════════
        //  一次扫描
        // ══════════════════════════════════════════════════════

        private sealed class CaseResult
        {
            public int Hits;
            public int Scanned;

            /// <summary>真实违规（算样本、算命中，但要报错）。</summary>
            public readonly List<string> Failures = new List<string>();

            /// <summary>与本条规则无关的杂音（不算样本）。</summary>
            public readonly List<string> Notes = new List<string>();

            public bool Ok
            {
                get { return Hits >= NeededHits && Failures.Count == 0; }
            }
        }

        private static bool RunCase(int keep, bool expectSlow, out CaseResult result)
        {
            result = new CaseResult();

            for (int seed = 1; seed <= MaxSeeds && result.Hits < NeededHits; seed++)
            {
                result.Scanned++;

                Hit hit = RunOne(seed, keep, expectSlow);

                if (hit == null)
                {
                    result.Scanned--;
                    continue;   // 这一局没逼出「手上正好有地震 + 冷却区有牌」的时机，换种子
                }

                if (hit.Error != null || hit.Stalled)
                {
                    result.Scanned--;
                    if (result.Notes.Count < 3)
                    {
                        result.Notes.Add("seed " + seed + " → "
                                         + (hit.Error ?? "推进无进展（疑似状态机卡住）"));
                    }

                    continue;
                }

                if (!hit.Armed)
                {
                    result.Scanned--;
                    continue;   // 没真的布置成（手牌张数不够等）
                }

                result.Hits++;

                if (!hit.Ok)
                {
                    result.Failures.Add("seed " + seed + " → " + hit.Why);
                }
            }

            if (result.Hits < NeededHits)
            {
                result.Notes.Add("只逼出 " + result.Hits + " 次样本，不足以证明（检查测试设置）");
            }

            return result.Ok;
        }

        private static Hit RunOne(int seed, int keep, bool expectSlow)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine, keep, expectSlow);

            engine.OnEvent += run.OnEvent;

            // ⚠ 必须先 Start()：引擎初始停在「未开赛」的相位。
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

            return run.Hit.Error != null || run.Hit.Stalled || run.Hit.Armed ? run.Hit : null;
        }

        // ══════════════════════════════════════════════════════
        //  脚本化双方 + 事件观察
        // ══════════════════════════════════════════════════════

        private sealed class Hit
        {
            /// <summary>真的布置成局面了（裁了手牌 + 打出了地震）。</summary>
            public bool Armed;

            /// <summary>布置完那一拍之后，引擎有没有发出减速目标决策。</summary>
            public bool SlowAsked;

            /// <summary>本条对照组期望的值。</summary>
            public bool ExpectSlow;

            /// <summary>布置那一刻，出牌后（= 决策时）攻击方的手牌张数，用来复核条件口径。</summary>
            public int HandWhenAsked = -1;

            public bool Stalled;
            public string Error;

            public bool Ok
            {
                get { return Armed && SlowAsked == ExpectSlow; }
            }

            public string Why
            {
                get
                {
                    if (ExpectSlow && !SlowAsked)
                    {
                        return "手牌只剩 2 张（含地震）却没问减速目标 —— 条件没生效（或归类落错了阶段）";
                    }

                    if (!ExpectSlow && SlowAsked)
                    {
                        return "手牌还剩 2 张以上也问了减速目标 —— 条件判宽了（判成「出牌后 <= 1 张」以外的口径）";
                    }

                    return "未知";
                }
            }
        }

        private sealed class Runner
        {
            private readonly BattleEngine _engine;
            private readonly int _keep;
            private readonly bool _expectSlow;

            /// <summary>已经布置过了（只布置一次）。</summary>
            private bool _armed;

            /// <summary>布置的那一拍已经打进第 ① 步之后（开始盯第 ③ 步）。</summary>
            private bool _watching;

            /// <summary>那一拍的防御已经结算完 —— 第 ③ 步的决策要么已经来了，要么不会来。</summary>
            private bool _attackDone;

            public readonly Hit Hit = new Hit();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine, int keep, bool expectSlow)
            {
                _engine = engine;
                _keep = keep;
                _expectSlow = expectSlow;
                Hit.ExpectSlow = expectSlow;
            }

            public void OnEvent(BattleEvent e)
            {
                if (e is AttackDeclaredEvent && _armed)
                {
                    _watching = true;
                }

                if (e is DefenseResolvedEvent && _watching)
                {
                    _attackDone = true;
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
                        if (_attackDone)
                        {
                            // 下半场又轮到进攻 = 第 ③ 步已经走完，收摊
                            Stop = true;
                            return First(req);
                        }

                        return DecideAttack(req);

                    default:
                        if (_attackDone)
                        {
                            // 防御结算之后的第一个决策请求 —— 是减速就是它，不是就说明没减速
                            if (req.Kind == RequestKind.ChooseSlowTarget)
                            {
                                Hit.SlowAsked = true;
                            }

                            Stop = true;
                        }

                        return First(req);
                }
            }

            /// <summary>
            /// 挑一次进攻机会布置局面：手牌够、含地震、且场上有能被减速的牌
            /// （否则减速候选表是空的）。
            /// 布置 = 手牌裁到 <c>_keep</c> 张（地震必留） + 强制打出地震。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_armed)
                {
                    return First(req);
                }

                PlayerState attacker = _engine.State.Of(req.Seat);

                if (attacker.Hand.Count < _keep || !AnySlowable())
                {
                    return First(req);
                }

                Option quake = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Card != null && o.Card.Def.Id == QuakeId)
                    {
                        quake = o;
                        break;
                    }
                }

                if (quake == null)
                {
                    return First(req);   // 手上没地震 → 换种子
                }

                TrimHand(attacker, _keep);

                _armed = true;
                Hit.Armed = true;
                Hit.HandWhenAsked = attacker.Hand.Count;

                return DecisionResponse.Of(req.Seat, quake.Index);
            }

            /// <summary>把手牌裁到 <paramref name="keep"/> 张，地震必留（它就是被测的那张）。</summary>
            private static void TrimHand(PlayerState player, int keep)
            {
                var kept = new List<CardInstance>();

                CardInstance quake = null;
                for (int i = 0; i < player.Hand.Count; i++)
                {
                    if (player.Hand[i].Def.Id == QuakeId)
                    {
                        quake = player.Hand[i];
                        break;
                    }
                }

                if (quake == null)
                {
                    return;
                }

                kept.Add(quake);

                for (int i = 0; i < player.Hand.Count && kept.Count < keep; i++)
                {
                    if (player.Hand[i] != quake)
                    {
                        kept.Add(player.Hand[i]);
                    }
                }

                player.Hand.Clear();
                player.Hand.AddRange(kept);
            }

            /// <summary>
            /// 任意一方的冷却区里有没有<strong>能被减速</strong>的牌 —— 第 ③ 步的减速目标才有得列。
            ///
            /// <para><b>⚠ 2026-09-23 由「有牌就行」收紧成「有能减速的牌」</b>：引擎生成加速 / 减速候选时
            /// 会把<b>剩余冷却已达基础值</b>的牌过滤掉（`BattleEngine.IssueStage3Repeat` ——
            /// 那些牌列出来也只能是「点了没反应」，属于「这一拍根本做不到的事」）。
            /// 于是「冷却区里只剩顶到上限的牌」这种局面下，引擎<b>不会</b>发出减速决策，
            /// 本条断言就会误报成「条件没生效」。</para>
            ///
            /// <para>收紧后它仍然是有效的断言：只要场上存在一张真的减得动的牌，
            /// 条件成立时<b>必然</b>要出现减速决策 —— 「条件判反 / 判在错时机 / 归类落错阶段」
            /// 这三种缺陷照样会被抓住。</para>
            /// </summary>
            private bool AnySlowable()
            {
                for (int seat = 0; seat < _engine.State.Players.Count; seat++)
                {
                    List<CardInstance> zone = _engine.State.Of(seat).CoolingZone;
                    for (int i = 0; i < zone.Count; i++)
                    {
                        if (zone[i].RemainingCooldown < zone[i].Def.Cooldown)
                        {
                            return true;
                        }
                    }
                }

                return false;
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
