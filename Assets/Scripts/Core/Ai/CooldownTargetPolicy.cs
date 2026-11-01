namespace MagicBrawl.Core
{
    /// <summary>
    /// 冷却类决策的<b>立场</b>口径（2026-10-31）——<b>加速只朝自己、减速只朝对方</b>。
    ///
    /// <para><b>为什么需要它（真实缺陷）</b>：加速 / 减速的目标横跨双方 ——
    /// <see cref="EffectContext.TargetSeats"/> 按 <c>Participants</c> 把两边冷却区的牌
    /// 一起列进 <c>Options</c>（玩家确实可以自由选）。而引擎会把同一拍里两条并列的冷却效果
    /// 合成<b>一个</b> <see cref="RequestKind.ChooseCooldownEffects"/> 决策
    /// （<see cref="EffectWindow"/> 的 <c>combined</c>，例如暴风雪 a =「加速」+「区域减速」、
    /// 海涌 q =「加速」+「区域加速」）。</para>
    ///
    /// <para>旧 AI 的 <c>Decide</c> 里没有这个 Kind 的分支，于是落进默认的
    /// <c>FirstNonSkip</c> —— <b>取选项列表里的第一个</b>。而选项是按
    /// <c>EffectSeats = { 施法者, 对方 }</c> 的顺序生成的，所以：</para>
    /// <list type="bullet">
    /// <item><b>区域减速</b>先出现的是<b>施法者自己</b>那一侧 → AI 把自己冷却区减速；</item>
    /// <item>自己冷却区空着时，<b>加速</b>选项只剩对方的牌 → AI 拿加速去帮对手。</item>
    /// </list>
    ///
    /// <para><b>口径</b>（用户 2026-10-31）：「加速一般是正面效果，即使自己没有牌能加速，
    /// 也不能给敌人加速。减速一般是负面效果，即使对方没有牌能被减速，也不能给自己减速。」
    /// 所以本类只做三件事：</para>
    /// <list type="number">
    /// <item>按 <see cref="Option.HasteOption"/> 判极性、按「这张牌 / 这一侧是谁的」判立场；</item>
    /// <item>在<b>有利</b>档里按原有偏好排序（加速挑最快回手的、减速挑力量最高的、区域挑最密的）；</item>
    /// <item>一步有利的都没有 → 交「跳过」；引擎不让跳过时，才退而求其次挑最不伤的那一个
    ///   （例如被强制减速、而对面冷却区是空的，只能减自己的）。</item>
    /// </list>
    ///
    /// <para>⚠ <b>只有 AI 走这里</b>：玩家的选项一个都没少、也没被过滤 ——
    /// 引擎给的合法目标仍然由玩家自己挑（铁律 3）。</para>
    /// </summary>
    internal static class CooldownTargetPolicy
    {
        /// <summary>档位权重：立场差一档 = 1000，任何「同档内偏好」都盖不过立场。</summary>
        private const int StanceWeight = 1000;

        /// <summary>同档内偏好的上限（剩余冷却 / 力量 / 匹配张数都远小于它）。</summary>
        private const int TieCap = 64;

        /// <summary>
        /// 从本拍选项里挑一个「立场正确」的目标。<paramref name="state"/> 可为 null
        /// （拿不到对局状态时，「双方一起」的区域减速按中性处理 —— 不擅自改成不做）。
        /// 返回 null = 本拍一个可判的选项都没有（调用方按「跳过」处理）。
        /// </summary>
        public static Option Pick(DecisionRequest req, BattleState state)
        {
            if (req == null || req.Options == null)
            {
                return null;
            }

            Option best = null;
            int bestRank = int.MinValue;
            int bestStance = -1;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option option = req.Options[i];
                if (option == null || option.IsSkip || option.AuraSource != null)
                {
                    continue;
                }

                bool card = option.Card != null;
                bool zone = option.Kind == OptionKind.ZoneValue;
                if (!card && !zone)
                {
                    continue;
                }

                int stance = StanceOf(option, req, state, option.HasteOption, card, zone);
                int rank = stance * StanceWeight + TieOf(option, card);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    bestStance = stance;
                    best = option;
                }
            }

            if (best == null)
            {
                return null;
            }

            // 一个「有利 / 中性」的都没有 → 宁可不做，也不做资敌（或自伤）的那一步。
            // ⚠ 判据必须看立场本身，不能看 rank —— 同档偏好会往 rank 上加正数，
            //    「立场 0 + 剩余冷却很小」会伪装成正分（2026-10-31 实测踩到）。
            if (bestStance <= 0 && req.SkipOption != null)
            {
                return req.SkipOption;
            }

            return best;
        }

        /// <summary>同档内的偏好（0…<see cref="TieCap"/>），只为「同立场里挑哪个」服务。</summary>
        private static int TieOf(Option option, bool card)
        {
            if (!card)
            {
                // 区域档：命中张数最多的那一档（与 2026-09-20 的口径一致）。
                return Clamp(option.Count, 0, TieCap);
            }

            // 加速：剩余冷却最小的先挑（它马上就回手，收益最直接）；
            // 减速：力量最高的先挑（拖住对面最能打的那张）。
            return option.HasteOption
                ? TieCap - Clamp(option.Card.RemainingCooldown, 0, TieCap)
                : Clamp(option.Card.EffectivePower, 0, TieCap);
        }

        /// <summary>立场：2 = 有利、1 = 中性（双方一起）、0 = 不利。</summary>
        private static int StanceOf(Option option, DecisionRequest req, BattleState state,
            bool haste, bool card, bool zone)
        {
            int seat = card ? option.Card.OwnerSeat : option.Seat;

            if (seat == req.Seat)
            {
                return haste ? 2 : 0;
            }

            if (req.IsEnemy(seat))
            {
                return haste ? 0 : 2;
            }

            if (seat == -1)
            {
                return BothStance(option, req, state, haste, zone);
            }

            return 1;
        }

        /// <summary>
        /// <c>Seat = -1</c> = 双方一起（目前只有雪崩 an 的 <see cref="EffectOp.SlowZoneBoth"/>）。
        ///
        /// <para>它不是「帮了谁」，而是<b>两边一起减速</b> —— 值不值得做，只能数两边的张数：
        /// 对面被打到的比自己多才划算。旧逻辑把 -1 一律当「有利」，于是「自己冷却区有 3 张、
        /// 对面 0 张」时 AI 照样放雪崩，纯粹的自我减速。</para>
        ///
        /// <para>拿不到 <paramref name="state"/>（简单 AI 未注入状态）时按中性处理 ——
        /// 保持改动之前的行为，不因为缺信息就凭空变保守。</para>
        /// </summary>
        private static int BothStance(Option option, DecisionRequest req, BattleState state, bool haste, bool zone)
        {
            if (!zone || state == null)
            {
                return 1;
            }

            if (haste)
            {
                return 1;   // 目前没有「双方一起加速」的卡；真有的话两边都受益，按中性排
            }

            int own = CountMatching(state, req.Seat, option.Value);
            int enemy = 0;
            for (int i = 0; i < req.EnemySeats.Count; i++)
            {
                enemy += CountMatching(state, req.EnemySeats[i], option.Value);
            }

            if (enemy > own)
            {
                return 2;
            }

            return enemy == own ? 1 : 0;
        }

        /// <summary>某个座位冷却区里「剩余冷却 = <paramref name="remaining"/>」的牌有几张。</summary>
        private static int CountMatching(BattleState state, int seat, int remaining)
        {
            if (state == null || seat < 0 || seat >= state.Players.Count)
            {
                return 0;
            }

            PlayerState player = state.Of(seat);
            int count = 0;
            for (int i = 0; i < player.CoolingZone.Count; i++)
            {
                if (player.CoolingZone[i].RemainingCooldown == remaining)
                {
                    count++;
                }
            }

            return count;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
