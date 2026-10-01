namespace MagicBrawl.App
{
    /// <summary>
    /// <b>存档档位</b>（2026-10-01 · 玩家卡池统一）。
    ///
    /// <para><b>一个存档 = 一个玩家 = 一份卡池</b>（<c>SaveData.player.cardIds</c>）。
    /// 从前「卡池」在四处各有一份来路 —— 战斗读一份 JSON 覆盖 + 角色资产、
    /// 商店读 <c>ShopPool</c> 资产、强化与女巫读 <c>UpgradePool</c> 资产 ——
    /// 于是「同一个玩家」在不同场景里是不同的人。现在四处统一到存档这一个来源。</para>
    ///
    /// <para><b>两个档位的适用范围是硬约束，不是配置项</b>：</para>
    /// <list type="bullet">
    /// <item><description>
    ///   <b><see cref="Test"/></b>：战斗测试档。只有战斗场景（<c>BattleDriver</c>）读得到；
    ///   商店 / 强化 / 女巫工坊的入口类<b>在代码层面不引用它</b>——
    ///   想让它们读也读不到，改配置也读不到。
    /// </description></item>
    /// <item><description>
    ///   <b><see cref="Main"/></b>：主存档。战斗（把 <c>BattleDriver._saveSlot</c>
    ///   改成它时）+ 商店 + 强化 + 女巫工坊<b>共用同一份</b>卡池 ——
    ///   商店买下的牌、女巫消耗掉的牌都会落到这里，另外三个场景下一次读到的就是新的那份。
    /// </description></item>
    /// </list>
    ///
    /// <para>刻意<b>只有两个</b>：多开档位是「多存档位」那套需求，本作还没有，
    /// 先别扩。要加就得同时想清楚「新档位的初始卡池从哪来」。</para>
    /// </summary>
    public enum SaveSlot
    {
        /// <summary>存档 1 · 战斗测试档（默认只有战斗场景读它）。</summary>
        Test = 1,

        /// <summary>存档 2 · 主存档（战斗 + 商店 + 强化 + 女巫工坊共用）。</summary>
        Main = 2
    }

    /// <summary>
    /// 档位的落盘与展示口径 —— <b>文件名、ID、显示名都只从这一处出</b>。
    ///
    /// <para>写死在这里而不是散在调用点：改名时漏一处就会静默读不到存档
    /// （<c>File.Exists</c> 返回 false → 悄悄退回默认卡池，零报错）。</para>
    /// </summary>
    public static class SaveSlots
    {
        /// <summary>落盘用的稳定 ID（写进 JSON 的 <c>slotId</c>，别用文件名当 ID）。</summary>
        public static string Id(SaveSlot slot)
        {
            return slot == SaveSlot.Main ? "main" : "test";
        }

        /// <summary>落盘文件名（放在 <c>Application.persistentDataPath</c> 下）。</summary>
        public static string FileName(SaveSlot slot)
        {
            return "save-" + Id(slot) + ".json";
        }

        /// <summary>给人看的名字（日志与设置面板用）。</summary>
        public static string DisplayName(SaveSlot slot)
        {
            return slot == SaveSlot.Main ? "主存档（存档 2）" : "战斗测试档（存档 1）";
        }
    }
}
