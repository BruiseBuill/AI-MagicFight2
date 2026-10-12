using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 启发式 AI（四流派）自测（2026-10-03）。
    ///
    /// <para><b>为什么要单独一个场景</b>：万局统计里跑的是 <c>SimpleAiAgent</c>
    /// （见 <c>RuleAssertions.RunSingleGame</c>），新 AI 在那一万局里<b>一次都不会被走到</b>。
    /// 而新 AI 的失效方式又全是无声的 —— 优先级档位写反只会表现为「打得不合套路」，
    /// 引擎一个字都不会报。所以两层都要钉：</para>
    /// <list type="number">
    /// <item><b>纯函数层</b>：<see cref="CardRole"/> 的卡牌分类 + <see cref="ArchetypeJudge"/>
    /// 的四流派打分（构造手牌直接算，不跑引擎）；</item>
    /// <item><b>实跑层</b>：用 <see cref="HeuristicAgent"/> 跑一批完整对局，
    /// 断言「不卡死、不抛异常、每次回填都合法」—— 这是唯一能覆盖
    /// 「防御侧报备光环 → 引擎重发 → 第二次提交」那条两拍链路的地方。
    /// 它同时跑一遍<b>旧 AI 的对照</b>，用来区分「新 AI 的回归」与「规则固有现象」。</item>
    /// </list>
    /// </summary>
    public static class HeuristicAgentScenario
    {
        public static bool Run(List<string> report)
        {
            report.Add("── 启发式 AI（四流派）──");
            bool ok = true;

            ok &= CheckRoleClassification(report);
            ok &= CheckArchetypeScoring(report);
            ok &= CheckDoubleDefenseCorrection(report);
            ok &= CheckMassGames(report, 300);

            return ok;
        }

        // ── ① 卡牌角色分类（纯 CardDef）────────────────────────

        private static bool CheckRoleClassification(List<string> report)
        {
            bool ok = true;

            // 连击卡：除过载外，包括引雷（引雷是「光环给连击」）。
            ok &= Check(report, CardRole.IsComboCard(CardLibrary.Get("j")), "闪电（j）是连击卡");
            ok &= Check(report, CardRole.IsComboCard(CardLibrary.Get("n")), "引雷（n）算连击卡（光环给连击）");
            ok &= Check(report, !CardRole.IsComboCard(CardLibrary.Get("o")), "过载（o）不算连击卡（归入回血）");

            // 回血卡：过载 / 自燃。
            ok &= Check(report,
                CardRole.IsHealCard(CardLibrary.Get("o")) && CardRole.IsHealCard(CardLibrary.Get("p")),
                "过载（o）/ 自燃（p）是回血卡");

            // 区域减速：暴风雪 / 冰风暴 / 雪崩。
            ok &= Check(report,
                CardRole.IsSlowZoneCard(CardLibrary.Get("a"))
                && CardRole.IsSlowZoneCard(CardLibrary.Get("b"))
                && CardRole.IsSlowZoneCard(CardLibrary.Get("an")),
                "区域减速 = 暴风雪（a）/ 冰风暴（b）/ 雪崩（an）");

            // 其他减速：凝固 / 雷云 / 淬火 / 冷冻核心。
            ok &= Check(report,
                CardRole.IsSlowCard(CardLibrary.Get("c")) && !CardRole.IsSlowZoneCard(CardLibrary.Get("c")),
                "凝固（c）是「其他减速」而不是区域减速");

            // 双发卡。
            ok &= Check(report,
                CardRole.IsDoubleCard(CardLibrary.Get("t")) && CardRole.IsDoubleCard(CardLibrary.Get("w")),
                "荆棘（t）/ 狂躁蘑菇（w）是双发卡");

            // 光环数值（双光环要相加）。
            ok &= Check(report, CardRole.AttackAuraValue(CardLibrary.Get("aj")) == 2,
                "火灾（aj）的双进攻光环 = 2");
            ok &= Check(report, CardRole.DefenseAuraValue(CardLibrary.Get("af")) == 4,
                "冰封铠甲（af）的双防御光环 = 4");
            ok &= Check(report, CardRole.AttackAuraValue(CardLibrary.Get("ah")) == 3,
                "烈焰斗篷（ah）的攻/防光环在进攻侧算 3");

            // 加速强度。
            ok &= Check(report, CardRole.HasteStrength(CardLibrary.Get("ab"), 0) == 2,
                "湍流（ab）加速强度 = 2");
            ok &= Check(report, CardRole.HasteStrength(CardLibrary.Get("y"), 2) == 3,
                "瀑流（y）在损失 2 点生命时加速强度 = 1 + 2");

            // 「力量潜力」：卡自带进攻光环要算进去（火灾 8 + 1 + 1 = 10）。
            ok &= Check(report, CardRole.AttackPotential(Make("aj")) == 10,
                "火灾（aj）力量潜力 = 8 + 1 + 1 = 10");
            // 「防御潜力」：冰封铠甲 5 + 2 + 2 = 9 达标。
            ok &= Check(report, CardRole.DefensePotential(Make("af")) == 9,
                "冰封铠甲（af）防御潜力 = 5 + 2 + 2 = 9");
            // ⚠ 2026-10-03：冰风暴（b）的光环「防御力量 +2」改成「守护」，
            //   它不再是防御光环卡 —— 潜力回落到基础力量 5（含 β「防御时 +A」为 0）。
            ok &= Check(report, CardRole.DefensePotential(Make("b")) == 5,
                "冰风暴（b）光环改成守护后防御潜力 = 5");
            ok &= Check(report, !CardRole.HasDefenseAura(CardLibrary.Get("b")),
                "冰风暴（b）不再算防御光环卡");

            return ok;
        }

        // ── ② 四流派打分（构造手牌）────────────────────────────

        private static bool CheckArchetypeScoring(List<string> report)
        {
            bool ok = true;

            // 高攻：f(9) / g(9) / i(8) 三张 ≥8。
            ArchetypeAnalysis high = ArchetypeJudge.Analyze(Hand("f", "g", "i"));
            ok &= Check(report,
                high.Chosen == BattleArchetype.HighPower
                && high.ScoreOf(BattleArchetype.HighPower) == 3,
                "三张力量 ≥8 → 高攻 3 分并胜出（实际 " + high.ScoreOf(BattleArchetype.HighPower) + " / "
                + high.Chosen + "）");

            // 连击：j / l / m / n 四张连击卡 + q 区域加速。
            // 2（基础）+ 2（4 张里超出基础的那 2 张）+ 2（区域加速：m 与 q 各一张）。
            // ⚠ 雷鸣（m）**同时**是连击卡与区域加速卡，两处各计一次 —— 这是「每有一张……各记一分」
            //   的字面结果，不是重复计数。
            ArchetypeAnalysis combo = ArchetypeJudge.Analyze(Hand("j", "l", "m", "n", "q"));
            ok &= Check(report,
                combo.Chosen == BattleArchetype.Combo
                && combo.ScoreOf(BattleArchetype.Combo) == 6,
                "四张连击卡（含雷鸣）+ 区域加速 → 连击 6 分并胜出（实际 "
                + combo.ScoreOf(BattleArchetype.Combo) + " / " + combo.Chosen + "）");

            // 减速：a / b 两张区域减速 + c 一张其他减速 → 1 + 3 + 1 = 5。
            ArchetypeAnalysis slow = ArchetypeJudge.Analyze(Hand("a", "b", "c"));
            ok &= Check(report,
                slow.Chosen == BattleArchetype.Slow
                && slow.ScoreOf(BattleArchetype.Slow) == 5,
                "两张区域减速 + 一张其他减速 → 减速 5 分并胜出（实际 "
                + slow.ScoreOf(BattleArchetype.Slow) + " / " + slow.Chosen + "）");

            // 减速的前提：只有一张「其他减速」（地震）时，减速流派一分都不该得。
            ArchetypeAnalysis noZone = ArchetypeJudge.Analyze(Hand("i"));
            ok &= Check(report,
                noZone.ScoreOf(BattleArchetype.Slow) == 0
                && noZone.Chosen == BattleArchetype.HighPower,
                "只有一张地震（无区域减速）→ 减速 0 分，落回高攻（实际 "
                + noZone.ScoreOf(BattleArchetype.Slow) + " / " + noZone.Chosen + "）");

            // 同分取靠前：两张力量 ≥8（高攻 2）同时也让「防御 ≥8」记 2 分 → 双发也是 2
            // ⇒ 平手，必须取靠前的高攻。
            // ⚠ 注意「力量 ≥8 的牌必然防御也 ≥8」（防御力量 = 有效力量 + β 加值），
            //   所以这两项的分数在这一类手牌上天然同步 —— 这正好是检验「同分取靠前」的样本。
            ArchetypeAnalysis tie = ArchetypeJudge.Analyze(Hand("g", "i"));
            ok &= Check(report,
                tie.ScoreOf(BattleArchetype.HighPower) == 2
                && tie.ScoreOf(BattleArchetype.Double) == 2
                && tie.Chosen == BattleArchetype.HighPower,
                "高攻与双发同分 → 取靠前的高攻（实际 "
                + tie.ScoreOf(BattleArchetype.HighPower) + " / " + tie.ScoreOf(BattleArchetype.Double)
                + " → " + tie.Chosen + "）");

            // 空手牌兜底。
            ArchetypeAnalysis empty = ArchetypeJudge.Analyze(new List<CardInstance>());
            ok &= Check(report, empty.Chosen == BattleArchetype.HighPower, "空手牌 → 兜底高攻");

            return ok;
        }

        /// <summary>
        /// 造一张「已经进过手牌」的实例。
        ///
        /// <para><b>⚠ 必须走 <see cref="CardInstance.ToHand"/></b>：构造完的实例
        /// <c>EffectivePower</c> 是 0（那一位由 <c>ResetEffectivePower</c> 在 ToHand / ToPool /
        /// PutIntoCooldown 三处维护），直接拿它算「力量潜力」会得到「光环值」—— 这正是本场景
        /// 第一版踩到的坑（火灾被算成 2 而不是 10）。</para>
        /// </summary>
        private static CardInstance Make(string id)
        {
            var card = new CardInstance(CardLibrary.Get(id), 0);
            card.ToHand();
            return card;
        }

        private static List<CardInstance> Hand(params string[] ids)
        {
            var list = new List<CardInstance>();
            for (int i = 0; i < ids.Length; i++)
            {
                list.Add(Make(ids[i]));
            }

            return list;
        }

        // ── ③ 实跑：不卡死 / 回填合法 / 流派分布 ────────────────

        private const int StepLimit = 20000;

        private static bool CheckMassGames(List<string> report, int games)
        {
            bool ok = true;
            int exceptions = 0;
            int stall = 0;
            int rejected = 0;
            int illegal = 0;
            int wins0 = 0, wins1 = 0, draws = 0;
            long turns = 0;
            int maxTurns = 0;
            var archetypes = new int[4];
            var stalledSeeds = new List<string>();

            for (int g = 0; g < games; g++)
            {
                int seed = 9700 + g;
                BattleEngine engine = BattleEngine.Create(seed);
                // 这一批是 AI 对 AI → 套上回合上限（用户 2026-10-03 口径：最多 100 回合）。
                // ⚠ 必须显式设：Create 只按 setup 的 Control 判「全员 AI」，而默认 setup 的
                //   0 号座位写着 Human，自动判定不会生效。
                engine.MaxTurns = BattleEngine.AiTurnLimit;
                var agent = new HeuristicAgent(engine.State);

                try
                {
                    engine.Start();
                    int guard = 0;
                    while (!engine.IsOver)
                    {
                        engine.Advance();
                        if (engine.IsOver)
                        {
                            break;
                        }

                        if (engine.Pending == null)
                        {
                            stall++;
                            stalledSeeds.Add("seed " + seed + " 既未终局也无待决策（状态机卡住）");
                            break;
                        }

                        DecisionRequest req = engine.Pending;
                        DecisionResponse resp = agent.Decide(req);
                        if (!IsLegal(req, resp))
                        {
                            illegal++;
                        }

                        // 引擎对非法 / 不合窗口的提交是**静默拒绝**（Pending 原样留着），
                        // 所以比一次引用就知道有没有被吞掉 —— 被吞掉而外层不换招就是死循环。
                        engine.Submit(resp);
                        if (ReferenceEquals(engine.Pending, req))
                        {
                            rejected++;
                            if (rejected <= 3)
                            {
                                stalledSeeds.Add("seed " + seed + " 提交被静默拒绝：停在 " + req.Kind
                                                 + "（Min/Max = " + req.MinSelect + "/" + req.MaxSelect
                                                 + "，options = " + req.OptionCount + "）");
                            }

                            break;
                        }

                        if (++guard > StepLimit)
                        {
                            stall++;
                            if (stalledSeeds.Count < 8)
                            {
                                // 关键判别：回合数也很大 ⇒ 是「对局打不完」（互挡）；
                                // 回合数很小 ⇒ 是「同一个半场里空转」（真 bug）。
                                stalledSeeds.Add("seed " + seed + " 步数超限：" + engine.State.Describe());
                            }

                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions++;
                    stalledSeeds.Add("seed " + seed + " 抛异常：" + ex.GetType().Name + " " + ex.Message);
                    continue;
                }

                if (!engine.IsOver)
                {
                    continue;
                }

                turns += engine.State.TurnNumber;
                if (engine.State.TurnNumber > maxTurns)
                {
                    maxTurns = engine.State.TurnNumber;
                }

                if (agent.HasJudged)
                {
                    archetypes[(int)agent.Archetype]++;
                }

                int winner = engine.State.WinnerSeat;
                if (winner == BattleState.SeatPlayer)
                {
                    wins0++;
                }
                else if (winner == BattleState.SeatAi)
                {
                    wins1++;
                }
                else
                {
                    draws++;
                }
            }

            // ── 对照：同样的种子用旧 AI 跑，判断「打不完」是新 AI 的回归还是规则固有现象 ──
            int legacyStall = 0;
            int legacyMaxTurns = 0;
            for (int g = 0; g < games; g++)
            {
                int seed = 9700 + g;
                BattleEngine engine = BattleEngine.Create(seed);
                var legacy = new SimpleAiAgent();
                engine.Start();
                int guard = 0;
                while (!engine.IsOver)
                {
                    engine.Advance();
                    if (engine.IsOver)
                    {
                        break;
                    }

                    if (engine.Pending == null)
                    {
                        legacyStall++;
                        break;
                    }

                    engine.Submit(legacy.Decide(engine.Pending));
                    if (++guard > StepLimit)
                    {
                        legacyStall++;
                        engine = null;
                        break;
                    }
                }

                if (engine != null && engine.IsOver && engine.State.TurnNumber > legacyMaxTurns)
                {
                    legacyMaxTurns = engine.State.TurnNumber;
                }
            }

            for (int i = 0; i < stalledSeeds.Count; i++)
            {
                report.Add("      · " + stalledSeeds[i]);
            }

            ok &= Check(report, exceptions == 0, "跑 " + games + " 局无异常（实际 " + exceptions + "）");
            // 有了回合上限（2026-10-03 用户口径：AI 对 AI 最多 100 回合），这一批必须
            // **全部跑完** —— 任何未跑完的局都说明上限没生效，或引擎在别的路径上真的死循环了。
            // 加限之前这里会稳定出现 7 局 3000+ 回合的长局（双方都「能防就防 + 交光环补值」⇒
            // 谁都不掉血），那是规则现象、不是缺陷，见文档 §10。
            ok &= Check(report, stall == 0,
                "跑 " + games + " 局全部跑完（上限 " + BattleEngine.AiTurnLimit + " 回合；实际未跑完 " + stall + "）");
            ok &= Check(report, rejected == 0, "没有提交被引擎静默拒绝（实际 " + rejected + " 次）");
            ok &= Check(report, illegal == 0, "每次回填都合法（实际非法 " + illegal + " 次）");

            report.Add("      ~ 局数 " + games + " · 座位0 胜 " + wins0 + " · 座位1 胜 " + wins1
                       + " · 平局 " + draws);
            report.Add("      ~ 平均回合 " + (games > 0 ? (turns / (double)games).ToString("F2") : "-")
                       + " · 最长回合 " + maxTurns);
            report.Add("      ~ 开局判定出的流派分布：高攻 " + archetypes[0] + " · 双发 " + archetypes[1]
                       + " · 连击 " + archetypes[2] + " · 减速 " + archetypes[3]);
            report.Add("      ~ 旧 AI 对照（同 " + games + " 个种子）：未跑完 " + legacyStall
                       + " 局 · 最长回合 " + legacyMaxTurns);

            return ok;
        }

        /// <summary>回填是否合法：座位一致、序号都在范围内、光环项确实指向光环。</summary>
        private static bool IsLegal(DecisionRequest req, DecisionResponse resp)
        {
            if (resp == null || resp.Seat != req.Seat)
            {
                return false;
            }

            int[] options = resp.OptionIndices ?? new int[0];
            for (int i = 0; i < options.Length; i++)
            {
                if (req.Get(options[i]) == null)
                {
                    return false;
                }
            }

            int[] auras = resp.AuraOptionIndices ?? new int[0];
            for (int i = 0; i < auras.Length; i++)
            {
                Option o = req.Get(auras[i]);
                if (o == null || o.AuraSource == null)
                {
                    return false;
                }
            }

            return true;
        }

        // ── ④ 双发「行动修正」（2026-10-06）──────────────────────

        /// <summary>
        /// 双发「行动修正」自测：<b>张数 → 概率</b> + <b>同一次进攻只掷一次骰</b>。
        ///
        /// <para><b>为什么必须在这里钉</b>：这条规则是<b>概率</b>的 —— 写错了（把 40% 与 80%
        /// 弄反、张数阈值判错）不会报错、不会崩，只会让 AI「手感不对」；
        /// 而万局自测跑的是 <see cref="SimpleAiAgent"/>，一次都走不到这段代码。</para>
        ///
        /// <para><b>怎么在没有真实进攻的情况下测</b>：直接造一份
        /// <see cref="DecisionRequest"/>（<c>ChooseDefense</c> + <c>ContextDouble</c>）——
        /// 引擎发本拍时填的就是这几个字段（见 <c>BattleEngine.IssueDefense</c>），
        /// 选项也走同一份 <see cref="DefenseResolver.BuildOptions"/>。手牌由
        /// <see cref="FillHand"/> 直接写进 <c>PlayerState.Hand</c>，想摆几张挡牌就摆几张。</para>
        ///
        /// <para><b>怎么分辨「闸门放行」与「闸门拦下」</b>：统计用的挡牌一律选<b>真挡牌</b>
        /// （自身防御力量就 ≥ 进攻力量，不依赖光环补值）—— 这样闸门放行时必然挑得出两张、
        /// 回填里一定有牌；拦下时只剩 Skip。若用「靠自带防御光环才达标」的牌，
        /// 闸门放行后管线仍可能找不到合法组合，两种结果都返回 Skip 就分不开了。
        /// 「自带防御光环算不算数」由第 ① 条纯函数断言单独覆盖。</para>
        /// </summary>
        private static bool CheckDoubleDefenseCorrection(List<string> report)
        {
            bool ok = true;
            const int AttackPower = 8;

            BattleEngine engine = MakeEngine(4300);

            // ① 纯函数：手牌里「单张就能挡住该双发」的牌有几张。
            //    g(9) / f(9) 力量达标；af(5 + 自带防御光环 4 = 9) 靠光环达标；
            //    ak(6) 靠「守护」无条件挡；c(3) / t(3) 两张不算 ⇒ 共 5 张。
            FillHand(engine.State, 1, "g", "f", "af", "ak", "c", "t");
            var probe = new DefenseProbe(engine.State);
            int blockers = probe.CountBlockers(1, AttackPower);
            ok &= Check(report, blockers == 4,
                "挡牌数 = 防御力量达标(g/f) + 自带防御光环(af) + 守护(ak)，共 4 张（实际 " + blockers + "）");

            // 对照：把「靠光环达标」的 af 换成 c(3) ⇒ 应少一张。
            FillHand(engine.State, 1, "g", "f", "c", "ak", "t");
            blockers = probe.CountBlockers(1, AttackPower);
            ok &= Check(report, blockers == 3,
                "去掉靠光环达标的 af → 挡牌数落到 3（实际 " + blockers + "）");

            // ② 张数 → 概率。四组手牌只差挡牌张数，其余都是凑数的弱牌。
            ok &= CheckDefenseRate(report, engine, AttackPower,
                new[] { "g", "c", "k", "t", "l", "m" }, 1, 0);
            ok &= CheckDefenseRate(report, engine, AttackPower,
                new[] { "g", "f", "c", "k", "t", "l" }, 2, HeuristicAgent.DoubleDefendChanceAtTwo);
            ok &= CheckDefenseRate(report, engine, AttackPower,
                new[] { "g", "f", "i", "c", "k", "t", "l" }, 3, HeuristicAgent.DoubleDefendChanceAtThree);
            ok &= CheckDefenseRate(report, engine, AttackPower,
                new[] { "g", "f", "i", "j", "c", "k", "t", "l" }, 4, 100);

            // ③ 同一次进攻只掷一次骰：引擎因「报备光环」重发本拍时会再问一次，
            //    两次必须给出同一个答案 —— 否则表现是「报备完光环又放弃」。
            //    这里对同一份 request 连问两次，覆盖两个会掷骰的档位。
            int flips = 0;
            var hands = new[]
            {
                new[] { "g", "f", "c", "k", "t", "l" },
                new[] { "g", "f", "i", "c", "k", "t", "l" },
            };

            for (int h = 0; h < hands.Length; h++)
            {
                for (int k = 0; k < 200; k++)
                {
                    FillHand(engine.State, 1, hands[h]);
                    var once = new DefenseProbe(engine.State) { DoubleDefendSeed = 900001 + k * 104729 };
                    DecisionRequest req = MakeDoubleRequest(engine, 1, AttackPower);
                    if (Defended(req, once.Decide(req)) != Defended(req, once.Decide(req)))
                    {
                        flips++;
                    }
                }
            }

            ok &= Check(report, flips == 0, "同一次进攻重发本拍不翻盘（实际翻盘 " + flips + " 次）");

            // ④ 不是双发 → 这条修正完全不介入（挡住就交牌）。
            int notDoubleSkips = 0;
            for (int k = 0; k < 100; k++)
            {
                FillHand(engine.State, 1, "g", "f", "i", "c", "k", "t", "l");
                var plain = new DefenseProbe(engine.State) { DoubleDefendSeed = 300001 + k * 65537 };
                DecisionRequest req = MakeDoubleRequest(engine, 1, AttackPower);
                req.ContextDouble = false;
                if (!Defended(req, plain.Decide(req)))
                {
                    notDoubleSkips++;
                }
            }

            ok &= Check(report, notDoubleSkips == 0,
                "非双发的防御不受影响（100 次全部照旧交牌，实际放弃 " + notDoubleSkips + " 次）");

            // ⑤ 例外：手上有**可用的免疫光环** → 不掷骰、恒防（且管线真的把那枚免疫交出去）。
            //    手牌只摆 1 张真挡牌 ⇒ 按张数规则该 0% 防，所以「恒防」只可能来自免疫例外。
            int immuneSkipped = 0;
            int immuneUsed = 0;
            for (int k = 0; k < 200; k++)
            {
                FillHand(engine.State, 1, "g", "c", "k", "t", "l", "m");
                var withImmune = new DefenseProbe(engine.State) { DoubleDefendSeed = 770001 + k * 7919 };
                DecisionRequest req = MakeDoubleRequest(engine, 1, AttackPower);
                Option immune = AddImmuneAuraOption(engine, req, 1, "ak");

                DecisionResponse resp = withImmune.Decide(req);
                bool usedImmune = resp != null && resp.AuraOptionIndices != null
                                  && resp.AuraOptionIndices.Length == 1
                                  && resp.AuraOptionIndices[0] == immune.Index;

                if (usedImmune)
                {
                    immuneUsed++;
                }
                else if (!Defended(req, resp))
                {
                    immuneSkipped++;
                }
            }

            ok &= Check(report, immuneSkipped == 0 && immuneUsed == 200,
                "有可用免疫光环 → 恒防且真的交出它（200 次：交出 " + immuneUsed
                + " 次、放弃 " + immuneSkipped + " 次）");

            // 对照：把免疫选项拿掉，同一份手牌（只 1 张挡牌）回到「必被破」。
            int plainSkips = 0;
            for (int k = 0; k < 100; k++)
            {
                FillHand(engine.State, 1, "g", "c", "k", "t", "l", "m");
                var plain = new DefenseProbe(engine.State) { DoubleDefendSeed = 770001 + k * 7919 };
                DecisionRequest req = MakeDoubleRequest(engine, 1, AttackPower);
                if (!Defended(req, plain.Decide(req)))
                {
                    plainSkips++;
                }
            }

            ok &= Check(report, plainSkips == 100,
                "对照组：同一份手牌没有免疫光环时 100 次全部放弃（实际放弃 " + plainSkips + " 次）");

            return ok;
        }

        /// <summary>
        /// 统计 <paramref name="expectedBlockers"/> 张挡牌下的防御率，与
        /// <paramref name="expectedPercent"/>（%）比对。
        ///
        /// <para>每个样本换一个 <see cref="HeuristicAgent.DoubleDefendSeed"/> ——
        /// 「同一次进攻只掷一次」靠 <c>_gambleKey</c> 缓存实现，所以同一个 agent 不可能在
        /// 同一个局面上给出两个答案；要拿分布只能换种子。</para>
        ///
        /// <para>容差 ±8%：500 个样本下 p = 0.4 的 3σ ≈ ±6.6%、p = 0.8 的 3σ ≈ ±5.4%。
        /// 它是<b>统计</b>断言，不是精确值；只要不把 40 / 80 弄反就不会误报。</para>
        /// </summary>
        private static bool CheckDefenseRate(List<string> report, BattleEngine engine, int attackPower,
            string[] hand, int expectedBlockers, int expectedPercent)
        {
            const int Samples = 500;
            int defended = 0;
            int wrongCount = 0;

            for (int k = 0; k < Samples; k++)
            {
                FillHand(engine.State, 1, hand);
                var probe = new DefenseProbe(engine.State) { DoubleDefendSeed = 600001 + k * 7919 };
                if (probe.CountBlockers(1, attackPower) != expectedBlockers)
                {
                    wrongCount++;
                }

                DecisionRequest req = MakeDoubleRequest(engine, 1, attackPower);
                if (Defended(req, probe.Decide(req)))
                {
                    defended++;
                }
            }

            double percent = defended * 100.0 / Samples;
            bool ok = wrongCount == 0 && Math.Abs(percent - expectedPercent) <= 8.0;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "挡牌 " + expectedBlockers + " 张 → 防御率 " + percent.ToString("F1") + "%"
                       + "（期望 " + expectedPercent + "%，±8%）"
                       + (wrongCount > 0 ? "，另有 " + wrongCount + " 个样本的挡牌数算错" : string.Empty));
            return ok;
        }

        /// <summary>
        /// 回填是否「真的交牌防御了」（而不是放弃）。
        ///
        /// <para><b>⚠ 不能只看 <c>OptionIndices.Length &gt; 0</c></b>：放弃防御就是回填
        /// <b>Skip 选项的序号</b>，长度同样是 1 —— 那样写会让「放弃」与「交牌」都算成防住，
        /// 每条概率断言都会 PASS（本场景第一版就是这么写的，实测 0%/40%/80% 三档全部 100%）。</para>
        /// </summary>
        private static bool Defended(DecisionRequest req, DecisionResponse resp)
        {
            if (resp == null || resp.OptionIndices == null || resp.OptionIndices.Length == 0)
            {
                return false;
            }

            Option picked = req.Get(resp.OptionIndices[0]);
            return picked != null && !picked.IsSkip;
        }

        /// <summary>
        /// 往请求的选项表里塞一枚<b>免疫光环</b>选项（石化 ak 的 α 光环：免疫力量 ≥6 的攻击，含双发）。
        ///
        /// <para><b>为什么自测要自己造它</b>：那类选项由 <c>BattleEngine.BuildDefenseAuras</c>
        /// 生成 —— 它挂在冷却区里、且要按本次攻击力量过滤区间（区间不含就不给）。
        /// 直接造一份 state + 冷却区也能做，但那是另一条路径；这里只要一个「选项表里确实有可用免疫」
        /// 的局面，就按同一形状手工加一项。</para>
        ///
        /// <para>返回加进去的那一项（调用方按 <c>Index</c> 核回填）。</para>
        /// </summary>
        private static Option AddImmuneAuraOption(BattleEngine engine, DecisionRequest req,
            int seat, string sourceCardId)
        {
            var source = new CardInstance(CardLibrary.Get(sourceCardId), seat);
            source.ToHand();

            var list = new List<Option>();
            for (int i = 0; i < req.Options.Count; i++)
            {
                list.Add(req.Options[i]);
            }

            var option = new Option
            {
                Index = list.Count,
                Kind = OptionKind.UseAura,
                Seat = seat,
                Card = source,
                AuraSource = source,
                AuraKind = AuraKind.ImmuneHigh,
                Value = 6,
                Label = "免疫力量 ≥6 的攻击",
            };

            list.Add(option);
            req.Options = list;
            return option;
        }

        /// <summary>造一个「双发防御」的决策请求（字段与 <c>BattleEngine.IssueDefense</c> 同口径）。</summary>
        private static DecisionRequest MakeDoubleRequest(BattleEngine engine, int seat, int attackPower)
        {
            return new DecisionRequest
            {
                Seat = seat,
                Kind = RequestKind.ChooseDefense,
                MinSelect = 0,
                MaxSelect = 1,
                ContextPower = attackPower,
                ContextDouble = true,
                Options = DefenseResolver.BuildOptions(engine.State, seat, attackPower, true),
            };
        }

        /// <summary>建一个跑起来（发过牌）的引擎；后续只用它的 <see cref="BattleState"/> 当容器。</summary>
        private static BattleEngine MakeEngine(int seed)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            engine.Start();
            return engine;
        }

        /// <summary>
        /// 直接改写某个座位的手牌（自测专用）。
        ///
        /// <para><b>⚠ 必须走 <see cref="CardInstance.ToHand"/></b>：不调它的话
        /// <c>EffectivePower</c> 是 0，所有牌都会被数成「挡不住」——
        /// 断言会以「全部不防」的形式失败，看不出是这里的问题。</para>
        /// </summary>
        private static void FillHand(BattleState state, int seat, params string[] ids)
        {
            PlayerState player = state.Of(seat);
            player.Hand.Clear();
            for (int i = 0; i < ids.Length; i++)
            {
                var card = new CardInstance(CardLibrary.Get(ids[i]), seat);
                card.ToHand();
                player.Hand.Add(card);
            }
        }

        /// <summary>把 <c>protected</c> 的「数挡牌」暴露出来供断言（不改任何规则）。</summary>
        private sealed class DefenseProbe : HeuristicAgent
        {
            public DefenseProbe(BattleState state) : base(state)
            {
            }

            public int CountBlockers(int seat, int attackPower)
            {
                return CountDoubleDefenders(seat, attackPower, DefenseStyle.Cheapest);
            }
        }

        private static bool Check(List<string> report, bool ok, string label)
        {
            report.Add((ok ? "  [PASS] " : "  [FAIL] ") + label);
            return ok;
        }
    }
}
