using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>敌人的智力档位</b>（2026-10-30 · 用户口径）。
    /// </summary>
    public enum AiDifficulty
    {
        /// <summary>
        /// <b>简单</b>：只会「按当前力量从大到小」出牌 —— 不看流派、不用光环、不换开局手牌。
        /// 实现就是现成的 <see cref="SimpleAiAgent"/>（它的进攻选择恰好就是这一条）。
        /// </summary>
        Easy = 0,

        /// <summary><b>标准</b>：四流派启发式 + 怪物档案（<see cref="MonsterAgentFactory"/>），即大改之前的行为。</summary>
        Standard = 1,
    }

    /// <summary>
    /// <b>对局阵容</b>（2026-10-30）：两个座位各自由谁操作。
    ///
    /// <para><b>为什么是「阵容」而不是「模式」</b>：引擎侧的 <c>IBattleMode</c> 管的是
    /// 人数 / 行动顺序 / 敌我关系 / 胜负；这里只管<b>谁坐在哪个座位上按按钮</b>。
    /// 两件事分开之后，将来接网络对战时只需换掉「座位 → 控制器」那一层，
    /// 引擎与规则一行都不用动。</para>
    /// </summary>
    public enum MatchLineup
    {
        /// <summary>座位 0 = 本地玩家，座位 1 = AI（默认，大改之前的行为）。</summary>
        PlayerVsAi = 0,

        /// <summary>两个座位都是 AI —— 观战 / 自测用，全程不需要点击。</summary>
        AiVsAi = 1,

        /// <summary>
        /// 两个座位都是玩家（人类）。
        ///
        /// <para><b>单机上</b>是「同屏热座」：轮到谁，界面视角就切到谁的座位
        /// （见 <c>BattleUi.HandleDecision</c>）。</para>
        ///
        /// <para><b>网络上</b>（未来）：两个客户端各自把 <see cref="LocalSeat"/> 设成
        /// 自己的座位即可 —— 每个客户端只看到自己那一侧的手牌，这正是本档位要预留的形状。</para>
        /// </summary>
        PlayerVsPlayer = 2,
    }

    /// <summary>
    /// <b>跨场景的全局设置</b>（2026-10-30）—— 主菜单里的三项设置住在这一处。
    ///
    /// <para><b>为什么是静态类</b>：设置在主菜单改、在战斗场景消费，换场景会把场景对象
    /// 全部销毁。与 <see cref="BattleLaunch"/> 同一条理由（静态字段 + 不随场景销毁）。</para>
    ///
    /// <para><b>为什么落 PlayerPrefs</b>：游戏的存档系统（SaveStore / 5 个存档位）
    /// 已在 2026-10-28 随冒险模式一起删掉；剩下这点开关没必要再拉一套存档回来。
    /// PlayerPrefs 就是「上次选了什么，下次进来还是什么」这个量级。</para>
    ///
    /// <para>⚠ <b>只影响下一局</b>：对局中途改这里不会改变已经在跑的那一局
    /// （<c>BattleDriver.StartBattle</c> 只在开局那一刻读它）。战斗里也没有这两个入口。</para>
    /// </summary>
    public static class GameSettings
    {
        /// <summary>
        /// <b>经典模式只留前 N 张</b>（2026-10-30 · 用户口径「前 40 张卡是经典的老卡」）。
        ///
        /// <para>判据是 <see cref="CardDef.Index"/> &lt; 本常量 —— 也就是卡表顺序，
        /// 与卡面文件名 <c>Card_01_a_暴风雪 …</c> 的编号完全一致。</para>
        ///
        /// <para>⚠ 当前卡表 44 张（a–ar），排除的是最后 4 张：
        /// <c>ao 冰霜核心</c> / <c>ap 火炬</c> / <c>aq 雷云</c> / <c>ar 地刺</c>。
        /// 注意 <c>ad 吞食</c> 虽然也是 2026-10-29 改版新增的，但它的序号是 29，
        /// <b>在 40 之内</b> —— 按「前 40 张」这条字面口径它属于经典池。</para>
        /// </summary>
        public const int ClassicCardLimit = 40;

        private const string KeyClassic = "MagicBrawl.Settings.ClassicOnly";
        private const string KeyDifficulty = "MagicBrawl.Settings.AiDifficulty";
        private const string KeyLineup = "MagicBrawl.Settings.MatchLineup";
        private const string KeyLocalSeat = "MagicBrawl.Settings.LocalSeat";

        private static bool _loaded;
        private static bool _classicOnly;
        private static AiDifficulty _difficulty = AiDifficulty.Standard;
        private static MatchLineup _lineup = MatchLineup.PlayerVsAi;
        private static int _localSeat;

        // ══════════════════════════════════════════════════════
        //  读 / 写
        // ══════════════════════════════════════════════════════

        /// <summary>从 PlayerPrefs 读一次（幂等；<see cref="Reload"/> 可强制重读）。</summary>
        public static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _classicOnly = PlayerPrefs.GetInt(KeyClassic, 0) != 0;
            _difficulty = (AiDifficulty)Clamp(PlayerPrefs.GetInt(KeyDifficulty, (int)AiDifficulty.Standard),
                0, 1);
            _lineup = (MatchLineup)Clamp(PlayerPrefs.GetInt(KeyLineup, (int)MatchLineup.PlayerVsAi), 0, 2);
            _localSeat = PlayerPrefs.GetInt(KeyLocalSeat, 0) == 1 ? 1 : 0;
        }

        /// <summary>强制重读（设置面板每次打开都会调一次，防止别处改了 PlayerPrefs 不同步）。</summary>
        public static void Reload()
        {
            _loaded = false;
            Load();
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>
        /// <b>经典模式</b>：开 = 只用前 <see cref="ClassicCardLimit"/> 张（a–an）。
        /// </summary>
        public static bool ClassicOnly
        {
            get { Load(); return _classicOnly; }
            set
            {
                Load();
                if (_classicOnly == value)
                {
                    return;
                }

                _classicOnly = value;
                PlayerPrefs.SetInt(KeyClassic, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary><b>敌人智力</b>：简单 / 标准。</summary>
        public static AiDifficulty Difficulty
        {
            get { Load(); return _difficulty; }
            set
            {
                Load();
                if (_difficulty == value)
                {
                    return;
                }

                _difficulty = value;
                PlayerPrefs.SetInt(KeyDifficulty, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary><b>对局阵容</b>：谁坐哪一边。</summary>
        public static MatchLineup Lineup
        {
            get { Load(); return _lineup; }
            set
            {
                Load();
                if (_lineup == value)
                {
                    return;
                }

                _lineup = value;
                PlayerPrefs.SetInt(KeyLineup, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// <b>本机坐哪一个座位</b>（0 / 1）。
        ///
        /// <para>默认 0。设 1 时整块界面会把「座位 1」当成自己那一侧来渲染
        /// （手牌在下、对手在上）—— 这一条是给将来网络对战留的：两个客户端各设各的座位。</para>
        /// </summary>
        public static int LocalSeat
        {
            get { Load(); return _localSeat; }
            set
            {
                Load();
                int next = value == 1 ? 1 : 0;
                if (_localSeat == next)
                {
                    return;
                }

                _localSeat = next;
                PlayerPrefs.SetInt(KeyLocalSeat, next);
                PlayerPrefs.Save();
            }
        }

        // ══════════════════════════════════════════════════════
        //  对局接线（BattleDriver 读这几条）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>某个座位由谁操作</b> —— 座位控制方式的<b>唯一判据</b>。
        ///
        /// <para>⚠ 这套设置会<b>覆盖</b> <c>BattleCanvas.prefab</c> 上
        /// <c>_participants[i].control</c> 里写的那一份：名单表达的是「谁能上场」，
        /// 而「这一局谁操作」是玩家在主菜单选的。默认档位 <see cref="MatchLineup.PlayerVsAi"/>
        /// 与名单里现有的 0=Human / 1=Ai 完全吻合，所以不填设置时行为与之前逐字相同。</para>
        /// </summary>
        public static ControlKind ControlOf(int seat)
        {
            Load();

            switch (_lineup)
            {
                case MatchLineup.AiVsAi:
                    return ControlKind.Ai;

                case MatchLineup.PlayerVsPlayer:
                    return ControlKind.Human;

                default:
                    return seat == _localSeat ? ControlKind.Human : ControlKind.Ai;
            }
        }

        /// <summary>这个座位是不是人类操作（= 界面要不要等他点）。</summary>
        public static bool IsHumanSeat(int seat)
        {
            return ControlOf(seat) == ControlKind.Human;
        }

        // ══════════════════════════════════════════════════════
        //  经典模式：卡表过滤
        // ══════════════════════════════════════════════════════

        /// <summary>这张牌在不在当前卡池口径里。</summary>
        public static bool Allows(CardDef card)
        {
            Load();
            if (card == null)
            {
                return false;
            }

            return !_classicOnly || card.Index < ClassicCardLimit;
        }

        /// <summary>按当前口径裁剪一张卡表（关闭经典模式时原样返回同一个对象）。</summary>
        public static ICardCatalog Filter(ICardCatalog catalog)
        {
            Load();
            if (catalog == null || !_classicOnly)
            {
                return catalog;
            }

            return new ClassicCatalog(catalog, ClassicCardLimit);
        }

        // ══════════════════════════════════════════════════════
        //  文案（设置面板与提示行共用，避免两处各写一份）
        // ══════════════════════════════════════════════════════

        /// <summary>阵容的中文名。</summary>
        public static string LineupName(MatchLineup lineup)
        {
            switch (lineup)
            {
                case MatchLineup.AiVsAi:
                    return "AI 对 AI（观战）";

                case MatchLineup.PlayerVsPlayer:
                    return "玩家对玩家";

                default:
                    return "玩家对 AI";
            }
        }

        /// <summary>难度的中文名。</summary>
        public static string DifficultyName(AiDifficulty difficulty)
        {
            return difficulty == AiDifficulty.Easy ? "简单" : "标准";
        }

        /// <summary>经典模式的中文名。</summary>
        public static string ClassicName(bool classicOnly)
        {
            return classicOnly ? "开（仅前 " + ClassicCardLimit + " 张）" : "关（全部卡表）";
        }

        /// <summary>
        /// 只留前 <paramref name="limit"/> 张的卡表视图。
        ///
        /// <para><b>⚠ 为什么 <see cref="Get"/> 要转发给内层而不是在裁剪后的表里查</b>：
        /// 角色的卡池资产（<c>Resources/Pools/MonsterPool_*.asset</c>）里手写着卡 ID，
        /// 其中 <c>ao</c> / <c>aq</c> 正好落在 40 之后。若这里也把它们判成「不存在」，
        /// <see cref="CardPool"/> 的构造会当场抛 <c>Unknown card ID</c> ——
        /// 表现是「一开经典模式，进战斗直接崩」，而根因只是某只怪的卡池里写了新卡。
        /// 让 <see cref="Get"/> 照旧能取到，那些牌在 <see cref="CardPool.Resolve"/>
        /// （遍历 <see cref="All"/>）时自然就不会出现 —— 正好是想要的降级。</para>
        /// </summary>
        private sealed class ClassicCatalog : ICardCatalog
        {
            private readonly ICardCatalog _inner;
            private readonly List<CardDef> _all;

            public ClassicCatalog(ICardCatalog inner, int limit)
            {
                _inner = inner;
                _all = new List<CardDef>();
                IReadOnlyList<CardDef> source = inner.All;
                for (int i = 0; i < source.Count; i++)
                {
                    CardDef card = source[i];
                    if (card != null && card.Index < limit)
                    {
                        _all.Add(card);
                    }
                }
            }

            public IReadOnlyList<CardDef> All { get { return _all; } }

            public CardDef Get(string id) { return _inner.Get(id); }
        }
    }
}
