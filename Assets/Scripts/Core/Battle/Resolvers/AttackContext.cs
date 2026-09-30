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

        /// <summary>
        /// 本半场进攻方的虚弱层数 —— 开拍时从 <see cref="PlayerState.WeakenStacks"/> 取一份快照。
        ///
        /// <para>取快照而不是每次回读玩家状态：虚弱的递减发生在<b>半场结束时</b>，
        /// 半场进行中不该变；而且连击追加进攻是新的 <see cref="AttackContext"/>，
        /// 两次取到的是同一个值（递减在半场收尾才做），口径一致。</para>
        /// </summary>
        public int WeakenStacks;

        /// <summary>
        /// 最终进攻力量 = （基础 + 增量）再按虚弱层数折算（有层数则 ×50% 向上取整）。
        ///
        /// <para><b>为什么折算放在这里、而不是各调用点自己减</b>：这个数就是「防御方
        /// 需要面对的力量」—— 防御合法性（<see cref="DefenseResolver.BuildOptions"/>）、
        /// 免疫光环的区间判定、以及所有提示文案都读它。分散折算必然漏掉一处，
        /// 而漏掉的表现是「虚弱没生效」：不报错、不崩、只悄悄影响平衡。</para>
        /// </summary>
        public int FinalPower
        {
            get { return PlayerState.Weakened(BasePower + BonusPower, WeakenStacks); }
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
