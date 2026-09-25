using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 对局界面总装（M8）。挂在 <c>BattleCanvas</c> 根上，是唯一同时认识
    /// <see cref="BattleDriver"/> 与各视图的类。
    ///
    /// <para><b>数据流（单向）</b>：</para>
    /// <code>
    /// BattleEngine ──OnEvent──► BattleDriver ──OnBeat/OnStateChanged──► BattleUi ──► 各 View
    ///                                            │
    ///                                       OnPlayerDecision
    ///                                            │
    /// 玩家点牌 ──► HandView/CooldownView/TargetPicker ──Option──► BattleUi ──► driver.SubmitPlayerDecision
    /// </code>
    ///
    /// <para><b>选项分类是这里唯一的「业务判断」，但它不是规则判断</b>：
    /// 引擎给的 <c>Options</c> 已经全部合法，本类只是按「有没有实体卡在屏幕上」把它们分成
    /// 「点那张卡」和「点选项按钮」两拨 —— 目的是让界面不出现「同一件事有两个入口」的重复。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleUi : MonoBehaviour
    {
        [SerializeField] private SettingsView _settings;
        private int LocalSeat { get { return _driver == null ? 0 : _driver.LocalSeat; } }
        private int OpponentSeat { get { return _driver == null ? 1 : _driver.OpponentSeat; } }

        private void BindSeats()
        {
            if (_hand != null) _hand.ConfigureSeats(LocalSeat, OpponentSeat);
            if (_cooldown != null) _cooldown.ConfigureSeats(LocalSeat, OpponentSeat);
            if (_transit != null) _transit.ConfigureSeats(LocalSeat, OpponentSeat);
        }


        [Header("驱动")]
        [SerializeField] private BattleDriver _driver;

        [Header("视图")]
        [SerializeField] private HandView _hand;
        [SerializeField] private CooldownView _cooldown;
        [SerializeField] private StageView _stage;
        [SerializeField] private TargetPicker _picker;

        [Header("M11 美术层（顶部状态栏 / 舞台角色 / 能量球）")]
        [SerializeField] private HudView _hud;
        [SerializeField] private CharacterView _hero;
        [SerializeField] private CharacterView _monster;

        [Header("M15 光环图标区（ArtLayer/HudBuff）")]
        [Tooltip("双方光环图标：玩家在左、敌人在右。长按看说明，拖到中央判定区即准备使用。")]
        [SerializeField] private HudBuffView _hudBuff;

        [Header("M16 判定区左侧的「已准备光环」标记条")]
        [Tooltip("Canvas/PreparedAura（M8 构建器预建）。判定区矩形在接线时向 HandView 实时取。")]
        [SerializeField] private PreparedAuraView _preparedBar;

        [Header("屏幕中央行动提示（轮到你进攻 / 进攻结束 / 防御结果）")]
        [Tooltip("Canvas/ActionBanner（M8 构建器预建）。闪一下就走，不吃射线。")]
        [SerializeField] private ActionBannerView _banner;

        [Header("M36 屏幕中央浮字（「该牌无法被加速 / 减速」这类一句话回答）")]
        [Tooltip("Canvas/FloatTip（M8 构建器预建）。中间弹出 → 向上移动 → 迅速透明消失，不吃射线。")]
        [SerializeField] private FloatTipView _floatTip;

        [Header("飞牌动画层（手牌 ↔ 冷却区）")]
        [Tooltip("Canvas/CardTransitLayer（M8 构建器预建）。")]
        [SerializeField] private CardTransitView _transit;

        [Header("M13/M35 头顶出牌展示（双方各一块）")]
        [Tooltip("对手（AI 座位）打出进攻 / 防御牌时，在其头顶弹出的那张卡面。")]
        [SerializeField] private PlayedCardView _playedFoe;

        [Tooltip("玩家自己打出进攻 / 防御牌时，在自己头顶弹出的那张卡面（M35 新增）。")]
        [SerializeField] private PlayedCardView _playedHero;

        [Header("发牌台（M9：开局 / 补牌后的替换）")]
        [SerializeField] private DealView _deal;

        [Header("M25 查看对方手牌浮层（雷云 / 狂躁蘑菇的第 2 个 α 效果）")]
        [Tooltip("Canvas/PeekLayer（M8 构建器预建、且刻意排在最后一个建 = 画在最上）。")]
        [SerializeField] private PeekView _peek;

        [Header("M27 手牌选择弹窗（磁暴 / 充能 / 电弧）")]
        [Tooltip("Canvas/HandPickLayer（M8 构建器预建、排在 PeekLayer 之后 = 压在最上）。")]
        [SerializeField] private HandPickView _handPick;

        [Header("M28 按住怪物手牌数 → 查看它的手牌")]
        [Tooltip("Canvas/MonsterHandLayer（M8 构建器预建）。玩家已知的牌正面朝上、未知的盖牌背。")]
        [SerializeField] private MonsterHandView _monsterHand;

        [Tooltip("挂在 ArtLayer/HandCount_Monster 上的「按住 / 松手」手势。留空则运行时自动去捞。")]
        [SerializeField] private PressHoldButton _monsterHandHold;

        [Header("结算浮层")]
        [SerializeField] private GameObject _resultRoot;
        [SerializeField] private TMP_Text _resultTitle;
        [SerializeField] private TMP_Text _resultSub;
        [SerializeField] private Button _againButton;

        [Header("战斗日志（规划 §7：默认关闭，M10 从设置里开）")]
        [SerializeField] private GameObject _logPanel;
        [SerializeField] private TMP_Text _logText;
        [SerializeField] private bool _showLog = false;

        // 种子由 BattleDriver 决定并公开（BattleDriver.LastSeed）——
        // 本类不再自己算种子：那会和 driver 的「随机 / 固定」开关形成第二套口径。

        // ── 缓冲（避免每拍分配）──────────────────────────────
        private readonly List<CardSnapshot> _handBuf = new List<CardSnapshot>();
        private readonly List<CardSnapshot> _playerCoolingBuf = new List<CardSnapshot>();
        private readonly List<CardSnapshot> _enemyCoolingBuf = new List<CardSnapshot>();
        private readonly List<CardSnapshot> _playedBuf = new List<CardSnapshot>();

        // ── M35：头顶出牌展示的「在册」名单 ─────────────────────
        //
        //  头顶展示不再是一个定长停留的浮层，而是「这张牌离开了手牌、还没进冷却区」这段空窗期的
        //  唯一显示位。所以必须记账：谁此刻挂在头顶（uid → 座位）。一旦它在快照里出现在冷却区，
        //  就把它从头顶撤下、改由一张飞行卡从头顶滑到那个冷却槽（用户 2026-09-23 口径）。
        //
        //  ⚠ 用 uid 而不是「哪个面板有几张」：同一次双发两张牌是各自独立离场的，
        //    而引擎第 ④ 步把「进攻牌 + 防御牌」一起送进冷却 —— 两侧的牌会同时触发离场。

        /// <summary>此刻挂在头顶的牌（<b>uid → 座位</b>），M35。</summary>
        private readonly Dictionary<int, int> _onHead = new Dictionary<int, int>();

        /// <summary>「该从头顶起飞」的 uid 复用缓冲（<see cref="DetectPlayedDepartures"/> 每趟都用）。</summary>
        private readonly List<int> _headDepartBuf = new List<int>();

        /// <summary>划掉一侧在册名单时的 key 临时缓冲（与上面那个分开，免得两处嵌套调用互相踩）。</summary>
        private readonly List<int> _headScratch = new List<int>();
        private readonly List<Option> _cardOptions = new List<Option>();
        private readonly List<Option> _flatOptions = new List<Option>();
        private readonly HashSet<int> _handPlayable = new HashSet<int>();
        private readonly HashSet<int> _coolingPlayable = new HashSet<int>();
        private readonly List<string> _logBuf = new List<string>();

        // ── M28：怪物手牌情报 ─────────────────────────────────
        /// <summary>按下手牌数标签时，怪物当前手牌的落点缓冲。</summary>
        private readonly List<CardSnapshot> _monsterHandBuf = new List<CardSnapshot>();

        /// <summary>
        /// 玩家已经「知道身份」的怪物手牌 —— 以 <see cref="CardInstance.Uid"/> 为键。
        /// <para>登记来源有三处：① 怪物作为<b>进攻牌</b>打出（<c>AttackDeclaredEvent</c>）；</para>
        /// <para>② 怪物作为<b>防御牌</b>打出（<c>DefenseResolvedEvent</c>）；</para>
        /// <para>③ 雷云 / 狂躁蘑菇把它翻给玩家看过（<c>HandRevealedEvent</c>）。</para>
        /// <para>用 uid 而不是手牌下标：同一张牌回手后再打出、手牌增删导致下标漂移，
        /// 记忆都应当延续。</para>
        /// </summary>
        private readonly HashSet<int> _seenMonsterCards = new HashSet<int>();

        // ── M15 光环图标 ─────────────────────────────────────
        private readonly List<AuraIconData> _playerAuras = new List<AuraIconData>();
        private readonly List<AuraIconData> _enemyAuras = new List<AuraIconData>();

        /// <summary>本拍引擎允许消耗的光环指示物（键 = <see cref="AuraIconData.MakeKey"/>）→ 对应选项。</summary>
        private readonly Dictionary<long, Option> _auraOptions = new Dictionary<long, Option>();

        /// <summary>
        /// 玩家在判定区「准备使用」的光环（键 = <see cref="AuraIconData.MakeKey"/>，按准备先后）。
        ///
        /// <para><b>为什么只存键、不存 Option</b>：每加 / 减一枚，引擎都会按新的准备集合
        /// <b>重发本拍决策</b>（防御牌的合法性取决于这份集合），重发之后 <c>Option</c> 全是新对象、
        /// 序号也变了。键（座位 + 来源牌 + 指示物序号）才是跨重发稳定的身份。</para>
        /// </summary>
        private readonly List<long> _preparedKeys = new List<long>();

        /// <summary>
        /// <b>正在「取消使用」飞回默认位</b>的指示物（键，M34）。
        ///
        /// <para>取消分为两步：① 从 <see cref="_preparedKeys"/> 里删掉（立刻重发本拍决策）；
        /// ② 那一枚标记自己从手牌区左侧滑回默认位（<see cref="PreparedAuraView.FlyHome"/>），
        /// 落地才算完。这两步之间 HudBuff 里那枚原图标<b>必须继续隐着</b> ——
        /// 否则屏幕上是「图标已经回到原位」+「标记还在往那儿飞」两份同时存在。</para>
        ///
        /// <para>所以它是 <see cref="_preparedKeys"/> 的「影子」：两个集合一起进
        /// <see cref="HudBuffView.SetHidden"/>（见 <see cref="HiddenAuraKeys"/>）。</para>
        /// </summary>
        private readonly List<long> _returningKeys = new List<long>();

        /// <summary><see cref="HiddenAuraKeys"/> 的复用缓冲（每拍都会调，别每次都 new 一个 List）。</summary>
        private readonly List<long> _hiddenBuf = new List<long>();

        /// <summary>准备标记的显示数据（给 <see cref="PreparedAuraView"/> 用）。</summary>
        private readonly List<AuraIconData> _preparedIcons = new List<AuraIconData>();

        private bool _pendingValid;

        /// <summary>当前这一拍的决策类型。拖拽落点要靠它判断「这一拖算进攻还是算防御」。</summary>
        private RequestKind _pendingKind;

        /// <summary>
        /// 本拍是不是「敌方双发」的防御决策（<c>DecisionSnapshot.ContextDouble</c>）。
        ///
        /// <para><b>为什么界面必须知道这件事</b>（2026-09-23 修）：双发要求交出<b>两张</b>够挡的牌，
        /// 而引擎给的防御选项是<b>成对的组合</b>（<c>Option.Card</c> + <c>Option.PairCard</c>）。
        /// 原先界面完全不认识 <c>PairCard</c>：只有组合里靠前的那张牌被点亮成「可出」，
        /// 玩家拖它一张就提交了一整对 —— 第二张牌是被<b>替玩家选走</b>的，
        /// 「必须交两张」这条规则在界面上一句都没说，看着就像双发没生效。</para>
        ///
        /// <para>现在的口径：这一拍改成<b>连着拖两张</b> —— 第一张只钉住（见
        /// <see cref="_pairFirstUid"/>），第二张与它凑成一对时才提交。</para>
        /// </summary>
        private bool _pendingDouble;

        /// <summary>
        /// 双发防御里「已经钉住的第一张」的 uid（<c>-1</c> = 还没选）。
        /// 配对与提交都在 <see cref="OnHandCardDropped"/>；形状上的辉光由
        /// <see cref="HandView.SetPinnedUid"/> 表达。
        /// </summary>
        private int _pairFirstUid = -1;

        /// <summary>
        /// 本拍有没有「冷却区里的牌」这类目标（加速 / 减速 / 移除 / 复制 / 重置冷却）。
        ///
        /// <para><b>为什么是个 bool 而不是「哪一方」</b>：加速与减速的目标<b>横跨双方</b>，
        /// 记「哪一方」必然把另一方的迷你卡一起压暗（2026-09-18 之前的实际缺陷）。
        /// 能不能点已经由 <see cref="_coolingPlayable"/> 的 Uid 集合逐张表达，这里只回答
        /// 「要不要进入限制态」。</para>
        /// </summary>
        private bool _coolingTargeted;
        private int _attacksRemaining;

        // ── M26：区域加速 / 减速的「点冷却槽」入口 ─────────────
        //
        //  引擎给区域类的选项是「(某一方, 剩余冷却 = k)」一档一个（`BattleEngine.IssueZoneValue`）。
        //  用户口径：这些档不要列成按钮，改成**直接点屏幕上那 8 格冷却槽**
        //  （每侧 4 格，槽位徽标「N / 冷却区N」就写着这一格的 k）。点哪一格 = 选 (座位, k)。
        //
        //  ⚠ 槽位徽标本身就是行的语义，所以不需要任何文字提示：玩家看得见的
        //    「冷却区2」就是 k = 2。点下去在引擎给的 Options 里找 (该座位, k = 4 − 行)，
        //    找到就提交、没有就什么也不做。
        //  本类不做任何规则判断，只是在引擎给的 Options 里挑出口（铁律 3）。

        /// <summary>本拍是不是「区域类」决策（决定要不要打开冷却区的整片可点态）。</summary>
        private bool _zonePickMode;

        /// <summary>本拍的区域档，按座位分组（下标 = 座位；<c>Seat = -1</c> 的选项单独放 <see cref="_zoneBothOptions"/>）。</summary>
        private readonly Dictionary<int, List<Option>> _zoneBySeat = new Dictionary<int, List<Option>>();

        /// <summary>区域档里座位为「双方」的那些（雪崩）。它们没有「点哪一片」可言。</summary>
        private readonly List<Option> _zoneBothOptions = new List<Option>();

        /// <summary>
        /// 本拍「演出还没播完」的剩余秒数（M20）——由 <see cref="HandleBeat"/> 算出，
        /// 经 <see cref="BattleDriver.BeatHold"/> 交回给 driver 当等待时长。
        ///
        /// <para>每拍开头先归零，只有确实要等的拍才写值。</para>
        ///
        /// <para><b>现在唯一的写入者是查看对方手牌的浮层</b>（<see cref="HandlePeekReveal"/> →
        /// <c>PeekBeatHoldSeconds</c>）：它要停够时间让玩家看清翻开的牌。</para>
        ///
        /// <para>⚠ <b>M35 起头顶出牌展示不再走这条闸门</b>：它已经是「常驻到进冷却区」的，
        /// 没有固定时长可报，也不该拿闸门把下一拍压住（压住反而会让「进攻牌与防御牌同时离场」
        /// 这件事被拆成两拍）。所以本类不再为 <c>PlayedCard</c> 写这个值。</para>
        /// </summary>
        private float _pendingHold;

        /// <summary>
        /// 本拍「谁在执行效果」（M36）。
        ///
        /// <para><b>为什么界面要自己追这个</b>：③ 阶段那一串 <c>CooldownChangedEvent</c> 只说
        /// 「哪张牌的剩余冷却变了」—— <c>Change.Card.OwnerSeat</c> 是<b>被改的那张牌</b>的归属，
        /// 而加速 / 减速的目标横跨双方（加速可以加自己的、减速是减对方的）。
        /// 所以「是谁在加速 / 减速」只能由「这一半场是谁的」推出来：它由
        /// <c>TurnStartedEvent.Seat</c> 与 <c>AttackDeclaredEvent.Seat</c> 给出，
        /// 而 ③ 阶段永远发生在进攻方的半场里。</para>
        /// </summary>
        private int _actorSeat;

        /// <summary>
        /// 这一批「区域类冷却改动」是否已经报过一次（M36）。
        ///
        /// <para>一次区域减速会**连着发好几条** <c>CooldownChangedEvent</c>（每张牌一条），
        /// 而每条节拍只有 0.06 s —— 逐条都写提示条、都重新起播中央横幅的话，
        /// 屏幕正中会闪成一片，玩家一个字也读不到。所以「区域」这一批只在第一条上发一次声
        /// （逐张的「加速 / 减速一张牌」不在此列，那种本来就该一张一条）。</para>
        /// </summary>
        private bool _effectBatchShown;

        /// <summary>准备标记条是否已经接过线（<see cref="EnsurePreparedBar"/> 幂等用）。</summary>
        private bool _preparedWired;

        // ══════════════════════════════════════════════════════
        //  生命周期
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_driver == null)
            {
                _driver = GetComponent<BattleDriver>();
            }

            // 演出闸门：本类认识「头顶卡面还要停多久」，driver 只负责等（M20）
            if (_driver != null)
            {
                _driver.BeatHold = BeatHoldSeconds;
            }

            if (_againButton != null)
            {
                _againButton.onClick.AddListener(OnAgainClicked);
            }

            if (_hand != null)
            {
                _hand.CardClicked += OnHandCardClicked;
                _hand.CardDropped += OnHandCardDropped;
            }

            if (_cooldown != null)
            {
                _cooldown.CardClicked += OnCoolingCardClicked;
                _cooldown.ZoneRowClicked += OnCooldownZoneRowClicked;
            }

            if (_picker != null)
            {
                _picker.OptionPicked += OnOptionPicked;
            }

            if (_deal != null)
            {
                _deal.Confirmed += OnDealConfirmed;
                _deal.Skipped += OnDealSkipped;
            }

            // M25：浮层里点一张牌背 → 把那一项回填给引擎
            if (_peek != null)
            {
                _peek.CardPicked += OnPeekCardPicked;
            }

            // M27：选牌弹窗点「确认」→ 把选中的若干项一次性回填给引擎
            if (_handPick != null)
            {
                _handPick.Confirmed += OnHandPickConfirmed;
            }

            // M28：按住怪物手牌数标签 = 查看它的手牌；松手即收。
            // 手势组件挂在 ArtLayer/HandCount_Monster 上（BattleArtLayerBuilder 建节点、
            // BattleUiBuilder 挂组件）；这里没接上就去捞一把，省得漏配后按了没反应。
            if (_monsterHandHold == null)
            {
                if (_monster != null && _monster.HandCountRoot != null)
                {
                    _monsterHandHold = _monster.HandCountRoot.GetComponent<PressHoldButton>();
                }
            }
            if (_monsterHandHold != null)
            {
                _monsterHandHold.Pressed += OnMonsterHandPressed;
                _monsterHandHold.Released += OnMonsterHandReleased;
            }

            // M15：光环图标的手势 —— 拖动期间点亮判定区落区，松手时判落点
            if (_hudBuff != null)
            {
                _hudBuff.IconDragBegin += OnAuraDragMove;
                _hudBuff.IconDragMove += OnAuraDragMove;
                _hudBuff.IconDropped += OnAuraDropped;
                _hudBuff.GestureEnded += OnAuraGestureEnded;
            }

            if (_resultRoot != null)
            {
                _resultRoot.SetActive(false);
            }

            if (_logPanel != null)
            {
                _logPanel.SetActive(_showLog);
            }

            if (_transit != null)
            {
                _transit.Configure(_hand, _cooldown);
            }

            BindSeats();
            WireArtLayer();

            // M13：两侧角色交给手牌区，只做「进了判定区 → 点亮目标」的高亮反馈；
            // 出牌与否由判定区（PlayZone）决定，不再拿角色矩形做命中判定
            //（角色节点每帧尺寸都在变，那套判定时灵时不灵）。
            if (_hand != null)
            {
                _hand.SetDropTargets(_hero, _monster);
            }

            EnsurePreparedBar();
        }

        /// <summary>
        /// 把图标模板接给预建的「已准备光环」标记行（幂等）。
        ///
        /// <para><b>节点是 M8 构建器预建的</b>（<c>Canvas/PreparedAura</c>）—— 它要能在
        /// Hierarchy 里被看见、被调，所以不在代码里 new。这里只剩一处<b>引用</b>：
        /// 图标模板来自 <see cref="HudBuffView.IconTemplate"/>，不是本类可以预写进 Prefab 的东西。</para>
        ///
        /// <para><b>M21 起不再需要判定区</b>：取消改成「再拖一下那枚标记」，
        /// 标记与出牌判定区再也没有关系（<see cref="PreparedAuraView"/> 里那层
        /// <c>ZoneHighlight</c> 也已删除）。</para>
        /// </summary>
        private void EnsurePreparedBar()
        {
            if (_preparedWired || _preparedBar == null || _hand == null || _hudBuff == null)
            {
                return;
            }

            _preparedWired = true;

            _preparedBar.Configure(_hudBuff.IconTemplate);
            _preparedBar.Cancelled += OnPreparedAuraCancelled;
            // M34：取消的标记「飞回默认位」落地之后，才把 HudBuff 里那枚原图标放出来。
            _preparedBar.ReturnLanded += OnPreparedAuraReturnLanded;
        }

        // ══════════════════════════════════════════════════════
        //  M11 美术层
        // ══════════════════════════════════════════════════════

        private void WireArtLayer()
        {
            // 齿轮只打开设置；重开与测试卡池入口由 SettingsView 管理。
            if (_hud != null)
            {
                _hud.GearClicked += OnSettingsClicked;
            }

            BattleArtLibrary art = BattleArtLibrary.Instance;
            if (art == null)
            {
                Debug.LogWarning("[BattleUi] 缺 Assets/Resources/BattleArtLibrary.asset —— "
                                 + "跑菜单 `魔法乱斗/整理 · 配置新美术导入` 生成。舞台会留空。");
                return;
            }

            BindCharacter(_hero, art.Hero);
            BindCharacter(_monster, art.Monster);
        }

        private static void BindCharacter(CharacterView view, BattleArtLibrary.CharacterSet set)
        {
            if (view == null)
            {
                return;
            }

            view.SetScale(UiLayout.CharScale);
            // 这两个只是**兜底**：素材自带帧率（由 ArtImportBuilder 按动作写进 Clip.Fps）
            // 优先，只有映射表没配时才用它们。
            view.SetFps(UiLayout.CharIdleFps, UiLayout.CharAttackFps);
            view.Bind(set);
        }

        /// <summary>战斗日志面板开关（齿轮按钮 / M10 设置面板都走这里）。</summary>
        public void SetLogVisible(bool visible)
        {
            _showLog = visible;

            if (_logPanel != null)
            {
                _logPanel.SetActive(_showLog);
            }

            if (_showLog)
            {
                RefreshLog();
            }
        }

        public bool ShowLog
        {
            get { return _showLog; }
        }

        private void ToggleLog()
        {
            SetLogVisible(!_showLog);
        }

        private void OnEnable()
        {
            if (_driver == null)
            {
                return;
            }

            _driver.OnStarted += HandleStarted;
            _driver.OnBeat += HandleBeat;
            _driver.OnStateChanged += RefreshAll;
            _driver.OnPlayerDecision += HandleDecision;
            _driver.OnFinished += HandleFinished;
        }

        private void OnDisable()
        {
            if (_driver == null)
            {
                return;
            }

            _driver.OnStarted -= HandleStarted;
            _driver.OnBeat -= HandleBeat;
            _driver.OnStateChanged -= RefreshAll;
            _driver.OnPlayerDecision -= HandleDecision;
            _driver.OnFinished -= HandleFinished;
        }

        // ══════════════════════════════════════════════════════
        //  驱动事件
        // ══════════════════════════════════════════════════════

        private void HandleStarted()
        {
            BindSeats();
            _actorSeat = LocalSeat;
            _attacksRemaining = 0;
            if (_hand != null) _hand.ResetOrder();
            if (_transit != null) _transit.ResetPresentation();
            ClearDecision();

            if (_resultRoot != null)
            {
                _resultRoot.SetActive(false);
            }

            // M35：新一局开始 → 两侧头顶的牌（以及在册名单）一起清掉
            HideHeadAll();

            // M36：上一局还在飞的「AI 用光环」演出图标、以及没读完的中央浮字一起收掉 ——
            // 重开一局必须是一瞬间的事。
            if (_hudBuff != null)
            {
                _hudBuff.ClearCasts();
            }

            if (_floatTip != null)
            {
                _floatTip.Hide();
            }

            // M25：上一局没收干净的查看浮层。正常路径上它自己会淡出（1.4 s 内），
            // 但「重开一局」必须是一瞬间的事 —— 不能留着半透明的遮罩挡着新一局的手牌
            // （遮罩期间 blocksRaycasts 是开着的，玩家点不动）。
            if (_peek != null)
            {
                _peek.HideImmediate();
            }

            // M27：同上 —— 选牌弹窗的遮罩也要在一瞬间收掉（blocksRaycasts 开着会挡住新一局）
            if (_handPick != null)
            {
                _handPick.HideImmediate();
            }

            // M28：新一局 = 情报清零。上一局看过 / 打过哪些牌跟这一局毫无关系，
            // 而且 uid 会重新从头分配 —— 留着旧集合只会误判成「已知」。
            _seenMonsterCards.Clear();
            _monsterHandBuf.Clear();
            if (_monsterHand != null)
            {
                _monsterHand.HideImmediate();
            }

            if (_stage != null)
            {
                // 中央的 StageMark 在 M8 里停用了（它占的正是浮层要用的中央净空），
                // 所有叙事统一走顶部提示条。
                _stage.SetMark(string.Empty);
                _stage.SetPrompt("发牌中…");
            }

            // 上一局倒下的那个要站起来（M14）—— Revive 只是清掉「已倒下」的闸，
            // 还得显式叫一次待机，否则新一局开场双方都躺在地上。
            if (_hero != null)
            {
                _hero.Revive();
                _hero.PlayIdle();
            }

            if (_monster != null)
            {
                _monster.Revive();
                _monster.PlayIdle();
            }

            RefreshAll();
        }

        /// <summary>
        /// 把事件流翻译成角色的插播动作（M11 / M14）+ 双方头顶的出牌展示（M13 / M20 / M35）。
        /// 四个触发点，都取「谁在做这件事」：
        ///
        /// <list type="bullet">
        /// <item><see cref="AttackDeclaredEvent"/> —— 打出进攻牌的那一方出招，那张牌上**进攻方自己**的头顶</item>
        /// <item><see cref="DefenseResolvedEvent"/> —— 防御方摆架势，交出的牌上**防御方自己**的头顶</item>
        /// <item><see cref="DamageTakenEvent"/> —— 挨打方受击（M14 新增）</item>
        /// <item><see cref="GameOverEvent"/> —— 败方倒地，**播完停住**（M14 新增）</item>
        /// </list>
        ///
        /// <para><b>M35 起「谁打的就挂在谁头顶」（2026-09-23 用户口径）</b>：旧口径是
        /// 「对手头顶的出牌展示只给 AI 座位」，理由是玩家自己点出去的牌当然知道是什么。
        /// 用户实测后要求改成两边一致 —— 玩家也需要在头顶确认「我这一刀打出去的是什么」，
        /// 尤其是光环加值 / 双发这种数值与手牌上不一样的时刻。所以
        /// <see cref="ShowOnHead"/> 对两个座位一视同仁。</para>
        ///
        /// <para><b>消失时机（M35 改写，同一条用户口径）</b>：不再是「固定停 N 秒再淡出」，
        /// 而是 <b>常驻到这张牌进入冷却区为止</b> —— 那一刻它改由一张飞行卡从头顶滑到冷却槽
        /// （见 <see cref="DetectPlayedDepartures"/>）。对双方而言，一方的进攻牌与另一方的防御牌
        /// 是<b>同一刻</b>进入冷却区的（引擎第 ④ 步把两者一起送进去），所以也是同一刻离场。</para>
        /// </summary>
        private void PlayCharacterBeats(BattleEvent e)
        {
            if (e is AttackDeclaredEvent)
            {
                var ad = (AttackDeclaredEvent)e;
                CharacterView actor = CharacterFor(ad.Seat);
                if (actor != null)
                {
                    actor.PlayPose(CharacterPose.Attack);
                }

                // M35：进攻牌挂到**进攻方自己**的头顶（玩家侧也挂 —— 用户 2026-09-23 口径
                // 「玩家进攻/防御打出来的卡也应当显示在头顶，规则与怪物一致」）。
                // 它一直常驻到这张牌进入冷却区为止，那才是收牌时机（见 DetectPlayedDepartures）。
                if (ad.Card != null)
                {
                    if (ad.Seat == OpponentSeat)
                    {
                        // M28：怪物亮出来的进攻牌 → 玩家从此知道这张牌的身份
                        MarkMonsterCardSeen(ad.Card);
                    }

                    _playedBuf.Clear();
                    _playedBuf.Add(CardSnapshot.From(ad.Card));
                    ShowOnHead(ad.Seat, _playedBuf);
                }
            }
            else if (e is DefenseResolvedEvent)
            {
                var dr = (DefenseResolvedEvent)e;
                CharacterView actor = CharacterFor(dr.DefenderSeat);
                if (actor != null)
                {
                    actor.PlayPose(CharacterPose.Defend);
                }

                // M35：防御牌同样先挂到**防御方自己**的头顶 —— 成功、双发、甚至交了牌仍没挡住
                // 都算（只有「放弃 / 手上没牌可挡」才没有牌，那种情况自然什么都不显示）。
                //
                // ⚠ 它离场的时机不由这里决定：引擎第 ④ 步会把「本次进攻牌」与「本次防御牌」
                //   一起送进冷却区，那一拍就是**两边同时**从头顶飞向冷却槽的时刻
                //   （用户口径：「一方的进攻卡进入冷却区时，就是另外一方的防御卡进入冷却区域的时机」）。
                //   所以这里也给 hold = 0，绝不写死停留时长。
                _playedBuf.Clear();
                for (int i = 0; i < dr.Cards.Count; i++)
                {
                    if (dr.Cards[i] == null)
                    {
                        continue;
                    }

                    if (dr.DefenderSeat == OpponentSeat)
                    {
                        // M28：怪物打出的防御牌同样归入「玩家已知」
                        MarkMonsterCardSeen(dr.Cards[i]);
                    }

                    _playedBuf.Add(CardSnapshot.From(dr.Cards[i]));
                }

                if (_playedBuf.Count > 0)
                {
                    ShowOnHead(dr.DefenderSeat, _playedBuf);
                }
            }
            else if (e is DamageTakenEvent)
            {
                // 只有**真的掉血**才播受击 —— 挡下来的防御不发这个事件，
                // 拿它当「挨打」的触发点，正好等于「扣了 1 点生命」。
                var dt = (DamageTakenEvent)e;
                CharacterView victim = CharacterFor(dt.Seat);
                if (victim != null)
                {
                    victim.PlayPose(CharacterPose.BeHit);
                }

                // ⚠ M35 去掉了这里原来那句「玩家掉血 → 收掉怪的进攻牌」的兜底：
                //   掉血发生在引擎第 ③ 步（防御结算里），而进攻牌要等第 ④ 步才进冷却区 ——
                //   拿掉血当收牌信号会让那张牌在进冷却区之前就先消失，随后冷却槽里凭空冒出来一张。
                //   现在唯一的收牌判据是「它进冷却区了没有」（DetectPlayedDepartures）。
            }
            else if (e is GameOverEvent)
            {
                // ⚠ 败方必须用 PlayPoseHold：普通插播播完会回调待机，
                //   而结算浮层还要在屏幕上停好几秒 —— 那几秒里尸体会自己站起来。
                var go = (GameOverEvent)e;
                int loserSeat = go.WinnerSeat == LocalSeat
                    ? OpponentSeat
                    : LocalSeat;
                CharacterView loser = CharacterFor(loserSeat);
                if (loser != null)
                {
                    loser.PlayPoseHold(CharacterPose.Death);
                }

                // M35：终局了，两边头顶的牌都收掉（对局已结束，没有「进冷却区」这回事了）
                HideHeadAll();
            }
        }

        /// <summary>
        /// 演出闸门：本拍之后 driver 还要等多少秒（见 <see cref="BattleDriver.BeatHold"/>）。
        ///
        /// <para>同一次 <c>OnBeat</c> 里 <see cref="HandleBeat"/> 先把 <see cref="_pendingHold"/> 算好，
        /// 本方法紧接着被 driver 调用 —— 顺序由 <c>CoPlayBeats</c> 保证。</para>
        /// </summary>
        private float BeatHoldSeconds(BattleEvent e)
        {
            return _pendingHold;
        }

        /// <summary>座位 → 舞台上的角色（玩家侧是主角，AI 侧是怪物）。</summary>
        private CharacterView CharacterFor(int seat)
        {
            return seat == LocalSeat ? _hero : _monster;
        }

        /// <summary>座位 → 该侧头顶的「刚打出的牌」展示面板（M35：双方各一块）。</summary>
        private PlayedCardView PlayedFor(int seat)
        {
            return seat == LocalSeat ? _playedHero : _playedFoe;
        }

        /// <summary>
        /// 把一批刚打出的牌挂到某一侧的头顶（M35）。
        ///
        /// <para>一律 <c>hold = 0</c>（常驻）—— <b>收牌的时机不在这里决定</b>，而由
        /// <see cref="DetectPlayedDepartures"/> 按「它进冷却区了没有」来判。
        /// 这替掉了旧口径里「怪物防御牌固定停 2 秒再淡出」那种写死的时长。</para>
        ///
        /// <para>同侧之前挂着的那批<b>直接作废</b>（从在册名单里划掉）：面板只有两个卡位，
        /// 新一批会整体覆盖它。被覆盖的那张牌若随后进冷却区，就没有「从头顶起飞」可言 ——
        /// 那种情况下它会直接出现在冷却槽里（连击在一回合内连打两张时会遇到，属可接受的退化）。</para>
        /// </summary>
        /// <para><b>M36 起怪物那一侧带入场动画</b>（用户口径：「加入 AI 的出牌动画
        /// （从其模型，向上移动，并逐渐出现）」）：那张牌从<b>怪物模型当前帧的中心</b>
        /// 升起并渐渐显形，落到头顶卡位。玩家自己那一侧不做 —— 他的牌是从手牌区拖上去的，
        /// 起点另有其物，而且他自己知道打了什么。</para>
        /// </summary>
        private void ShowOnHead(int seat, IReadOnlyList<CardSnapshot> cards)
        {
            PlayedCardView view = PlayedFor(seat);
            if (view == null || cards == null || cards.Count == 0)
            {
                return;
            }

            DiscardHeadFor(seat);

            if (seat == OpponentSeat)
            {
                view.ShowFrom(cards, 0f, ModelCenterWorld(seat));
            }
            else
            {
                view.Show(cards, 0f);
            }

            for (int i = 0; i < cards.Count; i++)
            {
                _onHead[cards[i].Uid] = seat;
            }
        }

        /// <summary>
        /// 某一侧角色模型当前帧的**中心**世界坐标（M36）。
        ///
        /// <para>⚠ 不能用 <c>anchoredPosition</c> 去推：角色的 <c>RectTransform</c> 每换一帧动作
        /// 就重设 <c>pivot</c> 与 <c>sizeDelta</c>（见 <see cref="CharacterView"/> 的说明），
        /// 只有 <c>rect.center</c> 才是「这块贴图此刻的中心」——它已经含了 pivot 偏移。
        /// 也不要用「脚底 + 固定高度」：不同动作帧的包围盒不一样，写死高度会随动作飘。</para>
        /// </summary>
        private Vector3 ModelCenterWorld(int seat)
        {
            CharacterView view = CharacterFor(seat);
            RectTransform rt = view == null ? null : view.transform as RectTransform;
            if (rt == null)
            {
                return Vector3.zero;
            }

            return rt.TransformPoint(rt.rect.center);
        }

        /// <summary>把某一侧头顶的「在册名单」整体划掉（该侧面板被新一批覆盖时用）。</summary>
        private void DiscardHeadFor(int seat)
        {
            if (_onHead.Count == 0)
            {
                return;
            }

            _headScratch.Clear();
            foreach (KeyValuePair<int, int> pair in _onHead)
            {
                if (pair.Value == seat)
                {
                    _headScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < _headScratch.Count; i++)
            {
                _onHead.Remove(_headScratch[i]);
            }
        }

        /// <summary>两侧头顶一起收掉（重开一局 / 终局 / 队列断开）。</summary>
        private void HideHeadAll()
        {
            _onHead.Clear();

            if (_playedHero != null)
            {
                _playedHero.Hide();
            }

            if (_playedFoe != null)
            {
                _playedFoe.Hide();
            }
        }

        /// <summary>
        /// 头顶的牌「该飞走了」的检测（M35）—— <b>收牌时机的唯一判据</b>。
        ///
        /// <para><b>为什么拿「快照里出现了没有」当判据</b>：引擎第 ④ 步
        /// （<c>BattleEngine.DoStage4</c>）把「本次进攻牌」与「本次防御牌」放进同一个缓冲、
        /// 再一起 <c>FlushCooldown</c>。所以「这张牌出现在冷却区快照里」这件事本身，
        /// 就已经精确表达了用户要的时序 —— 不必再监听某几个事件，也不必写死等待时长。</para>
        ///
        /// <list type="bullet">
        /// <item><b>玩家进攻 + 怪物防御</b>：两张牌同在第 ④ 步进冷却 → <b>同一次刷新</b>里被检测到
        /// → 两张飞行卡从各自头顶同时起飞。</item>
        /// <item><b>怪物进攻 + 玩家防御</b>：同理。</item>
        /// <item><b>放弃防御 / 免疫光环</b>：只有进攻牌进冷却 → 只有它从头顶飞走。</item>
        /// </list>
        ///
        /// <para>⚠ 必须在 <see cref="RefreshAll"/> 里 <c>CollectCooling</c> <b>之后</b>、
        /// <c>CardTransitView.AnimateAfterBind</c> <b>之前</b>调用 —— 前者给它数据，
        /// 后者消费它登记下来的飞行起点。</para>
        /// </summary>
        private void DetectPlayedDepartures()
        {
            if (_onHead.Count == 0)
            {
                return;
            }

            DepartFrom(_playerCoolingBuf, LocalSeat);
            DepartFrom(_enemyCoolingBuf, OpponentSeat);
        }

        /// <summary>
        /// 某一侧的冷却区里有没有「还挂在头顶」的牌 —— 有就让它们起飞并撤下
        /// （见 <see cref="DetectPlayedDepartures"/>）。
        /// </summary>
        private void DepartFrom(IReadOnlyList<CardSnapshot> cooling, int seat)
        {
            _headDepartBuf.Clear();

            for (int i = 0; i < cooling.Count; i++)
            {
                int uid = cooling[i].Uid;
                int owner;
                if (!_onHead.TryGetValue(uid, out owner) || owner != seat)
                {
                    continue;
                }

                // 顺序要紧：**先登记飞行起点、再撤下头顶那张**。
                // 撤下会把卡位失活，之后就再也读不到那块矩形了（起点会退化成零尺寸）。
                RectTransform rect;
                PlayedCardView view = PlayedFor(seat);
                if (_transit != null && view != null && view.TryGetFaceRect(uid, out rect))
                {
                    _transit.PrimePlayedOrigin(uid, rect);
                }

                _onHead.Remove(uid);
                _headDepartBuf.Add(uid);
            }

            if (_headDepartBuf.Count == 0)
            {
                return;
            }

            PlayedCardView panel = PlayedFor(seat);
            if (panel != null)
            {
                panel.HideFaces(_headDepartBuf);
            }
        }

        /// <summary>
        /// 逐拍演出。文案统一写顶部提示条 —— 它是唯一一处「不跟任何浮层抢位置」的显示位。
        /// </summary>
        private void HandleBeat(BattleEvent e)
        {
            // 每拍先清掉上一拍留下的等待（只有确实要等的拍才会重写它）
            _pendingHold = 0f;

            // M36：本拍「谁在做这件事」。③ 阶段那一串 CooldownChangedEvent 只说「哪张牌的冷却变了」，
            // 而 Change.Card 可能是**对方**的牌（减速就是减对方的），所以「是谁在加速 / 减速」
            // 只能由「这一半场是谁的」推出来 —— 它由回合开始 / 打出进攻牌两个事件给出。
            if (e is TurnStartedEvent)
            {
                _actorSeat = ((TurnStartedEvent)e).Seat;
            }
            else if (e is AttackDeclaredEvent)
            {
                _actorSeat = ((AttackDeclaredEvent)e).Seat;
            }

            // 「一批区域改动」的边界：遇上别的节拍就重开（见 _effectBatchShown 的说明）
            if (!(e is CooldownChangedEvent))
            {
                _effectBatchShown = false;
            }

            if (e is TurnStartedEvent)
                _attacksRemaining = ((TurnStartedEvent)e).Seat == LocalSeat ? 1 : 0;
            else if (e is AttackDeclaredEvent && ((AttackDeclaredEvent)e).Seat == LocalSeat)
                _attacksRemaining = 0;
            else if (e is GameOverEvent) _attacksRemaining = 0;
            if (_hud != null) _hud.SetAttacksRemaining(_attacksRemaining);
            // 演出先跑 —— 角色的插播动作不该因为「提示条没接上」就一起失效
            PlayCharacterBeats(e);
            PlayActionBanner(e);

            // M36：怪物用掉一枚光环 → 让那一枚**飞到怪物模型右侧**（用户口径：AI 使用光环时
            // 也要有一个光环移动的过程来帮玩家识别）。
            // ⚠ 必须在这一拍里做：光环数据（_enemyAuras）此刻还是**消耗前**的样子，
            //   那一枚图标还在敌方光环行里 —— 起点就从它身上取；下一拍刷新就再也找不到它了。
            if (e is AuraConsumedEvent)
            {
                PlayAiAuraCast((AuraConsumedEvent)e);
            }

            // M28：雷云 / 狂躁蘑菇把怪物的某张牌翻给玩家看过 → 这张牌从此归入「已知」。
            // ⚠ 必须在 HandlePeekReveal 之前登记：那个方法在浮层没显示时直接 return
            //   （AI 侧触发翻牌时浮层本来就没开），情报不能跟着一起丢。
            if (e is HandRevealedEvent)
            {
                var revealed = (HandRevealedEvent)e;
                if (revealed.OwnerSeat == OpponentSeat)
                {
                    MarkMonsterCardSeen(revealed.Card);
                }
            }

            // M25：玩家查看对方手牌 —— 翻面的后半段（正面卡面 + 结果角标）由这一事件驱动。
            // ⚠ 特意放在下面 _stage 的空判**之前**：这一步不只是演出，它同时把
            //   「结果停留 + 淡出」报给 driver 当等待时长（见方法注释），
            //   不能因为顶部提示条没接上就一起失效。
            HandlePeekReveal(e);

            if (_stage == null)
            {
                return;
            }

            if (e is TurnStartedEvent)
            {
                var ts = (TurnStartedEvent)e;
                _stage.SetPrompt(SeatName(ts.Seat) + (ts.IsComboFollowUp ? " 连击追加进攻" : " 进攻回合"));
            }
            else if (e is AttackDeclaredEvent)
            {
                var ad = (AttackDeclaredEvent)e;
                _stage.SetPrompt(SeatName(ad.Seat) + " 打出「" + ad.Card.Def.Name + "」");
            }
            else if (e is AttackPowerResolvedEvent)
            {
                var ap = (AttackPowerResolvedEvent)e;
                _stage.SetPrompt("进攻力量 " + ap.FinalPower
                                 + (ap.IsDouble ? "（双发）" : string.Empty)
                                 + " · 需要 ≥" + ap.FinalPower + " 才能挡住");
            }
            else if (e is AuraConsumedEvent)
            {
                var au = (AuraConsumedEvent)e;
                string line = SeatName(au.Seat) + " 消耗光环：「" + au.Source.Def.Name + "」"
                              + AuraResolver.DescribeEffect(au.Kind, au.Value);
                _stage.SetPrompt(line);
                PlayEffectBanner(line);
            }
            else if (e is AuraActivatedEvent)
            {
                // M36：光环激活以前在提示条上**完全无声** —— 玩家只看得到冷却区里那张牌
                // 多了一枚图标，不知道它是「刚拿到的」。怪物的牌更是毫无交代。
                var aa = (AuraActivatedEvent)e;
                string line = SeatName(aa.Seat) + " 的「" + aa.Card.Def.Name + "」光环激活 ×" + aa.Tokens;
                _stage.SetPrompt(line);
                PlayEffectBanner(line);
            }
            else if (e is CooldownChangedEvent)
            {
                // M36：冷却改动以前在提示条上**完全无声**（加速 / 减速 / 区域加减速 / 重置 /
                // 立即冷却完成全都只有日志）。用户口径：怪物执行这些效果时要给更多提示。
                // 逐条翻译成人话；「区域」那一批只报第一条（见 _effectBatchShown）。
                var cc = (CooldownChangedEvent)e;
                string line = CooldownChangeLine(cc.Change);
                if (line != null)
                {
                    bool zone = IsZoneReason(cc.Change.Reason);
                    if (!zone || !_effectBatchShown)
                    {
                        _effectBatchShown = true;
                        _stage.SetPrompt(line);
                        PlayEffectBanner(line);
                    }
                }
            }
            else if (e is DefenseResolvedEvent)
            {
                var dr = (DefenseResolvedEvent)e;
                _stage.SetPrompt(dr.Success
                    ? SeatName(dr.DefenderSeat) + (dr.UsedImmune ? " 用免疫光环挡住了" : " 挡住了")
                    : SeatName(dr.DefenderSeat) + (dr.GaveUp ? " 放弃防御" : " 无法防御") + " → 掉 1 点生命");
            }
            else if (e is DamageTakenEvent)
            {
                var dt = (DamageTakenEvent)e;
                _stage.SetPrompt(SeatName(dt.Seat) + " 生命 −" + dt.Amount + "（" + dt.Source + "）");
                // M20：这里原来还有一发「整个屏幕闪红」，已按用户口径删掉 ——
                // 掉血的反馈是挨打方自己的受击动画（见 PlayCharacterBeats），
                // 全屏压红跟「谁掉的血」无关，只会盖住角色动作。
            }
            else if (e is HandRevealedEvent)
            {
                var hr = (HandRevealedEvent)e;
                _stage.SetPrompt(SeatName(hr.ViewerSeat) + " 查看了对方的手牌"
                                 + (hr.Cooled ? "，送入冷却" : string.Empty));
            }
            else if (e is CardRemovedEvent)
            {
                var cr = (CardRemovedEvent)e;
                _stage.SetPrompt("「" + cr.Card.Def.Name + "」被永久移出游戏");
            }
            else if (e is GameOverEvent)
            {
                _stage.SetPrompt(string.Empty);
            }

            if (_showLog)
            {
                RefreshLog();
            }
        }

        /// <summary>
        /// M25：把引擎翻出来的那张牌接到浮层的翻面动画上。
        ///
        /// <para><b>为什么翻面要分两段</b>：玩家点下牌背的那一瞬就起播前半段
        /// （原面 → 侧薄），但正面卡面只有引擎的 <c>HandRevealedEvent</c> 才拿得到 ——
        /// 等引擎回话（约下一拍）才开始动，手感上就是「点了没反应」。
        /// 所以前半段跟手起播，后半段（侧薄 → 正面 + 结果角标）由本方法接上；
        /// 两段的接缝由 <see cref="PeekView"/> 内部的状态位兜住，点得快或回得快都不会截断。</para>
        ///
        /// <para><b>只有玩家自己查看时浮层才开着</b>：AI 打出雷云 / 狂躁蘑菇时同一事件也会来，
        /// 但那一拍走的是 AI 决策、根本没起过浮层，<see cref="PeekView.IsShowing"/> 是 false，
        /// 这里直接返回（AI 不需要看自己的翻牌动画）。</para>
        ///
        /// <para><b>顺带占住 driver</b>：把「结果停留 + 淡出」整段报给
        /// <see cref="BattleDriver.BeatHold"/>。这一拍演不完就不能进下一拍 ——
        /// 否则下一拍的选项会在这层遮罩还压着界面时亮起来，玩家看得见却点不动。</para>
        /// </summary>
        private void HandlePeekReveal(BattleEvent e)
        {
            var hr = e as HandRevealedEvent;
            if (hr == null || _peek == null || !_peek.IsShowing)
            {
                return;
            }

            // SlotIndex = 被翻的这张在拥有者手牌里的下标，供浮层确认「翻的正是玩家点的那张」
            _peek.Reveal(CardSnapshot.From(hr.Card), hr.Cooled, hr.SlotIndex);
            _pendingHold = Mathf.Max(_pendingHold, UiLayout.PeekBeatHoldSeconds);
        }

        /// <summary>
        /// 浮层里点了一张牌背：把那一项回填给引擎。
        ///
        /// <para>翻面已经在点击的同一帧起播（<see cref="PeekView"/> 内部），不等引擎回话。</para>
        ///
        /// <para><b>这里故意不调 <see cref="ClearDecision"/></b>：它会把 <c>_pendingValid</c>
        /// 置 false、并把各处高亮收掉，而这一次的浮层必须继续开着等引擎回话翻牌。
        /// 该收东西浮层自己会收（<c>Hide</c> → 淡出 → <c>SetActive(false)</c>），
        /// 下一拍 <see cref="HandleDecision"/> 会照常把本拍的状态整个重建一遍。</para>
        /// </summary>
        private void OnPeekCardPicked(int optionIndex)
        {
            if (!_pendingValid || _driver == null || optionIndex < 0)
            {
                return;
            }

            _driver.SubmitPlayerDecision(new[] { optionIndex });
        }

        // ══════════════════════════════════════════════════════
        //  M28 按住手牌数 → 查看怪物手牌
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 按下怪物手牌数标签：捞一份怪物当前手牌，连同「哪些是玩家已知的」一起交给浮层。
        ///
        /// <para>已知判定（用户口径）：<b>打出过的 + 看过的</b>。两份情报的来源
        /// 分别登记在 <see cref="PlayCharacterBeats"/>（打出）与 <see cref="HandleBeat"/>
        /// 的 <c>HandRevealedEvent</c> 支（雷云 / 狂躁蘑菇翻开看过），
        /// 这里只负责读 <see cref="_seenMonsterCards"/>，不重复判断规则（铁律 3）。</para>
        ///
        /// <para>手牌为空的边界由 <see cref="MonsterHandView.Bind"/> 自己收掉（直接不显示），
        /// 免得在标签上按出一块空面板。</para>
        /// </summary>
        private void OnMonsterHandPressed()
        {
            if (_monsterHand == null || _driver == null)
            {
                return;
            }

            _driver.CollectHand(OpponentSeat, _monsterHandBuf);
            _monsterHand.Bind(_monsterHandBuf, _seenMonsterCards);
        }

        /// <summary>
        /// 松手：立刻收起浮层。
        ///
        /// <para>走 <see cref="MonsterHandView.Hide"/>（淡出）而不是 <c>HideImmediate</c> ——
        /// 按住型面板一松手就消失本身已经很快，再硬切会显得闪。
        /// 这里<b>不</b>动 <c>_pendingHold</c>：浮层是玩家自己按出来的，
        /// 不是引擎给的一拍，不该阻塞驱动（松手后玩家该能马上继续出牌）。</para>
        /// </summary>
        private void OnMonsterHandReleased()
        {
            if (_monsterHand != null)
            {
                _monsterHand.Hide();
            }
        }

        /// <summary>
        /// 把一张怪物手牌记为「玩家已知」（幂等）。
        /// 只登记 uid，不存引用 —— 牌回手、换牌都不影响。
        /// </summary>
        private void MarkMonsterCardSeen(CardInstance card)
        {
            if (card != null)
            {
                _seenMonsterCards.Add(card.Uid);
            }
        }

        /// <summary>
        /// 屏幕中央的行动提示（2026-09-19）。
        ///
        /// <para>只挑「一眼必须看到」的几个时刻：轮到谁进攻、玩家这一拍打完了、防御的结果。
        /// 其余细节仍然只走顶部提示条 —— 中央横幅一局闪十几次就成了噪音，
        /// 而它的价值恰恰在「不用盯提示条也知道现在该谁」。</para>
        ///
        /// <para>文案是表现层的措辞，不是规则判断（铁律 3）：谁进攻、成没成，都是引擎事件里的事实。</para>
        /// </summary>
        private void PlayActionBanner(BattleEvent e)
        {
            if (_banner == null)
            {
                return;
            }

            if (e is TurnStartedEvent)
            {
                var ts = (TurnStartedEvent)e;
                if (ts.Seat == LocalSeat)
                {
                    _banner.Show(ts.IsComboFollowUp ? "连击 · 追加进攻" : "轮到你进攻");
                }
                else
                {
                    _banner.Show(ts.IsComboFollowUp ? "对手连击追加进攻" : "对手进攻回合");
                }
            }
            else if (e is AttackDeclaredEvent)
            {
                // 玩家这一拍已经交牌了 —— 告诉玩家「该我做的做完了」；
                // 挡没挡住是结算的事，下一拍 DefenseResolved 会说。
                if (((AttackDeclaredEvent)e).Seat == LocalSeat)
                {
                    _banner.Show("进攻结束");
                }
            }
            else if (e is DefenseResolvedEvent)
            {
                var dr = (DefenseResolvedEvent)e;
                _banner.Show(dr.Success
                    ? "防御成功"
                    : (dr.GaveUp ? "放弃防御 · 掉 1 点" : "防御失败 · 掉 1 点"));
            }
        }

        /// <summary>
        /// 把一条冷却改动翻译成人话（M36）。
        ///
        /// <para>返回 null = 这一条不值得报：<c>Reason</c> 里的「进攻开始 −1」与
        /// 「进入冷却区」是结算的**副产品**（每半场对每张冷却牌都会发一条），
        /// 报出来只会把提示条刷成流水账，把真正的效果提示挤掉。</para>
        /// </summary>
        private string CooldownChangeLine(CooldownChange change)
        {
            string reason = change.Reason ?? string.Empty;

            if (reason.Contains("进攻开始") || reason.Contains("进入冷却区"))
            {
                return null;
            }

            // 区域类「无匹配目标」的收尾条目（引擎专门发了它）—— 正好用来解释
            // 「为什么什么也没发生」，这是最需要提示的一种情况。
            if (change.Card == null || reason.Contains("无匹配目标"))
            {
                return SeatName(_actorSeat) + " " + reason;
            }

            return SeatName(_actorSeat) + " " + VerbOf(reason) + "「" + change.Card.Def.Name + "」 "
                   + change.From + " → " + change.To;
        }

        /// <summary>这一条冷却改动是不是「区域类」（区域加速 / 区域减速）。</summary>
        private static bool IsZoneReason(string reason)
        {
            return !string.IsNullOrEmpty(reason) && reason.Contains("区域");
        }

        /// <summary>
        /// 冷却改动的「动作」措辞 —— 规则事实（是哪一种改动）来自 <c>Reason</c>
        /// （<c>CooldownOps</c> 里写死的），本方法<b>只负责换词</b>，不做任何判断。
        /// </summary>
        private static string VerbOf(string reason)
        {
            if (reason.Contains("区域"))
            {
                return reason.Contains("减速") ? "区域减速" : "区域加速";
            }

            if (reason.Contains("减速"))
            {
                return "减速";
            }

            if (reason.Contains("重置"))
            {
                return "重置冷却";
            }

            if (reason.Contains("立即冷却完成"))
            {
                return "立即冷却完成";
            }

            if (reason.Contains("光环"))
            {
                return "光环指示物作废";
            }

            return "加速";
        }

        /// <summary>
        /// M36：怪物执行效果时额外给一次**中央**提示。
        ///
        /// <para><b>为什么只给怪物</b>（用户口径：「怪物在执行效果（加速减速区加区减等）时，
        /// 应当给予更多的提示」）：玩家自己做的选择自己知道，他看顶部提示条就够了；
        /// 而怪物做的效果（尤其是区域加减速这种一次动好几张牌的）如果只写在那条小小的提示条上，
        /// 很容易整局都注意不到。中央横幅是「一眼必须看到」的位置，正好补这一块。</para>
        /// </summary>
        private void PlayEffectBanner(string line)
        {
            if (_banner == null || string.IsNullOrEmpty(line) || _actorSeat != OpponentSeat)
            {
                return;
            }

            _banner.Show(line);
        }

        /// <summary>
        /// M36：怪物用掉一枚光环 → 那一枚从它在敌方光环行里的位置**飞到怪物模型右侧**。
        ///
        /// <para><b>只做怪物</b>：玩家自己用的那枚走的是另一条路（拖到自己的手牌区准备、
        /// 再由准备标记表达），不需要这一段。</para>
        ///
        /// <para>拿不到「那一枚此刻在哪儿」（例如它本来就被准备标记藏着）时退回用
        /// <b>怪物模型中心</b>当起点 —— 少一点「从哪儿来」的连线感，但方向仍然对。</para>
        /// </summary>
        private void PlayAiAuraCast(AuraConsumedEvent au)
        {
            if (_hudBuff == null || au.Seat != OpponentSeat)
            {
                return;
            }

            AuraIconData data;
            if (!FindAuraBySource(_enemyAuras, au.Source, out data))
            {
                // 图标刚被重建掉（或这一枚本来就不在屏幕上）→ 用事件里的信息合成一份，
                // 画出来仍然是「哪一张牌的哪一种光环」。
                data = new AuraIconData();
                data.Seat = au.Seat;
                data.SourceUid = au.Source == null ? -1 : au.Source.Uid;
                data.Kind = au.Kind;
                data.Value = au.Value;
                data.SourceName = au.Source == null ? string.Empty : au.Source.Def.Name;
                data.Text = string.Empty;
                data.TokenIndex = 0;
            }

            Vector3 from;
            if (!_hudBuff.TryGetIconWorld(data.SourceUid, out from))
            {
                from = ModelCenterWorld(OpponentSeat);
            }

            _hudBuff.PlayCast(data, from);
        }

        /// <summary>在某一份光环数据里按「来源牌」找那枚指示物（M36；按 uid 找，不看第几枚）。</summary>
        private static bool FindAuraBySource(IReadOnlyList<AuraIconData> list, CardInstance source,
            out AuraIconData found)
        {
            found = default(AuraIconData);

            if (list == null || source == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].SourceUid == source.Uid)
                {
                    found = list[i];
                    return true;
                }
            }

            return false;
        }

        private void HandleDecision(DecisionSnapshot snap)
        {
            if (snap.Seat != LocalSeat) return;
            _pendingValid = true;
            _pendingKind = snap.Kind;
            ClassifyOptions(snap);

            // 防御拍不会发 TurnStarted（那是「进攻回合」的开场事件）——
            // 玩家只有在被问到的这一刻才知道「该我挡了」，所以在中央补一声。
            if (_banner != null && snap.Kind == RequestKind.ChooseDefense)
            {
                _banner.Show("轮到你防御");
            }

            // M13：把「这一拍问的是哪件事」翻成拖拽出牌的方向 ——
            // 问进攻 = 箭头指对手，问防御 = 箭头指自己；其余（替换 / 光环 / 选目标…）不开放拖拽。
            // 注意这只是「方向」不是规则判断：能不能出仍然由 Options 说了算。
            if (_hand != null)
            {
                _hand.SetDropIntent(snap.Kind == RequestKind.ChooseAttackCard ? OpponentSeat
                    : snap.Kind == RequestKind.ChooseDefense ? LocalSeat
                    : -1);
            }

            // 替换是唯一的多选决策（可换至多 3 张），它有专门的发牌台；
            // 其余决策一律走「点牌 + 选项浮层」那套。
            bool replace = snap.Kind == RequestKind.ChooseReplace;

            if (_deal != null)
            {
                if (replace)
                {
                    _deal.Show(snap, _hand);
                }
                else
                {
                    _deal.Hide();
                }
            }

            // M25：查看对方手牌（雷云 / 狂躁蘑菇的第 2 个 α 效果）。
            //
            // 这一拍引擎给的选项是**对方的每一张手牌**，但对玩家而言它们全是牌背 ——
            // 列成按钮就等于把「翻开第 1 张 … 翻开第 N 张」写脸上，既没信息量也让
            // 「随机查看」看着像「按序号挑选」。所以不列按钮，改由浮层摆出一排牌背让玩家点。
            //
            // ⚠ 浮层只读选项序号（Option.Index / Value），**绝不碰 Option.Card** ——
            //   一旦拿去取牌名，玩家就能挑走「自己想看的那张」，效果从「随机查看」变成「定向查看」。
            if (_peek != null)
            {
                if (snap.Kind == RequestKind.ChoosePeekCard)
                {
                    _peek.Show(snap.Options);
                }
                else if (_peek.IsShowing)
                {
                    // 上一拍漏收（例如引擎重发了别的决策）时的兜底
                    _peek.Hide();
                }
            }

            // M27：卡牌选择弹窗（磁暴 α / 充能 α / 电弧 α）；M31 起漩涡也走这块浮层
            //（候选换成**冷却区**的牌）。
            //
            // 磁暴 / 充能 / 电弧的选项就是**玩家自己的手牌** —— 走通用分类会照旧把它们的 uid
            // 塞进 _handPlayable（这正是我们要的：手牌要亮、要点得动），所以
            // ClassifyOptions **不**为它早退。真正要拦的是「点一张就立刻提交」那条老路，
            // 那在 OnHandCardClicked 里由 _handPick.IsShowing 分流。
            //
            // 漩涡的选项是**冷却区**的牌 —— 通用分类照旧把 uid 塞进 _coolingPlayable，
            // 于是冷却区那几张迷你卡会被点亮成可点（用户要的「冷却区卡牌都要加上点击判定」
            // 正是这一条走通的：点迷你卡 → OnCoolingCardClicked → 分流进弹窗）。
            //
            // 这一拍的提交入口只有弹窗的确认键，所以浮层下面不列按钮（_picker.Hide()）。
            if (_handPick != null)
            {
                if (snap.Kind == RequestKind.ChooseCoolHandCards)
                {
                    // 标题直接读引擎给的短标题（磁暴 / 充能 / 电弧）——
                    // 「这一拍是哪张牌在结算」只有引擎知道，UI 不猜（见 DecisionRequest.Title）。
                    _handPick.Show(snap, snap.Title, _hand);
                }
                else if (snap.Kind == RequestKind.ChooseRemoveFromGame)
                {
                    // M31 漩涡：候选在冷却区，所以 zone 传 Cooling、hand 传 null
                    //（冷却区没有「多选辉光」那套，见 HandPickView.Show 注释）。
                    _handPick.Show(snap, snap.Title, null, CardZone.Cooling);
                }
                else if (_handPick.IsShowing)
                {
                    _handPick.Hide();
                }
            }

            if (_picker != null)
            {
                if (replace)
                {
                    // 替换的按钮全部由发牌台承担（它还要显示「已选 N / M」）。
                    // 若这里再列一遍 Done，同一件事就有两个入口了。
                    _picker.Hide();
                }
                else if (_zonePickMode)
                {
                    // M26：区域档由「点某一侧的某一格冷却槽」来选（槽位徽标 = 剩余冷却值），
                    // 浮层**只留一条「不执行」的退路**，不再把档位文案列一遍。
                    //（档本身仍然躺在引擎给的 Options 里等槽位的点击配对 —— 铁律 3。）
                    //
                    // ⚠ 例外：若本拍**只有**「双方」档（雪崩 SlowZoneBoth），没有任何侧别可点，
                    //   那就没有「点哪一格」这回事，只能把档列出来让玩家挑。
                    if (snap.Kind == RequestKind.ChooseCooldownEffects) _picker.Show(string.Empty, _flatOptions);
                    else _picker.ShowSkipOnly(snap.Options);

                    if (_zoneBySeat.Count == 0)
                    {
                        _picker.Show(string.Empty, _zoneBothOptions);
                    }
                }
                else if (snap.Kind == RequestKind.ChoosePeekCard)
                {
                    // 这一拍由 _peek 浮层承担输入，浮层下面不再叠一层按钮
                    _picker.Hide();
                }
                else if ((snap.Kind == RequestKind.ChooseCoolHandCards
                          || snap.Kind == RequestKind.ChooseRemoveFromGame)
                         && _handPick != null && _handPick.IsShowing)
                {
                    // M27 / M31：同理 —— 这些拍的输入由选牌弹窗承担（「一张不选直接确认」
                    // 或「强制选一张」都由弹窗自己的确认键表达），浮层下面不再列一遍选项按钮。
                    _picker.Hide();
                }
                else
                {
                    // 标题不重复：决策文案已经由顶部提示条显示（它在最上方、全宽、更醒目），
                    // 浮层只留按钮。
                    _picker.Show(string.Empty, _flatOptions);
                }
            }

            if (_stage != null)
            {
                _stage.SetPrompt(snap.Prompt);
            }

            RefreshAll();

            // M24 #2：刷新之后（它会 ClearPlayable）把「两片冷却区可点」重新点亮
            ApplyZonePickState();

            // M16：引擎每次重发本拍（玩家加 / 减了一枚准备使用的光环就会重发一次）之后，
            // 标记条按新的选项集合重建一次 —— 掉出选项的键会在这里被丢掉。
            // ⚠ 必须排在 RefreshAll() 之后：标记的数据源 _playerAuras 是那一趟里重建的。
            RefreshPreparedMarkers();
        }

        private void HandleFinished(int winnerSeat, string reason)
        {
            if (_resultRoot == null)
            {
                return;
            }

            bool win = winnerSeat == LocalSeat;
            bool draw = winnerSeat < 0;

            if (_resultTitle != null)
            {
                _resultTitle.text = draw ? "平局" : (win ? "胜利" : "失败");
                _resultTitle.color = draw ? UiTheme.TextSecondary : (win ? UiTheme.WinAccent : UiTheme.LoseAccent);
            }

            if (_resultSub != null)
            {
                // 引擎给的 reason 本身就是「AI 生命归零」这种完整短语，
                // 别再在前面套一句同义的「AI 的生命值归零」—— 那是同一句话说两遍。
                string head = draw
                    ? "双方同时倒下"
                    : (string.IsNullOrEmpty(reason) ? (win ? "AI 生命归零" : "你生命归零") : reason);

                _resultSub.text = head
                                  + "\n回合数 " + _driver.GetBattle().TurnNumber
                                  + "　·　种子 " + _driver.LastSeed;
            }

            _resultRoot.SetActive(true);

            if (_stage != null)
            {
                _stage.SetPrompt(string.Empty);
            }

            // M25：终局时把查看浮层收掉，别让它跟结算浮层叠在一起
            if (_peek != null)
            {
                _peek.HideImmediate();
            }

            // M27：同上
            if (_handPick != null)
            {
                _handPick.HideImmediate();
            }

            // M28：按住型浮层不能挂在结算面板上 —— 万一终局那一刻玩家的手指还按着
            //（或者指针已经移出按钮、Released 没送达），这个遮罩会一直压着结算面板。
            // 顺手把手指状态复位，免得下一局的第一次按下被吞掉。
            if (_monsterHand != null)
            {
                _monsterHand.HideImmediate();
            }
            if (_monsterHandHold != null)
            {
                _monsterHandHold.ResetState();
            }

            ClearDecision();
        }

        // ══════════════════════════════════════════════════════
        //  刷新
        // ══════════════════════════════════════════════════════

        private void RefreshAll()
        {
            if (_driver == null || !_driver.IsRunning)
            {
                return;
            }

            // M35：两侧冷却区都要参与配对 —— 玩家的牌与怪物的牌都会从头顶飞向各自的槽
            if (_transit != null) _transit.CaptureBeforeBind(_handBuf, _playerCoolingBuf, _enemyCoolingBuf);
            _driver.CollectHand(LocalSeat, _handBuf);
            _driver.CollectCooling(LocalSeat, _playerCoolingBuf);
            _driver.CollectCooling(OpponentSeat, _enemyCoolingBuf);

            if (_hand != null)
            {
                _hand.Bind(_handBuf, _pendingValid ? _handPlayable : null);
            }

            if (_cooldown != null)
            {
                _cooldown.BindBoth(_playerCoolingBuf, _enemyCoolingBuf);

                if (_pendingValid && _coolingTargeted)
                {
                    _cooldown.SetPlayable(_coolingPlayable);
                }
                else
                {
                    // ⚠ ClearPlayable() 会把「整片可点」的高亮一起收掉（它属于同一次
                    //   决策的显示态），所以区域态必须**排在它之后**重新下下去 ——
                    //   否则每趟刷新都会把刚点亮的两片冷却区按灭。
                    _cooldown.ClearPlayable();
                    ApplyZonePickState();
                }
            }

            if (_stage != null)
            {
                _stage.BindBars(_driver.GetPlayer(LocalSeat),
                    _driver.GetPlayer(OpponentSeat));
            }

            // M15：光环图标必须在 CollectCooling 之后重建（它的数据源就是那两个快照缓冲）
            RefreshAuraIcons();

            ApplyHandPowerPreview();

            BindArtLayer();

            // M35：头顶 → 冷却槽。开局/防御牌的「该飞走了没有」在这里判，
            // ⚠ 必须排在下面 AnimateAfterBind 之前 —— 后者消费本方法登记下来的飞行起点。
            DetectPlayedDepartures();

            if (_transit != null) _transit.AnimateAfterBind(_handBuf, _playerCoolingBuf, _enemyCoolingBuf);
        }

        /// <summary>
        /// 把「玩家已在判定区准备的力量光环」预览到手牌上（M24 #4）。
        ///
        /// <para><b>用户口径</b>：把增加进攻 / 防御力量的光环拖到准备区之后，<b>手牌上显示的
        /// 力量数值要同步变过去</b> —— 让玩家在出牌前就看得见「加上这一枚能涨到多少」。</para>
        ///
        /// <para><b>只预览「用得上的那些牌」</b>，判断依赖本拍的决策类型：</para>
        /// <list type="bullet">
        /// <item>进攻拍（<see cref="RequestKind.ChooseAttackCard"/>）→ 只有进攻向的光环能生效
        /// （<c>AtkPower</c> / <c>AtkOrDefPower</c>），预览的是将要打出的那张；</item>
        /// <item>防御拍（<see cref="RequestKind.ChooseDefense"/>）→ 只有防御向的光环
        /// （<c>DefPower</c> / <c>AtkOrDefPower</c>）；</item>
        /// <item>其它拍（选目标 / 替换 / 光环只报了准备还没出牌…）→ 不预览。</item>
        /// </list>
        ///
        /// <!-- 例外 -->
        /// <para><b>例外（2026-09-25）</b>：卡面写着「此法术的进攻力量不能增加」的牌（沉重打击）
        /// <b>不参与进攻加值预览</b> —— 规则上那份加值对它无效，画上去就是画一个不存在的结果。
        /// 判据见 <see cref="PreviewBonusFor"/>。</para>
        ///
        /// <para><b>为什么「可出的牌」才预览</b>：光环的加值只作用于<b>当时正在打出的那一张</b>
        /// （`rules/01-规则基线.md` §6.2），所以能拿到这份加值的正是本拍 <c>Options</c> 里
        /// 给出来的那几张。给一张打不出的牌画上加值，是画一个不存在的结果。</para>
        ///
        /// <para>⚠ 这里画出来的数<b>不参与任何规则</b> —— 真正加不加、加几张，仍然只由引擎
        /// 在提交那一刻按 <c>Options</c> 决定（铁律 3）。所谓「同源」靠的是两边都读同一份
        /// 已准备集合，而不是这里替引擎算账。</para>
        /// </summary>
        private void ApplyHandPowerPreview()
        {
            if (_hand == null)
            {
                return;
            }

            _hand.ApplyPowerPreview(PreviewBonusFor);
        }

        /// <summary>某张手牌该预览多少力量加值（0 = 不预览）。见 <see cref="ApplyHandPowerPreview"/>。</summary>
        private int PreviewBonusFor(CardSnapshot card)
        {
            if (_preparedKeys.Count == 0 || !_pendingValid)
            {
                return 0;
            }

            // 只有「这一拍在问进攻 / 防御」才有可言的落点
            bool attack = _pendingKind == RequestKind.ChooseAttackCard;
            bool defend = _pendingKind == RequestKind.ChooseDefense;
            if (!attack && !defend)
            {
                return 0;
            }

            // 沉重打击（卡面带「此法术的进攻力量不能增加」）——**卡面必须和规则一致**：
            // 引擎在结算时本来就按不变算（AttackContext.BonusPower 在 NoAtkBuff 下恒为 0），
            // 所以这里也不能把进攻光环的加值画到卡面上，否则玩家看到 9、实际打出 7。
            //
            // ⚠ 只挡**进攻侧**：这条规则管的是进攻力量，防御加值照常预览（规则 §6.3 场合限制）。
            // ⚠ 判据来自快照的 NoAtkBuff（= 卡表里有没有 NoAtkBuff 这一条效果），
            //   不在表现层认卡 ID —— 规则事实仍然只有卡表一个来源。
            if (attack && card.NoAtkBuff)
            {
                return 0;
            }

            // 这张牌得是本拍真能打出去的那几张之一 —— 加值只落在它们身上
            if (!_handPlayable.Contains(card.Uid))
            {
                return 0;
            }

            int bonus = 0;
            for (int i = 0; i < _preparedKeys.Count; i++)
            {
                Option o;
                if (!_auraOptions.TryGetValue(_preparedKeys[i], out o) || o == null)
                {
                    continue;
                }

                if (!BonusApplies(o.AuraKind, attack, defend))
                {
                    continue;
                }

                bonus += o.Value;
            }

            return bonus;
        }

        /// <summary>这枚光环的加值在当前这一拍会不会落到牌上（场合限制，`rules/01-规则基线.md` §6.3）。</summary>
        private static bool BonusApplies(AuraKind kind, bool attack, bool defend)
        {
            switch (kind)
            {
                case AuraKind.AtkPower:
                    return attack;

                case AuraKind.DefPower:
                    return defend;

                // 「进攻 +N 或防御 +N」两拍都能用
                case AuraKind.AtkOrDefPower:
                    return attack || defend;

                // 连击 / 免疫不是「力量 +N」，不改力量数值
                default:
                    return false;
            }
        }

        /// <summary>
        /// 把「两片冷却区可以点」的显示态按本拍的分组下一次（M24 #2）。
        ///
        /// <para>抽成方法是因为它要在两处被下：<see cref="HandleDecision"/> 里点完决策、
        /// 以及 <see cref="RefreshAll"/> 收尾（后者的 <c>ClearPlayable</c> 会把它抹掉）。</para>
        /// </summary>
        private void ApplyZonePickState()
        {
            if (_cooldown == null)
            {
                return;
            }

            if (!_pendingValid || !_zonePickMode)
            {
                _cooldown.SetZonePickable(false);
                _cooldown.SetZoneSeats(null);
                return;
            }

            var seats = new HashSet<int>();
            foreach (var pair in _zoneBySeat)
                if (pair.Value.Count > 0) seats.Add(pair.Key);

            // 一个侧别档都没有（只有雪崩那种「双方」档）→ 没有「点哪一片」可言，不点亮。
            if (seats.Count == 0)
            {
                _cooldown.SetZonePickable(false);
                _cooldown.SetZoneSeats(null);
                return;
            }

            _cooldown.SetZonePickable(true);
            _cooldown.SetZoneSeats(seats);
        }

        /// <summary>
        /// 重建双方的光环图标（M15）。
        ///
        /// <para>数据源 = 冷却区快照里的<strong>指示物明细</strong>：一张牌剩几枚指示物就摆几个图标
        /// （用户口径「相同的 buff 不堆叠，每有一个光环就显示一个图标」）；
        /// 回手 / 消耗之后明细里没有它了，图标自然消失。</para>
        ///
        /// <para><b>「能不能用」由引擎说了算</b>（铁律 3）：这里只做一件事 ——
        /// 拿 <see cref="AuraIconData.MakeKey"/> 去本拍的 <c>Options</c> 里查有没有对应那一项，
        /// 有就点亮、可以拖；没有就压暗、拖了也没反应。</para>
        /// </summary>
        private void RefreshAuraIcons()
        {
            BuildAuras(LocalSeat, _playerCoolingBuf, _playerAuras);
            BuildAuras(OpponentSeat, _enemyCoolingBuf, _enemyAuras);

            if (_hudBuff != null)
            {
                _hudBuff.Bind(_playerAuras, _enemyAuras);
            }

            // 标记条可能到这一刻才建得出来（模板来自 ArtLayer）——建出来后
            // 每次刷新都让它按判定区的实时矩形重新对一次位（分辨率变了也不会偏）。
            EnsurePreparedBar();
            if (_preparedBar != null)
            {
                _preparedBar.Layout();
            }
        }

        private void BuildAuras(int seat, IReadOnlyList<CardSnapshot> cooling, List<AuraIconData> into)
        {
            into.Clear();

            if (cooling == null)
            {
                return;
            }

            for (int i = 0; i < cooling.Count; i++)
            {
                CardSnapshot card = cooling[i];
                if (!card.AuraLive || card.AuraTokens <= 0 || card.AuraTokensDetail == null)
                {
                    continue;
                }

                for (int t = 0; t < card.AuraTokensDetail.Length; t++)
                {
                    AuraTokenSnapshot tok = card.AuraTokensDetail[t];
                    into.Add(new AuraIconData
                    {
                        Seat = seat,
                        SourceUid = card.Uid,
                        TokenIndex = tok.TokenId,
                        Kind = tok.Kind,
                        Value = tok.Value,
                        SourceName = string.IsNullOrEmpty(tok.CardName) ? card.Name : tok.CardName,
                        Text = tok.Text,
                        Usable = _auraOptions.ContainsKey(AuraIconData.MakeKey(seat, card.Uid, tok.TokenId)),
                    });
                }
            }
        }

        /// <summary>把双方快照喂给 HUD / 能量球 / 角色头顶徽标。</summary>
        private void BindArtLayer()
        {
            PlayerSnapshot me = _driver.GetPlayer(LocalSeat);
            PlayerSnapshot foe = _driver.GetPlayer(OpponentSeat);

            if (_hud != null)
            {
                _hud.Bind(me, _handBuf.Count, _attacksRemaining);
                // M15：原来这里还会调 BindBuffSlots 去点亮顶栏下面那排占位图标 ——
                // 那排假图标已经删掉，真正的光环改由 _hudBuff 承载（见 RefreshAuraIcons）。
            }

            Sprite heart = BattleArtLibrary.Instance != null ? BattleArtLibrary.Instance.HudAvatar : null;

            if (_hero != null)
            {
                _hero.SetHpBadge(me.Hp, me.MaxHp, heart);
            }

            // 对方血量必须看得见 —— 否则玩家无法判断「还有几刀能收」。参考图里
            // 双方头顶本来也有浮动徽标，这里沿用同一个位。
            if (_monster != null)
            {
                _monster.SetHpBadge(foe.Hp, foe.MaxHp, heart);
                // M20：怪物模型右下角的手牌数（对方手牌数本来就是公开信息，
                // 玩家侧不建这个节点 —— 手牌就在屏幕上摆着）
                _monster.SetHandCount(foe.HandCount, true);
            }
        }

        private void RefreshLog()
        {
            if (_logText == null || _driver == null)
            {
                return;
            }

            IReadOnlyList<string> lines = _driver.Log;
            _logBuf.Clear();

            int from = Mathf.Max(0, lines.Count - 16);
            for (int i = from; i < lines.Count; i++)
            {
                _logBuf.Add(lines[i]);
            }

            _logText.text = string.Join("\n", _logBuf.ToArray());
        }

        // ══════════════════════════════════════════════════════
        //  选项分类
        // ══════════════════════════════════════════════════════

        private void ClassifyOptions(DecisionSnapshot snap)
        {
            _cardOptions.Clear();
            _flatOptions.Clear();
            // 双发（敌方双发时的防御拍）：整拍改走「连着拖两张」的节奏，
            // 见 OnHandCardDropped / _pairFirstUid。
            _pendingDouble = snap.Kind == RequestKind.ChooseDefense && snap.ContextDouble;
            _pairFirstUid = -1;
            _handPlayable.Clear();
            _coolingPlayable.Clear();
            _auraOptions.Clear();
            _coolingTargeted = false;
            _zonePickMode = false;
            _zoneBySeat.Clear();
            _zoneBothOptions.Clear();

            if (snap.Options == null)
            {
                return;
            }

            // M25：查看对方手牌这一拍，选项挂的牌是**对手**手牌（Option.Card = opponent.Hand[i]），
            // 走下面的通用分类会把它们的 uid 当成「玩家的可选牌」塞进 _handPlayable ——
            // 眼下靠 uid 不撞才不会误点亮，太依赖巧合。这一拍的输入由 PeekView 独占，
            // 所以整体跳过分类，一个都不发出去。
            if (snap.Kind == RequestKind.ChoosePeekCard)
            {
                return;
            }

            for (int i = 0; i < snap.Options.Count; i++)
            {
                Option o = snap.Options[i];

                // M15：光环选项**不走「点牌」那条路** —— 它的入口是 HudBuff 里的图标
                // （拖到自己的手牌区）。所以单拎出来只记进 _auraOptions：
                //   · 不进 _cardOptions → 点冷却区那张迷你卡不会再顺手把光环用掉；
                //   · 不进 _coolingPlayable → 那张迷你卡不会被点亮成「可点」。
                // 用户口径：「该光环的使用判定类似于出牌」= 一样要拖，一样只有合法的那几个可选。
                if (o.AuraSource != null)
                {
                    _auraOptions[AuraIconData.MakeKey(o.AuraSource.OwnerSeat, o.AuraSource.Uid, o.AuraTokenIndex)] = o;
                    continue;
                }

                // M24 #2：区域档（ZoneValue）的分组。它们带 Seat 不带来 Card，
                // 所以先于下面那条「Card == null → _flatOptions」拦下来：
                // 档本身不进浮层，浮层只留一个「不执行」（见 HandleDecision）。
                if (o.Kind == OptionKind.ZoneValue)
                {
                    _zonePickMode = true;

                    if (o.Seat >= 0)
                    {
                        if (!_zoneBySeat.ContainsKey(o.Seat)) _zoneBySeat.Add(o.Seat, new List<Option>());
                        _zoneBySeat[o.Seat].Add(o);
                    }
                    else
                    {
                        _zoneBothOptions.Add(o);
                    }

                    continue;
                }

                if (o.Card == null)
                {
                    _flatOptions.Add(o);
                    continue;
                }

                _cardOptions.Add(o);

                if (o.Card.Zone == CardZone.Cooling)
                {
                    _coolingPlayable.Add(o.Card.Uid);
                    _coolingTargeted = true;
                }
                else
                {
                    _handPlayable.Add(o.Card.Uid);

                    // 双发：成对选项里的**第二张牌**同样要能拖 —— HandView 只放行
                    // _playable 里的牌进判定区，漏了它，玩家想选的「另一半」就是暗的、
                    // 拖不动，而它明明是这次防御的一部分（2026-09-23 修）。
                    // ⚠ 放行只解决「拖得动」；「谁跟谁能配成一对」仍然只由引擎给的选项说了算
                    // （铁律 3：界面不拼选项）。
                    if (o.PairCard != null && o.PairCard.Zone != CardZone.Cooling)
                    {
                        _handPlayable.Add(o.PairCard.Uid);
                    }
                }
            }
        }

        private void ClearDecision()
        {
            _pendingValid = false;

            // 没有待决策时把类型打成一个不可能的值：拖拽落点那处只看 _pendingValid，
            // 但留一个「上一次是什么决策」的残值给下一个读代码的人添堵不值得。
            _pendingKind = (RequestKind)(-1);
            _pendingDouble = false;
            ClearPairPick();
            _cardOptions.Clear();
            _flatOptions.Clear();
            _handPlayable.Clear();
            _coolingPlayable.Clear();
            _auraOptions.Clear();
            _coolingTargeted = false;
            _preparedKeys.Clear();

            // M34：同一时刻在飞的「取消使用」标记也要一起收 —— 这一拍已经结束，
            // 那一枚没有「回到默认位」可言（下一拍引擎会照实发一份光环数据）。
            // ⚠ 先清这份再调 _preparedBar.Clear()：飞行段被放弃时会补发 ReturnLanded，
            //   handler 里那句 `_returningKeys.Remove` 正好返回 false 直接退出，
            //   不会在清场途中反过来刷新一遍界面。
            _returningKeys.Clear();
            _zonePickMode = false;
            _zoneBySeat.Clear();
            _zoneBothOptions.Clear();

            if (_picker != null)
            {
                _picker.Hide();
            }

            // M27：选牌弹窗与发牌台是同一类「标记 + 一次性确认」的界面，收起时一起收。
            if (_handPick != null)
            {
                _handPick.Hide();
            }

            if (_deal != null)
            {
                // 顺带把多选模式关掉、标记清空（否则下一次对局的手牌还亮着辉光）
                _deal.Hide();
            }

            if (_hand != null)
            {
                _hand.ClearSelection();
                _hand.SetPlayable(null);
                // 拖拽方向一并收掉（否则决策结束的瞬间还挂着箭头 / 目标高亮）
                _hand.SetDropIntent(-1);
                // 落区提示底也一并收掉 —— 它的「收」挂在拖拽结束那条路上
                //（HudBuffView.GestureEnded），万一这一拍是在拖拽途中被决策打断的，
                // 那层通栏底色就会一直盖在手牌上（2026-09-20 起它「一上手就亮」，
                // 亮面比原来大，收不干净更容易看出来）。
                _hand.SetAuraDropHighlight(false);
            }

            if (_preparedBar != null)
            {
                // 决策结束 → 手牌区左侧的准备标记全部收掉。真正的消耗与否由引擎决定
                // （放弃防御时准备的力量光环不消耗，那枚指示物会照旧出现在 HudBuff 里）。
                _preparedBar.Clear();
            }

            if (_cooldown != null)
            {
                _cooldown.ClearPlayable();
            }

            if (_hudBuff != null)
            {
                _hudBuff.HideTip();

                // _preparedKeys 刚被清空 → 把上面那几枚被藏起来的图标放出来。
                //（放弃防御时准备的力量光环并不消耗，那枚指示物照旧要显现在 HudBuff 里。）
                _hudBuff.SetHidden(HiddenAuraKeys());
                RefreshAuraIcons();
            }
        }

        // ══════════════════════════════════════════════════════
        //  玩家输入
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 点一下手牌。
        ///
        /// <para><b>M15 起「点一下」不再等于出牌</b>（用户口径：单次点击无法直接出牌进攻/防御，
        /// 必须拖动）。原来这里直接调 <c>TryPickByCard</c> 把牌打出去 ——
        /// 与「拖进判定区松手才出牌」那套并存时，手一抖点歪就把牌丢了。</para>
        ///
        /// <para><b>发牌替换和效果选目标</b>仍保留点击；发牌替换：那本来就是「点牌标记 / 取消标记」的多选交互（M9），
        /// 它不是「出牌」，保留点击。</para>
        ///
        /// <para>点的时候手牌仍然会亮一下（<see cref="HandView"/> 自己在 OnCardClicked 里置亮），
        /// 反馈够用；再往前走一步就回到「误触即出牌」了。</para>
        /// </summary>
        private void OnHandCardClicked(CardView card)
        {
            if (!_pendingValid || card == null)
            {
                return;
            }

            if (_deal != null && _deal.IsOpen && _handPlayable.Contains(card.Card.Uid))
            {
                _deal.Toggle(card.Card.Uid);
                return;
            }

            // M27：选牌弹窗开着时，点手牌 = 把这张放进 / 移出卡框（不提交）。
            // 提交只发生在弹窗的确认键上 —— 与发牌替换同一套「标记 + 一次性确认」的多选语义。
            // ⚠ 这一拍的选择集合由弹窗独占，所以**不能**落到下面 PickCardOption
            //   （那会点一张就立刻提交，把「可多选」压成单选）。
            if (_handPick != null && _handPick.IsShowing)
            {
                _handPick.Toggle(card.Card.Uid);
                return;
            }

            // 效果选目标仍可点击，只有进攻/防御必须拖动。
            //（2026-09-18 起光环也不再有「点击」这条路：RequestKind.ChooseAuraUse 已删，
            //  光环的手势是「拖到判定区」。）
            if (_pendingKind != RequestKind.ChooseAttackCard
                && _pendingKind != RequestKind.ChooseDefense)
                PickCardOption(card.Card.Uid);
        }

        /// <summary>
        /// 手牌被拖进判定区后松手 → 提交决策（M13）。
        ///
        /// <para>用户要的语义是「拖进判定区 → 出箭头 → 松手即出牌」。本作里
        /// 「进攻 / 防御」不是玩家在出牌那一刻选的，而是<b>当前这一拍问的就是哪一件事</b>：
        /// 引擎在 <see cref="RequestKind.ChooseAttackCard"/> 里问「打哪张去进攻」、
        /// 在 <see cref="RequestKind.ChooseDefense"/> 里问「出哪张来挡」。
        /// 所以这里的做法是：把手牌区报回来的座位（= 拖拽开始时定好的方向）译成
        /// 「哪一类决策」，跟当前决策类型对得上就往 <c>Options</c> 里找那张牌；
        /// 对不上就什么都不做，牌自己滑回手牌。</para>
        ///
        /// <para><b>这不是规则判断</b>：能不能打仍然是 <c>Options</c> 说了算 ——
        /// 这里只回答「玩家比划的这个方向，对应的是哪一种决策」，对不上就直接放弃，
        /// 绝不自己拼一个选项出来（铁律第 3 条）。</para>
        /// </summary>
        private void OnHandCardDropped(CardView card, int targetSeat)
        {
            if (!_pendingValid || card == null || _driver == null)
            {
                return;
            }

            bool aimedAtFoe = targetSeat == OpponentSeat;
            bool matches = (aimedAtFoe && _pendingKind == RequestKind.ChooseAttackCard)
                           || (targetSeat == LocalSeat && _pendingKind == RequestKind.ChooseDefense);

            if (!matches)
            {
                return;
            }

            // 双发：一次拖拽只表达得了一张牌，所以这一拍分两段收（2026-09-23）。
            if (_pendingDouble)
            {
                HandleDoubleDefenseDrop(card);
                return;
            }

            PickCardOption(card.Card.Uid);
        }

        /// <summary>
        /// 双发防御的两段拖拽：第一次拖进来只「钉住」，第二次拖进来的牌与它凑成一对才提交。
        ///
        /// <para><b>为什么不能一张拖完就提交</b>：引擎给的双发防御选项是<b>成对的组合</b>
        /// （<c>Option.Card</c> + <c>Option.PairCard</c>）。原先界面按组合里靠前的那张匹配、
        /// 一张就替玩家把整对交了出去 —— 第二张牌玩家根本没机会表达，
        /// 「必须交出两张牌」这条规则在界面上等于不存在。</para>
        ///
        /// <para><b>本方法不判断任何规则</b>：「这两张能不能配成一对」就是去引擎给的选项里
        /// <b>找</b>有没有对应组合（铁律 3）。找不到就什么都不做、只提示，玩家自然会换一张。</para>
        /// </summary>
        private void HandleDoubleDefenseDrop(CardView card)
        {
            int uid = card.Card.Uid;

            // 第一张：只钉住，不提交
            if (_pairFirstUid < 0)
            {
                _pairFirstUid = uid;
                _hand.SetPinnedUid(uid);
                TipDoubleProgress();
                return;
            }

            // 又拖了同一张 = 取消这一张
            if (uid == _pairFirstUid)
            {
                _pairFirstUid = -1;
                _hand.SetPinnedUid(-1);
                TipDoubleProgress();
                return;
            }

            Option pair = FindDefensePair(_pairFirstUid, uid);
            if (pair == null)
            {
                // 这一对不是引擎给出的合法组合 → 保留第一张，只提示（已选的那张不会白选）
                if (_stage != null)
                {
                    _stage.SetPrompt("这两张合计挡不住这次双发，换一张再试；"
                                     + "再拖一次已选的那张可以取消");
                }

                return;
            }

            _pairFirstUid = -1;
            _hand.SetPinnedUid(-1);
            Pick(pair);
        }

        /// <summary>在引擎给的双发选项里找「正好是这两张」的那一对（与选中顺序无关）。</summary>
        private Option FindDefensePair(int uidA, int uidB)
        {
            for (int i = 0; i < _cardOptions.Count; i++)
            {
                Option o = _cardOptions[i];
                if (o.Card == null || o.PairCard == null)
                {
                    continue;
                }

                if ((o.Card.Uid == uidA && o.PairCard.Uid == uidB)
                    || (o.Card.Uid == uidB && o.PairCard.Uid == uidA))
                {
                    return o;
                }
            }

            return null;
        }

        /// <summary>双发选到一半时的提示条文案（选完 / 取消第一张之后都要刷一次）。</summary>
        private void TipDoubleProgress()
        {
            if (_stage == null)
            {
                return;
            }

            _stage.SetPrompt(_pairFirstUid < 0
                ? "对方双发：拖出第一张用于防御的牌"
                : "对方双发：已选 1 张，再拖一张凑成一对（两张合计需够挡）");
        }

        /// <summary>清掉双发「已选第一张」（换拍 / 提交 / 收尾时用）。</summary>
        private void ClearPairPick()
        {
            _pairFirstUid = -1;

            if (_hand != null)
            {
                _hand.SetPinnedUid(-1);
            }
        }

        /// <summary>
        /// 冷却区的牌仍然是「点选一个目标」的语义（加速 / 减速 / 移除 / 复制目标…）。
        ///
        /// <para><b>但光环选项除外</b>：那条入口在 HudBuff 的图标上（M15），
        /// 点一下冷却区的迷你卡不该顺手把光环用掉。</para>
        ///
        /// <para><b>M31 漩涡例外</b>：这一拍「点冷却区的牌」= 把它标记进选择弹窗的卡框
        /// （不提交），与 M27 里点手牌的行为一致 —— 提交只在弹窗的确认键上。
        /// 用户口径：「漩涡是从冷却区当中选，强制选择一张」，
        /// 且「冷却区域当中的所有卡牌上都要加上点击判定」。</para>
        /// </summary>
        private void OnCoolingCardClicked(CardView card)
        {
            if (!_pendingValid || card == null)
            {
                return;
            }

            // ⚠ 必须先于下面的 PickCardOption：那会点一张就立刻提交，
            //   既做不到「先看清楚再确认」，也压不住「强制选一张」这条规则的界面表达。
            if (_handPick != null && _handPick.IsShowing)
            {
                _handPick.Toggle(card.Card.Uid);
                return;
            }

            if (!PickCardOption(card.Card.Uid))
            {
                // M36：这张牌本拍不在引擎给的候选里（也就是屏幕上被压暗的那一类）——
                // 在中央弹一句「为什么」。用户口径：「当对这些不可加速或减速的卡进行
                // 加速或减速时，应当弹出一段无法被加速或减速的文字」。
                NotifyRejectedTarget();
            }
        }

        /// <summary>
        /// 在「有实体卡」的选项里找那张牌并提交。<b>返回是否真的提交了</b>（M36）——
        /// 没找到说明这张牌本拍不是合法目标，调用方据此弹提示。
        /// </summary>
        private bool PickCardOption(int uid)
        {
            for (int i = 0; i < _cardOptions.Count; i++)
            {
                Option o = _cardOptions[i];
                if (o.Card != null && o.Card.Uid == uid)
                {
                    Pick(o);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 点了一张**本拍不能作为目标**的冷却区牌 → 在屏幕中央弹一句「为什么」（M36）。
        ///
        /// <para><b>这不是规则判断</b>（铁律 3）：这里回答的是「引擎这一拍给的候选里有没有它」，
        /// 而那件事已经由引擎在生成 <c>Options</c> 时定好了（例如瀑流的「已经加速过的那张
        /// 不再列出来」、减速的「已经顶到基础冷却值的不列出来」）。
        /// 界面只是把「没有它」这件事翻译成一句人能读懂的话。</para>
        ///
        /// <para>只有本拍确实在选冷却区目标时才出声：选牌弹窗 / 发牌替换那几拍里被压暗的牌
        /// 另有含义，乱弹一句反而误导。</para>
        /// </summary>
        private void NotifyRejectedTarget()
        {
            if (_floatTip == null || !_pendingValid || !_coolingTargeted)
            {
                return;
            }

            _floatTip.Show(RejectTipFor(_pendingKind));
        }

        /// <summary>这一拍「点不动的牌」该怎么解释 —— 措辞随决策类型（理由来自引擎的过滤口径）。</summary>
        private static string RejectTipFor(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.ChooseHasteTarget:
                    return "该牌无法被加速";

                case RequestKind.ChooseSlowTarget:
                    return "该牌无法被减速 · 剩余冷却已达上限";

                case RequestKind.ChooseRefreshTarget:
                    return "该牌的冷却无法再被改动";

                default:
                    return "该牌本拍无法作为目标";
            }
        }

        /// <summary>
        /// 点了一下某一侧某一行的冷却槽（区域加速 / 减速，M26）。
        ///
        /// <para><b>一个点击怎么落成「某一档 k」</b>：槽位徽标上写的就是这一行的含义
        /// （「N / 冷却区N」），行号与剩余冷却的换算是
        /// <c>k = 4 − rowIndex</c>（行 0 = 冷却区4 → 剩 4 回合；行 3 = 冷却区1 → 剩 1 回合，
        /// 与 <see cref="CooldownView.BindGrouped"/> 的分行口径一致）。</para>
        ///
        /// <para>拿到 (座位, k) 之后仍然<strong>去本拍的 <c>Options</c> 里找那一项</strong>：
        /// 该侧这一档存在就提交它，不存在就什么都不做 —— 界面始终不替引擎做规则判断
        /// （铁律 3）。玩家点的可能是一格没有牌的槽（那一档在引擎里就没被生成成选项）。</para>
        ///
        /// <para>挡在门外的两种情况：本拍根本不是区域决策、或这一侧一个档都没有。</para>
        /// </summary>
        private void OnCooldownZoneRowClicked(int seat, int rowIndex)
        {
            if (!_pendingValid || seat < 0 || !_zoneBySeat.ContainsKey(seat))
            {
                return;
            }

            List<Option> options = _zoneBySeat[seat];
            if (options.Count == 0)
            {
                return;
            }

            int value = 4 - rowIndex;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Value == value)
                {
                    Pick(options[i]);
                    return;
                }
            }

            // 这一格没有对应的档（该剩余冷却值上没有牌 / 引擎没给这一项）——
            // 不做任何事，也不退回浮层：玩家点的是空格，理应没有后果。
        }

        // ══════════════════════════════════════════════════════
        //  M15 / M16 / M21：光环的手势
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-19 定落区，2026-09-20 改摆位与取消手势）：
        //    · 不设光环选择环节 —— 进攻 / 防御时**随时**可拖；
        //    · 落区 = **自己的手牌区**（HandArea，屏幕底部那条），不是出牌的中央判定区；
        //    · 松手在手牌区 = 这枚光环「现在准备用」：原图标从 HudBuff 消失，
        //      标记飞到**手牌区左侧**排好；
        //    · **再拖一下那枚标记** = 取消（自动复位回 HudBuff 的默认位）；
        //    · 免疫类光环不需要牌，所以拖进手牌区就是当场使用，不进入准备态。

        /// <summary>
        /// 拖动中：把整片落区点亮（只做视觉暗示，不判合法性）。
        ///
        /// <para><b>用户口径（2026-09-20）</b>：<b>一开始拖就亮</b>，不等指针进到手牌区 ——
        /// 这层提示底的作用是「告诉玩家该往哪儿拖」，等他自己碰巧摸到落区才亮就等于没提示。
        /// 所以判据从「指针在不在 <c>HandArea</c> 里」改成「这一拍这枚指示物能不能用」，
        /// 且整个拖拽期间<b>一直亮着</b>（<c>IconDragBegin</c> 与 <c>IconDragMove</c> 都走这里，
        /// 松手 / 挪开时由 <see cref="OnAuraGestureEnded"/> 收掉）。</para>
        ///
        /// <para>落点合法与否**不在这里判** —— 松手那一拍才判（见 <see cref="OnAuraDropped"/>）。</para>
        /// </summary>
        private void OnAuraDragMove(AuraIconData data, Vector2 screenPos)
        {
            // 用不了的指示物拖不动（AuraIconView.OnBeginDrag 直接返回），这里再挡一道，
            // 免得将来有别的入口把「不可用」的图标也接上拖拽、却亮了落区。
            bool usable = _pendingValid && _auraOptions.ContainsKey(data.Key);

            // 拖 HudBuff 的原图标时只点亮**手牌区**那一层提示底（M21 起这是唯一的落区提示）。
            if (_hand != null)
            {
                _hand.SetAuraDropHighlight(usable);
            }
        }

        /// <summary>
        /// 松手：落点在<b>自己的手牌区</b>里、且这枚指示物确实对得上本拍的一个合法选项 → 准备使用它。
        ///
        /// <para><b>本方法不判断任何规则</b>（铁律 3）：「这枚能不能用」就是
        /// 「<see cref="_auraOptions"/> 里有没有它」；用不了的拖过来没有任何反应，图标自己弹回。
        /// 免疫类走「当场使用」，其余走「准备态」，也只是光环类型的分支，不是合法性判断。</para>
        /// </summary>
        private void OnAuraDropped(AuraIconData data, Vector2 screenPos)
        {
            OnAuraGestureEnded();

            if (!_pendingValid || _driver == null || _hand == null)
            {
                return;
            }

            Option o;
            if (!_auraOptions.TryGetValue(data.Key, out o))
            {
                return;
            }

            if (!_hand.IsInHandArea(screenPos))
            {
                return;
            }

            if (AuraResolver.IsImmune(o.AuraKind))
            {
                // 免疫：消耗即整个攻击被免疫、不需要交牌，所以没有「准备」这一说，
                // 拖进手牌区松手就当场结算。
                ClearDecision();
                _driver.SubmitPlayerDecision(new int[0], new[] { o.Index });
                return;
            }

            if (_preparedKeys.Contains(data.Key))
            {
                return;
            }

            _preparedKeys.Add(data.Key);

            // 从落点飞向手牌区左侧的槽位（起点 = 松手时指针的位置）。
            if (_preparedBar != null)
            {
                _preparedBar.SetSpawnOrigin(data.Key, screenPos);
            }

            RefreshPreparedMarkers();
            SubmitPreparedAuras();
        }

        /// <summary>
        /// 手牌区左侧的标记被再拖了一下 → 取消使用这枚光环。
        ///
        /// <para><b>用户口径（2026-09-23，M34）</b>：取消时这枚光环会「<b>从左侧立即消失、
        /// 然后瞬间出现在原位</b>」，要求补一个动画。所以现在不是「删掉一枚标记 +
        /// 让图标复位」，而是让<b>这一枚自己走回去</b>：</para>
        ///
        /// <list type="number">
        /// <item>先算它回默认位之后占<b>第几格</b>（按取消之后的可见序列 —— 被藏起来的那些不占格，
        /// 见 <see cref="HomeSlotFor"/>）；</item>
        /// <item>把它记进 <see cref="_returningKeys"/>，让 HudBuff 里那枚继续隐着；</item>
        /// <item><see cref="PreparedAuraView.FlyHome"/> 起航（0.30 s 滑回，其余标记同时补位）；</item>
        /// <item>落地 → <see cref="OnPreparedAuraReturnLanded"/> 把图标放出来 —— 同一位置、同一尺寸，
        /// 视觉上就是「它回到了自己的位置」。</item>
        /// </list>
        ///
        /// <para>本方法仍然不含任何规则判断：删掉一个键、请引擎重发本拍，合法性照旧只由引擎说。</para>
        /// </summary>
        private void OnPreparedAuraCancelled(AuraIconData data)
        {
            if (!_preparedKeys.Remove(data.Key))
            {
                return;
            }

            // ⚠ 落点必须按**已经删掉它**的集合算：它自己不该占格（见 HomeSlotFor）。
            RectTransform home = HomeSlotFor(data.Key);

            bool flying = false;
            if (_preparedBar != null && home != null)
            {
                _returningKeys.Add(data.Key);
                flying = _preparedBar.FlyHome(data.Key, home, UiLayout.AuraReturnSeconds);

                if (!flying)
                {
                    // 找不到那枚标记（理论上不该发生）→ 退回「当场收回」，别把它永久隐着。
                    _returningKeys.Remove(data.Key);
                }
            }

            RefreshPreparedMarkers();
            SubmitPreparedAuras();

            // 没有飞行段（拿不到落点 / 找不到标记）：图标该立刻回来，否则要等下一次刷新才出现。
            if (!flying)
            {
                RefreshAuraIcons();
            }
        }

        /// <summary>
        /// 一枚「取消使用」的标记飞回默认位并落地了 → 把它从 <see cref="_returningKeys"/> 摘掉，
        /// 于是下一次 <see cref="HudBuffView.SetHidden"/> 不再隐它，图标就在原地显出来。
        /// </summary>
        private void OnPreparedAuraReturnLanded(long key)
        {
            if (!_returningKeys.Remove(key))
            {
                return;
            }

            if (_hudBuff != null)
            {
                _hudBuff.SetHidden(HiddenAuraKeys());
                RefreshAuraIcons();
            }
        }

        /// <summary>
        /// 这枚指示物<b>回到默认位之后</b>占默认位那一行的第几格（找不到 = null）。
        ///
        /// <para>按 <see cref="_playerAuras"/> 的顺序数「没被藏起来的」有几枚：
        /// <see cref="HudBuffView.Collect"/> 第 3 步就是按这个顺序一格一个往下发的，
        /// 所以这个序号既是取消之后的序号，也是它落地那一刻它真正会占的格子
        /// （那时 <see cref="_returningKeys"/> 已经把它摘掉了）。</para>
        /// </summary>
        private RectTransform HomeSlotFor(long key)
        {
            if (_hudBuff == null)
            {
                return null;
            }

            int index = 0;
            for (int i = 0; i < _playerAuras.Count; i++)
            {
                if (_playerAuras[i].Key == key)
                {
                    return _hudBuff.PlayerSlotAt(index);
                }

                if (!_preparedKeys.Contains(_playerAuras[i].Key))
                {
                    index++;
                }
            }

            return null;
        }

        /// <summary>
        /// 要在 HudBuff 里隐去的指示物 = <b>已准备的</b> + <b>正飞回默认位的</b>（M34）。
        ///
        /// <para>这两类在屏幕上都不该由 HudBuff 出头：前者显示在手牌区左侧那排标记上，
        /// 后者正在飞回去的路上。做成一处的口径，省得两个集合各隐一半、出现两份或零份。</para>
        /// </summary>
        private IReadOnlyList<long> HiddenAuraKeys()
        {
            _hiddenBuf.Clear();

            for (int i = 0; i < _preparedKeys.Count; i++)
            {
                _hiddenBuf.Add(_preparedKeys[i]);
            }

            for (int i = 0; i < _returningKeys.Count; i++)
            {
                _hiddenBuf.Add(_returningKeys[i]);
            }

            return _hiddenBuf;
        }

        private void OnAuraGestureEnded()
        {
            if (_hand != null)
            {
                _hand.SetAuraDropHighlight(false);
            }
        }

        /// <summary>
        /// 重建手牌区左侧的准备标记。顺带把「已经不在本拍选项里」的键丢掉
        /// （光环被消耗 / 回手 / 本拍不再合法）。
        /// </summary>
        private void RefreshPreparedMarkers()
        {
            _preparedIcons.Clear();

            for (int i = _preparedKeys.Count - 1; i >= 0; i--)
            {
                long key = _preparedKeys[i];
                if (!_auraOptions.ContainsKey(key))
                {
                    _preparedKeys.RemoveAt(i);
                    continue;
                }

                AuraIconData data;
                if (FindAura(_playerAuras, key, out data))
                {
                    _preparedIcons.Insert(0, data);
                }
            }

            // 准备中的这几枚在 HudBuff 里要隐去 —— 同一个指示物只该亮一处，
            // 真正显示它的地方是判定区左侧那一排（M19）。
            // M34：正在飞回默认位的那几枚也算（落地前不能两份同时在屏幕上）。
            if (_hudBuff != null)
            {
                _hudBuff.SetHidden(HiddenAuraKeys());
            }

            if (_preparedBar != null)
            {
                _preparedBar.Bind(_preparedIcons);
            }
        }

        private static bool FindAura(IReadOnlyList<AuraIconData> list, long key, out AuraIconData found)
        {
            found = default(AuraIconData);

            if (list == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key == key)
                {
                    found = list[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>把「准备使用的光环」解析成本拍的选项序号（解析不出来的直接跳过）。</summary>
        private int[] PreparedAuraIndices()
        {
            if (_preparedKeys.Count == 0)
            {
                return null;
            }

            var list = new List<int>();
            for (int i = 0; i < _preparedKeys.Count; i++)
            {
                Option o;
                if (_auraOptions.TryGetValue(_preparedKeys[i], out o))
                {
                    list.Add(o.Index);
                }
            }

            return list.Count == 0 ? null : list.ToArray();
        }

        /// <summary>
        /// 把「准备使用的光环」报备给引擎，请它按新集合重发本拍决策。
        ///
        /// <para><b>为什么不只是改改 UI</b>：防御牌的合法性依赖已准备的光环加值
        /// （引擎按它算「这张牌还差几点够挡」）。这份集合只有引擎知道，界面才可能只渲染
        /// 合法选项。重发期间本拍暂时失效（<see cref="_pendingValid"/> 置 false），
        /// 否则玩家在往返的一两帧里落牌会带着过期的序号。</para>
        /// </summary>
        private void SubmitPreparedAuras()
        {
            if (_driver == null)
            {
                return;
            }

            _pendingValid = false;

            if (_picker != null) _picker.Hide();
            if (_hand != null)
            {
                _hand.SetPlayable(null);
                _hand.SetDropIntent(-1);
            }

            if (_cooldown != null) _cooldown.ClearPlayable();

            _driver.SubmitPlayerAuraPrep(PreparedAuraIndices());
        }

        private void OnOptionPicked(Option option)
        {
            Pick(option);
        }

        /// <summary>
        /// 发牌台「确认替换」：把标记住的多个选项序号<strong>一次性</strong>回填给引擎。
        /// 引擎的 <c>PickOptions</c> 会按 <c>MaxSelect</c> 截断，所以这里不需要自己再数一遍上限。
        /// </summary>
        private void OnDealConfirmed(List<int> optionIndices)
        {
            if (!_pendingValid || _driver == null || optionIndices == null || optionIndices.Count <= 0)
            {
                return;
            }

            // 先拷成数组：ClearDecision() 会走 _deal.Hide()，把发牌台内部那份列表清掉。
            int[] picks = optionIndices.ToArray();

            ClearDecision();
            _driver.SubmitPlayerDecision(picks);
        }

        /// <summary>发牌台「不替换 / 保留这张」：直接提交引擎给的那个 Done 选项（文案也是它给的）。</summary>
        private void OnDealSkipped(Option option)
        {
            Pick(option);
        }

        /// <summary>
        /// M27 选牌弹窗「确认」：把选中的多个选项序号<strong>一次性</strong>回填给引擎。
        ///
        /// <para><b>一张没选也照样提交</b>（空数组）—— 磁暴 / 充能的 <c>MinSelect = 0</c>，
        /// 「什么都没选」本身就是合法答案；电弧的 <c>MinSelect = 1</c> 由弹窗自己拦住
        /// （选够之前确认键是按不动的），所以这里不重复判断。</para>
        /// </summary>
        private void OnHandPickConfirmed(List<int> optionIndices)
        {
            if (!_pendingValid || _driver == null)
            {
                return;
            }

            // 先拷成数组：ClearDecision() 会走 _handPick.Hide()，把弹窗内部那份列表清掉。
            int[] picks = optionIndices == null ? new int[0] : optionIndices.ToArray();

            ClearDecision();
            _driver.SubmitPlayerDecision(picks);
        }

        private void Pick(Option option)
        {
            if (!_pendingValid || option == null || _driver == null)
            {
                return;
            }

            // 出牌那一刻把「判定区里准备使用的光环」一并提交（2026-09-18）。
            // 其它决策的 _auraOptions 一定是空的，所以这里不需要按决策类型分叉。
            int[] auras = PreparedAuraIndices();

            ClearDecision();
            _driver.SubmitPlayerDecision(new[] { option.Index }, auras);
        }

        private void OnSettingsClicked()
        {
            if (_settings != null) _settings.Open();
        }

        private void OnAgainClicked()
        {
            if (_driver == null)
            {
                return;
            }

            // 由 driver 决定这一局用随机种子还是固定种子（BattleDriver._randomSeed）——
            // 本类不再自己算，否则会出现「两套种子口径」，随机开关也会被绕过。
            _driver.StartBattle();
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

        private string SeatName(int seat)
        {
            if (_driver == null || !_driver.IsRunning)
            {
                return seat == LocalSeat ? "你" : "AI";
            }

            return _driver.GetPlayer(seat).Name;
        }
    }
}
