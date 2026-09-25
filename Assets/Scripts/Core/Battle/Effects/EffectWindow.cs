using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    internal sealed class EffectActivation
    {
        public EffectDef Definition;
        public EffectContext Context;
    }

    internal sealed class EffectWindow : IDisposable
    {
        private sealed class Work
        {
            public int Id;
            public EffectActivation Activation;
            public IEffectHandler Handler;
            public IEnumerator<EffectChoice> Iterator;
            public EffectChoice Choice;
            public bool Done;
        }
        private readonly EffectRegistry _registry;
        private readonly List<Work> _work = new List<Work>();
        private readonly List<Work> _group = new List<Work>();
        private readonly Dictionary<int, Tuple<Work, Option>> _routes = new Dictionary<int, Tuple<Work, Option>>();
        private DecisionRequest _request;
        public bool Complete { get; private set; }

        public EffectWindow(EffectRegistry registry, IEnumerable<EffectActivation> effects)
        { _registry = registry; foreach (var effect in effects) Add(effect); }

        public void Add(EffectActivation activation)
        {
            if (_work.Count >= 512) throw new InvalidOperationException("Effect expansion exceeded 512 entries.");
            _registry.Validate(activation.Definition);
            _work.Add(new Work { Id = _work.Count + 1, Activation = activation, Handler = _registry.Get(activation.Definition.HandlerId) });
            Complete = false;
        }

        // One iterator step per tick lets the engine drain nested events before presenting another choice.
        public DecisionRequest Tick()
        {
            if (_group.Count == 0)
            {
                Work first = _work.Find(w => !w.Done);
                if (first == null) { Complete = true; return null; }
                _group.Add(first);
                if (first.Handler.Family != EffectFamily.None && first.Handler.Shape != EffectTargetShape.None)
                {
                    bool complementary = _work.Exists(w => !w.Done && w != first && SameGroup(first, w)
                        && w.Handler.Shape != first.Handler.Shape && w.Handler.Shape != EffectTargetShape.None);
                    if (complementary)
                        foreach (Work candidate in _work)
                            if (candidate != first && !candidate.Done && SameGroup(first, candidate)) _group.Add(candidate);
                }
            }
            foreach (Work work in _group)
            {
                if (work.Done || work.Choice != null && work.Choice.Selected == null) continue;
                if (work.Iterator == null)
                {
                    if (!_registry.Matches(work.Activation.Context, work.Activation.Definition)) { work.Done = true; return null; }
                    work.Iterator = work.Handler.Execute(work.Activation.Context, work.Activation.Definition).GetEnumerator();
                }
                if (!work.Iterator.MoveNext()) { work.Done = true; work.Iterator.Dispose(); work.Iterator = null; work.Choice = null; }
                else work.Choice = work.Iterator.Current ?? throw new InvalidOperationException("Effect yielded a null choice.");
                return null;
            }
            _routes.Clear();
            var options = new List<Option>();
            DecisionRequest template = null;
            int live = 0;
            foreach (Work work in _group)
            {
                if (work.Done) continue;
                DecisionRequest request = work.Choice.Build();
                bool hasTarget = false;
                if (request != null)
                    foreach (Option option in request.Options) if (!option.IsSkip) { hasTarget = true; break; }
                // Keep a currently unavailable sibling dormant while another action can change its targets.
                if (!hasTarget) continue;
                live++;
                if (template == null) template = request;
                foreach (Option option in request.Options)
                {
                    Option copy = option.Copy();
                    copy.Index = options.Count;
                    copy.EffectExecutionId = work.Id;
                    options.Add(copy);
                    _routes.Add(copy.Index, Tuple.Create(work, option));
                }
            }
            if (live == 0)
            {
                foreach (Work work in _group)
                    if (!work.Done) { work.Choice.Selected = new Option[0]; }
                if (_group.TrueForAll(w => w.Done)) _group.Clear();
                return null;
            }
            bool combined = _group.Count > 1;
            _request = new DecisionRequest {
                Kind = combined ? RequestKind.ChooseCooldownEffects : template.Kind,
                Seat = template.Seat, Title = template.Title,
                Prompt = combined ? (template.ContextHaste ? "加速 / 区域加速" : "减速 / 区域减速") : template.Prompt,
                Options = options, MinSelect = combined ? 1 : template.MinSelect,
                MaxSelect = combined ? 1 : template.MaxSelect,
                ContextHaste = template.ContextHaste, ContextNoSkip = template.ContextNoSkip
            };
            return _request;
        }

        private static bool SameGroup(Work first, Work other)
        {
            return first.Handler.Family == other.Handler.Family
                && ReferenceEquals(first.Activation.Context, other.Activation.Context)
                && first.Activation.Definition.Trigger == other.Activation.Definition.Trigger;
        }

        public bool Submit(DecisionResponse response)
        {
            if (_request == null || response.Seat != _request.Seat) return false;
            var selected = new List<Option>();
            Work destination = null;
            var seen = new HashSet<int>();
            foreach (int index in response.OptionIndices ?? new int[0])
            {
                if (!seen.Add(index) || !_routes.TryGetValue(index, out var route)) return false;
                if (destination != null && destination != route.Item1) return false;
                destination = route.Item1;
                selected.Add(route.Item2);
            }
            if (selected.Count > _request.MaxSelect || selected.Count < _request.MinSelect) return false;
            if (selected.Exists(o => o.IsSkip) && selected.Count != 1) return false;
            if (destination == null)
            {
                if (_group.Count != 1 || _request.MinSelect != 0) return false;
                destination = _group[0];
            }
            destination.Choice.Selected = selected.AsReadOnly();
            _request = null;
            return true;
        }
        public void Dispose()
        {
            foreach (Work work in _work) { work.Iterator?.Dispose(); work.Iterator = null; }
        }
    }
}
