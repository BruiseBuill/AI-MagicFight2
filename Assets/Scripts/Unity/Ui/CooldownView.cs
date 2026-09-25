using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 按剩余冷却分行显示纯牌面，双侧独立池化，纵向滚动保留完整卡高与行距。
    /// <b>「第几行」就是这张牌还剩几回合</b>（行 = 4 − 剩余冷却），卡面上的冷却数则是
    /// <b>这张牌的基础冷却值</b>（2026-09-20 用户口径，三处卡面统一按手牌的样式）。
    /// 光环统一显示在 HudBuff 中。
    ///
    /// <para>另有一件事落在这里（M26）：<b>区域加速 / 减速的入口是「点某一侧的某一格冷却槽」</b> ——
    /// 决定生效时两侧的 8 个槽位底图一起点亮（<see cref="SetZonePickable"/> /
    /// <see cref="SetZoneSeats"/>），点哪一格就把 (座位, 行) 报给
    /// <see cref="ZoneRowClicked"/>；行号与剩余冷却的换算是 <c>k = 4 − 行</c>。
    /// 本拍若<b>同时</b>还有逐张目标（加速 + 区域加速），区域档的判定区域<b>只留槽位徽标</b>，
    /// 卡面点击归还给卡牌（见 <see cref="SetZoneWithCardTargets"/>）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CooldownView : MonoBehaviour, ICardGestureHost
    {
        public int LocalSeat { get; private set; }
        public int OpponentSeat { get; private set; } = 1;
        public void ConfigureSeats(int localSeat, int opponentSeat)
        {
            LocalSeat = localSeat; OpponentSeat = opponentSeat;
            EnsureZoneClickCatchers();
        }

        [Header("Prefab / 挂载点")]
        [SerializeField] private CardView _cardPrefab;
        [SerializeField] private RectTransform _playerRoot;
        [SerializeField] private RectTransform _enemyRoot;

        [Header("M11 分行挂载点（下标 0 = 冷却区4 … 3 = 冷却区1）")]
        [Tooltip("4 行槽位。填了就走「按剩余冷却分行」的排布；留空则退回单一 Grid 的老行为。")]
        [SerializeField] private RectTransform[] _playerRows = new RectTransform[4];
        [SerializeField] private RectTransform[] _enemyRows = new RectTransform[4];

        [Header("M12：长按看完整卡面")]
        [SerializeField] private CardDetailView _peek;
        [SerializeField] private bool _peekOnLongPress = true;

        [Tooltip("目标类决策时，把冷却区里不可选的迷你卡压暗。")]
        [SerializeField] private bool _dimUnplayable = true;

        private readonly List<CardView> _playerShown = new List<CardView>();
        private readonly List<CardView> _playerPool = new List<CardView>();
        private readonly List<CardView> _enemyShown = new List<CardView>();
        private readonly List<CardView> _enemyPool = new List<CardView>();

        private readonly Dictionary<int, CardView> _reuse = new Dictionary<int, CardView>();
        private readonly HashSet<int> _playable = new HashSet<int>();

        /// <summary>
        /// 是否处在「本拍有冷却区目标」的限制态（整片只有 <see cref="_playable"/> 里的牌能点）。
        ///
        /// <para><b>为什么不能再用「哪一方的冷却区」来切</b>：加速 / 减速的目标<b>横跨双方</b>
        /// （见 <c>BattleEngine.IssueStage3Repeat</c>），旧版记「最后一个选项落在哪一方」的结果
        /// 是被后遍历的敌方覆盖 —— 玩家的迷你卡整片被压暗、点不动，
        /// 表现就是「加速减速只能选对方、选不了自己的卡」。</para>
        ///
        /// <para>改用「<b>选项里的 Uid 集合</b>」当判据：<c>CardInstance.Uid</c> 全对局唯一，
        /// 一份集合天然能同时容纳双方的目标。谁能点仍然只由引擎的 <c>Options</c> 说了算。</para>
        /// </summary>
        private bool _restricted;

        private void Awake()
        {
            ConfigureLayout();
            EnsureZoneClickCatchers();
        }

        public void ConfigureLayout()
        {
            ConfigureColumn(_playerRows, true);
            ConfigureColumn(_enemyRows, false);
        }

        private static void ConfigureColumn(RectTransform[] rows, bool player)
        {
            if (rows == null || rows.Length == 0 || rows[0] == null) return;
            var column = rows[0].parent as RectTransform;
            if (column == null) return;
            var viewport = column.parent as RectTransform;
            ScrollRect scroll = viewport == null ? null : viewport.GetComponent<ScrollRect>();

            // ⚠ 这里**不再运行时 new 节点**：滚动层（含 ScrollBar / Handle）由 M11 的
            //   BuildSlotColumn 预建成 ArtLayer/CoolingScroll_Player · CoolingScroll_Enemy。
            //   代码里造出来的节点在 Hierarchy 里看不见、调不了，版式只能靠猜。
            if (scroll == null)
            {
                Debug.LogWarning("[Cooldown] 冷却列里找不到预建的 ScrollRect —— "
                                 + "先跑菜单 `魔法乱斗/M11 · 构建 BattleArtLayer`");
                return;
            }

            float side = player ? 0f : 1f;
            const float top = 115f;
            float width = UiLayout.SlotColumnBudgetWidth + UiLayout.SlotMiniCardGap
                + UiLayout.SlotMiniCardWidth + (UiLayout.SlotMiniCardMax - 1) * UiLayout.SlotMiniCardStep;
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(side, 1f);
            viewport.anchoredPosition = new Vector2(player ? UiLayout.SlotLeft : -UiLayout.SlotRight, -top);
            viewport.sizeDelta = new Vector2(width, UiLayout.ReferenceHeight - UiLayout.HandAreaHeight - top);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(side, 1f);
            column.anchoredPosition = Vector2.zero;
            column.sizeDelta = new Vector2(width, UiLayout.SlotMiniCardHeight + (rows.Length - 1) * UiLayout.SlotRowPitch);
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null)
                    rows[i].anchoredPosition = new Vector2(0f,
                        -i * UiLayout.SlotRowPitch - (UiLayout.SlotMiniCardHeight - rows[i].rect.height) * 0.5f);
            scroll.viewport = viewport;
            scroll.content = column;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            scroll.inertia = true;
            if (scroll.verticalScrollbar == null)
            {
                Debug.LogWarning("[Cooldown] 预建的滚动条丢了（" + column.name + "）—— "
                                 + "先跑菜单 `魔法乱斗/M11 · 构建 BattleArtLayer`");
            }

            scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>点了一张冷却区的牌（加速 / 减速 / 移除类决策）。</summary>
        public event Action<CardView> CardClicked;

        /// <summary>
        /// 点了<strong>某一侧某一行的冷却槽</strong>—— 区域加速 / 减速用（M26）。
        ///
        /// <para>参数：<c>seat</c> = 哪一方（<see cref="LocalSeat"/> /
        /// <see cref="OpponentSeat"/>），<c>rowIndex</c> = 哪一行（0 = 冷却区4 … 3 = 冷却区1，
        /// 与 <see cref="CooldownView.BindGrouped"/> 的行号同口径）。</para>
        ///
        /// <para><b>为什么从「点整片」改成「点某一行」</b>：槽位徽标上写的就是这一行的含义
        /// （「N / 冷却区N」= 这张槽代表剩余冷却 N），玩家点哪一格，语义上就是选了
        /// 「该侧剩余冷却 = N 的那一批」。旧版把整个 674×621 视口涂成一片，
        /// 既盖住了迷你卡（用户 2026-09-21），又在同一侧有多档时无法表达选择。</para>
        /// </summary>
        public event Action<int, int> ZoneRowClicked;

        /// <summary>
        /// 侧别能不能点（区域类决策的「可点」态）。
        ///
        /// <para>这是**本视图自己的显示态**，不是规则判断 —— 具体点下去算不算数，
        /// 由 <see cref="BattleUi"/> 拿 (座位, 行) 去本拍的 <c>Options</c> 里对照（铁律 3）。</para>
        /// </summary>
        private bool _zonePickable;

        /// <summary>点亮 / 收掉「这一侧冷却槽可点」的高亮。</summary>
        public void SetZonePickable(bool on)
        {
            if (_zonePickable == on)
            {
                return;
            }

            _zonePickable = on;
            ApplyZoneHighlight(LocalSeat);
            ApplyZoneHighlight(OpponentSeat);
        }

        /// <summary>某一侧目前有没有可点的区域档（由 BattleUi 按 Options 告知，用来只点亮该点的那侧）。</summary>
        private readonly HashSet<int> _zoneSeats = new HashSet<int>();

        /// <summary>
        /// 本拍<strong>区域档与「逐张目标」并存</strong>吗（加速 + 区域加速 / 减速 + 区域减速，2026-09-25）。
        ///
        /// <para><b>为什么会并存</b>：一张牌同时带 <c>Haste</c> 与 <c>HasteZone</c> 时，
        /// 引擎把两件事合成<b>同一个决策</b>（<c>EffectWindow</c> 的 <c>combined</c> →
        /// <c>RequestKind.ChooseCooldownEffects</c>，提示条写「加速 / 区域加速」），
        /// 选项里既有「哪一张牌」也有「哪一方 · 剩余冷却 = k」——
        /// 玩家二选一，<c>MaxSelect = 1</c>。</para>
        ///
        /// <para><b>这个开关的作用</b>：为 true 时<see cref="OnCardClicked"/> 里的
        /// 「点卡面 = 点这一格」补路<b>关掉</b> —— 卡面点击归卡牌（逐张那一半），
        /// 区域档只剩<b>槽位徽标自己那块空白</b>可点。卡摆在槽的外侧
        /// （见 <see cref="BindGrouped"/> 的 <c>offset = rowW + gap</c>），
        /// 所以「判定区域收窄到不碰卡面」在几何上是成立的。</para>
        ///
        /// <para>为 false（本拍只有区域档，也就是 M26 原本那一拍）时补路照旧开着：
        /// 一行放满 4 张迷你卡时槽图露出的空白少得可怜，关掉就等于这个入口点不动。</para>
        /// </summary>
        private bool _zoneWithCardTargets;

        /// <summary>
        /// 告知「哪些座位这一拍真的能点」。传 null / 空集 = 两侧都点不了（收掉高亮）。
        ///
        /// <para>与 <see cref="SetZonePickable"/> 配合：前者管「此刻是不是区域决策」，
        /// 这个管「具体哪几侧」。只点亮真能点的那些侧 —— 区域加速是「选某一方的冷却区」，
        /// 引擎两个座位都会给选项，所以通常是两个；但雪崩（<c>SlowZoneBoth</c>）
        /// 只有一个 <c>Seat = -1</c> 的选项，那两侧都不该被点亮成「点一下就行」。</para>
        /// </summary>
        public void SetZoneSeats(HashSet<int> seats)
        {
            _zoneSeats.Clear();

            if (seats != null)
            {
                foreach (int s in seats)
                {
                    _zoneSeats.Add(s);
                }
            }

            ApplyZoneHighlight(LocalSeat);
            ApplyZoneHighlight(OpponentSeat);
        }

        /// <summary>
        /// 告知「本拍区域档与逐张目标并存」（见 <see cref="_zoneWithCardTargets"/>）。
        ///
        /// <para>由 <c>BattleUi.ApplyZonePickState</c> 在点亮区域态时一起下 ——
        /// 判据就是同一处的 <c>_coolingTargeted</c>（本拍 Options 里有没有「具体哪张冷却牌」）。
        /// 它不是规则判断，只是「这一拍的选项形状」的显示态（铁律 3）。</para>
        /// </summary>
        public void SetZoneWithCardTargets(bool on)
        {
            _zoneWithCardTargets = on;
        }

        /// <summary>
        /// 某一侧的<strong>4 个槽位底图</strong>改色 / 复位（M26）。
        ///
        /// <para><b>只点亮槽底图本身，不点视口</b>：高亮的是
        /// <c>SlotL_Col/SlotL_CD4…1</c>（敌方 <c>SlotR_*</c>）这 8 个节点各自的
        /// <see cref="Image"/>，也就是槽位那张带「N / 冷却区N」徽标的图 ——
        /// 方块形状与槽图完全重合，不会再盖住迷你卡，也不会留下 674 宽的空白色块。</para>
        ///
        /// <para><b>为什么能直接改 <c>Image.color</c> 而不新增节点</b>：槽底图本来就是
        /// 构建器预建的（<c>BattleArtLayerBuilder.BuildSlotColumn</c>），
        /// 平时是纯白 1.0（把槽图画成原色）；点亮 = 换成暖金，复位 = 换回纯白。
        /// 这样绕开 M21 的教训（运行时 new 的节点会被构建器推倒重建冲掉）。</para>
        ///
        /// <para>⚠ <b>颜色常量只从 <see cref="UiTheme"/> 取</b>，prefab 里存的是复位值纯白 ——
        /// 提示类颜色不写死在 prefab（用户红线，见 <see cref="UiTheme"/> 顶部说明）。</para>
        /// </summary>
        private void ApplyZoneHighlight(int seat)
        {
            RectTransform[] rows = RowsFor(seat);
            if (rows == null)
            {
                return;
            }

            bool on = _zonePickable && _zoneSeats.Contains(seat);
            Color c = on ? UiTheme.ZonePickableEdge : Color.white;

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null)
                {
                    continue;
                }

                Image img = rows[i].GetComponent<Image>();
                if (img != null)
                {
                    img.color = c;

                    // 只有「区域决策那一拍」才让槽图接射线 —— 平时它是 raycastTarget=false
                    //（构建器这么建的，理由是「不挡操作」）。点亮时打开，
                    // 才能接住「点在槽图上的空白处」；收掉时关回去，行为不变。
                    img.raycastTarget = on;
                }
            }
        }

        /// <summary>该侧的滚动视口（= 整片冷却区的可见范围）。</summary>
        private RectTransform ViewportFor(int seat)
        {
            RectTransform[] rows = RowsFor(seat);
            if (rows == null || rows[0] == null)
            {
                return seat == LocalSeat ? _playerRoot : _enemyRoot;
            }

            RectTransform column = rows[0].parent as RectTransform;
            return column == null ? null : column.parent as RectTransform;
        }

        /// <summary>
        /// 某一行的槽被点了一下 → 转成 <see cref="ZoneRowClicked"/>。
        ///
        /// <para>调用方是挂在每个槽位节点上的 <see cref="ZoneSlotClickCatcher"/>；
        /// 卡片自己带 <c>Button</c>，点在牌面上时冒泡链先被卡片吃掉，
        /// 所以另有一条 <see cref="OnCardClicked"/> 里的补路（见该方法注释）。</para>
        /// </summary>
        public void NotifyZoneRowClicked(int seat, int rowIndex)
        {
            // 没开「可点」时完全不响应 —— 平时点槽图不该有任何后果。
            if (!_zonePickable || !_zoneSeats.Contains(seat))
            {
                return;
            }

            Action<int, int> handler = ZoneRowClicked;
            if (handler != null)
            {
                handler(seat, rowIndex);
            }
        }

        /// <summary>某张迷你卡所在的槽（行）下标；找不到返回 -1。</summary>
        public int RowIndexFor(CardView card)
        {
            if (card == null)
            {
                return -1;
            }

            RectTransform rt = card.transform.parent as RectTransform;
            if (rt == null)
            {
                return -1;
            }

            // 迷你卡挂在行下的 Cards 容器里（见 BindGrouped / ResolveRowContainer），
            // 也可能直接挂在行上 —— 两种都往上找一级「名字是 Slot*_CD*」的祖先。
            Transform t = rt;
            while (t != null)
            {
                if (t.name.StartsWith("SlotL_CD") || t.name.StartsWith("SlotR_CD"))
                {
                    return RowIndexFromSlotName(t.name);
                }

                t = t.parent;
            }

            return -1;
        }

        /// <summary>从槽位节点名（<c>SlotL_CD3</c>）解析出行下标（0 = 冷却区4 … 3 = 冷却区1）。</summary>
        internal static int RowIndexFromSlotName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 9)
            {
                return -1;
            }

            // 名字末尾那位数字就是「冷却区N」，行下标 = 4 − N
            int n;
            if (!int.TryParse(name.Substring(name.Length - 1), out n))
            {
                return -1;
            }

            int row = 4 - n;
            return row < 0 ? -1 : row;
        }

        // ══════════════════════════════════════════════════════
        //  槽位点击（M26）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 给两侧的 <strong>8 个槽位底图</strong>各挂一个「点一下就报 (座位, 行)」的接收入口。
        ///
        /// <para>槽底图的 <c>raycastTarget</c> 在构建器里是 <c>false</c>（它不挡操作），
        /// 所以 <see cref="AttachZoneCatcher"/> 会把它打开 —— 只有区域决策那一拍才开，
        /// 平时关回去，免得槽图抢走迷你卡的点击（迷你卡是行的子节点，
        /// 子节点射线优先级更高，但把底图的 raycastTarget 打开仍然会吃掉
        /// 「点在槽上、牌外」的那部分空白，这正是我们想要的）。</para>
        /// </summary>
        private void EnsureZoneClickCatchers()
        {
            AttachZoneCatcher(LocalSeat);
            AttachZoneCatcher(OpponentSeat);
        }

        private void AttachZoneCatcher(int seat)
        {
            RectTransform[] rows = RowsFor(seat);
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null)
                {
                    continue;
                }

                ZoneSlotClickCatcher catcher = rows[i].GetComponent<ZoneSlotClickCatcher>();
                if (catcher == null)
                {
                    catcher = rows[i].gameObject.AddComponent<ZoneSlotClickCatcher>();
                }

                catcher.Seat = seat;
                catcher.RowIndex = i;
                catcher.Owner = this;
            }
        }

        public CardView FindCard(int seat, int uid)
        {
            var cards = seat == LocalSeat ? _playerShown : _enemyShown;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].HasCard && cards[i].Card.Uid == uid) return cards[i];
            return null;
        }

        /// <summary>绑一侧的冷却区。</summary>
        public void Bind(int seat, IReadOnlyList<CardSnapshot> cards)
        {
            RectTransform[] rows = RowsFor(seat);
            if (rows != null)
            {
                BindGrouped(seat, cards, rows);
                return;
            }

            RectTransform root = seat == LocalSeat ? _playerRoot : _enemyRoot;
            List<CardView> shown = seat == LocalSeat ? _playerShown : _enemyShown;
            List<CardView> pool = seat == LocalSeat ? _playerPool : _enemyPool;

            if (root == null || _cardPrefab == null)
            {
                return;
            }

            _reuse.Clear();

            if (cards != null)
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    CardView v = shown[i];
                    if (v == null || !v.HasCard)
                    {
                        continue;
                    }

                    for (int c = 0; c < cards.Count; c++)
                    {
                        if (cards[c].Uid == v.Card.Uid && !_reuse.ContainsKey(cards[c].Uid))
                        {
                            _reuse.Add(cards[c].Uid, v);

                            // 数字与力量预览都按新快照重下一次，不重建节点
                            v.RefreshCooldown(cards[c]);
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < shown.Count; i++)
            {
                CardView v = shown[i];
                if (v != null && (!v.HasCard || !_reuse.ContainsKey(v.Card.Uid)))
                {
                    v.Clear();
                    v.gameObject.SetActive(false);
                    pool.Add(v);
                }
            }

            shown.Clear();

            if (cards == null || cards.Count == 0)
            {
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                CardView v;
                if (!_reuse.TryGetValue(cards[i].Uid, out v))
                {
                    v = Take(pool, root);
                    v.Bind(cards[i], CardView.ViewMode.Mini, i);
                }

                v.transform.SetAsLastSibling();
                shown.Add(v);
            }
        }

        /// <summary>双方一起绑（最常见的一次调用）。</summary>
        public void BindBoth(IReadOnlyList<CardSnapshot> player, IReadOnlyList<CardSnapshot> enemy)
        {
            Bind(LocalSeat, player);
            Bind(OpponentSeat, enemy);
        }

        // ══════════════════════════════════════════════════════
        //  M11：按「剩余冷却」分到 4 行
        // ══════════════════════════════════════════════════════

        /// <summary>该侧是否配了 4 行挂载点；没配返回 null（走老的单一 Grid）。</summary>
        private RectTransform[] RowsFor(int seat)
        {
            RectTransform[] rows = seat == LocalSeat ? _playerRows : _enemyRows;
            if (rows == null || rows.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null)
                {
                    return rows;
                }
            }

            return null;
        }

        /// <summary>
        /// 按剩余冷却分行摆放。
        ///
        /// <para>行号 = <c>4 − 剩余冷却</c>（剩余 4 → 第 0 行「冷却区4」）。</para>
        ///
        /// <para><b>M12 摆位口径</b>：迷你卡挂在槽的<b>外侧</b> —— 玩家侧从槽的右边缘往右排
        /// （左对齐、轴心在左上），敌方侧从槽的左边缘往左排（右对齐、轴心在右上）。
        /// 水平的起点用<b>运行时的槽宽</b>（<c>rows[i].rect.width</c>）而不是常量：
        /// 右列「冷却区1」那张槽图比其它行宽 42 px（它自己带了个内嵌卡框），
        /// 写死常量会让这一行的牌压在槽上。</para>
        ///
        /// <para>尺寸与位置<b>必须在 Bind 之后设</b>：<see cref="CardView.Bind"/> 不碰这两个值，
        /// 老布局靠父级的 GridLayoutGroup 兜，新布局没有布局组，得自己来。</para>
        /// </summary>
        private void BindGrouped(int seat, IReadOnlyList<CardSnapshot> cards, RectTransform[] rows)
        {
            List<CardView> shown = seat == LocalSeat ? _playerShown : _enemyShown;
            List<CardView> pool = seat == LocalSeat ? _playerPool : _enemyPool;
            bool playerSide = seat == LocalSeat;
            int count = cards == null ? 0 : cards.Count;

            _reuse.Clear();

            // 1) 复用：Uid 还在的先把数字刷成新值（只改数字，不重建整卡）
            //    ⚠ 卡面上的冷却数是**基础冷却**（不随 tick 变），所以这一趟通常是幂等的 ——
            //      留着是因为 `Bind` 那条路也会走到这里，且牌可能换过实例。
            for (int i = 0; i < shown.Count; i++)
            {
                CardView v = shown[i];
                if (v == null || !v.HasCard)
                {
                    continue;
                }

                for (int c = 0; c < count; c++)
                {
                    if (cards[c].Uid == v.Card.Uid && !_reuse.ContainsKey(cards[c].Uid))
                    {
                        _reuse.Add(cards[c].Uid, v);
                        v.RefreshCooldown(cards[c]);
                        break;
                    }
                }
            }

            // 2) 回收：已离场的卡退回池子
            var keep = new List<CardView>();
            for (int i = 0; i < shown.Count; i++)
            {
                CardView v = shown[i];
                if (v == null)
                {
                    continue;
                }

                if (v.HasCard && _reuse.ContainsKey(v.Card.Uid))
                {
                    keep.Add(v);
                    continue;
                }

                v.Clear();
                v.gameObject.SetActive(false);
                pool.Add(v);
            }

            shown.Clear();

            // 3) 逐张入行
            var used = new int[rows.Length];
            var totals = new int[rows.Length];
            for (int i = 0; i < count; i++) totals[Mathf.Clamp(4 - cards[i].RemainingCooldown, 0, rows.Length - 1)]++;
            for (int i = 0; i < count; i++)
            {
                int remaining = cards[i].RemainingCooldown;
                int rowIndex = Mathf.Clamp(4 - remaining, 0, rows.Length - 1);
                RectTransform row = rows[rowIndex];
                RectTransform container = ResolveRowContainer(row);
                if (container == null)
                {
                    continue;
                }


                CardView v;
                if (!_reuse.TryGetValue(cards[i].Uid, out v))
                {
                    v = Take(pool, container);
                    v.Bind(cards[i], CardView.ViewMode.Mini, used[rowIndex]);
                }
                else
                {
                    v.transform.SetParent(container, false);
                }

                var rt = v.transform as RectTransform;
                if (rt != null)
                {
                    float w = UiLayout.SlotMiniCardWidth;
                    float h = UiLayout.SlotMiniCardHeight;
                    rt.sizeDelta = new Vector2(w, h);

                    // 锚点/轴心决定「从槽的哪一侧往外排」
                    float ax = playerSide ? 0f : 1f;
                    rt.anchorMin = new Vector2(ax, 1f);
                    rt.anchorMax = new Vector2(ax, 1f);
                    rt.pivot = new Vector2(ax, 1f);

                    // 槽宽要用行自己的 —— 右列第一行那张槽图宽 224，其余 182
                    float rowW = row == null ? w : row.rect.width;
                    float rowH = row == null ? h : row.rect.height;
                    float offset = rowW + UiLayout.SlotMiniCardGap
                                   + used[rowIndex] * Mathf.Min(UiLayout.SlotMiniCardStep,
                                       UiLayout.SlotMiniCardStep * (UiLayout.SlotMiniCardMax - 1) / Mathf.Max(1, totals[rowIndex] - 1));
                    float x = playerSide ? offset : -offset;

                    // 竖直：跟槽行居中对齐
                    rt.anchoredPosition = new Vector2(x, -(rowH - h) * 0.5f);
                }

                v.transform.SetAsLastSibling();
                // 同一行里越靠后的越往画面中心走 —— 压在上层的那张要是离中心近的，
                // 不然先摆的会把后摆的挡住一半
                CardInteractor.Attach(v.gameObject, this, v, false);
                shown.Add(v);
                used[rowIndex]++;
            }

            Apply(shown);
        }

        /// <summary>行挂载点下若有一个叫 <c>Cards</c> 的子容器就用它，否则直接用行本身。</summary>
        private RectTransform ResolveRowContainer(RectTransform row)
        {
            if (row == null)
            {
                return null;
            }

            Transform child = row.Find("Cards");
            return child == null ? row : child as RectTransform;
        }

        /// <summary>
        /// 哪些迷你卡当前可选（Uid 集合）。<c>CardInstance.Uid</c> 全对局唯一，
        /// 所以一份集合就能同时表达「双方冷却区都有目标」（加速 / 减速）。
        ///
        /// <para>集合为空 = 本拍没有任何冷却区目标 → 取消限制，两片都恢复常态可读。</para>
        /// </summary>
        public void SetPlayable(HashSet<int> playableUids)
        {
            _playable.Clear();

            if (playableUids != null)
            {
                foreach (int uid in playableUids)
                {
                    _playable.Add(uid);
                }
            }

            _restricted = _playable.Count > 0;

            Apply(_playerShown);
            Apply(_enemyShown);
        }

        public void ClearPlayable()
        {
            _playable.Clear();
            _restricted = false;

            // 整片可点的态一并收掉（区域决策结束 / 玩家点了别处都会走到这里）
            _zoneSeats.Clear();
            _zonePickable = false;
            _zoneWithCardTargets = false;
            ApplyZoneHighlight(LocalSeat);
            ApplyZoneHighlight(OpponentSeat);

            Apply(_playerShown);
            Apply(_enemyShown);

            // 决策结束（含「拖牌打出去」这类本视图没参与的操作）时，浮层不能留着 ——
            // 它的数据源是上一拍的快照，留着就是错的。
            HidePeek();
        }

        /// <summary>
        /// 把「可点 / 压暗」落到一张张迷你卡上。
        ///
        /// <para><b>⚠ M36（2026-09-23）改了后半段：压暗的牌仍然点得动。</b>
        /// 用户口径是 —— 对一张本拍「不可能被加速 / 减速」的牌操作时，要弹一段文字说明
        /// （见 <c>BattleUi</c> 的浮字）。要弹得出来，这次点击就必须走到 <c>BattleUi</c>：
        /// 旧实现把压暗与「吃掉点击」绑在一起，点下去<b>什么都不会发生</b>，
        /// 玩家读到的是「这游戏点不动」而不是「这张牌现在不行」。</para>
        ///
        /// <para>谁能点、点了算不算数<b>仍然只由引擎的 <c>Options</c> 决定</b>（铁律 3）：
        /// 这里只表达「暗」，<c>BattleUi</c> 拿到点击后回本拍选项里找，找不到才弹提示。</para>
        ///
        /// <para>⚠ 顺序要紧：<see cref="CardView.SetInteractable"/> 内部会把压暗一起<b>重置</b>
        /// （<c>SetDimmed(!interactable &amp;&amp; HasCard)</c>），所以必须<b>先</b>设可点、
        /// <b>后</b>设压暗 —— 反过来这句 <c>SetDimmed</c> 会被它抹掉。</para>
        /// </summary>
        private void Apply(List<CardView> shown)
        {
            bool dimming = _dimUnplayable && _restricted;

            for (int i = 0; i < shown.Count; i++)
            {
                CardView v = shown[i];
                if (v == null)
                {
                    continue;
                }

                bool can = !_restricted || _playable.Contains(v.Card.Uid);

                v.SetInteractable(true);
                v.SetDimmed(dimming && !can);
            }
        }

        private CardView Take(List<CardView> pool, RectTransform root)
        {
            CardView v = null;

            if (pool.Count > 0)
            {
                int last = pool.Count - 1;
                v = pool[last];
                pool.RemoveAt(last);
                v.gameObject.SetActive(true);
                v.transform.SetParent(root, false);
            }

            if (v == null)
            {
                v = Instantiate(_cardPrefab, root);
                v.name = "MiniCard";
                v.Clicked += OnCardClicked;
            }

            // M12：迷你卡也要长按看牌。**不给拖拽**（Draggable=false）——
            // 冷却区的语义是「点选一个目标」，拖过来拖过去没有对应规则。
            CardInteractor.Attach(v.gameObject, this, v, false);

            // 卡面统一（2026-09-25）：冷却迷你卡与手牌**共用同一份 CardView Prefab**
            // （`CardView_Hand.prefab`），尺寸靠 CardRoot 的缩放适配 —— 所以每次取牌
            // 都要下一次，池化复用的那几张也不例外。
            //
            // ⚠ 取牌路径已经在下面对卡根节点下 sizeDelta（见 BindGrouped 的 SlotMiniCard*），
            //   两者分工：sizeDelta = 占位尺寸，本方法 = 卡面内部按这个宽度重排缩放。
            v.SetFaceWidth(UiLayout.SlotMiniCardWidth);
            return v;
        }

        private void OnCardClicked(CardView card)
        {
            // ⚠ M26：区域加减速那一拍，**落在卡面上的点击也必须算「点了这一格」**。
            //
            //   为什么不能只靠 ZoneSlotClickCatcher：迷你卡是槽位的下层子节点，
            //   EventSystem 派发 pointerClick 时是从射线命中的那个节点**往上冒泡，
            //   遇到第一个能处理的节点就停**（`ExecuteEvents.ExecuteHierarchy`）。
            //   卡片自己是带 Button 的，所以点在牌面上时永远到不了挂在槽上的接收器 ——
            //   只有点在「牌与牌之间的空隙 / 槽图露出部分」才生效。冷却区一行放满 4 张迷你卡，
            //   空隙少得可怜，等于这个入口基本点不动。
            //
            //   这里把两条路并成一条：区域档那一拍，点牌面 = 点它所在的那一格，
            //   走同一个 ZoneRowClicked（行号由 RowIndexFor 从卡片的祖先名解析）。
            //
            //   ⚠⚠ 2026-09-25 收窄（用户口径：「区域加速的判定区域过大，甚至覆盖到了卡牌上，
            //   至少不能与冷却区域当中的卡牌重叠」）：**本拍若同时还有逐张目标
            //   （_zoneWithCardTargets），这条补路必须关掉。**
            //   加速 + 区域加速 是同一个决策里的两半（引擎合成 ChooseCooldownEffects，
            //   MaxSelect = 1 二选一），补路一开，点任何一张卡都被吞成「选了那一格」，
            //   玩家永远选不出「只加速这一张」—— 屏幕上就是「区域档把卡面盖住了」。
            //   这时区域档的判定区域只剩槽位徽标自己那块空白：卡摆在槽的外侧
            //   （BindGrouped 的 offset = rowW + gap），两者本来就不重叠。
            //   只有「本拍纯粹是区域档」（M26 原本那一拍）才保留补路 ——
            //   那时一行 4 张卡把槽图露出的空白挤得几乎为零，关了就等于点不动。
            if (_zonePickable && !_zoneWithCardTargets)
            {
                int seat = SeatOf(card);
                if (seat >= 0 && _zoneSeats.Contains(seat))
                {
                    int row = RowIndexFor(card);
                    if (row >= 0)
                    {
                        NotifyZoneRowClicked(seat, row);
                        return;
                    }
                }
            }

            if (CardClicked != null)
            {
                CardClicked(card);
            }
        }

        /// <summary>这张迷你卡画在哪一侧（不在任何一侧时返回 -1）。</summary>
        private int SeatOf(CardView card)
        {
            if (card == null)
            {
                return -1;
            }

            if (_playerShown.Contains(card))
            {
                return LocalSeat;
            }

            if (_enemyShown.Contains(card))
            {
                return OpponentSeat;
            }

            return -1;
        }

        // ══════════════════════════════════════════════════════
        //  手势（ICardGestureHost）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 冷却区只接<strong>长按</strong>：弹出完整卡面方便确认「这是哪张牌」。
        ///
        /// <para>悬停、按下、抬起都不做额外的事 —— 选目标仍然是「点一下」。
        /// 长按抬起时 <see cref="CardInteractor"/> 会把这次点击吃掉，
        /// 所以「按住看一眼」不会被当成「选中了这张牌」。</para>
        /// </summary>
        public void OnCardGesture(CardView card, CardGesture gesture, Vector2 screenPos)
        {
            if (card == null)
            {
                return;
            }

            if (gesture == CardGesture.LongPress)
            {
                if (_peekOnLongPress && _peek != null && card.HasCard)
                {
                    _peek.Show(card.Card);
                }

                return;
            }

            // 松手 / 挪开都收
            if (gesture == CardGesture.Release || gesture == CardGesture.HoverExit)
            {
                HidePeek();
            }
        }

        private void HidePeek()
        {
            if (_peek != null)
            {
                _peek.Hide();
            }
        }
    }
}
