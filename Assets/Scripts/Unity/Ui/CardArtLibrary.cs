using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 卡 ID → 卡面 <b>插画</b> Sprite 的映射表。
    ///
    /// <para><b>为什么需要它</b>：卡面图放在 `Assets/Art/CardArt/`（不在 Resources 下，
    /// 运行时加载不到），而手牌等是运行时按卡 ID 动态取的 —— 所以用一张 ScriptableObject
    /// 把 Sprite 引用烘进去，既能被 Prefab / 场景序列化，也不占 Resources 的打包体积。</para>
    ///
    /// <para><b>⚠ 2026-09-30：只剩插画这一份图</b>（用户口径「任何地方都不要用成品整图，
    /// 把 `Art/Cards/` 彻底删掉」）。此前还并列存在一族 760×1056 的<b>成品整图</b> ——
    /// 卡名 / 力量 / 冷却 / 效果文字全烘焙在 PNG 里。它的问题不是难看，而是
    /// <b>会与卡表各说各话</b>：2026-09-28 磁暴 / 引雷互换力量时，必须回头去改那张 PNG 里
    /// 烘焙的数字，否则同一张牌在「手牌」与「长按详情」上会显示两个数。
    /// 现在的口径是：<b>卡面的一切文字与数值都由 TMP 现场渲染</b>（M16 组成式卡面），
    /// 图片只提供无框无字的插画。</para>
    ///
    /// <para>生成方式：菜单 `魔法乱斗/M7 · 构建 UiKit` 会扫描 `Assets/Art/CardArt/` 重建本资产。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "CardArtLibrary", menuName = "魔法乱斗/卡面映射表")]
    public sealed class CardArtLibrary : ScriptableObject
    {
        /// <summary>约定的 Resources 路径（不含扩展名）。</summary>
        public const string ResourcePath = "CardArtLibrary";

        [Serializable]
        public struct Entry
        {
            /// <summary>卡 ID（a–ap）。</summary>
            public string CardId;

            /// <summary>
            /// 插画（`Assets/Art/CardArt/`，784×1168，<b>无框无字</b>）。
            ///
            /// <para>卡名 / 力量 / 冷却 / 效果文字全部由 TMP 现场渲染（组成式卡面，M16），
            /// 所以这一份图在手牌、冷却迷你卡、长按放大、选牌弹窗、看对方手牌、
            /// 头顶出牌、飞行卡这几处<b>是同一张</b> —— 差别只在显示尺寸与渲染精度。</para>
            /// </summary>
            public Sprite Illustration;
        }

        [SerializeField]
        private Entry[] _entries = new Entry[0];

        [NonSerialized]
        private Dictionary<string, Sprite> _illustrationMap;

        private static CardArtLibrary _instance;

        /// <summary>从 Resources 取单例（不存在返回 null，UI 会退化成纯色占位）。</summary>
        public static CardArtLibrary Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<CardArtLibrary>(ResourcePath);
                }

                return _instance;
            }
        }

        public int Count
        {
            get { return _entries == null ? 0 : _entries.Length; }
        }

        public Entry[] Entries
        {
            get { return _entries ?? new Entry[0]; }
        }

        /// <summary>批量写入（编辑器生成器用）。</summary>
        public void SetEntries(List<Entry> entries)
        {
            _entries = entries == null ? new Entry[0] : entries.ToArray();
            _illustrationMap = null;
        }

        /// <summary>
        /// 取卡面（插画）。查不到返回 <c>null</c>，由调用方各自决定怎么退化。
        ///
        /// <para>⚠ 2026-09-30 之前这里是两步：先取成品整图、缺了再回落到插画。
        /// 现在整图整族已删，所以只剩这一步 —— 调用方也不必再写「整图 → 插画」的退化链。</para>
        /// </summary>
        public Sprite GetIllustration(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return null;
            }

            EnsureIllustrationMap();
            Sprite sprite;
            return _illustrationMap.TryGetValue(cardId, out sprite) ? sprite : null;
        }

        public Sprite GetIllustration(CardDef def)
        {
            return def == null ? null : GetIllustration(def.Id);
        }

        private void EnsureIllustrationMap()
        {
            if (_illustrationMap != null)
            {
                return;
            }

            _illustrationMap = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (!string.IsNullOrEmpty(e.CardId) && e.Illustration != null
                    && !_illustrationMap.ContainsKey(e.CardId))
                {
                    _illustrationMap.Add(e.CardId, e.Illustration);
                }
            }
        }
    }
}
