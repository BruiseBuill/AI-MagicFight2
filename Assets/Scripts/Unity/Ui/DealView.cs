using System;
using System.Collections;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 发牌台（M9）：开局发 6 张、以及第 2/3 回合补牌之后的<strong>图形化替换交互</strong>。
    ///
    /// <para><b>它补的是哪个洞</b>：引擎的 <c>ChooseReplace</c> 决策本来就是<strong>多选</strong>
    /// （<c>MinSelect = 0</c>、<c>MaxSelect = 至多 3</c>），而 M8 里点一张牌就立刻回填了一个序号 ——
    /// 于是「可替换至多 3 张」实际上只能换 1 张，而且没有任何「已挑了几张」的反馈。
    /// 本类把这件事补齐：<b>点手牌 = 标记 / 取消标记</b>，选够了一次性回填。</para>
    ///
    /// <para><b>为什么不自己判断规则</b>：哪些牌能被替换、最多换几张，全在
    /// <c>DecisionSnapshot</c> 的 <c>Options</c> 与 <c>MaxSelect</c> 里 —— 引擎已经把非法项滤掉了，
    /// 本类只做「标记 + 数数量 + 把序号回传」（铁律第 3 条）。连「不替换」那颗按钮的文字
    /// 都直接用引擎给的 Done 选项 <c>Label</c>：开局是「不替换，开始对局」，补牌后是「保留这张，继续」，
    /// 一句话都不用在 UI 里硬编码。</para>
    ///
    /// <para><b>两个入口不重复</b>：替换决策期间 <see cref="TargetPicker"/> 整个收起 ——
    /// Done 选项改由这里承担，否则同一件事会有两个按钮。</para>
    ///
    /// <para><b>2026-10-31 · 换牌改成手势</b>：点 / 划把手牌标记成「要换的」，
    /// 然后把其中任意一张<b>向上拖进手牌区上方那条投放槽</b> —— 标记的整批一次换掉。
    /// 只拖一张没标记的牌，就只换那一张。额度（开局 3 / 补牌 1）由引擎逐次递减。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DealView : MonoBehaviour
    {
        [Header("面板")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _counter;
        [SerializeField] private TMP_Text _hint;

        [Header("按钮")]
        [SerializeField] private Button _confirmButton;
        [SerializeField] private TMP_Text _confirmLabel;
        [SerializeField] private Button _skipButton;
        [SerializeField] private TMP_Text _skipLabel;

        /// <summary>玩家点了「确认替换」：回填选中的<strong>选项序号</strong>（按标记顺序）。</summary>
        public event Action<List<int>> Confirmed;

        /// <summary>玩家点了 Done（不替换 / 保留这张）：原样回传引擎给的那个选项。</summary>
        public event Action<Option> Skipped;

        private readonly List<Option> _replaceOptions = new List<Option>();
        private readonly List<Option> _selected = new List<Option>();
        private readonly List<int> _uidBuf = new List<int>();
        private readonly List<int> _indexBuf = new List<int>();

        private Option _doneOption;
        private HandView _hand;
        private int _maxSelect = 1;

        private Coroutine _flashRoutine;

        // ── 换牌投放区（2026-10-31）──────────────────────────
        //
        //  用户口径：**向上拖牌到换牌区就算换掉**。开局能换 3 张，补牌能换 1 张 ——
        //  一次扔一张，扔完一张引擎会原地重发同一条决策，界面不收起，接着扔下一张。
        //
        //  ⚠ 这块区域**运行时创建**，不进 Prefab：`BattleUiBuilder.BuildDeal` 只认识
        //    DealPanel 那一块，为了加一条投放槽去重跑 M7→M8→M11 会把别的构建器挂在
        //    同一子树上的节点一起吃掉（铁律 4）。运行时建反而更安全 —— 改版式只要改
        //    `UiLayout.ReplaceZone*` 这几个数，不用碰任何资产。

        /// <summary>投放区的根（拖拽判定用的矩形）。</summary>
        private RectTransform _dropZone;

        /// <summary>常态底 / 高亮底 —— 只切 activeSelf，颜色不写代码（项目惯例）。</summary>
        private GameObject _dropZoneIdle;
        private GameObject _dropZoneHot;

        private TMP_Text _dropZoneLabel;
        private bool _dropZoneBuilt;

        public bool IsOpen
        {
            get { return UiPanelAnimator.IsShown(_panel); }
        }

        /// <summary>当前已标记几张。</summary>
        public int SelectedCount
        {
            get { return _selected.Count; }
        }

        private void Awake()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.AddListener(OnConfirmClicked);
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.AddListener(OnSkipClicked);
            }

            UiPanelAnimator.SetVisible(_panel, false, immediate: true);
        }

        // ══════════════════════════════════════════════════════
        //  开合
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 打开发牌台。<paramref name="snap"/> 必须是 <c>ChooseReplace</c> 决策 ——
        /// 本类只认识这一种，别的决策由 <see cref="TargetPicker"/> 负责。
        /// </summary>
        public void Show(DecisionSnapshot snap, HandView hand)
        {
            _hand = hand;
            _replaceOptions.Clear();
            _selected.Clear();
            _doneOption = null;

            // MaxSelect 是「这次最多能换几张」：开局是 3，补牌后是 1。
            _maxSelect = Mathf.Max(1, snap.MaxSelect);

            if (snap.Options != null)
            {
                for (int i = 0; i < snap.Options.Count; i++)
                {
                    Option o = snap.Options[i];
                    if (o.Kind == OptionKind.Replace && o.Card != null)
                    {
                        _replaceOptions.Add(o);
                    }
                    else if (_doneOption == null
                             && (o.Kind == OptionKind.Done || o.Kind == OptionKind.Skip))
                    {
                        _doneOption = o;
                    }
                }
            }

            // 按钮文字来自引擎：开局 =「不替换，开始对局」，补牌后 =「保留这张，继续」。
            if (_skipLabel != null)
            {
                _skipLabel.text = _doneOption != null && !string.IsNullOrEmpty(_doneOption.Label)
                    ? _doneOption.Label
                    : "不替换";
            }

            if (_confirmLabel != null)
            {
                _confirmLabel.text = "确认替换";
            }

            // 2026-10-31：不再「预先标记刚补到的那张」。想换就点一下 / 划一下，
            // 或者直接把那张牌往上拖 —— 三条路都通，预选只会让人以为已经换过了。
            RefreshHint();

            if (_hand != null)
            {
                // 多选回来了（2026-10-31 第二轮）：一次最多能换 3 张，
                // 玩家先点够几张再一起拖上去，比一张一张扔快得多。
                _hand.SetMultiSelect(true);
                _hand.SetPaintSink(Paint, _maxSelect);
            }

            // 「确认替换」也回来：选中之后拖上去是一种提交方式，点它也是一种。
            if (_confirmButton != null)
            {
                _confirmButton.gameObject.SetActive(true);
            }

            if (_panel != null)
            {
                UiPanelAnimator.SetVisible(_panel, true);
            }

            // 换牌区：只有「这一拍真的能换」才亮出来（候选为空时引擎根本不会发这条决策，
            // 这里再兜一次底 —— 牌堆空了 / 手上没有可换的牌，摆一块点了没反应的槽更糟）。
            ShowDropZone(_replaceOptions.Count > 0);
            BindDropZone();

            RefreshCounter();
            ApplySelection();
        }

        public void Hide()
        {
            StopFlash();

            _replaceOptions.Clear();
            _selected.Clear();
            _doneOption = null;

            if (_hand != null)
            {
                _hand.ReplaceDropped -= OnReplaceDropped;
                _hand.SetReplaceZone(null, null);
                _hand.SetPaintSink(null, 0);
                _hand.SetMultiSelect(false);
                _hand.SetMultiSelected(null);
            }

            ShowDropZone(false);

            if (_panel != null)
            {
                UiPanelAnimator.SetVisible(_panel, false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  标记
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 点了一张手牌：标记 / 取消标记。<b>不提交</b> —— 提交只发生在「确认替换」上，
        /// 这样「至多换 3 张」才真的能一次换 3 张。
        /// </summary>
        public void Toggle(int uid)
        {
            if (!IsOpen)
            {
                return;
            }

            Option target = FindByUid(uid);
            if (target == null)
            {
                return;
            }

            int at = IndexOfOption(target);
            if (at < 0 && _selected.Count >= _maxSelect)
            {
                // 已经选满：不改选中集合，只闪一下计数行告个知。
                //（画刷那条路刻意不闪 —— 一路划过去连闪好几下像卡住了，见 Paint。）
                StartFlash();
                return;
            }

            Paint(uid, at < 0);
        }

        /// <summary>
        /// 画刷式选中（2026-10-31）：把一张牌<b>置为</b>选中 / 未选中，<b>不切换</b>。
        ///
        /// <para>滑动选牌是一支画笔 —— 笔尖扫过哪些牌，哪些牌就统一变成同一个状态。
        /// 所以这条入口必须是「置状态」而不是「Toggle」：划到一张<b>已经选中</b>的牌时，
        /// 加选笔刷要原样留着它，而不是顺手把它取消掉（那正是「逐张点击的批量版」）。</para>
        ///
        /// <para>上限仍然是 <see cref="_maxSelect"/>（开局 3 / 补牌 1，来自引擎的
        /// <c>MaxSelect</c>）：满了就什么都不做，也<b>不</b>顶掉前面的 ——
        /// 用户口径「最多扫到三张牌之后就不会再额外增加了」。</para>
        /// </summary>
        public void Paint(int uid, bool on)
        {
            if (!IsOpen)
            {
                return;
            }

            Option target = FindByUid(uid);
            if (target == null)
            {
                return;
            }

            int at = IndexOfOption(target);

            if (on)
            {
                if (at >= 0 || _selected.Count >= _maxSelect)
                {
                    return;
                }

                _selected.Add(target);
            }
            else
            {
                if (at < 0)
                {
                    return;
                }

                _selected.RemoveAt(at);
            }

            StopFlash();
            ApplySelection();
            RefreshCounter();
            RefreshHint();   // 「已选 1/1」被玩家点掉之后提示行要跟着改口
        }

        private void ApplySelection()
        {
            if (_hand == null)
            {
                return;
            }

            _uidBuf.Clear();
            for (int i = 0; i < _selected.Count; i++)
            {
                _uidBuf.Add(_selected[i].Card.Uid);
            }

            _hand.SetMultiSelected(_uidBuf);
            RefreshDropZoneLabel();
        }

        /// <summary>
        /// 写提示行 —— 提示「怎么操作」；「做什么」（换几张）由顶部提示条里的引擎文案承担，不重复。
        ///
        /// <para>三种口气：开局多选 / 补牌单候选且已预选 / 单候选但玩家刚把它取消了。
        /// 第三档不能沿用第二档的文案，否则界面上写着「已选中」而计数行是「已选 0 / 1」。</para>
        /// </summary>
        private void RefreshHint()
        {
            if (_hint == null)
            {
                return;
            }

            // 两条路都通（2026-10-31 第二轮）：点/划选好再一起拖，或者直接拖那一张。
            //
            // ⚠ 文案必须短：Hint 行只有 DealHintHeight(24) 高，420 宽的面板里
            //   19pt 超过 ~20 个字就会折行，折出来的第二行会被下面的「确认替换」压住。
            //   「还能换几张」由计数行（已选 N / M）承担，这里只说怎么操作。
            _hint.text = _maxSelect > 1
                ? "点选要换的牌，向上拖进换牌区"
                : "把这张牌向上拖进换牌区";
        }

        private void RefreshCounter()
        {
            if (_counter != null)
            {
                // `_maxSelect` 是**本趟还剩多少额度**（引擎每次重发都会把 MaxSelect 递减），
                // 所以这行读作「已选 2 / 3 张」时，分母永远是此刻真的还能换的张数。
                _counter.text = "已选 " + _selected.Count + " / " + _maxSelect;
                _counter.color = _selected.Count > 0 ? UiTheme.InkOnIvoryAccent : UiTheme.InkMuted;
            }

            if (_confirmButton != null)
            {
                _confirmButton.interactable = _selected.Count > 0;
            }
        }

        // ══════════════════════════════════════════════════════
        //  提交
        // ══════════════════════════════════════════════════════

        private void OnConfirmClicked()
        {
            if (_selected.Count <= 0)
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

        private void OnSkipClicked()
        {
            if (_doneOption == null)
            {
                return;
            }

            if (Skipped != null)
            {
                Skipped(_doneOption);
            }
        }

        // ══════════════════════════════════════════════════════
        //  换牌投放区（2026-10-31）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把投放区接到手牌视图上（幂等）：手牌往上拖、松手落在区里 → 本类收到 uid 就提交。
        /// </summary>
        private void BindDropZone()
        {
            if (_hand == null)
            {
                return;
            }

            EnsureDropZone();

            // 先摘再挂：Show 会被引擎的「换完一张重发决策」反复调用，
            // 不摘的话同一个回调会叠好几份（换一张提交好几次）。
            _hand.ReplaceDropped -= OnReplaceDropped;
            _hand.ReplaceDropped += OnReplaceDropped;
            _hand.SetReplaceZone(_dropZone, SetDropZoneHot);
        }

        /// <summary>
        /// 拖进投放区一张 → <b>立刻</b>提交这一张。
        ///
        /// <para>引擎那边换完会原地重发同一条 <c>ChooseReplace</c>（还有额度时），
        /// 所以这个面板不会收起来 —— 玩家的手感是「扔一张、计数减一、接着扔」。</para>
        /// </summary>
        private void OnReplaceDropped(CardView card)
        {
            if (!IsOpen || card == null || !card.HasCard)
            {
                return;
            }

            Option target = FindByUid(card.Card.Uid);
            if (target == null)
            {
                return;
            }

            SetDropZoneHot(false);

            // 拖的是**已标记**那张 → 把标记的整批一起换掉（「选三张一次拖上去」）。
            // 拖的是**没标记**的牌 → 只换它自己（顺手直接拖，不用先点）。
            var payload = new List<int>();
            if (IndexOfOption(target) >= 0)
            {
                for (int i = 0; i < _selected.Count; i++)
                {
                    payload.Add(_selected[i].Index);
                }
            }
            else
            {
                payload.Add(target.Index);
            }

            if (payload.Count == 0)
            {
                return;
            }

            if (Confirmed != null)
            {
                Confirmed(payload);
            }
        }

        /// <summary>投放区的两块底：常态一块、拖入高亮一块，只切 activeSelf。</summary>
        private void SetDropZoneHot(bool on)
        {
            if (_dropZoneIdle != null && _dropZoneIdle.activeSelf != !on)
            {
                _dropZoneIdle.SetActive(!on);
            }

            if (_dropZoneHot != null && _dropZoneHot.activeSelf != on)
            {
                _dropZoneHot.SetActive(on);
            }

            if (_dropZoneLabel != null)
            {
                _dropZoneLabel.color = on ? UiTheme.InkOnIvory : UiTheme.InkMuted;
            }
        }

        private void RefreshDropZoneLabel()
        {
            if (_dropZoneLabel == null)
            {
                return;
            }

            _dropZoneLabel.text = _selected.Count > 1
                ? "↑ 拖到这里，一次换掉这 " + _selected.Count + " 张"
                : (_selected.Count == 1
                    ? "↑ 拖到这里换掉这一张"
                    : "↑ 把要换掉的牌拖到这里");
        }

        private void ShowDropZone(bool on)
        {
            if (!on)
            {
                if (_dropZone != null && _dropZone.gameObject.activeSelf)
                {
                    _dropZone.gameObject.SetActive(false);
                }

                return;
            }

            EnsureDropZone();
            if (_dropZone == null)
            {
                return;
            }

            if (!_dropZone.gameObject.activeSelf)
            {
                _dropZone.gameObject.SetActive(true);
            }

            SetDropZoneHot(false);
            RefreshDropZoneLabel();
        }

        /// <summary>
        /// 建投放区（只建一次）。挂在 Canvas 下、锚在画面底部 ——
        /// 位置由 <see cref="UiLayout.ReplaceZoneY"/> 定，语义是「略高于手牌区」。
        /// </summary>
        private void EnsureDropZone()
        {
            if (_dropZoneBuilt)
            {
                return;
            }

            _dropZoneBuilt = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            Transform parent = canvas != null ? canvas.transform : transform;
            if (parent == null)
            {
                return;
            }

            var root = new GameObject("ReplaceDropZone", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            // 层级插到结算界面之前：`ResultWinRoot` / `ResultLoseRoot` 是 Canvas 的直接子节点，
            // 换牌区默认会被 append 到它们后面 —— 那样一旦某条路径漏了收，一条空槽就压在
            // 「战斗胜利」上面了。放在它们前面只是兜底（正常流程 ClearDecision 会收掉）。
            root.transform.SetSiblingIndex(SiblingBeforeResults(parent));

            _dropZone = (RectTransform)root.transform;
            _dropZone.anchorMin = new Vector2(0.5f, 0f);
            _dropZone.anchorMax = new Vector2(0.5f, 0f);
            _dropZone.pivot = new Vector2(0.5f, 0.5f);
            _dropZone.anchoredPosition = new Vector2(0f, UiLayout.ReplaceZoneY);
            _dropZone.sizeDelta = new Vector2(UiLayout.ReplaceZoneWidth, UiLayout.ReplaceZoneHeight);

            // 底色与边框都收在两块「底」里，靠切 activeSelf 换个样子 ——
            // 第一版只有一层很淡的米白，铺在偏亮的战场上等于没有边界（2026-10-31 实测）。
            _dropZoneIdle = MakeZonePlate(root.transform, UiTheme.IvoryRow, 0.86f, UiTheme.IvoryEdge);
            _dropZoneHot = MakeZonePlate(root.transform, UiTheme.DealConfirm, 0.34f, UiTheme.DealConfirmHot);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(root.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _dropZoneLabel = labelGo.AddComponent<TextMeshProUGUI>();
            if (_counter != null)
            {
                _dropZoneLabel.font = _counter.font;
            }

            _dropZoneLabel.fontSize = UiLayout.FontSizeReplaceZone;
            _dropZoneLabel.fontStyle = FontStyles.Bold;
            _dropZoneLabel.alignment = TextAlignmentOptions.Center;
            _dropZoneLabel.color = UiTheme.InkMuted;
            _dropZoneLabel.raycastTarget = false;
            _dropZoneLabel.text = "↑ 把要换掉的牌拖到这里";

            // 这一块**永远不吃指针**：它是拖拽的落点，不是按钮。
            // 一旦开了 raycastTarget，牌拖到它上面时会被它挡住 DragEnd（那一下会丢）。
            var hit = root.AddComponent<CanvasGroup>();
            hit.blocksRaycasts = false;
            hit.interactable = false;

            root.SetActive(false);
        }

        /// <summary>Canvas 下第一块结算界面的下标（没有就返回末尾）。</summary>
        private static int SiblingBeforeResults(Transform parent)
        {
            if (parent == null)
            {
                return 0;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name.StartsWith("Result", StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return parent.childCount;
        }

        /// <summary>投放区的一块底：半透明填充 + 四条 3px 描边（没有圆角图集也不难看）。</summary>
        private static GameObject MakeZonePlate(Transform parent, Color color, float alpha, Color edge)
        {
            var go = new GameObject("Plate", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(go.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var img = fill.GetComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, alpha);
            img.raycastTarget = false;

            AddZoneEdge(go.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -1.5f), new Vector2(0f, 3f), edge);
            AddZoneEdge(go.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1.5f), new Vector2(0f, 3f), edge);
            AddZoneEdge(go.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1.5f, 0f), new Vector2(3f, 0f), edge);
            AddZoneEdge(go.transform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-1.5f, 0f), new Vector2(3f, 0f), edge);

            return go;
        }

        private static void AddZoneEdge(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offset, Vector2 size, Color color)
        {
            var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private Option FindByUid(int uid)
        {
            for (int i = 0; i < _replaceOptions.Count; i++)
            {
                Option o = _replaceOptions[i];
                if (o.Card != null && o.Card.Uid == uid)
                {
                    return o;
                }
            }

            return null;
        }

        private int IndexOfOption(Option option)
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

        /// <summary>选满之后又点多一张 —— 闪一下计数行，比默默无反应清楚。</summary>
        private void StartFlash()
        {
            if (_counter == null || !isActiveAndEnabled)
            {
                return;
            }

            StopFlash();
            _flashRoutine = StartCoroutine(CoFlashCounter());
        }

        private void StopFlash()
        {
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }

            RefreshCounter();
        }

        private IEnumerator CoFlashCounter()
        {
            _counter.color = UiTheme.InkOnIvoryWarm;
            yield return new WaitForSecondsRealtime(0.32f);
            _flashRoutine = null;
            RefreshCounter();
        }
    }
}
