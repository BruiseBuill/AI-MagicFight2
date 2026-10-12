namespace MagicBrawl.App
{
    /// <summary>
    /// <b>存档档位</b>（2026-10-01 · 玩家卡池统一；<b>2026-10-05 扩成多存档位</b>）。
    ///
    /// <para><b>一个存档 = 一个玩家 = 一份卡池</b>（<c>SaveData.player.cardIds</c>）。
    /// 从前「卡池」在四处各有一份来路 —— 战斗读一份 JSON 覆盖 + 角色资产、
    /// 商店读 <c>ShopPool</c> 资产、强化与女巫读 <c>UpgradePool</c> 资产 ——
    /// 于是「同一个玩家」在不同场景里是不同的人。现在四处统一到存档这一个来源。</para>
    ///
    /// <para><b>档位分两类</b>：</para>
    /// <list type="bullet">
    /// <item><description>
    ///   <b><see cref="Test"/></b>：战斗测试档。只有战斗场景（<c>BattleDriver</c>）读得到；
    ///   商店 / 强化 / 女巫工坊的入口类<b>在代码层面不引用它</b>。
    ///   <b>它不进玩家的存档列表</b>（选存档界面只列玩家档位）—— 单场景调试用它。
    /// </description></item>
    /// <item><description>
    ///   <b><see cref="Main"/> / <see cref="Slot3"/> / <see cref="Slot4"/> / <see cref="Slot5"/> /
    ///   <see cref="Slot6"/></b>：<b>玩家的 5 个存档位</b>（显示成「存档 1..5」）。
    ///   战斗 + 商店 + 强化 + 女巫工坊<b>共用玩家当前选中的那一个</b>
    ///   （<see cref="SaveSlots.Active"/>）—— 商店买下的牌、女巫消耗掉的牌都会落到同一份账上。
    /// </description></item>
    /// </list>
    ///
    /// <para><b>⚠ <see cref="Main"/> 的落盘 ID 仍然是 <c>"main"</c></b>（文件名 <c>save-main.json</c>）：
    /// 2026-10-05 之前玩家实际的存档就写在它上面，改 ID 会让老存档当场「消失」（读不到 = 悄悄
    /// 退回默认卡池，零报错）。新增的位才用 <c>slot-3</c>… 这套 ID。</para>
    ///
    /// <para><b>⚠ 枚举取值是落盘 / 序列化的口径</b>：<c>BattleDriver._saveSlot</c>、
    /// <c>GlobalOverlayView._slot</c> 都是按 <c>int</c> 存进 Prefab / 场景的
    /// —— 只许在末尾追加，不许插值、不许改名。</para>
    /// </summary>
    public enum SaveSlot
    {
        /// <summary>战斗测试档（不进玩家的存档列表；默认只有战斗场景读它）。</summary>
        Test = 1,

        /// <summary>玩家存档位 1（原「主存档」；落盘 ID 仍是 <c>main</c>）。</summary>
        Main = 2,

        /// <summary>玩家存档位 2。</summary>
        Slot3 = 3,

        /// <summary>玩家存档位 3。</summary>
        Slot4 = 4,

        /// <summary>玩家存档位 4。</summary>
        Slot5 = 5,

        /// <summary>玩家存档位 5。</summary>
        Slot6 = 6
    }

    /// <summary>
    /// 档位的落盘与展示口径 —— <b>文件名、ID、显示名、玩家列表都只从这一处出</b>。
    ///
    /// <para>写死在这里而不是散在调用点：改名时漏一处就会静默读不到存档
    /// （<c>File.Exists</c> 返回 false → 悄悄退回默认卡池，零报错）。</para>
    /// </summary>
    public static class SaveSlots
    {
        /// <summary>
        /// 玩家的存档位（按显示顺序）。<b>不含 <see cref="SaveSlot.Test"/></b> ——
        /// 那是调试档，选存档界面不该出现它（否则玩家会在「存档 1」里看到一个测试档）。
        /// </summary>
        public static readonly SaveSlot[] PlayerSlots =
        {
            SaveSlot.Main,
            SaveSlot.Slot3,
            SaveSlot.Slot4,
            SaveSlot.Slot5,
            SaveSlot.Slot6
        };

        /// <summary>玩家的存档位一共几个。</summary>
        public static int PlayerSlotCount
        {
            get { return PlayerSlots.Length; }
        }

        /// <summary>这个档位是不是「玩家的存档位」（假的只有 <see cref="SaveSlot.Test"/>）。</summary>
        public static bool IsPlayerSlot(SaveSlot slot)
        {
            for (int i = 0; i < PlayerSlots.Length; i++)
            {
                if (PlayerSlots[i] == slot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>落盘用的稳定 ID（写进 JSON 的 <c>slotId</c>，别用文件名当 ID）。</summary>
        public static string Id(SaveSlot slot)
        {
            switch (slot)
            {
                case SaveSlot.Test:
                    return "test";
                case SaveSlot.Main:
                    // ⚠ 保持 "main"（老存档就写在这儿），别顺手改成 "slot-1"。
                    return "main";
                case SaveSlot.Slot3:
                    return "slot-3";
                case SaveSlot.Slot4:
                    return "slot-4";
                case SaveSlot.Slot5:
                    return "slot-5";
                case SaveSlot.Slot6:
                    return "slot-6";
                default:
                    return "slot-" + (int)slot;
            }
        }

        /// <summary>落盘文件名（放在 <c>Application.persistentDataPath</c> 下）。</summary>
        public static string FileName(SaveSlot slot)
        {
            return "save-" + Id(slot) + ".json";
        }

        /// <summary>给人看的名字（选存档界面 / 日志用）。玩家档位 = 「存档 N」。</summary>
        public static string DisplayName(SaveSlot slot)
        {
            if (slot == SaveSlot.Test)
            {
                return "战斗测试档";
            }

            for (int i = 0; i < PlayerSlots.Length; i++)
            {
                if (PlayerSlots[i] == slot)
                {
                    return "存档 " + (i + 1);
                }
            }

            return "未知档位（" + (int)slot + "）";
        }

        /// <summary>
        /// <b>这一局该读哪个档</b>：玩家在主菜单里选过就用他选的，否则用
        /// <paramref name="fallback"/>（各场景自己的 Inspector 值）。
        ///
        /// <para><b>为什么要有它</b>：选过档之后，商店 / 强化 / 女巫工坊 / 战斗读的必须是
        /// <b>同一本账</b>。从前那三个事件场景是<b>写死 <see cref="SaveSlot.Main"/></b> 的，
        /// 只有一个玩家档位时看不出问题；有了多存档位之后，写死 Main 会变成
        /// 「在存档 3 里玩，商店却把牌买进了存档 1」—— 而且零报错。</para>
        ///
        /// <para>没选过档时返回 <paramref name="fallback"/> = 「单独打开某个场景进 Play 调试」
        /// 完全不受主菜单影响（与三个事件场景的独立场景契约同一口径）。</para>
        /// </summary>
        public static SaveSlot Active(SaveSlot fallback)
        {
            if (!GameSession.HasChosen)
            {
                return fallback;
            }

            SaveSlot chosen = GameSession.Slot;

            // 战斗测试档只有战斗能读：事件场景遇到它时退回自己的档
            //（否则「在测试档里进商店」会把牌买进测试档，而战斗读的是玩家存档）。
            if (chosen == SaveSlot.Test && fallback != SaveSlot.Test)
            {
                return fallback;
            }

            return chosen;
        }
    }
}
