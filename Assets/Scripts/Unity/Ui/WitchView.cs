using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊场景的<b>运行时视图</b>（2026-10-01 · P6）。
    ///
    /// <para><b>职责边界</b>（与 <see cref="ShopView"/> / <see cref="UpgradeView"/> 同口径）：
    /// 本类只做三件事 —— ① 把场景里两个可点的东西（水晶球 / 离开）的点击冒泡成事件；
    /// ② 管标题与引导文字的显隐；③ 把里面那两层浮层（<see cref="WitchLayerView"/> =
    /// 两个空位那一层、<see cref="WitchPickerView"/> = 卡池浏览层）暴露给上层。
    /// <b>它不认识卡表、不认识规则、不认识存档</b> ——
    /// 「卡池够不够开工、哪张牌能当目标、两张牌撞没撞」全是上层
    /// （<c>WitchSceneEntry</c> → 将来是冒险流程桥）的事。</para>
    ///
    /// <para><b>为什么两层浮层不在这里包一层事件</b>：它们各自已经是有状态的对象
    /// （<c>IsOpen</c> / 自己开关自己），再包一层转发只会多出「忘了接某个事件」的机会。
    /// 上层直接订阅它们 —— 见 <see cref="Layer"/> / <see cref="Picker"/>。</para>
    ///
    /// <para><b>卡面复用唯一那份 <c>CardView_Hand.prefab</c></b>（铁律 9）：
    /// 空位里的卡与浏览层里的卡都是 <see cref="CardView"/>，尺寸由 <c>SetFaceWidth</c> 在运行时给。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchView : MonoBehaviour
    {
        /// <summary>水晶球命中区（一整块透明的矩形，**唯一**吃「点水晶球」这一下的东西）。</summary>
        [SerializeField] private Button _orbButton;

        [SerializeField] private Button _leaveButton;
        [SerializeField] private TMP_Text _leaveLabel;
        [SerializeField] private TMP_Text _title;

        /// <summary>水晶球下方那行引导文字（浮层一打开就藏起来）。</summary>
        [SerializeField] private GameObject _hint;

        [SerializeField] private TMP_Text _hintText;

        /// <summary>主浮层（两个空位 + 状态行 + 确认 / 关闭）。</summary>
        [SerializeField] private WitchLayerView _layer;

        /// <summary>卡池浏览层（点空位之后弹出来的那一层）。</summary>
        [SerializeField] private WitchPickerView _picker;

        /// <summary>玩家点了水晶球。上层拿它去判「卡池够不够」再决定弹不弹浮层。</summary>
        public event Action OrbClicked;

        /// <summary>玩家点了「离开」。上层拿它去结束这个事件节点。</summary>
        public event Action LeaveClicked;

        /// <summary>主浮层（两个空位那一层）。</summary>
        public WitchLayerView Layer
        {
            get { return _layer; }
        }

        /// <summary>卡池浏览层。</summary>
        public WitchPickerView Picker
        {
            get { return _picker; }
        }

        /// <summary>主浮层是不是开着。</summary>
        public bool IsLayerOpen
        {
            get { return _layer != null && _layer.IsOpen; }
        }

        /// <summary>浏览层是不是开着。</summary>
        public bool IsPickerOpen
        {
            get { return _picker != null && _picker.IsOpen; }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按序列化字段自己接（铁律 8）：
            //   构建器在编辑模式里 AddListener 的那一份不是序列化数据，存 Prefab / 场景时蒸发。
            //   症状是「一切看着正常、onClick 上 0 个监听器、点了毫无反应且零报错」。
            Wire(_orbButton, OnOrb);
            Wire(_leaveButton, OnLeave);

            if (_layer == null)
            {
                Debug.LogError("[WitchView] 没有接 WitchLayerView —— 先跑 `魔法乱斗/P6 · 构建 WitchWorkshop 场景`。");
            }

            if (_picker == null)
            {
                Debug.LogError("[WitchView] 没有接 WitchPickerView —— 先跑 `魔法乱斗/P6 · 构建 WitchWorkshop 场景`。");
            }
        }

        // ══════════════════════════════════════════════════════
        //  画
        // ══════════════════════════════════════════════════════

        /// <summary>设置场景标题（默认「女巫的工坊」）。</summary>
        public void SetTitle(string text)
        {
            if (_title != null)
            {
                _title.text = text;
            }
        }

        /// <summary>「离开」按钮的文字（默认「离开」）。</summary>
        public void SetLeaveLabel(string text)
        {
            if (_leaveLabel != null)
            {
                _leaveLabel.text = text;
            }
        }

        /// <summary>
        /// 设置引导文字内容，并把它恢复成正常色（上一次可能被
        /// <see cref="ShowHintAsWarning"/> 染成了警示色）。
        /// </summary>
        public void SetHint(string text)
        {
            if (_hintText != null)
            {
                _hintText.text = text;
                _hintText.color = UiTheme.WitchHintText;
            }
        }

        /// <summary>
        /// 引导文字的显隐。
        ///
        /// <para>口径：<b>浮层开着时不显示</b>（面板已经说明了一切），
        /// 关掉之后由上层决定要不要放回来 —— 节点结束了就不该再放回来，
        /// 否则会出现「已经结束了、屏幕上还写着『点击水晶球』」。</para>
        /// </summary>
        public void SetHintVisible(bool visible)
        {
            if (_hint != null && _hint.activeSelf != visible)
            {
                _hint.SetActive(visible);
            }
        }

        /// <summary>把引导文字临时换成一句警示（卡池不够时用），并让它显示出来。</summary>
        public void ShowHintAsWarning(string text, bool warn)
        {
            if (_hintText != null)
            {
                _hintText.text = text;
                _hintText.color = warn ? UiTheme.WitchStatusWarn : UiTheme.WitchHintText;
            }

            SetHintVisible(true);
        }

        /// <summary>
        /// 水晶球还能不能点。
        ///
        /// <para>本场景的口径是「一次特殊强化 = 一个事件节点」，
        /// 所以做完要把水晶球关掉 —— 否则玩家还能再点开一次浮层，
        /// 对着一个已经结束的节点继续操作（流程上说不通，而且零报错）。</para>
        /// </summary>
        public void SetOrbInteractable(bool interactable)
        {
            if (_orbButton != null)
            {
                _orbButton.interactable = interactable;
            }
        }

        /// <summary>打开主浮层（同时把引导文字收起来）。</summary>
        public void OpenLayer(string title, string status)
        {
            SetHintVisible(false);

            if (_layer != null)
            {
                _layer.Show(title, status);
            }
        }

        /// <summary>关掉两层浮层（幂等）。</summary>
        public void CloseAll()
        {
            if (_picker != null)
            {
                _picker.Hide();
            }

            if (_layer != null)
            {
                _layer.Hide();
            }
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private void OnOrb()
        {
            OrbClicked?.Invoke();
        }

        private void OnLeave()
        {
            LeaveClicked?.Invoke();
        }

        /// <summary>把按钮接到回调（幂等：删了再加，避免重复接）。</summary>
        private void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }
    }
}
