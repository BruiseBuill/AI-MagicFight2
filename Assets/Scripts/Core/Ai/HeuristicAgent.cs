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
    /// <item>每次出牌按所属流派的优先级取最高的一张（<see cref="PriorityOf"/>）；</item>
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
        public ArchetypeAnalysis LastAnalysis { get; private set; }

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

                CardPriority priority = PriorityOf(o.Card, req.Seat, _archetype);
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

                CardPriority priority = PriorityOf(c, seat, archetype);
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

        /// <summary>
        /// 防御：见类注释。流派 3（连击）自动切到 <see cref="DefenseStyle.AvoidComboAndZone"/>，
        /// 其余流派走 <see cref="DefenseStyle.Cheapest"/>。
        /// </summary>
        protected virtual DecisionResponse DecideDefense(DecisionRequest req)
        {
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
