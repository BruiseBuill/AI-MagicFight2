namespace MagicBrawl.Core
{
    /// <summary>
    /// 卡牌的元素属性（2026-09-26 新增）。
    ///
    /// <para><b>它回答什么问题</b>：这张牌「看起来是什么系」的 ——
    /// 由卡名 + 卡面美术的视觉主题决定（用户口径：按美术视觉主题归类）。
    /// 目前它<b>不参与任何规则结算</b>，只有两个消费者：</para>
    /// <list type="number">
    /// <item>「点击怪物 → 思考框」把怪物下一张会打出的牌翻译成<b>元素符号</b>
    /// （用户要求：只暴露元素、不暴露是哪一张）；</item>
    /// <item>以后做卡面角标 / 筛选时复用。</item>
    /// </list>
    ///
    /// <para><b>为什么放在 Core 而不是表现层</b>：元素是**卡牌的静态属性**
    /// （和力量 / 冷却 / 效果一样写在卡表里），不是画面上的东西 ——
    /// 表现层如果要自己维护一张「卡 ID → 元素」的映射表，就变成了第二份卡表，
    /// 迟早与 <see cref="CardLibrary"/> 对不上（铁律 1 的同一条精神）。</para>
    ///
    /// <para><b>顺序与美术一一对应</b>：数值 1–7 与
    /// <c>Assets/Art/Icons/Elements/Element_01_Ice.png</c> … <c>Element_07_Curse.png</c>
    /// 的编号完全一致（切图脚本 <c>Tools/art-audit/slice_element_icons.py</c>），
    /// 所以改这里的顺序等于改美术资源的语义，别乱动。</para>
    ///
    /// <para>⚠ <b>None = 未标注</b>，只应该出现在「以后新增、还没归类」的卡上。
    /// 表现层拿到 <see cref="None"/> 一律不显示符号（不要兜底成某个元素）。</para>
    /// </summary>
    public enum CardElement
    {
        /// <summary>未标注（表现层不显示符号）。</summary>
        None = 0,

        /// <summary>冰。</summary>
        Ice = 1,

        /// <summary>水。</summary>
        Water = 2,

        /// <summary>电。</summary>
        Electric = 3,

        /// <summary>火。</summary>
        Fire = 4,

        /// <summary>草。</summary>
        Grass = 5,

        /// <summary>石。</summary>
        Stone = 6,

        /// <summary>诅咒。⚠ 牌堆里目前还没有这一系的卡，符号先备着。</summary>
        Curse = 7,
    }

    /// <summary>
    /// <see cref="CardElement"/> 的文案 / 资源名映射。
    ///
    /// <para>放在 Core 里（而不是在表现层各写一份）：<b>字符串常量是资源契约</b> ——
    /// 表现层拼 `Element_01_Ice` 这样的资源名时用它，就不会出现「一处写 Ice、
    /// 另一处写 Frost」这种静默查不到图的情况。</para>
    /// </summary>
    public static class CardElementInfo
    {
        /// <summary>元素总数（不含 <see cref="CardElement.None"/>）。</summary>
        public const int Count = 7;

        /// <summary>中文名（「冰」「水」…），供提示文案用。</summary>
        public static string DisplayName(CardElement element)
        {
            switch (element)
            {
                case CardElement.Ice: return "冰";
                case CardElement.Water: return "水";
                case CardElement.Electric: return "电";
                case CardElement.Fire: return "火";
                case CardElement.Grass: return "草";
                case CardElement.Stone: return "石";
                case CardElement.Curse: return "诅咒";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 英文资源标识（与美术文件名中的那一段一致，如 <c>Ice</c> / <c>Electric</c>）。
        /// 资源名 = <c>Element_" + (int)element 的两位序号 + "_" + ResourceId(element)</c>。
        /// </summary>
        public static string ResourceId(CardElement element)
        {
            switch (element)
            {
                case CardElement.Ice: return "Ice";
                case CardElement.Water: return "Water";
                case CardElement.Electric: return "Electric";
                case CardElement.Fire: return "Fire";
                case CardElement.Grass: return "Grass";
                case CardElement.Stone: return "Stone";
                case CardElement.Curse: return "Curse";
                default: return string.Empty;
            }
        }
    }
}
