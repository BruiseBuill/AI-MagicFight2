using System;
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
    /// <b>P5 · 强化卡牌场景</b>的构建器（2026-09-30）。
    ///
    /// <para>菜单：<c>魔法乱斗/P5 · 构建 Upgrade 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>先调 <see cref="CardPoolBuilder.RunAll"/> 把三份场景卡池准备好（缺失才建，不覆盖）；</item>
    /// <item>打开 / 新建 <c>Assets/Scenes/Upgrade.unity</c>；</item>
    /// <item>建 <b>一个</b> <c>UpgradeCanvas</c>：背景 + 主角 + 石台命中区 + 标题 + 引导行 + 离开键
    /// + 选牌弹窗 + 强化动画；</item>
    /// <item>把 <c>UpgradeView</c> / <c>UpgradePickerView</c> / <c>UpgradePickerCell</c> /
    /// <c>UpgradeFxView</c> 的序列化字段一次性接好；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/UpgradeCanvas.prefab</c>；</item>
    /// <item>场景里额外挂一个 <c>UpgradeSceneEntry</c>（调试入口）—— <b>它不进 Prefab</b>。</item>
    /// </list>
    ///
    /// <para><b>⚠ 弹窗版式直接复用 <c>UiLayout.ShopBag*</c> 常量</b>（用户 2026-09-30 口径
    /// 「照商店背包那套」）：同一张 <c>Peek_Panel</c> 底图、同一套面板尺寸与网格参数、
    /// 同一个「关闭」键位置。本构建器只多两样东西 ——
    /// <b>底部正中的选中预览</b>与<b>右下角的「确认」</b>（「关闭」镜像到左下角）。</para>
    ///
    /// <para><b>⚠ 卡面只 instantiate、绝不新建第二份</b>（铁律 9）：弹窗里与动画里的卡都是
    /// <c>Assets/Prefabs/Ui/CardView_Hand.prefab</c> 的实例，尺寸由 <c>SetFaceWidth</c> 在运行时给。</para>
    /// </summary>
    public static class UpgradeUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/Upgrade.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/UpgradeCanvas.prefab";
        private const string CanvasName = "UpgradeCanvas";
        private const string HandCardPath = "Assets/Prefabs/Ui/CardView_Hand.prefab";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>弹窗底板 —— 与商店背包**同一张** <c>Peek_Panel</c>（992×447 九宫格）。</summary>
        private const string PanelSpritePath = "Assets/Art/Ui/Peek_Panel.png";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        /// <summary>卡面「高 / 宽」（组成式卡面画在固定设计空间里，外框矩形要按同比例给）。</summary>
        private static readonly float CardAspect = UiLayout.HandCardHeight / UiLayout.HandCardWidth;

        [MenuItem("魔法乱斗/P5 · 构建 Upgrade 场景", false, 41)]
        private static void MenuEntry()
        {
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Upgrade] ==== 开始 ====");

            if (EditorApplication.isPlaying)
            {
                log.AppendLine("✘ 请先退出 Play 再构建（Play 模式下改场景不会落盘）");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            log.Append(CardPoolBuilder.RunAll());      // 先把卡池准备好

            TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            TMP_FontAsset title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            if (body == null || title == null)
            {
                log.AppendLine("✘ 缺 TMP 字体：" + BodyFontPath + " / " + TitleFontPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.UpgradeBgSpritePath);
            Sprite panel = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            Sprite leave = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopLeaveSpritePath);
            Sprite confirm = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);
            Sprite glow = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.UpgradeFxGlowSpritePath);

            if (bg == null)
            {
                log.AppendLine("✘ 缺强化背景图：" + UiLayout.UpgradeBgSpritePath
                               + "（先跑 `魔法乱斗/整理 · 配置新美术导入`）");
            }

            if (panel == null)
            {
                log.AppendLine("✘ 缺弹窗底板：" + PanelSpritePath + "（会退化成纯色面板）");
            }

            if (leave == null)
            {
                log.AppendLine("✘ 缺「离开」按钮底图：" + UiLayout.ShopLeaveSpritePath);
            }

            if (confirm == null)
            {
                log.AppendLine("✘ 缺「确认」按钮底图：" + UiLayout.HandPickConfirmSpritePath);
            }

            if (glow == null)
            {
                log.AppendLine("✘ 缺强化光爆图：" + UiLayout.UpgradeFxGlowSpritePath
                               + "（动画里就没有光，其它照常）");
            }

            GameObject handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandCardPath);
            if (handPrefab == null)
            {
                log.AppendLine("✘ 缺卡面 prefab：" + HandCardPath);
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
            GameObject canvasGo = BuildCanvas(body, title, bg, panel, leave, confirm, glow, handPrefab, log);
            EnsureEventSystem();
            EnsureCamera(log);

            // 4) 存 Prefab（此刻场景里还没有调试入口）
            PrefabUtility.SaveAsPrefabAsset(canvasGo, PrefabPath);
            log.AppendLine("  已存 " + PrefabPath);

            // 5) 存场景
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            log.AppendLine("  已存 " + ScenePath);

            // 6) 调试入口（**存完 Prefab 之后再挂**，它天然不进 Prefab）
            GameObject debugEntry = BuildDebugEntry(canvasGo, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            if (debugEntry != null)
            {
                log.AppendLine("  调试入口：Scene 根下的 " + debugEntry.name
                               + "（卡池取自 Resources/Pools/UpgradePool，改 Inspector 可覆盖）");
            }

            log.AppendLine("[Upgrade] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ══════════════════════════════════════════════════════
        //  Canvas
        // ══════════════════════════════════════════════════════

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title, Sprite bg,
            Sprite panel, Sprite leave, Sprite confirm, Sprite glow, GameObject handPrefab, StringBuilder log)
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

            // ⚠ 底板垫在最底下，但强化背景是**不透明**的 —— 排在它后面就什么都看不见。
            //   背景图本身就铺满，所以只在缺图时补一块深色底。
            if (bg == null)
            {
                GameObject fallback = NewUi("Backdrop", canvasGo.transform);
                AddImage(fallback, UiTheme.Backdrop);
                Stretch(Rt(fallback));
            }

            BuildBackground(canvasGo.transform, bg, log);
            BuildHero(canvasGo.transform, log);

            // 石台命中区：整合场景**唯一**吃「点台面」这一下的东西。
            Button tableButton = BuildTableHit(canvasGo.transform, log);

            // 标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), Mid, Mid,
                new Vector2(0f, UiLayout.UpgradeTitleCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.UpgradeTitleWidth, UiLayout.UpgradeTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeUpgradeTitle,
                UiTheme.UpgradeTitleText, TextAlignmentOptions.Center, "强化卡牌");

            // 引导行（弹窗一打开就藏起来，见 UpgradeView.SetHintVisible）
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rt(hintGo), Mid, Mid,
                new Vector2(0f, UiLayout.UpgradeHintCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.UpgradeHintWidth, UiLayout.UpgradeHintHeight));
            TMP_Text hintText = AddText(hintGo, body, UiLayout.FontSizeUpgradeHint,
                UiTheme.UpgradeHintText, TextAlignmentOptions.Center,
                "点击台面，选择一张牌强化（基础力量 +2，上限 9）");

            // 离开键（只有它一个，所以右边距用 64 而不是给背包键让位的 176）
            Button leaveButton = BuildLeaveButton(canvasGo.transform, body, leave, out TMP_Text leaveLabel);

            // 选牌弹窗（盖在整层之上，默认失活）
            UpgradePickerView picker = BuildPicker(canvasGo.transform, body, title, panel, confirm,
                handPrefab, log);

            // 强化动画（默认失活）
            UpgradeFxView fx = BuildFx(canvasGo.transform, body, title, glow, handPrefab, log);

            // ── 接线 ─────────────────────────────────────────────
            var view = canvasGo.AddComponent<UpgradeView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_tableButton", tableButton);
            SetRef(serialized, "_leaveButton", leaveButton);
            SetRef(serialized, "_leaveLabel", leaveLabel);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_hint", hintGo);
            SetRef(serialized, "_hintText", hintText);
            SetRef(serialized, "_picker", picker);
            SetRef(serialized, "_fx", fx);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Vector2 tableSize = UiLayout.UpgradeTableSize();
            log.AppendLine("  Canvas OK：背景 " + (bg != null ? "OK" : "缺图")
                           + " · 主角 OK · 石台命中区 " + tableSize.x.ToString("0.#") + "×"
                           + tableSize.y.ToString("0.#")
                           + " · 弹窗 OK · 动画 OK · 离开键 " + (leave != null ? "OK" : "缺图"));
            return canvasGo;
        }

        /// <summary>背景按 cover 铺（与商店同一套算法）。</summary>
        private static void BuildBackground(Transform parent, Sprite bg, StringBuilder log)
        {
            if (bg == null)
            {
                return;
            }

            GameObject go = NewUi("Bg_Upgrade", parent);
            var img = go.AddComponent<Image>();
            img.sprite = bg;
            img.raycastTarget = false;
            img.preserveAspect = true;

            Vector2 size = UiLayout.UpgradeBgCoverSize();
            Box(Rt(go), Mid, Mid, Vector2.zero, size);
            log.AppendLine("  背景 cover " + size.x.ToString("0.#") + "×" + size.y.ToString("0.#")
                           + "（放大 " + (size.y / UiLayout.UpgradeBgNativeHeight).ToString("0.####") + "×）");
        }

        /// <summary>
        /// 主角（左下角，与 <c>Reference-4</c> 同一站位）。
        ///
        /// <para>素材吃战斗那份 <c>BattleArtLibrary.Hero.Idle</c>（待机只有 1 帧）——
        /// 与战斗 / 商店里是同一位主角、同一张图，不新增第二份美术。
        /// 缩放取 <see cref="UiLayout.UpgradeHeroScale"/>（= 商店那个 2.3，
        /// 是该素材的画质上限；参考图里那位巫师是高分辨率插画，照它的体量铺会糊）。</para>
        /// </summary>
        private static void BuildHero(Transform parent, StringBuilder log)
        {
            BattleArtLibrary art = BattleArtLibrary.Instance;
            BattleArtLibrary.CharacterSet hero = art != null ? art.Hero : null;
            BattleArtLibrary.Clip idle = hero != null ? hero.Idle : null;

            if (idle == null || !idle.IsValid || idle.Frames[0] == null)
            {
                log.AppendLine("✘ 主角待机素材缺失（BattleArtLibrary.Hero.Idle）→ 不建主角节点"
                               + "（先跑 `魔法乱斗/整理 · 配置新美术导入`）");
                return;
            }

            GameObject go = NewUi("Char_Hero_Upgrade", parent);
            var img = go.AddComponent<Image>();
            img.sprite = idle.Frames[0];
            img.raycastTarget = false;      // 纯立绘：不许挡到石台的射线

            float scale = UiLayout.UpgradeHeroScale;
            Vector2 size = new Vector2(idle.Size.x * scale, idle.Size.y * scale);

            // ⚠ 锚点必须是左下 (0,0)：UpgradeHeroX / UpgradeHeroGroundY 是画布绝对坐标。
            //   用 (0.5,0.5) 会先叠上画布中心 960×540，节点直接跑到屏幕外（本工程踩过两次）。
            // ⚠ pivot 用素材自带的（Clip.Pivot ≈ (0.5, 0.038)，定义的是「脚底中心」）。
            Box(Rt(go), Vector2.zero, idle.Pivot,
                new Vector2(UiLayout.UpgradeHeroX, UiLayout.UpgradeHeroGroundY), size);

            log.AppendLine("  主角：" + idle.Frames[0].name
                           + " " + size.x.ToString("0.#") + "×" + size.y.ToString("0.#")
                           + " @ (" + UiLayout.UpgradeHeroX.ToString("0.#") + ", "
                           + UiLayout.UpgradeHeroGroundY.ToString("0.#") + ")");
        }

        /// <summary>
        /// <b>石台命中区</b>：一整块透明的矩形（四角坐标对着背景图量，见
        /// <c>UiLayout.UpgradeTableBg*</c>），是场景里唯一吃「点台面」这一下的东西。
        ///
        /// <para><b>⚠ 为什么是一整块可见矩形而不是「按台子的形状」</b>：
        /// 台面是个带透视的斜四边形，用多边形命中要额外写一套 <c>ICanvasRaycastFilter</c>；
        /// 而用户 2026-09-30 的口径就是「整张石台（含木边）一块矩形命中区」——
        /// 方框比斜四边形更好点，多出来的四个角本来就是台子周围的空地。</para>
        /// </summary>
        private static Button BuildTableHit(Transform parent, StringBuilder log)
        {
            GameObject go = NewUi("TableHit", parent);

            Vector2 center = UiLayout.UpgradeTableCenter();
            Vector2 size = UiLayout.UpgradeTableSize();

            // center 是画布绝对坐标（左下原点）→ 换算成「相对画布中心」的 anchoredPosition。
            Box(Rt(go), Mid, Mid,
                new Vector2(center.x - UiLayout.ReferenceWidth * 0.5f,
                    center.y - UiLayout.ReferenceHeight * 0.5f),
                size);

            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);       // 完全透明，只是一块命中面
            img.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;

            log.AppendLine("  石台命中区：背景像素 (" + UiLayout.UpgradeTableBgLeft + ", "
                           + UiLayout.UpgradeTableBgTop + ")–(" + UiLayout.UpgradeTableBgRight + ", "
                           + UiLayout.UpgradeTableBgBottom + ") → 画布 center ("
                           + center.x.ToString("0.#") + ", " + center.y.ToString("0.#") + ")");
            return button;
        }

        /// <summary>「离开」按钮（底图自带「离开」两个字，只留一个空 Label 备用）。</summary>
        private static Button BuildLeaveButton(Transform parent, TMP_FontAsset body, Sprite sprite,
            out TMP_Text label)
        {
            GameObject go = NewUi("LeaveButton", parent);
            Box(Rt(go), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.UpgradeLeaveRight, UiLayout.UpgradeLeaveBottom),
                new Vector2(UiLayout.ShopLeaveWidth, UiLayout.ShopLeaveHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                // ⚠ Simple 而不是 Sliced —— 与商店那处同口径（用户在编辑器里手调过，
                //   底图自带木质高光与描边，九宫格 border 对不上会拉出接缝且零报错）。
                img.type = Image.Type.Simple;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo), 0f, 5f, 0f, 0f);       // 与商店那处同值（用户手调过）
            label = AddText(labelGo, body, UiLayout.FontSizeShopLeave,
                UiTheme.ShopLeaveText, TextAlignmentOptions.Center, "");

            return button;
        }

        // ══════════════════════════════════════════════════════
        //  选牌弹窗
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>选牌弹窗</b>。层级（幂等，整体失活进 Prefab）：
        /// <code>
        /// PickerLayer                  ← UpgradePickerView 在这里
        /// ├─ Veil                      铺满画布的黑纱，**同时是关闭区**
        /// └─ Panel                     Peek_Panel 九宫格（与商店背包同尺寸）
        ///    ├─ Title / Count / Preview
        ///    ├─ Cards                  滚动区（ScrollRect + 背板）
        ///    │  ├─ Viewport → Content  网格（GridLayoutGroup + ContentSizeFitter）
        ///    │  │              └─ CellTemplate（UpgradePickerCell，失活）
        ///    │  └─ Scrollbar
        ///    ├─ Empty                  一张牌都没有时的提示
        ///    ├─ CloseButton            左下「关闭」
        ///    └─ ConfirmButton          右下「确认」
        /// </code>
        ///
        /// <para>除「确认 / 预览」之外，所有坐标都直接取 <c>UiLayout.ShopBag*</c> ——
        /// 用户口径就是「照商店背包那套」，抄一份同值常量只会让以后两边分叉。</para>
        /// </summary>
        private static UpgradePickerView BuildPicker(Transform parent, TMP_FontAsset body,
            TMP_FontAsset title, Sprite panelSprite, Sprite confirmSprite, GameObject handPrefab,
            StringBuilder log)
        {
            GameObject layerGo = NewUi("PickerLayer", parent);
            Stretch(Rt(layerGo));

            // ① 遮罩（吃射线 + 点它取消）
            GameObject veilGo = NewUi("Veil", layerGo.transform);
            Stretch(Rt(veilGo));
            var veilImg = veilGo.AddComponent<Image>();
            veilImg.color = UiTheme.UpgradeVeil;
            veilImg.raycastTarget = true;
            var veilButton = veilGo.AddComponent<Button>();
            veilButton.targetGraphic = veilImg;
            veilButton.transition = Selectable.Transition.None;

            // ② 面板
            GameObject panelGo = NewUi("Panel", layerGo.transform);
            Box(Rt(panelGo), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.ShopBagPanelWidth, UiLayout.ShopBagPanelHeight));
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.sprite = panelSprite;
            panelImg.color = panelSprite != null ? Color.white : UiTheme.Panel;
            panelImg.raycastTarget = true;      // ⚠ 必须吃射线：否则点面板空白会穿到遮罩上把面板关掉
            if (panelSprite != null)
            {
                panelImg.type = Image.Type.Sliced;
                panelImg.pixelsPerUnitMultiplier = 1f;
            }

            // ③ 标题 + 计数
            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagTitleCenterY),
                new Vector2(UiLayout.ShopBagTitleWidth, UiLayout.ShopBagTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeShopBagTitle,
                UiTheme.UpgradeTitleColor, TextAlignmentOptions.Center, "强化卡牌");

            GameObject countGo = NewUi("Count", panelGo.transform);
            Box(Rt(countGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagCountCenterY),
                new Vector2(UiLayout.ShopBagCountWidth, UiLayout.ShopBagCountHeight));
            TMP_Text countText = AddText(countGo, body, UiLayout.FontSizeShopBagCount,
                UiTheme.UpgradeCountText, TextAlignmentOptions.Center, "卡池 0 张");

            // ④ 滚动网格
            GameObject scrollGo = NewUi("Cards", panelGo.transform);
            Box(Rt(scrollGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagScrollCenterY),
                new Vector2(UiLayout.ShopBagScrollWidth, UiLayout.ShopBagScrollHeight));
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = UiTheme.ShopBagGridBacking;
            scrollImg.raycastTarget = true;     // 滚动的拖拽靠它接

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            GameObject viewportGo = NewUi("Viewport", scrollGo.transform);
            Stretch(Rt(viewportGo));
            Rt(viewportGo).offsetMax = new Vector2(-UiLayout.ShopBagViewportRightInset, 0f);
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0f);
            viewportImg.raycastTarget = true;
            viewportGo.AddComponent<RectMask2D>();

            GameObject contentGo = NewUi("Content", viewportGo.transform);
            RectTransform contentRt = Rt(contentGo);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = Vector2.one;
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var grid = contentGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(UiLayout.ShopBagCellWidth, UiLayout.ShopBagCellHeight);
            grid.spacing = new Vector2(UiLayout.ShopBagGridSpacingX, UiLayout.ShopBagGridSpacingY);
            grid.padding = new RectOffset((int)UiLayout.ShopBagGridPadding, (int)UiLayout.ShopBagGridPadding,
                (int)UiLayout.ShopBagGridPadding, (int)UiLayout.ShopBagGridPadding);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = UiLayout.ShopBagColumns;
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = Rt(viewportGo);
            scroll.content = contentRt;

            // 滚动条
            GameObject barGo = NewUi("Scrollbar", scrollGo.transform);
            RectTransform barRt = Rt(barGo);
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = Vector2.one;
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-UiLayout.ShopBagScrollbarWidth, 8f);
            barRt.offsetMax = new Vector2(0f, -8f);
            var barBg = barGo.AddComponent<Image>();
            barBg.color = UiTheme.PanelEdge;
            barBg.raycastTarget = true;

            GameObject handleGo = NewUi("Handle", barGo.transform);
            Stretch(Rt(handleGo));
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = UiTheme.TextSecondary;
            handleImg.raycastTarget = true;

            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = Rt(handleGo);
            scrollbar.targetGraphic = handleImg;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;

            // ⑤ 格子模板
            UpgradePickerCell cell = BuildCellTemplate(contentGo.transform, handPrefab, body, log);

            // ⑥ 空提示
            GameObject emptyGo = NewUi("Empty", panelGo.transform);
            Box(Rt(emptyGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagScrollCenterY),
                new Vector2(UiLayout.ShopBagEmptyWidth, UiLayout.ShopBagEmptyHeight));
            AddText(emptyGo, body, UiLayout.FontSizeShopBagEmpty, UiTheme.ShopBagEmptyText,
                TextAlignmentOptions.Center, "卡池里还没有牌");
            emptyGo.SetActive(false);

            // ⑦ 「关闭」（左下，与「确认」镜像）
            float sideX = UiLayout.ShopBagPanelWidth * 0.5f - UiLayout.ShopBagCloseRightInset
                          - UiLayout.ShopBagCloseWidth * 0.5f;

            Button closeButton = BuildPanelButton(panelGo.transform, "CloseButton", title,
                UiTheme.UpgradeCloseText, new Vector2(-sideX, UiLayout.ShopBagCloseCenterY),
                confirmSprite, false, "关闭");

            // ⑧ 「确认」（右下）
            Button confirmButton = BuildPanelButton(panelGo.transform, "ConfirmButton", title,
                UiTheme.UpgradeConfirmOn, new Vector2(sideX, UiLayout.ShopBagCloseCenterY),
                confirmSprite, true, "确认");

            Image confirmImg = confirmButton.GetComponent<Image>();
            TMP_Text confirmLabel = confirmButton.transform.Find("Label") != null
                ? confirmButton.transform.Find("Label").GetComponent<TMP_Text>()
                : null;

            // ⑨ 选中预览（底部正中，夹在「关闭」与「确认」之间）
            GameObject previewGo = NewUi("Preview", panelGo.transform);
            Box(Rt(previewGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagCloseCenterY),
                new Vector2(UiLayout.ShopBagPanelWidth - (UiLayout.ShopBagCloseWidth
                            + UiLayout.ShopBagCloseRightInset) * 2f - 40f,
                    UiLayout.ShopBagCloseHeight));
            TMP_Text previewText = AddText(previewGo, body, 30f, UiTheme.UpgradeAccent,
                TextAlignmentOptions.Center, "点一张牌，看它强化后的样子");

            // ⑩ 接线
            var picker = layerGo.AddComponent<UpgradePickerView>();
            var serialized = new SerializedObject(picker);
            SetRef(serialized, "_veilButton", veilButton);
            SetRef(serialized, "_closeButton", closeButton);
            SetRef(serialized, "_confirmButton", confirmButton);
            SetRef(serialized, "_confirmImage", confirmImg);
            SetRef(serialized, "_confirmLabel", confirmLabel);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_count", countText);
            SetRef(serialized, "_preview", previewText);
            SetRef(serialized, "_empty", emptyGo);
            SetRef(serialized, "_scroll", scroll);
            SetRef(serialized, "_content", contentRt);
            SetRef(serialized, "_cellTemplate", cell);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // ⚠ 默认失活。**不要在 Awake 里 Hide**（首次 SetActive(true) 才触发 Awake，
            //   同一帧里的 Hide 会把刚显示的内容按灭）。
            layerGo.SetActive(false);

            log.AppendLine("  选牌弹窗：" + UiLayout.ShopBagColumns + " 列网格（格 "
                           + UiLayout.ShopBagCellWidth + "×" + UiLayout.ShopBagCellHeight
                           + "）+ 确认/关闭/预览 · 底图 "
                           + (panelSprite != null ? panelSprite.name : "缺图（纯色）"));
            return picker;
        }

        /// <summary>面板底部那两个按钮（结构一样，只有位置与文字不同）。</summary>
        private static Button BuildPanelButton(Transform panel, string name, TMP_FontAsset titleFont,
            Color labelColor, Vector2 position, Sprite sprite, bool isConfirm, string label)
        {
            GameObject go = NewUi(name, panel);
            Box(Rt(go), Mid, Mid, position,
                new Vector2(UiLayout.ShopBagCloseWidth, UiLayout.ShopBagCloseHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null
                ? (isConfirm ? UiTheme.UpgradeConfirmOff : Color.white)
                : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            if (isConfirm)
            {
                button.interactable = false;    // 一开始没选牌 → 确认键置灰
            }

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            AddText(labelGo, titleFont, UiLayout.FontSizeShopBagClose, labelColor,
                TextAlignmentOptions.Center, label);

            return button;
        }

        /// <summary>
        /// 弹窗里的一格模板（失活）。子节点顺序即绘制顺序：
        /// 卡面 → 压暗纱（含原因）→ 命中区。
        /// </summary>
        private static UpgradePickerCell BuildCellTemplate(Transform content, GameObject handPrefab,
            TMP_FontAsset body, StringBuilder log)
        {
            GameObject go = NewUi("CellTemplate", content);
            RectTransform rt = Rt(go);
            rt.anchorMin = Mid;
            rt.anchorMax = Mid;
            rt.pivot = Mid;
            rt.anchoredPosition = Vector2.zero;         // 位置由 GridLayoutGroup 给
            rt.sizeDelta = new Vector2(UiLayout.ShopBagCellWidth, UiLayout.ShopBagCellHeight);

            // ① 卡面（唯一那份 prefab 的实例）
            GameObject face = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, go.transform);
            face.name = "Card";
            Box(Rt(face), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.ShopBagCardFaceWidth,
                    UiLayout.ShopBagCardFaceWidth * CardAspect));
            CardView card = face.GetComponent<CardView>();
            if (card != null)
            {
                card.SetFaceWidth(UiLayout.ShopBagCardFaceWidth);
            }

            // ⚠ 卡面自带的交互必须全部关掉（与商店货位同一套理由）：
            //   ① CardInteractor 一按就 BeginDrag → 列表拖不动、点击还被它吃掉；
            //   ② Button 在抬起时派发 onClick，与格子的命中区叠成两次冒泡。
            foreach (Graphic g in face.GetComponentsInChildren<Graphic>(true))
            {
                g.raycastTarget = false;
            }

            foreach (Button b in face.GetComponentsInChildren<Button>(true))
            {
                b.enabled = false;
            }

            foreach (CardInteractor it in face.GetComponentsInChildren<CardInteractor>(true))
            {
                it.enabled = false;
            }

            // ② 压暗纱（不可强化时盖在卡面上）
            GameObject blocked = NewUi("Blocked", go.transform);
            Stretch(Rt(blocked));
            var blockedImg = blocked.AddComponent<Image>();
            blockedImg.color = UiTheme.UpgradeBlockedDim;
            blockedImg.raycastTarget = false;

            GameObject reasonGo = NewUi("Reason", blocked.transform);
            Box(Rt(reasonGo), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.ShopBagCellWidth - 24f, 120f));
            TMP_Text reason = AddText(reasonGo, body, 24f, UiTheme.UpgradeBlockedReason,
                TextAlignmentOptions.Center, "");
            reason.enableWordWrapping = true;
            blocked.SetActive(false);

            // ③ 命中区（透明，**唯一**吃射线的东西，排在最后 = 最上层）
            GameObject hit = NewUi("Hit", go.transform);
            Stretch(Rt(hit));
            var hitImg = hit.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;
            var hitButton = hit.AddComponent<Button>();
            hitButton.targetGraphic = hitImg;
            hitButton.transition = Selectable.Transition.None;

            var cell = go.AddComponent<UpgradePickerCell>();
            var serialized = new SerializedObject(cell);
            SetRef(serialized, "_card", card);
            SetRef(serialized, "_hit", hitButton);
            SetRef(serialized, "_blocked", blocked);
            SetRef(serialized, "_reason", reason);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            log.AppendLine("  格子模板：卡面(" + handPrefab.name + ") + 压暗纱(带原因) + 命中区");
            return cell;
        }

        // ══════════════════════════════════════════════════════
        //  强化动画
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>强化动画层</b>（默认失活）。层级（绘制顺序）：
        /// 光爆 → 卡面 → 说明字 → 大号力量读数。
        ///
        /// <para>光爆排在卡面<b>之前</b>（底下）：光从卡背后炸出来，
        /// 卡面才不会被一团橙色盖住；这也是「卡在光里换掉」这个读法的前提。</para>
        /// </summary>
        private static UpgradeFxView BuildFx(Transform parent, TMP_FontAsset body, TMP_FontAsset title,
            Sprite glow, GameObject handPrefab, StringBuilder log)
        {
            GameObject layerGo = NewUi("FxLayer", parent);
            Stretch(Rt(layerGo));
            var group = layerGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;       // 演出期间不吃射线

            float cardHeight = UiLayout.UpgradeFxCardWidth * CardAspect;

            // ① 光爆
            GameObject glowGo = NewUi("Glow", layerGo.transform);
            Box(Rt(glowGo), Mid, Mid,
                new Vector2(0f, UiLayout.UpgradeFxCardCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.UpgradeFxGlowSize, UiLayout.UpgradeFxGlowSize));
            var glowImg = glowGo.AddComponent<Image>();
            glowImg.sprite = glow;
            glowImg.color = UiTheme.UpgradeAccent;
            glowImg.raycastTarget = false;
            glowImg.preserveAspect = true;
            glowGo.SetActive(false);

            // ② 卡面
            GameObject slotGo = NewUi("CardSlot", layerGo.transform);
            Box(Rt(slotGo), Mid, Mid,
                new Vector2(0f, UiLayout.UpgradeFxCardCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.UpgradeFxCardWidth, cardHeight));

            GameObject face = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, slotGo.transform);
            face.name = "Card";
            Box(Rt(face), Mid, Mid, Vector2.zero, new Vector2(UiLayout.UpgradeFxCardWidth, cardHeight));
            CardView card = face.GetComponent<CardView>();
            if (card != null)
            {
                card.SetFaceWidth(UiLayout.UpgradeFxCardWidth);
            }

            foreach (Graphic g in face.GetComponentsInChildren<Graphic>(true))
            {
                g.raycastTarget = false;
            }

            foreach (Button b in face.GetComponentsInChildren<Button>(true))
            {
                b.enabled = false;
            }

            foreach (CardInteractor it in face.GetComponentsInChildren<CardInteractor>(true))
            {
                it.enabled = false;
            }

            // ③ 力量说明字 + 大号读数
            GameObject captionGo = NewUi("PowerCaption", layerGo.transform);
            Box(Rt(captionGo), Mid, Mid,
                new Vector2(0f, UiLayout.UpgradeFxPowerCenterY - 130f - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.UpgradeFxPowerWidth, 44f));
            AddText(captionGo, body, 34f, UiTheme.UpgradeFxCaption, TextAlignmentOptions.Center, "力量");

            GameObject powerGo = NewUi("Power", layerGo.transform);
            Box(Rt(powerGo), Mid, Mid,
                new Vector2(0f, UiLayout.UpgradeFxPowerCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.UpgradeFxPowerWidth, UiLayout.UpgradeFxPowerHeight));
            TMP_Text power = AddText(powerGo, title, UiLayout.FontSizeUpgradeFxPower,
                UiTheme.UpgradePowerOld, TextAlignmentOptions.Center, "");

            var fx = layerGo.AddComponent<UpgradeFxView>();
            var serialized = new SerializedObject(fx);
            SetRef(serialized, "_group", group);
            SetRef(serialized, "_cardSlot", Rt(slotGo));
            SetRef(serialized, "_card", card);
            SetRef(serialized, "_glow", glowImg);
            SetRef(serialized, "_power", power);
            SetRef(serialized, "_caption", captionGo.GetComponent<TMP_Text>());
            serialized.ApplyModifiedPropertiesWithoutUndo();

            layerGo.SetActive(false);
            log.AppendLine("  强化动画：" + UiLayout.UpgradeFxTotalDuration.ToString("0.00") + " s（"
                           + "卡宽 " + UiLayout.UpgradeFxCardWidth
                           + " · 光 " + (glow != null ? UiLayout.UpgradeFxGlowSize.ToString() : "缺图") + "）");
            return fx;
        }

        // ══════════════════════════════════════════════════════
        //  调试入口
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 场景里的调试入口（<see cref="UpgradeSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的<b>父级</b>（场景根）而不是 Canvas 上</b>：
        /// 存 Prefab 时只保存 Canvas 那一棵子树，挂在场景根就<b>天然不会进 Prefab</b> ——
        /// 不需要「存完再删」那套容易漏的做法。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            UpgradeSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<UpgradeSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("UpgradeSceneEntry");
            var entry = go.AddComponent<UpgradeSceneEntry>();

            UpgradeView view = canvasGo.GetComponent<UpgradeView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  调试入口 UpgradeSceneEntry（挂在场景根，不进 Prefab）");
            return go;
        }

        // ══════════════════════════════════════════════════════
        //  场景杂项
        // ══════════════════════════════════════════════════════

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

        /// <summary>资产路径 → 磁盘绝对路径（⚠ 必须补上 <c>Assets</c> 那一级，见商店构建器的注释）。</summary>
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
