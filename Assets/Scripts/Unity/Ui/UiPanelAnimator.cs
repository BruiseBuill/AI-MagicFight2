using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>Reversible, unscaled visibility motion. Does not change sibling order or layout.</summary>
    [DisallowMultipleComponent]
    public sealed class UiPanelAnimator : MonoBehaviour
    {
        public const float EnterSeconds = 0.20f;
        public const float ExitSeconds = 0.14f;
        private CanvasGroup _group;
        private bool _initialized;
        private bool _visible;
        private bool _moving;
        private float _from;
        private float _elapsed;
        private float _duration;
        private System.Action _onHidden;

        public static bool IsShown(GameObject target)
        {
            if (target == null || !target.activeSelf) return false;
            var motion = target.GetComponent<UiPanelAnimator>();
            return motion == null || !motion._initialized || motion._visible;
        }

        public static void SetVisible(GameObject target, bool visible, bool interactive = true, bool immediate = false)
        {
            if (target == null) return;
            if (!Application.isPlaying || immediate || (target.transform.parent != null && !target.transform.parent.gameObject.activeInHierarchy))
            {
                var existing = target.GetComponent<UiPanelAnimator>();
                if (existing != null) existing.Snap(visible, interactive);
                else target.SetActive(visible);
                return;
            }
            var motion = target.GetComponent<UiPanelAnimator>();
            if (motion == null) motion = target.AddComponent<UiPanelAnimator>();
            motion.Change(visible, interactive);
        }

        public static void RevealContent(GameObject target, bool interactive = false)
        {
            SetVisible(target, true, interactive);
            if (target == null || !Application.isPlaying || !target.activeInHierarchy) return;
            var motion = target.GetComponent<UiPanelAnimator>();
            motion._from = motion._group.alpha = 0f;
            motion._elapsed = 0f;
            motion._duration = EnterSeconds;
            motion._moving = true;
        }

        public static void Close(GameObject target, System.Action completed)
        {
            SetVisible(target, false);
            var motion = target == null ? null : target.GetComponent<UiPanelAnimator>();
            if (motion != null && motion._moving) motion._onHidden = completed;
            else if (completed != null) completed();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _visible = gameObject.activeSelf;
            if (!_visible) _group.alpha = 0f;
            _initialized = true;
        }

        private void Change(bool visible, bool interactive)
        {
            Initialize();
            _group.interactable = visible && interactive;
            _group.blocksRaycasts = visible && interactive;
            if (_visible == visible && (_moving || gameObject.activeSelf == visible)) return;
            _onHidden = null;
            _visible = visible;
            _from = _group.alpha;
            _elapsed = 0f;
            _duration = visible ? EnterSeconds : ExitSeconds;
            // A fading-out view must never submit a stale decision.
            _moving = true;
            if (visible) gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!_moving) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            _group.alpha = Mathf.Lerp(_from, _visible ? 1f : 0f, UiEase.Evaluate(UiEaseKind.OutCubic, t));
            if (t < 1f) return;
            _moving = false;
            if (!_visible)
            {
                var completed = _onHidden;
                _onHidden = null;
                gameObject.SetActive(false);
                if (completed != null) completed();
            }
        }

        private void Snap(bool visible, bool interactive)
        {
            Initialize();
            _moving = false;
            _onHidden = null;
            _visible = visible;
            _group.alpha = visible ? 1f : 0f;
            _group.interactable = _group.blocksRaycasts = visible && interactive;
            gameObject.SetActive(visible);
        }

        private void OnDisable()
        {
            _moving = false;
            _onHidden = null;
            // Parent deactivation must not leave an otherwise visible child permanently transparent.
            if (!gameObject.activeSelf) _visible = false;
            if (_group != null) _group.alpha = _visible ? 1f : 0f;
            if (!_visible && gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
