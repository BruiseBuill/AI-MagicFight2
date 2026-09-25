using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>光环可用的场合。</summary>
    public enum AuraContext
    {
        Attack = 0,
        Defend = 1,
    }

    /// <summary>
    /// 光环解析（规则 §6）。光环 = 附着在提供它的那张牌上的一次性指示物，
    /// 牌进入冷却区后才激活，冷却完毕回手时未使用的作废。
    ///
    /// 注意：40 张卡里带双光环的（af / aj）两张指示物<strong>完全相同</strong>，
    /// 所以用「剩余数量」建模就够了，不需要逐个指示物记类型。
    /// </summary>
    public static class AuraResolver
    {
        /// <summary>某种光环是否能在该场合使用（规则 §6.3）。</summary>
        public static bool UsableIn(AuraKind kind, AuraContext ctx)
        {
            switch (kind)
            {
                case AuraKind.AtkPower:
                case AuraKind.Combo:
                    return ctx == AuraContext.Attack;

                case AuraKind.DefPower:
                case AuraKind.ImmuneHigh:
                case AuraKind.ImmuneLow:
                    return ctx == AuraContext.Defend;

                case AuraKind.AtkOrDefPower:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>取一张牌定义中的全部光环效果（按卡面顺序）。</summary>
        public static List<EffectDef> AuraEffectsOf(CardDef def)
        {
            var list = new List<EffectDef>();
            if (def == null)
            {
                return list;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (def.Effects[i].Op == EffectOp.Aura)
                {
                    list.Add(def.Effects[i]);
                }
            }

            return list;
        }

        /// <summary>
        /// 枚举该玩家当前可消耗的光环指示物，每个指示物一个选项。
        /// 已消耗的指示物从卡面效果列表的<strong>前面</strong>开始算，双光环同型时无差别。
        /// </summary>
        public static List<Option> BuildOptions(PlayerState owner, AuraContext ctx)
        {
            var options = new List<Option>();
            if (owner == null)
            {
                return options;
            }

            for (int i = 0; i < owner.CoolingZone.Count; i++)
            {
                CardInstance src = owner.CoolingZone[i];
                if (!src.HasUsableAura)
                {
                    continue;
                }

                for (int e = 0; e < src.ActiveAuras.Count; e++)
                {
                    AuraToken token = src.ActiveAuras[e];
                    EffectDef ef = token.Definition;
                    if (!UsableIn(ef.Aura, ctx))
                    {
                        continue;
                    }

                    options.Add(new Option
                    {
                        Kind = OptionKind.UseAura,
                        Seat = owner.Seat,
                        Card = src,
                        AuraSource = src,
                        AuraKind = ef.Aura,
                        AuraTokenIndex = token.Id,
                        Value = ef.A,
                        Label = AuraLabel(src, ef),
                    });
                }
            }

            for (int i = 0; i < options.Count; i++)
            {
                options[i].Index = i;
            }

            return options;
        }

        private static string AuraLabel(CardInstance src, EffectDef ef)
        {
            return src.Def.Name + " 光环：" + DescribeEffect(ef.Aura, ef.A);
        }

        /// <summary>
        /// 光环的效果描述（不含来源卡名）。UI 的 HudBuff 长按浮层与决策选项**共用这一份措辞** ——
        /// 两处各写一遍迟早会说两件不同的事。
        /// </summary>
        public static string DescribeEffect(AuraKind kind, int value)
        {
            switch (kind)
            {
                case AuraKind.AtkPower:
                    return "进攻力量 +" + value;

                case AuraKind.DefPower:
                    return "防御力量 +" + value;

                case AuraKind.Combo:
                    return "本次进攻获得连击（基础力量 ≤" + value + "）";

                case AuraKind.ImmuneHigh:
                    return "免疫力量 ≥" + value + " 的攻击（含双发）";

                case AuraKind.ImmuneLow:
                    return "免疫力量 ≤" + value + " 的攻击（含双发）";

                case AuraKind.AtkOrDefPower:
                    return "进攻力量 +" + value + " 或防御力量 +" + value;

                default:
                    return "无效果";
            }
        }

        /// <summary>该光环类型是否「只是加数值」（加值类，无分支语义）。</summary>
        public static bool IsPowerBonus(AuraKind kind)
        {
            return kind == AuraKind.AtkPower || kind == AuraKind.DefPower
                   || kind == AuraKind.AtkOrDefPower;
        }

        /// <summary>
        /// 该光环是否「免疫类」。
        ///
        /// <para>UI 靠它决定拖到判定区之后的行为：免疫 = 当场消耗结算（它不需要牌），
        /// 其余（力量加值 / 连击）= 进入判定区左侧的「准备使用」状态，随出牌一并提交。</para>
        /// </summary>
        public static bool IsImmune(AuraKind kind)
        {
            return kind == AuraKind.ImmuneHigh || kind == AuraKind.ImmuneLow;
        }

        /// <summary>
        /// 找出能免疫指定攻击力量的免疫光环 —— 返回来源牌、该指示物在剩余指示物里的序号、以及类型。
        /// 没有可用免疫光环时返回 false。
        /// </summary>
        public static bool TryFindImmune(PlayerState defender, int attackPower,
            out CardInstance source, out int tokenIndex, out AuraKind kind)
        {
            source = null;
            tokenIndex = -1;
            kind = AuraKind.None;

            if (defender == null)
            {
                return false;
            }

            for (int i = 0; i < defender.CoolingZone.Count; i++)
            {
                CardInstance src = defender.CoolingZone[i];
                if (!src.HasUsableAura)
                {
                    continue;
                }

                for (int e = 0; e < src.ActiveAuras.Count; e++)
                {
                    AuraToken token = src.ActiveAuras[e];
                    EffectDef ef = token.Definition;
                    if (ef.Aura == AuraKind.ImmuneHigh && attackPower >= ef.A)
                    {
                        source = src;
                        tokenIndex = token.Id;
                        kind = AuraKind.ImmuneHigh;
                        return true;
                    }

                    if (ef.Aura == AuraKind.ImmuneLow && attackPower <= ef.A)
                    {
                        source = src;
                        tokenIndex = token.Id;
                        kind = AuraKind.ImmuneLow;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>找出能免疫指定攻击力量的免疫光环（返回指示物来源；无则 null）。</summary>
        public static CardInstance FindImmuneSource(PlayerState defender, int attackPower)
        {
            CardInstance src;
            int index;
            AuraKind kind;
            return TryFindImmune(defender, attackPower, out src, out index, out kind) ? src : null;
        }

        /// <summary>真正的消耗动作：指示物 −1（光环来源牌仍在冷却区，不影响其冷却）。</summary>
        internal static bool Consume(CardInstance src, int tokenId = -1)
        {
            if (src == null || src.ActiveAuras.Count == 0) return false;
            int index = tokenId < 0 ? 0 : src.ActiveAuras.FindIndex(t => t.Id == tokenId);
            if (index < 0) return false;
            src.ActiveAuras.RemoveAt(index);
            return true;
        }
    }
}
