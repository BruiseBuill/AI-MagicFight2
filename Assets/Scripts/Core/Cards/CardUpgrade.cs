using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 强化<b>方向</b>（轴）。一张牌可以同时被多个方向强化，互不覆盖。
    ///
    /// <para><b>为什么是「轴」而不是「强化等级」</b>：用户 2026-10-02 口径 ——
    /// 「一张卡既可以增加力量，又可以使其冷却值减小，甚至可以额外再加上一个加速效果，
    /// 但仍然是原来那张牌」。这三件事改的不是同一个字段，用<b>一个等级数</b>表达不了
    /// （等级 2 到底是力量还是冷却？），所以轴必须显式。</para>
    /// </summary>
    public enum UpgradeAxis
    {
        /// <summary>力量轴：基础力量 ± N（仍受 <see cref="CardUpgrade.PowerCap"/> 与卡表事实约束）。</summary>
        Power = 0,

        /// <summary>冷却轴：基础冷却 ± N（下限 <see cref="CardUpgrade.CooldownFloor"/>）。</summary>
        Cooldown = 1,

        /// <summary>效果轴：追加一条效果词条（见 <see cref="UpgradeTraits"/>）。</summary>
        Effect = 2,

        /// <summary>
        /// 转移轴：把**另一张牌的一条效果**并进来（2026-10-02 · 女巫的工坊）。
        ///
        /// <para>它与 <see cref="Effect"/> 的区别是「效果从哪来」：那个只能挑
        /// <see cref="UpgradeTraits"/> 里的固定词条，这个装的是<b>任意一条既有效果</b>的画像
        /// （<see cref="MagicBrawl.Core.EffectSpec"/>）—— 也就是「把 <c>沉重打击</c> 的那条
        /// 「额外伤害」搬到别的牌上」这类跨卡转移。</para>
        /// </summary>
        Transfer = 3,
    }

    /// <summary>
    /// <b>一条强化</b>（配方里的一步）：改哪个轴、改多少 / 加哪条词条。
    ///
    /// <para><b>⚠ 它必须能被 <c>JsonUtility</c> 序列化</b>（存档就是靠它落盘的）：
    /// <c>[Serializable]</c> + <b>public 字段</b> + <b>无参构造</b>，不允许出现只读字段、
    /// 属性、字典或接口类型 —— 那些会被 JsonUtility <b>静默丢掉</b>（不报错，存档少一截）。
    /// 这条约束由自测 <c>UpgradeBookScenario</c> 用反射钉住。</para>
    /// </summary>
    [Serializable]
    public sealed class CardUpgradeMod
    {
        /// <summary>改哪个轴。</summary>
        public UpgradeAxis axis = UpgradeAxis.Power;

        /// <summary>
        /// 数值：<see cref="UpgradeAxis.Power"/> 是力量增量（正数）；
        /// <see cref="UpgradeAxis.Cooldown"/> 是冷却增量（<b>负数 = 加速</b>，例如 −1）；
        /// <see cref="UpgradeAxis.Effect"/> 不用（留 0）。
        /// </summary>
        public int value;

        /// <summary>效果轴的词条 ID（见 <see cref="UpgradeTraits"/>）；其余轴留空。</summary>
        public string trait = string.Empty;

        /// <summary>转移轴搬进来的那条效果（<see cref="UpgradeAxis.Transfer"/> 专用）。</summary>
        public EffectSpec effect;

        /// <summary>JsonUtility 要无参构造。</summary>
        public CardUpgradeMod()
        {
        }

        /// <summary>力量 + <paramref name="delta"/>。</summary>
        public static CardUpgradeMod Power(int delta)
        {
            return new CardUpgradeMod { axis = UpgradeAxis.Power, value = delta, trait = string.Empty };
        }

        /// <summary>冷却 + <paramref name="delta"/>（<b>负数 = 加速</b>）。</summary>
        public static CardUpgradeMod Cooldown(int delta)
        {
            return new CardUpgradeMod { axis = UpgradeAxis.Cooldown, value = delta, trait = string.Empty };
        }

        /// <summary>追加一条效果词条。</summary>
        public static CardUpgradeMod Effect(string traitId)
        {
            return new CardUpgradeMod { axis = UpgradeAxis.Effect, value = 0, trait = traitId ?? string.Empty };
        }

        /// <summary>把一条效果（从别的牌搬来的）并进来。</summary>
        public static CardUpgradeMod Transfer(EffectSpec spec)
        {
            return new CardUpgradeMod { axis = UpgradeAxis.Transfer, value = 0, effect = spec };
        }

        /// <summary>给人看的一行字（界面预览与日志共用）。</summary>
        public string Describe()
        {
            switch (axis)
            {
                case UpgradeAxis.Power:
                    return "力量 " + (value >= 0 ? "+" : "") + value;
                case UpgradeAxis.Cooldown:
                    return "冷却 " + (value >= 0 ? "+" : "") + value;
                case UpgradeAxis.Effect:
                    return "追加「" + UpgradeTraits.LabelOf(trait) + "」";
                case UpgradeAxis.Transfer:
                    return "获得效果「" + (effect == null ? "?" : effect.Describe()) + "」";
                default:
                    return "未知强化";
            }
        }
    }

    /// <summary>
    /// <b>一张牌的强化配方</b>：这张牌被叠过哪些强化，<b>按叠加顺序</b>记着。
    ///
    /// <para><b>为什么配方挂在「基础 ID」上而不是挂在一份新卡定义上</b>：
    /// 这是这一版和上一版（<c>Card_a_Up.asset</c> 那种「落盘一张新卡」）最根本的区别 ——
    /// ① 配方是<b>数据</b>，能随存档走，也能挂在怪物的牌堆上；一份资产做不到
    /// 「同一个基础卡在 A 存档是 +2 力量、在 B 存档是 −1 冷却」；
    /// ② 多轴叠加时，「当前是第几版」这种东西没有唯一解，但「叠过哪几笔」有 ——
    /// 记流水才可回退、可复算。</para>
    ///
    /// <para><b>⚠ 同一性靠 <see cref="baseId"/>，不靠卡面</b>：卡面 ID / 卡名挂 <c>+</c>、
    /// 数值与词条都变了，但 <see cref="CardUpgrade.BaseIdOf"/> 永远能把它还原成
    /// 「还是那张 <c>a</c>」—— 卡池、商店「未持有」比对、女巫「未强化过」判定全都读它。</para>
    /// </summary>
    [Serializable]
    public sealed class CardUpgradeRecord
    {
        /// <summary>这张牌的基础 ID（<b>永远不带 <c>+</c></b>）。</summary>
        public string baseId = string.Empty;

        /// <summary>叠过的强化，按顺序。</summary>
        public List<CardUpgradeMod> mods = new List<CardUpgradeMod>();

        public CardUpgradeRecord()
        {
        }

        /// <summary>
        /// 配一个配方（<b>会过滤 null 与无效项，并做一次基础归一</b>）。
        ///
        /// <para>归一规则与「写入存档」同一口径：<see cref="baseId"/> 一律削掉 <c>+</c>
        /// （否则同一个基础卡会分裂成 <c>a</c> 与 <c>a+</c> 两条配方，
        /// 读取时只有一条生效、另一条静默失效）。</para>
        /// </summary>
        public static CardUpgradeRecord Of(string baseId, params CardUpgradeMod[] mods)
        {
            var record = new CardUpgradeRecord { baseId = CardUpgrade.BaseIdOf(baseId) };
            if (mods != null)
            {
                for (int i = 0; i < mods.Length; i++)
                {
                    if (mods[i] == null)
                    {
                        continue;
                    }

                    // 空词条 / 空效果 = 无效项（不放进配方，否则「有配方但什么也没加」，
                    // 合成出一张挂着「+」却跟基础卡一模一样的牌）。
                    if (mods[i].axis == UpgradeAxis.Effect && string.IsNullOrEmpty(mods[i].trait))
                    {
                        continue;
                    }

                    if (mods[i].axis == UpgradeAxis.Transfer
                        && (mods[i].effect == null || string.IsNullOrEmpty(mods[i].effect.handlerId)))
                    {
                        continue;
                    }

                    record.mods.Add(mods[i]);
                }
            }

            return record;
        }

        /// <summary>这条配方是否什么也没做（<b>不产生强化版</b>）。</summary>
        public bool IsEmpty
        {
            get { return mods == null || mods.Count == 0 || string.IsNullOrEmpty(baseId); }
        }

        /// <summary>某个轴的净增量（<see cref="UpgradeAxis.Effect"/> 返回词条条数）。</summary>
        public int Sum(UpgradeAxis axis)
        {
            int total = 0;
            if (mods == null)
            {
                return 0;
            }

            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i] != null && mods[i].axis == axis)
                {
                    total += mods[i].value;
                }
            }

            return total;
        }

        /// <summary>这个配方里的全部词条 ID（顺序保留，可能有重复 —— 由校验挡下）。</summary>
        public List<string> Traits()
        {
            var list = new List<string>();
            if (mods != null)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    if (mods[i] != null && mods[i].axis == UpgradeAxis.Effect
                        && !string.IsNullOrEmpty(mods[i].trait))
                    {
                        list.Add(mods[i].trait);
                    }
                }
            }

            return list;
        }

        /// <summary>这个配方里搬进来的全部效果（转移轴，顺序保留）。</summary>
        public List<EffectSpec> Transfers()
        {
            var list = new List<EffectSpec>();
            if (mods != null)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    if (mods[i] != null && mods[i].axis == UpgradeAxis.Transfer && mods[i].effect != null)
                    {
                        list.Add(mods[i].effect);
                    }
                }
            }

            return list;
        }

        /// <summary>一行摘要（日志 / 弹出提示用）。</summary>
        public string Describe()
        {
            if (IsEmpty)
            {
                return "（无强化）";
            }

            var parts = new List<string>();
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i] != null)
                {
                    parts.Add(mods[i].Describe());
                }
            }

            return string.Join(" · ", parts.ToArray());
        }
    }

    /// <summary>
    /// 卡牌<b>强化</b>的唯一口径（2026-09-30 首版 · 2026-10-02 改成多轴配方）。
    ///
    /// <para><b>它现在管三件事</b>：</para>
    /// <list type="number">
    /// <item>判定：某个轴还能不能强化（力量封顶 9 / 冷却下限 1 / 词条不重复且不与卡面冲突）；</item>
    /// <item>合成：把「基础卡 + 配方」合成出一份<b>新的不可变 <see cref="CardDef"/></b>；</item>
    /// <item>同一性：卡面 ID / 卡名挂 <c>+</c>，而 <see cref="BaseIdOf"/> 永远能还原成原牌。</item>
    /// </list>
    ///
    /// <para><b>为什么放在 Core 而不是视图里</b>：+2 / 封顶 9 / 谁不能强化，是**规则**，
    /// 不是画法。视图只消费判定结果与数字，不自己写「≥9 就跳过」这类判断 ——
    /// 否则界面与引擎会各算一份（本工程在「攻击预判」上已经定过这条：AI 与预判必须同源）。</para>
    ///
    /// <para><b>为什么不改 <see cref="CardDef"/> 而是合成一份新的</b>：<see cref="CardDef"/>
    /// 是不可变的，且它是<b>卡表级的共享定义</b>（敌方同名牌指着同一份）。原地改力量会
    /// 连敌人手里的那张一起改掉。</para>
    ///
    /// <para><b>⚠ 「强化版」的 ID 与卡面插画</b>：合成定义的 <c>Id</c> = 基础 ID 后面挂一个 <c>+</c>
    /// （<c>"a"</c> → <c>"a+"</c>），<b>而 <c>ArtId</c> 保持基础卡原样（<c>"a"</c>）</b> ——
    /// 卡面插画是按 ArtId 查 <c>CardArtLibrary</c> 的，让新 ID 去查会查不到，
    /// 症状是「强化后的牌变一块纯色板」而且零报错。</para>
    /// </summary>
    public static class CardUpgrade
    {
        /// <summary>每次强化的力量增量。</summary>
        public const int PowerStep = 2;

        /// <summary>
        /// 力量上限。<b>「最多只能把基础力量提升到 9」</b>（用户 2026-09-30 口径）。
        ///
        /// <para>口径是<b>结果封顶</b>而不是「只允许 ≤7 的牌」：基础力量 8 的牌照样能选，
        /// 结果是 9（实际只 +1），界面上显示成「8 → 9」。</para>
        /// </summary>
        public const int PowerCap = 9;

        /// <summary>冷却轴的下限（<see cref="CardDef"/> 构造就要求冷却 ≥ 1）。</summary>
        public const int CooldownFloor = 1;

        /// <summary>合成定义的卡表序号起点 —— 与 <c>CardCatalogAsset</c> 的 1000 / 10000 两段错开。</summary>
        public const int SynthesizedIndexBase = 30000;

        /// <summary>强化卡的 ID / 卡名后缀。</summary>
        public const string Suffix = "+";

        /// <summary>同一个后缀的字符形式（<c>TrimEnd</c> 只吃字符）。</summary>
        public const char SuffixChar = '+';

        // ══════════════════════════════════════════════════════
        //  同一性
        // ══════════════════════════════════════════════════════

        /// <summary>这张牌是不是已经强化过的（ID 带后缀）。</summary>
        public static bool IsUpgraded(CardDef def)
        {
            return def != null && def.Id != null
                   && def.Id.EndsWith(Suffix, StringComparison.Ordinal);
        }

        /// <summary>去掉尾部的 <c>+</c>（反复强化时不能越挂越多，见 <see cref="UpgradedId"/>）。</summary>
        public static string BaseId(CardDef def)
        {
            return def == null ? string.Empty : BaseIdOf(def.Id);
        }

        /// <summary>
        /// 同上，但吃的是字符串 ID。
        ///
        /// <para><b>这是「仍然能被识别为同一张卡」的唯一实现点</b>：存档里存的是<b>基础 ID</b>
        /// （<c>"a"</c>），界面上拿到的常常是<b>解析后</b>的 ID（<c>"a+"</c>）——
        /// 比较同一张牌、写入存档前归一、按 ID 查配方，全部走它。卡池 / 商店 / 女巫工坊
        /// / 存档 / 强化册共用这一处。</para>
        /// </summary>
        public static string BaseIdOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            return id.TrimEnd(SuffixChar);
        }

        /// <summary>强化后的卡 ID。<b>不叠加</b> —— 强化过的牌再强化仍是 <c>"a+"</c>。</summary>
        public static string UpgradedId(CardDef def)
        {
            return BaseId(def) + Suffix;
        }

        /// <summary>强化后的卡 ID（吃字符串）。</summary>
        public static string UpgradedIdOf(string id)
        {
            return BaseIdOf(id) + Suffix;
        }

        /// <summary>强化后的卡名（<c>"暴风雪"</c> → <c>"暴风雪+"</c>）。同样不叠加。</summary>
        public static string UpgradedName(CardDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Name))
            {
                return string.Empty;
            }

            return def.Name.TrimEnd(SuffixChar) + Suffix;
        }

        // ══════════════════════════════════════════════════════
        //  判定（逐轴）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌<b>力量轴</b>能不能再强化。<paramref name="reason"/> 在返回 <c>false</c> 时
        /// 给出**给玩家看的原因**（界面直接在不可选的卡上写它）。
        ///
        /// <para><b>三条拒绝规则</b>（都用卡表里的既有事实判，<b>不写死卡 ID</b>）：</para>
        /// <list type="number">
        /// <item><see cref="CardDef.HiddenPower"/> —— 卡面力量显示为 <c>X</c>（模仿 <c>x</c>）；</item>
        /// <item><see cref="CardDef.ForbidsAtkBuff"/> —— 卡面带「此法术的进攻力量不能增加」（沉重打击 <c>h</c>）；</item>
        /// <item>力量已到 <see cref="PowerCap"/>。</item>
        /// </list>
        /// </summary>
        public static bool CanUpgradePower(CardDef def, out string reason)
        {
            if (def == null)
            {
                reason = "没有这张牌";
                return false;
            }

            if (def.HiddenPower)
            {
                reason = "力量为 X 的牌无法强化";
                return false;
            }

            if (def.ForbidsAtkBuff)
            {
                reason = "此牌的进攻力量不能增加";
                return false;
            }

            if (def.Power >= PowerCap)
            {
                reason = "力量已达上限 " + PowerCap;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// <b>力量轴</b>的判定。等价于 <see cref="CanUpgradePower"/> —— 保留旧名字，
        /// 因为「力量 +2」仍然是第一个强化节点给出的那一条，调用点最多。
        /// </summary>
        public static bool CanUpgrade(CardDef def, out string reason)
        {
            return CanUpgradePower(def, out reason);
        }

        /// <summary>
        /// 这张牌<b>冷却轴</b>能不能强化（<b>加速</b>方向）。
        ///
        /// <para>拒绝条件只有一条：冷却已在下限 <see cref="CooldownFloor"/>。
        /// <b>刻意不设 ≤1 的「本来就很短所以不许加速」这类额外条件</b> ——
        /// 那是平衡口径，不是规则；真要卡，加在节点发放侧（哪个节点给哪个方向）。</para>
        /// </summary>
        public static bool CanUpgradeCooldown(CardDef def, out string reason)
        {
            if (def == null)
            {
                reason = "没有这张牌";
                return false;
            }

            if (def.Cooldown <= CooldownFloor)
            {
                reason = "冷却已达下限 " + CooldownFloor;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 这张牌能不能追加词条 <paramref name="trait"/>（效果轴）。
        /// 判据本体在 <see cref="UpgradeTraits.CanApply"/>（那里才知道每个词条意味着什么）。
        /// </summary>
        public static bool CanApplyEffect(CardDef def, string trait, CardUpgradeRecord existing, out string reason)
        {
            return UpgradeTraits.CanApply(trait, def, existing, out reason);
        }

        /// <summary>
        /// 这张牌能不能接收一条搬来的效果（转移轴）。
        ///
        /// <para><b>三条拒绝</b>：</para>
        /// <list type="number">
        /// <item>效果是空的（没得搬）；</item>
        /// <item>力量为 X 的牌（<see cref="CardDef.HiddenPower"/>，模仿）—— 它的力量由
        ///   <see cref="EffectOp.Copy"/> 临时接管、卡面恒定「未复制时视为 1」，
        ///   再挂一条常驻效果会与卡面直接矛盾（与 <see cref="UpgradeTraits.CanApply"/> 同口径）；</item>
        /// <item>这张牌<b>已经有同一条效果</b>（判据见 <see cref="EffectSpec.SameAs"/>）——
        ///   多一条一模一样的文案没用，而重复的「连击 / 双发」是实打实的强度翻倍。</item>
        /// </list>
        /// </summary>
        public static bool CanTransfer(CardDef def, EffectSpec spec, out string reason)
        {
            if (def == null)
            {
                reason = "没有这张牌";
                return false;
            }

            if (spec == null || string.IsNullOrEmpty(spec.handlerId))
            {
                reason = "没有可转移的效果";
                return false;
            }

            if (def.HiddenPower)
            {
                reason = "力量为 X 的牌无法强化";
                return false;
            }

            if (HasEquivalent(def, spec))
            {
                reason = "这张牌已经有同一条效果";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>这张牌身上有没有与 <paramref name="spec"/> 等价的那条效果。</summary>
        public static bool HasEquivalent(CardDef def, EffectSpec spec)
        {
            if (def == null || spec == null || def.Effects == null)
            {
                return false;
            }

            for (int i = 0; i < def.Effects.Count; i++)
            {
                if (spec.SameAs(def.Effects[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>一次判定某个轴上的某一步强化能不能做（发决策前的那道门）。</summary>
        public static bool CanApply(CardDef def, CardUpgradeMod mod, CardUpgradeRecord existing, out string reason)
        {
            if (def == null)
            {
                reason = "没有这张牌";
                return false;
            }

            if (mod == null)
            {
                reason = "没有强化内容";
                return false;
            }

            switch (mod.axis)
            {
                case UpgradeAxis.Power:
                    return CanUpgradePower(def, out reason);
                case UpgradeAxis.Cooldown:
                    return CanUpgradeCooldown(def, out reason);
                case UpgradeAxis.Effect:
                    return UpgradeTraits.CanApply(mod.trait, def, existing, out reason);
                case UpgradeAxis.Transfer:
                    return CanTransfer(def, mod.effect, out reason);
                default:
                    reason = "未知强化方向";
                    return false;
            }
        }

        // ══════════════════════════════════════════════════════
        //  数值
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 强化后的力量。
        ///
        /// <para>⚠ 是「<b>当前</b>力量」不是「卡表原值」：强化过的牌再强化，是在它自己身上再加 2。
        /// 因为封顶 9，8 → 9 时增量只有 1，这是有意为之（口径 = 结果封顶）。</para>
        ///
        /// <para>X（模仿）与「不能增加力量」的牌恒等于原值 —— 力量轴在它们身上根本不生效，
        /// 这也让 <see cref="Apply(CardDef, CardUpgradeRecord, int)"/> 不必特判。</para>
        /// </summary>
        public static int UpgradedPower(CardDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            if (def.HiddenPower || def.ForbidsAtkBuff)
            {
                return def.Power;
            }

            int next = def.Power + PowerStep;
            return next > PowerCap ? PowerCap : next;
        }

        /// <summary>按一份配方算出合成后的力量（<b>P 轴全部求和后封顶</b>）。</summary>
        public static int PowerAfter(CardDef baseDef, CardUpgradeRecord record)
        {
            if (baseDef == null)
            {
                throw new ArgumentNullException(nameof(baseDef));
            }

            if (baseDef.HiddenPower || baseDef.ForbidsAtkBuff)
            {
                return baseDef.Power;   // 力量轴在这两类牌上不生效
            }

            int power = baseDef.Power + (record == null ? 0 : record.Sum(UpgradeAxis.Power));
            if (power > PowerCap)
            {
                power = PowerCap;
            }

            return power < 0 ? 0 : power;
        }

        /// <summary>按一份配方算出合成后的冷却（<b>C 轴求和后夹在 [1, 基础值] 之间</b>）。</summary>
        public static int CooldownAfter(CardDef baseDef, CardUpgradeRecord record)
        {
            if (baseDef == null)
            {
                throw new ArgumentNullException(nameof(baseDef));
            }

            int cooldown = baseDef.Cooldown + (record == null ? 0 : record.Sum(UpgradeAxis.Cooldown));

            // 上限 = 基础冷却值：卡表的「减速上限 = 基础冷却值」（见 EffectOp.Slow）在强化上也照用，
            // 否则一张 2 冷却的牌叠两次「+1 冷却」会长成 4，卡面写着 4 但规则的减速上限仍是 2。
            if (cooldown > baseDef.Cooldown)
            {
                cooldown = baseDef.Cooldown;
            }

            return cooldown < CooldownFloor ? CooldownFloor : cooldown;
        }

        // ══════════════════════════════════════════════════════
        //  合成
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>把「基础卡 + 配方」合成出一份定义</b>（本类的主入口）。
        ///
        /// <para><b>除了配方点名的字段，其余逐项沿用基础卡</b> —— 元素、隐藏力量标记、
        /// <see cref="CardDef.ArtId"/>（卡面插画）、版本号都不动；卡面 ID / 卡名只挂一个 <c>+</c>，
        /// <b>不叠加</b>（强化十次也还是 <c>"a+"</c> / <c>"暴风雪+"</c>）。</para>
        ///
        /// <para><b>⚠ 配方为空时原样返回 <paramref name="baseDef"/></b>（不是"挂个 + 的空壳"）：
        /// 一张没被强化过的牌不该在界面上显示加号。调用方要判「有没有强化」请用
        /// <see cref="CardUpgradeRecord.IsEmpty"/>。</para>
        ///
        /// <para><paramref name="index"/> 是卡表序号口径：合成卡不属于内置卡表，
        /// 由调用方给一个离开内置区间的序号（<see cref="UpgradeBook.BuildCatalog"/> 用
        /// <see cref="SynthesizedIndexBase"/> 起）。</para>
        /// </summary>
        public static CardDef Apply(CardDef baseDef, CardUpgradeRecord record, int index)
        {
            if (baseDef == null)
            {
                throw new ArgumentNullException(nameof(baseDef));
            }

            if (record == null || record.IsEmpty)
            {
                return baseDef;
            }

            var effects = new List<EffectDef>();
            if (baseDef.Effects != null)
            {
                for (int i = 0; i < baseDef.Effects.Count; i++)
                {
                    effects.Add(baseDef.Effects[i]);
                }
            }

            var traits = record.Traits();
            for (int i = 0; i < traits.Count; i++)
            {
                IReadOnlyList<EffectDef> added = UpgradeTraits.Build(traits[i], baseDef);
                for (int e = 0; e < added.Count; e++)
                {
                    effects.Add(added[e]);
                }
            }

            // 转移轴：把搬来的效果并进效果列表。
            // ⚠ 光环类要按**目标牌已有的光环符号**改派触发时机（见 EffectSpec.ToEffectDef）——
            //   否则一张牌上会出现两种符号的光环，AuraResolver 按「尾部 N 条」取剩余指示物时会错位。
            var transfers = record.Transfers();
            for (int i = 0; i < transfers.Count; i++)
            {
                EffectSpec spec = transfers[i];
                EffectTrigger trigger = spec.aura != AuraKind.None
                    ? UpgradeTraits.AuraTriggerFor(baseDef)
                    : spec.trigger;
                effects.Add(spec.ToEffectDef(trigger));
            }

            return new CardDef(
                index,
                UpgradedId(baseDef),
                UpgradedName(baseDef),
                PowerAfter(baseDef, record),
                CooldownAfter(baseDef, record),
                effects,
                baseDef.HiddenPower,
                baseDef.ArtId,
                baseDef.Version,
                baseDef.Element);
        }

        /// <summary>
        /// 只叠一笔的便捷入口（等价于 <c>Apply(baseDef, CardUpgradeRecord.Of(id, mod), index)</c>）。
        /// </summary>
        public static CardDef Apply(CardDef baseDef, CardUpgradeMod mod, int index)
        {
            if (baseDef == null)
            {
                throw new ArgumentNullException(nameof(baseDef));
            }

            return Apply(baseDef, CardUpgradeRecord.Of(baseDef.Id, mod), index);
        }

        /// <summary>
        /// <b>兼容重载</b>：只做一次「力量 +2」的合成（2026-09-30 那版的唯一行为）。
        ///
        /// <para>保留它是因为调用点最多的就是这一条（自测 <c>CardUpgradeScenario</c> 也是按它写的），
        /// 而它现在的语义只是 <see cref="Apply(CardDef, CardUpgradeRecord, int)"/> 的一个特例 ——
        /// <b>不存在第二套强化实现</b>。</para>
        /// </summary>
        public static CardDef Apply(CardDef def, int index)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            return Apply(def, CardUpgradeRecord.Of(def.Id, CardUpgradeMod.Power(PowerStep)), index);
        }

        // ══════════════════════════════════════════════════════
        //  目录解析
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 从一份卡池 ID 清单解析出「**最终该用哪几份定义**」的 ID：清单里若写着 <c>a</c>，
        /// 而 <paramref name="catalog"/> 里已经存在 <c>a+</c>，就换成 <c>a+</c>。
        ///
        /// <para><b>这是「强化过一次，下次进来那张牌还是 6 点」的闭环点之一</b>，
        /// 卡池资产（<c>CardPoolConfig.ResolveIds</c>）与存档（<c>SaveStore</c>）都必须走它。
        /// 强化版现在有两种来路，<b>两种都落在 <paramref name="catalog"/> 这一层</b>，
        /// 所以这里不需要知道它们有什么区别：</para>
        /// <list type="bullet">
        /// <item>新口径 —— 存档里的<b>配方</b>由 <see cref="UpgradeBook.BuildCatalog"/> 合成；</item>
        /// <item>旧口径 —— 上一版落盘的 <c>Card_a_Up.asset</c>（动态卡区），仍可读。</item>
        /// </list>
        ///
        /// <para><b>⚠ 判据是「<paramref name="catalog"/> 里有没有 <c>&lt;id&gt;+</c>」，
        /// 不是「清单里有没有」</b>：清单里永远只写基础 ID。第一版写成看清单，于是永远不触发。</para>
        ///
        /// <para>顺带<b>去重</b>（保持首次出现的顺序）：清单里同时有 <c>a</c> 与 <c>a+</c> 时
        /// 只留一份，不会出现「同名牌两张」。</para>
        /// </summary>
        public static IReadOnlyList<string> PreferUpgraded(IEnumerable<string> ids, ICardCatalog catalog = null)
        {
            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            var list = new List<string>();

            if (ids != null)
            {
                foreach (string id in ids)
                {
                    if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                    {
                        list.Add(id);
                    }
                }
            }

            var catalogIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CardDef card in source.All)
            {
                if (card != null)
                {
                    catalogIds.Add(card.Id);
                }
            }

            var kept = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string id = list[i];
                string upgradedId = UpgradedIdOf(id);
                if (catalogIds.Contains(upgradedId))
                {
                    if (!kept.Contains(upgradedId))
                    {
                        kept.Add(upgradedId);   // 强化版在 → 用强化版
                    }

                    continue;
                }

                kept.Add(id);
            }

            return kept.AsReadOnly();
        }

        /// <summary>一份 ID 清单在给定目录下「有没有可用的强化版」（只问有没有，不动清单）。</summary>
        public static bool HasUpgraded(CardDef def, ICardCatalog catalog)
        {
            if (def == null || catalog == null)
            {
                return false;
            }

            string id = UpgradedId(def);
            for (int i = 0; i < catalog.All.Count; i++)
            {
                CardDef card = catalog.All[i];
                if (card != null && string.Equals(card.Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
