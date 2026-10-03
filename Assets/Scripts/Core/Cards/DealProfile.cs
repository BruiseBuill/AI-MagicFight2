using System;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 一名角色的<b>发牌口径</b>（2026-10-03）。
    ///
    /// <para><b>为什么需要它</b>：在此之前「开局抽几张 / 第几回合补几张 / 能换几张」是
    /// <b>整局一份</b>的（<see cref="DefaultDealPolicy"/>）—— 于是所有座位只能是同一套口径。
    /// 用户口径要求<b>怪物不等于人类玩家</b>：人类开局 6 张、第 2–3 回合各补 1；
    /// 怪物开局直接抽满 8 张，之后<b>一张都不再补</b>。所以这份口径必须挂到
    /// <see cref="CharacterDefinition"/> 上，由引擎<b>按座位</b>读。</para>
    ///
    /// <para><b>与 <see cref="IDealPolicy"/> 的关系</b>：那个接口继续存在，作为
    /// <b>没写口径时的兜底</b>（<see cref="CharacterDefinition.Deal"/> 为 null 才用它）。
    /// 这样老的构造点与自测脚本不需要一次全改，而且「换一套发牌策略」这个扩展位仍在。</para>
    ///
    /// <para><b>不可变</b>：一个角色一局之内不会换口径，所以字段全是 <c>readonly</c>，
    /// 越界值在构造时就抛 —— 一个写错的配置不该在对局中途才表现为「少发了两张牌」。</para>
    /// </summary>
    public sealed class DealProfile
    {
        /// <summary>开局手牌数。</summary>
        public readonly int InitialHandSize;

        /// <summary>开局可替换张数上限。</summary>
        public readonly int InitialReplaceLimit;

        /// <summary>从第几回合开始补牌（含）。</summary>
        public readonly int DrawFromTurn;

        /// <summary>补到第几回合为止（含）。</summary>
        public readonly int DrawToTurn;

        /// <summary>补牌区间内每回合补几张。</summary>
        public readonly int DrawPerTurn;

        /// <summary>补牌当拍可替换几张（替换的正是刚补到的那张）。</summary>
        public readonly int ReplacePerDraw;

        public DealProfile(int initialHandSize, int initialReplaceLimit = 0,
            int drawFromTurn = 0, int drawToTurn = 0, int drawPerTurn = 0, int replacePerDraw = 0)
        {
            if (initialHandSize < 1)
            {
                throw new ArgumentException("开局手牌数必须至少为 1。");
            }

            if (initialReplaceLimit < 0 || drawPerTurn < 0 || replacePerDraw < 0)
            {
                throw new ArgumentException("发牌口径的数值不能为负。");
            }

            if (drawPerTurn > 0 && drawToTurn < drawFromTurn)
            {
                throw new ArgumentException("补牌区间非法：结束回合早于起始回合。");
            }

            InitialHandSize = initialHandSize;
            InitialReplaceLimit = initialReplaceLimit;
            DrawFromTurn = drawFromTurn;
            DrawToTurn = drawToTurn;
            DrawPerTurn = drawPerTurn;
            ReplacePerDraw = replacePerDraw;
        }

        /// <summary>第 <paramref name="turnNumber"/> 回合开局补几张（不在区间内返回 0）。</summary>
        public int DrawOnTurn(int turnNumber)
        {
            return DrawPerTurn > 0 && turnNumber >= DrawFromTurn && turnNumber <= DrawToTurn
                ? DrawPerTurn
                : 0;
        }

        /// <summary>第 <paramref name="turnNumber"/> 回合补牌后，可替换几张（没补牌就是 0）。</summary>
        public int ReplaceLimitOnTurn(int turnNumber)
        {
            return DrawOnTurn(turnNumber) > 0 ? ReplacePerDraw : 0;
        }

        /// <summary>
        /// <b>人类玩家</b>的口径（= 旧的 <see cref="DefaultDealPolicy"/>）：
        /// 开局 6 张可换 3，第 2–3 回合各补 1 张、可换 1 次。
        ///
        /// <para>⚠ 这三个数一律引 <see cref="BattleState"/> 上的常量，不写字面量 ——
        /// 它们是规则基线的一部分，只能有一个来源。</para>
        /// </summary>
        public static DealProfile Human()
        {
            return new DealProfile(
                BattleState.InitialHandSize,
                BattleState.ReplaceLimitInitial,
                2,
                BattleState.DrawTurnsMax,
                1,
                1);
        }

        /// <summary>
        /// <b>怪物</b>的口径（2026-10-03 用户口径）：开局直接抽满 <b>8 张</b>（= 手牌上限），
        /// <b>之后一张都不再补</b>，开局也<b>不换牌</b>。
        ///
        /// <para>8 张正好等于 <see cref="BattleState.HandLimit"/>，所以「不再补牌」这件事
        /// <b>不需要额外写一条规则</b>：抽牌入口 <c>DrawCardToHand</c> 本来就按
        /// 「手牌 + 冷却区 + 场上 &gt;= 上限」拦下。这里把补牌区间写成空，是为了让
        /// 「第 2、3 回合的补牌拍」根本不发生（否则会在那里白白弹一次「无可替换」的过场）。</para>
        /// </summary>
        public static DealProfile Monster()
        {
            return new DealProfile(BattleState.HandLimit, 0, 0, 0, 0, 0);
        }
    }
}
