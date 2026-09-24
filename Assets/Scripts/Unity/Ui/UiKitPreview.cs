using MagicBrawl.Core;
using TMPro;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// M7 的<strong>自检装置</strong>：不连引擎，直接把样本卡灌进 Canvas，
    /// 用来肉眼验版式（左右对峙 / 冷却区在最左右 / 手牌底部全宽 / 迷你卡显示剩余冷却 / 光环亮点 / 折列）。
    ///
    /// 它只构造 <see cref="CardSnapshot"/> 这种只读快照，所以 M7 完全不需要依赖 M4 的引擎 ——
    /// 这也是分层铁律带来的额外好处：界面可以脱离规则独立开发和验收。
    /// M8 接上真正的 <c>BattleDriver</c> 后，这个组件就只留在预览场景里。
    /// </summary>
    public sealed class UiKitPreview : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private CardView _handCardPrefab;
        [SerializeField] private CardView _miniCardPrefab;

        [Header("挂载点")]
        [SerializeField] private RectTransform _handRoot;
        [SerializeField] private RectTransform _playerCoolingRoot;
        [SerializeField] private RectTransform _enemyCoolingRoot;

        [Header("信息条")]
        [SerializeField] private PlayerBarView _playerBar;
        [SerializeField] private PlayerBarView _enemyBar;
        [SerializeField] private TMP_Text _stageMark;

        [Header("行为")]
        [SerializeField] private bool _buildOnAwake = true;

        private void Awake()
        {
            if (_buildOnAwake)
            {
                BuildSample();
            }
        }

        /// <summary>用样本数据铺满界面。</summary>
        [ContextMenu("铺样本数据")]
        public void BuildSample()
        {
            BuildHand();
            BuildPlayerCooling();
            BuildEnemyCooling();
            BuildBars();
        }

        private void BuildHand()
        {
            if (_handRoot == null || _handCardPrefab == null)
            {
                return;
            }

            ClearChildren(_handRoot);

            // 前 8 张 —— 刚好是手牌上限，用来验「8 张横排是否放得下」
            int count = Mathf.Min(BattleState.HandLimit, CardLibrary.Count);
            for (int i = 0; i < count; i++)
            {
                CardDef def = CardLibrary.GetByIndex(i);
                CardSnapshot snap = CardSnapshot.FromDef(def, BattleState.SeatPlayer, 0);
                CardView view = Instantiate(_handCardPrefab, _handRoot);
                view.name = "HandCard_" + def.Id;
                view.Bind(snap, CardView.ViewMode.Hand, i);
                view.SetInteractable(true);
            }
        }

        private void BuildPlayerCooling()
        {
            if (_playerCoolingRoot == null || _miniCardPrefab == null)
            {
                return;
            }

            ClearChildren(_playerCoolingRoot);

            // 5 张 → 会折成 2 列，用来验折列逻辑
            AddMini(_playerCoolingRoot, "l", 2, 0, false);   // 磁暴：无光环、剩余 2
            AddMini(_playerCoolingRoot, "ak", 4, 1, true);   // 石化：光环 1 枚未用
            AddMini(_playerCoolingRoot, "af", 2, 2, true);   // 冰封铠甲：双光环都还在
            AddMini(_playerCoolingRoot, "ad", 1, 0, false);  // 雷云：剩余 1（强调色）
            AddMini(_playerCoolingRoot, "aa", 2, 0, false);  // 漩涡
        }

        private void BuildEnemyCooling()
        {
            if (_enemyCoolingRoot == null || _miniCardPrefab == null)
            {
                return;
            }

            ClearChildren(_enemyCoolingRoot);

            // 4 张 → 单列
            AddMini(_enemyCoolingRoot, "j", 4, 0, false);    // 闪电
            AddMini(_enemyCoolingRoot, "al", 3, 0, true);    // 石盾：光环已用完 → 亮点变灰
            AddMini(_enemyCoolingRoot, "t", 2, 0, false);    // 荆棘
            AddMini(_enemyCoolingRoot, "y", 1, 0, false);    // 瀑流：剩余 1
        }

        private void AddMini(RectTransform parent, string cardId, int remaining, int auraTokens, bool auraLive)
        {
            CardDef def = CardLibrary.Get(cardId);

            var snap = new CardSnapshot
            {
                CardId = def.Id,
                Name = def.Name,
                Power = def.Power,
                PowerText = def.PowerText,
                BaseCooldown = def.Cooldown,
                RemainingCooldown = remaining,
                AuraTokens = auraTokens,
                AuraTokenMax = def.AuraTokenCount,
                AuraLive = auraLive && def.AuraTokenCount > 0,
                InCoolingZone = true,
                Uid = 0,
                OwnerSeat = parent == _playerCoolingRoot ? BattleState.SeatPlayer : BattleState.SeatAi,
            };

            CardView view = Instantiate(_miniCardPrefab, parent);
            view.name = "MiniCard_" + def.Id;
            view.Bind(snap, CardView.ViewMode.Mini, parent.childCount - 1);
        }

        private void BuildBars()
        {
            if (_playerBar != null)
            {
                _playerBar.SetAccent(UiTheme.PlayerAccent);
                _playerBar.ShowHandCount = false;
                _playerBar.Bind(new PlayerSnapshot
                {
                    Seat = BattleState.SeatPlayer,
                    Name = "你",
                    Hp = 3,
                    MaxHp = 4,
                    IsAi = false,
                    HandCount = 8,
                    AuraTokensReady = 3,
                });
            }

            if (_enemyBar != null)
            {
                _enemyBar.SetAccent(UiTheme.EnemyAccent);
                _enemyBar.ShowHandCount = true;
                _enemyBar.Bind(new PlayerSnapshot
                {
                    Seat = BattleState.SeatAi,
                    Name = "AI",
                    Hp = 2,
                    MaxHp = 3,          // 上限被削过 → 第 4 个圆点画成「已削掉」
                    IsAi = true,
                    HandCount = 5,
                    AuraTokensReady = 0,
                });
            }

            if (_stageMark != null)
            {
                _stageMark.text = "VS";
            }
        }

        private static void ClearChildren(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>编辑器构建器用它把引用一次性接好（避免 SerializedObject 反射一堆私有字段）。</summary>
        public void EditorWire(
            CardView handPrefab,
            CardView miniPrefab,
            RectTransform handRoot,
            RectTransform playerCoolingRoot,
            RectTransform enemyCoolingRoot,
            PlayerBarView playerBar,
            PlayerBarView enemyBar,
            TMP_Text stageMark)
        {
            _handCardPrefab = handPrefab;
            _miniCardPrefab = miniPrefab;
            _handRoot = handRoot;
            _playerCoolingRoot = playerCoolingRoot;
            _enemyCoolingRoot = enemyCoolingRoot;
            _playerBar = playerBar;
            _enemyBar = enemyBar;
            _stageMark = stageMark;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
