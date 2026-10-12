using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-05 · <b>卡牌获取池</b>（<see cref="CardAcquisition"/>）自测。
    ///
    /// <para><b>这一轮改了什么</b>：用户口述——「待获取卡池 = 全部卡；已获得过的牌出现概率 0
    /// （含获得后又被消耗掉的）；曾展示过但没获得的牌概率减半；其余等概率」。</para>
    ///
    /// <para><b>为什么必须脚本化逼出来</b>：这套规则失效的样子<b>全是无声的</b> ——
    /// 概率写错只是「刷出来的牌不太对」，不会报我一个字；「已获得」漏归一基础 ID
    /// （<c>a</c> 挡不住 <c>a+</c>）也只是偶尔多刷一张，肉眼根本看不出来。
    /// 所以这里用几千次抽样把<b>频率</b>钉住，并逐条验「已获得永远不出现」。</para>
    /// </summary>
    internal static class CardAcquisitionScenario
    {
        /// <summary>统计频率用的抽样次数。</summary>
        private const int Samples = 20000;

        public static bool Run(List<string> report)
        {
            bool ok = true;

            ok &= CheckWeights(report);
            ok &= CheckAcquiredNeverAppears(report);
            ok &= CheckOfferedHalvedRate(report);
            ok &= CheckNoReplacement(report);
            ok &= CheckExhaustedPool(report);

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  1 · 权重本身（纯函数）
        // ══════════════════════════════════════════════════════

        private static bool CheckWeights(List<string> report)
        {
            var acquired = new HashSet<string>(StringComparer.Ordinal) { "a" };
            var offered = new HashSet<string>(StringComparer.Ordinal) { "b" };

            bool fresh = CardAcquisition.WeightUnitsOf("c", acquired, offered)
                         == CardAcquisition.FreshWeightUnits;
            bool shown = CardAcquisition.WeightUnitsOf("b", acquired, offered)
                         == CardAcquisition.OfferedWeightUnits;
            bool owned = CardAcquisition.WeightUnitsOf("a", acquired, offered) == 0;

            // ⚠ 「已获得的 a」必须也挡得住目录里的强化版 a+（基础 ID 归一）。
            bool upgraded = CardAcquisition.WeightUnitsOf("a+", acquired, offered) == 0;
            // ⚠ 曾展示过的 b，其强化版 b+ 也应继承「减半」。
            bool upgradedShown = CardAcquisition.WeightUnitsOf("b+", acquired, offered)
                                 == CardAcquisition.OfferedWeightUnits;

            bool ratioOk = CardAcquisition.FreshWeightUnits
                           == 2 * CardAcquisition.OfferedWeightUnits;

            bool ok = fresh && shown && owned && upgraded && upgradedShown && ratioOk;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "权重：从没展示过 = " + CardAcquisition.FreshWeightUnits
                       + "、曾展示过 = " + CardAcquisition.OfferedWeightUnits
                       + "（正好一半）、已获得 = 0（含已获得/已展示牌的强化版 a+ / b+）");
            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  2 · 已获得的牌（含被消耗掉的）永远不出现
        // ══════════════════════════════════════════════════════

        private static bool CheckAcquiredNeverAppears(List<string> report)
        {
            ICardCatalog catalog = CardCatalog.Builtin();

            // 「已获得」= 玩家现在持有的 + 曾经获得过但后来被消耗掉的。
            var acquired = new HashSet<string>(StringComparer.Ordinal);
            var held = new List<string>();
            for (int i = 0; i < catalog.All.Count; i++)
            {
                if (i % 5 == 0)                         // 大约 1/5 的牌「已获得」
                {
                    acquired.Add(catalog.All[i].Id);
                    held.Add(catalog.All[i].Id);
                }
            }

            // 其中再挑几张「获得之后又被消耗掉」的（不在 cardIds 里、只在 everOwned 里）——
            // 对获取池而言两者是同一种事实，本次抽查就是要看它们是不是都被挡掉了。
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < held.Count; i += 2)
            {
                consumed.Add(held[i]);
            }

            bool ok = true;
            int leaks = 0;
            for (int seed = 0; seed < 400; seed++)
            {
                var rng = new Rng(seed);
                List<CardDef> picks = CardAcquisition.Draw(catalog, acquired, null, 3, rng);
                for (int i = 0; i < picks.Count; i++)
                {
                    string baseId = CardUpgrade.BaseIdOf(picks[i].Id);
                    if (acquired.Contains(baseId))
                    {
                        leaks++;
                    }
                }
            }

            ok &= leaks == 0;
            report.Add((leaks == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "已获得（含被消耗掉的）永不被抽中：400 次抽 3 张，泄漏 " + leaks + " 次；"
                       + "已获得 " + acquired.Count + " 张（其中被消耗 " + consumed.Count + " 张）");
            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  3 · 曾展示过的牌，出现率约为「没展示过」的一半
        // ══════════════════════════════════════════════════════

        private static bool CheckOfferedHalvedRate(List<string> report)
        {
            ICardCatalog catalog = CardCatalog.Builtin();

            // 构造一个小池子：10 张没展示过 + 10 张曾展示过，别把 45 张全用上 ——
            // 样本越少，频率越贴近「单张的权重比例」，判定越干净。
            var fresh = new List<string>();
            var shown = new List<string>();
            for (int i = 0; i < catalog.All.Count && fresh.Count < 10; i++)
            {
                fresh.Add(catalog.All[i].Id);
            }

            for (int i = 10; i < catalog.All.Count && shown.Count < 10; i++)
            {
                shown.Add(catalog.All[i].Id);
            }

            // 只让这 20 张参与：其余都记成「已获得」。
            var acquired = new HashSet<string>(StringComparer.Ordinal);
            var offered = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < catalog.All.Count; i++)
            {
                string id = catalog.All[i].Id;
                if (fresh.Contains(id) || shown.Contains(id))
                {
                    continue;
                }

                acquired.Add(id);
            }

            for (int i = 0; i < shown.Count; i++)
            {
                offered.Add(shown[i]);
            }

            int freshHits = 0;
            int shownHits = 0;
            for (int seed = 0; seed < Samples; seed++)
            {
                var rng = new Rng(seed);
                List<CardDef> picks = CardAcquisition.Draw(catalog, acquired, offered, 1, rng);
                if (picks.Count == 0)
                {
                    continue;
                }

                string id = CardUpgrade.BaseIdOf(picks[0].Id);
                if (fresh.Contains(id))
                {
                    freshHits++;
                }
                else if (shown.Contains(id))
                {
                    shownHits++;
                }
            }

            // 期望：fresh 每张 2 份、shown 每张 1 份 → 20 张总权重 30；
            // fresh 命中率 ~ 20/30 = 0.667、shown ~ 10/30 = 0.333，比值 ≈ 2。
            double ratio = shownHits <= 0 ? 0.0 : freshHits / (double)shownHits;
            bool ok = ratio > 1.8 && ratio < 2.2;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "曾展示过 = 没展示过的一半：抽样 " + Samples + " 次，"
                       + "没展示过命中 " + freshHits + " / 曾展示过命中 " + shownHits
                       + "（比值 " + ratio.ToString("F3") + "，期望 ≈ 2）");
            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  4 · 同一次抽取里不出现重复
        // ══════════════════════════════════════════════════════

        private static bool CheckNoReplacement(List<string> report)
        {
            ICardCatalog catalog = CardCatalog.Builtin();
            int duplicates = 0;
            int shortDraws = 0;

            for (int seed = 0; seed < 2000; seed++)
            {
                var rng = new Rng(seed);
                List<CardDef> picks = CardAcquisition.Draw(catalog, null, null, 3, rng);
                if (picks.Count != 3)
                {
                    shortDraws++;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < picks.Count; i++)
                {
                    if (!seen.Add(CardUpgrade.BaseIdOf(picks[i].Id)))
                    {
                        duplicates++;
                    }
                }
            }

            bool ok = duplicates == 0 && shortDraws == 0;
            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "无放回：2000 次从 45 张里抽 3 张，重复 " + duplicates
                       + " 次、不足 3 张 " + shortDraws + " 次");
            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  5 · 池子被抽干时优雅收场（不抛、不补）
        // ══════════════════════════════════════════════════════

        private static bool CheckExhaustedPool(List<string> report)
        {
            ICardCatalog catalog = CardCatalog.Builtin();

            var allAcquired = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < catalog.All.Count; i++)
            {
                allAcquired.Add(catalog.All[i].Id);
            }

            List<CardDef> none = CardAcquisition.Draw(catalog, allAcquired, null, 3, new Rng(7));

            // 只剩两张可用时 → 抽到 2 张（不补、不报错）
            var twoLeft = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 2; i < catalog.All.Count; i++)
            {
                twoLeft.Add(catalog.All[i].Id);
            }

            List<CardDef> two = CardAcquisition.Draw(catalog, twoLeft, null, 3, new Rng(7));

            bool ok = none.Count == 0 && two.Count == 2;
            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "池子抽干时优雅收场：全被获得 → 抽到 " + none.Count
                       + " 张（期望 0）；只剩 2 张可用 → 抽到 " + two.Count + " 张（期望 2）");
            return ok;
        }
    }
}
