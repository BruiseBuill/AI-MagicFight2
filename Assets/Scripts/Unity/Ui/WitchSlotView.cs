using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊浮层里的<b>一个空位</b>（2026-10-01 · P6）。
    ///
    /// <para><b>空位有两种长相</b>：没选牌时是一块会呼吸的凹槽（半透明暗底 + 冷灰描边 +
    /// 中央一个「+」），选了牌之后把那张牌画进去、描边转暖金。
    /// 左边的空位还要多一层淡暖光 —— 它那张牌<b>会被吃掉</b>，
    /// 得让人一眼看出这两个位置不是对等的。</para>
    ///
    /// <para><b>职责边界</b>（与 <see cref="UpgradePickerCell"/> 同口径）：
    /// 本类<b>只画、只收点击</b>。<b>它不认识规则</b> —— 「哪张牌能当目标」是 Core 的
    /// <see cref="WitchWorkshop.CanBeTarget"/> 判的，上层把结论当参数传进来。
    /// 空位里选了什么牌也是上层的状态（本类只负责显示）。</para>
    ///
    /// <para><b>⚠ 底图与描边都走 <c>CardBox</c> / <c>CardBox_Line</c> 九宫格</b>
    /// （border 38，与卡面自己的底衬同一批图）—— 不新增美术。
    /// image.type 必须是 <c>Sliced</c>：空位是 230×292 的竖长条，
    /// 而那两张图是 160×160 的方图，不用九宫格会把圆角抻成椭圆且零报错。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchSlotView : MonoBehaviour
    {
        /// <summary>整格的透明命中区（**唯一**吃射线的东西）。</summary>
        [SerializeField] private Button _hit;

        /// <summary>空位底衬（<c>CardBox</c> 九宫格，暗色半透明）。</summary>
        [SerializeField] private Image _backdrop;

        /// <summary>空位描边（<c>CardBox_Line</c> 九宫格；未选 = 冷灰、已选 = 暖金）。</summary>
        [SerializeField] private Image _edge;

        /// <summary>没选牌时中央那个「+」。</summary>
        [SerializeField] private GameObject _plus;

        /// <summary>选中的那张牌的卡面（唯一那份 <c>CardView_Hand.prefab</c> 的实例）。</summary>
        [SerializeField] private CardView _card;

        /// <summary>「它会被吃掉」的那层暖光（只有左边空位才有）。</summary>
        [SerializeField] private GameObject _sacrificeTint;

        /// <summary>空位下方的角色标签（「献祭」/「强化目标」）。</summary>
        [SerializeField] private TMP_Text _label;

        /// <summary>被点了（参数是自己）。上层拿它去决定「这次是给哪个空位选牌」。</summary>
        public event Action<WitchSlotView> Clicked;

        /// <summary>这个空位是第几个（0 = 左 · 献祭，1 = 右 · 目标）。</summary>
        public int Index { get; private set; }

        /// <summary>空位里现在放着哪张牌（没选 = null）。</summary>
        public CardDef Card { get; private set; }

        /// <summary>这一格现在能不能点。</summary>
        public bool IsInteractable
        {
            get { return _hit != null && _hit.interactable; }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）：
            //   构建器在编辑模式里 AddListener 的那一份不是序列化数据，存 Prefab / 场景时蒸发，
            //   症状是「一切看着正常、onClick 上 0 个监听器、点了毫无反应且零报错」。
            if (_hit != null)
            {
                _hit.onClick.RemoveListener(OnHit);
                _hit.onClick.AddListener(OnHit);
            }
        }

        /// <summary>
        /// 把一格画出来。固定配置（序号 / 标签 / 要不要那层暖光）只在第一次需要设置，
        /// 所以拆成 <see cref="Configure"/> 与 <see cref="Bind"/> 两个方法 ——
        /// 前者由构建器与 <c>Awake</c> 各调一次，后者每次刷新调。
        /// </summary>
        public void Configure(int index, string label, bool sacrificeRole)
        {
            Index = index;
            if (_label != null)
            {
                _label.text = label;
            }

            if (_sacrificeTint != null && _sacrificeTint.activeSelf != sacrificeRole)
            {
                _sacrificeTint.SetActive(sacrificeRole);
            }
        }

        /// <summary>
        /// 换一张牌。传 null = 把这个空位清空。
        ///
        /// <para>描边颜色在这里切 —— 它是**逻辑态**（空 / 满）而不是「提示类颜色」，
        /// 两个色值都来自 <see cref="UiTheme"/>，运行时只做二选一。</para>
        /// </summary>
        public void Bind(CardDef def)
        {
            Card = def;
            bool has = def != null;

            if (_plus != null && _plus.activeSelf == has)
            {
                _plus.SetActive(!has);
            }

            if (_card != null && _card.gameObject.activeSelf != has)
            {
                _card.gameObject.SetActive(has);
            }

            if (_edge != null)
            {
                _edge.color = has ? UiTheme.WitchSlotEdgeFilled : UiTheme.WitchSlotEdgeEmpty;
            }

            if (!has || _card == null)
            {
                return;
            }

            // 卡面唯一来源 = 卡表定义；冷却取基础冷却（与手牌 / 货位 / 背包 / 选牌弹窗同一口径）。
            CardSnapshot snap = CardSnapshot.FromDef(def, 0, def.Cooldown);
            _card.Bind(snap, CardView.ViewMode.Hand, Index);

            // ⚠ 尺寸一律运行时重申（铁律 15）：编辑模式写进 Prefab 的 localScale 不落盘。
            _card.SetFaceWidth(UiLayout.WitchSlotCardFaceWidth);

            _card.SetInteractable(false);       // 卡面自己不接点击；命中区在整格上
            _card.SetSelected(false);
            _card.SetDimmed(false);
        }

        /// <summary>这一格还能不能点（节点结束 / 正在播动画时关掉）。</summary>
        public void SetInteractable(bool value)
        {
            if (_hit != null)
            {
                _hit.interactable = value;
            }
        }

        private void OnHit()
        {
            Clicked?.Invoke(this);
        }
    }
}
