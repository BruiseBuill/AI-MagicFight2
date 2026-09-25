using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    public interface ICardCatalog
    {
        IReadOnlyList<CardDef> All { get; }
        CardDef Get(string id);
    }

    /// <summary>A content snapshot. A running battle keeps its definitions when assets are edited.</summary>
    public sealed class CardCatalog : ICardCatalog
    {
        private readonly Dictionary<string, CardDef> _byId = new Dictionary<string, CardDef>(StringComparer.Ordinal);
        public IReadOnlyList<CardDef> All { get; private set; }
        public CardCatalog(IEnumerable<CardDef> definitions)
        {
            var list = new List<CardDef>();
            foreach (CardDef definition in definitions)
            {
                if (definition == null) throw new ArgumentException("Null card definition.");
                _byId.Add(definition.Id, definition); list.Add(definition);
            }
            list.Sort((a, b) => {
                int index = a.Index.CompareTo(b.Index);
                return index != 0 ? index : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            All = list.AsReadOnly();
        }
        public CardDef Get(string id)
        {
            if (id != null && _byId.TryGetValue(id, out var definition)) return definition;
            throw new ArgumentException("Unknown card ID: " + id);
        }
        public static CardCatalog Builtin() { return new CardCatalog(CardLibrary.All); }
    }
}
