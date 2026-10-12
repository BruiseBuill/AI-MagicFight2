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

        // ── 2026-10-06：四种流派各一只（四种攻击逻辑各对应一类敌人）─────
        /// <summary>高攻型：堆力量硬推（对应 4 张力量 ≥7 的牌 + 攻光环）。</summary>
        public const string Power = "power";

        /// <summary>双发型：交双发逼对方交两张（卡池 = 全部 4 张双发 + 3 张防御光环卡）。</summary>
        public const string Double = "double";

        /// <summary>连击型：靠连击 / 区域加速打多次。</summary>
        public const string Combo = "combo";

        /// <summary>减速型：以区域减速为核心拖对方节奏。</summary>
        public const string Slow = "slow";

        /// <summary>
        /// 按档案名建一个怪物 AI。未知档案名<b>静默回退</b>到通用 AI
        /// （不抛异常 —— 一个配置字段写错不该让整局开不起来）。
        /// </summary>
        public static HeuristicAgent Create(string profile, BattleState state,
            bool useAuras = true, int blindSeed = 0, CardPriorityTable priorities = null)
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

                // ── 2026-10-06：四流派敌人各一种攻击逻辑 ──
                case Power:
                    agent = new PowerAgent(state);
                    break;

                case Double:
                    agent = new DoubleAgent(state);
                    break;

                case Combo:
                    agent = new ComboAgent(state);
                    break;

                case Slow:
                    agent = new SlowAgent(state);
                    break;

                default:
                    agent = new HeuristicAgent(state);
                    break;
            }

            agent.UseAuras = useAuras;

            // 手写出牌优先级（2026-10-08）：来自这只怪的卡池资产。非空时 AI 的出牌顺序
            // **完全**由它决定，连上面那些子类的「最爱卡」也要让位（见 PriorityFor）。
            agent.ManualPriorities = priorities;
            if (blindSeed != 0)
            {
                agent.BlindPickSeed = blindSeed;

                // 双发「行动修正」的掷骰另起一条随机流（换一个奇乘数 + 偏移）。
                // ⚠ 必须与盲选分开：混用一条流的话，多一次防御决策会改变后面所有盲选结果，
                //   而「同种子 → 同对局」这条前提要求每条流各自可复现。
                // ⚠ 必须随种子变：不派生的话所有对局的掷骰序列完全相同，
                //   玩家会看到「每次都在同样局面放弃防御」的假随机。
                agent.DoubleDefendSeed = unchecked(blindSeed * 486187739 + 12345);
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

    // ══════════════════════════════════════════════════════════
    //  四流派敌人（2026-10-06）
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 「强制流派」敌人的公共基类（2026-10-06）—— 怪物<b>无视手牌打分、恒走指定流派</b>。
    ///
    /// <para><b>为什么不让卡池自己决定流派</b>：卡池已经按流派配好了（见
    /// <c>MonsterBuilder.Specs</c>），按理 <see cref="ArchetypeJudge"/> 也能判对 ——
    /// 但那是<b>打分</b>：手牌里恰好有几张力量 ≥8 的牌就可能把「双发怪」判成「高攻怪」，
    /// 而用户的口径是「<b>4 种类型的敌人分别对应 4 种类型的攻击逻辑</b>」，
    /// 是<b>身份</b>不是概率。强制它，行为才可预期、也才好按种类调平衡。</para>
    ///
    /// <para>只覆写 <see cref="HeuristicAgent.SelectArchetype"/> 一处 —— 出牌优先级、
    /// 防御口径、双发行动修正全部沿用通用实现（父类的
    /// <see cref="HeuristicAgent.DefenseStyleOf"/> 会因为流派是连击而自动切到
    /// 「不用连击 / 区域加速牌来防」的保守口径，这正是想要的）。</para>
    /// </summary>
    public abstract class ForcedArchetypeAgent : HeuristicAgent
    {
        private readonly BattleArchetype _forced;

        protected ForcedArchetypeAgent(BattleState state, BattleArchetype archetype) : base(state)
        {
            _forced = archetype;
        }

        /// <summary>这一只恒走的流派。</summary>
        public BattleArchetype Forced
        {
            get { return _forced; }
        }

        protected override BattleArchetype SelectArchetype(int seat)
        {
            // ⚠ 仍然走一遍 Analyze 只是为了把明细留在 LastAnalysis 里（调试 / 排查用），
            //   返回值一律以强制流派为准 —— 否则「判分明细说高攻、行为却是双发」很难查。
            LastAnalysis = ArchetypeJudge.Analyze(HandOf(seat));
            return _forced;
        }
    }

    /// <summary>高攻型敌人：恒走高攻（力量 ≥8 的连击 → 进攻光环 → 力量 ≥7 → 加速 → 按力量）。</summary>
    public sealed class PowerAgent : ForcedArchetypeAgent
    {
        public PowerAgent(BattleState state) : base(state, BattleArchetype.HighPower)
        {
        }
    }

    /// <summary>双发型敌人：恒走双发（防御光环卡 → 双发卡 → 加速 → 按力量）。</summary>
    public sealed class DoubleAgent : ForcedArchetypeAgent
    {
        public DoubleAgent(BattleState state) : base(state, BattleArchetype.Double)
        {
        }
    }

    /// <summary>
    /// 连击型敌人：恒走连击（连击卡 → 区域加速 → 加速 → 按力量）。
    ///
    /// <para>⚠ 与 <see cref="TricksterAgent"/> 的区别：这只<b>不</b>改
    /// <see cref="HeuristicAgent.MaxCoolHandCards"/>（仍是 1）—— 它配的是「全部连击牌」
    /// 的卡池，每次烧 1 张就够触发连击，烧 3 张会把 8 张的手牌掏空。</para>
    /// </summary>
    public sealed class ComboAgent : ForcedArchetypeAgent
    {
        public ComboAgent(BattleState state) : base(state, BattleArchetype.Combo)
        {
        }
    }

    /// <summary>减速型敌人：恒走减速（区域减速[须满足开启条件] → 防御光环 → 连击 → 双发 → 加速 → 按力量）。</summary>
    public sealed class SlowAgent : ForcedArchetypeAgent
    {
        public SlowAgent(BattleState state) : base(state, BattleArchetype.Slow)
        {
        }
    }
}
