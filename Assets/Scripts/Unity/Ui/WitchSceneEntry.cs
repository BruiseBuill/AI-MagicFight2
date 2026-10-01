using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊的<b>单场景入口</b>（2026-10-01 · P6 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>它把这一整套串起来</b>：点水晶球 → 判卡池够不够 → 弹两个空位的浮层 →
    /// 点空位弹卡池浏览 → 选牌填坑 → 两个都填上且不是同一张才让确认 →
    /// 确认之后<b>第一张从卡池里消失</b>（用户 2026-10-01 口径）。</para>
    ///
    /// <para><b>为什么需要它</b>：女巫工坊是冒险模式里的一个事件节点，正式流程中它会从地图层
    /// 接到一份 <c>RunSnapshot</c>（这一趟冒险已持有的牌）+ 一个节点种子。那套链路还没接，
    /// 而<b>每个事件类型都必须能被单独进 Play 调试</b>（架构文档 §3），
    /// 否则想知道「卡池只有 8 张时到底拦不拦得住」就得先把整条冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：卡池从<see cref="SaveSlot.Main"/>读（或由序列化字段覆盖）；</item>
    /// <item><b>E2 不依赖前一节点</b>：直接以「主存档里那份卡池」开局；</item>
    /// <item><b>E3 读存档、也写存档</b>（2026-10-01 统一卡池时订正，原先是「完全不碰存档」）：
    /// 玩家卡池从 <see cref="SaveSlot.Main"/> 读；「第一张牌被消耗」会<b>写回主存档</b>
    /// （用户 2026-10-01 确认改口径 —— 原先只活在这一局的内存里，退出重进场景就恢复）。
    /// ⚠ 只有卡池确实来自主存档时才写（<c>_cardIds</c> / <c>_pool</c> 调试覆盖时不写）；</item>
    /// <item><b>E4 Core 一行不改</b>：这里只<b>读</b>卡表与 <see cref="WitchWorkshop"/> 的判定
    /// （那是唯一的规则实现），不存在「第二套特殊强化逻辑」。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：<see cref="Rebuild"/> 里那段「解析持有牌」现在是
    /// <b>读主存档</b>（2026-10-01 起，不再是卡池资产，与 <c>UpgradeSceneEntry</c> /
    /// <c>ShopSceneEntry</c> 同一待遇）—— 正式接入 run 时换成从地图层递进来的
    /// <c>RunSnapshot</c> 读，其余流程（选两个空位 → 确认 → 消耗 → 写回 → 结束节点）
    /// 可以原样留用。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：构建器把它挂在 <b>Canvas 的父级（场景根）</b>，
    /// 而存 Prefab 时只保存 Canvas 那一棵子树 —— 它天然不会进 Prefab。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchSceneEntry : MonoBehaviour
    {
        // ── 调试参数（正式接入后由地图层 / 存档给）────────────────────

        [Header("调试 · 卡池")]
        [Tooltip("调试覆盖：直接指向一份卡池资产（不读存档）。\n"
                 + "留空 = 读**主存档**（存档 2）里那个玩家的卡池 —— 与战斗 / 商店 / 强化同一份"
                 + "（2026-10-01 起；原先默认的 Resources/Pools/UpgradePool 已不再被读取）。\n"
                 + "主存档首次创建是随机 10 张，所以默认进来是**能开工**的。\n"
                 + "⚠ 卡池 ≤ 8 张时本场景会提示「无法进行特殊强化」—— 那是**正确行为**，"
                 + "想验那个分支就把卡池消耗到 8 张以下，或在这里指向一份 8 张的资产。")]
        [SerializeField] private CardPoolConfig _pool;

        [Tooltip("直接写卡 ID 覆盖上面的卡池资产（留空 = 用资产）。\n"
                 + "调试用：想验「卡池只有 8 张」就留空、想验正常流程就填 9 个以上。")]
        [SerializeField] private string[] _cardIds = new string[0];

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「离开」的点击打到 Console —— 方便在单场景里确认事件通了"
                 + "（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logLeaveClick = true;

        [Tooltip("勾上 = 确认完就结束这个节点，水晶球不再可点（用户 2026-10-01 口径）。\n"
                 + "取消勾选 = 留在场景里可以接着做第二次（只用于调试）。")]
        [SerializeField] private bool _endNodeAfterConfirm = true;

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private WitchView _view;

        /// <summary>当前卡池里的牌（**可变** —— 确认之后第一张会从这里消失）。</summary>
        private readonly List<CardDef> _cards = new List<CardDef>();

        /// <summary>卡池里的卡 ID（与 <see cref="_cards"/> 一一对应）。</summary>
        private readonly List<string> _ids = new List<string>();

        /// <summary>本次运行用的卡目录（内置 45 张 + 已落盘的强化卡）。</summary>
        private ICardCatalog _catalog;

        /// <summary>左空位（会被消耗掉的那张）。</summary>
        private CardDef _sacrifice;

        /// <summary>右空位（强化目标）。</summary>
        private CardDef _target;

        /// <summary>正在为哪个空位挑牌（−1 = 没在挑）。</summary>
        private int _pendingSlot = -1;

        /// <summary>
        /// 本次 <see cref="_ids"/> 是不是从<b>主存档</b>读来的。
        ///
        /// <para>只有为 <c>true</c> 才允许把「消耗掉第一张」写回存档：
        /// 调试覆盖（<c>_cardIds</c> / <c>_pool</c>）与「读档失败」两种情形都不该
        /// 拿手上的数据去覆盖真实存档。</para>
        /// </summary>
        private bool _loadedFromMainSave;

        /// <summary>节点已结束（确认完 / 点过离开）—— 水晶球不再响应。</summary>
        private bool _ended;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<WitchView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[WitchSceneEntry] 没有接 WitchView —— 先跑 `魔法乱斗/P6 · 构建 WitchWorkshop 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）：构建器接的那一份不是序列化数据。
            _view.OrbClicked += OnOrbClicked;
            _view.LeaveClicked += OnLeaveClicked;

            if (_view.Layer != null)
            {
                _view.Layer.SlotClicked += OnSlotClicked;
                _view.Layer.ConfirmClicked += OnConfirmClicked;
                _view.Layer.Closed += OnLayerClosed;
            }

            if (_view.Picker != null)
            {
                _view.Picker.Picked += OnPicked;
                _view.Picker.Closed += OnPickerClosed;
            }

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.OrbClicked -= OnOrbClicked;
            _view.LeaveClicked -= OnLeaveClicked;

            if (_view.Layer != null)
            {
                _view.Layer.SlotClicked -= OnSlotClicked;
                _view.Layer.ConfirmClicked -= OnConfirmClicked;
                _view.Layer.Closed -= OnLayerClosed;
            }

            if (_view.Picker != null)
            {
                _view.Picker.Picked -= OnPicked;
                _view.Picker.Closed -= OnPickerClosed;
            }
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
            _sacrifice = null;
            _target = null;
            _pendingSlot = -1;

            _view.SetTitle("女巫的工坊");
            _view.SetLeaveLabel("离开");
            _view.SetHint("点击水晶球，进行一次特殊强化（献祭一张牌，强化另一张）");
            _view.SetHintVisible(true);
            _view.SetOrbInteractable(true);
            _view.CloseAll();

            _catalog = ResolveCatalog();
            ResolveIds();
            ResolveCards();

            Debug.Log("[WitchSceneEntry] 卡池 " + _cards.Count + " 张 · 可作目标 " + CountTargets()
                      + " 张 · 门槛 " + (WitchWorkshop.CanOpen(_cards.Count) ? "已达（≥"
                          + WitchWorkshop.MinPoolSize + "）" : "未达（需 ≥"
                          + WitchWorkshop.MinPoolSize + "）")
                      + (_pool != null ? "（来源 " + _pool.displayName + "）" : ""));
        }

        // ══════════════════════════════════════════════════════
        //  卡池
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 取本次运行用的卡目录。
        ///
        /// <para>⚠ 必须和战斗 / 强化用的是**同一份** <c>CardCatalog</c> ——
        /// 强化卡（<c>"a+"</c>）只存在于那份目录的「动态卡」清单里，
        /// 用 <c>CardCatalog.Builtin()</c> 去查会查不到。</para>
        /// </summary>
        private ICardCatalog ResolveCatalog()
        {
            CardCatalogAsset asset = Resources.Load<CardCatalogAsset>("CardCatalog");
            return asset == null ? CardCatalog.Builtin() : asset.CreateCatalog();
        }

        /// <summary>
        /// 解析「玩家现在持有哪些牌」的 ID：Inspector 覆盖 &gt; 调试卡池资产 &gt; <b>主存档</b>。
        ///
        /// <para><b>⚠ 2026-10-01 统一</b>：原先第三档是 <c>Resources/Pools/UpgradePool</c>
        /// （8 张）—— 与商店看的、战斗看的都不是同一份。现在默认读 <see cref="SaveSlot.Main"/>；
        /// 商店买下的牌会出现在这里（本场景的卡池门槛因此从「永远不够」变成默认可开工）。</para>
        ///
        /// <para><b>⚠ 必须过 <see cref="CardUpgrade.PreferUpgraded"/></b>：那里带着
        /// 「基础版 / 强化版只留一份」的规则。</para>
        /// </summary>
        private void ResolveIds()
        {
            _ids.Clear();
            _loadedFromMainSave = false;

            if (_cardIds != null && _cardIds.Length > 0)
            {
                for (int i = 0; i < _cardIds.Length; i++)
                {
                    AddId(_cardIds[i]);
                }

                return;
            }

            // 调试覆盖：直接指向一份卡池资产
            if (_pool != null)
            {
                IReadOnlyList<string> assetIds = _pool.ResolveIds(_catalog);
                for (int i = 0; i < assetIds.Count; i++)
                {
                    AddId(assetIds[i]);
                }

                return;
            }

            // 正式来源：主存档的玩家卡池
            PlayerData player;
            string error;
            if (!SaveStore.TryLoadPlayer(SaveSlot.Main, out player, out error))
            {
                Debug.LogWarning("[WitchSceneEntry] 读主存档失败，本局卡池为空：" + error);
                return;
            }

            if (player == null || player.cardIds == null)
            {
                return;
            }

            IReadOnlyList<string> ids = CardUpgrade.PreferUpgraded(player.cardIds, _catalog);
            for (int i = 0; i < ids.Count; i++)
            {
                AddId(ids[i]);
            }

            _loadedFromMainSave = true;
        }

        private void AddId(string id)
        {
            if (string.IsNullOrEmpty(id) || _ids.Contains(id))
            {
                return;
            }

            _ids.Add(id);
        }

        /// <summary>
        /// 把 ID 解析成卡定义。
        ///
        /// <para><b>⚠ 顺序按 <see cref="_ids"/>（卡池自己声明的顺序），不是按卡目录顺序</b> ——
        /// 与 <c>UpgradeSceneEntry</c> 同一条理由：强化卡的序号是「动态卡区」，
        /// 按目录序排会跳到网格最后一行。</para>
        /// </summary>
        private void ResolveCards()
        {
            _cards.Clear();

            var byId = new Dictionary<string, CardDef>(System.StringComparer.Ordinal);
            foreach (CardDef card in _catalog.All)
            {
                if (card != null && !byId.ContainsKey(card.Id))
                {
                    byId.Add(card.Id, card);
                }
            }

            for (int i = 0; i < _ids.Count; i++)
            {
                CardDef card;
                if (byId.TryGetValue(_ids[i], out card))
                {
                    _cards.Add(card);
                    continue;
                }

                // 少了这一条，症状是「卡池资产里写了 8 张，界面上只有 6 张」，而且不报任何错。
                Debug.LogWarning("[WitchSceneEntry] 卡池里的 ID 不在卡目录里，已跳过：" + _ids[i]);
            }
        }

        private int CountTargets()
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                string reason;
                if (WitchWorkshop.CanBeTarget(_cards[i], out reason))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// 把卡池翻译成浏览层要的格子数据（**唯一的规则判定点**）。
        ///
        /// <para>两个空位的候选集**都是整份卡池** —— 左空位全部可点（第一条口径：献祭不限牌），
        /// 右空位把不合格的压暗并写上原因（而不是从列表里去掉：玩家要能看见
        /// 「我有这张牌，它为什么不能当目标」，直接消失会被当成 bug）。</para>
        ///
        /// <para>⚠ <b>故意不过滤掉「左空位已经选中的那张」</b>：用户 2026-10-01 口径是
        /// 「这两个空位当中的牌不可以相同，<b>否则无法确认</b>」—— 也就是说允许选中、
        /// 只是不让确认。所以这里照旧全部列出，由 <see cref="RefreshLayer"/> 把确认键按灰。</para>
        /// </summary>
        private List<UpgradePickerView.Entry> BuildEntries(int slot)
        {
            var list = new List<UpgradePickerView.Entry>(_cards.Count);
            for (int i = 0; i < _cards.Count; i++)
            {
                CardDef card = _cards[i];
                var entry = new UpgradePickerView.Entry();
                entry.Card = card;

                if (slot == 0)
                {
                    entry.Upgradable = WitchWorkshop.CanBeSacrifice(card);
                    entry.Reason = null;
                }
                else
                {
                    string reason;
                    entry.Upgradable = WitchWorkshop.CanBeTarget(card, out reason);
                    entry.Reason = reason;
                }

                list.Add(entry);
            }

            return list;
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 点了水晶球：<b>先判卡池够不够</b>，不够就只给一句提示、连浮层都不弹
        /// （用户 2026-10-01 口径）。
        /// </summary>
        private void OnOrbClicked()
        {
            if (_ended || _view.IsLayerOpen)
            {
                return;
            }

            if (!WitchWorkshop.CanOpen(_cards.Count))
            {
                _view.ShowHintAsWarning(WitchWorkshop.OpenBlockedReason(_cards.Count), true);
                Debug.Log("[WitchSceneEntry] " + WitchWorkshop.OpenBlockedReason(_cards.Count));
                return;
            }

            _sacrifice = null;
            _target = null;
            _pendingSlot = -1;

            _view.OpenLayer("特殊强化", "点空位，从卡池里选一张牌");
            RefreshLayer();
        }

        /// <summary>点了某个空位：弹出卡池浏览，让玩家为它挑一张。</summary>
        private void OnSlotClicked(int slot)
        {
            if (_ended || _view.Picker == null)
            {
                return;
            }

            _pendingSlot = slot;
            _view.Picker.Show(
                BuildEntries(slot),
                slot == 0 ? "选择献祭的牌" : "选择要强化的牌",
                slot == 0
                    ? "这张牌会被消耗掉，从卡池里移除"
                    : "只有「卡面恰好 1 条效果、且从未强化过」的牌能当目标");
        }

        /// <summary>浏览层里点了一张牌：填进刚才那个空位。</summary>
        private void OnPicked(CardDef card)
        {
            int slot = _pendingSlot;
            _pendingSlot = -1;

            if (slot == 0)
            {
                _sacrifice = card;
            }
            else if (slot == 1)
            {
                _target = card;
            }
            else
            {
                return;
            }

            RefreshLayer();
        }

        /// <summary>浏览层关掉了（选完 / 取消都会发）。</summary>
        private void OnPickerClosed()
        {
            _pendingSlot = -1;
        }

        /// <summary>
        /// 玩家按了「确认」：<b>第一张牌从卡池里消失</b>，然后结束这个节点。
        ///
        /// <para>⚠ <b>强化效果本批不做</b>（用户 2026-10-01 口径「先不做强化的具体效果，
        /// 之后再做」），所以这里<b>不</b>生成新卡、<b>不</b>改第二张牌的任何字段 ——
        /// 只有左边那张离场。等效果定了，往 <see cref="WitchWorkshop"/> 里加一条口径再回来接。</para>
        /// </summary>
        private void OnConfirmClicked()
        {
            if (_ended)
            {
                return;
            }

            string reason;
            if (!WitchWorkshop.CanConfirm(_sacrifice, _target, out reason))
            {
                // 双保险：确认键本来就按灰了，这里再挡一次。
                _view.Layer.SetStatus(reason, true);
                return;
            }

            CardDef sacrifice = _sacrifice;
            CardDef target = _target;

            // 被献祭的那张从本局卡池里移除，并把结果**写回主存档**
            // （2026-10-01 统一卡池时改口径：原先只活在本局内存里、退出重进就恢复）。
            // ⚠ 顺序是先改内存再落盘：落盘失败时本局的画面仍然是对的，只多一条警告。
            int cardAt = _cards.IndexOf(sacrifice);
            if (cardAt >= 0)
            {
                _cards.RemoveAt(cardAt);
            }

            int idAt = _ids.IndexOf(sacrifice.Id);
            if (idAt >= 0)
            {
                _ids.RemoveAt(idAt);
            }

            PersistIds();

            _sacrifice = null;
            _target = null;

            _view.CloseAll();

            string message = "《" + sacrifice.Name + "》已被消耗（卡池 " + (_cards.Count + 1)
                             + " → " + _cards.Count + " 张），《" + target.Name
                             + "》等待特殊强化（效果待实现）";
            Debug.Log("[WitchSceneEntry] " + message);

            if (_endNodeAfterConfirm)
            {
                EndNode(message);
                return;
            }

            _view.ShowHintAsWarning(message, false);
            RefreshLayer();
        }

        /// <summary>
        /// 把「当前卡池」写回主存档（2026-10-01 起：消耗掉的那张<b>真的落盘</b>）。
        ///
        /// <para>⚠ 只在 <see cref="_loadedFromMainSave"/> 为真时写 —— 调试覆盖与读档失败
        /// 两种情形都不该拿手上的数据去覆盖真实存档。</para>
        ///
        /// <para>交给 <see cref="SaveStore.TrySaveCardIds"/> 的是**解析后**的 ID
        /// （可能带 <c>+</c>）；它会归一成基础 ID 再落盘 —— 存档里永远只写基础 ID。</para>
        /// </summary>
        private void PersistIds()
        {
            if (!_loadedFromMainSave)
            {
                return;
            }

            string error;
            if (!SaveStore.TrySaveCardIds(SaveSlot.Main, _ids, out error))
            {
                Debug.LogWarning("[WitchSceneEntry] 消耗没能写回主存档：" + error);
                return;
            }

            Debug.Log("[WitchSceneEntry] 主存档卡池已更新：" + _ids.Count + " 张");
        }

        private void OnLayerClosed()
        {
            // 已经结束 / 正在收尾时不要把引导文字按回默认那句 ——
            // 否则会出现「节点都结束了、屏幕上还写着『点击水晶球』」。
            if (_ended)
            {
                return;
            }

            _view.ShowHintAsWarning("点击水晶球，进行一次特殊强化（献祭一张牌，强化另一张）", false);
        }

        /// <summary>
        /// 结束这个节点。
        ///
        /// <para>用户 2026-10-01 口径：<b>一次特殊强化 = 一个事件节点</b>。
        /// 正式流程里这一下会提交「完成节点」并回地图层；本场景是独立调试场景，
        /// 所以只打日志 + 收摊（水晶球不可再点）。</para>
        /// </summary>
        private void EndNode(string message)
        {
            _ended = true;
            _view.SetOrbInteractable(false);
            _view.ShowHintAsWarning(message, false);

            if (_logLeaveClick)
            {
                Debug.Log("[WitchSceneEntry] 特殊强化完成，本节点结束（正式流程里这一下会回地图层）。");
            }
        }

        private void OnLeaveClicked()
        {
            _view.CloseAll();
            _view.SetHintVisible(true);

            if (_logLeaveClick)
            {
                Debug.Log("[WitchSceneEntry] 点了「离开」—— 单场景调试下到此为止；"
                          + "正式流程里这一下会提交「完成节点」并回地图层。");
            }
        }

        /// <summary>按当前两个空位刷新确认键与状态行（**唯一的界面判定点**）。</summary>
        private void RefreshLayer()
        {
            WitchLayerView layer = _view.Layer;
            if (layer == null)
            {
                return;
            }

            layer.SetSlots(_sacrifice, _target);

            string reason;
            bool ok = WitchWorkshop.CanConfirm(_sacrifice, _target, out reason);
            layer.SetConfirmEnabled(ok);

            if (ok)
            {
                layer.SetStatus("确认后，《" + _sacrifice.Name + "》会被消耗掉", false);
                return;
            }

            if (!WitchWorkshop.BothSlotsFilled(_sacrifice, _target))
            {
                bool any = _sacrifice != null || _target != null;
                layer.SetStatus(any ? "还需要为另一个空位选一张牌" : "点空位，从卡池里选一张牌", false);
                return;
            }

            // 两个都填了却不合格 —— 眼下只有「撞了同一张牌」这一种（目标牌的合格性在浏览层就拦住了）。
            layer.SetStatus(reason, true);
        }
    }
}
