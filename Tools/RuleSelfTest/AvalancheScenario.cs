using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-25 · 雪崩（an）从「可选的单效果」改成「双效果」之后的行为自测。
    ///
    /// <para><b>改了什么</b>：</para>
    /// <list type="number">
    /// <item>① 强制区域减速 —— 双方冷却区中剩余冷却 = 1 的牌全体 +1，<b>不发决策、不给放弃</b>
    /// （原来和区域加速 / 减速共用「选一个剩余冷却值」那一拍，玩家可以选「不执行」）；</item>
    /// <item>② 快速回填 —— 本牌进冷却区时剩余冷却额外 −1。</item>
    /// </list>
    ///
    /// <para><b>为什么必须脚本化逼出来</b>：这两条都不会让万局统计变红 ——
    /// 「少发了一个决策」不影响事件流自洽，「快速回填」少结算也只是冷却多 1。
    /// 只有把局面钉死才看得出来：</para>
    /// <list type="bullet">
    /// <item>减速这一半：<b>双方各布置一张剩余 = 1 的牌 + 一张剩余 = 2 的诱饵</b>。
    /// 前者必须 1→2，后者必须一动不动（证明匹配的是「= 1」而不是「≤ 1」或「全部」）；</item>
    /// <item>「双方」这一条：两侧各有一张被减速，才算真的作用了双方
    /// （不是只挑一方跑一遍）；</item>
    /// <item>「不发决策」：从防御结算完到下一个决策之间，<b>不得</b>出现
    /// <c>RequestKind.ChooseZoneValue</c>；</item>
    /// <item>快速回填：雪崩自己进冷却区的那条改动必须是 <c>0 → 基础冷却 − 1</c>。</item>
    /// </list>
    ///
    /// <para><b>局面怎么布置</b>：不去打乱发牌，而是在<b>进攻方选牌那一拍</b>
    /// 直接把牌从手里按指定初始冷却送进冷却区 —— <c>CooldownOps.PutIntoCooldown</c>
    /// 正是引擎自己的入口，走它不会绕过任何不变量。</para>
    /// </summary>
    internal static class AvalancheScenario
    {
        /// <summary>雪崩的卡 ID。</summary>
        private const string AvalancheId = "an";

        /// <summary>区域减速的 reason（<c>CooldownOps.SlowZone</c> 写死的那一个）。</summary>
        private const string ZoneReason = "区域减速";

        /// <summary>本条要收集几次「布置成」的样本才算证明。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可布置的局面。</summary>
        private const int MaxSeeds = 400;

        public static bool Run(List<string> report)
        {
            bool defOk = CheckCardDef(out string defWhy);
            report.Add((defOk ? "  [PASS] " : "  [FAIL] ")
                       + "雪崩（an）的 α = 强制区域减速（阈值 1）+ 快速回填（两条）" + defWhy);

            var result = new CaseResult();

            for (int seed = 1; seed <= MaxSeeds && result.Hits < NeededHits; seed++)
            {
                result.Scanned++;
                Hit hit = RunOne(seed);

                if (hit == null)
                {
                    result.Scanned--;
                    continue;   // 这一局没逼出「手上正好有雪崩 + 两侧手牌够摆」的时机
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

                if (!hit.Ok)
                {
                    result.Failures.Add("seed " + seed + " → " + hit.Why);
                }
            }

            report.Add((result.Ok ? "  [PASS] " : "  [FAIL] ")
                       + "雪崩打出后：双方冷却区中剩余 = 1 的牌被强制 +1（不弹区域决策）、"
                       + "剩余 = 2 的诱饵不动、本牌进冷却区时快速回填 −1"
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

            return defOk && result.Ok;
        }

        // ══════════════════════════════════════════════════════
        //  卡面 / 结构自检
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 卡表这一处必须钉住：α 恰好是「区域减速（阈值 1）」+「快速回填」两条。
        ///
        /// <para>⚠ 卡面文字里不该再出现「<b>可</b>使…」—— 这一条已经是强制的，
        /// 文案留着「可」会让玩家以为还能不执行。</para>
        /// </summary>
        private static bool CheckCardDef(out string why)
        {
            CardDef def = CardLibrary.Get(AvalancheId);

            EffectDef slow = null;
            bool quick = false;

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef ef = def.Effects[i];
                if (ef.Trigger != EffectTrigger.Attack)
                {
                    continue;
                }

                if (ef.Op == EffectOp.SlowZoneBoth)
                {
                    slow = ef;
                }
                else if (ef.Op == EffectOp.QuickRefill)
                {
                    quick = true;
                }
            }

            if (slow == null)
            {
                why = "：没找到 α 的 SlowZoneBoth 效果 —— 卡表被改回去了？";
                return false;
            }

            if (slow.A != 1)
            {
                why = "：区域减速的阈值 A 应为 1，实为 " + slow.A;
                return false;
            }

            if (!quick)
            {
                why = "：缺少 α 的快速回填（QuickRefill）—— 双效果只落地了一半";
                return false;
            }

            if (slow.Text != null && slow.Text.IndexOf('可') >= 0)
            {
                why = "：卡面文字仍写着「可…」（「" + slow.Text + "」）—— 这一条已经是强制的了";
                return false;
            }

            why = "（阈值 A = " + slow.A + "，卡面「" + slow.Text + "」+「快速回填」）";
            return true;
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

        private static Hit RunOne(int seed)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine);

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
            /// <summary>真的布置成局面了（摆好了两侧的牌 + 打出了雪崩）。</summary>
            public bool Armed;

            /// <summary>观察窗口正常收尾（防御结算之后的第一个决策到了）。</summary>
            public bool Observed;

            /// <summary>观察窗口里出现了区域选值决策 —— 说明还在弹「选一个剩余冷却值」。</summary>
            public bool ZoneAsked;

            /// <summary>进攻方那一侧「剩余 = 1」的牌被 1→2。</summary>
            public bool SlowAttackerSide;

            /// <summary>防御方那一侧「剩余 = 1」的牌被 1→2。</summary>
            public bool SlowDefenderSide;

            /// <summary>诱饵（剩余 = 2）被区域减速蹭到了。</summary>
            public bool DecoySlowed;

            /// <summary>雪崩自己进冷却区时观察到的剩余冷却（−1 = 没观察到）。</summary>
            public int AvalancheCd = -1;

            public bool Stalled;
            public string Error;

            public bool Ok
            {
                get
                {
                    return Armed && Observed && !ZoneAsked
                           && SlowAttackerSide && SlowDefenderSide && !DecoySlowed
                           && AvalancheCd == ExpectedAvalancheCd;
                }
            }

            /// <summary>雪崩进冷却区时应有的剩余冷却 = 基础冷却 − 1（快速回填）。</summary>
            public int ExpectedAvalancheCd = -1;

            public string Why
            {
                get
                {
                    if (ZoneAsked)
                    {
                        return "雪崩仍然弹出了「选一个剩余冷却值」的区域决策 —— 强制化没落地";
                    }

                    if (!SlowAttackerSide || !SlowDefenderSide)
                    {
                        return "区域减速没有作用到双方（进攻方侧 " + SlowAttackerSide
                               + " / 防御方侧 " + SlowDefenderSide + "）—— 「双方」只跑了一侧，或根本没结算";
                    }

                    if (DecoySlowed)
                    {
                        return "剩余 = 2 的诱饵也被减速了 —— 匹配条件比「= 1」宽";
                    }

                    if (AvalancheCd != ExpectedAvalancheCd)
                    {
                        return "雪崩进冷却区的剩余冷却 = " + AvalancheCd
                               + "，期望 " + ExpectedAvalancheCd + "（基础冷却 − 1）—— 快速回填没结算";
                    }

                    return "未知";
                }
            }
        }

        private sealed class Runner
        {
            private readonly BattleEngine _engine;

            /// <summary>已经布置过了（只布置一次）。</summary>
            private bool _armed;

            /// <summary>布置那一拍的防御已经结算完 —— 接下来第一个决策就是观察收尾点。</summary>
            private bool _defenseDone;

            private CardInstance _card;         // 打出的雪崩
            private CardInstance _hitSelf;      // 进攻方侧：剩余 1
            private CardInstance _decoySelf;    // 进攻方侧：剩余 2（诱饵）
            private CardInstance _hitEnemy;     // 防御方侧：剩余 1
            private CardInstance _decoyEnemy;   // 防御方侧：剩余 2（诱饵）

            public readonly Hit Hit = new Hit();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                if (e is DefenseResolvedEvent && _armed)
                {
                    _defenseDone = true;
                }

                var cd = e as CooldownChangedEvent;
                if (cd == null || !_armed || cd.Change.Card == null)
                {
                    return;
                }

                CooldownChange c = cd.Change;

                if (c.Reason == ZoneReason)
                {
                    if (c.Card == _hitSelf)
                    {
                        Hit.SlowAttackerSide = c.From == 1 && c.To == 2;
                    }
                    else if (c.Card == _hitEnemy)
                    {
                        Hit.SlowDefenderSide = c.From == 1 && c.To == 2;
                    }
                    else if (c.Card == _decoySelf || c.Card == _decoyEnemy)
                    {
                        Hit.DecoySlowed = true;
                    }
                }

                if (c.Card == _card && c.Reason == "本次进攻牌进入冷却区")
                {
                    Hit.AvalancheCd = c.To;
                }
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

                if (_defenseDone && !Stop)
                {
                    // 防御结算之后的第一个决策 = ③④⑤ 都走完了。
                    // 它竟然还是区域选值 → 雪崩没强制化。
                    if (req.Kind == RequestKind.ChooseZoneValue)
                    {
                        Hit.ZoneAsked = true;
                    }

                    Hit.Observed = true;
                    Stop = true;
                }
                else if (_armed && req.Kind == RequestKind.ChooseZoneValue)
                {
                    // ③ 在防御之后，这里不该出现区域决策（留着兜底，防观察窗口提前断）
                    Hit.ZoneAsked = true;
                }

                return First(req);
            }

            /// <summary>
            /// 挑一次进攻机会布置局面：手上要有雪崩 + 至少 1 张别的牌（当「剩余 = 1」），
            /// 再要 1 张当诱饵；对方手上也要 2 张。布置完就强制打出雪崩。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_armed)
                {
                    return First(req);
                }

                Option avalanche = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Card != null && o.Card.Def.Id == AvalancheId)
                    {
                        avalanche = o;
                        break;
                    }
                }

                if (avalanche == null)
                {
                    return First(req);   // 这一拍手上没雪崩 → 换种子
                }

                PlayerState attacker = _engine.State.Of(req.Seat);
                PlayerState defender = _engine.State.Of(1 - req.Seat);

                CardInstance hitSelf = Pick(attacker.Hand, avalanche.Card, null);
                CardInstance decoySelf = Pick(attacker.Hand, avalanche.Card, hitSelf);
                CardInstance hitEnemy = Pick(defender.Hand, null, null);
                CardInstance decoyEnemy = Pick(defender.Hand, null, hitEnemy);

                if (hitSelf == null || decoySelf == null || hitEnemy == null || decoyEnemy == null)
                {
                    return First(req);   // 手牌不够摆 → 换种子
                }

                // ⚠ 这一拍的上半场「冷却 −1」已经结算过了（TurnStartedEvent 之前），
                //   所以这里写进去的值就是第 ③ 步会看到的值。
                // ⚠ sink 传 null：布置本身不该混进事件流，否则「诱饵有没有被动」就说不清了。
                CooldownOps.PutIntoCooldown(attacker, hitSelf, 1, null);
                CooldownOps.PutIntoCooldown(attacker, decoySelf, 2, null);
                CooldownOps.PutIntoCooldown(defender, hitEnemy, 1, null);
                CooldownOps.PutIntoCooldown(defender, decoyEnemy, 2, null);

                _card = avalanche.Card;
                _hitSelf = hitSelf;
                _decoySelf = decoySelf;
                _hitEnemy = hitEnemy;
                _decoyEnemy = decoyEnemy;

                _armed = true;
                Hit.Armed = true;
                Hit.ExpectedAvalancheCd = _card.Def.Cooldown - 1;

                return DecisionResponse.Of(req.Seat, avalanche.Index);
            }

            /// <summary>从手牌里挑一张不是 <paramref name="a"/> / <paramref name="b"/> 的牌。</summary>
            private static CardInstance Pick(List<CardInstance> hand, CardInstance a, CardInstance b)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    if (hand[i] != a && hand[i] != b)
                    {
                        return hand[i];
                    }
                }

                return null;
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
