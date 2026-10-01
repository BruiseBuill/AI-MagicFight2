using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-01 · 三张新卡（水之形 aq / 闪电球 ar / 冷冻核心 as）的行为自测。
    ///
    /// <para><b>为什么必须单独写一条</b>：三张卡的风险全是「卡面写了、引擎里没接上」，
    /// 而这类缺陷在万局统计里<b>一个也抓不到</b>（事件流自洽、手牌守恒、胜负分布都正常）：</para>
    /// <list type="bullet">
    /// <item><b>水之形</b>的「基础力量成长」是内核里<b>第一处写在牌实例上、跨回合不消失</b>的力量改动。
    /// 最典型的错法是直接改 <c>EffectivePower</c> —— 进冷却区 / 回手时会按 <c>Def.Power</c> 重算，
    /// 成长被静默抹掉（表现：「第一次打完加了 2，回手又变回 1」）。
    /// β 那条还多一层风险：它带着 <see cref="EffectTrigger.Defend"/>，必须被
    /// <c>BattleEngine.StageOf</c> 从「力量阶段」改派到「② 防御阶段」—— 改派漏了就是整条不结算。</item>
    /// <item><b>闪电球</b>的条件连击靠 <c>HandCountAtPlay == 1</c>。条件没挂上就等于一张
    /// 无条件连击牌（强度完全不同），而「多连击了一次」在统计里只是回合数变了变。</item>
    /// <item><b>冷冻核心</b>只是把既有的「防御力量 +N」光环换了个数值，风险集中在卡表录入
    /// （光环类型 / 数值 / 触发符号），行为侧要证明它真的会点亮、真的能被防御拍取用。</item>
    /// </list>
    ///
    /// <para><b>⚠ 已知边界（用户 2026-10-01 明确「就按字面实现」）</b>：闪电球的
    /// 「若这是你的最后一张手牌」在现行规则下<b>正例永不成立</b> —— 「连击」= 结算完
    /// <b>再打出 1 张</b>，而「最后一张手牌」意味着打出后手上为空，<c>DoStage6</c> 的
    /// <c>Hand.Count &gt; 0</c> 判据直接拦下（不会追加、也不会因此掉血）。
    /// 所以这里只能做<b>反面对着</b>：手牌 ≥2 张时不得连击 —— 条件若被漏掉，
    /// 这一条立刻变红。</para>
    /// </summary>
    internal static class NewCardsScenario
    {
        /// <summary>水之形（水 · 1 / 2）。</summary>
        private const string WaterId = "aq";

        /// <summary>闪电球（电 · 7 / 3）。</summary>
        private const string BallId = "ar";

        /// <summary>冷冻核心（冰 · 6 / 3）。</summary>
        private const string CoreId = "as";

        /// <summary>每条要收集几次「命中」才算真的被跑到。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可成立的局面。</summary>
        private const int MaxSeeds = 2000;

        private enum Mode
        {
            /// <summary>水之形被当作进攻牌打出。</summary>
            WaterAttack,

            /// <summary>水之形被当作防御牌交出。</summary>
            WaterDefend,

            /// <summary>闪电球在手牌 ≥2 张时打出（连击的<strong>反面</strong>对照）。</summary>
            BallNotLast,

            /// <summary>冷冻核心被打出 → 光环点亮 → 下次防御时取用。</summary>
            CoreAura,
        }

        public static bool Run(List<string> report)
        {
            bool defs = CheckCardDefs(report);

            bool water = RunCase(Mode.WaterAttack, out CaseResult wa);
            report.Add(Flag(water) + "水之形（aq）α：打出去这一拍的力量仍是 1（「进攻后」才成长），"
                       + "成长 +2 写进牌实例、进冷却区后力量仍是 3"
                       + "（命中 " + wa.Hits + " / 扫了 " + wa.Scanned + " 个种子）");
            AppendNotes(report, wa);

            bool defend = RunCase(Mode.WaterDefend, out CaseResult wd);
            report.Add(Flag(defend) + "水之形（aq）β：防御之后基础力量 +1（β 被正确改派到 ② 防御阶段）"
                       + "（命中 " + wd.Hits + " / 扫了 " + wd.Scanned + " 个种子）");
            AppendNotes(report, wd);

            bool ball = RunCase(Mode.BallNotLast, out CaseResult ba);
            report.Add(Flag(ball) + "闪电球（ar）反面：手牌 ≥2 张时不得连击（条件 hand-at-play = 1 真的被判）"
                       + "（命中 " + ba.Hits + " / 扫了 " + ba.Scanned + " 个种子）");
            AppendNotes(report, ba);

            bool core = RunCase(Mode.CoreAura, out CaseResult co);
            report.Add(Flag(core) + "冷冻核心（as）：进冷却区点亮「防御力量 +3」光环，"
                       + "且防御拍把它列为可用选项、准备后防御预算 +3"
                       + "（命中 " + co.Hits + " / 扫了 " + co.Scanned + " 个种子）");
            AppendNotes(report, co);

            return defs && water && defend && ball && core;
        }

        private static string Flag(bool ok)
        {
            return ok ? "  [PASS] " : "  [FAIL] ";
        }

        private static void AppendNotes(List<string> report, CaseResult r)
        {
            for (int i = 0; i < r.Failures.Count; i++)
            {
                report.Add("      · " + r.Failures[i]);
            }

            for (int i = 0; i < r.Notes.Count; i++)
            {
                report.Add("      · 杂音：" + r.Notes[i]);
            }
        }

        // ══════════════════════════════════════════════════════
        //  ① 卡表结构自检（数据类）
        // ══════════════════════════════════════════════════════

        private static bool CheckCardDefs(List<string> report)
        {
            var bad = new List<string>();

            CardDef aq = CardLibrary.Get(WaterId);
            Need(bad, "水之形", aq.Power == 1, "力量应为 1，实为 " + aq.Power);
            Need(bad, "水之形", aq.Cooldown == 2, "冷却应为 2，实为 " + aq.Cooldown);
            Need(bad, "水之形", aq.Element == CardElement.Water, "元素应为 水，实为 " + aq.Element);
            Need(bad, "水之形", HasOp(aq, EffectTrigger.Attack, EffectOp.GrowBasePower, 2),
                "缺少 α GrowBasePower（amount = 2）");
            Need(bad, "水之形", HasOp(aq, EffectTrigger.Defend, EffectOp.GrowBasePower, 1),
                "缺少 β GrowBasePower（amount = 1）");
            Need(bad, "水之形", !aq.HasAura, "不该带光环");

            CardDef ar = CardLibrary.Get(BallId);
            Need(bad, "闪电球", ar.Power == 7, "力量应为 7，实为 " + ar.Power);
            Need(bad, "闪电球", ar.Cooldown == 3, "冷却应为 3，实为 " + ar.Cooldown);
            Need(bad, "闪电球", ar.Element == CardElement.Electric, "元素应为 电，实为 " + ar.Element);
            Need(bad, "闪电球", HasOp(ar, EffectTrigger.Attack, EffectOp.Haste, 1), "缺少 α 加速（count = 1）");
            Need(bad, "闪电球", HasOp(ar, EffectTrigger.Attack, EffectOp.ComboIfLastHand, 0),
                "缺少 α 条件连击（ComboIfLastHand）");
            Need(bad, "闪电球", HasHandAtPlay(ar, 1),
                "条件连击没挂上 hand-at-play = 1（EffectDef 构造时应当自动补）");

            CardDef asDef = CardLibrary.Get(CoreId);
            Need(bad, "冷冻核心", asDef.Power == 6, "力量应为 6，实为 " + asDef.Power);
            Need(bad, "冷冻核心", asDef.Cooldown == 3, "冷却应为 3，实为 " + asDef.Cooldown);
            Need(bad, "冷冻核心", asDef.Element == CardElement.Ice, "元素应为 冰，实为 " + asDef.Element);
            Need(bad, "冷冻核心", HasOp(asDef, EffectTrigger.Attack, EffectOp.Slow, 1), "缺少 α 减速（count = 1）");
            Need(bad, "冷冻核心", HasDefPowerAura(asDef, 3), "缺少 α 光环：防御力量 +3");
            Need(bad, "冷冻核心", asDef.AuraTokenCount == 1, "光环指示物应为 1 枚，实为 " + asDef.AuraTokenCount);

            report.Add(Flag(bad.Count == 0)
                       + "三张新卡的卡表结构（力量 / 冷却 / 元素 / 算子 / 光环 / 条件）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            return bad.Count == 0;
        }

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

        private static bool HasHandAtPlay(CardDef def, int value)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op != EffectOp.ComboIfLastHand)
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

        private static bool HasDefPowerAura(CardDef def, int value)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == EffectOp.Aura && e.Trigger == EffectTrigger.Attack
                    && e.Aura == AuraKind.DefPower && e.A == value)
                {
                    return true;
                }
            }

            return false;
        }

        // ══════════════════════════════════════════════════════
        //  ② 扫描一次
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

        private static bool RunCase(Mode mode, out CaseResult result)
        {
            result = new CaseResult();

            for (int seed = 1; seed <= MaxSeeds && result.Hits < NeededHits; seed++)
            {
                Hit hit = RunOne(seed, mode);

                if (hit == null || hit.Error != null || hit.Stalled || !hit.Sample)
                {
                    if (hit != null && (hit.Error != null || hit.Stalled) && result.Notes.Count < 3)
                    {
                        result.Notes.Add("seed " + seed + " → "
                                         + (hit.Error ?? "推进无进展（疑似状态机卡住）"));
                    }

                    continue;   // 这一局没逼出可成立的局面，换种子
                }

                result.Scanned++;
                result.Hits++;

                if (!hit.Ok)
                {
                    result.Failures.Add("seed " + seed + " → " + hit.Why);
                }
            }

            if (result.Hits < NeededHits)
            {
                result.Notes.Add("只逼出 " + result.Hits + " 次样本，不足以证明（检查测试设置）");
            }

            return result.Ok;
        }

        private static Hit RunOne(int seed, Mode mode)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine, mode);

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
            run.Finish();

            return run.Hit;
        }

        // ══════════════════════════════════════════════════════
        //  ③ 观察结果
        // ══════════════════════════════════════════════════════

        private sealed class Hit
        {
            /// <summary>局面真的被布置出来、该见到的都见到了。</summary>
            public bool Sample;

            // ── 水之形 α ────────────────────────────────────
            /// <summary>打出那一拍第 ① 步报出的基础力量。</summary>
            public int AttackBasePower = -1;

            /// <summary>见到过「+2 成长」事件。</summary>
            public bool GrowSeen;

            /// <summary>那张牌进冷却区后的累计成长 / 有效力量。</summary>
            public int BonusInCooling = -1;
            public int PowerInCooling = -1;

            /// <summary>同一时刻**卡面快照**里那张牌的力量文本（必须与有效力量一致，否则成长在界面上是隐形的）。</summary>
            public string PowerTextInCooling;

            /// <summary>回手之后的有效力量 / 累计成长（没回手过就是 −1）。</summary>
            public int PowerOnReturn = -1;
            public int BonusOnReturn = -1;

            // ── 水之形 β ────────────────────────────────────
            /// <summary>那张牌真的作为防御牌交出去了。</summary>
            public bool DefensePlayed;

            /// <summary>见到过「+1 成长」事件。</summary>
            public bool DefendGrowSeen;

            /// <summary>防御后进冷却区时的累计成长 / 有效力量。</summary>
            public int DefendBonusInCooling = -1;
            public int DefendPowerInCooling = -1;

            // ── 闪电球 ──────────────────────────────────────
            /// <summary>打出之后的第一条半场开始事件是不是「连击追加」。</summary>
            public bool NextHalfIsCombo;

            /// <summary>看到了那条半场开始事件。</summary>
            public bool NextHalfSeen;

            // ── 冷冻核心 ────────────────────────────────────
            /// <summary>光环点亮过。</summary>
            public bool AuraActivated;

            /// <summary>防御拍把「防御力量 +3」列为可用选项。</summary>
            public bool AuraOffered;

            /// <summary>准备那枚光环之后，本拍防御预算（应为 3）。</summary>
            public int DefenseBonusAfterPrep = -1;

            public bool Stalled;
            public string Error;

            public bool Ok;
            public string Why = "未知";
        }

        // ══════════════════════════════════════════════════════
        //  ④ 脚本化双方 + 事件观察
        // ══════════════════════════════════════════════════════

        private sealed class Runner
        {
            private readonly BattleEngine _engine;
            private readonly Mode _mode;

            /// <summary>布局完成（一局只看一次）。</summary>
            private bool _armed;

            /// <summary>布局之后开始盯后续事件。</summary>
            private bool _watching;

            /// <summary>局面凑不出来（不算样本）。</summary>
            private bool _armFailed;

            /// <summary>被观测的那张牌实例（水之形 / 冷冻核心）。</summary>
            private int _watchUid = -1;

            /// <summary>冷冻核心：已经报备过一次「准备使用那枚光环」。</summary>
            private bool _corePrepSent;

            public readonly Hit Hit = new Hit();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine, Mode mode)
            {
                _engine = engine;
                _mode = mode;
            }

            public void OnEvent(BattleEvent e)
            {
                if (e is BasePowerGrownEvent)
                {
                    ObserveGrow((BasePowerGrownEvent)e);
                }
                else if (e is AttackPowerResolvedEvent)
                {
                    ObserveAttackPower((AttackPowerResolvedEvent)e);
                }
                else if (e is DefenseResolvedEvent)
                {
                    ObserveDefense((DefenseResolvedEvent)e);
                }
                else if (e is AuraActivatedEvent)
                {
                    var au = (AuraActivatedEvent)e;
                    if (_armed && au.Card != null && au.Card.Uid == _watchUid)
                    {
                        Hit.AuraActivated = true;
                    }
                }
                else if (e is CooldownChangedEvent)
                {
                    ObserveCooldown((CooldownChangedEvent)e);
                }
                else if (e is TurnStartedEvent && _watching && !Hit.NextHalfSeen)
                {
                    // 布局那一拍之后的第一条「半场开始」：连击的追加进攻就在这条上现形
                    // （追加进攻 = 同一个座位、IsComboFollowUp = true）。
                    Hit.NextHalfSeen = true;
                    Hit.NextHalfIsCombo = ((TurnStartedEvent)e).IsComboFollowUp;

                    if (_mode == Mode.BallNotLast)
                    {
                        Hit.Sample = true;
                        Stop = true;
                    }
                }
            }

            private void ObserveGrow(BasePowerGrownEvent e)
            {
                if (!_armed || e.Card == null || e.Card.Uid != _watchUid)
                {
                    return;
                }

                if (_mode == Mode.WaterAttack && e.Amount == 2)
                {
                    Hit.GrowSeen = true;
                }
                else if (_mode == Mode.WaterDefend && e.Amount == 1)
                {
                    Hit.DefendGrowSeen = true;
                }
            }

            private void ObserveAttackPower(AttackPowerResolvedEvent e)
            {
                if (!_armed || _mode != Mode.WaterAttack || e.Card == null || e.Card.Uid != _watchUid)
                {
                    return;
                }

                if (Hit.AttackBasePower < 0)
                {
                    Hit.AttackBasePower = e.BasePower;
                }
            }

            private void ObserveDefense(DefenseResolvedEvent e)
            {
                if (!_armed || _mode != Mode.WaterDefend)
                {
                    return;
                }

                for (int i = 0; i < e.Cards.Count; i++)
                {
                    if (e.Cards[i] != null && e.Cards[i].Uid == _watchUid)
                    {
                        Hit.DefensePlayed = true;
                        return;
                    }
                }
            }

            private void ObserveCooldown(CooldownChangedEvent e)
            {
                if (!_armed || e.Change.Card == null || e.Change.Card.Uid != _watchUid)
                {
                    return;
                }

                CardInstance card = e.Change.Card;

                if (e.Change.ReturnedToHand)
                {
                    Hit.PowerOnReturn = card.EffectivePower;
                    Hit.BonusOnReturn = card.BattlePowerBonus;
                    return;
                }

                if (card.Zone != CardZone.Cooling || e.Change.To <= 0)
                {
                    return;
                }

                if (_mode == Mode.WaterAttack && Hit.PowerInCooling < 0)
                {
                    Hit.PowerInCooling = card.EffectivePower;
                    Hit.BonusInCooling = card.BattlePowerBonus;
                    // 卡面读的是快照的 PowerText —— 那条路径必须一起跟着成长，
                    // 否则「引擎按 3 打、卡上写着 1」。
                    Hit.PowerTextInCooling = CardSnapshot.From(card).PowerText;

                    if (Hit.GrowSeen && Hit.AttackBasePower >= 0)
                    {
                        Hit.Sample = true;
                    }
                }
                else if (_mode == Mode.WaterDefend && Hit.DefendPowerInCooling < 0)
                {
                    Hit.DefendPowerInCooling = card.EffectivePower;
                    Hit.DefendBonusInCooling = card.BattlePowerBonus;
                    Hit.Sample = true;
                    Stop = true;
                }
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        return DecisionResponse.Of(req.Seat);   // 不替换

                    case RequestKind.ChooseAttackCard:
                        return DecideAttack(req);

                    case RequestKind.ChooseDefense:
                        return DecideDefense(req);

                    default:
                        return First(req);
                }
            }

            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_armed || _armFailed)
                {
                    return First(req);
                }

                PlayerState attacker = _engine.State.Of(req.Seat);

                switch (_mode)
                {
                    case Mode.WaterAttack:
                    {
                        Option water = FindOwn(req, attacker, WaterId);

                        // 只认「还没成长过」的那一张：同一实例可以反复打出，累计值会干扰断言。
                        if (water == null || water.Card.BattlePowerBonus != 0)
                        {
                            return First(req);
                        }

                        Arm(water.Card.Uid);
                        return DecisionResponse.Of(req.Seat, water.Index);
                    }

                    case Mode.WaterDefend:
                    {
                        int defenderSeat = _engine.State.Mode.SelectDefender(_engine.State, req.Seat);
                        PlayerState defender = _engine.State.Of(defenderSeat);

                        // ⚠ 防守方的手牌**不在本拍选项表里**（这一拍只列进攻方的牌），
                        //    所以只能直接读它的手牌。
                        CardInstance water = FindInHand(defender, WaterId);

                        // 「干净的一刀」= 力量 1、且 α 里只有「基础力量成长」这一类静默算子
                        //   （潮汐 e 一条 α 都没有；水之形 aq 只有 GrowBasePower）。
                        //   它不带出任何决策 / 连击，也能让力量 1 的水之形**不需要光环**就挡得住。
                        Option strike = FindCleanStrike(req, attacker);

                        if (water == null || water.BattlePowerBonus != 0 || strike == null)
                        {
                            return First(req);
                        }

                        Arm(water.Uid);
                        return DecisionResponse.Of(req.Seat, strike.Index);
                    }

                    case Mode.BallNotLast:
                    {
                        Option ball = FindOwn(req, attacker, BallId);

                        // 反面样本：打出之后手上还必须剩牌，否则「连不连击」无从观察。
                        if (ball == null || attacker.Hand.Count < 2)
                        {
                            return First(req);
                        }

                        Arm(ball.Card.Uid);
                        return DecisionResponse.Of(req.Seat, ball.Index);
                    }

                    case Mode.CoreAura:
                    {
                        Option core = FindOwn(req, attacker, CoreId);
                        if (core == null)
                        {
                            return First(req);
                        }

                        Arm(core.Card.Uid);
                        return DecisionResponse.Of(req.Seat, core.Index);
                    }
                }

                return First(req);
            }

            private DecisionResponse DecideDefense(DecisionRequest req)
            {
                if (_mode == Mode.WaterDefend)
                {
                    if (!_armed)
                    {
                        return First(req);
                    }

                    Option water = FindByUid(req, _watchUid);
                    if (water == null)
                    {
                        // 那一刀的最终力量比预期高 / 水之形不在候选表里 —— 不算样本。
                        _armFailed = true;
                        Hit.Sample = false;
                        Stop = true;
                        return First(req);
                    }

                    return DecisionResponse.Of(req.Seat, water.Index);
                }

                if (_mode == Mode.CoreAura && _armed && Hit.AuraActivated)
                {
                    if (!Hit.AuraOffered)
                    {
                        int idx = FindAuraOption(req, _watchUid);
                        if (idx >= 0)
                        {
                            Hit.AuraOffered = true;
                            Hit.Sample = true;
                            _corePrepSent = true;
                            // 「只报备准备使用」→ 引擎按新集合把本拍重发一次，
                            // 重发的那一份 ContextDefenseBonus 就是加了这 3 点之后的预算。
                            return DecisionResponse.PrepAuras(req.Seat, new[] { idx });
                        }
                    }
                    else if (_corePrepSent)
                    {
                        Hit.DefenseBonusAfterPrep = req.ContextDefenseBonus;
                        Stop = true;
                    }
                }

                return First(req);
            }

            private void Arm(int uid)
            {
                _armed = true;
                _watching = true;
                _watchUid = uid;
            }

            public void Finish()
            {
                switch (_mode)
                {
                    case Mode.WaterAttack:
                        Hit.Ok = _armed && Hit.AttackBasePower == 1 && Hit.GrowSeen
                                 && Hit.BonusInCooling == 2 && Hit.PowerInCooling == 3
                                 && Hit.PowerTextInCooling == "3"
                                 && (Hit.PowerOnReturn < 0
                                     || (Hit.PowerOnReturn == 3 && Hit.BonusOnReturn == 2));
                        Hit.Why = WhyWaterAttack();
                        break;

                    case Mode.WaterDefend:
                        Hit.Ok = _armed && Hit.DefensePlayed && Hit.DefendGrowSeen
                                 && Hit.DefendBonusInCooling == 1 && Hit.DefendPowerInCooling == 2;
                        Hit.Why = "防御牌没交出去 / β 没结算 / 成长没保住（应为 +1 → 力量 2），实为 played="
                                  + Hit.DefensePlayed + " 成长=" + Hit.DefendGrowSeen
                                  + " 冷却区力量=" + Hit.DefendPowerInCooling;
                        break;

                    case Mode.BallNotLast:
                        Hit.Ok = _armed && Hit.NextHalfSeen && !Hit.NextHalfIsCombo;
                        Hit.Why = "手牌 ≥2 张却触发了连击 —— 条件 hand-at-play = 1 没被判 / 没挂上";
                        break;

                    case Mode.CoreAura:
                        Hit.Ok = _armed && Hit.AuraActivated && Hit.AuraOffered
                                 && Hit.DefenseBonusAfterPrep == 3;
                        Hit.Why = "光环没点亮 / 防御拍没列出它 / 预算不是 +3，实为 activated="
                                  + Hit.AuraActivated + " offered=" + Hit.AuraOffered
                                  + " 预算=" + Hit.DefenseBonusAfterPrep;
                        break;
                }

                if (_armFailed)
                {
                    Hit.Sample = false;
                }
            }

            private string WhyWaterAttack()
            {
                if (!_armed)
                {
                    return "没布置出局面";
                }

                if (Hit.AttackBasePower != 1)
                {
                    return "打出那一拍的力量应为 1（基础 1，「进攻后」不追认本次），实为 "
                           + Hit.AttackBasePower + " —— 成长被算进了本次进攻";
                }

                if (!Hit.GrowSeen)
                {
                    return "没见到 α 的 +2 成长事件（算子没接上 / 归类落错阶段）";
                }

                if (Hit.BonusInCooling != 2 || Hit.PowerInCooling != 3)
                {
                    return "进冷却区后成长被抹掉：累计 +" + Hit.BonusInCooling + " / 力量 "
                           + Hit.PowerInCooling + "（应为 +2 / 3）"
                           + " —— 多半是某处写了 EffectivePower = Def.Power";
                }

                if (Hit.PowerTextInCooling != "3")
                {
                    return "卡面快照的力量文本是 \"" + Hit.PowerTextInCooling + "\"（应为 \"3\"）"
                           + " —— 成长在界面上是不可见的（快照又回去读卡表静态值了）";
                }

                if (Hit.PowerOnReturn >= 0
                    && (Hit.PowerOnReturn != 3 || Hit.BonusOnReturn != 2))
                {
                    return "回手后成长被抹掉：力量 " + Hit.PowerOnReturn + " / 累计 +"
                           + Hit.BonusOnReturn + "（应为 3 / +2）";
                }

                return "未知";
            }

            /// <summary>直接在某人的手牌里找一张指定卡 ID 的实例（不看选项表）。</summary>
            private static CardInstance FindInHand(PlayerState owner, string cardId)
            {
                for (int i = 0; i < owner.Hand.Count; i++)
                {
                    if (owner.Hand[i].Def.Id == cardId)
                    {
                        return owner.Hand[i];
                    }
                }

                return null;
            }

            /// <summary>
            /// 找一张「干净的一刀」：力量 1，且 α 效果里只有「基础力量成长」这种静默算子
            /// （潮汐 e 一条 α 都没有；水之形 aq 只有 GrowBasePower）。
            ///
            /// <para>为什么要有这条筛选：力量 1 的攻击让力量 1 的水之形**不需要光环**就能挡住，
            /// 而安静的那几张不会额外带出决策 / 连击，防守侧的观测面最小。</para>
            /// </summary>
            private static Option FindCleanStrike(DecisionRequest req, PlayerState attacker)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.AuraSource != null || o.Card == null
                        || o.Card.OwnerSeat != attacker.Seat)
                    {
                        continue;
                    }

                    if (o.Card.EffectivePower != 1 || o.Card.BattlePowerBonus != 0)
                    {
                        continue;
                    }

                    CardDef def = o.Card.Def;
                    bool clean = true;
                    for (int e = 0; e < def.Effects.Count; e++)
                    {
                        if (def.Effects[e].Trigger == EffectTrigger.Attack
                            && def.Effects[e].Op != EffectOp.GrowBasePower)
                        {
                            clean = false;
                            break;
                        }
                    }

                    if (clean)
                    {
                        return o;
                    }
                }

                return null;
            }

            /// <summary>在自己的手牌里找一张指定卡 ID 的出牌选项。</summary>
            private static Option FindOwn(DecisionRequest req, PlayerState owner, string cardId)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (!o.IsSkip && o.AuraSource == null && o.Card != null
                        && o.Card.OwnerSeat == owner.Seat && o.Card.Def.Id == cardId)
                    {
                        return o;
                    }
                }

                return null;
            }

            private static Option FindByUid(DecisionRequest req, int uid)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (!o.IsSkip && o.AuraSource == null && o.Card != null && o.Card.Uid == uid)
                    {
                        return o;
                    }
                }

                return null;
            }

            /// <summary>找「来源是那张牌、类型是防御加值 +3」的光环选项，返回它的序号（没有则 −1）。</summary>
            private static int FindAuraOption(DecisionRequest req, int uid)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.AuraSource != null && o.AuraSource.Uid == uid
                        && o.AuraKind == AuraKind.DefPower && o.Value == 3)
                    {
                        return o.Index;
                    }
                }

                return -1;
            }

            private static DecisionResponse First(DecisionRequest req)
            {
                if (req.Options != null && req.Options.Count > 0)
                {
                    return DecisionResponse.Of(req.Seat, req.Options[0].Index);
                }

                return DecisionResponse.Of(req.Seat);
            }
        }
    }
}
