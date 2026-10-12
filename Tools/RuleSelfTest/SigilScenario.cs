using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-07 · <b>刻印（Sigil）</b> + 两张新卡（火炬 at / 海啸 au）的行为自测。
    ///
    /// <para><b>为什么必须单独写一条</b>：刻印的风险全是「卡面写了、引擎里没接上」，
    /// 而这类缺陷在万局统计里<b>一个也抓不到</b>（事件流自洽、手牌守恒、胜负分布都正常）。
    /// 刻印尤其隐蔽 —— 它由三件<b>互不相干</b>的事拼起来，缺哪一件都不报错：</para>
    /// <list type="number">
    /// <item><b>点亮</b>：只有「本牌作为<b>进攻牌</b>打出」才亮（<c>DoStage5</c> → <c>LightSigils</c>）。
    /// 漏了它 → 刻印永远不触发；判宽了（防御也亮）→ 卡面写的「只有进攻时」作废。</item>
    /// <item><b>触发</b>：<c>SigilTiming</c> 两个点位（回合开始 / 自己进攻结束）上一次都不许少。
    /// 漏了它 → 亮了但不生效，界面上看不出任何异常。</item>
    /// <item><b>不当拍结算</b>：打出去那一拍<b>只能点亮</b>（<c>EnqueueEffects</c> 里必须跳过刻印）。
    /// 漏了它 → 「打出去当拍白拿一次 + 以后每回合再拿一次」，凭空多算一次，只表现为强度偏高。</item>
    /// </list>
    ///
    /// <para><b>四层断言</b>：① 卡表结构；② 火炬 —— 点亮、<b>每回合累积</b>光环指示物、回手熄灭；
    /// ③ 海啸 α —— 点亮它的那一拍<b>立刻</b>拿到一次加速（用户 2026-10-07 口径）；
    /// ④ 海啸 β —— 「防御时增加等于手牌数的力量值」真的进了防御合法性计算
    /// （外加一个「作为防御牌打出不点亮刻印」的反面对照）。</para>
    /// </summary>
    internal static class SigilScenario
    {
        /// <summary>火炬（火 · 4 / 4）—— α刻印：每回合开始时获得光环：进攻力量 +2。</summary>
        private const string TorchId = "at";

        /// <summary>海啸（水 · 2 / 4）—— α刻印：每次进攻结束时获得一次加速；β：防御时 +手牌数。</summary>
        private const string TsunamiId = "au";

        /// <summary>每条要收集几次「命中」才算真的被跑到。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子去找可成立的局面。</summary>
        private const int MaxSeeds = 2000;

        private enum Mode
        {
            /// <summary>火炬被打出 → 每回合开始累积 1 枚「进攻力量 +2」指示物。</summary>
            TorchAccumulate,

            /// <summary>火炬冷却完毕回手 → 指示物与刻印一起作废。</summary>
            TorchExtinguish,

            /// <summary>海啸作为进攻牌打出 → 同一拍结束时立刻拿到一次加速。</summary>
            TsunamiAttackEnd,

            /// <summary>海啸作为防御牌交出 → β 的「等于手牌数」进了合法性计算。</summary>
            TsunamiBetaHand,

            /// <summary>反面：带刻印的牌作为<strong>防御牌</strong>打出 → 一条都不许点亮。</summary>
            NoLitOnDefense,
        }

        public static bool Run(List<string> report)
        {
            bool defs = CheckCardDefs(report);

            bool accumulate = RunCase(Mode.TorchAccumulate, out CaseResult ac);
            report.Add(Flag(accumulate) + "火炬（at）刻印：打出去那一拍**只点亮、不给光环**；"
                       + "随后每个回合开始各 +1 枚「进攻力量 +2」指示物（累积到 2 枚）"
                       + "（命中 " + ac.Hits + " / 扫了 " + ac.Scanned + " 个种子）");
            AppendNotes(report, ac);

            bool extinguish = RunCase(Mode.TorchExtinguish, out CaseResult ex);
            report.Add(Flag(extinguish) + "火炬（at）：冷却完毕回手时，未用的刻印指示物与刻印状态一起作废"
                       + "（命中 " + ex.Hits + " / 扫了 " + ex.Scanned + " 个种子）");
            AppendNotes(report, ex);

            bool attackEnd = RunCase(Mode.TsunamiAttackEnd, out CaseResult ae);
            report.Add(Flag(attackEnd) + "海啸（au）α刻印：点亮它的那一拍结束时**立刻**触发一次加速"
                       + "（真的弹了选目标并真的减了 1 点冷却）"
                       + "（命中 " + ae.Hits + " / 扫了 " + ae.Scanned + " 个种子）");
            AppendNotes(report, ae);

            bool beta = RunCase(Mode.TsunamiBetaHand, out CaseResult be);
            report.Add(Flag(beta) + "海啸（au）β：防御「需要的力量」按 = 攻击力量 −（2 + 手牌数）算，"
                       + "且提交后引擎复核通过（两处口径一致）"
                       + "（命中 " + be.Hits + " / 扫了 " + be.Scanned + " 个种子）");
            AppendNotes(report, be);

            bool notLit = RunCase(Mode.NoLitOnDefense, out CaseResult nl);
            report.Add(Flag(notLit) + "反面：带刻印的牌作为**防御牌**打出时，一条刻印都不许点亮"
                       + "（命中 " + nl.Hits + " / 扫了 " + nl.Scanned + " 个种子）");
            AppendNotes(report, nl);

            return defs && accumulate && extinguish && attackEnd && beta && notLit;
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
        //  ① 卡表结构 + 纯函数（数据类）
        // ══════════════════════════════════════════════════════

        private static bool CheckCardDefs(List<string> report)
        {
            var bad = new List<string>();

            CardDef torch = CardLibrary.Get(TorchId);
            Need(bad, "火炬", torch.Power == 4, "力量应为 4，实为 " + torch.Power);
            Need(bad, "火炬", torch.Cooldown == 4, "冷却应为 4，实为 " + torch.Cooldown);
            Need(bad, "火炬", torch.Element == CardElement.Fire, "元素应为 火，实为 " + torch.Element);
            Need(bad, "火炬", HasSigil(torch, EffectOp.Aura, SigilTiming.TurnStart, 2, AuraKind.AtkPower),
                "缺少 α 刻印：TurnStart 的 Aura（进攻力量 +2）");
            Need(bad, "火炬", torch.AuraTokenCount == 1, "卡面光环条数应为 1，实为 " + torch.AuraTokenCount);
            Need(bad, "火炬", NoBetaSigil(torch), "刻印不该出现在 β 上（只有进攻时点亮）");

            CardDef wave = CardLibrary.Get(TsunamiId);
            Need(bad, "海啸", wave.Power == 2, "力量应为 2，实为 " + wave.Power);
            Need(bad, "海啸", wave.Cooldown == 4, "冷却应为 4，实为 " + wave.Cooldown);
            Need(bad, "海啸", wave.Element == CardElement.Water, "元素应为 水，实为 " + wave.Element);
            Need(bad, "海啸", HasSigil(wave, EffectOp.Haste, SigilTiming.AttackEnd, 1, AuraKind.None),
                "缺少 α 刻印：AttackEnd 的 Haste ×1");
            Need(bad, "海啸", IsMandatory(wave, EffectOp.Haste),
                "α 刻印的加速应当是 [强制]（刻印是自动触发的，不该再摆一个「跳过」按钮）");
            Need(bad, "海啸", HasOp(wave, EffectTrigger.Defend, EffectOp.DefPlusPerHand, 1),
                "缺少 β「防御时增加等于手牌数的力量值」（DefPlusPerHand，amount = 1）");
            Need(bad, "海啸", wave.AuraTokenCount == 0, "海啸不该带光环，实为 " + wave.AuraTokenCount);

            // 纯函数：DefensePower 必须把手牌数算进去（否则 β 只是个摆设）。
            // ⚠ CardInstance 构造时 EffectivePower 还是 0（真正赋值发生在 ToHand / PutIntoCooldown
            //   走的 ResetEffectivePower）—— 这里先让它上手，免得拿 0 去算。
            var probe = new CardInstance(CardLibrary.Get(TsunamiId), 0);
            probe.ToHand();
            Need(bad, "海啸", DefenseResolver.DefensePower(probe, 0) == 2,
                "DefensePower(手牌 0) 应为 2（有效力量 2 + 0），实为 " + DefenseResolver.DefensePower(probe, 0));
            Need(bad, "海啸", DefenseResolver.DefensePower(probe, 5) == 7,
                "DefensePower(手牌 5) 应为 7（2 + 5），实为 " + DefenseResolver.DefensePower(probe, 5));
            Need(bad, "海啸", DefenseResolver.RequiredBonus(probe, 6, 5) == 0
                              && DefenseResolver.RequiredBonus(probe, 6, 2) == 2,
                "RequiredBonus 没跟上手牌数（6 攻 / 5 手牌 应为 0；6 攻 / 2 手牌 应为 2）");

            report.Add(Flag(bad.Count == 0)
                       + "两张新卡的卡表结构（力量 / 冷却 / 元素 / 算子 / 刻印时机 / 光环条数）"
                       + " + 海啸 β 的纯函数（每张手牌 +1）");
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

        private static bool HasSigil(CardDef def, EffectOp op, SigilTiming timing, int amount, AuraKind aura)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Trigger == EffectTrigger.Attack && e.Op == op && e.Sigil == timing
                    && e.A == amount && e.Aura == aura)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NoBetaSigil(CardDef def)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (def.Effects[i].Trigger != EffectTrigger.Attack
                    && def.Effects[i].Sigil != SigilTiming.None)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsMandatory(CardDef def, EffectOp op)
        {
            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (def.Effects[i].Op == op && def.Effects[i].Sigil != SigilTiming.None)
                {
                    return def.Effects[i].Mandatory;
                }
            }

            return false;
        }

        // ══════════════════════════════════════════════════════
        //  ② 扫描
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

            // ── 通用 ────────────────────────────────────────
            /// <summary>被观测的那张牌实例。</summary>
            public int WatchUid = -1;

            /// <summary>见到过「刻印点亮」事件（挂在那张牌上）。</summary>
            public bool LitSeen;
            public SigilTiming LitTiming = SigilTiming.None;

            // ── 火炬 ────────────────────────────────────────
            /// <summary>「打出去那一拍」内就收到了光环（= 刻印被当拍结算了，必须是 false）。</summary>
            public bool AuraAtPlay;

            /// <summary>刻印触发的次数（AuraActivated 计数）。</summary>
            public int AuraGrants;

            /// <summary>第 2 次到账时的指示物数（应为 2 —— 累积）。</summary>
            public int TokensAfterSecond = -1;

            /// <summary>到账指示物的类型 / 数值（应全部是 AtkPower / 2）。</summary>
            public bool TokensWellFormed = true;

            /// <summary>回手那一刻：剩余指示物数 / 刻印是否还亮。</summary>
            public int TokensOnReturn = -1;
            public bool SigilLiveOnReturn = true;

            // ── 海啸 α ──────────────────────────────────────
            /// <summary>见到「刻印触发（AttackEnd）」。</summary>
            public bool TriggeredAtAttackEnd;

            /// <summary>触发之后真的减了 1 点冷却。</summary>
            public bool HasteApplied;

            /// <summary>触发时那张牌在冷却区里（加速才有目标可指）。</summary>
            public bool CoolingAtTrigger;

            // ── 海啸 β ──────────────────────────────────────
            /// <summary>防御方手牌里的海啸被列成了选项。</summary>
            public bool BetaOptionListed;

            /// <summary>那一条选项报出的「还差几点」。</summary>
            public int BetaOptionValue = -1;

            /// <summary>本拍的攻击力量 / 防御方手牌数。</summary>
            public int BetaAttackPower = -1;
            public int BetaHandCount = -1;

            /// <summary>提交之后引擎复核通过、防御成功。</summary>
            public bool BetaCommitted;

            /// <summary>同拍里「力量相同但不带 β」的牌<b>没有</b>被列出来（反面对照，可能没样本）。</summary>
            public int PlainSamePowerListed = -1;

            // ── 反面（防御不点亮） ───────────────────────────
            /// <summary>那张牌真的作为防御牌交出去了。</summary>
            public bool WasDefended;

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

            /// <summary>布局完成。</summary>
            private bool _armed;

            /// <summary>被观测那张牌已经打完它自己的那一拍了（之后的光环到账才算刻印触发的）。</summary>
            private bool _playedPhaseOver;

            /// <summary>已经看到「刻印触发（AttackEnd）」，正在等那一次加速落地。</summary>
            private bool _awaitHaste;

            /// <summary>已经提交过海啸的 β 选项。</summary>
            private bool _betaSubmitted;

            /// <summary>已经认过那一条防御结果（只认第一条）。</summary>
            private bool _betaObserved;

            /// <summary>反面对照：已经交出防御牌，正在等半场翻篇以便判定「没被点亮」。</summary>
            private bool _awaitNoLitEnd;

            public readonly Hit Hit = new Hit();

            public bool Stop { get; private set; }

            public Runner(BattleEngine engine, Mode mode)
            {
                _engine = engine;
                _mode = mode;
            }

            public void OnEvent(BattleEvent e)
            {
                if (e is SigilLitEvent)
                {
                    ObserveLit((SigilLitEvent)e);
                }
                else if (e is SigilTriggeredEvent)
                {
                    ObserveTriggered((SigilTriggeredEvent)e);
                }
                else if (e is AuraActivatedEvent)
                {
                    ObserveAuraGrant((AuraActivatedEvent)e);
                }
                else if (e is TurnStartedEvent)
                {
                    _playedPhaseOver = true;

                    if (_awaitNoLitEnd)
                    {
                        // 半场翻篇 = 这一拍的五个阶段（含第 ⑤ 步的点亮）都跑完了 ——
                        // 到这一刻还没有 SigilLit，才能说「防御牌不点亮刻印」。
                        _awaitNoLitEnd = false;
                        Hit.Sample = true;
                        Stop = true;
                    }
                }
                else if (e is DefenseResolvedEvent)
                {
                    ObserveDefense((DefenseResolvedEvent)e);
                }
                else if (e is CooldownChangedEvent)
                {
                    ObserveCooldown((CooldownChangedEvent)e);
                }
            }

            private void ObserveLit(SigilLitEvent e)
            {
                if (!_armed || e.Card == null || e.Card.Uid != Hit.WatchUid)
                {
                    return;
                }

                Hit.LitSeen = true;
                Hit.LitTiming = e.Timing;
            }

            private void ObserveTriggered(SigilTriggeredEvent e)
            {
                if (!_armed || e.Card == null || e.Card.Uid != Hit.WatchUid)
                {
                    return;
                }

                if (e.Timing == SigilTiming.AttackEnd && _mode == Mode.TsunamiAttackEnd)
                {
                    Hit.TriggeredAtAttackEnd = true;
                    Hit.CoolingAtTrigger = e.Card.IsCooling;
                    _awaitHaste = true;
                }
            }

            private void ObserveAuraGrant(AuraActivatedEvent e)
            {
                if (!_armed || e.Card == null || e.Card.Uid != Hit.WatchUid)
                {
                    return;
                }

                if (!_playedPhaseOver)
                {
                    // 打出去那一拍（还没翻到下一条半场开始）就到账了 → 刻印被当拍结算了。
                    Hit.AuraAtPlay = true;
                }

                Hit.AuraGrants++;

                // ⚠ 只有 TorchAccumulate 拿「第 2 次到账」当样本 ——
                //   TorchExtinguish 的样本是「回手」，被这里顺手置真会让它在没回手时也算命中。
                if (_mode != Mode.TorchAccumulate)
                {
                    return;
                }

                if (Hit.AuraGrants == 2 && Hit.TokensAfterSecond < 0)
                {
                    // 累计 2 次到账 → 应当有 **2 枚**指示物（用户 2026-10-07 口径：累积叠加）。
                    Hit.TokensAfterSecond = e.Card.AuraTokens;

                    AuraTokenSnapshot[] detail = CardSnapshot.From(e.Card).AuraTokensDetail;
                    if (detail == null || detail.Length != e.Card.AuraTokens)
                    {
                        Hit.TokensWellFormed = false;
                    }
                    else
                    {
                        for (int i = 0; i < detail.Length; i++)
                        {
                            if (detail[i].Kind != AuraKind.AtkPower || detail[i].Value != 2)
                            {
                                Hit.TokensWellFormed = false;
                            }
                        }
                    }

                    Hit.Sample = true;
                }
            }

            private void ObserveDefense(DefenseResolvedEvent e)
            {
                if (!_armed)
                {
                    return;
                }

                for (int i = 0; i < e.Cards.Count; i++)
                {
                    if (e.Cards[i] != null && e.Cards[i].Uid == Hit.WatchUid)
                    {
                        Hit.WasDefended = true;
                    }
                }

                if (_mode == Mode.TsunamiBetaHand && _betaSubmitted && !_betaObserved)
                {
                    // ⚠ 只认提交之后**第一条** —— 不设闸门的话，后面某一拍对方放弃防御的
                    //   那条 Success = false 会把这里覆盖掉（表现是「明明挡住了却报 False」）。
                    _betaObserved = true;
                    Hit.BetaCommitted = e.Success;
                    Hit.Sample = true;
                    Stop = true;
                }

                if (_mode == Mode.NoLitOnDefense && _armed && Hit.WasDefended)
                {
                    // 半场翻篇之后再判「有没有被点亮」—— 第 ⑤ 步在防御结算之后才跑。
                    _awaitNoLitEnd = true;
                }
            }

            private void ObserveCooldown(CooldownChangedEvent e)
            {
                if (e.Change.Card == null)
                {
                    return;
                }

                // ① 先认「刻印触发之后那一次加速」—— 它**可能正好把被观测的那张牌减回手**
                //    （加速命中自己时 ReturnedToHand = true），所以必须排在下面那个分支前面，
                //    否则「牌回手了」会把这次加速的证据吃掉，海啸那条断言永远等不到。
                if (_awaitHaste && _armed)
                {
                    string reason = e.Change.Reason ?? string.Empty;
                    if (reason.Contains("加速"))
                    {
                        Hit.HasteApplied = true;
                        _awaitHaste = false;
                        Hit.Sample = true;
                        Stop = true;
                        return;
                    }
                }

                // ② 被观测那张牌回手 → 清空凭据（火炬的「回手熄灭」就靠它）。
                if (_armed && e.Change.Card.Uid == Hit.WatchUid && e.Change.ReturnedToHand)
                {
                    Hit.TokensOnReturn = e.Change.Card.AuraTokens;
                    Hit.SigilLiveOnReturn = e.Change.Card.SigilLive;

                    if (_mode == Mode.TorchExtinguish)
                    {
                        Hit.Sample = true;
                        Stop = true;
                    }
                }
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        return DecisionResponse.Of(req.Seat);

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
                if (_armed)
                {
                    return First(req);
                }

                PlayerState attacker = _engine.State.Of(req.Seat);

                switch (_mode)
                {
                    case Mode.TorchAccumulate:
                    case Mode.TorchExtinguish:
                    {
                        Option torch = FindOwn(req, attacker, TorchId);
                        if (torch == null)
                        {
                            return First(req);
                        }

                        Arm(torch.Card.Uid);
                        return DecisionResponse.Of(req.Seat, torch.Index);
                    }

                    case Mode.TsunamiAttackEnd:
                    {
                        Option wave = FindOwn(req, attacker, TsunamiId);
                        if (wave == null)
                        {
                            return First(req);
                        }

                        Arm(wave.Card.Uid);
                        return DecisionResponse.Of(req.Seat, wave.Index);
                    }
                }

                return First(req);
            }

            private DecisionResponse DecideDefense(DecisionRequest req)
            {
                if (_mode == Mode.TsunamiBetaHand && !_armed)
                {
                    return ArmBeta(req);
                }

                if (_mode == Mode.NoLitOnDefense && !_armed)
                {
                    return ArmNoLit(req);
                }

                if (_mode == Mode.TsunamiBetaHand && _betaSubmitted)
                {
                    return First(req);
                }

                return First(req);
            }

            /// <summary>
            /// 海啸 β：找一次「海啸作为防御牌、且这一刀的力量 &gt; 2」的防御拍 ——
            /// 只有这样「等于手牌数」那部分才是承重的（否则它本来就挡得住，看不出差别）。
            /// </summary>
            private DecisionResponse ArmBeta(DecisionRequest req)
            {
                if (req.ContextDouble)
                {
                    return First(req);
                }

                PlayerState defender = _engine.State.Of(req.Seat);
                CardInstance wave = FindInHand(defender, TsunamiId);

                int handCount = defender.Hand.Count;

                // 有效样本的判据：这一刀的力量必须落在「**靠 β 的手牌数部分才够**」的那一段 ——
                //   力量 > 2（只用海啸自己的 2 点力量挡不住）且 ≤ 2 + 手牌数（算上 β 才够）。
                // 它出现在候选表里这件事本身就是凭据：判据漏了手牌数的话，
                //   所需补值 = 力量 − 2 > 0 > 可用预算 0 → 它根本不会被列出来。
                if (wave == null || req.ContextPower <= 2
                    || req.ContextPower > 2 + handCount || handCount < 3)
                {
                    // 抽到的这一拍不构成有效样本（换种子继续）。
                    return First(req);
                }

                Option pick = null;
                int plainListed = 0;

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.AuraSource != null || o.Card == null)
                    {
                        continue;
                    }

                    if (o.Card.Uid == wave.Uid)
                    {
                        pick = o;
                    }
                    else if (o.Card.EffectivePower == 2 && DefenseResolver.DefendBonus(o.Card, defender.Hand.Count) == 0
                             && o.Card.Def.Power == 2 && !DefenseResolver.HasGuard(o.Card))
                    {
                        // 反面对照：同样力量 2、但没有 β 加值的牌 —— 它不该出现在这一拍里。
                        plainListed++;
                    }
                }

                Arm(wave.Uid);

                Hit.BetaAttackPower = req.ContextPower;
                Hit.BetaHandCount = defender.Hand.Count;
                Hit.PlainSamePowerListed = plainListed;

                if (pick == null)
                {
                    // 海啸没被列出来 → β 那部分多半没进合法性计算（真正的失败，但要算样本）。
                    Hit.BetaOptionListed = false;
                    Hit.Sample = true;
                    Stop = true;
                    return First(req);
                }

                Hit.BetaOptionListed = true;
                Hit.BetaOptionValue = pick.Value;
                _betaSubmitted = true;

                return DecisionResponse.Of(req.Seat, pick.Index);
            }

            /// <summary>反面：挑一张带刻印的牌作为防御牌交出去，看它会不会被点亮。</summary>
            private DecisionResponse ArmNoLit(DecisionRequest req)
            {
                if (req.ContextDouble)
                {
                    return First(req);
                }

                PlayerState defender = _engine.State.Of(req.Seat);

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.AuraSource != null || o.Card == null)
                    {
                        continue;
                    }

                    bool hasSigil = false;
                    for (int e = 0; e < o.Card.Def.Effects.Count; e++)
                    {
                        if (o.Card.Def.Effects[e].Sigil != SigilTiming.None)
                        {
                            hasSigil = true;
                            break;
                        }
                    }

                    if (!hasSigil)
                    {
                        continue;
                    }

                    Arm(o.Card.Uid);
                    return DecisionResponse.Of(req.Seat, o.Index);
                }

                return First(req);
            }

            private void Arm(int uid)
            {
                _armed = true;
                Hit.WatchUid = uid;
            }

            public void Finish()
            {
                switch (_mode)
                {
                    case Mode.TorchAccumulate:
                        Hit.Ok = _armed && Hit.LitSeen && Hit.LitTiming == SigilTiming.TurnStart
                                 && !Hit.AuraAtPlay && Hit.AuraGrants >= 2
                                 && Hit.TokensAfterSecond == 2 && Hit.TokensWellFormed;
                        Hit.Why = WhyTorch();
                        break;

                    case Mode.TorchExtinguish:
                        Hit.Ok = _armed && Hit.LitSeen && Hit.TokensOnReturn == 0
                                 && !Hit.SigilLiveOnReturn;
                        Hit.Why = "回手时指示物 " + Hit.TokensOnReturn + " 枚 / 刻印仍亮 "
                                  + Hit.SigilLiveOnReturn + "（应为 0 / false）";
                        break;

                    case Mode.TsunamiAttackEnd:
                        Hit.Ok = _armed && Hit.LitSeen && Hit.LitTiming == SigilTiming.AttackEnd
                                 && Hit.TriggeredAtAttackEnd && Hit.CoolingAtTrigger && Hit.HasteApplied;
                        Hit.Why = "点亮=" + Hit.LitSeen + "（" + Hit.LitTiming + "）触发="
                                  + Hit.TriggeredAtAttackEnd + " 触发时在冷却区=" + Hit.CoolingAtTrigger
                                  + " 真的加速=" + Hit.HasteApplied
                                  + "（点亮的那一拍就该触发一次加速，用户 2026-10-07 口径）";
                        break;

                    case Mode.TsunamiBetaHand:
                    {
                        int expected = Hit.BetaAttackPower - 2 - Hit.BetaHandCount;
                        if (expected < 0)
                        {
                            expected = 0;
                        }

                        Hit.Ok = _armed && Hit.BetaOptionListed
                                 && Hit.BetaOptionValue == expected && Hit.BetaCommitted;
                        Hit.Why = "海啸没进候选=" + (!Hit.BetaOptionListed)
                                  + " / 报出的差值 " + Hit.BetaOptionValue + "（应为 " + expected
                                  + "，攻击 " + Hit.BetaAttackPower + " − 2 − 手牌 " + Hit.BetaHandCount
                                  + "）/ 提交后复核通过=" + Hit.BetaCommitted;
                        break;
                    }

                    case Mode.NoLitOnDefense:
                        Hit.Ok = _armed && Hit.WasDefended && !Hit.LitSeen;
                        Hit.Why = "带刻印的牌作为防御牌打出后刻印亮了：" + Hit.LitSeen
                                  + "（卡面写的是「只有进攻时会点亮」）";
                        break;
                }
            }

            private string WhyTorch()
            {
                if (!_armed)
                {
                    return "没布置出局面";
                }

                if (!Hit.LitSeen || Hit.LitTiming != SigilTiming.TurnStart)
                {
                    return "没见到「刻印点亮（TurnStart）」事件（点亮没接上 / 时机写错）";
                }

                if (Hit.AuraAtPlay)
                {
                    return "打出去那一拍就到账了光环 —— 刻印被当拍结算了"
                           + "（EnqueueEffects 必须把刻印从当拍结算里摘出去）";
                }

                if (Hit.AuraGrants < 2)
                {
                    return "只到账 " + Hit.AuraGrants + " 次（应为 ≥2）—— 回合开始的触发点没接上";
                }

                if (Hit.TokensAfterSecond != 2)
                {
                    return "第 2 次到账之后手上有 " + Hit.TokensAfterSecond
                           + " 枚指示物（应为 2 —— 用户口径是「累积叠加」）";
                }

                if (!Hit.TokensWellFormed)
                {
                    return "指示物不是「进攻力量 +2」（类型 / 数值接错了）";
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
