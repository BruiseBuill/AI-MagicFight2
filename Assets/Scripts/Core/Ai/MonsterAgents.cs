using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 怪物 AI 的<b>示例子类</b>（2026-10-03）—— 演示「在通用逻辑上复写特定卡牌 / 强制流派」。
    ///
    /// <para><b>为什么要代码子类而不是配置资产</b>：用户 2026-10-03 选定「代码子类」方案 ——
    /// 怪物对某些牌的偏好往往不是一条规则能表达的（「它宁可死也不回血」这种），
    /// 子类里写几行 <c>override</c> 比堆一堆配置字段更直接，也不用为每种组合新增字段。</para>
    ///
    /// <para><b>可复写的四个点</b>（都在 <see cref="HeuristicAgent"/> 上）：</para>
    /// <list type="bullet">
    /// <item><see cref="HeuristicAgent.SelectArchetype"/> —— 强制流派，无视四流派打分；</item>
    /// <item><see cref="HeuristicAgent.PriorityOf"/> —— 任一张牌的档位（提权 / 降权）；</item>
    /// <item><see cref="HeuristicAgent.IsCardAllowed"/> —— 禁用某张牌（永不打出）；</item>
    /// <item><see cref="HeuristicAgent.DefenseStyleOf"/> —— 换一套防御口径。</item>
    /// </list>
    ///
    /// <para><b>怎么挂到怪物上</b>：在 <c>CharacterConfig.aiProfile</c> 里填档案名
    /// （空串 = 默认），它随 <see cref="CharacterDefinition.AiProfile"/> 一路传到
    /// <see cref="MonsterAgentFactory.Create"/>。加一个新怪物 AI = 写一个子类 + 在工厂里
    /// 加一个 <c>case</c>，不需要动引擎与界面。</para>
    /// </summary>
    public static class MonsterAgentFactory
    {
        /// <summary>默认档案名（等同空串 = 通用 <see cref="HeuristicAgent"/>）。</summary>
        public const string Default = "";

        /// <summary>狂战怪：永远走高攻。</summary>
        public const string Berserker = "berserker";

        /// <summary>诡术怪：永远走连击。</summary>
        public const string Trickster = "trickster";

        /// <summary>
        /// 按档案名建一个怪物 AI。未知档案名<b>静默回退</b>到通用 AI
        /// （不抛异常 —— 一个配置字段写错不该让整局开不起来）。
        /// </summary>
        public static HeuristicAgent Create(string profile, BattleState state,
            bool useAuras = true, int blindSeed = 0)
        {
            HeuristicAgent agent;

            switch (profile)
            {
                case Berserker:
                    agent = new BerserkerAgent(state);
                    break;

                case Trickster:
                    agent = new TricksterAgent(state);
                    break;

                default:
                    agent = new HeuristicAgent(state);
                    break;
            }

            agent.UseAuras = useAuras;
            if (blindSeed != 0)
            {
                agent.BlindPickSeed = blindSeed;
            }

            return agent;
        }
    }

    /// <summary>
    /// 狂战怪（示例）：<b>永远走高攻</b>。
    ///
    /// <para>复写了三件事 ——</para>
    /// <list type="bullet">
    /// <item>流派：无视手牌打分，恒为 <see cref="BattleArchetype.HighPower"/>；</item>
    /// <item>提权：陨石（g）永远第一个打（它觉得快速回填比什么都值）；</item>
    /// <item>禁用：凝固（c）太温和，这怪一辈子不打它。</item>
    /// </list>
    ///
    /// <para>⚠ 「禁用」不是硬约束：若手上只剩被禁的牌，引擎的兜底分支仍会把它打出去
    /// （有牌必打，见 <c>BattleEngine.DoChooseAttackCard</c>）—— 所以禁用表现为
    /// 「永远排在最后」，而不是「永远不出」。</para>
    /// </summary>
    public sealed class BerserkerAgent : HeuristicAgent
    {
        /// <summary>它最爱的牌（陨石 · 快速回填）。</summary>
        public const string FavoriteCardId = "g";

        /// <summary>它绝不打出的牌（凝固）。</summary>
        public const string BannedCardId = "c";

        public BerserkerAgent(BattleState state) : base(state)
        {
        }

        protected override BattleArchetype SelectArchetype(int seat)
        {
            return BattleArchetype.HighPower;
        }

        protected override bool IsCardAllowed(CardInstance card)
        {
            return card != null && card.Def.Id != BannedCardId;
        }

        protected override CardPriority PriorityOf(CardInstance card, int seat, BattleArchetype archetype)
        {
            if (card.Def.Id == FavoriteCardId)
            {
                // −2 而不是 −1：−1 档被「可回血时的回血卡」占着，别撞。
                return new CardPriority { Tier = -2, Sub = 0 };
            }

            return base.PriorityOf(card, seat, archetype);
        }
    }

    /// <summary>
    /// 诡术怪（示例）：<b>永远走连击</b>，并且比通用 AI 更舍得烧手牌换连击
    /// （<see cref="HeuristicAgent.MaxCoolHandCards"/> 提到 3；电弧 / 磁暴 / 充能那一拍一次烧三张）。
    ///
    /// <para>它同时把「电弧（k）」提到最高档 —— 这是「对某张牌有偏好」的第二种写法：
    /// 不改禁用、不改流派，只在同流派内部把一张牌往前挪。</para>
    /// </summary>
    public sealed class TricksterAgent : HeuristicAgent
    {
        /// <summary>它最先想用的牌（电弧 · 烧牌换连击）。</summary>
        public const string FavoriteCardId = "k";

        public TricksterAgent(BattleState state) : base(state)
        {
            MaxCoolHandCards = 3;
        }

        protected override BattleArchetype SelectArchetype(int seat)
        {
            return BattleArchetype.Combo;
        }

        protected override CardPriority PriorityOf(CardInstance card, int seat, BattleArchetype archetype)
        {
            if (card.Def.Id == FavoriteCardId)
            {
                return new CardPriority { Tier = -2, Sub = 0 };
            }

            return base.PriorityOf(card, seat, archetype);
        }
    }
}
