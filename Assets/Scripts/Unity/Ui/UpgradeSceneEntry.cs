using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 强化场景的<b>单场景入口</b>（2026-09-30 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>为什么需要它</b>：强化是冒险模式里的一个事件节点，正式流程中它会从地图层
    /// 接到一份 <c>RunSnapshot</c>（这一趟冒险已持有的牌）+ 一个节点种子。那套链路还没接，
    /// 而<b>每个事件类型都必须能被单独进 Play 调试</b>（架构文档 §3），
    /// 否则想知道「这张牌强化完是不是真的变 6」就得先把整条冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：卡池由序列化字段 / <see cref="CardPoolConfig"/> 给；</item>
    /// <item><b>E2 不依赖前一节点</b>：直接以「一份起始卡池」开局；</item>
    /// <item><b>E3 不写真实存档</b>：本类<b>完全不碰存档</b>（唯一会落盘的东西是强化后的
    /// <c>CardDefinitionAsset</c> —— 那是「新卡」本身，不是进度）；</item>
    /// <item><b>E4 Core 一行不改</b>：这里只<b>读</b>卡表与 <see cref="CardUpgrade"/> 的判定
    /// （那是唯一的规则实现），不存在「第二套强化逻辑」。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：正式接入时，「这一趟冒险持有哪些牌」应当由 Core 侧
    /// （run 的持有卡）算出再喂进来；本类里那段「从卡池资产解析」到时换成从 run 读，
    /// 其余流程（选牌 → 确认 → 落盘 → 动画 → 结束节点）可以原样留用。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：构建器把它挂在 <b>Canvas 的父级（场景根）</b>，
    /// 而存 Prefab 时只保存 Canvas 那一棵子树 —— 它天然不会进 Prefab。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeSceneEntry : MonoBehaviour
    {
        // ── 调试参数（正式接入后由地图层 / 存档给）────────────────────

        [Header("调试 · 卡池")]
        [Tooltip("这份卡池就是「玩家现在持有的牌」。留空 = 用 Resources/Pools/UpgradePool。")]
        [SerializeField] private CardPoolConfig _pool;

        [Tooltip("直接写卡 ID 覆盖上面的卡池资产（留空 = 用资产）。\n"
                 + "想验「卡池里一张可强化的都没有」就把这里填成 ag 之外的不可强化牌，或留空并改资产。")]
        [SerializeField] private string[] _cardIds = new string[0];

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「离开」的点击打到 Console —— 方便在单场景里确认事件通了"
                 + "（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logLeaveClick = true;

        [Tooltip("勾上 = 强化完（动画播完）就结束这个节点，台面不再可点（用户 2026-09-30 口径）。\n"
                 + "取消勾选 = 留在场景里可以接着强化第二张（只用于调试）。")]
        [SerializeField] private bool _endNodeAfterUpgrade = true;

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private UpgradeView _view;

        /// <summary>当前卡池里的牌（**可变** —— 强化完把那一张换成新定义）。</summary>
        private readonly List<CardDef> _cards = new List<CardDef>();

        /// <summary>卡池里的卡 ID（与 <see cref="_cards"/> 一一对应，解析与替换都按 ID 走）。</summary>
        private readonly List<string> _ids = new List<string>();

        /// <summary>本次运行用的卡目录（内置 42 张 + 已落盘的强化卡）。</summary>
        private ICardCatalog _catalog;

        /// <summary>节点已结束（强化完 / 点过离开）—— 台面不再响应。</summary>
        private bool _ended;

        /// <summary>强化动画正在播。</summary>
        private bool _busy;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<UpgradeView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[UpgradeSceneEntry] 没有接 UpgradeView —— 先跑 `魔法乱斗/P5 · 构建 Upgrade 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）：构建器接的那一份不是序列化数据。
            _view.TableClicked += OnTableClicked;
            _view.LeaveClicked += OnLeaveClicked;
            _view.UpgradeConfirmed += OnUpgradeConfirmed;
            _view.PickerClosed += OnPickerClosed;
            _view.FxFinished += OnFxFinished;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.TableClicked -= OnTableClicked;
            _view.LeaveClicked -= OnLeaveClicked;
            _view.UpgradeConfirmed -= OnUpgradeConfirmed;
            _view.PickerClosed -= OnPickerClosed;
            _view.FxFinished -= OnFxFinished;
        }

        /// <summary>按当前调试参数重载一次卡池（Inspector 的右键菜单也可以调）。</summary>
        [ContextMenu("重载卡池")]
        public void Rebuild()
        {
            if (_view == null)
            {
                return;
            }

            _ended = false;
            _busy = false;

            _view.SetTitle("强化卡牌");
            _view.SetLeaveLabel("离开");
            _view.SetHint("点击台面，选择一张牌强化（基础力量 +2，上限 9）");
            _view.SetHintVisible(true);
            _view.SetTableInteractable(true);
            _view.ClosePicker();

            _catalog = ResolveCatalog();
            ResolveIds();
            ResolveCards();

            Debug.Log("[UpgradeSceneEntry] 卡池 " + _cards.Count + " 张 · 可强化 " + CountUpgradable()
                      + " 张" + (_pool != null ? "（来源 " + _pool.displayName + "）" : ""));
        }

        // ══════════════════════════════════════════════════════
        //  卡池
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 取本次运行用的卡目录。
        ///
        /// <para>⚠ 必须和战斗 / 强化落盘用的是**同一份** <c>CardCatalog</c> ——
        /// 强化卡（<c>"a+"</c>）只存在于那份目录的「动态卡」清单里，
        /// 用 <c>CardCatalog.Builtin()</c> 去查会查不到（症状：强化完下一次进场景那张牌不见了）。</para>
        /// </summary>
        private ICardCatalog ResolveCatalog()
        {
            CardCatalogAsset asset = Resources.Load<CardCatalogAsset>("CardCatalog");
            return asset == null ? CardCatalog.Builtin() : asset.CreateCatalog();
        }

        /// <summary>解析「玩家现在持有哪些牌」的 ID：Inspector 覆盖 &gt; 卡池资产 &gt; 空。</summary>
        private void ResolveIds()
        {
            _ids.Clear();

            if (_cardIds != null && _cardIds.Length > 0)
            {
                for (int i = 0; i < _cardIds.Length; i++)
                {
                    AddId(_cardIds[i]);
                }

                return;
            }

            CardPoolConfig pool = _pool != null
                ? _pool
                : Resources.Load<CardPoolConfig>(CardPoolConfig.UpgradePoolResourcePath);

            if (pool == null)
            {
                Debug.LogWarning("[UpgradeSceneEntry] 找不到卡池资产（" + CardPoolConfig.UpgradePoolResourcePath
                                 + "）—— 先跑 `魔法乱斗/P5 · 构建 Upgrade 场景`。");
                return;
            }

            if (pool.useAllCards)
            {
                foreach (CardDef card in _catalog.All)
                {
                    AddId(card.Id);
                }

                return;
            }

            if (pool.cardIds == null)
            {
                return;
            }

            for (int i = 0; i < pool.cardIds.Length; i++)
            {
                AddId(pool.cardIds[i]);
            }
        }

        private void AddId(string id)
        {
            if (string.IsNullOrEmpty(id) || _ids.Contains(id))
            {
                return;
            }

            _ids.Add(id);
        }

        /// <summary>把 ID 解析成卡定义（**按卡目录顺序**，与 <see cref="CardPool.Resolve"/> 同口径）。</summary>
        private void ResolveCards()
        {
            _cards.Clear();

            foreach (CardDef card in _catalog.All)
            {
                if (card != null && _ids.Contains(card.Id))
                {
                    _cards.Add(card);
                }
            }

            // 目录里查不到的 ID 单独报一声 —— 少了这一条，症状是「卡池资产里写了 8 张，
            // 界面上只有 6 张」，而且不报任何错。
            for (int i = 0; i < _ids.Count; i++)
            {
                bool found = false;
                for (int c = 0; c < _cards.Count; c++)
                {
                    if (_cards[c].Id == _ids[i])
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Debug.LogWarning("[UpgradeSceneEntry] 卡池里的 ID 不在卡目录里，已跳过：" + _ids[i]);
                }
            }
        }

        private int CountUpgradable()
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                string reason;
                if (CardUpgrade.CanUpgrade(_cards[i], out reason))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>把卡池翻译成弹窗要的格子数据（**唯一的规则判定点**）。</summary>
        private List<UpgradePickerView.Entry> BuildEntries()
        {
            var list = new List<UpgradePickerView.Entry>(_cards.Count);
            for (int i = 0; i < _cards.Count; i++)
            {
                CardDef card = _cards[i];
                string reason;
                bool ok = CardUpgrade.CanUpgrade(card, out reason);
                list.Add(new UpgradePickerView.Entry { Card = card, Upgradable = ok, Reason = reason });
            }

            return list;
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        private void OnTableClicked()
        {
            if (_ended || _busy || _view.IsPickerOpen)
            {
                return;
            }

            _view.OpenPicker(BuildEntries(), "强化卡牌");
        }

        /// <summary>
        /// 玩家按了「确认」：生成新卡 →（编辑器里）落盘 → 换掉卡池里那一张 → 播动画。
        ///
        /// <para><b>⚠ 顺序不能换</b>：先落盘拿到「运行时该用的那一份定义」，再替换卡池，
        /// 最后才播动画（动画拿的就是新旧两份，播的是同一个事实）。</para>
        /// </summary>
        private void OnUpgradeConfirmed(CardDef card)
        {
            if (_ended || _busy || card == null)
            {
                return;
            }

            string reason;
            if (!CardUpgrade.CanUpgrade(card, out reason))
            {
                // 双保险：弹窗里不可选的格子本来就点不动，这里再挡一次。
                Debug.LogWarning("[UpgradeSceneEntry] 这张牌不能强化：" + card.Name + " —— " + reason);
                return;
            }

            CardDef upgraded = CardUpgrade.Apply(card, 20000 + _cards.Count);

            string note;
            upgraded = CardUpgradeWriter.Persist(upgraded, card, out note);

            int at = _cards.IndexOf(card);
            if (at < 0)
            {
                _cards.Add(upgraded);
            }
            else
            {
                _cards[at] = upgraded;
            }

            int idAt = _ids.IndexOf(card.Id);
            if (idAt < 0)
            {
                _ids.Add(upgraded.Id);
            }
            else
            {
                _ids[idAt] = upgraded.Id;
            }

            _busy = true;
            _view.PlayUpgradeFx(card, upgraded);

            Debug.Log("[UpgradeSceneEntry] 强化《" + card.Name + "》力量 " + card.Power + " → "
                      + upgraded.Power + "（" + note + "）");
        }

        private void OnPickerClosed()
        {
            // 已经结束 / 正在播动画时不要把引导文字放回来 ——
            // 否则会出现「节点都结束了、屏幕上还写着『点击台面强化』」。
            if (_ended || _busy)
            {
                return;
            }

            _view.SetHintVisible(true);
        }

        private void OnFxFinished()
        {
            _busy = false;

            if (_endNodeAfterUpgrade)
            {
                EndNode();
                return;
            }

            _view.SetHintVisible(true);
            _view.SetTableInteractable(true);
        }

        /// <summary>
        /// 结束这个节点。
        ///
        /// <para>用户 2026-09-30 口径：<b>一次强化一张，确认完就结束节点</b>。
        /// 正式流程里这一下会提交「完成节点」并回地图层；本场景是独立调试场景，所以只打日志 + 收摊。</para>
        /// </summary>
        private void EndNode()
        {
            _ended = true;
            _view.ClosePicker();
            _view.SetHintVisible(false);
            _view.SetTableInteractable(false);

            if (_logLeaveClick)
            {
                Debug.Log("[UpgradeSceneEntry] 强化完成，本节点结束（正式流程里这一下会回地图层）。");
            }
        }

        private void OnLeaveClicked()
        {
            _view.ClosePicker();

            if (_logLeaveClick)
            {
                Debug.Log("[UpgradeSceneEntry] 点了「离开」—— 单场景调试下到此为止；"
                          + "正式流程里这一下会提交「完成节点」并回地图层。");
            }
        }
    }
}
