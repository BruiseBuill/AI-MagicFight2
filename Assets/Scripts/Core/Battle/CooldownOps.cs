using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>一次冷却改动的记录，供引擎转成事件。</summary>
    public struct CooldownChange
    {
        public CardInstance Card;
        public int From;
        public int To;
        public bool ReturnedToHand;
        public string Reason;

        public override string ToString()
        {
            return Card + " " + From + "→" + To + (ReturnedToHand ? " 回手" : string.Empty) + " (" + Reason + ")";
        }
    }

    /// <summary>
    /// 冷却系统（M3）。<strong>唯一</strong>允许修改 <see cref="CardInstance.RemainingCooldown"/> 的入口，
    /// 其他模块一律通过它。规则依据：`Docs/rules/01-规则基线.md` §4「冷却只有以下五条通路，别无其它增减方式」。
    ///
    /// 不变量：冷却区中的牌满足 <c>1 ≤ 剩余冷却 ≤ 基础冷却值</c>；一旦归 0 立即回手。
    /// </summary>
    public static class CooldownOps
    {
        /// <summary>
        /// 进攻开始时结算：<strong>进攻方自己</strong>的冷却区全体剩余冷却 −1，归 0 的牌立即回手。
        /// 每个半场只结算一次 —— 连击的追加进攻不重复触发（推论 P2，由引擎侧保证只调用一次）。
        /// </summary>
        public static void TickOnAttackStart(PlayerState owner, List<CooldownChange> sink)
        {
            if (owner == null)
            {
                return;
            }

            // 倒序遍历：回手会改动列表
            for (int i = owner.CoolingZone.Count - 1; i >= 0; i--)
            {
                CardInstance card = owner.CoolingZone[i];
                if (card.RemainingCooldown <= 0)
                {
                    continue;
                }

                int from = card.RemainingCooldown;
                int to = from - 1;
                bool back = false;

                if (to <= 0)
                {
                    ReturnToHand(owner, card, sink, "进攻开始结算");
                    back = true;
                    to = 0;
                }
                else
                {
                    card.RemainingCooldown = to;
                }

                if (sink != null)
                {
                    sink.Add(new CooldownChange
                    {
                        Card = card,
                        From = from,
                        To = to,
                        ReturnedToHand = back,
                        Reason = "进攻开始 −1",
                    });
                }
            }
        }

        /// <summary>
        /// 加速一张牌（−1，下限 0）；到 0 立即回手。返回是否真的生效。
        ///
        /// <para><paramref name="reason"/> 传了就写进 <see cref="CooldownChange.Reason"/>，
        /// 不传用默认的「加速 −1」。区域类（<see cref="HasteZone"/>）会传「区域加速」——
        /// 好让表现层分得出「这一次是点了某一格冷却槽」还是「点了某一张牌」
        /// （M36：怪物执行效果时要在提示条上写清是哪一种）。</para>
        /// </summary>
        public static bool TryHaste(PlayerState owner, CardInstance card, List<CooldownChange> sink,
            string reason = null)
        {
            if (owner == null || card == null || !card.IsCooling || card.RemainingCooldown <= 0)
            {
                return false;
            }

            int from = card.RemainingCooldown;
            int to = from - 1;

            if (to <= 0)
            {
                ReturnToHand(owner, card, sink, "加速");
                to = 0;
            }
            else
            {
                card.RemainingCooldown = to;
            }

            if (sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = card,
                    From = from,
                    To = to,
                    ReturnedToHand = to == 0,
                    Reason = string.IsNullOrEmpty(reason) ? "加速 −1" : reason,
                });
            }

            return true;
        }

        /// <summary>
        /// 减速一张牌（+1，上限 = 基础冷却值）。
        ///
        /// <para>⚠ <b>「能不能减速」的上限就在这里</b>：已经顶到
        /// <see cref="CardDef.Cooldown"/> 的牌是减不动的，返回 false。
        /// 引擎生成加速 / 减速候选时也按同一条判据把这类牌<b>过滤掉</b>
        /// （见 <c>BattleEngine.IssueStage3Repeat</c>）—— 界面上会把它画成
        /// 「压暗、点得动、点了弹一条无法减速的提示」，两处判据必须一致。</para>
        /// </summary>
        public static bool TrySlow(CardInstance card, List<CooldownChange> sink, string reason = null)
        {
            if (card == null || !card.IsCooling)
            {
                return false;
            }

            if (card.RemainingCooldown >= card.Def.Cooldown)
            {
                return false;
            }

            int from = card.RemainingCooldown;
            int to = from + 1;
            card.RemainingCooldown = to;

            if (sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = card,
                    From = from,
                    To = to,
                    ReturnedToHand = false,
                    Reason = string.IsNullOrEmpty(reason) ? "减速 +1" : reason,
                });
            }

            return true;
        }

        /// <summary>区域加速：把 <paramref name="owner"/> 冷却区中剩余冷却 = <paramref name="value"/> 的牌全体 −1。</summary>
        public static int HasteZone(PlayerState owner, int value, List<CooldownChange> sink)
        {
            return ApplyZone(owner, value, -1, sink, "区域加速");
        }

        /// <summary>区域减速：把 <paramref name="owner"/> 冷却区中剩余冷却 = <paramref name="value"/> 的牌全体 +1。</summary>
        public static int SlowZone(PlayerState owner, int value, List<CooldownChange> sink)
        {
            return ApplyZone(owner, value, +1, sink, "区域减速");
        }

        /// <summary>区域类的共同实现。返回实际改动的张数。</summary>
        private static int ApplyZone(PlayerState owner, int value, int delta, List<CooldownChange> sink, string reason)
        {
            if (owner == null || value <= 0)
            {
                return 0;
            }

            int touched = 0;
            for (int i = owner.CoolingZone.Count - 1; i >= 0; i--)
            {
                CardInstance card = owner.CoolingZone[i];
                if (card.RemainingCooldown != value)
                {
                    continue;
                }

                if (delta < 0)
                {
                    if (TryHaste(owner, card, sink, reason))
                    {
                        touched++;
                    }
                }
                else
                {
                    if (TrySlow(card, sink, reason))
                    {
                        touched++;
                    }
                }
            }

            if (touched == 0 && sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = null,
                    From = value,
                    To = value,
                    ReturnedToHand = false,
                    Reason = reason + "（无匹配目标）",
                });
            }

            return touched;
        }

        /// <summary>重置冷却：剩余冷却恢复为基础冷却值（寒流）。</summary>
        public static bool ResetToBase(CardInstance card, List<CooldownChange> sink)
        {
            if (card == null || !card.IsCooling)
            {
                return false;
            }

            int from = card.RemainingCooldown;
            if (from == card.Def.Cooldown)
            {
                return false;
            }

            card.RemainingCooldown = card.Def.Cooldown;

            if (sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = card,
                    From = from,
                    To = card.Def.Cooldown,
                    ReturnedToHand = false,
                    Reason = "重置冷却",
                });
            }

            return true;
        }

        /// <summary>使目标立即冷却完成（剩余归 0 → 立即回手）。</summary>
        public static bool Refresh(PlayerState owner, CardInstance card, List<CooldownChange> sink)
        {
            if (owner == null || card == null || !card.IsCooling)
            {
                return false;
            }

            int from = card.RemainingCooldown;
            ReturnToHand(owner, card, sink, "立即冷却完成");

            if (sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = card,
                    From = from,
                    To = 0,
                    ReturnedToHand = true,
                    Reason = "立即冷却完成",
                });
            }

            return true;
        }

        /// <summary>
        /// 把一张牌（手牌或攻防中的牌）按基础冷却送入冷却区。
        /// <paramref name="craftedCd"/> 可覆盖初始冷却（蘑菇额外 −1）；
        /// 由磁暴 / 电弧 / 充能 / 蘑菇 / 雷云的效果造成的入场<strong>不触发</strong>该牌自身的攻防效果（推论 P4）。
        /// </summary>
        public static bool PutIntoCooldown(
            PlayerState owner, CardInstance card, int? craftedCd, List<CooldownChange> sink)
        {
            return PutIntoCooldown(owner, card, craftedCd, sink, "按基础冷却进入冷却区");
        }

        internal static bool PutIntoCooldown(
            PlayerState owner, CardInstance card, int? craftedCd, List<CooldownChange> sink, string reason)
        {
            if (owner == null || card == null)
            {
                return false;
            }

            if (card.Zone == CardZone.Removed || card.Zone == CardZone.Cooling)
            {
                return false;
            }

            int baseCd = card.Def.Cooldown;
            int cd = craftedCd.HasValue ? craftedCd.Value : baseCd;

            // 不变量：冷却区中的牌 1 ≤ 剩余冷却 ≤ 基础冷却值
            if (cd < 1)
            {
                cd = 1;
            }

            if (cd > baseCd)
            {
                cd = baseCd;
            }

            // 关键：必须真的挂到冷却区列表上，否则所有依赖遍历冷却区的规则（进攻开始 −1、
            // 区域加速 / 减速、加速 / 减速选目标、回手）都会作用在空列表上。
            owner.Hand.Remove(card);
            if (!owner.CoolingZone.Contains(card))
            {
                owner.CoolingZone.Add(card);
            }

            card.Zone = CardZone.Cooling;
            card.RemainingCooldown = cd;
            card.AuraLive = false;
            card.AuraTokens = 0;
            card.EffectivePower = card.Def.Power;

            if (sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = card,
                    From = 0,
                    To = cd,
                    ReturnedToHand = false,
                    Reason = reason,
                });
            }

            return true;
        }

        /// <summary>把某人冷却区中的一张牌永久移出游戏（漩涡）。</summary>
        public static bool RemoveFromGame(PlayerState owner, CardInstance card)
        {
            if (owner == null || card == null || !card.IsCooling || !owner.CoolingZone.Contains(card))
            {
                return false;
            }

            owner.CoolingZone.Remove(card);
            card.Zone = CardZone.Removed;
            card.RemainingCooldown = 0;
            card.AuraTokens = 0;
            card.AuraLive = false;
            return true;
        }

        /// <summary>
        /// 回手（内部实现）：移出冷却区、<b>挂回手牌列表</b>、清空光环指示物、重置有效力量。
        ///
        /// <para><b>⚠ 2026-09-17 修复的严重缺陷</b>：原来这里只有 <c>card.ToHand()</c>，
        /// 而 <see cref="CardInstance.ToHand"/> 只把 <c>Zone</c> 字段改成 Hand，
        /// <b>不会把牌塞回 <see cref="PlayerState.Hand"/></b>（手牌列表只有引擎在发牌时
        /// 用 <c>Hand.Add</c> 维护）。结果是：</para>
        /// <list type="bullet">
        /// <item><c>CooldownChangedEvent</c>（回手）与 <c>CardReturnedEvent</c> 照发，日志里也照打
        /// 「冷却完毕回手」——<b>看起来一切正常</b>；</item>
        /// <item>冷却区确实少了一张（所以冷却相关的规则不会露馅）；</item>
        /// <item>但那张牌从此既不在手牌、也不在冷却区，<b>彻底失联</b> ——
        /// 该方总牌数永久少一张，冷却越用越空，最后因为「手上无牌」被判无法攻击而白掉血。</item>
        /// </list>
        ///
        /// <para>这与 <see cref="PutIntoCooldown"/> 当年漏挂 <c>CoolingZone</c> 是<b>同一类 bug</b>：
        /// 只改了牌自己的字段，没改「归属列表」。凡是要改一个牌的归属，两处都要动。
        /// 万局统计查不出来（事件数量、冷却数值全对），只有「手牌守恒」这类断言能抓住，
        /// 见 <c>Tools/RuleSelfTest</c> 的 <c>EventWatcher</c>。</para>
        /// </summary>
        private static void ReturnToHand(PlayerState owner, CardInstance card, List<CooldownChange> sink, string reason)
        {
            if (owner.CoolingZone.Contains(card))
            {
                owner.CoolingZone.Remove(card);
            }

            bool droppedAura = card.AuraTokens > 0;
            card.ToHand();

            // 挂回手牌。这里**不做**手牌上限判定：本作总牌数 = 初始 6 + 第 2/3 回合各 1 = 8
            // = 牌手上限，所以「手上 8 张」时冷却区必然为空，不可能溢出；
            // 而规则 §4「归 0 立即回手」是无条件的，加一个「满手就不回」的分支反而会让牌凭空消失。
            if (!owner.Hand.Contains(card))
            {
                owner.Hand.Add(card);
            }

            if (droppedAura && sink != null)
            {
                sink.Add(new CooldownChange
                {
                    Card = card,
                    From = 0,
                    To = 0,
                    ReturnedToHand = false,
                    Reason = "回手时未使用的光环指示物作废",
                });
            }
        }
    }
}
