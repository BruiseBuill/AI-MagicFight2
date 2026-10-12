using System;
using System.Collections.Generic;
using System.Text;
using MagicBrawl.App;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>P8 · 宝箱事件场景</b>的构建器（2026-10-09）。
    ///
    /// <para>菜单：<c>魔法乱斗/P8 · 构建 Treasure 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>打开 / 新建 <c>Assets/Scenes/Treasure.unity</c>；</item>
    /// <item>建一个 <c>TreasureCanvas</c>：背景 + 箱子（关/开两张图）+ 开箱读数 + 标题 + 引导行 + 离开键；</item>
    /// <item>把 <see cref="TreasureView"/> 的序列化字段一次性接好；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/TreasureCanvas.prefab</c>；</item>
    /// <item>场景根挂一个 <c>TreasureSceneEntry</c>（调试入口）—— <b>它不进 Prefab</b>；</item>
    /// <item>把 <c>Treasure</c> 登记进 Build Settings（顺序 = <see cref="MapRoutes.BuildOrder"/>）。</item>
    /// </list>
    ///
    /// <para><b>⚠ 版式与商店 / 石台 / 女巫逐条同源</b>：背景 cover 铺、标题带 1002、引导行 158、
    /// 右下角「离开」（同一个 <c>Shop_LeaveButton</c> 底图与同一套尺寸）。所以本构建器只写
    /// <b>本场景独有</b>的那几样 —— 背景、箱子、开箱读数；其余坐标全部取自
    /// <c>UiLayout.Treasure*</c>，不在这里另算一份。</para>
    ///
    /// <para><b>⚠ 那两张箱子图要先"抠成透明底"</b>：它们是整张渲染图（1024×576 / 1671×941），
    /// 成品在 <c>Art/Ui/Treasure_Chest*.png</c>（已裁到内容边缘）。本构建器不负责抠图 ——
    /// 但会把两张图的导入参数钉一遍（Sprite / PPU 100 / 关 mipmap / AlphaIsTransparency /
    /// Clamp / Bilinear），否则重导一次就会在箱子边缘留一圈黑边，而且零报错。</para>
    /// </summary>
    public static class TreasureUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/Treasure.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/TreasureCanvas.prefab";
        private const string CanvasName = "TreasureCanvas";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>开箱读数下面那行小字「金币」相对读数的偏移（画布单位）。</summary>
        private const float GainCaptionOffsetY = -104f;

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        [MenuItem("魔法乱斗/P8 · 构建 Treasure 场景", false, 46)]
        private static void MenuEntry()
        {
            // ⚠ 故意不加 EditorUtility.DisplayDialog：带弹窗的菜单被脚本 / MCP 触发会挂住主线程。
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Treasure] ==== 开始 ====");

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

            // 先把这几张图的导入参数钉住（Python 直接写盘的图 Unity 可能还没建过 .meta）。
            EnsureSpriteSettings(UiLayout.TreasureBgSpritePath, log);
            EnsureSpriteSettings(UiLayout.TreasureChestClosedSpritePath, log);
            EnsureSpriteSettings(UiLayout.TreasureChestOpenSpritePath, log);

            Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.TreasureBgSpritePath);
            Sprite closed = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.TreasureChestClosedSpritePath);
            Sprite open = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.TreasureChestOpenSpritePath);
            Sprite leave = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopLeaveSpritePath);

            Missing(log, bg, "宝箱背景", UiLayout.TreasureBgSpritePath);
            Missing(log, closed, "关着的箱子", UiLayout.TreasureChestClosedSpritePath);
            Missing(log, open, "开着的箱子", UiLayout.TreasureChestOpenSpritePath);
            Missing(log, leave, "「离开」按钮底图", UiLayout.ShopLeaveSpritePath);

            if (closed == null)
            {
                log.AppendLine("✘ 关箱图是本体 —— 没有它这个场景没有意义，中止。");
                Debug.LogError(log.ToString());
                return log.ToString();
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
            GameObject canvasGo = BuildCanvas(body, title, bg, closed, open, leave, log);
            EnsureEventSystem();
            EnsureCamera(log);

            // 4) 存 Prefab（此刻场景里还没有调试入口 → 它天然不进 Prefab）
            PrefabUtility.SaveAsPrefabAsset(canvasGo, PrefabPath);
            log.AppendLine("  已存 " + PrefabPath);

            // 5) 存场景
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            log.AppendLine("  已存 " + ScenePath);

            // 6) 调试入口（**存完 Prefab 之后再挂**）
            GameObject debugEntry = BuildDebugEntry(canvasGo, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 7) Build Settings
            EnsureBuildSettings(log);

            if (debugEntry != null)
            {
                log.AppendLine("  调试入口：场景根下的 " + debugEntry.name
                               + "（改 Inspector 的 _gain 换开箱金额）");
            }

            log.AppendLine("[Treasure] ==== 完成 ====");
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
            Sprite closed, Sprite open, Sprite leave, StringBuilder log)
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

            // ① 背景（cover；缺图时垫一块深色底，否则后面全是相机底色）
            if (bg == null)
            {
                GameObject fallback = NewUi("Backdrop", canvasGo.transform);
                AddImage(fallback, UiTheme.Backdrop);
                Stretch(Rt(fallback));
            }

            BuildBackground(canvasGo.transform, bg, log);

            // ② 箱子（本场景唯一可点的东西）—— 必须在背景**之后**建（后建 = 画在上面）
            Button chestButton = BuildChest(canvasGo.transform, closed, log);

            // ③ 开箱读数（默认失活，压在箱子正上方）
            GameObject gainRoot = BuildGain(canvasGo.transform, body, title, log);

            // ④ 标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), Mid, Mid,
                new Vector2(0f, UiLayout.TreasureTitleCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.TreasureTitleWidth, UiLayout.TreasureTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeTreasureTitle,
                UiTheme.ShopTitleText, TextAlignmentOptions.Center, "宝箱");

            // ⑤ 引导行
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rt(hintGo), Mid, Mid,
                new Vector2(0f, UiLayout.TreasureHintCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.TreasureHintWidth, UiLayout.TreasureHintHeight));
            TMP_Text hintText = AddText(hintGo, body, UiLayout.FontSizeTreasureHint,
                UiTheme.UpgradeHintText, TextAlignmentOptions.Center, "点击宝箱，把它打开");

            // ⑥ 离开键
            Button leaveButton = BuildLeaveButton(canvasGo.transform, body, leave, out TMP_Text leaveLabel);

            // ── 接线 ─────────────────────────────────────────────
            var view = canvasGo.AddComponent<TreasureView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_chestButton", chestButton);
            SetRef(serialized, "_chest", chestButton.GetComponent<Image>());
            SetRef(serialized, "_chestClosed", closed);
            SetRef(serialized, "_chestOpen", open);
            SetRef(serialized, "_gainRoot", gainRoot);
            SetRef(serialized, "_gain", gainRoot.transform.Find("Gain") != null
                ? gainRoot.transform.Find("Gain").GetComponent<TMP_Text>() : null);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_hintRoot", hintGo);
            SetRef(serialized, "_hint", hintText);
            SetRef(serialized, "_leaveButton", leaveButton);
            SetRef(serialized, "_leaveLabel", leaveLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  Canvas OK：背景 " + (bg != null ? "OK" : "缺图（纯色底）")
                           + " · 箱子 " + (closed != null ? "关 OK" : "缺关图")
                           + "/" + (open != null ? "开 OK" : "缺开图")
                           + " · 开箱读数 OK · 标题 OK · 引导行 OK"
                           + " · 离开键 " + (leave != null ? "OK" : "缺图"));
            return canvasGo;
        }

        /// <summary>背景按 cover 铺（与商店 / 强化 / 女巫同一套算法）。</summary>
        private static void BuildBackground(Transform parent, Sprite bg, StringBuilder log)
        {
            if (bg == null)
            {
                return;
            }

            GameObject go = NewUi("Bg_Treasure", parent);
            var img = go.AddComponent<Image>();
            img.sprite = bg;
            img.raycastTarget = false;
            img.preserveAspect = true;

            Vector2 size = UiLayout.TreasureBgCoverSize();
            Box(Rt(go), Mid, Mid, Vector2.zero, size);
            log.AppendLine("  背景 cover " + size.x.ToString("0.#") + "×" + size.y.ToString("0.#")
                           + "（放大 " + (size.y / UiLayout.TreasureBgNativeHeight).ToString("0.####") + "×）");
        }

        /// <summary>
        /// <b>箱子</b>：整个场景里唯一吃射线的东西（与女巫工坊那个水晶球同一个位置）。
        ///
        /// <para>结构与石台的命中区同源：一枚 <c>Button</c> + 一张 <c>Image</c>，
        /// <b>按钮的 targetGraphic 就是箱子的图本身</b>（不再单开一块透明 Hit —— 箱子是矩形
        /// 命中区里最好点的形状，单开一层只会多一个要同步尺寸的地方）。</para>
        ///
        /// <para><b>⚠ 锚点 / pivot 都是底边中心</b>：<c>anchorMin/Max = (0.5, 0)</c>、
        /// <c>pivot = (0.5, 0)</c>、<c>anchoredPosition.y = UiLayout.TreasureChestGroundY</c>。
        /// 于是「换图变高」只往上长，箱底不会跳离地面（尺寸由
        /// <c>TreasureView.ApplyChestSize</c> 在运行时按图算）。</para>
        ///
        /// <para>⚠ <c>preserveAspect</c> <b>必须关掉</b>：矩形尺寸本来就要按图算，
        /// 再让 Image 自己等比缩放会让命中区与画面各说各话。</para>
        /// </summary>
        private static Button BuildChest(Transform parent, Sprite closed, StringBuilder log)
        {
            GameObject go = NewUi("Chest", parent);

            float width = UiLayout.TreasureChestWidth;
            float height = closed != null && closed.rect.width > 0f
                ? width * (closed.rect.height / closed.rect.width)
                : width * 0.81f;

            Box(Rt(go), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiLayout.TreasureChestGroundY), new Vector2(width, height));

            var img = go.AddComponent<Image>();
            img.sprite = closed;
            img.color = closed != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            img.preserveAspect = false;

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            // 底图自带描边与高光 → 颜色过渡会把它刷成单色（商店 / 石台 / 女巫那几处已定过这条口径）。
            button.transition = Selectable.Transition.None;

            log.AppendLine("  箱子：" + width.ToString("0.#") + "×" + height.ToString("0.#")
                           + " @ 底边 y=" + UiLayout.TreasureChestGroundY.ToString("0.#")
                           + "（开箱后自动换成高 "
                           + (width * (835f / 831f)).ToString("0.#") + " 那一张）");
            return button;
        }

        /// <summary>
        /// <b>开箱读数</b>（默认失活）：一行大号「+50」+ 底下一行小字「金币」。
        ///
        /// <para>分两行是因为中文字号与数字对不上：数字要 96 才够"弹"，同一行里放「金币」
        /// 两个字会被撑得很挤。颜色走 <c>UiTheme.ResultWinCoin</c>（战斗胜利结算里那枚金币的金）。</para>
        /// </summary>
        private static GameObject BuildGain(Transform parent, TMP_FontAsset body, TMP_FontAsset title,
            StringBuilder log)
        {
            GameObject root = NewUi("GainRoot", parent);
            Box(Rt(root), Mid, Mid,
                new Vector2(0f, UiLayout.TreasureGainCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.TreasureGainWidth, UiLayout.TreasureGainHeight));

            GameObject amountGo = NewUi("Gain", root.transform);
            Stretch(Rt(amountGo));
            AddText(amountGo, title, UiLayout.FontSizeTreasureGain,
                UiTheme.ResultWinCoin, TextAlignmentOptions.Center, "+50");

            GameObject captionGo = NewUi("GainCaption", root.transform);
            Box(Rt(captionGo), Mid, Mid, new Vector2(0f, GainCaptionOffsetY),
                new Vector2(UiLayout.TreasureGainWidth, UiLayout.TreasureHintHeight));
            AddText(captionGo, body, UiLayout.FontSizeTreasureHint,
                UiTheme.UpgradeHintText, TextAlignmentOptions.Center, "金币");

            root.SetActive(false);
            log.AppendLine("  开箱读数：+N（" + UiLayout.FontSizeTreasureGain.ToString("0") + "pt，"
                           + "压 y=" + UiLayout.TreasureGainCenterY.ToString("0.#") + "）默认失活");
            return root;
        }

        /// <summary>「离开」按钮（右下角；底图自带「离开」两个字，只留一个空 Label 备用）。</summary>
        private static Button BuildLeaveButton(Transform parent, TMP_FontAsset body, Sprite sprite,
            out TMP_Text label)
        {
            GameObject go = NewUi("LeaveButton", parent);
            Box(Rt(go), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.TreasureLeaveRight, UiLayout.TreasureLeaveBottom),
                new Vector2(UiLayout.ShopLeaveWidth, UiLayout.ShopLeaveHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                // ⚠ Simple 而不是 Sliced —— 与商店 / 石台那两处同口径（底图自带木质高光与描边，
                //   九宫格 border 对不上会拉出接缝且零报错）。
                img.type = Image.Type.Simple;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo), 0f, 5f, 0f, 0f);       // 与商店 / 石台同值（用户手调过）
            label = AddText(labelGo, body, UiLayout.FontSizeShopLeave,
                UiTheme.ShopLeaveText, TextAlignmentOptions.Center, "");

            return button;
        }

        // ══════════════════════════════════════════════════════
        //  调试入口 / 场景杂项 / Build Settings
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 场景里的调试入口（<see cref="TreasureSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的<b>父级</b>（场景根）</b>：本构建器存 Prefab 时只保存 Canvas
        /// 这一棵子树，挂在场景根就天然不会进 Prefab —— 不需要「存完再删」那套容易漏的做法。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            TreasureSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<TreasureSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("TreasureSceneEntry");
            var entry = go.AddComponent<TreasureSceneEntry>();

            TreasureView view = canvasGo.GetComponent<TreasureView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  调试入口 TreasureSceneEntry（挂在场景根，不进 Prefab）");
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

            // 本工程用旧版 Input Manager（manifest 里没有 inputsystem）→ StandaloneInputModule。
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        /// <summary>
        /// 把场景登记进 Build Settings（与 P0 / P7 两个构建器逐字同源，顺序取自
        /// <see cref="MapRoutes.BuildOrder"/> —— 那张表是唯一一处）。
        ///
        /// <para><b>⚠ 这不是可选项</b>：<c>SceneManager.LoadScene("Treasure")</c> 这类按名字的调用，
        /// 场景不在 Build Settings 里就会当场抛错 —— 而它抛的时刻是<b>玩家点到那个节点的时候</b>，
        /// 前面所有验证都看不出来。</para>
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
                           + " 个）· 启动场景 = " + MapRoutes.BuildOrder[0]);
        }

        // ══════════════════════════════════════════════════════
        //  导入参数
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把一张图钉成「UI 用的 Sprite」（与 P7 地图构建器同一套值）。
        ///
        /// <para><b>为什么构建期要管这个</b>：<c>alphaIsTransparency</c> / <c>pixelsPerUnit</c> /
        /// <c>mipmap</c> 只活在 <c>.meta</c> 里，重导一次就丢、丢了<b>零报错</b> ——
        /// 表现只有「图边缘有一圈黑边」或者「图糊成一片」。而且 Python 直接写盘的图
        /// Unity 可能还没建过 <c>.meta</c>，这时候 <c>GetAtPath</c> 返回 null，得先
        /// <c>ImportAsset</c> 一次。</para>
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
        /// 每跑一次构建就把场景再新建一次 —— 手改的那些 Inspector 值全被抹掉。</para>
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

        private static Image AddImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
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
    }
}
