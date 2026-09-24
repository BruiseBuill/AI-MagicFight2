using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 效果时机 → 图标 Sprite 的映射表。
    ///
    /// <para><b>它解决什么</b>：卡面上的 α / β / γ 符号说明「这条效果什么时候结算」
    /// （见 `Docs/rules/01-规则基线.md` §通用原则）。卡面文字是烘焙死的，但**迷你卡、详情浮层、
    /// 提示条上的文字要由 TMP 现场渲染**，那时符号必须由 Sprite 提供。</para>
    ///
    /// <para><b>符号与枚举一一对应</b>：</para>
    /// <list type="bullet">
    /// <item><c>EffectTrigger.Attack</c> = α = <b>剑</b> —— 本牌作为进攻牌打出时结算</item>
    /// <item><c>EffectTrigger.Defend</c> = β = <b>盾</b> —— 本牌作为防御牌打出时结算</item>
    /// <item><c>EffectTrigger.Special</c> = γ = <b>感叹号</b> —— 特殊时机，由卡面文字说明</item>
    /// <item><c>EffectTrigger.Passive</c> = 常驻 —— 本批 40 张卡未使用，故暂无图标</item>
    /// </list>
    ///
    /// <para>图标素材由 `Tools/art-audit/slice_icons.py` 从三合一拼图切出，
    /// 再经菜单 `魔法乱斗/整理 · 建触发图标库` 写入本资产。
    /// 拼图原图留在 `Assets/Art/Icons/_Source/`。</para>
    ///
    /// <para>放在 Resources 下是为了运行时能取到（与 <see cref="CardArtLibrary"/> 同理）。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "TriggerIconLibrary", menuName = "魔法乱斗/触发图标库")]
    public sealed class TriggerIconLibrary : ScriptableObject
    {
        /// <summary>约定的 Resources 路径（不含扩展名）。</summary>
        public const string ResourcePath = "TriggerIconLibrary";

        /// <summary>图标资源所在目录（生成器与文档共用这一个常量）。</summary>
        public const string IconDir = "Assets/Art/Icons";

        [Serializable]
        public struct Entry
        {
            /// <summary>效果时机。</summary>
            public EffectTrigger Trigger;

            /// <summary>对应的符号 Sprite（剑 / 盾 / 感叹号）。</summary>
            public Sprite Icon;
        }

        [SerializeField]
        private Entry[] _entries = new Entry[0];

        [NonSerialized]
        private Dictionary<EffectTrigger, Sprite> _map;

        private static TriggerIconLibrary _instance;

        /// <summary>从 Resources 取单例（不存在返回 null，UI 需自行降级）。</summary>
        public static TriggerIconLibrary Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<TriggerIconLibrary>(ResourcePath);
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
        }

        /// <summary>取某个时机的符号。没有对应图标时返回 null。</summary>
        public Sprite GetIcon(EffectTrigger trigger)
        {
            EnsureMap();
            Sprite sprite;
            return _map.TryGetValue(trigger, out sprite) ? sprite : null;
        }

        /// <summary>某个时机有没有图标（UI 可据此决定是否留出符号位）。</summary>
        public bool HasIcon(EffectTrigger trigger)
        {
            return GetIcon(trigger) != null;
        }

        private void EnsureMap()
        {
            if (_map != null)
            {
                return;
            }

            _map = new Dictionary<EffectTrigger, Sprite>();
            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (e.Icon != null && !_map.ContainsKey(e.Trigger))
                {
                    _map.Add(e.Trigger, e.Icon);
                }
            }
        }
    }
}
