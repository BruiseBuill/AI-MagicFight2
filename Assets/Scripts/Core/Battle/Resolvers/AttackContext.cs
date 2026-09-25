using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 单次进攻的运行期上下文。模仿的复制结果、光环加成、双发 / 连击标记都挂在这里，
    /// <strong>不写回卡面</strong>（规则 §6.2「不永久改造牌面」）。
    /// </summary>
    internal sealed class AttackContext
    {
        public int AttackerSeat;
        public int DefenderSeat;

        /// <summary>本次进攻牌。</summary>
        public CardInstance Card;

        /// <summary>基础力量（模仿复制成功后被改写）。</summary>
        public int BasePower;

        /// <summary>效果带来的额外进攻力量（翻倍前）。</summary>
        public int EffectBonus;

        /// <summary>光环带来的额外进攻力量。</summary>
        public int AuraBonus;

        /// <summary>获得的额外进攻力量翻倍。</summary>
        public bool DoubleAtkBonus;

        /// <summary>本牌进攻力量不可增加。</summary>
        public bool NoAtkBuff;

        public bool IsDouble;

        public bool HasCombo;

        /// <summary>模仿复制到的卡（null = 未复制）。</summary>
        public CardDef CopySource;

        /// <summary>本段是否为「连击的追加进攻」（追加不重复触发冷却 −1）。</summary>
        public bool IsFollowUp;

        /// <summary>最终进攻力量（= 基础 + 增量）。</summary>
        public int FinalPower
        {
            get { return BasePower + BonusPower; }
        }

        /// <summary>额外进攻力量合计（翻倍 / 禁增都已计入）。</summary>
        public int BonusPower
        {
            get
            {
                if (NoAtkBuff)
                {
                    return 0;
                }

                int raw = EffectBonus + AuraBonus;
                return DoubleAtkBonus ? raw * 2 : raw;
            }
        }
    }
}
