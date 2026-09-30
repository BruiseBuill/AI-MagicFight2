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
                // 2026-09-29 · 击穿（ap）：这枚指示物赐予的是「本次打出的牌获得快速回填」，
                //   作用对象与连击一样是「当场打出的那张牌」，所以同侧 —— 只有进攻场合可用。
                case AuraKind.QuickRefill:
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

        /// <summary>
        /// 这张卡面是否<b>自带连击</b>（α 效果里有 <see cref="EffectOp.Combo"/>）。
        ///
        /// <para>读卡表而不是读 <c>AttackContext.HasCombo</c>：卡面自带的那条连击是在
        /// <c>EffectStage.Power</c>（结算第 ① 步）才写进去的，而「准备的光环要不要收」这件事
        /// 发生在 <c>DoChooseAttackCard</c> 里 —— 那时第 ① 步还没跑，<c>HasCombo</c> 必然是 false。
        /// 要判「这张牌本来就会给连击吗」只能问卡表。</para>
        /// </summary>
        public static bool CardGrantsCombo(CardDef card)
        {
            if (card == null)
            {
                return false;
            }

            for (int i = 0; i < card.Effects.Count; i++)
            {
                EffectDef e = card.Effects[i];
                if (e.Op == EffectOp.Combo && e.Trigger == EffectTrigger.Attack)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 挑出「准备使用、但最终<b>不会消耗</b>」的那几枚光环（返回在 <paramref name="auras"/>
        /// 里的下标，升序）。
        ///
        /// <para><b>为什么要有这个函数</b>：同一件事有两个消费者，两边必须永远一致 ——</para>
        /// <list type="bullet">
        /// <item><b>引擎</b>（<c>BattleEngine.ApplyPreparedAttackAuras</c>）：这几枚<b>不生效、也不消耗</b>；</item>
        /// <item><b>界面</b>（<c>BattleUi.Pick</c>）：这几枚要按「玩家最终取消了使用」处理 ——
        /// 出牌之后那枚图标<b>从准备位滑回默认位</b>，而不是「原地消失又在原位冒出来」。</item>
        /// </list>
        /// <para>各写一份判据迟早会分叉（一边退回、一边消耗 = 玩家看见图标回去了、指示物却没了），
        /// 所以判据只留在这里，两处都调它（铁律 3 的落法：规则事实在 Core 一处）。</para>
        ///
        /// <para><b>判据（三条，都是既有规则）</b>：</para>
        /// <list type="number">
        /// <item><b>沉重打击</b>（卡面带「此法术的进攻力量不能增加」）→ 本次准备的光环<b>全部</b>都不生效
        /// —— 这条在 <c>ApplyPreparedAttackAuras</c> 里原本就是「整批 continue」；</item>
        /// <item><b>连击阈值</b>：连击光环只对「基础力量 ≤ A」的牌有效，超了就无事发生；</item>
        /// <item><b>连击不可叠加</b>（2026-09-30 用户口径）：卡面自带连击、或本次进攻前面已经有一枚
        /// 连击光环真的生效了，后面的连击光环就是纯浪费 —— 按「取消使用」处理。</item>
        /// </list>
        ///
        /// <para>⚠ 只用于<b>进攻</b>那一拍。防御拍走 <c>ConsumeDefenseAuras</c>，它只认
        /// <see cref="IsPowerBonus"/> 那一类，而连击不在其中。</para>
        /// </summary>
        public static void CollectRefunded(List<Option> auras, CardDef card, List<int> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();

            if (auras == null || auras.Count == 0)
            {
                return;
            }

            bool forbidden = card != null && card.ForbidsAtkBuff;
            bool comboGranted = CardGrantsCombo(card);
            int power = card == null ? 0 : card.Power;

            for (int i = 0; i < auras.Count; i++)
            {
                Option o = auras[i];
                if (o == null)
                {
                    continue;
                }

                if (forbidden)
                {
                    // ① 力量不能增加 → 整批都不生效（原来是「整批 continue」，口径不变）
                    into.Add(i);
                    continue;
                }

                if (o.AuraKind != AuraKind.Combo)
                {
                    continue;                       // 其余光环照旧生效
                }

                if (power > o.Value)
                {
                    into.Add(i);                    // ② 连击阈值：基础力量超过 A
                    continue;
                }

                if (comboGranted)
                {
                    into.Add(i);                    // ③ 连击不可叠加
                    continue;
                }

                // 这一枚真的会给连击 → 后面再来的连击光环全部没有意义。
                comboGranted = true;
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

                case AuraKind.QuickRefill:
                    return "本次打出的法术获得快速回填（进入冷却区时剩余冷却额外 −" + value + "）";

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
