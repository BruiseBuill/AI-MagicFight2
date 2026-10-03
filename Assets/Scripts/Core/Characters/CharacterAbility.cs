using System;

namespace MagicBrawl.Core
{
    public enum AbilityTrigger { BattleStarted, TurnStarted, AttackPower, DefensePower, BeforeDamage, AfterDamage }

    /// <summary>
    /// 角色能力的算子（2026-09-19 首批；2026-10-03 补上「怪物回合行为」三个）。
    ///
    /// <para><b>前四个</b>是数值/资源类（回血、抽牌、力量加值、减伤），走的是
    /// <see cref="AbilityContext"/> 里的请求字段，最终由引擎落地。</para>
    ///
    /// <para><b>后三个</b>（2026-10-03）是<b>规则级的回合行为</b> —— 它们不是数值改动，
    /// 而是「这一拍不按常规走」：</para>
    /// <list type="bullet">
    /// <item><see cref="SkipAttack"/> —— 本次进攻<b>不出牌</b>，直接结束这个半场；</item>
    /// <item><see cref="DamageOpponents"/> —— 对敌方造成伤害，<b>绕过防御与光环</b>（「强制」）；</item>
    /// <item><see cref="SelfDestruct"/> —— 自身<b>立即死亡</b>。</item>
    /// </list>
    ///
    /// <para><b>为什么做成算子而不是引擎硬编码</b>：用户口径是「为自由设置怪物的能力」。
    /// 把「第 3 回合自爆」写成引擎里的一条 <c>if</c>，下一种怪（第 5 回合削血上限、
    /// 第 2 回合偷一张牌……）就得再改一次引擎。做成算子 + <see cref="CharacterAbilityDefinition.TriggerTurn"/>
    /// 之后，<b>新怪 = 怪物资产上多一条配置</b>，引擎一行不动。</para>
    /// </summary>
    public enum CharacterAbilityOp
    {
        Heal,
        DrawCards,
        AddPower,
        ReduceDamage,

        /// <summary>本次进攻不出牌（直接结束这个半场，<b>不算「手上无牌」</b>、不掉血）。</summary>
        SkipAttack,

        /// <summary>对敌方造成伤害 —— 不经防御结算、不受免疫光环影响（卡面口径里的「强制」）。</summary>
        DamageOpponents,

        /// <summary>自身立即死亡（不经过伤害结算，也没有「减伤」插手）。</summary>
        SelfDestruct,
    }

    /// <summary>
    /// 算子的<b>数值来源</b>（2026-10-03）。
    ///
    /// <para>「自爆伤害 = 当前生命值」这类口径用固定 <see cref="CharacterAbilityDefinition.Amount"/>
    /// 表达不了 —— 它是对局的函数。所以数值除了写死，还可以取自身生命。</para>
    /// </summary>
    public enum AbilityAmountSource
    {
        /// <summary>用 <see cref="CharacterAbilityDefinition.Amount"/> 写的那个数。</summary>
        Fixed = 0,

        /// <summary>自身<b>当前</b>生命。</summary>
        OwnHp = 1,

        /// <summary>自身生命上限。</summary>
        OwnMaxHp = 2,

        /// <summary>自身<b>已损失</b>的生命（上限 − 当前）。</summary>
        MissingHp = 3,
    }

    public interface ICharacterAbilityDefinition
    {
        ICharacterAbility CreateRuntime();
    }

    /// <summary>AttackPower/DefensePower 是纯查询：不得消耗次数、抽牌、治疗或修改外部状态。</summary>
    public interface ICharacterAbility
    {
        void Apply(AbilityContext context);
    }

    /// <summary>能力的受限结算接口。数值修改与抽牌/治疗最终仍由引擎落地。</summary>
    public sealed class AbilityContext
    {
        public AbilityTrigger Trigger { get; internal set; }
        public int Seat { get; internal set; }
        public int TurnNumber { get; internal set; }
        public int Hp { get; internal set; }
        public int InitialHp { get; internal set; }
        public int MaxHp { get; internal set; }
        public int Value { get; set; }
        public int HealRequested { get; private set; }
        public int DrawRequested { get; private set; }

        /// <summary>本次进攻不出的牌（<see cref="CharacterAbilityOp.SkipAttack"/>）。</summary>
        public bool AttackSkipped { get; private set; }

        /// <summary>本拍要向敌方强制的伤害合计（<see cref="CharacterAbilityOp.DamageOpponents"/>）。</summary>
        public int DamageRequested { get; private set; }

        /// <summary>本拍自身要自毁（<see cref="CharacterAbilityOp.SelfDestruct"/>）。</summary>
        public bool SelfDestructRequested { get; private set; }

        public void Heal(int amount) { HealRequested += Math.Max(0, amount); }
        public void Draw(int count) { DrawRequested += Math.Max(0, count); }

        public void SkipAttack() { AttackSkipped = true; }
        public void DamageOpponents(int amount) { DamageRequested += Math.Max(0, amount); }
        public void SelfDestruct() { SelfDestructRequested = true; }
    }

    /// <summary>首批可配置算子；复杂角色可另实现 ICharacterAbilityDefinition。</summary>
    public sealed class CharacterAbilityDefinition : ICharacterAbilityDefinition
    {
        public readonly string Id;
        public readonly AbilityTrigger Trigger;
        public readonly CharacterAbilityOp Operation;
        public readonly int Amount;
        public readonly int MaxUses;

        /// <summary>
        /// <b>只在第 N 回合触发</b>；0 = 不限（每次该触发时机都算）。
        ///
        /// <para>口径是<b>对局回合号</b>（<see cref="BattleState.TurnNumber"/>，
        /// 由 <see cref="AbilityContext.TurnNumber"/> 传进来），不是「轮到我的第 N 次」
        /// —— 「第 3 回合自爆」指的是整局第 3 回合。</para>
        /// </summary>
        public readonly int TriggerTurn;

        /// <summary><see cref="Amount"/> 的含义（写死 / 取自身生命）。</summary>
        public readonly AbilityAmountSource AmountSource;

        public CharacterAbilityDefinition(string id, AbilityTrigger trigger, CharacterAbilityOp operation,
            int amount, int maxUses = 0, int triggerTurn = 0,
            AbilityAmountSource amountSource = AbilityAmountSource.Fixed)
        {
            if (string.IsNullOrWhiteSpace(id) || amount < 0 || maxUses < 0 || triggerTurn < 0)
                throw new ArgumentException("能力 ID/数值不合法。");
            bool power = trigger == AbilityTrigger.AttackPower || trigger == AbilityTrigger.DefensePower;
            if (power && maxUses != 0) throw new ArgumentException("力量加值是常驻查询，MaxUses 必须为 0。有限次数能力需实现独立的触发策略。");
            if (operation == CharacterAbilityOp.AddPower && !power
                || operation == CharacterAbilityOp.ReduceDamage && trigger != AbilityTrigger.BeforeDamage
                || (operation == CharacterAbilityOp.Heal || operation == CharacterAbilityOp.DrawCards) && power)
                throw new ArgumentException("能力算子与触发时机不兼容。");

            // 2026-10-03：三个规则级算子既不是数值加值，也不是「每次攻防都跑一遍」的纯查询 ——
            // 它们只在「这个角色开始一个新的半场」那一刻才有意义（BattleStarted 也允许，
            // 用来表达「一开局就自爆」这种极端设计）。
            bool ruleLevel = operation == CharacterAbilityOp.SkipAttack
                             || operation == CharacterAbilityOp.DamageOpponents
                             || operation == CharacterAbilityOp.SelfDestruct;
            if (ruleLevel && power) throw new ArgumentException("规则级算子（不出牌 / 强制伤害 / 自爆）不能挂在力量查询触发上。");
            if (operation == CharacterAbilityOp.SkipAttack && amountSource != AbilityAmountSource.Fixed)
                throw new ArgumentException("「不出牌」不取数值，AmountSource 必须是 Fixed。");
            if (operation == CharacterAbilityOp.SelfDestruct && amount != 0)
                throw new ArgumentException("「自爆」不取数值 —— 伤害由另一条 DamageOpponents 表达，Amount 必须为 0。");
            if (operation != CharacterAbilityOp.DamageOpponents && amountSource != AbilityAmountSource.Fixed)
                throw new ArgumentException("只有「强制伤害」支持按自身生命取数值。");
            if (amountSource == AbilityAmountSource.Fixed && operation == CharacterAbilityOp.DamageOpponents && amount <= 0)
                throw new ArgumentException("固定数值的「强制伤害」必须大于 0。");

            Id = id; Trigger = trigger; Operation = operation; Amount = amount; MaxUses = maxUses;
            TriggerTurn = triggerTurn;
            AmountSource = amountSource;
        }

        /// <summary>把 <see cref="AmountSource"/> 解析成这一拍真正的数值。</summary>
        internal int ResolveAmount(AbilityContext context)
        {
            switch (AmountSource)
            {
                case AbilityAmountSource.OwnHp:
                    return Math.Max(0, context.Hp);
                case AbilityAmountSource.OwnMaxHp:
                    return Math.Max(0, context.MaxHp);
                case AbilityAmountSource.MissingHp:
                    return Math.Max(0, context.MaxHp - context.Hp);
                default:
                    return Amount;
            }
        }

        public ICharacterAbility CreateRuntime() { return new Runtime(this); }

        private sealed class Runtime : ICharacterAbility
        {
            private readonly CharacterAbilityDefinition _definition;
            private int _uses;
            public Runtime(CharacterAbilityDefinition definition) { _definition = definition; }
            public void Apply(AbilityContext context)
            {
                if (context.Trigger != _definition.Trigger || _definition.MaxUses > 0 && _uses >= _definition.MaxUses) return;

                // 限回合的能力（2026-10-03）：只在指定的那一回合生效。
                // ⚠ 判在「消耗次数」之前 —— 不满足回合就完全不发生，也不该白白吃掉一次次数。
                if (_definition.TriggerTurn > 0 && context.TurnNumber != _definition.TriggerTurn) return;

                // 力量钩子是纯查询，UI/光环准备重发决策时不得消耗能力次数。
                if (context.Trigger != AbilityTrigger.AttackPower && context.Trigger != AbilityTrigger.DefensePower) _uses++;
                switch (_definition.Operation)
                {
                    case CharacterAbilityOp.Heal: context.Heal(_definition.Amount); break;
                    case CharacterAbilityOp.DrawCards: context.Draw(_definition.Amount); break;
                    case CharacterAbilityOp.AddPower: context.Value += _definition.Amount; break;
                    case CharacterAbilityOp.ReduceDamage: context.Value = Math.Max(0, context.Value - _definition.Amount); break;
                    case CharacterAbilityOp.SkipAttack: context.SkipAttack(); break;
                    case CharacterAbilityOp.DamageOpponents: context.DamageOpponents(_definition.ResolveAmount(context)); break;
                    case CharacterAbilityOp.SelfDestruct: context.SelfDestruct(); break;
                }
            }
        }
    }
}
