using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>自定义战斗 · 牌组编成界面</b>（2026-10-28 · 大改新增）。
    ///
    /// <para><b>它长什么样</b>：左侧是整张卡表（可点），右侧是<b>当前正在编辑的那一方</b>的
    /// 8 个格子；顶部两枚页签在「你的牌组」与「对手的牌组」之间切换；底部「开战 / 返回」。</para>
    ///
    /// <para><b>它管什么</b>：只负责「摆」和「报」—— 把卡表画出来、把已选的卡点亮、
    /// 把 8 个格子铺满，然后报出 <see cref="CatalogCardClicked"/> / <see cref="TabClicked"/> /
    /// <see cref="StartClicked"/> / <see cref="BackClicked"/>。
    /// <b>哪张牌在谁的牌组里、能不能再加一张、能不能开战</b>全部由
    /// <see cref="DeckSelectEntry"/> 决定（与工程里其它界面同一分工）。</para>
    ///
    /// <para><b>节点由构建器建</b>（<c>DeckSelectUiBuilder</c>）：卡面模板、8 个格子、
    /// 两枚页签都是那趟构建出来的长期节点；运行时只按需克隆卡表的卡面并复用。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckSelectView : MonoBehaviour
    {
        [Header("标题 / 提示")]
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _hint;

        [Header("两枚页签")]
        [SerializeField] private Button _playerTab;
        [SerializeField] private TMP_Text _playerTabLabel;
        [SerializeField] private GameObject _playerTabGlow;
        [SerializeField] private Button _enemyTab;
        [SerializeField] private TMP_Text _enemyTabLabel;
        [SerializeField] private GameObject _enemyTabGlow;

        [Header("左侧卡表")]
        [SerializeField] private ScrollRect _catalogScroll;
        [SerializeField] private RectTransform _catalogContent;
        [SerializeField] private CardView _catalogTemplate;

        [Header("右侧 8 格")]
        [SerializeField] private CardView[] _slotViews = new CardView[0];

        [Header("底部")]
        [SerializeField] private Button _startButton;
        [SerializeField] private TMP_Text _startLabel;
        [SerializeField] private Button _backButton;

        /// <summary>点了某一方的页签（0 = 你 / 1 = 对手）。</summary>
        public event Action<int> TabClicked;

        /// <summary>点了卡表里的某一张（参数 = 卡表下标）。</summary>
        public event Action<int> CatalogCardClicked;

        public event Action StartClicked;
        public event Action BackClicked;

        /// <summary>当前卡表里铺了几张（探针用）。</summary>
        public int CatalogCardCount
        {
            get { return _catalogCards.Count; }
        }

        private readonly List<CardView> _catalogCards = new List<CardView>();
        private readonly Dictionary<CardView, Vector2> _slotHomes = new Dictionary<CardView, Vector2>();
        private int _tab = -1;
        private bool _tabChanged;

        private UnityAction _playerTabHandler;
        private UnityAction _enemyTabHandler;
        private UnityAction _startHandler;
        private UnityAction _backHandler;

        private void Awake()
        {
            Hook();
        }

        private void OnEnable()
        {
            Hook();
        }

        private void OnDisable()
        {
            Unhook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        /// <summary>
        /// 监听器一律运行时按序列化字段自己接（铁律 8）：构建器在编辑模式接的那些不是序列化数据，
        /// 存 Prefab / 场景时直接蒸发 —— 症状是「每一项检查都通过，但按钮点下去什么都没有」。
        /// </summary>
        private void Hook()
        {
            HookButton(_playerTab, ref _playerTabHandler, delegate { RaiseTab(0); });
            HookButton(_enemyTab, ref _enemyTabHandler, delegate { RaiseTab(1); });
            HookButton(_startButton, ref _startHandler, RaiseStart);
            HookButton(_backButton, ref _backHandler, RaiseBack);
        }

        private static void HookButton(Button button, ref UnityAction handler, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            if (handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = action;
            button.onClick.AddListener(handler);
        }

        private void Unhook()
        {
            UnhookButton(_playerTab, ref _playerTabHandler);
            UnhookButton(_enemyTab, ref _enemyTabHandler);
            UnhookButton(_startButton, ref _startHandler);
            UnhookButton(_backButton, ref _backHandler);
        }

        private static void UnhookButton(Button button, ref UnityAction handler)
        {
            if (button != null && handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = null;
        }

        private void RaiseTab(int index)
        {
            Action<int> handler = TabClicked;
            if (handler != null) handler(index);
        }

        private void RaiseStart()
        {
            Action handler = StartClicked;
            if (handler != null) handler();
        }

        private void RaiseBack()
        {
            Action handler = BackClicked;
            if (handler != null) handler();
        }

        private void OnCatalogCardClicked(CardView card)
        {
            Action<int> handler = CatalogCardClicked;
            if (handler != null && card != null) handler(card.SlotIndex);
        }

        // ══════════════════════════════════════════════════════
        //  铺界面
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把整张卡表铺到左侧网格里。
        /// </summary>
        /// <param name="cards">卡表里的全部卡（顺序固定 = 卡表顺序）。</param>
        /// <param name="pickedIds">当前页签已经选中的卡（按 ID 点亮）。</param>
        public void ShowCatalog(IReadOnlyList<CardDef> cards, ICollection<string> pickedIds)
        {
            int count = cards != null ? cards.Count : 0;
            EnsureCatalogCards(count);

            for (int i = 0; i < _catalogCards.Count; i++)
            {
                CardView view = _catalogCards[i];
                if (view == null)
                {
                    continue;
                }

                bool used = i < count;
                if (view.gameObject.activeSelf != used)
                {
                    view.gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                CardDef def = cards[i];
                view.Bind(CardSnapshot.FromDef(def, 0, def.Cooldown), CardView.ViewMode.Hand, i);
                view.SetFaceWidth(DeckCardFaceWidth);
                view.SetInteractable(true);
                view.SetSelected(pickedIds != null && pickedIds.Contains(def.Id));
                view.SetDimmed(false);
            }

            if (_catalogScroll != null)
            {
                _catalogScroll.StopMovement();
            }
        }

        /// <summary>把当前页签的 8 个格子铺出来（少于 8 张 = 后面的格子空着）。</summary>
        public void ShowDeck(IReadOnlyList<CardDef> cards)
        {
            if (_slotViews == null)
            {
                return;
            }

            var origins = new Dictionary<string, Vector3>();
            var remaining = new HashSet<string>();
            if (cards != null) foreach (CardDef card in cards) if (card != null) remaining.Add(card.Id);
            foreach (CardView old in _slotViews)
            {
                if (old == null) continue;
                if (!_slotHomes.ContainsKey(old)) _slotHomes.Add(old, ((RectTransform)old.transform).anchoredPosition);
                if (!_tabChanged && old.HasCard && UiPanelAnimator.IsShown(old.gameObject))
                {
                    origins[old.Card.CardId] = old.transform.position;
                    if (!remaining.Contains(old.Card.CardId) && Application.isPlaying)
                    {
                        CardView departing = Instantiate(old, old.transform.parent);
                        departing.name = "DepartingDeckCard";
                        departing.SetReadOnly();
                        departing.SetAlpha(1f);
                        UiPanelAnimator.Close(departing.gameObject, () =>
                        {
                            if (departing != null) Destroy(departing.gameObject);
                        });
                        var rect = (RectTransform)departing.transform;
                        UiPositionMotion.MoveTo(rect, rect.anchoredPosition + Vector2.down * 24f);
                    }
                }
            }

            for (int i = 0; i < _slotViews.Length; i++)
            {
                CardView view = _slotViews[i];
                if (view == null)
                {
                    continue;
                }

                CardDef def = cards != null && i < cards.Count ? cards[i] : null;
                if (def == null)
                {
                    // Surviving identities are now rendered in their new slots; do not fade duplicate faces.
                    UiPanelAnimator.SetVisible(view.gameObject, false, interactive: false, immediate: true);
                    continue;
                }

                bool sameCard = view.HasCard && view.Card.CardId == def.Id && !_tabChanged;
                Vector3 origin;
                bool hasOrigin = origins.TryGetValue(def.Id, out origin);
                if (!hasOrigin && !_tabChanged)
                {
                    CardView source = _catalogCards.Find(c => c != null && c.HasCard && c.Card.CardId == def.Id);
                    if (source != null) { origin = source.transform.position; hasOrigin = true; }
                }
                UiPanelAnimator.SetVisible(view.gameObject, true, interactive: false);
                view.Bind(CardSnapshot.FromDef(def, 0, def.Cooldown), CardView.ViewMode.Hand, i);
                view.SetFaceWidth(DeckSlotFaceWidth);
                view.SetReadOnly();
                view.SetSelected(false);
                view.SetDimmed(false);
                RectTransform rect = (RectTransform)view.transform;
                if (hasOrigin) UiPositionMotion.FromWorld(rect, origin, _slotHomes[view]);
                else UiPositionMotion.MoveTo(rect, _slotHomes[view], false);
                if (_tabChanged && !sameCard)
                {
                    UiPanelAnimator.RevealContent(view.gameObject);
                }
            }
            _tabChanged = false;
        }

        /// <summary>切换页签的高亮，并刷新两边的计数（0/8）。</summary>
        public void SetTab(int index, int playerCount, int enemyCount)
        {
            _tabChanged = _tab != index;
            _tab = index;
            if (_playerTabGlow != null && _playerTabGlow.activeSelf != (index == 0))
            {
                _playerTabGlow.SetActive(index == 0);
            }

            if (_enemyTabGlow != null && _enemyTabGlow.activeSelf != (index == 1))
            {
                _enemyTabGlow.SetActive(index == 1);
            }

            if (_playerTabLabel != null)
            {
                _playerTabLabel.text = "你的牌组　" + playerCount + " / " + BattleLaunch.DeckSize;
            }

            if (_enemyTabLabel != null)
            {
                _enemyTabLabel.text = "对手的牌组　" + enemyCount + " / " + BattleLaunch.DeckSize;
            }

            if (_title != null)
            {
                _title.text = index == 0 ? "自定义战斗 · 你的牌组" : "自定义战斗 · 对手的牌组";
            }
        }

        /// <summary>「开战」键的可用状态。</summary>
        public void SetStartInteractable(bool interactable)
        {
            if (_startButton != null)
            {
                _startButton.interactable = interactable;
            }

            if (_startLabel != null)
            {
                _startLabel.color = interactable ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            }
        }

        /// <summary>底部提示行。</summary>
        public void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text ?? string.Empty;
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        /// <summary>维持至少 <paramref name="count"/> 个卡表卡面（按需克隆 + 失活复用，同 CardPoolView）。</summary>
        private void EnsureCatalogCards(int count)
        {
            if (_catalogTemplate == null || _catalogContent == null)
            {
                return;
            }

            while (_catalogCards.Count < count)
            {
                CardView clone = Instantiate(_catalogTemplate, _catalogContent);
                clone.name = "CatalogCard_" + _catalogCards.Count;
                clone.gameObject.SetActive(false);
                clone.Clicked += OnCatalogCardClicked;
                _catalogCards.Add(clone);
            }
        }

        /// <summary>卡表卡面宽度（构建器的网格 cell 按它留位）。</summary>
        public const float DeckCardFaceWidth = 168f;

        /// <summary>右侧 8 格的卡面宽度。</summary>
        public const float DeckSlotFaceWidth = 168f;
    }
}
