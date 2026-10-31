using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 本局的<b>唯一实体牌堆</b>（2026-10-31 用户口径：双方共用同一副）。
    /// 构造时洗一次；之后所有座位都只从<b>堆顶</b>抽牌，被换掉的牌<b>回堆底</b> ——
    /// 同一张实体牌同一时刻只会在一个地方，因此两个座位不可能拿到相同的牌。
    /// </summary>
    public sealed class Deck
    {
        private readonly List<CardDef> _cards;

        public Deck(IEnumerable<CardDef> defs, Rng rng)
        {
            if (defs == null)
            {
                throw new ArgumentNullException("defs");
            }

            if (rng == null)
            {
                throw new ArgumentNullException("rng");
            }

            _cards = new List<CardDef>(defs);
            rng.Shuffle(_cards);
        }

        /// <summary>堆中剩余张数。</summary>
        public int Remaining
        {
            get { return _cards.Count; }
        }

        /// <summary>从堆顶抽一张；堆空返回 null。</summary>
        public CardDef Draw()
        {
            if (_cards.Count == 0)
            {
                return null;
            }

            CardDef def = _cards[0];
            _cards.RemoveAt(0);
            return def;
        }

        /// <summary>把一张牌放回<b>堆底</b>（用户口径：换掉的牌塞回堆底）。</summary>
        public void Return(CardDef def)
        {
            if (def == null)
            {
                return;
            }

            _cards.Add(def);
        }

        /// <summary>只读快照（自测与调试用）。</summary>
        public IReadOnlyList<CardDef> Snapshot()
        {
            return new List<CardDef>(_cards);
        }
    }

    /// <summary>
    /// 发牌策略。做成接口是为了给「进阶模式（抽 9 选 6）」留替换位（`Docs/engineering/03-工程规划.md` §1）。
    /// </summary>
    public interface IDealPolicy
    {
        /// <summary>开局手牌数。</summary>
        int InitialHandSize { get; }

        /// <summary>开局可替换张数上限。</summary>
        int InitialReplaceLimit { get; }

        /// <summary>第 N 回合开始时，每人补几张牌。</summary>
        int DrawOnTurn(int turnNumber);

        /// <summary>第 N 回合补牌后，可替换张数（替换刚补的那张）。</summary>
        int ReplaceLimitOnTurn(int turnNumber);
    }

    /// <summary>基础模式发牌策略（决策 D1 / D2）：开局 6 张可换 3 张；第 2、3 回合各补 1 张、可换 1 次。</summary>
    public sealed class DefaultDealPolicy : IDealPolicy
    {
        public int InitialHandSize
        {
            get { return BattleState.InitialHandSize; }
        }

        public int InitialReplaceLimit
        {
            get { return BattleState.ReplaceLimitInitial; }
        }

        public int DrawOnTurn(int turnNumber)
        {
            return turnNumber >= 2 && turnNumber <= BattleState.DrawTurnsMax ? 1 : 0;
        }

        public int ReplaceLimitOnTurn(int turnNumber)
        {
            return DrawOnTurn(turnNumber) > 0 ? 1 : 0;
        }
    }
}
