using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>玩家卡池存档的读写</b>（2026-10-01 · 玩家卡池统一）。取代了 M39 的
    /// <c>PlayerCardPoolStore</c>（那个只有一份「测试用本地覆盖」，且只有战斗在用）。
    ///
    /// <para><b>它是「玩家卡池」的唯一来源</b>：战斗 / 商店 / 强化 / 女巫工坊
    /// 四个场景都从这里取同一个玩家的同一份卡池（按 <see cref="SaveSlot"/> 分档）。
    /// 场景入口不再自带清单，也不再各自读一份卡池资产 —— 那是「同一个玩家在四个场景里
    /// 是四个人」的病根。</para>
    ///
    /// <para><b>⚠ 卡池 ID 的出入口都过 <see cref="CardUpgrade.PreferUpgraded"/></b>：
    /// 存档里写的是基础 ID（<c>"a"</c>），读出来要换成强化版（<c>"a+"</c>，如果已落盘）；
    /// 写回去之前要把解析后的 ID 归一成基础 ID。漏了任何一头，症状都是
    /// 「强化过的牌换个场景又变回原样」且零报错。</para>
    ///
    /// <note><b>无状态</b>：只有静态方法，没有单例也没有缓存。两个场景各自 <c>new</c> 也行，
    /// 但当前四个调用点都用静态调用 —— 存档是「外部的文件」，不是会话内的对象。</note>
    /// </summary>
    public static class SaveStore
    {
        /// <summary>
        /// 当前支持的存档格式版本。
        ///
        /// <para><b>版本 2（2026-10-02）</b>：<c>player.upgrades</c>（多轴强化配方）。
        /// 版本 1 的存档<b>直接读得进来</b> —— 旧格式缺这个字段时 <c>JsonUtility</c> 会留空列表，
        /// 语义正好是「什么都没强化」，所以不需要搬运代码（见 <see cref="Read"/>）。</para>
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>玩家的稳定角色 ID（与 <c>Resources/Characters/DefaultPlayer.asset</c> 对齐）。</summary>
        public const string DefaultPlayerCharacterId = "player.default";

        /// <summary>
        /// 主存档<b>首次生成</b>时随机塞进去几张牌（用户 2026-10-01 口径：「选随机 10 张卡牌塞进去」）。
        ///
        /// <para>10 &gt; <c>WitchWorkshop.MinPoolSize</c>（9），所以开新档第一次进女巫工坊
        /// 是**能开工**的 —— 这一点是刻意的：起始 8 张那套旧口径下女巫永远是「无法进行特殊强化」，
        /// 想验正常分支还得手动改 Inspector。</para>
        /// </summary>
        public const int MainStartingCardCount = 10;

        // ══════════════════════════════════════════════════════
        //  路径
        // ══════════════════════════════════════════════════════

        /// <summary>某个档位的落盘路径。</summary>
        public static string FilePathFor(SaveSlot slot)
        {
            return Path.Combine(Application.persistentDataPath, SaveSlots.FileName(slot));
        }

        /// <summary>某个档位有没有落过盘。</summary>
        public static bool Exists(SaveSlot slot)
        {
            return File.Exists(FilePathFor(slot));
        }

        // ══════════════════════════════════════════════════════
        //  读
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 读某个档位的玩家数据体。
        ///
        /// <para><b>文件不存在时两个档位的行为**刻意不同**</b>：</para>
        /// <list type="bullet">
        /// <item><description>
        ///   <see cref="SaveSlot.Main"/> —— 这是「开新档」：现抽 <see cref="MainStartingCardCount"/>
        ///   张随机卡、<b>立刻落盘</b>、返回它。所以「删掉 <c>save-main.json</c> 再进任意一个
        ///   共用场景」= 重开一局（重新随机 10 张）。
        /// </description></item>
        /// <item><description>
        ///   <see cref="SaveSlot.Test"/> —— <b>不落盘、返回 null</b>，调用方回退角色自带的卡池
        ///   （<c>BattlePool</c> 全卡表）。保留 M39 的调试手感：进战斗不需要先有存档，
        ///   只有点了设置面板的「保存并重开」才写文件。
        /// </description></item>
        /// </list>
        ///
        /// <para>返回 <c>false</c> 才代表出错（文件损坏 / 版本不支持 / 权限）。
        /// 返回 <c>true</c> 而 <paramref name="player"/> 为 null = 「没有存档，用调用方的默认值」。</para>
        /// </summary>
        public static bool TryLoadPlayer(SaveSlot slot, out PlayerData player, out string error)
        {
            player = null;
            error = null;

            SaveData data;
            try
            {
                data = Read(slot);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is InvalidDataException)
            {
                error = "无法读取存档（" + SaveSlots.DisplayName(slot) + "）：" + ex.Message;
                return false;
            }

            if (data == null)
            {
                if (slot == SaveSlot.Test)
                {
                    return true;                       // player 保持 null = 用角色默认卡池
                }

                player = CreateMainStartingPlayer();
                if (!TryWrite(slot, player, out error))
                {
                    return false;
                }

                Debug.Log("[SaveStore] 新建主存档：" + player.cardIds.Count + " 张起始牌 —— "
                          + FilePathFor(slot));
                return true;
            }

            player = data.player;
            return true;
        }

        /// <summary>
        /// 读某个档位的**玩家卡池**（已过 <see cref="CardUpgrade.PreferUpgraded"/> 的解析）。
        ///
        /// <para>没有存档（或该档是未落盘的测试档）时，<paramref name="pool"/> 保持
        /// <paramref name="definition"/> 自带的那一份 —— 调用方不用自己判「读到了没有」。</para>
        ///
        /// <para><b>⚠ 顺序不能颠倒</b>：必须<b>先</b>把存档里的强化配方叠到卡目录上
        /// （<see cref="UpgradeBook.BuildCatalog"/> 合成出 <c>a+</c>），<b>再</b>走
        /// <see cref="CardUpgrade.PreferUpgraded"/> 把清单里的 <c>a</c> 换成 <c>a+</c>。
        /// 反过来的话目录里根本没有 <c>a+</c>，判据不成立 → 强化静默丢失
        /// （症状：「强化过的牌换个场景又变回原样」，零报错）。</para>
        /// </summary>
        public static bool TryLoadCardPool(SaveSlot slot, CharacterDefinition definition,
            out CardPool pool, out string error)
        {
            if (definition == null)
            {
                pool = null;
                error = "角色定义不能为空。";
                return false;
            }

            pool = definition.CreateCardPool();
            error = null;

            PlayerData player;
            if (!TryLoadPlayer(slot, out player, out error))
            {
                return false;
            }

            if (player == null || player.cardIds == null || player.cardIds.Count == 0)
            {
                return true;
            }

            ICardCatalog catalog = WithUpgrades(pool.Catalog, UpgradeBook.FromRecords(player.upgrades));
            var saved = new CardPool(CardUpgrade.PreferUpgraded(player.cardIds, catalog), catalog);
            if (!saved.Validate(definition.MinimumCardCount, out error))
            {
                return false;
            }

            pool = saved;
            return true;
        }

        /// <summary>
        /// 读某个档位的玩家卡池 ID 清单（已解析、已去重）。没有存档时返回空清单。
        ///
        /// <para><paramref name="catalog"/> 传的通常是<b>基础卡目录</b>（<c>CardCatalogAsset</c> 那份），
        /// 本方法会自己把强化册叠上去；<b>调用方若还要用目录去查这些 ID，必须改用
        /// <paramref name="resolvedCatalog"/></b> —— 否则 <c>a+</c> 在基础目录里查不到，
        /// 症状是「强化过的牌在背包里凭空消失」且零报错。</para>
        /// </summary>
        public static bool TryLoadCardIds(SaveSlot slot, ICardCatalog catalog,
            out IReadOnlyList<string> ids, out ICardCatalog resolvedCatalog, out string error)
        {
            ids = new List<string>().AsReadOnly();
            error = null;

            PlayerData player;
            if (!TryLoadPlayer(slot, out player, out error))
            {
                resolvedCatalog = catalog;
                return false;
            }

            UpgradeBook book = player == null ? UpgradeBook.Empty : UpgradeBook.FromRecords(player.upgrades);
            resolvedCatalog = WithUpgrades(catalog, book);

            if (player == null || player.cardIds == null)
            {
                return true;
            }

            ids = CardUpgrade.PreferUpgraded(player.cardIds, resolvedCatalog);
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  强化册（2026-10-02 · 多轴强化）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把一份强化册叠到卡目录上，产出「这个玩家 / 这只怪物眼里的卡目录」。
        ///
        /// <para><b>它是「四个场景读同一个玩家」这条口径在强化上的落点</b>：
        /// 商店背包 / 强化场景 / 女巫工坊 / 战斗都得用<b>叠过册子</b>的那份目录，
        /// 否则强化版（<c>a+</c>）在目录里不存在，那些界面会把它当「不在目录里的 ID」跳过。</para>
        ///
        /// <para><paramref name="catalog"/> 为 null 时回落到内置卡表；
        /// 册子为空时<b>原样返回 <paramref name="catalog"/></b>（不造新目录）。</para>
        /// </summary>
        public static ICardCatalog WithUpgrades(ICardCatalog catalog, UpgradeBook book)
        {
            ICardCatalog source = catalog ?? CardCatalog.Builtin();
            return book == null || book.IsEmpty ? source : book.BuildCatalog(source);
        }

        /// <summary>
        /// 读某个档位的强化册。没有存档时返回 <see cref="UpgradeBook.Empty"/>（不是错误）。
        /// </summary>
        public static bool TryLoadUpgradeBook(SaveSlot slot, out UpgradeBook book, out string error)
        {
            book = UpgradeBook.Empty;
            PlayerData player;
            if (!TryLoadPlayer(slot, out player, out error))
            {
                return false;
            }

            if (player != null)
            {
                book = UpgradeBook.FromRecords(player.upgrades);
            }

            return true;
        }

        /// <summary>
        /// <b>某个档位的卡目录</b> = 基础目录 + 那个玩家的强化册（2026-10-02）。
        ///
        /// <para>商店 / 强化 / 女巫工坊三个场景的入口在拿到 <c>CardCatalogAsset</c> 之后
        /// <b>都要过这一下</b>：不过的话目录里没有 <c>a+</c>，<see cref="CardUpgrade.PreferUpgraded"/>
        /// 的判据不成立，界面上那张强化过的牌会退回基础版（或干脆被当成「不在目录里的 ID」跳过）。
        /// 把它们收敛到这个方法，是为了让「强化版在目录里长什么样」只有一处定义。</para>
        ///
        /// <para>读册子失败时打警告并按「没有强化」继续 —— 一个读不出来的强化册
        /// 不该让整个商店打不开。</para>
        /// </summary>
        public static ICardCatalog WithSavedUpgrades(SaveSlot slot, ICardCatalog catalog)
        {
            UpgradeBook book;
            string error;
            if (!TryLoadUpgradeBook(slot, out book, out error))
            {
                Debug.LogWarning("[SaveStore] 读强化册失败，本场景按「没有强化」处理：" + error);
                return catalog ?? CardCatalog.Builtin();
            }

            return WithUpgrades(catalog, book);
        }

        /// <summary>
        /// <b>给某个档位的某张牌追加一笔强化</b>（强化场景 / 调试菜单的唯一写入口）。
        ///
        /// <para>语义是<b>追加</b>而不是覆盖：同一张牌先加了 2 点力量、再加 1 条词条，
        /// 最后是「力量 +2 <b>且</b>多了那条词条」—— 用户 2026-10-02 口径
        /// 「一张卡既可以增加力量，又可以使其冷却值减小，甚至可以额外再加上一个加速效果，
        /// 但仍然还是原来那张」。</para>
        ///
        /// <para><b>⚠ 落盘的是基础 ID</b>（<c>CardUpgrade.BaseIdOf</c> 归一）：
        /// 传 <c>"a+"</c> 进来也会记成挂在 <c>"a"</c> 上，
        /// 否则同一张牌会分裂成两条配方（一条生效、一条静默失效）。</para>
        ///
        /// <para><b>⚠ 不做强度校验</b>：能不能强化由调用方过
        /// <c>CardUpgrade.CanApply</c>（那是规则）。存档是<b>事实记录</b> ——
        /// 在这里再判一次会导致「界面允许、存档拒绝」这种查不出来的分叉。</para>
        /// </summary>
        public static bool TryAppendUpgrade(SaveSlot slot, string idOrBaseId, CardUpgradeMod mod, out string error)
        {
            error = null;
            string baseId = CardUpgrade.BaseIdOf(idOrBaseId);
            if (baseId.Length == 0)
            {
                error = "缺少卡 ID。";
                return false;
            }

            if (mod == null)
            {
                error = "缺少强化内容。";
                return false;
            }

            return MutatePlayer(slot, delegate (PlayerData player)
            {
                AppendUpgrade(player, baseId, mod);
            }, out error);
        }

        /// <summary>
        /// <b>女巫的工坊</b>的一次性写回：<b>消耗一张牌</b>，同时把一条效果<b>转移</b>给另一张
        /// （2026-10-02）。
        ///
        /// <para><b>为什么必须一次写完，而不是「先删再写」两次调用</b>：两次调用 = 两次
        /// 读→改→写。中间那次失败（磁盘满 / 文件被占）会留下
        /// <b>「牌已经吃掉、效果却没拿到」</b>的存档 —— 玩家的损失是实打实的，而且没有任何提示。
        /// 一次写进同一个文件，只有「全成」和「全不成」两种结果。</para>
        /// </summary>
        public static bool TryConsumeAndUpgrade(SaveSlot slot, string consumedIdOrBaseId,
            string targetIdOrBaseId, CardUpgradeMod mod, out string error)
        {
            error = null;

            string consumed = CardUpgrade.BaseIdOf(consumedIdOrBaseId);
            string target = CardUpgrade.BaseIdOf(targetIdOrBaseId);
            if (consumed.Length == 0 || target.Length == 0)
            {
                error = "缺少卡 ID。";
                return false;
            }

            if (mod == null)
            {
                error = "缺少强化内容。";
                return false;
            }

            if (string.Equals(consumed, target, StringComparison.Ordinal))
            {
                error = "不能消耗自己。";
                return false;
            }

            return MutatePlayer(slot, delegate (PlayerData player)
            {
                if (player.cardIds != null)
                {
                    player.cardIds.Remove(consumed);
                }

                AppendUpgrade(player, target, mod);
            }, out error);
        }

        /// <summary>把一笔强化追到某个玩家的配方册上（<b>追加</b>语义）。调用方负责归一 ID。</summary>
        private static void AppendUpgrade(PlayerData player, string baseId, CardUpgradeMod mod)
        {
            if (player.upgrades == null)
            {
                player.upgrades = new List<CardUpgradeRecord>();
            }

            CardUpgradeRecord record = Find(player.upgrades, baseId);
            if (record == null)
            {
                record = new CardUpgradeRecord { baseId = baseId };
                player.upgrades.Add(record);
            }

            if (record.mods == null)
            {
                record.mods = new List<CardUpgradeMod>();
            }

            record.mods.Add(mod);
        }

        /// <summary>整条替换某个档位里一张牌的配方（调试 / 以后「取消强化」用）。</summary>
        public static bool TrySetUpgrade(SaveSlot slot, CardUpgradeRecord record, out string error)
        {
            error = null;
            if (record == null || record.IsEmpty)
            {
                error = "配方为空。";
                return false;
            }

            string baseId = CardUpgrade.BaseIdOf(record.baseId);
            record.baseId = baseId;
            return MutatePlayer(slot, delegate (PlayerData player)
            {
                if (player.upgrades == null)
                {
                    player.upgrades = new List<CardUpgradeRecord>();
                }

                CardUpgradeRecord existing = Find(player.upgrades, baseId);
                if (existing == null)
                {
                    player.upgrades.Add(record);
                    return;
                }

                existing.mods = new List<CardUpgradeMod>(record.mods);
            }, out error);
        }

        /// <summary>
        /// <b>整份替换某个档位的强化册</b>（传 null 或空 = 清空）。
        ///
        /// <para>只有一个用途：调试菜单把主存档复原成「一张牌都没强化过」。
        /// 单张牌的增删请走 <see cref="TryAppendUpgrade"/> —— 那个是追加语义，
        /// 不会把这张牌已有的其它轴一起抹掉。</para>
        /// </summary>
        public static bool TryReplaceUpgrades(SaveSlot slot, IEnumerable<CardUpgradeRecord> records, out string error)
        {
            error = null;
            var list = new List<CardUpgradeRecord>();
            if (records != null)
            {
                foreach (CardUpgradeRecord record in records)
                {
                    if (record != null && !record.IsEmpty)
                    {
                        list.Add(record);
                    }
                }
            }

            return MutatePlayer(slot, delegate (PlayerData player)
            {
                player.upgrades = list;
            }, out error);
        }

        // ══════════════════════════════════════════════════════
        //  写
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把一份 ID 清单写进某个档位（先归一成**基础 ID**）。
        ///
        /// <para>商店买入 / 女巫消耗用这个 —— 它们手上只有一串 ID，没有
        /// <see cref="CharacterDefinition"/>（那是战斗侧的概念，要它来做最低张数校验）。</para>
        ///
        /// <para><b>⚠ 只改卡池，不碰强化册</b>：<c>player.upgrades</c> 会被原样保留。
        /// 「买一张牌 = 重建一个 <c>PlayerData</c>」这种写法会把整个强化册悄悄清空
        /// （症状：去商店买张牌，回来发现所有强化都没了，零报错）——
        /// 所以这里走 <see cref="MutatePlayer"/> 而不是自己 new 一个。</para>
        ///
        /// <para><b>刻意不做「最低张数」校验</b>：存档是<b>事实记录</b>，不是策略校验点。
        /// 战斗那侧的最低张数由设置面板的按钮 + <c>CardPool.Validate</c> 管住；
        /// 女巫消耗掉一张之后卡池自然变少，不该因此写不进去。</para>
        /// </summary>
        public static bool TrySaveCardIds(SaveSlot slot, IEnumerable<string> cardIds, out string error)
        {
            error = null;
            var normalized = new List<string>();
            if (cardIds != null)
            {
                foreach (string id in cardIds)
                {
                    string baseId = CardUpgrade.BaseIdOf(id);
                    if (!string.IsNullOrEmpty(baseId) && !normalized.Contains(baseId))
                    {
                        normalized.Add(baseId);
                    }
                }
            }

            return MutatePlayer(slot, delegate (PlayerData player)
            {
                player.cardIds = normalized;
            }, out error);
        }

        /// <summary>同上，但吃一份运行期的 <see cref="CardPool"/>（战斗的设置面板用）。</summary>
        public static bool TrySaveCardPool(SaveSlot slot, CardPool pool, out string error)
        {
            if (pool == null)
            {
                error = "卡池不能为空。";
                return false;
            }

            return TrySaveCardIds(slot, pool.CardIds, out error);
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// <b>读 → 改 → 写</b>的唯一实现（商店买入 / 女巫消耗 / 追加强化都走它）。
        ///
        /// <para><b>⚠ 为什么要绕这一圈，而不是「new 一个 <see cref="PlayerData"/> 写下去」</b>：
        /// 那样会把<b>没被这次操作碰到的字段</b>一起清空（最典型的是强化册 ——
        /// 去商店买一张牌，回来发现所有强化都没了）。存档是几个功能共写的一份数据，
        /// 每次写都必须建立在「当前文件里的内容」之上。</para>
        ///
        /// <para>没有存档时：主存档由 <see cref="TryLoadPlayer"/> 现场开新档（随机起始牌），
        /// 测试档则就地建一份空玩家（<b>只有真写了才落盘</b>，与 M39 的调试手感一致）。</para>
        /// </summary>
        private static bool MutatePlayer(SaveSlot slot, Action<PlayerData> mutate, out string error)
        {
            PlayerData player;
            if (!TryLoadPlayer(slot, out player, out error))
            {
                return false;
            }

            if (player == null)
            {
                player = new PlayerData();
            }

            mutate(player);
            Normalize(player);
            return TryWrite(slot, player, out error);
        }

        /// <summary>按基础 ID 找一张牌的配方。</summary>
        private static CardUpgradeRecord Find(List<CardUpgradeRecord> records, string baseId)
        {
            if (records == null)
            {
                return null;
            }

            for (int i = 0; i < records.Count; i++)
            {
                CardUpgradeRecord record = records[i];
                if (record != null && string.Equals(CardUpgrade.BaseIdOf(record.baseId), baseId,
                        StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// 归一化一份玩家数据体（<b>读与写都过它</b>）：
        /// 卡池只留非空基础 ID 且去重；强化册只留非空配方，
        /// 同一基础 ID 的多条<b>按顺序合并成一条</b>（后者接在前者后面）。
        ///
        /// <para>为什么归一而不是拒绝：这两个字段都是<b>追加式</b>的，出现了重复说明
        /// 中间有人手改过文件或跨版本写过 —— 丢掉整份存档太狠，
        /// 合并成一条才是用户预期的语义（都在描述同一张牌被叠了哪些强化）。</para>
        /// </summary>
        private static void Normalize(PlayerData player)
        {
            if (player == null)
            {
                return;
            }

            if (player.cardIds == null)
            {
                player.cardIds = new List<string>();
            }

            var ids = new List<string>();
            for (int i = 0; i < player.cardIds.Count; i++)
            {
                string baseId = CardUpgrade.BaseIdOf(player.cardIds[i]);
                if (baseId.Length > 0 && !ids.Contains(baseId))
                {
                    ids.Add(baseId);
                }
            }

            player.cardIds = ids;

            if (player.upgrades == null)
            {
                player.upgrades = new List<CardUpgradeRecord>();
                return;
            }

            var kept = new List<CardUpgradeRecord>();
            for (int i = 0; i < player.upgrades.Count; i++)
            {
                CardUpgradeRecord record = player.upgrades[i];
                if (record == null)
                {
                    continue;
                }

                string baseId = CardUpgrade.BaseIdOf(record.baseId);
                if (baseId.Length == 0 || record.IsEmpty)
                {
                    continue;
                }

                record.baseId = baseId;
                CardUpgradeRecord merged = Find(kept, baseId);
                if (merged == null)
                {
                    kept.Add(record);
                    continue;
                }

                for (int m = 0; m < record.mods.Count; m++)
                {
                    merged.mods.Add(record.mods[m]);
                }
            }

            player.upgrades = kept;
        }

        /// <summary>真的去读文件；不存在返回 <c>null</c>（不是错误）。格式不对则抛。</summary>
        private static SaveData Read(SaveSlot slot)
        {
            string path = FilePathFor(slot);
            if (!File.Exists(path))
            {
                return null;
            }

            SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path, Encoding.UTF8));
            if (data == null)
            {
                throw new InvalidDataException("存档内容为空或不是合法 JSON。");
            }

            if (data.version < 1 || data.version > CurrentVersion)
            {
                throw new InvalidDataException("存档版本 " + data.version + " 不受支持（当前 "
                                               + CurrentVersion + "）。");
            }

            // v1 → v2 的迁移就是「什么都不做」：v2 只多了 player.upgrades，
            // 而 JSON 里没有这个键时字段留空列表 —— 语义正好是「一张牌都没强化过」。
            // 这里显式写出来是为了让「为什么不用搬运」这件事可查，而不是靠默认值。
            data.version = CurrentVersion;

            if (data.player == null)
            {
                throw new InvalidDataException("存档缺少玩家数据体。");
            }

            Normalize(data.player);
            return data;
        }

        /// <summary>
        /// 原子写：先写临时文件、再替换目标（沿用 M39 的实现）。
        ///
        /// <para>⚠ 这份原子替换是 Windows 口径（<c>File.Replace</c>）；其他平台发布前要重新验证。
        /// 写失败时<b>保留原文件</b>，调用方拿到的是一句人话的错误。</para>
        /// </summary>
        private static bool TryWrite(SaveSlot slot, PlayerData player, out string error)
        {
            error = null;
            string path = FilePathFor(slot);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                var data = new SaveData
                {
                    version = CurrentVersion,
                    slotId = SaveSlots.Id(slot),
                    player = player
                };

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException)
            {
                error = "保存失败：" + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>
        /// 开新档：从<b>内置卡表</b>里随机抽 <see cref="MainStartingCardCount"/> 张不重复的牌。
        ///
        /// <para><b>⚠ 用 <see cref="CardCatalog.Builtin"/> 而不是 <c>CardCatalogAsset</c></b>：
        /// 后者会把已经落盘的强化卡（<c>"a+"</c>）也算进候选，抽到一个「凭空多出来的强化版」
        /// 而基础版不在池里 —— 玩家会觉得这张牌是哪来的。基础牌池只从内置卡表里抽，
        /// 之后它若被强化，由 <see cref="CardUpgrade.PreferUpgraded"/> 解析成强化版。</para>
        ///
        /// <para><b>真随机</b>（种子取 <see cref="Environment.TickCount"/>）：每次开新档都不一样。
        /// 想重抽就把 <c>save-main.json</c> 删掉再进场景。
        /// <paramref name="seed"/> 只为自动化验证准备（同种子 → 同一批牌）。</para>
        /// </summary>
        public static PlayerData CreateMainStartingPlayer(int? seed = null)
        {
            ICardCatalog catalog = CardCatalog.Builtin();
            var ids = new List<string>();
            foreach (CardDef card in catalog.All)
            {
                if (card != null)
                {
                    ids.Add(card.Id);
                }
            }

            var rng = new System.Random(seed ?? unchecked(Environment.TickCount * 397 ^ 0x5A17));
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string tmp = ids[i];
                ids[i] = ids[j];
                ids[j] = tmp;
            }

            var player = new PlayerData { characterId = DefaultPlayerCharacterId };
            int take = ids.Count < MainStartingCardCount ? ids.Count : MainStartingCardCount;
            for (int i = 0; i < take; i++)
            {
                player.cardIds.Add(ids[i]);
            }

            return player;
        }
    }
}
