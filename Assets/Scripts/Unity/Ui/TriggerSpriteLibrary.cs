using MagicBrawl.Core;
using TMPro;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// α / β / γ 三个触发符号的**图文混排入口**。
    ///
    /// <para><b>它解决什么</b>：卡面的效果栏文字必须由 TMP 现场渲染（手牌缩到 196 px 后
    /// 烘焙进图里的字只有 8 px，不可读），而效果文案的**规则原文**是带 α / β / γ 字符的
    /// （<see cref="CardDef.EffectTextWithSymbols"/>）。中文正文字体里这几个希腊字母要么缺字形、
    /// 要么排出来跟卡面设计对不上 —— 卡面上那是**剑 / 盾 / 感叹号三张图标**。</para>
    ///
    /// <para>做法 = TMP 的 inline sprite（俗称图文混排）：把文本里的 α 换成
    /// <c>&lt;sprite name="attack"&gt;</c>，TMP 在排版时按字体大小把这个图标插进同一行。
    /// 图标来自 <see cref="TriggerIconLibrary.IconDir"/> 那三张图合成的一张
    /// <see cref="TMP_SpriteAsset"/>（图集见 `Tools/art-audit/build_tmp_icon_sheet.py`，
    /// 资产由菜单 `魔法乱斗/整理 · 建触发符号 SpriteAsset` 生成）。</para>
    ///
    /// <para><b>为什么要「查得到才换」</b>：SpriteAsset 是 Resources 里的资产，
    /// 没跑过生成菜单的干净检出里不存在。这时必须退化成原字符（正文的 fallback 字体
    /// <c>Google-Regular</c> 有 α β γ 字形），而不是让整段文字变成一堆空方块。</para>
    /// </summary>
    public static class TriggerSpriteLibrary
    {
        /// <summary>约定的 Resources 路径（不含扩展名）。</summary>
        public const string ResourcePath = "TriggerIconSprite";

        // ⚠ 这三个名字必须与 `Editor/TriggerSpriteAssetBuilder.cs` 写入的
        //    TMP_SpriteCharacter.name 完全一致，否则 <sprite name="..."> 查不到。
        /// <summary>α 剑。</summary>
        public const string SpriteAttack = "attack";

        /// <summary>β 盾。</summary>
        public const string SpriteDefend = "defend";

        /// <summary>γ 感叹号。</summary>
        public const string SpriteSpecial = "special";

        private static TMP_SpriteAsset _instance;
        private static bool _probed;

        /// <summary>取图集资产。不存在返回 null（调用方需降级成原字符）。</summary>
        public static TMP_SpriteAsset Instance
        {
            get
            {
                if (!_probed)
                {
                    _probed = true;
                    _instance = Resources.Load<TMP_SpriteAsset>(ResourcePath);
                }

                return _instance;
            }
        }

        /// <summary>某个时机对应的 sprite 名。Passive 没有图标，返回 null。</summary>
        public static string SpriteName(EffectTrigger trigger)
        {
            switch (trigger)
            {
                case EffectTrigger.Attack:
                    return SpriteAttack;
                case EffectTrigger.Defend:
                    return SpriteDefend;
                case EffectTrigger.Special:
                    return SpriteSpecial;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 单个符号的富文本标签。图集没生成时退化成符号字符本身
        /// （不要返回空串 —— 那会让「α 效果」看起来像没有时机的普通说明）。
        /// </summary>
        public static string Tag(EffectTrigger trigger)
        {
            string name = SpriteName(trigger);
            if (name == null)
            {
                return string.Empty;
            }

            if (Instance == null)
            {
                return TriggerSymbol.Of(trigger);
            }

            return "<sprite name=\"" + name + "\">";
        }

        /// <summary>
        /// 把一段文案里的 α / β / γ **字符**换成对应的 sprite 标签。
        ///
        /// <para>逐字符扫描而不是 <c>string.Replace</c>：Replace 会连
        /// 已经写好的 <c>&lt;sprite&gt;</c> 标签里的内容一起看，
        /// 而且三处替换要新建三个中间串。这里一次遍历就够，也不怕重复调用
        /// （标签里没有裸的 α，所以幂等）。</para>
        /// </summary>
        public static string ToRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // 没有图集就不用做任何事，直接返回原文（少一次分配）
            if (Instance == null)
            {
                return text;
            }

            var sb = new System.Text.StringBuilder(text.Length + 32);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == 'α')
                {
                    sb.Append(Tag(EffectTrigger.Attack));
                }
                else if (c == 'β')
                {
                    sb.Append(Tag(EffectTrigger.Defend));
                }
                else if (c == 'γ')
                {
                    sb.Append(Tag(EffectTrigger.Special));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>卡面口径的效果栏文案（已换成 sprite 标签）。</summary>
        public static string EffectRichText(CardDef def)
        {
            return def == null ? string.Empty : ToRichText(def.EffectTextWithSymbols);
        }

        /// <summary>
        /// 把 TMP 组件接上图文混排：挂 sprite 资产 + 关掉「只认字体」的解析。
        /// 图集缺失时不挂（TMP 会照常渲染原字符，不会报错）。
        /// </summary>
        public static void Attach(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            TMP_SpriteAsset asset = Instance;
            if (asset == null)
            {
                return;
            }

            text.spriteAsset = asset;
            text.richText = true;
        }
    }
}
