using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-29 · <b>全卡表改版新增的那四张牌</b>的结构自测。
    ///
    /// <para><b>它顶掉了什么</b>：原来这份场景测的是 2026-10-01 的三张牌
    /// （水之形 <c>aq</c> / 闪电球 <c>ar</c> / 冷冻核心 <c>as</c>）——
    /// 那三张在新卡表里已经全部删除，各自的效果也换了主人。现在测的是真正新增的四张：</para>
    /// <list type="bullet">
    /// <item><b>冰霜核心 ao</b>（冰 6/3）：α 减速 + α 虚弱 ×1 给对手；</item>
    /// <item><b>火炬 ap</b>（火 3/4）：α 光环攻 +3 + γ「掉血复位」（<c>RestoreAuraOnHpLoss</c>）；</item>
    /// <item><b>地刺 ar</b>（石 9/4）：α 条件回手（<c>ReturnToHandIfLastHand</c> + hand-at-play = 1）；</item>
    /// <item><b>漩涡 aa</b>（水 5/3）：α 加速 + α 光环快速回填。</item>
    /// </list>
    ///
    /// <para><b>为什么「注册 + Validate」要单独一条</b>：卡面写了一个算子、但引擎侧忘了
    /// <c>registry.Register</c> 的话，编译期一切正常，<b>进战斗那一刻才抛
    /// 「Unregistered effect」</b>；而 <c>EffectTrigger.Special</c> 忘了写 <c>specialEvent</c>
    /// 也是同一类硬错（Validate 里直接抛）。这两件事在万局统计里都表现为「这一局崩了」，
    /// 却看不出崩在哪张牌上 —— 所以这里把 44 张的每一条效果都过一遍。</para>
    /// </summary>
    internal static class NewCardsScenario
    {
        /// <summary>冰霜核心（冰 · 6 / 3）。</summary>
        private const string FrostCoreId = "ao";

        /// <summary>火炬（火 · 3 / 4）。</summary>
        private const string TorchId = "ap";

        /// <summary>地刺（石 · 9 / 4）。</summary>
        private const string SpikeId = "ar";

        /// <summary>漩涡（水 · 5 / 3）。</summary>
        private const string VortexId = "aa";

        public static bool Run(List<string> report)
        {
            bool defs = CheckCardDefs(report);
            bool handlers = CheckHandlers(report);
            return defs && handlers;
        }

        private static string Flag(bool ok)
        {
            return ok ? "  [PASS] " : "  [FAIL] ";
        }

        // ══════════════════════════════════════════════════════
        //  1) 四张新卡的卡表结构
        // ══════════════════════════════════════════════════════

        private static bool CheckCardDefs(List<string> report)
        {
            var bad = new List<string>();

            // ── 冰霜核心 ──
            CardDef frost = CardLibrary.Get(FrostCoreId);
            Need(bad, "冰霜核心", frost.Power == 6, "力量应为 6，实为 " + frost.Power);
            Need(bad, "冰霜核心", frost.Cooldown == 3, "冷却应为 3，实为 " + frost.Cooldown);
            Need(bad, "冰霜核心", frost.Element == CardElement.Ice, "元素应为冰，实为 " + frost.Element);
            Need(bad, "冰霜核心", HasOp(frost, EffectTrigger.Attack, EffectOp.Slow, 1), "缺 α 减速 ×1");
            Need(bad, "冰霜核心", HasOp(frost, EffectTrigger.Attack, EffectOp.Weaken, 1),
                "缺 α 虚弱 ×1（「使对方下次进攻时力量减半」用的就是 Weaken 的折半口径）");
            Need(bad, "冰霜核心", !frost.HasAura, "不该带光环");

            // ── 火炬 ──
            CardDef torch = CardLibrary.Get(TorchId);
            Need(bad, "火炬", torch.Power == 3, "力量应为 3，实为 " + torch.Power);
            Need(bad, "火炬", torch.Cooldown == 4, "冷却应为 4，实为 " + torch.Cooldown);
            Need(bad, "火炬", torch.Element == CardElement.Fire, "元素应为火，实为 " + torch.Element);
            Need(bad, "火炬", HasAura(torch, EffectTrigger.Attack, AuraKind.AtkPower, 3),
                "缺 α 光环：进攻力量 +3");
            Need(bad, "火炬", torch.AuraTokenCount == 1, "卡面光环条数应为 1，实为 " + torch.AuraTokenCount);
            Need(bad, "火炬", HasSpecialEvent(torch, EffectOp.RestoreAuraOnHpLoss, "owner.hp.lost"),
                "缺 γ「掉血复位」（RestoreAuraOnHpLoss + specialEvent = owner.hp.lost）");

            // ── 地刺 ──
            CardDef spike = CardLibrary.Get(SpikeId);
            Need(bad, "地刺", spike.Power == 9, "力量应为 9，实为 " + spike.Power);
            Need(bad, "地刺", spike.Cooldown == 4, "冷却应为 4，实为 " + spike.Cooldown);
            Need(bad, "地刺", spike.Element == CardElement.Stone, "元素应为石，实为 " + spike.Element);
            Need(bad, "地刺", HasHandAtPlay(spike, EffectOp.ReturnToHandIfLastHand, 1),
                "缺 α 条件回手（ReturnToHandIfLastHand + hand-at-play = 1）");
            Need(bad, "地刺", !spike.HasAura, "不该带光环");

            // ── 漩涡 ──
            CardDef vortex = CardLibrary.Get(VortexId);
            Need(bad, "漩涡", vortex.Power == 5, "力量应为 5，实为 " + vortex.Power);
            Need(bad, "漩涡", vortex.Cooldown == 3, "冷却应为 3，实为 " + vortex.Cooldown);
            Need(bad, "漩涡", vortex.Element == CardElement.Water, "元素应为水，实为 " + vortex.Element);
            Need(bad, "漩涡", HasOp(vortex, EffectTrigger.Attack, EffectOp.Haste, 1), "缺 α 加速 ×1");
            Need(bad, "漩涡", HasAura(vortex, EffectTrigger.Attack, AuraKind.QuickRefill, 1),
                "缺 α 光环：快速回填（AuraKind.QuickRefill）");
            Need(bad, "漩涡", vortex.AuraTokenCount == 1, "卡面光环条数应为 1，实为 " + vortex.AuraTokenCount);

            report.Add(Flag(bad.Count == 0)
                       + "2026-10-29 新卡结构：冰霜核心（冰 6/3 · 减速 + 虚弱 ×1）、"
                       + "火炬（火 3/4 · 攻 +3 光环 + 掉血复位）、"
                       + "地刺（石 9/4 · 最后一张手牌时打完回手）、"
                       + "漩涡（水 5/3 · 加速 + 快速回填光环）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        // ══════════════════════════════════════════════════════
        //  2) 每个算子都必须在注册表里，且每条 γ 都带事件 id
        // ══════════════════════════════════════════════════════

        private static bool CheckHandlers(List<string> report)
        {
            var bad = new List<string>();
            EffectRegistry registry = EffectRegistry.CreateDefault();

            // ① 三个 2026-10-29 新增的算子：注册了、阶段对。
            ExpectStage(bad, registry, EffectOp.RestoreAuraOnHpLoss, EffectStage.Cooldown);
            ExpectStage(bad, registry, EffectOp.ReturnToHandIfLastHand, EffectStage.AfterCooldown);
            ExpectStage(bad, registry, EffectOp.RuleNote, EffectStage.Power);

            // ② 全卡表逐条 Validate —— 漏注册 / γ 没写事件 id 都会在这里抛。
            int checkedCount = 0;
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                for (int e = 0; e < def.Effects.Count; e++)
                {
                    EffectDef effect = def.Effects[e];
                    checkedCount++;
                    try
                    {
                        registry.Validate(effect);
                    }
                    catch (Exception ex)
                    {
                        bad.Add(def.Name + "（" + def.Id + "）第 " + e + " 条「" + effect.Text
                                + "」→ " + ex.Message);
                    }
                }
            }

            report.Add(Flag(bad.Count == 0)
                       + "算子注册表：新增的 3 个算子都在，且全卡表 " + checkedCount
                       + " 条效果逐条 Validate 通过（没有「卡面写了、引擎里没有」的死效果）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

        private static void ExpectStage(List<string> bad, EffectRegistry registry, EffectOp op, EffectStage want)
        {
            try
            {
                EffectStage got = registry.Get(op.ToString()).Stage;
                if (got != want)
                {
                    bad.Add(op + " 的阶段应为 " + want + "，实为 " + got);
                }
            }
            catch (Exception ex)
            {
                bad.Add(op + " 没有注册：" + ex.Message);
            }
        }

        // ── 小小查询工具（本文件自用）────────────────────────

        private static void Need(List<string> bad, string label, bool ok, string trouble)
        {
            if (!ok)
            {
                bad.Add(label + "：" + trouble);
            }
        }

        private static bool HasOp(CardDef def, EffectTrigger trigger, EffectOp op, int amount)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Trigger == trigger && e.Op == op && (amount == 0 || e.A == amount))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAura(CardDef def, EffectTrigger trigger, AuraKind kind, int value)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == EffectOp.Aura && e.Trigger == trigger && e.Aura == kind && e.A == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSpecialEvent(CardDef def, EffectOp op, string eventId)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == op && e.Trigger == EffectTrigger.Special && e.SpecialEvent == eventId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHandAtPlay(CardDef def, EffectOp op, int value)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op != op)
                {
                    continue;
                }

                for (int c = 0; c < e.Conditions.Count; c++)
                {
                    if (e.Conditions[c].Id == "hand-at-play" && e.Conditions[c].Value == value)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
