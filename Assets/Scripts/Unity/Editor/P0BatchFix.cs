using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// <b>一次性批处理</b>（2026-10-05）：把当天四项改动落到资产上。
    ///
    /// <para><b>为什么需要它</b>：当天编辑器的脚本编译流水线卡死（<c>isCompiling</c>
    /// 一直是 true、反复「Requested script compilation」但从不执行），而构建器是 C# ——
    /// 不编译就没法跑 <c>HomeUiBuilder</c> / <c>GlobalOverlayBuilder</c>。
    /// 于是用 <c>-executeMethod</c> 让 Unity 自己启动、自己编译、自己跑这份脚本。</para>
    ///
    /// <para><b>它做什么</b>：</para>
    /// <list type="number">
    /// <item>重建 <c>Home</c> 场景与 <c>HomeCanvas.prefab</c>（5 行存档 + 新建/删除 + 进入游戏）；</item>
    /// <item>重建五个场景的全局覆盖层（设置键 = 齿轮、无文字）；</item>
    /// <item>把 <c>BattleCanvas.prefab</c> 里 <c>Hud/GearButton</c> 那枚<b>隐形热点</b>拆掉
    /// （<c>Hud_Bar.png</c> 上的齿轮已经擦掉，留着就是一块看不见却能点开的死区）；</item>
    /// <item>把 MCP 的本地 HTTP 服务拉起来（编辑器重启后它<b>不会自启</b>，
    /// 不拉起来的话这一轮就没法继续远程驱动）。</item>
    /// </list>
    ///
    /// <para><b>跑完就可以删掉这个文件</b>（它是脚手架，不是长期节点）。</para>
    /// </summary>
    public static class P0BatchFix
    {
        public static void RunAll()
        {
            var log = new StringBuilder();
            log.AppendLine("[P0BatchFix] ==== 开始 ====");

            try
            {
                log.AppendLine(HomeUiBuilder.RunAll());
            }
            catch (Exception ex)
            {
                log.AppendLine("✘ Home 构建失败：" + ex);
            }

            try
            {
                log.AppendLine(GlobalOverlayBuilder.RunAll());
            }
            catch (Exception ex)
            {
                log.AppendLine("✘ 全局覆盖层构建失败：" + ex);
            }

            try
            {
                log.AppendLine(RemoveDeadGearHotspot());
            }
            catch (Exception ex)
            {
                log.AppendLine("✘ 拆 GearButton 失败：" + ex);
            }

            try
            {
                log.AppendLine(StartMcpServer());
            }
            catch (Exception ex)
            {
                log.AppendLine("✘ 启动 MCP 服务失败：" + ex);
            }

            log.AppendLine("[P0BatchFix] ==== 结束 ====");
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// 拆掉 <c>BattleCanvas.prefab</c> 里那枚盖在「已经不存在的齿轮」上的隐形点击区。
        ///
        /// <para><b>为什么</b>：2026-10-05 把齿轮从 <c>Hud_Bar.png</c> 上抠出来做成独立的
        /// <c>Setting_Gear.png</c>、挂到右上角（全局覆盖层那枚）。战斗 HUD 里原来靠一枚
        /// 全透明的 <c>Button</c> 点那条上烘死的齿轮 —— 齿轮没了，那块 45×45 就成了一块
        /// 看不见却能点开的死区（点它会弹设置面板，而玩家看不到任何可点的东西）。</para>
        ///
        /// <para><b>⚠ 只改 Prefab</b>：战斗场景里的 <c>BattleCanvas</c> 是它的 PrefabInstance，
        /// 场景文件里没有这棵子树的拷贝。</para>
        /// </summary>
        private static string RemoveDeadGearHotspot()
        {
            const string prefabPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                return "  ✘ 找不到 Prefab：" + prefabPath;
            }

            try
            {
                Transform gear = FindDeep(root.transform, "GearButton");
                if (gear == null)
                {
                    return "  · BattleCanvas 里已经没有 GearButton 了";
                }

                // 先把 HudView 上那个引用摘掉，免得 Prefab 里留一条指向已销毁节点的引用。
                HudView[] views = root.GetComponentsInChildren<HudView>(true);
                for (int i = 0; i < views.Length; i++)
                {
                    var data = new SerializedObject(views[i]);
                    SerializedProperty prop = data.FindProperty("_gearButton");
                    GameObject refGo = prop.objectReferenceValue as GameObject;
                    if (prop != null && prop.objectReferenceValue != null
                        && refGo != null
                        && refGo.transform.IsChildOf(gear))
                    {
                        prop.objectReferenceValue = null;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                UnityEngine.Object.DestroyImmediate(gear.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return "  ✔ 已拆掉 BattleCanvas 里的隐形齿轮热点";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 把 MCP for Unity 的本地 HTTP 服务拉起来（<c>127.0.0.1:8080</c>）。
        ///
        /// <para><b>⚠ 走反射</b>：MCP 包的类型不是本工程的编译依赖，直接 <c>using</c> 会让
        /// 这个脚本在没装 MCP 包的机器上编译失败。这里按名字找类型、找方法，
        /// 找不到就安静地跳过（那一台只能手点窗口里的 Start）。</para>
        /// </summary>
        private static string StartMcpServer()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    if (types[t].FullName == null
                        || types[t].FullName.IndexOf("MCPServiceLocator", StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    PropertyInfo server = types[t].GetProperty("Server", flags);
                    object instance = server == null ? null : server.GetValue(null, null);
                    if (instance == null)
                    {
                        continue;
                    }

                    MethodInfo start = instance.GetType().GetMethod("StartLocalHttpServer",
                        new Type[] { typeof(bool) });
                    if (start == null)
                    {
                        return "  ⚠ 找到了 MCPServiceLocator，但没有 StartLocalHttpServer(bool)";
                    }

                    // ⚠ quiet 必须显式传：那个方法有 bool 参数，漏了会 TargetParameterCountException。
                    start.Invoke(instance, new object[] { true });
                    return "  ✔ MCP 本地 HTTP 服务已启动";
                }
            }

            return "  ⚠ 没找到 MCPServiceLocator —— 需要手点 Window > MCP for Unity > Start";
        }
    }
}
