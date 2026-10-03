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

        private static bool Check(List<string> report, bool ok, string label)
        {
            report.Add((ok ? "  [PASS] " : "  [FAIL] ") + label);
            return ok;
        }
    }
}
