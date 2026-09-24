using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 怪物（AI 座位）「刚打出的牌」头顶展示（M13 起，M20 重做版式与消失时机）。
    ///
    /// <para><b>为什么要有它</b>：对方手牌不可见，打出一张牌只有顶部提示条一行字，
    /// 玩家很难把「字」跟「哪张卡」对上。所以对面每次打出（进攻或防御），
    /// 都在<b>头顶</b>弹出那张卡面 —— 卡照样进冷却区，两边都看得见。</para>
    ///
    /// <para><b>摆位</b>：挂在 ArtLayer 上（与血条徽标同层）的<b>固定锚点</b>，
    /// 不挂在角色节点下 —— 角色的 pivot 随动作帧变，挂进去会跟着抖（M11 踩过的坑）。
    /// 怪物不动，所以固定位就够。</para>
    ///
    /// <para><b>M20 版式（2026-09-20 用户口径）</b>：</para>
    /// <list type="bullet">
    /// <item><b>只出一张就不再按两张的版式摆</b>：单张时卡位居中（原来坐在 −63 那个左卡位上，
    /// 右边永远空一格），衬底也按 1 张收缩（128 宽，而不是双张的 254）。</item>
    /// <item><b>两张只在「防御双发」时出现</b>：进攻永远只有一张牌，防御要交两张才是并排。
    /// 超过两张（规则上不存在）只显示前两张。</item>
    /// </list>
    ///
    /// <para><b>M20 时长（同一条用户口径）</b>：</para>
    /// <list type="bullet">
    /// <item><b>进攻牌常驻</b>：<c>holdSeconds = 0</c> —— 一直挂到玩家把这一刀挡下 /
    /// 选择掉血为止（<c>BattleUi</c> 在防御结算那一拍 <see cref="Hide"/>）。</item>
    /// <item><b>防御牌定长</b>：停 <see cref="UiLayout.PlayedCardDefenseHold"/> 秒再淡出，
    /// 并且这整段时长会经 <c>BattleDriver.BeatHold</c> 变成一道节拍闸门 ——
    /// 下一回合要等它停完（含淡出）才开始。</item>
    /// </list>
    ///
    /// <para>⚠ 卡位（<c>Card1</c> / <c>Card2</c>）的坐标与尺寸<b>由本类在运行时算</b>，
    /// 不再读 prefab 里那对手调的 −63 / +63 —— 单张与双张是两套版式，静态位置表达不了。
    /// 想调尺寸/间距/留白请改 <see cref="UiLayout"/> 的 M13/M20 段（那是唯一来源）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayedCardView : MonoBehaviour
    {
        [Header("节点")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private RectTransform _cardsRoot;
        [SerializeField] private Image[] _faces = new Image[0];
        [SerializeField] private CanvasGroup _group;

        [Header("节奏")]
        [Tooltip("默认停留秒数（Show 不显式给时长时用它），到点开始淡出。")]
        [SerializeField] private float _holdSeconds = 2f;
        [Tooltip("淡出秒数。")]
        [SerializeField] private float _fadeSeconds = 0.35f;

        /// <summary>本次停留时长；<c>&lt;= 0</c> = 常驻（要上层显式 <see cref="Hide"/>）。</summary>
        private float _autoHold;

        private float _timer;

        /// <summary>
        /// 每个卡位当前挂着的牌的 UID（<c>-1</c> = 该卡位空），<b>下标与 <see cref="_faces"/> 严格一一对应</b>（M35）。
        ///
        /// <para><b>为什么必须记住它</b>：这张牌离场的方式已经改成「从头顶飞到冷却槽的飞行卡」，
        /// 而 <c>CardTransitView</c> 的飞行是<b>按 UID 配对</b>的 —— 它要知道「此刻头顶这张
        /// 是哪张牌、占屏的那块矩形在哪」才能把飞行起点摆对。</para>
        ///
        /// <para>⚠ <b>不能用「UID 列表 + 下标即卡位」那种写法</b>：双发一次交两张，
        /// 第一张先撤下之后列表会缩短，第二张的下标就与卡位错开一位，
        /// <see cref="TryGetFaceRect"/> 会去问一个已经失活的卡位、恒返回 false ——
        /// 表现是「第一张飞走了、第二张永远留在头顶」。所以这里按卡位存、空位填 -1。</para>
        /// </summary>
        private int[] _faceUid = new int[0];

        // ── M36 · 「从角色模型升上来」的入场动画 ────────────────
        //
        //  用户口径（2026-09-23）：「加入 AI 的出牌动画（从其模型，向上移动，并逐渐出现）」。
        //  只在**怪物**一侧调用（见 BattleUi.ShowOnHead）—— 玩家自己打出的牌是从手牌区
        //  拖上去的，起点另有其物，而且玩家自己知道打了什么。
        //
        //  ⚠ 这里**不新增任何 [SerializeField] 字段**：起点由调用方给（它是每次都不同的世界坐标），
        //    时长与起始缩放读 UiLayout 常量。这样这次改动不需要重建 Prefab / 场景。

        /// <summary>弹出点（= prefab 里那个 anchoredPosition）是否已经记下来了。</summary>
        private bool _homeCaptured;

        /// <summary>头顶卡位的本地面板位置 —— 入场动画的**终点**。</summary>
        private Vector2 _home;

        private bool _introOn;
        private float _introElapsed;

        /// <summary>入场起点相对 <see cref="_home"/> 的偏移（面板坐标）。</summary>
        private Vector2 _introOffset;

        // ⚠ 故意**没有** Awake 里调 Hide()（2026-09-20 修）：
        //   根节点在构建器里就是失活的（Configure → Hide），而 Awake 要等 GameObject
        //   第一次被激活才跑 —— 也就是 <see cref="Show"/> 里那句 SetActive(true)
        //   会同步把 Awake 叫起来，Awake 里的 Hide() 转头就把刚点亮的面板按灭；
        //   Show 剩下的代码照跑（布局、贴图、alpha 都设好了），屏幕上却什么都没有。
        //   症状特别误导：**本局第一次 Show 不显示、第二次起正常**（实测确认过）。
        //   DropArrowView 2026-09-17 踩过同一脚，那边注释写的就是这一条。

        /// <summary>面板当前是否可见（探针 / 自测用）。</summary>
        public bool IsVisible
        {
            get { return _panel != null && _panel.activeSelf; }
        }

        /// <summary>按默认停留时长显示（等同 <c>Show(cards, _holdSeconds)</c>）。</summary>
        public void Show(IReadOnlyList<CardSnapshot> cards)
        {
            Show(cards, _holdSeconds);
        }

        /// <summary>
        /// 显示怪物刚打出的一批牌（进攻 1 张 / 防御双发 2 张）。
        ///
        /// <para><paramref name="holdSeconds"/> <b>传 0 表示常驻</b>：不自动淡出，
        /// 由上层在合适的时机（玩家防御完成 / 掉落血）调 <see cref="Hide"/>。
        /// 传正数则停这么久之后自动淡出，总占用 = 本值 + <see cref="UiLayout.PlayedCardFade"/>。</para>
        ///
        /// <para>传空列表等于隐藏。</para>
        /// </summary>
        public void Show(IReadOnlyList<CardSnapshot> cards, float holdSeconds)
        {
            if (cards == null || cards.Count <= 0 || _faces == null || _faces.Length == 0)
            {
                Hide();
                return;
            }

            if (_panel != null)
            {
                _panel.SetActive(true);
            }

            int shown = Mathf.Min(cards.Count, _faces.Length);
            Layout(shown);

            CardArtLibrary lib = CardArtLibrary.Instance;

            for (int i = 0; i < _faces.Length; i++)
            {
                Image face = _faces[i];
                if (face == null)
                {
                    continue;
                }

                bool exists = i < shown;
                face.gameObject.SetActive(exists);

                if (!exists)
                {
                    face.sprite = null;
                    continue;
                }

                Sprite sprite = lib == null ? null : lib.GetArt(cards[i].CardId);

                if (sprite != null)
                {
                    face.sprite = sprite;
                    face.color = Color.white;
                }
                else
                {
                    // 没映射表就退化成纯色块，至少还知道「这儿有一张牌」
                    face.sprite = null;
                    face.color = UiTheme.MiniCardFace;
                }

                face.preserveAspect = true;
            }

            // 登记本次展示的牌（按卡位存）—— 它离场时要靠这份 uid 把「飞行起点」
            // 交给 CardTransitView（见 M35：头顶 → 冷却槽的飞行卡）。
            EnsureFaceUidSize();
            for (int i = 0; i < _faceUid.Length; i++)
            {
                _faceUid[i] = i < shown ? cards[i].Uid : -1;
            }

            _timer = 0f;
            _autoHold = holdSeconds;
            SetAlpha(1f);

            // 入场动画属于「上一趟」的事：新一趟 Show 必须把它收干净，否则面板会带着
            // 上一张牌的偏移量出现（双发第二张、或连击追加时最容易看出来）。
            ResetIntro();
        }

        /// <summary>
        /// 从某个世界点**升起**到头顶卡位（M36）—— 「怪物打出的牌从它的模型升上来」。
        ///
        /// <para><b>怎么读</b>：起点 = 怪物模型当前帧的中心，终点 = 头顶卡位（<c>y = 902</c>），
        /// 期间从 0 淡到 1、从小长到原尺寸 —— 三件事合起来才像「从怪物身上冒出来」，
        /// 只做位移会像「一张牌从下面滑上来」。</para>
        ///
        /// <para><b>起点换算为什么不手算</b>：面板挂在 ArtLayer（铺满画布）下、锚点是左下，
        /// 而锚点参考点在父级局部空间里是 <c>(-960,-540)</c> —— 手算要处理这一层，
        /// 错一个符号卡片就从屏幕外飞进来。这里用「先把 <c>position</c> 摆到那个世界点、
        /// 读回 <c>anchoredPosition</c>」让 Unity 自己算，再复位。</para>
        ///
        /// <para>没接线 / 面板不可见时退回普通 <see cref="Show(IReadOnlyList{CardSnapshot},float)"/>
        /// —— 少一段动效，但牌必须照样显示出来。</para>
        /// </summary>
        public void ShowFrom(IReadOnlyList<CardSnapshot> cards, float holdSeconds, Vector3 fromWorld)
        {
            Show(cards, holdSeconds);

            RectTransform rt = _panel == null ? null : _panel.transform as RectTransform;
            if (rt == null || !_panel.activeSelf)
            {
                return;
            }

            CaptureHome(rt);

            rt.position = fromWorld;
            Vector2 from = rt.anchoredPosition;
            rt.anchoredPosition = _home;

            _introOffset = from - _home;
            _introElapsed = 0f;
            _introOn = true;

            // 起点那一帧就要是「还没出来」的样子，不能先亮一下再淡入
            rt.localScale = Vector3.one * UiLayout.PlayedCardIntroStartScale;
            SetAlpha(0f);
        }

        /// <summary>把面板的「家」位置记下来（只记一次 —— 之后每一帧都从它算，不累积）。</summary>
        private void CaptureHome(RectTransform rt)
        {
            if (_homeCaptured)
            {
                return;
            }

            _home = rt.anchoredPosition;
            _homeCaptured = true;
        }

        /// <summary>收掉入场动画，把面板摆回家、缩放归 1。</summary>
        private void ResetIntro()
        {
            _introOn = false;
            _introElapsed = 0f;
            _introOffset = Vector2.zero;

            RectTransform rt = _panel == null ? null : _panel.transform as RectTransform;
            if (rt == null)
            {
                return;
            }

            if (_homeCaptured)
            {
                rt.anchoredPosition = _home;
            }

            rt.localScale = Vector3.one;
        }

        public void Hide()
        {
            if (_panel != null && _panel.activeSelf)
            {
                _panel.SetActive(false);
            }

            for (int i = 0; i < _faceUid.Length; i++)
            {
                _faceUid[i] = -1;
            }

            _timer = 0f;
            _autoHold = 0f;

            // 收掉时也要把入场动画复位：面板中途被收起（例如这张牌进冷却区飞走了），
            // 下一次 Show 不能带着上一次的半截偏移与缩放。
            ResetIntro();
            SetAlpha(1f);
        }

        /// <summary><see cref="_faceUid"/> 的长度跟着卡位数走（构建器改了卡位数才需要重建）。</summary>
        private void EnsureFaceUidSize()
        {
            if (_faces == null || _faceUid.Length == _faces.Length)
            {
                return;
            }

            _faceUid = new int[_faces.Length];
            for (int i = 0; i < _faceUid.Length; i++)
            {
                _faceUid[i] = -1;
            }
        }

        /// <summary>
        /// 取某个 UID 此刻在头顶占屏的那块矩形（M35）。
        ///
        /// <para><b>用途</b>：<c>CardTransitView</c> 拿它当「头顶 → 冷却槽」那趟飞行的<b>起点</b>。
        /// 与它读手牌卡 / 冷却卡用的是同一套 <c>ReadPose</c>，所以三种起点天然同口径
        /// —— 卡宽差一截也没关系，飞行途中会平滑缩放到落点尺寸。</para>
        ///
        /// <para>返回 <c>false</c> = 这一侧头顶此刻没有这张牌（<paramref name="rect"/> 为 null）。</para>
        /// </summary>
        public bool TryGetFaceRect(int uid, out RectTransform rect)
        {
            rect = null;

            for (int i = 0; i < _faceUid.Length && i < _faces.Length; i++)
            {
                if (_faceUid[i] != uid || _faces[i] == null)
                {
                    continue;
                }

                if (!_faces[i].gameObject.activeSelf)
                {
                    return false;
                }

                rect = _faces[i].transform as RectTransform;
                return rect != null;
            }

            return false;
        }

        /// <summary>
        /// 把若干张牌<b>从头顶撤下</b>（M35）—— 它们已经在往冷却槽飞，原位不能同时留一份。
        ///
        /// <para>只把对应卡位失活、<b>不重排版式</b>：双发那两张几乎总是同时离场，
        /// 重排会让还没轮到的那一张先弹一下。撤空了才收起整个面板。</para>
        /// </summary>
        public void HideFaces(IReadOnlyList<int> uids)
        {
            if (uids == null || uids.Count == 0)
            {
                return;
            }

            for (int u = 0; u < uids.Count; u++)
            {
                for (int i = 0; i < _faceUid.Length && i < _faces.Length; i++)
                {
                    if (_faceUid[i] != uids[u])
                    {
                        continue;
                    }

                    _faceUid[i] = -1;

                    if (_faces[i] != null)
                    {
                        _faces[i].gameObject.SetActive(false);
                        _faces[i].sprite = null;
                    }

                    break;
                }
            }

            // 头顶还有牌挂着（比如双发只飞走了一张）→ 面板留着
            for (int i = 0; i < _faceUid.Length; i++)
            {
                if (_faceUid[i] >= 0)
                {
                    return;
                }
            }

            Hide();
        }

        /// <summary>构建器接线用。</summary>
        public void Configure(GameObject panel, RectTransform cardsRoot, Image[] faces,
            CanvasGroup group, float holdSeconds, float fadeSeconds)
        {
            _panel = panel;
            _cardsRoot = cardsRoot;
            _faces = faces;
            _group = group;
            _holdSeconds = holdSeconds;
            _fadeSeconds = fadeSeconds;

            Hide();
        }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            // 入场动画与自动淡出是两段独立的插值，都要走 ——
            // ⚠ 不能像旧版那样在 _autoHold <= 0 时整体早退：头顶出牌展示是**常驻**的
            //   （hold = 0），一早就退的话入场动画永远不会播放。
            if (_introOn)
            {
                StepIntro(dt);
            }

            if (_autoHold <= 0f)
            {
                // 常驻态（_autoHold <= 0）不参与倒计时 —— 什么时候收由上层说了算
                return;
            }

            _timer += dt;

            if (_timer <= _autoHold)
            {
                return;
            }

            float t = (_timer - _autoHold) / Mathf.Max(0.01f, _fadeSeconds);
            SetAlpha(1f - Mathf.Clamp01(t));

            if (t >= 1f)
            {
                Hide();
            }
        }

        /// <summary>推进「从模型升起来」这一段：位移（OutCubic）+ 缩放 + 透明度同步走完。</summary>
        private void StepIntro(float dt)
        {
            RectTransform rt = _panel == null ? null : _panel.transform as RectTransform;
            if (rt == null)
            {
                _introOn = false;
                return;
            }

            _introElapsed += dt;

            float k = Mathf.Clamp01(_introElapsed / Mathf.Max(0.01f, UiLayout.PlayedCardIntroSeconds));
            float e = UiEase.Evaluate(UiEaseKind.OutCubic, k);

            rt.anchoredPosition = _home + _introOffset * (1f - e);
            rt.localScale = Vector3.one * Mathf.LerpUnclamped(
                UiLayout.PlayedCardIntroStartScale, 1f, e);
            SetAlpha(e);

            if (k >= 1f)
            {
                _introOn = false;
                rt.anchoredPosition = _home;
                rt.localScale = Vector3.one;
                SetAlpha(1f);
            }
        }

        private void SetAlpha(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01(alpha);
            }
        }

        /// <summary>
        /// 按张数摆卡位与衬底（M20）。
        ///
        /// <list type="bullet">
        /// <item><b>1 张</b>：<c>Card1</c> 移到面板正中，衬底收缩到「1 张 + 两侧留白」。</item>
        /// <item><b>2 张</b>：<c>Card1</c> / <c>Card2</c> 左右对称并排，衬底 = 两张 + 间隙 + 留白。</item>
        /// </list>
        ///
        /// <para>尺寸与间距一律取自 <see cref="UiLayout"/>（版式唯一来源），
        /// 这里不缓存也不暴露 Inspector 值 —— 免得出现「改了一处、另一处还是老数」的第二套口径。</para>
        /// </summary>
        public void Layout(int count)
        {
            if (_faces == null)
            {
                return;
            }

            count = Mathf.Clamp(count, 0, _faces.Length);
            bool single = count <= 1;

            float scale = single ? Mathf.Max(0.1f, UiLayout.PlayedCardSingleScale) : 1f;
            float w = UiLayout.PlayedCardWidth * scale;
            float h = UiLayout.PlayedCardHeight * scale;

            for (int i = 0; i < _faces.Length; i++)
            {
                Image face = _faces[i];
                if (face == null)
                {
                    continue;
                }

                bool on = i < count;
                face.gameObject.SetActive(on);

                if (!on)
                {
                    continue;
                }

                RectTransform rt = face.transform as RectTransform;
                if (rt == null)
                {
                    continue;
                }

                rt.sizeDelta = new Vector2(w, h);
                // 单张居中；双张左右对称（±(卡宽+间隙)/2）
                float x = single ? 0f : (i == 0 ? -1f : 1f) * (w + UiLayout.PlayedCardGap) * 0.5f;
                rt.anchoredPosition = new Vector2(x, 0f);
            }

            // 面板本体就是衬底的外框：内容 + 两侧留白。单张时它比双张窄一半，
            // 「展示区域大小适配」说的就是这一步 —— 不能留一格空位给不存在的第二张牌。
            RectTransform panelRt = _panel == null ? null : _panel.transform as RectTransform;
            if (panelRt != null)
            {
                float content = single ? w : w * 2f + UiLayout.PlayedCardGap;
                panelRt.sizeDelta = new Vector2(content + UiLayout.PlayedCardPadding * 2f,
                    h + UiLayout.PlayedCardPadding * 2f);
            }
        }
    }
}
