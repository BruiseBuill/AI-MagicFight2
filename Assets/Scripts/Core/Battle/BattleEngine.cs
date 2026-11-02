using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 对局结算引擎（M4）。六步结算流水线 + 暂停-恢复状态机。
    ///
    /// <para><b>为什么是暂停-恢复</b>：规则结算天生同步顺序，而 Unity UI 异步等点击。
    /// 引擎推进到需要外部输入时就停下、抛出 <see cref="Pending"/>；
    /// AI 座位即时应答后 <see cref="Submit"/>，玩家座位由 UI 渲染后回填。
    /// 引擎不依赖协程、不依赖 MonoBehaviour，自测时两座位都即时，可一路循环到终局。</para>
    ///
    /// <para><b>规则约束只收敛在这里</b>：非法选项在生成 <see cref="DecisionRequest.Options"/>
    /// 时就被过滤（典型是「防御方不能打出挡不住的牌」），AI 与 UI 都不重复判断。</para>
    /// </summary>
    public sealed partial class BattleEngine
    {
        /// <summary>连击连锁的保险上限（正常规则下牌数有限，不可能触顶）。</summary>
        private const int MaxComboChain = 24;

        /// <summary>
        /// 漩涡那一拍的弹窗标题（<see cref="RequestKind.ChooseRemoveFromGame"/>）。
        ///
        /// <para>口径同 <c>IssueCoolHandSelection</c>：标题命名<b>效果</b>、不写卡名，
        /// 而且要短 —— 标题栏平直段只有约 257 px，7 字以内才不缩号。</para>
        /// </summary>
        private const string RemoveFromGameTitle = "移出·获加速";

        private enum Phase
        {
            NotStarted = 0,
            InitialDeal,
            AwaitReplace,
            BeginTurn,
            BeginHalfTurn,
            AnnounceHalfTurn,
            AwaitAttackCard,
            Stage1,
            AwaitDefense,
            Stage2Effects,
            Stage3,
            Stage4,
            Stage4Effects,
            Stage5,
            Stage6,
            EndHalfTurn,
            Finished,
        }

        // ── 公开状态 ────────────────────────────────────────────
        public BattleState State { get; private set; }

        /// <summary>待回填的决策；null 表示可以继续推进。</summary>
        public DecisionRequest Pending { get; private set; }

        public bool IsOver
        {
            get { return State.IsOver; }
        }

        /// <summary>结构化事件流。M6 订阅后切成动画，M4 完全不知道 UI 存在。</summary>
        public event Action<BattleEvent> OnEvent;

        // ── 内部状态 ────────────────────────────────────────────
        private readonly IDealPolicy _deal;
        private readonly List<CooldownChange> _cooldownBuffer = new List<CooldownChange>();

        private Phase _phase = Phase.NotStarted;

        // 发牌 / 替换
        //
        // ⚠ 2026-10-03 起「能换几张」不再是一个全局字段（_replaceLimit 已删除）——
        //   它现在按座位从角色的 DealProfile 现算（见 ReplaceLimitOnTurnOf）。
        //   同一个 Pass 里人类与怪物的替换上限可以不同，所以它绝不能是个共享字段。
        private int _replaceSeat;
        private bool _replaceIsInitial;
        private Phase _replaceNextPhase = Phase.BeginTurn;
        private readonly List<CardInstance> _replaceTargets = new List<CardInstance>();

        /// <summary>
        /// 本趟替换**已经换掉几张**（2026-10-31）。
        ///
        /// <para><b>为什么需要它</b>：拖着手牌往上一扔就换掉一张之后，这一次替换拍并不结束 ——
        /// 引擎原地重发同一条 <see cref="RequestKind.ChooseReplace"/>，让玩家继续扔下一张。
        /// 重发时额度必须扣掉已用掉的张数，否则每重发一次 <c>MaxSelect</c> 又回到 3，
        /// 开局就能把整手牌换一遍。</para>
        /// </summary>
        private int _replaceUsed;

        // 半场上下文
        private AttackContext _atk;

        /// <summary>
        /// 本半场的进攻方被角色能力要求「不出牌」（<see cref="CharacterAbilityOp.SkipAttack"/>）。
        ///
        /// <para>与「手上无牌」是<b>两回事</b>：后者是规则 §3 的「无法攻击 → 掉 1 点」，
        /// 前者是角色主动放弃这一拍（例如自爆怪在自己炸掉之前的那一回合），<b>不掉血</b>。</para>
        /// </summary>
        private bool _skipAttack;

        // Effect execution state lives in BattleEngine.Effects.cs.

        // 防御
        //
        // ⚠ 曾经还有一个 _immuneApplied 字段，用来把「免疫已消耗」带进防御决策
        //   （旧推论 P1：免疫后仍须交牌、力量不参与比较）。2026-09-18 规则改动之后
        //   免疫是「消耗掉就整个攻击被免疫、不再进防御决策」，这个状态在方法内就地用完，
        //   再也没有第二处读者 —— 留着只会让下一个人以为它还有用。
        private bool _defenseSuccess;
        private bool _gaveUp;
        private readonly List<CardInstance> _defenseCards = new List<CardInstance>();
        private int _defensePower;
        private int _turnLimitLoser = -1;
        private bool _allowDrawProposal;
        private int _drawObservationStart;

        private BattleEngine(BattleState state, IDealPolicy dealPolicy, EffectRegistry effects)
        {
            State = state;
            _effectRegistry = effects;
            _effectRegistry.Freeze();
            _deal = dealPolicy ?? new DefaultDealPolicy();
        }

        /// <summary>
        /// 「全员 AI」对局的默认回合上限（用户 2026-10-03 口径：<b>最多不超过 100 回合</b>）。
        ///
        /// <para><b>为什么需要它</b>：本作「防御成功 = 不掉血」，而双方都严格执行
        /// 「能防就防 + 必须交防御光环才补值」时，光环由「打出的牌进冷却区」源源不断再生
        /// —— 于是谁也不掉血，对局可能一直跑下去（实测能到 3000+ 回合，见
        /// `Tools/RuleSelfTest/HeuristicAgentScenario.cs`）。用户明确：这种不收敛
        /// <b>只出现在 AI 对 AI</b> 的对局里，给个上限即可，不必改规则。</para>
        ///
        /// <para>全 AI 使用此保护上限；人机对局使用 40 回合判负，双人使用平局观察。</para>
        /// </summary>
        public const int AiTurnLimit = 100;
        public const int HumanAiTurnLimit = 40;
        public const int DrawObservationTurns = 4;
        public int DrawObservationStartTurn { get { return _drawObservationStart; } }

        public void UseAiTurnLimit()
        {
            MaxTurns = AiTurnLimit;
            _turnLimitLoser = -1;
            _allowDrawProposal = false;
        }

        /// <summary>
        /// 本局的回合上限；0 = 不限。人机对局到限判玩家失败，其余到限判平。
        ///
        /// <para>构造时由 <see cref="Create(int, BattleSetup, IDealPolicy)"/> 按「是否全员 AI」
        /// 自动给值；调用方可以覆盖它（自测里给「标着人类、实际由 AI 驱动」的座位显式设上限，
        /// 或设 0 关掉以做长局对照）。</para>
        /// </summary>
        public int MaxTurns { get; set; }

        /// <summary>建一局 1v1（0 号座位 = 玩家先手，D3）。</summary>
        public static BattleEngine Create(int seed, IDealPolicy dealPolicy = null)
        {
            return Create(seed, BattleSetup.Default(), dealPolicy);
        }

        public static BattleEngine Create(int seed, BattleSetup setup, IDealPolicy dealPolicy = null)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            var rng = new Rng(seed);
            var state = new BattleState(rng, setup.Mode);

            // 牌堆口径（见 BattleSetup.ShareDeck）：
            //   · 共用（标准战斗）—— 所有座位卡池求并集，开局洗一次，双方都从堆顶抽；
            //   · 独享（自定义战斗）—— 每个座位洗自己的卡池，玩家挑的 8 张只归玩家。
            // 2026-11-01 用户口径：自定义战斗必须各洗各的 —— 原来并集成 16 张，
            // 玩家开局 6 张里平均 3 张是对手选的，表现成「我选的牌没变成我的牌」。
            Deck sharedDeck = null;
            if (setup.ShareDeck)
            {
                var sharedDefs = new List<CardDef>();
                var sharedIds = new HashSet<string>(StringComparer.Ordinal);
                for (int seat = 0; seat < setup.Participants.Count; seat++)
                {
                    foreach (CardDef def in setup.Participants[seat].Character.CreateCardPool().Resolve())
                    {
                        if (sharedIds.Add(def.Id))
                        {
                            sharedDefs.Add(def);
                        }
                    }
                }

                sharedDeck = new Deck(sharedDefs, rng);
            }

            for (int seat = 0; seat < setup.Participants.Count; seat++)
            {
                // ⚠ 独立牌堆那一路必须在**同一次循环里按座位顺序** new —— 共用同一个 rng，
                //   洗牌次序固定，同种子复现照样成立。
                Deck seatDeck = sharedDeck;
                if (seatDeck == null)
                {
                    var defs = new List<CardDef>(setup.Participants[seat].Character.CreateCardPool().Resolve());
                    seatDeck = new Deck(defs, rng);
                }

                state.Players.Add(new PlayerState(seat, setup.Participants[seat], seatDeck));
            }

            // 效果校验：共用时一副就够；独享时逐座位过一遍（每座都是不同的牌）。
            var effects = setup.Effects;
            if (sharedDeck != null)
            {
                foreach (CardDef card in sharedDeck.Snapshot())
                    foreach (EffectDef effect in card.Effects) effects.Validate(effect);
            }
            else
            {
                foreach (PlayerState player in state.Players)
                    foreach (CardDef card in player.Deck.Snapshot())
                        foreach (EffectDef effect in card.Effects) effects.Validate(effect);
            }

            var engine = new BattleEngine(state, dealPolicy, effects);

            // 「全员 AI」→ 自动带上回合上限。判据是 setup 里的 Control（而不是「谁在驱动」）：
            // BattleDriver 的 _autoPlay 会把玩家座位也交给 AI，但 setup 仍写着 Human ——
            // 那种情况由调用方自己补一刀（见 BattleDriver.StartBattle）。
            bool allAi = true;
            for (int i = 0; i < setup.Participants.Count; i++)
            {
                if (setup.Participants[i].Control != ControlKind.Ai)
                {
                    allAi = false;
                    break;
                }
            }

            int humans = 0;
            int humanSeat = -1;
            for (int i = 0; i < setup.Participants.Count; i++)
                if (setup.Participants[i].Control == ControlKind.Human) { humans++; humanSeat = i; }
            bool humanAiDuel = setup.Mode is DuelMode && humans == 1 && setup.Participants.Count == 2;
            engine._turnLimitLoser = humanAiDuel ? humanSeat : -1;
            engine._allowDrawProposal = setup.Mode is DuelMode && humans == 2;
            engine.MaxTurns = allAi ? AiTurnLimit : humanAiDuel ? HumanAiTurnLimit : 0;
            return engine;
        }

        /// <summary>开局：发初始手牌。之后由 <see cref="Advance"/> 推进。</summary>
        public void Start()
        {
            if (_phase != Phase.NotStarted)
            {
                throw new InvalidOperationException("引擎已经启动过了");
            }

            _phase = Phase.InitialDeal;
            foreach (PlayerState player in State.Players)
                ApplyAbilities(player.Seat, AbilityTrigger.BattleStarted);
        }

        // ══════════════════════════════════════════════════════
        //  推进 / 回填
        // ══════════════════════════════════════════════════════

        /// <summary>推进到下一个决策点或终局。</summary>
        public void Advance()
        {
            int guard = 0;
            while (!State.IsOver && Pending == null)
            {
                if (++guard > 50000)
                {
                    throw new InvalidOperationException("引擎推进次数超限 —— 疑似状态机死循环，请检查 Phase=" + _phase);
                }

                if (!AdvanceSpecialEffects()) Step();
            }
            if (Pending != null)
            {
                var enemies = new List<int>();
                foreach (PlayerState player in State.Players)
                    if (State.Mode.AreEnemies(State, Pending.Seat, player.Seat)) enemies.Add(player.Seat);
                Pending.EnemySeats = enemies.AsReadOnly();
                Pending.AurasPrepared = _preparedAuras.Count > 0;
                Pending.DrawStatus = _drawObservationStart == 0 ? string.Empty
                    : State.TurnNumber < _drawObservationStart ? "平局观察 · 下回合开始"
                    : "平局观察 · " + (State.TurnNumber - _drawObservationStart + 1) + " / " + DrawObservationTurns;
                if (Pending.RequestId == 0) Pending.RequestId = ++_requestSequence;
            }
        }

        /// <summary>回填外部决策，然后可以继续 <see cref="Advance"/>。</summary>
        public void Submit(DecisionResponse response)
        {
            if (Pending == null)
            {
                throw new InvalidOperationException("当前没有待回填的决策");
            }

            if (response == null || response.Seat != Pending.Seat)
                throw new ArgumentException("决策不属于当前行动座位。");
            DecisionRequest req = Pending;
            if (response.RequestId != 0 && response.RequestId != req.RequestId) return;
            if ((req.Kind == RequestKind.ChooseAttackCard || req.Kind == RequestKind.ChooseDefense)
                && response.OptionIndices != null && response.OptionIndices.Length == 1)
            {
                foreach (Option option in req.Options)
                    if (option.Index == response.OptionIndices[0] && option.Kind == OptionKind.ProposeDraw)
                    {
                        if (TryProposeDraw(req.Seat)) Pending = null;
                        return;
                    }
            }
            if (_pendingEffectWindow != null)
            {
                if (!_pendingEffectWindow.Submit(response)) return;
                _pendingEffectWindow = null;
                Pending = null;
                return;
            }

            // 「只更新光环」的回填：玩家在判定区加 / 减了一枚准备使用的光环，
            // 但还没出牌。这一拍**不消费**，只是按新的准备集合把同一条决策重发一次 ——
            // 防御牌的合法性依赖这份集合（要几点补值才算够挡），所以必须由引擎重算。
            if (response != null && response.AuraPrepOnly
                && (req.Kind == RequestKind.ChooseAttackCard || req.Kind == RequestKind.ChooseDefense))
            {
                _preparedAuras.Clear();
                _preparedAuras.AddRange(PickAuras(req, response));

                Pending = null;
                _phase = req.Kind == RequestKind.ChooseDefense ? Phase.AwaitDefense : Phase.AwaitAttackCard;
                return;
            }

            Pending = null;

            List<Option> picked = PickOptions(req, response);
            List<Option> auras = PickAuras(req, response);
            Dispatch(req, picked, auras);
        }

        /// <summary>一步状态机。</summary>
        private void Step()
        {
            switch (_phase)
            {
                case Phase.InitialDeal:
                    DoInitialDeal();
                    break;

                case Phase.AwaitReplace:
                    IssueReplace();
                    break;

                case Phase.BeginTurn:
                    DoBeginTurn();
                    break;

                case Phase.BeginHalfTurn:
                    DoBeginHalfTurn();
                    break;

                case Phase.AnnounceHalfTurn:
                    DoAnnounceHalfTurn();
                    break;

                case Phase.AwaitAttackCard:
                    IssueAttackCard();
                    break;

                case Phase.Stage1:
                    RunStage1();
                    break;

                case Phase.AwaitDefense:
                    IssueDefense();
                    break;

                case Phase.Stage2Effects:
                    RunEffects(EffectStage.Defense, () => _phase = Phase.Stage3);
                    break;

                case Phase.Stage4Effects:
                    RunEffects(EffectStage.AfterCooldown, () => _phase = Phase.Stage5);
                    break;

                case Phase.Stage3:
                    RunStage3();
                    break;

                case Phase.Stage4:
                    DoStage4();
                    break;

                case Phase.Stage5:
                    DoStage5();
                    break;

                case Phase.Stage6:
                    DoStage6();
                    break;

                case Phase.EndHalfTurn:
                    DoEndHalfTurn();
                    break;

                default:
                    _phase = Phase.Finished;
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        //  发牌 / 替换
        // ══════════════════════════════════════════════════════

        private void DoInitialDeal()
        {
            // 按座位读各自的发牌口径（2026-10-03）：人类 6 张、怪物 8 张，一局之内共存。
            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                PlayerState player = State.Of(seat);
                int count = InitialHandSizeOf(player);
                for (int i = 0; i < count; i++)
                {
                    DrawCardToHand(player, "开局发牌");
                }
            }

            _replaceIsInitial = true;
            _replaceNextPhase = Phase.BeginTurn;
            BeginReplacePass(null);
        }

        // ══════════════════════════════════════════════════════
        //  发牌口径（按座位）
        // ══════════════════════════════════════════════════════
        //
        // 角色写了 DealProfile 就以它为准；没写才回落到构造时传进来的 IDealPolicy
        //（老口径，也是「换一套发牌策略」的扩展位）。四个读数入口全在这里，
        // 别在对局流程里各处自己判 —— 否则又会出现「一开始 6 张、补牌却按 8 张算」这种分叉。

        private int InitialHandSizeOf(PlayerState player)
        {
            return player.Definition.Deal != null ? player.Definition.Deal.InitialHandSize : _deal.InitialHandSize;
        }

        /// <summary>
        /// 开局可换张数 = <b>配置额度</b>与<b>牌堆剩余</b>取小（2026-11-01）。
        ///
        /// <para>为什么要裁这一刀：自定义战斗是「各洗各的 8 张」，发完开局 6 张后堆里只剩 2 张。
        /// 额度若仍是 3，第 3 张会在旧牌 <see cref="Deck.Return"/> 回堆底之后被抽回来 ——
        /// 实测「换完手牌和换之前一模一样」（`d,g,f → h,e,d`），换牌整个空转。</para>
        ///
        /// <para>裁成 min 之后：8 张牌堆换 2 张就把堆抽空，绝不回抽自己；
        /// 标准战斗是整表 44 张，min 恒等于配置额度 3，行为一个字没变。</para>
        /// </summary>
        private int InitialReplaceLimitOf(PlayerState player)
        {
            int configured = player.Definition.Deal != null
                ? player.Definition.Deal.InitialReplaceLimit
                : _deal.InitialReplaceLimit;

            // ⚠ 只在开局这一趟裁 —— 补牌那一趟（ReplaceLimitOnTurnOf）不动：
            //   它本来就只有 0/1 张，且手牌已经满了，堆剩余不该再改它的语义。
            return configured < player.Deck.Remaining ? configured : player.Deck.Remaining;
        }

        private int DrawOnTurnOf(PlayerState player, int turnNumber)
        {
            return player.Definition.Deal != null ? player.Definition.Deal.DrawOnTurn(turnNumber) : _deal.DrawOnTurn(turnNumber);
        }

        private int ReplaceLimitOnTurnOf(PlayerState player, int turnNumber)
        {
            return player.Definition.Deal != null
                ? player.Definition.Deal.ReplaceLimitOnTurn(turnNumber)
                : _deal.ReplaceLimitOnTurn(turnNumber);
        }

        private void BeginReplacePass(List<CardInstance> targets)
        {
            _replaceTargets.Clear();
            if (targets != null)
            {
                _replaceTargets.AddRange(targets);
            }

            _replaceSeat = 0;
            _replaceUsed = 0;
            _phase = Phase.AwaitReplace;
        }

        private void IssueReplace()
        {
            if (_replaceSeat >= State.Players.Count)
            {
                _phase = _replaceNextPhase;
                return;
            }

            PlayerState p = State.Of(_replaceSeat);
            if (p.IsDead) { _replaceSeat++; _replaceUsed = 0; return; }
            List<CardInstance> pool = _replaceIsInitial ? p.Hand : _replaceTargets;

            // 本座的替换上限现算（开局那趟用角色的开局上限；补牌那趟用它本回合的上限）——
            // 怪物写的是 0，所以「不补牌 + 不换牌」这条在这里就自然成立，不需要额外分支。
            int limit = _replaceIsInitial
                ? InitialReplaceLimitOf(p)
                : ReplaceLimitOnTurnOf(p, State.TurnNumber);

            // 2026-10-31：一张一张换 —— 每换完一张本趟会原地重发这次决策，
            // 所以额度要扣掉已经用掉的张数（否则重发一次就又满额了）。
            int left = limit - _replaceUsed;

            if (left <= 0 || pool.Count == 0 || p.Deck.Remaining == 0)
            {
                _replaceSeat++;
                _replaceUsed = 0;
                return;
            }

            var options = new List<Option>();
            for (int i = 0; i < pool.Count; i++)
            {
                CardInstance c = pool[i];

                // 开局那一趟的 pool 就是本座自己的手牌，天然干净；
                // 补牌那一趟的 pool 是「双方这回合补到的牌」混在一起（见 DoDrawPhase），
                // 所以必须按归属过一遍 —— 否则对面刚补的牌会变成你的可替换候选：
                // 玩家侧是看得见却换不掉的假选项，AI 侧则会挑中玩家的牌、在 DoReplace 里
                // 被归属检查静默跳过，白白浪费这次替换。铁律第 3 条：非法项在生成时就滤掉。
                if (!_replaceIsInitial && c.OwnerSeat != p.Seat)
                {
                    continue;
                }

                // 2026-10-31：只列**此刻还在手上**的牌。逐张换之后，上一张已经被替换掉
                // （Zone 回到 Deck），它仍留在本趟的 _replaceTargets 里 —— 不挡掉就会变成一个
                // 点了没反应的假选项（补牌那趟尤其明显：候选只有那一张）。
                if (c.Zone != CardZone.Hand)
                {
                    continue;
                }

                options.Add(new Option
                {
                    Kind = OptionKind.Replace,
                    Seat = p.Seat,
                    Card = c,
                    Label = "替换 " + c.Def.Name + "（" + c.Def.PowerText + "/" + c.Def.Cooldown + "）",
                });
            }

            // 过滤后本座没有任何可替换的牌 —— 这一趟没什么可问的，直接轮下一位。
            if (options.Count == 0)
            {
                _replaceSeat++;
                _replaceUsed = 0;
                return;
            }

            int candidateCount = options.Count;

            options.Add(new Option
            {
                Kind = OptionKind.Done,
                Seat = p.Seat,
                Label = _replaceIsInitial ? "不替换，开始对局" : "保留这张，继续",
            });

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = p.Seat,
                Kind = RequestKind.ChooseReplace,
                Prompt = _replaceIsInitial
                    ? "把要换掉的手牌向上拖进换牌区（还可换 " + left + " 张）"
                    : "把这手牌向上拖进换牌区即可换掉它",
                Options = options,
                MinSelect = 0,
                // 用过滤后的候选数，不是 pool.Count —— 补牌那趟 pool 含对面的牌，
                // 拿它当上限会给出「能换 2 张其实只有 1 张可选」的错数。
                MaxSelect = Math.Min(left, candidateCount),
            };
        }

        private void DoReplace(List<Option> picked)
        {
            PlayerState p = State.Of(_replaceSeat);

            // ① 先把这批要换掉的牌收集齐（同一张只算一次，只认自己手上还没被换走的）。
            var outgoing = new List<CardInstance>();
            for (int i = 0; i < picked.Count; i++)
            {
                CardInstance old = picked[i].Card;
                if (old == null || old.OwnerSeat != p.Seat || old.Zone != CardZone.Hand)
                {
                    continue;
                }

                if (outgoing.Contains(old))
                {
                    continue;
                }

                outgoing.Add(old);
            }

            if (outgoing.Count == 0)
            {
                _replaceSeat++;
                _replaceUsed = 0;
                return;
            }

            // ② 抽新牌 —— ⚠ 这一步必须**在任何 Return 之前**做完。
            //
            //   原来的写法是「抽一张 → 立刻把旧牌回池 → 抽下一张」，于是第 2 张就可能
            //   抽到第 1 张刚退回去的牌（开局牌堆只剩 2 张时概率 50%，第 3 张更高）——
            //   玩家看到的就是「被换掉的牌几乎必然又被抽回来」（2026-10-31 用户报的正是这个）。
            //   现在整批抽完之后才回池，同一批换牌绝不会抽回自己。
            var fresh = new List<CardDef>(outgoing.Count);
            for (int i = 0; i < outgoing.Count; i++)
            {
                CardDef def = p.Deck.Draw();
                if (def == null)
                {
                    break;
                }

                fresh.Add(def);
            }

            // ③ 逐张落位；换掉的牌攒到最后统一回池。
            var spent = new List<CardInstance>(fresh.Count);
            for (int i = 0; i < fresh.Count; i++)
            {
                CardInstance old = outgoing[i];
                var inst = State.CreateCard(fresh[i], p.Seat);
                inst.ToHand();

                int idx = p.Hand.IndexOf(old);
                if (idx >= 0)
                {
                    p.Hand[idx] = inst;
                }
                else
                {
                    p.Hand.Add(inst);
                }

                old.ToPool();
                spent.Add(old);

                Emit(new ReplaceEvent { Seat = p.Seat, Returned = old, Gained = inst });
            }

            for (int i = 0; i < spent.Count; i++)
            {
                p.Deck.Return(spent[i].Def);
            }

            // ④ 还有额度、牌堆里也还有牌 → **原地重发这一次替换决策**。
            //
            //   这就是「拖一张换一张」：玩家把一张牌拖进换牌区，界面不会整个收掉，
            //   而是刷成剩下可换的张数，等着接下一张。额度用完（或牌堆空了）才轮到下一位。
            _replaceUsed += spent.Count;

            int limit = _replaceIsInitial
                ? InitialReplaceLimitOf(p)
                : ReplaceLimitOnTurnOf(p, State.TurnNumber);

            if (_replaceUsed < limit && p.Deck.Remaining > 0)
            {
                _phase = Phase.AwaitReplace;
                return;
            }

            _replaceSeat++;
            _replaceUsed = 0;
        }

        private int OwnedCardCount(PlayerState player)
        {
            int count = player.Hand.Count + player.CoolingZone.Count;
            if (_atk != null && _atk.Card != null && _atk.Card.OwnerSeat == player.Seat && _atk.Card.Zone == CardZone.InPlay) count++;
            foreach (CardInstance card in _defenseCards)
                if (card.OwnerSeat == player.Seat && card.Zone == CardZone.InPlay) count++;
            return count;
        }

        private CardInstance DrawCardToHand(PlayerState p, string reason)
        {
            if (p.IsDead || OwnedCardCount(p) >= BattleState.HandLimit)
            {
                return null;
            }

            CardDef def = p.Deck.Draw();
            if (def == null)
            {
                return null;
            }

            var inst = State.CreateCard(def, p.Seat);
            inst.ToHand();
            p.Hand.Add(inst);
            Emit(new CardDrawnEvent { Seat = p.Seat, Card = inst, Reason = reason });
            return inst;
        }

        // ══════════════════════════════════════════════════════
        //  回合 / 半场骨架
        // ══════════════════════════════════════════════════════

        private void DoBeginTurn()
        {
            if (_drawObservationStart > 0 && State.TurnNumber >= _drawObservationStart + DrawObservationTurns - 1)
            {
                FinishByRule(new int[0], "平局：连续四个完整回合生命未减少");
                return;
            }
            // Complete the previous full turn before applying the mode-specific outcome.
            if (MaxTurns > 0 && State.TurnNumber >= MaxTurns)
            {
                var winners = new List<int>();
                if (_turnLimitLoser >= 0)
                    foreach (PlayerState player in State.Players)
                        if (State.Mode.AreEnemies(State, _turnLimitLoser, player.Seat)) winners.Add(player.Seat);
                FinishByRule(winners, _turnLimitLoser >= 0
                    ? "回合上限：玩家未能在 " + MaxTurns + " 回合内取胜"
                    : "双方均未能在 " + MaxTurns + " 回合内取胜");
                return;
            }

            State.TurnNumber++;
            State.AttackerSeat = State.Mode.FirstActor(State);
            int skipped = 0;
            while (State.AttackerSeat >= 0 && State.Of(State.AttackerSeat).IsDead)
            {
                if (++skipped > State.Players.Count) throw new InvalidOperationException("模式的行动顺序形成无效循环。");
                State.AttackerSeat = State.Mode.NextActor(State, State.AttackerSeat);
            }
            if (State.AttackerSeat < 0) throw new InvalidOperationException("模式未提供存活的行动角色。");
            State.InCombo = false;
            State.ComboDepth = 0;

            // 回合宣告拍（2026-11-01）：先把「进入第 N 回合」发出去，补牌排在它后面。
            //   引擎口径一直是「TurnNumber 自增之后才补」，问题在演出 —— 事件批量回放，
            //   表现层若在补牌那一拍之前读引擎，就会把第 N 回合的牌提前画在第 N−1 回合里。
            //   这一条就是那条边界：界面据此先亮出「第 N 回合」，再让补牌进场。
            Emit(new RoundBeganEvent());

            // 刻印 · TurnStart（2026-10-07）：双方已点亮的刻印各触发一次（火炬 at）。
            //   插在这里 = 「回合开始」最靠前的落点；真正的结算由 Advance 的
            //   AdvanceSpecialEffects 在下一拍之前跑完 —— 所以本回合的补牌与替换决策
            //   排在它后面（玩家先看到刻印的结算，再被问要不要换牌）。
            //   ⚠ PublishSigils 只**入队**，不动冷却区列表，可以安全地在遍历前后调用。
            for (int sigilSeat = 0; sigilSeat < State.Players.Count; sigilSeat++)
            {
                if (!State.Of(sigilSeat).IsDead)
                {
                    PublishSigils(sigilSeat, SigilTiming.TurnStart);
                }
            }

            // 按座位各自判断本回合补不补（2026-10-03）：人类第 2–3 回合各补 1 张，
            // 怪物（开局就抽满 8 张）在这里一张都不补，于是也不会进入替换拍。
            var drawn = new List<CardInstance>();
            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                PlayerState p = State.Of(seat);
                int draw = DrawOnTurnOf(p, State.TurnNumber);
                for (int i = 0; i < draw; i++)
                {
                    CardInstance c = DrawCardToHand(p, "回合 " + State.TurnNumber + " 补牌");
                    if (c != null)
                    {
                        drawn.Add(c);
                    }
                }
            }

            _replaceIsInitial = false;
            _replaceNextPhase = Phase.BeginHalfTurn;
            BeginReplacePass(drawn);
        }

        private void DoBeginHalfTurn()
        {
            if (State.IsOver)
            {
                _phase = Phase.Finished;
                return;
            }

            // 先宣告半场开始，再结算冷却 −1 —— 这样事件流的因果顺序与规则 §2 一致
            Emit(new TurnStartedEvent
            {
                Seat = State.AttackerSeat,
                IsComboFollowUp = State.InCombo,
            });

            if (!State.InCombo)
            {
                // 角色能力（含「怪物回合行为」，2026-10-03）先跑：它可能回血 / 抽牌 /
                // 强制伤害 / 自爆 / 要求本拍不出牌。原来的 `ApplyAbilities(TurnStarted)`
                // 就是这条路的一个子集，合并进 ApplyTurnBehaviours 免得跑两遍。
                _skipAttack = false;
                ApplyTurnBehaviours(State.AttackerSeat);
                if (State.IsOver)
                {
                    _phase = Phase.Finished;
                    return;
                }

                // 进攻开始时：进攻方自己的冷却区全体 −1（每个半场只结算一次，P2）
                // ⚠ 这一条在「不出牌」时照样结算 —— 那一拍仍是它的一次进攻半场，
                //   冷却该走的表要走，只是它没交牌。
                _cooldownBuffer.Clear();
                CooldownOps.TickOnAttackStart(State.Of(State.AttackerSeat), _cooldownBuffer);
                FlushCooldown();

                if (_skipAttack)
                {
                    // 主动放弃这一拍：直接进收尾（NextActor），**不走** DoAnnounceHalfTurn ——
                    // 那条路会把「没牌」当成规则 §3 的「无法攻击 → 掉 1 点」。
                    _phase = Phase.EndHalfTurn;
                    return;
                }

                GoToTriggerOr(Phase.AnnounceHalfTurn);
                return;
            }

            _phase = Phase.AnnounceHalfTurn;
        }

        private void DoAnnounceHalfTurn()
        {
            if (State.IsOver)
            {
                _phase = Phase.Finished;
                return;
            }

            int attacker = State.AttackerSeat;

            if (State.Of(attacker).Hand.Count == 0)
            {
                // 手上完全没牌才构成「无法攻击」→ 掉 1 点，该半场结束（规则 §3）
                if (!State.InCombo) ApplyDamage(attacker, 1, "手上无牌，无法攻击");
                _phase = State.IsOver ? Phase.Finished : Phase.EndHalfTurn;
                return;
            }

            _atk = new AttackContext
            {
                AttackerSeat = attacker,
                DefenderSeat = State.Mode.SelectDefender(State, attacker),
                IsFollowUp = State.InCombo,

                // 虚弱：开拍时取一份层数快照（递减发生在半场收尾，半场进行中不变）。
                // 连击的追加进攻会新建一次 AttackContext，取到的仍是同一个值 —— 口径一致。
                WeakenStacks = State.Of(attacker).WeakenStacks,
            };

            // 准备使用的光环是「本半场」的暂存：连击追加进攻是全新的一次进攻，
            // 上一次剩下的准备集合绝不能带进这一拍（否则会凭空消耗指示物）。
            _preparedAuras.Clear();
            _pendingRetryNote = null;

            _phase = Phase.AwaitAttackCard;
        }

        private void DoEndHalfTurn()
        {
            State.InCombo = false;
            State.ComboDepth = 0;

            if (State.IsOver)
            {
                _phase = Phase.Finished;
                return;
            }

            // 刻印 · AttackEnd（2026-10-07）：刚刚完成进攻的那一方，已点亮的刻印各触发一次
            //   （海啸 au「每次进攻结束时获得一次加速」）。
            //   位置与虚弱递减**同一口径**：连击的追加进攻走的是 Stage6 → BeginHalfTurn 的循环，
            //   只有整条链走完才会回到本方法，所以「一次进攻半场」= 整条连击链，只算一次。
            //   ⚠ 点亮它的那一拍就会走到这里，所以是「当拍即触发」（用户 2026-10-07 口径）。
            PublishSigils(State.AttackerSeat, SigilTiming.AttackEnd);

            // 虚弱递减：刚刚完成进攻的那一方 −1 层（2026-09-29）。
            // 放在这里而不是 Stage6，是因为连击的追加进攻会再次回到 BeginHalfTurn ——
            // 一次「进攻半场」= 整条连击链，只该减 1 层。
            DecayWeaken(State.AttackerSeat);

            int next = State.Mode.NextActor(State, State.AttackerSeat);
            int skipped = 0;
            while (next >= 0 && State.Of(next).IsDead)
            {
                if (++skipped > State.Players.Count) throw new InvalidOperationException("模式的行动顺序形成无效循环。");
                next = State.Mode.NextActor(State, next);
            }
            if (next >= 0)
            {
                State.AttackerSeat = next;
                _phase = Phase.BeginHalfTurn;
            }
            else _phase = Phase.BeginTurn;
        }

        /// <summary>
        /// 虚弱递减：被施加者<b>自己的一个进攻半场结束之后</b> −1 层（2026-09-29 用户口径）。
        ///
        /// <para><b>为什么在 <see cref="DoEndHalfTurn"/></b>：那里是「一个进攻半场真正收尾」的
        /// 唯一落点 —— 连击的追加进攻走的是 <c>Stage6 → BeginHalfTurn</c> 的循环，
        /// 只有整条链走完才会进来，所以一次完整的进攻（含连击）只减 1 层。</para>
        ///
        /// <para>「手牌为空、无法进攻 → 掉 1 点」那条路径也落到这里，同样算一次进攻结束
        /// —— 与卡面「在其进攻结束之后」的字面口径一致。</para>
        /// </summary>
        private void DecayWeaken(int seat)
        {
            if (seat < 0 || seat >= State.Players.Count)
            {
                return;
            }

            PlayerState player = State.Of(seat);
            if (player.WeakenStacks <= 0)
            {
                return;
            }

            player.WeakenStacks--;
            Emit(new WeakenDecayedEvent { Seat = seat, Stacks = player.WeakenStacks });
        }

        // ══════════════════════════════════════════════════════
        //  γ 触发（潮汐：冷却完毕时使另一张牌立即冷却完成）
        // ══════════════════════════════════════════════════════

        private void GoToTriggerOr(Phase next) { _phase = next; }

        private void IssueAttackCard()
        {
            PlayerState attacker = State.Of(_atk.AttackerSeat);
            var options = new List<Option>();

            for (int i = 0; i < attacker.Hand.Count; i++)
            {
                CardInstance c = attacker.Hand[i];
                options.Add(new Option
                {
                    Kind = OptionKind.PlayCard,
                    Seat = attacker.Seat,
                    Card = c,
                    Value = c.EffectivePower,
                    // ⚠ 用实例的 PowerLabelText（不是 c.Def.PowerText）：水之形（aq）成长之后
                    //   这一行必须与它卡面上画的力量一致，否则同屏两处数字对不上。
                    Label = "打出 " + c.Def.Name + "（力量 " + c.PowerLabelText
                            + " · 冷却 " + c.Def.Cooldown + "）",
                });
            }

            // 2026-09-18：光环不再单独占一拍。玩家把 HudBuff 里的图标拖到判定区即「准备使用」，
            //   出牌那一刻随牌一并回填（DecisionResponse.AuraOptionIndices）。
            //   这里**不做**「哪张牌配哪个光环」的预筛 —— 牌还没定；精筛放在 DoChooseAttackCard，
            //   那里才拿得到最终打出的牌（连击阈值、沉重打击的「力量不能增加」都依赖它）。
            AppendAuras(options, AuraResolver.BuildOptions(attacker, AuraContext.Attack));

            // 2026-10-29：**有牌也可以不出**。用户口径：进攻是一次决策，不是义务 ——
            //   「一张牌都还没打出」和「连击打了一半不想再打」都要能主动收尾。
            //   所以只要手上有牌就补一条 Skip。
            // Value carries the actual life cost to the UI; ending a follow-up is free.
            if (attacker.Hand.Count > 0)
            {
                options.Add(new Option
                {
                    Kind = OptionKind.Skip,
                    Seat = attacker.Seat,
                    Label = "结束进攻",
                    Value = _atk.IsFollowUp ? 0 : 1,
                });
            }

            AppendDrawProposal(options);
            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = attacker.Seat,
                Kind = RequestKind.ChooseAttackCard,
                Prompt = attacker.Hand.Count > 0
                    ? "选择本次进攻打出的法术（也可以结束进攻 · 掉 1 点）"
                    : "手上无牌，无法进攻",
                Options = options,
                MinSelect = 1,
                MaxSelect = 1,
                ContextNoSkip = attacker.Hand.Count == 0,
            };
        }

        private void DoChooseAttackCard(List<Option> picked, List<Option> auras)
        {
            PlayerState attacker = State.Of(_atk.AttackerSeat);

            Option chosen = null;
            for (int i = 0; i < picked.Count; i++)
            {
                // 光环选项也带 Card（= 来源牌），不能当出牌选项用
                if (picked[i].AuraSource == null && picked[i].Card != null)
                {
                    chosen = picked[i];
                    break;
                }
            }

            // 2026-10-29：选项里既然给了「结束进攻」，**没选牌就等于结束进攻** ——
            //   原来那条「兜底打第一张」必须删掉：它会让玩家点了「结束」却还是打出去一张，
            //   而且零报错（上层看起来一切正常）。
            //
            // 2026-10-30 用户口径（回退 10-29 的「结束进攻不掉血」）：
            //   **进攻半场一张牌都没打出 = 这次进攻失败，强制掉 1 点**。
            //   所以「主动结束」与「手上无牌」现在走同一条结算，只是日志里的原因不同；
            //   两处都是先扣血、再按终局与否决定收尾，光环不消耗（本拍根本没出牌）。
            if (chosen == null)
            {
                if (!_atk.IsFollowUp)
                    ApplyDamage(_atk.AttackerSeat, 1,
                        attacker.Hand.Count > 0 ? "未出牌，进攻失败" : "手上无牌，无法攻击");
                _preparedAuras.Clear();
                _phase = State.IsOver ? Phase.Finished : Phase.EndHalfTurn;
                return;
            }

            CardInstance card = chosen.Card;
            attacker.Hand.Remove(card);
            card.Zone = CardZone.InPlay;

            _atk.Card = card;
            _atk.BasePower = card.EffectivePower;
            SplitEffects(card);
            _atk.EffectBonus += ApplyAbilities(attacker.Seat, AbilityTrigger.AttackPower);
            Emit(new AttackDeclaredEvent { Seat = _atk.AttackerSeat, Card = card });

            ApplyPreparedAttackAuras(auras);
            _preparedAuras.Clear();
            _phase = Phase.Stage1;
        }

        /// <summary>把进攻牌的 α 效果分派到 ① / ③ / ④ / ⑤ 各阶段。</summary>
        private void SplitEffects(CardInstance card)
        {
            _effects.Clear();
            _effectContexts.Clear();
            _effectWindow?.Dispose();
            _effectWindow = null;
            AddCardEffects(card, EffectTrigger.Attack, _atk.DefenderSeat, State.Of(card.OwnerSeat).Hand.Count + 1);
        }

        // ⚠ 「卡面是否带『进攻力量不能增加』（沉重打击）」这条判断 2026-09-30 起**只住在**
        //   `AuraResolver.CollectRefunded` 里（它与「连击阈值」「连击不可叠加」合在一处，
        //   引擎与界面共用同一份判据）。原来这里的 `CardForbidsAtkBuff` 包装因此删掉 ——
        //   留着就是第二处口径。卡表侧的读数仍走 `CardDef.ForbidsAtkBuff`。

        // ══════════════════════════════════════════════════════
        //  光环：准备 → 随出牌一并提交（2026-09-18）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 玩家在判定区「准备使用」的光环（本拍的暂存）。
        ///
        /// <para><b>为什么要存在引擎里而不是只放 UI</b>：防御牌的合法性直接取决于这份集合 ——
        /// 「这张牌还需要几点防御力量」是拿已准备的光环加值算出来的
        /// （<see cref="DefenseResolver.BuildOptions"/> 的 <c>appliedDefenseBonus</c>）。
        /// 只有引擎知道它，界面才可能只渲染合法选项（铁律 3）。</para>
        ///
        /// <para>玩家每拖动一枚图标，UI 就回填一次「只更新光环」的响应
        /// （<see cref="DecisionResponse.AuraPrepOnly"/>），引擎按新集合重发同一拍决策；
        /// 真正出牌时再连同主选择一次性提交。</para>
        /// </summary>
        private readonly List<Option> _preparedAuras = new List<Option>();

        /// <summary>
        /// <see cref="ApplyPreparedAttackAuras"/> 的复用缓冲：本拍「准备了、但最终不消耗」的那几枚
        /// 光环在 <c>auras</c> 里的下标（由 <see cref="AuraResolver.CollectRefunded"/> 填）。
        /// </summary>
        private readonly List<int> _refundedAuras = new List<int>();

        /// <summary>
        /// 上一拍被驳回的原因（例如「准备的光环加值还不够」）—— 在下一次发同一条决策时并进提示文案里。
        /// 只在重发同一条决策时消费一次，用完即清。
        /// </summary>
        private string _pendingRetryNote;

        /// <summary>把一批选项按「光环选项」补进主列表（同时修好序号）。</summary>
        private static void AppendAuras(List<Option> options, List<Option> auras)
        {
            if (options == null || auras == null)
            {
                return;
            }

            for (int i = 0; i < auras.Count; i++)
            {
                if (auras[i] != null)
                {
                    options.Add(auras[i]);
                }
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }
        }

        /// <summary>提示条后缀：本拍准备了几枚光环、合计多少加值（让玩家随时看得见自己准备到什么程度）。</summary>
        private string PreparedHint()
        {
            if (_preparedAuras.Count == 0)
            {
                return string.Empty;
            }

            int bonus = PreparedAuraBonus();
            string text = "　·　已准备光环 " + _preparedAuras.Count + " 枚";

            if (bonus > 0)
            {
                text += "（力量 +" + bonus + "）";
            }

            return text + "，拖出判定区可取消";
        }

        /// <summary>已准备光环里「力量加值类」的合计（免疫 / 连击不计入力量预算）。</summary>
        private int PreparedAuraBonus()
        {
            int bonus = 0;

            for (int i = 0; i < _preparedAuras.Count; i++)
            {
                Option o = _preparedAuras[i];
                if (o != null && AuraResolver.IsPowerBonus(o.AuraKind))
                {
                    bonus += o.Value;
                }
            }

            return bonus;
        }

        /// <summary>
        /// 把准备的光环逐枚结算到本次进攻上。
        ///
        /// <para><b>为什么要在这里做二次筛选</b>：准备发生在选牌之前，engine 当时还不知道
        /// 最终打出的是哪张牌，而两条规则是依赖那张牌的 ——
        /// 「连击：基础力量 ≤ 阈值」与「沉重打击：此法术的进攻力量不能增加」。
        /// 不满足的**既不生效也不消耗**（回填序号来自本拍选项，引擎只认其中当下仍成立的部分）。</para>
        /// </summary>
        private void ApplyPreparedAttackAuras(List<Option> auras)
        {
            if (auras == null || auras.Count == 0)
            {
                return;
            }

            // 「准备了、但最终不消耗」的那几枚由 AuraResolver 一处判：① 沉重打击（整批不进）、
            // ② 连击阈值（基础力量 > A）、③ 连击不可叠加（2026-09-30 用户口径）。
            // ⚠ 界面在同一时刻用**同一个函数**算「要播归位动画」的那几枚
            //   （BattleUi.RefundUnusedAuras）—— 两边各写一份判据迟早分叉成
            //   「图标回到原位了、指示物却已经被消耗」。
            AuraResolver.CollectRefunded(auras, _atk.Card == null ? null : _atk.Card.Def, _refundedAuras);

            for (int i = 0; i < auras.Count; i++)
            {
                if (_refundedAuras.Contains(i))
                {
                    continue;
                }

                Option o = auras[i];
                ApplyAuraOnAttack(o.AuraSource, o.AuraKind, o.Value, o.AuraTokenIndex);
            }
        }

        private void ApplyAuraOnAttack(CardInstance source, AuraKind kind, int value, int tokenId)
        {
            if (!AuraResolver.Consume(source, tokenId)) return;

            switch (kind)
            {
                case AuraKind.AtkPower:
                case AuraKind.AtkOrDefPower:
                    _atk.AuraBonus += value;
                    break;

                case AuraKind.Combo:
                    // 引雷：本次打出的法术基础力量 ≤ A 时获得连击（不叠加）
                    if (_atk.Card.Def.Power <= value)
                    {
                        _atk.HasCombo = true;
                    }

                    break;

                case AuraKind.QuickRefill:
                    // 击穿：把「快速回填」记到本次打出的那张牌的冷却结算上 ——
                    //   第 ④ 步（DoStage4）读的是该牌 EffectContext.CooldownReduction，
                    //   拿基础冷却减掉这么多。⚠ 必须写进**那张牌的 context**，
                    //   而不是 _atk 上的某个字段：快速回填的语义就是「这一张进冷却时少等一回合」，
                    //   与 <see cref="EffectOp.QuickRefill"/> 走的是同一条落点。
                    //   那张牌的 context 在 SplitEffects 里已经建好（本方法必然晚于它）。
                    if (_effectContexts.TryGetValue(_atk.Card.Uid, out EffectContext quickRefillContext))
                    {
                        quickRefillContext.CooldownReduction += value;
                    }

                    break;
            }

            Emit(new AuraConsumedEvent
            {
                Seat = source.OwnerSeat,
                Source = source,
                Kind = kind,
                Value = value,
                TargetCard = _atk.Card,
            });
        }

        // ══════════════════════════════════════════════════════
        //  ① 力量与结构效果
        // ══════════════════════════════════════════════════════

        private void RunStage1() { RunEffects(EffectStage.Power, FinishStage1); }

        private void FinishStage1()
        {
            if (State.IsOver)
            {
                _phase = Phase.Finished;
                return;
            }

            Emit(new AttackPowerResolvedEvent
            {
                Seat = _atk.AttackerSeat,
                Card = _atk.Card,
                BasePower = _atk.BasePower,
                BonusPower = _atk.BonusPower,
                IsDouble = _atk.IsDouble,

                // 虚弱层数必须随事件一起发出去：界面直接印 FinalPower，
                // 少了它就会出现「提示条说 7、实际只打 4」。
                WeakenStacks = _atk.WeakenStacks,
            });

            // 本次防御的光环预算完全来自玩家在判定区准备的那几枚；
            // 未准备的指示物不参与预算（规则 §6.2）。
            _defensePower = 0;
            _phase = Phase.AwaitDefense;
        }

        // ② 防御：光环与防御牌在同一拍里提交。
        //
        // 2026-09-18 之前这里是一个独立的「先逐枚选光环、再选防御牌」环节
        //（Phase.AwaitDefenseAura / RequestKind.ChooseAuraUse）。用户口径改为：
        // 不设光环选择环节，玩家在防御时可随时把 HudBuff 图标拖到判定区「准备」，
        // 出牌那一刻连同光环一起提交；拖出去即取消。所以那一拍整个删掉了 ——
        // 光环选项现在并进 ChooseDefense 的 Options（见 IssueDefense）。
        //
        // ⚠ 由此产生的一个必然结果：防御牌的合法性依赖「已准备的光环加值」，
        //   所以每准备 / 取消一枚，UI 都要回填一次 AuraPrepOnly 让引擎按新预算重发本拍。
        private void IssueDefense()
        {
            int defenderSeat = _atk.DefenderSeat;
            PlayerState defender = State.Of(defenderSeat);

            // 光环预算 = 玩家此刻在判定区准备的那几枚（未准备的不参与，规则 §6.2）。
            _defensePower = PreparedAuraBonus() + ApplyAbilities(defenderSeat, AbilityTrigger.DefensePower);

            List<Option> options = DefenseResolver.BuildOptions(
                State, defenderSeat, _atk.FinalPower, _atk.IsDouble, _defensePower);

            AppendAuras(options, BuildDefenseAuras(defender));
            AppendDrawProposal(options);

            string prompt = (_atk.IsDouble
                                ? "对方双发（力量 " + _atk.FinalPower + "）：需交出两张够挡的牌，否则放弃防御掉 1 点"
                                : "对方进攻力量 " + _atk.FinalPower + "：出一张够挡的牌或放弃防御")
                            + PreparedHint();

            if (!string.IsNullOrEmpty(_pendingRetryNote))
            {
                prompt = _pendingRetryNote + "　·　" + prompt;
            }

            _pendingRetryNote = null;

            Pending = new DecisionRequest
            {
                Seat = defenderSeat,
                Kind = RequestKind.ChooseDefense,
                Prompt = prompt,
                Options = options,
                MinSelect = 0,
                MaxSelect = 1,
                ContextPower = _atk.FinalPower,
                ContextDefenseBonus = _defensePower,
                ContextDouble = _atk.IsDouble,
                // ContextImmune 恒为 false：免疫不再是一拍决策，直接在提交时结算并跳过防御。
            };
        }

        /// <summary>
        /// 本拍可用的防御光环选项。过滤口径与旧 <c>IssueDefenseAura</c> 完全一致，只是换了落点：
        /// <list type="bullet">
        /// <item>区间不含本次攻击力量的免疫光环不给（给了也用不上）；</item>
        /// <item>手上没牌时力量加成没有可加的牌、不给 —— 免疫仍然可用（规则 §6.5：空手也能免疫）。</item>
        /// </list>
        /// </summary>
        private List<Option> BuildDefenseAuras(PlayerState defender)
        {
            var usable = new List<Option>();
            List<Option> available = AuraResolver.BuildOptions(defender, AuraContext.Defend);

            for (int i = 0; i < available.Count; i++)
            {
                Option o = available[i];

                if (o.AuraKind == AuraKind.ImmuneHigh && _atk.FinalPower < o.Value)
                {
                    continue;
                }

                if (o.AuraKind == AuraKind.ImmuneLow && _atk.FinalPower > o.Value)
                {
                    continue;
                }

                if (AuraResolver.IsPowerBonus(o.AuraKind) && defender.Hand.Count == 0)
                {
                    continue;
                }

                usable.Add(o);
            }

            return usable;
        }

        /// <summary>
        /// 防御提交。同一拍里同时带着「出哪张牌」与「准备使用的光环」。
        ///
        /// <para><b>三条去向</b>：</para>
        /// <list type="number">
        /// <item>准备了能覆盖本次力量的<b>免疫</b>光环 → 整个攻击（含双发）被免疫，
        /// 不交牌、跳过防御（规则 §6.5，推论 P1 已作废）；</item>
        /// <item>交了牌 → 逐枚消耗准备的力量光环，记入本次防御预算，再按规则校验合法性；</item>
        /// <item>没交牌 → 放弃防御（准备的光环<b>不消耗</b>：加成只作用于「当时正在打出的那一张牌」，
        /// 没有牌就没有作用对象）。</item>
        /// </list>
        /// </summary>
        private void DoChooseDefense(List<Option> picked, List<Option> auras)
        {
            int defenderSeat = _atk.DefenderSeat;
            PlayerState defender = State.Of(defenderSeat);

            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                // 光环选项也带 Card（= 来源牌），不能当防御牌用
                if (!picked[i].IsSkip && picked[i].AuraSource == null && picked[i].Card != null)
                {
                    pick = picked[i];
                    break;
                }
            }

            // 免疫优先：一旦准备（= 主动使用）就整个攻击被免疫，连牌都不用交。
            for (int i = 0; i < auras.Count; i++)
            {
                Option immune = auras[i];
                if (immune.AuraKind != AuraKind.ImmuneHigh && immune.AuraKind != AuraKind.ImmuneLow)
                {
                    continue;
                }

                ConsumeDefenseAuras(auras, i, null);
                ResolveImmune(defenderSeat, immune);
                return;
            }

            _defenseCards.Clear();

            if (pick == null)
            {
                // 放弃防御（或手上确实没有能挡的牌）→ 掉 1 点（双方都答 A1/A5：只扣 1 点）
                List<Option> all = DefenseResolver.BuildOptions(
                    State, defenderSeat, _atk.FinalPower, _atk.IsDouble, _defensePower);
                _gaveUp = DefenseResolver.HasAnyBlock(all);
                _defenseSuccess = false;
                _preparedAuras.Clear();
            }
            else
            {
                // 合法性再校验一次：选项是按「已准备的光环预算」算出来的，
                // 但回填来自上层，可能带着已经过期的准备集合。
                // ⚠ 手牌数取「还没把它从手上摘掉」的那个数 —— 与 IssueDefense 里
                //   DefenseResolver.BuildOptions 用的是同一个口径（Bis 手牌数含正在打出的这一张，
                //   用户 2026-10-07 拍板）。两处不一致 = 「选项给了、提交却被打回」。
                int need = DefenseResolver.RequiredBonus(pick.Card, _atk.FinalPower, defender.Hand.Count);
                if (pick.PairCard != null)
                {
                    need += DefenseResolver.RequiredBonus(pick.PairCard, _atk.FinalPower, defender.Hand.Count);
                }

                if (need > _defensePower)
                {
                    // 挡不住 → 不消耗、不出牌，把本拍原样重发一次（提示条说明差多少）。
                    // 绝不「打出一张挡不住的牌」：那是规则 §3 的硬性约束。
                    _pendingRetryNote = "当前防御效果不足：需要补值 +" + need + "，已准备 +" + _defensePower;
                    _phase = Phase.AwaitDefense;
                    return;
                }

                ConsumeDefenseAuras(auras, -1, pick.Card);

                _gaveUp = false;
                _defenseSuccess = true;

                var toPlay = new List<CardInstance>();
                toPlay.Add(pick.Card);
                if (pick.PairCard != null)
                {
                    toPlay.Add(pick.PairCard);
                }

                _cooldownBuffer.Clear();
                for (int i = 0; i < toPlay.Count; i++)
                {
                    CardInstance c = toPlay[i];
                    defender.Hand.Remove(c);
                    c.Zone = CardZone.InPlay;
                    _defenseCards.Add(c);
                    AddCardEffects(c, EffectTrigger.Defend, _atk.AttackerSeat, defender.Hand.Count + i + 1);
                }

                _preparedAuras.Clear();
            }

            Emit(new DefenseResolvedEvent
            {
                DefenderSeat = defenderSeat,
                AttackPower = _atk.FinalPower,
                Success = _defenseSuccess,
                GaveUp = _gaveUp,
                // 走到这里必然不是「用免疫光环挡住」的那条路径 —— 那条路就地结算、
                // 根本不会进入本方法（见 DoChooseDefense 开头的免疫分支）。
                UsedImmune = false,
                Cards = new List<CardInstance>(_defenseCards),
            });

            if (!_defenseSuccess)
            {
                ApplyDamage(defenderSeat, 1, _atk.IsDouble ? "双发未被挡住" : "未挡住进攻");
            }

            _phase = State.IsOver ? Phase.Finished : Phase.Stage2Effects;
        }

        /// <summary>
        /// 逐枚消耗准备的光环（跳过 <paramref name="skipIndex"/> 那一枚）。
        /// 免疫类不在这里消耗 —— 它由 <see cref="ResolveImmune"/> 自己负责，
        /// 因为它一旦成立就跳过整个防御，别的光环都不该再动。
        /// </summary>
        private void ConsumeDefenseAuras(List<Option> auras, int skipIndex, CardInstance target)
        {
            for (int i = 0; i < auras.Count; i++)
            {
                if (i == skipIndex)
                {
                    continue;
                }

                Option o = auras[i];
                if (!AuraResolver.IsPowerBonus(o.AuraKind))
                {
                    continue;
                }

                if (!AuraResolver.Consume(o.AuraSource, o.AuraTokenIndex)) continue;
                Emit(new AuraConsumedEvent
                {
                    Seat = o.AuraSource.OwnerSeat,
                    Source = o.AuraSource,
                    Kind = o.AuraKind,
                    Value = o.Value,
                    TargetCard = target,
                });
            }
        }

        /// <summary>
        /// 免疫光环的结算（规则 §6.5）：消耗它 = <b>整个攻击（含整个双发）被免疫，
        /// 且不需要再打出一张防御牌</b>。就地判定防御成功、发出
        /// <see cref="DefenseResolvedEvent"/>，把防御选牌整拍跳过 —— 玩家省下的那张牌仍留在手上。
        ///
        /// <para>旧规则（推论 P1）要求「免疫之后仍须交一张防御牌、只是力量不参与比较」，
        /// 用户已明确作废它。</para>
        /// </summary>
        private void ResolveImmune(int defenderSeat, Option immune)
        {
            if (!AuraResolver.Consume(immune.AuraSource, immune.AuraTokenIndex)) return;
            Emit(new AuraConsumedEvent
            {
                Seat = immune.AuraSource.OwnerSeat,
                Source = immune.AuraSource,
                Kind = immune.AuraKind,
                Value = immune.Value,
                TargetCard = null,
            });

            // _defenseCards 保持为空 → 第 ④ 步不会把任何牌送进冷却区，
            // 「未防御分支」也自然不触发。
            _defenseCards.Clear();
            _defensePower = _atk.FinalPower;
            _gaveUp = false;
            _defenseSuccess = true;
            _preparedAuras.Clear();

            Emit(new DefenseResolvedEvent
            {
                DefenderSeat = defenderSeat,
                AttackPower = _atk.FinalPower,
                Success = true,
                GaveUp = false,
                UsedImmune = true,
                Cards = new List<CardInstance>(),
            });

            // 免疫期间不再扣血，所以这里不需要检查 IsOver —— 但仍走同一条收尾路径
            _phase = State.IsOver ? Phase.Finished : Phase.Stage3;
        }

        // ══════════════════════════════════════════════════════
        //  ③ 冷却改动 / 查看手牌
        // ══════════════════════════════════════════════════════

        private void RunStage3() { RunEffects(EffectStage.Cooldown, () => _phase = Phase.Stage4); }

        private void DoStage4()
        {
            _cooldownBuffer.Clear();
            var cards = new List<CardInstance>(_defenseCards);
            cards.Insert(0, _atk.Card);
            foreach (CardInstance card in cards)
            {
                if (card == null || card.Zone != CardZone.InPlay) continue;
                int reduction = _effectContexts.TryGetValue(card.Uid, out var context) ? context.CooldownReduction : 0;
                CooldownOps.PutIntoCooldown(State.Of(card.OwnerSeat), card, Math.Max(0, card.Def.Cooldown - reduction),
                    _cooldownBuffer, card == _atk.Card ? "本次进攻牌进入冷却区" : "本次防御牌进入冷却区");
            }
            FlushCooldown();
            _phase = State.IsOver ? Phase.Finished : Phase.Stage4Effects;
        }

        private void DoStage5()
        {
            // 刻印点亮（2026-10-07）：第 ⑤ 步正好是「本牌已经进了冷却区（第 ④ 步）、
            //   但这一拍还没收尾」那一刻 —— 与光环激活同一拍，所以「打了才亮、回手就灭」
            //   这条语义天然成立（熄灭在 CardInstance / CooldownOps 里）。
            //   只有**作为进攻牌打出**的那一张会被点亮（见 LightSigils）。
            LightSigils(_atk.Card);
            RunEffects(EffectStage.Aura, () => _phase = Phase.Stage6);
        }

        private void DoStage6()
        {
            if (State.IsOver)
            {
                _phase = Phase.Finished;
                return;
            }

            PlayerState attacker = State.Of(_atk.AttackerSeat);
            bool chain = _atk.HasCombo && attacker.Hand.Count > 0 && State.ComboDepth < MaxComboChain;

            if (chain)
            {
                State.InCombo = true;
                State.ComboDepth++;
                _phase = Phase.BeginHalfTurn;
            }
            else
            {
                State.InCombo = false;
                _phase = Phase.EndHalfTurn;
            }
        }

        // ══════════════════════════════════════════════════════
        //  刻印（Sigil）· 2026-10-07
        // ══════════════════════════════════════════════════════
        //
        //  用户口径：刻印「类似于光环」—— 挂在提供它的那张牌上、**只有进攻时会点亮**、
        //  回到手牌时效果消失（= 与光环完全相同的那三条）；与光环的唯一差别是
        //  **点亮之后每回合都会自动触发其效果**（光环是一次性指示物，用掉就没了）。
        //
        //  落在三个点位上（都在本文件里，一眼看得全）：
        //    · 点亮 —— DoStage5 调 LightSigils（第 ⑤ 步，与光环激活同一拍）；
        //    · 触发 —— DoBeginTurn（TurnStart）/ DoEndHalfTurn（AttackEnd）调 PublishSigils；
        //    · 熄灭 —— CardInstance.ToHand/ToPool + CooldownOps.PutIntoCooldown/RemoveFromGame。
        //
        //  ⚠ 刻印**不是新算子**：卡表里写的就是普通的 Aura / Haste 效果，外加一个
        //    EffectDef.Sigil 标记。所以「效果本身长什么样」一条代码都没重写，
        //    BattleEngine.Effects.EnqueueEffects 只把它从「当拍结算」里摘出去。

        /// <summary>
        /// 点亮 <paramref name="card"/> 身上的 <b>α 刻印</b>（2026-10-07）。
        ///
        /// <para><b>为什么只认 α</b>：用户口径「刻印只有进攻时会点亮」。本方法只被
        /// <see cref="DoStage5"/> 用 <c>_atk.Card</c>（本次打出的<b>进攻牌</b>）调用，
        /// 但仍复核触发符号 —— 一张带 β 刻印的牌被当成防御牌打出时不该亮
        /// （与光环「卡面符号 = 结算时机」同一条口径，见 CardDef.AuraTokenCountOf）。</para>
        ///
        /// <para>点亮发生在<b>进冷却区之后</b>，所以那一刻它已经是 <see cref="CardZone.Cooling"/>；
        /// 不在冷却区（对局在更早的阶段就结束了）就不亮。</para>
        /// </summary>
        private void LightSigils(CardInstance card)
        {
            if (card == null || !card.IsCooling)
            {
                return;
            }

            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                EffectDef effect = card.Def.Effects[i];
                if (effect.Sigil == SigilTiming.None || effect.Trigger != EffectTrigger.Attack)
                {
                    continue;
                }

                if (card.LitSigils.Contains(effect))
                {
                    continue;   // 幂等：同一条刻印不会记两遍（静态卡表里是同一个引用）
                }

                card.LightSigil(effect);
                Emit(new SigilLitEvent { Seat = card.OwnerSeat, Card = card, Timing = effect.Sigil });
            }
        }

        /// <summary>
        /// 把 <paramref name="seat"/> 冷却区里<b>已点亮的刻印</b>中、时机等于
        /// <paramref name="timing"/> 的那几条全部排进特殊效果队列（2026-10-07）。
        ///
        /// <para><b>为什么走 <c>_pendingSpecial</c> 而不是 <c>_effects</c></b>：<c>_effects</c>
        /// 是「本次攻防的效果表」、按 <see cref="EffectStage"/> 分五步跑；而刻印的触发点在
        /// <b>五步之外</b>（回合开始 / 半场收尾），而且海啸的「获得一次加速」要<b>弹一次选目标</b>
        /// —— 半场收尾那一刻弹决策只能靠暂停-恢复管线。γ「冷却完毕」（潮汐 e）走的就是同一条
        /// 管线（<c>AdvanceSpecialEffects</c>），AI / UI 两侧完全无感。</para>
        ///
        /// <para><b>本方法只入队、不结算</b>（所以可以安全地在遍历冷却区的前后调用）；
        /// 真正的结算由 <c>Advance</c> 循环里的 <c>AdvanceSpecialEffects</c> 在下一拍之前跑完。</para>
        ///
        /// <para>⚠ 传入的 <c>EffectTrigger.Special</c> 只影响 <see cref="EffectContext.Trigger"/>：
        /// 刻印的载荷（Aura / Haste）都不读它（读它的 <c>AddAttackPower</c> / <c>GrantCombo</c> /
        /// <c>CopyAttack</c> 都不是刻印会用的算子）。</para>
        /// </summary>
        private void PublishSigils(int seat, SigilTiming timing)
        {
            if (timing == SigilTiming.None || seat < 0 || seat >= State.Players.Count || State.IsOver)
            {
                return;
            }

            PlayerState player = State.Of(seat);
            for (int c = 0; c < player.CoolingZone.Count; c++)
            {
                CardInstance card = player.CoolingZone[c];
                if (!card.SigilLive)
                {
                    continue;
                }

                for (int i = 0; i < card.LitSigils.Count; i++)
                {
                    EffectDef effect = card.LitSigils[i];
                    if (effect.Sigil != timing)
                    {
                        continue;
                    }

                    _effectRegistry.Validate(effect);
                    var context = new EffectContext(this, card,
                        State.Mode.SelectDefender(State, card.OwnerSeat), EffectTrigger.Special, _atk,
                        player.Hand.Count);
                    _pendingSpecial.Add(new EffectActivation { Context = context, Definition = effect });
                    Emit(new SigilTriggeredEvent { Seat = card.OwnerSeat, Card = card, Timing = timing });
                }
            }
        }

        // ══════════════════════════════════════════════════════
        //  决策分发
        // ══════════════════════════════════════════════════════

        private void Dispatch(DecisionRequest req, List<Option> picked, List<Option> auras)
        {
            switch (req.Kind)
            {
                case RequestKind.ChooseReplace:
                    DoReplace(picked);
                    _phase = Phase.AwaitReplace;
                    break;

                case RequestKind.ChooseAttackCard:
                    DoChooseAttackCard(picked, auras);
                    break;

                case RequestKind.ChooseDefense:
                    DoChooseDefense(picked, auras);
                    break;

                default:
                    _phase = Phase.Finished;
                    break;
            }
        }

        /// <summary>
        /// 校验并取出选项。引擎<strong>不信任上层回填</strong>：越界 / 重复 / 空回填一律安全降级，
        /// 绝不因为 UI 或 AI 传了非法值而把对局卡死或破坏规则。
        /// </summary>
        private static List<Option> PickOptions(DecisionRequest req, DecisionResponse resp)
        {
            var picked = new List<Option>();
            if (resp == null || resp.OptionIndices == null || req == null)
            {
                return picked;
            }

            var seen = new HashSet<int>();
            for (int i = 0; i < resp.OptionIndices.Length; i++)
            {
                int idx = resp.OptionIndices[i];
                Option o = req.Get(idx);
                if (o == null || !seen.Add(idx))
                {
                    continue;
                }

                picked.Add(o);
                if (picked.Count >= Math.Max(1, req.MaxSelect))
                {
                    break;
                }
            }

            return picked;
        }

        /// <summary>
        /// 取出并校验「准备使用的光环」。
        ///
        /// <para><b>只有出牌那两拍读它</b>：其它决策即使上层塞了序号也一律忽略 ——
        /// 光环的入口只有一个，就是「拖到判定区、随出牌提交」。</para>
        ///
        /// <para>逐项校验（引擎不信任上层）：序号必须在本拍 <c>Options</c> 里、
        /// 必须真的是光环选项、来源牌必须还有可用余量；任一条不满足就丢掉这一项，
        /// 而不是让整次提交失败（宁可少用一枚光环，也不能让玩家卡在这一拍）。</para>
        ///
        /// <para>重复序号按一次算 —— 双光环卡的两枚指示物是<b>两个不同的序号</b>
        /// （<see cref="Option.AuraTokenIndex"/> 不同），所以去重不会误吞合法选择。</para>
        /// </summary>
        private static List<Option> PickAuras(DecisionRequest req, DecisionResponse resp)
        {
            var auras = new List<Option>();

            if (req == null || resp == null || resp.AuraOptionIndices == null)
            {
                return auras;
            }

            if (req.Kind != RequestKind.ChooseAttackCard && req.Kind != RequestKind.ChooseDefense)
            {
                return auras;
            }

            var seen = new HashSet<int>();
            for (int i = 0; i < resp.AuraOptionIndices.Length; i++)
            {
                int idx = resp.AuraOptionIndices[i];
                Option o = req.Get(idx);
                if (o == null || !seen.Add(idx))
                {
                    continue;
                }

                if (o.AuraSource == null || o.AuraKind == AuraKind.None || !o.AuraSource.HasUsableAura)
                {
                    continue;
                }

                auras.Add(o);
            }

            return auras;
        }

        // ══════════════════════════════════════════════════════
        //  伤害 / 生命 / 事件
        // ══════════════════════════════════════════════════════

        private IReadOnlyList<int> EffectSeats(int source, int target)
        {
            var seats = new List<int>();
            foreach (int seat in State.Mode.EffectSeats(State, source, target))
                if (seat >= 0 && seat < State.Players.Count && !State.Of(seat).IsDead && !seats.Contains(seat)) seats.Add(seat);
            seats.Sort();
            return seats;
        }

        private int EffectHpLost(int source, int target)
        {
            int total = 0;
            foreach (int seat in EffectSeats(source, target)) total += State.Of(seat).HpLost;
            return total;
        }

        /// <summary>
        /// <b>一个进攻半场开始时的角色能力结算</b>（2026-10-03）—— 原来这里只调
        /// <see cref="ApplyAbilities"/>（回血 / 抽牌 / 力量），现在多收三样东西：
        /// 强制伤害、自爆、以及「本拍不出牌」。
        ///
        /// <para><b>为什么单独一个方法而不是塞进 <see cref="ApplyAbilities"/></b>：
        /// <see cref="ApplyAbilities"/> 是「算一个数并返回」的形状（力量加值要拿返回值），
        /// 而这三个算子的效果是<b>对局级的副作用</b>（要发事件、要判终局、要改 Phase）。
        /// 混在一起会让那个被力量查询高频调用的方法顺手带上终局判定。</para>
        ///
        /// <para><b>结算顺序（有讲究）</b>：</para>
        /// <list type="number">
        /// <item>回血 / 抽牌 —— 与旧口径一致，先发生，让「自爆前最后一口气」也能回血；</item>
        /// <item><b>自爆</b>：把自身生命置 0，发 <see cref="SelfDestructEvent"/>。
        /// ⚠ 这里<b>先不</b>判终局 —— 否则「同归于尽」会在伤害落地之前就结束对局，
        /// 被炸的那一方反而毫发无伤；</item>
        /// <item><b>强制伤害</b>：对每个敌方座位走一次常规 <see cref="ApplyDamage"/>
        /// （它自己会判一次终局，所以「玩家被炸死」这一条照常成立）；</item>
        /// <item>统一补一次 <see cref="CheckDeath"/>：自爆的那一方在这里才判负，
        /// 于是「两边同时倒地」会被 <see cref="IBattleMode.TryFinish"/> 正确地
        /// 判成「双方同时出局」。</item>
        /// </list>
        /// </summary>
        private void ApplyTurnBehaviours(int seat)
        {
            PlayerState player = State.Of(seat);
            var context = new AbilityContext
            {
                Seat = seat,
                Trigger = AbilityTrigger.TurnStarted,
                TurnNumber = State.TurnNumber,
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                InitialHp = player.InitialHp,
            };

            foreach (ICharacterAbility ability in player.Abilities)
            {
                ability.Apply(context);
            }

            if (context.HealRequested > 0)
            {
                Heal(seat, context.HealRequested);
            }

            for (int i = 0; i < context.DrawRequested && !State.IsOver; i++)
            {
                if (DrawCardToHand(player, "角色能力") == null)
                {
                    break;
                }
            }

            if (context.SelfDestructRequested && !player.IsDead)
            {
                Emit(new SelfDestructEvent { Seat = seat, HpBefore = player.Hp });
                player.Hp = 0;
            }

            if (context.DamageRequested > 0)
            {
                for (int i = 0; i < State.Players.Count; i++)
                {
                    PlayerState other = State.Players[i];
                    if (other.Seat == seat || other.IsDead) continue;
                    if (!State.Mode.AreEnemies(State, seat, other.Seat)) continue;
                    ApplyDamage(other.Seat, context.DamageRequested, "自爆");
                }
            }

            _skipAttack = context.AttackSkipped;
            CheckDeath();
        }

        private int ApplyAbilities(int seat, AbilityTrigger trigger, int value = 0)
        {
            PlayerState player = State.Of(seat);
            var context = new AbilityContext { Seat = seat, Trigger = trigger, TurnNumber = State.TurnNumber,
                Hp = player.Hp, MaxHp = player.MaxHp, InitialHp = player.InitialHp, Value = value };
            foreach (ICharacterAbility ability in player.Abilities) ability.Apply(context);
            if (context.HealRequested > 0) Heal(seat, context.HealRequested);
            for (int i = 0; i < context.DrawRequested && !State.IsOver; i++)
                if (DrawCardToHand(player, "角色能力") == null) break;
            return Math.Max(0, context.Value);
        }

        private void ApplyDamage(int seat, int amount, string source)
        {
            if (amount <= 0 || State.IsOver)
            {
                return;
            }

            PlayerState p = State.Of(seat);
            amount = ApplyAbilities(seat, AbilityTrigger.BeforeDamage, amount);
            if (amount <= 0) return;
            p.Hp -= amount;
            CancelDrawObservation();
            Emit(new DamageTakenEvent { Seat = seat, Amount = amount, Source = source });
            PublishHpLoss(seat);
            CheckDeath();
            if (!State.IsOver && !p.IsDead) ApplyAbilities(seat, AbilityTrigger.AfterDamage, amount);
        }

        private void Heal(int seat, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            PlayerState p = State.Of(seat);
            int before = p.Hp;
            p.Hp += amount;
            if (p.Hp > p.MaxHp)
            {
                p.Hp = p.MaxHp;
            }

            Emit(new HealEvent { Seat = seat, Amount = amount, Actual = p.Hp - before });
        }

        private void CutMaxHp(int seat, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            PlayerState p = State.Of(seat);
            int before = p.MaxHp;
            p.MaxHp -= amount;
            if (p.MaxHp < 0)
            {
                p.MaxHp = 0;
            }

            Emit(new HpMaxChangedEvent { Seat = seat, From = before, To = p.MaxHp });

            // 削上限按常规：最后 Hp = min(Hp, MaxHp)（规则 §9）
            if (p.Hp > p.MaxHp)
            {
                p.Hp = p.MaxHp;
                CancelDrawObservation();
                // 削上限把 Hp 一起拉下来的那一格同样是「生命值减少」-> 火炬（ap）的 gamma 要复位。
                PublishHpLoss(seat);
            }

            CheckDeath();
        }

        /// <summary>
        /// 拥有者生命值减少之后，把冷却区里「掉血就恢复」的光环补满（2026-10-29 · 火炬 ap）。
        ///
        /// <para><b>为什么在这里、而不是在效果处理器里轮询</b>：掉血有两条入口
        /// （<see cref="ApplyDamage"/> 与 <see cref="CutMaxHp"/>），而「光环复位」的判定
        /// 属于<b>状态变化</b>而不是某张牌的一次结算 —— 只有在两个入口各挂一次，
        /// 才不会出现「被削上限弄掉血时光环不复活」这种一条路径漏掉的坑。</para>
        ///
        /// <para>走的是现成的 gamma 管线（<see cref="PublishEffectEvent"/> -> <c>_pendingSpecial</c>
        /// -> <c>AdvanceSpecialEffects</c>），与潮汐 e 的「冷却完毕」完全同一条路 ——
        /// 所以真正补指示物的代码只有 <c>BuiltinEffects</c> 那一处，AI / UI 两侧无感。</para>
        /// </summary>
        private void PublishHpLoss(int seat)
        {
            if (State.IsOver || seat < 0 || seat >= State.Players.Count)
            {
                return;
            }

            PlayerState p = State.Of(seat);
            if (p.IsDead)
            {
                return;     // 已经出局：补光环没有任何意义，也避免在结算链尾节外生枝
            }

            // 先拷一份冷却区：PublishEffectEvent 只入队，但将来若有人在这里改成同步结算，
            // 遍历一个正在被改的 List 会当场抛。
            var cooling = new List<CardInstance>(p.CoolingZone);
            for (int i = 0; i < cooling.Count; i++)
            {
                PublishEffectEvent("owner.hp.lost", cooling[i]);
            }
        }

        /// <summary>生命归零 -> 立即判负；生命上限归零 -> 即刻死亡（决策 C1，不锁底）。</summary>
        private void CheckDeath()
        {
            if (State.IsOver) return;
            BattleOutcome outcome;
            if (!State.Mode.TryFinish(State, out outcome)) return;
            State.Finish(outcome);
            Pending = null;
            Emit(new GameOverEvent { WinnerSeat = State.WinnerSeat, Reason = State.EndReason });
        }

        private void FinishByRule(IEnumerable<int> winners, string reason)
        {
            State.Finish(new BattleOutcome(winners, reason));
            Pending = null;
            _phase = Phase.Finished;
            Emit(new GameOverEvent { WinnerSeat = State.WinnerSeat, Reason = State.EndReason });
        }

        private void AppendDrawProposal(List<Option> options)
        {
            if (!_allowDrawProposal || _drawObservationStart != 0) return;
            options.Add(new Option { Index = options.Count, Kind = OptionKind.ProposeDraw, Label = "提出平局" });
        }

        private bool TryProposeDraw(int seat)
        {
            if (!_allowDrawProposal || _drawObservationStart != 0 || State.IsOver) return false;
            _drawObservationStart = State.TurnNumber + 1;
            Emit(new DrawObservationEvent { Seat = seat, StartTurn = _drawObservationStart });
            return true;
        }

        private void CancelDrawObservation()
        {
            if (_drawObservationStart == 0) return;
            _drawObservationStart = 0;
            Emit(new DrawObservationEvent { Cancelled = true });
        }

        private void FlushCooldown()
        {
            for (int i = 0; i < _cooldownBuffer.Count; i++)
            {
                CooldownChange ch = _cooldownBuffer[i];
                Emit(new CooldownChangedEvent { Change = ch });

                if (ch.Card != null && ch.ReturnedToHand)
                {
                    Emit(new CardReturnedEvent { Seat = ch.Card.OwnerSeat, Card = ch.Card });
                    PublishEffectEvent("cooldown.completed", ch.Card);
                }
            }

            _cooldownBuffer.Clear();
        }

        private void Emit(BattleEvent e)
        {
            if (e == null)
            {
                return;
            }

            State.EventSeq++;
            e.Seq = State.EventSeq;
            e.TurnNumber = State.TurnNumber;

            Action<BattleEvent> handler = OnEvent;
            if (handler != null)
            {
                handler(e);
            }
        }
    }
}
