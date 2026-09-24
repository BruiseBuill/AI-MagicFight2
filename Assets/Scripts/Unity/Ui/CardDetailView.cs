using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 长按一张牌时弹出的「放大查看」浮层。
    ///
    /// <para><b>M24 #3（2026-09-20 用户口径）起，这里放的是与手牌、冷却迷你卡
    /// <u>同一棵节点树</u>的组成式卡面</b>（<see cref="CardView"/>），只是尺寸更大。
    /// 旧版塞的是 `Art/Cards` 里那张 760×1056 的**成品卡面整图** —— 于是同一张牌
    /// 手牌上是一个样子、放大看又是另一个样子（成品图上的数值是画死的，光环加值、
    /// 基础冷却这些口子在图上根本改不了）。现在三处显示的信息、字体、配色完全一致，
    /// 差别只剩缩放，而缩放本来就只加在 `CardRoot` 一个节点上。</para>
    ///
    /// <para>卡面节点由 <c>BattleUiBuilder.BuildDetail</c> 在构建 prefab 时生成并回填，
    /// 本类只负责 <see cref="Show"/> / <see cref="Hide"/> 与绑定快照。</para>
    ///
    /// <para><b>⚠ 本类刻意没有 <c>Awake() { Hide(); }</c></b>：初始隐藏由构建器
    /// <c>SetActiveIfFound(topArea, "DetailPanel", false)</c> 在 prefab 里就落好。
    /// 如果这里再写一句 <c>Hide</c>，第一次 <see cref="Show"/> 会被它当场撤销 ——
    /// 因为 <c>Show</c> 先把面板置活，<b>置活那一刻才触发 <c>Awake</c></b>，
    /// 于是「刚打开就被自己关掉」，表现为<b>本局第一次长按不出牌、第二次起正常</b>，
    /// 且零报错。同类症状已经中过两次（<c>DropArrowView</c> 2026-09-17、
    /// <c>PlayedCardView</c> 2026-09-20）—— 旧版本这里正是这么写的，一并拆掉。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardDetailView : MonoBehaviour
    {
        [Header("浮层")]
        [SerializeField] private GameObject _panel;

        /// <summary>放大的组成式卡面（与手牌同款）。</summary>
        [SerializeField] private CardView _card;

        /// <summary>显示某张牌的放大卡面；传 default 快照则隐藏。</summary>
        public void Show(CardSnapshot card)
        {
            if (string.IsNullOrEmpty(card.CardId) || _panel == null)
            {
                Hide();
                return;
            }

            _panel.SetActive(true);

            if (_card != null)
            {
                // 手牌形态：与手牌上那张牌逐项一致（卡名 / 力量 / 基础冷却 / 效果文字）。
                _card.Bind(card, CardView.ViewMode.Hand, 0);
            }
        }

        public void Hide()
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        /// <summary>
        /// 把浮层收紧为牌面本身。
        ///
        /// <para><b>M18 起不再碰尺寸 / 锚点</b>：<c>TopArea/DetailPanel</c> 与它下面那棵
        /// 卡面的尺寸、锚点都已经摆在 <c>BattleCanvas.prefab</c> 里（见 <c>UiLayout.DetailFace*</c>），
        /// 之前每次 <see cref="Show"/> 都重设一遍，等于把「在 Prefab 里手调的结果」冲掉 ——
        /// 现在这个方法只负责<b>显隐与关掉多余的命中</b>。</para>
        ///
        /// <para>浮层不要接受点击：卡面节点自己不进交互（没有 Button / CardInteractor），
        /// 但底图 <c>DetailPanel</c> 的那张 Image 默认是 raycast 的，会挡住底下的手牌 —— 关掉它。</para>
        /// </summary>
        public void ConfigureFaceOnly()
        {
            if (_panel == null) return;

            var backdrop = _panel.GetComponent<Image>();
            if (backdrop != null) backdrop.enabled = false;

            // 只留卡面这一个子节点参与渲染（当前 DetailPanel 下也只有它）。
            for (int i = 0; i < _panel.transform.childCount; i++)
            {
                Transform child = _panel.transform.GetChild(i);
                if (_card != null && child == _card.transform) continue;
                child.gameObject.SetActive(false);
            }
        }
    }
}
