using System;
using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>两种玩法</b>（2026-10-28 · 大改 —— 去掉冒险与强化之后，游戏只剩这两条路）。
    /// </summary>
    public enum BattleModeKind
    {
        /// <summary><b>标准战斗</b>：双方各从全卡表随机抽 8 张，直接开打。</summary>
        Standard = 0,

        /// <summary><b>自定义战斗</b>：开战前玩家自己挑双方的 8 张。</summary>
        Custom = 1,
    }

    /// <summary>
    /// <b>这一局怎么开</b>（2026-10-28）—— 主菜单 → 牌组编辑 → 战斗三个场景之间的
    /// <b>唯一</b>传参通道。
    ///
    /// <para><b>为什么是静态类</b>：决策点（主菜单点「标准战斗」/ 牌组编辑点「开战」）
    /// 与消费点（<see cref="BattleDriver"/> 组装对局）在<b>不同场景</b>里，换场景会把
    /// 场景对象全部销毁。与工程里其它跨场景状态的处置一致（静态字段、不随场景销毁）。</para>
    ///
    /// <para><b>⚠ 它<b>不</b>落盘</b>：这是「这一局用什么牌」的一次性参数，不是存档。
    /// 退出游戏 / 退出 Play 就归零 —— 下次进来回到标准战斗的随机口径。</para>
    ///
    /// <para><b>⚠ 标准战斗的牌组<b>不</b>存在这里</b>：它由对局种子派生
    /// （见 <see cref="BattleDriver"/> 的 <c>RandomDeck</c>），这样
    /// 「重试 = 同一颗种子 → 同一副 8 张」自动成立，不需要在这里另存一份快照。</para>
    /// </summary>
    public static class BattleLaunch
    {
        /// <summary>双方各自的牌数（= 引擎手牌上限 <see cref="BattleState.HandLimit"/>）。</summary>
        public const int DeckSize = BattleState.HandLimit;

        /// <summary>这一次要开的玩法。默认标准战斗（单场景调试也走它）。</summary>
        public static BattleModeKind Mode = BattleModeKind.Standard;

        private static string[] _playerIds;
        private static string[] _monsterIds;

        /// <summary>玩家侧的 8 张（<b>基础 ID</b>；只在自定义战斗里有值）。</summary>
        public static string[] PlayerCardIds { get { return _playerIds; } }

        /// <summary>怪物侧的 8 张（同上）。</summary>
        public static string[] MonsterCardIds { get { return _monsterIds; } }

        /// <summary>
        /// 现在是不是一套<b>可用的</b>自定义牌组（模式对 + 两边都正好 8 张）。
        /// <c>false</c> 时战斗侧一律退回「按种子随机」。
        /// </summary>
        public static bool HasCustomDecks
        {
            get
            {
                return Mode == BattleModeKind.Custom
                       && _playerIds != null && _playerIds.Length == DeckSize
                       && _monsterIds != null && _monsterIds.Length == DeckSize;
            }
        }

        /// <summary>选「标准战斗」：清掉上一次的自定义牌组。</summary>
        public static void SetStandard()
        {
            Mode = BattleModeKind.Standard;
            _playerIds = null;
            _monsterIds = null;
        }

        /// <summary>
        /// 选「自定义战斗」并把两套牌记下来。
        ///
        /// <para><b>校验口径</b>（失败时什么都不改，返回 <c>false</c> 并把原因写进
        /// <paramref name="error"/>）：</para>
        /// <list type="number">
        /// <item>两边都正好 <see cref="DeckSize"/> 张；</item>
        /// <item>每一张都在卡表里（按 ID 精确匹配）；</item>
        /// <item>同一侧不重复（<see cref="CardPool"/> 本身就是「每种卡最多一张」）。</item>
        /// </list>
        /// </summary>
        public static bool TrySetCustom(IReadOnlyList<string> playerIds,
            IReadOnlyList<string> monsterIds, out string error)
        {
            if (!Validate(playerIds, "你的牌组", out error)) return false;
            if (!Validate(monsterIds, "对手的牌组", out error)) return false;

            _playerIds = Copy(playerIds);
            _monsterIds = Copy(monsterIds);
            Mode = BattleModeKind.Custom;
            return true;
        }

        /// <summary>回到「什么都没选」的初始态（单纯为了干净，行为上等价于 <see cref="SetStandard"/>）。</summary>
        public static void Clear()
        {
            SetStandard();
        }

        /// <summary>
        /// 单个牌组的合法性校验（也供牌组编辑界面在「开战」键上实时判定用 ——
        /// <b>判据只有这一处</b>，界面别自己再数一遍张数）。
        /// </summary>
        public static bool Validate(IReadOnlyList<string> ids, string label, out string error)
        {
            error = null;
            if (ids == null)
            {
                error = label + "还没有选牌。";
                return false;
            }

            if (ids.Count != DeckSize)
            {
                error = label + "需要正好 " + DeckSize + " 张，当前 " + ids.Count + " 张。";
                return false;
            }

            // 2026-10-30：与战斗侧同一条口径 —— 经典模式下 ao..ar 不在卡表里，
            // 自定义牌组也就不该选到它们（否则「选了却抽不到」会在开局当场抛异常）。
            ICardCatalog catalog = GameSettings.Filter(CardCatalog.Builtin());
            IReadOnlyList<CardDef> all = catalog.All;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    error = label + "第 " + (i + 1) + " 格是空的。";
                    return false;
                }

                if (!seen.Add(CardIds.BaseOf(id)))
                {
                    error = label + "里有重复的牌。";
                    return false;
                }

                bool found = false;
                for (int k = 0; k < all.Count; k++)
                {
                    if (all[k] != null && string.Equals(all[k].Id, id, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    error = label + "里有一张卡表里没有的牌：" + id;
                    return false;
                }
            }

            return true;
        }

        private static string[] Copy(IReadOnlyList<string> ids)
        {
            var copy = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) copy[i] = ids[i];
            return copy;
        }
    }
}
