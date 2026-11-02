using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>对局记录的界面</b>（2026-11-02）：结算页那张表 + 首页的历史列表。
    ///
    /// <para><b>为什么整块在运行时搭、不进 Prefab</b>：这块界面要按「这局有几个回合」
    /// 长出行数，行数是不定值，预制体只能摆一个固定骨架；而且它没有任何美术件
    /// （纯文字 + 纯色底板），运行时生成的代价远小于「改构建器 → 重跑 M7→M8→M11 →
    /// 重新保存 Prefab」那条链路。工程里 <c>ResultWinView.EnsureHomeButton</c> 也是同一个理由。</para>
    ///
    /// <para><b>它不认识战斗</b>：只吃 <see cref="BattleRecord"/>，谁给的、怎么来的都不知道 ——
    /// 所以结算页和历史详情页共用同一段绘制代码。</para>
    /// </summary>
    public static class BattleRecordOverlay
    {
        private const float PanelWidth = 1680f;
        private const float PanelHeight = 940f;
        private const float RowHeight = 44f;
        private const float TableWidth = 1560f;

        private static GameObject _root;
        private static TMP_FontAsset _font;

        // ── 对外入口 ────────────────────────────────────────────

        /// <summary>铺开一局的记录（结算页 / 历史详情共用）。</summary>
        public static void ShowRecord(BattleRecord record, string title, string subtitle,
            Action onHome, Action onRestart, Action onClose)
        {
            if (record == null) return;

            Canvas canvas = Open("BattleRecordOverlay");
            GameObject panel = Panel(canvas.transform);

            MakeText(panel.transform, title, 46, UiTheme.ResultWinTitle, TextAlignmentOptions.Center,
                new Vector2(0f, -34f), new Vector2(PanelWidth, 60f));

            MakeText(panel.transform, subtitle, 22, UiTheme.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, -100f), new Vector2(PanelWidth, 32f));

            MakeText(panel.transform, BattleRecordText.Legend(true), 18, UiTheme.TextSecondary, TextAlignmentOptions.Right,
                new Vector2(60f, -146f), new Vector2(TableWidth, 26f));

            BuildTable(panel.transform, record, -150f);

            float y = -PanelHeight + 120f;
            if (onRestart != null)
            {
                MakeButton(panel.transform, "重新开始", 170f, y, new Vector2(300f, 76f), true, onRestart);
            }

            if (onHome != null)
            {
                MakeButton(panel.transform, "返回主菜单", -170f, y, new Vector2(300f, 76f), false, onHome);
            }

            if (onClose != null)
            {
                MakeButton(panel.transform, "返回", 0f, y - 100f, new Vector2(300f, 76f), false, onClose);
            }
        }

        /// <summary>铺开历史列表：一行一局，点进去看那一局的记录。</summary>
        public static void ShowHistory(Action onClose)
        {
            Canvas canvas = Open("BattleHistoryOverlay");
            GameObject panel = Panel(canvas.transform);

            MakeText(panel.transform, "对局记录", 46, UiTheme.ResultWinTitle, TextAlignmentOptions.Center,
                new Vector2(0f, -34f), new Vector2(PanelWidth, 60f));

            IReadOnlyList<BattleRecord> all = BattleRecordStore.All;
            MakeText(panel.transform, all.Count == 0 ? "还没有对局记录" : "共 " + all.Count + " 局",
                22, UiTheme.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, -100f), new Vector2(PanelWidth, 32f));

            float top = -150f;
            float step = 78f;
            int max = Mathf.Min(all.Count, 8);

            for (int i = 0; i < max; i++)
            {
                BattleRecord record = all[i];
                int index = i;
                string line = record.EndedAtLocal.ToString("yyyy-MM-dd HH:mm")
                              + "      " + record.ResultText + "      " + record.TurnsText + "      " + record.Opponent;

                MakeButton(panel.transform, line, 0f, top - step * i, new Vector2(TableWidth, 66f),
                    record.Victory, () => ShowRecord(record, record.ResultText + " · " + record.Opponent,
                        record.EndedAtLocal.ToString("yyyy-MM-dd HH:mm") + " · " + record.TurnsText,
                        null, null, ShowHistoryBack));
            }

            MakeButton(panel.transform, "返回", 0f, -PanelHeight + 120f, new Vector2(300f, 76f), false, onClose);
        }

        private static void ShowHistoryBack()
        {
            Hide();
            ShowHistory(Hide);
        }

        public static void Hide()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        // ── 记录表 ──────────────────────────────────────────────

        private static void BuildTable(Transform panel, BattleRecord record, float top)
        {
            int rows = Mathf.Max(1, record.Rows.Count);
            float rowH = Mathf.Min(RowHeight, 620f / rows);

            MakeText(panel, "回合", 18, UiTheme.TextSecondary, TextAlignmentOptions.Left,
                new Vector2(60f, top), new Vector2(90f, 26f));
            MakeText(panel, "你 → 敌", 18, UiTheme.TextSecondary, TextAlignmentOptions.Left,
                new Vector2(150f, top), new Vector2(600f, 26f));
            MakeText(panel, "敌 → 你", 18, UiTheme.TextSecondary, TextAlignmentOptions.Left,
                new Vector2(830f, top), new Vector2(600f, 26f));

            float y = top - 34f;
            for (int i = 0; i < record.Rows.Count; i++)
            {
                TurnRecord row = record.Rows[i];
                bool last = i == record.Rows.Count - 1;

                if (last)
                {
                    RectTransform bar = Bar(panel, y - rowH + 6f, TableWidth, rowH - 6f, new Color(0.91f, 0.78f, 0.48f, 0.10f));
                    bar.name = "LastRow";
                }

                MakeText(panel, "T" + row.Turn, 21, last ? UiTheme.WinAccent : UiTheme.TextSecondary,
                    TextAlignmentOptions.Left, new Vector2(60f, y), new Vector2(90f, rowH));

                MakeText(panel, BattleRecordText.Half(row.First, true), 21, UiTheme.TextPrimary,
                    TextAlignmentOptions.TopLeft, new Vector2(150f, y), new Vector2(660f, rowH * 2f));

                MakeText(panel, BattleRecordText.Half(row.Second, true), 21, UiTheme.TextPrimary,
                    TextAlignmentOptions.TopLeft, new Vector2(830f, y), new Vector2(660f, rowH * 2f));

                y -= rowH;
            }
        }

        // ── 通用小件 ────────────────────────────────────────────

        private static Canvas Open(string name)
        {
            Hide();

            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 420;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (EventSystem.current == null)
            {
                new GameObject("BattleRecordEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            _root = go;

            RectTransform dim = Box(name + "Dim", go.transform, Vector2.zero, new Vector2(1920f, 1080f));
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = Vector2.zero;
            dim.offsetMax = Vector2.zero;
            Image image = dim.gameObject.AddComponent<Image>();
            image.color = new Color(0.02f, 0.04f, 0.07f, 0.94f);
            image.raycastTarget = true;

            return canvas;
        }

        private static GameObject Panel(Transform parent)
        {
            RectTransform rt = Box("Panel", parent, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            Image image = rt.gameObject.AddComponent<Image>();
            image.color = UiTheme.Panel;

            return rt.gameObject;
        }

        private static RectTransform Box(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Bar(Transform parent, float y, float width, float height, Color color)
        {
            RectTransform rt = Box("Bar", parent, new Vector2(60f, y), new Vector2(width, height));
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rt;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, Vector2 pos, Vector2 box)
        {
            RectTransform rt = Box("Text", parent, pos, box);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (FontAsset() != null) label.font = FontAsset();
            label.text = text ?? string.Empty;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.enableWordWrapping = true;
            return label;
        }

        private static Button MakeButton(Transform parent, string text, float x, float y, Vector2 size,
            bool primary, Action onClick)
        {
            RectTransform rt = Box("Button", parent, new Vector2(x, y), size);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image image = rt.gameObject.AddComponent<Image>();
            image.color = primary ? UiTheme.WinAccent : UiTheme.PanelEdge;

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            RectTransform labelRect = Box("Label", rt, Vector2.zero, size);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            if (FontAsset() != null) label.font = FontAsset();
            label.text = text ?? string.Empty;
            label.fontSize = 24;
            label.color = primary ? new Color(0.04f, 0.13f, 0.19f) : UiTheme.TextPrimary;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            return button;
        }

        private static TMP_FontAsset FontAsset()
        {
            if (_font != null) return _font;

            _font = TMP_Settings.defaultFontAsset;
            if (_font == null)
            {
                TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null && all.Length > 0) _font = all[0];
            }

            return _font;
        }
    }
}
