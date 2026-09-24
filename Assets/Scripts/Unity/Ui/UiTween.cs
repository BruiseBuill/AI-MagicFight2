using System;
using System.Collections;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>缓动曲线。</summary>
    public enum UiEaseKind
    {
        Linear = 0,
        OutQuad = 1,
        InQuad = 2,
        OutCubic = 3,
        InCubic = 4,
        InOutCubic = 5,
        OutBack = 6,
    }

    public static class UiEase
    {
        public static float Evaluate(UiEaseKind kind, float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 1f;
            }

            switch (kind)
            {
                case UiEaseKind.OutQuad:
                    return 1f - (1f - t) * (1f - t);

                case UiEaseKind.InQuad:
                    return t * t;

                case UiEaseKind.InCubic:
                    return t * t * t;

                case UiEaseKind.InOutCubic:
                    return t < 0.5f
                        ? 4f * t * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

                case UiEaseKind.OutBack:
                {
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1f;
                    float u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }

                default:
                    return t;
            }
        }
    }

    /// <summary>
    /// 动画基类（M7 的「动画基类」交付项）。
    ///
    /// 刻意不依赖 DOTween：内核与表现层的分层已经够复杂，动画这一层保持零第三方依赖，
    /// 用协程 + 无缩放时间驱动，M8 的演出直接继承它写具体动画即可。
    ///
    /// 用法：
    /// <code>
    /// var t = go.AddComponent&lt;TweenScale&gt;();
    /// t.Setup(Vector3.one * 0.8f, Vector3.one, 0.18f);
    /// t.Play();
    /// </code>
    /// </summary>
    public abstract class UiTween : MonoBehaviour
    {
        [SerializeField]
        protected float Duration = 0.2f;

        [SerializeField]
        protected UiEaseKind Ease = UiEaseKind.OutCubic;

        [SerializeField]
        protected float Delay;

        private Coroutine _routine;

        public bool IsPlaying { get; private set; }

        /// <summary>播放结束回调。</summary>
        public event Action Finished;

        /// <summary>按缓动后的 t ∈ [0,1] 取样。子类实现具体插值。</summary>
        protected abstract void OnSample(float eased);

        protected virtual void OnPlayStart()
        {
        }

        public void Play()
        {
            Stop();
            if (!isActiveAndEnabled)
            {
                // 组件未启用时直接落到终态，避免状态卡在中间
                OnSample(1f);
                return;
            }

            _routine = StartCoroutine(Run());
        }

        /// <summary>立刻跳到终态（跳过动画）—— 设置里「关闭动画」或跳过演出时用。</summary>
        public void Snap()
        {
            Stop();
            OnSample(1f);
            RaiseFinished();
        }

        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            IsPlaying = false;
        }

        private IEnumerator Run()
        {
            IsPlaying = true;
            OnPlayStart();
            OnSample(0f);

            float waited = 0f;
            while (waited < Delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            float elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Duration <= 0f ? 1f : Mathf.Clamp01(elapsed / Duration);
                OnSample(UiEase.Evaluate(Ease, t));
                yield return null;
            }

            OnSample(1f);
            _routine = null;
            IsPlaying = false;
            RaiseFinished();
        }

        private void RaiseFinished()
        {
            IsPlaying = false;
            Action handler = Finished;
            if (handler != null)
            {
                handler();
            }
        }
    }

    /// <summary>缩放插值（打出 / 选中时的弹一下）。</summary>
    public sealed class TweenScale : UiTween
    {
        [SerializeField] private Vector3 _from = Vector3.one;
        [SerializeField] private Vector3 _to = Vector3.one;

        public void Setup(Vector3 from, Vector3 to, float duration, UiEaseKind ease = UiEaseKind.OutBack)
        {
            _from = from;
            _to = to;
            Duration = duration;
            Ease = ease;
        }

        protected override void OnSample(float eased)
        {
            transform.localScale = Vector3.LerpUnclamped(_from, _to, eased);
        }
    }

    /// <summary>位置插值（在布局组里搬牌时用 RectTransform.anchoredPosition）。</summary>
    public sealed class TweenAnchoredPosition : UiTween
    {
        [SerializeField] private Vector2 _from;
        [SerializeField] private Vector2 _to;
        private RectTransform _rt;

        public void Setup(Vector2 from, Vector2 to, float duration, UiEaseKind ease = UiEaseKind.OutCubic)
        {
            _from = from;
            _to = to;
            Duration = duration;
            Ease = ease;
        }

        protected override void OnSample(float eased)
        {
            if (_rt == null)
            {
                _rt = transform as RectTransform;
            }

            if (_rt != null)
            {
                _rt.anchoredPosition = Vector2.LerpUnclamped(_from, _to, eased);
            }
        }
    }

    /// <summary>透明度插值。</summary>
    public sealed class TweenCanvasAlpha : UiTween
    {
        [SerializeField] private float _from;
        [SerializeField] private float _to = 1f;
        private CanvasGroup _group;

        public void Setup(float from, float to, float duration, UiEaseKind ease = UiEaseKind.OutQuad)
        {
            _from = from;
            _to = to;
            Duration = duration;
            Ease = ease;
        }

        protected override void OnSample(float eased)
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
            }

            if (_group != null)
            {
                _group.alpha = Mathf.LerpUnclamped(_from, _to, eased);
            }
        }
    }
}
