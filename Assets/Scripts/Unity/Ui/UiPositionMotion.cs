using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>Retargetable anchored motion for manually laid out UI, including reparenting.</summary>
    [DisallowMultipleComponent]
    public sealed class UiPositionMotion : MonoBehaviour
    {
        /// <summary>标准位移时长（秒）。</summary>
        public const float MoveSeconds = 0.24f;

        private RectTransform _rect;
        private Vector2 _from;
        private Vector2 _target;
        private float _elapsed;

        /// <summary>
        /// 这一次补间走多久（秒）。
        ///
        /// <para>2026-10-31：原来恒等于 <see cref="MoveSeconds"/>。冷却区需要「玩家自己被减速」
        /// 那一格走得慢一点（用户口径：至少 0.7 秒，不然很难注意到），而其它牌不该陪着变慢 ——
        /// 所以时长改成每次请求可带，默认仍然是 <see cref="MoveSeconds"/>，行为不变。</para>
        /// </summary>
        private float _duration = MoveSeconds;

        private bool _moving;
        private Transform _parent;

        public static void MoveLocalTo(RectTransform rect, Vector3 target)
        {
            MoveTo(rect, rect.anchoredPosition + (Vector2)(target - rect.localPosition));
        }

        public static void MoveTo(RectTransform rect, Vector2 target, bool animate = true,
            float seconds = MoveSeconds)
        {
            if (rect == null) return;
            var motion = rect.GetComponent<UiPositionMotion>();
            if (!Application.isPlaying || !rect.gameObject.activeInHierarchy || !animate)
            {
                if (motion != null) motion._moving = false;
                rect.anchoredPosition = target;
                return;
            }
            if (motion == null) motion = rect.gameObject.AddComponent<UiPositionMotion>();
            float duration = Mathf.Max(0.01f, seconds);
            if (motion._moving && motion._target == target && motion._parent == rect.parent
                && Mathf.Approximately(motion._duration, duration)) return;
            motion._rect = rect;
            motion._parent = rect.parent;
            motion._from = rect.anchoredPosition;
            motion._target = target;
            motion._duration = duration;
            motion._elapsed = 0f;
            motion._moving = true;
        }

        public static void FromWorld(RectTransform rect, Vector3 from, Vector2 target, bool animate = true,
            float seconds = MoveSeconds)
        {
            if (animate && Application.isPlaying && rect.gameObject.activeInHierarchy) rect.position = from;
            MoveTo(rect, target, animate, seconds);
        }

        public static void Cancel(RectTransform rect)
        {
            var motion = rect == null ? null : rect.GetComponent<UiPositionMotion>();
            if (motion != null) motion._moving = false;
        }

        private void Update()
        {
            if (!_moving) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            _rect.anchoredPosition = Vector2.LerpUnclamped(_from, _target, UiEase.Evaluate(UiEaseKind.OutCubic, t));
            if (t >= 1f) _moving = false;
        }

        private void OnDisable() { _moving = false; }
    }
}
