using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 40 张卡的静态定义表（a–an）。
    ///
    /// 录入依据：`Docs/rules/02-卡牌图鉴.md`。任何数值改动只改这一处。
    /// 校验钩子：<see cref="ValidatePowerDistribution"/> / <see cref="ValidateCooldownDistribution"/>
    /// —— M11 自测会调用它们做「力量分布 = 1:3 / 2:4 / 3:5 / 4:5 / 5:6 / 6:5 / 7:5 / 8:4 / 9:2 / X:1」的断言。
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
            // ── 冰 / 水 ─────────────────────────────────────────────
            Add(0, "a", "暴风雪", 4, 4,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.SlowZone, text: "区域减速"));

            Add(1, "b", "冰风暴", 5, 4,
                A(EffectOp.SlowZone, text: "区域减速"),
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：防御力量 +2"));

            Add(2, "c", "凝固", 3, 2,
                A(EffectOp.Slow, 2, text: "减速 ×2"));

            Add(3, "d", "寒流", 7, 3,
                A(EffectOp.ResetCooldown, 0, 1, text: "重置对方一个法术的冷却时间"));

            Add(4, "e", "潮汐", 1, 3,
                S(EffectOp.ReadyRefresh, text: "本牌冷却完毕时，使一个法术立即冷却完毕"),
                D(EffectOp.DefPlus, 1, text: "防御时力量 +1"));

            Add(5, "f", "滚石冲击", 9, 4,
                A(EffectOp.Haste, 1, text: "加速"),
                D(EffectOp.Guard, text: "守护"));

            Add(6, "g", "陨石", 9, 4,
                A(EffectOp.QuickRefill, text: "快速回填"));

            Add(7, "h", "沉重打击", 7, 3,
                A(EffectOp.ExtraDamageIfUnblocked, 1, 1, text: "对方无法防御时，额外减少 1 点生命值和生命上限"),
                A(EffectOp.NoAtkBuff, text: "此法术的进攻力量不能增加"));

            Add(8, "i", "地震", 8, 3,
                A(EffectOp.SlowIfLastTwoHand, 1, text: "若这是你的最后两张手牌，减速"));

            Add(9, "j", "闪电", 8, 4,
                A(EffectOp.Combo, text: "连击"));

            Add(10, "k", "电弧", 3, 2,
                A(EffectOp.CoolHandForCombo, 1, text: "攻击时可将手中一张其它法术进入冷却，获得连击"));

            Add(11, "l", "磁暴", 3, 3,
                A(EffectOp.CoolHandForAtk, 3, text: "攻击时可将手中其它法术进入冷却，每冷却一张进攻力量 +3"),
                A(EffectOp.Combo, text: "连击"));

            Add(12, "m", "雷鸣", 1, 4,
                A(EffectOp.HasteZone, text: "区域加速"),
                A(EffectOp.Combo, text: "连击"));

            Add(13, "n", "引雷", 2, 3,
                A(EffectOp.Aura, 6, aura: AuraKind.Combo, text: "光环：使本次打出的法术（基础力量 ≤6）获得连击"));

            Add(14, "o", "过载", 1, 4,
                A(EffectOp.HealMinusMax, 1, 1, mandatory: true, text: "[强制] 恢复 1 点生命值，减少 1 点生命上限"),
                A(EffectOp.Combo, text: "连击"));

            Add(15, "p", "自燃", 2, 2,
                A(EffectOp.HealMinusMax, 1, 1, mandatory: true, text: "[强制] 恢复 1 点生命值，减少 1 点生命上限"),
                A(EffectOp.HasteZone, text: "区域加速"));

            Add(16, "q", "海涌", 4, 4,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.HasteZone, text: "区域加速"));

            Add(17, "r", "水刃", 5, 3,
                A(EffectOp.Refresh, text: "使一张法术立即冷却完成"));

            Add(18, "s", "喷泉", 3, 3,
                A(EffectOp.HasteZone, text: "区域加速"),
                A(EffectOp.QuickRefill, text: "快速回填"));

            Add(19, "t", "荆棘", 3, 3,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.CoolMinusIfUnblocked, 2, text: "对方无法防御时，此法术冷却时间 −2"));

            Add(20, "u", "飞叶连击", 2, 2,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.DoubleAtkBonus, text: "此法术获得的额外进攻力量翻倍"));

            Add(21, "v", "藤蔓", 5, 3,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.SlowIfUnblocked, 1, text: "对方无法防御时，获得减速"));

            Add(22, "w", "狂躁蘑菇", 2, 3,
                A(EffectOp.Double, text: "双发"),
                A(EffectOp.LookAndCool, 7, EffectDef.LookAtLeast, -1,
                    text: "随机查看对方一张手牌，力量 ≥7 则立即进入冷却且冷却时间 −1"));

            Add(23, "x", "模仿", 1, 4, true,
                A(EffectOp.Copy, 3, text: "复制你冷却区中一个「基础冷却为 3 且无光环」的法术的力量和进攻效果；未复制时力量视为 1"));

            Add(24, "y", "瀑流", 6, 3,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.HastePerHpLoss, 1, text: "你每损失 1 点生命值，加速一个不同的法术"));

            Add(25, "z", "地动波", 7, 3,
                A(EffectOp.QuickRefill, text: "快速回填"),
                D(EffectOp.DefPlus, 1, text: "防御时力量 +1"));

            Add(26, "aa", "漩涡", 4, 2,
                A(EffectOp.RemoveFromGame, 3, text: "可将冷却区中的一张法术永久移出游戏，获得加速 ×3"));

            Add(27, "ab", "湍流", 5, 3,
                A(EffectOp.Haste, 2, text: "加速 ×2"));

            Add(28, "ac", "雪球", 4, 2,
                A(EffectOp.AtkPlusPerCooling, 1, text: "你的冷却区每有一张牌，进攻力量 +1"));

            Add(29, "ad", "雷云", 6, 3,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.LookAndCool, 4, EffectDef.LookAtMost, 0,
                    text: "随机查看对方一张手牌，力量 ≤4 则立即进入冷却"));

            Add(30, "ae", "灼烧", 4, 2,
                A(EffectOp.AtkPlusPerHpLoss, 1, text: "你或对方每损失 1 点生命值，进攻力量 +1"));

            Add(31, "af", "冰封铠甲", 5, 3,
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：防御力量 +2"),
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：防御力量 +2"));

            Add(32, "ag", "淬火", 7, 3,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.Aura, 2, aura: AuraKind.AtkPower, text: "光环：进攻力量 +2"));

            Add(33, "ah", "烈焰斗篷", 5, 3,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.Aura, 3, aura: AuraKind.AtkOrDefPower, text: "光环：进攻力量 +3 或防御力量 +3"));

            Add(34, "ai", "爆燃", 6, 3,
                A(EffectOp.Aura, 4, aura: AuraKind.AtkPower, text: "光环：进攻力量 +4"));

            Add(35, "aj", "火灾", 8, 4,
                A(EffectOp.Aura, 1, aura: AuraKind.AtkPower, text: "光环：进攻力量 +1"),
                A(EffectOp.Aura, 1, aura: AuraKind.AtkPower, text: "光环：进攻力量 +1"));

            Add(36, "ak", "石化", 6, 4,
                A(EffectOp.Aura, 6, aura: AuraKind.ImmuneHigh, text: "光环：免疫力量 ≥6 的攻击（含双发）"),
                D(EffectOp.Guard, text: "守护"));

            Add(37, "al", "石盾", 8, 4,
                A(EffectOp.Aura, 3, aura: AuraKind.ImmuneLow, text: "光环：免疫力量 ≤3 的攻击（含双发）"),
                D(EffectOp.Guard, text: "守护"));

            Add(38, "am", "充能", 7, 3,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.CoolHandForHaste, 1, text: "攻击时可将手中其它法术进入冷却，每冷却一张获得一次加速"));

            Add(39, "an", "雪崩", 6, 3,
                A(EffectOp.SlowZoneBoth, 1, text: "可使双方冷却区中剩余冷却为 1 的法术都被减速"));
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
        /// 力量分布校验：1:3 / 2:4 / 3:5 / 4:5 / 5:6 / 6:5 / 7:5 / 8:4 / 9:2 / X:1。
        /// 这一组数字来自原始文档，且与本表逐卡录入完全吻合 —— 是最硬的数据断言。
        /// </summary>
        public static bool ValidatePowerDistribution(out string report)
        {
            int[] expected = { 3, 4, 5, 5, 6, 5, 5, 4, 2 };
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
                ? "力量分布 OK：1:3 2:4 3:5 4:5 5:6 6:5 7:5 8:4 9:2 X:1"
                : "力量分布不符 → " + string.Join(" / ", diffs.ToArray());
            return diffs.Count == 0;
        }

        /// <summary>
        /// 冷却分布校验：以逐卡数值为准 —— 2:7 / 3:21 / 4:12。
        /// （原始文档标注为 3:20 / 4:13，相差 1 张，见 `02-卡牌图鉴.md` §统计校验。）
        /// </summary>
        public static bool ValidateCooldownDistribution(out string report)
        {
            int[] expected = { 0, 0, 7, 21, 12 };  // 下标 0..4，只用 2/3/4
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
                ? "冷却分布 OK：2:7 3:21 4:12（以逐卡数值为准）"
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

                if (auras > 0 && seen.HasValue && seen.Value == EffectTrigger.Passive)
                {
                    bad.Add(d.Name + "（" + d.Id + "）的光环标成了 Passive（常驻）—— "
                            + "光环必须写明 α / β / γ 才会被第 ⑤ 步点亮");
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

        // ── 构建辅助 ────────────────────────────────────────────

        private static EffectDef A(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null)
        {
            return new EffectDef(EffectTrigger.Attack, op, a, b, c, aura, mandatory, text);
        }

        private static EffectDef D(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null)
        {
            return new EffectDef(EffectTrigger.Defend, op, a, b, c, aura, mandatory, text);
        }

        private static EffectDef S(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null)
        {
            return new EffectDef(EffectTrigger.Special, op, a, b, c, aura, mandatory, text);
        }

        private static void Add(
            int index, string id, string name, int power, int cooldown,
            params EffectDef[] effects)
        {
            Add(index, id, name, power, cooldown, false, effects);
        }

        private static void Add(
            int index, string id, string name, int power, int cooldown,
            bool hiddenPower, params EffectDef[] effects)
        {
            var def = new CardDef(index, id, name, power, cooldown, effects, hiddenPower);
            if (ById.ContainsKey(id))
            {
                throw new InvalidOperationException("卡 ID 重复：" + id);
            }

            ById.Add(id, def);
            Defs.Add(def);
        }
    }
}
