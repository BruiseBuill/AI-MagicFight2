using System;
using System.Collections;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 表现桥（M6）。
    ///
    /// <para><b>它解决什么</b>：规则结算天生<strong>同步顺序</strong>（<c>BattleEngine.Advance</c> 一次
    /// 可能连发几十条事件），而 Unity UI 要<strong>异步等点击</strong>。driver 做两件事：</para>
    /// <list type="number">
    /// <item>把同步事件流<strong>缓冲成一条条 beat</strong>，按节拍逐个回放 —— 一次 <c>Advance</c> 的结果
    /// 会被切成「AI 打出闪电 → 最终力量 5 → 你放弃 → 掉 1 点」这样有节奏的演出。</item>
    /// <item>把「AI 座位即时应答 / 玩家座位等点击」这套暂停-恢复循环真正跑在 Unity 里。</item>
    /// </list>
    ///
    /// <para><b>铁律第 2 条</b>：本类只做「读快照 + 订阅事件 + 提交决策」三件事，
    /// 不反向调用引擎内部，也不替 UI 判断规则合法性（非法选项引擎根本不会生成）。</para>
    ///
    /// <para>UI 侧的接入方式：订阅 <see cref="OnBeat"/> 做演出、<see cref="OnStateChanged"/> 重绑快照、
    /// <see cref="OnPlayerDecision"/> 渲染选项；玩家点完调 <see cref="SubmitPlayerDecision"/>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleDriver : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  配置
        // ══════════════════════════════════════════════════════

        [Header("对局")]
        [Tooltip("勾上 = 每次开局都抽一个新种子（对局不可复现）；不勾 = 用下面的固定种子，便于排查问题。")]
        [SerializeField] private bool _randomSeed = true;

        [Tooltip("固定随机种子 —— 同种子必然复现同一局（Core 用自实现的确定性 Rng）。只有关掉随机时才用。")]
        [SerializeField] private int _seed = 20260916;

        [Tooltip("AI 是否动用光环（免疫 / 防御补值）。HeuristicAgent 的防御口径依赖它，默认开。")]
        [SerializeField] private bool _aiUseAuras = true;

        [Tooltip("勾上则玩家座位也交给 AI 决策（调试 / 截图用，不用手点）。")]
        [SerializeField] private bool _autoPlay = false;

        [Header("参战角色（留空使用默认玩家和怪物）")]
        [SerializeField] private BattleParticipantConfig[] _participants = new BattleParticipantConfig[0];
        [SerializeField] private CardCatalogAsset _cardCatalog;
        [SerializeField] private int _localSeat;

        /// <summary>
        /// <b>本场战斗的怪物资产</b>（2026-10-03，怪物框架）。
        ///
        /// <para>一只怪 = 一份 <see cref="CharacterConfig"/> 资产
        /// （建议放 <c>Assets/Resources/Monsters/Monster_*.asset</c>），
        /// 上面写着它的<b>血量 / 卡池 / AI 档案 / 强化 / 能力（含「第 N 回合自爆」这类行为）/
        /// 发牌口径 / 可选美术</b> —— 改怪只需改这份资产，不用碰代码、不用碰 Prefab。</para>
        ///
        /// <para><b>与 <see cref="_participants"/> 的分工</b>：那份是「谁能上场的完整名单」
        /// （含控制方式与队伍）。这一格是<b>更省事的单人入口</b> —— 留空名单、只填一只怪，
        /// 它就当对手（<see cref="ControlKind.Ai"/>）。二者同时填时，
        /// <b>名单里显式写的角色优先</b>（见 <c>ConfigFor</c>）。</para>
        ///
        /// <para>留空 = 老行为：兜底「怪物」4/4、全 45 张牌池、怪物发牌口径。</para>
        /// </summary>
        [Tooltip("本场战斗的怪物资产（留空 = 兜底怪物）。建议 Resources/Monsters/Monster_*.asset。")]
        [SerializeField] private CharacterConfig _monsterConfig;


        [Header("演出节奏")]
        [Tooltip("节拍总倍率：0 = 关闭演出（事件仍逐条发，只不等待）。")]
        [SerializeField] private float _beatScale = 1f;

        [Tooltip("AI 思考停顿（秒），让出牌看起来不是瞬发。")]
        [SerializeField] private float _aiThinkSeconds = 0.28f;

        [Tooltip("开打前等一帧，确保 UI 已经订阅完事件。")]
        [SerializeField] private bool _autoStart = true;

        // ══════════════════════════════════════════════════════
        //  运行时
        // ══════════════════════════════════════════════════════

        private BattleEngine _engine;
        private readonly Dictionary<int, IParticipantController> _controllers = new Dictionary<int, IParticipantController>();
        private IParticipantController _activeController;
        private BattleSetup _setup;
        private BattleArtLibrary.CharacterSet _monsterArt;
        private bool _paused;
        public Func<IBattleMode> ModeFactory { get; set; }
        public Func<EffectRegistry> EffectRegistryFactory { get; set; }
        public Func<int, ParticipantSetup, IParticipantController> ControllerFactory { get; set; }

        /// <summary>
        /// <b>本机视角的座位</b>（= 界面把谁摆在「我方」那一侧）。
        ///
        /// <para>2026-10-30 起<b>可以运行时切换</b>：同屏热座（玩家对玩家）时，轮到谁操作
        /// 就把视角切到谁的座位 —— 见 <c>BattleUi.HandleDecision</c>。正常单机
        /// （玩家对 AI）一局里不会变，值来自 <see cref="GameSettings.LocalSeat"/>。</para>
        /// </summary>
        public int LocalSeat { get { return _activeLocalSeat < 0 ? _localSeat : _activeLocalSeat; } }

        /// <summary>
        /// 把本机视角切到 <paramref name="seat"/>（热座 / 未来网络客户端切换视角用）。
        /// 越界直接忽略 —— 这个口子只服务界面，不该因为一次误调把整局打崩。
        /// </summary>
        public void SetLocalSeat(int seat)
        {
            if (_engine != null && (seat < 0 || seat >= _engine.State.Players.Count))
            {
                return;
            }

            _activeLocalSeat = seat;
        }

        /// <summary>这个座位是不是人类操作（= 界面要等他点）。判据只有 <see cref="GameSettings"/> 一处。</summary>
        public bool IsHumanSeat(int seat)
        {
            return GameSettings.IsHumanSeat(seat);
        }
        public int ParticipantCount { get { return _engine == null ? 0 : _engine.State.Players.Count; } }
        public bool IsPaused { get { return _paused; } }

        public void SetPaused(bool paused) { _paused = paused; }

        /// <summary>
        /// 设置面板里的<b>测试开关</b>（2026-10-03）：打开后「按住查看怪物手牌」不再按玩家的
        /// 已知情报盖牌背，<b>一律正面显示</b>，便于校正 AI 逻辑。
        ///
        /// <para>⚠ <b>刻意不落盘</b> —— 它是调试口径、不是游戏设置；每次进 Play 默认关闭。</para>
        /// <para>⚠ 只影响玩家查看的那块浮层（<c>MonsterHandView</c>），不改变引擎里任何情报判定
        /// （<c>LookAndCool</c> 的翻牌、AI 的决策都不受影响）。</para>
        /// </summary>
        public bool ForceRevealMonsterHand { get; set; }
        public int OpponentSeat { get { return _engine == null ? (LocalSeat == 0 ? 1 : 0) : _engine.State.Mode.SelectDefender(_engine.State, LocalSeat); } }
        public BattleOutcome Outcome { get { return _engine == null ? null : _engine.State.Outcome; } }

        /// <summary>
        /// 本场战斗那只怪<b>自带的美术</b>（2026-10-03）；没配返回 null。
        ///
        /// <para><see cref="BattleUi"/> 拿到 null 时退回 <c>BattleArtLibrary.Monster</c>
        /// —— 那是「所有怪共用一份」的旧口径，保留它才不会让没配美术的怪变成空白。</para>
        /// </summary>
        public BattleArtLibrary.CharacterSet MonsterArtSet { get { return _monsterArt; } }

        /// <summary>最近一次开局用的种子（<see cref="GetLocalCharacterDefinition"/> 在开局前
        /// 被调用时拿它去派生牌组 —— 没有它就只能是 0，抽出来的牌与真正开局那副不一致）。</summary>
        private int _lastSeed;

        /// <summary>
        /// <b>本局的本机座位</b>（2026-10-30）；<c>-1</c> = 还没开局、用 Inspector 上的
        /// <see cref="_localSeat"/>。
        ///
        /// <para><b>为什么要这一层而不是直接改 <see cref="_localSeat"/></b>：那是个
        /// <c>[SerializeField]</c>，Play 模式里改它会把「这一局用哪个座位」写回 Inspector，
        /// 退出 Play 后留痕，下次排查会被一个说不清来历的 1 绊住。运行态单独存一份就没有这个问题。</para>
        /// </summary>
        private int _activeLocalSeat = -1;

        private Coroutine _routine;

        /// <summary>本轮 <see cref="BattleEngine.Advance"/> / <c>Submit</c> 累积的待演出事件。</summary>
        private readonly List<BattleEvent> _buffer = new List<BattleEvent>();

        /// <summary>
        /// 是否正在逐拍回放缓冲事件（2026-10-31）。
        ///
        /// <para><b>为什么表现层需要知道这件事</b>：事件是「事后回放」的 —— 一次
        /// <c>Advance</c> 会把整个批次算完，<b>引擎状态早就走到终态</b>，而这些事件还要
        /// 按节拍一条条演。界面若在回放中途重读引擎（<c>CollectCooling</c> / <c>CollectHand</c>），
        /// 读到的是终态而不是「这一拍该有的样子」。</para>
        ///
        /// <para>最典型的一例（用户 2026-10-31 报的「减速后牌原地不动」）：引擎依次发出
        /// 「减速 +1」→「对方半场开始」→「进攻开始 −1」，三条事件一次性算完；回放到
        /// 「减速 +1」那一拍时，那张牌的剩余冷却已经是 −1 之后的值 —— 界面照着终态画，
        /// 牌直接被画在原行，**一次位移都没发生**。看起来像「向前向后抵消」，其实是
        /// 第一次位移压根没画出来。</para>
        ///
        /// <para>本属性给 <c>BattleUi</c> 一个门控：回放中（true）用<b>事件累加出来的演出态</b>，
        /// 回放外（false）才以引擎重建 —— 后者保证任何时刻都不会和规则跑偏。</para>
        /// </summary>
        public bool IsPlayingBeats { get; private set; }

        private readonly List<string> _log = new List<string>();

        private bool _waitingPlayer;
        private DecisionSnapshot _pending;

        // ── 事件 ───────────────────────────────────────────────

        /// <summary>逐条演出：每个事件一个节拍（UI 在这里播动画 + 刷状态）。</summary>
        public event Action<BattleEvent> OnBeat;

        /// <summary>一个节拍之后，快照可能变了 —— UI 在这里做全量重绑（列表都很小，够用）。</summary>
        public event Action OnStateChanged;

        /// <summary>轮到玩家决策：UI 渲染选项 + 置亮可点的牌。</summary>
        public event Action<DecisionSnapshot> OnPlayerDecision;

        /// <summary>对局结束：(胜者座位, 结束原因)。</summary>
        public event Action<int, string> OnFinished;

        /// <summary>对局真正开打（发完牌之前）—— UI 用来切「发牌中」以外的界面状态。</summary>
        public event Action OnStarted;

        /// <summary>
        /// 演出闸门（2026-09-20）：某个节拍之后<b>还要再等多少秒</b>（0 = 不等）。
        ///
        /// <para><b>为什么需要它</b>：<see cref="BeatDuration"/> 只知道事件类型，
        /// 回答的是「这类事件默认展示多久」；而有些演出要等「一条具体的动画播完」才知道时长 ——
        /// 例如怪物交牌防御后头顶那张卡面必须停够时间再消失，下一回合不能提前压上来。
        /// 于是把这个问题交回给认识动画的上层（UI）。</para>
        ///
        /// <para>与 <see cref="BeatDuration"/> 取<b>较大者</b>，并且照旧乘 <c>_beatScale</c> ——
        /// 所以「跳过演出」（<see cref="SetFastForward"/>）能把这部分时间一起压掉。
        /// 上层在 <see cref="OnBeat"/> 里把本拍要等的秒数算好（同一拍里 <c>OnBeat</c> 先于本委托被调用），
        /// 本类只负责等。</para>
        /// </summary>
        public Func<BattleEvent, float> BeatHold;

        // ── 只读访问 ───────────────────────────────────────────

        /// <summary>引擎是否可用（尚未 <see cref="StartBattle"/> 时为 false）。</summary>
        public bool IsRunning
        {
            get { return _engine != null; }
        }

        /// <summary>当前待玩家回填的决策；无则 <c>Seat = -1</c>。</summary>
        public DecisionSnapshot Pending
        {
            get { return _pending; }
        }

        /// <summary>本局是否已终局。</summary>
        public bool IsOver
        {
            get { return _engine != null && _engine.IsOver; }
        }

        /// <summary>事件流文本（供日志面板显示）。</summary>
        public IReadOnlyList<string> Log
        {
            get { return _log; }
        }

        public BattleSnapshot GetBattle()
        {
            return _engine == null ? default(BattleSnapshot) : BattleSnapshot.From(_engine.State);
        }

        public PlayerSnapshot GetPlayer(int seat)
        {
            return _engine == null ? default(PlayerSnapshot) : PlayerSnapshot.From(_engine.State.Of(seat));
        }

        /// <summary>把手牌塞进 sink（不返回列表，避免每帧分配）。</summary>
        public void CollectHand(int seat, List<CardSnapshot> sink)
        {
            sink.Clear();
            if (_engine == null)
            {
                return;
            }

            List<CardInstance> hand = _engine.State.Of(seat).Hand;
            for (int i = 0; i < hand.Count; i++)
            {
                sink.Add(CardSnapshot.From(hand[i]));
            }
        }

        /// <summary>把冷却区塞进 sink。</summary>
        public void CollectCooling(int seat, List<CardSnapshot> sink)
        {
            sink.Clear();
            if (_engine == null)
            {
                return;
            }

            List<CardInstance> zone = _engine.State.Of(seat).CoolingZone;
            for (int i = 0; i < zone.Count; i++)
            {
                sink.Add(CardSnapshot.From(zone[i]));
            }
        }

        /// <summary>
        /// 预判 <paramref name="seat"/> 下一次进攻会打出的牌的<b>元素</b>（2026-09-26）。
        ///
        /// <para><b>为什么只给元素、不给牌</b>：这是用户口径 —— 玩家点怪物只该看到
        /// 「它接下来会用什么系」，看到牌名就等于把手牌全告诉他了。所以在
        /// <see cref="BattleDriver"/> 这一层就把信息<b>收窄到最小</b>：
        /// 只返回一个 <see cref="CardElement"/>，调用方拿不到 <see cref="CardInstance"/>，
        /// 连想「顺手多显示一点」都做不到。</para>
        ///
        /// <para>预判口径见 <see cref="AttackForecast"/> 与 <see cref="HeuristicAgent"/>：
        /// 优先用<b>真正在对局的那个 AI</b>现算（它身上有开局锁定的流派与怪物子类的复写），
        /// 拿不到时退回静态预判。手牌为空返回 <see cref="CardElement.None"/>。</para>
        /// </summary>
        public CardElement ForecastAttackElement(int seat)
        {
            if (_engine == null)
            {
                return CardElement.None;
            }

            // 优先问真正在对局的那个 AI —— 只有它身上才有「已判定的流派」与怪物子类的复写
            //（强制流派 / 禁用某卡）。拿不到（人类座位，或外部注入了别的 AI）才退回静态预判。
            HeuristicAgent agent = HeuristicAgentAt(seat);
            CardInstance card = agent != null
                ? agent.PredictNextAttack(seat)
                : AttackForecast.PredictNextAttack(_engine.State, seat);

            return card == null ? CardElement.None : card.Def.Element;
        }

        /// <summary>取某个座位背后真正的 <see cref="HeuristicAgent"/>（不是它时返回 null）。</summary>
        private HeuristicAgent HeuristicAgentAt(int seat)
        {
            IParticipantController controller;
            if (!_controllers.TryGetValue(seat, out controller))
            {
                return null;
            }

            AiController ai = controller as AiController;
            return ai == null ? null : ai.Agent as HeuristicAgent;
        }

        // ══════════════════════════════════════════════════════
        //  生命周期
        // ══════════════════════════════════════════════════════

        private void Start()
        {
            if (_autoStart)
            {
                // 等到 Start 阶段之后 —— Unity 保证所有 Awake 先于所有 Start，
                // 所以这里开始能确保订阅者已经接上
                StartCoroutine(CoAutoStart());
            }
        }

        private IEnumerator CoAutoStart()
        {
            yield return null;
            StartBattle();
        }

        private void OnDestroy() { StopBattle(); }

        /// <summary>开一局（可在 Inspector 里重跑菜单触发）。种子按 <see cref="_randomSeed"/> 决定。</summary>
        [ContextMenu("开一局")]
        public void StartBattle()
        {
            StartBattle(_randomSeed ? NewRandomSeed() : _seed);
        }

        /// <summary>
        /// 本局实际用的种子。
        ///
        /// <para>随机开局之后必须把它显示给玩家 / 记进日志 —— 否则「这局出问题了」就再也回不来。</para>
        /// </summary>
        public int LastSeed { get; private set; }

        /// <summary>
        /// 抽一个开局种子。
        ///
        /// <para><b>为什么不用 Core 的确定性 Rng 自己抽</b>：那是「同一 seed 必须走出同一局」的
        /// 规则设施，用它来生成自己的种子会把这条前提绕成循环。
        /// <c>UnityEngine.Random</c> 由系统时钟初始化，两次开局撞车可以忽略；
        /// 区间取非负是为了避开个别平台 <c>Range(int,int)</c> 在极值上的溢出实现。</para>
        /// </summary>
        private static int NewRandomSeed()
        {
            return UnityEngine.Random.Range(0, int.MaxValue);
        }

        public void StartBattle(int seed)
        {
            _lastSeed = seed;

            // 2026-10-30：本机座位在开局这一刻定下来（主菜单设置里可选 0 / 1）。
            // 对局中途不改；热座时的临时切换走 SetLocalSeat。
            _activeLocalSeat = GameSettings.LocalSeat;

            BattleSetup setup = BuildSetup(seed);
            BattleEngine engine = BattleEngine.Create(seed, setup);
            var controllers = new Dictionary<int, IParticipantController>();
            for (int seat = 0; seat < setup.Participants.Count; seat++)
            {
                ParticipantSetup participant = setup.Participants[seat];
                IParticipantController controller = ControllerFactory == null ? null : ControllerFactory(seat, participant);
                if (controller == null)
                    controller = participant.Control == ControlKind.Human && !_autoPlay
                        ? (IParticipantController)new HumanController()
                        : CreateAiController(participant, engine.State, seat, seed);
                controllers.Add(seat, controller);
            }

            // 没有真人参与（含 _autoPlay 把玩家座位也交给 AI 的情况）→ 套上回合上限。
            // BattleEngine.Create 只按 setup 的 Control 判「全员 AI」，而这里 setup 仍写着
            // Human（玩家座位是 Human + _autoPlay 时尤其如此），所以要在此补一刀。
            // 见 BattleEngine.AiTurnLimit 的说明。
            bool anyHuman = false;
            foreach (IParticipantController controller in controllers.Values)
            {
                if (controller is HumanController)
                {
                    anyHuman = true;
                    break;
                }
            }

            if (!anyHuman)
            {
                engine.UseAiTurnLimit();
            }

            StopBattle();
            LastSeed = seed;
            _setup = setup;
            _engine = engine;
            foreach (var pair in controllers) _controllers.Add(pair.Key, pair.Value);
            _engine.OnEvent += HandleEvent;
            _paused = false;

            _log.Clear();
            _buffer.Clear();
            _pending = default(DecisionSnapshot);
            _waitingPlayer = false;
            if (OnStarted != null)
            {
                OnStarted();
            }

            _routine = StartCoroutine(CoRun());
        }

        /// <summary>
        /// 按<b>敌人智力</b>建一个 AI 控制器（2026-10-30）。
        ///
        /// <list type="bullet">
        /// <item><b>简单</b> → <see cref="SimpleAiAgent"/>：进攻只会「挑当前力量最大的一张」，
        /// 这正好是用户要的「按力量从大到小打牌」。刻意<b>不开光环、不换开局手牌</b> ——
        /// 这两条都会让它明显强于「只会按力量出牌」，与这一档的定位不符。</item>
        /// <item><b>标准</b> → <see cref="MonsterAgentFactory"/>（四流派 + 手写优先级），
        /// 即大改之前的行为，一个字节都没变。</item>
        /// </list>
        ///
        /// <para>⚠ <c>_aiUseAuras</c> 只喂给标准档：它是「AI 是否动用光环」的调试开关，
        /// 简单档按定义不用光环。</para>
        /// </summary>
        private IParticipantController CreateAiController(ParticipantSetup participant,
            BattleState state, int seat, int seed)
        {
            if (GameSettings.Difficulty == AiDifficulty.Easy)
            {
                var easy = new SimpleAiAgent
                {
                    UseAuras = false,
                    ReplaceWeakCards = false,
                    BlindPickSeed = unchecked(seed + seat),
                    // 2026-10-31：只给「雪崩那种双方一起的区域减速」数张数用 ——
                    // 没有它，「自己 4 张 / 对面 0 张」时照样会放（等于自我减速）。
                    State = state,
                };
                return new AiController(easy);
            }

            // 2026-10-03：默认 AI 换成 HeuristicAgent（四流派 + 出牌优先级 + 防御口径），
            // 并按角色的 aiProfile 选择怪物专属子类（空 = 通用）。
            // ⚠ 必须把 state 传进去 —— 流派判定要读手牌，减速流派还要读
            //   对方冷却区张数与手牌数；不传的话这两条功能会静默降级。
            // 2026-10-08：手写出牌优先级挂在角色定义上（来自它的卡池资产），
            // 与 AiProfile 同一条传递路径 —— 一个座位一份，不会串台。
            return new AiController(MonsterAgentFactory.Create(
                participant.Character.AiProfile, state,
                _aiUseAuras, unchecked(seed + seat),
                participant.Character.CardPriorities));
        }

        public void StopBattle()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (_engine != null)
            {
                _engine.OnEvent -= HandleEvent;
                _engine = null;
            }

            _waitingPlayer = false;
            _pending = default(DecisionSnapshot);
            _activeController = null;
            // 回放协程被停掉时 IsPlayingBeats 会留在 true（迭代器没有 finally 复位）
            IsPlayingBeats = false;
            foreach (IParticipantController controller in _controllers.Values) controller.Cancel();
            _controllers.Clear();
            _paused = false;
        }

        /// <summary>关掉节拍等待，把整局瞬间跑完（「跳过演出」）。</summary>
        public void SetFastForward(bool on)
        {
            _beatScale = on ? 0f : 1f;
        }

        // ══════════════════════════════════════════════════════
        //  主循环
        // ══════════════════════════════════════════════════════

        private IEnumerator CoRun()
        {
            while (_paused) yield return null;
            _engine.Start();

            int guard = 0;
            while (!_engine.IsOver)
            {
                while (_paused) yield return null;
                if (++guard > 5000)
                {
                    Debug.LogError("[MagicBrawl] BattleDriver 决策轮次超过 5000，疑似死循环");
                    yield break;
                }

                if (_engine.Pending == null)
                {
                    _engine.Advance();
                }

                // 把这一轮（Advance 或上一次 Submit）产生的事件逐拍演完
                yield return CoPlayBeats();

                if (_engine.IsOver)
                {
                    break;
                }

                DecisionRequest req = _engine.Pending;
                if (req == null)
                {
                    // 引擎既没终局也没有待决策 —— 状态机异常
                    Debug.LogError("[MagicBrawl] 引擎既未终局也没有待决策 —— 状态机卡住");
                    yield break;
                }

                yield return CoDecision(req);
            }

            // 终局：把最后一拍演完再报结果
            yield return CoPlayBeats();

            _routine = null;

            if (OnFinished != null)
            {
                OnFinished(_engine.State.WinnerSeat, _engine.State.EndReason);
            }
        }

        private IEnumerator CoDecision(DecisionRequest request)
        {
            IParticipantController controller = _controllers[request.Seat];
            while (_paused) yield return null;
            if (!controller.UsesLocalInput && _beatScale > 0f)
                yield return CoWaitSeconds(_aiThinkSeconds);
            controller.BeginDecision(request);
            _activeController = controller;
            _waitingPlayer = controller.UsesLocalInput;
            if (_waitingPlayer)
            {
                _pending = DecisionSnapshot.From(request);
                if (OnPlayerDecision != null) OnPlayerDecision(_pending);
            }

            DecisionResponse response;
            while (true)
            {
                while (_paused) yield return null;
                if (controller.TryTakeResponse(out response)) break;
                yield return null;
            }
            _waitingPlayer = false;
            _activeController = null;
            _pending = default(DecisionSnapshot);
            RaiseStateChanged();
            _engine.Submit(response);
        }

        private IEnumerator CoWaitSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds || _paused)
            {
                yield return null;
                if (!_paused) elapsed += Time.unscaledDeltaTime;
            }
        }

        /// <summary>逐拍回放缓冲里的事件。</summary>
        private IEnumerator CoPlayBeats()
        {
            // 取副本再清空 —— 演出过程中 Submit 可能又塞进新事件
            int count = _buffer.Count;
            if (count == 0)
            {
                yield break;
            }

            var beats = new List<BattleEvent>(count);
            for (int i = 0; i < count; i++)
            {
                beats.Add(_buffer[i]);
            }

            _buffer.RemoveRange(0, count);

            // 从这里到循环结束，引擎状态都是「整批算完的终态」——
            // 界面必须改用事件累加出来的演出态（见 IsPlayingBeats 的说明）。
            IsPlayingBeats = true;
            for (int i = 0; i < beats.Count; i++)
            {
                while (_paused) yield return null;
                BattleEvent e = beats[i];

                _log.Add(e.Describe());
                if (_log.Count > 400)
                {
                    _log.RemoveAt(0);
                }

                if (OnBeat != null)
                {
                    OnBeat(e);
                }

                // 2026-10-31：**开局发牌合并成一拍**。
                //
                // 发牌事件一拍一张，每拍都会 RaiseStateChanged → BattleUi.RefreshAll
                // 重建整副手牌（还带飞行 ghost）——12 张牌就是 12 次全量刷新，实测
                // 「开局到换牌界面」要 4~5 秒。而发牌这件事根本不需要逐张刷新：
                // 手牌一次性铺开、逐张冒出来的观感交给 HandView 的入场错开（EnterDelay）。
                //
                // 判据是「后面紧跟着的还是发牌」，不是「事件总数」—— 效果抽牌（抽 2 张）
                // 同样合在一拍里，收尾那一张照常刷新 + 等待。
                if (e is CardDrawnEvent && i + 1 < beats.Count && beats[i + 1] is CardDrawnEvent)
                {
                    // 连一帧都不让出：发牌这一段整批在**同一帧**里过完。
                    // 让出帧只是为了让玩家看到逐张落下，而这件事现在由最后的
                    // 一次性刷新 + HandView 的入场错开承担 —— 多让 11 帧纯属白等。
                    continue;
                }

                // 2026-10-31：**连续的冷却变化也合成一拍**。
                //
                // 一次区域减速 / 加速会命中整档（最多 4~5 张），引擎每张发一条
                // CooldownChangedEvent。逐条刷新 = 迷你卡一张一张挪（相邻两条只隔 0.06s，
                // 后一张刚开始动、前一张还没到位）；若这一批正好是本回合最后一个效果，
                // 下一拍（对方进攻）会紧跟着压上来 —— 玩家看到的就是「被快进 / 直接省略」。
                //
                // 合并之后：整批在同一帧刷新一次（所有受影响的牌**同时**起动画），
                // 然后由 BattleUi 把这一拍的等待抬到冷却位移动画长度（0.24s + 余量），
                // 动画播完才轮到下一拍。日志与逐条提示照旧在 OnBeat 里过。
                //
                // ⚠ 判据不能只写「下一条也是 CooldownChangedEvent」：冷却归零回手的那一张，
                //   引擎会在它自己那条冷却事件后**紧跟一条 CardReturnedEvent**（FlushCooldown），
                //   那样整批会被回手切成两半 —— 回合开始「冷却区全体前进」就变成几张一起动、
                //   后面几张各自一拍，观感是「一张一张往前挪」。判据收进 ContinuesCooldownBatch。
                if (ContinuesCooldownBatch(beats, i))
                {
                    continue;
                }

                RaiseStateChanged();

                // 节拍时长与「演出闸门」取较大者：前者按事件类型给默认时长，
                // 后者由 UI 报「本拍这动画还要播多久」。两者都受 _beatScale 支配。
                float wait = Mathf.Max(BeatDuration(e), BeatHold == null ? 0f : BeatHold(e)) * _beatScale;
                if (wait > 0f)
                {
                    yield return CoWaitSeconds(wait);
                }
                else
                {
                    // 关演出时也要让出一帧，否则 UI 一帧内重绑上千次
                    yield return null;
                }
            }

            // 回放结束 = 所有事件都演完了，引擎状态与演出态此刻等价。
            // 下一次界面刷新会以引擎重建（见 BattleUi.SyncCoolingLive）。
            IsPlayingBeats = false;
        }

        /// <summary>
        /// 这一条是不是「同一批冷却演出」里还要继续往后播的一条（2026-10-31）。
        ///
        /// <para><b>为什么要放宽判据</b>：<c>FlushCooldown</c> 逐条发事件时，一张牌若冷却
        /// 归零回手，会在<b>它自己那条冷却事件后面紧跟一条 <see cref="CardReturnedEvent"/></b>。
        /// 只判「下一条也是 <see cref="CooldownChangedEvent"/>」的话，回手会把整批切成两半：
        /// 回合开始时「冷却区全体前进」就变成前几张同帧动、后面几张各自一拍，
        /// 观感正是用户报的「一张一张往前挪」。回手是这批冷却演出的附属事件，不该打断它。</para>
        ///
        /// <para>判据只看事件本身（谁的牌、加还是减都不管），不涉及任何规则判断。</para>
        /// </summary>
        private static bool ContinuesCooldownBatch(List<BattleEvent> beats, int i)
        {
            BattleEvent e = beats[i];
            bool cooling = e is CooldownChangedEvent;
            if (!cooling && !(e is CardReturnedEvent))
            {
                return false;
            }

            int next = i + 1;
            if (next >= beats.Count)
            {
                return false;
            }

            if (beats[next] is CooldownChangedEvent)
            {
                return true;
            }

            // 回手紧跟在它自己那条冷却事件后面：只要回手之后还接着冷却事件，
            // 这一条（冷却事件或回手）都属于同一批。
            return beats[next] is CardReturnedEvent
                   && next + 1 < beats.Count
                   && beats[next + 1] is CooldownChangedEvent;
        }

        private void RaiseStateChanged()
        {
            if (OnStateChanged != null)
            {
                OnStateChanged();
            }
        }

        // ══════════════════════════════════════════════════════
        //  玩家回填
        // ══════════════════════════════════════════════════════

        /// <summary>玩家点完了：回填选项序号（主选择）。</summary>
        public void SubmitPlayerDecision(params int[] optionIndices)
        {
            SubmitPlayerDecision(optionIndices, null);
        }

        /// <summary>
        /// 玩家点完了：主选择 + 在判定区「准备使用」的光环（2026-09-18）。
        ///
        /// <para><paramref name="auraIndices"/> 传空数组 / null 表示不消耗光环。
        /// <paramref name="optionIndices"/> 传空数组表示「没有主选择」（例如免疫光环：
        /// 它一消耗就整个攻击被免疫，不需要交牌）。</para>
        /// </summary>
        public void SubmitPlayerDecision(int[] optionIndices, int[] auraIndices)
        {
            if (!_waitingPlayer || _paused || _activeController == null) return;
            _activeController.Submit(DecisionResponse.WithAuras(_pending.Seat,
                optionIndices ?? new int[0], auraIndices ?? new int[0]));
        }

        public void SubmitPlayerAuraPrep(int[] auraIndices)
        {
            if (!_waitingPlayer || _paused || _activeController == null) return;
            _activeController.Submit(DecisionResponse.PrepAuras(_pending.Seat, auraIndices ?? new int[0]));
        }

        /// <summary>玩家点「放弃」（等价于回填 Skip 选项）。</summary>
        public void SubmitSkip()
        {
            if (!_waitingPlayer)
            {
                return;
            }

            DecisionSnapshot snap = _pending;
            if (snap.Options != null)
            {
                for (int i = 0; i < snap.Options.Count; i++)
                {
                    if (snap.Options[i].IsSkip)
                    {
                        SubmitPlayerDecision(snap.Options[i].Index);
                        return;
                    }
                }
            }

            SubmitPlayerDecision();
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 某个座位用哪份角色资产。查找顺序（2026-10-28 起三项）：
        /// ① <b>怪物资产</b>（对非本地座位）→ ② 名单里显式写的那一份 → ③ null（用兜底）。
        ///
        /// <para>⚠ <b>怪物资产优先，不是名单优先</b> —— 这是实测逼出来的：本工程的
        /// <c>BattleCanvas.prefab</c> 里 <c>_participants</c> <b>早就填着</b>
        /// 玩家 / 怪物的那份老名单（<c>DefaultPlayer</c> / <c>DefaultMonster</c>）。
        /// 若名单优先，新加的 <c>_monsterConfig</c> 会<b>永远不生效</b>，而且症状是
        /// 「进 Play 一切正常」—— 只有去读怪物血量才会发现还是老那份 4/4。</para>
        ///
        /// <para>要让名单生效（例如 3 人以上、或两个座位都自定义），把
        /// <c>_monsterConfig</c> 留空即可。</para>
        /// </summary>
        private CharacterConfig ConfigFor(int seat)
        {
            // ① 本场战斗的怪
            if (_monsterConfig != null && seat != LocalSeat)
            {
                return _monsterConfig;
            }

            bool hasList = _participants != null && _participants.Length > 0;
            if (hasList && seat >= 0 && seat < _participants.Length
                && _participants[seat] != null && _participants[seat].character != null)
            {
                return _participants[seat].character;
            }

            if (hasList && seat >= 0 && seat < _participants.Length && _monsterConfig == null)
            {
                throw new InvalidOperationException("参战角色配置不完整。");
            }

            return null;
        }

        private CharacterDefinition BaseDefinition(int seat)
        {
            CardCatalogAsset catalogAsset = _cardCatalog == null ? Resources.Load<CardCatalogAsset>("CardCatalog") : _cardCatalog;
            ICardCatalog catalog = catalogAsset == null ? null : catalogAsset.CreateCatalog();

            CharacterConfig config = ConfigFor(seat);
            if (config != null)
            {
                // 怪物自带的美术（可选）：没勾「用自己的美术」时交出 null，
                // 让 BattleUi 退回 BattleArtLibrary.Monster。
                // ⚠ 判据必须是那个 bool，不能判 `art != null` —— 它是 [Serializable] class，
                //   反序列化之后**永远非 null**（只是一组空 Clip）。用 null 判会让每只怪
                //   都被绑上一份空美术，表现为「怪物整个不见了」而且零报错。
                if (seat != LocalSeat && config.useCustomArt)
                {
                    _monsterArt = config.art;
                }

                return config.CreateDefinition(catalog);
            }

            // 兜底（没配任何角色资产）—— 本地座位是人类口径，其余是怪物口径。
            // ⚠ 2026-10-03 起这里必须显式给 DealProfile：不给就会落回引擎默认（人类 6 张），
            //   兜底怪物又会变成「开局 6 张、第 2–3 回合各补 1」。
            bool local = seat == LocalSeat;
            return new CharacterDefinition(
                local ? "player.default" : "monster.default",
                local ? "你" : "怪物",
                local ? CharacterKind.Player : CharacterKind.Monster,
                4, 4, BattleLaunch.DeckSize,
                CardPool.AllCards(catalog),
                null, null,
                DealProfile.Duel());
        }

        /// <summary>
        /// 组装这一局（2026-10-28 · 大改：牌组由<b>玩法</b>决定，不再读存档）。
        ///
        /// <para><b>两条来源</b>：</para>
        /// <list type="bullet">
        /// <item><description><b>自定义战斗</b>（<see cref="BattleLaunch.HasCustomDecks"/> 为真）——
        /// 用玩家在牌组编辑界面挑好的两份 8 张；</description></item>
        /// <item><description><b>其余情况</b>（标准战斗 / 直接从编辑器进 Play）——
        /// 由<b>本局种子</b>派生，双方各随机 8 张。同一颗种子必然抽出同一副牌，
        /// 所以「重试 = 回到本场最开始」自动成立。</description></item>
        /// </list>
        ///
        /// <para><b>⚠ 发牌口径一律覆盖成 <see cref="DealProfile.Duel"/></b>：
        /// 角色资产上写的（玩家 EngineDefault / 怪物 Monster）在这条路上都不再生效 ——
        /// 两种玩法的节奏必须完全一致（开局 8 张、之后不补不换）。</para>
        /// </summary>
        private BattleSetup BuildSetup(int seed)
        {
            _monsterArt = null;
            int count = _participants == null || _participants.Length == 0 ? 2 : _participants.Length;
            if (LocalSeat < 0 || LocalSeat >= count) throw new InvalidOperationException("本地玩家座位无效。");

            ICardCatalog catalog = GetCardCatalog();

            var participants = new List<ParticipantSetup>();
            for (int seat = 0; seat < count; seat++)
            {
                CharacterDefinition character = BaseDefinition(seat);
                character = character.WithCardPool(DeckFor(seat, catalog)).WithDeal(DealProfile.Duel());

                // 2026-10-30：**控制方式改由 GameSettings 决定** —— 主菜单里选的
                // 「玩家对 AI / AI 对 AI / 玩家对玩家」才是这一局的真相。
                // 名单（_participants）继续只表达「谁能上场 + 属于哪一队」：
                //   · 控制方式：它是玩法设置，不是角色属性；
                //   · 队伍：那是规则（谁是敌人），必须留在名单/模式里。
                bool hasList = _participants != null && _participants.Length > 0;
                int team = hasList && seat < _participants.Length && _participants[seat] != null
                    ? _participants[seat].team
                    : seat;
                participants.Add(new ParticipantSetup(character, GameSettings.ControlOf(seat), team));
            }
            // 2026-11-01：牌堆口径跟着玩法走 ——
            //   标准战斗：所有座位卡池求并集洗成一副（整表，本来就是同一副）；
            //   自定义战斗：**各洗各的 8 张**。原来并集成 16 张，玩家开局 6 张里平均
            //   3 张是对手选的，表现成「我选的牌没变成我的牌」。
            return new BattleSetup(participants, ModeFactory == null ? new DuelMode() : ModeFactory(),
                EffectRegistryFactory == null ? null : EffectRegistryFactory(),
                shareDeck: !BattleLaunch.HasCustomDecks);
        }

        /// <summary>
        /// 这一局给 <paramref name="seat"/> 用的<b>卡池定义</b>。
        ///
        /// <para><b>2026-10-31 用户最终口径：一局只有一副实体牌堆</b> —— 引擎把所有座位的
        /// 卡池求并集后洗成同一副，双方都从堆顶抽、换掉的牌回堆底。标准战斗的卡池就是
        /// 整张卡表；自定义战斗仍是玩家挑出的 8 张（双方互斥）。</para>
        /// </summary>
        private CardPool DeckFor(int seat, ICardCatalog catalog)
        {
            if (BattleLaunch.HasCustomDecks)
            {
                string[] ids = seat == LocalSeat ? BattleLaunch.PlayerCardIds : BattleLaunch.MonsterCardIds;
                if (ids != null && ids.Length == BattleLaunch.DeckSize)
                {
                    return new CardPool(ids, catalog);
                }
            }

            return CardPool.AllCards(catalog);
        }

        /// <summary>
        /// 本局的卡表 —— <b>唯一出口</b>（随机牌组、自定义牌组校验、卡池面板全走它）。
        ///
        /// <para>2026-10-30：主菜单的「<b>经典模式</b>」在这里生效 —— 开 = 只给前
        /// <see cref="GameSettings.ClassicCardLimit"/> 张。过滤放在这个出口而不是散在
        /// 各个消费点，才能保证「标准战斗抽不到 ao..ar」与「自定义战斗也选不到它」是同一件事。</para>
        /// </summary>
        public ICardCatalog GetCardCatalog()
        {
            CardCatalogAsset catalog = _cardCatalog == null ? Resources.Load<CardCatalogAsset>("CardCatalog") : _cardCatalog;
            ICardCatalog raw = catalog == null ? CardCatalog.Builtin() : catalog.CreateCatalog();
            return GameSettings.Filter(raw);
        }

        public CardPool GetAllCardPool() { return CardPool.AllCards(GetCardCatalog()); }

        public CardInstance CreateGeneratedCard(CardDefinitionAsset definition, int seat = -1)
        {
            if (_engine == null) throw new InvalidOperationException("请先开局。");
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            int targetSeat = seat < 0 ? LocalSeat : seat;
            return _engine.GenerateCard(definition.CreateDefinition(10000 + targetSeat), targetSeat);
        }

        public CharacterDefinition GetLocalCharacterDefinition()
        {
            return _setup == null
                ? BuildSetup(_lastSeed).Participants[LocalSeat].Character
                : _setup.Participants[LocalSeat].Character;
        }

        private void HandleEvent(BattleEvent e)
        {
            _buffer.Add(e);
        }

        /// <summary>
        /// 每种事件的节拍长度。设计意图：进攻 / 防御 / 掉血要「看得清」，
        /// 冷却与抽牌这类批量发生的要「不拖沓」（0.08 只是为了让状态逐条刷新，不是在等人看）。
        /// </summary>
        private static float BeatDuration(BattleEvent e)
        {
            if (e is TurnStartedEvent)
            {
                return 0.32f;
            }

            if (e is AttackDeclaredEvent)
            {
                return 0.55f;
            }

            if (e is AttackPowerResolvedEvent)
            {
                return 0.28f;
            }

            if (e is AuraConsumedEvent)
            {
                return 0.38f;
            }

            if (e is AuraActivatedEvent)
            {
                return 0.22f;
            }

            if (e is DefenseResolvedEvent)
            {
                return 0.5f;
            }

            if (e is DamageTakenEvent)
            {
                return 0.6f;
            }

            if (e is HpMaxChangedEvent)
            {
                return 0.4f;
            }

            if (e is HealEvent)
            {
                return 0.4f;
            }

            if (e is CooldownChangedEvent)
            {
                return 0.06f;
            }

            if (e is CardDrawnEvent)
            {
                // 2026-10-31：发牌原本 0.18s/张 —— 开局双方各 6 张就是 2.16 秒，
                // 玩家盯着手牌一张一张往外冒，进不了换牌。压到 0.06（约 4 帧一张），
                // 逐张出现的观感交给 HandView 的入场动画，不再由节拍扛。
                return 0.06f;
            }

            if (e is CardReturnedEvent)
            {
                // 冷却结束回手 —— 与发牌同一档，不必比它慢。
                return 0.12f;
            }

            if (e is HandRevealedEvent)
            {
                return 0.42f;
            }

            if (e is CardRemovedEvent)
            {
                return 0.4f;
            }

            if (e is ReplaceEvent)
            {
                // 换牌只是「手上那张换成了别的」，不是一段演出；0.22 攒在 3 张上就是 0.66 秒。
                return 0.12f;
            }

            if (e is SelfDestructEvent)
            {
                // 2026-10-03：自爆要有存在感 —— 与掉血同一档，别一闪而过。
                return 0.6f;
            }

            if (e is GameOverEvent)
            {
                return 0.85f;
            }

            return 0.15f;
        }
    }
}
