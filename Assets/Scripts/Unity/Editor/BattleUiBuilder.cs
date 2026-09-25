using System.Collections.Generic;
using System.IO;
using MagicBrawl.App;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// M8 BattleUi 的构建器：在 M7 已经建好的 <c>BattleCanvas</c> 上<b>幂等追加</b> M8 的节点，
    /// 并把 <c>BattleDriver</c> / <c>BattleUi</c> 与五个视图的引用一次性接好。
    ///
    /// <para><b>为什么与 M7 分成两个 builder</b>：M7 只管「版式骨架」（改了要重跑、会重建整个 Canvas），
    /// M8 只管「接线 + 浮层」。两者职责不同但都幂等 —— 改了 <see cref="UiLayout"/> 之后要
    /// <b>先跑 M7、再跑 M8</b>（M7 会把 Canvas 推倒重建，M8 的接线随之丢失）。</para>
    ///
    /// <para><b>菜单不带确认弹窗</b>：脚本化触发（Unity MCP 的 execute_menu_item）遇到
    /// <c>EditorUtility.DisplayDialog</c> 会挂住主线程、把调用方一起拖死。所以菜单与
    /// <see cref="RunBuild"/> 走同一条无弹窗路径。</para>
    /// </summary>
    public static class BattleUiBuilder
    {
        private const string CanvasName = "BattleCanvas";
        private const string PrefabDir = "Assets/Prefabs/Ui";
        private const string HandCardPrefabPath = PrefabDir + "/CardView_Hand.prefab";
        private const string MiniCardPrefabPath = HandCardPrefabPath;

        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>M8 / M9 / M13 / M15 / M16 新增的根节点（幂等重建时先整体删掉这些）。</summary>
        private static readonly string[] OwnedNodes =
        {
            // ⚠ M20（2026-09-20）删掉了 "Flash" —— 全屏受击红闪按用户口径取消，
            //   节点与 StageView._flash 一并移除（残留的旧节点会被 Cleanup 顺带清掉，
            //   但要先把名字留在下面这份列表里最后一趟，见 Cleanup 的说明）。
            "PromptBar", "PickerPanel", "DealPanel", "DetailPanel", "ResultRoot", "LogPanel",
            "PlayZone", "DropArrow", "AuraDropHint",
            // 2026-09-19：这三个原先由运行时 new 出来，改成这里预建
            //（光环标记条 / 飞牌层 / 中央行动提示）—— 用户在 Hierarchy 里能看见、能调。
            "PreparedAura", "CardTransitLayer", "ActionBanner",
            // M36：屏幕中央的「一句话浮字」（回答「这张牌为什么点不动」）
            "FloatTip",
            // M25：查看对方手牌弹窗（雷云 / 狂躁蘑菇的第 2 个 α 效果）
            "PeekLayer",
            // M27：手牌选择弹窗（磁暴 / 充能 / 电弧）
            "HandPickLayer",
            // M28：按住怪物手牌数 → 查看它的手牌
            "MonsterHandLayer",
            // 已废弃、但仍要从老场景里清掉的历史节点
            "Flash",
        };

        /// <summary>Peek 面板九宫格图的路径。</summary>
        private const string PeekPanelPath = "Assets/Art/Ui/Peek_Panel.png";

        // ══════════════════════════════════════════════════════
        //  菜单
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/M8 · 构建 BattleUi（接线 + 浮层）", priority = 2)]
        public static void BuildBattleUi()
        {
            RunBuild();
        }

        /// <summary>
        /// M9 的菜单是<strong>同一趟接线</strong>的别名：发牌台要挂的引用（<c>_deal</c>）和 M8 那些
        /// 视图写在同一个 <c>BattleUi</c> 上，拆成两趟反而会出现「M7 重建之后只跑了 M9 菜单，
        /// 结果 M8 的接线全丢」这种半截状态。构建是幂等的，重复跑没有副作用。
        /// </summary>
        [MenuItem("魔法乱斗/M9 · 构建界面接线（BattleUi + 发牌台）", priority = 3)]
        public static void BuildBattleUiWithDeal()
        {
            RunBuild();
        }

        /// <summary>无弹窗的核心构建（菜单与 MCP 反射都走这里）。</summary>
        public static void RunBuild()
        {
            GameObject canvas = GameObject.Find(CanvasName);

            if (canvas == null)
            {
                Debug.LogWarning("[BattleUi] 场景里没有 " + CanvasName + " —— 先跑 M7 的构建菜单");
                return;
            }

            CardView handPrefab = AssetDatabase.LoadAssetAtPath<CardView>(HandCardPrefabPath);
            CardView miniPrefab = AssetDatabase.LoadAssetAtPath<CardView>(MiniCardPrefabPath);

            if (handPrefab == null || miniPrefab == null)
            {
                Debug.LogWarning("[BattleUi] 找不到卡片 Prefab（" + HandCardPrefabPath + " / "
                                 + MiniCardPrefabPath + "）—— 先跑 M7 的构建菜单");
                return;
            }

            TMP_FontAsset body = LoadFont(BodyFontPath);
            TMP_FontAsset title = LoadFont(TitleFontPath);

            EnsurePeekImporters();

            Transform topArea = FindDeep(canvas.transform, "TopArea");
            Transform stage = FindDeep(canvas.transform, "Stage");
            Transform handArea = FindDeep(canvas.transform, "HandArea");
            Transform handRow = FindDeep(canvas.transform, "HandRow");
            Transform playerCooling = FindDeep(canvas.transform, "PlayerCoolingPanel");
            Transform enemyCooling = FindDeep(canvas.transform, "EnemyCoolingPanel");

            if (topArea == null || stage == null || handArea == null || handRow == null
                || playerCooling == null || enemyCooling == null)
            {
                Debug.LogWarning("[BattleUi] M7 的骨架不完整（TopArea / Stage / HandArea / HandRow / "
                                 + "两个 CoolingPanel 必须都在）—— 先跑 M7 的构建菜单");
                return;
            }

            Cleanup(canvas.transform);

            // ── 建 M8 节点 ─────────────────────────────────────
            StageView stageView = BuildStageExtras(stage);
            HandView handView = BuildHandExtras(handArea, handRow, handPrefab);
            CooldownView cooldownView = BuildCooldownExtras(topArea, miniPrefab, playerCooling, enemyCooling);
            TargetPicker picker = BuildPicker(topArea, body, title);
            CardDetailView detail = BuildDetail(topArea, body, title);
            DealView deal = BuildDeal(topArea, body, title);

            // HandView / CooldownView 都要能弹出看牌浮层（长按一张牌看完整卡面）
            var hso = new SerializedObject(handView);
            hso.FindProperty("_peek").objectReferenceValue = detail;
            hso.ApplyModifiedPropertiesWithoutUndo();

            var cso = new SerializedObject(cooldownView);
            cso.FindProperty("_peek").objectReferenceValue = detail;
            cso.ApplyModifiedPropertiesWithoutUndo();

            BuildResult(canvas.transform, body, title, out GameObject resultRoot,
                out TMP_Text resultTitle, out TMP_Text resultSub, out Button againButton);
            BuildLog(canvas.transform, body, out GameObject logPanel, out TMP_Text logText);

            // M13：出牌判定区（不可视）+ 指向箭头，接回 HandView
            RectTransform playZone = BuildPlayZone(canvas.transform);
            DropArrowView dropArrow = BuildDropArrow(canvas.transform);

            // 2026-09-19：原来由运行时 new 出来的三个节点，改在这里预建成场景节点。
            // 顺序 = 绘制顺序（后面的压前面的）：飞牌层 → 行动提示 → 光环标记条。
            // 标记条必须在最上 —— 拖拽中的光环图标不能被任何东西盖住。
            CardTransitView transit = BuildTransitLayer(canvas.transform);
            ActionBannerView actionBanner = BuildActionBanner(canvas.transform, title);
            FloatTipView floatTip = BuildFloatTip(canvas.transform, title);
            PreparedAuraView preparedBar = BuildPreparedBar(canvas.transform);

            // M25：查看对方手牌弹窗。**必须排在最后一个建** ——
            // 它要压在所有视图之上（准备标记条、飞牌层、中央提示都在它下面），
            // Canvas 里同层节点的绘制顺序 = 兄弟顺序，最后建 = 画在最上。
            PeekView peek = BuildPeekLayer(canvas.transform, body, title);

            // M27：手牌选择弹窗（磁暴 / 充能 / 电弧）。
            // 比 PeekLayer 更晚建 = 压在它之上 —— 两者不会同屏（一拍只有一种决策），
            // 但绘制顺序上「后建的在上面」这条要守住。
            HandPickView handPick = BuildHandPickLayer(canvas.transform, body, title);

            // M28：按住怪物手牌数 → 查看它的手牌。同样排在最后，
            // 「后建 = 画在最上」这条要守住（三块浮层不会同屏，但顺序是硬约束）。
            MonsterHandView monsterHand = BuildMonsterHandLayer(canvas.transform, body, title);

            // ── M28：捞一下 ArtLayer/HandCount_Monster 上的「按住 / 松手」手势 ──
            // ⚠ 组件本体由 BattleArtLayerBuilder.BuildHandCount 挂，本构建器只做接线。
            //   原因是那条构建链会 DestroyImmediate 整棵旧 ArtLayer 推倒重建 ——
            //   在这里 AddComponent 的话，先跑本菜单、再跑 M11，组件就会被静默抹掉
            //  （实测过一次）。谁的节点谁负责挂组件，才不会互相拆台。
            PressHoldButton monsterHandHold = null;
            Transform handCount = FindDeep(canvas.transform, "HandCount_Monster");
            if (handCount != null)
            {
                monsterHandHold = handCount.GetComponent<PressHoldButton>();
                if (monsterHandHold == null)
                {
                    // 兜底：M11 还没带上这行代码时的老场景，先补一个。
                    // 正常情况下 M11 已经挂好，这里是 no-op。
                    monsterHandHold = handCount.gameObject.AddComponent<PressHoldButton>();
                }
            }
            else
            {
                Debug.LogWarning("[BattleUi] 没找到 ArtLayer/HandCount_Monster —— "
                                 + "M28 的「按住查看怪物手牌」手势没接上。"
                                 + "先跑「M11 · 构建 BattleArtLayer」再跑本菜单。");
            }

            var dragSo = new SerializedObject(handView);
            dragSo.FindProperty("_playZone").objectReferenceValue = playZone;
            dragSo.FindProperty("_arrow").objectReferenceValue = dropArrow;
            dragSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 把 HandRow 的布局组拆掉 ────────────────────────
            // HorizontalLayoutGroup 每帧会按 preferredSize 重排子节点的 anchoredPosition，
            // 把「选中上浮」直接抹掉。HandView 用自定义排布接管。
            LayoutGroup rowLayout = handRow.GetComponent<LayoutGroup>();
            if (rowLayout != null)
            {
                Object.DestroyImmediate(rowLayout);
            }

            // ── UiKitPreview 别在真对局里铺样本卡 ──────────────
            UiKitPreview preview = canvas.GetComponent<UiKitPreview>();
            if (preview != null)
            {
                var pso = new SerializedObject(preview);
                SerializedProperty build = pso.FindProperty("_buildOnAwake");
                if (build != null)
                {
                    build.boolValue = false;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // ── 驱动 + 总装 ───────────────────────────────────
            BattleDriver driver = canvas.GetComponent<BattleDriver>();
            if (driver == null)
            {
                driver = canvas.AddComponent<BattleDriver>();
            }

            BattleUi ui = canvas.GetComponent<BattleUi>();
            if (ui == null)
            {
                ui = canvas.AddComponent<BattleUi>();
            }

            var uso = new SerializedObject(ui);
            uso.FindProperty("_driver").objectReferenceValue = driver;
            uso.FindProperty("_hand").objectReferenceValue = handView;
            uso.FindProperty("_cooldown").objectReferenceValue = cooldownView;
            uso.FindProperty("_stage").objectReferenceValue = stageView;
            uso.FindProperty("_picker").objectReferenceValue = picker;
            uso.FindProperty("_deal").objectReferenceValue = deal;
            uso.FindProperty("_transit").objectReferenceValue = transit;
            uso.FindProperty("_preparedBar").objectReferenceValue = preparedBar;
            uso.FindProperty("_banner").objectReferenceValue = actionBanner;
            uso.FindProperty("_floatTip").objectReferenceValue = floatTip;
            uso.FindProperty("_peek").objectReferenceValue = peek;
            uso.FindProperty("_handPick").objectReferenceValue = handPick;
            uso.FindProperty("_monsterHand").objectReferenceValue = monsterHand;
            uso.FindProperty("_monsterHandHold").objectReferenceValue = monsterHandHold;
            uso.FindProperty("_resultRoot").objectReferenceValue = resultRoot;
            uso.FindProperty("_resultTitle").objectReferenceValue = resultTitle;
            uso.FindProperty("_resultSub").objectReferenceValue = resultSub;
            uso.FindProperty("_againButton").objectReferenceValue = againButton;
            uso.FindProperty("_logPanel").objectReferenceValue = logPanel;
            uso.FindProperty("_logText").objectReferenceValue = logText;
            uso.ApplyModifiedPropertiesWithoutUndo();

            // ⚠ M20 删掉了这里对 StageView._flash 的接线（全屏受击红闪已取消）

            // ── 收尾：浮层默认收起 ─────────────────────────────
            SetActiveIfFound(topArea, "PromptBar", false);
            HideBody(topArea, "PickerPanel");
            HideBody(topArea, "DealPanel");

            // 看牌浮层没有 Body 子节点（面板本身就是内容），整块收起来
            SetActiveIfFound(topArea, "DetailPanel", false);
            if (resultRoot != null)
            {
                resultRoot.SetActive(false);
            }

            if (logPanel != null)
            {
                logPanel.SetActive(false);
            }

            SettingsUiBuilder.Ensure(canvas);

            // ── 保存 ──────────────────────────────────────────
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvas, PrefabDir + "/" + CanvasName + ".prefab",
                InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(canvas.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = canvas;
            EditorGUIUtility.PingObject(canvas);

            Debug.Log("[BattleUi] M8+M9+M12+M13 构建完成：\n"
                      + "  · Canvas 上挂 BattleDriver + BattleUi\n"
                      + "  · HandArea/HandView（扇形真圆弧排布 + 判定区拖拽出牌 + 长按看牌）\n"
                      + "  · Canvas/PlayZone（不可视出牌判定区）+ Canvas/DropArrow（指向箭头）\n"
                      + "  · TopArea/CooldownView（两个冷却区，迷你卡挂在槽外侧）\n"
                      + "  · Stage/StageView（信息条 + 中央标记）\n"
                      + "  · TopArea/PickerPanel/TargetPicker（选项浮层）\n"
                      + "  · TopArea/DealPanel/DealView（M9 发牌台：多选替换 + 已选计数 + 确认）\n"
                      + "  · TopArea/DetailPanel/CardDetailView（长按弹出的完整卡面）\n"
                      + "  · ResultRoot（结算 + 再来一局）、LogPanel（默认关）\n"
                      + "  改版式：改 UiLayout.cs → 先跑 M7 菜单、再跑本菜单、最后跑 M11。");
        }

        // ══════════════════════════════════════════════════════
        //  各块构建
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Stage 上的 M8 增量：顶部提示条 + StageView 接线。
        ///
        /// <para><b>StageMark 在 M8 里被停用</b>：它占着中央 200×90，而中央净空只有 420 宽，
        /// 选项浮层（最高 380 高）会从上面压过来。叙事文案统一改走顶部提示条
        /// （<c>PromptBar</c> 640×58，能放 22 个字），中央就腾出来给浮层了。</para>
        /// </summary>
        private static StageView BuildStageExtras(Transform stage)
        {
            TMP_FontAsset title = LoadFont(TitleFontPath);

            Transform topArea = stage.parent;

            GameObject prompt = NewUi("PromptBar", topArea);
            Box(Rt(prompt), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.PromptTop), new Vector2(UiLayout.PromptWidth, UiLayout.PromptHeight));
            AddImage(prompt, UiTheme.PromptBackdrop);
            MakeFrame(prompt.transform, 2f, UiTheme.OverlayEdge);

            GameObject promptText = NewUi("Text", prompt.transform);
            Stretch(Rt(promptText), 16f, 4f, 16f, 4f);
            TMP_Text promptLabel = AddText(promptText, title, UiLayout.FontSizePrompt,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);

            var view = stage.GetComponent<StageView>();
            if (view == null)
            {
                view = stage.gameObject.AddComponent<StageView>();
            }

            Transform playerBar = stage.Find("PlayerBar");
            Transform enemyBar = stage.Find("EnemyBar");
            Transform markRoot = stage.Find("StageMark");

            if (markRoot != null)
            {
                markRoot.gameObject.SetActive(false);
            }

            var so = new SerializedObject(view);
            so.FindProperty("_playerBar").objectReferenceValue =
                playerBar == null ? null : playerBar.GetComponent<PlayerBarView>();
            so.FindProperty("_enemyBar").objectReferenceValue =
                enemyBar == null ? null : enemyBar.GetComponent<PlayerBarView>();
            so.FindProperty("_markRoot").objectReferenceValue = markRoot == null ? null : markRoot.gameObject;
            so.FindProperty("_mark").objectReferenceValue =
                markRoot == null ? null : markRoot.GetComponent<TMP_Text>();
            so.FindProperty("_markBackdrop").objectReferenceValue = null;
            so.FindProperty("_promptRoot").objectReferenceValue = prompt;
            so.FindProperty("_prompt").objectReferenceValue = promptLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        // ⚠ M20（2026-09-20）：这里原来有个 BuildFlash —— 一块铺满上部、掉血时压一层的红色 Image。
        //   用户要求删掉「双方受伤时整个屏幕红色闪烁」的动画，节点与 StageView._flash 一并移除。
        //   "Flash" 仍留在 OwnedNodes 里，只为让重跑本菜单能把老场景 / 老 prefab 里的残留节点清掉。

        // ══════════════════════════════════════════════════════
        //  M13：出牌判定区 + 指向箭头
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 不可视的出牌判定区。
        ///
        /// <para><b>只是一个 RectTransform</b>：不挂任何 Graphic，所以既不参与绘制
        /// （「不可视」是要求）、也不参与射线检测（它不挡任何点击），
        /// 纯粹给 <see cref="HandView"/> 当判定框用。
        /// 挂在 Canvas 根下而不是 HandArea 里 —— 判定区属于「整块画面」，
        /// 跟手牌区的锚定方式无关，M7 重跑手牌区也不该把它带丢。</para>
        ///
        /// <para><b>M21：横向改成与屏幕左右两端对齐</b>（用户 2026-09-20）。
        /// 所以这里不能用 <c>Box</c>（它写的是 anchorMin == anchorMax 的定点写法），
        /// 而是 <c>anchorMin.x = 0 / anchorMax.x = 1 / sizeDelta.x = 0</c> 的<b>拉伸</b>写法 ——
        /// 换任何画布宽高比都贴到两端，不用维护一个「宽度」常量。
        /// 纵向仍是「下沿距底边 430、高 270」。</para>
        /// </summary>
        private static RectTransform BuildPlayZone(Transform canvas)
        {
            GameObject go = NewUi("PlayZone", canvas);
            RectTransform rt = Rt(go);

            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, UiLayout.PlayZoneBottom);
            rt.sizeDelta = new Vector2(0f, UiLayout.PlayZoneHeight);
            return rt;
        }

        /// <summary>
        /// 指向箭头：<c>DropArrow(铺满画布，负责坐标换算) / Arrow(负责旋转与定位) / Shaft + Head</c>。
        ///
        /// <para><b>杆为什么不用图片</b>：不赋 Sprite 的 <see cref="Image"/> 画的就是一块纯白矩形，
        /// 长短粗细全靠 sizeDelta —— 运行时每帧改尺寸的是它，做成图片反而要 9-slice。</para>
        ///
        /// <para>挂在 Canvas 根的最后 → 绘制顺序压过所有界面元素，保证箭头不会被
        /// 判定区上方的浮层 / 冷却区盖住。</para>
        /// </summary>
        private static DropArrowView BuildDropArrow(Transform canvas)
        {
            GameObject rootGo = NewUi("DropArrow", canvas);
            RectTransform root = Stretch(Rt(rootGo));

            // Arrow：pivot 在杆的起点，运行时设 anchoredPosition（= 箭头尾端）与旋转角
            GameObject arrowGo = NewUi("Arrow", rootGo.transform);
            RectTransform arrow = Rt(arrowGo);
            Box(arrow, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

            GameObject shaftGo = NewUi("Shaft", arrowGo.transform);
            Image shaft = AddImage(shaftGo, UiTheme.ArrowAttack);
            Box(Rt(shaftGo), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

            GameObject headGo = NewUi("Head", arrowGo.transform);
            Image head = AddImage(headGo, UiTheme.ArrowAttack);
            Box(Rt(headGo), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

            var view = rootGo.AddComponent<DropArrowView>();
            view.Configure(rootGo, arrow, shaft, head,
                UiLayout.DropArrowShaft, UiLayout.DropArrowHeadWidth, UiLayout.DropArrowHeadLength);

            rootGo.SetActive(false);
            return view;
        }

        // ══════════════════════════════════════════════════════
        //  2026-09-19：原先由运行时 new 出来的三个节点，改为预建
        // ══════════════════════════════════════════════════════
        //
        //  用户口径：**所有不是 Prefab 的 UI 物体都不要在代码里生成**，
        //  要在场景里预先做好 —— 代码里造出来的节点在 Hierarchy 里看不见、调不了。
        //  下面三个就是当时仅剩的三处（其余早已是「预建节点」或「Prefab 实例化」）。

        /// <summary>
        /// M12：飞牌用的自由层 + 一张「飞行中的牌」的模板。
        ///
        /// <para>层铺满画布、<c>CanvasGroup</c> 关掉交互与射线 —— 飞行卡只是画面，
        /// 不能截走指针事件（否则拖拽出牌会半路失灵）。</para>
        ///
        /// <para>模板失活：运行时 <see cref="CardTransitView"/> 从它实例化并池化复用，
        /// 「飞行中的牌长什么样、多大」在这一层就能调。</para>
        /// </summary>
        private static CardTransitView BuildTransitLayer(Transform canvas)
        {
            GameObject rootGo = NewUi("CardTransitLayer", canvas);
            RectTransform root = Stretch(Rt(rootGo));
            var group = rootGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject flightGo = NewUi("FlightTemplate", rootGo.transform);
            RectTransform flight = Rt(flightGo);
            // 锚点 / pivot 都在中心：CardTransitView 用 localPosition 直接摆到「世界角点算出的中心」
            Box(flight, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.HandCardWidth, UiLayout.HandCardHeight));
            Image face = AddImage(flightGo, Color.white);
            face.preserveAspect = true;
            face.raycastTarget = false;
            flightGo.SetActive(false);

            var view = rootGo.AddComponent<CardTransitView>();
            var so = new SerializedObject(view);
            so.FindProperty("_layer").objectReferenceValue = root;
            so.FindProperty("_flightTemplate").objectReferenceValue = face;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        /// <summary>
        /// 屏幕中央的行动提示横幅（「轮到你进攻」/「进攻结束」）。
        ///
        /// <para>不吃射线：它压着所有界面闪一下，但不会截走任何点击。
        /// 默认 <c>alpha = 0</c>，由 <see cref="ActionBannerView"/> 在提示时淡入淡出。</para>
        /// </summary>
        private static ActionBannerView BuildActionBanner(Transform canvas, TMP_FontAsset title)
        {
            GameObject go = NewUi("ActionBanner", canvas);
            RectTransform rt = Rt(go);
            Box(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, UiLayout.ActionBannerY),
                new Vector2(UiLayout.ActionBannerWidth, UiLayout.ActionBannerHeight));

            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            GameObject backGo = NewUi("Backdrop", go.transform);
            Stretch(Rt(backGo));
            Image back = AddImage(backGo, UiTheme.ActionBannerBackdrop);
            // 底衬用**卡框九宫格图**（CardBox，border 38）。⚠ 别用 PolySprite 的
            // RoundedRectangle —— 那张图没有 border，Sliced 会退化成 Simple，
            // 256×256 的方图拉到 560×104（5.4:1）会把圆角抻成一段椭圆。
            // 运行时 Sprite.Create 出来的图不是 Asset，存进 Prefab 会被丢掉，所以只能取工程资源。
            back.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Ui/CardBox.png");
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            TMP_Text label = AddText(labelGo, title, UiLayout.FontSizeActionBanner,
                UiTheme.ActionBannerText, TextAlignmentOptions.Center);

            var view = go.AddComponent<ActionBannerView>();
            var so = new SerializedObject(view);
            so.FindProperty("_group").objectReferenceValue = group;
            so.FindProperty("_label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        /// <summary>
        /// M36：屏幕中央的「一句话浮字」—— 回答「这张牌为什么点不动」。
        ///
        /// <para><b>用户口径</b>：对一张本拍不可能被加速 / 减速的牌操作时，
        /// 「弹出一段无法被加速或减速的文字，此文字应当是在中间弹出，
        /// 然后向上移动且迅速变透明消失」。</para>
        ///
        /// <para>与 <c>ActionBanner</c> 同位置、同一套九宫格底衬做法，但更小更淡 ——
        /// 横幅是「轮到你 / 打完了」这种一眼必须看到的事，浮字只是回答一个问题。
        /// 两者<b>不会同屏</b>：横幅只在怪物那一侧的节拍里闪，浮字只在玩家自己挑目标的
        /// 那几拍里出现。</para>
        ///
        /// <para>不吃射线：它出现的时机正是玩家在点冷却区的时候，挡一下就把操作打断了。</para>
        /// </summary>
        private static FloatTipView BuildFloatTip(Transform canvas, TMP_FontAsset title)
        {
            GameObject go = NewUi("FloatTip", canvas);
            RectTransform rt = Rt(go);
            Box(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, UiLayout.FloatTipY),
                new Vector2(UiLayout.FloatTipWidth, UiLayout.FloatTipHeight));

            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            GameObject backGo = NewUi("Backdrop", go.transform);
            Stretch(Rt(backGo));
            Image back = AddImage(backGo, UiTheme.FloatTipBackdrop);
            // 同 ActionBanner：底衬必须用带 border 的九宫格图，否则圆角会被抻成椭圆。
            back.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Ui/CardBox.png");
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            TMP_Text label = AddText(labelGo, title, UiLayout.FontSizeFloatTip,
                UiTheme.FloatTipText, TextAlignmentOptions.Center);
            label.raycastTarget = false;

            var view = go.AddComponent<FloatTipView>();
            view.Configure(group, label,
                UiLayout.FloatTipSeconds, UiLayout.FloatTipFadeInSeconds, UiLayout.FloatTipRise);
            return view;
        }

        /// <summary>
        /// M16/M21：手牌区左侧的「已准备光环」标记行 + 拖光环时的落区高亮。
        ///
        /// <para><b>为什么建在 Canvas 根的最后</b>：ArtLayer 在 Canvas 里的兄弟序很靠前
        /// （紧跟 Backdrop），界面全在它上面 —— 标记条若压进 ArtLayer 就会被手牌盖住。
        /// 铺满画布 + 最后一位 = 拖到哪都看得见。</para>
        ///
        /// <para><b>M21：槽位 <c>Slot0…Slot7</c> 由这里生成</b>（摆在手牌区左侧，见
        /// <see cref="UiLayout.PreparedAuraX"/> / <see cref="UiLayout.PreparedAuraRowCenterY"/>）。
        /// 它们原本是手摆在 Prefab 里的，被本类 <c>Cleanup</c>（整棵删掉 "PreparedAura" 再建）
        /// 冲掉过 —— 生成出来才活得过重跑。本层是<b>铺满画布</b>的，局部原点 = 画布中心，
        /// 所以槽位坐标要减掉半个画布。</para>
        ///
        /// <para>判定区矩形与图标模板仍是**运行时接线**（前者归 <see cref="HandView"/> 持有、
        /// 后者来自 <see cref="HudBuffView.IconTemplate"/>），所以这里只建节点、不复制版式常量。</para>
        /// </summary>
        private static PreparedAuraView BuildPreparedBar(Transform canvas)
        {
            GameObject go = NewUi("PreparedAura", canvas);
            RectTransform rt = Rt(go);
            Stretch(rt);
            rt.SetAsLastSibling();

            // 槽位：首格左缘 = PreparedAuraX，中心线 = PreparedAuraRowCenterY（都按画布坐标算，
            // 再换算到「以画布中心为原点」的局部坐标 —— 本节点是铺满画布的）。
            float half = UiLayout.BuffIconSize * 0.5f;

            for (int i = 0; i < UiLayout.BuffIconSlots; i++)
            {
                float cx = UiLayout.PreparedAuraX + half + UiLayout.BuffIconSpacing * i;
                float cy = UiLayout.PreparedAuraRowCenterY;

                GameObject slotGo = NewUi("Slot" + i, go.transform);
                Box(Rt(slotGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(cx - UiLayout.ReferenceWidth * 0.5f, cy - UiLayout.ReferenceHeight * 0.5f),
                    new Vector2(UiLayout.BuffIconSize, UiLayout.BuffIconSize));
            }

            // ⚠ 这里原来还有一个 `ZoneHighlight` 子节点（拖「已准备」标记时整片亮起的保留区底）。
            //   M21 把取消改回「再拖一下标记」之后它没有含义了（标记不再有「必须待在这里」的约束），
            //   按项目惯例整块删掉 —— 不只是停用，连节点一起不再建。
            var view = go.AddComponent<PreparedAuraView>();
            return view;
        }

        /// <summary>
        /// 落区提示底的<strong>占位</strong>颜色 —— 只在「这次重建新建了这个节点」时用一次。
        ///
        /// <para><b>⚠ 正式颜色在 Prefab 里调</b>：选中 <c>HandArea/AuraDropHint</c> → Image → Color。
        /// 运行时不写这个颜色，所以在 Play 里调的、在 Prefab 里调的都能留住
        /// （用户 2026-09-19 明确要求：这类提示的颜色不要用代码写）。
        /// <b>重跑 M8 会按这个占位色重建节点</b>，调完色就别再跑了
        /// —— 与 M18「位置搬进 Prefab，但构建器仍会按常量重算」是同一笔代价。</para>
        ///
        /// <para><b>M21 把透明度从 0.13 降到 0.06</b>（用户 2026-09-20：提示底改成方块并「降低不透明度」）。
        /// 方块是靠 <see cref="BuildAuraDropHint"/> 不赋 Sprite 实现的（无图 = 纯白矩形），
        /// 所以这里只需要管 alpha。</para>
        /// </summary>
        private static readonly Color HintPlaceholder = new Color(1f, 1f, 1f, 0.06f);

        private static HandView BuildHandExtras(Transform handArea, Transform handRow, CardView handPrefab)
        {
            var view = handArea.GetComponent<HandView>();
            if (view == null)
            {
                view = handArea.gameObject.AddComponent<HandView>();
            }

            GameObject hint = BuildAuraDropHint(handArea);

            var so = new SerializedObject(view);
            so.FindProperty("_cardPrefab").objectReferenceValue = handPrefab;
            so.FindProperty("_row").objectReferenceValue = handRow;
            so.FindProperty("_auraDropHint").objectReferenceValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        /// <summary>
        /// M15：把 HudBuff 的光环图标拖到<b>自己的手牌区</b>时的落区提示底（很淡的一片）。
        ///
        /// <para><b>落区（M19 改）</b>：光环的使用判定是底部通栏的 <c>HandArea</c>，
        /// 不是出牌的中央判定区 —— 所以这一层铺满 HandArea（锚点 0…1），
        /// 拖光环经过手牌区时由 <see cref="HandView.SetAuraDropHighlight"/> 点亮。</para>
        ///
        /// <para>插在 <c>HandArea</c> 的<b>第一个</b>子节点上 —— 手牌是后面才摆进来的，
        /// 这样提示底永远压在手牌<b>下面</b>（不然一拖过来整排手牌就被蒙上一层色）。
        /// <c>raycastTarget=false</c>，不挡任何操作。</para>
        ///
        /// <para><b>颜色不在代码里</b>：这里只给一个中性占位色（见 <see cref="HintPlaceholder"/>），
        /// 正式颜色在 Prefab 里选中这个节点改 Image.color；运行时一个字节都不写。</para>
        ///
        /// <para><b>M21：改成纯方块</b>（用户 2026-09-20）。原来赋的是
        /// <c>ThirdParty/PolySprite/RoundedRectangle.png</c> + <c>Type.Simple</c> ——
        /// 那张图是 256×256 且<b>没有 border</b>，Simple 模式把它拉成 1920×344（5.6:1）时，
        /// 圆角会被抻成一大段弧，整片看起来是个畸形的胶囊而不是落区。
        /// 现在<b>不赋 Sprite</b>：UGUI 的 Image 在没有图时画的就是一块纯色矩形，
        /// 形状由 RectTransform 说了算，配上「更低的 alpha」就只剩一层极淡的通栏底色。
        /// （顺带把「贴图必须是工程资源」那条约束也绕开了。）</para>
        /// </summary>
        private static GameObject BuildAuraDropHint(Transform handArea)
        {
            GameObject go = NewUi("AuraDropHint", handArea);
            go.transform.SetAsFirstSibling();

            RectTransform rt = Rt(go);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = AddImage(go, HintPlaceholder);
            img.sprite = null;              // 无图 = 纯白方块（见上面的说明）
            img.type = Image.Type.Simple;   // 无图时本项无效，写出来只为「这里不是九宫格」留证
            img.raycastTarget = false;

            go.SetActive(false);
            return go;
        }

        // ══════════════════════════════════════════════════════
        //  M25 · 查看对方手牌弹窗
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 「查看对方手牌」浮层（雷云 <c>ad</c> / 狂躁蘑菇 <c>w</c> 的第 2 个 α 效果）。
        ///
        /// <para><b>为什么必须建在 Canvas 子节点的最后一个</b>：Canvas 里同层节点的绘制顺序
        /// = 兄弟顺序，最后建 = 画在最上面。这块浮层要压住准备标记条（<c>PreparedAura</c>，
        /// 它自己还会 <c>SetAsLastSibling</c>）、飞牌层与中央提示 —— 否则拖光环留下的标记
        /// 会浮在弹窗上面。</para>
        ///
        /// <para><b>牌位 <c>Slot0…Slot7</c> 由这里生成，不手摆在 Prefab 里</b>：
        /// 本类的 <see cref="Cleanup"/> 会整棵删掉 <c>PeekLayer</c> 再重建，手摆的节点
        /// 活不过一次重跑（M21 在 <c>PreparedAura</c> 上正是这么丢的，且零报错）。</para>
        ///
        /// <para>面板与牌位的尺寸 / 位置全部来自 <see cref="UiLayout"/> 的 M25 段常量；
        /// 运行时按张数自适应（1–4 张一行、5–8 张两行、面板只往纵向长）在
        /// <see cref="PeekView.Layout"/> 里算 —— 这里只保证「8 个槽都在、尺寸一致」，
        /// 不复制那份版式算术。</para>
        ///
        /// <para><b>初始隐藏写进 Prefab</b>（结尾那句 <c>SetActive(false)</c>），
        /// 不在 <c>Awake</c> 里做 —— 「构建器置失活 + 运行时 Show」的组件在 <c>Awake</c> 里
        /// <c>Hide()</c> 会把本局第一次 Show 一起关回去（本工程已在
        /// <c>DropArrowView</c> / <c>PlayedCardView</c> 上中过两次）。</para>
        /// </summary>
        private static PeekView BuildPeekLayer(Transform canvas, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject go = NewUi("PeekLayer", canvas);
            Stretch(Rt(go));
            Rt(go).SetAsLastSibling();

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;              // 默认全透明
            group.interactable = false;
            group.blocksRaycasts = false;  // 由 PeekView.Show 打开

            // ── 遮罩：铺满画布，吃掉下层所有点击 ────────────────
            // 面板本身不吃射线（它只是块底图），牌位各自吃自己的那片 —— 所以「点空白处」
            // 落在遮罩上，什么都不发生，也不会穿到下面的手牌。
            GameObject veilGo = NewUi("Veil", go.transform);
            Stretch(Rt(veilGo));
            Image veil = AddImage(veilGo, UiTheme.PeekVeil);
            veil.raycastTarget = true;

            // ── 面板 ────────────────────────────────────────────
            GameObject panelGo = NewUi("Panel", go.transform);
            RectTransform panel = Rt(panelGo);
            Box(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.PeekPanelWidth, UiLayout.PeekPanelHeightOneRow));
            Image panelImg = AddImage(panelGo, Color.white);
            panelImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PeekPanelPath);
            // 九宫格：这张图横向**不拉伸**（左右 border = 0），纵向拉伸中间那条纯色躯干。
            // border 只存在 .meta 里、重导就悄悄丢 → 每次构建都由 EnsurePeekImporters 对一遍。
            panelImg.type = Image.Type.Sliced;
            panelImg.raycastTarget = true;

            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.PeekTitleTop),
                new Vector2(UiLayout.PeekTitleWidth, UiLayout.PeekTitleHeight));
            TMP_Text titleLabel = AddText(titleGo, title, UiLayout.FontSizePeekTitle,
                UiTheme.TextPrimary, TextAlignmentOptions.Center, "查看对方手牌");

            // ── 8 个预建牌位（运行时只激活前 N 个）─────────────
            var slots = new PeekCardSlot[UiLayout.PeekSlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = BuildPeekSlot(panelGo.transform, i, body);
            }

            var view = go.AddComponent<PeekView>();
            var so = new SerializedObject(view);
            so.FindProperty("_group").objectReferenceValue = group;
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_title").objectReferenceValue = titleLabel;
            // 牌背的兜底来源。正常走 BattleArtLibrary，但那本库由 ArtImportBuilder 生成 ——
            // 新素材还没进库时 PeekCardBack 是 null，牌位会被刷成一块灰方块且零报错。
            // 构建期直接从 AssetDatabase 取一份，就与建库顺序无关了。
            so.FindProperty("_cardBackFallback").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Sprite>(PeekCardBackPath);

            SerializedProperty slotsProp = so.FindProperty("_slots");
            if (slotsProp != null)
            {
                slotsProp.arraySize = slots.Length;
                for (int i = 0; i < slots.Length; i++)
                {
                    slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// 一个会翻面的牌位。
        ///
        /// <para>里外只有<b>一张</b> <see cref="Image"/>：翻面靠「换 sprite + 横向压缩」
        /// 实现，不是正反两张图对叠 —— 对叠在 <c>scale.x</c> 掠过 0 的那一帧会同时看到两面。</para>
        ///
        /// <para>结果角标（「力量 N · 送入冷却 / 未送入冷却」）的底衬<b>不赋 Sprite</b>：
        /// UGUI 的 Image 没有图时画的就是一块纯色矩形，形状完全由 RectTransform 决定。
        /// 这也是本工程处理「窄条 / 通栏」的既定做法（见 <c>BuildAuraDropHint</c>）——
        /// 角标是 186×34 ≈ 5.5:1，拿任何一张无 border 的圆角图去 Simple 拉伸都会把圆角
        /// 抻成一段弧。</para>
        /// </summary>
        private static PeekCardSlot BuildPeekSlot(Transform panel, int index, TMP_FontAsset body)
        {
            GameObject slotGo = NewUi("Slot" + index, panel);
            RectTransform slot = Rt(slotGo);
            // 锚在面板中心：PeekView.Layout 算的就是「以面板中心为原点」的坐标
            Box(slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.PeekCardWidth, UiLayout.PeekCardHeight));

            GameObject faceGo = NewUi("Face", slotGo.transform);
            Stretch(Rt(faceGo));
            Image face = AddImage(faceGo, Color.white);
            face.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PeekCardBackPath);
            // 图还没导进来时退化成一块深色方块 —— 至少位置和数量是对的
            face.color = face.sprite != null ? Color.white : UiTheme.MiniCardFace;
            // 正面是烘好的整张卡面（622×872），与牌位 186×256 的比例不完全一致 → 别拉变形
            face.preserveAspect = true;
            // 射线由 PeekView 在 Show 时打开（这一拍才允许点），Awake 阶段绝不写
            face.raycastTarget = false;

            GameObject badgeGo = NewUi("Badge", slotGo.transform);
            RectTransform badge = Rt(badgeGo);
            badge.anchorMin = new Vector2(0f, 0f);
            badge.anchorMax = new Vector2(1f, 0f);    // 横向跟牌位等宽
            badge.pivot = new Vector2(0.5f, 0f);
            badge.sizeDelta = new Vector2(0f, UiLayout.PeekBadgeHeight);
            badge.anchoredPosition = new Vector2(0f, UiLayout.PeekBadgeBottom);

            Image badgeBack = AddImage(badgeGo, UiTheme.PeekBadgeBackdrop);
            badgeBack.sprite = null;                 // 无图 = 纯色矩形（见上面的说明）
            badgeBack.type = Image.Type.Simple;      // 无图时本项无效，写出来只为留证

            GameObject badgeTextGo = NewUi("Label", badgeGo.transform);
            Stretch(Rt(badgeTextGo));
            TMP_Text badgeText = AddText(badgeTextGo, body, UiLayout.FontSizePeekBadge,
                UiTheme.PeekKept, TextAlignmentOptions.Center);
            // 文案是「力量 12 · 未送入冷却」这种 10 个字上下的短句，牌宽只有 186 ——
            // 开自动缩放兜住不同位数（与卡名框 ⑬ 同一套做法）
            badgeText.enableAutoSizing = true;
            badgeText.fontSizeMin = UiLayout.FontSizePeekBadge * 0.62f;
            badgeText.fontSizeMax = UiLayout.FontSizePeekBadge;

            badgeGo.SetActive(false);                // 翻完面才亮

            var comp = slotGo.AddComponent<PeekCardSlot>();
            var so = new SerializedObject(comp);
            so.FindProperty("_face").objectReferenceValue = face;
            so.FindProperty("_badge").objectReferenceValue = badgeGo;
            so.FindProperty("_badgeText").objectReferenceValue = badgeText;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        /// <summary>Peek 牌背图的路径。</summary>
        private const string PeekCardBackPath = "Assets/Art/Ui/Peek_CardBack.png";

        // ══════════════════════════════════════════════════════
        //  M28 · 按住怪物手牌数 → 查看它的手牌
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 「查看怪物手牌」浮层（按住 <c>ArtLayer/HandCount_Monster</c> 显示，松手即收）。
        ///
        /// <para><b>为什么借 M25 的 <c>Peek_Panel</c> 九宫格</b>：两块浮层是同一个语义
        /// （「给你看几张牌」），玩家不该觉得是两套东西；而且 <c>Peek_Panel</c> 的
        /// 左右 border = 0（横向不拉伸），本面板宽度恰好也按 <see cref="UiLayout.PeekPanelWidth"/>
        /// 定死，尺寸账完全对得上，不需要再切一张图。</para>
        ///
        /// <para><b>遮罩 <c>raycastTarget = false</c></b>：与 M25 / M27 两块的遮罩相反。
        /// 那两块是「等玩家点一张牌」的模态弹窗，必须吃掉下层点击；这一块是纯查看 ——
        /// 玩家按住标签的整段时间里，手牌区、冷却区、判定区都还在原位，
        /// 遮罩一旦吃射线，松手前就什么都点不了了（玩家会以为卡死了）。
        /// 它也不参与 <c>BattleDriver.BeatHold</c>：这是玩家自己按出来的浮层，
        /// 不是引擎给的一拍，不该阻塞驱动。</para>
        ///
        /// <para><b>建在最后 = 画在最上</b>。它要压住 <c>PeekLayer</c> 与 <c>HandPickLayer</c>
        /// —— 严格说三者不会同屏，但绘制顺序这条要守住。</para>
        ///
        /// <para><b>初始隐藏写进 Prefab</b>（结尾那句 <c>SetActive(false)</c>），
        /// 不在 <c>Awake</c> 里做 —— 「构建器置失活 + 运行时 Show」的组件在 <c>Awake</c> 里
        /// <c>Hide()</c> 会把本局第一次 Show 一起关回去（<c>DropArrowView</c> /
        /// <c>PlayedCardView</c> 已中过两次）。</para>
        /// </summary>
        private static MonsterHandView BuildMonsterHandLayer(Transform canvas, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject go = NewUi("MonsterHandLayer", canvas);
            Stretch(Rt(go));
            Rt(go).SetAsLastSibling();

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;              // 默认全透明
            group.interactable = false;
            group.blocksRaycasts = false;  // 由 MonsterHandView.Bind 打开

            // ── 遮罩：只做视觉压暗，不吃射线（见上面的说明）─────
            GameObject veilGo = NewUi("Veil", go.transform);
            Stretch(Rt(veilGo));
            Image veil = AddImage(veilGo, UiTheme.MonsterHandVeil);
            veil.raycastTarget = false;

            // ── 面板 ────────────────────────────────────────────
            GameObject panelGo = NewUi("Panel", go.transform);
            RectTransform panel = Rt(panelGo);
            Box(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.MonsterHandPanelWidth, UiLayout.MonsterHandPanelHeightOneRow));
            Image panelImg = AddImage(panelGo, Color.white);
            panelImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PeekPanelPath);
            // 与 M25 同款九宫格：横向**不拉伸**（左右 border = 0），纵向拉伸中间那条纯色躯干。
            // border 只存在 .meta 里、重导就悄悄丢 → 每次构建都由 EnsurePeekImporters 对一遍。
            panelImg.type = Image.Type.Sliced;
            panelImg.raycastTarget = false;   // 纯查看面板：整块都不吃射线

            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.MonsterHandTitleTop),
                new Vector2(UiLayout.MonsterHandTitleWidth, UiLayout.MonsterHandTitleHeight));
            TMP_Text titleLabel = AddText(titleGo, title, UiLayout.FontSizeMonsterHandTitle,
                UiTheme.TextPrimary, TextAlignmentOptions.Center, "怪物手牌");

            // 副标题（「已知 3 / 5」这种计数）—— 标题正下方一点点，落在同一块牌位里
            GameObject subGo = NewUi("SubTitle", panelGo.transform);
            Box(Rt(subGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.MonsterHandSubTop),
                new Vector2(UiLayout.MonsterHandTitleWidth, UiLayout.MonsterHandSubHeight));
            TMP_Text subTitle = AddText(subGo, body, UiLayout.FontSizeMonsterHandSub,
                UiTheme.TextSecondary, TextAlignmentOptions.Center, "已知 0 / 0");

            // ── 8 个预建牌位（运行时只激活前 N 个）─────────────
            // ⚠ 装饰节点（Title / SubTitle）必须先建、牌位后建 ——
            //   MonsterHandView.Layout 刻意不调 SetSiblingIndex（M27 血案：父节点
            //   前置有装饰节点时按序号硬插必然把牌位挤到装饰后面去）。牌位只改
            //   anchoredPosition，靠「后建 = 在上」自然压住面板底纹。
            var slots = new MonsterHandSlot[UiLayout.MonsterHandSlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = BuildMonsterHandSlot(panelGo.transform, i, body);
            }

            var view = go.AddComponent<MonsterHandView>();
            var so = new SerializedObject(view);
            so.FindProperty("_group").objectReferenceValue = group;
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_title").objectReferenceValue = titleLabel;
            so.FindProperty("_subTitle").objectReferenceValue = subTitle;
            // 牌背的兜底来源。正常走 CardArtLibrary，但那本库由 ArtImportBuilder 生成 ——
            // 新素材还没进库时是 null，牌位会被刷成一块灰方块且零报错。
            // 构建期直接从 AssetDatabase 取一份，就与建库顺序无关了。
            so.FindProperty("_cardBackFallback").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Sprite>(PeekCardBackPath);

            SerializedProperty slotsProp = so.FindProperty("_slots");
            if (slotsProp != null)
            {
                slotsProp.arraySize = slots.Length;
                for (int i = 0; i < slots.Length; i++)
                {
                    slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// 一格「怪物手牌」牌位：卡面 + 一层「未知」压暗。
        ///
        /// <para><b>比 M25 的 <c>PeekCardSlot</c> 简单一档</b> —— 它不翻面、不吃射线、
        /// 没有角标。玩家按住标签看到的是一次性的成品画面：已知的露正面、未知的留牌背，
        /// 松手就没了，中间没有任何交互。</para>
        ///
        /// <para><b>压暗层的底衬不赋 Sprite</b>：UGUI 的 Image 没有图时画的就是一块纯色
        /// 矩形，形状完全由 RectTransform 决定。盖在牌背上而不是另画一张「未知牌背图」，
        /// 是为了让已知 / 未知两种格子共用同一张底图、只差一层色调 ——
        /// 玩家一眼就能看出「这几张跟我手上的是同一种东西」。</para>
        /// </summary>
        private static MonsterHandSlot BuildMonsterHandSlot(Transform panel, int index, TMP_FontAsset body)
        {
            GameObject slotGo = NewUi("Slot" + index, panel);
            RectTransform slot = Rt(slotGo);
            // 锚在面板中心：MonsterHandView.Layout 算的就是「以面板中心为原点」的坐标
            Box(slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.MonsterHandCardWidth, UiLayout.MonsterHandCardHeight));

            GameObject faceGo = NewUi("Face", slotGo.transform);
            Stretch(Rt(faceGo));
            Image face = AddImage(faceGo, Color.white);
            face.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PeekCardBackPath);
            // 图还没导进来时退化成一块深色方块 —— 至少位置和数量是对的
            face.color = face.sprite != null ? Color.white : UiTheme.MiniCardFace;
            // 正面是烘好的整张卡面（622×872），与牌位比例不完全一致 → 别拉变形
            face.preserveAspect = true;
            face.raycastTarget = false;   // 纯展示，永远不吃射线

            // 「未知」压暗层：只在没有正面卡面时亮（由 MonsterHandSlot.Bind 切）
            GameObject dimGo = NewUi("Dim", slotGo.transform);
            Stretch(Rt(dimGo));
            Image dim = AddImage(dimGo, UiTheme.MonsterHandUnknownDim);
            dim.sprite = null;            // 无图 = 纯色矩形（见上面的说明）
            dim.raycastTarget = false;

            var comp = slotGo.AddComponent<MonsterHandSlot>();
            var so = new SerializedObject(comp);
            so.FindProperty("_face").objectReferenceValue = face;
            so.FindProperty("_dim").objectReferenceValue = dim;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // ══════════════════════════════════════════════════════
        //  M27 · 手牌选择弹窗（磁暴 / 充能 / 电弧）
        // ══════════════════════════════════════════════════════

        /// <summary>M27 面板图的路径（复用 M25 切的 Peek_Frame）。</summary>
        private const string HandPickPanelPath = "Assets/Art/Ui/Peek_Frame.png";

        /// <summary>
        /// 「手牌选择弹窗」（磁暴 / 充能 / 电弧）。
        ///
        /// <para><b>为什么必须建在 Canvas 子节点的最后一个</b>：Canvas 里同层节点的绘制顺序
        /// = 兄弟顺序，最后建 = 画在最上面。这块浮层要压住准备标记条、飞牌层与中央提示，
        /// 也要压在 M25 的 <c>PeekLayer</c> 之上（两者不会同屏，但顺序要守住）。</para>
        ///
        /// <para><b>卡框 <c>Slot0…Slot7</c> 由这里生成，不手摆在 Prefab 里</b>：
        /// <see cref="Cleanup"/> 会整棵删掉 <c>HandPickLayer</c> 再重建，手摆的节点
        /// 活不过一次重跑（M21 在 <c>PreparedAura</c> 上正是这么丢的，且零报错）。</para>
        ///
        /// <para>面板尺寸 / 卡框尺寸 / 动画时长全部来自 <see cref="UiLayout"/> 的 M27 段常量；
        /// 运行时按已选张数横向变宽在 <see cref="HandPickView.Layout"/> 里算 ——
        /// 这里只保证「8 个框都在、尺寸一致」，不复制那份版式算术。</para>
        ///
        /// <para><b>初始隐藏写进 Prefab</b>（结尾那句 <c>SetActive(false)</c>），
        /// 不在 <c>Awake</c> 里做 —— 「构建器置失活 + 运行时 Show」的组件在 <c>Awake</c> 里
        /// <c>Hide()</c> 会把本局第一次 Show 一起关回去（本工程已在
        /// <c>DropArrowView</c> / <c>PlayedCardView</c> 上中过两次）。</para>
        /// </summary>
        private static HandPickView BuildHandPickLayer(Transform canvas, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject go = NewUi("HandPickLayer", canvas);
            Stretch(Rt(go));
            Rt(go).SetAsLastSibling();

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;   // 由 HandPickView.Show 打开

            // ── 遮罩：**不吃射线**，让下层手牌仍然点得到 ──────────
            //
            // ⚠ 这里刻意与 M25 的 PeekLayer 不同（那个是 raycastTarget = true，吃掉下层所有点击）：
            //   本弹窗的**核心交互就是点自己的手牌**（点一张 → 飞进卡框 → 再点确认），
            //   而 HandPickLayer 是 SetAsLastSibling 摆在最上层的 —— 遮罩一旦吃射线，
            //   手牌区就整个点不动，玩家根本没有办法选牌（2026-09-22 实测到的正是这个）。
            //   所以遮罩只做「视觉压暗」：把颜色调浅（见 UiTheme.HandPickVeil）让它像一层纱，
            //   同时 raycastTarget = false，点击径直穿到下面的 HandArea。
            //
            //   代价是「点手牌区以外的空白」也会穿到下层（比如误触冷却区 / 角色）。
            //   这是可接受的：那一拍 HandView.SetPlayable 只允许候选牌可点，
            //   其余牌与冷却区的点击都会被 BattleUi 按 Options 过滤掉（铁律 3），
            //   而本弹窗的确认键是唯一提交口 —— 不会因为穿透而产生错误的出牌。
            GameObject veilGo = NewUi("Veil", go.transform);
            Stretch(Rt(veilGo));
            Image veil = AddImage(veilGo, UiTheme.HandPickVeil);
            veil.raycastTarget = false;

            // ── 面板 ────────────────────────────────────────────
            GameObject panelGo = NewUi("Panel", go.transform);
            RectTransform panel = Rt(panelGo);
            Box(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.HandPickPanelBaseWidth, UiLayout.HandPickPanelHeight));
            Image panelImg = AddImage(panelGo, Color.white);
            panelImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(HandPickPanelPath);
            // 九宫格：横向拉伸（左右 border 落在躯干里），纵向**不**拉伸（上下 border 保住装饰）。
            // border 只存在 .meta 里、重导就悄悄丢 → 每次构建都由 EnsureHandPickImporters 对一遍。
            panelImg.type = Image.Type.Sliced;
            // ⚠ 不吃射线：面板底边 y = (1080−556)/2 = 262，而手牌扇形最高那张的顶角到 y ≈ 294 ——
            //   两者重迭了顶上约 31 px。对「牌整体在面板下方」的情形无碍（点牌身仍然过），
            //   但让一块纯装饰的底板去吃掉手牌顶部那一条命中区是没道理的：牌多、摆角大时
            //   玩家瞄着卡面上边点，反馈就是「点了没反应」。它没有任何点击语义，
            //   交给下面的手牌即可（装饰不参与交互，与 AuraDropHint 一个口径）。
            panelImg.raycastTarget = false;

            GameObject titleGo = NewUi("Title", panelGo.transform);
            Box(Rt(titleGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.HandPickTitleTop),
                new Vector2(UiLayout.HandPickTitleWidth, UiLayout.HandPickTitleHeight));
            TMP_Text titleLabel = AddText(titleGo, title, UiLayout.FontSizeHandPickTitle,
                UiTheme.TextPrimary, TextAlignmentOptions.Center, "选择手牌");
            // M30：标题从「卡名」改成「效果名」之后变长（最长「送入冷却增加力量（可选择多张）」= 15 字），
            // 30 号字放不下 470 宽的牌位。开自动缩容：
            //   · AddText 默认 enableWordWrapping = false + overflowMode = Overflow，
            //     长文案会直接**溢出面板**（在美术图上就是压到内框上去），而且零报错；
            //   · 这里改成「不换行 + 缩到框内」，配合 FontSizeHandPickTitleMin 兜底。
            titleLabel.enableAutoSizing = true;
            titleLabel.fontSizeMin = UiLayout.FontSizeHandPickTitleMin;
            titleLabel.fontSizeMax = UiLayout.FontSizeHandPickTitleBase;
            titleLabel.overflowMode = TextOverflowModes.Truncate;

            // 副标题（「0 / 3」这种计数）—— 挂在标题正下方一点的同一块牌位里
            GameObject subGo = NewUi("SubTitle", panelGo.transform);
            Box(Rt(subGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.HandPickTitleTop - UiLayout.HandPickTitleHeight
                                - UiLayout.HandPickSubTitleGap),
                new Vector2(UiLayout.HandPickTitleWidth, UiLayout.HandPickSubTitleHeight));
            TMP_Text subTitle = AddText(subGo, body, UiLayout.FontSizeHandPickSubTitle,
                UiTheme.TextSecondary, TextAlignmentOptions.Center, "0 / 3");

            // ── 卡框区（运行时按张数改宽 / 改位置）──────────────
            // 锚在面板中心：HandPickView.Layout 算的就是「以面板中心为原点」的坐标。
            GameObject areaGo = NewUi("SlotArea", panelGo.transform);
            RectTransform area = Rt(areaGo);
            Box(area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.HandPickSlotWidth, UiLayout.HandPickSlotAreaHeight));

            // 盖住面板原图内框装饰的纯色条（有牌时才亮）。
            // 面板横向拉宽时原图内框的圆角描边 + 中心菱形花纹会跟着变形，所以整条压住它。
            // 它**铺满 SlotArea**：SlotArea 已由 HandPickView.Layout 按张数算好宽高，
            // 这里跟着拉伸即可（Stretch 的 offset 在运行时不会与 sizeDelta 打架，
            // 因为没人会去改这个节点的 sizeDelta）。
            GameObject backdropGo = NewUi("Backdrop", areaGo.transform);
            // 上下各多盖 8 px：原图内框的亮色描边外还有一层薄辉光，只盖到 280 会在上下
            // 各留一条发亮的边。左右不扩 —— 那两侧是面板躯干，本来就该露出来。
            Stretch(Rt(backdropGo), 0f, -8f, 0f, -8f);
            Image areaBackdrop = AddImage(backdropGo, UiTheme.HandPickPanelTorso);
            areaBackdrop.sprite = null;      // 无图 = 纯色矩形（形状完全由 RectTransform 决定）

            // 空卡框：一张没选时露出的那一块「可以放牌的地方」
            GameObject emptyGo = NewUi("EmptyFrame", areaGo.transform);
            RectTransform empty = Rt(emptyGo);
            Box(empty, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.HandPickSlotWidth, UiLayout.HandPickSlotHeight));
            Image emptyFill = AddImage(emptyGo, UiTheme.HandPickEmptyFill);
            emptyFill.sprite = null;
            // 描边：四根细条，颜色是「这里可以放牌」的冷蓝灰
            MakeFrame(emptyGo.transform, UiLayout.HandPickEmptyOutline, UiTheme.HandPickEmptyEdge);

            // ── 8 个预建卡框（运行时只激活前 N 个）─────────────
            var slots = new HandPickSlot[UiLayout.PeekSlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = BuildHandPickSlot(areaGo.transform, i);
            }

            // ── 确认键 ─────────────────────────────────────────
            //
            // ⚠ M31（2026-09-22 用户第二条）：「确认键的背景框不需要随着卡牌的逐渐增多而变宽」。
            //
            //   原来 ConfirmButton 直接挂在 Panel 下 —— 而 Panel 的宽度会被
            //   HandPickView.Layout 按张数改成 HandPickPanelWidthFor(count)
            //   （431 → 670 → 885 …）。按钮自己虽然量着 258 宽没变，但它座在面板底边上，
            //   玩家看到的「确认键那块背景」是**按钮 + 它底下那截面板**，面板一拉宽，
            //   整个观感就是确认键跟着变宽了。
            //
            //   所以把按钮移出 Panel，改挂在一个**固定尺寸**的中间层 ConfirmSeat 上，
            //   再把这个中间层挂在 HandPickLayer 根下（根是全屏 Stretch，不随面板变）。
            //   中间层的尺寸就锁死成面板的基准宽度 —— 按钮相对它居中，
            //   面板再怎么长，按钮与它脚下的那 431 宽底座都待在原地。
            //
            //   位置换算：原来锚 (0.5, 0)、y = CenterFromBottom − Height/2，是相对**面板中心**的。
            //   面板中心在屏幕中心往上 HandPickPanelHeight/2 处，所以相对屏幕中心
            //   y = HandPickPanelHeight/2 − CenterFromBottom。两者在同一根竖直轴上，
            //   按钮的视觉位置**逐像素不变**（只是不再跟着面板横向漂）。
            //
            //   ⚠⚠ 上面这段口径本身是对的，但 M31 实现时符号写反了（写成了
            //   `Height/2 − CenterFromBottom` 那个正号版），按钮于是被镜像到面板**顶部**、
            //   压住标题牌位与「N / M」计数，底面那块原图自带的按钮座空着 —— 用户
            //   2026-09-23 的截图就是这一条。现在这行换算收敛到
            //   UiLayout.HandPickConfirmCenterYFromCenter()（负号 + 解释都在那边）。
            GameObject seatGo = NewUi("ConfirmSeat", go.transform);
            RectTransform seat = Rt(seatGo);
            Box(seat, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, UiLayout.HandPickConfirmCenterYFromCenter()),
                new Vector2(UiLayout.HandPickPanelBaseWidth, UiLayout.HandPickConfirmHeight));
            // 纯定位容器：不吃射线（吃射线会在手牌区正中横一条看不见的挡板）。
            // 也不挂 Image —— 用户要的就是「别再多一块变宽的背景」。

            GameObject btnGo = NewUi("ConfirmButton", seatGo.transform);
            Box(Rt(btnGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.HandPickConfirmWidth, UiLayout.HandPickConfirmHeight));

            // ⚠ ColorTint 的 tint 是作用在 CanvasRenderer 上的，与 Image.color **相乘**
            //   → Image.color 必须是纯白，两态的实色交给 colors 那两个常量。
            var btnImg = btnGo.AddComponent<Image>();
            //
            // ⚠ 2026-09-23（用户第二条）：「移除掉这个确认键的背景，就用按钮座那个背景」。
            //   所以这里**刻意不给 sprite** —— Image 没有图时画的就是一块纯色矩形，
            //   而可用色是透明的（UiTheme.HandPickConfirmOn），落下来就是「什么都没有」，
            //   露出的是 Peek_Frame.png 自带的那块底部按钮座。
            //   保留 Image 组件的唯一理由是**它要当命中区**：UGUI 的
            //   Image.IsRaycastLocationValid 在 alphaHitTestMinimumThreshold == 0 时
            //   直接 return true —— 也就是说「透明 Image 照样吃射线」，不需要另加不可见 Graphic。
            //
            //   （原来这里挂的是 HandPick_Confirm.png，横向九宫格拉伸。它纵向被 174 → 58
            //    硬压成一条发白的扁条，与面板自带的座对不齐 —— 那正是用户圈红框要挪走的东西。
            //    素材与它的 border 常量都还留着，见 UiLayout 的 M30 段。）
            btnImg.sprite = null;
            btnImg.color = UiTheme.HandPickConfirmOn;   // 纯白：让 colors 的 tint 说了算
            btnImg.raycastTarget = true;

            var confirm = btnGo.AddComponent<Button>();
            confirm.transition = Selectable.Transition.ColorTint;
            confirm.targetGraphic = btnImg;
            var colors = confirm.colors;
            colors.normalColor = UiTheme.HandPickConfirmOn;
            colors.highlightedColor = UiTheme.HandPickConfirmOn;
            colors.pressedColor = UiTheme.HandPickConfirmOn;
            colors.selectedColor = UiTheme.HandPickConfirmOn;
            colors.disabledColor = UiTheme.HandPickConfirmOff;
            colors.fadeDuration = 0.08f;
            confirm.colors = colors;

            // 初始 disabled（用 disabledColor 呈现）。运行时 HandPickView.Show 会立刻调
            // RefreshConfirm 按真实 MinSelect 覆盖它；这里的值只是「万一没走到那段代码」
            // 时的安全默认 —— 让按钮偏向不可按，而不是偏向可按。
            confirm.interactable = false;

            GameObject btnLabelGo = NewUi("Label", btnGo.transform);
            Stretch(Rt(btnLabelGo));
            TMP_Text confirmLabel = AddText(btnLabelGo, title, UiLayout.FontSizeHandPickConfirm,
                UiTheme.TextPrimary, TextAlignmentOptions.Center, "确认");

            var view = go.AddComponent<HandPickView>();
            var so = new SerializedObject(view);
            so.FindProperty("_group").objectReferenceValue = group;
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_panelImage").objectReferenceValue = panelImg;
            so.FindProperty("_title").objectReferenceValue = titleLabel;
            so.FindProperty("_subTitle").objectReferenceValue = subTitle;
            so.FindProperty("_slotArea").objectReferenceValue = area;
            so.FindProperty("_slotAreaBackdrop").objectReferenceValue = areaBackdrop;
            so.FindProperty("_emptyFrame").objectReferenceValue = empty;
            so.FindProperty("_confirmButton").objectReferenceValue = confirm;
            so.FindProperty("_confirmImage").objectReferenceValue = btnImg;
            so.FindProperty("_confirmLabel").objectReferenceValue = confirmLabel;
            // 卡面的兜底来源。正常走 CardArtLibrary（烘焙好的整张卡面），但那本库由
            // ArtImportBuilder 生成 —— 库还没建到这一批时 GetArt 是 null，卡框会刷成一块
            // 灰方块且零报错（M25 的 PeekCardBack 正是这么炸过）。构建期直接从
            // AssetDatabase 取一张牌背顶上：它不是正确卡面，但形状与「这是一张牌」的
            // 语义是对的，比一块灰方块清楚得多（顺序问题一解决就被真卡面盖掉）。
            so.FindProperty("_cardFaceFallback").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Sprite>(PeekCardBackPath);

            SerializedProperty slotsProp = so.FindProperty("_slots");
            if (slotsProp != null)
            {
                slotsProp.arraySize = slots.Length;
                for (int i = 0; i < slots.Length; i++)
                {
                    slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// 一格「已选卡框」：卡面 + 亮色描边。
        ///
        /// <para>卡面 <c>preserveAspect = true</c>：背面是烘好的整张卡面（622×872），
        /// 与卡框 207×280 的比例不完全一致，别拉变形。</para>
        ///
        /// <para><b>2026-09-23 删掉左上角序号角标</b>（用户：「移除掉 slot area 里的
        /// indexBadge，不需要序号」）。之前那枚角标是为了让玩家看出「点选的顺序」，
        /// 与回填给引擎的序号一一对应；用户认为不需要 —— 顺序靠卡框从左到右的排布就能读出来。
        /// ⚠ 删它的连带项：<c>HandPickSlot._indexBadge / _indexText</c> 两个字段 +
        ///   两个属性（已一起删），以及只服务于它的 <c>UiTheme.HandPickIndexBadge</c>。
        ///   Prefab 里残留的旧序列化数据 Unity 会静默丢弃，不需要手工清。</para>
        /// </summary>
        private static HandPickSlot BuildHandPickSlot(Transform area, int index)
        {
            GameObject slotGo = NewUi("Slot" + index, area);
            RectTransform slot = Rt(slotGo);
            // 锚在卡框区中心：HandPickView.RefreshSlots 算的就是「以卡区中心为原点」的坐标
            Box(slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.HandPickSlotWidth, UiLayout.HandPickSlotHeight));

            // 描边（在卡面底下，比卡面大一圈 → 露出来的就是一圈亮边）
            GameObject edgeGo = NewUi("Edge", slotGo.transform);
            Stretch(Rt(edgeGo));
            Image edge = AddImage(edgeGo, UiTheme.HandPickSlotEdge);
            edge.sprite = null;

            GameObject faceGo = NewUi("Face", slotGo.transform);
            Stretch(Rt(faceGo), 3f, 3f, 3f, 3f);   // 比描边小 3 px，露出亮边
            Image face = AddImage(faceGo, Color.white);
            face.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PeekCardBackPath);
            face.color = face.sprite != null ? Color.white : UiTheme.MiniCardFace;
            face.preserveAspect = true;
            // 射线由 HandPickView 在 Show 时打开（这一拍才允许点）
            face.raycastTarget = false;

            // ⚠ 这里刻意**没有**序号角标节点（2026-09-23 用户口径）。
            //   别照着旧版本再加回来：SlotArea 的兄弟序里有 SlotSiblingBase() 那套
            //   「装饰在下、槽位在上」的算术，多一个子节点不是无害的改动。
            var comp = slotGo.AddComponent<HandPickSlot>();
            var so = new SerializedObject(comp);
            so.FindProperty("_face").objectReferenceValue = face;
            so.FindProperty("_edge").objectReferenceValue = edge;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        /// <summary>
        /// 这批素材的全部路径 —— 构建时逐个过一遍导入参数（见 <see cref="EnsurePeekImporters"/>）。
        ///
        /// <para>其中只有 <see cref="PeekPanelPath"/> 需要九宫格边界；另外三张
        /// （Banner / Frame / PanelSmall）眼下没接进版式，但同样要保证「以 Sprite 形式、
        /// 不压缩、无 mipmap」地躺着 —— 等 UI 用到它们时不必再回头收拾导入设置。</para>
        /// </summary>
        private static readonly string[] PeekSpritePaths =
        {
            PeekPanelPath,
            "Assets/Art/Ui/Peek_Banner.png",
            "Assets/Art/Ui/Peek_Frame.png",
            "Assets/Art/Ui/Peek_PanelSmall.png",
            PeekCardBackPath,
            // M30：确认键底图（素材按钮条）。它也要横向九宫格拉伸 ——
            // 丢了 border 的症状是两端六边形被抻成椭圆，且 Unity 零警告，
            // 所以一并进这张表由 EnsurePeekImporters 每次构建对一遍。
            UiLayout.HandPickConfirmSpritePath,
        };

        /// <summary>
        /// 校验 / 修正 Peek 素材的导入参数。
        ///
        /// <para>与 <c>UiKitBuilder.EnsureCardBoxImporters</c> 同一套理由：这批图是 Python
        /// 直接写盘的，<c>spriteBorder</c> / <c>spritePixelsPerUnit</c> 只活在 <c>.meta</c> 里，
        /// 图一旦被重导（换素材、换机器、被别的脚本重设导入设置）就会悄悄丢 ——
        /// 丢了的症状是<b>圆角被抻成椭圆、描边粗细不对，而且 Unity 零警告</b>。
        /// 每次构建对一遍成本几乎为零。</para>
        ///
        /// <para>顺带解决「干净检出上第一次跑 M8」的问题：图是外面写进来的，
        /// Unity 还没建过它的 <c>.meta</c>，不先补一次导入就取不到 Sprite，
        /// 面板会退化成一块纯白矩形。</para>
        /// </summary>
        private static void EnsurePeekImporters()
        {
            for (int i = 0; i < PeekSpritePaths.Length; i++)
            {
                // 需要九宫格边界的只有两张面板图：Peek_Panel（M25，纵向拉伸）
                // 与 Peek_Frame（M27，横向拉伸）；其余三张留 0（Sprite 的 border 默认就是 0）。
                Vector4 border = Vector4.zero;

                if (PeekSpritePaths[i] == PeekPanelPath)
                {
                    border = new Vector4(UiLayout.PeekPanelBorderLeft, UiLayout.PeekPanelBorderBottom,
                        UiLayout.PeekPanelBorderRight, UiLayout.PeekPanelBorderTop);
                }
                else if (PeekSpritePaths[i] == HandPickPanelPath)
                {
                    // ⚠ M27 起 Peek_Frame 有第二个消费者（手牌选择弹窗），它要横向拉伸。
                    //   这张图原来在 M25 里只是「备用素材、border 留 0」——
                    //   现在必须补上左右边界，否则拉宽时两侧的翼与收边会跟着被抻开。
                    border = new Vector4(UiLayout.HandPickPanelBorderLeft, UiLayout.HandPickPanelBorderBottom,
                        UiLayout.HandPickPanelBorderRight, UiLayout.HandPickPanelBorderTop);
                }
                else if (PeekSpritePaths[i] == UiLayout.HandPickConfirmSpritePath)
                {
                    // M30：确认键底图 —— 横向拉伸（两端六边形造型靠左右 border 保住），
                    // 纵向也吃满边界（按钮只有一种高度，纵拉没意义还会把上下辉光抻开）。
                    border = new Vector4(UiLayout.HandPickConfirmBorderX, UiLayout.HandPickConfirmBorderY,
                        UiLayout.HandPickConfirmBorderX, UiLayout.HandPickConfirmBorderY);
                }

                ApplyPeekImporter(PeekSpritePaths[i], border);
            }
        }

        private static void ApplyPeekImporter(string path, Vector4 border)
        {
            // 图是 Python 直接写盘的，Unity 还没建过它的 .meta —— 先补一次导入再取 Inspector。
            // 少了这一步，干净检出上第一次跑 M8 会取不到 Sprite（Image 退化成纯白方块），
            // 而图其实就在那儿。
            if (AssetImporter.GetAtPath(path) == null && File.Exists(path))
            {
                AssetDatabase.ImportAsset(path);
            }

            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning("[BattleUi] 找不到 Peek 素材：" + path
                                 + "\n    先跑：python Tools/art-audit/slice_peek_kit.py");
                return;
            }

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

            // 100 px/unit：九宫格边界的像素数在 UI 里就当像素用（不缩放），
            // 与工程里其余 UI 图保持一致（否则同一张图在两处的圆角粗细会不一样）。
            if (!Mathf.Approximately(ti.spritePixelsPerUnit, 100f))
            {
                ti.spritePixelsPerUnit = 100f;
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

        private static CooldownView BuildCooldownExtras(Transform topArea, CardView miniPrefab,
            Transform playerCooling, Transform enemyCooling)
        {
            var view = topArea.GetComponent<CooldownView>();
            if (view == null)
            {
                view = topArea.gameObject.AddComponent<CooldownView>();
            }

            var so = new SerializedObject(view);
            so.FindProperty("_cardPrefab").objectReferenceValue = miniPrefab;
            so.FindProperty("_playerRoot").objectReferenceValue = Rt(playerCooling.gameObject);
            so.FindProperty("_enemyRoot").objectReferenceValue = Rt(enemyCooling.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        private static TargetPicker BuildPicker(Transform topArea, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject panel = NewUi("PickerPanel", topArea);
            Box(Rt(panel), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.PickerTop), new Vector2(UiLayout.PickerWidth, 300f));

            GameObject bodyGo = NewUi("Body", panel.transform);

            // Body 不能 stretch：ContentSizeFitter 与拉伸锚点互斥（改 sizeDelta 时会被 anchor 覆盖，
            // Console 也会报 "parent has a type of layout group" 之类的冲突）。锚到面板顶部即可。
            var bodyRt = Rt(bodyGo);
            bodyRt.anchorMin = new Vector2(0.5f, 1f);
            bodyRt.anchorMax = new Vector2(0.5f, 1f);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.anchoredPosition = Vector2.zero;
            bodyRt.sizeDelta = new Vector2(UiLayout.PickerWidth, 300f);

            AddImage(bodyGo, UiTheme.OverlayPanel);
            MakeFrame(bodyGo.transform, 2f, UiTheme.OverlayEdge);

            // Body 用竖直布局 + 自适应高度：选项少的时候面板自己收起来
            var layout = bodyGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 12);
            layout.spacing = UiLayout.PickerRowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            // childControlHeight 必须为 true，否则 LayoutElement.preferredHeight 完全不生效 ——
            // 布局组会退回用子节点自己的 sizeDelta，而 NewUi 建出来的节点默认是 100×100。
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = bodyGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject titleGo = NewUi("Title", bodyGo.transform);
            var titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.preferredHeight = UiLayout.PickerTitleHeight;
            TMP_Text titleLabel = AddText(titleGo, title, UiLayout.FontSizePickerTitle,
                UiTheme.TextSecondary, TextAlignmentOptions.Center);

            GameObject rows = NewUi("Rows", bodyGo.transform);
            var rowsLayout = rows.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = UiLayout.PickerRowSpacing;
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;

            var rowsLe = rows.AddComponent<LayoutElement>();
            rowsLe.preferredHeight = UiLayout.PickerMaxRows * UiLayout.PickerRowHeight
                                     + (UiLayout.PickerMaxRows - 1) * UiLayout.PickerRowSpacing;

            var buttons = new Button[UiLayout.PickerMaxRows];
            var labels = new TMP_Text[UiLayout.PickerMaxRows];
            var rowGos = new GameObject[UiLayout.PickerMaxRows];

            for (int i = 0; i < UiLayout.PickerMaxRows; i++)
            {
                GameObject row = NewUi("Row" + i, rows.transform);
                var rowLe = row.AddComponent<LayoutElement>();
                rowLe.preferredHeight = UiLayout.PickerRowHeight;

                // ColorTint 是「直接把 targetGraphic 的颜色设成状态色」，不是相乘 ——
                // 所以底图必须是白色，把常态色交给 normalColor 给。
                var img = row.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = true;

                var btn = row.AddComponent<Button>();
                btn.transition = Selectable.Transition.ColorTint;
                btn.targetGraphic = img;
                var colors = btn.colors;
                colors.normalColor = UiTheme.PickerRow;
                colors.highlightedColor = UiTheme.PickerRowHot;
                colors.pressedColor = UiTheme.OverlayEdge;
                colors.selectedColor = UiTheme.PickerRow;
                colors.disabledColor = UiTheme.PickerRow;
                colors.fadeDuration = 0.08f;
                btn.colors = colors;

                GameObject labelGo = NewUi("Label", row.transform);
                Stretch(Rt(labelGo), 10f, 0f, 10f, 0f);
                TMP_Text label = AddText(labelGo, body, UiLayout.FontSizePickerRow,
                    UiTheme.TextPrimary, TextAlignmentOptions.Center);

                rowGos[i] = row;
                buttons[i] = btn;
                labels[i] = label;
            }

            var view = panel.AddComponent<TargetPicker>();
            var so = new SerializedObject(view);
            so.FindProperty("_panel").objectReferenceValue = bodyGo;
            so.FindProperty("_rowRoot").objectReferenceValue = Rt(rows);
            so.FindProperty("_title").objectReferenceValue = titleLabel;
            SetArray(so.FindProperty("_rows"), rowGos);
            SetArray(so.FindProperty("_rowButtons"), buttons);
            SetArray(so.FindProperty("_rowLabels"), labels);
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        /// <summary>
        /// 看牌浮层（M12，M24 #3 换成组成式卡面）：<strong>长按一张牌时弹出的放大卡面</strong>。
        ///
        /// <para><b>M24 #3 的改动</b>：内容从「<c>Art/Cards</c> 的成品卡面整图」换成
        /// <b>与手牌同款的组成式卡面</b>（<see cref="UiKitBuilder.AddCardSurfaceTo"/>）。
        /// 用户口径是「手牌 / 冷却区 / 放大查看三处样式统一，统一按手牌来」——
        /// 成品图上的数值是烘焙死的，光环加值与基础冷却这些口子在图上根本改不了，
        /// 所以直接让三处<b>物理上共用同一棵节点树</b>，只差一个缩放。</para>
        ///
        /// <para><b>尺寸</b>：仍然是 <see cref="UiLayout.DetailFaceWidth"/> ×
        /// <see cref="UiLayout.DetailFaceHeight"/>（396×550.23）—— 竖着把舞台中央占满，
        /// 横向刚好是中央净空的 420，不会盖住两侧的冷却区。这个尺寸比手牌（196 宽）
        /// 大一倍，字也就跟着大一倍，长按看清楚的目的达到了。</para>
        ///
        /// <para>高度是算出来的常量（<see cref="UiLayout.DetailHeight"/>），所以
        /// <b>不需要布局组、也不需要 ContentSizeFitter</b> —— 顺手绕开了
        /// 「childControlHeight / 标脏 / anchor 不能 stretch」那一串坑。</para>
        /// </summary>
        private static CardDetailView BuildDetail(Transform topArea, TMP_FontAsset body, TMP_FontAsset title)
        {
            // 容器只负责定位，不渲染（底图 Image 由 ConfigureFaceOnly 关掉）
            GameObject panel = NewUi("DetailPanel", topArea);
            Box(Rt(panel), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiLayout.DetailBottom),
                new Vector2(UiLayout.DetailWidth, UiLayout.DetailHeight));

            // 放大的组成式卡面：与手牌同一段构建代码，只把宽高换掉（缩放只加在 CardRoot 上）
            GameObject faceGo = NewUi("Face", panel.transform);
            Box(Rt(faceGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(UiLayout.DetailFaceWidth, UiLayout.DetailFaceHeight));

            CardView face = UiKitBuilder.AddCardSurfaceTo(faceGo, UiLayout.DetailFaceWidth,
                UiLayout.DetailFaceHeight, body, title);

            var view = panel.AddComponent<CardDetailView>();
            var so = new SerializedObject(view);
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_card").objectReferenceValue = face;
            so.ApplyModifiedPropertiesWithoutUndo();

            view.ConfigureFaceOnly();
            return view;
        }

        /// <summary>
        /// 发牌台（M9）。放在<strong>选项浮层那一档</strong>（顶部起算 86）—— 替换决策期间选项浮层整体收起，
        /// 两者不会同屏，所以可以共用这块中央净空。版式预算见 <see cref="UiLayout.DealHeight"/> 的注释。
        ///
        /// <para>结构照抄 <see cref="BuildDetail"/> 那套「底图 / 边框 ignoreLayout + 竖直布局 + 自适应高度」，
        /// 因为踩过的坑是同一个：底图与 4 根描边条如果不忽略布局，会被布局组当成两个真子节点挤掉内容。</para>
        /// </summary>
        private static DealView BuildDeal(Transform topArea, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject panel = NewUi("DealPanel", topArea);
            Box(Rt(panel), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiLayout.DealTop), new Vector2(UiLayout.DealWidth, UiLayout.DealHeight));

            GameObject bodyGo = NewUi("Body", panel.transform);

            // ContentSizeFitter 与拉伸锚点互斥：必须用固定锚点 + pivot（顶部对齐，向下生长）。
            var bodyRt = Rt(bodyGo);
            bodyRt.anchorMin = new Vector2(0.5f, 1f);
            bodyRt.anchorMax = new Vector2(0.5f, 1f);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.anchoredPosition = Vector2.zero;
            bodyRt.sizeDelta = new Vector2(UiLayout.DealWidth, UiLayout.DealHeight);

            GameObject backdrop = NewUi("Backdrop", bodyGo.transform);
            AddImage(backdrop, UiTheme.OverlayPanel);
            Stretch(Rt(backdrop));
            backdrop.AddComponent<LayoutElement>().ignoreLayout = true;

            GameObject frame = MakeFrame(bodyGo.transform, 2f, UiTheme.OverlayEdge);
            frame.AddComponent<LayoutElement>().ignoreLayout = true;

            var layout = bodyGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)UiLayout.DealPadding, (int)UiLayout.DealPadding,
                (int)UiLayout.DealPadding, (int)UiLayout.DealPadding);
            layout.spacing = UiLayout.DealRowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            // childControlHeight 必须 true，否则下面所有 LayoutElement.preferredHeight 都是死代码
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = bodyGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject counterGo = NewUi("Counter", bodyGo.transform);
            counterGo.AddComponent<LayoutElement>().preferredHeight = UiLayout.DealCounterHeight;
            TMP_Text counter = AddText(counterGo, body, UiLayout.FontSizeDealCounter,
                UiTheme.TextSecondary, TextAlignmentOptions.Center);
            counter.text = "已选 0 / 3";

            GameObject hintGo = NewUi("Hint", bodyGo.transform);
            hintGo.AddComponent<LayoutElement>().preferredHeight = UiLayout.DealHintHeight;
            TMP_Text hint = AddText(hintGo, body, UiLayout.FontSizeDealHint,
                UiTheme.TextSecondary, TextAlignmentOptions.Center);
            hint.text = "点手牌标记要替换的牌";

            // 「确认替换」——主按钮，常态是青绿；一张没标记时不可点（disabledColor 给压暗态）。
            Button confirm;
            TMP_Text confirmLabel;
            MakeDealButton(bodyGo.transform, "ConfirmButton", UiLayout.DealConfirmHeight, title,
                "确认替换", UiTheme.DealConfirm, UiTheme.DealConfirmHot, UiTheme.DealConfirmOff,
                out confirm, out confirmLabel);

            // 「不替换 / 保留这张」——文案运行时由引擎的 Done 选项覆盖（开局与补牌后不一样）。
            Button skip;
            TMP_Text skipLabel;
            MakeDealButton(bodyGo.transform, "SkipButton", UiLayout.DealSkipHeight, title,
                "不替换", UiTheme.PickerRow, UiTheme.PickerRowHot, UiTheme.PickerRow,
                out skip, out skipLabel);

            var view = panel.AddComponent<DealView>();
            var so = new SerializedObject(view);
            so.FindProperty("_panel").objectReferenceValue = bodyGo;
            so.FindProperty("_counter").objectReferenceValue = counter;
            so.FindProperty("_hint").objectReferenceValue = hint;
            so.FindProperty("_confirmButton").objectReferenceValue = confirm;
            so.FindProperty("_confirmLabel").objectReferenceValue = confirmLabel;
            so.FindProperty("_skipButton").objectReferenceValue = skip;
            so.FindProperty("_skipLabel").objectReferenceValue = skipLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        /// <summary>
        /// 发牌台上的按钮。注意 <c>ColorTint</c> 是<strong>替换</strong>颜色而不是乘算 ——
        /// 底图必须建成白色，常态色交给 <c>colors.normalColor</c>，否则构建时设的底色
        /// 会在下一次状态切换被抹掉。
        /// </summary>
        private static void MakeDealButton(Transform parent, string name, float height, TMP_FontAsset font,
            string label, Color normal, Color hot, Color off, out Button button, out TMP_Text labelText)
        {
            GameObject go = NewUi(name, parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = true;

            button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hot;
            colors.pressedColor = hot;
            colors.selectedColor = normal;
            colors.disabledColor = off;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            GameObject labelGo = NewUi("Label", go.transform);
            Stretch(Rt(labelGo));
            labelText = AddText(labelGo, font, UiLayout.FontSizeDealButton,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);
            labelText.text = label;
            // Done 的文案来自引擎，将来可能更长（现在最长是「不替换，开始对局」8 个字），
            // 留个换行兜底，别让它溢出按钮。
            labelText.enableWordWrapping = true;
        }

        private static void BuildResult(Transform canvas, TMP_FontAsset body, TMP_FontAsset title,
            out GameObject resultRoot, out TMP_Text resultTitle, out TMP_Text resultSub, out Button againButton)
        {
            resultRoot = NewUi("ResultRoot", canvas);
            Stretch(Rt(resultRoot));

            GameObject veil = NewUi("Veil", resultRoot.transform);
            Image veilImg = AddImage(veil, UiTheme.OverlayVeil);
            veilImg.raycastTarget = true;
            Stretch(Rt(veil));

            GameObject panel = NewUi("Panel", resultRoot.transform);
            Box(Rt(panel), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(UiLayout.ResultWidth, UiLayout.ResultHeight));
            AddImage(panel, UiTheme.OverlayPanel);
            MakeFrame(panel.transform, 3f, UiTheme.OverlayEdge);

            GameObject titleGo = NewUi("Title", panel.transform);
            Box(Rt(titleGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f),
                new Vector2(UiLayout.ResultWidth - 48f, 84f));
            resultTitle = AddText(titleGo, title, UiLayout.FontSizeResultTitle,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);

            GameObject subGo = NewUi("Sub", panel.transform);
            Box(Rt(subGo), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f),
                new Vector2(UiLayout.ResultWidth - 64f, 110f));
            resultSub = AddText(subGo, body, UiLayout.FontSizeResultBody,
                UiTheme.TextSecondary, TextAlignmentOptions.Center);
            resultSub.enableWordWrapping = true;

            GameObject btnGo = NewUi("AgainButton", panel.transform);
            Box(Rt(btnGo), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                new Vector2(UiLayout.ResultButtonWidth, UiLayout.ResultButtonHeight));
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = UiTheme.PickerRow;
            btnImg.raycastTarget = true;
            againButton = btnGo.AddComponent<Button>();
            againButton.targetGraphic = btnImg;

            GameObject btnLabelGo = NewUi("Label", btnGo.transform);
            Stretch(Rt(btnLabelGo));
            AddText(btnLabelGo, title, 28f, UiTheme.TextPrimary, TextAlignmentOptions.Center).text = "再来一局";
        }

        private static void BuildLog(Transform canvas, TMP_FontAsset body,
            out GameObject logPanel, out TMP_Text logText)
        {
            logPanel = NewUi("LogPanel", canvas);
            Box(Rt(logPanel), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-UiLayout.LogMargin, UiLayout.HandAreaHeight + 20f),
                new Vector2(UiLayout.LogWidth, UiLayout.LogHeight));
            AddImage(logPanel, UiTheme.OverlayPanel);
            MakeFrame(logPanel.transform, 2f, UiTheme.OverlayEdge);

            GameObject textGo = NewUi("Text", logPanel.transform);
            Stretch(Rt(textGo), 14f, 12f, 14f, 12f);
            logText = AddText(textGo, body, UiLayout.FontSizeLog,
                UiTheme.TextSecondary, TextAlignmentOptions.TopLeft);
            logText.enableWordWrapping = true;
        }

        // ══════════════════════════════════════════════════════
        //  幂等清理
        // ══════════════════════════════════════════════════════

        private static void Cleanup(Transform canvas)
        {
            for (int i = 0; i < OwnedNodes.Length; i++)
            {
                Transform found = FindDeep(canvas, OwnedNodes[i]);
                if (found != null && found != canvas)
                {
                    Object.DestroyImmediate(found.gameObject);
                }
            }
        }

        private static void HideBody(Transform root, string panelName)
        {
            Transform panel = FindDeep(root, panelName);
            Transform body = panel == null ? null : FindDeep(panel, "Body");
            if (body != null)
            {
                body.gameObject.SetActive(false);
            }
        }

        private static void SetActiveIfFound(Transform root, string name, bool active)
        {
            Transform t = FindDeep(root, name);
            if (t != null)
            {
                t.gameObject.SetActive(active);
            }
        }

        // ══════════════════════════════════════════════════════
        //  基础构件（与 UiKitBuilder 的同名私有方法有意重复：
        //  M7 已验收、不动它；这已是第二处，若出现第三处就该提取公共类了）
        // ══════════════════════════════════════════════════════

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

        private static TMP_FontAsset LoadFont(string path)
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }
    }
}
