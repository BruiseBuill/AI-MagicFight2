using MagicBrawl.Core;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「把强化后的新卡<b>落盘成资产</b>」这条能力的<b>运行时插槽</b>（2026-09-30）。
    ///
    /// <para><b>为什么需要一层插槽</b>：写资产只能走 <c>AssetDatabase</c>，而它<b>只存在于编辑器</b>；
    /// 强化场景的入口（<c>UpgradeSceneEntry</c>）却必须留在运行时程序集里
    /// （它要挂进场景、要能在真机上跑）。所以：编辑器侧在
    /// <c>[InitializeOnLoadMethod]</c> 里把实现塞进 <see cref="Handler"/>，
    /// 运行时侧只认这个委托 —— 编辑器里跑就落盘，构建版里跑就退化成内存新定义。</para>
    ///
    /// <para><b>⚠ 这不是「两条实现」</b>：规则只有一条（<see cref="CardUpgrade.Apply"/>），
    /// 这里插的只是「要不要把结果写成资产文件」这一步。少了它，真机上会抛
    /// <c>AssetDatabase</c> 不可用的错；两边各写一套强度计算才是真正的分叉。</para>
    /// </summary>
    public static class CardUpgradeWriter
    {
        /// <summary>
        /// 落盘实现。<paramref name="upgraded"/> 是内存里那份新定义，
        /// <paramref name="source"/> 是被强化前的牌（卡面插画要从它身上继承）。
        /// 返回值 = 运行时应当真正使用的那份定义（落盘成功时来自资产，失败时就是入参）。
        /// </summary>
        public delegate CardDef PersistHandler(CardDef upgraded, CardDef source, out string note);

        /// <summary>编辑器注入的实现（构建版里恒为 null）。</summary>
        public static PersistHandler Handler { get; set; }

        /// <summary>当前是否处在「能落盘」的环境（编辑器）。</summary>
        public static bool Available
        {
            get { return Handler != null; }
        }

        /// <summary>
        /// 把新定义落盘（能落就落），并返回**运行时该用的那一份**。
        ///
        /// <para><paramref name="note"/> 是给日志用的一句人话：
        /// 编辑器里是「已落盘 <c>Assets/…/Card_a_Up.asset</c>」，
        /// 构建版里是「只在本局内存里生效」。</para>
        /// </summary>
        public static CardDef Persist(CardDef upgraded, CardDef source, out string note)
        {
            if (Handler == null)
            {
                note = "非编辑器运行：强化只在本局内存里生效，没有落盘成资产";
                return upgraded;
            }

            return Handler(upgraded, source, out note);
        }
    }
}
