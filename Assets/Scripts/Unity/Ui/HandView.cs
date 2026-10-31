using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 手牌区视图（M8 建、M12 改成扇形 + 可拖拽、M13 排布改真圆弧 + 出牌判定区）。
    ///
    /// <para><b>为什么不用 <c>HorizontalLayoutGroup</c></b>：布局组会用子节点的 preferredSize
    /// 重新分配 <c>anchoredPosition</c>，「选中上浮」改的正是这个值 —— 两者会每帧打架。
    /// 本类改成<b>自定义排布</b>：每一帧自己算目标位姿（位置 + 角度 + 缩放），再平滑趋近，
    /// 顺带把「手牌增删时卡片滑动换位」也做出来了（布局组只会瞬移）。</para>
    ///
    /// <para><b>弧形（M13 定稿：真圆）</b>（`Assets/Art/_Reference/Reference.png` 的摆法）：
    /// 所有牌的底边中点落在<b>同一个圆</b>上，旋转角 = 圆心角，圆心在画面正下方。
    /// 圆半径由「横向半跨距」与「半张角」反解（见 <see cref="UiLayout.HandFanTotalAngle"/> 的注释），
    /// 于是「弧的深浅」是被动量，永远与横跨宽度和摆角匹配 ——
    /// 旧版「横排 + 一个独立的下沉量」能调出不成形的弧，已废弃。</para>
    ///
    /// <para><b>拖拽出牌（M13 口径）</b>：牌要拖进画面中央那块<b>不可视判定区</b>才算数 ——
    /// 进区即出现指向箭头（进攻 = 红指对手 / 防御 = 蓝指自己），此时松手即出牌；
    /// 没进区就什么都不发生，牌自己滑回扇形。判定哪个方向是「这一拍该做的事」不在本类：
    /// <see cref="BattleUi"/> 按 <c>DecisionSnapshot.Kind</c> 调 <see cref="SetDropIntent"/>，
    /// 本类只把「牌被拖进了判定区」这件事报回给 <see cref="BattleUi"/>，
    /// 由后者去 <c>DecisionSnapshot.Options</c> 里找对应选项（铁律第 3 条：规则只收敛在引擎一处）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandView : MonoBehaviour, ICardGestureHost, ICardSlideHost
    {
        public int LocalSeat { get; private set; }
        public int OpponentSeat { get; private set; } = 1;
        public void ConfigureSeats(int localSeat, int opponentSeat)
        {
            LocalSeat = localSeat; OpponentSeat = opponentSeat;
        }

        /// <summary>悬停/选中趋近速度（越大越跟手）。</summary>
        private const float SmoothSpeed = 16f;

        [Header("Prefab / 容器")]
        [SerializeField] private CardView _cardPrefab;
        [SerializeField] private RectTransform _row;

        [Header("看牌浮层（长按弹完整卡面）")]
        [SerializeField] private CardDetailView _peek;

        [Header("M13 出牌判定区 + 指向箭头")]
        [Tooltip("不可视的出牌判定区（拖进来才算「要出牌」）。")]
        [SerializeField] private RectTransform _playZone;
        [Tooltip("拖进判定区时出现的指向箭头（进攻红 / 防御蓝）。")]
        [SerializeField] private DropArrowView _arrow;

        [Header("行为")]
        [Tooltip("场上有待决策时，把不可打的牌压暗。")]
        [SerializeField] private bool _dimUnplayable = true;

        [Tooltip("长按手牌时弹出完整卡面。")]
        [SerializeField] private bool _peekOnLongPress = true;

        [Header("换牌投放区（2026-10-31 · 由 DealView 运行时接进来）")]
        [Tooltip("换牌决策时，把手牌向上拖进这块区域 = 把这张牌换掉。null = 关掉这条手势。")]
        [SerializeField] private RectTransform _replaceZone;

        [Header("M15 光环落区")]
        [Tooltip("把 HudBuff 的光环图标拖进手牌区时亮起的那一层提示底（很淡）。")]
        [SerializeField] private GameObject _auraDropHint;

        private readonly List<CardView> _pool = new List<CardView>();
        private readonly List<CardView> _shown = new List<CardView>();
        private readonly Dictionary<int, CardView> _reuse = new Dictionary<int, CardView>();
        private readonly HashSet<int> _playable = new HashSet<int>();

        /// <summary>多选模式下被标记的牌（M9 发牌替换）。单选模式不使用。</summary>
        private readonly HashSet<int> _multi = new HashSet<int>();

        /// <summary>拖拽落点：己方 / 敌方角色（由 <see cref="BattleUi"/> 塞进来），只用来做高亮反馈。</summary>
        private CharacterView _selfTarget;
        private CharacterView _enemyTarget;

        private int _hoverIndex = -1;
        private int _selectedUid = -1;

        /// <summary>
        /// 双发防御这一拍的「已选」集合（2026-10-08 改；原先是单个 <c>_pinnedUid</c>）。
        ///
        /// <para>双发要求交出<b>两张</b>够挡的牌，所以这一拍必须能<b>同时</b>盯住两张。原来的写法
        /// 是「第一张钉住、第二张拖进来配对」—— 钉住的那张不认点击，玩家点第一张（亮）再点第二张，
        /// 第一张的辉光会被单选逻辑换掉，看着像「只选了一张」；而且一旦拖拽失败，
        /// 已选的也留不住。现在改成一份集合：<b>点一下加一张、再点一下取消</b>，
        /// <b>拖进判定区的那张也并进来</b>（见 <see cref="AddPick"/>），最多两张。</para>
        ///
        /// <para>⚠ 本视图仍然<b>不判断任何规则</b>：这两张能不能配成一对、能不能挡住这次双发，
        /// 只有引擎给的 <c>DecisionSnapshot.Options</c> 说了算（<see cref="BattleUi"/> 在那里面
        /// <b>找</b>组合，找不到就什么都不提交）。</para>
        ///
        /// <para>⚠ <b>顺序有意义</b>：满两张后再加会顶掉<b>最早</b>那张 —— 后点 / 后拖的优先，
        /// 保证「玩家刚表达的那张」永远在集合里。</para>
        /// </summary>
        private readonly List<int> _picked = new List<int>();

        /// <summary>这一拍最多能标记几张（双发 = 两张）。</summary>
        private const int MaxPicked = 2;

        /// <summary>是否处于「点击累积标记」模式（<see cref="SetAccumulatePick"/>）。</summary>
        private bool _accumulatePick;

        /// <summary>多选模式：选中不再互斥，可以同时标好几张（发牌替换用）。</summary>
        private bool _multiMode;

        /// <summary>是否处于「本次决策只允许点某几张牌」的状态。
        /// 注意区分「不限制」（restrict = false）与「一张都不能点」（restrict = true 且集合为空）。</summary>
        private bool _restrict;

        /// <summary>
        /// 当前这一拍「拖牌出牌」的方向：<see cref="OpponentSeat"/> = 进攻（箭头指对手）、
        /// <see cref="LocalSeat"/> = 防御（箭头指自己）、-1 = 这一阵没有拖拽出牌的语义。
        ///
        /// <para><b>谁来定</b>：<see cref="BattleUi"/> 按 <c>DecisionSnapshot.Kind</c> 翻译
        /// （ChooseAttackCard → 敌、ChooseDefense → 己）。本类**不判断规则**，
        /// 只把「玩家比划的方向」报回给 BattleUi 去对 Options（铁律第 3 条）。</para>
        /// </summary>
        private int _dropIntentSeat = -1;

        // ── 滑动选牌 = 画刷（2026-10-31 重做）──────────────────
        //
        //  用户口径（原文）：「整次滑动被当成一支画笔，笔尖扫过哪些牌，哪些牌就统一变成
        //  同一个状态」—— 不是「逐张点击」的批量版。所以：
        //    · 笔刷状态在按下那一刻由**起点牌**决定：起点没选过 → 整次都「加选」；
        //      起点已经选过 → 整次都「取消」（划到已选中的牌不会把它反选掉）；
        //    · 上限按场景不同（进攻 / 单防 1 张、双发 2 张、开局换牌 3 张、补牌 1 张），
        //      满了以后继续划过的牌**一律不选**，也**不**顶掉前面已选的；
        //    · 指针按**线段**补点（OnDrag 一帧只来一次，快速划过会跳格），
        //      笔尖路径压到哪张就算哪张。
        //
        //  与拖牌的关系：两者抢同一串指针事件，由 TryBeginSlide 按**主轴方向**分派 ——
        //  向上（进判定区）= 出牌，横向 = 选牌。滑动期间牌不跟手、不重排。

        /// <summary>正在滑动选牌。</summary>
        private bool _sliding;

        /// <summary>本次滑动的笔刷状态：true = 一路「取消」，false = 一路「加选」。</summary>
        private bool _slideErase;

        /// <summary>上一条 Slide 报来的指针位置（画刷按线段补点用）。</summary>
        private Vector2 _slideLastScreen;

        /// <summary>本次滑动已经处理过的牌（同一张划两次既不重复选、也不来回取消）。</summary>
        private readonly HashSet<int> _slideTouched = new HashSet<int>();

        /// <summary>本次滑动已作废（决策已经交出去了）—— 之后划到的牌一律不理。</summary>
        private bool _slideDead;

        /// <summary>这一帧收到过「抬起」（见 <see cref="LateUpdate"/> 的兜底）。</summary>
        private bool _releasePending;

        /// <summary>
        /// 滑动选牌总开关（2026-10-31 用户临时口径：false = 任何手势都按「拖动」处理）。
        /// 恢复滑动 = 改回 true，方向判据（SlideMinPixels / SlideHorizontalRatio）原样保留。
        /// 故意用 static 字段而不是 const：const 会让编译器把判据代码判成不可达（CS0162）。
        /// </summary>
        private static bool _slideSelectEnabled = false;

        /// <summary>横向位移至少这么多像素才可能是滑动（太小 = 手抖，交给点击 / 拖拽）。</summary>
        private const float SlideMinPixels = 12f;

        /// <summary>横向位移要超过纵向的多少倍才算「滑动选牌」，否则按向上拖牌出牌处理。</summary>
        private const float SlideHorizontalRatio = 1.35f;

        /// <summary>画刷补点的步长（屏幕像素）：一帧内跨过的牌全部算划过。</summary>
        private const float SlideSamplePixels = 10f;

        /// <summary>一次 Slide 最多补多少个采样点（极快甩动时封顶，别白算）。</summary>
        private const int SlideMaxSamples = 240;

        /// <summary>
        /// 画刷把「选中 / 取消」交给谁 —— 发牌台 / 选牌弹窗手里那份选中集合才是真源，
        /// 它们改完会回写 <see cref="_multi"/>。为 null 时本视图自己管（单选拍 / 双发拍）。
        /// </summary>
        private Action<int, bool> _paintSink;

        /// <summary>本次决策最多能同时选几张（发牌台 / 选牌弹窗按引擎的 MaxSelect 给）。0 = 按模式兜底。</summary>
        private int _paintLimit;

        // ── 换牌投放区（2026-10-31）──────────────────────────
        //
        //  这块区域不是「判定区」的复用：判定区在画面中央、只在出牌拍亮；
        //  投放区贴在手牌区上方、只在换牌拍出现。所以单开一组状态，别把两套语义拌在一起。

        /// <summary>投放区此刻是否被牌压住（决定松手时是「换掉」还是「滑回扇形」）。</summary>
        private bool _dragOverReplace;

        /// <summary>通知外面把投放区切成高亮 / 常态（发牌台自己管那两块底图的 active）。</summary>
        private Action<bool> _replaceZoneHot;

        // ── 拖拽状态 ─────────────────────────────────────────
        private readonly List<int> _order = new List<int>();
        private readonly List<CardSnapshot> _orderedHand = new List<CardSnapshot>();
        private readonly HashSet<int> _incomingUids = new HashSet<int>();
        private int _dragIndex = -1;
        private Vector2 _dragLocal;
        private int _dragHoverSeat = -1;

        /// <summary>
        /// 跟着一起被拖的牌（2026-10-31：双发防御时两张一起拖）。
        /// 第一位永远是被按住的那张，后面是同一「已选」集合里的其它牌；
        /// <see cref="_dragGroupOffsets"/> 与之一一对应，是相对第一张的横向偏移。
        /// </summary>
        private readonly List<int> _dragGroupUids = new List<int>();
        private readonly List<float> _dragGroupOffsets = new List<float>();

        /// <summary>点了一张牌（BattleUi 判断它对应哪个合法选项）。</summary>
        public event Action<CardView> CardClicked;

        /// <summary>手牌被拖放到某个座位上（0 = 自己 → 防御，1 = 对方 → 进攻）。</summary>
        public event Action<CardView, int> CardDropped;

        /// <summary>
        /// 手牌被拖进了<b>换牌投放区</b>（2026-10-31）—— 发牌台收到就立刻把这张换掉。
        ///
        /// <para>与 <see cref="CardDropped"/> 的区别：那个是「拖进中央判定区 = 出牌」，
        /// 只在进攻 / 防御拍有效；这个只在换牌决策期间有效，方向也是**向上**推到
        /// 手牌区上方那条槽里，两者不会同时成立。</para>
        /// </summary>
        public event Action<CardView> ReplaceDropped;

        public int CardCount
        {
            get { return _shown.Count; }
        }

        /// <summary>是否有牌正被拖着。</summary>
        public bool IsDragging
        {
            get { return _dragIndex >= 0; }
        }

        private void Awake()
        {
            // M7 的 HandRow 上挂着 HorizontalLayoutGroup —— 它会覆盖我们的 anchoredPosition。
            // 这里禁掉它（用 enabled 而不是 Destroy：运行时 Destroy 要等到帧末，期间会闪一下）。
            if (_row != null)
            {
                LayoutGroup lg = _row.GetComponent<LayoutGroup>();
                if (lg != null)
                {
                    lg.enabled = false;
                }
            }

            PurgeStrayRowChildren();
            SetAuraDropHighlight(false);
        }

        /// <summary>
        /// 清理 <c>HandRow</c> 下**不属于本视图账本**的遗留子节点（2026-09-22）。
        ///
        /// <para><b>为什么需要这道兜底</b>：<c>HandRow</c> 在 Prefab 里带过 6 个
        /// 命名为 <c>HandCard</c> 的残留节点 —— 它们是 <see cref="UiKitPreview"/> 的自检样本卡
        /// 被 Unity 连 <c>LayoutGroup</c> 容器一起序列化进资产留下的化石。因为本类的池化
        /// 只认自己的 <see cref="_shown"/> / <see cref="_pool"/>、从不扫描 <c>_row</c> 的实际
        /// 子节点，这些幽灵既不会被回收也不会被摆位：它们带着存盘时的位置与旋转角**永远
        /// 钉在手牌底下**（开局看起来就是「手牌莫名多出好几张」），而且 <c>CardInteractor</c>
        /// 还活着，能被点到、把不存在的牌当成合法选项报给上层。</para>
        ///
        /// <para><b>判据</b>：<c>HandRow</c> 下只该有两种东西 ——
        /// <see cref="_shown"/> 里的（本帧要摆的）与 <see cref="_pool"/> 里的（停用的备用牌）。
        /// 两者之外的一切都由本视图自己 <c>Instantiate</c> 不出来，因此一定是外部污染。
        /// 用「标记集合」而不是「按名字删」：名字是 <c>HandCard</c> 这件事本身就不可靠
        /// （运行时 new 的也叫这个名，按名字删会把真手牌一起删掉）。</para>
        ///
        /// <para><b>池里的牌不能误杀</b>：它们合法地挂在 <c>_row</c> 下且 <c>activeSelf = false</c>，
        /// 所以判据必须是「不在两个集合里」，而不是「不活跃」。</para>
        ///
        /// <para><b>关于 Destroy 的帧末延迟</b>：<see cref="Object.Destroy"/> 要到帧末才真正
        /// 摘除节点 —— 本帧余下的时间里它们仍在 <c>_row</c> 下、也仍会渲染一次。
        /// 这在本处可以接受：<see cref="Awake"/> 跑在任何一帧绘制之前，且构建器预置的
        /// 残留节点是「静态错位」的旧画面，最多闪一帧；用 <c>DestroyImmediate</c> 换取
        /// 「零帧残留」会在真机上抛异常（Unity 明确禁止在运行时对它所属的层级这么做）。
        /// 真正的解法是把污染从资产里删掉（已做），这里只防复发。</para>
        /// </summary>
        private void PurgeStrayRowChildren()
        {
            if (_row == null)
            {
                return;
            }

            for (int i = _row.childCount - 1; i >= 0; i--)
            {
                Transform child = _row.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                CardView card = child.GetComponent<CardView>();

                // 本视图正经持有的牌（_shown 要摆的 / _pool 停用的）：留下。
                // ⚠ 判据必须是「在集合里」而不是「是不是活跃」—— 池里的牌合法地
                //   挂在 _row 下且 activeSelf = false，按活跃度删会把池整个清空。
                if (card != null && (_shown.Contains(card) || _pool.Contains(card)))
                {
                    continue;
                }

                // 走到这里 = 不在账本里。可能是：
                //   a) 残留 HandCard（有 CardView 组件）—— 就是我们要清的污染；
                //   b) 组件被别的构建器抹掉、只剩壳子的残骸（有 HandCardState / CardInteractor 的痕迹）；
                //   c) 别的构建器给 HandArea/HandRow 挂的装饰节点 —— **不能碰**。
                // (c) 的实例是 AuraDropHint，但它挂在 HandArea 上而不是 HandRow 上，
                // 所以这里实际不会遇到；仍然显式区分，免得以后往 HandRow 加装饰时被误删。
                bool looksLikeCard = card != null
                    || child.GetComponent<HandCardState>() != null
                    || child.GetComponent<CardInteractor>() != null;

                if (!looksLikeCard)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }
        }

        // ══════════════════════════════════════════════════════
        //  M15 · 「自己的手牌区」= 光环的落区
        // ══════════════════════════════════════════════════════
        //
        //  用户口径：光环的使用判定「类似出牌」—— 出牌是把手牌拖进画面中央的判定区，
        //  光环是反过来的：把 HudBuff 里的图标拖到**自己的手牌区**。
        //  （M16 一度改成「与出牌同一个判定区」，2026-09-19 又改回手牌区 —— 两者是**不同**的区。）
        //
        //  判定用整块 <c>HandArea</c>（全宽 × 344），不用扇面本身那个「每帧都在动」的弧：
        //    · 弧上每张牌的位置/角度都是平滑趋近的，拿它当判定框会「看着放进去了其实没进」；
        //    · 落区大一点对「拖过来松手」这种操作才跟手 —— 与 M13 判定区同一个思路。
        //  代价是左下角能量球也落在这块里；它不参与光环语义，放着不影响（不做规则判断，铁律 3）。

        /// <summary>某个屏幕点是否落在「自己的手牌区」里。</summary>
        public bool IsInHandArea(Vector2 screenPos)
        {
            RectTransform area = (RectTransform)transform;
            return RectTransformUtility.RectangleContainsScreenPoint(area, screenPos, null);
        }

        //  ── M21：判定区不再对外暴露 ──────────────────────────────
        //
        //  曾经有两个公开成员：`IsInPlayZone(Vector2)` 与 `PlayZone`，
        //  都是给「已准备光环」标记的取消手势用的（标记拖出判定区 = 取消）。
        //  M21 把取消改成「再拖一下那枚标记」（不看落点），光环与判定区彻底解耦，
        //  这两个成员一并删除 —— 留着只会让人以为光环还跟判定区有关系。
        //
        //  判定区本身仍是本视图持有的 _playZone（M13 的判定），只在手牌拖拽出牌时用
        //  （见私有方法 InPlayZone()：它判的是**被拖那张牌的中心**，口径与裸屏幕点不同）。
        //
        //  光环的落区也**不是**判定区，是 IsInHandArea（底部通栏的那块，M19 定稿）。

        /// <summary>
        /// 拖光环经过手牌区时点亮整片提示底（只做视觉暗示）。
        ///
        /// <para><b>只切 activeSelf，不碰颜色</b> —— 提示底的颜色写在
        /// <c>BattleCanvas.prefab</c> 的 <c>HandArea/AuraDropHint → Image.color</c> 上，
        /// 在 Hierarchy 里随时调；代码里给它赋值会把那份调色悄悄冲掉
        /// （2026-09-19 用户明确要求：这类提示的颜色不要用代码写）。</para>
        /// </summary>
        public void SetAuraDropHighlight(bool on)
        {
            if (_auraDropHint != null && _auraDropHint.activeSelf != on)
            {
                _auraDropHint.SetActive(on);
            }
        }

        /// <summary>
        /// 把两侧角色交给本视图做<b>高亮反馈</b>（<see cref="BattleUi"/> 在接线时调）。
        ///
        /// <para>M13 起「能不能出」不再由拖到谁身上判定，改由 <see cref="_playZone"/> 判定区决定；
        /// 这里保留的是「进了判定区 → 把目标角色点亮」的视觉反馈。</para>
        /// </summary>
        public void SetDropTargets(CharacterView self, CharacterView enemy)
        {
            _selfTarget = self;
            _enemyTarget = enemy;
        }

        /// <summary>
        /// 设置当前拖拽出牌的方向（见 <see cref="_dropIntentSeat"/>）。
        /// 传 -1 表示这一阵没有拖拽出牌的语义（比如替换决策、发牌阶段）——
        /// 此时仍可拖拽整理手牌，但出牌判定区不响应。
        /// </summary>
        public void SetDropIntent(int seat)
        {
            _dropIntentSeat = seat;

            // 决策切走的瞬间可能还挂着箭头 / 高亮，一并收掉
            HideArrow();
            ClearTargetHighlight();
        }

        /// <summary>
        /// 接上 / 摘掉<b>换牌投放区</b>（2026-10-31）。
        ///
        /// <para><paramref name="zone"/> 为 null = 这一拍没有换牌语义（关掉整条手势）。
        /// <paramref name="hot"/> 是给外面把投放区切成高亮的回调 —— 本视图不碰
        /// 那块区域的任何视觉，只负责报「牌进没进去」（与判定区同一条分工）。</para>
        /// </summary>
        public void SetReplaceZone(RectTransform zone, Action<bool> hot)
        {
            _replaceZone = zone;
            _replaceZoneHot = hot;
            _dragOverReplace = false;
            if (hot != null)
            {
                hot(false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  滑动选牌（2026-10-30 · ICardSlideHost）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这一次拖拽算不算「滑动选牌」（见 <see cref="ICardSlideHost"/>）。
        ///
        /// <para><b>判据只看方向</b>：横向位移 ≥ <see cref="SlideMinPixels"/> 像素，
        /// 且 ≥ 纵向位移的 <see cref="SlideHorizontalRatio"/> 倍。向上拖（出牌）天然
        /// 不满足这两条，所以「拖进判定区」那条路一点都不受影响。</para>
        ///
        /// <para><b>笔刷状态在这里定死</b>：起点牌<b>还没选</b> → 整次都加选；
        /// 起点牌<b>已经选了</b> → 整次都取消。一次手势只有一种状态 ——
        /// 中途不会因为划到已选中的牌就把它反选掉（那正是「逐张点击」的行为）。</para>
        ///
        /// <para><b>起点牌算第一张</b>：按住那张立即按笔刷状态刷一遍 —— 与斗地主的手感一致。
        /// 上限由 <see cref="PaintLimit"/> 把着：进攻只能打一张，所以从第二张起就不再生效。</para>
        /// </summary>
        public bool TryBeginSlide(CardView card, Vector2 pressScreenPos, Vector2 screenPos)
        {
            // ⚠ 2026-10-31 临时口径：滑动选牌整体关掉，一切手势先按「拖动」处理
            //   （开关见 _slideSelectEnabled，恢复滑动 = 改回 true）。
            if (!_slideSelectEnabled)
            {
                return false;
            }

            if (card == null || !card.HasCard)
            {
                return false;
            }

            int index = _shown.IndexOf(card);
            if (index < 0 || card.PresentationHidden)
            {
                return false;
            }

            // ⚠ 本拍不合法（被压暗）的牌**不**在这里早退：横向滑动照样吃掉这个手势，
            // 否则它会落到「拖拽」那条路 —— 玩家看到的是「把一张不能出的牌拖着满屏跑」。
            // 「一张都不刷上」由 SlideTouch 自己把关（它逐张查 _playable）。
            Vector2 delta = screenPos - pressScreenPos;
            float ax = Mathf.Abs(delta.x);
            float ay = Mathf.Abs(delta.y);
            if (ax < SlideMinPixels || ax < ay * SlideHorizontalRatio)
            {
                return false;
            }

            // 滑动一旦成立，任何残留的拖拽姿态都必须先收掉 —— 牌回扇形再刷。
            ResetDragPose();

            _sliding = true;
            _slideDead = false;
            _slideTouched.Clear();
            _slideLastScreen = pressScreenPos;
            _hoverIndex = -1;
            HidePeek();

            // 笔刷状态 = 起点牌现在的状态取反（未选 → 加选；已选 → 取消）。
            _slideErase = IsMarked(card.Card.Uid);

            // 与拖牌同一条口径：加选笔刷一开始，上一次点击留下的单选辉光就该让位给这次手势。
            if (!_slideErase)
            {
                _selectedUid = -1;
            }

            _slideTouched.Add(card.Card.Uid);
            SlideTouch(card);
            return true;
        }

        /// <summary>终止本次滑动（决策收尾 / 手牌重绑）。之后同一手势里划到的牌不再生效。</summary>
        public void EndSlide()
        {
            _sliding = false;
            _slideErase = false;
            _slideDead = true;
            _slideTouched.Clear();

            // 决策收尾 / 手牌重绑的同一刻：拖拽姿态也一并收掉（牌必须回到扇形）。
            ResetDragPose();
        }

        /// <summary>
        /// 把「正在拖动」这套姿态整个收掉：牌回扇形、箭头与目标高亮一并清。
        ///
        /// <para><b>为什么要有这个收口</b>（用户 2026-10-31 口径）：「手牌里绝不该有牌是正着的 ——
        /// 正着只属于拖动；没拖出去、拖出去之后回来，都必须恢复原来的倾斜弧度」。
        /// 原来只有 <see cref="EndDrag"/> 一条路清 <c>_dragIndex</c>，一旦 DragEnd 没送到
        ///（手势被别的层吞掉 / 指针在窗口外抬起），那张牌就永远停在正立姿态上，
        /// 而且还能继续划选别的牌。</para>
        /// </summary>
        private void ResetDragPose()
        {
            _dragIndex = -1;
            _dragHoverSeat = -1;
            _dragGroupUids.Clear();
            _dragGroupOffsets.Clear();
            ClearReplaceHover();
            HideArrow();
            ClearTargetHighlight();
        }

        /// <summary>
        /// 抬起事件的兜底（2026-10-31）：<c>Release</c> 一定在 <c>OnPointerUp</c> 里送到，
        /// 而 <c>DragEnd</c> 是同一个 <c>EventSystem.Update</c> 里紧随其后派的 ——
        /// 到了 LateUpdate 还挂着拖拽姿态，就说明这一手 DragEnd 丢了，必须收掉。
        ///
        /// <para>为什么不直接在 <c>Release</c> 里收：Button 的 onClick 与 DragEnd 都排在
        /// <c>OnPointerUp</c> <b>之后</b>，那一帧立刻清 <c>_dragIndex</c> 会把
        /// 「拖进判定区松手出牌」整条路废掉。挪到 LateUpdate 就只剩「真的没送 DragEnd」这一种情况。</para>
        /// </summary>
        private void LateUpdate()
        {
            if (!_releasePending)
            {
                return;
            }

            _releasePending = false;

            if (_dragIndex >= 0)
            {
                ResetDragPose();
            }
        }

        /// <summary>
        /// 滑动中：把「上一条指针位置 → 当前位置」这条线段扫过的牌依次刷过去。
        ///
        /// <para><b>为什么不是只看当前这一点</b>：<c>OnDrag</c> 一帧只来一次，
        /// 快速划过时两帧之间能跨过两三张牌 —— 只测当前点等于「跳着选」，
        /// 玩家看到的是「我明明划过了它却没选中」。这里按 <see cref="SlideSamplePixels"/>
        /// 补点，整条笔迹都算数。</para>
        ///
        /// <para>去重交给 <see cref="_slideTouched"/>：同一张牌一次手势只处理一次，
        /// 所以来回划不会把已经刷上的状态又刷回去。</para>
        /// </summary>
        private void Slide(Vector2 screenPos)
        {
            if (!_sliding || _slideDead)
            {
                return;
            }

            Vector2 from = _slideLastScreen;
            _slideLastScreen = screenPos;

            float distance = Vector2.Distance(from, screenPos);
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / SlideSamplePixels), 1, SlideMaxSamples);

            for (int s = 1; s <= steps; s++)
            {
                Vector2 point = Vector2.Lerp(from, screenPos, s / (float)steps);
                CardView card = CardAt(point);
                if (card == null || !card.HasCard)
                {
                    continue;
                }

                if (!_slideTouched.Add(card.Card.Uid))
                {
                    continue;
                }

                SlideTouch(card);

                // 这一刷把决策交出去了（单选拍点一下就提交）→ 剩下的采样点不用再算。
                if (_slideDead)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 笔尖刷到一张牌 → 把它<b>置成</b>本次笔刷的状态（不是切换）。
        ///
        /// <para>三条规矩：</para>
        /// <list type="number">
        /// <item>已经是目标状态的牌不动 —— 划回来不会反选，这是「画刷」与「逐张点击」的分界；</item>
        /// <item>「加选」笔刷碰上已达上限的拍 → 直接不理，也<b>不</b>顶掉前面已选的
        ///       （用户口径：「最多扫到三张牌之后就不会再额外增加了」）；</item>
        /// <item>「取消」笔刷只取消、不新增（取消不受上限约束）。</item>
        /// </list>
        /// </summary>
        private void SlideTouch(CardView card)
        {
            if (card == null || _slideDead || !card.HasCard)
            {
                return;
            }

            int uid = card.Card.Uid;

            if (_restrict && !_playable.Contains(uid))
            {
                return;
            }

            bool marked = IsMarked(uid);

            if (_slideErase)
            {
                if (marked)
                {
                    SetMarked(uid, false);
                }

                return;
            }

            if (marked || MarkedCount >= PaintLimit)
            {
                return;
            }

            SetMarked(uid, true);
        }

        /// <summary>这张牌现在是不是「已选中」（三种拍各有各的账本）。</summary>
        private bool IsMarked(int uid)
        {
            if (uid < 0)
            {
                return false;
            }

            if (_multiMode)
            {
                return _multi.Contains(uid);
            }

            if (_accumulatePick)
            {
                return _picked.Contains(uid);
            }

            return _selectedUid == uid;
        }

        /// <summary>这一拍已经选了几张。</summary>
        private int MarkedCount
        {
            get
            {
                if (_multiMode)
                {
                    return _multi.Count;
                }

                if (_accumulatePick)
                {
                    return _picked.Count;
                }

                return _selectedUid >= 0 ? 1 : 0;
            }
        }

        /// <summary>
        /// 这一拍最多能同时选几张：发牌台 / 选牌弹窗按引擎的 <c>MaxSelect</c> 塞进来
        /// （开局 3 / 补牌 1 / 磁暴·充能 1~N）；没塞过就按本视图的模式兜底 ——
        /// 双发两张、其余（进攻 / 单防）只能一张。
        /// </summary>
        private int PaintLimit
        {
            get
            {
                if (_paintLimit > 0)
                {
                    return _paintLimit;
                }

                return _accumulatePick ? MaxPicked : 1;
            }
        }

        /// <summary>
        /// 把一张牌置成「选中 / 未选中」。
        ///
        /// <para>有 <see cref="_paintSink"/> 时交给上层：发牌台 / 选牌弹窗手里那份
        /// <c>_selected</c> 才是真源，它们改完会回写 <see cref="_multi"/> ——
        /// 本视图不另存一份，免得两处账本对不上。</para>
        /// </summary>
        private void SetMarked(int uid, bool on)
        {
            if (uid < 0)
            {
                return;
            }

            if (_paintSink != null && _multiMode)
            {
                _paintSink(uid, on);
                return;
            }

            if (_multiMode)
            {
                if (on)
                {
                    _multi.Add(uid);
                }
                else
                {
                    _multi.Remove(uid);
                }

                return;
            }

            if (_accumulatePick)
            {
                if (on)
                {
                    if (!_picked.Contains(uid))
                    {
                        AddPick(uid);
                    }
                }
                else
                {
                    _picked.Remove(uid);
                }

                RaiseCardClicked(uid);
                return;
            }

            _selectedUid = on ? uid : -1;

            // 取消不补通知：单选拍（选目标 / 光环…）的「点一下」本身就是提交，
            // 把「取消选中」也发过去等于替玩家把这张牌交出去。
            if (on)
            {
                RaiseCardClicked(uid);
            }
        }

        /// <summary>
        /// 刷过之后补一次「这张牌变了」的通知 —— 双发靠它刷提示条张数；
        /// 单选拍本来就靠点击提交，也走这里。
        /// </summary>
        private void RaiseCardClicked(int uid)
        {
            if (CardClicked == null)
            {
                return;
            }

            CardView view = FindCard(uid);
            if (view != null)
            {
                CardClicked(view);
            }
        }

        /// <summary>屏幕点上压着的那张手牌（没有 → null）。倒序找 = 取画在最上面的那张。</summary>
        private CardView CardAt(Vector2 screenPos)
        {
            for (int i = _shown.Count - 1; i >= 0; i--)
            {
                CardView v = _shown[i];
                if (v == null || !v.isActiveAndEnabled || !v.HasCard)
                {
                    continue;
                }

                RectTransform rt = (RectTransform)v.transform;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                {
                    return v;
                }
            }

            return null;
        }

        // ══════════════════════════════════════════════════════
        //  绑定
        // ══════════════════════════════════════════════════════

        /// <summary>按快照重建手牌。会尽量复用同一张牌已有的 <see cref="CardView"/>（按 Uid 配对），避免闪。</summary>
        public void Bind(IReadOnlyList<CardSnapshot> hand, HashSet<int> playableUids)
        {
            // 手牌重绑（新一拍 / 牌进出）→ 上一下滑动手势作废：
            // 它对应的是旧的那一排牌，继续按坐标判定只会划到不该选的牌。
            EndSlide();

            CardView dragged = _dragIndex >= 0 && _dragIndex < _shown.Count ? _shown[_dragIndex] : null;
            int draggedUid = dragged == null ? -1 : dragged.Card.Uid;
            _incomingUids.Clear();
            if (hand != null)
                for (int i = 0; i < hand.Count; i++) _incomingUids.Add(hand[i].Uid);
            _order.RemoveAll(uid => !_incomingUids.Contains(uid));
            if (hand != null)
                for (int i = 0; i < hand.Count; i++)
                    if (!_order.Contains(hand[i].Uid)) _order.Add(hand[i].Uid);
            _orderedHand.Clear();
            for (int i = 0; i < _order.Count; i++)
                for (int h = 0; hand != null && h < hand.Count; h++)
                    if (hand[h].Uid == _order[i]) { _orderedHand.Add(hand[h]); break; }
            hand = _orderedHand;
            _reuse.Clear();

            if (hand != null && hand.Count > 0)
            {
                for (int i = 0; i < _shown.Count; i++)
                {
                    CardView v = _shown[i];
                    if (v != null && v.HasCard)
                    {
                        for (int h = 0; h < hand.Count; h++)
                        {
                            if (hand[h].Uid == v.Card.Uid && !_reuse.ContainsKey(hand[h].Uid))
                            {
                                _reuse.Add(hand[h].Uid, v);
                                break;
                            }
                        }
                    }
                }
            }

            // 没被复用的收回池子
            for (int i = 0; i < _shown.Count; i++)
            {
                CardView v = _shown[i];
                if (v != null && (!v.HasCard || !_reuse.ContainsKey(v.Card.Uid)))
                {
                    v.Clear();
                    v.gameObject.SetActive(false);
                    _pool.Add(v);
                }
            }

            _shown.Clear();
            _dragIndex = -1;
            _dragHoverSeat = -1;
            _dragGroupUids.Clear();
            _dragGroupOffsets.Clear();
            ClearTargetHighlight();

            if (hand != null)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    CardView v;
                    if (!_reuse.TryGetValue(hand[i].Uid, out v))
                    {
                        v = TakeFromPool();
                        HandCardState added = InstantiateState(v);
                        added.Entering = Application.isPlaying;
                        added.EnterDelay = i * 0.035f;
                    }

                    v.Bind(hand[i], CardView.ViewMode.Hand, i);
                    v.gameObject.SetActive(true);

                    // 显式定序：池化复用时 SetAsLastSibling 会把顺序打乱，
                    // 而扇面里相邻的牌是会互相压的 —— 顺序不定就会出现「同一手牌、两次运行谁压谁不一样」。
                    v.transform.SetAsLastSibling();
                    _shown.Add(v);
                }
            }

            _dragIndex = dragged != null && dragged.Card.Uid == draggedUid ? _shown.IndexOf(dragged) : -1;
            if (_dragIndex >= 0) dragged.transform.SetAsLastSibling();
            else HideArrow();
            SetPlayable(playableUids);

            // 待选的牌可能已经不在手上了
            if (_selectedUid >= 0 && !ContainsUid(_selectedUid))
            {
                _selectedUid = -1;
            }

            // 多选标记同理：换牌之后被换掉的那张已经不在手上，标记要跟着丢掉，
            // 否则它会一直占着「已选 N」的名额，而屏幕上根本找不到那张牌。
            if (_multi.Count > 0)
            {
                _multi.RemoveWhere(u => IndexOfUid(u) < 0);
            }

            // 双发的「已选」同理：换手牌之后被换掉的那张已经不在手上，标记要跟着丢掉，
            // 否则它会一直占着「已选 N」的名额，而屏幕上根本找不到那张牌。
            if (_picked.Count > 0)
            {
                _picked.RemoveAll(u => IndexOfUid(u) < 0);
            }
        }

        /// <summary>哪些牌当前可以点（Uid 集合）。传 null = 不限制；传空集 = 一张都不允许点。</summary>
        public void SetPlayable(HashSet<int> playableUids)
        {
            _restrict = playableUids != null;
            _playable.Clear();

            if (playableUids != null)
            {
                foreach (int uid in playableUids)
                {
                    _playable.Add(uid);
                }
            }

            for (int i = 0; i < _shown.Count; i++)
            {
                CardView v = _shown[i];
                bool can = !_restrict || _playable.Contains(v.Card.Uid);
                v.SetInteractable(can);
                v.SetDimmed(_dimUnplayable && !can);
            }
        }

        /// <summary>置亮某张牌（该牌就是本次决策的答案）。</summary>
        public void SetSelectedUid(int uid)
        {
            _selectedUid = uid;
        }

        /// <summary>
        /// 切到 / 切出「点击累积标记」模式（双发防御这一拍，2026-10-08）。
        ///
        /// <para>开启后：「点一下」不再走单选框（<see cref="_selectedUid"/>，点第二张就把第一张换掉），
        /// 改成加减 <see cref="_picked"/> 里的标记；拖拽<b>只</b>会往里加（<see cref="BeginDrag"/>
        /// 清的仍然是点击残留），所以「凑不够两张、牌弹回来」也不会把玩家已选的牌取消掉 ——
        /// 这正是用户 2026-10-08 要的「防御失败、弹回去，但并不取消这张牌的选择」。</para>
        /// </summary>
        public void SetAccumulatePick(bool on)
        {
            _accumulatePick = on;

            if (!on)
            {
                _picked.Clear();
            }
        }

        /// <summary>这一拍已经标记了几张。</summary>
        public int PickCount
        {
            get { return _picked.Count; }
        }

        /// <summary>这一拍标记的牌（顺序 = 标记的先后，最多 <see cref="MaxPicked"/> 张）。</summary>
        public IReadOnlyList<int> PickedUids
        {
            get { return _picked.AsReadOnly(); }
        }

        /// <summary>点一下：加一张 / 取消那一张（满两张后加新的会顶掉最早那张）。</summary>
        public void TogglePick(int uid)
        {
            if (uid < 0)
            {
                return;
            }

            if (_picked.Remove(uid))
            {
                return;
            }

            AddPick(uid);
        }

        /// <summary>
        /// 把一张牌并进「已选」（拖进判定区的那张走这里）。已经在集合里就什么都不做。
        /// </summary>
        public void AddPick(int uid)
        {
            if (uid < 0 || _picked.Contains(uid))
            {
                return;
            }

            while (_picked.Count >= MaxPicked)
            {
                _picked.RemoveAt(0);
            }

            _picked.Add(uid);
        }

        /// <summary>清空这一拍的「已选」（提交 / 换拍 / 收尾）。</summary>
        public void ClearPick()
        {
            _picked.Clear();
        }

        /// <summary>
        /// 把画刷的「选中 / 取消」接到上层的选中集合上（发牌台 / 选牌弹窗）。
        ///
        /// <para><paramref name="sink"/> 为 null = 解除，之后本视图自己管账本
        /// （单选拍 / 双发拍就是这么走的）。<paramref name="limit"/> ≤ 0 = 由
        /// <see cref="PaintLimit"/> 按模式兜底。</para>
        /// </summary>
        public void SetPaintSink(Action<int, bool> sink, int limit)
        {
            _paintSink = sink;
            _paintLimit = Mathf.Max(0, limit);
        }

        /// <summary>
        /// 给手牌里的每张牌下一个「力量加值预览」（M24 #4）。
        ///
        /// <para><paramref name="select"/> 由调用方（<see cref="BattleUi"/>）决定这张牌该不该
        /// 预览、以及预览多少 —— 因为那条判断依赖「本拍在问进攻还是防御」与「这张牌打不打得出」，
        /// 两者都只有 <see cref="BattleUi"/> 手上有（铁律 3：本视图不碰规则）。</para>
        ///
        /// <para><b>为什么要在 Bind 之后再下一次</b>：<see cref="CardView.Bind"/> 会把力量文本
        /// 重置成卡面原值（那是每张卡绑定时必须做的），所以预览必须排在它之后 ——
        /// 由 <c>BattleUi.RefreshAll</c> 在 <c>_hand.Bind(...)</c> 之后调用本方法。</para>
        /// </summary>
        public void ApplyPowerPreview(Func<CardSnapshot, int> select)
        {
            for (int i = 0; i < _shown.Count; i++)
            {
                CardView v = _shown[i];
                if (v == null || !v.HasCard)
                {
                    continue;
                }

                int bonus = select == null ? 0 : select(v.Card);
                v.SetPowerPreview(bonus < 0 ? 0 : bonus);
            }
        }

        /// <summary>
        /// 切到 / 切出<strong>多选模式</strong>（M9 发牌替换）。
        ///
        /// <para>单选模式下手牌只有一张能被置亮（<c>_selectedUid</c>）；替换是多选决策，
        /// 需要同时标好几张，所以另开一个集合。两种模式互不干扰：切出时把标记清掉，
        /// 免得下次进入普通对局还留着上一局的辉光。</para>
        /// </summary>
        public void SetMultiSelect(bool on)
        {
            _multiMode = on;

            if (!on)
            {
                _multi.Clear();
            }
        }

        /// <summary>设置被标记的牌（Uid 集合）。传 null 等于清空。</summary>
        public void SetMultiSelected(IReadOnlyList<int> uids)
        {
            _multi.Clear();

            if (uids != null)
            {
                for (int i = 0; i < uids.Count; i++)
                {
                    _multi.Add(uids[i]);
                }
            }
        }

        public void ClearSelection()
        {
            // 决策已经交出去了 → 本次滑动到此为止。否则手指还在划，
            // 会把下一拍的牌也一路点亮（而那一拍玩家根本没在选牌）。
            EndSlide();

            _selectedUid = -1;
            _picked.Clear();
            _multi.Clear();
            HidePeek();
        }

        // ══════════════════════════════════════════════════════
        //  排布（每帧趋近目标）
        // ══════════════════════════════════════════════════════

        private void Update()
        {
            if (_shown.Count == 0)
            {
                return;
            }

            float k = 1f - Mathf.Exp(-SmoothSpeed * Time.unscaledDeltaTime);
            int n = _shown.Count;

            float w = UiLayout.HandCardWidth;

            // ── 扇形参数（M13：真圆弧）──────────────────────────
            //  所有牌的底边中点落在同一个圆上，旋转角 = 圆心角；
            //  半径由「横向半跨距 S」与「半张角 A」反解：R = S / sin(A)。
            //  这样横跨宽度（pitch 决定）与摆角（总角度决定）一旦给定，
            //  弧的深浅（下沉量）就是被动量 —— 想调出「弧很平但牌转很凶」这种
            //  自相矛盾的形状都做不到。参数含义与预算见 UiLayout.HandFanTotalAngle。
            float tMax = n > 1 ? (n - 1) * 0.5f : 0f;
            float step = n > 1
                ? Mathf.Min(UiLayout.HandFanMaxStep, UiLayout.HandFanTotalAngle / (n - 1))
                : 0f;
            float aMaxRad = step * tMax * Mathf.Deg2Rad;

            // 横向步进：常规 = 卡宽 + 间隙；牌多到会压到两侧安全区时按可用宽度收紧
            float pitch = w + UiLayout.HandCardSpacing;
            float usable = UiLayout.ReferenceWidth - UiLayout.HandFanSideMargin * 2f;

            if (n > 1)
            {
                float fit = (usable - w) / (n - 1f);
                if (fit < pitch)
                {
                    pitch = fit;
                }
            }

            float halfSpan = pitch * tMax;                                   // 最外侧牌的圆心 x
            float radius = aMaxRad > 1e-5f ? halfSpan / Mathf.Sin(aMaxRad) : 0f;

            // 卡片 pivot 在底边中点：转 θ 之后最低的那个角比底边中点低 (宽/2)·sinθ。
            // 整排抬到「最低角 = 手牌底边距」，即中间那张的底边 = 下沉量 + 这份额外压低。
            float sink = radius > 0f ? radius * (1f - Mathf.Cos(aMaxRad)) : 0f;
            float baseY = sink + (w * 0.5f) * Mathf.Sin(aMaxRad);

            bool canHover = !_restrict || _playable.Count > 0;

            for (int i = 0; i < n; i++)
            {
                CardView v = _shown[i];
                RectTransform rt = (RectTransform)v.transform;
                HandCardState st = v.GetComponent<HandCardState>();
                if (st == null)
                {
                    st = InstantiateState(v);
                }

                float t = i - tMax;
                float kk = tMax <= 0f ? 0f : t / tMax;

                // 圆上的位置：x = R·sinφ（φ = 该牌的圆心角），y = 中心牌底边 − 下沉量
                float phi = aMaxRad * kk;
                float targetX = radius > 0f ? radius * Mathf.Sin(phi) : pitch * t;
                float targetY = baseY - radius * (1f - Mathf.Cos(phi));
                float targetRot = -aMaxRad * kk * Mathf.Rad2Deg;
                float targetScale = 1f;

                // 拖动中的这一组：「正在拖动」是第三种状态 —— 摆正（0°）+ 放大 + 跟手；
                // 与「已选中」（仍在扇形里、保留倾斜）区分开。双发时这组里有两张，
                // 按原槽位差横着排开一起搬（见 BuildDragGroup）。
                bool dragPose = _dragIndex >= 0 && _dragIndex < _shown.Count;
                int groupAt = dragPose && _dragGroupUids.Count > 0
                    ? _dragGroupUids.IndexOf(v.Card.Uid)
                    : -1;
                bool dragging = dragPose && (groupAt >= 0 || i == _dragIndex);

                if (dragging)
                {
                    targetX = _dragLocal.x + (groupAt >= 0 ? _dragGroupOffsets[groupAt] : 0f);
                    targetY = _dragLocal.y;
                    targetRot = 0f;
                    targetScale = UiLayout.HandDragScale;

                    // 拖拽中这张牌的辉光由「正在被拖」这件事决定，而不是靠别处残留的状态
                    //（2026-09-23 修：原先这个分支完全不碰 SetSelected，辉光只能靠上一帧的
                    // 旧状态撑着 —— 于是「拖动中的卡发光」看着像实现了，其实是点击残留；
                    // 一旦残留被清掉，它就会变成拖拽中反而不亮）。
                    // 拖拽结束的下一帧会走下面 else 分支按 marked 重算，辉光自然收掉。
                    v.SetSelected(true);
                }
                else
                {
                    if (i == _hoverIndex && canHover)
                    {
                        targetY += UiLayout.HandHoverRaise;
                        targetScale = UiLayout.HandHoverScale;
                    }

                    // 单选模式看 _selectedUid（普通进攻 / 防御拍，一次只亮一张）；
                    // 多选模式看 _multi（M9 替换 / 选牌弹窗可同时标多张）；
                    // _picked 是双发防御那一拍的「已选」。三本账的统一入口就是 IsMarked ——
                    // 画刷也读它，两边必须同一套判据，否则会出现「亮着但划不动 / 划得动却不亮」。
                    bool marked = IsMarked(v.Card.Uid);

                    if (marked)
                    {
                        targetY += UiLayout.HandSelectRaise;
                        targetScale = UiLayout.HandSelectScale;
                    }

                    // 每帧都喊也没关系 —— CardView.SetSelected 内部只在真的翻转时才 SetActive。
                    // 反过来把「亮没亮」记在手牌状态里是不行的：池化复用时 Clear() 会把辉光关掉，
                    // 而那份记录还留着 true，之后这张牌就再也亮不起来了。
                    v.SetSelected(marked);
                }

                if (st.First)
                {
                    st.X = targetX;
                    st.Y = targetY - (st.Entering ? UiLayout.HandCardHeight : 0f);
                    st.Rot = targetRot;
                    st.Scale = st.Entering ? 0.85f : targetScale;
                    st.First = false;
                }
                else if (st.Entering && st.EnterDelay > 0f)
                {
                    st.EnterDelay -= Time.unscaledDeltaTime;
                }
                else
                {
                    st.Entering = false;
                    st.X = Mathf.Lerp(st.X, targetX, k);
                    st.Y = Mathf.Lerp(st.Y, targetY, k);
                    st.Rot = Mathf.Lerp(st.Rot, targetRot, k);
                    st.Scale = Mathf.Lerp(st.Scale, targetScale, k);
                }

                rt.anchoredPosition = new Vector2(st.X, st.Y);
                rt.localRotation = Quaternion.Euler(0f, 0f, st.Rot);
                rt.localScale = new Vector3(st.Scale, st.Scale, 1f);
            }
        }

        /// <summary>立即把排布落到终态（跳过趋近，用于切场景 / 截图）。</summary>
        public void SnapLayout()
        {
            for (int i = 0; i < _shown.Count; i++)
            {
                HandCardState st = _shown[i].GetComponent<HandCardState>();
                if (st != null)
                {
                    st.First = true;
                    st.Entering = false;
                    st.EnterDelay = 0f;
                }
            }

            Update();
        }

        // ══════════════════════════════════════════════════════
        //  手势（ICardGestureHost）
        // ══════════════════════════════════════════════════════

        public void OnCardGesture(CardView card, CardGesture gesture, Vector2 screenPos)
        {
            if (card == null)
            {
                return;
            }

            switch (gesture)
            {
                case CardGesture.HoverEnter:
                    _hoverIndex = _shown.IndexOf(card);
                    break;

                case CardGesture.HoverExit:
                    if (_hoverIndex == _shown.IndexOf(card))
                    {
                        _hoverIndex = -1;
                    }

                    // 「挪开就收起」
                    HidePeek();
                    break;

                case CardGesture.Press:
                    break;

                case CardGesture.LongPress:
                    ShowPeek(card);
                    break;

                case CardGesture.Release:
                    HidePeek();
                    // 抬手了 → 记一笔，LateUpdate 里核对 DragEnd 有没有真的送到。
                    _releasePending = true;
                    break;

                case CardGesture.DragBegin:
                    BeginDrag(card);
                    break;

                case CardGesture.Drag:
                    MoveDrag(screenPos);
                    break;

                case CardGesture.DragEnd:
                    EndDrag(screenPos);
                    break;

                case CardGesture.SlideBegin:
                    // 起点牌已经在 TryBeginSlide 里处理过了（那时才拿得到按下位置）
                    break;

                case CardGesture.Slide:
                    Slide(screenPos);
                    break;

                case CardGesture.SlideEnd:
                    _sliding = false;
                    _slideErase = false;
                    _slideTouched.Clear();
                    break;
            }
        }

        private void BeginDrag(CardView card)
        {
            HidePeek();

            int idx = _shown.IndexOf(card);

            // 飞行动画中的牌暂不接收拖拽，其他牌均可整理。
            if (idx < 0 || card.PresentationHidden)
            {
                _dragIndex = -1;
                return;
            }

            // 整理手牌始终可用；能否出牌由 CanDropDraggedCard 单独判定。

            // 拖拽一开始就收回「点击残留」的辉光（2026-09-23 修）。
            //
            // ⚠ 用户报的现象：第 1 张被<b>点</b>过的卡会一直亮着 —— 点击走
            //   OnCardClicked → _selectedUid = uid，而 _selectedUid 只在决策收尾
            //   （BattleUi.ClearDecision → ClearSelection）才清；「拖一下再挪个位置」
            //   这种不提交决策的操作不会碰它。于是后面拖别的卡时，Update 里那张旧卡
            //   仍然满足 marked → 辉光留在原处，看着像「提示不会转移」。
            //
            // 语义上也对：手一旦开始拖，这一拍的「选中」就该由拖拽接管，
            // 上一次点击的残留不该再有发言权。
            _selectedUid = -1;

            _dragIndex = idx;
            _dragHoverSeat = -1;
            BuildDragGroup(card, idx);

            // 把指针位置先按「卡原位」初始化一下，免得第一帧跳到别处
            _dragLocal = ((RectTransform)card.transform).anchoredPosition;

            // 拖起来的那张要盖在最上面
            card.transform.SetAsLastSibling();
        }

        /// <summary>
        /// 组出「这次拖拽要一起搬走的那几张」。
        ///
        /// <para><b>为什么需要它</b>（用户 2026-10-31 口径）：「在防御双发时，拖拽时应当是
        /// 这两张牌一起被拖动」。双发本来就要求交出两张 —— 已经选好的那两张如果只跟着拖一张，
        /// 看着就像「另一张被丢下了」，玩家会以为要重选。</para>
        ///
        /// <para><b>只有双发这一拍才成群</b>：那个「已选」集合本来就是「本拍要一起交出去的牌」。
        /// 换牌 / 普通单选拖拽只搬一张 —— 别的时候把旁边的牌一起拖走只会让人以为排序乱了。</para>
        ///
        /// <para>偏移取<b>扇形原槽位</b>的横向差：两张牌并排保持原来的左右关系，
        /// 拖动时都摆正、同高；松手（没进判定区）又各自回到扇形、恢复倾斜。</para>
        /// </summary>
        private void BuildDragGroup(CardView card, int index)
        {
            _dragGroupUids.Clear();
            _dragGroupOffsets.Clear();

            _dragGroupUids.Add(card.Card.Uid);
            _dragGroupOffsets.Add(0f);

            // 组员从哪来：
            //   · 双发防御拍 —— `_picked`（点选累积，2026-09-30）
            //   · 换牌拍    —— `_multi`（手牌上已标记要换掉的那几张，2026-10-31 第二轮）
            // 两处都要求「被按住这张本身在组里」：拖一张没标记的牌，就只拖它自己。
            List<int> members = null;
            if (_accumulatePick && _picked.Count >= 2 && _picked.Contains(card.Card.Uid))
            {
                members = _picked;
            }
            else if (_multiMode && _multi.Count >= 2 && _multi.Contains(card.Card.Uid))
            {
                members = new List<int>(_multi);
            }

            if (members == null)
            {
                return;
            }

            float baseX = FanSlotX(index, _shown.Count);

            for (int i = 0; i < members.Count; i++)
            {
                int uid = members[i];
                if (uid == card.Card.Uid)
                {
                    continue;
                }

                int at = IndexOfUid(uid);
                if (at < 0)
                {
                    continue;
                }

                _dragGroupUids.Add(uid);
                _dragGroupOffsets.Add(FanSlotX(at, _shown.Count) - baseX);

                // 同组的一起抬到最上层（被按住那张随后再抬一次，压在最上面）
                _shown[at].transform.SetAsLastSibling();
            }
        }

        private void MoveDrag(Vector2 screenPos)
        {
            if (_dragIndex < 0 || _row == null)
            {
                return;
            }

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_row, screenPos, null, out local))
            {
                return;
            }

            // 指针坐在卡的下沿往上 DragLiftAbove 处 —— 手在卡下面，不挡卡面
            _dragLocal = new Vector2(local.x, local.y - UiLayout.DragLiftAbove);

            // 判定区说了算：进了区才算「要出牌」，出箭头 + 点亮目标；
            // 没进区什么都不发生，松手牌自己滑回扇形。
            _dragHoverSeat = CanDropDraggedCard() && InPlayZone() ? _dropIntentSeat : -1;

            // 换牌拍（2026-10-31）：牌压进手牌区上方那条槽 → 点亮它，松手就换掉。
            // 与出牌互斥 —— 换牌拍的 _dropIntentSeat 恒为 -1，_dragHoverSeat 也就恒为 -1。
            _dragOverReplace = _replaceZone != null && InReplaceZone();
            if (_replaceZoneHot != null)
            {
                _replaceZoneHot(_dragOverReplace);
            }

            if (_dragHoverSeat < 0 && !_dragOverReplace && IsInHandArea(screenPos)) ReorderAtPointer();
            ApplyTargetHighlight(_dragHoverSeat);
            UpdateArrow();
        }

        private void EndDrag(Vector2 screenPos)
        {
            if (_dragIndex < 0)
            {
                return;
            }

            MoveDrag(screenPos);
            CardView card = _dragIndex < _shown.Count ? _shown[_dragIndex] : null;
            bool toReplace = _dragOverReplace && _replaceZone != null;
            bool dropped = CanDropDraggedCard() && InPlayZone();

            _dragIndex = -1;
            _dragHoverSeat = -1;
            _dragGroupUids.Clear();
            _dragGroupOffsets.Clear();
            ClearReplaceHover();
            HideArrow();
            ClearTargetHighlight();

            if (card == null)
            {
                return;
            }

            // 换牌拍：牌落在投放区里 → 报给发牌台去换掉它。
            // 这一条排在出牌前面：换牌拍的 _dropIntentSeat 是 -1，两条路本来就不会同时成立，
            // 排前面只是让「换牌」这件事在代码里读起来优先。
            if (toReplace)
            {
                if (ReplaceDropped != null)
                {
                    ReplaceDropped(card);
                }

                RestoreSiblingOrder();
                return;
            }

            // 没拖进判定区就保留调整后的排序，平滑落回新的扇形槽位。
            // （这里**不判断**「能不能打到对方」，那是 BattleUi 拿 Options 去比对的事。）
            if (dropped && CardDropped != null)
            {
                CardDropped(card, _dropIntentSeat);
            }

            // 拖完之后鼠标往往还停在原位，重新算一次悬停，避免卡在「悬停放大」的中间态
            _hoverIndex = -1;
            RestoreSiblingOrder();
        }

        // ══════════════════════════════════════════════════════
        //  判定区（M13）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 被拖的那张牌是否已经进入<strong>出牌判定区</strong>。
        ///
        /// <para>判的是<b>牌的落点</b>（指针位置换算出的牌底边 + 半张卡高），不是指针本身 ——
        /// 玩家的直觉是「把这张牌拖上去」，而指针被刻意压在牌下面
        /// （<see cref="UiLayout.DragLiftAbove"/>），直接拿指针判会有一段「看着牌已经进区了、
        /// 其实还差一截」的错位。</para>
        ///
        /// <para><b>为什么不用牌的实时视觉中心</b>：排布是每帧平滑趋近的
        /// （<see cref="SmoothSpeed"/>），快速一甩时视觉中心要晚几帧才到；
        /// 用视觉中心判定会出现「甩进区里松手却没反应」。落点是<b>逻辑位置</b>，
        /// 与指针同步、零滞后，手感与「松手即出」的承诺一致。</para>
        /// </summary>
        private bool CanDropDraggedCard()
        {
            return _dropIntentSeat >= 0 && _dragIndex >= 0 && _dragIndex < _shown.Count
                && (!_restrict || _playable.Contains(_shown[_dragIndex].Card.Uid));
        }

        private void ReorderAtPointer()
        {
            // 双发那两张是「一起搬」的，途中挪排序只会把它们的相对位置打散。
            if (_dragGroupUids.Count > 1)
            {
                return;
            }

            CardView dragged = _shown[_dragIndex];
            int target = _dragIndex;
            // Compare against stable fan slots, not the moving neighbours.
            for (int i = 0; i < _shown.Count; i++)
            {
                float x = FanSlotX(i, _shown.Count);
                if (i < _dragIndex && _dragLocal.x < x - 12f) { target = i; break; }
                if (i > _dragIndex && _dragLocal.x > x + 12f) target = i;
            }
            if (target == _dragIndex) return;
            _shown.RemoveAt(_dragIndex);
            _shown.Insert(target, dragged);
            _dragIndex = target;
            _hoverIndex = -1;
            _order.Clear();
            for (int i = 0; i < _shown.Count; i++)
            {
                _shown[i].SlotIndex = i;
                _order.Add(_shown[i].Card.Uid);
            }
            RestoreSiblingOrder();
            dragged.transform.SetAsLastSibling();
        }

        private static float FanSlotX(int index, int count)
        {
            if (count <= 1) return 0f;
            float half = (count - 1) * 0.5f;
            float angle = Mathf.Min(UiLayout.HandFanMaxStep, UiLayout.HandFanTotalAngle / (count - 1)) * half * Mathf.Deg2Rad;
            float pitch = Mathf.Min(UiLayout.HandCardWidth + UiLayout.HandCardSpacing,
                (UiLayout.ReferenceWidth - UiLayout.HandFanSideMargin * 2f - UiLayout.HandCardWidth) / (count - 1));
            return pitch * half / Mathf.Sin(angle) * Mathf.Sin(angle * (index - half) / half);
        }

        private void RestoreSiblingOrder()
        {
            for (int i = 0; i < _shown.Count; i++) _shown[i].transform.SetAsLastSibling();
        }

        public CardView FindCard(int uid)
        {
            int index = IndexOfUid(uid);
            return index < 0 ? null : _shown[index];
        }

        /// <summary>
        /// 按实例号取一张手牌的 <see cref="RectTransform"/>（找不到 → false）。
        ///
        /// <para><b>谁在用</b>：<see cref="PeekEyeView"/> —— AI 用雷云 / 狂躁蘑菇偷看玩家手牌时
        /// （<c>HandRevealedEvent.OwnerSeat</c> = 本地座位），要在「被看的那张牌」上方亮一只眼睛。
        /// 事件里给的是引擎的 <c>Card</c>，表现层不该为了配一次对去持有它，所以只认实例号
        /// <c>Uid</c> —— 比下标可靠：玩家拖拽可以改手牌的<b>显示</b>顺序。</para>
        /// </summary>
        public bool TryGetCardRect(int uid, out RectTransform rect)
        {
            CardView view = FindCard(uid);
            rect = view == null ? null : (RectTransform)view.transform;
            return rect != null;
        }

        public void ResetOrder()
        {
            _order.Clear();
            _dragIndex = _hoverIndex = -1;
        }

        private bool InPlayZone()
        {
            if (_playZone == null || _row == null || _dragIndex < 0 || _dragIndex >= _shown.Count)
            {
                return false;
            }

            // 拖拽中 pivot 在底边中点、整卡放大 HandDragScale → 中心 = 底边中点上方半张卡
            Vector2 center = _dragLocal
                             + new Vector2(0f, UiLayout.HandCardHeight * UiLayout.HandDragScale * 0.5f);
            Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(null, _row.TransformPoint(center));
            return RectTransformUtility.RectangleContainsScreenPoint(_playZone, centerScreen, null);
        }

        /// <summary>
        /// 被拖的那张牌是否压在<b>换牌投放区</b>里（2026-10-31）。
        ///
        /// <para>口径与 <see cref="InPlayZone"/> 完全一致：判的是<b>牌的落点</b>
        /// （指针换算出的牌心），不是指针本身 —— 指针被刻意压在牌下沿
        /// （<see cref="UiLayout.DragLiftAbove"/>），拿指针判会有一段「看着推进去了其实没进」。</para>
        /// </summary>
        private bool InReplaceZone()
        {
            if (_replaceZone == null || _row == null || _dragIndex < 0 || _dragIndex >= _shown.Count
                || !_replaceZone.gameObject.activeInHierarchy)
            {
                return false;
            }

            Vector2 center = _dragLocal
                             + new Vector2(0f, UiLayout.HandCardHeight * UiLayout.HandDragScale * 0.5f);
            Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(null, _row.TransformPoint(center));
            return RectTransformUtility.RectangleContainsScreenPoint(_replaceZone, centerScreen, null);
        }

        /// <summary>把投放区切回常态（抬手 / 手势作废 / 决策收尾都要走一遍）。</summary>
        private void ClearReplaceHover()
        {
            if (!_dragOverReplace)
            {
                return;
            }

            _dragOverReplace = false;
            if (_replaceZoneHot != null)
            {
                _replaceZoneHot(false);
            }
        }

        /// <summary>
        /// 在判定区内 → 画箭头（从牌心指向目标）；出区 → 收起。
        ///
        /// <para>瞄点 = 目标「脚底」上抬一个固定高度（<see cref="UiLayout.DropArrowAimLift"/>），
        /// 不用角色包围盒中心 —— 那个值每帧随动作变，箭头末端会一跳一跳。</para>
        /// </summary>
        private void UpdateArrow()
        {
            if (_arrow == null)
            {
                return;
            }

            if (_dragHoverSeat < 0 || _dragIndex < 0 || _dragIndex >= _shown.Count)
            {
                _arrow.Hide();
                return;
            }

            CharacterView target = _dragHoverSeat == LocalSeat ? _selfTarget : _enemyTarget;
            if (target == null)
            {
                _arrow.Hide();
                return;
            }

            RectTransform cardRt = (RectTransform)_shown[_dragIndex].transform;
            Vector3 fromWorld = cardRt.TransformPoint(cardRt.rect.center);
            Vector2 feetScreen = RectTransformUtility.WorldToScreenPoint(null, target.transform.position);

            _arrow.Show(fromWorld, feetScreen + new Vector2(0f, UiLayout.DropArrowAimLift),
                _dragHoverSeat == OpponentSeat);
        }

        private void HideArrow()
        {
            if (_arrow != null)
            {
                _arrow.Hide();
            }
        }

        private void ApplyTargetHighlight(int seat)
        {
            if (_selfTarget != null)
            {
                _selfTarget.SetHighlight(seat == LocalSeat);
            }

            if (_enemyTarget != null)
            {
                _enemyTarget.SetHighlight(seat == OpponentSeat);
            }
        }

        private void ClearTargetHighlight()
        {
            ApplyTargetHighlight(-1);
        }

        // ══════════════════════════════════════════════════════
        //  看牌浮层
        // ══════════════════════════════════════════════════════

        private void ShowPeek(CardView card)
        {
            if (!_peekOnLongPress || _peek == null || card == null || !card.HasCard)
            {
                return;
            }

            _peek.Show(card.Card);
        }

        private void HidePeek()
        {
            if (_peek != null)
            {
                _peek.Hide();
            }
        }

        // ══════════════════════════════════════════════════════
        //  池
        // ══════════════════════════════════════════════════════

        private CardView TakeFromPool()
        {
            CardView v = null;

            if (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                v = _pool[last];
                _pool.RemoveAt(last);
            }

            if (v == null)
            {
                v = Instantiate(_cardPrefab, _row);
                v.name = "HandCard";
                v.Clicked += OnCardClicked;
                // HandCardState / CardInteractor 都随 CardView_Hand.prefab 预置好了
                //（见 UiKitBuilder.BuildCardPrefab）；这里只在「塞进来一张没配组件的卡」时兜底。
                InstantiateState(v);
            }

            v.gameObject.SetActive(true);

            // 手势组件是「挂一次、长期复用」：Host / Card 都是引用，每次取出来重指一遍即可。
            CardInteractor.Attach(v.gameObject, this, v, true);
            return v;
        }

        private HandCardState InstantiateState(CardView v)
        {
            HandCardState st = v.GetComponent<HandCardState>();
            if (st == null)
            {
                st = v.gameObject.AddComponent<HandCardState>();
            }

            st.First = true;
            return st;
        }

        private void OnCardClicked(CardView card)
        {
            if (card == null)
            {
                return;
            }

            // 双发这一拍：点一下 = 加减标记（见 SetAccumulatePick），**不**走单选辉光 ——
            // 单选辉光每点一张就换地方，玩家没法看出自己到底选了哪两张。
            // ⚠ 两条路互斥：否则「点已选的牌取消标记」会被单选辉光重新点亮，看着像没取消。
            if (_accumulatePick)
            {
                TogglePick(card.Card.Uid);
            }
            else
            {
                _selectedUid = card.Card.Uid;
            }

            if (CardClicked != null)
            {
                CardClicked(card);
            }
        }

        private int IndexOfUid(int uid)
        {
            if (uid < 0)
            {
                return -1;
            }

            for (int i = 0; i < _shown.Count; i++)
            {
                if (_shown[i].Card.Uid == uid)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool ContainsUid(int uid)
        {
            return IndexOfUid(uid) >= 0;
        }
    }

    // ⚠ HandCardState 已移到同目录的 HandCardState.cs ——
    //   Unity 只认「文件名 == 类名」的 MonoBehaviour，挤在本文件里时
    //   运行时 AddComponent 能用，但存进 Prefab 会被丢掉（序列化不了脚本引用）。
}

