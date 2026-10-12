using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 宝箱事件场景的<b>运行时视图</b>（2026-10-09）。
    ///
    /// <para><b>职责边界</b>（与 <see cref="ShopView"/> / <see cref="UpgradeView"/> 同口径）：
    /// 本类只做三件事 —— ① 把「点箱子 / 点离开」两下点击冒泡成事件；
    /// ② 在「关着 / 开着」两张箱子图之间切换；③ 开箱后把 +N 那行金色读数显示出来。
    /// <b>它不认识地图、不认识金币账本、不认识存档</b> —— 「开箱给多少钱、记在谁的账上」
    /// 是上层（<c>TreasureSceneEntry</c> → 将来是冒险流程桥）的事。</para>
    ///
    /// <para><b>⚠ 颜色与底图全部写在 prefab 上</b>（用户 2026-09-19 的红线），本类不写色。</para>
    ///
    /// <para><b>⚠ 监听器一律在 <c>Awake</c> 里按 SerializeField 自己接</b>（铁律 8）：
    /// 构建器在编辑模式里 <c>AddListener</c> 的那一份不是序列化数据，存 Prefab 时蒸发 ——
    /// 症状是「一切看着正常、onClick 上 0 个监听器、点了毫无反应且零报错」。</para>
    ///
    /// <para><b>⚠ 换图必须同时换尺寸</b>：箱子的矩形是按<b>图片自身的宽高比</b>算出来的
    /// （见 <see cref="ApplyChestSize"/>）。开着的那张比关着的高 24%
    /// （411×333 → 835×830），只换 sprite 不换尺寸的话，箱子会横向压扁 —— 而且零报错。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreasureView : MonoBehaviour
    {
        [Header("箱子（本场景唯一可点的东西）")]
        [Tooltip("箱子本体就是那枚按钮（Image 同时是 targetGraphic 与命中区）。")]
        [SerializeField] private Button _chestButton;

        [SerializeField] private Image _chest;

        [Tooltip("「关着」的那张图（进场景时的样子）。")]
        [SerializeField] private Sprite _chestClosed;

        [Tooltip("「开着」的那张图（点开之后换上去）。")]
        [SerializeField] private Sprite _chestOpen;

        [Header("开箱后的 +N 读数（默认失活）")]
        [SerializeField] private GameObject _gainRoot;

        [SerializeField] private TMP_Text _gain;

        [Header("标题 / 引导行 / 离开")]
        [SerializeField] private TMP_Text _title;

        [SerializeField] private GameObject _hintRoot;

        [SerializeField] private TMP_Text _hint;

        [SerializeField] private Button _leaveButton;

        [SerializeField] private TMP_Text _leaveLabel;

        /// <summary>玩家点了箱子。上层拿它去记账。</summary>
        public event Action ChestClicked;

        /// <summary>玩家点了「离开」。上层拿它去结束这个事件节点。</summary>
        public event Action LeaveClicked;

        /// <summary>箱子开了没有（开了之后不再响应第二次点击）。</summary>
        public bool IsOpened { get; private set; }

        private void Awake()
        {
            Wire(_chestButton, OnChest);
            Wire(_leaveButton, OnLeave);

            IsOpened = false;
            if (_chest != null && _chestClosed != null)
            {
                _chest.sprite = _chestClosed;
            }

            ApplyChestSize();
            HideGain();
        }

        private void OnDestroy()
        {
            Unwire(_chestButton, OnChest);
            Unwire(_leaveButton, OnLeave);
        }

        // ══════════════════════════════════════════════════════
        //  画
        // ══════════════════════════════════════════════════════

        /// <summary>设置场景标题（默认「宝箱」）。</summary>
        public void SetTitle(string text)
        {
            if (_title != null)
            {
                _title.text = text;
            }
        }

        /// <summary>设置箱子下方那行引导文字。</summary>
        public void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text;
            }
        }

        /// <summary>引导行的显隐。</summary>
        public void SetHintVisible(bool visible)
        {
            if (_hintRoot != null && _hintRoot.activeSelf != visible)
            {
                _hintRoot.SetActive(visible);
            }
        }

        /// <summary>「离开」按钮的文字（默认「离开」）。</summary>
        public void SetLeaveLabel(string text)
        {
            if (_leaveLabel != null)
            {
                _leaveLabel.text = text;
            }
        }

        /// <summary>箱子还能不能点。开过之后恒为 <c>false</c>。</summary>
        public void SetChestInteractable(bool interactable)
        {
            if (_chestButton != null)
            {
                _chestButton.interactable = interactable;
            }
        }

        /// <summary>
        /// 把箱子换成「开着」的那张图并停掉它的点击。<b>幂等</b> ——
        /// 重复调只是再设一遍同一个 sprite。
        /// </summary>
        public void SetOpened()
        {
            IsOpened = true;

            if (_chest != null && _chestOpen != null)
            {
                _chest.sprite = _chestOpen;
            }

            ApplyChestSize();
            SetChestInteractable(false);
        }

        /// <summary>显示开箱读数（<paramref name="amount"/> &gt; 0 → 「+50」）。</summary>
        public void ShowGain(int amount)
        {
            if (_gain != null)
            {
                _gain.text = "+" + amount;
            }

            if (_gainRoot != null && !_gainRoot.activeSelf)
            {
                _gainRoot.SetActive(true);
            }
        }

        /// <summary>把开箱读数收起来（进场景 / 重置事件时用）。</summary>
        public void HideGain()
        {
            if (_gainRoot != null && _gainRoot.activeSelf)
            {
                _gainRoot.SetActive(false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 按当前 sprite 的宽高比重算箱子矩形。
        ///
        /// <para><b>宽度是固定的</b>（<see cref="UiLayout.TreasureChestWidth"/>）—— 两张图
        /// 描的是同一只箱子，宽度一致箱体才对得上、换图时才不会横跳；
        /// 高度随图长（开箱那张高 1.006，关箱那张 0.810）。</para>
        ///
        /// <para>⚠ <b>不用 <c>preserveAspect</c></b>：那会让 RectTransform 的尺寸与图的尺寸
        /// 各说各话（命中区按 RectTransform 算、画面按图算），而这里本来就要按图改尺寸。
        /// pivot 是 (0.5, 0)，所以尺寸变化只往上长，箱底不会离开地面。</para>
        /// </summary>
        private void ApplyChestSize()
        {
            if (_chest == null || _chest.sprite == null)
            {
                return;
            }

            float width = UiLayout.TreasureChestWidth;
            float nativeWidth = _chest.sprite.rect.width;
            if (nativeWidth <= 0f)
            {
                return;
            }

            float height = width * (_chest.sprite.rect.height / nativeWidth);
            _chest.rectTransform.sizeDelta = new Vector2(width, height);
        }

        private void OnChest()
        {
            Action handler = ChestClicked;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnLeave()
        {
            Action handler = LeaveClicked;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>把按钮接到回调（幂等：先删后加，避免重复接）。</summary>
        private void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void Unwire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }
    }
}
