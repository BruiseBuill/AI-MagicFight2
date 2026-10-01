using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊的<b>主浮层</b>（2026-10-01 · P6）：点水晶球之后弹出来的那一层，
    /// 里面是「两个空位 + 状态行 + 确认 / 关闭」。
    ///
    /// <para><b>它长什么样</b>：一层铺满画布的遮罩 + 一块 <c>Peek_Panel</c> 面板。
    /// 面板版式<b>照抄商店背包 / 强化弹窗</b>（同一张底图、同一批 <c>UiLayout.ShopBag*</c>
    /// 坐标、同一个 <c>CardView_Hand.prefab</c>）—— 三个面板看起来是一套东西。</para>
    ///
    /// <para><b>面板里是什么</b>：标题 → 两个并排的空位（中间一个「→」）→ 一行状态 →
    /// 左下「关闭」/ 右下「确认」。</para>
    ///
    /// <para><b>职责边界</b>（与 <see cref="UpgradePickerView"/> / <see cref="ShopBagView"/> 同口径）：
    /// 本类<b>只画、只收点击</b>。<b>它不认识规则、不认识卡池</b> ——
    /// 「两个空位里放的是哪两张牌」「确认键该不该亮」「状态行写什么」全是上层给的
    /// （<c>WitchSceneEntry</c> → 将来是冒险流程桥）。</para>
    ///
    /// <para><b>⚠ 它自己负责关掉自己</b>：遮罩与「关闭」都接到 <see cref="Hide"/>，
    /// 关掉之后发一次 <see cref="Closed"/> 供上层记账；「确认」只发 <see cref="ConfirmClicked"/>、
    /// <b>不</b>自己关 —— 因为确认之后要不要关、什么时候关（等动画？）是上层的事。</para>
    ///
    /// <para><b>⚠ 面板那一层必须吃射线</b>（<c>raycastTarget = true</c>）：否则点面板内部的
    /// 空白会穿到遮罩上、把面板自己关掉 —— 伸手去点「确认」的途中面板先关了，而且零报错。</para>
    ///
    /// <para><b>⚠ 本类所在节点在构建器里就是失活的，且 <c>Awake</c> 里不许 Hide</b>：
    /// 首次 <c>SetActive(true)</c> 才触发 <c>Awake</c>，同一帧里的 Hide 会把刚显示的内容按灭，
    /// 而 <see cref="Show"/> 剩下的代码照跑 —— 症状是「本局第一次点水晶球什么都不弹、
    /// 第二次起正常」。这个坑在本工程已中招两次。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchLayerView : MonoBehaviour
    {
        /// <summary>点它关闭（铺满画布的半透明遮罩，同时挡住底下的水晶球与离开键）。</summary>
        [SerializeField] private Button _veilButton;

        /// <summary>「关闭」按钮（取消，什么都不做）。</summary>
        [SerializeField] private Button _closeButton;

        /// <summary>「确认」按钮。</summary>
        [SerializeField] private Button _confirmButton;

        /// <summary>「确认」键的底图（可用 / 不可用时切色调）。</summary>
        [SerializeField] private Image _confirmImage;

        [SerializeField] private TMP_Text _confirmLabel;

        [SerializeField] private TMP_Text _title;

        /// <summary>状态行 —— 中性提示与校验失败的原因都写这里。</summary>
        [SerializeField] private TMP_Text _status;

        /// <summary>两个空位中间那个「→」（纯装饰）。</summary>
        [SerializeField] private TMP_Text _arrow;

        /// <summary>左空位 = 献祭（会被消耗掉的那张）。</summary>
        [SerializeField] private WitchSlotView _slotSacrifice;

        /// <summary>右空位 = 强化目标。</summary>
        [SerializeField] private WitchSlotView _slotTarget;

        /// <summary>玩家点了某个空位，参数 = 它的序号（0 = 左 / 1 = 右）。</summary>
        public event Action<int> SlotClicked;

        /// <summary>玩家按了「确认」。</summary>
        public event Action ConfirmClicked;

        /// <summary>浮层被关掉了（遮罩或「关闭」）。</summary>
        public event Action Closed;

        /// <summary>浮层是不是开着。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>左空位现在放的是哪张牌（没选 = null）。</summary>
        public CardDef Sacrifice
        {
            get { return _slotSacrifice != null ? _slotSacrifice.Card : null; }
        }

        /// <summary>右空位现在放的是哪张牌（没选 = null）。</summary>
        public CardDef Target
        {
            get { return _slotTarget != null ? _slotTarget.Card : null; }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按序列化字段自己接（铁律 8）。
            Wire(_veilButton, OnClose);
            Wire(_closeButton, OnClose);
            Wire(_confirmButton, OnConfirm);

            if (_slotSacrifice != null)
            {
                _slotSacrifice.Configure(0, "献祭", true);
                _slotSacrifice.Clicked += OnSlotClicked;
            }
            else
            {
                Debug.LogError("[WitchLayerView] 没有接左边那个空位 —— 先跑 `魔法乱斗/P6 · 构建 WitchWorkshop 场景`。");
            }

            if (_slotTarget != null)
            {
                _slotTarget.Configure(1, "强化目标", false);
                _slotTarget.Clicked += OnSlotClicked;
            }
            else
            {
                Debug.LogError("[WitchLayerView] 没有接右边那个空位 —— 先跑 `魔法乱斗/P6 · 构建 WitchWorkshop 场景`。");
            }
        }

        private void OnDestroy()
        {
            if (_slotSacrifice != null)
            {
                _slotSacrifice.Clicked -= OnSlotClicked;
            }

            if (_slotTarget != null)
            {
                _slotTarget.Clicked -= OnSlotClicked;
            }
        }

        // ══════════════════════════════════════════════════════
        //  开关
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 打开浮层。<b>幂等</b>：反复调用都行（每次都会把两个空位清空、
        /// 状态行复位、确认键置灰）。
        /// </summary>
        public void Show(string title, string status)
        {
            if (_title != null)
            {
                _title.text = string.IsNullOrEmpty(title) ? "特殊强化" : title;
            }

            SetSlots(null, null);
            SetStatus(status, false);
            SetConfirmEnabled(false);
            SetInteractable(true);

            IsOpen = true;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);     // 这一下会触发 Awake（首次）
            }
        }

        /// <summary>关掉浮层。重复调用是幂等的（只有真正关掉那一次会发事件）。</summary>
        public void Hide()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        //  画
        // ══════════════════════════════════════════════════════

        /// <summary>把两个空位刷成这两张牌（null = 空着）。</summary>
        public void SetSlots(CardDef sacrifice, CardDef target)
        {
            if (_slotSacrifice != null)
            {
                _slotSacrifice.Bind(sacrifice);
            }

            if (_slotTarget != null)
            {
                _slotTarget.Bind(target);
            }
        }

        /// <summary>
        /// 状态行。<paramref name="warn"/> 为真时转成警示色 ——
        /// 「两个空位不能是同一张牌」这类**要玩家改操作**的话必须与普通提示区别开，
        /// 否则玩家会以为自己已经可以确认了。
        /// </summary>
        public void SetStatus(string text, bool warn)
        {
            if (_status == null)
            {
                return;
            }

            _status.text = text ?? string.Empty;
            _status.color = warn ? UiTheme.WitchStatusWarn : UiTheme.WitchStatusText;
        }

        /// <summary>
        /// 「确认」键的可用态。
        ///
        /// <para><b>⚠ 只切 <c>interactable</c> 与 <c>Image.color</c>，不切 active</b>：
        /// 切 active 会让按钮在版式里「消失」，玩家找不到确认键在哪。</para>
        /// </summary>
        public void SetConfirmEnabled(bool enabled)
        {
            if (_confirmButton != null)
            {
                _confirmButton.interactable = enabled;
            }

            if (_confirmImage != null)
            {
                _confirmImage.color = enabled ? UiTheme.WitchConfirmOn : UiTheme.WitchConfirmOff;
            }

            if (_confirmLabel != null)
            {
                _confirmLabel.color = enabled ? UiTheme.WitchConfirmOn : UiTheme.WitchConfirmOff;
            }
        }

        /// <summary>两个空位还能不能点（节点结束 / 正在播动画时关掉）。</summary>
        public void SetInteractable(bool value)
        {
            if (_slotSacrifice != null)
            {
                _slotSacrifice.SetInteractable(value);
            }

            if (_slotTarget != null)
            {
                _slotTarget.SetInteractable(value);
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private void OnSlotClicked(WitchSlotView slot)
        {
            SlotClicked?.Invoke(slot.Index);
        }

        private void OnConfirm()
        {
            ConfirmClicked?.Invoke();
        }

        private void OnClose()
        {
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
