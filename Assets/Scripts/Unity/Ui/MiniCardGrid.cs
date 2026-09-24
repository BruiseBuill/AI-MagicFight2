using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 冷却区迷你卡的排布：默认自上而下竖排，超过
    /// <see cref="UiLayout.MiniCardsPerColumn"/> 张时自动折成第二列。
    ///
    /// 工程规划 §7 要求「冷却区卡牌缩小、竖排」——但一个玩家的牌最多 8 张，
    /// 8 张竖排会顶穿 736 px 的上部区域，所以按每列 4 张折列。
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    public sealed class MiniCardGrid : MonoBehaviour
    {
        [SerializeField] private int _rowsPerColumn = UiLayout.MiniCardsPerColumn;
        [SerializeField] private int _maxColumns = UiLayout.MiniMaxColumns;

        private GridLayoutGroup _grid;
        private int _lastActiveCount = -1;

        private void Awake()
        {
            _grid = GetComponent<GridLayoutGroup>();
        }

        private void OnEnable()
        {
            Reflow();
        }

        private void LateUpdate()
        {
            if (CountActiveChildren() != _lastActiveCount)
            {
                Reflow();
            }
        }

        /// <summary>按当前有效子节点数重排列数。</summary>
        public void Reflow()
        {
            if (_grid == null)
            {
                _grid = GetComponent<GridLayoutGroup>();
            }

            if (_grid == null)
            {
                return;
            }

            int n = CountActiveChildren();
            _lastActiveCount = n;

            int rows = Mathf.Max(1, _rowsPerColumn);
            int cols = n <= 0 ? 1 : Mathf.Clamp(Mathf.CeilToInt(n / (float)rows), 1, Mathf.Max(1, _maxColumns));

            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = cols;
        }

        private int CountActiveChildren()
        {
            int n = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).gameObject.activeSelf)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
