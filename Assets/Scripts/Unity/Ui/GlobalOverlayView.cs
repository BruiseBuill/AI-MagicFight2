using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>每个场景都长一样的那一层</b>（M44 · 2026-10-05；2026-10-28 大改后只剩设置）。
    ///
    /// <para><b>它现在管什么</b>：右上角一枚「设置」键，点开一块两行浮层 ——
    /// 「继续游戏」与「退出到主菜单」。右下角那枚「卡池」键<b>已随冒险删掉</b>
    /// （卡池是冒险的产物；两种玩法下每局就是 8 张，看手牌即可）。</para>
    ///
    /// <para><b>设置面板里只有两项</b>：<b>继续游戏</b>（关掉面板）与
    /// <b>退出到主菜单</b>。战斗里那枚齿轮打开的是同一块面板。</para>
    ///
    /// <para><b>⚠ 面板节点在构建器里就是失活的</b>：首次 <c>SetActive(true)</c>
    /// 才触发 <c>Awake</c>，而首次 Show 里的 Hide 会把刚显示的东西按灭 ——
    /// 所以两块的 <c>Awake</c> 里<b>都不许</b>调 Hide。</para>
    /// </summary>
    public sealed class GlobalOverlayView : MonoBehaviour
    {
        // ── 两枚常驻键（构建器接线）──────────────────────────────

        /// <summary>右上角的「设置」键。</summary>
        [SerializeField] private Button _settingsButton;

        // ── 设置层 ──────────────────────────────────────────────

        /// <summary>设置层整棵（默认失活）。</summary>
        [SerializeField] private GameObject _settingsLayer;

        /// <summary>设置层的遮罩（点它关闭）。</summary>
        [SerializeField] private Button _settingsVeil;

        /// <summary>「继续游戏」。</summary>
        [SerializeField] private Button _resumeButton;

        /// <summary>「退出到主菜单」。</summary>
        [SerializeField] private Button _quitButton;

        // ── 隐藏入口（2026-10-05）────────────────────────────────

        /// <summary>
        /// <b>设置面板里的隐藏热点</b>：一个铺在面板标题上、完全透明的 Button。
        ///
        /// <para><b>为什么要有它</b>：用户口径（2026-10-05）——「战斗界面当中增加一个隐藏选项，
        /// 点击之后可以解锁全部的设置内容」。M44 这块面板只有「继续游戏 / 退出到主菜单」
        /// 两行（是有意收窄的），而战斗里原本还有一堆调试内容
        /// （强制显示对手手牌、改玩家自己的卡池）—— 它们连同 M39 的 <c>SettingsLayer</c>
        /// 一起被 M44 从 Canvas 上删掉了，于是「功能还在、入口没了」。</para>
        ///
        /// <para>点它 → 关掉这块面板、打开那份完整设置层（见 <see cref="_fullSettings"/>）。</para>
        /// </summary>
        [SerializeField] private Button _secretHotspot;

        /// <summary>
        /// 隐藏热点解锁出来的<b>完整设置层</b>（M39 的 <c>SettingsLayer</c>，只建在战斗画布上）。
        ///
        /// <para>它自带遮罩、自带「继续游戏 / 重新开始 / 测试功能 · 玩家卡池」三枚键，
        /// 以及 M43 加进去的「强制显示怪物手牌（AI 调试）」开关 ——
        /// 由 <c>M45 · 战斗设置·隐藏入口</c> 重建并接线。其余四个场景整层都没有它（这里为 null）。</para>
        /// </summary>
        [SerializeField] private SettingsView _fullSettings;

        // ── 数据 ────────────────────────────────────────────────

        /// <summary>
        /// 设置面板打开 / 关闭时通知上层（参数 = 是否打开）。
        /// <b>战斗侧用它暂停对局推进</b>：设置面板开着的时候引擎不该继续走。
        /// </summary>
        public Action<bool> SettingsVisibilityChanged;

        /// <summary>设置面板是不是开着。</summary>
        public bool IsSettingsOpen
        {
            get { return UiPanelAnimator.IsShown(_settingsLayer); }
        }

        private void Awake()
        {
            Hook();
        }

        // ⚠ 注册写在 OnEnable 而不是只在 Awake：Awake 每个对象只跑一次，而 OnEnable
        //   每次「重新激活」都会跑。Hook() 自己先 Remove 再 Add，走到两次也不会重复触发。
        //   （这条口径是 2026-10-05 建 Home 时踩出来的：构建器接好的按钮，只在 Awake 里
        //    挂监听器会出现「首轮不生效、点下去零报错」。）
        private void OnEnable()
        {
            Hook();
        }

        /// <summary>
        /// 把两枚键接到自己的处理上（<b>可重入</b>：先摘旧的再挂新的）。
        ///
        /// <para><b>⚠ 铁律 8</b>：监听器一律在<b>运行时</b>按序列化字段自己接 —— 构建器在编辑模式
        /// 接的那些不是序列化数据，存 Prefab / 存场景时直接蒸发。</para>
        /// </summary>
        private void Hook()
        {
            Wire(_settingsButton, OnSettingsClicked);
            Wire(_settingsVeil, OnResumeClicked);
            Wire(_resumeButton, OnResumeClicked);
            Wire(_quitButton, OnQuitClicked);
            Wire(_secretHotspot, OnSecretHotspotClicked);
        }

        /// <summary>
        /// 隐藏热点被点了：关掉这块两行面板，打开完整的设置层。
        ///
        /// <para>没接 <see cref="_fullSettings"/>（四个事件场景 / 没跑过 M45 的老 Prefab）
        /// 时只打一条日志，不做任何事 —— 热点本身是透明按钮，点空不会有任何可见反应。</para>
        /// </summary>
        private void OnSecretHotspotClicked()
        {
            if (_fullSettings == null)
            {
                Debug.LogWarning("[GlobalOverlay] 隐藏入口没有接完整的设置层 —— "
                                 + "先跑 `魔法乱斗/M45 · 战斗设置·隐藏入口`。");
                return;
            }

            CloseSettings();
            _fullSettings.Open();
        }

        private void Wire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        // ══════════════════════════════════════════════════════
        //  对外
        // ══════════════════════════════════════════════════════

        /// <summary>打开设置面板（右上角那枚键 / 战斗顶栏齿轮都调它）。</summary>
        public void OpenSettings()
        {
            if (_settingsLayer != null)
            {
                UiPanelAnimator.SetVisible(_settingsLayer, true);
                _settingsLayer.transform.SetAsLastSibling();
            }

            NotifySettings(true);
        }

        /// <summary>关掉设置面板。</summary>
        public void CloseSettings()
        {
            if (!IsSettingsOpen)
            {
                return;
            }

            UiPanelAnimator.SetVisible(_settingsLayer, false);
            NotifySettings(false);
        }

        /// <summary>两块都关掉。</summary>
        public void HideAll()
        {
            CloseSettings();
        }

        /// <summary>
        /// 两枚常驻键显示 / 隐藏。<b>战斗侧在结算界面铺开时调它</b>：
        /// 那时候底下的对局已经结束，右上角还留着一枚能点开设置／卡池的键没有意义，
        /// 而且它排在结算界面之后就会盖在结算画面上。
        /// </summary>
        public void SetButtonsVisible(bool visible)
        {
            if (_settingsButton != null && _settingsButton.gameObject.activeSelf != visible)
            {
                _settingsButton.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                HideAll();
            }
        }

        // ══════════════════════════════════════════════════════
        //  按钮处理
        // ══════════════════════════════════════════════════════

        private void OnSettingsClicked()
        {
            if (IsSettingsOpen)
            {
                CloseSettings();
                return;
            }

            OpenSettings();
        }

        private void OnResumeClicked()
        {
            CloseSettings();
        }

        /// <summary>「退出到主菜单」（2026-10-28：冒险运行态已不存在，直接切场景）。</summary>
        private void OnQuitClicked()
        {
            HideAll();

            SceneRoutes.LoadHome();
        }

        private void NotifySettings(bool open)
        {
            if (SettingsVisibilityChanged != null)
            {
                SettingsVisibilityChanged(open);
            }
        }

    }
}
