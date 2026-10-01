using System;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>「女巫的工坊」特殊强化</b>的唯一口径（2026-10-01）。
    ///
    /// <para><b>它和 <see cref="CardUpgrade"/> 不是一回事，两套并存</b>：</para>
    /// <list type="bullet">
    /// <item><description>
    ///   <b><see cref="CardUpgrade"/>（P5 · 石台）</b>：免费、一次一张、基础力量 +2 封顶 9，
    ///   <b>不消耗任何牌</b>。
    /// </description></item>
    /// <item><description>
    ///   <b>本类（女巫的工坊 · 水晶球）</b>：<b>献祭一张牌</b>换另一张牌的特殊强化。
    ///   两个空位 —— 左 = 被消耗的牌，右 = 强化目标；目标必须是
    ///   「卡面只有 1 条效果、且从未被强化过」的牌。
    /// </description></item>
    /// </list>
    ///
    /// <para><b>⚠ 本批只做「流程」，不做强化效果</b>（用户 2026-10-01 口径
    /// 「先不做强化的具体效果，之后再做」）：所以这里<b>没有</b> <c>Apply</c> 那种
    /// 「造一张新卡」的方法 —— 确认之后此刻只发生两件事：左边那张牌从卡池里消失、
    /// 节点结束。等效果定了，再往这里加一条「目标牌变成什么」的口径。</para>
    ///
    /// <para><b>为什么这些判据在 Core 而不是视图里</b>：谁能当目标、两个空位能不能确认，
    /// 是<b>规则</b>不是画法。视图只消费布尔量与给玩家看的原因字符串 ——
    /// 与 <see cref="CardUpgrade.CanUpgrade"/> 同一条理由（规则只收敛在一处，
    /// 界面与 AI / 将来的存档校验都调同一个函数）。</para>
    /// </summary>
    public static class WitchWorkshop
    {
        /// <summary>
        /// 能开工的<b>最小卡池张数</b>。
        ///
        /// <para>用户 2026-10-01 口径：「如果玩家当前卡池中<b>只有 8 张牌</b>，
        /// 则改为提示无法进行特殊强化」。8 张正是本作的<b>起始牌池</b>
        /// （<c>ShopPool</c> / <c>UpgradePool</c> 各 8 张）——也就是说
        /// 「这一趟冒险还没额外拿到过牌」时不给做，因为它<b>要吃掉一张牌</b>，
        /// 起始那 8 张是玩家的全部家底。</para>
        ///
        /// <para>判据写成 <b>≤ 8 而不是 == 8</b>（用户 2026-10-01 确认）：
        /// 「只有 8 张」的自然语义是「卡池没有变多」，8 以下同样不该放行。</para>
        /// </summary>
        public const int MinPoolSize = 9;

        /// <summary>本场景要填的空位数（左 = 献祭、右 = 强化目标）。</summary>
        public const int SlotCount = 2;

        // ══════════════════════════════════════════════════════
        //  入口：卡池够不够开
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 卡池够不够开这一趟特殊强化。不够时<b>连界面都不弹</b>，
        /// 直接在场景里提示一句（用户 2026-10-01 口径）。
        /// </summary>
        public static bool CanOpen(int poolCount)
        {
            return poolCount >= MinPoolSize;
        }

        /// <summary>卡池不够时给玩家看的那句话。</summary>
        public static string OpenBlockedReason(int poolCount)
        {
            return "卡池只有 " + poolCount + " 张牌，无法进行特殊强化（至少需要 "
                   + MinPoolSize + " 张）";
        }

        // ══════════════════════════════════════════════════════
        //  第一个空位：献祭
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌能不能放进<b>第一个空位</b>（会被消耗掉的那张）。
        ///
        /// <para>用户 2026-10-01 口径：<b>没有限制</b> —— 卡池里的任何一张都可以献祭，
        /// 包括已经强化过的牌。<b>唯一的约束是两个空位不能是同一张牌</b>，
        /// 那一条由 <see cref="CanConfirm"/> 统一表达（不在这里判，
        /// 因为「两张相同」是<b>一对</b>的性质，不是单张的性质）。</para>
        /// </summary>
        public static bool CanBeSacrifice(CardDef def)
        {
            return def != null;
        }

        // ══════════════════════════════════════════════════════
        //  第二个空位：强化目标
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌能不能放进<b>第二个空位</b>（要被特殊强化的那张）。
        ///
        /// <para>用户 2026-10-01 口径：<b>「当前只有一个效果且没有任何强化的卡」</b>。
        /// 两条都用卡表里已有的事实判，<b>不写死卡 ID</b>：</para>
        /// <list type="number">
        /// <item><see cref="CardDef.Effects"/> <b>恰好 1 条</b> —— 用户 2026-10-01 确认
        /// 「效果条目 = 1 条」，<b>光环也算一条</b>（光环在卡表里就是
        /// <see cref="EffectOp.Aura"/>，与普通效果同属 <c>Effects</c> 列表）。
        /// 所以「一条普通效果 + 一条光环」的牌是 <b>2 条</b>、不合格；纯粹只有
        /// 一条光环的牌是 <b>1 条</b>、合格。</item>
        /// <item><b>从未被强化过</b>（<see cref="CardUpgrade.IsUpgraded"/> 为假）——
        /// 已经挂过 <c>+</c> 的牌不能再来一次。</item>
        /// </list>
        ///
        /// <para>⚠ 刻意<b>没有</b>排除「力量显示为 X」的牌（模仿 <c>x</c>）——
        /// P5 的 <see cref="CardUpgrade.CanUpgrade"/> 要排除它是因为那条口径是
        /// 「基础力量 +2」，X 加不了；本批<b>还没有强化效果</b>，
        /// 不该提前替将来的效果定规矩。等效果定了再回来看这一条。</para>
        /// </summary>
        public static bool CanBeTarget(CardDef def, out string reason)
        {
            if (def == null)
            {
                reason = "这一格还没有选牌";
                return false;
            }

            if (CardUpgrade.IsUpgraded(def))
            {
                reason = "已经是强化过的牌";
                return false;
            }

            int count = def.Effects != null ? def.Effects.Count : 0;
            if (count != 1)
            {
                reason = count == 0
                    ? "这张牌没有效果"
                    : "这张牌有 " + count + " 条效果，只有单效果牌能做目标";
                return false;
            }

            reason = null;
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  两个空位一起看
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 两个空位都填好了没有 —— 只判「有没有」，不判「能不能」。
        ///
        /// <para>⚠ 它<b>不是</b>「能不能点确认」：用户 2026-10-01 口径是
        /// 「两个空位都选择了之后就可以点击确认」，而「两张牌不能相同」
        /// 是<b>确认时的校验</b>（<see cref="CanConfirm"/>）。
        /// 分开两个函数是为了让界面能分别表达「确认键还灰着」与
        /// 「确认键亮着、点了会被拦下来」这两种状态。</para>
        /// </summary>
        public static bool BothSlotsFilled(CardDef sacrifice, CardDef target)
        {
            return sacrifice != null && target != null;
        }

        /// <summary>
        /// 能不能确认。<paramref name="reason"/> 在返回 <c>false</c> 时给出**给玩家看的原因**。
        ///
        /// <para>三条：</para>
        /// <list type="number">
        /// <item>两个空位都得有牌；</item>
        /// <item><b>不能是同一张牌</b>（用户 2026-10-01 口径：「这两个空位当中的牌不可以相同，
        /// 否则无法确认」）—— 按 <see cref="CardDef.Id"/> 比，不按引用；
        /// 卡池是去重后的 ID 清单，所以同 ID 就是同一张牌。</item>
        /// <item>目标牌必须仍然满足 <see cref="CanBeTarget"/>（双保险：界面本来就只让选合格的）。</item>
        /// </list>
        /// </summary>
        public static bool CanConfirm(CardDef sacrifice, CardDef target, out string reason)
        {
            if (sacrifice == null || target == null)
            {
                reason = "两个空位都要选一张牌";
                return false;
            }

            if (string.Equals(sacrifice.Id, target.Id, StringComparison.Ordinal))
            {
                reason = "两个空位不能是同一张牌";
                return false;
            }

            return CanBeTarget(target, out reason);
        }

        /// <summary>
        /// 确认之后，卡池里应该失去哪一张。
        ///
        /// <para>用户 2026-10-01 口径：<b>「第一张牌是会被消耗掉的（从卡池当中移除）」</b>。
        /// 所以返回的是<b>第一个空位</b>那张的 ID；第二个空位那张<b>留在卡池里</b>
        /// （它这次只是被强化，等效果做出来之后是「就地换成新卡」而不是「消失」）。</para>
        ///
        /// <para>算 ID 而不是算对象，是因为将来接上冒险 run 之后，卡池是一份
        /// <c>List&lt;string&gt;</c>，移除要按 ID 走。</para>
        /// </summary>
        public static string ConsumedId(CardDef sacrifice)
        {
            return sacrifice == null ? null : sacrifice.Id;
        }
    }
}
