using System;
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
    /// <b>P4 · 商店独立场景</b>的构建器（2026-09-26）。
    ///
    /// <para>菜单：<c>魔法乱斗/P4 · 构建 Shop 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>打开 / 新建 <c>Assets/Scenes/Shop.unity</c>；</item>
    /// <item>在场景里建 <b>一个</b> <c>ShopCanvas</c>（背景 + 顶栏 + 标题 + 货架 + 离开按钮）；</item>
    /// <item>把 <c>ShopView</c> / <c>ShopSlotView</c> 的序列化字段一次性接好；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/ShopCanvas.prefab</c>；</item>
    /// <item>场景里额外挂一个 <c>ShopSceneEntry</c>（调试入口）—— <b>它不进 Prefab</b>，见下。</item>
    /// </list>
    ///
    /// <para><b>⚠ 为什么顶栏与货位都要在场景里、而不能直接复用 <c>BattleCanvas.prefab</c></b>：
    /// 战斗的 Canvas 是整块挂在 <c>SampleScene</c> 上的，把它拉进商店场景等于把战斗 UI
    /// 连冷却列、角色动画、出血浮字一起带过来。这里只<b>照抄版式与节点名</b>
    /// （<c>Hud</c> / <c>Hud_Bar</c> 那条分支的参数与 <see cref="UiLayout.Hud*"/> 全一致），
    /// 复用是「同一套常量」，不是「同一个 prefab」。</para>
    ///
    /// <para><b>⚠ 卡面只 instantiate、绝不新建第二份</b>（铁律 9）：
    /// 货位里的卡是 <c>Assets/Prefabs/Ui/CardView_Hand.prefab</c> 的实例，
    /// 与手牌 / 冷却迷你卡 / 放大查看是<b>同一份源</b>，尺寸由 <c>SetFaceWidth</c> 在运行时给。</para>
    /// </summary>
    public static class ShopUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/Shop.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/ShopCanvas.prefab";
        private const string CanvasName = "ShopCanvas";
        private const string HandCardPath = "Assets/Prefabs/Ui/CardView_Hand.prefab";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>背包面板的底板 —— 复用 M25「查看对方手牌」那块 <c>Peek_Panel</c>（992×447）。
        ///
        /// <para>路径与 <c>SettingsUiBuilder</c> 里那一处同值：本工程的面板底图目前只有这一张，
        /// 再开一个 <c>UiLayout</c> 常量会变成两个都得维护。</para>
        /// </summary>
        private const string BagPanelSpritePath = "Assets/Art/Ui/Peek_Panel.png";

        /// <summary>居中锚点 + pivot 的简写（背包面板内部那批节点都是居中对齐的）。</summary>
        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        // ── 菜单入口 ─────────────────────────────────────────────────

        [MenuItem("魔法乱斗/P4 · 构建 Shop 场景", false, 40)]
        private static void MenuEntry()
        {
            // ⚠ 这里**故意不加** EditorUtility.DisplayDialog：
            //   带弹窗的菜单在脚本 / MCP 调用时会挂住主线程（构建器模式的老坑）。
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Shop] ==== 开始 ====");

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

            Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopBgSpritePath);
            Sprite pricePlate = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopPricePlateSpritePath);
            Sprite leaveSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopLeaveSpritePath);
            Sprite bagIcon = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopBagIconSpritePath);
            Sprite bagPanel = AssetDatabase.LoadAssetAtPath<Sprite>(BagPanelSpritePath);
            Sprite closeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);
            if (bg == null)
            {
                log.AppendLine("✘ 缺商店背景图：" + UiLayout.ShopBgSpritePath);
            }

            if (pricePlate == null)
            {
                log.AppendLine("✘ 缺价格牌底图：" + UiLayout.ShopPricePlateSpritePath
                               + "（先跑 Tools/art-audit/slice_shop_kit.py）");
            }

            if (leaveSprite == null)
            {
                log.AppendLine("✘ 缺「离开」按钮底图：" + UiLayout.ShopLeaveSpritePath);
            }

            if (bagIcon == null)
            {
                log.AppendLine("✘ 缺背包图标：" + UiLayout.ShopBagIconSpritePath
                               + "（同一支切图脚本会切出它）");
            }

            if (bagPanel == null)
            {
                log.AppendLine("✘ 缺背包面板底图：" + BagPanelSpritePath + "（会退化成纯色面板）");
            }

            if (closeSprite == null)
            {
                log.AppendLine("✘ 缺「关闭」按钮底图：" + UiLayout.HandPickConfirmSpritePath);
            }

            GameObject handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandCardPath);
            if (handPrefab == null)
            {
                log.AppendLine("✘ 缺卡面 prefab：" + HandCardPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            // 1) 场景：已经在 Shop.unity 就直接用，否则新建（并先存一次，免得后续
            //    SaveOpenScenes 把别人的场景也一起写了）。
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
            GameObject canvasGo = BuildCanvas(body, title, bg, pricePlate, leaveSprite, bagIcon,
                bagPanel, closeSprite, handPrefab, log);
            EnsureEventSystem();
            EnsureCamera(log);

            // 4) 存 Prefab（存之前先把调试入口摘掉 —— 见 BuildDebugEntry 的说明）
            GameObject debugEntry = BuildDebugEntry(canvasGo, log);
            PrefabUtility.SaveAsPrefabAsset(canvasGo, PrefabPath);
            log.AppendLine("  已存 " + PrefabPath);

            // 5) 存场景
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            log.AppendLine("  已存 " + ScenePath);

            if (debugEntry != null)
            {
                log.AppendLine("  调试入口：Scene 根下的 " + debugEntry.name
                               + "（金币 50 / 生命 4-4 · 改 Inspector 参数即可换货架）");
            }

            log.AppendLine("[Shop] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ══════════════════════════════════════════════════════
        //  Canvas
        // ══════════════════════════════════════════════════════

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title,
            Sprite bg, Sprite pricePlate, Sprite leaveSprite, Sprite bagIcon, Sprite bagPanel,
            Sprite closeSprite, GameObject handPrefab, StringBuilder log)
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

            // ⚠ 底板要垫在最底下：商店背景是**不透明**的，底板排在它后面就什么都看不见。
            //   这里干脆不给底板（背景图本身就铺满）—— 少了这一层，也就不存在顺序问题。
            //   背景图万一没找到才补一块深色底，免得整个场景是透明的一片。
            if (bg == null)
            {
                GameObject fallback = NewUi("Backdrop", canvasGo.transform);
                AddImage(fallback, UiTheme.Backdrop);
                Stretch(Rt(fallback));
            }

            BuildBackground(canvasGo.transform, bg, log);

            // 主角：**排在背景之后、货架之前** —— 他与第一列价格牌在纵向上有一点重叠，
            // 让货架画在他上面，「牌价压过主角的兜帽」读起来才是「他站在柜台前」。
            BuildHero(canvasGo.transform, log);

            // 顶栏：与战斗场景**同一套常量**，节点名也照抄，方便两边的截图对读。
            BuildHud(canvasGo.transform, body, title, log);

            // 标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(UiLayout.ShopTitleCenterX - UiLayout.ReferenceWidth * 0.5f,
                    UiLayout.ShopTitleCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.ShopTitleWidth, UiLayout.ShopTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeShopTitle,
                UiTheme.ShopTitleText, TextAlignmentOptions.Center, "商店");

            // 货架：一个**零尺寸的点**坐在整排正中，货位由 ShopView 运行时按模板复制
            GameObject shelf = NewUi("Shelf", canvasGo.transform);
            RectTransform shelfRt = Rt(shelf);
            shelfRt.anchorMin = new Vector2(0.5f, 0.5f);
            shelfRt.anchorMax = new Vector2(0.5f, 0.5f);
            shelfRt.pivot = new Vector2(0.5f, 0.5f);
            shelfRt.anchoredPosition = new Vector2(
                UiLayout.ShopShelfCenterX - UiLayout.ReferenceWidth * 0.5f,
                UiLayout.ShopCardCenterY - UiLayout.ReferenceHeight * 0.5f);
            shelfRt.sizeDelta = Vector2.zero;

            ShopSlotView slotTemplate = BuildSlotTemplate(shelf.transform, body, pricePlate, handPrefab, log);

            // 离开按钮
            Button leave = BuildLeaveButton(canvasGo.transform, body, leaveSprite, out TMP_Text leaveLabel);

            // 背包键（右下角最右一格，在「离开」右边）
            Button bagButton = BuildBagButton(canvasGo.transform, bagIcon, log);

            // 背包面板（盖在最上层，默认失活）
            ShopBagView bag = BuildBagPanel(canvasGo.transform, body, title, bagPanel,
                closeSprite, handPrefab, log);

            // ── 接线（一次性把 ShopView 的字段全填好）──────────────
            var view = canvasGo.AddComponent<ShopView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_leaveButton", leave);
            SetRef(serialized, "_leaveLabel", leaveLabel);
            SetRef(serialized, "_bagButton", bagButton);
            SetRef(serialized, "_bag", bag);
            SetRef(serialized, "_hudName", FindDeep(canvasGo.transform, "Name")?.GetComponent<TMP_Text>());
            SetRef(serialized, "_hudHp", FindDeep(canvasGo.transform, "HpNumber")?.GetComponent<TMP_Text>());
            SetRef(serialized, "_hudGold", FindDeep(canvasGo.transform, "ResNumber")?.GetComponent<TMP_Text>());
            SetRef(serialized, "_slotTemplate", slotTemplate);
            SetRef(serialized, "_slotRoot", shelfRt);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  Canvas OK：背景 " + (bg != null ? "OK" : "缺图（用深色底）")
                           + " · 顶栏 OK · 货位模板 OK（含 "
                           + handPrefab.name + " 实例）"
                           + " · 离开按钮 " + (leaveSprite != null ? "OK" : "缺图")
                           + " · 背包键 " + (bagIcon != null ? "OK" : "缺图")
                           + " · 背包面板 " + (bagPanel != null ? "OK" : "缺图"));
            return canvasGo;
        }

        /// <summary>
        /// 背景按 <b>cover</b> 铺（与战斗场景 <c>Bg_Dungeon</c> 同一套算法，见
        /// <see cref="UiLayout.ShopBgCoverSize"/>）。直接 Stretch 会把 1664×928（1.7931）
        /// 拉成 16:9（1.7778），横向被压 0.86%，虽然肉眼看不太出来，
        /// 但**版式是照着这张图量的**，压过的图会让量与实物对不上。
        /// </summary>
        private static void BuildBackground(Transform parent, Sprite bg, StringBuilder log)
        {
            if (bg == null)
            {
                return;
            }

            GameObject go = NewUi("Bg_Shop", parent);
            var img = go.AddComponent<Image>();
            img.sprite = bg;
            img.raycastTarget = false;      // 背景不吃射线，免得挡在货位下面
            img.preserveAspect = true;

            Vector2 size = UiLayout.ShopBgCoverSize();
            Box(Rt(go), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            log.AppendLine("  背景 cover " + size.x.ToString("0.#") + "×" + size.y.ToString("0.#"));
        }

        /// <summary>
        /// <b>主角</b>（站在柜台左前方，2026-09-26）。
        ///
        /// <para>素材直接吃战斗那份 <c>BattleArtLibrary.Hero.Idle</c>（待机只有 1 帧）——
        /// 与战斗里是同一位主角、同一张图，不新增第二份美术。</para>
        ///
        /// <para><b>为什么是一张 <see cref="Image"/> 而不是 <c>CharacterView</c> / <c>SpriteAnimator</c></b>：
        /// 待机素材只有 1 帧，这里要的就是「一张静态立绘摆在该在的位置」。
        /// 尺寸与锚点由构建期一次算好写进 Prefab（<c>sizeDelta = 素材画布 × ShopHeroScale</c>、
        /// <c>pivot = 素材自带的脚底锚点</c>），比挂一个只会写一次值的组件更少出错 ——
        /// 组件那套 <c>_clip</c> / <c>_scale</c> 是<b>非序列化</b>字段，重开工程就归零，
        /// 万一哪天有人给它加个 <c>OnEnable</c>，静态立绘会当场消失且零报错。</para>
        ///
        /// <para><b>⚠ 锚点必须是左下 <c>(0,0)</c></b>：<c>ShopHeroX</c> / <c>ShopHeroGroundY</c>
        /// 是画布绝对坐标（与 <c>Char*</c> 那批同口径）。用 <c>(0.5,0.5)</c> 会先叠上画布中心 960 × 540，
        /// 节点直接跑到屏幕外 —— 本工程在 M41 与「点击怪物」两处各踩过一次。</para>
        ///
        /// <para><b>⚠ <c>pivot</c> 用素材自带的那个</b>（<c>Clip.Pivot</c>，≈ (0.5, 0.038)）：
        /// 它定义的是「脚底中心」。直接用 (0.5, 0.5) 会让角色的一半身体沉到地面以下。</para>
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

            GameObject go = NewUi("Char_Hero_Shop", parent);
            var img = go.AddComponent<Image>();
            img.sprite = idle.Frames[0];
            img.raycastTarget = false;      // 纯立绘：不许挡到货位的射线

            float scale = UiLayout.ShopHeroScale;
            Vector2 size = new Vector2(idle.Size.x * scale, idle.Size.y * scale);

            Box(Rt(go), Vector2.zero, idle.Pivot,
                new Vector2(UiLayout.ShopHeroX, UiLayout.ShopHeroGroundY), size);

            log.AppendLine("  主角：" + idle.Frames[0].name
                           + " " + size.x.ToString("0.#") + "×" + size.y.ToString("0.#")
                           + " @ (" + UiLayout.ShopHeroX.ToString("0.#") + ", "
                           + UiLayout.ShopHeroGroundY.ToString("0.#") + ")"
                           + " · pivot=" + idle.Pivot.y.ToString("0.###"));
        }

        /// <summary>
        /// 顶栏 —— <b>节点名与参数逐条对账战斗场景的 <c>ArtLayer/Hud</c></b>
        /// （<c>Hud / Bar / Name / Subtitle / HpNumber / ResNumber / GearButton</c>）。
        ///
        /// <para><b>口径（用户 2026-09-26）</b>：商店的 💰 位显示<b>金币</b>（不是战斗里的「未用光环数」）；
        /// 条底直接用战斗美术的唯一取图入口 <see cref="BattleArtLibrary.HudBar"/>。
        /// 但这里<b>不挂 <see cref="HudView"/></b> —— 那个类整条逻辑都是为战斗写的
        /// （<c>Bind(PlayerSnapshot, …)</c> 会把 💰 刷成光环数、还会给能量球做脉冲动画）。
        /// 商店自己只需要三行数字，字段直接接进 <see cref="ShopView"/>。</para>
        ///
        /// <para><b>⚠ 锚点是左上角口径</b>（<see cref="UiLayout.HudLeft"/> / <c>HudTop</c> /
        /// <c>Hud*X</c> / <c>Hud*Y</c>），别跟货位那批左下角口径混（铁律 10）。</para>
        /// </summary>
        private static void BuildHud(Transform parent, TMP_FontAsset body, TMP_FontAsset title,
            StringBuilder log)
        {
            GameObject hudGo = NewUi("Hud", parent);
            RectTransform hudRoot = Rt(hudGo);
            Box(hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudLeft, -UiLayout.HudTop),
                new Vector2(UiLayout.HudBarWidth, UiLayout.HudBarHeight));

            // 条底：**直接取战斗场景那份 `Hud_Bar` 美术**
            // （BattleArtLibrary 是战斗美术的唯一取图入口，`Instance` = Resources.Load）。
            // 这样商店顶栏与战斗顶栏是同一张图、同一套 `UiLayout.Hud*` 文字位置 ——
            // 也就是用户要的「参考战斗场景的 reference」，而不是我另画一条长得像的板。
            // 取不到图时退化成主题色纯板（不报错，只是难看）。
            BattleArtLibrary art = BattleArtLibrary.Instance;
            Sprite hudBarSprite = art != null ? art.HudBar : null;

            GameObject barGo = NewUi("Hud_Bar", hudRoot);
            Stretch(Rt(barGo));
            var barImg = barGo.AddComponent<Image>();
            barImg.sprite = hudBarSprite;
            barImg.color = hudBarSprite != null ? Color.white : UiTheme.Panel;
            barImg.raycastTarget = false;

            // 名字（左上大字）
            GameObject nameGo = NewUi("Name", hudRoot);
            Box(Rt(nameGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudNameX, -UiLayout.HudNameY),
                new Vector2(UiLayout.HudNameWidth, UiLayout.HudNameHeight));
            AddText(nameGo, title, UiLayout.FontSizeHudName, UiTheme.TextPrimary,
                TextAlignmentOptions.Left, "旅者");

            // 副标题：战斗里是「手牌 ×N」，商店没有手牌 → 写死一行静态说明。
            GameObject subGo = NewUi("Subtitle", hudRoot);
            Box(Rt(subGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudSubX, -UiLayout.HudSubY),
                new Vector2(UiLayout.HudSubWidth, UiLayout.HudSubHeight));
            AddText(subGo, body, UiLayout.FontSizeHudSub, UiTheme.TextSecondary,
                TextAlignmentOptions.Left, "商店");

            // ❤ 生命
            GameObject hpGo = NewUi("HpNumber", hudRoot);
            Box(Rt(hpGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudHpX, -UiLayout.HudHpY),
                new Vector2(UiLayout.HudHpWidth, UiLayout.HudHpHeight));
            AddText(hpGo, body, UiLayout.FontSizeHudHp, UiTheme.TextPrimary,
                TextAlignmentOptions.Left, "4/4");

            // ⚠ 节点名仍叫 ResNumber：**位置就是战斗 💰 那一格**，口径改成金币。
            //   改名会让「两边截图对读」时对不上，所以名字保留、语义记在这里。
            GameObject resGo = NewUi("ResNumber", hudRoot);
            Box(Rt(resGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudResX, -UiLayout.HudResY),
                new Vector2(UiLayout.HudResWidth, UiLayout.HudResHeight));
            AddText(resGo, body, UiLayout.FontSizeHudRes, UiTheme.AuraReady,
                TextAlignmentOptions.Left, "50");

            log.AppendLine("  顶栏：Hud_Bar" + (hudBarSprite != null ? "（战斗美术）" : "（缺图·纯色板）")
                           + " + Name / Subtitle / HpNumber / ResNumber(=金币)");
        }

        /// <summary>
        /// <b>一个货位</b>的失活模板。子节点顺序即绘制顺序（越靠后越上层）：
        /// 卡面 → 压暗纱 → 价格牌 → 「卖光了」→ 命中区。
        /// </summary>
        private static ShopSlotView BuildSlotTemplate(Transform shelf, TMP_FontAsset body,
            Sprite pricePlate, GameObject handPrefab, StringBuilder log)
        {
            // 圆角白图（工程第三方素材库）—— 只是给「卖光了」当底板，取不到就退化成无底板。
            Sprite soldOutPlateSprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopSoldOutPlateSpritePath);

            GameObject go = NewUi("ShopSlotTemplate", shelf);
            RectTransform rt = Rt(go);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;         // 位置由 ShopView.EnsureSlots 按序号给
            rt.sizeDelta = new Vector2(UiLayout.ShopCardWidth, UiLayout.ShopCardHeight);

            // ① 卡面（唯一那份 prefab 的实例）
            GameObject face = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, go.transform);
            face.name = "Card";
            Box(Rt(face), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.ShopCardWidth, UiLayout.ShopCardHeight));
            CardView card = face.GetComponent<CardView>();
            if (card != null)
            {
                card.SetFaceWidth(UiLayout.ShopCardWidth);
            }

            // ⚠⚠ 卡面自带的交互必须**全部关掉**，否则有两种「零报错」的坏事：
            //   ① `CardInteractor` 一按就 BeginDrag（卡是 Draggable 的），但 Host 没接 →
            //      拖起来卡跟着指针跑、放下又弹回，而且**点击被它吃掉**；
            //   ② `Button` 在抬起时派发 onClick，加上货位自己的 `IPointerClickHandler`
            //      → 一次点击冒泡两遍（这里没有监听所以不报错，但将来接了购买就会买两次）。
            //   货位的点击一律由 `ShopSlotView.OnPointerClick` 统一接。
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

            // ② 压暗纱（买不起时盖在卡面上）
            GameObject dim = NewUi("Dim", go.transform);
            Stretch(Rt(dim));
            var dimImg = dim.AddComponent<Image>();
            dimImg.color = UiTheme.ShopSlotDim;
            dimImg.raycastTarget = false;
            dim.SetActive(false);

            // ③ 价格牌（挂在卡面正下方）
            GameObject plate = NewUi("PricePlate", go.transform);
            Box(Rt(plate), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -UiLayout.ShopPricePlateDrop),
                new Vector2(UiLayout.ShopPricePlateWidth, UiLayout.ShopPricePlateHeight));
            var plateImg = plate.AddComponent<Image>();
            plateImg.sprite = pricePlate;
            plateImg.color = Color.white;
            plateImg.raycastTarget = false;
            if (pricePlate != null)
            {
                // 六边形两端不能被拉变形 —— border 与切图尺寸相符才不会出现「圆角糊掉」且零报错。
                plateImg.type = Image.Type.Sliced;
                plateImg.pixelsPerUnitMultiplier = 1f;
            }

            GameObject priceGo = NewUi("PriceText", plate.transform);
            Box(Rt(priceGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(UiLayout.ShopPriceTextOffsetX, UiLayout.ShopPriceTextOffsetY),
                new Vector2(UiLayout.ShopPriceTextWidth, UiLayout.ShopPriceTextHeight));
            TMP_Text priceText = AddText(priceGo, body, UiLayout.FontSizeShopPrice,
                UiTheme.ShopPriceText, TextAlignmentOptions.Left, "20");

            // ④ 「卖光了」（与卡面 / 价格牌互斥显示）
            //
            //    ⚠ 空位**不是一张新卡**，但也**不能只有一行灰字**：
            //    空槽背后是商店的浅色木质内景，灰字直接浮上去几乎读不清（2026-09-26 首次落版实测）。
            //    给一块「比卡面小一圈的暗色圆角剪影」把字托起来：文字立住了，
            //    那一格也读作「槽位还在、只是空了」，而且与左右卡面同宽同高、整排节奏不塌。
            GameObject soldOut = NewUi("SoldOut", go.transform);
            Box(Rt(soldOut), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.ShopCardWidth, UiLayout.ShopCardHeight));

            Vector2 plateSize = new Vector2(
                UiLayout.ShopCardWidth - UiLayout.ShopSoldOutPlateInset * 2f,
                UiLayout.ShopCardHeight - UiLayout.ShopSoldOutPlateInset * 2f);

            GameObject soldOutPlate = NewUi("Plate", soldOut.transform);
            Box(Rt(soldOutPlate), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, plateSize);
            var soldOutPlateImg = soldOutPlate.AddComponent<Image>();
            soldOutPlateImg.sprite = soldOutPlateSprite;
            soldOutPlateImg.color = UiTheme.ShopSoldOutPlate;
            soldOutPlateImg.raycastTarget = false;

            // ⚠ 用 Simple 而不是 Sliced —— 理由见 UiLayout.ShopSoldOutPlateSpritePath 的注释
            //   （那张图的 border 是 0，而且被战斗的光环图标共用，不能改）。

            GameObject soldOutTextGo = NewUi("Text", soldOut.transform);
            Box(Rt(soldOutTextGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, plateSize);
            TMP_Text soldOutText = AddText(soldOutTextGo, body, UiLayout.FontSizeShopSoldOut,
                UiTheme.ShopSoldOutText, TextAlignmentOptions.Center, "卖光了");
            soldOut.SetActive(false);

            // ⑤ 命中区（透明，**唯一**吃射线的东西）
            //
            //    ⚠ 它比卡面大一圈：向下一直包到价格牌底边（见 UiLayout.ShopSlotHitHeight）。
            //      价格牌挂在卡面**下方**，只覆盖卡面的命中区收不到点它的指针 ——
            //      而玩家把那块木质六边形读作「购买键」（2026-09-27 用户要求：
            //      点购买键与点卡面都要能买）。扩大命中区就等于把价格牌也变成购买键，
            //      而且不用给价格牌开 raycastTarget、不必多接一套事件。
            GameObject hit = NewUi("Hit", go.transform);
            Box(Rt(hit), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, UiLayout.ShopSlotHitCenterY),
                new Vector2(UiLayout.ShopSlotHitWidth, UiLayout.ShopSlotHitHeight));
            var hitImg = hit.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;

            var slot = go.AddComponent<ShopSlotView>();
            var serialized = new SerializedObject(slot);
            SetRef(serialized, "_card", card);
            SetRef(serialized, "_pricePlate", plate);
            SetRef(serialized, "_priceText", priceText);
            SetRef(serialized, "_soldOut", soldOut);
            SetRef(serialized, "_soldOutText", soldOutText);
            SetRef(serialized, "_dim", dim);
            SetRef(serialized, "_hit", hitImg);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            log.AppendLine("  货位模板：卡面(" + handPrefab.name + ") + 价格牌"
                           + (pricePlate != null ? "(" + pricePlate.name + ")" : "(缺图)")
                           + " + 卖光了" + (soldOutPlateSprite != null ? "(带暗底板)" : "(无底板)")
                           + " + 压暗纱 + 命中区");
            return slot;
        }

        /// <summary>
        /// 「离开」按钮。<b>底图里已经烘着「离开」两个字</b>（切图时没擦 —— 那是固定文案），
        /// 所以这里不再叠一层 TMP，只留一个空 Label 供 <c>SetLeaveLabel</c> 备用（默认空）。
        /// </summary>
        private static Button BuildLeaveButton(Transform parent, TMP_FontAsset body,
            Sprite sprite, out TMP_Text label)
        {
            GameObject go = NewUi("LeaveButton", parent);
            Box(Rt(go), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.ShopLeaveRight, UiLayout.ShopLeaveBottom),
                new Vector2(UiLayout.ShopLeaveWidth, UiLayout.ShopLeaveHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                // ⚠ 2026-09-27：由 Sliced 改成 Simple —— **这是用户在编辑器里手调的**，
                //   不是构建器的决定。这张底图自带木质高光与描边，九宫格一旦与源图的
                //   实际 border 对不上，斜边与描边就会拉出接缝（而且零报错）。
                //   写回这里只为让「下次重跑构建器」不会把用户的调整改回去。
                //   ⚠ UiLayout.ShopLeaveBorderX / ShopLeaveBorderY 随之失效（Simple 不读 border），
                //     保留它们是为了将来想换回九宫格时还有一组量过的值。
                img.type = Image.Type.Simple;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            // 底图自带高光与描边，再做颜色过渡会跟它的木质感打架 → 关掉过渡。
            button.transition = Selectable.Transition.None;

            GameObject labelGo = NewUi("Label", go.transform);
            // ⚠ 2026-09-27：原来是 Stretch（四边全铺，sizeDelta 恒为 0）。
            //   用户在编辑器里把 Label 的下边收进去 5（sizeDelta.y = −5、
            //   anchoredPosition.y = +2.5），文字因此往上抬一点。
            //   **这是他手调的位置，不要改回去** —— 写在这里是为了让
            //   「下次重跑构建器」仍然得到同一组值，而不是回到全铺。
            Stretch(Rt(labelGo), 0f, 5f, 0f, 0f);
            label = AddText(labelGo, body, UiLayout.FontSizeShopLeave,
                UiTheme.ShopLeaveText, TextAlignmentOptions.Center, "");

            return button;
        }

        /// <summary>
        /// <b>背包键</b>（右下角最右一格，2026-09-26）。
        ///
        /// <para>底图 <c>Shop_BagIcon.png</c>（144×135）是<b>整块六边形按钮 + 背包图形</b>，
        /// 图形已经烘在图上，所以这里不叠 TMP —— 与「离开」同一个做法。</para>
        ///
        /// <para><b>⚠ <c>transition = None</c> 不是可选项</b>：按钮底图自带木质描边与高光，
        /// 再叠一层颜色过渡会跟它打架；更要紧的是 <c>ColorTint</c> 会把
        /// <c>targetGraphic</c> 的<b>常态色</b>刷上去 —— 底图会被整块改成单色，
        /// 六边形与背包一起消失（本工程在「离开」上已经定过这条口径）。</para>
        /// </summary>
        private static Button BuildBagButton(Transform parent, Sprite icon, StringBuilder log)
        {
            GameObject go = NewUi("BagButton", parent);
            Box(Rt(go), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.ShopBagButtonRight, UiLayout.ShopBagButtonBottom),
                new Vector2(UiLayout.ShopBagButtonWidth, UiLayout.ShopBagButtonHeight));

            var img = go.AddComponent<Image>();
            img.sprite = icon;
            img.color = icon != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;

            // ⚠ 用 Simple 不用 Sliced：这张图的 spriteBorder 是 0，
            //   而且显示尺寸 92×86 与素材 144×135 的比例只差 0.3%，铺开看不出变形。
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;

            log.AppendLine("  背包键：" + (icon != null ? icon.name : "缺图（纯色块）")
                           + " " + UiLayout.ShopBagButtonWidth + "×" + UiLayout.ShopBagButtonHeight
                           + " @ 右边距 " + UiLayout.ShopBagButtonRight);
            return button;
        }

        /// <summary>
        /// <b>背包面板</b>（点背包键弹出，看主角当前卡池）。
        ///
        /// <para>节点层级（幂等，整体失活进 Prefab）：</para>
        /// <code>
        /// BagLayer                     ← ShopBagView 就在这里
        /// ├─ Veil                      铺满画布的黑纱，**同时是关闭区**
        /// └─ Panel                     Peek_Panel 九宫格
        ///    ├─ Title / Count          「背包 · 当前卡池」/「N 张」
        ///    ├─ Cards                  滚动区（ScrollRect + 背板）
        ///    │  ├─ Viewport → Content  网格（GridLayoutGroup + ContentSizeFitter）
        ///    │  │              └─ BagCardTemplate（CardView_Hand 实例，失活）
        ///    │  └─ Scrollbar
        ///    ├─ Empty                  一张牌都没有时的提示
        ///    └─ CloseButton            「关闭」
        /// </code>
        ///
        /// <para><b>⚠ 面板那一层的 <c>raycastTarget</c> 必须是 true</b>：遮罩是「点哪儿关哪儿」，
        /// 面板若不吃射线，点面板内部的空白就会穿到遮罩上，伸手去点「关闭」的途中
        /// 面板先自己关了 —— 而且不会有任何报错。</para>
        ///
        /// <para><b>⚠ 卡面子树自带的交互要全部关掉</b>（与货位同一套理由）：
        /// <c>Button</c> / <c>CardInteractor</c> / 所有 <c>Graphic.raycastTarget</c> 一律关，
        /// 否则① 卡面会吃掉滚动的拖拽（列表拖不动），② 一次点击冒泡两遍。</para>
        /// </summary>
        private static ShopBagView BuildBagPanel(Transform parent, TMP_FontAsset body,
            TMP_FontAsset title, Sprite panelSprite, Sprite closeSprite, GameObject handPrefab,
            StringBuilder log)
        {
            GameObject layerGo = NewUi("BagLayer", parent);
            Stretch(Rt(layerGo));

            // ① 遮罩（吃射线 + 点它关闭）
            GameObject veilGo = NewUi("Veil", layerGo.transform);
            Stretch(Rt(veilGo));
            var veilImg = veilGo.AddComponent<Image>();
            veilImg.color = UiTheme.ShopBagVeil;
            veilImg.raycastTarget = true;
            var veilButton = veilGo.AddComponent<Button>();
            veilButton.targetGraphic = veilImg;
            veilButton.transition = Selectable.Transition.None;

            // ② 面板
            GameObject panelGo = NewUi("Panel", layerGo.transform);
            Box(Rt(panelGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.ShopBagPanelWidth, UiLayout.ShopBagPanelHeight));
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.sprite = panelSprite;
            panelImg.color = panelSprite != null ? Color.white : UiTheme.Panel;
            panelImg.raycastTarget = true;
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
                UiTheme.ShopBagTitleText, TextAlignmentOptions.Center, "背包 · 当前卡池");

            GameObject countGo = NewUi("Count", panelGo.transform);
            Box(Rt(countGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagCountCenterY),
                new Vector2(UiLayout.ShopBagCountWidth, UiLayout.ShopBagCountHeight));
            TMP_Text countText = AddText(countGo, body, UiLayout.FontSizeShopBagCount,
                UiTheme.ShopBagCountText, TextAlignmentOptions.Center, "0 张");

            // ④ 滚动网格
            GameObject scrollGo = NewUi("Cards", panelGo.transform);
            Box(Rt(scrollGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagScrollCenterY),
                new Vector2(UiLayout.ShopBagScrollWidth, UiLayout.ShopBagScrollHeight));
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = UiTheme.ShopBagGridBacking;
            scrollImg.raycastTarget = true;     // 滚动的拖拽靠它接（卡面一律不吃射线）

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

            // 滚动条（长卡池必需；卡池短的时候它自己会缩成一条）
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

            // ⑤ 卡面模板（唯一那份 CardView_Hand 的实例，失活）
            GameObject face = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, contentGo.transform);
            face.name = "BagCardTemplate";
            // ⚠ 高度必须用手牌口径 HandCardHeight，**不能**用 ShopCardHeight：
            //   货架的卡在 2026-09-27 放大到了 1.1 倍，背包里的仍是手牌尺寸 ——
            //   宽 196 / 高 299.2 会把背包里的卡片纵向拉长（卡面视觉由 SetFaceWidth 缩，
            //   但外框矩形会跟 GridLayout 的单元格对不上）。
            Box(Rt(face), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.ShopBagCardFaceWidth, UiLayout.HandCardHeight));
            CardView card = face.GetComponent<CardView>();
            if (card != null)
            {
                card.SetFaceWidth(UiLayout.ShopBagCardFaceWidth);
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

            face.SetActive(false);

            // ⑥ 空提示（一张牌都没有时）
            GameObject emptyGo = NewUi("Empty", panelGo.transform);
            Box(Rt(emptyGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagScrollCenterY),
                new Vector2(UiLayout.ShopBagEmptyWidth, UiLayout.ShopBagEmptyHeight));
            AddText(emptyGo, body, UiLayout.FontSizeShopBagEmpty, UiTheme.ShopBagEmptyText,
                TextAlignmentOptions.Center, "背包里还没有牌");
            emptyGo.SetActive(false);

            // ⑦ 「关闭」
            GameObject closeGo = NewUi("CloseButton", panelGo.transform);
            Box(Rt(closeGo), Mid, Mid,
                new Vector2(UiLayout.ShopBagPanelWidth * 0.5f - UiLayout.ShopBagCloseRightInset
                            - UiLayout.ShopBagCloseWidth * 0.5f, UiLayout.ShopBagCloseCenterY),
                new Vector2(UiLayout.ShopBagCloseWidth, UiLayout.ShopBagCloseHeight));
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.sprite = closeSprite;
            closeImg.color = closeSprite != null ? Color.white : UiTheme.Panel;
            closeImg.raycastTarget = true;
            if (closeSprite != null)
            {
                closeImg.type = Image.Type.Sliced;
                closeImg.pixelsPerUnitMultiplier = 1f;
            }

            var closeButton = closeGo.AddComponent<Button>();
            closeButton.targetGraphic = closeImg;
            closeButton.transition = Selectable.Transition.None;

            GameObject closeLabelGo = NewUi("Label", closeGo.transform);
            Stretch(Rt(closeLabelGo));
            AddText(closeLabelGo, title, UiLayout.FontSizeShopBagClose,
                UiTheme.ShopBagCloseText, TextAlignmentOptions.Center, "关闭");

            // ⑧ 接线
            var bag = layerGo.AddComponent<ShopBagView>();
            var serialized = new SerializedObject(bag);
            SetRef(serialized, "_veilButton", veilButton);
            SetRef(serialized, "_closeButton", closeButton);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_count", countText);
            SetRef(serialized, "_empty", emptyGo);
            SetRef(serialized, "_scroll", scroll);
            SetRef(serialized, "_content", contentRt);
            SetRef(serialized, "_cardTemplate", card);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // ⚠ 默认失活。**不要在 ShopBagView.Awake 里 Hide**（见那个类的注释）：
            //   首次 SetActive(true) 才触发 Awake，同一帧里的 Hide 会把刚显示的内容按灭。
            layerGo.SetActive(false);

            log.AppendLine("  背包面板：" + UiLayout.ShopBagColumns + " 列网格（格 "
                           + UiLayout.ShopBagCellWidth + "×" + UiLayout.ShopBagCellHeight
                           + " · 卡 " + UiLayout.ShopBagCardFaceWidth + "）+ 滚动条 + 关闭键"
                           + " · 底图 " + (panelSprite != null ? panelSprite.name : "缺图（纯色）"));
            return bag;
        }

        // ══════════════════════════════════════════════════════
        //  调试入口
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 场景里的调试入口（<see cref="ShopSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的<b>父级</b>（场景根）而不是 Canvas 上</b>：
        /// 本构建器在存 Prefab 时只保存 Canvas 这一棵子树，挂在场景根就<b>天然不会进 Prefab</b>——
        /// 不需要"存完再删"那套容易漏的做法。正式接入冒险流程时，这个节点连同本方法一起删掉即可。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            // 幂等：上一次跑留下的先清掉
            ShopSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<ShopSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("ShopSceneEntry");
            var entry = go.AddComponent<ShopSceneEntry>();

            ShopView view = canvasGo.GetComponent<ShopView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  调试入口 ShopSceneEntry（挂在场景根，不进 Prefab）");
            return go;
        }

        // ══════════════════════════════════════════════════════
        //  场景杂项
        // ══════════════════════════════════════════════════════

        /// <summary>空场景没有相机 → 进 Play 只看到一片默认灰，截图没法看。补一台纯色底相机。</summary>
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
        /// 资产路径 → 磁盘绝对路径。
        ///
        /// <para><b>⚠ 2026-09-26 修</b>：原来写的是
        /// <c>Combine(工程根, assetPath.Substring("Assets/".Length))</c> —— 把 <c>Assets/</c>
        /// 摘掉却没把工程根补成 <c>&lt;工程&gt;/Assets</c>，于是查的是
        /// <c>&lt;工程&gt;/Scenes/Shop.unity</c>（不存在）→ 这个函数<b>恒返回 false</b>，
        /// <see cref="RunAll"/> 每次都当「场景还不存在」去<b>新建一个空场景</b>再把结果存回
        /// <c>Assets/Scenes/Shop.unity</c>。日志上只表现为「新建」两个字，
        /// 实际后果是**场景里手改过的东西（相机参数、调试入口的 Inspector 值）每跑一次构建就被抹掉**
        /// —— 正是「手摆的节点活不过一次重跑」那一类。</para>
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
