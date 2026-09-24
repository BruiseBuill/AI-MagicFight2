using TMPro;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 屏幕中央的**一句话浮字**（M36）。
    ///
    /// <para><b>用户口径（2026-09-23）</b>：对一张本拍<b>不可能被加速 / 减速</b>的牌操作时，
    /// 「应当弹出一段无法被加速或减速的文字，此文字应当是在中间弹出，
    /// 然后向上移动且迅速变透明消失」。</para>
    ///
    /// <para><b>与 <see cref="ActionBannerView"/> 的分工</b>：横幅讲「现在轮到谁 / 这一拍结束了」
    /// —— 是你必须抬眼看的事，所以大、亮、占满整条；本类只回答一个问题
    /// （「为什么这张牌点不动」），所以小、轻、说完就走。两者可以同屏，互不遮挡。</para>
    ///
    /// <para><b>节点在场景里预建</b>（<c>BattleCanvas/FloatTip</c>，由 M8 构建器建出来）：
    /// 文案 / 字号 / 底色 / 位置都能在 Hierarchy 里直接看到并调整，所以<b>不运行时 new</b>。
    /// 本类不含任何规则判断（铁律 3）—— 该不该提示、提示什么，由 <see cref="BattleUi"/> 说了算。</para>
    ///
    /// <para><b>动画三段</b>：淡入（0.10 s，原地弹出）→ 停留 → 边上升边淡出。
    /// 竖向上「弹出点」就是它在 prefab 里的位置，上升只在最后一段发生 ——
    /// 所以玩家读到的顺序是「在这儿出现 → 抬起来 → 没了」，而不是一出现就在往上飘。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloatTipView : MonoBehaviour
    {
        [Header("构件（场景预建，构建器接线）")]
        [Tooltip("整块浮字的透明度控制。")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("文案。")]
        [SerializeField] private TMP_Text _label;

        [Header("节奏（默认值取自 UiLayout，改版式请改那里）")]
        [SerializeField] private float _totalSeconds = UiLayout.FloatTipSeconds;
        [SerializeField] private float _fadeInSeconds = UiLayout.FloatTipFadeInSeconds;
        [SerializeField] private float _rise = UiLayout.FloatTipRise;

        private RectTransform _rect;

        /// <summary>弹出点（prefab 里的位置）—— 每一段的坐标都从它算，不累积。</summary>
        private Vector2 _home;

        private float _elapsed;
        private bool _playing;

        /// <summary>还在闪（探针 / 自测用）。</summary>
        public bool IsPlaying
        {
            get { return _playing; }
        }

        /// <summary>当前显示的文案（探针 / 自测用）。</summary>
        public string CurrentText
        {
            get { return _label == null ? string.Empty : _label.text; }
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            Hide();
        }

        /// <summary>
        /// 把一句话闪一下。重复调用会从头开始（后一句盖掉前一句）——
        /// 玩家连点好几张不可选的牌时，看到的是「同一句话重新弹一次」，
        /// 而不是几句话叠在一起。
        /// </summary>
        public void Show(string message)
        {
            if (_group == null || _label == null)
            {
                // 没接线就什么都不做：这类缺失的后果是多一句提示没有，不值得抛异常打断对局
                return;
            }

            _label.text = message ?? string.Empty;

            _elapsed = 0f;
            _playing = true;
            Apply(1f, 0f);
        }

        public void Hide()
        {
            _playing = false;
            _elapsed = 0f;
            Apply(0f, 0f);
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            float fadeIn = Mathf.Max(0.0001f, _fadeInSeconds);
            float fadeOut = Mathf.Max(0.12f, _totalSeconds * 0.45f);
            float hold = Mathf.Max(0f, _totalSeconds - fadeIn - fadeOut);

            // 出现（0 → 1）与离开（1 → 0）取较小者 —— 停留段里两者都是 1
            float a = Mathf.Min(Mathf.Clamp01(_elapsed / fadeIn),
                Mathf.Clamp01((_totalSeconds - _elapsed) / fadeOut));

            // 上升只发生在「停留结束之后」那一段，与淡出同步
            float riseT = UiEase.Evaluate(UiEaseKind.OutQuad,
                Mathf.Clamp01((_elapsed - fadeIn - hold) / fadeOut));

            Apply(a, riseT);

            if (_elapsed >= _totalSeconds)
            {
                Hide();
            }
        }

        /// <summary>把透明度与「升到哪一段」一起落到整块浮字上。</summary>
        private void Apply(float alpha, float riseT)
        {
            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01(alpha);
            }

            if (_rect != null)
            {
                _rect.anchoredPosition = _home + new Vector2(0f, _rise * Mathf.Clamp01(riseT));
            }
        }

        /// <summary>构建器接线用。</summary>
        public void Configure(CanvasGroup group, TMP_Text label,
            float totalSeconds, float fadeInSeconds, float rise)
        {
            _group = group;
            _label = label;
            _totalSeconds = totalSeconds;
            _fadeInSeconds = fadeInSeconds;
            _rise = rise;

            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            Hide();
        }
    }
}
