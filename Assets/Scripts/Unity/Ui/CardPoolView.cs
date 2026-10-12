using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「<b>查看当前卡池</b>」的通用只读面板（M44 · 2026-10-05）。
    ///
    /// <para><b>它与 <see cref="ShopBagView"/> 是什么关系</b>：同一个东西的两个调用场景 ——
    /// 商店的背包面板显示的是「主角当前卡池」，这个面板显示的也是。两边的版式（面板底图、
    /// 6 列滚动网格、唯一那份 <c>CardView_Hand.prefab</c>）完全一致，常量也共用
    /// <c>UiLayout.ShopBag*</c>。之所以没有合成一个类，是因为商店那份有一个本类<b>不该有</b>的
    /// 行为：它买完牌要就地刷新（<c>ShopSceneEntry</c> 记账），而本类是纯查看。
    /// <b>差异只有一条</b>：本类多一个 <c>dimmedBaseIds</c> —— 战斗里用来把「本局已经抽到过」
    /// 的牌压暗。</para>
    ///
    /// <para><b>职责边界</b>：本类<b>只画</b>。「卡池里有哪些牌 / 哪几张该压暗」全部由上层给
    /// （<see cref="Show"/> 的参数），这里不查存档、不认识 <c>CardPool</c> / 冒险流程 ——
    /// 那是 <see cref="GlobalOverlayView"/> 的决定。</para>
    ///
    /// <para><b>⚠ 它自己负责关掉自己</b>：遮罩与「关闭」都接到 <see cref="Hide"/>，
    /// 上层不需要为此接一条命令。关掉之后发一次 <see cref="Closed"/> 供上层记账。</para>
    ///
    /// <para><b>⚠ 遮罩的作用不只是好看</b>：它吃射线且盖满画布，于是底下的出牌区 / 货位 /
    /// 节点在面板开着时<b>都点不到</b> —— 少了这一层会出现「隔着面板点到下面」这种零报错的怪事。</para>
    ///
    /// <para><b>⚠ 本类所在节点在构建器里就是失活的，且 <c>Awake</c> 里不许 Hide</b>：
    /// 首次 <c>SetActive(true)</c> 才触发 <c>Awake</c>，同一帧里的 Hide 会把刚显示的东西按灭，
    /// 而 <see cref="Show"/> 剩下的代码照跑（版式都设好了，屏幕上什么都没有）——
    /// 症状是「本局第一次打开卡池不显示、第二次起正常」。这个坑在本工程已中招两次。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardPoolView : MonoBehaviour
    {
        /// <summary>点它关闭（铺满画布的半透明遮罩，同时挡住底下的界面）。</summary>
        [SerializeField] private Button _veilButton;

        /// <summary>「关闭」按钮。</summary>
        [SerializeField] private Button _closeButton;

        [SerializeField] private TMP_Text _title;

        /// <summary>「N 张」那一行计数。</summary>
        [SerializeField] private TMP_Text _count;

        /// <summary>说明行（战斗里写「灰色的牌本局已经抽到过」；空则整行不显示）。</summary>
        [SerializeField] private TMP_Text _hint;

        /// <summary>一张牌都没有时的提示（默认隐藏）。</summary>
        [SerializeField] private GameObject _empty;

        [SerializeField] private ScrollRect _scroll;

        /// <summary>网格内容节点（<c>GridLayoutGroup</c> + <c>ContentSizeFitter</c> 挂在它上面）。</summary>
        [SerializeField] private RectTransform _content;

        /// <summary>卡面模板（失活，藏在 <see cref="_content"/> 里）。</summary>
        [SerializeField] private CardView _cardTemplate;

        /// <summary>已经克隆出来的卡面（多出来的失活备用，不销毁 —— 反复开关不该一直产生垃圾）。</summary>
        private readonly List<CardView> _cards = new List<CardView>();

        private bool _open;

        /// <summary>面板是不是开着。</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>当前显示出来的卡面张数（验收探针读它比读 <c>_cards.Count</c> 可信）。</summary>
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

        /// <summary>当前被压暗的张数（探针用：验「本局已抽到的牌真的暗了」）。</summary>
        public int DimmedCardCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    if (_cards[i] != null && _cards[i].gameObject.activeSelf && _cards[i].IsDimmed)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>面板被关掉了（参数 = 关掉时显示的张数）。上层拿它记账 / 播提示，不做内容判断。</summary>
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
        /// 打开面板并把卡池画出来。
        ///
        /// <para>幂等：反复调用都行 —— 先把上一次用的卡面复用 / 失活，再按本次张数激活。</para>
        /// </summary>
        /// <param name="cards">要显示的卡（null / 空表 = 显示「一张牌都没有」）。</param>
        /// <param name="dimmedBaseIds">
        /// 需要<b>压暗</b>的卡（按<b>基础 ID</b> 比较）。传 null / 空 = 一张都不压暗。
        /// 战斗里传的是「本局已经抽到过手牌的那些牌」。
        /// </param>
        /// <param name="title">面板标题。</param>
        /// <param name="hint">标题下那行说明；空则不显示这一行。</param>
        public void Show(IReadOnlyList<CardDef> cards, ICollection<string> dimmedBaseIds, string title, string hint)
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

                // 卡面唯一来源 = 卡表定义；冷却取基础冷却（与手牌 / 货位 / 背包同一口径）。
                CardSnapshot snap = CardSnapshot.FromDef(def, 0, def.Cooldown);
                _cards[i].Bind(snap, CardView.ViewMode.Hand, i);

                // ⚠ 尺寸一律运行时重申（铁律 15）：编辑模式写进 Prefab 的那份 localScale 不落盘。
                _cards[i].SetFaceWidth(UiLayout.ShopBagCardFaceWidth);

                _cards[i].SetReadOnly();                // 卡池里的卡不参与出牌，也不接点击（且不压暗）
                _cards[i].SetSelected(false);
                _cards[i].SetDimmed(IsDimmed(def, dimmedBaseIds));
            }

            if (_title != null)
            {
                _title.text = string.IsNullOrEmpty(title) ? "卡池" : title;
            }

            if (_count != null)
            {
                _count.text = count + " 张";
            }

            if (_hint != null)
            {
                _hint.text = string.IsNullOrEmpty(hint) ? string.Empty : hint;
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

            // 盖在最上层：构建器把它排在最后，但运行时别的层也会 SetAsLastSibling
            // （战斗的浮层 / 事件场景的面板），谁在最后不能只靠构建顺序保证 —— 这里自己钉一次。
            transform.SetAsLastSibling();
        }

        /// <summary>关掉面板。重复调用是幂等的（只有真正关掉那一次会发事件）。</summary>
        public void Hide()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            int count = VisibleCardCount;
            gameObject.SetActive(false);
            if (Closed != null)
            {
                Closed(count);
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 这张牌要不要压暗。
        ///
        /// <para><b>⚠ 两边都要归一成基础 ID</b>：卡池里显示的可能是强化版（<c>a+</c>，
        /// 由 <c>CardUpgrade.PreferUpgraded</c> 解析出来），而「本局抽到过」那边记的也是
        /// 实例的当前 ID —— 不归一的话强化过的牌永远暗不下来，且零报错。</para>
        /// </summary>
        private static bool IsDimmed(CardDef def, ICollection<string> dimmedBaseIds)
        {
            if (def == null || dimmedBaseIds == null || dimmedBaseIds.Count == 0)
            {
                return false;
            }

            return dimmedBaseIds.Contains(CardUpgrade.BaseIdOf(def.Id));
        }

        /// <summary>
        /// 保证手里有 <paramref name="count"/> 个卡面实例。
        ///
        /// <para><b>为什么是「克隆到够」而不是「预建满」</b>：卡池张数来自数据
        /// （45 张全开 / 存档里的任意子集），预建满会让「只带 8 张牌」的玩家白扛 37 个实例；
        /// 建死一个数又会在卡表扩张时不够用。按需克隆 + 失活复用，两种极端都对。</para>
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
                clone.name = "PoolCard_" + _cards.Count;
                clone.gameObject.SetActive(false);
                _cards.Add(clone);
            }
        }

        /// <summary>把遮罩 / 关闭键接到 <see cref="Hide"/>（幂等，先删再加，避免重复接）。</summary>
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
