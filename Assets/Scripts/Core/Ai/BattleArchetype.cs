using System.Collections.Generic;
using System.Text;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 战斗流派（用户 2026-10-03 口径）。<b>枚举顺序 = 同分时的优先级</b>
    /// （「如果出现等大的，则优先前面的思路」）。
    /// </summary>
    public enum BattleArchetype
    {
        /// <summary>高攻：堆力量 ≥8 的牌硬推。</summary>
        HighPower = 0,

        /// <summary>双发：靠双发逼对方交两张牌。</summary>
        Double = 1,

        /// <summary>连击：靠连击 / 区域加速打多次。</summary>
        Combo = 2,

        /// <summary>减速：靠区域减速拖对方节奏。</summary>
        Slow = 3,
    }

    /// <summary>
    /// 一次流派判定的完整结果（含每项得分与逐条依据），供 AI、怪物复写与调试使用。
    /// </summary>
    public sealed class ArchetypeAnalysis
    {
        /// <summary>判定胜出的流派（同分取枚举序靠前者）。</summary>
        public BattleArchetype Chosen;

        /// <summary>四个流派的得分，下标 = <see cref="BattleArchetype"/> 的整数值。</summary>
        public int[] Scores = new int[4];

        /// <summary>逐条打分依据（调试 / 日志用；不影响判定）。</summary>
        public string Report = string.Empty;

        public int ScoreOf(BattleArchetype archetype)
        {
            int i = (int)archetype;
            return i >= 0 && i < Scores.Length ? Scores[i] : 0;
        }
    }

    /// <summary>
    /// 战斗流派判定（2026-10-03）。
    ///
    /// <para><b>它是一次性判定</b>：用户口径为「一局<b>开局</b>判定一次固定」——
    /// 调用方（<see cref="HeuristicAgent"/>）在第一次轮到自己出牌时判一次，
    /// 之后整局沿用，不再随手牌变化重算。所以本类是纯函数，不持有任何状态。</para>
    ///
    /// <para><b>打分规则逐条对应</b>（原文 → 实现）：</para>
    ///
    /// <para>① <b>高攻</b>：力量（含卡自带进攻光环）≥ <see cref="HighPowerThreshold"/> 的牌，每张 1 分。</para>
    ///
    /// <para>② <b>双发</b>：至少一张双发卡 → 1 分；手牌里每有一张「防御时力量 ≥
    /// <see cref="DefenseThreshold"/> 的牌」（含卡自带防御光环）→ 1 分；至少两张双发卡 → 再 1 分。
    /// ⚠ 「防御力量 ≥8 的牌」按原文是<b>整条手牌</b>里数，不限于双发卡。</para>
    ///
    /// <para>③ <b>连击</b>：连击卡（除过载外，含引雷）≥ <see cref="ComboBaseCount"/> 张 → 2 分；
    /// 超出 2 张的每张连击卡，以及每张<b>区域加速</b>卡，各 +1；
    /// 有回血卡（自燃 / 过载）→ +1。
    /// ⚠ 「额外的连击卡」以「至少 2 张」这条基础条件成立为前提 —— 不够 2 张时不计基数，
    /// 也不计超出部分（见 <see cref="Analyze"/> 注释）。回血那 1 分独立于基础条件。</para>
    ///
    /// <para>④ <b>减速</b>：至少一张区域减速（暴风雪 / 冰风暴 / 雪崩）<b>且</b> 手上
    /// 减速牌（区域减速 + 其他减速）总数 ≥2 → 1 分；每多一张区域减速 → +3；
    /// 有任何一张其他减速卡 → +1。</para>
    ///
    /// <para><b>为什么把 Report 也做出来</b>：这套打分很容易「看起来该选 A 却选了 B」，
    /// 没有明细就只能靠猜。Report 是纯字符串，不参与任何判定。</para>
    /// </summary>
    public static class ArchetypeJudge
    {
        /// <summary>高攻判定阈值：力量（含进攻光环）达到这个数才算「高攻牌」。</summary>
        public const int HighPowerThreshold = 8;

        /// <summary>双发判定里的「防御力量达标」阈值。</summary>
        public const int DefenseThreshold = 8;

        /// <summary>连击流派的基础门槛张数。</summary>
        public const int ComboBaseCount = 2;

        /// <summary>
        /// 对 <paramref name="hand"/> 做四流派打分并选出胜者。
        /// </summary>
        public static ArchetypeAnalysis Analyze(IReadOnlyList<CardInstance> hand)
        {
            var result = new ArchetypeAnalysis();
            if (hand == null || hand.Count == 0)
            {
                // 空手牌 = 没有思路可言，落回「高攻」（枚举序第一）—— 反正无牌可打。
                result.Chosen = BattleArchetype.HighPower;
                result.Report = "手牌为空 → 默认高攻";
                return result;
            }

            int highPowerCards = 0;   // 力量（含进攻光环）≥8
            int doubleCards = 0;      // 双发卡
            int defPowerCards = 0;    // 防御力量（含防御光环）≥8
            int comboCards = 0;       // 连击卡（除过载）
            int hasteZoneCards = 0;   // 区域加速卡
            int healCards = 0;        // 回血卡
            int slowZoneCards = 0;    // 区域减速卡
            int slowCards = 0;        // 其他减速卡（不含区域减速）

            var sb = new StringBuilder();

            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance c = hand[i];
                if (c == null)
                {
                    continue;
                }

                CardDef d = c.Def;

                int atk = CardRole.AttackPotential(c);
                if (atk >= HighPowerThreshold)
                {
                    highPowerCards++;
                    sb.Append("\n  · 高攻命中 ").Append(d.Name).Append("（力量潜力 ").Append(atk).Append("）");
                }

                if (CardRole.IsDoubleCard(d))
                {
                    doubleCards++;
                }

                int def = CardRole.DefensePotential(c);
                if (def >= DefenseThreshold)
                {
                    defPowerCards++;
                }

                if (CardRole.IsComboCard(d))
                {
                    comboCards++;
                }

                if (CardRole.IsHasteZoneCard(d))
                {
                    hasteZoneCards++;
                }

                if (CardRole.IsHealCard(d))
                {
                    healCards++;
                }

                if (CardRole.IsSlowZoneCard(d))
                {
                    slowZoneCards++;
                }
                else if (CardRole.IsSlowCard(d))
                {
                    slowCards++;
                }
            }

            // ① 高攻：每张达标牌 1 分。
            int highPower = highPowerCards;

            // ② 双发：双发卡 ≥1 → 1；防御力量达标牌每张 → 1；双发卡 ≥2 → 再 1。
            int dbl = 0;
            if (doubleCards >= 1)
            {
                dbl += 1;
            }

            dbl += defPowerCards;
            if (doubleCards >= 2)
            {
                dbl += 1;
            }

            // ③ 连击：连击卡 ≥2 → 2；超出部分 + 区域加速各 +1；回血 +1。
            int combo = 0;
            if (comboCards >= ComboBaseCount)
            {
                combo = 2;
                combo += (comboCards - ComboBaseCount) + hasteZoneCards;
            }

            int healBonus = healCards >= 1 ? 1 : 0;
            combo += healBonus;

            // ④ 减速：<b>整条以「手上有区域减速」为前提</b> —— 用户这条的主题就是
            //    「以区域减速为核心的流派」，所以没有区域减速时它一分都不该得
            //    （否则任何一张带减速的牌 —— 比如地震 —— 都会凭空拿到 1 分，
            //     表现是「手上只有一张地震却判成了减速流派」）。
            //    前提成立后：区域减速 ≥1 且 减速牌总数 ≥2 → 1 分；多出的区域减速每张 +3；
            //    有其他减速卡 → +1。
            int slow = 0;
            if (slowZoneCards >= 1)
            {
                if ((slowZoneCards + slowCards) >= 2)
                {
                    slow = 1;
                }

                slow += (slowZoneCards - 1) * 3;

                if (slowCards >= 1)
                {
                    slow += 1;
                }
            }

            result.Scores[(int)BattleArchetype.HighPower] = highPower;
            result.Scores[(int)BattleArchetype.Double] = dbl;
            result.Scores[(int)BattleArchetype.Combo] = combo;
            result.Scores[(int)BattleArchetype.Slow] = slow;

            // 同分取枚举序靠前者 —— 直接按顺序比「严格大于」即可。
            BattleArchetype best = BattleArchetype.HighPower;
            for (int i = 1; i < 4; i++)
            {
                if (result.Scores[i] > result.Scores[(int)best])
                {
                    best = (BattleArchetype)i;
                }
            }

            result.Chosen = best;

            sb.Append("\n  高攻 ").Append(highPower).Append("（≥8 的牌 ").Append(highPowerCards).Append(" 张）")
              .Append(" · 双发 ").Append(dbl).Append("（双发 ").Append(doubleCards)
              .Append(" / 防御≥8 ").Append(defPowerCards).Append("）")
              .Append(" · 连击 ").Append(combo).Append("（连击 ").Append(comboCards)
              .Append(" / 区域加速 ").Append(hasteZoneCards).Append(" / 回血 ").Append(healCards).Append("）")
              .Append(" · 减速 ").Append(slow).Append("（区域减速 ").Append(slowZoneCards)
              .Append(" / 其他减速 ").Append(slowCards).Append("）")
              .Append("\n  → 判定流派：").Append(DisplayName(best));
            result.Report = sb.ToString();

            return result;
        }

        /// <summary>流派的显示名（日志 / 提示用）。</summary>
        public static string DisplayName(BattleArchetype archetype)
        {
            switch (archetype)
            {
                case BattleArchetype.HighPower:
                    return "高攻";
                case BattleArchetype.Double:
                    return "双发";
                case BattleArchetype.Combo:
                    return "连击";
                case BattleArchetype.Slow:
                    return "减速";
                default:
                    return archetype.ToString();
            }
        }
    }
}
