using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 女巫工坊浏览层里<b>一条效果</b>（2026-10-02）：献祭牌有 &gt;1 条效果时，
    /// 玩家在这一屏里选「要转移哪一条」。
    ///
    /// <para><b>为什么单独一个组件而不是在 <see cref="WitchPickerView"/> 里直接管两个按钮</b>：
    /// 效果条数是<b>运行时</b>才知道的（单效果牌根本不进这一屏），而节点必须由构建器预建 ——
    /// 所以「模板 + 运行时克隆」这条既有的路（同 <see cref="UpgradePickerCell"/> /
    /// <c>WitchSlotView</c>）需要一个可克隆的小组件来承载「第几条 / 文案 / 点击」。
    /// 直接在视图里用闭包捕获循环变量也可以，但那种写法在 <c>for</c> 里极易写漏
    /// （C# 的循环变量捕获），而且克隆出来的节点没有稳定身份、排查时分不清点的是哪一条。</para>
    ///
    /// <para><b>职责边界</b>：<b>只画、只收点击</b>。「哪几条能选」是 Core 的
    /// <c>WitchWorkshop.TransferableEffects</c> 给的，本类不认识规则。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WitchEffectOptionView : MonoBehaviour
    {
        /// <summary>整条的命中区（**唯一**吃射线的东西）。</summary>
        [SerializeField] private Button _button;

        /// <summary>效果文案。</summary>
        [SerializeField] private TMP_Text _label;

        /// <summary>被点了（参数是自己）。</summary>
        public event Action<WitchEffectOptionView> Clicked;

        /// <summary>这一条是「第几条效果」（对应 <c>CardDef.Effects</c> 的下标）。</summary>
        public int Index { get; private set; }

        /// <summary>现在显示的文案（验收探针用）。</summary>
        public string Text
        {
            get { return _label == null ? string.Empty : _label.text; }
        }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时接（铁律 8）：编辑模式 AddListener 的那一份不落 Prefab。
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClick);
                _button.onClick.AddListener(OnClick);
            }
        }

        /// <summary>把这行画出来。</summary>
        public void Bind(int index, string text)
        {
            Index = index;
            if (_label != null)
            {
                _label.text = text ?? string.Empty;
            }
        }

        /// <summary>显示 / 隐藏（效果条数少于模板数时，多出来的要关掉）。</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        private void OnClick()
        {
            Clicked?.Invoke(this);
        }
    }
}
