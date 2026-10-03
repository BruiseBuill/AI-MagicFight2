using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>「女巫的工坊」特殊强化</b>的唯一口径（2026-10-01 建 · 2026-10-02 补上效果 ·
    /// 2026-10-03 起<b>要花金币</b>）。
    ///
    /// <para><b>它和 <see cref="CardUpgrade"/> 不是一回事，两套并存</b>：</para>
    /// <list type="bullet">
    /// <item><description>
    ///   <b><see cref="CardUpgrade"/>（P5 · 石台）</b>：免费、一次一张、基础力量 +2 封顶 9，
    ///   <b>不消耗任何牌</b>。
    /// </description></item>
    /// <item><description>
    ///   <b>本类（女巫的工坊 · 水晶球）</b>：<b>献祭一张牌</b>换另一张牌的特殊强化。
    ///   两个空位 —— 左 = 被消耗的牌，右 = 强化目标。
    /// </description></item>
    /// </list>
    ///
    /// <para><b>2026-10-02 补上的效果口径（用户原话）</b>：</para>
    /// <list type="number">
    /// <item>「该强化效果实际上是将第 1 张卡牌的一个效果，转移给第 2 张的卡牌，
    ///   也就是使第 2 张卡牌增加一个效果」；</item>
    /// <item>「当第 1 张卡牌只有一个效果时，则将其效果转移给第 2 张。如果第 1 张卡牌拥有多个效果，
    ///   这需要在选择卡牌的界面当中额外，显示一个让玩家选择获得哪一项效果的选项」；</item>
    /// <item>「一些特殊的卡牌，因为他们的效果存在连锁，因此他们没有办法被放到第 1 张。
    ///   例如沉重打击和模仿」。</item>
    /// </list>
    ///
    /// <para><b>效果怎么落地</b>：目标牌身上不是「多了一张牌」，而是
    /// <see cref="CardUpgradeMod.Transfer"/>（转移轴）—— 一条
    /// <see cref="EffectSpec"/> 被并进目标牌的<b>效果列表</b>，其余字段（力量 / 冷却 / 元素 /
    /// 插画）一个都不动，卡面 ID / 卡名只挂一个 <c>+</c>。这与「强化 = 一条配方，
    /// 读的时候合成」是同一套模型（见 <see cref="CardUpgrade"/>）。</para>
    ///
    /// <para><b>为什么这些判据在 Core 而不是视图里</b>：谁能当献祭牌、谁能当目标、
    /// 两个空位能不能确认、<b>这一次要花多少金币</b>，是<b>规则</b>不是画法。
    /// 视图只消费布尔量与给玩家看的原因字符串 ——
    /// 与 <see cref="CardUpgrade.CanUpgrade"/> 同一条理由（规则只收敛在一处，
    /// 界面与 AI / 将来的存档校验都调同一个函数）。</para>
    ///
    /// <para><b>2026-10-03 加的「价钱」</b>：见本文件下半部分
    /// <see cref="CostBase"/> 那一节 —— 公式、四句读法、以及
    /// 「为什么价钱和刚才不一样」（<see cref="CostChangeLine"/>）都在那里。</para>
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

        /// <summary>
        /// <b>连锁算子</b>：它们本身<b>不是一条独立能力</b>，而是「对本牌其它部分」的
        /// 约束 / 倍增 / 接管 —— 单独搬到别的牌上会失去意义，或者给目标挂上一条
        /// 莫名其妙的限制。含任一条的牌<b>整张不能进第一个空位</b>（用户 2026-10-02 第 3 条口径）。
        ///
        /// <list type="bullet">
        /// <item><see cref="EffectOp.NoAtkBuff"/>（沉重打击）—— 「此法术的进攻力量不能增加」
        ///   是对本牌力量的**性质声明**，它不是一条能用出来的能力。搬出去会给目标挂一条
        ///   「不能增加力量」的诅咒；而把它留在原牌上、只搬走「额外伤害」，
        ///   又会把那条效果的代价抽掉。</item>
        /// <item><see cref="EffectOp.Copy"/>（模仿）—— 效果依赖「本牌力量视为 X」这个身份
        ///   （卡面写着「未复制时力量视为 1」），而 X 是**卡级标记**
        ///   （<see cref="CardDef.HiddenPower"/>）搬不走。</item>
        /// <item><see cref="EffectOp.DoubleAtkBonus"/>（飞叶连击）—— 「此法术获得的额外进攻力量翻倍」
        ///   是**倍增器**：没有别的加值来源时它等于一条空效果。</item>
        /// </list>
        ///
        /// <para>⚠ 判据写成算子表而<b>不写卡 ID</b>：以后再加一张带这些算子的牌，
        /// 自动就进不了第一个空位，不需要回来补一个 ID 分支。</para>
        /// </summary>
        private static readonly EffectOp[] ChainedOps =
        {
            EffectOp.NoAtkBuff,
            EffectOp.Copy,
            EffectOp.DoubleAtkBonus,
        };

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
        /// <para>用户 2026-10-02 第 3 条口径：<b>效果「连锁」的牌不能放第一个空位</b>
        /// （见 <see cref="ChainedOps"/>），理由是「转移的效果」必须是一条**能独立成立的效果**。
        /// 除此之外没有别的限制 —— 已经强化过的牌照样能献祭。</para>
        ///
        /// <para>「两个空位不能是同一张牌」不在这里判：那是<b>一对</b>的性质，不是单张的性质，
        /// 统一由 <see cref="CanConfirm"/> 表达。</para>
        /// </summary>
        public static bool CanBeSacrifice(CardDef def, out string reason)
        {
            if (def == null)
            {
                reason = "这一格还没有选牌";
                return false;
            }

            return !IsChained(def, out reason);
        }

        /// <summary>
        /// 这张牌的效果有没有「连锁」—— 有的话不能进第一个空位。
        /// <paramref name="why"/> 给玩家看（直接写在压暗的卡面上）。
        ///
        /// <para>两条判据，都用卡表里已有的事实：</para>
        /// <list type="number">
        /// <item><see cref="CardDef.HiddenPower"/>（力量 X，模仿）—— 效果与卡面绑死；</item>
        /// <item>卡上任意一条效果的算子在 <see cref="ChainedOps"/> 里。</item>
        /// </list>
        /// </summary>
        public static bool IsChained(CardDef def, out string why)
        {
            if (def == null)
            {
                why = null;
                return false;
            }

            if (def.HiddenPower)
            {
                why = "力量为 X 的牌，效果与卡面绑死，不能献祭";
                return true;
            }

            if (def.Effects != null)
            {
                for (int i = 0; i < def.Effects.Count; i++)
                {
                    EffectDef effect = def.Effects[i];
                    if (effect == null)
                    {
                        continue;
                    }

                    if (Contains(effect.Op))
                    {
                        string text = string.IsNullOrEmpty(effect.Text) ? effect.HandlerId : effect.Text;
                        why = "「" + text + "」与这张牌的其它部分连锁，不能单独转移";
                        return true;
                    }
                }
            }

            why = null;
            return false;
        }

        // ══════════════════════════════════════════════════════
        //  第二个空位：强化目标
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌能不能放进<b>第二个空位</b>（要被特殊强化的那张）。
        ///
        /// <para>用户 2026-10-01 口径：<b>「当前只有一个效果且没有任何强化的卡」</b>。
        /// 三条都用卡表里已有的事实判，<b>不写死卡 ID</b>：</para>
        /// <list type="number">
        /// <item><see cref="CardDef.Effects"/> <b>恰好 1 条</b> —— 用户 2026-10-01 确认
        /// 「效果条目 = 1 条」，<b>光环也算一条</b>（光环在卡表里就是
        /// <see cref="EffectOp.Aura"/>，与普通效果同属 <c>Effects</c> 列表）。
        /// 所以「一条普通效果 + 一条光环」的牌是 <b>2 条</b>、不合格；纯粹只有
        /// 一条光环的牌是 <b>1 条</b>、合格。⇒ 转移完成后目标固定是 <b>2 条效果</b>，
        /// 不会越滚越多。</item>
        /// <item><b>从未被强化过</b>（<see cref="CardUpgrade.IsUpgraded"/> 为假）——
        /// 已经挂过 <c>+</c> 的牌不能再来一次。</item>
        /// <item><b>效果不连锁</b>（<see cref="IsChained"/>）—— 2026-10-02 补：目标也要判。
        /// 否则「力量 X 的模仿」会出现在候选里、点了才在确认那一步被拒
        /// （<see cref="CardUpgrade.CanTransfer"/> 也挡它），玩家会以为是界面坏了。</item>
        /// </list>
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

            if (IsChained(def, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  效力选择（第一个空位的牌有 >1 条效果时）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张献祭牌身上<b>可以转移的效果</b>（按卡面书写顺序 = <see cref="CardDef.Effects"/> 的顺序）。
        ///
        /// <para>⚠ 这里<b>不做算子过滤</b>：连锁的牌<b>整张</b>已经在
        /// <see cref="CanBeSacrifice"/> 那一关被挡住了，所以能走到这里的效果天然都不连锁。
        /// 自测里有一条断言专门盯这个「两条规则不打架」。</para>
        /// </summary>
        public static IReadOnlyList<EffectDef> TransferableEffects(CardDef def)
        {
            var list = new List<EffectDef>();
            if (def != null && def.Effects != null)
            {
                for (int i = 0; i < def.Effects.Count; i++)
                {
                    if (def.Effects[i] != null)
                    {
                        list.Add(def.Effects[i]);
                    }
                }
            }

            return list.AsReadOnly();
        }

        /// <summary>
        /// 这张献祭牌要不要玩家<b>额外选一条效果</b>（用户 2026-10-02 第 2 条口径）。
        ///
        /// <para>「只有一个效果」→ <c>false</c>（直接搬）；「多个效果」→ <c>true</c>
        /// （选择界面上多显示一个让玩家选哪一项）。</para>
        /// </summary>
        public static bool NeedsEffectChoice(CardDef sacrifice)
        {
            return TransferableEffects(sacrifice).Count > 1;
        }

        /// <summary>
        /// 不用问玩家时该取第几条效果。
        ///
        /// <para>只有 1 条 → <c>0</c>；0 条或多条 → <c>-1</c>（多条时必须让玩家选，
        /// 由 <see cref="NeedsEffectChoice"/> 决定界面弹不弹那一步）。</para>
        /// </summary>
        public static int AutoEffectIndex(CardDef sacrifice)
        {
            int count = TransferableEffects(sacrifice).Count;
            return count == 1 ? 0 : -1;
        }

        /// <summary>这条效果搬过去之后会是哪一条（越界返回 null）。</summary>
        public static EffectDef EffectAt(CardDef sacrifice, int effectIndex)
        {
            IReadOnlyList<EffectDef> list = TransferableEffects(sacrifice);
            return effectIndex >= 0 && effectIndex < list.Count ? list[effectIndex] : null;
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
        /// 能不能确认（带着「玩家选了第几条效果」）。<paramref name="reason"/> 在返回 <c>false</c>
        /// 时给出**给玩家看的原因**。
        ///
        /// <para>五条：</para>
        /// <list type="number">
        /// <item>两个空位都得有牌；</item>
        /// <item><b>不能是同一张牌</b>（用户 2026-10-01 口径：「这两个空位当中的牌不可以相同，
        ///   否则无法确认」）—— 按 <see cref="CardDef.Id"/> 比，不按引用；</item>
        /// <item>献祭牌要仍然合格（<see cref="CanBeSacrifice"/>，双保险）；</item>
        /// <item>多条效果时必须<b>已经选了一条</b>（<paramref name="effectIndex"/> &lt; 0 就拦下）；</item>
        /// <item>目标牌要仍然合格（<see cref="CanBeTarget"/> + 目标身上<b>还没有</b>这条效果，
        ///   见 <see cref="CardUpgrade.CanTransfer"/>）。</item>
        /// </list>
        /// </summary>
        public static bool CanConfirm(CardDef sacrifice, int effectIndex, CardDef target, out string reason)
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

            if (!CanBeSacrifice(sacrifice, out reason))
            {
                return false;
            }

            if (effectIndex < 0)
            {
                reason = "还要选一条要转移的效果";
                return false;
            }

            EffectDef effect = EffectAt(sacrifice, effectIndex);
            if (effect == null)
            {
                reason = "要转移的效果已经不在《" + sacrifice.Name + "》上了";
                return false;
            }

            if (!CanBeTarget(target, out reason))
            {
                return false;
            }

            return CardUpgrade.CanTransfer(target, EffectSpec.From(effect, sacrifice.Id), out reason);
        }

        /// <summary>
        /// 同上，但**带上玩家现有的金币**（2026-10-03 · 特殊强化改成要花钱）。
        ///
        /// <para>价钱那一条**排在最后**：两个空位没填 / 撞了同一张牌 / 还没选效果 /
        /// 目标已经有这条效果 —— 这些都是「操作还没做对」，先让玩家看见它们；
        /// 只有一切都对、单纯是钱不够时才说钱不够。</para>
        /// </summary>
        public static bool CanConfirm(CardDef sacrifice, int effectIndex, CardDef target, int gold,
            out string reason)
        {
            if (!CanConfirm(sacrifice, effectIndex, target, out reason))
            {
                return false;
            }

            int cost;
            string error;
            if (!TryCost(sacrifice, target, out cost, out error))
            {
                reason = error;
                return false;
            }

            if (!CanAfford(gold, cost))
            {
                reason = CostShortfall(gold, cost);
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 同上，但不带效果序号 —— 单效果牌直接判；多效果牌一律拦下并说明「先选一条」。
        /// </summary>
        public static bool CanConfirm(CardDef sacrifice, CardDef target, out string reason)
        {
            int index = AutoEffectIndex(sacrifice);
            if (sacrifice != null && index < 0)
            {
                reason = "《" + sacrifice.Name + "》有 " + TransferableEffects(sacrifice).Count
                         + " 条效果，先选一条要转移的";
                return false;
            }

            return CanConfirm(sacrifice, index, target, out reason);
        }

        // ══════════════════════════════════════════════════════
        //  产物
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把「从 <paramref name="source"/> 的第 <paramref name="effectIndex"/> 条效果」
        /// 拍成一份可落盘的 <see cref="EffectSpec"/>。越界返回 null。
        /// </summary>
        public static EffectSpec BuildSpec(CardDef source, int effectIndex)
        {
            EffectDef effect = EffectAt(source, effectIndex);
            return effect == null ? null : EffectSpec.From(effect, source.Id);
        }

        /// <summary>
        /// 造这一笔强化（转移轴）—— 写进目标牌的配方里，就是「它多了一条效果」。
        ///
        /// <para>⚠ 它<b>只负责造数据</b>，不做校验（校验在 <see cref="CanConfirm"/>）。
        /// 与 <see cref="CardUpgrade.Apply"/> 的分工一致：判定一处、合成一处。</para>
        /// </summary>
        public static CardUpgradeMod BuildTransferMod(CardDef source, int effectIndex)
        {
            return CardUpgradeMod.Transfer(BuildSpec(source, effectIndex));
        }

        /// <summary>
        /// 确认之后，卡池里应该失去哪一张。
        ///
        /// <para>用户 2026-10-01 口径：<b>「第一张牌是会被消耗掉的（从卡池当中移除）」</b>。
        /// 所以返回的是<b>第一个空位</b>那张的 ID；第二个空位那张<b>留在卡池里</b>
        /// （它这次是「就地变成强化版」而不是「消失」）。</para>
        ///
        /// <para>算 ID 而不是算对象，是因为卡池是一份 <c>List&lt;string&gt;</c>，
        /// 移除要按 ID 走。</para>
        /// </summary>
        public static string ConsumedId(CardDef sacrifice)
        {
            return sacrifice == null ? null : sacrifice.Id;
        }

        private static bool Contains(EffectOp op)
        {
            for (int i = 0; i < ChainedOps.Length; i++)
            {
                if (ChainedOps[i] == op)
                {
                    return true;
                }
            }

            return false;
        }

        // ══════════════════════════════════════════════════════
        //  价钱（2026-10-03 · 用户口径）
        // ══════════════════════════════════════════════════════
        //
        //  特殊强化**要花金币**（原先免费）。价钱的公式是用户给的：
        //
        //      40（基础值）
        //      × 被消耗牌的冷却 ÷ 被消耗牌的力量 ÷ 被消耗牌的效果数量（1 或 2）
        //      × Max(被消耗牌的冷却 − 被强化牌的冷却, 1)
        //
        //  读法（也就是「为什么价钱会变」那四句话）：
        //    ① 冷却在分子上 → 献祭牌**冷却越高越贵**；
        //    ② 力量在分母上 → 献祭牌**力量越高越便宜**；
        //    ③ 效果数在分母上 → 献祭牌**效果越多越便宜**（2 条的比 1 条的便宜一半）；
        //    ④ 最后一项是**两张牌的冷却差**，且下限 1 → 目标冷却**越低**（差越大）**越贵**；
        //       目标冷却 ≥ 献祭牌时差 ≤ 0，被 Max 抬到 1，也就是「不再更便宜」的地板。
        //
        //  ⚠ 判据（谁能献祭 / 谁能当目标）在文件上半部分，**价钱是另一件事**：
        //    价钱只看两张牌身上的四个数，不看别的。所以它是一条纯函数，
        //    界面、AI、将来的日志都调同一个 —— 与「规则只收敛在一处」同一条理由。
        //
        //  ⚠ 除不尽时四舍五入（AwayFromZero），并保证**至少 1 金** ——
        //    公式本身会有小数（例：40×2÷9÷2 = 4.44），价钱是整数。

        /// <summary>价钱的<b>基础值</b>（公式里的 40）。想整体调价只改这一个数。</summary>
        public const int CostBase = 40;

        /// <summary>冷却差那一项的<b>下限</b>（公式里的 <c>Max(…, 1)</c>）。</summary>
        public const int MinCooldownGap = 1;

        /// <summary>这张牌身上有几条效果（null / 空表 = 0）。</summary>
        public static int EffectCount(CardDef def)
        {
            return def == null || def.Effects == null ? 0 : def.Effects.Count;
        }

        /// <summary>
        /// 算这一次特殊强化的价钱。算不出来时返回 <c>false</c> 并给出**给玩家看的原因**
        /// （两个空位没填满 / 力量为 0 / 一条效果都没有）。
        ///
        /// <para><b>⚠ 这两个空位缺一不可</b>：公式里同时用到献祭牌的冷却 / 力量 / 效果数
        /// 与目标牌的冷却 —— 只填了一格时连「大概多少钱」都算不出来
        /// （界面那时显示的是 <see cref="MinCost"/>，即冷却差取下限 1 的底价）。</para>
        /// </summary>
        public static bool TryCost(CardDef sacrifice, CardDef target, out int cost, out string error)
        {
            cost = 0;

            if (sacrifice == null || target == null)
            {
                error = "两个空位都要选一张牌，才算得出价钱";
                return false;
            }

            if (sacrifice.Power <= 0)
            {
                error = "《" + sacrifice.Name + "》的力量是 " + sacrifice.Power + "，价钱算不出来";
                return false;
            }

            int effects = EffectCount(sacrifice);
            if (effects <= 0)
            {
                error = "《" + sacrifice.Name + "》一条效果都没有，没什么可转移的";
                return false;
            }

            cost = RoundCost(CostBase * (double)sacrifice.Cooldown / sacrifice.Power / effects
                             * Gap(sacrifice, target));
            error = null;
            return true;
        }

        /// <summary>
        /// 价钱；算不出来时返回 <c>0</c>。⚠ 需要「算不出来」的原因时用
        /// <see cref="TryCost"/> —— 界面要拿那句话给玩家看。
        /// </summary>
        public static int Cost(CardDef sacrifice, CardDef target)
        {
            int cost;
            string error;
            return TryCost(sacrifice, target, out cost, out error) ? cost : 0;
        }

        /// <summary>
        /// <b>底价</b>：只填了献祭牌时能给出的那个数 —— 冷却差取公式下限（1）时的价钱。
        ///
        /// <para>它同时是**这个献祭牌的最低价**（目标冷却再低也不会比它更便宜），
        /// 所以界面上写成「30 金起」是准确的，不是估算。</para>
        /// </summary>
        public static int MinCost(CardDef sacrifice)
        {
            if (sacrifice == null || sacrifice.Power <= 0)
            {
                return 0;
            }

            int effects = EffectCount(sacrifice);
            if (effects <= 0)
            {
                return 0;
            }

            return RoundCost(CostBase * (double)sacrifice.Cooldown / sacrifice.Power / effects
                             * MinCooldownGap);
        }

        /// <summary>冷却差（带公式里的下限）。献祭牌冷却 − 目标牌冷却，最小 1。</summary>
        public static int Gap(CardDef sacrifice, CardDef target)
        {
            if (sacrifice == null || target == null)
            {
                return MinCooldownGap;
            }

            int gap = sacrifice.Cooldown - target.Cooldown;
            return gap < MinCooldownGap ? MinCooldownGap : gap;
        }

        /// <summary>金币够不够付这一笔。</summary>
        public static bool CanAfford(int gold, int cost)
        {
            return gold >= cost;
        }

        /// <summary>金币不够时给玩家看的那句话。</summary>
        public static string CostShortfall(int gold, int cost)
        {
            return "金币不足：这一次要 " + cost + " 金，你只有 " + gold + " 金";
        }

        // ── 给玩家看的几行字（**全部在 Core**，界面只是把它们摆上去）──────

        /// <summary>公式本身（一行，放在明细的抬头）。</summary>
        public static string CostFormula()
        {
            return "价钱 = " + CostBase
                   + " × 被消耗牌的冷却 ÷ 它的力量 ÷ 它的效果数 × max(冷却差, "
                   + MinCooldownGap + ")";
        }

        /// <summary>
        /// <b>把这一次的价钱拆开写出来</b>（「为什么是这个数」）。
        /// 例：<c>40 × 冷却3 ÷ 力量2 ÷ 效果2 × 1 = 30 金</c>；
        /// 冷却差被下限抬起来时写成 <c>1（冷却差 0，按下限算）</c>，让玩家看得出
        /// 「目标冷却比献祭牌高也不会更便宜」。
        /// </summary>
        public static string CostLine(CardDef sacrifice, CardDef target)
        {
            int cost;
            string error;
            if (!TryCost(sacrifice, target, out cost, out error))
            {
                return error;
            }

            int raw = sacrifice.Cooldown - target.Cooldown;
            int gap = Gap(sacrifice, target);
            string gapText = raw < MinCooldownGap
                ? gap + "（冷却差 " + raw + "，按下限算）"
                : gap.ToString();

            return CostBase + " × 冷却" + sacrifice.Cooldown
                   + " ÷ 力量" + sacrifice.Power
                   + " ÷ 效果" + EffectCount(sacrifice)
                   + " × " + gapText
                   + " = " + cost + " 金";
        }

        /// <summary>
        /// 四个方向的口诀（玩家背下来就能自己估价）。**没有变化可讲时**贴在明细第二行。
        /// </summary>
        public static string CostRuleLine()
        {
            return "冷却越高越贵 · 力量越高越便宜 · 效果越多越便宜 · 目标冷却越低越贵";
        }

        /// <summary>
        /// 只填了献祭牌时那行提示：底价 + 后面会怎么变。
        /// 例：<c>冷却3 ÷ 力量2 ÷ 效果2 × 40 = 30 金起；目标冷却比它低时更贵</c>。
        /// </summary>
        public static string CostFloorLine(CardDef sacrifice)
        {
            if (sacrifice == null)
            {
                return string.Empty;
            }

            int effects = EffectCount(sacrifice);
            if (sacrifice.Power <= 0 || effects <= 0)
            {
                return "这张牌的价钱算不出来（力量 " + sacrifice.Power + " / 效果 " + effects + " 条）";
            }

            return "底价 " + CostBase + " × 冷却" + sacrifice.Cooldown
                   + " ÷ 力量" + sacrifice.Power + " ÷ 效果" + effects
                   + " = " + MinCost(sacrifice) + " 金起；右边那张冷却比它低时会更贵";
        }

        /// <summary>
        /// <b>「价钱为什么和刚才不一样」</b>—— 换了牌之后指出是哪个因素动了、往哪边动。
        ///
        /// <para>返回空串 = 没有变化（或没有可比的上一次）。有变化时形如
        /// <c>比刚才 30 金 → 60 金（贵了 30）：目标冷却 4→2（更贵）</c>。</para>
        ///
        /// <para>⚠ 它是一个**纯函数**（连「上一次」都当参数给），所以界面只需记住上一笔价钱，
        /// 不必自己拼句子 —— 拼句子的地方多一处，两处说法就会分叉。</para>
        /// </summary>
        public static string CostChangeLine(CardDef prevSacrifice, CardDef prevTarget, int prevCost,
            CardDef sacrifice, CardDef target, int cost)
        {
            if (prevCost <= 0 || cost <= 0 || prevCost == cost)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            bool sameSacrifice = prevSacrifice != null && sacrifice != null
                                 && string.Equals(prevSacrifice.Id, sacrifice.Id, StringComparison.Ordinal);

            if (!sameSacrifice)
            {
                parts.Add("换了一张被消耗的牌");
            }
            else
            {
                AddDelta(parts, "它的冷却", prevSacrifice.Cooldown, sacrifice.Cooldown, true);
                AddDelta(parts, "它的力量", prevSacrifice.Power, sacrifice.Power, false);
                AddDelta(parts, "它的效果数", EffectCount(prevSacrifice), EffectCount(sacrifice), false);
            }

            bool sameTarget = prevTarget != null && target != null
                              && string.Equals(prevTarget.Id, target.Id, StringComparison.Ordinal);
            if (!sameTarget)
            {
                AddDelta(parts, "目标冷却", prevTarget == null ? 0 : prevTarget.Cooldown,
                    target.Cooldown, false);
            }

            int delta = cost - prevCost;
            string head = "比刚才 " + prevCost + " 金 → " + cost + " 金（"
                          + (delta > 0 ? "贵了 " + delta : "便宜了 " + (-delta)) + "）";

            return parts.Count == 0 ? head : head + "：" + string.Join("、", parts.ToArray());
        }

        /// <summary>
        /// 某一个因素变了 → 一句话。<paramref name="raiseCosts"/> = 这个因素变大 = <b>更贵</b>
        /// （冷却 / 冷却差是这样），为假则相反（力量 / 效果数是分母，变大 = 更便宜）。
        /// </summary>
        private static void AddDelta(List<string> parts, string label, int before, int after,
            bool raiseCosts)
        {
            if (before == after)
            {
                return;
            }

            bool up = after > before;
            parts.Add(label + " " + before + "→" + after + "（" + (up == raiseCosts ? "更贵" : "更便宜") + "）");
        }

        private static int RoundCost(double value)
        {
            int cost = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            return cost < 1 ? 1 : cost;
        }
    }
}
