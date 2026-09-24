using System;
using System.Collections;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「查看对方手牌」弹窗（M25）。
    ///
    /// <para><b>它为什么存在</b>：雷云（ad）与狂躁蘑菇（w）的第 2 个 α 效果是
    /// 「随机查看对方一张手牌，满足阈值则送入冷却」。这一拍引擎给的选项是
    /// <b>对手的每一张手牌</b>，但对玩家而言它们全是牌背 —— 于是需要一块专门的浮层，
    /// 把 N 张牌背摆出来让玩家点一张（用户 2026-09-21 口径）。</para>
    ///
    /// <para><b>三段节奏</b>（用户原话：点一张 → 翻面成正面 → 结算 → 界面关闭，动画要流畅）：</para>
    /// <list type="number">
    /// <item><b>点击</b>：立刻起翻面前半段（原面 → 侧薄，<see cref="UiLayout.PeekFlipOutSeconds"/>），
    /// 同时把选项序号抛给上层回填引擎 —— 翻面必须跟手，不能等引擎回话才开始。</item>
    /// <item><b>翻面</b>：引擎的 <c>HandRevealedEvent</c> 一到（约下一帧）就拿到正面卡面，
    /// 接上后半段（侧薄 → 正面，带一点回弹），再亮出「力量 N · 送入冷却 / 未送入冷却」角标。</item>
    /// <item><b>关闭</b>：停 <see cref="UiLayout.PeekResultHoldSeconds"/> 让玩家看清，然后淡出。
    /// 这段时间同时报给 <see cref="BattleDriver.BeatHold"/>，下一拍不会提前压上来。</item>
    /// </list>
    ///
    /// <para><b>从选项里只读序号，绝不读牌</b>：<see cref="Option"/> 上挂着真实的
    /// <c>CardInstance</c>，一旦拿去取卡名，玩家就能挑走「自己想看的那张」——
    /// 效果从「随机查看」变成「定向查看」。本类只碰 <c>Index</c> 与 <c>Value</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PeekView : MonoBehaviour
    {
        [Header("节点")]
        [Tooltip("整块浮层的 CanvasGroup（淡入淡出用它，同时负责挡住下层输入）。")]
        [SerializeField] private CanvasGroup _group;

        [Tooltip("弹窗底板（九宫格图，运行时按张数只改高度）。")]
        [SerializeField] private RectTransform _panel;

        [Tooltip("标题（「查看对方手牌」）。")]
        [SerializeField] private TMP_Text _title;

        [Header("牌位（M8 构建器预建 Slot0…Slot7，运行时只激活前 N 个）")]
        [SerializeField] private PeekCardSlot[] _slots = new PeekCardSlot[0];

        /// <summary>
        /// 牌背图的兜底来源（构建器写入 = `Peek_CardBack.png`）。
        ///
        /// <para><b>为什么要兜底</b>：正常路径是走 <see cref="BattleArtLibrary.Instance"/>，
        /// 但那本库是 `ArtImportBuilder.RunAll()` 生成的 —— 新素材导入流程还没跑过时，
        /// <c>PeekCardBack</c> 是 null，牌位就会被刷成一块灰方块（`UiTheme.MiniCardFace`），
        /// 而且**没有任何报错**（2026-09-21 实证：`BattleArtLibrary.asset` 停在 09:23，
        /// 素材 14:59 才切出来）。参考图是构建期从 <c>AssetDatabase</c> 直接取的，
        /// 用它兜底就与建库顺序无关了。</para>
        /// </summary>
        [SerializeField] private Sprite _cardBackFallback;

        /// <summary>玩家点了第几格 —— 参数是回填给引擎的选项序号。</summary>
        public event Action<int> CardPicked;

        // ── 运行态 ───────────────────────────────────────────
        private bool _showing;
        private bool _wired;
        private bool _locked;
        private bool _closing;
        private PeekCardSlot _picked;

        /// <summary>解析出来的牌背图（一次 Show 里要被取 N 次，缓存住）。见 <see cref="BackSprite"/>。</summary>
        private Sprite _backSprite;

        /// <summary>翻面前半段是否已经播完（等 <c>HandRevealedEvent</c> 到齐才接后半段）。</summary>
        private bool _flipOutDone;

        // 引擎回话之后填这里
        private bool _frontReady;
        private CardSnapshot _front;
        private bool _cooled;
        private int _frontHandIndex = -1;

        private Coroutine _holdRoutine;
        private TweenCanvasAlpha _fade;
        private readonly List<PeekCardSlot> _active = new List<PeekCardSlot>(8);

        /// <summary>浮层现在是否开着（上层用它判断 <c>HandRevealedEvent</c> 该不该交给这里演）。</summary>
        public bool IsShowing
        {
            get { return _showing; }
        }

        // ══════════════════════════════════════════════════════
        //  对外
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 摆出这一拍的全部牌背并淡入。
        ///
        /// <para>牌数由<b>选项条数</b>决定（引擎给的选项就是对手当时的手牌，一条一张），
        /// 面板与布局按张数自适应：1–4 张一行，5–8 张换两行。</para>
        /// </summary>
        public void Show(IReadOnlyList<Option> options)
        {
            if (_group == null || _panel == null || options == null)
            {
                return;
            }

            Wire();
            StopHold();

            _showing = true;
            _locked = false;
            _closing = false;
            _picked = null;
            _frontReady = false;
            _frontHandIndex = -1;
            _flipOutDone = false;
            _active.Clear();

            // 选项里可能混 Skip（当前引擎不会给，但别把它当成一张牌画出来）
            for (int i = 0; i < options.Count && _active.Count < _slots.Length; i++)
            {
                Option o = options[i];
                if (o == null || o.IsSkip || _slots[_active.Count] == null)
                {
                    continue;
                }

                PeekCardSlot slot = _slots[_active.Count];
                slot.OptionIndex = o.Index;
                slot.HandIndex = o.Value;          // 只读序号，不碰 o.Card（见类注释）
                slot.transform.SetSiblingIndex(_active.Count);
                _active.Add(slot);
            }

            int n = _active.Count;
            if (n == 0)
            {
                _showing = false;
                return;
            }

            Layout(n);
            ResetSlots(n);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();      // 压在所有视图之上（含铺满画布的准备标记条）

            if (_group != null)
            {
                _group.blocksRaycasts = true;
                _group.interactable = true;
                _group.alpha = 0f;
            }

            // ⚠ UiTween.Setup 返回 void —— 不能写成 .Setup(...).Play()，必须分两句。
            TweenCanvasAlpha fadeIn = FadeFor();
            fadeIn.Setup(0f, 1f, UiLayout.PeekFadeSeconds, UiEaseKind.OutQuad);
            fadeIn.Play();

            // ⚠ 面板的弹出**不能**共用牌位那支补间：牌位那支把「结束」接到了翻面状态机上，
            //   而面板弹出只要 0.22 s —— 玩家手快时弹出先结束，会把翻面的前半段误判成已完成。
            TweenScale pop = PanelTween();
            pop.Setup(Vector3.one * UiLayout.PeekPanelPopFrom, Vector3.one,
                UiLayout.PeekPanelPopSeconds, UiEaseKind.OutBack);
            pop.Play();
        }

        /// <summary>
        /// 引擎回话了：把正面卡面接到被点的那一格上，播后半段翻面。
        ///
        /// <para>翻面前半段若还在播，这里只把数据存下 —— 由前半段的结束时回调接着走，
        /// 所以「点得快 / 引擎回得快」都不会把翻面截断成两半。</para>
        /// </summary>
        public void Reveal(CardSnapshot card, bool cooled, int handIndex)
        {
            if (!_showing)
            {
                return;
            }

            _front = card;
            _cooled = cooled;
            _frontHandIndex = handIndex;
            _frontReady = true;
            TryFlipIn();
        }

        /// <summary>收起来（淡出）。已经在关 / 本来就没开 → 什么都不做。</summary>
        public void Hide()
        {
            if (!_showing)
            {
                return;
            }

            if (_closing)
            {
                return;
            }

            _closing = true;
            StopHold();

            TweenCanvasAlpha fadeOut = FadeFor();
            fadeOut.Setup(_group == null ? 1f : _group.alpha, 0f,
                UiLayout.PeekFadeSeconds, UiEaseKind.OutQuad);
            fadeOut.Play();
        }

        /// <summary>立刻收起来（重开一局时用，不要动画）。</summary>
        public void HideImmediate()
        {
            StopHold();
            _showing = false;
            _closing = false;
            _locked = false;
            _picked = null;
            _frontReady = false;

            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        //  布局
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 按张数摆位。
        ///
        /// <para><b>面板只往纵向长</b>：宽度恒为原图的 992（顶部中央那块凸起的标题牌位
        /// 在九宫格的「上中」格里，横向一拉就被抻开）。所以 4 张一行放不下时不是把面板拉宽，
        /// 而是换第二行 —— 九宫格把中间那条纯色躯干纵向拉伸，肉眼零差别
        /// （推导见 <see cref="UiLayout.PeekPanelHeightTwoRows"/>）。</para>
        /// </summary>
        private void Layout(int count)
        {
            int perRow = UiLayout.PeekMaxPerRow;
            int rows = count <= perRow ? 1 : 2;

            // 两行时上行取多的一张（5→3+2、6→3+3、7→4+3、8→4+4）
            int topCount = rows == 1 ? count : (count + 1) / 2;

            float panelHeight = rows == 1
                ? UiLayout.PeekPanelHeightOneRow
                : UiLayout.PeekPanelHeightTwoRows;

            _panel.sizeDelta = new Vector2(UiLayout.PeekPanelWidth, panelHeight);

            float contentTop = panelHeight * 0.5f - UiLayout.PeekContentTop;
            float contentBottom = -panelHeight * 0.5f + UiLayout.PeekContentBottom;
            float centerY = (contentTop + contentBottom) * 0.5f;
            float step = UiLayout.PeekCardHeight + UiLayout.PeekRowGap;

            int index = 0;

            for (int r = 0; r < rows; r++)
            {
                int inRow = rows == 1 ? count : (r == 0 ? topCount : count - topCount);
                if (inRow <= 0)
                {
                    continue;
                }

                float rowY = rows == 1 ? centerY : centerY + step * (r == 0 ? 0.5f : -0.5f);
                float totalW = inRow * UiLayout.PeekCardWidth + (inRow - 1) * UiLayout.PeekCardGap;
                float firstX = -totalW * 0.5f + UiLayout.PeekCardWidth * 0.5f;
                float pitch = UiLayout.PeekCardWidth + UiLayout.PeekCardGap;

                for (int c = 0; c < inRow; c++)
                {
                    RectTransform rt = (RectTransform)_active[index++].transform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(UiLayout.PeekCardWidth, UiLayout.PeekCardHeight);
                    rt.anchoredPosition = new Vector2(firstX + c * pitch, rowY);
                }
            }
        }

        /// <summary>把前 N 个牌位复位成「牌背朝上、可点」，其余停用。</summary>
        private void ResetSlots(int count)
        {
            Sprite back = BackSprite();

            for (int i = 0; i < _slots.Length; i++)
            {
                PeekCardSlot slot = _slots[i];
                if (slot == null)
                {
                    continue;
                }

                bool used = i < count;
                slot.gameObject.SetActive(used);
                if (!used)
                {
                    continue;
                }

                slot.transform.localScale = Vector3.one;
                slot.Interactable = true;

                if (slot.Face != null)
                {
                    slot.Face.sprite = back;
                    // 图还是没有时退化成一块深色方块 —— 至少位置和数量是对的
                    slot.Face.color = back != null ? Color.white : UiTheme.MiniCardFace;
                }

                if (slot.Badge != null)
                {
                    slot.Badge.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 牌背图：优先用美术库，库还没建到这一批时退回构建器写进来的那张（见
        /// <see cref="_cardBackFallback"/> 的说明）。结果缓存 —— 它在一次 Show 里要被取 N 次。
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

        // ══════════════════════════════════════════════════════
        //  交互 / 翻面
        // ══════════════════════════════════════════════════════

        private void Wire()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i].Clicked += OnSlotClicked;
                }
            }
        }

        private void OnSlotClicked(PeekCardSlot slot)
        {
            if (!_showing || _locked || _closing || slot == null)
            {
                return;
            }

            _locked = true;
            _picked = slot;

            // 这一拍只回答一次：其余牌位立刻不可点
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i].Interactable = false;
                }
            }

            // 翻面必须在点击的同一帧起 —— 引擎回话（下一帧）才动就明显不跟手了
            _flipOutDone = false;
            TweenScale flipOut = TweenFor(slot.gameObject);
            flipOut.Setup(Vector3.one, new Vector3(0f, 1f, 1f),
                UiLayout.PeekFlipOutSeconds, UiEaseKind.OutQuad);
            flipOut.Play();

            if (CardPicked != null)
            {
                CardPicked(slot.OptionIndex);
            }
        }

        /// <summary>前半段播完（或者本来就没在播）就接后半段。</summary>
        private void TryFlipIn()
        {
            if (!_frontReady || _picked == null || _closing)
            {
                return;
            }

            if (!_flipOutDone)
            {
                // 前半段还在播 —— 它的结束时回调会再叫一次本方法
                return;
            }

            if (_frontHandIndex >= 0 && _picked.HandIndex >= 0 && _frontHandIndex != _picked.HandIndex)
            {
                // 引擎翻的不是我点的那张 —— 版面上仍然是「我点的那一格」翻面（不静默改版式），
                // 但这件事本身就是缺陷，留一条日志。
                Debug.LogWarning("[Peek] 引擎回填的是第 " + _frontHandIndex + " 张，玩家点的是第 "
                                 + _picked.HandIndex + " 张");
            }

            PeekCardSlot slot = _picked;

            if (slot.Face != null)
            {
                Sprite face = FaceSpriteFor(_front.CardId);
                if (face != null)
                {
                    slot.Face.sprite = face;
                    slot.Face.color = Color.white;
                }
            }

            if (slot.BadgeText != null)
            {
                slot.BadgeText.text = "力量 " + _front.PowerText
                                      + (_cooled ? " · 送入冷却" : " · 未送入冷却");
                slot.BadgeText.color = _cooled ? UiTheme.PeekCooled : UiTheme.PeekKept;
            }

            // 角标留到翻完再亮 —— 不然它会跟着一起被横向压扁
            if (slot.Badge != null)
            {
                slot.Badge.SetActive(false);
            }

            TweenScale flipIn = TweenFor(slot.gameObject);
            flipIn.Setup(new Vector3(0f, 1f, 1f), Vector3.one,
                UiLayout.PeekFlipInSeconds, UiEaseKind.OutBack);
            flipIn.Play();
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

        private void OnFlipTweenFinished()
        {
            PeekCardSlot slot = _picked;

            if (slot == null)
            {
                return;
            }

            if (!_flipOutDone)
            {
                // 前半段刚结束：如果引擎已经回话就直接接上，否则等 Reveal 来叫
                _flipOutDone = true;
                TryFlipIn();
                return;
            }

            // 后半段结束：亮角标 → 停一会儿 → 收
            if (slot.Badge != null)
            {
                slot.Badge.SetActive(true);
            }

            StopHold();
            _holdRoutine = StartCoroutine(CoAutoClose());
        }

        private IEnumerator CoAutoClose()
        {
            yield return new WaitForSecondsRealtime(UiLayout.PeekResultHoldSeconds);
            _holdRoutine = null;
            Hide();
        }

        private void StopHold()
        {
            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
                _holdRoutine = null;
            }
        }

        private void OnDisable()
        {
            StopHold();
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 取某个牌位上的缩放补间，顺手把「结束时」接到翻面状态机上。
        ///
        /// <para>同一个牌位的两段翻面共用它：真正的分流靠 <see cref="_flipOutDone"/> 那个状态位，
        /// 而不是「这是第几次回调」—— 因为后半段起播时前半段必然已经置位。</para>
        /// </summary>
        private TweenScale TweenFor(GameObject go)
        {
            TweenScale t = go.GetComponent<TweenScale>();
            if (t == null)
            {
                t = go.AddComponent<TweenScale>();
            }

            t.Finished -= OnFlipTweenFinished;
            t.Finished += OnFlipTweenFinished;
            return t;
        }

        /// <summary>面板自己的缩放补间 —— 只做「弹一下」，不参与翻面状态机。</summary>
        private TweenScale PanelTween()
        {
            TweenScale t = _panel.GetComponent<TweenScale>();
            if (t == null)
            {
                t = _panel.gameObject.AddComponent<TweenScale>();
            }

            return t;
        }

        private TweenCanvasAlpha FadeFor()
        {
            if (_fade == null)
            {
                _fade = GetComponent<TweenCanvasAlpha>();
                if (_fade == null)
                {
                    _fade = gameObject.AddComponent<TweenCanvasAlpha>();
                }
            }

            _fade.Finished -= OnFadeFinished;
            _fade.Finished += OnFadeFinished;
            return _fade;
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
