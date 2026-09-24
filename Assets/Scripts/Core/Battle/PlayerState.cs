using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 玩家状态容器（`Docs/engineering/04-架构与接口.md` §2）。
    /// 用 <see cref="List{T}"/> 承载座位而不是写死两人 —— 为后续 4 人 2v2 留路
    /// （`Docs/engineering/03-工程规划.md` §1 预留接口）。
    /// </summary>
    public sealed class PlayerState
    {
        /// <summary>座位号：0 = 玩家、1 = AI。</summary>
        public readonly int Seat;

        /// <summary>显示名。</summary>
        public readonly string Name;

        /// <summary>是否由 AI 驱动。</summary>
        public readonly bool IsAi;

        internal PlayerState(int seat, string name, bool isAi)
        {
            Seat = seat;
            Name = name;
            IsAi = isAi;
            Hp = BattleState.InitialHp;
            MaxHp = BattleState.InitialHp;
            Hand = new List<CardInstance>();
            CoolingZone = new List<CardInstance>();
        }

        /// <summary>当前生命值。</summary>
        public int Hp { get; internal set; }

        /// <summary>生命上限。可被效果削到 0 —— 归零即刻死亡（决策 C1）。</summary>
        public int MaxHp { get; internal set; }

        /// <summary>手牌。上限 <see cref="BattleState.HandLimit"/>。</summary>
        public List<CardInstance> Hand { get; private set; }

        /// <summary>冷却区。敌方冷却区是明牌（减速要选目标）。</summary>
        public List<CardInstance> CoolingZone { get; private set; }

        /// <summary>是否已出局。</summary>
        public bool IsDead
        {
            get { return Hp <= 0 || MaxHp <= 0; }
        }

        /// <summary>手牌是否已满（满 8 后不再获得新牌）。</summary>
        public bool IsHandFull
        {
            get { return Hand.Count >= BattleState.HandLimit; }
        }

        /// <summary>相较初始值 4 已损失的生命点数（瀑流的基准固定为 4，决策 C2）。</summary>
        public int HpLost
        {
            get { return BattleState.InitialHp - Hp; }
        }

        /// <summary>手牌中当前进攻可用的最高力量（AI 与 UI 都直接用这个值，不重复算）。</summary>
        public int HighestHandPower
        {
            get
            {
                int best = -1;
                for (int i = 0; i < Hand.Count; i++)
                {
                    if (Hand[i].EffectivePower > best)
                    {
                        best = Hand[i].EffectivePower;
                    }
                }

                return best;
            }
        }

        /// <summary>冷却区中当前可用的光环来源（已激活且仍有指示物）。</summary>
        public List<CardInstance> AuraSources()
        {
            var list = new List<CardInstance>();
            for (int i = 0; i < CoolingZone.Count; i++)
            {
                if (CoolingZone[i].HasUsableAura)
                {
                    list.Add(CoolingZone[i]);
                }
            }

            return list;
        }

        public override string ToString()
        {
            return Name + "(seat" + Seat + ") Hp=" + Hp + "/" + MaxHp
                   + " 手牌=" + Hand.Count + " 冷却=" + CoolingZone.Count;
        }
    }
}
