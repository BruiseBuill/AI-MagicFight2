using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-25 · 模仿（x）的复制条件从「基础冷却 <b>= 3</b> 且无光环」放宽成
    /// 「基础冷却 <b>≤ 3</b> 且无光环」之后的候选表自测。
    ///
    /// <para><b>为什么必须脚本化逼出来</b>：这条改动的两处表达 ——
    /// 卡面文字（<c>CardLibrary</c>）与候选过滤（<c>BattleEngine.IssueCopyTarget</c>）——
    /// 只改了文字、漏改过滤时，表现是「卡面写着能复制，界面上一个候选都没有」；
    /// 只改过滤、漏改文字则相反。两者在万局统计里<b>完全无声</b>
    /// （模仿照样能用，只是复制到的东西不对 / 复制不到）。</para>
    ///
    /// <para><b>四个探针</b>（每个都保证<b>进攻方冷却区里除注入的那一张外没有别的候选</b>，
    /// 这样断言才不靠运气）：</para>
    /// <list type="bullet">
    /// <item>基础冷却 3 且无光环 → <b>必须</b>出现在候选里（旧行为不能退化）；</item>
    /// <item>基础冷却 2 且无光环 → <b>必须</b>出现在候选里（<b>本次新增的能力</b>，
    /// 旧实现这里是空的）；</item>
    /// <item>基础冷却 4 且无光环 → <b>不得</b>出现（放宽没有放过头）；</item>
    /// <item>基础冷却 ≤3 但有光环 → <b>不得</b>出现（「无光环」这一半没被一起放宽掉）。</item>
    /// </list>
    ///
    /// <para>局面布置走引擎自己的入口 <c>CooldownOps.PutIntoCooldown</c>，
    /// 不去打乱发牌、也不手改牌上的字段。</para>
    /// </summary>
    internal static class MimicScenario
    {
        private const string MimicId = "x";
        private const int NeededHits = 3;
        private const int MaxSeeds = 400;

        /// <summary>探针：往进攻方冷却区里放哪一种牌。</summary>
        private enum Probe
        {
            /// <summary>基础冷却 = 3 且无光环（旧行为，必须仍然可选）。</summary>
            BaseCd3NoAura,

            /// <summary>基础冷却 = 2 且无光环（本次放宽后才该可选）。</summary>
            BaseCd2NoAura,

            /// <summary>基础冷却 = 4 且无光环（超出上限，不可选）。</summary>
            BaseCd4NoAura,

            /// <summary>基础冷却 ≤ 3 但有光环（无光环这一半，不可选）。</summary>
            BaseCd3WithAura,
        }

        public static bool Run(List<string> report)
        {
            bool defOk = CheckCardDef(out string defWhy);
            report.Add((defOk ? "  [PASS] " : "  [FAIL] ")
                       + "模仿（x）的 α 复制上限 A = 3，卡面写「基础冷却 ≤ 3 且无光环」" + defWhy);

            bool ok = defOk;

            ok &= One(report, Probe.BaseCd3NoAura, true,
                "基础冷却 = 3 且无光环的牌仍可复制");

            ok &= One(report, Probe.BaseCd2NoAura, true,
                "基础冷却 = 2 且无光环的牌也可复制（本次放宽）");

            ok &= One(report, Probe.BaseCd4NoAura, false,
                "基础冷却 = 4 的牌不可复制（放宽没过头）");

            ok &= One(report, Probe.BaseCd3WithAura, false,
                "有光环的牌不可复制（「无光环」这一半没被放宽）");

            return ok;
        }

        /// <summary>跑一条探针，并把结果写进报告。</summary>
        private static bool One(List<string> report, Probe probe, bool expectOffered, string what)
        {
            var result = new CaseResult();

            for (int seed = 1; seed <= MaxSeeds && result.Hits < NeededHits; seed++)
            {
                result.Scanned++;
                Hit hit = RunOne(seed, probe);

                if (hit == null)
                {
                    result.Scanned--;
                    continue;
                }

                if (hit.Error != null || hit.Stalled)
                {
                    result.Scanned--;
                    if (result.Notes.Count < 3)
                    {
                        result.Notes.Add("seed " + seed + " → " + (hit.Error ?? "推进无进展（疑似状态机卡住）"));
                    }

                    continue;
                }

                if (!hit.Armed || !hit.Observed)
                {
                    result.Scanned--;
                    continue;
                }

                result.Hits++;

                if (!hit.Ok(expectOffered))
                {
                    result.Failures.Add("seed " + seed + " → " + hit.Why(expectOffered));
                }
            }

            report.Add((result.Ok ? "  [PASS] " : "  [FAIL] ")
                       + "模仿：" + what
                       + "（命中 " + result.Hits + " 次 / 扫了 " + result.Scanned + " 个种子）");

            for (int i = 0; i < result.Failures.Count; i++)
            {
                report.Add("      · " + result.Failures[i]);
            }

            for (int i = 0; i < result.Notes.Count; i++)
            {
                report.Add("      · 杂音：" + result.Notes[i]);
            }

            if (result.Hits < NeededHits)
            {
                report.Add("      · 只逼出 " + result.Hits + " 次样本，不足以证明（检查测试设置）");
            }

            return result.Ok;
        }

        // ══════════════════════════════════════════════════════
        //  卡面 / 结构自检
        // ══════════════════════════════════════════════════════

        private static bool CheckCardDef(out string why)
        {
            CardDef def = CardLibrary.Get(MimicId);

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef ef = def.Effects[i];
                if (ef.Op != EffectOp.Copy)
                {
                    continue;
                }

                if (ef.A != 3)
                {
                    why = "：复制上限 A 应为 3，实为 " + ef.A;
                    return false;
                }

                if (ef.Text == null || ef.Text.IndexOf('≤') < 0)
                {
                    why = "：卡面文字没写「≤」（「" + ef.Text + "」）—— 放宽只落地了一半";
                    return false;
                }

                why = "（A = " + ef.A + "，卡面「" + ef.Text + "」）";
                return true;
            }

            why = "：没找到 α 的 Copy 效果 —— 卡表被改回去了？";
            return false;
        }

        // ══════════════════════════════════════════════════════
        //  一次扫描
        // ══════════════════════════════════════════════════════

        private sealed class CaseResult
        {
            public int Hits;
            public int Scanned;

            /// <summary>真实违规（算样本、算命中，但要报错）。</summary>
            public readonly List<string> Failures = new List<string>();

            /// <summary>与本条规则无关的杂音（不算样本）。</summary>
            public readonly List<string> Notes = new List<string>();

            public bool Ok
            {
                get { return Hits >= NeededHits && Failures.Count == 0; }
            }
        }

        private static Hit RunOne(int seed, Probe probe)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine, probe);

            engine.OnEvent += run.OnEvent;

            // ⚠ 必须先 Start()：引擎初始停在「未开赛」的相位。
            engine.Start();

            int guard = 0;

            try
            {
                while (!engine.IsOver && !run.Stop)
                {
                    engine.Advance();

                    if (engine.IsOver)
                    {
                        break;
                    }

                    if (++guard > 20000)
                    {
                        run.Hit.Stalled = true;
                        break;
                    }

                    if (engine.Pending == null)
                    {
                        run.Hit.Stalled = true;
                        break;
                    }

                    engine.Submit(run.Decide(engine.Pending));
                }
            }
            catch (Exception ex)
            {
                run.Hit.Error = ex.GetType().Name + ": " + ex.Message;
            }

            engine.OnEvent -= run.OnEvent;

            return run.Hit.Error != null || run.Hit.Stalled || run.Hit.Armed ? run.Hit : null;
        }

        // ══════════════════════════════════════════════════════
        //  布置 + 观察
        // ══════════════════════════════════════════════════════

        private sealed class Hit
        {
            /// <summary>真的布置成局面了（注入了探针牌 + 打出了模仿）。</summary>
            public bool Armed;

            /// <summary>拿到了布置之后的下一条决策请求。</summary>
            public bool Observed;

            /// <summary>下一条决策是不是「选复制目标」。</summary>
            public bool CopyAsked;

            /// <summary>注入的那张牌出现在候选里。</summary>
            public bool Offered;

            /// <summary>候选里的其它牌（仅作信息，正常应为 0 —— 我们要求注入前冷却区没有别的候选）。</summary>
            public int OtherCandidates;

            public bool Stalled;
            public string Error;

            public bool Ok(bool expectOffered)
            {
                return Armed && Observed && CopyAsked == expectOffered && (!expectOffered || Offered);
            }

            public string Why(bool expectOffered)
            {
                if (expectOffered && !CopyAsked)
                {
                    return "冷却区里明明有一张可复制的牌，引擎却没发复制决策 —— 候选过滤还停在旧的「= A」";
                }

                if (expectOffered && CopyAsked && !Offered)
                {
                    return "发了复制决策，但那张贴合的牌不在候选里";
                }

                if (!expectOffered && CopyAsked && Offered)
                {
                    return "不该被列进候选的牌出现在了复制候选里 —— 过滤条件写宽了";
                }

                return "未知";
            }
        }

        private sealed class Runner
        {
            private readonly BattleEngine _engine;
            private readonly Probe _probe;

            /// <summary>已经布置过了（只布置一次）。</summary>
            private bool _armed;

            private CardInstance _injected;

            public readonly Hit Hit = new Hit();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine, Probe probe)
            {
                _engine = engine;
                _probe = probe;
            }

            public void OnEvent(BattleEvent e)
            {
                // 模仿的 Copy 属 ① 阶段（EffectClassifier），所以整条断言都在决策流上看，
                // 不需要事件流 —— 这个钩子只留作以后扩展。
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        // 不替换（空回填 = 一次都不换）
                        return DecisionResponse.Of(req.Seat);

                    case RequestKind.ChooseAttackCard:
                        return DecideAttack(req);
                }

                if (_armed && !Stop)
                {
                    // 布置之后的下一条决策 —— 模仿唯一的 α 是 Copy（① 阶段），
                    // 所以「发了决策」就必然是它；没发就必然是防御拍。
                    Hit.Observed = true;
                    Hit.CopyAsked = req.Kind == RequestKind.ChooseCopyTarget;

                    if (Hit.CopyAsked)
                    {
                        for (int i = 0; i < req.Options.Count; i++)
                        {
                            Option o = req.Options[i];
                            if (o.Card == null)
                            {
                                continue;
                            }

                            if (o.Card == _injected)
                            {
                                Hit.Offered = true;
                            }
                            else
                            {
                                Hit.OtherCandidates++;
                            }
                        }
                    }

                    Stop = true;
                }

                return First(req);
            }

            /// <summary>
            /// 挑一次进攻机会布置局面：手上要有模仿 + 一张符合本次探针的牌，
            /// 且<b>冷却区里没有别的候选</b>（否则断言会被自然局面掩盖）。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_armed)
                {
                    return First(req);
                }

                Option mimic = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Card != null && o.Card.Def.Id == MimicId)
                    {
                        mimic = o;
                        break;
                    }
                }

                if (mimic == null)
                {
                    return First(req);   // 这一拍手上没模仿 → 换种子
                }

                PlayerState attacker = _engine.State.Of(req.Seat);
                CardInstance probe = null;

                for (int i = 0; i < attacker.Hand.Count; i++)
                {
                    CardInstance c = attacker.Hand[i];
                    if (c == mimic.Card)
                    {
                        continue;
                    }

                    if (Matches(c.Def, _probe))
                    {
                        probe = c;
                        break;
                    }
                }

                if (probe == null || HasOtherCandidate(attacker, null))
                {
                    return First(req);   // 手牌里没有贴合的探针牌 / 冷却区本来就有候选 → 换种子
                }

                // ⚠ 按它自己的基础冷却入场：这就是「一张正常回手后被再次打出的牌」的样子。
                CooldownOps.PutIntoCooldown(attacker, probe, probe.Def.Cooldown, null);

                _injected = probe;
                _armed = true;
                Hit.Armed = true;

                return DecisionResponse.Of(req.Seat, mimic.Index);
            }

            private static bool Matches(CardDef d, Probe probe)
            {
                switch (probe)
                {
                    case Probe.BaseCd3NoAura:
                        return d.Cooldown == 3 && !d.HasAura;

                    case Probe.BaseCd2NoAura:
                        return d.Cooldown == 2 && !d.HasAura;

                    case Probe.BaseCd4NoAura:
                        return d.Cooldown == 4 && !d.HasAura;

                    default:
                        return d.Cooldown <= 3 && d.HasAura;
                }
            }

            /// <summary>
            /// 冷却区里有没有「按新规则会被列成候选」的牌（除了 <paramref name="skip"/>）。
            ///
            /// <para>布置前必须是 false —— 这样「候选里有没有探针牌」这件事才完全由我们决定，
            /// 不会被一张自然存在的牌顶掉。</para>
            /// </summary>
            private static bool HasOtherCandidate(PlayerState p, CardInstance skip)
            {
                for (int i = 0; i < p.CoolingZone.Count; i++)
                {
                    CardInstance c = p.CoolingZone[i];
                    if (c == skip)
                    {
                        continue;
                    }

                    if (c.Def.Cooldown <= 3 && !c.Def.HasAura)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>默认应答：有「放弃」就放弃，否则选第一项（进攻拍没有放弃项，等于出第一张牌）。</summary>
            private static DecisionResponse First(DecisionRequest req)
            {
                if (req.Options != null && req.Options.Count > 0)
                {
                    Option skip = req.SkipOption;
                    return DecisionResponse.Of(req.Seat, skip != null ? skip.Index : req.Options[0].Index);
                }

                return DecisionResponse.Of(req.Seat);
            }
        }
    }
}
