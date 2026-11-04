using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>自定义战斗 · 牌组编成的规则层</b>（2026-10-28 新增，2026-11-01 跟进新版界面）。
    ///
    /// <para><b>状态只有两个列表</b>：<see cref="_playerIds"/> 与 <see cref="_enemyIds"/>，
    /// 各最多 <see cref="BattleLaunch.DeckSize"/> 张。<b>页签（<see cref="_tab"/>）只决定
    /// 「现在点卡表是在改哪一边」</b>，不是第三种状态。</para>
    ///
    /// <para><b>点一张卡的语义</b>：已在当前页签里 → 移除；已被另一方选走 → 什么都不做，
    /// 只在提示行说一句；还不在且没满 → 加入；已满 → 什么都不做，提示一句。</para>
    ///
    /// <para><b>2026-11-01 新增的两条入口</b>：8 格右上角的 × （<see cref="OnSlotRemoveClicked"/>）
    /// 与底部「清空」（<see cref="OnClearClicked"/>）—— 都只是同一份状态的不同写法，
    /// 改完统一走 <see cref="Refresh"/>，界面永远是数据的投影。</para>
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
            _view.SlotRemoveClicked += OnSlotRemoveClicked;
            _view.StartClicked += OnStartClicked;
            _view.BackClicked += OnBackClicked;
            _view.ClearClicked += OnClearClicked;
            _view.RandomClicked += OnRandomClicked;
            _view.HistoryClicked += OnHistoryClicked;
            _view.HistoryClosed += OnHistoryClosed;
            _view.HistoryLoadClicked += OnHistoryLoadClicked;
            _view.HistoryDeleteClicked += OnHistoryDeleteClicked;

            _tab = 0;
            Refresh();
            _view.SetHistoryBadge(DeckHistoryStore.Count);

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
            _view.SlotRemoveClicked -= OnSlotRemoveClicked;
            _view.StartClicked -= OnStartClicked;
            _view.BackClicked -= OnBackClicked;
            _view.ClearClicked -= OnClearClicked;
            _view.RandomClicked -= OnRandomClicked;
            _view.HistoryClicked -= OnHistoryClicked;
            _view.HistoryClosed -= OnHistoryClosed;
            _view.HistoryLoadClicked -= OnHistoryLoadClicked;
            _view.HistoryDeleteClicked -= OnHistoryDeleteClicked;
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
                _view.SetHint("这张牌已被另一方选走 —— 双方共用同一张卡表，同一张不能重复。");
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

        /// <summary>8 格右上角的 ×：把那一格清掉。</summary>
        private void OnSlotRemoveClicked(int slotIndex)
        {
            List<string> ordered = ResolveIds(CurrentIds);
            if (slotIndex < 0 || slotIndex >= ordered.Count)
            {
                return;
            }

            CurrentIds.Remove(ordered[slotIndex]);
            Refresh();
        }

        /// <summary>底部「清空」：只清当前页签这一方，不动另一边。</summary>
        private void OnClearClicked()
        {
            CurrentIds.Clear();
            Refresh();
        }

        // ══════════════════════════════════════════════════════
        //  随机 8 张（2026-11-02）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 「随机 8 张」：<b>只洗当前页签这一方</b>，另一方的牌一张不动。
        ///
        /// <para><b>池子口径</b>：当前卡表 − <b>另一方已选</b>。双方共用同一副牌表
        /// （同一张不能重复选），所以随机也不能把对方占着的牌抽给你 ——
        /// 否则「随机一次，结果开战报错」就成了最气人的路径。</para>
        ///
        /// <para>整方覆盖而不是「补空位」：用户点的是「随机选牌」，
        /// 期望的是<b>换一套</b>；补空位在已有 7 张时会让人以为按钮没反应。</para>
        /// </summary>
        private void OnRandomClicked()
        {
            var pool = new List<CardDef>(_cards.Count);
            for (int i = 0; i < _cards.Count; i++)
            {
                CardDef card = _cards[i];
                if (card != null && !OtherIds.Contains(card.Id))
                {
                    pool.Add(card);
                }
            }

            if (pool.Count < BattleLaunch.DeckSize)
            {
                Refresh();
                _view.SetHint("可选的牌不够 " + BattleLaunch.DeckSize + " 张 —— 对方那边占得太多了。");
                return;
            }

            List<string> ids = CurrentIds;
            ids.Clear();
            for (int i = 0; i < BattleLaunch.DeckSize; i++)
            {
                int pick = UnityEngine.Random.Range(0, pool.Count);
                ids.Add(pool[pick].Id);
                pool.RemoveAt(pick);
            }

            Refresh();

            string side = _tab == 0 ? "你的牌组" : "对手的牌组";
            _view.SetHint("已随机填入 " + BattleLaunch.DeckSize + " 张（" + side + "）· 另一方的牌没有动");
            _view.ShowToast("已随机填入 " + BattleLaunch.DeckSize + " 张");
        }

        // ══════════════════════════════════════════════════════
        //  历史牌组（2026-11-02）
        // ══════════════════════════════════════════════════════

        private void OnHistoryClicked()
        {
            ShowHistoryNow();
        }

        private void OnHistoryClosed()
        {
            _view.HideHistory();
        }

        /// <summary>
        /// 把存档里的某一条整组载入<b>当前页签这一方</b>。
        ///
        /// <para><b>与另一方撞牌怎么办</b>：静默覆盖对手会让人莫名其妙丢牌，所以
        /// 这里选择「从另一方移除那几张」<b>并在提示里说清楚</b> —— 双方共用一张卡表，
        /// 不处理的话开战校验会直接失败，那才是死路。</para>
        /// </summary>
        private void OnHistoryLoadClicked(int storeIndex)
        {
            DeckHistoryEntry entry = DeckHistoryStore.At(storeIndex);
            if (entry == null)
            {
                ShowHistoryNow();
                return;
            }

            List<CardDef> cards = Resolve(entry.ids);
            if (cards.Count != BattleLaunch.DeckSize)
            {
                _view.SetHint("这一条里有 " + (BattleLaunch.DeckSize - cards.Count)
                              + " 张已经不在当前卡表里，载入不了。");
                return;
            }

            int bumped = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (OtherIds.Remove(cards[i].Id))
                {
                    bumped++;
                }
            }

            CurrentIds.Clear();
            for (int i = 0; i < cards.Count; i++)
            {
                CurrentIds.Add(cards[i].Id);
            }

            Refresh();
            _view.HideHistory();

            string side = _tab == 0 ? "你的牌组" : "对手的牌组";
            string owner = entry.Owner == DeckOwner.Player ? "玩家" : "AI";
            _view.SetHint("已把「" + owner + "」用过的 " + BattleLaunch.DeckSize + " 张载入到「" + side + "」"
                           + (bumped > 0 ? "　·　另一边有 " + bumped + " 张重复，已从那边移除" : string.Empty));
            _view.ShowToast("已载入 " + BattleLaunch.DeckSize + " 张");
        }

        private void OnHistoryDeleteClicked(int storeIndex)
        {
            if (!DeckHistoryStore.RemoveAt(storeIndex))
            {
                return;
            }

            _view.SetHistoryBadge(DeckHistoryStore.Count);
            ShowHistoryNow();
        }

        /// <summary>把存档投影成行：解析不了的（有卡已不在当前卡表）整条跳过。</summary>
        private void ShowHistoryNow()
        {
            IReadOnlyList<DeckHistoryEntry> all = DeckHistoryStore.All;
            var rows = new List<DeckHistoryRowData>(all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                DeckHistoryEntry entry = all[i];
                if (entry == null)
                {
                    continue;
                }

                List<CardDef> cards = Resolve(entry.ids);
                if (cards.Count != BattleLaunch.DeckSize)
                {
                    continue;
                }

                rows.Add(new DeckHistoryRowData(entry.Owner, cards.ToArray(), i));
            }

            _view.ShowHistory(rows);
        }

        private void OnStartClicked()
        {
            string error;
            if (!BattleLaunch.TrySetCustom(_playerIds, _enemyIds, out error))
            {
                _view.SetHint(string.IsNullOrEmpty(error) ? "两边的牌组都要满 8 张才能开战。" : error);
                return;
            }

            // 2026-11-02：开战这一刻把双方各记一条历史（用户口径「每一次开战之后默认保存」）。
            // ⚠ 校验通过之后才写 —— 半成品牌组不该进历史，否则第一次载入就会拿到 5 张。
            // ⚠ 先写 AI 再写玩家：两条都是插到最前，反过来的话列表第一行永远是「对手那套」，
            //   而玩家打开历史第一个想找的通常是自己刚用的那副。
            DeckHistoryStore.Add(DeckOwner.Ai, _enemyIds);
            DeckHistoryStore.Add(DeckOwner.Player, _playerIds);
            _view.SetHistoryBadge(DeckHistoryStore.Count);

            Debug.Log("[DeckSelectEntry] 开战 → " + SceneRoutes.BattleScene
                      + "（你 " + _playerIds.Count + " 张 / 对手 " + _enemyIds.Count + " 张）"
                      + " · 历史 " + DeckHistoryStore.Count + " 条");
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
            var taken = new HashSet<string>(OtherIds);
            _view.ShowCatalog(_cards, picked, taken);
            _view.ShowDeck(Resolve(CurrentIds));

            bool ready = _playerIds.Count == BattleLaunch.DeckSize
                         && _enemyIds.Count == BattleLaunch.DeckSize;
            _view.SetStartInteractable(ready);
            _view.SetClearInteractable(CurrentIds.Count > 0);

            _view.SetHint(ready
                ? "两边各 " + BattleLaunch.DeckSize + " 张 · 点「开战」开始"
                : "点卡表里的牌加入 / 移除　·　两边都满 " + BattleLaunch.DeckSize + " 张才能开战"
                  + "　·　还差 " + (BattleLaunch.DeckSize - _playerIds.Count) + " 张（你）+ "
                  + (BattleLaunch.DeckSize - _enemyIds.Count) + " 张（对手）");
        }

        /// <summary>
        /// 把 ID 清单按<b>卡表顺序</b>排好 —— 8 格与「点 × 删哪一张」共用同一份次序，
        /// 否则格子的下标和列表下标对不上（旧版就只按卡表顺序画、按点击顺序存）。
        /// </summary>
        private List<string> ResolveIds(IReadOnlyList<string> ids)
        {
            var list = new List<string>(ids.Count);
            var set = new HashSet<string>(ids);
            for (int i = 0; i < _cards.Count; i++)
            {
                if (set.Contains(_cards[i].Id))
                {
                    list.Add(_cards[i].Id);
                }
            }

            return list;
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
