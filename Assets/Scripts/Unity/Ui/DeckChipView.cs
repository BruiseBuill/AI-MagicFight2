using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>历史牌组里的一枚小牌</b>（2026-11-02）—— 元素符号 + 卡名，<b>不画卡面</b>。
    ///
    /// <para><b>为什么要它</b>：历史列表一条就是 8 张、面板里同时能看到十几条，
    /// 真卡面在这个尺寸下只剩一团色块。用户口径是「用一个符号加名称来表明卡牌」，
    /// 所以这里的牌 = 22×22 的元素色块（符号就压在色块上）+ 13px 卡名。</para>
    ///
    /// <para><b>符号用的是游戏内既有那套元素图</b>（<see cref="ElementIconLibrary"/>，
    /// 与卡面角标 / 怪物思考框同一批 <c>Resources/Elements</c>），不是另画一套 ——
    /// 同一张牌在三个地方露面必须是同一个符号，否则玩家要记两套。</para>
    ///
    /// <para>纯视图：<see cref="Bind"/> 只负责把 <see cref="CardDef"/> 画出来，
    /// 不判断任何「这张牌能不能用」。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckChipView : MonoBehaviour
    {
        /// <summary>元素色块的底（22×22）。</summary>
        [SerializeField] private Image _badge;

        /// <summary>压在色块上的元素符号。</summary>
        [SerializeField] private Image _icon;

        /// <summary>卡名。</summary>
        [SerializeField] private TMP_Text _label;

        /// <summary>现在绑的是哪张牌（探针用；空牌 = null）。</summary>
        public string BoundId { get; private set; }

        public void Bind(CardDef card)
        {
            if (card == null)
            {
                Clear();
                return;
            }

            BoundId = card.Id;

            Color accent = UiTheme.ElementAccent(card.Element);
            if (_badge != null)
            {
                // 底只要「一档淡色」，符号才是主角 —— 与设计稿 16% 一致
                _badge.color = new Color(accent.r, accent.g, accent.b, 0.16f);
            }

            if (_icon != null)
            {
                Sprite sprite = ElementIconLibrary.Get(card.Element);
                _icon.sprite = sprite;
                _icon.enabled = sprite != null;
                if (sprite != null)
                {
                    _icon.color = Color.white;
                }
            }

            if (_label != null)
            {
                _label.text = card.Name;
            }
        }

        public void Clear()
        {
            BoundId = null;
            if (_icon != null)
            {
                _icon.sprite = null;
                _icon.enabled = false;
            }

            if (_label != null)
            {
                _label.text = string.Empty;
            }
        }
    }
}
