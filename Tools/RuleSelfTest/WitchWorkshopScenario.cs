using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-01 · <b>女巫的工坊（特殊强化）</b>的定点断言。
    ///
    /// <para><b>为什么这条线要单独脚本化</b>：与 <see cref="CardUpgradeScenario"/> 同一个理由 ——
    /// 特殊强化发生在战斗之外，<b>完全不进万局随机对局</b>，万局统计一个缺陷也抓不到。
    /// 而且它的失效方式全是无声的：</para>
    /// <list type="bullet">
    /// <item><description>卡池门槛写成 <c>&gt; 8</c> 而不是 <c>&gt;= 9</c> → 8 张时照样弹界面，
    /// 玩家能把起始牌池吃掉一张；</description></item>
    /// <item><description>「只有一个效果」判成 <c>&lt;= 1</c> → 0 条效果的牌（如果有）也合格；
    /// 判成「不含光环」→ 纯光环牌被误拒；</description></item>
    /// <item><description>没判 <see cref="CardUpgrade.IsUpgraded"/> → 已经强化过的牌能再被强化一次；</description></item>
    /// <item><description>「两张不能相同」漏判 → 玩家把同一张牌同时放进两个空位，
    /// 确认之后那张牌被消耗掉、什么也没得到。</description></item>
    /// </list>
    ///
    /// <para>断言分四层：① 卡池门槛的边界；② 目标判据的三条拒绝 + 一条放行；
    /// ③ 全表扫描（判据必须与「恰好 1 条效果 且 未强化」逐张对齐）；
    /// ④ 两个空位一起看（缺牌 / 同 ID / 献祭位不限制强化）。</para>
    /// </summary>
    public static class WitchWorkshopScenario
    {
        public static bool Run(List<string> report)
        {
            var bad = new List<string>();
            var notes = new List<string>();

            CheckPoolGate(bad);
            CheckTargetBoundaries(bad, notes);
            CheckWholeTable(bad, notes);
            CheckPair(bad);

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "女巫的工坊（卡池门槛 / 单效果目标 / 两个空位不可相同）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                report.Add("      ~ " + notes[i]);
            }

            return bad.Count == 0;
        }

        // ── ① 卡池门槛 ──────────────────────────────────────────────

        private static void CheckPoolGate(List<string> bad)
        {
            // 用户 2026-10-01 口径「只有 8 张牌 → 无法进行特殊强化」。
            // 8 以下同样禁止（确认过：判据是 ≤ 8，不是 == 8）。
            if (WitchWorkshop.CanOpen(7))
            {
                bad.Add("卡池 7 张却放行（应禁止）");
            }

            if (WitchWorkshop.CanOpen(WitchWorkshop.MinPoolSize - 1))
            {
                bad.Add("卡池 8 张却放行 —— 这会允许玩家把起始那 8 张家底吃掉一张");
            }

            if (!WitchWorkshop.CanOpen(WitchWorkshop.MinPoolSize))
            {
                bad.Add("卡池 9 张却被拒（应放行）");
            }

            if (WitchWorkshop.CanOpen(0))
            {
                bad.Add("空卡池却放行");
            }

            string reason = WitchWorkshop.OpenBlockedReason(8);
            if (string.IsNullOrEmpty(reason))
            {
                bad.Add("卡池不足时没有给原因 —— 场景里会弹一个什么都不说的空提示");
            }
        }

        // ── ② 目标判据的边界 ────────────────────────────────────────

        private static void CheckTargetBoundaries(List<string> bad, List<string> notes)
        {
            string reason;

            if (WitchWorkshop.CanBeTarget(null, out reason))
            {
                bad.Add("空牌却判定为目标合格");
            }

            // 动态挑牌，不写死卡 ID（卡表会扩容，写死 ID 的断言总要回来改）。
            CardDef single = null;      // 恰好 1 条效果
            CardDef multi = null;       // ≥ 2 条效果
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                if (single == null && def.Effects.Count == 1)
                {
                    single = def;
                }

                if (multi == null && def.Effects.Count >= 2)
                {
                    multi = def;
                }
            }

            if (single == null)
            {
                bad.Add("卡表里找不到一张「恰好 1 条效果」的牌 —— 本场景将永远无法开工");
            }
            else if (!WitchWorkshop.CanBeTarget(single, out reason))
            {
                bad.Add("单效果牌被误拒：" + single.Name + "（" + single.Id + "）—— " + reason);
            }

            if (multi == null)
            {
                notes.Add("卡表里没有「≥ 2 条效果」的牌，多效果拒绝这一支本次没取到样本");
            }
            else if (WitchWorkshop.CanBeTarget(multi, out reason))
            {
                bad.Add("多效果牌被误判合格：" + multi.Name + "（" + multi.Id + "）有 "
                        + multi.Effects.Count + " 条效果");
            }

            // 强化过的牌不能当目标：拿一张单效果牌现造一个强化版。
            if (single != null)
            {
                CardDef upgraded = CardUpgrade.Apply(single, 30000);
                if (WitchWorkshop.CanBeTarget(upgraded, out reason))
                {
                    bad.Add("强化过的牌被误判合格：" + upgraded.Name + "（" + upgraded.Id + "）");
                }

                // 但**献祭位不限制**：强化过的牌照样可以被吃掉（用户 2026-10-01 口径：第一个空位无限制）。
                if (!WitchWorkshop.CanBeSacrifice(upgraded))
                {
                    bad.Add("强化过的牌被拒绝放进献祭位：" + upgraded.Id
                            + "（第一个空位本应不限牌）");
                }
            }

            if (WitchWorkshop.CanBeSacrifice(null))
            {
                bad.Add("空牌却判定为可以献祭");
            }
        }

        // ── ③ 全表扫描 ──────────────────────────────────────────────

        private static void CheckWholeTable(List<string> bad, List<string> notes)
        {
            int eligible = 0;
            int rejected = 0;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                string reason;
                bool can = WitchWorkshop.CanBeTarget(def, out reason);

                // 口径：**恰好 1 条效果**（光环也算一条）且**从未被强化过**。
                bool expected = def.Effects.Count == 1 && !CardUpgrade.IsUpgraded(def);
                if (can != expected)
                {
                    bad.Add("判据不符：" + def.Name + "（" + def.Id + "）效果 "
                            + def.Effects.Count + " 条 / 光环 " + def.AuraTokenCount
                            + " 枚 → CanBeTarget=" + can + "，应为 " + expected);
                    continue;
                }

                if (!can)
                {
                    rejected++;
                    if (string.IsNullOrEmpty(reason))
                    {
                        bad.Add("拒绝时没有给原因：" + def.Name + "（" + def.Id + "）"
                                + "—— 界面会显示一张灰卡却写不出为什么");
                    }

                    continue;
                }

                eligible++;
            }

            // ⚠ 不写死张数：卡表每加一张牌、或某张牌的效果条数被改动，这个数就会变。
            //   打印出来是为了让人一眼看到「这一轮有多少张牌能当目标」，
            //   真出问题时上面的逐张比对会先红。
            notes.Add("全表 " + CardLibrary.Count + " 张：可作目标 " + eligible
                      + " 张，被拒 " + rejected + " 张");
        }

        // ── ④ 两个空位一起看 ────────────────────────────────────────

        private static void CheckPair(List<string> bad)
        {
            string reason;

            CardDef a = null;
            CardDef b = null;
            for (int i = 0; i < CardLibrary.Count && b == null; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                if (def.Effects.Count != 1)
                {
                    continue;
                }

                if (a == null)
                {
                    a = def;
                }
                else
                {
                    b = def;
                }
            }

            if (a == null || b == null)
            {
                bad.Add("卡表里凑不出两张「恰好 1 条效果」的牌，两个空位的断言无法执行");
                return;
            }

            if (WitchWorkshop.BothSlotsFilled(a, null))
            {
                bad.Add("只填了一个空位却判定为「两个都填好了」");
            }

            if (WitchWorkshop.BothSlotsFilled(a, b) == false)
            {
                bad.Add("两个空位都填了却判定为「没填好」");
            }

            // 同一张牌：两个空位都放它 → 不能确认
            if (WitchWorkshop.CanConfirm(a, a, out reason))
            {
                bad.Add("两个空位放了同一张牌（" + a.Name + "）却允许确认 —— "
                        + "确认之后这张牌会被消耗掉、玩家什么也没得到");
            }
            else if (string.IsNullOrEmpty(reason))
            {
                bad.Add("两个空位同牌被拒时没有给原因");
            }

            // 两张不同的牌 → 可以确认
            if (!WitchWorkshop.CanConfirm(a, b, out reason))
            {
                bad.Add("两张不同的单效果牌（" + a.Name + " / " + b.Name + "）却不能确认 —— " + reason);
            }

            if (WitchWorkshop.CanConfirm(null, b, out reason))
            {
                bad.Add("献祭位空着却允许确认");
            }

            if (WitchWorkshop.CanConfirm(a, null, out reason))
            {
                bad.Add("目标位空着却允许确认");
            }

            // 消耗的永远是**第一个**空位那张。
            if (WitchWorkshop.ConsumedId(a) != a.Id)
            {
                bad.Add("ConsumedId 返回的不是献祭位那张：" + WitchWorkshop.ConsumedId(a));
            }

            if (WitchWorkshop.ConsumedId(null) != null)
            {
                bad.Add("ConsumedId(null) 应返回 null");
            }
        }
    }
}
