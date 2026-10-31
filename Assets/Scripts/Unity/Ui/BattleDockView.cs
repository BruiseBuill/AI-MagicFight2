using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 战斗界面右侧的动作区（2026-10-29）。
    ///
    /// <para><b>它解决什么</b>：以前「放弃防御」「不执行」是屏幕中央上方浮层里的按钮，
    /// 眼睛每次都要从手牌往画面上方跑一趟；而「进攻时不想再出牌」这件事<b>根本没有入口</b>
    /// （引擎硬性要求有牌必须打）。现在所有「结束 / 放弃 / 不选」都落在同一个物理位置：
    /// 形状不变，只换文案与颜色。</para>
    ///
    /// <para><b>它只回填选项，不做规则判断</b>（铁律 3）：<see cref="BattleUi"/> 把本拍
    /// 引擎给出的 <c>Skip</c> / <c>Done</c> 序号交给它，没有可交的选项时它自己隐藏 ——
    /// 所以永远不会出现「点了没反应」的死键。</para>
    ///
    /// <para><b>节点由 M11 构建器生成</b>（<c>ArtLayer/ActionDock</c>，推倒重建时一起重建），
    /// 不要手摆。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleDockView : MonoBehaviour
    {
        /// <summary>压在象牙白底板上的墨色字。</summary>
        private static readonly Color Ink = new Color(0.13f, 0.19f, 0.18f, 1f);

        /// <summary>「要付代价」那一档的墨红字（放弃防御 · −1）。</summary>
        private static readonly Color InkWarn = new Color(0.56f, 0.16f, 0.14f, 1f);

        /// <summary>危险态的底板染色（把象牙白压向暖红，不换图）。</summary>
        private static readonly Color PlateWarn = new Color(1f, 0.84f, 0.80f, 1f);

        [Header("构件（ArtLayer/ActionDock，M11 构建器生成）")]
        [SerializeField] private Button _button;
        [SerializeField] private Image _plate;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _sub;

        [Header("进攻刻度（还剩几次能出牌）")]
        [SerializeField] private Image[] _pips = new Image[0];

        /// <summary>按键被点。由 <see cref="BattleUi"/> 接。</summary>
        public event Action Clicked;

        private bool _visible;

        /// <summary>这个键此刻是不是摆着（探针 / 自测用）。</summary>
        public bool Visible
        {
            get { return _visible; }
        }

        public void Configure(Button button, Image plate, TMP_Text label, TMP_Text sub, Image[] pips)
        {
            _button = button;
            _plate = plate;
            _label = label;
            _sub = sub;
            _pips = pips ?? new Image[0];
            Hook();
        }

        /// <summary>
        /// 点击监听**必须在运行时再接一次**：构建器在编辑模式里接的 <c>UnityEvent</c> 监听器
        /// 不是序列化数据，存 Prefab / 存场景时会被直接丢掉（M34 那条「点齿轮没反应」，零报错）。
        /// 先 Remove 再 Add，所以与构建器留下的那一份不冲突（幂等）。
        /// </summary>
        private void Awake()
        {
            Hook();
        }

        private void Hook()
        {
            if (_button == null)
            {
                return;
            }

            _button.onClick.RemoveListener(OnClick);
            _button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (_visible && Clicked != null)
            {
                Clicked();
            }
        }

        /// <summary>
        /// 摆出 / 收起这个键，并刷新文案、代价提示与刻度。
        /// </summary>
        /// <param name="visible">本拍有没有「结束」这个动作可交。</param>
        /// <param name="label">主文案（结束进攻 / 放弃防御 / 不选 / 放弃）。</param>
        /// <param name="sub">副标（「−1」这种代价），空则不占位。</param>
        /// <param name="danger">要不要按「有代价」的样式画。</param>
        /// <param name="pips">亮着几枚刻度（剩余进攻次数，0 = 全暗）。</param>
        /// <param name="pipSlots">本拍摆几枚刻度（普通 1、连击追加 2、非进攻拍 0）。</param>
        public void Bind(bool visible, string label, string sub, bool danger, int pips, int pipSlots)
        {
            _visible = visible;

            UiPanelAnimator.SetVisible(gameObject, visible);

            if (!visible)
            {
                return;
            }

            if (_label != null)
            {
                _label.text = label ?? string.Empty;
                _label.color = danger ? InkWarn : Ink;
            }

            if (_sub != null)
            {
                bool showSub = !string.IsNullOrEmpty(sub);
                if (_sub.gameObject.activeSelf != showSub)
                {
                    _sub.gameObject.SetActive(showSub);
                }

                if (showSub)
                {
                    _sub.text = sub;
                    _sub.color = danger ? InkWarn : Ink;
                }
            }

            if (_plate != null)
            {
                _plate.color = danger ? PlateWarn : Color.white;
            }

            for (int i = 0; i < _pips.Length; i++)
            {
                Image pip = _pips[i];
                if (pip == null)
                {
                    continue;
                }

                bool show = i < pipSlots;
                if (pip.gameObject.activeSelf != show)
                {
                    pip.gameObject.SetActive(show);
                }

                if (!show)
                {
                    continue;
                }

                pip.color = i < pips ? UiTheme.AuraReady : UiTheme.HpEmpty;
            }
        }

        /// <summary>整块收起（对手回合 / 动画中 / 结算）。</summary>
        public void Hide()
        {
            Bind(false, null, null, false, 0, 0);
        }
    }
}
