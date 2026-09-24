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

        [Tooltip("AI 座位是否消耗光环。规划 §6：最简 AI 默认不使用。")]
        [SerializeField] private bool _aiUseAuras = false;

        [Tooltip("勾上则玩家座位也交给 AI 决策（调试 / 截图用，不用手点）。")]
        [SerializeField] private bool _autoPlay = false;

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
        private IAgent _ai;
        private Coroutine _routine;

        /// <summary>本轮 <see cref="BattleEngine.Advance"/> / <c>Submit</c> 累积的待演出事件。</summary>
        private readonly List<BattleEvent> _buffer = new List<BattleEvent>();

        private readonly List<string> _log = new List<string>();

        private bool _waitingPlayer;
        private bool _playerPicked;
        private int[] _playerPick;

        /// <summary>本次回填里「在判定区准备使用的光环」选项序号（2026-09-18）。</summary>
        private int[] _playerAuraPick;

        /// <summary>
        /// 这次回填是「只更新准备使用的光环」还是「连同主选择一起提交」。
        ///
        /// <para><b>必须区分</b>：前者**不消费**当前这一拍（引擎按新的准备集合重发一次），
        /// 后者才是真正的出牌 / 放弃。两者都靠 <c>AuraOptionIndices</c> 传光环，
        /// 只有这个标志能告诉引擎该走哪条路。</para>
        /// </summary>
        private bool _playerAuraPrepOnly;

        private DecisionSnapshot _pending;
        private int _aiSeat = BattleState.SeatAi;

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

        private void OnDestroy()
        {
            if (_engine != null)
            {
                _engine.OnEvent -= HandleEvent;
            }
        }

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
            LastSeed = seed;
            StopBattle();

            _engine = BattleEngine.Create(seed);
            _engine.OnEvent += HandleEvent;

            var ai = new SimpleAiAgent();
            ai.UseAuras = _aiUseAuras;
            _ai = ai;

            _log.Clear();
            _buffer.Clear();
            _pending = default(DecisionSnapshot);
            _waitingPlayer = false;
            _playerPicked = false;
            _playerPick = null;
            _playerAuraPick = null;
            _playerAuraPrepOnly = false;

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
            _engine.Start();

            int guard = 0;
            while (!_engine.IsOver)
            {
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

                if (req.Seat == _aiSeat || _autoPlay)
                {
                    if (_beatScale > 0f && _aiThinkSeconds > 0f)
                    {
                        yield return new WaitForSecondsRealtime(_aiThinkSeconds);
                    }

                    _engine.Submit(_ai.Decide(req));
                }
                else
                {
                    yield return CoWaitPlayer(req);
                }
            }

            // 终局：把最后一拍演完再报结果
            yield return CoPlayBeats();

            _routine = null;

            if (OnFinished != null)
            {
                OnFinished(_engine.State.WinnerSeat, _engine.State.EndReason);
            }
        }

        private IEnumerator CoWaitPlayer(DecisionRequest req)
        {
            _pending = DecisionSnapshot.From(req);
            _playerPicked = false;
            _playerPick = null;
            _playerAuraPick = null;
            _playerAuraPrepOnly = false;
            _waitingPlayer = true;

            if (OnPlayerDecision != null)
            {
                OnPlayerDecision(_pending);
            }

            while (!_playerPicked)
            {
                yield return null;
            }

            _waitingPlayer = false;
            _pending = default(DecisionSnapshot);

            // 回填后立刻广播一次状态，让 UI 把「选项高亮」清掉
            RaiseStateChanged();

            int[] pick = _playerPick ?? new int[0];
            int[] auras = _playerAuraPick ?? new int[0];

            DecisionResponse resp;
            if (_playerAuraPrepOnly)
            {
                // 只报备准备使用的光环，请引擎按新集合重发这一拍
                resp = DecisionResponse.PrepAuras(req.Seat, auras);
            }
            else if (auras.Length > 0)
            {
                // 出牌 + 准备的光环一起提交（同一拍里的两条通道）
                resp = DecisionResponse.WithAuras(req.Seat, pick, auras);
            }
            else
            {
                resp = DecisionResponse.Of(req.Seat, pick);
            }

            _engine.Submit(resp);
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
                    yield return new WaitForSecondsRealtime(wait);
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
            if (!_waitingPlayer)
            {
                return;
            }

            _playerPick = optionIndices ?? new int[0];
            _playerAuraPick = auraIndices ?? new int[0];
            _playerPicked = true;
        }

        /// <summary>
        /// 玩家只是在判定区加 / 减了一枚「准备使用」的光环 —— 只报备这枚变动，
        /// <b>不提交主选择</b>。引擎会按新的准备集合把本拍决策重发一次
        /// （防御牌的合法性依赖这份集合，必须由引擎重算）。
        /// </summary>
        public void SubmitPlayerAuraPrep(int[] auraIndices)
        {
            if (!_waitingPlayer)
            {
                return;
            }

            _playerPick = new int[0];
            _playerAuraPick = auraIndices ?? new int[0];
            _playerAuraPrepOnly = true;
            _playerPicked = true;
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

            if (e is GameOverEvent)
            {
                return 0.85f;
            }

            return 0.15f;
        }
    }
}
