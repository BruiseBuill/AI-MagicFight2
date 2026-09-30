using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 强化场景的<b>运行时视图</b>（2026-09-30 · P5）。
    ///
    /// <para><b>职责边界</b>（与 <see cref="ShopView"/> 同口径）：本类只做三件事 ——
    /// ① 把三个可点的东西（石台 / 离开 / 弹窗）的点击冒泡成事件；
    /// ② 转发「打开选牌弹窗」「播强化动画」两个动作；③ 管一行引导文字的显隐。
    /// <b>它不认识卡表、不认识强化规则、不认识存档</b> ——
    /// 「哪些牌能强化、强化完变成什么」是上层（<c>UpgradeSceneEntry</c> → 将来是冒险流程桥）的事。</para>
    ///
    /// <para><b>卡面复用唯一那份 <c>CardView_Hand.prefab</c></b>（铁律 9）：
    /// 弹窗里的卡与动画里的卡都是 <see cref="CardView"/>，尺寸由 <c>SetFaceWidth</c> 给。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeView : MonoBehaviour
    {
        /// <summary>石台命中区（一整块透明的矩形，**唯一**吃台面点击的东西）。</summary>
        [SerializeField] private Button _tableButton;

        [SerializeField] private Button _leaveButton;
        [SerializeField] private TMP_Text _leaveLabel;
        [SerializeField] private TMP_Text _title;

        /// <summary>台面下方那行引导文字（弹窗一打开就藏起来）。</summary>
        [SerializeField] private GameObject _hint;

        [SerializeField] private TMP_Text _hintText;

        /// <summary>
        /// 选牌弹窗。<b>它自己负责开关</b>（遮罩 / 关闭都接到它的 Hide），
        /// 本类只做两件事：把「打开」转过去 + 把它的两个事件再冒一层。
        /// </summary>
        [SerializeField] private UpgradePickerView _picker;

        /// <summary>强化演出（默认失活，播完自己关）。</summary>
        [SerializeField] private UpgradeFxView _fx;

        /// <summary>玩家点了石台。上层拿它去准备一份卡池再调 <see cref="OpenPicker"/>。</summary>
        public event Action TableClicked;

        /// <summary>玩家点了「离开」。上层拿它去结束这个事件节点。</summary>
        public event Action LeaveClicked;

        /// <summary>玩家在弹窗里按了「确认」，参数 = 被选中的那张牌。</summary>
        public event Action<CardDef> UpgradeConfirmed;

        /// <summary>弹窗关掉了（确认或取消都会发）。上层拿它决定要不要把引导文字放回来。</summary>
        public event Action PickerClosed;

        /// <summary>强化动画播完了。上层拿它去结束这个事件节点。</summary>
        public event Action FxFinished;

        /// <summary>选牌弹窗是不是开着。</summary>
        public bool IsPickerOpen
        {
            get { return _picker != null && _picker.IsOpen; }
        }

        /// <summary>强化动画是不是在播。</summary>
        public bool IsFxPlaying
        {
            get { return _fx != null && _fx.IsPlaying; }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按序列化字段自己接（铁律 8）：
            //   构建器在编辑模式里 AddListener 的那一份不是序列化数据，存 Prefab / 场景时蒸发。
            //   症状是「一切看着正常、onClick 上 0 个监听器、点了毫无反应且零报错」。
            Wire(_tableButton, OnTable);
            Wire(_leaveButton, OnLeave);

            if (_picker != null)
            {
                _picker.Confirmed += OnPickerConfirmed;
                _picker.Closed += OnPickerClosed;
            }
            else
            {
                Debug.LogError("[UpgradeView] 没有接 UpgradePickerView —— 先跑 `魔法乱斗/P5 · 构建 Upgrade 场景`。");
            }

            if (_fx == null)
            {
                Debug.LogError("[UpgradeView] 没有接 UpgradeFxView —— 先跑 `魔法乱斗/P5 · 构建 Upgrade 场景`。");
            }
            else
            {
                _fx.Finished += OnFxFinished;
            }
        }

        private void OnDestroy()
        {
            if (_picker != null)
            {
                _picker.Confirmed -= OnPickerConfirmed;
                _picker.Closed -= OnPickerClosed;
            }

            if (_fx != null)
            {
                _fx.Finished -= OnFxFinished;
            }
        }

        // ══════════════════════════════════════════════════════
        //  画
        // ══════════════════════════════════════════════════════

        /// <summary>设置场景标题（默认「强化卡牌」）。</summary>
        public void SetTitle(string text)
        {
            if (_title != null)
            {
                _title.text = text;
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

        /// <summary>设置引导文字内容。</summary>
        public void SetHint(string text)
        {
            if (_hintText != null)
            {
                _hintText.text = text;
            }
        }

        /// <summary>
        /// 引导文字的显隐。
        ///
        /// <para>口径：<b>弹窗开着时不显示</b>（面板已经说明了一切），
        /// 关掉之后由上层决定要不要放回来 —— 强化完要离开节点时就不该再放回来，
        /// 否则会出现「已经结束了、屏幕上还写着『点击台面强化』」。</para>
        /// </summary>
        public void SetHintVisible(bool visible)
        {
            if (_hint != null && _hint.activeSelf != visible)
            {
                _hint.SetActive(visible);
            }
        }

        /// <summary>打开选牌弹窗（同时把引导文字收起来）。</summary>
        public void OpenPicker(IList<UpgradePickerView.Entry> entries, string title)
        {
            if (_picker == null)
            {
                return;
            }

            SetHintVisible(false);
            _picker.Show(entries, title);
        }

        /// <summary>关掉选牌弹窗（幂等）。</summary>
        public void ClosePicker()
        {
            if (_picker != null)
            {
                _picker.Hide();
            }
        }

        /// <summary>
        /// 石台还能不能点。
        ///
        /// <para>本场景的口径是「<b>一次只强化一张，确认完这个节点就结束</b>」，
        /// 所以播完动画要把台面关掉 —— 否则玩家还能再点开一次弹窗，
        /// 对着一个已经结束的节点继续强化（流程上说不通，而且零报错）。</para>
        /// </summary>
        public void SetTableInteractable(bool interactable)
        {
            if (_tableButton != null)
            {
                _tableButton.interactable = interactable;
            }
        }

        /// <summary>播一段强化演出（<paramref name="before"/> → <paramref name="after"/>）。</summary>
        public void PlayUpgradeFx(CardDef before, CardDef after)
        {
            if (_fx == null)
            {
                return;
            }

            SetHintVisible(false);
            _fx.Play(before, after);
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private void OnTable()
        {
            TableClicked?.Invoke();
        }

        private void OnLeave()
        {
            LeaveClicked?.Invoke();
        }

        private void OnPickerConfirmed(CardDef card)
        {
            UpgradeConfirmed?.Invoke(card);
        }

        private void OnPickerClosed(int selectedIndex)
        {
            PickerClosed?.Invoke();
        }

        private void OnFxFinished()
        {
            FxFinished?.Invoke();
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
