using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 强化选牌弹窗里的<b>一格</b>（2026-09-30）。
    ///
    /// <para><b>一个格子里有什么</b>：一张卡面（唯一那份 <c>CardView_Hand.prefab</c> 的实例）+
    /// 一层整格的透明命中区 + 一层「不可强化」压暗纱（含原因文字）。</para>
    ///
    /// <para><b>⚠ 为什么点选自己实现 <see cref="Button"/> 而不是让卡面自己接</b>：
    /// 卡面（<see cref="CardView"/>）自带 Button 与 <c>CardInteractor</c>，
    /// 在列表里必须全部关掉（否则① 卡面会吃掉滚动的拖拽 → 列表拖不动；
    /// ② 一次点击冒泡两遍）。所以点击统一由本类的命中区接 ——
    /// 与 <see cref="ShopSlotView"/> 同一套做法。</para>
    ///
    /// <para><b>⚠ 选中态复用卡面自己的 <c>Glow</c></b>：<see cref="CardView.SetSelected"/>
    /// 只切那个节点的 active，不写颜色 —— 那份描边本来就是暖金
    /// （用户 2026-09-30 口径「统一暖金」），所以本场景不需要另画一层描边，
    /// 也不必去动那份被手调过的 <c>CardView_Hand.prefab</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradePickerCell : MonoBehaviour
    {
        /// <summary>卡面（子节点，来自 <c>CardView_Hand.prefab</c>）。</summary>
        [SerializeField] private CardView _card;

        /// <summary>整格的透明命中区（**唯一**吃射线的东西）。</summary>
        [SerializeField] private Button _hit;

        /// <summary>「不可强化」压暗纱（默认失活，盖在卡面上）。</summary>
        [SerializeField] private GameObject _blocked;

        /// <summary>压暗纱上的原因文字。</summary>
        [SerializeField] private TMP_Text _reason;

        /// <summary>被点了（参数是自己）。只有可强化的格子才会冒泡。</summary>
        public event Action<UpgradePickerCell> Clicked;

        /// <summary>这一格当前对应的卡（空位 = null）。</summary>
        public CardDef Card { get; private set; }

        /// <summary>这一格在卡池里的序号。</summary>
        public int Index { get; private set; }

        /// <summary>这张牌能不能被强化（<see cref="CardUpgrade.CanUpgrade"/> 的结果，由上层给）。</summary>
        public bool Upgradable { get; private set; }

        private void Awake()
        {
            // ⚠ 监听器一律在运行时接（铁律 8）：构建器在编辑模式里 AddListener 的那一份
            //   不是序列化数据，存 Prefab / 场景时蒸发 —— 症状是「一切正常、点了毫无反应、零报错」。
            if (_hit != null)
            {
                _hit.onClick.RemoveListener(OnHit);
                _hit.onClick.AddListener(OnHit);
            }
        }

        /// <summary>整格显示 / 隐藏（卡池张数少于模板数时，多出来的要关掉）。</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 把一格画出来。
        ///
        /// <para><paramref name="reason"/> 只在 <paramref name="upgradable"/> 为 false 时显示 ——
        /// 它是「为什么这张不能点」，由 Core 的 <see cref="CardUpgrade.CanUpgrade"/> 给出，
        /// 本类不自己判断（规则只收敛在一处）。</para>
        /// </summary>
        public void Bind(int index, CardDef def, bool upgradable, string reason, int ownerSeat)
        {
            Index = index;
            Card = def;
            Upgradable = upgradable;

            bool has = def != null;

            if (_card != null)
            {
                _card.gameObject.SetActive(has);
            }

            if (_blocked != null)
            {
                _blocked.SetActive(has && !upgradable);
            }

            if (_reason != null)
            {
                _reason.text = has && !upgradable && !string.IsNullOrEmpty(reason) ? reason : string.Empty;
            }

            if (_hit != null)
            {
                _hit.interactable = has && upgradable;
            }

            if (!has || _card == null)
            {
                return;
            }

            // 卡面唯一来源 = 卡表定义；冷却取基础冷却（与手牌 / 货位 / 背包同一口径）。
            CardSnapshot snap = CardSnapshot.FromDef(def, ownerSeat, def.Cooldown);
            _card.Bind(snap, CardView.ViewMode.Hand, index);

            // ⚠ 尺寸一律运行时重申（铁律 15）：编辑模式写进 Prefab 的那份 localScale 不落盘。
            _card.SetFaceWidth(UiLayout.ShopBagCardFaceWidth);

            _card.SetInteractable(false);       // 卡面自己不接点击；不压暗（压暗由 _blocked 负责）
            _card.SetSelected(false);
            _card.SetDimmed(false);
        }

        /// <summary>选中态（复用卡面自带的暖金描边）。</summary>
        public void SetSelected(bool selected)
        {
            if (_card != null)
            {
                _card.SetSelected(selected);
            }
        }

        private void OnHit()
        {
            if (Card == null || !Upgradable)
            {
                return;                         // 不可强化的格子点了不冒泡（原因已经写在卡面上）
            }

            Clicked?.Invoke(this);
        }
    }
}
