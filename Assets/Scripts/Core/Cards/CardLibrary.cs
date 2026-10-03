using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 45 张卡的静态定义表（a–as）。
    ///
    /// 录入依据：`Docs/rules/02-卡牌图鉴.md`。任何数值改动只改这一处。
    /// 校验钩子：<see cref="ValidatePowerDistribution"/> / <see cref="ValidateCooldownDistribution"/>
    /// —— M11 自测会调用它们做「力量分布 = 1:4 / 2:4 / 3:6 / 4:5 / 5:6 / 6:6 / 7:7 / 8:4 / 9:2 / X:1」的断言。
    /// </summary>
    public static class CardLibrary
    {
        private static readonly List<CardDef> Defs = new List<CardDef>();
        private static readonly Dictionary<string, CardDef> ById = new Dictionary<string, CardDef>(StringComparer.Ordinal);

        /// <summary>全部 40 张卡，按卡表顺序（a → an）。</summary>
        public static IReadOnlyList<CardDef> All
        {
            get { return Defs; }
        }

        public static int Count
        {
            get { return Defs.Count; }
        }

        static CardLibrary()
        {
            // 元素（第 2 个参数组）按卡名 + 卡面美术的视觉主题归类（用户 2026-09-26 口径）。
            // ⚠ 它不参与规则结算，只供「点击怪物 → 思考框」把预测出的牌翻译成元素符号。

            // ── 冰 / 水 ─────────────────────────────────────────────
            Add(0, "a", "暴风雪", 4, 4, CardElement.Ice,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.SlowZone, text: "区域减速"));

            // ⚠ 2026-10-03（用户口径）：冰风暴的「α 光环：防御力量 +2」直接换成「β 守护」。
            //   它从此**不再是防御光环卡** —— CardRole.HasDefenseAura / DefensePotential 归零，
            //   AI 的「双发流派优先打防御光环卡」与「减速流派的 tier1」都不再把它算进去
            //   （原本它就是靠「区域减速 + 防御光环」被流派 2 排除的，现在这条排除自然成立）。
            Add(1, "b", "冰风暴", 5, 4, CardElement.Ice,
                A(EffectOp.SlowZone, text: "区域减速"),
                D(EffectOp.Guard, text: "守护"));

            Add(2, "c", "凝固", 3, 2, CardElement.Ice,
                A(EffectOp.Slow, 2, text: "减速 ×2"));

            Add(3, "d", "寒流", 7, 3, CardElement.Ice,
                A(EffectOp.ResetCooldown, 0, 1, text: "重置对方一个法术的冷却时间"));

            Add(4, "e", "潮汐", 1, 3, CardElement.Water,
                S(EffectOp.ReadyRefresh, text: "本牌冷却完毕时，使一个法术立即冷却完毕"),
                D(EffectOp.DefPlus, 1, text: "防御时力量 +1"));

            // ⚠ 2026-10-03（用户口径）：滚石冲击移除「β 守护」，只剩 α 加速。
            Add(5, "f", "滚石冲击", 9, 4, CardElement.Stone,
                A(EffectOp.Haste, 1, text: "加速"));

            Add(6, "g", "陨石", 9, 4, CardElement.Stone,
                A(EffectOp.QuickRefill, text: "快速回填"));

            Add(7, "h", "沉重打击", 7, 3, CardElement.Stone,
                A(EffectOp.ExtraDamageIfUnblocked, 1, 1, text: "对方无法防御时，额外减少 1 点生命值和生命上限"),
                A(EffectOp.NoAtkBuff, text: "此法术的进攻力量不能增加"));

            Add(8, "i", "地震", 8, 3, CardElement.Stone,
                A(EffectOp.SlowIfLastTwoHand, 1, text: "若这是你的最后两张手牌，减速"));

            Add(9, "j", "闪电", 8, 4, CardElement.Electric,
                A(EffectOp.Combo, text: "连击"));

            Add(10, "k", "电弧", 3, 2, CardElement.Electric,
                A(EffectOp.CoolHandForCombo, 1, text: "攻击时可将手中一张其它法术进入冷却，获得连击"));

            // ⚠ 2026-09-28（用户口径）：磁暴基础力量 3 → 2（与引雷对调）。注意下面那条
            //   `CoolHandForAtk` 的 A = 3 是「每冷却一张的加值」，与基础力量是两回事，不动。
            //   卡面 `Art/Cards/Card_12_l_磁暴.png` 的力量数字是烘焙在图上的一位数字，
            //   已同步改成 2（见 `Artifacts/backups/2026-09-28-cardface-power/`）。
            Add(11, "l", "磁暴", 2, 3, CardElement.Electric,
                A(EffectOp.CoolHandForAtk, 3, text: "攻击时可将手中其它法术进入冷却，每冷却一张进攻力量 +3"),
                A(EffectOp.Combo, text: "连击"));

            Add(12, "m", "雷鸣", 1, 4, CardElement.Electric,
                A(EffectOp.HasteZone, text: "区域加速"),
                A(EffectOp.Combo, text: "连击"));

            // ⚠ 2026-09-28（用户口径）：引雷基础力量 2 → 3（与磁暴对调）。光环阈值 A = 6
            //   是「被打出那张牌的基础力量上限」，与引雷自身的力量无关，不动。
            Add(13, "n", "引雷", 3, 3, CardElement.Electric,
                A(EffectOp.Aura, 6, aura: AuraKind.Combo, text: "光环：使本次打出的法术（基础力量 ≤6）获得连击"));

            Add(14, "o", "过载", 1, 4, CardElement.Electric,
                A(EffectOp.HealMinusMax, 1, 1, mandatory: true, text: "[强制] 恢复 1 点生命值，减少 1 点生命上限"),
                A(EffectOp.Combo, text: "连击"));

            Add(15, "p", "自燃", 2, 2, CardElement.Fire,
                A(EffectOp.HealMinusMax, 1, 1, mandatory: true, text: "[强制] 恢复 1 点生命值，减少 1 点生命上限"),
                A(EffectOp.HasteZone, text: "区域加速"));

            Add(16, "q", "海涌", 4, 4, CardElement.Water,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.HasteZone, text: "区域加速"));

            Add(17, "r", "水刃", 5, 3, CardElement.Water,
                A(EffectOp.Refresh, text: "使一张法术立即冷却完成"));

            Add(18, "s", "喷泉", 3, 3, CardElement.Water,
                A(EffectOp.HasteZone, text: "区域加速"),
                A(EffectOp.QuickRefill, text: "快速回填"));

            Add(19, "t", "荆棘", 3, 3, CardElement.Grass,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.CoolMinusIfUnblocked, 2, text: "对方无法防御时，此法术冷却时间 −2"));

            Add(20, "u", "飞叶连击", 2, 2, CardElement.Grass,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.DoubleAtkBonus, text: "此法术获得的额外进攻力量翻倍"));

            Add(21, "v", "藤蔓", 5, 3, CardElement.Grass,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.SlowIfUnblocked, 1, text: "对方无法防御时，获得减速"));

            Add(22, "w", "狂躁蘑菇", 2, 3, CardElement.Grass,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.LookAndCool, 7, EffectDef.LookAtLeast, -1,
                    text: "随机查看对方一张手牌，力量 ≥7 则立即进入冷却且冷却时间 −1"));

            // 模仿：卡面是一张「套着别人外衣」的牌，元素按「无属性」处理 —— 归到诅咒系
            // （符号已经切好备用）。⚠ 牌堆里目前没有诅咒系的卡，这一条是唯一的例外。
            Add(23, "x", "模仿", 1, 4, true, CardElement.Curse,
                A(EffectOp.Copy, 3, text: "复制你冷却区中一个「基础冷却 ≤ 3 且无光环」的法术的力量和进攻效果；未复制时力量视为 1"));

            Add(24, "y", "瀑流", 6, 3, CardElement.Water,
                new EffectDef(EffectTrigger.Attack, EffectOp.Haste, 1, text: "加速", distinctTargetGroup: "torrent"),
                new EffectDef(EffectTrigger.Attack, EffectOp.HastePerHpLoss, 1, text: "你每损失 1 点生命值，加速一个不同的法术", distinctTargetGroup: "torrent"));

            Add(25, "z", "地动波", 7, 3, CardElement.Stone,
                A(EffectOp.QuickRefill, text: "快速回填"),
                D(EffectOp.DefPlus, 1, text: "防御时力量 +1"));

            Add(26, "aa", "漩涡", 4, 2, CardElement.Water,
                A(EffectOp.RemoveFromGame, 3, text: "可将冷却区中的一张法术永久移出游戏，获得加速 ×3"));

            Add(27, "ab", "湍流", 5, 3, CardElement.Water,
                A(EffectOp.Haste, 2, text: "加速 ×2"));

            Add(28, "ac", "雪球", 4, 2, CardElement.Ice,
                A(EffectOp.AtkPlusPerCooling, 1, text: "你的冷却区每有一张牌，进攻力量 +1"));

            Add(29, "ad", "雷云", 6, 3, CardElement.Electric,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.LookAndCool, 4, EffectDef.LookAtMost, 0,
                    text: "随机查看对方一张手牌，力量 ≤4 则立即进入冷却"));

            Add(30, "ae", "灼烧", 4, 2, CardElement.Fire,
                A(EffectOp.AtkPlusPerHpLoss, 1, text: "你或对方每损失 1 点生命值，进攻力量 +1"));

            Add(31, "af", "冰封铠甲", 5, 3, CardElement.Ice,
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：防御力量 +2"),
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：防御力量 +2"));

            Add(32, "ag", "淬火", 7, 3, CardElement.Fire,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.Aura, 2, aura: AuraKind.AtkPower, text: "光环：进攻力量 +2"));

            Add(33, "ah", "烈焰斗篷", 5, 3, CardElement.Fire,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.Aura, 3, aura: AuraKind.AtkOrDefPower, text: "光环：进攻力量 +3 或防御力量 +3"));

            Add(34, "ai", "爆燃", 6, 3, CardElement.Fire,
                A(EffectOp.Aura, 4, aura: AuraKind.AtkPower, text: "光环：进攻力量 +4"));

            Add(35, "aj", "火灾", 8, 4, CardElement.Fire,
                A(EffectOp.Aura, 1, aura: AuraKind.AtkPower, text: "光环：进攻力量 +1"),
                A(EffectOp.Aura, 1, aura: AuraKind.AtkPower, text: "光环：进攻力量 +1"));

            Add(36, "ak", "石化", 6, 4, CardElement.Stone,
                A(EffectOp.Aura, 6, aura: AuraKind.ImmuneHigh, text: "光环：免疫力量 ≥6 的攻击（含双发）"),
                D(EffectOp.Guard, text: "守护"));

            Add(37, "al", "石盾", 8, 4, CardElement.Stone,
                A(EffectOp.Aura, 3, aura: AuraKind.ImmuneLow, text: "光环：免疫力量 ≤3 的攻击（含双发）"),
                D(EffectOp.Guard, text: "守护"));

            Add(38, "am", "充能", 7, 3, CardElement.Electric,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.CoolHandForHaste, 1, text: "攻击时可将手中其它法术进入冷却，每冷却一张获得一次加速"));

            // 雪崩（2026-09-25 用户口径）：从「可选的单效果」改成**双效果** ——
            //   ① 强制区域减速：双方冷却区中剩余冷却 = 1 的牌全体 +1。**不给选择**，
            //      所以 SlowZoneBoth 不再发决策（见 EffectDef / BattleEngine）。
            //   ② 快速回填：本牌进冷却区时剩余冷却额外 −1（④ 阶段标记）。
            Add(39, "an", "雪崩", 6, 3, CardElement.Ice,
                A(EffectOp.SlowZoneBoth, 1, text: "使双方冷却区中剩余冷却为 1 的法术全部减速"),
                A(EffectOp.QuickRefill, text: "快速回填"));

            // ── 2026-09-29 新增（41–42）──────────────────────────
            // 毒刺（草 · 力量 7 / 冷却 4）：α 给**被攻击的目标**挂 2 层虚弱
            //（只削它的进攻力量：最终力量 ×50% 向上取整，它打完一次进攻后 −1 层）。
            // ⚠ targets 必须是 Opponent —— 默认的 Participants 在 1v1 下会把施法者
            //   自己也一起削（EffectSeats 口径），表现是「打完毒刺自己下回合也变虚」。
            Add(40, "ao", "毒刺", 7, 4, CardElement.Grass,
                A(EffectOp.Weaken, 2, targets: EffectTargetScope.Opponent, text: "使被攻击的目标虚弱 ×2"));

            // 击穿（电 · 力量 3 / 冷却 4）：α 光环（赐予本次打出的牌「快速回填」）+ α 连击。
            // 两条都是 α —— 满足「同一张牌的光环必须同符号」的硬约束（ValidateAuraTriggers）。
            Add(41, "ap", "击穿", 3, 4, CardElement.Electric,
                A(EffectOp.Aura, 1, aura: AuraKind.QuickRefill, text: "光环：使本次打出的法术获得快速回填"),
                A(EffectOp.Combo, text: "连击"));

            // ── 2026-10-01 新增（43–45）──────────────────────────
            // 水之形（水 · 力量 1 / 冷却 2）：α 每次进攻后本卡基础力量 +2、
            // β 每次防御后本卡基础力量 +1。成长写在**这张牌的实例**上
            // （CardInstance.BattlePowerBonus），只在本场战斗内有效 —— 进冷却区 / 回手都不清。
            // 两条效果**必须同用一个算子**（GrowBasePower），由触发符号区分 α / β。
            Add(42, "aq", "水之形", 1, 2, CardElement.Water,
                A(EffectOp.GrowBasePower, 2, text: "每次进攻后，此卡的基础力量 +2（本场战斗内有效）"),
                D(EffectOp.GrowBasePower, 1, text: "每次防御后，此卡的基础力量 +1（本场战斗内有效）"));

            // 闪电球（电 · 力量 7 / 冷却 3）：α 加速；α **只有**「这是你的最后一张手牌」
            // 时才连击（条件由 EffectDef 构造时自动挂 hand-at-play = 1）。
            Add(43, "ar", "闪电球", 7, 3, CardElement.Electric,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.ComboIfLastHand, text: "若这是你的最后一张手牌，连击"));

            // 冷冻核心（冰 · 力量 6 / 冷却 3）：α 减速；α 光环：防御力量 +3。
            Add(44, "as", "冷冻核心", 6, 3, CardElement.Ice,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.Aura, 3, aura: AuraKind.DefPower, text: "光环：防御力量 +3"));
        }

        // ── 查询 ────────────────────────────────────────────────

        public static CardDef Get(string id)
        {
            CardDef def;
            if (id != null && ById.TryGetValue(id, out def))
            {
                return def;
            }

            throw new ArgumentException("未知卡 ID：" + (id ?? "<null>"), "id");
        }

        public static CardDef GetByIndex(int index)
        {
            if (index < 0 || index >= Defs.Count)
            {
                throw new ArgumentOutOfRangeException("index", "卡表序号越界：" + index);
            }

            return Defs[index];
        }

        public static bool TryGet(string id, out CardDef def)
        {
            return ById.TryGetValue(id ?? string.Empty, out def);
        }

        // ── 校验 ────────────────────────────────────────────────

        /// <summary>
        /// 力量分布校验：1:4 / 2:4 / 3:6 / 4:5 / 5:6 / 6:6 / 7:7 / 8:4 / 9:2 / X:1（共 45 张）。
        ///
        /// <para>前 40 张的一组数字来自原始文档、与逐卡录入完全吻合 —— 是最硬的数据断言。
        /// 2026-09-29 新增毒刺（力量 7）/ 击穿（力量 3）之后，两档各 +1
        /// （<b>3:5 → 3:6、7:5 → 7:6</b>，总数 40 → 42）；
        /// 2026-10-01 新增水之形（力量 1）/ 闪电球（力量 7）/ 冷冻核心（力量 6），
        /// 三档各 +1（<b>1:3 → 1:4、7:6 → 7:7、6:5 → 6:6</b>，总数 42 → 45）。
        /// 加卡时忘了改这里，报出来的是「力量分布不符」，看不出问题在新卡上。</para>
        /// </summary>
        public static bool ValidatePowerDistribution(out string report)
        {
            int[] expected = { 4, 4, 6, 5, 6, 6, 7, 4, 2 };
            int[] actual = new int[9];
            int hidden = 0;

            for (int i = 0; i < Defs.Count; i++)
            {
                CardDef d = Defs[i];
                if (d.HiddenPower)
                {
                    hidden++;
                }
                else if (d.Power >= 1 && d.Power <= 9)
                {
                    actual[d.Power - 1]++;
                }
            }

            var diffs = new List<string>();
            for (int p = 1; p <= 9; p++)
            {
                if (actual[p - 1] != expected[p - 1])
                {
                    diffs.Add("力量 " + p + "：期望 " + expected[p - 1] + " 实为 " + actual[p - 1]);
                }
            }

            if (hidden != 1)
            {
                diffs.Add("力量 X：期望 1 实为 " + hidden);
            }

            report = diffs.Count == 0
                ? "力量分布 OK：1:4 2:4 3:6 4:5 5:6 6:6 7:7 8:4 9:2 X:1（共 45 张）"
                : "力量分布不符 → " + string.Join(" / ", diffs.ToArray());
            return diffs.Count == 0;
        }

        /// <summary>
        /// 冷却分布校验：以逐卡数值为准 —— 2:8 / 3:23 / 4:14（共 45 张）。
        /// （原始文档标注为 3:20 / 4:13，相差 1 张，见 `02-卡牌图鉴.md` §统计校验；
        /// 2026-09-29 新增的毒刺 / 击穿都是冷却 4，所以 4:12 → 4:14；
        /// 2026-10-01 新增水之形（冷却 2）/ 闪电球与冷冻核心（冷却 3），2:7 → 2:8、3:21 → 3:23。）
        /// </summary>
        public static bool ValidateCooldownDistribution(out string report)
        {
            int[] expected = { 0, 0, 8, 23, 14 };  // 下标 0..4，只用 2/3/4
            int[] actual = new int[5];
            for (int i = 0; i < Defs.Count; i++)
            {
                int cd = Defs[i].Cooldown;
                if (cd >= 0 && cd < actual.Length)
                {
                    actual[cd]++;
                }
            }

            var diffs = new List<string>();
            for (int cd = 2; cd <= 4; cd++)
            {
                if (actual[cd] != expected[cd])
                {
                    diffs.Add("冷却 " + cd + "：期望 " + expected[cd] + " 实为 " + actual[cd]);
                }
            }

            report = diffs.Count == 0
                ? "冷却分布 OK：2:8 3:23 4:14（共 45 张，以逐卡数值为准）"
                : "冷却分布不符 → " + string.Join(" / ", diffs.ToArray());
            return diffs.Count == 0;
        }

        /// <summary>
        /// 光环触发符号校验（2026-09-20 新增）：<b>同一张牌的光环效果必须同符号</b>。
        ///
        /// <para><b>为什么这是一条硬约束</b>：<see cref="EffectTrigger"/> 决定第 ⑤ 步点亮哪几枚
        /// 指示物（α 只有进攻牌点亮 / β 只有防御牌点亮），而
        /// <see cref="AuraResolver"/> 把「剩余指示物」建模成
        /// <b>光环效果列表的尾部 N 条</b> —— 一张牌上混着 α 与 β 两个光环时，
        /// 「亮了 1 枚」到底是哪一枚就说不清，会静默地给错效果（同型双光环看不出来，
        /// 异型的一混就错）。本批 40 张卡全部同符号，这里把它钉成断言，别等以后踩。</para>
        ///
        /// <para>另一条检查：光环效果必须真的是 <see cref="EffectOp.Aura"/> ——
        /// 「带 <c>AuraKind</c> 但算子不是 <c>Aura</c>」这种录入错会让指示物数算成 0。</para>
        /// </summary>
        public static bool ValidateAuraTriggers(out string report)
        {
            var bad = new List<string>();

            for (int i = 0; i < Defs.Count; i++)
            {
                CardDef d = Defs[i];
                EffectTrigger? seen = null;
                int auras = 0;

                for (int e = 0; e < d.Effects.Count; e++)
                {
                    EffectDef ef = d.Effects[e];

                    if (ef.Aura != AuraKind.None && ef.Op != EffectOp.Aura)
                    {
                        bad.Add(d.Name + "（" + d.Id + "）第 " + e + " 条：填了光环类型但算子是 "
                                + ef.Op + "，不是 Aura");
                    }

                    if (ef.Op != EffectOp.Aura)
                    {
                        continue;
                    }

                    auras++;

                    if (ef.Aura == AuraKind.None)
                    {
                        bad.Add(d.Name + "（" + d.Id + "）第 " + e + " 条：Aura 算子却没有光环类型");
                    }

                    if (seen.HasValue && seen.Value != ef.Trigger)
                    {
                        bad.Add(d.Name + "（" + d.Id + "）的光环效果混了 " + seen.Value + " 与 "
                                + ef.Trigger + " 两个触发符号（第 ⑤ 步无法区分点亮的是哪一枚）");
                    }

                    seen = ef.Trigger;
                }



                // γ 光环的时机由卡面文字各自规定，而第 ⑤ 步只会按「本牌这一拍是攻是防」点亮
                // α / β —— 直接放进来会是一个永远不亮的死效果。本批 40 张卡还没有 γ 光环，
                // 真要用就得先给它单独接一个触发点。
                if (auras > 0 && seen.HasValue && seen.Value == EffectTrigger.Special)
                {
                    bad.Add(d.Name + "（" + d.Id + "）的光环标成了 γ（特殊）—— "
                            + "第 ⑤ 步只按 α / β 点亮，γ 光环需要单独接触发点（尚未实现）");
                }
            }

            report = bad.Count == 0
                ? "光环触发符号 OK：每张牌的光环同符号、且都是 α / β / γ"
                : "光环触发符号不符 → " + string.Join(" / ", bad.ToArray());
            return bad.Count == 0;
        }

        /// <summary>
        /// 元素标注校验（2026-09-26 新增）：<b>不允许有未标注元素的卡</b>。
        ///
        /// <para><b>为什么是硬断言</b>：元素目前唯一的消费者是「点击怪物 → 思考框」——
        /// 它拿到 <see cref="CardElement.None"/> 会<b>静默不显示符号</b>（不兜底成某个元素）。
        /// 也就是说「漏标一张卡」的表现是「玩家点怪物时偶尔什么也不弹」，
        /// 既不报错、也很难复现。在这里钉死比事后排查便宜得多。</para>
        ///
        /// <para>统计口径一并报出来（各系几张），方便对照 <c>02-卡牌图鉴.md</c> 的分配表。</para>
        /// </summary>
        public static bool ValidateElements(out string report)
        {
            var missing = new List<string>();
            int[] counts = new int[CardElementInfo.Count + 1];

            for (int i = 0; i < Defs.Count; i++)
            {
                CardDef d = Defs[i];
                if (d.Element == CardElement.None)
                {
                    missing.Add(d.Name + "（" + d.Id + "）");
                    continue;
                }

                counts[(int)d.Element]++;
            }

            var parts = new List<string>();
            for (int e = 1; e <= CardElementInfo.Count; e++)
            {
                parts.Add(CardElementInfo.DisplayName((CardElement)e) + ":" + counts[e]);
            }

            if (missing.Count > 0)
            {
                report = "元素标注不全 → " + string.Join(" / ", missing.ToArray());
                return false;
            }

            report = "元素标注 OK：" + string.Join(" ", parts.ToArray())
                     + "（共 " + Defs.Count + " 张）";
            return true;
        }

        // ── 构建辅助 ────────────────────────────────────────────

        /// <summary>
        /// α 效果（进攻时）。<paramref name="targets"/> 默认 <see cref="EffectTargetScope.Participants"/>
        /// —— 只有「定向给某一方」的效果（如毒刺的虚弱，必须只给对方）才需要显式传。
        /// </summary>
        private static EffectDef A(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null,
            EffectTargetScope targets = EffectTargetScope.Participants)
        {
            return new EffectDef(EffectTrigger.Attack, op, a, b, c, aura, mandatory, text,
                distinctTargetGroup: null, conditions: null, targets: targets);
        }

        private static EffectDef D(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null,
            EffectTargetScope targets = EffectTargetScope.Participants)
        {
            return new EffectDef(EffectTrigger.Defend, op, a, b, c, aura, mandatory, text,
                distinctTargetGroup: null, conditions: null, targets: targets);
        }

        private static EffectDef S(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null,
            EffectTargetScope targets = EffectTargetScope.Participants)
        {
            return new EffectDef(EffectTrigger.Special, op, a, b, c, aura, mandatory, text,
                distinctTargetGroup: null, conditions: null, targets: targets);
        }

        private static void Add(
            int index, string id, string name, int power, int cooldown,
            params EffectDef[] effects)
        {
            Add(index, id, name, power, cooldown, false, CardElement.None, effects);
        }

        private static void Add(
            int index, string id, string name, int power, int cooldown,
            bool hiddenPower, params EffectDef[] effects)
        {
            Add(index, id, name, power, cooldown, hiddenPower, CardElement.None, effects);
        }

        /// <summary>带元素的重载（2026-09-26）。<paramref name="effects"/> 之前的最后一个参数是元素。</summary>
        private static void Add(
            int index, string id, string name, int power, int cooldown,
            CardElement element, params EffectDef[] effects)
        {
            Add(index, id, name, power, cooldown, false, element, effects);
        }

        /// <summary>带元素 + 隐藏力量的完整重载（模仿用）。</summary>
        private static void Add(
            int index, string id, string name, int power, int cooldown,
            bool hiddenPower, CardElement element, params EffectDef[] effects)
        {
            var def = new CardDef(index, id, name, power, cooldown, effects, hiddenPower,
                null, 1, element);
            if (ById.ContainsKey(id))
            {
                throw new InvalidOperationException("卡 ID 重复：" + id);
            }

            ById.Add(id, def);
            Defs.Add(def);
        }
    }
}
