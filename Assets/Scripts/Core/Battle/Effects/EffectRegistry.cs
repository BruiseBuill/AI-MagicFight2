using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    public enum EffectStage { Power = 1, Defense = 2, Cooldown = 3, AfterCooldown = 4, Aura = 5 }
    public enum EffectFamily { None, Haste, Slow }
    public enum EffectTargetShape { None, Card, Zone }
    public enum EffectTargetScope { Participants, Self, Opponent }

    public sealed class EffectCondition
    {
        public readonly string Id;
        public readonly int Value;
        public EffectCondition(string id, int value = 0) { Id = id; Value = value; }
    }

    // Implementations are stateless. All per-resolution state belongs to the returned iterator.
    public interface IEffectHandler
    {
        EffectStage Stage { get; }
        EffectFamily Family { get; }
        EffectTargetShape Shape { get; }
        IEnumerable<EffectChoice> Execute(EffectContext context, EffectDef effect);
    }

    public sealed class EffectHandler : IEffectHandler
    {
        private readonly Func<EffectContext, EffectDef, IEnumerable<EffectChoice>> _execute;
        public EffectStage Stage { get; private set; }
        public EffectFamily Family { get; private set; }
        public EffectTargetShape Shape { get; private set; }
        public EffectHandler(EffectStage stage, Func<EffectContext, EffectDef, IEnumerable<EffectChoice>> execute,
            EffectFamily family = EffectFamily.None, EffectTargetShape shape = EffectTargetShape.None)
        { Stage = stage; _execute = execute ?? throw new ArgumentNullException(nameof(execute)); Family = family; Shape = shape; }
        public IEnumerable<EffectChoice> Execute(EffectContext context, EffectDef effect) { return _execute(context, effect); }
    }

    public sealed class EffectRegistry
    {
        private readonly Dictionary<string, IEffectHandler> _handlers = new Dictionary<string, IEffectHandler>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<EffectContext, int, bool>> _conditions = new Dictionary<string, Func<EffectContext, int, bool>>(StringComparer.Ordinal);
        private bool _frozen;
        public static EffectRegistry CreateDefault()
        {
            var registry = new EffectRegistry();
            registry.RegisterCondition("hand-at-play", (c, n) => c.HandCountAtPlay == n);
            registry.RegisterCondition("unblocked", (c, n) => !c.DefenseSucceeded);
            registry.RegisterCondition("hp-lost-at-least", (c, n) => c.Owner.HpLost >= n);
            BuiltinEffects.Register(registry);
            return registry;
        }
        public void Register(string id, IEffectHandler handler)
        {
            if (_frozen) throw new InvalidOperationException("The battle effect registry is frozen.");
            if (string.IsNullOrWhiteSpace(id) || handler == null) throw new ArgumentException("Invalid effect handler.");
            _handlers.Add(id, handler);
        }
        public void RegisterCondition(string id, Func<EffectContext, int, bool> condition)
        {
            if (_frozen) throw new InvalidOperationException("The battle effect registry is frozen.");
            _conditions.Add(id, condition ?? throw new ArgumentNullException(nameof(condition)));
        }
        internal void Freeze() { _frozen = true; }
        public IEffectHandler Get(string id)
        {
            if (!_handlers.TryGetValue(id, out var handler)) throw new ArgumentException("Unregistered effect: " + id);
            return handler;
        }
        public void Validate(EffectDef effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            Get(effect.HandlerId);
            if (effect.Trigger == EffectTrigger.Special && string.IsNullOrWhiteSpace(effect.SpecialEvent))
                throw new ArgumentException("A special effect must specify its event.");
            foreach (EffectCondition condition in effect.Conditions)
                if (!_conditions.ContainsKey(condition.Id)) throw new ArgumentException("Unregistered condition: " + condition.Id);
        }
        internal bool Matches(EffectContext context, EffectDef effect)
        {
            foreach (EffectCondition condition in effect.Conditions)
                if (!_conditions[condition.Id](context, condition.Value)) return false;
            return true;
        }
    }

    public sealed class EffectChoice
    {
        private readonly Func<DecisionRequest> _build;
        public IReadOnlyList<Option> Selected { get; internal set; }
        public EffectChoice(Func<DecisionRequest> build) { _build = build ?? throw new ArgumentNullException(nameof(build)); }
        internal DecisionRequest Build() { return _build(); }
    }
}
