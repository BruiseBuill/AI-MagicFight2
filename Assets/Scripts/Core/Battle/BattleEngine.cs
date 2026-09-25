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
        private int _replaceSeat;
        private int _replaceLimit;
        private bool _replaceIsInitial;
        private Phase _replaceNextPhase = Phase.BeginTurn;
        private readonly List<CardInstance> _replaceTargets = new List<CardInstance>();

        // 半场上下文
        private AttackContext _atk;

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

        private BattleEngine(BattleState state, IDealPolicy dealPolicy, EffectRegistry effects)
        {
            State = state;
            _effectRegistry = effects;
            _effectRegistry.Freeze();
            _deal = dealPolicy ?? new DefaultDealPolicy();
        }

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
            for (int seat = 0; seat < setup.Participants.Count; seat++)
            {
                // 每个座位独立随机流；其他角色换牌不会消耗自己的抽牌随机数。
                var deckRng = new Rng(unchecked(seed + (seat + 1) * (int)0x9E3779B9u));
                state.Players.Add(new PlayerState(seat, setup.Participants[seat], deckRng));
            }
            var effects = setup.Effects;
            foreach (PlayerState player in state.Players)
                foreach (CardDef card in player.Deck.Snapshot())
                    foreach (EffectDef effect in card.Effects) effects.Validate(effect);
            return new BattleEngine(state, dealPolicy, effects);
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
            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                for (int i = 0; i < _deal.InitialHandSize; i++)
                {
                    DrawCardToHand(State.Of(seat), "开局发牌");
                }
            }

            _replaceIsInitial = true;
            _replaceLimit = _deal.InitialReplaceLimit;
            _replaceNextPhase = Phase.BeginTurn;
            BeginReplacePass(null);
        }

        private void BeginReplacePass(List<CardInstance> targets)
        {
            _replaceTargets.Clear();
            if (targets != null)
            {
                _replaceTargets.AddRange(targets);
            }

            _replaceSeat = 0;
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
            if (p.IsDead) { _replaceSeat++; return; }
            List<CardInstance> pool = _replaceIsInitial ? p.Hand : _replaceTargets;

            if (_replaceLimit <= 0 || pool.Count == 0 || p.Deck.Remaining == 0)
            {
                _replaceSeat++;
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
                    ? "选择要替换掉的手牌（至多 " + _replaceLimit + " 张）"
                    : "可替换刚补到的这张牌",
                Options = options,
                MinSelect = 0,
                // 用过滤后的候选数，不是 pool.Count —— 补牌那趟 pool 含对面的牌，
                // 拿它当上限会给出「能换 2 张其实只有 1 张可选」的错数。
                MaxSelect = Math.Min(_replaceLimit, candidateCount),
            };
        }

        private void DoReplace(List<Option> picked)
        {
            PlayerState p = State.Of(_replaceSeat);
            for (int i = 0; i < picked.Count; i++)
            {
                CardInstance old = picked[i].Card;
                if (old == null || old.OwnerSeat != p.Seat)
                {
                    continue;
                }

                CardDef fresh = p.Deck.Draw();
                if (fresh == null)
                {
                    break;
                }

                var inst = State.CreateCard(fresh, p.Seat);
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
                p.Deck.Return(old.Def);

                Emit(new ReplaceEvent { Seat = p.Seat, Returned = old, Gained = inst });
            }

            _replaceSeat++;
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

            int draw = _deal.DrawOnTurn(State.TurnNumber);
            if (draw <= 0)
            {
                _phase = Phase.BeginHalfTurn;
                return;
            }

            var drawn = new List<CardInstance>();
            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                for (int i = 0; i < draw; i++)
                {
                    CardInstance c = DrawCardToHand(State.Of(seat), "回合 " + State.TurnNumber + " 补牌");
                    if (c != null)
                    {
                        drawn.Add(c);
                    }
                }
            }

            _replaceIsInitial = false;
            _replaceLimit = _deal.ReplaceLimitOnTurn(State.TurnNumber);
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
                ApplyAbilities(State.AttackerSeat, AbilityTrigger.TurnStarted);
                // 进攻开始时：进攻方自己的冷却区全体 −1（每个半场只结算一次，P2）
                _cooldownBuffer.Clear();
                CooldownOps.TickOnAttackStart(State.Of(State.AttackerSeat), _cooldownBuffer);
                FlushCooldown();
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
                ApplyDamage(attacker, 1, "手上无牌，无法攻击");
                _phase = State.IsOver ? Phase.Finished : Phase.EndHalfTurn;
                return;
            }

            _atk = new AttackContext
            {
                AttackerSeat = attacker,
                DefenderSeat = State.Mode.SelectDefender(State, attacker),
                IsFollowUp = State.InCombo,
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
                    Label = "打出 " + c.Def.Name + "（力量 " + c.Def.PowerText + " · 冷却 " + c.Def.Cooldown + "）",
                });
            }

            // 2026-09-18：光环不再单独占一拍。玩家把 HudBuff 里的图标拖到判定区即「准备使用」，
            //   出牌那一刻随牌一并回填（DecisionResponse.AuraOptionIndices）。
            //   这里**不做**「哪张牌配哪个光环」的预筛 —— 牌还没定；精筛放在 DoChooseAttackCard，
            //   那里才拿得到最终打出的牌（连击阈值、沉重打击的「力量不能增加」都依赖它）。
            AppendAuras(options, AuraResolver.BuildOptions(attacker, AuraContext.Attack));

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = attacker.Seat,
                Kind = RequestKind.ChooseAttackCard,
                Prompt = "选择本次进攻打出的法术（有牌时必须进攻）"
                         + PreparedHint(),
                Options = options,
                MinSelect = 1,
                MaxSelect = 1,
                ContextNoSkip = true,
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

            if (chosen == null && attacker.Hand.Count > 0)
            {
                // 兜底：非法回填 → 取第一张手牌（引擎不信任上层，绝不因此卡死）
                chosen = new Option { Card = attacker.Hand[0], Seat = attacker.Seat };
            }

            if (chosen == null)
            {
                ApplyDamage(_atk.AttackerSeat, 1, "手上无牌，无法攻击");
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

        /// <summary>卡面是否带「进攻力量不能增加」（沉重打击）—— 带它就不必弹光环选择。</summary>
        private static bool CardForbidsAtkBuff(CardInstance card)
        {
            return card != null && card.Def.ForbidsAtkBuff;
        }

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

            bool forbidden = CardForbidsAtkBuff(_atk.Card);

            for (int i = 0; i < auras.Count; i++)
            {
                Option o = auras[i];

                if (forbidden)
                {
                    continue;
                }

                if (o.AuraKind == AuraKind.Combo && _atk.Card.Def.Power > o.Value)
                {
                    continue;
                }

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
                int need = DefenseResolver.RequiredBonus(pick.Card, _atk.FinalPower);
                if (pick.PairCard != null)
                {
                    need += DefenseResolver.RequiredBonus(pick.PairCard, _atk.FinalPower);
                }

                if (need > _defensePower)
                {
                    // 挡不住 → 不消耗、不出牌，把本拍原样重发一次（提示条说明差多少）。
                    // 绝不「打出一张挡不住的牌」：那是规则 §3 的硬性约束。
                    _pendingRetryNote = "这批光环还不够：这张牌需要 +" + need + "，当前准备 +" + _defensePower;
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

        private void DoStage5() { RunEffects(EffectStage.Aura, () => _phase = Phase.Stage6); }

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
            Emit(new DamageTakenEvent { Seat = seat, Amount = amount, Source = source });
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
            }

            CheckDeath();
        }

        /// <summary>生命归零 → 立即判负；生命上限归零 → 即刻死亡（决策 C1，不锁底）。</summary>
        private void CheckDeath()
        {
            if (State.IsOver) return;
            BattleOutcome outcome;
            if (!State.Mode.TryFinish(State, out outcome)) return;
            State.Finish(outcome);
            Pending = null;
            Emit(new GameOverEvent { WinnerSeat = State.WinnerSeat, Reason = State.EndReason });
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
