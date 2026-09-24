using System;
using System.Collections;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 发牌台（M9）：开局发 6 张、以及第 2/3 回合补牌之后的<strong>图形化替换交互</strong>。
    ///
    /// <para><b>它补的是哪个洞</b>：引擎的 <c>ChooseReplace</c> 决策本来就是<strong>多选</strong>
    /// （<c>MinSelect = 0</c>、<c>MaxSelect = 至多 3</c>），而 M8 里点一张牌就立刻回填了一个序号 ——
    /// 于是「可替换至多 3 张」实际上只能换 1 张，而且没有任何「已挑了几张」的反馈。
    /// 本类把这件事补齐：<b>点手牌 = 标记 / 取消标记</b>，选够了一次性回填。</para>
    ///
    /// <para><b>为什么不自己判断规则</b>：哪些牌能被替换、最多换几张，全在
    /// <c>DecisionSnapshot</c> 的 <c>Options</c> 与 <c>MaxSelect</c> 里 —— 引擎已经把非法项滤掉了，
    /// 本类只做「标记 + 数数量 + 把序号回传」（铁律第 3 条）。连「不替换」那颗按钮的文字
    /// 都直接用引擎给的 Done 选项 <c>Label</c>：开局是「不替换，开始对局」，补牌后是「保留这张，继续」，
    /// 一句话都不用在 UI 里硬编码。</para>
    ///
    /// <para><b>两个入口不重复</b>：替换决策期间 <see cref="TargetPicker"/> 整个收起 ——
    /// Done 选项改由这里承担，否则同一件事会有两个按钮。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DealView : MonoBehaviour
    {
        [Header("面板")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _counter;
        [SerializeField] private TMP_Text _hint;

        [Header("按钮")]
        [SerializeField] private Button _confirmButton;
        [SerializeField] private TMP_Text _confirmLabel;
        [SerializeField] private Button _skipButton;
        [SerializeField] private TMP_Text _skipLabel;

        /// <summary>玩家点了「确认替换」：回填选中的<strong>选项序号</strong>（按标记顺序）。</summary>
        public event Action<List<int>> Confirmed;

        /// <summary>玩家点了 Done（不替换 / 保留这张）：原样回传引擎给的那个选项。</summary>
        public event Action<Option> Skipped;

        private readonly List<Option> _replaceOptions = new List<Option>();
        private readonly List<Option> _selected = new List<Option>();
        private readonly List<int> _uidBuf = new List<int>();
        private readonly List<int> _indexBuf = new List<int>();

        private Option _doneOption;
        private HandView _hand;
        private int _maxSelect = 1;

        /// <summary>
        /// 本次是「预先标记好的单候选」—— 见 <see cref="Show"/>。
        /// 只用来决定提示行文案；选中集合本身仍然是 <see cref="_selected"/>，
        /// 玩家点那张牌照样能把它取消掉（<see cref="Toggle"/> 是对称的）。
        /// </summary>
        private bool _autoSelected;

        private Coroutine _flashRoutine;

        public bool IsOpen
        {
            get { return _panel != null && _panel.activeSelf; }
        }

        /// <summary>当前已标记几张。</summary>
        public int SelectedCount
        {
            get { return _selected.Count; }
        }

        private void Awake()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.AddListener(OnConfirmClicked);
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.AddListener(OnSkipClicked);
            }

            Hide();
        }

        // ══════════════════════════════════════════════════════
        //  开合
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 打开发牌台。<paramref name="snap"/> 必须是 <c>ChooseReplace</c> 决策 ——
        /// 本类只认识这一种，别的决策由 <see cref="TargetPicker"/> 负责。
        /// </summary>
        public void Show(DecisionSnapshot snap, HandView hand)
        {
            _hand = hand;
            _replaceOptions.Clear();
            _selected.Clear();
            _doneOption = null;

            // MaxSelect 是「这次最多能换几张」：开局是 3，补牌后是 1。
            _maxSelect = Mathf.Max(1, snap.MaxSelect);

            if (snap.Options != null)
            {
                for (int i = 0; i < snap.Options.Count; i++)
                {
                    Option o = snap.Options[i];
                    if (o.Kind == OptionKind.Replace && o.Card != null)
                    {
                        _replaceOptions.Add(o);
                    }
                    else if (_doneOption == null
                             && (o.Kind == OptionKind.Done || o.Kind == OptionKind.Skip))
                    {
                        _doneOption = o;
                    }
                }
            }

            // 按钮文字来自引擎：开局 =「不替换，开始对局」，补牌后 =「保留这张，继续」。
            if (_skipLabel != null)
            {
                _skipLabel.text = _doneOption != null && !string.IsNullOrEmpty(_doneOption.Label)
                    ? _doneOption.Label
                    : "不替换";
            }

            if (_confirmLabel != null)
            {
                _confirmLabel.text = "确认替换";
            }

            // 只有一个候选（= 第 2 / 3 回合的补牌替换）→ **直接预先标记它**。
            //
            // 用户口径（2026-09-20）：这时候「能换掉的只有刚到手的那一张」，
            // 让玩家再点它一下是纯粹多余的一步 —— 两个按钮本身已经把「换 / 不换」问清楚了。
            // 判据用「候选只有 1 个且上限为 1」而不是「是不是第 2 回合」：开局那趟的候选是整副手牌，
            // 引擎给的 MaxSelect 必然 > 1，两者不会混。
            _autoSelected = _replaceOptions.Count == 1 && _maxSelect == 1;
            if (_autoSelected)
            {
                _selected.Add(_replaceOptions[0]);
            }

            RefreshHint();

            if (_hand != null)
            {
                _hand.SetMultiSelect(true);
            }

            if (_panel != null)
            {
                _panel.SetActive(true);
            }

            RefreshCounter();
            ApplySelection();
        }

        public void Hide()
        {
            StopFlash();

            _replaceOptions.Clear();
            _selected.Clear();
            _doneOption = null;
            _autoSelected = false;

            if (_hand != null)
            {
                _hand.SetMultiSelect(false);
                _hand.SetMultiSelected(null);
            }

            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  标记
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 点了一张手牌：标记 / 取消标记。<b>不提交</b> —— 提交只发生在「确认替换」上，
        /// 这样「至多换 3 张」才真的能一次换 3 张。
        /// </summary>
        public void Toggle(int uid)
        {
            if (!IsOpen)
            {
                return;
            }

            Option target = FindByUid(uid);
            if (target == null)
            {
                return;
            }

            int at = IndexOfOption(target);
            if (at >= 0)
            {
                _selected.RemoveAt(at);
            }
            else if (_selected.Count < _maxSelect)
            {
                _selected.Add(target);
            }
            else
            {
                // 已经选满：不改选中集合，只闪一下计数行告个知。
                StartFlash();
                return;
            }

            StopFlash();
            ApplySelection();
            RefreshCounter();
            RefreshHint();   // 「已选 1/1」被玩家点掉之后提示行要跟着改口
        }

        private void ApplySelection()
        {
            if (_hand == null)
            {
                return;
            }

            _uidBuf.Clear();
            for (int i = 0; i < _selected.Count; i++)
            {
                _uidBuf.Add(_selected[i].Card.Uid);
            }

            _hand.SetMultiSelected(_uidBuf);
        }

        /// <summary>
        /// 写提示行 —— 提示「怎么操作」；「做什么」（换几张）由顶部提示条里的引擎文案承担，不重复。
        ///
        /// <para>三种口气：开局多选 / 补牌单候选且已预选 / 单候选但玩家刚把它取消了。
        /// 第三档不能沿用第二档的文案，否则界面上写着「已选中」而计数行是「已选 0 / 1」。</para>
        /// </summary>
        private void RefreshHint()
        {
            if (_hint == null)
            {
                return;
            }

            if (_maxSelect > 1)
            {
                _hint.text = "点手牌标记要替换的牌";
            }
            else if (_autoSelected && _selected.Count > 0)
            {
                _hint.text = "已选中刚补到的这张 —— 点「确认替换」换掉它，或直接保留";
            }
            else
            {
                _hint.text = "点这张牌即可标记替换";
            }
        }

        private void RefreshCounter()
        {
            if (_counter != null)
            {
                _counter.text = "已选 " + _selected.Count + " / " + _maxSelect;

                // 有标记时数字行转成高亮色 —— 「已选 0 / 3」和「已选 2 / 3」要一眼能分开。
                _counter.color = _selected.Count > 0 ? UiTheme.SelectionGlow : UiTheme.TextSecondary;
            }

            // 一张没标记时「确认替换」没有意义（MinSelect = 0，等价于不替换）。
            if (_confirmButton != null)
            {
                _confirmButton.interactable = _selected.Count > 0;
            }
        }

        // ══════════════════════════════════════════════════════
        //  提交
        // ══════════════════════════════════════════════════════

        private void OnConfirmClicked()
        {
            if (_selected.Count <= 0)
            {
                return;
            }

            _indexBuf.Clear();
            for (int i = 0; i < _selected.Count; i++)
            {
                _indexBuf.Add(_selected[i].Index);
            }

            // 先取出副本再抛事件：订阅方收到后通常会 Hide()，把 _indexBuf 清掉。
            var payload = new List<int>(_indexBuf);
            if (Confirmed != null)
            {
                Confirmed(payload);
            }
        }

        private void OnSkipClicked()
        {
            if (_doneOption == null)
            {
                return;
            }

            if (Skipped != null)
            {
                Skipped(_doneOption);
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private Option FindByUid(int uid)
        {
            for (int i = 0; i < _replaceOptions.Count; i++)
            {
                Option o = _replaceOptions[i];
                if (o.Card != null && o.Card.Uid == uid)
                {
                    return o;
                }
            }

            return null;
        }

        private int IndexOfOption(Option option)
        {
            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i].Index == option.Index)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>选满之后又点多一张 —— 闪一下计数行，比默默无反应清楚。</summary>
        private void StartFlash()
        {
            if (_counter == null || !isActiveAndEnabled)
            {
                return;
            }

            StopFlash();
            _flashRoutine = StartCoroutine(CoFlashCounter());
        }

        private void StopFlash()
        {
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }

            RefreshCounter();
        }

        private IEnumerator CoFlashCounter()
        {
            _counter.color = UiTheme.WarnRed;
            yield return new WaitForSecondsRealtime(0.32f);
            _flashRoutine = null;
            RefreshCounter();
        }
    }
}
