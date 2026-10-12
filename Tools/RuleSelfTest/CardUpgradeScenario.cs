using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-01 · <b>卡牌强化</b>（基础力量 +2 / 封顶 9）的定点断言。
    ///
    /// <para><b>为什么这条线要单独脚本化</b>：强化发生在战斗之外（强化场景），
    /// 完全不进万局随机对局 —— 万局统计 <b>一个缺陷也抓不到</b>。
    /// 而它的失效方式全是无声的：判据写错 → 界面上一张牌能点但结果不变；
    /// 封顶写成 「> 9 才拒绝」→ 8 变成 10（超上限，卡面写着 9 却是 10）；
    /// 卡名后缀用 <c>+=</c> 而不是去重 → 反复强化出 <c>暴风雪++</c>。</para>
    ///
    /// <para>断言分三层：① 逐张牌扫全表，判据与「+2 封顶 9」逐项对齐；
    /// ② 抽几个具体的力量档（7 / 8 / 9 / X / 沉重打击）钉死边界；
    /// ③ 反复强化不叠加后缀、且除力量与卡名之外**一个字段都不许动**。</para>
    /// </summary>
    public static class CardUpgradeScenario
    {
        public static bool Run(List<string> report)
        {
            var bad = new List<string>();

            CheckBoundaries(bad);
            CheckWholeTable(bad);
            CheckRepeated(bad);
            CheckImmutability(bad);
            CheckAlreadyUpgraded(bad);

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "卡牌强化（基础力量 +2 / 封顶 9 / X 与沉重打击不可强化）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        // ── ① 边界档 ────────────────────────────────────────────────

        private static void CheckBoundaries(List<string> bad)
        {
            // 普通牌：+2
            ExpectApply(bad, "c", 5);                 // 凝固 3 → 5
            ExpectApply(bad, "a", 6);                 // 暴风雪 4 → 6

            // 封顶：7 → 9（正好 +2）、8 → 9（只 +1）
            ExpectApply(bad, "d", 9);                 // 寒流 7 → 9
            ExpectApply(bad, "i", 9);                 // 地震 8 → 9

            // 力量 9：不可强化（i 是 8，f/ag 里 f = 滚石冲击 9）
            ExpectReject(bad, "f", "力量 9 的牌不可强化");

            // X（模仿）：不可强化
            ExpectReject(bad, "x", "力量为 X 的牌不可强化");

            // 沉重打击 h：卡面带「进攻力量不能增加」→ 不可强化
            ExpectReject(bad, "h", "沉重打击不可强化");
        }

        // ── ② 全表扫描 ──────────────────────────────────────────────

        private static void CheckWholeTable(List<string> bad)
        {
            int upgradable = 0;
            int rejected = 0;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                string reason;
                bool can = CardUpgrade.CanUpgrade(def, out reason);

                bool expected = !def.HiddenPower && !def.ForbidsAtkBuff && def.Power < CardUpgrade.PowerCap;
                if (can != expected)
                {
                    bad.Add("判据不符：" + def.Name + "（" + def.Id + "）力量 " + def.PowerText
                            + " HiddenPower=" + def.HiddenPower + " ForbidsAtkBuff=" + def.ForbidsAtkBuff
                            + " → CanUpgrade=" + can + "，应为 " + expected);
                    continue;
                }

                if (!can)
                {
                    rejected++;
                    if (string.IsNullOrEmpty(reason))
                    {
                        bad.Add("拒绝时没有给原因：" + def.Name + "（" + def.Id + "）—— 界面会显示一张灰卡却写不出为什么");
                    }

                    continue;
                }

                upgradable++;
                int expectedPower = def.Power + CardUpgrade.PowerStep;
                if (expectedPower > CardUpgrade.PowerCap)
                {
                    expectedPower = CardUpgrade.PowerCap;
                }

                CardDef up = CardUpgrade.Apply(def, 20000 + i);
                if (up.Power != expectedPower)
                {
                    bad.Add("强化后力量不符：" + def.Name + " " + def.Power + " → " + up.Power
                            + "（应为 " + expectedPower + "）");
                }

                if (up.Power > CardUpgrade.PowerCap)
                {
                    bad.Add("强化后超过上限：" + def.Name + " → " + up.Power);
                }
            }

            // 47 张里不可强化的是 4 张：模仿 X / 力量 9 的两张 / 沉重打击（NoAtkBuff）。
            // 这条数字断言的作用与 CardLibrary.ValidatePowerDistribution 同级 ——
            // 卡表加一张「力量 9」或「不能增加力量」的牌时，这里会立刻变红。
            // （2026-10-01 新增的三张 水之形 1 / 闪电球 7 / 冷冻核心 6 都可强化：38 → 41；
            //   2026-10-07 新增的火炬 4 / 海啸 2 也都可强化：41 → 43。）
            const int expectedUpgradable = 43;
            const int expectedRejected = 4;
            if (upgradable != expectedUpgradable || rejected != expectedRejected)
            {
                bad.Add("可强化张数不符：实为 " + upgradable + " 可 / " + rejected + " 不可，期望 "
                        + expectedUpgradable + " / " + expectedRejected
                        + "（卡表共 " + CardLibrary.Count + " 张）");
            }
        }

        // ── ③ 反复强化不叠加后缀 ────────────────────────────────────

        private static void CheckRepeated(List<string> bad)
        {
            CardDef def = CardLibrary.Get("c");                    // 凝固 3

            CardDef once = CardUpgrade.Apply(def, 20000);
            CardDef twice = CardUpgrade.Apply(once, 20001);
            CardDef thrice = CardUpgrade.Apply(twice, 20002);

            if (once.Power != 5 || twice.Power != 7 || thrice.Power != 9)
            {
                bad.Add("连续强化力量不符：" + once.Power + " / " + twice.Power + " / " + thrice.Power
                        + "（应为 5 / 7 / 9）");
            }

            Expect(bad, "反复强化后 ID", thrice.Id, "c+");
            Expect(bad, "反复强化后卡名", thrice.Name, "凝固+");
            Expect(bad, "反复强化后 ArtId", thrice.ArtId, def.ArtId);

            // 已经封顶的牌不能再强化，而且原因要写出来
            string reason;
            if (CardUpgrade.CanUpgrade(thrice, out reason))
            {
                bad.Add("封顶后仍判定可强化：凝固+ 力量 " + thrice.Power);
            }
            else if (string.IsNullOrEmpty(reason))
            {
                bad.Add("封顶被拒时没有给原因");
            }

            // IsUpgraded / BaseId 的口径
            if (!CardUpgrade.IsUpgraded(thrice) || CardUpgrade.IsUpgraded(def))
            {
                bad.Add("IsUpgraded 判定不符：基础版应为 false、强化版应为 true");
            }

            Expect(bad, "BaseId(凝固+)", CardUpgrade.BaseId(thrice), "c");
            Expect(bad, "UpgradedId(凝固)", CardUpgrade.UpgradedId(def), "c+");
        }

        // ── ④ 除力量与卡名，一个字段都不许动 ────────────────────────

        private static void CheckImmutability(List<string> bad)
        {
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                string reason;
                if (!CardUpgrade.CanUpgrade(def, out reason))
                {
                    continue;
                }

                CardDef up = CardUpgrade.Apply(def, 20000 + i);
                string tag = def.Name + "（" + def.Id + "）";

                if (up.Cooldown != def.Cooldown)
                {
                    bad.Add(tag + " 冷却被改动：" + def.Cooldown + " → " + up.Cooldown);
                }

                if (up.Element != def.Element)
                {
                    bad.Add(tag + " 元素被改动：" + def.Element + " → " + up.Element);
                }

                if (up.HiddenPower != def.HiddenPower)
                {
                    bad.Add(tag + " HiddenPower 被改动");
                }

                if (up.ForbidsAtkBuff != def.ForbidsAtkBuff)
                {
                    bad.Add(tag + "「进攻力量不能增加」标记被改动");
                }

                if (up.ArtId != def.ArtId)
                {
                    bad.Add(tag + " ArtId 被改动：" + def.ArtId + " → " + up.ArtId
                            + "（卡面插画是按 ArtId 查的，改了会变成纯色板）");
                }

                if (up.Effects.Count != def.Effects.Count || up.AuraTokenCount != def.AuraTokenCount)
                {
                    bad.Add(tag + " 效果列表被改动：" + def.Effects.Count + " → " + up.Effects.Count
                            + " 条 / 光环 " + def.AuraTokenCount + " → " + up.AuraTokenCount);
                }

                if (up.EffectTextWithSymbols != def.EffectTextWithSymbols)
                {
                    bad.Add(tag + " 效果文案被改动：" + def.EffectTextWithSymbols
                            + " → " + up.EffectTextWithSymbols);
                }
            }
        }

        // ── ⑤ 已经强化过的牌不能再强化（2026-10-08）─────────────────

        /// <summary>
        /// 用户 2026-10-08 口径：<b>「进行任何一种强化后的牌，都不能再次进行强化」</b>。
        ///
        /// <para><b>为什么要钉</b>：四根轴（力量 / 冷却 / 词条 / 转移）各有<b>一处</b>判定，
        /// 漏掉任何一处都只表现为「某一类强化能偷偷加成两次」—— 卡面看不出来
        /// （后缀不叠加，强化十次也还是 <c>a+</c>），引擎一个字也不报。</para>
        ///
        /// <para>用的样本是<b>没封顶</b>的强化版（暴风雪 4 → 6，离上限 9 还远）：
        /// 这样「被拒」只可能来自新规则，而不是既有那条「力量已达上限」——
        /// 否则这条断言在回归里会退化成「什么都没测」。</para>
        /// </summary>
        private static void CheckAlreadyUpgraded(List<string> bad)
        {
            CardDef blaze = CardLibrary.Get("a");                  // 暴风雪 4（未封顶）
            CardDef upgraded = CardUpgrade.Apply(blaze, 20000);    // 暴风雪+ 力量 6
            string reason;

            if (!CardUpgrade.IsUpgraded(upgraded) || upgraded.Power >= CardUpgrade.PowerCap)
            {
                bad.Add("样本不合格：应是一张「已经强化过、但没封顶」的牌");
                return;
            }

            // 基础版照旧能强化（防止新规则把所有人都挡了）
            if (!CardUpgrade.CanUpgrade(blaze, out reason))
            {
                bad.Add("基础版被新规则误挡：" + reason);
            }

            // 四个轴逐一：都必须拒绝，而且原因要是新规则那句话（界面直接显示它）
            if (CardUpgrade.CanUpgrade(upgraded, out reason))
            {
                bad.Add("强化版仍能再强化力量（新规则没生效）");
            }
            else if (reason != CardUpgrade.AlreadyUpgradedReason)
            {
                bad.Add("力量轴拒绝的原因不是「已经是强化过的牌」：" + reason);
            }

            if (CardUpgrade.CanUpgradeCooldown(upgraded, out reason))
            {
                bad.Add("强化版仍能再强化冷却");
            }

            if (CardUpgrade.CanApplyEffect(upgraded, UpgradeTraits.Haste, null, out reason))
            {
                bad.Add("强化版仍能再加词条");
            }

            EffectSpec spec = WitchWorkshop.BuildSpec(CardLibrary.Get("c"), 0);
            if (spec == null)
            {
                bad.Add("样本不合格：凝固（c）第 1 条效果取不到，转移轴这条测不了");
            }
            else if (CardUpgrade.CanTransfer(upgraded, spec, out reason))
            {
                bad.Add("强化版仍能再接收一条转移效果");
            }

            // 决策入口那一道门（CanApply 是四处判定的分发点，界面 / AI 都走它）
            if (CardUpgrade.CanApply(upgraded, CardUpgradeMod.Power(CardUpgrade.PowerStep), null, out reason)
                || CardUpgrade.CanApply(upgraded, CardUpgradeMod.Cooldown(-1), null, out reason)
                || CardUpgrade.CanApply(upgraded, CardUpgradeMod.Effect(UpgradeTraits.Haste), null, out reason))
            {
                bad.Add("CanApply 仍放行强化版的某一根轴");
            }

            if (spec != null
                && CardUpgrade.CanApply(upgraded, CardUpgradeMod.Transfer(spec), null, out reason))
            {
                bad.Add("CanApply 仍放行强化版的转移轴");
            }

            // 反向：**祭品**资格不受影响（「已经强化过的牌照样能献祭」是既有口径）——
            // 别把「不能当目标」误写成「不能用这张牌」。
            if (!AltarRite.CanBeSacrifice(upgraded, null, out reason))
            {
                bad.Add("强化版不能当祭品了（新规则波及过宽）：" + reason);
            }
        }

        // ── 辅助 ────────────────────────────────────────────────────

        private static void ExpectApply(List<string> bad, string id, int expectedPower)
        {
            CardDef def = CardLibrary.Get(id);
            CardDef up = CardUpgrade.Apply(def, 20000);

            if (up.Power != expectedPower)
            {
                bad.Add(def.Name + "（" + id + "）力量 " + def.Power + " → " + up.Power
                        + "，期望 " + expectedPower);
            }

            if (up.Id != id + CardUpgrade.Suffix)
            {
                bad.Add(def.Name + " 强化后 ID 应为 " + id + CardUpgrade.Suffix + "，实为 " + up.Id);
            }

            if (up.Name != def.Name + CardUpgrade.Suffix)
            {
                bad.Add(def.Name + " 强化后卡名应为 " + def.Name + CardUpgrade.Suffix + "，实为 " + up.Name);
            }
        }

        private static void ExpectReject(List<string> bad, string id, string label)
        {
            CardDef def = CardLibrary.Get(id);
            string reason;
            if (CardUpgrade.CanUpgrade(def, out reason))
            {
                bad.Add(label + " —— " + def.Name + "（" + id + "）却判定为可强化");
            }
        }

        private static void Expect(List<string> bad, string label, string actual, string expected)
        {
            if (actual != expected)
            {
                bad.Add(label + " 不符：实为 " + actual + "，期望 " + expected);
            }
        }
    }
}
