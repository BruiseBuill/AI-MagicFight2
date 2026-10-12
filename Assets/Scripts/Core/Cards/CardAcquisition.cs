using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>卡牌获取池</b>（2026-10-05 · 用户口述规则）—— 「这个玩家接下来还能刷出哪些牌」的唯一口径。
    ///
    /// <para><b>规则</b>（用户原话）：</para>
    /// <list type="number">
    /// <item>待获取卡池 = <b>全部卡的卡池</b>；开局随机发 8 张，这 8 张就从待获取池里变成「已获取」；</item>
    /// <item>任何涉及卡牌获取的场景（战斗奖励 / 商店货架），<b>已获得过的卡出现概率为 0</b>
    /// —— 注意是「获得过」而不是「现在持有」：<b>获得之后又被消耗掉的，也永远不再出现</b>；</item>
    /// <item><b>曾经展示过但没被获得</b>的卡，再次出现的概率<b>减半</b>（固定减半一次，不累积）；</item>
    /// <item>其它卡（从没展示过的）：概率相等。</item>
    /// </list>
    ///
    /// <para><b>为什么权重写成整数（2 / 1）而不是 1.0 / 0.5</b>：本项目 Core 里的随机一律走
    /// <see cref="Rng"/>，而它只提供<b>整数</b>均匀抽样（<c>Next(int)</c>）。把权重放大成
    /// 「从没展示过 = 2、曾展示过 = 1」，总权重就是个整数，一次 <c>rng.Next(总权重)</c>
    /// 落下后按区间取卡 —— 比例精确等于 1 : 0.5，而且**不引入任何浮点**，
    /// 「同种子 → 同一批牌」这条可复现性也就不依赖浮点误差。</para>
    ///
    /// <para><b>⚠ 它只做「抽」，不碰存档</b>：谁被抽出来（= 被展示）由调用方写回存档
    /// （<c>SaveStore.TryRecordOffered</c> / <c>TryGrantCard</c>）——
    /// 那些是「事实记录」，属于 App 层的事。</para>
    ///
    /// <para><b>⚠ 参数是「集合」而不是存档对象</b>：Core 不许引用 App 的
    /// <c>PlayerData</c>（那在另一个程序集里）。调用方把存档里的两张单子翻译成
    /// <c>ICollection&lt;string&gt;</c> 传进来，本类就只认这两张单子。</para>
    /// </summary>
    public static class CardAcquisition
    {
        /// <summary>从没展示过的卡的权重单位（= 1.0）。</summary>
        public const int FreshWeightUnits = 2;

        /// <summary>曾展示过但没获得的卡的权重单位（= <see cref="FreshWeightUnits"/> 的一半 = 0.5）。</summary>
        public const int OfferedWeightUnits = 1;

        /// <summary>展示减半的倍率（给人看的说明用；真正的比例由那两个整数权重表达）。</summary>
        public const float OfferedMultiplier = 0.5f;

        /// <summary>这张卡还能不能出现（出现概率是否 &gt; 0）：被「已获得」单子挡掉的返回 <c>false</c>。</summary>
        public static bool IsAvailable(string id, ICollection<string> acquired)
        {
            if (acquired == null)
            {
                return true;
            }

            return !acquired.Contains(CardUpgrade.BaseIdOf(id));
        }

        /// <summary>
        /// 这张卡的<b>权重单位</b>：被「已获得」挡掉 → <c>0</c>；曾展示过但没获得 →
        /// <see cref="OfferedWeightUnits"/>；从没展示过 → <see cref="FreshWeightUnits"/>。
        ///
        /// <para>比较一律走<b>基础 ID</b>（<see cref="CardUpgrade.BaseIdOf"/>）——
        /// 存档里写的是基础 ID，而目录里可能存在强化版（<c>a+</c>），
        /// 不归一的话「已获得的 a」挡不住「目录里的 a+」，症状是同一张牌又刷出来一次。</para>
        /// </summary>
        public static int WeightUnitsOf(string id, ICollection<string> acquired, ICollection<string> offered)
        {
            string baseId = CardUpgrade.BaseIdOf(id);

            if (acquired != null && acquired.Contains(baseId))
            {
                return 0;
            }

            if (offered != null && offered.Contains(baseId))
            {
                return OfferedWeightUnits;
            }

            return FreshWeightUnits;
        }

        /// <summary>
        /// 从 <paramref name="catalog"/> 里按权重<b>无放回</b>抽 <paramref name="count"/> 张
        /// 互不相同的卡。
        ///
        /// <para>可用卡不足 <paramref name="count"/> 张时就抽到几张算几张（不补、不报错）——
        /// 卡池只剩两三张时，奖励界面自然会空掉后面那几格（<c>ResultWinView.Show</c>
        /// 本来就支持「少于三张」）。</para>
        ///
        /// <para><b>⚠ 抽的时候把「已获得」当硬过滤、把「曾展示」当权重</b>：
        /// 前者是 0 权重、后者是 1/2 —— 两者语义不同，不要合并成一个数字。</para>
        /// </summary>
        public static List<CardDef> Draw(ICardCatalog catalog, ICollection<string> acquired,
            ICollection<string> offered, int count, Rng rng)
        {
            var result = new List<CardDef>();
            if (catalog == null || count <= 0 || rng == null)
            {
                return result;
            }

            // ① 可用池 = 目录里「没被获得过」的那些。
            var pool = new List<CardDef>();
            IReadOnlyList<CardDef> all = catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                CardDef def = all[i];
                if (def == null || !IsAvailable(def.Id, acquired))
                {
                    continue;
                }

                // 同一张牌在目录里只该出现一次；真撞了也按一张算。
                bool dup = false;
                for (int k = 0; k < pool.Count; k++)
                {
                    if (string.Equals(CardUpgrade.BaseIdOf(pool[k].Id), CardUpgrade.BaseIdOf(def.Id),
                            StringComparison.Ordinal))
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                {
                    pool.Add(def);
                }
            }

            int take = count < pool.Count ? count : pool.Count;
            for (int n = 0; n < take; n++)
            {
                int total = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    total += WeightUnitsOf(pool[i].Id, null, offered);   // 已获得的已在上面滤掉
                }

                if (total <= 0)
                {
                    break;   // 理论上到不了（曾展示过的权重是 1，不是 0）
                }

                int roll = rng.Next(total);
                int pick = pool.Count - 1;
                int acc = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += WeightUnitsOf(pool[i].Id, null, offered);
                    if (roll < acc)
                    {
                        pick = i;
                        break;
                    }
                }

                result.Add(pool[pick]);
                pool.RemoveAt(pick);        // 无放回：同一个奖励界面里不会出现两张一样的牌
            }

            return result;
        }
    }
}
