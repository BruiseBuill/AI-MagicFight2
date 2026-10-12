using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 启发式 AI（2026-10-03 新增，规则来源：用户口述的「四流派 + 出牌优先级 + 防御口径」）。
    ///
    /// <para><b>与 <see cref="SimpleAiAgent"/> 的分工</b>：那个是「能跑完整局」的基线，
    /// 刻意保持粗糙、用来给内核做压力测试与万局回归；这个才是真正按战斗思路打牌的 AI。
    /// 两者并存 —— 基线不能删，否则回归失去参照。</para>
    ///
    /// <para><b>核心流程</b>（进攻）：</para>
    /// <list type="number">
    /// <item><b>开局判定一次流派</b>并固定（用户 2026-10-03 口径；见
    /// <see cref="SelectArchetype"/>）—— 判定发生在它第一次轮到自己出牌时，
    /// 用那一刻的手牌（= 开局手牌 + 替换结果，第 1 回合不补牌）。</item>
    /// <item>每次出牌按所属流派的优先级取最高的一张（<see cref="PriorityOf"/>；
    /// 若配了手写优先级表则以它为准，见 <see cref="PriorityFor"/>）；</item>
    /// <item>全局覆盖：回血卡永远最低，但当「生命上限 − 当前生命 ≥ 2」时强制最高。</item>
    /// </list>
    ///
    /// <para><b>防御</b>：能挡就挡、同类里挑<b>防御力量最小</b>的牌；能不交光环就不交
    /// （先找 0 补值的牌 → 其次免疫光环 → 最后才报备防御力量光环）。连击流派例外：
    /// 只用「非连击、非区域加速」的牌防，且不动用光环，挡不住就直接掉血。</para>
    ///
    /// <para><b>怪物复写</b>：本类所有关键决策都是 <c>virtual</c>，怪物专属 AI 继承它、
    /// 只覆写需要改的那几条即可（见 <see cref="RushMonsterAgent"/> 示例）。可覆写点：</para>
    /// <list type="bullet">
    /// <item><see cref="SelectArchetype"/> —— 强制流派（无视打分）；</item>
    /// <item><see cref="PriorityOf"/> —— 任一张牌的档位（提权 / 降权）；</item>
    /// <item><see cref="IsCardAllowed"/> —— 禁用某张牌（永不打出）；</item>
    /// <item><see cref="DefenseStyle"/> —— 换一套防御口径。</item>
    /// </list>
    ///
    /// <para><b>⚠ 它需要 <see cref="BattleState"/></b>：流派判定要读手牌，减速流派的前置条件
    /// 要读对方冷却区与手牌数，回血覆盖要读生命上限。没传 state 时这些功能自动降级
    /// （进攻仍可用 <see cref="DecisionRequest.Options"/> 正常工作，但区域减速永不开、
    /// 回血永不强制优先）。</para>
    /// </summary>
    public class HeuristicAgent : IAgent
    {
        // ══════════════════════════════════════════════════════
        //  配置
        // ══════════════════════════════════════════════════════

        /// <summary>是否动用光环（防御侧的免疫 / 力量补值）。默认开 —— 新 AI 的防御口径依赖它。</summary>
        public bool UseAuras { get; set; }

        /// <summary>是否在开局 / 补牌后替换弱牌。</summary>
        public bool ReplaceWeakCards { get; set; }

        /// <summary>送入冷却换增益时最多冷却几张手牌（电弧 / 磁暴 / 充能那一拍）。</summary>
        public int MaxCoolHandCards { get; set; }

        /// <summary>「查看对方手牌」的盲选种子（见 <see cref="SimpleAiAgent.BlindPickSeed"/> 的说明）。</summary>
        public int BlindPickSeed { get; set; }

        /// <summary>是否把流派判定结果打进 <see cref="BattleLog"/>（调试用，默认关）。</summary>
        public bool LogArchetype { get; set; }

        /// <summary>
        /// 双发「行动修正」的掷骰种子（2026-10-06）。
        ///
        /// <para><b>为什么要一个独立种子</b>：Core 里所有随机都必须可复现（<see cref="Rng"/>），
        /// 而「这次要不要防」是一个<em>规则外</em>的概率决策 —— 必须能从种子推出来，
        /// 否则「同种子 → 同对局」这条前提就断了。它与 <see cref="BlindPickSeed"/>
        /// （查看对方手牌那类的盲选）<b>分开</b>：混用一条随机流会让两边互相扰动，
        /// 多一次防御决策就改变后面所有盲选的结果。</para>
        /// </summary>
        public int DoubleDefendSeed { get; set; }

        /// <summary>
        /// <b>手写的出牌优先级表</b>（2026-10-08）—— 来自卡池资产的
        /// <see cref="CardPriorityTable"/>。非空且非空表时，本 AI 的<b>出牌优先级完全由它决定</b>：
        /// 见 <see cref="PriorityFor"/>。
        ///
        /// <para><b>⚠ 「完全」是字面意思</b>（用户 2026-10-08 口径：「一旦启用了手动，
        /// 代码当中就不能再对卡的优先级再赋值」）：登记过的卡用表里的数、没登记的一律并列最低
        /// （<see cref="CardPriorityTable.UnlistedTier"/>），四流派档位、
        /// 回血卡的全局覆盖、怪物子类的「最爱卡」全部让位 —— 否则「我写好的顺序」会被
        /// 悄悄改掉几处，而卡面上什么也看不出来。</para>
        ///
        /// <para>默认 <c>null</c>（= 老行为：全部按自动档位）。谁填它：
        /// <c>MonsterAgentFactory.Create</c>（从 <c>CharacterDefinition.CardPriorities</c> 来，
        /// 而那份来自 <c>CardPoolConfig</c>）。</para>
        /// </summary>
        public CardPriorityTable ManualPriorities { get; set; }

        // ══════════════════════════════════════════════════════
        //  状态
        // ══════════════════════════════════════════════════════

        /// <summary>本局状态（可为 null —— 见类注释的降级说明）。</summary>
        protected readonly BattleState State;

        private BattleArchetype _archetype = BattleArchetype.HighPower;
        private bool _judged;
        private int _blindState = -1;

        public HeuristicAgent(BattleState state = null)
        {
            State = state;
            UseAuras = true;
            ReplaceWeakCards = true;
            MaxCoolHandCards = 1;
            BlindPickSeed = 20261003;
            DoubleDefendSeed = 20261006;
        }

        /// <summary>本局判定的流派（尚未判定时返回 <see cref="BattleArchetype.HighPower"/>）。</summary>
        public BattleArchetype Archetype
        {
            get { return _archetype; }
        }

        /// <summary>是否已经做过开局判定。</summary>
        public bool HasJudged
        {
            get { return _judged; }
        }

        /// <summary>上一次判定的完整明细（调试 / 日志用；未判定时为 null）。</summary>
        public ArchetypeAnalysis LastAnalysis { get; protected set; }

        // ══════════════════════════════════════════════════════
        //  入口
        // ══════════════════════════════════════════════════════

        public virtual DecisionResponse Decide(DecisionRequest request)
        {
            if (request == null || request.Options == null || request.Options.Count == 0)
            {
                return DecisionResponse.Skip(request == null ? 0 : request.Seat);
            }

            switch (request.Kind)
            {
                case RequestKind.ChooseAttackCard:
                    return DecideAttack(request);

                case RequestKind.ChooseDefense:
                    return DecideDefense(request);

                case RequestKind.ChooseHasteTarget:
                    return Of(request, PickSmallestCooldown(request));

                case RequestKind.ChooseSlowTarget:
                    return Of(request, PickStrongestOpponentCooling(request));

                case RequestKind.ChooseZoneValue:
                    return Of(request, PickDensestZone(request));

                case RequestKind.ChooseRefreshTarget:
                    return Of(request, PickRefreshTarget(request));

                case RequestKind.ChooseCoolHandCards:
                    return PickCoolHandCards(request);

                case RequestKind.ChoosePeekCard:
                    return Of(request, PickBlindCard(request));

                case RequestKind.ChooseReplace:
                    return PickReplaces(request);

                default:
                    return Of(request, FirstNonSkip(request));
            }
        }

        // ══════════════════════════════════════════════════════
        //  流派：开局判定一次并固定
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 确保已判定流派。只在<b>第一次进攻决策</b>时执行一次 —— 之后整局沿用
        /// （用户口径：一局开局判定一次固定）。
        ///
        /// <para><b>⚠ 判定的输入是「那一刻的手牌」，不是写死的张数</b>（2026-10-03 复核）：
        /// 人类开局 6 张（可换 3）、怪物开局 <b>8 张</b>（不换、之后不再补）——
        /// 本方法读的是 <see cref="HandOf"/>，所以两套发牌口径都能直接用，不需要分支。</para>
        ///
        /// <para>⚠ 但 <see cref="ArchetypeJudge"/> 的四个分数是<b>张数</b>（「力量 ≥8 的牌有几张」），
        /// 手牌多一张就多一点分数 —— 8 张的怪物天然更容易被判成「高攻」（四个流派里唯一
        /// 纯按张数计分的那一个）。这是已知的口径偏移，暂不改阈值：改它会同时改变
        /// 玩家的 AI 行为，属另一轮回归。</para>
        /// </summary>
        protected virtual void EnsureJudged(int seat)
        {
            if (_judged)
            {
                return;
            }

            _archetype = SelectArchetype(seat);
            _judged = true;
        }

        /// <summary>
        /// 开局判定流派。默认走 <see cref="ArchetypeJudge.Analyze"/> 的四流派打分；
        /// 怪物子类可覆写它来<b>强制某个流派</b>（例如「这只怪永远走连击」）。
        /// </summary>
        protected virtual BattleArchetype SelectArchetype(int seat)
        {
            IReadOnlyList<CardInstance> hand = HandOf(seat);
            ArchetypeAnalysis analysis = ArchetypeJudge.Analyze(hand);
            LastAnalysis = analysis;
            return analysis.Chosen;
        }

        /// <summary>取某个座位的手牌（无 state 时返回空表）。</summary>
        protected IReadOnlyList<CardInstance> HandOf(int seat)
        {
            if (State == null || seat < 0 || seat >= State.Players.Count)
            {
                return new CardInstance[0];
            }

            return State.Of(seat).Hand;
        }

        // ══════════════════════════════════════════════════════
        //  进攻
        // ══════════════════════════════════════════════════════

        /// <summary>进攻：选出本次要打出的牌，并按需带上光环。</summary>
        protected virtual DecisionResponse DecideAttack(DecisionRequest req)
        {
            EnsureJudged(req.Seat);

            Option best = null;
            CardPriority bestPriority = default(CardPriority);
            int[] bestAuras = null;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || !IsCardAllowed(o.Card))
                {
                    continue;
                }

                CardPriority priority = PriorityFor(o.Card, req.Seat, _archetype);
                if (best == null || Better(priority, bestPriority))
                {
                    best = o;
                    bestPriority = priority;
                }
            }

            if (best == null)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            bestAuras = AurasForAttack(req, best.Card);
            return DecisionResponse.WithAuras(req.Seat, new[] { best.Index }, bestAuras);
        }

        /// <summary>
        /// 出牌时随牌提交的进攻光环。默认<b>不消耗</b>任何光环
        /// —— 用户口径只要求「优先打出带光环的卡」（那是出牌选择，会让它进冷却区、
        /// 成为未来的光环来源），没要求进攻时主动消耗指示物。
        /// 怪物子类若想打爆发，可覆写它返回需要的光环序号。
        /// </summary>
        protected virtual int[] AurasForAttack(DecisionRequest req, CardInstance card)
        {
            return null;
        }

        /// <summary>
        /// 某张牌在当前局面下的<b>出牌档位</b>。档位小者优先；同档位比
        /// <see cref="CardPriority.Sub"/>（大者优先）。
        ///
        /// <para>默认实现按流派分派（见 <see cref="DefaultPriority"/>）。怪物子类可整体覆写，
        /// 或只改某一张牌（先 <c>if (card.Def.Id == "x") return ...;</c> 再 <c>base</c>）。</para>
        /// </summary>
        protected virtual CardPriority PriorityOf(CardInstance card, int seat, BattleArchetype archetype)
        {
            return DefaultPriority(card, seat, archetype);
        }

        /// <summary>
        /// <b>出牌优先级的唯一入口</b>（2026-10-08）—— 手写表优先，没开表才问
        /// <see cref="PriorityOf"/>（自动档位 / 子类覆写）。
        ///
        /// <para><b>为什么要有这一层，而不是把判断塞进 <see cref="PriorityOf"/> 或
        /// <see cref="DefaultPriority"/></b>：那两个都是<b>子类可以整体覆写</b>的
        /// （<c>BerserkerAgent.PriorityOf</c> 就把「陨石」提到最前，并且是<b>先</b>判自己的
        /// 最爱、<b>后</b>才调 base）。把手写表写在被覆写的那一端，「怪物子类的偏爱」就会盖掉
        /// 玩家写进卡池的顺序 —— 而用户要的恰恰是<b>我写的不能被代码覆盖</b>。
        /// 所以门开在更外面一层：两个取档位的地方（<see cref="DecideAttack"/> /
        /// <see cref="PredictNextAttack"/>）都只走这里。</para>
        ///
        /// <para>⚠ <b>预测与实战必须同源</b>（本工程的老口径）：<see cref="PredictNextAttack"/>
        /// 也走这一个方法，所以「玩家点怪物看到的元素」与「它真打出来的那张」不会因为
        /// 手写表而分叉。</para>
        /// </summary>
        protected CardPriority PriorityFor(CardInstance card, int seat, BattleArchetype archetype)
        {
            CardPriorityTable table = ManualPriorities;
            if (table != null && !table.IsEmpty)
            {
                // 登记过 → 用它写的数（小者先打）；没登记 → 一律并列最低。
                // 刻意**不**回落 PriorityOf：那正是「代码又给卡赋值」。
                int tier;
                table.TryGet(card.Def.Id, out tier);
                return new CardPriority { Tier = tier, Sub = 0 };
            }

            return PriorityOf(card, seat, archetype);
        }

        /// <summary>是否允许打出这张牌（返回 false = 永不打出）。怪物子类用它禁用某张牌。</summary>
        protected virtual bool IsCardAllowed(CardInstance card)
        {
            return card != null;
        }

        /// <summary>默认的四流派出牌优先级。</summary>
        protected CardPriority DefaultPriority(CardInstance card, int seat, BattleArchetype archetype)
        {
            CardDef d = card.Def;

            // ── 全局覆盖：回血卡 ────────────────────────────────
            // 「所有回血卡的优先级永远最低（无视连击或者加速的附带效果），
            //   但如果此时的生命最大值 − 当前生命 ≥ 2，那一定优先回血。」
            if (CardRole.IsHealCard(d))
            {
                if (CanBenefitFromHeal(seat))
                {
                    return new CardPriority { Tier = -1, Sub = 0 };
                }

                return new CardPriority { Tier = 1000, Sub = 0 };
            }

            switch (archetype)
            {
                case BattleArchetype.HighPower:
                    return HighPowerPriority(card, seat);

                case BattleArchetype.Double:
                    return DoublePriority(card, seat);

                case BattleArchetype.Combo:
                    return ComboPriority(card, seat);

                default:
                    return SlowPriority(card, seat);
            }
        }

        /// <summary>
        /// 流派 1 · 高攻：
        /// 高攻连击（闪电那类，<b>不含磁暴</b>，即力量潜力 ≥8 的连击牌）
        /// → 带进攻光环的卡（力量潜力高的先）→ 力量 ≥7 的普通牌
        /// → 加速卡（加速强的先）→ 其余按力量降序。
        /// </summary>
        protected virtual CardPriority HighPowerPriority(CardInstance card, int seat)
        {
            CardDef d = card.Def;
            int potential = CardRole.AttackPotential(card);

            // 「闪电这种高攻击力的连击（不包含磁暴）」—— 磁暴力量 2，天然被阈值挡在外面。
            if (CardRole.IsComboCard(d) && potential >= ArchetypeJudge.HighPowerThreshold)
            {
                return new CardPriority { Tier = 0, Sub = potential };
            }

            if (CardRole.HasAttackAura(d))
            {
                return new CardPriority { Tier = 1, Sub = potential };
            }

            // 「没有力量 ≥7 的卡牌之后，则优先打出手中的加速卡」—— 所以 ≥7 的普通牌先打。
            if (card.EffectivePower >= 7)
            {
                return new CardPriority { Tier = 2, Sub = card.EffectivePower };
            }

            if (CardRole.IsHasteCard(d))
            {
                return new CardPriority { Tier = 3, Sub = CardRole.HasteStrength(d, HpLostOf(seat)) };
            }

            return new CardPriority { Tier = 4, Sub = card.EffectivePower };
        }

        /// <summary>
        /// 流派 2 · 双发：防御光环卡（<b>区域减速牌除外</b>）→ 双发卡（力量高的先）
        /// → 加速卡（加速强的先）→ 其余按力量降序。
        ///
        /// <para>「区域减速牌除外」的实现口径 = 「同时带区域减速与防御光环」的牌
        /// （<c>HasDefenseAura &amp;&amp; !IsSlowZoneCard</c>）—— 区域减速牌是减速流派的核心，
        /// 不该在这个流派里被优先烧掉。</para>
        ///
        /// <para>⚠ 2026-10-03 冰风暴 b 的光环改成「守护」后它已不带防御光环，
        /// 这条排除对它自然成立（它仍会落进 tier 3 按力量排序）。</para>
        /// </summary>
        protected virtual CardPriority DoublePriority(CardInstance card, int seat)
        {
            CardDef d = card.Def;

            if (CardRole.HasDefenseAura(d) && !CardRole.IsSlowZoneCard(d))
            {
                return new CardPriority { Tier = 0, Sub = CardRole.DefensePotential(card) };
            }

            if (CardRole.IsDoubleCard(d))
            {
                return new CardPriority { Tier = 1, Sub = card.EffectivePower };
            }

            if (CardRole.IsHasteCard(d))
            {
                return new CardPriority { Tier = 2, Sub = CardRole.HasteStrength(d, HpLostOf(seat)) };
            }

            return new CardPriority { Tier = 3, Sub = card.EffectivePower };
        }

        /// <summary>
        /// 流派 3 · 连击：连击卡 → 区域加速 → 任何加速卡 → 其余按力量降序。
        ///
        /// <para>「电弧需要烧卡才能连击」的处置不在这里 —— 它发生在
        /// <see cref="RequestKind.ChooseCoolHandCards"/> 那一拍（默认烧力量最低的，
        /// 见 <see cref="PickCoolHandCards"/>）。</para>
        /// </summary>
        protected virtual CardPriority ComboPriority(CardInstance card, int seat)
        {
            CardDef d = card.Def;

            if (CardRole.IsComboCard(d))
            {
                return new CardPriority { Tier = 0, Sub = card.EffectivePower };
            }

            if (CardRole.IsHasteZoneCard(d))
            {
                return new CardPriority { Tier = 1, Sub = card.EffectivePower };
            }

            if (CardRole.IsHasteCard(d))
            {
                return new CardPriority { Tier = 2, Sub = CardRole.HasteStrength(d, HpLostOf(seat)) };
            }

            return new CardPriority { Tier = 3, Sub = card.EffectivePower };
        }

        /// <summary>
        /// 流派 4 · 减速：区域减速（<b>须先满足开启条件</b>，见 <see cref="CanOpenSlowZone"/>）
        /// → 防御光环卡 → 连击卡 → 双发卡 → 加速卡 → 其余按力量降序。
        ///
        /// <para>⚠ 开启条件不满足时，区域减速<b>不作为区域减速处理</b> —— 它掉到最后一档
        /// 按力量排序（原话：「使用之前必须先检查……这个时候才能开」）。</para>
        /// </summary>
        protected virtual CardPriority SlowPriority(CardInstance card, int seat)
        {
            CardDef d = card.Def;

            if (CardRole.IsSlowZoneCard(d) && CanOpenSlowZone(seat))
            {
                return new CardPriority { Tier = 0, Sub = card.EffectivePower };
            }

            if (CardRole.HasDefenseAura(d))
            {
                return new CardPriority { Tier = 1, Sub = CardRole.DefensePotential(card) };
            }

            if (CardRole.IsComboCard(d))
            {
                return new CardPriority { Tier = 2, Sub = card.EffectivePower };
            }

            if (CardRole.IsDoubleCard(d))
            {
                return new CardPriority { Tier = 3, Sub = card.EffectivePower };
            }

            if (CardRole.IsHasteCard(d))
            {
                return new CardPriority { Tier = 4, Sub = CardRole.HasteStrength(d, HpLostOf(seat)) };
            }

            return new CardPriority { Tier = 5, Sub = card.EffectivePower };
        }

        // ══════════════════════════════════════════════════════
        //  局面查询（供优先级判断与子类使用）
        // ══════════════════════════════════════════════════════

        /// <summary>某个座位已损失的生命（无 state 时返回 0）。</summary>
        protected int HpLostOf(int seat)
        {
            if (State == null || seat < 0 || seat >= State.Players.Count)
            {
                return 0;
            }

            return State.Of(seat).HpLost;
        }

        /// <summary>
        /// 「此时的生命最大值 − 当前生命 ≥ 2」—— 回血卡强制优先的唯一判据
        /// （用户原话：「如果此时的最大生命值减去当前生命值大于等于2，也就是能够回血，
        /// 那一定优先回血」）。
        /// </summary>
        protected bool CanBenefitFromHeal(int seat)
        {
            if (State == null || seat < 0 || seat >= State.Players.Count)
            {
                return false;
            }

            PlayerState p = State.Of(seat);
            return p.MaxHp - p.Hp >= 2;
        }

        /// <summary>
        /// 区域减速的开启条件（用户原话：<b>「必须先检查对方冷却区，仅当其中至少有三张卡
        /// 且对方当前手牌数小于等于 3，这个时候才能开」</b>）。
        ///
        /// <para>无 state（拿不到对手信息）时恒返回 false —— 宁可不放，也不在信息不足时开大。</para>
        /// </summary>
        protected bool CanOpenSlowZone(int seat)
        {
            if (State == null || seat < 0 || seat >= State.Players.Count)
            {
                return false;
            }

            PlayerState opponent = State.OpponentOf(seat);
            if (opponent == null)
            {
                return false;
            }

            return opponent.CoolingZone.Count >= 3 && opponent.Hand.Count <= 3;
        }

        // ══════════════════════════════════════════════════════
        //  档位比较
        // ══════════════════════════════════════════════════════

        /// <summary>出牌档位：<see cref="Tier"/> 小者优先，同档 <see cref="Sub"/> 大者优先。</summary>
        protected struct CardPriority
        {
            /// <summary>档位（越小越先打）。</summary>
            public int Tier;

            /// <summary>档位内的强度（越大越先打）。</summary>
            public int Sub;
        }

        private static bool Better(CardPriority a, CardPriority b)
        {
            if (a.Tier != b.Tier)
            {
                return a.Tier < b.Tier;
            }

            // 严格大于 —— 相等时不替换，等价于「取手牌里靠前的那张」（与卡片顺序稳定）
            return a.Sub > b.Sub;
        }

        // ══════════════════════════════════════════════════════
        //  预测（供 AttackForecast / UI 复用，不改变本 AI 的状态）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 预测该座位<b>下一步会打出的牌</b>（与原 AI 的策略同源）。
        ///
        /// <para><b>⚠ 这是预测不是保证</b>：手牌会继续变化，且它不锁定流派
        /// —— 尚未判定时用一份<b>临时</b>判定（不写入 <see cref="_archetype"/>），
        /// 免得「玩家点了一下怪物」就把 AI 的流派提前钉死。</para>
        /// </summary>
        public virtual CardInstance PredictNextAttack(int seat)
        {
            if (State == null || seat < 0 || seat >= State.Players.Count)
            {
                return null;
            }

            IReadOnlyList<CardInstance> hand = State.Of(seat).Hand;
            if (hand.Count == 0)
            {
                return null;
            }

            BattleArchetype archetype = _judged
                ? _archetype
                : ArchetypeJudge.Analyze(hand).Chosen;

            CardInstance best = null;
            CardPriority bestPriority = default(CardPriority);

            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance c = hand[i];
                if (!IsCardAllowed(c))
                {
                    continue;
                }

                CardPriority priority = PriorityFor(c, seat, archetype);
                if (best == null || Better(priority, bestPriority))
                {
                    best = c;
                    bestPriority = priority;
                }
            }

            return best;
        }

        // ══════════════════════════════════════════════════════
        //  防御
        // ══════════════════════════════════════════════════════

        /// <summary>防御口径（供怪物复写）。</summary>
        protected enum DefenseStyle
        {
            /// <summary>能挡就挡、挑防御力量最小的牌。</summary>
            Cheapest = 0,

            /// <summary>连击流派的保守口径：只用非连击、非区域加速的牌挡，挡不住就掉血。</summary>
            AvoidComboAndZone = 1,
        }

        // ══════════════════════════════════════════════════════
        //  防御 · 双发「行动修正」（2026-10-06 · 用户口径）
        // ══════════════════════════════════════════════════════

        /// <summary>「能挡住该双发的牌」达到这个张数 → 一定防（用户口径：「4 张卡及以上则一定防」）。</summary>
        public const int DoubleDefendAlwaysCount = 4;

        /// <summary>恰好 3 张能挡时的防御概率（%）。</summary>
        public const int DoubleDefendChanceAtThree = 80;

        /// <summary>恰好 2 张能挡时的防御概率（%）。</summary>
        public const int DoubleDefendChanceAtTwo = 40;

        private Rng _gambleRng;
        private int _gambleKey = int.MinValue;
        private bool _gambleValue;

        /// <summary>
        /// <b>面对双发的「行动修正」</b>（用户 2026-10-06 口径原文）：
        /// <i>「面对带有双发的效果时，需要检查自身能防御住该双发的卡的数量（如果有防御光环，
        /// 则也要考虑到防御光环的加成），如果恰好只有两张卡能防御住，那进行行动修正，
        /// 只有 40% 的概率会进行防御；如果恰好只有三张卡能防御住，则 80% 的概率会进行防御；
        /// 4 张卡及以上则一定防。」</i>
        ///
        /// <para><b>为什么要有这条</b>：双发要交两张牌才挡得住，而「能挡就挡」的最优 AI
        /// 会让双发永远打不穿 —— 玩家侧表现为「双发卡毫无威胁」，AI 对 AI 侧表现为
        /// 双方都能一直补值挡住、只能靠 100 回合上限收场（见
        /// `Docs/implementation/2026-10-03-AI四流派重构.md` §10）。这条修正让
        /// 「挡得住、但挡得很勉强」的局面变成一次赌博。</para>
        ///
        /// <para><b>「能防御住该双发的卡」的判据</b>（<see cref="CountDoubleDefenders"/>）：
        /// 手牌里<b>单张</b>就能吃下这次进攻力量的牌 ——
        /// <see cref="DefenseResolver.HasGuard"/>（守护无视力量差）<b>或</b>
        /// <see cref="CardRole.DefensePotential"/> ≥ 进攻力量。
        /// 「防御光环的加成」按<b>卡自带的那部分</b>算，与流派判定
        /// （`ArchetypeJudge` 的「防御力量 ≥8，包括防御光环的数值在内」）<b>同一口径</b>
        /// —— 见 `Docs/implementation/2026-10-06-AI双发防御修正与四流派敌人.md` §8 歧义 1。</para>
        ///
        /// <para><b>张数 → 概率</b>：≥4 张 = 100%（不消耗掷骰）；3 张 = 80%；2 张 = 40%；
        /// ≤1 张 = 0%（凑不出两张，本来也挡不住）。量的是「张数」不是「组合数」——
        /// 双发必须交两张牌，所以「有几张能挡」直接等于「能不能凑出一对」。</para>
        ///
        /// <para><b>⚠ 结果按「本次进攻」缓存</b>（`_gambleKey`）：防御那两拍
        /// （报备光环 → 引擎按新预算重发 → 再提交）里引擎会<b>再问一次</b>
        /// <see cref="DecideDefense"/>。不缓存的话第二次掷骰可能翻盘，表现为
        /// 「报备完光环又放弃」—— 白白烧掉一批光环。键 = 回合号 + 座位 + 力量 + 张数 +
        /// 手牌内容，所以同一拍必然命中缓存、新的一次进攻必然重新掷骰。</para>
        ///
        /// <para><b>⚠ 它排在「不用光环就能挡住」那条分支之前</b>：否则能挡住时永远 100% 防，
        /// 这条修正就形同虚设。</para>
        ///
        /// <para><b>例外 · 有可用的免疫光环 → 恒防</b>（2026-10-06 用户追加口径）：
        /// 免疫光环一发就把<b>整次攻击（含双发）</b>免掉、而且<b>不需要交牌</b> ——
        /// 那是白拿的手段，没有理由把它赌掉。判据 = 本拍选项里存在免疫类光环，
        /// 且这一局真的会用它（见 <see cref="HasImmuneAuraOption"/>）。</para>
        /// </summary>
        protected virtual bool ShouldDefendDouble(DecisionRequest req)
        {
            // 拿不到对局状态 → 数不出手牌 → 退回加这条规则之前的旧口径（能挡就挡）。
            // 宁可「照旧会防」，也不要因为改规则把没传 state 的调用点悄悄变成「必被破」。
            if (State == null)
            {
                return true;
            }

            DefenseStyle style = DefenseStyleOf(req.Seat);

            // ★ 例外：手上有**可用的免疫光环**时不再掷骰。
            //   免疫不交牌、直接免掉整次攻击（含双发），是白拿的手段 —— 赌掉它没有道理。
            //   ⚠ 判据必须与「管线真的会用掉它」一致，所以带上 UseAuras 与防御口径：
            //     ① UseAuras = false 的调用点根本不会消耗光环；
            //     ② 连击流派（AvoidComboAndZone）在管线里于「用免疫」那一支 **之前** 就放弃防御了
            //        （它刻意不动用任何光环）—— 那种情况下这里返回 true 也会被拦下，
            //        与其制造「闸门放行、管线却放弃」的自相矛盾，不如让它照常走张数规则。
            if (UseAuras && style != DefenseStyle.AvoidComboAndZone && HasImmuneAuraOption(req))
            {
                return true;
            }

            int blockers = CountDoubleDefenders(req.Seat, req.ContextPower, style);

            int key = GambleKey(req.Seat, req.ContextPower, blockers);
            if (key == _gambleKey)
            {
                return _gambleValue;
            }

            _gambleKey = key;

            if (blockers < 2)
            {
                _gambleValue = false;
            }
            else if (blockers >= DoubleDefendAlwaysCount)
            {
                _gambleValue = true;
            }
            else
            {
                int chance = blockers == 3 ? DoubleDefendChanceAtThree : DoubleDefendChanceAtTwo;
                if (_gambleRng == null)
                {
                    _gambleRng = new Rng(DoubleDefendSeed);
                }

                _gambleValue = _gambleRng.Next(100) < chance;
            }

            return _gambleValue;
        }

        /// <summary>
        /// 本拍有没有<b>可用的免疫光环选项</b>（石化 ak / 石盾 al 那类 —— 它们覆盖整次攻击、含双发）。
        ///
        /// <para>复用 <see cref="FirstAuraIndex"/>（与管线 <c>DecideDefense</c> 第 ③ 步同一个判据），
        /// 所以「闸门说恒防」与「管线真的会用掉那枚免疫」永远是同一件事 ——
        /// 判据各写一份的下场是「闸门放行、管线却放弃」这种自相矛盾的行为。</para>
        /// </summary>
        protected static bool HasImmuneAuraOption(DecisionRequest req)
        {
            return FirstAuraIndex(req, true) >= 0;
        }

        /// <summary>
        /// 手牌里有几张<b>单张</b>就能挡住 <paramref name="attackPower"/> 的牌。
        ///
        /// <para>口径见 <see cref="ShouldDefendDouble"/>；<paramref name="style"/> 为
        /// <see cref="DefenseStyle.AvoidComboAndZone"/> 时同样排除连击 / 区域加速卡
        /// —— 否则连击流派会「先数出 3 张、按 80% 决定要防，再去挑牌时只剩 1 张」，
        /// 表现是概率对了但行为自相矛盾。</para>
        /// </summary>
        protected virtual int CountDoubleDefenders(int seat, int attackPower, DefenseStyle style)
        {
            IReadOnlyList<CardInstance> hand = HandOf(seat);
            int count = 0;

            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance card = hand[i];
                if (card == null)
                {
                    continue;
                }

                if (style == DefenseStyle.AvoidComboAndZone)
                {
                    CardDef def = card.Def;
                    if (CardRole.IsComboCard(def) || CardRole.IsHasteZoneCard(def))
                    {
                        continue;
                    }
                }

                if (DefenseResolver.HasGuard(card) || CardRole.DefensePotential(card) >= attackPower)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>这次掷骰的键（同一次进攻必须得到同一个结果，见 <see cref="ShouldDefendDouble"/>）。</summary>
        private int GambleKey(int seat, int attackPower, int blockers)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (State == null ? 0 : State.TurnNumber);
                h = h * 31 + seat;
                h = h * 31 + attackPower;
                h = h * 31 + blockers;

                IReadOnlyList<CardInstance> hand = HandOf(seat);
                h = h * 31 + hand.Count;
                for (int i = 0; i < hand.Count; i++)
                {
                    CardInstance card = hand[i];
                    h = h * 31 + card.Def.Power;
                    h = h * 31 + card.EffectivePower;
                    string id = card.Def.Id;
                    for (int k = 0; k < id.Length; k++)
                    {
                        h = h * 31 + id[k];
                    }
                }

                return h;
            }
        }

        /// <summary>
        /// 防御：见类注释。流派 3（连击）自动切到 <see cref="DefenseStyle.AvoidComboAndZone"/>，
        /// 其余流派走 <see cref="DefenseStyle.Cheapest"/>。
        /// </summary>
        protected virtual DecisionResponse DecideDefense(DecisionRequest req)
        {
            // ★ 双发「行动修正」（2026-10-06）：先算手里有几张挡得住，再决定要不要真的交牌。
            if (req.ContextDouble && !ShouldDefendDouble(req))
            {
                return Of(req, req.SkipOption);
            }

            if (!UseAuras)
            {
                return Of(req, PickCheapestDefense(req, DefenseStyle.Cheapest, true));
            }

            DefenseStyle style = DefenseStyleOf(req.Seat);

            // ① 不用光环就能挡住 → 直接出（最省资源）。
            Option free = PickCheapestDefense(req, style, true);
            if (free != null)
            {
                return SubmitDefense(req, free, null);
            }

            // ② 连击流派不动用光环（也就不用免疫）→ 挡不住就掉血。
            if (style == DefenseStyle.AvoidComboAndZone)
            {
                return Of(req, req.SkipOption);
            }

            // ③ 免疫光环：不用交牌、比「补值 + 出牌」更省。只在还没准备过光环时决策。
            if (!req.AurasPrepared)
            {
                int immune = FirstAuraIndex(req, true);
                if (immune >= 0)
                {
                    return DecisionResponse.WithAuras(req.Seat, null, new[] { immune });
                }
            }

            // ④ 需要防御力量光环补值。
            Option paid = PickCheapestDefense(req, style, false);
            int need = paid != null ? paid.Value : ComputeMinRequiredBonus(req.Seat, req.ContextPower, req.ContextDouble, style);
            if (need <= 0)
            {
                return Of(req, req.SkipOption);
            }

            // 还没准备过 → 报备「凑够 need 的最省一批」，引擎会按新预算重发本拍。
            if (!req.AurasPrepared)
            {
                int[] prep = PickDefenseAuras(req, need);
                if (prep.Length > 0 && SumAuraValues(req, prep) >= need)
                {
                    return DecisionResponse.PrepAuras(req.Seat, prep);
                }

                return Of(req, req.SkipOption);
            }

            // 已准备过 → 这次真的要出牌了，随牌提交所消耗的光环。
            if (paid == null)
            {
                return Of(req, req.SkipOption);
            }

            int[] pay = PickDefenseAuras(req, paid.Value);
            return SubmitDefense(req, paid, pay);
        }

        /// <summary>本座位的防御口径（怪物子类可覆写）。</summary>
        protected virtual DefenseStyle DefenseStyleOf(int seat)
        {
            return _judged && _archetype == BattleArchetype.Combo
                ? DefenseStyle.AvoidComboAndZone
                : DefenseStyle.Cheapest;
        }

        /// <summary>
        /// 挑最省的防御选项。
        /// <paramref name="noAuraOnly"/> = true 时只要「不需要光环补值」的（Value == 0）。
        /// <see cref="DefenseStyle.AvoidComboAndZone"/> 会额外把连击卡与区域加速卡排除。
        /// </summary>
        protected virtual Option PickCheapestDefense(DecisionRequest req, DefenseStyle style, bool noAuraOnly)
        {
            Option best = null;
            int bestCost = int.MaxValue;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o))
                {
                    continue;
                }

                if (noAuraOnly && o.Value > 0)
                {
                    continue;
                }

                if (style == DefenseStyle.AvoidComboAndZone)
                {
                    CardDef d = o.Card.Def;
                    if (CardRole.IsComboCard(d) || CardRole.IsHasteZoneCard(d))
                    {
                        continue;   // 「优先使用除了连击和区域加速的牌来防」
                    }
                }

                int cost = DefenseCost(o);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = o;
                }
            }

            return best;
        }

        /// <summary>防御代价 = 主牌防御力量 +（双发时）第二张的防御力量。</summary>
        protected static int DefenseCost(Option o)
        {
            int cost = DefenseResolver.DefensePower(o.Card);
            if (o.PairCard != null)
            {
                cost += DefenseResolver.DefensePower(o.PairCard);
            }

            return cost;
        }

        /// <summary>
        /// 自己算「最少还差多少防御力量才挡得住」—— 用于还没准备光环、
        /// 引擎的选项列表里一张牌都没有时（那些牌都因为补值不够被过滤掉了）。
        /// </summary>
        protected virtual int ComputeMinRequiredBonus(int seat, int attackPower, bool isDouble, DefenseStyle style)
        {
            IReadOnlyList<CardInstance> hand = HandOf(seat);
            if (hand.Count == 0)
            {
                return -1;
            }

            var needs = new List<int>();
            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance c = hand[i];
                if (style == DefenseStyle.AvoidComboAndZone)
                {
                    CardDef d = c.Def;
                    if (CardRole.IsComboCard(d) || CardRole.IsHasteZoneCard(d))
                    {
                        continue;
                    }
                }

                needs.Add(DefenseResolver.RequiredBonus(c, attackPower));
            }

            if (needs.Count == 0)
            {
                return -1;
            }

            if (!isDouble)
            {
                int min = int.MaxValue;
                for (int i = 0; i < needs.Count; i++)
                {
                    if (needs[i] < min)
                    {
                        min = needs[i];
                    }
                }

                return min;
            }

            // 双发：两张牌合计
            int best = int.MaxValue;
            for (int i = 0; i < needs.Count; i++)
            {
                for (int j = i + 1; j < needs.Count; j++)
                {
                    int sum = needs[i] + needs[j];
                    if (sum < best)
                    {
                        best = sum;
                    }
                }
            }

            return best == int.MaxValue ? -1 : best;
        }

        /// <summary>
        /// 挑「凑够 <paramref name="need"/> 点防御力量」的最省一批光环（枚数最少 ——
        /// 按加值降序贪心）。返回的是 <paramref name="req"/> 当前选项里的<b>序号</b>。
        /// </summary>
        protected virtual int[] PickDefenseAuras(DecisionRequest req, int need)
        {
            var picked = new List<Option>();
            int sum = 0;

            while (sum < need)
            {
                Option best = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.AuraSource == null || o.AuraKind == AuraKind.None || !AuraResolver.IsPowerBonus(o.AuraKind))
                    {
                        continue;
                    }

                    if (picked.Contains(o) || !AuraResolver.UsableIn(o.AuraKind, AuraContext.Defend))
                    {
                        continue;
                    }

                    if (best == null || o.Value > best.Value)
                    {
                        best = o;
                    }
                }

                if (best == null)
                {
                    break;
                }

                picked.Add(best);
                sum += best.Value;
            }

            var indices = new int[picked.Count];
            for (int i = 0; i < picked.Count; i++)
            {
                indices[i] = picked[i].Index;
            }

            return indices;
        }

        private static int SumAuraValues(DecisionRequest req, int[] indices)
        {
            int sum = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                Option o = req.Get(indices[i]);
                if (o != null)
                {
                    sum += o.Value;
                }
            }

            return sum;
        }

        private static DecisionResponse SubmitDefense(DecisionRequest req, Option pick, int[] auras)
        {
            if (pick == null || pick.IsSkip)
            {
                return DecisionResponse.WithAuras(req.Seat, null, auras);
            }

            return DecisionResponse.WithAuras(req.Seat, new[] { pick.Index }, auras);
        }

        /// <summary>第一个免疫类光环选项的序号（没有返回 −1）。</summary>
        private static int FirstAuraIndex(DecisionRequest req, bool immune)
        {
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (o.AuraSource != null && o.AuraKind != AuraKind.None
                    && AuraResolver.IsImmune(o.AuraKind) == immune)
                {
                    return o.Index;
                }
            }

            return -1;
        }

        // ══════════════════════════════════════════════════════
        //  通用选项工具
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 出牌选项与光环选项同在一个 <c>Options</c> 里，而光环选项也带 <c>Card</c>
        /// （= 它的来源牌）。所有「扫一遍选项找牌」的地方都必须用这个判据把光环排除掉。
        /// </summary>
        protected static bool IsCardOption(Option o)
        {
            return o != null && !o.IsSkip && o.AuraSource == null && o.Card != null;
        }

        private static Option FirstNonSkip(DecisionRequest req)
        {
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (!req.Options[i].IsSkip)
                {
                    return req.Options[i];
                }
            }

            return req.SkipOption;
        }

        private static DecisionResponse Of(DecisionRequest req, Option option)
        {
            if (option == null)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            return DecisionResponse.Of(req.Seat, option.Index);
        }

        // ══════════════════════════════════════════════════════
        //  非核心决策（沿用 SimpleAiAgent 的既定口径）
        // ══════════════════════════════════════════════════════

        /// <summary>加速：己方剩余冷却最小的牌。</summary>
        protected static Option PickSmallestCooldown(DecisionRequest req)
        {
            Option best = null;
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || o.Card.OwnerSeat != req.Seat)
                {
                    continue;
                }

                if (best == null || o.Card.RemainingCooldown < best.Card.RemainingCooldown)
                {
                    best = o;
                }
            }

            return best ?? req.SkipOption;
        }

        /// <summary>减速：对方力量最高的冷却牌（对方冷却区是明牌）。</summary>
        protected static Option PickStrongestOpponentCooling(DecisionRequest req)
        {
            Option best = null;
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || !req.IsEnemy(o.Card.OwnerSeat))
                {
                    continue;
                }

                if (best == null || o.Card.EffectivePower > best.Card.EffectivePower)
                {
                    best = o;
                }
            }

            return best ?? req.SkipOption;
        }

        /// <summary>
        /// 区域类：只在对我有利的那一方里挑匹配张数最多的一档
        /// （区域加速偏向自己的冷却区、区域减速偏向对方的冷却区）。
        /// </summary>
        protected static Option PickDensestZone(DecisionRequest req)
        {
            Option best = null;
            int bestScore = -1;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (o.Kind != OptionKind.ZoneValue)
                {
                    continue;
                }

                bool favorable = o.Seat == -1
                                 || (req.ContextHaste ? o.Seat == req.Seat : req.IsEnemy(o.Seat));

                if (!favorable || o.Count <= bestScore)
                {
                    continue;
                }

                bestScore = o.Count;
                best = o;
            }

            return best ?? req.SkipOption;
        }

        /// <summary>立即冷却完成 / 重置对方冷却：按极性挑剩余冷却最小的。</summary>
        protected static Option PickRefreshTarget(DecisionRequest req)
        {
            Option best = null;
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || !(req.ContextHaste ? o.Card.OwnerSeat == req.Seat : req.IsEnemy(o.Card.OwnerSeat)))
                {
                    continue;
                }

                if (best == null || o.Card.RemainingCooldown < best.Card.RemainingCooldown)
                {
                    best = o;
                }
            }

            return best ?? req.SkipOption;
        }

        /// <summary>
        /// 送入冷却换增益：挑力量最低的那张。
        /// 用户口径：「对于电弧需要烧卡才能连击的情况，默认烧掉进攻力量最低的。」
        /// </summary>
        protected virtual DecisionResponse PickCoolHandCards(DecisionRequest req)
        {
            int take = MaxCoolHandCards;
            if (take <= 0)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            var indices = new List<int>();
            var used = new List<Option>();

            while (indices.Count < take && indices.Count < req.Options.Count)
            {
                Option weakest = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (!IsCardOption(o) || used.Contains(o))
                    {
                        continue;
                    }

                    if (weakest == null || o.Card.EffectivePower < weakest.Card.EffectivePower)
                    {
                        weakest = o;
                    }
                }

                if (weakest == null)
                {
                    break;
                }

                used.Add(weakest);
                indices.Add(weakest.Index);
            }

            return new DecisionResponse { Seat = req.Seat, OptionIndices = indices.ToArray() };
        }

        /// <summary>查看对方手牌：盲选（引擎刻意不给内容，见 RequestKind.ChoosePeekCard 的注释）。</summary>
        protected Option PickBlindCard(DecisionRequest req)
        {
            var pool = new List<Option>();
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (IsCardOption(req.Options[i]))
                {
                    pool.Add(req.Options[i]);
                }
            }

            if (pool.Count == 0)
            {
                return FirstNonSkip(req);
            }

            if (_blindState < 0)
            {
                _blindState = BlindPickSeed;
            }

            unchecked
            {
                _blindState = _blindState * 1103515245 + 12345;
            }

            int pick = (int)((uint)_blindState % (uint)pool.Count);
            return pool[pick];
        }

        /// <summary>替换：换掉力量 ≤1 的弱牌。</summary>
        protected DecisionResponse PickReplaces(DecisionRequest req)
        {
            if (!ReplaceWeakCards)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            var indices = new List<int>();
            for (int i = 0; i < req.Options.Count && indices.Count < req.MaxSelect; i++)
            {
                Option o = req.Options[i];
                if (o.Kind != OptionKind.Replace || o.Card == null)
                {
                    continue;
                }

                if (o.Card.EffectivePower <= 1)
                {
                    indices.Add(o.Index);
                }
            }

            return new DecisionResponse { Seat = req.Seat, OptionIndices = indices.ToArray() };
        }
    }
}
