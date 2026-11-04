using MagicBrawl.App;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.EditorTools
{
    /// <summary>
    /// <b>对局记录的预览入口</b>（2026-11-02）。只做自测用，不进包。
    ///
    /// <para><b>为什么要它</b>：记录表是运行时按回合数长出来的，编辑器里没有预制体可看 ——
    /// 想确认版式（行高、两列对不对齐、盾字有没有渲出来），最快的方式就是进 Play 模式
    /// 把一份写死的样例铺上去看一眼。</para>
    /// </summary>
    public static class BattleRecordMenu
    {
        [MenuItem("工具/对局记录/预览样例战报")]
        private static void PreviewSample()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[战报] 请先进入 Play 模式再预览 —— 这块界面是运行时搭的，编辑模式没有 Canvas。");
                return;
            }

            BattleRecordDemo.ShowSample();
            Debug.Log("[战报] 已铺开样例（8 回合），看 Game 视图。");
        }

        [MenuItem("工具/对局记录/清空历史存档")]
        private static void ClearHistory()
        {
            BattleRecordStore.Clear();
            Debug.Log("[战报] 本地对局记录已清空。");
        }

        [MenuItem("工具/对局记录/打印历史存档")]
        private static void DumpHistory()
        {
            var all = BattleRecordStore.All;
            if (all.Count == 0)
            {
                Debug.Log("[战报] 没有历史记录。");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("[战报] 共 ").Append(all.Count).Append(" 局\n");
            for (int i = 0; i < all.Count; i++)
            {
                sb.Append(i).Append(". ").Append(all[i].EndedAtLocal.ToString("MM-dd HH:mm"))
                  .Append("  ").Append(all[i].ResultText)
                  .Append("  ").Append(all[i].TurnsText).Append('\n');
            }

            Debug.Log(sb.ToString());
        }
    }
}
