using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>主菜单</b>（2026-10-28 · 大改）—— 冒险、商店、强化、存档全部删掉之后，
    /// 这块界面只剩一件事：<b>选哪种打法</b>。
    ///
    /// <para><b>两个入口</b>：</para>
    /// <list type="number">
    /// <item><b>标准战斗</b> —— 双方各随机 8 张，直接开打；</item>
    /// <item><b>自定义战斗</b> —— 先去牌组编辑界面挑双方的 8 张，再开打。</item>
    /// </list>
    ///
    /// <para><b>职责边界</b>：本类只负责「摆 + 报」，点了哪一枚只把事件报出去；
    /// 切哪个场景、要不要清运行态由 <see cref="HomeSceneEntry"/> 决定。</para>
    ///
    /// <para><b>节点由构建器建</b>（<c>HomeUiBuilder.BuildCanvas</c>），
    /// 两枚键的版式是那里的局部常量 —— 这一版刻意不进 <c>UiLayout</c>：
    /// 主菜单只有这一套版式，没必要为它再开一组全局常量。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeView : MonoBehaviour
    {
        [Header("接线（由 HomeUiBuilder 写入）")]
        [Tooltip("大标题。")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("标题下面那行小字。")]
        [SerializeField] private TMP_Text _subtitle;

        [Tooltip("「标准战斗」。")]
        [SerializeField] private Button _standardButton;

        [Tooltip("「自定义战斗」。")]
        [SerializeField] private Button _customButton;

        [Tooltip("底部提示行。")]
        [SerializeField] private TMP_Text _hint;

        /// <summary>玩家点了「标准战斗」。</summary>
        public event Action StandardClicked;

        /// <summary>玩家点了「自定义战斗」。</summary>
        public event Action CustomClicked;

        /// <summary>构建器的接线口。</summary>
        public void Configure(TMP_Text title, TMP_Text subtitle,
            Button standardButton, Button customButton, TMP_Text hint)
        {
            _title = title;
            _subtitle = subtitle;
            _standardButton = standardButton;
            _customButton = customButton;
            _hint = hint;
        }

        private UnityAction _standardHandler;
        private UnityAction _customHandler;

        private void Awake()
        {
            Debug.Log("[HomeView] Awake: 标准="
                      + (_standardButton == null ? "null" : _standardButton.name)
                      + " 自定义=" + (_customButton == null ? "null" : _customButton.name));
            Hook();
        }

        // ⚠ 注册写在 OnEnable 而不是只在 Awake：Awake 每个对象只跑一次，
        //   而 OnEnable 每次「重新激活」都会跑 —— 界面被 SetActive 关掉再打开时，
        //   Awake 里挂的那份监听器如果被 Unhook 掉了就再也回不来。
        private void OnEnable()
        {
            Hook();
        }

        private void OnDisable()
        {
            Unhook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        /// <summary>
        /// 把两枚键接到自己的处理上（<b>可重入</b>：先摘旧的再挂新的）。
        ///
        /// <para><b>⚠ 铁律 8</b>：监听器一律在<b>运行时</b>按序列化字段自己接 ——
        /// 构建器在编辑模式接的那些不是序列化数据，存 Prefab / 场景时直接蒸发
        /// （症状是「每一项检查都通过，但按钮点下去什么都没有」）。</para>
        /// </summary>
        private void Hook()
        {
            HookOne(_standardButton, ref _standardHandler, OnStandardClicked);
            HookOne(_customButton, ref _customHandler, OnCustomClicked);
        }

        private static void HookOne(Button button, ref UnityAction handler, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            if (handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = action;
            button.onClick.AddListener(handler);
        }

        private void Unhook()
        {
            UnhookOne(_standardButton, ref _standardHandler);
            UnhookOne(_customButton, ref _customHandler);
        }

        private static void UnhookOne(Button button, ref UnityAction handler)
        {
            if (button != null && handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = null;
        }

        private void OnStandardClicked()
        {
            Action handler = StandardClicked;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnCustomClicked()
        {
            Action handler = CustomClicked;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>底部那行小字。</summary>
        public void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text ?? string.Empty;
            }
        }
    }
}
