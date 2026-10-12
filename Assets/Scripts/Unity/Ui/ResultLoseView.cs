using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>战斗失败的结算界面</b>（2026-10-05 · 用户口径「失败时要有重试与退出」）。
    ///
    /// <para><b>它管什么</b>：打输（或平局）之后铺上来的那块全屏界面 ——
    /// 顶徽记 + 「战斗失败」横幅 + 一行提示（原因 / 回合数）+ <b>两个键</b>：
    /// 左「重试」· 右「退出」。</para>
    ///
    /// <para><b>它不管什么</b>：「重试 = 用同一颗种子把这一局从头再来」「退出 = 回地图 / 停战」
    /// 这两件事<b>都不在这里</b> —— 本类只把 <see cref="Retried"/> / <see cref="Quit"/> 报出去，
    /// 由 <see cref="BattleUi"/> 决定调 <c>BattleDriver.StartBattle(LastSeed)</c> 还是切场景。
    /// 与 <see cref="ResultWinView"/> 同一条分工。</para>
    ///
    /// <para><b>版本与「平局」</b>：平局在冒险里按失败算（<c>冒险模式实施规格</c> §4.3），
    /// 所以它也走这一块，只把横幅文字换成「平局」（<see cref="Show"/> 的第一个参数）——
    /// 不另开一套界面。</para>
    ///
    /// <para><b>节点由构建器建</b>（<c>BattleUiBuilder.BuildResultLose</c>），版式常量在
    /// <c>UiLayout.ResultLose*</c>，配色复用 <c>UiTheme.ResultWin*</c>（同一套视觉语言）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResultLoseView : MonoBehaviour
    {
        [Header("接线（由 BattleUiBuilder 写入）")]
        [Tooltip("整块界面（含底图 / 暗纱）。开关就是切它的 activeSelf。")]
        [SerializeField] private GameObject _root;

        [Tooltip("横幅上的标题（「战斗失败」/「平局」）。")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("横幅下面那行提示（原因 / 回合数）。")]
        [SerializeField] private TMP_Text _hint;

        [Tooltip("左键「重试」—— 用同一颗种子把这一局从头再来。")]
        [SerializeField] private Button _retryButton;

        [Tooltip("右键「退出」—— 冒险里回地图（判失败）/ 单场景里停战。")]
        [SerializeField] private Button _quitButton;

        /// <summary>玩家点了「重试」。</summary>
        public event Action Retried;

        /// <summary>玩家点了「退出」。</summary>
        public event Action Quit;

        private bool _open;

        /// <summary>这块界面此刻铺着没有。</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>构建器的接线口。</summary>
        public void Configure(GameObject root, TMP_Text title, TMP_Text hint,
            Button retryButton, Button quitButton)
        {
            _root = root;
            _title = title;
            _hint = hint;
            _retryButton = retryButton;
            _quitButton = quitButton;
        }

        private void Awake()
        {
            // ⚠ 监听器一律在**运行时**按序列化字段自己接（铁律 8）：构建器在编辑模式
            //   接的那些不是序列化数据，存 Prefab / 存场景时直接蒸发 —— 症状是
            //   「每一项检查都通过，但按钮点下去什么都没有」。
            if (_retryButton != null)
            {
                _retryButton.onClick.AddListener(OnRetryClicked);
            }

            if (_quitButton != null)
            {
                _quitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        private void OnDestroy()
        {
            if (_retryButton != null)
            {
                _retryButton.onClick.RemoveListener(OnRetryClicked);
            }

            if (_quitButton != null)
            {
                _quitButton.onClick.RemoveListener(OnQuitClicked);
            }
        }

        /// <summary>铺开失败界面。</summary>
        /// <param name="title">横幅文字（「战斗失败」/「平局」）。</param>
        /// <param name="hint">横幅下面那行提示；空串时该行隐藏。</param>
        public void Show(string title, string hint)
        {
            _open = true;
            if (_root != null)
            {
                _root.SetActive(true);
            }

            if (_title != null)
            {
                _title.text = string.IsNullOrEmpty(title) ? "战斗失败" : title;
            }

            if (_hint != null)
            {
                _hint.text = hint ?? string.Empty;
                _hint.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            }
        }

        /// <summary>立刻收掉。</summary>
        public void Hide()
        {
            _open = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void OnRetryClicked()
        {
            if (!_open)
            {
                return;
            }

            Hide();
            Action handler = Retried;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnQuitClicked()
        {
            if (!_open)
            {
                return;
            }

            Hide();
            Action handler = Quit;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
