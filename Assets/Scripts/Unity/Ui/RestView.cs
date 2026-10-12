using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 休息事件场景的<b>运行时视图</b>（2026-10-09）。
    ///
    /// <para><b>⚠ 这个场景目前是占位</b>：用户口径原话「该事件对应的场景暂时还没有做好，
    /// 你先姑且用个黑屏 + 按钮的方式替代一下」。所以本类里<b>没有</b>任何换图 / 尺寸逻辑
    /// （宝箱那边要按 sprite 宽高比现算矩形，这里一个 sprite 都不换）——
    /// 它只做三件事：① 把「点休息 / 点离开」两下点击冒泡成事件；
    /// ② 在「还能点 / 已经休息过」之间切按钮的可点与文案；③ 换那行引导文字。</para>
    ///
    /// <para><b>职责边界</b>（与 <see cref="TreasureView"/> / <see cref="ShopView"/> 同口径）：
    /// <b>它不认识地图、不认识生命账本、不认识存档</b> ——
    /// 「休息恢复多少生命、记在谁的账上」是上层（<c>RestSceneEntry</c>）的事。</para>
    ///
    /// <para><b>⚠ 颜色全部写在 prefab 上</b>（用户 2026-09-19 的红线），本类不写色 ——
    /// 运行期只切 <c>active</c> / <c>interactable</c> 与文字。</para>
    ///
    /// <para><b>⚠ 监听器一律在 <c>Awake</c> 里按 SerializeField 自己接</b>（铁律 8）：
    /// 构建器在编辑模式里 <c>AddListener</c> 的那一份不是序列化数据，存 Prefab 时蒸发 ——
    /// 症状是「一切看着正常、<c>onClick</c> 上 0 个监听器、点了毫无反应且零报错」。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RestView : MonoBehaviour
    {
        [Header("「休息」大键（本场景唯一要做事的按钮）")]
        [Tooltip("按钮本体就是那块色块（Image 同时是 targetGraphic 与命中区）。")]
        [SerializeField] private Button _restButton;

        [SerializeField] private TMP_Text _restLabel;

        [Header("标题 / 引导行 / 离开")]
        [SerializeField] private TMP_Text _title;

        [SerializeField] private GameObject _hintRoot;

        [SerializeField] private TMP_Text _hint;

        [SerializeField] private Button _leaveButton;

        [SerializeField] private TMP_Text _leaveLabel;

        /// <summary>玩家点了「休息」。上层拿它去回血。</summary>
        public event Action RestClicked;

        /// <summary>玩家点了「离开」。上层拿它去结束这个事件节点。</summary>
        public event Action LeaveClicked;

        /// <summary>这一趟已经休息过了（再点不重复回血）。</summary>
        public bool IsRested { get; private set; }

        private void Awake()
        {
            Wire(_restButton, OnRest);
            Wire(_leaveButton, OnLeave);

            IsRested = false;
        }

        private void OnDestroy()
        {
            Unwire(_restButton, OnRest);
            Unwire(_leaveButton, OnLeave);
        }

        // ══════════════════════════════════════════════════════
        //  画
        // ══════════════════════════════════════════════════════

        /// <summary>设置场景标题（默认「休息」）。</summary>
        public void SetTitle(string text)
        {
            if (_title != null)
            {
                _title.text = text;
            }
        }

        /// <summary>设置大键下方那行引导文字。</summary>
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

        /// <summary>大键上的文字（默认「休息」；休息过后由上层改成「已休息」）。</summary>
        public void SetRestLabel(string text)
        {
            if (_restLabel != null)
            {
                _restLabel.text = text;
            }
        }

        /// <summary>大键还能不能点。休息过之后恒为 <c>false</c>。</summary>
        public void SetRestInteractable(bool interactable)
        {
            if (_restButton != null)
            {
                _restButton.interactable = interactable;
            }
        }

        /// <summary>
        /// 把大键切到「已经休息过」：关掉可点并记下状态。<b>幂等</b> ——
        /// 重复调只是再关一次。文案不在这里（由上层 <c>SetRestLabel</c> 给）。
        /// </summary>
        public void SetRestDone()
        {
            IsRested = true;
            SetRestInteractable(false);
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private void OnRest()
        {
            Action handler = RestClicked;
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
