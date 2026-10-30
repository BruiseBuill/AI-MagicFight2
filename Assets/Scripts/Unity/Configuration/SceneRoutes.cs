using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「场景名 → 去哪」的<b>唯一一张表</b>（2026-10-28 · 大改后只剩三个场景）。
    ///
    /// <para><b>为什么必须是一处</b>：按名字 <see cref="UnityEngine.SceneManagement.SceneManager.LoadScene(string)"/>
    /// 时，场景不在 Build Settings 里会当场抛 —— 而它抛的时刻是<b>玩家点了按键的时候</b>，
    /// 前面所有验证都看不出来。启动顺序、兜底路径、构建器登记三处必须同源。</para>
    ///
    /// <para><b>三个场景</b>：主菜单 →（自定义战斗时）牌组编辑 → 战斗。
    /// 地图 / 商店 / 石台 / 女巫工坊 / 宝箱 / 休息<b>已随冒险一起删除</b>。</para>
    /// </summary>
    public static class SceneRoutes
    {
        /// <summary>主菜单（正式构建的<b>启动场景</b>）。</summary>
        public const string HomeScene = "Home";

        /// <summary>自定义战斗的牌组编辑场景。</summary>
        public const string DeckScene = "DeckSelect";

        /// <summary>战斗场景 —— <b>就是现有的 <c>SampleScene</c></b>，不新造一个。</summary>
        public const string BattleScene = "SampleScene";

        /// <summary>
        /// 需要登记进 <c>EditorBuildSettings</c> 的场景（按顺序，<b>开始场景排第一</b>）。
        /// </summary>
        public static readonly string[] BuildOrder =
        {
            HomeScene, DeckScene, BattleScene,
        };

        /// <summary>
        /// <b>切场景的唯一入口</b>：先判它在不在 Build Settings（<c>LoadScene</c> 对不存在的场景
        /// 会当场抛，而抛出点是「玩家点了按键的时候」），再切。
        /// </summary>
        /// <returns>真的切过去了才返回 <c>true</c>。</returns>
        public static bool TryLoad(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("[SceneRoutes] 场景 '" + sceneName
                               + "' 加载不了 —— 先跑对应的构建器把它登记进 Build Settings。");
                return false;
            }

            SceneManager.LoadScene(sceneName);
            return true;
        }

        /// <summary>回主菜单。</summary>
        public static bool LoadHome()
        {
            return TryLoad(HomeScene);
        }

        /// <summary>进战斗（标准 / 自定义都走这里）。</summary>
        public static bool LoadBattle()
        {
            return TryLoad(BattleScene);
        }

        /// <summary>进牌组编成界面。</summary>
        public static bool LoadDeck()
        {
            return TryLoad(DeckScene);
        }

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
    }
}
