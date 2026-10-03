using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// M43 · 在设置菜单里追加「强制显示怪物手牌（AI 调试）」测试开关，<b>不重建</b>设置子树。
    ///
    /// <para>与 M40 同一套理由：重跑 M39 会重建整棵 <c>SettingsLayer</c>、丢掉用户在 Prefab 上的手改。
    /// 本构建器只做增量 —— Menu 下没有 <c>ForceRevealToggle</c> 就建一个（底板 + 勾选标记 + 文案），
    /// 再把 <c>SettingsView._revealMonsterHand</c> 接上；已有则只补引用，<b>可重跑</b>。</para>
    ///
    /// <para>⚠ 只改 <c>BattleCanvas.prefab</c>：场景里的 BattleCanvas 是它的
    /// <b>PrefabInstance</b>（见 <c>SampleScene.unity</c>），改 Prefab 会自动同步 ——
    /// 与 WitchUiBuilder 那种「场景里建再 SaveAsPrefabAsset」的拷贝式构建器不同。</para>
    ///
    /// <para>⚠ 勾选标记<b>不能</b> <c>SetActive(false)</c>：<see cref="Toggle"/> 是靠
    /// <c>graphic</c> 的 canvasRenderer alpha 表示勾选状态的（<c>OnEnable</c> 里会重设），
    /// 节点一停用就永远不亮 —— 进 Play 后表现为「勾了没反应」。</para>
    /// </summary>
    public static class M43SettingsRevealToggle
    {
        private const string CanvasPath = "Assets/Prefabs/Ui/BattleCanvas.prefab";
        private const string FontPath = "Assets/Art/Fonts/Black/Google-Regular.asset";

        [MenuItem("魔法乱斗/M43 · 设置里加「强制显示怪物手牌」开关", priority = 43)]
        public static void BuildAndSave()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后再构建。");

            GameObject prefab = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                if (!Ensure(prefab))
                {
                    Debug.LogWarning("[M43] 没找到 SettingsLayer（先跑 M39），本次未改动。");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(prefab, CanvasPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            AssetDatabase.SaveAssets();
            Debug.Log("[M43] 设置面板已加入「强制显示怪物手牌」测试开关。");
        }

        /// <summary>可在 Prefab 内容根上调用；返回是否找到了 SettingsLayer 并完成接线。</summary>
        public static bool Ensure(GameObject canvas)
        {
            Transform layer = canvas.transform.Find("SettingsLayer");
            if (layer == null) return false;      // 还没跑过 M39 —— 不在这里新建整层

            SettingsView view = layer.GetComponent<SettingsView>();
            if (view == null) throw new InvalidOperationException("SettingsLayer 上没有 SettingsView。");

            Transform menu = layer.Find("Menu");
            if (menu == null) throw new InvalidOperationException("SettingsLayer 缺少 Menu。");

            Transform node = menu.Find("ForceRevealToggle");
            if (node == null) node = Build(menu);

            Toggle toggle = node.GetComponent<Toggle>();
            if (toggle == null) throw new InvalidOperationException("ForceRevealToggle 上没有 Toggle。");

            var data = new SerializedObject(view);
            SerializedProperty field = data.FindProperty("_revealMonsterHand");
            if (field == null)
            {
                throw new InvalidOperationException("SettingsView 上没有 _revealMonsterHand 字段（脚本未编译？）。");
            }

            field.objectReferenceValue = toggle;
            data.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>建一行「☐ 强制显示怪物手牌（AI 调试）」。位置接在「测试功能 · 玩家卡池」下面。</summary>
        private static Transform Build(Transform menu)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new InvalidOperationException("找不到项目字体资源：" + FontPath);

            GameObject root = Node("ForceRevealToggle", menu);
            Box(Rect(root), 0, -246, 440, 56);

            // 整条都能点：透明命中面当 Toggle 的 targetGraphic（与卡池复选框同一手法）
            Image hit = Image(root, Color.clear, true);

            GameObject box = Node("Box", root.transform);
            Box(Rect(box), -196, 0, 26, 26);
            Image(box, UiTheme.PanelEdge, false);

            GameObject check = Node("Checkmark", box.transform);
            Stretch(Rect(check));
            Image checkImage = Image(check, UiTheme.SelectionGlow, false);
            GameObject left = Node("CheckShort", check.transform);
            Box(Rect(left), -5, -1, 5, 12);
            Rect(left).localRotation = Quaternion.Euler(0, 0, 40);
            Image(left, UiTheme.Backdrop, false);
            GameObject right = Node("CheckLong", check.transform);
            Box(Rect(right), 3, 2, 5, 21);
            Rect(right).localRotation = Quaternion.Euler(0, 0, -36);
            Image(right, UiTheme.Backdrop, false);

            GameObject label = Node("Label", root.transform);
            Box(Rect(label), 22, 0, 380, 44);
            TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = "强制显示怪物手牌（AI 调试）";
            text.fontSize = 23;
            text.color = UiTheme.TextPrimary;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Left;
            text.enableWordWrapping = false;

            Toggle toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = hit;
            toggle.graphic = checkImage;                     // ⚠ 见类注释：勾选标记必须保持 active
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.isOn = false;
            return root.transform;
        }

        // ── 小工具（与 SettingsUiBuilder 同一套写法）─────────────

        private static GameObject Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            return node;
        }

        private static RectTransform Rect(GameObject node) { return (RectTransform)node.transform; }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Image Image(GameObject node, Color color, bool raycast)
        {
            var image = node.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }
    }
}
