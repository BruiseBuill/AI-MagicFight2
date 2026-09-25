using System;
using System.Collections.Generic;
using System.Linq;
using MagicBrawl.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            CheckPools();
            CheckHealthAndAbilities();
            CheckControllers();
            CheckModeExtension();
            Console.WriteLine("PASS: independent pools, minimum sizes, depletion/replacement, health, ability isolation, controller parity, four-seat mode extension.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static CharacterDefinition Character(string id, IEnumerable<string> cards, int minimum = 8)
    {
        return new CharacterDefinition(id, id, CharacterKind.Player, 4, 4, minimum, new CardPool(cards));
    }

    private static void CheckPools()
    {
        var ids = CardLibrary.All.Take(8).Select(card => card.Id).ToArray();
        var definition = Character("test", ids);
        CardPool draft = definition.CreateCardPool();
        draft.Remove(ids[0]);
        string error;
        Require(!draft.Validate(8, out error), "Seven-card pool must fail validation.");
        bool rejected = false;
        try { definition.WithCardPool(draft); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid pool must also fail at core boundary.");
        Require(definition.CreateCardPool().Count == 8, "Draft must not mutate definition.");

        var setup = new BattleSetup(new[] {
            new ParticipantSetup(definition, ControlKind.Human, 0),
            new ParticipantSetup(definition, ControlKind.Ai, 1)
        });
        BattleEngine first = BattleEngine.Create(239, setup);
        BattleEngine second = BattleEngine.Create(239, setup);
        first.Start(); first.Advance(); second.Start(); second.Advance();
        Require(first.State.Of(0).Deck != first.State.Of(1).Deck, "Decks cannot share mutable state.");
        Require(first.State.Of(0).Hand.Count == 6 && first.State.Of(1).Hand.Count == 6, "Each seat must receive six cards.");
        Require(first.State.Of(0).Hand.Select(card => card.Def.Id).SequenceEqual(second.State.Of(0).Hand.Select(card => card.Def.Id)), "Seed must reproduce hand.");
        Require(first.State.Of(0).Hand.All(card => ids.Contains(card.Def.Id)), "Draw escaped configured pool.");
        var foeBefore = first.State.Of(1).Deck.Snapshot().Select(card => card.Id).ToArray();
        Option replace = first.Pending.Options.First(option => option.Kind == OptionKind.Replace);
        first.Submit(DecisionResponse.Of(0, replace.Index));
        Require(first.State.Of(0).Hand.Count == 6 && first.State.Of(0).Deck.Remaining == 2, "Replacement broke conservation.");
        Require(first.State.Of(1).Deck.Snapshot().Select(card => card.Id).SequenceEqual(foeBefore), "Replacement touched opponent deck.");
        var seen = new HashSet<string>();
        foreach (CardInstance card in first.State.Of(0).Hand) Require(seen.Add(card.Def.Id), "Duplicate hand card.");
        while (first.State.Of(0).Deck.Remaining > 0)
            Require(seen.Add(first.State.Of(0).Deck.Draw().Id), "Duplicate remaining card.");
        Require(first.State.Of(0).Deck.Draw() == null && seen.Count == 8, "Finite deck must exhaust.");

        var six = Character("six", ids.Take(6), 6);
        var shortGame = BattleEngine.Create(13, new BattleSetup(new[] {
            new ParticipantSetup(six, ControlKind.Human, 0), new ParticipantSetup(six, ControlKind.Ai, 1)
        }));
        shortGame.Start(); shortGame.Advance();
        Require(shortGame.Pending.Kind != RequestKind.ChooseReplace, "Exhausted deck must not offer replacement.");
    }

    private static void CheckHealthAndAbilities()
    {
        var ability = new CharacterAbilityDefinition("heal-once", AbilityTrigger.BattleStarted, CharacterAbilityOp.Heal, 2, 1);
        var character = new CharacterDefinition("tall", "Tall", CharacterKind.Monster, 10, 12, 8, CardPool.AllCards(), new[] { ability });
        var setup = new BattleSetup(new[] { new ParticipantSetup(character, ControlKind.Ai, 0), new ParticipantSetup(character, ControlKind.Human, 1) });
        BattleEngine game = BattleEngine.Create(71, setup);
        game.Start();
        Require(game.State.Of(0).Hp == 12 && game.State.Of(1).Hp == 12, "Runtime ability state leaked between seats.");
        Require(game.State.Of(0).HpLost == 0, "Healing above initial HP cannot produce negative loss.");
        game.State.Of(0).Hp = 7;
        game.State.Of(0).MaxHp = 8;
        Require(game.State.Of(0).InitialHp == 10 && game.State.Of(0).HpLost == 3, "HP loss must use initial life, not four or current max.");
        BattleEngine next = BattleEngine.Create(71, setup); next.Start();
        Require(next.State.Of(0).Hp == 12, "Ability state leaked across battles.");
    }

    private static void CheckControllers()
    {
        BattleEngine game = BattleEngine.Create(85);
        game.Start(); game.Advance();
        var human = new HumanController();
        human.BeginDecision(game.Pending);
        Require(!human.Submit(DecisionResponse.Skip(1)), "Wrong seat response accepted.");
        DecisionResponse response;
        Require(!human.TryTakeResponse(out response), "Human input should wait.");
        Require(human.Submit(DecisionResponse.Skip(0)) && human.TryTakeResponse(out response), "Human controller failed to deliver input.");
        game.Submit(response); game.Advance();
        var ai = new AiController(new SimpleAiAgent { UseAuras = true });
        ai.BeginDecision(game.Pending);
        Require(ai.TryTakeResponse(out response) && response.Seat == game.Pending.Seat, "AI controller did not use same decision contract.");
        game.Submit(response); game.Advance();
        Require(game.Pending.Kind == RequestKind.ChooseAttackCard, "Controllers did not reach first attack.");
        // A bounded exchange checks the actual AI/engine decision handoff; this is not a full-game regression.
        for (int i = 0; i < 24 && !game.IsOver; i++)
        {
            ai.BeginDecision(game.Pending); Require(ai.TryTakeResponse(out response), "AI response missing.");
            game.Submit(response); game.Advance();
        }
    }

    private static void CheckModeExtension()
    {
        var participants = new List<ParticipantSetup>();
        for (int i = 0; i < 4; i++) participants.Add(new ParticipantSetup(CharacterDefinition.DefaultPlayer(), ControlKind.Ai, i % 2));
        BattleEngine game = BattleEngine.Create(99, new BattleSetup(participants, new FourSeatProbe()));
        game.Start();
        var decisionSeats = new HashSet<int>();
        for (int i = 0; i < 4; i++)
        {
            game.Advance(); decisionSeats.Add(game.Pending.Seat);
            Require(game.Pending.EnemySeats.Count == 2, "Team enemies not projected into request.");
            game.Submit(DecisionResponse.Skip(game.Pending.Seat));
        }
        Require(decisionSeats.SetEquals(new[] { 0, 1, 2, 3 }), "Four-seat setup did not dispatch every seat.");
        Require(game.State.Players.All(player => player.Hand.Count == 6 && player.Deck.Remaining == 34), "Four-seat deck isolation failed.");
    }

    private sealed class FourSeatProbe : IBattleMode
    {
        public void Validate(IReadOnlyList<ParticipantSetup> participants) { Require(participants.Count == 4, "Probe expects four seats."); }
        public int FirstActor(BattleState state) { return 0; }
        public int NextActor(BattleState state, int seat) { return seat < 3 ? seat + 1 : -1; }
        public bool AreEnemies(BattleState state, int a, int b) { return state.Of(a).Team != state.Of(b).Team; }
        public int SelectDefender(BattleState state, int attacker) { return (attacker + 1) % 4; }
        public IReadOnlyList<int> EffectSeats(BattleState state, int source, int target) { return new[] { source, target }; }
        public bool TryFinish(BattleState state, out BattleOutcome outcome) { outcome = null; return false; }
    }
}
