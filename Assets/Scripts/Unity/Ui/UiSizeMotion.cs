using UnityEngine;

namespace MagicBrawl.App
{
    [DisallowMultipleComponent]
    public sealed class UiSizeMotion : MonoBehaviour
    {
        private RectTransform _rect;
        private Vector2 _from;
        private Vector2 _target;
        private float _elapsed;
        private bool _moving;

        public static void Resize(RectTransform rect, Vector2 target, bool animate)
        {
            if (rect == null) return;
            var motion = rect.GetComponent<UiSizeMotion>();
            if (!animate || !Application.isPlaying || !rect.gameObject.activeInHierarchy)
            {
                if (motion != null) motion._moving = false;
                rect.sizeDelta = target;
                return;
            }
            if (motion == null) motion = rect.gameObject.AddComponent<UiSizeMotion>();
            if (motion._moving && motion._target == target) return;
            motion._rect = rect;
            motion._from = rect.sizeDelta;
            motion._target = target;
            motion._elapsed = 0f;
            motion._moving = true;
        }

        private void Update()
        {
            if (!_moving) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / UiPositionMotion.MoveSeconds);
            _rect.sizeDelta = Vector2.Lerp(_from, _target, UiEase.Evaluate(UiEaseKind.OutCubic, t));
            if (t >= 1f) _moving = false;
        }

        private void OnDisable() { _moving = false; }
    }
}
