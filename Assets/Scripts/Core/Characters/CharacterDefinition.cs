using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    public enum CharacterKind { Player, Monster }
    public enum ControlKind { Human, Ai }

    /// <summary>可编辑的配置卡池；与每局消耗的 Deck 分开。每种卡最多一张。</summary>
    public sealed class CardPool
    {
        private readonly List<string> _ids;
        private readonly ICardCatalog _catalog;
        public ICardCatalog Catalog { get { return _catalog; } }
        public IReadOnlyList<string> CardIds { get { return _ids.AsReadOnly(); } }
        public int Count { get { return _ids.Count; } }

        public CardPool(IEnumerable<string> ids, ICardCatalog catalog = null)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            _catalog = catalog ?? CardCatalog.Builtin();
            _ids = new List<string>();
            foreach (string id in ids) Add(id);
        }

        public static CardPool AllCards(ICardCatalog catalog = null)
        {
            catalog = catalog ?? CardCatalog.Builtin();
            var ids = new List<string>();
            foreach (CardDef card in catalog.All) ids.Add(card.Id);
            return new CardPool(ids, catalog);
        }

        public bool Add(string id)
        {
            _catalog.Get(id);
            if (_ids.Contains(id)) return false;
            _ids.Add(id);
            return true;
        }

        public bool Remove(string id) { return _ids.Remove(id); }
        public bool Contains(string id) { return _ids.Contains(id); }
        public CardPool Copy() { return new CardPool(_ids, _catalog); }

        public bool Validate(int minimum, out string error)
        {
            error = Count < minimum ? "卡池至少需要 " + minimum + " 张牌，当前为 " + Count + " 张。" : null;
            return error == null;
        }

        public IReadOnlyList<CardDef> Resolve()
        {
            // 固定为卡表顺序，勾选顺序不影响同种子复现。
            var cards = new List<CardDef>();
            foreach (CardDef card in _catalog.All)
                if (Contains(card.Id)) cards.Add(card);
            return cards.AsReadOnly();
        }
    }

    /// <summary>静态角色定义；能力定义在开局时创建各自的运行实例。</summary>
    public sealed class CharacterDefinition
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public CharacterKind Kind { get; private set; }
        public int InitialHp { get; private set; }
        public int MaxHp { get; private set; }
        public int MinimumCardCount { get; private set; }

        /// <summary>
        /// 这个角色用的<b>怪物 AI 档案名</b>（2026-10-03）—— 「代码子类复写」方案的接线点。
        ///
        /// <para>空串 = 通用 <see cref="HeuristicAgent"/>；其它值由
        /// <see cref="MonsterAgentFactory"/> 映射到具体子类
        /// （如 <see cref="MonsterAgentFactory.Berserker"/> / <see cref="MonsterAgentFactory.Trickster"/>）。</para>
        ///
        /// <para>放在 <see cref="CharacterDefinition"/> 而不是 App 的配置资产上：它是
        /// <b>角色的一部分</b>（同一只怪换到哪一关都该用同一套思路），且战斗侧（Core）
        /// 需要读它来建 AI —— 挂在 App 层的话 Core 就拿不到了。</para>
        /// </summary>
        public string AiProfile { get; private set; }

        /// <summary>
        /// 这个角色的<b>发牌口径</b>（2026-10-03）。
        ///
        /// <para><b>null = 用引擎那份 <see cref="IDealPolicy"/></b>（老口径：开局 6 张可换 3、
        /// 第 2–3 回合各补 1）。给了值就以它为准，<b>按座位</b>生效 —— 人类与怪物可以在
        /// 同一局里用两套完全不同的口径。</para>
        ///
        /// <para>放在角色定义上而不是引擎上，理由与 <see cref="AiProfile"/> 相同：
        /// 「开局抽几张、之后补不补」是<b>这个角色的一部分</b>（同一只怪换到哪一关都该
        /// 是 8 张不再补），不是某一局的参数。</para>
        /// </summary>
        public DealProfile Deal { get; private set; }

        private readonly CardPool _pool;
        public CardPool CreateCardPool() { return _pool.Copy(); }
        public IReadOnlyList<ICharacterAbilityDefinition> Abilities { get; private set; }

        public CharacterDefinition(string id, string name, CharacterKind kind, int initialHp,
            int maxHp, int minimumCardCount, CardPool pool,
            IEnumerable<ICharacterAbilityDefinition> abilities = null, string aiProfile = null,
            DealProfile deal = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("角色 ID 不能为空。");
            if (initialHp <= 0 || maxHp < initialHp) throw new ArgumentException("生命必须大于 0，且不超过上限。");
            if (minimumCardCount < 1) throw new ArgumentException("最少卡牌数必须至少为 1。");
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            string error;
            if (!pool.Validate(minimumCardCount, out error)) throw new ArgumentException(error);
            Id = id; Name = name; Kind = kind; InitialHp = initialHp; MaxHp = maxHp;
            MinimumCardCount = minimumCardCount;
            AiProfile = aiProfile ?? string.Empty;
            Deal = deal;
            _pool = pool.Copy();
            var list = new List<ICharacterAbilityDefinition>(abilities ?? new ICharacterAbilityDefinition[0]);
            if (list.Contains(null)) throw new ArgumentException("能力定义不能为空。");
            Abilities = list.AsReadOnly();
        }

        public CharacterDefinition WithCardPool(CardPool pool)
        {
            return new CharacterDefinition(Id, Name, Kind, InitialHp, MaxHp, MinimumCardCount, pool, Abilities, AiProfile, Deal);
        }

        public static CharacterDefinition DefaultPlayer()
        {
            return new CharacterDefinition("player.default", "你", CharacterKind.Player, 4, 4, 8, CardPool.AllCards(),
                null, null, DealProfile.Human());
        }

        /// <summary>
        /// 兜底怪物（2026-10-03 起带 <see cref="DealProfile.Monster"/>：开局 8 张、之后不补、不换牌）。
        ///
        /// <para>⚠ 它同时是万局自测（<c>BattleSetup.Default()</c>）与「没配怪物资产」时的
        /// 运行时兜底 —— 这次发给它怪物口径之后，<b>万局统计会整体改变</b>（那是预期内的
        /// 规则改动，不是回归退化；见 <c>Docs/implementation/2026-10-03-怪物框架与自爆怪.md</c>）。</para>
        /// </summary>
        public static CharacterDefinition DefaultMonster()
        {
            return new CharacterDefinition("monster.default", "怪物", CharacterKind.Monster, 4, 4, 8, CardPool.AllCards(),
                null, null, DealProfile.Monster());
        }
    }
}
