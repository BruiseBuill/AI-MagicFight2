namespace MagicBrawl.App
{
    /// <summary>
    /// <b>这一次「玩游戏」的会话状态</b>（2026-10-05 · 游戏开始场景）——
    /// 目前只有一样东西：<b>玩家在主菜单里选中的那个存档档位</b>。
    ///
    /// <para><b>为什么需要一个静态量，而不是让每个场景各自读 Inspire 里的字段</b>：
    /// 玩家是在 <c>Home</c> 场景里选的档，而读档的是<b>另一个场景</b>里的
    /// <c>BattleDriver</c>（战斗）/ <c>ShopSceneEntry</c>（商店）…… 换场景会把场景对象全销毁，
    /// 「这一局玩的是哪个档」必须放在不随场景销毁的地方 —— 与 <see cref="MapRun"/> 同一条理由。</para>
    ///
    /// <para><b>⚠ 它是「玩家这一次的选择」，不是「默认值」</b>：<see cref="HasChosen"/> 为假时
    /// 各场景仍按自己 Inspector 里的档位跑（战斗默认 <c>SaveSlot.Main</c>）——
    /// 这样单独打开 <c>SampleScene</c> 进 Play 调试，一个字节都不会被主菜单影响
    /// （与三个事件场景的「独立场景契约」E1/E2 同一口径）。</para>
    ///
    /// <para><b>⚠ 它不落盘</b>：选档只是「这次玩哪本账」，本身不是要存的东西。
    /// 真正落盘的是 <see cref="SaveStore"/>（一个档位一个 <c>save-*.json</c>）。</para>
    /// </summary>
    public static class GameSession
    {
        /// <summary>玩家在主菜单里<b>显式选过</b>档位没有。假 = 各场景按自己的 Inspector 值跑。</summary>
        public static bool HasChosen;

        /// <summary>玩家选中的档位。只在 <see cref="HasChosen"/> 为真时有意义。</summary>
        public static SaveSlot Slot = SaveSlot.Main;

        /// <summary>
        /// 把「这一局玩哪个档」定下来（主菜单的「进入游戏」调它）。
        /// </summary>
        public static void Choose(SaveSlot slot)
        {
            Slot = slot;
            HasChosen = true;
        }

        /// <summary>
        /// 忘掉这次选择（回到「各场景自己说了算」）。
        ///
        /// <para>目前只有一个调用点：从主菜单退出整套流程时清一遍，
        /// 免得下一次进来还带着上一次的档 —— 那会让「单独进战斗场景调试」变得不可预测。</para>
        /// </summary>
        public static void Forget()
        {
            HasChosen = false;
            Slot = SaveSlot.Main;
        }
    }
}
