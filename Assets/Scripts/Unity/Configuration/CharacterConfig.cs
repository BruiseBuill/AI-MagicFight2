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

        /// <summary>
        /// 这个角色用的<b>卡池资产</b>（2026-09-30 起；见 <see cref="CardPoolConfig"/>）。
        ///
        /// <para>填了就<b>以它为准</b>，下面的 <see cref="useAllCards"/> / <see cref="cardIds"/>
        /// 不再生效 —— 旧的单字段写法保留是为了不破坏已经存过值的资产
        /// （<c>Resources/Characters/DefaultPlayer.asset</c> 等），不是「两个来源都算」。</para>
        ///
        /// <para><b>⚠ 别两边各配一半</b>：那正是「卡池分离」之前三处各写一份清单的老问题。</para>
        /// </summary>
        [Tooltip("填了就以此卡池资产为准；留空才用下面的 useAllCards / cardIds。")]
        public CardPoolConfig cardPool;

        public CharacterAbilityConfig[] abilities = new CharacterAbilityConfig[0];

        public CharacterDefinition CreateDefinition(ICardCatalog catalog = null)
        {
            var definitions = new List<ICharacterAbilityDefinition>();
            foreach (CharacterAbilityConfig ability in abilities ?? new CharacterAbilityConfig[0])
            {
                if (ability == null) throw new ArgumentException("角色能力配置不能为空。");
                definitions.Add(ability.CreateDefinition());
            }
            CardPool pool = cardPool != null
                ? cardPool.CreatePool(catalog)
                : (useAllCards ? CardPool.AllCards(catalog) : new CardPool(cardIds, catalog));
            return new CharacterDefinition(characterId, displayName, kind, initialHp, maxHp,
                minimumCardCount, pool, definitions);
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
