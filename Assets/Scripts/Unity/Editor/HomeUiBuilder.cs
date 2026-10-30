using System;
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
    /// <b>P0 · 游戏开始场景</b>的构建器（2026-10-05）。
    ///
    /// <para>菜单：<c>魔法乱斗/P0 · 构建 Home 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>打开 / 新建 <c>Assets/Scenes/Home.unity</c>；</item>
    /// <item>建一个 <c>HomeCanvas</c>：整屏底 + 大标题 + 副标题 + <b>5 行存档</b>（建满、运行时只显示有存档的）
    /// + <b>新建存档 / 删除存档</b> + 「进入游戏」+ 一行提示；</item>
    /// <item>把 <see cref="HomeView"/> 的序列化字段一次性接好（含 5 行的按钮 / 文本 / 选中描边数组，
    /// 以及新建 / 删除两枚键）；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/HomeCanvas.prefab</c>；</item>
    /// <item>场景根挂一个 <c>HomeSceneEntry</c>（入口）—— <b>它不进 Prefab</b>；</item>
    /// <item>把六个场景登记进 Build Settings（<b>Home 排第一</b> = 正式构建的启动场景）。</item>
    /// </list>
    ///
    /// <para><b>⚠ 「存档有哪几个」不在这个构建器里写死</b>：行数按
    /// <c>UiLayout.HomeSlotRows</c>（= <c>SaveSlots.PlayerSlots</c> 的长度）建满，
    /// 运行时由入口按<b>磁盘上真的有哪几个存档</b>决定显示前几行。
    /// 这里只负责「摆够几行」，每一行显示什么由入口在运行时读存档决定 ——
    /// 构建器写死文案的话，构建一次就再也不更新了。</para>
    ///
    /// <para><b>⚠ 选中态用 <c>Outline</c> 而不是改底色</b>：铁律 3（颜色只写 prefab
    /// <c>Image.color</c>，运行时只切 active / enabled）。改底色的话那个颜色进不了 Prefab。</para>
    /// </summary>
    public static class HomeUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/Home.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/HomeCanvas.prefab";
        private const string CanvasName = "HomeCanvas";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>「进入游戏」按钮的底图 —— 与各处确认键同一张（本工程按钮底图只有这一套）。</summary>
        private const string EnterButtonSpritePath = "Assets/Art/Ui/HandPick_Confirm.png";

        /// <summary>「删除存档」的底图 —— 用「离开」那一张（木色、带警示感；也是设置面板里「退出」用的）。</summary>
        private const string DeleteButtonSpritePath = "Assets/Art/Ui/Shop_LeaveButton.png";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        // ── 菜单入口 ─────────────────────────────────────────────────

        [MenuItem("魔法乱斗/P0 · 构建 Home 场景", false, 40)]
        private static void MenuEntry()
        {
            // ⚠ 故意不加 EditorUtility.DisplayDialog：带弹窗的菜单被脚本 / MCP 触发会挂住主线程。
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Home] ==== 开始 ====");

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

            EnsureSpriteSettings(EnterButtonSpritePath, log);
            Sprite enterSprite = AssetDatabase.LoadAssetAtPath<Sprite>(EnterButtonSpritePath);
            Missing(log, enterSprite, "「进入游戏」按钮底图", EnterButtonSpritePath);

            // 1) 场景
            EditorSceneManager.SaveOpenScenes();
            bool created = !System.IO.File.Exists(AbsolutePath(ScenePath));
            Scene scene = created
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
            GameObject canvasGo = BuildCanvas(body, title, enterSprite, log);
            EnsureEventSystem();
            EnsureCamera(log);

            // 4) 存 Prefab（先建入口：它挂在场景根，天然不进 Canvas 那棵子树）
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
                log.AppendLine("  入口：场景根下的 " + entry.name);
            }

            log.AppendLine("[Home] ==== 完成 ====");
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

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title, Sprite enterSprite,
            StringBuilder log)
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
            Stretch(Rt(bgGo));
            var bg = bgGo.AddComponent<Image>();
            bg.color = UiTheme.Backdrop;
            bg.raycastTarget = false;

            // ② 大标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.HomeTitleY),
                new Vector2(1500f, 150f));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeHomeTitle, UiTheme.TextPrimary,
                TextAlignmentOptions.Center, "魔法乱斗");

            // ③ 副标题
            GameObject subGo = NewUi("Subtitle", canvasGo.transform);
            Box(Rt(subGo), Mid, Mid, new Vector2(0f, UiLayout.HomeSubtitleY),
                new Vector2(1200f, 60f));
            TMP_Text subtitle = AddText(subGo, body, UiLayout.FontSizeHomeSubtitle, UiTheme.TextSecondary,
                TextAlignmentOptions.Center, "选择玩法");

            // ④ 两枚玩法键（2026-10-28 · 大改：存档行 / 新建 / 删除全删）
            //     标准战斗 = 双方各随机 8 张；自定义战斗 = 去牌组编辑界面挑牌。
            const float buttonWidth = 440f;
            const float buttonHeight = 156f;
            const float buttonPitch = 520f;
            const float buttonY = -60f;

            Button standardButton = BuildModeButton(canvasGo.transform, "StandardButton", "标准战斗",
                new Vector2(-buttonPitch * 0.5f, buttonY), new Vector2(buttonWidth, buttonHeight),
                enterSprite, title);

            Button customButton = BuildModeButton(canvasGo.transform, "CustomButton", "自定义战斗",
                new Vector2(buttonPitch * 0.5f, buttonY), new Vector2(buttonWidth, buttonHeight),
                enterSprite, title);

            // ⑤ 底部提示
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rt(hintGo), Mid, Mid, new Vector2(0f, UiLayout.HomeHintY),
                new Vector2(UiLayout.HomeHintWidth, UiLayout.HomeHintHeight));
            TMP_Text hint = AddText(hintGo, body, UiLayout.FontSizeHomeHint, UiTheme.TextSecondary,
                TextAlignmentOptions.Center, string.Empty);

            // 接线
            var view = canvasGo.AddComponent<HomeView>();
            view.Configure(titleText, subtitle, standardButton, customButton, hint);

            log.AppendLine("  已建 " + CanvasName + "（标准战斗 + 自定义战斗）");
            return canvasGo;
        }

        /// <summary>
        /// 建一枚主菜单大键（底图 + 居中文字）。
        ///
        /// <para><b>⚠ 底图用 <c>Simple</c> 而不是九宫格</b>：本工程那几张按钮底图自带木质高光与
        /// 描边，九宫格与源图 border 对不上会拉出接缝 —— 与设置面板「退出到主菜单」、
        /// 商店「离开」同一口径。</para>
        /// </summary>
        private static Button BuildModeButton(Transform parent, string name, string label,
            Vector2 position, Vector2 size, Sprite sprite, TMP_FontAsset titleFont)
        {
            GameObject go = NewUi(name, parent);
            Box(Rt(go), Mid, Mid, position, size);

            var image = go.AddComponent<Image>();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
            }

            image.color = sprite != null ? Color.white : UiTheme.Panel;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            AddText(labelGo, titleFont, UiLayout.FontSizeHomeEnter, UiTheme.TextPrimary,
                TextAlignmentOptions.Center, label);

            return button;
        }

        /// <summary>
        /// 场景里的入口（<see cref="HomeSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的父级（场景根）</b>：本构建器存 Prefab 时只保存 Canvas
        /// 这一棵子树，挂在场景根就天然不会进 Prefab —— 不需要「存完再删」那套容易漏的做法。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            HomeSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<HomeSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("HomeSceneEntry");
            var entry = go.AddComponent<HomeSceneEntry>();

            HomeView view = canvasGo.GetComponent<HomeView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  入口 HomeSceneEntry（挂在场景根，不进 Prefab）");
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
        /// 把六个场景登记进 Build Settings，<b>Home 排第一</b>。
        ///
        /// <para><b>⚠ 这不是可选项</b>：<c>SceneManager.LoadScene("Map")</c> 这类按名字的调用，
        /// 场景不在 Build Settings 里就会当场抛错 —— 而它抛的时刻是<b>玩家点了按键的时候</b>，
        /// 前面所有验证都看不出来。战斗失败界面的「退出」现在会切到 <c>Home</c>，
        /// 所以 Home 必须在这里。</para>
        ///
        /// <para>顺序 = <see cref="SceneRoutes.BuildOrder"/>；<b>排第一的那个就是正式构建的启动场景</b>，
        /// 所以 Home 在最前 = 打开游戏从开始界面进。</para>
        /// </summary>
        private static void EnsureBuildSettings(StringBuilder log)
        {
            List<string> want = SceneRoutes.BuildOrderPaths();

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
                           + " 个）· 启动场景 = " + SceneRoutes.BuildOrder[0]);
        }

        // ══════════════════════════════════════════════════════
        //  导入参数
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把一张图钉成「UI 用的 Sprite」。
        ///
        /// <para><b>为什么构建期要管这个</b>：<c>alphaIsTransparency</c> / <c>pixelsPerUnit</c> /
        /// <c>mipmap</c> 只活在 <c>.meta</c> 里，重导一次就丢、丢了<b>零报错</b>
        /// —— 表现只有「图边缘有一圈黑边」或者「图糊成一片」。</para>
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
        /// 写成「工程根 + 摘掉 <c>Assets/</c> 的路径」会恒判「场景不存在」，
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
