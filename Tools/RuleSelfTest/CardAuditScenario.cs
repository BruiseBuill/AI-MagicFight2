using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-30 · <b>44 张卡逐张核对</b>：卡面描述 ↔ 卡表录入 ↔ 引擎结算 三者一致，
    /// 以及「描述里写『可以』⇒ 这一拍必须能放弃」。
    ///
    /// <para><b>为什么要有这一份</b>：万局统计只能证明「对局跑得完、事件自洽」，
    /// 证明不了「某张卡的效果真的按卡面结算」。漏接一个算子、把 <c>α</c> 写成 <c>β</c>、
    /// 参数填错一格、把「可以」做成「必须」—— 这些在统计里全是静默的，
    /// 表现只是「这张牌不好用」，不报错也不崩。</para>
    ///
    /// <para><b>三层检查</b>：</para>
    /// <list type="number">
    /// <item><b>结构层</b>：逐张核对「触发符号 + 算子 + 主参数 + 光环类型」的完整序列
    /// （<see cref="Signature"/>），以及可选性 —— 卡面带「可以」的效果必须能放弃；</item>
    /// <item><b>实跑层</b>：44 张<b>每张都真的作为进攻牌打一次</b>，走完整条
    /// ①力量 → ②防御 → ③冷却 → ④进冷却区 → ⑤光环 的流水线，
    /// 断言「不抛异常 / 半场能收尾 / 牌进了冷却区 / 光环指示物数正确」；</item>
    /// <item><b>行为层</b>：对 26 组容易被做错的效果逐条钉死数值
    /// （电弧空确认、雪崩双选、地刺回手、冰霜核心减半、荆棘 −2、沉重打击额外伤害、
    ///  模仿的候选过滤、双光环 2 枚、免疫光环、寒流重置、水刃刷新、灼烧按掉血加点、
    ///  雪球按冷却区张数加点、火炬掉血复位、漩涡快速回填光环、引雷连击光环、潮汐 γ）。</item>
    /// </list>
    ///
    /// <para><b>局面怎么布置</b>：不扫种子、不靠发牌运气 —— 换牌那一拍结束后直接
    /// 用 <c>State.CreateCard</c> + <c>Hand</c>/<c>CooldownOps.PutIntoCooldown</c>
    /// 把双方手牌与冷却区摆成需要的样子（全是引擎自己的入口，不绕过任何不变量）。
    /// 所以本场景是<b>确定性</b>的：同一个种子每次跑出同一份结果。</para>
    /// </summary>
    internal static class CardAuditScenario
    {
        // ══════════════════════════════════════════════════════
        //  0) 期望表：卡面描述 → 应有的效果序列
        // ══════════════════════════════════════════════════════
        //
        // 写法：<触发符号>:<算子>[=<主参数>][/<光环类型>][/<目标范围>]，多条用空格分隔。
        //   A = α（进攻）· D = β（防御）· S = γ（特殊）
        // 这份表是**独立于 CardLibrary 的第二份表述** —— 两份一致才算「录对了」。
        //
        // ⚠ 卡面文字里带「可以」的，EffectsDeclinable 那一列必须是 true（用户 2026-10-30 口径）。

        private static readonly string[] Expected =
        {
            "a|A:Haste=1 A:SlowZone",
            "b|A:SlowZone D:Guard",
            "c|A:Slow=2",
            "d|A:ResetCooldown",
            "e|S:ReadyRefresh D:DefPlus=1",
            "f|A:Haste=1",
            "g|A:QuickRefill",
            "h|A:ExtraDamageIfUnblocked=1 A:NoAtkBuff",
            "i|A:SlowIfLastTwoHand=1",
            "j|A:Combo",
            "k|A:CoolHandForCombo=1",
            "l|A:CoolHandForAtk=3 A:Combo",
            "m|A:HasteZone A:Combo",
            "n|A:Aura=6/Combo",
            "o|A:HealMinusMax=1 A:Combo",
            "p|A:HealMinusMax=1 A:HasteZone",
            "q|A:Haste=1 A:HasteZone",
            "r|A:Refresh",
            "s|A:HasteZone A:QuickRefill",
            "t|A:Double A:CoolMinusIfUnblocked=2",
            "u|A:Double A:DoubleAtkBonus",
            "v|A:Double A:SlowIfUnblocked=1",
            "w|A:Double A:LookAndCool=7",
            "x|A:Copy=3 S:RuleNote",
            "y|A:Haste=1 A:HastePerHpLoss=1",
            "z|A:QuickRefill D:DefPlus=1",
            "aa|A:Haste=1 A:Aura=1/QuickRefill",
            "ab|A:Haste=2",
            "ac|A:AtkPlusPerCooling=1",
            "ad|A:Slow=1 A:LookAndCool=4",
            "ae|A:AtkPlusPerHpLoss=1",
            "af|A:Aura=2/DefPower A:Aura=2/DefPower",
            "ag|A:Slow=1 A:Aura=2/AtkPower",
            "ah|A:Haste=1 A:Aura=3/AtkOrDefPower",
            "ai|A:Aura=4/AtkPower",
            "aj|A:Aura=1/AtkPower A:Aura=1/AtkPower",
            "ak|A:Aura=6/ImmuneHigh D:Guard",
            "al|A:Aura=3/ImmuneLow D:Guard",
            "am|A:Haste=1 A:CoolHandForHaste=1",
            "an|A:SlowZoneBoth=1",
            "ao|A:Slow=1 A:Weaken=1/Opponent",
            "ap|A:Aura=3/AtkPower S:RestoreAuraOnHpLoss",
            "aq|A:Slow=1 A:Combo",
            "ar|A:ReturnToHandIfLastHand",
        };

        public static bool Run(List<string> report)
        {
            bool structure = CheckStructure(report);
            bool optional = CheckOptionality(report);
            bool smoke = CheckEveryCardPlays(report);
            bool behaviour = CheckBehaviours(report);
            return structure && optional && smoke && behaviour;
        }

        private static string Flag(bool ok)
        {
            return ok ? "  [PASS] " : "  [FAIL] ";
        }

        // ══════════════════════════════════════════════════════
        //  1) 结构层
        // ══════════════════════════════════════════════════════

        private static bool CheckStructure(List<string> report)
        {
            var bad = new List<string>();
            int checkedCount = 0;

            for (int i = 0; i < Expected.Length; i++)
            {
                string row = Expected[i];
                int bar = row.IndexOf('|');
                string id = row.Substring(0, bar);
                string want = row.Substring(bar + 1).Trim();

                CardDef def;
                if (!CardLibrary.TryGet(id, out def) || def == null)
                {
                    bad.Add("卡表里没有 " + id + "（期望表与 CardLibrary 不同步）");
                    continue;
                }

                checkedCount++;
                string got = Signature(def);
                if (got != want)
                {
                    bad.Add(def.Name + "（" + id + "）：\n        期望 " + want + "\n        实为 " + got);
                }
            }

            // 反向：卡表里多出来的卡
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                bool listed = false;
                for (int j = 0; j < Expected.Length; j++)
                {
                    if (Expected[j].StartsWith(def.Id + "|", StringComparison.Ordinal)) { listed = true; break; }
                }

                if (!listed)
                {
                    bad.Add("卡表里有、期望表里没登记：" + def.Name + "（" + def.Id + "）");
                }
            }

            report.Add(Flag(bad.Count == 0)
                       + "全卡表结构：" + checkedCount + " 张卡的「触发符号 + 算子 + 参数 + 光环类型」"
                       + "与卡面描述逐条一致（44 张全覆盖）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        /// <summary>
        /// 一张卡的效果序列：<c>α:Haste=1 α:SlowZone</c>。
        ///
        /// <para>主参数只写非 0 的（<c>Guard</c> / <c>Double</c> / <c>Combo</c> 这类没有参数的效果
        /// 就不带 <c>=</c>），目标范围只在**不是默认 Participants** 时写出来
        /// （目前只有冰霜核心的虚弱是指向对手的）。</para>
        /// </summary>
        private static string Signature(CardDef def)
        {
            var parts = new List<string>();
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                string s = Symbol(e.Trigger) + ":" + e.Op;
                if (e.A != 0)
                {
                    s += "=" + e.A;
                }

                if (e.Aura != AuraKind.None)
                {
                    s += "/" + e.Aura;
                }
                else if (e.Targets == EffectTargetScope.Opponent)
                {
                    s += "/Opponent";
                }

                parts.Add(s);
            }

            return string.Join(" ", parts.ToArray());
        }

        private static string Symbol(EffectTrigger trigger)
        {
            switch (trigger)
            {
                case EffectTrigger.Attack: return "A";
                case EffectTrigger.Defend: return "D";
                default: return "S";
            }
        }

        // ══════════════════════════════════════════════════════
        //  2) 可选性：卡面写「可以」⇒ 必须能放弃
        // ══════════════════════════════════════════════════════

        private static bool CheckOptionality(List<string> report)
        {
            var bad = new List<string>();
            int canDeclineCards = 0;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                for (int e = 0; e < def.Effects.Count; e++)
                {
                    EffectDef ef = def.Effects[e];
                    bool textSaysCan = ef.Text != null && ef.Text.IndexOf("可以", StringComparison.Ordinal) >= 0;
                    bool declinable = Declinable(ef);

                    if (textSaysCan && !declinable)
                    {
                        bad.Add(def.Name + "（" + def.Id + "）第 " + e + " 条写着「可以」，"
                                + "但引擎这一拍不允许放弃（" + ef.Op + " 是强制效果）");
                    }

                    // 反向：加速 / 减速这一族天生可以跳过（规则 §0「效果可选性」），
                    // 其余效果若没写「可以」就不该给出放弃的口子。
                    bool expected = !ef.Text.Contains("[强制]") && !ef.IsConstraint;
                    if (declinable != expected)
                    {
                        bad.Add(def.Name + " / " + ef.Op + " 的可选性与强制标记或固有约束不一致");
                    }

                    if (textSaysCan)
                    {
                        canDeclineCards++;
                    }
                }
            }

            report.Add(Flag(bad.Count == 0)
                       + "可选性：卡面写「可以」的效果全部能放弃（共 " + canDeclineCards
                       + " 条）；其余未标强制的效果可跳过，固有约束不可跳过");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        /// <summary>这一条效果在引擎里能不能被放弃（跳过 / 空确认）。</summary>
        private static bool Declinable(EffectDef ef)
        {
            if (ef.Mandatory || ef.IsConstraint) return false;
            if (ef.CanSkip)
            {
                return true;
            }

            switch (ef.Op)
            {
                // 这三条走「MinSelect = 0」而不是「跳过」按钮：弹窗里可以一张都不选直接确认
                // （磁暴 / 充能 / 电弧，2026-09-27 用户口径）。
                case EffectOp.CoolHandForAtk:
                case EffectOp.CoolHandForHaste:
                case EffectOp.CoolHandForCombo:
                // 雪崩：选项只有「双方减速」+「跳过」两条（阈值由卡表定死）。
                case EffectOp.SlowZoneBoth:
                // 漩涡（当前无卡使用）：同样 allowEmpty。
                case EffectOp.RemoveFromGame:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>加速 / 减速这一族（规则 §0：只有它们允许「不执行」，与卡面是否写「可以」无关）。</summary>
        private static bool IsCooldownOp(EffectOp op)
        {
            return op == EffectOp.Haste || op == EffectOp.Slow
                   || op == EffectOp.HasteZone || op == EffectOp.SlowZone
                   || op == EffectOp.HastePerHpLoss || op == EffectOp.SlowIfLastTwoHand
                   || op == EffectOp.SlowIfUnblocked;
        }

        // ══════════════════════════════════════════════════════
        //  3) 实跑层：44 张每张都真的打一次
        // ══════════════════════════════════════════════════════

        private static bool CheckEveryCardPlays(List<string> report)
        {
            var bad = new List<string>();
            int played = 0;

            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                string why;
                if (!PlayOnce(def.Id, out why))
                {
                    bad.Add(def.Name + "（" + def.Id + "）：" + why);
                }
                else
                {
                    played++;
                }
            }

            report.Add(Flag(bad.Count == 0)
                       + "实跑：" + played + " / 44 张卡作为进攻牌打出后，"
                       + "①力量 → ②防御 → ③冷却 → ④进冷却区 → ⑤光环 全流程走完、无异常");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        /// <summary>把一张卡当进攻牌打一次，检查半场能收尾、牌落在正确的区域、光环枚数正确。</summary>
        private static bool PlayOnce(string id, out string why)
        {
            why = null;
            CardDef def = CardLibrary.Get(id);

            // 手牌三张：目标卡 + 两张陪衬（避免「最后一张手牌」类条件被顺带触发）。
            Table r = Table.Begin(900 + def.Index);
            // 对手两张手牌：挡一张之后还有一张，才走得到「对手出牌拍」（半场收尾的判据）
            r.Prepare(new[] { id, "a", "f" }, new[] { "c", "d", "j", "f" });
            r.SetCooling(0, "b:2");
            r.SetCooling(1, "c:2");
            r.ToAttackTurn();

            try
            {
                if (!r.PlayCard(id))
                {
                    why = r.Trouble;
                    return false;
                }

                r.RunToSeat1Attack();
            }
            catch (Exception ex)
            {
                why = "打出时抛异常：" + ex.GetType().Name + " " + ex.Message;
                return false;
            }

            if (r.Trouble.Length > 0)
            {
                why = r.Trouble;
                return false;
            }

            // 半场收尾判据：要么已经轮到对手出牌，要么对局结束。
            if (r.Engine.Pending == null && !r.Engine.IsOver)
            {
                why = "半场没有收尾（既没到对手的出牌拍、也没结束）";
                return false;
            }

            CardInstance card = r.FindAny(id);
            if (card == null)
            {
                why = "打出之后找不到这张牌的实例（既不在手牌也不在冷却区）";
                return false;
            }

            if (card.Zone != CardZone.Cooling && card.Zone != CardZone.Hand && card.Zone != CardZone.Removed)
            {
                why = "半场结束仍停在 " + card.Zone + "（应为冷却区 / 手牌 / 移出）";
                return false;
            }

            // 光环：打出去 → 进冷却区 → 第 ⑤ 步点亮。还在冷却区时枚数必须等于卡面条数。
            if (def.HasAura && card.Zone == CardZone.Cooling && card.AuraTokens != def.AuraTokenCount)
            {
                why = "光环指示物 " + card.AuraTokens + " 枚，卡面是 " + def.AuraTokenCount + " 条";
                return false;
            }

            return true;
        }

        // ══════════════════════════════════════════════════════
        //  4) 行为层：逐条钉死数值
        // ══════════════════════════════════════════════════════

        private static bool CheckBehaviours(List<string> report)
        {
            var bad = new List<string>();
            int total = 0;

            One(report, bad, ref total, "电弧（k）：卡面「可以将……送入冷却」，弹窗必须能一张都不选，空确认不给连击",
                ProbeArcDecline);
            One(report, bad, ref total, "磁暴（l）：空确认 → 进攻力量 +0（不是「必须选一张」）",
                ProbeMagneticEmpty);
            One(report, bad, ref total, "雪崩（an）：「可以」= 有「跳过」；跳过不动任何冷却，执行则双方各 +1",
                ProbeAvalanche);
            One(report, bad, ref total, "地刺（ar）：只剩这一张手牌时打完自动回手",
                ProbeSpikeReturn);
            One(report, bad, ref total, "地刺（ar）：手牌 ≥2 张时不回手（条件没被放大）",
                ProbeSpikeNoReturn);
            One(report, bad, ref total, "冰霜核心（ao）：对方下一次进攻的最终力量减半（8 → 4，向上取整）",
                ProbeFrostCore);
            One(report, bad, ref total, "荆棘（t）：对方没挡住 → 此牌冷却 −2（3 → 1）",
                ProbeThorns);
            One(report, bad, ref total, "藤蔓（v）：对方没挡住 → 获得减速（目标 +1）",
                ProbeVine);
            One(report, bad, ref total, "沉重打击（h）：没挡住 → 额外 −1 生命、−1 生命上限",
                ProbeHeavyStrike);
            One(report, bad, ref total, "模仿（x）：基础冷却 4 的牌不在候选里，此时力量仍是 1",
                ProbeMimicReject);
            One(report, bad, ref total, "模仿（x）：复制基础冷却 2 的牌 → 本次力量变成 3",
                ProbeMimicCopy);
            One(report, bad, ref total, "冰封铠甲（af）：双光环 → 2 枚指示物",
                ProbeDoubleAura);
            One(report, bad, ref total, "石化（ak）：免疫 ≥6 的攻击（9 点攻击被整轮免疫、不交牌）",
                ProbeImmuneHigh);
            One(report, bad, ref total, "寒流（d）：把对方一张法术的剩余冷却重置为基础值",
                ProbeColdWave);
            One(report, bad, ref total, "水刃（r）：使一张法术立即冷却完成 → 回手",
                ProbeWaterBlade);
            One(report, bad, ref total, "灼烧（ae）：双方各损失的生命值都算进攻力量（4 + 3 = 7）",
                ProbeBurn);
            One(report, bad, ref total, "雪球（ac）：冷却区每张牌 +1（2 张 → +2）",
                ProbeSnowball);
            One(report, bad, ref total, "火炬（ap）：自己掉血后光环指示物补满",
                ProbeTorch);
            One(report, bad, ref total, "漩涡（aa）：快速回填光环 → 打出的牌进冷却区时额外 −1（4 → 3）",
                ProbeVortexAura);
            One(report, bad, ref total, "引雷（n）：连击光环使基础力量 ≤6 的牌获得连击（打出后追加一次进攻）",
                ProbeThunderAura);
            One(report, bad, ref total, "潮汐（e）：冷却完毕回手时，弹一次「使另一张法术立即冷却完成」",
                ProbeTide);
            One(report, bad, ref total, "电弧（k）：选一张送冷却 → 真的拿到连击（打出后再来一次进攻）",
                ProbeArcCombo);
            One(report, bad, ref total, "飞叶连击（u）：「额外进攻力量翻倍」（光环 +3 变成 +6）",
                ProbeDoubleAtkBonus);
            One(report, bad, ref total, "过载（o）：回 1 点生命、减 1 点生命上限（[强制]，无决策）",
                ProbeOverload);
            One(report, bad, ref total, "石盾（al）：免疫 ≤3 的攻击（3 点攻击被整轮免疫）",
                ProbeImmuneLow);
            One(report, bad, ref total, "冰风暴（b）：β 守护，5 点也能挡住 9 点",
                ProbeGuard);

            return bad.Count == 0;
        }

        private static void One(List<string> report, List<string> bad, ref int total,
            string label, Func<string> probe)
        {
            total++;
            string why;
            try
            {
                why = probe();
            }
            catch (Exception ex)
            {
                why = "抛异常：" + ex.GetType().Name + " " + ex.Message;
            }

            report.Add(Flag(string.IsNullOrEmpty(why)) + label);
            if (!string.IsNullOrEmpty(why))
            {
                report.Add("      · " + why);
                bad.Add(label + " → " + why);
            }
        }

        // ── 探针 1：电弧 ───────────────────────────────────────
        private static string ProbeArcDecline()
        {
            Table r = Table.Begin(201);
            r.Prepare(new[] { "k", "a", "f" }, new[] { "b", "c" });
            r.SetCooling(0);
            r.SetCooling(1, "c:2");
            r.ToAttackTurn();

            if (!r.PlayCard("k")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseCoolHandCards)
            {
                return "没有弹出「选一张手牌送入冷却」的决策（" + (req == null ? "null" : req.Kind.ToString()) + "）";
            }

            if (req.MinSelect != 0)
            {
                return "MinSelect = " + req.MinSelect + " —— 卡面写「可以」，必须能一张都不选";
            }

            r.SubmitEmpty();          // 空确认 = 放弃这条效果
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            PlayerState p = r.Engine.State.Of(0);
            int keep = 0;
            for (int i = 0; i < p.Hand.Count; i++)
            {
                if (p.Hand[i].Def.Id == "a" || p.Hand[i].Def.Id == "f") keep++;
            }

            if (keep != 2)
            {
                return "空确认之后陪衬牌被吃掉了（应留 2 张，实际 " + keep + " 张）";
            }

            CardInstance k = r.FindCooling(0, "k");
            if (k == null)
            {
                return "电弧自己没进冷却区";
            }

            return null;
        }

        // ── 探针 2：磁暴空确认 ─────────────────────────────────
        private static string ProbeMagneticEmpty()
        {
            Table r = Table.Begin(202);
            r.Prepare(new[] { "l", "a", "f" }, new[] { "b" });
            r.SetCooling(0);
            r.SetCooling(1, "c:2");
            r.ToAttackTurn();

            if (!r.PlayCard("l")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseCoolHandCards) return "没有弹选牌决策";
            if (req.MinSelect != 0) return "MinSelect = " + req.MinSelect + "（应可空确认）";

            r.SubmitEmpty();
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            AttackPowerResolvedEvent ev = r.FindAttack("l");
            if (ev == null) return "没找到磁暴的力量结算事件";
            if (ev.BonusPower != 0) return "空确认却拿到 +" + ev.BonusPower + " 进攻力量";
            if (ev.BasePower != 2) return "基础力量应为 2，实为 " + ev.BasePower;
            return null;
        }

        // ── 探针 3：雪崩 ───────────────────────────────────────
        private static string ProbeAvalanche()
        {
            // ① 跳过 → 双方 1 点剩余冷却的牌都不动
            Table r = Table.Begin(203);
            r.Prepare(new[] { "an" }, new[] { "b" });
            r.SetCooling(1, "c:1");
            r.ToAttackTurn();
            r.SetCooling(0, "a:1");      // 出牌拍之后再摆，避免开拍 tick

            if (!r.PlayCard("an")) return r.Trouble;

            DecisionRequest req = r.AdvanceToDecision(RequestKind.ChooseZoneValue);
            if (req == null)
            {
                return "没有弹「双方减速」决策" + r.Dump(10);
            }

            if (req.SkipOption == null || req.MinSelect != 0)
            {
                return "没有「跳过」（MinSelect = " + req.MinSelect + "）—— 卡面写的是「可以选择」";
            }

            r.Submit(req.SkipOption.Index);
            r.RunToSeat1Attack();

            // ⚠ 不能读「现在的剩余冷却」：对手半场开拍的 tick 会各 −1，
            //   所以看的是事件 —— 跳过时一条「区域减速」都不该有。
            if (r.CountChanges("区域减速") != 0)
            {
                return "选择了跳过，却仍然发生了区域减速（" + r.CountChanges("区域减速") + " 条）";
            }

            // ② 执行 → 双方各 +1
            Table s = Table.Begin(204);
            s.Prepare(new[] { "an" }, new[] { "b" });
            s.SetCooling(1, "c:1");
            s.ToAttackTurn();
            s.SetCooling(0, "a:1");
            if (!s.PlayCard("an")) return s.Trouble;

            DecisionRequest q = s.AdvanceToDecision(RequestKind.ChooseZoneValue);
            if (q == null) return "第二局没有弹「双方减速」决策" + s.Dump(10);

            int pick = -1;
            for (int i = 0; i < q.Options.Count; i++)
            {
                if (!q.Options[i].IsSkip) { pick = i; break; }
            }

            if (pick < 0) return "「双方减速」选项本身不存在";
            s.Submit(pick);
            s.RunToSeat1Attack();

            CooldownChangedEvent mine = s.FindChange("区域减速", "a");
            CooldownChangedEvent theirs = s.FindChange("区域减速", "c");
            if (mine == null) return "自己那张剩余 1 的牌没有被减速";
            if (theirs == null) return "对方那张剩余 1 的牌没有被减速";
            if (mine.Change.From != 1 || mine.Change.To != 2)
            {
                return "自己那张应 1 → 2，实际 " + mine.Change.From + " → " + mine.Change.To;
            }

            if (theirs.Change.From != 1 || theirs.Change.To != 2)
            {
                return "对方那张应 1 → 2，实际 " + theirs.Change.From + " → " + theirs.Change.To;
            }

            return null;
        }

        // ── 探针 4 / 5：地刺 ───────────────────────────────────
        private static string ProbeSpikeReturn()
        {
            Table r = Table.Begin(205);
            r.Prepare(new[] { "ar" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("ar")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            CardInstance card = r.FindAny("ar");
            if (card == null) return "找不到地刺";
            if (card.Zone != CardZone.Hand) return "只有这一张手牌，打完却没回手（停在 " + card.Zone + "）";
            return null;
        }

        private static string ProbeSpikeNoReturn()
        {
            Table r = Table.Begin(206);
            r.Prepare(new[] { "ar", "a" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("ar")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            CardInstance card = r.FindAny("ar");
            if (card == null) return "找不到地刺";
            if (card.Zone != CardZone.Cooling) return "手上还有别的牌，却回了手（Zone = " + card.Zone + "）";
            return null;
        }

        // ── 探针 6：冰霜核心 ───────────────────────────────────
        private static string ProbeFrostCore()
        {
            Table r = Table.Begin(207);
            r.Prepare(new[] { "ao" }, new[] { "j" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("ao")) return r.Trouble;

            // ① 力量阶段就该挂上 1 层虚弱 —— 到防御拍这一刻检查最准（再往后对手半场结束会 −1 层）
            DecisionRequest def = r.AdvanceToDecision(RequestKind.ChooseDefense);
            if (def == null) return "没有进入防御拍" + r.Dump(10);

            PlayerState enemy = r.Engine.State.Of(1);
            if (enemy.WeakenStacks != 1)
            {
                return "对方身上应有 1 层虚弱，实为 " + enemy.WeakenStacks + r.Dump(14);
            }

            // ② 主动放弃防御，把闪电留在手里当下一拍的进攻牌
            r.SubmitSkipOrEmpty(def);
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            if (!r.PlayCard("j")) return "对手没能打出闪电：" + r.Trouble;
            r.Advance();

            AttackPowerResolvedEvent ev = r.FindAttack("j");
            if (ev == null) return "没找到闪电的力量结算事件";
            if (ev.BasePower != 8) return "闪电基础力量应为 8，实为 " + ev.BasePower;
            if (ev.FinalPower != 4) return "虚弱之后最终力量应为 4（8 折半向上取整），实为 " + ev.FinalPower;
            return null;
        }

        // ── 探针 7：荆棘 ───────────────────────────────────────
        private static string ProbeThorns()
        {
            Table r = Table.Begin(208);
            r.Prepare(new[] { "t" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("t")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            CardInstance t = r.FindAny("t");
            if (t == null) return "找不到荆棘";
            if (t.Zone != CardZone.Cooling) return "荆棘没有进冷却区（" + t.Zone + "）";
            if (t.RemainingCooldown != 1) return "对方没挡住，冷却应 3 → 1，实为 " + t.RemainingCooldown;
            return null;
        }

        // ── 探针 8：藤蔓 ───────────────────────────────────────
        private static string ProbeVine()
        {
            Table r = Table.Begin(209);
            r.Prepare(new[] { "v" }, new[] { "a" });
            r.SetCooling(0);
            r.SetCooling(1, "b:2");
            r.ToAttackTurn();

            if (!r.PlayCard("v")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            CooldownChangedEvent slow = r.FindChange("减速", "b");
            if (slow == null) return "对方没挡住，但没有产生任何减速" + r.Dump(12);
            if (slow.Change.From != 2 || slow.Change.To != 3)
            {
                return "应减速 2 → 3，实际 " + slow.Change.From + " → " + slow.Change.To;
            }

            return null;
        }

        // ── 探针 9：沉重打击 ───────────────────────────────────
        private static string ProbeHeavyStrike()
        {
            Table r = Table.Begin(210);
            r.Prepare(new[] { "h" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("h")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            PlayerState enemy = r.Engine.State.Of(1);
            if (enemy.MaxHp != 3) return "对方生命上限应 4 → 3，实为 " + enemy.MaxHp;
            if (enemy.Hp != 2) return "对方生命应 4 → 2（没挡住 1 + 额外 1），实为 " + enemy.Hp;
            return null;
        }

        // ── 探针 10 / 11：模仿 ─────────────────────────────────
        private static string ProbeMimicReject()
        {
            Table r = Table.Begin(211);
            r.Prepare(new[] { "x" }, new[] { "b" });
            r.SetCooling(0, "a:3");      // a 基础冷却 4 → 超出「≤3」，不可复制
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("x")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req != null && req.Kind == RequestKind.ChooseCopyTarget)
            {
                return "基础冷却 4 的牌被当成候选了（门槛应是 ≤3）";
            }

            AttackPowerResolvedEvent ev = r.FindAttack("x");
            if (ev == null) return "没找到模仿的力量结算事件";
            if (ev.BasePower != 1) return "没有合法复制目标时力量应为 1，实为 " + ev.BasePower;
            return null;
        }

        private static string ProbeMimicCopy()
        {
            Table r = Table.Begin(212);
            r.Prepare(new[] { "x" }, new[] { "b" });
            r.SetCooling(1);
            r.ToAttackTurn();
            r.SetCooling(0, "c:1");      // c 基础冷却 2、无光环 → 可复制（出牌拍之后再摆，避开开拍 tick）

            if (!r.PlayCard("x")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseCopyTarget)
            {
                return "没有弹复制候选（" + (req == null ? "null" : req.Kind.ToString()) + "）";
            }

            int idx = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].Card != null && req.Options[i].Card.Def.Id == "c") { idx = i; break; }
            }

            if (idx < 0) return "候选里没有凝固（c）";
            r.Submit(idx);
            r.Advance();

            AttackPowerResolvedEvent ev = r.FindAttack("x");
            if (ev == null) return "没找到模仿的力量结算事件";
            if (ev.BasePower != 3) return "复制凝固之后力量应为 3，实为 " + ev.BasePower;
            return null;
        }

        // ── 探针 12：双光环 ────────────────────────────────────
        private static string ProbeDoubleAura()
        {
            Table r = Table.Begin(213);
            r.Prepare(new[] { "af" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("af")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            CardInstance af = r.FindAny("af");
            if (af == null) return "找不到冰封铠甲";
            if (af.AuraTokens != 2) return "指示物应为 2（双光环），实为 " + af.AuraTokens;
            return null;
        }

        // ── 探针 13：免疫 ──────────────────────────────────────
        private static string ProbeImmuneHigh()
        {
            Table r = Table.Begin(214);
            r.Prepare(new[] { "f" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1, "ak:2");
            r.ToAttackTurn();

            CardInstance ak = r.FindCooling(1, "ak");
            if (ak == null) return "布置的石化不见了";
            r.GiveAura(ak);

            if (!r.PlayCard("f")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseDefense) return "没有进入防御拍";

            int idx = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].AuraSource != null && req.Options[i].AuraKind == AuraKind.ImmuneHigh) { idx = i; break; }
            }

            if (idx < 0) return "防御拍没有给出「免疫 ≥6」的光环选项";

            r.Engine.Submit(DecisionResponse.WithAuras(1, new int[0], new int[] { idx }));
            r.Advance();

            DefenseResolvedEvent def = r.FindDefense();
            if (def == null) return "没有防御结算事件";
            if (!def.UsedImmune) return "免疫没有被采用";
            if (!def.Success) return "免疫之后防御却判失败";
            if (def.Cards.Count != 0) return "免疫还消耗了手牌（应一张都不交）";
            if (ak.AuraTokens != 0) return "免疫指示物没被消耗";
            if (r.Engine.State.Of(1).Hp != 4) return "免疫之后仍然掉血了";
            return null;
        }

        // ── 探针 14：寒流 ──────────────────────────────────────
        private static string ProbeColdWave()
        {
            Table r = Table.Begin(215);
            r.Prepare(new[] { "d" }, new[] { "b" });
            r.SetCooling(0);
            r.SetCooling(1, "a:1");
            r.ToAttackTurn();

            if (!r.PlayCard("d")) return r.Trouble;

            DecisionRequest req = r.AdvanceToDecision(RequestKind.ChooseRefreshTarget);
            if (req == null) return "没有弹「重置冷却」决策" + r.Dump(10);

            int idx = r.IndexOfOptionCard("a");
            if (idx < 0) return "候选里没有对方那张牌";

            r.Submit(idx);
            r.Advance();

            CooldownChangedEvent reset = r.FindChange("重置冷却", "a");
            if (reset == null) return "没有产生「重置冷却」的改动" + r.Dump(12);
            if (reset.Change.From != 1 || reset.Change.To != 4)
            {
                return "应重置为 4（1 → 4），实际 " + reset.Change.From + " → " + reset.Change.To;
            }

            return null;
        }

        // ── 探针 15：水刃 ──────────────────────────────────────
        private static string ProbeWaterBlade()
        {
            Table r = Table.Begin(216);
            r.Prepare(new[] { "r" }, new[] { "b" });
            r.SetCooling(0, "a:2");
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("r")) return r.Trouble;

            DecisionRequest req = r.AdvanceToDecision(RequestKind.ChooseRefreshTarget);
            if (req == null) return "没有弹「立即冷却完成」决策" + r.Dump(10);

            int idx = r.IndexOfOptionCard("a");
            if (idx < 0) return "候选里没有自己那张牌";
            r.Submit(idx);
            r.Advance();

            CardInstance a = r.FindAny("a");
            if (a == null) return "找不到那张牌";
            if (a.Zone != CardZone.Hand) return "立即冷却完成之后应回手，实际在 " + a.Zone;
            return null;
        }

        // ── 探针 16：灼烧 ──────────────────────────────────────
        private static string ProbeBurn()
        {
            Table r = Table.Begin(217);
            r.Prepare(new[] { "ae" }, new[] { "b" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            r.Engine.State.Of(0).Hp = 2;     // 自己掉 2
            r.Engine.State.Of(1).Hp = 3;     // 对方掉 1

            if (!r.PlayCard("ae")) return r.Trouble;
            r.Advance();

            AttackPowerResolvedEvent ev = r.FindAttack("ae");
            if (ev == null) return "没找到灼烧的力量结算事件";
            if (ev.BasePower != 4) return "基础力量应为 4，实为 " + ev.BasePower;
            if (ev.BonusPower != 3) return "双方合计掉 3 点 → 应 +3，实为 " + ev.BonusPower;
            return null;
        }

        // ── 探针 17：雪球 ──────────────────────────────────────
        private static string ProbeSnowball()
        {
            Table r = Table.Begin(218);
            r.Prepare(new[] { "ac" }, new[] { "b" });
            r.SetCooling(1);
            r.ToAttackTurn();
            r.SetCooling(0, "a:2", "b:2");    // 自己冷却区 2 张

            if (!r.PlayCard("ac")) return r.Trouble;
            r.Advance();

            AttackPowerResolvedEvent ev = r.FindAttack("ac");
            if (ev == null) return "没找到雪球的力量结算事件";
            if (ev.BonusPower != 2) return "冷却区 2 张 → 应 +2，实为 " + ev.BonusPower;
            return null;
        }

        // ── 探针 18：火炬 ──────────────────────────────────────
        private static string ProbeTorch()
        {
            Table r = Table.Begin(219);
            r.Prepare(new[] { "c" }, new[] { "j", "b" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();
            r.SetCooling(0, "ap:3");

            CardInstance ap = r.FindCooling(0, "ap");
            if (ap == null) return "布置的火炬不见了";
            r.GiveAura(ap);
            if (ap.AuraTokens != 1) return "火炬光环应为 1 枚";

            r.ConsumeAllAura(ap);
            if (ap.AuraTokens != 0) return "指示物没消耗掉（测试布置有问题）";

            // 直接走引擎自己的伤害入口（= 真实掉血那条路），不必等对手那一拍：
            //   它会 PublishHpLoss → owner.hp.lost → 特殊窗口 → RestoreAuraOnHpLoss
            r.Engine.EffectDamage(0, 1, "自测掉血");
            if (r.Engine.State.Of(0).Hp != 3) return "掉血没有生效";

            if (!r.PlayCard("c")) return r.Trouble;
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            if (ap.AuraTokens != 1)
            {
                return "掉血之后光环没有复位（指示物 " + ap.AuraTokens + " 枚）" + r.Dump(10);
            }

            return null;
        }

        // ── 探针 19：漩涡的快速回填光环 ────────────────────────
        private static string ProbeVortexAura()
        {
            Table r = Table.Begin(220);
            r.Prepare(new[] { "b" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.SetCooling(0, "aa:4");     // 开拍 tick 之后剩 3
            CardInstance aa = r.FindCooling(0, "aa");
            if (aa != null) r.GiveAura(aa);
            r.ToAttackTurn();

            if (aa == null) return "布置的漩涡不见了";

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseAttackCard) return "不在出牌拍";

            int card = -1;
            int aura = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].AuraSource != null && req.Options[i].AuraKind == AuraKind.QuickRefill) aura = i;
                else if (req.Options[i].Card != null && req.Options[i].Card.Def.Id == "b") card = i;
            }

            if (card < 0) return "手里没有冰风暴";
            if (aura < 0) return "出牌拍没有给出「快速回填」光环选项";

            r.Engine.Submit(DecisionResponse.WithAuras(0, new int[] { card }, new int[] { aura }));
            r.RunToSeat1Attack();
            if (r.Trouble.Length > 0) return r.Trouble;

            CardInstance b = r.FindAny("b");
            if (b == null) return "找不到冰风暴";
            if (b.Zone != CardZone.Cooling) return "冰风暴没进冷却区（" + b.Zone + "）";
            if (b.RemainingCooldown != 3) return "快速回填之后剩余冷却应为 3（4 − 1），实为 " + b.RemainingCooldown;
            return null;
        }

        // ── 探针 20：引雷的连击光环 ────────────────────────────
        private static string ProbeThunderAura()
        {
            Table r = Table.Begin(221);
            r.Prepare(new[] { "a", "f" }, new[] { "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.SetCooling(0, "n:4");      // 开拍 tick 之后剩 3
            CardInstance n = r.FindCooling(0, "n");
            if (n == null) return "布置的引雷不见了";
            r.GiveAura(n);
            r.ToAttackTurn();

            DecisionRequest req = r.Engine.Pending;
            int card = -1;
            int aura = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].AuraSource != null && req.Options[i].AuraKind == AuraKind.Combo) aura = i;
                else if (req.Options[i].Card != null && req.Options[i].Card.Def.Id == "a") card = i;
            }

            if (card < 0) return "手里没有暴风雪";
            if (aura < 0) return "出牌拍没有给出「连击」光环选项";

            r.Engine.Submit(DecisionResponse.WithAuras(0, new int[] { card }, new int[] { aura }));
            r.Advance();

            // 连击 = 打完还能再打一次：下一个出牌拍仍然属于座位 0
            int guard = 0;
            while (r.Engine.Pending != null && ++guard < 40)
            {
                DecisionRequest q = r.Engine.Pending;
                if (q.Kind == RequestKind.ChooseAttackCard && q.Seat == 0) return null;
                if (q.Kind == RequestKind.ChooseAttackCard && q.Seat == 1) break;
                r.SubmitSkipOrEmpty(q);
                r.Advance();
            }

            return "拿到连击光环之后没有出现「再打一次」的出牌拍";
        }

        // ── 探针 21：潮汐的 γ ──────────────────────────────────
        private static string ProbeTide()
        {
            Table r = Table.Begin(222);
            r.Prepare(new[] { "a", "f" }, new[] { "c" });
            // e 在开拍 tick 里回手 → 触发 γ；b 是 γ 的刷新目标（γ 只认自己的冷却区）
            r.SetCooling(0, "e:1", "b:3");
            r.SetCooling(1);
            r.AdvanceUntilKind(RequestKind.ChooseRefreshTarget);

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseRefreshTarget)
            {
                return "潮汐冷却完毕回手时没有弹出「使一张法术立即冷却完成」"
                       + "（当前 " + (req == null ? "null" : req.Kind.ToString()) + "）";
            }

            int idx = r.IndexOfOptionCard("b");
            if (idx < 0) return "候选里没有自己冷却区那张牌";
            r.Submit(idx);
            r.Advance();

            CardInstance b = r.FindAny("b");
            if (b == null) return "找不到那张牌";
            if (b.Zone != CardZone.Hand) return "立即冷却完成之后应回手，实际在 " + b.Zone;
            return null;
        }

        // ── 探针 22：电弧的正面对照 ────────────────────────────
        private static string ProbeArcCombo()
        {
            Table r = Table.Begin(223);
            r.Prepare(new[] { "k", "a", "f" }, new[] { "b", "c" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("k")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseCoolHandCards) return "没有弹选牌决策";

            int card = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (!req.Options[i].IsSkip && req.Options[i].Card != null) { card = i; break; }
            }

            if (card < 0) return "没有可送冷却的手牌";
            r.Submit(card);
            r.Advance();

            int guard = 0;
            while (r.Engine.Pending != null && ++guard < 60)
            {
                DecisionRequest q = r.Engine.Pending;
                if (q.Kind == RequestKind.ChooseAttackCard && q.Seat == 0) return null;
                if (q.Kind == RequestKind.ChooseAttackCard && q.Seat == 1) break;
                r.SubmitSkipOrEmpty(q);
                r.Advance();
            }

            return "送了一张牌进冷却，却没有拿到连击（没出现追加进攻）" + r.Dump(12);
        }

        // ── 探针 23：飞叶连击的翻倍 ────────────────────────────
        private static string ProbeDoubleAtkBonus()
        {
            Table r = Table.Begin(224);
            r.Prepare(new[] { "u" }, new[] { "c", "d" });
            r.SetCooling(1);
            r.SetCooling(0, "ah:4");       // 烈焰斗篷：光环 进攻 +3 或防御 +3
            CardInstance ah = r.FindCooling(0, "ah");
            if (ah == null) return "布置的烈焰斗篷不见了";
            r.GiveAura(ah);
            r.ToAttackTurn();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseAttackCard) return "不在出牌拍";

            int card = -1;
            int aura = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].AuraSource != null && req.Options[i].AuraKind == AuraKind.AtkOrDefPower) aura = i;
                else if (req.Options[i].Card != null && req.Options[i].Card.Def.Id == "u") card = i;
            }

            if (card < 0) return "手里没有飞叶连击";
            if (aura < 0) return "出牌拍没有给出「攻/防 +3」光环选项";

            r.Engine.Submit(DecisionResponse.WithAuras(0, new int[] { card }, new int[] { aura }));
            r.Advance();

            AttackPowerResolvedEvent ev = r.FindAttack("u");
            if (ev == null) return "没找到飞叶连击的力量结算事件" + r.Dump(10);
            if (ev.BasePower != 2) return "基础力量应为 2，实为 " + ev.BasePower;
            if (ev.BonusPower != 6) return "光环 +3 翻倍后应 +6，实为 " + ev.BonusPower;
            return null;
        }

        // ── 探针 24：过载 ──────────────────────────────────────
        private static string ProbeOverload()
        {
            Table r = Table.Begin(225);
            r.Prepare(new[] { "o" }, new[] { "c", "d" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();
            r.Engine.State.Of(0).Hp = 3;

            if (!r.PlayCard("o")) return r.Trouble;
            r.Advance();

            PlayerState me = r.Engine.State.Of(0);
            if (me.MaxHp != 3) return "生命上限应 4 → 3，实为 " + me.MaxHp;
            if (me.Hp != 3) return "回 1 点后又被上限拉回，Hp 应为 3，实为 " + me.Hp;
            return null;
        }

        // ── 探针 25：石盾的免疫 ≤3 ─────────────────────────────
        private static string ProbeImmuneLow()
        {
            Table r = Table.Begin(226);
            r.Prepare(new[] { "c" }, new[] { "b", "d" });
            r.SetCooling(0);
            r.SetCooling(1, "al:2");
            r.ToAttackTurn();

            CardInstance al = r.FindCooling(1, "al");
            if (al == null) return "布置的石盾不见了";
            r.GiveAura(al);

            if (!r.PlayCard("c")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseDefense) return "没有进入防御拍";

            int idx = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].AuraSource != null && req.Options[i].AuraKind == AuraKind.ImmuneLow) { idx = i; break; }
            }

            if (idx < 0) return "防御拍没有给出「免疫 ≤3」的光环选项";

            r.Engine.Submit(DecisionResponse.WithAuras(1, new int[0], new int[] { idx }));
            r.Advance();

            DefenseResolvedEvent def = r.FindDefense();
            if (def == null) return "没有防御结算事件";
            if (!def.UsedImmune || !def.Success) return "免疫没有成立";
            if (al.AuraTokens != 0) return "免疫指示物没被消耗";
            return null;
        }

        // ── 探针 26：守护 ──────────────────────────────────────
        private static string ProbeGuard()
        {
            Table r = Table.Begin(227);
            r.Prepare(new[] { "f" }, new[] { "b", "d" });
            r.SetCooling(0);
            r.SetCooling(1);
            r.ToAttackTurn();

            if (!r.PlayCard("f")) return r.Trouble;
            r.Advance();

            DecisionRequest req = r.Engine.Pending;
            if (req == null || req.Kind != RequestKind.ChooseDefense) return "没有进入防御拍";

            int idx = -1;
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (req.Options[i].Kind == OptionKind.PlayCard
                    && req.Options[i].Card != null && req.Options[i].Card.Def.Id == "b") { idx = i; break; }
            }

            if (idx < 0) return "冰风暴（5）没有被列为「9 点攻击」的合法防御牌（守护没生效）";

            r.Submit(idx);
            r.Advance();

            DefenseResolvedEvent def = r.FindDefense();
            if (def == null) return "没有防御结算事件";
            if (!def.Success) return "守护牌居然没挡住";
            if (r.Engine.State.Of(1).Hp != 4) return "守护成功却掉血了";
            return null;
        }

        // ══════════════════════════════════════════════════════
        //  5) 驱动：一局可布置的对局
        // ══════════════════════════════════════════════════════

        private sealed class Table
        {
            public BattleEngine Engine;
            public readonly List<BattleEvent> Events = new List<BattleEvent>();
            public string Trouble = string.Empty;

            public void Advance()
            {
                Engine.Advance();
                for (int guard = 0; Engine.Pending != null && Engine.Pending.Kind == RequestKind.ResolveOrder; guard++)
                {
                    if (guard >= 100) throw new InvalidOperationException("Effect confirmation loop");
                    Engine.Submit(DecisionResponse.Of(Engine.Pending.Seat, 0));
                    Engine.Advance();
                }
            }

            public static Table Begin(int seed)
            {
                Table r = new Table();
                r.Engine = BattleEngine.Create(seed);
                r.Engine.OnEvent += r.OnEvent;
                r.Engine.Start();
                r.Advance();

                // ⚠ **只停在换牌拍、不代答** —— 手牌必须在这一拍之前布置好，
                //   因为出牌选项是在「推进到出牌拍」那一刻按当时的手牌建出来的。
                return r;
            }

            private void OnEvent(BattleEvent e)
            {
                Events.Add(e);
            }

            // ── 布置 ────────────────────────────────────────────

            /// <summary>把某个座位的手牌换成指定的几张（必须在换牌拍提交之前调用）。</summary>
            public void Prepare(string[] hand0, string[] hand1)
            {
                SetHand(0, hand0);
                SetHand(1, hand1);
            }

            private void SetHand(int seat, string[] ids)
            {
                PlayerState p = Engine.State.Of(seat);
                p.Hand.Clear();
                for (int i = 0; i < ids.Length; i++)
                {
                    CardInstance c = Engine.State.CreateCard(CardLibrary.Get(ids[i]), seat);
                    c.ToHand();
                    p.Hand.Add(c);
                }
            }

            /// <summary>布置冷却区：<c>"a:2"</c> = 暴风雪、剩余冷却 2。</summary>
            public void SetCooling(int seat, params string[] specs)
            {
                PlayerState p = Engine.State.Of(seat);
                p.CoolingZone.Clear();
                for (int i = 0; i < specs.Length; i++)
                {
                    string spec = specs[i];
                    int colon = spec.IndexOf(':');
                    string id = colon < 0 ? spec : spec.Substring(0, colon);
                    int cd = colon < 0 ? CardLibrary.Get(id).Cooldown : int.Parse(spec.Substring(colon + 1));
                    CardInstance c = Engine.State.CreateCard(CardLibrary.Get(id), seat);
                    CooldownOps.PutIntoCooldown(p, c, cd, null);
                }
            }

            /// <summary>把这张牌卡面上的光环全部点亮（等价于「它已经作为进攻牌打出过」）。</summary>
            public void GiveAura(CardInstance card)
            {
                List<EffectDef> auras = AuraResolver.AuraEffectsOf(card.Def);
                for (int i = 0; i < auras.Count; i++)
                {
                    card.AddAura(auras[i]);
                }
            }

            public void ConsumeAllAura(CardInstance card)
            {
                while (card.AuraTokens > 0)
                {
                    if (!AuraResolver.Consume(card)) break;
                }
            }

            // ── 推进 ────────────────────────────────────────────

            /// <summary>推进到「座位 0 的出牌拍」（沿途的换牌 / 其它决策一律跳过）。</summary>
            public void ToAttackTurn()
            {
                int guard = 0;
                while (++guard < 200)
                {
                    DecisionRequest req = Engine.Pending;

                    if (req == null)
                    {
                        if (Engine.IsOver) { Trouble = "对局在出牌前就结束了"; return; }
                        Engine.Advance();
                        continue;
                    }

                    if (req.Kind == RequestKind.ChooseAttackCard)
                    {
                        if (req.Seat != 0) Trouble = "先手不是座位 0（" + req.Seat + "）";
                        return;
                    }

                    SubmitSkipOrEmpty(req);
                    Engine.Advance();
                }

                Trouble = "推进到出牌拍失败（次数超限）";
            }

            /// <summary>
            /// 从当前拍往前推进，直到出现指定类型的决策；
            /// 若先撞上「座位 1 的出牌拍」（= 座位 0 的半场已经走完）则返回 null。
            /// </summary>
            public DecisionRequest AdvanceToDecision(RequestKind kind)
            {
                int guard = 0;
                while (++guard < 200)
                {
                    DecisionRequest req = Engine.Pending;

                    if (req == null)
                    {
                        if (Engine.IsOver) return null;
                        Engine.Advance();
                        continue;
                    }

                    if (req.Kind == kind) return req;
                    if (req.Kind == RequestKind.ChooseAttackCard && req.Seat == 1) return null;

                    Answer(req);
                    Engine.Advance();
                }

                return null;
            }

            /// <summary>最近几条事件的摘要（失败时贴进报告，免得靠猜）。</summary>
            public string Dump(int count)
            {
                var sb = new System.Text.StringBuilder();
                int start = Events.Count - count;
                if (start < 0) start = 0;
                for (int i = start; i < Events.Count; i++)
                {
                    sb.Append("\n          " + Events[i].Describe());
                }

                return sb.ToString();
            }

            /// <summary>推进到指定类型的决策（潮汐 γ 要在出牌拍**之前**停下来）。</summary>
            public void AdvanceUntilKind(RequestKind kind)
            {
                int guard = 0;
                while (++guard < 200)
                {
                    DecisionRequest req = Engine.Pending;

                    if (req == null)
                    {
                        if (Engine.IsOver) { Trouble = "对局提前结束"; return; }
                        Engine.Advance();
                        continue;
                    }

                    if (req.Kind == kind) return;

                    SubmitSkipOrEmpty(req);
                    Engine.Advance();
                }

                Trouble = "没有等到 " + kind + " 决策";
            }

            /// <summary>从当前出牌拍一路跑到「座位 1 的出牌拍」（= 座位 0 的半场收尾）。</summary>
            public void RunToSeat1Attack()
            {
                Engine.Advance();
                int guard = 0;
                while (Engine.Pending != null && ++guard < 300)
                {
                    DecisionRequest req = Engine.Pending;
                    if (req.Kind == RequestKind.ChooseAttackCard && req.Seat == 1) return;
                    Answer(req);
                    Engine.Advance();
                }

                if (guard >= 300) Trouble = "半场推进次数超限（疑似状态机卡住）";
                else if (Engine.Pending == null && !Engine.IsOver)
                {
                    Trouble = "半场没有走到对手的出牌拍（推进中断）";
                }
                else if (Engine.IsOver)
                {
                    Trouble = "对局在对手出牌拍之前就结束了";
                }
            }

            private void Answer(DecisionRequest req)
            {
                if (req.Kind == RequestKind.ChooseReplace || req.Kind == RequestKind.ChooseDefense
                    || req.Kind == RequestKind.ChooseAttackCard)
                {
                    // 防御：能挡就挡（挡不住时只有 Skip）
                    for (int i = 0; i < req.Options.Count; i++)
                    {
                        if (req.Options[i].Kind == OptionKind.PlayCard)
                        {
                            Submit(i);
                            return;
                        }
                    }

                    SubmitSkipOrEmpty(req);
                    return;
                }

                // 其余（加速 / 减速 / 区域 / 刷新 / 复制 / 偷看 / 送冷却）：取第一个合法项
                for (int i = 0; i < req.Options.Count; i++)
                {
                    if (!req.Options[i].IsSkip && req.Options[i].Kind != OptionKind.Done)
                    {
                        Submit(i);
                        return;
                    }
                }

                SubmitSkipOrEmpty(req);
            }

            // ── 回填 ────────────────────────────────────────────

            public bool PlayCard(string id)
            {
                DecisionRequest req = Engine.Pending;
                if (req == null || req.Kind != RequestKind.ChooseAttackCard)
                {
                    Trouble = "不在出牌拍（" + (req == null ? "null" : req.Kind.ToString()) + "）";
                    return false;
                }

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Card != null && o.AuraSource == null && o.Card.Def.Id == id)
                    {
                        Engine.Submit(DecisionResponse.Of(req.Seat, i));
                        return true;
                    }
                }

                Trouble = "手里没有 " + id;
                return false;
            }

            public void Submit(int index)
            {
                Engine.Submit(DecisionResponse.Of(Engine.Pending.Seat, index));
            }

            public void SubmitEmpty()
            {
                Engine.Submit(DecisionResponse.Of(Engine.Pending.Seat));
            }

            public void SubmitSkipOrEmpty(DecisionRequest req)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    if (req.Options[i].IsSkip)
                    {
                        Engine.Submit(DecisionResponse.Of(req.Seat, i));
                        return;
                    }
                }

                Engine.Submit(DecisionResponse.Of(req.Seat));
            }

            public int IndexOfOptionCard(string id)
            {
                DecisionRequest req = Engine.Pending;
                if (req == null) return -1;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    if (req.Options[i].Card != null && req.Options[i].Card.Def.Id == id
                        && req.Options[i].AuraSource == null)
                    {
                        return i;
                    }
                }

                return -1;
            }

            // ── 查询 ────────────────────────────────────────────

            public CardInstance FindAny(string id)
            {
                for (int seat = 0; seat < Engine.State.Players.Count; seat++)
                {
                    PlayerState p = Engine.State.Of(seat);
                    for (int i = 0; i < p.Hand.Count; i++)
                    {
                        if (p.Hand[i].Def.Id == id) return p.Hand[i];
                    }

                    for (int i = 0; i < p.CoolingZone.Count; i++)
                    {
                        if (p.CoolingZone[i].Def.Id == id) return p.CoolingZone[i];
                    }
                }

                return null;
            }

            public CardInstance FindCooling(int seat, string id)
            {
                PlayerState p = Engine.State.Of(seat);
                for (int i = 0; i < p.CoolingZone.Count; i++)
                {
                    if (p.CoolingZone[i].Def.Id == id) return p.CoolingZone[i];
                }

                return null;
            }

            /// <summary>最近一条「原因匹配 + 目标牌匹配」的冷却改动（卡牌为 null 的占位不算）。</summary>
            public CooldownChangedEvent FindChange(string reasonLike, string cardId)
            {
                for (int i = Events.Count - 1; i >= 0; i--)
                {
                    CooldownChangedEvent e = Events[i] as CooldownChangedEvent;
                    if (e == null || e.Change.Card == null) continue;
                    if (e.Change.Reason == null
                        || e.Change.Reason.IndexOf(reasonLike, StringComparison.Ordinal) < 0) continue;
                    if (cardId != null && e.Change.Card.Def.Id != cardId) continue;
                    return e;
                }

                return null;
            }

            public int CountChanges(string reasonLike)
            {
                int n = 0;
                for (int i = 0; i < Events.Count; i++)
                {
                    CooldownChangedEvent e = Events[i] as CooldownChangedEvent;
                    if (e == null || e.Change.Card == null || e.Change.Reason == null) continue;
                    if (e.Change.Reason.IndexOf(reasonLike, StringComparison.Ordinal) >= 0) n++;
                }

                return n;
            }

            public AttackPowerResolvedEvent FindAttack(string id)
            {
                for (int i = Events.Count - 1; i >= 0; i--)
                {
                    AttackPowerResolvedEvent e = Events[i] as AttackPowerResolvedEvent;
                    if (e != null && e.Card != null && e.Card.Def.Id == id) return e;
                }

                return null;
            }

            public DefenseResolvedEvent FindDefense()
            {
                for (int i = Events.Count - 1; i >= 0; i--)
                {
                    DefenseResolvedEvent e = Events[i] as DefenseResolvedEvent;
                    if (e != null) return e;
                }

                return null;
            }
        }
    }
}
