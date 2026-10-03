using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊的<b>单场景入口</b>（2026-10-01 · P6 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>它把这一整套串起来</b>：点水晶球 → 判卡池够不够 → 弹两个空位的浮层 →
    /// 点空位弹卡池浏览 → 选牌填坑（献祭牌有 &gt;1 条效果时再多一步「选哪一条」）→
    /// 两个都填上且不是同一张才让确认 → 确认之后
    /// <b>第一张从卡池里消失，它的一条效果转移到第二张牌上</b>（用户 2026-10-02 口径）。</para>
    ///
    /// <para><b>为什么需要它</b>：女巫工坊是冒险模式里的一个事件节点，正式流程中它会从地图层
    /// 接到一份 <c>RunSnapshot</c>（这一趟冒险已持有的牌）+ 一个节点种子。那套链路还没接，
    /// 而<b>每个事件类型都必须能被单独进 Play 调试</b>（架构文档 §3），
    /// 否则想知道「卡池只有 8 张时到底拦不拦得住」就得先把整条冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：卡池从<see cref="SaveSlot.Main"/>读（或由序列化字段覆盖）；</item>
    /// <item><b>E2 不依赖前一节点</b>：直接以「主存档里那份卡池」开局；</item>
    /// <item><b>E3 读存档、也写存档</b>（2026-10-01 统一卡池时订正，原先是「完全不碰存档」）：
    /// 玩家卡池从 <see cref="SaveSlot.Main"/> 读；确认时<b>一次写完两件事</b> ——
    /// 「第一张牌被消耗」+「第二张牌多一条效果」（<see cref="SaveStore.TryConsumeAndUpgrade"/>，
    /// 语义见 <see cref="UpgradeAxis.Transfer"/>）。
    /// ⚠ 只有卡池确实来自主存档时才写（<c>_cardIds</c> / <c>_pool</c> 调试覆盖时不写）；</item>
    /// <item><b>E4 Core 一行不改</b>：这里只<b>读</b>卡表与 <see cref="WitchWorkshop"/> 的判定
    /// （那是唯一的规则实现），不存在「第二套特殊强化逻辑」。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：<see cref="Rebuild"/> 里那段「解析持有牌」现在是
    /// <b>读主存档</b>（2026-10-01 起，不再是卡池资产，与 <c>UpgradeSceneEntry</c> /
    /// <c>ShopSceneEntry</c> 同一待遇）—— 正式接入 run 时换成从地图层递进来的
    /// <c>RunSnapshot</c> 读，其余流程（选两个空位 → 确认 → 消耗 → 写回 → 结束节点）
    /// 可以原样留用。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：构建器把它挂在 <b>Canvas 的父级（场景根）</b>，
    /// 而存 Prefab 时只保存 Canvas 那一棵子树 —— 它天然不会进 Prefab。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchSceneEntry : MonoBehaviour
    {
        // ── 调试参数（正式接入后由地图层 / 存档给）────────────────────

        [Header("调试 · 卡池")]
        [Tooltip("调试覆盖：直接指向一份卡池资产（不读存档）。\n"
                 + "留空 = 读**主存档**（存档 2）里那个玩家的卡池 —— 与战斗 / 商店 / 强化同一份"
                 + "（2026-10-01 起；原先默认的 Resources/Pools/UpgradePool 已不再被读取）。\n"
                 + "主存档首次创建是随机 10 张，所以默认进来是**能开工**的。\n"
                 + "⚠ 卡池 ≤ 8 张时本场景会提示「无法进行特殊强化」—— 那是**正确行为**，"
                 + "想验那个分支就把卡池消耗到 8 张以下，或在这里指向一份 8 张的资产。")]
        [SerializeField] private CardPoolConfig _pool;

        [Tooltip("直接写卡 ID 覆盖上面的卡池资产（留空 = 用资产）。\n"
                 + "调试用：想验「卡池只有 8 张」就留空、想验正常流程就填 9 个以上。")]
        [SerializeField] private string[] _cardIds = new string[0];

        [Header("调试 · 金币")]
        [Tooltip("玩家现有金币。\n"
                 + "2026-10-03 起特殊强化**要花金币**，价钱 = 40 × 被消耗牌的冷却 ÷ 它的力量 "
                 + "÷ 它的效果数 × max(两张牌的冷却差, 1)（见 WitchWorkshop.CostBase 那一节）。\n"
                 + "它是**单独打开本场景调试时**的默认值（50，与商店 / 地图开局同值）；"
                 + "从地图走进来时会改用 MapRun.Gold —— 与地图顶栏、上一家商店是同一本账。")]
        [SerializeField] private int _gold = MapRun.DefaultGold;

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「离开」的点击打到 Console —— 方便在单场景里确认事件通了"
                 + "（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logLeaveClick = true;

        [Tooltip("勾上 = 确认完就结束这个节点，水晶球不再可点（用户 2026-10-01 口径）。\n"
                 + "取消勾选 = 留在场景里可以接着做第二次（只用于调试）。")]
        [SerializeField] private bool _endNodeAfterConfirm = true;

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private WitchView _view;

        /// <summary>当前卡池里的牌（**可变** —— 确认之后第一张会从这里消失）。</summary>
        private readonly List<CardDef> _cards = new List<CardDef>();

        /// <summary>卡池里的卡 ID（与 <see cref="_cards"/> 一一对应）。</summary>
        private readonly List<string> _ids = new List<string>();

        /// <summary>本次运行用的卡目录（内置 45 张 + 按强化册合成出来的强化版）。</summary>
        private ICardCatalog _catalog;

        /// <summary>玩家当前的强化册（主存档里的那份；调试覆盖时为空）。</summary>
        private UpgradeBook _upgrades = UpgradeBook.Empty;

        /// <summary>左空位（会被消耗掉的那张）。</summary>
        private CardDef _sacrifice;

        /// <summary>
        /// 左空位那张牌里<b>要转移第几条效果</b>（对应 <c>CardDef.Effects</c> 的下标）。
        ///
        /// <para>只选了一张牌还没选效果时是 <c>-1</c> —— 单效果牌会被
        /// <see cref="WitchWorkshop.AutoEffectIndex"/> 直接定成 0，
        /// 多效果牌则必须由玩家在浏览层里选（2026-10-02 用户口径第 2 条）。</para>
        /// </summary>
        private int _sacrificeEffect = -1;

        /// <summary>右空位（强化目标）。</summary>
        private CardDef _target;

        /// <summary>
        /// 上一笔算出来的价钱（2026-10-03）。
        ///
        /// <para>只为说清「<b>为什么价钱和刚才不一样</b>」：换牌之后拿它和这一次比，
        /// 指出是哪个因素动了（<see cref="WitchWorkshop.CostChangeLine"/>）。
        /// <c>0</c> = 没有可比的上一次（刚打开浮层 / 刚确认过一次）。</para>
        /// </summary>
        private int _lastCost;

        /// <summary>上一笔价钱对应的两张牌（同上 —— 不带它们就说不清是「谁」变了）。</summary>
        private CardDef _lastSacrifice;

        private CardDef _lastTarget;

        /// <summary>正在为哪个空位挑牌（−1 = 没在挑）。</summary>
        private int _pendingSlot = -1;

        /// <summary>
        /// 本次 <see cref="_ids"/> 是不是从<b>主存档</b>读来的。
        ///
        /// <para>只有为 <c>true</c> 才允许把「消耗掉第一张」写回存档：
        /// 调试覆盖（<c>_cardIds</c> / <c>_pool</c>）与「读档失败」两种情形都不该
        /// 拿手上的数据去覆盖真实存档。</para>
        /// </summary>
        private bool _loadedFromMainSave;

        /// <summary>节点已结束（确认完 / 点过离开）—— 水晶球不再响应。</summary>
        private bool _ended;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<WitchView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[WitchSceneEntry] 没有接 WitchView —— 先跑 `魔法乱斗/P6 · 构建 WitchWorkshop 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）：构建器接的那一份不是序列化数据。
            _view.OrbClicked += OnOrbClicked;
            _view.LeaveClicked += OnLeaveClicked;

            if (_view.Layer != null)
            {
                _view.Layer.SlotClicked += OnSlotClicked;
                _view.Layer.ConfirmClicked += OnConfirmClicked;
                _view.Layer.Closed += OnLayerClosed;
            }

            if (_view.Picker != null)
            {
                _view.Picker.Picked += OnPicked;
                _view.Picker.Closed += OnPickerClosed;
            }

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.OrbClicked -= OnOrbClicked;
            _view.LeaveClicked -= OnLeaveClicked;

            if (_view.Layer != null)
            {
                _view.Layer.SlotClicked -= OnSlotClicked;
                _view.Layer.ConfirmClicked -= OnConfirmClicked;
                _view.Layer.Closed -= OnLayerClosed;
            }

            if (_view.Picker != null)
            {
                _view.Picker.Picked -= OnPicked;
                _view.Picker.Closed -= OnPickerClosed;
            }
        }

        /// <summary>按当前调试参数重载一次卡池（Inspector 的右键菜单也可以调）。</summary>
        [ContextMenu("重载卡池")]
        public void Rebuild()
        {
            if (_view == null)
            {
                return;
            }

            _ended = false;
            _sacrifice = null;
            _target = null;
            _pendingSlot = -1;
            _sacrificeEffect = -1;
            _lastCost = 0;
            _lastSacrifice = null;
            _lastTarget = null;

            // 2026-10-03：这一趟是从地图走进来的话，金币用**冒险那本账**的余额
            //（地图顶栏显示的就是它，上一家商店花剩下的），而不是本场景的调试默认值。
            // ⚠ 单向：只在 HasActiveRun 时覆盖 —— 单独打开 WitchWorkshop.unity 调试时
            //   MapRun 是空的，本场景照旧按 Inspector 上的 _gold 跑（独立场景契约 E1/E2）。
            if (MapRun.HasActiveRun)
            {
                _gold = MapRun.Gold;
            }

            _view.SetTitle("女巫的工坊");
            _view.SetLeaveLabel("离开");
            _view.SetGold(_gold);
            _view.SetHint("点击水晶球，进行一次特殊强化（献祭一张牌，把它的一条效果转移给另一张）· 要花金币");
            _view.SetHintVisible(true);
            _view.SetOrbInteractable(true);
            _view.CloseAll();

            // ⚠ 目录要叠上主存档的强化册（2026-10-02），口径同商店 / 战斗：
            //   强化版是读的时候合成出来的，目录里没有它，卡池与浏览层都查不到。
            _catalog = SaveStore.WithSavedUpgrades(SaveSlot.Main, ResolveCatalog());
            ResolveIds();
            ResolveCards();

            Debug.Log("[WitchSceneEntry] 卡池 " + _cards.Count + " 张 · 可作目标 " + CountTargets()
                      + " 张 · 门槛 " + (WitchWorkshop.CanOpen(_cards.Count) ? "已达（≥"
                          + WitchWorkshop.MinPoolSize + "）" : "未达（需 ≥"
                          + WitchWorkshop.MinPoolSize + "）")
                      + (_pool != null ? "（来源 " + _pool.displayName + "）" : ""));
        }

        // ══════════════════════════════════════════════════════
        //  卡池
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 取本次运行用的卡目录。
        ///
        /// <para>⚠ 必须和战斗 / 强化用的是**同一份** <c>CardCatalog</c> ——
        /// 强化卡（<c>"a+"</c>）只存在于那份目录的「动态卡」清单里，
        /// 用 <c>CardCatalog.Builtin()</c> 去查会查不到。</para>
        /// </summary>
        private ICardCatalog ResolveCatalog()
        {
            CardCatalogAsset asset = Resources.Load<CardCatalogAsset>("CardCatalog");
            return asset == null ? CardCatalog.Builtin() : asset.CreateCatalog();
        }

        /// <summary>
        /// 解析「玩家现在持有哪些牌」的 ID：Inspector 覆盖 &gt; 调试卡池资产 &gt; <b>主存档</b>。
        ///
        /// <para><b>⚠ 2026-10-01 统一</b>：原先第三档是 <c>Resources/Pools/UpgradePool</c>
        /// （8 张）—— 与商店看的、战斗看的都不是同一份。现在默认读 <see cref="SaveSlot.Main"/>；
        /// 商店买下的牌会出现在这里（本场景的卡池门槛因此从「永远不够」变成默认可开工）。</para>
        ///
        /// <para><b>⚠ 必须过 <see cref="CardUpgrade.PreferUpgraded"/></b>：那里带着
        /// 「基础版 / 强化版只留一份」的规则。</para>
        /// </summary>
        private void ResolveIds()
        {
            _ids.Clear();
            _loadedFromMainSave = false;
            _upgrades = UpgradeBook.Empty;

            if (_cardIds != null && _cardIds.Length > 0)
            {
                for (int i = 0; i < _cardIds.Length; i++)
                {
                    AddId(_cardIds[i]);
                }

                return;
            }

            // 调试覆盖：直接指向一份卡池资产
            if (_pool != null)
            {
                IReadOnlyList<string> assetIds = _pool.ResolveIds(_catalog);
                for (int i = 0; i < assetIds.Count; i++)
                {
                    AddId(assetIds[i]);
                }

                return;
            }

            // 正式来源：主存档的玩家卡池
            PlayerData player;
            string error;
            if (!SaveStore.TryLoadPlayer(SaveSlot.Main, out player, out error))
            {
                Debug.LogWarning("[WitchSceneEntry] 读主存档失败，本局卡池为空：" + error);
                return;
            }

            if (player == null || player.cardIds == null)
            {
                return;
            }

            IReadOnlyList<string> ids = CardUpgrade.PreferUpgraded(player.cardIds, _catalog);
            for (int i = 0; i < ids.Count; i++)
            {
                AddId(ids[i]);
            }

            // 记下这份册子：确认之后要往它上面追加一笔（转移轴），并据此重建目录。
            // 调试覆盖时不写存档，所以那份册子恒为空 —— 见 PersistConsumeAndTransfer。
            _upgrades = UpgradeBook.FromRecords(player.upgrades);
            _loadedFromMainSave = true;
        }

        private void AddId(string id)
        {
            if (string.IsNullOrEmpty(id) || _ids.Contains(id))
            {
                return;
            }

            _ids.Add(id);
        }

        /// <summary>
        /// 把 ID 解析成卡定义。
        ///
        /// <para><b>⚠ 顺序按 <see cref="_ids"/>（卡池自己声明的顺序），不是按卡目录顺序</b> ——
        /// 与 <c>UpgradeSceneEntry</c> 同一条理由：强化卡的序号是「动态卡区」，
        /// 按目录序排会跳到网格最后一行。</para>
        /// </summary>
        private void ResolveCards()
        {
            _cards.Clear();

            var byId = new Dictionary<string, CardDef>(System.StringComparer.Ordinal);
            foreach (CardDef card in _catalog.All)
            {
                if (card != null && !byId.ContainsKey(card.Id))
                {
                    byId.Add(card.Id, card);
                }
            }

            for (int i = 0; i < _ids.Count; i++)
            {
                CardDef card;
                if (byId.TryGetValue(_ids[i], out card))
                {
                    _cards.Add(card);
                    continue;
                }

                // 少了这一条，症状是「卡池资产里写了 8 张，界面上只有 6 张」，而且不报任何错。
                Debug.LogWarning("[WitchSceneEntry] 卡池里的 ID 不在卡目录里，已跳过：" + _ids[i]);
            }
        }

        private int CountTargets()
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                string reason;
                if (WitchWorkshop.CanBeTarget(_cards[i], out reason))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// 把卡池翻译成浏览层要的格子数据（**唯一的规则判定点**）。
        ///
        /// <para>两个空位的候选集**都是整份卡池** —— 左空位除了「效果连锁」的牌
        /// （模仿 / 沉重打击那一类）之外全部可点，右空位把不合格的压暗并写上原因
        /// （而不是从列表里去掉：玩家要能看见「我有这张牌，它为什么不能当目标」，
        /// 直接消失会被当成 bug）。</para>
        ///
        /// <para>⚠ <b>故意不过滤掉「另一个空位已经选中的那张」</b>：用户 2026-10-01 口径是
        /// 「这两个空位当中的牌不可以相同，<b>否则无法确认</b>」—— 也就是说允许选中、
        /// 只是不让确认。所以这里照旧全部列出，由 <see cref="RefreshLayer"/> 把确认键按灰。</para>
        /// </summary>
        private List<UpgradePickerView.Entry> BuildEntries(int slot)
        {
            var list = new List<UpgradePickerView.Entry>(_cards.Count);
            for (int i = 0; i < _cards.Count; i++)
            {
                CardDef card = _cards[i];
                var entry = new UpgradePickerView.Entry();
                entry.Card = card;

                if (slot == 0)
                {
                    string why;
                    entry.Upgradable = WitchWorkshop.CanBeSacrifice(card, out why);
                    entry.Reason = why;
                }
                else
                {
                    string reason;
                    entry.Upgradable = WitchWorkshop.CanBeTarget(card, out reason);
                    entry.Reason = reason;
                }

                list.Add(entry);
            }

            return list;
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 点了水晶球：<b>先判卡池够不够</b>，不够就只给一句提示、连浮层都不弹
        /// （用户 2026-10-01 口径）。
        /// </summary>
        private void OnOrbClicked()
        {
            if (_ended || _view.IsLayerOpen)
            {
                return;
            }

            if (!WitchWorkshop.CanOpen(_cards.Count))
            {
                _view.ShowHintAsWarning(WitchWorkshop.OpenBlockedReason(_cards.Count), true);
                Debug.Log("[WitchSceneEntry] " + WitchWorkshop.OpenBlockedReason(_cards.Count));
                return;
            }

            _sacrifice = null;
            _target = null;
            _sacrificeEffect = -1;
            _pendingSlot = -1;

            _view.OpenLayer("特殊强化", "点空位，从卡池里选一张牌");
            RefreshLayer();
        }

        /// <summary>点了某个空位：弹出卡池浏览，让玩家为它挑一张。</summary>
        private void OnSlotClicked(int slot)
        {
            if (_ended || _view.Picker == null)
            {
                return;
            }

            _pendingSlot = slot;

            // ⚠ 只有献祭位才让浏览层多问一步「选哪一条效果」（2026-10-02）：
            //   目标牌必须恰好 1 条效果，没有可选的余地。
            //
            // ⚠ 浏览层那两行提示**把价钱的口诀拼进去**（2026-10-03）：玩家是在**这里**
            //   挑牌的，离开这一步就不可能知道「为什么等一下的价钱是这个数」。
            //   两行都压在 900 宽 / 字号 26 里（约 31 个字），再长就会溢出面板。
            _view.Picker.Show(
                BuildEntries(slot),
                slot == 0 ? "选择献祭的牌" : "选择要强化的牌",
                slot == 0
                    ? "被消耗的牌：冷却越高 / 力量越低 / 效果越少 → 花费越贵"
                    : "只有「1 条效果且未强化」的牌能当目标；它冷却越低 → 花费越贵",
                slot == 0);
        }

        /// <summary>
        /// 浏览层里选中了牌（<paramref name="effectIndex"/> = 用它的第几条效果；
        /// 给右空位选目标时恒为 −1），填进刚才那个空位。
        /// </summary>
        private void OnPicked(CardDef card, int effectIndex)
        {
            int slot = _pendingSlot;
            _pendingSlot = -1;

            if (slot == 0)
            {
                _sacrifice = card;

                // 单效果牌浏览层直接回报 0；双效果牌回报玩家点的那一条。
                // 兜底：万一回报 −1（理论上不会），按 Core 的口径能自动定就自动定。
                int auto = WitchWorkshop.AutoEffectIndex(card);
                _sacrificeEffect = effectIndex >= 0 ? effectIndex : auto;
            }
            else if (slot == 1)
            {
                _target = card;
            }
            else
            {
                return;
            }

            RefreshLayer();
        }

        /// <summary>浏览层关掉了（选完 / 取消都会发）。</summary>
        private void OnPickerClosed()
        {
            _pendingSlot = -1;
        }

        /// <summary>
        /// 玩家按了「确认」：<b>第一张牌从卡池里消失，它的一条效果转移到第二张牌上</b>，
        /// 然后结束这个节点。
        ///
        /// <para><b>2026-10-02 起效果落地</b>：目标牌拿到一笔
        /// <see cref="UpgradeAxis.Transfer"/> 强化（一条 <see cref="EffectSpec"/> 被并进它的
        /// 效果列表）—— 力量 / 冷却 / 元素 / 插画都不动，卡面 ID / 卡名只挂一个 <c>+</c>。
        /// 这是「强化 = 一条配方，读的时候合成」那套模型（见 <see cref="UpgradeBook"/>），
        /// 所以它<b>随存档走</b>，也会出现在战斗 / 商店 / 强化四个场景里。</para>
        ///
        /// <para><b>⚠ 顺序不能换</b>：先落盘（<see cref="SaveStore.TryConsumeAndUpgrade"/>
        /// 一次写完「消耗 + 转移」两件事），再改内存里的卡池 / 目录，最后才刷新界面 ——
        /// 分成两次写会留下「牌已经吃掉、效果却没拿到」的存档（见那个方法的注释）。</para>
        /// </summary>
        private void OnConfirmClicked()
        {
            if (_ended)
            {
                return;
            }

            string reason;
            if (!WitchWorkshop.CanConfirm(_sacrifice, _sacrificeEffect, _target, _gold, out reason))
            {
                // 双保险：确认键本来就按灰了，这里再挡一次（2026-10-03 起也挡「金币不足」）。
                _view.Layer.SetStatus(reason, true);
                return;
            }

            CardDef sacrifice = _sacrifice;
            CardDef target = _target;
            int effectIndex = _sacrificeEffect;
            int cost = WitchWorkshop.Cost(sacrifice, target);
            CardUpgradeMod mod = WitchWorkshop.BuildTransferMod(sacrifice, effectIndex);

            if (mod == null || mod.effect == null)
            {
                // 理论不可达（CanConfirm 已经验过「要转移的效果还在」）。真发生了就什么也别改。
                _view.Layer.SetStatus("要转移的效果不见了，请重新选一次", true);
                return;
            }

            string persist = PersistConsumeAndTransfer(sacrifice, target, mod);

            // 2026-10-03：扣钱。
            // ⚠ 只写**冒险那本账**（MapRun）—— 与商店同口径：「金币」目前还不是存档字段
            //   （真存档在 P6 的 Core/Adventure，尚未接入）。所以退出 Play 会回到 50。
            // ⚠ 顺序：先落盘（消耗 + 转移一次写完），再扣钱、再刷界面 —— 见 PersistConsumeAndTransfer。
            _gold -= cost;
            if (_gold < 0)
            {
                _gold = 0;
            }

            if (MapRun.HasActiveRun)
            {
                MapRun.SetGold(_gold);
            }

            _view.SetGold(_gold);

            // 被献祭的那张从本局卡池里移除。
            int cardAt = _cards.IndexOf(sacrifice);
            if (cardAt >= 0)
            {
                _cards.RemoveAt(cardAt);
            }

            int idAt = _ids.IndexOf(sacrifice.Id);
            if (idAt >= 0)
            {
                _ids.RemoveAt(idAt);
            }

            // 目录叠上这一笔新强化，再把目标那一格换成合成出来的强化版 ——
            // 界面上的卡面（效果栏）当场就多出那一条，玩家看得见自己换到了什么。
            _upgrades = _upgrades.Append(target.Id, mod);
            _catalog = SaveStore.WithUpgrades(ResolveCatalog(), _upgrades);

            string targetBase = CardUpgrade.BaseIdOf(target.Id);
            CardDef upgraded = null;
            for (int i = 0; i < _catalog.All.Count; i++)
            {
                if (_catalog.All[i] != null
                    && string.Equals(_catalog.All[i].Id, CardUpgrade.UpgradedIdOf(targetBase),
                        System.StringComparison.Ordinal))
                {
                    upgraded = _catalog.All[i];
                    break;
                }
            }

            if (upgraded != null)
            {
                int targetAt = _cards.IndexOf(target);
                if (targetAt >= 0)
                {
                    _cards[targetAt] = upgraded;
                }

                int targetIdAt = _ids.IndexOf(target.Id);
                if (targetIdAt >= 0)
                {
                    _ids[targetIdAt] = upgraded.Id;
                }
            }

            _sacrifice = null;
            _target = null;
            _sacrificeEffect = -1;

            _view.CloseAll();

            string effectText = mod.effect.Describe();
            string message = "《" + sacrifice.Name + "》已被消耗，《" + target.Name + "》获得效果「"
                             + effectText + "」（" + (upgraded == null ? target.Id : upgraded.Id)
                             + "）· 花费 " + cost + " 金，余 " + _gold + " 金" + persist;
            Debug.Log("[WitchSceneEntry] " + message);

            if (_endNodeAfterConfirm)
            {
                EndNode(message);
                return;
            }

            _view.ShowHintAsWarning(message, false);
            RefreshLayer();
        }

        /// <summary>
        /// 把「消耗 + 转移」写回主存档。
        ///
        /// <para>⚠ 只在 <see cref="_loadedFromMainSave"/> 为真时写 —— 调试覆盖与读档失败
        /// 两种情形都不该拿手上的数据去覆盖真实存档（与商店 / 强化同口径）。</para>
        ///
        /// <para>返回一句给日志用的附言（成功 / 调试 / 失败各一句）。</para>
        /// </summary>
        private string PersistConsumeAndTransfer(CardDef sacrifice, CardDef target, CardUpgradeMod mod)
        {
            if (!_loadedFromMainSave)
            {
                return "· 调试覆盖：只在本局内存里生效，没有写存档";
            }

            string error;
            if (!SaveStore.TryConsumeAndUpgrade(SaveSlot.Main, sacrifice.Id, target.Id, mod, out error))
            {
                Debug.LogWarning("[WitchSceneEntry] 消耗与转移没能写回主存档：" + error);
                return "· ⚠ 写回失败：" + error;
            }

            return "· 已写回 " + SaveStore.FilePathFor(SaveSlot.Main);
        }

        private void OnLayerClosed()
        {
            // 已经结束 / 正在收尾时不要把引导文字按回默认那句 ——
            // 否则会出现「节点都结束了、屏幕上还写着『点击水晶球』」。
            if (_ended)
            {
                return;
            }

            _view.ShowHintAsWarning("点击水晶球，进行一次特殊强化（献祭一张牌，把它的一条效果转移给另一张）· 要花金币", false);
        }

        /// <summary>
        /// 结束这个节点。
        ///
        /// <para>用户 2026-10-01 口径：<b>一次特殊强化 = 一个事件节点</b>。
        /// 正式流程里这一下会提交「完成节点」并回地图层；本场景是独立调试场景，
        /// 所以只打日志 + 收摊（水晶球不可再点）。</para>
        /// </summary>
        private void EndNode(string message)
        {
            _ended = true;
            _view.SetOrbInteractable(false);
            _view.ShowHintAsWarning(message, false);

            if (_logLeaveClick)
            {
                Debug.Log("[WitchSceneEntry] 特殊强化完成，本节点结束（正式流程里这一下会回地图层）。");
            }
        }

        private void OnLeaveClicked()
        {
            _view.CloseAll();
            _view.SetHintVisible(true);

            // 2026-10-02：从地图走进来的话，「离开」= 完成这个节点并回地图
            //（地图那边 MapSceneEntry.Awake 会结算这一步）。
            if (MapRoutes.LeaveToMap())
            {
                if (_logLeaveClick)
                {
                    Debug.Log("[WitchSceneEntry] 离开女巫的工坊，回地图。");
                }

                return;
            }

            if (_logLeaveClick)
            {
                Debug.Log("[WitchSceneEntry] 点了「离开」—— 单场景调试下到此为止；"
                          + "从地图走进来时这一下会回地图层。");
            }
        }

        /// <summary>
        /// 按当前两个空位刷新确认键、状态行、价钱两行（**唯一的界面判定点**）。
        ///
        /// <para><b>顺序</b>：先摆卡面 → 再判「能不能确认」（<b>带金币</b>）→ 再写价钱，
        /// 因为价钱那一行要跟着确认键的状态一起被人看见（键灰着 + 价钱标红 = 一眼就知道差在钱上）。</para>
        /// </summary>
        private void RefreshLayer()
        {
            WitchLayerView layer = _view.Layer;
            if (layer == null)
            {
                return;
            }

            layer.SetSlots(_sacrifice, _target);

            string reason;
            bool ok = WitchWorkshop.CanConfirm(_sacrifice, _sacrificeEffect, _target, _gold,
                out reason);
            layer.SetConfirmEnabled(ok);

            // ⚠ 右上角那行金币**每次刷新都重写一遍**：价钱那两行读的是 `_gold`，
            //   两处必须同源同刻 —— 否则会出现「右侧写着现有 10 金、右上角还写着金币 50」
            //   这种一眼假的画面（今天没有第二个改钱的入口，但这个是零成本的保险）。
            _view.SetGold(_gold);

            RefreshCost(layer);

            if (ok)
            {
                EffectDef effect = WitchWorkshop.EffectAt(_sacrifice, _sacrificeEffect);
                string effectText = string.IsNullOrEmpty(effect.Text) ? effect.HandlerId : effect.Text;
                layer.SetStatus("确认后，《" + _sacrifice.Name + "》会被消耗，「" + effectText
                                + "」转移到《" + _target.Name + "》（花费 "
                                + WitchWorkshop.Cost(_sacrifice, _target) + " 金）", false);
                return;
            }

            if (!WitchWorkshop.BothSlotsFilled(_sacrifice, _target))
            {
                bool any = _sacrifice != null || _target != null;
                layer.SetStatus(any ? "还需要为另一个空位选一张牌" : "点空位，从卡池里选一张牌", false);
                return;
            }

            // 两个都填了却不合格。除了「撞了同一张牌」与「多效果还没选一条」，
            // 2026-10-03 起还多一种：**金币不够**（reason 由 Core 给出，里面带着两个数）。
            layer.SetStatus(reason, true);
        }

        /// <summary>
        /// 价钱那一行 + 明细那一行（2026-10-03）。
        ///
        /// <para><b>三档</b>：</para>
        /// <list type="number">
        /// <item><b>两张都选了</b>：价格牌 + 数字（不够就标红）+「现有 N 金」，
        ///   明细优先写「为什么和刚才不一样」，没有变化时退回那条等式；</item>
        /// <item><b>只选了献祭牌</b>：价钱算不出来（公式要用到目标的冷却），
        ///   所以藏起价格牌，改用<b>底价 + 口诀</b>把量级交代掉；</item>
        /// <item><b>一张都没选</b>：两行都空着。</item>
        /// </list>
        ///
        /// <para>⚠ 说过一次就记住 <see cref="_lastCost"/> / <see cref="_lastSacrifice"/> /
        /// <see cref="_lastTarget"/>，下一句才是「比刚才」——少了这个，
        /// 玩家换牌之后只会看到一个和刚才完全无关的数字，这正是用户要解决的那个问题。</para>
        /// </summary>
        private void RefreshCost(WitchLayerView layer)
        {
            int cost = WitchWorkshop.Cost(_sacrifice, _target);
            bool both = WitchWorkshop.BothSlotsFilled(_sacrifice, _target) && cost > 0;

            if (!both)
            {
                layer.SetCostVisible(false);
                layer.SetCostDetail(_sacrifice != null
                    ? WitchWorkshop.CostFloorLine(_sacrifice) + "\n" + WitchWorkshop.CostRuleLine()
                    : string.Empty);
                _lastCost = 0;
                _lastSacrifice = null;
                _lastTarget = null;
                return;
            }

            layer.SetCost(cost, _gold, WitchWorkshop.CanAfford(_gold, cost));

            string change = WitchWorkshop.CostChangeLine(_lastSacrifice, _lastTarget, _lastCost,
                _sacrifice, _target, cost);
            layer.SetCostDetail(string.IsNullOrEmpty(change)
                ? WitchWorkshop.CostLine(_sacrifice, _target)
                : change);

            _lastCost = cost;
            _lastSacrifice = _sacrifice;
            _lastTarget = _target;
        }

    }
}
