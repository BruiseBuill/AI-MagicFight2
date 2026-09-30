using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 商店场景的<strong>运行时视图</strong>（2026-09-26 · P4 的第一个事件切片）。
    ///
    /// <para><b>职责边界</b>（与战斗侧同口径）：本类只做三件事 ——
    /// ① 读一份「货架快照」把它画出来；② 把玩家的点击冒泡成事件；③ 暴露资源栏（生命 / 金币）。
    /// <b>它不认识 <c>ShopService</c>、不认识 <c>AdventureEngine</c></b>：
    /// 买 / 离开这些决定由上层（<c>ShopSceneEntry</c> → 流程桥）翻译成 Core 的命令。
    /// 这样「同一个商店在不同驱动下是逐字同一份 UI」（架构文档 E4 的 UI 层表述）。</para>
    ///
    /// <para><b>卡面复用唯一那份 <c>CardView_Hand.prefab</c></b>（铁律 9）：
    /// 货位上的卡就是一张 <see cref="CardView"/>，尺寸由 <c>SetFaceWidth</c> 给 ——
    /// 与手牌 / 冷却迷你卡 / 放大查看是同一棵节点树、同一套版式。</para>
    ///
    /// <para><b>⚠ 为什么每个货位是「一个 <see cref="ShopSlotView"/> + 一个 CardView 子节点」</b>：
    /// 货位除了卡面之外还要有价格牌、卖光了空位、买不起压暗 —— 这些不属于卡面。
    /// 把它们塞进 CardView 会让「卡面」这一件事多出商店专属的分支，
    /// 而 CardView 是四个界面共用的。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopView : MonoBehaviour
    {
        /// <summary>一个货位要显示的东西（纯数据，UI 不认识它从哪来）。</summary>
        public struct ShelfEntry
        {
            /// <summary>货位序号 0..3（3 号位是特价位）。</summary>
            public int Index;

            /// <summary>这张牌的卡表定义；<b>null = 这个位置卖光了</b>。</summary>
            public CardDef Card;

            /// <summary>价格（金币）。卖光了时无意义。</summary>
            public int Price;

            /// <summary>是不是特价位（价格用暖金显示）。</summary>
            public bool Discount;

            /// <summary>玩家现在买不买得起（余额 ≥ 价格）。</summary>
            public bool Affordable;
        }

        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private TMP_Text _leaveLabel;

        /// <summary>右下角的背包键（点它看主角当前卡池）。</summary>
        [SerializeField] private Button _bagButton;

        /// <summary>
        /// 背包面板。<b>它自己负责开关</b>（遮罩 / 关闭键都接到它的 Hide），
        /// 本类只做两件事：把点击转过去 + 在数据变了的时候重新喂一份卡池。
        /// </summary>
        [SerializeField] private ShopBagView _bag;

        // ── 顶栏（复用战斗那套 Hud_Bar 的节点，但只接商店要用的几个字段）──
        [SerializeField] private TMP_Text _hudName;
        [SerializeField] private TMP_Text _hudHp;
        [SerializeField] private TMP_Text _hudGold;

        /// <summary>货位模板（失活），运行时按 <see cref="UiLayout.ShopSlotCount"/> 复制。</summary>
        [SerializeField] private ShopSlotView _slotTemplate;

        /// <summary>货位容器：模板的父节点，复制出来的货位都挂这里。</summary>
        [SerializeField] private RectTransform _slotRoot;

        private readonly List<ShopSlotView> _slots = new List<ShopSlotView>();

        /// <summary>主角当前卡池（<see cref="BindBag"/> 给的那一份，原样存着）。</summary>
        private readonly List<CardDef> _bagCards = new List<CardDef>();

        /// <summary>玩家点了某个货位（参数 = 货位序号）。上层拿它去提交购买命令。</summary>
        public event Action<int> SlotClicked;

        /// <summary>玩家点了「离开」。上层拿它去提交 <c>LeaveShop</c>。</summary>
        public event Action LeaveClicked;

        /// <summary>玩家点了背包键。上层拿它记账（内容已由 <see cref="BindBag"/> 喂过，本类自己会打开）。</summary>
        public event Action BagClicked;

        /// <summary>背包面板是不是开着。</summary>
        public bool IsBagOpen
        {
            get { return _bag != null && _bag.IsOpen; }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按序列化字段自己接（铁律 8）：
            //   构建器在编辑模式里 AddListener 接的那一份不是序列化数据，存 Prefab / 场景时蒸发。
            //   症状是「一切看着正常、onClick 上 0 个监听器、点了毫无反应且零报错」。
            if (_leaveButton != null)
            {
                _leaveButton.onClick.RemoveListener(OnLeave);
                _leaveButton.onClick.AddListener(OnLeave);
            }

            if (_bagButton != null)
            {
                _bagButton.onClick.RemoveListener(OnBag);
                _bagButton.onClick.AddListener(OnBag);
            }
        }

        /// <summary>
        /// 把货架画出来。
        ///
        /// <para>幂等：调用几次都行 —— 会先把上一次复制出来的货位全删掉再重建。
        /// 这样切入口参数（换种子 / 换持有卡）时不需要重载场景。</para>
        /// </summary>
        public void BindShelf(IList<ShelfEntry> entries)
        {
            EnsureSlots(entries != null ? entries.Count : 0);

            int count = entries != null ? entries.Count : 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].SetVisible(i < count);
                if (i < count)
                {
                    _slots[i].Bind(entries[i]);
                }
            }
        }

        /// <summary>绑定左侧资源栏（生命 / 金币）与角色名。</summary>
        public void BindResources(string name, int hp, int maxHp, int gold)
        {
            if (_hudName != null)
            {
                _hudName.text = string.IsNullOrEmpty(name) ? "旅者" : name;
            }

            if (_hudHp != null)
            {
                _hudHp.text = hp + "/" + Mathf.Max(0, maxHp);
                _hudHp.color = hp <= 1 ? UiTheme.HpFull : UiTheme.TextPrimary;
            }

            if (_hudGold != null)
            {
                _hudGold.text = gold.ToString();
                _hudGold.color = gold > 0 ? UiTheme.AuraReady : UiTheme.TextSecondary;
            }
        }

        /// <summary>设置标题（默认「商店」，留出以后按节点类型换名的余地）。</summary>
        public void SetTitle(string text)
        {
            if (_title != null)
            {
                _title.text = text;
            }
        }

        /// <summary>「离开」按钮的文字（默认「离开」）。</summary>
        public void SetLeaveLabel(string text)
        {
            if (_leaveLabel != null)
            {
                _leaveLabel.text = text;
            }
        }

        /// <summary>
        /// 喂一份「主角当前卡池」给背包。<b>只存不画</b> —— 玩家点背包键时才展开。
        ///
        /// <para>为什么不在每次数据变化时立刻重画：买一张牌就要刷一次 40 个卡面实例，
        /// 而面板多半是关着的，这份重排没人看得见。开着的时候才需要跟着变，
        /// 所以这里只在「开着」时补一次刷新。</para>
        ///
        /// <para>口径（谁是「主角当前卡池」）由上层决定，本类不判断 ——
        /// 它可能是「这次冒险 run 里已获得的卡」，也可能是「本局启用的卡表子集」。</para>
        /// </summary>
        public void BindBag(IList<CardDef> cards)
        {
            _bagCards.Clear();
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i] != null)
                    {
                        _bagCards.Add(cards[i]);
                    }
                }
            }

            if (_bag != null && _bag.IsOpen)
            {
                _bag.Show(_bagCards, BagTitle);
            }
        }

        /// <summary>关掉背包（幂等）。正式流程里「离开商店」之前调一下，免得把面板留在屏幕上。</summary>
        public void CloseBag()
        {
            if (_bag != null)
            {
                _bag.Hide();
            }
        }

        /// <summary>背包面板的标题（唯一一处字面量，改口径改这里）。</summary>
        private const string BagTitle = "背包 · 当前卡池";

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 保证有 <paramref name="count"/> 个货位实例（不够就按模板复制，多了就失活备用）。
        ///
        /// <para><b>为什么不预建满 4 个</b>：货位数来自 <c>AdventureConfig.ShopSlotCount</c>
        /// （红线 8：不写死进引擎），场景里的模板只有一份 —— 数量由数据决定，
        /// 复制在运行时做，这样「配置改成 5 个货位」不需要回来改场景。</para>
        /// </summary>
        private void EnsureSlots(int count)
        {
            if (_slotTemplate == null || _slotRoot == null)
            {
                return;
            }

            while (_slots.Count < count)
            {
                ShopSlotView clone = Instantiate(_slotTemplate, _slotRoot);
                clone.name = "ShopSlot_" + _slots.Count;
                RectTransform rt = (RectTransform)clone.transform;

                // 货位容器 `_slotRoot` 是一个**零尺寸的点**，摆在整排货架的中心
                // （见 ShopUiBuilder）。所以每个货位的 anchoredPosition 就是
                // 「相对货架中心的偏移」—— 不用再加半个总宽，也不必重新算锚点。
                int index = _slots.Count;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(
                    UiLayout.ShopSlotCenterX(index) - UiLayout.ShopShelfCenterX, 0f);
                rt.sizeDelta = new Vector2(UiLayout.ShopCardWidth, UiLayout.ShopCardHeight);

                clone.Clicked += OnSlotClicked;
                _slots.Add(clone);
            }
        }

        private void OnSlotClicked(ShopSlotView slot)
        {
            int index = _slots.IndexOf(slot);
            if (index >= 0)
            {
                SlotClicked?.Invoke(index);
            }
        }

        private void OnLeave()
        {
            LeaveClicked?.Invoke();
        }

        /// <summary>
        /// 点背包键。本类直接把面板打开（内容已经在 <see cref="BindBag"/> 里存好了），
        /// 再向上冒一个事件供记账 —— 打开一个面板不需要上层先回一趟命令。
        /// </summary>
        private void OnBag()
        {
            if (_bag != null)
            {
                _bag.Show(_bagCards, BagTitle);
            }

            BagClicked?.Invoke();
        }
    }
}
