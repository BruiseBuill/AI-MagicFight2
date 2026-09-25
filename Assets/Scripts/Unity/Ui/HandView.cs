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
    public sealed class HandView : MonoBehaviour, ICardGestureHost
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
        /// 「已经选定、等着凑成一对」的那张牌（双发防御用，2026-09-23）。
        ///
        /// <para>双发要求交出<b>两张</b>够挡的牌，而本作的手势是「拖一张进判定区、松手即出牌」——
        /// 一次拖拽只表达得了一张。所以这一拍改成两段：第一次拖进来的牌只「钉住」
        /// （辉光保持、留在手牌里等），第二次拖进来的牌才与它凑成一对一起提交
        /// （配对与提交都在 <c>BattleUi.OnHandCardDropped</c>，本视图不判断任何规则）。</para>
        ///
        /// <para>⚠ 为什么不复用 <see cref="_selectedUid"/>：那是「点一下」的残留，
        /// 拖拽一开始就被清掉（见 <see cref="BeginDrag"/>）—— 而钉住的那张必须活过第二次拖拽。</para>
        /// </summary>
        private int _pinnedUid = -1;

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

        // ── 拖拽状态 ─────────────────────────────────────────
        private readonly List<int> _order = new List<int>();
        private readonly List<CardSnapshot> _orderedHand = new List<CardSnapshot>();
        private readonly HashSet<int> _incomingUids = new HashSet<int>();
        private int _dragIndex = -1;
        private Vector2 _dragLocal;
        private int _dragHoverSeat = -1;

        /// <summary>点了一张牌（BattleUi 判断它对应哪个合法选项）。</summary>
        public event Action<CardView> CardClicked;

        /// <summary>手牌被拖放到某个座位上（0 = 自己 → 防御，1 = 对方 → 进攻）。</summary>
        public event Action<CardView, int> CardDropped;

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

        // ══════════════════════════════════════════════════════
        //  绑定
        // ══════════════════════════════════════════════════════

        /// <summary>按快照重建手牌。会尽量复用同一张牌已有的 <see cref="CardView"/>（按 Uid 配对），避免闪。</summary>
        public void Bind(IReadOnlyList<CardSnapshot> hand, HashSet<int> playableUids)
        {
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
            ClearTargetHighlight();

            if (hand != null)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    CardView v;
                    if (!_reuse.TryGetValue(hand[i].Uid, out v))
                    {
                        v = TakeFromPool();
                        InstantiateState(v);
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
        /// 钉住 / 取消钉住某张牌（<c>-1</c> = 取消）。双发防御的「已选第一张」。
        ///
        /// <para>与 <see cref="SetSelectedUid"/> 的区别：钉住的辉光<b>不会被下一次拖拽清掉</b>
        /// （<see cref="BeginDrag"/> 会清掉点击残留，而钉住的那张必须活到第二次拖拽）。</para>
        /// </summary>
        public void SetPinnedUid(int uid)
        {
            _pinnedUid = uid;
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
            _selectedUid = -1;
            _pinnedUid = -1;
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

                if (i == _dragIndex)
                {
                    // 正被拖着：跟着指针、摆正、放大
                    targetX = _dragLocal.x;
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

                    // 单选模式看 _selectedUid，多选模式看 _multi（M9 替换可同时标多张）；
                    // _pinnedUid 是双发「已选第一张」，与单选模式并存（两者都亮）。
                    bool marked = _multiMode
                        ? _multi.Contains(v.Card.Uid)
                        : v.Card.Uid == _selectedUid || v.Card.Uid == _pinnedUid;

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
                    st.Y = targetY;
                    st.Rot = targetRot;
                    st.Scale = targetScale;
                    st.First = false;
                }
                else
                {
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

            // 把指针位置先按「卡原位」初始化一下，免得第一帧跳到别处
            _dragLocal = ((RectTransform)card.transform).anchoredPosition;

            // 拖起来的那张要盖在最上面
            card.transform.SetAsLastSibling();
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
            if (_dragHoverSeat < 0 && IsInHandArea(screenPos)) ReorderAtPointer();
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
            bool dropped = CanDropDraggedCard() && InPlayZone();

            _dragIndex = -1;
            _dragHoverSeat = -1;
            HideArrow();
            ClearTargetHighlight();

            if (card == null)
            {
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

            _selectedUid = card.Card.Uid;

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

