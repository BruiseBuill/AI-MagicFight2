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
                var candidates = _work.FindAll(w => !w.Done && w.Iterator == null
                    && ReferenceEquals(w.Activation.Context, first.Activation.Context)
                    && _registry.Matches(w.Activation.Context, w.Activation.Definition));
                Work constraint = candidates.Find(w => w.Activation.Definition.IsConstraint);
                if (constraint != null) first = constraint;
                // 2026-10-31 用户口径（吞食）：同卡同触发、混有无 family 冷却效果的一组，
                // 不再弹「选择结算顺序」（ResolveOrder），按卡面书写顺序逐条结算 ——
                // 吞食 = 先减速、再随机查看。44 张里只有吞食（含模仿复制吞食）走得到这条路径；
                // 「点区域 / 点卡片定先后」的合并拍（暴风雪等）不受影响。
                _group.Add(first);
                if (first.Handler.Family != EffectFamily.None && first.Handler.Shape != EffectTargetShape.None)
                {
                    bool complementary = _work.Exists(w => !w.Done && w != first && Joinable(first, w)
                        && w.Handler.Shape != first.Handler.Shape && w.Handler.Shape != EffectTargetShape.None);
                    if (complementary)
                        foreach (Work candidate in _work)
                            if (candidate != first && !candidate.Done && Joinable(first, candidate)) _group.Add(candidate);
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
            bool skipAdded = false;
            int live = 0;
            foreach (Work work in _group)
            {
                if (work.Done || work.Choice == null) continue;
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

                    // 并列时两条效果各带一个「跳过」—— 合并成一个入口（点它 = 整组都不执行，
                    // 见 Submit）。两个「跳过」按钮并排只会让人以为它们有区别。
                    if (copy.IsSkip)
                    {
                        if (skipAdded) continue;
                        skipAdded = true;
                    }

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
            string prompt = combined ? CombinedPrompt() : template.Prompt;
            _request = new DecisionRequest {
                Kind = combined ? RequestKind.ChooseCooldownEffects : template.Kind,
                Seat = template.Seat, Title = combined ? prompt : template.Title, Prompt = prompt,
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

        /// <summary>「区域 + 单体」互补对：同一张牌、同一触发、family 不同、形状一条 Card 一条 Zone。</summary>
        private static bool ZoneCardPair(Work first, Work other)
        {
            return ReferenceEquals(first.Activation.Context, other.Activation.Context)
                && first.Activation.Definition.Trigger == other.Activation.Definition.Trigger
                && first.Handler.Stage == EffectStage.Cooldown && other.Handler.Stage == EffectStage.Cooldown
                && first.Handler.Family != EffectFamily.None && other.Handler.Family != EffectFamily.None
                && first.Handler.Family != other.Handler.Family
                && first.Handler.Shape != EffectTargetShape.None && other.Handler.Shape != EffectTargetShape.None
                && first.Handler.Shape != other.Handler.Shape;
        }

        private static bool Joinable(Work first, Work other)
        {
            return SameGroup(first, other) || ZoneCardPair(first, other);
        }

        /// <summary>
        /// 并列拍的那行提示，按<b>组里真实存在的效果</b>拼（不再拿 template 的极性猜）。
        ///
        /// <para>旧写法是 <c>template.ContextHaste ? "加速 / 区域加速" : "减速 / 区域减速"</c>
        /// —— 暴风雪（加速 + 区域<b>减速</b>）会念成「加速 / 区域加速」，教玩家点错。</para>
        /// </summary>
        private string CombinedPrompt()
        {
            bool cardHaste = false, cardSlow = false, zoneHaste = false, zoneSlow = false;
            for (int i = 0; i < _group.Count; i++)
            {
                Work work = _group[i];
                if (work.Done || work.Choice == null || work.Choice.Selected != null) continue;
                bool zone = work.Handler.Shape == EffectTargetShape.Zone;
                bool haste = work.Handler.Family == EffectFamily.Haste;
                if (zone) { if (haste) zoneHaste = true; else zoneSlow = true; }
                else { if (haste) cardHaste = true; else cardSlow = true; }
            }

            var parts = new List<string>();
            if (cardHaste) parts.Add("加速");
            if (cardSlow) parts.Add("减速");
            if (zoneHaste) parts.Add("区域加速");
            if (zoneSlow) parts.Add("区域减速");
            return parts.Count == 0 ? "冷却效果" : string.Join(" / ", parts.ToArray());
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

            // 并列拍只有一个「跳过」入口 —— 点它就是这一整组都不执行（两条各点一次太多余）。
            if (_group.Count > 1 && selected.Count == 1 && selected[0].IsSkip)
            {
                DropGroup();
                _request = null;
                return true;
            }

            if (destination == null)
            {
                if (_group.Count != 1 || _request.MinSelect != 0) return false;
                destination = _group[0];
            }
            destination.Choice.Selected = selected.AsReadOnly();
            _request = null;
            return true;
        }

        /// <summary>整组都不执行（玩家点了并列拍唯一的那个「跳过」）。</summary>
        private void DropGroup()
        {
            for (int i = 0; i < _group.Count; i++)
            {
                Work work = _group[i];
                if (work.Done) continue;
                work.Done = true;
                if (work.Iterator != null) { work.Iterator.Dispose(); work.Iterator = null; }
                work.Choice = null;
            }

            _group.Clear();
        }

        public void Dispose()
        {
            foreach (Work work in _work) { work.Iterator?.Dispose(); work.Iterator = null; }
        }
    }
}
