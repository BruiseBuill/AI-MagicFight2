using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    [CreateAssetMenu(menuName = "魔法乱斗/卡牌定义", fileName = "CardDefinition")]
    public sealed class CardDefinitionAsset : ScriptableObject
    {
        public string cardId = "generated.card";
        public string displayName = "新卡牌";
        [Min(0)] public int power = 1;
        [Min(1)] public int cooldown = 3;
        public bool hiddenPower;
        public string artId;
        [Min(1)] public int version = 1;
        public CardEffectAsset[] effects = new CardEffectAsset[0];

        public CardDef CreateDefinition(int index = 10000)
        {
            var list = new List<EffectDef>();
            foreach (CardEffectAsset effect in effects ?? new CardEffectAsset[0])
            {
                if (effect == null) throw new ArgumentException("卡牌效果配置不能为空。");
                list.Add(effect.CreateDefinition());
            }
            return new CardDef(index, cardId, displayName, power, cooldown, list.AsReadOnly(), hiddenPower,
                string.IsNullOrEmpty(artId) ? cardId : artId, version);
        }
    }

    [Serializable]
    public sealed class CardEffectAsset
    {
        public EffectTrigger trigger;
        public EffectOp operation;
        public string handlerId;
        public int amount;
        public int secondary;
        public int cooldownAdjustment;
        public AuraKind aura;
        public bool mandatory;
        public string specialEvent;
        public string distinctTargetGroup;
        public string text;
        public CardEffectConditionAsset[] conditions = new CardEffectConditionAsset[0];

        public EffectDef CreateDefinition()
        {
            string id = string.IsNullOrEmpty(handlerId) ? operation.ToString() : handlerId;
            if (string.IsNullOrEmpty(handlerId))
                return new EffectDef(trigger, operation, amount, secondary, cooldownAdjustment, aura, mandatory, text, distinctTargetGroup);
            var args = new Dictionary<string, int> { { "amount", amount }, { "secondary", secondary }, { "cooldownAdjustment", cooldownAdjustment } };
            var rules = new List<EffectCondition>();
            foreach (CardEffectConditionAsset condition in conditions ?? new CardEffectConditionAsset[0])
                if (condition != null) rules.Add(new EffectCondition(condition.id, condition.value));
            return new EffectDef(trigger, id, args, aura, text, specialEvent,
                EffectTargetScope.Participants, distinctTargetGroup, rules, mandatory);
        }
    }

    [Serializable]
    public sealed class CardEffectConditionAsset
    {
        public string id;
        public int value;
    }

    [CreateAssetMenu(menuName = "魔法乱斗/卡牌目录", fileName = "CardCatalog")]
    public sealed class CardCatalogAsset : ScriptableObject
    {
        public bool includeBuiltinCards = true;
        public CardDefinitionAsset[] cards = new CardDefinitionAsset[0];
        public CardDefinitionAsset[] generatedCards = new CardDefinitionAsset[0];

        public ICardCatalog CreateCatalog()
        {
            var list = new List<CardDef>();
            if (includeBuiltinCards) list.AddRange(CardLibrary.All);
            foreach (CardDefinitionAsset card in cards ?? new CardDefinitionAsset[0])
                if (card != null) list.Add(card.CreateDefinition(list.Count + 1000));
            foreach (CardDefinitionAsset card in generatedCards ?? new CardDefinitionAsset[0])
                if (card != null) list.Add(card.CreateDefinition(list.Count + 10000));
            return new CardCatalog(list);
        }

        public CardDef CreateRuntimeCard(CardDefinitionAsset card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            return card.CreateDefinition(10000 + generatedCards.Length);
        }
    }
}
