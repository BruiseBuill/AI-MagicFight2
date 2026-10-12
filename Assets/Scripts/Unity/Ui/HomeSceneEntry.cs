using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>游戏开始场景的单场景入口</b>（2026-10-05；<b>同日扩成多存档位</b>）。
    ///
    /// <para><b>它把这条链路串起来</b>：进 <c>Home</c> → 列出玩家的每一个存档 →
    /// 点一行选中 → 「进入游戏」记下档位、清掉上一趟冒险、切到地图（<c>Map</c>）。
    /// 另有「新建存档」（占下一个空位、随机起手牌、立刻落盘）与「删除存档」
    /// （<b>两步确认</b>，见 <see cref="OnDeleteClicked"/>）。
    /// 战斗失败界面的「退出」也会回到这里（<c>BattleUi.HandleQuitBattle</c>）。</para>
    ///
    /// <para><b>⚠ 「有哪些存档」只能用 <see cref="SaveStore.ListPlayerSaves"/></b>：
    /// 那一条判据是「文件在不在」。走 <c>SaveStore.TryLoadPlayer(Main)</c> 的副作用会
    /// <b>当场随机开一个新档并落盘</b> —— 只想看一眼列表就凭空写出一份 8 张牌的新档。</para>
    ///
    /// <para><b>⚠ 行号 ↔ 档位的映射在 <see cref="_saves"/> 这一个列表上</b>：
    /// 界面上第 <c>i</c> 行就是 <c>_saves[i]</c>。因为列表只装「有存档的档位」，
    /// 行号<b>不等于</b> <c>SaveSlots.PlayerSlots</c> 的下标 —— 中间有空洞时（比如只留了
    /// 存档 1 和存档 3）第 1 行是「存档 2」那一格的下标 2，掺着用必然选错档。</para>
    ///
    /// <para><b>⚠ 进入游戏时为什么要 <see cref="MapRun.Clear"/></b>：从主菜单点「进入游戏」
    /// 的语义是「开一趟新的」，而不是「接着上次没打完的那趟」。不清的话，
    /// 因为 <see cref="MapRun"/> 是静态的、换场景不会重置，玩家会直接掉进上一趟
    /// 已经打输（或走到一半）的地图里。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeSceneEntry : MonoBehaviour
    {
        [Header("视图（构建器接线）")]
        [SerializeField] private HomeView _view;

        /// <summary>界面上第 i 行对应哪个档（只装「已经有存档」的玩家档位，按档位顺序）。</summary>
        private readonly List<SaveSlot> _saves = new List<SaveSlot>();

        /// <summary>当前选中的行号；−1 = 没选中（一个存档都没有）。</summary>
        private int _selectedRow = -1;

        /// <summary>删除键是不是已经在「确认态」（点第二下才真的删）。</summary>
        private bool _deleteArmed;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<HomeView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[HomeSceneEntry] 没有接 HomeView —— 先跑 `魔法乱斗/P0 · 构建 Home 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）。
            _view.SlotClicked += OnSlotClicked;
            _view.NewClicked += OnNewClicked;
            _view.DeleteClicked += OnDeleteClicked;
            _view.EnterClicked += OnEnterClicked;

            Refresh();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.SlotClicked -= OnSlotClicked;
            _view.NewClicked -= OnNewClicked;
            _view.DeleteClicked -= OnDeleteClicked;
            _view.EnterClicked -= OnEnterClicked;
        }

        /// <summary>
        /// 重新读一遍「有哪些存档」、铺到界面上、默认选中第一行（没有就全不选）。
        ///
        /// <para>新建 / 删除之后都要调它 —— 界面永远是「磁盘上的事实」的投影，
        /// 别在内存里自己加减行。</para>
        /// </summary>
        private void Refresh()
        {
            _saves.Clear();
            _saves.AddRange(SaveStore.ListPlayerSaves());

            _view.SetRowCount(_saves.Count);
            for (int i = 0; i < _saves.Count; i++)
            {
                _view.SetSlotText(i, SaveSlots.DisplayName(_saves[i]), StatusOf(_saves[i]));
            }

            _deleteArmed = false;
            _view.SetDeleteArmed(false);
            _view.SetNewInteractable(_saves.Count < SaveSlots.PlayerSlotCount);

            Select(_saves.Count > 0 ? 0 : -1);
        }

        /// <summary>
        /// 某一档现在长什么样。
        ///
        /// <para><b>⚠ 先 <see cref="SaveStore.Exists"/> 再读</b>：没有文件时
        /// <c>TryLoadPlayer</c> 会顺手开一个新档并落盘（见类注释）。</para>
        /// </summary>
        private static string StatusOf(SaveSlot slot)
        {
            if (!SaveStore.Exists(slot))
            {
                return "空";
            }

            PlayerData player;
            string error;
            if (!SaveStore.TryLoadPlayer(slot, out player, out error) || player == null)
            {
                return "读不出来";
            }

            return player.cardIds.Count + " 张牌";
        }

        private void OnSlotClicked(int row)
        {
            // 点别的行 = 放弃「确认删除」这个状态（不然第二下会误删另一行）。
            SetDeleteArmed(false);
            Select(row);
        }

        /// <summary>把第 <paramref name="row"/> 行选上（越界 = 全不选）。</summary>
        private void Select(int row)
        {
            if (row < 0 || row >= _saves.Count)
            {
                _selectedRow = -1;
                _view.SetSelected(-1);
                _view.SetActionsInteractable(false);
                if (_saves.Count == 0)
                {
                    _view.SetHint("还没有存档　·　点「新建存档」开始");
                }

                return;
            }

            _selectedRow = row;
            _view.SetSelected(row);
            _view.SetActionsInteractable(true);
            _view.SetHint("已选中 " + SaveSlots.DisplayName(_saves[row])
                          + "　·　" + StatusOf(_saves[row]));
        }

        /// <summary>
        /// 「新建存档」：占下一个空位、随机 <c>SaveStore.MainStartingCardCount</c> 张起手牌、
        /// <b>立刻落盘</b>，然后刷新列表并选中它。
        /// </summary>
        private void OnNewClicked()
        {
            SaveSlot slot;
            if (!SaveStore.TryNextFreePlayerSlot(out slot))
            {
                _view.SetNewInteractable(false);
                _view.SetHint("存档位已满（一共 " + SaveSlots.PlayerSlotCount + " 个）—— 先删掉一个。");
                return;
            }

            PlayerData player;
            string error;
            if (!SaveStore.TryCreateNew(slot, out player, out error))
            {
                _view.SetHint(string.IsNullOrEmpty(error) ? "新建存档失败。" : error);
                return;
            }

            Refresh();
            Select(_saves.IndexOf(slot));
            _view.SetHint("已新建 " + SaveSlots.DisplayName(slot) + "　·　"
                          + (player == null ? 0 : player.cardIds.Count) + " 张起始牌");
        }

        /// <summary>
        /// 「删除存档」——<b>两步</b>：第一下只是把键切成「确认删除」并在提示行说清楚，
        /// 第二下才真的删。
        ///
        /// <para><b>⚠ 为什么必须两步</b>：删的是 <c>save-*.json</c>，不可逆；
        /// 而这枚键就在「进入游戏」旁边，误点一次的代价是玩家整本账没了。</para>
        /// </summary>
        private void OnDeleteClicked()
        {
            if (_selectedRow < 0 || _selectedRow >= _saves.Count)
            {
                return;
            }

            SaveSlot slot = _saves[_selectedRow];

            if (!_deleteArmed)
            {
                SetDeleteArmed(true);
                _view.SetHint("再点一次「确认删除」就会删掉 " + SaveSlots.DisplayName(slot)
                              + "（不可恢复）");
                return;
            }

            string error;
            if (!SaveStore.TryDelete(slot, out error))
            {
                SetDeleteArmed(false);
                _view.SetHint(string.IsNullOrEmpty(error) ? "删除存档失败。" : error);
                return;
            }

            int row = _selectedRow;
            Refresh();
            if (_saves.Count > 0)
            {
                Select(Mathf.Clamp(row, 0, _saves.Count - 1));
            }

            _view.SetHint("已删除 " + SaveSlots.DisplayName(slot));
        }

        private void SetDeleteArmed(bool armed)
        {
            _deleteArmed = armed;
            _view.SetDeleteArmed(armed);
        }

        /// <summary>「进入游戏」：定下档位、作废上一趟冒险，切到地图场景。</summary>
        private void OnEnterClicked()
        {
            if (_selectedRow < 0 || _selectedRow >= _saves.Count)
            {
                _view.SetHint("先选中一个存档（一个都没有就点「新建存档」）");
                return;
            }

            SaveSlot slot = _saves[_selectedRow];
            MapRun.Clear();
            GameSession.Choose(slot);

            Debug.Log("[HomeSceneEntry] 进入游戏：" + SaveSlots.DisplayName(slot)
                      + " → 场景 " + MapRoutes.MapScene);
            SceneManager.LoadScene(MapRoutes.MapScene);
        }
    }
}
