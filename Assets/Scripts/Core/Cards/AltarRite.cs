using System;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>石台（祭坛）强化</b>的唯一口径（2026-10-06 · 用户口径：献祭一张牌才能力量 +2）。
    ///
    /// <para><b>它是什么</b>：地图上的石台节点进的是强化场景（<c>Upgrade.unity</c>）。
    /// 这一步强化 <b>= 献祭一张牌 + 另一张牌基础力量 +2</b>。原来的口径是
    /// 「免费、一次一张、不消耗任何牌」（用户 2026-09-30），
    /// 2026-10-06 用户改口：「强化时需要选择一张牌作为祭品，然后才能力量加2，
    /// 不然无法强化（类似于女巫）」—— 也就是<b>给「力量 +2」标了个价：一张牌</b>。</para>
    ///
    /// <para><b>为什么它自己一个类，而不是塞进 <see cref="CardUpgrade"/></b>：
    /// <see cref="CardUpgrade"/> 管的是「<b>一根轴怎么算</b>」（力量 / 冷却 / 词条 / 转移），
    /// 与「哪个节点、要付什么代价」无关；女巫的 <see cref="WitchWorkshop"/> 已经先例在
    /// —— 那边也是「判定 + 价钱」自成一处、只<b>调用</b> <see cref="CardUpgrade"/> 合成。
    /// 本类照抄这个分工：<b>本类只判「这一步成不成立」，力量怎么加仍然只有
    /// <see cref="CardUpgrade"/> 一处实现</b>（<see cref="BuildMod"/> 直接返回
    /// <see cref="CardUpgradeMod.Power"/>，没有第二套加法）。</para>
    ///
    /// <para><b>与 <see cref="WitchWorkshop"/> 的两处差别</b>：</para>
    /// <list type="number">
    /// <item>石台给的是<b>力量 +2</b>（走 <see cref="CardUpgrade.CanUpgradePower"/> 那一套判据，
    ///   所以「力量 X 的模仿 / 沉重打击 / 已到上限 9」照样不能当目标）；
    ///   女巫给的是「把一条效果搬过来」，两边的目标门槛完全不同（那边要求「恰好 1 条效果且从未强化过」）；</item>
    /// <item>石台<b>不花金币</b>（用户这次只说「祭品」，没提钱）。</item>
    /// </list>
    ///
    /// <para><b>祭品没有额外限制</b>：任何还持有的牌都可以当祭品 —— 包括已经强化过的、
    /// 力量为 X 的、效果连锁的。理由很直接：祭品只是<b>被拿走</b>，它身上没有任何东西
    /// 需要「单独成立」（女巫那边限制连锁牌，是因为要把它的一条效果搬走）。</para>
    /// </summary>
    public static class AltarRite
    {
        /// <summary>
        /// 能开工的<b>最小卡池张数</b>：<b>2</b>。
        ///
        /// <para>一张当祭品（会被消耗掉）、一张留下来当目标，缺一不可。
        /// ⚠ 与 <see cref="WitchWorkshop.MinPoolSize"/>（9）不同：那个门槛是**保护起始 8 张**
        /// 那一类「家底」用的（女巫是额外奖励节点）；石台是玩家自己选的路，
        /// 烧掉哪张牌是玩家的决定，所以只卡「还剩得下一张能强化」。</para>
        /// </summary>
        public const int MinPoolSize = 2;

        // ══════════════════════════════════════════════════════
        //  入口：卡池够不够开
        // ══════════════════════════════════════════════════════

        /// <summary>卡池够不够开这一次强化。不够时连选牌弹窗都不弹，只提示一句。</summary>
        public static bool CanOpen(int poolCount)
        {
            return poolCount >= MinPoolSize;
        }

        /// <summary>卡池不够时给玩家看的那句话。</summary>
        public static string OpenBlockedReason(int poolCount)
        {
            return "卡池只有 " + poolCount + " 张牌，无法强化（石台要吃掉一张当祭品，至少需要 "
                   + MinPoolSize + " 张）";
        }

        // ══════════════════════════════════════════════════════
        //  两张牌各自的资格
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌能不能当<b>祭品</b>（会被从卡池里消耗掉）。
        ///
        /// <para><paramref name="target"/> 是已经选好的「要强化的那张」（还没有选就传 <c>null</c>）——
        /// 只在<b>两个位置撞成同一张牌</b>时拒绝。同一性按
        /// <see cref="CardUpgrade.BaseIdOf"/> 比（不是按卡面 ID）：
        /// 基础版与它的 <c>+</c> 版是同一张牌，不能一个当祭品、一个当目标。</para>
        /// </summary>
        public static bool CanBeSacrifice(CardDef def, CardDef target, out string reason)
        {
            if (def == null)
            {
                reason = "还没有选祭品";
                return false;
            }

            if (target != null && SameCard(def, target))
            {
                reason = "这是要强化的那张牌，不能同时当祭品";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 这张牌能不能当<b>目标</b>（要被 +2 力量的那张）。
        ///
        /// <para>判据本体在 <see cref="CardUpgrade.CanUpgrade"/>（力量轴）——
        /// 那里才知道「X 的模仿 / 不能增加力量 / 上限 9」是怎么回事。
        /// 这里只多加一条：不能与祭品是同一张牌。</para>
        /// </summary>
        public static bool CanBeTarget(CardDef def, CardDef sacrifice, out string reason)
        {
            if (def == null)
            {
                reason = "还没有选要强化的牌";
                return false;
            }

            if (sacrifice != null && SameCard(def, sacrifice))
            {
                reason = "这是祭品，不能再拿来强化";
                return false;
            }

            return CardUpgrade.CanUpgrade(def, out reason);
        }

        // ══════════════════════════════════════════════════════
        //  两张牌一起看
        // ══════════════════════════════════════════════════════

        /// <summary>两个位置都选好了没有 —— 只判「有没有」，不判「能不能」。</summary>
        public static bool BothPicked(CardDef sacrifice, CardDef target)
        {
            return sacrifice != null && target != null;
        }

        /// <summary>
        /// 能不能确认。<paramref name="reason"/> 在返回 <c>false</c> 时给出**给玩家看的原因**。
        ///
        /// <para><b>四道门</b>（顺序即「先说哪个问题」）：
        /// ① 两个位置都得有牌；② 不能是同一张牌；
        /// ③ 祭品仍然合格（<see cref="CanBeSacrifice"/>，双保险）；
        /// ④ 目标仍然合格（<see cref="CanBeTarget"/> + 力量轴那一笔真的加得上去，
        /// 见 <see cref="CardUpgrade.CanApply"/>）。</para>
        ///
        /// <para><paramref name="targetExisting"/> 是目标牌已有的配方（主存档里的那份；
        /// 没有就传 <c>null</c>）—— 力量轴只看当前力量，所以它通常用不上，
        /// 但传对了将来加「不能重复叠加」的规则时不用改调用点。</para>
        /// </summary>
        public static bool CanConfirm(CardDef sacrifice, CardDef target,
            CardUpgradeRecord targetExisting, out string reason)
        {
            if (sacrifice == null || target == null)
            {
                reason = "祭品与要强化的牌都要选";
                return false;
            }

            if (SameCard(sacrifice, target))
            {
                reason = "祭品不能是要强化的那张牌";
                return false;
            }

            if (!CanBeSacrifice(sacrifice, target, out reason))
            {
                return false;
            }

            if (!CanBeTarget(target, sacrifice, out reason))
            {
                return false;
            }

            return CardUpgrade.CanApply(target, BuildMod(), targetExisting, out reason);
        }

        // ══════════════════════════════════════════════════════
        //  产物
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 造这一笔强化：<b>力量 + <see cref="CardUpgrade.PowerStep"/></b>（= 2）。
        ///
        /// <para>⚠ 它<b>只造数据</b>，不做校验（校验在 <see cref="CanConfirm"/>）——
        /// 与 <see cref="WitchWorkshop.BuildTransferMod"/> 同一分工。
        /// 数值本身没有第二处定义：这里直接借用
        /// <see cref="CardUpgradeMod.Power"/> 与 <see cref="CardUpgrade.PowerStep"/>，
        /// 所以「石台 +2」与「封顶 9」永远与 <see cref="CardUpgrade"/> 同源。</para>
        /// </summary>
        public static CardUpgradeMod BuildMod()
        {
            return CardUpgradeMod.Power(CardUpgrade.PowerStep);
        }

        /// <summary>确认之后卡池里应该失去哪一张：<b>祭品</b>（目标就地变成强化版、不消失）。</summary>
        public static string ConsumedId(CardDef sacrifice)
        {
            return sacrifice == null ? null : sacrifice.Id;
        }

        /// <summary>给人看的一行摘要（确认后的提示语 / 日志共用）。</summary>
        public static string ConfirmSummary(CardDef sacrifice, CardDef target, CardDef upgraded)
        {
            string now = upgraded == null ? target == null ? "?" : target.Id : upgraded.Id;
            return "《" + Name(sacrifice) + "》已被献祭消耗，《" + Name(target) + "》力量 +"
                   + CardUpgrade.PowerStep + "（" + now + "）";
        }

        /// <summary>
        /// 挂在弹窗底部给玩家看的那句：这一步会变成什么样。
        ///
        /// <para>⚠ 要分三种情形写，<b>不能拿「祭品还没选」当普通情形拼句子</b> ——
        /// 那时祭品的名字是 <c>?</c>，拼出来是「确认后《?》会被消耗」，
        /// 玩家读到的第一件事就是「有个东西我不知道是什么」（截图实测过）。
        /// 另外这句是**一行 TMP**，太长会顶到两端的「关闭 / 确认」键上。</para>
        /// </summary>
        public static string Preview(CardDef sacrifice, CardDef target)
        {
            if (target == null)
            {
                return "选一张牌，看它献祭之后会变成什么样";
            }

            if (sacrifice == null)
            {
                return "再点一张牌当祭品：它会从卡池里消失，《" + Name(target) + "》力量 "
                       + target.Power + " → " + CardUpgrade.UpgradedPower(target);
            }

            return "确认后《" + Name(sacrifice) + "》会被消耗，《" + Name(target)
                   + "》力量 " + target.Power + " → " + CardUpgrade.UpgradedPower(target);
        }

        /// <summary>两张牌是不是同一张（按基础 ID 比 —— 基础版与它的 <c>+</c> 版算同一张）。</summary>
        public static bool SameCard(CardDef a, CardDef b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return string.Equals(CardUpgrade.BaseIdOf(a.Id), CardUpgrade.BaseIdOf(b.Id),
                StringComparison.Ordinal);
        }

        private static string Name(CardDef def)
        {
            if (def == null)
            {
                return "?";
            }

            return string.IsNullOrEmpty(def.Name) ? def.Id : def.Name;
        }
    }
}
