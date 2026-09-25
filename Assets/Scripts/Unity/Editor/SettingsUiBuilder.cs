using System;
using MagicBrawl.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>只追加设置层与配置引用，不重建现有战斗美术。</summary>
    public static class SettingsUiBuilder
    {
        private const string CanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";
        private const string HandPath = "Assets/Prefabs/Ui/CardView_Hand.prefab";
        private const string ConfigDirectory = "Assets/Resources/Characters";
        private static TMP_FontAsset _font;
        private static TMP_FontAsset _titleFont;
        private static Sprite _panelSprite;
        private static Sprite _buttonSprite;

        [MenuItem("魔法乱斗/M39 · 构建设置与测试卡池", priority = 39)]
        public static void BuildAndSave()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后构建设置界面。");
            // 只修改 prefab 中新增的设置子树，不 Apply 场景上的用户覆盖。
            GameObject prefab = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                Ensure(prefab);
                PrefabUtility.SaveAsPrefabAsset(prefab, CanvasPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            foreach (BattleDriver driver in UnityEngine.Object.FindObjectsOfType<BattleDriver>(true))
            {
                if (!driver.gameObject.scene.IsValid()) continue;
                Ensure(driver.gameObject);
                EditorSceneManager.MarkSceneDirty(driver.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[M39] 已保存设置界面、测试卡池和默认玩家/怪物配置。");
        }

        public static void Ensure(GameObject canvas)
        {
            BattleDriver driver = canvas.GetComponent<BattleDriver>();
            BattleUi ui = canvas.GetComponent<BattleUi>();
            if (driver == null || ui == null) throw new InvalidOperationException("BattleCanvas 缺少 BattleDriver / BattleUi。");
            ConfigureCharacters(driver);
            Transform existing = canvas.transform.Find("SettingsLayer");
            if (existing != null)
            {
                var current = existing.GetComponent<SettingsView>();
                if (current == null) throw new InvalidOperationException("SettingsLayer 存在但没有 SettingsView。");
                SetReference(ui, "_settings", current);
                return;
            }

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/Black/Google-Regular.asset");
            _titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/BlackLike/站酷仓耳渔阳体-W03 SDF.asset");
            if (_font == null || _titleFont == null) throw new InvalidOperationException("找不到项目字体资源。");
            _panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Ui/Peek_Panel.png");
            _buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);

            GameObject layer = Node("SettingsLayer", canvas.transform);
            Stretch(Rect(layer));
            // 独立排序层，避免战斗浮层 SetAsLastSibling 后盖住设置。
            Canvas overlay = layer.AddComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = 500;
            layer.AddComponent<GraphicRaycaster>();
            GameObject veil = Node("Veil", layer.transform);
            Stretch(Rect(veil));
            Image(veil, UiTheme.OverlayVeil, true);

            GameObject menu = Panel("Menu", layer.transform, UiLayout.SettingsWidth, UiLayout.SettingsHeight);
            Text("Title", menu.transform, "设置", 0, 210, 500, 60, 42, true);
            Text("Subtitle", menu.transform, "战斗已暂停", 0, 149, 500, 36, 23);
            Button resume = Button("Resume", menu.transform, "继续游戏", 0, 56, 360, 76);
            Button restart = Button("Restart", menu.transform, "重新开始", 0, -48, 360, 76);
            Button test = Button("Test", menu.transform, "测试功能 · 玩家卡池", 0, -152, 440, 76);

            GameObject pool = Panel("CardPoolPanel", layer.transform, UiLayout.CardPoolPanelWidth, UiLayout.CardPoolPanelHeight);
            Text("Title", pool.transform, "玩家卡池 · 测试", 0, 415, 900, 60, 39, true);
            TMP_Text count = Text("Count", pool.transform, "", 0, 360, 900, 38, 25);
            TMP_Text error = Text("Status", pool.transform, "", 0, -338, 1450, 45, 24);
            Button back = Button("Back", pool.transform, "返回", -651, -411, 218, 66);
            Button all = Button("SelectAll", pool.transform, "全选", -370, -411, 218, 66);
            Button reset = Button("ResetDefault", pool.transform, "恢复默认", -64, -411, 280, 66);
            Button save = Button("Save", pool.transform, "保存并重开", 560, -411, 372, 76);

            GameObject scrollNode = Node("Cards", pool.transform);
            Box(Rect(scrollNode), 0, 8, 1572, 616);
            Image(scrollNode, UiTheme.OverlayPanel, true);
            ScrollRect scroll = scrollNode.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
            GameObject viewport = Node("Viewport", scrollNode.transform);
            Stretch(Rect(viewport));
            Rect(viewport).offsetMax = new Vector2(-26, 0);
            Image(viewport, Color.clear, true);
            viewport.AddComponent<RectMask2D>();
            GameObject content = Node("Content", viewport.transform);
            RectTransform contentRect = Rect(content);
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(UiLayout.CardPoolCellWidth, UiLayout.CardPoolCellHeight);
            grid.spacing = new Vector2(6, 10);
            grid.padding = new RectOffset(14, 14, 18, 18);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = UiLayout.CardPoolColumns;
            grid.childAlignment = TextAnchor.UpperCenter;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = Rect(viewport);
            scroll.content = contentRect;

            GameObject bar = Node("Scrollbar", scrollNode.transform);
            RectTransform barRect = Rect(bar);
            barRect.anchorMin = new Vector2(1, 0);
            barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(1, .5f);
            barRect.offsetMin = new Vector2(-18, 8);
            barRect.offsetMax = new Vector2(0, -8);
            Image barBackground = Image(bar, UiTheme.PanelEdge, true);
            GameObject handle = Node("Handle", bar.transform);
            Stretch(Rect(handle));
            Image handleImage = Image(handle, UiTheme.TextSecondary, true);
            Scrollbar scrollbar = bar.AddComponent<Scrollbar>();
            scrollbar.handleRect = Rect(handle);
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;

            CardPoolEntryView template = BuildEntry(layer.transform);
            SettingsView view = layer.AddComponent<SettingsView>();
            var serialized = new SerializedObject(view);
            Set(serialized, "_driver", driver); Set(serialized, "_menu", menu); Set(serialized, "_poolPanel", pool);
            Set(serialized, "_resume", resume); Set(serialized, "_restart", restart); Set(serialized, "_test", test);
            Set(serialized, "_back", back); Set(serialized, "_save", save); Set(serialized, "_selectAll", all);
            Set(serialized, "_resetDefault", reset); Set(serialized, "_count", count); Set(serialized, "_error", error);
            Set(serialized, "_scroll", scroll); Set(serialized, "_content", contentRect); Set(serialized, "_entryTemplate", template);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetReference(ui, "_settings", view);
            pool.SetActive(false);
            layer.SetActive(false);
        }

        private static CardPoolEntryView BuildEntry(Transform parent)
        {
            GameObject root = Node("CardPoolEntryTemplate", parent);
            Box(Rect(root), 0, 0, UiLayout.CardPoolCellWidth, UiLayout.CardPoolCellHeight);
            Image hit = Image(root, Color.clear, true);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPath);
            if (prefab == null) throw new InvalidOperationException("找不到统一卡牌 Prefab。");
            GameObject face = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            Box(Rect(face), 0, 17, UiLayout.CardPoolCardWidth, UiLayout.CardPoolCardHeight);
            CardView card = face.GetComponent<CardView>();
            card.SetFaceWidth(UiLayout.CardPoolCardWidth);
            foreach (Graphic graphic in face.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (Button button in face.GetComponentsInChildren<Button>(true)) button.enabled = false;
            foreach (CardInteractor interactor in face.GetComponentsInChildren<CardInteractor>(true)) interactor.enabled = false;

            GameObject badge = Node("Checkbox", root.transform);
            Box(Rect(badge), 0, -160, 30, 30);
            Image(badge, UiTheme.PanelEdge, false);
            GameObject check = Node("Checked", badge.transform);
            Stretch(Rect(check));
            Image(check, UiTheme.SelectionGlow, false);
            GameObject left = Node("CheckShort", check.transform);
            Box(Rect(left), -5, -1, 5, 12);
            Rect(left).localRotation = Quaternion.Euler(0, 0, 40);
            Image(left, UiTheme.Backdrop, false);
            GameObject right = Node("CheckLong", check.transform);
            Box(Rect(right), 3, 2, 5, 21);
            Rect(right).localRotation = Quaternion.Euler(0, 0, -36);
            Image(right, UiTheme.Backdrop, false);
            Toggle toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = hit;
            toggle.transition = Selectable.Transition.None;
            toggle.isOn = false;
            check.SetActive(false);
            var entry = root.AddComponent<CardPoolEntryView>();
            entry.Configure(card, toggle, check);
            root.SetActive(false);
            return entry;
        }

        private static void ConfigureCharacters(BattleDriver driver)
        {
            if (!AssetDatabase.IsValidFolder(ConfigDirectory)) AssetDatabase.CreateFolder("Assets/Resources", "Characters");
            CharacterConfig player = DefaultAsset("DefaultPlayer", CharacterKind.Player, "player.default", "你");
            CharacterConfig monster = DefaultAsset("DefaultMonster", CharacterKind.Monster, "monster.default", "怪物");
            var data = new SerializedObject(driver);
            SerializedProperty participants = data.FindProperty("_participants");
            if (participants.arraySize != 0) return;
            participants.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                SerializedProperty slot = participants.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("character").objectReferenceValue = i == 0 ? player : monster;
                slot.FindPropertyRelative("control").enumValueIndex = i == 0 ? (int)ControlKind.Human : (int)ControlKind.Ai;
                slot.FindPropertyRelative("team").intValue = i;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static CharacterConfig DefaultAsset(string file, CharacterKind kind, string id, string name)
        {
            string path = ConfigDirectory + "/" + file + ".asset";
            CharacterConfig asset = AssetDatabase.LoadAssetAtPath<CharacterConfig>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<CharacterConfig>();
            asset.kind = kind; asset.characterId = id; asset.displayName = name;
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void SetReference(UnityEngine.Object owner, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(owner); Set(data, field, value); data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Set(SerializedObject data, string field, UnityEngine.Object value) { data.FindProperty(field).objectReferenceValue = value; }
        private static GameObject Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            return node;
        }
        private static RectTransform Rect(GameObject node) { return (RectTransform)node.transform; }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }
        private static Image Image(GameObject node, Color color, bool raycast)
        {
            var image = node.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image;
        }
        private static GameObject Panel(string name, Transform parent, float width, float height)
        {
            GameObject panel = Node(name, parent); Box(Rect(panel), 0, 0, width, height);
            Image image = Image(panel, Color.white, true); image.sprite = _panelSprite; image.type = UnityEngine.UI.Image.Type.Sliced;
            return panel;
        }
        private static TMP_Text Text(string name, Transform parent, string text, float x, float y, float width, float height, float size, bool title = false)
        {
            GameObject node = Node(name, parent); Box(Rect(node), x, y, width, height);
            TextMeshProUGUI label = node.AddComponent<TextMeshProUGUI>();
            label.font = title ? _titleFont : _font; label.text = text; label.fontSize = size;
            label.color = UiTheme.TextPrimary; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center; label.enableWordWrapping = false;
            return label;
        }
        private static Button Button(string name, Transform parent, string label, float x, float y, float width, float height)
        {
            GameObject node = Node(name, parent); Box(Rect(node), x, y, width, height);
            Image image = Image(node, Color.white, true); image.sprite = _buttonSprite; image.type = UnityEngine.UI.Image.Type.Sliced;
            Button button = node.AddComponent<Button>(); button.targetGraphic = image;
            ColorBlock colors = button.colors; colors.disabledColor = UiTheme.TextSecondary; button.colors = colors;
            Text("Label", node.transform, label, 0, 0, width - 22, height - 8, 28, true);
            return button;
        }
    }
}
