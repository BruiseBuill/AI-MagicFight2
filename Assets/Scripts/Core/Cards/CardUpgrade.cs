using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 卡牌<b>强化</b>的唯一口径（2026-09-30 · 强化场景）。
    ///
    /// <para><b>本批只有一条强化方向：基础力量 +2（封顶 9）</b>，用户 2026-09-30 口径。
    /// 旧的 <c>Docs/design/冒险模式实施规格.md</c> §10 里那版「每牌一次 / ≥2 个方向
    /// / 改冷却 / 改效果」是<b>待实施</b>的 P4U 规格，与本批不是同一件事 ——
    /// 本类只实现这一条，别把两套口径混着改。</para>
    ///
    /// <para><b>为什么放在 Core 而不是视图里</b>：+2 / 封顶 9 / 谁不能强化，是**规则**，
    /// 不是画法。视图只消费 <see cref="CanUpgrade"/> 的布尔与 <see cref="UpgradedPower"/> 的数，
    /// 不自己写「≥9 就跳过」这类判断 —— 否则界面与引擎会各算一份（本工程在
    /// 「攻击预判」上已经定过这条：AI 与预判必须同源）。</para>
    ///
    /// <para><b>为什么不改 <see cref="CardDef"/> 而是造一张新的</b>：<see cref="CardDef"/>
    /// 是不可变的，且它是<b>卡表级的共享定义</b>（敌方同名牌指着同一份）。原地改力量会
    /// 连敌人手里的那张一起改掉。所以强化 = 用同一批效果 / 冷却 / 元素造一份新定义。</para>
    ///
    /// <para><b>⚠ 「新卡」的 ID 与卡面插画</b>：新定义的 <c>Id</c> = 基础 ID 后面挂一个 <c>+</c>
    /// （<c>"a"</c> → <c>"a+"</c>），<b>而 <c>ArtId</c> 保持基础卡原样（<c>"a"</c>）</b> ——
    /// 卡面插画是按 ArtId 查 <c>CardArtLibrary</c> 的，让新 ID 去查会查不到，
    /// 症状是「强化后的牌变一块纯色板」而且零报错。</para>
    /// </summary>
    public static class CardUpgrade
    {
        /// <summary>每次强化的力量增量。</summary>
        public const int PowerStep = 2;

        /// <summary>
        /// 力量上限。<b>「最多只能把基础力量提升到 9」</b>（用户 2026-09-30 口径）。
        ///
        /// <para>口径是<b>结果封顶</b>而不是「只允许 ≤7 的牌」：基础力量 8 的牌照样能选，
        /// 结果是 9（实际只 +1），界面上显示成「8 → 9」。</para>
        /// </summary>
        public const int PowerCap = 9;

        /// <summary>强化卡的 ID / 卡名后缀。</summary>
        public const string Suffix = "+";

        /// <summary>同一个后缀的字符形式（<c>TrimEnd</c> 只吃字符）。</summary>
        public const char SuffixChar = '+';

        /// <summary>这张牌是不是已经强化过的（ID 带后缀）。</summary>
        public static bool IsUpgraded(CardDef def)
        {
            return def != null && def.Id != null
                   && def.Id.EndsWith(Suffix, StringComparison.Ordinal);
        }

        /// <summary>去掉尾部的 <c>+</c>（反复强化时不能越挂越多，见 <see cref="UpgradedId"/>）。</summary>
        public static string BaseId(CardDef def)
        {
            return def == null ? string.Empty : BaseIdOf(def.Id);
        }

        /// <summary>
        /// 同上，但吃的是字符串 ID。
        ///
        /// <para><b>为什么需要它</b>：存档里存的是<b>基础 ID</b>（<c>"a"</c>），而界面上拿到的
        /// 常常是<b>解析后</b>的 ID（<c>"a+"</c>）—— 两边要做「同一张牌」的比较、写入存档前
        /// 要做归一，都得能把后缀削掉。卡池 / 商店 / 女巫工坊三处共用这一处实现。</para>
        /// </summary>
        public static string BaseIdOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            return id.TrimEnd(SuffixChar);
        }

        /// <summary>
        /// 从一份卡池 ID 清单解析出「**最终该用哪几份定义**」：清单里若写着 <c>a</c>，
        /// 而卡表里已经存在 <c>a+</c>（强化卡已落盘），就换成 <c>a+</c>。
        ///
        /// <para><b>这是「强化过一次，下次进来那张牌还是 6 点」的唯一闭环点</b>，
        /// 卡池资产（<c>CardPoolConfig.ResolveIds</c>）与存档（<c>SaveStore</c>）都必须走它 ——
        /// 两边各写一份的下场是「从卡池资产进场景是强化版、从存档进场景又变回基础版」，
        /// 而且零报错。</para>
        ///
        /// <para><b>⚠ 判据是「<paramref name="catalog"/> 里有没有 <c>&lt;id&gt;+</c>」，
        /// 不是「清单里有没有」</b>：清单里永远只写基础 ID，强化版只存在于卡目录的
        /// 「动态卡」区（<c>CardCatalogAsset.generatedCards</c>）。第一版写成看清单，
        /// 于是永远不触发。</para>
        ///
        /// <para>顺带<b>去重</b>（保持首次出现的顺序）：清单里同时有 <c>a</c> 与 <c>a+</c> 时
        /// 只留一份，不会出现「同名牌两张」。</para>
        /// </summary>
        public static IReadOnlyList<string> PreferUpgraded(IEnumerable<string> ids, ICardCatalog catalog = null)
        {
            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            var list = new List<string>();

            if (ids != null)
            {
                foreach (string id in ids)
                {
                    if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                    {
                        list.Add(id);
                    }
                }
            }

            var catalogIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CardDef card in source.All)
            {
                if (card != null)
                {
                    catalogIds.Add(card.Id);
                }
            }

            var kept = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string id = list[i];
                string upgradedId = id + Suffix;
                if (catalogIds.Contains(upgradedId))
                {
                    if (!kept.Contains(upgradedId))
                    {
                        kept.Add(upgradedId);   // 强化版在 → 用强化版
                    }

                    continue;
                }

                kept.Add(id);
            }

            return kept.AsReadOnly();
        }

        /// <summary>强化后的卡 ID。<b>不叠加</b> —— 强化过的牌再强化仍是 <c>"a+"</c>。</summary>
        public static string UpgradedId(CardDef def)
        {
            return BaseId(def) + Suffix;
        }

        /// <summary>强化后的卡名（<c>"暴风雪"</c> → <c>"暴风雪+"</c>）。同样不叠加。</summary>
        public static string UpgradedName(CardDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Name))
            {
                return string.Empty;
            }

            return def.Name.TrimEnd(SuffixChar) + Suffix;
        }

        /// <summary>
        /// 强化后的力量 = <c>min(9, 当前力量 + 2)</c>。
        ///
        /// <para>⚠ 是「<b>当前</b>力量」不是「卡表原值」：强化过的牌再强化，是在它自己身上再加 2。
        /// 因为封顶 9，8 → 9 时增量只有 1，这是有意为之（口径 = 结果封顶）。</para>
        /// </summary>
        public static int UpgradedPower(CardDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            int next = def.Power + PowerStep;
            return next > PowerCap ? PowerCap : next;
        }

        /// <summary>
        /// 这张牌能不能被强化。<paramref name="reason"/> 在返回 <c>false</c> 时给出**给玩家看的原因**
        /// （界面直接在不可选的卡上写它）。
        ///
        /// <para><b>三条拒绝规则</b>（都用卡表里的既有事实判，<b>不写死卡 ID</b>）：</para>
        /// <list type="number">
        /// <item><see cref="CardDef.HiddenPower"/> —— 卡面力量显示为 <c>X</c>（模仿 <c>x</c>）。
        /// 「基础力量为 X 的牌无法提升」（用户口径）；</item>
        /// <item><see cref="CardDef.ForbidsAtkBuff"/> —— 卡面带「此法术的进攻力量不能增加」
        /// （沉重打击 <c>h</c>）。给它 +2 会**说不清**：卡面写着不能增加，界面却变大了；</item>
        /// <item>力量已到 <see cref="PowerCap"/>。</item>
        /// </list>
        ///
        /// <para>⚠ 不判 ID 是刻意的：以后再加一张「力量不能增加」的牌，只要它在卡表里带上
        /// <c>NoAtkBuff</c>，这里自动生效，不需要回来补一个 ID。</para>
        /// </summary>
        public static bool CanUpgrade(CardDef def, out string reason)
        {
            if (def == null)
            {
                reason = "没有这张牌";
                return false;
            }

            if (def.HiddenPower)
            {
                reason = "力量为 X 的牌无法强化";
                return false;
            }

            if (def.ForbidsAtkBuff)
            {
                reason = "此牌的进攻力量不能增加";
                return false;
            }

            if (def.Power >= PowerCap)
            {
                reason = "力量已达上限 " + PowerCap;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 造一张强化后的新定义。<b>除了力量与卡名，其余字段逐项沿用</b> ——
        /// 冷却、效果列表、元素、隐藏力量标记、<b><c>ArtId</c></b> 都不动。
        ///
        /// <para><paramref name="index"/> 是卡表序号口径：新卡不属于内置卡表，
        /// 由调用方给一个离开内置区间的序号（<c>CardCatalogAsset</c> 用的是 10000 起）。</para>
        ///
        /// <para><b>⚠ 调用前必须先过 <see cref="CanUpgrade"/></b>：本方法<b>不重复校验</b>
        /// （只有一个调用点，校验写在入口比在这里再判一次更好读）。</para>
        /// </summary>
        public static CardDef Apply(CardDef def, int index)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            IReadOnlyList<EffectDef> effects = def.Effects ?? new List<EffectDef>();
            return new CardDef(
                index,
                UpgradedId(def),
                UpgradedName(def),
                UpgradedPower(def),
                def.Cooldown,
                effects,
                def.HiddenPower,
                def.ArtId,
                def.Version,
                def.Element);
        }
    }
}
