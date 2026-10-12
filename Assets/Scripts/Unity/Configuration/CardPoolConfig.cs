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
    /// <para><b>它还带一处「AI 怎么打」</b>（2026-10-08）：<see cref="useManualPriority"/> +
    /// <see cref="priorities"/> —— 手写的出牌优先级。它是这份卡池<b>唯一</b>的规则外信息，
    /// 放这里的理由很直接：这只怪能打哪些牌（卡池）与它先打哪张，是同一个策划问题。</para>
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

        /// <summary>
        /// <b>出牌优先级改成手写</b>（2026-10-08 · 用户口径：「应当格外设置一个选项，开启选项之后，
        /// 我可以自己在编辑器当中写入不同卡的使用优先级，而不会被自动计算给覆盖掉」）。
        ///
        /// <para>勾上之后，这份卡池的出牌顺序<b>完全</b>由 <see cref="priorities"/> 决定 ——
        /// AI 不再给任何卡算档位（连「回血卡永远最低 / 可回血时最高」那条全局覆盖也让位），
        /// 没登记在表里的卡一律并列最低。不勾 = 老行为（四流派自动档位 + 怪物子类的偏爱）。</para>
        ///
        /// <para>⚠ 它只影响<b>出牌选择</b>（AI 打哪张）：防御口径、流派判定、光环用法、
        /// 禁用某张牌（那是 AI 档案的事）都不动。</para>
        ///
        /// <para>⚠ <b>不要只填一半</b>：既然勾了开关就请把这张卡池里<b>在意顺序的卡</b>都写上 ——
        /// 没写的会掉到最后（这是刻意的二选一，不做「手写值 / 自动档位混着比」那种没人算得清的折衷）。</para>
        /// </summary>
        [Tooltip("勾上 = 出牌顺序完全用下面那张手写表，不再被自动计算覆盖（没登记的卡排最后）。")]
        public bool useManualPriority;

        /// <summary>
        /// 手写的出牌优先级表（<see cref="useManualPriority"/> 勾上才生效）。
        ///
        /// <para>一行 = 一张卡：<c>cardId</c> 填<b>基础 ID</b>（<c>a</c>，不是 <c>a+</c>）、
        /// <c>priority</c> <b>越小越先打出</b>（与代码里的档位同方向：0 号位最先出）。
        /// 允许重复数值（并列时取引擎给的候选顺序里靠前的那张）。</para>
        ///
        /// <para>⚠ 这份表<b>不参与</b>卡池的内容（不在池子里的 ID 也能写，只是永远不会被打出）
        /// —— 它是「顺序」不是「清单」，内容仍然只看 <see cref="cardIds"/> / <see cref="useAllCards"/>。
        /// 编辑器面板会在下面把每行的中文名 / 无效 ID 列出来核对。</para>
        /// </summary>
        [Tooltip("手写优先级：cardId 填基础 ID（a），priority 越小越先打。只在本开关勾上时生效。")]
        public CardPriorityEntry[] priorities = new CardPriorityEntry[0];

        /// <summary>这份卡池里有几个 ID（<see cref="useAllCards"/> 时无法离线得知，返回 −1）。</summary>
        public int DeclaredCount
        {
            get { return useAllCards ? -1 : (cardIds == null ? 0 : cardIds.Length); }
        }

        /// <summary>
        /// <b>与 <see cref="cardIds"/> 逐位对应的中文卡名</b>（2026-10-07）——
        /// 只读派生，**不落盘**。
        ///
        /// <para>用途只有一个：把资产里那串字母 ID（<c>a</c> / <c>ao</c> / <c>au</c>…）
        /// 在编辑器 / 日志 / 调试面板里显示成人看得懂的中文名。规则侧一律不读它，
        /// 卡表才是唯一事实源（<see cref="CardLibrary"/>）。</para>
        ///
        /// <para>⚠ 它是**读时按当前卡表翻译**的，所以卡名改了这里自动跟着变 ——
        /// 这正是它比「往资产里塞一份 string[] 快照」好的地方：那种写法一旦忘了同步，
        /// 界面上会长期挂着一个对不上号的旧名字，而且零报错。</para>
        ///
        /// <para>⚠ 与 <see cref="ResolveIds"/> 的区别：那个会**去重 + 只留强化版**，
        /// 所以它的下标与 <see cref="cardIds"/> 不再一一对应。要让「第 i 个名字 = 第 i 个 ID」
        /// 成立，就必须逐位翻译原始清单，不能拿 ResolveIds 的结果来查。</para>
        ///
        /// <para>认不出来的 ID（打错字 / 找不到的强化版）原样回填 ID 而不是空串 ——
        /// 空串在界面上是「一片空白」，看不出是这个 ID 有问题。</para>
        /// </summary>
        public IReadOnlyList<string> CardNames
        {
            get { return NameOf(cardIds); }
        }

        /// <summary>
        /// 把任意一串卡 ID 翻译成中文名（逐位对应，长度与入参一致）。
        ///
        /// <para><see cref="CardNames"/> 就是拿 <see cref="cardIds"/> 调它；
        /// 单独暴露出来是为了让调用方翻别的清单（比如运行时 <c>CardPool</c> 里的 ID）
        /// 而不必先写回资产。</para>
        /// </summary>
        public static IReadOnlyList<string> NameOf(IReadOnlyList<string> ids, ICardCatalog catalog = null)
        {
            var names = new List<string>();
            if (ids == null)
            {
                return names.AsReadOnly();
            }

            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                CardDef def = null;
                // ⚠ catalog.Get 在找不到 ID 时会**抛异常**（ICardCatalog 的契约），
                //   而这个属性是给界面 / 日志用的：一个手滑打错的 ID
                //   不该把整个 Inspector 或整帧界面打崩 —— 所以这里必须兜住。
                //   （CardLibrary 确实有个 TryGet，但它是静态类上的；
                //     ICardCatalog / CardCatalog 都没有，走不了。）
                if (!string.IsNullOrEmpty(id))
                {
                    try
                    {
                        def = source.Get(id);
                    }
                    catch (ArgumentException)
                    {
                        def = null;
                    }
                }

                names.Add(def != null ? def.Name : (id ?? string.Empty));
            }

            return names.AsReadOnly();
        }

        /// <summary>
        /// <b>中文名清单的整条字符串</b>（2026-10-07）—— 逗号分隔，例：
        /// <c>暴风雪, 冰风暴, 凝固</c>。给日志 / 调试打印用；
        /// 要和 ID 配对看的时候用 <see cref="CardNamesWithIds"/>。
        /// </summary>
        public string NamesText
        {
            get
            {
                IReadOnlyList<string> names = CardNames;
                if (names.Count == 0)
                {
                    return string.Empty;
                }

                var parts = new string[names.Count];
                for (int i = 0; i < names.Count; i++)
                {
                    parts[i] = names[i];
                }

                return string.Join(", ", parts);
            }
        }

        /// <summary>
        /// <b>「ID = 中文名」配对字符串</b>（2026-10-07），例：
        /// <c>a = 暴风雪, b = 冰风暴, au = 海啸</c>。
        /// 排查「资产里这个字母到底是哪张牌」时用它最直接。
        /// </summary>
        public string CardNamesWithIds
        {
            get
            {
                if (cardIds == null || cardIds.Length == 0)
                {
                    return string.Empty;
                }

                IReadOnlyList<string> names = CardNames;
                var parts = new List<string>(cardIds.Length);
                for (int i = 0; i < cardIds.Length; i++)
                {
                    parts.Add(cardIds[i] + " = " + names[i]);
                }

                return string.Join(", ", parts.ToArray());
            }
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

        /// <summary>
        /// <b>手写优先级表</b>（没勾开关 / 一列都没填 → <c>null</c>，表示「不插手，按自动算」）。
        ///
        /// <para><see cref="CharacterConfig.CreateDefinition"/> 把它交给
        /// <see cref="CharacterDefinition"/>，再经 <c>MonsterAgentFactory</c> 落到 AI 身上 ——
        /// 也就是「卡池资产上写的顺序，跟这只怪一起走」。</para>
        /// </summary>
        public CardPriorityTable BuildPriorityTable()
        {
            return useManualPriority ? CardPriorityTable.From(priorities) : null;
        }

        /// <summary>
        /// 手写表里 <b>不在本卡池</b> 的 ID（编辑器核对用；返回空表 = 都对得上）。
        ///
        /// <para>⚠ 只报「写了但用不到」的那些。<see cref="useAllCards"/> 时不判
        /// （那时池子就是整张卡表，没有「不在池里」这回事）。</para>
        /// </summary>
        public System.Collections.Generic.List<string> PriorityIdsOutsidePool()
        {
            var list = new System.Collections.Generic.List<string>();
            if (useAllCards || priorities == null)
            {
                return list;
            }

            for (int i = 0; i < priorities.Length; i++)
            {
                CardPriorityEntry entry = priorities[i];
                if (entry == null || string.IsNullOrEmpty(entry.cardId))
                {
                    continue;
                }

                if (!Contains(CardUpgrade.BaseIdOf(entry.cardId))
                    && !list.Contains(entry.cardId))
                {
                    list.Add(entry.cardId);
                }
            }

            return list;
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
