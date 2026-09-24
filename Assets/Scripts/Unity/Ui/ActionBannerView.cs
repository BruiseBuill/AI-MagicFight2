using TMPro;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 屏幕中央的「行动提示」横幅（2026-09-19）。
    ///
    /// <para><b>用户口径</b>：「轮到你进攻」「进攻结束」这类一句话提示，
    /// <b>在屏幕中间闪一下就够了</b> —— 不做常驻、不需要点确认。
    /// 常驻的叙事仍然走顶部提示条（<see cref="StageView.SetPrompt"/>），
    /// 本类只管「这一拍轮到谁 / 这一拍结束了」这种一眼必须看到的时刻。</para>
    ///
    /// <para><b>节点在场景里预建</b>（<c>BattleCanvas/ActionBanner</c>，由 M8 构建器建出来）：
    /// 文案 / 字号 / 底色 / 位置都能在 Hierarchy 里直接看到并调整，
    /// 所以<b>不运行时 new</b>。本类只做淡入淡出，不含任何规则判断（铁律 3）——
    /// 该不该提示、提示什么，由 <see cref="BattleUi"/> 说了算。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionBannerView : MonoBehaviour
    {
        [Header("构件（场景预建，构建器接线）")]
        [Tooltip("整条横幅的透明度控制。")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("文案。")]
        [SerializeField] private TMP_Text _label;

        [Header("节奏")]
        [Tooltip("整条横幅从出现到消失的总时长（含淡入淡出）。")]
        [SerializeField] private float _totalSeconds = 1.3f;

        [Tooltip("淡入 / 淡出各占的时长。")]
        [SerializeField] private float _fadeSeconds = 0.2f;

        [Tooltip("消失前向上飘的距离（px）—— 让它「闪一下就走」，而不是杵在那。")]
        [SerializeField] private float _riseDistance = 14f;

        private RectTransform _rect;
        private Vector2 _home;
        private float _elapsed;
        private bool _playing;

        /// <summary>还在闪（探针 / 自测用）。</summary>
        public bool IsPlaying
        {
            get { return _playing; }
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            Hide();
        }

        /// <summary>把一句话闪一下。重复调用会从头开始（后一句盖掉前一句）。</summary>
        public void Show(string message)
        {
            if (_label != null)
            {
                _label.text = message ?? string.Empty;
            }

            _elapsed = 0f;
            _playing = true;
            Apply(0f);
        }

        public void Hide()
        {
            _playing = false;
            _elapsed = 0f;
            Apply(0f);
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            float fade = Mathf.Max(_fadeSeconds, 0.0001f);
            float fadeIn = Mathf.Clamp01(_elapsed / fade);
            float fadeOut = Mathf.Clamp01((_totalSeconds - _elapsed) / fade);
            Apply(Mathf.Min(fadeIn, fadeOut));

            if (_elapsed >= _totalSeconds)
            {
                Hide();
            }
        }

        /// <summary>把透明度落到整条横幅上（文字与底衬都跟着 <see cref="_group"/>）。</summary>
        private void Apply(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = alpha;
            }

            if (_rect != null)
            {
                // alpha = 0 时停在「抬起」的位置，淡入时从上往下落到 _home —— 有「落下来」的手感
                _rect.anchoredPosition = _home + new Vector2(0f, _riseDistance * (1f - alpha));
            }
        }
    }
}
