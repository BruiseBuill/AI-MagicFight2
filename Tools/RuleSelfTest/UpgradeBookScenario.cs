using System;
using System.Collections.Generic;
using System.Reflection;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-02 · <b>多轴强化 + 强化册</b>的定点断言。
    ///
    /// <para><b>为什么这条线要单独脚本化</b>：与 <see cref="CardUpgradeScenario"/> 同一个理由 ——
    /// 强化完全发生在战斗之外（强化场景 / 存档），万局随机对局<b>一个缺陷也抓不到</b>。
    /// 而这一版新引入的失效方式比上一版更多、也更安静：</para>
    /// <list type="bullet">
    /// <item><description>合成时只累加了一部分轴的增量 → 「力量涨了、冷却没降」，界面毫无提示；</description></item>
    /// <item><description>合成时没沿用 <c>ArtId</c> → 强化过的牌变一块纯色板（零报错）；</description></item>
    /// <item><description>卡面 ID 越挂越多（<c>a++</c>）→ 「还是同一张牌」这条口径直接崩；</description></item>
    /// <item><description>光环词条的触发符号跟卡面已有的光环不一致 →
    /// <c>AuraResolver</c> 按「列表尾部 N 条」取剩余指示物，会取到<b>另一条光环</b>，
    /// 表现是「亮了两枚、效果算的是别的那条」，零报错；</description></item>
    /// <item><description>存档类型里出现只读字段 / 属性 / 字典 → <c>JsonUtility</c>
    /// <b>静默丢掉</b>那些字段，存档少一截、也不报错（本文件用反射钉住）。</description></item>
    /// </list>
    ///
    /// <para>断言分七层：① 三轴同时合成；② 同一性（永远只有一个 <c>+</c>）；
    /// ③ 两个上限（力量 9 / 冷却 1，且冷却不得高于基础值）；④ 词条判据（四条拒绝、一条放行）；
    /// ⑤ <b>全表 × 全词条</b>扫描（每条不变量逐张验）；⑥ 强化册叠目录（去重 / 配方优先）；
    /// ⑦ 存档类型的可序列化形状。</para>
    /// </summary>
    public static class UpgradeBookScenario
    {
        public static bool Run(List<string> report)
        {
            var bad = new List<string>();

            CheckMultiAxis(bad);
            CheckIdentity(bad);
            CheckCaps(bad);
            CheckTraits(bad);
            CheckWholeTable(bad);
            CheckCatalogOverlay(bad);
            CheckJsonShape(bad);

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "多轴强化与强化册（力量 / 冷却 / 词条三轴 · 同一性 · 配方叠目录）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        // ── ① 三轴同时合成 ──────────────────────────────────────────

        private static void CheckMultiAxis(List<string> bad)
        {
            CardDef def = CardLibrary.Get("a");                 // 暴风雪 4/4，α加速 + α区域减速
            CardUpgradeRecord record = CardUpgradeRecord.Of("a",
                CardUpgradeMod.Power(2), CardUpgradeMod.Cooldown(-1),
                CardUpgradeMod.Effect(UpgradeTraits.AuraHaste));

            CardDef up = CardUpgrade.Apply(def, record, CardUpgrade.SynthesizedIndexBase);

            Expect(bad, "三轴合成后力量", up.Power.ToString(), (def.Power + 2).ToString());
            Expect(bad, "三轴合成后冷却", up.Cooldown.ToString(), (def.Cooldown - 1).ToString());
            Expect(bad, "三轴合成后光环数", up.AuraTokenCount.ToString(), "1");
            Expect(bad, "三轴合成后效果条数", up.Effects.Count.ToString(), (def.Effects.Count + 1).ToString());
            Expect(bad, "三轴合成后 ID", up.Id, "a+");
            Expect(bad, "三轴合成后卡名", up.Name, def.Name + "+");
            Expect(bad, "三轴合成后 ArtId", up.ArtId, def.ArtId);
            Expect(bad, "三轴合成后元素", up.Element.ToString(), def.Element.ToString());
            Expect(bad, "三轴合成后序号", up.Index.ToString(), CardUpgrade.SynthesizedIndexBase.ToString());

            // 合成的效果文案里必须能看到那条词条的**卡面原文** —— 否则界面会「加了但看不见」
            UpgradeTraits.Trait auraTrait;
            if (UpgradeTraits.TryGet(UpgradeTraits.AuraHaste, out auraTrait)
                && up.EffectTextWithSymbols.IndexOf(auraTrait.Text, StringComparison.Ordinal) < 0)
            {
                bad.Add("合成的卡面文案里没有词条文字「" + auraTrait.Text + "」："
                        + up.EffectTextWithSymbols.Replace("\n", " | "));
            }

            // 配方顺序不影响结果（三轴各自独立）
            CardUpgradeRecord reversed = CardUpgradeRecord.Of("a",
                CardUpgradeMod.Effect(UpgradeTraits.AuraHaste), CardUpgradeMod.Cooldown(-1),
                CardUpgradeMod.Power(2));
            CardDef up2 = CardUpgrade.Apply(def, reversed, CardUpgrade.SynthesizedIndexBase);
            if (up2.Power != up.Power || up2.Cooldown != up.Cooldown
                || up2.EffectTextWithSymbols != up.EffectTextWithSymbols)
            {
                bad.Add("配方顺序影响了合成结果（力量 " + up2.Power + "/" + up.Power
                        + " 冷却 " + up2.Cooldown + "/" + up.Cooldown + "）");
            }

            // 空配方 = 原样返回（不产生「挂了个加号的空壳」）
            CardDef empty = CardUpgrade.Apply(def, new CardUpgradeRecord { baseId = "a" }, 1);
            if (!ReferenceEquals(empty, def))
            {
                bad.Add("空配方不该产生强化版（实为 " + empty.Id + "）");
            }
        }

        // ── ② 同一性 ────────────────────────────────────────────────

        private static void CheckIdentity(List<string> bad)
        {
            CardDef def = CardLibrary.Get("c");                 // 凝固 3/2

            // 反复追加力量（三笔）—— 卡面 ID / 卡名只挂一个 +
            CardUpgradeRecord record = CardUpgradeRecord.Of("c",
                CardUpgradeMod.Power(2), CardUpgradeMod.Power(2), CardUpgradeMod.Power(2));
            CardDef up = CardUpgrade.Apply(def, record, 30000);

            Expect(bad, "反复强化后 ID（只一个 +）", up.Id, "c+");
            Expect(bad, "反复强化后卡名（只一个 +）", up.Name, def.Name + "+");
            Expect(bad, "UpgradedIdOf 幂等", CardUpgrade.UpgradedIdOf(up.Id), "c+");

            string baseId;
            if (!string.Equals(baseId = CardUpgrade.BaseIdOf(up.Id), "c", StringComparison.Ordinal))
            {
                bad.Add("BaseIdOf(" + up.Id + ") 应为 c，实为 " + baseId);
            }

            if (!CardUpgrade.IsUpgraded(up) || CardUpgrade.IsUpgraded(def))
            {
                bad.Add("IsUpgraded 判定不符：基础版应为 false、强化版应为 true");
            }

            // 强化册按「基础 ID 或解析后 ID」都查得到（界面拿到的是 a+）
            UpgradeBook book = UpgradeBook.FromRecords(new List<CardUpgradeRecord> { record });
            CardUpgradeRecord found;
            if (!book.TryGet("c+", out found) || found == null || found.baseId != "c")
            {
                bad.Add("强化册按解析后的 ID（c+）查不到配方");
            }

            // 记录在册子里的 ID 一定被归一过：写 "c+" 进去也必须记成 "c"
            UpgradeBook viaUpgraded = UpgradeBook.FromRecords(
                new List<CardUpgradeRecord> { CardUpgradeRecord.Of("c+", CardUpgradeMod.Power(2)) });
            if (!viaUpgraded.TryGet("c", out found) || found.baseId != "c")
            {
                bad.Add("用解析后的 ID 建册子时没有归一成基础 ID");
            }
        }

        // ── ③ 两个上限 ──────────────────────────────────────────────

        private static void CheckCaps(List<string> bad)
        {
            // 力量封顶 9：寒流 d = 7，两次 +2 只到 9
            CardDef d = CardLibrary.Get("d");
            CardDef dUp = CardUpgrade.Apply(d, CardUpgradeRecord.Of("d",
                CardUpgradeMod.Power(2), CardUpgradeMod.Power(2)), 30000);
            Expect(bad, "力量封顶（7 → ?）", dUp.Power.ToString(), CardUpgrade.PowerCap.ToString());

            if (CardUpgrade.CanUpgradePower(dUp, out _))
            {
                bad.Add("封顶后的牌仍判定为可继续强化力量");
            }

            // 冷却下限 1：凝固 c = 2，两次 −1 只到 1
            CardDef c = CardLibrary.Get("c");
            CardDef cUp = CardUpgrade.Apply(c, CardUpgradeRecord.Of("c",
                CardUpgradeMod.Cooldown(-1), CardUpgradeMod.Cooldown(-1)), 30000);
            Expect(bad, "冷却下限", cUp.Cooldown.ToString(), CardUpgrade.CooldownFloor.ToString());

            // 冷却不得高于基础值（与规则里「减速上限 = 基础冷却值」同口径）
            CardDef cSlow = CardUpgrade.Apply(c, CardUpgradeRecord.Of("c",
                CardUpgradeMod.Cooldown(1)), 30000);
            Expect(bad, "冷却上限（不得超过基础值）", cSlow.Cooldown.ToString(), c.Cooldown.ToString());

            // 力量为 X（模仿 x）：力量轴整体不生效
            CardDef x = CardLibrary.Get("x");
            CardDef xUp = CardUpgrade.Apply(x, CardUpgradeRecord.Of("x", CardUpgradeMod.Power(2)), 30000);
            Expect(bad, "模仿（力量 X）不吃力量强化", xUp.Power.ToString(), x.Power.ToString());
            Expect(bad, "模仿的力量文本", xUp.PowerText, "X");

            // 沉重打击 h：卡面写着进攻力量不能增加 → 力量轴不生效，且进攻光环词条被拒
            CardDef h = CardLibrary.Get("h");
            CardDef hUp = CardUpgrade.Apply(h, CardUpgradeRecord.Of("h", CardUpgradeMod.Power(2)), 30000);
            Expect(bad, "沉重打击不吃力量强化", hUp.Power.ToString(), h.Power.ToString());

            // 冷却轴的判据：冷却已在下限的牌不能再加速。
            // ⚠ 卡表里**一张冷却 1 的牌都没有**（45 张是 2:8 / 3:23 / 4:14），
            //   所以这里用一个合成的定义来钉死这条边界 —— 靠扫卡表会让这条断言永远跑不到。
            var floorDef = new CardDef(0, "test.cooldown1", "测试·冷却1", 3, CardUpgrade.CooldownFloor,
                new List<EffectDef>(), false, "test.cooldown1", 1, CardElement.None);
            if (CardUpgrade.CanUpgradeCooldown(floorDef, out _))
            {
                bad.Add("冷却已在下限（" + CardUpgrade.CooldownFloor + "）却仍判定可加速");
            }

            // 反过来：卡表里只要有冷却 > 1 的牌，就至少要放行一张（判据别整体写反了）
            int upgradableCooldown = 0;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                if (CardUpgrade.CanUpgradeCooldown(CardLibrary.GetByIndex(i), out _))
                {
                    upgradableCooldown++;
                }
            }

            if (upgradableCooldown != CardLibrary.Count)
            {
                bad.Add("卡表里应当每张牌的冷却都能被加速一次，实为 " + upgradableCooldown
                        + " / " + CardLibrary.Count + " 张");
            }
        }

        // ── ④ 词条判据 ──────────────────────────────────────────────

        private static void CheckTraits(List<string> bad)
        {
            CardDef a = CardLibrary.Get("a");                   // 已有 α 加速

            string reason;
            ExpectReject(bad, "未知词条",
                UpgradeTraits.CanApply("no.such.trait", a, null, out reason), reason);

            CardUpgradeRecord withAura = CardUpgradeRecord.Of("a",
                CardUpgradeMod.Effect(UpgradeTraits.AuraHaste));
            ExpectReject(bad, "同一条词条加两次",
                UpgradeTraits.CanApply(UpgradeTraits.AuraHaste, a, withAura, out reason), reason);

            ExpectReject(bad, "卡面本身已有该效果（a 已有加速）",
                UpgradeTraits.CanApply(UpgradeTraits.Haste, a, null, out reason), reason);

            ExpectReject(bad, "沉重打击 + 进攻光环",
                UpgradeTraits.CanApply(UpgradeTraits.AuraAtk, CardLibrary.Get("h"), null, out reason), reason);

            ExpectReject(bad, "力量为 X 的牌 + 任意词条",
                UpgradeTraits.CanApply(UpgradeTraits.Guard, CardLibrary.Get("x"), null, out reason), reason);

            // 放行：给没有光环的牌加防御光环
            if (!UpgradeTraits.CanApply(UpgradeTraits.AuraDef, CardLibrary.Get("c"), null, out reason))
            {
                bad.Add("凝固（无光环）应当能加「" + UpgradeTraits.LabelOf(UpgradeTraits.AuraDef)
                        + "」，却被拒：" + reason);
            }

            // 光环符号必须跟卡面已有的对齐：af 已有 2 条 α 光环，新增的也必须是 α（总数 3）
            // ⚠ 2026-10-03：原来这条用 b 冰风暴（自带 1 条 α 光环）验证，但它现在
            //   光环已改成「守护」、成了无光环牌 —— 改用 af 冰封铠甲，口径更硬。
            CardDef af = CardLibrary.Get("af");
            CardDef afUp = CardUpgrade.Apply(af, CardUpgradeRecord.Of("af",
                CardUpgradeMod.Effect(UpgradeTraits.AuraAtk)), 30000);
            Expect(bad, "三光环总数", afUp.AuraTokenCount.ToString(), "3");
            Expect(bad, "三光环 α 数", afUp.AuraTokenCountOf(EffectTrigger.Attack).ToString(), "3");

            // 无光环牌（b 冰风暴）加光环词条 → 走 AuraTriggerFor 的默认分支 α
            CardDef bUp = CardUpgrade.Apply(CardLibrary.Get("b"), CardUpgradeRecord.Of("b",
                CardUpgradeMod.Effect(UpgradeTraits.AuraAtk)), 30000);
            Expect(bad, "无光环牌加词条后的光环数", bUp.AuraTokenCount.ToString(), "1");
            Expect(bad, "无光环牌加词条的光环符号", bUp.AuraTokenCountOf(EffectTrigger.Attack).ToString(), "1");

            // 词条库自身：ID 唯一、文案非空
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < UpgradeTraits.All.Count; i++)
            {
                UpgradeTraits.Trait trait = UpgradeTraits.All[i];
                if (!seen.Add(trait.Id))
                {
                    bad.Add("词条 ID 重复：" + trait.Id);
                }

                if (string.IsNullOrEmpty(trait.Label) || string.IsNullOrEmpty(trait.Text))
                {
                    bad.Add("词条缺少名称或文案：" + trait.Id);
                }
            }
        }

        // ── ⑤ 全表 × 全词条 ─────────────────────────────────────────

        private static void CheckWholeTable(List<string> bad)
        {
            int composed = 0;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);

                // 每条轴单独试一遍；再叠一条词条
                var mods = new List<CardUpgradeMod> { CardUpgradeMod.Power(2), CardUpgradeMod.Cooldown(-1) };
                for (int t = 0; t < UpgradeTraits.All.Count; t++)
                {
                    string traitId = UpgradeTraits.All[t].Id;
                    string reason;
                    if (!UpgradeTraits.CanApply(traitId, def, null, out reason))
                    {
                        if (string.IsNullOrEmpty(reason))
                        {
                            bad.Add("词条被拒但没给原因：" + def.Name + " × " + traitId);
                        }

                        continue;
                    }

                    var all = new List<CardUpgradeMod>(mods);
                    all.Add(CardUpgradeMod.Effect(traitId));
                    CardDef up = CardUpgrade.Apply(def, CardUpgradeRecord.Of(def.Id, all.ToArray()),
                        CardUpgrade.SynthesizedIndexBase + i);

                    composed++;
                    CheckInvariants(bad, def, up, traitId);
                }
            }

            // 一条兜底：至少要真的合成过一批，否则上面的循环写空了也不会红
            if (composed < 100)
            {
                bad.Add("全表 × 全词条的合成次数过少（" + composed + "）—— 判据可能被意外收紧了");
            }
        }

        /// <summary>合成结果必须满足的全部不变量（每一条都对应一种「零报错」的故障）。</summary>
        private static void CheckInvariants(List<string> bad, CardDef def, CardDef up, string traitId)
        {
            string tag = def.Name + "（" + def.Id + "）× " + traitId + "：";

            if (up.Id != def.Id + "+")
            {
                bad.Add(tag + "ID 应为 " + def.Id + "+，实为 " + up.Id);
            }

            if (CardUpgrade.BaseIdOf(up.Id) != def.Id)
            {
                bad.Add(tag + "同一性丢失 —— BaseIdOf(" + up.Id + ") = " + CardUpgrade.BaseIdOf(up.Id));
            }

            if (up.Name != def.Name + "+")
            {
                bad.Add(tag + "卡名后缀不对：" + up.Name);
            }

            if (up.ArtId != def.ArtId)
            {
                bad.Add(tag + "ArtId 被改动：" + def.ArtId + " → " + up.ArtId
                        + "（卡面插画是按 ArtId 查的，改了会变成纯色板）");
            }

            if (up.Power > CardUpgrade.PowerCap)
            {
                bad.Add(tag + "力量超过上限：" + up.Power);
            }

            if (up.Cooldown < CardUpgrade.CooldownFloor || up.Cooldown > def.Cooldown)
            {
                bad.Add(tag + "冷却越界：" + def.Cooldown + " → " + up.Cooldown);
            }

            if (up.HiddenPower != def.HiddenPower || up.Element != def.Element)
            {
                bad.Add(tag + "HiddenPower / 元素被改动");
            }

            if (up.Effects.Count != def.Effects.Count + 1)
            {
                bad.Add(tag + "效果条数应为 +1，实为 " + def.Effects.Count + " → " + up.Effects.Count);
            }

            // 光环符号一致性（AuraResolver 按「列表尾部 N 条」取剩余指示物，
            // 一张牌出现两种符号的光环时，消耗顺序与卡面符号会错位）
            int attack = up.AuraTokenCountOf(EffectTrigger.Attack);
            int defend = up.AuraTokenCountOf(EffectTrigger.Defend);
            int special = up.AuraTokenCountOf(EffectTrigger.Special);
            int kinds = (attack > 0 ? 1 : 0) + (defend > 0 ? 1 : 0) + (special > 0 ? 1 : 0);
            if (kinds > 1)
            {
                bad.Add(tag + "同一张牌出现了多种符号的光环（α" + attack + " / β" + defend
                        + " / γ" + special + "）");
            }

            if (up.AuraTokenCount != def.AuraTokenCount + 1 && UpgradeTraits.TryGet(traitId, out UpgradeTraits.Trait t)
                && t.IsAura)
            {
                bad.Add(tag + "光环词条没加上指示物：" + def.AuraTokenCount + " → " + up.AuraTokenCount);
            }

            if (string.IsNullOrEmpty(up.EffectTextWithSymbols))
            {
                bad.Add(tag + "合成后的效果文案是空的");
            }

            // 词条必须**看得见**（卡面文案里出现它的原文）—— 「加了但看不见」是纯界面缺陷，
            // 引擎侧一切正常，所以只能在这里钉住。
            UpgradeTraits.Trait trait;
            if (UpgradeTraits.TryGet(traitId, out trait)
                && up.EffectTextWithSymbols.IndexOf(trait.Text, StringComparison.Ordinal) < 0)
            {
                bad.Add(tag + "卡面文案里看不到这条词条（" + trait.Text + "）");
            }
        }

        // ── ⑥ 强化册叠目录 ──────────────────────────────────────────

        private static void CheckCatalogOverlay(List<string> bad)
        {
            ICardCatalog builtin = CardCatalog.Builtin();

            // 空册子：原样返回同一个实例（每次开局都会走这条路径，不该造新目录）
            if (!ReferenceEquals(UpgradeBook.Empty.BuildCatalog(builtin), builtin))
            {
                bad.Add("空强化册不该重建卡目录");
            }

            UpgradeBook book = UpgradeBook.FromRecords(new List<CardUpgradeRecord>
            {
                CardUpgradeRecord.Of("a", CardUpgradeMod.Power(2), CardUpgradeMod.Cooldown(-1)),
                CardUpgradeRecord.Of("c", CardUpgradeMod.Effect(UpgradeTraits.AuraDef)),
            });

            ICardCatalog overlaid = book.BuildCatalog(builtin);

            Expect(bad, "叠册后目录张数", overlaid.All.Count.ToString(),
                (CardLibrary.Count + 2).ToString());

            // 不能有重复 ID（CardCatalog 的构造函数会抛，这里再确认一次语义）
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < overlaid.All.Count; i++)
            {
                CardDef card = overlaid.All[i];
                if (card == null)
                {
                    bad.Add("叠册后的目录里有 null");
                    continue;
                }

                if (!ids.Add(card.Id))
                {
                    bad.Add("叠册后的目录里有重复 ID：" + card.Id);
                }
            }

            CardDef a = Get(overlaid, "a+");
            CardDef c = Get(overlaid, "c+");
            if (a == null || a.Power != CardLibrary.Get("a").Power + 2 || a.Cooldown != CardLibrary.Get("a").Cooldown - 1)
            {
                bad.Add("叠册后 a+ 的数值不对：" + (a == null ? "<缺失>" : a.Power + "/" + a.Cooldown));
            }

            if (c == null || c.AuraTokenCount != 1)
            {
                bad.Add("叠册后 c+ 的光环数不对：" + (c == null ? "<缺失>" : c.AuraTokenCount.ToString()));
            }

            // 基础卡仍在（叠册是「加」不是「换」）
            if (Get(overlaid, "b") == null)
            {
                bad.Add("叠册把没被强化的基础卡弄丢了（b 不在目录里）");
            }

            // PreferUpgraded 联动：清单里写基础 ID，解析出来是强化版
            List<string> pref = new List<string>(CardUpgrade.PreferUpgraded(new string[] { "a", "b" }, overlaid));
            Expect(bad, "PreferUpgraded 张数", pref.Count.ToString(), "2");
            Expect(bad, "PreferUpgraded[0]", pref.Count > 0 ? pref[0] : "<空>", "a+");
            Expect(bad, "PreferUpgraded[1]", pref.Count > 1 ? pref[1] : "<空>", "b");

            // 清单里同时有 a 与 a+ → 只留一份
            List<string> dedup = new List<string>(CardUpgrade.PreferUpgraded(new string[] { "a", "a+" }, overlaid));
            Expect(bad, "基础版 / 强化版只留一份", dedup.Count.ToString(), "1");

            // 配方优先于「同名强化版」（模拟上一版落盘的 Card_a_Up.asset 仍在目录里）
            var legacy = new List<CardDef>();
            for (int i = 0; i < builtin.All.Count; i++)
            {
                legacy.Add(builtin.All[i]);
            }

            var assetLike = new CardDef(10000, "a+", "暴风雪+", 6, 4,
                CardLibrary.Get("a").Effects, false, "a", 1, CardLibrary.Get("a").Element);
            legacy.Add(assetLike);

            ICardCatalog withLegacy = new CardCatalog(legacy);
            ICardCatalog overLegacy = UpgradeBook.FromRecords(new List<CardUpgradeRecord>
            {
                CardUpgradeRecord.Of("a", CardUpgradeMod.Cooldown(-1)),
            }).BuildCatalog(withLegacy);

            int count = 0;
            CardDef winner = null;
            for (int i = 0; i < overLegacy.All.Count; i++)
            {
                if (overLegacy.All[i].Id == "a+")
                {
                    count++;
                    winner = overLegacy.All[i];
                }
            }

            if (count != 1)
            {
                bad.Add("配方与旧强化资产同名时应当只留一份，实为 " + count + " 份");
            }
            else if (winner == null || winner.Cooldown != 3)
            {
                bad.Add("配方应当顶掉同名资产（冷却应为 3，实为 "
                        + (winner == null ? "<空>" : winner.Cooldown.ToString()) + "）");
            }

            // 配方的基础卡不在目录里 → 整条跳过，不抛
            ICardCatalog missing = UpgradeBook.FromRecords(new List<CardUpgradeRecord>
            {
                CardUpgradeRecord.Of("zzz", CardUpgradeMod.Power(2)),
            }).BuildCatalog(builtin);
            Expect(bad, "基础卡不存在时不影响目录张数", missing.All.Count.ToString(),
                CardLibrary.Count.ToString());

            // With / Append / Without 的语义
            UpgradeBook appended = UpgradeBook.Empty
                .Append("a", CardUpgradeMod.Power(2))
                .Append("a+", CardUpgradeMod.Cooldown(-1));
            CardUpgradeRecord rec;
            if (!appended.TryGet("a", out rec) || rec == null || rec.mods.Count != 2)
            {
                bad.Add("Append 应当是追加（力量 +2 与冷却 −1 应同时留在 a 的配方上）");
            }

            if (appended.Without("a").Count != 0)
            {
                bad.Add("Without 没把配方去掉");
            }
        }

        // ── ⑦ 存档类型的可序列化形状 ────────────────────────────────

        /// <summary>
        /// <c>JsonUtility</c> 只认「<c>[Serializable]</c> + public 非只读字段」，
        /// 其它一律<b>静默跳过</b>（存档少一截、不报错）。这条断言用反射把
        /// <see cref="CardUpgradeRecord"/> / <see cref="CardUpgradeMod"/> 的形状钉死 ——
        /// 以后有人给它加一个属性或只读字段，这里会立刻变红。
        /// </summary>
        private static void CheckJsonShape(List<string> bad)
        {
            CheckSerializableType(bad, typeof(CardUpgradeMod), new HashSet<Type>());
            CheckSerializableType(bad, typeof(CardUpgradeRecord), new HashSet<Type>());
            // 2026-10-02：女巫工坊转移过来的效果也是存档的一部分（同一条约束）。
            CheckSerializableType(bad, typeof(EffectSpec), new HashSet<Type>());
            CheckSerializableType(bad, typeof(EffectSpecCondition), new HashSet<Type>());
        }

        private static void CheckSerializableType(List<string> bad, Type type, HashSet<Type> visited)
        {
            if (!visited.Add(type))
            {
                return;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                bad.Add("存档类型缺少无参构造（JsonUtility 建不出来）：" + type.Name);
            }

            if (type.GetCustomAttribute<SerializableAttribute>() == null)
            {
                bad.Add("存档类型缺少 [Serializable]：" + type.Name);
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance
                                                | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];

                if (field.IsDefined(typeof(NonSerializedAttribute), false)
                    || field.IsStatic || field.IsPrivate || field.IsInitOnly)
                {
                    // 只有「本意就是运行期状态」的字段才允许这样写；存档数据体里出现就是丢字段
                    bad.Add(type.Name + "." + field.Name + " 不会被 JsonUtility 序列化"
                            + "（需 public 非只读实例字段）");
                    continue;
                }

                if (!IsJsonFriendly(field.FieldType, visited))
                {
                    bad.Add(type.Name + "." + field.Name + " 的类型 " + field.FieldType.Name
                            + " 不能被 JsonUtility 序列化");
                }
            }

            // 属性不是字段 —— JsonUtility 完全不看它们。存档数据体里出现「看起来像数据」的属性
            // 就是「以为存了、其实没存」。
            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance
                                                      | BindingFlags.DeclaredOnly);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].CanWrite)
                {
                    bad.Add(type.Name + " 有可写属性 " + props[i].Name
                            + " —— JsonUtility 不序列化属性，存档里不会出现它");
                }
            }
        }

        private static bool IsJsonFriendly(Type type, HashSet<Type> visited)
        {
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                return true;
            }

            if (type.IsArray)
            {
                return IsJsonFriendly(type.GetElementType(), visited);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return IsJsonFriendly(type.GetGenericArguments()[0], visited);
            }

            if (type.IsInterface || type.IsAbstract)
            {
                return false;
            }

            if (type.GetCustomAttribute<SerializableAttribute>() == null)
            {
                return false;
            }

            CheckSerializableType(new List<string>(), type, visited);
            return true;
        }

        // ── 辅助 ────────────────────────────────────────────────────

        private static CardDef Get(ICardCatalog catalog, string id)
        {
            for (int i = 0; i < catalog.All.Count; i++)
            {
                if (catalog.All[i] != null && string.Equals(catalog.All[i].Id, id, StringComparison.Ordinal))
                {
                    return catalog.All[i];
                }
            }

            return null;
        }

        private static void ExpectReject(List<string> bad, string label, bool allowed, string reason)
        {
            if (allowed)
            {
                bad.Add(label + " —— 应当被拒，却放行了");
            }
            else if (string.IsNullOrEmpty(reason))
            {
                bad.Add(label + " —— 被拒但没给原因（界面会显示一张灰卡却写不出为什么）");
            }
        }

        private static void Expect(List<string> bad, string label, string actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                bad.Add(label + " 不符：实为 " + actual + "，期望 " + expected);
            }
        }
    }
}
