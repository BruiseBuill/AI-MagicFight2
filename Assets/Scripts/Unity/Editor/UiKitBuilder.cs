using System.Collections.Generic;
using System.IO;
using MagicBrawl.App;
using MagicBrawl.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// M7 UiKit 的构建器：程序化生成 Canvas 层级与 Prefab。
    ///
    /// <para>为什么用代码生成而不是手摆 Hierarchy：UI 节点上百个，手写 `.prefab` / `.unity` 的 YAML
    /// 既易错又不可复现。生成器是幂等的 —— 每次重跑都重建出同样的结构，改版式只需要改
    /// <see cref="UiLayout"/> 再跑一次菜单。</para>
    ///
    /// 菜单：`魔法乱斗/M7 · 构建 UiKit（Canvas + Prefab）`
    /// </summary>
    public static class UiKitBuilder
    {
        private const string PrefabDir = "Assets/Prefabs/Ui";
        private const string ResourcesDir = "Assets/Resources";

        /// <summary>成品卡面（760×1056，卡名 / 数值 / 文字全烘焙在图上）。</summary>
        private const string ArtDir = "Assets/Art/Cards";

        /// <summary>插画原图（784×1168，无框无字）—— M16 组成式卡面的底图。</summary>
        private const string IllustrationDir = "Assets/Art/CardArt";

        private const string CanvasName = "BattleCanvas";

        // ── M16 组成式卡面用到的零件 ─────────────────────────
        /// <summary>圆角矩形九宫格底图（填充 / 描边），由 `Tools/art-audit/build_card_box.py` 生成。</summary>
        private const string CardBoxPath = "Assets/Art/Ui/CardBox.png";
        private const string CardBoxLinePath = "Assets/Art/Ui/CardBox_Line.png";

        /// <summary>力量徽标的星爆 / 冷却徽标的圆章（沿用 MagicCardKit 示例卡里的那两张）。</summary>
        private const string BurstPath = "Assets/Art/Fx/Fx_Explosion_Light.png";
        private const string CoolDiscPath = "Assets/Art/Fx/icon_no_number_ns_256.png";

        /// <summary>
        /// 九宫格 border（**精灵像素**）。
        ///
        /// <para>必须 ≥ 圆角半径 + 内缩，否则圆角会被拉伸成椭圆 —— 这个错是**安静**的，
        /// Unity 不报任何警告。数值必须与 `build_card_box.py` 的 `BORDER` 一致，
        /// 脚本跑完会把它打印出来。</para>
        /// </summary>
        private const int CardBoxBorder = 38;

        /// <summary>九宫格图的 PPU：取 100（= 画布 referencePixelsPerUnit）→ 精灵像素与 UI 单位 1:1。</summary>
        private const float CardBoxPixelsPerUnit = 100f;

        // 字体（选型见 `Docs/engineering/06-美术与字体规范.md` §3.4）
        private const string BodyFontPath = "Assets/Font/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Font/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";
        private const string AccentFontPath = "Assets/Font/English-Handwriting-Italy/ZCOOL Addict Italic 02 SDF.asset";

        /// <summary>
        /// 卡面「力量 / 冷却」两个数字专用的字体（M35，2026-09-23）。
        ///
        /// <para><b>用户指定</b>：<c>Power/Text (TMP)</c> 与 <c>CD/Text (TMP)</c> 用
        /// <b>千图马克手写体</b> —— 手写笔画更粗、更像「牌面上的记号」，
        /// 比原来跟卡名共用的标题体更适合这两个大数字。</para>
        ///
        /// <para><b>为什么必须写进构建器</b>：用户是先在 <c>CardView_Hand.prefab</c> 上
        /// 手工改好的（14:51 那份），而 Prefab 的字体是构建器重跑时会整体覆盖的东西 ——
        /// 不固化在这里，哪天跑一次 M7 就会把这两个数字悄悄换回标题体，而且<b>零报错</b>。
        /// 这边固化之后，「手工改的」与「重建出来的」永远是同一个口径。</para>
        ///
        /// <para>⚠ <b>改动前先跟用户确认</b> —— 这是他在意的两处细节之一（另一处是卡名的自动缩放）。</para>
        /// </summary>
        private const string CardNumberFontPath = "Assets/Font/HandWriting/千图马克手写体 SDF.asset";

        // ══════════════════════════════════════════════════════
        //  菜单入口
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/M7 · 构建 UiKit（Canvas + Prefab）", priority = 1)]
        public static void BuildUiKit()
        {
            TMP_FontAsset body = LoadFont(BodyFontPath);
            TMP_FontAsset title = LoadFont(TitleFontPath);

            // ⚠ 这里刻意不弹 Dialog：本菜单经常被 MCP / 脚本化调用，
            //   编辑器里一旦弹出模态框，主线程就停在那儿 —— 后续所有自动化调用全部超时。
            if (body == null || title == null)
            {
                Debug.LogError("[UiKit] 缺少 TMP 字体资产，构建中止。请确认：\n"
                               + BodyFontPath + "\n" + TitleFontPath);
                return;
            }

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabDir);
            EnsureFolder(ResourcesDir);

            // 先把 TMP 默认字体与 fallback 配好，否则新建 TMP 文本会刷一堆「no default font」警告
            ConfigureTmpFonts();

            // 卡框九宫格图的 border / PPU 是手写不进 .meta 的运行时口径，每次构建都对一遍
            EnsureCardBoxImporters();

            RebuildCardArtLibraryInternal();

            // ── 卡面只有一份（2026-09-25 用户口径：「所有卡面显示统一」）──────────
            //  手牌 / 冷却迷你卡 / 放大查看 / 出牌演出**共用同一个 Prefab**，尺寸差别
            //  由 `CardView.SetFaceWidth` 在运行时给（它改 CardRoot 的 localScale）。
            //
            //  ⚠ 不再产出 `CardView_Mini.prefab`：它存在的唯一原因是「构建期把卡宽算进了
            //    CardRoot 的 localScale」—— 于是用户在手牌 Prefab 上手工调好的卡名样式
            //    永远传不到迷你卡上。那个限制已经解除，第二份 Prefab 只会制造下一次不一致。
            CardView handPrefab = BuildHandCardPrefab(body, title);

            GameObject canvas = BuildCanvasInActiveScene(handPrefab, handPrefab);

            PrefabUtility.SaveAsPrefabAssetAndConnect(
                canvas, PrefabDir + "/" + CanvasName + ".prefab", InteractionMode.AutomatedAction);

            EditorSceneManager.MarkSceneDirty(canvas.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = canvas;
            EditorGUIUtility.PingObject(canvas);

            Debug.Log("[UiKit] 构建完成：\n"
                      + "  · Prefab  " + PrefabDir + "/" + CanvasName + ".prefab\n"
                      + "  · Prefab  " + PrefabDir + "/CardView_Hand.prefab（**唯一的卡面**：手牌 / 冷却迷你卡 / 放大 / 演出共用）\n"
                      + "  · 美术映射 " + ResourcesDir + "/" + CardArtLibrary.ResourcePath + ".asset\n"
                      + "  版式数值全部在 UiLayout.cs，改完重跑本菜单即可重建。");
        }

        /// <summary>只重建卡面映射表（美术改名 / 补图后单独跑）。</summary>
        [MenuItem("魔法乱斗/重建卡面映射表（CardArtLibrary）", priority = 20)]
        public static void RebuildCardArtLibrary()
        {
            EnsureFolder(ResourcesDir);
            RebuildCardArtLibraryInternal();
            AssetDatabase.SaveAssets();
        }

        // ══════════════════════════════════════════════════════
        //  TMP 字体设置（补 `06-美术与字体规范.md` §3.5 的待办）
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/配置 TMP 默认字体与 fallback", priority = 21)]
        public static void ConfigureTmpFonts()
        {
            TMP_FontAsset body = LoadFont(BodyFontPath);
            TMP_FontAsset title = LoadFont(TitleFontPath);
            TMP_FontAsset accent = LoadFont(AccentFontPath);

            if (body == null)
            {
                Debug.LogError("[UiKit] 找不到正文字体：" + BodyFontPath);
                return;
            }

            var log = new List<string>();

            // ① TMP Settings 的默认字体
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(
                "Assets/TextMesh Pro/Resources/TMP Settings.asset");

            if (settings == null)
            {
                log.Add("⚠ 找不到 TMP Settings，先跑一次 Window > TextMeshPro > Import TMP Essential Resources");
            }
            else
            {
                var so = new SerializedObject(settings);
                SerializedProperty prop = so.FindProperty("m_defaultFontAsset");
                if (prop != null)
                {
                    prop.objectReferenceValue = body;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    log.Add("✔ TMP Settings 默认字体 → " + Path.GetFileName(BodyFontPath));
                }
            }

            // ② 给缺字形的字体挂 fallback（仓耳渔阳体 / ZCOOL 都缺 α β γ ≤ ≥）
            log.Add(SetFallback(title, body, "站酷仓耳渔阳体 W03"));
            log.Add(SetFallback(accent, body, "ZCOOL Addict Italic 02"));

            AssetDatabase.SaveAssets();
            Debug.Log("[UiKit] TMP 字体配置：\n  " + string.Join("\n  ", log.ToArray()));
        }

        private static string SetFallback(TMP_FontAsset target, TMP_FontAsset fallback, string label)
        {
            if (target == null || fallback == null)
            {
                return "⚠ 跳过 " + label + "（资源缺失）";
            }

            var so = new SerializedObject(target);
            SerializedProperty list = so.FindProperty("m_FallbackFontAssetTable");

            if (list == null)
            {
                return "⚠ " + label + " 找不到 m_FallbackFontAssetTable";
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == fallback)
                {
                    return "· " + label + " 已挂着该 fallback，跳过";
                }
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fallback;
            so.ApplyModifiedPropertiesWithoutUndo();
            return "✔ " + label + " fallback → 正文字体";
        }

        // ══════════════════════════════════════════════════════
        //  卡面映射表
        // ══════════════════════════════════════════════════════

        private static void RebuildCardArtLibraryInternal()
        {
            string assetPath = ResourcesDir + "/" + CardArtLibrary.ResourcePath + ".asset";
            var lib = AssetDatabase.LoadAssetAtPath<CardArtLibrary>(assetPath);

            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<CardArtLibrary>();
                AssetDatabase.CreateAsset(lib, assetPath);
            }

            // 两份 guid 表各扫一次：40 张卡 × 两个目录，逐个 FindAssets 会白扫 80 遍
            string[] faceGuids = AssetDatabase.FindAssets("t:Sprite", new[] { ArtDir });
            string[] illustrationGuids = AssetDatabase.FindAssets("t:Sprite", new[] { IllustrationDir });

            var entries = new List<CardArtLibrary.Entry>();

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                Sprite face = FindCardSprite(faceGuids, def);
                Sprite illustration = FindCardSprite(illustrationGuids, def);

                entries.Add(new CardArtLibrary.Entry
                {
                    CardId = def.Id,
                    Art = face,
                    Illustration = illustration,
                });

                if (face == null)
                {
                    Debug.LogWarning("[UiKit] 卡面缺失：" + def.Id + " " + def.Name);
                }

                if (illustration == null)
                {
                    Debug.LogWarning("[UiKit] 插画原图缺失：" + def.Id + " " + def.Name);
                }
            }

            lib.SetEntries(entries);
            EditorUtility.SetDirty(lib);
        }

        /// <summary>
        /// 按命名规范 `Card_<两位序号>_<卡ID>_<卡名>.<ext>` 找图。
        ///
        /// <para>用「文件名里含 `_卡ID_`」匹配而不是拼完整文件名：`Art/CardArt/` 里
        /// `.png` 与 `.jpg` 混用（见 06-美术与字体规范 §2.6），拼扩展名会漏掉一半。</para>
        ///
        /// <para>显式跳过 `Draft_` 前缀 —— `Art/CardArt/_Draft/` 里那 9 张是「美术先行、
        /// 规则未定义」的草稿，命名里没有卡 ID，但万一将来出现同名前缀就会误配。</para>
        /// </summary>
        private static Sprite FindCardSprite(string[] guids, CardDef def)
        {
            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                string file = Path.GetFileName(path);

                if (file.StartsWith("Draft_") || !file.Contains("_" + def.Id + "_"))
                {
                    continue;
                }

                Sprite found = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // ══════════════════════════════════════════════════════
        //  组成式卡面（M16）
        // ══════════════════════════════════════════════════════
        //
        //  节点结构照 `ThirdParty/MagicCardKit/Prefabs/BigMagicCard.prefab`：
        //      BG / BGLIne · Texture（插画）· Power · CD · Name · Content · EdgeLine
        //
        //  与那个参考 Prefab 的三点不同（都是必须改的，理由写在各自位置）：
        //   ① 它是 3D 展示卡（Canvas.renderMode = WorldSpace，根节点连 RectTransform 都没有），
        //      手牌是 UGUI —— 直接嵌进来整张卡会渲染成纯白，实测过；
        //   ② 它的圆角是 ShaderGraph（RoundBox/RoundBoxLine），不吃 UGUI 的顶点色与精灵贴图，
        //      换成九宫格圆角图（Tools/art-audit/build_card_box.py 生成）；
        //   ③ 它的文字框原本挂在卡外、设计成「可以抽出来」，手牌只有 196 px 宽，必须收回卡内。

        /// <summary>组成式卡面的零件引用（构建时收集，回填给 <see cref="CardView"/>）。</summary>
        private sealed class CardParts
        {
            public GameObject CardRoot;
            public Image Art;
            public GameObject Dim;
            public TMP_Text Name;
            public TMP_Text Power;
            public TMP_Text Cool;
            public TMP_Text Effect;
        }

        /// <summary>
        /// 在一棵**已经存在**的节点上挂一棵组成式卡面，并回填 <see cref="CardView"/>
        /// 的全部序列化引用（M24 #3：「放大查看」也要用与手牌同款的卡面）。
        ///
        /// <para>和 <see cref="BuildCardPrefab"/> 的差别只有两处，都是刻意的：</para>
        /// <list type="bullet">
        /// <item>不产 Prefab、不挂 <c>Button</c> / <c>CardInteractor</c> / <c>HandCardState</c>
        /// —— 看牌浮层是纯展示，点它、拖它都没有意义；</item>
        /// <item><c>Glow</c> / <c>CanvasGroup</c> / <c>LayoutElement</c> 也不挂 —— 浮层不在布局组里，
        /// 也不需要「点选上浮」那套交互态。省掉它们，<see cref="CardView"/> 里对应的
        /// <c>null</c> 检查本来就已经全部写好了。</item>
        /// </list>
        ///
        /// <para><b>为什么必须是「和手牌同一段构建代码」</b>：用户红线是「三处卡面样式统一」，
        /// 而统一下来最省事也最不容易走样的做法，就是让三处**物理上共用同一棵节点树**。
        /// 只要这里改成另写一套版式，将来改 UiLayout 就会又变成「改一处漏一处」。</para>
        /// </summary>
        public static CardView AddCardSurfaceTo(GameObject host, float cardWidth, float cardHeight,
            TMP_FontAsset body, TMP_FontAsset title)
        {
            RectTransform rt = Rt(host);

            CardParts parts = BuildCardSurface(rt, cardWidth, cardHeight, body, title);

            var view = host.AddComponent<CardView>();
            var so = new SerializedObject(view);
            so.FindProperty("_art").objectReferenceValue = parts.Art;
            so.FindProperty("_name").objectReferenceValue = parts.Name;
            so.FindProperty("_power").objectReferenceValue = parts.Power;
            so.FindProperty("_cool").objectReferenceValue = parts.Cool;
            so.FindProperty("_effect").objectReferenceValue = parts.Effect;
            so.FindProperty("_dim").objectReferenceValue = parts.Dim;
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        private static CardView BuildHandCardPrefab(TMP_FontAsset body, TMP_FontAsset title)
        {
            return BuildCardPrefab("CardView_Hand", UiLayout.HandCardWidth, UiLayout.HandCardHeight,
                body, title, true);
        }

        // ⚠ `BuildMiniCardPrefab` 已于 2026-09-25 删除 —— 卡面只剩一份（见 BuildUiKit 里的说明）。
        //   想再要一个尺寸，用 `CardView.SetFaceWidth(宽)`，不要再加第二份 Prefab。

        /// <summary>
        /// 造一张卡：外层是「布局占位 + 交互」，内层是一棵 622×872 设计空间的组成式卡面。
        ///
        /// <para>两种卡只差尺寸，所以共用这一个方法 —— 版式写在 <see cref="UiLayout"/> 的
        /// 设计空间常量里，缩放只加在 <c>CardRoot</c> 这一个节点的 localScale 上。</para>
        ///
        /// <para><paramref name="withHandState"/> 只有手牌为真：它给手牌扇面排布用的
        /// <see cref="HandCardState"/>（冷却区的迷你卡是静态格子，不需要）。</para>
        /// </summary>
        private static CardView BuildCardPrefab(string fileName, float cardWidth, float cardHeight,
            TMP_FontAsset body, TMP_FontAsset title, bool withHandState)
        {
            GameObject root = NewUi(fileName, null);
            RectTransform rt = Rt(root);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(cardWidth, cardHeight);

            // 放在布局组里时按这个尺寸占位；localScale 由 M8 做「点选上浮」时改，不影响布局
            var le = root.AddComponent<LayoutElement>();
            le.preferredWidth = cardWidth;
            le.preferredHeight = cardHeight;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            root.AddComponent<CanvasGroup>();

            CardParts parts = BuildCardSurface(rt, cardWidth, cardHeight, body, title);

            GameObject glow = MakeFrame(root.transform, 3f, UiTheme.SelectionGlow);
            glow.name = "Glow";

            var view = root.AddComponent<CardView>();
            var so = new SerializedObject(view);
            so.FindProperty("_art").objectReferenceValue = parts.Art;
            so.FindProperty("_name").objectReferenceValue = parts.Name;
            so.FindProperty("_power").objectReferenceValue = parts.Power;
            so.FindProperty("_cool").objectReferenceValue = parts.Cool;
            so.FindProperty("_effect").objectReferenceValue = parts.Effect;
            so.FindProperty("_glow").objectReferenceValue = glow;
            so.FindProperty("_dim").objectReferenceValue = parts.Dim;
            so.FindProperty("_group").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureButton(root, true);
            WireButton(view);
            glow.SetActive(false);

            // 交互组件随 Prefab 预置（2026-09-19）：原先由 HandView / CooldownView 在运行时
            // AddComponent —— 那样在 Prefab 上根本看不见这两个组件，也就没法调它的序列化值。
            // Host / Card / Draggable 是接口与引用字段，运行时由 CardInteractor.Attach 重指。
            root.AddComponent<CardInteractor>();
            if (withHandState)
            {
                root.AddComponent<HandCardState>();
            }

            return SavePrefab(root, fileName);
        }

        /// <summary>
        /// 建一棵组成式卡面（手牌 / 冷却迷你卡共用）。
        ///
        /// <para><b>整棵树的几何逐字照抄</b> `ThirdParty/MagicCardKit/Prefabs/BigMagicCard.prefab`
        /// —— 锚点 / pivot / anchoredPosition / 尺寸 / 字号全部取自它，逐条对应关系写在
        /// <see cref="UiLayout"/> 的 M16 段。两处必要偏差（圆角改用九宫格图、效果面板收进卡内）
        /// 的理由也写在那里。</para>
        ///
        /// <para>整卡的缩放只加在 <c>CardRoot</c> 一个节点上，所以同一段构建代码就能产出
        /// 手牌（196×272）与冷却迷你卡（137.8×193.7）两种尺寸。</para>
        /// </summary>
        private static CardParts BuildCardSurface(RectTransform cardRoot, float cardWidth, float cardHeight,
            TMP_FontAsset body, TMP_FontAsset title)
        {
            var parts = new CardParts();

            // M35：力量 / 冷却这两个数字用千图马克手写体（口径见 CardNumberFontPath）。
            // 找不到就退回卡名字体 —— 宁可字体不对，也不能因此让整张卡构建不出来。
            TMP_FontAsset cardNumber = LoadFont(CardNumberFontPath);
            if (cardNumber == null)
            {
                Debug.LogWarning("[UiKit] 找不到卡面数字字体：" + CardNumberFontPath
                                 + " —— 本次退回卡名字体，请检查资源是否还在。");
                cardNumber = title;
            }

            Sprite box = AssetDatabase.LoadAssetAtPath<Sprite>(CardBoxPath);
            Sprite boxLine = AssetDatabase.LoadAssetAtPath<Sprite>(CardBoxLinePath);
            Sprite burst = AssetDatabase.LoadAssetAtPath<Sprite>(BurstPath);
            Sprite coolDisc = AssetDatabase.LoadAssetAtPath<Sprite>(CoolDiscPath);

            // ── 设计空间容器：整卡的缩放只加在这一层 ──────────────
            GameObject space = NewUi("CardRoot", cardRoot);
            RectTransform spaceRt = Rt(space);
            Box(spaceRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.CardSpaceWidth, UiLayout.CardSpaceHeight));
            float scale = UiLayout.CardSpaceScale(cardWidth);
            spaceRt.localScale = new Vector3(scale, scale, 1f);
            parts.CardRoot = space;

            // ── ① 插画（prefab 的 `Texture`：锚点拉伸铺满整卡，圆角靠遮罩切）──
            GameObject windowGo = NewUi("Texture", space.transform);
            Stretch(Rt(windowGo));
            Image windowImg = AddCardBox(windowGo, box, Color.white);
            windowImg.raycastTarget = false;

            // showMaskGraphic = false：遮罩自己那张白图不画出来，只借它的 alpha 定形状
            var mask = windowGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject artGo = NewUi("Art", windowGo.transform);
            Stretch(Rt(artGo));
            parts.Art = AddImage(artGo, UiTheme.MiniCardFace);

            // ── ② 卡底压暗（prefab 的 `BG` 就是「盖在贴图上」的那层 box，顺序在插画之后）──
            GameObject bgGo = NewUi("BG", space.transform);
            Stretch(Rt(bgGo));
            AddCardBox(bgGo, box, UiTheme.CardFaceBackdrop);

            // ── ③ 力量徽标（prefab `Power`：越出卡左上角各 10 px）──────
            GameObject powerGo = NewUi("Power", space.transform);
            Box(Rt(powerGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.CardPowerOffsetX, UiLayout.CardPowerOffsetY),
                new Vector2(UiLayout.CardPowerSize, UiLayout.CardPowerSize));

            GameObject burstGo = NewUi("BG", powerGo.transform);
            Stretch(Rt(burstGo));
            Image burstImg = AddImage(burstGo, Color.white);
            burstImg.sprite = burst;
            burstImg.preserveAspect = true;

            GameObject powerTextGo = NewUi("Text (TMP)", powerGo.transform);
            Box(Rt(powerTextGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(UiLayout.CardPowerTextOffsetX, UiLayout.CardPowerTextOffsetY),
                new Vector2(UiLayout.CardPowerTextWidth, UiLayout.CardPowerTextHeight));

            // ⚠ 力量数字必须是**深色**：星爆中心是一团亮米白，白字会整块糊进去（照 prefab 用深灰）
            //    字体 = 千图马克手写体（M35，用户指定；见 CardNumberFontPath）
            parts.Power = AddText(powerTextGo, cardNumber, UiLayout.CardFontSizePower,
                UiTheme.CardPowerNumber, TextAlignmentOptions.Center);

            // ── ④ 冷却徽标（prefab `CD`：左侧、力量徽标**正下方**，不是右上角）──
            GameObject coolGo = NewUi("CD", space.transform);
            Box(Rt(coolGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.CardCoolOffsetX, UiLayout.CardCoolOffsetY),
                new Vector2(UiLayout.CardCoolSize, UiLayout.CardCoolSize));

            GameObject discGo = NewUi("Image", coolGo.transform);
            Stretch(Rt(discGo));
            Image discImg = AddImage(discGo, Color.white);
            discImg.sprite = coolDisc;
            discImg.preserveAspect = true;

            GameObject coolTextGo = NewUi("Text (TMP)", coolGo.transform);
            Box(Rt(coolTextGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(UiLayout.CardCoolTextOffsetX, UiLayout.CardCoolTextOffsetY),
                new Vector2(UiLayout.CardCoolTextSize, UiLayout.CardCoolTextSize));
            // 字体同力量数字 = 千图马克手写体（M35，用户指定；见 CardNumberFontPath）
            parts.Cool = AddText(coolTextGo, cardNumber, UiLayout.CardFontSizeCool,
                UiTheme.CardCoolNumber, TextAlignmentOptions.Center);

            // ── ⑤ 卡名（prefab `Name`：顶端、偏右 50.7 给徽标让位）─────
            GameObject nameGo = NewUi("Name", space.transform);
            Box(Rt(nameGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(UiLayout.CardNameOffsetX, UiLayout.CardNameOffsetY),
                new Vector2(UiLayout.CardNameWidth, UiLayout.CardNameHeight));

            // 偏差说明：prefab 的 Name 节点**没有衬底**，名字直接压在插画上。本作 40 张插画
            // 明暗差异很大（「寒流」几乎是白的），白字会读不出来 —— 所以在**同一块矩形**上
            // 补一层半透明深色衬底。位置与尺寸仍按 prefab，只是多一层底。
            AddCardBox(nameGo, box, UiTheme.CardNameBarBackdrop);

            GameObject nameTextGo = NewUi("Name", nameGo.transform);
            Stretch(Rt(nameTextGo), UiLayout.CardNamePad, 0f, UiLayout.CardNamePad, 0f);
            parts.Name = AddText(nameTextGo, title, UiLayout.CardFontSizeName,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);

            // prefab 的 Name 框一行只放得下 4.18 个字，而本作最长卡名正好 4 字 ——
            // 让它自己缩一点，别被截成「冰封…」（见 CardFontSizeNameMin 的注释）。
            parts.Name.enableAutoSizing = true;
            parts.Name.fontSizeMin = UiLayout.CardFontSizeNameMin;
            parts.Name.fontSizeMax = UiLayout.CardFontSizeName;
            parts.Name.overflowMode = TextOverflowModes.Ellipsis;

            // 卡面文字**一律加粗**（2026-09-25 用户在手牌 Prefab 上手工调的口径）。
            //
            // <para>为什么必须写进构建器（同 CardNumberFontPath 的理由）：粗体是用户
            // 在那份 Prefab 上改的，而 TMP 的 fontStyle 是构建器重跑时会整体覆盖的东西 ——
            // 不固化在这里，哪天跑一次 M7 就会默默变回常规体，而且零报错。</para>
            //
            // 只加粗「卡名 + 力量 + 冷却」三个数，**效果文字不加** —— 那一段本来就小，
            // 加粗会糊成一片（用户的改动里也没有它）。
            parts.Name.fontStyle = FontStyles.Bold;
            parts.Power.fontStyle = FontStyles.Bold;
            parts.Cool.fontStyle = FontStyles.Bold;

            // ── ⑥ 效果面板 + 效果文字（prefab `Content` 的两个子节点）──
            // 面板与文字是**兄弟**（prefab 里它们同属一个 `Content` 容器节点，这里把那层拉平了），
            // 所以面板不能再叫 "Content"，否则会出现两个同名兄弟。
            GameObject panelGo = NewUi("ContentPanel", space.transform);
            Box(Rt(panelGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, UiLayout.CardPanelCenterY),
                new Vector2(UiLayout.CardPanelWidth, UiLayout.CardPanelHeight));
            AddCardBox(panelGo, box, UiTheme.CardTextBoxBackdrop);

            GameObject panelLineGo = NewUi("BGLIne", panelGo.transform);
            Stretch(Rt(panelLineGo));
            AddCardBox(panelLineGo, boxLine, UiTheme.CardTextBoxEdge);

            GameObject effectGo = NewUi("Content", space.transform);
            Box(Rt(effectGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
                new Vector2(UiLayout.CardTextOffsetX, UiLayout.CardTextOffsetY),
                new Vector2(UiLayout.CardTextWidth, UiLayout.CardTextHeight));
            parts.Effect = AddText(effectGo, body, UiLayout.CardFontSizeEffect,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);

            // 效果文字是这一批里唯一「长度不可控」的内容：自动换行 + 装不下就整体缩到
            // CardFontSizeEffectMin，再装不下才截断。宁可字小一点也不要看不全。
            parts.Effect.enableWordWrapping = true;
            parts.Effect.enableAutoSizing = true;
            parts.Effect.fontSizeMin = UiLayout.CardFontSizeEffectMin;
            parts.Effect.fontSizeMax = UiLayout.CardFontSizeEffect;
            parts.Effect.overflowMode = TextOverflowModes.Truncate;

            // 图文混排：把 α / β / γ 换成剑 / 盾 / 感叹号
            TriggerSpriteLibrary.Attach(parts.Effect);

            // ── ⑦ 卡框描边（prefab 的 `EdgeLine` 是最后一个子节点 → 压在最上面）──
            GameObject edgeGo = NewUi("EdgeLine", space.transform);
            Stretch(Rt(edgeGo));
            AddCardBox(edgeGo, boxLine, UiTheme.CardFaceEdge);

            // ── ⑧ 压暗层（放在设计空间里，才跟着圆角走）─────────────
            GameObject dimGo = NewUi("Dim", space.transform);
            Stretch(Rt(dimGo));
            AddCardBox(dimGo, box, UiTheme.CannotPlayDim);
            dimGo.SetActive(false);
            parts.Dim = dimGo;

            return parts;
        }

        /// <summary>
        /// 九宫格圆角底（卡框 / 插画遮罩 / 卡名条 / 文字框都用它）。
        /// 图是 `Tools/art-audit/build_card_box.py` 生成的，导入参数见
        /// <see cref="ApplyCardBoxImporter"/> —— border 没设对的话圆角会拉成椭圆。
        /// </summary>
        private static Image AddCardBox(GameObject go, Sprite sprite, Color color)
        {
            Image img = AddImage(go, color);
            img.sprite = sprite;
            img.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            return img;
        }

        /// <summary>
        /// 校验 / 修正卡框九宫格图的导入参数。
        ///
        /// <para>为什么放在构建流程里而不是「一次性设好就别动」：`spriteBorder` 与
        /// `spritePixelsPerUnit` 只存在于 `.meta`，图一旦被重导（换素材、换机器、被
        /// 别的脚本重设导入设置）就会悄悄丢。丢了的症状是圆角被拉伸成椭圆 / 描边粗细不对，
        /// 而且<b>没有任何报错</b>。构建时对一遍成本几乎为零。</para>
        /// </summary>
        private static void EnsureCardBoxImporters()
        {
            ApplyCardBoxImporter(CardBoxPath);
            ApplyCardBoxImporter(CardBoxLinePath);
        }

        private static void ApplyCardBoxImporter(string path)
        {
            // 图是 Python 直接写盘的，Unity 还没建过它的 .meta —— 先补一次导入再取 Inspector。
            // 少了这一步，干净检出上第一次跑 M7 会报「找不到卡框图」，而图其实就在那儿。
            if (AssetImporter.GetAtPath(path) == null && File.Exists(path))
            {
                AssetDatabase.ImportAsset(path);
            }

            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning("[UiKit] 找不到卡框九宫格图：" + path
                                 + "\n    先跑：python Tools/art-audit/build_card_box.py --apply");
                return;
            }

            var border = new Vector4(CardBoxBorder, CardBoxBorder, CardBoxBorder, CardBoxBorder);
            bool dirty = false;

            if (ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (ti.spriteBorder != border)
            {
                ti.spriteBorder = border;
                dirty = true;
            }

            if (!Mathf.Approximately(ti.spritePixelsPerUnit, CardBoxPixelsPerUnit))
            {
                ti.spritePixelsPerUnit = CardBoxPixelsPerUnit;
                dirty = true;
            }

            if (ti.mipmapEnabled)
            {
                ti.mipmapEnabled = false;
                dirty = true;
            }

            if (!ti.alphaIsTransparency)
            {
                ti.alphaIsTransparency = true;
                dirty = true;
            }

            if (ti.wrapMode != TextureWrapMode.Clamp)
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                dirty = true;
            }

            if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (dirty)
            {
                ti.SaveAndReimport();
            }
        }

        // ══════════════════════════════════════════════════════
        //  Canvas
        // ══════════════════════════════════════════════════════

        private static GameObject BuildCanvasInActiveScene(CardView handPrefab, CardView miniPrefab)
        {
            // 幂等：先清掉上一次构建出来的 Canvas
            GameObject existing = GameObject.Find(CanvasName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var canvasGo = new GameObject(CanvasName,
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiLayout.ReferenceWidth, UiLayout.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 底色
            GameObject backdrop = NewUi("Backdrop", canvasGo.transform);
            AddImage(backdrop, UiTheme.Backdrop);
            Stretch(Rt(backdrop));

            // ── 上部区域 ──────────────────────────────────────
            GameObject topArea = NewUi("TopArea", canvasGo.transform);
            RectTransform topRt = Rt(topArea);
            topRt.anchorMin = new Vector2(0f, 0f);
            topRt.anchorMax = new Vector2(1f, 1f);
            topRt.offsetMin = new Vector2(0f, UiLayout.HandAreaHeight);
            topRt.offsetMax = Vector2.zero;

            RectTransform playerCooling = BuildCoolingPanel(topArea.transform, "PlayerCoolingPanel",
                new Vector2(0f, 1f), new Vector2(UiLayout.CoolingPanelMargin, -UiLayout.CoolingPanelTop));

            RectTransform enemyCooling = BuildCoolingPanel(topArea.transform, "EnemyCoolingPanel",
                new Vector2(1f, 1f), new Vector2(-UiLayout.CoolingPanelMargin, -UiLayout.CoolingPanelTop));

            // ── 舞台 ──────────────────────────────────────────
            GameObject stage = NewUi("Stage", topArea.transform);
            Stretch(Rt(stage));

            PlayerBarView playerBar = BuildPlayerBar(stage.transform, "PlayerBar",
                new Vector2(-UiLayout.PlayerBarOffsetX, 0f), UiTheme.PlayerAccent, false);

            PlayerBarView enemyBar = BuildPlayerBar(stage.transform, "EnemyBar",
                new Vector2(UiLayout.PlayerBarOffsetX, 0f), UiTheme.EnemyAccent, true);

            GameObject markGo = NewUi("StageMark", stage.transform);
            Box(Rt(markGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 90f));
            TMP_Text mark = AddText(markGo, LoadFont(TitleFontPath), UiLayout.FontSizeStageMark,
                UiTheme.TextSecondary, TextAlignmentOptions.Center);

            // ── 手牌区（底部全宽）────────────────────────────
            GameObject handArea = NewUi("HandArea", canvasGo.transform);
            RectTransform handAreaRt = Rt(handArea);
            handAreaRt.anchorMin = new Vector2(0f, 0f);
            handAreaRt.anchorMax = new Vector2(1f, 0f);
            handAreaRt.pivot = new Vector2(0.5f, 0f);
            handAreaRt.anchoredPosition = Vector2.zero;
            handAreaRt.sizeDelta = new Vector2(0f, UiLayout.HandAreaHeight);

            GameObject handRow = NewUi("HandRow", handArea.transform);
            RectTransform handRowRt = Rt(handRow);
            handRowRt.anchorMin = new Vector2(0f, 0f);
            handRowRt.anchorMax = new Vector2(1f, 0f);
            handRowRt.pivot = new Vector2(0.5f, 0f);
            handRowRt.anchoredPosition = new Vector2(0f, UiLayout.HandCardBottom);
            handRowRt.sizeDelta = new Vector2(0f, UiLayout.HandCardHeight);

            var rowLayout = handRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = UiLayout.HandCardSpacing;
            rowLayout.childAlignment = TextAnchor.LowerCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;

            // ── 自检装置 ─────────────────────────────────────
            var preview = canvasGo.AddComponent<UiKitPreview>();
            preview.EditorWire(handPrefab, miniPrefab, handRowRt, playerCooling, enemyCooling,
                playerBar, enemyBar, mark);

            EnsureEventSystem();

            return canvasGo;
        }

        /// <summary>
        /// 场景里没有 EventSystem 的话，所有 Button 都点不动 —— M8 接交互时会踩。
        /// 本工程用的是旧版 Input Manager（manifest 里没有 com.unity.inputsystem），
        /// 所以挂 StandaloneInputModule。
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        private static RectTransform BuildCoolingPanel(
            Transform parent, string name, Vector2 anchor, Vector2 offset)
        {
            GameObject panel = NewUi(name, parent);
            RectTransform rt = Rt(panel);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, 1f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(UiLayout.CoolingPanelWidth,
                UiLayout.TopAreaHeight - UiLayout.CoolingPanelTop - UiLayout.CoolingPanelBottom);

            GameObject grid = NewUi("Grid", panel.transform);
            Stretch(Rt(grid));

            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(UiLayout.MiniCardWidth, UiLayout.MiniCardHeight);
            layout.spacing = new Vector2(UiLayout.MiniCardSpacing, UiLayout.MiniCardSpacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 1;
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperCenter;

            grid.AddComponent<MiniCardGrid>();

            return Rt(grid);
        }

        private static PlayerBarView BuildPlayerBar(
            Transform parent, string name, Vector2 offset, Color accent, bool showHandCount)
        {
            GameObject bar = NewUi(name, parent);
            Box(Rt(bar), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), offset,
                new Vector2(UiLayout.PlayerBarWidth, UiLayout.PlayerBarHeight));

            GameObject accentGo = NewUi("Accent", bar.transform);
            RectTransform accentRt = Rt(accentGo);
            accentRt.anchorMin = new Vector2(0f, 0f);
            accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = Vector2.zero;
            accentRt.sizeDelta = new Vector2(6f, 0f);
            Image accentImg = AddImage(accentGo, accent);

            GameObject panelGo = NewUi("Panel", bar.transform);
            AddImage(panelGo, UiTheme.Panel);
            Stretch(Rt(panelGo), 6f, 0f, 0f, 0f);

            GameObject nameGo = NewUi("Name", bar.transform);
            Box(Rt(nameGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(UiLayout.PlayerBarWidth - 32f, 44f));
            TMP_Text nameText = AddText(nameGo, LoadFont(TitleFontPath), UiLayout.FontSizePlayerName,
                accent, TextAlignmentOptions.Center);

            GameObject hpRow = NewUi("HpDots", bar.transform);
            float dotsWidth = UiLayout.HpDotCount * UiLayout.HpDotSize
                              + (UiLayout.HpDotCount - 1) * UiLayout.HpDotSpacing;
            Box(Rt(hpRow), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(dotsWidth, UiLayout.HpDotSize));
            var hpLayout = hpRow.AddComponent<HorizontalLayoutGroup>();
            hpLayout.spacing = UiLayout.HpDotSpacing;
            hpLayout.childAlignment = TextAnchor.MiddleCenter;
            hpLayout.childForceExpandWidth = false;
            hpLayout.childForceExpandHeight = false;
            hpLayout.childControlWidth = false;
            hpLayout.childControlHeight = false;

            var dots = new Image[UiLayout.HpDotCount];
            for (int i = 0; i < dots.Length; i++)
            {
                GameObject dotGo = NewUi("Dot" + i, hpRow.transform);
                dots[i] = AddImage(dotGo, UiTheme.HpFull);
                Box(Rt(dotGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(UiLayout.HpDotSize, UiLayout.HpDotSize));
                var leDot = dotGo.AddComponent<LayoutElement>();
                leDot.preferredWidth = UiLayout.HpDotSize;
                leDot.preferredHeight = UiLayout.HpDotSize;
            }

            GameObject hpNumGo = NewUi("HpNumber", bar.transform);
            Box(Rt(hpNumGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -104f), new Vector2(160f, 44f));
            TMP_Text hpNum = AddText(hpNumGo, LoadFont(BodyFontPath), UiLayout.FontSizeHpNumber,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);
            hpNum.fontStyle = FontStyles.Bold;

            GameObject handCountRoot = NewUi("HandCount", bar.transform);
            Box(Rt(handCountRoot), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -146f), new Vector2(220f, 30f));
            TMP_Text handCount = AddText(handCountRoot, LoadFont(BodyFontPath), UiLayout.FontSizeHandCount,
                UiTheme.TextSecondary, TextAlignmentOptions.Center);
            handCountRoot.SetActive(showHandCount);

            GameObject auraRoot = NewUi("Aura", bar.transform);
            Box(Rt(auraRoot), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(240f, 30f));
            TMP_Text auraText = AddText(auraRoot, LoadFont(BodyFontPath), 22f,
                UiTheme.AuraReady, TextAlignmentOptions.Center);

            var view = bar.AddComponent<PlayerBarView>();
            var so = new SerializedObject(view);
            so.FindProperty("_nameLabel").objectReferenceValue = nameText;
            so.FindProperty("_hpNumber").objectReferenceValue = hpNum;
            SetArray(so.FindProperty("_hpDots"), dots);
            so.FindProperty("_accent").objectReferenceValue = accentImg;
            so.FindProperty("_handCountRoot").objectReferenceValue = handCountRoot;
            so.FindProperty("_handCount").objectReferenceValue = handCount;
            so.FindProperty("_auraRoot").objectReferenceValue = auraRoot;
            so.FindProperty("_auraCount").objectReferenceValue = auraText;
            so.FindProperty("_showHandCount").boolValue = showHandCount;
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        // ══════════════════════════════════════════════════════
        //  基础构件
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

        /// <summary>用 4 根细条拼一个矩形边框（没有 9-slice 贴图时最省事的做法）。</summary>
        private static GameObject MakeFrame(Transform parent, float thickness, Color color)
        {
            GameObject frame = NewUi("Frame", parent);
            Stretch(Rt(frame));

            AddBar(frame.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, thickness), color);
            AddBar(frame.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, thickness), color);
            AddBar(frame.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(thickness, 0f), color);
            AddBar(frame.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(thickness, 0f), color);

            return frame;
        }

        private static void AddBar(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot,
            Vector2 pos, Vector2 size, Color color)
        {
            GameObject bar = NewUi("Bar", parent);
            RectTransform rt = Rt(bar);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            AddImage(bar, color);
        }

        /// <summary>
        /// 把刚挂上去的 <see cref="Button"/> 接回 <c>CardView._button</c>。
        ///
        /// <para><b>这一步以前漏掉了，是个静默的坑</b>：卡片 Prefab 里 Button 挂上了、EventSystem 也在，
        /// 但 <c>CardView</c> 手里的 <c>_button</c> 一直是 null —— 于是 <c>Awake</c> 里那行
        /// <c>onClick.AddListener</c> 从来没执行过，<b>整张卡点不动</b>，
        /// 而 <c>SetInteractable</c> 也悄悄退化成空操作（不可点的牌照样能点）。</para>
        ///
        /// <para>界面上一眼看不出异常 —— 压暗由 <c>_dim</c> 负责、选中边框由 <c>_glow</c> 负责，
        /// 都不是这个字段；M8 的截图又是靠 <c>_autoPlay</c> 自动打完的，所以它一直没暴露。
        /// 修完必须<b>真点一下</b>才算验过，光看截图不算。</para>
        /// </summary>
        private static void WireButton(CardView view)
        {
            var so = new SerializedObject(view);
            so.FindProperty("_button").objectReferenceValue = view.GetComponent<Button>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureButton(GameObject go, bool withTargetGraphic)
        {
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

            if (withTargetGraphic)
            {
                // 透明兜底 Image，保证整张卡都能接收点击（子节点都是 raycastTarget=false）
                GameObject hitGo = NewUi("Hit", go.transform);
                var hit = hitGo.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;
                Stretch(Rt(hitGo));
                hitGo.transform.SetSiblingIndex(0);
                btn.targetGraphic = hit;
            }
        }

        private static void SetArray<T>(SerializedProperty prop, T[] values) where T : Object
        {
            if (prop == null)
            {
                return;
            }

            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static CardView SavePrefab(GameObject template, string fileName)
        {
            string path = PrefabDir + "/" + fileName + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(template, path);
            Object.DestroyImmediate(template);

            // 卡片 Prefab 的根节点是半成品（不在场景里），LayoutElement 等由运行时的布局组接手
            return prefab == null ? null : prefab.GetComponent<CardView>();
        }

        private static TMP_FontAsset LoadFont(string path)
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path);
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent.Replace('\\', '/'));
            }

            AssetDatabase.CreateFolder(parent == null ? "Assets" : parent.Replace('\\', '/'), leaf);
        }
    }
}
