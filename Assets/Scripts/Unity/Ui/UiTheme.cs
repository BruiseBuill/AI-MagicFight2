using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 配色表。深板岩底 + 暖色点缀，和卡面的 low-poly 美术调性一致。
    /// 所有颜色都集中在这里，改风格只改这一处。
    /// </summary>
    public static class UiTheme
    {
        // ── 底色 / 面板 ──────────────────────────────────────
        public static readonly Color Backdrop = Hex("0F131B");
        public static readonly Color Panel = Hex("1A2130");
        public static readonly Color PanelEdge = Hex("2B3547");
        public static readonly Color MiniCardFace = Hex("262F3F");
        public static readonly Color MiniCardEdge = Hex("3C4860");

        // ── 双方主色 ─────────────────────────────────────────
        public static readonly Color PlayerAccent = Hex("4FC3F7");
        public static readonly Color EnemyAccent = Hex("FF7043");

        // ── 文字 ─────────────────────────────────────────────
        public static readonly Color TextPrimary = Hex("EEF2F8");
        public static readonly Color TextSecondary = Hex("96A3B8");
        public static readonly Color TextOnArt = Hex("FFFFFF");

        /// <summary>
        /// 剩余冷却数字专用色。取卡面右上角那个冷却圆点的浅蓝 ——
        /// 迷你卡上「力量」和「剩余冷却」都是数字，光靠位置容易看混，用颜色延续卡面的视觉语言来区分。
        /// </summary>
        public static readonly Color CooldownNumber = Hex("9FD8F0");

        // ── 生命 ─────────────────────────────────────────────
        /// <summary>仍持有的生命点。</summary>
        public static readonly Color HpFull = Hex("E5484D");

        /// <summary>失去的生命点（本回合掉的血）。</summary>
        public static readonly Color HpEmpty = Hex("333B4B");

        /// <summary>已被永久削掉的生命上限（过载 / 自燃 / 沉重打击造成）。</summary>
        public static readonly Color HpGone = Hex("1A1F29");

        // ── 光环指示物 ───────────────────────────────────────
        /// <summary>未使用（亮）。</summary>
        public static readonly Color AuraReady = Hex("FFD166");

        /// <summary>已用完（灰）。</summary>
        public static readonly Color AuraSpent = Hex("3A4150");

        // ── 交互态 ───────────────────────────────────────────
        public static readonly Color SelectionGlow = Hex("FFD166");
        public static readonly Color CannotPlayDim = new Color(0f, 0f, 0f, 0.58f);

        /// <summary>角标底色（半透明黑，压在手牌图上保证白字可读）。</summary>
        public static readonly Color BadgeBackdrop = new Color(0f, 0f, 0f, 0.62f);

        // ══════════════════════════════════════════════════════
        //  M8 BattleUi
        // ══════════════════════════════════════════════════════

        /// <summary>浮层底（比 Panel 更实一点，压在场景上也读得清）。</summary>
        public static readonly Color OverlayPanel = Hex("141A25");

        /// <summary>浮层描边。</summary>
        public static readonly Color OverlayEdge = Hex("3A4761");

        /// <summary>结算浮层背后的遮罩（压暗整局画面）。</summary>
        public static readonly Color OverlayVeil = new Color(0f, 0f, 0f, 0.72f);

        /// <summary>选项按钮常态 / 悬停。</summary>
        public static readonly Color PickerRow = Hex("232C3C");
        public static readonly Color PickerRowHot = Hex("33405A");

        /// <summary>
        /// 告警红：<b>低生命数值</b>（≤1 点时头顶徽标的读数）与发牌台「已选满」计数行的闪烁。
        ///
        /// <para><b>M20 起它不再用于受击</b>：原来那块「掉血时铺满屏幕的红色 Image」
        /// 已按用户口径删除（掉血改由角色自己的受击动画表达）。
        /// 名字从 <c>DamageFlash</c> 改成 <c>WarnRed</c>，免得后来人以为还有个全屏闪红。</para>
        /// </summary>
        public static readonly Color WarnRed = Hex("FF5252");

        /// <summary>胜利 / 失败主色。</summary>
        public static readonly Color WinAccent = Hex("7BD88F");
        public static readonly Color LoseAccent = Hex("FF6B6B");

        /// <summary>提示条底（半透明，压住后面的舞台）。</summary>
        public static readonly Color PromptBackdrop = new Color(0.06f, 0.08f, 0.12f, 0.86f);

        // ══════════════════════════════════════════════════════
        //  屏幕中央行动提示（2026-09-19）
        // ══════════════════════════════════════════════════════
        //
        //  与 PromptBackdrop 是同一个底（都压着舞台看），只把不透明度再抬一点 ——
        //  中央横幅比顶部提示条更靠画面中心，背后的地牢亮部会透上来。

        /// <summary>中央行动提示的底衬。</summary>
        public static readonly Color ActionBannerBackdrop = new Color(0.06f, 0.08f, 0.12f, 0.9f);

        /// <summary>中央行动提示的文字（金色，与「可用光环」同一档，一眼看出是「该你了」）。</summary>
        public static readonly Color ActionBannerText = Hex("FFE9A8");

        // ══════════════════════════════════════════════════════
        //  M36 · 屏幕中央的「浮字」（回答「这张牌为什么点不动」）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-23）：对一张本拍不可能被加速 / 减速的牌操作时，
        //  要弹一段文字说明，在中间弹出 → 向上移动 → 迅速透明消失。
        //
        //  比行动横幅（ActionBanner）更轻：横幅是「轮到谁了」这种一眼必须看到的事，
        //  浮字只是回答一个问题，底衬压得比它淡、字号也只有它一半。

        /// <summary>浮字底衬（半透明深色，压住场景保证可读）。</summary>
        public static readonly Color FloatTipBackdrop = new Color(0.06f, 0.08f, 0.12f, 0.82f);

        /// <summary>浮字文字（暖金，与「可用光环 / 该你了」同一档，读作「提示」）。</summary>
        public static readonly Color FloatTipText = Hex("FFE9A8");

        // ══════════════════════════════════════════════════════
        //  M12 手牌拖拽
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 拖牌落到角色身上时给角色的乘算色。
        ///
        /// <para>取「略偏暖的浅色」而不是饱和色：<c>Image.color</c> 在这条链路上是<b>乘算</b>，
        /// 角色原画本身偏暗蓝，乘一个饱和的橙会直接糊成褐色。浅暖色只提亮、不换色相，
        /// 配合 6% 的放大就足够读出「这个目标选上了」。</para>
        /// </summary>
        public static readonly Color DropTargetTint = Hex("FFE7C4");

        // ══════════════════════════════════════════════════════
        //  M13 · 出牌指向箭头 + 对手头顶出牌
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 进攻箭头：红。取比敌方主色（<see cref="EnemyAccent"/> 偏橙）更纯的红 ——
        /// 箭头是细长条，橙在深色地牢背景上会跟火光混在一起读不出来。
        /// </summary>
        public static readonly Color ArrowAttack = Hex("FF4D4D");

        /// <summary>
        /// 防御箭头：蓝。直接用玩家主色 —— 语义就是「指向自己这边」，
        /// 颜色本身也跟「己方冷却区 / 己方能量球」那一整套青蓝呼应。
        /// </summary>
        public static readonly Color ArrowDefend = PlayerAccent;

        /// <summary>头顶那张「对手刚打出的牌」的衬底（半透明深色底，保证卡面边缘在场景上能读出来）。</summary>
        public static readonly Color PlayedCardBackdrop = new Color(0.02f, 0.03f, 0.05f, 0.78f);

        /// <summary>头顶那张牌的描边（暗金，与头顶血条徽标的边一致，暗示「这是刚刚发生的事」）。</summary>
        public static readonly Color PlayedCardEdge = Hex("8A6A2F");

        // ══════════════════════════════════════════════════════
        //  M9 DealUi
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 「确认替换」主按钮。取玩家主色（青）再压暗一档 —— 它是最该被点的那颗，
        /// 但又不该亮到跟卡面抢注意力。
        /// </summary>
        public static readonly Color DealConfirm = Hex("2E7C8F");

        public static readonly Color DealConfirmHot = Hex("3E9DB4");

        /// <summary>
        /// 「确认替换」的不可用态（一张都没标记时）。<c>ColorTint</c> 是<b>替换</b>颜色而不是乘算，
        /// 所以这里必须给一个本身就压到底的颜色，光靠 alpha 是暗不下来的。
        /// </summary>
        public static readonly Color DealConfirmOff = Hex("243040");

        // ══════════════════════════════════════════════════════
        //  M15 · HudBuff 光环图标区
        // ══════════════════════════════════════════════════════
        //
        //  光环图标 = 底版（按类型上色）+ 符号（剑 / 盾 / 感叹号，取自 TriggerIconLibrary）
        //  + 数值。**颜色是给人分类用的第一眼信息**，所以按「这条光环管进攻还是管防御」来分：
        //  红 = 进攻、蓝 = 防御、紫 = 攻防二选一、青 = 连击、金 = 免疫。
        //  （金沿用 AuraReady —— 整个游戏的「光环」语义本来就是这个颜色。）

        /// <summary>进攻力量类。</summary>
        public static readonly Color AuraPlateAtk = Hex("C4453F");

        /// <summary>防御力量类。</summary>
        public static readonly Color AuraPlateDef = Hex("2F6FA8");

        /// <summary>攻防二选一。</summary>
        public static readonly Color AuraPlateBoth = Hex("7A55C4");

        /// <summary>连击。</summary>
        public static readonly Color AuraPlateCombo = Hex("2E8B8B");

        /// <summary>免疫。</summary>
        public static readonly Color AuraPlateImmune = Hex("B98A2A");

        /// <summary>底版描边（暗色，让图标在浅色背景上也立得住）。</summary>
        public static readonly Color AuraIconEdge = Hex("10151E");

        /// <summary>符号（剑 / 盾 / 感叹号）的颜色 —— 近白，压在饱和底版上最清楚。</summary>
        public static readonly Color AuraGlyph = Hex("FFF6E2");

        /// <summary>数值文字的底衬（半透明黑）。</summary>
        public static readonly Color AuraValueBackdrop = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>数值文字。</summary>
        public static readonly Color AuraValueText = Hex("FFFFFF");

        /// <summary>
        /// 「这张牌的光环现在用不了」时压在图上的遮罩。
        /// 用<b>半透明黑覆盖层</b>而不是给底版换色：底版的色相本身就是分类信息（红/蓝/紫/青/金），
        /// 用亮度去表达「能不能用」不会把分类读丢。
        /// </summary>
        public static readonly Color AuraIconDim = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>可以拖去用的那一刻，图标外圈的点亮色。</summary>
        public static readonly Color AuraIconReadyEdge = Hex("FFD166");

        // ⚠ 这里**没有** AuraDropHint —— 光环落区提示底（HandArea/AuraDropHint）的颜色是纯美术属性，
        //   只写在 BattleCanvas.prefab 的 Image.color 上，在 Hierarchy 里随时调。代码里任何一处
        //   给它赋值都会把那份调色悄悄冲掉（2026-09-19 用户明确要求：这类提示的颜色不要用代码写）。
        //   （M21 删掉了另一个同类节点 PreparedAura/ZoneHighlight —— 取消手势改成「再拖一下标记」之后
        //   它不再有含义。）

        /// <summary>长按光环弹出的说明浮层底色 / 描边。</summary>
        public static readonly Color AuraTipBackdrop = new Color(0.05f, 0.07f, 0.11f, 0.96f);
        public static readonly Color AuraTipEdge = Hex("6E5A2A");

        // ══════════════════════════════════════════════════════
        //  M16 · 组成式卡面
        // ══════════════════════════════════════════════════════
        //
        //  口径：卡面要压住底下的插画、又不把插画闷死 ——
        //  卡底只做「整体压暗」，重点区域（卡名条 / 效果文字框）再叠一层更实的深色；
        //  描边走暖白细边（跟成品卡面那圈浅金边框一个调子）。
        //
        //  ⚠ 这些是**半透明覆盖色**，不是实色。插画原图是满幅不透明的，
        //    调这里就是调「插画还能看清多少」；想更清楚地看到插画就整体调低 alpha。

        /// <summary>卡底：盖在插画上的整体压暗层。</summary>
        public static readonly Color CardFaceBackdrop = new Color(0.05f, 0.04f, 0.06f, 0.34f);

        /// <summary>卡框描边（暖白细边）。</summary>
        public static readonly Color CardFaceEdge = new Color(1f, 0.95f, 0.86f, 0.55f);

        /// <summary>卡名条底色（比卡底更实，否则名字压在插画上读不出来）。</summary>
        public static readonly Color CardNameBarBackdrop = new Color(0.07f, 0.06f, 0.08f, 0.62f);

        /// <summary>效果文字框底色。</summary>
        public static readonly Color CardTextBoxBackdrop = new Color(0.07f, 0.06f, 0.08f, 0.90f);

        /// <summary>效果文字框描边。</summary>
        public static readonly Color CardTextBoxEdge = new Color(1f, 0.92f, 0.80f, 0.28f);

        /// <summary>
        /// 力量数字：**深色**。
        /// 星爆（`Art/Fx/Fx_Explosion_Light.png`）的中心是一团亮米白，白字会整块糊在里面 ——
        /// 实测过：白字完全看不见。prefab 自己用的就是深灰（0.189），这里照抄。
        /// </summary>
        public static readonly Color CardPowerNumber = new Color(0.189f, 0.189f, 0.189f, 1f);

        /// <summary>冷却数字：深色（蓝圆章很亮，白字同样看不清）；照 prefab 的近黑色。</summary>
        public static readonly Color CardCoolNumber = new Color(0f, 0f, 0f, 0.98f);

        /// <summary>
        /// 力量数字的<strong>预览色</strong>（玩家在判定区准备了力量光环时，手牌上的力量改成
        /// 「原值 + 加值」并染成这个色，见 <see cref="CardView.SetPowerPreview"/>）。
        ///
        /// <para>必须是<strong>深色</strong>：星爆底是一团亮米白，浅色 / 高饱和的亮色数字
        /// 会整块糊进去（同 <see cref="CardPowerNumber"/> 的理由）。所以这里用暗金 ——
        /// 压在原深灰上能一眼看出「这个数被光环改过」，又仍读得清。</para>
        /// </summary>
        public static readonly Color PowerPreview = new Color(0.62f, 0.34f, 0f, 1f);

        /// <summary>
        /// 冷却区的「整列可点」高亮（M24 #2：区域加速 / 减速改为直接点那一侧冷却区）。
        ///
        /// <para>同 <see cref="SelectionGlow"/> 的暖金调子，让玩家一眼看出「这两片现在能点」。</para>
        /// </summary>
        public static readonly Color ZonePickableEdge = Hex("FFD166");

        // ══════════════════════════════════════════════════════
        //  M25 查看对方手牌弹窗（雷云 / 狂躁蘑菇的第 2 个 α 效果）
        // ══════════════════════════════════════════════════════

        /// <summary>弹窗背后压暗整局画面的遮罩。</summary>
        public static readonly Color PeekVeil = new Color(0f, 0f, 0f, 0.7f);

        /// <summary>结算角标的底衬（压在有花纹的面板上，保证白字可读）。</summary>
        public static readonly Color PeekBadgeBackdrop = new Color(0f, 0f, 0f, 0.66f);

        /// <summary>结算结果「送入冷却」——用的是对手色，读作「对面这张被锁住了」。</summary>
        public static readonly Color PeekCooled = EnemyAccent;

        /// <summary>结算结果「不送入冷却」——中性，不抢注意力。</summary>
        public static readonly Color PeekKept = TextSecondary;

        // ══════════════════════════════════════════════════════
        //  M27 手牌选择弹窗（磁暴 / 充能 / 电弧）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 弹窗背后压暗整局画面的遮罩。
        ///
        /// <para><b>2026-09-22 从 0.62 降到 0.32</b>：这块遮罩已改成<b>不吃射线</b>
        /// （见 <c>BattleUiBuilder.BuildHandPickLayer</c> 的说明）—— 因为玩家正是要点
        /// 它背后那片手牌来选牌，遮罩吃射线就等于把手牌区整个封死。既然要从它后面选牌，
        /// 那压暗就必须收手：0.62 的黑会让手牌变成一片剪影，玩家看不清自己在点哪张
        /// （卡面、力量数字、以及 HandView 打上的「已选」辉光都会被盖掉）。
        /// 0.32 还看得清全局「现在是被弹窗接管的一拍」，但手牌仍是可读、可选的。</para>
        /// </summary>
        public static readonly Color HandPickVeil = new Color(0f, 0f, 0f, 0.32f);

        /// <summary>
        /// 面板躯干色 —— 唯一一个「从美术图里量出来的颜色」。
        ///
        /// <para>用途是盖住 <c>Peek_Frame.png</c> 里那块内框装饰：面板横向拉宽时，
        /// 内框的圆角描边与中心菱形花纹会被一起抻开，所以在它上面盖一条与躯干同色的
        /// 纯色条（多选时），再在纯色条上摆自己的卡框。</para>
        ///
        /// <para>量自原图躯干（x=60/370 的 y=200/300/400 三处采样）：RGB(20, 40, 52) = #142834。</para>
        /// </summary>
        public static readonly Color HandPickPanelTorso = Hex("142834");

        /// <summary>空卡框（还没选任何牌时那一块）的描边色 —— 冷蓝灰，读作「这里可以放牌」。</summary>
        public static readonly Color HandPickEmptyEdge = Hex("4A7EA8");

        /// <summary>空卡框的内部底色 —— 比躯干再暗一档，让空框有「凹进去」的感觉。</summary>
        public static readonly Color HandPickEmptyFill = new Color(0f, 0f, 0f, 0.32f);

        /// <summary>已选卡框的描边色 —— 选中就亮起来。</summary>
        public static readonly Color HandPickSlotEdge = SelectionGlow;

        // ⚠ 2026-09-23：原先这里有一条 HandPickIndexBadge（卡框左上角的「第几张」角标底色）。
        //   用户口径「移除掉 slot area 里的 indexBadge，不需要序号」→ 角标整个删掉，
        //   常量跟着删 —— 留着一个没人用的颜色只会让下一个人以为它还有读者
        //  （它当初就是为了「借用一个会随别处漂移的常量」而拆出来的，见 v1 的 2026-09-22 记录）。

        /// <summary>
        /// 确认键「可用」态的颜色 —— <b>全透明</b>。
        ///
        /// <para><b>2026-09-23 用户口径</b>：「移除掉这个确认键的背景，（背景）就用按钮座那个」。
        /// 确认键从 M30 起画的那条素材（<c>HandPick_Confirm.png</c>）纵向被 174 → 58 硬压，
        /// 观感是一条发白的扁条；而 <c>Peek_Frame.png</c> <b>本身就带</b>一块做好的底部按钮座。
        /// 所以现在按钮只留文字 + 命中区，底色交给面板自带的按钮座。</para>
        ///
        /// <para>于是这里只能给<b>透明</b>：任何不透明色都会盖住那块美术（这正是要移除的东西）。
        /// ⚠ 与 <see cref="HandPickConfirmOff"/> 配对时注意 <c>ColorTint</c> 的乘法语义 ——
        /// Image.color 保持不透明白，两态的实色由这两个常量给。</para>
        /// </summary>
        public static readonly Color HandPickConfirmOn = new Color(1f, 1f, 1f, 0f);

        /// <summary>
        /// 确认键「不可用」态的颜色 —— 一层半透明暗纱，压暗面板自带的按钮座。
        ///
        /// <para><b>为什么要保留它</b>：电弧 / 漩涡是「必须选够才能确认」的
        /// （引擎给 <c>MinSelect = 1</c>），必须让玩家一眼看出「现在还不能按」。
        /// 只靠 <see cref="HandPickConfirmTextOff"/> 把文字变暗太弱。</para>
        ///
        /// <para>⚠ 它是<b>纱</b>不是底图：alpha 别往上调 —— 按钮矩形比按钮座略宽，
        /// alpha 一大就会在座的斜角之外露出一圈方块边。0.55 是在暗色躯干上几乎看不出边界、
        /// 又能把亮蓝座压下去的值。</para>
        /// </summary>
        public static readonly Color HandPickConfirmOff = new Color(0.055f, 0.105f, 0.155f, 0.55f);

        /// <summary>确认键不可用时的文字色。</summary>
        public static readonly Color HandPickConfirmTextOff = Hex("6B7C88");

        // ══════════════════════════════════════════════════════
        //  M28 查看怪物手牌（按住右下角手牌数）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 面板背后的遮罩。
        ///
        /// <para>比 M25 的 <see cref="PeekVeil"/> 更淡：这块面板是**按住**看的，
        /// 玩家的注意力全在被按住的标签与面板之间，压太黑反而像「卡住了」；
        /// 而且松手就消失，不需要靠遮罩来强调「这是一个模态界面」。</para>
        /// </summary>
        public static readonly Color MonsterHandVeil = new Color(0f, 0f, 0f, 0.45f);

        /// <summary>「未知」牌位上的暗压 —— 唯一一处「牌背也看不太清」的表达，靠它区分知与不知。</summary>
        public static readonly Color MonsterHandUnknownDim = new Color(0.35f, 0.38f, 0.45f, 0.72f);

        /// <summary>副标题里「已知 N」那半截的高亮色（与选中态同一支暖金）。</summary>
        public static readonly Color MonsterHandKnown = SelectionGlow;

        /// <summary>把 "RRGGBB" 转成 Color（不解析 # 前缀之外的花样，够用即可）。</summary>
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return Color.magenta;
            }

            if (hex[0] == '#')
            {
                hex = hex.Substring(1);
            }

            if (hex.Length < 6)
            {
                return Color.magenta;
            }

            int r = System.Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = System.Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = System.Convert.ToInt32(hex.Substring(4, 2), 16);
            float a = 1f;

            if (hex.Length >= 8)
            {
                a = System.Convert.ToInt32(hex.Substring(6, 2), 16) / 255f;
            }

            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        /// <summary>同色但改透明度。</summary>
        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
