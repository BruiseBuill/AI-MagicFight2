using System;
using System.Collections.Generic;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>玩家数据体</b>（2026-10-01 · 玩家卡池统一）—— 一个存档里的那个玩家。
    ///
    /// <para>它装三样东西：这个玩家的角色 ID、<b>他的卡池</b>、以及<b>卡池里那些牌的强化配方</b>。
    /// 金币 / 生命 / 这一趟冒险已走到的节点等以后都往这里加（冒险模式的
    /// <c>RunState</c>），但那是另一批，本批只落卡池与强化。</para>
    ///
    /// <para><b>⚠ 存的是「基础 ID」，不是解析后的 ID</b>（<c>"a"</c> 而不是 <c>"a+"</c>）：
    /// 强化版是<b>读的时候合成出来的</b>（见 <see cref="MagicBrawl.Core.UpgradeBook.BuildCatalog"/>
    /// 与 <see cref="MagicBrawl.Core.CardUpgrade.PreferUpgraded"/>）。
    /// 存解析后的 ID 会让「基础牌」从卡池里消失 —— 以后要回退 / 再强化都找不回原牌。</para>
    /// </summary>
    [Serializable]
    public sealed class PlayerData
    {
        /// <summary>玩家的稳定角色 ID（与 <c>CharacterConfig.characterId</c> 同口径）。</summary>
        public string characterId = SaveStore.DefaultPlayerCharacterId;

        /// <summary>玩家的卡池：稳定卡 ID 清单（**基础 ID**，每种卡最多一张）。</summary>
        public List<string> cardIds = new List<string>();

        /// <summary>
        /// 玩家的强化配方（<b>2026-10-02 · 多轴强化</b>）：一张牌一条，按基础 ID 挂。
        ///
        /// <para><b>为什么强化要进存档</b>：上一版把强化落成一份
        /// <c>Card_a_Up.asset</c> —— 那是<b>全局</b>事实，「这个玩家的暴风雪强化过」
        /// 与「别人的暴风雪也强化过」分不开，怪物侧更是无处安放。
        /// 现在一个存档 = 一个玩家 = 一份卡池 + 一份强化册，卡牌身份始终是那张基础牌。</para>
        ///
        /// <para>⚠ 字段名 / 结构一旦发布就不要再改（<c>JsonUtility</c> 按字段名落盘）；
        /// 要改走 <see cref="SaveStore.CurrentVersion"/> 的迁移。</para>
        /// </summary>
        public List<MagicBrawl.Core.CardUpgradeRecord> upgrades
            = new List<MagicBrawl.Core.CardUpgradeRecord>();

        /// <summary>
        /// <b>曾经获得过的卡</b>（2026-10-05 · 卡牌获取池）：基础 ID 清单，<b>只增不减</b>。
        ///
        /// <para><b>为什么不能只用 <see cref="cardIds"/></b>：那张单子表达的是「<b>现在</b>持有」——
        /// 女巫工坊献祭掉一张牌之后它会少一项。而获取池的规则是
        /// 「<b>获得过</b>的牌永远不再出现（<b>包括获得之后又把它消耗掉的情况</b>）」，
        /// 所以「曾经拿到过」必须单独记一笔，否则同一张牌被消耗掉之后又会在奖励 / 商店里刷出来。</para>
        ///
        /// <para>语义上它是 <see cref="cardIds"/> 的<b>超集</b>（持有的一定获得过）——
        /// <c>SaveStore.Normalize</c> 每次读 / 写都会用 <see cref="cardIds"/> 把它补齐，
        /// 所以旧存档（v2 及以前）没有这个字段也能自动获得正确的语义。</para>
        /// </summary>
        public List<string> everOwnedIds = new List<string>();

        /// <summary>
        /// <b>曾经展示过、但没被获得</b>的卡（2026-10-05 · 卡牌获取池）：基础 ID 清单。
        ///
        /// <para>这些卡下次出现在战斗奖励 / 商店货架上的概率<b>减半</b>（固定减半一次，不累积）。
        /// 一旦被获得，就从这张单子里移出去（它进了 <see cref="everOwnedIds"/>，概率归零，
        /// 两种状态不会同时成立）。</para>
        ///
        /// <para>「展示过」= 它作为候选真的被摆到玩家眼前那一刻（奖励界面的三张 / 商店货架的四个位），
        /// 而不是「进过随机池」。玩家看了没拿，才算「展示过而没获得」。</para>
        /// </summary>
        public List<string> offeredIds = new List<string>();
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
