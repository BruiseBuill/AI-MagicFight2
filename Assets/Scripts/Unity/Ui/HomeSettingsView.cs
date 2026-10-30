using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>主菜单的设置面板</b>（2026-10-30）—— 三项设置住在这里：
    ///
    /// <list type="number">
    /// <item><b>对局双方</b>：玩家对 AI / AI 对 AI（观战）/ 玩家对玩家（同屏热座）；</item>
    /// <item><b>经典模式</b>：开 = 只用前 40 张老卡；</item>
    /// <item><b>怪物智力</b>：简单（只会按力量从大到小出牌）/ 标准。</item>
    /// </list>
    ///
    /// <para><b>职责边界</b>：本类只做「读 <see cref="GameSettings"/> → 摆成一行字 → 点一下切下一档」。
    /// 值存在哪、怎么落盘、怎么影响战斗，全在 <see cref="GameSettings"/> 与
    /// <c>BattleDriver</c> 里，界面不碰规则。</para>
    ///
    /// <para><b>为什么每行是一个「点一下换一档」的按钮而不是下拉框 / Toggle</b>：
    /// 本工程没有 Dropdown 的既有样式，而 Toggle 的勾选标记必须保持 active 才亮
    /// （<c>M43</c> 踩过：节点一停用就永远不亮）。选项都只有 2–3 档，循环按钮最稳。</para>
    ///
    /// <para><b>⚠ 面板节点在构建器里就是失活的</b>：首次 <c>SetActive(true)</c> 才触发 Awake，
    /// 而 Awake 里调 Close 会把刚显示的东西按灭 —— 所以这里<b>不在 Awake 里关面板</b>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeSettingsView : MonoBehaviour
    {
        [Header("接线（由 M50 构建器写入）")]
        [Tooltip("右上角那枚齿轮。")]
        [SerializeField] private Button _openButton;

        [Tooltip("整块浮层（默认失活）。")]
        [SerializeField] private GameObject _layer;

        [Tooltip("点遮罩关闭。")]
        [SerializeField] private Button _veilButton;

        [Tooltip("「返回」。")]
        [SerializeField] private Button _closeButton;

        [Tooltip("「对局双方」那一行的值按钮。")]
        [SerializeField] private Button _lineupButton;

        [SerializeField] private TMP_Text _lineupValue;

        [Tooltip("「经典模式」那一行的值按钮。")]
        [SerializeField] private Button _classicButton;

        [SerializeField] private TMP_Text _classicValue;

        [Tooltip("「怪物智力」那一行的值按钮。")]
        [SerializeField] private Button _difficultyButton;

        [SerializeField] private TMP_Text _difficultyValue;

        // ── 监听器（铁律 8：一律运行时接，可重入）──────────────
        private UnityAction _openHandler;
        private UnityAction _veilHandler;
        private UnityAction _closeHandler;
        private UnityAction _lineupHandler;
        private UnityAction _classicHandler;
        private UnityAction _difficultyHandler;

        private void Awake()
        {
            Hook();
        }

        // ⚠ 注册写在 OnEnable 而不是只在 Awake：Awake 每个对象只跑一次，
        //   而 OnEnable 每次「重新激活」都会跑。Hook 自己先 Add 再 Remove，走两次也不会重复。
        private void OnEnable()
        {
            Hook();
            Refresh();
        }

        private void OnDisable()
        {
            Unhook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        private void Hook()
        {
            Wire(_openButton, ref _openHandler, Open);
            Wire(_veilButton, ref _veilHandler, Close);
            Wire(_closeButton, ref _closeHandler, Close);
            Wire(_lineupButton, ref _lineupHandler, CycleLineup);
            Wire(_classicButton, ref _classicHandler, ToggleClassic);
            Wire(_difficultyButton, ref _difficultyHandler, ToggleDifficulty);
        }

        private void Unhook()
        {
            Clear(_openButton, ref _openHandler);
            Clear(_veilButton, ref _veilHandler);
            Clear(_closeButton, ref _closeHandler);
            Clear(_lineupButton, ref _lineupHandler);
            Clear(_classicButton, ref _classicHandler);
            Clear(_difficultyButton, ref _difficultyHandler);
        }

        /// <summary>把一枚按钮接到自己的处理上（幂等：先摘旧的再挂新的）。</summary>
        private static void Wire(Button button, ref UnityAction handler, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            // ⚠ 不能用 `if (handler != null) Remove`：handler 是本实例的缓存，
            //   而按钮上可能还留着上一次（另一份实例 / 构建器）挂的东西。
            //   直接 RemoveListener(action) 是无害的（不存在时什么都不做）。
            button.onClick.RemoveListener(action);
            handler = action;
            button.onClick.AddListener(handler);
        }

        private static void Clear(Button button, ref UnityAction handler)
        {
            if (button != null && handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = null;
        }

        // ══════════════════════════════════════════════════════
        //  开 / 关
        // ══════════════════════════════════════════════════════

        /// <summary>打开面板：重读一次设置（防止别处改过 PlayerPrefs），再把三行刷成当前值。</summary>
        public void Open()
        {
            GameSettings.Reload();
            Refresh();

            if (_layer != null)
            {
                _layer.SetActive(true);
                _layer.transform.SetAsLastSibling();
            }
        }

        /// <summary>关掉面板。</summary>
        public void Close()
        {
            if (_layer != null)
            {
                _layer.SetActive(false);
            }
        }

        public bool IsOpen
        {
            get { return _layer != null && _layer.activeSelf; }
        }

        // ══════════════════════════════════════════════════════
        //  三行设置
        // ══════════════════════════════════════════════════════

        /// <summary>对局双方：玩家对 AI → AI 对 AI → 玩家对玩家 → 回到第一档。</summary>
        private void CycleLineup()
        {
            MatchLineup next;
            switch (GameSettings.Lineup)
            {
                case MatchLineup.PlayerVsAi:
                    next = MatchLineup.AiVsAi;
                    break;

                case MatchLineup.AiVsAi:
                    next = MatchLineup.PlayerVsPlayer;
                    break;

                default:
                    next = MatchLineup.PlayerVsAi;
                    break;
            }

            GameSettings.Lineup = next;
            Refresh();
        }

        /// <summary>经典模式：开 / 关。</summary>
        private void ToggleClassic()
        {
            GameSettings.ClassicOnly = !GameSettings.ClassicOnly;
            Refresh();
        }

        /// <summary>怪物智力：标准 / 简单。</summary>
        private void ToggleDifficulty()
        {
            GameSettings.Difficulty = GameSettings.Difficulty == AiDifficulty.Easy
                ? AiDifficulty.Standard
                : AiDifficulty.Easy;
            Refresh();
        }

        /// <summary>把三行的值刷成当前设置。</summary>
        private void Refresh()
        {
            if (_lineupValue != null)
            {
                _lineupValue.text = GameSettings.LineupName(GameSettings.Lineup);
            }

            if (_classicValue != null)
            {
                _classicValue.text = GameSettings.ClassicName(GameSettings.ClassicOnly);
            }

            if (_difficultyValue != null)
            {
                _difficultyValue.text = GameSettings.DifficultyName(GameSettings.Difficulty);
            }
        }
    }
}
