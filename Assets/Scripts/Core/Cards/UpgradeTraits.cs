using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <b>强化词条库</b>（效果轴）—— 「给这张牌额外加上一个什么效果」的唯一清单。
    ///
    /// <para><b>为什么需要一张表，而不是让强化随便塞一个 <see cref="EffectOp"/></b>：
    /// 用户 2026-10-02 口径里的第三类强化是「改变其效果词条」——那是<b>内容</b>，
    /// 不是引擎能力。引擎只需要知道「现在多了这条效果」，而「叫它『加速』、
    /// 文案写什么、能不能叠在带光环的牌上」是设计口径，集中在这一个文件里，
    /// 加一条词条 = 这里加一行，其余代码（存档 / 合成 / 界面 / 自测）自动认得。</para>
    ///
    /// <para><b>⚠ 词条只能是既有算子</b>（<see cref="EffectOp"/>），不引入新机制：
    /// 本类只负责「拼一条 <see cref="EffectDef"/>」，结算仍然完全走
    /// <c>BuiltinEffects</c> 那套注册表。所以给一张牌加词条<b>不需要改引擎</b>，
    /// 也不会出现「界面说加了、实际不生效」的第三条路。</para>
    ///
    /// <para><b>⚠ 光环类词条的触发符号要跟卡面已有的光环对齐</b>（<c>CardDef.AuraTokenCountOf</c>）：
    /// <c>AuraResolver</c> 把「剩余指示物」建模成「光环效果列表的尾部 N 条」，
    /// 所以<b>同一张牌的光环必须同符号</b>（内建卡表由
    /// <c>CardLibrary.ValidateAuraTriggers</c> 断言）。加词条时也一样 ——
    /// 见 <see cref="AuraTriggerFor"/>；弄错的话症状是「光环亮了几枚、效果按另一张牌的算」，
    /// 而且不报任何错。</para>
    /// </summary>
    public static class UpgradeTraits
    {
        // ── 词条 ID（存档里存的就是这些字符串，**改名 = 丢数据**）──────────

        /// <summary>α 加速 −1（单张）。</summary>
        public const string Haste = "keyword.haste";

        /// <summary>α 快速回填：本牌进入冷却区时剩余冷却额外 −1。</summary>
        public const string QuickRefill = "keyword.quick-refill";

        /// <summary>α 连击：本次进攻结算完后追加一次完整进攻。</summary>
        public const string Combo = "keyword.combo";

        /// <summary>β 守护：无视力量差异必定挡住，只挡 1 次攻击。</summary>
        public const string Guard = "keyword.guard";

        /// <summary>α 双发：对方必须交出两张牌才能挡住。</summary>
        public const string Double = "keyword.double";

        /// <summary>光环：进攻力量 +2。</summary>
        public const string AuraAtk = "aura.atk";

        /// <summary>光环：防御力量 +2。</summary>
        public const string AuraDef = "aura.def";

        /// <summary>光环：使本次打出的法术获得「快速回填」（<b>用户举例里的「加速效果」</b>）。</summary>
        public const string AuraHaste = "aura.haste";

        /// <summary>光环：免疫力量 ≥6 的攻击（含双发）。</summary>
        public const string AuraImmuneHigh = "aura.immune-high";

        /// <summary>一条词条的静态描述。</summary>
        public sealed class Trait
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string Text;
            public readonly EffectTrigger Trigger;
            public readonly EffectOp Op;
            public readonly int Amount;
            public readonly AuraKind Aura;

            public Trait(string id, string label, string text, EffectTrigger trigger,
                EffectOp op, int amount = 0, AuraKind aura = AuraKind.None)
            {
                Id = id;
                Label = label;
                Text = text;
                Trigger = trigger;
                Op = op;
                Amount = amount;
                Aura = aura;
            }

            /// <summary>是不是光环类词条（追加的是第 ⑤ 步的指示物，不是即时效果）。</summary>
            public bool IsAura { get { return Op == EffectOp.Aura; } }
        }

        private static readonly List<Trait> Items = new List<Trait>
        {
            new Trait(Haste, "加速", "加速", EffectTrigger.Attack, EffectOp.Haste, 1),
            new Trait(QuickRefill, "快速回填", "快速回填", EffectTrigger.Attack, EffectOp.QuickRefill),
            new Trait(Combo, "连击", "连击", EffectTrigger.Attack, EffectOp.Combo),
            new Trait(Guard, "守护", "守护", EffectTrigger.Defend, EffectOp.Guard),
            new Trait(Double, "双发", "双发", EffectTrigger.Attack, EffectOp.Double),
            new Trait(AuraAtk, "光环：进攻力量 +2", "光环：进攻力量 +2", EffectTrigger.Attack,
                EffectOp.Aura, 2, AuraKind.AtkPower),
            new Trait(AuraDef, "光环：防御力量 +2", "光环：防御力量 +2", EffectTrigger.Attack,
                EffectOp.Aura, 2, AuraKind.DefPower),
            new Trait(AuraHaste, "光环：快速回填", "光环：使本次打出的法术获得快速回填", EffectTrigger.Attack,
                EffectOp.Aura, 1, AuraKind.QuickRefill),
            new Trait(AuraImmuneHigh, "光环：免疫力量 ≥6", "光环：免疫力量 ≥6 的攻击（含双发）", EffectTrigger.Attack,
                EffectOp.Aura, 6, AuraKind.ImmuneHigh),
        };

        /// <summary>全部词条（界面 / 调试菜单 / 自测用）。</summary>
        public static IReadOnlyList<Trait> All
        {
            get { return Items.AsReadOnly(); }
        }

        /// <summary>按 ID 取词条。</summary>
        public static bool TryGet(string id, out Trait trait)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (string.Equals(Items[i].Id, id, StringComparison.Ordinal))
                    {
                        trait = Items[i];
                        return true;
                    }
                }
            }

            trait = null;
            return false;
        }

        /// <summary>词条给人看的短名（未知 ID 原样返回，便于排查旧存档）。</summary>
        public static string LabelOf(string id)
        {
            Trait trait;
            return TryGet(id, out trait) ? trait.Label : (id ?? string.Empty);
        }

        /// <summary>
        /// 追加这个光环词条时该用哪个触发符号。
        ///
        /// <para>口径：<b>跟这张牌已有的光环同符号</b>；本来没有光环就用 α（本作 45 张卡的
        /// 光环全部是 α，所以这是常态分支）。见类注释里「同一张牌的光环必须同符号」。</para>
        /// </summary>
        public static EffectTrigger AuraTriggerFor(CardDef def)
        {
            if (def == null || def.AuraTokenCount <= 0)
            {
                return EffectTrigger.Attack;
            }

            if (def.AuraTokenCountOf(EffectTrigger.Attack) > 0)
            {
                return EffectTrigger.Attack;
            }

            if (def.AuraTokenCountOf(EffectTrigger.Defend) > 0)
            {
                return EffectTrigger.Defend;
            }

            return EffectTrigger.Special;
        }

        /// <summary>
        /// 这条词条能不能加到这张牌上。<paramref name="reason"/> 在拒绝时给**给玩家看的原因**。
        ///
        /// <para><b>四条拒绝规则</b>：</para>
        /// <list type="number">
        /// <item>词条 ID 不认识（旧存档 / 拼错）；</item>
        /// <item>这张牌<b>已经加过同一条词条</b>（配方里已经有它）——
        /// 允许叠两条「加速」会让卡面文案重复、也让「强化了几次」看不出；</item>
        /// <item>卡面<b>本来就有</b>这条算子（例如给「闪电」再加「连击」）——
        /// 双连击在现行结算里没有定义，宁可不给；</item>
        /// <item>力量类词条（进攻光环）撞上 <see cref="CardDef.ForbidsAtkBuff"/>，
        /// 以及「力量为 X」的牌（<see cref="CardDef.HiddenPower"/>）一律不加词条 ——
        /// 与力量轴同口径，避免出现「卡面写着不能增加力量，却多了一枚进攻光环」。</item>
        /// </list>
        /// </summary>
        public static bool CanApply(string traitId, CardDef def, CardUpgradeRecord existing, out string reason)
        {
            if (def == null)
            {
                reason = "没有这张牌";
                return false;
            }

            Trait trait;
            if (!TryGet(traitId, out trait))
            {
                reason = "未知词条：" + traitId;
                return false;
            }

            if (def.HiddenPower)
            {
                reason = "力量为 X 的牌无法强化";
                return false;
            }

            if (existing != null)
            {
                List<string> already = existing.Traits();
                if (already.Contains(trait.Id))
                {
                    reason = "这张牌已经强化过「" + trait.Label + "」";
                    return false;
                }
            }

            if (HasOp(def, trait.Op, trait.Aura))
            {
                reason = "卡面本身就有「" + trait.Label + "」";
                return false;
            }

            if (trait.IsAura && trait.Aura == AuraKind.AtkPower && def.ForbidsAtkBuff)
            {
                reason = "此牌的进攻力量不能增加";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 拼出这条词条对应的效果（<b>唯一实现点</b>，<see cref="CardUpgrade.Apply"/> 调它）。
        ///
        /// <para>⚠ 不做校验：调用方必须先过 <see cref="CanApply"/>。
        /// 未知词条返回空列表（不是抛异常）—— 存档里留着一条废弃词条时，
        /// 那张牌应当<b>少一条效果</b>，而不是整局打不开。</para>
        /// </summary>
        public static IReadOnlyList<EffectDef> Build(string traitId, CardDef def)
        {
            var list = new List<EffectDef>();
            Trait trait;
            if (!TryGet(traitId, out trait))
            {
                return list.AsReadOnly();
            }

            EffectTrigger trigger = trait.IsAura ? AuraTriggerFor(def) : trait.Trigger;
            list.Add(new EffectDef(trigger, trait.Op, trait.Amount, 0, 0, trait.Aura, false, trait.Text));
            return list.AsReadOnly();
        }

        /// <summary>卡面「本来就有这条算子吗」。光环按 <see cref="AuraKind"/> 细分比（双光环是合法的）。</summary>
        private static bool HasOp(CardDef def, EffectOp op, AuraKind aura)
        {
            if (def == null || def.Effects == null)
            {
                return false;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                EffectDef effect = def.Effects[i];
                if (effect == null || effect.Op != op)
                {
                    continue;
                }

                if (op != EffectOp.Aura || effect.Aura == aura)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
