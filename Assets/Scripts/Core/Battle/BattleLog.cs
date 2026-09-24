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

        public int FinalPower
        {
            get { return BasePower + BonusPower; }
        }

        public override string Describe()
        {
            return "    → 最终进攻力量 " + FinalPower
                   + (BonusPower != 0 ? "（基础 " + BasePower + " " + (BonusPower > 0 ? "+" : "") + BonusPower + "）" : string.Empty)
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
