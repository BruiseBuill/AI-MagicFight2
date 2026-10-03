using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「节点类型 → 有没有场景 / 去哪个场景 / 叫什么」的<b>唯一一张表</b>（2026-10-02）。
    ///
    /// <para><b>为什么必须是唯一一处</b>：地图上「点这个节点会发生什么」有三个地方要知道
    /// —— 地图（决定点完是切场景还是原地结算）、地图上那行小字（显示叫什么）、
    /// 构建器（把场景登记进 Build Settings）。三处各写一份 switch 的话，
    /// 加一种节点时必然漏掉一处，而漏掉的样子是<b>零报错</b>的：
    /// 漏了地图那一处 → 点下去什么都不发生；漏了 Build Settings 那一处
    /// → <c>SceneManager.LoadScene</c> 抛「场景不在 Build Settings 里」，玩到那一步才炸。</para>
    ///
    /// <para><b>⚠ 落在这一层而不是 <c>Core</c></b>：「场景名」是 Unity 的概念，
    /// <c>Core</c> 不许出现 UnityEngine（红线 1）。所以 <c>Core</c> 只回答
    /// 「这个节点<b>有没有</b>场景」（<see cref="MapNode.HasScene"/>），
    /// 「<b>哪一个</b>场景」由这里回答。</para>
    /// </summary>
    public static class MapRoutes
    {
        /// <summary>地图场景本身（从事件场景「离开」时回到这里）。</summary>
        public const string MapScene = "Map";

        /// <summary>战斗场景 —— <b>就是现有的 <c>SampleScene</c></b>，不新造一个。</summary>
        public const string BattleScene = "SampleScene";

        /// <summary>商店场景（P4 已落地）。</summary>
        public const string ShopScene = "Shop";

        /// <summary>女巫工坊场景（P6 已落地）。</summary>
        public const string WitchScene = "WitchWorkshop";

        /// <summary>石台强化场景（P5 已落地）。</summary>
        public const string AltarScene = "Upgrade";

        /// <summary>
        /// 这个节点类型对应的场景名；<c>null</c> = <b>没有场景</b>
        /// （走过去停在那儿，节点当场结算，玩家可以继续往下走）。
        ///
        /// <para>营地与未知都没有场景：前者是出发点，后者是「事件」的占位
        /// —— 冒险的事件系统还没做，所以这里<b>故意</b>不给它场景，
        /// 而不是随便挑一个顶上。</para>
        /// </summary>
        public static string SceneFor(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Battle:
                    return BattleScene;
                case MapNodeType.Boss:
                    return BattleScene;
                case MapNodeType.Shop:
                    return ShopScene;
                case MapNodeType.Witch:
                    return WitchScene;
                case MapNodeType.Altar:
                    return AltarScene;
                default:
                    return null;
            }
        }

        /// <summary>节点下方那行小字。</summary>
        public static string DisplayName(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Camp:
                    return "营地";
                case MapNodeType.Battle:
                    return "战斗";
                case MapNodeType.Shop:
                    return "商店";
                case MapNodeType.Witch:
                    return "女巫工坊";
                case MapNodeType.Altar:
                    return "石台";
                case MapNodeType.Unknown:
                    return "未知";
                case MapNodeType.Boss:
                    return "Boss";
                default:
                    return "?";
            }
        }

        /// <summary>
        /// 需要登记进 <c>EditorBuildSettings</c> 的场景（按顺序，地图排第一 ——
        /// 正式构建时第一个场景就是启动场景，冒险从地图开始）。
        /// </summary>
        public static readonly string[] BuildOrder =
        {
            MapScene, BattleScene, ShopScene, WitchScene, AltarScene,
        };

        /// <summary>把 <see cref="BuildOrder"/> 拼成 <c>Assets/Scenes/&lt;名&gt;.unity</c> 列表。</summary>
        public static List<string> BuildOrderPaths()
        {
            var list = new List<string>();
            for (int i = 0; i < BuildOrder.Length; i++)
            {
                list.Add("Assets/Scenes/" + BuildOrder[i] + ".unity");
            }

            return list;
        }

        /// <summary>
        /// 事件场景（商店 / 石台 / 女巫工坊）点「离开」时调它 → 回地图。
        ///
        /// <para><b>返回 false = 没有冒险在跑</b>，调用方按原来的「单场景调试」行为走
        /// （只打日志、不切场景）。这样三个事件场景仍然满足独立场景契约 E1/E2
        /// —— 单独打开 <c>Shop.unity</c> 进 Play，它一个字节都不会被这趟冒险影响。</para>
        ///
        /// <para><b>⚠ 这里<b>不</b>调 <c>MapRun.SettleReturn()</c></b>：结算由地图那边的
        /// <c>MapSceneEntry.Awake</c> 做 —— 一个动作只在一个地方发生，
        /// 否则「离开时结算一次 + 回地图又结算一次」会把同一个节点算两遍。</para>
        /// </summary>
        public static bool LeaveToMap()
        {
            if (!MapRun.HasActiveRun)
            {
                return false;
            }

            SceneManager.LoadScene(MapScene);
            return true;
        }
    }
}
