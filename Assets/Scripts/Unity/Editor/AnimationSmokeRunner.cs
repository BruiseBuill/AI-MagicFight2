using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>Explicit MCP-invoked Play Mode regression; exits Play Mode even after a failed assertion.</summary>
    [InitializeOnLoad]
    public static class AnimationSmokeRunner
    {
        private const string Pending = "MagicBrawl.AnimationSmoke.Pending";
        private const string Report = "Artifacts/animation/2026-10-30-smoke.txt";
        private static IEnumerator _run;
        private static GameObject _fixture;
        private static readonly List<string> Checks = new List<string>();
        private static double _deadline;

        static AnimationSmokeRunner()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
            if (SessionState.GetBool(Pending, false)) EditorApplication.update += Tick;
        }

        public static string Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Refused: already in Play Mode.";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                return "Refused: current scene has unsaved changes.";
            SessionState.SetBool(Pending, true);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
            return "Animation smoke scheduled; automatically exits Play Mode and writes " + Report;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                Checks.Clear();
                _deadline = EditorApplication.timeSinceStartup + 90;
                _run = Run();
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
        }

        private static void Tick()
        {
            if (_run == null) return;
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode was interrupted.");
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Smoke timeout.");
                if (!_run.MoveNext()) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            if (error != null) Checks.Add("FAIL " + error);
            else Checks.Add("PASS all animation smoke checks");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllLines(Report, Checks);
            (_run as IDisposable)?.Dispose();
            _run = null;
            Time.timeScale = 1f;
            if (_fixture != null) UnityEngine.Object.DestroyImmediate(_fixture);
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }

        private static void Check(bool passed, string label)
        {
            if (!passed) throw new Exception(label);
            Checks.Add("PASS " + label);
        }

        private static void Set(object owner, string name, object value)
        {
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        }

        private static T Get<T>(object owner, string name)
        {
            return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            return rect;
        }

        private static CardSnapshot Card(int uid, int remaining, int seat = 0)
        {
            var card = CardSnapshot.FromDef(CardLibrary.All[0], 0, remaining);
            card.Uid = uid;
            card.OwnerSeat = seat;
            card.RemainingCooldown = remaining;
            card.InCoolingZone = remaining > 0;
            return card;
        }

        private static IEnumerator Run()
        {
            double until = EditorApplication.timeSinceStartup + 1.5;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            var driver = UnityEngine.Object.FindObjectOfType<BattleDriver>();
            if (driver != null) driver.SetPaused(true);
            Directory.CreateDirectory("Captures/animation");
            ScreenCapture.CaptureScreenshot("Captures/animation/battle.png");
            _fixture = new GameObject("AnimationSmoke", typeof(RectTransform));
            _fixture.hideFlags = HideFlags.DontSave;
            var root = (RectTransform)_fixture.transform;

            Check(Mathf.Abs(UiEase.Evaluate(UiEaseKind.OutCubic, 0.5f) - 0.875f) < 0.0001f, "OutCubic is nonlinear");
            Check(UiEase.Evaluate(UiEaseKind.OutCubic, 0f) == 0f && UiEase.Evaluate(UiEaseKind.OutCubic, 1f) == 1f, "easing endpoints");
            var panel = Rect("Panel", root, Vector2.zero).gameObject;
            panel.transform.localScale = new Vector3(1.2f, 0.8f, 1f);
            panel.SetActive(false);
            Time.timeScale = 0f;
            UiPanelAnimator.SetVisible(panel, true);
            var group = panel.GetComponent<CanvasGroup>();
            Check(group.alpha == 0f && UiPanelAnimator.IsShown(panel), "panel starts hidden but logically open");
            until = EditorApplication.timeSinceStartup + 0.08;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            float openingAlpha = group.alpha;
            UiPanelAnimator.SetVisible(panel, false);
            Check(!group.interactable && !group.blocksRaycasts && !UiPanelAnimator.IsShown(panel), "closing immediately rejects stale input");
            Check(Mathf.Abs(group.alpha - openingAlpha) < 0.001f, "close preserves intermediate alpha");
            UiPanelAnimator.SetVisible(panel, true);
            Check(Mathf.Abs(group.alpha - openingAlpha) < 0.001f, "reverse preserves intermediate alpha");
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) { UiPanelAnimator.SetVisible(panel, true); yield return null; }
            Check(group.alpha == 1f && panel.activeSelf, "repeated show completes while paused");
            Check(panel.transform.localScale == new Vector3(1.2f, 0.8f, 1f), "panel preserves authored scale");
            int completed = 0;
            UiPanelAnimator.Close(panel, () => completed++);
            until = EditorApplication.timeSinceStartup + 0.3;
            while (EditorApplication.timeSinceStartup < until) { UiPanelAnimator.SetVisible(panel, false); yield return null; }
            Check(!panel.activeSelf && completed == 1, "repeated hide completes once and deactivates");
            UiPanelAnimator.SetVisible(panel, true);
            panel.SetActive(false);
            UiPanelAnimator.SetVisible(panel, true);
            until = EditorApplication.timeSinceStartup + 0.3;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(group.alpha == 1f, "pooled panel recovers after interruption");
            _fixture.SetActive(false);
            _fixture.SetActive(true);
            Check(group.alpha == 1f && UiPanelAnimator.IsShown(panel), "parent disable/enable preserves visible child");
            UiPanelAnimator.SetVisible(panel, false);
            _fixture.SetActive(false);
            _fixture.SetActive(true);
            Check(!panel.activeSelf, "parent disable completes child close without invisible blocker");

            var loseRoot = Rect("Lose", root, Vector2.zero).gameObject;
            loseRoot.SetActive(false);
            var lose = loseRoot.AddComponent<ResultLoseView>();
            lose.Configure(loseRoot, null, null, null, null);
            int retries = 0;
            lose.Retried += () => retries++;
            lose.Show("Test", "");
            until = EditorApplication.timeSinceStartup + 0.3;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            var retry = typeof(ResultLoseView).GetMethod("OnRetryClicked", BindingFlags.Instance | BindingFlags.NonPublic);
            retry.Invoke(lose, null);
            retry.Invoke(lose, null);
            Check(retries == 0 && loseRoot.activeSelf && !lose.IsOpen, "result waits for exit and rejects double click");
            until = EditorApplication.timeSinceStartup + 0.3;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(retries == 1 && !loseRoot.activeSelf, "result callback fires once after exit");

            var a = Rect("ParentA", root, Vector2.zero);
            var b = Rect("ParentB", root, new Vector2(200f, 100f));
            var moving = Rect("Moving", a, new Vector2(40f, 10f));
            Vector3 original = moving.position;
            moving.SetParent(b, false);
            UiPositionMotion.FromWorld(moving, original, Vector2.zero);
            Check(Vector3.Distance(moving.position, original) < 0.001f, "reparent preserves world origin");
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) { UiPositionMotion.MoveTo(moving, Vector2.zero); yield return null; }
            Check(moving.anchoredPosition.sqrMagnitude < 0.001f, "repeated motion target settles while paused");
            UiPositionMotion.MoveTo(moving, Vector2.one * 100f);
            UiPositionMotion.MoveTo(moving, Vector2.one * -40f);
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(Vector2.Distance(moving.anchoredPosition, Vector2.one * -40f) < 0.01f, "movement retargets latest destination");

            var prefab = AssetDatabase.LoadAssetAtPath<CardView>("Assets/Prefabs/Ui/CardView_Hand.prefab");
            Check(prefab != null, "real card prefab available");
            var handRoot = Rect("Hand", root, new Vector2(0f, -250f));
            var hand = handRoot.gameObject.AddComponent<HandView>();
            Set(hand, "_row", handRoot);
            Set(hand, "_cardPrefab", prefab);
            var cooling = Rect("Cooling", root, Vector2.zero).gameObject.AddComponent<CooldownView>();
            var rows = new RectTransform[4];
            for (int i = 0; i < rows.Length; i++) rows[i] = Rect("Row" + i, root, new Vector2(-300f, 200f - i * 100f));
            Set(cooling, "_playerRows", rows);
            Set(cooling, "_enemyRows", rows);
            Set(cooling, "_cardPrefab", prefab);
            var card = Card(501, 4);
            cooling.Bind(0, new[] { card });
            CardView coolingCard = cooling.FindCard(0, 501);
            original = coolingCard.transform.position;
            card.RemainingCooldown = 2;
            cooling.Bind(0, new[] { card });
            Check(Vector3.Distance(original, coolingCard.transform.position) < 0.01f, "cooldown row change does not teleport");
            until = EditorApplication.timeSinceStartup + 0.45;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(Vector3.Distance(original, coolingCard.transform.position) > 100f, "cooldown card reaches new row");

            var transit = Rect("Transit", root, Vector2.zero).gameObject.AddComponent<CardTransitView>();
            Set(transit, "_layer", (RectTransform)transit.transform);
            transit.Configure(hand, cooling);
            var empty = new CardSnapshot[0];
            var inHand = Card(601, 0);
            hand.Bind(new[] { inHand }, null);
            hand.SnapLayout();
            cooling.BindBoth(empty, empty);
            transit.CaptureBeforeBind(new[] { inHand }, empty, empty);
            hand.Bind(empty, null);
            var inCooling = Card(601, 3);
            cooling.BindBoth(new[] { inCooling }, empty);
            transit.AnimateAfterBind(empty, new[] { inCooling }, empty);
            Check(cooling.FindCard(0, 601).PresentationHidden, "direct hand-to-cooling launches ghost and hides destination");
            Check(((IList)Get<object>(transit, "_flights")).Count == 1, "exactly one flight per UID");
            until = EditorApplication.timeSinceStartup + 0.85;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(!cooling.FindCard(0, 601).PresentationHidden, "flight reveals destination after landing");
            transit.ResetPresentation();

            hand.ConfigureSeats(1, 0);
            cooling.ConfigureSeats(1, 0);
            transit.ConfigureSeats(1, 0);
            var hotCooling = Card(701, 1, 1);
            hand.Bind(empty, null);
            cooling.BindBoth(new[] { hotCooling }, empty);
            transit.CaptureBeforeBind(empty, new[] { hotCooling }, empty);
            var hotHand = Card(701, 0, 1);
            hand.Bind(new[] { hotHand }, null);
            hand.SnapLayout();
            cooling.BindBoth(empty, empty);
            transit.AnimateAfterBind(new[] { hotHand }, empty, empty);
            Check(hand.FindCard(701).PresentationHidden, "hotseat local seat 1 returns from correct origin array");
            transit.ResetPresentation();
            Check(!hand.FindCard(701).PresentationHidden, "reset restores hidden destination");

            var deck = Rect("Deck", root, Vector2.zero).gameObject.AddComponent<DeckSelectView>();
            var slots = new CardView[3];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = UnityEngine.Object.Instantiate(prefab, root);
                ((RectTransform)slots[i].transform).anchoredPosition = new Vector2(i * 180f, 0f);
                slots[i].gameObject.SetActive(false);
            }
            Set(deck, "_slotViews", slots);
            deck.SetTab(0, 3, 0);
            deck.ShowDeck(new[] { CardLibrary.All[0], CardLibrary.All[1], CardLibrary.All[2] });
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            original = slots[1].transform.position;
            deck.SetTab(0, 2, 0);
            deck.ShowDeck(new[] { CardLibrary.All[1], CardLibrary.All[2] });
            Check(Vector3.Distance(slots[0].transform.position, original) < 0.01f, "deck removal preserves remaining card identity/origin");
            deck.SetTab(1, 2, 1);
            deck.ShowDeck(new[] { CardLibrary.All[3] });
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(slots[0].gameObject.activeSelf && !slots[1].gameObject.activeSelf && !slots[2].gameObject.activeSelf, "rapid tab switch settles visibility");
            Check(slots[0].GetComponent<CanvasGroup>().alpha == 1f, "tab reveal has a single alpha owner");

            var aura = Rect("Aura", b, new Vector2(70f, 60f)).gameObject.AddComponent<AuraIconView>();
            aura.HomeGroup = a;
            original = aura.transform.position;
            aura.ReturnHome();
            Check(Vector3.Distance(aura.transform.position, original) < 0.01f, "aura cancel preserves drag origin");
            UiSizeMotion.Resize(moving, new Vector2(340f, 160f), true);
            var image = moving.gameObject.AddComponent<Image>();
            image.color = Color.white;
            UiColorMotion.Set(image, Color.red);
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(((RectTransform)aura.transform).anchoredPosition.sqrMagnitude < 0.001f, "aura reaches home while paused");
            Check(moving.sizeDelta == new Vector2(340f, 160f), "panel resize reaches target while paused");
            Check(image.color == Color.red, "HP/highlight color reaches target while paused");
            var scale = moving.gameObject.AddComponent<TweenScale>();
            scale.Setup(Vector3.one, Vector3.one * 1.1f, 1f);
            scale.Play();
            moving.gameObject.SetActive(false);
            Check(!scale.IsPlaying, "disabled tween clears playing state");

            var retired = Card(801, 0, 1);
            hand.Bind(new[] { retired }, null);
            hand.SnapLayout();
            transit.PlayDeparture(retired, true);
            Check(hand.FindCard(801).PresentationHidden, "removed card transfers to exit ghost");
            hand.Bind(empty, null);
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(((IList)Get<object>(transit, "_flights")).Count == 0, "removed card ghost is recycled");

            // Exercise the actual scene hierarchy and capture a start/middle/end sequence.
            UnityEngine.Object.DestroyImmediate(_fixture);
            _fixture = null;
            Time.timeScale = 1f;
            var actualHand = UnityEngine.Object.FindObjectOfType<HandView>();
            var actualCooling = UnityEngine.Object.FindObjectOfType<CooldownView>();
            var actualUi = UnityEngine.Object.FindObjectOfType<BattleUi>();
            if (actualUi != null) actualUi.StopAllCoroutines();
            actualHand.Bind(new[] { Card(901, 0), Card(902, 0), Card(903, 0) }, null);
            actualCooling.BindBoth(new[] { Card(904, 4), Card(905, 3) }, new[] { Card(906, 2, 1) });
            until = EditorApplication.timeSinceStartup + 0.5;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            ScreenCapture.CaptureScreenshot("Captures/animation/cooling-start.png");
            yield return null;
            yield return null;
            actualCooling.BindBoth(new[] { Card(904, 1), Card(905, 2) }, new[] { Card(906, 3, 1) });
            until = EditorApplication.timeSinceStartup + 0.08;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            ScreenCapture.CaptureScreenshot("Captures/animation/cooling-middle.png");
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            ScreenCapture.CaptureScreenshot("Captures/animation/cooling-end.png");
            yield return null;
            yield return null;
            var overlay = UnityEngine.Object.FindObjectOfType<GlobalOverlayView>();
            if (overlay != null)
            {
                overlay.OpenSettings();
                until = EditorApplication.timeSinceStartup + 0.3;
                while (EditorApplication.timeSinceStartup < until) yield return null;
                Check(overlay.IsSettingsOpen, "real settings opens");
                ScreenCapture.CaptureScreenshot("Captures/animation/settings.png");
                yield return null;
                yield return null;
                overlay.CloseSettings();
                overlay.OpenSettings();
                until = EditorApplication.timeSinceStartup + 0.3;
                while (EditorApplication.timeSinceStartup < until) yield return null;
                Check(overlay.IsSettingsOpen, "real settings reopens during close");
                overlay.CloseSettings();
            }
            Check(SceneRoutes.LoadHome(), "scene navigation accepts first request");
            Check(!SceneRoutes.LoadDeck(), "scene navigation rejects concurrent request");
            until = EditorApplication.timeSinceStartup + 12;
            while (UnityEngine.Object.FindObjectOfType<SceneTransition>() != null && EditorApplication.timeSinceStartup < until) yield return null;
            Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneRoutes.HomeScene, "Home transition completes");
            Check(UnityEngine.Object.FindObjectOfType<SceneTransition>() == null, "transition veil is destroyed");
            ScreenCapture.CaptureScreenshot("Captures/animation/home.png");
            yield return null;
            yield return null;
            Check(SceneRoutes.LoadDeck(), "Deck transition accepted after Home");
            until = EditorApplication.timeSinceStartup + 12;
            while (UnityEngine.Object.FindObjectOfType<SceneTransition>() != null && EditorApplication.timeSinceStartup < until) yield return null;
            var actualDeck = UnityEngine.Object.FindObjectOfType<DeckSelectView>();
            Check(actualDeck != null, "Deck scene loaded");
            actualDeck.SetTab(0, 3, 0);
            actualDeck.ShowDeck(new[] { CardLibrary.All[0], CardLibrary.All[1], CardLibrary.All[2] });
            until = EditorApplication.timeSinceStartup + 0.4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            ScreenCapture.CaptureScreenshot("Captures/animation/deck.png");
            yield return null;
            yield return null;
            Check(SceneRoutes.LoadBattle(), "Battle transition accepted after Deck");
            until = EditorApplication.timeSinceStartup + 12;
            while (UnityEngine.Object.FindObjectOfType<SceneTransition>() != null && EditorApplication.timeSinceStartup < until) yield return null;
            Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneRoutes.BattleScene, "Battle transition completes");
        }
    }
}
