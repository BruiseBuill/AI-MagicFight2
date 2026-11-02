using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>历史面板里的一行</b>要画什么（2026-11-02）。
    ///
    /// <para>把「归属 + 8 张卡 + 它在存档里的下标」打成一只小包递给视图 ——
    /// 视图不需要认识 <see cref="DeckHistoryEntry"/> 与卡表，只在
    /// <see cref="DeckHistoryRowView.Bind"/> 里照着画。<b>下标必须一起带</b>：
    /// 视图会跳过「有卡已经不在当前卡表里」的条目，行号与存档下标就对不上了。</para>
    /// </summary>
    public sealed class DeckHistoryRowData
    {
        public readonly DeckOwner Owner;
        public readonly CardDef[] Cards;
        public readonly int StoreIndex;

        public DeckHistoryRowData(DeckOwner owner, CardDef[] cards, int storeIndex)
        {
            Owner = owner;
            Cards = cards;
            StoreIndex = storeIndex;
        }
    }

    /// <summary>
    /// <b>历史牌组面板里的一行</b>（2026-11-02）—— 归属标签 + 8 枚小牌 + 载入 / 删除。
    ///
    /// <para><b>为什么一行是 734 宽、两层</b>：8 枚小牌横排就要 723（86×8 + 5×7），
    /// 再挤一个归属标签和载入键就放不下了。所以第一层只放「谁用的 + 载入」，
    /// 第二层整条铺 8 枚 —— 两条并排正好是用户要的「一行 16 张（2 列）」。</para>
    ///
    /// <para><b>纯视图</b>：只报 <see cref="LoadClicked"/> / <see cref="DeleteClicked"/>，
    /// 载入到哪一方、删完怎么刷新，全部由 <see cref="DeckSelectEntry"/> 决定。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckHistoryRowView : MonoBehaviour
    {
        /// <summary>最左侧那条归属色条（蓝 = 玩家 / 橙 = AI）。</summary>
        [SerializeField] private Image _stripe;

        /// <summary>归属标签的底（同色 16%）。</summary>
        [SerializeField] private Image _ownerPill;

        /// <summary>归属标签的字（玩家 / AI）。</summary>
        [SerializeField] private TMP_Text _ownerLabel;

        [SerializeField] private Button _loadButton;
        [SerializeField] private Button _deleteButton;

        /// <summary>8 枚小牌（构建器按 DeckSize 建好并接线）。</summary>
        [SerializeField] private DeckChipView[] _chips = new DeckChipView[0];

        public event Action LoadClicked;
        public event Action DeleteClicked;

        /// <summary>这一行对应存档里的第几条（点删除时原样报回去）。</summary>
        public int StoreIndex { get; private set; }

        private UnityAction _loadHandler;
        private UnityAction _deleteHandler;

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

        /// <summary>监听器运行时自己接（铁律 8：构建器在编辑模式接的存不下来）。</summary>
        private void Hook()
        {
            if (_loadButton != null)
            {
                if (_loadHandler != null)
                {
                    _loadButton.onClick.RemoveListener(_loadHandler);
                }

                _loadHandler = RaiseLoad;
                _loadButton.onClick.AddListener(_loadHandler);
            }

            if (_deleteButton != null)
            {
                if (_deleteHandler != null)
                {
                    _deleteButton.onClick.RemoveListener(_deleteHandler);
                }

                _deleteHandler = RaiseDelete;
                _deleteButton.onClick.AddListener(_deleteHandler);
            }
        }

        private void Unhook()
        {
            if (_loadButton != null && _loadHandler != null)
            {
                _loadButton.onClick.RemoveListener(_loadHandler);
            }

            if (_deleteButton != null && _deleteHandler != null)
            {
                _deleteButton.onClick.RemoveListener(_deleteHandler);
            }

            _loadHandler = null;
            _deleteHandler = null;
        }

        private void RaiseLoad()
        {
            Action handler = LoadClicked;
            if (handler != null) handler();
        }

        private void RaiseDelete()
        {
            Action handler = DeleteClicked;
            if (handler != null) handler();
        }

        public void Bind(DeckHistoryRowData data)
        {
            if (data == null)
            {
                return;
            }

            StoreIndex = data.StoreIndex;

            bool player = data.Owner == DeckOwner.Player;
            Color accent = player ? UiTheme.PlayerAccent : UiTheme.EnemyAccent;

            if (_stripe != null)
            {
                _stripe.color = accent;
            }

            if (_ownerPill != null)
            {
                _ownerPill.color = new Color(accent.r, accent.g, accent.b, 0.16f);
            }

            if (_ownerLabel != null)
            {
                _ownerLabel.text = player ? "玩家" : "AI";
                _ownerLabel.color = accent;
            }

            if (_chips == null)
            {
                return;
            }

            for (int i = 0; i < _chips.Length; i++)
            {
                DeckChipView chip = _chips[i];
                if (chip == null)
                {
                    continue;
                }

                CardDef card = data.Cards != null && i < data.Cards.Length ? data.Cards[i] : null;
                chip.Bind(card);
            }
        }
    }
}
