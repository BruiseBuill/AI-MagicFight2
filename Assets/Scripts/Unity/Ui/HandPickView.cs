using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「卡牌选择弹窗」（M27 建、M31 扩）—— 磁暴 / 充能 / 电弧的攻击时效果，
    /// 以及 M31 的漩涡（从<b>冷却区</b>里挑一张移出游戏）共用的一块浮层。
    ///
    /// <para><b>它补的是哪个洞</b>：这些效果（<c>EffectOp.CoolHandForAtk</c> /
    /// <c>CoolHandForHaste</c> / <c>CoolHandForCombo</c> / <c>EffectOp.RemoveFromGame</c>）
    /// 的选项就是<b>玩家自己的某些牌</b>，引擎那一拍早已是「多选 / 单选、可空 / 强制」，
    /// 但界面只是把它们和「放弃」一起列成按钮 —— 玩家要点一个按钮才动一张，看不出
    /// 「选了几张」，也不知道点下去的后果。用户口径（2026-09-21）：要一个参考图那样的弹窗，
    /// 中间一块卡框，点牌就飞进框里，点框里的牌就是取消。</para>
    ///
    /// <para><b>候选从哪来由引擎说</b>：<see cref="Show(DecisionSnapshot,string,HandView,CardZone)"/>
    /// 的 <c>zone</c> 参数 —— 手牌候选（磁暴 / 充能 / 电弧）与冷却区候选（漩涡）
    /// 走同一块浮层，只是筛 <c>Option.Card.Zone</c> 时用的值不同（铁律 3）。</para>
    ///
    /// <para><b>交互口径（用户原话逐条落地）</b></para>
    /// <list type="number">
    /// <item>弹出时中间<b>只有一张空卡框</b>。</item>
    /// <item>玩家点候选牌 → 那张牌<b>飞进卡框</b>（<see cref="UiLayout.HandPickFlySeconds"/>）。</item>
    /// <item>点卡框里的牌 = <b>取消选择</b>（逐张退回）。</item>
    /// <item>可选多张的（磁暴 / 充能）：多张都进框，<b>框与面板按张数横向变宽</b>。</item>
    /// <item>点确认 = 提交；<b>一张没选也能确认</b>（= 什么都没选）。</item>
    /// <item>必须选一张的（电弧 <c>MinSelect = 1</c>、漩涡 <c>MinSelect = MaxSelect = 1</c>）：
    /// <b>确认键在放牌之前不可用</b>，放入一张后可用，且<b>不能再放第二张</b>。</item>
    /// </list>
    ///
    /// <para><b>不自己判断规则</b>（铁律 3）：能选哪几张、最多几张、能不能空着确认，
    /// 全部读 <c>DecisionSnapshot.Options</c> / <c>MinSelect</c> / <c>MaxSelect</c>。
    /// 本类只做「标记 + 数数量 + 在确认时把序号回传」。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandPickView : MonoBehaviour
    {
        [Header("节点")]
        [Tooltip("整块浮层的 CanvasGroup（淡入淡出 + 挡下层输入）。")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("弹窗底板（Peek_Frame.png，九宫格横向拉伸）。")]
        [SerializeField] private RectTransform _panel;
        [SerializeField] private Image _panelImage;

        [Tooltip("标题（「磁暴」/「充能」/「电弧」，三张牌文案不同）。")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("副标题：这一拍要做什么的一句话。")]
        [SerializeField] private TMP_Text _subTitle;

        [Header("卡框区")]
        [Tooltip("装已选牌的一排（卡框 + 覆盖装饰的躯干条都在这一层里）。")]
        [SerializeField] private RectTransform _slotArea;

        [Tooltip("盖住面板原图内框装饰的纯色条（多选 / 有牌时才亮）。")]
        [SerializeField] private Image _slotAreaBackdrop;

        [Tooltip("还没选牌时显示的那一块空卡框（虚线描边 + 暗底）。")]
        [SerializeField] private RectTransform _emptyFrame;

        [Tooltip("预建的已选卡框 Slot0…Slot7，运行时只激活前 N 个。")]
        [SerializeField] private HandPickSlot[] _slots = new HandPickSlot[0];

        [Header("确认")]
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Image _confirmImage;
        [SerializeField] private TMP_Text _confirmLabel;

        [Header("兜底素材（构建期写入，库没建到时顶上）")]
        [SerializeField] private Sprite _cardFaceFallback;

        /// <summary>确认：把选中的<strong>选项序号</strong>（按选择顺序）回填给上层。</summary>
        public event Action<List<int>> Confirmed;

        // ── 运行态 ───────────────────────────────────────────
        private readonly List<Option> _available = new List<Option>();
        private readonly List<Option> _selected = new List<Option>();
        private readonly List<int> _indexBuf = new List<int>();
        private readonly List<int> _uidBuf = new List<int>();

        /// <summary>手牌区 —— 用它把「已选」的辉光打在对应的手牌上（与发牌台同一套）。</summary>
        private HandView _hand;

        /// <summary>本拍候选所在的区域（手牌 / 冷却区）。由 Show 写入，决定辉光往哪边打。</summary>
        private CardZone _zone = CardZone.Hand;

        private bool _wired;
        private bool _closing;
        private bool _showing;
        private int _maxSelect = 1;
        private int _minSelect;

        private TweenScale _panelTween;
        private TweenCanvasAlpha _fade;

        public bool IsShowing
        {
            get { return _showing; }
        }

        /// <summary>当前已选几张（供自测 / 调试读）。</summary>
        public int SelectedCount
        {
            get { return _selected.Count; }
        }

        /// <summary>本拍最多能选几张。</summary>
        public int MaxSelect
        {
            get { return _maxSelect; }
        }

        /// <summary>本拍最少要选几张（> 0 = 确认键在选够之前不可用）。</summary>
        public int MinSelect
        {
            get { return _minSelect; }
        }

        // ══════════════════════════════════════════════════════
        //  开合
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 打开选牌弹窗。只认识 <c>ChooseCoolHandCards</c> 这一种决策。
        ///
        /// <para><paramref name="title"/> 是弹窗顶部的大字（「磁暴」/「充能」/「电弧」），
        /// 由上层按当前生效的效果给出 —— 三个效果的界面必须看得出区别（用户要求）。</para>
        /// </summary>
        public void Show(DecisionSnapshot snap, string title)
        {
            Show(snap, title, null);
        }

        /// <summary>
        /// 打开选牌弹窗（带手牌区引用）。
        ///
        /// <para><paramref name="hand"/> 用来把「已选」的辉光打在对应手牌上 ——
        /// 与发牌台（<see cref="DealView"/>）同一套做法：<c>SetMultiSelect(true)</c> +
        /// <c>SetMultiSelected(uids)</c>。传 null 也能工作，只是手牌上不会有标记。
        /// <b>候选不是手牌时（漩涡选冷却区）也必须传 null</b> —— 见下面那个重载。</para>
        /// </summary>
        public void Show(DecisionSnapshot snap, string title, HandView hand)
        {
            Show(snap, title, hand, CardZone.Hand);
        }

        /// <summary>
        /// 打开选牌弹窗（指定候选所在区域）。
        ///
        /// <para><b>为什么要这个参数</b>：M31 起同一块浮层要服务两种候选来源 ——
        /// 磁暴 / 充能 / 电弧的候选是<b>手牌</b>（<see cref="CardZone.Hand"/>），
        /// 漩涡的候选是<b>冷却区</b>（<see cref="CardZone.Cooling"/>）。
        /// 「哪一拍的候选在哪一区」是引擎在 <c>Option.Card.Zone</c> 里说清楚的事实，
        /// 界面只按它筛，不自己推断（铁律 3）。</para>
        ///
        /// <para><paramref name="hand"/> 只在 <paramref name="zone"/> 是手牌时有意义：
        /// 冷却区没有「多选辉光」这套东西，传进来会被忽略（<see cref="ApplyHandSelection"/>
        /// 自己按候选区域兜底）。</para>
        /// </summary>
        public void Show(DecisionSnapshot snap, string title, HandView hand, CardZone zone)
        {
            // ⚠ DecisionSnapshot 是 struct —— 不能写 snap == null（CS0019），
            //   用 Options 是否为空当「这份快照有没有内容」的判据（Options 是引用类型）。
            if (_group == null || _panel == null || snap.Options == null)
            {
                return;
            }

            Wire();

            _hand = hand;
            _zone = zone;
            _available.Clear();
            _selected.Clear();
            _minSelect = Mathf.Max(0, snap.MinSelect);
            // MaxSelect <= 0 是引擎不可能给的值；真给了就退化成单选，别让玩家无限加
            _maxSelect = Mathf.Max(1, snap.MaxSelect);

            if (snap.Options != null)
            {
                for (int i = 0; i < snap.Options.Count; i++)
                {
                    Option o = snap.Options[i];
                    // 只收「真的指向本拍候选区里的牌」的选项。Skip / ZoneValue / 光环… 一律不收 ——
                    // 它们不是这一栏的语义（Skip 由「一张不选直接确认」表达，见类注释）。
                    if (o != null && !o.IsSkip && o.Card != null && o.Card.Zone == zone)
                    {
                        _available.Add(o);
                    }
                }
            }

            // 一张候选都没有（手牌里只剩这张打出去的牌 / 冷却区空着）→ 引擎那边本来也不会发这一拍
            // （IssueCoolHandSelection / IssueRemoveFromGame 无候选时返回 false）。真到了这一步就静默收起来，
            // 绝不弹一个空框让玩家点不动。
            if (_available.Count == 0)
            {
                _showing = false;
                gameObject.SetActive(false);
                return;
            }

            _showing = true;
            _closing = false;

            SetTitle(title, snap.Prompt);
            Layout();
            // ⚠ 必须在这里也刷一次确认键：SetTitle/Layout 都不碰 interactable，
            //   不刷的话按钮会保留 prefab 里的初值（True）—— 于是「电弧一张没选时
            //   确认键亮着」这个恰恰是用户点名要拦的情况反而没拦住。
            RefreshConfirm();

            // 手牌进入「多选标记」模式：点一下标记、再点一下取消（与发牌台一致的观感）
            // ⚠ 只有候选真的是手牌时才这么干：漩涡的候选在冷却区，把手牌切进多选模式
            //   会让一整排手牌看着像能点，而它们其实一张都提交不出去。
            if (_hand != null && zone == CardZone.Hand)
            {
                _hand.SetMultiSelect(true);
                _hand.SetMultiSelected(null);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            _group.blocksRaycasts = true;
            _group.interactable = true;
            _group.alpha = 0f;

            // ⚠ UiTween.Setup 返回 void —— 不能写成 .Setup(...).Play()，必须分两句。
            TweenCanvasAlpha fadeIn = FadeFor();
            fadeIn.Setup(0f, 1f, UiLayout.HandPickFadeSeconds, UiEaseKind.OutQuad);
            fadeIn.Play();

            // 面板的弹出单独一支补间（它不参与任何状态机，只管弹一下）
            _panelTween = PanelTween();
            _panelTween.Setup(Vector3.one * UiLayout.HandPickPanelPopFrom, Vector3.one,
                UiLayout.HandPickPanelPopSeconds, UiEaseKind.OutBack);
            _panelTween.Play();
        }

        /// <summary>收起来（淡出）。已经在关 / 本来就没开 → 什么都不做。</summary>
        public void Hide()
        {
            if (!_showing || _closing)
            {
                return;
            }

            _closing = true;

            TweenCanvasAlpha fadeOut = FadeFor();
            fadeOut.Setup(_group == null ? 1f : _group.alpha, 0f,
                UiLayout.HandPickFadeSeconds, UiEaseKind.OutQuad);
            fadeOut.Play();
        }

        /// <summary>立刻收起来（重开一局 / 终局用，不要动画）。</summary>
        public void HideImmediate()
        {
            _showing = false;
            _closing = false;
            _available.Clear();
            _selected.Clear();

            if (_hand != null)
            {
                // 手牌退出多选标记模式、辉光收掉。做在 HideImmediate 里（不是 Hide 里）——
                // 淡出的那 0.18 s 里手牌还得看得见标记；真收起来时一起清。
                //（候选在冷却区时 _hand 本来就是 null，这段自然跳过。）
                _hand.SetMultiSelect(false);
                _hand.SetMultiSelected(null);
                _hand = null;
            }

            // 下一拍重新由 Show 决定候选区；不还原的话「上一拍选冷却区」会残留下来，
            // 让下一拍的手牌候选被 ApplyHandSelection 无视掉。
            _zone = CardZone.Hand;

            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }

            gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        //  选择
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 玩家点了手牌区的一张牌（uid）。在候选里就标记进框；已经在框里就取出来。
        ///
        /// <para><b>不提交</b> —— 提交只发生在确认键上，这样「选多张」才真的能一次选多张
        /// （与 <see cref="DealView.Toggle"/> 同一套理由）。</para>
        /// </summary>
        public void Toggle(int uid)
        {
            if (!_showing || _closing)
            {
                return;
            }

            Option target = FindByUid(uid);
            if (target == null)
            {
                return;   // 不在候选里（例如就是这轮打出去的那张）→ 静默忽略
            }

            int at = IndexOfSelected(target);
            if (at >= 0)
            {
                _selected.RemoveAt(at);
                Layout();
                RefreshConfirm();
                ApplyHandSelection();
                return;
            }

            if (_selected.Count >= _maxSelect)
            {
                // 选满（电弧就是这种：放了一张就不能再放第二张）。
                // 不弹出提示条 —— 确认键已经亮了，玩家能看出「只能一张」；
                // 这里只让刚点的那格不被点亮即可。
                return;
            }

            _selected.Add(target);
            Layout(_selected.Count - 1);   // 面板按新张数变宽 + 新落进来的那一格播大回弹
            RefreshConfirm();
            ApplyHandSelection();
        }

        /// <summary>点框里的某一张 = 取消选择它。</summary>
        private void OnSlotClicked(HandPickSlot slot)
        {
            if (slot == null || slot.OptionIndex < 0 || !_showing || _closing)
            {
                return;
            }

            int at = -1;
            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i].Index == slot.OptionIndex)
                {
                    at = i;
                    break;
                }
            }

            if (at < 0)
            {
                return;
            }

            _selected.RemoveAt(at);
            Layout();          // 面板按新张数收窄（取消选择也要跟着变）
            RefreshConfirm();
            ApplyHandSelection();
        }

        /// <summary>
        /// 把「已选」的 uid 集合交给手牌区打辉光 —— 与发牌台同一套做法。
        ///
        /// <para>哪些牌能选由引擎的 <c>Options</c> 决定，<see cref="HandView.SetPlayable"/>
        /// 已经负责「能点的亮、不能点的暗」；这里只补「已点过的亮得不一样」。</para>
        /// </summary>
        private void ApplyHandSelection()
        {
            // 候选不是手牌时（漩涡选冷却区）这套辉光无处可打，且 _hand 往往是 null ——
            // 直接跳过，别把冷却区卡的 uid 塞进手牌的多选集合里。
            if (_hand == null || _zone != CardZone.Hand)
            {
                return;
            }

            _uidBuf.Clear();
            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i].Card != null)
                {
                    _uidBuf.Add(_selected[i].Card.Uid);
                }
            }

            _hand.SetMultiSelected(_uidBuf);
        }

        private void OnConfirmClicked()
        {
            if (!_showing || _closing || _selected.Count < _minSelect)
            {
                return;
            }

            _indexBuf.Clear();
            for (int i = 0; i < _selected.Count; i++)
            {
                _indexBuf.Add(_selected[i].Index);
            }

            // 先取出副本再抛事件：订阅方收到后通常会 Hide()，把 _indexBuf 清掉。
            var payload = new List<int>(_indexBuf);
            if (Confirmed != null)
            {
                Confirmed(payload);
            }
        }

        // ══════════════════════════════════════════════════════
        //  版式
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 按「当前已选张数」重排。
        ///
        /// <para><b>面板横向变宽</b>：面板图是九宫格（左右 border = 110），拉宽时
        /// 顶部的标题牌位与底部的按钮座原地不动，只有中间那段躯干变宽 ——
        /// 所以「界面大小可变」不需要另切一张图。</para>
        ///
        /// <para><b>同时把原图内框的装饰盖掉</b>：那块内框（圆角描边 + 中心菱形花纹）
        /// 在被拉宽时会一起变形，所以有牌时就压一条与躯干同色的纯色条
        /// （<see cref="UiTheme.HandPickPanelTorso"/>）在上面，再在纯色条上摆自己的卡框。
        /// 一张没选时**不盖** —— 那时露出来的正是原图那块空卡框，就是用户要的初始样子。</para>
        /// </summary>
        private void Layout()
        {
            Layout(-1);
        }

        /// <summary>
        /// 同上，但可以指定「第几个卡框要播落位回弹」。
        ///
        /// <para><b>参数</b>：<paramref name="popIndex"/> ≥ 0 = 那一格是新加进来的（播大回弹）；
        /// -1 = 没有新格（重排 / 取消 / 首次弹出）。</para>
        ///
        /// <para><b>为什么选牌后必须走这里而不是只调 <see cref="RefreshSlots"/> 一次</b>：
        /// 面板宽度、SlotArea 宽度、空框显隐、遮挡条显隐全在 <b>本方法</b>里 ——
        /// 只调 RefreshSlots 会让「选了 3 张但面板仍是单张宽」这种半调子状态出现。
        /// 所有改动已选集合的地方都必须收口到本方法。</para>
        /// </summary>
        private void Layout(int popIndex)
        {
            int count = _selected.Count;
            float panelW = UiLayout.HandPickPanelWidthFor(count);
            float areaW = UiLayout.SlotAreaWidthFor(count);

            if (_panel != null)
            {
                _panel.sizeDelta = new Vector2(panelW, UiLayout.HandPickPanelHeight);
            }

            float areaCenterY = UiLayout.HandPickPanelHeight * 0.5f - UiLayout.HandPickSlotAreaCenterY;

            if (_slotArea != null)
            {
                _slotArea.sizeDelta = new Vector2(areaW, UiLayout.HandPickSlotAreaHeight);
                _slotArea.anchoredPosition = new Vector2(0f, areaCenterY);
            }

            // 一张没选：露出原图的空卡框（它在面板正中，尺寸 = 单张框）
            if (_emptyFrame != null)
            {
                _emptyFrame.sizeDelta =
                    new Vector2(UiLayout.HandPickSlotWidth, UiLayout.HandPickSlotHeight);
                _emptyFrame.anchoredPosition = Vector2.zero;
                _emptyFrame.gameObject.SetActive(count == 0);
            }

            // 有牌：盖住被拉变形的内框装饰。
            // 这一层是 SlotArea 的子节点、铺满父节点（构建器里设成 Stretch），所以
            // 这里只切 active —— 尺寸跟着 SlotArea 走，不要再写 sizeDelta（会与拉伸锚点打架）。
            if (_slotAreaBackdrop != null)
            {
                _slotAreaBackdrop.gameObject.SetActive(count > 0);
            }

            RefreshSlots(popIndex);
        }

        /// <summary>
        /// 把前 N 个卡框摆好（N = 已选张数），其余停用。
        /// <paramref name="popIndex"/> 若 ≥ 0，那一格额外播一下放大回弹。
        /// </summary>
        private void RefreshSlots(int popIndex)
        {
            int count = _selected.Count;
            float pitch = UiLayout.HandPickSlotWidth + UiLayout.HandPickSlotGap;
            float totalW = count <= 0 ? 0f : count * pitch - UiLayout.HandPickSlotGap;
            float firstX = -totalW * 0.5f + UiLayout.HandPickSlotWidth * 0.5f;

            // 槽位在 SlotArea 里的起始兄弟下标。
            //
            // SlotArea 的子节点 = [Backdrop, EmptyFrame, Slot0…Slot7]（构建器顺序）。
            // 前两个是「装饰」，必须待在槽位**下面**（同层绘制顺序 = 兄弟顺序），
            // 所以槽位只能从它们后面开始排 —— 用实测下标算，构建器若增删装饰也能自适应。
            int slotSiblingBase = SlotSiblingBase();

            for (int i = 0; i < _slots.Length; i++)
            {
                HandPickSlot slot = _slots[i];
                if (slot == null)
                {
                    continue;
                }

                bool used = i < count;
                if (!used)
                {
                    slot.gameObject.SetActive(false);
                    slot.SlotIndex = -1;
                    slot.OptionIndex = -1;
                    slot.CardUid = -1;
                    slot.Interactable = false;
                    continue;
                }

                Option o = _selected[i];
                RectTransform rt = (RectTransform)slot.transform;
                slot.gameObject.SetActive(true);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(UiLayout.HandPickSlotWidth, UiLayout.HandPickSlotHeight);
                rt.anchoredPosition = new Vector2(firstX + i * pitch, 0f);

                slot.SlotIndex = i;
                slot.OptionIndex = o.Index;
                slot.CardUid = o.Card != null ? o.Card.Uid : -1;
                slot.Interactable = true;

                if (slot.Face != null)
                {
                    Sprite face = FaceSpriteFor(o.Card);
                    slot.Face.sprite = face;
                    // 图还是没有时退化成一块深色方块 —— 至少位置和顺序是对的
                    slot.Face.color = face != null ? Color.white : UiTheme.MiniCardFace;
                }

                if (slot.Edge != null)
                {
                    slot.Edge.color = UiTheme.HandPickSlotEdge;
                }

                // 2026-09-23：序号角标已按用户口径整个移除，这里不再有 IndexBadge 那段。

                // ⚠ 不能写 SetSiblingIndex(i)：SlotArea 的前两个子节点是 Backdrop 与 EmptyFrame
                //   （构建器里就是先建它俩、再建 Slot0…7，同层绘制顺序 = 兄弟顺序，
                //   所以它俩「天然在槽位下面」是靠这个次序保证的）。
                //   若把 Slot_i 钉到下标 i，第 0/1/2 个 Slot 就会挤到 Backdrop 前面，
                //   于是 Backdrop 反过来盖住已经放好的牌 —— 现象是「面板加宽了、框里却是空的」。
                //   这里改成从槽位应有的起始下标起算，保持「装饰在下、槽位在上」。
                slot.transform.SetSiblingIndex(slotSiblingBase + i);
            }

            if (popIndex >= 0 && popIndex < _slots.Length && _slots[popIndex] != null
                && _slots[popIndex].gameObject.activeSelf && isActiveAndEnabled)
            {
                TweenScale t = SlotTween(_slots[popIndex].gameObject);
                t.Setup(Vector3.one * 0.82f, Vector3.one,
                    UiLayout.HandPickAddSeconds, UiEaseKind.OutBack);
                t.Play();
            }
        }

        /// <summary>
        /// 槽位在 <c>SlotArea</c> 里的起始兄弟下标 = 「第一个槽位之前还有几个装饰节点」。
        ///
        /// <para><b>为什么需要它</b>：同层绘制顺序 = 兄弟顺序，而 <c>SlotArea</c> 的前两个子节点
        /// 是 Backdrop / EmptyFrame（装饰，必须待在牌下面）。若槽位把下标从 0 开始占，
        /// 就会把这两个装饰挤到后面 —— Backdrop 反过来盖住已经放好的牌，
        /// 现象是「面板加宽了、框里却是空的」。</para>
        ///
        /// <para><b>为什么按装饰实测、而不是按槽位实测</b>：槽位下标一旦被写坏，
        /// 「槽位最小下标」也会跟着变成 0，读不出真实的起始位。
        /// 装饰节点（Backdrop / EmptyFrame）从不被本类移动，它们的下标才是稳定的基准。</para>
        /// </summary>
        private int SlotSiblingBase()
        {
            Transform area = _slotArea != null ? _slotArea : transform;
            int count = 0;

            if (_slotAreaBackdrop != null && _slotAreaBackdrop.transform.parent == area)
            {
                count++;
            }

            if (_emptyFrame != null && _emptyFrame.transform.parent == area)
            {
                count++;
            }

            // 两个装饰都没接上（理论上不会）→ 退回 0，行为与改动前一致
            return count;
        }

        /// <summary>
        /// 确认键的可用态 —— <b>唯一一处「界面上能不能按」的判断</b>，
        /// 判据完全是引擎给的 <c>MinSelect</c>（铁律 3：不在这里重复规则）。
        ///
        /// <para>电弧（<c>MinSelect = 1</c>）：放第一张之前确认键暗着，放进去就亮，
        /// 而且因为 <c>MaxSelect = 1</c> 又放不进第二张 —— 正是用户要的那条。</para>
        /// </summary>
        private void RefreshConfirm()
        {
            bool ok = _selected.Count >= _minSelect;

            if (_confirmButton != null)
            {
                // ⚠ ColorTint 是替换不是乘算，且 disabledColor 的 alpha 暗不下来 ——
                //   所以两个颜色都用「本身就压到底」的实色（见 UiTheme 的说明）。
                ColorBlock cb = _confirmButton.colors;
                cb.normalColor = UiTheme.HandPickConfirmOn;
                cb.highlightedColor = UiTheme.HandPickConfirmOn;
                cb.pressedColor = UiTheme.HandPickConfirmOn;
                cb.selectedColor = UiTheme.HandPickConfirmOn;
                cb.disabledColor = UiTheme.HandPickConfirmOff;
                _confirmButton.colors = cb;
                _confirmButton.interactable = ok;
            }

            if (_confirmLabel != null)
            {
                // 底图是素材那条**亮蓝**按钮条（M30），上面写白字正好；
                // 不可用时整条被 ColorTint 换成深灰蓝，白字会糊，所以跟着暗下去。
                _confirmLabel.color = ok ? UiTheme.TextPrimary : UiTheme.HandPickConfirmTextOff;
            }

            if (_subTitle != null)
            {
                _subTitle.text = _selected.Count + " / " + Mathf.Max(1, _maxSelect);
                _subTitle.color = _selected.Count > 0 ? UiTheme.SelectionGlow : UiTheme.TextSecondary;
            }
        }

        private void SetTitle(string title, string prompt)
        {
            if (_title != null)
            {
                // 兜底文案跟着候选区走：候选在冷却区却写「选择手牌」会把人指错地方。
                //（引擎正常情况下都会给 Title —— 这只在 Title 缺失时才露出来。）
                _title.text = string.IsNullOrEmpty(title)
                    ? (_zone == CardZone.Cooling ? "选择冷却区法术" : "选择手牌")
                    : title;
            }

            // 顶部提示条会把引擎的 Prompt 说一遍，这里只说「选了几张 / 最多几张」，
            // 不重复那句话（与 TargetPicker 的既有做法一致）。
            if (_subTitle != null)
            {
                _subTitle.text = "0 / " + Mathf.Max(1, _maxSelect);
                _subTitle.color = UiTheme.TextSecondary;
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private void Wire()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;

            if (_confirmButton != null)
            {
                _confirmButton.onClick.AddListener(OnConfirmClicked);
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i].Clicked += OnSlotClicked;
                }
            }
        }

        private Option FindByUid(int uid)
        {
            for (int i = 0; i < _available.Count; i++)
            {
                Option o = _available[i];
                if (o.Card != null && o.Card.Uid == uid)
                {
                    return o;
                }
            }

            return null;
        }

        private int IndexOfSelected(Option option)
        {
            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i].Index == option.Index)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 卡面：优先用烘焙好的整张卡面（与「长按看完整卡面」同一张图），
        /// 库里没有就用构建期写进来的兜底。
        /// </summary>
        private Sprite FaceSpriteFor(CardInstance card)
        {
            if (card == null)
            {
                return _cardFaceFallback;
            }

            CardArtLibrary lib = CardArtLibrary.Instance;
            if (lib != null && card.Def != null)
            {
                Sprite art = lib.GetArt(card.Def);
                if (art == null)
                {
                    art = lib.GetIllustration(card.Def);
                }

                if (art != null)
                {
                    return art;
                }
            }

            return _cardFaceFallback;
        }

        // ── 小工具 ───────────────────────────────────────────

        private TweenScale PanelTween()
        {
            if (_panelTween == null)
            {
                _panelTween = _panel.GetComponent<TweenScale>();
                if (_panelTween == null)
                {
                    _panelTween = _panel.gameObject.AddComponent<TweenScale>();
                }
            }

            return _panelTween;
        }

        private static TweenScale SlotTween(GameObject go)
        {
            TweenScale t = go.GetComponent<TweenScale>();
            if (t == null)
            {
                t = go.AddComponent<TweenScale>();
            }

            return t;
        }

        private TweenCanvasAlpha FadeFor()
        {
            if (_fade == null)
            {
                _fade = GetComponent<TweenCanvasAlpha>();
                if (_fade == null)
                {
                    _fade = gameObject.AddComponent<TweenCanvasAlpha>();
                }
            }

            _fade.Finished -= OnFadeFinished;
            _fade.Finished += OnFadeFinished;
            return _fade;
        }

        private void OnFadeFinished()
        {
            if (!_closing)
            {
                return;   // 淡入结束，什么都不用做
            }

            HideImmediate();
        }
    }
}
