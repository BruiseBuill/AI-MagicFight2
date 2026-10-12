using System;
using System.Collections;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>战斗胜利的结算界面</b>（2026-10-04 · 参考 <c>Assets/Art/Reference-5.png</c>；
    /// 2026-10-05 补上进场动画与「选中的那张亮起、其余退场」的收尾）。
    ///
    /// <para><b>它管什么</b>：冒险里打赢一局之后铺上来的那块全屏界面 ——
    /// 顶部徽记 + 「战斗胜利」横幅 · 「获得奖励」分隔线 · <b>三张奖励卡</b> ·
    /// 金币「+ 20」 · 底部「继续」。玩家点一张牌选中（再点一下取消），点「继续」走人。</para>
    ///
    /// <para><b>它不管什么</b>：奖励怎么落盘（加牌进卡池 / 加金币）不在这里 ——
    /// 那颗「继续」只把 <see cref="ContinueClicked"/> 连同「选了第几张」报出去，
    /// 由 <see cref="BattleUi"/> 决定写到哪个存档、加多少金币、接下来切哪个场景。
    /// 这条分工与 <c>BattleUi</c> 一贯的「UI 不碰规则」一致。
    /// <b>同样地，三张候选牌是「哪三张」也是外面算好传进来的</b>（<see cref="BattleUi.ShowResultWin"/>
    /// 走 <c>CardAcquisition</c> 抽），本类不知道概率这回事。</para>
    ///
    /// <para><b>节点由构建器建</b>（<c>BattleUiBuilder.BuildResultWin</c>）：整块 <c>ResultWinRoot</c>
    /// 连同三张卡位、金币行、继续键都是那趟构建出来的，本类只做「绑卡 / 选中 / 收工」。
    /// 版式常量全在 <c>UiLayout.ResultWin*</c>。</para>
    ///
    /// <para><b>动画（2026-10-05）</b>：三张卡是<b>一张一张弹出来</b>的 —— 每张从
    /// <c>UiLayout.ResultWinCardEnterScale</c> 放大到 1（OutBack 回弹）并淡入，相邻两张错开
    /// <c>ResultWinCardStagger</c> 秒；选中时那一张打一记「落定」脉冲；点「继续」时选中的那张
    /// 再亮一下、其余淡出，走完 <c>ResultWinCloseSeconds</c> 才真的报事件。
    /// <b>动画挂在「卡位（Slot）」上的 <c>TweenScale</c> / <c>TweenCanvasAlpha</c></b>
    /// —— 由 M8 构建器挂、本类按名字取；取不到就整个退化成「没有动画」（不会报错、
    /// 也不会把界面弄坏）。卡位上的缩放与卡自身的 <c>SetFaceWidth</c>（改的是 CardRoot）
    /// 井水不犯河水。</para>
    ///
    /// <para><b>⚠ 与旧 <c>ResultRoot</c> 的关系</b>：两块是并存的不同界面 ——
    /// 旧那块（标题 + 副标题 + 一个键）只作<b>没有新失败界面时的兜底</b>；
    /// 现在失败 / 平局走 <see cref="ResultLoseView"/>，胜利走这一块。
    /// 见 <c>BattleUi.HandleFinished</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResultWinView : MonoBehaviour
    {
        [Header("接线（由 BattleUiBuilder 写入）")]
        [Tooltip("整块界面（含底图 / 暗纱）。开关就是切它的 activeSelf。")]
        [SerializeField] private GameObject _root;

        [Tooltip("三个卡位上的卡面（顺序 = 从左到右）。")]
        [SerializeField] private CardView[] _cards = new CardView[0];

        [Tooltip("「+ 20」那个数字。")]
        [SerializeField] private TMP_Text _goldText;

        [Tooltip("底部「继续」键。")]
        [SerializeField] private Button _continueButton;

        /// <summary>
        /// 玩家点了「继续」。参数 = 选中的卡位下标，<b>−1 = 一张都没选</b>（跳过奖励）。
        /// </summary>
        public event Action<int> ContinueClicked;

        /// <summary>本局三张候选的卡 ID（与 <see cref="_cards"/> 对齐；空串 = 该位没有牌）。</summary>
        private readonly string[] _candidateIds = new string[UiLayout.ResultWinCardCount];

        /// <summary>三张卡的基准位置（第一次铺开时记一次 —— 抬升只在此基础上加）。</summary>
        private readonly Vector2[] _homePos = new Vector2[UiLayout.ResultWinCardCount];

        /// <summary>三个卡位上的进场补间（构建器挂；没有就是 null = 不做动画）。</summary>
        private readonly TweenScale[] _slotScale = new TweenScale[UiLayout.ResultWinCardCount];
        private readonly TweenCanvasAlpha[] _slotAlpha = new TweenCanvasAlpha[UiLayout.ResultWinCardCount];

        private bool _homeRecorded;
        private bool _tweensBound;
        private int _selected = -1;
        private bool _open;
        private Coroutine _closing;

        /// <summary>这块界面此刻铺着没有。</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>当前选中的卡位；−1 = 没选。</summary>
        public int SelectedIndex
        {
            get { return _selected; }
        }

        /// <summary>当前选中那张牌的卡 ID；没选返回 <c>null</c>。</summary>
        public string SelectedCardId
        {
            get
            {
                if (_selected < 0 || _selected >= _candidateIds.Length)
                {
                    return null;
                }

                string id = _candidateIds[_selected];
                return string.IsNullOrEmpty(id) ? null : id;
            }
        }

        /// <summary>构建器的接线口（与工程里其它 View 一致：字段私有 + Configure 写入）。</summary>
        public void Configure(GameObject root, CardView[] cards, TMP_Text goldText, Button continueButton)
        {
            _root = root;
            _cards = cards ?? new CardView[0];
            _goldText = goldText;
            _continueButton = continueButton;
        }

        private void Awake()
        {
            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(OnContinueClicked);
            }

            // 卡面的点击来自 CardView 自己的 Button —— 订阅它，别在卡上再叠一层 Button
            //（CardView_Hand 的 Button 就长在卡面上，多一个会打架）。
            if (_cards != null)
            {
                for (int i = 0; i < _cards.Length; i++)
                {
                    if (_cards[i] != null)
                    {
                        _cards[i].Clicked += OnCardClicked;
                    }
                }
            }

            BindTweens();
        }

        private void OnDestroy()
        {
            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveListener(OnContinueClicked);
            }

            if (_cards != null)
            {
                for (int i = 0; i < _cards.Length; i++)
                {
                    if (_cards[i] != null)
                    {
                        _cards[i].Clicked -= OnCardClicked;
                    }
                }
            }
        }

        /// <summary>
        /// 把三个卡位上的补间组件捞出来；<b>没有就当场补一个</b>（惰性、幂等）。
        ///
        /// <para><b>为什么是运行时 AddComponent 而不是构建器挂</b>：<c>TweenScale</c> /
        /// <c>TweenCanvasAlpha</c> 与它们的基类 <c>UiTween</c> 写在同一个文件里，
        /// 而「进 Prefab 的 MonoBehaviour 必须一文件一类」—— 构建器挂上去存 Prefab 时
        /// <c>m_Script</c> 会被写成 <c>{fileID: 0}</c>（2026-10-05 实测踩过：3 个卡位 ×
        /// 2 个补间 = 6 个空脚本组件）。本项目其余视图（PeekView / MonsterHandView /
        /// HandPickView / HudBuffView / MapView）也都是运行时补 —— 照它们办。</para>
        ///
        /// <para>这块界面是「构建器置失活 + 运行时 Show」那一类，所以 <c>Awake</c>
        ///（第一次被激活）跑一次就够；运行时的 AddComponent <b>不会</b>被写进 Prefab。</para>
        /// </summary>
        private void BindTweens()
        {
            if (_tweensBound || _cards == null)
            {
                return;
            }

            _tweensBound = true;
            for (int i = 0; i < _cards.Length && i < _slotScale.Length; i++)
            {
                CardView card = _cards[i];
                if (card == null)
                {
                    continue;
                }

                // 动画挂在**卡位**上（卡面自己那份缩放被 SetFaceWidth 占着 CardRoot，
                // 动卡面根节点的 localScale 会与它打架）。
                Transform slot = card.transform.parent;
                if (slot == null)
                {
                    continue;
                }

                if (slot.GetComponent<CanvasGroup>() == null)
                {
                    slot.gameObject.AddComponent<CanvasGroup>();
                }

                _slotScale[i] = slot.GetComponent<TweenScale>();
                if (_slotScale[i] == null)
                {
                    _slotScale[i] = slot.gameObject.AddComponent<TweenScale>();
                }

                _slotAlpha[i] = slot.GetComponent<TweenCanvasAlpha>();
                if (_slotAlpha[i] == null)
                {
                    _slotAlpha[i] = slot.gameObject.AddComponent<TweenCanvasAlpha>();
                }
            }
        }

        // ══════════════════════════════════════════════════════
        //  铺开 / 收起
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 铺开结算界面。
        /// </summary>
        /// <param name="candidates">
        /// 三张候选牌（按卡位顺序）。可以少于 3 张 —— 靠后的卡位会被隐藏。
        /// </param>
        /// <param name="goldReward">这一行金币显示的数额（= <c>UiLayout.ResultWinGoldReward</c>）。</param>
        public void Show(CardDef[] candidates, int goldReward)
        {
            // ⚠ **先激活再绑卡**。三张卡的 Button 监听是在 CardView.Awake 里注册的，
            //   而 Awake 只在「第一次被激活」时执行一次 —— 顺序反过来的话，
            //   卡面都绑好了、点击监听却还没接上，症状是「卡片看得见但点不动」，零报错。
            if (_root != null)
            {
                _root.SetActive(true);
            }

            RecordHome();
            BindTweens();
            StopClosing();

            _selected = -1;
            _open = true;

            for (int i = 0; i < _cards.Length; i++)
            {
                CardView card = _cards[i];
                if (card == null)
                {
                    continue;
                }

                CardDef def = candidates != null && i < candidates.Length ? candidates[i] : null;
                _candidateIds[i] = def == null ? string.Empty : def.Id;

                if (def == null)
                {
                    card.Clear();
                    card.gameObject.SetActive(false);
                    continue;
                }

                card.gameObject.SetActive(true);
                card.Bind(CardSnapshot.FromDef(def, 0, 0), CardView.ViewMode.Hand, i);
                card.SetFaceWidth(UiLayout.ResultWinCardWidth);
                card.SetInteractable(true);
                card.SetSelected(false);
                card.SetDimmed(false);
                card.SetAlpha(1f);
                card.transform.localPosition = _homePos[i];
            }

            if (_goldText != null)
            {
                _goldText.text = "+ " + goldReward;
            }

            PlayEntrance();
        }

        /// <summary>立刻收掉（切场景前调一次；出错兜底也调它）。</summary>
        public void Hide()
        {
            StopClosing();
            _open = false;
            _selected = -1;
            ResetSlots();
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>把三个卡位的缩放置回 1、透明度放回不透明（进场/退场动画的收尾）。</summary>
        private void ResetSlots()
        {
            if (_cards == null)
            {
                return;
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                if (i < _slotScale.Length && _slotScale[i] != null)
                {
                    _slotScale[i].Stop();
                }

                if (i < _slotAlpha.Length && _slotAlpha[i] != null)
                {
                    _slotAlpha[i].Stop();
                }

                CardView card = _cards[i];
                if (card == null)
                {
                    continue;
                }

                Transform slot = card.transform.parent;
                if (slot != null)
                {
                    slot.localScale = Vector3.one;
                }

                card.SetAlpha(1f);
            }
        }

        // ══════════════════════════════════════════════════════
        //  动画
        // ══════════════════════════════════════════════════════

        /// <summary>三张候选依次弹出来（缩放 OutBack + 淡入，每张错开一点）。</summary>
        private void PlayEntrance()
        {
            if (_cards == null)
            {
                return;
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                CardView card = _cards[i];
                if (card == null || !card.gameObject.activeSelf)
                {
                    continue;
                }

                float delay = i * UiLayout.ResultWinCardStagger;
                Transform slot = card.transform.parent;
                if (slot != null)
                {
                    slot.localScale = Vector3.one * UiLayout.ResultWinCardEnterScale;
                }

                if (i < _slotScale.Length && _slotScale[i] != null)
                {
                    _slotScale[i].Setup(Vector3.one * UiLayout.ResultWinCardEnterScale, Vector3.one,
                        UiLayout.ResultWinCardEnterSeconds, UiEaseKind.OutBack);
                    _slotScale[i].SetDelay(delay);
                    _slotScale[i].Play();
                }
                else if (slot != null)
                {
                    slot.localScale = Vector3.one;      // 没有补间 → 直接到位
                }

                // ⚠ 淡入画的是**卡位**那枚 CanvasGroup（`BindTweens` 挂在卡位上）——
                //   **不要**去动卡面自己的那一枚（<c>CardView.SetAlpha</c>）。
                //   两者是两个不同的 CanvasGroup：卡面那枚一旦被这里置 0，
                //   卡位那支补间把卡位拉回 1 也救不回来 —— 症状是
                //   「三张卡位摆得好好的、卡面全透明」，零报错（2026-10-05 实踩）。
                if (i < _slotAlpha.Length && _slotAlpha[i] != null)
                {
                    _slotAlpha[i].Setup(0f, 1f, UiLayout.ResultWinCardEnterSeconds, UiEaseKind.OutQuad);
                    _slotAlpha[i].SetDelay(delay);
                    _slotAlpha[i].Play();
                }
            }
        }

        /// <summary>选中一张时打一记「落定」脉冲（1.08 → 1）。</summary>
        private void PunchSlot(int index)
        {
            if (index < 0 || _slotScale == null || index >= _slotScale.Length || _slotScale[index] == null)
            {
                return;
            }

            _slotScale[index].Setup(Vector3.one * UiLayout.ResultWinPickPunchScale, Vector3.one,
                UiLayout.ResultWinPickPunchSeconds, UiEaseKind.OutQuad);
            _slotScale[index].Play();
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 点了一张卡：选中它；再点同一张 = 取消（这样「跳过奖励」不需要额外的按钮）。
        ///
        /// <para>选中的表现有三层：卡面自己的暖金描边（<c>SetSelected</c>，来自被手调过的
        /// <c>CardView_Hand.prefab</c>）、整张卡往上抬 <c>UiLayout.ResultWinCardLift</c>、
        /// 以及卡位上一记缩放脉冲（2026-10-05）—— 参考图里三张卡是平的，
        /// 但「点了有反馈」这件事比复刻更要紧。</para>
        /// </summary>
        private void OnCardClicked(CardView card)
        {
            if (!_open || card == null)
            {
                return;
            }

            int index = IndexOf(card);
            if (index < 0 || string.IsNullOrEmpty(_candidateIds[index]))
            {
                return;   // 空卡位不吃点击
            }

            bool wasSelected = _selected == index;
            _selected = wasSelected ? -1 : index;
            RefreshSelection();

            if (!wasSelected)
            {
                PunchSlot(index);
            }
        }

        private void OnContinueClicked()
        {
            if (!_open)
            {
                return;
            }

            // 立刻上锁：退场动画走完之前再点「继续」不该走第二遍。
            _open = false;
            StopClosing();
            _closing = StartCoroutine(CoClose(_selected));
        }

        /// <summary>
        /// 「选中的那张亮起、其余退场」→ 才把 <see cref="ContinueClicked"/> 报出去。
        ///
        /// <para>用<b>无缩放实时</b>（<c>WaitForSecondsRealtime</c>）：结算界面出现时
        /// 战斗已经被 <c>ShutdownAfterFinish</c> 停掉了，不该再受任何时间缩放影响。</para>
        /// </summary>
        private IEnumerator CoClose(int picked)
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                CardView card = _cards[i];
                if (card == null || !card.gameObject.activeSelf)
                {
                    continue;
                }

                bool isPicked = i == picked;
                bool hasSlot = i < _slotScale.Length && _slotScale[i] != null;

                if (isPicked && hasSlot)
                {
                    _slotScale[i].Setup(Vector3.one, Vector3.one * 1.10f,
                        UiLayout.ResultWinCloseSeconds, UiEaseKind.OutQuad);
                    _slotScale[i].Play();
                }
                else if (hasSlot)
                {
                    _slotScale[i].Setup(Vector3.one, Vector3.one * 0.94f,
                        UiLayout.ResultWinCloseSeconds, UiEaseKind.InQuad);
                    _slotScale[i].Play();
                    if (i < _slotAlpha.Length && _slotAlpha[i] != null)
                    {
                        _slotAlpha[i].Setup(1f, 0f, UiLayout.ResultWinCloseSeconds, UiEaseKind.InQuad);
                        _slotAlpha[i].Play();
                    }
                }
            }

            yield return new WaitForSecondsRealtime(UiLayout.ResultWinCloseSeconds);
            _closing = null;
            Hide();

            Action<int> handler = ContinueClicked;
            if (handler != null)
            {
                handler(picked);
            }
        }

        private void StopClosing()
        {
            if (_closing != null)
            {
                StopCoroutine(_closing);
                _closing = null;
            }
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                CardView card = _cards[i];
                if (card == null || !card.gameObject.activeSelf)
                {
                    continue;
                }

                bool picked = i == _selected;
                card.SetSelected(picked);
                // 选中那张不压暗；其余三张压下去，让玩家一眼看出「就它了」。
                card.SetDimmed(_selected >= 0 && !picked);

                // ⚠ Vector2 与 Vector3 的 + 是有歧义的（两个方向的隐式转换都存在），
                //   必须显式拼成 Vector3 再相加。
                var pos = new Vector3(_homePos[i].x, _homePos[i].y, 0f);
                if (picked)
                {
                    pos.y += UiLayout.ResultWinCardLift;
                }

                card.transform.localPosition = pos;
            }
        }

        /// <summary>
        /// 记一次三张卡的基准位置。
        ///
        /// <para><b>⚠ 只记一次</b>：<see cref="Show"/> 每次都会把卡摆回基准位，
        /// 而基准位本身若被「上一次抬升」污染，第二局起三张卡会越抬越高。
        /// 记的是构建器摆好的原始 <c>localPosition</c>（那时还没人动过）。</para>
        /// </summary>
        private void RecordHome()
        {
            if (_homeRecorded || _cards == null)
            {
                return;
            }

            for (int i = 0; i < _cards.Length && i < _homePos.Length; i++)
            {
                _homePos[i] = _cards[i] == null
                    ? Vector2.zero
                    : (Vector2)_cards[i].transform.localPosition;
            }

            _homeRecorded = true;
        }

        private int IndexOf(CardView card)
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] == card)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
