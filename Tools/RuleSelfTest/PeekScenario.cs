using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-09-21 · 「查看对方一张手牌」交互改造的场景自测。
    ///
    /// <para><b>改了什么</b>：雷云（ad）与狂躁蘑菇（w）的第 2 个 α 效果原本由引擎
    /// <b>自己随机抽一张</b>（<c>HandleImmediateStage3</c>），玩家全程看不见发生了什么；
    /// 现改为引擎发出 <see cref="RequestKind.ChoosePeekCard"/> 决策 —— 玩家在一排牌背里点一张，
    /// 翻面之后才结算。</para>
    ///
    /// <para><b>为什么这类改动必须脚本化验证</b>：这是一次「引擎自洽但交互被吞掉」的改造 ——
    /// 万局统计里事件数、手牌守恒、胜负分布全都不变，接口改了也不会有人报错。
    /// 真正会出错的地方只有三处，且都在统计里无声：</para>
    /// <list type="number">
    /// <item><b>选项文案带上了牌名 / 力量</b> —— 效果就从「随机查看」变成「定向查看」，
    /// 那是另一张强度完全不同的卡。所以逐条断言文案里不含任何卡名。</item>
    /// <item><b>翻的那张 ≠ 我点的那张</b> —— <c>HandRevealedEvent</c> 的卡必须是所选选项那张，
    /// <c>SlotIndex</c> 必须等于该选项的手牌下标（UI 靠它决定翻第几格）。</item>
    /// <item><b>阈值 / 冷却修正判错</b> —— 结果不按引擎里写死的常数复核，而是
    /// <b>从卡表里那条 α 效果的 A / B / C 独立复算</b>一遍，再和引擎实际做的比。</item>
    /// </list>
    ///
    /// <para>逼出局面的办法：脚本化进攻方，只要对手手里有牌、且自己手上有「恰好带一条
    /// α 查看手牌效果」的牌，就打那一张。两个分支（命中 → 送入冷却 / 未命中 → 原样留在手里）
    /// 交替取样，保证都被覆盖到。</para>
    /// </summary>
    internal static class PeekScenario
    {
        /// <summary>要收集几次「真的问到查看手牌」的样本才算这条断言被跑到。</summary>
        private const int NeededHits = 4;

        /// <summary>最多扫多少个种子。</summary>
        private const int MaxSeeds = 4000;

        /// <summary>
        /// 下一个样本要验「命中（送入冷却）」还是「不命中（原样留在手里）」。
        ///
        /// <para><b>为什么是静态的 / 跨局交替</b>：每局只取一个样本（拿到
        /// <c>HandRevealedEvent</c> 就收工），所以如果每个 Runner 都从「验命中」起步，
        /// 交替标记会永远停在第一档 —— 结果是两条分支里只跑到「送入冷却」那条，
        /// 「不命中」的代码一行都不会执行（实测就是这样）。</para>
        /// </summary>
        private static bool s_preferHit = true;

        public static bool Run(List<string> report)
        {
            bool dataOk = CheckCardData(out string dataWhy);
            report.Add((dataOk ? "  [PASS] " : "  [FAIL] ")
                       + "查看手牌（LookAndCool）的卡面参数：雷云 ≤4 修正 0、狂躁蘑菇 ≥7 修正 −1"
                       + dataWhy);

            var failures = new List<string>();
            int hits = 0;
            int scanned = 0;
            int coolBranch = 0;
            int keepBranch = 0;

            for (int seed = 1; seed <= MaxSeeds && hits < NeededHits; seed++)
            {
                scanned++;

                PeekProbe probe = RunOne(seed);
                if (probe == null || !probe.Asked)
                {
                    continue;   // 这一局没逼出「查看手牌」的局面，换种子
                }

                hits++;

                if (probe.ExpectedCooled)
                {
                    coolBranch++;
                }
                else
                {
                    keepBranch++;
                }

                if (!probe.Ok)
                {
                    failures.Add("seed " + seed + " → " + probe.Reason);
                }
            }

            bool ok = dataOk && hits >= NeededHits && failures.Count == 0;

            report.Add((ok ? "  [PASS] " : "  [FAIL] ")
                       + "查看手牌走玩家选择：选项不带牌名、翻开的正是所选那张、结果与卡面阈值一致"
                       + "（命中 " + hits + " 次 / 扫了 " + scanned + " 个种子；"
                       + "送入冷却 " + coolBranch + " 次、留在手里 " + keepBranch + " 次）");

            for (int i = 0; i < failures.Count; i++)
            {
                report.Add("      · " + failures[i]);
            }

            if (hits < NeededHits)
            {
                report.Add("      · 只逼出 " + hits + " 次「查看手牌」局面，样本不足以证明（检查测试设置）");
            }
            else if (coolBranch == 0 || keepBranch == 0)
            {
                report.Add("      · ⚠ 只覆盖到单侧分支（送入冷却 " + coolBranch + " / 留在手里 " + keepBranch
                           + "）—— 另一条路的代码没被跑到");
            }

            return ok;
        }

        /// <summary>纯数据自检：卡表里两条 α 效果的阈值 / 方向 / 冷却修正没被改错。</summary>
        private static bool CheckCardData(out string why)
        {
            why = string.Empty;

            EffectDef cloud = FindAlphaLook(CardLibrary.Get("ad"));
            EffectDef mushroom = FindAlphaLook(CardLibrary.Get("w"));

            if (cloud == null)
            {
                why = "（雷云 ad 找不到 α 查看手牌效果 —— 要么没录，要么录了不止一条）";
                return false;
            }

            if (mushroom == null)
            {
                why = "（狂躁蘑菇 w 找不到 α 查看手牌效果 —— 要么没录，要么录了不止一条）";
                return false;
            }

            if (cloud.A != 4 || cloud.B != EffectDef.LookAtMost || cloud.C != 0)
            {
                why = "（雷云 = A" + cloud.A + "/B" + cloud.B + "/C" + cloud.C + "，期望 4/≤/0）";
                return false;
            }

            if (mushroom.A != 7 || mushroom.B != EffectDef.LookAtLeast || mushroom.C != -1)
            {
                why = "（狂躁蘑菇 = A" + mushroom.A + "/B" + mushroom.B + "/C" + mushroom.C + "，期望 7/≥/−1）";
                return false;
            }

            return true;
        }

        /// <summary>取卡面里<b>唯一</b>那条 α（进攻触发）的 <see cref="EffectOp.LookAndCool"/>。</summary>
        private static EffectDef FindAlphaLook(CardDef def)
        {
            if (def == null || def.Effects == null)
            {
                return null;
            }

            EffectDef found = null;
            int count = 0;

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == EffectOp.LookAndCool && e.Trigger == EffectTrigger.Attack)
                {
                    found = e;
                    count++;
                }
            }

            return count == 1 ? found : null;
        }

        // ══════════════════════════════════════════════════════
        //  一次尝试
        // ══════════════════════════════════════════════════════

        private sealed class PeekProbe
        {
            /// <summary>本局确实问到了「查看对方手牌」这一拍（= 有效样本）。</summary>
            public bool Asked;

            /// <summary>某个选项的文案里出现了卡名（= 「随机查看」退化成「定向查看」）。</summary>
            public bool LabelLeak;
            public string LeakedLabel;

            /// <summary>选项数 = 对手手牌数，且每条选项的序号 / Value 都等于同一手牌下标。</summary>
            public bool ShapeOk;

            /// <summary>翻开的正是所选那张，且 SlotIndex / OwnerSeat 都对得上。</summary>
            public bool PickOk;

            /// <summary>结果（是否送入冷却 / 冷却值 / 所在区域）与卡面参数独立复算的一致。</summary>
            public bool OutcomeOk;

            /// <summary>本次取样针对的是「应当命中」还是「应当不命中」。</summary>
            public bool ExpectedCooled;

            public string Why = "未知";

            public bool Ok
            {
                get { return Asked && !LabelLeak && ShapeOk && PickOk && OutcomeOk; }
            }

            /// <summary>失败原因（按检查顺序给第一条真正成立的）。</summary>
            public string Reason
            {
                get
                {
                    if (LabelLeak)
                    {
                        return "选项文案泄底：「" + LeakedLabel + "」里出现了卡名（效果会退化成「定向查看」）";
                    }

                    if (!ShapeOk)
                    {
                        return "选项结构不对（数量 = 对手手牌数、序号 = Value = 下标、且不该有 Skip）";
                    }

                    return Why;
                }
            }
        }

        private static PeekProbe RunOne(int seed)
        {
            BattleEngine engine = BattleEngine.Create(seed);
            var run = new Runner(engine);

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

                    if (++guard > 20000 || engine.Pending == null)
                    {
                        break;
                    }

                    engine.Submit(run.Decide(engine.Pending));
                }
            }
            catch (Exception ex)
            {
                if (run.Probe.Asked)
                {
                    run.Probe.OutcomeOk = false;
                    run.Probe.Why = ex.GetType().Name + ": " + ex.Message;
                }
            }

            engine.OnEvent -= run.OnEvent;

            return run.Probe.Asked ? run.Probe : null;
        }

        // ══════════════════════════════════════════════════════
        //  脚本化双方
        // ══════════════════════════════════════════════════════

        private sealed class Runner
        {
            private readonly BattleEngine _engine;

            /// <summary>该座位最近一次宣告打出的那张进攻牌（用来从卡表反推这一拍的效果参数）。</summary>
            private readonly CardDef[] _lastAttack = new CardDef[2];

            public readonly PeekProbe Probe = new PeekProbe();

            /// <summary>本局是否已经收工。</summary>
            public bool Stop { get; private set; }

            // ── 待校验的那一拍 ─────────────────────────────────
            private bool _armed;
            private CardInstance _expectCard;
            private bool _expectCooled;
            private int _expectSlot;
            private int _expectCd;
            private int _expectOwner;

            public Runner(BattleEngine engine)
            {
                _engine = engine;
            }

            public void OnEvent(BattleEvent e)
            {
                // 「卡面 → 效果参数」这条线只能从卡表反推，而 AttackContext 不进快照，
                // 所以这一笔得在这里留。
                var declared = e as AttackDeclaredEvent;
                if (declared != null)
                {
                    if (declared.Seat >= 0 && declared.Seat < _lastAttack.Length)
                    {
                        _lastAttack[declared.Seat] = declared.Card.Def;
                    }

                    return;
                }

                var revealed = e as HandRevealedEvent;
                if (revealed == null || !_armed)
                {
                    return;
                }

                _armed = false;

                if (!ReferenceEquals(revealed.Card, _expectCard))
                {
                    Probe.Why = "翻开的不是所选那张（事件给的是 " + revealed.Card.Def.Name
                                + "，选的是 " + _expectCard.Def.Name + "）";
                    Stop = true;
                    return;
                }

                if (revealed.SlotIndex != _expectSlot)
                {
                    Probe.Why = "SlotIndex = " + revealed.SlotIndex + "，期望 " + _expectSlot;
                    Stop = true;
                    return;
                }

                if (revealed.OwnerSeat != _expectOwner)
                {
                    Probe.Why = "OwnerSeat = " + revealed.OwnerSeat + "，期望 " + _expectOwner;
                    Stop = true;
                    return;
                }

                if (revealed.Cooled != _expectCooled)
                {
                    Probe.Why = "Cooled = " + revealed.Cooled + "，按卡面阈值复算应是 " + _expectCooled;
                    Stop = true;
                    return;
                }

                Probe.PickOk = true;

                PlayerState owner = _engine.State.Of(_expectOwner);
                bool inHand = owner.Hand.Contains(_expectCard);
                bool inCooling = owner.CoolingZone.Contains(_expectCard);

                if (_expectCooled)
                {
                    if (inHand || !inCooling)
                    {
                        Probe.Why = "判成送入冷却，但牌没真的离开手牌 / 没进冷却区（手牌=" + inHand
                                    + " 冷却区=" + inCooling + "）";
                        Stop = true;
                        return;
                    }

                    if (_expectCard.RemainingCooldown != _expectCd)
                    {
                        Probe.Why = "剩余冷却 = " + _expectCard.RemainingCooldown + "，期望 " + _expectCd;
                        Stop = true;
                        return;
                    }
                }
                else
                {
                    if (!inHand || inCooling)
                    {
                        Probe.Why = "判成不送入冷却，但牌的归属变了（手牌=" + inHand
                                    + " 冷却区=" + inCooling + "）";
                        Stop = true;
                        return;
                    }
                }

                Probe.OutcomeOk = true;
                Stop = true;
            }

            public DecisionResponse Decide(DecisionRequest req)
            {
                switch (req.Kind)
                {
                    case RequestKind.ChooseReplace:
                        // 一次都不换：手牌越干净，「对手手里有几张」越好确认
                        return DecisionResponse.Of(req.Seat);

                    case RequestKind.ChooseAttackCard:
                        return DecideAttack(req);

                    case RequestKind.ChoosePeekCard:
                        return DecidePeek(req);

                    default:
                        return First(req);
                }
            }

            /// <summary>
            /// 进攻：手上有「恰好带一条 α 查看手牌效果」的牌、且对手手里有牌 → 就打那一张
            /// （这是逼出被测局面的唯一手段）；其余照常。
            /// </summary>
            private DecisionResponse DecideAttack(DecisionRequest req)
            {
                if (_engine.State.Of(1 - req.Seat).Hand.Count == 0)
                {
                    return First(req);
                }

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.Card == null || o.AuraSource != null)
                    {
                        continue;
                    }

                    if (FindAlphaLook(o.Card.Def) != null)
                    {
                        return DecisionResponse.Of(req.Seat, o.Index);
                    }
                }

                return First(req);
            }

            /// <summary>
            /// 查看手牌：先逐条校验选项结构（数量 / 序号 / 文案不泄底），
            /// 再按「这一轮要验命中还是验不命中」挑一张，并把期望结果算好等事件复核。
            /// </summary>
            private DecisionResponse DecidePeek(DecisionRequest req)
            {
                int ownerSeat = 1 - req.Seat;
                PlayerState owner = _engine.State.Of(ownerSeat);

                Probe.Asked = true;

                // ① 结构：每个选项都必须是「对手的一张手牌」，序号 = Value = 下标
                Probe.ShapeOk = req.MinSelect == 1 && req.MaxSelect == 1
                               && req.Options.Count == owner.Hand.Count
                               && req.SkipOption == null;

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.Index != i || o.Value != i || o.Card == null
                        || !owner.Hand.Contains(o.Card))
                    {
                        Probe.ShapeOk = false;
                    }

                    // ② 文案不得泄底：任何卡名出现在选项文案里都算失败
                    if (!Probe.LabelLeak && LabelLeaksCardName(o.Label, out string leaked))
                    {
                        Probe.LabelLeak = true;
                        Probe.LeakedLabel = leaked;
                    }
                }

                // ③ 从卡面复算这一拍该是什么结果，并据此挑一张
                EffectDef ef = FindAlphaLook(LastAttackCard(req.Seat));
                if (ef == null)
                {
                    // 触发来源不是「牌面直接写的 α 查看手牌」（例如模仿复制来的）——
                    // 本拍不算样本（无法从卡面独立复算），照常作答即可。
                    Probe.Asked = false;
                    return First(req);
                }

                bool atMost = ef.B == EffectDef.LookAtMost;
                Option chosen = null;
                Option fallback = null;

                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (o.IsSkip || o.Card == null)
                    {
                        continue;
                    }

                    if (fallback == null)
                    {
                        fallback = o;
                    }

                    bool hits = atMost ? o.Card.Def.Power <= ef.A : o.Card.Def.Power >= ef.A;
                    if (hits == s_preferHit)
                    {
                        chosen = o;
                        break;
                    }
                }

                // 想要的那一侧一张都没有 → 这一侧覆盖不到，退而取任意一张，
                // 期望仍按它**实际**该有的结果算（照样是一次完整的独立复算）。
                if (chosen == null)
                {
                    chosen = fallback;
                }

                if (chosen == null)
                {
                    Probe.Asked = false;
                    return First(req);
                }

                s_preferHit = !s_preferHit;   // 下一个样本换另一条分支

                bool hit = atMost ? chosen.Card.Def.Power <= ef.A : chosen.Card.Def.Power >= ef.A;

                _expectCard = chosen.Card;
                _expectCooled = hit;
                _expectSlot = chosen.Value;
                _expectOwner = ownerSeat;
                // 「冷却值 = 基础冷却 + C」再按冷却区不变量夹到 [1, 基础冷却]
                _expectCd = Clamp(chosen.Card.Def.Cooldown + ef.C, 1, chosen.Card.Def.Cooldown);
                _armed = true;

                Probe.ExpectedCooled = hit;
                Probe.PickOk = false;
                Probe.OutcomeOk = false;
                Probe.Why = "HandRevealedEvent 没有出现（本局在查看手牌之后就结束了？）";

                return DecisionResponse.Of(req.Seat, chosen.Index);
            }

            /// <summary>
            /// 该座位最近一次宣告打出的那张进攻牌。
            /// </summary>
            private CardDef LastAttackCard(int seat)
            {
                return seat >= 0 && seat < _lastAttack.Length ? _lastAttack[seat] : null;
            }

            private static bool LabelLeaksCardName(string label, out string leaked)
            {
                leaked = null;
                if (string.IsNullOrEmpty(label))
                {
                    return false;
                }

                for (int c = 0; c < CardLibrary.Count; c++)
                {
                    string name = CardLibrary.GetByIndex(c).Name;
                    if (!string.IsNullOrEmpty(name)
                        && label.IndexOf(name, StringComparison.Ordinal) >= 0)
                    {
                        leaked = label;
                        return true;
                    }
                }

                return false;
            }

            private static int Clamp(int value, int min, int max)
            {
                if (value < min)
                {
                    return min;
                }

                return value > max ? max : value;
            }

            private static DecisionResponse First(DecisionRequest req)
            {
                for (int i = 0; i < req.Options.Count; i++)
                {
                    if (!req.Options[i].IsSkip)
                    {
                        return DecisionResponse.Of(req.Seat, req.Options[i].Index);
                    }
                }

                return DecisionResponse.Of(req.Seat);
            }
        }
    }
}
