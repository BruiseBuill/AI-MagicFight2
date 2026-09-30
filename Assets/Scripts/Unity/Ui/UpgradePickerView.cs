using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 强化场景里的<b>选牌弹窗</b>（2026-09-30）：点石台弹出，从自己的卡池里挑一张来强化。
    ///
    /// <para><b>它长什么样</b>：一层铺满画布的遮罩 + 一块面板 + 可滚动的卡面网格 + 「确认」/「关闭」。
    /// 版式<b>照抄商店背包面板</b>（同一张 <c>Peek_Panel</c> 底图、同一批 <c>UiLayout.ShopBag*</c> 坐标、
    /// 同一个 <c>CardView_Hand.prefab</c>）—— 用户 2026-09-30 口径「照商店背包那套」。</para>
    ///
    /// <para><b>职责边界</b>（与 <see cref="ShopBagView"/> / <see cref="ShopView"/> 同口径）：
    /// 本类<b>只画、只收点击</b>。哪些牌能强化、为什么不能，是上层给的（<see cref="Entry"/>）——
    /// 这里不查卡表、不调 <see cref="CardUpgrade"/>，也不碰任何存档。</para>
    ///
    /// <para><b>⚠ 它自己负责关掉自己</b>：遮罩与「关闭」都接到 <see cref="Hide"/>，
    /// 关掉之后发一次 <see cref="Closed"/> 供上层记账；<b>只有「确认」会发
    /// <see cref="Confirmed"/></b>，而且发完自己先关。</para>
    ///
    /// <para><b>⚠ 遮罩的职责不只是好看</b>：它吃射线且盖满画布 ——
    /// 少了这一层会出现「隔着面板点到台面、又弹一次面板」这种零报错的怪事。</para>
    ///
    /// <para><b>⚠ 本类所在节点在构建器里就是失活的，且 <c>Awake</c> 里不许 Hide</b>：
    /// 首次 <c>SetActive(true)</c> 才触发 <c>Awake</c>，同一帧里的 Hide 会把刚显示的东西按灭，
    /// 而 <see cref="Show"/> 剩下的代码照跑（版式都设好了，屏幕上什么都没有）——
    /// 症状是「本局第一次点台面什么都不弹、第二次起正常」。这个坑在本工程已中招两次。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradePickerView : MonoBehaviour
    {
        /// <summary>一格要显示的东西（纯数据，UI 不认识它从哪来）。</summary>
        public struct Entry
        {
            /// <summary>这张牌的卡表定义（null = 空位）。</summary>
            public CardDef Card;

            /// <summary>能不能强化（<see cref="CardUpgrade.CanUpgrade"/> 的结果）。</summary>
            public bool Upgradable;

            /// <summary>不能强化的原因（可强化时为 null）。</summary>
            public string Reason;
        }

        /// <summary>点它关闭（铺满画布的半透明遮罩，同时挡住底下的石台与离开键）。</summary>
        [SerializeField] private Button _veilButton;

        /// <summary>「关闭」按钮（取消，不强化）。</summary>
        [SerializeField] private Button _closeButton;

        /// <summary>「确认」按钮（强化选中的那张）。</summary>
        [SerializeField] private Button _confirmButton;

        /// <summary>「确认」键的底图（按钮可用 / 不可用时切色调）。</summary>
        [SerializeField] private Image _confirmImage;

        [SerializeField] private TMP_Text _confirmLabel;

        [SerializeField] private TMP_Text _title;

        /// <summary>「卡池 N 张 · 可强化 M 张」那一行。</summary>
        [SerializeField] private TMP_Text _count;

        /// <summary>选中预览（「暴风雪+　力量 4 → 6」）；没选时是空串。</summary>
        [SerializeField] private TMP_Text _preview;

        /// <summary>一张牌都没有时的提示（默认隐藏）。</summary>
        [SerializeField] private GameObject _empty;

        [SerializeField] private ScrollRect _scroll;

        /// <summary>网格内容节点（<c>GridLayoutGroup</c> + <c>ContentSizeFitter</c> 挂在它上面）。</summary>
        [SerializeField] private RectTransform _content;

        /// <summary>格子模板（失活，藏在 <see cref="_content"/> 里）。</summary>
        [SerializeField] private UpgradePickerCell _cellTemplate;

        /// <summary>已经克隆出来的格子（多出来的失活备用，不销毁 —— 反复开关不该一直产生垃圾）。</summary>
        private readonly List<UpgradePickerCell> _cells = new List<UpgradePickerCell>();

        /// <summary>本次显示的数据（<see cref="Show"/> 给的那一份，原样存着）。</summary>
        private readonly List<Entry> _entries = new List<Entry>();

        private int _selected = -1;

        private bool _open;

        /// <summary>面板是不是开着。</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

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

        /// <summary>这次卡池里可强化的张数。</summary>
        public int UpgradableCount
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

        /// <summary>当前选中的序号（−1 = 没选）。</summary>
        public int SelectedIndex
        {
            get { return _selected; }
        }

        /// <summary>当前选中的那张牌（没选 = null）。</summary>
        public CardDef SelectedCard
        {
            get
            {
                return _selected >= 0 && _selected < _entries.Count ? _entries[_selected].Card : null;
            }
        }

        /// <summary>能不能按确认（= 选了一张）。</summary>
        public bool CanConfirm
        {
            get { return SelectedCard != null; }
        }

        /// <summary>玩家按了「确认」，参数 = 被选中的那张牌。上层拿它去生成新卡。</summary>
        public event Action<CardDef> Confirmed;

        /// <summary>面板被关掉了（参数 = 关掉时选中的序号，−1 = 没选）。</summary>
        public event Action<int> Closed;

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按序列化字段自己接（铁律 8）。
            Wire(_veilButton, OnCancel);
            Wire(_closeButton, OnCancel);
            Wire(_confirmButton, OnConfirm);
        }

        // ══════════════════════════════════════════════════════
        //  开关
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 打开弹窗并把卡池画出来。<b>幂等</b>：反复调用都行 ——
        /// 会先把上一次用的格子复用 / 失活，再按本次张数激活，
        /// 所以「强化完一张、又点了一次台面」不需要重建面板。
        /// </summary>
        /// <param name="entries">这一格要显示什么（null / 空表 = 显示「卡池里还没有牌」）。</param>
        /// <param name="title">面板标题。</param>
        public void Show(IList<Entry> entries, string title)
        {
            _entries.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    _entries.Add(entries[i]);
                }
            }

            _selected = -1;                     // 每次打开都是「还没选」
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

                Entry entry = _entries[i];
                _cells[i].Bind(i, entry.Card, entry.Upgradable, entry.Reason, 0);
            }

            if (_title != null)
            {
                _title.text = string.IsNullOrEmpty(title) ? "强化卡牌" : title;
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

            RefreshSelectionVisual();

            _open = true;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);     // 这一下会触发 Awake（首次）
            }

            // 盖在最上层：构建器把它排在最后，但运行时格子是复制出来的 ——
            // 谁在最后不能只靠构建顺序保证，这里自己钉一次。
            transform.SetAsLastSibling();
        }

        /// <summary>关掉弹窗。重复调用是幂等的（只有真正关掉那一次会发事件）。</summary>
        public void Hide()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            int selected = _selected;
            gameObject.SetActive(false);
            Closed?.Invoke(selected);
        }

        // ══════════════════════════════════════════════════════
        //  选中
        // ══════════════════════════════════════════════════════

        /// <summary>把第 <paramref name="index"/> 格选上（重复点同一格 = 取消选中）。</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= _entries.Count || !_entries[index].Upgradable)
            {
                return;
            }

            _selected = _selected == index ? -1 : index;
            RefreshSelectionVisual();
        }

        /// <summary>
        /// 重画所有选中态与底部那两行字。
        ///
        /// <para><b>⚠ 幂等且便宜</b>：卡面自己的 <c>SetSelected</c> 只在状态真的翻转时才动节点
        /// （手牌区每帧都调它），所以这里直接全量刷。</para>
        /// </summary>
        private void RefreshSelectionVisual()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] != null && i < _entries.Count)
                {
                    _cells[i].SetSelected(i == _selected);
                }
            }

            int upgradable = UpgradableCount;

            if (_count != null)
            {
                _count.text = "卡池 " + _entries.Count + " 张 · 可强化 " + upgradable + " 张";
            }

            CardDef card = SelectedCard;
            if (_preview != null)
            {
                _preview.text = card == null
                    ? (upgradable == 0 ? "卡池里没有可强化的牌" : "点一张牌，看它强化后的样子")
                    : "选中 " + CardUpgrade.UpgradedName(card)
                      + "　力量 " + card.Power + " → " + CardUpgrade.UpgradedPower(card);
            }

            ApplyConfirmState(card != null);
        }

        /// <summary>
        /// 「确认」键的可用态。
        ///
        /// <para><b>⚠ 只切 <c>enabled</c> / <c>interactable</c> 与 <c>Image.color</c>，
        /// 不切 active</b>：切 active 会让按钮在版式里「消失」，玩家找不到确认键在哪。</para>
        /// </summary>
        private void ApplyConfirmState(bool enabled)
        {
            if (_confirmButton != null)
            {
                _confirmButton.interactable = enabled;
            }

            if (_confirmImage != null)
            {
                _confirmImage.color = enabled ? UiTheme.UpgradeConfirmOn : UiTheme.UpgradeConfirmOff;
            }

            if (_confirmLabel != null)
            {
                _confirmLabel.color = enabled ? UiTheme.UpgradeConfirmOn : UiTheme.UpgradeConfirmOff;
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 保证手里有 <paramref name="count"/> 个格子实例。
        ///
        /// <para>与背包面板同一套口径：按需克隆 + 失活复用，**不预建满** ——
        /// 卡池 8 张时白扛 42 个卡面实例，而建满又在卡表扩容后不够用。</para>
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
                clone.name = "UpgradeCell_" + _cells.Count;
                clone.gameObject.SetActive(false);
                clone.Clicked += OnCellClicked;
                _cells.Add(clone);
            }
        }

        private void OnCellClicked(UpgradePickerCell cell)
        {
            Select(cell.Index);
        }

        private void OnCancel()
        {
            Hide();
        }

        /// <summary>
        /// 按确认：先把选中的牌报给上层，再关自己。
        ///
        /// <para>⚠ 顺序是「先发事件再 Hide」：<see cref="Hide"/> 会把选中序号清掉之前
        /// 先发 <see cref="Closed"/>，而上层在 <see cref="Confirmed"/> 里就要用到那张牌 ——
        /// 反过来写会让上层拿不到它（表现为「点了确认什么也没发生」）。</para>
        /// </summary>
        private void OnConfirm()
        {
            CardDef card = SelectedCard;
            if (card == null)
            {
                return;
            }

            Confirmed?.Invoke(card);
            Hide();
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
