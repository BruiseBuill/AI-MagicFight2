using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>D1 · 自定义战斗的牌组编成场景</b>的构建器（2026-10-28 · 大改新增）。
    ///
    /// <para>菜单：<c>魔法乱斗/D1 · 构建 DeckSelect 场景</c>（<b>无确认弹窗</b>，可被脚本 / MCP 直调）。</para>
    ///
    /// <para><b>它建什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item><c>DeckSelectCanvas</c>：整屏底 + 标题 + 副标题 + 两枚页签 +
    /// 左侧卡表（ScrollRect + 5 列网格）+ 右侧 8 格 + 底部「开战 / 返回」+ 提示行；</item>
    /// <item>把 <see cref="DeckSelectView"/> 的序列化字段一次性接好（含 8 个卡位与卡表模板）；</item>
    /// <item>场景根挂 <see cref="DeckSelectEntry"/>（<b>不进 Prefab</b>）；</item>
    /// <item>存 <c>Assets/Prefabs/Ui/DeckSelectCanvas.prefab</c> 与 <c>Assets/Scenes/DeckSelect.unity</c>；
    /// 登记进 Build Settings（顺序看 <see cref="SceneRoutes.BuildOrder"/>）。</item>
    /// </list>
    ///
    /// <para><b>⚠ 卡面只有一份</b>：卡表的模板与 8 个卡位全部来自
    /// <c>Assets/Prefabs/Ui/CardView_Hand.prefab</c> —— 与手牌 / 冷却区 / 背包同一张。
    /// 别在这里另建卡面结构，那会让「改一次卡面要改五处」重新成立。</para>
    /// </summary>
    public static class DeckSelectUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/DeckSelect.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/DeckSelectCanvas.prefab";
        private const string CanvasName = "DeckSelectCanvas";
        private const string HandCardPath = "Assets/Prefabs/Ui/CardView_Hand.prefab";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";
        private const string ButtonSpritePath = "Assets/Art/Ui/HandPick_Confirm.png";
        private const string PanelSpritePath = "Assets/Art/Ui/Peek_Panel.png";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        // ── 版式 ─────────────────────────────────────────────────
        private const float CatalogCenterX = -470f;
        private const float CatalogCenterY = -20f;
        private const float CatalogWidth = 940f;
        private const float CatalogHeight = 760f;

        private const float DeckCenterX = 500f;
        private const float DeckCenterY = -20f;
        private const float DeckWidth = 840f;
        private const float DeckHeight = 760f;

        private const float SlotWidth = 190f;
        private const float SlotHeight = 252f;
        private const float SlotGapX = 12f;
        private const float SlotGapY = 16f;

        [MenuItem("魔法乱斗/D1 · 构建 DeckSelect 场景", false, 46)]
        private static void MenuEntry()
        {
            Debug.Log(RunAll());
        }

        /// <summary>无弹窗核心（MCP 走反射调这个）。</summary>
        public static string RunAll()
        {
            // ⚠ 2026-10-28：统一兜一层 —— 菜单里抛异常只会留一句 NullReferenceException，
            //   而真正的位置（哪个节点、哪一行）只有完整堆栈里才有。
            try
            {
                return RunAllInternal();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[D1] 构建失败：" + ex);
                return "[D1] 构建失败：" + ex;
            }
        }

        private static string RunAllInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("[D1] ==== 开始 ====");

            if (EditorApplication.isPlaying)
            {
                log.AppendLine("✘ 请先退出 Play 再构建（Play 模式下改场景不会落盘）");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            TMP_FontAsset title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            CardView handPrefab = AssetDatabase.LoadAssetAtPath<CardView>(HandCardPath);
            Sprite buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonSpritePath);
            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);

            if (body == null || title == null || handPrefab == null)
            {
                log.AppendLine("✘ 缺字体或卡面 Prefab：" + BodyFontPath + " / " + TitleFontPath + " / " + HandCardPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            // 1) 场景
            // ⚠ 不用 SaveOpenScenes()：当前要是「Untitled 且脏」的场景（上一次中途失败留下的），
            //   它会弹一个「Save Scene?」模态框，而模态框在脚本/MCP 调用下没人点 ——
            //   表现是构建器卡死不返回（2026-10-28 踩过两次）。这里只保存**有路径**的场景。
            Scene active = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path) && active.isDirty)
            {
                EditorSceneManager.SaveScene(active);
            }

            bool created = !System.IO.File.Exists(AbsolutePath(ScenePath));
            Scene scene = created
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            log.AppendLine((created ? "  新建 " : "  打开 ") + ScenePath);

            // 2) 幂等：拆旧 Canvas
            GameObject old = GameObject.Find(CanvasName);
            if (old != null)
            {
                Object.DestroyImmediate(old);
                log.AppendLine("  拆掉旧 " + CanvasName);
            }

            // 3) 建
            GameObject canvasGo = BuildCanvas(body, title, buttonSprite, panelSprite, log);
            EnsureEventSystem();
            EnsureCamera(log);

            // 4) 入口 + 存 Prefab / 场景
            GameObject entry = BuildEntry(canvasGo, log);
            PrefabUtility.SaveAsPrefabAsset(canvasGo, PrefabPath);
            log.AppendLine("  已存 " + PrefabPath);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            log.AppendLine("  已存 " + ScenePath);

            EnsureBuildSettings(log);

            log.AppendLine("[D1] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ══════════════════════════════════════════════════════
        //  Canvas
        // ══════════════════════════════════════════════════════

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title,
            Sprite buttonSprite, Sprite panelSprite, StringBuilder log)
        {
            var canvasGo = new GameObject(CanvasName,
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiLayout.ReferenceWidth, UiLayout.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // ① 整屏底
            GameObject bgGo = NewUi("Bg", canvasGo.transform);
            Stretch(Rect(bgGo));
            var bg = bgGo.AddComponent<Image>();
            bg.color = UiTheme.Backdrop;
            bg.raycastTarget = false;

            // ② 标题 / 副标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rect(titleGo), Mid, Mid, new Vector2(0f, 468f), new Vector2(1500f, 96f));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeHomeTitle * 0.72f,
                UiTheme.TextPrimary, TextAlignmentOptions.Center, "自定义战斗");

            GameObject subGo = NewUi("Subtitle", canvasGo.transform);
            Box(Rect(subGo), Mid, Mid, new Vector2(0f, 406f), new Vector2(1500f, 52f));
            TMP_Text subtitle = AddText(subGo, body, 26f, UiTheme.TextSecondary,
                TextAlignmentOptions.Center, "点卡表里的牌加入 / 移除　·　两边各 8 张才能开战");

            // ③ 两枚页签
            Button playerTab;
            TMP_Text playerLabel;
            GameObject playerGlow;
            BuildTab(canvasGo.transform, "PlayerTab", "你的牌组", -320f, body, buttonSprite,
                out playerTab, out playerLabel, out playerGlow);

            Button enemyTab;
            TMP_Text enemyLabel;
            GameObject enemyGlow;
            BuildTab(canvasGo.transform, "EnemyTab", "对手的牌组", 320f, body, buttonSprite,
                out enemyTab, out enemyLabel, out enemyGlow);

            // ④ 左侧卡表
            GameObject catalogGo = NewUi("CatalogPanel", canvasGo.transform);
            Box(Rect(catalogGo), Mid, Mid, new Vector2(CatalogCenterX, CatalogCenterY),
                new Vector2(CatalogWidth, CatalogHeight));
            var catalogBg = catalogGo.AddComponent<Image>();
            catalogBg.color = UiTheme.Panel;
            if (panelSprite != null)
            {
                catalogBg.sprite = panelSprite;
                catalogBg.type = Image.Type.Sliced;
            }

            var scroll = catalogGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            GameObject viewport = NewUi("Viewport", catalogGo.transform);
            Stretch(Rect(viewport));
            Rect(viewport).offsetMax = new Vector2(-18f, -18f);
            Rect(viewport).offsetMin = new Vector2(18f, 18f);
            var vpImage = viewport.AddComponent<Image>();
            vpImage.color = Color.clear;
            vpImage.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            GameObject content = NewUi("Content", viewport.transform);
            RectTransform contentRect = Rect(content);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(184f, 258f);
            grid.spacing = new Vector2(8f, 10f);
            grid.padding = new RectOffset(10, 10, 12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;

            // ⚠ 内容高度写死（10 行 × 268 + padding 24 = 2704），不用 ContentSizeFitter ——
            //   ScrollRect + RectMask2D + Fitter + Grid 这套在编辑模式下会让 UGUI 反复重排，
            //   构建期间表现成「点了菜单就不回来」。卡表是 47 张固定量，直接给高度最稳。
            contentRect.sizeDelta = new Vector2(0f, 2704f);

            scroll.viewport = Rect(viewport);
            scroll.content = contentRect;

            // 卡表模板：失活、不参与布局（运行时克隆）
            // ⚠ 这里传 **GameObject** 而不是 CardView 组件：组件重载在某些父级下会返回 null，
            //   而 null 只会在一行之后以 NullReferenceException 暴露（2026-10-28 踩过）。
            CardView templatePrefab = LoadHandPrefab();
            GameObject templateGo = templatePrefab == null
                ? null
                : PrefabUtility.InstantiatePrefab(templatePrefab.gameObject, content.transform) as GameObject;
            if (templateGo == null)
            {
                log.AppendLine("✘ 卡表模板实例化失败（卡面 Prefab 取不到：" + HandCardPath + "）");
                Debug.LogError(log.ToString());
                return canvasGo;
            }

            CardView template = templateGo.GetComponent<CardView>();
            template.gameObject.name = "CatalogCardTemplate";
            Box(Rect(template.gameObject), Mid, Mid, Vector2.zero, new Vector2(184f, 258f));
            template.SetFaceWidth(DeckSelectView.DeckCardFaceWidth);
            template.Clear();
            template.gameObject.SetActive(false);

            // ⑤ 右侧牌组 8 格
            GameObject deckGo = NewUi("DeckPanel", canvasGo.transform);
            Box(Rect(deckGo), Mid, Mid, new Vector2(DeckCenterX, DeckCenterY),
                new Vector2(DeckWidth, DeckHeight));
            var deckBg = deckGo.AddComponent<Image>();
            deckBg.color = UiTheme.Panel;
            if (panelSprite != null)
            {
                deckBg.sprite = panelSprite;
                deckBg.type = Image.Type.Sliced;
            }

            var slots = new CardView[BattleLaunch.DeckSize];
            for (int i = 0; i < slots.Length; i++)
            {
                int column = i % 4;
                int row = i / 4;
                float x = (column - 1.5f) * (SlotWidth + SlotGapX);
                float y = 118f - row * (SlotHeight + SlotGapY);

                GameObject slotGo = NewUi("Slot" + (i + 1), deckGo.transform);
                Box(Rect(slotGo), Mid, Mid, new Vector2(x, y), new Vector2(SlotWidth, SlotHeight));
                var slotImg = slotGo.AddComponent<Image>();
                slotImg.color = UiTheme.PanelEdge;

                CardView slotPrefab = LoadHandPrefab();
                GameObject cardGo = slotPrefab == null
                    ? null
                    : PrefabUtility.InstantiatePrefab(slotPrefab.gameObject, slotGo.transform) as GameObject;
                if (cardGo == null)
                {
                    log.AppendLine("✘ 第 " + (i + 1) + " 格卡面实例化失败");
                    continue;
                }

                CardView view = cardGo.GetComponent<CardView>();
                view.gameObject.name = "Card";
                Box(Rect(view.gameObject), Mid, Mid, Vector2.zero, new Vector2(SlotWidth, SlotHeight));
                view.SetFaceWidth(DeckSelectView.DeckSlotFaceWidth);
                view.Clear();
                view.SetReadOnly();
                slots[i] = view;
            }

            // ⑥ 底部
            Button startButton = BuildButton(canvasGo.transform, "StartButton", "开战",
                new Vector2(DeckCenterX, -468f), new Vector2(360f, 84f), buttonSprite, title,
                out TMP_Text startLabel);

            Button backButton = BuildButton(canvasGo.transform, "BackButton", "返回",
                new Vector2(CatalogCenterX, -468f), new Vector2(280f, 76f), buttonSprite, body,
                out _);

            // ⑦ 提示行
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rect(hintGo), Mid, Mid, new Vector2(0f, -532f), new Vector2(1600f, 40f));
            TMP_Text hint = AddText(hintGo, body, 24f, UiTheme.TextSecondary,
                TextAlignmentOptions.Center, string.Empty);

            // ⑧ 接线
            var deckView = canvasGo.AddComponent<DeckSelectView>();
            var so = new SerializedObject(deckView);
            SetRef(so, "_title", titleText);
            SetRef(so, "_hint", hint);
            SetRef(so, "_playerTab", playerTab);
            SetRef(so, "_playerTabLabel", playerLabel);
            SetRef(so, "_playerTabGlow", playerGlow);
            SetRef(so, "_enemyTab", enemyTab);
            SetRef(so, "_enemyTabLabel", enemyLabel);
            SetRef(so, "_enemyTabGlow", enemyGlow);
            SetRef(so, "_catalogScroll", scroll);
            SetRef(so, "_catalogContent", contentRect);
            SetRef(so, "_catalogTemplate", template);
            SetRef(so, "_startButton", startButton);
            SetRef(so, "_startLabel", startLabel);
            SetRef(so, "_backButton", backButton);

            SerializedProperty array = so.FindProperty("_slotViews");
            if (array != null)
            {
                array.arraySize = slots.Length;
                for (int i = 0; i < slots.Length; i++)
                {
                    array.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  已建 " + CanvasName + "（卡表 + 8 格 × 2 方 + 开战/返回）");
            return canvasGo;
        }

        /// <summary>
        /// <b>每次用之前现取一次卡面 Prefab</b>。
        ///
        /// <para><b>⚠ 为什么不在 RunAll 开头取一次就传下来</b>：中间会经过
        /// <c>EditorSceneManager.NewScene</c> 与资产导入，之前拿到的那个组件引用会变成
        /// <b>已销毁对象</b>（<c>MissingReferenceException: The object of type 'CardView'
        /// has been destroyed</c>，2026-10-28 踩过）。按路径现取最稳，成本可以忽略。</para>
        /// </summary>
        private static CardView LoadHandPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<CardView>(HandCardPath);
        }

        private static void BuildTab(Transform parent, string name, string label, float x,
            TMP_FontAsset font, Sprite sprite,
            out Button button, out TMP_Text labelText, out GameObject glow)
        {
            GameObject go = NewUi(name, parent);
            Box(Rect(go), Mid, Mid, new Vector2(x, 336f), new Vector2(420f, 82f));

            var image = go.AddComponent<Image>();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
            }

            image.color = sprite != null ? Color.white : UiTheme.Panel;

            button = go.AddComponent<Button>();
            button.targetGraphic = image;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rect(labelGo));
            labelText = AddText(labelGo, font, 30f, UiTheme.TextPrimary, TextAlignmentOptions.Center, label);

            // 选中态 = 一圈金色描边（不换底色，铁律 3：颜色只写 prefab）
            GameObject edgeGo = NewUi("SelectedEdge", go.transform);
            Stretch(Rect(edgeGo));
            var edge = edgeGo.AddComponent<Image>();
            edge.color = new Color(1f, 0.82f, 0.4f, 0.28f);
            edge.raycastTarget = false;
            glow = edgeGo;
            glow.SetActive(false);
        }

        private static Button BuildButton(Transform parent, string name, string label,
            Vector2 position, Vector2 size, Sprite sprite, TMP_FontAsset font, out TMP_Text labelText)
        {
            GameObject go = NewUi(name, parent);
            Box(Rect(go), Mid, Mid, position, size);

            var image = go.AddComponent<Image>();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
            }

            image.color = sprite != null ? Color.white : UiTheme.Panel;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rect(labelGo));
            labelText = AddText(labelGo, font, 32f, UiTheme.TextPrimary, TextAlignmentOptions.Center, label);

            return button;
        }

        private static GameObject BuildEntry(GameObject canvasGo, StringBuilder log)
        {
            DeckSelectEntry[] leftovers = Object.FindObjectsOfType<DeckSelectEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null)
                {
                    Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("DeckSelectEntry");
            var entry = go.AddComponent<DeckSelectEntry>();
            var so = new SerializedObject(entry);
            SetRef(so, "_view", canvasGo.GetComponent<DeckSelectView>());
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  入口 DeckSelectEntry（挂在场景根，不进 Prefab）");
            return go;
        }

        private static void EnsureBuildSettings(StringBuilder log)
        {
            List<string> want = SceneRoutes.BuildOrderPaths();

            var next = new List<EditorBuildSettingsScene>();
            for (int i = 0; i < want.Count; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(want[i]) == null)
                {
                    log.AppendLine("  ✘ Build Settings 跳过（场景不存在）：" + want[i]);
                    continue;
                }

                next.Add(new EditorBuildSettingsScene(want[i], true));
            }

            EditorBuildSettings.scenes = next.ToArray();
            log.AppendLine("  Build Settings：" + next.Count + " 个场景 · 启动场景 = " + SceneRoutes.BuildOrder[0]);
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

        private static void EnsureCamera(StringBuilder log)
        {
            if (Object.FindObjectOfType<Camera>() != null)
            {
                return;
            }

            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            Camera cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiTheme.Backdrop;
            cam.orthographic = true;
            log.AppendLine("  补了一台相机（空场景没有）");
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static RectTransform Rect(GameObject node)
        {
            return (RectTransform)node.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        private static TMP_Text AddText(GameObject node, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align, string text)
        {
            var label = node.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        private static void SetRef(SerializedObject data, string field, Object value)
        {
            SerializedProperty prop = data.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[D1] 找不到序列化字段：" + field);
                return;
            }

            prop.objectReferenceValue = value;
        }

        private static string AbsolutePath(string assetPath)
        {
            return System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);
        }
    }
}
