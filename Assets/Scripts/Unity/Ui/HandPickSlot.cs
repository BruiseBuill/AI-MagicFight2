using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「手牌选择弹窗」里的一格<strong>已选卡框</strong>（M27）。
    ///
    /// <para>一块卡框 + 一张正面卡面。点它 = <b>取消选择这张</b>
    /// （用户口径 2026-09-21：「玩家点击界面中的卡时，视为取消选择该卡」）。</para>
    ///
    /// <para><b>2026-09-23 删掉左上角的序号角标</b>（用户：「移除掉 slot area 里的
    /// indexBadge，不需要序号」）。选牌的顺序靠卡框从左到右的排布就能读出来，
    /// 不必再压一枚数字 —— 序号本身仍然由 <see cref="OptionIndex"/> 回传给引擎，
    /// 那与「显示不显示」是两件事。</para>
    ///
    /// <para><b>为什么自己实现 <see cref="IPointerClickHandler"/> 而不用 <c>Button</c></b>：
    /// 与 <see cref="PeekCardSlot"/> 同一套理由 —— ① 省掉 <c>Selectable</c> 的
    /// 「ColorTint 是替换不是乘算」那套麻烦（卡面会被刷成灰）；② 取消选择后这一格
    /// 可能被复用给别的牌，直接关 <c>raycastTarget</c> 最干净。</para>
    ///
    /// <para><b>⚠ 为什么单独一个文件</b>：要进 Prefab 的 MonoBehaviour = 一个文件一个、
    /// 文件名与类名一致。Unity 的 <c>MonoScript</c> ↔ 类对应关系靠文件名，
    /// 同文件里的第二个 MonoBehaviour 存进 Prefab 时 <c>m_Script</c> 会被写成
    /// <c>{fileID: 0}</c>（M25 的 <c>PeekCardSlot</c> 正是这么炸过 8 个牌位、且零报错）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandPickSlot : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("卡面（已选牌的正脸）。")]
        [SerializeField] private Image _face;

        [Tooltip("卡框描边（选中态亮色）。")]
        [SerializeField] private Image _edge;

        /// <summary>被点了（= 取消选择）。参数是自己，上层好知道该取掉哪一格。</summary>
        public event Action<HandPickSlot> Clicked;

        /// <summary>这一格装着的手牌在这个弹窗内部列表里的下标；−1 = 空格。</summary>
        public int SlotIndex = -1;

        /// <summary>这一格对应的选项序号（<c>Option.Index</c>）。空格为 −1。</summary>
        public int OptionIndex = -1;

        /// <summary>这一格装的牌的 uid（用于对账「取走的是哪张」）；−1 = 空格。</summary>
        public int CardUid = -1;

        public Image Face
        {
            get { return _face; }
        }

        public Image Edge
        {
            get { return _edge; }
        }

        /// <summary>还能不能点（= 卡面吃不吃射线）。空格不可点。</summary>
        public bool Interactable
        {
            get { return _face != null && _face.raycastTarget; }
            set
            {
                if (_face != null)
                {
                    _face.raycastTarget = value;
                }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Clicked != null)
            {
                Clicked(this);
            }
        }
    }
}
