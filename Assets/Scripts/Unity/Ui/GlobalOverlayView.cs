using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>每个场景都长一样的那一层</b>（M44 · 2026-10-05）：右上角一枚「设置」键、
    /// 右下角一枚「卡池」键，各自点开一块浮层。
    ///
    /// <para><b>为什么做成一个通用组件而不是每个场景各接一遍</b>：这五块界面
    /// （战斗 / 地图 / 商店 / 石台 / 女巫工坊）是五棵互不相干的 Canvas 树，
    /// 「设置键在哪、点开是什么」如果各写一份，改一处必然漏一处 —— 而漏掉的样子是
    /// <b>零报错</b>的：某个场景的右上角干脆没有那颗键，或者点下去什么都不发生。
    /// 所以节点由同一个构建器（<c>GlobalOverlayBuilder</c>）生成、行为由本类一处实现。</para>
    ///
    /// <para><b>设置面板里只有两项</b>：<b>继续游戏</b>（关掉面板）与
    /// <b>退出到主菜单</b>（用户 2026-10-05 口径：「所有场景统一只放退出」）。
    /// 战斗里原有的「重新开始」「测试卡池」两枚已随本次改造<b>从 Prefab 上移除</b>
    /// （源文件 <c>SettingsView</c> / <c>SettingsUiBuilder</c> 仍在，但没有节点再引用它们）。</para>
    ///
    /// <para><b>「退出」到底做了什么</b>：<c>MapRun.Clear()</c> + <c>GameSession.Forget()</c>
    /// + <c>LoadScene(Home)</c> —— 与战斗失败界面上那枚「退出」<b>同一套</b>
    /// （那里是 <c>BattleUi.HandleQuitBattle</c>）。语义是「放弃这一趟，回开始界面重新选档」，
    /// 所以会顺手清掉冒险进度，不然回主菜单再进来会接着上一趟的图，而档已经换了。</para>
    ///
    /// <para><b>卡池面板只读</b>：内容是<b>当前存档那一份玩家卡池</b>（与战斗 / 商店 / 女巫工坊
    /// 读的是同一个玩家的同一份账）。战斗场景多一条：<b>本局已经抽到过手牌的牌压暗</b> ——
    /// 「哪些牌这局已经出现过了」由战斗侧通过 <see cref="DimmedProvider"/> 注入，
    /// 本类不认识对局状态。</para>
    ///
    /// <para><b>⚠ 面板节点在构建器里就是失活的</b>（和商店背包 / 选牌弹窗同一口径）：
    /// 首次 <c>SetActive(true)</c> 才触发 <c>Awake</c>，而首次 Show 里的 Hide 会把刚显示的东西
    /// 按灭 —— 所以两块的 <c>Awake</c> 里<b>都不许</b>调 Hide。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlobalOverlayView : MonoBehaviour
    {
        // ── 两枚常驻键（构建器接线）──────────────────────────────

        /// <summary>右上角的「设置」键。</summary>
        [SerializeField] private Button _settingsButton;

        /// <summary>右下角的「卡池」键。</summary>
        [SerializeField] private Button _poolButton;

        // ── 设置层 ──────────────────────────────────────────────

        /// <summary>设置层整棵（默认失活）。</summary>
        [SerializeField] private GameObject _settingsLayer;

        /// <summary>设置层的遮罩（点它关闭）。</summary>
        [SerializeField] private Button _settingsVeil;

        /// <summary>「继续游戏」。</summary>
        [SerializeField] private Button _resumeButton;

        /// <summary>「退出到主菜单」。</summary>
        [SerializeField] private Button _quitButton;

        // ── 卡池层 ──────────────────────────────────────────────

        /// <summary>卡池面板（默认失活；它自己管遮罩与关闭键）。</summary>
        [SerializeField] private CardPoolView _cardPool;

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
        /// 玩家在开始界面<b>没选过档</b>时，本场景按哪个档读卡池
        /// （与 <c>BattleDriver._saveSlot</c> / 各事件场景 Inspector 里那一格同口径）。
        /// 玩家选过档则以 <see cref="GameSession"/> 为准。
        /// </summary>
        [SerializeField] private SaveSlot _slot = SaveSlot.Main;

        /// <summary>
        /// 开 = 卡池里「本局已经抽到过手牌」的牌压暗。<b>只有战斗场景为真</b>（构建器按场景填）。
        /// 其余场景没有「本局」这个概念，压暗了反而像是坏了。
        /// </summary>
        [SerializeField] private bool _dimDrawnCards;

        /// <summary>
        /// <b>战斗侧注入</b>：本局已经抽到过手牌的卡（按基础 ID）。
        /// 为 null（或 <see cref="_dimDrawnCards"/> 为假）= 一张都不压暗。
        /// </summary>
        public Func<ICollection<string>> DimmedProvider;

        /// <summary>
        /// <b>战斗侧注入</b>：本局<b>实际在用</b>的那份卡池。为 null 时回落成
        /// 「读当前存档的玩家卡池」。
        ///
        /// <para><b>为什么战斗非要注入不可</b>：战斗读的是 <c>BattleDriver.ActiveSaveSlot</c>，
        /// 而当那个档<b>还不存在</b>时战斗会回退到角色自带的卡池（全 45 张）——
        /// 于是「存档里的卡池」与本局实际的牌堆根本不是同一份。不注入的话症状是
        /// <b>玩家手里那几张牌在卡池面板里一张都找不到</b>（只靠 id 偶然重合），
        /// 而且打开面板本身还会顺手新建一个存档。注入之后两边永远是同一份。</para>
        /// </summary>
        public Func<IReadOnlyList<CardDef>> CardsProvider;

        /// <summary>
        /// 设置面板打开 / 关闭时通知上层（参数 = 是否打开）。
        /// <b>战斗侧用它暂停对局推进</b>：设置面板开着的时候引擎不该继续走。
        /// </summary>
        public Action<bool> SettingsVisibilityChanged;

        /// <summary>设置面板是不是开着。</summary>
        public bool IsSettingsOpen
        {
            get { return _settingsLayer != null && _settingsLayer.activeSelf; }
        }

        /// <summary>卡池面板是不是开着。</summary>
        public bool IsCardPoolOpen
        {
            get { return _cardPool != null && _cardPool.IsOpen; }
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
            Wire(_poolButton, OnPoolClicked);
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
            CloseCardPool();

            if (_settingsLayer != null && !_settingsLayer.activeSelf)
            {
                _settingsLayer.SetActive(true);
                _settingsLayer.transform.SetAsLastSibling();
            }

            NotifySettings(true);
        }

        /// <summary>关掉设置面板。</summary>
        public void CloseSettings()
        {
            if (_settingsLayer == null || !_settingsLayer.activeSelf)
            {
                return;
            }

            _settingsLayer.SetActive(false);
            NotifySettings(false);
        }

        /// <summary>打开卡池面板（右下角那枚键）。开着则重画一遍（卡池可能刚变过）。</summary>
        public void OpenCardPool()
        {
            CloseSettings();

            if (_cardPool == null)
            {
                Debug.LogWarning("[GlobalOverlay] 没有接 CardPoolView —— 先跑 `魔法乱斗/M44 · 构建全局覆盖层`。");
                return;
            }

            List<CardDef> cards = new List<CardDef>();
            ICollection<string> dimmed = null;
            LoadCardPool(cards, out dimmed);

            _cardPool.Show(cards, dimmed, "卡池", _dimDrawnCards ? UiLayout.OverlayPoolDimHint : null);
        }

        /// <summary>关掉卡池面板。</summary>
        public void CloseCardPool()
        {
            if (_cardPool != null)
            {
                _cardPool.Hide();
            }
        }

        /// <summary>两块都关掉。</summary>
        public void HideAll()
        {
            CloseSettings();
            CloseCardPool();
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

            if (_poolButton != null && _poolButton.gameObject.activeSelf != visible)
            {
                _poolButton.gameObject.SetActive(visible);
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

        private void OnPoolClicked()
        {
            if (IsCardPoolOpen)
            {
                CloseCardPool();
                return;
            }

            OpenCardPool();
        }

        private void OnResumeClicked()
        {
            CloseSettings();
        }

        /// <summary>
        /// 「退出到主菜单」。
        ///
        /// <para><b>⚠ 顺序不能反</b>：必须<b>先清运行态、再切场景</b>。
        /// 反过来的话新场景（Home）的 <c>Awake</c> 会读到还没清掉的 <c>MapRun</c>，
        /// 而 Home 本身不显示冒险进度 —— 于是「进了主菜单、再进地图又接着上一趟」，
        /// 换档也没用。</para>
        /// </summary>
        private void OnQuitClicked()
        {
            HideAll();
            MapRun.Clear();
            GameSession.Forget();

            try
            {
                SceneManager.LoadScene(MapRoutes.HomeScene);
            }
            catch (Exception ex)
            {
                // Home 没登记进 Build Settings 时 LoadScene 会抛 —— 给一句人话，
                // 别让「点退出什么都不发生」变成查不出来的问题。
                Debug.LogError("[GlobalOverlay] 退出失败：场景 '" + MapRoutes.HomeScene
                               + "' 加载不了（先跑 `魔法乱斗/P0 · 构建 Home 场景`）。" + ex.Message);
            }
        }

        private void NotifySettings(bool open)
        {
            if (SettingsVisibilityChanged != null)
            {
                SettingsVisibilityChanged(open);
            }
        }

        // ══════════════════════════════════════════════════════
        //  卡池数据
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把「当前存档的玩家卡池」读成一张卡定义表（已按该玩家的强化册解析）。
        ///
        /// <para><b>⚠ 档位口径</b>：玩家在开始界面选过档就用他选的那个，
        /// 否则用本场景 Inspector 填的那个 —— 与各场景自己读存档的口径完全一致，
        /// 所以「单独打开商店进 Play 调试」时看到的还是商店那一档，主菜单一个字节都影响不到它。</para>
        /// </summary>
        private void LoadCardPool(List<CardDef> cards, out ICollection<string> dimmed)
        {
            cards.Clear();
            dimmed = null;

            // ① 战斗：用本局实际那份卡池（见 CardsProvider 的说明）。
            if (CardsProvider != null)
            {
                IReadOnlyList<CardDef> own = CardsProvider();
                if (own != null)
                {
                    for (int i = 0; i < own.Count; i++)
                    {
                        if (own[i] != null)
                        {
                            cards.Add(own[i]);
                        }
                    }
                }

                if (_dimDrawnCards && DimmedProvider != null)
                {
                    dimmed = DimmedProvider();
                }

                return;
            }

            // ② 其余场景：读当前存档的玩家卡池。
            SaveSlot slot = SaveSlots.Active(_slot);

            CardCatalogAsset asset = Resources.Load<CardCatalogAsset>("CardCatalog");
            ICardCatalog catalog = asset != null ? asset.CreateCatalog() : CardCatalog.Builtin();

            IReadOnlyList<string> ids;
            ICardCatalog resolved;
            string error;
            if (!SaveStore.TryLoadCardIds(slot, catalog, out ids, out resolved, out error))
            {
                Debug.LogWarning("[GlobalOverlay] 读卡池失败（" + SaveSlots.DisplayName(slot) + "）：" + error);
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                CardDef def = FindById(resolved, ids[i]);
                if (def != null)
                {
                    cards.Add(def);
                }
            }

            if (_dimDrawnCards && DimmedProvider != null)
            {
                dimmed = DimmedProvider();
            }
        }

        /// <summary>
        /// 按 ID 在目录里找一张卡。
        ///
        /// <para><b>⚠ 不能直接用 <c>ICardCatalog.Get</c></b>：它在未知 ID 上<b>抛异常</b>，
        /// 而卡池里完全可能留着一张「目录里已经没有的牌」（卡表改过、强化配方对不上）——
        /// 那会让整个面板打不开。这里找不到就跳过。</para>
        /// </summary>
        private static CardDef FindById(ICardCatalog catalog, string id)
        {
            if (catalog == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            IReadOnlyList<CardDef> all = catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].Id, id, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
