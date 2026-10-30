namespace MagicBrawl.Core
{
    /// <summary>
    /// 触发时机 ↔ 卡面符号的**唯一映射**。
    ///
    /// <para>放在 Core 而不是表现层：`Docs/rules/01-规则基线.md` §通用原则写明
    /// 「卡面符号即结算时机」—— 哪个符号对应哪个时机是**规则事实**，
    /// 表现层只负责把字符换成图（手牌走 TMP 图文混排，光环图标走 Image）。</para>
    ///
    /// <para>为什么要一个常量类而不是散在各处写 `"α"`：符号是**字符**不是图片，
    /// 手牌效果栏、详情浮层、提示条各自去写字面量，早晚会出现一处写成 `"a"`、
    /// 一处漏了符号的错位。这里定死，其余地方只引用。</para>
    /// </summary>
    public static class TriggerSymbol
    {
        /// <summary>α 剑 —— 本牌作为进攻牌打出时结算。</summary>
        public const string Attack = "α";

        /// <summary>β 盾 —— 本牌作为防御牌打出时结算。</summary>
        public const string Defend = "β";

        /// <summary>γ 感叹号 —— 特殊时机，由卡面文字说明。</summary>
        public const string Special = "γ";

        /// <summary>
        /// 取某个时机的符号。三种触发时机均有唯一符号，未知值返回空串。
        /// </summary>
        public static string Of(EffectTrigger trigger)
        {
            switch (trigger)
            {
                case EffectTrigger.Attack:
                    return Attack;
                case EffectTrigger.Defend:
                    return Defend;
                case EffectTrigger.Special:
                    return Special;
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// 效果触发时机。对应卡面上的符号（见 `Docs/rules/01-规则基线.md` §0）：
    /// α 剑 = 进攻时 · β 盾 = 防御时 · γ 圈 = 特殊（时机见卡面文字）。
    /// </summary>
    public enum EffectTrigger
    {
        /// <summary>α：本牌作为<strong>进攻牌</strong>打出时触发。</summary>
        Attack = 0,

        /// <summary>β：本牌作为<strong>防御牌</strong>打出时触发。</summary>
        Defend = 1,

        /// <summary>γ：特殊时机，由卡面文字说明（目前仅潮汐的「冷却完毕时」）。</summary>
        Special = 2,

    }

    /// <summary>
    /// 光环类型（`Docs/engineering/04-架构与接口.md` §3 末尾）。
    /// 光环 = 附着在提供它的那张牌上的<strong>一次性指示物</strong>，
    /// 牌进入冷却区后才生效，冷却完毕回手时未使用的指示物作废。
    /// </summary>
    public enum AuraKind
    {
        None = 0,

        /// <summary>进攻力量 +A。仅进攻时可用。</summary>
        AtkPower = 1,

        /// <summary>防御力量 +A。仅防御时可用。</summary>
        DefPower = 2,

        /// <summary>使本次打出的牌获得「连击」。仅进攻时可用；A = 可赋予连击的基础力量上限。</summary>
        Combo = 3,

        /// <summary>免疫力量 ≥ A 的攻击（含整个双发）。仅防御时可用。</summary>
        ImmuneHigh = 4,

        /// <summary>免疫力量 ≤ A 的攻击（含整个双发）。仅防御时可用。</summary>
        ImmuneLow = 5,

        /// <summary>进攻力量 +A <em>或</em> 防御力量 +A，使用时二选一。</summary>
        AtkOrDefPower = 6,

        /// <summary>
        /// 快速回填光环：使<strong>本次打出的那张牌</strong>获得「快速回填」
        /// （进入冷却区时剩余冷却额外 −A）。仅进攻时可用。
        ///
        /// <para><b>2026-09-29 新增（击穿 ap）</b>。它与 <see cref="EffectOp.QuickRefill"/>
        /// 是同一条规则，区别只在发放方式：那个是打出时就绑在自己身上，
        /// 这个是<b>存成一枚指示物</b>，可以留给后面某一拍打出的大牌用
        /// —— 所以它必须能作用于「别人的牌」，这只有光环这条路做得到。</para>
        ///
        /// <para><b>它不算「力量加值」</b>（<see cref="AuraResolver.IsPowerBonus"/> 返回 false）：
        /// 防御拍上「这张牌还差几点补值才算挡住」的预算里不能把它算进去
        /// —— 它减的是冷却，不是力量。</para>
        /// </summary>
        QuickRefill = 7,
    }

    /// <summary>
    /// <b>刻印</b>的重复触发时机（2026-10-07 新增）。
    ///
    /// <para><b>刻印是什么</b>（用户口径）：像光环一样挂在<b>提供它的那张牌</b>上、
    /// <b>只有这张牌作为进攻牌打出时才点亮</b>，回手即熄灭；与光环的差别只有一条 ——
    /// 光环是<b>一次性</b>指示物（用掉就没了），刻印点亮之后<b>每个回合都会自动再触发一次</b>
    /// 自己的效果。</para>
    ///
    /// <para><b>它和 <see cref="EffectOp.Aura"/> 的关系</b>：刻印不是新算子，而是给
    /// <b>既有算子</b>加的一层「触发方式」。卡表里写 <c>A(EffectOp.Aura, 2, aura: AuraKind.AtkPower,
    /// sigil: SigilTiming.TurnStart)</c> 就是「α刻印：每回合开始时获得光环：进攻力量 +2」——
    /// 效果本身（光环 / 加速）一条代码都不用重写，引擎只多做两件事：
    /// <list type="number">
    /// <item>打出去那一拍的第 ⑤ 步<b>点亮</b>它（而不是当场结算）；</item>
    /// <item>之后每个点位把同一条效果<b>再跑一遍</b>（见 <see cref="SigilTiming"/> 各成员）。</item>
    /// </list></para>
    ///
    /// <para>⚠ 不立项的话这两件事都无处安放：光环算子是在第 ⑤ 步「结算一次」的，
    /// 而刻印要的是「第 ⑤ 步只点亮、以后每回合重放」。</para>
    /// </summary>
    public enum SigilTiming
    {
        /// <summary>不是刻印 —— 普通效果（绝大多数卡）。</summary>
        None = 0,

        /// <summary>
        /// <b>每回合开始时</b>触发一次（「回合」= <c>BattleState.TurnNumber</c> 自增那一刻，
        /// 双方各自的一次进攻合起来算一个回合）。火炬（at）用的就是它。
        /// </summary>
        TurnStart = 1,

        /// <summary>
        /// <b>拥有者自己的进攻半场结束时</b>触发一次。连击链算<b>一次</b>
        /// （口径与虚弱递减一致：一次「进攻半场」= 整条连击链）。
        /// 点亮它的那一拍<b>立刻</b>触发（用户 2026-10-07 口径）。海啸（au）用的就是它。
        /// </summary>
        AttackEnd = 2,
    }

    /// <summary>
    /// 效果算子。40 张卡全部映射到这里的算子上（`Docs/engineering/04-架构与接口.md` §3）。
    /// 参数约定：A / B / C 的含义逐算子说明在各自成员上。
    /// </summary>
    public enum EffectOp
    {
        None = 0,

        // ── 冷却类（结算第 ③ 步）─────────────────────────────
        /// <summary>单张加速 −1。A = 次数（可分配给同一张或不同张）。</summary>
        Haste = 1,
        /// <summary>单张减速 +1，上限 = 基础冷却值。A = 次数。</summary>
        Slow = 2,
        /// <summary>区域加速：选定某一方冷却区中剩余冷却 = k 的牌全体 −1。k 由决策给出。</summary>
        HasteZone = 3,
        /// <summary>区域减速：选定某一方冷却区中剩余冷却 = k 的牌全体 +1。</summary>
        SlowZone = 4,
        /// <summary>
        /// 雪崩（an）：把<b>双方</b>冷却区中剩余冷却 = A 的牌全体 +1。A = 阈值（本卡为 1）。
        ///
        /// <para><b>2026-10-29 口径变更（全卡表改版）</b>：卡面改成
        /// 「<b>可以选择</b>使你和对方的冷却区中剩余冷却为 1 的法术都被减速」——「可以选择」
        /// 就是<b>可以放弃</b>，于是它重新发一条 <see cref="RequestKind.ChooseZoneValue"/> 决策
        /// （带「跳过」）。这与 2026-09-25 那一版的「强制、不发决策」正好相反，
        /// 两处断言（<c>Tools/RuleSelfTest/AvalancheScenario.cs</c>）必须跟着翻面。</para>
        ///
        /// <para>选项只有一个（阈值由卡表定死，玩家选的不是 k 而是「执不执行」），
        /// 它的 <c>Seat = -1</c> 表示<b>双方</b> —— 与区域加速 / 减速「某一方」的语义不同，
        /// 所以处理器拿到选项后<b>不看 Seat</b>，直接对 <c>TargetSeats</c> 逐个施放。</para>
        /// </summary>
        SlowZoneBoth = 5,
        /// <summary>使目标立即冷却完成（剩余归 0 → 立即回手）。</summary>
        Refresh = 6,
        /// <summary>使目标剩余冷却恢复为基础冷却值。B = 0 可指定任意一方；B = 1 限定对方。</summary>
        ResetCooldown = 7,
        /// <summary>漩涡：把己方冷却区一张牌永久移出游戏（可选），随后获得 A 次加速。</summary>
        RemoveFromGame = 8,
        /// <summary>瀑流：己方每损失 1 点生命值（相较各角色本局初始生命），加速一张<em>不同</em>的牌。A = 每次的点数。</summary>
        HastePerHpLoss = 9,

        // ── 力量类（结算第 ①② 步）───────────────────────────
        /// <summary>本次进攻力量 +A。</summary>
        AtkPlus = 10,
        /// <summary>本次防御力量 +A。</summary>
        DefPlus = 11,
        /// <summary>己方冷却区每有一张牌，本次进攻力量 +A（不含正在打出的这张）。</summary>
        AtkPlusPerCooling = 12,
        /// <summary>双方每合计损失 1 点生命值（相较各角色本局初始生命），本次进攻力量 +A。</summary>
        AtkPlusPerHpLoss = 13,
        /// <summary>本牌获得的<em>全部来源</em>额外进攻力量翻倍。</summary>
        DoubleAtkBonus = 14,
        /// <summary>本牌的进攻力量不可增加（沉重打击）。</summary>
        NoAtkBuff = 15,

        // ── 攻防结构类 ───────────────────────────────────────
        /// <summary>连击：本次进攻结算完后追加一次完整进攻。</summary>
        Combo = 16,
        /// <summary>双发：对方必须交出两张牌才能挡住。</summary>
        Double = 17,
        /// <summary>守护：无视力量差异必定挡住，只挡 1 次攻击。</summary>
        Guard = 18,
        /// <summary>快速回填：本牌进入冷却区时剩余冷却额外 −1。</summary>
        QuickRefill = 19,
        /// <summary>对方无法防御时，本牌冷却额外 −A。</summary>
        CoolMinusIfUnblocked = 20,
        /// <summary>对方无法防御时，获得 A 次减速（目标由使用者指定任意一方）。</summary>
        SlowIfUnblocked = 21,
        /// <summary>对方无法防御时，额外造成 A 点伤害、B 点生命上限削减。</summary>
        ExtraDamageIfUnblocked = 22,

        // ── 光环 ─────────────────────────────────────────────
        /// <summary>结算第 ⑤ 步：本牌进入冷却区时获得一枚光环指示物。配合 <see cref="EffectDef.Aura"/>。</summary>
        Aura = 23,

        // ── 手牌操作 ─────────────────────────────────────────
        /// <summary>把手中其它法术送入冷却，每冷却一张本次进攻力量 +A（磁暴）。</summary>
        CoolHandForAtk = 24,
        /// <summary>把手中其它法术送入冷却，冷却 ≥1 张即获得「连击」（电弧）。</summary>
        CoolHandForCombo = 25,
        /// <summary>把手中其它法术送入冷却，每冷却一张获得一次加速（充能）。</summary>
        CoolHandForHaste = 26,

        // ── 情报 ─────────────────────────────────────────────
        /// <summary>
        /// 随机查看对方一张手牌，满足阈值则立即进入冷却。
        /// A = 阈值；B = 方向（<see cref="EffectDef.LookAtLeast"/> / <see cref="EffectDef.LookAtMost"/>）；
        /// C = 进入冷却时的额外冷却修正（蘑菇 −1、雷云 0）。
        /// </summary>
        LookAndCool = 27,

        // ── 生命 ─────────────────────────────────────────────
        /// <summary>恢复 A 点生命值，然后生命上限 −B，最后 Hp = min(Hp, MaxHp)。B 为 0 时只回血。</summary>
        HealMinusMax = 28,

        // ── 复制 ─────────────────────────────────────────────
        /// <summary>
        /// 模仿：复制己方冷却区中「基础冷却 <b>≤ A</b> 且无光环」的一张牌的基础力量与全部进攻效果。
        /// （2026-09-25 由「基础冷却 = A」放宽为「≤ A」。）
        /// </summary>
        Copy = 29,

        // ── 特殊 ─────────────────────────────────────────────
        /// <summary>潮汐 γ：本牌冷却完毕回手时，可另选一张牌使其立即冷却完成。</summary>
        ReadyRefresh = 30,

        // ── 带条件的冷却类 ───────────────────────────────────
        /// <summary>
        /// 地震：<b>本牌是「你的最后两张手牌」之一</b>时才生效的减速（A = 次数）。
        ///
        /// <para><b>条件口径</b>：出牌那一刻手上共 2 张（本牌 + 另一张），也就是
        /// <see cref="PlayerState.Hand"/> 在出牌之后只剩 1 张。条件不满足时<b>整条效果无事发生</b>
        /// —— 不弹决策、不消耗任何东西。</para>
        ///
        /// <para>为什么不复用 <see cref="Slow"/> + 一个条件字段：本作的「算子即规则」口径下，
        /// 同一个算子不允许有两种触发条件（<see cref="SlowIfUnblocked"/> 当初也是这么做出来的）。
        /// 归类、决策发放与 <see cref="Slow"/> 完全一致，唯一差别就是进门那道判断。</para>
        /// </summary>
        SlowIfLastTwoHand = 31,

        // ── 状态类（2026-09-29）───────────────────────────────
        /// <summary>
        /// 毒刺（ao）：使<b>被攻击的目标</b>获得 A 层虚弱。
        ///
        /// <para><b>目标必须是 Opponent，不能用默认的 Participants</b>：虚弱是「使用者施加给
        /// 被攻击目标的」，在 1v1 下 <c>Participants</c> 会把施法者自己也一起削
        /// （<see cref="BattleState"/> 的 <c>Mode.EffectSeats</c> 口径）。</para>
        ///
        /// <para><b>结算阶段 = ① 力量阶段</b>，与其它 α 即时效果同拍。这与「只削进攻力量」
        /// 是配套的：本次防御比拼用的是<b>打出者自己</b>的力量，不受刚挂上的虚弱影响 ——
        /// 虚弱要到<b>目标下一次进攻</b>才真正咬人。</para>
        ///
        /// <para>无决策、无可放弃、不消耗任何资源，纯状态写入（见
        /// <see cref="PlayerState.WeakenStacks"/>）。</para>
        /// </summary>
        Weaken = 32,

        // ── 2026-10-01 新增（水之形 / 闪电球）──────────────────
        /// <summary>
        /// 水之形（aq）：<b>本卡的「基础力量」永久成长</b>（A = 每次增量；<b>只在本场战斗内有效</b>）。
        ///
        /// <para><b>它和 <see cref="AtkPlus"/> 的区别是「落在哪儿」</b>：AtkPlus 改的是
        /// <see cref="AttackContext"/> 上的<b>本次</b>增量（攻防结束即蒸发）；本算子写的是
        /// <see cref="CardInstance.BattlePowerBonus"/> —— <b>挂在牌实例上的持久成长</b>，
        /// 进冷却区、回手都不会清（<see cref="CardInstance.ToHand"/> 只是按
        /// <c>Def.Power + BattlePowerBonus</c> 重算有效力量）。</para>
        ///
        /// <para><b>「进攻后 / 防御后」是自然得到的，不需要额外时机</b>：本算子归
        /// <see cref="EffectStage.Power"/>，α 在 ① 力量阶段结算、β 被
        /// <c>StageOf</c> 重定向到 ② 防御阶段。两处都<b>晚于</b>本次攻防的力量快照与比拼
        /// —— 进攻侧的 <c>AttackContext.BasePower</c> 在选牌那一刻就取好了、
        /// 防御侧的 <c>DefenseResolver</c> 在提交那一刻就算完了，所以成长只对
        /// <b>以后</b>的攻击 / 防御生效，正好是卡面写的「每次进攻 / 防御后」。</para>
        ///
        /// <para>⚠ <b>力量视为 X 的牌（模仿 x）不吃成长</b>：它的力量由 <see cref="Copy"/>
        /// 临时接管、卡面恒定「未复制时视为 1」，写进成长会与卡面直接矛盾。</para>
        ///
        /// <para>无决策、无可放弃，纯写入（另发一条 <c>BasePowerGrownEvent</c> 便于回放）。</para>
        /// </summary>
        GrowBasePower = 33,

        /// <summary>
        /// 闪电球（ar）：<b>只有「这是你的最后一张手牌」时才获得「连击」</b>。
        ///
        /// <para>与 <see cref="SlowIfLastTwoHand"/> 同一类：<b>同一个算子不允两种触发条件</b>，
        /// 所以条件连击是独立算子。构造时会自动挂上 <c>hand-at-play = 1</c> 条件
        /// （<see cref="PlayerState.Hand"/> 在出牌之后为空），条件不满足则<b>整条不结算</b>
        /// —— 不弹决策、不消耗任何东西。</para>
        /// </summary>
        ComboIfLastHand = 34,

        // ── 2026-10-07 新增（海啸 au）──────────────────────────
        /// <summary>
        /// 海啸（au）：β「防御时增加<b>等于手牌数</b>的力量值」。
        ///
        /// <para>A = 每张手牌的加值（本卡为 1）。加值 = <b>A × 手牌数</b>，
        /// 口径见 <c>DefenseResolver.DefendBonus(card, handCount)</c> ——
        /// 手牌数取<b>防御方出牌前</b>的手牌张数（<b>含正在打出的这一张</b>，
        /// 用户 2026-10-07 拍板）。</para>
        ///
        /// <para><b>为什么不复用 <see cref="DefPlus"/></b>：那个的 A 是一个静态常量
        /// （潮汐 / 地动波的 +1）。本作「算子即规则」的口径下，<b>同一个算子不允许两种取值方式</b>
        /// —— 一条效果到底加几点必须一眼看得出来（同 <see cref="SlowIfLastTwoHand"/> 的先例）。</para>
        ///
        /// <para>归 <c>EffectStage.Defense</c>：与 <see cref="DefPlus"/> / <see cref="Guard"/>
        /// 一样是「防御侧查询」，真正读它的是 <c>DefenseResolver</c>，处理器是空实现。</para>
        /// </summary>
        DefPlusPerHand = 35,

        // -- 2026-10-29 全卡表改版（44 张）新增 ----------------------
        /// <summary>
        /// 火炬（ap）γ：<b>每当你生命值减少时，此光环将恢复到可用状态</b>。
        ///
        /// <para><b>它不是新的一类光环</b>：火炬的 α 是一条普通的
        /// <see cref="AuraKind.AtkPower"/> 光环（打出去点亮 1 枚、用掉就没了）；
        /// 本算子只是给那张牌加一条<b>补货规则</b> —— 拥有者掉血时把指示物补回满。</para>
        ///
        /// <para><b>为什么是一个算子而不是卡牌上的一个布尔</b>：卡面这一行是独立的 γ 效果
        /// （有自己的符号、自己的文案），并且它<b>有自己的触发点</b>（掉血）——
        /// 按本作「算子即规则」的口径，一个触发点一个算子。</para>
        ///
        /// <para><b>触发方式</b>：<see cref="EffectTrigger.Special"/> +
        /// <c>specialEvent = "owner.hp.lost"</c>（与潮汐 γ 的 <c>"cooldown.completed"</c>
        /// 同一条管线）。<c>BattleEngine.ApplyDamage</c> / <c>CutMaxHp</c> 在真正扣血之后
        /// 把该座位冷却区的每张牌按这个事件过一遍；处理器只做「补满指示物」。</para>
        /// </summary>
        RestoreAuraOnHpLoss = 36,

        /// <summary>
        /// 地刺（ar）：「<b>如果这是你手中的最后一张牌，进攻打出之后自动回手</b>」。
        ///
        /// <para>条件由 <see cref="EffectDef"/> 构造时自动挂 <c>hand-at-play = 1</c>
        /// （与 <see cref="ComboIfLastHand"/> / <see cref="SlowIfLastTwoHand"/> 同源，
        /// 阈值 1 = 出牌那一刻手上只有这一张）。条件不满足 → <b>整条不结算</b>，
        /// 不弹决策、不消耗任何东西。</para>
        ///
        /// <para>归 <see cref="EffectStage.AfterCooldown"/>：第 4 步牌已经进了冷却区，
        /// 此时把剩余冷却刷到 0 走的就是「冷却完毕回手」那条现成路径
        /// （<c>CooldownOps.Refresh</c> -> <c>ReturnToHand</c>），
        /// 所以光环指示物作废、刻印熄灭、<c>cooldown.completed</c> 事件全都自动成立。</para>
        /// </summary>
        ReturnToHandIfLastHand = 37,

        /// <summary>
        /// <b>纯卡面声明</b>：有这一行文字、但<b>不产生任何结算</b>（处理器是空实现）。
        ///
        /// <para>用在「卡面要单独占一行、但规则上由别的算子兑现」的说明性文字上 ——
        /// 目前只有模仿（x）的 γ「未复制或防御时力量视为 1」：那条规则的落点是
        /// <see cref="Copy"/>（未复制 -> 不改 <c>EffectivePower</c>）与
        /// <c>CardInstance.PowerLabelText</c>（隐藏力量恒显示 X），
        /// 卡面这一行只是把它写明白。</para>
        ///
        /// <para>它是 <see cref="EffectTrigger.Special"/> + <c>specialEvent = "rule.note"</c>
        /// —— 那个事件<b>永远没有人发布</b>，所以这条效果永远不会被排进特殊效果队列。
        /// 之所以仍要一个事件 id：<c>EffectRegistry.Validate</c> 硬性要求 γ 效果必须有它
        /// （见 EffectRegistry.cs），没有就是编译期能过、进战斗当场抛。</para>
        /// </summary>
        RuleNote = 38,
    }

    /// <summary>Immutable, serializable effect recipe. Handler identity is independent of card identity.</summary>
    public sealed class EffectDef
    {
        public const int LookAtLeast = 0;
        public const int LookAtMost = 1;
        public readonly EffectTrigger Trigger;
        public readonly string HandlerId;
        public readonly string SpecialEvent;
        public readonly EffectTargetScope Targets;
        public readonly string DistinctTargetGroup;
        public readonly System.Collections.Generic.IReadOnlyList<EffectCondition> Conditions;
        public readonly System.Collections.Generic.IReadOnlyDictionary<string, int> Arguments;
        public readonly AuraKind Aura;

        /// <summary>刻印的重复触发时机（见 <see cref="SigilTiming"/>）；<see cref="SigilTiming.None"/> = 普通效果。</summary>
        public readonly SigilTiming Sigil;

        public readonly string Text;

        // Legacy card tables and integrations can keep using the operator constructor.
        public readonly EffectOp Op;
        public readonly int A;
        public readonly int B;
        public readonly int C;
        public bool CanSkip { get { return !Mandatory && (Op == EffectOp.Haste || Op == EffectOp.Slow
            || Op == EffectOp.HasteZone || Op == EffectOp.SlowZone
            || Op == EffectOp.HastePerHpLoss || Op == EffectOp.SlowIfLastTwoHand || Op == EffectOp.SlowIfUnblocked); } }
        public readonly bool Mandatory;

        public EffectDef(EffectTrigger trigger, EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null,
            string distinctTargetGroup = null, System.Collections.Generic.IEnumerable<EffectCondition> conditions = null,
            EffectTargetScope targets = EffectTargetScope.Participants, SigilTiming sigil = SigilTiming.None)
            : this(trigger, op.ToString(), LegacyArguments(op, a, b, c), aura, text,
                // 两个 gamma 算子自带「永远由引擎发的事件 id」（与潮汐的 cooldown.completed 同一套）：
                //   RestoreAuraOnHpLoss -> ApplyDamage / CutMaxHp 扣完血就发；
                //   RuleNote            -> 纯声明，永远没人发（但 Validate 要求 gamma 必须有 id）。
                op == EffectOp.ReadyRefresh ? "cooldown.completed"
                    : op == EffectOp.RestoreAuraOnHpLoss ? "owner.hp.lost"
                    : op == EffectOp.RuleNote ? "rule.note"
                    : null,
                targets, distinctTargetGroup, conditions, mandatory, sigil)
        { }

        public EffectDef(EffectTrigger trigger, string handlerId,
            System.Collections.Generic.IDictionary<string, int> arguments = null,
            AuraKind aura = AuraKind.None, string text = null, string specialEvent = null,
            EffectTargetScope targets = EffectTargetScope.Participants, string distinctTargetGroup = null,
            System.Collections.Generic.IEnumerable<EffectCondition> conditions = null, bool mandatory = false,
            SigilTiming sigil = SigilTiming.None)
        {
            if (!System.Enum.IsDefined(typeof(EffectTrigger), trigger)) throw new System.ArgumentException("Invalid trigger.");
            if (!System.Enum.IsDefined(typeof(SigilTiming), sigil)) throw new System.ArgumentException("Invalid sigil timing.");
            if (string.IsNullOrWhiteSpace(handlerId)) throw new System.ArgumentException("Effect handler ID is required.");
            Trigger = trigger; HandlerId = handlerId; Aura = aura; Sigil = sigil; Text = text ?? string.Empty;
            SpecialEvent = specialEvent ?? string.Empty; Targets = targets; DistinctTargetGroup = distinctTargetGroup ?? string.Empty;
            var values = new System.Collections.Generic.Dictionary<string, int>(arguments ?? new System.Collections.Generic.Dictionary<string, int>());
            Arguments = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(values);
            System.Enum.TryParse(handlerId, out EffectOp legacy);
            Op = legacy;
            A = Arg(PrimaryArgument(legacy)); B = Arg("secondary"); C = Arg("cooldownAdjustment");
            var rules = new System.Collections.Generic.List<EffectCondition>(conditions ?? new EffectCondition[0]);
            if (legacy == EffectOp.SlowIfLastTwoHand && !rules.Exists(r => r.Id == "hand-at-play"))
                rules.Add(new EffectCondition("hand-at-play", 2));
            if (legacy == EffectOp.SlowIfUnblocked && !rules.Exists(r => r.Id == "unblocked"))
                rules.Add(new EffectCondition("unblocked"));
            // 闪电球（ar）：条件连击 = 「出牌那一刻手上只有这一张」。口径与 SlowIfLastTwoHand
            // 同源（HandCountAtPlay 已经含正在打出的这一张），只是阈值从 2 变成 1。
            if (legacy == EffectOp.ComboIfLastHand && !rules.Exists(r => r.Id == "hand-at-play"))
                rules.Add(new EffectCondition("hand-at-play", 1));
            // 地刺（ar）：条件回手 = 「出牌那一刻手上只有这一张」。口径与闪电球完全同源
            // （HandCountAtPlay 已经含正在打出的这一张），只是后果从「获得连击」换成「打完回手」。
            if (legacy == EffectOp.ReturnToHandIfLastHand && !rules.Exists(r => r.Id == "hand-at-play"))
                rules.Add(new EffectCondition("hand-at-play", 1));
            if (rules.Contains(null)) throw new System.ArgumentException("Null effect condition.");
            Conditions = rules.AsReadOnly();
            Mandatory = mandatory || !(legacy == EffectOp.Haste || legacy == EffectOp.Slow
                || legacy == EffectOp.HasteZone || legacy == EffectOp.SlowZone
                || legacy == EffectOp.HastePerHpLoss || legacy == EffectOp.SlowIfLastTwoHand || legacy == EffectOp.SlowIfUnblocked);
        }
        public int Arg(string name, int fallback = 0) { return Arguments.TryGetValue(name, out int value) ? value : fallback; }

        /// <summary>
        /// 某个算子的「主参数」在 <see cref="Arguments"/> 里叫什么名字（<c>count</c> / <c>threshold</c> / <c>amount</c>）。
        ///
        /// <para><b>为什么必须公开</b>：把一张牌的效果**写回资产**（强化卡、动态卡牌）时，
        /// 参数要按名字填。如果按算子乱猜一个键名，<c>A</c> 会静默读成 0
        /// —— 症状是「生成的卡效果栏写着『加速』但一点也不加速」，且不报任何错。</para>
        /// </summary>
        public static string PrimaryArgumentName(EffectOp op)
        {
            return PrimaryArgument(op);
        }

        private static string PrimaryArgument(EffectOp op)
        {
            switch (op)
            {
                case EffectOp.Haste: case EffectOp.Slow: case EffectOp.RemoveFromGame:
                case EffectOp.HastePerHpLoss: case EffectOp.SlowIfUnblocked: case EffectOp.SlowIfLastTwoHand: return "count";
                case EffectOp.SlowZoneBoth: case EffectOp.LookAndCool: case EffectOp.Copy: return "threshold";
                default: return "amount";
            }
        }
        private static System.Collections.Generic.Dictionary<string, int> LegacyArguments(EffectOp op, int a, int b, int c)
        {
            return new System.Collections.Generic.Dictionary<string, int> {
                { PrimaryArgument(op), a }, { "secondary", b }, { "cooldownAdjustment", c }
            };
        }
        public override string ToString() { return Trigger + "/" + HandlerId; }
    }
}
