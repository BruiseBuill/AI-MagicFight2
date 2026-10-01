using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    internal static class BuiltinEffects
    {
        internal static void Register(EffectRegistry registry)
        {
            Register(registry, EffectOp.Haste, EffectStage.Cooldown, Cooldown, EffectFamily.Haste, EffectTargetShape.Card);
            Register(registry, EffectOp.HastePerHpLoss, EffectStage.Cooldown, Cooldown, EffectFamily.Haste, EffectTargetShape.Card);
            Register(registry, EffectOp.Slow, EffectStage.Cooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Card);
            Register(registry, EffectOp.SlowIfLastTwoHand, EffectStage.Cooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Card);
            Register(registry, EffectOp.SlowIfUnblocked, EffectStage.AfterCooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Card);
            Register(registry, EffectOp.HasteZone, EffectStage.Cooldown, Cooldown, EffectFamily.Haste, EffectTargetShape.Zone);
            Register(registry, EffectOp.SlowZone, EffectStage.Cooldown, Cooldown, EffectFamily.Slow, EffectTargetShape.Zone);
            Register(registry, EffectOp.Refresh, EffectStage.Cooldown, Refresh);
            Register(registry, EffectOp.ResetCooldown, EffectStage.Cooldown, Refresh);
            Register(registry, EffectOp.ReadyRefresh, EffectStage.Cooldown, Refresh);
            Register(registry, EffectOp.RemoveFromGame, EffectStage.Cooldown, Remove);
            Register(registry, EffectOp.CoolHandForAtk, EffectStage.Power, CoolHand);
            Register(registry, EffectOp.CoolHandForCombo, EffectStage.Power, CoolHand);
            Register(registry, EffectOp.CoolHandForHaste, EffectStage.Cooldown, CoolHand);
            Register(registry, EffectOp.Copy, EffectStage.Power, Copy);
            Register(registry, EffectOp.LookAndCool, EffectStage.Cooldown, Peek);
            Automatic(registry, EffectOp.SlowZoneBoth, EffectStage.Cooldown, (c, e) => {
                foreach (int seat in c.TargetSeats(e)) c.ChangeZone(seat, e.Arg("threshold"), false);
            });
            Automatic(registry, EffectOp.AtkPlus, EffectStage.Power, (c, e) => c.AddAttackPower(e.Arg("amount")));
            Automatic(registry, EffectOp.AtkPlusPerCooling, EffectStage.Power, (c, e) => c.AddAttackPower(c.Owner.CoolingZone.Count * e.Arg("amount")));
            Automatic(registry, EffectOp.AtkPlusPerHpLoss, EffectStage.Power, (c, e) => {
                int lost = 0; foreach (int seat in c.TargetSeats(e)) lost += c.State.Of(seat).HpLost;
                c.AddAttackPower(lost * e.Arg("amount"));
            });
            Automatic(registry, EffectOp.DoubleAtkBonus, EffectStage.Power, (c, e) => c.Attack.DoubleAtkBonus = true);
            Automatic(registry, EffectOp.NoAtkBuff, EffectStage.Power, (c, e) => c.Attack.NoAtkBuff = true);
            Automatic(registry, EffectOp.Combo, EffectStage.Power, (c, e) => c.GrantCombo());
            Automatic(registry, EffectOp.Double, EffectStage.Power, (c, e) => c.Attack.IsDouble = true);
            Automatic(registry, EffectOp.QuickRefill, EffectStage.Power, (c, e) => c.CooldownReduction += e.Arg("amount", 1) == 0 ? 1 : e.Arg("amount"));
            Automatic(registry, EffectOp.HealMinusMax, EffectStage.Power, (c, e) => {
                c.Heal(c.Seat, e.Arg("amount")); c.CutMaxHp(c.Seat, e.Arg("secondary"));
            });
            // 毒刺（ao）：给被攻击的目标挂虚弱。① 力量阶段的纯写入 —— 无决策、无可放弃。
            // 目标范围走卡表里的 EffectTargetScope.Opponent（不能是 Participants，那样会削到自己）。
            Automatic(registry, EffectOp.Weaken, EffectStage.Power, (c, e) => {
                int amount = e.Arg("amount");
                if (amount <= 0) return;
                foreach (int seat in c.TargetSeats(e))
                {
                    PlayerState target = c.State.Of(seat);
                    target.WeakenStacks += amount;
                    c.Emit(new WeakenAppliedEvent { Seat = seat, Amount = amount, Stacks = target.WeakenStacks });
                }
            });
            // 水之形（aq）：α / β「本卡**基础力量**成长」。
            //   写的是**卡牌实例**上的 BattlePowerBonus（本场战斗内有效），不是本次攻防的临时增量
            //   —— 归 ① 力量阶段 / ② 防御阶段，两处都晚于本次的力量快照与比拼
            //   （进攻侧 BasePower 在选牌那一刻取好、防御侧 DefenseResolver 在提交那一刻算完），
            //   所以「本次用不上、以后才生效」，正好是卡面写的「每次进攻 / 防御后」。
            //   ⚠ 力量视为 X 的牌（模仿 x）不吃成长：它的力量由 Copy 临时接管、
            //     卡面恒定「未复制时视为 1」，写进去会与卡面直接矛盾
            //     （模仿确实能把水之形复制过来 —— 它无光环、基础冷却 2 ≤ 3）。
            Automatic(registry, EffectOp.GrowBasePower, EffectStage.Power, (c, e) => {
                int amount = e.Arg("amount");
                if (amount <= 0 || c.Source.Def.HiddenPower) return;
                c.Source.BattlePowerBonus += amount;
                c.Source.ResetEffectivePower();
                c.Emit(new BasePowerGrownEvent
                {
                    Seat = c.Seat,
                    Card = c.Source,
                    Amount = amount,
                    BattlePowerBonus = c.Source.BattlePowerBonus,
                    Power = c.Source.EffectivePower,
                });
            });
            // 闪电球（ar）：条件连击。条件 hand-at-play = 1 由 EffectDef 构造时自动挂上；
            //   条件不满足时 EffectWindow 根本不会执行这一条 —— 不弹决策、不消耗任何东西。
            Automatic(registry, EffectOp.ComboIfLastHand, EffectStage.Power, (c, e) => c.GrantCombo());
            Automatic(registry, EffectOp.CoolMinusIfUnblocked, EffectStage.AfterCooldown, (c, e) => {
                if (!c.DefenseSucceeded) for (int i = 0; i < e.Arg("amount") && c.Source.IsCooling; i++) c.ChangeCooldown(c.Source, true);
            });
            Automatic(registry, EffectOp.ExtraDamageIfUnblocked, EffectStage.AfterCooldown, (c, e) => {
                if (c.DefenseSucceeded) return;
                c.Damage(c.OpponentSeat, e.Arg("amount"));
                if (!c.State.IsOver) c.CutMaxHp(c.OpponentSeat, e.Arg("secondary"));
            });
            Automatic(registry, EffectOp.Aura, EffectStage.Aura, (c, e) => {
                if (!c.Source.IsCooling) return;
                c.Source.AddAura(e);
                c.Emit(new AuraActivatedEvent { Seat = c.Seat, Card = c.Source, Tokens = 1 });
            });
            // Defense modifiers are queried before committing a legal defense, never applied twice.
            Automatic(registry, EffectOp.DefPlus, EffectStage.Defense, (c, e) => { });
            Automatic(registry, EffectOp.Guard, EffectStage.Defense, (c, e) => { });
        }
        private static void Register(EffectRegistry r, EffectOp op, EffectStage stage,
            Func<EffectContext, EffectDef, IEnumerable<EffectChoice>> execute,
            EffectFamily family = EffectFamily.None, EffectTargetShape shape = EffectTargetShape.None)
        { r.Register(op.ToString(), new EffectHandler(stage, execute, family, shape)); }
        private static void Automatic(EffectRegistry r, EffectOp op, EffectStage stage, Action<EffectContext, EffectDef> action)
        { Register(r, op, stage, (c, e) => Apply(c, e, action)); }
        private static IEnumerable<EffectChoice> Apply(EffectContext c, EffectDef e, Action<EffectContext, EffectDef> action)
        { action(c, e); yield break; }

        /// <param name="optional">
        /// 可放弃：会额外发一条「跳过…」选项（加速 / 减速这类「不做也行」的效果）。
        /// </param>
        /// <param name="allowEmpty">
        /// 可以一张都不选、直接确认，但<b>不发「跳过」选项</b>。磁暴 / 充能的选牌弹窗要的就是这一种：
        /// 界面上「可以空着确认」只由 <c>MinSelect = 0</c> 表达（见 <c>HandPickView.RefreshConfirm</c>），
        /// 再摆一条「跳过」反而多出一个语义重复的按钮（2026-09-27 用户口径）。
        /// </param>
        private static DecisionRequest Request(EffectContext c, RequestKind kind, string title, List<Option> options,
            bool optional = false, int maximum = 1, bool haste = false, bool allowEmpty = false)
        {
            if (options.Count == 0) return null;
            if (optional) options.Add(new Option { Kind = OptionKind.Skip, Label = "跳过" + title });
            for (int i = 0; i < options.Count; i++) options[i].Index = i;
            bool canLeaveEmpty = optional || allowEmpty;
            return new DecisionRequest { Seat = c.Seat, Kind = kind, Title = title, Prompt = title,
                Options = options, MinSelect = canLeaveEmpty ? 0 : 1, MaxSelect = maximum,
                ContextNoSkip = !canLeaveEmpty, ContextHaste = haste };
        }
        private static Option CardOption(CardInstance card)
        { return new Option { Kind = OptionKind.ChooseCard, Seat = card.OwnerSeat, Card = card, Label = card.Def.Name, Value = card.RemainingCooldown }; }
        private static Option Pick(EffectChoice choice)
        {
            if (choice.Selected != null) foreach (Option option in choice.Selected) if (!option.IsSkip) return option;
            return null;
        }

        private static IEnumerable<EffectChoice> Cooldown(EffectContext c, EffectDef e)
        {
            bool haste = e.Op == EffectOp.Haste || e.Op == EffectOp.HasteZone || e.Op == EffectOp.HastePerHpLoss;
            bool zone = e.Op == EffectOp.HasteZone || e.Op == EffectOp.SlowZone;
            int count = e.Arg("count", 1);
            if (e.Op == EffectOp.HastePerHpLoss) count *= c.Owner.HpLost;
            string title = (zone ? "区域" : "") + (haste ? "加速" : "减速");
            for (int n = 0; n < count; n++)
            {
                var choice = new EffectChoice(() => {
                    var options = new List<Option>();
                    foreach (int seat in c.TargetSeats(e))
                    {
                        var values = new HashSet<int>();
                        foreach (CardInstance card in c.State.Of(seat).CoolingZone)
                        {
                            if (c.WasSelected(e.DistinctTargetGroup, card) || !haste && card.RemainingCooldown >= card.Def.Cooldown) continue;
                            if (zone)
                            {
                                if (values.Add(card.RemainingCooldown)) options.Add(new Option { Kind = OptionKind.ZoneValue,
                                    Seat = seat, Value = card.RemainingCooldown, Label = title + " " + card.RemainingCooldown });
                            }
                            else options.Add(CardOption(card));
                        }
                    }
                    return Request(c, zone ? RequestKind.ChooseZoneValue : haste ? RequestKind.ChooseHasteTarget : RequestKind.ChooseSlowTarget,
                        title, options, e.CanSkip, haste: haste);
                });
                yield return choice;
                Option pick = Pick(choice);
                if (pick == null) yield break;
                if (zone) c.ChangeZone(pick.Seat, pick.Value, haste);
                else { c.Remember(e.DistinctTargetGroup, pick.Card); c.ChangeCooldown(pick.Card, haste); }
            }
        }
        private static IEnumerable<EffectChoice> Refresh(EffectContext c, EffectDef e)
        {
            bool reset = e.Op == EffectOp.ResetCooldown;
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                IEnumerable<int> seats = e.Op == EffectOp.ReadyRefresh ? new[] { c.Seat } : c.TargetSeats(e);
                foreach (int seat in seats)
                {
                    if (reset && e.Arg("secondary") == 1 && seat != c.OpponentSeat) continue;
                    foreach (CardInstance card in c.State.Of(seat).CoolingZone)
                        if ((!reset || card.RemainingCooldown < card.Def.Cooldown) && (e.Op != EffectOp.ReadyRefresh || card != c.Source)) options.Add(CardOption(card));
                }
                return Request(c, RequestKind.ChooseRefreshTarget, reset ? "重置冷却" : "立即冷却完成", options, haste: !reset);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick != null) c.Refresh(pick.Card, reset);
        }
        /// <summary>
        /// 漩涡：把己方冷却区里的一张法术永久移出游戏，然后获得 <c>count</c> 次加速。
        ///
        /// <para><b>可以一张都不移出（2026-09-28 用户口径）</b>：卡面原文是「<b>可</b>将冷却区中的
        /// 一张法术永久移出游戏，获得加速 ×3」——「可」就是可选。不移出 → 也拿不到加速
        /// （下面 <c>pick == null</c> 那一行直接收摊，不会走到 <see cref="Cooldown"/>）。
        /// 界面上这条由 <c>MinSelect = 0</c> 表达，<b>不发</b> Skip 选项
        /// （口径与磁暴 / 充能一致：多一条「跳过」反而多一个语义重复的按钮）。</para>
        ///
        /// <para>⚠ M31（2026-09-22）曾按当时的用户口径做成「强制选一张」（<c>MinSelect = 1</c>），
        /// 2026-09-28 按卡面字面意思改回来 —— <c>Tools/RuleSelfTest/RemoveFromGameScenario.cs</c>
        /// 的断言与注释同步改成 0 / 1。</para>
        /// </summary>
        private static IEnumerable<EffectChoice> Remove(EffectContext c, EffectDef e)
        {
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                foreach (CardInstance card in c.Owner.CoolingZone) options.Add(CardOption(card));
                return Request(c, RequestKind.ChooseRemoveFromGame, "移出·获加速", options,
                    allowEmpty: true);
            });
            yield return choice;
            Option pick = Pick(choice);
            // 一张都没选（或移出失败）→ 这一拍到此为止：牌不移出、加速也不给。
            if (pick == null || !c.RemoveCard(pick.Card)) yield break;
            foreach (EffectChoice next in Cooldown(c, new EffectDef(c.Trigger, EffectOp.Haste, e.Arg("count")))) yield return next;
        }
        private static IEnumerable<EffectChoice> CoolHand(EffectContext c, EffectDef e)
        {
            bool single = e.Op == EffectOp.CoolHandForCombo;
            string title = single ? "冷却·换连击" : e.Op == EffectOp.CoolHandForAtk ? "冷却·加力量" : "冷却·获加速";
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                foreach (CardInstance card in c.Owner.Hand) if (card != c.Source) options.Add(CardOption(card));
                // 磁暴 / 充能：一张都不选也是合法答案 —— 玩家可以直接点确认（2026-09-27 用户口径）。
                // 电弧（CoolHandForCombo）是「冷却一张换一次连击」，必须恰好一张，所以不给 allowEmpty；
                // 漩涡（RemoveFromGame）走的是完全独立的一条流程，2026-09-28 起它也给 allowEmpty
                // —— 卡面写的是「<b>可</b>将……」（见 Remove 的注释）。
                return Request(c, RequestKind.ChooseCoolHandCards, title, options,
                    maximum: single ? 1 : options.Count, allowEmpty: !single);
            });
            yield return choice;
            int count = 0;
            foreach (Option option in choice.Selected)
                if (option.Card != null && c.Owner.Hand.Contains(option.Card) && c.CoolCard(option.Card)) count++;
            if (e.Op == EffectOp.CoolHandForAtk) c.AddAttackPower(count * e.Arg("amount"));
            else if (single) { if (count > 0) c.GrantCombo(); }
            else
            {
                // 充能：选完手牌之后才谈得上「总的加速次数」——
                //   = 每冷却一张的收益 × 选了几张
                //   + 本牌那条 Haste 被吸收进来的次数（absorbedHaste，见 BattleEngine.EnqueueEffects）
                // 算完再一次性把加速目标问完，而不是先问加速、再问选牌。
                int total = count * e.Arg("amount") + e.Arg("absorbedHaste");
                foreach (EffectChoice next in Cooldown(c, new EffectDef(c.Trigger, EffectOp.Haste, total))) yield return next;
            }
        }
        private static IEnumerable<EffectChoice> Copy(EffectContext c, EffectDef e)
        {
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                foreach (CardInstance card in c.Owner.CoolingZone)
                    if (card.Def.Cooldown <= e.Arg("threshold") && !card.Def.HasAura) options.Add(CardOption(card));
                return Request(c, RequestKind.ChooseCopyTarget, "复制力量与效果", options);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick != null) c.CopyAttack(pick.Card.Def);
        }
        private static IEnumerable<EffectChoice> Peek(EffectContext c, EffectDef e)
        {
            var choice = new EffectChoice(() => {
                var options = new List<Option>();
                var hand = c.State.Of(c.OpponentSeat).Hand;
                for (int i = 0; i < hand.Count; i++) options.Add(new Option { Kind = OptionKind.ChooseCard,
                    Seat = c.OpponentSeat, Card = hand[i], Value = i, Label = "翻开第 " + (i + 1) + " 张" });
                return Request(c, RequestKind.ChoosePeekCard, "查看对方手牌", options);
            });
            yield return choice;
            Option pick = Pick(choice);
            if (pick == null) yield break;
            bool hit = e.Arg("secondary") == EffectDef.LookAtMost ? pick.Card.Def.Power <= e.Arg("threshold") : pick.Card.Def.Power >= e.Arg("threshold");
            bool cooled = hit && c.CoolCard(pick.Card, e.Arg("cooldownAdjustment"));
            c.Emit(new HandRevealedEvent { ViewerSeat = c.Seat, OwnerSeat = c.OpponentSeat, Card = pick.Card, Cooled = cooled, SlotIndex = pick.Value });
        }
    }
}
