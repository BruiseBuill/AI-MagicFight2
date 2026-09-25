using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    internal static class BuiltinEffects
    {
        internal static void Register(EffectRegistry registry)
        {
            Register(registry, EffectOp.Haste, EffectStage.Cooldown, Cooldown, EffectFamily.Haste, EffectTargetShape.Card);
            Register(registry, EffectOp.HastePerHpLoss, EffectStage.Cooldown, Cooldown, EffectFamily.Haste, EffectTargetShape.Card);
            Register(registry, EffectOp.Slow, EffectStage.Cooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Card);
            Register(registry, EffectOp.SlowIfLastTwoHand, EffectStage.Cooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Card);
            Register(registry, EffectOp.SlowIfUnblocked, EffectStage.AfterCooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Card);
            Register(registry, EffectOp.HasteZone, EffectStage.Cooldown, Cooldown, EffectFamily.Haste, EffectTargetShape.Zone);
            Register(registry, EffectOp.SlowZone, EffectStage.Cooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Zone);
            Register(registry, EffectOp.Refresh, EffectStage.Cooldown, Refresh);
            Register(registry, EffectOp.ResetCooldown, EffectStage.Cooldown, Refresh);
            Register(registry, EffectOp.ReadyRefresh, EffectStage.Cooldown, Refresh);
            Register(registry, EffectOp.RemoveFromGame, EffectStage.Cooldown, Remove);
            Register(registry, EffectOp.CoolHandForAtk, EffectStage.Power, CoolHand);
            Register(registry, EffectOp.CoolHandForCombo, EffectStage.Power, CoolHand);
            Register(registry, EffectOp.CoolHandForHaste, EffectStage.Cooldown, CoolHand);
            Register(registry, EffectOp.Copy, EffectStage.Power, Copy);
            Register(registry, EffectOp.LookAndCool, EffectStage.Cooldown, Peek);
            Automatic(registry, EffectOp.SlowZoneBoth, EffectStage.Cooldown, (c, e) => {
                foreach (int seat in c.TargetSeats(e)) c.ChangeZone(seat, e.Arg("threshold"), false);
            });
            Automatic(registry, EffectOp.AtkPlus, EffectStage.Power, (c, e) => c.AddAttackPower(e.Arg("amount")));
            Automatic(registry, EffectOp.AtkPlusPerCooling, EffectStage.Power, (c, e) => c.AddAttackPower(c.Owner.CoolingZone.Count * e.Arg("amount")));
            Automatic(registry, EffectOp.AtkPlusPerHpLoss, EffectStage.Power, (c, e) => {
                int lost = 0; foreach (int seat in c.TargetSeats(e)) lost += c.State.Of(seat).HpLost;
                c.AddAttackPower(lost * e.Arg("amount"));
            });
            Automatic(registry, EffectOp.DoubleAtkBonus, EffectStage.Power, (c, e) => c.Attack.DoubleAtkBonus = true);
            Automatic(registry, EffectOp.NoAtkBuff, EffectStage.Power, (c, e) => c.Attack.NoAtkBuff = true);
            Automatic(registry, EffectOp.Combo, EffectStage.Power, (c, e) => c.GrantCombo());
            Automatic(registry, EffectOp.Double, EffectStage.Power, (c, e) => c.Attack.IsDouble = true);
            Automatic(registry, EffectOp.QuickRefill, EffectStage.Power, (c, e) => c.CooldownReduction += e.Arg("amount", 1) == 0 ? 1 : e.Arg("amount"));
            Automatic(registry, EffectOp.HealMinusMax, EffectStage.Power, (c, e) => {
                c.Heal(c.Seat, e.Arg("amount")); c.CutMaxHp(c.Seat, e.Arg("secondary"));
            });
            Automatic(registry, EffectOp.CoolMinusIfUnblocked, EffectStage.AfterCooldown, (c, e) => {
                if (!c.DefenseSucceeded) for (int i = 0; i < e.Arg("amount") && c.Source.IsCooling; i++) c.ChangeCooldown(c.Source, true);
            });
            Automatic(registry, EffectOp.ExtraDamageIfUnblocked, EffectStage.AfterCooldown, (c, e) => {
                if (c.DefenseSucceeded) return;
                c.Damage(c.OpponentSeat, e.Arg("amount"));
                if (!c.State.IsOver) c.CutMaxHp(c.OpponentSeat, e.Arg("secondary"));
            });
            Automatic(registry, EffectOp.Aura, EffectStage.Aura, (c, e) => {
                if (!c.Source.IsCooling) return;
                c.Source.AddAura(e);
                c.Emit(new AuraActivatedEvent { Seat = c.Seat, Card = c.Source, Tokens = 1 });
            });
            // Defense modifiers are queried before committing a legal defense, never applied twice.
            Automatic(registry, EffectOp.DefPlus, EffectStage.Defense, (c, e) => { });
            Automatic(registry, EffectOp.Guard, EffectStage.Defense, (c, e) => { });
        }
        private static void Register(EffectRegistry r, EffectOp op, EffectStage stage,
            Func<EffectContext, EffectDef, IEnumerable<EffectChoice>> execute,
            EffectFamily family = EffectFamily.None, EffectTargetShape shape = EffectTargetShape.None)
        { r.Register(op.ToString(), new EffectHandler(stage, execute, family, shape)); }
        private static void Automatic(EffectRegistry r, EffectOp op, EffectStage stage, Action<EffectContext, EffectDef> action)
        { Register(r, op, stage, (c, e) => Apply(c, e, action)); }
        private static IEnumerable<EffectChoice> Apply(EffectContext c, EffectDef e, Action<EffectContext, EffectDef> action)
        { action(c, e); yield break; }

        private static DecisionRequest Request(EffectContext c, RequestKind kind, string title, List<Option> options,
            bool optional = false, int maximum = 1, bool haste = false)
        {
            if (options.Count == 0) return null;
            if (optional) options.Add(new Option { Kind = OptionKind.Skip, Label = "跳过" + title });
            for (int i = 0; i < options.Count; i++) options[i].Index = i;
            return new DecisionRequest { Seat = c.Seat, Kind = kind, Title = title, Prompt = title,
                Options = options, MinSelect = optional ? 0 : 1, MaxSelect = maximum,
                ContextNoSkip = !optional, ContextHaste = haste };
        }
        private static Option CardOption(CardInstance card)
        { return new Option { Kind = OptionKind.ChooseCard, Seat = card.OwnerSeat, Card = card, Label = card.Def.Name, Value = card.RemainingCooldown }; }
        private static Option Pick(EffectChoice choice)
        {
            if (choice.Selected != null) foreach (Option option in choice.Selected) if (!option.IsSkip) return option;
            return null;
        }

        private static IEnumerable<EffectChoice> Cooldown(EffectContext c, EffectDef e)
        {
            bool haste = e.Op == EffectOp.Haste || e.Op == EffectOp.HasteZone || e.Op == EffectOp.HastePerHpLoss;
            bool zone = e.Op == EffectOp.HasteZone || e.Op == EffectOp.SlowZone;
            int count = e.Arg("count", 1);
            if (e.Op == EffectOp.HastePerHpLoss) count *= c.Owner.HpLost;
            string title = (zone ? "区域" : "") + (haste ? "加速" : "减速");
            for (int n = 0; n < count; n++)
            {
                var choice = new EffectChoice(() => {
                    var options = new List<Option>();
                    foreach (int seat in c.TargetSeats(e))
                    {
                        var values = new HashSet<int>();
                        foreach (CardInstance card in c.State.Of(seat).CoolingZone)
                        {
                            if (c.WasSelected(e.DistinctTargetGroup, card) || !haste && card.RemainingCooldown >= card.Def.Cooldown) continue;
                            if (zone)
                            {
                                if (values.Add(card.RemainingCooldown)) options.Add(new Option { Kind = OptionKind.ZoneValue,
                                    Seat = seat, Value = card.RemainingCooldown, Label = title + " " + card.RemainingCooldown });
                            }
                            else options.Add(CardOption(card));
                        }
                    }
                    return Request(c, zone ? RequestKind.ChooseZoneValue : haste ? RequestKind.ChooseHasteTarget : RequestKind.ChooseSlowTarget,
                        title, options, e.CanSkip, haste: haste);
                });
                yield return choice;
                Option pick = Pick(choice);
                if (pick == null) yield break;
                if (zone) c.ChangeZone(pick.Seat, pick.Value, haste);
                else { c.Remember(e.DistinctTargetGroup, pick.Card); c.ChangeCooldown(pick.Card, haste); }
            }
        }
        private static IEnumerable<EffectChoice> Refresh(EffectContext c, EffectDef e)
        {
            bool reset = e.Op == EffectOp.ResetCooldown;
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                IEnumerable<int> seats = e.Op == EffectOp.ReadyRefresh ? new[] { c.Seat } : c.TargetSeats(e);
                foreach (int seat in seats)
                {
                    if (reset && e.Arg("secondary") == 1 && seat != c.OpponentSeat) continue;
                    foreach (CardInstance card in c.State.Of(seat).CoolingZone)
                        if ((!reset || card.RemainingCooldown < card.Def.Cooldown) && (e.Op != EffectOp.ReadyRefresh || card != c.Source)) options.Add(CardOption(card));
                }
                return Request(c, RequestKind.ChooseRefreshTarget, reset ? "重置冷却" : "立即冷却完成", options, haste: !reset);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick != null) c.Refresh(pick.Card, reset);
        }
        private static IEnumerable<EffectChoice> Remove(EffectContext c, EffectDef e)
        {
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                foreach (CardInstance card in c.Owner.CoolingZone) options.Add(CardOption(card));
                return Request(c, RequestKind.ChooseRemoveFromGame, "移出·获加速", options);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick == null || !c.RemoveCard(pick.Card)) yield break;
            foreach (EffectChoice next in Cooldown(c, new EffectDef(c.Trigger, EffectOp.Haste, e.Arg("count")))) yield return next;
        }
        private static IEnumerable<EffectChoice> CoolHand(EffectContext c, EffectDef e)
        {
            bool single = e.Op == EffectOp.CoolHandForCombo;
            string title = single ? "冷却·换连击" : e.Op == EffectOp.CoolHandForAtk ? "冷却·加力量" : "冷却·获加速";
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                foreach (CardInstance card in c.Owner.Hand) if (card != c.Source) options.Add(CardOption(card));
                return Request(c, RequestKind.ChooseCoolHandCards, title, options, maximum: single ? 1 : options.Count);
            });
            yield return choice;
            int count = 0;
            foreach (Option option in choice.Selected)
                if (option.Card != null && c.Owner.Hand.Contains(option.Card) && c.CoolCard(option.Card)) count++;
            if (e.Op == EffectOp.CoolHandForAtk) c.AddAttackPower(count * e.Arg("amount"));
            else if (single) { if (count > 0) c.GrantCombo(); }
            else foreach (EffectChoice next in Cooldown(c, new EffectDef(c.Trigger, EffectOp.Haste, count * e.Arg("amount")))) yield return next;
        }
        private static IEnumerable<EffectChoice> Copy(EffectContext c, EffectDef e)
        {
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                foreach (CardInstance card in c.Owner.CoolingZone)
                    if (card.Def.Cooldown <= e.Arg("threshold") && !card.Def.HasAura) options.Add(CardOption(card));
                return Request(c, RequestKind.ChooseCopyTarget, "复制力量与效果", options);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick != null) c.CopyAttack(pick.Card.Def);
        }
        private static IEnumerable<EffectChoice> Peek(EffectContext c, EffectDef e)
        {
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                var hand = c.State.Of(c.OpponentSeat).Hand;
                for (int i = 0; i < hand.Count; i++) options.Add(new Option { Kind = OptionKind.ChooseCard,
                    Seat = c.OpponentSeat, Card = hand[i], Value = i, Label = "翻开第 " + (i + 1) + " 张" });
                return Request(c, RequestKind.ChoosePeekCard, "查看对方手牌", options);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick == null) yield break;
            bool hit = e.Arg("secondary") == EffectDef.LookAtMost ? pick.Card.Def.Power <= e.Arg("threshold") : pick.Card.Def.Power >= e.Arg("threshold");
            bool cooled = hit && c.CoolCard(pick.Card, e.Arg("cooldownAdjustment"));
            c.Emit(new HandRevealedEvent { ViewerSeat = c.Seat, OwnerSeat = c.OpponentSeat, Card = pick.Card, Cooled = cooled, SlotIndex = pick.Value });
        }
    }
}
