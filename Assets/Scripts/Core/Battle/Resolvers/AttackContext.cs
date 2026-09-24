using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 效果归属的结算阶段。规则依据：`Docs/rules/01-规则基线.md` §8 的六步结算顺序。
    /// </summary>
    internal enum EffectStage
    {
        /// <summary>① 力量增益 / 双发 / 生命 / 手牌换力量 / 复制。</summary>
        Stage1Power = 1,

        /// <summary>② 防御侧（由防御牌自己的 β 效果驱动，不走本队列）。</summary>
        Stage2Defense = 2,

        /// <summary>③ 冷却改动 / 查看手牌。</summary>
        Stage3Cooldown = 3,

        /// <summary>④ 进冷却区时消费的标记（快速回填 / 未防御分支）。</summary>
        Stage4Marker = 4,

        /// <summary>⑤ 光环激活。</summary>
        Stage5Aura = 5,
    }

    /// <summary>把算子归到六步结算的哪一步 —— 这是「一份规则、一处实现」的落点。</summary>
    internal static class EffectClassifier
    {
        public static EffectStage Classify(EffectOp op)
        {
            switch (op)
            {
                case EffectOp.AtkPlus:
                case EffectOp.AtkPlusPerCooling:
                case EffectOp.AtkPlusPerHpLoss:
                case EffectOp.DoubleAtkBonus:
                case EffectOp.NoAtkBuff:
                case EffectOp.Combo:
                case EffectOp.Double:
                case EffectOp.CoolHandForAtk:
                case EffectOp.CoolHandForCombo:
                case EffectOp.HealMinusMax:
                case EffectOp.Copy:
                    return EffectStage.Stage1Power;

                case EffectOp.Haste:
                case EffectOp.Slow:
                // 地震的条件减速：与 Slow 同一阶段（③ 冷却改动）。敲门那道条件判断
                // 在 StartStage3Decision 里 —— 归类必须与 Slow 一致，
                // 落到 default（Stage1Power）会变成「静默不结算」。
                case EffectOp.SlowIfLastTwoHand:
                case EffectOp.HasteZone:
                case EffectOp.SlowZone:
                case EffectOp.SlowZoneBoth:
                case EffectOp.Refresh:
                case EffectOp.ResetCooldown:
                case EffectOp.RemoveFromGame:
                case EffectOp.HastePerHpLoss:
                case EffectOp.CoolHandForHaste:
                case EffectOp.LookAndCool:
                    return EffectStage.Stage3Cooldown;

                case EffectOp.QuickRefill:
                case EffectOp.CoolMinusIfUnblocked:
                case EffectOp.SlowIfUnblocked:
                case EffectOp.ExtraDamageIfUnblocked:
                    return EffectStage.Stage4Marker;

                case EffectOp.Aura:
                    return EffectStage.Stage5Aura;

                // 防御侧的 β 效果**不走进攻队列**（第 ② 步由防御牌自己驱动）——
                // 登记它只为了让「新算子忘了登记」这件事不再悄悄发生：
                // 落到 default 会被当成 Stage1Power，而 AddEffectToStage 对它是空操作，
                // 结果同样是「静默不结算」，但连归类都指错了方向。
                case EffectOp.DefPlus:
                case EffectOp.Guard:
                    return EffectStage.Stage2Defense;

                default:
                    return EffectStage.Stage1Power;
            }
        }
    }

    /// <summary>
    /// 单次进攻的运行期上下文。模仿的复制结果、光环加成、双发 / 连击标记都挂在这里，
    /// <strong>不写回卡面</strong>（规则 §6.2「不永久改造牌面」）。
    /// </summary>
    internal sealed class AttackContext
    {
        public int AttackerSeat;

        /// <summary>本次进攻牌。</summary>
        public CardInstance Card;

        /// <summary>基础力量（模仿复制成功后被改写）。</summary>
        public int BasePower;

        /// <summary>效果带来的额外进攻力量（翻倍前）。</summary>
        public int EffectBonus;

        /// <summary>光环带来的额外进攻力量。</summary>
        public int AuraBonus;

        /// <summary>获得的额外进攻力量翻倍。</summary>
        public bool DoubleAtkBonus;

        /// <summary>本牌进攻力量不可增加。</summary>
        public bool NoAtkBuff;

        public bool IsDouble;

        public bool HasCombo;

        public bool QuickRefill;

        public int CoolMinusIfUnblocked;

        public int SlowIfUnblocked;

        public int ExtraDamage;

        public int ExtraMaxHpCut;

        /// <summary>模仿复制到的卡（null = 未复制）。</summary>
        public CardDef CopySource;

        /// <summary>本段是否为「连击的追加进攻」（追加不重复触发冷却 −1）。</summary>
        public bool IsFollowUp;

        /// <summary>① 阶段待处理的效果队列。</summary>
        public readonly List<EffectDef> Stage1 = new List<EffectDef>();

        /// <summary>③ 阶段待处理的效果队列。</summary>
        public readonly List<EffectDef> Stage3 = new List<EffectDef>();

        /// <summary>最终进攻力量（= 基础 + 增量）。</summary>
        public int FinalPower
        {
            get { return BasePower + BonusPower; }
        }

        /// <summary>额外进攻力量合计（翻倍 / 禁增都已计入）。</summary>
        public int BonusPower
        {
            get
            {
                if (NoAtkBuff)
                {
                    return 0;
                }

                int raw = EffectBonus + AuraBonus;
                return DoubleAtkBonus ? raw * 2 : raw;
            }
        }
    }
}
