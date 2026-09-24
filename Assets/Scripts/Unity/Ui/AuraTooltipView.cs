using TMPro;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 长按光环图标弹出的说明浮层（M15）。
    ///
    /// <para>三行：来源卡名 + 光环 · 效果原文 · 怎么用。
    /// <b>不是</b>复用 <see cref="CardDetailView"/> —— 那个展示的是整张卡面（396×550），
    /// 摆在画布顶上的图标下面会直接铺满半个屏幕；光环要的就是「这一条」。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuraTooltipView : MonoBehaviour
    {
        [SerializeField] private RectTransform _panel;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TMP_Text _hint;

        public void Configure(RectTransform panel, TMP_Text title, TMP_Text body, TMP_Text hint)
        {
            _panel = panel;
            _title = title;
            _body = body;
            _hint = hint;
            Hide();
        }

        /// <param name="anchor">浮层顶端中点（本视图父节点的局部坐标）。</param>
        public void Show(AuraIconData data, Vector2 anchor)
        {
            if (_panel == null)
            {
                return;
            }

            if (_title != null)
            {
                _title.text = data.SourceName + " · 光环";
            }

            if (_body != null)
            {
                _body.text = data.Describe();
            }

            if (_hint != null)
            {
                _hint.text = data.Usable
                    ? "把图标拖到自己的手牌区即可使用"
                    : data.Seat == MagicBrawl.Core.BattleState.SeatAi ? "敌方光环，仅供查看"
                    : "当前不可使用";
                _hint.color = data.Usable ? UiTheme.AuraReady : UiTheme.TextSecondary;
            }

            _panel.gameObject.SetActive(true);
            _panel.anchoredPosition = Clamp(anchor);
        }

        public void Hide()
        {
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
            }
        }

        /// <summary>贴边时把浮层拽回画面内（光环图标可以贴着左右边缘）。</summary>
        private static Vector2 Clamp(Vector2 anchor)
        {
            float halfW = UiLayout.BuffTipWidth * 0.5f + UiLayout.BuffTipScreenPadding;
            float limit = UiLayout.ReferenceWidth * 0.5f - halfW;

            anchor.x = Mathf.Clamp(anchor.x, -limit, limit);
            return anchor;
        }
    }
}
