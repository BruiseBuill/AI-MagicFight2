using System;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    public sealed class CardPoolEntryView : MonoBehaviour
    {
        [SerializeField] private CardView _card;
        [SerializeField] private Toggle _toggle;
        [SerializeField] private GameObject _check;
        public string CardId { get; private set; }
        public event Action<string, bool> Changed;

        public void Configure(CardView card, Toggle toggle, GameObject check) { _card = card; _toggle = toggle; _check = check; }
        private void Awake() { _toggle.onValueChanged.AddListener(OnChanged); }
        private void OnDestroy() { if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnChanged); }
        public void Bind(CardDef card, bool selected, int ownerSeat)
        {
            CardId = card.Id;
            _card.Bind(CardSnapshot.FromDef(card, ownerSeat, 0), CardView.ViewMode.Hand, card.Index);
            _card.SetFaceWidth(UiLayout.CardPoolCardWidth);
            _toggle.SetIsOnWithoutNotify(selected); _check.SetActive(selected);
        }
        public void SetSelected(bool selected) { _toggle.SetIsOnWithoutNotify(selected); _check.SetActive(selected); }
        private void OnChanged(bool selected) { _check.SetActive(selected); if (Changed != null) Changed(CardId, selected); }
    }
}
