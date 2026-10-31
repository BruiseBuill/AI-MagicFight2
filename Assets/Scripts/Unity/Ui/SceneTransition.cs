using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>One transition owns scene loading and blocks repeated navigation until revealed.</summary>
    public sealed class SceneTransition : MonoBehaviour
    {
        private static SceneTransition _instance;
        private CanvasGroup _group;
        private bool _loading;

        public static bool Load(string sceneName)
        {
            if (_instance != null && _instance._loading) return false;
            if (_instance == null)
            {
                var root = new GameObject("SceneTransition", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(SceneTransition));
                DontDestroyOnLoad(root);
                _instance = root.GetComponent<SceneTransition>();
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32760;
                var veil = new GameObject("Veil", typeof(RectTransform), typeof(Image));
                veil.transform.SetParent(root.transform, false);
                var rect = (RectTransform)veil.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                veil.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.03f, 1f);
                _instance._group = root.GetComponent<CanvasGroup>();
                _instance._group.alpha = 0f;
            }
            _instance._loading = true;
            _instance.StartCoroutine(_instance.Run(sceneName));
            return true;
        }

        private IEnumerator Run(string sceneName)
        {
            _group.blocksRaycasts = true;
            yield return Fade(0f, 1f);
            AsyncOperation load;
            try { load = SceneManager.LoadSceneAsync(sceneName); }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                load = null;
            }
            if (load != null) yield return load;
            yield return null;
            yield return Fade(1f, 0f);
            _group.blocksRaycasts = false;
            Destroy(gameObject);
        }

        private IEnumerator Fade(float from, float to)
        {
            float elapsed = 0f;
            while (elapsed < 0.18f)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, to, UiEase.Evaluate(UiEaseKind.InOutCubic, elapsed / 0.18f));
                yield return null;
            }
            _group.alpha = to;
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
