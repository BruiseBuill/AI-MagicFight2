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
        /// <summary>座位号，由对局配置决定，与控制器类型无关。</summary>
        public readonly int Seat;

        /// <summary>显示名。</summary>
        public readonly string Name;

        public readonly CharacterDefinition Definition;
        public readonly ControlKind Control;
        public readonly int Team;
        public readonly int InitialHp;
        public readonly Deck Deck;
        internal readonly List<ICharacterAbility> Abilities = new List<ICharacterAbility>();
        public bool IsAi { get { return Control == ControlKind.Ai; } }

        internal PlayerState(int seat, ParticipantSetup setup, Rng rng)
        {
            Seat = seat;
            Definition = setup.Character;
            Name = Definition.Name;
            Control = setup.Control;
            Team = setup.Team;
            InitialHp = Definition.InitialHp;
            Hp = InitialHp;
            MaxHp = Definition.MaxHp;
            Deck = new Deck(Definition.CreateCardPool().Resolve(), rng);
            Hand = new List<CardInstance>();
            CoolingZone = new List<CardInstance>();
            foreach (ICharacterAbilityDefinition ability in Definition.Abilities)
            {
                ICharacterAbility runtime = ability.CreateRuntime();
                if (runtime == null) throw new InvalidOperationException("能力未创建运行实例。");
                Abilities.Add(runtime);
            }
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

        /// <summary>相较本局初始生命损失的点数；回血后下降，下限为 0。</summary>
        public int HpLost
        {
            get { return Math.Max(0, InitialHp - Hp); }
        }

        /// <summary>
        /// 虚弱层数（毒刺 ao 施加给被攻击的目标）。
        ///
        /// <para><b>效果</b>：拥有者打出的牌，<b>最终进攻力量</b>按 <see cref="Weakened"/>
        /// 折算（有层级就减半）。<b>只削进攻力量</b>，它作为防御方时的防御力量不受影响
        /// （2026-09-29 用户口径）。</para>
        ///
        /// <para><b>递减时机</b>：拥有者<b>自己的一个进攻半场结束之后</b> −1
        /// （在 <c>BattleEngine.DoEndHalfTurn</c> 里结算）。连击的追加进攻属于同一个半场，
        /// 整场只减 1 层。</para>
        ///
        /// <para>层数可叠加、无上限：毒刺一次给 2 层。</para>
        /// </summary>
        public int WeakenStacks { get; internal set; }

        /// <summary>
        /// 把最终进攻力量按虚弱层数折算：有层数时 ×50% 并<b>向上取整</b>
        /// （7 → 4 · 9 → 5 · 5 → 3 · 2 → 1）。
        ///
        /// <para>⚠ 取整口径是用户 2026-09-29 明确指定的「<b>×50% 后</b>向上取整」，
        /// 不是「砍掉一半、砍的那份向上取整」（那样 7 会得 3）。两者只在奇数上有差别，
        /// 但直接决定「这一刀挡不挡得住」，所以引擎、AI、界面一律引这一个函数。</para>
        ///
        /// <para>无层数或力量 ≤0 时原样返回。</para>
        /// </summary>
        public static int Weakened(int power, int stacks)
        {
            return stacks > 0 && power > 0 ? (power + 1) / 2 : power;
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
