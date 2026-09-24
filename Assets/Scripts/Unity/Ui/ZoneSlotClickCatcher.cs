using UnityEngine;
using UnityEngine.EventSystems;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「点了这一格冷却槽」的接收器（M26）。由 <see cref="CooldownView"/> 在
    /// <c>Awake</c> 里给 8 个槽位节点各挂一个，一个管一格。
    ///
    /// <para><b>为什么用 <c>IPointerClickHandler</c> 而不是 <c>Button</c></b>：
    /// 槽位节点上已经有一个滚动视口在吃拖拽（滚轮 / 拖动滚动），再挂一个
    /// <c>Button</c> 会与它抢事件、还会因为 <c>Selectable</c> 的颜色替换把槽图
    /// 的高亮色冲掉（<c>Selectable</c> 的色是「替换」不是「乘算」）。
    /// 直接实现指针接口则只拿「点了一下」这件事，别的照旧。</para>
    ///
    /// <para><b>为什么不会抢走卡片的点击</b>：迷你卡是槽位容器的子节点，EventSystem 先给
    /// 层级最深的目标派发事件，卡片自己的 <c>Button</c> 在冒泡链的更前面 ——
    /// 点到卡上时它先处理（并转成同一件事，见 <c>CooldownView.OnCardClicked</c>）；
    /// 只有点在槽图本身（牌外）才会落到本类。</para>
    ///
    /// <para><b>⚠⚠ 为什么必须单独一个文件（2026-09-23 补，血的教训）</b>：
    /// 它原先和 <see cref="CooldownView"/> 同写在 <c>CooldownView.cs</c> 里。
    /// Unity 的 <c>MonoScript</c> ↔ 类对应关系靠<b>文件名</b> —— 同一个 .cs 里的第二个
    /// MonoBehaviour 没有自己的 <c>MonoScript</c> 资产，于是它一旦被写进 Prefab，
    /// <c>m_Script</c> 就变成 <c>{fileID: 0}</c>。后果比「少一个组件」严重得多：
    /// <b>Unity 从此拒绝保存这份 Prefab</b>（<i>You are trying to save a Prefab with a
    /// missing script. This is not allowed.</i>），所有构建菜单的产物都落不了盘，
    /// 而且报错只出现在 Console 里、构建流程本身照常往下跑。</para>
    ///
    /// <para>它就是怎么进 Prefab 的：<c>CooldownView.Awake</c> 在运行时
    /// <c>AddComponent</c>，只要有人在 <b>Play 模式下</b>把场景实例写回 Prefab
    /// （构建器存盘 / 手工 Apply），这 8 个组件就带着空脚本引用进了 Prefab。
    /// 本批实测到的正是 <c>SlotL_CD1…4</c> + <c>SlotR_CD1…4</c> 共 8 处。
    /// 同一类隐患：<c>PeekCardSlot</c>（M25）、本类的旧名 <c>ZoneClickCatcher</c>。
    /// <b>规矩：任何要挂到节点上的组件，一律新开一个同名文件。</b></para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZoneSlotClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>本槽代表哪一方。</summary>
        public int Seat = -1;

        /// <summary>本槽代表哪一行（0 = 冷却区4 … 3 = 冷却区1）。</summary>
        public int RowIndex = -1;

        /// <summary>回报给谁。</summary>
        public CooldownView Owner;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner == null || Seat < 0 || RowIndex < 0)
            {
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Owner.NotifyZoneRowClicked(Seat, RowIndex);
        }
    }
}
