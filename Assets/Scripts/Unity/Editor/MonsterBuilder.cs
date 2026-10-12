using System;
using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>怪物资产构建器</b>（2026-10-03；2026-10-05 起连同<b>卡池</b>一起生成）—— 一只怪 = 一份
    /// <see cref="CharacterConfig"/> 资产（放 <c>Assets/Resources/Monsters/</c>）
    /// + 一份自己的 <see cref="CardPoolConfig"/> 卡池资产（放 <c>Assets/Resources/Pools/</c>）。
    ///
    /// <para><b>为什么是「资产」而不是「代码里的一只怪」</b>：用户口径要的是
    /// 「我自己来调这个怪物的卡池 / 血量 / 种类」。血量、AI 档案、强化、行为、发牌口径写在
    /// 角色资产上，<b>卡池单独一份资产</b>（2026-10-05：用户要「怪物卡池在非运行状态下配好、
    /// 战斗中只读卡池随机发牌」）—— 改怪就只是打开这两份资产改几个数，不碰代码、不碰 Prefab。</para>
    ///
    /// <para><b>「缺失才建」而不是「每次覆盖」</b>：构建器跑第二遍时如果<b>重新写一遍</b>资产，
    /// 你在 Inspector 里手调过的东西（血量、卡池内容、新加的一条行为）就全没了 ——
    /// 那是本项目已经踩过的坑（手改被构建器冲掉）。所以这里只在<b>文件不存在</b>时建；
    /// 想推倒重来请用下面那个显式的「重置」菜单（它会明确告诉你它会覆盖）。
    /// 老资产缺的那一处卡池引用由 <see cref="PointPool"/> 单独补（只补引用，不动别的）。</para>
    ///
    /// <para><b>本批 6 只</b>（2026-10-06）：自爆傀儡 / 基础傀儡 + 四种流派各一只
    /// （高攻 · 烈焰 / 双发 · 荆棘 / 连击 · 雷霆 / 减速 · 冰霜）—— 后四只各配 8 张卡池，
    /// 由 <c>MonsterAgentFactory</c> 的 <c>power</c> / <c>double</c> / <c>combo</c> / <c>slow</c>
    /// 四个档案强制走对应流派。</para>
    ///
    /// <para><b>2026-10-09 追加第 7 只 <c>Monster_Boss</c>（终点专用 / 精英）</b>：
    /// 地图上走到 Boss 那一格时，战斗改用这一只（路由在 <c>BattleDriver._bossConfig</c>）。
    /// 它<b>与那六只不同</b>：4 血而不是 1 血、血量不被本构建器同步（<c>SyncHp = false</c>）、
    /// 卡池留成"全卡表占位"等用户自己填。详见 <see cref="Specs"/> 最后一项的注释。</para>
    ///
    /// <para><b>接线只按 Prefab 改</b>：<c>BattleCanvas.prefab</c> 在场景里是
    /// <b>PrefabInstance</b>（见 <c>SampleScene.unity</c>），改 Prefab 会自动同步 ——
    /// 与 WitchUiBuilder 那种「场景里建再 SaveAsPrefabAsset」的拷贝式构建器不同。
    /// ⚠ 那只「本场用哪只怪」的引用（<c>_monsterConfig</c>）<b>只在它为空时</b>才会被接上，
    /// 多只怪之后你在 Inspector 里挑过的那一只不会被构建器换回第一只。</para>
    /// </summary>
    public static class MonsterBuilder
    {
        private const string CanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";
        private const string Folder = "Assets/Resources/Monsters";

        /// <summary>
        /// 终点专用的那一只的文件名（2026-10-09）。它在 <see cref="Specs"/> 里<b>最后一项</b>，
        /// 并且要单独接进 <c>BattleDriver._bossConfig</c>（另外六只走 <c>_monsterConfig</c>）。
        /// </summary>
        private const string BossFileName = "Monster_Boss";

        /// <summary>本批怪物的定义。加一只怪 = 在这里加一行。</summary>
        private sealed class MonsterSpec
        {
            public string FileName;
            public string CharacterId;
            public string DisplayName;

            /// <summary>入场 / 上限血量（本批一律 <see cref="TestHp"/>）。见 <see cref="TestHp"/> 的说明。</summary>
            public int Hp;

            /// <summary>
            /// 跑这个构建器时<b>要不要把血量覆盖成 <see cref="Hp"/></b>（2026-10-09）。
            ///
            /// <para>默认 <c>true</c> —— 那六只测试怪的血量由 <see cref="TestHp"/> 统一管，
            /// 用户口径是「目前所有的 AI 都 1 血」，改回去只有一个旋钮。</para>
            ///
            /// <para><b>⚠ 精英（<c>Monster_Boss</c>）必须是 <c>false</c></b>：它的血量是
            /// <b>内容</b>、归用户自己调（跟卡组一样），每次跑构建器都把它拉回一个常量，
            /// 正是「手改被构建器冲掉」那条老坑。</para>
            /// </summary>
            public bool SyncHp = true;
            public string AiProfile;
            public CharacterAbilityConfig[] Abilities;

            /// <summary>
            /// 这只怪自己的<b>卡池资产</b>文件名（不含扩展名，放 <c>Resources/Pools/</c>）。
            ///
            /// <para><b>为什么一只怪一份</b>（2026-10-05 · 用户口径「我需要能改怪物的卡池」）：
            /// 在此之前怪物走的是 <c>useAllCards = true</c>（= 全 45 张，与玩家同一份）——
            /// 「这只怪能打哪些牌」这件事<b>没有任何地方能配</b>。做成一份独立资产之后，
            /// 改怪只需打开它自己的卡池改勾选；想让两只怪共用一份，
            /// 把它们的 <c>cardPool</c> 指到同一个资产即可。</para>
            ///
            /// <para>⚠ <b>战斗侧不需要任何改动</b>：<see cref="CharacterConfig.CreateDefinition"/>
            /// 本来就先看 <c>cardPool</c>，而引擎抽牌只按 <see cref="CardPool"/> 走 ——
            /// 所谓「战斗时只是读取卡池随机发牌」正是既有行为，这一轮只是把卡池<b>摆到资产上</b>。</para>
            /// </summary>
            public string PoolFileName;

            /// <summary>卡池资产上给人看的名字。</summary>
            public string PoolDisplayName;

            /// <summary>卡池内容；<c>null</c> / 空 = 全卡表（与改造前逐字一致）。</summary>
            public string[] PoolCardIds;

            public string Note;
        }

        /// <summary>
        /// ⚠ 怪物卡池的<b>张数下限</b> —— 怪物开局要一次抽满 8 张（<c>DealProfile.Monster</c>），
        /// 而 <see cref="CharacterDefinition"/> 的构造器会按角色的
        /// <c>minimumCardCount</c> 校验卡池。配少了不是「少发几张」，是<b>进战斗当场抛异常</b>，
        /// 所以这里在生成时就把话说在前面。
        /// </summary>
        private const int MinPoolCards = 8;

        /// <summary>
        /// <b>测试期血量</b>（2026-10-06 用户口径：「为了便于测试，目前所有的 AI 都设定为只有一点血」）。
        ///
        /// <para>一只怪 1 血 = 玩家打中一下（或它自己踩到负面效果）就结束 —— 目的是
        /// <b>快速过一遍每只怪的攻击逻辑</b>，不用陪它打完 4 血的长局。
        /// 它写在这里而不是每只怪各写一遍，是为了「改回去」只有一个旋钮：
        /// 把这里改成 4，重跑菜单，六只怪一起恢复常规血量。</para>
        ///
        /// <para>⚠ <b>它会被 <see cref="PointHp"/> 主动同步到已存在的资产上</b>
        /// （与 <c>cardPool</c> 那种「只补空位」不同）—— 因为用户明确要求「目前所有的 AI
        /// 都是 1 血」，而六个资产都已经存在、靠「缺失才建」一个都改不到。
        /// 代价是：在 Inspector 里手调过的血量，下次跑菜单会被拉回这里。
        /// 想永久改血 → 改这个常量。</para>
        /// </summary>
        private const int TestHp = 1;

        /// <summary>
        /// <b>精英（Boss）的血量</b>（2026-10-09）—— 与那六只测试怪的 1 血不同：
        /// 终点那一战是<b>唯一一场要真打的</b>，1 血会把它变成「点一下就过」。
        ///
        /// <para>取 4 = <c>BattleState.InitialHp</c>（玩家自己的初始生命），也就是「和你一样能挨四下」
        /// 这一个量级。⚠ 这一格<b>只在建资产那一刻</b>写进去（<c>SyncHp = false</c>），
        /// 之后改血请直接在 <c>Assets/Resources/Monsters/Monster_Boss.asset</c> 上调 ——
        /// 重跑本构建器不会把它拉回来。</para>
        /// </summary>
        private const int BossHp = 4;

        private static readonly MonsterSpec[] Specs =
        {
            new MonsterSpec
            {
                FileName = "Monster_Bomber",
                CharacterId = "monster.bomber",
                DisplayName = "自爆傀儡",
                Hp = TestHp,
                AiProfile = "",
                Abilities = new[]
                {
                    new CharacterAbilityConfig
                    {
                        id = "bomber.damage",
                        trigger = AbilityTrigger.TurnStarted,
                        operation = CharacterAbilityOp.DamageOpponents,
                        amount = 0,
                        maxUses = 0,
                        triggerTurn = 3,
                        amountSource = AbilityAmountSource.OwnHp,
                    },
                    new CharacterAbilityConfig
                    {
                        id = "bomber.die",
                        trigger = AbilityTrigger.TurnStarted,
                        operation = CharacterAbilityOp.SelfDestruct,
                        amount = 0,
                        maxUses = 0,
                        triggerTurn = 3,
                        amountSource = AbilityAmountSource.Fixed,
                    },
                },
                PoolFileName = "MonsterPool_Bomber",
                PoolDisplayName = "自爆傀儡 · 卡池",
                PoolCardIds = null,
                Note = "第 3 回合不出牌 → 强制伤害 = 自己当前生命 → 自身归零",
            },

            new MonsterSpec
            {
                FileName = "Monster_Basic",
                CharacterId = "monster.basic",
                DisplayName = "基础傀儡",
                // 用户 2026-10-05 口径：「增加一种基础的怪，4 点血，没有自爆效果」。
                // 它身上的 abilities 是**空数组**（不是 null）—— 空 = 这条路径上确实没有行为，
                // null 只会让人分不清「没配」与「配了个空」。
                Hp = TestHp,
                AiProfile = "",
                Abilities = new CharacterAbilityConfig[0],
                PoolFileName = "MonsterPool_Basic",
                PoolDisplayName = "基础傀儡 · 卡池",
                PoolCardIds = null,
                Note = "最普通的一只怪：无任何回合行为（对照组 / 常规遭遇）",
            },

            // ── 2026-10-06：四种流派各一只（四种攻击逻辑各对应一类敌人）────────
            // 卡池一律 **8 张**（= 怪物开局抽满的手牌数，见 DealProfile.Monster）——
            // 所以「卡池」就是「它这局的手牌」，配的牌决定它每一回合能做什么。
            new MonsterSpec
            {
                FileName = "Monster_Power",
                CharacterId = "monster.power",
                DisplayName = "烈焰傀儡",
                Hp = TestHp,
                AiProfile = MonsterAgentFactory.Power,
                Abilities = new CharacterAbilityConfig[0],
                PoolFileName = "MonsterPool_Power",
                PoolDisplayName = "烈焰傀儡 · 卡池（高攻）",
                // 高攻逻辑的档位顺序：力量 ≥8 的连击 → 带进攻光环的卡 → 力量 ≥7 → 加速 → 按力量。
                // 所以卡池 = 1 张高攻连击（闪电）+ 3 张带进攻光环的卡（爆燃 6 / 火灾 8 / 淬火 7）
                //          + 4 张力量 ≥7 的普通牌（陨石 9 / 滚石冲击 9 / 地震 8 / 寒流 7）。
                // 8 张里力量最低的一张也有 6 —— 这就是「高攻」的全部含义。
                PoolCardIds = new[] { "j", "ai", "aj", "ag", "g", "f", "i", "d" },
                Note = "高攻：堆力量硬推（力量 6–9 ×8）",
            },

            new MonsterSpec
            {
                FileName = "Monster_Double",
                CharacterId = "monster.double",
                DisplayName = "荆棘傀儡",
                Hp = TestHp,
                AiProfile = MonsterAgentFactory.Double,
                Abilities = new CharacterAbilityConfig[0],
                PoolFileName = "MonsterPool_Double",
                PoolDisplayName = "荆棘傀儡 · 卡池（双发）",
                // 双发逻辑的档位顺序：防御光环卡（区域减速牌除外）→ 双发卡 → 加速 → 按力量。
                // 卡池 = 全部 4 张双发卡（荆棘 t / 飞叶连击 u / 藤蔓 v / 狂躁蘑菇 w）
                //        + 3 张防御光环卡（冰封铠甲 af / 冷冻核心 as / 烈焰斗篷 ah）
                //        + 1 张加速卡（湍流 ab，加速 ×2）。
                // 4 张双发里 3 张带「对方无法防御时」的额外收益 —— 正好呼应
                // 「双发不吃防御修正时会很痛」这件事（见 HeuristicAgent.ShouldDefendDouble）。
                PoolCardIds = new[] { "t", "u", "v", "w", "af", "as", "ah", "ab" },
                Note = "双发：交双发逼对方交两张牌（全部 4 张双发 + 3 张防御光环）",
            },

            new MonsterSpec
            {
                FileName = "Monster_Combo",
                CharacterId = "monster.combo",
                DisplayName = "雷霆傀儡",
                Hp = TestHp,
                AiProfile = MonsterAgentFactory.Combo,
                Abilities = new CharacterAbilityConfig[0],
                PoolFileName = "MonsterPool_Combo",
                PoolDisplayName = "雷霆傀儡 · 卡池（连击）",
                // 连击逻辑的档位顺序：连击卡 → 区域加速 → 加速 → 按力量。
                // 卡池 = 全部 7 张连击卡（闪电 j / 电弧 k / 磁暴 l / 雷鸣 m / 引雷 n /
                //          击穿 ap / 闪电球 ar）+ 1 张区域加速（海涌 q，同时是加速卡）。
                // ⚠ 不放「过载 o」：它是回血卡（连击判定里被排除），且 1 血时
                //   HealMinusMax 会削自己的生命上限 —— 1 血怪打它等于自杀。
                PoolCardIds = new[] { "j", "k", "l", "m", "n", "ap", "ar", "q" },
                Note = "连击：靠连击 / 区域加速打多次（全部 7 张连击卡）",
            },

            new MonsterSpec
            {
                FileName = "Monster_Slow",
                CharacterId = "monster.slow",
                DisplayName = "冰霜傀儡",
                Hp = TestHp,
                AiProfile = MonsterAgentFactory.Slow,
                Abilities = new CharacterAbilityConfig[0],
                PoolFileName = "MonsterPool_Slow",
                PoolDisplayName = "冰霜傀儡 · 卡池（减速）",
                // 减速逻辑的档位顺序：区域减速（须满足开启条件：对方冷却 ≥3 且手牌 ≤3）
                //   → 防御光环卡 → 连击 → 双发 → 加速 → 按力量。
                // 卡池 = 3 张区域减速（暴风雪 a / 冰风暴 b / 雪崩 an）
                //        + 3 张普通减速（凝固 c / 雷云 ad / 地震 i 的条件减速）
                //        + 1 张「减速 + 防御光环」（冷冻核心 as）+ 1 张防御光环（冰封铠甲 af）。
                // ⚠ 条件不满足时区域减速会掉到最后一档 —— 那时它只是一张普通牌，
                //   这是用户 2026-10-03 的明确口径（「使用之前必须先检查」）。
                PoolCardIds = new[] { "a", "b", "an", "c", "ad", "i", "as", "af" },
                Note = "减速：区域减速为核心拖节奏（开启条件不满足时按普通牌打）",
            },

            // ── 2026-10-09：终点（Boss 那一格）专用的一只 ────────────────────
            //
            //  用户口径：「最后的一个事件注定是精英战役，你需要配置一个精英，
            //  然后我来亲自给这个精英写卡组」。
            //
            //  所以这一只按「框架配好、内容留白」建：
            //   · 血量 = BossHp（4），且 SyncHp = false（改血不会被构建器拉回）；
            //   · AI 档案留空 = 通用 HeuristicAgent（与「怪物按流派打分」同一套脑子）；
            //   · 行为留空（空数组 = 这条路径上确实没有行为，不是「没配」）；
            //   · **卡池留空 = useAllCards = true**（全 45 张）—— 那是**占位**，
            //     等用户打开 Pools/MonsterPool_Boss.asset 取消勾选再填 cardIds。
            //     ⚠ 不能留成「空卡池」：怪物开局要一次抽满 8 张（DealProfile.Monster），
            //     卡池不足 8 张不是"少发几张"，是进战斗当场抛异常（见 MinPoolCards）。
            //
            //  ⚠ 它是**最后一项**：Wire 只把 Specs[0]（自爆傀儡）接进 _monsterConfig，
            //    把精英接进 _bossConfig 的那一段另外走（见 Run 里的 WireBoss 调用）。
            new MonsterSpec
            {
                FileName = "Monster_Boss",
                CharacterId = "monster.boss",
                DisplayName = "精英",
                Hp = BossHp,
                SyncHp = false,
                AiProfile = "",
                Abilities = new CharacterAbilityConfig[0],
                PoolFileName = "MonsterPool_Boss",
                PoolDisplayName = "精英 · 卡池（待填）",
                PoolCardIds = null,
                Note = "终点专用：4 血、无回合行为、卡池待用户填写（现在是全卡表占位）",
            },
        };

        // ══════════════════════════════════════════════════════
        //  菜单
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/怪物 · 生成怪物资产 + 接进战斗（缺失才建）", priority = 60)]
        public static void BuildMissing()
        {
            Run(overwrite: false);
        }

        [MenuItem("魔法乱斗/怪物 · 重置怪物资产（会覆盖你在 Inspector 里的手改）", priority = 61)]
        public static void ResetAll()
        {
            if (!EditorUtility.DisplayDialog("重置怪物资产",
                    "这会用构建器里的定义覆盖 " + Specs.Length + " 份怪物资产，\n"
                    + "你在 Inspector 里手调过的血量 / 卡池 / 行为都会丢失。\n\n确定继续？",
                    "覆盖", "取消"))
            {
                return;
            }

            Run(overwrite: true);
        }

        // ══════════════════════════════════════════════════════
        //  主流程
        // ══════════════════════════════════════════════════════

        private static void Run(bool overwrite)
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("请退出 Play 后再构建。");
            }

            EnsureFolder();
            EnsurePoolFolder();

            var created = new List<string>();
            var kept = new List<string>();
            var createdPools = new List<string>();
            var keptPools = new List<string>();
            var linked = new List<string>();
            var linkedHp = new List<string>();
            var log = new StringBuilder();

            CharacterConfig first = null;
            CharacterConfig boss = null;

            for (int i = 0; i < Specs.Length; i++)
            {
                MonsterSpec spec = Specs[i];
                if (spec.PoolCardIds != null && spec.PoolCardIds.Length > 0
                    && spec.PoolCardIds.Length < MinPoolCards)
                {
                    Debug.LogWarning("[MonsterBuilder] " + spec.FileName + " 的卡池只配了 "
                                     + spec.PoolCardIds.Length + " 张（少于 " + MinPoolCards
                                     + "）—— 怪物开局要一次抽满 8 张，进战斗会构造失败。");
                }

                CardPoolConfig pool = EnsurePool(spec, createdPools, keptPools);

                string path = Folder + "/" + spec.FileName + ".asset";
                CharacterConfig asset = AssetDatabase.LoadAssetAtPath<CharacterConfig>(path);

                if (asset == null || overwrite)
                {
                    if (asset != null)
                    {
                        AssetDatabase.DeleteAsset(path);
                    }

                    asset = ScriptableObject.CreateInstance<CharacterConfig>();
                    Apply(asset, spec, pool);
                    AssetDatabase.CreateAsset(asset, path);
                    created.Add(spec.FileName + "（" + spec.DisplayName + "）");
                }
                else
                {
                    kept.Add(spec.FileName);

                    // 增量补线（2026-10-05）：本批之前建的怪资产身上 cardPool 还是空的 ——
                    // 只补这一处引用，行为 / 美术 / 发牌口径一律不碰（保护 Inspector 手改）。
                    if (PointPool(asset, pool, log))
                    {
                        linked.Add(spec.FileName);
                    }

                    // 血量同步（2026-10-06）：与 cardPool 不同，这里是**主动覆盖** ——
                    // 测试期要求所有 AI 都是 1 血，而老资产的 3/4 血是「缺失才建」改不到的。
                    // 见 TestHp 的说明；改回来只有一个旋钮（那个常量）。
                    //
                    // ⚠ 2026-10-09：精英那一只显式关掉它（SyncHp = false）——
                    //   它的血量是内容、归用户自己调（与卡组同一口径）。
                    if (spec.SyncHp && PointHp(asset, spec, log))
                    {
                        linkedHp.Add(spec.FileName);
                    }
                }

                if (first == null)
                {
                    first = asset;
                }

                if (spec.FileName == BossFileName)
                {
                    boss = asset;
                }
            }

            AssetDatabase.SaveAssets();

            bool wired = Wire(first, "_monsterConfig");
            bool bossWired = Wire(boss, "_bossConfig");

            var report = new StringBuilder();
            report.AppendLine("[MonsterBuilder] 怪物：新建 " + created.Count + " 份、保留 " + kept.Count + " 份；"
                              + "卡池：新建 " + createdPools.Count + " 份、沿用 " + keptPools.Count + " 份；"
                              + (wired ? "已把第一只怪接到 BattleCanvas。" : "没改 BattleCanvas 的怪物格。"));
            report.AppendLine("  终点格（_bossConfig）："
                              + (bossWired ? "已接上 " + BossFileName + "。" : "没改（已指向别的怪或资产缺失）。"));
            for (int i = 0; i < created.Count; i++) report.AppendLine("  · 新建怪 " + created[i]);
            for (int i = 0; i < kept.Count; i++) report.AppendLine("  · 保留怪 " + kept[i] + "（想重置用「怪物 · 重置怪物资产」菜单）");
            for (int i = 0; i < createdPools.Count; i++) report.AppendLine("  · 新建卡池 " + createdPools[i]);
            for (int i = 0; i < keptPools.Count; i++) report.AppendLine("  · 沿用卡池 " + keptPools[i]);
            for (int i = 0; i < linked.Count; i++) report.AppendLine("  · 补接卡池引用 " + linked[i]);
            for (int i = 0; i < linkedHp.Count; i++) report.AppendLine("  · 同步血量 " + linkedHp[i] + " → " + TestHp + " 血");
            report.Append(log);
            report.AppendLine("[MonsterBuilder] ==== 完成 ====");
            Debug.Log(report.ToString());
        }

        private static void Apply(CharacterConfig asset, MonsterSpec spec, CardPoolConfig pool)
        {
            asset.characterId = spec.CharacterId;
            asset.displayName = spec.DisplayName;
            asset.kind = CharacterKind.Monster;
            asset.initialHp = spec.Hp;
            asset.maxHp = spec.Hp;
            asset.minimumCardCount = MinPoolCards;

            // 卡池资产优先（CharacterConfig.cardPool 的既有口径）：
            // 接上了就把 useAllCards / cardIds 让位 —— 两个来源都算等于谁也不知道
            // 怪物到底拿的是哪一份清单，而那正是「卡池分离」之前三处各写一份的老问题。
            asset.cardPool = pool;
            asset.useAllCards = pool == null;
            asset.cardIds = new string[0];

            asset.aiProfile = spec.AiProfile ?? string.Empty;
            asset.upgrades = new CardUpgradeRecord[0];
            asset.abilities = spec.Abilities ?? new CharacterAbilityConfig[0];
            // 美术：本批怪共用 BattleArtLibrary 里那份（不勾自定义），
            // 将来要单独换图时在资产上勾上 useCustomArt 再填动作即可。
            asset.useCustomArt = false;

            // ⚠ 怪物一律用怪物口径（开局 8 张、之后不补、不换牌）——
            //   这正是「怪物不像人类玩家」这条规则的落点。
            asset.dealPreset = DealPreset.Monster;
        }

        /// <summary>
        /// 取（缺失则建）这只怪自己的<b>卡池资产</b>（2026-10-05）。
        ///
        /// <para><b>缺失才建、在位就沿用</b>：卡池是<b>内容</b>，玩家 / 策划会直接改资产 ——
        /// 每次跑构建器都按代码重写，等于把编辑器里的改动静默抹掉
        /// （与 <see cref="CardPoolBuilder"/> 同一条口径，见铁律 16）。</para>
        ///
        /// <para>默认内容 = <c>useAllCards = true</c>（全 45 张）—— 与改造前怪物「全卡」
        /// 的行为<b>逐字一致</b>，所以这次接线不会悄悄改变任何一场对局；
        /// 想限定这只怪能打哪些牌，打开这份资产取消勾选再填 <c>cardIds</c> 即可。</para>
        /// </summary>
        private static CardPoolConfig EnsurePool(MonsterSpec spec, List<string> createdPools, List<string> keptPools)
        {
            if (string.IsNullOrEmpty(spec.PoolFileName))
            {
                return null;
            }

            string path = CardPoolConfig.PoolFolder + "/" + spec.PoolFileName + ".asset";
            CardPoolConfig pool = AssetDatabase.LoadAssetAtPath<CardPoolConfig>(path);
            if (pool != null)
            {
                keptPools.Add(spec.PoolFileName);
                return pool;
            }

            pool = ScriptableObject.CreateInstance<CardPoolConfig>();
            pool.poolId = spec.CharacterId + ".pool";
            pool.displayName = string.IsNullOrEmpty(spec.PoolDisplayName) ? spec.PoolFileName : spec.PoolDisplayName;
            pool.preferUpgraded = true;
            if (spec.PoolCardIds == null || spec.PoolCardIds.Length == 0)
            {
                pool.useAllCards = true;
                pool.cardIds = new string[0];
            }
            else
            {
                pool.useAllCards = false;
                pool.SetIds(spec.PoolCardIds);
            }

            AssetDatabase.CreateAsset(pool, path);
            createdPools.Add(spec.PoolFileName);
            return pool;
        }

        /// <summary>
        /// 把一只<b>已存在</b>的怪资产接到它的卡池上（只在它还是空的时候接）。
        ///
        /// <para>为什么要这一段：本批之前建的怪（<c>Monster_Bomber</c>）身上
        /// <c>cardPool</c> 是空的 —— 而「缺失才建」的规矩不允许覆盖它，
        /// 于是那只怪会永远拿不到卡池。这里只补<b>这一处引用</b>，
        /// 它的血量 / 行为 / 美术 / 发牌口径全部原样保留。</para>
        /// </summary>
        private static bool PointPool(CharacterConfig asset, CardPoolConfig pool, StringBuilder log)
        {
            if (asset == null || pool == null)
            {
                return false;
            }

            if (asset.cardPool != null)
            {
                log.AppendLine("  " + asset.name + "：卡池已指向 " + asset.cardPool.name + "（不动）");
                return false;
            }

            asset.cardPool = pool;
            asset.useAllCards = false;
            asset.cardIds = new string[0];
            EditorUtility.SetDirty(asset);
            log.AppendLine("  " + asset.name + "：卡池 → " + pool.name);
            return true;
        }

        /// <summary>
        /// 把一只<b>已存在</b>的怪的血量同步到 <see cref="TestHp"/>（2026-10-06 · 测试期）。
        ///
        /// <para><b>为什么这条要覆盖、而 <see cref="PointPool"/> 只补空位</b>：
        /// cardPool 是「一个从未配过的字段」，补空位永远不会破坏用户的意图；
        /// 血量是「已经配过、但需求变了」—— 用户明确说「目前所有的 AI 都是 1 血」，
        /// 而六个资产都已存在，「缺失才建」一个都改不到。所以这里显式覆盖，
        /// 并把每一次改动都打进报告（不静默改数据）。</para>
        ///
        /// <para>返回值 = 是否真的改了（没变就不写盘、不刷 dirty）。</para>
        /// </summary>
        private static bool PointHp(CharacterConfig asset, MonsterSpec spec, StringBuilder log)
        {
            if (asset == null)
            {
                return false;
            }

            if (asset.initialHp == spec.Hp && asset.maxHp == spec.Hp)
            {
                return false;
            }

            log.AppendLine("  " + asset.name + "：血量 " + asset.initialHp + "/" + asset.maxHp
                           + " → " + spec.Hp + "/" + spec.Hp);
            asset.initialHp = spec.Hp;
            asset.maxHp = spec.Hp;
            EditorUtility.SetDirty(asset);
            return true;
        }

        /// <summary>
        /// 把一只怪接进 <c>BattleCanvas.prefab</c> 上 <see cref="BattleDriver"/> 的某一格
        /// （2026-10-09 起带字段名 —— 现在有「这一场用哪只」（<c>_monsterConfig</c>）与
        /// 「终点那一格用哪只」（<c>_bossConfig</c>）两格要接）。
        ///
        /// <para><b>只在它还是空的时候接</b>：这两格是「用哪只怪」的选择 ——
        /// 多只怪之后，用户在 Inspector 里挑过的那一只不该被构建器悄悄换回列表里的第一只
        /// （铁律 16 的同一条口径）。所以已经指向某只怪时只打一行日志，不动它。</para>
        ///
        /// <para>⚠ 接线只按 <b>Prefab</b> 改：<c>BattleCanvas</c> 在场景里是 PrefabInstance，
        /// 改 Prefab 会自动同步 —— 这也是这里走 <c>LoadPrefabContents</c> 而不是改场景的原因。</para>
        /// </summary>
        private static bool Wire(CharacterConfig monster, string fieldName)
        {
            if (monster == null || AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPath) == null)
            {
                return false;
            }

            GameObject prefab = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                BattleDriver driver = prefab.GetComponent<BattleDriver>();
                if (driver == null)
                {
                    driver = prefab.GetComponentInChildren<BattleDriver>(true);
                }

                if (driver == null)
                {
                    Debug.LogWarning("[MonsterBuilder] BattleCanvas 上找不到 BattleDriver —— 没接线。");
                    return false;
                }

                var data = new SerializedObject(driver);
                SerializedProperty field = data.FindProperty(fieldName);
                if (field == null)
                {
                    Debug.LogWarning("[MonsterBuilder] BattleDriver 上没有 " + fieldName
                                     + " 字段（脚本未编译？）。");
                    return false;
                }

                if (field.objectReferenceValue != null)
                {
                    Debug.Log("[MonsterBuilder] BattleCanvas 的 " + fieldName + " 已指向 "
                              + ((CharacterConfig)field.objectReferenceValue).name
                              + "，保持不动（想换怪请直接在 Inspector 里拖）。");
                    return false;
                }

                field.objectReferenceValue = monster;
                data.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(prefab, CanvasPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Monsters");
            }
        }

        /// <summary>怪物卡池和战斗 / 商店 / 强化那三份放同一个目录（<c>Resources/Pools/</c>）。</summary>
        private static void EnsurePoolFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!AssetDatabase.IsValidFolder(CardPoolConfig.PoolFolder))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Pools");
            }
        }
    }
}
