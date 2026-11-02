using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    public sealed class EffectContext
    {
        private readonly BattleEngine _engine;
        internal readonly AttackContext Attack;
        private readonly Dictionary<string, HashSet<int>> _selected = new Dictionary<string, HashSet<int>>();
        public CardInstance Source { get; private set; }
        public int Seat { get { return Source.OwnerSeat; } }
        public int OpponentSeat { get; private set; }
        public EffectTrigger Trigger { get; private set; }
        public PlayerState Owner { get { return State.Of(Seat); } }
        public BattleState State { get { return _engine.State; } }
        public int HandCountAtPlay { get; private set; }
        public bool DefenseSucceeded { get { return _engine.EffectDefenseSucceeded; } }
        internal int CooldownReduction;
        internal EffectContext(BattleEngine engine, CardInstance source, int opponent, EffectTrigger trigger,
            AttackContext attack, int handCount)
        { _engine = engine; Source = source; OpponentSeat = opponent; Trigger = trigger; Attack = attack; HandCountAtPlay = handCount; }

        public IReadOnlyList<int> TargetSeats(EffectDef effect)
        {
            if (effect.Targets == EffectTargetScope.Self) return new[] { Seat };
            if (effect.Targets == EffectTargetScope.Opponent) return new[] { OpponentSeat };
            return State.Mode.EffectSeats(State, Seat, OpponentSeat);
        }
        public bool WasSelected(string group, CardInstance card)
        { return !string.IsNullOrEmpty(group) && _selected.TryGetValue(group, out var ids) && ids.Contains(card.Uid); }
        public void Remember(string group, CardInstance card)
        {
            if (string.IsNullOrEmpty(group)) return;
            if (!_selected.TryGetValue(group, out var ids)) _selected.Add(group, ids = new HashSet<int>());
            ids.Add(card.Uid);
        }
        public void ChangeCooldown(CardInstance target, bool haste, string reason = null)
        {
            var changes = new List<CooldownChange>();
            if (haste) CooldownOps.TryHaste(State.Of(target.OwnerSeat), target, changes, reason);
            else CooldownOps.TrySlow(target, changes, reason);
            _engine.EffectFlush(changes);
        }
        public void ChangeZone(int seat, int value, bool haste)
        {
            var changes = new List<CooldownChange>();
            if (haste) CooldownOps.HasteZone(State.Of(seat), value, changes);
            else CooldownOps.SlowZone(State.Of(seat), value, changes);
            _engine.EffectFlush(changes);
        }
        public void Refresh(CardInstance target, bool reset = false)
        {
            var changes = new List<CooldownChange>();
            if (reset) CooldownOps.ResetToBase(target, changes);
            else CooldownOps.Refresh(State.Of(target.OwnerSeat), target, changes);
            _engine.EffectFlush(changes);
        }
        /// <param name="reason">
        /// 写进 <see cref="CooldownChange.Reason"/>。战报靠它区分「弃牌换资源」与
        /// 「查看后送入冷却」这两种同样走 <see cref="CooldownOps.PutIntoCooldown"/> 的效果 ——
        /// 不传就是引擎默认的「效果送入冷却区」，战报会当成无名入场而不记。
        /// </param>
        public bool CoolCard(CardInstance target, int adjustment = 0, string reason = null)
        {
            var changes = new List<CooldownChange>();
            bool changed = CooldownOps.PutIntoCooldown(State.Of(target.OwnerSeat), target,
                Math.Max(0, target.Def.Cooldown + adjustment), changes,
                string.IsNullOrEmpty(reason) ? "效果送入冷却区" : reason);
            _engine.EffectFlush(changes);
            return changed;
        }
        public bool RemoveCard(CardInstance target)
        {
            if (!CooldownOps.RemoveFromGame(State.Of(target.OwnerSeat), target)) return false;
            Emit(new CardRemovedEvent { Seat = target.OwnerSeat, Card = target });
            return true;
        }
        public void Heal(int seat, int amount) { _engine.EffectHeal(seat, amount); }
        public void Damage(int seat, int amount) { _engine.EffectDamage(seat, amount, Source.Def.Name); }
        public void CutMaxHp(int seat, int amount) { _engine.EffectCutMaxHp(seat, amount); }
        public void Emit(BattleEvent battleEvent) { _engine.EffectEmit(battleEvent); }
        public void CopyAttack(CardDef definition) { _engine.EffectCopy(this, definition); }
        public void AddAttackPower(int amount)
        {
            if (Attack == null || Trigger != EffectTrigger.Attack) throw new InvalidOperationException("Attack power requires an attack context.");
            Attack.EffectBonus += amount;
        }
        public void GrantCombo() { if (Attack != null && Trigger == EffectTrigger.Attack) Attack.HasCombo = true; }
        public CardInstance GenerateCard(CardDef definition, int seat)
        { return _engine.GenerateCard(definition, seat); }
        public void Publish(string eventId, CardInstance source) { _engine.PublishEffectEvent(eventId, source); }
    }
}
