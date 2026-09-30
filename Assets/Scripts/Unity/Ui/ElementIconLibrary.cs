using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 七元素符号的取图入口（2026-09-26）。
    ///
    /// <para><b>它为什么单独一份、而不并进 <see cref="BattleArtLibrary"/></b>：
    /// <see cref="BattleArtLibrary"/> 的那批图是 M11「美术层」的资产，由
    /// `ArtImportBuilder` 扫 `Art/Ui`、`Art/Chars`、`Art/Backgrounds` 重建；
    /// 元素符号在 `Art/Icons/Elements` 下，是另一条目录、另一批用途
    /// （卡牌属性），混在一起会让「重跑美术导入」的副作用面变大。
    /// 单独一份表，谁改谁清楚。</para>
    ///
    /// <para><b>为什么用 <c>Resources.Load</c> 按名加载而不是烘一张 SO</b>：
    /// 元素图是<b>7 张固定名字的小图</b>（<c>Element_01_Ice</c> … <c>Element_07_Curse</c>），
    /// 名字由 <see cref="CardElementInfo.ResourceId"/> 唯一决定，没有「用户在 Inspector 里
    /// 拖一张别的图进来」这种需求。为了它再养一张 ScriptableObject + 一个构建器入口，
    /// 维护成本大于收益 —— 而且 <c>Resources</c> 加载天然带缓存，只取一次。
    /// </para>
    ///
    /// <para>⚠ <b>加载路径固定为 <c>Assets/Resources/Elements/</c></b>。
    /// 图还没导进去时返回 null，调用方应当<b>不显示符号</b>（不要兜底成别的图）。</para>
    /// </summary>
    public static class ElementIconLibrary
    {
        /// <summary>Resources 下的子目录（相对 <c>Assets/Resources</c>）。</summary>
        private const string ResourceFolder = "Elements";

        /// <summary>缓存：元素 → Sprite（null 也缓存，避免每次点击都去 Load 一次失败的路径）。</summary>
        private static readonly Dictionary<CardElement, Sprite> Cache = new Dictionary<CardElement, Sprite>();

        /// <summary>
        /// 取某个元素的符号图；无对应资源时返回 null。
        ///
        /// <para>文件名 = <c>Element_</c> + 两位序号 + <c>_</c> + 英文标识，
        /// 例如 <c>Element_01_Ice</c> —— 与切图脚本
        /// `Tools/art-audit/slice_element_icons.py` 的输出严格一致。</para>
        /// </summary>
        public static Sprite Get(CardElement element)
        {
            if (element == CardElement.None)
            {
                return null;
            }

            Sprite cached;
            if (Cache.TryGetValue(element, out cached))
            {
                return cached;
            }

            string name = "Element_" + ((int)element).ToString("D2") + "_" + CardElementInfo.ResourceId(element);
            Sprite sprite = Resources.Load<Sprite>(ResourceFolder + "/" + name);

            if (sprite == null)
            {
                // ⚠ 只在第一次查时告警一次（之后缓存了 null，走上面的分支直接返回）
                Debug.LogWarning("[Elements] 缺图：Assets/Resources/" + ResourceFolder + "/" + name
                                 + ".png —— 请先跑 Tools/art-audit/slice_element_icons.py 并把它拷进 Resources。");
            }

            Cache[element] = sprite;
            return sprite;
        }

        /// <summary>
        /// 元素的中文名（「冰」「水」…）；<see cref="CardElement.None"/> 返回空串。
        ///
        /// <para>直接转发到 <see cref="CardElementInfo.DisplayName"/> —— 文案的权威在 Core，
        /// 表现层不再抄一份。</para>
        /// </summary>
        public static string DisplayName(CardElement element)
        {
            return CardElementInfo.DisplayName(element);
        }
    }
}
