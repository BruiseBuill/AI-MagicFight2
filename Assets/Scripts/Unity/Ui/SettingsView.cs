using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MagicBrawl.App
{
    /// <summary>设置与测试卡池的模态视图；草稿在保存成功之前不影响当前对局。</summary>
    public sealed class SettingsView : MonoBehaviour
    {
        [SerializeField] private BattleDriver _driver;
        [SerializeField] private GameObject _menu;
        [SerializeField] private GameObject _poolPanel;
        [SerializeField] private Button _resume;
        [SerializeField] private Button _restart;
        [SerializeField] private Button _test;
        [SerializeField] private Button _back;
        [SerializeField] private Button _save;
        [SerializeField] private Button _selectAll;
        [SerializeField] private Button _resetDefault;
        [SerializeField] private TMP_Text _count;
        [SerializeField] private TMP_Text _error;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RectTransform _content;
        [SerializeField] private CardPoolEntryView _entryTemplate;
        private readonly List<CardPoolEntryView> _entries = new List<CardPoolEntryView>();
        private CharacterDefinition _character;
        private CardPool _draft;
        private bool _open;
        private bool _previousPause;

        private void Awake()
        {
            _resume.onClick.AddListener(Close);
            _restart.onClick.AddListener(Restart);
            _test.onClick.AddListener(OpenCardPool);
            _back.onClick.AddListener(Back);
            _save.onClick.AddListener(Save);
            _selectAll.onClick.AddListener(SelectAll);
            _resetDefault.onClick.AddListener(ResetDefault);
        }

        private void OnEnable() { if (_driver != null) _driver.OnStarted += Close; }
        private void OnDisable()
        {
            if (_driver != null) _driver.OnStarted -= Close;
            if (_open && _driver != null) _driver.SetPaused(_previousPause);
            _open = false;
            _draft = null;
        }

        public void Open()
        {
            if (_open || _driver == null) return;
            _previousPause = _driver.IsPaused;
            _driver.SetPaused(true);
            _open = true;
            gameObject.SetActive(true);
            _menu.SetActive(true);
            _poolPanel.SetActive(false);
            _resume.Select();
        }

        public void Close()
        {
            if (!_open) return;
            _driver.SetPaused(_previousPause);
            _open = false;
            _draft = null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            gameObject.SetActive(false);
        }

        private void Restart() { _driver.StartBattle(); }

        public void OpenCardPool()
        {
            _character = _driver.GetLocalCharacterDefinition();
            _draft = _character.CreateCardPool();
            _menu.SetActive(false);
            _poolPanel.SetActive(true);
            IReadOnlyList<CardDef> cards = _driver.GetCardCatalog().All;
            for (int i = _entries.Count; i < cards.Count; i++)
            {
                CardPoolEntryView entry = Instantiate(_entryTemplate, _content);
                entry.name = "Card_" + cards[i].Id;
                entry.gameObject.SetActive(true);
                entry.Changed += ChangeCard;
                _entries.Add(entry);
            }
            for (int i = 0; i < _entries.Count; i++)
            {
                bool exists = i < cards.Count;
                _entries[i].gameObject.SetActive(exists);
                if (exists) _entries[i].Bind(cards[i], _draft.Contains(cards[i].Id), _driver.LocalSeat);
            }
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
            RefreshStatus();
            _back.Select();
        }

        private void ChangeCard(string id, bool selected)
        {
            if (_draft == null) return;
            if (selected) _draft.Add(id); else _draft.Remove(id);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            string error;
            _save.interactable = _draft.Validate(_character.MinimumCardCount, out error);
            _count.text = "已选 " + _draft.Count + " / " + _driver.GetCardCatalog().All.Count + " 张    ·    至少 " + _character.MinimumCardCount + " 张";
            _error.text = error ?? "保存后立即重新开局，并从新卡池随机发牌。";
        }

        private void SelectAll() { _draft = _driver.GetAllCardPool(); RefreshSelection(); }
        private void ResetDefault() { _draft = _driver.GetDefaultLocalCardPool(); RefreshSelection(); }
        private void RefreshSelection()
        {
            foreach (CardPoolEntryView entry in _entries) entry.SetSelected(_draft.Contains(entry.CardId));
            RefreshStatus();
        }

        private void Back()
        {
            _draft = null;
            _poolPanel.SetActive(false);
            _menu.SetActive(true);
            _resume.Select();
        }

        private void Save()
        {
            if (_draft == null || !_save.interactable) return;
            string error;
            if (!_driver.TrySaveCardPoolAndRestart(_draft, out error)) _error.text = error;
        }
    }
}
