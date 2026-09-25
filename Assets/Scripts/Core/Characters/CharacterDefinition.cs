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
        private readonly CardPool _pool;
        public CardPool CreateCardPool() { return _pool.Copy(); }
        public IReadOnlyList<ICharacterAbilityDefinition> Abilities { get; private set; }

        public CharacterDefinition(string id, string name, CharacterKind kind, int initialHp,
            int maxHp, int minimumCardCount, CardPool pool,
            IEnumerable<ICharacterAbilityDefinition> abilities = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("角色 ID 不能为空。");
            if (initialHp <= 0 || maxHp < initialHp) throw new ArgumentException("生命必须大于 0，且不超过上限。");
            if (minimumCardCount < 1) throw new ArgumentException("最少卡牌数必须至少为 1。");
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            string error;
            if (!pool.Validate(minimumCardCount, out error)) throw new ArgumentException(error);
            Id = id; Name = name; Kind = kind; InitialHp = initialHp; MaxHp = maxHp;
            MinimumCardCount = minimumCardCount;
            _pool = pool.Copy();
            var list = new List<ICharacterAbilityDefinition>(abilities ?? new ICharacterAbilityDefinition[0]);
            if (list.Contains(null)) throw new ArgumentException("能力定义不能为空。");
            Abilities = list.AsReadOnly();
        }

        public CharacterDefinition WithCardPool(CardPool pool)
        {
            return new CharacterDefinition(Id, Name, Kind, InitialHp, MaxHp, MinimumCardCount, pool, Abilities);
        }

        public static CharacterDefinition DefaultPlayer()
        {
            return new CharacterDefinition("player.default", "你", CharacterKind.Player, 4, 4, 8, CardPool.AllCards());
        }

        public static CharacterDefinition DefaultMonster()
        {
            return new CharacterDefinition("monster.default", "怪物", CharacterKind.Monster, 4, 4, 8, CardPool.AllCards());
        }
    }
}
