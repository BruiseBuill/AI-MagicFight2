using System;
using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>决策类型。见 `Docs/engineering/04-架构与接口.md` §4。</summary>
    public enum RequestKind
    {
        /// <summary>
        /// 进攻：出哪张牌（有牌时不可跳过）。<b>光环选项与出牌选项同在这一拍里</b>
        /// （2026-09-18：取消独立的光环选择环节，光环由玩家提前拖到判定区准备、随出牌一并提交）。
        /// </summary>
        ChooseAttackCard = 0,

        /// <summary>防御：放弃 / 出哪张（双发时为两张）—— 同样与光环选项同拍。</summary>
        ChooseDefense = 1,

        /// <summary>加速目标。</summary>
        ChooseHasteTarget = 2,

        /// <summary>减速目标。</summary>
        ChooseSlowTarget = 3,

        /// <summary>区域加速 / 减速：选某一方冷却区中的剩余冷却值 k。</summary>
        ChooseZoneValue = 4,

        /// <summary>立即冷却完成 / 重置冷却的目标。</summary>
        ChooseRefreshTarget = 5,

        /// <summary>把手牌送入冷却（可多选，可空）。</summary>
        ChooseCoolHandCards = 6,

        /// <summary>漩涡：永久移出游戏的目标。</summary>
        ChooseRemoveFromGame = 7,

        /// <summary>模仿：复制目标。</summary>
        ChooseCopyTarget = 8,

        /// <summary>开局 / 补牌后的替换。</summary>
        ChooseReplace = 9,

        /// <summary>同优先级效果的结算顺序。</summary>
        ResolveOrder = 10,

        ChooseCooldownEffects = 12,

        /// <summary>
        /// 查看对方一张手牌（雷云 / 狂躁蘑菇的第 2 个 α 效果，2026-09-21）。
        ///
        /// <para><b>为什么要一个决策</b>：卡面写的是「随机查看对方一张手牌」，
        /// 但玩家看到的是<b>一排牌背</b>；引擎自己抽一张等于把这段交互整段吞掉
        /// （用户 2026-09-21：要一个专门的界面，让玩家在牌背里点一张）。</para>
        ///
        /// <para><b>选项的 <see cref="Option.Label"/> 里绝不能出现牌名 / 力量</b>：
        /// 一旦带上，玩家就能挑走「自己想看的那张」，效果从「随机查看」变成
        /// 「定向查看」—— 那是另一张强度完全不同的卡。所以本拍只认序号，不看内容。</para>
        /// </summary>
        ChoosePeekCard = 11,
    }

    /// <summary>选项种类。</summary>
    public enum OptionKind
    {
        /// <summary>放弃 / 不执行（除 [强制] 外一律可选）。</summary>
        Skip = 0,

        /// <summary>打出一张牌。</summary>
        PlayCard = 1,

        /// <summary>消耗一枚光环指示物。</summary>
        UseAura = 2,

        /// <summary>选定一个目标牌。</summary>
        ChooseCard = 3,

        /// <summary>选定区域类的 k 值。</summary>
        ZoneValue = 4,

        /// <summary>结束多选。</summary>
        Done = 5,

        /// <summary>替换掉某张牌。</summary>
        Replace = 6,
    }

    /// <summary>一个合法选项。UI 只渲染 <see cref="Label"/>，AI 只读结构字段。</summary>
    public sealed class Option
    {
        public int EffectExecutionId;
        internal Option Copy() { return (Option)MemberwiseClone(); }
        /// <summary>选项序号（回填 <see cref="DecisionResponse"/> 用）。</summary>
        public int Index;

        public OptionKind Kind;

        /// <summary>展示文案。</summary>
        public string Label = string.Empty;

        /// <summary>主目标牌。</summary>
        public CardInstance Card;

        /// <summary>双发时的第二张牌。</summary>
        public CardInstance PairCard;

        /// <summary>本次要消耗的光环来源牌（在所有者冷却区中）。</summary>
        public CardInstance AuraSource;

        /// <summary>被消耗的光环类型。</summary>
        public AuraKind AuraKind;

        /// <summary>
        /// 被消耗的那枚指示物在<strong>剩余指示物里的序号</strong>（0 起；非光环选项为 −1）。
        ///
        /// <para><b>为什么要有它</b>：HudBuff 是「一枚指示物一个图标」，
        /// 玩家拖的是<b>某一个图标</b>；双光环的卡（af / aj）两枚同型，
        /// 只靠 (来源牌, 光环类型) 分不出拖的是哪一枚。
        /// 本字段与 <c>CardSnapshot.AuraTokensDetail</c> 的下标同口径。</para>
        /// </summary>
        public int AuraTokenIndex = -1;

        /// <summary>归属座位；−1 表示「双方」（区域类）。</summary>
        public int Seat = -1;

        /// <summary>数值：区域类 = k；其它视场景而定。</summary>
        public int Value;

        /// <summary>该选项能影响到的牌数（区域类的「数值集中度」，供 AI 与 UI 评估）。</summary>
        public int Count;

        public bool IsSkip
        {
            get { return Kind == OptionKind.Skip; }
        }

        public override string ToString()
        {
            return "[" + Index + "] " + Label;
        }
    }

    /// <summary>
    /// 决策请求。引擎推进到需要外部输入时就停下并抛出这个对象；
    /// AI 座位的 <c>Decide</c> 即时应答、玩家座位的由 UI 渲染后回填。
    /// </summary>
    public sealed class DecisionRequest
    {
        public long RequestId;
        public IReadOnlyList<int> EnemySeats = new int[0];
        public bool AurasPrepared;
        public bool IsEnemy(int seat)
        {
            for (int i = 0; i < EnemySeats.Count; i++) if (EnemySeats[i] == seat) return true;
            return false;
        }

        /// <summary>谁要决策。</summary>
        public int Seat;

        public RequestKind Kind;

        /// <summary>给 UI 的提示文案。</summary>
        public string Prompt = string.Empty;

        /// <summary>
        /// 给 UI 的<strong>短标题</strong>（可空）。用于「弹窗顶部大字」这类位置。
        ///
        /// <para><b>为什么由引擎给而不是 UI 猜</b>：同一个 <see cref="RequestKind"/> 可能由
        /// 好几张牌触发，界面必须看得出来是哪一张 —— 例如
        /// <see cref="RequestKind.ChooseCoolHandCards"/> 背后是磁暴 / 充能 / 电弧三张牌，
        /// 三者规则不同（磁暴每张 +3 力量、充能每张一次加速、电弧换连击）。
        /// 「这一拍是谁在结算」只有引擎知道（它手上才有正在生效的那张牌），
        /// UI 靠 <see cref="MaxSelect"/> 之类的数字去反推是在猜（磁暴与充能都是多选，猜不开）。
        /// 所以把「这一拍要做什么」这一个事实由引擎直接给出来，UI 只负责显示。</para>
        ///
        /// <para><b>写的是「效果」不是「卡名」</b>（2026-09-22 用户口径）：卡名对玩家没有信息量
        /// —— 看到「磁暴」还得自己回忆它能干什么。标题直接说结果：
        /// 「冷却·加力量」/「冷却·获加速」/「冷却·换连击」。</para>
        ///
        /// <para><b>⚠ 有长度预算</b>：这个短标题要落在弹窗顶部 title band 的平直段里，
        /// 那段只有约 257 px（见 <c>UiLayout.HandPickTitleWidth</c>）。字数超过 7 个
        /// TMP 就得缩到 19 号以下 —— 所以标题口径是「4 字效果 + 最多 3 字后缀」，
        /// 详细规则交给 <see cref="Prompt"/> 那一行去说。</para>
        /// </summary>
        public string Title = string.Empty;

        /// <summary>全部合法选项（含 Skip，除非 [强制]）。</summary>
        public IReadOnlyList<Option> Options = new Option[0];

        /// <summary>最少选择数（0 = 可以什么都不选）。</summary>
        public int MinSelect = 1;

        /// <summary>最多选择数。</summary>
        public int MaxSelect = 1;

        // ── 上下文（给 UI 显示、给自测做独立校验）────────────
        /// <summary>本次进攻的最终力量（仅防御相关决策有意义）。</summary>
        public int ContextPower;

        /// <summary>本次防御已主动消耗的力量光环总加值。</summary>
        public int ContextDefenseBonus;

        /// <summary>本次进攻是否双发。</summary>
        public bool ContextDouble;

        /// <summary>兼容旧快照字段；免疫直接结算，不再产生防御选牌决策。</summary>
        public bool ContextImmune;

        /// <summary>该决策是否不允许放弃（如「有牌时必须进攻」）。</summary>
        public bool ContextNoSkip;

        /// <summary>
        /// 本拍效果的<strong>极性</strong>：true = 冷却前进（加速 / 立即冷却完成，有利于施法者），
        /// false = 冷却倒退（减速 / 重置对方冷却，对对方不利）。
        ///
        /// <para>区域类（<see cref="RequestKind.ChooseZoneValue"/>）与冷却类
        /// （<see cref="RequestKind.ChooseRefreshTarget"/>）都靠它表达，
        /// AI 用它决定「该往自己身上使劲还是往对方身上使劲」（2026-09-20）。</para>
        /// </summary>
        public bool ContextHaste;

        public bool AllowsEmpty
        {
            get { return MinSelect <= 0; }
        }

        public int OptionCount
        {
            get { return Options == null ? 0 : Options.Count; }
        }

        /// <summary>取某个序号的选项；不存在返回 null。</summary>
        public Option Get(int index)
        {
            if (Options == null || index < 0 || index >= Options.Count)
            {
                return null;
            }

            return Options[index];
        }

        /// <summary>第一个 Skip 选项（没有则 null）。</summary>
        public Option SkipOption
        {
            get
            {
                if (Options == null)
                {
                    return null;
                }

                for (int i = 0; i < Options.Count; i++)
                {
                    if (Options[i].IsSkip)
                    {
                        return Options[i];
                    }
                }

                return null;
            }
        }

        public override string ToString()
        {
            return "Request(" + Kind + " seat" + Seat + " options=" + OptionCount + ")";
        }
    }

    /// <summary>外部回填的决策。</summary>
    public sealed class DecisionResponse
    {
        public long RequestId;
        public int Seat;

        /// <summary>选中的「主选择」选项序号（出哪张牌 / 区域 k 值 / 放弃；单选时长度为 1）。</summary>
        public int[] OptionIndices = new int[0];

        /// <summary>
        /// 本次一并提交的<strong>光环选项序号</strong>（玩家在判定区里「准备使用」的那几枚）。
        ///
        /// <para><b>为什么单开一条通道</b>：光环与出牌同在一个 <see cref="DecisionRequest.Options"/>
        /// 里（同一份序号空间），但它们回答的是两个问题 —— 「打哪张」和「顺带消耗哪几枚指示物」。
        /// 混在同一条 <see cref="OptionIndices"/> 里，<c>MaxSelect = 1</c> 的截断会把其中一件事吃掉。</para>
        ///
        /// <para>引擎不信任上层：只有 <see cref="RequestKind.ChooseAttackCard"/> /
        /// <see cref="RequestKind.ChooseDefense"/> 两拍会读它，其余一律忽略；
        /// 越界、重复、指向非光环项、来源已无余量的序号全部被丢掉（见
        /// <c>BattleEngine.PickAuras</c>）。</para>
        /// </summary>
        public int[] AuraOptionIndices = new int[0];

        /// <summary>
        /// 只更新「准备使用的光环」、<b>不提交主选择</b>（玩家还在挑牌）。
        ///
        /// <para>引擎收到之后不会消费这一拍，而是按新的准备集合把同一条决策重发一次 ——
        /// 因为防御牌的合法性取决于这份集合（够不够挡）。出牌那一刻才用
        /// <see cref="OptionIndices"/> 一起提交，此时 <c>AuraPrepOnly</c> 为 false。</para>
        ///
        /// <para>只对 <see cref="RequestKind.ChooseAttackCard"/> /
        /// <see cref="RequestKind.ChooseDefense"/> 有效，其余决策会被当作普通提交。</para>
        /// </summary>
        public bool AuraPrepOnly;

        public static DecisionResponse Skip(int seat)
        {
            return new DecisionResponse { Seat = seat, OptionIndices = new int[0] };
        }

        public static DecisionResponse Of(int seat, params int[] indices)
        {
            return new DecisionResponse
            {
                Seat = seat,
                OptionIndices = indices ?? new int[0],
            };
        }

        /// <summary>同时回填「主选择 + 准备使用的光环」。</summary>
        public static DecisionResponse WithAuras(int seat, int[] optionIndices, int[] auraIndices)
        {
            return new DecisionResponse
            {
                Seat = seat,
                OptionIndices = optionIndices ?? new int[0],
                AuraOptionIndices = auraIndices ?? new int[0],
            };
        }

        /// <summary>只报备「准备使用的光环」，请引擎按新集合重发这一拍。</summary>
        public static DecisionResponse PrepAuras(int seat, int[] auraIndices)
        {
            return new DecisionResponse
            {
                Seat = seat,
                OptionIndices = new int[0],
                AuraOptionIndices = auraIndices ?? new int[0],
                AuraPrepOnly = true,
            };
        }
    }
}
