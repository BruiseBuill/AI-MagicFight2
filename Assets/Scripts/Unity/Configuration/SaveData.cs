using System;
using System.Collections.Generic;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>玩家数据体</b>（2026-10-01 · 玩家卡池统一）—— 一个存档里的那个玩家。
    ///
    /// <para>它现在只装两样东西：这个玩家的角色 ID，和<b>他的卡池</b>。
    /// 金币 / 生命 / 这一趟冒险已走到的节点等以后都往这里加（冒险模式的
    /// <c>RunState</c>），但那是另一批，本批只落卡池。</para>
    ///
    /// <para><b>⚠ 存的是「基础 ID」，不是解析后的 ID</b>（<c>"a"</c> 而不是 <c>"a+"</c>）：
    /// 强化版是否生效由「卡目录里有没有 <c>&lt;id&gt;+</c>」决定，见
    /// <see cref="MagicBrawl.Core.CardUpgrade.PreferUpgraded"/>。
    /// 存解析后的 ID 会让「基础牌」从卡池里消失 —— 以后要回退 / 再强化都找不回原牌。</para>
    /// </summary>
    [Serializable]
    public sealed class PlayerData
    {
        /// <summary>玩家的稳定角色 ID（与 <c>CharacterConfig.characterId</c> 同口径）。</summary>
        public string characterId = SaveStore.DefaultPlayerCharacterId;

        /// <summary>玩家的卡池：稳定卡 ID 清单（**基础 ID**，每种卡最多一张）。</summary>
        public List<string> cardIds = new List<string>();
    }

    /// <summary>
    /// <b>存档数据体</b>（2026-10-01 · 玩家卡池统一）—— 一个存档 = 一个玩家。
    ///
    /// <para>分层刻意写成 <c>SaveData</c> 包住 <c>PlayerData</c>（而不是把 cardIds 直接铺在顶层）：
    /// 用户的模型是「存档数据体当中包含玩家数据体」，将来 <c>SaveData</c> 上还会长出
    /// 「存档本身」的东西（存档时间 / 版本迁移信息 / 多角色），而 <c>player</c> 始终是那个玩家。</para>
    ///
    /// <para><b>⚠ 这是纯数据，没有行为</b>：读写、校验、初始卡池怎么来，全在 <see cref="SaveStore"/>。
    /// 这里放方法会让「存档格式」与「存档策略」混在一起，迁移时看不清哪边变了。</para>
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>格式版本。<see cref="SaveStore"/> 读到别的版本直接拒绝，**不静默按新格式解析**。</summary>
        public int version = SaveStore.CurrentVersion;

        /// <summary>这份存档属于哪个档位（<c>"test"</c> / <c>"main"</c>，见 <see cref="SaveSlots.Id"/>）。</summary>
        public string slotId = "main";

        /// <summary>这个存档里的玩家（玩家数据体）。</summary>
        public PlayerData player = new PlayerData();
    }
}
