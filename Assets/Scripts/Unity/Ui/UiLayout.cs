using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 界面版式的**唯一数值来源**。所有尺寸都按 1920×1080 参考分辨率折算
    /// （`CanvasScaler.ScaleWithScreenSize`，match = 0.5），改版式只改这一处。
    ///
    /// 版式依据：`Docs/engineering/03-工程规划.md` §7 —— 左右对峙、己方冷却区在最左、敌方最右、
    /// 手牌区底部全宽、对方手牌只显示数量。
    /// </summary>
    public static class UiLayout
    {
        // M39 设置 / 卡池编辑（1920 x 1080 参考画布）。
        public const float SettingsWidth = 660f;
        public const float SettingsHeight = 570f;
        public const float CardPoolPanelWidth = 1700f;
        public const float CardPoolPanelHeight = 960f;
        public const float CardPoolCardWidth = 224f;
        public const float CardPoolCardHeight = 314f;
        public const int CardPoolColumns = 6;
        public const float CardPoolCellWidth = 248f;
        public const float CardPoolCellHeight = 362f;

        // ── 参考分辨率 ────────────────────────────────────────
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        // ── 手牌区（底部全宽，M12 起排成弧形扇面）──────────────
        /// <summary>手牌区高度。</summary>
        public const float HandAreaHeight = 344f;

        /// <summary>
        /// 手牌卡宽。卡面是 760×1056，比例 0.7197。
        ///
        /// <para><b>M12 由 208 缩到 196</b>，M13 保留：扇形最外侧那张要转 ±11°，
        /// 卡片轴心在<b>底边中点</b>，所以转起来之后「最低角比底边中点低 (宽/2)·sinθ = 18.7」、
        /// 「最高角比底边中点高 高·cosθ + (宽/2)·sinθ = 266 + 18.7」。
        /// 用 196×272 时整排抬高后中间牌顶边在 378 ≤ 地平线 402 ✔；
        /// 若改回 208×289，最低角要压低 19.8、最高角要抬高 283+19.8，顶边直接顶到 400+。</para>
        /// </summary>
        public const float HandCardWidth = 196f;

        /// <summary>手牌卡高（= 宽 / 0.7206，保持卡面比例；卡面本身是 0.7197）。</summary>
        public const float HandCardHeight = 272f;

        /// <summary>相邻手牌的水平额外间隙（横向步进 = 卡宽 + 本值）。</summary>
        public const float HandCardSpacing = 10f;

        /// <summary>手牌底边距（扇形最低点的位置）。</summary>
        public const float HandCardBottom = 22f;

        // ── 手牌扇形（M13：真圆弧）────────────────────────────
        //
        //  口径：扇形必须是**一条真圆弧**，而不是「横排 + 一个独立的下沉量」。
        //
        //  几何定义 —— 所有牌的**底边中点**落在同一个圆上，圆心在画面正下方；
        //  每张牌的旋转角 = 它在圆上的圆心角（牌的「上」方向沿半径朝外）。
        //  于是相邻两张的夹角恒等于角度步进、每张牌都跟弧线相切，
        //  位置与角度来自同一个圆的两个投影，物理上不可能出现「弧很平、牌转很凶」。
        //
        //  圆半径不直接给，由「横跨半宽 S」与「半张角 A」反解：R = S / sin(A)。
        //  想改弧的深浅 → 改总张开角度；想改横向排布 → 改卡宽 / 间隙 / 侧边安全宽。
        //
        //  旧写法是 y = baseY − 固定下沉量 × k²（抛物线近似），下沉量与横跨宽度是两个
        //  各自独立的常数，随便改一个就会调出不成形的弧（截图里看着就是一条歪斜的直线），
        //  M13 已把它删掉。
        //
        //  ⚠ 唯一的硬约束是**竖向预算**（卡片 pivot 在底边中点，转 θ 之后最低的那个角
        //  比底边中点还低 (卡宽/2)·sinθ，整排必须抬到「最低角 = 手牌底边距」，
        //  抬得越高，中间那张的顶边就越往上顶 —— 手牌区没有 Mask，会压到舞台地面）。
        //  实测（8 张满手 · 总角 22° · 卡 196×272）：
        //    A = 11°，R = 681.8 / sin11° ≈ 3573，最外侧下沉 = R(1−cos11°) ≈ 65.5
        //    整排抬高 = 65.5 + (196/2)·sin11° ≈ 84.2
        //    中间牌顶边 = 22(底边距) + 84.2 + 272 ≈ 378 ≤ 地平线 402 ✔（余 24 px）
        //  改 HandFanTotalAngle / HandCardWidth / HandFanSideMargin 之后必须重算这两行。

        /// <summary>
        /// 单张牌的角度步进<strong>上限</strong>（度）。实际步进 = min(本值, 总角度/(张数−1))，
        /// 所以手牌少的时候不会甩得太开。
        /// </summary>
        public const float HandFanMaxStep = 6f;

        /// <summary>
        /// 扇面的总张开角度（度）—— 最外侧两张各偏 ±总角/2。
        /// 定成「总角度」而不是「每张固定角度」，是为了让 8 张牌也不会甩成一把扇子。
        /// 22° 的来历见上面的竖向预算。
        /// </summary>
        public const float HandFanTotalAngle = 22f;

        /// <summary>
        /// 扇形两侧各留多少安全宽度（px）。8 张满手时横向步进会被这个值压小，
        /// 免得最外侧的牌压到左下角的能量球。
        /// </summary>
        public const float HandFanSideMargin = 180f;

        // ── 冷却区迷你卡（M12：挪到槽外侧，按剩余冷却分行）──────
        //
        //  M12 改动：迷你卡不再压在槽位<b>里面</b>烘出来的那个空卡框上，
        //  而是挪到槽的外侧 —— 玩家侧往右、敌方侧往左（都朝向画面中心）。
        //  好处有两个：①「冷却区N」标签完全不被盖住；②槽里烘的空卡框留作「空位」指示，
        //  和参考图一致（参考图左侧那列就是这么显示的）。
        //  代价是卡面尺寸不再受槽内卡框约束，可以放大到「靠画面就能认牌」。
        public const float MiniCardWidth = 104f;
        public const float MiniCardHeight = 140f;
        public const float MiniCardSpacing = 8f;

        /// <summary>每列最多几张。</summary>
        public const int MiniCardsPerColumn = 4;

        /// <summary>最多折成几列。</summary>
        public const int MiniMaxColumns = 2;

        public const float CoolingPanelWidth = 224f;
        public const float CoolingPanelMargin = 28f;
        public const float CoolingPanelTop = 20f;
        public const float CoolingPanelBottom = 12f;

        /// <summary>迷你卡上光环亮点的直径（M15 随卡体 ×1.5）。</summary>
        public const float AuraPipSize = 21f;

        // ── 舞台（中部）───────────────────────────────────────
        /// <summary>上部区域高度（= 总高 − 手牌区）。</summary>
        public const float TopAreaHeight = ReferenceHeight - HandAreaHeight;

        public const float PlayerBarWidth = 300f;

        public const float PlayerBarHeight = 220f;

        /// <summary>双方信息条相对屏幕中心左右各偏多少。</summary>
        public const float PlayerBarOffsetX = 360f;

        public const float HpDotSize = 32f;
        public const float HpDotSpacing = 10f;

        /// <summary>生命上限（点数 = 生命上限，规则固定 4）。</summary>
        public const int HpDotCount = 4;


        // ── 字号（对齐 `Docs/engineering/06-美术与字体规范.md` §3.6）────────
        /// <summary>卡名 / 提示条：22–24，仓耳渔阳体 W03 Bold。</summary>
        public const float FontSizeCardName = 22f;

        /// <summary>冷却区数字（力量 / 剩余冷却）：26–32，Noto Sans SC Bold。</summary>
        public const float FontSizeCoolingNumber = 28f;

        /// <summary>生命值数字：32–40。</summary>
        public const float FontSizeHpNumber = 34f;

        /// <summary>手牌 ×N：26–28。</summary>
        public const float FontSizeHandCount = 26f;

        /// <summary>虚弱徽标「虚弱 ×N」：24。</summary>
        public const float FontSizeWeaken = 24f;

        /// <summary>玩家名。</summary>
        public const float FontSizePlayerName = 34f;

        /// <summary>中央对峙标记。</summary>
        public const float FontSizeStageMark = 48f;

        /// <summary>手牌角标的字号（缩到 208 px 后卡面烘焙文字已不可读，关键数值要重排）。</summary>
        public const float FontSizeHandBadge = 26f;

        /// <summary>单张手牌显示缩放比 —— 用来提醒：卡面烘焙文字在这个比例下只有 ~8–10 px，不可读。</summary>
        public const float HandCardArtScale = HandCardWidth / 760f;

        // ══════════════════════════════════════════════════════
        //  M8 BattleUi
        // ══════════════════════════════════════════════════════

        // ── 手牌交互动效（M12 扇形版）─────────────────────────
        /// <summary>
        /// 悬停上浮量。扇形摆好之后中心那张的顶边在 330，手牌区上沿是 344 ——
        /// 只有 <b>14 px</b> 余量，所以悬停只敢给 12。超过就会顶进上部区域
        /// （手牌区没有 Mask，不会被裁掉，但会盖住冷却区最下面一行）。
        /// </summary>
        public const float HandHoverRaise = 12f;

        public const float HandHoverScale = 1.03f;

        /// <summary>选中（已选为出牌目标）时的上浮量。会往上部区域探进约 6 px，可接受。</summary>
        public const float HandSelectRaise = 20f;

        /// <summary>
        /// 选中缩放。校验：272 × 1.05 + 20 = 305.6，加上扇形基准位仍在手牌区内；
        /// 中心那张的顶边会到 350（越过上沿 6 px），视觉上压住一点地面，正合参考图。
        /// </summary>
        public const float HandSelectScale = 1.05f;

        /// <summary>手牌入场动画的单张延迟（依次弹出）。</summary>
        public const float HandDealStagger = 0.05f;

        // ── 提示条（顶部中央，占中央 420 px 净空的顶格）────────
        public const float PromptWidth = 640f;
        public const float PromptHeight = 58f;
        public const float PromptTop = 16f;

        // ── 选项浮层（提示条正下方）──────────────────────────
        public const float PickerWidth = 420f;
        public const float PickerTop = 84f;
        public const float PickerTitleHeight = 40f;
        public const float PickerRowHeight = 52f;
        public const float PickerRowSpacing = 6f;

        /// <summary>最多显示几行（超过就只留前 N 个）。4 行 = 标题 40 + 4×52 + 3×6 + padding 24 = 296，底边 380。</summary>
        public const int PickerMaxRows = 4;

        // ── M12 看牌浮层（长按弹出「完整卡面」）──────────────────
        //
        //  口径变更（用户 2026-09-17 要求）：
        //    · 悬停**不再**弹任何浮层（原来那个「重排效果文字」的详情框被认定为多余）
        //    · 改成**长按**任意一张牌 → 弹出这张卡的**完整卡面**；松手 / 挪开即消失
        //    · 玩家认牌靠画面而不是文字，所以浮层显示的就是那张 760×1056 的成品卡面整图
        //
        //  版式账（宽度仍被中央净空卡在 420）：420 = 12 + 396 + 12；
        //  卡面 396 宽 → 396 / 0.7197 = 550 高。
        //  浮层总高 = 12 + 34 + 6 + 550 + 6 + 26 + 12 = 646，
        //  底边钉在手牌区上沿 10 px 处（y = 354）→ 顶边 1000 ≤ 1080 ✔
        /// <summary>看牌浮层总宽（含内边距）。</summary>
        public const float DetailWidth = 420f;

        /// <summary>浮层内边距。</summary>
        public const float DetailPadding = 12f;

        /// <summary>距手牌区上沿（TopArea 底边）的距离。</summary>
        public const float DetailBottom = 10f;

        /// <summary>标题行（卡名）高度。</summary>
        public const float DetailTitleHeight = 34f;

        /// <summary>卡面宽度（= 浮层宽 − 两侧内边距）。</summary>
        public const float DetailFaceWidth = DetailWidth - DetailPadding * 2f;

        /// <summary>卡面高度（按 760×1056 的比例算出来，不写死）。</summary>
        public const float DetailFaceHeight = DetailFaceWidth / 0.7197f;

        /// <summary>力量 / 冷却 / 光环那一行的高度。</summary>
        public const float DetailMetaHeight = 26f;

        /// <summary>浮层内相邻块的间距。</summary>
        public const float DetailGap = 6f;

        /// <summary>
        /// 浮层自适应后的总高 —— 只用于**校验版式预算**（浮层实际高度由 ContentSizeFitter 撑），
        /// 以及给构建器一个「按这个尺寸建」的确定值。
        /// </summary>
        public const float DetailHeight = DetailPadding * 2f + DetailTitleHeight + DetailGap
                                          + DetailFaceHeight + DetailGap + DetailMetaHeight;

        /// <summary>长按判定时长（秒）。短了会误触，长了不像「按住看牌」。</summary>
        public const float LongPressSeconds = 0.32f;

        /// <summary>拖拽的启动距离（px）。小于它算点击，不算拖动。</summary>
        public const float DragStartDistance = 12f;

        /// <summary>拖动中的牌相对指针的抬升量（让指针不挡住卡面）。</summary>
        public const float DragLiftAbove = 56f;

        /// <summary>拖动中的缩放（比选中再大一点，强调「拿在手上」）。</summary>
        public const float HandDragScale = 1.06f;

        // ══════════════════════════════════════════════════════
        //  M13 出牌判定区 + 指向箭头
        // ══════════════════════════════════════════════════════
        //
        //  规则口径（用户 2026-09-17）：出牌不再靠「拖到角色身上」判定 ——
        //  角色节点每帧尺寸都在变，判定框只能外扩到很松，手感是「时灵时不灵」。
        //  改成画面正中一块**不可视的判定区**：把手牌拖进这块区域，
        //  才出现一支指向目标的箭头（进攻 = 红箭头指敌人 / 防御 = 蓝箭头指自己），
        //  此时松手即出牌；没进区域就什么都不发生，牌自己滑回扇形。
        //
        //  版式账：
        //    · 横向 **M21 起与屏幕左右两端对齐**（原来是居中 1200 = 1920 − 两侧各 360，
        //      把左右冷却槽整列让开；用户 2026-09-20 要求加宽到通栏）；
        //    · 纵向 430 ~ 700（距屏幕底边）：
        //        下沿 430 高于「手牌最高点 378」→ 与手牌不重叠，必须**往上拖**才算进区；
        //        上沿 700 低于「顶栏下沿 900 附近」→ 不会跟状态图标行打架；
        //        地平线 402 落在区内偏下，视觉上就是「把牌丢到场地上」。
        //
        //  ⚠ 通栏之后两处副作用（都是有意接受的）：
        //    ① 拖牌经过左右冷却列那两条竖带时也算「进了判定区」—— 判定只看纵向 430…700。
        //       冷却列里的牌本来就不能拖（它们是迷你卡、没有出牌手势），所以只是判定框变大。
        //    ② 因此**不再有「宽度」常量**：构建器改用锚点 0…1 横向拉伸
        //       （见 BattleUiBuilder.BuildPlayZone），换任何画布宽度都贴到两端。

        /// <summary>判定区下沿距屏幕底边。</summary>
        public const float PlayZoneBottom = 430f;

        /// <summary>判定区高度。</summary>
        public const float PlayZoneHeight = 270f;

        /// <summary>箭头杆粗。</summary>
        public const float DropArrowShaft = 14f;

        /// <summary>箭头三角的宽（垂直于杆）。</summary>
        public const float DropArrowHeadWidth = 46f;

        /// <summary>箭头三角的长（沿杆）。</summary>
        public const float DropArrowHeadLength = 52f;

        /// <summary>
        /// 箭头瞄点 = 目标角色的「脚底」再往上抬这么高（px）。
        ///
        /// <para>为什么不瞄角色包围盒中心：角色的 <c>sizeDelta</c> 每一帧都随动作帧变
        /// （<see cref="SpriteAnimator"/> 重设），拿中心当瞄点会让箭头末端一跳一跳。
        /// 脚底（pivot）在整个动作切换过程里是稳定的，抬一个固定高度即得到躯干位置。</para>
        /// </summary>
        public const float DropArrowAimLift = 220f;

        /// <summary>箭头最短长度 —— 卡贴到目标身上时杆会退化成 0 长，三角会糊成一团。</summary>
        public const float DropArrowMinLength = 120f;

        // ══════════════════════════════════════════════════════
        //  M13 对手「刚打出的牌」抬头展示
        // ══════════════════════════════════════════════════════
        //
        //  用户口径：对手打出的牌除了进冷却区，还要额外在其**头顶**显示一张。
        //  理由是对方手牌不可见 —— 只靠顶部提示条那行字，玩家很难第一时间看清对面出了什么。
        //
        //  版式账（怪物侧，卡宽 116 = 760:1056 比例下高 161）：
        //    锚点 y = 地平线 402 + 徽标高度 392 + 108 = 902 → 卡体 822 ~ 982，
        //    与头顶血条徽标（773 ~ 815）留 7 px 不叠；离画布顶 1080 还有 98 px ✔
        //  列（两张并排，双发防御用得到）：总宽 = 2×116 + 10 = 242，仍在右侧净空里。
        //  ⚠ **M20 起卡位与衬底宽度是运行时按张数算的**（`PlayedCardView.Layout`）：
        //    1 张 → 居中、衬底收到 128 宽；2 张（只有防御双发会出现）→ ±63 并排、衬底 254 宽。
        //    所以 prefab 里 Card1/Card2 那对坐标只是初值，手改不生效。

        /// <summary>头顶那张牌的宽（高按 760:1056 比例算）。</summary>
        public const float PlayedCardWidth = 116f;

        /// <summary>头顶那张牌的高。</summary>
        public const float PlayedCardHeight = PlayedCardWidth / 0.7197f;

        /// <summary>同时显示两张时的水平间隙（双发要交两张牌）。</summary>
        public const float PlayedCardGap = 10f;

        /// <summary>牌心相对「徽标中心」再往上抬多少。</summary>
        public const float PlayedCardAboveBadge = 108f;

        /// <summary>配牌衬底的留白（每边）。</summary>
        public const float PlayedCardPadding = 6f;

        /// <summary>
        /// 单张时的显示倍率（1 = 与双张时同尺寸）。
        ///
        /// <para><b>M20 新增</b>：用户口径「只出一张牌就别按两张的版式显示」——
        /// 衬底会按张数收缩到刚好包住内容，单张居中；这个值只影响单张那一种版式，
        /// 想让单张更醒目就调大它（卡位与衬底一起放大）。</para>
        /// </summary>
        public const float PlayedCardSingleScale = 1f;

        /// <summary>淡出时长（秒）。只在 <c>Show(cards, hold &gt; 0)</c> 的自动淡出模式下生效。</summary>
        public const float PlayedCardFade = 0.35f;

        // ── M36 · 打出的牌「从角色模型升上来」的入场动画 ────────
        //
        //  用户口径（2026-09-23）：「加入 AI 的出牌动画（从其模型，向上移动，并逐渐出现的动画）」。
        //  只给**怪物**一侧（玩家自己打出的牌从手牌区拖上去，起点另有其物，而且玩家自己知道打了什么）。
        //
        //  怎么读：起点 = 怪物模型当前帧的中心（含光效的包围盒中心，比脚底高出一半身高），
        //  终点 = 头顶卡位（y = 902）。所以「向上移动」这段距离约 300 px，
        //  期间同时从 0 淡到 1、从小长到原尺寸 —— 三件事合起来才像「从怪物身上冒出来」。

        /// <summary>入场动画时长（秒，无缩放时间）。</summary>
        public const float PlayedCardIntroSeconds = 0.42f;

        /// <summary>入场起始缩放（从「一小撮」长到原尺寸）。</summary>
        public const float PlayedCardIntroStartScale = 0.72f;

        /// <summary>
        /// 头顶那张牌「该飞走了」之后再停留多久（秒）—— <b>M35 新增（2026-09-23）</b>。
        ///
        /// <para><b>收牌时机改成事件驱动</b>：牌一旦在快照里出现在冷却区（引擎第 ④ 步把
        /// 「本次进攻牌」与「本次防御牌」<b>一起</b>送进冷却），就立刻从头顶起飞、落到那个冷却槽。
        /// 于是<b>一方的进攻牌与另一方的防御牌在同一刻离场</b>（用户 2026-09-23 口径：
        /// 「一方的进攻卡进入冷却区时，就是另外一方的防御卡进入冷却区域的时机」）。</para>
        ///
        /// <para>这个值只用来在「觉得太快看不清」时留一点余量：<b>0 = 一到冷却区就飞</b>。
        /// ⚠ 改大它会让「两张牌同时飞」不再严格同时 —— 两张牌各自从自己被检测到的时刻起算，
        /// 而它们本来就是在同一帧被检测到的，所以只要这个值对双方一致，仍然会同时起飞；
        /// 真正会错位的是「进攻拍与防御拍之间隔了几拍」的那种情形。</para>
        /// </summary>
        public const float PlayedCardHeadLingerSeconds = 0f;

        /// <summary>
        /// ⚠ <b>已废弃（M35，2026-09-23）</b>：怪物「交牌防御」后头顶卡面原来固定停这么久再淡出，
        /// 并把这整段时长报给 <c>BattleDriver.BeatHold</c> 当节拍闸门。
        ///
        /// <para>现在两个座位（玩家 / 怪物）的头顶牌一律<b>常驻到进冷却区为止</b>
        /// （见 <see cref="PlayedCardHeadLingerSeconds"/>），不再有固定停留时长，本常量已无引用。
        /// 保留定义只为不让文档里的旧引用变成悬空 —— <b>不要再拿它去接新逻辑</b>。</para>
        /// </summary>
        public const float PlayedCardDefenseHold = 2f;

        // ══════════════════════════════════════════════════════
        //  M20 怪物手牌数（模型右下角）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-20）：给怪物加一个手牌数显示，摆在「怪物模型的右下角」。
        //  对方手牌数本来就是公开信息（工程规划 §7），但玩家得把目光从右侧冷却区挪回舞台，
        //  贴脸摆一个读数最省事。
        //
        //  版式账：怪物待机帧画布 257×216 × CharScale 1.55 = 屏幕 398×335，
        //  脚底中心 = (CharMonsterX 1246.5, CharGroundY 402)，可见身体右缘 ≈ 1392
        //  （实测：后腿那一带画到 x≈1370）。
        //  取偏移 +196 / +26 → 标签心 (1442.5, 428)，竖直 408…448、水平 1380…1504，
        //  左边缘刚好越过后腿、落在「模型右下角」外侧一点点。
        //  （v1 是 +176，左边缘 1360 会压到腿上一小块 —— 2026-09-20 截图核对后右移 20。）
        //  ⚠ 敌方冷却区最下面两行的迷你卡（剩余冷却 2 / 1）最多向左伸到 x ≈ 1226，
        //    与本标签可能重叠；本标签建在 ArtLayer 的最后 = 绘制在最上层，读得清。
        //    想更干净就把 OffsetX 再往右挪，或把敌方迷你卡收窄（SlotMiniCard* 那一组）。

        /// <summary>手牌数标签的尺寸。</summary>
        public const float CharHandCountWidth = 124f;
        public const float CharHandCountHeight = 40f;

        /// <summary>手牌数标签中心相对「怪物脚底中心」的偏移（px）。</summary>
        public const float CharHandCountOffsetX = 196f;
        public const float CharHandCountOffsetY = 26f;

        /// <summary>手牌数标签字号。</summary>
        public const float FontSizeCharHandCount = 22f;

        // ── 结算浮层（屏幕正中）──────────────────────────────

        public const float ResultWidth = 620f;
        public const float ResultHeight = 340f;
        public const float ResultButtonWidth = 240f;
        public const float ResultButtonHeight = 64f;

        // ── 战斗日志（设置里开关，默认关；M10 接管）─────────────
        public const float LogWidth = 520f;
        public const float LogHeight = 420f;
        public const float LogMargin = 40f;

        // ── M8 字号 ───────────────────────────────────────────
        /// <summary>顶部提示条。</summary>
        public const float FontSizePrompt = 28f;

        /// <summary>屏幕中央行动提示（「轮到你进攻」/「进攻结束」）—— 比顶部条大一倍，一眼看得见。</summary>
        public const float FontSizeActionBanner = 54f;

        // ── 中央行动提示（ActionBanner，2026-09-19）─────────────
        //
        //  位置核对：锚在**画面正中**（0.5, 0.5），只往上抬一点点就够 ——
        //  它是一闪而过的，不参与「中央 420 px 净空」那套常驻版式预算
        //  （浮层族占 750…1170，本横幅会横跨它们，但只出现 1 秒多且不吃射线）。
        //  竖向上沿避开顶部提示条（PromptBar 底边 74），下沿避开判定区上沿。
        public const float ActionBannerWidth = 560f;
        public const float ActionBannerHeight = 104f;

        /// <summary>横幅中心相对画面中心的纵向偏移（正数往上）—— 抬到「胸口」高度，不挡手牌也不碰顶栏。</summary>
        public const float ActionBannerY = 46f;

        // ══════════════════════════════════════════════════════
        //  M36 · 屏幕中央的「浮字」（提示为什么这张牌点不动）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-23）：对一张**本拍不可能被加速 / 减速**的牌操作时，
        //  「应当弹出一段无法被加速或减速的文字，此文字应当是在中间弹出，
        //    然后向上移动且迅速变透明消失」。
        //
        //  版式账：与 ActionBanner 同一套定位口径（锚画面正中、再往上抬 ActionBannerY），
        //  但尺寸只有它一半 —— 横幅是「轮到你」这种一眼必须看到的事，浮字只是回答一个问题。
        //    宽 520 = 最长一句「该牌无法被减速 · 剩余冷却已达上限」16 字 × 26 号 ≈ 416 + 两侧留白；
        //    高 84 装一行 26 号 + 上下留白 —— ⚠ 不能再矮：底衬用的是卡框九宫格图
        //    （border 38），高度不足 76 时四边会被挤压变形。
        //    竖向 540±42 + 46 → 544…628，仍在中央净空里，不碰顶栏与手牌。
        //
        //  ⚠ **不吃射线**（构建器把两个 Image / Text 的 raycastTarget 都关掉）：
        //    它出现的时机正是玩家在点冷却区的那几拍，挡一下就把操作打断了。

        /// <summary>浮字的尺寸。</summary>
        public const float FloatTipWidth = 520f;
        public const float FloatTipHeight = 84f;

        /// <summary>浮字中心相对画面中心的纵向偏移（正数往上）。</summary>
        public const float FloatTipY = 46f;

        public const float FontSizeFloatTip = 26f;

        /// <summary>浮字整段「弹出 → 停留 → 上升淡出」的总时长（秒，无缩放时间）。</summary>
        public const float FloatTipSeconds = 0.95f;

        /// <summary>浮字淡入时长 —— 要短，它是「点下去立刻有回话」。</summary>
        public const float FloatTipFadeInSeconds = 0.10f;

        /// <summary>停留结束后的上升距离：从弹出点向上走这么多像素再消失。</summary>
        public const float FloatTipRise = 48f;

        // ══════════════════════════════════════════════════════
        //  M41 · 「思考框」——点击怪物时弹出的元素提示（2026-09-26）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径：点击怪物模型时弹出一个像「思考框」一样的提示，
        //  告诉玩家这只怪物下一次进攻会打出哪种元素的牌（只给元素符号，
        //  不告诉是哪一张），**显示约两秒之后渐隐消失**。
        //
        //  ── 2026-09-27 用户口径（三处一起改）────────────────────
        //  ① 「显示在怪物模型的**偏右上角**」—— 原来摆在模型**头顶正中**
        //     （与脚底同轴 1246.5、抬到徽标之上），现在移到模型右上角那一带；
        //  ② 「**不需要有白色的背景**」—— 那块象牙白的圆角泡体（Backdrop / CardBox.png）
        //     整个去掉，屏幕上只剩元素符号本身；
        //  ③ 「元素符号大小缩小到当前的 40%，同时缩小背后的框」——
        //     符号 104 → 41.6、泡体 168 → 67.2（同一个 0.4 系数；弹出上浮量同比例缩到 14）。
        //     ⚠ 泡体已经**没有图**了，所以「框」只决定弹出动画的缩放原点与下面这笔版式账，
        //       屏幕上看不见 —— 想让它重新长出一块底，把 Backdrop 加回来即可
        //       （构建器 `BuildThinkBubble` 里那一段已按口径删掉）。
        //
        //  版式账（画布 1920×1080，坐标从左下角算）：
        //    · 怪物脚底中心 = (CharMonsterX 1246.5, CharGroundY 402)，
        //      待机帧 257×216 × CharScale 1.55 = 398×335 → 模型占 x 1048…1445、y 402…737；
        //    · 落点取 **模型右上角那一带**：x = 1246.5 + 200 = 1446.5
        //      （与 M36「怪物用掉光环」的落点同一列：贴模型右缘、不与本体叠），
        //      y = 402 + 368 = 770（模型顶边 737 之上一点点，读作「右上角」）；
        //    · 缩到 67.2 之后泡体占 x 1412.9…1480.1、y 736.4…803.6，于是：
        //        - 血量徽标 x 1180.5…1312.5 —— **横向不重叠**（泡在它右边，改动前是压在它上面）；
        //        - 头顶出牌面板 x 1119.5…1373.5 / y 815.4…988.6 —— 也不重叠；
        //        - 敌方冷却列的视口上沿 736 —— 泡底边 736.4，正好让开。
        //
        //  ⚠ **本节点锚在画面正中（anchor 0.5/0.5）**，所以 ThinkBubbleX / Y 要写
        //    「相对画面中心」的值（同 FloatTipY 的口径）：上面算的 1446.5 / 770 是
        //    **从画布左下角**量出来的，换算过来是 486.5 / 230。第一版直接填了底边口径的值，
        //    在 1920×1080 的画布上等于把泡顶到屏幕外，于是「点击有反应、但什么都看不见」
        //    （2026-09-26 踩过）。
        //
        //  ⚠ **不吃射线**：它出现的时机正是玩家在点怪物，挡一下就把后续点击打断了。
        //
        //  ⚠⚠ 改这几个常量**不会**自动改到已经存盘的 `BattleCanvas.prefab` ——
        //    所以 `ThinkBubbleView.ApplyLayout()` 在运行时按常量重申一次
        //    尺寸 / 位置 / 符号大小（`Show` 与 `Configure` 各一次），改常量立刻见效，
        //    不必为了挪一个泡去重跑整棵界面的构建器；构建器那几行读的也是这些常量。

        /// <summary>思考框的尺寸（正方形）。**2026-09-27：168 × 0.4 = 67.2**。</summary>
        public const float ThinkBubbleWidth = 67.2f;
        public const float ThinkBubbleHeight = 67.2f;

        /// <summary>
        /// 思考框中心相对「怪物脚底中心」的横向偏移（正数往右）。
        /// 取 200 = 模型右缘（398/2 = 199）那一列 —— 与 <see cref="AuraCastX"/> 同一条竖线。
        /// </summary>
        public const float ThinkBubbleRightOffsetX = 200f;

        /// <summary>
        /// 思考框中心距怪物脚底（<see cref="CharGroundY"/>）的抬升量。
        /// 模型顶边在脚底 +335 处（398×335），取 368 = 顶边再往上一点，落在「模型右上角」。
        /// </summary>
        public const float ThinkBubbleUpperRightRiseY = 368f;

        /// <summary>
        /// 思考框中心<b>相对画面中心</b>的位置（正数往右上）。
        ///
        /// <para><b>本节点锚在画面正中</b>（anchor 0.5/0.5），所以两个分量都得是
        /// 「相对中心」的值 —— 和 <see cref="FloatTipY"/> 同一个口径：</para>
        /// <list type="bullet">
        /// <item>x = 怪物脚底 x（<see cref="CharMonsterX"/> 1246.5）+ 右移量 200
        ///     − <see cref="ReferenceWidth"/> / 2 = 486.5；</item>
        /// <item>y = 脚底 402 + 抬升量 368 − <see cref="ReferenceHeight"/> / 2 = 230。</item>
        /// </list>
        ///
        /// <para>⚠ 第一版直接把底边口径的值填进来，等于把泡顶到屏幕外，
        /// 表现是「点击有反应、但什么都看不见」（2026-09-26 踩过）。</para>
        /// </summary>
        public const float ThinkBubbleX = CharMonsterX + ThinkBubbleRightOffsetX - ReferenceWidth / 2f;

        /// <inheritdoc cref="ThinkBubbleX"/>
        public const float ThinkBubbleY = CharGroundY + ThinkBubbleUpperRightRiseY - ReferenceHeight / 2f;

        /// <summary>框内元素符号的边长。**2026-09-27：104 × 0.4 = 41.6**。</summary>
        public const float ThinkBubbleIconSize = 41.6f;

        /// <summary>整段「弹出 → 停留 → 渐隐」的总时长。<b>用户口径：约两秒。</b></summary>
        public const float ThinkBubbleSeconds = 2.0f;

        /// <summary>弹出时长（缩放 OutBack + 淡入）—— 要短，它是「点下去立刻有回话」。</summary>
        public const float ThinkBubblePopSeconds = 0.16f;

        /// <summary>弹出时从多小开始放大。</summary>
        public const float ThinkBubblePopFrom = 0.72f;

        /// <summary>渐隐阶段的上浮距离。**2026-09-27：随泡体一起 ×0.4（36 → 14）**。</summary>
        public const float ThinkBubbleRise = 14f;

        // ── M41 · 怪物点击面（透明、吃射线；见 MonsterClickCatcher）────────
        //
        //  它是「点了怪物」的命中范围 —— 单独一块固定尺寸的矩形，不挂到角色本体上
        //  （角色 sizeDelta / pivot 每帧随动作帧变，挂上去命中范围会跟着漂）。
        //
        //  ⚠ 2026-09-27 用户口径：「当前的有效点击判定区域太小，应当**覆盖整个怪物模型**」。
        //   原来 320×340（当时故意比模型小一圈），现在按模型包围盒给足：
        //     · 模型 398×335，占 x 1048…1445、y 402…737；
        //     · 取 420×360、中心抬到 402 + 168 = 570 → 覆盖 x 1036.5…1456.5、y 390…750，
        //       四边各留约 10 px 余量（点模型最边缘那几像素也不会漏）。
        //
        //  ⚠ 撑大到这个尺寸之后它会不会抢掉别人的点击？**不会**，靠的是兄弟序：
        //   本节点由 `BattleArtLayerBuilder` 在**角色之后、冷却列与手牌数标签之前**建，
        //   同层绘制顺序 = 兄弟序 —— 后建的在上面，所以
        //     · 头顶手牌数标签（按住看怪物手牌，x 1380…1504 / y 408…448）、
        //     · 两侧冷却列的迷你卡
        //   这一带落的点按仍然归它们自己（射线从上往下问，谁在上面谁先应答）。
        //
        //  位置基准：中心 = 脚底(CharGroundY) + ThinkBubbleClickCenterY。

        /// <summary>怪物点击面的宽（覆盖模型包围盒 398 宽 + 两侧余量）。</summary>
        public const float ThinkBubbleClickWidth = 420f;

        /// <summary>怪物点击面的高（覆盖模型包围盒 335 高 + 上下余量）。</summary>
        public const float ThinkBubbleClickHeight = 360f;

        /// <summary>点击面中心相对怪物脚底（<see cref="CharGroundY"/>）的抬升量（= 335 / 2）。</summary>
        public const float ThinkBubbleClickCenterY = 168f;

        /// <summary>选项浮层标题。</summary>
        public const float FontSizePickerTitle = 24f;

        /// <summary>选项按钮文字。</summary>
        public const float FontSizePickerRow = 24f;

        /// <summary>看牌浮层卡名。</summary>
        public const float FontSizeDetailName = 26f;

        /// <summary>看牌浮层的力量/冷却/光环行。</summary>
        public const float FontSizeDetailMeta = 22f;

        // ⚠ M12 删掉了 FontSizeDetailEffect / FontSizeDetailEffectMin ——
        //   浮层不再用 TMP 重排效果文字，改成直接显示成品卡面整图
        //   （用户口径：认牌靠画面，不靠文字）。想恢复「可读的效果文字」时，
        //   正确的做法是把卡面里的插画与文字区分开取，而不是把手牌放大。

        /// <summary>结算标题（胜 / 负）。</summary>
        public const float FontSizeResultTitle = 54f;

        public const float FontSizeResultBody = 24f;

        /// <summary>战斗日志。</summary>
        public const float FontSizeLog = 18f;

        // ══════════════════════════════════════════════════════
        //  M9 DealUi（开局发牌 / 补牌后的替换）
        // ══════════════════════════════════════════════════════
        //
        //  位置核对（中央 420 px 净空的三段分配，续 `09-BattleUi实现说明.md` §5）：
        //
        //    区块            顶部起算   高度
        //    PromptBar          16       58
        //    DealPanel          86      212   ← M9 新增，占用「选项浮层」那一档
        //    PickerPanel        84      ≤296  ← 替换决策时整个收起，不会与发牌台叠
        //    DetailPanel      底边距 10   646   ← M12 由「效果文字浮层」改成「完整卡面看牌浮层」，
        //                                          底边仍在 354，但变高了：顶边到 1000（画布 1080）✔
        //
        //  发牌台底边 = 86 + 212 = 298 ≤ 354，与看牌浮层不重叠。
        //  ⚠ 但**看牌浮层与发牌台会重叠**（354 vs 298）—— 两者不会同时出现：
        //    看牌浮层只在长按某张牌时出现，而发牌替换是「点牌标记」的多选交互，不长按。
        //    真要同时看，浮层压在发牌台上，松手即消失，不影响操作。
        //  改任何一个数值前先回来核对这张表。

        /// <summary>发牌台宽 —— 与选项浮层同宽，都被中央净空（左右信息条各 ±360 占 300）卡死。</summary>
        public const float DealWidth = 420f;

        /// <summary>发牌台顶部起算 = 提示条底边（16 + 58）+ 12 间隙。</summary>
        public const float DealTop = PromptTop + PromptHeight + 12f;

        public const float DealPadding = 14f;

        /// <summary>「已选 N / M」计数行。</summary>
        public const float DealCounterHeight = 34f;

        /// <summary>操作提示行（点手牌标记要替换的牌）。</summary>
        public const float DealHintHeight = 24f;

        /// <summary>「确认替换」按钮。</summary>
        public const float DealConfirmHeight = 54f;

        /// <summary>「不替换 / 保留这张」按钮 —— 文案由引擎的 Done 选项给，长的有 8 个字。</summary>
        public const float DealSkipHeight = 48f;

        public const float DealRowSpacing = 8f;

        /// <summary>发牌台总高 = 14 + 34 + 24 + 54 + 48 + 8×3 + 14 = <b>212</b>。底边落在 298。</summary>
        public const float DealHeight = DealPadding * 2f + DealCounterHeight + DealHintHeight
                                        + DealConfirmHeight + DealSkipHeight + DealRowSpacing * 3f;

        /// <summary>已选计数（数字用高亮色，与「已选 / M」的灰字区分）。</summary>
        public const float FontSizeDealCounter = 26f;

        /// <summary>操作提示。</summary>
        public const float FontSizeDealHint = 19f;

        /// <summary>发牌台两个按钮的文字。</summary>
        public const float FontSizeDealButton = 26f;

        // ══════════════════════════════════════════════════════
        //  M11 美术层（版式依据 `Assets/Art/_Reference/Reference.png`）
        // ══════════════════════════════════════════════════════
        //
        //  参考图是 1536×1024，本画布是 1920×1080。**没有**按比例拉伸 ——
        //  图上元素基本就是 1:1 像素尺寸直接落到 1920×1080 上（HUD 条 704 px 宽 ≈ 画布 37%，
        //  冷却槽 190 px 宽，角色画布 ~210–560 px，都是合理量级），
        //  所以这套常量全部是「切出来的成品 Sprite 原始像素」，不做二次缩放。
        //
        //  ⚠ 参考图与本作规则不一致的地方，一律以本作规则为准（用户 2026-09-17 确认「照参考外观还原」时
        //    也保留了卡牌与本作数值口径）：
        //    · 生命显示成 `Hp/MaxHp`（规则固定 4 点），不是参考图的 72/80
        //    · 参考图的「金币」本作没有 → 顶栏那一格改显示冷却区里未使用的光环指示物数
        //    · 手牌/迷你卡一律用本作自己的 `Assets/Art/Cards/` 那 40 张卡面（不用参考图里的卡样式）
        //    · 参考图里「冷却区1」那格右侧多出来的六边形属于参考图自己的排版，不照搬

        // ── 背景 ──────────────────────────────────────────────
        /// <summary>背景图原始尺寸（算 cover 尺寸用）。</summary>
        public const float BgNativeWidth = 1536f;
        public const float BgNativeHeight = 990f;

        // ── 顶部状态栏 ────────────────────────────────────────
        /// <summary>HUD 条距左 / 上边缘。</summary>
        public const float HudLeft = 20f;
        public const float HudTop = 14f;

        /// <summary>HUD 条成品尺寸（含切图留的 2 px 透明边）。</summary>
        public const float HudBarWidth = 708f;
        public const float HudBarHeight = 95f;

        // HUD 内部锚点：全部相对**条左上角**，取切图时实测的亮块包围盒
        // （实测数据见 `Tools/art-audit/slice_ui_kit.py` 的 HUD_REGIONS 注释）。
        /// <summary>名字（第一行，大字）。</summary>
        public const float HudNameX = 118f;
        public const float HudNameY = 16f;
        public const float HudNameWidth = 104f;
        public const float HudNameHeight = 32f;

        /// <summary>副标题（第二行，小字；本作显示手牌数）。</summary>
        public const float HudSubX = 118f;
        public const float HudSubY = 51f;
        public const float HudSubWidth = 104f;
        public const float HudSubHeight = 22f;

        /// <summary>生命数字「n/m」。</summary>
        public const float HudHpX = 274f;
        public const float HudHpY = 31f;
        public const float HudHpWidth = 78f;
        public const float HudHpHeight = 32f;

        /// <summary>资源数字（未用光环数）。</summary>
        public const float HudResX = 434f;
        public const float HudResY = 33f;
        public const float HudResWidth = 64f;
        public const float HudResHeight = 28f;

        /// <summary>齿轮按钮（设置入口）—— 位置与条上烘的那颗齿轮重合。</summary>
        public const float HudGearX = 617f;
        public const float HudGearY = 22f;
        public const float HudGearSize = 45f;

        // ⚠ M15 删掉了 BuffFirstX / BuffRowTop / BuffSpacing —— 那是顶栏下面「一排 5 个占位状态图标」
        //   的位置。用户要求把光环真正摆出来之后，那排假图标连同这几个常量一起作废；
        //   真正的光环图标区改由本节末尾的 HudBuffTop / BuffIconSpacing 等常量描述。

        public const float FontSizeHudName = 26f;
        public const float FontSizeHudSub = 17f;
        public const float FontSizeHudHp = 25f;
        public const float FontSizeHudRes = 22f;

        // ── 两侧冷却槽（每侧 4 行，行 = 一个「剩余冷却」值）──────
        //
        // 卡面线性放大 30% 至 137.8×193.7，行间保留 10 px。
        // 四行内容共 804.8 px，由 CooldownView 在 115…736 的视口内纵向滚动。
        public const float SlotRowTop = 142f;
        public const float SlotRowPitch = SlotMiniCardHeight + 10f;
        public const float SlotLeft = 20f;
        public const float SlotRight = 20f;

        /// <summary>左槽（己方）成品尺寸。</summary>
        public const float SlotPlayerWidth = 190f;
        public const float SlotPlayerHeight = 95f;

        /// <summary>右槽（敌方）成品尺寸 —— 每行的成品图都不一样宽（182 / 183 / 185）。</summary>
        public const float SlotEnemyWidth = 182f;
        public const float SlotEnemyHeight = 84f;

        /// <summary>
        /// 右列「冷却区1」槽的宽度 —— <b>2026-09-20 用户手工校准值，不要按成品图重算</b>。
        ///
        /// <para><b>来历（一条已作废的分支留下的坑）</b>：这张槽图最早<b>烘了一张敌方假卡面</b>
        /// （原图 <c>SlotR_CD1_Raw.png</c> 224 宽），所以构建器里有一条
        /// 「右列只有冷却区1 带内嵌卡框、宽一截 → 撑到 224」的分支。
        /// 后来 M11 把那张假卡面擦掉了（见 `11-美术层实现说明.md`），重切出来的
        /// <c>SlotR_CD1.png</c> 只剩 <b>185×86</b>（右侧还留 2 px 全透明）——
        /// 那条分支却留着，于是每次重建都把 185 的图<b>横向拉 21%</b> 成 224，
        /// 并且让这一行的第 4 张迷你卡越过视口遮罩左边被 <c>RectMask2D</c> 裁掉。
        /// 用户在 Prefab 里把它手工改成 <b>183×86</b>（= 185 减掉那 2 px 透明边）。</para>
        ///
        /// <para>本常量就是那个手改值的落点：构建器按它建，重跑 M11 不再冲掉用户的调整。</para>
        /// </summary>
        public const float SlotEnemyCD1Width = 183f;

        /// <summary>
        /// 冷却列的<b>宽度预算</b>用的「最宽槽宽」—— <b>不是任何一行的实际宽度</b>。
        ///
        /// <para>列宽 = 本值 + <see cref="SlotMiniCardGap"/> + <see cref="SlotMiniCardWidth"/>
        /// + (SlotMiniCardMax − 1) × <see cref="SlotMiniCardStep"/> = <b>674</b>，
        /// 视口（同时也是遮罩）就按它建。实际最宽的槽只有 183，多出来的 41 px 是<b>遮罩余量</b>：
        /// 一行的迷你卡超过 4 张时会继续往画面中间长，留这点宽度才不会让它们被裁掉
        ///（<c>SlotMiniCardMax</c> 只约束「不重叠的步进」，没有硬性截断张数）。</para>
        /// </summary>
        public const float SlotColumnBudgetWidth = 224f;

        // ⚠ M12 已删除 SlotPlayerCardX/Y 与 SlotEnemyCardX/Y ——
        //   迷你卡不再按「槽内烘出来的卡框」摆位，改成按运行时的槽宽往外侧推
        //   （见 CooldownView.BindGrouped）。留着一组没人用的常量比删掉更容易误导下一个改版式的人。

        /// <summary>冷却区纯牌面宽高在上一版基础上各增加 30%。</summary>
        public const float SlotMiniCardWidth = 106f * 1.3f;
        public const float SlotMiniCardHeight = 149f * 1.3f;

        /// <summary>
        /// 槽与卡的间距（卡在槽外侧，不叠上去）。
        /// </summary>
        public const float SlotMiniCardGap = 8f;

        /// <summary>
        /// 同一行内多张迷你卡的水平步进。**必须接近卡宽** ——
        /// 步进太小的重叠会把卡面遮成一条竖条，那「看画面认牌」就白做了。
        /// 78 / 106 = 每张露出 74%（与 44/60、66/90 是同一个口径）。
        /// </summary>
        public const float SlotMiniCardStep = 78f * 1.3f;

        /// <summary>一行最多直接摆几张（再多只摆前面几张，剩余靠行首数字体现）。</summary>
        public const int SlotMiniCardMax = 4;

        // 迷你卡内部的版式（M15 第一轮随卡体 ×1.5，第二轮随「面积 +40%」再 ×1.18）
        /// <summary>力量 / 剩余冷却 <b>数字底下那块深色底</b>的高。压在卡面插画上必须给底，否则数字读不出来。</summary>
        public const float SlotMiniChipHeight = 38f;

        /// <summary>力量底块的宽（只有 1–2 个数字，窄一点少挡插画）。</summary>
        public const float SlotMiniPowerChipWidth = 46f;

        /// <summary>冷却底块的宽（要同时竖排「剩余」和「基础」两行，高一些）。</summary>
        public const float SlotMiniCooldownChipWidth = 53f;

        /// <summary>卡名条的高（贴卡底）。</summary>
        public const float SlotMiniNameHeight = 32f;

        /// <summary>底衬距卡边的内边距。</summary>
        public const float SlotMiniPad = 4f;

        /// <summary>冷却底衬比力量底衬额外多出的高（竖排两行字号 32 + 20）。</summary>
        public const float SlotMiniCooldownExtra = 28f;

        /// <summary>「基础冷却」小字那一行的高。</summary>
        public const float SlotMiniCooldownBaseHeight = 25f;

        /// <summary>光环亮点那一排的宽（双光环最多 2 枚）。</summary>
        public const float SlotMiniPipRowWidth = 78f;

        /// <summary>光环亮点那一排的高。</summary>
        public const float SlotMiniPipRowHeight = 25f;

        public const float FontSizeMiniPower = 32f;
        public const float FontSizeMiniCooldown = 32f;
        public const float FontSizeMiniCooldownBase = 20f;
        public const float FontSizeMiniName = 26f;

        /// <summary>卡名的自动缩放下限（「防御姿态」4 个字要挤进 98 px）。</summary>
        public const float FontSizeMiniNameMin = 15f;

        // ── 能量球 ────────────────────────────────────────────
        public const float OrbLeft = 22f;
        public const float OrbBottom = 22f;
        public const float OrbSize = 150f;

        public const float FontSizeOrbNumber = 34f;

        // ── 舞台角色 ──────────────────────────────────────────
        /// <summary>
        /// 角色显示缩放（切图后的画布像素 → 界面像素）。
        ///
        /// <para><b>2026-09-18 由 1.3 调到 1.55</b>：新一批主角素材每帧画布更大、
        /// 角色本身画得更小 —— 旧待机「本体高」205 px（画布 208×220，几乎占满），
        /// 新待机只有 172 px（画布 233×213，占八成）。继续用 1.3 会让主角凭空小一圈。</para>
        ///
        /// <para>标定口径 = <b>角色在屏幕上的高度不变</b>：
        /// 205 × 1.3 = 172 × 1.55 ≈ 266 px。**换素材必须回来重算这一条** ——
        /// 本体高不是包围盒高（包围盒含光效），量法见 `Tools/art-audit/slice_hero_anim.py`
        /// 的 <c>body_height()</c>，核对图在 `Tools/art-audit/_work/hero2_*.png`。</para>
        /// </summary>
        public const float CharScale = 1.55f;

        /// <summary>角色「脚底」距屏幕底边的距离（两名角色共用一条地平线）。</summary>
        public const float CharGroundY = 402f;

        /// <summary>
        /// 两名角色之间的水平距离（脚底中心之间）。
        ///
        /// <para><b>2026-09-19 由 870 收到 783（−10%）</b>：用户嫌两人离得太远。
        /// 783 = 870 × 0.9。改间距只需要动这一个数 —— 两侧的 x 都由它 + 中线推出来，
        /// 两名角色永远对称地贴着中线站。</para>
        /// </summary>
        public const float CharSpacing = 783f;

        /// <summary>
        /// 两名角色这条轴线的中心（画布绝对坐标）。
        ///
        /// <para>注意它<b>不是画面正中</b>（960）：历史上两人整体略偏左，右边要给冷却槽让位。
        /// 这个偏移一直存在，缩间距时保持不变即可（只动 <see cref="CharSpacing"/>）。</para>
        /// </summary>
        public const float CharCenterX = 855f;

        /// <summary>主角脚底中心的水平位置（中心往左半个间距）。</summary>
        public const float CharHeroX = CharCenterX - CharSpacing * 0.5f;

        /// <summary>怪物脚底中心的水平位置（中心往右半个间距）。</summary>
        public const float CharMonsterX = CharCenterX + CharSpacing * 0.5f;

        // ── 各动作的帧率 ──────────────────────────────────────
        //
        //  ⚠ **帧数变了就回来改这里。** 第一批素材每个动作只有 5 帧，
        //    第二批（2026-09-18）待机 18 帧、出招 / 防御 / 受击 / 倒地各 12 帧。
        //    沿用旧的 6 / 11 会让待机 3 秒才走完一圈、出招拖到 1 秒多，明显发飘。
        //
        //  这几个值是写进 `Clip.Fps` 的初值（由 `ArtImportBuilder` 落到映射表），
        //  运行时 `CharacterView` **优先用素材自带值**，没写才退回兜底。

        /// <summary>待机循环。18 帧 ≈ 1.8 秒一圈 —— 再慢就像定格。</summary>
        public const float CharIdleFps = 10f;

        /// <summary>出招。12 帧 ≈ 0.86 秒（起手 → 蓄力 → 发射 → 收招）。</summary>
        public const float CharAttackFps = 14f;

        /// <summary>防御。12 帧 ≈ 0.86 秒。</summary>
        public const float CharDefendFps = 14f;

        /// <summary>受击。12 帧 ≈ 0.67 秒 —— 比出招快，挨打是一瞬间的事。</summary>
        public const float CharBeHitFps = 18f;

        /// <summary>倒地。12 帧 ≈ 1.33 秒 —— 慢一档，演出看得清，也对应「认输」的节奏。</summary>
        public const float CharDeathFps = 9f;

        /// <summary>头顶徽标：宽高，以及中心距地平线的高度。</summary>
        public const float CharBadgeWidth = 132f;
        public const float CharBadgeHeight = 42f;
        public const float CharBadgeGroundOffset = 392f;

        // ── 虚弱徽标（2026-09-29，毒刺 ao）─────────────────────
        //
        //  版式账（画布 1920×1080，坐标从左下角算）：
        //    怪物脚底 = CharGroundY 402，模型 398×335 → 顶边 y = 737；
        //    头顶「生命 n/m」徽标中心 = 402 + 392 = 794（占 773…815）；
        //    虚弱徽标**再往上叠一行**：中心 = 794 + 21 + 6 + 14 = 835。
        //
        //  为什么往上叠而不是并排：徽标宽只有 132，横向塞不下第二行读数；
        //  而头顶那一片本来就是空的（模型顶边 737 以上、思考框在更右侧 x≈1446）。
        //
        //  ⚠ 双方各一枚（主角 / 怪物）—— 虚弱是「被攻击目标」身上的状态，
        //    玩家会被削、怪物也会被削，只画一侧就有半边局面看不见。

        /// <summary>虚弱徽标中心距地平线的高度（= 头顶徽标中心 + 半高 + 缝 + 半高）。</summary>
        public const float CharWeakenGroundOffset = 435f;

        public const float CharWeakenWidth = 160f;
        public const float CharWeakenHeight = 28f;

        public const float FontSizeCharBadge = 24f;

        // ══════════════════════════════════════════════════════
        //  M15 · ArtLayer/HudBuff 光环图标区 + 拖到「自己的手牌区」使用
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-18）：
        //    · 双方持有的光环显示在 ArtLayer 的 HudBuff 里，**玩家在左、敌人在右**；
        //    · 相同的 buff 不堆叠 —— **每有一枚指示物就摆一个图标**（不是「同名合成一个 ×N」）；
        //    · 用掉 / 消失之后把图标移除；
        //    · 长按图标看它的具体内容；
        //    · 把图标拖到**自己的手牌区**即视为使用这枚光环（仅限本拍确实能用的那几枚）。
        //
        //  版式账（画布 1920×1080，坐标从屏幕顶 / 左算）：
        //    · 顶栏 HUD 条占 (20,-14) 708×95 → 下沿 y=109；
        //    · **⚠ M21（2026-09-20）：图标行的默认位改到「角色头顶血量徽标」旁边** ——
        //        用户口径：主角的光环排在**徽标右侧**、怪物侧镜像到**徽标左侧**，一行横排。
        //        徽标 = Char_Hero / Char_Monster 的 Badge（132×42，底边距地平线 CharBadgeGroundOffset）：
        //          主角徽标 x 397.5…529.5、y 244…286（中心 463.5 / 265）
        //          怪物徽标 x 1180.5…1312.5（中心 1246.5，与主角关于 CharCenterX 对称）
        //        图标行与徽标**同高**（中心 y = AuraIconRowCenterY = 265），
        //        玩家侧从徽标右缘 +36 起往右排，怪物侧从徽标左缘 −36 起往左排。
        //    · M15/M18 的旧口径（图标行贴顶栏下沿 y=119、各自贴在冷却列内侧）已废弃。
        //
        //  ⚠ HudBuff 根节点是**铺满画布**的：图标拖拽时要能在画布任意位置自由摆
        //    （挂在一个只有几十像素宽的组里，拖出组外就得换算半天）。
        //  ⚠ 一排 8 枚 × 80 = 640 px：玩家行右端到 1205、怪物行左端到 505 ——
        //    **双方各准备 4 枚以上时会在画面正中叠一起**。这是「都往中间长」的必然结果，
        //    实际对局里同一拍准备的光环通常 1~3 枚，先按用户口径实现；
        //    真要更多，减掉 Slot 节点即可（本行只挂到「编号找得到的那个槽位」为止）。

        /// <summary>光环图标边长（图标内部的符号 / 数值尺寸都按它定）。</summary>
        public const float BuffIconSize = 72f;

        /// <summary>
        /// 同一行里相邻图标的水平步进（= 图标 72 + 8 的间隙）。
        ///
        /// <para><b>M21 起由构建器按它生成槽位</b>：<c>PlayerGroup</c> / <c>EnemyGroup</c> 下的
        /// <c>Slot0</c>…<c>Slot7</c> 由 <c>BattleArtLayerBuilder.BuildBuffGroup</c> 建出来，
        /// 坐标 = 本值 × 序号 —— 手改槽位在重跑 M11 时会被推倒重建冲掉
        /// （M20 就是这么丢的，见 <see cref="HudBuffTop"/> 的注释）。</para>
        /// </summary>
        public const float BuffIconSpacing = 80f;

        /// <summary>图标行摆几枚（槽位 Slot0…Slot7）。</summary>
        public const int BuffIconSlots = 8;

        /// <summary>
        /// 图标行的**中心线**距屏幕顶边 = 角色血量徽标的中心线（徽标底边 286 − 半高 21）。
        ///
        /// <para>构建器由它反推组的顶端：<c>HudBuffTop = AuraIconRowCenterY − BuffIconSize/2</c>。</para>
        /// </summary>
        public const float AuraIconRowCenterY = 265f;

        /// <summary>
        /// 图标与血量徽标之间的净空（玩家侧从徽标右缘往右、怪物侧从徽标左缘往左）。
        ///
        /// <para>36 来自用户给的第 2 张参考图：那枚盾牌图标左缘 564、主角徽标右缘 529.5。</para>
        /// </summary>
        public const float AuraIconBadgeGap = 36f;

        /// <summary>图标块顶端距画布上边缘（= 中心线 265 − 半格 36）。</summary>
        public const float HudBuffTop = AuraIconRowCenterY - BuffIconSize * 0.5f;

        /// <summary>
        /// 玩家块（左）距左边缘 —— <b>块左边缘的 x</b>（块内 Slot0 就贴在这条线上）。
        ///
        /// <para>= 主角血量徽标右缘（<c>CharHeroX + CharBadgeWidth/2</c> = 529.5）+ 净空 36
        /// = 565.5。M15 时这个数写成「冷却卡最右 558 + 8」，M21 把它换成了**徽标的推导** ——
        /// 徽标动了这一行就跟着动，不用去数字符。</para>
        /// </summary>
        public const float HudBuffPlayerX = CharHeroX + CharBadgeWidth * 0.5f + AuraIconBadgeGap;

        /// <summary>
        /// 敌人块（右）距右边缘 —— <b>块右边缘到屏幕右边的距离</b>（内部按右上锚点往左长）。
        ///
        /// <para>= 1920 − (怪物徽标左缘 <c>CharMonsterX − CharBadgeWidth/2</c> = 1180.5 − 净空 36)
        /// = 775.5。玩家侧往右长、怪物侧往左长 —— 两侧都是「从自己的徽标往外走」。</para>
        /// </summary>
        public const float HudBuffEnemyX = ReferenceWidth
            - (CharMonsterX - CharBadgeWidth * 0.5f - AuraIconBadgeGap);

        /// <summary>图标外框（描边）粗细。</summary>
        public const float BuffIconEdgeWidth = 3f;

        /// <summary>单个符号（剑 / 盾 / 感叹号）的边长。</summary>
        public const float BuffIconGlyph = 44f;

        /// <summary>「攻防二选一」那种要并排画两个符号时的符号边长。</summary>
        public const float BuffIconGlyphDual = 30f;

        /// <summary>并排两个符号时各自的水平偏移。</summary>
        public const float BuffIconGlyphDualOffset = 15f;

        /// <summary>符号中心相对图标中心再往上抬多少（给下边那颗数值腾地方）。</summary>
        public const float BuffIconGlyphOffsetY = 6f;

        /// <summary>图标里那颗数值（+2 / ≥6 / ≤3）的尺寸。</summary>
        public const float BuffIconValueWidth = 56f;
        public const float BuffIconValueHeight = 24f;

        /// <summary>数值底衬距图标下边缘。</summary>
        public const float BuffIconValueBottom = 4f;

        public const float FontSizeBuffValue = 20f;

        /// <summary>拖着图标走时，图标相对指针再往上抬一点（别被手指/指针挡住）。</summary>
        public const float BuffIconDragLift = 34f;

        /// <summary>「可拖动使用」时图标外圈的点亮描边粗细。</summary>
        public const float BuffIconReadyEdgeWidth = 4f;

        // ── 长按光环弹出的说明浮层 ────────────────────────────
        //
        //  图标行贴着画布顶，所以浮层只能往下摆（图标下沿 + BuffTipBelow）。
        //  高度按「来源行 + 说明最多两行 + 提示行」估：132 = 12×2 + 26 + 21×2 + 18 + 6×2 + 余量。
        public const float BuffTipWidth = 400f;
        public const float BuffTipHeight = 132f;
        public const float BuffTipPadding = 12f;
        public const float BuffTipGap = 6f;

        /// <summary>浮层顶端距图标下沿的间隙。</summary>
        public const float BuffTipBelow = 10f;

        /// <summary>浮层左右距画布边缘的最小留白（贴边时把浮层拽回画面内）。</summary>
        public const float BuffTipScreenPadding = 16f;

        public const float FontSizeBuffTipName = 24f;
        public const float FontSizeBuffTipBody = 21f;
        public const float FontSizeBuffTipHint = 18f;

        // ══════════════════════════════════════════════════════
        //  M16/M21 · 手牌区左侧的「已准备光环」标记行
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-19 定稿落区，2026-09-20 改摆位与取消手势）：
        //    · 落区 = **自己的手牌区**（HandArea，底部 344 px 通栏）—— 与「出牌判定区」
        //      （中央 PlayZone）**不是同一个区**；出牌仍然拖进中央判定区；
        //    · 松手在手牌区里 = 这枚光环「准备使用」：原图标从 HudBuff 消失，
        //      标记**飞**到**手牌区的左方**排好（见 AuraFlySeconds）；
        //    · 取消 = **再拖一下这枚标记**（M21 改）：一进入拖拽就复位回默认位
        //      （角色徽标旁那行），不再是「拖出判定区」那套落点判定；
        //    · 真被引擎消耗掉之后，标记自然消失（选项里不再有它）。
        //  用整块 HandArea 而不是扇面本身那个「每帧都在动」的弧：弧上每张牌的位置/角度
        //  都是平滑趋近的，拿它当判定框会「看着放进去了其实没进」；落区大一点才跟手。
        //  代价是左下角能量球也落在这块里 —— 它不参与光环语义，放着不影响（不做规则判断，铁律 3）。
        //
        //  摆位（M21）：`PreparedAura` 下的空节点 Slot0…Slot7 由构建器建出来，
        //  槽位坐标由本节的三个常量算出（不再手摆在 Prefab 里 —— 手摆的在重跑 M8 时会被
        //  `Cleanup` 整棵删掉，M20 已经丢过一次）。
        //  图标尺寸仍沿用 BuffIconSize —— AuraIconView 内部的符号 / 数值尺寸都按它定，
        //  换一档尺寸会让「+2」压到图标框外。
        //
        //  ⚠ 提示底（HandArea/AuraDropHint）的颜色**不在代码里**：
        //    只在 Prefab 的 Image.color 上调，运行时一个字节都不写。
        //    （M21 删掉了另一个同类节点 PreparedAura/ZoneHighlight。）
        //
        //  ⚠ M18 已删除 PreparedAuraLeftInset / PreparedAuraSpacing / PreparedAuraZoneInset：
        //    这三个常量描述的正是上面那套「跟着判定区算」的算法，现在没有任何地方再用它们。

        /// <summary>
        /// 准备标记行的**中心线**距屏幕底边。
        ///
        /// <para>版式账：手牌区高 344；能量球占左下 (22,22) 150×150 → 顶边到 172。
        /// 标记行高 72，取中心 216 → 占 180…252，与能量球留 8 px 净空。
        /// 手牌扇形在 5 张时最左约 410，标记行从 24 起 —— 前几枚绝不会压到手牌；
        /// 准备 5 枚以上时才会碰到（同一拍准备这么多是极端情况）。</para>
        /// </summary>
        public const float PreparedAuraRowCenterY = 216f;

        /// <summary>准备标记行最左一枚的左边缘距屏幕左边（其余按 <see cref="BuffIconSpacing"/> 往右排）。</summary>
        public const float PreparedAuraX = 24f;

        /// <summary>
        /// 准备标记「飞入」的时长（秒，无缩放时间）。
        ///
        /// <para>短到不拖沓、长到能被看见：起点是松手时指针的位置（手牌区里），
        /// 终点在手牌区左侧，跨度不大，所以比一般的 UI 过渡略长一点即可。</para>
        /// </summary>
        public const float AuraFlySeconds = 0.32f;

        /// <summary>飞入起始缩放 —— 从「一小撮」长大到位，配合位移做出「飞过去」的读感。</summary>
        public const float AuraFlyStartScale = 0.55f;

        /// <summary>
        /// 「取消使用」时，标记从准备位**滑回默认位**（角色徽标旁）的时长（秒，无缩放时间）。
        ///
        /// <para>与 <see cref="AuraFlySeconds"/> 同量级：这一段跨越的屏幕距离更长
        /// （手牌区左侧 → 顶部徽标旁），太短就成了「闪一下」，太长会挡住玩家接着操作。
        /// 缩放**不变**（两端都是成品尺寸）—— 见 <c>AuraIconView.GlideTo</c> 的说明。</para>
        /// </summary>
        public const float AuraReturnSeconds = 0.30f;

        /// <summary>
        /// 「取消其中一枚」之后，其余标记**往前补位**的滑行时长（秒，无缩放时间）。
        ///
        /// <para>比飞回更短：这只是一格的位移（<see cref="BuffIconSpacing"/> = 80 px），
        /// 目的只是别让后面几枚瞬间跳一格。0 也能用（= 瞬移，M34 之前的表现）。</para>
        /// </summary>
        public const float AuraReflowSeconds = 0.16f;

        // ══════════════════════════════════════════════════════
        //  M36 · 「AI 用掉了一枚光环」的识别动画（飞向怪物模型右侧）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-23）：怪物用手里的光环时，**应当有一个光环移动的过程**
        //  帮玩家识别 —— 那一枚飞到**怪物模型的右侧**。
        //
        //  版式账（画布 1920×1080，坐标从左下角算）：
        //    怪物脚底中心 = (CharMonsterX 1246.5, CharGroundY 402)，
        //    待机帧 257×216 × CharScale 1.55 = 398×335 → 模型占 x 1048…1445、y 402…737。
        //    落点取 **模型右缘 + 就近一点、腰部高度**：
        //      x = 1246.5 + 200 = 1446.5（贴着模型右侧，不与本体叠）
        //      y = 560（`CharHandCount` 标签在 408…448 —— 落点抬到它上面 112 px，不会压住它）
        //  ⚠ 敌人冷却列的迷你卡会伸到 x ≈ 1226 以左，落点所在的 x 落在那一带的上层 ——
        //    这一枚是**一次性演出**（停留不到 1 秒就淡出），且飞行层在画布根（压在所有界面之上），
        //    所以「短暂压住几张迷你卡」是可接受的代价；想彻底避开就把 AuraCastX 再往右挪。

        /// <summary>落点相对「怪物脚底中心」的横向偏移（正数往右）。</summary>
        public const float AuraCastX = 200f;

        /// <summary>落点的绝对纵坐标（画布坐标，从下边缘算）。</summary>
        public const float AuraCastY = 560f;

        /// <summary>这一枚从敌方光环行飞到落点的时长（秒，无缩放时间）。</summary>
        public const float AuraCastFlySeconds = 0.45f;

        /// <summary>落点停留时长（秒）—— 停一下让眼睛跟得上，再淡出。</summary>
        public const float AuraCastHoldSeconds = 0.65f;

        /// <summary>落地后的淡出时长（秒）。</summary>
        public const float AuraCastFadeSeconds = 0.25f;

        /// <summary>落地那一刻的轻微放大（读作「用掉了」）—— 1 = 不放大。</summary>
        public const float AuraCastLandScale = 1.15f;

        // ══════════════════════════════════════════════════════
        //  M16 · 组成式卡面（手牌与冷却迷你卡共用一套设计空间）
        // ══════════════════════════════════════════════════════
        //
        //  ── 口径 ──────────────────────────────────────────────
        //  卡面版式**逐字照抄** `ThirdParty/MagicCardKit/Prefabs/BigMagicCard.prefab`：
        //  锚点、pivot、anchoredPosition、尺寸、字号全部取自它，不参考任何「烘焙好的卡面整图」。
        //  ⚠ 2026-09-30（用户口径）：原来并列存在的那一族 760×1056 成品整图
        //  （`Assets/Art/Cards/`，卡名 / 力量 / 冷却 / 文字全烘在 PNG 里）**已整族删除** ——
        //  卡面的一切文字与数值现在只有「TMP 现场渲染」这一条来源，图片只提供无框无字的插画。
        //
        //  设计空间 = 622×872，就是那个 prefab 的 Canvas 尺寸；卡 = 画布整块。
        //
        //  下面每条常量后面的注释都写明了它在 prefab 里的对应写法，方便逐条对账。
        //  **不要凭「看起来该在哪」去改这些数** —— 比如力量徽标是真的故意越出卡角
        //  10 px，冷却徽标也不是右上角而是左侧、力量徽标正下方。
        //
        //  ── 与 prefab 的两处必要偏差 ────────────────────────────
        //  ① 圆角与描边：prefab 靠 ShaderGraph（RoundBox / RoundBoxLine）画，那套 shader
        //     在 UGUI 下不吃顶点色与精灵贴图（实测整张卡渲成纯白）。改用九宫格圆角图，
        //     半径 30 / 线宽 4 由那张图给定。
        //  ② 效果面板高度：prefab 的 `Content/BG` 是 620×1118.5 —— 比卡本身还高，
        //     是**设计成可以从卡里抽出来的展示面板**。手牌只有 272 px 高（设计空间 872），
        //     面板必须收回卡内：保留它的宽度 620 与「包住文字」的用意，把高度收到 300。

        /// <summary>卡面设计空间宽（= BigMagicCard.prefab 的 Canvas 宽）。</summary>
        public const float CardSpaceWidth = 622f;

        /// <summary>卡面设计空间高。</summary>
        public const float CardSpaceHeight = 872f;

        /// <summary>卡框圆角半径（设计空间）。见本段开头「偏差 ①」。</summary>
        public const float CardSpaceRadius = 30f;

        /// <summary>卡框描边粗细。见本段开头「偏差 ①」。</summary>
        public const float CardSpaceEdgeWidth = 4f;

        // ── 力量徽标：prefab `Power` 200×200 @ 锚(0,1) pivot(0,1) pos(−10, 10) ──
        public const float CardPowerSize = 200f;

        /// <summary>越出卡左边缘 10 px（prefab 的原值，故意的）。</summary>
        public const float CardPowerOffsetX = -10f;

        /// <summary>越出卡上边缘 10 px（prefab 的原值，故意的）。</summary>
        public const float CardPowerOffsetY = 10f;

        /// <summary>力量数字文本框：prefab `Power/Text (TMP)` 155.53×179.2，比徽标本身还高。</summary>
        public const float CardPowerTextWidth = 155.53f;
        public const float CardPowerTextHeight = 179.2f;

        /// <summary>相对徽标中心的偏移（prefab 原值 6 / 9.6 —— 数字偏上偏右）。</summary>
        public const float CardPowerTextOffsetX = 6f;
        public const float CardPowerTextOffsetY = 9.6f;

        // ── 冷却徽标：prefab `CD` 190×190 @ 锚(0,1) pivot(0,1) pos(1, −191.87) ──
        //    注意它在**左侧、力量徽标的下面**，不是右上角。
        public const float CardCoolSize = 190f;
        public const float CardCoolOffsetX = 1f;
        public const float CardCoolOffsetY = -191.87f;

        /// <summary>冷却数字文本框：prefab `CD/Text (TMP)` 145×145。</summary>
        public const float CardCoolTextSize = 145f;
        public const float CardCoolTextOffsetX = 0f;
        public const float CardCoolTextOffsetY = 6f;

        // ── 卡名：prefab `Name` 356.6×93.6 @ 锚(0.5,1) pivot(0.5,1) pos(52.5, −47.1) ──
        //    偏右 52.5 —— 给左边的徽标让位。
        //
        //  ⚠ 2026-09-25 用户在手牌 Prefab 上把这一格**加宽并右移**（301.2 → 356.6 / 50.7 → 52.5）：
        //    四字卡名原来会被挤到自动缩号，加宽之后不用缩了。口径固化在这里，
        //    以后重建出来的卡面与手工改的那份就是同一组数。
        public const float CardNameWidth = 356.6f;
        public const float CardNameHeight = 93.6f;
        public const float CardNameOffsetX = 52.5f;
        public const float CardNameOffsetY = -47.1f;

        /// <summary>卡名左右各留多少（免得名字顶到衬底边）。</summary>
        public const float CardNamePad = 14f;

        // ── 效果文字：prefab `Content/Content` 585.4×252.4 @ 锚(0.5,0.5) pivot(0.5,1) pos(0, −131.4) ──
        public const float CardTextWidth = 585.4f;
        public const float CardTextHeight = 252.4f;
        public const float CardTextOffsetX = 0f;
        public const float CardTextOffsetY = -131.4f;

        /// <summary>效果面板宽度（= prefab `Content/BG` 的宽）。</summary>
        public const float CardPanelWidth = 620f;

        /// <summary>
        /// 效果面板高度。prefab 是 1118.5（抽出式展示面板）；这里收到恰好包住文字：
        /// 文字 252.4 + 上下各 23.8 = 300 → 面板底边 y = −407.6，卡底 −436，余 28.4。
        /// </summary>
        public const float CardPanelHeight = 300f;

        /// <summary>效果面板中心 y（与文字同中心 → 上下留白均等）。</summary>
        public const float CardPanelCenterY = -257.6f;

        // ── 字号（全部取自 prefab 的 TMP fontSize）──────────────
        /// <summary>力量数字：prefab `Power/Text` 的 160。</summary>
        public const float CardFontSizePower = 160f;

        /// <summary>冷却数字：prefab `CD/Text` 的 155。</summary>
        public const float CardFontSizeCool = 155f;

        /// <summary>卡名：prefab `Name/Name` 的 72。</summary>
        public const float CardFontSizeName = 72f;

        /// <summary>
        /// 卡名自动缩放的**下限**。
        ///
        /// <para>prefab 的 Name 框只有 301.2 宽、字号 72 → 一行恰好放得下 <b>4.18</b> 个字。
        /// 本作最长的卡名正好是 4 字（「冰封铠甲」「烈焰斗篷」「飞叶连击」…），
        /// 扣掉左右留白就顶出去了 —— 不加这一条，卡名会被省略号截成「冰封…」。</para>
        /// </summary>
        public const float CardFontSizeNameMin = 52f;

        /// <summary>效果文字：prefab `Content/Content` 的 49。</summary>
        public const float CardFontSizeEffect = 49f;

        /// <summary>
        /// 效果文字自动缩放的**下限**。
        /// prefab 没有这一条 —— 它那张示例卡的文字是固定的两行；本作 40 张卡的效果
        /// 条数与字数差异很大，装不下时整体缩到 30（约 0.61×）而不是直接截断。
        /// </summary>
        public const float CardFontSizeEffectMin = 30f;

        /// <summary>设计空间 → 目标卡宽的缩放比。卡的两种尺寸共用它换算版式。</summary>
        public static float CardSpaceScale(float cardWidth)
        {
            return cardWidth / CardSpaceWidth;
        }

        // ══════════════════════════════════════════════════════
        //  M25 · 查看对方手牌弹窗（雷云 / 狂躁蘑菇的第 2 个 α 效果）
        // ══════════════════════════════════════════════════════
        //
        //  这是一块**独立于既有版式**的浮层：铺满画布的遮罩 + 正中一块面板，
        //  面板里摆 N 张牌背（N = 对手当时的手牌数），玩家点一张 → 翻正面 → 结算 → 关闭。
        //
        //  ⚠ 下面这组数字**不是随手定的**，它们是从素材原图上量出来的
        //   （见 `Tools/art-audit/slice_peek_kit.py` 的推导）：
        //
        //      面板原图 992×447，牌背原图 186×256
        //      面板宽 992 = 4×186 + 3×24 + 2×88      ← 4 张一行、间距 24、左右各 88
        //      面板高 447 =   256 + 2×95.5           ← 上下各 95.5
        //
        //  所以「横向永远用原宽 992」是刻意的：顶部中央那块凸起的标题牌位落在九宫格的
        //  「上中」格，横向一拉伸就被抻开。行数不够时**只往纵向长**（纵向有一条
        //  完全平坦的躯干带可以无损拉伸，见下）。

        /// <summary>
        /// 面板宽度。<b>恒等于原图宽，任何情况下都不缩放</b>（理由见上）。
        /// </summary>
        public const float PeekPanelWidth = 992f;

        /// <summary>牌背的显示尺寸（= 原图尺寸，不缩放）。</summary>
        public const float PeekCardWidth = 186f;

        public const float PeekCardHeight = 256f;

        /// <summary>牌与牌的水平间距。</summary>
        public const float PeekCardGap = 24f;

        /// <summary>两行时的行距。</summary>
        public const float PeekRowGap = 24f;

        /// <summary>一行最多几张牌（= (992 − 2×88) ÷ (186 + 24)）。</summary>
        public const int PeekMaxPerRow = 4;

        /// <summary>
        /// 预建的牌位总数（= 对手手牌上限）。
        ///
        /// <para>牌位由 M8 构建器一次建满 8 个，运行时只激活前 N 个 ——
        /// 这是「要长期存在的节点一律由构建器生成」那条铁律的又一次应用：
        /// M21 在 <c>PreparedAura</c> 上手摆的 <c>Slot0…7</c> 被重跑冲掉，
        /// 症状是「标记没有落点」且<b>零报错</b>。</para>
        /// </summary>
        public const int PeekSlotCount = 8;

        /// <summary>牌区左右留白。</summary>
        public const float PeekSidePadding = 88f;

        /// <summary>
        /// 牌区上边界 —— 让开顶部的标题牌位与装饰带。
        /// 原图实测：第 127 行之前全是标题 / 装饰（逐行标准差 &gt; 4），
        /// 128…398 是纯色躯干（标准差 ≈ 1.1）。
        /// </summary>
        public const float PeekContentTop = 128f;

        /// <summary>牌区下边界留白。</summary>
        public const float PeekContentBottom = 48f;

        /// <summary>一行时面板的高度（= 原图高，像素级不动）。</summary>
        public const float PeekPanelHeightOneRow = 447f;

        /// <summary>
        /// 两行时的面板高度 = 一行高 + 一行牌 + 行距。
        /// 纵向九宫格会把中间那条平坦躯干（原图 128…398，共 271 行）拉到 551 行 ——
        /// 约 ×2.03，而那片区域逐像素均匀，拉伸等于什么都不做。
        /// </summary>
        public const float PeekPanelHeightTwoRows = PeekPanelHeightOneRow + PeekCardHeight + PeekRowGap;

        /// <summary>
        /// 面板的纵向九宫格边界（左, 下, 右, 上）。
        /// <para>左右都是 0 —— 不是漏填：横向本来就不拉伸（见 <see cref="PeekPanelWidth"/>）。</para>
        /// <para>⚠ 这三个地方必须一致：本组常量、<c>Tools/art-audit/slice_peek_kit.py</c> 的说明、
        /// 编辑器侧的 <c>EnsurePeekImporters</c>。它们丢了只会让圆角被抻成椭圆，且<b>零报错</b>。</para>
        /// </summary>
        public const float PeekPanelBorderLeft = 0f;

        public const float PeekPanelBorderBottom = 48f;
        public const float PeekPanelBorderRight = 0f;
        public const float PeekPanelBorderTop = 128f;

        /// <summary>
        /// 标题文字框的<strong>顶边</strong>距面板顶边的距离。
        ///
        /// <para><b>38 这个数是量出来的</b>（`Tools/art-audit` 那套逐行不透明像素剖面）：
        /// `Peek_Panel.png` 顶部是一条窄尖 + 一条横band 组成的标题牌位 ——
        /// 第 0…44 行只有中间一小条（不透明像素 2→129，是牌位的尖顶），
        /// 第 45 行骤增到 330（横band 开始），第 80 行涨到 938（面板外框开始），
        /// 第 128 行起整幅不透明（纯色躯干）。所以牌位横band 覆盖 45…78、**中心 ≈ 61**。</para>
        ///
        /// <para>于是 `38 + PeekTitleHeight/2 = 38 + 23 = 61` —— 文字正落在牌位中心。
        /// ⚠ 早先填的 4 把标题摆到了面板<b>外面上方</b>悬空（中心 27），肉眼就是「标题飘在弹窗外」。</para>
        /// </summary>
        public const float PeekTitleTop = 38f;

        /// <summary>标题文字框的高度。</summary>
        public const float PeekTitleHeight = 46f;

        /// <summary>标题文字框宽度（标题牌位宽约 331，留点余量给「查看对方手牌」6 个字）。</summary>
        public const float PeekTitleWidth = 420f;

        /// <summary>结算结果角标：距牌底边的距离。</summary>
        public const float PeekBadgeBottom = 6f;

        public const float PeekBadgeHeight = 34f;

        // ── 动画时长（全部走 UiTween 的无缩放时间）────────────

        /// <summary>翻面的前半段（原面 → 侧薄）与后半段（侧薄 → 新面）。</summary>
        public const float PeekFlipOutSeconds = 0.15f;

        public const float PeekFlipInSeconds = 0.22f;

        /// <summary>整块浮层淡入 / 淡出的时长。</summary>
        public const float PeekFadeSeconds = 0.18f;

        /// <summary>浮层出现时面板的起始缩放（弹一下）。</summary>
        public const float PeekPanelPopFrom = 0.92f;

        /// <summary>面板那一下弹出的时长。与翻面时长分开给 —— 它们挂在两个不同的补间上。</summary>
        public const float PeekPanelPopSeconds = 0.22f;

        /// <summary>
        /// 翻完面之后，这块浮层还留多久再关（秒）。
        ///
        /// <para>停留结束时才开始淡出，交给 driver 的等待时长是
        /// <see cref="PeekBeatHoldSeconds"/>（= 本常量 + 淡出 + 余量）。</para>
        /// </summary>
        public const float PeekResultHoldSeconds = 1.25f;

        /// <summary>
        /// 这次查看一共该占住 driver 多久（<see cref="BattleDriver.BeatHold"/> 的返回值）。
        ///
        /// <para>它必须把「结果停留 + 淡出结束」都盖住：<see cref="PeekResultHoldSeconds"/>
        /// 之后浮层才开始淡出，淡出没走完就换下一拍的话，下一拍的选项已经亮起、而界面
        /// 上还压着一层半透明遮罩 —— 遮罩期间 <c>blocksRaycasts</c> 是开着的，
        /// 玩家看得见却点不动。末尾 0.1 s 是「淡出最后一帧到 <c>SetActive(false)</c>」的余量。</para>
        /// </summary>
        public const float PeekBeatHoldSeconds = PeekResultHoldSeconds + PeekFadeSeconds + 0.1f;

        // ── 字号 ─────────────────────────────────────────────
        public const float FontSizePeekTitle = 30f;

        /// <summary>结算角标字号。</summary>
        public const float FontSizePeekBadge = 22f;

        // ══════════════════════════════════════════════════════
        //  M27 · 手牌选择弹窗（磁暴 / 充能 / 电弧）
        // ══════════════════════════════════════════════════════
        //
        //  这三种效果都是「攻击时把手中若干张其它法术送入冷却」（`EffectOp.CoolHandForAtk` /
        //  `CoolHandForHaste` / `CoolHandForCombo`），引擎给的选项就是**玩家自己的手牌**。
        //  用户口径（2026-09-21）：不要用按钮列表，要一个**参考图那样的弹窗** ——
        //  顶部标题、中间一块「已选卡框」、底部一个确认键。
        //
        //  素材直接复用 M25 切出来的 `Peek_Frame.png`（431×556）——
        //  它就是「标题牌位 + 中间一块空卡框 + 底部一枚按钮座」的三段结构，
        //  与参考图那张面板完全同构（当初切出来时注释里写「留给放大查看的单卡展示」，
        //  这里正好用上）。逐像素量出来的三段边界见下。

        /// <summary>
        /// 面板尺寸基准（= `Peek_Frame.png` 原图高度，**纵向任何情况下都不缩放**）。
        ///
        /// <para>纵向绝不拉伸：这张图的三段装饰（顶部尖顶 + 两侧翼、中间内框、底部按钮座）
        /// 挤在同一块画布里，纵向一拉就把尖顶和按钮座抻变形。</para>
        ///
        /// <para><b>横向是可以拉的</b> —— 逐行剖面量出来的事实：
        /// 第 100…520 行两侧全幅（0…430）不透明、且边缘无装饰，只有顶部 0…99 与
        /// 底部 521…555 带翅膀 / 收边。所以九宫格的左右 border 取
        /// <see cref="HandPickPanelBorderLeft"/> / <see cref="HandPickPanelBorderRight"/>
        /// （= 110，正好落在「面板外框内侧、内框外侧」那段空白里），
        /// 中间那段横向拉伸时两侧的翼与收边**原地不动**。</para>
        /// </summary>
        public const float HandPickPanelBaseWidth = 431f;

        public const float HandPickPanelHeight = 556f;

        /// <summary>
        /// 面板九宫格的左右边界。
        ///
        /// <para>取 110：原图内框描边在 x=107…314，而面板外框内缘约在 x=0…430 的
        /// 外描边之后（约 8）。110 落在「外框内缘」与「内框左缘」之间那片纯色躯干里，
        /// 拉伸它不会碰到任何装饰。右侧对称取到 431 − 314 − 7 ≈ 110。</para>
        ///
        /// <para>⚠ 这三个地方必须一致：本组常量、编辑器侧 <c>EnsureHandPickImporters</c>、
        /// 以及 <c>UiTheme.HandPickPanelTorso</c>（盖住被拉宽的内框装饰用的躯干色）。</para>
        /// </summary>
        public const float HandPickPanelBorderLeft = 110f;

        public const float HandPickPanelBorderRight = 110f;

        /// <summary>
        /// 面板九宫格的上下边界：保住顶部牌位 + 两侧翼（0…99）与底部收边（521…555）。
        /// </summary>
        public const float HandPickPanelBorderTop = 100f;

        public const float HandPickPanelBorderBottom = 36f;

        /// <summary>
        /// 标题文字框：距面板顶边的距离与高度。
        ///
        /// <para>量自原图（`Peek_Frame.png`），<b>2026-09-28 重新量的</b>：顶部 title band
        /// 的亮边在第 44…47 行、下亮边在第 105…108 行，中间那块可以放字的暗色内槽是
        /// <b>48…103</b>（高 56，中心 ≈ 75.5）。于是 title box 顶边取
        /// `53.5`（中心 = 53.5 + 46/2 = 76.5，字墨迹中心 ≈ 75.5）。</para>
        ///
        /// <para>⚠ <b>原来是 35（中心 58）—— 那是「标题 + 副标题」两行时代的取值</b>：
        /// 原先标题下面还有一条「已选 N / M」的计数（中心 ≈ 105），两行合起来才在 band 里居中。
        /// 2026-09-27 用户要求删掉那条计数之后，标题<b>没有跟着重新居中</b>，
        /// 于是整整偏上约 18 px —— 文字顶到 band 上缘、下面空一大片
        /// （用户 2026-09-28 的截图：标题「移出·获加速」看着「没放进框里、偏上了」）。
        /// 单行标题现在要自己占满这块内槽。</para>
        /// </summary>
        public const float HandPickTitleTop = 53.5f;

        public const float HandPickTitleHeight = 46f;

        // ⚠ 2026-09-27：原先这里有两条副标题常量（`HandPickSubTitleGap = 8` /
        //   `HandPickSubTitleHeight = 32`）—— 供标题正下方那条「已选 N / M」的计数用。
        //   用户口径「标题底下会有一个当前选择牌数和当前手牌总数的数字显示，不需要显示这个」
        //   → 计数连同节点、字号常量一起删掉（节点由 `BattleUiBuilder.BuildHandPickLayer` 建）。
        //   删掉它们**不影响任何布局**：那条计数是压在标题牌位下方的装饰，
        //   中间的「已选卡框」区一直按 <see cref="HandPickSlotAreaCenterY"/> 定位。

        /// <summary>
        /// 标题文字框宽度 —— <b>= 原图 title band 的内槽宽</b>（不是「面板减去内边距」）。
        ///
        /// <para>M30 起标题改成「效果名」而不是卡名，最长的一条是
        /// 「送入冷却增加力量（可选择多张）」= 15 字，30 号字量出来约 450 px。
        /// 一开始把框开到 470「怕被截成省略号」—— 那是错的：470 &gt; 431（单张态面板宽），
        /// 于是标题框左右各捅出面板 20 px，在美术图上就是**文字横着压在面板外框上**
        /// （2026-09-22 截图 `m30_confirm_empty.png` 一眼可见）。</para>
        ///
        /// <para>改 379（面板 − 2×26）之后仍然偏宽：那只保证「文字不出面板」，
        /// 但 title band 是一条**两端带翼的横条**（见目标图
        /// `ae67a6sgshbf-…png`），文字压在两端的翼上看着同样脏。
        /// 逐行量原图 y=58 那一行的暗色内槽：x ≈ 83…340 → <b>宽 ≈ 257</b>、中心 211.5
        /// （面板中心 215.5，基本居中）。所以这里取 257 —— 文字只花在平直那段里。</para>
        ///
        /// <para>代价是 15 字要缩到 19 号左右（<see cref="FontSizeHandPickTitleMin"/> 正好兜住）。
        /// 缩字号只是小一点，溢出却是「压到描边上」——前者可接受，后者不能。</para>
        /// </summary>
        public const float HandPickTitleWidth = 257f;

        /// <summary>标题字号（含「（可选择多张）」的后缀时靠自动缩容压回框内，所以基数不用调小）。</summary>
        public const float FontSizeHandPickTitleBase = 30f;

        /// <summary>标题自动缩容的下限 —— 再小就糊了，宁可让框宽一点。</summary>
        public const float FontSizeHandPickTitleMin = 19f;

        /// <summary>
        /// 中间「已选卡框」区的中心距面板<strong>顶边</strong>的距离。
        ///
        /// <para>量自原图：内框描边的上边界 ≈ 第 150 行、下边界 ≈ 第 430 行 →
        /// 中心 ≈ 290。半高 = (430 − 150) / 2 = 140。</para>
        /// </summary>
        public const float HandPickSlotAreaCenterY = 290f;

        /// <summary>中间卡框区的高度（= 原图内框高 280）。</summary>
        public const float HandPickSlotAreaHeight = 280f;

        /// <summary>
        /// 单张卡框的尺寸（= 原图内框 207×280 —— 恰好是一张卡的比例）。
        ///
        /// <para>它是**单张时**的框宽；多选时框宽仍是它，但整条卡槽区
        /// （<see cref="HandPickSlotAreaWidthFor"/>）会按张数变宽。</para>
        /// </summary>
        public const float HandPickSlotWidth = 207f;

        public const float HandPickSlotHeight = 280f;

        /// <summary>已选卡之间的间距（多选时卡框并排，间距要小 —— 卡框本身已有描边）。</summary>
        public const float HandPickSlotGap = 8f;

        /// <summary>
        /// 卡槽区左右各留的内边距 —— 它**不是**「贴着面板外框」的余量，
        /// 而是「贴着原图内框（装卡那圈亮描边）」的余量。
        ///
        /// <para>原图内框的左右边界量得 x ≈ 116…312（宽 ≈ 196），而面板九宫格的
        /// 左右 border 是 110 → 拉伸区从左 110 起。于是内框距拉伸区左沿 6 px、
        /// 距右沿 9 px。这里取 12 稍宽一点，让牌不至于压在内框描边上。</para>
        /// </summary>
        public const float HandPickSlotAreaPadding = 12f;

        /// <summary>
        /// 原图内框左右各距「九宫格拉伸区边界」多少 px（左 6 / 右 9，取大者）。
        ///
        /// <para><b>为什么需要这个数</b>：面板横向拉伸时，内框是**烘焙在中间拉伸区里的**，
        /// 它不像左右 border 那样原地不动 —— 但也不会按比例放大。
        /// 真实的内框宽 = <c>面板宽 − 110 − 110 − 2×本值</c>。
        /// 牌排得太宽就会伸出内框、压到面板躯干上（2026-09-22 的 3 张态截图正是如此）。</para>
        /// </summary>
        public const float HandPickInnerFrameInset = 12f;

        /// <summary>
        /// 已选卡框区的最大宽度 = <b>手牌上限那一档的内容宽</b>。
        ///
        /// <para>⚠ 这里**不能**用「画布宽 − 两侧留白」那种屏幕级上限（原来是 1800）：
        /// 那条上限比面板能长的宽度还大，等于没有约束 —— 卡槽区一旦比面板内框宽，
        /// 牌就会横着捅出内框、压到面板躯干上（2026-09-22 的 3 张态截图正是如此）。</para>
        ///
        /// <para>= 8 张（<c>BattleState.HandLimit</c>）× 207 + 7×8 + 2×12 = <b>1744</b>。
        /// 这个值同时是 <see cref="HandPickPanelMaxWidthFor"/> 的基准 ——
        /// 两者都用**常量**表达，避免「卡槽区上限 ↔ 面板上限」互相引用成死循环
        /// （属性取值器在静态初始化顺序上是不可靠的）。</para>
        /// </summary>
        public const float HandPickSlotAreaMaxWidth = HandPickSlotRowMaxItems * 207f
            + (HandPickSlotRowMaxItems - 1) * 8f + 2f * 12f;

        /// <summary>一行最多摆几张（= 手牌上限 8）。超过就溢出面板，不做换行。</summary>
        public const int HandPickSlotRowMaxItems = 8;

        /// <summary>空卡框的描边粗细（没选牌时，卡框是一圈虚线占位）。</summary>
        public const float HandPickEmptyOutline = 3f;

        /// <summary>
        /// 底部确认键的按钮座：距面板<strong>底边</strong>的距离与高度。
        ///
        /// <para><b>2026-09-22 对齐原图按钮座</b>（修「双下巴」）。确认键底图换成素材
        /// （<c>HandPick_Confirm.png</c>）之后，它与 <c>Peek_Frame.png</c> **自带的**
        /// 底部按钮座叠在一起 —— 原图那条按钮座还在画面里，我的按钮只要不完全盖住它，
        /// 就会露出第二个轮廓，看着像两张脸叠一起。</para>
        ///
        /// <para>逐行量原图按钮座的内槽（在 431×556 的原图里）：
        /// PNG y ≈ 469…526、x ≈ 85…341。换算到 Unity 坐标（原点在面板中心、y 向上）：
        /// <c>y = 556−1−526 … 556−1−469 = 29…86</c>（高 <b>58</b>、中心 <b>57.5</b>）、
        /// <c>x = 85…341 − 215.5 = −130.5…125.5</c>（宽 <b>257</b>、中心 −2.5 ≈ 居中）。</para>
        ///
        /// <para>所以按钮就是 <b>310 → 258 宽、76 → 58 高、中心 51 → 57.5</b>
        /// —— 尺寸与原图内槽一致，正好盖住它、不再露出第二圈。
        /// ⚠ 别再「凭手感」给尺寸：这个值必须来自量图，差 10 px 就是一条明显的重影。</para>
        /// </summary>
        public const float HandPickConfirmCenterFromBottom = 57.5f;

        public const float HandPickConfirmHeight = 58f;

        /// <summary>确认键宽度（= 原图按钮座内槽宽 257，取整 258）。</summary>
        public const float HandPickConfirmWidth = 258f;

        /// <summary>
        /// 确认键中心相对 <c>HandPickLayer</c> 根（= <b>屏幕中心</b>）的纵向偏移。
        ///
        /// <para><b>为什么单独列一条、而不是在建节点时现算</b> —— 这是 M31 真实踩过的符号坑：
        /// M31 把确认键的父节点从「面板」（尺寸会被按张数改宽）换成了「满屏根」，
        /// 两者原点其实都是屏幕中心，但 <see cref="HandPickConfirmCenterFromBottom"/> 的口径是
        /// 「离<b>面板底边</b>多远」，换算成「离屏幕中心多远」时<b>必须取负</b>
        /// —— 面板底边在屏幕中心<b>下方</b> <c>HandPickPanelHeight/2</c> 处。</para>
        ///
        /// <para>写成正号的现象：按钮镜像到面板<b>顶部</b>、压在标题牌位与「1/3」计数上，
        /// 而底面那块原图自带的按钮座空着 —— <b>Unity 零报错</b>，只能靠肉眼/截图发现
        /// （用户 2026-09-23 截图报的正是这个）。所以这行换算只准写在这里，别再当场乘加减。</para>
        /// </summary>
        public static float HandPickConfirmCenterYFromCenter()
        {
            return HandPickConfirmCenterFromBottom - HandPickPanelHeight * 0.5f;
        }

        // ── M30 · 确认键底图（`HandPick_Confirm.png`）────────────
        //
        // 用户口径（2026-09-22）：确认键不要用「自己摆的一个灰蓝色方块」，要用素材里给的那条按钮条。
        // 素材 = `Assets/Art/Ui/HandPick_Confirm.png`（626×174），由 `Tools/art-audit/slice_handpick_kit.py`
        // 从 `Art/148cae8c-….png` 底部那条切出。两端是「六边形 + 斜角 + 外侧辉光」造型，
        // 直接按尺寸缩放会把两端压扁，所以走九宫格横向拉伸：左右各 96 当 border 保护两端造型，
        // 中间那段纯色躯干负责横拉。
        //
        // ⚠ 纵向 border 已经改到 26（见 HandPickConfirmBorderY）：素材 626×174 里上下
        //   各有一条很薄的亮描边，26 刚好把它们护住又不至于把按钮压成细缝。
        //
        // ⚠ **2026-09-23 起按钮不再画这条素材了**（用户口径：「移除掉这个确认键的背景，
        //   就用按钮座那个背景」）。它压在原图内槽上时纵向被 174 → 58 硬压，观感是一条
        //   发白的扁条，与底面那块按钮座对不上。现在按钮只留文字与命中区、
        //   背景交给 `Peek_Frame.png` 自带的按钮座。这三个常量仍然留着并继续做导入参数校验
        //   （`EnsurePeekImporters`）—— 素材没删，将来要用不必再回头收拾 border。
        //   详见 `Docs/implementation/30-…`。

        /// <summary>确认键底图路径（构建器 + 导入参数校验都用它）。</summary>
        public const string HandPickConfirmSpritePath = "Assets/Art/Ui/HandPick_Confirm.png";

        /// <summary>
        /// 确认键底图九宫格的左右 border（= 素材两端的造型宽度）。
        ///
        /// <para>⚠ 只存在 .meta 里、重导就悄悄丢，丢了是圆角/斜角被抻成椭圆而 <b>Unity 零警告</b>
        /// —— 所以构建流程里固定校验（编辑器侧 <c>EnsureHandPickImporters</c>）。</para>
        /// </summary>
        public const float HandPickConfirmBorderX = 96f;

        /// <summary>
        /// 确认键底图九宫格的上下 border。
        ///
        /// <para><b>必须小于按钮高度</b>：九宫格里上下 border 之和一旦 ≥ 目标高度，
        /// 中间那段可拉伸区就没了 —— Unity 会把整张图硬压，两端造型直接被挤扁。
        /// 素材高 174、按钮高 76，所以这里取 26 + 26 = 52 &lt; 76，中间留 24 px 拉伸余量；
        /// 纵向本来也不该拉伸，这点余量只是让整图能被匀压缩到 76。</para>
        /// </summary>
        public const float HandPickConfirmBorderY = 26f;

        /// <summary>
        /// 手牌飞入卡框的时长（玩家点一张手牌 → 它在卡框里落位）。
        /// 0.26 s、OutCubic：比淡入慢一点，让人看清「哪张牌飞过去了」。
        /// </summary>
        public const float HandPickFlySeconds = 0.26f;

        /// <summary>整块浮层淡入 / 淡出的时长（与 M25 同口径）。</summary>
        public const float HandPickFadeSeconds = 0.18f;

        /// <summary>面板出现时从多小弹到 1（弹一下）。</summary>
        public const float HandPickPanelPopFrom = 0.94f;

        public const float HandPickPanelPopSeconds = 0.24f;

        /// <summary>取走一张卡时的缩小回弹时长。</summary>
        public const float HandPickRemoveSeconds = 0.16f;

        /// <summary>新卡落进卡框时的放大回弹时长。</summary>
        public const float HandPickAddSeconds = 0.2f;

        /// <summary>
        /// 面板宽度随「中间卡槽区」的宽度走：`卡槽区宽 + 2×` 内框到<b>内框沿</b>的距离。
        ///
        /// <para><b>原图实测</b>：九宫格左右 border = 110，内框左右边界 = 116 / 312。
        /// 单张态面板 431、卡槽区 207 → 每侧余量 = <b>112</b>。
        /// 但内框本身的左右沿距面板外沿是 116 / 431−312 = 119 ——
        /// 这两个数不一样，是因为卡槽区（放牌的那条）比内框**还窄一点**：
        /// 牌不该贴着内框的亮描边。差值 119 − 112 = 7 就是那圈留白。</para>
        ///
        /// <para>所以「面板 = 卡槽区 + 224」这条口径天然保证了牌在内框里
        /// —— 只要 112 × 2 = 224 ≤ 内框两侧总留白 238。**不要再改这个 112**：
        /// 改小了牌会伸出内框压到面板躯干上（2026-09-22 的 3 张态截图）。</para>
        /// </summary>
        public static float HandPickPanelWidthFor(int count)
        {
            float w = SlotAreaWidthFor(count) + 2f * HandPickPanelSideMargin;
            float max = HandPickPanelMaxWidthFor();
            return w > max ? max : w;
        }

        /// <summary>
        /// 卡槽区两侧各留多少才到面板外沿（实测 112）—— <b>面板与卡槽区之间的唯一口径</b>。
        /// </summary>
        public const float HandPickPanelSideMargin = 112f;

        /// <summary>
        /// 面板能到的最宽值 —— 由卡槽区上限推出（卡槽区上限 + 两侧各 112）。
        ///
        /// <para>为什么要有这条：面板与卡槽区必须**同进同退**。面板宽度没有上限时，
        /// 卡槽区封了顶而面板还在长，两者就脱钩了（牌挤在中间、面板左右空出一大块）。
        /// 两边都以 <see cref="HandPickSlotAreaMaxWidth"/> 为唯一基准，口径才一致。</para>
        /// </summary>
        public static float HandPickPanelMaxWidthFor()
        {
            return HandPickSlotAreaMaxWidth + 2f * HandPickPanelSideMargin;
        }

        /// <summary>
        /// 中间卡槽区（装已选牌的那一排）在给定了「选了几张」之后应该有多宽。
        ///
        /// <para>0 张时 = 单张框宽（画面上一块空卡框，与面板严格对齐）；
        /// 多张时按张数往外扩，并以 <see cref="HandPickSlotAreaMaxWidth"/> 封顶。</para>
        /// </summary>
        public static float SlotAreaWidthFor(int count)
        {
            if (count <= 1)
            {
                return HandPickSlotWidth;
            }

            float w = count * HandPickSlotWidth + (count - 1) * HandPickSlotGap
                      + 2f * HandPickSlotAreaPadding;
            return w > HandPickSlotAreaMaxWidth ? HandPickSlotAreaMaxWidth : w;
        }

        // ── 字号 ─────────────────────────────────────────────

        /// <summary>标题字号。</summary>
        public const float FontSizeHandPickTitle = 30f;

        /// <summary>
        /// 确认键文字字号。
        ///
        /// <para>2026-09-23 起确认键自己不再画底图（露出的是 <c>Peek_Frame.png</c> 自带的
        /// 底部按钮座），两字「确认」要落在那个内槽里不顶上下描边，所以字号保持 26。</para>
        /// </summary>
        public const float FontSizeHandPickConfirm = 26f;

        // ⚠ 2026-09-27：原先这里有一条 `FontSizeHandPickSubTitle = 22`（「已选 0 / 3」那行计数）。
        //   计数整个移除，字号常量跟着删 —— 见 `HandPickTitleTop` 那段后的说明。

        /// <summary>确认键不可用时文字的颜色（暗下去；按钮底图的 disabled 色另有其物）。</summary>
        public const float HandPickDisabledDim = 0.45f;

        // ══════════════════════════════════════════════════════
        //  M28 · 查看怪物手牌（按住怪物右下角的手牌数标签）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-22）：怪物右下角那个「手牌 ×N」标签，**按住**就弹出
        //  一块面板，把怪物当前手牌全摆出来 —— 玩家已知的（打出过 / 用雷云·狂躁蘑菇
        //  看过）正面朝上，未知的显示牌背；**松手即消失**。
        //
        //  版式沿用 M25 `Peek_Panel.png`（992 宽、4 张一行、一行放不下就往纵向长）——
        //  两张面板的「牌位尺寸 / 间距 / 留白」逐位相同，唯一差别是本块面板由
        //  **按住**驱动、且牌位会显示真牌面。
        //
        //  ⚠ 这里**不另立一套尺寸常量**，直接复用 <see cref="PeekCardWidth"/> 那一组：
        //  两份互相独立的「牌宽 186」早晚会漂移，而它们必须一样（同一张牌背图）。

        /// <summary>本块面板的宽度 —— 与 <see cref="PeekPanelWidth"/> 同一张素材、同一条理由（横向不拉伸）。</summary>
        public const float MonsterHandPanelWidth = PeekPanelWidth;

        /// <summary>一行时面板的高度。</summary>
        public const float MonsterHandPanelHeightOneRow = PeekPanelHeightOneRow;

        /// <summary>两行时面板的高度（纵向九宫格拉中间那条平躯干，肉眼零差别）。</summary>
        public const float MonsterHandPanelHeightTwoRows = PeekPanelHeightTwoRows;

        /// <summary>标题文字框（内容「查看对方手牌」，与 M25 同位）。</summary>
        public const float MonsterHandTitleTop = PeekTitleTop;

        public const float MonsterHandTitleHeight = PeekTitleHeight;

        public const float MonsterHandTitleWidth = PeekTitleWidth;

        /// <summary>副标题（「已知 2 / 5」）—— 让玩家一眼看出这份情报有多少是真的。</summary>
        public const float MonsterHandSubTop = PeekTitleTop + PeekTitleHeight - 4f;

        public const float MonsterHandSubHeight = 30f;

        /// <summary>已知 / 未知两类牌位共用的尺寸与间距（与 M25 逐位相同）。</summary>
        public const float MonsterHandCardWidth = PeekCardWidth;

        public const float MonsterHandCardHeight = PeekCardHeight;

        public const float MonsterHandCardGap = PeekCardGap;

        public const float MonsterHandRowGap = PeekRowGap;

        /// <summary>一行最多几张（= M25 同一笔账）。</summary>
        public const int MonsterHandMaxPerRow = PeekMaxPerRow;

        /// <summary>
        /// 预建的牌位总数。
        ///
        /// <para>比 <see cref="PeekSlotCount"/> 多留 4 格：查看手牌只面对「对手当时的手牌」，
        /// 而这里是「对手手牌上限」的展示位 —— 手牌补满时可能到 8 张，取 8 已经够；
        /// 但本工程的手牌上限由 <c>BattleState</c> 决定，宁可多建几格也不要在
        /// 运行时才发现摆不下（多出来的格子永远失活，成本几乎为零）。</para>
        /// </summary>
        public const int MonsterHandSlotCount = PeekSlotCount;

        /// <summary>整块浮层淡入 / 淡出的时长（按住型面板要更快 —— 它跟手感）。</summary>
        public const float MonsterHandFadeSeconds = 0.12f;

        /// <summary>面板出现时面板的起始缩放（弹一下）。</summary>
        public const float MonsterHandPanelPopFrom = PeekPanelPopFrom;

        public const float MonsterHandPanelPopSeconds = PeekPanelPopSeconds;

        /// <summary>牌位上浮进场的时长（每格错开一点，做出「依次翻开」的感觉）。</summary>
        public const float MonsterHandCardPopSeconds = 0.16f;

        /// <summary>牌位之间的进场延迟（× 序号）。</summary>
        public const float MonsterHandCardPopStagger = 0.035f;

        /// <summary>「未知」牌位上面的暗压（未知牌整张压暗，读作「这是牌背、没有信息」）。</summary>
        public const float MonsterHandUnknownDim = 0.7f;

        /// <summary>标题字号。</summary>
        public const float FontSizeMonsterHandTitle = FontSizePeekTitle;

        /// <summary>副标题字号。</summary>
        public const float FontSizeMonsterHandSub = 22f;

        // ══════════════════════════════════════════════════════
        //  商店场景（2026-09-26 · P4 的第一个事件切片）
        // ══════════════════════════════════════════════════════
        //
        //  版式来自用户给的 `Art/ReferenceShop.png`（商店内景）与
        //  `Art/d30f41c7-….png`（同一套商店 UI：木质价格牌 + 「离开」按钮）。
        //
        //  与战斗场景的关系：**场景独立、版式复用**。
        //  背景换成商店内景图，顶栏仍用战斗那套 `Hud_Bar`（口径：💰 位改显示金币），
        //  卡面仍用**唯一那份** `CardView_Hand.prefab`（红线 9）。
        //
        //  ⚠ 本段所有坐标都是「画布绝对坐标」，锚点用**左下角**（同 Char* 那批），
        //    别跟战斗里 `(0f,1f)` 那批左上角口径的混用（铁律 10）。

        /// <summary>商店货位数量（3 原价 + 1 特价；口径见 <c>Docs/design/冒险事件架构.md</c> §5.1）。</summary>
        public const int ShopSlotCount = 4;

        /// <summary>
        /// 货位卡面宽度 = <see cref="HandCardWidth"/> × 1.1。
        ///
        /// <para><b>⚠ 2026-09-27 由「= HandCardWidth」改成独立的 1.1 倍</b>（用户要求
        /// 「出售的卡牌适度放开一点点」）：货架上的卡比手牌大一档。
        /// <b>背包里的卡不受影响</b> —— 那是 <see cref="ShopBagCardFaceWidth"/>，仍 = HandCardWidth。</para>
        /// </summary>
        public const float ShopCardWidth = HandCardWidth * 1.1f;

        /// <summary>货位卡面高度（= <see cref="HandCardHeight"/> × 1.1，与宽度同比例）。</summary>
        public const float ShopCardHeight = HandCardHeight * 1.1f;

        /// <summary>
        /// 相邻货位的中心间距（<b>步进</b>，= <b>手牌卡宽</b> + 34 的缝）。
        ///
        /// <para>缝为什么是 34：柜台台面在画布上占 x 275…1525，但右侧 1185…1350 那一段被
        /// 店主伸出的手占着（他在「展示货品」）。留给货架的干净台面就是 275…1185 = 910 宽。
        /// 4 张 196 的卡要全塞进 910：196 + 3 × 步进 ≤ 900 → 步进 ≤ 234。
        /// 取 <b>230</b>（缝 34）→ 整排 886 宽、两侧各余 12，是最宽的合规解。</para>
        ///
        /// <para><b>⚠ 2026-09-27：基准是 <see cref="HandCardWidth"/>，不是 <see cref="ShopCardWidth"/></b>
        /// —— 卡面放大 1.1 倍之后若步进跟着长，整排会一口气宽出近 9%、把最右一张推去压店主的手。
        /// 放大只作用于卡面本身：卡视觉宽 215.6 仍 &lt; 230，卡与卡的缝从 34 收到 14.4，依旧不重叠。</para>
        /// </summary>
        public const float ShopSlotSpacing = HandCardWidth + 34f;

        /// <summary>
        /// 一个货位的<b>命中区</b>（透明的整格矩形，货位里唯一吃射线的东西）。
        ///
        /// <para><b>⚠ 它必须一直包到价格牌底边</b>（2026-09-27 修）：价格牌挂在卡面<b>下方</b>
        /// （<see cref="ShopPricePlateDrop"/> = 202.9 &gt; 半个卡高 149.6），所以「一张卡那么大」的
        /// 命中区<b>不含价格牌</b> —— 点价格牌（那块木质六边形，玩家读作「购买」）什么都不会发生，
        /// 而且零报错。用户的原话是「无论点击购买键还是点击卡牌都能买」，这一条就是它的实现。</para>
        /// </summary>
        public const float ShopSlotHitWidth = ShopCardWidth;

        public const float ShopSlotHitHeight = ShopCardHeight * 0.5f
                                               + ShopPricePlateDrop + ShopPricePlateHeight * 0.5f;

        public const float ShopSlotHitCenterY = (ShopCardHeight * 0.5f
                                                 - (ShopPricePlateDrop + ShopPricePlateHeight * 0.5f)) * 0.5f;

        /// <summary>
        /// 4 个货位的整排中心 X（画布绝对坐标）。
        ///
        /// <para><b>不是画布中心，也不是台面中心</b>：台面 275…1525 的中心是 900，但台面右侧
        /// 被店主的手占着（见 <see cref="ShopSlotSpacing"/>），货架只能占 275…1185 这一段。
        /// 最初按这个口径取 <b>730</b>（整排落在 287…1173）。</para>
        ///
        /// <para><b>⚠ 2026-09-27 改成 976</b>：用户在编辑器里把场景中的 <c>Shelf</c> 节点
        /// 右移了 246（<c>anchoredPosition.x</c> 由 −230 改成 <b>16</b> = 画布 976）。
        /// <b>这是用户手调的位置，不要再改回去</b> —— 把它同步进常量，只是让
        /// 「万一重跑构建器」时落点仍然一致，而不是重新裁决它该在哪。</para>
        /// </summary>
        public const float ShopShelfCenterX = 976f;

        /// <summary>
        /// 货位卡面中心 Y（画布绝对坐标，从底边算）。
        ///
        /// <para>版式账（对着背景量，换算用的是 <see cref="ShopBgCoverSize"/> 的 <b>1.16379 放大</b>
        /// —— 不是缩小，一开始按缩小算过一版，整排位置全错）：
        /// 柜台台面在画布上占 y 410…475（上沿 = 柜面后棱线 475，下沿 = 齐柜面前沿 410）。
        /// 卡高 272 时卡底取 472 → 中心 = 472 + 136 = <b>608</b>；
        /// 卡顶 = 744，仍远低于顶栏底边（1080 − 14 − 95 = 971）与标题（964…1040）。</para>
        ///
        /// <para><b>⚠ 2026-09-27：卡高变 299.2 后这个值<b>故意保持 608</b></b>：
        /// 放大以卡心为基准 → 卡底由 472 落到 458.4、卡顶升到 757.6（仍在标题之下）。
        /// <b>不动 <c>Shelf</c> 的 y</b> —— 那个节点用户已经手调过，动它的 y 等于动用户的节点。
        /// 价格牌的下移由 <see cref="ShopPricePlateDrop"/> 的表达式自动补偿，不靠这里。</para>
        /// </summary>
        public const float ShopCardCenterY = 608f;

        /// <summary>价格牌宽度（= 切出来的 <c>Shop_PricePlate.png</c> 原始宽 × 显示缩放 × 0.9）。</summary>
        public const float ShopPricePlateWidth = 212.4f;

        /// <summary>价格牌高度（同口径 × 0.9；2026-09-27 用户要求「购买键缩小 10%」）。</summary>
        public const float ShopPricePlateHeight = 84.6f;

        /// <summary>
        /// 价格牌中心相对卡面中心的下移量（价格牌挂在卡面正下方）。
        ///
        /// <para>= 半张卡 + 半个牌 + 间距 11。间距取 11 而不是更宽，
        /// 是为了让价格牌还能压在台面棱线（画布 y ≈ 482）附近而不是滑到柜台下面去。</para>
        ///
        /// <para><b>⚠ 2026-09-27 由写死的 194 改成表达式</b>：卡高与价格牌高在这一天同时变了
        /// （卡 ×1.1、牌 ×0.9），写死的数字必然与它们脱钩 —— 症状是价格牌<b>被卡面盖住一角</b>，
        /// 而且零报错。改成表达式之后，以后再调卡高 / 牌高都不会再对不上。</para>
        /// </summary>
        public const float ShopPricePlateDrop = ShopCardHeight * 0.5f + ShopPricePlateHeight * 0.5f + 11f;

        /// <summary>价格数字的文本框尺寸（压在金币右侧，位置由原图量出；同牌面 × 0.9）。</summary>
        public const float ShopPriceTextWidth = 86.4f;

        /// <summary>价格数字文本框高度。</summary>
        public const float ShopPriceTextHeight = 41.4f;

        /// <summary>
        /// 价格数字文本框相对价格牌中心的偏移。
        ///
        /// <para>版式账（原图）：牌 236×94，金币圆心约在中心左侧 −46 px、屏幕 y 中心附近；
        /// 原图数字在金币右侧、横向居中于牌面右半 → 偏移取 (+30, 0)。
        /// 2026-09-27 牌面缩到 0.9 倍，偏移同比例跟随（金币也随底图一起缩）→ (+27, 0.9)。</para>
        /// </summary>
        public const float ShopPriceTextOffsetX = 27f;

        public const float ShopPriceTextOffsetY = 0.9f;

        /// <summary>价格牌上金币的显示尺寸（切图里的原尺寸，不缩放）。</summary>
        public const float ShopPriceCoinSize = 54f;

        /// <summary>金币圆心相对价格牌中心的偏移（让 TMP 数字压在它右边）。</summary>
        public const float ShopPriceCoinOffsetX = -46f;

        public const float ShopPriceCoinOffsetY = 0f;

        /// <summary>「卖光了」空位上的文字颜色 / 字号。</summary>
        public const float FontSizeShopSoldOut = 30f;

        /// <summary>
        /// 「卖光了」底板相对卡面的内缩量（四周各缩这么多）。
        ///
        /// <para><b>底板为什么做成「整卡大小的暗色剪影」</b>：空槽背后是商店的木质内景
        /// （浅暖色 + 货架结构），灰字直接浮上去几乎读不清。垫一块与卡面同尺寸的压暗圆角牌之后，
        /// ① 文字立刻立起来；② 那一格读作「槽位还在、只是空了」，
        /// 而且与左右卡面同宽同高、整排节奏不塌。</para>
        ///
        /// <para>内缩 4 px 是为了让空槽比真卡**略小一圈** —— 一眼能看出「这里没有卡」，
        /// 而不是「这里有一张很暗的卡」。</para>
        /// </summary>
        public const float ShopSoldOutPlateInset = 4f;

        /// <summary>
        /// 「卖光了」底板的图路径（工程第三方白色几何图，可被 <c>Image.color</c> 上色）。
        ///
        /// <para>⚠ <b>按 <c>Image.Type.Simple</c> 用，不要用 Sliced</b>：这张
        /// `RoundedRectangle.png`（256×256，圆角半径 ≈26）的 <c>spriteBorder</c> 是 0，
        /// 而且它同时被战斗的光环图标（<c>BattleArtLayerBuilder.BuildAuraIcon</c>）以 Sliced 引用着 ——
        /// 去改它的 meta border 会把光环图标的圆角一起改掉。拿一个接近 1:1 的矩形去 Simple 拉伸，
        /// 圆角只会被压一点点，肉眼看不出来。</para>
        /// </summary>
        public const string ShopSoldOutPlateSpritePath = "Assets/ThirdParty/PolySprite/RoundedRectangle.png";

        /// <summary>
        /// 价格数字字号。
        ///
        /// <para><b>⚠ 2026-09-27：40 → 36（×0.9）</b>，与价格牌一起缩 —— 牌缩了字不缩，
        /// 数字会顶到六边形的斜边上。</para>
        /// </summary>
        public const float FontSizeShopPrice = 36f;

        /// <summary>商店标题字号。</summary>
        public const float FontSizeShopTitle = 52f;

        /// <summary>
        /// 商店标题中心（画布绝对坐标）—— 顶部居中，与顶栏同一条水平带。
        ///
        /// <para><b>不压背景上的元素</b>：背景图顶部那排蓝旗（背景图 y ≈ 20…170 一段彩色三角旗）
        /// 是画面里最抢眼的细节，标题压上去会读不清。而顶栏占的是画布 x 20…728、
        /// 画布中心 960 在它右边、两者不重叠，所以直接与顶栏同高最干净。
        /// y 取 1002 → 标签占 964…1040，与顶栏（971…1066）横向并排。</para>
        /// </summary>
        public const float ShopTitleCenterX = ReferenceWidth * 0.5f;

        public const float ShopTitleCenterY = 1002f;

        /// <summary>标题文本框尺寸。</summary>
        public const float ShopTitleWidth = 460f;

        public const float ShopTitleHeight = 76f;

        /// <summary>
        /// 「离开」按钮尺寸与位置。
        ///
        /// <para>右下角，与参考图里那颗木质六边形按钮同侧（垫在背景右侧的晶石箱一带）。
        /// 尺寸取切图等比缩到 236×86 —— 比价格牌略小，不至于跟货架抢注意力。</para>
        /// </summary>
        public const float ShopLeaveWidth = 236f;

        public const float ShopLeaveHeight = 86f;

        /// <summary>
        /// 「离开」按钮的右边距。
        ///
        /// <para><b>⚠ 2026-09-26 由 64 改成 176</b>：背包键（<see cref="ShopBagButtonRight"/>）
        /// 要占住右下角最右那一格（与参考图一致：离开在左、背包贴角），
        /// 所以离开必须往左让出「背包宽 + 缝」= <see cref="ShopBagButtonWidth"/> +
        /// <see cref="ShopBagButtonGap"/> = 112。
        /// 离开右缘因此从 1856 退到 1744，两键之间留 20 的缝。</para>
        /// </summary>
        public const float ShopLeaveRight = 64f + ShopBagButtonWidth + ShopBagButtonGap;

        public const float ShopLeaveBottom = 44f;

        /// <summary>「离开」按钮文字字号。</summary>
        public const float FontSizeShopLeave = 34f;

        // ── 主角（站在柜台左前方，2026-09-26）────────────────────────
        //
        //  参考图里主角在左下角、正面偏右站着看货架。我们的货架最左一张卡
        //  占 x 287…483（见 ShopShelfTotalWidth），所以主角**横向只能占 0…287**，
        //  否则会压到第一张卡上。取「身高 425（= 185×2.3）+ 脚底 y=24」之后
        //  头顶落在 449，正好压在卡底（472）之下 —— 主角因此读作
        //  「站在柜台前面」，而不是「浮在货架中间」。

        /// <summary>主角脚底中心的画布 X（底边口径，同 <c>Char*</c> 那批）。</summary>
        public const float ShopHeroX = 215f;

        /// <summary>主角脚底的画布 Y。留 24 是为了让斗篷下沿离屏幕底边有一点余量。</summary>
        public const float ShopHeroGroundY = 24f;

        /// <summary>
        /// 主角显示缩放 = 界面像素 / 素材画布像素。
        ///
        /// <para>待机素材画布 188×185（<c>BattleArtLibrary.Hero.Idle</c>），
        /// 战斗里用的是 1.55（≈287 高）。商店是<b>单场景立绘</b>，比战斗里大一号：
        /// 2.3 → 432×425.5。
        /// ⚠ 素材本身只有 185 像素高，2.3 倍已经是「平涂低多边形还能扛住」的上限，
        /// 再放大就明显发糊了 —— 要更大的主角必须换一张更大的素材。</para>
        /// </summary>
        public const float ShopHeroScale = 2.3f;

        // ── 背包键（右下角最右一格）─────────────────────────────────

        /// <summary>
        /// 背包键尺寸。
        ///
        /// <para>切图 <c>Shop_BagIcon.png</c> 是 144×135（木质六边形底 + 背包图形，
        /// 底板已经烘在图上）；显示 92×86 是把它缩到与「离开」同高，
        /// 两键并排时视觉重量相当。⚠ 92/86 = 1.0698 与素材的 1.0667 只差 0.3%，
        /// 所以直接用 <c>Image.Type.Simple</c> 铺，不做九宫格（它的 border 是 0）。</para>
        /// </summary>
        public const float ShopBagButtonWidth = 92f;

        public const float ShopBagButtonHeight = 86f;

        /// <summary>背包键的右边距 / 下边距（贴住右下角，比「离开」更靠角）。</summary>
        public const float ShopBagButtonRight = 64f;

        public const float ShopBagButtonBottom = 44f;

        /// <summary>背包键与「离开」之间的水平缝。</summary>
        public const float ShopBagButtonGap = 20f;

        // ── 背包面板（点背包键弹出的「主角卡池」）────────────────────

        /// <summary>
        /// 背包面板尺寸。
        ///
        /// <para>1620×880 在 1920×1080 上四边各留 ≥100。面板底图复用 M25 的
        /// <c>Peek_Panel</c>（992×447，<c>spriteBorder</c> = 左右 0 / 下 48 / 上 128）——
        /// 横向本来就靠拉伸（左右 border 是 0，中间是平整的深色面），
        /// 纵向 447→880 的拉伸被上下 border 护住了两端。</para>
        /// </summary>
        public const float ShopBagPanelWidth = 1620f;

        public const float ShopBagPanelHeight = 880f;

        /// <summary>网格列数。6 列 × 卡宽 196 在 1500 的内容宽里刚好排得开。</summary>
        public const int ShopBagColumns = 6;

        /// <summary>
        /// 网格单元格（比卡面略大，留出卡与卡之间的呼吸）。
        ///
        /// <para><b>⚠ 宽度是被算出来的，不能随便加</b>：滚动区 1500 − 滚动条 26 = 视口 1474，
        /// 再减网格左右 padding 各 14 → 可用 <b>1446</b>。
        /// 6 列 × 230 + 5 道缝 × 12 = <b>1440</b> ≤ 1446（只余 6）。
        /// 单元格一旦超过 231，第 6 列就会被 <c>RectMask2D</c> 裁掉一截 —— 而且零报错。</para>
        /// </summary>
        public const float ShopBagCellWidth = 230f;

        /// <summary>单元格高度 = 卡高 272 + 上下留白 20。</summary>
        public const float ShopBagCellHeight = 292f;

        /// <summary>背包里的卡面宽度 —— 与手牌 / 货位同一档 196（三处卡面统一，红线 9）。</summary>
        public const float ShopBagCardFaceWidth = HandCardWidth;

        public const float ShopBagGridSpacingX = 12f;

        public const float ShopBagGridSpacingY = 16f;

        /// <summary>网格左右内边距（算列宽时要减掉，见 <see cref="ShopBagCellWidth"/>）。</summary>
        public const float ShopBagGridPadding = 14f;

        // 面板内部的带子（坐标都相对**面板中心**，y 向上）：

        /// <summary>滚动区尺寸。高 620 → 顶边 +300、底边 −320。</summary>
        public const float ShopBagScrollWidth = 1500f;

        public const float ShopBagScrollHeight = 620f;

        public const float ShopBagScrollCenterY = -10f;

        /// <summary>视口右侧给滚动条让出的宽度。</summary>
        public const float ShopBagViewportRightInset = 26f;

        public const float ShopBagScrollbarWidth = 18f;

        /// <summary>标题带（面板顶边 −440 往上 70 起）。</summary>
        public const float ShopBagTitleCenterY = 370f;

        public const float ShopBagTitleWidth = 900f;

        public const float ShopBagTitleHeight = 60f;

        /// <summary>「N 张」计数带，压在标题下面。</summary>
        public const float ShopBagCountCenterY = 322f;

        public const float ShopBagCountWidth = 900f;

        public const float ShopBagCountHeight = 36f;

        /// <summary>「关闭」按钮（右下，与面板底边留 28）。</summary>
        public const float ShopBagCloseCenterY = -378f;

        public const float ShopBagCloseWidth = 240f;

        public const float ShopBagCloseHeight = 68f;

        public const float ShopBagCloseRightInset = 60f;

        /// <summary>「背包里还没有牌」那行提示（居中在滚动区上）。</summary>
        public const float ShopBagEmptyWidth = 900f;

        public const float ShopBagEmptyHeight = 60f;

        /// <summary>背包面板里那几行字的字号。</summary>
        public const float FontSizeShopBagTitle = 46f;

        public const float FontSizeShopBagCount = 26f;

        public const float FontSizeShopBagClose = 28f;

        public const float FontSizeShopBagEmpty = 30f;

        /// <summary>
        /// 背包键底图路径。
        ///
        /// <para>由 <c>Tools/art-audit/slice_shop_kit.py</c> 从商店套件源表切出（144×135），
        /// 2026-09-26 落版时切了但没用上，本次接进版式。</para>
        /// </summary>
        public const string ShopBagIconSpritePath = "Assets/Art/Ui/Shop_BagIcon.png";

        /// <summary>
        /// 价格牌九宫格 border（**精灵像素**）。
        ///
        /// <para>与 <see cref="HandPickConfirmBorderX"/> 同一类口径：牌面本身只做横向拉伸
        /// （四张牌等宽），所以纵向不拉伸 —— 给一个够大的 border 把六边形两端 + 上下描边钉住。
        /// 数值必须与切图实际尺寸相容，否则圆角/尖角会被拉变形，且**零报错**。</para>
        /// </summary>
        public const float ShopPlateBorderX = 96f;

        public const float ShopPlateBorderY = 24f;

        /// <summary>「离开」按钮九宫格 border（精灵像素）。</summary>
        public const float ShopLeaveBorderX = 96f;

        public const float ShopLeaveBorderY = 26f;

        // ── 图集路径（构建器与运行时都从这里取，别写第二处字面量）──────

        /// <summary>商店背景（内景，1664×928）。归档在 Backgrounds/（由 `ArtImportBuilder` 从 Art 根搬来）。</summary>
        public const string ShopBgSpritePath = "Assets/Art/Backgrounds/Bg_Shop.png";

        /// <summary>价格牌底图（已擦掉数字，金币保留）。</summary>
        public const string ShopPricePlateSpritePath = "Assets/Art/Ui/Shop_PricePlate.png";

        /// <summary>「离开」按钮底图。</summary>
        public const string ShopLeaveSpritePath = "Assets/Art/Ui/Shop_LeaveButton.png";

        /// <summary>商店背景原图尺寸（cover 铺法要按它算）。</summary>
        public const float ShopBgNativeWidth = 1664f;

        public const float ShopBgNativeHeight = 928f;

        /// <summary>
        /// 货位整排的总宽（= 第 1 张的左缘到第 4 张的右缘）。
        ///
        /// <para>⚠ <see cref="ShopSlotSpacing"/> 是<b>步进</b>，不是「卡与卡之间的间距」——
        /// 这里也只能用它乘 (张数 − 1) 再加一张卡宽。别写成
        /// <c>ShopCardWidth * ShopSlotCount + ShopSlotSpacing * (ShopSlotCount − 1)</c>
        /// （那是把步进当成了缝，会多算 3 个卡宽 → 整排跑到屏幕外、最左一张被切一半，
        /// 且**零报错**。2026-09-26 就是这么错过一次）。</para>
        /// </summary>
        public const float ShopShelfTotalWidth = ShopSlotSpacing * (ShopSlotCount - 1) + ShopCardWidth;

        /// <summary>
        /// 第 <paramref name="i"/> 个货位的中心 X（从左到右）。
        ///
        /// <para>⚠ 步进用 <see cref="ShopSlotSpacing"/>（不要再加一个卡宽，理由见
        /// <see cref="ShopShelfTotalWidth"/>）。</para>
        /// </summary>
        public static float ShopSlotCenterX(int i)
        {
            return ShopShelfCenterX - ShopShelfTotalWidth * 0.5f
                   + ShopCardWidth * 0.5f + ShopSlotSpacing * i;
        }

        // ── 商店背景的 cover 铺法与「背景 → 画布」坐标换算 ──────────────
        //
        //  ⚠ 商店的版式是**对着背景图量的**（柜台棱线、招牌位置都在那张图上），
        //    所以这里必须给出一个确定的 cover 公式。cover 的后果是图会被裁掉一部分，
        //    而「裁掉多少」直接决定图上的某个像素会落在画布的哪一点 ——
        //    不把这个偏移量算出来，所有「按背景量出来的坐标」都会整体错位。

        /// <summary>cover 铺法下，背景按比例放大后的显示尺寸（≥ 画布，多出来的被裁掉）。</summary>
        public static Vector2 ShopBgCoverSize()
        {
            float ratio = ShopBgNativeWidth / ShopBgNativeHeight;
            float need = ReferenceWidth / ReferenceHeight;
            return ratio > need
                ? new Vector2(ReferenceHeight * ratio, ReferenceHeight)
                : new Vector2(ReferenceWidth, ReferenceWidth / ratio);
        }

        /// <summary>
        /// 背景图左上角相对画布左上角的偏移。
        ///
        /// <para>背景是<b>居中铺</b>的，所以这个量恒为负（图比画布大，左上角跑到画布外面）。
        /// 用 <c>bgPixelX + X = 画布上的 X</c> 就能把「按图量的像素」翻译成画布坐标。</para>
        /// </summary>
        public static Vector2 ShopBgCoverOffset()
        {
            Vector2 size = ShopBgCoverSize();
            return new Vector2((ReferenceWidth - size.x) * 0.5f, (ReferenceHeight - size.y) * 0.5f);
        }

        /// <summary>
        /// 背景图上的像素点 → 画布坐标（左下角为原点，y 向上）。
        ///
        /// <para><b>⚠ 必须乘缩放</b>：cover 是「等比放大到盖住画布」，放大倍率 = 画布高 / 图高
        /// = 1080 / 928 = <b>1.16379</b>（> 1，是放大不是缩小）。忘了乘这一下，
        /// 「对着背景量的坐标」会系统性地缩在画面左上角，且不报任何错 —— 2026-09-26 首次落版就是这么错的。</para>
        /// </summary>
        public static Vector2 ShopBgPointToCanvas(float bgPixelX, float bgPixelY)
        {
            float scale = ShopBgCoverSize().y / ShopBgNativeHeight;
            Vector2 offset = ShopBgCoverOffset();
            return new Vector2(offset.x + bgPixelX * scale, ReferenceHeight - (bgPixelY * scale));
        }

        // ══════════════════════════════════════════════════════
        //  强化卡牌场景（2026-09-30 · P5）
        // ══════════════════════════════════════════════════════
        //
        //  版式来自用户给的 `Art/_Reference/Reference-4.png`（最终效果：左下站着主角、正中一张石台），
        //  背景 = 同一张图把主角抹掉之后的 `Art/Backgrounds/Bg_Upgrade.png`（1671×941）。
        //
        //  与商店场景的关系：**场景独立、版式复用**。弹窗照抄商店背包那一套
        //  （`Peek_Panel` 底图 + 6 列网格 + 卡面用唯一那份 `CardView_Hand.prefab`），
        //  所以面板内部那批坐标**直接复用 `ShopBag*` 常量**，不另抄一份同值的升级版
        //  —— 抄一份的下场是「改了一处、另一处留在旧版」，而且零报错。
        //
        //  ⚠ 本段坐标口径与商店一致：**画布绝对坐标 + 左下角原点**（同 Char* 那批），
        //    别跟战斗里 `(0f,1f)` 那批左上角口径混用（铁律 10）。

        /// <summary>强化场景背景（1671×941，无烘焙 UI，无主角）。</summary>
        public const string UpgradeBgSpritePath = "Assets/Art/Backgrounds/Bg_Upgrade.png";

        /// <summary>背景原图尺寸（cover 铺法要按它算）。</summary>
        public const float UpgradeBgNativeWidth = 1671f;

        public const float UpgradeBgNativeHeight = 941f;

        /// <summary>强化动画用的光爆素材（暖金色星芒，与场景蓝调形成对比）。</summary>
        public const string UpgradeFxGlowSpritePath = "Assets/Art/Fx/Fx_Explosion_Light.png";

        /// <summary>cover 铺法下背景放大后的显示尺寸（≥ 画布，多出来的被裁掉）。</summary>
        public static Vector2 UpgradeBgCoverSize()
        {
            float ratio = UpgradeBgNativeWidth / UpgradeBgNativeHeight;
            float need = ReferenceWidth / ReferenceHeight;
            return ratio > need
                ? new Vector2(ReferenceHeight * ratio, ReferenceHeight)
                : new Vector2(ReferenceWidth, ReferenceWidth / ratio);
        }

        /// <summary>背景图左上角相对画布左上角的偏移（居中铺 → 恒为负）。</summary>
        public static Vector2 UpgradeBgCoverOffset()
        {
            Vector2 size = UpgradeBgCoverSize();
            return new Vector2((ReferenceWidth - size.x) * 0.5f, (ReferenceHeight - size.y) * 0.5f);
        }

        /// <summary>背景图像素 → 画布坐标（左下角为原点，y 向上）。</summary>
        public static Vector2 UpgradeBgPointToCanvas(float bgPixelX, float bgPixelY)
        {
            float scale = UpgradeBgCoverSize().y / UpgradeBgNativeHeight;
            Vector2 offset = UpgradeBgCoverOffset();
            return new Vector2(offset.x + bgPixelX * scale, ReferenceHeight - (bgPixelY * scale));
        }

        // ── 石台命中区 ──────────────────────────────────────────
        //
        //  用户口径（2026-09-30）：**整张石台（含两侧木边）一块矩形命中区**，
        //  不含主角、不含漂浮石板与背景。
        //  下面四个数是**对着背景图量的像素**（左上为原点），换算走
        //  `UpgradeBgPointToCanvas` —— 背景是 cover 铺的（放大 1.149 倍），
        //  忘了乘缩放的话整块会缩在画面左上角，而且零报错（商店首次落版就这么错过一次）。

        public const float UpgradeTableBgLeft = 170f;
        public const float UpgradeTableBgTop = 292f;
        public const float UpgradeTableBgRight = 1525f;
        public const float UpgradeTableBgBottom = 698f;

        /// <summary>石台命中区的中心（画布坐标）。</summary>
        public static Vector2 UpgradeTableCenter()
        {
            Vector2 topLeft = UpgradeBgPointToCanvas(UpgradeTableBgLeft, UpgradeTableBgTop);
            Vector2 bottomRight = UpgradeBgPointToCanvas(UpgradeTableBgRight, UpgradeTableBgBottom);
            return new Vector2((topLeft.x + bottomRight.x) * 0.5f, (topLeft.y + bottomRight.y) * 0.5f);
        }

        /// <summary>石台命中区的尺寸（画布单位）。</summary>
        public static Vector2 UpgradeTableSize()
        {
            Vector2 topLeft = UpgradeBgPointToCanvas(UpgradeTableBgLeft, UpgradeTableBgTop);
            Vector2 bottomRight = UpgradeBgPointToCanvas(UpgradeTableBgRight, UpgradeTableBgBottom);
            return new Vector2(Mathf.Abs(bottomRight.x - topLeft.x), Mathf.Abs(bottomRight.y - topLeft.y));
        }

        // ── 主角（左下角，与 Reference-4 同一站位）──────────────────
        //
        //  ⚠ Reference-4 里那位巫师是一张**高分辨率插画**（占画面高约七成），
        //    我们的主角素材只有 188×185（`BattleArtLibrary.Hero.Idle`）——
        //    照参考图那个尺寸铺要放大到 ≈4.3 倍，平涂低多边形会明显发糊。
        //    所以这里取与商店场景**同一个 2.3 倍**（实测是该素材的画质上限），
        //    位置也照商店那套（左下、脚底留一点余量）：读起来仍是
        //    「主角站在台子左前方」，只是比参考图小一号。
        //    想要参考图那个体量，得先有一张更大的主角立绘。

        public const float UpgradeHeroX = 205f;

        public const float UpgradeHeroGroundY = 44f;

        public const float UpgradeHeroScale = 2.3f;

        // ── 标题 / 提示行 / 离开键 ────────────────────────────────

        /// <summary>场景标题「强化卡牌」（与商店标题同一条水平带）。</summary>
        public const float UpgradeTitleCenterY = 1002f;

        public const float UpgradeTitleWidth = 460f;

        public const float UpgradeTitleHeight = 76f;

        public const float FontSizeUpgradeTitle = 52f;

        /// <summary>
        /// 台面下方的引导文字（「点击台面 …」）。
        ///
        /// <para>没有它，玩家进场景只看到一张石台，不知道要点哪儿 ——
        /// 弹窗一打开这行就藏起来（见 <c>UpgradeView.SetHintVisible</c>）。</para>
        /// </summary>
        public const float UpgradeHintCenterY = 158f;

        public const float UpgradeHintWidth = 1300f;

        public const float UpgradeHintHeight = 56f;

        public const float FontSizeUpgradeHint = 34f;

        /// <summary>
        /// 「离开」按钮（右下角）。
        ///
        /// <para>⚠ 本场景<b>没有背包键</b>，所以右边距用 <see cref="ShopBagButtonRight"/> 原值 64，
        /// 而不是 <see cref="ShopLeaveRight"/>（那个是「给背包键让位」之后的 176）。</para>
        /// </summary>
        public const float UpgradeLeaveRight = 64f;

        public const float UpgradeLeaveBottom = 44f;

        // ── 强化动画（确认之后播的那一段，约 1.2 s）──────────────────
        //
        //  时间线：卡面轻缩（0.18）→ 光爆炸开（0.30）→ 卡面换成新卡（0.42）→
        //  力量数字从旧值滚到新值（0.36）。四段总 1.26 s，用户口径「约 1.2 秒」。
        //
        //  ⚠ 四段是**串行**的（每一段结束才起下一段），所以常量直接相加就是总时长；
        //    要调快调慢改这四个数即可，别在代码里另写一份时长。

        /// <summary>动画里那张卡面的宽度（比手牌大一号，是演出）。</summary>
        public const float UpgradeFxCardWidth = 320f;

        /// <summary>动画卡面的中心 Y（画布坐标）—— 压在石台上方。</summary>
        public const float UpgradeFxCardCenterY = 560f;

        /// <summary>光爆图案的显示尺寸。</summary>
        public const float UpgradeFxGlowSize = 820f;

        /// <summary>力量数字行（在卡面下方）的中心 Y 与尺寸。</summary>
        public const float UpgradeFxPowerCenterY = 248f;

        public const float UpgradeFxPowerWidth = 760f;

        public const float UpgradeFxPowerHeight = 170f;

        public const float FontSizeUpgradeFxPower = 128f;

        public const float UpgradeFxShrinkDuration = 0.18f;

        public const float UpgradeFxBurstDuration = 0.30f;

        public const float UpgradeFxRevealDuration = 0.42f;

        public const float UpgradeFxCountDuration = 0.36f;

        /// <summary>动画总时长（四段之和）。</summary>
        public const float UpgradeFxTotalDuration = UpgradeFxShrinkDuration + UpgradeFxBurstDuration
                                                   + UpgradeFxRevealDuration + UpgradeFxCountDuration;

        // ══════════════════════════════════════════════════════
        //  女巫的工坊 · 特殊强化场景（2026-10-01 · P6）
        // ══════════════════════════════════════════════════════
        //
        //  与 P5 强化场景（Upgrade.unity）是**两套并存的强化口径**：
        //    · P5 石台  = 免费给一张牌基础力量 +2（**不消耗任何牌**）；
        //    · 本场景    = **献祭一张牌**，换另一张牌的特殊强化（消耗 + 目标两个空位）。
        //
        //  版式同样「照商店背包那套」——面板底图、滚动网格、底部按钮直接取 `ShopBag*` 常量，
        //  不另抄一份同值常量（抄一份的下场是改了一处、另一处留在旧版，且零报错）。
        //  所以这一段只写**本场景独有**的那几样：背景、水晶球命中区、两个空位。
        //
        //  背景 = `Art/Backgrounds/Bg_WitchWorkshop.png`（1672×941，帐篷内景：
        //  女巫 + 中央水晶球 + 两侧货架），**没有任何烘焙 UI** ——
        //  场景里唯一可点的东西是那个水晶球（外加右下角的「离开」）。
        //
        //  ⚠ 坐标口径与商店 / 强化一致：**画布绝对坐标 + 左下角原点**（铁律 10），
        //    别跟战斗里 `(0f,1f)` 那批左上角口径混用。

        /// <summary>女巫工坊场景背景（1672×941，无烘焙 UI）。</summary>
        public const string WitchBgSpritePath = "Assets/Art/Backgrounds/Bg_WitchWorkshop.png";

        /// <summary>背景原图尺寸（cover 铺法要按它算）。</summary>
        public const float WitchBgNativeWidth = 1672f;

        public const float WitchBgNativeHeight = 941f;

        /// <summary>cover 铺法下背景放大后的显示尺寸（≥ 画布，多出来的被裁掉）。</summary>
        public static Vector2 WitchBgCoverSize()
        {
            float ratio = WitchBgNativeWidth / WitchBgNativeHeight;
            float need = ReferenceWidth / ReferenceHeight;
            return ratio > need
                ? new Vector2(ReferenceHeight * ratio, ReferenceHeight)
                : new Vector2(ReferenceWidth, ReferenceWidth / ratio);
        }

        /// <summary>背景图左上角相对画布左上角的偏移（居中铺 → 恒为负）。</summary>
        public static Vector2 WitchBgCoverOffset()
        {
            Vector2 size = WitchBgCoverSize();
            return new Vector2((ReferenceWidth - size.x) * 0.5f, (ReferenceHeight - size.y) * 0.5f);
        }

        /// <summary>
        /// 背景图像素 → 画布坐标（左下角为原点，y 向上）。
        ///
        /// <para><b>⚠ 必须乘缩放</b>：cover 放大倍率 = 1080 / 941 ≈ <b>1.14833</b>。
        /// 忘了乘这一下，「对着背景量的坐标」会系统性地缩在画面左上角，且不报任何错
        /// —— 商店首次落版就是这么错的。</para>
        /// </summary>
        public static Vector2 WitchBgPointToCanvas(float bgPixelX, float bgPixelY)
        {
            float scale = WitchBgCoverSize().y / WitchBgNativeHeight;
            Vector2 offset = WitchBgCoverOffset();
            return new Vector2(offset.x + bgPixelX * scale, ReferenceHeight - (bgPixelY * scale));
        }

        // ── 水晶球命中区 ────────────────────────────────────────
        //
        //  用户口径（2026-10-01）：**点水晶球**弹出特殊强化界面。
        //  下面四个数是**对着背景图量的像素**（左上为原点，1682×941 的图上）：
        //  球体轮廓约 x 660…795 / y 480…610，四周各留一点余量好点 ——
        //  多出来的那几个像素本来也是球周围的暗色桌面。

        public const float WitchOrbBgLeft = 645f;
        public const float WitchOrbBgTop = 470f;
        public const float WitchOrbBgRight = 800f;
        public const float WitchOrbBgBottom = 615f;

        /// <summary>水晶球命中区的中心（画布坐标）。</summary>
        public static Vector2 WitchOrbCenter()
        {
            Vector2 topLeft = WitchBgPointToCanvas(WitchOrbBgLeft, WitchOrbBgTop);
            Vector2 bottomRight = WitchBgPointToCanvas(WitchOrbBgRight, WitchOrbBgBottom);
            return new Vector2((topLeft.x + bottomRight.x) * 0.5f, (topLeft.y + bottomRight.y) * 0.5f);
        }

        /// <summary>水晶球命中区的尺寸（画布单位）。</summary>
        public static Vector2 WitchOrbSize()
        {
            Vector2 topLeft = WitchBgPointToCanvas(WitchOrbBgLeft, WitchOrbBgTop);
            Vector2 bottomRight = WitchBgPointToCanvas(WitchOrbBgRight, WitchOrbBgBottom);
            return new Vector2(Mathf.Abs(bottomRight.x - topLeft.x), Mathf.Abs(bottomRight.y - topLeft.y));
        }

        // ── 标题 / 引导行 / 离开键 ────────────────────────────────
        //
        //  与强化场景同一条水平带（两个场景的标题与离开键读起来应当是一套）。

        /// <summary>场景标题「女巫的工坊」。</summary>
        public const float WitchTitleCenterY = 1002f;

        public const float WitchTitleWidth = 620f;

        public const float WitchTitleHeight = 76f;

        public const float FontSizeWitchTitle = 52f;

        /// <summary>
        /// 水晶球下方的引导文字（「点击水晶球，进行一次特殊强化」）。
        ///
        /// <para>没有它玩家进场景只看到一张帐篷图，不知道要点哪儿；
        /// 浮层一打开这行就藏起来。</para>
        /// </summary>
        public const float WitchHintCenterY = 158f;

        public const float WitchHintWidth = 1300f;

        public const float WitchHintHeight = 56f;

        public const float FontSizeWitchHint = 34f;

        /// <summary>「离开」按钮（右下角；本场景没有背包键，所以右边距用 64 原值）。</summary>
        public const float WitchLeaveRight = 64f;

        public const float WitchLeaveBottom = 44f;

        // ── 浮层（两个空位那一层）────────────────────────────────
        //
        //  面板本身取 `ShopBagPanelWidth × ShopBagPanelHeight`（1620×880，与商店背包 /
        //  强化弹窗同一张 Peek_Panel）。下面这一组是**面板内部坐标**
        //  （锚点与轴心都是 Mid，原点 = 面板中心），纵向可用 y ∈ [−440, 440]。
        //
        //  纵向版式账（自上而下）：
        //    标题      y ∈ [ 340,  400]   高 60，面板顶 440 之下留 40
        //    两个空位  y ∈ [  24,  316]   高 292，标题底 340 之下留 24
        //    空位标签  y ∈ [ -30,   18]   高 48，挂在空位下缘再往下 30
        //    状态行    y ∈ [-188, -132]   高 56，标签底 −30 之下留 102
        //    底部按钮  y ∈ [-412, -344]   高 68，面板底 −440 之上留 28
        //
        //  横向：两个空位并排，中心 x = ±265（步进 530），中间净空 300 放「→」；
        //        状态行宽 980（±490），底部两个按钮各 240 宽、中心 x = ±630。

        /// <summary>空位（槽）的尺寸 —— 与商店背包网格的一格同尺寸，卡面才塞得进去。</summary>
        public const float WitchSlotWidth = ShopBagCellWidth;

        public const float WitchSlotHeight = ShopBagCellHeight;

        /// <summary>空位里那张卡面的宽度（与商店背包 / 强化弹窗同一口径）。</summary>
        public const float WitchSlotCardFaceWidth = ShopBagCardFaceWidth;

        /// <summary>两个空位中心的横向间距（步进）。</summary>
        public const float WitchSlotSpacing = 530f;

        /// <summary>两个空位中心的纵向位置。</summary>
        public const float WitchSlotCenterY = 170f;

        /// <summary>空位标签中心相对空位中心再往下多少（挂在下缘外侧）。</summary>
        public const float WitchSlotLabelDrop = WitchSlotHeight * 0.5f + 30f;

        public const float WitchSlotLabelWidth = WitchSlotWidth;

        public const float WitchSlotLabelHeight = 48f;

        public const float FontSizeWitchSlotLabel = 28f;

        /// <summary>两个空位中间那个「→」的尺寸（纯装饰，不吃射线）。</summary>
        public const float WitchArrowWidth = 160f;

        public const float WitchArrowHeight = 100f;

        public const float FontSizeWitchArrow = 64f;

        /// <summary>状态行（提示 / 校验失败的原因）。</summary>
        public const float WitchStatusCenterY = -160f;

        public const float WitchStatusWidth = 980f;

        public const float WitchStatusHeight = 56f;

        public const float FontSizeWitchStatus = 30f;

        // ── 价钱（2026-10-03 · 特殊强化改成**要花金币**）──────────────
        // 版式：状态行下面加「价格牌（木质六边形）+ 左右两块字」，再下面一行是价钱明细。
        // 三个 y 是**一起算出来的**（面板高 880、按钮顶边 −344、状态行底边 −188）：
        //   状态 −160 / 价格牌 −232 / 明细 −304 —— 三块互不重叠，也**不能再往下挪**
        //   （明细底边 −340，离按钮顶边只剩 4px）。改任何一块都要把这三行一起挪。

        /// <summary>价格牌的中心 Y。</summary>
        public const float WitchCostPlateCenterY = -232f;

        /// <summary>价格牌的缩放 —— 底图就是商店货位那张 <c>Shop_PricePlate</c>（金币已烘在图上）。</summary>
        public const float WitchCostPlateScale = 0.8f;

        public const float WitchCostPlateWidth = ShopPricePlateWidth * WitchCostPlateScale;

        public const float WitchCostPlateHeight = ShopPricePlateHeight * WitchCostPlateScale;

        /// <summary>
        /// 价钱数字的框（压在金币右侧）。
        ///
        /// <para>⚠ <b>不能只按比例缩</b>：价格牌是 Sliced —— 四周的 border 缩了、
        /// <b>中间那块（金币）几乎保持原大小</b>，所以金币的右边缘比「按比例」算出来的更靠右。
        /// 直接用 <c>ShopPriceTextOffsetX × 0.8</c>（= 21.6）会让第一个数字被金币压住一角
        /// （2026-10-03 截屏放大核对过）。往右推 14px 之后正好贴着金币右边。</para>
        /// </summary>
        public const float WitchCostTextOffsetX = ShopPriceTextOffsetX * WitchCostPlateScale + 14f;

        /// <summary>数字框的宽（同一条理由：往右挪了 14，右边也要跟着放宽）。</summary>
        public const float WitchCostTextWidth = ShopPriceTextWidth * WitchCostPlateScale + 14f;

        public const float WitchCostTextHeight = ShopPriceTextHeight * WitchCostPlateScale;

        public const float WitchCostTextOffsetY = ShopPriceTextOffsetY * WitchCostPlateScale;

        public const float FontSizeWitchCost = FontSizeShopPrice * WitchCostPlateScale;

        /// <summary>价格牌左右那两块说明字的横向中心（距面板中线）。</summary>
        public const float WitchCostLabelSideX = 320f;

        public const float WitchCostLabelWidth = 400f;

        public const float WitchCostLabelHeight = 56f;

        public const float FontSizeWitchCostLabel = 28f;

        /// <summary>价钱明细那一行（公式等式 / 「为什么变了」）的中心 Y、尺寸与字号。</summary>
        public const float WitchCostDetailCenterY = -304f;

        public const float WitchCostDetailWidth = 1400f;

        public const float WitchCostDetailHeight = 72f;

        public const float FontSizeWitchCostDetail = 22f;

        // ── 场景右上角的金币（2026-10-03）────────────────────────────
        // 女巫工坊没有商店那条 Hud_Bar，所以只补一枚「金币 50」在右上角 ——
        // 与左下角的「离开」键镜像对称，不占用水晶球与标题那一带。

        /// <summary>金币行离右边缘的间距（与「离开」键同值）。</summary>
        public const float WitchGoldRight = 64f;

        /// <summary>金币行离上边缘的间距（与「离开」键的下边距镜像）。</summary>
        public const float WitchGoldTop = 44f;

        public const float WitchGoldWidth = 460f;

        public const float WitchGoldHeight = 64f;

        public const float FontSizeWitchGold = 34f;

        /// <summary>底部两个按钮的中心 Y（与商店背包的「关闭」同一条线）。</summary>
        public const float WitchButtonCenterY = ShopBagCloseCenterY;

        public const float WitchButtonWidth = ShopBagCloseWidth;

        public const float WitchButtonHeight = ShopBagCloseHeight;

        /// <summary>按钮离面板左右边缘的间距（与商店背包同一个数）。</summary>
        public const float WitchButtonInset = ShopBagCloseRightInset;

        /// <summary>按钮中心的横向位置（左右对称）。</summary>
        public const float WitchButtonSideX = ShopBagPanelWidth * 0.5f - WitchButtonInset
                                              - WitchButtonWidth * 0.5f;

        public const float FontSizeWitchButton = FontSizeShopBagClose;

        /// <summary>浮层标题（面板顶部那一行）。</summary>
        public const float WitchPanelTitleCenterY = ShopBagTitleCenterY;

        public const float WitchPanelTitleWidth = ShopBagTitleWidth;

        public const float WitchPanelTitleHeight = ShopBagTitleHeight;

        public const float FontSizeWitchPanelTitle = FontSizeShopBagTitle;

        // ── 浏览层里的「效果选择」区（2026-10-02）────────────────────
        // 献祭牌有 >1 条效果时，浏览层换成这一屏：一行一条效果，点一条 = 选它并返回。
        // 位置整块与 `Cards` 滚动区重合（同一块矩形，切换显示），所以只有内部排布需要定义。

        /// <summary>这一屏的标题（「选一条要转移的效果」）中心 Y。</summary>
        public const float WitchEffectTitleCenterY = 232f;

        public const float WitchEffectTitleWidth = 1400f;

        public const float WitchEffectTitleHeight = 56f;

        public const float FontSizeWitchEffectTitle = 34f;

        /// <summary>第一行效果按钮的中心 Y（往下排，见 <see cref="WitchEffectRowStep"/>）。</summary>
        public const float WitchEffectFirstRowY = 120f;

        /// <summary>行高（按钮本体高度）。</summary>
        public const float WitchEffectRowHeight = 96f;

        /// <summary>相邻两行的中心距（= 行高 + 缝）。四行刚好排到 −222，不与底部那行说明打架。</summary>
        public const float WitchEffectRowStep = WitchEffectRowHeight + 18f;

        public const float WitchEffectRowWidth = 1320f;

        public const float FontSizeWitchEffectRow = 28f;

        /// <summary>
        /// 效果按钮的实例上限（构建器按它预建模板池；超出就换行参数自己不够用了）。
        ///
        /// <para>⚠ 目前卡表里<b>最多 2 条效果</b>（全表扫描确认过），4 是留的余量。
        /// 自测里有一条断言盯着「卡表最大效果条数 ≤ 这个数」——
        /// 以后加一张 5 效果牌会立刻变红，而不是在界面上少半行、零报错。</para>
        /// </summary>
        public const int WitchEffectMaxOptions = 4;

        /// <summary>这一屏底部那行说明（「这条效果会加到右边那张牌上」）。</summary>
        public const float WitchEffectHintCenterY = -300f;

        public const float WitchEffectHintWidth = 1400f;

        public const float WitchEffectHintHeight = 44f;

        public const float FontSizeWitchEffectHint = 26f;

        /// <summary>「返回」键的中心 Y（与底部的「关闭」同一条线）。</summary>
        public const float WitchEffectBackCenterY = WitchButtonCenterY;

        // ══════════════════════════════════════════════════════
        //  地图场景（2026-10-02 · P7 冒险地图）
        // ══════════════════════════════════════════════════════
        //
        //  版式来自用户 2026-10-02 给的 `Art/Map/` 那一批图：
        //  一张 1920×1080 的底面（`background_empty.png`，正好是参考分辨率，铺满即可）
        //  + 7 张节点图（营地 / 战斗 / 商店 / 女巫 / 石台 / 未知 / Boss）
        //  + 2 张桥（横的 / 斜的）+ 一张主角立绘。
        //
        //  ⚠ 本段所有坐标都是「画布绝对坐标」，锚点用**左下角 (0,0)**（同商店 / Char* 那批）。
        //    整个地图只用一套坐标：节点 (层, 行) → (x, y)，桥按两端节点算，
        //    主角棋子按当前节点 + 偏移算。
        //
        //  ── 版式账（1920×1080）──────────────────────────────────
        //  9 列（层）× 190 = 1520，左边距 200 → 最右列 1720，右边距 200；
        //  4 行 × 144 = 432，再叠「每层上漂 48」（8 层 = 384）→ 纵向共 816；
        //  底边 104 → 最高的节点中心 920，顶部还留着给标题 / 提示 / 顶栏。
        //  节点直径 128 < 行距 144 → 同一列上下两个节点不会挨上；
        //  128 < 列距 190 → 相邻两列的节点也不会挨上。
        //  ⚠ 这三个数字**不能单独改**：桥的角度是按 (列距, 行距, 每层上漂) 算出来的，
        //    只改一个会让「正好落在节点中心」的缩放公式失效（症状是桥短一截或长出节点）。

        /// <summary>地图层数（含起点层与 Boss 层）。与 <c>MapGenerator.DefaultLayerCount</c> 同值。</summary>
        public const int MapLayerCount = 9;

        /// <summary>地图每层最多几行。与 <c>MapGenerator.DefaultRowCount</c> 同值。</summary>
        public const int MapRowCount = 4;

        /// <summary>节点外框边长（正方形，图按原比例居中放进去）。</summary>
        public const float MapNodeSize = 128f;

        /// <summary>相邻两层（列）的中心距 X。</summary>
        public const float MapColumnStep = 190f;

        /// <summary>同一层内相邻两行（行）的中心距 Y。</summary>
        public const float MapRowStep = 144f;

        /// <summary>
        /// 每往右走一层，整个节点带往上漂多少 —— 这一项就是用户要的
        /// 「从左下角出发，一直到右上角迎接 Boss 战」。
        /// </summary>
        public const float MapLayerRise = 48f;

        /// <summary>第 0 层（起点层）节点的中心 X（画布绝对坐标）。</summary>
        public const float MapOriginX = 200f;

        /// <summary>第 0 行节点的中心 Y（画布绝对坐标）。</summary>
        public const float MapBottomY = 104f;

        /// <summary>节点下方那行小字（只在「可前往 / 当前」时显示）的中心偏移与尺寸。</summary>
        public const float MapLabelOffsetY = -68f;

        public const float MapLabelWidth = 160f;

        public const float MapLabelHeight = 22f;

        public const float FontSizeMapLabel = 20f;

        // ── 主角棋子 ──────────────────────────────────────────────
        //
        //  ⚠ 它**不盖在节点正中**，而是站在平台的右前方（偏移 +44, −6）：
        //    立绘高 116、宽约 60，正中摆放时头部会探进「上一行」那个节点 20 多像素，
        //    把那一格的图标压掉一角（而那一格很可能正是玩家下一步要点的）。
        //    往右前方挪之后，它与右邻列（列距 190）之间还留着 45 px 净空。

        public const float MapPlayerHeight = 116f;

        /// <summary>主角立绘的宽高比（素材 739×1435）。换立绘要一起改。</summary>
        public const float MapPlayerAspect = 739f / 1435f;

        public const float MapPlayerOffsetX = 44f;

        public const float MapPlayerOffsetY = -6f;

        /// <summary>走过去用多久（无缩放时间）。</summary>
        public const float MapMoveSeconds = 0.42f;

        /// <summary>
        /// 走到之后<b>停多久</b>再切场景（无缩放时间）。
        /// 不留这一下的话，位移还没看清画面就换了 —— 玩家读不到「我走到了哪一格」。
        /// </summary>
        public const float MapMoveHoldSeconds = 0.24f;

        /// <summary>高亮圈相对节点的放大倍数（圈图自带一圈内边距，1.28 刚好贴在节点外缘）。</summary>
        public const float MapRingScale = 1.3f;

        /// <summary>
        /// 可前往节点的高亮圈呼吸幅度（缩放 ±值）与周期（秒）。
        /// 位置固定、只动缩放：圈是**提示**不是状态，动位置会与节点错位。
        /// </summary>
        public const float MapRingPulse = 0.035f;

        public const float MapRingPulseSeconds = 1.35f;

        // ── 桥（数值量自素材本身，不是估的）────────────────────────
        //
        //  08_bridge_straight.png 315×70：两个端帽（深灰多边形）的中心在
        //      (49.4, 34.3) 与 (264.5, 35.0)（图像坐标，y 向下）→ 相对中心 (±107.6, ∓0.3)。
        //      端帽偏移**在本地 x 轴上**，所以直桥可以「只缩 x」：
        //      方向精确不变、木板厚度恒定。
        //  09_bridge_diagonal.png 243×179：端帽中心 (39.1,139.1) 与 (200.6,40.4)
        //      → 相对中心 (±80.75, ±49.35)（Unity 坐标 y 向上）。
        //      端帽偏移**不在任一轴上**，所以「只缩 x」会把桥的角度压斜
        //      （缩 1.5 倍时角度从 31.4° 掉到 21.5°）—— 必须反解：
        //      s = √(len² − B²) / A，再把旋转补上 α = atan2(B, A·s) 这一段。

        /// <summary>直桥两端帽中心距（素材像素；1 px = 1 UI 单位，因为 RectTransform 就是按素材尺寸建的）。</summary>
        public const float MapBridgeStraightCapSpan = 215.2f;

        /// <summary>斜桥端帽偏移的 x 分量（×2 = 端帽中心距的 x 部分）。</summary>
        public const float MapBridgeDiagonalCapX = 161.5f;

        /// <summary>斜桥端帽偏移的 y 分量（×2 = 端帽中心距的 y 部分）。</summary>
        public const float MapBridgeDiagonalCapY = 98.7f;

        /// <summary>
        /// 边与水平线的夹角超过这个值就用<b>斜桥</b>，否则用直桥（度）。
        ///
        /// <para>8° 的来源：本版式只有三种边角 —— 14.2°（同行）/ 45.3°（上一行）/ −26.8°（下一行）。
        /// 14.2° 与 45.3° 交给斜桥（素材本身 31.4°，最多偏转 ±24°），
        /// −26.8° 那条交给直桥（直桥在任何角度都不变形，而且斜桥反着转 −58° 时
        /// 它烘好的明暗面会翻过来、看着像被掀了顶）。</para>
        /// </summary>
        public const float MapBridgeDiagonalMinAngle = 8f;

        // ── 标题 / 提示 / 结束浮层 ─────────────────────────────────

        public const float MapTitleCenterY = 1014f;

        public const float MapTitleWidth = 900f;

        public const float MapTitleHeight = 74f;

        public const float FontSizeMapTitle = 52f;

        /// <summary>标题下面那行操作提示（「点高亮的节点前进」）。</summary>
        public const float MapHintCenterY = 952f;

        public const float MapHintWidth = 1300f;

        public const float MapHintHeight = 34f;

        public const float FontSizeMapHint = 26f;

        /// <summary>结束浮层里那块面板的尺寸（沿用背包面板的底图与九宫格设置）。</summary>
        public const float MapEndPanelWidth = 880f;

        public const float MapEndPanelHeight = 460f;

        public const float MapEndTitleCenterY = 118f;

        public const float MapEndTitleWidth = 760f;

        public const float MapEndTitleHeight = 88f;

        public const float FontSizeMapEndTitle = 64f;

        public const float MapEndBodyCenterY = 18f;

        public const float MapEndBodyWidth = 760f;

        public const float MapEndBodyHeight = 120f;

        public const float FontSizeMapEndBody = 26f;

        public const float MapEndButtonCenterY = -148f;

        public const float MapEndButtonWidth = 260f;

        public const float MapEndButtonHeight = 72f;

        public const float FontSizeMapEndButton = 30f;

        /// <summary>地图背景的铺法：素材正好 1920×1080 → 直接铺满（不用 cover 那套换算）。</summary>
        public const float MapBackgroundWidth = 1920f;

        public const float MapBackgroundHeight = 1080f;

        // ── 图集路径（构建器从这里取，运行时不读）────────────────────

        public const string MapBackgroundSpritePath = "Assets/Art/Map/background_empty.png";

        public const string MapPlayerSpritePath = "Assets/Art/Chars/Map/Player_Map.png";

        /// <summary>
        /// 节点高亮框（「可以去」金 / 「你在这儿」蓝）用的那张图。
        ///
        /// <para><b>⚠ 2026-10-02 换过一次</b>：最初拿的是 <c>Buff_02_RingEmpty.png</c>
        /// （光环槽那块等距深色石环）。切进九宫格、染成金色之后是<b>橄榄色</b>的方框
        /// —— 因为那张图本身是深蓝灰，乘金色只能得到脏黄，读不出「亮着」。
        /// <c>CardBox_Line</c> 是<b>白色描边</b>的圆角框，染什么色就是什么色，
        /// 而且它本来就是工程里「可点 / 空位」的统一语言（商店的空槽位用的就是它）。</para>
        /// </summary>
        public const string MapRingSpritePath = "Assets/Art/Ui/CardBox_Line.png";

        public const string MapBridgeStraightSpritePath = "Assets/Art/Map/08_bridge_straight.png";

        public const string MapBridgeDiagonalSpritePath = "Assets/Art/Map/09_bridge_diagonal.png";

        /// <summary>7 张节点图，<b>下标 = <c>MapNodeType</c> 的值</b>（构建器按这个顺序填进 MapView 的数组）。</summary>
        public static readonly string[] MapNodeSpritePaths =
        {
            "Assets/Art/Map/06_camp.png",       // Camp
            "Assets/Art/Map/04_battle.png",     // Battle
            "Assets/Art/Map/01_shop.png",       // Shop
            "Assets/Art/Map/02_witch.png",      // Witch
            "Assets/Art/Map/03_altar.png",      // Altar
            "Assets/Art/Map/07_unknown.png",    // Unknown
            "Assets/Art/Map/05_Boss.png",       // Boss
        };

        /// <summary>
        /// 由 (层, 行) 算节点中心（画布绝对坐标）。
        ///
        /// <para><b>唯一的一处坐标换算</b>：桥的两端、主角的落点、节点的位置全部调它 ——
        /// 谁都不许自己再算一遍，否则「桥歪了半格」这种问题会同时有三个可疑来源。</para>
        /// </summary>
        public static Vector2 MapNodePosition(int layer, int row)
        {
            return new Vector2(MapOriginX + layer * MapColumnStep,
                MapBottomY + row * MapRowStep + layer * MapLayerRise);
        }
    }
}
