using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    [CreateAssetMenu(menuName = "魔法乱斗/角色配置", fileName = "Character")]
    public sealed class CharacterConfig : ScriptableObject
    {
        public string characterId = "player.default";
        public string displayName = "你";
        public CharacterKind kind = CharacterKind.Player;
        [Min(1)] public int initialHp = 4;
        [Min(1)] public int maxHp = 4;
        [Min(1)] public int minimumCardCount = 8;
        public bool useAllCards = true;
        public string[] cardIds = new string[0];
        public CharacterAbilityConfig[] abilities = new CharacterAbilityConfig[0];

        public CharacterDefinition CreateDefinition(ICardCatalog catalog = null)
        {
            var definitions = new List<ICharacterAbilityDefinition>();
            foreach (CharacterAbilityConfig ability in abilities ?? new CharacterAbilityConfig[0])
            {
                if (ability == null) throw new ArgumentException("角色能力配置不能为空。");
                definitions.Add(ability.CreateDefinition());
            }
            return new CharacterDefinition(characterId, displayName, kind, initialHp, maxHp,
                minimumCardCount, useAllCards ? CardPool.AllCards(catalog) : new CardPool(cardIds, catalog), definitions);
        }
    }

    [Serializable]
    public sealed class CharacterAbilityConfig
    {
        public string id = "ability";
        public AbilityTrigger trigger;
        public CharacterAbilityOp operation;
        [Min(0)] public int amount = 1;
        [Tooltip("0 = 不限次数；力量加值为常驻查询，使用 0。")]
        [Min(0)] public int maxUses;
        public ICharacterAbilityDefinition CreateDefinition()
        {
            return new CharacterAbilityDefinition(id, trigger, operation, amount, maxUses);
        }
    }

    [Serializable]
    public sealed class BattleParticipantConfig
    {
        public CharacterConfig character;
        public ControlKind control;
        public int team;
    }
}
