using System;
using System.Collections.Generic;
using System.Linq;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    internal static class RuleAlignmentScenario
    {
        public static bool Run(List<string> report)
        {
            bool ok = true;
            var cases = new Dictionary<string, Action> {
                { "initial skip costs life; combo skip is free and preserves auras", ComboSkip },
                { "human/AI turn limits use controller seats and emit one terminal event", TurnLimits },
                { "draw observes four full future turns; proposals do not consume actions", DrawTiming },
                { "life loss cancels draw; healing and max-only loss do not", DrawCancellation },
                { "automatic effects resolve without confirmation; constraints remain active", AutomaticEffects },
                { "tidal refresh can target either side and can be skipped", TidalTargets },
                { "vine slow resolves before attacking and defending cards enter cooldown", VineTiming },
                { "Unity duel deal profile is exercised for both participants", DuelDeal }
            };
            foreach (var test in cases)
            {
                try { test.Value(); report.Add("  [PASS] " + test.Key); }
                catch (Exception e) { ok = false; report.Add("  [FAIL] " + test.Key + ": " + e.Message); }
            }
            return ok;
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static BattleEngine New(ControlKind first = ControlKind.Human, ControlKind second = ControlKind.Human)
        {
            var character = CharacterDefinition.DefaultPlayer().WithDeal(DealProfile.Duel());
            var engine = BattleEngine.Create(731, new BattleSetup(new[] {
                new ParticipantSetup(character, first, 0), new ParticipantSetup(character, second, 1)
            }));
            engine.Start(); engine.Advance();
            return engine;
        }

        private static CardInstance Hand(BattleEngine engine, int seat, CardDef def)
        {
            var card = engine.State.CreateCard(def, seat);
            card.ToHand(); engine.State.Of(seat).Hand.Add(card); return card;
        }

        private static CardInstance Cooling(BattleEngine engine, int seat, string id, int remaining)
        {
            var card = engine.State.CreateCard(CardLibrary.Get(id), seat);
            CooldownOps.PutIntoCooldown(engine.State.Of(seat), card, remaining, null); return card;
        }

        private static BattleEngine Fixture(string[] first, string[] second)
        {
            var engine = New();
            for (int seat = 0; seat < 2; seat++)
            {
                engine.State.Of(seat).Hand.Clear();
                foreach (string id in seat == 0 ? first : second) Hand(engine, seat, CardLibrary.Get(id));
            }
            Until(engine, r => r.Kind == RequestKind.ChooseAttackCard);
            return engine;
        }

        private static void Respond(BattleEngine engine, Option option)
        {
            var request = engine.Pending;
            engine.Submit(new DecisionResponse { Seat = request.Seat, RequestId = request.RequestId,
                OptionIndices = new[] { option.Index } });
            engine.Advance();
        }

        private static void Accept(BattleEngine engine)
        {
            var request = engine.Pending;
            Option option;
            if (request.Kind == RequestKind.ChooseReplace)
                option = request.Options.First(o => o.Kind == OptionKind.Done);
            else option = request.Options.FirstOrDefault(o => !o.IsSkip && o.Kind != OptionKind.ProposeDraw)
                ?? request.SkipOption;
            Check(option != null, "No response available");
            Respond(engine, option);
        }

        private static void Until(BattleEngine engine, Func<DecisionRequest, bool> stop)
        {
            engine.Advance();
            for (int i = 0; i < 5000 && !engine.IsOver; i++)
            {
                Check(engine.Pending != null, "Missing decision");
                if (stop(engine.Pending)) return;
                Accept(engine);
            }
            Check(engine.IsOver, "Decision loop");
        }

        private static void Play(BattleEngine engine, string id)
        { Respond(engine, engine.Pending.Options.First(o => o.Kind == OptionKind.PlayCard && o.Card.Def.Id == id)); }

        private static void ComboSkip()
        {
            var engine = Fixture(new[] { "j", "f" }, new[] { "f", "g" });
            var aura = Cooling(engine, 0, "ai", 3);
            aura.AddAura(aura.Def.Effects[0]);
            Play(engine, "j");
            Until(engine, r => r.Kind == RequestKind.ChooseAttackCard && r.Seat == 0);
            Check(engine.State.InCombo && engine.Pending.SkipOption.Value == 0, "Not a free follow-up");
            int hp = engine.State.Of(0).Hp;
            Respond(engine, engine.Pending.SkipOption);
            Check(engine.State.Of(0).Hp == hp && aura.AuraTokens == 1, "Combo skip consumed resources");
            engine = Fixture(new[] { "f" }, new[] { "f" });
            Check(engine.Pending.SkipOption.Value == 1, "Initial skip cost missing");
            Respond(engine, engine.Pending.SkipOption);
            Check(engine.State.Of(0).Hp == 3, "Initial skip must lose one life");
        }

        // Identical low-cooldown cards produce a deterministic stalemate without mutating turn counters.
        private static BattleEngine Stalemate(ControlKind first, ControlKind second)
        {
            var engine = New(first, second);
            var plain = new CardDef(0, "fixture", "Fixture", 5, 1, new EffectDef[0]);
            for (int seat = 0; seat < 2; seat++)
            {
                engine.State.Of(seat).Hand.Clear();
                for (int i = 0; i < 6; i++) Hand(engine, seat, plain);
            }
            Until(engine, r => r.Kind == RequestKind.ChooseAttackCard);
            return engine;
        }

        private static void TurnLimits()
        {
            foreach (int human in new[] { 0, 1 })
            {
                var engine = Stalemate(human == 0 ? ControlKind.Human : ControlKind.Ai,
                    human == 1 ? ControlKind.Human : ControlKind.Ai);
                int terminalEvents = 0;
                engine.OnEvent += e => { if (e is GameOverEvent) terminalEvents++; };
                Check(engine.MaxTurns == 40, "Human/AI limit is not 40");
                Until(engine, r => false);
                Check(engine.State.TurnNumber == 40 && engine.State.WinnerSeat == 1 - human, "Wrong limit outcome");
                Check(terminalEvents == 1, "Missing or duplicate terminal event");
            }
            var ai = Stalemate(ControlKind.Ai, ControlKind.Ai);
            Until(ai, r => false);
            Check(ai.State.TurnNumber == 100 && ai.State.WinnerSeat == -1, "AI safety cap changed");
        }

        private static void Propose(BattleEngine engine)
        { Respond(engine, engine.Pending.Options.First(o => o.Kind == OptionKind.ProposeDraw)); }

        private static void DrawTiming()
        {
            var engine = Stalemate(ControlKind.Human, ControlKind.Human);
            int count = engine.State.Of(0).Hand.Count;
            Propose(engine);
            Check(engine.Pending.Kind == RequestKind.ChooseAttackCard && engine.State.Of(0).Hand.Count == count,
                "Proposal consumed attack");
            Check(engine.DrawObservationStartTurn == 2 && !engine.Pending.Options.Any(o => o.Kind == OptionKind.ProposeDraw),
                "Wrong start or duplicate proposal");
            Until(engine, r => false);
            Check(engine.State.TurnNumber == 5 && engine.State.WinnerSeat == -1, "Four full turns were not observed");

            engine = Stalemate(ControlKind.Human, ControlKind.Human);
            Accept(engine);
            Check(engine.Pending.Kind == RequestKind.ChooseDefense, "Expected defense");
            Propose(engine);
            Check(engine.Pending.Kind == RequestKind.ChooseDefense, "Proposal consumed defense");
            Respond(engine, engine.Pending.SkipOption);
            Check(engine.DrawObservationStartTurn == 0, "Defense damage did not cancel observation");
            Propose(engine);
            Check(engine.DrawObservationStartTurn == 2, "Cannot repropose after cancellation");
        }

        private static void DrawCancellation()
        {
            var engine = Stalemate(ControlKind.Human, ControlKind.Human);
            Propose(engine);
            engine.EffectHeal(0, 1);
            Check(engine.DrawObservationStartTurn == 2, "Healing cancelled observation");
            engine.State.Of(0).Hp = 2;
            engine.EffectCutMaxHp(0, 1);
            Check(engine.DrawObservationStartTurn == 2, "Max-only change cancelled observation");
            engine.EffectCutMaxHp(0, 2);
            Check(engine.State.Of(0).Hp == 1 && engine.DrawObservationStartTurn == 0, "Clamp failed to cancel");
            engine.Submit(DecisionResponse.PrepAuras(engine.Pending.Seat, new int[0])); engine.Advance();
            Propose(engine);
            engine.EffectDamage(1, 1, "self damage");
            Check(engine.DrawObservationStartTurn == 0, "Damage failed to cancel");
            engine.Submit(DecisionResponse.PrepAuras(engine.Pending.Seat, new int[0])); engine.Advance();
            Propose(engine);
            Respond(engine, engine.Pending.SkipOption);
            Check(engine.DrawObservationStartTurn == 0, "No-attack loss failed to cancel");
        }

        private static void AutomaticEffects()
        {
            foreach (string id in new[] { "g", "ai", "ac", "j", "ar" })
            {
                var engine = Fixture(new[] { id }, new[] { "f", "g" });
                var played = engine.State.Of(0).Hand[0];
                Play(engine, id);
                Check(engine.Pending.Kind == RequestKind.ChooseDefense, "Unexpected confirmation");
                Until(engine, r => r.Kind == RequestKind.ChooseAttackCard && r.Seat == 1);
                if (id == "g") Check(played.RemainingCooldown == 3, "Quick refill did not trigger");
                if (id == "ai") Check(played.AuraTokens == 1, "Aura did not trigger");
                if (id == "ar") Check(played.Zone == CardZone.Hand, "Last-card return did not trigger");
            }
            var forced = Fixture(new[] { "o", "f" }, new[] { "f" });
            forced.State.Of(0).Hp = 2;
            Play(forced, "o");
            Check(forced.State.Of(0).Hp == 3 && forced.State.Of(0).MaxHp == 3, "Forced heal did not run");
            Check(forced.Pending.Kind == RequestKind.ChooseDefense, "Unexpected combo confirmation");
            var guard = Fixture(new[] { "f" }, new[] { "b", "g" });
            Play(guard, "f");
            Until(guard, r => r.Kind == RequestKind.ChooseDefense);
            Play(guard, "b");
            Check(guard.State.Of(1).Hp == 4, "Guard did not defend automatically");
        }

        private static void TidalTargets()
        {
            var engine = Fixture(new[] { "f" }, new[] { "g" });
            var tide = Hand(engine, 0, CardLibrary.Get("e"));
            var own = Cooling(engine, 0, "a", 2);
            var enemy = Cooling(engine, 1, "c", 1);
            engine.PublishEffectEvent("cooldown.completed", tide);
            // The current attack decision completes before the queued special event.
            Play(engine, "f");
            Check(engine.Pending.Kind == RequestKind.ChooseRefreshTarget, "Missing tidal choice");
            Check(engine.Pending.Options.Any(o => o.Card == own) && engine.Pending.Options.Any(o => o.Card == enemy),
                "Tidal target scope restricted");
            Respond(engine, engine.Pending.Options.First(o => o.Card == enemy));
            Check(enemy.Zone == CardZone.Hand, "Opponent target did not return");
        }

        private static void VineTiming()
        {
            var engine = Fixture(new[] { "v" }, new[] { "f" });
            var existing = Cooling(engine, 1, "a", 2);
            bool appliedBeforeEntry = false;
            engine.OnEvent += e => {
                if (e is CooldownChangedEvent changed && changed.Change.Card == existing && existing.RemainingCooldown == 3)
                    appliedBeforeEntry = !engine.State.Of(0).CoolingZone.Any(c => c.Def.Id == "v");
            };
            Play(engine, "v");
            Check(engine.Pending.Kind == RequestKind.ChooseDefense, "Double confirmation did not resolve");
            Respond(engine, engine.Pending.SkipOption);
            Check(engine.Pending.Kind == RequestKind.ChooseSlowTarget, "Vine slow missing");
            Check(engine.Pending.Options.Any(o => o.Card == existing)
                && !engine.Pending.Options.Any(o => o.Card != null && o.Card.Def.Id == "v"), "In-play card was targetable");
            Respond(engine, engine.Pending.Options.First(o => o.Card == existing));
            Check(appliedBeforeEntry, "Vine slow did not apply before card entry");
        }

        private static void DuelDeal()
        {
            var engine = New();
            Check(engine.State.Players.All(p => p.Hand.Count == 6), "Duel initial deal not symmetric");
            var dealt = new int[2];
            engine.OnEvent += e => { if (e is CardDrawnEvent d && d.Reason.StartsWith("回合")) dealt[d.Seat]++; };
            for (int seat = 0; seat < 2; seat++)
            {
                engine.State.Of(seat).Hand.Clear();
                var plain = new CardDef(0, "dealfixture", "Fixture", 5, 1, new EffectDef[0]);
                for (int i = 0; i < 6; i++) Hand(engine, seat, plain);
            }
            Until(engine, r => engine.State.TurnNumber == 4 && r.Kind == RequestKind.ChooseAttackCard);
            Check(dealt[0] == 2 && dealt[1] == 2, "Both sides must draw on turns 2 and 3");
        }
    }
}
