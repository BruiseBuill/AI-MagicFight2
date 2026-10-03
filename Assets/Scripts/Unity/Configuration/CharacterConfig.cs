using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 角色配置上的<b>发牌口径预设</b>（2026-10-03）。真正的规则值是 Core 的
    /// <see cref="DealProfile"/>，这里只是一个「让你在 Inspector 里选一种」的开关。
    /// </summary>
    public enum DealPreset
    {
        /// <summary>不写口径 → 用引擎那份 <see cref="IDealPolicy"/>（= 人类老口径）。</summary>
        EngineDefault = 0,

        /// <summary>人类玩家：开局 6 张可换 3、第 2–3 回合各补 1。</summary>
        Human = 1,

        /// <summary>怪物：开局 8 张（= 手牌上限）、之后不补、开局不换牌。</summary>
        Monster = 2,

        /// <summary>自己填下面的六个数字。</summary>
        Custom = 3,
    }

    [CreateAssetMenu(menuName = "魔法乱斗/角色配置", fileName = "Character")]
    public sealed class CharacterConfig : ScriptableObject
    {
        public string characterId = "player.default";
        public string displayName = "你";
        public CharacterKind kind = CharacterKind.Player;
        [Min(1)] public int initialHp = 4;
        [Min(1)] public int maxHp = 4;
        [Min(1)] public int minimumCardCount = 8;
        public bool useAllCards = true;
        public string[] cardIds = new string[0];

        /// <summary>
        /// 这个角色用的<b>怪物 AI 档案名</b>（2026-10-03）—— 空串 = 通用
        /// <see cref="HeuristicAgent"/>，其它值由 <see cref="MonsterAgentFactory"/> 映射。
        ///
        /// <para>当前可用：<c>"berserker"</c>（永远高攻 · 只爱陨石 · 绝不打凝固）、
        /// <c>"trickster"</c>（永远连击 · 先打电弧 · 一次烧三张手牌）。
        /// 加新怪物 = 在 <see cref="MonsterAgentFactory"/> 里加一个 <c>case</c>。</para>
        ///
        /// <para>⚠ 只对 <see cref="ControlKind.Ai"/> 的座位有意义；玩家座位忽略它。</para>
        /// </summary>
        [Tooltip("怪物 AI 档案名（空 = 通用）。当前：berserker / trickster")]
        public string aiProfile = "";

        /// <summary>
        /// 这个角色用的<b>卡池资产</b>（2026-09-30 起；见 <see cref="CardPoolConfig"/>）。
        ///
        /// <para>填了就<b>以它为准</b>，下面的 <see cref="useAllCards"/> / <see cref="cardIds"/>
        /// 不再生效 —— 旧的单字段写法保留是为了不破坏已经存过值的资产
        /// （<c>Resources/Characters/DefaultPlayer.asset</c> 等），不是「两个来源都算」。</para>
        ///
        /// <para><b>⚠ 别两边各配一半</b>：那正是「卡池分离」之前三处各写一份清单的老问题。</para>
        /// </summary>
        [Tooltip("填了就以此卡池资产为准；留空才用下面的 useAllCards / cardIds。")]
        public CardPoolConfig cardPool;

        /// <summary>
        /// <b>这个角色自带的强化配方</b>（2026-10-02 · 多轴强化）——
        /// <b>怪物也能强化</b>这件事的落点。
        ///
        /// <para>玩家的强化册在存档里（<c>PlayerData.upgrades</c>，随存档走）；
        /// 怪物没有存档，它的强化属于<b>角色配置</b>（精英怪 / Boss 强化就是这一格）。
        /// 两者是同一个类型、同一套合成规则（<see cref="UpgradeBook.BuildCatalog"/>），
        /// 结算侧完全不知道区别 —— 一张牌被强化成什么样，只取决于「它挂在谁的册子上」。</para>
        ///
        /// <para><b>⚠ 这里填的是<b>基础 ID</b></b>（<c>a</c> 而不是 <c>a+</c>）：
        /// 和存档同口径，强化版永远是读的时候合成出来的。</para>
        ///
        /// <para>例：给「滚石冲击 f」加一条 <c>Power +2</c> 与一条 <c>Cooldown −1</c>；</para>
        /// </summary>
        [Tooltip("这个角色自带的强化（怪物 / 精英 / Boss 用）。baseId 填基础 ID（a，不是 a+）。")]
        public CardUpgradeRecord[] upgrades = new CardUpgradeRecord[0];

        /// <summary>
        /// <b>发牌口径</b>（2026-10-03）—— 开局抽几张、之后补不补、能换几张。
        ///
        /// <para><b>为什么一个角色一份而不是整局一份</b>：用户口径要求怪物<b>不等于</b>人类玩家
        /// （怪物开局直接抽满 8 张、之后一张都不再补）。做成整局一份就表达不了这件事。</para>
        ///
        /// <para><b>怪物一律选 <see cref="DealPreset.Monster"/></b>；
        /// 玩家资产保持 <see cref="DealPreset.EngineDefault"/> 即可（与旧行为逐字相同）。</para>
        /// </summary>
        [Header("发牌口径（怪物选 Monster）")]
        [Tooltip("EngineDefault = 不写口径（用引擎默认 = 人类老口径）；Human = 6 张可换 3 + 第 2–3 回合各补 1；\n"
                 + "Monster = 开局 8 张、之后不补、不换牌；Custom = 用下面六个数字。")]
        public DealPreset dealPreset = DealPreset.EngineDefault;

        [Tooltip("Custom 专用：开局手牌数。")]
        public int dealInitialHandSize = 8;

        [Tooltip("Custom 专用：开局可替换张数。")]
        public int dealInitialReplaceLimit = 0;

        [Tooltip("Custom 专用：从第几回合开始补牌（含）。0 = 不补。")]
        public int dealDrawFromTurn = 0;

        [Tooltip("Custom 专用：补到第几回合为止（含）。")]
        public int dealDrawToTurn = 0;

        [Tooltip("Custom 专用：补牌区间内每回合补几张。")]
        public int dealDrawPerTurn = 0;

        [Tooltip("Custom 专用：补牌当拍可替换几张。")]
        public int dealReplacePerDraw = 0;

        /// <summary>
        /// <b>这个角色自己的美术</b>（2026-10-03，可选）。
        ///
        /// <para>留空 → 沿用 <c>BattleArtLibrary.Monster</c> 那一份（当前唯一一份怪物美术，
        /// 也就是「所有怪长一样」）。填上就<b>以它为准</b> —— 这样将来做第二只怪时，
        /// 不用改任何代码、也不用碰美术映射表，只在这份怪物资产上换一组图。</para>
        ///
        /// <para>⚠ 素材仍然放在 <c>Assets/Art/</c> 下并由 <c>BattleArtLibrary</c> 统一导入；
        /// 这里只是「引用一份 CharacterSet」，不是第二套导入流程。</para>
        /// </summary>
        [Header("美术（勾上才用这里这份，否则共用 BattleArtLibrary 里那份怪物美术）")]
        [Tooltip("勾上 = 这只怪用下面那组动作；不勾 = 沿用 BattleArtLibrary.Monster。")]
        public bool useCustomArt;

        [Tooltip("这只怪自己的动作（勾上上一项才生效）。")]
        public BattleArtLibrary.CharacterSet art;

        [Header("能力 / 行为")]
        [Tooltip("角色能力兼怪物行为。例：第 3 回合自爆 = 一条 DamageOpponents（AmountSource=OwnHp，TriggerTurn=3）"
                 + " + 一条 SelfDestruct（TriggerTurn=3）。")]
        public CharacterAbilityConfig[] abilities = new CharacterAbilityConfig[0];

        public CharacterDefinition CreateDefinition(ICardCatalog catalog = null)
        {
            var definitions = new List<ICharacterAbilityDefinition>();
            foreach (CharacterAbilityConfig ability in abilities ?? new CharacterAbilityConfig[0])
            {
                if (ability == null) throw new ArgumentException("角色能力配置不能为空。");
                definitions.Add(ability.CreateDefinition());
            }

            // ⚠ 先把强化册叠到卡目录上，再去建卡池 —— 顺序反了的话
            //   `cardPool.CreatePool` 里的 `PreferUpgraded` 在目录里找不到 `a+`，
            //   强化版静默退回基础版（与 SaveStore.TryLoadCardPool 是同一条口径）。
            ICardCatalog effective = SaveStore.WithUpgrades(catalog, UpgradeBook.FromRecords(upgrades));
            CardPool pool = cardPool != null
                ? cardPool.CreatePool(effective)
                : (useAllCards ? CardPool.AllCards(effective) : new CardPool(cardIds, effective));
            return new CharacterDefinition(characterId, displayName, kind, initialHp, maxHp,
                minimumCardCount, pool, definitions, aiProfile, CreateDealProfile());
        }

        /// <summary>
        /// 把 Inspector 上的预设翻成 Core 的 <see cref="DealProfile"/>。
        /// <see cref="DealPreset.EngineDefault"/> 返回 null（= 用引擎默认），这样老资产的
        /// 行为与这一轮之前<b>逐字相同</b>。
        /// </summary>
        public DealProfile CreateDealProfile()
        {
            switch (dealPreset)
            {
                case DealPreset.Human:
                    return DealProfile.Human();

                case DealPreset.Monster:
                    return DealProfile.Monster();

                case DealPreset.Custom:
                    return new DealProfile(dealInitialHandSize, dealInitialReplaceLimit,
                        dealDrawFromTurn, dealDrawToTurn, dealDrawPerTurn, dealReplacePerDraw);

                default:
                    return null;
            }
        }
    }

    [Serializable]
    public sealed class CharacterAbilityConfig
    {
        public string id = "ability";
        public AbilityTrigger trigger;
        public CharacterAbilityOp operation;
        [Min(0)] public int amount = 1;
        [Tooltip("0 = 不限次数；力量加值为常驻查询，使用 0。")]
        [Min(0)] public int maxUses;

        [Tooltip("只在第 N 回合触发；0 = 不限。例：自爆怪填 3。")]
        [Min(0)] public int triggerTurn;

        [Tooltip("amount 的含义：Fixed = 用上面那个数；OwnHp / OwnMaxHp / MissingHp = 取自身生命。\n"
                 + "例：自爆伤害 = 自己当前生命 → 选 OwnHp（此时 amount 填 0）。")]
        public AbilityAmountSource amountSource = AbilityAmountSource.Fixed;

        public ICharacterAbilityDefinition CreateDefinition()
        {
            return new CharacterAbilityDefinition(id, trigger, operation, amount, maxUses, triggerTurn, amountSource);
        }
    }

    [Serializable]
    public sealed class BattleParticipantConfig
    {
        public CharacterConfig character;
        public ControlKind control;
        public int team;
    }
}
