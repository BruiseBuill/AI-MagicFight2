using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「查看怪物手牌」面板里的<strong>一格牌位</strong>（M28）。
    ///
    /// <para><b>它比 M25 的牌位简单在哪</b>：<see cref="PeekCardSlot"/> 要承担「点一下 →
    /// 翻面 → 结算」三段节奏，所以自带 <c>Clicked</c> 事件与可交互开关；本类**只负责显示** ——
    /// 这块面板是「按住看一眼」的纯展示界面，玩家点了它没有任何后果（松手就关）。
    /// 不实现 <c>IPointerClickHandler</c>、不吃射线，也就不会拦掉任何手势。</para>
    ///
    /// <para><b>两张图决定这一格说什么</b>：牌背 = <c>Peek_CardBack.png</c>（未知），
    /// 正面 = 烘好的整张卡面（已知）。切换只改 <see cref="Image.sprite"/> + 一层暗压，
    /// 不换节点、不换位置 —— 这样「已知 3 张」的位置与「一共 5 张」的位置严格一一对应。</para>
    ///
    /// <para><b>⚠ 为什么单独一个文件</b>：要进 Prefab 的 MonoBehaviour = 一个文件一个、
    /// 文件名与类名一致。Unity 的 <c>MonoScript</c> ↔ 类对应靠文件名，
    /// 同文件里的第二个 MonoBehaviour 存进 Prefab 时 <c>m_Script</c> 会被写成
    /// <c>{fileID: 0}</c>（M25 的 <c>PeekCardSlot</c> 正是这么炸过 8 个牌位、且零报错）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterHandSlot : MonoBehaviour
    {
        [Tooltip("牌面（牌背 / 正面卡面共用这一张 Image，只换 sprite）。")]
        [SerializeField] private Image _face;

        [Tooltip("「未知」时压在上面的一层暗色（平时失活）。")]
        [SerializeField] private Image _dim;

        /// <summary>
        /// 「已知」时显示的<b>组成式卡面</b>（唯一那份 <c>CardView_Hand.prefab</c> 的实例），
        /// 默认失活 —— 2026-10-05 加。
        ///
        /// <para>与 <see cref="PeekCardSlot.CardFace"/> 同一件事：工程里只剩插画那一份美术，
        /// 直接换 <see cref="_face"/> 的 sprite 会只显示一张光图，没有卡名 / 力量 / 冷却 /
        /// 效果文字（用户 2026-10-05：「只显示插画，不显示卡面」）。</para>
        /// </summary>
        [SerializeField] private CardView _cardFace;

        /// <summary>这一格当前显示的是不是牌背（未知）。供自测对账。</summary>
        public bool IsUnknown { get; private set; }

        public Image Face
        {
            get { return _face; }
        }

        public Image Dim
        {
            get { return _dim; }
        }

        /// <summary>「已知」时显示的组成式卡面（见字段说明）。可能为 null（老 Prefab）。</summary>
        public CardView CardFace
        {
            get { return _cardFace; }
        }

        /// <summary>
        /// 摆一张牌。
        ///
        /// <para><b>「已知 / 未知」由 <paramref name="isKnown"/> 明确告知，不从
        /// <paramref name="face"/> 是否为 null 反推</b>：卡面图来自 <c>CardArtLibrary</c>，
        /// 那本库由构建器生成、可能还没有这一批素材。若拿「找不到图」当成「玩家不知道」，
        /// 一张<b>明明看过</b>的牌会因为缺图被静默画成牌背 —— 那是把美术缺料伪装成情报缺失，
        /// 玩家会以为自己的记忆不对。所以两者分开：知不知道该亮，能不能亮是另一回事
        /// （缺图时退化成一块深色矩形，位置和数量仍然正确）。</para>
        ///
        /// <para><b>2026-10-05 起「已知」走 <see cref="_cardFace"/></b>（组成式卡面）：
        /// 它自己会把插画 + 卡名 + 力量 + 冷却 + 效果文字一起画出来。
        /// <paramref name="card"/> 就是那一张牌的快照；<see cref="_face"/> 只留给牌背。</para>
        /// </summary>
        /// <param name="isKnown">玩家是否知道这是哪张牌（由上层按 uid 判定）。</param>
        /// <param name="card">已知时那张牌的快照（未知时用不到）。</param>
        /// <param name="backSprite">未知时的牌背图。</param>
        /// <param name="fallbackFace">
        /// 兜底正面图：<b>只有老的 Prefab 里没有卡面子节点时</b>才会用到
        /// （那时退回「插画塞 Image」的老行为）。跑过补丁之后恒为 null。
        /// </param>
        public void Bind(bool isKnown, CardSnapshot card, Sprite backSprite, Sprite fallbackFace = null)
        {
            IsUnknown = !isKnown;

            bool showCardFace = isKnown && _cardFace != null;

            if (_cardFace != null)
            {
                if (isKnown)
                {
                    _cardFace.Bind(card, CardView.ViewMode.Hand, 0);
                    // ⚠ 尺寸一律运行时重申（铁律 15）：构建器写进 Prefab 的缩放不随嵌套 Prefab 落盘。
                    _cardFace.SetFaceWidth(UiLayout.MonsterHandCardWidth);
                    _cardFace.SetReadOnly();      // 纯展示，不吃射线也不压暗
                }

                if (_cardFace.gameObject.activeSelf != showCardFace)
                {
                    _cardFace.gameObject.SetActive(showCardFace);
                }
            }

            if (_face != null)
            {
                // 这张 Image 服务两件事：未知时画牌背；已知但**没有卡面节点**时退回插画。
                Sprite show = isKnown ? fallbackFace : backSprite;
                _face.sprite = show;
                _face.color = show != null ? Color.white : UiTheme.MiniCardFace;
                _face.enabled = !showCardFace;
            }

            if (_dim != null)
            {
                // 暗压只在「未知」时亮 —— 牌背本身已经和正面卡面长得不一样，
                // 这一层是补强：一眼扫过去，「没信息的那几张」要明显暗下去。
                _dim.gameObject.SetActive(IsUnknown);
                if (IsUnknown)
                {
                    _dim.color = UiTheme.MonsterHandUnknownDim;
                }
            }
        }
    }
}
