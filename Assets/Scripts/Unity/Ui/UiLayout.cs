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
        //  锚点、pivot、anchoredPosition、尺寸、字号全部取自它，不再参考
        //  `Assets/Art/Cards/` 那张 760×1056 的成品卡面（用户 2026-09-19 明确要求）。
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
        /// <para>量自原图：第 41 行起不透明像素从 2 跳到 157、第 42 行到 289 ——
        /// 那是顶部牌位的横 band 起点；band 覆盖 42…75 左右，**中心 ≈ 58**。
        /// 于是 `35 + 46/2 = 58`。</para>
        /// </summary>
        public const float HandPickTitleTop = 35f;

        public const float HandPickTitleHeight = 46f;

        /// <summary>
        /// 副标题（「0 / 6」这种计数）距标题框<strong>底边</strong>的间隙。
        ///
        /// <para>原来写死 2 px，配上 46 高的标题框与 32 高的副标题框 →
        /// 两行文字实际只隔 2 px，截图里「送入冷却增加力量（可选择多张）」的降部
        /// 几乎贴到「0 / 6」上（2026-09-22 `m30_confirm_empty.png`）。
        /// 这里给到 8 px：标题降部不会撞上计数，又不至于把副标题推进中间卡框区。</para>
        /// </summary>
        public const float HandPickSubTitleGap = 8f;

        /// <summary>副标题的高度（一行「0 / 6」）。</summary>
        public const float HandPickSubTitleHeight = 32f;

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

        /// <summary>副标题（「0 / 3」这一行计数）的字号。</summary>
        public const float FontSizeHandPickSubTitle = 22f;

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
    }
}
