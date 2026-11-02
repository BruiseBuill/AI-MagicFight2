using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// AI 的光环消费策略（2026-11-01 v2）。
    ///
    /// <para><b>为什么单独一层</b>：光环「生产」与「消费」是两件事 ——
    /// 出牌优先级只保证带光环的牌被打出去（指示物被生产），本类才决定
    /// 什么时候把指示物花掉。两者混在 <see cref="HeuristicAgent"/> 里会导致
    /// 「优先级改了、消费逻辑跟着漂」，也不方便怪物子类只换其中一半。</para>
    ///
    /// <para><b>用户口径（2026-11-01）</b>：</para>
    /// <list type="number">
    /// <item>免疫光环<b>只要这一拍能用就用掉</b>（来源牌回手即作废，留着没有意义）；
    /// 连击流派也一样 —— 免疫不交牌、不进力量预算，和「留着牌打连击」不冲突。</item>
    /// <item>进攻力量光环：<b>当前牌的力量 + 这枚光环的加值 &gt; 9 → 不用，否则用</b>。
    /// 9 是「有意义的力量上限」—— 全卡表最高防御力量就是 9（地刺 / 陨石 / 滚石 / 冰封铠甲 / 冷冻核心），
    /// 再加也不会让更多牌挡不住，只会浪费指示物。</item>
    /// <item>不做「反免疫避让」：不为了绕开对方的免疫区间去刻意压低或抬高力量。</item>
    /// </list>
    ///
    /// <para><b>连击与快速回填</b>：连击光环仍按卡面阈值（基础力量 ≤A）且不叠加；
    /// 快速回填只在「减 1 之后仍 ≥1」时才有意义。两者都只有进攻场合可用，因此
    /// 能用就用，不需要和 9 这条线比较。</para>
    /// </summary>
    public static class AuraPolicy
    {
        /// <summary>进攻力量的有意义上限 —— 全卡表最高防御力量为 9。</summary>
        public const int MaxAttackPower = 9;

        /// <summary>
        /// 本拍要随牌提交的进攻光环序号（按选项表的下标）。没有可用的返回 null。
        ///
        /// <para>只读 <see cref="DecisionRequest.Options"/> 与<b>自己的</b>
        /// <see cref="BattleState"/>（手牌张数），不读对方手牌内容。</para>
        /// </summary>
        public static int[] PickAttackAuras(DecisionRequest req, CardInstance card, BattleState state)
        {
            if (req == null || card == null || card.Def == null)
            {
                return null;
            }

            // 沉重打击这类「此法术的进攻力量不能增加」：一枚都不烧。
            // 引擎的 CollectRefunded 本来也会整批退回，这里提前挡住，避免「准备了又退回」的空转。
            if (card.Def.ForbidsAtkBuff)
            {
                return null;
            }

            var picked = new List<Option>();

            Option combo = PickCombo(req, card, state);
            if (combo != null)
            {
                picked.Add(combo);
            }

            PickPowerAuras(req, card, picked);
            PickQuickRefill(req, card, picked);

            if (picked.Count == 0)
            {
                return null;
            }

            var indices = new int[picked.Count];
            for (int i = 0; i < picked.Count; i++)
            {
                indices[i] = picked[i].Index;
            }

            return indices;
        }

        /// <summary>
        /// 连击光环（引雷）：基础力量 ≤A、卡面自己不会获得连击、且打完这张后手里还有牌可接。
        ///
        /// <para>「还有牌可接」= 手牌 ≥2（当前这张还在手里，所以 2 表示打完剩 1 张）。
        /// 没有 state 时判不出这一点，宁可不烧。</para>
        /// </summary>
        private static Option PickCombo(DecisionRequest req, CardInstance card, BattleState state)
        {
            if (state == null)
            {
                return null;
            }

            PlayerState owner = state.Of(req.Seat);
            if (owner == null || owner.Hand.Count < 2)
            {
                return null;
            }

            // 卡面自己就会给连击的牌不再烧连击光环 —— 引擎对这类「重复授予」不会退回
            // （CollectRefunded 只认 EffectOp.Combo），烧了就是白扔。
            if (AlreadyGrantsCombo(card.Def, owner.Hand.Count))
            {
                return null;
            }

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsAura(o) || o.AuraKind != AuraKind.Combo)
                {
                    continue;
                }

                // 卡面写的是「基础力量 ≤A」—— 用 Def.Power，不是被加过力量的 EffectivePower。
                if (card.Def.Power <= o.Value)
                {
                    return o;
                }
            }

            return null;
        }

        /// <summary>
        /// 这张牌本身是否已经会获得连击（无需光环）：
        /// 无条件连击 / 烧手牌换连击 / 条件连击（手牌只剩这一张时成立）。
        /// </summary>
        private static bool AlreadyGrantsCombo(CardDef def, int handCount)
        {
            if (CardRole.HasAttackOp(def, EffectOp.Combo))
            {
                return true;
            }

            if (CardRole.HasAttackOp(def, EffectOp.CoolHandForCombo))
            {
                return true;
            }

            // 闪电球：只有它是手里最后一张时才会获得连击。
            return CardRole.HasAttackOp(def, EffectOp.ComboIfLastHand) && handCount <= 1;
        }

        /// <summary>
        /// 力量加值类：按加值从大到小贪心，<b>只要「当前力量 + 这枚 &gt; 9」就跳过这一枚</b>，
        /// 否则用掉。
        ///
        /// <para>降序遍历的好处：牌面 8 时 +4 / +3 / +2 都会被跳过，最后 +1 刚好到 9 ——
        /// 既满足了「不超过 9」，又不会因为最大的那枚塞不下就整批不用。</para>
        ///
        /// <para>同加值时 <see cref="AuraKind.AtkPower"/> 排在
        /// <see cref="AuraKind.AtkOrDefPower"/> 前面：后者还能在防御侧补值，进攻侧先用专用的。</para>
        /// </summary>
        private static void PickPowerAuras(DecisionRequest req, CardInstance card, List<Option> picked)
        {
            var list = new List<Option>();
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (IsAura(o) && AuraResolver.IsPowerBonus(o.AuraKind))
                {
                    list.Add(o);
                }
            }

            if (list.Count == 0)
            {
                return;
            }

            list.Sort(ComparePowerAura);

            int power = card.EffectivePower;
            for (int i = 0; i < list.Count; i++)
            {
                Option o = list[i];
                if (power + o.Value > MaxAttackPower)
                {
                    continue;
                }

                picked.Add(o);
                power += o.Value;
            }
        }

        /// <summary>加值降序；同加值时 AtkPower 先于 AtkOrDefPower；再同则按选项序号稳定排序。</summary>
        private static int ComparePowerAura(Option a, Option b)
        {
            if (a.Value != b.Value)
            {
                return b.Value - a.Value;
            }

            int rankA = a.AuraKind == AuraKind.AtkOrDefPower ? 1 : 0;
            int rankB = b.AuraKind == AuraKind.AtkOrDefPower ? 1 : 0;
            if (rankA != rankB)
            {
                return rankA - rankB;
            }

            return a.Index - b.Index;
        }

        /// <summary>
        /// 快速回填：只有进攻时可用，且「剩余冷却额外 −1」要真的有意义。
        /// 基础冷却 ≥2 才烧（冷却 1 的牌减到 0 等于当拍回手，收益仍然存在，但卡池里没有这种牌）。
        /// </summary>
        private static void PickQuickRefill(DecisionRequest req, CardInstance card, List<Option> picked)
        {
            if (card.Def.Cooldown < 2)
            {
                return;
            }

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (IsAura(o) && o.AuraKind == AuraKind.QuickRefill && !picked.Contains(o))
                {
                    picked.Add(o);
                    return;
                }
            }
        }

        /// <summary>光环选项：出牌选项也带 Card（= 来源牌），必须靠 AuraSource 区分。</summary>
        private static bool IsAura(Option o)
        {
            return o != null && o.AuraSource != null && o.AuraKind != AuraKind.None;
        }
    }
}
