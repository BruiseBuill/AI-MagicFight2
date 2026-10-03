using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 卡牌的<strong>角色分类</strong>（2026-10-03 新增）—— 启发式 AI、流派判定与怪物复写
    /// 共用的<strong>唯一一份</strong>「这张牌是干什么的」读卡表口径。
    ///
    /// <para><b>为什么必须收在一处</b>：新 AI（<see cref="HeuristicAgent"/>）与流派判定
    /// （<see cref="BattleArchetype"/>）要反复回答同一批问题 ——「这是连击卡吗」「这是区域减速吗」
    /// 「它自带几点进攻光环」。这些判据如果各写一份，加一张卡时必然漏改一处，
    /// 表现是「流派判定认它是连击、出牌时却不优先」，且完全不报错。</para>
    ///
    /// <para><b>读的是算子不是卡 ID</b>：本批 45 张卡里没有两张的算子组合完全相同，
    /// 所以按 <see cref="EffectOp"/> / <see cref="AuraKind"/> 判足以区分，且加新卡时自动生效
    /// —— 只有「过载算回血不算连击」这种<em>规则口径</em>才需要额外的组合判断（见
    /// <see cref="IsComboCard"/>）。</para>
    ///
    /// <para><b>与卡名的对应关系</b>（便于人工核对，代码里不存在这些字面量）：</para>
    /// <list type="bullet">
    /// <item>进攻光环卡：淬火 ag · 爆燃 ai · 火灾 aj · 烈焰斗篷 ah</item>
    /// <item>防御光环卡：冰封铠甲 af · 冷冻核心 as · 烈焰斗篷 ah（冰风暴 b 自 2026-10-03 起改为「守护」，已不是防御光环卡）</item>
    /// <item>连击卡：闪电 j · 电弧 k · 磁暴 l · 雷鸣 m · 引雷 n · 击穿 ap · 闪电球 ar</item>
    /// <item>双发卡：荆棘 t · 飞叶连击 u · 藤蔓 v · 狂躁蘑菇 w</item>
    /// <item>加速卡：海涌 q · 湍流 ab · 充能 am · 瀑流 y · 闪电球 ar · 烈焰斗篷 ah · 暴风雪 a · 滚石冲击 f</item>
    /// <item>区域加速卡：暴风雪 a · 雷鸣 m · 自燃 p · 海涌 q · 喷泉 s</item>
    /// <item>减速卡：凝固 c · 雷云 ad · 淬火 ag · 冷冻核心 as · 地震 i · 藤蔓 v</item>
    /// <item>区域减速卡：暴风雪 a · 冰风暴 b · 雪崩 an</item>
    /// <item>回血卡：过载 o · 自燃 p</item>
    /// </list>
    /// </summary>
    public static class CardRole
    {
        // ── 通用：α 效果查询 ────────────────────────────────────

        /// <summary>卡面是否有某个 <b>α（进攻时）</b>算子。</summary>
        public static bool HasAttackOp(CardDef def, EffectOp op)
        {
            return HasOp(def, op, EffectTrigger.Attack);
        }

        /// <summary>卡面是否有某个算子（不限触发时机）。</summary>
        public static bool HasOp(CardDef def, EffectOp op)
        {
            if (def == null)
            {
                return false;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (def.Effects[i].Op == op)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>卡面是否有某个算子，且限定触发时机。</summary>
        public static bool HasOp(CardDef def, EffectOp op, EffectTrigger trigger)
        {
            if (def == null)
            {
                return false;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == op && e.Trigger == trigger)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>某个算子在卡面上的主参数之和（如「加速 ×2」返回 2）。</summary>
        public static int SumOp(CardDef def, EffectOp op)
        {
            if (def == null)
            {
                return 0;
            }

            int sum = 0;
            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (def.Effects[i].Op == op)
                {
                    sum += def.Effects[i].A;
                }
            }

            return sum;
        }

        /// <summary>卡面是否带该类型的光环（只看光环类型，不看触发符号）。</summary>
        public static bool HasAuraOfKind(CardDef def, AuraKind kind)
        {
            if (def == null)
            {
                return false;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == EffectOp.Aura && e.Aura == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>卡面某种光环的加值合计（双光环会相加 —— 冰封铠甲返回 4、火灾返回 2）。</summary>
        public static int AuraValue(CardDef def, AuraKind kind)
        {
            if (def == null)
            {
                return 0;
            }

            int sum = 0;
            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef e = def.Effects[i];
                if (e.Op == EffectOp.Aura && e.Aura == kind)
                {
                    sum += e.A;
                }
            }

            return sum;
        }

        // ── 进攻光环（力量）─────────────────────────────────────

        /// <summary>
        /// 卡面是否自带<b>进攻力量光环</b>（<see cref="AuraKind.AtkPower"/> /
        /// <see cref="AuraKind.AtkOrDefPower"/>）。
        /// </summary>
        public static bool HasAttackAura(CardDef def)
        {
            return HasAuraOfKind(def, AuraKind.AtkPower) || HasAuraOfKind(def, AuraKind.AtkOrDefPower);
        }

        /// <summary>卡面自带的进攻力量光环加值合计。</summary>
        public static int AttackAuraValue(CardDef def)
        {
            return AuraValue(def, AuraKind.AtkPower) + AuraValue(def, AuraKind.AtkOrDefPower);
        }

        /// <summary>
        /// 这张牌的<b>进攻力量潜力</b> = 当前有效力量 + 它自带的进攻力量光环加值。
        ///
        /// <para>口径来自用户 2026-10-03 的流派判定规则：「对于有增加进攻力量的光环卡，
        /// 将光环所能提供的全部力量值直接加进去」。判分发生在开局，那时冷却区是空的
        /// —— 已激活的指示物一枚也没有，所以「光环能提供的力量」只能是<b>卡自己带的</b>那部分。</para>
        ///
        /// <para>⚠ 「力量不能增加」的牌（沉重打击 h）不带进攻光环，所以这里天然为 0，
        /// 不需要额外的 <see cref="CardDef.ForbidsAtkBuff"/> 分支。</para>
        /// </summary>
        public static int AttackPotential(CardInstance card)
        {
            return card == null ? 0 : card.EffectivePower + AttackAuraValue(card.Def);
        }

        // ── 防御光环（力量）─────────────────────────────────────

        /// <summary>卡面是否自带<b>防御力量光环</b>（<see cref="AuraKind.DefPower"/> / <see cref="AuraKind.AtkOrDefPower"/>）。</summary>
        public static bool HasDefenseAura(CardDef def)
        {
            return HasAuraOfKind(def, AuraKind.DefPower) || HasAuraOfKind(def, AuraKind.AtkOrDefPower);
        }

        /// <summary>卡面自带的防御力量光环加值合计。</summary>
        public static int DefenseAuraValue(CardDef def)
        {
            return AuraValue(def, AuraKind.DefPower) + AuraValue(def, AuraKind.AtkOrDefPower);
        }

        /// <summary>
        /// 这张牌的<b>防御力量潜力</b> = 防御时的有效力量（含 β「防御时 +A」）+ 它自带的防御力量光环加值。
        ///
        /// <para>口径来自用户规则「若有防御时的力量能达到八的卡（包括防御光环的数值在内）」。
        /// 冰封铠甲 af（5 + 2 + 2 = 9）与冷冻核心 as（6 + 3 = 9）达标。</para>
        ///
        /// <para>⚠ 冰风暴 b 自 2026-10-03 起光环改成「守护」，<b>不再是防御光环卡</b>
        /// —— 它的潜力回落到 5 + 0 = 5（本来也够不上 8 这条线）。</para>
        ///
        /// <para>⚠ 免疫光环（石化 ak / 石盾 al）不算「防御力量」，它们不进这条计算。</para>
        /// </summary>
        public static int DefensePotential(CardInstance card)
        {
            return card == null ? 0 : DefenseResolver.DefensePower(card) + DefenseAuraValue(card.Def);
        }

        // ── 连击 ────────────────────────────────────────────────

        /// <summary>
        /// 卡面是否带<b>连击来源</b>（α 时机）：自带连击 / 换连击（电弧）/
        /// 条件连击（闪电球）/ 连击光环（引雷）。
        /// </summary>
        public static bool HasCombo(CardDef def)
        {
            return HasAttackOp(def, EffectOp.Combo)
                   || HasAttackOp(def, EffectOp.CoolHandForCombo)
                   || HasAttackOp(def, EffectOp.ComboIfLastHand)
                   || HasAuraOfKind(def, AuraKind.Combo);
        }

        /// <summary>
        /// 「连击卡」—— 用户流派 3 的判分口径：<b>「除了过载外，包括引雷」</b>。
        ///
        /// <para>过载（o）同时带「回血 + 连击」，规则把它归到<b>回血卡</b>那一档，
        /// 不再计入连击卡数。所以判据是「有连击来源 <b>且</b> 不是回血卡」——
        /// 这样以后再加同类卡也自动遵守同一条口径。</para>
        /// </summary>
        public static bool IsComboCard(CardDef def)
        {
            return HasCombo(def) && !IsHealCard(def);
        }

        // ── 双发 ────────────────────────────────────────────────

        /// <summary>卡面是否带双发。</summary>
        public static bool IsDoubleCard(CardDef def)
        {
            return HasAttackOp(def, EffectOp.Double);
        }

        // ── 加速 ────────────────────────────────────────────────

        /// <summary>卡面是否带<b>单张加速</b>（含「每损失 1 点生命加速一次」的瀑流）。</summary>
        public static bool IsHasteCard(CardDef def)
        {
            return HasAttackOp(def, EffectOp.Haste) || HasAttackOp(def, EffectOp.HastePerHpLoss);
        }

        /// <summary>卡面是否带<b>区域加速</b>。</summary>
        public static bool IsHasteZoneCard(CardDef def)
        {
            return HasAttackOp(def, EffectOp.HasteZone);
        }

        /// <summary>
        /// 加速强度 —— 用于「加速能力越强越优先」的排序。
        ///
        /// <para>口径：<see cref="EffectOp.Haste"/> 按次数直接累加；
        /// <see cref="EffectOp.HastePerHpLoss"/>（瀑流）的收益与实际已损失生命挂钩，
        /// 所以按 <paramref name="hpLost"/> 折算（<b>至少算 1 次</b> —— 卡面本身就带一次底数，
        /// 见瀑流 y 的两条效果）。</para>
        ///
        /// <para>⚠ 区域加速不参与这条排序（它有自己的优先级档位）。</para>
        /// </summary>
        public static int HasteStrength(CardDef def, int hpLost)
        {
            if (def == null)
            {
                return 0;
            }

            int n = SumOp(def, EffectOp.Haste);
            int perLoss = SumOp(def, EffectOp.HastePerHpLoss);
            if (perLoss > 0)
            {
                n += perLoss * (hpLost > 0 ? hpLost : 1);
            }

            return n;
        }

        // ── 减速 ────────────────────────────────────────────────

        /// <summary>卡面是否带<b>单张减速</b>（含条件减速：地震 / 藤蔓）。</summary>
        public static bool IsSlowCard(CardDef def)
        {
            return HasAttackOp(def, EffectOp.Slow)
                   || HasAttackOp(def, EffectOp.SlowIfLastTwoHand)
                   || HasAttackOp(def, EffectOp.SlowIfUnblocked);
        }

        /// <summary>
        /// 卡面是否带<b>区域减速</b>。用户举例「暴风雪或者冰风暴」（a / b），
        /// 雪崩（an）的强制双方减速属同一类，一并计入。
        /// </summary>
        public static bool IsSlowZoneCard(CardDef def)
        {
            return HasAttackOp(def, EffectOp.SlowZone) || HasAttackOp(def, EffectOp.SlowZoneBoth);
        }

        // ── 回血 ────────────────────────────────────────────────

        /// <summary>卡面是否带回血（<see cref="EffectOp.HealMinusMax"/>：过载 / 自燃）。</summary>
        public static bool IsHealCard(CardDef def)
        {
            return HasOp(def, EffectOp.HealMinusMax);
        }

        // ── 结构 / 特殊 ─────────────────────────────────────────

        /// <summary>
        /// 是否<b>连锁牌</b>（女巫工坊「不能进献祭位」的口径，2026-10-02）。
        /// 与新 AI 无关，但同属「读卡表的角色判断」，一并收在这里避免第二处口径。
        /// </summary>
        public static bool IsChainCard(CardDef def)
        {
            return HasOp(def, EffectOp.NoAtkBuff)
                   || HasOp(def, EffectOp.Copy)
                   || HasOp(def, EffectOp.DoubleAtkBonus)
                   || (def != null && def.HiddenPower);
        }

        /// <summary>卡面是否带「守护」（防御时无视力量差必定挡住）。</summary>
        public static bool HasGuard(CardDef def)
        {
            return HasOp(def, EffectOp.Guard);
        }
    }
}
