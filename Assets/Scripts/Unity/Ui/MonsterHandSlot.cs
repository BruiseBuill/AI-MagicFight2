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

        /// <summary>
        /// 摆一张牌。
        ///
        /// <para><b>「已知 / 未知」由 <paramref name="isKnown"/> 明确告知，不从
        /// <paramref name="face"/> 是否为 null 反推</b>：卡面图来自 <c>CardArtLibrary</c>，
        /// 那本库由构建器生成、可能还没有这一批素材。若拿「找不到图」当成「玩家不知道」，
        /// 一张<b>明明看过</b>的牌会因为缺图被静默画成牌背 —— 那是把美术缺料伪装成情报缺失，
        /// 玩家会以为自己的记忆不对。所以两者分开：知不知道该亮，能不能亮是另一回事
        /// （缺图时退化成一块深色矩形，位置和数量仍然正确）。</para>
        /// </summary>
        /// <param name="isKnown">玩家是否知道这是哪张牌（由上层按 uid 判定）。</param>
        /// <param name="face">已知时的正面卡面；可能为 null（库还没这批次素材）。</param>
        /// <param name="backSprite">未知时的牌背图。</param>
        public void Bind(bool isKnown, Sprite face, Sprite backSprite)
        {
            IsUnknown = !isKnown;

            if (_face != null)
            {
                // 未知 → 牌背；已知 → 正面卡面（拿不到就退化成深色方块，但仍是「亮着」的那格）
                Sprite show = isKnown ? face : backSprite;
                _face.sprite = show;
                _face.color = show != null ? Color.white : UiTheme.MiniCardFace;
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
