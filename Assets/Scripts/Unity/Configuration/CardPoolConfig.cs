using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>一份卡池配置</b>（2026-09-30）—— 把「哪些牌属于这个场景」从代码里搬进资产。
    ///
    /// <para><b>为什么要有它</b>：在此之前，「卡池」在三处各写了一份 ——
    /// 战斗读 <see cref="CharacterConfig"/>（<c>useAllCards</c> / <c>cardIds</c>）、
    /// 商店把 8 张起始牌池<b>写死在 <c>ShopSceneEntry</c> 的静态数组里</b>、
    /// 而强化场景只能再抄一遍。三份同名不同源的清单，改一处漏一处查不出来
    /// （本工程在「已持有卡」上已经定过这条口径：补集与背包必须是同一份）。
    /// 现在三份都是这个资产类型的实例，各场景读自己那份，<b>卡池分离</b>但不重复定义。</para>
    ///
    /// <para><b>⚠ 它不是 <see cref="CardPool"/></b>：那个是 Core 里的运行期对象
    /// （持有 <c>ICardCatalog</c>，能 Resolve 成 <c>CardDef</c>）；这个是编辑器里编辑的
    /// <b>资产</b>，只存 ID 清单，运行时由 <see cref="CreatePool"/> 转成 <see cref="CardPool"/>。
    /// 把 catalog 烘进资产会让「改了卡表但没重存资产」变成一个静默的旧快照。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "魔法乱斗/卡池", fileName = "CardPool")]
    public sealed class CardPoolConfig : ScriptableObject
    {
        /// <summary>战斗卡池的 Resources 路径（不含扩展名）。</summary>
        public const string BattlePoolResourcePath = "Pools/BattlePool";

        /// <summary>商店卡池的 Resources 路径（不含扩展名）。</summary>
        public const string ShopPoolResourcePath = "Pools/ShopPool";

        /// <summary>强化卡池的 Resources 路径（不含扩展名）。</summary>
        public const string UpgradePoolResourcePath = "Pools/UpgradePool";

        /// <summary>三份卡池资产在工程里的目录（构建器建它们，运行时按上面的路径取）。</summary>
        public const string PoolFolder = "Assets/Resources/Pools";

        /// <summary>稳定 ID（存档 / 日志用；别用文件名当 ID，改个名就变了）。</summary>
        public string poolId = "pool";

        /// <summary>给人看的名字（日志、以后可能上界面）。</summary>
        public string displayName = "卡池";

        /// <summary>
        /// 勾上 = 用卡表里的<b>全部</b>卡（此时 <see cref="cardIds"/> 不生效）。
        ///
        /// <para>战斗卡池用它保持与改造前逐字一致的行为（42 张全开）——
        /// ⚠ 「强化过的卡要不要进战斗卡池」是**冒险链路**的事（还没有 run / 存档），
        /// 本批不碰，所以这里维持「全开」而不是另立一套筛选。</para>
        /// </summary>
        public bool useAllCards;

        /// <summary>卡 ID 清单（按卡表顺序解析，与 <see cref="CardPool.Resolve"/> 同口径）。</summary>
        public string[] cardIds = new string[0];

        /// <summary>
        /// 同一张牌的<b>基础版与强化版同时存在时，只保留强化版</b>（默认开）。
        ///
        /// <para><b>为什么这是一条必须写在卡池上的规则</b>：强化会产生一张新卡
        /// （ID = 基础 ID 挂一个 <c>+</c>，见 <see cref="CardUpgrade"/>），
        /// 而「内置 42 张」那份清单永远还在。没有这条规则时，
        /// <c>useAllCards</c> 的卡池会同时拿到 <c>a</c> 与 <c>a+</c> ——
        /// 玩家手里凭空多出一张同名牌，而且零报错。
        /// <c>Docs/design/冒险模式实施规格.md</c> §10.2 明确禁止这件事
        /// （「不让基础版与强化版作为两张牌同时进卡池」）。</para>
        ///
        /// <para><b>它同时是「永久 +2」的闭环点</b>：卡池里写的是基础 ID <c>a</c>，
        /// 但只要 <c>a+</c> 已经落盘存在，解析出来就是强化版 ——
        /// 所以「强化过一次，下次进来那张牌还是 6 点」不需要额外的存档。</para>
        /// </summary>
        public bool preferUpgraded = true;

        /// <summary>这份卡池里有几个 ID（<see cref="useAllCards"/> 时无法离线得知，返回 −1）。</summary>
        public int DeclaredCount
        {
            get { return useAllCards ? -1 : (cardIds == null ? 0 : cardIds.Length); }
        }

        /// <summary>这个 ID 在不在池子里。</summary>
        public bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (cardIds == null)
            {
                return false;
            }

            for (int i = 0; i < cardIds.Length; i++)
            {
                if (string.Equals(cardIds[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 解析成**去重后的 ID 清单**（<see cref="preferUpgraded"/> 在这里生效）。
        ///
        /// <para>去重规则只有一条：清单里若同时有 <c>a</c> 与 <c>a+</c>，去掉 <c>a</c>。
        /// 判定用的是清单本身（不是卡表），所以 <c>useAllCards</c> 与手写 ID 两种口径都走同一段逻辑。</para>
        /// </summary>
        public IReadOnlyList<string> ResolveIds(ICardCatalog catalog = null)
        {
            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            var ids = new List<string>();

            if (useAllCards)
            {
                foreach (CardDef card in source.All)
                {
                    if (card != null && !ids.Contains(card.Id))
                    {
                        ids.Add(card.Id);
                    }
                }
            }
            else if (cardIds != null)
            {
                for (int i = 0; i < cardIds.Length; i++)
                {
                    string id = cardIds[i];
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            if (!preferUpgraded)
            {
                return ids.AsReadOnly();
            }

            // ⚠ 规则本体在 <see cref="CardUpgrade.PreferUpgraded"/>（Core）——
            //   存档（SaveStore）也要走同一条，两边各写一份的下场是
            //   「从卡池资产进场景是强化版、从存档进场景又变回基础版」，而且零报错。
            //   判据是「**卡表里**有没有 <id>+」而不是「清单里有没有」：卡池资产与存档里
            //   写的永远只是基础 ID（"a"），强化版只存在于卡目录的动态卡区。
            return CardUpgrade.PreferUpgraded(ids, source);
        }

        /// <summary>
        /// 解析成有序的卡定义列表（按 <paramref name="catalog"/> 的顺序，同 <see cref="CardPool.Resolve"/>）。
        ///
        /// <para>⚠ 用 catalog 的顺序而不是 <see cref="cardIds"/> 的顺序：同种子复现要求牌序稳定，
        /// 而勾选顺序是会变的。</para>
        /// </summary>
        public IReadOnlyList<CardDef> Resolve(ICardCatalog catalog = null)
        {
            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            // ⚠ 用 HashSet 而不是 `ids.Contains(...)`：`IReadOnlyList<string>` 上没有
            //   `Contains`，直接写会解析到 `MemoryExtensions.Contains(ReadOnlySpan<char>, …)`
            //   编译不过（2026-10-01 踩过）。
            var set = new HashSet<string>(ResolveIds(source), StringComparer.Ordinal);
            var list = new List<CardDef>();

            foreach (CardDef card in source.All)
            {
                if (card != null && set.Contains(card.Id))
                {
                    list.Add(card);
                }
            }

            return list.AsReadOnly();
        }

        /// <summary>转成运行期的 <see cref="CardPool"/>（可以喂给 <see cref="CharacterDefinition"/>）。</summary>
        public CardPool CreatePool(ICardCatalog catalog = null)
        {
            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            return new CardPool(ResolveIds(source), source);
        }

        /// <summary>批量写入（编辑器生成器用）。</summary>
        public void SetIds(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                cardIds = new string[0];
                return;
            }

            var list = new List<string>();
            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                {
                    list.Add(id);
                }
            }

            cardIds = list.ToArray();
        }
    }
}
