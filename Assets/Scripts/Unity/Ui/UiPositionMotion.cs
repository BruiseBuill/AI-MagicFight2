using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>Retargetable anchored motion for manually laid out UI, including reparenting.</summary>
    [DisallowMultipleComponent]
    public sealed class UiPositionMotion : MonoBehaviour
    {
        public const float MoveSeconds = 0.24f;
        private RectTransform _rect;
        private Vector2 _from;
        private Vector2 _target;
        private float _elapsed;
        private bool _moving;
        private Transform _parent;

        public static void MoveLocalTo(RectTransform rect, Vector3 target)
        {
            MoveTo(rect, rect.anchoredPosition + (Vector2)(target - rect.localPosition));
        }

        public static void MoveTo(RectTransform rect, Vector2 target, bool animate = true)
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
            if (motion._moving && motion._target == target && motion._parent == rect.parent) return;
            motion._rect = rect;
            motion._parent = rect.parent;
            motion._from = rect.anchoredPosition;
            motion._target = target;
            motion._elapsed = 0f;
            motion._moving = true;
        }

        public static void FromWorld(RectTransform rect, Vector3 from, Vector2 target, bool animate = true)
        {
            if (animate && Application.isPlaying && rect.gameObject.activeInHierarchy) rect.position = from;
            MoveTo(rect, target, animate);
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
            float t = Mathf.Clamp01(_elapsed / MoveSeconds);
            _rect.anchoredPosition = Vector2.LerpUnclamped(_from, _target, UiEase.Evaluate(UiEaseKind.OutCubic, t));
            if (t >= 1f) _moving = false;
        }

        private void OnDisable() { _moving = false; }
    }
}
