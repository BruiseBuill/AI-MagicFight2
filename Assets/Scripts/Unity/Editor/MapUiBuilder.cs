using System;
using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>P7 · 冒险地图场景</b>的构建器（2026-10-02）。
    ///
    /// <para>菜单：<c>魔法乱斗/P7 · 构建 Map 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>打开 / 新建 <c>Assets/Scenes/Map.unity</c>；</item>
    /// <item>建一个 <c>MapCanvas</c>：底面背景 + 桥根 + 节点根 + 主角棋子 + 顶栏 + 标题 + 提示
    /// + 结束浮层 + 三个<b>失活模板</b>（节点 / 直桥 / 斜桥）；</item>
    /// <item>把 <see cref="MapView"/> / <see cref="MapNodeView"/> 的序列化字段一次性接好
    /// （含 7 张节点图的 <c>Sprite[]</c> 数组）；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/MapCanvas.prefab</c>；</item>
    /// <item>场景根挂一个 <c>MapSceneEntry</c>（调试入口）—— <b>它不进 Prefab</b>；</item>
    /// <item>把 <c>Map / SampleScene / Shop / WitchWorkshop / Upgrade</c> 登记进 Build Settings
    /// （<b>Map 排第一</b> = 正式构建的启动场景）。</item>
    /// </list>
    ///
    /// <para><b>⚠ 模板挂在 <c>Templates</c> 节点下，不挂在 <c>NodeRoot</c> / <c>BridgeRoot</c> 里</b>：
    /// <see cref="MapView.Build"/> 会把那两个 root 的子节点<b>整批清掉</b>再按图重建 ——
    /// 模板要是混在里面就会被一起清掉（第一次重建就空屏，且零报错）。
    /// 靠「跳过某个特定子节点」保命是行不通的：迟早有人加第二种模板然后漏掉一处。</para>
    ///
    /// <para><b>⚠ 「节点图数组」的下标 = <c>MapNodeType</c> 的值</b>（<see cref="UiLayout.MapNodeSpritePaths"/>）——
    /// 加一种节点类型时，那个数组与枚举要一起改；只改枚举的症状是那一类节点**没有图标**
    /// （一块空白方框），零报错。</para>
    /// </summary>
    public static class MapUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/Map.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/MapCanvas.prefab";
        private const string CanvasName = "MapCanvas";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>结束浮层的底板与按钮底图 —— 与商店「背包面板 / 关闭」同一对（本工程 UI 底图只有这一套）。</summary>
        private const string EndPanelSpritePath = "Assets/Art/Ui/Peek_Panel.png";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        // ── 菜单入口 ─────────────────────────────────────────────────

        [MenuItem("魔法乱斗/P7 · 构建 Map 场景", false, 44)]
        private static void MenuEntry()
        {
            // ⚠ 故意不加 EditorUtility.DisplayDialog：带弹窗的菜单被脚本 / MCP 触发会挂住主线程。
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Map] ==== 开始 ====");

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

            // 先把地图那批图的导入参数钉一遍（Python 直接写盘的图 Unity 可能还没建过 .meta）。
            for (int i = 0; i < UiLayout.MapNodeSpritePaths.Length; i++)
            {
                EnsureSpriteSettings(UiLayout.MapNodeSpritePaths[i], log);
            }

            EnsureSpriteSettings(UiLayout.MapBackgroundSpritePath, log);
            EnsureSpriteSettings(UiLayout.MapPlayerSpritePath, log);
            EnsureSpriteSettings(UiLayout.MapBridgeStraightSpritePath, log);
            EnsureSpriteSettings(UiLayout.MapBridgeDiagonalSpritePath, log);

            Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.MapBackgroundSpritePath);
            Sprite playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.MapPlayerSpritePath);
            Sprite ringLine = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.MapRingSpritePath);
            Sprite bridgeStraight = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.MapBridgeStraightSpritePath);
            Sprite bridgeDiagonal = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.MapBridgeDiagonalSpritePath);
            Sprite endPanel = AssetDatabase.LoadAssetAtPath<Sprite>(EndPanelSpritePath);
            Sprite endButton = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);

            var nodeSprites = new Sprite[UiLayout.MapNodeSpritePaths.Length];
            for (int i = 0; i < nodeSprites.Length; i++)
            {
                nodeSprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.MapNodeSpritePaths[i]);
            }

            Missing(log, bg, "地图背景", UiLayout.MapBackgroundSpritePath);
            Missing(log, playerSprite, "主角立绘", UiLayout.MapPlayerSpritePath);
            Missing(log, ringLine, "高亮框", UiLayout.MapRingSpritePath);
            Missing(log, bridgeStraight, "直桥", UiLayout.MapBridgeStraightSpritePath);
            Missing(log, bridgeDiagonal, "斜桥", UiLayout.MapBridgeDiagonalSpritePath);
            Missing(log, endPanel, "结束浮层底图", EndPanelSpritePath);
            Missing(log, endButton, "结束浮层按钮", UiLayout.HandPickConfirmSpritePath);
            for (int i = 0; i < nodeSprites.Length; i++)
            {
                Missing(log, nodeSprites[i], "节点图[" + (MapNodeType)i + "] " + MapRoutes.DisplayName((MapNodeType)i),
                    UiLayout.MapNodeSpritePaths[i]);
            }

            // 1) 场景
            EditorSceneManager.SaveOpenScenes();
            bool created = !System.IO.File.Exists(AbsolutePath(ScenePath));
            UnityEngine.SceneManagement.Scene scene = created
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            log.AppendLine((created ? "  新建 " : "  打开 ") + ScenePath);

            // 2) 幂等：拆掉旧的 Canvas
            GameObject old = GameObject.Find(CanvasName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old);
                log.AppendLine("  拆掉旧 " + CanvasName);
            }

            // 3) 建新
            GameObject canvasGo = BuildCanvas(body, title, bg, playerSprite, ringLine, bridgeStraight,
                bridgeDiagonal, endPanel, endButton, nodeSprites, log);
            EnsureEventSystem();
            EnsureCamera(log);

            // 4) 存 Prefab（先建调试入口：它挂在场景根，天然不进 Canvas 那棵子树）
            GameObject entry = BuildDebugEntry(canvasGo, log);
            PrefabUtility.SaveAsPrefabAsset(canvasGo, PrefabPath);
            log.AppendLine("  已存 " + PrefabPath);

            // 5) 存场景
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            log.AppendLine("  已存 " + ScenePath);

            // 6) Build Settings
            EnsureBuildSettings(log);

            if (entry != null)
            {
                log.AppendLine("  调试入口：场景根下的 " + entry.name
                               + "（改 Inspector 的 seed 换一张图；勾 skipWalkAnimation 可秒切）");
            }

            log.AppendLine("[Map] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        private static void Missing(StringBuilder log, UnityEngine.Object asset, string what, string path)
        {
            if (asset == null)
            {
                log.AppendLine("  ✘ 缺" + what + "：" + path);
            }
        }

        // ══════════════════════════════════════════════════════
        //  Canvas
        // ══════════════════════════════════════════════════════

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title, Sprite bg,
            Sprite playerSprite, Sprite ringLine, Sprite bridgeStraight, Sprite bridgeDiagonal,
            Sprite endPanel, Sprite endButton, Sprite[] nodeSprites, StringBuilder log)
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

            // ① 背景：素材正好是参考分辨率（1920×1080）→ 铺满即可，不用 cover 那套换算。
            GameObject bgGo = NewUi("Bg_Map", canvasGo.transform);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = bg;
            bgImg.color = bg != null ? Color.white : UiTheme.Backdrop;
            bgImg.raycastTarget = false;
            Box(Rt(bgGo), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.MapBackgroundWidth, UiLayout.MapBackgroundHeight));

            // ② 桥层（在节点**下面** → 先建）
            GameObject bridgeRoot = NewUi("BridgeRoot", canvasGo.transform);
            FullRoot(Rt(bridgeRoot));

            // ③ 节点层
            GameObject nodeRoot = NewUi("NodeRoot", canvasGo.transform);
            FullRoot(Rt(nodeRoot));

            // ④ 主角棋子（在节点**上面** → 后建）
            GameObject playerGo = NewUi("Player", canvasGo.transform);
            var playerImg = playerGo.AddComponent<Image>();
            playerImg.sprite = playerSprite;
            playerImg.color = playerSprite != null ? Color.white : UiTheme.MiniCardFace;
            playerImg.raycastTarget = false;
            playerImg.preserveAspect = true;

            float playerH = UiLayout.MapPlayerHeight;
            float playerW = playerH * UiLayout.MapPlayerAspect;
            Box(Rt(playerGo), Vector2.zero, new Vector2(0.5f, 0f),
                UiLayout.MapNodePosition(0, 0) + new Vector2(UiLayout.MapPlayerOffsetX, UiLayout.MapPlayerOffsetY),
                new Vector2(playerW, playerH));

            // ⑤ 顶栏（与商店同一套常量；两个数字位换成「进度」「金币」）
            BuildHud(canvasGo.transform, body, title, log);

            // ⑥ 标题 + 提示
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.MapTitleCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.MapTitleWidth, UiLayout.MapTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeMapTitle,
                UiTheme.MapTitleText, TextAlignmentOptions.Center, "冒　险　地　图");

            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rt(hintGo), Mid, Mid, new Vector2(0f, UiLayout.MapHintCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.MapHintWidth, UiLayout.MapHintHeight));
            TMP_Text hintText = AddText(hintGo, body, UiLayout.FontSizeMapHint,
                UiTheme.MapHintText, TextAlignmentOptions.Center, "");

            // ⑦ 结束浮层
            BuildEndLayer(canvasGo.transform, body, title, endPanel, endButton, log);

            // ⑧ 模板（失活；挂在独立的 Templates 下，**不是** NodeRoot / BridgeRoot 的子节点）
            GameObject templates = NewUi("Templates", canvasGo.transform);
            FullRoot(Rt(templates));

            RectTransform straight = BuildBridgeTemplate(templates.transform, "BridgeStraightTemplate",
                bridgeStraight);
            RectTransform diagonal = BuildBridgeTemplate(templates.transform, "BridgeDiagonalTemplate",
                bridgeDiagonal);

            GameObject nodeTemplateGo = NewUi("NodeTemplate", templates.transform);
            RectTransform nodeRt = Rt(nodeTemplateGo);
            Box(nodeRt, Vector2.zero, Mid, Vector2.zero,
                new Vector2(UiLayout.MapNodeSize, UiLayout.MapNodeSize));
            MapNodeView nodeTemplate = BuildNodeTemplate(nodeTemplateGo, body, ringLine, log);

            templates.SetActive(false);

            // ⑨ 接线
            var view = canvasGo.AddComponent<MapView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_bridgeRoot", Rt(bridgeRoot));
            SetRef(serialized, "_nodeRoot", Rt(nodeRoot));
            SetRef(serialized, "_nodeTemplate", nodeTemplate);
            SetRef(serialized, "_bridgeStraightTemplate", straight);
            SetRef(serialized, "_bridgeDiagonalTemplate", diagonal);
            SetRef(serialized, "_player", Rt(playerGo));
            SetRef(serialized, "_playerImage", playerImg);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_hint", hintText);
            SetRef(serialized, "_progressText", FindDeep(canvasGo.transform, "ProgressNumber") != null
                ? FindDeep(canvasGo.transform, "ProgressNumber").GetComponent<TMP_Text>() : null);
            SetRef(serialized, "_goldText", FindDeep(canvasGo.transform, "GoldNumber") != null
                ? FindDeep(canvasGo.transform, "GoldNumber").GetComponent<TMP_Text>() : null);
            SetRef(serialized, "_endLayer", FindDeep(canvasGo.transform, "EndLayer") != null
                ? FindDeep(canvasGo.transform, "EndLayer").gameObject : null);
            SetRef(serialized, "_endTitle", FindDeep(canvasGo.transform, "EndTitle") != null
                ? FindDeep(canvasGo.transform, "EndTitle").GetComponent<TMP_Text>() : null);
            SetRef(serialized, "_endBody", FindDeep(canvasGo.transform, "EndBody") != null
                ? FindDeep(canvasGo.transform, "EndBody").GetComponent<TMP_Text>() : null);
            SetRef(serialized, "_endButton", FindDeep(canvasGo.transform, "EndButton") != null
                ? FindDeep(canvasGo.transform, "EndButton").GetComponent<Button>() : null);
            SetRef(serialized, "_endButtonLabel", FindDeep(canvasGo.transform, "EndButtonLabel") != null
                ? FindDeep(canvasGo.transform, "EndButtonLabel").GetComponent<TMP_Text>() : null);

            // 节点图数组：下标 = MapNodeType 的值
            SerializedProperty icons = serialized.FindProperty("_nodeIcons");
            icons.arraySize = nodeSprites.Length;
            for (int i = 0; i < nodeSprites.Length; i++)
            {
                icons.GetArrayElementAtIndex(i).objectReferenceValue = nodeSprites[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  Canvas OK：背景 " + (bg != null ? "OK" : "缺图（纯色底）")
                           + " · 节点模板（" + nodeSprites.Length + " 张节点图已接）"
                           + " · 桥模板 直/斜 " + (bridgeStraight != null ? "OK" : "缺")
                           + "/" + (bridgeDiagonal != null ? "OK" : "缺")
                           + " · 主角 " + (playerSprite != null ? "OK" : "缺图")
                           + " · 结束浮层 " + (endPanel != null ? "OK" : "缺图"));
            return canvasGo;
        }

        /// <summary>铺满父级的空根节点（BridgeRoot / NodeRoot / Templates 共用）。</summary>
        private static RectTransform FullRoot(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// 顶栏 —— 参数与商店 <c>BuildHud</c> 逐条一致（同一张 <c>Hud_Bar</c>、同一套 <c>UiLayout.Hud*</c>），
        /// 只是两个数字位的名字改成 <c>ProgressNumber</c> / <c>GoldNumber</c>。
        ///
        /// <para><b>⚠ 为什么不沿用战斗那套 <c>HpNumber</c> / <c>ResNumber</c></b>：那套名字的语义是
        /// 「生命 / 资源」，而地图顶上这两个数一个是层数进度、一个是金币。名字留着旧语义，
        /// 下一个改的人会按名字去猜，而地图里<b>没有</b>「生命」这个量（战斗的入场生命不由地图给），
        /// 那才是最费时间的错。</para>
        /// </summary>
        private static void BuildHud(Transform parent, TMP_FontAsset body, TMP_FontAsset title,
            StringBuilder log)
        {
            GameObject hudGo = NewUi("Hud", parent);
            RectTransform hudRoot = Rt(hudGo);
            Box(hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudLeft, -UiLayout.HudTop),
                new Vector2(UiLayout.HudBarWidth, UiLayout.HudBarHeight));

            BattleArtLibrary art = BattleArtLibrary.Instance;
            Sprite hudBarSprite = art != null ? art.HudBar : null;

            GameObject barGo = NewUi("Hud_Bar", hudRoot);
            Stretch(Rt(barGo));
            var barImg = barGo.AddComponent<Image>();
            barImg.sprite = hudBarSprite;
            barImg.color = hudBarSprite != null ? Color.white : UiTheme.Panel;
            barImg.raycastTarget = false;

            GameObject nameGo = NewUi("Name", hudRoot);
            Box(Rt(nameGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudNameX, -UiLayout.HudNameY),
                new Vector2(UiLayout.HudNameWidth, UiLayout.HudNameHeight));
            AddText(nameGo, title, UiLayout.FontSizeHudName, UiTheme.TextPrimary,
                TextAlignmentOptions.Left, "旅者");

            GameObject subGo = NewUi("Subtitle", hudRoot);
            Box(Rt(subGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudSubX, -UiLayout.HudSubY),
                new Vector2(UiLayout.HudSubWidth, UiLayout.HudSubHeight));
            AddText(subGo, body, UiLayout.FontSizeHudSub, UiTheme.TextSecondary,
                TextAlignmentOptions.Left, "冒险地图");

            GameObject progressGo = NewUi("ProgressNumber", hudRoot);
            Box(Rt(progressGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudHpX, -UiLayout.HudHpY),
                new Vector2(UiLayout.HudHpWidth, UiLayout.HudHpHeight));
            AddText(progressGo, body, UiLayout.FontSizeHudHp, UiTheme.TextPrimary,
                TextAlignmentOptions.Left, "进度 0 / 8");

            GameObject goldGo = NewUi("GoldNumber", hudRoot);
            Box(Rt(goldGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudResX, -UiLayout.HudResY),
                new Vector2(UiLayout.HudResWidth, UiLayout.HudResHeight));
            AddText(goldGo, body, UiLayout.FontSizeHudRes, UiTheme.AuraReady,
                TextAlignmentOptions.Left, "50");

            log.AppendLine("  顶栏：Hud_Bar" + (hudBarSprite != null ? "（战斗美术）" : "（缺图·纯色板）")
                           + " + Name / Subtitle / ProgressNumber / GoldNumber");
        }

        /// <summary>
        /// 节点模板。子节点顺序 = 绘制顺序（越靠后越上层）：
        /// 高亮框 ×2 → 图标 → 压暗纱 → 小字 → 命中区。
        /// </summary>
        private static MapNodeView BuildNodeTemplate(GameObject root, TMP_FontAsset body, Sprite ringLine,
            StringBuilder log)
        {
            float nodeSize = UiLayout.MapNodeSize;
            float frameSize = nodeSize + 34f;

            // ① 「可以去」的金色圆角框
            Image ring = BuildFrame(root.transform, "RingLine", ringLine, frameSize, UiTheme.MapNodeRing);

            // ② 「你在这儿」的蓝色圆角框
            Image current = BuildFrame(root.transform, "RingCurrent", ringLine, frameSize,
                UiTheme.PlayerAccent);

            // ③ 节点图
            GameObject iconGo = NewUi("Icon", root.transform);
            Box(Rt(iconGo), Mid, Mid, Vector2.zero, new Vector2(nodeSize, nodeSize));
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = null;
            icon.color = Color.white;
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            // ④ 压暗纱
            //
            //    ⚠ sprite 留空、**由运行时按节点图填**（MapNodeView.Bind 里那句），
            //      因为每个节点的图不一样。这里必须开 preserveAspect：
            //      否则运行时换上来的图会被拉成正方形（节点图是 274×250 的椭圆）。
            GameObject dimGo = NewUi("Dim", root.transform);
            Box(Rt(dimGo), Mid, Mid, Vector2.zero, new Vector2(nodeSize, nodeSize));
            var dim = dimGo.AddComponent<Image>();
            dim.sprite = null;
            dim.color = UiTheme.MapNodeDim;
            dim.raycastTarget = false;
            dim.preserveAspect = true;

            // ⑤ 小字（只在「可以去 / 当前」时显示）
            GameObject labelGo = NewUi("Label", root.transform);
            Box(Rt(labelGo), Mid, Mid, new Vector2(0f, UiLayout.MapLabelOffsetY),
                new Vector2(UiLayout.MapLabelWidth, UiLayout.MapLabelHeight));
            TMP_Text label = AddText(labelGo, body, UiLayout.FontSizeMapLabel,
                UiTheme.MapNodeLabel, TextAlignmentOptions.Center, "");

            // ⑥ 命中区（唯一吃射线的）
            GameObject hitGo = NewUi("Hit", root.transform);
            Box(Rt(hitGo), Mid, Mid, Vector2.zero, new Vector2(nodeSize, nodeSize));
            var hit = hitGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var view = root.AddComponent<MapNodeView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_ringLine", ring);
            SetRef(serialized, "_ringCurrent", current);
            SetRef(serialized, "_icon", icon);
            SetRef(serialized, "_dim", dim);
            SetRef(serialized, "_labelRoot", labelGo);
            SetRef(serialized, "_label", label);
            SetRef(serialized, "_hit", hit);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  节点模板：高亮框(金/蓝) + 图标 " + nodeSize + " + 压暗纱 + 小字 + 命中区");
            return view;
        }

        private static Image BuildFrame(Transform parent, string name, Sprite sprite, float size, Color color)
        {
            GameObject go = NewUi(name, parent);
            Box(Rt(go), Mid, Mid, Vector2.zero, new Vector2(size, size));
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? color : new Color(color.r, color.g, color.b, 0.25f);
            img.raycastTarget = false;
            if (sprite != null)
            {
                // ⚠ 圆角框必须 Sliced：Simple 会把圆角给抻成椭圆，而且零报错。
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }

            go.SetActive(false);
            return img;
        }

        private static RectTransform BuildBridgeTemplate(Transform parent, string name, Sprite sprite)
        {
            GameObject go = NewUi(name, parent);
            var rt = Rt(go);
            float w = sprite != null ? sprite.rect.width : 315f;
            float h = sprite != null ? sprite.rect.height : 70f;
            Box(rt, Vector2.zero, Mid, Vector2.zero, new Vector2(w, h));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.PanelEdge;
            img.raycastTarget = false;

            go.SetActive(false);
            return rt;
        }

        /// <summary>
        /// 结束浮层（通关 / 旅程结束）。Veil 吃射线 —— 它挡住的正是底下那些节点的点击。
        /// </summary>
        private static void BuildEndLayer(Transform parent, TMP_FontAsset body, TMP_FontAsset title,
            Sprite panelSprite, Sprite buttonSprite, StringBuilder log)
        {
            GameObject layer = NewUi("EndLayer", parent);
            Stretch(Rt(layer));

            GameObject veil = NewUi("Veil", layer.transform);
            Stretch(Rt(veil));
            var veilImg = veil.AddComponent<Image>();
            veilImg.color = UiTheme.MapEndVeil;
            veilImg.raycastTarget = true;

            GameObject panel = NewUi("Panel", layer.transform);
            Box(Rt(panel), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.MapEndPanelWidth, UiLayout.MapEndPanelHeight));
            var panelImg = panel.AddComponent<Image>();
            panelImg.sprite = panelSprite;
            panelImg.color = panelSprite != null ? Color.white : UiTheme.Panel;
            panelImg.raycastTarget = true;
            if (panelSprite != null)
            {
                panelImg.type = Image.Type.Sliced;
                panelImg.pixelsPerUnitMultiplier = 1f;
            }

            GameObject titleGo = NewUi("EndTitle", panel.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.MapEndTitleCenterY),
                new Vector2(UiLayout.MapEndTitleWidth, UiLayout.MapEndTitleHeight));
            AddText(titleGo, title, UiLayout.FontSizeMapEndTitle, UiTheme.MapEndText,
                TextAlignmentOptions.Center, "通　关");

            GameObject bodyGo = NewUi("EndBody", panel.transform);
            Box(Rt(bodyGo), Mid, Mid, new Vector2(0f, UiLayout.MapEndBodyCenterY),
                new Vector2(UiLayout.MapEndBodyWidth, UiLayout.MapEndBodyHeight));
            TMP_Text bodyText = AddText(bodyGo, body, UiLayout.FontSizeMapEndBody,
                UiTheme.MapEndText, TextAlignmentOptions.Center, "");
            bodyText.enableWordWrapping = true;

            GameObject buttonGo = NewUi("EndButton", panel.transform);
            Box(Rt(buttonGo), Mid, Mid, new Vector2(0f, UiLayout.MapEndButtonCenterY),
                new Vector2(UiLayout.MapEndButtonWidth, UiLayout.MapEndButtonHeight));
            var buttonImg = buttonGo.AddComponent<Image>();
            buttonImg.sprite = buttonSprite;
            buttonImg.color = buttonSprite != null ? Color.white : UiTheme.PanelEdge;
            buttonImg.raycastTarget = true;
            if (buttonSprite != null)
            {
                buttonImg.type = Image.Type.Sliced;
                buttonImg.pixelsPerUnitMultiplier = 1f;
            }

            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = buttonImg;
            // 底图自带高光与描边 → 颜色过渡会把它刷成单色（商店那两处已经定过这条口径）。
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("EndButtonLabel", buttonGo.transform);
            Stretch(Rt(labelGo));
            AddText(labelGo, title, UiLayout.FontSizeMapEndButton, UiTheme.MapEndText,
                TextAlignmentOptions.Center, "重新开始");

            layer.SetActive(false);
            log.AppendLine("  结束浮层：Veil + Panel(Peek_Panel) + EndTitle / EndBody / EndButton"
                           + " · 底图 " + (panelSprite != null ? "OK" : "缺图"));
        }

        // ══════════════════════════════════════════════════════
        //  调试入口 / 场景杂项 / Build Settings
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 场景里的调试入口（<see cref="MapSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的<b>父级</b>（场景根）</b>：本构建器存 Prefab 时只保存 Canvas
        /// 这一棵子树，挂在场景根就天然不会进 Prefab —— 不需要「存完再删」那套容易漏的做法。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            MapSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<MapSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("MapSceneEntry");
            var entry = go.AddComponent<MapSceneEntry>();

            MapView view = canvasGo.GetComponent<MapView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  调试入口 MapSceneEntry（挂在场景根，不进 Prefab）");
            return go;
        }

        private static void EnsureCamera(StringBuilder log)
        {
            if (UnityEngine.Object.FindObjectOfType<Camera>() != null)
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
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        /// <summary>
        /// 把五个场景登记进 Build Settings，<b>地图排第一</b>。
        ///
        /// <para><b>⚠ 这不是可选项</b>：<c>SceneManager.LoadScene("Shop")</c> 这类按名字的调用，
        /// 场景不在 Build Settings 里就会当场抛错 —— 而它抛的时刻是<b>玩家点到那个节点的时候</b>，
        /// 前面所有验证都看不出来。</para>
        ///
        /// <para>顺序 = <see cref="MapRoutes.BuildOrder"/>；<b>排第一的那个就是正式构建的启动场景</b>，
        /// 所以地图在最前 = 打开游戏从地图开始。</para>
        /// </summary>
        private static void EnsureBuildSettings(StringBuilder log)
        {
            List<string> want = MapRoutes.BuildOrderPaths();

            var keep = new List<EditorBuildSettingsScene>();
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            for (int i = 0; i < current.Length; i++)
            {
                if (!want.Contains(current[i].path))
                {
                    keep.Add(current[i]);
                }
            }

            var next = new List<EditorBuildSettingsScene>();
            int missing = 0;
            for (int i = 0; i < want.Count; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(want[i]) == null)
                {
                    log.AppendLine("  ✘ Build Settings 跳过（场景不存在）：" + want[i]);
                    continue;
                }

                bool existed = false;
                for (int k = 0; k < current.Length; k++)
                {
                    if (current[k].path == want[i])
                    {
                        existed = true;
                        break;
                    }
                }

                if (!existed)
                {
                    missing++;
                }

                next.Add(new EditorBuildSettingsScene(want[i], true));
            }

            next.AddRange(keep);
            EditorBuildSettings.scenes = next.ToArray();

            log.AppendLine("  Build Settings：" + want.Count + " 个场景（新登记 " + missing
                           + " 个）· 启动场景 = " + MapRoutes.MapScene);
        }

        // ══════════════════════════════════════════════════════
        //  导入参数
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把一张图钉成「UI 用的 Sprite」。
        ///
        /// <para><b>为什么构建期要管这个</b>：<c>alphaIsTransparency</c> / <c>pixelsPerUnit</c> /
        /// <c>mipmap</c> 只活在 <c>.meta</c> 里，重导一次就丢、丢了<b>零报错</b>
        /// —— 表现只有「图边缘有一圈黑边」或者「图糊成一片」。
        /// 而且 Python 直接写盘的图 Unity 可能还没建过 <c>.meta</c>，
        /// 这时候 <c>GetAtPath</c> 返回 null，得先 <c>ImportAsset</c> 一次。</para>
        /// </summary>
        private static void EnsureSpriteSettings(string path, StringBuilder log)
        {
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;

            if (imp == null)
            {
                if (!System.IO.File.Exists(AbsolutePath(path)))
                {
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

        /// <summary>
        /// 资产路径 → 磁盘绝对路径。
        ///
        /// <para><b>⚠ 这里写成 <c>Combine(工程根, assetPath)</c> 是对的</b>：
        /// <c>assetPath</c> 本身以 <c>Assets/</c> 开头，工程根就是它的上一级。
        /// 商店构建器当初写的是「工程根 + 摘掉 <c>Assets/</c> 的路径」，于是恒判「场景不存在」，
        /// 每跑一次构建就把场景再新建一次 —— 手改的那些 Inspector 值全被抹掉
        /// （见 <c>ShopUiBuilder.AbsolutePath</c> 的注释）。</para>
        /// </summary>
        private static string AbsolutePath(string assetPath)
        {
            return System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName,
                assetPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        // ══════════════════════════════════════════════════════
        //  辅助（与其它构建器同一套写法）
        // ══════════════════════════════════════════════════════

        private static GameObject NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        private static void SetRef(SerializedObject data, string field, UnityEngine.Object value)
        {
            SerializedProperty prop = data.FindProperty(field);
            if (prop == null)
            {
                throw new InvalidOperationException(
                    "字段不存在：" + field + " —— 检查脚本里的 SerializeField 名字是否与构建器一致。");
            }

            prop.objectReferenceValue = value;
        }

        private static RectTransform Rt(GameObject go)
        {
            return (RectTransform)go.transform;
        }

        private static RectTransform Stretch(RectTransform rt, float left = 0f, float bottom = 0f,
            float right = 0f, float top = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        private static RectTransform Box(RectTransform rt, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static TMP_Text AddText(GameObject go, TMP_FontAsset font, float size,
            Color color, TextAlignmentOptions align, string text = "")
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                t.font = font;
            }

            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
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
    }
}
