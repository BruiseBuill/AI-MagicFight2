using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-03 · <b>怪物框架</b>（按角色的发牌口径 + 回合行为算子）自测。
    ///
    /// <para><b>这一轮改了什么</b>：</para>
    /// <list type="number">
    /// <item><b>发牌口径按角色</b>（<see cref="DealProfile"/>）：人类 6 张可换 3、第 2–3 回合各补 1
    /// （与旧口径逐字相同）；怪物开局抽满 <b>8 张</b>、之后<b>一张都不补</b>、开局也不换牌。
    /// 引擎原来把「开局几张 / 补几张 / 能换几张」写成整局一份，现在是<b>按座位</b>读。</item>
    /// <item><b>回合行为算子</b>：<see cref="CharacterAbilityOp.SkipAttack"/> /
    /// <see cref="CharacterAbilityOp.DamageOpponents"/> / <see cref="CharacterAbilityOp.SelfDestruct"/>
    /// + <see cref="CharacterAbilityDefinition.TriggerTurn"/>（只在第 N 回合触发）。自爆怪就是它们的一组配置。</item>
    /// </list>
    ///
    /// <para><b>为什么必须脚本化逼出来</b>：</para>
    /// <list type="bullet">
    /// <item>「怪物不再补牌」在万局统计里<b>不会变红</b>（手牌少了只是打得差一点）；
    /// 而它失效的样子恰恰是「看起来一样能跑完」—— 必须逐条数事件。</item>
    /// <item>自爆最隐蔽的错法是把「主动不出牌」走成规则 §3 的「手上无牌 → 掉 1 点」：
    /// 两者都表现为「这一拍没打出牌」，但一个有自伤、一个没有，而且自爆伤害的数值来源
    /// （<see cref="AbilityAmountSource.OwnHp"/>）一旦取错（比如取了初始生命）也完全不会报错。</item>
    /// </list>
    /// </summary>
    internal static class MonsterScenario
    {
        /// <summary>要找够几个种子的合格样本才算证明。</summary>
        private const int NeededHits = 3;

        /// <summary>最多扫多少个种子。</summary>
        private const int MaxSeeds = 120;

        public static bool Run(List<string> report)
        {
            bool ok = true;

            ok &= CheckDealProfiles(report);
            ok &= CheckMonsterNeverDraws(report);
            ok &= CheckSelfDestruct(report);
            ok &= CheckSkipAttackKeepsHp(report);

            return ok;
        }

        // ══════════════════════════════════════════════════════
        //  1 · 发牌口径本身（纯函数）
        // ══════════════════════════════════════════════════════

        private static bool CheckDealProfiles(List<string> report)
        {
            DealProfile human = DealProfile.Human();
            DealProfile monster = DealProfile.Monster();

            bool humanOk = human.InitialHandSize == BattleState.InitialHandSize
                           && human.InitialReplaceLimit == BattleState.ReplaceLimitInitial
                           && human.DrawOnTurn(1) == 0
                           && human.DrawOnTurn(2) == 1
                           && human.DrawOnTurn(3) == 1
                           && human.DrawOnTurn(4) == 0
                           && human.ReplaceLimitOnTurn(2) == 1
                           && human.ReplaceLimitOnTurn(1) == 0;

            report.Add((humanOk ? "  [PASS] " : "  [FAIL] ")
                       + "人类发牌口径不变：开局 " + human.InitialHandSize + " 张可换 " + human.InitialReplaceLimit
                       + "、第 2–3 回合各补 1、第 4 回合起不补");

            bool monsterOk = monster.InitialHandSize == BattleState.HandLimit
                             && monster.InitialHandSize == 8
                             && monster.InitialReplaceLimit == 0
                             && monster.DrawOnTurn(2) == 0
                             && monster.DrawOnTurn(3) == 0
                             && monster.ReplaceLimitOnTurn(2) == 0;

            report.Add((monsterOk ? "  [PASS] " : "  [FAIL] ")
                       + "怪物发牌口径：开局 8 张（= 手牌上限）、之后不补、开局不换牌");

            bool defaultsOk = CharacterDefinition.DefaultMonster().Deal != null
                              && CharacterDefinition.DefaultMonster().Deal.InitialHandSize == 8
                              && CharacterDefinition.DefaultPlayer().Deal != null
                              && CharacterDefinition.DefaultPlayer().Deal.InitialHandSize == BattleState.InitialHandSize;

            report.Add((defaultsOk ? "  [PASS] " : "  [FAIL] ")
                       + "兜底角色带上了各自的口径（DefaultMonster = 8 张 / DefaultPlayer = 6 张）");

            bool rejectOk = false;
            try
            {
                new DealProfile(0);
            }
            catch (ArgumentException)
            {
                try
                {
                    new DealProfile(6, 3, 3, 2, 1, 1);
                }
                catch (ArgumentException)
                {
                    rejectOk = true;
                }
            }

            report.Add((rejectOk ? "  [PASS] " : "  [FAIL] ")
                       + "非法口径（0 张开局 / 补牌区间反向）在构造时就被拒绝");

            return humanOk && monsterOk && defaultsOk && rejectOk;
        }

        // ══════════════════════════════════════════════════════
        //  2 · 怪物「开局 8 张、之后一张都不补」
        // ══════════════════════════════════════════════════════

        private static bool CheckMonsterNeverDraws(List<string> report)
        {
            int games = 0;
            int bad = 0;
            int maxMonsterHand = 0;
            string firstBad = null;

            for (int seed = 1; seed <= 40; seed++)
            {
                Watcher w = RunGame(seed, null, 12, 12, out string error);
                if (w == null)
                {
                    if (firstBad == null)
                    {
                        firstBad = "seed " + seed + " " + error;
                    }

                    continue;
                }

                games++;
                if (w.MonsterHandMax > maxMonsterHand)
                {
                    maxMonsterHand = w.MonsterHandMax;
                }

                bool ok = w.MonsterInitialDraws == 8
                          && w.PlayerInitialDraws == BattleState.InitialHandSize
                          && w.MonsterTurnDraws == 0
                          && w.MonsterHandMax <= BattleState.HandLimit;

                if (!ok)
                {
                    bad++;
                    if (firstBad == null)
                    {
                        firstBad = "seed " + seed + "：开局手牌 怪 " + w.MonsterInitialDraws
                                   + " / 玩家 " + w.PlayerInitialDraws
                                   + "，怪物回合补牌 " + w.MonsterTurnDraws
                                   + " 次，怪物手牌峰值 " + w.MonsterHandMax;
                    }
                }
            }

            bool ok2 = games > 0 && bad == 0;
            report.Add((ok2 ? "  [PASS] " : "  [FAIL] ")
                       + "怪物开局真拿 8 张、玩家拿 6 张，之后怪物一次「回合补牌」都没有"
                       + "（跑了 " + games + " 局，怪物手牌峰值 " + maxMonsterHand + " ≤ " + BattleState.HandLimit + "）");
            if (firstBad != null)
            {
                report.Add("      · " + firstBad);
            }

            return ok2;
        }

        // ══════════════════════════════════════════════════════
        //  3 · 自爆怪：第 3 回合不出牌 + 按当前生命强制伤害 + 自毁
        // ══════════════════════════════════════════════════════

        private static bool CheckSelfDestruct(List<string> report)
        {
            int games = 0;
            int bad = 0;
            string firstBad = null;

            for (int seed = 1; seed <= MaxSeeds && games < NeededHits * 3; seed++)
            {
                // 自爆怪的能力：第 3 回合 → 对敌方造成「自己当前生命」的强制伤害 → 自毁。
                // ⚠ 两条能力要各自建实例（Runtime 有「用了几次」的内部状态，共用会互相影响）。
                var abilities = new[]
                {
                    (ICharacterAbilityDefinition)new CharacterAbilityDefinition(
                        "boom-damage", AbilityTrigger.TurnStarted, CharacterAbilityOp.DamageOpponents,
                        0, 0, 3, AbilityAmountSource.OwnHp),
                    new CharacterAbilityDefinition(
                        "boom-die", AbilityTrigger.TurnStarted, CharacterAbilityOp.SelfDestruct, 0, 0, 3),
                };

                Watcher w = RunGame(seed, abilities, 20, 12, out string error);
                if (w == null || !w.ReachedTurn3)
                {
                    continue;
                }

                games++;

                bool ok = w.SelfDestructSeen
                          && w.BoomDamage > 0
                          && w.BoomDamage == w.MonsterHpAtTurn3
                          && !w.MonsterAttackedInTurn3
                          && !w.MonsterDamagedInOwnTurn3
                          && w.PlayerHp == w.PlayerHpAtTurn3 - w.BoomDamage
                          && w.MonsterHp <= 0
                          && w.Over;

                if (!ok)
                {
                    bad++;
                    if (firstBad == null)
                    {
                        firstBad = "seed " + seed + "：自爆 " + w.SelfDestructSeen
                                   + "，伤害 " + w.BoomDamage + "（第 3 回合开始时怪物生命 " + w.MonsterHpAtTurn3 + "）"
                                   + "，第 3 回合怪物出牌 " + w.MonsterAttackedInTurn3
                                   + "，怪物自己掉血 " + w.MonsterDamagedInOwnTurn3
                                   + "，玩家 " + w.PlayerHpAtTurn3 + " → " + w.PlayerHp
                                   + "，怪物生命 " + w.MonsterHp + "，终局 " + w.Over;
                    }
                }
            }

            bool ok3 = games >= NeededHits && bad == 0;
            report.Add((ok3 ? "  [PASS] " : "  [FAIL] ")
                       + "自爆怪：第 3 回合不出牌 → 强制伤害 = 自己当前生命（不打折、不算防御）→ 自身归零"
                       + "（命中 " + games + " 局）");
            if (firstBad != null)
            {
                report.Add("      · " + firstBad);
            }

            if (games < NeededHits)
            {
                report.Add("      · 只逼出 " + games + " 局样本，不足以证明（检查测试设置）");
            }

            return ok3;
        }

        // ══════════════════════════════════════════════════════
        //  4 · 对照组：只「不出牌」时不得掉血、也不得提前终局
        // ══════════════════════════════════════════════════════

        private static bool CheckSkipAttackKeepsHp(List<string> report)
        {
            int games = 0;
            int bad = 0;
            string firstBad = null;

            for (int seed = 1; seed <= MaxSeeds && games < NeededHits; seed++)
            {
                var abilities = new[]
                {
                    (ICharacterAbilityDefinition)new CharacterAbilityDefinition(
                        "skip", AbilityTrigger.TurnStarted, CharacterAbilityOp.SkipAttack, 0, 0, 3),
                };

                Watcher w = RunGame(seed, abilities, 20, 12, out string error);
                if (w == null || !w.ReachedTurn3)
                {
                    continue;
                }

                games++;

                bool ok = !w.MonsterAttackedInTurn3          // 没出牌
                          && !w.MonsterDamagedInOwnTurn3     // 也**不是**「手上无牌掉 1 点」那条路
                          && !w.SelfDestructSeen
                          && w.ReachedTurn4;                 // 对局没被这一拍截断

                if (!ok)
                {
                    bad++;
                    if (firstBad == null)
                    {
                        firstBad = "seed " + seed + "：第 3 回合怪物出牌 " + w.MonsterAttackedInTurn3
                                   + "，怪物自己掉血 " + w.MonsterDamagedInOwnTurn3
                                   + "，走到第 4 回合 " + w.ReachedTurn4;
                    }
                }
            }

            bool ok4 = games >= NeededHits && bad == 0;
            report.Add((ok4 ? "  [PASS] " : "  [FAIL] ")
                       + "对照组：只声明「第 3 回合不出牌」时，不走「手上无牌 → 掉 1 点」、对局继续"
                       + "（命中 " + games + " 局）");
            if (firstBad != null)
            {
                report.Add("      · " + firstBad);
            }

            return ok4;
        }

        // ══════════════════════════════════════════════════════
        //  跑一局 + 观察
        // ══════════════════════════════════════════════════════

        /// <summary>一局里收集到的事实。</summary>
        private sealed class Watcher
        {
            public int PlayerInitialDraws;
            public int MonsterInitialDraws;

            /// <summary>怪物（seat1）收到过几次「回合 N 补牌」。</summary>
            public int MonsterTurnDraws;

            /// <summary>怪物（seat1）手牌数的峰值。</summary>
            public int MonsterHandMax;

            /// <summary>对局走到过第 3 回合的「怪物半场开始」。</summary>
            public bool ReachedTurn3;
            public bool ReachedTurn4;

            public int MonsterHpAtTurn3 = -1;
            public int PlayerHpAtTurn3 = -1;

            public bool SelfDestructSeen;
            public int BoomDamage;

            /// <summary>第 3 回合里怪物打出过进攻牌。</summary>
            public bool MonsterAttackedInTurn3;

            /// <summary>
            /// <b>在怪物自己第 3 回合那半场里</b>它掉过血。
            ///
            /// <para>⚠ 时间窗只圈「怪物那半场」：第 3 回合是玩家先攻，玩家把它打掉的血不算 ——
            /// 用「整个第 3 回合」会把正常挨打算成违规。</para>
            /// </summary>
            public bool MonsterDamagedInOwnTurn3;

            /// <summary>窗口是否开着（= 正处在怪物第 3 回合的半场里）。</summary>
            private bool _inMonsterTurn3;

            public void OpenWindow()
            {
                _inMonsterTurn3 = true;
            }

            public void CloseWindow()
            {
                _inMonsterTurn3 = false;
            }

            public bool WindowOpen
            {
                get { return _inMonsterTurn3; }
            }

            public int MonsterHp = -1;
            public int PlayerHp = -1;
            public bool Over;
        }

        private static Watcher RunGame(int seed, ICharacterAbilityDefinition[] monsterAbilities,
            int playerHp, int monsterHp, out string error)
        {
            error = null;
            var watch = new Watcher();

            try
            {
                var player = MakeCharacter("test.player", "你", CharacterKind.Player, playerHp,
                    DealProfile.Human(), null);
                var monster = MakeCharacter("test.monster", "怪", CharacterKind.Monster, monsterHp,
                    DealProfile.Monster(), monsterAbilities);

                var setup = new BattleSetup(new[]
                {
                    new ParticipantSetup(player, ControlKind.Ai, 0),
                    new ParticipantSetup(monster, ControlKind.Ai, 1),
                });

                BattleEngine engine = BattleEngine.Create(seed, setup);
                engine.OnEvent += e => Observe(engine, watch, e);
                engine.Start();

                var brain = new SimpleAiAgent();
                int guard = 0;
                while (!engine.IsOver)
                {
                    engine.Advance();
                    if (engine.IsOver)
                    {
                        break;
                    }

                    if (++guard > 20000 || engine.Pending == null)
                    {
                        error = "引擎推进卡住（Pending=" + (engine.Pending == null ? "null" : "有") + "）";
                        return null;
                    }

                    engine.Submit(brain.Decide(engine.Pending));
                }

                watch.MonsterHp = engine.State.Of(1).Hp;
                watch.PlayerHp = engine.State.Of(0).Hp;
                watch.Over = engine.State.IsOver;
                return watch;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private static void Observe(BattleEngine engine, Watcher w, BattleEvent e)
        {
            if (e is CardDrawnEvent)
            {
                var d = (CardDrawnEvent)e;
                if (d.Reason == "开局发牌")
                {
                    if (d.Seat == 0) w.PlayerInitialDraws++;
                    else if (d.Seat == 1) w.MonsterInitialDraws++;
                }
                else if (d.Seat == 1 && d.Reason != null && d.Reason.StartsWith("回合"))
                {
                    w.MonsterTurnDraws++;
                }

                return;
            }

            if (e is TurnStartedEvent)
            {
                var t = (TurnStartedEvent)e;

                // ⚠ 只看「非连击追加」的那一次 —— 连击会再发一条同回合的 TurnStarted，
                //   拿它取样会把「第 3 回合开始时的生命」算成打了一半之后的数。
                if (t.IsComboFollowUp)
                {
                    return;
                }

                // 时间窗：只在「怪物第 3 回合那个半场」里开，遇上别的半场就关。
                if (t.TurnNumber == 3 && t.Seat == 1)
                {
                    w.OpenWindow();
                }
                else
                {
                    w.CloseWindow();
                }

                if (t.TurnNumber >= 4)
                {
                    w.ReachedTurn4 = true;
                }

                if (t.Seat == 1)
                {
                    int hand = engine.State.Of(1).Hand.Count;
                    if (hand > w.MonsterHandMax)
                    {
                        w.MonsterHandMax = hand;
                    }

                    if (t.TurnNumber == 3)
                    {
                        w.ReachedTurn3 = true;
                        w.MonsterHpAtTurn3 = engine.State.Of(1).Hp;
                        w.PlayerHpAtTurn3 = engine.State.Of(0).Hp;
                    }
                }

                return;
            }

            if (e is AttackDeclaredEvent)
            {
                var a = (AttackDeclaredEvent)e;
                if (a.Seat == 1 && a.TurnNumber == 3)
                {
                    w.MonsterAttackedInTurn3 = true;
                }

                return;
            }

            if (e is SelfDestructEvent)
            {
                var s = (SelfDestructEvent)e;
                if (s.Seat == 1)
                {
                    w.SelfDestructSeen = true;
                }

                return;
            }

            if (e is DamageTakenEvent)
            {
                var d = (DamageTakenEvent)e;
                if (d.Seat == 1)
                {
                    // 自爆走的是 SelfDestructEvent；这里只可能是「手上无牌」「未挡住」这类常规掉血。
                    // 圈在怪物自己那半场里 —— 玩家在同一个回合把它打掉的血不算违规。
                    if (w.WindowOpen)
                    {
                        w.MonsterDamagedInOwnTurn3 = true;
                    }

                    return;
                }

                if (d.Seat == 0 && e.TurnNumber == 3 && d.Source == "自爆")
                {
                    w.BoomDamage = d.Amount;
                }
            }
        }

        private static CharacterDefinition MakeCharacter(string id, string name, CharacterKind kind, int hp,
            DealProfile deal, ICharacterAbilityDefinition[] abilities)
        {
            return new CharacterDefinition(id, name, kind, hp, hp, 1, CardPool.AllCards(),
                abilities, null, deal);
        }
    }
}
