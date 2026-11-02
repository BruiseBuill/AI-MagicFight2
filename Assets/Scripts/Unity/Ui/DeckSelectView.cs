using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>自定义战斗 · 牌组编成界面</b>（2026-11-01 重设计）。
    ///
    /// <para><b>它长什么样</b>：左侧整张卡表（5 列网格，可滚），右侧<b>当前正在编辑的那一方</b>的
    /// 8 个格子；左上两枚页签牌座在「你的牌组 / 对手的牌组」之间切；底部「清空 / 开战」。</para>
    ///
    /// <para><b>按钮口径（2026-11-01 用户要求「按钮要统一」）</b>：所有功能键一律是
    /// 战斗内选牌弹窗（充能 / 磁暴那一版）的<b>牌座</b> —— 同一张
    /// <c>HandPick_Seat.png</c>（264×66）、同一套亮/暗两态（<c>HandPick_SeatOff.png</c>）。
    /// 页签也用牌座表达：亮 = 正在编辑这一方。别再引 `HandPick_Confirm.png` 那条奶油长条。</para>
    ///
    /// <para><b>它管什么</b>：只负责「摆」和「报」—— 把卡表画出来、把已选的卡点亮、
    /// 把已被对方占走的卡灰掉、把 8 个格子铺满，然后报出
    /// <see cref="CatalogCardClicked"/> / <see cref="TabClicked"/> / <see cref="SlotRemoveClicked"/> /
    /// <see cref="StartClicked"/> / <see cref="BackClicked"/> / <see cref="ClearClicked"/>。
    /// <b>哪张牌在谁的牌组里、能不能再加一张、能不能开战</b>全部由
    /// <see cref="DeckSelectEntry"/> 决定（与工程里其它界面同一分工）。</para>
    ///
    /// <para><b>节点由构建器建</b>（<c>DeckSelectUiBuilder</c>）：卡面模板、8 个格子（含虚线空位、
    /// 编号、右上角移除键）、两枚页签都是那趟构建出来的长期节点；运行时只按需克隆卡表的卡面并复用。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckSelectView : MonoBehaviour
    {
        [Header("标题 / 提示")]
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _hint;

        [Header("两枚页签（牌座亮 / 暗两态）")]
        [SerializeField] private Button _playerTab;
        [SerializeField] private Image _playerTabImage;
        [SerializeField] private TMP_Text _playerTabLabel;
        [SerializeField] private Button _enemyTab;
        [SerializeField] private Image _enemyTabImage;
        [SerializeField] private TMP_Text _enemyTabLabel;

        [Header("牌座贴图（亮 / 暗）—— 与 HandPick 弹窗同素材")]
        [SerializeField] private Sprite _seatOn;
        [SerializeField] private Sprite _seatOff;

        [Header("左侧卡表")]
        [SerializeField] private ScrollRect _catalogScroll;
        [SerializeField] private RectTransform _catalogContent;
        [SerializeField] private CardView _catalogTemplate;

        [Header("右侧 8 格")]
        [SerializeField] private CardView[] _slotViews = new CardView[0];

        /// <summary>空位虚线框（有卡时隐藏）。</summary>
        [SerializeField] private GameObject[] _slotFrames = new GameObject[0];

        /// <summary>空位编号 1–8（有卡时隐藏）。</summary>
        [SerializeField] private TMP_Text[] _slotNumbers = new TMP_Text[0];

        /// <summary>右上角 × 移除键（空位时隐藏）。</summary>
        [SerializeField] private Button[] _slotRemoves = new Button[0];

        [Header("底部")]
        [SerializeField] private Button _startButton;
        [SerializeField] private Image _startImage;
        [SerializeField] private TMP_Text _startLabel;
        [SerializeField] private Button _clearButton;
        [SerializeField] private Image _clearImage;
        [SerializeField] private TMP_Text _clearLabel;
        [SerializeField] private Button _backButton;

        [Header("历史牌组 / 随机（2026-11-02）")]
        [SerializeField] private Button _historyButton;
        [SerializeField] private Image _historyImage;
        [SerializeField] private TMP_Text _historyLabel;
        [Tooltip("历史牌座右上角的计数徽标（0 条时隐藏）。")]
        [SerializeField] private GameObject _historyBadge;
        [SerializeField] private TMP_Text _historyBadgeText;
        [SerializeField] private Button _randomButton;
        [SerializeField] private Image _randomImage;
        [SerializeField] private TMP_Text _randomLabel;

        [Header("历史牌组面板（覆盖层）")]
        [SerializeField] private GameObject _historyOverlay;
        [Tooltip("整屏遮罩，同时也是「点空白关闭」的按钮。")]
        [SerializeField] private Button _historyVeil;
        [SerializeField] private Button _historyCloseButton;
        [Tooltip("面板底部那枚「关闭」牌座（与标题栏的 × 同一件事）。")]
        [SerializeField] private Button _historyCloseSeat;
        [SerializeField] private RectTransform _historyRowsRoot;
        [Tooltip("两列网格的 Content；高度由 ShowHistory 按行数算（不用 ContentSizeFitter）。")]
        [SerializeField] private RectTransform _historyContent;
        [SerializeField] private DeckHistoryRowView _historyRowTemplate;
        [SerializeField] private GameObject _historyEmpty;
        [SerializeField] private TMP_Text _historySubtitle;
        [SerializeField] private ScrollRect _historyScroll;

        [Header("中央浮字（复用战斗的 FloatTipView）")]
        [SerializeField] private FloatTipView _toast;

        /// <summary>点了某一方的页签（0 = 你 / 1 = 对手）。</summary>
        public event Action<int> TabClicked;

        /// <summary>点了卡表里的某一张（参数 = 卡表下标）。</summary>
        public event Action<int> CatalogCardClicked;

        /// <summary>点了 8 格中某一格的 ×（参数 = 槽位下标 0–7）。</summary>
        public event Action<int> SlotRemoveClicked;

        public event Action StartClicked;
        public event Action BackClicked;
        public event Action ClearClicked;

        /// <summary>点了「随机 8 张」（只影响当前页签这一方）。</summary>
        public event Action RandomClicked;

        /// <summary>点了顶部的「历史牌组」牌座。</summary>
        public event Action HistoryClicked;

        /// <summary>历史面板被关掉（点遮罩 / 点 × / 点关闭）。</summary>
        public event Action HistoryClosed;

        /// <summary>点了某一条的「载入」（参数 = 该条在存档里的下标）。</summary>
        public event Action<int> HistoryLoadClicked;

        /// <summary>点了某一条的「×」（参数 = 该条在存档里的下标）。</summary>
        public event Action<int> HistoryDeleteClicked;

        /// <summary>当前卡表里铺了几张（探针用）。</summary>
        public int CatalogCardCount
        {
            get { return _catalogCards.Count; }
        }

        /// <summary>当前有几格是填满的（探针用）。</summary>
        public int FilledSlotCount
        {
            get
            {
                int n = 0;
                if (_slotViews == null) return 0;
                for (int i = 0; i < _slotViews.Length; i++)
                {
                    if (_slotViews[i] != null && _slotViews[i].HasCard) n++;
                }

                return n;
            }
        }

        private readonly List<CardView> _catalogCards = new List<CardView>();

        /// <summary>卡表卡面 → 它下面那条「对手已选」标签（构建器建的长期节点，克隆自带）。</summary>
        private readonly Dictionary<CardView, GameObject> _takenTags = new Dictionary<CardView, GameObject>();

        /// <summary>当前页签（只用于<b>探针</b>与「同一帧重复刷新时别动动画」判断）。</summary>
        private int _tab = -1;

        private UnityAction _playerTabHandler;
        private UnityAction _enemyTabHandler;
        private UnityAction _startHandler;
        private UnityAction _backHandler;
        private UnityAction _clearHandler;
        private UnityAction _historyHandler;
        private UnityAction _randomHandler;
        private UnityAction _historyVeilHandler;
        private UnityAction _historyCloseHandler;
        private UnityAction _historyCloseSeatHandler;
        private readonly List<UnityAction> _removeHandlers = new List<UnityAction>();

        /// <summary>历史面板的行（按需克隆 + 失活复用，与卡表卡面同一口径）。</summary>
        private readonly List<DeckHistoryRowView> _historyRows = new List<DeckHistoryRowView>();

        /// <summary>历史面板当前铺了几行（探针用）。</summary>
        public int HistoryRowCount
        {
            get { return _historyRows.Count; }
        }

        private void Awake()
        {
            Hook();
        }

        private void OnEnable()
        {
            Hook();
        }

        private void OnDisable()
        {
            Unhook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        /// <summary>
        /// 监听器一律运行时按序列化字段自己接（铁律 8）：构建器在编辑模式接的那些不是序列化数据，
        /// 存 Prefab / 场景时直接蒸发 —— 症状是「每一项检查都通过，但按钮点下去什么都没有」。
        /// </summary>
        private void Hook()
        {
            HookButton(_playerTab, ref _playerTabHandler, delegate { RaiseTab(0); });
            HookButton(_enemyTab, ref _enemyTabHandler, delegate { RaiseTab(1); });
            HookButton(_startButton, ref _startHandler, RaiseStart);
            HookButton(_backButton, ref _backHandler, RaiseBack);
            HookButton(_clearButton, ref _clearHandler, RaiseClear);
            HookButton(_historyButton, ref _historyHandler, RaiseHistory);
            HookButton(_randomButton, ref _randomHandler, RaiseRandom);
            HookButton(_historyVeil, ref _historyVeilHandler, RaiseHistoryClosed);
            HookButton(_historyCloseButton, ref _historyCloseHandler, RaiseHistoryClosed);
            HookButton(_historyCloseSeat, ref _historyCloseSeatHandler, RaiseHistoryClosed);

            if (_slotRemoves == null)
            {
                return;
            }

            while (_removeHandlers.Count < _slotRemoves.Length)
            {
                int index = _removeHandlers.Count;
                _removeHandlers.Add(delegate { RaiseSlotRemove(index); });
            }

            for (int i = 0; i < _slotRemoves.Length; i++)
            {
                Button button = _slotRemoves[i];
                if (button == null)
                {
                    continue;
                }

                UnityAction handler = _removeHandlers[i];
                button.onClick.RemoveListener(handler);
                button.onClick.AddListener(handler);
            }
        }

        private static void HookButton(Button button, ref UnityAction handler, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            if (handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = action;
            button.onClick.AddListener(handler);
        }

        private void Unhook()
        {
            UnhookButton(_playerTab, ref _playerTabHandler);
            UnhookButton(_enemyTab, ref _enemyTabHandler);
            UnhookButton(_startButton, ref _startHandler);
            UnhookButton(_backButton, ref _backHandler);
            UnhookButton(_clearButton, ref _clearHandler);
            UnhookButton(_historyButton, ref _historyHandler);
            UnhookButton(_randomButton, ref _randomHandler);
            UnhookButton(_historyVeil, ref _historyVeilHandler);
            UnhookButton(_historyCloseButton, ref _historyCloseHandler);
            UnhookButton(_historyCloseSeat, ref _historyCloseSeatHandler);

            if (_slotRemoves != null)
            {
                for (int i = 0; i < _slotRemoves.Length && i < _removeHandlers.Count; i++)
                {
                    // ⚠ List 索引器不是引用返回，不能直接 ref 进去 —— 取出来、解绑、再写回。
                    UnityAction handler = _removeHandlers[i];
                    UnhookButton(_slotRemoves[i], ref handler);
                    _removeHandlers[i] = handler;
                }
            }
        }

        private static void UnhookButton(Button button, ref UnityAction handler)
        {
            if (button != null && handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = null;
        }

        private void RaiseTab(int index)
        {
            Action<int> handler = TabClicked;
            if (handler != null) handler(index);
        }

        private void RaiseStart()
        {
            Action handler = StartClicked;
            if (handler != null) handler();
        }

        private void RaiseBack()
        {
            Action handler = BackClicked;
            if (handler != null) handler();
        }

        private void RaiseClear()
        {
            Action handler = ClearClicked;
            if (handler != null) handler();
        }

        private void RaiseSlotRemove(int index)
        {
            Action<int> handler = SlotRemoveClicked;
            if (handler != null) handler(index);
        }

        private void RaiseRandom()
        {
            Action handler = RandomClicked;
            if (handler != null) handler();
        }

        private void RaiseHistory()
        {
            Action handler = HistoryClicked;
            if (handler != null) handler();
        }

        private void RaiseHistoryClosed()
        {
            Action handler = HistoryClosed;
            if (handler != null) handler();
        }

        private void RaiseHistoryLoad(int storeIndex)
        {
            Action<int> handler = HistoryLoadClicked;
            if (handler != null) handler(storeIndex);
        }

        private void RaiseHistoryDelete(int storeIndex)
        {
            Action<int> handler = HistoryDeleteClicked;
            if (handler != null) handler(storeIndex);
        }

        private void OnCatalogCardClicked(CardView card)
        {
            Action<int> handler = CatalogCardClicked;
            if (handler != null && card != null) handler(card.SlotIndex);
        }

        // ══════════════════════════════════════════════════════
        //  铺界面
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把整张卡表铺到左侧网格里。
        /// </summary>
        /// <param name="cards">卡表里的全部卡（顺序固定 = 卡表顺序）。</param>
        /// <param name="pickedIds">当前页签已经选中的卡（按 ID 点亮）。</param>
        /// <param name="takenIds">
        /// 已被<b>另一方</b>选走的卡（按 ID 压暗 + 挂「对手已选」标签）。
        /// 2026-11-01 新增：旧版没有任何可见标记，点下去只弹一句提示，
        /// 玩家看到的就是「点了没反应」。
        /// </param>
        public void ShowCatalog(IReadOnlyList<CardDef> cards, ICollection<string> pickedIds,
            ICollection<string> takenIds)
        {
            int count = cards != null ? cards.Count : 0;
            EnsureCatalogCards(count);

            for (int i = 0; i < _catalogCards.Count; i++)
            {
                CardView view = _catalogCards[i];
                if (view == null)
                {
                    continue;
                }

                bool used = i < count;
                if (view.gameObject.activeSelf != used)
                {
                    view.gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                CardDef def = cards[i];
                bool picked = pickedIds != null && pickedIds.Contains(def.Id);
                bool taken = takenIds != null && takenIds.Contains(def.Id);

                view.Bind(CardSnapshot.FromDef(def, 0, def.Cooldown), CardView.ViewMode.Hand, i);
                view.SetFaceWidth(DeckCardFaceWidth);

                // 被对方占走的那张仍然可点 —— 点了由 Entry 说明原因（弹一句提示），
                // 比「卡面吃不到点击」更像「这张牌确实存在、只是不能选」。
                view.SetInteractable(true);
                view.SetSelected(picked);
                view.SetDimmed(taken);
                SetTakenTag(view, taken);
            }

            if (_catalogScroll != null)
            {
                _catalogScroll.StopMovement();
            }
        }

        /// <summary>把当前页签的 8 个格子铺出来（少于 8 张 = 后面的格子显示虚线 + 编号）。</summary>
        public void ShowDeck(IReadOnlyList<CardDef> cards)
        {
            if (_slotViews == null)
            {
                return;
            }

            for (int i = 0; i < _slotViews.Length; i++)
            {
                CardDef def = cards != null && i < cards.Count ? cards[i] : null;
                bool filled = def != null;

                CardView view = _slotViews[i];
                if (view != null)
                {
                    if (filled)
                    {
                        UiPanelAnimator.SetVisible(view.gameObject, true, interactive: false);
                        view.Bind(CardSnapshot.FromDef(def, 0, def.Cooldown), CardView.ViewMode.Hand, i);
                        view.SetFaceWidth(DeckSlotFaceWidth);
                        view.SetReadOnly();
                        view.SetSelected(false);
                        view.SetDimmed(false);
                    }
                    else
                    {
                        // ⚠ 必须先 Clear 再隐藏：只隐藏的话 CardView.HasCard 仍是 true，
                        //   切页签后 8 格会继续报「填满」（2026-11-01 实测探针抓到）。
                        view.Clear();
                        UiPanelAnimator.SetVisible(view.gameObject, false, interactive: false);
                    }
                }

                SetActiveSafe(_slotFrames, i, !filled);
                SetActiveSafe(_slotNumbers, i, !filled);

                if (_slotRemoves != null && i < _slotRemoves.Length && _slotRemoves[i] != null
                    && _slotRemoves[i].gameObject.activeSelf != filled)
                {
                    _slotRemoves[i].gameObject.SetActive(filled);
                }
            }
        }

        /// <summary>切换页签的亮 / 暗与计数（N/8）。</summary>
        public void SetTab(int index, int playerCount, int enemyCount)
        {
            bool tabChanged = _tab != index;
            _tab = index;

            ApplySeat(_playerTabImage, _playerTabLabel, index == 0);
            ApplySeat(_enemyTabImage, _enemyTabLabel, index == 1);

            if (_playerTabLabel != null)
            {
                _playerTabLabel.text = "你的牌组 " + playerCount + " / " + BattleLaunch.DeckSize;
            }

            if (_enemyTabLabel != null)
            {
                _enemyTabLabel.text = "对手的牌组 " + enemyCount + " / " + BattleLaunch.DeckSize;
            }

            // 标题固定 —— 现在在编辑哪一方由页签的亮 / 暗表达（标题再拼一遍会把 62 号字挤成两段）。
            if (_title != null && _title.text != DeckTitle)
            {
                _title.text = DeckTitle;
            }

            if (tabChanged)
            {
                ResetScrollToTop();
            }
        }

        /// <summary>
        /// 卡表滚回最上面。
        ///
        /// <para><b>为什么必须显式做</b>：切页签会整表重铺（<see cref="ShowCatalog"/> 会重新 Bind 44 张），
        /// ScrollRect 在布局还没重建时算出来的 bounds 是旧值，位置会飘到中间 ——
        /// 表现成「一进来卡表就滚到一半」（2026-11-01 第一次构建踩到）。
        /// 先强制刷新一次布局，再归位，顺序不能反。</para>
        /// </summary>
        private void ResetScrollToTop()
        {
            if (_catalogScroll == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            _catalogScroll.StopMovement();
            _catalogScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>大标题文案（构建器也用它，别在两处各写一遍）。</summary>
        public const string DeckTitle = "自定义战斗";

        /// <summary>「开战」键的可用状态（牌座换亮 / 暗两态，文字同步）。</summary>
        public void SetStartInteractable(bool interactable)
        {
            if (_startButton != null)
            {
                _startButton.interactable = interactable;
            }

            ApplySeat(_startImage, _startLabel, interactable);
        }

        /// <summary>「清空」键跟着当前页签有没有牌走（没牌就不用按）。</summary>
        public void SetClearInteractable(bool interactable)
        {
            if (_clearButton != null)
            {
                _clearButton.interactable = interactable;
            }

            ApplySeat(_clearImage, _clearLabel, interactable);
        }

        /// <summary>底部提示行。</summary>
        public void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text ?? string.Empty;
            }
        }

        // ══════════════════════════════════════════════════════
        //  历史牌组 / 随机（2026-11-02）
        // ══════════════════════════════════════════════════════

        /// <summary>历史牌座右上角的计数徽标（0 条时整枚隐藏，别显示「0」）。</summary>
        public void SetHistoryBadge(int count)
        {
            if (_historyBadge != null)
            {
                bool show = count > 0;
                if (_historyBadge.activeSelf != show)
                {
                    _historyBadge.SetActive(show);
                }
            }

            if (_historyBadgeText != null)
            {
                _historyBadgeText.text = count > 0 ? count.ToString() : string.Empty;
            }
        }

        /// <summary>
        /// 铺开历史面板。<paramref name="rows"/> 已经由 Entry 解析成卡定义
        /// （视图不认识存档，也不认识卡表）。
        /// </summary>
        public void ShowHistory(IReadOnlyList<DeckHistoryRowData> rows)
        {
            int count = rows != null ? rows.Count : 0;
            EnsureHistoryRows(count);

            for (int i = 0; i < _historyRows.Count; i++)
            {
                DeckHistoryRowView row = _historyRows[i];
                if (row == null)
                {
                    continue;
                }

                bool used = i < count;
                if (row.gameObject.activeSelf != used)
                {
                    row.gameObject.SetActive(used);
                }

                if (used)
                {
                    row.Bind(rows[i]);
                }
            }

            if (_historyEmpty != null && _historyEmpty.activeSelf != (count == 0))
            {
                _historyEmpty.SetActive(count == 0);
            }

            if (_historySubtitle != null)
            {
                _historySubtitle.text = count == 0
                    ? "还没有记录 —— 打一场就会自动留下"
                    : "共 " + count + " 条 · 只记「谁用过」和 8 张牌 · 最多 " + DeckHistoryStore.Capacity + " 条";
            }

            if (_historyContent != null)
            {
                // 两列 → 行数 = ceil(条数 / 2)。高度自己算，不用 ContentSizeFitter
                // （ScrollRect + Fitter + Grid 在编辑模式下会让 UGUI 反复重排，构建器那边踩过）
                int rowCount = (count + 1) / 2;
                float height = rowCount <= 0
                    ? 0f
                    : rowCount * HistoryRowHeight + (rowCount - 1) * HistoryRowGap;
                _historyContent.sizeDelta = new Vector2(_historyContent.sizeDelta.x, height);
            }

            if (_historyOverlay != null && !_historyOverlay.activeSelf)
            {
                _historyOverlay.SetActive(true);
            }

            if (_historyScroll != null)
            {
                // 与卡表同一个坑：先强制重建布局，再归位，顺序不能反
                Canvas.ForceUpdateCanvases();
                _historyScroll.StopMovement();
                _historyScroll.verticalNormalizedPosition = 1f;
            }
        }

        public void HideHistory()
        {
            if (_historyOverlay != null && _historyOverlay.activeSelf)
            {
                _historyOverlay.SetActive(false);
            }
        }

        /// <summary>历史面板现在是开的吗（探针 / 关闭逻辑用）。</summary>
        public bool IsHistoryOpen
        {
            get { return _historyOverlay != null && _historyOverlay.activeSelf; }
        }

        /// <summary>中央一句话浮字（复用战斗里的 FloatTipView，1 秒左右自己淡出）。</summary>
        public void ShowToast(string message)
        {
            if (_toast != null)
            {
                _toast.Show(message);
            }
        }

        /// <summary>维持至少 <paramref name="count"/> 行（克隆一次，之后只切 active）。</summary>
        private void EnsureHistoryRows(int count)
        {
            if (_historyRowTemplate == null || _historyRowsRoot == null)
            {
                return;
            }

            // ⚠ 模板必须失活：它在 Content 里也是 GridLayout 的一个孩子，
            //   亮着会自己占掉第一格、还吃掉第一行的点击（2026-11-02 实测抓到）。
            //   构建器已经会关它，这里再兜一层 —— 存量场景/prefab 不必重跑也正确。
            if (_historyRowTemplate.gameObject.activeSelf)
            {
                _historyRowTemplate.gameObject.SetActive(false);
            }

            while (_historyRows.Count < count)
            {
                DeckHistoryRowView clone = Instantiate(_historyRowTemplate, _historyRowsRoot);
                clone.name = "HistoryRow_" + _historyRows.Count;
                clone.gameObject.SetActive(false);

                // ⚠ 闭包捕获的必须是**这一行**，不是循环里会变的那个变量
                DeckHistoryRowView captured = clone;
                clone.LoadClicked += delegate { RaiseHistoryLoad(captured.StoreIndex); };
                clone.DeleteClicked += delegate { RaiseHistoryDelete(captured.StoreIndex); };

                _historyRows.Add(clone);
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>亮 / 暗两态换<b>两张图</b>（牌座的银边与青灯不是乘一个颜色能压暗的，乘出来会发灰）。</summary>
        private void ApplySeat(Image image, TMP_Text label, bool on)
        {
            if (image != null)
            {
                Sprite sprite = on ? _seatOn : _seatOff;
                if (sprite != null && image.sprite != sprite)
                {
                    image.sprite = sprite;
                }
            }

            if (label != null)
            {
                label.color = on ? UiTheme.HandPickSeatTextOn : SeatTextOff;
            }
        }

        private static void SetActiveSafe(GameObject[] nodes, int index, bool active)
        {
            if (nodes == null || index >= nodes.Length || nodes[index] == null)
            {
                return;
            }

            if (nodes[index].activeSelf != active)
            {
                nodes[index].SetActive(active);
            }
        }

        private static void SetActiveSafe(TMP_Text[] nodes, int index, bool active)
        {
            if (nodes == null || index >= nodes.Length || nodes[index] == null)
            {
                return;
            }

            if (nodes[index].gameObject.activeSelf != active)
            {
                nodes[index].gameObject.SetActive(active);
            }
        }

        private void SetTakenTag(CardView card, bool taken)
        {
            GameObject tag;
            if (!_takenTags.TryGetValue(card, out tag) || tag == null)
            {
                Transform found = card.transform.Find("TakenTag");
                tag = found == null ? null : found.gameObject;
                _takenTags[card] = tag;
            }

            if (tag != null && tag.activeSelf != taken)
            {
                tag.SetActive(taken);
            }
        }

        /// <summary>维持至少 <paramref name="count"/> 个卡表卡面（按需克隆 + 失活复用，同 CardPoolView）。</summary>
        private void EnsureCatalogCards(int count)
        {
            if (_catalogTemplate == null || _catalogContent == null)
            {
                return;
            }

            while (_catalogCards.Count < count)
            {
                CardView clone = Instantiate(_catalogTemplate, _catalogContent);
                clone.name = "CatalogCard_" + _catalogCards.Count;
                clone.gameObject.SetActive(false);
                clone.Clicked += OnCatalogCardClicked;
                _catalogCards.Add(clone);
            }
        }

        /// <summary>卡表卡面宽度（构建器的网格 cell 按它留位）。</summary>
        public const float DeckCardFaceWidth = 168f;

        /// <summary>右侧 8 格的卡面宽度（= 格子宽，让卡面正好填满格子）。</summary>
        public const float DeckSlotFaceWidth = 184f;

        /// <summary>历史面板里一行的高度（两列网格的 cell 高）。</summary>
        public const float HistoryRowHeight = 62f;

        /// <summary>历史面板里行与行的间距。</summary>
        public const float HistoryRowGap = 10f;

        /// <summary>历史面板里一行的宽度（半个面板减去间距）。</summary>
        public const float HistoryRowWidth = 734f;

        /// <summary>历史面板里一枚小牌的宽度 / 高度 / 间距（构建器与视图共用）。</summary>
        public const float HistoryChipWidth = 86f;
        public const float HistoryChipHeight = 30f;
        public const float HistoryChipGap = 5f;

        /// <summary>牌座暗态的文字色（与 HandPick 弹窗的暗态同色）。</summary>
        private static readonly Color SeatTextOff = new Color32(0x7C, 0x8D, 0xA3, 0xFF);
    }
}
