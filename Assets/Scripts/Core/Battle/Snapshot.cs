using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 一枚<strong>未消耗的光环指示物</strong>的只读快照（M15）。
    ///
    /// <para><b>为什么需要它</b>：HudBuff 区要「一枚指示物一个图标」，而
    /// <see cref="CardSnapshot.AuraTokens"/> 只是个数字 —— UI 无法知道这几枚分别是哪种光环。
    /// 枚举顺序与 <see cref="AuraResolver"/> 完全一致（剩余指示物 = 效果列表的尾部），
    /// 所以「第 k 个图标」与「第 k 个可选项」是同一枚指示物。</para>
    /// </summary>
    public struct AuraTokenSnapshot
    {
        /// <summary>光环类型。</summary>
        public AuraKind Kind;

        /// <summary>数值（加值 / 阈值，随 <see cref="Kind"/> 而异）。</summary>
        public int Value;

        /// <summary>卡表里的光环说明原文（如「光环：防御力量 +2」），供长按浮层显示。</summary>
        public string Text;

        /// <summary>提供这枚指示物的卡名。</summary>
        public string CardName;
    }

    /// <summary>
    /// 卡牌的<strong>只读快照</strong> —— 表现层唯一能看到的东西。
    ///
    /// 为什么不直接把 <see cref="CardInstance"/> 交给 UI：铁律第 2 条「依赖只能向下，
    /// 表现层不得反向调用引擎」。快照是值类型、构造后不可变，UI 拿不到任何写入口，
    /// 也不可能顺手改坏对局状态。顺带还让 UI 的 Prefab 预览可以不依赖引擎就跑起来。
    /// </summary>
    public struct CardSnapshot
    {
        /// <summary>卡 ID（a–an），用于查卡面美术。</summary>
        public string CardId;

        /// <summary>卡名。</summary>
        public string Name;

        /// <summary>有效力量（模仿复制成功后会被改写）。</summary>
        public int Power;

        /// <summary>卡面显示的力量文本：模仿是 "X"，其余是数字。</summary>
        public string PowerText;

        /// <summary>基础冷却值。</summary>
        public int BaseCooldown;

        /// <summary>剩余冷却（仅当在冷却区时有意义；手牌恒为 0）。</summary>
        public int RemainingCooldown;

        /// <summary>尚未消耗的光环指示物数。</summary>
        public int AuraTokens;

        /// <summary>卡面上一共有几枚光环指示物（0 = 这张牌不带光环）。</summary>
        public int AuraTokenMax;

        /// <summary>
        /// 剩余指示物的<strong>明细</strong>（长度 = <see cref="AuraTokens"/>；无光环时为空数组）。
        /// HudBuff 区按它一枚一枚地建图标。
        /// </summary>
        public AuraTokenSnapshot[] AuraTokensDetail;

        /// <summary>光环是否已激活（进入冷却区之后才为真）。</summary>
        public bool AuraLive;

        /// <summary>是否在冷却区。</summary>
        public bool InCoolingZone;

        /// <summary>实例编号（UI 做动画时用来配对）。</summary>
        public int Uid;

        /// <summary>归属座位。</summary>
        public int OwnerSeat;

        /// <summary>这张牌是否带光环（卡面有 α 光环）。</summary>
        public bool HasAura
        {
            get { return AuraTokenMax > 0; }
        }

        /// <summary>无光环时共用的空明细（省掉每次快照都新建一个空数组）。</summary>
        private static readonly AuraTokenSnapshot[] EmptyAuras = new AuraTokenSnapshot[0];

        public static CardSnapshot From(CardInstance card)
        {
            if (card == null)
            {
                return default(CardSnapshot);
            }

            return new CardSnapshot
            {
                CardId = card.Def.Id,
                Name = card.Def.Name,
                Power = card.EffectivePower,
                PowerText = card.Def.PowerText,
                BaseCooldown = card.Def.Cooldown,
                RemainingCooldown = card.RemainingCooldown,
                AuraTokens = card.AuraTokens,
                AuraTokenMax = card.Def.AuraTokenCount,
                AuraTokensDetail = AuraDetail(card),
                AuraLive = card.AuraLive,
                InCoolingZone = card.Zone == CardZone.Cooling,
                Uid = card.Uid,
                OwnerSeat = card.OwnerSeat,
            };
        }

        /// <summary>
        /// 剩余指示物的明细。口径与 <see cref="AuraResolver.BuildOptions"/> 一致：
        /// 已消耗的从效果列表<b>头部</b>扣，所以剩余的那几枚就是列表尾部的这几条。
        /// </summary>
        private static AuraTokenSnapshot[] AuraDetail(CardInstance card)
        {
            int remaining = card.AuraTokens;
            if (remaining <= 0)
            {
                return EmptyAuras;
            }

            List<EffectDef> all = AuraResolver.AuraEffectsOf(card.Def);
            if (all.Count == 0)
            {
                return EmptyAuras;
            }

            int start = System.Math.Max(0, all.Count - remaining);
            var list = new List<AuraTokenSnapshot>(all.Count - start);
            for (int e = start; e < all.Count; e++)
            {
                list.Add(new AuraTokenSnapshot
                {
                    Kind = all[e].Aura,
                    Value = all[e].A,
                    Text = all[e].Text,
                    CardName = card.Def.Name,
                });
            }

            return list.ToArray();
        }

        /// <summary>只给 UI 预览用的便捷构造（取卡表静态数值，不涉及对局状态）。</summary>
        public static CardSnapshot FromDef(CardDef def, int ownerSeat, int remainingCooldown)
        {
            return new CardSnapshot
            {
                CardId = def.Id,
                Name = def.Name,
                Power = def.Power,
                PowerText = def.PowerText,
                BaseCooldown = def.Cooldown,
                RemainingCooldown = remainingCooldown,
                AuraTokens = 0,
                AuraTokenMax = def.AuraTokenCount,
                AuraTokensDetail = EmptyAuras,
                AuraLive = false,
                InCoolingZone = remainingCooldown > 0,
                Uid = 0,
                OwnerSeat = ownerSeat,
            };
        }
    }

    /// <summary>玩家的只读快照。</summary>
    public struct PlayerSnapshot
    {
        public int Seat;
        public string Name;
        public int Hp;
        public int MaxHp;
        public bool IsAi;

        /// <summary>手牌张数（对方只给数量，不给内容）。</summary>
        public int HandCount;

        /// <summary>冷却区中未使用的光环指示物总数。</summary>
        public int AuraTokensReady;

        public bool IsDead
        {
            get { return Hp <= 0 || MaxHp <= 0; }
        }

        public static PlayerSnapshot From(PlayerState p)
        {
            if (p == null)
            {
                return default(PlayerSnapshot);
            }

            int aura = 0;
            for (int i = 0; i < p.CoolingZone.Count; i++)
            {
                aura += p.CoolingZone[i].AuraTokens;
            }

            return new PlayerSnapshot
            {
                Seat = p.Seat,
                Name = p.Name,
                Hp = p.Hp,
                MaxHp = p.MaxHp,
                IsAi = p.IsAi,
                HandCount = p.Hand.Count,
                AuraTokensReady = aura,
            };
        }
    }

    /// <summary>对局层面的只读快照（回合数 / 进攻方 / 终局信息）。</summary>
    public struct BattleSnapshot
    {
        public int TurnNumber;
        public int AttackerSeat;
        public bool IsOver;
        public int WinnerSeat;
        public string EndReason;
        public bool InCombo;

        public static BattleSnapshot From(BattleState s)
        {
            if (s == null)
            {
                return default(BattleSnapshot);
            }

            return new BattleSnapshot
            {
                TurnNumber = s.TurnNumber,
                AttackerSeat = s.AttackerSeat,
                IsOver = s.IsOver,
                WinnerSeat = s.WinnerSeat,
                EndReason = s.EndReason,
                InCombo = s.InCombo,
            };
        }
    }

    /// <summary>
    /// 决策请求的<strong>只读快照</strong>。UI 渲染选项 / 提示文案 / 上下文（需 ≥N 点力量、是否双发…）
    /// 全都从这里读，不需要（也不允许）自己去推算规则。
    /// </summary>
    public struct DecisionSnapshot
    {
        public int Seat;
        public RequestKind Kind;
        public string Prompt;

        /// <summary>短标题（弹窗顶部大字用）；引擎不填时 UI 回落到 <see cref="Prompt"/>。见 <see cref="DecisionRequest.Title"/>。</summary>
        public string Title;

        public int MinSelect;
        public int MaxSelect;
        public int ContextPower;
        public int ContextDefenseBonus;
        public bool ContextDouble;
        public bool ContextImmune;
        public bool ContextNoSkip;

        /// <summary>
        /// 本拍效果的极性（同 <see cref="DecisionRequest.ContextHaste"/>）：
        /// true = 冷却前进（加速 / 立即冷却完成），false = 冷却倒退（减速 / 重置冷却）。
        /// </summary>
        public bool ContextHaste;

        /// <summary>选项的只读副本。</summary>
        public System.Collections.Generic.IReadOnlyList<Option> Options;

        public static DecisionSnapshot From(DecisionRequest r)
        {
            if (r == null)
            {
                return default(DecisionSnapshot);
            }

            return new DecisionSnapshot
            {
                Seat = r.Seat,
                Kind = r.Kind,
                Prompt = r.Prompt,
                Title = r.Title,
                MinSelect = r.MinSelect,
                MaxSelect = r.MaxSelect,
                ContextPower = r.ContextPower,
                ContextDefenseBonus = r.ContextDefenseBonus,
                ContextDouble = r.ContextDouble,
                ContextImmune = r.ContextImmune,
                ContextNoSkip = r.ContextNoSkip,
                ContextHaste = r.ContextHaste,
                Options = r.Options,
            };
        }
    }
}
