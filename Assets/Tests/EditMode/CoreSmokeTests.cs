using System.Collections.Generic;
using MagicBrawl.Core;
using NUnit.Framework;

namespace MagicBrawl.Tests
{
    /// <summary>
    /// EditMode 测试（M11 的 Unity 侧镜像）。
    ///
    /// 与 <c>Tools/RuleSelfTest</c> 的分工：命令行那套跑万局 + 全量断言（快、无需 Unity），
    /// 这里只留「随手点一下就能确认内核没坏」的关键几条，方便在 Unity Test Runner 里跑。
    /// </summary>
    public class CoreSmokeTests
    {
        [Test]
        public void 卡表_共40张且力量分布吻合原始统计()
        {
            Assert.AreEqual(40, CardLibrary.Count);

            string report;
            Assert.IsTrue(CardLibrary.ValidatePowerDistribution(out report), report);
        }

        [Test]
        public void 卡表_冷却分布为2比7_3比21_4比12()
        {
            string report;
            Assert.IsTrue(CardLibrary.ValidateCooldownDistribution(out report), report);
        }

        [Test]
        public void 卡表_每张卡都有效果且ID无重复()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef d = CardLibrary.GetByIndex(i);
                Assert.Greater(d.Effects.Count, 0, d.Id + " 没有任何效果");
                Assert.IsTrue(seen.Add(d.Id), "卡 ID 重复：" + d.Id);
            }
        }

        [Test]
        public void 卡表_双光环卡的指示物数为2()
        {
            Assert.AreEqual(2, CardLibrary.Get("af").AuraTokenCount);
            Assert.AreEqual(2, CardLibrary.Get("aj").AuraTokenCount);
        }

        [Test]
        public void 卡表_光环触发符号同符号且都是αβγ()
        {
            string report;
            Assert.IsTrue(CardLibrary.ValidateAuraTriggers(out report), report);

            // 每条光环都必须落在一个「会被第 ⑤ 步点亮」的符号上（α / β）——
            // 落到 Passive 或 γ 就是永远不亮的死效果。
            // （本批 40 张卡的光环全部标着 α，见 `Docs/rules/02-卡牌图鉴.md`；
            //   这是当前卡表的事实，不是需要钉死的约束，所以这里只校验完整性。）
            for (int i = 0; i < CardLibrary.Count; i++)
            {
                CardDef d = CardLibrary.GetByIndex(i);
                if (!d.HasAura)
                {
                    continue;
                }

                int covered = d.AuraTokenCountOf(EffectTrigger.Attack)
                              + d.AuraTokenCountOf(EffectTrigger.Defend);
                Assert.AreEqual(d.AuraTokenCount, covered,
                    d.Name + " 有光环指示物却没写 α / β 触发符号");
            }
        }

        [Test]
        public void 卡表_模仿卡面力量显示为X()
        {
            CardDef mimic = CardLibrary.Get("x");
            Assert.IsTrue(mimic.HiddenPower);
            Assert.AreEqual("X", mimic.PowerText);
        }

        [Test]
        public void 引擎_万局中抽样100局必须正常终局且无异常()
        {
            for (int i = 0; i < 100; i++)
            {
                int seed = 50000 + i;
                BattleEngine engine = BattleEngine.Create(seed);
                var agent = new SimpleAiAgent();

                engine.Start();
                int guard = 0;

                while (!engine.IsOver)
                {
                    engine.Advance();
                    if (engine.IsOver)
                    {
                        break;
                    }

                    Assert.IsNotNull(engine.Pending, "seed " + seed + "：既未终局也没有待决策");
                    engine.Submit(agent.Decide(engine.Pending));

                    Assert.Less(guard++, 20000, "seed " + seed + "：疑似死循环");
                }

                Assert.IsTrue(engine.IsOver, "seed " + seed + "：没有正常终局");
            }
        }

        [Test]
        public void 规则_进攻决策不给放弃选项()
        {
            for (int seed = 60000; seed < 60010; seed++)
            {
                BattleEngine engine = BattleEngine.Create(seed);
                var agent = new SimpleAiAgent();

                engine.Start();
                while (!engine.IsOver)
                {
                    engine.Advance();
                    if (engine.IsOver)
                    {
                        break;
                    }

                    if (engine.Pending.Kind == RequestKind.ChooseAttackCard)
                    {
                        Assert.IsTrue(engine.Pending.ContextNoSkip, "有牌时必须能进攻");
                        Assert.IsNull(engine.Pending.SkipOption, "进攻决策不应提供 Skip");
                    }

                    engine.Submit(agent.Decide(engine.Pending));
                }
            }
        }

        [Test]
        public void 不变量_冷却区始终满足1到基础冷却之间()
        {
            for (int seed = 61000; seed < 61010; seed++)
            {
                BattleEngine engine = BattleEngine.Create(seed);
                var agent = new SimpleAiAgent();
                engine.Start();

                while (!engine.IsOver)
                {
                    engine.Advance();
                    if (engine.IsOver)
                    {
                        break;
                    }

                    engine.Submit(agent.Decide(engine.Pending));

                    for (int s = 0; s < engine.State.Players.Count; s++)
                    {
                        PlayerState p = engine.State.Players[s];
                        for (int k = 0; k < p.CoolingZone.Count; k++)
                        {
                            CardInstance c = p.CoolingZone[k];
                            Assert.GreaterOrEqual(c.RemainingCooldown, 1, c.Def.Name + " 剩余冷却 < 1");
                            Assert.LessOrEqual(c.RemainingCooldown, c.Def.Cooldown, c.Def.Name + " 超过基础冷却");
                        }

                        Assert.LessOrEqual(p.Hand.Count, BattleState.HandLimit, "手牌超过 8 张");
                        Assert.LessOrEqual(p.Hp, p.MaxHp, "Hp 超过 MaxHp");
                    }
                }
            }
        }
    }
}
