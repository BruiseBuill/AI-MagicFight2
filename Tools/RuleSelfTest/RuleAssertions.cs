using System;
using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// M11 · 规则断言集。分三层：
    /// <list type="number">
    /// <item><b>数据类</b>：卡表本身的硬约束（力量分布等），确定性最强。</item>
    /// <item><b>不变量类</b>：万局随机对局中，任何时刻都必须成立的全局不变量。</item>
    /// <item><b>规则类</b>：从决策请求 / 事件流反查具体规则有没有被守住。</item>
    /// </list>
    /// </summary>
    public static class RuleAssertions
    {
        // ══════════════════════════════════════════════════════
        //  1) 数据类断言
        // ══════════════════════════════════════════════════════

        public static bool RunDataAssertions(List<string> report)
        {
            bool ok = true;
            report.Add("── 数据类断言 ──");

            ok &= Check(report, CardLibrary.Count == 40, "卡表共 40 张（实为 " + CardLibrary.Count + "）");

            string powerReport;
            bool powerOk = CardLibrary.ValidatePowerDistribution(out powerReport);
            ok &= Check(report, powerOk, powerReport);

            string cdReport;
            bool cdOk = CardLibrary.ValidateCooldownDistribution(out cdReport);
            ok &= Check(report, cdOk, cdReport);

            // 2026-09-20：光环的触发符号必须写对且同符号 —— 第 ⑤ 步靠它决定点亮哪几枚。
            string auraReport;
            bool auraOk = CardLibrary.ValidateAuraTriggers(out auraReport);
            ok &= Check(report, auraOk, auraReport);

            // 每张卡至少 1 条效果；ID 唯一且连续；模仿卡面显示 X
            var ids = new List<string>();
            bool everyHasEffect = true;
            bool idsOk = true;
            bool mimicOk = true;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef d = CardLibrary.GetByIndex(i);
                if (d.Effects.Count == 0)
                {
                    everyHasEffect = false;
                }

                if (ids.Contains(d.Id))
                {
                    idsOk = false;
                }

                ids.Add(d.Id);

                if (d.Id == "x")
                {
                    mimicOk = d.HiddenPower && d.PowerText == "X";
                }
            }

            ok &= Check(report, everyHasEffect, "每张卡至少有 1 条效果");
            ok &= Check(report, idsOk, "卡 ID 无重复");
            ok &= Check(report, mimicOk, "模仿（x）HiddenPower=true 且卡面显示 \"X\"");

            // 双光环卡：af / aj 的指示物数为 2
            bool afOk = CardLibrary.Get("af").AuraTokenCount == 2;
            bool ajOk = CardLibrary.Get("aj").AuraTokenCount == 2;
            ok &= Check(report, afOk, "冰封铠甲（af）光环指示物数 = 2");
            ok &= Check(report, ajOk, "火灾（aj）光环指示物数 = 2");

            // 带光环的牌共 8 张（b/n/af/ag/ah/ai/aj/ak/al 中 af 是双光环，共 9 张卡面 ——
            // 规划 §2 的归类表标注为 8 张，此处按逐卡实测统计，仅作信息输出）
            int auraCards = 0;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                if (CardLibrary.GetByIndex(i).HasAura)
                {
                    auraCards++;
                }
            }

            report.Add("   · 信息：带光环的卡共 " + auraCards + " 张");

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  2) 万局自测 + 不变量 / 规则反查
        // ══════════════════════════════════════════════════════

        public sealed class MassResult
        {
            public int Games;
            public int PlayerWins;
            public int AiWins;
            public int Draws;
            public int Exceptions;
            public int TotalTurns;
            public int MaxTurns;
            public int TotalEvents;

            /// <summary>实际检查过的「带 α 光环的进攻牌」张数（断言覆盖率凭据）。</summary>
            public int AttackAuraChecked;

            /// <summary>实际检查过的「带光环的防御牌」张数（断言覆盖率凭据）。</summary>
            public int DefenseAuraChecked;
            public readonly List<string> Violations = new List<string>();
            public readonly List<string> Failures = new List<string>();
        }

        public static MassResult RunMassGames(int games, int seedBase, bool trace)
        {
            var result = new MassResult { Games = games };

            for (int g = 0; g < games; g++)
            {
                int seed = seedBase + g;
                try
                {
                    MassResult one = RunSingleGame(seed, trace && g == 0);
                    result.PlayerWins += one.PlayerWins;
                    result.AiWins += one.AiWins;
                    result.Draws += one.Draws;
                    result.TotalTurns += one.TotalTurns;
                    result.TotalEvents += one.TotalEvents;
                    result.AttackAuraChecked += one.AttackAuraChecked;
                    result.DefenseAuraChecked += one.DefenseAuraChecked;
                    if (one.MaxTurns > result.MaxTurns)
                    {
                        result.MaxTurns = one.MaxTurns;
                    }

                    for (int i = 0; i < one.Violations.Count && result.Violations.Count < 12; i++)
                    {
                        result.Violations.Add("seed " + seed + "：" + one.Violations[i]);
                    }
                }
                catch (Exception ex)
                {
                    result.Exceptions++;
                    if (result.Failures.Count < 12)
                    {
                        result.Failures.Add("seed " + seed + " 抛异常：" + ex.GetType().Name + " " + ex.Message);
                    }
                }
            }

            return result;
        }

        private static MassResult RunSingleGame(int seed, bool trace)
        {
            var one = new MassResult { Games = 1 };

            BattleEngine engine = BattleEngine.Create(seed);
            var watcher = new EventWatcher(engine);
            var brain = new SimpleAiAgent();
            var agent = new CheckingAgent(brain, engine, watcher);

            engine.OnEvent += watcher.OnEvent;
            if (trace)
            {
                engine.OnEvent += e => Console.WriteLine("    " + e.Describe());
            }

            engine.Start();

            int steps = 0;
            while (!engine.IsOver)
            {
                engine.Advance();

                if (engine.IsOver)
                {
                    break;
                }

                if (engine.Pending == null)
                {
                    one.Violations.Add("Advance 之后既未终局也没有待决策 —— 状态机卡住");
                    break;
                }

                if (++steps > 20000)
                {
                    one.Violations.Add("决策轮次超过 20000，疑似死循环");
                    break;
                }

                DecisionRequest req = engine.Pending;
                DecisionResponse resp = agent.Decide(req);
                engine.Submit(resp);

                watcher.CheckInvariants();
            }

            watcher.CheckInvariants();

            one.TotalTurns = engine.State.TurnNumber;
            one.MaxTurns = engine.State.TurnNumber;
            one.TotalEvents = engine.State.EventSeq;
            one.AttackAuraChecked = watcher.AttackAuraChecked;
            one.DefenseAuraChecked = watcher.DefenseAuraChecked;

            if (!engine.IsOver)
            {
                one.Violations.Add("对局没有正常结束");
            }
            else if (engine.State.WinnerSeat == BattleState.SeatPlayer)
            {
                one.PlayerWins = 1;
            }
            else if (engine.State.WinnerSeat == BattleState.SeatAi)
            {
                one.AiWins = 1;
            }
            else
            {
                one.Draws = 1;
            }

            one.Violations.AddRange(watcher.Violations);
            one.Violations.AddRange(agent.Violations);
            return one;
        }

        // ══════════════════════════════════════════════════════
        //  3) 单点规则断言（用定制牌局直接验证边界判定）
        // ══════════════════════════════════════════════════════

        public static bool RunRuleAssertions(List<string> report)
        {
            bool ok = true;
            report.Add("── 规则类断言（结构性反查）──");

            // R1 · 「有牌时不能主动放弃进攻」：ChooseAttackCard 一律不给 Skip，且 MinSelect = 1
            bool sawAttackRequest = false;
            bool attackNoSkip = true;

            for (int seed = 9000; seed < 9060; seed++)
            {
                BattleEngine engine = BattleEngine.Create(seed);
                var probe = new LambdaAgent(req =>
                {
                    if (req.Kind == RequestKind.ChooseAttackCard)
                    {
                        sawAttackRequest = true;
                        if (req.SkipOption != null || req.ContextNoSkip == false || req.MinSelect < 1)
                        {
                            attackNoSkip = false;
                        }
                    }

                    return new SimpleAiAgent().Decide(req);
                });

                Drain(engine, probe);
            }

            ok &= Check(report, sawAttackRequest, "样本中出现过进攻选牌决策");
            ok &= Check(report, attackNoSkip, "规则 19：有牌时进攻决策不提供「放弃进攻」选项");

            // R2 · 「防御方不允许打出挡不住的牌」：使用本次已投入的防御光环加成，校验每个非 Skip 选项都够挡
            bool sawDefenseRequest = false;
            bool defenseLegal = true;

            for (int seed = 9100; seed < 9160; seed++)
            {
                BattleEngine engine = BattleEngine.Create(seed);
                var probe = new LambdaAgent(req =>
                {
                    if (req.Kind == RequestKind.ChooseDefense)
                    {
                        sawDefenseRequest = true;
                        if (!CheckDefenseLegality(engine, req))
                        {
                            defenseLegal = false;
                        }
                    }

                    return new SimpleAiAgent().Decide(req);
                });

                Drain(engine, probe);
            }

            ok &= Check(report, sawDefenseRequest, "样本中出现过防御决策");
            ok &= Check(report, defenseLegal, "规则 2/7：防御选项全部满足「力量 ≥ 攻击力量」（或守护 / 免疫）");

            // R3 · 规则 9：进攻开始时只有进攻方自己冷却区 −1
            bool tickSeatOk = true;
            for (int seed = 9200; seed < 9240; seed++)
            {
                var watcher = new TickWatcher();
                BattleEngine engine = BattleEngine.Create(seed);
                engine.OnEvent += watcher.OnEvent;
                Drain(engine, new SimpleAiAgent());
                if (!watcher.Ok)
                {
                    tickSeatOk = false;
                    break;
                }
            }

            ok &= Check(report, tickSeatOk, "规则 9：进攻开始结算只减进攻方自己的冷却区");

            // R4 · 推论 P2：连击的追加进攻不重复触发冷却 −1
            bool comboNoDoubleTick = true;
            for (int seed = 9300; seed < 9340; seed++)
            {
                var watcher = new TickWatcher();
                BattleEngine engine = BattleEngine.Create(seed);
                engine.OnEvent += watcher.OnEvent;
                Drain(engine, new SimpleAiAgent());
                if (!watcher.Ok || !watcher.ComboCheckedAllFine)
                {
                    comboNoDoubleTick = false;
                    break;
                }
            }

            ok &= Check(report, comboNoDoubleTick, "推论 P2：连击追加进攻不重复触发冷却 −1");

            // R5 · 决策请求的结构合法性（选项序号连续、无重复、非空）
            bool requestShapeOk = true;
            for (int seed = 9400; seed < 9440; seed++)
            {
                // ⚠ agent 必须**每局一个**，不能每次决策 new 一个：
                //   2026-09-21 起 SimpleAiAgent 带了「查看对方手牌」的盲选状态，
                //   每拍重建会让它每次都从同一起点出发，盲选退化成「按手牌张数查表」。
                var brain = new SimpleAiAgent();
                var probe = new LambdaAgent(req =>
                {
                    if (!CheckRequestShape(req))
                    {
                        requestShapeOk = false;
                    }

                    return brain.Decide(req);
                });
                Drain(BattleEngine.Create(seed), probe);
            }

            ok &= Check(report, requestShapeOk, "所有决策请求的选项结构合法（序号连续、无重复、非空）");

            // R6 · 光环交互（2026-09-18）：AI **开着光环**也要能跑完整局。
            //
            // 为什么要单独跑一遍：万局回归里 UseAuras 默认 false，光环那条新路径
            // （判定区准备 → AuraPrepOnly 重发本拍 → 随出牌提交；免疫即结算）
            // 在那一万局里**一次都不会被走到**。这是唯一能在无 Unity 环境下覆盖它的地方。
            bool auraAiOk = true;
            string auraAiWhy = string.Empty;

            for (int seed = 9500; seed < 9540 && auraAiOk; seed++)
            {
                var brain = new SimpleAiAgent { UseAuras = true };
                BattleEngine engine = BattleEngine.Create(seed);
                var watcher = new EventWatcher(engine);
                engine.OnEvent += watcher.OnEvent;

                var probe = new LambdaAgent(req =>
                {
                    if (!CheckRequestShape(req))
                    {
                        auraAiOk = false;
                        auraAiWhy = "seed " + seed + " 请求结构非法：" + req.Kind;
                    }

                    return brain.Decide(req);
                });

                Drain(engine, probe);

                if (!engine.IsOver)
                {
                    auraAiOk = false;
                    auraAiWhy = "seed " + seed + " 没打完（疑似在光环准备上反复重发）";
                }
                else if (watcher.Violations.Count > 0)
                {
                    auraAiOk = false;
                    auraAiWhy = "seed " + seed + " " + watcher.Violations[0];
                }
            }

            ok &= Check(report, auraAiOk, "AI 开启光环后仍能跑完 40 局（覆盖准备 / 重发 / 免疫路径）"
                                          + (auraAiOk ? string.Empty : "：" + auraAiWhy));

            return ok;
        }

        // ── 辅助 ────────────────────────────────────────────────

        private static void Drain(BattleEngine engine, IAgent agent)
        {
            engine.Start();
            int steps = 0;
            while (!engine.IsOver)
            {
                engine.Advance();
                if (engine.IsOver || engine.Pending == null)
                {
                    break;
                }

                engine.Submit(agent.Decide(engine.Pending));
                if (++steps > 20000)
                {
                    break;
                }
            }
        }

        public static bool CheckRequestShape(DecisionRequest req)
        {
            if (req.Options == null || req.Options.Count == 0)
            {
                return false;
            }

            var seen = new HashSet<int>();
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].Index != i || !seen.Add(req.Options[i].Index))
                {
                    return false;
                }
            }

            if (req.MaxSelect < 1 || req.MinSelect < 0 || req.MinSelect > req.MaxSelect)
            {
                return false;
            }

            // 允许放弃的决策必须带 Skip
            if (req.MinSelect == 0 && req.SkipOption == null && req.Kind != RequestKind.ChooseCoolHandCards
                && req.Kind != RequestKind.ChooseReplace)
            {
                return false;
            }

            return true;
        }

        /// <summary>读取「本次防御已主动投入的光环加成」，逐个校验防御选项确实够挡。</summary>
        public static bool CheckDefenseLegality(BattleEngine engine, DecisionRequest req)
        {
            PlayerState defender = engine.State.Of(req.Seat);
            int available = req.ContextDefenseBonus;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (o.IsSkip)
                {
                    continue;
                }

                // 光环选项也带 Card（= 它的来源牌），但它不是「要打出的那张牌」——
                // 2026-09-18 起它与防御选项同在一个列表里，不排掉就会被当成一张挡不住的牌。
                if (o.AuraSource != null)
                {
                    continue;
                }

                if (o.Card == null)
                {
                    return false;
                }

                int need = NeedBonus(o.Card, req.ContextPower);
                if (o.PairCard != null)
                {
                    need += NeedBonus(o.PairCard, req.ContextPower);
                }
                else if (req.ContextDouble)
                {
                    return false;   // 双发必须给成对选项（A1：不能部分防御）
                }

                if (need > available)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 本牌挡下这一刀还差几点「已投入光环」——<b>独立于引擎重算一遍</b>。
        ///
        /// <para>⚠ 引擎改了防御力量的算法，这里必须跟着改，否则断言会误报：
        /// 2026-09-20 起防御力量 = 有效力量 + 卡面 β「防御时力量 +A」
        /// （<see cref="DefenseResolver.DefensePower"/>）。</para>
        /// </summary>
        private static int NeedBonus(CardInstance card, int attackPower)
        {
            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                EffectDef ef = card.Def.Effects[i];
                if (ef.Trigger == EffectTrigger.Defend && ef.Op == EffectOp.Guard)
                {
                    return 0;   // 守护：无视力量差异
                }
            }

            int need = attackPower - DefenseResolver.DefensePower(card);
            return need < 0 ? 0 : need;
        }

        private static bool Check(List<string> report, bool condition, string label)
        {
            report.Add((condition ? "  [PASS] " : "  [FAIL] ") + label);
            return condition;
        }

        // ── 测试用代理 ──────────────────────────────────────────

        private sealed class LambdaAgent : IAgent
        {
            private readonly Func<DecisionRequest, DecisionResponse> _fn;

            public LambdaAgent(Func<DecisionRequest, DecisionResponse> fn)
            {
                _fn = fn;
            }

            public DecisionResponse Decide(DecisionRequest request)
            {
                return _fn(request);
            }
        }

        /// <summary>包一层结构校验的 AI：任何请求形状不合法都记下来。</summary>
        private sealed class CheckingAgent : IAgent
        {
            private readonly IAgent _inner;
            private readonly BattleEngine _engine;
            private readonly EventWatcher _watcher;

            public readonly List<string> Violations = new List<string>();

            public CheckingAgent(IAgent inner, BattleEngine engine, EventWatcher watcher)
            {
                _inner = inner;
                _engine = engine;
                _watcher = watcher;
            }

            public DecisionResponse Decide(DecisionRequest request)
            {
                if (!CheckRequestShape(request))
                {
                    Violations.Add("决策请求结构非法：" + request.Kind);
                }

                if (request.Kind == RequestKind.ChooseDefense
                    && !CheckDefenseLegality(_engine, request))
                {
                    Violations.Add("防御选项合法性被破坏（出现挡不住的牌）");
                }

                if (request.Kind == RequestKind.ChooseAttackCard
                    && (request.SkipOption != null || !request.ContextNoSkip))
                {
                    Violations.Add("进攻决策出现了「放弃进攻」选项 —— 违反规则 19");
                }

                // 光环选项（2026-09-18 起与出牌同拍）：
                //   ① 只允许出现在「出牌 / 防御」两拍里；
                //   ② 指向的来源牌必须仍然激活且有余量 —— 否则玩家拖过去会白白消耗一次手势。
                bool auraHost = request.Kind == RequestKind.ChooseAttackCard
                                || request.Kind == RequestKind.ChooseDefense;

                for (int i = 0; i < request.Options.Count; i++)
                {
                    Option o = request.Options[i];
                    if (o.AuraSource == null)
                    {
                        continue;
                    }

                    if (!auraHost)
                    {
                        Violations.Add("光环选项出现在了不该出现的决策里：" + request.Kind);
                        continue;
                    }

                    if (o.AuraSource.AuraTokens <= 0 || !o.AuraSource.AuraLive)
                    {
                        Violations.Add("光环选项指向了未激活 / 无余量的来源");
                    }
                }

                if (request.Kind == RequestKind.ChooseZoneValue)
                {
                    bool any = false;
                    for (int i = 0; i < request.Options.Count; i++)
                    {
                        if (request.Options[i].Kind != OptionKind.ZoneValue)
                        {
                            continue;
                        }

                        any = true;
                        if (request.Options[i].Value < 1 || request.Options[i].Count < 1)
                        {
                            Violations.Add("区域类选项的 k 或匹配张数非法");
                        }
                    }

                    if (!any)
                    {
                        Violations.Add("区域类决策没有给出任何可选项");
                    }
                }

                DecisionResponse resp = _inner.Decide(request);
                CheckTargetPolarity(request, resp);
                return resp;
            }

            /// <summary>
            /// 定向效果的<b>立场</b>断言（2026-09-20 新增）。
            ///
            /// <para><c>SimpleAiAgent</c> 的既定策略是「加速一律给自己、减速一律给对方」，
            /// 这里把它变成可回归的硬断言：</para>
            /// <list type="bullet">
            /// <item>加速 / 立即冷却完成（<see cref="DecisionRequest.ContextHaste"/> = true）→ 目标必须是自己</item>
            /// <item>减速 / 重置对方冷却（ContextHaste = false）→ 目标必须是对手</item>
            /// <item>区域类看 <c>ContextHaste</c>；选项 Seat = −1（雪崩那种「双方一起」）不判</item>
            /// </list>
            ///
            /// <para>这条断言能抓住的实际缺陷：「区域类打分被张数盖过，AI 把区域加速放给对面」
            /// 与「立即冷却完成拿玩家的牌去冷却」（都真实发生过）。</para>
            /// </summary>
            private void CheckTargetPolarity(DecisionRequest req, DecisionResponse resp)
            {
                if (req == null || resp == null || req.Seat < 0)
                {
                    return;
                }

                bool favorable;   // true = 本拍有利于施法者 → 目标应是自己
                switch (req.Kind)
                {
                    case RequestKind.ChooseHasteTarget:
                        favorable = true;
                        break;

                    case RequestKind.ChooseSlowTarget:
                        favorable = false;
                        break;

                    case RequestKind.ChooseZoneValue:
                    case RequestKind.ChooseRefreshTarget:
                        favorable = req.ContextHaste;
                        break;

                    default:
                        return;
                }

                if (resp.OptionIndices == null || resp.OptionIndices.Length == 0)
                {
                    return;
                }

                Option pick = req.Get(resp.OptionIndices[0]);
                if (pick == null || pick.IsSkip)
                {
                    return;
                }

                int seat = pick.Kind == OptionKind.ZoneValue
                    ? pick.Seat
                    : (pick.Card != null ? pick.Card.OwnerSeat : -1);

                if (seat < 0)
                {
                    return;   // −1 = 双方（雪崩），没有「帮了谁」这一说
                }

                bool own = seat == req.Seat;
                if (favorable != own)
                {
                    Violations.Add("定向效果目标立场错误：" + req.Kind
                                   + " 选了" + (own ? "自己" : "对方") + "的牌（期望"
                                   + (favorable ? "自己" : "对方") + "）");
                }
            }
        }

        /// <summary>订阅事件流，检查跨事件的全局不变量与规则。</summary>
        public sealed class EventWatcher
        {
            private readonly BattleEngine _engine;
            private int _currentAttacker = -1;
            private bool _inComboFollowUp;
            private bool _expectGameOverNext;
            private bool _gameOverSeen;

            /// <summary>各方被永久移出游戏的张数（漩涡），用于「手牌守恒」。</summary>
            private readonly int[] _removedBySeat = new int[4];

            /// <summary>
            /// 本次防御交出去的、卡面带光环的牌 —— 第 ⑤ 步按卡面符号决定它们该不该亮。
            ///
            /// <para><b>这条断言的来历（两次方向相反）</b>：
            /// 2026-09-18 发现「用光环卡防御拿不到光环」（当时的口径是「光环与该牌是攻是防无关」），
            /// 于是断言「防御牌必须激活」；<b>2026-09-20 用户以卡面 α / β / γ 为准推翻了那个口径</b>
            /// —— 本批 12 张光环卡的光环全部标着 α，所以防御牌一枚都不该亮。断言随之翻面。</para>
            ///
            /// <para>这类缺陷在万局统计里<strong>完全无声</strong>（事件照发、手牌守恒照过、
            /// 胜负分布不变），只能在事件流上反查 —— 所以两次方向都写在这里，别再靠记忆。</para>
            ///
            /// <para>只收「由 <see cref="DefenseResolvedEvent"/> 交出的牌」——
            /// 被效果直接丢进冷却区的牌（磁暴 / 电弧等，推论 P4）本来就不该激活光环，不能混进来。</para>
            /// </summary>
            private readonly List<CardInstance> _pendingDefenseAuras = new List<CardInstance>();

            /// <summary>
            /// 本半场的<b>进攻牌</b> —— 第 ⑤ 步必须给它的 α 光环点亮指示物。
            ///
            /// <para>与 <see cref="_pendingDefenseAuras"/> 成正反面：那张查「不该亮的别亮」，
            /// 这张查「该亮的亮了」。</para>
            /// </summary>
            private CardInstance _pendingAttackAura;

            /// <summary>
            /// 本段进攻已经结算过几次防御（−1 = 当前没有正在进行的进攻段）。
            ///
            /// <para><b>为什么钉这一条</b>：一次进攻（含它自己的 α 效果队列）只允许结算一次防御。
            /// 2026-09-23 用户报的「充能陷入无限循环」破坏的正是它 —— 第 ③ 步里
            /// <c>CoolHandForHaste</c> 的分支误把阶段写回 <c>Phase.Stage1</c>，
            /// 而 <c>FinishStage1</c> 会先把<b>①③ 两阶段共用的</b> <c>_cursor</c> 归零、
            /// 再落到 <c>AwaitDefense</c>，于是「防御 → 加速 → 选牌 → 防御 → …」无限重放。</para>
            ///
            /// <para><b>为什么万局统计抓不到、只能在这一层反查</b>：重放期间每一条事件单看都合法
            /// （冷却值、手牌守恒、光环归属全都不越界），只有「同一段进攻里防御结算了 N 次」
            /// 这个<b>跨事件</b>的事实是错的。</para>
            /// </summary>
            private int _defenseResolvedForAttack = -1;

            /// <summary>实际检查过的「带 α 光环的进攻牌」张数 —— 断言不是空跑的凭据。</summary>
            public int AttackAuraChecked { get; private set; }

            /// <summary>实际检查过的「带光环的防御牌」张数 —— 断言不是空跑的凭据。</summary>
            public int DefenseAuraChecked { get; private set; }

            public readonly List<string> Violations = new List<string>();

            public EventWatcher(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                // 上限归零 → 即刻死亡的后续事件必须是对局结束（决策 C1）
                if (_expectGameOverNext && !(e is GameOverEvent))
                {
                    Violations.Add("生命上限归零后未立即结束对局，而是发生了 " + e.GetType().Name);
                }

                _expectGameOverNext = false;

                var turn = e as TurnStartedEvent;
                if (turn != null)
                {
                    // 上一半场的攻防牌到这一刻必须已经按 α / β 点亮过光环
                    //（第 ④ 步入冷却区 → 第 ⑤ 步点亮，中间不会再有 TurnStartedEvent）。
                    CheckAuraActivationByRole();

                    // 半场翻篇 = 上一段进攻结束。正常这里记到的是「恰好 1 次防御」；
                    // 若跑出 > 1，说明结算阶段被重放（见 _defenseResolvedForAttack 的说明）。
                    FlushAttackSegment();

                    _pendingDefenseAuras.Clear();
                    _pendingAttackAura = null;

                    _currentAttacker = turn.Seat;
                    _inComboFollowUp = turn.IsComboFollowUp;

                    // 「冷却完毕必须真的回到手牌」—— 半场开始的那一刻，上一半场的攻防牌都已入冷却区，
                    // 没有任何牌处于「攻防中」，所以此时 手牌 + 冷却区 + 已移出 = 该方总牌数，
                    // 而且冷却区里不该留着剩余 0 的牌（归 0 必须当场回手）。
                    //
                    // ⚠ 这条断言是 2026-09-17 补的：此前 CooldownOps.ReturnToHand 只改 Zone 字段、
                    // 不把牌挂回 Hand 列表，事件与日志却全都正常，万局统计一个都抓不到。
                    CheckHandConservation("半场开始");
                }

                // ⚠ 回手事件发出时，那张牌必须**已经**在手牌列表里。
                // 这是「回手」这条规则的唯一硬判据 —— 光看事件发没发是查不出问题的。
                var returned = e as CardReturnedEvent;
                if (returned != null && returned.Card != null)
                {
                    PlayerState owner = _engine.State.Of(returned.Seat);
                    if (owner.Hand.IndexOf(returned.Card) < 0)
                    {
                        Violations.Add("回手未落到手牌列表：seat" + returned.Seat + " " + returned.Card.Def.Name);
                    }

                    if (owner.CoolingZone.IndexOf(returned.Card) >= 0)
                    {
                        Violations.Add("回手后仍留在冷却区：" + returned.Card.Def.Name);
                    }
                }

                var removed = e as CardRemovedEvent;
                if (removed != null && removed.Seat >= 0 && removed.Seat < _removedBySeat.Length)
                {
                    _removedBySeat[removed.Seat]++;
                }

                // 「作为防御牌打出」这件事的唯一来源 —— 免疫那条臂的 Cards 为空，自然不入账。
                var defended = e as DefenseResolvedEvent;
                if (defended != null)
                {
                    // 一次进攻只该结算一次防御（见 _defenseResolvedForAttack 的说明）。
                    if (_defenseResolvedForAttack >= 0)
                    {
                        _defenseResolvedForAttack++;
                    }

                    if (defended.Cards != null)
                    {
                        for (int i = 0; i < defended.Cards.Count; i++)
                        {
                            CardInstance c = defended.Cards[i];
                            if (c != null && c.Def != null && c.Def.HasAura)
                            {
                                _pendingDefenseAuras.Add(c);
                            }
                        }
                    }
                }

                // 「作为进攻牌打出」这件事的唯一来源 —— 第 ⑤ 步必须点亮它的 α 光环。
                var declared = e as AttackDeclaredEvent;
                if (declared != null && declared.Card != null)
                {
                    // 新的一段进攻开始了 → 先把上一段结掉
                    FlushAttackSegment();
                    _defenseResolvedForAttack = 0;
                    _pendingAttackAura = declared.Card;
                }

                var activated = e as AuraActivatedEvent;
                if (activated != null && activated.Card != null)
                {
                    _pendingDefenseAuras.Remove(activated.Card);
                }

                if (e is GameOverEvent)
                {
                    // 对局在结算途中结束（第 ④ 步就 Finished）时第 ⑤ 步本来就不会跑，不算违规
                    FlushAttackSegment();
                    _pendingDefenseAuras.Clear();
                    _pendingAttackAura = null;
                }

                var cd = e as CooldownChangedEvent;
                if (cd != null)
                {
                    if (cd.Change.Card != null && cd.Change.Card.IsCooling)
                    {
                        int remain = cd.Change.Card.RemainingCooldown;
                        int baseCd = cd.Change.Card.Def.Cooldown;
                        if (remain < 1 || remain > baseCd)
                        {
                            Violations.Add("冷却不变量被破坏：" + cd.Change.Card.Def.Name
                                           + " 剩余 " + remain + " 基础 " + baseCd);
                        }
                    }

                    if (cd.Change.Reason != null && cd.Change.Reason.Contains("进攻开始 −1"))
                    {
                        if (_inComboFollowUp)
                        {
                            Violations.Add("推论 P2 被破坏：连击的追加进攻又触发了一次冷却 −1");
                        }

                        if (cd.Change.Card != null && cd.Change.Card.OwnerSeat != _currentAttacker)
                        {
                            Violations.Add("规则 9 被破坏：非进攻方的冷却区被结算了 −1");
                        }
                    }
                }

                var dmg = e as DamageTakenEvent;
                if (dmg != null && dmg.Source != null && dmg.Source.Contains("双发未被挡住"))
                {
                    if (dmg.Amount != 1)
                    {
                        Violations.Add("边界判定被破坏：双发未被挡住应只扣 1 点，实为 " + dmg.Amount);
                    }
                }

                var max = e as HpMaxChangedEvent;
                if (max != null && max.To <= 0)
                {
                    _expectGameOverNext = true;
                }

                if (e is GameOverEvent)
                {
                    _gameOverSeen = true;
                }
            }

            /// <summary>
            /// 结掉当前这段进攻，检查它只结算过一次防御。
            ///
            /// <para>调用点 = 「一段进攻结束」的三个时刻：新的进攻宣告、新的半场开始、对局结束。
            /// 三条都必须调 —— 只调一处会让最后一段进攻永远不被检查（那正是缺陷最容易藏身的位置）。</para>
            /// </summary>
            private void FlushAttackSegment()
            {
                CheckAttackSegment();
                _defenseResolvedForAttack = -1;
                _segmentReported = false;
            }

            /// <summary>本段已经报过违规（循环里只报一次，免得刷出上千条重复）。</summary>
            private bool _segmentReported;

            /// <summary>
            /// 检查「这一段进攻是不是结算了不止一次防御」——<b>只检查，不重置</b>。
            ///
            /// <para><b>为什么除了 <see cref="FlushAttackSegment"/> 还要有这一个</b>：
            /// 重放型缺陷会让对局<b>卡在同一段进攻里出不来</b>（「防御 → 加速 → 选牌 → 防御 → …」），
            /// 于是「段结束」这个时刻<b>永远不会到来</b> —— 只在段结束时检查的话，
            /// 这条断言在最需要它的场景里恰好是哑的（2026-09-23 实测：
            /// 坏版本跑 8 局 11 分钟都没结束，而断言一次都没报）。</para>
            ///
            /// <para>所以 <see cref="CheckInvariants"/>（万局回归每一拍都会调）里也调它 ——
            /// 第 2 次防御一发生就被抓住，与「段有没有结束」无关。</para>
            /// </summary>
            private void CheckAttackSegment()
            {
                if (_defenseResolvedForAttack > 1 && !_segmentReported)
                {
                    _segmentReported = true;
                    Violations.Add("一段进攻里结算了 " + _defenseResolvedForAttack
                                   + " 次防御 —— 结算阶段被重放（效果把阶段写回了它已经走过的分支，"
                                   + "后果是对局卡在同一段进攻里出不来）");
                }
            }

            /// <summary>
            /// 「卡面符号 = 结算时机」在光环上的落地检查（2026-09-20，`Docs/rules/01-规则基线.md` §0）：
            /// <list type="bullet">
            /// <item><b>进攻牌</b>（α）：卡面带 α 光环 → 进冷却区后必须点亮；</item>
            /// <item><b>防御牌</b>（β）：只有当卡面确实有 β 光环时才允许点亮 —— 本批 12 张光环卡的
            /// 光环全部标着 α，所以「拿光环卡去防御」必须<b>一枚都不亮</b>。</item>
            /// </list>
            ///
            /// <para>2026-09-18 这条曾经是反过来的（当时要求防御牌也点亮），用户 2026-09-20
            /// 以卡面 α/β/γ 为准推翻了那个口径 —— 断言跟着翻面，别再改回去。</para>
            /// </summary>
            private void CheckAuraActivationByRole()
            {
                CardInstance atk = _pendingAttackAura;
                if (atk != null && atk.IsCooling && atk.Def.AuraTokenCountOf(EffectTrigger.Attack) > 0)
                {
                    AttackAuraChecked++;

                    if (!atk.AuraLive)
                    {
                        Violations.Add("进攻牌的 α 光环未点亮：" + atk.Def.Name
                                       + "（卡面带 α 光环，作为进攻牌打出后 AuraLive 仍为 false）");
                    }
                }

                for (int i = 0; i < _pendingDefenseAuras.Count; i++)
                {
                    CardInstance c = _pendingDefenseAuras[i];
                    if (c == null)
                    {
                        continue;
                    }

                    DefenseAuraChecked++;

                    int beta = c.Def.AuraTokenCountOf(EffectTrigger.Defend);

                    if (beta > 0 && !c.AuraLive)
                    {
                        Violations.Add("防御牌的 β 光环未点亮：" + c.Def.Name);
                    }
                    else if (beta == 0 && (c.AuraLive || c.AuraTokens != 0))
                    {
                        Violations.Add("α 光环被「作为防御牌打出」点亮了：" + c.Def.Name
                                       + "（卡面写的是 α = 只有进攻时才触发，见 02-卡牌图鉴）");
                    }
                }
            }

            /// <summary>
            /// 手牌守恒：某一方「手牌 + 冷却区 + 已永久移出」必须等于该方应有的总牌数
            /// （规则 §1：初始 6 + 第 2、3 回合各 1）。
            ///
            /// <para>调用点必须是<b>没有牌处于攻防中</b>的时刻（半场开始），
            /// 否则会误报 —— 攻防中的牌既不在手牌也不在冷却区。</para>
            /// </summary>
            private void CheckHandConservation(string when)
            {
                BattleState s = _engine.State;

                for (int seat = 0; seat < s.Players.Count; seat++)
                {
                    PlayerState p = s.Players[seat];
                    int expected = 6;
                    if (s.TurnNumber >= 2)
                    {
                        expected++;
                    }

                    if (s.TurnNumber >= 3)
                    {
                        expected++;
                    }

                    int removed = seat < _removedBySeat.Length ? _removedBySeat[seat] : 0;
                    expected -= removed;

                    int actual = p.Hand.Count + p.CoolingZone.Count;
                    if (actual != expected)
                    {
                        Violations.Add("手牌守恒被破坏（" + when + "）：seat" + seat
                                       + " 手牌 " + p.Hand.Count + " + 冷却 " + p.CoolingZone.Count
                                       + " = " + actual + "，应为 " + expected
                                       + "（有牌在移出/回手时丢了归属，或重复计数）");
                    }
                }
            }

            public void CheckInvariants()
            {
                // 先看「这一段进攻有没有被重放」：重放时对局根本走不到段结束，这个入口是唯一抓得住它的地方。
                CheckAttackSegment();

                BattleState s = _engine.State;

                for (int i = 0; i < s.Players.Count; i++)
                {
                    PlayerState p = s.Players[i];

                    if (p.Hand.Count > BattleState.HandLimit)
                    {
                        Violations.Add("规则 16 被破坏：手牌超过 8 张（" + p.Hand.Count + "）");
                    }

                    if (p.Hp > p.MaxHp)
                    {
                        Violations.Add("不变量被破坏：Hp(" + p.Hp + ") > MaxHp(" + p.MaxHp + ")");
                    }

                    for (int k = 0; k < p.CoolingZone.Count; k++)
                    {
                        CardInstance c = p.CoolingZone[k];
                        if (c.RemainingCooldown < 1 || c.RemainingCooldown > c.Def.Cooldown)
                        {
                            Violations.Add("冷却不变量被破坏：" + c.Def.Name + " 剩余 " + c.RemainingCooldown);
                        }

                        if (c.AuraTokens > c.Def.AuraTokenCount)
                        {
                            Violations.Add("光环指示物超过卡面上限：" + c.Def.Name);
                        }

                        if (c.AuraTokens > 0 && !c.AuraLive)
                        {
                            Violations.Add("光环未激活却留有指示物：" + c.Def.Name);
                        }
                    }

                    // 手牌里不该残留光环状态（回手即作废）
                    for (int k = 0; k < p.Hand.Count; k++)
                    {
                        if (p.Hand[k].AuraTokens != 0 || p.Hand[k].AuraLive)
                        {
                            Violations.Add("回手后未清空光环状态：" + p.Hand[k].Def.Name);
                        }

                        if (p.Hand[k].RemainingCooldown != 0)
                        {
                            Violations.Add("手牌残留冷却值：" + p.Hand[k].Def.Name);
                        }
                    }
                }

                if (s.IsOver && !_gameOverSeen)
                {
                    Violations.Add("对局已结束但没有发出 GameOverEvent");
                }
            }
        }

        /// <summary>只盯「进攻开始 −1」的座位归属与连击重复触发。</summary>
        private sealed class TickWatcher
        {
            private int _currentAttacker = -1;
            private bool _inCombo;
            public bool Ok = true;
            public bool ComboCheckedAllFine = true;

            public void OnEvent(BattleEvent e)
            {
                var turn = e as TurnStartedEvent;
                if (turn != null)
                {
                    _inCombo = turn.IsComboFollowUp;
                    _currentAttacker = turn.Seat;
                    return;
                }

                var cd = e as CooldownChangedEvent;
                if (cd == null || cd.Change.Reason == null || !cd.Change.Reason.Contains("进攻开始 −1"))
                {
                    return;
                }

                if (cd.Change.Card != null && cd.Change.Card.OwnerSeat != _currentAttacker)
                {
                    Ok = false;
                }

                if (_inCombo)
                {
                    ComboCheckedAllFine = false;
                }
            }
        }
    }
}
