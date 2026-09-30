using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 「这名玩家下一次进攻会打出哪张牌」的<strong>预判</strong>（2026-09-26 新增）。
    ///
    /// <para><b>它为什么在 Core 而不是 UI 层</b>：预判口径必须与真正的 AI 选牌
    /// <b>同源</b> —— 否则会出现「界面告诉玩家是冰系，AI 实际打出来是火系」这种
    /// 说不清对错的分歧。而「AI 怎么选」这件事的权威只有 <see cref="SimpleAiAgent"/>
    /// 手里有，所以预判逻辑也应当贴着它放。</para>
    ///
    /// <para><b>预判口径 = <see cref="SimpleAiAgent"/> 的进攻策略</b>：
    /// 「有牌就打，选当前<b>有效力量</b>最高的一张」。
    /// 平手时与 AI 取同一个（先出现在手牌里的那张）——
    /// <see cref="SimpleAiAgent.PickStrongestAttack"/> 用的是 <c>&gt;</c>，
    /// 所以相等时不替换，等价于「取最早的那张」。这里必须<b>严格照抄</b>这条比较。</para>
    ///
    /// <para><b>⚠ 这是预判、不是保证</b>：AI 的策略将来会变（<c>HeuristicAgent</c> 在规划里），
    /// 手牌也会在玩家点开提示框之后继续变化。所以调用方拿到的只是
    /// 「按当前局面，它下一张会打这个」—— 用它做提示文案时不要写成「它一定会打」。</para>
    ///
    /// <para><b>它<b>不</b>依赖 <see cref="DecisionRequest"/></b>：玩家点怪物时引擎往往
    /// 正停在「等玩家自己出牌」那一拍，此刻根本不存在「AI 进攻决策」这个对象。
    /// 所以这里直接读 <see cref="PlayerState.Hand"/>，按同一条策略现算 ——
    /// 这也是它能放进 <see cref="BattleState"/> 的静态方法的原因。</para>
    /// </summary>
    public static class AttackForecast
    {
        /// <summary>
        /// 预判 <paramref name="state"/> 里 <paramref name="seat"/> 座位下一次进攻会打出的那张牌。
        /// 手牌为空返回 null（此时它打不出任何牌）。
        ///
        /// <para><b>为什么手牌为空返回 null 而不是随便给一张</b>：手牌空 = 这一拍它确实无牌可打，
        /// 表现层拿到 null 应当<b>不弹提示框</b>（而不是弹一个错的元素符号）。</para>
        /// </summary>
        public static CardInstance StrongestInHand(BattleState state, int seat)
        {
            if (state == null)
            {
                return null;
            }

            PlayerState player = state.Of(seat);
            if (player == null || player.Hand.Count == 0)
            {
                return null;
            }

            CardInstance best = null;
            for (int i = 0; i < player.Hand.Count; i++)
            {
                CardInstance card = player.Hand[i];

                // ⚠ 比较符必须与 SimpleAiAgent.PickStrongestAttack 一致（严格大于）。
                //    改成 >= 会让「同力量时取最后一张」，与 AI 实际行为不符 ——
                //    而提示框只说元素，这种偏差很难被发现。
                if (best == null || card.EffectivePower > best.EffectivePower)
                {
                    best = card;
                }
            }

            return best;
        }

        /// <summary>
        /// 预判结果里<b>不含任何牌名 / 力量</b>的那个字段 —— 元素。
        ///
        /// <para>用户口径（2026-09-26）：点击怪物只允许暴露<b>元素符号</b>，
        /// 不能暴露「是哪一张牌」。所以表现层<b>只应该</b>调这一个方法，
        /// 不要自己去拿 <see cref="StrongestInHand"/> 的返回值再取别的字段。</para>
        /// </summary>
        public static CardElement ElementOfNextAttack(BattleState state, int seat)
        {
            CardInstance card = StrongestInHand(state, seat);
            return card == null ? CardElement.None : card.Def.Element;
        }
    }
}
