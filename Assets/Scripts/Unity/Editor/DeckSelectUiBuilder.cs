using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;
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
    /// <b>D1 · 自定义战斗的牌组编成场景</b>（2026-10-28 建，2026-11-01 按新设计稿重排）。
    ///
    /// <para>菜单：<c>魔法乱斗/D1 · 构建 DeckSelect 场景</c>（<b>无确认弹窗</b>，可被脚本 / MCP 直调）。</para>
    ///
    /// <para><b>2026-11-01 的三条改动</b>（用户口径「UI 优化 + 按钮统一」）：</para>
    /// <list type="number">
    /// <item><b>按钮全部换成牌座</b> —— 与战斗内选牌弹窗（充能 / 磁暴）的确认键同一张
    /// <c>HandPick_Seat.png</c>（264×66）、同一套亮 / 暗两态（<c>HandPick_SeatOff.png</c>）。
    /// 旧版那几枚「奶油长条」（<c>HandPick_Confirm.png</c> 拉成 360×84 / 280×76）全部下掉；
    /// 页签也用牌座表达亮 / 暗。<b>不要再往这里引 HandPick_Confirm.png。</b></item>
    /// <item><b>卡表新增占用态</b>：被对方选走的牌压暗 + 底部挂一条「对手已选」标签
    /// （标签节点在模板上，克隆自带，运行时只切 active）。</item>
    /// <item><b>8 格补全</b>：空位有虚线框与编号 1–8，有卡时右上角出现 × 移除键；
    /// 卡表补一条滚动条与底部渐隐。</item>
    /// </list>
    ///
    /// <para><b>它建什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item><c>DeckSelectCanvas</c>：整屏底 + 左对齐标题 + 副标题 + 两枚页签牌座 +
    /// 左侧卡表（ScrollRect + 5 列网格 + 滚动条）+ 右侧 8 格 + 底部「清空 / 开战」+ 提示行；</item>
    /// <item><b>2026-11-02 新增</b>：第三枚牌座「历史牌组」（带计数徽标）、底部「随机 8 张」、
    /// 历史覆盖层（遮罩 / 两列网格 / 行模板 / 空态 / 滚动条 / 关闭）与中央浮字；</item>
    /// <item>把 <see cref="DeckSelectView"/> 的序列化字段一次性接好（含 8 个卡位、虚线框、编号、移除键、
    /// 牌座亮暗两张图）；</item>
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
        private const string PanelSpritePath = "Assets/Art/Ui/CardBox_Line.png";
        private const string SlotDashPath = "Assets/Art/Ui/HandPick_SlotDash.png";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 LeftMid = new Vector2(0f, 0.5f);

        // ── 版式（1920×1080，原点在屏幕中心，y 向上）─────────────
        private const float TitleX = 40f;
        private const float TitleY = 475f;
        private const float SubtitleX = 42f;
        private const float SubtitleY = 417f;

        private const float SeatWidth = 264f;
        private const float SeatHeight = 66f;

        private const float BackX = 768f;
        private const float BackY = 463f;

        private const float TabY = 355f;
        private const float TabPlayerX = -788f;
        private const float TabEnemyX = -516f;

        private const float CatalogCenterX = -425f;
        private const float CatalogCenterY = -16f;
        private const float CatalogWidth = 990f;
        private const float CatalogHeight = 660f;

        private const float DeckCenterX = 510f;
        private const float DeckCenterY = -16f;
        private const float DeckWidth = 820f;
        private const float DeckHeight = 660f;

        private const float PanelPad = 18f;
        private const float HeadHeight = 34f;
        private const float HeadCenterY = -33f;

        /// <summary>
        /// 卡表 cell <b>= 卡面尺寸</b>（168 × 233）。
        /// 别把 cell 做得比卡面大：CardView 的 Rect 是布局占位、卡面是内部缩放，
        /// cell 留太宽只会让行距看起来凭空多出十几像素（2026-11-01 第一次构建踩到）。
        /// 5 × 168 + 4 × 12 = 888，卡表内宽 954，两侧各留 33。
        /// </summary>
        private const float CatalogCellWidth = 168f;
        private const float CatalogCellHeight = 233f;
        private const float CatalogGapX = 12f;
        private const float CatalogGapY = 10f;

        /// <summary>卡表内容高度：9 行 × 233 + 8 × 10 + 上下 padding 12 = 2201，留 9 px 余量。</summary>
        private const float CatalogContentHeight = 2210f;

        private const float SlotCellWidth = 184f;
        private const float SlotCellHeight = 256f;
        private const float SlotGapX = 13f;
        private const float SlotGapY = 14f;

        private const float HintY = -394f;
        private const float ActionY = -469f;

        // ── 2026-11-02：历史牌组 / 随机 8 张 ──────────────────
        //
        //  历史牌座接在两枚页签右边、空 40px —— 它是「动作」不是「页签」，
        //  贴太近会被当成第三个页签（点下去并不会切换编辑对象）。
        private const float HistorySeatX = -212f;
        private const float RandomSeatX = -695f;

        private const float HistoryPanelWidth = 1560f;
        private const float HistoryPanelHeight = 820f;

        /// <summary>面板内滚动区距顶 / 距底（标题区 128、页脚 118）。</summary>
        private const float HistoryScrollTop = 128f;
        private const float HistoryScrollBottom = 118f;

        private const string HistoryTitle = "历史牌组";

        private static readonly Color38 PanelFill = new Color38("1A2130");
        private static readonly Color38 PanelEdge = new Color38("2B3547");
        private static readonly Color38 HeadTitleColor = new Color38("C9D6E8");
        private static readonly Color38 HeadCountColor = new Color38("7C8DA3");
        private static readonly Color38 SlotNumberColor = new Color38("2C3B50");
        private static readonly Color38 SlotEmptyFill = new Color38("0A1B23");
        private static readonly Color38 SlotEmptyEdge = new Color38("223044");
        private static readonly Color38 RemoveFill = new Color38("101822");
        private static readonly Color38 RemoveGlyph = new Color38("96A3B8");
        private static readonly Color38 BarBackdrop = new Color38("0A0E14");

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
            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            Sprite slotDash = AssetDatabase.LoadAssetAtPath<Sprite>(SlotDashPath);
            Sprite seatOn = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickSeatSpritePath);
            Sprite seatOff = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickSeatOffSpritePath);

            if (body == null || title == null || handPrefab == null)
            {
                log.AppendLine("✘ 缺字体或卡面 Prefab：" + BodyFontPath + " / " + TitleFontPath + " / " + HandCardPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            if (seatOn == null || seatOff == null)
            {
                log.AppendLine("✘ 缺牌座贴图：" + UiLayout.HandPickSeatSpritePath
                               + " / " + UiLayout.HandPickSeatOffSpritePath);
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
            GameObject canvasGo = BuildCanvas(body, title, handPrefab, panelSprite, slotDash, seatOn, seatOff, log);
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

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title, CardView handPrefab,
            Sprite panelSprite, Sprite slotDash, Sprite seatOn, Sprite seatOff, StringBuilder log)
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

            // ② 标题（左对齐）+ 副标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rect(titleGo), LeftMid, LeftMid, new Vector2(TitleX, TitleY), new Vector2(900f, 80f));
            TMP_Text titleText = AddText(titleGo, title, 62f, UiTheme.TextPrimary, TextAlignmentOptions.Left,
                "自定义战斗");
            titleText.characterSpacing = 2f;

            GameObject subGo = NewUi("Subtitle", canvasGo.transform);
            Box(Rect(subGo), LeftMid, LeftMid, new Vector2(SubtitleX, SubtitleY), new Vector2(1100f, 30f));
            AddText(subGo, body, 21f, UiTheme.TextSecondary, TextAlignmentOptions.Left,
                "编成双方的牌组　·　各 " + BattleLaunch.DeckSize + " 张、互不重复　·　点卡表里的牌加入或移除");

            // ③ 右上「返回」
            Image backImage;
            TMP_Text backLabel;
            Button backButton = BuildSeat(canvasGo.transform, "BackButton", "返回",
                new Vector2(BackX, BackY), title, seatOff, seatOn, 30f, out backImage, out backLabel);

            // ④ 两枚页签（牌座亮 / 暗两态）
            Image playerTabImage;
            TMP_Text playerLabel;
            Button playerTab = BuildSeat(canvasGo.transform, "PlayerTab", "你的牌组 0 / " + BattleLaunch.DeckSize,
                new Vector2(TabPlayerX, TabY), title, seatOff, seatOn, 28f, out playerTabImage, out playerLabel);

            Image enemyTabImage;
            TMP_Text enemyLabel;
            Button enemyTab = BuildSeat(canvasGo.transform, "EnemyTab", "对手的牌组 0 / " + BattleLaunch.DeckSize,
                new Vector2(TabEnemyX, TabY), title, seatOff, seatOn, 28f, out enemyTabImage, out enemyLabel);

            // ④′ 历史牌组（第三枚牌座）+ 右上角计数徽标
            Image historyImage;
            TMP_Text historyLabel;
            Button historyButton = BuildSeat(canvasGo.transform, "HistoryButton", HistoryTitle,
                new Vector2(HistorySeatX, TabY), title, seatOff, seatOn, 28f, out historyImage, out historyLabel);

            GameObject historyBadge;
            TMP_Text historyBadgeText;
            BuildCountBadge(historyButton.transform, title, out historyBadge, out historyBadgeText);

            // ⑤ 左侧卡表
            GameObject catalogGo = BuildPanel(canvasGo.transform, "CatalogPanel",
                new Vector2(CatalogCenterX, CatalogCenterY), new Vector2(CatalogWidth, CatalogHeight), panelSprite);

            BuildPanelHead(catalogGo.transform, body, title, "基础牌库", CatalogWidth,
                out TMP_Text catalogCount);
            BuildLegend(catalogGo.transform, body, CatalogWidth);

            var scroll = catalogGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            GameObject viewport = NewUi("Viewport", catalogGo.transform);
            Stretch(Rect(viewport));
            // ⚠ 上沿必须让开面板头（Head 占 16…50），否则第一行卡的上半截被头部压住，
            //   看上去像「卡表滚到中间去了」（2026-11-01 第一次构建踩到）。
            Rect(viewport).offsetMax = new Vector2(-PanelPad, -58f);
            Rect(viewport).offsetMin = new Vector2(PanelPad, PanelPad);
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
            contentRect.sizeDelta = new Vector2(0f, CatalogContentHeight);

            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CatalogCellWidth, CatalogCellHeight);
            grid.spacing = new Vector2(CatalogGapX, CatalogGapY);
            grid.padding = new RectOffset(0, 0, 12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;

            // ⚠ 内容高度写死（9 行 × 266 + 24 = 2418 → 取 2410 的整十），不用 ContentSizeFitter ——
            //   ScrollRect + RectMask2D + Fitter + Grid 这套在编辑模式下会让 UGUI 反复重排，
            //   构建期间表现成「点了菜单就不回来」。卡表是 44 张固定量，直接给高度最稳。
            scroll.viewport = Rect(viewport);
            scroll.content = contentRect;

            Scrollbar scrollbar = BuildScrollbar(catalogGo.transform, scroll);

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
            Box(Rect(template.gameObject), Mid, Mid, Vector2.zero,
                new Vector2(CatalogCellWidth, CatalogCellHeight));
            template.SetFaceWidth(DeckSelectView.DeckCardFaceWidth);
            BuildTakenTag(template.transform, title);
            template.Clear();
            template.gameObject.SetActive(false);

            // ⑥ 右侧牌组面板 + 8 格
            GameObject deckGo = BuildPanel(canvasGo.transform, "DeckPanel",
                new Vector2(DeckCenterX, DeckCenterY), new Vector2(DeckWidth, DeckHeight), panelSprite);

            BuildPanelHeadSimple(deckGo.transform, body, title, "牌组内容", DeckWidth);

            GameObject slotsRoot = NewUi("Slots", deckGo.transform);
            Box(Rect(slotsRoot), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(PanelPad, -58f), new Vector2(DeckWidth - 2f * PanelPad, 584f));

            var slotGrid = slotsRoot.AddComponent<GridLayoutGroup>();
            slotGrid.cellSize = new Vector2(SlotCellWidth, SlotCellHeight);
            slotGrid.spacing = new Vector2(SlotGapX, SlotGapY);
            slotGrid.padding = new RectOffset(0, 0, 0, 0);
            slotGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            slotGrid.constraintCount = 4;
            slotGrid.childAlignment = TextAnchor.MiddleCenter;

            var slots = new CardView[BattleLaunch.DeckSize];
            var frames = new GameObject[BattleLaunch.DeckSize];
            var numbers = new TMP_Text[BattleLaunch.DeckSize];
            var removes = new Button[BattleLaunch.DeckSize];

            for (int i = 0; i < BattleLaunch.DeckSize; i++)
            {
                BuildDeckSlot(slotsRoot.transform, i, handPrefab, title, slotDash,
                    out slots[i], out frames[i], out numbers[i], out removes[i]);
            }

            // ⑦ 底部：提示 + 清空 / 开战
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rect(hintGo), Mid, Mid, new Vector2(0f, HintY), new Vector2(1700f, 40f));
            TMP_Text hint = AddText(hintGo, body, 19f, UiTheme.TextSecondary, TextAlignmentOptions.Center,
                string.Empty);

            Image randomImage;
            TMP_Text randomLabel;
            Button randomButton = BuildSeat(canvasGo.transform, "RandomButton", "随机 8 张",
                new Vector2(RandomSeatX, ActionY), title, seatOff, seatOn, 30f, out randomImage, out randomLabel);

            Image clearImage;
            TMP_Text clearLabel;
            Button clearButton = BuildSeat(canvasGo.transform, "ClearButton", "清空",
                new Vector2(CatalogCenterX, ActionY), title, seatOff, seatOn, 30f, out clearImage, out clearLabel);

            Image startImage;
            TMP_Text startLabel;
            Button startButton = BuildSeat(canvasGo.transform, "StartButton", "开战",
                new Vector2(DeckCenterX, ActionY), title, seatOff, seatOn, 30f, out startImage, out startLabel);

            // ⑧ 接线
            var deckView = canvasGo.AddComponent<DeckSelectView>();
            var so = new SerializedObject(deckView);
            SetRef(so, "_title", titleText);
            SetRef(so, "_hint", hint);
            SetRef(so, "_playerTab", playerTab);
            SetRef(so, "_playerTabImage", playerTabImage);
            SetRef(so, "_playerTabLabel", playerLabel);
            SetRef(so, "_enemyTab", enemyTab);
            SetRef(so, "_enemyTabImage", enemyTabImage);
            SetRef(so, "_enemyTabLabel", enemyLabel);
            SetRef(so, "_seatOn", seatOn);
            SetRef(so, "_seatOff", seatOff);
            SetRef(so, "_catalogScroll", scroll);
            SetRef(so, "_catalogContent", contentRect);
            SetRef(so, "_catalogTemplate", template);
            SetRef(so, "_startButton", startButton);
            SetRef(so, "_startImage", startImage);
            SetRef(so, "_startLabel", startLabel);
            SetRef(so, "_clearButton", clearButton);
            SetRef(so, "_clearImage", clearImage);
            SetRef(so, "_clearLabel", clearLabel);
            SetRef(so, "_backButton", backButton);

            SetRef(so, "_historyButton", historyButton);
            SetRef(so, "_historyImage", historyImage);
            SetRef(so, "_historyLabel", historyLabel);
            SetRef(so, "_historyBadge", historyBadge);
            SetRef(so, "_historyBadgeText", historyBadgeText);
            SetRef(so, "_randomButton", randomButton);
            SetRef(so, "_randomImage", randomImage);
            SetRef(so, "_randomLabel", randomLabel);

            // 覆盖层放在最后建 —— 兄弟顺序就是层级，它必须盖在标题 / 卡表 / 按钮之上
            HistoryLayer layer = BuildHistoryLayer(canvasGo.transform, body, title, panelSprite, seatOff, seatOn);
            SetRef(so, "_historyOverlay", layer.Root);
            SetRef(so, "_historyVeil", layer.Veil);
            SetRef(so, "_historyCloseButton", layer.Close);
            SetRef(so, "_historyCloseSeat", layer.CloseSeat);
            SetRef(so, "_historyRowsRoot", layer.RowsRoot);
            SetRef(so, "_historyContent", layer.Content);
            SetRef(so, "_historyRowTemplate", layer.RowTemplate);
            SetRef(so, "_historyEmpty", layer.Empty);
            SetRef(so, "_historySubtitle", layer.Subtitle);
            SetRef(so, "_historyScroll", layer.Scroll);

            SetRef(so, "_toast", BuildToast(canvasGo.transform, title));

            SetArray(so, "_slotViews", slots);
            SetArray(so, "_slotFrames", frames);
            SetArray(so, "_slotNumbers", numbers);
            SetArray(so, "_slotRemoves", removes);

            so.ApplyModifiedPropertiesWithoutUndo();

            // 初始点亮「你的牌组」这一枚页签（Awake 里 SetTab 会再刷一次）
            if (playerTabImage != null) playerTabImage.sprite = seatOn;
            if (playerLabel != null) playerLabel.color = UiTheme.HandPickSeatTextOn;

            log.AppendLine("  已建 " + CanvasName
                           + "（卡表 5 列 + 8 格 + 页签 / 开战 / 清空 / 返回 全部牌座"
                           + " + 历史牌组牌座与覆盖层 + 随机 8 张 + 中央浮字）");
            if (catalogCount != null) catalogCount.text = CardCatalog.Builtin().All.Count + " 张";
            if (scrollbar == null) log.AppendLine("  ⚠ 滚动条没建出来");
            return canvasGo;
        }

        // ══════════════════════════════════════════════════════
        //  零件
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>牌座按钮</b>：全界面唯一的功能键形态（与战斗内 HandPick 弹窗同素材同尺寸）。
        ///
        /// <para>亮 / 暗是<b>两张图</b>（<c>HandPick_Seat.png</c> / <c>HandPick_SeatOff.png</c>），
        /// 不是 ColorTint —— 牌座的银边与青灯乘一个颜色出来会发灰（HandPick 那边已踩过）。
        /// 运行时由 <see cref="DeckSelectView"/> 按状态换 <c>Image.sprite</c>。</para>
        /// </summary>
        private static Button BuildSeat(Transform parent, string name, string label, Vector2 position,
            TMP_FontAsset font, Sprite off, Sprite on, float fontSize, out Image image, out TMP_Text labelText)
        {
            GameObject go = NewUi(name, parent);
            Box(Rect(go), Mid, Mid, position, new Vector2(SeatWidth, SeatHeight));

            var img = go.AddComponent<Image>();
            img.sprite = off;
            img.color = Color.white;      // 贴图里已经带了配色，别再染色
            img.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.SpriteSwap;
            var state = button.spriteState;
            state.highlightedSprite = on;
            state.pressedSprite = off;
            state.selectedSprite = on;
            state.disabledSprite = off;
            button.spriteState = state;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rect(labelGo));
            labelText = AddText(labelGo, font, fontSize, UiTheme.HandPickSeatTextOn,
                TextAlignmentOptions.Center, label);
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = fontSize * 0.62f;
            labelText.fontSizeMax = fontSize;
            labelText.overflowMode = TextOverflowModes.Truncate;

            image = img;
            return button;
        }

        /// <summary>面板：纯色底 + 一圈 <c>CardBox_Line.png</c> 描边（九宫格，怎么拉都不变形）。</summary>
        private static GameObject BuildPanel(Transform parent, string name, Vector2 center, Vector2 size,
            Sprite edgeSprite)
        {
            GameObject go = NewUi(name, parent);
            Box(Rect(go), Mid, Mid, center, size);

            var fill = go.AddComponent<Image>();
            fill.color = PanelFill;
            fill.raycastTarget = false;

            GameObject edgeGo = NewUi("Edge", go.transform);
            Stretch(Rect(edgeGo));
            var edge = edgeGo.AddComponent<Image>();
            if (edgeSprite != null)
            {
                edge.sprite = edgeSprite;
                edge.type = Image.Type.Sliced;
            }

            edge.color = PanelEdge;
            edge.raycastTarget = false;

            return go;
        }

        /// <summary>面板头（卡表那版）：左「基础牌库 + 张数」，右图例留位。</summary>
        private static void BuildPanelHead(Transform panel, TMP_FontAsset body, TMP_FontAsset title,
            string heading, float panelWidth, out TMP_Text countText)
        {
            GameObject headGo = NewUi("Head", panel);
            Box(Rect(headGo), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(PanelPad, HeadCenterY), new Vector2(panelWidth - 2f * PanelPad, HeadHeight));

            GameObject labelGo = NewUi("Heading", headGo.transform);
            Box(Rect(labelGo), LeftMid, LeftMid, new Vector2(0f, 0f), new Vector2(240f, HeadHeight));
            AddText(labelGo, title, 22f, HeadTitleColor, TextAlignmentOptions.Left, heading);

            GameObject countGo = NewUi("Count", headGo.transform);
            Box(Rect(countGo), LeftMid, LeftMid, new Vector2(132f, 0f), new Vector2(120f, HeadHeight));
            countText = AddText(countGo, body, 16f, HeadCountColor, TextAlignmentOptions.Left, string.Empty);
        }

        /// <summary>面板头（牌组那版）：左标签 + 右提示，够用不啰嗦。</summary>
        private static void BuildPanelHeadSimple(Transform panel, TMP_FontAsset body, TMP_FontAsset title,
            string heading, float panelWidth)
        {
            GameObject headGo = NewUi("Head", panel);
            Box(Rect(headGo), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(PanelPad, HeadCenterY), new Vector2(panelWidth - 2f * PanelPad, HeadHeight));

            GameObject labelGo = NewUi("Heading", headGo.transform);
            Box(Rect(labelGo), LeftMid, LeftMid, new Vector2(0f, 0f), new Vector2(240f, HeadHeight));
            AddText(labelGo, title, 22f, HeadTitleColor, TextAlignmentOptions.Left, heading);

            GameObject tipGo = NewUi("Tip", headGo.transform);
            Box(Rect(tipGo), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f),
                new Vector2(360f, HeadHeight));
            AddText(tipGo, body, 14f, HeadCountColor, TextAlignmentOptions.Right, "点格子右上角 × 移除");
        }

        /// <summary>卡表右上角的图例：两个色块 + 两段说明。</summary>
        private static void BuildLegend(Transform panel, TMP_FontAsset body, float panelWidth)
        {
            GameObject legend = NewUi("Legend", panel);
            Box(Rect(legend), new Vector2(0f, 1f), new Vector2(1f, 0.5f),
                new Vector2(panelWidth - PanelPad, HeadCenterY), new Vector2(260f, HeadHeight));

            AddLegendItem(legend.transform, body, 0f, UiTheme.SelectionGlow, "你已选");
            AddLegendItem(legend.transform, body, 132f, UiTheme.EnemyAccent, "对方已选");
        }

        private static void AddLegendItem(Transform parent, TMP_FontAsset body, float x, Color color, string label)
        {
            GameObject dotGo = NewUi("Dot", parent);
            Box(Rect(dotGo), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f),
                new Vector2(10f, 10f));
            var dot = dotGo.AddComponent<Image>();
            dot.color = color;
            dot.raycastTarget = false;

            GameObject textGo = NewUi("Text", parent);
            Box(Rect(textGo), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x + 16f, 0f),
                new Vector2(110f, HeadHeight));
            AddText(textGo, body, 14f, HeadCountColor, TextAlignmentOptions.Left, label);
        }

        /// <summary>
        /// 卡表卡面上的「对手已选」标签（模板上建，克隆自带）。
        ///
        /// <para>放在 CardView 根节点下、<b>最上层</b>，<c>raycastTarget = false</c> ——
        /// 它只做标记，点击要穿到卡面自己的 Button 上（那张牌仍然可点，由 Entry 解释原因）。</para>
        /// </summary>
        private static void BuildTakenTag(Transform card, TMP_FontAsset font)
        {
            GameObject tagGo = NewUi("TakenTag", card);
            Stretch(Rect(tagGo));
            tagGo.transform.SetAsLastSibling();

            GameObject barGo = NewUi("Bar", tagGo.transform);
            var barRect = Rect(barGo);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, 26f);
            var bar = barGo.AddComponent<Image>();
            bar.color = new Color(BarBackdrop.R, BarBackdrop.G, BarBackdrop.B, 0.86f);
            bar.raycastTarget = false;

            GameObject labelGo = NewUi("Label", tagGo.transform);
            var labelRect = Rect(labelGo);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, 26f);
            AddText(labelGo, font, 13f, UiTheme.EnemyAccent, TextAlignmentOptions.Center, "对手已选");

            tagGo.SetActive(false);
        }

        /// <summary>8 格中的一格：虚线空位 + 编号 + 卡面 + 右上角 × 。</summary>
        private static void BuildDeckSlot(Transform parent, int index, CardView handPrefab, TMP_FontAsset title,
            Sprite slotDash, out CardView view, out GameObject frame, out TMP_Text number, out Button remove)
        {
            GameObject slotGo = NewUi("Slot" + (index + 1), parent);
            // 尺寸由 GridLayoutGroup 接管；这里给个初值，免得编辑模式下看起来是一坨。
            RTCopy(Rect(slotGo), new Vector2(SlotCellWidth, SlotCellHeight));

            // 空位底盘（有卡时隐藏，露出底下的卡）
            GameObject emptyGo = NewUi("Empty", slotGo.transform);
            Stretch(Rect(emptyGo));
            var empty = emptyGo.AddComponent<Image>();
            empty.color = SlotEmptyFill;
            empty.raycastTarget = false;
            frame = emptyGo;

            GameObject dashGo = NewUi("Dash", emptyGo.transform);
            Stretch(Rect(dashGo));
            var dash = dashGo.AddComponent<Image>();
            if (slotDash != null)
            {
                dash.sprite = slotDash;
                dash.type = Image.Type.Simple;
            }

            dash.color = Color.white;
            dash.raycastTarget = false;

            GameObject numGo = NewUi("Number", emptyGo.transform);
            Stretch(Rect(numGo));
            number = AddText(numGo, title, 34f, SlotNumberColor, TextAlignmentOptions.Center,
                (index + 1).ToString());

            // 卡面（有卡时显示）
            CardView slotPrefab = LoadHandPrefab();
            GameObject cardGo = slotPrefab == null
                ? null
                : PrefabUtility.InstantiatePrefab(slotPrefab.gameObject, slotGo.transform) as GameObject;

            view = null;
            if (cardGo != null)
            {
                view = cardGo.GetComponent<CardView>();
                cardGo.name = "Card";
                Box(Rect(cardGo), Mid, Mid, Vector2.zero, new Vector2(SlotCellWidth, SlotCellHeight));
                view.SetFaceWidth(DeckSelectView.DeckSlotFaceWidth);
                view.Clear();
                view.SetReadOnly();
                cardGo.SetActive(false);
            }

            // 右上角 ×
            GameObject removeGo = NewUi("RemoveButton", slotGo.transform);
            var removeRect = Rect(removeGo);
            removeRect.anchorMin = new Vector2(1f, 1f);
            removeRect.anchorMax = new Vector2(1f, 1f);
            removeRect.pivot = new Vector2(0.5f, 0.5f);
            removeRect.anchoredPosition = Vector2.zero;
            removeRect.sizeDelta = new Vector2(30f, 30f);
            removeGo.transform.SetAsLastSibling();

            var removeImage = removeGo.AddComponent<Image>();
            removeImage.color = RemoveFill;
            removeImage.raycastTarget = true;

            remove = removeGo.AddComponent<Button>();
            remove.targetGraphic = removeImage;
            var colors = remove.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 1f);
            remove.colors = colors;

            GameObject glyphGo = NewUi("Glyph", removeGo.transform);
            Stretch(Rect(glyphGo));
            AddText(glyphGo, title, 18f, RemoveGlyph, TextAlignmentOptions.Center, "×");

            removeGo.SetActive(false);
        }

        /// <summary>卡表右侧的细滚动条（接给 <see cref="ScrollRect.verticalScrollbar"/>）。</summary>
        private static Scrollbar BuildScrollbar(Transform panel, ScrollRect scroll)
        {
            GameObject barGo = NewUi("Scrollbar", panel);
            var barRect = Rect(barGo);
            barRect.anchorMin = new Vector2(1f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.anchoredPosition = new Vector2(-9f, -58f);
            barRect.sizeDelta = new Vector2(6f, 584f);

            var track = barGo.AddComponent<Image>();
            track.color = new Color(1f, 1f, 1f, 0.10f);
            track.raycastTarget = true;

            var bar = barGo.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.transition = Selectable.Transition.None;

            GameObject slideGo = NewUi("SlidingArea", barGo.transform);
            Stretch(Rect(slideGo));

            GameObject handleGo = NewUi("Handle", slideGo.transform);
            Stretch(Rect(handleGo));
            var handle = handleGo.AddComponent<Image>();
            handle.color = new Color32(0x6B, 0x7D, 0x96, 0xFF);
            handle.raycastTarget = true;

            bar.handleRect = Rect(handleGo);
            bar.targetGraphic = handle;

            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return bar;
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
        //  历史牌组覆盖层（2026-11-02）
        // ══════════════════════════════════════════════════════

        /// <summary>覆盖层建出来之后要接线的那几样（只在这一趟构建里用）。</summary>
        private struct HistoryLayer
        {
            public GameObject Root;
            public Button Veil;
            public Button Close;
            public Button CloseSeat;
            public RectTransform RowsRoot;
            public RectTransform Content;
            public DeckHistoryRowView RowTemplate;
            public GameObject Empty;
            public TMP_Text Subtitle;
            public ScrollRect Scroll;
        }

        /// <summary>
        /// <b>历史牌组面板</b>：遮罩 + 1560×820 的面板 + 两列网格 + 空态 + 页脚。
        ///
        /// <para>网格的 cell 固定 734×62、两列 —— 两条并排正好是「一行 16 张」。
        /// <b>内容高度不用 ContentSizeFitter</b>（ScrollRect + Fitter + Grid 会让 UGUI
        /// 在编辑模式下反复重排，构建器那边踩过），改由 <see cref="DeckSelectView.ShowHistory"/>
        /// 按行数直接写 sizeDelta。</para>
        ///
        /// <para>整层建完 <c>SetActive(false)</c>：打开是运行时的事，编辑模式下不该挡住场景。</para>
        /// </summary>
        private static HistoryLayer BuildHistoryLayer(Transform canvas, TMP_FontAsset body, TMP_FontAsset title,
            Sprite panelSprite, Sprite seatOff, Sprite seatOn)
        {
            var layer = new HistoryLayer();

            GameObject root = NewUi("HistoryOverlay", canvas);
            Stretch(Rect(root));

            // ① 遮罩：同时是关闭区，必须吃射线
            GameObject veilGo = NewUi("Veil", root.transform);
            Stretch(Rect(veilGo));
            var veilImage = veilGo.AddComponent<Image>();
            veilImage.color = UiTheme.HistoryVeil;
            veilImage.raycastTarget = true;
            var veilButton = veilGo.AddComponent<Button>();
            veilButton.transition = Selectable.Transition.None;
            veilButton.targetGraphic = veilImage;
            layer.Veil = veilButton;

            // ② 面板
            GameObject panel = BuildPanel(root.transform, "HistoryPanel", Vector2.zero,
                new Vector2(HistoryPanelWidth, HistoryPanelHeight), panelSprite);
            var panelFill = panel.GetComponent<Image>();
            if (panelFill != null)
            {
                panelFill.color = UiTheme.HistoryPanel;

                // ⚠ 面板必须自己吃射线：BuildPanel 的底图默认 raycastTarget=false，
                //   不打开的话点面板空白处会穿到背后的遮罩上，面板直接被关掉
                //   （看着就像「点哪都关闭」）。
                panelFill.raycastTarget = true;
            }

            GameObject titleGo = NewUi("Title", panel.transform);
            Box(Rect(titleGo), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(26f, -52f),
                new Vector2(700f, 60f));
            AddText(titleGo, title, 40f, UiTheme.TextPrimary, TextAlignmentOptions.Left, HistoryTitle);

            GameObject subGo = NewUi("Subtitle", panel.transform);
            Box(Rect(subGo), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(28f, -100f),
                new Vector2(1200f, 30f));
            layer.Subtitle = AddText(subGo, body, 20f, UiTheme.TextSecondary, TextAlignmentOptions.Left,
                string.Empty);

            layer.Close = BuildSmallButton(panel.transform, "CloseButton", "×",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(60f, 60f),
                UiTheme.OverlayPanel, UiTheme.OverlayEdge, UiTheme.TextSecondary, title, 30f);

            // ③ 滚动区（只滚列表，标题与页脚固定）
            GameObject scrollGo = NewUi("HistoryScroll", panel.transform);
            RectTransform scrollRect = Rect(scrollGo);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(24f, HistoryScrollBottom);
            scrollRect.offsetMax = new Vector2(-24f, -HistoryScrollTop);

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            GameObject viewport = NewUi("Viewport", scrollGo.transform);
            Stretch(Rect(viewport));
            var viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            GameObject content = NewUi("Content", viewport.transform);
            RectTransform contentRect = Rect(content);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(DeckSelectView.HistoryRowWidth, DeckSelectView.HistoryRowHeight);
            grid.spacing = new Vector2(20f, DeckSelectView.HistoryRowGap);
            grid.padding = new RectOffset(0, 0, 0, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;

            scroll.viewport = Rect(viewport);
            scroll.content = contentRect;

            layer.Scroll = scroll;
            layer.RowsRoot = contentRect;
            layer.Content = contentRect;

            BuildHistoryScrollbar(panel.transform, scroll);

            // ④ 空态（一条都没有时才亮）
            GameObject emptyGo = NewUi("Empty", panel.transform);
            Box(Rect(emptyGo), Mid, Mid, new Vector2(0f, 40f), new Vector2(1200f, 150f));
            AddText(emptyGo, body, 26f, UiTheme.TextSecondary, TextAlignmentOptions.Center,
                "还没有历史牌组\n打一局自定义战斗，点「开战」那一刻会自动记下双方的 8 张");
            emptyGo.SetActive(false);
            layer.Empty = emptyGo;

            // ⑤ 页脚（文字只占左侧 440，右边留给「关闭」牌座 —— 写宽了会钻到牌座底下）
            GameObject footGo = NewUi("FootHint", panel.transform);
            Box(Rect(footGo), new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(26f, 52f),
                new Vector2(440f, 30f));
            AddText(footGo, body, 17f, UiTheme.TextSecondary, TextAlignmentOptions.Left,
                "点「载入」= 整组 " + BattleLaunch.DeckSize + " 张填进当前页签那一方");

            Image closeSeatImage;
            TMP_Text closeSeatLabel;
            layer.CloseSeat = BuildSeat(panel.transform, "CloseSeat", "关闭",
                new Vector2(HistoryPanelWidth * 0.5f - 24f - 132f, -HistoryPanelHeight * 0.5f + 24f + 33f),
                title, seatOff, seatOn, 30f, out closeSeatImage, out closeSeatLabel);

            // ⑥ 行模板（失活；运行时按条数克隆）
            layer.RowTemplate = BuildHistoryRow(contentRect, body, title);

            layer.Root = root;
            root.SetActive(false);
            return layer;
        }

        /// <summary>
        /// 一行 = 第一层「归属标签 + 载入 / 删除」+ 第二层 8 枚小牌。
        /// 两层是必须的：8 枚小牌横排已经 723 宽，挤不下第三样东西。
        /// </summary>
        private static DeckHistoryRowView BuildHistoryRow(Transform parent, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject rowGo = NewUi("HistoryRowTemplate", parent);
            Box(Rect(rowGo), Mid, Mid, Vector2.zero,
                new Vector2(DeckSelectView.HistoryRowWidth, DeckSelectView.HistoryRowHeight));

            // 行底板 = 外圈描边 + 内层深底（同 BuildSmallButton 的两层套路，28px 也不花）
            var frame = rowGo.AddComponent<Image>();
            frame.color = UiTheme.HistoryRowEdge;
            frame.raycastTarget = false;

            GameObject fillGo = NewUi("Fill", rowGo.transform);
            RectTransform fillRect = Rect(fillGo);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1.5f, 1.5f);
            fillRect.offsetMax = new Vector2(-1.5f, -1.5f);
            var bg = fillGo.AddComponent<Image>();
            bg.color = UiTheme.HistoryRow;
            bg.raycastTarget = false;

            // 左归属色条（蓝 = 玩家 / 橙 = AI，运行时换色）
            GameObject stripeGo = NewUi("Stripe", rowGo.transform);
            RectTransform stripeRect = Rect(stripeGo);
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.anchoredPosition = Vector2.zero;
            stripeRect.sizeDelta = new Vector2(4f, 0f);
            var stripe = stripeGo.AddComponent<Image>();
            stripe.color = UiTheme.PlayerAccent;
            stripe.raycastTarget = false;

            GameObject pillGo = NewUi("OwnerPill", rowGo.transform);
            Box(Rect(pillGo), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(12f, -16f),
                new Vector2(88f, 20f));
            var pill = pillGo.AddComponent<Image>();
            pill.color = UiTheme.WithAlpha(UiTheme.PlayerAccent, 0.16f);
            pill.raycastTarget = false;

            GameObject ownerGo = NewUi("OwnerLabel", pillGo.transform);
            Stretch(Rect(ownerGo));
            TMP_Text ownerLabel = AddText(ownerGo, body, 16f, UiTheme.PlayerAccent,
                TextAlignmentOptions.Center, "玩家");

            Button load = BuildSmallButton(rowGo.transform, "LoadButton", "载入",
                new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-12f, -16f), new Vector2(108f, 28f),
                UiTheme.HistoryLoadFill, UiTheme.HistoryLoadEdge, UiTheme.HistoryLoadText, body, 20f);

            Button remove = BuildSmallButton(rowGo.transform, "DeleteButton", "×",
                new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-128f, -16f), new Vector2(28f, 28f),
                UiTheme.OverlayPanel, UiTheme.PanelEdge, UiTheme.TextSecondary, title, 20f);

            var chips = new DeckChipView[BattleLaunch.DeckSize];
            for (int i = 0; i < BattleLaunch.DeckSize; i++)
            {
                float x = 6f + i * (DeckSelectView.HistoryChipWidth + DeckSelectView.HistoryChipGap);
                chips[i] = BuildHistoryChip(rowGo.transform, "Chip" + (i + 1), body, new Vector2(x, -31f));
            }

            var view = rowGo.AddComponent<DeckHistoryRowView>();
            var so = new SerializedObject(view);
            SetRef(so, "_stripe", stripe);
            SetRef(so, "_ownerPill", pill);
            SetRef(so, "_ownerLabel", ownerLabel);
            SetRef(so, "_loadButton", load);
            SetRef(so, "_deleteButton", remove);
            SetArray(so, "_chips", chips);
            so.ApplyModifiedPropertiesWithoutUndo();

            // ⚠ 模板必须失活：它在 Content 里也是 GridLayout 的一个「孩子」，
            //   忘了关就会自己占掉第一格、还挡住第一行的点击（2026-11-02 实测抓到）。
            rowGo.SetActive(false);

            return view;
        }

        /// <summary>一枚小牌：元素色块 22 + 符号 18 + 卡名 13（86×30，正好 8 枚一行）。</summary>
        private static DeckChipView BuildHistoryChip(Transform parent, string name, TMP_FontAsset body,
            Vector2 position)
        {
            GameObject chipGo = NewUi(name, parent);
            Box(Rect(chipGo), new Vector2(0f, 1f), new Vector2(0f, 1f), position,
                new Vector2(DeckSelectView.HistoryChipWidth, DeckSelectView.HistoryChipHeight));

            var bg = chipGo.AddComponent<Image>();
            bg.color = UiTheme.HistoryChip;
            bg.raycastTarget = false;

            GameObject badgeGo = NewUi("Badge", chipGo.transform);
            Box(Rect(badgeGo), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f),
                new Vector2(22f, 22f));
            var badge = badgeGo.AddComponent<Image>();
            badge.color = UiTheme.WithAlpha(UiTheme.ElementIce, 0.16f);
            badge.raycastTarget = false;

            GameObject iconGo = NewUi("Icon", chipGo.transform);
            Box(Rect(iconGo), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f),
                new Vector2(18f, 18f));
            var icon = iconGo.AddComponent<Image>();
            icon.color = Color.white;
            icon.raycastTarget = false;
            icon.enabled = false;

            GameObject labelGo = NewUi("Label", chipGo.transform);
            Box(Rect(labelGo), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(29f, 0f),
                new Vector2(54f, 26f));
            TMP_Text label = AddText(labelGo, body, 13f, UiTheme.HistoryChipName,
                TextAlignmentOptions.Left, string.Empty);
            label.enableAutoSizing = true;
            label.fontSizeMin = 9f;
            label.fontSizeMax = 13f;
            label.overflowMode = TextOverflowModes.Truncate;

            var view = chipGo.AddComponent<DeckChipView>();
            var so = new SerializedObject(view);
            SetRef(so, "_badge", badge);
            SetRef(so, "_icon", icon);
            SetRef(so, "_label", label);
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        /// <summary>
        /// 小号描边键（载入 / 删除 / 面板的 ×）：<b>外圈一层窄色边 + 内层底色</b>，不做九宫格。
        ///
        /// <para>为什么不用 <c>CardBox_Line.png</c>：那张图的九宫格边距是 38px（图 160×160），
        /// 铺到 28–108 宽的小键上边框比矩形还大，Unity 会直接把图拉花。
        /// 两层矩形没有任何尺寸限制，28×28 也干净。</para>
        /// </summary>
        private static Button BuildSmallButton(Transform parent, string name, string label, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size, Color fillColor, Color edgeColor,
            Color textColor, TMP_FontAsset font, float fontSize)
        {
            GameObject go = NewUi(name, parent);
            Box(Rect(go), anchor, pivot, position, size);

            var frame = go.AddComponent<Image>();
            frame.color = edgeColor;
            frame.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 1f);
            button.colors = colors;

            GameObject fillGo = NewUi("Fill", go.transform);
            RectTransform fillRect = Rect(fillGo);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1.5f, 1.5f);
            fillRect.offsetMax = new Vector2(-1.5f, -1.5f);
            var fill = fillGo.AddComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rect(labelGo));
            TMP_Text text = AddText(labelGo, font, fontSize, textColor, TextAlignmentOptions.Center, label);
            text.enableAutoSizing = true;
            text.fontSizeMin = fontSize * 0.6f;
            text.fontSizeMax = fontSize;

            return button;
        }

        /// <summary>历史牌座右上角的计数徽标（0 条时由视图关掉）。</summary>
        private static void BuildCountBadge(Transform seat, TMP_FontAsset font, out GameObject badge,
            out TMP_Text label)
        {
            GameObject go = NewUi("CountBadge", seat);
            Box(Rect(go), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, 6f),
                new Vector2(52f, 30f));
            go.transform.SetAsLastSibling();

            var img = go.AddComponent<Image>();
            img.color = UiTheme.HistoryBadge;
            img.raycastTarget = false;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rect(labelGo));
            label = AddText(labelGo, font, 18f, UiTheme.HistoryBadgeText, TextAlignmentOptions.Center, "0");

            go.SetActive(false);
            badge = go;
        }

        private static void BuildHistoryScrollbar(Transform panel, ScrollRect scroll)
        {
            GameObject barGo = NewUi("Scrollbar", panel);
            RectTransform barRect = Rect(barGo);
            barRect.anchorMin = new Vector2(1f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.anchoredPosition = new Vector2(-8f, -HistoryScrollTop);
            barRect.sizeDelta = new Vector2(6f, HistoryPanelHeight - HistoryScrollTop - HistoryScrollBottom);

            var track = barGo.AddComponent<Image>();
            track.color = new Color(1f, 1f, 1f, 0.10f);
            track.raycastTarget = true;

            var bar = barGo.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.transition = Selectable.Transition.None;

            GameObject slideGo = NewUi("SlidingArea", barGo.transform);
            Stretch(Rect(slideGo));

            GameObject handleGo = NewUi("Handle", slideGo.transform);
            Stretch(Rect(handleGo));
            var handle = handleGo.AddComponent<Image>();
            handle.color = new Color32(0x6B, 0x7D, 0x96, 0xFF);
            handle.raycastTarget = true;

            bar.handleRect = Rect(handleGo);
            bar.targetGraphic = handle;

            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        /// <summary>
        /// 中央一句话浮字（复用战斗里的 <see cref="FloatTipView"/>，同一套淡入 / 停留 / 上浮）。
        /// 放在提示行上方（y=-330）而不是正中央 —— 刚载入完，玩家正要核对那 8 格，别挡住。
        /// </summary>
        private static FloatTipView BuildToast(Transform canvas, TMP_FontAsset font)
        {
            GameObject go = NewUi("Toast", canvas);
            Box(Rect(go), Mid, Mid, new Vector2(0f, -330f), new Vector2(660f, 56f));

            var group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            GameObject bgGo = NewUi("Bg", go.transform);
            Stretch(Rect(bgGo));
            var bg = bgGo.AddComponent<Image>();
            bg.color = UiTheme.FloatTipBackdrop;
            bg.raycastTarget = false;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rect(labelGo));
            TMP_Text label = AddText(labelGo, font, 26f, UiTheme.FloatTipText,
                TextAlignmentOptions.Center, string.Empty);

            var toast = go.AddComponent<FloatTipView>();
            var so = new SerializedObject(toast);
            SetRef(so, "_group", group);
            SetRef(so, "_label", label);
            so.ApplyModifiedPropertiesWithoutUndo();

            return toast;
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

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

        /// <summary>只改尺寸（锚点已经是 Grid 的 cell 时用）。</summary>
        private static void RTCopy(RectTransform rect, Vector2 size)
        {
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

        private static void SetArray<T>(SerializedObject data, string field, T[] values) where T : Object
        {
            SerializedProperty array = data.FindProperty(field);
            if (array == null)
            {
                Debug.LogWarning("[D1] 找不到序列化数组：" + field);
                return;
            }

            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static string AbsolutePath(string assetPath)
        {
            return System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);
        }

        /// <summary>十六进制颜色的轻量替身（构建器里只用来填 Image.color，不需要 UnityEngine.Color 的静态方法）。</summary>
        private struct Color38
        {
            public readonly float R;
            public readonly float G;
            public readonly float B;

            public Color38(string hex)
            {
                R = Convert(hex, 0);
                G = Convert(hex, 2);
                B = Convert(hex, 4);
            }

            private static float Convert(string hex, int at)
            {
                int value = System.Convert.ToInt32(hex.Substring(at, 2), 16);
                return value / 255f;
            }

            public static implicit operator Color(Color38 c)
            {
                return new Color(c.R, c.G, c.B, 1f);
            }
        }
    }
}
