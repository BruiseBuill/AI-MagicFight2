using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>对局记录的本地存档</b>（2026-11-02）。
    ///
    /// <para><b>存在哪</b>：<c>PlayerPrefs</c> 的一个 JSON 字符串。记录是几十条短行、
    /// 一局几十 KB，用文件反而多一层路径 / 权限 / 清理的麻烦；<c>JsonUtility</c>
    /// 对 <see cref="BattleRecord"/> 这种「全是值类型 + List」的结构开箱即用。</para>
    ///
    /// <para><b>只留最近 <see cref="MaxRecords"/> 局</b>：新的一局插在表头，
    /// 超出就丢尾。历史列表按时间倒序，正是这个顺序。</para>
    ///
    /// <para><b>⚠ 读失败不当异常抛</b>：存档格式变了 / 玩家改坏了，
    /// 都必须退化成「没有历史」而不是让 Home 场景起不来。</para>
    /// </summary>
    public static class BattleRecordStore
    {
        /// <summary>最多留几局。</summary>
        public const int MaxRecords = 30;

        private const string PrefsKey = "magicbrawl.battle.records.v1";

        [Serializable]
        private sealed class Bundle
        {
            public List<BattleRecord> Items = new List<BattleRecord>();
        }

        private static List<BattleRecord> _cache;

        /// <summary>全部记录，<b>最新在前</b>。</summary>
        public static IReadOnlyList<BattleRecord> All
        {
            get
            {
                EnsureLoaded();
                return _cache;
            }
        }

        public static int Count
        {
            get { return All.Count; }
        }

        /// <summary>把一局塞进表头并落盘。</summary>
        public static void Add(BattleRecord record)
        {
            if (record == null) return;

            EnsureLoaded();
            _cache.Insert(0, record);
            while (_cache.Count > MaxRecords)
            {
                _cache.RemoveAt(_cache.Count - 1);
            }

            Save();
        }

        public static void Clear()
        {
            _cache = new List<BattleRecord>();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        private static void EnsureLoaded()
        {
            if (_cache != null) return;

            _cache = new List<BattleRecord>();
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                Bundle bundle = JsonUtility.FromJson<Bundle>(json);
                if (bundle != null && bundle.Items != null)
                {
                    for (int i = 0; i < bundle.Items.Count; i++)
                    {
                        if (bundle.Items[i] != null) _cache.Add(bundle.Items[i]);
                    }
                }
            }
            catch (Exception)
            {
                // 存档坏了 → 当作没有历史。旧串保留在 PlayerPrefs 里，下一次 Save 才会覆盖。
                _cache = new List<BattleRecord>();
            }
        }

        private static void Save()
        {
            var bundle = new Bundle();
            for (int i = 0; i < _cache.Count; i++) bundle.Items.Add(_cache[i]);
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(bundle));
            PlayerPrefs.Save();
        }
    }
}
