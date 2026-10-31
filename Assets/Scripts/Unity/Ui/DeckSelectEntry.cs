using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>自定义战斗 · 牌组编成的规则层</b>（2026-10-28 · 大改新增）。
    ///
    /// <para><b>状态只有两个列表</b>：<see cref="_playerIds"/> 与 <see cref="_enemyIds"/>，
    /// 各最多 <see cref="BattleLaunch.DeckSize"/> 张。<b>页签（<see cref="_tab"/>）只决定
    /// 「现在点卡表是在改哪一边」</b>，不是第三种状态。</para>
    ///
    /// <para><b>点一张卡的语义</b>：已在当前页签里 → 移除；还不在且没满 → 加入；
    /// 已满 → 什么都不做，只在提示行说一句。</para>
    ///
    /// <para><b>开战</b>：两边都满 8 张才亮。点下去走
    /// <see cref="BattleLaunch.TrySetCustom"/>（<b>校验只有那一处</b>），成功才切战斗场景。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckSelectEntry : MonoBehaviour
    {
        [Header("视图（构建器接线）")]
        [SerializeField] private DeckSelectView _view;

        /// <summary>整张卡表（顺序固定；卡表是唯一事实源）。</summary>
        private readonly List<CardDef> _cards = new List<CardDef>();

        private readonly List<string> _playerIds = new List<string>();
        private readonly List<string> _enemyIds = new List<string>();

        /// <summary>0 = 你 / 1 = 对手。</summary>
        private int _tab;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<DeckSelectView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[DeckSelectEntry] 没有接 DeckSelectView —— "
                               + "先跑 `魔法乱斗/D1 · 构建 DeckSelect 场景`。");
                return;
            }

            // 2026-10-30：经典模式下只列前 40 张（与战斗侧同一个出口）。
            ICardCatalog catalog = GameSettings.Filter(CardCatalog.Builtin());
            for (int i = 0; i < catalog.All.Count; i++)
            {
                if (catalog.All[i] != null)
                {
                    _cards.Add(catalog.All[i]);
                }
            }

            // 上一趟的自定义牌组带回来（玩家从战斗「返回主菜单」再进编成界面时不用重挑）。
            if (BattleLaunch.HasCustomDecks)
            {
                // ⚠ 上一趟可能是「全卡表」时挑的（含 ao..ar）；这一趟开了经典模式，
                //   那几张就不在 _cards 里了。直接 AddRange 会让界面去找一张不存在的卡 ——
                //   所以按当前卡表过一遍，只带得回来的那几张。
                AppendAllowed(_playerIds, BattleLaunch.PlayerCardIds, catalog);
                AppendAllowed(_enemyIds, BattleLaunch.MonsterCardIds, catalog);
            }

            _view.TabClicked += OnTabClicked;
            _view.CatalogCardClicked += OnCatalogCardClicked;
            _view.StartClicked += OnStartClicked;
            _view.BackClicked += OnBackClicked;

            _tab = 0;
            Refresh();

            Debug.Log("[DeckSelectEntry] 卡表 " + _cards.Count + " 张，"
                      + "你的牌组 " + _playerIds.Count + " 张，对手 " + _enemyIds.Count + " 张。");
        }

        /// <summary>把 <paramref name="ids"/> 里当前卡表还认得的那些追加进 <paramref name="sink"/>。</summary>
        private static void AppendAllowed(List<string> sink, IReadOnlyList<string> ids, ICardCatalog catalog)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                for (int k = 0; k < catalog.All.Count; k++)
                {
                    CardDef card = catalog.All[k];
                    if (card != null && string.Equals(card.Id, id, StringComparison.Ordinal))
                    {
                        sink.Add(id);
                        break;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.TabClicked -= OnTabClicked;
            _view.CatalogCardClicked -= OnCatalogCardClicked;
            _view.StartClicked -= OnStartClicked;
            _view.BackClicked -= OnBackClicked;
        }

        private List<string> CurrentIds
        {
            get { return _tab == 0 ? _playerIds : _enemyIds; }
        }

        private List<string> OtherIds
        {
            get { return _tab == 0 ? _enemyIds : _playerIds; }
        }

        private void OnTabClicked(int index)
        {
            _tab = index == 1 ? 1 : 0;
            Refresh();
        }

        private void OnCatalogCardClicked(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= _cards.Count)
            {
                return;
            }

            string id = _cards[catalogIndex].Id;
            List<string> ids = CurrentIds;

            int at = ids.IndexOf(id);
            if (at >= 0)
            {
                ids.RemoveAt(at);
            }
            else if (OtherIds.Contains(id))
            {
                Refresh();
                _view.SetHint("这张牌已被另一方选走 —— 双方共用同一副牌，不能重复。");
                return;
            }
            else if (ids.Count >= BattleLaunch.DeckSize)
            {
                Refresh();
                _view.SetHint("这一方已经选满 " + BattleLaunch.DeckSize + " 张 —— 先点掉一张再换。");
                return;
            }
            else
            {
                ids.Add(id);
            }

            Refresh();
        }

        private void OnStartClicked()
        {
            string error;
            if (!BattleLaunch.TrySetCustom(_playerIds, _enemyIds, out error))
            {
                _view.SetHint(string.IsNullOrEmpty(error) ? "两边的牌组都要满 8 张才能开战。" : error);
                return;
            }

            Debug.Log("[DeckSelectEntry] 开战 → " + SceneRoutes.BattleScene
                      + "（你 " + _playerIds.Count + " 张 / 对手 " + _enemyIds.Count + " 张）");
            SceneRoutes.LoadBattle();
        }

        private void OnBackClicked()
        {
            SceneRoutes.LoadHome();
        }

        /// <summary>把两份状态投影到界面上（界面永远是数据的投影，别在点击里自己加减节点）。</summary>
        private void Refresh()
        {
            _view.SetTab(_tab, _playerIds.Count, _enemyIds.Count);

            var picked = new HashSet<string>(CurrentIds);
            _view.ShowCatalog(_cards, picked);
            _view.ShowDeck(Resolve(CurrentIds));

            bool ready = _playerIds.Count == BattleLaunch.DeckSize
                         && _enemyIds.Count == BattleLaunch.DeckSize;
            _view.SetStartInteractable(ready);

            _view.SetHint(ready
                ? "两边各 " + BattleLaunch.DeckSize + " 张 · 点「开战」开始"
                : "点卡表里的牌加入 / 移除　·　两边都满 " + BattleLaunch.DeckSize + " 张才能开战");
        }

        /// <summary>把 ID 清单解析成卡定义（按卡表顺序，与 <c>CardPool.Resolve</c> 同口径）。</summary>
        private List<CardDef> Resolve(IReadOnlyList<string> ids)
        {
            var list = new List<CardDef>(ids.Count);
            var set = new HashSet<string>(ids);
            for (int i = 0; i < _cards.Count; i++)
            {
                if (set.Contains(_cards[i].Id))
                {
                    list.Add(_cards[i]);
                }
            }

            return list;
        }
    }
}
