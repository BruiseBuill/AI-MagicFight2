using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「查看对方手牌」弹窗里的<strong>一个牌位</strong>（M25）。
    ///
    /// <para>它就是一张<b>会翻面的卡</b>：正面朝下时显示牌背、被点开之后换成正面。
    /// 里外只有一张 <see cref="Image"/>（翻面靠「换 sprite + 横向缩放」实现，
    /// 不用两张图对叠 —— 那样在 scale.x 过 0 的一瞬会同时看到两张），
    /// 外加一枚底部的结果角标。</para>
    ///
    /// <para><b>为什么自己实现 <see cref="IPointerClickHandler"/> 而不用 <c>Button</c></b>：
    /// ① 省掉 <c>Selectable</c> 的「颜色是替换不是乘算」那套麻烦（底图会被刷成灰）；
    /// ② 点完就要立刻失效（这一拍只回答一次），直接关 <c>raycastTarget</c> 最干净 ——
    /// 不依赖有没有 EventSystem 的选中态。</para>
    ///
    /// <para><b>⚠ 为什么单独一个文件</b>：它本来和 <see cref="PeekView"/> 写在同一个
    /// <c>PeekView.cs</c> 里，构建器跑得通、字段也写进去了，但
    /// <c>SaveAsPrefabAssetAndConnect</c> 存盘时把 8 个牌位的 <c>m_Script</c> 写成了
    /// <c>{fileID: 0}</c> —— 重开即「The referenced script (Unknown) on this Behaviour is missing!」。
    /// 原因是 Unity 只认<b>与文件同名</b>的那个 MonoBehaviour 能挂到 GameObject 上
    /// （MonoScript 与类的对应靠文件名），同文件里的其它 MonoBehaviour 存进 prefab 会丢脚本引用。
    /// 要进 prefab 的 MonoBehaviour = 一个文件一个、文件名与类名一致。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PeekCardSlot : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image _face;
        [SerializeField] private GameObject _badge;
        [SerializeField] private TMP_Text _badgeText;

        /// <summary>被点了。参数是自己，方便上层直接看是哪一格。</summary>
        public event Action<PeekCardSlot> Clicked;

        /// <summary>回填给引擎的选项序号（<c>Option.Index</c>）。</summary>
        public int OptionIndex = -1;

        /// <summary>
        /// 这格对应「对手手牌里的第几张」（<c>Option.Value</c>）。
        /// 事件回填的 <c>SlotIndex</c> 拿它来对账，确认翻的是玩家点的那张。
        /// </summary>
        public int HandIndex = -1;

        public Image Face
        {
            get { return _face; }
        }

        public GameObject Badge
        {
            get { return _badge; }
        }

        public TMP_Text BadgeText
        {
            get { return _badgeText; }
        }

        /// <summary>还能不能点（= 底图吃不吃射线）。</summary>
        public bool Interactable
        {
            get { return _face != null && _face.raycastTarget; }
            set
            {
                if (_face != null)
                {
                    _face.raycastTarget = value;
                }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Clicked != null)
            {
                Clicked(this);
            }
        }
    }
}
