using System.Collections.Generic;
using System.Text;
using MagicBrawl.App;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// M11 美术层的构建器。菜单：`魔法乱斗/M11 · 构建 BattleArtLayer`（幂等可重跑）。
    ///
    /// <para>在 <c>BattleCanvas</c> 下插入<b>唯一一个</b>新节点 <c>ArtLayer</c>，
    /// 承接版式（依据 `Assets/Art/_Reference/Reference.png`）：
    /// 全屏背景 · 顶部状态栏 · 左右各 4 格冷却槽 · 主角 / 怪物动画 · 左下能量球。</para>
    ///
    /// <para><b>为什么单独一个 ArtLayer 而不是改 M7/M8</b>：M7 一旦重跑会推倒整棵 Canvas，
    /// M8 的接线也会跟着重来。把新美术收敛进一个自有节点，
    /// 老的两条构建链一行都不用改，重建代价也只剩「本菜单 + M7 → M8」。</para>
    ///
    /// <para><b>旧节点处置</b>：<see cref="RetiredNodes"/> 里那几个（中央双方信息条、
    /// 左右两个旧冷却网格）职责已被新层接管，构建时一律 <c>SetActive(false)</c> ——
    /// 不删，是因为 `CooldownView` / `StageView` 仍有字段指向它们；
    /// 留着停用比留悬空引用安全。</para>
    ///
    /// <para><b>构建顺序</b>：
    /// <c>配置新美术导入</c>（建映射表）→ <c>M7</c> → <c>M8</c> → <b>本菜单</b>。
    /// 注意 M7 会推倒 Canvas，所以只要动过 M7/M8，本菜单必须重跑。</para>
    /// </summary>
    public static class BattleArtLayerBuilder
    {
        private const string CanvasName = "BattleCanvas";
        private const string PrefabPath = "Assets/Prefabs/Ui/" + CanvasName + ".prefab";
        private const string LayerName = "ArtLayer";

        private const string BodyFontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";
        private const string TitleFontPath = "Assets/Art/Fonts/BlackLike/站酷仓耳渔阳体-W03 SDF.asset";

        /// <summary>新层接管的旧节点：构建时收起。</summary>
        private static readonly string[] RetiredNodes =
        {
            "PlayerCoolingPanel",   // 旧：左上迷你卡网格
            "EnemyCoolingPanel",    // 旧：右上迷你卡网格
            "PlayerBar",            // 旧：中央「你」信息条
            "EnemyBar",             // 旧：中央「AI」信息条
        };

        // ── 菜单入口（带确认弹窗）────────────────────────────────────

        [MenuItem("魔法乱斗/M11 · 构建 BattleArtLayer", false, 30)]
        private static void MenuEntry()
        {
            if (!EditorUtility.DisplayDialog(
                    "构建 M11 美术层",
                    "会在 BattleCanvas 下重建 ArtLayer（背景 / 顶栏 / 左右冷却槽 / 主角怪物 / 能量球），\n" +
                    "收起中央旧信息条与两个旧冷却网格，并把冷却区改成「按剩余冷却分 4 行」。\n\n" +
                    "幂等可重跑。前提：已跑过「配置新美术导入」与 M7 / M8。",
                    "构建", "取消"))
            {
                return;
            }

            Debug.Log(RunAll());
        }

        // ── 无弹窗核心（MCP 走反射调这个）──────────────────────────────

        public static string RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[ArtLayer] ==== 开始 ====");

            GameObject canvasGo = GameObject.Find(CanvasName);
            if (canvasGo == null)
            {
                log.AppendLine("✘ 场景里找不到 " + CanvasName + " —— 先跑 M7 / M8 的构建菜单");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            BattleArtLibrary art = BattleArtLibrary.Instance;
            if (art == null)
            {
                log.AppendLine("✘ 缺 Assets/Resources/BattleArtLibrary.asset —— 先跑 `魔法乱斗/整理 · 配置新美术导入`");
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            TMP_FontAsset body = LoadFont(BodyFontPath);
            TMP_FontAsset title = LoadFont(TitleFontPath);
            if (body == null || title == null)
            {
                log.AppendLine("✘ 缺 TMP 字体：" + BodyFontPath + " / " + TitleFontPath);
                Debug.LogError(log.ToString());
                return log.ToString();
            }

            // 1) 幂等：先拆掉旧的 ArtLayer
            Transform old = FindDeep(canvasGo.transform, LayerName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
                log.AppendLine("  拆掉旧 " + LayerName);
            }

            // 2) 建新层
            var layerGo = new GameObject(LayerName, typeof(RectTransform));
            layerGo.transform.SetParent(canvasGo.transform, false);
            RectTransform layer = Stretch(Rt(layerGo), 0f, 0f, 0f, 0f);

            // ⚠ 绘制顺序 = Canvas 里的兄弟顺序，**越靠后越上层**。
            //   Backdrop（UiTheme.Backdrop，alpha = 1 的不透明底色）必须排在 ArtLayer 之前，
            //   否则它会整块盖住背景图 / 角色 / 冷却槽 / 顶栏 —— 就是「开局全黑」。
            //   这里两步都做：① 把 Backdrop 摁到兄弟序 0（最底）；② ArtLayer 插到它后面。
            //   只改其一都不保险：M7 重跑会推倒整棵 Canvas 按「Backdrop 最先建」重建，
            //   而 Backdrop 若被谁挪到后面，单靠 SetSiblingIndex 也只能跟着错。
            NormalizeBackdropOrder(canvasGo.transform, log);
            layerGo.transform.SetSiblingIndex(SiblingIndexAfterBackdrop(canvasGo.transform));

            // 背景图本身也必须垫在 ArtLayer 最底层（绘制顺序 = 建立的先后）
            BuildBackground(layer, art);

            BuildCharacter(layer, "Char_Hero", art.Hero, UiLayout.CharHeroX, log);
            CharacterView monster = BuildCharacter(layer, "Char_Monster", art.Monster, UiLayout.CharMonsterX, log);
            TMP_Text orbNumber = BuildOrb(layer, art, body, log);
            RectTransform[] playerRows = BuildSlotColumn(layer, art, true, log);
            RectTransform[] enemyRows = BuildSlotColumn(layer, art, false, log);
            HudView hud = BuildHud(layer, art, title, body, orbNumber, log);
            // M35：两侧各一块「刚打出的牌」展示面板（玩家在左、怪物在右，与角色同轴）
            PlayedCardView playedHero = BuildPlayedCard(layer, "PlayedCard_Hero",
                UiLayout.CharHeroX, body, log);
            PlayedCardView playedFoe = BuildPlayedCard(layer, "PlayedCard_Monster",
                UiLayout.CharMonsterX, body, log);
            HudBuffView hudBuff = BuildHudBuff(layer, body, title, log);

            // M20：手牌数标签必须建在冷却列之后（绘制顺序 = 建的先后，它可能与敌方迷你卡位置相叠）
            BuildHandCount(layer, UiLayout.CharMonsterX, monster);

            // 最后把 Bg_Dungeon 钉回 ArtLayer 最底层 —— 上面任何一个构建器若往 layer 里
            // 插入节点，背景都可能被挤到上层去，这里做一次兜底收口。
            Transform bg = layer.Find("Bg_Dungeon");
            if (bg != null && bg.GetSiblingIndex() != 0)
            {
                bg.SetAsFirstSibling();
            }

            // 3) 收起被接管的旧节点
            for (int i = 0; i < RetiredNodes.Length; i++)
            {
                Transform t = FindDeep(canvasGo.transform, RetiredNodes[i]);
                if (t == null)
                {
                    log.AppendLine("  ⚠ 找不到旧节点 " + RetiredNodes[i]);
                    continue;
                }

                if (t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                }
            }

            log.AppendLine("  收起旧节点 " + string.Join(" / ", RetiredNodes));

            // 4) 接线
            WireCooldown(canvasGo, playerRows, enemyRows, log);
            WireBattleUi(canvasGo, hud, playedHero, playedFoe, hudBuff, layer, log);

            // 4.5) 层级自检（不合规就往 Console 砸 error，避免又变成一次静默黑屏）
            VerifyLayerOrder(canvasGo, log);

            // 5) 存回 Prefab
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGo, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            EditorSceneManager.SaveOpenScenes();

            log.AppendLine("  已存回 " + PrefabPath);
            log.AppendLine("[ArtLayer] ==== 完成 ====");
            string text = log.ToString();
            Debug.Log(text);
            return text;
        }

        // ── 背景 ─────────────────────────────────────────────────────

        /// <summary>
        /// 背景按 <b>cover</b> 铺：等比放大到「宽和高都不小于画布」，多出来的裁掉。
        /// 直接 Stretch 会把 1536×990（1.552）拉成 16:9（1.778），横向拉长 15%，肉眼能看出来。
        /// </summary>
        private static void BuildBackground(RectTransform layer, BattleArtLibrary art)
        {
            if (art.Background == null)
            {
                return;
            }

            GameObject go = NewUi("Bg_Dungeon", layer);
            var img = go.AddComponent<Image>();
            img.sprite = art.Background;
            img.raycastTarget = false;
            img.preserveAspect = true;

            float ratio = UiLayout.BgNativeWidth / UiLayout.BgNativeHeight;
            float need = UiLayout.ReferenceWidth / UiLayout.ReferenceHeight;
            Vector2 size = ratio > need
                ? new Vector2(UiLayout.ReferenceHeight * ratio, UiLayout.ReferenceHeight)
                : new Vector2(UiLayout.ReferenceWidth, UiLayout.ReferenceWidth / ratio);

            Box(Rt(go), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        }

        // ── 角色 ─────────────────────────────────────────────────────

        private static CharacterView BuildCharacter(RectTransform layer, string name,
            BattleArtLibrary.CharacterSet set, float groundX, StringBuilder log)
        {
            GameObject go = NewUi(name, layer);
            RectTransform root = Rt(go);

            BattleArtLibrary.Clip idle = set != null ? set.Idle : null;
            Vector2 size = idle != null && idle.IsValid
                ? idle.Size * UiLayout.CharScale
                : new Vector2(200f, 280f);
            Vector2 pivot = idle != null && idle.IsValid ? idle.Pivot : new Vector2(0.5f, 0f);

            // 位置 = 脚底（锚点）在屏幕上的点；pivot 由 SpriteAnimator 随动作切，
            // anchoredPosition 保持不变，所以换动作角色不会跳。
            Box(root, Vector2.zero, pivot, new Vector2(groundX, UiLayout.CharGroundY), size);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = false;
            if (idle != null && idle.IsValid && idle.Frames[0] != null)
            {
                img.sprite = idle.Frames[0];
            }

            var anim = go.AddComponent<SpriteAnimator>();
            anim.SetTarget(img);

            // 头顶徽标：**不挂在角色节点下** —— 角色的 pivot 会随动作变，
            // 挂进去会让徽标跟着抖几个像素。挂在层上，位置直接由地平线算。
            GameObject badge = NewUi("Badge", layer);
            Box(Rt(badge), Vector2.zero, new Vector2(0.5f, 0.5f),
                new Vector2(groundX, UiLayout.CharGroundY + UiLayout.CharBadgeGroundOffset),
                new Vector2(UiLayout.CharBadgeWidth, UiLayout.CharBadgeHeight));

            GameObject badgeBg = NewUi("Bg", Rt(badge));
            Stretch(Rt(badgeBg), 0f, 0f, 0f, 0f);
            var bgImg = badgeBg.AddComponent<Image>();
            bgImg.color = UiTheme.PromptBackdrop;
            bgImg.raycastTarget = false;

            GameObject icon = NewUi("Icon", Rt(badge));
            Box(Rt(icon), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-34f, 0f), new Vector2(26f, 26f));
            var iconImg = icon.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.enabled = false;

            GameObject textGo = NewUi("Text", Rt(badge));
            Stretch(Rt(textGo), 8f, 0f, 8f, 0f);
            TMP_Text badgeText = AddText(textGo, LoadFont(BodyFontPath),
                UiLayout.FontSizeCharBadge, UiTheme.TextPrimary, TextAlignmentOptions.Center);

            var view = go.AddComponent<CharacterView>();
            view.Configure(img, anim, Rt(badge), badgeText, iconImg);
            view.SetScale(UiLayout.CharScale);
            view.SetFps(UiLayout.CharIdleFps, UiLayout.CharAttackFps);
            view.Bind(set);

            log.AppendLine("  " + name + " 待机 " + (idle != null ? idle.FrameCount : 0)
                           + " 帧 · 画布 " + size.x + "×" + size.y);
            return view;
        }

        /// <summary>
        /// 怪物手牌数标签（M20）：深色底 + 一行「手牌 ×N」，摆在模型右下角。
        ///
        /// <para><b>故意最后一个建</b>：层内绘制顺序 = 建节点的先后，而它的位置可能与
        /// 敌方冷却区的迷你卡重叠（见 <see cref="UiLayout.CharHandCountOffsetX"/> 里的账）。
        /// 放在冷却列之后建，读数才不会被迷你卡盖住。</para>
        ///
        /// <para>位置同头顶徽标 —— 挂层上、按地平线算固定点，不挂进角色节点
        /// （角色的 pivot 每帧随动作帧变，挂进去标签会跟着抖）。</para>
        /// </summary>
        private static void BuildHandCount(RectTransform layer, float groundX, CharacterView view)
        {
            GameObject root = NewUi("HandCount_Monster", layer);
            Box(Rt(root), Vector2.zero, new Vector2(0.5f, 0.5f),
                new Vector2(groundX + UiLayout.CharHandCountOffsetX,
                            UiLayout.CharGroundY + UiLayout.CharHandCountOffsetY),
                new Vector2(UiLayout.CharHandCountWidth, UiLayout.CharHandCountHeight));

            GameObject bg = NewUi("Bg", Rt(root));
            Stretch(Rt(bg), 0f, 0f, 0f, 0f);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = UiTheme.PromptBackdrop;   // 与头顶徽标同一个底，视觉上是一家人
            // M28：这枚标签现在还是「按住查看怪物手牌」的手势面，必须吃射线。
            bgImg.raycastTarget = true;

            GameObject textGo = NewUi("Text", Rt(root));
            Stretch(Rt(textGo), 6f, 0f, 6f, 0f);
            TMP_Text label = AddText(textGo, LoadFont(BodyFontPath),
                UiLayout.FontSizeCharHandCount, UiTheme.TextPrimary, TextAlignmentOptions.Center);
            label.text = "手牌 ×0";

            // M28：「按住查看怪物手牌」的手势组件**由这里挂**，不在 BattleUiBuilder 里挂。
            // 原因是本构建器会 DestroyImmediate 整棵旧 ArtLayer 推倒重建 ——
            // 挂在 HandCount_Monster 上的组件会跟着一起没（实测过一次：
            // M9 挂上、M11 重跑后消失，静默无报错）。谁的节点谁负责，
            // 组件必须与节点同生命周期，才不会被另一条构建链顺手抹掉。
            root.AddComponent<PressHoldButton>();

            view.ConfigureHandCount(root, label);
        }

        // ── 能量球 ───────────────────────────────────────────────────

        private static TMP_Text BuildOrb(RectTransform layer, BattleArtLibrary art,
            TMP_FontAsset body, StringBuilder log)
        {
            GameObject go = NewUi("Orb_Energy", layer);
            Box(Rt(go), Vector2.zero, Vector2.zero,
                new Vector2(UiLayout.OrbLeft, UiLayout.OrbBottom),
                new Vector2(UiLayout.OrbSize, UiLayout.OrbSize));

            var img = go.AddComponent<Image>();
            img.sprite = art.EnergyOrb;
            img.preserveAspect = true;
            img.raycastTarget = false;

            GameObject numGo = NewUi("Number", Rt(go));
            // 宝石中心略偏上（原图 1/1 的位置），居中排数字
            Box(Rt(numGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 8f), new Vector2(UiLayout.OrbSize - 24f, 64f));
            TMP_Text num = AddText(numGo, body, UiLayout.FontSizeOrbNumber,
                UiTheme.TextPrimary, TextAlignmentOptions.Center);

            num.text = "0/1";

            log.AppendLine("  能量球 " + (art.EnergyOrb != null ? "OK" : "缺图"));
            return num;
        }

        // ── 冷却槽（一侧 4 行）──────────────────────────────────────

        private static RectTransform[] BuildSlotColumn(RectTransform layer, BattleArtLibrary art,
            bool playerSide, StringBuilder log)
        {
            string colName = playerSide ? "SlotL_Col" : "SlotR_Col";
            string viewName = playerSide ? "CoolingScroll_Player" : "CoolingScroll_Enemy";
            Sprite[] sprites = playerSide ? art.SlotPlayerRow : art.SlotEnemyRow;
            int rows = Mathf.Max(4, sprites == null ? 0 : sprites.Length);
            var result = new RectTransform[rows];

            float side = playerSide ? 0f : 1f;
            float width = UiLayout.SlotColumnBudgetWidth + UiLayout.SlotMiniCardGap
                + UiLayout.SlotMiniCardWidth + (UiLayout.SlotMiniCardMax - 1) * UiLayout.SlotMiniCardStep;
            const float top = 115f;

            // ── 滚动层（M11 预建）──────────────────────────────────────
            //  一列最多要放 5 张迷你卡，4 行装不下得能滑。
            //
            //  ⚠ 这一层原先由 `CooldownView.ConfigureColumn` 在**运行时 new** 出来 ——
            //    后果是：它在 Hierarchy 里看不见、调不了，而且每次 M11 重建 ArtLayer
            //    都会把它整个丢掉（它挂在 ArtLayer 下），只剩「运行一次才有」的假象。
            //    改到这里预建；运行时的 ConfigureColumn 只设参数，不再造节点。
            GameObject viewGo = NewUi(viewName, layer);
            RectTransform viewport = Rt(viewGo);
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(side, 1f);
            viewport.anchoredPosition = new Vector2(playerSide ? UiLayout.SlotLeft : -UiLayout.SlotRight, -top);
            viewport.sizeDelta = new Vector2(width,
                UiLayout.ReferenceHeight - UiLayout.HandAreaHeight - top);

            var viewImg = viewGo.AddComponent<Image>();
            viewImg.color = new Color(0f, 0f, 0f, 0f);   // 只当滚动框 / 遮罩载体，本身不画东西
            viewImg.raycastTarget = true;                // 要能接滚轮
            viewGo.AddComponent<RectMask2D>();
            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            scroll.inertia = true;

            GameObject colGo = NewUi(colName, viewGo.transform);
            RectTransform col = Rt(colGo);
            col.anchorMin = col.anchorMax = col.pivot = new Vector2(side, 1f);
            col.anchoredPosition = Vector2.zero;
            col.sizeDelta = new Vector2(width,
                UiLayout.SlotMiniCardHeight + (rows - 1) * UiLayout.SlotRowPitch);

            int built = 0;
            for (int i = 0; i < rows; i++)
            {
                // 行下标 0 = 冷却区4 … 3 = 冷却区1
                Sprite s = sprites != null && i < sprites.Length ? sprites[i] : null;
                float w = s != null ? s.rect.width : UiLayout.SlotPlayerWidth;
                float h = s != null ? s.rect.height : UiLayout.SlotPlayerHeight;

                // 右列「冷却区1」（行下标 3）用**校准过的常量**，不按成品图尺寸算。
                //
                // ⚠ 这里原本是一条「w > SlotEnemyWidth + 1 → 撑到 224」的分支，
                //   那是给「带内嵌卡框」的老图留的 —— 假卡面在 M11 就擦掉了，
                //   重切出来的图只有 185 宽，于是每次重建都把图横向拉 21%、
                //   还让这一行第 4 张迷你卡越过视口遮罩被裁掉。
                //   用户在 Prefab 里手工改成 183×86，现在把这个值搬进常量（2026-09-20）。
                if (!playerSide && i == rows - 1)
                {
                    w = UiLayout.SlotEnemyCD1Width;
                }

                GameObject rowGo = NewUi((playerSide ? "SlotL_CD" : "SlotR_CD") + (4 - i), col);
                RectTransform row = Rt(rowGo);
                // 行在内容列里垂直居中（右列比左列矮 11 px）——
                // 与 CooldownView.ConfigureColumn 用同一套算式，运行时不会跳一下
                float y = -(i * UiLayout.SlotRowPitch
                            + (UiLayout.SlotMiniCardHeight - h) * 0.5f);
                Box(row, new Vector2(side, 1f), new Vector2(side, 1f),
                    new Vector2(0f, y), new Vector2(w, h));

                var img = rowGo.AddComponent<Image>();
                img.sprite = s;
                img.raycastTarget = false;
                if (s == null)
                {
                    img.color = new Color(0.1f, 0.13f, 0.18f, 0.7f);
                }

                // 迷你卡挂载点：CooldownView 会找这个子节点，摆不下就退回行本身
                GameObject cards = NewUi("Cards", row);
                Stretch(Rt(cards));
                result[i] = row;
                built++;
            }

            // ── 滚动条（预建）──────────────────────────────────────────
            GameObject barGo = NewUi("ScrollBar", viewGo.transform);
            RectTransform bar = Rt(barGo);
            bar.anchorMin = new Vector2(side, 0f);
            bar.anchorMax = new Vector2(side, 1f);
            bar.pivot = new Vector2(side, 0.5f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(5f, -8f);
            var barImg = barGo.AddComponent<Image>();
            barImg.color = new Color(0.1f, 0.17f, 0.22f, 0.35f);
            var scrollbar = barGo.AddComponent<Scrollbar>();

            GameObject handleGo = NewUi("Handle", barGo.transform);
            RectTransform handle = Rt(handleGo);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.65f, 0.83f, 0.9f, 0.65f);

            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImg;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scroll.viewport = viewport;
            scroll.content = col;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalNormalizedPosition = 1f;

            log.AppendLine("  " + viewName + " + " + colName + " " + built + " 行"
                           + (sprites != null && sprites.Length > 0 ? "" : "（缺槽图，用纯色占位）"));
            return result;
        }

        // ── 顶部状态栏 ───────────────────────────────────────────────

        private static HudView BuildHud(RectTransform layer, BattleArtLibrary art,
            TMP_FontAsset title, TMP_FontAsset body, TMP_Text orbNumber, StringBuilder log)
        {
            GameObject hudGo = NewUi("Hud", layer);
            RectTransform hudRoot = Rt(hudGo);
            Box(hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudLeft, -UiLayout.HudTop),
                new Vector2(UiLayout.HudBarWidth, UiLayout.HudBarHeight));

            GameObject barGo = NewUi("Bar", hudRoot);
            Stretch(Rt(barGo));
            var barImg = barGo.AddComponent<Image>();
            barImg.sprite = art.HudBar;
            barImg.raycastTarget = false;
            if (art.HudBar == null)
            {
                barImg.color = UiTheme.Panel;
            }

            // 名字 / 副标题 —— 位置就是切图时擦掉的那两行文字原位
            GameObject nameGo = NewUi("Name", hudRoot);
            Box(Rt(nameGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudNameX, -UiLayout.HudNameY),
                new Vector2(UiLayout.HudNameWidth, UiLayout.HudNameHeight));
            TMP_Text name = AddText(nameGo, title, UiLayout.FontSizeHudName,
                UiTheme.TextPrimary, TextAlignmentOptions.Left);

            GameObject subGo = NewUi("Subtitle", hudRoot);
            Box(Rt(subGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudSubX, -UiLayout.HudSubY),
                new Vector2(UiLayout.HudSubWidth, UiLayout.HudSubHeight));
            TMP_Text sub = AddText(subGo, body, UiLayout.FontSizeHudSub,
                UiTheme.TextSecondary, TextAlignmentOptions.Left);

            GameObject hpGo = NewUi("HpNumber", hudRoot);
            Box(Rt(hpGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudHpX, -UiLayout.HudHpY),
                new Vector2(UiLayout.HudHpWidth, UiLayout.HudHpHeight));
            TMP_Text hp = AddText(hpGo, body, UiLayout.FontSizeHudHp,
                UiTheme.TextPrimary, TextAlignmentOptions.Left);

            GameObject resGo = NewUi("ResNumber", hudRoot);
            Box(Rt(resGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudResX, -UiLayout.HudResY),
                new Vector2(UiLayout.HudResWidth, UiLayout.HudResHeight));
            TMP_Text res = AddText(resGo, body, UiLayout.FontSizeHudRes,
                UiTheme.AuraReady, TextAlignmentOptions.Left);

            // 齿轮：条上那颗齿轮是烘在图里的，这里只放一个不可见的点击区，正好盖住它
            GameObject gearGo = NewUi("GearButton", hudRoot);
            Box(Rt(gearGo), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiLayout.HudGearX, -UiLayout.HudGearY),
                new Vector2(UiLayout.HudGearSize, UiLayout.HudGearSize));
            var gearHit = gearGo.AddComponent<Image>();
            gearHit.color = new Color(0f, 0f, 0f, 0f);
            gearHit.raycastTarget = true;
            var gear = gearGo.AddComponent<Button>();
            gear.transition = Selectable.Transition.None;

            // 状态图标行（M15：由「条下方一排假图标」改成真正的光环图标区 HudBuff，见 BuildHudBuff）
            var hud = hudGo.AddComponent<HudView>();
            hud.Configure(name, sub, hp, res, orbNumber, gear);

            log.AppendLine("  顶栏 " + (art.HudBar != null ? "OK" : "缺图"));
            return hud;
        }

        // ── 光环图标区（M15）────────────────────────────────────────
        //
        //  用户口径（2026-09-18）：双方持有的光环显示在 ArtLayer 的 HudBuff 里，
        //  **玩家在左、敌人在右**；相同的 buff 不堆叠（每枚指示物一个图标）；
        //  长按看具体内容；把图标**拖到自己的手牌区**即视为使用。
        //
        //  这一节只负责「建出能用的节点 + 一个失活的图标模板」，运行时的一切
        //  （摆几枚、摆在哪、能不能拖）都在 Ui/HudBuffView.cs。
        //
        //  ⚠ 根节点**铺满画布**：图标拖拽时会被挂到它下面自由移动，
        //    挂在一个只有几百像素宽的组里是拖不出组外的。
        private static HudBuffView BuildHudBuff(RectTransform layer, TMP_FontAsset body,
            TMP_FontAsset title, StringBuilder log)
        {
            GameObject rootGo = NewUi("HudBuff", layer);
            RectTransform root = Stretch(Rt(rootGo));

            RectTransform player = BuildBuffGroup(root, "PlayerGroup", true);
            RectTransform enemy = BuildBuffGroup(root, "EnemyGroup", false);

            AuraIconView icon = BuildAuraIcon(root, body);
            AuraTooltipView tip = BuildAuraTip(root, body, title);
            RectTransform castSeat = BuildAuraCastSeat(root);

            var view = rootGo.AddComponent<HudBuffView>();
            view.Configure(player, enemy, icon, tip, castSeat);

            log.AppendLine("  HudBuff OK（玩家组在左 / 敌人组在右 · 图标模板 + 长按浮层"
                           + " · AI 用光环的落点 AuraCastSeat）");
            return view;
        }

        /// <summary>
        /// M36：「AI 用掉一枚光环」时那一枚飞过去的落点 —— <b>怪物模型的右侧</b>。
        ///
        /// <para><b>用户口径（2026-09-23）</b>：AI 使用光环时也应当有一个光环移动的过程来帮玩家
        /// 识别，移动到怪物的模型的右侧。</para>
        ///
        /// <para><b>为什么是一个真节点、而不是在代码里算坐标</b>：与光环槽位同一套理由
        /// （见 <see cref="BuildBuffGroup"/>）——本构建器第一步就是 <c>DestroyImmediate</c>
        /// 整棵 ArtLayer，任何手摆的节点都活不过一次重跑。生成出来的才在；
        /// 想微调位置改 <c>UiLayout.AuraCastX / AuraCastY</c> 即可。</para>
        ///
        /// <para>它是一枚**看不见的空节点**（没有 Image）：作用只是给飞行终点一个位置，
        /// 在 Hierarchy 里能看到、能拖，但什么都不画。</para>
        ///
        /// <para>版式账（画布 1920×1080，从左下角算）：怪物脚底中心 (1246.5, 402)、
        /// 待机帧 398×335 → 落点 = (1246.5 + 200, 560) = (1446.5, 560)，
        /// 落在模型右侧、且抬到「手牌 ×N」标签（408…448）上方 112 px，不会压住它。</para>
        /// </summary>
        private static RectTransform BuildAuraCastSeat(RectTransform root)
        {
            GameObject go = NewUi("AuraCastSeat", root);
            RectTransform rt = Rt(go);

            // 与角色 / 头顶出牌面板同一套锚点口径：锚左下角，坐标就是画布绝对坐标。
            Box(rt, Vector2.zero, new Vector2(0.5f, 0.5f),
                new Vector2(UiLayout.CharMonsterX + UiLayout.AuraCastX, UiLayout.AuraCastY),
                new Vector2(UiLayout.BuffIconSize, UiLayout.BuffIconSize));

            return rt;
        }

        /// <summary>
        /// 一侧的图标组 + 它下面的一排槽位 <c>Slot0…Slot7</c>（M21 改）。
        ///
        /// <para><b>默认位（用户 2026-09-20）</b>：光环图标排在**角色头顶血量徽标旁边** ——
        /// 主角侧从徽标右缘往右、怪物侧从徽标左缘往左，一行横排。
        /// 组的锚点就是「徽标外侧那条线」（<see cref="UiLayout.HudBuffPlayerX"/> /
        /// <see cref="UiLayout.HudBuffEnemyX"/>），槽位由组内偏移推出。</para>
        ///
        /// <para><b>⚠ 槽位为什么由构建器生成</b>：M18 时这 8 个槽位是<b>手摆在
        /// BattleCanvas.prefab 里</b>的（当时的说法是「坐标摆在 Prefab 里，在 Hierarchy 里直接拖」）。
        /// 但本构建器的第一步是 <c>DestroyImmediate</c> 整棵 ArtLayer —— 2026-09-20 重跑 M11
        /// 就把它们全冲掉了（同时丢的还有 <c>PreparedAura/Slot0…7</c>，那一份是 M8 的
        /// <c>Cleanup</c> 删的），于是图标和准备标记**都没有落点**。现在改成按
        /// <see cref="UiLayout.BuffIconSpacing"/> 生成：重跑多少次都在。
        /// 代价 = 手拖槽位活不过一次重跑（可以改常量，或者把序号往后多加几个）。</para>
        ///
        /// <para>组本身只是锚点容器（尺寸给个够大的方框），槽位用 anchoredPosition 摆，
        /// 所以组的 sizeDelta 不影响排布，别拿它当版式依据。</para>
        /// </summary>
        private static RectTransform BuildBuffGroup(RectTransform root, string name, bool playerSide)
        {
            GameObject go = NewUi(name, root);
            RectTransform rt = Rt(go);

            Vector2 anchor = playerSide ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            float x = playerSide ? UiLayout.HudBuffPlayerX : -UiLayout.HudBuffEnemyX;

            Box(rt, anchor, anchor, new Vector2(x, -UiLayout.HudBuffTop),
                new Vector2(UiLayout.BuffIconSpacing * UiLayout.BuffIconSlots, UiLayout.BuffIconSize));

            // 槽位：锚在自己的上角（玩家=左上 / 怪物=右上），轴心居中，
            // 于是 anchoredPosition 直接就是「第 i 格中心」相对组上角的偏移。
            // 首格中心 = 半个图标（让 Slot0 的左/右缘正好压在组的那条线上，与徽标 +36 对齐）。
            float half = UiLayout.BuffIconSize * 0.5f;
            float dir = playerSide ? 1f : -1f;

            for (int i = 0; i < UiLayout.BuffIconSlots; i++)
            {
                GameObject slotGo = NewUi("Slot" + i, go.transform);
                Box(Rt(slotGo), anchor, new Vector2(0.5f, 0.5f),
                    new Vector2(dir * (half + UiLayout.BuffIconSpacing * i), -half),
                    new Vector2(UiLayout.BuffIconSize, UiLayout.BuffIconSize));
            }

            return rt;
        }

        /// <summary>
        /// 图标模板（失活）。结构：外框 → 底版 → 符号（剑 / 盾 / 感叹号）→ 数值底衬 + 数值 → 压暗遮罩。
        ///
        /// <para>外框做成「底版后面垫一个稍大的同形色块」而不是四根细条：图标只有 72 px，
        /// 细条拼的框在缩放 / 非整数位置时会露出缝。</para>
        /// </summary>
        private static AuraIconView BuildAuraIcon(RectTransform root, TMP_FontAsset body)
        {
            float size = UiLayout.BuffIconSize;
            float edge = UiLayout.BuffIconEdgeWidth;
            Sprite plate = LoadPolySprite("RoundedRectangle");

            GameObject go = NewUi("IconTemplate", root);
            Box(Rt(go), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size, size));

            // ① 外框（描边色；「本拍能用」时 HudBuffView 会把它点亮）
            GameObject frameGo = NewUi("Frame", go.transform);
            Image frame = AddImage(frameGo, UiTheme.AuraIconEdge);
            Stretch(Rt(frameGo));
            frame.sprite = plate;
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = true;   // 图标是本层唯一的射线目标

            // ② 底版（按光环类型上色）
            GameObject plateGo = NewUi("Plate", go.transform);
            Image plateImg = AddImage(plateGo, UiTheme.MiniCardFace);
            Box(Rt(plateGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size - edge * 2f, size - edge * 2f));
            plateImg.sprite = plate;
            plateImg.type = Image.Type.Sliced;

            // ③ 符号（攻防二选一时并排两个，位置/尺寸由 AuraIconView.Bind 每帧按需给）
            GameObject gaGo = NewUi("GlyphA", go.transform);
            Image ga = AddImage(gaGo, UiTheme.AuraGlyph);
            ga.preserveAspect = true;
            Box(Rt(gaGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, UiLayout.BuffIconGlyphOffsetY),
                new Vector2(UiLayout.BuffIconGlyph, UiLayout.BuffIconGlyph));

            GameObject gbGo = NewUi("GlyphB", go.transform);
            Image gb = AddImage(gbGo, UiTheme.AuraGlyph);
            gb.preserveAspect = true;
            gb.enabled = false;
            Box(Rt(gbGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(UiLayout.BuffIconGlyphDualOffset, UiLayout.BuffIconGlyphOffsetY),
                new Vector2(UiLayout.BuffIconGlyphDual, UiLayout.BuffIconGlyphDual));

            // ④ 数值（+2 / ≥6 / ≤3）
            GameObject bgGo = NewUi("ValueBg", go.transform);
            Image valueBg = AddImage(bgGo, UiTheme.AuraValueBackdrop);
            Box(Rt(bgGo), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiLayout.BuffIconValueBottom),
                new Vector2(UiLayout.BuffIconValueWidth, UiLayout.BuffIconValueHeight));

            GameObject valueGo = NewUi("Value", go.transform);
            Box(Rt(valueGo), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiLayout.BuffIconValueBottom),
                new Vector2(UiLayout.BuffIconValueWidth, UiLayout.BuffIconValueHeight));
            TMP_Text value = AddText(valueGo, body, UiLayout.FontSizeBuffValue,
                UiTheme.AuraValueText, TextAlignmentOptions.Center);

            // ⑤ 「本拍用不上」的压暗遮罩
            GameObject dimGo = NewUi("Dim", go.transform);
            Image dim = AddImage(dimGo, UiTheme.AuraIconDim);
            Stretch(Rt(dimGo));
            dim.sprite = plate;
            dim.type = Image.Type.Sliced;
            dim.enabled = false;

            var view = go.AddComponent<AuraIconView>();
            var so = new SerializedObject(view);
            so.FindProperty("_root").objectReferenceValue = Rt(go);
            so.FindProperty("_frame").objectReferenceValue = frame;
            so.FindProperty("_plate").objectReferenceValue = plateImg;
            so.FindProperty("_glyphA").objectReferenceValue = ga;
            so.FindProperty("_glyphB").objectReferenceValue = gb;
            so.FindProperty("_valueBg").objectReferenceValue = valueBg;
            so.FindProperty("_value").objectReferenceValue = value;
            so.FindProperty("_dim").objectReferenceValue = dim;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);   // 模板常驻失活；运行时 Instantiate 出来再激活
            return view;
        }

        /// <summary>长按光环弹出的说明浮层（三行：来源 · 效果 · 怎么用）。</summary>
        private static AuraTooltipView BuildAuraTip(RectTransform root, TMP_FontAsset body, TMP_FontAsset title)
        {
            GameObject tipGo = NewUi("Tooltip", root);
            Stretch(Rt(tipGo));

            GameObject panelGo = NewUi("Panel", tipGo.transform);
            RectTransform panel = Rt(panelGo);
            // 轴心在**顶边中点**：调用方给的是「图标下沿那一点」，浮层从这里往下长
            Box(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(UiLayout.BuffTipWidth, UiLayout.BuffTipHeight));

            GameObject backGo = NewUi("Backdrop", panelGo.transform);
            Stretch(Rt(backGo));
            AddImage(backGo, UiTheme.AuraTipBackdrop);

            GameObject edgeGo = NewUi("Edge", panelGo.transform);
            Stretch(Rt(edgeGo));
            MakeEdge(edgeGo, UiTheme.AuraTipEdge);

            float pad = UiLayout.BuffTipPadding;

            GameObject titleGo = NewUi("Name", panelGo.transform);
            Box(Rt(titleGo), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(pad, -pad),
                new Vector2(UiLayout.BuffTipWidth - pad * 2f, 26f));
            TMP_Text name = AddText(titleGo, title, UiLayout.FontSizeBuffTipName,
                UiTheme.AuraReady, TextAlignmentOptions.Left);

            GameObject bodyGo = NewUi("Body", panelGo.transform);
            Box(Rt(bodyGo), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(pad, -(pad + 32f)),
                new Vector2(UiLayout.BuffTipWidth - pad * 2f, 44f));
            TMP_Text text = AddText(bodyGo, body, UiLayout.FontSizeBuffTipBody,
                UiTheme.TextPrimary, TextAlignmentOptions.TopLeft);
            text.enableWordWrapping = true;

            GameObject hintGo = NewUi("Hint", panelGo.transform);
            Box(Rt(hintGo), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(pad, pad),
                new Vector2(UiLayout.BuffTipWidth - pad * 2f, 24f));
            TMP_Text hint = AddText(hintGo, body, UiLayout.FontSizeBuffTipHint,
                UiTheme.TextSecondary, TextAlignmentOptions.Left);

            var view = tipGo.AddComponent<AuraTooltipView>();
            view.Configure(panel, name, text, hint);
            return view;
        }

        /// <summary>取 ThirdParty/PolySprite 里的白色几何形状（可被 <c>Image.color</c> 上色）。</summary>
        private static Sprite LoadPolySprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/ThirdParty/PolySprite/" + name + ".png");
        }

        // ── 对手头顶出牌展示（M13）──────────────────────────────

        /// <summary>
        /// 一名角色头顶那张「刚打出的牌」：深色衬底 + 描边 + 两个卡位（双发防御要交两张）。
        ///
        /// <para><b>为什么挂 ArtLayer 的固定位</b>：与头顶血条徽标同理 —— 挂在角色节点下
        /// 会跟着 pivot 抖；两名角色又都不动，所以一个固定锚点就够（位置账见
        /// <see cref="UiLayout.PlayedCardAboveBadge"/> 的注释）。</para>
        ///
        /// <para><b>M35 起建两块</b>（2026-09-23 用户口径「玩家打出的牌也应当显示在头顶」）：
        /// 玩家侧 <c>PlayedCard_Hero</c> 在 <see cref="UiLayout.CharHeroX"/>、怪物侧
        /// <c>PlayedCard_Monster</c> 在 <see cref="UiLayout.CharMonsterX"/> —— 除了水平位置，
        /// 两块的尺寸 / 卡位 / 时长完全一致，所以只用同一个方法换两个参数。</para>
        ///
        /// <para>建在最后 → 层内绘制顺序压过角色，保证卡面不会被怪物的披风盖住。</para>
        /// </summary>
        private static PlayedCardView BuildPlayedCard(RectTransform layer, string nodeName, float groundX,
            TMP_FontAsset body, StringBuilder log)
        {
            float w = UiLayout.PlayedCardWidth;
            float h = UiLayout.PlayedCardHeight;
            float pad = UiLayout.PlayedCardPadding;

            // 锚点 = 角色脚底正上方、血条徽标再往上抬
            float y = UiLayout.CharGroundY + UiLayout.CharBadgeGroundOffset + UiLayout.PlayedCardAboveBadge;
            float totalW = UiLayout.PlayedCardGap + w * 2f + pad * 2f;

            GameObject panelGo = NewUi(nodeName, layer);
            RectTransform panel = Rt(panelGo);
            // 锚点必须是「左下」：CharHeroX / CharMonsterX / CharGroundY 都是**画布绝对坐标**
            //（与血条徽标同口径）。锚到 (0.5,0) 会在画布中心之上再加一次 1290，
            // 牌直接跑到屏幕外（实测 2187 > 1920）。
            Box(panel, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(groundX, y), new Vector2(totalW, h + pad * 2f));

            GameObject backdropGo = NewUi("Backdrop", panelGo.transform);
            Stretch(Rt(backdropGo), 0f, 0f, 0f, 0f);
            AddImage(backdropGo, UiTheme.PlayedCardBackdrop);

            GameObject edgeGo = NewUi("Edge", panelGo.transform);
            Stretch(Rt(edgeGo));
            MakeEdge(edgeGo, UiTheme.PlayedCardEdge);

            // 两个卡位，左右对称 —— ⚠ M20 起这对坐标只是**初值**：单张 / 双张是两套版式，
            // 运行时由 PlayedCardView.Layout(count) 现场摆（单张居中、衬底收缩），
            // 手改这里不会生效（改版式请动 UiLayout 的 M13/M20 段）。
            var faces = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1f : 1f) * (w + UiLayout.PlayedCardGap) * 0.5f;
                GameObject faceGo = NewUi("Card" + (i + 1), panelGo.transform);
                Image face = AddImage(faceGo, UiTheme.MiniCardFace);
                Box(Rt(faceGo), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, 0f), new Vector2(w, h));
                face.preserveAspect = true;
                faces[i] = face;
            }

            var group = panelGo.AddComponent<CanvasGroup>();
            var view = panelGo.AddComponent<PlayedCardView>();
            view.Configure(panelGo, panel, faces, group,
                UiLayout.PlayedCardDefenseHold, UiLayout.PlayedCardFade);

            log.AppendLine("  " + nodeName + " 出牌展示 OK（锚点 x=" + groundX + " y=" + y + "，双卡位；"
                           + "单张居中 / 双张并排，常驻到该牌进冷却区为止）");
            return view;
        }

        /// <summary>四根细条拼的描边（与 <see cref="UiKitBuilder"/> 的 MakeFrame 同思路，本文件自留一份）。</summary>
        private static void MakeEdge(GameObject parent, Color color)
        {
            float t = 2f;
            RectTransform rt = Rt(parent);
            Vector2 size = rt.sizeDelta;

            MakeBar(parent, "Top", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -t * 0.5f), new Vector2(size.x, t), color);
            MakeBar(parent, "Bottom", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, t * 0.5f), new Vector2(size.x, t), color);
            MakeBar(parent, "Left", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(t * 0.5f, 0f), new Vector2(t, size.y), color);
            MakeBar(parent, "Right", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-t * 0.5f, 0f), new Vector2(t, size.y), color);
        }

        private static void MakeBar(GameObject parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color color)
        {
            GameObject go = NewUi(name, parent.transform);
            Image img = AddImage(go, color);
            Box(Rt(go), anchor, pivot, position, size);
        }

        // ── 接线 ─────────────────────────────────────────────────────

        private static void WireCooldown(GameObject canvasGo, RectTransform[] playerRows,
            RectTransform[] enemyRows, StringBuilder log)
        {
            Transform topArea = FindDeep(canvasGo.transform, "TopArea");
            CooldownView view = topArea == null ? null : topArea.GetComponent<CooldownView>();
            if (view == null)
            {
                log.AppendLine("  ⚠ 找不到 CooldownView，冷却区没接上新行");
                return;
            }

            var so = new SerializedObject(view);
            SetArray(so.FindProperty("_playerRows"), playerRows);
            SetArray(so.FindProperty("_enemyRows"), enemyRows);
            // 老字段也指到新列上 —— 免得还挂着已被停用的旧网格
            so.FindProperty("_playerRoot").objectReferenceValue = playerRows.Length > 0
                ? playerRows[0].parent as RectTransform : null;
            so.FindProperty("_enemyRoot").objectReferenceValue = enemyRows.Length > 0
                ? enemyRows[0].parent as RectTransform : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            view.ConfigureLayout();
            log.AppendLine("  CooldownView → 分 4 行挂载点已接");
        }

        private static void WireBattleUi(GameObject canvasGo, HudView hud, PlayedCardView playedHero,
            PlayedCardView playedFoe, HudBuffView hudBuff, RectTransform layer, StringBuilder log)
        {
            BattleUi ui = canvasGo.GetComponent<BattleUi>();
            if (ui == null)
            {
                log.AppendLine("  ⚠ BattleCanvas 上没有 BattleUi，HUD / 角色没接上");
                return;
            }

            var so = new SerializedObject(ui);
            so.FindProperty("_hud").objectReferenceValue = hud;
            so.FindProperty("_playedHero").objectReferenceValue = playedHero;
            so.FindProperty("_playedFoe").objectReferenceValue = playedFoe;
            so.FindProperty("_hudBuff").objectReferenceValue = hudBuff;

            Transform hero = FindDeep(layer, "Char_Hero");
            Transform monster = FindDeep(layer, "Char_Monster");
            so.FindProperty("_hero").objectReferenceValue =
                hero == null ? null : hero.GetComponent<CharacterView>();
            so.FindProperty("_monster").objectReferenceValue =
                monster == null ? null : monster.GetComponent<CharacterView>();
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("  BattleUi → _hud / _hero / _monster / _hudBuff 已接");
        }

        // ══════════════════════════════════════════════════════
        //  基础构件（与 UiKitBuilder 同风格，故意各留一份，避免跨构建器耦合）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把 Canvas 的 <c>Backdrop</c> 摁到兄弟序 0（最底层）。
        ///
        /// <para><c>Backdrop</c> 是铺满画布的<b>不透明</b>底色（<c>UiTheme.Backdrop</c> = 0F131B，alpha = 1）。
        /// Canvas 里同层绘制顺序 = 兄弟顺序，越靠后越上层；它一旦不在最底，
        /// 就会盖住 ArtLayer 的背景图、角色与冷却槽 —— 表现就是「开局全黑 + 角色不显示」。</para>
        ///
        /// <para>正常情况下 M7 建 Canvas 时 Backdrop 本来就是第一个子节点，这里只是防空：
        /// 任何一次推倒重建 / 手工拖动都不该把这条不变量弄丢。</para>
        /// </summary>
        private static void NormalizeBackdropOrder(Transform canvas, StringBuilder log)
        {
            Transform backdrop = FindDeep(canvas, "Backdrop");
            if (backdrop == null)
            {
                log.AppendLine("  ⚠ 找不到 Backdrop（M7 没跑过？）");
                return;
            }

            if (backdrop.GetSiblingIndex() != 0)
            {
                int was = backdrop.GetSiblingIndex();
                backdrop.SetAsFirstSibling();
                log.AppendLine("  修正 Backdrop 层级：" + was + " → 0（必须在最底，否则糊住整个 ArtLayer）");
            }
        }

        /// <summary>
        /// ArtLayer 该插在 Canvas 的第几个兄弟位 = <b>紧贴 Backdrop 之后</b>（= 画在 Backdrop 上面）。
        ///
        /// <para>绘制顺序 = 兄弟顺序，越靠后越上层。<c>Backdrop</c> 是不透明底色，必须垫在
        /// ArtLayer <b>下面</b>；因此 ArtLayer 取 <c>backdrop.GetSiblingIndex() + 1</c>。</para>
        ///
        /// <para>调用前应先跑 <see cref="NormalizeBackdropOrder"/> 把 Backdrop 归到 0，
        /// 这样结果恒为 1，ArtLayer 稳定落在 Canvas 所有界面元素的最底层之上、底板之上。</para>
        /// </summary>
        private static int SiblingIndexAfterBackdrop(Transform canvas)
        {
            Transform backdrop = FindDeep(canvas, "Backdrop");
            // 没有 Backdrop 就排第一；否则紧跟其后（= 在底板之上、所有界面元素之下）
            return backdrop == null ? 0 : backdrop.GetSiblingIndex() + 1;
        }

        /// <summary>
        /// 收口校验：<c>Backdrop</c> 必须在 ArtLayer <b>之前</b>（兄弟序更小 = 画得更早 = 在下面），
        /// <c>Bg_Dungeon</c> 必须在 ArtLayer 的最底层。
        ///
        /// <para>这两条一破，表现就是「开局全黑 + 角色不显示」—— 而且**零报错**，
        /// 纯靠肉眼看截图才发现。所以每次构建完都体检一遍，坏了就往 Console 砸一条 error。</para>
        /// </summary>
        private static void VerifyLayerOrder(GameObject canvasGo, StringBuilder log)
        {
            Transform canvas = canvasGo.transform;
            Transform backdrop = FindDeep(canvas, "Backdrop");
            Transform layer = FindDeep(canvas, LayerName);
            if (backdrop == null || layer == null)
            {
                return;
            }

            bool ok = backdrop.GetSiblingIndex() < layer.GetSiblingIndex();

            Transform bg = layer.Find("Bg_Dungeon");
            if (bg != null && bg.GetSiblingIndex() != 0)
            {
                ok = false;
                log.AppendLine("  ✘ Bg_Dungeon 不在 ArtLayer 最底层（index=" + bg.GetSiblingIndex() + "），会盖住角色");
            }

            if (!ok)
            {
                string msg = "[ArtLayer] ✘ 层级自检失败：Backdrop(index=" + backdrop.GetSiblingIndex()
                             + ") 必须排在 " + LayerName + "(index=" + layer.GetSiblingIndex()
                             + ") 之前，否则不透明底板会糊住整个美术层（开局黑屏）。";
                log.AppendLine(msg);
                Debug.LogError(msg);
            }
            else
            {
                log.AppendLine("  ✔ 层级自检通过：Backdrop(" + backdrop.GetSiblingIndex()
                               + ") < " + LayerName + "(" + layer.GetSiblingIndex() + ")");
            }
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

        private static TMP_FontAsset LoadFont(string path)
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        private static void SetArray(SerializedProperty prop, RectTransform[] values)
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
