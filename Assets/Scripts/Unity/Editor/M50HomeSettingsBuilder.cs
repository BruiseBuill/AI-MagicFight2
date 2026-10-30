using System;
using System.Text;
using MagicBrawl.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>M50 · 主菜单设置面板</b>（2026-10-30）：右上角一枚齿轮，点开三行设置 ——
    /// 对局双方 / 经典模式 / 怪物智力。
    ///
    /// <para><b>为什么是一个新的增量构建器，而不是改 P0 重跑</b>：<c>HomeUiBuilder</c> 会
    /// <c>DestroyImmediate</c> 整棵 <c>HomeCanvas</c> 再重建 —— 那会冲掉用户在这棵树上
    /// 做过的任何手改（铁律 13）。这里只在既有的 Canvas 下补一个 <c>HomeSettingsLayer</c>，
    /// 与 P0 互不干涉，可反复重跑（每次先拆自己上次建的那一层）。</para>
    ///
    /// <para><b>⚠ 两处落点都要补</b>：<c>Home.unity</c> 里的 <c>HomeCanvas</c> 是
    /// <b>场景内联对象</b>（不是 PrefabInstance），所以改 <c>HomeCanvas.prefab</c>
    /// 不会同步到场景 —— 两份都得写（铁律 11）。</para>
    /// </summary>
    public static class M50HomeSettingsBuilder
    {
        private const string LayerName = "HomeSettingsLayer";
        private const string ScenePath = "Assets/Scenes/Home.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/HomeCanvas.prefab";
        private const string CanvasName = "HomeCanvas";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        // ── 面板版式（只此一处；面板是本构建器的产物，没必要进 UiLayout）──
        private const float PanelWidth = 900f;
        private const float PanelHeight = 640f;
        private const float RowWidth = 760f;
        private const float RowHeight = 84f;
        private const float LabelWidth = 300f;
        private const float ValueWidth = 340f;
        private const float ValueHeight = 64f;

        [MenuItem("魔法乱斗/M50 · Home 设置（经典模式 / 怪物智力 / 对局双方）", false, 50)]
        private static void MenuEntry()
        {
            // ⚠ 不加 EditorUtility.DisplayDialog：带弹窗的菜单被脚本 / MCP 触发会挂住主线程。
            Debug.Log(RunAll());
        }

        /// <summary>无弹窗入口（MCP 走反射调它）。</summary>
        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[M50] ==== Home 设置 ==== ");

            if (EditorApplication.isPlaying)
            {
                log.AppendLine("✘ 请先退出 Play 再构建（Play 模式下改场景不会落盘）");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            TMP_FontAsset title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            if (body == null || title == null)
            {
                log.AppendLine("✘ 缺 TMP 字体：" + BodyFontPath + " / " + TitleFontPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            Sprite gear = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.OverlaySettingsGearSpritePath);
            Sprite buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);
            if (gear == null) log.AppendLine("⚠ 缺齿轮图：" + UiLayout.OverlaySettingsGearSpritePath);
            if (buttonSprite == null) log.AppendLine("⚠ 缺按钮底图：" + UiLayout.HandPickConfirmSpritePath);

            // ① 场景里那份（内联对象）
            EditorSceneManager.SaveOpenScenes();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject canvas = GameObject.Find(CanvasName);
            if (canvas == null)
            {
                log.AppendLine("✘ 场景里找不到 " + CanvasName + " —— 先跑 `魔法乱斗/P0 · 构建 Home 场景`。");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            ApplyToRoot(canvas, body, title, gear, buttonSprite);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine("  ✔ 场景 " + ScenePath);

            // ② Prefab 那份（两份独立副本，都要补）
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                log.AppendLine("⚠ 找不到 Prefab：" + PrefabPath);
            }
            else
            {
                try
                {
                    ApplyToRoot(root, body, title, gear, buttonSprite);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    log.AppendLine("  ✔ Prefab " + PrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            log.AppendLine("[M50] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ══════════════════════════════════════════════════════
        //  建层
        // ══════════════════════════════════════════════════════

        /// <summary>在 <paramref name="root"/>（Canvas）下重建这一层。可重跑：先拆自己上次建的。</summary>
        private static void ApplyToRoot(GameObject root, TMP_FontAsset body, TMP_FontAsset title,
            Sprite gear, Sprite buttonSprite)
        {
            Transform old = root.transform.Find(LayerName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            GameObject layer = NewUi(LayerName, root.transform);
            Stretch(Rt(layer));

            // ── 右上角齿轮（与战斗里 M44 那颗同一张图、同一组版式常量）──
            GameObject openGo = NewUi("SettingsButton", layer.transform);
            Box(Rt(openGo), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiLayout.OverlaySettingsButtonRight, -UiLayout.OverlaySettingsButtonTop),
                new Vector2(UiLayout.OverlaySettingsButtonSize, UiLayout.OverlaySettingsButtonSize));
            Image openImage = Image(openGo, gear != null ? Color.white : UiTheme.Panel, true);
            if (gear != null)
            {
                openImage.sprite = gear;
                openImage.preserveAspect = true;
            }

            Button openButton = openGo.AddComponent<Button>();
            openButton.targetGraphic = openImage;
            openButton.transition = Selectable.Transition.None;

            // ── 面板整层（默认失活）──────────────────────────
            GameObject panelLayer = NewUi("PanelLayer", layer.transform);
            Stretch(Rt(panelLayer));

            GameObject veilGo = NewUi("Veil", panelLayer.transform);
            Stretch(Rt(veilGo));
            Image veilImage = Image(veilGo, UiTheme.OverlayVeil, true);
            Button veilButton = veilGo.AddComponent<Button>();
            veilButton.targetGraphic = veilImage;
            veilButton.transition = Selectable.Transition.None;

            // 描边（略大一圈的深色底板，衬在面板后面）
            GameObject edgeGo = NewUi("Edge", panelLayer.transform);
            Box(Rt(edgeGo), Mid, Mid, Vector2.zero, new Vector2(PanelWidth + 8f, PanelHeight + 8f));
            Image(edgeGo, UiTheme.OverlayEdge, false);

            GameObject panelGo = NewUi("Panel", panelLayer.transform);
            Box(Rt(panelGo), Mid, Mid, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            Image(panelGo, UiTheme.OverlayPanel, false);

            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, 258f), new Vector2(700f, 70f));
            AddText(titleGo, title, UiLayout.FontSizeOverlayPanelTitle, UiTheme.ShopBagTitleText,
                TextAlignmentOptions.Center, "设置");

            GameObject subGo = NewUi("Subtitle", panelGo.transform);
            Box(Rt(subGo), Mid, Mid, new Vector2(0f, 206f), new Vector2(760f, 40f));
            AddText(subGo, body, 24f, UiTheme.TextSecondary,
                TextAlignmentOptions.Center, "改动在下一局生效");

            // ── 三行 ─────────────────────────────────────────
            Button lineupButton = BuildRow(panelGo.transform, "Row_Lineup", "对局双方", 130f,
                body, buttonSprite, out TMP_Text lineupValue);
            Button classicButton = BuildRow(panelGo.transform, "Row_Classic", "经典模式", 30f,
                body, buttonSprite, out TMP_Text classicValue);
            Button difficultyButton = BuildRow(panelGo.transform, "Row_Difficulty", "怪物智力", -70f,
                body, buttonSprite, out TMP_Text difficultyValue);

            // ── 返回 ─────────────────────────────────────────
            GameObject closeGo = NewUi("CloseButton", panelGo.transform);
            Box(Rt(closeGo), Mid, Mid, new Vector2(0f, -238f), new Vector2(280f, 76f));
            Image closeImage = Image(closeGo, buttonSprite != null ? Color.white : UiTheme.Panel, true);
            if (buttonSprite != null) closeImage.sprite = buttonSprite;
            Button closeButton = closeGo.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;
            closeButton.transition = Selectable.Transition.ColorTint;

            GameObject closeLabel = NewUi("Label", closeGo.transform);
            Stretch(Rt(closeLabel));
            AddText(closeLabel, title, UiLayout.FontSizeOverlayPanelButton, UiTheme.ShopLeaveText,
                TextAlignmentOptions.Center, "返回");

            // ── 接线 ─────────────────────────────────────────
            var view = layer.AddComponent<HomeSettingsView>();
            var data = new SerializedObject(view);
            SetRef(data, "_openButton", openButton);
            SetRef(data, "_layer", panelLayer);
            SetRef(data, "_veilButton", veilButton);
            SetRef(data, "_closeButton", closeButton);
            SetRef(data, "_lineupButton", lineupButton);
            SetRef(data, "_lineupValue", lineupValue);
            SetRef(data, "_classicButton", classicButton);
            SetRef(data, "_classicValue", classicValue);
            SetRef(data, "_difficultyButton", difficultyButton);
            SetRef(data, "_difficultyValue", difficultyValue);
            data.ApplyModifiedPropertiesWithoutUndo();

            panelLayer.SetActive(false);
        }

        /// <summary>建一行：底板 + 左侧标题 + 右侧「点一下换一档」的值按钮。</summary>
        private static Button BuildRow(Transform parent, string name, string label, float centerY,
            TMP_FontAsset font, Sprite buttonSprite, out TMP_Text value)
        {
            GameObject row = NewUi(name, parent);
            Box(Rt(row), Mid, Mid, new Vector2(0f, centerY), new Vector2(RowWidth, RowHeight));
            Image(row, UiTheme.Panel, false);

            GameObject labelGo = NewUi("Label", row.transform);
            Box(Rt(labelGo), Mid, Mid, new Vector2(-RowWidth * 0.5f + 24f + LabelWidth * 0.5f, 0f),
                new Vector2(LabelWidth, RowHeight - 8f));
            AddText(labelGo, font, 30f, UiTheme.TextPrimary, TextAlignmentOptions.Left, label);

            GameObject valueGo = NewUi("Value", row.transform);
            Box(Rt(valueGo), Mid, Mid,
                new Vector2(RowWidth * 0.5f - 20f - ValueWidth * 0.5f, 0f),
                new Vector2(ValueWidth, ValueHeight));
            Image valueImage = Image(valueGo, buttonSprite != null ? Color.white : UiTheme.PanelEdge, true);
            if (buttonSprite != null) valueImage.sprite = buttonSprite;

            Button button = valueGo.AddComponent<Button>();
            button.targetGraphic = valueImage;
            button.transition = Selectable.Transition.ColorTint;

            GameObject textGo = NewUi("Text", valueGo.transform);
            Stretch(Rt(textGo));
            value = AddText(textGo, font, 26f, UiTheme.ShopLeaveText, TextAlignmentOptions.Center, string.Empty);

            return button;
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

        private static GameObject NewUi(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            return node;
        }

        private static RectTransform Rt(GameObject node) { return (RectTransform)node.transform; }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Image Image(GameObject node, Color color, bool raycast)
        {
            var image = node.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TMP_Text AddText(GameObject node, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align, string text)
        {
            TextMeshProUGUI label = node.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.text = text ?? string.Empty;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            return label;
        }

        private static void SetRef(SerializedObject data, string field, UnityEngine.Object value)
        {
            SerializedProperty property = data.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException("HomeSettingsView 上没有 " + field + "（脚本未编译？）");
            }

            property.objectReferenceValue = value;
        }
    }
}
