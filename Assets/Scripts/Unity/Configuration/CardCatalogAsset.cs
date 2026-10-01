using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 卡目录资产：内置卡表的开关 + 两张「额外卡」清单。
    ///
    /// <para><b>为什么要单开一个文件</b>：Unity 的 <c>m_Script</c> 引用指向的是
    /// <b>一个文件一个 MonoScript</b>，而那个 MonoScript 的类型 = **与文件同名的那个类**。
    /// 这个类原来和 <see cref="CardDefinitionAsset"/> 挤在同一个 .cs 里 ——
    /// 结果是创建 <c>CardCatalog.asset</c> 时 Unity 直接报
    /// <c>No script asset for CardCatalogAsset</c>，把资产写成
    /// <c>m_Script: {fileID: 0}</c>，**之后重开工程它就是一个空壳**（Load 返回 null，
    /// 强化卡整批从卡池里消失，而且零报错）。
    /// 这与「MonoBehaviour 一文件一类，否则 Prefab 的 m_Script 变 {fileID:0}」是同一条规则，
    /// 只是 ScriptableObject 这边以前没真的落过盘，所以一直没暴露。</para>
    /// </summary>
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
            if (card == null) throw new System.ArgumentNullException(nameof(card));
            return card.CreateDefinition(10000 + generatedCards.Length);
        }
    }
}
