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
        private static int _nextFixtureUid;
        internal readonly System.Collections.Generic.List<AuraToken> ActiveAuras = new System.Collections.Generic.List<AuraToken>();
        private int _nextAuraId;

        /// <summary>实例唯一编号（自测与事件流里便于追踪）。</summary>
        public readonly int Uid;

        /// <summary>静态定义。</summary>
        public readonly CardDef Def;

        /// <summary>所有者座位（0 = 玩家 · 1 = AI）。</summary>
        public readonly int OwnerSeat;

        internal CardInstance(CardDef def, int ownerSeat) : this(def, ownerSeat, System.Threading.Interlocked.Decrement(ref _nextFixtureUid)) { }

        internal CardInstance(CardDef def, int ownerSeat, int uid)
        {
            Uid = uid;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            OwnerSeat = ownerSeat;
            AuraTokens = def.AuraTokenCount;
            Zone = CardZone.Deck;
        }

        /// <summary>剩余冷却（仅当在冷却区 / 攻防中时有意义）。</summary>
        public int RemainingCooldown { get; internal set; }

        /// <summary>尚未消耗的光环指示物数（双光环 = 2）。</summary>
        public int AuraTokens
        {
            get { return ActiveAuras.Count; }
            internal set
            {
                ActiveAuras.Clear();
                var effects = AuraResolver.AuraEffectsOf(Def);
                for (int i = Math.Max(0, effects.Count - value); i < effects.Count; i++)
                    ActiveAuras.Add(new AuraToken(_nextAuraId++, effects[i]));
            }
        }
        internal void AddAura(EffectDef effect)
        { ActiveAuras.Add(new AuraToken(_nextAuraId++, effect)); AuraLive = true; }

        /// <summary>光环是否已激活 —— 本牌进入冷却区之后才为 true（免疫只对之后的防御生效）。</summary>
        public bool AuraLive { get; internal set; }

        /// <summary>当前所在区域。</summary>
        public CardZone Zone { get; internal set; }

        /// <summary>本牌当前的有效力量：模仿复制成功后被改写，其余情况等于 <c>Def.Power + BattlePowerBonus</c>。</summary>
        public int EffectivePower { get; internal set; }

        /// <summary>
        /// 卡面上该写的<b>力量文本</b>：模仿（力量显示为 X）恒为 <c>"X"</c>，
        /// 其余取<b>当前有效力量</b>。
        ///
        /// <para><b>为什么需要它、而不是直接用 <see cref="CardDef.PowerText"/></b>：
        /// 卡表那份是<b>静态</b>的，而水之形（aq）的 <see cref="BattlePowerBonus"/> 会让
        /// 「这张牌现在的力量」≥ 卡表值 —— 拿静态值画卡面，玩家的牌长到 3 了、
        /// 卡上还写着 1，整条效果在界面上是隐形的。快照与出牌提示都读这一个（2026-10-01）。</para>
        /// </summary>
        public string PowerLabelText
        {
            get { return Def.HiddenPower ? Def.PowerText : EffectivePower.ToString(); }
        }

        /// <summary>
        /// 本牌「基础力量」的<b>持久成长</b>（水之形 aq 的 α / β 效果写入）。
        ///
        /// <para><b>为什么需要一个独立字段，而不是直接改 <see cref="EffectivePower"/></b>：
        /// 有效力量是会被反复重算的（进冷却区 / 回手 / 回牌池都重算一次，见
        /// <see cref="ResetEffectivePower"/>），光改它的话成长会在下一次重算时被抹掉 ——
        /// 表现是「第一次进攻明明加了 2，回手又变回 1」，不报任何错。</para>
        ///
        /// <para><b>只在<b>本场战斗</b>内有效</b>（用户 2026-10-01 口径「仅针对于本场战斗」）：
        /// 它挂在<b>卡牌实例</b>上，而实例的生命周期就是这一局 —— 所以「本场战斗内有效」
        /// 是天然成立的，不需要额外的清理点。回牌池（换牌）时才清零（那张实例已被丢弃）。</para>
        /// </summary>
        public int BattlePowerBonus { get; internal set; }

        /// <summary>
        /// 按 <c>Def.Power + BattlePowerBonus</c> 重算有效力量。
        ///
        /// <para><b>所有「重置有效力量」的地方都必须走这里</b>（<see cref="ToHand"/> /
        /// <see cref="ToPool"/> / <c>CooldownOps.PutIntoCooldown</c>）—— 写成
        /// <c>EffectivePower = Def.Power</c> 就会静默吃掉成长。</para>
        /// </summary>
        internal void ResetEffectivePower()
        {
            EffectivePower = Def.Power + BattlePowerBonus;
        }

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
            // ⚠ 不能写成 EffectivePower = Def.Power —— 那会把水之形（aq）的
            //   BattlePowerBonus 成长静默吃掉（见 ResetEffectivePower）。
            ResetEffectivePower();
        }

        /// <summary>回到牌池（替换时用）。成长清零 —— 这张实例已被丢弃，重新抽到的是新实例。</summary>
        internal void ToPool()
        {
            Zone = CardZone.Deck;
            RemainingCooldown = 0;
            AuraTokens = 0;
            AuraLive = false;
            BattlePowerBonus = 0;
            ResetEffectivePower();
        }

        public override string ToString()
        {
            return "#" + Uid + " " + Def.Id + Def.Name + "(CD" + RemainingCooldown + ")";
        }
    }

    internal sealed class AuraToken
    {
        public readonly int Id;
        public readonly EffectDef Definition;
        public AuraToken(int id, EffectDef definition) { Id = id; Definition = definition; }
    }
}
