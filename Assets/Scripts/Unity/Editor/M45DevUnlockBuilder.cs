using System;
using MagicBrawl.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// M45 · <b>战斗界面的「隐藏入口 → 完整设置」</b>（2026-10-05）。
    ///
    /// <para><b>用户口径</b>：「战斗界面当中，你要增加一个隐藏选项，点击此隐藏选项之后，
    /// 可以解锁全部的设置内容，包括是否强制显示 AI 的手牌以及修改玩家自己的卡池
    /// （这些功能原本已有，但莫名其妙现在找不到了）」。</para>
    ///
    /// <para><b>「原本已有、现在找不到」是怎么回事</b>：这些东西在 M39 建的
    /// <c>SettingsLayer</c> 里（继续游戏 / 重新开始 / 测试功能 · 玩家卡池，
    /// 加 M43 的「强制显示怪物手牌」开关）—— 但 M44 做全局覆盖层时，按当时的口径
    /// 「所有场景统一只放退出」把整层从<b>每一个</b> Canvas 上删掉了
    /// （源码保留，节点全没了）。于是功能在、入口没了。</para>
    ///
    /// <para><b>本构建器做四件事</b>（全部幂等，可反复跑）：</para>
    /// <list type="number">
    /// <item>把 M39 的完整设置层建回 <c>BattleCanvas.prefab</c>（<see cref="SettingsUiBuilder.Ensure"/>）；</item>
    /// <item>给它补上 M43 的「强制显示怪物手牌（AI 调试）」开关（<see cref="M43SettingsRevealToggle.Ensure"/>）；</item>
    /// <item>在 M44 设置面板的<b>标题上</b>放一块完全透明的热点 <c>SecretHotspot</c>；</item>
    /// <item>把这一层接到 <c>GlobalOverlayView._fullSettings</c>、热点接到 <c>._secretHotspot</c>。</item>
    /// </list>
    ///
    /// <para><b>⚠ 只改 <c>BattleCanvas.prefab</c></b>：场景里的 BattleCanvas 是它的
    /// PrefabInstance（见 <c>SampleScene.unity</c>），改 Prefab 会自动同步 —— 与 M43 同一套。
    /// 其余四个场景<b>不要</b>这份完整设置层（那里没有对局、也没有卡池可改）。</para>
    ///
    /// <para><b>⚠ 跑 M44 之后必须重跑本菜单</b>：M44 会拆掉整个 <c>GlobalOverlayLayer</c>
    /// 重建（隐藏热点在里面），也会顺手清掉 M39 的旧设置层。</para>
    /// </summary>
    public static class M45DevUnlockBuilder
    {
        private const string CanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";

        [MenuItem("魔法乱斗/M45 · 战斗设置·隐藏入口", priority = 45)]
        public static void BuildAndSave()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后再构建。");

            GameObject prefab = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                if (!Ensure(prefab))
                {
                    Debug.LogWarning("[M45] 没找到 M44 的设置面板（先跑 `魔法乱斗/M44 · 构建全局覆盖层`），本次未改动。");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(prefab, CanvasPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[M45] 战斗界面已接好隐藏入口：点设置面板的标题 → 打开完整设置层"
                      + "（强制显示对手手牌 / 改玩家自己的卡池）。");
        }

        /// <summary>可在 Prefab 内容根上调用；返回是否找到了 M44 的设置面板并完成接线。</summary>
        public static bool Ensure(GameObject canvas)
        {
            Transform layer = FindDeep(canvas.transform, "SettingsPanelLayer");
            if (layer == null)
            {
                return false;      // 还没跑过 M44 —— 不在这里新建整层
            }

            GlobalOverlayView overlay = canvas.GetComponentInChildren<GlobalOverlayView>(true);
            if (overlay == null)
            {
                throw new InvalidOperationException("Canvas 上没有 GlobalOverlayView（M44 没跑全？）。");
            }

            // ① 完整设置层（M39）。它自己会在 Canvas 下建 SettingsLayer + CardPoolPanel，
            //    并把 _fullSettings 接到 overlay 上。
            Transform existing = canvas.transform.Find("SettingsLayer");
            if (existing == null || existing.GetComponent<SettingsView>() == null)
            {
                SettingsUiBuilder.Ensure(canvas);
            }

            SettingsView view = canvas.GetComponentInChildren<SettingsView>(true);
            if (view == null)
            {
                throw new InvalidOperationException("SettingsLayer 没能建起来（见 SettingsUiBuilder 的日志）。");
            }

            // ② M43 的「强制显示怪物手牌（AI 调试）」开关。
            if (!M43SettingsRevealToggle.Ensure(canvas))
            {
                throw new InvalidOperationException("M43 的开关没接上（SettingsLayer/Menu 结构不对？）。");
            }

            // ③ 隐藏热点：铺在设置面板标题上的透明按钮。
            Transform panel = layer.Find("Panel");
            if (panel == null)
            {
                throw new InvalidOperationException("SettingsPanelLayer 缺少 Panel。");
            }

            Transform node = panel.Find("SecretHotspot");
            if (node == null)
            {
                node = BuildSecretHotspot(panel);
            }

            Button hotspot = node.GetComponent<Button>();
            if (hotspot == null)
            {
                throw new InvalidOperationException("SecretHotspot 上没有 Button。");
            }

            // ④ 接线。
            var data = new SerializedObject(overlay);
            data.FindProperty("_fullSettings").objectReferenceValue = view;
            data.FindProperty("_secretHotspot").objectReferenceValue = hotspot;
            data.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// 建那块透明热点。它<b>故意没有任何像素</b>（Image 的 color 是全透明）——
        /// 玩家看不见它，但射线打得到它。
        /// </summary>
        private static Transform BuildSecretHotspot(Transform panel)
        {
            var go = new GameObject("SecretHotspot", typeof(RectTransform));
            go.layer = panel.gameObject.layer;
            go.transform.SetParent(panel, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, UiLayout.OverlayPanelTitleCenterY);
            rect.sizeDelta = new Vector2(UiLayout.OverlaySecretHotspotWidth,
                UiLayout.OverlaySecretHotspotHeight);

            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);      // 全透明：看不见
            image.raycastTarget = true;                    // 但吃射线

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;   // 别在按下时把「看不见」变成看得见

            return go.transform;
        }

        // ── 小工具 ───────────────────────────────────────────────

        /// <summary>按名字深度优先找一个节点（Canvas 树不大，够用）。</summary>
        private static Transform FindDeep(Transform root, string name)
        {
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
    }
}
