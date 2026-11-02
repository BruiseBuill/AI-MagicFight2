using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>这条历史记录是谁用的</b>（2026-11-02）。
    ///
    /// <para>只有两种 —— 自定义战斗里一副牌不是「你在用」就是「对面在用」，
    /// 没有第三种归属，也就不需要更复杂的枚举。</para>
    /// </summary>
    public enum DeckOwner
    {
        /// <summary>玩家自己用过的牌组。</summary>
        Player = 0,

        /// <summary>AI（对手）用过的牌组。</summary>
        Ai = 1,
    }

    /// <summary>
    /// <b>一条历史牌组</b>（2026-11-02）—— <b>一位使用者</b>的 8 张。
    ///
    /// <para><b>⚠ 它不是「一场对局」</b>：用户口径是「只记录是玩家用的还是 AI 用过的，
    /// 以及卡组内容」，所以一条 = 单方 8 张，双方在一次开战里会各写一条。
    /// 也正因为如此，<b>这里没有时间字段</b> —— 顺序就是「最近用过的在前」，
    /// 不需要再存一个永远只用于显示的时间戳。</para>
    ///
    /// <para><b>字段是 public 的</b>：<see cref="JsonUtility"/> 只认字段，不认属性；
    /// 其余读取一律走只读属性，避免外部拿到 <see cref="ids"/> 改一半忘了存档。</para>
    /// </summary>
    [Serializable]
    public sealed class DeckHistoryEntry
    {
        /// <summary>0 = 玩家 / 1 = AI（与 <see cref="DeckOwner"/> 同值，序列化成 int 更稳）。</summary>
        public int owner;

        /// <summary>8 个基础卡 ID（按卡表顺序，不是点击顺序）。</summary>
        public List<string> ids = new List<string>();

        public DeckOwner Owner
        {
            get { return owner == (int)DeckOwner.Ai ? DeckOwner.Ai : DeckOwner.Player; }
        }

        public int Count
        {
            get { return ids == null ? 0 : ids.Count; }
        }
    }

    /// <summary>
    /// <b>自定义战斗 · 历史牌组</b>（2026-11-02）—— 只服务 DeckSelect 一个场景的存档。
    ///
    /// <para><b>为什么落 PlayerPrefs</b>：与 <see cref="GameSettings"/> 同口径 ——
    /// 它回答的是「上次选了什么」这个量级，不值得为它启一套 SaveStore；
    /// 一个键存一段 JSON，读写都在这里，别处不碰。</para>
    ///
    /// <para><b>四条硬口径</b>（用户 2026-11-02 明确）：</para>
    /// <list type="number">
    /// <item>一条 = <b>单方 8 张</b> + 归属（玩家 / AI），<b>不存时间</b>；</item>
    /// <item>最多 <see cref="Capacity"/> = 60 条（约 30 场，每场写两条），超出淘汰最旧；</item>
    /// <item>去重按「归属 + 8 张的<b>集合</b>」—— 顺序不算差异，命中就把旧条提到最前；</item>
    /// <item>写入时机由调用方决定（<see cref="DeckSelectEntry"/> 在「开战」校验通过后写），
    /// 这里不做任何「什么时候该记」的判断。</item>
    /// </list>
    ///
    /// <para><b>⚠ 读失败一律当空表</b>：JSON 坏掉 / 老版本残留，宁可丢历史也不能让
    /// 编成界面打不开 —— 历史是锦上添花，牌组编成才是主线。</para>
    /// </summary>
    public static class DeckHistoryStore
    {
        /// <summary>上限（用户要求「至少 60 份」，正好取 60：多余的最旧一条被淘汰）。</summary>
        public const int Capacity = 60;

        /// <summary>PlayerPrefs 键。改结构时同时改 <see cref="Version"/>，别改键名。</summary>
        private const string PrefsKey = "MagicBrawl.CustomDeckHistory";

        private const int Version = 1;

        /// <summary>落盘的整体（JsonUtility 不能直接序列化顶层 List，必须包一层）。</summary>
        [Serializable]
        private sealed class Blob
        {
            public int version = Version;
            public List<DeckHistoryEntry> entries = new List<DeckHistoryEntry>();
        }

        private static Blob _blob;

        /// <summary>现在存了几条（徽标上的数字）。</summary>
        public static int Count
        {
            get
            {
                EnsureLoaded();
                return _blob.entries.Count;
            }
        }

        /// <summary>全部记录，<b>最近用过的在最前</b>（只读视图，别转成 List 改）。</summary>
        public static IReadOnlyList<DeckHistoryEntry> All
        {
            get
            {
                EnsureLoaded();
                return _blob.entries;
            }
        }

        /// <summary>取第 <paramref name="index"/> 条；越界返回 null。</summary>
        public static DeckHistoryEntry At(int index)
        {
            EnsureLoaded();
            if (index < 0 || index >= _blob.entries.Count)
            {
                return null;
            }

            return _blob.entries[index];
        }

        /// <summary>
        /// 记一条。少于 / 多于 <see cref="BattleLaunch.DeckSize"/> 张、或整条为空时<b>直接忽略</b>
        /// （返回 false）—— 半成品牌组不该进历史，否则第一次载入就会拿到 5 张。
        /// </summary>
        public static bool Add(DeckOwner owner, IReadOnlyList<string> ids)
        {
            EnsureLoaded();

            List<string> normalized = Normalize(ids);
            if (normalized.Count != BattleLaunch.DeckSize)
            {
                return false;
            }

            for (int i = _blob.entries.Count - 1; i >= 0; i--)
            {
                DeckHistoryEntry existing = _blob.entries[i];
                if (existing != null && existing.Owner == owner && SameSet(existing.ids, normalized))
                {
                    // 同一副牌又用了一次：不新增，把它提到最前（时间信息已由「位置」表达）
                    _blob.entries.RemoveAt(i);
                    break;
                }
            }

            var entry = new DeckHistoryEntry();
            entry.owner = (int)owner;
            entry.ids = normalized;
            _blob.entries.Insert(0, entry);

            while (_blob.entries.Count > Capacity)
            {
                _blob.entries.RemoveAt(_blob.entries.Count - 1);
            }

            Save();
            return true;
        }

        /// <summary>删掉第 <paramref name="index"/> 条（列表里那一行的 ×）。</summary>
        public static bool RemoveAt(int index)
        {
            EnsureLoaded();
            if (index < 0 || index >= _blob.entries.Count)
            {
                return false;
            }

            _blob.entries.RemoveAt(index);
            Save();
            return true;
        }

        /// <summary>清空（目前没有界面入口，留给自测与将来「清空历史」）。</summary>
        public static void Clear()
        {
            EnsureLoaded();
            _blob.entries.Clear();
            Save();
        }

        /// <summary>丢掉内存缓存、下次访问重新读盘（自测 / 换账号时用）。</summary>
        public static void Reload()
        {
            _blob = null;
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private static void EnsureLoaded()
        {
            if (_blob != null)
            {
                return;
            }

            _blob = null;
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    Blob parsed = JsonUtility.FromJson<Blob>(json);
                    if (parsed != null && parsed.entries != null)
                    {
                        _blob = parsed;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DeckHistoryStore] 历史牌组读不出来，按空表处理：" + ex.Message);
                }
            }

            if (_blob == null)
            {
                _blob = new Blob();
            }

            // 逐条体检：空行 / 张数不对的直接剔掉（老版本残留不配占位置）
            for (int i = _blob.entries.Count - 1; i >= 0; i--)
            {
                DeckHistoryEntry entry = _blob.entries[i];
                if (entry == null || Normalize(entry.ids).Count != BattleLaunch.DeckSize)
                {
                    _blob.entries.RemoveAt(i);
                    continue;
                }

                entry.ids = Normalize(entry.ids);
            }

            while (_blob.entries.Count > Capacity)
            {
                _blob.entries.RemoveAt(_blob.entries.Count - 1);
            }
        }

        private static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_blob));
            PlayerPrefs.Save();
        }

        /// <summary>去掉空串与升级后缀，保持原顺序。</summary>
        private static List<string> Normalize(IReadOnlyList<string> ids)
        {
            var list = new List<string>(BattleLaunch.DeckSize);
            if (ids == null)
            {
                return list;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                string baseId = CardIds.BaseOf(id);
                if (!string.IsNullOrEmpty(baseId) && !list.Contains(baseId))
                {
                    list.Add(baseId);
                }
            }

            return list;
        }

        /// <summary>两副牌是不是同一套（与顺序无关）。</summary>
        private static bool SameSet(List<string> a, List<string> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!b.Contains(a[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
