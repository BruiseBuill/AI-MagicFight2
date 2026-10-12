using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-08 · <b>手写出牌优先级</b>（卡池资产上的那张表）的定点断言。
    ///
    /// <para><b>为什么必须单独钉</b>：这张表的失效方式全是<b>无声</b>的 ——
    /// 数值方向反了（大者先打）只表现为「怪打得不合你写的顺序」；
    /// 「未登记的卡回落到自动档位」会让代码里那套四流派档位悄悄插进你的顺序里，
    /// 而卡面上、日志里一个字都看不出来；「基础 ID 折叠」漏了则连强化版都对不上号。
    /// 万局统计跑的是 <c>SimpleAiAgent</c>，这条路径一次都走不到。</para>
    ///
    /// <para>三层：① 表本身的形状（按基础 ID 折叠 / 空行忽略 / 查不到 → 并列最低）；
    /// ② <b>真的接管</b>出牌选择（同一手牌 + 同一流派，开表前后必须挑不同的牌，
    /// 并且「未登记的卡」不许插到手写卡前面）；③ 工厂接线（档案名 + 表一起传到 AI 身上）。</para>
    /// </summary>
    public static class CardPriorityScenario
    {
        public static bool Run(List<string> report)
        {
            report.Add("── 手写出牌优先级（卡池 SO）──");

            bool ok = true;
            ok &= CheckTableShape(report);
            ok &= CheckTakesOverAttack(report);
            ok &= CheckFactoryWiring(report);

            return ok;
        }

        // ── ① 表的形状（纯函数）────────────────────────────────────

        private static bool CheckTableShape(List<string> report)
        {
            bool ok = true;

            // 表里写 f+ 、查 f 也要命中（反过来也一样）：强化版与基础版是同一张牌 ——
            // 少了这条，手牌里那张 a+ 会查不到 a 的优先级，表现为「玩家写了却没生效」。
            CardPriorityTable table = CardPriorityTable.From(new[]
            {
                new CardPriorityEntry { cardId = "a", priority = 0 },
                new CardPriorityEntry { cardId = "f+", priority = 5 },
                new CardPriorityEntry { cardId = "", priority = 9 },
                null,
            });

            ok &= Check(report, table.Count == 2,
                "空 ID / null 行被忽略（表里 2 条，实际 " + table.Count + "）");

            int value;
            ok &= Check(report, table.TryGet("a", out value) && value == 0, "a → 0");
            ok &= Check(report, table.TryGet("a+", out value) && value == 0,
                "查 a+ 也命中 a 那一行（基础 ID 折叠）");
            ok &= Check(report, table.TryGet("f", out value) && value == 5,
                "写成 f+ 的行，查 f 也命中");

            ok &= Check(report, !table.TryGet("g", out value) && value == CardPriorityTable.UnlistedTier,
                "没登记的卡 → 查不到，且档位 = 并列最低（" + CardPriorityTable.UnlistedTier + "）");

            ok &= Check(report, CardPriorityTable.From(null).IsEmpty && CardPriorityTable.Empty.IsEmpty,
                "null / 空表 → IsEmpty（AI 据此走自动档位）");

            return ok;
        }

        // ── ② 真的接管出牌选择 ──────────────────────────────────────

        private static bool CheckTakesOverAttack(List<string> report)
        {
            bool ok = true;

            BattleEngine engine = MakeEngine(4321);

            // 手牌 = 滚石冲击 f(力量 9) + 凝固 c(力量 3)。
            // 不开表时，四个流派的自动档位都会挑 f（高攻按力量序、其余流派靠同档位的 Sub 比力量）。
            FillHand(engine.State, 1, "f", "c");
            var auto = new DoubleAgent(engine.State);
            CardInstance plain = auto.PredictNextAttack(1);
            ok &= Check(report, plain != null && plain.Def.Id == "f",
                "对照组：不开表 → 挑力量 9 的 滚石冲击 f（自动档位）");

            // 开表、把 c 排最前 → 必须挑 c（f 没登记 = 并列最低）
            var manual = new DoubleAgent(engine.State);
            manual.ManualPriorities = CardPriorityTable.From(new[]
            {
                new CardPriorityEntry { cardId = "c", priority = 0 },
            });
            CardInstance picked = manual.PredictNextAttack(1);
            ok &= Check(report, picked != null && picked.Def.Id == "c",
                "开了表（c = 0）→ 挑 凝固 c；未登记的 f 不再靠力量抢位");

            // 反过来：f(1) 比 c(5) 小 → 挑 f（证明顺序确实按表里的数走、小的先）
            var flipped = new DoubleAgent(engine.State);
            flipped.ManualPriorities = CardPriorityTable.From(new[]
            {
                new CardPriorityEntry { cardId = "c", priority = 5 },
                new CardPriorityEntry { cardId = "f", priority = 1 },
            });
            CardInstance flippedPick = flipped.PredictNextAttack(1);
            ok &= Check(report, flippedPick != null && flippedPick.Def.Id == "f",
                "表里 f(1) < c(5) → 挑 f（数值越小越先打）");

            // ★ 关键一条：未登记的卡**不许**回落到自动档位。
            //   冰封铠甲 af 在双发流派的自动档位里是 tier 0（带防御光环）—— 表里只写了 c，
            //   若「没登记 → 走 PriorityOf」，af 会压过 c，玩家写的顺序就被代码改掉了。
            FillHand(engine.State, 1, "af", "c", "f");
            var unlisted = new DoubleAgent(engine.State);
            unlisted.ManualPriorities = CardPriorityTable.From(new[]
            {
                new CardPriorityEntry { cardId = "c", priority = 3 },
            });
            CardInstance unlistedPick = unlisted.PredictNextAttack(1);
            ok &= Check(report, unlistedPick != null && unlistedPick.Def.Id == "c",
                "未登记的卡（af 自动档位 tier 0）不会插到手写卡前面");

            var unlistedAuto = new DoubleAgent(engine.State);
            CardInstance autoPick = unlistedAuto.PredictNextAttack(1);
            ok &= Check(report, autoPick != null && autoPick.Def.Id == "af",
                "对照组：同一手牌不开表 → 挑 冰封铠甲 af（双发流派 tier 0）");

            return ok;
        }

        // ── ③ 工厂接线 ──────────────────────────────────────────────

        private static bool CheckFactoryWiring(List<string> report)
        {
            bool ok = true;

            CardPriorityTable table = CardPriorityTable.From(new[]
            {
                new CardPriorityEntry { cardId = "g", priority = 0 },
            });

            HeuristicAgent agent = MonsterAgentFactory.Create(
                MonsterAgentFactory.Double, null, true, 20261008, table);

            ok &= Check(report, agent is DoubleAgent,
                "档案名 double 仍然建出 DoubleAgent（表没打断工厂分派）");
            ok &= Check(report, ReferenceEquals(agent.ManualPriorities, table),
                "MonsterAgentFactory 把表装到了 agent 身上");

            HeuristicAgent plain = MonsterAgentFactory.Create(MonsterAgentFactory.Double, null);
            ok &= Check(report, plain.ManualPriorities == null,
                "不传表 → 仍是自动档位（老行为不变）");

            return ok;
        }

        // ── 辅助 ────────────────────────────────────────────────────

        /// <summary>建一个跑起来（发过牌）的引擎；后续只用它的 <see cref="BattleState"/> 当容器。</summary>
        private static BattleEngine MakeEngine(int seed)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            engine.Start();
            return engine;
        }

        /// <summary>
        /// 直接改写某个座位的手牌（与 <c>HeuristicAgentScenario.FillHand</c> 同一手法）。
        ///
        /// <para><b>⚠ 必须走 <see cref="CardInstance.ToHand"/></b>：不调它的话
        /// <c>EffectivePower</c> 是 0，按力量排序的档位全部会算错。</para>
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

        private static bool Check(List<string> report, bool ok, string label)
        {
            report.Add((ok ? "  [PASS] " : "  [FAIL] ") + label);
            return ok;
        }
    }
}
