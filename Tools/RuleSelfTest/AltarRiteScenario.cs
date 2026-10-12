using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-06 · <b>石台（祭坛）强化</b>的定点断言：<b>献祭一张牌，才能力量 +2</b>。
    ///
    /// <para><b>为什么这条线要单独脚本化</b>：与 <see cref="CardUpgradeScenario"/> /
    /// <see cref="WitchWorkshopScenario"/> 同一个理由 —— 它完全发生在战斗之外，
    /// 万局随机对局里<b>一个缺陷也抓不到</b>。而失效方式全是无声的：</para>
    /// <list type="bullet">
    /// <item>卡池门槛写反 → 只剩 1 张牌也弹窗，然后「祭品与目标」永远是同一张；</item>
    /// <item>「两张不能相同」漏判 / 只按卡面 ID 比 → 玩家把同一张牌填进两个位置，
    ///   确认后牌被吃掉、目标却还是原样（<c>a</c> 与 <c>a+</c> 是同一张牌）；</item>
    /// <item>祭品判据照抄女巫的「连锁牌不能献祭」→ 石台里凭空少掉几张可选的祭品
    ///   （用户口径里祭品没有任何限制 —— 它只是被拿走，没有效果要「单独成立」）。</item>
    /// </list>
    ///
    /// <para>断言分四层：① 卡池门槛；② 两个位置的资格（含「同一张牌」按基础 ID 比）；
    /// ③ 确认这一道门 + 产物（力量 +2 / 封顶 9 / 其余字段不动）；
    /// ④ 全表扫描：祭品判据对<b>任何</b>牌都放行。</para>
    /// </summary>
    public static class AltarRiteScenario
    {
        public static bool Run(List<string> report)
        {
            var bad = new List<string>();

            CheckThreshold(bad);
            CheckSlots(bad);
            CheckConfirm(bad);
            CheckWholeTable(bad);

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "石台强化（献祭一张牌才能力量 +2 / 卡池门槛 / 两个位置不能同牌）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        // ── ① 卡池门槛 ──────────────────────────────────────────────

        private static void CheckThreshold(List<string> bad)
        {
            if (AltarRite.MinPoolSize != 2)
            {
                bad.Add("门槛应为 2 张（一张祭品 + 一张目标），实为 " + AltarRite.MinPoolSize);
            }

            if (AltarRite.CanOpen(0) || AltarRite.CanOpen(1))
            {
                bad.Add("卡池 0 / 1 张却判定为能开工（石台要吃掉一张，之后就没目标可强化了）");
            }

            if (!AltarRite.CanOpen(2) || !AltarRite.CanOpen(45))
            {
                bad.Add("卡池 ≥ 2 张应能开工");
            }

            if (string.IsNullOrEmpty(AltarRite.OpenBlockedReason(1)))
            {
                bad.Add("卡池不够时没有给出原因（界面会只把台面关掉、一个字都不说）");
            }
        }

        // ── ② 两个位置各自的资格 ────────────────────────────────────

        private static void CheckSlots(List<string> bad)
        {
            CardDef blaze = CardLibrary.Get("a");        // 暴风雪 力量 4
            CardDef freeze = CardLibrary.Get("c");       // 凝固 力量 3
            CardDef top = CardLibrary.Get("f");          // 滚石冲击 力量 9（不可强化）
            CardDef mimic = CardLibrary.Get("x");        // 模仿 力量 X（不可强化）
            CardDef heavy = CardLibrary.Get("h");        // 沉重打击（力量不能增加）

            string reason;

            // 祭品：任何牌都行 —— 包括上面那三张「不能当目标」的
            if (!AltarRite.CanBeSacrifice(blaze, null, out reason))
            {
                bad.Add("普通牌不能当祭品：" + reason);
            }

            if (!AltarRite.CanBeSacrifice(top, null, out reason)
                || !AltarRite.CanBeSacrifice(mimic, null, out reason)
                || !AltarRite.CanBeSacrifice(heavy, null, out reason))
            {
                bad.Add("力量 9 / X / 沉重打击 应能当祭品（祭品只是被拿走，没有效果要单独成立）");
            }

            if (AltarRite.CanBeSacrifice(null, null, out reason))
            {
                bad.Add("没有牌却判定为可当祭品");
            }

            // 目标：走 CardUpgrade 那一套判据，另加「不能是祭品」
            if (!AltarRite.CanBeTarget(blaze, null, out reason))
            {
                bad.Add("普通牌不能当目标：" + reason);
            }

            if (AltarRite.CanBeTarget(mimic, null, out reason))
            {
                bad.Add("力量为 X 的模仿却判定为可当目标");
            }

            if (AltarRite.CanBeTarget(top, null, out reason))
            {
                bad.Add("力量 9 的牌却判定为可当目标");
            }

            if (AltarRite.CanBeTarget(heavy, null, out reason))
            {
                bad.Add("沉重打击却判定为可当目标");
            }

            if (AltarRite.CanBeTarget(freeze, freeze, out reason))
            {
                bad.Add("祭品与目标同一张牌时，目标判据仍放行");
            }

            // 同一性：基础版与它的 + 版是同一张牌（这一条最容易漏）
            CardDef plus = CardUpgrade.Apply(blaze, 20000);
            if (!AltarRite.SameCard(blaze, plus))
            {
                bad.Add("SameCard 应把 " + blaze.Id + " 与 " + plus.Id + " 判为同一张牌");
            }

            if (AltarRite.SameCard(blaze, freeze) || AltarRite.SameCard(null, blaze))
            {
                bad.Add("SameCard 把两张不同的牌判成了同一张");
            }

            if (AltarRite.CanBeSacrifice(plus, blaze, out reason))
            {
                bad.Add("目标选基础版时，它的强化版仍能当祭品（同一张牌被用了两次）");
            }

            if (AltarRite.CanBeTarget(blaze, plus, out reason))
            {
                bad.Add("祭品选强化版时，它的基础版仍能当目标（同一张牌被用了两次）");
            }
        }

        // ── ③ 确认 + 产物 ───────────────────────────────────────────

        private static void CheckConfirm(List<string> bad)
        {
            CardDef blaze = CardLibrary.Get("a");        // 暴风雪 4 → 6
            CardDef freeze = CardLibrary.Get("c");       // 凝固 3 → 5
            CardDef quake = CardLibrary.Get("i");        // 地震 8 → 9
            CardDef top = CardLibrary.Get("f");          // 力量 9
            CardDef heavy = CardLibrary.Get("h");

            string reason;

            if (AltarRite.CanConfirm(null, blaze, null, out reason)
                || AltarRite.CanConfirm(blaze, null, null, out reason)
                || AltarRite.CanConfirm(null, null, null, out reason))
            {
                bad.Add("两个位置没填满却判定为可确认");
            }

            if (AltarRite.CanConfirm(blaze, blaze, null, out reason))
            {
                bad.Add("祭品与目标同一张牌却判定为可确认（症状：牌没了、力量没加）");
            }

            if (AltarRite.CanConfirm(blaze, top, null, out reason)
                || AltarRite.CanConfirm(blaze, heavy, null, out reason))
            {
                bad.Add("目标不可强化（力量 9 / 沉重打击）却判定为可确认");
            }

            if (!AltarRite.CanConfirm(freeze, blaze, null, out reason))
            {
                bad.Add("普通的「献祭凝固、强化暴风雪」却不可确认：" + reason);
            }

            if (!AltarRite.CanConfirm(heavy, blaze, null, out reason))
            {
                bad.Add("连锁牌（沉重打击）当祭品应放行：" + reason);
            }

            // 产物：力量 +2，封顶 9
            CardUpgradeMod mod = AltarRite.BuildMod();
            if (mod == null || mod.axis != UpgradeAxis.Power || mod.value != CardUpgrade.PowerStep)
            {
                bad.Add("BuildMod 不是「力量 +" + CardUpgrade.PowerStep + "」："
                        + (mod == null ? "null" : mod.Describe()));
            }

            CardDef up = CardUpgrade.Apply(blaze, mod, 20000);
            if (up.Power != blaze.Power + CardUpgrade.PowerStep)
            {
                bad.Add("献祭后力量不符：暴风雪 " + blaze.Power + " → " + up.Power);
            }

            if (up.Id != blaze.Id + CardUpgrade.Suffix || up.ArtId != blaze.ArtId
                || up.Cooldown != blaze.Cooldown || up.Element != blaze.Element)
            {
                bad.Add("献祭后的强化版动了不该动的字段：" + up.Id + " / ArtId " + up.ArtId
                        + " / 冷却 " + up.Cooldown);
            }

            CardDef capped = CardUpgrade.Apply(quake, mod, 20001);
            if (capped.Power != CardUpgrade.PowerCap)
            {
                bad.Add("封顶不生效：地震 8 → " + capped.Power + "（应为 "
                        + CardUpgrade.PowerCap + "）");
            }

            // 被消耗的那张：必须是祭品，不能是目标
            if (!string.Equals(AltarRite.ConsumedId(freeze), freeze.Id, System.StringComparison.Ordinal))
            {
                bad.Add("ConsumedId 返回的不是祭品：" + AltarRite.ConsumedId(freeze));
            }

            if (AltarRite.ConsumedId(null) != null)
            {
                bad.Add("ConsumedId(null) 应为 null");
            }

            // 摘要与预览不能为空（界面直接把它们显示出来）
            if (string.IsNullOrEmpty(AltarRite.ConfirmSummary(freeze, blaze, up)))
            {
                bad.Add("ConfirmSummary 为空");
            }

            if (string.IsNullOrEmpty(AltarRite.Preview(freeze, blaze)))
            {
                bad.Add("Preview 为空");
            }
        }

        // ── ④ 全表扫描：祭品无限制 ──────────────────────────────────

        private static void CheckWholeTable(List<string> bad)
        {
            int upgradable = 0;
            int notUpgradable = 0;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);

                string reason;
                if (!AltarRite.CanBeSacrifice(def, null, out reason))
                {
                    bad.Add("这张牌不能当祭品（石台里祭品不该有任何限制）：" + def.Name
                            + "（" + def.Id + "）—— " + reason);
                }

                if (AltarRite.CanBeTarget(def, null, out reason))
                {
                    upgradable++;
                }
                else
                {
                    notUpgradable++;
                    if (string.IsNullOrEmpty(reason))
                    {
                        bad.Add("不能当目标却没给原因：" + def.Name + "（" + def.Id + "）");
                    }
                }
            }

            // 与 CardUpgradeScenario 同一组数字：47 张里 43 张可强化、4 张不可
            // （2026-10-07 新增的火炬 4 / 海啸 2 都可强化：41 → 43）
            //（模仿 X / 力量 9 两张 / 沉重打击）。这里再钉一遍，因为石台的目标判据
            // 必须与「力量 +2」那条轴完全同源 —— 哪天有人在这里另写一套，立刻变红。
            if (upgradable != 43 || notUpgradable != 4)
            {
                bad.Add("可当目标的张数不符：实为 " + upgradable + " 可 / " + notUpgradable
                        + " 不可，期望 43 / 4（卡表共 " + CardLibrary.Count + " 张）");
            }
        }
    }
}
