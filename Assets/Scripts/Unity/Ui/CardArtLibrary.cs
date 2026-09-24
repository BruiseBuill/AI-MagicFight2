using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 卡 ID → 卡面 Sprite 的映射表。
    ///
    /// <para><b>为什么需要它</b>：40 张卡面在 `Assets/Art/Cards/`（不在 Resources 下，运行时
    /// 加载不到），而手牌是运行时按卡 ID 动态取的 —— 所以用一张 ScriptableObject 把
    /// Sprite 引用烘进去，既能被 Prefab / 场景序列化，也不占 Resources 的打包体积。</para>
    ///
    /// <para><b>卡面是「整卡」</b>：卡名、力量、冷却、效果文字全部烘焙在 760×1056 的图里
    /// （见 `Docs/engineering/06-美术与字体规范.md`）。所以：
    /// 手牌大卡直接用整图 + <b>关键数值的 TMP 角标</b>（缩到 208 px 后烘焙文字只有 ~8 px，不可读）；
    /// 冷却迷你卡不能复用整图（剩余冷却是动态值），必须卡框 + TMP 合成。</para>
    ///
    /// 生成方式：菜单 `魔法乱斗/M7 · 构建 UiKit` 会自动扫描 `Assets/Art/Cards/` 重建本资产。
    /// </summary>
    [CreateAssetMenu(fileName = "CardArtLibrary", menuName = "魔法乱斗/卡面映射表")]
    public sealed class CardArtLibrary : ScriptableObject
    {
        /// <summary>约定的 Resources 路径（不含扩展名）。</summary>
        public const string ResourcePath = "CardArtLibrary";

        [Serializable]
        public struct Entry
        {
            /// <summary>卡 ID（a–an）。</summary>
            public string CardId;

            /// <summary>整张卡面（`Assets/Art/Cards/`，760×1056，卡名 / 数值 / 文字全烘焙在图上）。</summary>
            public Sprite Art;

            /// <summary>
            /// 插画原图（`Assets/Art/CardArt/`，784×1168，无框无字）。
            ///
            /// <para>M16 起手牌与冷却迷你卡改用**组成式卡面**：这一张铺满整卡当底图，
            /// 卡名 / 力量 / 冷却 / 效果文字全部由 TMP 现场渲染。整卡面（<see cref="Art"/>）
            /// 保留给「长按看完整卡面」的详情浮层与出牌演出 —— 那两处要的就是烘焙好的成品图。</para>
            /// </summary>
            public Sprite Illustration;
        }

        [SerializeField]
        private Entry[] _entries = new Entry[0];

        [NonSerialized]
        private Dictionary<string, Sprite> _map;

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
            _map = null;
            _illustrationMap = null;
        }

        public Sprite GetArt(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return null;
            }

            EnsureMap();
            Sprite sprite;
            return _map.TryGetValue(cardId, out sprite) ? sprite : null;
        }

        public Sprite GetArt(CardDef def)
        {
            return def == null ? null : GetArt(def.Id);
        }

        /// <summary>取插画原图（组成式卡面的底图）。</summary>
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

        private void EnsureMap()
        {
            if (_map != null)
            {
                return;
            }

            _map = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (!string.IsNullOrEmpty(e.CardId) && e.Art != null && !_map.ContainsKey(e.CardId))
                {
                    _map.Add(e.CardId, e.Art);
                }
            }
        }
    }
}
