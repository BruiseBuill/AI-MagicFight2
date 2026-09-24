using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「查看怪物手牌」面板（M28）。
    ///
    /// <para><b>它补的是哪个洞</b>：怪物右下角一直挂着「手牌 ×N」（M20），但它只回答
    /// 「几张」，不回答「哪几张」。而对局里玩家其实**积累**了一份情报 ——
    /// 怪物打出来的牌他看见了（打出即亮在头顶）、用雷云 / 狂躁蘑菇翻开的牌他也看见了。
    /// 这些信息散在各拍的演出里，玩家记不住也没地方回看。用户口径（2026-09-22）：
    /// <b>按住</b>那个标签，把怪物当前手牌全摆出来 —— 已知的正面朝上、未知的显示牌背，
    /// <b>松手即消失</b>。</para>
    ///
    /// <para><b>「已知」由上层判、本类只负责显示</b>：谁算已知是一条**情报规则**，
    /// 它的判据（打出过 / 看过）散在事件流里，只有 <see cref="BattleUi"/> 订阅得到。
    /// 所以本类的输入是「一次摆位的完整数据」（<see cref="Bind"/> 的 <c>known</c> 集合），
    /// 它自己不订阅任何事件、也不持有引擎引用 —— 与「表现层只读快照」那条铁律一致。</para>
    ///
    /// <para><b>为什么按「下标」而不是「牌」传已知性</b>：怪物手牌的顺序在引擎里是稳定的
    /// （补牌往末尾追加、打出才移除），但玩家看到的这份面板不必与引擎下标对齐 ——
    /// 面板只是「N 张牌，其中 M 张亮着」。用 <c>uid</c> 集合是唯一稳定的身份
    /// （同一张牌回手再打出来，<c>uid</c> 不变，记忆得以延续）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterHandView : MonoBehaviour
    {
        [Header("节点")]
        [Tooltip("整块浮层的 CanvasGroup（淡入淡出 + 挡下层输入）。")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("弹窗底板（九宫格图，运行时按张数只改高度）。")]
        [SerializeField] private RectTransform _panel;

        [Tooltip("标题（「查看对方手牌」）。")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("副标题（「已知 2 / 5」）。")]
        [SerializeField] private TMP_Text _subTitle;

        [Header("牌位（构建器预建 Slot0…Slot7，运行时只激活前 N 个）")]
        [SerializeField] private MonsterHandSlot[] _slots = new MonsterHandSlot[0];

        /// <summary>
        /// 牌背图的兜底来源（构建器写入 = `Peek_CardBack.png`）。
        ///
        /// <para>与 <see cref="PeekView"/> 同一套理由：正常路径走
        /// <see cref="BattleArtLibrary.Instance"/>，但那本库由 `ArtImportBuilder.RunAll()` 生成 ——
        /// 新素材还没进库时 <c>PeekCardBack</c> 是 null，未知牌会被刷成一块灰方块且零报错。
        /// 构建期直接从 <c>AssetDatabase</c> 取一份写进 prefab，就与建库顺序无关了。</para>
        /// </summary>
        [SerializeField] private Sprite _cardBackFallback;

        // ── 运行态 ───────────────────────────────────────────
        private bool _showing;
        private bool _closing;
        private Sprite _backSprite;

        private readonly List<MonsterHandSlot> _active = new List<MonsterHandSlot>(UiLayout.MonsterHandSlotCount);

        /// <summary>纯展示面板 —— 玩家点它没有任何后果，所以不吃射线（不拦任何手势）。</summary>
        public bool IsShowing
        {
            get { return _showing; }
        }

        /// <summary>当前摆出来的张数（供自测 / 调试读）。</summary>
        public int ShownCount
        {
            get { return _active.Count; }
        }

        // ══════════════════════════════════════════════════════
        //  对外
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 摆出怪物的全部手牌并淡入。
        ///
        /// <para><paramref name="hand"/> = 怪物当前手牌（顺序即引擎里的顺序）；
        /// <paramref name="known"/> = 其中「玩家已知」的那些牌的 <c>uid</c>。
        /// 已知的画正面卡面，其余画牌背 + 一层暗压。</para>
        ///
        /// <para><b>张数自适应</b>：1–4 张一行、5–8 张换两行（与 M25 查看手牌同一笔版式账，
        /// 面板只往纵向长、横向恒为原宽 992 —— 顶部那块凸起的标题牌位横向一拉就被抻开）。</para>
        /// </summary>
        public void Bind(IReadOnlyList<CardSnapshot> hand, ICollection<int> known)
        {
            if (_group == null || _panel == null || hand == null)
            {
                return;
            }

            _active.Clear();

            // ⚠ 不能写 SetSiblingIndex(i)：牌位的父节点（Panel）里前面还压着 Title / SubTitle，
            //   把它钉到下标 i 会把标题挤到牌位底下（M27 的 HandPickView 正是这么埋掉前三张牌的）。
            //   面板的子节点只有「标题类装饰 + 牌位」两组，装饰先建、牌位后建，
            //   所以这里的顺序天然正确 —— 一行不动，只是记下这个前提。
            for (int i = 0; i < hand.Count && _active.Count < _slots.Length; i++)
            {
                if (_slots[_active.Count] == null)
                {
                    continue;
                }

                _active.Add(_slots[_active.Count]);
            }

            int n = _active.Count;
            if (n == 0)
            {
                // 怪物手牌空了 —— 面板没有内容可摆，静默收起来（不弹一块空面板）
                HideImmediate();
                return;
            }

            bool wasShowing = _showing;
            _showing = true;
            _closing = false;

            Sprite back = BackSprite();
            int knownCount = 0;

            for (int i = 0; i < n; i++)
            {
                CardSnapshot card = hand[i];
                bool isKnown = known != null && known.Contains(card.Uid);

                if (isKnown)
                {
                    knownCount++;
                }

                // 已知 → 正面卡面；未知 → 牌背。**「知不知道」与「有没有图」分开传**
                //（见 MonsterHandSlot.Bind 的说明：缺图不能伪装成未知）。
                _active[i].Bind(isKnown, isKnown ? FaceSpriteFor(card.CardId) : null, back);
            }

            Layout(n);
            SetSubTitle(knownCount, n);

            if (_title != null)
            {
                _title.text = "查看对方手牌";
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();      // 压在所有视图之上

            if (_group != null)
            {
                _group.blocksRaycasts = true;
                _group.interactable = true;
                _group.alpha = 0f;
            }

            // ⚠ UiTween.Setup 返回 void —— 不能写成 .Setup(...).Play()，必须分两句。
            TweenCanvasAlpha fadeIn = FadeFor();
            fadeIn.Setup(0f, 1f, UiLayout.MonsterHandFadeSeconds, UiEaseKind.OutQuad);
            fadeIn.Play();

            // 面板弹出单独一支补间（它不参与任何状态机）
            TweenScale pop = PanelTween();
            pop.Setup(Vector3.one * UiLayout.MonsterHandPanelPopFrom, Vector3.one,
                UiLayout.MonsterHandPanelPopSeconds, UiEaseKind.OutBack);
            pop.Play();

            // 牌位依次浮进来。**只在「本来没显示」时播** —— 按住期间每次刷新都会重新 Bind，
            // 若每次都重播一遍入场动画，面板会一直抖（玩家看到的是「按住的这段一直在抽动」）。
            if (!wasShowing)
            {
                for (int i = 0; i < n; i++)
                {
                    TweenScale t = SlotTween(_active[i].gameObject);
                    t.Setup(Vector3.one * 0.86f, Vector3.one,
                        UiLayout.MonsterHandCardPopSeconds + i * UiLayout.MonsterHandCardPopStagger,
                        UiEaseKind.OutBack);
                    t.Play();
                }
            }
        }

        /// <summary>收起来（淡出）。已经在关 / 本来就没开 → 什么都不做。</summary>
        public void Hide()
        {
            if (!_showing || _closing)
            {
                return;
            }

            _closing = true;

            TweenCanvasAlpha fadeOut = FadeFor();
            fadeOut.Setup(_group == null ? 1f : _group.alpha, 0f,
                UiLayout.MonsterHandFadeSeconds, UiEaseKind.OutQuad);
            fadeOut.Play();
        }

        /// <summary>立刻收起来（重开一局 / 终局用，不要动画）。</summary>
        public void HideImmediate()
        {
            _showing = false;
            _closing = false;

            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }

            gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        //  版式
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 按张数摆位。面板只往纵向长 —— 理由与 <see cref="PeekView"/> 完全相同
        /// （顶部中央那块凸起的标题牌位在九宫格的「上中」格，横向一拉就被抻开）。
        /// </summary>
        private void Layout(int count)
        {
            int perRow = UiLayout.MonsterHandMaxPerRow;
            int rows = count <= perRow ? 1 : 2;

            // 两行时上行取多的那张（5→3+2、6→3+3、7→4+3、8→4+4）
            int topCount = rows == 1 ? count : (count + 1) / 2;

            float panelHeight = rows == 1
                ? UiLayout.MonsterHandPanelHeightOneRow
                : UiLayout.MonsterHandPanelHeightTwoRows;

            _panel.sizeDelta = new Vector2(UiLayout.MonsterHandPanelWidth, panelHeight);

            float contentTop = panelHeight * 0.5f - UiLayout.PeekContentTop;
            float contentBottom = -panelHeight * 0.5f + UiLayout.PeekContentBottom;
            float centerY = (contentTop + contentBottom) * 0.5f;
            float step = UiLayout.MonsterHandCardHeight + UiLayout.MonsterHandRowGap;

            int index = 0;

            for (int r = 0; r < rows; r++)
            {
                int inRow = rows == 1 ? count : (r == 0 ? topCount : count - topCount);
                if (inRow <= 0)
                {
                    continue;
                }

                float rowY = rows == 1 ? centerY : centerY + step * (r == 0 ? 0.5f : -0.5f);
                float totalW = inRow * UiLayout.MonsterHandCardWidth
                               + (inRow - 1) * UiLayout.MonsterHandCardGap;
                float firstX = -totalW * 0.5f + UiLayout.MonsterHandCardWidth * 0.5f;
                float pitch = UiLayout.MonsterHandCardWidth + UiLayout.MonsterHandCardGap;

                for (int c = 0; c < inRow; c++)
                {
                    RectTransform rt = (RectTransform)_active[index++].transform;
                    // ⚠ 必须显式激活：上一轮 Bind 张数更少时，这个牌位被下面那段停用过。
                    //   只把它放进 _active 而不打开，它会带着「已停用」的状态被摆位 ——
                    //   结果是「按住查看」有时只亮第一张（实测过：1 张 → 2 张的连续 Bind）。
                    _active[index - 1].gameObject.SetActive(true);
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(UiLayout.MonsterHandCardWidth, UiLayout.MonsterHandCardHeight);
                    rt.anchoredPosition = new Vector2(firstX + c * pitch, rowY);
                }
            }

            // 多出来的牌位停用。⚠ 基准是「本轮真正用到的张数」（_active.Count，最多 = _slots.Length），
            // 不是手牌总数 count —— 手牌上限 8 时两者恰好相等，但 count 一旦超过槽位数
            // （引擎改上限 / 预建槽变少），拿 count 当基准会把用到的槽也停掉。
            for (int i = _active.Count; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].gameObject.activeSelf)
                {
                    _slots[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 副标题「已知 2 / 5」。
        ///
        /// <para><b>为什么要写这个数</b>：面板上「亮着几张」虽然是自明的，但玩家需要一眼知道
        /// 「这份情报到底够不够」——「已知 5 / 5」意味着对面手牌完全透明，那是完全不同的决策处境。
        /// 写成「已知 N / M」而不是「N / M」，是为了不让人误读成「已选 N 张」（M27 那个计数是这个意思）。</para>
        /// </summary>
        private void SetSubTitle(int knownCount, int total)
        {
            if (_subTitle == null)
            {
                return;
            }

            _subTitle.text = "已知 " + knownCount + " / " + total;
            _subTitle.color = knownCount >= total ? UiTheme.MonsterHandKnown : UiTheme.TextSecondary;
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 牌背图：优先用美术库，库还没建到这一批时退回构建器写进来的那张。
        /// 结果缓存 —— 它在一次 Bind 里要被取 N 次。
        /// </summary>
        private Sprite BackSprite()
        {
            if (_backSprite != null)
            {
                return _backSprite;
            }

            BattleArtLibrary lib = BattleArtLibrary.Instance;
            if (lib != null && lib.PeekCardBack != null)
            {
                _backSprite = lib.PeekCardBack;
            }
            else
            {
                _backSprite = _cardBackFallback;
            }

            return _backSprite;
        }

        /// <summary>正面卡面：优先用烘焙好的整张卡面（与「长按看完整卡面」同一张图）。</summary>
        private static Sprite FaceSpriteFor(string cardId)
        {
            CardArtLibrary lib = CardArtLibrary.Instance;
            if (lib == null || string.IsNullOrEmpty(cardId))
            {
                return null;
            }

            Sprite art = lib.GetArt(cardId);
            return art != null ? art : lib.GetIllustration(cardId);
        }

        private TweenScale PanelTween()
        {
            TweenScale t = _panel.GetComponent<TweenScale>();
            if (t == null)
            {
                t = _panel.gameObject.AddComponent<TweenScale>();
            }

            return t;
        }

        private static TweenScale SlotTween(GameObject go)
        {
            TweenScale t = go.GetComponent<TweenScale>();
            if (t == null)
            {
                t = go.AddComponent<TweenScale>();
            }

            return t;
        }

        private TweenCanvasAlpha FadeFor()
        {
            TweenCanvasAlpha fade = GetComponent<TweenCanvasAlpha>();
            if (fade == null)
            {
                fade = gameObject.AddComponent<TweenCanvasAlpha>();
            }

            fade.Finished -= OnFadeFinished;
            fade.Finished += OnFadeFinished;
            return fade;
        }

        private void OnFadeFinished()
        {
            if (!_closing)
            {
                return;   // 淡入结束，什么都不用做
            }

            HideImmediate();
        }
    }
}
