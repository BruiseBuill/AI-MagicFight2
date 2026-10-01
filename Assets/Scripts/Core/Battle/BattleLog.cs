using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 结构化事件流（`Docs/engineering/04-架构与接口.md` §5）。
    /// M4 只负责发事件，完全不知道 UI 存在；M6 BattleDriver 订阅后切成动画。
    /// </summary>
    public abstract class BattleEvent
    {
        /// <summary>事件序号（自增，便于排序与去重）。</summary>
        public int Seq;

        public int TurnNumber;

        public abstract string Describe();
    }

    /// <summary>半场开始（已触发冷却 −1）。</summary>
    public sealed class TurnStartedEvent : BattleEvent
    {
        public int Seat;
        public bool IsComboFollowUp;

        public override string Describe()
        {
            return "── 回合" + TurnNumber + " " + (IsComboFollowUp ? "连击追加" : "半场开始") + "：seat" + Seat + " 进攻";
        }
    }

    /// <summary>进攻方打出某张牌 —— 在选牌之后、结算之前发出。</summary>
    public sealed class AttackDeclaredEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Card;

        public override string Describe()
        {
            return "进攻 seat" + Seat + " 打出 " + Card.Def.Name + "（基础力量 " + Card.Def.PowerText
                   + " · 冷却 " + Card.Def.Cooldown + "）";
        }
    }

    /// <summary>进攻力量已结算完（第 ① 步结束）—— 防御方需要面对的就是这个数值。</summary>
    public sealed class AttackPowerResolvedEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Card;
        public int BasePower;
        public int BonusPower;
        public bool IsDouble;

        /// <summary>
        /// 本半场进攻方身上的虚弱层数（0 = 没被削）—— 2026-09-29。
        ///
        /// <para><b>为什么必须带上它</b>：这个事件的 <see cref="FinalPower"/> 会被
        /// 战斗界面直接印成「进攻力量 N · 需要 ≥N 才能挡住」。折扣若只发生在引擎内部、
        /// 事件里不体现，玩家看到的就是「说好 7，其实只打 4」——
        /// 所以力量和它的折扣必须在同一条事件里。</para>
        /// </summary>
        public int WeakenStacks;

        /// <summary>最终进攻力量 =（基础 + 增量）再按虚弱折算（有层数则减半、向上取整）。</summary>
        public int FinalPower
        {
            get { return PlayerState.Weakened(BasePower + BonusPower, WeakenStacks); }
        }

        public override string Describe()
        {
            return "    → 最终进攻力量 " + FinalPower
                   + (BonusPower != 0 ? "（基础 " + BasePower + " " + (BonusPower > 0 ? "+" : "") + BonusPower + "）" : string.Empty)
                   + (WeakenStacks > 0 ? " [虚弱 ×" + WeakenStacks + "，力量折半]" : string.Empty)
                   + (IsDouble ? " [双发]" : string.Empty);
        }
    }

    /// <summary>消耗了一枚光环指示物。</summary>
    public sealed class AuraConsumedEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Source;
        public AuraKind Kind;
        public int Value;
        public CardInstance TargetCard;

        public override string Describe()
        {
            return "光环 seat" + Seat + " " + Source.Def.Name + " → " + Kind + " +" + Value
                   + " 作用于 " + (TargetCard != null ? TargetCard.Def.Name : "—");
        }
    }

    /// <summary>光环激活：牌进入冷却区后获得指示物（规则 §6.1）。</summary>
    public sealed class AuraActivatedEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Card;
        public int Tokens;

        public override string Describe()
        {
            return "光环激活 seat" + Seat + " " + Card.Def.Name + " 获得 " + Tokens + " 枚指示物";
        }
    }

    /// <summary>防御结果。</summary>
    public sealed class DefenseResolvedEvent : BattleEvent
    {
        public int DefenderSeat;
        public int AttackPower;
        public bool Success;
        public bool GaveUp;
        public bool UsedImmune;
        public List<CardInstance> Cards = new List<CardInstance>();

        public override string Describe()
        {
            if (Success)
            {
                var names = new List<string>();
                for (int i = 0; i < Cards.Count; i++)
                {
                    names.Add(Cards[i].Def.Name);
                }

                return "防御 seat" + DefenderSeat + " 成功"
                       + (UsedImmune ? "（免疫光环）" : "（" + string.Join("＋", names.ToArray()) + "）");
            }

            return "防御 seat" + DefenderSeat + (GaveUp ? " 主动放弃" : " 无可挡之牌")
                   + "（需 ≥" + AttackPower + "）→ 掉 1 点生命";
        }
    }

    /// <summary>掉血。</summary>
    public sealed class DamageTakenEvent : BattleEvent
    {
        public int Seat;
        public int Amount;
        public string Source;

        public override string Describe()
        {
            return "伤害 seat" + Seat + " −" + Amount + "（" + Source + "）";
        }
    }

    /// <summary>生命上限变化。</summary>
    public sealed class HpMaxChangedEvent : BattleEvent
    {
        public int Seat;
        public int From;
        public int To;

        public override string Describe()
        {
            return "生命上限 seat" + Seat + " " + From + " → " + To;
        }
    }

    /// <summary>回血。</summary>
    public sealed class HealEvent : BattleEvent
    {
        public int Seat;
        public int Amount;
        public int Actual;

        public override string Describe()
        {
            return "回血 seat" + Seat + " 请求 +" + Amount + "，实际 +" + Actual;
        }
    }

    /// <summary>某张牌剩余冷却变化。</summary>
    public sealed class CooldownChangedEvent : BattleEvent
    {
        public CooldownChange Change;

        public override string Describe()
        {
            return "冷却 " + Change;
        }
    }

    /// <summary>补牌 / 回手。</summary>
    public sealed class CardDrawnEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Card;
        public string Reason;

        public override string Describe()
        {
            return "seat" + Seat + " 获得 " + Card.Def.Name + "（" + Reason + "）";
        }
    }

    /// <summary>回手。</summary>
    public sealed class CardReturnedEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Card;

        public override string Describe()
        {
            return "seat" + Seat + " " + Card.Def.Name + " 冷却完毕回手";
        }
    }

    /// <summary>查看对方手牌。</summary>
    public sealed class HandRevealedEvent : BattleEvent
    {
        public int ViewerSeat;
        public int OwnerSeat;
        public CardInstance Card;
        public bool Cooled;

        /// <summary>
        /// 被翻开的这张牌当时在<strong>拥有者手牌里的下标</strong>（0 起；−1 = 未知）。
        ///
        /// <para><b>为什么需要它</b>：2026-09-21 起这次查看由玩家在「一排牌背」里自己点一张
        /// （<see cref="RequestKind.ChoosePeekCard"/>）。那张牌点下去之后 UI 才开始翻面，
        /// 而翻面时要绑的正面卡面只有本事件才拿得到 —— UI 必须知道「翻的是我点的第几格」。
        /// 用下标而不是对象引用：表现层不该为了做一次配对去持有 <see cref="CardInstance"/>。</para>
        /// </summary>
        public int SlotIndex = -1;

        public override string Describe()
        {
            return "seat" + ViewerSeat + " 查看 seat" + OwnerSeat + " 的 " + Card.Def.Name
                   + "（力量 " + Card.Def.PowerText + "）" + (Cooled ? " → 送入冷却" : " → 无效果");
        }
    }

    /// <summary>永久移出游戏。</summary>
    public sealed class CardRemovedEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Card;

        public override string Describe()
        {
            return "seat" + Seat + " " + Card.Def.Name + " 永久移出游戏";
        }
    }

    /// <summary>替换手牌。</summary>
    public sealed class ReplaceEvent : BattleEvent
    {
        public int Seat;
        public CardInstance Returned;
        public CardInstance Gained;

        public override string Describe()
        {
            return "seat" + Seat + " 替换 " + Returned.Def.Name + " → " + Gained.Def.Name;
        }
    }

    /// <summary>被施加虚弱（毒刺 ao）—— 2026-09-29 新增。</summary>
    public sealed class WeakenAppliedEvent : BattleEvent
    {
        /// <summary>被施加者（= 本次进攻的目标）。</summary>
        public int Seat;

        /// <summary>本次施加的层数。</summary>
        public int Amount;

        /// <summary>施加<b>之后</b>的总层数。</summary>
        public int Stacks;

        public override string Describe()
        {
            return "虚弱 seat" + Seat + " +" + Amount + " 层（共 " + Stacks + " 层）";
        }
    }

    /// <summary>
    /// 虚弱消耗一层 —— 发生在拥有者<b>自己的进攻半场结束之后</b>（2026-09-29）。
    ///
    /// <para>连击的追加进攻算同一个半场，整条链只减 1 层。</para>
    /// </summary>
    public sealed class WeakenDecayedEvent : BattleEvent
    {
        public int Seat;

        /// <summary>消耗之后剩余的层数。</summary>
        public int Stacks;

        public override string Describe()
        {
            return "虚弱 seat" + Seat + " 进攻结束 → 余 " + Stacks + " 层";
        }
    }

    /// <summary>
    /// 某张牌的<b>基础力量发生持久成长</b>（水之形 aq 的 α / β）—— 2026-10-01 新增。
    ///
    /// <para>成长落在<b>卡牌实例</b>上（<see cref="CardInstance.BattlePowerBonus"/>），
    /// 本场战斗内一直有效。它不改变本次攻防已经结算完的力量
    /// （<see cref="AttackPowerResolvedEvent"/> 报的仍是成长前的数），
    /// 所以这条事件是「从下一拍开始，这张牌更强了」的凭据。</para>
    /// </summary>
    public sealed class BasePowerGrownEvent : BattleEvent
    {
        /// <summary>成长发生在谁的牌上。</summary>
        public int Seat;

        /// <summary>发生成长的牌。</summary>
        public CardInstance Card;

        /// <summary>本次增量。</summary>
        public int Amount;

        /// <summary>累计成长（本场战斗内）。</summary>
        public int BattlePowerBonus;

        /// <summary>成长后的有效力量。</summary>
        public int Power;

        public override string Describe()
        {
            return "成长 seat" + Seat + " " + Card.Def.Name + " 基础力量 +" + Amount
                   + "（累计 +" + BattlePowerBonus + " → 力量 " + Power + "）";
        }
    }

    /// <summary>胜负已分。</summary>
    public sealed class GameOverEvent : BattleEvent
    {
        public int WinnerSeat;
        public string Reason;

        public override string Describe()
        {
            return "🏁 对局结束：seat" + WinnerSeat + " 胜（" + Reason + "）";
        }
    }
}
