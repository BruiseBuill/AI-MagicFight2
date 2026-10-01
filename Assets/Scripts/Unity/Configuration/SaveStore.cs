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
        /// <summary>当前支持的存档格式版本。</summary>
        public const int CurrentVersion = 1;

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

            var saved = new CardPool(CardUpgrade.PreferUpgraded(player.cardIds, pool.Catalog), pool.Catalog);
            if (!saved.Validate(definition.MinimumCardCount, out error))
            {
                return false;
            }

            pool = saved;
            return true;
        }

        /// <summary>读某个档位的玩家卡池 ID 清单（已解析、已去重）。没有存档时返回空清单。</summary>
        public static bool TryLoadCardIds(SaveSlot slot, ICardCatalog catalog,
            out IReadOnlyList<string> ids, out string error)
        {
            ids = new List<string>().AsReadOnly();
            error = null;

            PlayerData player;
            if (!TryLoadPlayer(slot, out player, out error))
            {
                return false;
            }

            if (player == null || player.cardIds == null)
            {
                return true;
            }

            ids = CardUpgrade.PreferUpgraded(player.cardIds, catalog);
            return true;
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
        /// <para><b>刻意不做「最低张数」校验</b>：存档是<b>事实记录</b>，不是策略校验点。
        /// 战斗那侧的最低张数由设置面板的按钮 + <c>CardPool.Validate</c> 管住；
        /// 女巫消耗掉一张之后卡池自然变少，不该因此写不进去。</para>
        /// </summary>
        public static bool TrySaveCardIds(SaveSlot slot, IEnumerable<string> cardIds, out string error)
        {
            error = null;

            var player = new PlayerData { characterId = DefaultPlayerCharacterId };
            if (cardIds != null)
            {
                foreach (string id in cardIds)
                {
                    string baseId = CardUpgrade.BaseIdOf(id);
                    if (!string.IsNullOrEmpty(baseId) && !player.cardIds.Contains(baseId))
                    {
                        player.cardIds.Add(baseId);
                    }
                }
            }

            return TryWrite(slot, player, out error);
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

            if (data.version != CurrentVersion)
            {
                throw new InvalidDataException("存档版本 " + data.version + " 不受支持（当前 "
                                               + CurrentVersion + "）。");
            }

            if (data.player == null || data.player.cardIds == null)
            {
                throw new InvalidDataException("存档缺少玩家数据体。");
            }

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
