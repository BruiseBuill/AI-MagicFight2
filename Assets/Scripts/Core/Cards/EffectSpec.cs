using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// <see cref="EffectSpec"/> 里的一条**条件**的可序列化形式（<see cref="EffectCondition"/> 是只读的）。
    /// </summary>
    [Serializable]
    public sealed class EffectSpecCondition
    {
        public string id = string.Empty;
        public int value;

        /// <summary><c>JsonUtility</c> 要无参构造。</summary>
        public EffectSpecCondition()
        {
        }

        public EffectSpecCondition(string id, int value)
        {
            this.id = id ?? string.Empty;
            this.value = value;
        }
    }

    /// <summary>
    /// <b>一条效果的完整画像</b> —— 「女巫的工坊」把某张牌的一条效果搬到另一张牌上时，
    /// 存进强化配方里的就是它（2026-10-02）。
    ///
    /// <para><b>为什么要把效果「拍平」存下来，而不是存「源卡 ID + 第几条」</b>：
    /// 转移是<b>永久</b>的 —— 源牌<b>当场被消耗掉</b>，而目标牌之后要在任何场合
    /// （战斗 / 商店 / 女巫 / 强化）都带着这条效果。若只存「从 <c>h</c> 的第 1 条搬来」，
    /// 那就等于把「这张牌长什么样」这条事实<b>挂在一张已经不存在的牌上</b>，
    /// 而且卡表以后调整效果顺序（插一条、删一条）会让它静默变成另一条效果。
    /// 拍平存下来，效果就真正成了目标牌<b>自己身上的一条效果</b>。</para>
    ///
    /// <para><b>⚠ 它必须能被 <c>JsonUtility</c> 序列化</b>：<c>[Serializable]</c> + public
    /// 非只读字段 + 无参构造。字段是 <see cref="EffectDef"/> 的逐项镜像 ——
    /// 那边是只读的、还有 <c>IReadOnlyList</c> / 字典，<c>JsonUtility</c> 全都搬不动。</para>
    ///
    /// <para><b>⚠ 参数按「名字」存</b>（<see cref="primaryKey"/>）：<see cref="EffectDef"/> 的参数
    /// 是命名参数（<c>count</c> / <c>threshold</c> / <c>amount</c>），按算子猜键名会让
    /// <c>A</c> 静默读成 0 —— 症状是「效果搬过来了、但一点也不生效」。见
    /// <see cref="EffectDef.PrimaryArgumentName"/>。</para>
    /// </summary>
    [Serializable]
    public sealed class EffectSpec
    {
        /// <summary>触发时机（α / β / γ）。光环类在合成时会按目标牌已有的光环**改派**，见 <see cref="ToEffectDef"/>。</summary>
        public EffectTrigger trigger;

        /// <summary>行为注册名（内置算子 = <see cref="EffectOp"/>.ToString()）。</summary>
        public string handlerId = string.Empty;

        /// <summary>主参数在参数字典里的**键名**（<c>count</c> / <c>threshold</c> / <c>amount</c>）。</summary>
        public string primaryKey = "amount";

        /// <summary>主参数的值。</summary>
        public int primary;

        /// <summary>次参数（<c>secondary</c>）。</summary>
        public int secondary;

        /// <summary>冷却修正（<c>cooldownAdjustment</c>）。</summary>
        public int cooldownAdjustment;

        /// <summary>光环类型（<see cref="AuraKind.None"/> = 不是光环）。</summary>
        public AuraKind aura;

        /// <summary>能不能跳过（无目标效果自动执行）。</summary>
        public bool mandatory;

        /// <summary>作用范围（<c>Participants</c> / <c>Self</c> / <c>Opponent</c>）。</summary>
        public EffectTargetScope targets;

        /// <summary>特殊事件（如 <c>cooldown.completed</c>）。</summary>
        public string specialEvent = string.Empty;

        /// <summary>一次进攻内的目标去重范围（瀑流用 <c>torrent</c>）。</summary>
        public string distinctTargetGroup = string.Empty;

        /// <summary>卡面文案（含「光环：…」那类前缀，不带 α/β/γ 符号）。</summary>
        public string text = string.Empty;

        /// <summary>
        /// 这条效果是从哪张牌搬来的（**基础 ID**）。
        ///
        /// <para><b>只作追溯与显示</b>，合成时<b>完全不看它</b> —— 源牌早已被消耗，
        /// 效果已经是目标牌自己的一部分（见类注释「为什么拍平存」）。</para>
        /// </summary>
        public string sourceBaseId = string.Empty;

        /// <summary>附带条件（一般只有那三条条件算子才有；空数组 = 没有条件）。</summary>
        public EffectSpecCondition[] conditions = new EffectSpecCondition[0];

        /// <summary><c>JsonUtility</c> 要无参构造。</summary>
        public EffectSpec()
        {
        }

        // ══════════════════════════════════════════════════════
        //  进出
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把一条 <see cref="EffectDef"/> 拍成画像。
        ///
        /// <para><see cref="primary"/> 取的是 <c>effect.A</c> —— <see cref="EffectDef"/> 的构造函数
        /// 已经把「主参数」取出来放进 <c>A</c> 了（见那边的 <c>A = Arg(PrimaryArgument(legacy))</c>），
        /// 所以这里按同一个键名存回去就能逐字往返。</para>
        /// </summary>
        public static EffectSpec From(EffectDef effect, string sourceBaseId)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            var spec = new EffectSpec
            {
                trigger = effect.Trigger,
                handlerId = effect.HandlerId,
                primaryKey = EffectDef.PrimaryArgumentName(effect.Op),
                primary = effect.A,
                secondary = effect.B,
                cooldownAdjustment = effect.C,
                aura = effect.Aura,
                mandatory = effect.Mandatory,
                targets = effect.Targets,
                specialEvent = effect.SpecialEvent ?? string.Empty,
                distinctTargetGroup = effect.DistinctTargetGroup ?? string.Empty,
                text = effect.Text ?? string.Empty,
                sourceBaseId = CardUpgrade.BaseIdOf(sourceBaseId),
            };

            if (effect.Conditions != null && effect.Conditions.Count > 0)
            {
                spec.conditions = new EffectSpecCondition[effect.Conditions.Count];
                for (int i = 0; i < effect.Conditions.Count; i++)
                {
                    EffectCondition condition = effect.Conditions[i];
                    spec.conditions[i] = new EffectSpecCondition(
                        condition == null ? string.Empty : condition.Id,
                        condition == null ? 0 : condition.Value);
                }
            }

            return spec;
        }

        /// <summary>
        /// 还原成一条能进结算的效果。
        ///
        /// <para><b>⚠ <paramref name="useTrigger"/> 是给光环用的</b>：光环的剩余指示物是
        /// 「光环效果列表的尾部 N 条」（见 <c>AuraResolver</c>），所以<b>一张牌的光环必须同符号</b>。
        /// 一条 α 光环搬到一张只有 β 光环的牌上时必须<b>改派成 β</b>，
        /// 否则那张牌的两种符号光环会错位消耗 —— 表现是「亮了两枚、效果算的是另一条」，零报错。
        /// 非光环效果直接传 <see cref="trigger"/> 原值。</para>
        /// </summary>
        public EffectDef ToEffectDef(EffectTrigger useTrigger)
        {
            var arguments = new Dictionary<string, int>();
            arguments[string.IsNullOrEmpty(primaryKey) ? "amount" : primaryKey] = primary;
            arguments["secondary"] = secondary;
            arguments["cooldownAdjustment"] = cooldownAdjustment;

            List<EffectCondition> list = null;
            if (conditions != null && conditions.Length > 0)
            {
                list = new List<EffectCondition>(conditions.Length);
                for (int i = 0; i < conditions.Length; i++)
                {
                    EffectSpecCondition condition = conditions[i];
                    if (condition != null && !string.IsNullOrEmpty(condition.id))
                    {
                        list.Add(new EffectCondition(condition.id, condition.value));
                    }
                }
            }

            return new EffectDef(useTrigger, handlerId, arguments, aura, text, specialEvent,
                targets, distinctTargetGroup, list, mandatory);
        }

        /// <summary>还原成一条效果（非光环语义下用它；光环请走 <see cref="ToEffectDef"/> 显式给符号）。</summary>
        public EffectDef ToEffectDef()
        {
            return ToEffectDef(trigger);
        }

        // ══════════════════════════════════════════════════════
        //  比较 / 描述
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌上**是不是已经有同一条效果了**（判据 = 算子 + 光环类型 + 三个参数）。
        ///
        /// <para>比的是「效果本身」而不是触发符号：光环搬到别的牌上会被改派符号，
        /// 但它在语义上仍然是同一条光环。同理也不比 <see cref="sourceBaseId"/> ——
        /// 那是记录，不是效果的一部分。</para>
        ///
        /// <para>用它挡下「把 <c>加速</c> 搬给一张本来就有 <c>加速</c> 的牌」：
        /// 那不会崩，但卡面会出现两条一模一样的文案，而连击 / 双发的重复
        /// 更是实打实的强度翻倍（两条「连击」= 两次追加进攻）。</para>
        /// </summary>
        public bool SameAs(EffectDef other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(other.HandlerId, handlerId, StringComparison.Ordinal)
                   && other.Aura == aura
                   && other.A == primary
                   && other.B == secondary
                   && other.C == cooldownAdjustment;
        }

        /// <summary>同一份画像的逐项相等（自测用）。</summary>
        public bool Matches(EffectSpec other)
        {
            if (other == null)
            {
                return false;
            }

            return other.trigger == trigger
                   && string.Equals(other.handlerId, handlerId, StringComparison.Ordinal)
                   && string.Equals(other.primaryKey, primaryKey, StringComparison.Ordinal)
                   && other.primary == primary
                   && other.secondary == secondary
                   && other.cooldownAdjustment == cooldownAdjustment
                   && other.aura == aura
                   && other.mandatory == mandatory
                   && other.targets == targets
                   && string.Equals(other.specialEvent, specialEvent, StringComparison.Ordinal)
                   && string.Equals(other.distinctTargetGroup, distinctTargetGroup, StringComparison.Ordinal)
                   && string.Equals(other.text, text, StringComparison.Ordinal)
                   && ConditionCount(other) == ConditionCount(this);
        }

        private static int ConditionCount(EffectSpec spec)
        {
            return spec.conditions == null ? 0 : spec.conditions.Length;
        }

        /// <summary>给人看的一行字（界面 / 日志）。</summary>
        public string Describe()
        {
            string body = string.IsNullOrEmpty(text) ? handlerId : text;
            return string.IsNullOrEmpty(sourceBaseId) ? body : body + "（来自 " + sourceBaseId + "）";
        }
    }
}
