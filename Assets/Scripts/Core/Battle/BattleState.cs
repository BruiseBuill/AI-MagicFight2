using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 对局全局状态。引擎（M4）写、表现层（M6+）只读。
    /// 用「座位列表」而不是两个固定字段，为 4 人 2v2 留路。
    /// </summary>
    public sealed class BattleState
    {
        private int _nextCardUid = 1;
        internal CardInstance CreateCard(CardDef definition, int seat)
        { return new CardInstance(definition, seat, _nextCardUid++); }
        // ── 规则常量（`Docs/rules/01-规则基线.md` §1）───────────────
        public const int InitialHp = 4;
        public const int HandLimit = 8;
        public const int InitialHandSize = 6;
        public const int ReplaceLimitInitial = 3;
        public const int DrawTurnsMax = 3;

        /// <summary>兼容默认 1v1 的本地座位；通用逻辑应使用 BattleSetup。</summary>
        public const int SeatPlayer = 0;

        /// <summary>兼容默认 1v1 的对手座位；AI 可绑定任意座位。</summary>
        public const int SeatAi = 1;

        internal BattleState(Rng rng, IBattleMode mode)
        {
            Mode = mode;
            Rng = rng;
            Players = new List<PlayerState>();
            TurnNumber = 0;
            AttackerSeat = SeatPlayer;
            WinnerSeat = -1;
        }

        public IBattleMode Mode { get; private set; }
        public BattleOutcome Outcome { get; private set; }

        /// <summary>本局的随机源（显式种子，保证确定性）。</summary>
        public Rng Rng { get; private set; }

        /// <summary>座位列表。</summary>
        public List<PlayerState> Players { get; private set; }

        /// <summary>当前回合数（从 1 开始）。</summary>
        public int TurnNumber { get; internal set; }

        /// <summary>当前半场的进攻方座位。</summary>
        public int AttackerSeat { get; internal set; }

        /// <summary>对局是否已结束。</summary>
        public bool IsOver { get; internal set; }

        /// <summary>胜者座位；尚无胜者为 −1。</summary>
        public int WinnerSeat { get; internal set; }

        /// <summary>结束原因（文案，便于自测报告）。</summary>
        public string EndReason { get; internal set; }

        /// <summary>事件序号（事件流自增）。</summary>
        public int EventSeq { get; internal set; }

        /// <summary>当前是否处于「连击的追加进攻」中 —— 追加进攻不重复触发冷却 −1（推论 P2）。</summary>
        public bool InCombo { get; internal set; }

        /// <summary>本半场内已经发生的追加进攻次数（防死循环）。</summary>
        public int ComboDepth { get; internal set; }

        public PlayerState Player
        {
            get { return Players[SeatPlayer]; }
        }

        public PlayerState Ai
        {
            get { return Players[SeatAi]; }
        }

        public PlayerState Of(int seat)
        {
            return Players[seat];
        }

        public PlayerState OpponentOf(int seat)
        {
            return Of(Mode.SelectDefender(this, seat));
        }

        /// <summary>全体角色合计损失的生命点数；具体效果范围由 IBattleMode 决定。</summary>
        public int TotalHpLost
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Players.Count; i++)
                {
                    sum += Players[i].HpLost;
                }

                return sum;
            }
        }

        /// <summary>终局：判定胜负并落库。</summary>
        internal void Finish(BattleOutcome outcome)
        {
            if (IsOver)
            {
                return;
            }

            IsOver = true;
            Outcome = outcome;
            WinnerSeat = outcome.WinnerSeats.Count == 1 ? outcome.WinnerSeats[0] : -1;
            EndReason = outcome.Reason;
        }

        /// <summary>人类可读的单行局面摘要（自测日志用）。</summary>
        public string Describe()
        {
            var parts = new List<string>();
            for (int i = 0; i < Players.Count; i++)
            {
                parts.Add(Players[i].ToString());
            }

            return "回合 " + TurnNumber + " 进攻方=" + AttackerSeat + " | " + string.Join(" | ", parts.ToArray());
        }
    }
}
