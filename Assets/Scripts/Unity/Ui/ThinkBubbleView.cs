using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「思考框」——点击怪物时在它头顶弹出的一个提示泡（2026-09-26 新增）。
    ///
    /// <para><b>用户口径</b>：点击怪物模型时弹出一个像「思考框」一样的提示，
    /// 告诉玩家这只怪物<b>下一次进攻会打出哪种元素</b>的牌；约两秒之后渐隐消失。
    /// <b>只显示元素符号，不显示这是哪一张牌</b>（符号取自 <c>Art/Icons/Elements</c>，
    /// 由 <see cref="ElementIconLibrary"/> 按名加载）。</para>
    ///
    /// <para><b>为什么不做成一个「文字气泡」</b>：口径要的是符号本身 ——
    /// 一张元素图比「冰」这个字更直观，也不受字体 / 字号 / 换行的影响
    /// （构建器不用给每个元素配一段文案）。所以框里只有一个
    /// <see cref="Image"/>，没有 <c>TMP_Text</c>。</para>
    ///
    /// <para><b>2026-09-27 改版（用户口径三条）</b>：① 位置从模型头顶正中挪到
    /// <b>模型偏右上角</b>；② <b>去掉那块象牙白的泡体</b>—— 屏幕上只剩元素符号，
    /// 不再有「白色的背景」；③ 符号缩到原来的 <b>40%</b>（104 → 41.6），
    /// 泡体（弹出动画的缩放原点）同比例缩到 67.2。数值全在
    /// <see cref="UiLayout"/> 的 M41 段，由 <see cref="ApplyLayout"/> 在运行时重申。</para>
    ///
    /// <para><b>动画三段</b>（与 <see cref="FloatTipView"/> 同一套节奏口径）：
    /// 弹出（缩放 <c>OutBack</c> + 淡入，很短的 <c>_popSeconds</c>）→ 停留 →
    /// 渐隐（淡出 + 轻微上浮）。<b>用户要求「显示约两秒之后渐隐消失」</b>，
    /// 所以总时长默认取 <c>UiLayout.ThinkBubbleSeconds = 2.0</c>。</para>
    ///
    /// <para><b>本类不含任何规则判断</b>（铁律 3）：该显示哪种元素，
    /// 由 <see cref="BattleUi"/> 从 <c>BattleDriver.ForecastAttackElement</c> 取，
    /// 这里只负责把一张图摆出来。</para>
    ///
    /// <para><b>节点由构建器预建</b>（<c>BattleCanvas/ThinkBubble</c>），
    /// 位置 / 尺寸都能在 Hierarchy 里直接看到 —— 与 <see cref="FloatTipView"/> 同一套做法。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThinkBubbleView : MonoBehaviour
    {
        [Header("构件（场景预建，构建器接线）")]
        [Tooltip("整块提示泡的透明度控制。")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("气泡底图（同时负责缩放动画）。")]
        [SerializeField] private RectTransform _panel;

        [Tooltip("元素符号 —— 唯一的显示内容。")]
        [SerializeField] private Image _icon;

        [Header("节奏（默认值取自 UiLayout，改版式请改那里）")]
        [SerializeField] private float _totalSeconds = UiLayout.ThinkBubbleSeconds;
        [SerializeField] private float _popSeconds = UiLayout.ThinkBubblePopSeconds;
        [SerializeField] private float _rise = UiLayout.ThinkBubbleRise;

        private RectTransform _rect;

        /// <summary>弹出点（prefab 里的位置）—— 每一帧的坐标都从它算，不累积。</summary>
        private Vector2 _home;

        private float _elapsed;
        private bool _playing;

        /// <summary>当前显示的元素（探针 / 自测用）。</summary>
        public CardElement CurrentElement { get; private set; }

        /// <summary>还在显示（探针 / 自测用）。</summary>
        public bool IsPlaying
        {
            get { return _playing; }
        }

        /// <summary>符号图是否真的摆上了（探针用；缺图时为 false）。</summary>
        public bool HasIcon
        {
            get { return _icon != null && _icon.enabled && _icon.sprite != null; }
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            ApplyLayout();
            Hide();
        }

        /// <summary>
        /// 按 <see cref="UiLayout"/> 重申一次版式（位置 / 泡体尺寸 / 符号边长）。
        ///
        /// <para><b>为什么运行时还要再重申</b>：这几个值平时是构建器写进
        /// <c>BattleCanvas.prefab</c> 的，改 <see cref="UiLayout"/> 常量本身不会让已经存盘的
        /// prefab 跟着变 —— 症状是「改了常量、泡纹丝不动」而且<b>零报错</b>
        /// （本项目在商店卡面尺寸上踩过同一个坑，`ShopSlotView.Bind` 也是这么兜的）。
        /// 在这里收口之后，常量就是唯一事实来源：挪泡 / 缩符号只要改常量，
        /// 不必为了一个位置重跑整棵界面的构建器。</para>
        ///
        /// <para><b>为什么连锚点 / pivot 一起写</b>：这两个也是「锚在画面正中」这条口径的一部分
        /// （见 <see cref="UiLayout.ThinkBubbleX"/>）。只改坐标不改锚点，一旦哪天有人在
        /// prefab 里把锚点拖成左下角，坐标就会整体偏出去半个画布，而画面上的表现
        /// 只是「泡不见了」—— 一并重申才闭合。</para>
        /// </summary>
        private void ApplyLayout()
        {
            if (_rect != null)
            {
                _rect.anchorMin = new Vector2(0.5f, 0.5f);
                _rect.anchorMax = new Vector2(0.5f, 0.5f);
                _rect.pivot = new Vector2(0.5f, 0.5f);
                _rect.sizeDelta = new Vector2(UiLayout.ThinkBubbleWidth, UiLayout.ThinkBubbleHeight);
                _home = new Vector2(UiLayout.ThinkBubbleX, UiLayout.ThinkBubbleY);
                _rect.anchoredPosition = _home;
            }

            if (_icon != null)
            {
                RectTransform iconRect = (RectTransform)_icon.transform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(UiLayout.ThinkBubbleIconSize, UiLayout.ThinkBubbleIconSize);
                iconRect.anchoredPosition = Vector2.zero;
            }

            // 上浮量也属于版式（它决定泡「从哪儿起跳」），一并重申 ——
            // 「总时长 / 弹出时长」两条是节奏，仍由构建器经 Configure 注入 prefab。
            _rise = UiLayout.ThinkBubbleRise;
        }

        /// <summary>
        /// 弹出提示泡。<paramref name="element"/> 为 <see cref="CardElement.None"/>
        /// （或该元素缺图）时<b>什么都不做</b> —— 口径上宁可不出提示，
        /// 也不要弹一个空框或者错的符号。
        ///
        /// <para>重复调用会从头开始播（后一次盖掉前一次）：玩家连点怪物时看到的是
        /// 「同一个泡重新弹一次」，而不是几个泡叠在一起。</para>
        /// </summary>
        public void Show(CardElement element)
        {
            if (_group == null || _icon == null || element == CardElement.None)
            {
                return;
            }

            Sprite sprite = ElementIconLibrary.Get(element);
            if (sprite == null)
            {
                // 缺图 —— 静默不出（ElementIconLibrary 已经告警过一次）
                return;
            }

            // 版式按常量重申一遍（换过 UiLayout 常量、但没重跑构建器时靠这一句生效）
            ApplyLayout();

            _icon.sprite = sprite;
            _icon.enabled = true;
            _icon.preserveAspect = true;

            CurrentElement = element;
            _elapsed = 0f;
            _playing = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Apply(1f, 0f, 1f);
        }

        /// <summary>
        /// 立刻收起（重开一局 / 终局 / 构建期初始化用，不要动画）。
        ///
        /// <para>⚠ <b>收到的位置必须是 home 本身（<c>riseT = 0</c>）</b>。这里传 1 的话，
        /// 每次收起都会把它停在「上浮了 <see cref="UiLayout.ThinkBubbleRise"/> 那么多」的那一格上；
        /// 而 <c>Configure</c> 又会把当时的 <c>anchoredPosition</c> 记成新的 home ——
        /// 于是一收一存，位置在 Prefab 里就永久偏了那一段（踩过一次：节点存成 y=902 而不是 866）。</para>
        /// </summary>
        public void Hide()
        {
            _playing = false;
            _elapsed = 0f;
            CurrentElement = CardElement.None;
            Apply(0f, 0f, 0f);

            if (_group != null)
            {
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            float pop = Mathf.Max(0.0001f, _popSeconds);
            float fadeOut = Mathf.Max(0.12f, _totalSeconds * 0.35f);
            float hold = Mathf.Max(0f, _totalSeconds - pop - fadeOut);

            // 透明度：弹出段 0 → 1，停留段恒 1，渐隐段 1 → 0
            float alpha = Mathf.Min(
                Mathf.Clamp01(_elapsed / pop),
                Mathf.Clamp01((_totalSeconds - _elapsed) / fadeOut));

            // 弹出缩放：只在第一段跑 0.72 → 1，之后恒 1
            float popT = UiEase.Evaluate(UiEaseKind.OutBack, Mathf.Clamp01(_elapsed / pop));

            // 上浮与渐隐同步（只在最后一段发生）
            float riseT = UiEase.Evaluate(UiEaseKind.OutQuad,
                Mathf.Clamp01((_elapsed - pop - hold) / fadeOut));

            Apply(alpha, popT, riseT);

            if (_elapsed >= _totalSeconds)
            {
                Hide();
            }
        }

        /// <summary>把透明度 / 缩放 / 上浮三段一起落到节点上。</summary>
        private void Apply(float alpha, float popT, float riseT)
        {
            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01(alpha);
            }

            if (_panel != null)
            {
                // 缩放从 ThinkBubblePopFrom 推到 1 —— 与 MonsterHandView 的面板弹出同一口径
                float s = Mathf.Lerp(UiLayout.ThinkBubblePopFrom, 1f, Mathf.Clamp01(popT));
                _panel.localScale = new Vector3(s, s, 1f);
            }

            if (_rect != null)
            {
                _rect.anchoredPosition = _home + new Vector2(0f, _rise * Mathf.Clamp01(riseT));
            }
        }

        /// <summary>
        /// 构建器接线用。
        ///
        /// <para>⚠ 版式三项（位置 / 泡体尺寸 / 符号边长 / 上浮量）不靠这里的参数定，
        /// 由 <see cref="ApplyLayout"/> 在运行时按 <see cref="UiLayout"/> 重申 ——
        /// 所以改常量立刻见效，不必重跑构建器。<paramref name="rise"/> 只写进 prefab 当默认值
        /// （Inspector 里看得见），运行时会被同一条常量盖掉。</para>
        /// </summary>
        public void Configure(CanvasGroup group, RectTransform panel, Image icon,
            float totalSeconds, float popSeconds, float rise)
        {
            _group = group;
            _panel = panel;
            _icon = icon;
            _totalSeconds = totalSeconds;
            _popSeconds = popSeconds;
            _rise = rise;

            _rect = (RectTransform)transform;
            // 位置 / 尺寸不在 prefab 里「就地取」，而是按 UiLayout 重申 ——
            // 这样构建器写进 prefab 的值与运行时用的值永远是同一份（见 ApplyLayout）。
            ApplyLayout();
            Hide();
        }
    }
}
