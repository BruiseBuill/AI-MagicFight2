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

        [Header("存档（玩家卡池的来源）")]
        [Tooltip("本场战斗读哪个存档的卡池。\n"
                 + "· 战斗测试档（存档 1）= 默认。只有战斗读得到它；没有存档时用角色自带的卡池"
                 + "（BattlePool = 全 45 张），点了设置面板的「保存并重开」才落盘。\n"
                 + "· 主存档（存档 2）= 商店 / 强化 / 女巫工坊共用的那一份。正式跑冒险链路时切到它，"
                 + "这样战斗里用的牌就是商店买来的那些。\n"
                 + "⚠ 这两个档位是**两套独立的卡池**，互不影响。")]
        [SerializeField] private SaveSlot _saveSlot = SaveSlot.Test;

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
        public int LocalSeat { get { return _localSeat; } }
        public int ParticipantCount { get { return _engine == null ? 0 : _engine.State.Players.Count; } }
        public bool IsPaused { get { return _paused; } }
        /// <summary>
        /// 本场战斗读的存档档位。默认 <see cref="SaveSlot.Test"/>（战斗测试档）。
        ///
        /// <para>切到 <see cref="SaveSlot.Main"/> 之后，本场战斗用的就是商店买过、女巫消耗过的
        /// 那一份卡池 —— 也就是「冒险链路里的那个玩家」。</para>
        /// </summary>
        public SaveSlot ActiveSaveSlot { get { return _saveSlot; } }

        /// <summary>当前档位的落盘路径（设置面板显示用）。</summary>
        public string CardPoolSavePath { get { return SaveStore.FilePathFor(_saveSlot); } }
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
        public int OpponentSeat { get { return _engine == null ? (_localSeat == 0 ? 1 : 0) : _engine.State.Mode.SelectDefender(_engine.State, _localSeat); } }
        public BattleOutcome Outcome { get { return _engine == null ? null : _engine.State.Outcome; } }

        /// <summary>
        /// 本场战斗那只怪<b>自带的美术</b>（2026-10-03）；没配返回 null。
        ///
        /// <para><see cref="BattleUi"/> 拿到 null 时退回 <c>BattleArtLibrary.Monster</c>
        /// —— 那是「所有怪共用一份」的旧口径，保留它才不会让没配美术的怪变成空白。</para>
        /// </summary>
        public BattleArtLibrary.CharacterSet MonsterArtSet { get { return _monsterArt; } }

        private Coroutine _routine;

        /// <summary>本轮 <see cref="BattleEngine.Advance"/> / <c>Submit</c> 累积的待演出事件。</summary>
        private readonly List<BattleEvent> _buffer = new List<BattleEvent>();

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
            BattleSetup setup = BuildSetup();
            BattleEngine engine = BattleEngine.Create(seed, setup);
            var controllers = new Dictionary<int, IParticipantController>();
            for (int seat = 0; seat < setup.Participants.Count; seat++)
            {
                ParticipantSetup participant = setup.Participants[seat];
                IParticipantController controller = ControllerFactory == null ? null : ControllerFactory(seat, participant);
                if (controller == null)
                    controller = participant.Control == ControlKind.Human && !_autoPlay
                        ? (IParticipantController)new HumanController()
                        // 2026-10-03：默认 AI 换成 HeuristicAgent（四流派 + 出牌优先级 + 防御口径），
                        // 并按角色的 aiProfile 选择怪物专属子类（空 = 通用）。
                        // ⚠ 必须把 engine.State 传进去 —— 流派判定要读手牌，减速流派还要读
                        //   对方冷却区张数与手牌数；不传的话这两条功能会静默降级。
                        : new AiController(MonsterAgentFactory.Create(
                            participant.Character.AiProfile, engine.State,
                            _aiUseAuras, unchecked(seed + seat)));
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
                engine.MaxTurns = BattleEngine.AiTurnLimit;
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
        /// 某个座位用哪份角色资产。查找顺序（2026-10-03）：
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
            if (_monsterConfig != null && seat != _localSeat)
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
                if (seat != _localSeat && config.useCustomArt)
                {
                    _monsterArt = config.art;
                }

                return config.CreateDefinition(catalog);
            }

            // 兜底（没配任何角色资产）—— 本地座位是人类口径，其余是怪物口径。
            // ⚠ 2026-10-03 起这里必须显式给 DealProfile：不给就会落回引擎默认（人类 6 张），
            //   兜底怪物又会变成「开局 6 张、第 2–3 回合各补 1」。
            bool local = seat == _localSeat;
            return new CharacterDefinition(
                local ? "player.default" : "monster.default",
                local ? "你" : "怪物",
                local ? CharacterKind.Player : CharacterKind.Monster,
                4, 4, 8,
                CardPool.AllCards(catalog),
                null, null,
                local ? DealProfile.Human() : DealProfile.Monster());
        }

        private BattleSetup BuildSetup(CardPool localOverride = null)
        {
            _monsterArt = null;
            int count = _participants == null || _participants.Length == 0 ? 2 : _participants.Length;
            if (_localSeat < 0 || _localSeat >= count) throw new InvalidOperationException("本地玩家座位无效。");
            var participants = new List<ParticipantSetup>();
            for (int seat = 0; seat < count; seat++)
            {
                CharacterDefinition character = BaseDefinition(seat);
                if (seat == _localSeat)
                {
                    CardPool pool;
                    string error;
                    if (localOverride != null) pool = localOverride;
                    else if (!SaveStore.TryLoadCardPool(_saveSlot, character, out pool, out error)) Debug.LogWarning(error + " 本局使用角色默认卡池。");
                    character = character.WithCardPool(pool);
                }
                bool defaults = _participants == null || _participants.Length == 0;
                participants.Add(new ParticipantSetup(character,
                    defaults ? (seat == _localSeat ? ControlKind.Human : ControlKind.Ai) : _participants[seat].control,
                    defaults ? seat : _participants[seat].team));
            }
            return new BattleSetup(participants, ModeFactory == null ? new DuelMode() : ModeFactory(),
                EffectRegistryFactory == null ? null : EffectRegistryFactory());
        }

        public ICardCatalog GetCardCatalog()
        {
            CardCatalogAsset catalog = _cardCatalog == null ? Resources.Load<CardCatalogAsset>("CardCatalog") : _cardCatalog;
            return catalog == null ? CardCatalog.Builtin() : catalog.CreateCatalog();
        }

        public CardPool GetAllCardPool() { return CardPool.AllCards(GetCardCatalog()); }

        public CardInstance CreateGeneratedCard(CardDefinitionAsset definition, int seat = -1)
        {
            if (_engine == null) throw new InvalidOperationException("请先开局。");
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            int targetSeat = seat < 0 ? _localSeat : seat;
            return _engine.GenerateCard(definition.CreateDefinition(10000 + targetSeat), targetSeat);
        }

        public CharacterDefinition GetLocalCharacterDefinition()
        {
            return _setup == null ? BuildSetup().Participants[_localSeat].Character : _setup.Participants[_localSeat].Character;
        }

        public bool TrySaveCardPoolAndRestart(CardPool pool, out string error)
        {
            error = null;
            if (pool == null) { error = "卡池不能为空。"; return false; }
            BattleSetup setup;
            try { setup = BuildSetup(pool); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
            if (!SaveStore.TrySaveCardPool(_saveSlot, pool, out error)) return false;
            StartBattle();
            return true;
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
                return 0.18f;
            }

            if (e is CardReturnedEvent)
            {
                return 0.18f;
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
                return 0.22f;
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
