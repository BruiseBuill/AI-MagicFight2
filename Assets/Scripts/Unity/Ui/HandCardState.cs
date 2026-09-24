using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 手牌上每张牌自己的排布状态（跟着卡实例走，池化复用时不会丢）。
    ///
    /// <para><b>为什么单独一个文件</b>：Unity 只认「文件名 == 类名」的 MonoBehaviour ——
    /// 它原本挤在 <c>HandView.cs</c> 里，运行时 <c>AddComponent</c> 能用，
    /// 但<b>存进 Prefab 时会被丢掉</b>（序列化不了脚本引用）。
    /// 而 prefab 要能看见这个组件，所以必须独立成文件。</para>
    ///
    /// <para>字段是公开的可写值，不是序列化配置 —— 它们只在运行时被
    /// <see cref="HandView"/> 每帧读改写，Prefab 上这几个值只是初值。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandCardState : MonoBehaviour
    {
        public float X;
        public float Y;
        public float Rot;
        public float Scale = 1f;

        /// <summary>刚取出来（或刚被重排）—— 这一帧直接落到目标位姿，不做平滑趋近。</summary>
        public bool First = true;
    }
}
