using System;
using MagicBrawl.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// M40 · 只把卡池面板的「恢复默认」按钮换成「全不选」，<b>不重建</b>设置子树。
    ///
    /// <para>为什么不能重跑 M39 构建器：<c>Ensure</c> 发现 <c>SettingsLayer</c> 已存在就直接返回
    /// （只补引用），这是刻意的——重建成整棵子树会丢用户在 Prefab 上的手改。</para>
    ///
    /// <para>本构建器同样不 <c>Instantiate</c> 任何子树，只做两件事该做的动作：改子节点的名字与文案、
    /// 把序列化字段从旧的 <c>_resetDefault</c> 改成 <c>_selectNone</c>。
    /// 因为全部是「改已存在的实例引用」，<c>SaveAsPrefabAsset</c> 的引用会正常映射回 Prefab 内部。</para>
    /// </summary>
    public static class M40SettingsBuilder
    {
        private const string CanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";

        [MenuItem("魔法乱斗/M40 · 卡池「全不选」按钮", priority = 40)]
        public static void BuildAndSave()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后再构建。");

            GameObject prefab = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                Rewire(prefab);
                PrefabUtility.SaveAsPrefabAsset(prefab, CanvasPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            AssetDatabase.SaveAssets();
            Debug.Log("[M40] 卡池面板按钮已更新为「全不选」。");
        }

        /// <summary>可在场景对象或 Prefab 内容根上复用；返回是否改动了任何东西。</summary>
        public static bool Rewire(GameObject canvas)
        {
            Transform layer = canvas.transform.Find("SettingsLayer");
            if (layer == null) throw new InvalidOperationException("BattleCanvas 缺少 SettingsLayer，请先跑 M39 构建器。");
            SettingsView view = layer.GetComponent<SettingsView>();
            if (view == null) throw new InvalidOperationException("SettingsLayer 上没有 SettingsView。");
            Transform pool = layer.Find("CardPoolPanel");
            if (pool == null) throw new InvalidOperationException("SettingsLayer 缺少 CardPoolPanel。");

            Transform node = pool.Find("ResetDefault") ?? pool.Find("SelectNone");
            if (node == null) throw new InvalidOperationException("卡池面板里找不到 ResetDefault / SelectNone 按钮。");
            Button button = node.GetComponent<Button>();
            if (button == null) throw new InvalidOperationException("卡池面板的按钮节点上没有 Button。");
            button.gameObject.name = "SelectNone";

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) throw new InvalidOperationException("按钮上没有 TMP 文本。");
            label.text = "全不选";

            var data = new SerializedObject(view);
            SerializedProperty none = data.FindProperty("_selectNone");
            if (none == null) throw new InvalidOperationException("SettingsView 上没有 _selectNone 字段（脚本未编译？）。");
            none.objectReferenceValue = button;
            // 旧字段已被删除，FindProperty 返回 null 是预期结果。
            SerializedProperty old = data.FindProperty("_resetDefault");
            if (old != null) old.objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
