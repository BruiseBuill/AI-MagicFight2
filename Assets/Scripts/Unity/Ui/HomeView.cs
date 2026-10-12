using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>游戏开始界面</b>（2026-10-05）—— 极简：一个标题、若干行存档、
    /// 两枚操作键（新建 / 删除）、一个「进入游戏」。
    ///
    /// <para><b>它管什么</b>：把「每一行显示什么 / 哪一行被选中 / 哪几行可见 /
    /// 删除键是不是在确认态 / 按钮亮不亮」铺出来，然后把
    /// <see cref="SlotClicked"/>（点了某一行）、<see cref="NewClicked"/>、
    /// <see cref="DeleteClicked"/>、<see cref="EnterClicked"/> 报出去。</para>
    ///
    /// <para><b>它不管什么</b>：<b>存档有几个、每一行对应哪个档、点了之后发生什么</b>
    /// 都不在这里 —— 那是 <see cref="HomeSceneEntry"/> 的事。
    /// 与其它界面同一分工：视图只认「摆」和「报」，不认规则。</para>
    ///
    /// <para><b>⚠ 行是「构建器建满、运行时开前 N 个」</b>：构建器按
    /// <c>UiLayout.HomeSlotRows</c> 建满（= <c>SaveSlots.PlayerSlots</c> 的长度），
    /// 运行时由入口调 <see cref="SetRowCount"/> 只显示「真的有存档」的那几行。
    /// 不在运行时 <c>Instantiate</c> 行 —— 这里的行是长期节点，构建器生成（铁律 4）。</para>
    ///
    /// <para><b>选中的表现方式是「描边」而不是「换色」</b>（铁律 3：颜色只写 prefab
    /// <c>Image.color</c>，运行时只切 active / enabled）：每一行底板上挂一枚
    /// <see cref="Outline"/>，默认 <c>enabled = false</c>，选中时打开。
    /// 若改成「运行时给底色赋值」，那个颜色不会进 Prefab ——
    /// 构建器重跑一次就丢，症状是「选中态有时有、有时没有」。</para>
    ///
    /// <para><b>节点由构建器建</b>（<c>HomeUiBuilder.BuildCanvas</c>），
    /// 版式常量在 <c>UiLayout.Home*</c>，配色复用 <c>UiTheme</c> 里已有的一套。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeView : MonoBehaviour
    {
        [Header("接线（由 HomeUiBuilder 写入）")]
        [Tooltip("大标题。")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("标题下面那行小字。")]
        [SerializeField] private TMP_Text _subtitle;

        [Tooltip("每一行存档的按钮。下标 = 第几行（0 = 第一行）。")]
        [SerializeField] private Button[] _slotButtons;

        [Tooltip("每一行的档名文本（左）。")]
        [SerializeField] private TMP_Text[] _slotNames;

        [Tooltip("每一行的状态文本（右）—— 「N 张牌」/「新档」。")]
        [SerializeField] private TMP_Text[] _slotStatus;

        [Tooltip("每一行的选中描边。默认 enabled = false，选中时打开。")]
        [SerializeField] private Outline[] _slotEdges;

        [Tooltip("「新建存档」按钮。")]
        [SerializeField] private Button _newButton;

        [Tooltip("「新建存档」上的文字。")]
        [SerializeField] private TMP_Text _newLabel;

        [Tooltip("「删除存档」按钮。")]
        [SerializeField] private Button _deleteButton;

        [Tooltip("「删除存档」上的文字（确认态时换成「确认删除」）。")]
        [SerializeField] private TMP_Text _deleteLabel;

        [Tooltip("「进入游戏」按钮。")]
        [SerializeField] private Button _enterButton;

        [Tooltip("「进入游戏」上的文字。")]
        [SerializeField] private TMP_Text _enterLabel;

        [Tooltip("底部提示行（选中哪个档 / 出错信息）。")]
        [SerializeField] private TMP_Text _hint;

        /// <summary>玩家点了第 <c>index</c> 行存档。</summary>
        public event Action<int> SlotClicked;

        /// <summary>玩家点了「新建存档」。</summary>
        public event Action NewClicked;

        /// <summary>玩家点了「删除存档」（第二下 = 确认删除，由入口自己记着状态）。</summary>
        public event Action DeleteClicked;

        /// <summary>玩家点了「进入游戏」。</summary>
        public event Action EnterClicked;

        /// <summary>界面上一共建了几行存档（= <c>UiLayout.HomeSlotRows</c>）。</summary>
        public int SlotCapacity
        {
            get { return _slotButtons == null ? 0 : _slotButtons.Length; }
        }

        /// <summary>构建器的接线口。</summary>
        public void Configure(TMP_Text title, TMP_Text subtitle,
            Button[] slotButtons, TMP_Text[] slotNames, TMP_Text[] slotStatus, Outline[] slotEdges,
            Button newButton, TMP_Text newLabel, Button deleteButton, TMP_Text deleteLabel,
            Button enterButton, TMP_Text enterLabel, TMP_Text hint)
        {
            _title = title;
            _subtitle = subtitle;
            _slotButtons = slotButtons;
            _slotNames = slotNames;
            _slotStatus = slotStatus;
            _slotEdges = slotEdges;
            _newButton = newButton;
            _newLabel = newLabel;
            _deleteButton = deleteButton;
            _deleteLabel = deleteLabel;
            _enterButton = enterButton;
            _enterLabel = enterLabel;
            _hint = hint;
        }

        /// <summary>运行时挂监听器用的委托（要留着，否则没法 RemoveListener）。</summary>
        private UnityAction[] _slotHandlers;

        private UnityAction _newHandler;
        private UnityAction _deleteHandler;
        private UnityAction _enterHandler;

        private void Awake()
        {
            Debug.Log("[HomeView] Awake: 行数=" + SlotCapacity
                      + " 新建=" + (_newButton == null ? "null" : _newButton.name)
                      + " 删除=" + (_deleteButton == null ? "null" : _deleteButton.name));
            Hook();
        }

        // ⚠ 注册写在 OnEnable 而不是只在 Awake：Awake 每个对象只跑一次，
        //   而 OnEnable 每次「重新激活」都会跑 —— 界面被 SetActive 关掉再打开时，
        //   Awake 里挂的那份监听器如果被 Unhook 掉了就再也回不来。
        //   Hook() 自己会先 RemoveListener 再 AddListener，所以走到两次也不会重复触发。
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

        /// <summary>
        /// 把监听器挂到每一枚按钮上（<b>可重入</b>：先摘旧的再挂新的）。
        ///
        /// <para><b>⚠ 铁律 8</b>：监听器一律在**运行时**按序列化字段自己接 —— 构建器在编辑模式
        /// 接的那些不是序列化数据，存 Prefab / 存场景时直接蒸发（症状是「每一项检查都通过，
        /// 但按钮点下去什么都没有」）。</para>
        /// </summary>
        private void Hook()
        {
            if (_slotButtons != null)
            {
                if (_slotHandlers == null || _slotHandlers.Length != _slotButtons.Length)
                {
                    _slotHandlers = new UnityAction[_slotButtons.Length];
                }

                for (int i = 0; i < _slotButtons.Length; i++)
                {
                    Button button = _slotButtons[i];
                    if (button == null)
                    {
                        continue;
                    }

                    if (_slotHandlers[i] != null)
                    {
                        button.onClick.RemoveListener(_slotHandlers[i]);
                    }

                    int index = i;                  // 闭包捕获的是这个副本，不是循环变量本身
                    _slotHandlers[i] = delegate { OnSlotClicked(index); };
                    button.onClick.AddListener(_slotHandlers[i]);
                }
            }

            HookOne(_newButton, ref _newHandler, OnNewClicked);
            HookOne(_deleteButton, ref _deleteHandler, OnDeleteClicked);
            HookOne(_enterButton, ref _enterHandler, OnEnterClicked);
        }

        /// <summary>单枚按钮的「先摘再挂」（可重入）。</summary>
        private static void HookOne(Button button, ref UnityAction handler, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            if (handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = action;
            button.onClick.AddListener(handler);
        }

        /// <summary>把挂上去的监听器摘掉（与 <see cref="Hook"/> 严格成对）。</summary>
        private void Unhook()
        {
            if (_slotButtons != null && _slotHandlers != null)
            {
                for (int i = 0; i < _slotButtons.Length && i < _slotHandlers.Length; i++)
                {
                    if (_slotButtons[i] != null && _slotHandlers[i] != null)
                    {
                        _slotButtons[i].onClick.RemoveListener(_slotHandlers[i]);
                    }
                }
            }

            UnhookOne(_enterButton, ref _enterHandler);
            UnhookOne(_newButton, ref _newHandler);
            UnhookOne(_deleteButton, ref _deleteHandler);
        }

        private static void UnhookOne(Button button, ref UnityAction handler)
        {
            if (button != null && handler != null)
            {
                button.onClick.RemoveListener(handler);
            }

            handler = null;
        }

        private void OnSlotClicked(int index)
        {
            Action<int> handler = SlotClicked;
            if (handler != null)
            {
                handler(index);
            }
        }

        private void OnNewClicked()
        {
            Action handler = NewClicked;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnDeleteClicked()
        {
            Action handler = DeleteClicked;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnEnterClicked()
        {
            Action handler = EnterClicked;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// <b>只显示前 <paramref name="count"/> 行</b>（其余整行关掉）。
        ///
        /// <para>行是构建器建好的长期节点（铁律 4），这里只切 <c>active</c> ——
        /// 关掉的行不参与布局也不吃射线，所以「存档 3 个」时不会留两行空白把按钮顶下去。
        /// 传 0 = 一行都不显示（此时界面提示玩家去点「新建存档」）。</para>
        /// </summary>
        public void SetRowCount(int count)
        {
            if (_slotButtons == null)
            {
                return;
            }

            for (int i = 0; i < _slotButtons.Length; i++)
            {
                if (_slotButtons[i] == null)
                {
                    continue;
                }

                // 关的是「整行」（按钮挂在行底板上，行的父级才是那个带定位的容器）——
                // 直接关按钮自己会留下一块空底板。
                Transform target = _slotButtons[i].transform.parent != null
                    ? _slotButtons[i].transform.parent
                    : _slotButtons[i].transform;
                bool visible = i < count;
                if (target.gameObject.activeSelf != visible)
                {
                    target.gameObject.SetActive(visible);
                }
            }
        }

        /// <summary>给第 <paramref name="index"/> 行填上档名与状态。</summary>
        public void SetSlotText(int index, string name, string status)
        {
            if (_slotNames != null && index >= 0 && index < _slotNames.Length && _slotNames[index] != null)
            {
                _slotNames[index].text = name ?? string.Empty;
            }

            if (_slotStatus != null && index >= 0 && index < _slotStatus.Length && _slotStatus[index] != null)
            {
                _slotStatus[index].text = status ?? string.Empty;
            }
        }

        /// <summary>把第 <paramref name="index"/> 行显示成「选中」，其余行收掉描边。传 −1 = 全不选。</summary>
        public void SetSelected(int index)
        {
            if (_slotEdges == null)
            {
                return;
            }

            for (int i = 0; i < _slotEdges.Length; i++)
            {
                if (_slotEdges[i] != null)
                {
                    _slotEdges[i].enabled = i == index;
                }
            }
        }

        /// <summary>删除键的文案切到「确认删除」/ 退回「删除存档」。</summary>
        public void SetDeleteArmed(bool armed)
        {
            if (_deleteLabel != null)
            {
                _deleteLabel.text = armed ? UiLayout.HomeDeleteArmedLabel : UiLayout.HomeDeleteLabel;
            }
        }

        /// <summary>「删除存档」「进入游戏」的可用状态（没有存档时两枚都点不动）。</summary>
        public void SetActionsInteractable(bool hasSelection)
        {
            if (_deleteButton != null)
            {
                _deleteButton.interactable = hasSelection;
            }

            if (_enterButton != null)
            {
                _enterButton.interactable = hasSelection;
            }

            if (_enterLabel != null)
            {
                _enterLabel.color = hasSelection ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            }
        }

        /// <summary>「新建存档」的可用状态（五个位都占了就点不动）。</summary>
        public void SetNewInteractable(bool interactable)
        {
            if (_newButton != null)
            {
                _newButton.interactable = interactable;
            }
        }

        /// <summary>底部那行小字。</summary>
        public void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text ?? string.Empty;
            }
        }
    }
}
