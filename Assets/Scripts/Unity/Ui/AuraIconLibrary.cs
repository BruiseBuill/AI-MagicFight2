using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 光环徽章图库（v2，2026-10-31）：<see cref="AuraKind"/> → 一张**整图**。
    ///
    /// <para><b>为什么不是老那套「底版 + 符号」</b>：v1 的图标是拿
    /// <see cref="TriggerIconLibrary"/> 里的 α 剑 / β 盾 / γ 感叹号拼出来的，
    /// 而这三个符号在卡面上的语义是「这条效果什么时候结算」—— 同一个图形背两套含义，
    /// 且 7 种光环只有 3 个图形（`QuickRefill` 与 `AtkPower` 同画一把剑，
    /// `ImmuneHigh` / `ImmuneLow` / `DefPower` 同画一面盾）。
    /// 现在每种光环有自己的一枚六边形徽章，形状承载语义，颜色只做二次确认。</para>
    ///
    /// <para><b>为什么不建 ScriptableObject</b>：`TriggerIconLibrary` 是 SO，是因为它的映射由
    /// 菜单里的生成器从拼图切出来后写进资产；这 8 张是**脚本直接渲染**出来的
    /// （`Tools/art-audit/render_aura_icons.py`），文件名与枚举一一对应、永远不会变，
    /// 再挂一层资产只多一个会忘记同步的地方。放在 <c>Assets/Resources/AuraIcons/</c>
    /// 下按名取，够用。</para>
    ///
    /// <para><b>取不到怎么办</b>：返回 null，<see cref="AuraIconView"/> 会退回 v1 的
    /// 「底版 + 符号」画法 —— 素材没导入时 HUD 不能整排变空白。</para>
    /// </summary>
    public static class AuraIconLibrary
    {
        /// <summary>Resources 下的子目录（不含前导斜杠、含结尾斜杠）。</summary>
        public const string ResourceDir = "AuraIcons/";

        /// <summary>刻印那张 —— 它不是 <see cref="AuraKind"/> 的一员，单独放。</summary>
        public const string SigilFile = "Aura_08_sigil";

        private static readonly Dictionary<AuraKind, Sprite> Cache = new Dictionary<AuraKind, Sprite>();

        private static Sprite _sigil;
        private static bool _sigilLoaded;

        /// <summary>
        /// 取某一枚指示物的徽章图。<paramref name="sigil"/> 为真时给刻印那张
        /// （刻印的 <see cref="AuraKind"/> 通常是 <see cref="AuraKind.None"/>，
        /// 所以不能只按 kind 查表）。
        /// </summary>
        public static Sprite Get(AuraKind kind, bool sigil)
        {
            if (sigil)
            {
                if (!_sigilLoaded)
                {
                    _sigil = Resources.Load<Sprite>(ResourceDir + SigilFile);
                    _sigilLoaded = true;
                }

                return _sigil;
            }

            Sprite cached;
            if (Cache.TryGetValue(kind, out cached))
            {
                return cached;
            }

            string file = FileName(kind);
            Sprite sprite = string.IsNullOrEmpty(file)
                ? null
                : Resources.Load<Sprite>(ResourceDir + file);

            Cache[kind] = sprite;
            return sprite;
        }

        /// <summary>枚举 → 文件名（与 `render_aura_icons.py` 的 FILES 表同一份命名）。</summary>
        public static string FileName(AuraKind kind)
        {
            switch (kind)
            {
                case AuraKind.AtkPower:
                    return "Aura_01_atk";

                case AuraKind.DefPower:
                    return "Aura_02_def";

                case AuraKind.AtkOrDefPower:
                    return "Aura_03_both";

                case AuraKind.Combo:
                    return "Aura_04_combo";

                case AuraKind.ImmuneHigh:
                    return "Aura_05_immunehigh";

                case AuraKind.ImmuneLow:
                    return "Aura_06_immunelow";

                case AuraKind.QuickRefill:
                    return "Aura_07_refill";

                default:
                    return null;
            }
        }
    }
}
