using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>一张牌上可能发生的手势。</summary>
    public enum CardGesture
    {
        HoverEnter = 0,
        HoverExit = 1,

        /// <summary>按下（还没到长按、也还没开始拖）。</summary>
        Press = 2,

        /// <summary>抬起。</summary>
        Release = 3,

        /// <summary>按住不动超过 <see cref="UiLayout.LongPressSeconds"/>。</summary>
        LongPress = 4,

        DragBegin = 5,
        Drag = 6,
        DragEnd = 7,
    }

    /// <summary>接手势的一方（<see cref="HandView"/> / <see cref="CooldownView"/> 都实现它）。</summary>
    public interface ICardGestureHost
    {
        /// <param name="screenPos">指针屏幕坐标（Overlay 画布下等价于 Canvas 坐标 × CanvasScaler）。</param>
        void OnCardGesture(CardView card, CardGesture gesture, Vector2 screenPos);
    }

    /// <summary>
    /// 一张牌上的<strong>全部指针手势</strong>（M12）：悬停 / 按下 / 长按 / 拖拽。
    ///
    /// <para><b>为什么从原来那个 CardHoverProxy 扩过来</b>：M8 只需要「鼠标进来 / 出去」，
    /// M12 要拖拽和长按 —— 这三件事抢的是同一串指针事件，拆成两个组件会出现
    /// 「按下被 A 吃掉、移动被 B 吃掉」的诡异现象。</para>
    ///
    /// <para><b>怎么让拖拽不误触「点一下就出牌」</b>：<c>Button</c> 的 onClick 是在
    /// 指针抬起时、由 EventSystem 在 <c>OnPointerUp</c> <b>之后</b>、<c>OnEndDrag</c> <b>之前</b>派发的。
    /// 所以本类在「拖拽开始」和「长按已经触发」这两种情况下调
    /// <see cref="CardView.SetGestureHold"/> 把 Button 停用掉，<c>Press()</c> 里的
    /// <c>IsActive()</c> 判定就会把这次点击吃掉；下一帧 LateUpdate 再恢复
    /// （LateUpdate 一定晚于 EventSystem 的 Update）。</para>
    ///
    /// <para>长按计时放在本组件的 <c>Update</c> 里，不用协程 —— 卡被池化回收时
    /// <c>OnDisable</c> 会把状态清干净，协程还得额外管生命周期。</para>
    ///
    /// <para><b>⚠ 不可拖的牌（<c>Draggable = false</c>，冷却区迷你卡）要把拖拽转交出去</b>：
    /// 本类实现了拖拽接口就一定会被 EventSystem 选中，什么都不做等于把「滑冷却区」
    /// 整片吞掉 —— 见 <see cref="ForwardDragToScroll"/>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardInteractor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>手势的接收方。由创建者赋值（接口字段不能被 Unity 序列化，这是有意的）。</summary>
        public ICardGestureHost Host;

        /// <summary>是哪一张牌。</summary>
        public CardView Card;

        /// <summary>能不能拖。只有手牌为真 —— 冷却区的牌不做拖拽（那里是「点选目标」的语义）。</summary>
        public bool Draggable;

        /// <summary>
        /// 本牌<strong>不可拖</strong>时，拖拽要转交给谁（缓存）。
        ///
        /// <para>见 <see cref="ForwardDragToScroll"/>：冷却区的迷你卡挂在
        /// <c>CoolingScroll_Player / CoolingScroll_Enemy</c> 的 <c>ScrollRect</c> 下，
        /// 那才是「滑冷却区」这件事该由谁处理。</para>
        /// </summary>
        private ScrollRect _scrollHost;

        private bool _pressed;
        private bool _dragging;
        private bool _longFired;
        private float _pressTime;
        private Vector2 _pressPos;

        /// <summary>本帧结束后要把 Button 的临时停用撤掉（见类注释里的时序说明）。</summary>
        private bool _releaseHoldAtLateUpdate;

        private void OnDisable()
        {
            // 池化回收：卡被 SetActive(false) 时状态必须清零，
            // 否则下一局复用这张卡时会带着「正被按着」的幽灵状态。
            ResetState();
        }

        public void ResetState()
        {
            if (_releaseHoldAtLateUpdate && Card != null)
            {
                Card.SetGestureHold(false);
            }

            _pressed = false;
            _dragging = false;
            _longFired = false;
            _releaseHoldAtLateUpdate = false;
        }

        private void Update()
        {
            if (!_pressed || _dragging || _longFired)
            {
                return;
            }

            if (Time.unscaledTime - _pressTime < UiLayout.LongPressSeconds)
            {
                return;
            }

            _longFired = true;
            Raise(CardGesture.LongPress, _pressPos);
        }

        private void LateUpdate()
        {
            if (!_releaseHoldAtLateUpdate)
            {
                return;
            }

            _releaseHoldAtLateUpdate = false;

            if (Card != null)
            {
                Card.SetGestureHold(false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  指针事件
        // ══════════════════════════════════════════════════════

        public void OnPointerEnter(PointerEventData eventData)
        {
            Raise(CardGesture.HoverEnter, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 「挪开就收起」这条要求落在这里：长按弹着卡面时指针一移出这张牌就收。
            // 接收方（HandView / CooldownView）对 HoverExit 的处理本来就是
            // 「收浮层 + 取消上浮」，所以不需要为长按单开一条分支。
            // 拖拽中指针基本不会真的离开这张牌（卡跟着指针走），真要离开也由 DragEnd 收尾。
            _pressed = false;
            _longFired = false;
            Raise(CardGesture.HoverExit, eventData.position);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _longFired = false;
            _pressTime = Time.unscaledTime;
            _pressPos = eventData.position;
            Raise(CardGesture.Press, eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            // 长按已经弹过卡面 → 这次抬起只用来收浮层，不能再当成「点了这张牌」。
            if (_longFired && Card != null)
            {
                Card.SetGestureHold(true);
                _releaseHoldAtLateUpdate = true;
            }

            _pressed = false;
            _longFired = false;
            Raise(CardGesture.Release, eventData.position);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!Draggable)
            {
                // 这一拖不是「出牌」，是「滑冷却区」—— 按住的那张卡不能再在途中弹长按看牌。
                _pressed = false;
                _longFired = false;

                ForwardDragToScroll(DragPhase.Begin, eventData);
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            _dragging = true;
            _longFired = false;
            _pressed = false;

            if (Card != null)
            {
                Card.SetGestureHold(true);
            }

            Raise(CardGesture.DragBegin, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!Draggable)
            {
                ForwardDragToScroll(DragPhase.Move, eventData);
                return;
            }

            if (!_dragging)
            {
                return;
            }

            Raise(CardGesture.Drag, eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!Draggable)
            {
                ForwardDragToScroll(DragPhase.End, eventData);
                return;
            }

            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Raise(CardGesture.DragEnd, eventData.position);

            // 恢复 Button —— 但落点判定已经在这一刻做完了，恢复不会补发一次点击：
            // 抬起事件早在本回调之前就派发过，而那时 Button 还是停用的。
            if (Card != null)
            {
                Card.SetGestureHold(false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  不可拖的牌：拖拽转交给滚动层
        // ══════════════════════════════════════════════════════

        /// <summary>拖拽的三个阶段（转交时用）。</summary>
        private enum DragPhase
        {
            Begin = 0,
            Move = 1,
            End = 2,
        }

        /// <summary>
        /// 把拖拽原样转交给本牌所在的 <see cref="ScrollRect"/>（冷却区的纵向滚动）。
        ///
        /// <para><b>⚠⚠ 为什么非转不可（2026-09-25 用户口径：「常态下，滑动屏幕来滑动冷却区的
        /// 判定面积略微有点窄，经常非得要很靠边才能滑动成功」）</b>：</para>
        ///
        /// <para>EventSystem 找拖拽处理器的方式是「从射线命中的节点往上找<b>第一个</b>实现了
        /// <c>IDragHandler</c> 的节点」（<c>ExecuteEvents.GetEventHandler</c>）——
        /// 它看的是<b>类型有没有实现接口</b>，不看实现了以后做不做事。
        /// 本类为了手牌拖拽实现了 <c>IBeginDragHandler / IDragHandler / IEndDragHandler</c>，
        /// 于是冷却区的迷你卡（<c>Draggable = false</c>）虽然什么也不做，
        /// 却仍然<b>把拖拽整条吞掉</b>，事件永远上不到 <c>CoolingScroll_*</c> 的 ScrollRect。</para>
        ///
        /// <para>结果就是：只有点在槽位徽标那块空白（槽图 <c>raycastTarget = false</c>，
        /// 射线落到视口自己身上）才滑得动 —— 而一行 4 张迷你卡几乎铺满视口 674 宽，
        /// 玩家感受到的就是「得贴着最左边那条才滑得动」。</para>
        ///
        /// <para>这里把三个回调原样转发给 ScrollRect，等于把「判定面积」从
        /// <b>槽位徽标那一条</b>恢复到<b>整个冷却视口</b>。短点（没超过拖拽阈值）
        /// 不会走到这三个回调，所以「点选目标 / 长按看牌」完全不受影响。</para>
        /// </summary>
        private void ForwardDragToScroll(DragPhase phase, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (_scrollHost == null)
            {
                // 手牌不在任何滚动层里 → 取到 null，无需转交（手牌本来就 Draggable）。
                _scrollHost = GetComponentInParent<ScrollRect>();
            }

            if (_scrollHost == null)
            {
                return;
            }

            switch (phase)
            {
                case DragPhase.Begin:
                    _scrollHost.OnBeginDrag(eventData);
                    break;

                case DragPhase.Move:
                    _scrollHost.OnDrag(eventData);
                    break;

                default:
                    _scrollHost.OnEndDrag(eventData);
                    break;
            }
        }

        private void Raise(CardGesture gesture, Vector2 screenPos)
        {
            if (Host != null)
            {
                Host.OnCardGesture(Card, gesture, screenPos);
            }
        }

        /// <summary>
        /// 挂上交互组件（幂等）。两种牌形态共用同一套代码，差别只在 <paramref name="draggable"/>。
        /// </summary>
        public static CardInteractor Attach(GameObject go, ICardGestureHost host, CardView card, bool draggable)
        {
            if (go == null)
            {
                return null;
            }

            CardInteractor it = go.GetComponent<CardInteractor>();
            if (it == null)
            {
                it = go.AddComponent<CardInteractor>();
            }

            it.Host = host;
            it.Card = card;
            it.Draggable = draggable;

            // 池化复用的牌可能被搬到别的滚动层下 → 转交目标重新找一次。
            it._scrollHost = null;

            // 卡被池化回收时会 SetActive(false) → OnDisable → ResetState()，
            // 所以复用同一张卡时不用在这里额外清状态。
            return it;
        }
    }
}
