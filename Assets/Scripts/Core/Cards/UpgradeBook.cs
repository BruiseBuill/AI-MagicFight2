using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>强化册</b>：一份「基础 ID → 强化配方」的表。
    ///
    /// <para><b>它是「强化随谁走」这个问题的答案</b>。上一版把强化落成一份
    /// <c>Card_a_Up.asset</c>，于是强化成了<b>全局事实</b> —— 全工程只有一张
    /// <c>a+</c>，A 存档强化了暴风雪，B 存档也会看到一张强化过的暴风雪，
    /// 而「怪物强化了两点力量」根本无处安放。强化册把配方变回<b>数据</b>：
    /// 玩家那份随存档走（<c>PlayerData.upgrades</c>），怪物那份挂在角色配置上
    /// （<c>CharacterConfig.upgrades</c>），两份是同一个类型、同一套合成规则。</para>
    ///
    /// <para><b>它自己不产生任何 <see cref="CardDef"/></b>：合成唯一的入口是
    /// <see cref="BuildCatalog"/>（把配方叠到一份卡目录上，产出一份<b>新的目录</b>）。
    /// 这一点是刻意的 —— 一旦让「册子」也能单独吐出一张卡，就会出现第二个
    /// 「这张牌现在长什么样」的来源，与卡目录打架。</para>
    ///
    /// <para><b>不可变</b>：<see cref="With"/> / <see cref="Append"/> / <see cref="Without"/>
    /// 都返回新的册子。理由与 <see cref="CardDef"/> 一样 —— 册子会被多个场景共享
    /// （「读取时解析」这条链上到处都是），可变对象一改全改，排查起来是一场噩梦。</para>
    /// </summary>
    public sealed class UpgradeBook
    {
        private readonly Dictionary<string, CardUpgradeRecord> _byBaseId;

        /// <summary>空册子（什么也没强化）。</summary>
        public static readonly UpgradeBook Empty = new UpgradeBook(null);

        private UpgradeBook(IEnumerable<CardUpgradeRecord> records)
        {
            _byBaseId = new Dictionary<string, CardUpgradeRecord>(StringComparer.Ordinal);
            if (records != null)
            {
                foreach (CardUpgradeRecord record in records)
                {
                    Add(record);
                }
            }
        }

        /// <summary>把一条记录塞进字典（空配方直接丢）。同一基础 ID 出现两次时后者覆盖前者。</summary>
        private void Add(CardUpgradeRecord record)
        {
            if (record == null)
            {
                return;
            }

            string baseId = CardUpgrade.BaseIdOf(record.baseId);
            if (baseId.Length == 0 || record.IsEmpty)
            {
                return;
            }

            record.baseId = baseId;   // 归一：配方恒挂在基础 ID 上
            _byBaseId[baseId] = record;
        }

        /// <summary>册子里有几张牌被强化过（<b>不是几笔</b>）。</summary>
        public int Count
        {
            get { return _byBaseId.Count; }
        }

        /// <summary>什么也没强化。</summary>
        public bool IsEmpty
        {
            get { return _byBaseId.Count == 0; }
        }

        /// <summary>册子里的全部配方（顺序不保证，只用于落盘与日志）。</summary>
        public List<CardUpgradeRecord> ToRecords()
        {
            var list = new List<CardUpgradeRecord>();
            foreach (KeyValuePair<string, CardUpgradeRecord> pair in _byBaseId)
            {
                if (pair.Value != null && !pair.Value.IsEmpty)
                {
                    list.Add(pair.Value);
                }
            }

            return list;
        }

        /// <summary>
        /// 查一张牌的配方。<b>吃基础 ID 或解析后的 ID 都行</b>
        /// （内部过 <see cref="CardUpgrade.BaseIdOf"/>）—— 「仍然能被识别为同一张卡」
        /// 这条口径在册子上也必须成立，否则从界面拿到的 <c>a+</c> 会查不到配方。
        /// </summary>
        public bool TryGet(string idOrBaseId, out CardUpgradeRecord record)
        {
            string baseId = CardUpgrade.BaseIdOf(idOrBaseId);
            if (baseId.Length > 0 && _byBaseId.TryGetValue(baseId, out record))
            {
                return true;
            }

            record = null;
            return false;
        }

        /// <summary>这张牌强化过没有。</summary>
        public bool Has(string idOrBaseId)
        {
            CardUpgradeRecord record;
            return TryGet(idOrBaseId, out record);
        }

        /// <summary>换掉 / 新增一张牌的配方（返回新册子；<paramref name="record"/> 为空表示移除）。</summary>
        public UpgradeBook With(CardUpgradeRecord record)
        {
            var next = new Dictionary<string, CardUpgradeRecord>(_byBaseId);
            if (record == null || record.IsEmpty)
            {
                string key = record == null ? null : CardUpgrade.BaseIdOf(record.baseId);
                if (string.IsNullOrEmpty(key))
                {
                    return new UpgradeBook(next.Values);
                }

                next.Remove(key);
                return new UpgradeBook(next.Values);
            }

            next[record.baseId] = record;
            return new UpgradeBook(next.Values);
        }

        /// <summary><b>追加一笔</b>强化（保留这张牌已有的其它强化）—— 强化节点的入口用这个。</summary>
        public UpgradeBook Append(string idOrBaseId, CardUpgradeMod mod)
        {
            if (mod == null)
            {
                return new UpgradeBook(_byBaseId.Values);
            }

            string baseId = CardUpgrade.BaseIdOf(idOrBaseId);
            if (baseId.Length == 0)
            {
                return new UpgradeBook(_byBaseId.Values);
            }

            CardUpgradeRecord existing;
            var record = _byBaseId.TryGetValue(baseId, out existing) && existing != null
                ? new CardUpgradeRecord { baseId = baseId, mods = new List<CardUpgradeMod>(existing.mods) }
                : new CardUpgradeRecord { baseId = baseId };

            record.mods.Add(mod);
            return With(record);
        }

        /// <summary>去掉一张牌的强化（回退用；目前没有调用点，留给「取消强化」那类需求）。</summary>
        public UpgradeBook Without(string idOrBaseId)
        {
            string baseId = CardUpgrade.BaseIdOf(idOrBaseId);
            if (baseId.Length == 0 || !_byBaseId.ContainsKey(baseId))
            {
                return new UpgradeBook(_byBaseId.Values);
            }

            var next = new Dictionary<string, CardUpgradeRecord>(_byBaseId);
            next.Remove(baseId);
            return new UpgradeBook(next.Values);
        }

        /// <summary>从一份落盘记录建册子（<b>会过滤空配方与 null</b>）。</summary>
        public static UpgradeBook FromRecords(IEnumerable<CardUpgradeRecord> records)
        {
            if (records == null)
            {
                return Empty;
            }

            var book = new UpgradeBook(records);
            return book.IsEmpty ? Empty : book;
        }

        // ══════════════════════════════════════════════════════
        //  合成
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>把册子叠到一份卡目录上</b>，产出「这一个玩家 / 这一只怪物的卡目录」。
        ///
        /// <para>规则三条：</para>
        /// <list type="number">
        /// <item>配方<b>优先于</b>上一版落盘的强化卡资产：<c>a</c> 既有配方、又有
        ///   <c>Card_a_Up.asset</c> 时，<b>按配方合成</b>，资产版被剔除（不迁移、不合并）——</item>
        /// <item>基础卡不在源目录里时<b>整条配方跳过</b>（那张牌本来就不该出现）；</item>
        /// <item>合出来的定义沿用源目录的其它内容（内置 + 自定义 + 动态卡一张不少）。</item>
        /// </list>
        ///
        /// <para><b>⚠ 空册子直接返回 <paramref name="source"/> 本身</b>（同一个实例，零开销）：
        /// 这条路径在每次开局、每次进事件场景都会走，不该为了「什么都没强化」造一份新目录。</para>
        /// </summary>
        public ICardCatalog BuildCatalog(ICardCatalog source, int indexBase = CardUpgrade.SynthesizedIndexBase)
        {
            ICardCatalog catalog = source ?? CardCatalog.Builtin();
            if (IsEmpty)
            {
                return catalog;
            }

            // ① 先算出「哪些强化版 ID 被配方接管了」—— 这些 ID 要把源目录里同名的那份剔掉，
            //    否则 CardCatalog 的构造函数会因 ID 重复直接抛（开局就炸，很好发现），
            //    或者更糟：两份同名牌同时进卡池（零报错，很难发现）。
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var recipes = new List<CardUpgradeRecord>();
            foreach (KeyValuePair<string, CardUpgradeRecord> pair in _byBaseId)
            {
                if (pair.Value == null || pair.Value.IsEmpty)
                {
                    continue;
                }

                recipes.Add(pair.Value);
                claimed.Add(CardUpgrade.UpgradedIdOf(pair.Value.baseId));
            }

            var byId = new Dictionary<string, CardDef>(StringComparer.Ordinal);
            var list = new List<CardDef>();
            IReadOnlyList<CardDef> all = catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                CardDef card = all[i];
                if (card == null || claimed.Contains(card.Id))
                {
                    continue;
                }

                list.Add(card);
                if (!byId.ContainsKey(card.Id))
                {
                    byId.Add(card.Id, card);
                }
            }

            // ② 逐条配方合成。序号从 indexBase 起（与内置区间、动态卡区间都错开）。
            int index = indexBase;
            for (int i = 0; i < recipes.Count; i++)
            {
                CardDef baseDef;
                if (!byId.TryGetValue(recipes[i].baseId, out baseDef))
                {
                    continue;
                }

                list.Add(CardUpgrade.Apply(baseDef, recipes[i], index));
                index++;
            }

            return new CardCatalog(list);
        }

        /// <summary>一行摘要（日志用）。</summary>
        public string Describe()
        {
            if (IsEmpty)
            {
                return "（没有强化）";
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, CardUpgradeRecord> pair in _byBaseId)
            {
                parts.Add(pair.Key + "：" + pair.Value.Describe());
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join("；", parts.ToArray());
        }
    }
}
