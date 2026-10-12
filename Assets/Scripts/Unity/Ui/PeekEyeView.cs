using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 「AI 偷看你的手牌」的眼睛提示（2026-10-08 加）。
    ///
    /// <para><b>它补的是哪个洞</b>：雷云（ad）与狂躁蘑菇（w）的第 2 个 α 效果是
    /// 「随机查看对方一张手牌，满足阈值则送入冷却」。<b>玩家用</b>时走
    /// <see cref="PeekView"/>（一排牌背让玩家自己点一张，翻开看结果）；
    /// 但<b>AI 用</b>时那一拍根本没有浮层 —— 引擎直接替玩家点了，
    /// 于是玩家只看到顶部提示条一句「怪物 查看了对方的手牌，送入冷却」，
    /// <b>完全不知道看的是哪一张、少的又是哪一张</b>（用户 2026-10-08 口径）。</para>
    ///
    /// <para><b>表现</b>：在被看的那张手牌<b>正上方</b>亮起一只眼睛 ——</para>
    /// <list type="bullet">
    /// <item>先亮起来（暖白）＝「有人在看这张」；</item>
    /// <item>眨一下眼，然后落到结果色：
    /// <b>变暗</b>（<see cref="UiTheme.PeekEyeDim"/>）＝ 无事发生、
    /// <b>变红并抖一下</b>（<see cref="UiTheme.PeekEyeCooled"/>）＝ 这张要被送进冷却；</item>
    /// <item>停一下 → 淡出。整段 ~1.1–1.25 s。</item>
    /// </list>
    ///
    /// <para><b>不阻塞驱动</b>：这是纯展示的回话（性质同 M28「按住看怪物手牌」），
    /// 不写 <c>BattleUi._pendingHold</c> —— 写了的话 AI 每偷看一次就要多等一秒多，
    /// 而它本来只是「让玩家看懂刚才发生了什么」。眼睛按自己的时间轴跑完即可。</para>
    ///
    /// <para><b>为什么眼睛节点由 M8 构建器预建</b>：与 PeekLayer / MonsterHandLayer 同一条账 ——
    /// <c>BattleUiBuilder.Cleanup</c> 会整棵删掉 <c>PeekEyeLayer</c> 再重建，
    /// 手摆的节点活不过一次重跑。所以这里只认构建器写进来的槽位，
    /// 运行时<b>不新建任何节点</b>（铁律：长期节点一律构建器生成）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PeekEyeView : MonoBehaviour
    {
        /// <summary>一个眼睛槽位（构建器预建）。</summary>
        [System.Serializable]
        private sealed class EyeSlot
        {
            public RectTransform Root;
            public Image Icon;
        }

        [Header("槽位（M8 构建器预建、默认全部失活）")]
        [SerializeField] private EyeSlot[] _slots = new EyeSlot[0];

        /// <summary>
        /// 眼睛图的兜底来源（构建器写入 = <c>Assets/Art/Ui/Peek_Eye.png</c>）。
        ///
        /// <para>与 <c>PeekView._cardBackFallback</c> 同一条理由：正常路径是构建期直接
        /// 从 <c>AssetDatabase</c> 取；万一取不到（.meta 被重导丢设置之类），
        /// 这里至少还留着上一次序列化的引用，不至于画出一块纯白方块。</para>
        /// </summary>
        [SerializeField] private Sprite _eyeSprite;

        // ── 运行态 ───────────────────────────────────────────
        private RectTransform _self;
        private Coroutine[] _routines;
        private CardView[] _followView;
        private int[] _followUid;
        private Vector3[] _world;      // 锚点（画布世界坐标）
        private float[] _shake;        // 红态抖动（叠加在 x 上，见 LateUpdate）
        private int _cursor;

        private int SlotCount
        {
            get { return _routines == null ? 0 : _routines.Length; }
        }

        private void Awake()
        {
            _self = (RectTransform)transform;

            int n = _slots == null ? 0 : _slots.Length;
            _routines = new Coroutine[n];
            _followView = new CardView[n];
            _followUid = new int[n];
            _world = new Vector3[n];
            _shake = new float[n];

            for (int i = 0; i < n; i++)
            {
                _followUid[i] = -1;
                if (_slots[i] == null)
                {
                    continue;
                }

                // 构建期没取到图时补一次；再取不到就留空（画出来是一块纯色方块，位置仍对）
                if (_slots[i].Icon != null && _slots[i].Icon.sprite == null)
                {
                    _slots[i].Icon.sprite = _eyeSprite;
                }

                // ⚠ 这里**不**调 HideImmediate() 去关槽位 ——
                //   「构建器置失活 + 运行时 Show」的组件在 Awake 里关一次，
                //   会把本局第一次 Show 一起关掉（本工程在 DropArrowView /
                //   PlayedCardView 上各中过一次）。构建器已经把槽位存成失活的。
            }
        }

        // ══════════════════════════════════════════════════════
        //  对外
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 在 <paramref name="anchor"/> 这张牌的上方亮一只眼睛。
        ///
        /// <para><paramref name="uid"/> = 被看那张牌的实例号：只要这只眼睛还「认得出是这张牌」
        /// 就跟着它走（手牌扇形在动画里会滑），牌位被回收（卡进了冷却区、位置让给了别人）
        /// 就<b>冻在最后的位置</b> —— 语义上仍然指着「刚才被看的那张」。</para>
        ///
        /// <para><paramref name="anchor"/> 传进来的若不是 <see cref="CardView"/>
        /// （兜底：手牌区根节点），就只按它算一次位置、不跟随。</para>
        /// </summary>
        public void Show(RectTransform anchor, int uid, bool cooled)
        {
            if (anchor == null || SlotCount == 0)
            {
                return;
            }

            int i = TakeSlot();
            EyeSlot s = _slots[i];
            if (s == null || s.Root == null || s.Icon == null)
            {
                return;
            }

            CardView view = anchor.GetComponent<CardView>();
            _followView[i] = view;
            _followUid[i] = view != null ? uid : -1;
            _world[i] = AnchorWorld(anchor);
            _shake[i] = 0f;

            s.Root.gameObject.SetActive(true);
            s.Root.SetAsLastSibling();          // 压在手牌之上（同层绘制顺序 = 兄弟顺序）
            s.Root.localScale = Vector3.one * UiLayout.PeekEyePopFrom;
            s.Root.anchoredPosition = _self.InverseTransformPoint(_world[i]);
            SetIconColor(s.Icon, UiTheme.PeekEyeActive, 0f);

            _routines[i] = StartCoroutine(CoPlay(i, cooled));
        }

        /// <summary>立刻收掉全部眼睛（重开一局 / 进结算时用，不要动画）。</summary>
        public void HideImmediate()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                StopAt(i);
            }
        }

        /// <summary>
        /// 每帧把眼睛摆到锚点上方。
        ///
        /// <para>放在 <c>LateUpdate</c>：手牌的扇形排布在 <c>Update</c> 里平滑趋近，
        /// 必须先让它们算完，不然眼睛会永远差一帧、在牌滑动时可见地抖。</para>
        /// </summary>
        private void LateUpdate()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_routines[i] == null || _slots[i] == null || _slots[i].Root == null)
                {
                    continue;
                }

                CardView view = _followView[i];
                if (view != null && _followUid[i] >= 0 && view.HasCard
                    && view.Card.Uid == _followUid[i])
                {
                    _world[i] = AnchorWorld((RectTransform)view.transform);
                }
                // 认不出就冻在 _world[i] 里最后那次的结果（见 Show 的说明）

                Vector2 local = _self.InverseTransformPoint(_world[i]);
                _slots[i].Root.anchoredPosition = new Vector2(local.x + _shake[i], local.y);
            }
        }

        // ══════════════════════════════════════════════════════
        //  动画
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 一只眼睛的完整时间轴：
        /// 淡入弹起 → 亮着看 → 眨眼 → 落结果色（红 / 暗）→ 停 → 淡出。
        /// </summary>
        private IEnumerator CoPlay(int i, bool cooled)
        {
            EyeSlot s = _slots[i];

            // ① 亮起来
            yield return CoAppear(s);

            // ② 「正在看」——先亮着停一下，让玩家看清是哪一张
            yield return new WaitForSecondsRealtime(UiLayout.PeekEyeLookHoldSeconds);

            // ③ 眨一下眼（闭 → 开）：把「看完了」这件事做成一个动作，而不是硬切颜色
            yield return CoScaleY(s, 1f, UiLayout.PeekEyeBlinkCloseScale, UiLayout.PeekEyeBlinkSeconds * 0.5f);
            yield return CoScaleY(s, UiLayout.PeekEyeBlinkCloseScale, 1f, UiLayout.PeekEyeBlinkSeconds * 0.5f);

            // ④ 结果色
            SetIconColor(s.Icon, cooled ? UiTheme.PeekEyeCooled : UiTheme.PeekEyeDim, 1f);

            if (cooled)
            {
                yield return CoAlert(i, s, UiLayout.PeekEyeRedHoldSeconds);
            }
            else
            {
                yield return new WaitForSecondsRealtime(UiLayout.PeekEyeDimHoldSeconds);
            }

            // ⑤ 淡出
            yield return CoFadeOut(s);

            // ⚠ 收尾只能**就地写状态**，不能调 StopAt(i) —— 那会 StopCoroutine 掉自己，
            //   后面这几行永远不会执行，槽位就再也回收不了。
            s.Root.gameObject.SetActive(false);
            _followView[i] = null;
            _followUid[i] = -1;
            _shake[i] = 0f;
            _routines[i] = null;
        }

        private IEnumerator CoAppear(EyeSlot s)
        {
            float dur = UiLayout.PeekEyeFadeInSeconds;
            float t = 0f;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = EaseOut(Mathf.Clamp01(dur <= 0f ? 1f : t / dur));
                s.Root.localScale = Vector3.one * Mathf.Lerp(UiLayout.PeekEyePopFrom, 1f, k);
                SetAlpha(s.Icon, k);
                yield return null;
            }

            s.Root.localScale = Vector3.one;
            SetAlpha(s.Icon, 1f);
        }

        private IEnumerator CoFadeOut(EyeSlot s)
        {
            float dur = UiLayout.PeekEyeFadeOutSeconds;
            float t = 0f;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(dur <= 0f ? 1f : t / dur);
                SetAlpha(s.Icon, 1f - k);
                yield return null;
            }

            SetAlpha(s.Icon, 0f);
        }

        /// <summary>只改 y 向缩放（眨眼）。</summary>
        private static IEnumerator CoScaleY(EyeSlot s, float from, float to, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(dur <= 0f ? 1f : t / dur);
                s.Root.localScale = new Vector3(1f, Mathf.Lerp(from, to, k), 1f);
                yield return null;
            }

            s.Root.localScale = new Vector3(1f, to, 1f);
        }

        /// <summary>
        /// 红态：横向抖两下。
        ///
        /// <para>抖动写进 <see cref="_shake"/> 由 <c>LateUpdate</c> 叠加 ——
        /// 不直接写 <c>anchoredPosition</c>，否则会被同一帧的跟随逻辑覆盖掉。</para>
        /// </summary>
        private IEnumerator CoAlert(int i, EyeSlot s, float dur)
        {
            float t = 0f;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float damp = 1f - Mathf.Clamp01(dur <= 0f ? 1f : t / dur);
                _shake[i] = Mathf.Sin(t * UiLayout.PeekEyeShakeFrequency)
                            * UiLayout.PeekEyeShakeAmplitude * damp;
                yield return null;
            }

            _shake[i] = 0f;
        }

        // ══════════════════════════════════════════════════════
        //  小工具
        // ══════════════════════════════════════════════════════

        /// <summary>牌的顶边中点往上一段 —— 眼睛就摆在这儿。</summary>
        private static Vector3 AnchorWorld(RectTransform rt)
        {
            // rect.yMax = 自轴心到顶边的距离 → (0, yMax) 就是「顶边中点」在本地空间的坐标
            return rt.TransformPoint(new Vector3(0f, rt.rect.yMax, 0f));
        }

        /// <summary>取一个槽位：优先找空闲的，全忙就顶掉正在播的那只。</summary>
        private int TakeSlot()
        {
            int n = SlotCount;
            for (int k = 0; k < n; k++)
            {
                int i = (_cursor + k) % n;
                if (_routines[i] == null)
                {
                    _cursor = (i + 1) % n;
                    return i;
                }
            }

            int j = _cursor;
            _cursor = (_cursor + 1) % n;
            return j;
        }

        private void StopAt(int i)
        {
            if (_routines == null || i < 0 || i >= _routines.Length)
            {
                return;
            }

            if (_routines[i] != null)
            {
                StopCoroutine(_routines[i]);
                _routines[i] = null;
            }

            _followView[i] = null;
            _followUid[i] = -1;
            _shake[i] = 0f;

            if (_slots[i] != null && _slots[i].Root != null)
            {
                _slots[i].Root.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            HideImmediate();
        }

        private static void SetIconColor(Image img, Color tint, float alpha)
        {
            if (img != null)
            {
                img.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha);
            }
        }

        private static void SetAlpha(Image img, float alpha)
        {
            if (img != null)
            {
                Color c = img.color;
                c.a = alpha;
                img.color = c;
            }
        }

        /// <summary>ease-out quad —— 只用在淡入的弹起上，够用且不引依赖。</summary>
        private static float EaseOut(float k)
        {
            float u = 1f - k;
            return 1f - u * u;
        }
    }
}
