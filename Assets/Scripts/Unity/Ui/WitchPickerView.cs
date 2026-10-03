using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊里的<b>卡池浏览层</b>（2026-10-01 · P6）：点了某个空位之后弹出来，
    /// 从**玩家当前的卡池**里挑一张填进去。
    ///
    /// <para><b>它长什么样</b>：一层铺满画布的遮罩 + 一块 <c>Peek_Panel</c> 面板 +
    /// 可滚动的卡面网格 + 左下「关闭」。版式与
    /// <see cref="UpgradePickerView"/> / <see cref="ShopBagView"/> 完全同一套
    /// （同一张底图、同一批 <c>UiLayout.ShopBag*</c> 坐标、同一个 <c>CardView_Hand.prefab</c>）。</para>
    ///
    /// <para><b>与 <see cref="UpgradePickerView"/> 的两处关键差别</b>：</para>
    /// <list type="number">
    /// <item><description><b>点一张就返回</b>（下单式），不是「选中 → 再按确认」——
    /// 因为外层浮层已经有自己的「确认」了，这里再来一个是两层确认，玩家会懵。
    /// <b>例外</b>：点中的献祭牌有 &gt;1 条效果时会先换成「选哪一条效果」那一屏
    /// （2026-10-02），选完才返回。</description></item>
    /// <item><description><b>没有「预览」与「确认」</b>，也没有「N 张 · 可选 M 张」那行计数 ——
    /// 这里的语义是「给这个空位挑一张牌」，不是「强化这张牌」。</description></item>
    /// </list>
    ///
    /// <para><b>两屏共用一块面板</b>：卡池网格（<c>Cards</c>）与效果选择屏
    /// （<c>EffectChoice</c>）位置完全重合，靠 active 切换 —— 所以两个根节点都要
    /// 有各自的引用（别在代码里靠遍历子节点猜哪个是哪个）。</para>
    ///
    /// <para><b>⚠ 格子复用 <see cref="UpgradePickerCell"/>，不新建第二份</b>：
    /// 「一格 = 卡面 + 压暗纱（含原因）+ 整格命中区」这套结构两个场景一模一样，
    /// 抄一份只会让「格子该怎么摆」这件事有两个来源。所以本类直接消费
    /// <see cref="UpgradePickerView.Entry"/>（<c>Card</c> / <c>Upgradable</c> / <c>Reason</c>）——
    /// 那三个字段在这里读作「这张牌 / 这个空位能不能选它 / 不能选的原因」。</para>
    ///
    /// <para><b>⚠ 遮罩的职责不只是好看</b>：它吃射线且盖满画布 ——
    /// 少了这一层会出现「隔着面板点到空位、又弹一次浏览」这种零报错的怪事。</para>
    ///
    /// <para><b>⚠ 本类所在节点在构建器里就是失活的，且 <c>Awake</c> 里不许 Hide</b>（同 §上面那个坑）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchPickerView : MonoBehaviour
    {
        /// <summary>点它关闭（铺满画布的遮罩，同时挡住底下的主浮层）。</summary>
        [SerializeField] private Button _veilButton;

        /// <summary>「关闭」按钮（取消这次挑选，空位保持原样）。</summary>
        [SerializeField] private Button _closeButton;

        [SerializeField] private TMP_Text _title;

        /// <summary>面板左上那句固定的说明（「点一张牌，填进这个空位」）。</summary>
        [SerializeField] private TMP_Text _hint;

        [SerializeField] private ScrollRect _scroll;

        /// <summary>网格内容节点（<c>GridLayoutGroup</c> + <c>ContentSizeFitter</c> 挂在它上面）。</summary>
        [SerializeField] private RectTransform _content;

        /// <summary>格子模板（失活，藏在 <see cref="_content"/> 里）。</summary>
        [SerializeField] private UpgradePickerCell _cellTemplate;

        /// <summary>一张牌都没有时的提示（默认隐藏）。</summary>
        [SerializeField] private GameObject _empty;

        // ── 效果选择那一屏（2026-10-02）──────────────────────────────
        // 选卡列表与它是**同一块矩形**（`Cards` 的位置），靠 active 切换；
        // 所以必须单独拿一个引用，不能在 Show 里靠遍历子节点猜。

        /// <summary>卡池那一屏的根（= `Cards` 滚动区，要跟效果选择屏互相切换）。</summary>
        [SerializeField] private GameObject _cardsRoot;

        /// <summary>效果选择屏的根（默认隐藏）。</summary>
        [SerializeField] private GameObject _effectPanel;

        /// <summary>效果选择屏的标题（「选一条要转移的效果」）。</summary>
        [SerializeField] private TMP_Text _effectTitle;

        /// <summary>效果选择屏的行容器（行按 <c>WitchEffectFirstRowY</c> 往下排）。</summary>
        [SerializeField] private RectTransform _effectList;

        /// <summary>一条效果的模板（失活，藏在 <see cref="_effectList"/> 里）。</summary>
        [SerializeField] private WitchEffectOptionView _effectTemplate;

        /// <summary>效果选择屏底部那行说明。</summary>
        [SerializeField] private TMP_Text _effectHint;

        /// <summary>「返回」：从效果选择屏回到卡池列表（换一张牌）。</summary>
        [SerializeField] private Button _backButton;

        /// <summary>已经克隆出来的格子（多出来的失活备用，不销毁 —— 反复开关不该一直产生垃圾）。</summary>
        private readonly List<UpgradePickerCell> _cells = new List<UpgradePickerCell>();

        /// <summary>已经克隆出来的效果行（同上）。</summary>
        private readonly List<WitchEffectOptionView> _options = new List<WitchEffectOptionView>();

        /// <summary>本次显示的数据（<see cref="Show"/> 给的那一份，原样存着）。</summary>
        private readonly List<UpgradePickerView.Entry> _entries = new List<UpgradePickerView.Entry>();

        /// <summary>这一层要不要走「效果选择」这一步（只有献祭位才要走）。</summary>
        private bool _offerEffectChoice;

        /// <summary>等玩家选效果的**那张牌**（多效果牌的卡面被点之后停在这里）。</summary>
        private CardDef _pendingCard;

        /// <summary>
        /// 玩家点了一张牌，参数 = 那张牌 + 用它的**第几条效果**（<c>-1</c> = 用不着效果，
        /// 例如给右空位选目标牌）。上层拿它填进对应的空位。
        /// </summary>
        public event Action<CardDef, int> Picked;

        /// <summary>浏览层被关掉了（选了牌 / 点遮罩 / 点关闭都会发）。</summary>
        public event Action Closed;

        /// <summary>浏览层是不是开着。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>当前显示出来的格子数（验收探针用）。</summary>
        public int VisibleCellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i] != null && _cells[i].gameObject.activeSelf)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>这次卡池里可选（能点）的张数（验收探针用）。</summary>
        public int SelectableCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Upgradable)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）。
            Wire(_veilButton, OnCancel);
            Wire(_closeButton, OnCancel);
            Wire(_backButton, OnBack);
        }

        // ══════════════════════════════════════════════════════
        //  开关
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 打开浏览层并把卡池画出来。<b>幂等</b>：反复调用都行 ——
        /// 会先把上一次用的格子复用 / 失活，再按本次张数激活，
        /// 所以「给左空位选完又给右空位选」不需要重建面板。
        /// </summary>
        /// <param name="entries">每一格显示什么（null / 空表 = 显示「卡池里还没有牌」）。</param>
        /// <param name="title">面板标题（如「选择献祭的牌」）。</param>
        /// <param name="hint">面板里那行固定说明。</param>
        /// <param name="offerEffectChoice">
        /// 点中一张<b>多效果</b>的牌之后要不要多走一步「选哪一条效果」（2026-10-02）。
        /// 只有献祭位传 <c>true</c>；给右空位选目标牌时传 <c>false</c>（目标必须恰好 1 条效果，
        /// 没有可选的余地）。
        /// </param>
        public void Show(IList<UpgradePickerView.Entry> entries, string title, string hint,
            bool offerEffectChoice = false)
        {
            _offerEffectChoice = offerEffectChoice;
            _pendingCard = null;

            _entries.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    _entries.Add(entries[i]);
                }
            }

            int count = _entries.Count;
            EnsureCells(count);

            for (int i = 0; i < _cells.Count; i++)
            {
                bool used = i < count;
                _cells[i].SetVisible(used);

                if (!used)
                {
                    continue;
                }

                UpgradePickerView.Entry entry = _entries[i];
                _cells[i].Bind(i, entry.Card, entry.Upgradable, entry.Reason, 0);
            }

            if (_title != null)
            {
                _title.text = string.IsNullOrEmpty(title) ? "选择一张牌" : title;
            }

            if (_hint != null)
            {
                _hint.text = hint ?? string.Empty;
            }

            if (_empty != null && _empty.activeSelf != (count == 0))
            {
                _empty.SetActive(count == 0);
            }

            if (_scroll != null)
            {
                _scroll.StopMovement();
                _scroll.verticalNormalizedPosition = 1f;
            }

            ShowCardsScreen();

            IsOpen = true;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);     // 这一下会触发 Awake（首次）
            }

            // 盖在整层之上：构建器把它排在主浮层后面，但运行时格子是复制出来的 ——
            // 谁在最后不能只靠构建顺序保证，这里自己钉一次。
            transform.SetAsLastSibling();
        }

        /// <summary>关掉浏览层。重复调用是幂等的（只有真正关掉那一次会发事件）。</summary>
        public void Hide()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            _pendingCard = null;
            ShowCardsScreen();                  // 下次打开总是从卡池那一屏开始
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>浏览层现在是不是正显示「选哪一条效果」那一屏（验收探针用）。</summary>
        public bool IsChoosingEffect
        {
            get { return _effectPanel != null && _effectPanel.activeSelf; }
        }

        /// <summary>当前那一屏列出了几条效果（验收探针用）。</summary>
        public int VisibleOptionCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _options.Count; i++)
                {
                    if (_options[i] != null && _options[i].gameObject.activeSelf)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>当前那一屏的第 <paramref name="index"/> 条效果文案（验收探针用）。</summary>
        public string OptionTextAt(int index)
        {
            if (index < 0 || index >= _options.Count || _options[index] == null)
            {
                return string.Empty;
            }

            return _options[index].Text;
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 保证手里有 <paramref name="count"/> 个格子实例。
        ///
        /// <para>与背包 / 强化弹窗同一套口径：按需克隆 + 失活复用，**不预建满** ——
        /// 卡池 8 张时白扛 45 个卡面实例，而建满又在卡表扩容后不够用。</para>
        /// </summary>
        private void EnsureCells(int count)
        {
            if (_cellTemplate == null || _content == null)
            {
                return;
            }

            while (_cells.Count < count)
            {
                UpgradePickerCell clone = Instantiate(_cellTemplate, _content);
                clone.name = "WitchCell_" + _cells.Count;
                clone.gameObject.SetActive(false);
                clone.Clicked += OnCellClicked;
                _cells.Add(clone);
            }
        }

        /// <summary>
        /// 点了一格。
        ///
        /// <para><b>三条分支</b>（2026-10-02）：</para>
        /// <list type="number">
        /// <item>这一层不问效果（给右空位选目标）→ 直接报给上层，<c>effectIndex = -1</c>；</item>
        /// <item>献祭牌<b>只有 1 条效果</b> → 直接报（<c>0</c>），不再多问一步
        ///   （用户口径：「当第 1 张卡牌只有一个效果时，则将其效果转移给第 2 张」）；</item>
        /// <item>献祭牌<b>有多条效果</b> → 换成「选哪一条」那一屏，等玩家点一条才报
        ///   （用户口径：「需要在选择卡牌的界面当中额外，显示一个让玩家选择获得哪一项效果的选项」）。</item>
        /// </list>
        ///
        /// <para>⚠ <b>先发事件再 Hide</b>的次序不能换：<see cref="Hide"/> 会发
        /// <see cref="Closed"/>，上层收到「浏览层关了」时如果还没拿到选中的牌，
        /// 就会把「这次是给哪个空位选的」这个上下文清掉（表现为「点了牌什么都没发生」）。</para>
        /// </summary>
        private void OnCellClicked(UpgradePickerCell cell)
        {
            if (cell == null || cell.Card == null)
            {
                return;
            }

            CardDef card = cell.Card;

            if (_offerEffectChoice && card.Effects != null && card.Effects.Count > 1)
            {
                ShowEffectScreen(card);
                return;                         // 这一屏还开着，等玩家选一条效果
            }

            Picked?.Invoke(card, _offerEffectChoice ? 0 : -1);
            Hide();
        }

        /// <summary>
        /// 换成「选一条效果」那一屏。
        ///
        /// <para>⚠ 标题与说明都带上牌名 —— 玩家刚点完一格，屏幕上换了一批东西，
        /// 不写清「这是哪张牌的效果」会以为点错了。</para>
        /// </summary>
        private void ShowEffectScreen(CardDef card)
        {
            _pendingCard = card;

            int count = card.Effects == null ? 0 : card.Effects.Count;
            EnsureOptions(count);

            for (int i = 0; i < _options.Count; i++)
            {
                bool used = i < count;
                _options[i].SetVisible(used);

                if (!used)
                {
                    continue;
                }

                // 文案直接用卡面那一条效果自己的文字（与卡面 / 日志同源，不改写、
                // 不加「α」之类前缀 —— 那是卡面排版的事）。
                _options[i].Bind(i, card.Effects[i] == null ? string.Empty : card.Effects[i].Text);
            }

            if (_effectTitle != null)
            {
                _effectTitle.text = "《" + card.Name + "》有 " + count + " 条效果，选一条转移";
            }

            if (_effectHint != null)
            {
                _effectHint.text = "这条效果会加到右边那张牌上，并成为它自己的效果";
            }

            if (_cardsRoot != null && _cardsRoot.activeSelf)
            {
                _cardsRoot.SetActive(false);
            }

            if (_effectPanel != null && !_effectPanel.activeSelf)
            {
                _effectPanel.SetActive(true);
            }
        }

        /// <summary>回到卡池那一屏（点「返回」时用；卡池数据原样还在 <see cref="_entries"/> 里）。</summary>
        private void ShowCardsScreen()
        {
            _pendingCard = null;

            if (_effectPanel != null && _effectPanel.activeSelf)
            {
                _effectPanel.SetActive(false);
            }

            if (_cardsRoot != null && !_cardsRoot.activeSelf)
            {
                _cardsRoot.SetActive(true);
            }
        }

        /// <summary>点了一条效果：报给上层（带序号），然后关掉整层。</summary>
        private void OnEffectClicked(WitchEffectOptionView option)
        {
            if (option == null || _pendingCard == null)
            {
                return;
            }

            CardDef card = _pendingCard;
            int index = option.Index;
            Picked?.Invoke(card, index);
            Hide();
        }

        private void OnBack()
        {
            ShowCardsScreen();
        }

        private void OnCancel()
        {
            Hide();
        }

        /// <summary>
        /// 保证手里有 <paramref name="count"/> 个效果行实例（按需克隆 + 失活复用，不预建满）。
        /// </summary>
        private void EnsureOptions(int count)
        {
            if (_effectTemplate == null || _effectList == null)
            {
                return;
            }

            while (_options.Count < count)
            {
                WitchEffectOptionView clone = Instantiate(_effectTemplate, _effectList);
                clone.name = "EffectOption_" + _options.Count;
                clone.gameObject.SetActive(false);
                clone.Clicked += OnEffectClicked;
                _options.Add(clone);
            }
        }

        /// <summary>把按钮接到回调（幂等：删了再加，避免重复接）。</summary>
        private void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }
    }
}
