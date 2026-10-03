using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 商店场景的<b>单场景入口</b>（2026-09-26 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>为什么需要它</b>：商店是冒险模式里的一个事件节点，正式流程中它会从地图层
    /// 接到一份 <c>RunSnapshot</c> + 一个商店种子。但那套链路还没接（P4 只做「商店这一个切片」），
    /// 而<b>每个事件类型都必须能被单独进 Play 调试</b>（架构文档 §3）——
    /// 否则想知道「第 3 个货位显示对不对」就得先把整条冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：一切参数由本组件的序列化字段给（调试默认值），不做任何前置加载；</item>
    /// <item><b>E2 不依赖前一节点</b>：场景直接以「一个金币 50、生命 4/4」的假 Run 开局；</item>
    /// <item><b>E3 读主存档、也写主存档</b>（<b>2026-10-01 改口径</b>，原先写的是「完全不碰存档」）：
    /// 玩家卡池从 <see cref="SaveSlot.Main"/> 读，买下一张牌之后<b>写回同一份</b>。
    /// 「一个存档 = 一个玩家卡池」要求战斗 / 商店 / 强化 / 女巫工坊读的是同一份，
    /// 所以商店不能再自带一份清单。⚠ 写回只在「没走 <c>_ownedCardIds</c> 调试覆盖」
    /// 且「读档成功」时才发生（见 <c>_loadedFromMainSave</c>）——
    /// 否则拿调试数据覆盖真实存档是另一种事故；</item>
    /// <item><b>E4 Core 一行不改</b>：这里只<b>读</b>卡表与商店规则，
    /// 自己把结果翻译成 <see cref="ShopView.ShelfEntry"/>，不存在「第二套商店逻辑」。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：正式接入时，货架快照应当由 Core 侧
    /// （冒险的商店节点）算出、再喂给 <see cref="ShopView.BindShelf"/>；
    /// 本类里那段「抽 4 张 + 派特价」的算法到时<b>必须删掉</b>，不能原地留着当第二条实现。
    /// 它在今天的位置相当于「一张写着待办的字条」，不是最终架构。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：见构建器里对它的处理 —— 它只在
    /// <c>Shop.unity</c> 里存在，构建 <c>ShopCanvas.prefab</c> 时不复制。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopSceneEntry : MonoBehaviour
    {
        // ── 调试参数（正式接入后由地图层 / 存档给）────────────────────

        [Header("调试 · 角色与资源")]
        [SerializeField] private string _playerName = "旅者";

        [Tooltip("当前生命（调试默认值；商店不改变生命，只显示）")]
        [SerializeField] private int _hp = 4;

        [Tooltip("生命上限")]
        [SerializeField] private int _maxHp = 4;

        [Tooltip("金币（架构文档给的调试值 = 50）")]
        [SerializeField] private int _gold = 50;

        [Header("调试 · 货架")]
        [Tooltip("商店随机种子。同一个种子 → 同一批货、同一个特价位（可复现）。")]
        [SerializeField] private int _seed = 20260926;

        [Tooltip("特价位的价格（原价 20，特价 10）")]
        [SerializeField] private int _discountPrice = 10;

        [SerializeField] private int _fullPrice = 20;

        [Tooltip("勾上 = 每次都重新抽（不用随机种子的固定结果）。默认关，保证截图可复现。")]
        [SerializeField] private bool _rerollEveryTime;

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「离开」的点击打到 Console —— 方便在单场景里确认事件通了（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logLeaveClick = true;

        [Tooltip("「玩家已持有」的卡 id —— 货架只卖这张单子之外的卡，背包里显示的就是这一份。\n"
                 + "留空 = 读主存档（Resources 下的卡池资产已不再参与，2026-10-01 起）。\n"
                 + "填了就走调试覆盖：此时**不读写存档**（拿调试数据覆盖真实存档是事故）。\n"
                 + "想验「牌不够 4 张时缺位显示卖光了」就把这里填到只剩两三张。")]
        [SerializeField] private string[] _ownedCardIds = new string[0];

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private ShopView _view;

        /// <summary>本次运行用的卡目录（内置 45 张 + 已落盘的强化卡）—— 背包里要显示强化版就得用它。</summary>
        private ICardCatalog _catalog;

        /// <summary>当前货架（内部保留，供点击时查价）。</summary>
        private readonly List<ShopView.ShelfEntry> _shelf = new List<ShopView.ShelfEntry>();

        /// <summary>
        /// 主角<b>当前持有</b>的卡 id（= 背包的内容）。
        ///
        /// <para>开局由主存档（或 <see cref="_ownedCardIds"/> 调试覆盖）解析而来，
        /// 买下一张牌之后会当场加进来并<b>写回主存档</b> —— 所以
        /// 「买完点开背包，多出来的那一张就在里面」与「换个场景它还在」两条链路都是通的。</para>
        /// </summary>
        private readonly List<string> _owned = new List<string>();

        /// <summary>
        /// 本次 <see cref="_owned"/> 是不是从<b>主存档</b>读来的。
        ///
        /// <para>只有为 <c>true</c> 才允许写回：调试覆盖（<see cref="_ownedCardIds"/> 非空）
        /// 与「读档失败回退空卡池」两种情形都不该拿手上的数据去覆盖存档。</para>
        /// </summary>
        private bool _loadedFromMainSave;

        /// <summary>
        /// 起始（已持有）卡池的**唯一来源** —— <b>主存档</b>的玩家卡池。
        ///
        /// <para><b>⚠ 2026-10-01 统一</b>：原先这里是「Inspector 覆盖 → <c>ShopPool</c> 资产」，
        /// 而强化 / 女巫工坊读的是另一份 <c>UpgradePool</c>、战斗读的是第三份 ——
        /// 四个场景四个玩家。现在四个场景都从 <see cref="SaveStore"/> 取同一个玩家
        /// （按 <see cref="SaveSlot"/> 分档），本类只认 <see cref="SaveSlot.Main"/>。
        /// <c>ShopPool</c> / <c>UpgradePool</c> 两份资产因此<b>不再被运行时读取</b>。</para>
        /// </summary>
        private void ResolveOwned()
        {
            _owned.Clear();
            _loadedFromMainSave = false;

            // ① Inspector 直接写的 id（调试用，优先级最高；此时不碰存档）
            if (_ownedCardIds != null && _ownedCardIds.Length > 0)
            {
                for (int i = 0; i < _ownedCardIds.Length; i++)
                {
                    AddOwned(_ownedCardIds[i]);
                }

                return;
            }

            // ② 主存档（唯一的正式来源）
            PlayerData player;
            string error;
            if (!SaveStore.TryLoadPlayer(SaveSlot.Main, out player, out error))
            {
                Debug.LogWarning("[ShopSceneEntry] 读主存档失败，本局按空卡池起步：" + error);
                return;
            }

            if (player == null || player.cardIds == null)
            {
                return;
            }

            IReadOnlyList<string> ids = CardUpgrade.PreferUpgraded(player.cardIds, _catalog);
            for (int i = 0; i < ids.Count; i++)
            {
                AddOwned(ids[i]);
            }

            _loadedFromMainSave = true;
            Debug.Log("[ShopSceneEntry] 主存档卡池 " + _owned.Count + " 张（"
                      + SaveStore.FilePathFor(SaveSlot.Main) + "）");
        }

        /// <summary>把一个 id 收进「已持有」（去重 + 查目录；不在目录里的跳过并报警）。</summary>
        private void AddOwned(string id)
        {
            if (string.IsNullOrEmpty(id) || _owned.Contains(id))
            {
                return;
            }

            if (!KnownInCatalog(id))
            {
                Debug.LogWarning("[ShopSceneEntry] 已持有卡 id 不在卡目录里，已跳过：" + id);
                return;
            }

            _owned.Add(id);
        }

        /// <summary>这个 id 在不在<b>本次运行的卡目录</b>里。
        /// ⚠ 不能用 <c>CardLibrary.TryGet</c>：强化卡（<c>"a+"</c>）只存在于卡目录的动态卡区，
        /// 用内置卡表查会查不到 → 症状是「强化过的牌在背包里凭空消失」，零报错。</summary>
        private bool KnownInCatalog(string id)
        {
            if (_catalog == null)
            {
                return false;
            }

            IReadOnlyList<CardDef> all = _catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<ShopView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[ShopSceneEntry] 没有接 ShopView —— 先跑 `魔法乱斗/P4 · 构建 Shop 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）。
            //   构建器接的那一份不是序列化数据，存场景时蒸发。
            _view.SlotClicked += OnSlotClicked;
            _view.LeaveClicked += OnLeaveClicked;
            _view.BagClicked += OnBagClicked;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.SlotClicked -= OnSlotClicked;
            _view.LeaveClicked -= OnLeaveClicked;
            _view.BagClicked -= OnBagClicked;
        }

        /// <summary>按当前调试参数重抽一次货架（Inspector 的右键菜单也可以调）。</summary>
        [ContextMenu("重抽货架")]
        public void Rebuild()
        {
            if (_view == null)
            {
                return;
            }

            // ⚠ 目录要叠上主存档的强化册（2026-10-02）：否则强化过的牌（"a+"）不在目录里，
            //   背包会把它们当「不在目录里的 ID」跳过 —— 症状是「强化过的牌凭空消失」且零报错。
            _catalog = SaveStore.WithSavedUpgrades(SaveSlot.Main, ResolveCatalog());

            // 2026-10-02：这一趟是从地图走进来的话，金币用<b>冒险那本账</b>的余额
            //（地图顶栏显示的、上一家商店花剩下的），而不是本场景的调试默认值。
            // ⚠ 单向：只在 HasActiveRun 时覆盖 —— 单独打开 Shop.unity 调试时 MapRun 是空的，
            //   本场景照旧按 Inspector 上的 _gold 跑（独立场景契约 E1/E2）。
            if (MapRun.HasActiveRun)
            {
                _gold = MapRun.Gold;
                _playerName = "旅者";
            }

            _view.SetTitle("商店");
            _view.SetLeaveLabel("离开");
            _view.BindResources(_playerName, _hp, _maxHp, _gold);

            ResolveOwned();
            _view.BindBag(ResolveOwnedCards());

            BuildShelf();
            _view.BindShelf(_shelf);
        }

        /// <summary>
        /// 取本次运行用的卡目录（内置 45 张 + 已落盘的强化卡）。
        ///
        /// <para>必须与战斗 / 强化 / 女巫用的是**同一份** <c>CardCatalogAsset</c> ——
        /// 强化卡只存在于那份目录的「动态卡」清单里。</para>
        /// </summary>
        private ICardCatalog ResolveCatalog()
        {
            CardCatalogAsset asset = Resources.Load<CardCatalogAsset>("CardCatalog");
            return asset == null ? CardCatalog.Builtin() : asset.CreateCatalog();
        }

        // ══════════════════════════════════════════════════════
        //  主角当前持有（= 背包的内容）
        // ══════════════════════════════════════════════════════
        //
        //  ⚠ <c>ResolveOwned</c> / <c>AddOwned</c> 在文件上半部分（紧挨着 <c>_owned</c> 字段）——
        //    因为那一处口径要同时喂两个地方：货架的「未持有池」（补集）与背包的卡池。
        //    两者各算各的，就会出现「背包里明明有暴风雪、货架上还在卖暴风雪」这种
        //    一眼假、但零报错的画面，所以合一，并且只有一份实现。

        /// <summary>把「已持有 id」翻译成卡表定义（按<b>目录顺序</b>，与 <c>CardPool.Resolve</c> 同口径）。</summary>
        private List<CardDef> ResolveOwnedCards()
        {
            var cards = new List<CardDef>();
            if (_catalog == null)
            {
                return cards;
            }

            IReadOnlyList<CardDef> all = _catalog.All;

            for (int i = 0; i < all.Count; i++)
            {
                if (_owned.Contains(all[i].Id))
                {
                    cards.Add(all[i]);
                }
            }

            return cards;
        }

        // ══════════════════════════════════════════════════════
        //  货架（⚠ 临时实现 —— 正式接入时搬进 Core，见类注释）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 抽 4 张「玩家尚未持有」的卡，并用派生种子指定一个是特价。
        ///
        /// <para>规则见 <c>Docs/design/冒险事件架构.md</c> §5：货位 4 个（3 原价 20 + 1 特价 10）；
        /// 商品只来自未持有池、<b>无放回</b>；不足 4 张时缺位读作「卖光了」。</para>
        ///
        /// <para><b>⚠ 「未持有池」今天用的是「本局卡池里还没有的卡」</b>：
        /// 正式流程里它应该是「这次冒险 run 里玩家已获得的卡」的补集；
        /// 本类<b>没有 run</b>，所以退化成 <c>本局选择卡池的补集</c> —— 效果上就是
        /// 「货架上不会出现你已经带在身上的牌」，与规则的口径一致，
        /// 但来源是「本局卡池」而不是「冒险持有」，这一点在正式接入时要换掉。</para>
        /// </summary>
        private void BuildShelf()
        {
            _shelf.Clear();

            List<CardDef> pool = CollectUnshelvedPool();

            // 用派生种子（与商店种子区分开）洗牌，这样「换存档种子」和「刷新货架」
            // 不会碰巧给出同一个排列。
            int seed = _rerollEveryTime ? UnityEngine.Random.Range(int.MinValue, int.MaxValue) : _seed;
            var rng = new System.Random(seed ^ 0x5A17);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                CardDef tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            // 特价位：在「实际会摆出来的那几个」里选，否则牌少的时候特价会落在空位上。
            int shown = Mathf.Min(UiLayout.ShopSlotCount, pool.Count);
            int discountIndex = shown > 0 ? rng.Next(shown) : -1;

            for (int i = 0; i < UiLayout.ShopSlotCount; i++)
            {
                var entry = new ShopView.ShelfEntry { Index = i };

                if (i < pool.Count)
                {
                    entry.Card = pool[i];
                    entry.Discount = i == discountIndex;
                    entry.Price = entry.Discount ? _discountPrice : _fullPrice;
                    entry.Affordable = _gold >= entry.Price;
                }

                _shelf.Add(entry);
            }
        }

        /// <summary>
        /// 「玩家尚未持有」的卡池 = <b>当前持有（<see cref="_owned"/>）的补集</b>。
        ///
        /// <para>两个口径要点：</para>
        /// <list type="number">
        /// <item>候选只取<b>内置卡表</b>（<c>CardLibrary.All</c>）—— 已经落盘的强化卡
        /// （<c>"a+"</c>）是玩家自己强化出来的，不该出现在货架上；</item>
        /// <item>「已持有」按<b>基础 ID</b> 比（<see cref="CardUpgrade.BaseIdOf"/>）——
        /// 否则持有 <c>a+</c> 时货架会再一次卖 <c>a</c>，玩家买下去卡池里就同时有
        /// <c>a</c> 与 <c>a+</c> 两张同名牌，而且零报错。</item>
        /// </list>
        /// </summary>
        private List<CardDef> CollectUnshelvedPool()
        {
            var ownedBase = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _owned.Count; i++)
            {
                ownedBase.Add(CardUpgrade.BaseIdOf(_owned[i]));
            }

            var pool = new List<CardDef>();

            IReadOnlyList<CardDef> all = CardLibrary.All;
            for (int i = 0; i < all.Count; i++)
            {
                CardDef def = all[i];
                if (def == null || ownedBase.Contains(CardUpgrade.BaseIdOf(def.Id)))
                {
                    continue;
                }

                pool.Add(def);
            }

            return pool;
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        private void OnSlotClicked(int index)
        {
            if (index < 0 || index >= _shelf.Count)
            {
                return;
            }

            ShopView.ShelfEntry entry = _shelf[index];
            if (entry.Card == null)
            {
                return;                          // 空位本就不该冒泡（双保险）
            }

            if (_gold < entry.Price)
            {
                Debug.Log("[ShopSceneEntry] 金币不足：需要 " + entry.Price + "，只有 " + _gold);
                return;
            }

            // 调试语义：直接扣钱 + 重算「买得起 / 买不起」，好把三档价格色都看一遍。
            // ⚠ 正式接入时这一段整块换成 `AdventureCommand.BuyShopItem`。
            _gold -= entry.Price;
            Debug.Log("[ShopSceneEntry] 买下《" + entry.Card.Name + "》花费 " + entry.Price
                      + " 金 → 余 " + _gold);

            // 买下的牌**进背包**：这才是「背包 = 主角当前卡池」在玩法上的意义 ——
            // 不把 id 记进 _owned 的话，背包永远停在开局那几张，买完点开看不见新牌。
            if (!_owned.Contains(entry.Card.Id))
            {
                _owned.Add(entry.Card.Id);
            }

            PersistOwned();

            _shelf[index] = new ShopView.ShelfEntry { Index = entry.Index };

            // 买完要重算**其余**货位的 Affordable —— 钱少了之后它们可能就买不起了。
            // 漏掉这一步的症状是「钱花了，但别的格子还亮着」。
            for (int i = 0; i < _shelf.Count; i++)
            {
                ShopView.ShelfEntry e = _shelf[i];
                if (e.Card == null)
                {
                    continue;
                }

                _shelf[i] = new ShopView.ShelfEntry
                {
                    Index = e.Index,
                    Card = e.Card,
                    Price = e.Price,
                    Discount = e.Discount,
                    Affordable = _gold >= e.Price,
                };
            }

            _view.BindResources(_playerName, _hp, _maxHp, _gold);
            _view.BindBag(ResolveOwnedCards());
            _view.BindShelf(_shelf);

            // 2026-10-02：把这笔花销记进冒险那本账 —— 地图顶栏读的就是它，
            // 所以「买完离开 → 地图上的金币少了 20」是通的。
            if (MapRun.HasActiveRun)
            {
                MapRun.SetGold(_gold);
            }
        }

        /// <summary>
        /// 把「当前持有」写回主存档（2026-10-01 起：买下的牌<b>真的落盘</b>）。
        ///
        /// <para>⚠ 只在 <see cref="_loadedFromMainSave"/> 为真时写 —— 调试覆盖与读档失败
        /// 两种情形都不该拿手上的数据去覆盖真实存档。</para>
        ///
        /// <para>归一成基础 ID 这一步在 <see cref="SaveStore.TrySaveCardIds"/> 里做，
        /// 这里只管把手上这份清单交出去。</para>
        /// </summary>
        private void PersistOwned()
        {
            if (!_loadedFromMainSave)
            {
                return;
            }

            string error;
            if (!SaveStore.TrySaveCardIds(SaveSlot.Main, _owned, out error))
            {
                Debug.LogWarning("[ShopSceneEntry] 买下的牌没能写回主存档：" + error);
                return;
            }

            Debug.Log("[ShopSceneEntry] 主存档卡池已更新：" + _owned.Count + " 张");
        }

        private void OnBagClicked()
        {
            Debug.Log("[ShopSceneEntry] 打开背包：主角当前卡池 " + _owned.Count + " 张（面板由 ShopBagView 自己开关）。");
        }

        private void OnLeaveClicked()
        {
            _view.CloseBag();          // 别把面板留在屏幕上

            // 2026-10-02：从地图走进来的话，「离开」= 完成这个节点并回地图
            //（地图那边 MapSceneEntry.Awake 会结算这一步）。
            if (MapRoutes.LeaveToMap())
            {
                if (_logLeaveClick)
                {
                    Debug.Log("[ShopSceneEntry] 离开商店，回地图（余 " + _gold + " 金）。");
                }

                return;
            }

            if (_logLeaveClick)
            {
                Debug.Log("[ShopSceneEntry] 点了「离开」—— 单场景调试下到此为止；"
                          + "从地图走进来时这一下会回地图层。");
            }
        }
    }
}
