using System;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>全局覆盖层</b>的构建器（M44 · 2026-10-05）：给每个场景的 Canvas 挂上
    /// 「右上角设置键 + 右下角卡池键 + 两块浮层」这一套，并回填
    /// <see cref="GlobalOverlayView"/> / <see cref="CardPoolView"/> 的序列化字段。
    ///
    /// <para><b>为什么要一个统一构建器</b>：五个场景（战斗 / 地图 / 商店 / 石台 / 女巫工坊）
    /// 是五棵互不相干的 Canvas 树，而这一套在五处必须长得一样。各写一遍必然漏一处，
    /// 而漏掉的样子是<b>零报错</b>的（那个场景右上角没有键 / 点了没反应）。
    /// 菜单：<c>魔法乱斗/M44 · 构建全局覆盖层（设置键 + 卡池键）</c>，<b>无确认弹窗</b>，
    /// MCP 走反射调 <see cref="RunAll"/>。</para>
    ///
    /// <para><b>⚠ 两种场景的落点不一样，别照抄</b>：</para>
    /// <list type="bullet">
    /// <item><description><b>战斗</b>：场景里的 <c>BattleCanvas</c> 是
    /// <c>BattleCanvas.prefab</c> 的 <b>PrefabInstance</b>（场景文件里只有 <c>PrefabInstance</c> 块）
    /// → <b>只改 Prefab</b>，场景自动同步。</description></item>
    /// <item><description><b>地图 / 商店 / 石台 / 女巫工坊</b>：场景里的 Canvas 是<b>内联拷贝</b>
    /// （真正的子节点都写在 <c>.unity</c> 里）→ <b>Prefab 与场景两处都要补</b>。
    /// 只补一处的话，改的那处生效、另一处照旧（<c>P6</c> 的女巫工坊为此专门加过一个补丁菜单）。</description></item>
    /// </list>
    ///
    /// <para><b>⚠ 商店场景故意不建卡池键</b>：它的右下角本来就是<b>背包键</b>
    /// （打开的就是主角当前卡池），再叠一枚会正好压在同一格上。其余四个场景没有这个键，
    /// 所以由本层补上。</para>
    ///
    /// <para><b>⚠ 本层是「重建式」的</b>：重跑会把整棵 <c>GlobalOverlayLayer</c> 拆掉重建。
    /// 这是新加的层、还没有人手工调过它；将来若有人在这层上拖过节点，
    /// 要改成 M40/M43 那种「只补缺失节点」的增量写法。</para>
    /// </summary>
    public static class GlobalOverlayBuilder
    {
        /// <summary>这一层的节点名（各场景一致，便于查找与排查）。</summary>
        public const string LayerName = "GlobalOverlayLayer";

        private const string HandCardPath = "Assets/Prefabs/Ui/CardView_Hand.prefab";
        private const string PanelSpritePath = "Assets/Art/Ui/Peek_Panel.png";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";
        private const string BattleCanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";

        /// <summary>居中锚点 / pivot 的简写。</summary>
        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);


        // ══════════════════════════════════════════════════════
        //  菜单
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/M44 · 构建全局覆盖层（设置键）", false, 44)]
        private static void MenuEntry()
        {
            // ⚠ 故意不加 EditorUtility.DisplayDialog：带弹窗的菜单在脚本 / MCP 调用时
            //   会挂住主线程（构建器模式的老坑）。
            Debug.Log(RunAll());
        }

        /// <summary>无弹窗入口（MCP 走反射调它）。</summary>
        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[M44] ==== 全局覆盖层 · 开始 ====");

            if (EditorApplication.isPlaying)
            {
                log.AppendLine("✘ 请先退出 Play 再构建（Play 模式下改场景不会落盘）");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            Assets assets;
            if (!LoadAssets(out assets, log))
            {
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            // 2026-10-28：只剩战斗画布要这一层。主菜单与牌组编成界面都不需要
            //「退出到主菜单」（它们本来就在主菜单链路上），事件场景已全部删除。
            // 战斗场景里那份是 PrefabInstance → 只改 Prefab 就够。
            EditPrefab(BattleCanvasPath, assets, log);

            AssetDatabase.SaveAssets();
            log.AppendLine("[M44] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        /// <summary>
        /// 给一棵已经打开的 Canvas 补上这一层（<b>只做 Prefab 内容根 / 场景根这一处</b>）。
        ///
        /// <para>供 <c>BattleUiBuilder</c>（M8）在重建战斗界面时调用 —— 这样「重建战斗界面」
        /// 一条龙里就带上了这一层，不会出现「跑完 M8 右上角的键没了」。</para>
        /// </summary>
        public static void Ensure(GameObject canvasRoot)
        {
            if (canvasRoot == null)
            {
                return;
            }

            Assets assets;
            var log = new StringBuilder();
            if (!LoadAssets(out assets, log))
            {
                Debug.LogError(log.ToString());
                return;
            }

            ApplyToRoot(canvasRoot, assets, log);
        }

        // ══════════════════════════════════════════════════════
        //  两处落点
        // ══════════════════════════════════════════════════════

        /// <summary>改一份 Prefab（LoadPrefabContents → 补层 → SaveAsPrefabAsset）。</summary>
        private static void EditPrefab(string prefabPath, Assets assets, StringBuilder log)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                log.AppendLine("✘ 找不到 Prefab：" + prefabPath);
                return;
            }

            try
            {
                ApplyToRoot(root, assets, log);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                log.AppendLine("  ✔ Prefab " + prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ══════════════════════════════════════════════════════
        //  建层
        // ══════════════════════════════════════════════════════

        /// <summary>把 <c>GlobalOverlayLayer</c> 建到 <paramref name="root"/> 下（先拆旧的）。</summary>
        private static void ApplyToRoot(GameObject root, Assets assets, StringBuilder log)
        {
            Transform old = FindDeep(root.transform, LayerName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            // ── 兼容清理：M39 的旧设置层 ──────────────────────────
            //
            //  2026-10-05 用户口径「所有场景统一只放退出」：旧那三枚键
            //  （继续游戏 / 重新开始 / 测试卡池）已经不需要了，而它自己的三枚里
            //  有两枚依赖 SettingsView 上的序列化字段 —— 所以不能只删按钮、要整层删。
            //  ⚠ 判据加上 `SettingsView` 组件：同名装饰节点不会被误删。
            //  ⚠ SettingsView / SettingsUiBuilder / CardPoolEntryView 的<b>源码保留没删</b>
            //    （想恢复这块调试卡池界面时还有得可依），只是不再有任何节点引用它们。
            Transform legacy = FindDeep(root.transform, "SettingsLayer");
            if (legacy != null && legacy.GetComponent<SettingsView>() != null)
            {
                UnityEngine.Object.DestroyImmediate(legacy.gameObject);
                log.AppendLine("  · 已移除 M39 的旧设置层 SettingsLayer");
            }

            GameObject layer = NewUi(LayerName, root.transform);
            Stretch(Rt(layer));      // 铺满但不吃射线（没有 Image）—— 只有它下面的按键与遮罩吃

            // ── 右上角：设置键（齿轮图标，2026-10-05 改）────────
            //
            //  ⚠ 这里**不再有文字**：老做法是「通用按钮九宫格底图 + 一行『设置』」，
            //    挤成 78×78 之后是个被压扁的蓝色六边形，用户口径是「奇怪的图标 + 文字」。
            //    现在用从 `Hud_Bar.png` 右端抠出来的那颗齿轮（`Setting_Gear.png`），
            //    开 preserveAspect 等比缩放进方框 —— 与工程里「卡池键 = 背包图标」同一套语言。
            GameObject setGo = NewUi("SettingsButton", layer.transform);
            Box(Rt(setGo), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiLayout.OverlaySettingsButtonRight, -UiLayout.OverlaySettingsButtonTop),
                new Vector2(UiLayout.OverlaySettingsButtonSize, UiLayout.OverlaySettingsButtonSize));
            var setImg = setGo.AddComponent<Image>();
            setImg.sprite = assets.Gear;
            setImg.color = assets.Gear != null ? Color.white : UiTheme.Panel;
            setImg.raycastTarget = true;
            setImg.preserveAspect = true;          // 齿轮不拉扁；缺图时退化成一个方块也还能点

            var setButton = setGo.AddComponent<Button>();
            setButton.targetGraphic = setImg;
            setButton.transition = Selectable.Transition.None;

            // ── 设置层（默认失活）────────────────────────────
            GameObject settingsLayer = BuildSettingsLayer(layer.transform, assets);

            // ── 接线 ─────────────────────────────────────────
            var view = layer.AddComponent<GlobalOverlayView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_settingsButton", setButton);
            SetRef(serialized, "_settingsLayer", settingsLayer);
            SetRef(serialized, "_settingsVeil", settingsLayer.transform.Find("Veil").GetComponent<Button>());
            SetRef(serialized, "_resumeButton", settingsLayer.transform.Find("Panel/ResumeButton").GetComponent<Button>());
            SetRef(serialized, "_quitButton", settingsLayer.transform.Find("Panel/QuitButton").GetComponent<Button>());
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 战斗：把这一层接到 `BattleUi._overlay` —— 顶栏齿轮走它打开设置，
            // 而「本局已经抽到过手牌」那份数据也是通过它注入给卡池面板的。
            // ⚠ 这一步不能在别处省掉：`_overlay` 为 null 时齿轮点了没反应，
            //   而且卡池面板里一张牌都不会压暗（两件事都零报错）。
            BattleUi battleUi = root.GetComponent<BattleUi>();
            if (battleUi != null)
            {
                var uiSerialized = new SerializedObject(battleUi);
                SetRef(uiSerialized, "_overlay", view);
                uiSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            settingsLayer.SetActive(false);

            log.AppendLine("  · 覆盖层：设置键");
        }

        /// <summary>设置层：遮罩（点它关闭）+ 面板（标题 / 小字 / 继续游戏 / 退出到主菜单）。</summary>
        private static GameObject BuildSettingsLayer(Transform parent, Assets assets)
        {
            // ⚠ 节点名**刻意不叫** `SettingsLayer`：那是 M39 旧设置层的名字
            //   （`SettingsUiBuilder` 里 `Find("SettingsLayer")` 找的就是它）。
            //   重名会让「旧层删干净了没有」这条判据没法用名字表达，也让将来想恢复 M39 的人
            //   一 `Find` 就撞上这一棵（那边拿到 null 组件会直接抛异常）。
            GameObject layerGo = NewUi("SettingsPanelLayer", parent);
            Stretch(Rt(layerGo));

            GameObject veilGo = NewUi("Veil", layerGo.transform);
            Stretch(Rt(veilGo));
            var veilImg = veilGo.AddComponent<Image>();
            veilImg.color = UiTheme.ShopBagVeil;
            veilImg.raycastTarget = true;
            var veilButton = veilGo.AddComponent<Button>();
            veilButton.targetGraphic = veilImg;
            veilButton.transition = Selectable.Transition.None;

            GameObject panelGo = NewUi("Panel", layerGo.transform);
            Box(Rt(panelGo), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.OverlayPanelWidth, UiLayout.OverlayPanelHeight));
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.sprite = assets.Panel;
            panelImg.color = assets.Panel != null ? Color.white : UiTheme.Panel;
            // ⚠ 面板那一层必须吃射线：遮罩是「点哪儿关哪儿」，面板若不吃射线，
            //   点面板内部的空白就会穿到遮罩上，伸手去点按钮的途中面板先自己关了（零报错）。
            panelImg.raycastTarget = true;
            if (assets.Panel != null)
            {
                panelImg.type = Image.Type.Sliced;
                panelImg.pixelsPerUnitMultiplier = 1f;
            }

            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.OverlayPanelTitleCenterY),
                new Vector2(UiLayout.OverlayPanelWidth - 80f, 60f));
            AddText(titleGo, assets.Title, UiLayout.FontSizeOverlayPanelTitle,
                UiTheme.ShopBagTitleText, TextAlignmentOptions.Center, "设置");

            GameObject subtitleGo = NewUi("Subtitle", panelGo.transform);
            Box(Rt(subtitleGo), Mid, Mid, new Vector2(0f, UiLayout.OverlayPanelSubtitleCenterY),
                new Vector2(UiLayout.OverlayPanelWidth - 80f, 36f));
            AddText(subtitleGo, assets.Body, UiLayout.FontSizeOverlayPanelSubtitle,
                UiTheme.ShopBagCountText, TextAlignmentOptions.Center, "退出将返回开始界面");

            BuildPanelButton(panelGo.transform, "ResumeButton", "继续游戏", assets,
                UiLayout.OverlayPanelResumeCenterY, assets.Button);
            BuildPanelButton(panelGo.transform, "QuitButton", "退出到主菜单", assets,
                UiLayout.OverlayPanelQuitCenterY, assets.Leave);

            return layerGo;
        }

        /// <summary>设置面板里的一枚按钮（「继续游戏」用通用按钮底图，「退出」用离开键那张）。</summary>
        private static void BuildPanelButton(Transform parent, string name, string label, Assets assets,
            float centerY, Sprite sprite)
        {
            GameObject go = NewUi(name, parent);
            Box(Rt(go), Mid, Mid, new Vector2(0f, centerY),
                new Vector2(UiLayout.OverlayPanelButtonWidth, UiLayout.OverlayPanelButtonHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                // ⚠ 与商店「离开」同一口径（用户 2026-09-27 手调过）：这两张底图自带木质高光与
                //   描边，九宫格一旦与源图 border 对不上就会拉出接缝；「离开」那张实测用 Simple。
                img.type = sprite == assets.Leave ? Image.Type.Simple : Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            AddText(labelGo, assets.Title, UiLayout.FontSizeOverlayPanelButton,
                UiTheme.ShopLeaveText, TextAlignmentOptions.Center, label);
        }

        // ══════════════════════════════════════════════════════
        //  资源 / 小工具
        // ══════════════════════════════════════════════════════

        private struct Assets
        {
            public TMP_FontAsset Body;
            public TMP_FontAsset Title;
            public Sprite Panel;
            public Sprite Button;
            public Sprite BagIcon;
            public Sprite Leave;

            /// <summary>设置键用的齿轮图（2026-10-05）。</summary>
            public Sprite Gear;

            public GameObject HandPrefab;
        }

        private static bool LoadAssets(out Assets assets, StringBuilder log)
        {
            assets = new Assets();
            assets.Body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            assets.Title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            assets.Panel = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            assets.Button = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);
            assets.Leave = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.OverlayQuitSpritePath);
            assets.HandPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandCardPath);

            // 齿轮图是 Python 直接写盘的 png，Unity 可能还没把它导成 Sprite → 先钉导入参数再取。
            EnsureSpriteSettings(UiLayout.OverlaySettingsGearSpritePath, log);
            assets.Gear = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.OverlaySettingsGearSpritePath);

            bool ok = true;
            if (assets.Body == null) { log.AppendLine("✘ 缺正文字体：" + BodyFontPath); ok = false; }
            if (assets.Title == null) { log.AppendLine("✘ 缺标题字体：" + TitleFontPath); ok = false; }
            if (assets.HandPrefab == null) { log.AppendLine("✘ 缺卡面 Prefab：" + HandCardPath); ok = false; }
            if (assets.Panel == null) { log.AppendLine("⚠ 缺面板底图（会退化成纯色面板）：" + PanelSpritePath); }
            if (assets.Button == null) { log.AppendLine("⚠ 缺按钮底图（会退化成纯色块）：" + UiLayout.HandPickConfirmSpritePath); }
            if (assets.Leave == null) { log.AppendLine("⚠ 缺离开键底图（退出键会退化成色块）：" + UiLayout.OverlayQuitSpritePath); }
            if (assets.Gear == null) { log.AppendLine("⚠ 缺齿轮图（设置键会退化成一个方块）：" + UiLayout.OverlaySettingsGearSpritePath); }
            return ok;
        }

        /// <summary>
        /// 把一张图钉成「UI 用的 Sprite」（与 <c>MapUiBuilder.EnsureSpriteSettings</c> 同一套口径）。
        ///
        /// <para><b>为什么构建期要管这个</b>：<c>textureType</c> / <c>alphaIsTransparency</c> /
        /// <c>pixelsPerUnit</c> / <c>mipmap</c> 只活在 <c>.meta</c> 里，重导一次就丢、
        /// 丢了<b>零报错</b> —— 表现只有「图是一块白板」或者「边缘一圈黑边」。
        /// 而且 Python 直接写盘的图 Unity 可能还没建过 <c>.meta</c>，这时 <c>GetAtPath</c>
        /// 返回 null，得先 <c>ImportAsset</c> 一次。</para>
        /// </summary>
        private static void EnsureSpriteSettings(string path, StringBuilder log)
        {
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;

            if (imp == null)
            {
                if (!System.IO.File.Exists(AbsolutePath(path)))
                {
                    log.AppendLine("  ⚠ 找不到图：" + path);
                    return;
                }

                AssetDatabase.ImportAsset(path);
                imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null)
                {
                    log.AppendLine("  ✘ 导不进来：" + path);
                    return;
                }
            }

            bool dirty = false;
            if (imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (imp.spriteImportMode != SpriteImportMode.Single)
            {
                imp.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (imp.spritePixelsPerUnit != 100f)
            {
                imp.spritePixelsPerUnit = 100f;
                dirty = true;
            }

            if (imp.mipmapEnabled)
            {
                imp.mipmapEnabled = false;
                dirty = true;
            }

            if (!imp.alphaIsTransparency)
            {
                imp.alphaIsTransparency = true;
                dirty = true;
            }

            if (imp.wrapMode != TextureWrapMode.Clamp)
            {
                imp.wrapMode = TextureWrapMode.Clamp;
                dirty = true;
            }

            if (imp.filterMode != FilterMode.Bilinear)
            {
                imp.filterMode = FilterMode.Bilinear;
                dirty = true;
            }

            if (dirty)
            {
                imp.SaveAndReimport();
                log.AppendLine("  导入参数已修正：" + path);
            }
        }

        /// <summary>把场景资产路径翻译成磁盘绝对路径（判「文件在不在」用）。</summary>
        private static string AbsolutePath(string assetPath)
        {
            // ⚠ 这里必须补到 `<工程根>/Assets`：商店构建器曾经写成
            //   `Combine(工程根, path.Substring("Assets/".Length))` —— 摘掉了 Assets 却没补回来，
            //   于是 File.Exists 恒 false，每跑一次都把场景当「新建」，手改全被抹掉。
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            return System.IO.Path.Combine(projectRoot, assetPath);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            return node;
        }

        private static RectTransform Rt(GameObject node)
        {
            return (RectTransform)node.transform;
        }

        private static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        private static TMP_Text AddText(GameObject node, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align, string text)
        {
            TextMeshProUGUI label = node.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.text = text;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            return label;
        }

        private static void SetRef(SerializedObject data, string field, UnityEngine.Object value)
        {
            SerializedProperty property = data.FindProperty(field);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }
    }
}
