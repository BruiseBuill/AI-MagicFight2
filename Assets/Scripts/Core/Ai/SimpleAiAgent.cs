using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 最简 AI（`Docs/engineering/03-工程规划.md` §6）。策略刻意保持粗糙 —— 它只是「能跑完整局」的基线，
    /// 用来给内核做压力测试，强弱由后续的 <c>HeuristicAgent</c> 负责。
    ///
    /// <list type="bullet">
    /// <item>进攻：有牌就打，选当前力量最高的一张。</item>
    /// <item>防御：能挡就挡，选刚好够的最小牌（省大牌）；挡不住则放弃。</item>
    /// <item>加速：选己方剩余冷却最小的牌。减速：选对方力量最高的冷却牌。</item>
    /// <item>区域类：在**对我有利的那一方**里选当前数值最集中的一档。</item>
    /// <item>冷却类：立即冷却完成选自己的牌；重置冷却本来就只给对方的牌。</item>
    /// <item>光环：默认不使用（<see cref="UseAuras"/> 留开关）。</item>
    /// <item>其余：取第一个合法选项。</item>
    /// </list>
    /// </summary>
    public sealed class SimpleAiAgent : IAgent
    {
        /// <summary>是否使用光环。默认关（规划 §6：本阶段不使用）。</summary>
        public bool UseAuras { get; set; }

        /// <summary>是否在开局 / 补牌后替换弱牌。默认开，用来覆盖这条代码路径。</summary>
        public bool ReplaceWeakCards { get; set; }

        /// <summary>送入冷却换增益时，最多冷却几张手牌。</summary>
        public int MaxCoolHandCards { get; set; }

        /// <summary>
        /// 「查看对方手牌」的盲选种子（雷云 / 狂躁蘑菇，2026-09-21）。
        ///
        /// <para><b>为什么需要它</b>：这一拍引擎给的选项<b>刻意不带任何信息</b>
        /// （只有序号和「第 N 张」的文案），AI 无从择优 —— 取第一个就等于「永远翻对方
        /// 最先抽到的那张」，与卡面写的「随机查看」概率口径对不上。</para>
        ///
        /// <para><b>为什么不用 <c>System.Random</c></b>：本类必须完全可复现
        /// （万局回归要的是「同一个种子必然走出同一局」），而 <c>System.Random</c>
        /// 的实现细节不保证跨运行库一致。这里用一个自实现的线性同余，
        /// 每次都推进 —— 所以同一个 agent 实例连做几次盲选，翻的不会总是同一张。</para>
        /// </summary>
        public int BlindPickSeed { get; set; }

        /// <summary>盲选用的线性同余状态（&lt;0 = 还没初始化过，下次用 <see cref="BlindPickSeed"/> 起头）。</summary>
        private int _blindState = -1;

        public SimpleAiAgent()
        {
            UseAuras = false;
            ReplaceWeakCards = true;
            MaxCoolHandCards = 1;
            BlindPickSeed = 20260921;
        }

        public DecisionResponse Decide(DecisionRequest request)
        {
            if (request == null || request.Options == null || request.Options.Count == 0)
            {
                return DecisionResponse.Skip(request == null ? 0 : request.Seat);
            }

            switch (request.Kind)
            {
                case RequestKind.ChooseAttackCard:
                    return CommitAuras(request, PickStrongestAttack(request),
                        UseAuras ? AuraIndices(request, false) : null);

                case RequestKind.ChooseDefense:
                    return DecideDefense(request);

                case RequestKind.ChooseHasteTarget:
                    return Of(request, PickSmallestCooldown(request));

                case RequestKind.ChooseSlowTarget:
                    return Of(request, PickStrongestOpponentCooling(request));

                case RequestKind.ChooseZoneValue:
                    return Of(request, PickDensestZone(request));

                case RequestKind.ChooseRefreshTarget:
                    return Of(request, PickRefreshTarget(request));

                case RequestKind.ChooseCoolHandCards:
                    return PickCoolHandCards(request);

                case RequestKind.ChoosePeekCard:
                    return Of(request, PickBlindCard(request));

                case RequestKind.ChooseReplace:
                    return PickReplaces(request);

                default:
                    return Of(request, FirstNonSkip(request));
            }
        }

        // ── 光环（2026-09-18 起与出牌同拍提交）───────────────────

        /// <summary>
        /// 防御：免疫优先，其次「先把力量光环报备给引擎、再挑牌」。
        ///
        /// <para><b>为什么要分两步</b>：防御牌的合法性取决于已准备的光环加值
        /// （引擎按它算「这张牌够不够挡」）。所以先把光环报备一次
        /// （<see cref="DecisionResponse.PrepAuras"/>，不消费这一拍），
        /// 引擎会用新预算重发本拍，AI 第二次才真正挑牌 —— 否则它永远看不到
        /// 「要用光环补值才够挡」的那些牌。</para>
        /// </summary>
        private DecisionResponse DecideDefense(DecisionRequest req)
        {
            if (!UseAuras)
            {
                return Of(req, PickCheapestBlock(req));
            }

            int immune = FirstIndex(req, true);
            if (immune >= 0)
            {
                // 免疫 = 消耗即整个攻击被免疫、不用交牌（规则 §6.5）
                return DecisionResponse.WithAuras(req.Seat, null, new[] { immune });
            }

            if (!req.AurasPrepared)
            {
                int[] prep = AuraIndices(req, true);
                if (prep.Length > 0)
                {
                    return DecisionResponse.PrepAuras(req.Seat, prep);
                }
            }

            // 防御这边只带「力量加值类」：免疫已在上面单独处理掉了。
            return CommitAuras(req, PickCheapestBlock(req), AuraIndices(req, true));
        }

        /// <summary>用 <paramref name="option"/> 当主选择，并把 <paramref name="auras"/> 一并带上。</summary>
        private static DecisionResponse CommitAuras(DecisionRequest req, Option option, int[] auras)
        {
            if (option == null)
            {
                return DecisionResponse.WithAuras(req.Seat, null, auras);
            }

            return DecisionResponse.WithAuras(req.Seat, new[] { option.Index }, auras);
        }

        /// <summary>本拍光环选项的序号（<paramref name="powerOnly"/> 时只要力量加值类）。</summary>
        private static int[] AuraIndices(DecisionRequest req, bool powerOnly)
        {
            var list = new List<int>();
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (o.AuraSource == null || o.AuraKind == AuraKind.None)
                {
                    continue;
                }

                if (powerOnly && !AuraResolver.IsPowerBonus(o.AuraKind))
                {
                    continue;
                }

                list.Add(o.Index);
            }

            return list.ToArray();
        }

        /// <summary>第一个「免疫类」光环选项的序号（没有返回 −1）。</summary>
        private static int FirstIndex(DecisionRequest req, bool immune)
        {
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (o.AuraSource != null && AuraResolver.IsImmune(o.AuraKind) == immune
                    && o.AuraKind != AuraKind.None)
                {
                    return o.Index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 出牌选项与光环选项现在同在一个 <c>Options</c> 里，而光环选项也带 <c>Card</c>
        /// （= 它的来源牌）。所有「扫一遍选项找牌」的地方都必须用这个判据把光环排除掉，
        /// 否则会把徽标当成一张能打的牌。
        /// </summary>
        private static bool IsCardOption(Option o)
        {
            return o != null && !o.IsSkip && o.AuraSource == null && o.Card != null;
        }

        // ── 具体策略 ────────────────────────────────────────────

        /// <summary>进攻：力量最高的一张。</summary>
        private Option PickStrongestAttack(DecisionRequest req)
        {
            Option best = null;
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o))
                {
                    continue;
                }

                if (best == null || o.Card.EffectivePower > best.Card.EffectivePower)
                {
                    best = o;
                }
            }

            return best;
        }

        /// <summary>防御：刚好够的最小牌（省大牌）。双发时比两张的力量合计。</summary>
        private Option PickCheapestBlock(DecisionRequest req)
        {
            Option best = null;
            int bestCost = int.MaxValue;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o))
                {
                    continue;
                }

                int cost = o.Card.EffectivePower;
                if (o.PairCard != null)
                {
                    cost += o.PairCard.EffectivePower;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = o;
                }
            }

            // 挡不住 → 放弃（Skip）
            return best ?? req.SkipOption;
        }

        /// <summary>加速：己方剩余冷却最小的牌（让它尽快回手）。没有己方冷却牌则放弃。</summary>
        private Option PickSmallestCooldown(DecisionRequest req)
        {
            Option best = null;
            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || o.Card.OwnerSeat != req.Seat)
                {
                    continue;   // 加速只给自己用 —— 加速对方的牌等于资敌
                }

                if (best == null || o.Card.RemainingCooldown < best.Card.RemainingCooldown)
                {
                    best = o;
                }
            }

            return best ?? req.SkipOption;
        }

        /// <summary>减速：对方力量最高的冷却牌。</summary>
        private Option PickStrongestOpponentCooling(DecisionRequest req)
        {
            Option best = null;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || !req.IsEnemy(o.Card.OwnerSeat))
                {
                    continue;
                }

                if (best == null || o.Card.EffectivePower > best.Card.EffectivePower)
                {
                    best = o;
                }
            }

            return best ?? req.SkipOption;
        }

        /// <summary>
        /// 区域类：<b>只在对我有利的那一方</b>里挑匹配张数最多的一档
        /// （区域加速偏向自己的冷却区、区域减速偏向对方的冷却区）。
        ///
        /// <para><b>2026-09-20 修正</b>：旧打分是 <c>张数 × 2 + (有利 ? 1 : 0)</c>，
        /// 那 1 分被 2 倍张数轻易盖过 —— 对面 3 张的档（6 分）压过自己 2 张的档（5 分），
        /// AI 会当场把「区域加速」放给对手。用户口径是「加速一律给自己、减速一律给对方」，
        /// 所以改成<b>先按立场过滤、再在有利档里比密度</b>；一张都不占优就放弃
        /// （宁可不做，也不做资敌的那一步）。</para>
        ///
        /// <para>立场按 <see cref="Option.Seat"/> 判：施法者自己 = 有利、对方 = 不利；
        /// <c>Seat == -1</c>（双方一起，见雪崩）算有利 —— 它同时作用于两边，
        /// 不存在「选了它就是帮对手」这一说。极性取自 <see cref="DecisionRequest.ContextHaste"/>。</para>
        /// </summary>
        private Option PickDensestZone(DecisionRequest req)
        {
            Option best = null;
            int bestScore = -1;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (o.Kind != OptionKind.ZoneValue)
                {
                    continue;
                }

                bool favorable = o.Seat == -1
                                 || (req.ContextHaste ? o.Seat == req.Seat : req.IsEnemy(o.Seat));

                if (!favorable || o.Count <= bestScore)
                {
                    continue;
                }

                bestScore = o.Count;
                best = o;
            }

            return best ?? req.SkipOption;
        }

        /// <summary>
        /// 冷却类目标（立即冷却完成 / 重置对方冷却）。
        ///
        /// <para><b>同一拍里混着两种语义</b>：水刃的「使一张法术立即冷却完成」对自己有利，
        /// 寒流的「重置对方一个法术的冷却时间」是给对方添堵，两者共用
        /// <see cref="RequestKind.ChooseRefreshTarget"/>。极性由
        /// <see cref="DecisionRequest.ContextHaste"/> 给出（true = 冷却前进）。</para>
        ///
        /// <para>不按极性过滤的话会落进默认分支的 <c>FirstNonSkip</c> —— 那是「取选项列表里第一个」，
        /// 而选项是按座位顺序生成的，AI（座位 1）会排到玩家（座位 0）的牌后面，
        /// 于是它拿水刃去把<b>玩家</b>的牌冷却完。</para>
        /// </summary>
        private Option PickRefreshTarget(DecisionRequest req)
        {
            Option best = null;

            for (int i = 0; i < req.Options.Count; i++)
            {
                Option o = req.Options[i];
                if (!IsCardOption(o) || !(req.ContextHaste ? o.Card.OwnerSeat == req.Seat : req.IsEnemy(o.Card.OwnerSeat)))
                {
                    continue;
                }

                // 剩余冷却最小的先挑：立即冷却完成之后它马上就回手，收益最直接
                if (best == null || o.Card.RemainingCooldown < best.Card.RemainingCooldown)
                {
                    best = o;
                }
            }

            return best ?? req.SkipOption;
        }

        /// <summary>把手牌送入冷却换增益：挑力量最低的那张，避免浪费大牌。</summary>
        private DecisionResponse PickCoolHandCards(DecisionRequest req)
        {
            int take = MaxCoolHandCards;
            if (take <= 0)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            var indices = new List<int>();
            var used = new List<Option>();

            while (indices.Count < take && indices.Count < req.Options.Count)
            {
                Option weakest = null;
                for (int i = 0; i < req.Options.Count; i++)
                {
                    Option o = req.Options[i];
                    if (!IsCardOption(o) || used.Contains(o))
                    {
                        continue;
                    }

                    if (weakest == null || o.Card.EffectivePower < weakest.Card.EffectivePower)
                    {
                        weakest = o;
                    }
                }

                if (weakest == null)
                {
                    break;
                }

                used.Add(weakest);
                indices.Add(weakest.Index);
            }

            return new DecisionResponse { Seat = req.Seat, OptionIndices = indices.ToArray() };
        }

        /// <summary>替换：换掉力量 ≤1 的弱牌，至多到上限。</summary>
        private DecisionResponse PickReplaces(DecisionRequest req)        {
            if (!ReplaceWeakCards)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            var indices = new List<int>();
            for (int i = 0; i < req.Options.Count && indices.Count < req.MaxSelect; i++)
            {
                Option o = req.Options[i];
                if (o.Kind != OptionKind.Replace || o.Card == null)
                {
                    continue;
                }

                if (o.Card.EffectivePower <= 1)
                {
                    indices.Add(o.Index);
                }
            }

            return new DecisionResponse { Seat = req.Seat, OptionIndices = indices.ToArray() };
        }

        /// <summary>
        /// 查看对方手牌：<b>盲选</b>一张（雷云 / 狂躁蘑菇）。
        ///
        /// <para>这一拍没有可优化的东西 —— 引擎给的选项只有序号，牌名 / 力量都在引擎手里
        /// （见 <see cref="RequestKind.ChoosePeekCard"/> 的注释：文案带内容会让「随机查看」
        /// 退化成「定向查看」）。所以取第一个是错的、择优是做不到的，只剩随机。</para>
        ///
        /// <para>选项里可能混进 Skip（当前实现不会，但引擎以后若给「放弃」的口子，
        /// 这里也不该把 Skip 当成一张牌翻）—— <see cref="IsCardOption"/> 已把它排掉。</para>
        /// </summary>
        private Option PickBlindCard(DecisionRequest req)
        {
            var pool = new List<Option>();
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (IsCardOption(req.Options[i]))
                {
                    pool.Add(req.Options[i]);
                }
            }

            if (pool.Count == 0)
            {
                return FirstNonSkip(req);
            }

            if (_blindState < 0)
            {
                _blindState = BlindPickSeed;
            }

            // 数值质量不重要（只是打散），要紧的是「完全确定、每次推进」
            unchecked
            {
                _blindState = _blindState * 1103515245 + 12345;
            }

            int pick = (int)((uint)_blindState % (uint)pool.Count);
            return pool[pick];
        }

        private static Option FirstNonSkip(DecisionRequest req)
        {
            for (int i = 0; i < req.Options.Count; i++)
            {
                if (!req.Options[i].IsSkip)
                {
                    return req.Options[i];
                }
            }

            return req.SkipOption;
        }

        private static DecisionResponse Of(DecisionRequest req, Option option)
        {
            if (option == null)
            {
                return DecisionResponse.Skip(req.Seat);
            }

            return DecisionResponse.Of(req.Seat, option.Index);
        }
    }
}
