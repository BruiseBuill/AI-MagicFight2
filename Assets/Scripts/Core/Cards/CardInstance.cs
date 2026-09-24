using System;

namespace MagicBrawl.Core
{
    /// <summary>卡牌实例所处的区域。</summary>
    public enum CardZone
    {
        /// <summary>牌池（尚未发出）。</summary>
        Deck = 0,

        /// <summary>手牌。</summary>
        Hand = 1,

        /// <summary>冷却区。</summary>
        Cooling = 2,

        /// <summary>本半场正在攻防中的牌（尚未进入冷却区）。</summary>
        InPlay = 3,

        /// <summary>已永久移出游戏（漩涡）。</summary>
        Removed = 4,
    }

    /// <summary>
    /// 对局中的卡牌实例（`Docs/engineering/04-架构与接口.md` §2）。
    ///
    /// 注意：<see cref="RemainingCooldown"/> / <see cref="AuraTokens"/> 等运行期字段
    /// 一律 <c>internal set</c> —— <see cref="CooldownOps"/> 是唯一被允许改冷却的入口，
    /// 表现层（MagicBrawl.App）连编译期都写不动它们。
    /// </summary>
    public sealed class CardInstance
    {
        private static int _nextUid = 1;

        /// <summary>实例唯一编号（自测与事件流里便于追踪）。</summary>
        public readonly int Uid;

        /// <summary>静态定义。</summary>
        public readonly CardDef Def;

        /// <summary>所有者座位（0 = 玩家 · 1 = AI）。</summary>
        public readonly int OwnerSeat;

        internal CardInstance(CardDef def, int ownerSeat)
        {
            Uid = _nextUid++;
            Def = def;
            OwnerSeat = ownerSeat;
            AuraTokens = def.AuraTokenCount;
            Zone = CardZone.Deck;
        }

        /// <summary>剩余冷却（仅当在冷却区 / 攻防中时有意义）。</summary>
        public int RemainingCooldown { get; internal set; }

        /// <summary>尚未消耗的光环指示物数（双光环 = 2）。</summary>
        public int AuraTokens { get; internal set; }

        /// <summary>光环是否已激活 —— 本牌进入冷却区之后才为 true（免疫只对之后的防御生效）。</summary>
        public bool AuraLive { get; internal set; }

        /// <summary>当前所在区域。</summary>
        public CardZone Zone { get; internal set; }

        /// <summary>本牌当前的有效力量：模仿复制成功后被改写，其余情况等于 <see cref="CardDef.Power"/>。</summary>
        public int EffectivePower { get; internal set; }

        /// <summary>是否已被永久移出游戏。</summary>
        public bool RemovedFromGame
        {
            get { return Zone == CardZone.Removed; }
        }

        /// <summary>冷却区中的牌是否可用（未移出、确有冷却）。</summary>
        public bool IsCooling
        {
            get { return Zone == CardZone.Cooling; }
        }

        /// <summary>本牌当前是否有可用光环指示物（需已激活且还有余量）。</summary>
        public bool HasUsableAura
        {
            get { return AuraLive && AuraTokens > 0; }
        }

        /// <summary>
        /// 回到手牌。手牌里不保留任何光环状态 —— 光环是「作为进攻牌打出后」才获得的
        /// （`Docs/rules/01-规则基线.md` §6.1），且回手时未使用的指示物全部作废（§6.4）。
        /// </summary>
        internal void ToHand()
        {
            Zone = CardZone.Hand;
            RemainingCooldown = 0;
            AuraTokens = 0;
            AuraLive = false;
            EffectivePower = Def.Power;
        }

        /// <summary>回到牌池（替换时用）。</summary>
        internal void ToPool()
        {
            Zone = CardZone.Deck;
            RemainingCooldown = 0;
            AuraTokens = 0;
            AuraLive = false;
            EffectivePower = Def.Power;
        }

        public override string ToString()
        {
            return "#" + Uid + " " + Def.Id + Def.Name + "(CD" + RemainingCooldown + ")";
        }
    }
}
