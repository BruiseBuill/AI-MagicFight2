using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>One unscaled color transition per graphic; repeated state binds do not restart it.</summary>
    [DisallowMultipleComponent]
    public sealed class UiColorMotion : MonoBehaviour
    {
        private Graphic _graphic;
        private Color _from;
        private Color _target;
        private float _elapsed;
        private bool _moving;

        public static void Set(Graphic graphic, Color color, bool animate = true)
        {
            if (graphic == null) return;
            var motion = graphic.GetComponent<UiColorMotion>();
            if (!Application.isPlaying || !graphic.isActiveAndEnabled || !animate)
            {
                if (motion != null) motion._moving = false;
                graphic.color = color;
                return;
            }
            if (motion == null) motion = graphic.gameObject.AddComponent<UiColorMotion>();
            if ((motion._moving && motion._target == color) || (!motion._moving && graphic.color == color)) return;
            motion._graphic = graphic;
            motion._from = graphic.color;
            motion._target = color;
            motion._elapsed = 0f;
            motion._moving = true;
        }

        private void Update()
        {
            if (!_moving) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / 0.18f);
            _graphic.color = Color.Lerp(_from, _target, UiEase.Evaluate(UiEaseKind.OutCubic, t));
            if (t >= 1f) _moving = false;
        }

        private void OnDisable()
        {
            if (_moving && _graphic != null) _graphic.color = _target;
            _moving = false;
        }
    }
}
