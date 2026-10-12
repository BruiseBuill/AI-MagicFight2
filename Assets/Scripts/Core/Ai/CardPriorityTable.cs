using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>一条手写优先级</b>（2026-10-08）—— 「这张牌第几个打」写在卡池资产上。
    ///
    /// <para><b>⚠ 它必须能被 Unity 序列化</b>（卡池是 <c>ScriptableObject</c>、字段直接摆在
    /// Inspector 上）：<c>[Serializable]</c> + <b>public 字段</b> + <b>无参构造</b>。
    /// 与 <see cref="CardUpgradeRecord"/> 同一条约束（那边是被 <c>JsonUtility</c> 读，
    /// 这边是被 Unity 自己的序列化读），差别只是「属性 / 只读字段会被静默丢掉」这条陷阱
    /// 在两边都成立。</para>
    ///
    /// <para>本类型住在 Core（不是 App）的理由：吃它的是 <see cref="HeuristicAgent"/>，
    /// 而 AI 在 Core，它没有 UnityEngine、也就拿不到 <c>CardPoolConfig</c> ——
    /// 所以「表」这个形状必须在 Core，资产的类型才能在 App 层直接引用它
    /// （与 <c>CharacterConfig.upgrades</c> 引用 Core 的 <see cref="CardUpgradeRecord"/> 同一手法）。</para>
    /// </summary>
    [Serializable]
    public sealed class CardPriorityEntry
    {
        /// <summary>
        /// 卡 ID —— 填<b>基础 ID</b>（<c>a</c> 而不是 <c>a+</c>）。
        ///
        /// <para>⚠ 比对时两边都过 <see cref="CardUpgrade.BaseIdOf"/>，所以写了 <c>a+</c> 也能命中
        /// <c>a</c>；但推荐写基础 ID —— 强化版是「读时合成」出来的，存档 / 卡池里存的一律是基础 ID。</para>
        /// </summary>
        public string cardId = string.Empty;

        /// <summary>
        /// 出牌优先级：<b>数值越小越先打出</b>（与代码里的档位同方向 —— 0 号位最先出）。
        ///
        /// <para>表里的卡<b>彼此</b>按这个数排序；没登记的卡不看这个数（见
        /// <see cref="CardPriorityTable.UnlistedTier"/>）。</para>
        /// </summary>
        public int priority;

        /// <summary>Unity 序列化要无参构造。</summary>
        public CardPriorityEntry()
        {
        }
    }

    /// <summary>
    /// <b>手写的出牌优先级表</b>（2026-10-08 · 「我要自己排，别被自动计算覆盖」）。
    ///
    /// <para><b>它解决什么</b>：出牌优先级原先只有一条来路 —— <see cref="HeuristicAgent"/> 按
    /// 四流派现算档位（<c>DefaultPriority</c> 那一组函数）。想给某只怪一个「不合套路」的
    /// 出牌顺序（例如「它宁可先烧掉凝固」），唯一办法是写代码子类覆写
    /// <c>PriorityOf</c>（见 <c>MonsterAgents</c> 的 Berserker / Trickster），
    /// 而那要改代码、重编译、还只对那一只怪生效。现在把这条口子开到<b>卡池资产</b>上：
    /// 勾一个开关 + 填一张表，就能给这只怪（这份卡池）一个手写的出牌顺序。</para>
    ///
    /// <para><b>⚠ 开启开关 = 代码不再给任何卡赋值</b>（用户 2026-10-08 口径原文：
    /// 「一旦启用了手动，代码当中就不能再对卡的优先级再赋值」）。所以：
    /// <list type="bullet">
    /// <item>登记过的卡 → 用表里的数（小者先打）；</item>
    /// <item><b>没登记的卡 → 一律并列最低</b>（<see cref="UnlistedTier"/>），
    ///   它们之间按引擎给的候选顺序取第一张，<b>不会</b>偷偷回到四流派档位；</item>
    /// <item>「回血卡永远最低 / 可回血时永远最高」这条全局覆盖<b>也让位</b> ——
    ///   它同样是「代码给卡赋值」，会把玩家写的东西盖掉。</item>
    /// </list>
    /// 也就是说：<b>勾上开关、填了表，这个卡池的出牌顺序就完全由这张表决定</b>。
    /// 想只调几张、其余沿用自动 —— 那就别勾开关（这是刻意的二选一，不做「混两种刻度」
    /// 那种没人算得清的折衷）。</para>
    ///
    /// <para><b>不可变</b>：与 <see cref="UpgradeBook"/> 同理 —— 表会被多个座位 / 多次
    /// <c>BattleSetup</c> 共享，可变对象一改全改。</para>
    /// </summary>
    public sealed class CardPriorityTable
    {
        private readonly Dictionary<string, int> _byBaseId;

        /// <summary>空表（开关没勾 / 一列都没填）。</summary>
        public static readonly CardPriorityTable Empty = new CardPriorityTable(null);

        /// <summary>
        /// <b>没登记在表里的卡用的档位</b> —— 取一个大到不可能与手写值撞车的数，
        /// 保证「没登记的卡」永远排在任何登记过的卡之后（手写值允许为负）。
        /// </summary>
        public const int UnlistedTier = 1000000;

        private CardPriorityTable(IEnumerable<CardPriorityEntry> entries)
        {
            _byBaseId = new Dictionary<string, int>(StringComparer.Ordinal);
            if (entries == null)
            {
                return;
            }

            foreach (CardPriorityEntry entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                string baseId = CardUpgrade.BaseIdOf(entry.cardId);
                if (baseId.Length == 0)
                {
                    continue;   // 空 ID = 那一行还没填完，忽略（不落成一条「空卡的优先级」）
                }

                _byBaseId[baseId] = entry.priority;
            }
        }

        /// <summary>从一串条目建表（<c>null</c> / 空 → <see cref="Empty"/>）。</summary>
        public static CardPriorityTable From(IEnumerable<CardPriorityEntry> entries)
        {
            var table = new CardPriorityTable(entries);
            return table.IsEmpty ? Empty : table;
        }

        /// <summary>表里登记了几张卡。</summary>
        public int Count
        {
            get { return _byBaseId.Count; }
        }

        /// <summary>一张都没登记。</summary>
        public bool IsEmpty
        {
            get { return _byBaseId.Count == 0; }
        }

        /// <summary>
        /// 查一张牌的优先级。吃基础 ID 或解析后的 ID 都行（内部过
        /// <see cref="CardUpgrade.BaseIdOf"/> —— 「仍然能被识别为同一张卡」这条口径在这里也必须成立，
        /// 否则手牌里那张 <c>a+</c> 会查不到 <c>a</c> 的优先级）。
        /// </summary>
        public bool TryGet(string idOrBaseId, out int priority)
        {
            string baseId = CardUpgrade.BaseIdOf(idOrBaseId);
            if (baseId.Length > 0 && _byBaseId.TryGetValue(baseId, out priority))
            {
                return true;
            }

            priority = UnlistedTier;
            return false;
        }

        /// <summary>查不到时返回 <see cref="UnlistedTier"/>（不再有「回落到自动档位」这一说）。</summary>
        public int TierOf(string idOrBaseId)
        {
            int priority;
            TryGet(idOrBaseId, out priority);
            return priority;
        }

        /// <summary>表里的基础 ID 清单（顺序不保证；日志 / 调试用）。</summary>
        public List<string> Ids()
        {
            var list = new List<string>(_byBaseId.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>一行摘要（日志用），例：<c>a=0 · f=1 · h=3</c>。</summary>
        public string Describe()
        {
            if (IsEmpty)
            {
                return "（没有手写优先级）";
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in _byBaseId)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join(" · ", parts.ToArray());
        }
    }
}
