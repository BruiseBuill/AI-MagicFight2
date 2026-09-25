using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    public sealed class ParticipantSetup
    {
        public readonly CharacterDefinition Character;
        public readonly ControlKind Control;
        public readonly int Team;
        public ParticipantSetup(CharacterDefinition character, ControlKind control, int team)
        {
            Character = character ?? throw new ArgumentNullException(nameof(character));
            Control = control; Team = team;
        }
    }

    /// <summary>模式负责人数、行动顺序、敌我关系、目标与胜负；新模式不依赖 AI 类型。</summary>
    public interface IBattleMode
    {
        void Validate(IReadOnlyList<ParticipantSetup> participants);
        int FirstActor(BattleState state);
        int NextActor(BattleState state, int currentSeat); // -1 表示新一轮
        bool AreEnemies(BattleState state, int first, int second);
        int SelectDefender(BattleState state, int attackerSeat);
        IReadOnlyList<int> EffectSeats(BattleState state, int sourceSeat, int targetSeat);
        bool TryFinish(BattleState state, out BattleOutcome outcome);
    }

    public sealed class BattleOutcome
    {
        public readonly IReadOnlyList<int> WinnerSeats;
        public readonly string Reason;
        public BattleOutcome(IEnumerable<int> winners, string reason)
        {
            WinnerSeats = new List<int>(winners).AsReadOnly(); Reason = reason;
        }
    }

    public sealed class DuelMode : IBattleMode
    {
        public void Validate(IReadOnlyList<ParticipantSetup> participants)
        {
            if (participants.Count != 2) throw new ArgumentException("DuelMode 仅支持两名角色；多人对局需提供对应的 IBattleMode。");
            if (participants[0].Team == participants[1].Team) throw new ArgumentException("1v1 双方必须分属不同队伍。");
        }
        public int FirstActor(BattleState state) { return 0; }
        public int NextActor(BattleState state, int currentSeat) { return currentSeat == 0 ? 1 : -1; }
        public bool AreEnemies(BattleState state, int first, int second) { return state.Of(first).Team != state.Of(second).Team; }
        public int SelectDefender(BattleState state, int attackerSeat)
        {
            foreach (PlayerState player in state.Players)
                if (player.Seat != attackerSeat && AreEnemies(state, attackerSeat, player.Seat)) return player.Seat;
            throw new InvalidOperationException("没有合法防御方。");
        }
        public IReadOnlyList<int> EffectSeats(BattleState state, int sourceSeat, int targetSeat)
        {
            return new[] { sourceSeat, targetSeat };
        }
        public bool TryFinish(BattleState state, out BattleOutcome outcome)
        {
            outcome = null;
            if (!state.Of(0).IsDead && !state.Of(1).IsDead) return false;
            var winners = new List<int>();
            foreach (PlayerState player in state.Players) if (!player.IsDead) winners.Add(player.Seat);
            PlayerState loser = state.Of(0).IsDead ? state.Of(0) : state.Of(1);
            outcome = new BattleOutcome(winners, winners.Count == 0 ? "双方同时出局" : loser.Name + (loser.Hp <= 0 ? "生命归零" : "生命上限归零"));
            return true;
        }
    }

    public sealed class BattleSetup
    {
        public readonly IReadOnlyList<ParticipantSetup> Participants;
        public readonly IBattleMode Mode;
        public readonly EffectRegistry Effects;
        public BattleSetup(IEnumerable<ParticipantSetup> participants, IBattleMode mode = null, EffectRegistry effects = null)
        {
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            var list = new List<ParticipantSetup>(participants);
            if (list.Count < 2 || list.Contains(null)) throw new ArgumentException("对局至少需要两名有效角色。");
            Participants = list.AsReadOnly(); Mode = mode ?? new DuelMode(); Mode.Validate(Participants);
            Effects = effects ?? EffectRegistry.CreateDefault();
        }
        public static BattleSetup Default()
        {
            return new BattleSetup(new[] {
                new ParticipantSetup(CharacterDefinition.DefaultPlayer(), ControlKind.Human, 0),
                new ParticipantSetup(CharacterDefinition.DefaultMonster(), ControlKind.Ai, 1)
            });
        }
    }
}
