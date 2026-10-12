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
    /// <b>P9 · 休息事件场景</b>的构建器（2026-10-09）。
    ///
    /// <para>菜单：<c>魔法乱斗/P9 · 构建 Rest 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>⚠ 本场景是占位</b>：用户口径原话「该事件对应的场景暂时还没有做好，你先姑且用个
    /// 黑屏 + 按钮的方式替代一下」。所以这里<b>不铺任何背景美术</b> —— 整屏一块近黑底
    /// （<c>UiTheme.RestPlaceholderBackdrop</c>）+ 一个「休息」大键（暖金实心块，不是美术底图）
    /// + 标题 + 引导行 + 右下角「离开」（复用商店那张底图，与其它事件场景同一套边距）。
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>打开 / 新建 <c>Assets/Scenes/Rest.unity</c>；</item>
    /// <item>建一个 <c>RestCanvas</c>：黑底 + 标题 + 休息大键 + 引导行 + 离开键；</item>
    /// <item>把 <see cref="RestView"/> 的序列化字段一次性接好；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/RestCanvas.prefab</c>；</item>
    /// <item>场景根挂一个 <c>RestSceneEntry</c>（调试入口）—— <b>它不进 Prefab</b>；</item>
    /// <item>把 <c>Rest</c> 登记进 Build Settings（顺序 = <see cref="MapRoutes.BuildOrder"/>）。</item>
    /// </list>
    ///
    /// <para><b>⚠ 版式与商店 / 石台 / 女巫 / 宝箱逐条同源</b>：标题带 1002、引导行、
    /// 右下角「离开」（同一个 <c>Shop_LeaveButton</c> 底图与同一套边距 64 / 44）——
    /// 全部取自 <c>UiLayout.Rest*</c>，不在这里另算一份。</para>
    ///
    /// <para><b>⚠ 正式场景做好之后怎么接</b>：把背景图归位进 <c>Art/Backgrounds/</c>，
    /// 在本类的 <see cref="BuildCanvas"/> 里照 <c>TreasureUiBuilder.BuildBackground</c> 的
    /// cover 写法加一层背景即可 —— 上面那些版式常量一行都不用改。</para>
    /// </summary>
    public static class RestUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/Rest.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/RestCanvas.prefab";
        private const string CanvasName = "RestCanvas";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        [MenuItem("魔法乱斗/P9 · 构建 Rest 场景", false, 47)]
        private static void MenuEntry()
        {
            // ⚠ 故意不加 EditorUtility.DisplayDialog：带弹窗的菜单被脚本 / MCP 触发会挂住主线程。
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Rest] ==== 开始 ====");

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

            // 「离开」键那张底图（Python 直接写盘的图 Unity 可能还没建过 .meta）。
            EnsureSpriteSettings(UiLayout.ShopLeaveSpritePath, log);
            Sprite leave = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopLeaveSpritePath);
            Missing(log, leave, "「离开」按钮底图", UiLayout.ShopLeaveSpritePath);

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
            GameObject canvasGo = BuildCanvas(body, title, leave, log);
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
                log.AppendLine("  调试入口：场景根下的 " + debugEntry.name);
            }

            log.AppendLine("[Rest] ==== 完成 ====");
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

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title, Sprite leave,
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

            // ① 整屏黑底（占位 —— 本场景没有背景美术）
            GameObject backdrop = NewUi("Backdrop", canvasGo.transform);
            AddImage(backdrop, UiTheme.RestPlaceholderBackdrop);
            Stretch(Rt(backdrop));

            // ② 「休息」大键（本场景唯一要做事的按钮）
            Button restButton = BuildRestButton(canvasGo.transform, body, out TMP_Text restLabel,
                log);

            // ③ 标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), Mid, Mid,
                new Vector2(0f, UiLayout.RestTitleCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.RestTitleWidth, UiLayout.RestTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeRestTitle,
                UiTheme.RestTitleText, TextAlignmentOptions.Center, "休息");

            // ④ 引导行
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rt(hintGo), Mid, Mid,
                new Vector2(0f, UiLayout.RestHintCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.RestHintWidth, UiLayout.RestHintHeight));
            TMP_Text hintText = AddText(hintGo, body, UiLayout.FontSizeRestHint,
                UiTheme.RestHintText, TextAlignmentOptions.Center, "点击休息，恢复全部生命");

            // ⑤ 离开键
            Button leaveButton = BuildLeaveButton(canvasGo.transform, body, leave, out TMP_Text leaveLabel);

            // ── 接线 ─────────────────────────────────────────────
            var view = canvasGo.AddComponent<RestView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_restButton", restButton);
            SetRef(serialized, "_restLabel", restLabel);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_hintRoot", hintGo);
            SetRef(serialized, "_hint", hintText);
            SetRef(serialized, "_leaveButton", leaveButton);
            SetRef(serialized, "_leaveLabel", leaveLabel);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  Canvas OK：黑底（占位）· 休息大键 OK · 标题 OK · 引导行 OK"
                           + " · 离开键 " + (leave != null ? "OK" : "缺图"));
            return canvasGo;
        }

        /// <summary>
        /// <b>「休息」大键</b>：一块暖金实心矩形 + 一行近黑文字。
        ///
        /// <para><b>⚠ 故意不用美术底图</b>：占位阶段不该再为「一个黑屏上的按钮」增加一张素材
        /// —— 用户要的是「黑屏加按钮」。实心色块压在近黑底上对比最强，读起来就是「这里能点」。</para>
        ///
        /// <para>与宝箱 / 女巫那些键同一套结构：一枚 <c>Button</c> + 一张 <c>Image</c>，
        /// <b>按钮的 targetGraphic 就是那块色块本身</b>（不再单开一块透明 Hit）。</para>
        /// </summary>
        private static Button BuildRestButton(Transform parent, TMP_FontAsset font,
            out TMP_Text label, StringBuilder log)
        {
            GameObject go = NewUi("RestButton", parent);
            Box(Rt(go), Mid, Mid,
                new Vector2(0f, UiLayout.RestButtonCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.RestButtonWidth, UiLayout.RestButtonHeight));

            var img = go.AddComponent<Image>();
            img.color = UiTheme.RestButtonPlate;
            img.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            // 没有美术底图、也没有颜色过渡 —— 悬停/按下不该把暖金块刷成别的颜色。
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            label = AddText(labelGo, font, UiLayout.FontSizeRestButton,
                UiTheme.RestButtonText, TextAlignmentOptions.Center, "休息");

            log.AppendLine("  休息大键：" + UiLayout.RestButtonWidth.ToString("0.#") + "×"
                           + UiLayout.RestButtonHeight.ToString("0.#")
                           + " @ 中心 y=" + UiLayout.RestButtonCenterY.ToString("0.#")
                           + "（暖金实心块，占位）");
            return button;
        }

        /// <summary>「离开」按钮（右下角；底图自带「离开」两个字，只留一个空 Label 备用）。</summary>
        private static Button BuildLeaveButton(Transform parent, TMP_FontAsset body, Sprite sprite,
            out TMP_Text label)
        {
            GameObject go = NewUi("LeaveButton", parent);
            Box(Rt(go), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.RestLeaveRight, UiLayout.RestLeaveBottom),
                new Vector2(UiLayout.ShopLeaveWidth, UiLayout.ShopLeaveHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                // ⚠ Simple 而不是 Sliced —— 与商店 / 石台 / 宝箱那几处同口径（底图自带木质高光
                //   与描边，九宫格 border 对不上会拉出接缝且零报错）。
                img.type = Image.Type.Simple;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo), 0f, 5f, 0f, 0f);       // 与商店 / 石台 / 宝箱同值（用户手调过）
            label = AddText(labelGo, body, UiLayout.FontSizeShopLeave,
                UiTheme.RestHintText, TextAlignmentOptions.Center, "");

            return button;
        }

        // ══════════════════════════════════════════════════════
        //  调试入口 / 场景杂项 / Build Settings
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 场景里的调试入口（<see cref="RestSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的<b>父级</b>（场景根）</b>：本构建器存 Prefab 时只保存 Canvas
        /// 这一棵子树，挂在场景根就天然不会进 Prefab —— 不需要「存完再删」那套容易漏的做法。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            RestSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<RestSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("RestSceneEntry");
            var entry = go.AddComponent<RestSceneEntry>();

            RestView view = canvasGo.GetComponent<RestView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  调试入口 RestSceneEntry（挂在场景根，不进 Prefab）");
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
            cam.backgroundColor = UiTheme.RestPlaceholderBackdrop;
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
        /// 把场景登记进 Build Settings（与 P0 / P7 / P8 三个构建器逐字同源，顺序取自
        /// <see cref="MapRoutes.BuildOrder"/> —— 那张表是唯一一处）。
        ///
        /// <para><b>⚠ 这不是可选项</b>：<c>SceneManager.LoadScene("Rest")</c> 这类按名字的调用，
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
        /// 把一张图钉成「UI 用的 Sprite」（与 P7 / P8 同一套值）。
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
        /// <c>assetPath</c> 本身以 <c>Assets/</c> 开头，工程根就是它的上一级。</para>
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
