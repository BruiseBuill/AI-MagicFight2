using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 商店里的<b>背包面板</b>（2026-09-26）：点右下角背包键弹出，看主角<b>当前卡池</b>。
    ///
    /// <para><b>它是什么</b>：一层铺满画布的遮罩 + 一块面板 + 一个可滚动的卡面网格。
    /// 网格里的卡是<b>唯一那份 <c>CardView_Hand.prefab</c></b> 的实例（红线 9），
    /// 与货位 / 手牌 / 冷却迷你卡同源；数量由数据决定，模板只有一份 —— 运行时按需克隆，
    /// 所以「卡池从 8 张变成 40 张」不需要回来改场景。</para>
    ///
    /// <para><b>职责边界</b>（与 <see cref="ShopView"/> 同口径）：本类<b>只画</b>。
    /// 卡池里有哪些牌是上层给的（<see cref="Show"/> 的参数），
    /// 这里不查卡表、不碰存档、不认识 <c>CardPool</c> / 冒险流程 ——
    /// 「背包里的内容从哪来」是上层（正式接入后是冒险 run 的持有卡）的决定。</para>
    ///
    /// <para><b>⚠ 它自己负责关掉自己</b>：遮罩与「关闭」按钮都接到 <see cref="Hide"/>，
    /// 因为「怎么关」是面板的观感（点遮罩 / 点关闭键是一回事），
    /// 上层不需要为此接一条命令。关掉之后会发一次 <see cref="Closed"/> 供上层记账。</para>
    ///
    /// <para><b>⚠ 遮罩的作用不只是好看</b>：它吃射线且盖满画布，
    /// 于是货位、离开键、背包键在背包开着的时候<b>都点不到</b> ——
    /// 少了这一层会出现「隔着面板买到牌」这种零报错的怪事。</para>
    ///
    /// <para><b>⚠ 本类所在节点在构建器里就是失活的，且 <c>Awake</c> 里不许 Hide</b>：
    /// 首次 <c>SetActive(true)</c> 才触发 <c>Awake</c>，同一帧里的 Hide 会把刚显示的东西按灭，
    /// 而 <see cref="Show"/> 剩下的代码照跑（版式都设好了，屏幕上什么都没有）——
    /// 症状是「本局第一次打开背包不显示、第二次起正常」。这个坑在本工程已中招两次。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopBagView : MonoBehaviour
    {
        /// <summary>点它关闭（铺满画布的半透明遮罩，同时挡住底下的商店）。</summary>
        [SerializeField] private Button _veilButton;

        /// <summary>「关闭」按钮。</summary>
        [SerializeField] private Button _closeButton;

        [SerializeField] private TMP_Text _title;

        /// <summary>「N 张」那一行计数。</summary>
        [SerializeField] private TMP_Text _count;

        /// <summary>一张牌都没有时的提示（默认隐藏）。</summary>
        [SerializeField] private GameObject _empty;

        [SerializeField] private ScrollRect _scroll;

        /// <summary>网格内容节点（<c>GridLayoutGroup</c> + <c>ContentSizeFitter</c> 挂在它上面）。</summary>
        [SerializeField] private RectTransform _content;

        /// <summary>卡面模板（失活，藏在 <see cref="_content"/> 里）。</summary>
        [SerializeField] private CardView _cardTemplate;

        /// <summary>已经克隆出来的卡面（多出来的失活备用，不销毁 —— 反复开关背包不该一直产生垃圾）。</summary>
        private readonly List<CardView> _cards = new List<CardView>();

        private bool _open;

        /// <summary>面板是不是开着。</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>当前显示出来的卡面张数（验收探针用：探针读它比读 <c>_cards.Count</c> 可信）。</summary>
        public int VisibleCardCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    if (_cards[i] != null && _cards[i].gameObject.activeSelf)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>面板被关掉了（参数 = 关掉时的张数）。上层拿它记账 / 播提示，不做内容判断。</summary>
        public event Action<int> Closed;

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按序列化字段自己接（铁律 8）：
            //   构建器在编辑模式里 AddListener 接的那一份不是序列化数据，存 Prefab / 场景时蒸发，
            //   症状是「一切正常、onClick 上 0 个监听器、点了毫无反应且零报错」。
            Wire(_veilButton);
            Wire(_closeButton);
        }

        /// <summary>
        /// 打开背包并把卡池画出来。
        ///
        /// <para>幂等：反复调用都行 —— 会先把上一次用的卡面复用/失活，再按本次张数激活。
        /// 所以「买了一张牌 → 背包里立刻多一张」不需要重建面板。</para>
        /// </summary>
        /// <param name="cards">主角当前的卡池（null / 空表 = 显示「背包里还没有牌」）。</param>
        /// <param name="title">面板标题（留出以后按节点类型换名的余地）。</param>
        public void Show(IList<CardDef> cards, string title)
        {
            int count = cards != null ? cards.Count : 0;

            EnsureCards(count);

            for (int i = 0; i < _cards.Count; i++)
            {
                bool used = i < count;
                if (_cards[i].gameObject.activeSelf != used)
                {
                    _cards[i].gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                CardDef def = cards[i];
                if (def == null)
                {
                    _cards[i].gameObject.SetActive(false);
                    continue;
                }

                // 卡面唯一来源 = 卡表定义；冷却值取基础冷却（与手牌 / 货位同一口径）。
                CardSnapshot snap = CardSnapshot.FromDef(def, 0, def.Cooldown);
                _cards[i].Bind(snap, CardView.ViewMode.Hand, i);
                _cards[i].SetInteractable(false);       // 背包里的卡不参与出牌，也不接点击
                _cards[i].SetSelected(false);
                _cards[i].SetDimmed(false);
            }

            if (_title != null)
            {
                _title.text = string.IsNullOrEmpty(title) ? "背包" : title;
            }

            if (_count != null)
            {
                _count.text = count + " 张";
            }

            if (_empty != null && _empty.activeSelf != (count == 0))
            {
                _empty.SetActive(count == 0);
            }

            if (_scroll != null)
            {
                _scroll.StopMovement();
                _scroll.verticalNormalizedPosition = 1f;
            }

            _open = true;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);             // 这一下会触发 Awake（首次）
            }

            // 盖在最上层：构建器把它排在最后，但运行时货位是复制出来的，
            // 谁在最后不能只靠构建顺序保证 —— 这里自己钉一次。
            transform.SetAsLastSibling();
        }

        /// <summary>关掉背包。重复调用是幂等的（只有真正关掉那一次会发事件）。</summary>
        public void Hide()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            int count = VisibleCardCount;
            gameObject.SetActive(false);
            Closed?.Invoke(count);
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 保证手里有 <paramref name="count"/> 个卡面实例。
        ///
        /// <para><b>为什么是「克隆到够」而不是「预建满 40 个」</b>：卡池张数来自数据
        /// （40 张全开 / 8 张起始牌池 / 存档里的任意子集），预建满会让「只带 8 张牌」
        /// 的玩家白扛 32 个卡面实例；建满 40 又会在卡表扩到 60 张时不够用。
        /// 按需克隆 + 失活复用，两种极端都对。</para>
        /// </summary>
        private void EnsureCards(int count)
        {
            if (_cardTemplate == null || _content == null)
            {
                return;
            }

            while (_cards.Count < count)
            {
                CardView clone = Instantiate(_cardTemplate, _content);
                clone.name = "BagCard_" + _cards.Count;
                clone.gameObject.SetActive(false);
                _cards.Add(clone);
            }
        }

        /// <summary>把遮罩 / 关闭键接到 <see cref="Hide"/>（幂等，删了再加，避免重复接）。</summary>
        private void Wire(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(OnCloseRequested);
            button.onClick.AddListener(OnCloseRequested);
        }

        private void OnCloseRequested()
        {
            Hide();
        }
    }
}
