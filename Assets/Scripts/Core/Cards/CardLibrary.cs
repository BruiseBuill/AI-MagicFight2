using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>44 张卡</b>的静态定义表（a–ar）。
    ///
    /// 录入依据：`Docs/rules/02-卡牌图鉴.md`（2026-10-29 按用户给的
    /// `Resource/MagicFight2/Magic.txt` 整体改版）。任何数值改动只改这一处。
    /// 校验钩子：<see cref="ValidatePowerDistribution"/> / <see cref="ValidateCooldownDistribution"/>
    /// —— M11 自测会调用它们做「力量分布 = 1:3 / 2:4 / 3:6 / 4:6 / 5:6 / 6:6 / 7:5 / 8:4 / 9:3 / X:1」的断言。
    /// </summary>
    public static class CardLibrary
    {
        private static readonly List<CardDef> Defs = new List<CardDef>();
        private static readonly Dictionary<string, CardDef> ById = new Dictionary<string, CardDef>(StringComparer.Ordinal);

        /// <summary>全部 44 张卡，按卡表顺序（a → ar）。</summary>
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
            // ─────────────────────────────────────────────────────────
            // 2026-10-29 · 全卡表改版（用户口径：以 `Resource/MagicFight2/Magic.txt`
            // 为唯一权威）。47 张 → **44 张**：
            //   · 删掉 冷冻核心 as / 海啸 au，火炬 at 改挂到 ap（原「击穿」的编号）；
            //   · 新增 吞食 ad（原 ad 雷云的位子）/ 冰霜核心 ao / 地刺 ar；
            //   · 雷云的 ID 从 ad 挪到 aq（原「水之形」的位子），力量 6/3 → 4/4。
            // 序号 = 卡表顺序 = 卡面文件名里的两位数字（Card_01_a_暴风雪 … Card_44_ar_地刺）。
            // ─────────────────────────────────────────────────────────

            // ── 冰 / 水 ─────────────────────────────────────────────
            Add(0, "a", "暴风雪", 4, 4, CardElement.Ice,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.SlowZone, text: "区域减速"));

            Add(1, "b", "冰风暴", 5, 4, CardElement.Ice,
                A(EffectOp.SlowZone, text: "区域减速"),
                D(EffectOp.Guard, text: "守护"));

            Add(2, "c", "凝固", 3, 2, CardElement.Ice,
                A(EffectOp.Slow, 2, text: "减速 ×2"));

            Add(3, "d", "寒流", 7, 3, CardElement.Ice,
                A(EffectOp.ResetCooldown, 0, 1, text: "重置对方一个法术的冷却时间"));

            Add(4, "e", "潮汐", 1, 3, CardElement.Water,
                S(EffectOp.ReadyRefresh, text: "此法术冷却完毕时，使一个法术也立即冷却完毕"),
                D(EffectOp.DefPlus, 1, text: "防御时，力量 +1"));

            // ── 石 / 土 ─────────────────────────────────────────────
            Add(5, "f", "滚石冲击", 9, 4, CardElement.Stone,
                A(EffectOp.Haste, 1, text: "加速"));

            Add(6, "g", "陨石", 9, 4, CardElement.Stone,
                A(EffectOp.QuickRefill, text: "快速回填"));

            Add(7, "h", "沉重打击", 7, 3, CardElement.Stone,
                A(EffectOp.ExtraDamageIfUnblocked, 1, 1, text: "对方无法防御时，将额外减少 1 点生命值和生命上限"),
                A(EffectOp.NoAtkBuff, text: "此法术的进攻力量不能增加"));

            Add(8, "i", "地震", 8, 3, CardElement.Stone,
                A(EffectOp.SlowIfLastTwoHand, 1, text: "若这是你的最后两张手牌，减速"));

            // ── 电 ──────────────────────────────────────────────────
            Add(9, "j", "闪电", 8, 4, CardElement.Electric,
                A(EffectOp.Combo, text: "连击"));

            Add(10, "k", "电弧", 3, 2, CardElement.Electric,
                A(EffectOp.CoolHandForCombo, 1, text: "攻击时可以将手中一张其它法术进入冷却，获得连击"));

            Add(11, "l", "磁暴", 2, 3, CardElement.Electric,
                A(EffectOp.CoolHandForAtk, 3, text: "攻击时可以将手中其它法术进入冷却，每冷却一张进攻力量 +3"),
                A(EffectOp.Combo, text: "连击"));

            Add(12, "m", "雷鸣", 1, 4, CardElement.Electric,
                A(EffectOp.HasteZone, text: "区域加速"),
                A(EffectOp.Combo, text: "连击"));

            Add(13, "n", "引雷", 3, 3, CardElement.Electric,
                A(EffectOp.Aura, 6, aura: AuraKind.Combo,
                    text: "光环：使你的基础力量 ≤6 的法术获得连击（不能叠加）"));

            Add(14, "o", "过载", 1, 4, CardElement.Electric,
                A(EffectOp.HealMinusMax, 1, 1, mandatory: true, text: "[强制] 恢复 1 点生命值，减少 1 点生命上限"),
                A(EffectOp.Combo, text: "连击"));

            // ── 火 ──────────────────────────────────────────────────
            Add(15, "p", "自燃", 2, 2, CardElement.Fire,
                A(EffectOp.HealMinusMax, 1, 1, mandatory: true, text: "[强制] 恢复 1 点生命值，减少 1 点生命上限"),
                A(EffectOp.HasteZone, text: "区域加速"));

            // ── 水 ──────────────────────────────────────────────────
            Add(16, "q", "海涌", 4, 4, CardElement.Water,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.HasteZone, text: "区域加速"));

            // ⚠ 2026-10-29：水刃力量 5 → 4（新卡表）。
            Add(17, "r", "水刃", 4, 3, CardElement.Water,
                A(EffectOp.Refresh, text: "使一张法术立即冷却完成"));

            Add(18, "s", "喷泉", 3, 3, CardElement.Water,
                A(EffectOp.HasteZone, text: "区域加速"),
                A(EffectOp.QuickRefill, text: "快速回填"));

            // ── 草 ──────────────────────────────────────────────────
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

            // 模仿：卡面力量写 X（HiddenPower），规则上的值恒为 1 —— 复制成功后才由 Copy 接管。
            // γ 那一行是**纯卡面声明**（RuleNote，永不结算）：规则落点在 Copy 与 PowerLabelText。
            Add(23, "x", "模仿", 1, 4, true, CardElement.Curse,
                A(EffectOp.Copy, 3, text: "复制你冷却区中一个「基础冷却 ≤3 且无光环」的法术的力量和进攻效果"),
                S(EffectOp.RuleNote, text: "未复制或防御时力量视为 1"));

            Add(24, "y", "瀑流", 6, 3, CardElement.Water,
                new EffectDef(EffectTrigger.Attack, EffectOp.Haste, 1, text: "加速", distinctTargetGroup: "torrent"),
                new EffectDef(EffectTrigger.Attack, EffectOp.HastePerHpLoss, 1,
                    text: "你每损失 1 点生命值（相较初始值），额外加速一个不同的法术",
                    distinctTargetGroup: "torrent"));

            Add(25, "z", "地动波", 7, 3, CardElement.Stone,
                A(EffectOp.QuickRefill, text: "快速回填"),
                D(EffectOp.DefPlus, 1, text: "防御时，力量 +1"));

            // 漩涡（2026-10-29 全面换掉）：原来是「移出游戏 + 加速 ×3」，现在是
            // 「α 加速 + α 光环：使一个法术获得快速回填」。两条都是 α ——
            // 光环必须与同一张牌上的其它光环同符号（ValidateAuraTriggers）。
            Add(26, "aa", "漩涡", 5, 3, CardElement.Water,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.Aura, 1, aura: AuraKind.QuickRefill, text: "光环：使一个法术获得快速回填"));

            Add(27, "ab", "湍流", 5, 3, CardElement.Water,
                A(EffectOp.Haste, 2, text: "加速 ×2"));

            Add(28, "ac", "雪球", 4, 2, CardElement.Ice,
                A(EffectOp.AtkPlusPerCooling, 1, text: "你的冷却区每有一张牌，进攻力量 +1"));

            // 吞食（原 ad 雷云改名，数值与效果一字未动）：减速 + 看对方 ≤4 的手牌送冷却。
            Add(29, "ad", "吞食", 6, 3, CardElement.Grass,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.LookAndCool, 4, EffectDef.LookAtMost, 0,
                    text: "随机查看对方一张手牌，力量 ≤4 则立即进入冷却"));

            // ── 火 / 石（光环族）────────────────────────────────────
            Add(30, "ae", "灼烧", 4, 2, CardElement.Fire,
                A(EffectOp.AtkPlusPerHpLoss, 1, text: "你或对方每损失 1 点生命值（相较初始值），都会进攻力量 +1"));

            Add(31, "af", "冰封铠甲", 5, 3, CardElement.Ice,
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：使法术附带防御力量 +2"),
                A(EffectOp.Aura, 2, aura: AuraKind.DefPower, text: "光环：使法术附带防御力量 +2"));

            Add(32, "ag", "淬火", 7, 3, CardElement.Fire,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.Aura, 2, aura: AuraKind.AtkPower, text: "光环：使法术附带进攻力量 +2"));

            Add(33, "ah", "烈焰斗篷", 5, 3, CardElement.Fire,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.Aura, 3, aura: AuraKind.AtkOrDefPower, text: "光环：使法术附带进攻力量 +3 或防御力量 +3"));

            Add(34, "ai", "爆燃", 6, 3, CardElement.Fire,
                A(EffectOp.Aura, 4, aura: AuraKind.AtkPower, text: "光环：使法术附带进攻力量 +4"));

            Add(35, "aj", "火灾", 8, 4, CardElement.Fire,
                A(EffectOp.Aura, 1, aura: AuraKind.AtkPower, text: "光环：使法术附带进攻力量 +1"),
                A(EffectOp.Aura, 1, aura: AuraKind.AtkPower, text: "光环：使法术附带进攻力量 +1"));

            Add(36, "ak", "石化", 6, 4, CardElement.Stone,
                A(EffectOp.Aura, 6, aura: AuraKind.ImmuneHigh, text: "光环：免疫力量 ≥6 的攻击（包括双发）"),
                D(EffectOp.Guard, text: "守护"));

            Add(37, "al", "石盾", 8, 4, CardElement.Stone,
                A(EffectOp.Aura, 3, aura: AuraKind.ImmuneLow, text: "光环：免疫力量 ≤3 的攻击（包括双发）"),
                D(EffectOp.Guard, text: "守护"));

            Add(38, "am", "充能", 7, 3, CardElement.Electric,
                A(EffectOp.Haste, 1, text: "加速"),
                A(EffectOp.CoolHandForHaste, 1, text: "攻击时可以将手中其它法术进入冷却，每冷却一张获得一次加速"));

            // 雪崩（2026-10-29）：删掉「快速回填」，只剩可选的双向区域减速
            //（发一条 ChooseZoneValue 决策 + 跳过，见 BuiltinEffects.SlowZoneBoth）。
            Add(39, "an", "雪崩", 6, 3, CardElement.Ice,
                A(EffectOp.SlowZoneBoth, 1,
                    text: "可以选择使你和对方的冷却区中剩余冷却为 1 的法术都被减速"));

            // ── 冰 / 火（新卡）──────────────────────────────────────
            // 冰霜核心（原 ao 毒刺的位子，全新）：α 减速 + α 把「虚弱」挂给对方。
            // `Weakened()` 的口径就是「最终力量 ×50% 向上取整」，与卡面「力量值减半（向上取整）」
            // 同一条规则 —— 所以它复用 Weaken(1)，不再造一个「减半」算子。
            // ⚠ targets 必须显式写 Opponent：默认的 Participants 在 1v1 下会把施法者
            //   自己也一起削（EffectSeats 口径）。
            Add(40, "ao", "冰霜核心", 6, 3, CardElement.Ice,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.Weaken, 1, targets: EffectTargetScope.Opponent,
                    text: "使对方下次进攻时，法术最终的力量值减半（向上取整）"));

            // 火炬（原 ap 击穿的位子，全新）：α 光环：进攻力量 +3；
            // γ 掉血复位（RestoreAuraOnHpLoss，见 BuiltinEffects）。
            Add(41, "ap", "火炬", 3, 4, CardElement.Fire,
                A(EffectOp.Aura, 3, aura: AuraKind.AtkPower, text: "光环：使法术附带进攻力量 +3"),
                S(EffectOp.RestoreAuraOnHpLoss, text: "每当你生命值减少时，此光环将恢复到可用状态"));

            // 雷云（ID 从 ad 挪到 aq，力量 6/3 → 4/4）：减速 + 连击。
            Add(42, "aq", "雷云", 4, 4, CardElement.Electric,
                A(EffectOp.Slow, 1, text: "减速"),
                A(EffectOp.Combo, text: "连击"));

            // 地刺（原 ar 闪电球的位子，全新）：条件回手 —— 手牌只剩这一张时打完自动回手。
            Add(43, "ar", "地刺", 9, 4, CardElement.Stone,
                A(EffectOp.ReturnToHandIfLastHand, text: "如果这是你手中的最后一张牌，进攻打出之后自动回手"));
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
        /// 力量分布校验：1:3 / 2:4 / 3:6 / 4:6 / 5:6 / 6:6 / 7:5 / 8:4 / 9:3 / X:1（共 44 张）。
        ///
        /// <para><b>2026-10-29 全卡表改版</b>：47 → 44 张，这一组数字是从新卡表逐卡数出来的
        /// （删 冷冻核心 6 / 海啸 2 / 攻 4 的火炬，新增 吞食 6 / 冰霜核心 6 / 地刺 9，
        /// 雷云 6 → 4，水刃 5 → 4）。加卡 / 改力量时忘了同步这里，报出来的是
        /// 「力量分布不符」，看不出问题在新卡上 —— 所以这里保留逐档清单。</para>
        /// </summary>
        public static bool ValidatePowerDistribution(out string report)
        {
            int[] expected = { 3, 4, 6, 6, 6, 6, 5, 4, 3 };
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
                ? "力量分布 OK：1:3 2:4 3:6 4:6 5:6 6:6 7:5 8:4 9:3 X:1（共 44 张）"
                : "力量分布不符 → " + string.Join(" / ", diffs.ToArray());
            return diffs.Count == 0;
        }

        /// <summary>
        /// 冷却分布校验：以逐卡数值为准 —— 2:6 / 3:23 / 4:15（共 44 张）。
        ///
        /// <para><b>2026-10-29 全卡表改版</b>：47 → 44 张，从新卡表逐卡数出。
        /// 改冷却时只改这一行 + 上面那段注释，别去动 <c>CardDef.Cooldown</c> 之外的东西。</para>
        /// </summary>
        public static bool ValidateCooldownDistribution(out string report)
        {
            int[] expected = { 0, 0, 6, 23, 15 };  // 下标 0..4，只用 2/3/4
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
                ? "冷却分布 OK：2:6 3:23 4:15（共 44 张，以逐卡数值为准）"
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
            EffectTargetScope targets = EffectTargetScope.Participants,
            SigilTiming sigil = SigilTiming.None)
        {
            return new EffectDef(EffectTrigger.Attack, op, a, b, c, aura, mandatory, text,
                distinctTargetGroup: null, conditions: null, targets: targets, sigil: sigil);
        }

        private static EffectDef D(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null,
            EffectTargetScope targets = EffectTargetScope.Participants,
            SigilTiming sigil = SigilTiming.None)
        {
            return new EffectDef(EffectTrigger.Defend, op, a, b, c, aura, mandatory, text,
                distinctTargetGroup: null, conditions: null, targets: targets, sigil: sigil);
        }

        private static EffectDef S(
            EffectOp op, int a = 0, int b = 0, int c = 0,
            AuraKind aura = AuraKind.None, bool mandatory = false, string text = null,
            EffectTargetScope targets = EffectTargetScope.Participants,
            SigilTiming sigil = SigilTiming.None)
        {
            return new EffectDef(EffectTrigger.Special, op, a, b, c, aura, mandatory, text,
                distinctTargetGroup: null, conditions: null, targets: targets, sigil: sigil);
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
