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
    /// <b>P6 · 女巫的工坊（特殊强化）场景</b>的构建器（2026-10-01）。
    ///
    /// <para>菜单：<c>魔法乱斗/P6 · 构建 WitchWorkshop 场景</c>（<b>无确认弹窗</b>，可直接被脚本 / MCP 调）。</para>
    ///
    /// <para><b>它做什么</b>（幂等，可反复重跑）：</para>
    /// <list type="number">
    /// <item>先调 <see cref="CardPoolBuilder.RunAll"/> 把场景卡池准备好（缺失才建，不覆盖）；</item>
    /// <item>打开 / 新建 <c>Assets/Scenes/WitchWorkshop.unity</c>；</item>
    /// <item>建 <b>一个</b> <c>WitchCanvas</c>：背景 + 水晶球命中区 + 标题 + 引导行 + 离开键
    /// + 主浮层（两个空位）+ 卡池浏览层；</item>
    /// <item>把 <c>WitchView</c> / <c>WitchLayerView</c> / <c>WitchSlotView</c> /
    /// <c>WitchPickerView</c> / <c>UpgradePickerCell</c> 的序列化字段一次性接好；</item>
    /// <item>存出 <c>Assets/Prefabs/Ui/WitchCanvas.prefab</c>；</item>
    /// <item>场景里额外挂一个 <c>WitchSceneEntry</c>（调试入口）—— <b>它不进 Prefab</b>。</item>
    /// </list>
    ///
    /// <para><b>⚠ 弹窗版式直接复用 <c>UiLayout.ShopBag*</c> 常量</b>：
    /// 商店背包 / P5 强化弹窗 / 本场景是<b>同一套面板</b>（同一张 <c>Peek_Panel</c> 底图、
    /// 同一套网格参数）。抄一份同值常量只会让以后两边分叉。</para>
    ///
    /// <para><b>⚠ 卡面只 instantiate、绝不新建第二份</b>（铁律 9）：空位里与浏览层里的卡
    /// 都是 <c>Assets/Prefabs/Ui/CardView_Hand.prefab</c> 的实例，尺寸由 <c>SetFaceWidth</c> 在运行时给。</para>
    ///
    /// <para><b>⚠ 两个浮层在构建器里都是失活的（<c>SetActive(false)</c>）</b>，
    /// 且它们的 <c>Awake</c> 里不许 Hide —— 见 <see cref="WitchLayerView"/> 的注释。</para>
    /// </summary>
    public static class WitchUiBuilder
    {
        private const string ScenePath = "Assets/Scenes/WitchWorkshop.unity";
        private const string PrefabPath = "Assets/Prefabs/Ui/WitchCanvas.prefab";
        private const string CanvasName = "WitchCanvas";
        private const string HandCardPath = "Assets/Prefabs/Ui/CardView_Hand.prefab";
        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>弹窗底板 —— 与商店背包 / P5 强化弹窗**同一张** <c>Peek_Panel</c>（992×447 九宫格）。</summary>
        private const string PanelSpritePath = "Assets/Art/Ui/Peek_Panel.png";

        /// <summary>空位底衬 / 描边 —— 与卡面自己的底衬同一批九宫格图（border 38）。</summary>
        private const string BoxSpritePath = "Assets/Art/Ui/CardBox.png";

        private const string BoxLineSpritePath = "Assets/Art/Ui/CardBox_Line.png";

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);

        /// <summary>卡面「高 / 宽」（组成式卡面画在固定设计空间里，外框矩形要按同比例给）。</summary>
        private static readonly float CardAspect = UiLayout.HandCardHeight / UiLayout.HandCardWidth;

        [MenuItem("魔法乱斗/P6 · 构建 WitchWorkshop 场景", false, 42)]
        private static void MenuEntry()
        {
            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[Witch] ==== 开始 ====");

            if (EditorApplication.isPlaying)
            {
                log.AppendLine("✘ 请先退出 Play 再构建（Play 模式下改场景不会落盘）");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            log.Append(CardPoolBuilder.RunAll());      // 先把卡池准备好（本场景调试时读 UpgradePool）

            TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            TMP_FontAsset title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            if (body == null || title == null)
            {
                log.AppendLine("✘ 缺 TMP 字体：" + BodyFontPath + " / " + TitleFontPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.WitchBgSpritePath);
            Sprite panel = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            Sprite leave = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.ShopLeaveSpritePath);
            Sprite button = AssetDatabase.LoadAssetAtPath<Sprite>(UiLayout.HandPickConfirmSpritePath);
            Sprite box = AssetDatabase.LoadAssetAtPath<Sprite>(BoxSpritePath);
            Sprite boxLine = AssetDatabase.LoadAssetAtPath<Sprite>(BoxLineSpritePath);

            if (bg == null)
            {
                log.AppendLine("✘ 缺女巫工坊背景图：" + UiLayout.WitchBgSpritePath
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

            if (button == null)
            {
                log.AppendLine("✘ 缺「确认 / 关闭」按钮底图：" + UiLayout.HandPickConfirmSpritePath);
            }

            if (box == null || boxLine == null)
            {
                log.AppendLine("✘ 缺空位底衬 / 描边九宫格图：" + BoxSpritePath + " / " + BoxLineSpritePath
                               + "（空位会退化成没有边框的暗块）");
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
            GameObject canvasGo = BuildCanvas(body, title, bg, panel, leave, button, box, boxLine,
                handPrefab, log);
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

            log.AppendLine("[Witch] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ══════════════════════════════════════════════════════
        //  Canvas
        // ══════════════════════════════════════════════════════

        private static GameObject BuildCanvas(TMP_FontAsset body, TMP_FontAsset title, Sprite bg,
            Sprite panel, Sprite leave, Sprite button, Sprite box, Sprite boxLine,
            GameObject handPrefab, StringBuilder log)
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

            // ⚠ 女巫工坊背景是**不透明**的 —— 排在它后面就什么都看不见。
            //   背景图本身就铺满，所以只在缺图时补一块深色底。
            if (bg == null)
            {
                GameObject fallback = NewUi("Backdrop", canvasGo.transform);
                AddImage(fallback, UiTheme.Backdrop);
                Stretch(Rt(fallback));
            }

            BuildBackground(canvasGo.transform, bg, log);

            // 水晶球命中区：整合场景**唯一**吃「点水晶球」这一下的东西。
            Button orbButton = BuildOrbHit(canvasGo.transform, log);

            // 标题
            GameObject titleGo = NewUi("Title", canvasGo.transform);
            Box(Rt(titleGo), Mid, Mid,
                new Vector2(0f, UiLayout.WitchTitleCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.WitchTitleWidth, UiLayout.WitchTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeWitchTitle,
                UiTheme.WitchTitleText, TextAlignmentOptions.Center, "女巫的工坊");

            // 引导行（浮层一打开就藏起来）
            GameObject hintGo = NewUi("Hint", canvasGo.transform);
            Box(Rt(hintGo), Mid, Mid,
                new Vector2(0f, UiLayout.WitchHintCenterY - UiLayout.ReferenceHeight * 0.5f),
                new Vector2(UiLayout.WitchHintWidth, UiLayout.WitchHintHeight));
            TMP_Text hintText = AddText(hintGo, body, UiLayout.FontSizeWitchHint,
                UiTheme.WitchHintText, TextAlignmentOptions.Center,
                "点击水晶球，进行一次特殊强化（献祭一张牌，强化另一张）");

            // 离开键（只有它一个，所以右边距用 64 而不是给背包键让位的 176）
            Button leaveButton = BuildLeaveButton(canvasGo.transform, body, leave, out TMP_Text leaveLabel);

            // 主浮层（两个空位那一层，默认失活）
            WitchLayerView layer = BuildLayer(canvasGo.transform, body, title, panel, button, box, boxLine,
                handPrefab, log);

            // 卡池浏览层（默认失活；排在主浮层之后 = 更上层）
            WitchPickerView picker = BuildPicker(canvasGo.transform, body, title, panel, button,
                handPrefab, log);

            // ── 接线 ─────────────────────────────────────────────
            var view = canvasGo.AddComponent<WitchView>();
            var serialized = new SerializedObject(view);
            SetRef(serialized, "_orbButton", orbButton);
            SetRef(serialized, "_leaveButton", leaveButton);
            SetRef(serialized, "_leaveLabel", leaveLabel);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_hint", hintGo);
            SetRef(serialized, "_hintText", hintText);
            SetRef(serialized, "_layer", layer);
            SetRef(serialized, "_picker", picker);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Vector2 orbSize = UiLayout.WitchOrbSize();
            log.AppendLine("  Canvas OK：背景 " + (bg != null ? "OK" : "缺图")
                           + " · 水晶球命中区 " + orbSize.x.ToString("0.#") + "×"
                           + orbSize.y.ToString("0.#")
                           + " · 主浮层 OK · 浏览层 OK · 离开键 " + (leave != null ? "OK" : "缺图"));
            return canvasGo;
        }

        /// <summary>背景按 cover 铺（与商店 / 强化同一套算法）。</summary>
        private static void BuildBackground(Transform parent, Sprite bg, StringBuilder log)
        {
            if (bg == null)
            {
                return;
            }

            GameObject go = NewUi("Bg_WitchWorkshop", parent);
            var img = go.AddComponent<Image>();
            img.sprite = bg;
            img.raycastTarget = false;
            img.preserveAspect = true;

            Vector2 size = UiLayout.WitchBgCoverSize();
            Box(Rt(go), Mid, Mid, Vector2.zero, size);
            log.AppendLine("  背景 cover " + size.x.ToString("0.#") + "×" + size.y.ToString("0.#")
                           + "（放大 " + (size.y / UiLayout.WitchBgNativeHeight).ToString("0.####") + "×）");
        }

        /// <summary>
        /// <b>水晶球命中区</b>：一整块透明的矩形（四角坐标对着背景图量，见
        /// <c>UiLayout.WitchOrbBg*</c>），是场景里唯一吃「点水晶球」这一下的东西。
        ///
        /// <para><b>⚠ 为什么是一整块矩形而不是「按球的圆形」</b>：球体在插画里是半透明的多面体，
        /// 轮廓本来就模糊（上部高光、下部紫晶），抠圆形要多写一套 <c>ICanvasRaycastFilter</c>；
        /// 而矩形四个角多出来的那几个像素本来就是球周围的暗色桌面 —— 点那里也是「要点球」的意思。</para>
        /// </summary>
        private static Button BuildOrbHit(Transform parent, StringBuilder log)
        {
            GameObject go = NewUi("OrbHit", parent);

            Vector2 center = UiLayout.WitchOrbCenter();
            Vector2 size = UiLayout.WitchOrbSize();

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

            log.AppendLine("  水晶球命中区：背景像素 (" + UiLayout.WitchOrbBgLeft + ", "
                           + UiLayout.WitchOrbBgTop + ")–(" + UiLayout.WitchOrbBgRight + ", "
                           + UiLayout.WitchOrbBgBottom + ") → 画布 center ("
                           + center.x.ToString("0.#") + ", " + center.y.ToString("0.#") + ")");
            return button;
        }

        /// <summary>「离开」按钮（底图自带「离开」两个字，只留一个空 Label 备用）。</summary>
        private static Button BuildLeaveButton(Transform parent, TMP_FontAsset body, Sprite sprite,
            out TMP_Text label)
        {
            GameObject go = NewUi("LeaveButton", parent);
            Box(Rt(go), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.WitchLeaveRight, UiLayout.WitchLeaveBottom),
                new Vector2(UiLayout.ShopLeaveWidth, UiLayout.ShopLeaveHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : UiTheme.Panel;
            img.raycastTarget = true;
            if (sprite != null)
            {
                // ⚠ Simple 而不是 Sliced —— 与商店 / P5 那两处同口径（用户在编辑器里手调过，
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
        //  主浮层（两个空位）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>主浮层</b>。层级（幂等，整体失活进 Prefab）：
        /// <code>
        /// Layer                        ← WitchLayerView 在这里
        /// ├─ Veil                      铺满画布的黑纱，**同时是关闭区**
        /// └─ Panel                     Peek_Panel 九宫格（与商店背包 / P5 同尺寸）
        ///    ├─ Title                  面板标题
        ///    ├─ SlotSacrifice          左空位（献祭，《WitchSlotView》）
        ///    ├─ SlotTarget             右空位（强化目标）
        ///    ├─ Arrow                  两个空位中间的「→」
        ///    ├─ Status                 状态行（提示 / 校验失败的原因）
        ///    ├─ CloseButton            左下「关闭」
        ///    └─ ConfirmButton          右下「确认」
        /// </code>
        /// </summary>
        private static WitchLayerView BuildLayer(Transform parent, TMP_FontAsset body, TMP_FontAsset title,
            Sprite panelSprite, Sprite buttonSprite, Sprite box, Sprite boxLine, GameObject handPrefab,
            StringBuilder log)
        {
            GameObject layerGo = NewUi("Layer", parent);
            Stretch(Rt(layerGo));

            // ① 遮罩（吃射线 + 点它关闭）
            GameObject veilGo = NewUi("Veil", layerGo.transform);
            Stretch(Rt(veilGo));
            var veilImg = veilGo.AddComponent<Image>();
            veilImg.color = UiTheme.WitchVeil;
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

            // ③ 标题
            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.WitchPanelTitleCenterY),
                new Vector2(UiLayout.WitchPanelTitleWidth, UiLayout.WitchPanelTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeWitchPanelTitle,
                UiTheme.WitchTitleColor, TextAlignmentOptions.Center, "特殊强化");

            // ④ 两个空位（左 = 献祭、右 = 强化目标）
            WitchSlotView slotSacrifice = BuildSlot(panelGo.transform, "SlotSacrifice", body, title,
                "献祭", true, new Vector2(-UiLayout.WitchSlotSpacing * 0.5f, UiLayout.WitchSlotCenterY),
                box, boxLine, handPrefab, log);

            WitchSlotView slotTarget = BuildSlot(panelGo.transform, "SlotTarget", body, title,
                "强化目标", false, new Vector2(UiLayout.WitchSlotSpacing * 0.5f, UiLayout.WitchSlotCenterY),
                box, boxLine, handPrefab, log);

            // ⑤ 中间的「→」（纯装饰，不吃射线）
            GameObject arrowGo = NewUi("Arrow", panelGo.transform);
            Box(Rt(arrowGo), Mid, Mid, new Vector2(0f, UiLayout.WitchSlotCenterY),
                new Vector2(UiLayout.WitchArrowWidth, UiLayout.WitchArrowHeight));
            AddText(arrowGo, title, UiLayout.FontSizeWitchArrow, UiTheme.WitchSlotLabel,
                TextAlignmentOptions.Center, "→");

            // ⑥ 状态行
            GameObject statusGo = NewUi("Status", panelGo.transform);
            Box(Rt(statusGo), Mid, Mid, new Vector2(0f, UiLayout.WitchStatusCenterY),
                new Vector2(UiLayout.WitchStatusWidth, UiLayout.WitchStatusHeight));
            TMP_Text statusText = AddText(statusGo, body, UiLayout.FontSizeWitchStatus,
                UiTheme.WitchStatusText, TextAlignmentOptions.Center, "点空位，从卡池里选一张牌");

            // ⑦ 「关闭」（左下）与「确认」（右下）—— 与商店背包镜像同一条线
            Button closeButton = BuildPanelButton(panelGo.transform, "CloseButton", title,
                UiTheme.WitchCloseText, new Vector2(-UiLayout.WitchButtonSideX, UiLayout.WitchButtonCenterY),
                buttonSprite, false, "关闭");

            Button confirmButton = BuildPanelButton(panelGo.transform, "ConfirmButton", title,
                UiTheme.WitchConfirmOn, new Vector2(UiLayout.WitchButtonSideX, UiLayout.WitchButtonCenterY),
                buttonSprite, true, "确认");

            Image confirmImg = confirmButton.GetComponent<Image>();
            TMP_Text confirmLabel = confirmButton.transform.Find("Label") != null
                ? confirmButton.transform.Find("Label").GetComponent<TMP_Text>()
                : null;

            // ⑧ 接线
            var layer = layerGo.AddComponent<WitchLayerView>();
            var serialized = new SerializedObject(layer);
            SetRef(serialized, "_veilButton", veilButton);
            SetRef(serialized, "_closeButton", closeButton);
            SetRef(serialized, "_confirmButton", confirmButton);
            SetRef(serialized, "_confirmImage", confirmImg);
            SetRef(serialized, "_confirmLabel", confirmLabel);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_status", statusText);
            SetRef(serialized, "_arrow", arrowGo.GetComponent<TMP_Text>());
            SetRef(serialized, "_slotSacrifice", slotSacrifice);
            SetRef(serialized, "_slotTarget", slotTarget);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // ⚠ 默认失活。**不要在 Awake 里 Hide**。
            layerGo.SetActive(false);

            log.AppendLine("  主浮层：两个空位（" + UiLayout.WitchSlotWidth + "×" + UiLayout.WitchSlotHeight
                           + "，步进 " + UiLayout.WitchSlotSpacing + "）+ 状态行 + 确认/关闭 · 底图 "
                           + (panelSprite != null ? panelSprite.name : "缺图（纯色）"));
            return layer;
        }

        /// <summary>
        /// 一个空位。子节点顺序即绘制顺序：
        /// 底衬 → 卡面 → 「+」→ 献祭暖光 → 描边 → 命中区；标签挂在槽外下方。
        ///
        /// <para>⚠ 卡面自带的交互必须全部关掉（与商店货位 / 选牌格子同一套理由）：
        /// ① <c>CardInteractor</c> 一按就 BeginDrag → 卡面自己吃掉点击；
        /// ② 卡面的 <c>Button</c> 与格子的命中区叠成两次冒泡。</para>
        /// </summary>
        private static WitchSlotView BuildSlot(Transform panel, string name, TMP_FontAsset body,
            TMP_FontAsset title, string label, bool sacrificeRole, Vector2 position,
            Sprite box, Sprite boxLine, GameObject handPrefab, StringBuilder log)
        {
            GameObject go = NewUi(name, panel);
            Box(Rt(go), Mid, Mid, position,
                new Vector2(UiLayout.WitchSlotWidth, UiLayout.WitchSlotHeight));

            // ① 底衬（CardBox 九宫格，暗色半透明 —— 读作「这里还空着」）
            GameObject backdropGo = NewUi("Backdrop", go.transform);
            Stretch(Rt(backdropGo));
            Image backdrop = AddCardBox(backdropGo, box, UiTheme.WitchSlotBackdrop);

            // ② 卡面（唯一那份 prefab 的实例）
            GameObject face = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, go.transform);
            face.name = "Card";
            Box(Rt(face), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.WitchSlotCardFaceWidth,
                    UiLayout.WitchSlotCardFaceWidth * CardAspect));
            CardView card = face.GetComponent<CardView>();
            if (card != null)
            {
                card.SetFaceWidth(UiLayout.WitchSlotCardFaceWidth);
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

            // ③ 没选牌时中央那个「+」
            GameObject plusGo = NewUi("Plus", go.transform);
            Box(Rt(plusGo), Mid, Mid, Vector2.zero,
                new Vector2(UiLayout.WitchSlotWidth, UiLayout.WitchSlotWidth));
            AddText(plusGo, title, 96f, UiTheme.WitchSlotEdgeEmpty, TextAlignmentOptions.Center, "+");

            // ④ 「它会被吃掉」那层暖光（只有左边空位有）
            GameObject tintGo = NewUi("Tint", go.transform);
            Stretch(Rt(tintGo));
            AddCardBox(tintGo, box, UiTheme.WitchSacrificeTint);

            // ⑤ 描边（未选 = 冷灰 / 已选 = 暖金，运行时由 WitchSlotView 切）
            GameObject edgeGo = NewUi("Edge", go.transform);
            Stretch(Rt(edgeGo));
            Image edge = AddCardBox(edgeGo, boxLine, UiTheme.WitchSlotEdgeEmpty);

            // ⑥ 命中区（透明，**唯一**吃射线的东西，排在最后 = 最上层）
            GameObject hitGo = NewUi("Hit", go.transform);
            Stretch(Rt(hitGo));
            var hitImg = hitGo.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0f);
            hitImg.raycastTarget = true;
            var hitButton = hitGo.AddComponent<Button>();
            hitButton.targetGraphic = hitImg;
            hitButton.transition = Selectable.Transition.None;

            // ⑦ 角色标签（挂在槽外下方；`WitchSlotView.Configure` 会在 Awake 里再写一次文字）
            GameObject labelGo = NewUi("Label", go.transform);
            Box(Rt(labelGo), Mid, Mid, new Vector2(0f, -UiLayout.WitchSlotLabelDrop),
                new Vector2(UiLayout.WitchSlotLabelWidth, UiLayout.WitchSlotLabelHeight));
            TMP_Text labelText = AddText(labelGo, body, UiLayout.FontSizeWitchSlotLabel,
                UiTheme.WitchSlotLabel, TextAlignmentOptions.Center, label);

            // ⑧ 接线
            var slot = go.AddComponent<WitchSlotView>();
            var serialized = new SerializedObject(slot);
            SetRef(serialized, "_hit", hitButton);
            SetRef(serialized, "_backdrop", backdrop);
            SetRef(serialized, "_edge", edge);
            SetRef(serialized, "_plus", plusGo);
            SetRef(serialized, "_card", card);
            SetRef(serialized, "_sacrificeTint", tintGo);
            SetRef(serialized, "_label", labelText);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 初始态 = 空位：卡面藏着、「+」露着、暖光照 setActive（Configure 会在运行时重申）
            face.SetActive(false);
            if (tintGo.activeSelf != sacrificeRole)
            {
                tintGo.SetActive(sacrificeRole);
            }

            log.AppendLine("  空位 " + name + "（" + label + "）：底衬 + 卡面 + plus + 暖光"
                           + (sacrificeRole ? "（开）" : "（关）") + " + 描边 + 命中区");
            return slot;
        }

        /// <summary>面板底部那两个按钮（结构一样，只有位置与文字不同）。</summary>
        private static Button BuildPanelButton(Transform panel, string name, TMP_FontAsset titleFont,
            Color labelColor, Vector2 position, Sprite sprite, bool isConfirm, string label)
        {
            GameObject go = NewUi(name, panel);
            Box(Rt(go), Mid, Mid, position,
                new Vector2(UiLayout.WitchButtonWidth, UiLayout.WitchButtonHeight));

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null
                ? (isConfirm ? UiTheme.WitchConfirmOff : Color.white)
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
                button.interactable = false;    // 一开始两个空位都是空的 → 确认键置灰
            }

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            AddText(labelGo, titleFont, UiLayout.FontSizeWitchButton, labelColor,
                TextAlignmentOptions.Center, label);

            return button;
        }

        // ══════════════════════════════════════════════════════
        //  卡池浏览层
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>卡池浏览层</b>。层级（幂等，整体失活进 Prefab）：
        /// <code>
        /// Picker                       ← WitchPickerView 在这里
        /// ├─ Veil                      铺满画布的黑纱，**同时是关闭区**
        /// └─ Panel                     Peek_Panel 九宫格（与商店背包 / P5 同尺寸）
        ///    ├─ Title                  面板标题
        ///    ├─ Hint                   「这张牌会被消耗掉」那行固定说明
        ///    ├─ Cards                  滚动区（ScrollRect + 背板）
        ///    │  ├─ Viewport → Content  网格（GridLayoutGroup + ContentSizeFitter）
        ///    │  │  └─ CellTemplate     UpgradePickerCell（失活）
        ///    │  └─ Scrollbar
        ///    ├─ Empty                  一张牌都没有时的提示
        ///    └─ CloseButton            左下「关闭」
        /// </code>
        ///
        /// <para><b>⚠ 没有「确认」键</b>：这里点一张牌就直接返回（下单式），
        /// 外层浮层已经有自己的「确认」了 —— 再来一个是两层确认，玩家会懵。</para>
        /// </summary>
        private static WitchPickerView BuildPicker(Transform parent, TMP_FontAsset body,
            TMP_FontAsset title, Sprite panelSprite, Sprite buttonSprite, GameObject handPrefab,
            StringBuilder log)
        {
            GameObject layerGo = NewUi("Picker", parent);
            Stretch(Rt(layerGo));

            // ① 遮罩
            GameObject veilGo = NewUi("Veil", layerGo.transform);
            Stretch(Rt(veilGo));
            var veilImg = veilGo.AddComponent<Image>();
            veilImg.color = UiTheme.WitchVeil;
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
            panelImg.raycastTarget = true;
            if (panelSprite != null)
            {
                panelImg.type = Image.Type.Sliced;
                panelImg.pixelsPerUnitMultiplier = 1f;
            }

            // ③ 标题 + 一行固定说明
            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagTitleCenterY),
                new Vector2(UiLayout.ShopBagTitleWidth, UiLayout.ShopBagTitleHeight));
            TMP_Text titleText = AddText(titleGo, title, UiLayout.FontSizeShopBagTitle,
                UiTheme.WitchTitleColor, TextAlignmentOptions.Center, "选择一张牌");

            GameObject hintGo = NewUi("Hint", panelGo.transform);
            Box(Rt(hintGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagCountCenterY),
                new Vector2(UiLayout.ShopBagCountWidth, UiLayout.ShopBagCountHeight));
            TMP_Text hintText = AddText(hintGo, body, UiLayout.FontSizeShopBagCount,
                UiTheme.WitchStatusText, TextAlignmentOptions.Center, "");

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

            // ⑤ 格子模板（复用强化选牌弹窗那一份 —— 「卡面 + 压暗纱 + 命中区」两个场景一模一样）
            UpgradePickerCell cell = BuildCellTemplate(contentGo.transform, handPrefab, body, log);

            // ⑥ 空提示
            GameObject emptyGo = NewUi("Empty", panelGo.transform);
            Box(Rt(emptyGo), Mid, Mid, new Vector2(0f, UiLayout.ShopBagScrollCenterY),
                new Vector2(UiLayout.ShopBagEmptyWidth, UiLayout.ShopBagEmptyHeight));
            AddText(emptyGo, body, UiLayout.FontSizeShopBagEmpty, UiTheme.ShopBagEmptyText,
                TextAlignmentOptions.Center, "卡池里还没有牌");
            emptyGo.SetActive(false);

            // ⑦ 「关闭」（左下）
            Button closeButton = BuildPanelButton(panelGo.transform, "CloseButton", title,
                UiTheme.WitchCloseText, new Vector2(-UiLayout.WitchButtonSideX, UiLayout.WitchButtonCenterY),
                buttonSprite, false, "关闭");

            // ⑧ 接线
            var picker = layerGo.AddComponent<WitchPickerView>();
            var serialized = new SerializedObject(picker);
            SetRef(serialized, "_veilButton", veilButton);
            SetRef(serialized, "_closeButton", closeButton);
            SetRef(serialized, "_title", titleText);
            SetRef(serialized, "_hint", hintText);
            SetRef(serialized, "_scroll", scroll);
            SetRef(serialized, "_content", contentRt);
            SetRef(serialized, "_cellTemplate", cell);
            SetRef(serialized, "_empty", emptyGo);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // ⚠ 默认失活。**不要在 Awake 里 Hide**。
            layerGo.SetActive(false);

            log.AppendLine("  浏览层：" + UiLayout.ShopBagColumns + " 列网格（格 "
                           + UiLayout.ShopBagCellWidth + "×" + UiLayout.ShopBagCellHeight
                           + "）+ 关闭 · 格子复用 UpgradePickerCell");
            return picker;
        }

        /// <summary>
        /// 浏览层里的一格模板（失活）。子节点顺序即绘制顺序：卡面 → 压暗纱（含原因）→ 命中区。
        ///
        /// <para>⚠ 与 <c>UpgradeUiBuilder</c> 里那份是**同一套结构**，但这里要自己再建一遍 ——
        /// 节点树没法跨 Prefab 复用。<b>真正被复用的是格子组件本身</b>（<see cref="UpgradePickerCell"/>），
        /// 所以「一格长什么样、点击怎么冒泡」仍然只有一处实现。</para>
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

            // ② 压暗纱（不能选时盖在卡面上）
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
        //  调试入口 / 场景杂项
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 场景里的调试入口（<see cref="WitchSceneEntry"/>）。
        ///
        /// <para><b>⚠ 它挂在 Canvas 的<b>父级</b>（场景根）而不是 Canvas 上</b>：
        /// 存 Prefab 时只保存 Canvas 那一棵子树，挂在场景根就<b>天然不会进 Prefab</b>。</para>
        /// </summary>
        private static GameObject BuildDebugEntry(GameObject canvasGo, StringBuilder log)
        {
            WitchSceneEntry[] leftovers = UnityEngine.Object.FindObjectsOfType<WitchSceneEntry>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && !leftovers[i].gameObject.scene.name.StartsWith("Preview"))
                {
                    UnityEngine.Object.DestroyImmediate(leftovers[i].gameObject);
                }
            }

            var go = new GameObject("WitchSceneEntry");
            var entry = go.AddComponent<WitchSceneEntry>();

            WitchView view = canvasGo.GetComponent<WitchView>();
            var serialized = new SerializedObject(entry);
            SetRef(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  调试入口 WitchSceneEntry（挂在场景根，不进 Prefab）");
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

        /// <summary>
        /// 九宫格圆角底（空位底衬 / 描边用它）。
        /// 图是 <c>CardBox</c> / <c>CardBox_Line</c>（160×160，border 38）——
        /// border 没设对的话圆角会拉成椭圆，而且没有任何报错。
        /// </summary>
        private static Image AddCardBox(GameObject go, Sprite sprite, Color color)
        {
            Image img = AddImage(go, color);
            img.sprite = sprite;
            img.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
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
