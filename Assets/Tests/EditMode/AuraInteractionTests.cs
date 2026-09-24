using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MagicBrawl.Core;
using NUnit.Framework;

namespace MagicBrawl.Tests
{
    /// <summary>
    /// 光环与出牌同拍提交（2026-09-18 交互改动）的回归测试。
    ///
    /// <para><b>协议变化</b>：<c>RequestKind.ChooseAuraUse</c> 已经删掉 ——
    /// 光环选项现在并进 <see cref="RequestKind.ChooseAttackCard"/> /
    /// <see cref="RequestKind.ChooseDefense"/> 的 <c>Options</c>，
    /// 玩家在判定区「准备使用」的光环通过 <c>DecisionResponse.AuraOptionIndices</c> 一并回填；
    /// 每加 / 减一枚准备，回填一次 <c>DecisionResponse.PrepAuras</c> 让引擎按新预算重发本拍。</para>
    /// </summary>
    public class AuraInteractionTests
    {
        private static CardInstance Card(PlayerState owner, int power, params EffectDef[] effects)
        {
            var def = new CardDef(0, "test", "回归测试牌", power, 4, effects);
            var card = (CardInstance)Activator.CreateInstance(typeof(CardInstance),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { def, owner.Seat }, null);
            typeof(CardInstance).GetMethod("ToHand", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card, null);
            owner.Hand.Add(card);
            return card;
        }

        private static CardInstance Aura(PlayerState owner, AuraKind kind, int value, int count = 1)
        {
            var effects = Enumerable.Range(0, count).Select(_ =>
                new EffectDef(EffectTrigger.Attack, EffectOp.Aura, value, aura: kind)).ToArray();
            var card = Card(owner, 1, effects);
            CooldownOps.PutIntoCooldown(owner, card, null, null);
            typeof(CardInstance).GetProperty("AuraTokens").SetValue(card, count);
            typeof(CardInstance).GetProperty("AuraLive").SetValue(card, true);
            return card;
        }

        private static BattleEngine Ready()
        {
            var engine = BattleEngine.Create(20260918);
            engine.Start();
            for (int i = 0; i < 40; i++)
            {
                engine.Advance();
                if (engine.Pending.Kind == RequestKind.ChooseAttackCard) return engine;
                engine.Submit(DecisionResponse.Of(engine.Pending.Seat));
            }
            throw new Exception("未进入进攻决策");
        }

        /// <summary>把本拍的进攻选牌换成 <paramref name="card"/>（可选带上一个光环选项）。</summary>
        private static void Attack(BattleEngine engine, CardInstance card, CardInstance auraSource = null)
        {
            var options = new List<Option>
            {
                new Option { Index = 0, Kind = OptionKind.PlayCard, Seat = card.OwnerSeat, Card = card },
            };

            var auraIndices = new List<int>();

            if (auraSource != null)
            {
                options.Add(new Option
                {
                    Index = 1,
                    Kind = OptionKind.UseAura,
                    Seat = auraSource.OwnerSeat,
                    Card = auraSource,
                    AuraSource = auraSource,
                    AuraKind = AuraKind.Combo,
                    Value = 3,
                });
                auraIndices.Add(1);
            }

            engine.Pending.Options = options.ToArray();
            engine.Submit(DecisionResponse.WithAuras(engine.Pending.Seat, new[] { 0 }, auraIndices.ToArray()));
            engine.Advance();
        }

        private static void Choose(BattleEngine engine, Option option)
        {
            engine.Submit(DecisionResponse.Of(engine.Pending.Seat, option.Index));
            engine.Advance();
        }

        /// <summary>只回填光环（无主选择）—— 免疫拖进判定区就是这条路径。</summary>
        private static void UseAuras(BattleEngine engine, params Option[] options)
        {
            int[] indices = options.Select(o => o.Index).ToArray();
            engine.Submit(DecisionResponse.WithAuras(engine.Pending.Seat, null, indices));
            engine.Advance();
        }

        /// <summary>报备「准备使用」的光环，请引擎按新预算重发本拍。</summary>
        private static void PrepAuras(BattleEngine engine, params Option[] options)
        {
            int[] indices = options.Select(o => o.Index).ToArray();
            engine.Submit(DecisionResponse.PrepAuras(engine.Pending.Seat, indices));
            engine.Advance();
        }

        private static void Skip(BattleEngine engine)
        {
            engine.Submit(DecisionResponse.Of(engine.Pending.Seat));
            engine.Advance();
        }

        [TestCase(AuraKind.ImmuneLow, 3, 3, false, false, false)]
        [TestCase(AuraKind.ImmuneLow, 3, 3, false, false, true)]
        [TestCase(AuraKind.ImmuneLow, 3, 2, true, false, false)]
        [TestCase(AuraKind.ImmuneLow, 3, 3, true, true, false)]
        [TestCase(AuraKind.ImmuneHigh, 6, 6, false, false, false)]
        [TestCase(AuraKind.ImmuneHigh, 6, 8, true, true, false)]
        public void ImmunityBlocksEntireAttackWithoutCards(AuraKind kind, int threshold,
            int power, bool doubleAttack, bool emptyHand, bool extraDamage)
        {
            var engine = Ready();
            int attacker = engine.Pending.Seat;
            var defender = engine.State.Of(1 - attacker);
            var effects = new List<EffectDef>();
            if (doubleAttack) effects.Add(new EffectDef(EffectTrigger.Attack, EffectOp.Double));
            if (extraDamage) effects.Add(new EffectDef(EffectTrigger.Attack, EffectOp.ExtraDamageIfUnblocked, 2));
            var attack = Card(engine.State.Of(attacker), power, effects.ToArray());
            if (emptyHand) defender.Hand.Clear();
            var source = Aura(defender, kind, threshold);
            int hp = defender.Hp;
            var before = defender.Hand.Select(c => c.Uid).ToArray();
            DefenseResolvedEvent resolved = null;
            int[] handAtResolution = null;
            int damage = 0;
            bool attackFinished = false;
            int hpAtResolution = 0;
            engine.OnEvent += e =>
            {
                if (e is TurnStartedEvent && resolved != null) attackFinished = true;
                if (e is DefenseResolvedEvent result && result.DefenderSeat == defender.Seat)
                {
                    resolved = result;
                    hpAtResolution = defender.Hp;
                    handAtResolution = defender.Hand.Select(c => c.Uid).ToArray();
                }
                if (e is DamageTakenEvent hit && hit.Seat == defender.Seat && !attackFinished) damage += hit.Amount;
            };

            Attack(engine, attack);

            // 光环现在就在防御那一拍里（不再有独立的光环选择环节）
            Assert.AreEqual(RequestKind.ChooseDefense, engine.Pending.Kind);

            // 「拖进判定区即当场使用」：只回填光环、不交牌
            UseAuras(engine, engine.Pending.Options.Single(o => o.AuraSource == source));

            Assert.NotNull(resolved);
            Assert.IsTrue(resolved.Success && resolved.UsedImmune);
            Assert.IsEmpty(resolved.Cards);
            CollectionAssert.AreEqual(before, handAtResolution);
            Assert.AreEqual(0, damage);
            Assert.AreEqual(hp, hpAtResolution);
            Assert.AreEqual(0, source.AuraTokens);
            Assert.IsFalse(engine.Pending != null && engine.Pending.Seat == defender.Seat
                && engine.Pending.Kind == RequestKind.ChooseDefense);
        }

        [TestCase(AuraKind.ImmuneLow, 3, 4)]
        [TestCase(AuraKind.ImmuneHigh, 6, 5)]
        public void OutOfRangeImmunityCannotBeUsed(AuraKind kind, int threshold, int power)
        {
            var engine = Ready();
            int seat = engine.Pending.Seat;
            var source = Aura(engine.State.Of(1 - seat), kind, threshold);
            Attack(engine, Card(engine.State.Of(seat), power));
            Assert.AreEqual(RequestKind.ChooseDefense, engine.Pending.Kind);
            Assert.IsFalse(engine.Pending.Options.Any(o => o.AuraSource == source));
            Assert.AreEqual(1, source.AuraTokens);
        }

        [Test]
        public void EveryMatchingImmuneSourceCanBeChosen()
        {
            var engine = Ready();
            int seat = engine.Pending.Seat;
            var defender = engine.State.Of(1 - seat);
            var first = Aura(defender, AuraKind.ImmuneLow, 3);
            var second = Aura(defender, AuraKind.ImmuneLow, 3);
            Attack(engine, Card(engine.State.Of(seat), 3));
            Assert.AreEqual(2, engine.Pending.Options.Count(o => o.Kind == OptionKind.UseAura));
            UseAuras(engine, engine.Pending.Options.Single(o => o.AuraSource == second));
            Assert.AreEqual(1, first.AuraTokens);
            Assert.AreEqual(0, second.AuraTokens);
        }

        [Test]
        public void UnpreparedAuraIsNotAutomaticallySpentOnDefense()
        {
            var engine = Ready();
            int seat = engine.Pending.Seat;
            var defender = engine.State.Of(1 - seat);
            defender.Hand.Clear();
            var weak = Card(defender, 1);
            var source = Aura(defender, AuraKind.DefPower, 2);
            Attack(engine, Card(engine.State.Of(seat), 3));

            Assert.AreEqual(RequestKind.ChooseDefense, engine.Pending.Kind);

            // 没准备光环 → 预算为 0 → 力量 1 的牌补不起 2 点，不该出现在选项里
            Assert.AreEqual(0, engine.Pending.ContextDefenseBonus);
            Assert.IsFalse(engine.Pending.Options.Any(o => o.AuraSource == null && o.Card == weak));

            // 弃权 → 指示物保留（没有正在打出的牌，光环不消耗）
            Skip(engine);
            Assert.AreEqual(1, source.AuraTokens);
        }

        [Test]
        public void PreparingAurasRaisesBudgetAndEnablesDoubleDefense()
        {
            var engine = Ready();
            int seat = engine.Pending.Seat;
            var defender = engine.State.Of(1 - seat);
            defender.Hand.Clear();
            Card(defender, 1);
            Card(defender, 1);
            var source = Aura(defender, AuraKind.DefPower, 2, 2);
            Attack(engine, Card(engine.State.Of(seat), 3,
                new EffectDef(EffectTrigger.Attack, EffectOp.Double)));

            Assert.AreEqual(RequestKind.ChooseDefense, engine.Pending.Kind);
            Assert.IsFalse(engine.Pending.Options.Any(o => o.AuraSource == null && o.PairCard != null));

            var tokens = engine.Pending.Options.Where(o => o.AuraSource == source).ToArray();
            Assert.AreEqual(2, tokens.Length);

            // 报备两枚 → 引擎按 +4 预算重发本拍 → 双发的成对选项才出现
            PrepAuras(engine, tokens);
            Assert.AreEqual(4, engine.Pending.ContextDefenseBonus);

            var pair = engine.Pending.Options.First(o => o.AuraSource == null && o.PairCard != null);

            engine.Submit(DecisionResponse.WithAuras(
                engine.Pending.Seat,
                new[] { pair.Index },
                engine.Pending.Options.Where(o => o.AuraSource == source).Select(o => o.Index).ToArray()));
            engine.Advance();

            Assert.AreEqual(0, source.AuraTokens);
            Assert.IsEmpty(CardSnapshot.From(source).AuraTokensDetail);
        }

        [TestCase(3, true)]
        [TestCase(4, false)]
        public void ComboAuraOnlyTakesEffectForEligibleAttackPower(int power, bool usable)
        {
            var engine = Ready();
            var attacker = engine.State.Of(engine.Pending.Seat);
            var source = Aura(attacker, AuraKind.Combo, 3);

            // 准备发生在选牌之前（那时还不知道打哪张），所以连击光环照样可选；
            // 合法性由引擎在提交时按最终打出的牌精筛 —— 不合格的既不生效也不消耗。
            Attack(engine, Card(attacker, power), source);

            Assert.AreEqual(usable ? 0 : 1, source.AuraTokens);
        }

        [Test]
        public void ReturnedAuraExpiresInSnapshotAndOptions()
        {
            var engine = Ready();
            var owner = engine.State.Player;
            var source = Aura(owner, AuraKind.AtkOrDefPower, 2, 2);
            Assert.AreEqual(2, CardSnapshot.From(source).AuraTokensDetail.Length);
            CooldownOps.Refresh(owner, source, null);
            Assert.IsEmpty(CardSnapshot.From(source).AuraTokensDetail);
            Assert.IsFalse(AuraResolver.BuildOptions(owner, AuraContext.Defend).Any(o => o.AuraSource == source));
        }

        [Test]
        public void AiCanFinishGamesWithAurasEnabled()
        {
            for (int seed = 18000; seed < 18100; seed++)
            {
                var engine = BattleEngine.Create(seed);
                var ai = new SimpleAiAgent { UseAuras = true };
                engine.Start();
                int decisions = 0;
                while (!engine.IsOver)
                {
                    engine.Advance();
                    if (engine.IsOver) break;
                    Assert.NotNull(engine.Pending);
                    engine.Submit(ai.Decide(engine.Pending));
                    Assert.Less(++decisions, 20000);
                }
            }
        }
    }
}
