using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「按住不放」手势（M28）。挂在怪物右下角的「手牌 ×N」标签上：
    /// <b>按下</b> → <see cref="Pressed"/>，<b>松手（无论指针挪到哪儿）</b> → <see cref="Released"/>。
    ///
    /// <para><b>为什么要在松手时无条件收回</b>：用户口径是「松手后该界面消失」。
    /// Unity 的 <c>IPointerUpHandler</c> 只在「按下时命中的那个对象仍然接得住这次抬起」时
    /// 才会被调用 —— 玩家按下之后如果把指针甩到屏幕另一端再松开，这一格收不到
    /// <c>OnPointerUp</c>，面板就会一直挂在屏幕上。所以这里同时实现
    /// <c>IPointerExitHandler</c> <b>与</b>一个「按下期间每帧检查有没有松开」的兜底，
    /// 三处任意一处先到都算结束（<see cref="_held"/> 保证只发一次）。</para>
    ///
    /// <para><b>为什么不用 <c>Button</c></b>：<c>Selectable</c> 的 <c>ColorTint</c> 是
    /// <strong>替换</strong>而不是乘算，会把标签底色刷成灰；而且它也没有「按住 / 松开」两相
    /// 事件（<c>onClick</c> 只在抬起时才发一次）。</para>
    ///
    /// <para><b>为什么押在 <c>Update</c> 上做兜底检查</b>：<c>Input.GetMouseButton</c>
    /// 在触屏上同样有效（Unity 会把单指触摸映射成鼠标），而本工程是单机 2D、
    /// 只用左键。这样「手指滑出标签再松开」也能正确收起面板，不依赖 <c>OnPointerUp</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PressHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>按下（面板该弹出来了）。</summary>
        public event Action Pressed;

        /// <summary>松开 / 指针离开（面板该收起来了）。两相之间只会各发一次。</summary>
        public event Action Released;

        private bool _held;

        /// <summary>按下时是否要「指针一离开就收」——本处为真（按住看，手挪开就没了）。</summary>
        [Tooltip("勾上 = 指针一离开这一格就当作松手（按住型手势的常规语义）。")]
        [SerializeField] private bool _releaseOnExit = true;

        /// <summary>现在是不是按着。</summary>
        public bool IsHeld
        {
            get { return _held; }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // 只认左键 / 单指 —— 右键与中键在触屏上会变成奇怪的重影
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (_held)
            {
                return;
            }

            _held = true;

            if (Pressed != null)
            {
                Pressed();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Raise();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_releaseOnExit)
            {
                Raise();
            }
        }

        private void Update()
        {
            // 兜底：按下期间每帧确认左键还按着。
            // 玩家把指针甩出标签之外再松开时 OnPointerUp 不会送到这里（见类注释），
            // 少了这一道，面板会一直挂在屏幕上、只能靠重开一局清掉。
            if (_held && !Input.GetMouseButton(0))
            {
                Raise();
            }
        }

        private void Raise()
        {
            if (!_held)
            {
                return;
            }

            _held = false;

            if (Released != null)
            {
                Released();
            }
        }

        /// <summary>外部强制复位（重开一局 / 指针状态被别的东西打断时用）。</summary>
        public void ResetState()
        {
            _held = false;
        }

        private void OnDisable()
        {
            // 对象被关掉时不会再收到抬起事件 —— 直接当作松手，免得「按着标签时重开一局」
            // 把上层留在一个「以为还按着」的状态里。
            Raise();
        }
    }
}
