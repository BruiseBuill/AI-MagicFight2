using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 强化场景的<b>单场景入口</b>（2026-09-30 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>⚠ 2026-10-06 改口径（用户原话）</b>：「强化时需要选择一张牌作为祭品，
    /// 然后才能力量加2，不然无法强化（类似于女巫）」。于是本场景变成<b>两步</b>：
    /// ① 点台面 → 选「要强化的牌」；② 紧接着 → 选「祭品（会被消耗）」，两者都定了才动画 + 落盘。
    /// <b>规则本体全部在 <see cref="AltarRite"/></b>（Core），本类只负责流程、文案与写存档；
    /// 卡池少于 <see cref="AltarRite.MinPoolSize"/> 张时只提示、连弹窗都不开
    /// （与女巫工坊 <see cref="WitchWorkshop.CanOpen"/> 同一套做法）。</para>
    ///
    /// <para><b>为什么需要它</b>：强化是冒险模式里的一个事件节点，正式流程中它会从地图层
    /// 接到一份 <c>RunSnapshot</c>（这一趟冒险已持有的牌）+ 一个节点种子。那套链路还没接，
    /// 而<b>每个事件类型都必须能被单独进 Play 调试</b>（架构文档 §3），
    /// 否则想知道「这张牌强化完是不是真的变 6」就得先把整条冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：卡池从<see cref="SaveSlot.Main"/>读（或由序列化字段覆盖）；</item>
    /// <item><b>E2 不依赖前一节点</b>：直接以「主存档里那份卡池」开局；</item>
    /// <item><b>E3 读主存档、并把强化写回主存档</b>（2026-10-02 多轴强化时订正，原先是「只读」）：
    /// 玩家卡池从 <see cref="SaveSlot.Main"/> 读；<b>一笔强化 = 强化册里的一张牌 + 一笔</b>
    /// （<c>PlayerData.upgrades</c>，见 <see cref="UpgradeBook"/>），确认时写回同一个文件。
    /// 卡池本身<b>不写</b>（那张牌的 ID 不变，强化版由
    /// <see cref="CardUpgrade.PreferUpgraded"/> 在读取时解析）。
    /// ⚠ 上一版落盘的 <c>Card_*_Up.asset</c> 仍然读得进（向后兼容），但不再新写 ——
    /// 那份是<b>全局</b>的，表达不了「同一个基础卡在不同存档是不同强化」；</item>
    /// <item><b>E4 Core 一行不改</b>：这里只<b>读</b>卡表与 <see cref="CardUpgrade"/> 的判定
    /// （那是唯一的规则实现），不存在「第二套强化逻辑」。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：正式接入时，「这一趟冒险持有哪些牌」应当由 Core 侧
    /// （run 的持有卡）算出再喂进来；本类里那段「从卡池资产解析」到时换成从 run 读，
    /// 其余流程（选牌 → 确认 → 落盘 → 动画 → 结束节点）可以原样留用。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：构建器把它挂在 <b>Canvas 的父级（场景根）</b>，
    /// 而存 Prefab 时只保存 Canvas 那一棵子树 —— 它天然不会进 Prefab。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeSceneEntry : MonoBehaviour
    {
        /// <summary>
        /// 本场景读写哪个档 = <b>玩家在主菜单选的那个</b>（没选过就 <c>Main</c>，单场景调试不受影响）。
        /// 与 <c>ShopSceneEntry.SlotInUse</c> 同一口径，见那里的说明。
        /// </summary>
        private static SaveSlot SlotInUse
        {
            get { return SaveSlots.Active(SaveSlot.Main); }
        }

        // ── 调试参数（正式接入后由地图层 / 存档给）────────────────────

        [Header("调试 · 卡池")]
        [Tooltip("调试覆盖：直接指向一份卡池资产（不读存档）。\n"
                 + "留空 = 读**主存档**（存档 2）里那个玩家的卡池 —— 与战斗 / 商店 / 女巫工坊"
                 + "同一份（2026-10-01 起；原先默认的 Resources/Pools/UpgradePool 已不再被读取）。\n"
                 + "⚠ 主存档不存在时会被**现场创建**（随机 10 张），所以进本场景不需要先有存档。")]
        [SerializeField] private CardPoolConfig _pool;

        [Tooltip("直接写卡 ID 覆盖上面的卡池资产（留空 = 用资产）。\n"
                 + "想验「卡池里一张可强化的都没有」就把这里填成 ag 之外的不可强化牌，或留空并改资产。")]
        [SerializeField] private string[] _cardIds = new string[0];

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「离开」的点击打到 Console —— 方便在单场景里确认事件通了"
                 + "（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logLeaveClick = true;

        [Tooltip("勾上 = 强化完（动画播完）就结束这个节点，台面不再可点（用户 2026-09-30 口径）。\n"
                 + "取消勾选 = 留在场景里可以接着强化第二张（只用于调试）。")]
        [SerializeField] private bool _endNodeAfterUpgrade = true;

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private UpgradeView _view;

        /// <summary>当前卡池里的牌（**可变** —— 强化完把那一张换成新定义）。</summary>
        private readonly List<CardDef> _cards = new List<CardDef>();

        /// <summary>卡池里的卡 ID（与 <see cref="_cards"/> 一一对应，解析与替换都按 ID 走）。</summary>
        private readonly List<string> _ids = new List<string>();

        /// <summary>本次运行用的卡目录（内置 + 自定义 + 动态卡 + <b>按强化册合成出来的强化版</b>）。</summary>
        private ICardCatalog _catalog;

        /// <summary>玩家当前的强化册（主存档里的那份；调试覆盖时恒为空）。</summary>
        private UpgradeBook _upgrades = UpgradeBook.Empty;

        /// <summary>卡池是不是真的来自主存档（决定强化要不要写回存档，见 <see cref="OnUpgradeConfirmed"/>）。</summary>
        private bool _loadedFromMainSave;

        /// <summary>节点已结束（强化完 / 点过离开）—— 台面不再响应。</summary>
        private bool _ended;

        /// <summary>强化动画正在播。</summary>
        private bool _busy;

        /// <summary>第 1 步选的「要强化的牌」（还没选 = <c>null</c>）。</summary>
        private CardDef _target;

        /// <summary>第 2 步选的「祭品」—— 确认后它会从卡池里消失（还没选 = <c>null</c>）。</summary>
        private CardDef _sacrifice;

        /// <summary>
        /// 第 1 步刚确认、正在等弹窗关掉 —— 关掉之后再弹第 2 步。
        ///
        /// <para><b>⚠ 为什么必须等下一拍</b>：弹窗的「确认」是这样走的
        /// <c>Confirmed → Hide() → Closed</c>（见 <see cref="UpgradePickerView"/>）。
        /// 在 <c>Confirmed</c> 里直接开下一轮，会被紧随的 <c>Hide()</c> 立刻按灭 ——
        /// 症状是「第 1 步确认完，弹窗整个关掉、什么都没发生」（零报错）。
        /// 所以这里只记个标记，真正开第 2 步放在 <see cref="OnPickerClosed"/> 里。</para>
        /// </summary>
        private bool _pendingSacrifice;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<UpgradeView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[UpgradeSceneEntry] 没有接 UpgradeView —— 先跑 `魔法乱斗/P5 · 构建 Upgrade 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）：构建器接的那一份不是序列化数据。
            _view.TableClicked += OnTableClicked;
            _view.LeaveClicked += OnLeaveClicked;
            _view.UpgradeConfirmed += OnUpgradeConfirmed;
            _view.PickerClosed += OnPickerClosed;
            _view.FxFinished += OnFxFinished;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.TableClicked -= OnTableClicked;
            _view.LeaveClicked -= OnLeaveClicked;
            _view.UpgradeConfirmed -= OnUpgradeConfirmed;
            _view.PickerClosed -= OnPickerClosed;
            _view.FxFinished -= OnFxFinished;
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
            _busy = false;
            _target = null;
            _sacrifice = null;
            _pendingSacrifice = false;

            _view.SetTitle("强化卡牌");
            _view.SetLeaveLabel("离开");
            _view.SetHint("点击台面：先选要强化的牌，再选一张祭品（祭品会被消耗掉）· 力量 +2，上限 9");
            _view.SetHintVisible(true);
            _view.SetTableInteractable(true);
            _view.ClosePicker();

            _catalog = ResolveCatalog();
            _upgrades = UpgradeBook.Empty;
            _loadedFromMainSave = false;
            ResolveIds();
            ResolveCards();

            Debug.Log("[UpgradeSceneEntry] 卡池 " + _cards.Count + " 张 · 可强化 " + CountUpgradable()
                      + " 张 · 门槛 " + (AltarRite.CanOpen(_cards.Count)
                          ? "已达（≥" + AltarRite.MinPoolSize + "）"
                          : "未达（需 ≥" + AltarRite.MinPoolSize + "）")
                      + " · 强化册 " + _upgrades.Count + " 张"
                      + (_pool != null ? "（来源 " + _pool.displayName + "）" : ""));
        }

        // ══════════════════════════════════════════════════════
        //  卡池
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 取本次运行用的卡目录。
        ///
        /// <para>⚠ 必须和战斗 / 强化落盘用的是**同一份** <c>CardCatalog</c> ——
        /// 强化卡（<c>"a+"</c>）只存在于那份目录的「动态卡」清单里，
        /// 用 <c>CardCatalog.Builtin()</c> 去查会查不到（症状：强化完下一次进场景那张牌不见了）。</para>
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
        /// 那份 8 张的资产 —— 与商店看的、战斗看的都不是同一份。现在默认读
        /// <see cref="SaveSlot.Main"/>，也就是「一个存档 = 一个玩家卡池」的那一份；
        /// 商店买下的牌会出现在这里，不落盘。卡池资产只剩「调试覆盖」这一个用途。</para>
        ///
        /// <para><b>⚠ 两条解析必须走同一处</b>：<see cref="CardUpgrade.PreferUpgraded"/>
        /// 带着「基础版 / 强化版只留一份」的规则 —— 也就是「强化过一次，下次进来那张牌
        /// 还是 6 点」这条**永久性**。在这里另走一遍 <c>cardIds</c> 会把这层规则漏掉
        /// （表现为「强化完回场景，牌又变回 4 点」，零报错）。</para>
        /// </summary>
        private void ResolveIds()
        {
            _ids.Clear();

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
            if (!SaveStore.TryLoadPlayer(SlotInUse, out player, out error))
            {
                Debug.LogWarning("[UpgradeSceneEntry] 读主存档失败，本局卡池为空：" + error);
                return;
            }

            if (player == null || player.cardIds == null)
            {
                return;
            }

            // ⚠ 目录必须**先**叠上强化册，**再**解析卡池（2026-10-02）。
            //   顺序反了的话目录里没有 "a+"，PreferUpgraded 的判据不成立 ——
            //   症状是「强化过的牌下次进来又变回基础版」，零报错。
            _upgrades = UpgradeBook.FromRecords(player.upgrades);
            _catalog = SaveStore.WithUpgrades(_catalog, _upgrades);

            IReadOnlyList<string> ids = CardUpgrade.PreferUpgraded(player.cardIds, _catalog);
            for (int i = 0; i < ids.Count; i++)
            {
                AddId(ids[i]);
            }

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
        /// <para><b>⚠ 顺序按 <see cref="_ids"/>（卡池自己声明的顺序），不是按卡目录顺序</b>：
        /// 强化卡的序号是「动态卡区」（10000 起），按目录序排会**跳到网格最后一行**
        /// —— 玩家强化完一张牌，它会从原来那一格挪到队尾，看起来像「卡被换掉了」。
        /// 按卡池出的顺序排，强化版就留在基础版原来的位置上。
        /// （这与 <c>CardPool.Resolve</c> 的「按目录序」口径不同，是对齐「玩家自己的卡池」
        /// 这个语义的有意选择；同种子复现仍然稳定，因为 <see cref="_ids"/> 是确定的。）</para>
        /// </summary>
        private void ResolveCards()
        {
            _cards.Clear();

            var byId = new Dictionary<string, CardDef>(StringComparer.Ordinal);
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
                Debug.LogWarning("[UpgradeSceneEntry] 卡池里的 ID 不在卡目录里，已跳过：" + _ids[i]);
            }
        }

        private int CountUpgradable()
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                string reason;
                if (AltarRite.CanBeTarget(_cards[i], null, out reason))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// 第 1 步的格子数据：<b>要强化的牌</b>（判据 <see cref="AltarRite.CanBeTarget"/>，
        /// 参数 2 = 祭品还没选）。
        ///
        /// <para>不可选的格子照旧留在列表里、只压暗并写上原因（不从列表里去掉：
        /// 玩家要能看见「我有这张牌、它为什么不能强化」，直接消失会被当成 bug）。</para>
        /// </summary>
        private List<UpgradePickerView.Entry> BuildTargetEntries()
        {
            var list = new List<UpgradePickerView.Entry>(_cards.Count);
            for (int i = 0; i < _cards.Count; i++)
            {
                CardDef card = _cards[i];
                string reason;
                bool ok = AltarRite.CanBeTarget(card, null, out reason);
                list.Add(new UpgradePickerView.Entry { Card = card, Upgradable = ok, Reason = reason });
            }

            return list;
        }

        /// <summary>
        /// 第 2 步的格子数据：<b>祭品</b>（判据 <see cref="AltarRite.CanBeSacrifice"/>）。
        ///
        /// <para>整份卡池里只有「已经选定的目标」那一格不可选（<see cref="AltarRite.SameCard"/>
        /// 按基础 ID 比，所以基础版与它的 <c>+</c> 版也算同一张）——
        /// 其它任何牌都能当祭品，包括已强化过的、力量为 X 的、效果连锁的。</para>
        /// </summary>
        private List<UpgradePickerView.Entry> BuildSacrificeEntries()
        {
            var list = new List<UpgradePickerView.Entry>(_cards.Count);
            for (int i = 0; i < _cards.Count; i++)
            {
                CardDef card = _cards[i];
                string reason;
                bool ok = AltarRite.CanBeSacrifice(card, _target, out reason);
                list.Add(new UpgradePickerView.Entry { Card = card, Upgradable = ok, Reason = reason });
            }

            return list;
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        private void OnTableClicked()
        {
            if (_ended || _busy || _view.IsPickerOpen)
            {
                return;
            }

            // 卡池不够就只提示一句（连弹窗都不弹）—— 与女巫工坊同一套做法。
            if (!AltarRite.CanOpen(_cards.Count))
            {
                string blocked = AltarRite.OpenBlockedReason(_cards.Count);
                _view.SetHint(blocked);
                _view.SetHintVisible(true);
                Debug.Log("[UpgradeSceneEntry] " + blocked);
                return;
            }

            _target = null;
            _sacrifice = null;
            _pendingSacrifice = false;

            _view.OpenPicker(BuildTargetEntries(), "强化卡牌 · 第 1 步：选择要强化的牌");
        }

        /// <summary>
        /// 弹窗按了「确认」。<b>同一个回调要用两次</b>（2026-10-06 两步流程）：
        /// 第 1 次拿到的是「要强化的牌」，第 2 次拿到的是「祭品」。
        ///
        /// <para><b>⚠ 第 2 步不能在 <c>Confirmed</c> 里当场开</b>（理由见
        /// <see cref="_pendingSacrifice"/>）—— 这里只记标记，等 <see cref="OnPickerClosed"/>。</para>
        /// </summary>
        private void OnUpgradeConfirmed(CardDef card)
        {
            if (_ended || _busy || card == null)
            {
                return;
            }

            if (_target == null)
            {
                // 第 1 步：记下要强化的牌。
                _target = card;
                _pendingSacrifice = true;
                Debug.Log("[UpgradeSceneEntry] 第 1 步选定：《" + card.Name + "》"
                          + "（力量 " + card.Power + " → " + CardUpgrade.UpgradedPower(card) + "）"
                          + "，接着选祭品。");
                return;
            }

            // 第 2 步：祭品定了 → 干活。
            _sacrifice = card;
            DoUpgrade();
        }

        /// <summary>
        /// 真正落地这一笔：<b>祭品从卡池消失 + 目标的力量 +2</b>。
        ///
        /// <para><b>⚠ 顺序不能换</b>：先落盘（拿到「运行时该用的那一份定义」），
        /// 再改内存里的卡池 / 目录，最后才播动画 —— 动画拿的就是新旧两份，播的是同一个事实。</para>
        ///
        /// <para><b>⚠ 落盘只走一次写</b>：<see cref="SaveStore.TryConsumeAndUpgrade"/>
        /// 一次写完「消耗掉祭品 + 给目标追加一笔配方」。分两次写会留下
        /// 「牌已经吃掉、力量却没加上」的存档（见那个方法的注释）。</para>
        ///
        /// <para><b>⚠ 仍然是「改配方」而不是「造一张新卡资产」</b>（2026-10-02 多轴强化）：
        /// 强化记成 <c>PlayerData.upgrades</c> 里的一张牌 + 一笔
        /// （<see cref="UpgradeBook.Append"/>），强化版由 <see cref="UpgradeBook.BuildCatalog"/>
        /// 读的时候合成 —— 这样同一张牌能同时叠力量 / 冷却 / 词条三轴，
        /// 也不会出现「A 存档强化过、B 存档也看到」这种全局副作用。</para>
        ///
        /// <para><b>本节点给的方向 = 力量 +2</b>，笔的构造在
        /// <see cref="AltarRite.BuildMod"/>。冷却轴 / 词条轴的能力已经在 Core 里
        /// （<see cref="CardUpgradeMod.Cooldown"/> / <see cref="CardUpgradeMod.Effect"/>），
        /// <b>换方向只需要换 Core 里那一笔 mod</b> —— 多方向强化的接入口只在那里。</para>
        /// </summary>
        private void DoUpgrade()
        {
            CardDef sacrifice = _sacrifice;
            CardDef target = _target;
            if (sacrifice == null || target == null)
            {
                return;
            }

            string baseId = CardUpgrade.BaseIdOf(target.Id);
            CardUpgradeRecord existing;
            if (!_upgrades.TryGet(baseId, out existing))
            {
                existing = null;
            }

            string reason;
            if (!AltarRite.CanConfirm(sacrifice, target, existing, out reason))
            {
                // 双保险：弹窗里不可选的格子本来就点不动，这里再挡一次。
                Debug.LogWarning("[UpgradeSceneEntry] 这一步强化不成立：" + reason);
                _view.ClosePicker();
                _view.SetHint(reason);
                _view.SetHintVisible(true);
                ResetPick();
                return;
            }

            CardUpgradeMod mod = AltarRite.BuildMod();
            CardDef upgraded;
            string note;

            if (_loadedFromMainSave)
            {
                string error;
                if (!SaveStore.TryConsumeAndUpgrade(SlotInUse, sacrifice.Id, baseId, mod, out error))
                {
                    Debug.LogWarning("[UpgradeSceneEntry] 强化没能写回主存档：" + error);
                    _view.ClosePicker();
                    ResetPick();
                    return;
                }

                _upgrades = _upgrades.Append(baseId, mod);
                _catalog = SaveStore.WithUpgrades(ResolveCatalog(), _upgrades);
                upgraded = _catalog.Get(CardUpgrade.UpgradedIdOf(baseId));
                note = "已写回 " + SaveStore.FilePathFor(SlotInUse);
            }
            else
            {
                // 调试覆盖（Inspector 填了 _cardIds / _pool）：只在本局内存里生效，不碰存档
                // —— 拿调试数据去覆盖真实存档是另一种事故（与商店 / 女巫同口径）。
                upgraded = CardUpgrade.Apply(target, mod, 20000 + _cards.Count);
                note = "调试覆盖：强化只在本局内存里生效，没有写存档";
            }

            // 卡池：祭品消失，目标就地换成强化版（顺序无关，但先删后换更好读）。
            int sacrificeAt = _cards.IndexOf(sacrifice);
            if (sacrificeAt >= 0)
            {
                _cards.RemoveAt(sacrificeAt);
            }

            int sacrificeIdAt = _ids.IndexOf(sacrifice.Id);
            if (sacrificeIdAt >= 0)
            {
                _ids.RemoveAt(sacrificeIdAt);
            }

            int at = _cards.IndexOf(target);
            if (at < 0)
            {
                _cards.Add(upgraded);
            }
            else
            {
                _cards[at] = upgraded;
            }

            int idAt = _ids.IndexOf(target.Id);
            if (idAt < 0)
            {
                _ids.Add(upgraded.Id);
            }
            else
            {
                _ids[idAt] = upgraded.Id;
            }

            ResetPick();
            _busy = true;
            _view.ClosePicker();
            _view.PlayUpgradeFx(target, upgraded);

            Debug.Log("[UpgradeSceneEntry] " + AltarRite.ConfirmSummary(sacrifice, target, upgraded)
                      + "（力量 " + target.Power + " → " + upgraded.Power
                      + " · 冷却 " + target.Cooldown + " → " + upgraded.Cooldown
                      + " · 卡池剩 " + _cards.Count + " 张 · " + note + "）");
        }

        /// <summary>把「这两步的选择」清掉（确认完 / 中途放弃都走它）。</summary>
        private void ResetPick()
        {
            _target = null;
            _sacrifice = null;
            _pendingSacrifice = false;
        }

        /// <summary>
        /// 弹窗关掉了（确认或取消都会发）。<b>第 2 步就在这里开</b>：
        /// 第 1 步刚确认时 <see cref="_pendingSacrifice"/> 为真，此刻第 1 个弹窗
        /// 已经真的关掉了，再 <c>Show</c> 不会被它后面的 <c>Hide</c> 按灭。
        /// </summary>
        private void OnPickerClosed()
        {
            // 已经结束 / 正在播动画时不要把引导文字放回来 ——
            // 否则会出现「节点都结束了、屏幕上还写着『点击台面强化』」。
            if (_ended || _busy)
            {
                ResetPick();
                return;
            }

            if (_pendingSacrifice && _target != null)
            {
                _pendingSacrifice = false;
                _view.OpenPicker(BuildSacrificeEntries(),
                    "强化卡牌 · 第 2 步：选择祭品（会被消耗）",
                    UpgradePickerView.Mode.Sacrifice,
                    AltarRite.Preview(null, _target));
                return;
            }

            // 中途取消 / 直接关掉：把这半截选择丢掉，别让下一次点击踩着上次的目标。
            ResetPick();
            _view.SetHintVisible(true);
        }

        private void OnFxFinished()
        {
            _busy = false;

            if (_endNodeAfterUpgrade)
            {
                EndNode();
                return;
            }

            _view.SetHintVisible(true);
            _view.SetTableInteractable(true);
        }

        /// <summary>
        /// 结束这个节点。
        ///
        /// <para>用户 2026-09-30 口径：<b>一次强化一张，确认完就结束节点</b>。
        /// 正式流程里这一下会提交「完成节点」并回地图层；本场景是独立调试场景，所以只打日志 + 收摊。</para>
        /// </summary>
        private void EndNode()
        {
            _ended = true;
            _view.ClosePicker();
            _view.SetHintVisible(false);
            _view.SetTableInteractable(false);

            if (_logLeaveClick)
            {
                Debug.Log("[UpgradeSceneEntry] 强化完成，本节点结束（正式流程里这一下会回地图层）。");
            }
        }

        private void OnLeaveClicked()
        {
            _view.ClosePicker();

            // 2026-10-02：从地图走进来的话，「离开」= 完成这个节点并回地图
            //（地图那边 MapSceneEntry.Awake 会结算这一步）。
            if (MapRoutes.LeaveToMap())
            {
                if (_logLeaveClick)
                {
                    Debug.Log("[UpgradeSceneEntry] 离开石台，回地图。");
                }

                return;
            }

            if (_logLeaveClick)
            {
                Debug.Log("[UpgradeSceneEntry] 点了「离开」—— 单场景调试下到此为止；"
                          + "从地图走进来时这一下会回地图层。");
            }
        }
    }
}
