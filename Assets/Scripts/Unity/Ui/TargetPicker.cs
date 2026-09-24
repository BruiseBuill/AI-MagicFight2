using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MagicBrawl.App
{
    /// <summary>
    /// 选项浮层（M8）：把 <see cref="DecisionSnapshot"/> 里<strong>不挂在手牌上</strong>的选项
    /// 画成可点按钮（放弃防御 / 区域类的 k 值 / 「选择完毕」…）。
    ///
    /// <para><b>为什么只画「非卡牌选项」</b>：带 <c>Card</c> 的选项（出哪张牌 / 加速哪张 /
    /// 减速哪张）已经有实体卡在屏幕上，直接点那张卡才符合直觉，再列一份按钮是重复。
    /// 这样选项浮层最多只有 4–5 行，不会顶到详情浮层（见 <see cref="UiLayout.PickerMaxRows"/>）。</para>
    ///
    /// <para><b>不判断合法性</b>：选项本身就是引擎过滤后的产物，本类只负责「画出来 + 把点到的
    /// 那个 Option 原样回传」，一条规则逻辑都不写（铁律第 3 条）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TargetPicker : MonoBehaviour
    {
        [Header("面板")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _title;

        [Header("行池")]
        [SerializeField] private RectTransform _rowRoot;
        [SerializeField] private GameObject[] _rows = new GameObject[0];
        [SerializeField] private Button[] _rowButtons = new Button[0];
        [SerializeField] private TMP_Text[] _rowLabels = new TMP_Text[0];

        private readonly List<Option> _bound = new List<Option>();

        /// <summary>点中了某个选项。</summary>
        public event Action<Option> OptionPicked;

        public bool IsOpen
        {
            get { return _panel != null && _panel.activeSelf; }
        }

        private void Awake()
        {
            for (int i = 0; i < _rowButtons.Length; i++)
            {
                int captured = i;
                if (_rowButtons[i] != null)
                {
                    _rowButtons[i].onClick.AddListener(() => OnRowClicked(captured));
                }
            }

            Hide();
        }

        /// <summary>
        /// 显示选项。<paramref name="title"/> 留空则隐藏标题行 —— 决策文案一般已经由顶部提示条
        /// 承担了（它在最上方、全宽、更醒目），浮层里再抄一遍就是同样的字出现两次。
        /// </summary>
        public void Show(string title, IReadOnlyList<Option> options)
        {
            _bound.Clear();

            if (_panel == null)
            {
                return;
            }

            bool hasTitle = !string.IsNullOrEmpty(title);
            if (_title != null)
            {
                _title.gameObject.SetActive(hasTitle);
                if (hasTitle)
                {
                    _title.text = title;
                }
            }

            int used = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                if (_rows[i] == null)
                {
                    continue;
                }

                if (options == null || i >= options.Count || used >= UiLayout.PickerMaxRows)
                {
                    _rows[i].SetActive(false);
                    continue;
                }

                Option opt = options[i];
                _rows[i].SetActive(true);

                if (_rowLabels[i] != null)
                {
                    _rowLabels[i].text = opt.Label;
                }

                _bound.Add(opt);
                used++;
            }

            // 行容器的高度按实际显示行数给 —— 面板高度是 ContentSizeFitter 撑的，
            // 写死成「满 4 行」会让只有 1 个选项的决策顶着一块空面板。
            if (_rowRoot != null)
            {
                LayoutElement le = _rowRoot.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = used <= 0
                        ? 0f
                        : used * UiLayout.PickerRowHeight + (used - 1) * UiLayout.PickerRowSpacing;

                    LayoutRebuilder.MarkLayoutForRebuild(_rowRoot);
                }
            }

            // 一个可点的按钮都没有（比如「有牌必须出」的进攻决策，选项全在实体卡上）——
            // 整个浮层收起来，别在屏幕中央留一块空面板。
            _panel.SetActive(used > 0);
        }

        public void Hide()
        {
            _bound.Clear();

            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        /// <summary>
        /// 只显示「不选择」那一个按钮（M24 #2）。
        ///
        /// <para><b>用在哪</b>：区域加速 / 减速的选项在引擎里是「(某一方, 剩余冷却 = k)」一档一个，
        /// 但这些档现在由<b>直接点某一侧的某一格冷却槽</b>来选（见 <see cref="CooldownView.ZoneRowClicked"/>）——
        /// 再把它们列成按钮，等于把屏幕上已经画出来的两片冷却区又用文字说一遍。
        /// 所以浮层里只留一条「不执行」的退路，其余选项仍留在本拍的
        /// <c>Options</c> 里等冷却区的点击来配对（铁律 3：选项由引擎给，UI 只负责挑出口）。</para>
        /// </summary>
        public void ShowSkipOnly(IReadOnlyList<Option> options)
        {
            Option skip = null;
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    if (options[i] != null && options[i].IsSkip)
                    {
                        skip = options[i];
                        break;
                    }
                }
            }

            Show(string.Empty, skip == null ? null : new[] { skip });
        }

        private void OnRowClicked(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _bound.Count)
            {
                return;
            }

            Option opt = _bound[rowIndex];
            if (OptionPicked != null)
            {
                OptionPicked(opt);
            }
        }
    }
}
