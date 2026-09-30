using UnityEngine;
using UnityEngine.EventSystems;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「点了怪物」的接收器（2026-09-26）。由 <see cref="BattleUi"/> 挂在怪物的点击面节点上。
    ///
    /// <para><b>为什么单独一个文件</b>（铁律）：Unity 的 <c>MonoScript</c> ↔ 类对应关系靠
    /// <b>文件名</b> —— 同一个 .cs 里的第二个 <c>MonoBehaviour</c> 没有自己的
    /// <c>MonoScript</c> 资产，一旦被写进 Prefab，<c>m_Script</c> 就变成 <c>{fileID: 0}</c>，
    /// 而 Unity 会因此<b>拒绝保存整份 Prefab</b>（报错只在 Console 里、构建流程照常往下跑）。
    /// 详见 <see cref="ZoneSlotClickCatcher"/> 的说明 —— 那个坑已经踩过一次。</para>
    ///
    /// <para><b>为什么用 <c>IPointerClickHandler</c> 而不是 <c>Button</c></b>：
    /// 怪物身上没有任何 <c>Selectable</c>，加一个 <c>Button</c> 会带来不必要的
    /// 状态机（高亮 / 过渡色）与 <c>Selectable</c> 的颜色替换；而这里只需要「点了一下」。
    /// 与 <see cref="ZoneSlotClickCatcher"/> 同一套理由。</para>
    ///
    /// <para>⚠ <b>它挂在一个专门的透明点击面上</b>（<c>ArtLayer/Char_Monster_Click</c>），
    /// 不是挂在角色本体上 —— 角色本体每帧都在换帧、<c>sizeDelta</c> 随动作变，
    /// 挂上去会出现「有的动作点得到、有的动作点不到」的漂移。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>回报给谁。</summary>
        public BattleUi Owner;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner == null)
            {
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Owner.NotifyMonsterClicked();
        }
    }
}
