using System;

namespace MagicBrawl.Core
{
    public enum AbilityTrigger { BattleStarted, TurnStarted, AttackPower, DefensePower, BeforeDamage, AfterDamage }
    public enum CharacterAbilityOp { Heal, DrawCards, AddPower, ReduceDamage }

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
        public void Heal(int amount) { HealRequested += Math.Max(0, amount); }
        public void Draw(int count) { DrawRequested += Math.Max(0, count); }
    }

    /// <summary>首批可配置算子；复杂角色可另实现 ICharacterAbilityDefinition。</summary>
    public sealed class CharacterAbilityDefinition : ICharacterAbilityDefinition
    {
        public readonly string Id;
        public readonly AbilityTrigger Trigger;
        public readonly CharacterAbilityOp Operation;
        public readonly int Amount;
        public readonly int MaxUses;

        public CharacterAbilityDefinition(string id, AbilityTrigger trigger, CharacterAbilityOp operation,
            int amount, int maxUses = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || amount < 0 || maxUses < 0)
                throw new ArgumentException("能力 ID/数值不合法。");
            bool power = trigger == AbilityTrigger.AttackPower || trigger == AbilityTrigger.DefensePower;
            if (power && maxUses != 0) throw new ArgumentException("力量加值是常驻查询，MaxUses 必须为 0。有限次数能力需实现独立的触发策略。");
            if (operation == CharacterAbilityOp.AddPower && !power
                || operation == CharacterAbilityOp.ReduceDamage && trigger != AbilityTrigger.BeforeDamage
                || (operation == CharacterAbilityOp.Heal || operation == CharacterAbilityOp.DrawCards) && power)
                throw new ArgumentException("能力算子与触发时机不兼容。");
            Id = id; Trigger = trigger; Operation = operation; Amount = amount; MaxUses = maxUses;
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
                // 力量钩子是纯查询，UI/光环准备重发决策时不得消耗能力次数。
                if (context.Trigger != AbilityTrigger.AttackPower && context.Trigger != AbilityTrigger.DefensePower) _uses++;
                switch (_definition.Operation)
                {
                    case CharacterAbilityOp.Heal: context.Heal(_definition.Amount); break;
                    case CharacterAbilityOp.DrawCards: context.Draw(_definition.Amount); break;
                    case CharacterAbilityOp.AddPower: context.Value += _definition.Amount; break;
                    case CharacterAbilityOp.ReduceDamage: context.Value = Math.Max(0, context.Value - _definition.Amount); break;
                }
            }
        }
    }
}
