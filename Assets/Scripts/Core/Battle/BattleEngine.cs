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
    public sealed class BattleEngine
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
            ReadyRefreshTrigger,
            AwaitAttackCard,
            Stage1,
            AwaitDefense,
            Stage3,
            Stage4,
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
        private readonly Queue<CardInstance> _readyRefreshQueue = new Queue<CardInstance>();

        private Phase _phase = Phase.NotStarted;
        private Phase _afterTrigger = Phase.Finished;

        // 发牌 / 替换
        private int _replaceSeat;
        private int _replaceLimit;
        private bool _replaceIsInitial;
        private Phase _replaceNextPhase = Phase.BeginTurn;
        private readonly List<CardInstance> _replaceTargets = new List<CardInstance>();

        // 半场上下文
        private AttackContext _atk;

        // 效果队列
        private List<EffectDef> _queue = new List<EffectDef>();
        private int _cursor;
        private EffectDef _activeEffect;
        private int _repeatLeft;
        private List<CardInstance> _excludeTargets = new List<CardInstance>();

        /// <summary>
        /// <see cref="_excludeTargets"/> 的归属：那一份排除名单属于<strong>哪一次进攻</strong>的
        /// ③ 阶段结算（null = 还没有归属）。
        ///
        /// <para><b>为什么排除名单必须挂在「这次进攻」上，而不是「每处理一个效果就清一次」</b>
        /// （2026-09-23 用户口径）：瀑流（y）有两条 α —— 「加速」与
        /// 「你每损失 1 点生命值，加速一个<em>不同</em>的法术」。规则要的是
        /// <b>整张瀑流里同一张牌只能被加速一次</b>；而旧实现在
        /// <see cref="StartStage3Decision"/> 的第一句就把名单清空，于是第 1 条 α 加速过的
        /// 那张牌，在第 2 条 α 里又出现在候选里 —— 「不同」只在半条链上生效。</para>
        ///
        /// <para><b>为什么钥匙是 <see cref="AttackContext"/> 而不是那张牌</b>：同一张牌回手后
        /// 再打出时是<b>另一个 CardInstance 之外、却是同一个对象</b>（牌实例复用），
        /// 拿牌当钥匙会让上一次进攻的排除名单漏进下一次。每次进攻都新建一个上下文，
        /// 它天然是「一次结算」的精确边界。</para>
        ///
        /// <para>反过来也不能一直不清：③ 阶段的队列里可能挤着<b>好几次进攻</b>的效果
        /// （双发 / 连击的追加进攻各有自己的 <see cref="AttackContext"/>），
        /// 跨进攻携带名单会把上一次加速过的目标从下一次的候选里抹掉。</para>
        /// </summary>
        private AttackContext _excludeScope;

        /// <summary>
        /// 当前这张攻击牌的效果里有没有「每损失 1 点生命加速一个<em>不同</em>的法术」
        /// （瀑流，<see cref="EffectOp.HastePerHpLoss"/>）。
        ///
        /// <para><b>为什么是「一张牌的属性」而不是「一个算子的属性」</b>：用户口径
        /// （2026-09-23）—— 「同一张卡不能被加速第 2 次」是整张瀑流的规矩，
        /// 它第 1 条 α（<see cref="EffectOp.Haste"/>）与第 2 条 α
        /// （<see cref="EffectOp.HastePerHpLoss"/>）共享同一份排除名单。</para>
        ///
        /// <para>⚠ <b>不能推广到所有 <see cref="EffectOp.Haste"/></b>：湍流（加速 ×2）等牌的
        /// 规则是「A 次可分配给同一张或不同张」（`engineering/04-架构与接口.md` 的算子表），
        /// 只有带 <see cref="EffectOp.HastePerHpLoss"/> 的牌才是「必须不同张」。</para>
        /// </summary>
        private bool _hasteTargetsMustDiffer;

        /// <summary>待发放的加速次数（充能 / 漩涡的"获得加速 ×N"——先记账，第 ③ 步统一发放）。</summary>
        private int _pendingHastes;

        /// <summary>第 ④ 步的「未防御则减速」要借用第 ③ 步的重复决策，但不该重放 ③ 的效果队列。</summary>
        private bool _stage3QueueSkipped;
        private Phase _stage3ThenPhase = Phase.Stage4;

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

        private BattleEngine(BattleState state, IDealPolicy dealPolicy)
        {
            State = state;
            _deal = dealPolicy ?? new DefaultDealPolicy();
        }

        /// <summary>建一局 1v1（0 号座位 = 玩家先手，D3）。</summary>
        public static BattleEngine Create(int seed, IDealPolicy dealPolicy = null)
        {
            var rng = new Rng(seed);
            var deck = new Deck(CardLibrary.All, rng);
            var state = new BattleState(deck, rng);
            state.Players.Add(new PlayerState(BattleState.SeatPlayer, "你", false));
            state.Players.Add(new PlayerState(BattleState.SeatAi, "AI", true));
            return new BattleEngine(state, dealPolicy);
        }

        /// <summary>开局：发初始手牌。之后由 <see cref="Advance"/> 推进。</summary>
        public void Start()
        {
            if (_phase != Phase.NotStarted)
            {
                throw new InvalidOperationException("引擎已经启动过了");
            }

            _phase = Phase.InitialDeal;
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

                Step();
            }
        }

        /// <summary>回填外部决策，然后可以继续 <see cref="Advance"/>。</summary>
        public void Submit(DecisionResponse response)
        {
            if (Pending == null)
            {
                throw new InvalidOperationException("当前没有待回填的决策");
            }

            DecisionRequest req = Pending;

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

                case Phase.ReadyRefreshTrigger:
                    IssueReadyRefresh();
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
            List<CardInstance> pool = _replaceIsInitial ? p.Hand : _replaceTargets;

            if (_replaceLimit <= 0 || pool.Count == 0)
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

                CardDef fresh = State.Deck.Draw();
                if (fresh == null)
                {
                    break;
                }

                var inst = new CardInstance(fresh, p.Seat);
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
                State.Deck.Return(old.Def);

                Emit(new ReplaceEvent { Seat = p.Seat, Returned = old, Gained = inst });
            }

            _replaceSeat++;
        }

        private CardInstance DrawCardToHand(PlayerState p, string reason)
        {
            if (p.IsHandFull)
            {
                return null;
            }

            CardDef def = State.Deck.Draw();
            if (def == null)
            {
                return null;
            }

            var inst = new CardInstance(def, p.Seat);
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
            State.AttackerSeat = BattleState.SeatPlayer;   // 玩家先手（D3）
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

            if (State.AttackerSeat == BattleState.SeatPlayer)
            {
                State.AttackerSeat = 1;
                _phase = Phase.BeginHalfTurn;
            }
            else
            {
                _phase = Phase.BeginTurn;
            }
        }

        // ══════════════════════════════════════════════════════
        //  γ 触发（潮汐：冷却完毕时使另一张牌立即冷却完成）
        // ══════════════════════════════════════════════════════

        private void GoToTriggerOr(Phase next)
        {
            if (_readyRefreshQueue.Count > 0)
            {
                _afterTrigger = next;
                _phase = Phase.ReadyRefreshTrigger;
            }
            else
            {
                _phase = next;
            }
        }

        private void CollectReadyRefresh(CardInstance card)
        {
            if (card == null)
            {
                return;
            }

            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                EffectDef ef = card.Def.Effects[i];
                if (ef.Trigger == EffectTrigger.Special && ef.Op == EffectOp.ReadyRefresh)
                {
                    _readyRefreshQueue.Enqueue(card);
                    return;
                }
            }
        }

        private void IssueReadyRefresh()
        {
            if (_readyRefreshQueue.Count == 0)
            {
                _phase = _afterTrigger;
                return;
            }

            CardInstance source = _readyRefreshQueue.Peek();
            var options = new List<Option>();
            options.Add(new Option { Kind = OptionKind.Skip, Label = "不发动潮汐" });

            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                PlayerState p = State.Of(seat);
                for (int i = 0; i < p.CoolingZone.Count; i++)
                {
                    CardInstance c = p.CoolingZone[i];
                    options.Add(new Option
                    {
                        Kind = OptionKind.ChooseCard,
                        Seat = seat,
                        Card = c,
                        Value = c.RemainingCooldown,
                        Label = "使 " + (seat == source.OwnerSeat ? "我方" : "对方") + c.Def.Name
                                + "（剩余 " + c.RemainingCooldown + "）立即冷却完成",
                    });
                }
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = source.OwnerSeat,
                Kind = RequestKind.ChooseRefreshTarget,
                Prompt = "潮汐：本牌冷却完毕，可另使一张法术立即冷却完成",
                Options = options,
                MinSelect = 0,
                MaxSelect = 1,
            };
        }

        private void DoReadyRefresh(List<Option> picked)
        {
            CardInstance source = _readyRefreshQueue.Dequeue();
            if (picked.Count > 0 && picked[0].Card != null)
            {
                CardInstance target = picked[0].Card;
                _cooldownBuffer.Clear();
                CooldownOps.Refresh(State.Of(target.OwnerSeat), target, _cooldownBuffer);
                FlushCooldown();
                Emit(new CooldownChangedEvent
                {
                    Change = new CooldownChange
                    {
                        Card = target,
                        To = 0,
                        Reason = "潮汐 γ（源自 " + source.Def.Name + "）",
                    },
                });
            }
        }

        // ══════════════════════════════════════════════════════
        //  ① 进攻选牌 + 光环
        // ══════════════════════════════════════════════════════

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
            Emit(new AttackDeclaredEvent { Seat = _atk.AttackerSeat, Card = card });

            ApplyPreparedAttackAuras(auras);
            _preparedAuras.Clear();
            _phase = Phase.Stage1;
        }

        /// <summary>把进攻牌的 α 效果分派到 ① / ③ / ④ / ⑤ 各阶段。</summary>
        private void SplitEffects(CardInstance card)
        {
            _atk.Stage1.Clear();
            _atk.Stage3.Clear();
            _cursor = 0;
            _repeatLeft = 0;
            _pendingHastes = 0;
            _stage3QueueSkipped = false;
            _stage3ThenPhase = Phase.Stage4;

            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                EffectDef ef = card.Def.Effects[i];
                if (ef.Trigger != EffectTrigger.Attack)
                {
                    continue;
                }

                AddEffectToStage(ef);
            }
        }

        private void AddEffectToStage(EffectDef ef)
        {
            switch (EffectClassifier.Classify(ef.Op))
            {
                case EffectStage.Stage1Power:
                    if (ef.Op != EffectOp.Aura)
                    {
                        _atk.Stage1.Add(ef);
                    }

                    break;

                case EffectStage.Stage3Cooldown:
                    _atk.Stage3.Add(ef);
                    break;

                case EffectStage.Stage4Marker:
                    ApplyMarker(ef);
                    break;

                default:
                    break;
            }
        }

        private void ApplyMarker(EffectDef ef)
        {
            switch (ef.Op)
            {
                case EffectOp.QuickRefill:
                    _atk.QuickRefill = true;
                    break;
                case EffectOp.CoolMinusIfUnblocked:
                    _atk.CoolMinusIfUnblocked += ef.A;
                    break;
                case EffectOp.SlowIfUnblocked:
                    _atk.SlowIfUnblocked += ef.A;
                    break;
                case EffectOp.ExtraDamageIfUnblocked:
                    _atk.ExtraDamage += ef.A;
                    _atk.ExtraMaxHpCut += ef.B;
                    break;
            }
        }

        /// <summary>卡面是否带「进攻力量不能增加」（沉重打击）—— 带它就不必弹光环选择。</summary>
        private static bool CardForbidsAtkBuff(CardInstance card)
        {
            if (card == null)
            {
                return false;
            }

            for (int i = 0; i < card.Def.Effects.Count; i++)
            {
                if (card.Def.Effects[i].Op == EffectOp.NoAtkBuff)
                {
                    return true;
                }
            }

            return false;
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

                ApplyAuraOnAttack(o.AuraSource, o.AuraKind, o.Value);
            }
        }

        private void ApplyAuraOnAttack(CardInstance source, AuraKind kind, int value)
        {
            AuraResolver.Consume(source);

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

        private void RunStage1()
        {
            while (true)
            {
                if (State.IsOver)
                {
                    _phase = Phase.Finished;
                    return;
                }

                if (_cursor >= _atk.Stage1.Count)
                {
                    FinishStage1();
                    return;
                }

                EffectDef ef = _atk.Stage1[_cursor++];
                if (HandleImmediateStage1(ef))
                {
                    continue;
                }

                if (StartStage1Decision(ef))
                {
                    return;
                }
            }
        }

        private bool HandleImmediateStage1(EffectDef ef)
        {
            PlayerState attacker = State.Of(_atk.AttackerSeat);

            switch (ef.Op)
            {
                case EffectOp.AtkPlus:
                    _atk.EffectBonus += ef.A;
                    return true;

                case EffectOp.AtkPlusPerCooling:
                    _atk.EffectBonus += ef.A * attacker.CoolingZone.Count;
                    return true;

                case EffectOp.AtkPlusPerHpLoss:
                    _atk.EffectBonus += ef.A * State.TotalHpLost;
                    return true;

                case EffectOp.DoubleAtkBonus:
                    _atk.DoubleAtkBonus = true;
                    return true;

                case EffectOp.NoAtkBuff:
                    _atk.NoAtkBuff = true;
                    _atk.EffectBonus = 0;
                    _atk.AuraBonus = 0;
                    return true;

                case EffectOp.Combo:
                    _atk.HasCombo = true;
                    return true;

                case EffectOp.Double:
                    _atk.IsDouble = true;
                    return true;

                case EffectOp.HealMinusMax:
                    Heal(_atk.AttackerSeat, ef.A);
                    if (ef.B > 0)
                    {
                        CutMaxHp(_atk.AttackerSeat, ef.B);
                    }

                    return true;

                default:
                    return false;
            }
        }

        private bool StartStage1Decision(EffectDef ef)
        {
            switch (ef.Op)
            {
                case EffectOp.CoolHandForAtk:
                case EffectOp.CoolHandForCombo:
                    _activeEffect = ef;
                    // 手上没有别的牌可送 → 该效果无事发生（不能弹一个空决策出去）
                    return IssueCoolHandSelection(ef);

                case EffectOp.Copy:
                    _activeEffect = ef;
                    IssueCopyTarget(ef);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>发出「选择送入冷却的手牌」决策。返回是否真的发出了（无候选时返回 false）。</summary>
        private bool IssueCoolHandSelection(EffectDef ef)
        {
            PlayerState attacker = State.Of(_atk.AttackerSeat);
            var options = new List<Option>();

            for (int i = 0; i < attacker.Hand.Count; i++)
            {
                CardInstance c = attacker.Hand[i];
                options.Add(new Option
                {
                    Kind = OptionKind.ChooseCard,
                    Seat = attacker.Seat,
                    Card = c,
                    Label = "把 " + c.Def.Name + " 送入冷却（" + c.Def.PowerText + "/" + c.Def.Cooldown + "）",
                });
            }

            if (options.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            // 三种效果的「最少选择数」不一样，这是**规则事实**，不是 UI 口味：
            //   · 磁暴 CoolHandForAtk：0 张也合法（只是没有加值）→ MinSelect = 0
            //   · 充能 CoolHandForHaste：0 张也合法（只是不获得加速）→ MinSelect = 0
            //   · 电弧 CoolHandForCombo：**必须恰好 1 张** —— 卡面写的是「可将手中一张
            //     其它法术进入冷却，获得连击」，A = 1 就是「换一次连击」的那一张。
            //     玩家不选就没有任何事发生，那不如把「放弃」表达在界面之外：
            //     本拍 MinSelect = 1，确认键在选择之前不可用。
            //
            // ⚠ 这一段只表达「这一拍最少/最多能选几张」；**能不能不选这张牌**（即整张牌
            //   打不打）不在这里 —— 那是 ChooseAttackCard 那一拍的事。
            bool single = ef.Op == EffectOp.CoolHandForCombo;

            string prompt = ef.Op == EffectOp.CoolHandForAtk
                ? "逐张选择要送入冷却的手牌，每张使本次进攻力量 +" + ef.A + "（可随时停止）"
                : ef.Op == EffectOp.CoolHandForCombo
                    ? "选择 1 张手牌送入冷却，获得连击"
                    : "选择要送入冷却的手牌，每张获得一次加速（可随时停止）";

            // 短标题 = 这一拍**要做什么**，不是「哪张牌在结算」（2026-09-22 用户口径）：
            // 弹窗标题要用效果命名、并且比卡名更简洁 —— 三个效果规则不同，
            // 界面必须一眼看出「我要付出什么、换到什么」，而不是「这是磁暴」。
            //
            // ⚠ 有长度预算：title band 的平直段只有约 257 px（见
            //   UiLayout.HandPickTitleWidth），15 字就要缩到 19 号，太小。
            //   所以这里用「4 字效果 + 最多 3 字后缀」的口径，把字数控在 7 字以内
            //   （「送入冷却 · 多张」这类），既保留「付出什么」（送入冷却）也保留
            //   「换到什么」（力量 / 加速 / 连击）。完整规则由 Prompt 那一行说。
            string title = ef.Op == EffectOp.CoolHandForAtk
                ? "冷却·加力量"
                : ef.Op == EffectOp.CoolHandForCombo
                    ? "冷却·换连击"
                    : "冷却·获加速";

            // 后缀只在**真的能多选**时加：
            //   · 磁暴：每冷却一张 +A 力量 → 能多选（0 张起）
            //   · 充能：每冷却一张获得一次加速 → 能多选（0 张起）
            //   · 电弧：必须恰好 1 张 → 不多选
            //   · 手牌只剩 1 张时 Magnetic/Haste 也只能放一张 —— 那就别写「可多选」，
            //     否则玩家会以为界面坏了（点第二张没反应）。
            bool multi = !single && options.Count > 1;

            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = RequestKind.ChooseCoolHandCards,
                // 标题由引擎给出（UI 不猜是哪张牌在结算），见 DecisionRequest.Title 的说明。
                // 后缀用短括号版（「(可多选)」而不是「（可选择多张）」）—— 见上面的长度预算。
                Title = multi ? title + "（可多选）" : title,
                Prompt = prompt,
                Options = options,
                MinSelect = single ? 1 : 0,
                // ⚠ MaxSelect 是「这一拍**最多能选几张牌**」，不是效果参数：
                //   磁暴 / 充能都是「每冷却一张结算一次」，所以张数上限就是**候选数**
                //   （= 手上除本牌外的所有法术）。
                //   2026-09-22 修：这里原来写的是 `Math.Max(1, ef.A)` ——
                //   A 是「每张 +3 力量 / 每张 1 次加速」的**每张收益**，把它当成张数上限，
                //   结果是充能（A = 1）永远只能选 1 张，玩家点第二张毫无反应
                //   （用户报的「目前这个效果未能实现好，导致只能选择一张」正是这条）。
                //   磁暴（A = 3）因为 A ≥ 3 而侥幸看不出问题。
                //   电弧必须是恰好 1 张，由上面的 `single` 独立钉死，不依赖 A。
                MaxSelect = single ? 1 : options.Count,
            };
            return true;
        }

        private void DoCoolHandSelection(List<Option> picked)
        {
            int cooled = 0;
            _cooldownBuffer.Clear();

            for (int i = 0; i < picked.Count; i++)
            {
                CardInstance c = picked[i].Card;
                if (c == null || c == _atk.Card)
                {
                    continue;
                }

                PlayerState owner = State.Of(c.OwnerSeat);
                if (!owner.Hand.Contains(c))
                {
                    continue;
                }

                owner.Hand.Remove(c);
                // 按基础冷却进入冷却区 → 不触发该牌自身的攻防效果（P4）
                if (CooldownOps.PutIntoCooldown(owner, c, null, _cooldownBuffer, "效果送入冷却区"))
                {
                    cooled++;
                }
            }

            FlushCooldown();

            switch (_activeEffect.Op)
            {
                case EffectOp.CoolHandForAtk:
                    _atk.EffectBonus += _activeEffect.A * cooled;
                    _phase = Phase.Stage1;
                    break;

                case EffectOp.CoolHandForCombo:
                    if (cooled >= 1)
                    {
                        _atk.HasCombo = true;
                    }

                    _phase = Phase.Stage1;
                    break;

                case EffectOp.CoolHandForHaste:
                    // 先记账，「获得加速」统一在第 ③ 步发放（见 RunStage3 里 _pendingHastes 那段）。
                    //
                    // ⚠ 这里必须回 Phase.Stage3，**不能**跟上面两条一样写 Phase.Stage1：
                    //   CoolHandForHaste 被 EffectClassifier 归在 Stage3Cooldown，
                    //   只可能由 RunStage3 发出来。写回 Stage1 会连着踩两个坑：
                    //   ① FinishStage1 第一件事就是 `_cursor = 0` —— 而 _cursor 是 ①③ 两阶段
                    //      **共用**的游标 → ③ 的效果队列从头重放（充能的「加速」与
                    //      「选择冷却手牌」会各自再走一遍）；
                    //   ② FinishStage1 直接落到 Phase.AwaitDefense → 再问一次防御。
                    //   两者合起来就是「防御 → 加速 → 选牌 → 防御 → …」的无限循环
                    //   （用户 2026-09-23 报的「充能效果陷入无限判定」正是这一条）。
                    //   磁暴（CoolHandForAtk）与电弧（CoolHandForCombo）属 Stage1，
                    //   回 Stage1 才是对的 —— 三种算子在这件事上不同，别统一写。
                    _pendingHastes += cooled * _activeEffect.A;
                    _phase = Phase.Stage3;
                    break;
            }
        }

        private void IssueCopyTarget(EffectDef ef)
        {
            PlayerState attacker = State.Of(_atk.AttackerSeat);
            var options = new List<Option>();
            options.Add(new Option { Kind = OptionKind.Skip, Label = "不复制（力量视为 1）" });

            for (int i = 0; i < attacker.CoolingZone.Count; i++)
            {
                CardInstance c = attacker.CoolingZone[i];
                if (c.Def.Cooldown != ef.A || c.Def.HasAura)
                {
                    continue;
                }

                options.Add(new Option
                {
                    Kind = OptionKind.ChooseCard,
                    Seat = attacker.Seat,
                    Card = c,
                    Label = "复制 " + c.Def.Name + "（力量 " + c.Def.PowerText + "）的力量与进攻效果",
                });
            }

            if (options.Count <= 1)
            {
                _phase = Phase.Stage1;
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = RequestKind.ChooseCopyTarget,
                Prompt = "选择要复制的法术（基础冷却 = " + ef.A + " 且无光环）",
                Options = options,
                MinSelect = 0,
                MaxSelect = 1,
            };
        }

        private void DoCopyTarget(List<Option> picked)
        {
            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i].Card != null)
                {
                    pick = picked[i];
                    break;
                }
            }

            if (pick != null)
            {
                CardDef src = pick.Card.Def;
                _atk.CopySource = src;
                _atk.BasePower = src.Power;

                // 复制全部进攻效果（含连击 / 双发 / 快速回填），C8
                for (int i = 0; i < src.Effects.Count; i++)
                {
                    if (src.Effects[i].Trigger == EffectTrigger.Attack)
                    {
                        AddEffectToStage(src.Effects[i]);
                    }
                }
            }

            _phase = Phase.Stage1;
        }

        private void FinishStage1()
        {
            _cursor = 0;

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
            int defenderSeat = 1 - _atk.AttackerSeat;
            PlayerState defender = State.Of(defenderSeat);

            // 光环预算 = 玩家此刻在判定区准备的那几枚（未准备的不参与，规则 §6.2）。
            _defensePower = PreparedAuraBonus();

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
            int defenderSeat = 1 - _atk.AttackerSeat;
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

            _phase = State.IsOver ? Phase.Finished : Phase.Stage3;
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

                AuraResolver.Consume(o.AuraSource);
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
            AuraResolver.Consume(immune.AuraSource);
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

        private void RunStage3()
        {
            while (true)
            {
                if (State.IsOver)
                {
                    _phase = Phase.Finished;
                    return;
                }

                if (_repeatLeft > 0)
                {
                    IssueStage3Repeat();
                    return;
                }

                // 充能 / 漩涡记账的「获得加速 ×N」在这里统一发放
                if (_pendingHastes > 0)
                {
                    _activeEffect = new EffectDef(EffectTrigger.Attack, EffectOp.Haste, 1);
                    _repeatLeft = _pendingHastes;
                    _pendingHastes = 0;

                    // 这是一条**独立的**加速串（充能 / 漩涡，都不是瀑流），所以重开一份排除名单：
                    // 把归属清掉，让随后那次 StartStage3Decision 重新认领并重算
                    // 「这次进攻要不要落不同目标」。
                    _excludeScope = null;
                    _excludeTargets.Clear();
                    _hasteTargetsMustDiffer = false;
                    continue;
                }

                if (_stage3QueueSkipped || _cursor >= _atk.Stage3.Count)
                {
                    _cursor = 0;
                    _stage3QueueSkipped = false;
                    Phase next = _stage3ThenPhase;
                    _stage3ThenPhase = Phase.Stage4;
                    GoToTriggerOr(next);
                    return;
                }

                EffectDef ef = _atk.Stage3[_cursor++];

                if (StartStage3Decision(ef))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 发出「查看对方一张手牌」决策（雷云 / 狂躁蘑菇的第 2 个 α 效果）。
        ///
        /// <para><b>2026-09-21 由「引擎自动随机抽一张」改成「玩家在牌背里点一张」</b>
        /// （用户口径：要一个专门的界面，显示对方<b>全部</b>手牌（背面），
        /// 玩家点哪张就翻哪张，翻完再结算、再关界面）。</para>
        ///
        /// <para><b>选项文案里不出现牌名 / 力量</b>：牌背对玩家没有信息，点哪一张在信息上
        /// 等价于随机；一旦文案带上内容，效果就从「随机查看」变成「定向查看」——
        /// 那是另一张强度完全不同的卡。所以本拍只认 <see cref="Option.Value"/>（手牌下标）。</para>
        ///
        /// <para>对手无手牌 → 返回 false（本效果无事发生，不弹一个空决策出去）。</para>
        /// </summary>
        private bool IssuePeekSelection(EffectDef ef)
        {
            PlayerState opponent = State.Of(1 - _atk.AttackerSeat);
            if (opponent.Hand.Count == 0)
            {
                return false;
            }

            var options = new List<Option>(opponent.Hand.Count);
            for (int i = 0; i < opponent.Hand.Count; i++)
            {
                options.Add(new Option
                {
                    Kind = OptionKind.ChooseCard,
                    Seat = opponent.Seat,
                    Card = opponent.Hand[i],
                    Value = i,              // 手牌下标 —— DoPeekSelection 靠它回填 SlotIndex
                    Label = "翻开第 " + (i + 1) + " 张",
                });
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            bool atMost = ef.B == EffectDef.LookAtMost;
            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = RequestKind.ChoosePeekCard,
                Prompt = "查看对方一张手牌：力量 " + (atMost ? "≤" : "≥") + ef.A
                         + (ef.C != 0 ? "，送入冷却且冷却 " + ef.C : "，则立即送入冷却"),
                Options = options,
                // 必须点一张：牌背全一样，没有「放弃」这一说的意义
                MinSelect = 1,
                MaxSelect = 1,
                ContextNoSkip = true,
            };
            return true;
        }

        /// <summary>
        /// 结算「查看对方一张手牌」：按玩家翻开的那张的<strong>基础力量</strong>判阈值，
        /// 命中就立刻送入冷却（冷却值 = 基础冷却 + <c>ef.C</c>，狂躁蘑菇是 −1）。
        ///
        /// <para><b>引擎不信任上层</b>：没回填 / 回填了一张已经不在对手手牌里的牌，
        /// 一律安全降级成「随机抽一张」—— 这与 2026-09-21 之前的行为一致，
        /// 既不会卡死对局，也不会破坏概率口径。</para>
        /// </summary>
        private void DoPeekSelection(List<Option> picked)
        {
            EffectDef ef = _activeEffect;
            int opponentSeat = 1 - _atk.AttackerSeat;
            PlayerState opponent = State.Of(opponentSeat);

            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                Option o = picked[i];
                if (o != null && o.Card != null && opponent.Hand.Contains(o.Card))
                {
                    pick = o;
                    break;
                }
            }

            if (pick == null && opponent.Hand.Count > 0)
            {
                int idx = State.Rng.Next(opponent.Hand.Count);
                pick = new Option { Card = opponent.Hand[idx], Seat = opponentSeat, Value = idx };
            }

            if (pick != null)
            {
                CardInstance target = pick.Card;
                bool cooled = false;

                bool hit = ef.B == EffectDef.LookAtMost
                    ? target.Def.Power <= ef.A
                    : target.Def.Power >= ef.A;

                if (hit)
                {
                    int cd = target.Def.Cooldown + ef.C;
                    _cooldownBuffer.Clear();
                    // 按（可能被修正过的）冷却进入冷却区 → 不触发自身效果（P4）
                    cooled = CooldownOps.PutIntoCooldown(
                        opponent, target, cd, _cooldownBuffer, "查看手牌后送入冷却区");
                    FlushCooldown();
                }

                Emit(new HandRevealedEvent
                {
                    ViewerSeat = _atk.AttackerSeat,
                    OwnerSeat = opponentSeat,
                    Card = target,
                    Cooled = cooled,
                    SlotIndex = pick.Value,
                });
            }

            _phase = Phase.Stage3;
        }

        private bool StartStage3Decision(EffectDef ef)
        {
            _activeEffect = ef;
            _repeatLeft = 0;

            // 排除名单的生存期 = 「这一次进攻的整个 ③ 阶段」—— 见 _excludeScope 的说明。
            // 瀑流的两条 α 落在同一次进攻里，所以它的「加速一个不同的法术」能覆盖整张牌；
            // 换到下一次进攻（双发 / 连击的追加）时才会重开一份。
            if (!ReferenceEquals(_excludeScope, _atk))
            {
                _excludeScope = _atk;
                _excludeTargets.Clear();
                _hasteTargetsMustDiffer = HasDistinctHasteEffect(_atk);
            }

            switch (ef.Op)
            {
                case EffectOp.Haste:
                case EffectOp.Slow:
                    _repeatLeft = ef.A;
                    return true;

                // 地震：只有「本牌是玩家的最后两张手牌之一」时才减速（2026-09-23 规则改动）。
                //
                // 条件口径：出牌那一刻手上共 2 张（本牌 + 另一张）→ 走到这里时
                // DoChooseAttackCard 已经把手牌移走了，所以「出牌后还剩 1 张」就是判据。
                // 不满足 → 整条效果无事发生（return false，RunStage3 继续跑队列里的下一条）。
                //
                // ⚠ 复用减速那套「逐次选目标」时必须把 _activeEffect 换成等价的 Slow：
                //   IssueStage3Repeat / DoStage3Repeat 都按 `Op == EffectOp.Slow` 分
                //   「减速 / 加速」两条路 —— 带着本算子进去，卡面上写着减速、实际在加速。
                case EffectOp.SlowIfLastTwoHand:
                {
                    if (State.Of(_atk.AttackerSeat).Hand.Count != 1)
                    {
                        return false;
                    }

                    _activeEffect = new EffectDef(EffectTrigger.Attack, EffectOp.Slow, ef.A);
                    _repeatLeft = ef.A;
                    return true;
                }

                case EffectOp.HastePerHpLoss:
                    _repeatLeft = Math.Max(0, ef.A * State.Of(_atk.AttackerSeat).HpLost);
                    return true;

                case EffectOp.HasteZone:
                case EffectOp.SlowZone:
                case EffectOp.SlowZoneBoth:
                    IssueZoneValue(ef);
                    return true;

                case EffectOp.Refresh:
                case EffectOp.ResetCooldown:
                    IssueRefreshTarget(ef);
                    return true;

                case EffectOp.RemoveFromGame:
                    return IssueRemoveFromGame(ef);

                case EffectOp.CoolHandForHaste:
                    return IssueCoolHandSelection(ef);

                // 查看手牌（雷云 / 狂躁蘑菇）：2026-09-21 起走玩家选择
                //（原来是 HandleImmediateStage3 里引擎自己随机抽一张）。
                case EffectOp.LookAndCool:
                    return IssuePeekSelection(ef);

                default:
                    return false;
            }
        }

        /// <summary>
        /// 这次进攻的 ③ 阶段队列里有没有「每损失 1 点生命加速一个<em>不同</em>的法术」
        /// （瀑流，<see cref="EffectOp.HastePerHpLoss"/>）。
        ///
        /// <para><b>判据为什么从「队列里有没有这个算子」来</b>：本作不允许同一个算子带两种触发
        /// 条件（见 `04-架构与接口.md` 的算子表），所以「必须落在不同目标上」这件事
        /// 只能整张牌一起表达 —— 有这条算子的牌，它<b>全部</b>的加速都要求不同目标
        /// （用户 2026-09-23 口径：同一张卡不能被加速第 2 次）。</para>
        ///
        /// <para>队列只有几条，逐条扫的开销可以忽略；算一次存在 <see cref="_hasteTargetsMustDiffer"/>
        /// 而不是每次建候选表时重扫，是为了让「谁在要求不同目标」这件事只有一个落点。</para>
        /// </summary>
        private static bool HasDistinctHasteEffect(AttackContext atk)
        {
            if (atk == null || atk.Stage3 == null)
            {
                return false;
            }

            for (int i = 0; i < atk.Stage3.Count; i++)
            {
                if (atk.Stage3[i].Op == EffectOp.HastePerHpLoss)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>加速 / 减速 / 区域类的重复决策都回到这里。</summary>
        private void IssueStage3Repeat()
        {
            var options = new List<Option>();
            options.Add(new Option { Kind = OptionKind.Skip, Label = "不执行这一项" });

            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                PlayerState p = State.Of(seat);
                for (int i = 0; i < p.CoolingZone.Count; i++)
                {
                    CardInstance c = p.CoolingZone[i];
                    if (_excludeTargets.Contains(c))
                    {
                        continue;
                    }

                    // 减速的上限 = 这张牌的基础冷却值（CooldownOps.TrySlow，规则 §4 不变量）。
                    // 已经顶到上限的牌列进候选项只能是「点了什么都没发生」—— 它属于
                    // 「这一拍根本做不到的事」，在生成 Options 时就该被过滤掉（铁律 3：
                    // 非法项只收敛在引擎一处，界面与 AI 都不重复判断）。
                    // ⚠ 过滤之后界面会把那张牌画成**压暗且点得动**的态，点它会弹一条
                    //   「无法被减速（冷却已达上限）」的浮字 —— 那是提示，不是规则判断。
                    if (_activeEffect.Op == EffectOp.Slow && c.RemainingCooldown >= c.Def.Cooldown)
                    {
                        continue;
                    }

                    options.Add(new Option
                    {
                        Kind = OptionKind.ChooseCard,
                        Seat = seat,
                        Card = c,
                        Value = c.RemainingCooldown,
                        Label = (seat == _atk.AttackerSeat ? "我方 " : "对方 ") + c.Def.Name
                                + "（剩余 " + c.RemainingCooldown + "/" + c.Def.Cooldown + "）",
                    });
                }
            }

            if (options.Count <= 1)
            {
                _repeatLeft = 0;
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            string verb = _activeEffect.Op == EffectOp.Slow ? "减速" : "加速";
            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = _activeEffect.Op == EffectOp.Slow ? RequestKind.ChooseSlowTarget : RequestKind.ChooseHasteTarget,
                Prompt = verb + "一个法术（还剩 " + _repeatLeft + " 次，可放弃）",
                Options = options,
                MinSelect = 0,
                MaxSelect = 1,
            };
        }

        private void DoStage3Repeat(List<Option> picked)
        {
            if (_repeatLeft > 0)
            {
                _repeatLeft--;
            }

            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i].Card != null)
                {
                    pick = picked[i];
                    break;
                }
            }

            if (pick != null)
            {
                _cooldownBuffer.Clear();
                bool ok;
                if (_activeEffect.Op == EffectOp.Slow)
                {
                    ok = CooldownOps.TrySlow(pick.Card, _cooldownBuffer);
                }
                else
                {
                    ok = CooldownOps.TryHaste(State.Of(pick.Card.OwnerSeat), pick.Card, _cooldownBuffer);
                }

                FlushCooldown();

                // 瀑流（= 一切带 HastePerHpLoss 的牌）：这一张牌的**全部**加速都必须落在
                // 不同的法术上。用户 2026-09-23 口径「同一张卡不能被加速第 2 次」覆盖
                // 整张牌 —— 含它第 1 条 α 的那次「加速」（旧实现漏的正是这一半，
                // 排除名单在 StartStage3Decision 里被逐效果清空）。
                //
                // ⚠ 只有**成功**才记（`ok`）：加速失败的那张牌（例如已经不在冷却区）不该占名额。
                // ⚠ 减速那一路不参与 —— 排除名单只为「不能重复加速」服务；
                //   SlowIfLastTwoHand 借道本方法时已把 _activeEffect 换成了 Slow。
                if (ok && _hasteTargetsMustDiffer && _activeEffect.Op != EffectOp.Slow)
                {
                    _excludeTargets.Add(pick.Card);
                }
            }

            _phase = Phase.Stage3;
        }

        private void IssueZoneValue(EffectDef ef)
        {
            var options = new List<Option>();
            options.Add(new Option { Kind = OptionKind.Skip, Label = "不执行" });

            if (ef.Op == EffectOp.SlowZoneBoth)
            {
                int matched = 0;
                for (int seat = 0; seat < State.Players.Count; seat++)
                {
                    PlayerState p = State.Of(seat);
                    for (int i = 0; i < p.CoolingZone.Count; i++)
                    {
                        if (p.CoolingZone[i].RemainingCooldown == ef.A)
                        {
                            matched++;
                        }
                    }
                }

                if (matched == 0)
                {
                    _phase = Phase.Stage3;
                    return;
                }

                options.Add(new Option
                {
                    Kind = OptionKind.ZoneValue,
                    Seat = -1,
                    Value = ef.A,
                    Count = matched,
                    Label = "双方冷却区中剩余冷却 = " + ef.A + " 的 " + matched + " 张法术全体减速",
                });
            }
            else
            {
                for (int seat = 0; seat < State.Players.Count; seat++)
                {
                    PlayerState p = State.Of(seat);
                    var seen = new List<int>();
                    for (int i = 0; i < p.CoolingZone.Count; i++)
                    {
                        int v = p.CoolingZone[i].RemainingCooldown;
                        if (seen.Contains(v))
                        {
                            continue;
                        }

                        seen.Add(v);

                        int matched = 0;
                        for (int j = 0; j < p.CoolingZone.Count; j++)
                        {
                            if (p.CoolingZone[j].RemainingCooldown == v)
                            {
                                matched++;
                            }
                        }

                        options.Add(new Option
                        {
                            Kind = OptionKind.ZoneValue,
                            Seat = seat,
                            Value = v,
                            Count = matched,
                            Label = (seat == _atk.AttackerSeat ? "我方" : "对方")
                                    + "冷却区剩余冷却 = " + v + " 的 " + matched + " 张牌全体"
                                    + (ef.Op == EffectOp.HasteZone ? "加速" : "减速"),
                        });
                    }
                }
            }

            if (options.Count <= 1)
            {
                _phase = Phase.Stage3;
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = RequestKind.ChooseZoneValue,
                Prompt = ef.Op == EffectOp.HasteZone ? "区域加速：选一个剩余冷却值" : "区域减速：选一个剩余冷却值",
                Options = options,
                MinSelect = 0,
                MaxSelect = 1,
                ContextHaste = ef.Op == EffectOp.HasteZone,
            };
        }

        private void DoZoneValue(List<Option> picked)
        {
            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i].Kind == OptionKind.ZoneValue)
                {
                    pick = picked[i];
                    break;
                }
            }

            if (pick == null)
            {
                _phase = Phase.Stage3;
                return;
            }

            _cooldownBuffer.Clear();
            bool haste = _activeEffect.Op == EffectOp.HasteZone;

            if (pick.Seat < 0)
            {
                for (int seat = 0; seat < State.Players.Count; seat++)
                {
                    if (haste)
                    {
                        CooldownOps.HasteZone(State.Of(seat), pick.Value, _cooldownBuffer);
                    }
                    else
                    {
                        CooldownOps.SlowZone(State.Of(seat), pick.Value, _cooldownBuffer);
                    }
                }
            }
            else if (haste)
            {
                CooldownOps.HasteZone(State.Of(pick.Seat), pick.Value, _cooldownBuffer);
            }
            else
            {
                CooldownOps.SlowZone(State.Of(pick.Seat), pick.Value, _cooldownBuffer);
            }

            FlushCooldown();
            GoToTriggerOr(Phase.Stage3);
        }

        private void IssueRefreshTarget(EffectDef ef)
        {
            var options = new List<Option>();
            options.Add(new Option { Kind = OptionKind.Skip, Label = "不执行" });

            for (int seat = 0; seat < State.Players.Count; seat++)
            {
                // 寒流写明对象是「对方」，不可更改目标（规则 §0 目标指定）
                if (ef.Op == EffectOp.ResetCooldown && ef.B == 1 && seat == _atk.AttackerSeat)
                {
                    continue;
                }

                PlayerState p = State.Of(seat);
                for (int i = 0; i < p.CoolingZone.Count; i++)
                {
                    CardInstance c = p.CoolingZone[i];
                    options.Add(new Option
                    {
                        Kind = OptionKind.ChooseCard,
                        Seat = seat,
                        Card = c,
                        Value = c.RemainingCooldown,
                        Label = (seat == _atk.AttackerSeat ? "我方 " : "对方 ") + c.Def.Name
                                + (ef.Op == EffectOp.Refresh
                                    ? "（剩余 " + c.RemainingCooldown + "）→ 立即冷却完成"
                                    : "（剩余 " + c.RemainingCooldown + "）→ 重置为 " + c.Def.Cooldown),
                    });
                }
            }

            if (options.Count <= 1)
            {
                _phase = Phase.Stage3;
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = RequestKind.ChooseRefreshTarget,
                Prompt = ef.Op == EffectOp.Refresh ? "使一张法术立即冷却完成" : "重置对方一个法术的冷却时间",
                Options = options,
                MinSelect = 0,
                MaxSelect = 1,
                // 极性：立即冷却完成 = 冷却前进（对自己有利）；重置冷却 = 冷却倒退（对对方不利）。
                // AI 靠它决定「往自己的冷却区使劲还是往对方的」（2026-09-20）。
                ContextHaste = ef.Op == EffectOp.Refresh,
            };
        }

        private void DoRefreshTarget(List<Option> picked)
        {
            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i].Card != null)
                {
                    pick = picked[i];
                    break;
                }
            }

            if (pick != null)
            {
                _cooldownBuffer.Clear();
                if (_activeEffect.Op == EffectOp.Refresh)
                {
                    CooldownOps.Refresh(State.Of(pick.Card.OwnerSeat), pick.Card, _cooldownBuffer);
                }
                else
                {
                    CooldownOps.ResetToBase(pick.Card, _cooldownBuffer);
                }

                FlushCooldown();
            }

            GoToTriggerOr(Phase.Stage3);
        }

        /// <summary>
        /// 漩涡（<c>EffectOp.RemoveFromGame</c>）：让玩家从<b>自己的冷却区</b>里挑一张法术永久移出游戏。
        ///
        /// <para><b>2026-09-22 用户改的口径</b>：原来这一拍带一个「不移出（放弃）」的 Skip，
        /// 且 <c>MinSelect = 0</c>；现在改成<b>强制选一张</b>：
        /// 没有 Skip，<c>MinSelect == MaxSelect == 1</c>，候选 = 冷却区里的每一张法术。</para>
        ///
        /// <para>因此它跟 <see cref="IssueCoolHandSelection"/> 一样要能<b>拒绝发这一拍</b>
        /// —— 冷却区空着的时候没有任何候选，弹一个点不动的空框比直接跳过这个效果更糟。
        /// 返回值 <c>false</c> = 本效果无事可做，调用方照旧往下走（见 <c>StartStage1Decision</c>）。</para>
        /// </summary>
        private bool IssueRemoveFromGame(EffectDef ef)
        {
            PlayerState attacker = State.Of(_atk.AttackerSeat);

            // 冷却区空 → 没有可移出的目标，本效果整个跳过（不是「问一次再放弃」）。
            if (attacker.CoolingZone.Count == 0)
            {
                return false;
            }

            var options = new List<Option>();
            for (int i = 0; i < attacker.CoolingZone.Count; i++)
            {
                CardInstance c = attacker.CoolingZone[i];
                options.Add(new Option
                {
                    Kind = OptionKind.ChooseCard,
                    Seat = attacker.Seat,
                    Card = c,
                    Label = "永久移出 " + c.Def.Name + "，随后获得 " + ef.A + " 次加速",
                });
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            Pending = new DecisionRequest
            {
                Seat = _atk.AttackerSeat,
                Kind = RequestKind.ChooseRemoveFromGame,
                // 标题由引擎给（UI 不猜是哪张牌在结算）。⚠ 标题栏平直段只有 ~257px，
                //   标题必须短（≤ 7 字最稳），超了会被自动缩到 19pt。
                Title = RemoveFromGameTitle,
                Prompt = "漩涡：选一张冷却区法术永久移出游戏，获得加速 ×" + ef.A,
                Options = options,
                // 强制选一张：没有 Skip，最少 / 最多都是 1。
                //（UI 侧由此得到「确认键在选够之前不可用、选了一张就不能再选第二张」。）
                MinSelect = 1,
                MaxSelect = 1,
            };

            return true;
        }

        private void DoRemoveFromGame(List<Option> picked)
        {
            Option pick = null;
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i].Card != null)
                {
                    pick = picked[i];
                    break;
                }
            }

            if (pick != null && CooldownOps.RemoveFromGame(State.Of(pick.Card.OwnerSeat), pick.Card))
            {
                Emit(new CardRemovedEvent { Seat = pick.Card.OwnerSeat, Card = pick.Card });
                _pendingHastes += _activeEffect.A;   // 「获得加速 ×N」由第 ③ 步统一发放
            }

            _phase = Phase.Stage3;
        }

        // ══════════════════════════════════════════════════════
        //  ④ 进冷却区 + 快速回填 + 未防御分支
        // ══════════════════════════════════════════════════════

        private void DoStage4()
        {
            _cooldownBuffer.Clear();

            if (_atk.Card != null && _atk.Card.Zone == CardZone.InPlay)
            {
                int cd = _atk.Card.Def.Cooldown;
                if (_atk.QuickRefill)
                {
                    cd -= 1;
                }

                CooldownOps.PutIntoCooldown(
                    State.Of(_atk.Card.OwnerSeat), _atk.Card, cd, _cooldownBuffer, "本次进攻牌进入冷却区");
            }

            for (int i = 0; i < _defenseCards.Count; i++)
            {
                CardInstance c = _defenseCards[i];
                if (c.Zone == CardZone.InPlay)
                {
                    CooldownOps.PutIntoCooldown(
                        State.Of(c.OwnerSeat), c, null, _cooldownBuffer, "本次防御牌进入冷却区");
                }
            }

            FlushCooldown();

            if (State.IsOver)
            {
                _phase = Phase.Finished;
                return;
            }

            // 未防御分支：P6 —— 在该次防御决策完成后立即确定，并在第 ④ 步一并结算
            if (!_defenseSuccess)
            {
                if (_atk.CoolMinusIfUnblocked > 0 && _atk.Card != null && _atk.Card.IsCooling)
                {
                    _cooldownBuffer.Clear();
                    for (int i = 0; i < _atk.CoolMinusIfUnblocked; i++)
                    {
                        CooldownOps.TryHaste(State.Of(_atk.Card.OwnerSeat), _atk.Card, _cooldownBuffer);
                    }

                    FlushCooldown();
                }

                if (_atk.ExtraDamage > 0)
                {
                    ApplyDamage(1 - _atk.AttackerSeat, _atk.ExtraDamage, "对方无法防御（" + _atk.Card.Def.Name + "）");
                }

                if (_atk.ExtraMaxHpCut > 0)
                {
                    CutMaxHp(1 - _atk.AttackerSeat, _atk.ExtraMaxHpCut);
                }

                if (State.IsOver)
                {
                    _phase = Phase.Finished;
                    return;
                }

                if (_atk.SlowIfUnblocked > 0)
                {
                    // 借用第 ③ 步的「减速」重复决策，但不重放 ③ 的效果队列
                    _activeEffect = new EffectDef(EffectTrigger.Attack, EffectOp.Slow, _atk.SlowIfUnblocked);
                    _repeatLeft = _atk.SlowIfUnblocked;
                    _excludeTargets.Clear();
                    _stage3QueueSkipped = true;
                    _stage3ThenPhase = Phase.Stage5;
                    _phase = Phase.Stage3;
                    return;
                }
            }

            GoToTriggerOr(Phase.Stage5);
        }

        // ══════════════════════════════════════════════════════
        //  ⑤ 光环激活  ⑥ 连击
        // ══════════════════════════════════════════════════════

        private void DoStage5()
        {
            // α / β 由**本牌在这一拍里的角色**决定（`Docs/rules/01-规则基线.md` §0、`02-卡牌图鉴.md`）：
            //   本牌作为**进攻牌**打出 → 只有 α 光环被点亮；
            //   本牌作为**防御牌**打出 → 只有 β 光环被点亮。
            //
            // ⚠ 2026-09-18 曾经不分角色地全部点亮（当时的理由是「光环与它是攻是防无关」），
            //   用户 2026-09-20 明确以卡面 α / β / γ 为准把那一条作废：
            //   本批 12 张光环卡的光环**全部标着 α**，所以拿它们去防御**不会、也不该**拿到光环。
            //   （自测里对应两条断言：`EventWatcher` 的防御牌光环期望，以及
            //    `RuleAssertions` 的「α 光环不被防御点亮」。）
            //
            // 时序仍然天然隔绝自己：防御牌在第 ④ 步才进冷却区，而第 ⑤ 步在防御决策之后，
            // 所以「本张牌当次用不上自己刚拿到的光环」这条不变。
            ActivateAuras(_atk.Card, EffectTrigger.Attack);

            for (int i = 0; i < _defenseCards.Count; i++)
            {
                ActivateAuras(_defenseCards[i], EffectTrigger.Defend);
            }

            _phase = Phase.Stage6;
        }

        /// <summary>
        /// 点亮一张刚进冷却区的牌上、<b>触发符号与本次角色相符</b>的光环指示物（第 ⑤ 步）。
        ///
        /// <para>本角色下没有该符号的光环 → 一枚都不点亮（<c>AuraLive</c> 保持 false，
        /// 快照与 UI 于是什么都不显示）。</para>
        ///
        /// <para>⚠ <b>γ（特殊）光环不在这里</b>：它的时机由卡面文字各自规定，
        /// 得有专门的触发点。本批 40 张卡没有 γ 光环，
        /// <see cref="CardLibrary.ValidateAuraTriggers"/> 会在有人加进来时报错提醒。</para>
        /// </summary>
        private void ActivateAuras(CardInstance card, EffectTrigger role)
        {
            if (card == null || !card.IsCooling)
            {
                return;
            }

            int tokens = card.Def.AuraTokenCountOf(role);
            if (tokens <= 0)
            {
                return;
            }

            card.AuraTokens = tokens;
            card.AuraLive = true;

            Emit(new AuraActivatedEvent
            {
                Seat = card.OwnerSeat,
                Card = card,
                Tokens = tokens,
            });
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

                case RequestKind.ChooseHasteTarget:
                case RequestKind.ChooseSlowTarget:
                    DoStage3Repeat(picked);
                    break;

                case RequestKind.ChooseZoneValue:
                    DoZoneValue(picked);
                    break;

                case RequestKind.ChooseRefreshTarget:
                    if (_readyRefreshQueue.Count > 0 && _phase == Phase.ReadyRefreshTrigger)
                    {
                        DoReadyRefresh(picked);
                        _phase = Phase.ReadyRefreshTrigger;
                    }
                    else
                    {
                        DoRefreshTarget(picked);
                    }

                    break;

                case RequestKind.ChooseCoolHandCards:
                    DoCoolHandSelection(picked);
                    break;

                case RequestKind.ChoosePeekCard:
                    DoPeekSelection(picked);
                    break;

                case RequestKind.ChooseRemoveFromGame:
                    DoRemoveFromGame(picked);
                    break;

                case RequestKind.ChooseCopyTarget:
                    DoCopyTarget(picked);
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

        private void ApplyDamage(int seat, int amount, string source)
        {
            if (amount <= 0 || State.IsOver)
            {
                return;
            }

            PlayerState p = State.Of(seat);
            p.Hp -= amount;
            Emit(new DamageTakenEvent { Seat = seat, Amount = amount, Source = source });
            CheckDeath();
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
            bool p0 = State.Player.IsDead;
            bool p1 = State.Ai.IsDead;
            if (!p0 && !p1)
            {
                return;
            }

            string reason0 = State.Player.Hp <= 0 ? "生命归零" : "生命上限归零";
            string reason1 = State.Ai.Hp <= 0 ? "生命归零" : "生命上限归零";

            if (p0 && p1)
            {
                State.Finish(-1, "双方同时出局");
            }
            else if (p0)
            {
                State.Finish(BattleState.SeatAi, "你" + reason0);
            }
            else
            {
                State.Finish(BattleState.SeatPlayer, "AI " + reason1);
            }

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
                    CollectReadyRefresh(ch.Card);
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
