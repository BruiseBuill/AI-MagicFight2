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
        /// 取某个时机的符号。<see cref="EffectTrigger.Passive"/>（常驻）**没有符号**，
        /// 返回空串 —— 调用方需自行处理「不留符号位」，不要画成空方块。
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

        /// <summary>常驻：无时机依赖。本批 40 张卡未使用，为后续扩展预留。</summary>
        Passive = 3,
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
        /// <summary>雪崩：同时作用于双方冷却区中剩余冷却 = A 的牌，全体 +1。</summary>
        SlowZoneBoth = 5,
        /// <summary>使目标立即冷却完成（剩余归 0 → 立即回手）。</summary>
        Refresh = 6,
        /// <summary>使目标剩余冷却恢复为基础冷却值。B = 0 可指定任意一方；B = 1 限定对方。</summary>
        ResetCooldown = 7,
        /// <summary>漩涡：把己方冷却区一张牌永久移出游戏（可选），随后获得 A 次加速。</summary>
        RemoveFromGame = 8,
        /// <summary>瀑流：己方每损失 1 点生命值（相较初始值 4），加速一张<em>不同</em>的牌。A = 每次的点数。</summary>
        HastePerHpLoss = 9,

        // ── 力量类（结算第 ①② 步）───────────────────────────
        /// <summary>本次进攻力量 +A。</summary>
        AtkPlus = 10,
        /// <summary>本次防御力量 +A。</summary>
        DefPlus = 11,
        /// <summary>己方冷却区每有一张牌，本次进攻力量 +A（不含正在打出的这张）。</summary>
        AtkPlusPerCooling = 12,
        /// <summary>双方每合计损失 1 点生命值（相较初始值 4），本次进攻力量 +A。</summary>
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
        /// <summary>模仿：复制己方冷却区中「基础冷却 = A 且无光环」的一张牌的基础力量与全部进攻效果。</summary>
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
    }

    /// <summary>效果定义（见 `Docs/engineering/04-架构与接口.md` §2）。</summary>
    public sealed class EffectDef
    {
        /// <summary>LookAndCool 的方向参数：力量 ≥ 阈值。</summary>
        public const int LookAtLeast = 0;

        /// <summary>LookAndCool 的方向参数：力量 ≤ 阈值。</summary>
        public const int LookAtMost = 1;

        public readonly EffectTrigger Trigger;
        public readonly EffectOp Op;

        /// <summary>参数 A：阈值 / 数值 / 次数，逐算子而异。</summary>
        public readonly int A;

        /// <summary>参数 B：方向 / 第二数值 / 目标范围。</summary>
        public readonly int B;

        /// <summary>参数 C：附加修正。</summary>
        public readonly int C;

        /// <summary>当 <see cref="Op"/> 为 <see cref="EffectOp.Aura"/> 时有意义。</summary>
        public readonly AuraKind Aura;

        /// <summary>[强制] 效果不可选择不执行（仅过载 / 自燃的「恢复 1 点、上限 −1」）。</summary>
        public readonly bool Mandatory;

        /// <summary>卡面显示文本。</summary>
        public readonly string Text;

        public EffectDef(
            EffectTrigger trigger,
            EffectOp op,
            int a = 0,
            int b = 0,
            int c = 0,
            AuraKind aura = AuraKind.None,
            bool mandatory = false,
            string text = null)
        {
            Trigger = trigger;
            Op = op;
            A = a;
            B = b;
            C = c;
            Aura = aura;
            Mandatory = mandatory;
            Text = text ?? string.Empty;
        }

        /// <summary>本效果是否需要外部决策（供引擎判断是否走「暂停-恢复」）。</summary>
        public bool NeedsDecision
        {
            get
            {
                switch (Op)
                {
                    case EffectOp.Haste:
                    case EffectOp.Slow:
                    case EffectOp.HasteZone:
                    case EffectOp.SlowZone:
                    case EffectOp.SlowZoneBoth:
                    case EffectOp.Refresh:
                    case EffectOp.ResetCooldown:
                    case EffectOp.RemoveFromGame:
                    case EffectOp.HastePerHpLoss:
                    case EffectOp.CoolHandForAtk:
                    case EffectOp.CoolHandForCombo:
                    case EffectOp.CoolHandForHaste:
                    case EffectOp.Copy:
                    case EffectOp.SlowIfUnblocked:
                    case EffectOp.SlowIfLastTwoHand:
                    case EffectOp.ReadyRefresh:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public override string ToString()
        {
            return Trigger + "/" + Op + "(A=" + A + ",B=" + B + ",C=" + C + ")";
        }
    }
}
