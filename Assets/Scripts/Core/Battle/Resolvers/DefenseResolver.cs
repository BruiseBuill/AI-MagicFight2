using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 防御合法性解析 —— <strong>「防御方不允许打出挡不住的牌」这条硬性约束的唯一落点</strong>
    /// （`Docs/rules/01-规则基线.md` §3「硬性约束」）。
    ///
    /// 引擎在生成 <see cref="DecisionRequest.Options"/> 时就把非法项过滤掉，
    /// AI 与 UI 都不可能选到非法项，两者都不需要重复判断规则。
    /// </summary>
    public static class DefenseResolver
    {
        /// <summary>本牌是否带「守护」（无视力量差异必定挡住，只挡 1 次）。</summary>
        public static bool HasGuard(CardInstance card)
        {
            if (card == null)
            {
                return false;
            }

            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                if (card.Def.Effects[i].Trigger == EffectTrigger.Defend
                    && card.Def.Effects[i].Op == EffectOp.Guard)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 本牌作为防御牌打出时，它自己 β 效果带来的防御力量加值（「防御时力量 +A」）。
        ///
        /// <para><b>2026-09-20 补的洞</b>：<see cref="EffectOp.DefPlus"/> 从卡表录入那天起
        /// 就<b>没有任何结算点</b> —— 潮汐（e）/ 地动波（z）的「β 防御时力量 +1」是全程静默失效的
        /// （引擎只认 <see cref="CardInstance.EffectivePower"/>，那一位只反映进攻侧）。
        /// 表现是「地动波 7 挡不住 8」，而卡面明明写着 +1。</para>
        ///
        /// <para>只在防御侧叠加：本效果带 β 记号，与进攻力量无关；
        /// <see cref="CardInstance.EffectivePower"/>（模仿复制出来的力量）仍是进攻侧的口径。</para>
        /// </summary>
        /// <param name="handCount">
        /// 防御方<b>出牌前</b>的手牌张数 —— 只有 <see cref="EffectOp.DefPlusPerHand"/>
        /// （海啸 au 的 β「防御时增加等于手牌数的力量值」）用得上，其余 β 效果与它无关。
        ///
        /// <para>⚠ <b>不传 = 按 0 张算</b>（该项加值归零）。调用点必须自己想清楚：
        /// 凡是参与「这张牌挡不挡得住」判定的地方（<see cref="BuildOptions"/> /
        /// <c>BattleEngine.DoChooseDefense</c> / 自测的合法性反查）都<b>必须传真实手牌数</b>，
        /// 否则会出现「界面说挡得住、引擎校验说不够」这种两处口径分叉。</para>
        ///
        /// <para>「含不含正在打出的这一张」：<b>含</b>（用户 2026-10-07 拍板）——
        /// 调用点传的都是「还没把它从手牌里摘掉」的那个数。</para>
        /// </param>
        public static int DefendBonus(CardInstance card, int handCount = 0)
        {
            if (card == null)
            {
                return 0;
            }

            int sum = 0;
            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                EffectDef ef = card.Def.Effects[i];
                if (ef.Trigger != EffectTrigger.Defend)
                {
                    continue;
                }

                if (ef.Op == EffectOp.DefPlus)
                {
                    sum += ef.A;
                }
                else if (ef.Op == EffectOp.DefPlusPerHand)
                {
                    // 海啸（au）：每张手牌 +A。手牌数为负没有意义，兜一下。
                    sum += ef.A * (handCount < 0 ? 0 : handCount);
                }
            }

            return sum;
        }

        /// <summary>本牌作为防御牌打出时的有效防御力量（= 有效力量 + β 加值）。</summary>
        public static int DefensePower(CardInstance card, int handCount = 0)
        {
            return card == null ? 0 : card.EffectivePower + DefendBonus(card, handCount);
        }

        /// <summary>本牌挡住指定攻击力量还差的防御力量（守护牌恒为 0）。</summary>
        public static int RequiredBonus(CardInstance card, int attackPower, int handCount = 0)
        {
            if (HasGuard(card))
            {
                return 0;
            }

            int need = attackPower - DefensePower(card, handCount);
            return need < 0 ? 0 : need;
        }

        /// <summary>
        /// 生成防御选项。含 Skip（可以主动放弃防御，决策 A2 / 规则 §7）。
        ///
        /// <para><b>2026-09-18 规则改动</b>：免疫光环走的是<b>另一拍</b>（<c>ChooseAuraUse</c>，
        /// 由 HudBuff 的图标拖动触发），且一旦消耗就<b>整个攻击被免疫、不需要再出牌</b>。
        /// 所以这里不再需要 <c>immuneApplied</c> 这个分支 —— 走到本方法时必然没有免疫覆盖
        /// （旧的「免疫后仍须交牌、只是力量不参与比较」＝推论 P1 已作废）。</para>
        /// </summary>
        public static List<Option> BuildOptions(
            BattleState state,
            int defenderSeat,
            int attackPower,
            bool isDouble,
            int appliedDefenseBonus = 0)
        {
            var options = new List<Option>();
            PlayerState defender = state.Of(defenderSeat);

            options.Add(new Option
            {
                Kind = OptionKind.Skip,
                Card = null,
                Label = "放弃防御（掉 1 点生命）",
            });

            // 只有玩家/AI 本次明确消耗的光环可以补足防御力量。
            int avail = appliedDefenseBonus;

            // β「防御时 +（每张手牌 ×A）」（海啸 au）的手牌数口径 = 出牌前的手牌张数
            //（此时它还在手上，所以天然「含正在打出的这一张」）。
            int handCount = defender.Hand.Count;

            if (!isDouble)
            {
                for (int i = 0; i < defender.Hand.Count; i++)
                {
                    CardInstance c = defender.Hand[i];
                    int req = RequiredBonus(c, attackPower, handCount);
                    if (req > avail)
                    {
                        continue;
                    }

                    options.Add(new Option
                    {
                        Kind = OptionKind.PlayCard,
                        Seat = defenderSeat,
                        Card = c,
                        Value = req,
                        Label = Describe(c, attackPower, req, handCount),
                    });
                }
            }
            else
            {
                // 双发：必须交出两张够挡的牌；不能部分防御（双方都答 A1 确认）
                for (int i = 0; i < defender.Hand.Count; i++)
                {
                    for (int j = i + 1; j < defender.Hand.Count; j++)
                    {
                        CardInstance a = defender.Hand[i];
                        CardInstance b = defender.Hand[j];
                        int req = RequiredBonus(a, attackPower, handCount)
                                  + RequiredBonus(b, attackPower, handCount);
                        if (req > avail)
                        {
                            continue;
                        }

                        options.Add(new Option
                        {
                            Kind = OptionKind.PlayCard,
                            Seat = defenderSeat,
                            Card = a,
                            PairCard = b,
                            Value = req,
                            Label = "打出 " + a.Def.Name + " + " + b.Def.Name
                                    + "（" + a.Def.PowerText + " / " + b.Def.PowerText + "，挡双发）",
                        });
                    }
                }
            }

            Reindex(options);
            return options;
        }

        /// <summary>是否存在任何能挡住的防御方案（仅 Skip 时表示挡不住）。</summary>
        public static bool HasAnyBlock(List<Option> options)
        {
            if (options == null)
            {
                return false;
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (!options[i].IsSkip)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(CardInstance card, int attackPower, int required, int handCount)
        {
            // 防御力量与进攻力量不是同一个数（β 加值只算防御侧），所以这里把「实际用来挡的数」
            // 写清楚：玩家看选项时不该自己去心算「7 还是 8」。
            int bonus = DefendBonus(card, handCount);
            string head = "打出 " + card.Def.Name + "（力量 " + card.Def.PowerText
                          + (bonus > 0 ? "，防御 +" + bonus + " → " + DefensePower(card, handCount) : string.Empty)
                          + "）";
            if (HasGuard(card))
            {
                return head + " — 守护，必定挡住";
            }

            if (required > 0)
            {
                return head + " — 使用已投入光环 +" + required;
            }

            return head + " — 挡住";
        }

        private static void Reindex(List<Option> options)
        {
            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }
        }
    }
}
