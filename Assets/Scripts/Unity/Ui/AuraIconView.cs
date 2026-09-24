using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 一枚光环图标要显示的全部信息（M15）。
    ///
    /// <para><b>纯表现数据</b>：由 <see cref="BattleUi"/> 从「冷却区快照的指示物明细」+
    /// 「本拍引擎给的选项」拼出来 —— 本结构里没有任何规则判断，
    /// 连 <see cref="Usable"/> 都是「引擎的 Options 里有没有对应那一项」的翻译结果。</para>
    ///
    /// <para><b>为什么用 SourceUid + TokenIndex 当身份</b>：双光环的卡（冰封铠甲 / 火灾）
    /// 两枚指示物完全同型，只靠「哪张牌 + 哪种光环」分不出玩家拖的是哪一枚；
    /// 而这两枚在 UI 上就是两个图标，各自要能正确对应到引擎的哪一个选项。</para>
    /// </summary>
    public struct AuraIconData
    {
        public int Seat;

        /// <summary>提供这枚指示物的卡实例编号。</summary>
        public int SourceUid;

        /// <summary>该指示物在「剩余指示物」里的序号（0 起）。</summary>
        public int TokenIndex;

        public AuraKind Kind;

        /// <summary>数值（加值 / 阈值）。</summary>
        public int Value;

        /// <summary>来源卡名。</summary>
        public string SourceName;

        /// <summary>卡表里的光环说明原文。</summary>
        public string Text;

        /// <summary>本拍是否可用（引擎本拍的 Options 里确实存在对应的那一项）。</summary>
        public bool Usable;

        /// <summary>池化复用的配对键（同一枚指示物在两次重建之间保持不变，避免闪）。</summary>
        public long Key
        {
            get { return MakeKey(Seat, SourceUid, TokenIndex); }
        }

        /// <summary>
        /// 一枚指示物的身份键 —— <see cref="BattleUi"/> 用它把「引擎的选项」和「屏幕上的图标」
        /// 对起来，所以<b>两边必须走同一个函数</b>（各写一遍位运算迟早会错开一位）。
        /// </summary>
        public static long MakeKey(int seat, int sourceUid, int tokenIndex)
        {
            return ((long)seat << 48) | ((long)sourceUid << 8) | (uint)(tokenIndex & 0xFF);
        }

        /// <summary>图标里那颗数值的文案。</summary>
        public string ValueText
        {
            get
            {
                switch (Kind)
                {
                    case AuraKind.ImmuneHigh:
                        return "≥" + Value;

                    case AuraKind.ImmuneLow:
                    case AuraKind.Combo:
                        return "≤" + Value;

                    default:
                        return "+" + Value;
                }
            }
        }

        /// <summary>底版颜色 —— 一眼分类用（红进攻 / 蓝防御 / 紫二选一 / 青连击 / 金免疫）。</summary>
        public Color PlateColor
        {
            get
            {
                switch (Kind)
                {
                    case AuraKind.AtkPower:
                        return UiTheme.AuraPlateAtk;

                    case AuraKind.DefPower:
                        return UiTheme.AuraPlateDef;

                    case AuraKind.AtkOrDefPower:
                        return UiTheme.AuraPlateBoth;

                    case AuraKind.Combo:
                        return UiTheme.AuraPlateCombo;

                    case AuraKind.ImmuneHigh:
                    case AuraKind.ImmuneLow:
                        return UiTheme.AuraPlateImmune;

                    default:
                        return UiTheme.MiniCardFace;
                }
            }
        }

        /// <summary>该光环是否「攻防二选一」（图标上要并排画剑和盾两个符号）。</summary>
        public bool IsDual
        {
            get { return Kind == AuraKind.AtkOrDefPower; }
        }

        /// <summary>符号取自什么时机（复用卡面那套 α 剑 / β 盾 / γ 感叹号）。</summary>
        public EffectTrigger Glyph
        {
            get
            {
                switch (Kind)
                {
                    case AuraKind.AtkPower:
                        return EffectTrigger.Attack;

                    case AuraKind.DefPower:
                    case AuraKind.ImmuneHigh:
                    case AuraKind.ImmuneLow:
                        return EffectTrigger.Defend;

                    case AuraKind.Combo:
                        return EffectTrigger.Special;

                    default:
                        return EffectTrigger.Attack;
                }
            }
        }

        /// <summary>说明文字 —— 快照里带了卡表原文就用它，没有就退回引擎那份措辞。</summary>
        public string Describe()
        {
            if (!string.IsNullOrEmpty(Text))
            {
                return Text;
            }

            return "光环：" + AuraResolver.DescribeEffect(Kind, Value);
        }

        /// <summary>
        /// 图标上的符号图（剑 / 盾 / 感叹号，与卡面 α β γ 同一套），取不到就返回 null
        /// （图标只剩底版 + 数值）。
        ///
        /// <para>取图逻辑放在本结构里而不是某个 View 里：HudBuff 的图标和判定区左侧的
        /// 「已准备」标记都画同一枚指示物，两处各写一遍迟早会画得不一样。</para>
        /// </summary>
        public Sprite GlyphSprite(bool primary)
        {
            TriggerIconLibrary lib = TriggerIconLibrary.Instance;
            if (lib == null)
            {
                return null;
            }

            // 攻防二选一 —— 剑 + 盾并排，一眼看出「两边都能顶」
            if (IsDual)
            {
                return lib.GetIcon(primary ? EffectTrigger.Attack : EffectTrigger.Defend);
            }

            return primary ? lib.GetIcon(Glyph) : null;
        }
    }

    /// <summary>光环图标手势的接收方（<see cref="HudBuffView"/> 实现它）。</summary>
    public interface IAuraIconHost
    {
        void OnAuraPress(AuraIconView icon);

        void OnAuraLongPress(AuraIconView icon);

        void OnAuraDragBegin(AuraIconView icon, Vector2 screenPos);

        void OnAuraDrag(AuraIconView icon, Vector2 screenPos);

        void OnAuraDrop(AuraIconView icon, Vector2 screenPos);

        /// <summary>松手 / 挪开 —— 收浮层、撤高亮（不区分是否算一次「使用」）。</summary>
        void OnAuraRelease(AuraIconView icon);
    }

    /// <summary>
    /// HudBuff 区里的<strong>一枚光环图标</strong>（M15）。
    ///
    /// <para><b>结构</b>：外框（描边色）→ 底版（按光环类型上色）→ 符号（剑 / 盾 / 感叹号，
    /// 取自 <see cref="TriggerIconLibrary"/>）→ 数值底衬 + 数值 → 压暗遮罩。
    /// 外框做成「底版后面垫一个稍大的同形色块」而不是四根细条：图标只有 72 px，
    /// 细条拼的框在缩放时会露出缝。</para>
    ///
    /// <para><b>为什么不挂 Button</b>：本图标只有「长按看说明 / 拖到自己的手牌区使用」两个动作，
    /// 没有点击语义。用 Button 反而要处理
    /// <c>onClick 在 OnPointerUp 之后派发</c>那套时序（<see cref="CardInteractor"/> 里那段注释），
    /// 徒增一处会踩错的地方。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuraIconView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("构件")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private Image _frame;
        [SerializeField] private Image _plate;
        [SerializeField] private Image _glyphA;
        [SerializeField] private Image _glyphB;
        [SerializeField] private Image _valueBg;
        [SerializeField] private TMP_Text _value;
        [SerializeField] private Image _dim;

        /// <summary>手势接收方（构建器在接线时赋值）。</summary>
        public IAuraIconHost Host;

        /// <summary>当前代表的那枚光环。</summary>
        public AuraIconData Data;

        /// <summary>「家」——拖完要回去的那个组（由 <see cref="HudBuffView"/> 给）。</summary>
        public RectTransform HomeGroup;

        /// <summary>拖拽期间的自由坐标系（铺满画布的那一层）。</summary>
        public RectTransform DragLayer;

        /// <summary>
        /// 「<b>再拖一下就取消</b>」的标记（M21，手牌区左侧那排「已准备光环」）。
        ///
        /// <para>这类图标**不是**被拿来自由摆放的：拖它 = 取消这次准备。所以
        /// <see cref="OnBeginDrag"/> 里不进拖拽态、也不换父节点，只把这次拖拽转发给
        /// <see cref="Host"/>（<see cref="PreparedAuraView"/> 发 <c>Cancelled</c>）。</para>
        ///
        /// <para>由 <see cref="PreparedAuraView.Bind"/> 在 <see cref="Bind"/> **之后**置位、
        /// <see cref="ResetState"/> 清零 —— 这两枚图标共用同一个模板与池子，漏清零会让 HudBuff
        /// 里的图标也变成「一拖就取消」。⚠ 也正因为 ResetState 会清它，置位必须排在
        /// <see cref="Bind"/> 后面（写在建对象的地方会被无声冲掉）。</para>
        /// </summary>
        public bool CancelOnDrag;

        private bool _pressed;
        private bool _dragging;
        private bool _longFired;
        private float _pressTime;

        // ── 飞行动画（M19 飞入 / M34 加上「滑回默认位」）──────────────
        private bool _flying;
        private RectTransform _flySurface;
        private RectTransform _flyTarget;
        private Vector3 _flyFromWorld;
        private float _flyElapsed;
        private float _flyDuration;

        /// <summary>飞行两端的缩放。飞入是 0.55 → 1（「长大到位」），滑回是 1 → 1（尺寸不变）。</summary>
        private float _flyScaleFrom = 1f;
        private float _flyScaleTo = 1f;

        /// <summary>缩放是不是走 OutBack（只有「飞入」要那点过冲）。</summary>
        private bool _flyScaleBack;

        /// <summary>落地后的一次性回调（滑回默认位时用来把标记收进池子）。</summary>
        private Action<AuraIconView> _flyDone;

        /// <summary>
        /// 终点是「一个世界坐标点」而不是一个槽位节点（M36）。
        ///
        /// <para><b>什么时候需要它</b>：AI 用掉一枚光环时，那一枚要飞到<b>怪物模型右侧
        /// 某个位置</b>并停在那儿淡出 —— 那个位置不是任何槽位（它不属于任何一排），
        /// 只是一个画布坐标。为了不为了这一次演出专门造一个长期节点，
        /// 就让终点可以是点。</para>
        ///
        /// <para>代价是落地时**不能**把 <c>anchoredPosition</c> 归零（那会把它拽回原点），
        /// 所以 <see cref="_flyKeepPlace"/> 跟着为真。</para>
        /// </summary>
        private Vector3 _flyToWorld;

        private bool _flyPointTarget;
        private bool _flyKeepPlace;

        public long Key
        {
            get { return Data.Key; }
        }

        public bool IsDragging
        {
            get { return _dragging; }
        }

        /// <summary>正在飞向槽位（<see cref="PreparedAuraView.Layout"/> 要跳过它，别把坐标按回 0）。</summary>
        public bool IsFlying
        {
            get { return _flying; }
        }

        // ══════════════════════════════════════════════════════
        //  绑定
        // ══════════════════════════════════════════════════════

        /// <summary>把一枚指示物画到图标上（<paramref name="glyphA"/>/<paramref name="glyphB"/> 由调用方从图标库取）。</summary>
        public void Bind(AuraIconData data, Sprite glyphA, Sprite glyphB)
        {
            Data = data;

            if (_plate != null)
            {
                _plate.color = data.PlateColor;
            }

            if (_glyphA != null)
            {
                _glyphA.sprite = glyphA;
                _glyphA.enabled = glyphA != null;
                _glyphA.color = UiTheme.AuraGlyph;
            }

            if (_glyphB != null)
            {
                _glyphB.sprite = glyphB;
                _glyphB.enabled = glyphB != null;
                _glyphB.color = UiTheme.AuraGlyph;
            }

            // 「攻防二选一」要并排画剑 + 盾（左右各偏一点、尺寸收小一档），
            // 其余类型只有一个符号、居中且更大。模板里两个符号都建好了，这里只调位置和尺寸。
            PlaceGlyph(_glyphA, data, data.IsDual ? -UiLayout.BuffIconGlyphDualOffset : 0f);
            PlaceGlyph(_glyphB, data, UiLayout.BuffIconGlyphDualOffset);

            if (_value != null)
            {
                _value.text = data.ValueText;
            }

            // 本拍能不能用 —— 只有「能用」的图标才亮边框，其余压暗但**照样看得见**
            //（玩家需要知道自己有什么，只是现在动不了）。
            if (_frame != null)
            {
                _frame.color = data.Usable ? UiTheme.AuraIconReadyEdge : UiTheme.AuraIconEdge;
            }

            if (_dim != null)
            {
                _dim.enabled = !data.Usable;
            }

            ResetState();
        }

        /// <summary>把一个符号摆到指定水平偏移上；尺寸按「要不要并排两个」取两档之一。</summary>
        private static void PlaceGlyph(Image glyph, AuraIconData data, float offsetX)
        {
            if (glyph == null)
            {
                return;
            }

            float side = data.IsDual ? UiLayout.BuffIconGlyphDual : UiLayout.BuffIconGlyph;

            RectTransform rt = (RectTransform)glyph.transform;
            rt.sizeDelta = new Vector2(side, side);
            rt.anchoredPosition = new Vector2(offsetX, UiLayout.BuffIconGlyphOffsetY);
        }

        /// <summary>池化回收：状态必须清零，否则复用时会带着「正被按着」的幽灵状态。</summary>
        public void ResetState()
        {
            _pressed = false;
            _dragging = false;
            _longFired = false;
            CancelOnDrag = false;
            CancelFly();
            ReturnHome();
        }

        // ══════════════════════════════════════════════════════
        //  飞入动画（M19）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 从世界点 <paramref name="fromWorld"/> 飞到 <paramref name="target"/> 槽位。
        ///
        /// <para><b>为什么要换父节点</b>：飞行途中图标要跨越大半个屏幕，挂在 72×72 的目标槽位里
        /// 就得用几千像素的 anchoredPosition 当偏移，一旦槽位或祖节点有缩放/旋转就会算歪。
        /// 挂到铺满画布的 <paramref name="surface"/> 上，两端坐标都是这块的局部坐标，口径统一。</para>
        ///
        /// <para>落地（<see cref="EndFly"/>）时再挂回目标槽位并把坐标归零 ——
        /// 与 <see cref="PreparedAuraView.Layout"/> 的「对齐槽位」是同一种终态，
        /// 所以谁先谁后都不会让图标偏半个身位。</para>
        /// </summary>
        public void FlyIn(RectTransform surface, Vector3 fromWorld, RectTransform target, float duration)
        {
            if (surface == null || target == null)
            {
                return;
            }

            // 飞行与拖拽互斥：真在拖的时候被叫去飞，说明调用方搞错了顺序，
            // 让它自己飞会把玩家手上的图标抢走。
            if (_dragging)
            {
                return;
            }

            _flying = true;
            _flySurface = surface;
            _flyTarget = target;
            _flyFromWorld = fromWorld;
            _flyDuration = Mathf.Max(0.01f, duration);
            _flyElapsed = 0f;
            _flyScaleFrom = UiLayout.AuraFlyStartScale;
            _flyScaleTo = 1f;
            _flyScaleBack = true;
            _flyDone = null;
            _flyPointTarget = false;
            _flyKeepPlace = false;

            RectTransform rt = (RectTransform)transform;
            rt.SetParent(surface, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one * UiLayout.AuraFlyStartScale;
            rt.anchoredPosition = surface.InverseTransformPoint(fromWorld);
            rt.SetAsLastSibling();

            // 「家」在起飞时就定好 —— 中途被 ResetState / ReturnHome 打断也不会落回别的组。
            HomeGroup = target;
        }

        /// <summary>
        /// 从世界点 <paramref name="fromWorld"/> **滑**到槽位 <paramref name="target"/>，
        /// 缩放**保持 1**，落地后回调 <paramref name="onLanded"/>（可为 null）。
        ///
        /// <para><b>与 <see cref="FlyIn"/> 的差别只有缩放</b>：飞入是「一小撮长大到位」，
        /// 用来表达「这枚图标刚从手里飞出来」；本方法两端都是成品尺寸，用来表达
        /// 「一枚<b>已经就位</b>的图标挪了个位置」。两类用法：</para>
        /// <list type="bullet">
        /// <item><b>取消使用</b>（M34）：准备位的标记飞回默认位（角色徽标旁那行），
        /// 落地那一刻 HudBuff 里那枚原图标才显出来 —— 位置、尺寸、图案完全一致，
        /// 所以接缝是看不见的（M34 之前这里没有任何动画：标记**瞬间**消失、
        /// 图标**瞬间**出现在原位）；</item>
        /// <item>取消中间那一枚之后，其余标记往前补一格（见 <c>AuraReflowSeconds</c>）。</item>
        /// </list>
        ///
        /// <para>⚠ 起点必须在换父节点**之前**取（<c>transform.position</c>）—— 换父节点会顺带
        /// 改 localPosition，先换再取就拿到新位置的坐标，图标会从目标点起飞。</para>
        /// </summary>
        public void GlideTo(RectTransform surface, Vector3 fromWorld, RectTransform target,
            float duration, Action<AuraIconView> onLanded)
        {
            if (surface == null || target == null)
            {
                return;
            }

            if (_dragging)
            {
                return;
            }

            _flying = true;
            _flySurface = surface;
            _flyTarget = target;
            _flyFromWorld = fromWorld;
            _flyDuration = Mathf.Max(0.01f, duration);
            _flyElapsed = 0f;
            _flyScaleFrom = 1f;
            _flyScaleTo = 1f;
            _flyScaleBack = false;
            _flyDone = onLanded;
            _flyPointTarget = false;
            _flyKeepPlace = false;

            RectTransform rt = (RectTransform)transform;
            rt.SetParent(surface, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.anchoredPosition = surface.InverseTransformPoint(fromWorld);
            rt.SetAsLastSibling();

            HomeGroup = target;
        }

        /// <summary>从「现在的位置」滑到槽位（<paramref name="fromWorld"/> 由本方法自己取）。</summary>
        public void GlideTo(RectTransform surface, RectTransform target, float duration,
            Action<AuraIconView> onLanded)
        {
            GlideTo(surface, transform.position, target, duration, onLanded);
        }

        /// <summary>
        /// 从世界点滑到**另一个世界点**（不是槽位）—— M36「AI 用掉了一枚光环」的演出专用。
        ///
        /// <para>与 <see cref="GlideTo(RectTransform,Vector3,RectTransform,float,Action{AuraIconView})"/>
        /// 只差终点：这里给的是坐标而不是节点，所以落地时<b>保留位置</b>（不换父节点、不把
        /// <c>anchoredPosition</c> 归零）—— 归零会把它拽回 surface 的原点。</para>
        ///
        /// <para>起点仍必须在换父节点**之前**取（<c>transform.position</c>），原因同
        /// <see cref="GlideTo(RectTransform,Vector3,RectTransform,float,Action{AuraIconView})"/> 的说明。</para>
        /// </summary>
        public void GlideToPoint(RectTransform surface, Vector3 fromWorld, Vector3 toWorld,
            float duration, Action<AuraIconView> onLanded)
        {
            if (surface == null)
            {
                return;
            }

            if (_dragging)
            {
                return;
            }

            _flying = true;
            _flySurface = surface;
            _flyTarget = null;
            _flyToWorld = toWorld;
            _flyFromWorld = fromWorld;
            _flyDuration = Mathf.Max(0.01f, duration);
            _flyElapsed = 0f;
            _flyScaleFrom = 1f;
            _flyScaleTo = 1f;
            _flyScaleBack = false;
            _flyDone = onLanded;
            _flyPointTarget = true;
            _flyKeepPlace = true;

            RectTransform rt = (RectTransform)transform;
            rt.SetParent(surface, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.anchoredPosition = surface.InverseTransformPoint(fromWorld);
            rt.SetAsLastSibling();
        }

        /// <summary>
        /// 「这枚图标不吃射线」（M36 的演出用）。
        ///
        /// <para>图标模板上唯一接射线的是外框（<c>Frame</c>，见构建器注释）。一次性的演出图标
        /// 会短暂停在冷却区上方，若还接着射线就会把玩家那几下点击吃掉 ——
        /// 而它出现的时机恰恰是玩家正在点冷却区的时候。</para>
        /// </summary>
        public void SetRaycastTarget(bool on)
        {
            if (_frame != null)
            {
                _frame.raycastTarget = on;
            }
        }

        /// <summary>
        /// 打断飞行（不换父节点）—— 池化回收 / 重新绑定拖拽时用。
        ///
        /// <para>⚠ 顺带丢掉落地回调：被打断的飞行**不会**走到 <see cref="EndFly"/>，
        /// 所以指望那个回调做收尾的调用方（<see cref="PreparedAuraView"/> 的「飞回默认位」）
        /// 必须自己有一条「打断也要收尾」的路径（见 <c>PreparedAuraView.AbandonReturns</c>），
        /// 不能只挂着回调等它到点。</para>
        /// </summary>
        public void CancelFly()
        {
            _flying = false;
            _flySurface = null;
            _flyTarget = null;
            _flyDone = null;
            _flyPointTarget = false;
            _flyKeepPlace = false;
        }

        private void StepFly()
        {
            RectTransform rt = (RectTransform)transform;

            if (_flySurface == null || (!_flyPointTarget && _flyTarget == null))
            {
                EndFly();
                return;
            }

            _flyElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_flyElapsed / _flyDuration);

            // 位置用 OutCubic（收尾平缓）；缩放另说 —— 飞入用 OutBack（落定时轻微过冲再收回，
            // 才有「飞过去撞进槽位」的感觉），滑回两端同尺寸、不走曲线也不动。
            Vector2 from = _flySurface.InverseTransformPoint(_flyFromWorld);
            Vector3 toWorld = _flyPointTarget ? _flyToWorld : _flyTarget.position;
            Vector2 to = _flySurface.InverseTransformPoint(toWorld);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, UiEase.Evaluate(UiEaseKind.OutCubic, t));

            float s;
            if (_flyScaleBack)
            {
                s = Mathf.LerpUnclamped(_flyScaleFrom, _flyScaleTo,
                    UiEase.Evaluate(UiEaseKind.OutBack, t));
            }
            else
            {
                s = _flyScaleTo;
            }

            rt.localScale = new Vector3(s, s, 1f);

            if (t >= 1f)
            {
                EndFly();
            }
        }

        private void EndFly()
        {
            RectTransform target = _flyTarget;
            bool keepPlace = _flyKeepPlace;

            // 先取出回调再 CancelFly —— CancelFly 会把它清掉（见那里的说明）。
            Action<AuraIconView> done = _flyDone;
            CancelFly();

            RectTransform rt = (RectTransform)transform;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            if (target != null)
            {
                rt.SetParent(target, false);
                HomeGroup = target;
            }

            // 终点是「坐标」的那一趟保留落点；终点是槽位的那两趟归零（与槽位对齐）——
            // 归零对坐标终点是错的，那会把它拽回 surface 的原点。
            if (!keepPlace)
            {
                rt.anchoredPosition = Vector2.zero;
            }

            if (done != null)
            {
                done(this);
            }
        }

        // ══════════════════════════════════════════════════════
        //  指针事件
        // ══════════════════════════════════════════════════════

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // 飞入途中不吃任何手势：此时图标正从落点掠向槽位，指针很可能刚好压在它上面 ——
            // 收下这次按下会让「按下时间」从飞行中就开始计，落地瞬间直接弹出一层说明浮层。
            if (_flying) return;

            _pressed = true;
            _longFired = false;
            _pressTime = Time.unscaledTime;

            if (Host != null)
            {
                Host.OnAuraPress(this);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            _longFired = false;

            if (Host != null)
            {
                Host.OnAuraRelease(this);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 长按弹着说明时指针一移开就收（与手牌的「挪开就收起」同一条口径）
            _pressed = false;
            _longFired = false;

            if (Host != null)
            {
                Host.OnAuraRelease(this);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_flying) return;

            if (!Data.Usable || Data.Seat != BattleState.SeatPlayer
                || eventData.button != PointerEventData.InputButton.Left) return;

            // 「已准备」标记（M21）：拖一下 = 取消。不进拖拽态、不换父节点，
            // 只把这次拖拽转发出去；标记随后被 PreparedAuraView.Bind 收走、
            // 原图标回到 HudBuff 的默认位。打开 _dragging 会与「拖=取消」矛盾
            //（Layout 会跳过它、图标还会被拎着走）。
            if (CancelOnDrag)
            {
                _pressed = false;
                _longFired = false;

                if (Host != null)
                {
                    Host.OnAuraDragBegin(this, eventData.position);
                }

                return;
            }

            _dragging = true;
            _pressed = false;
            _longFired = false;
            CancelFly();

            // 拖出去之前先把它挂到自由层 —— 挂在一个只有几十像素宽的组里
            // 是拖不出组外的（会被父 RectTransform 的层级/锚点算得乱七八糟）。
            if (DragLayer != null)
            {
                RectTransform rt = (RectTransform)transform;
                rt.SetParent(DragLayer, false);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;

                // 压到同层最后一个：拖到手上时不能被手牌盖住
                //（ArtLayer 在 Canvas 里的兄弟序很靠前，光换父节点还不够）。
                rt.SetAsLastSibling();
            }

            if (Host != null)
            {
                Host.OnAuraDragBegin(this, eventData.position);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || DragLayer == null)
            {
                return;
            }

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    DragLayer, eventData.position, null, out local))
            {
                return;
            }

            // 图标中心摆在指针上再抬一点 —— 手指 / 指针本来就压在图标正中间，
            // 不抬起来就等于看不见自己拖的是什么。
            ((RectTransform)transform).anchoredPosition = local + new Vector2(0f, UiLayout.BuffIconDragLift);

            if (Host != null)
            {
                Host.OnAuraDrag(this, eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;

            if (Host != null)
            {
                Host.OnAuraDrop(this, eventData.position);
            }

            // 落点判定已经做完了，回家由 HudBuffView 重新排一次版（它会顺带把锚点复原）
            ReturnHome();
        }

        /// <summary>回到「家」那个组（锚点 / 轴心交回给 <see cref="HudBuffView.Layout"/> 重设）。</summary>
        public void ReturnHome()
        {
            if (HomeGroup == null)
            {
                return;
            }

            RectTransform rt = (RectTransform)transform;
            if (rt.parent != HomeGroup)
            {
                rt.SetParent(HomeGroup, false);
            }
        }

        private void Update()
        {
            // 飞行优先：动画期间不接受长按，也不让下面的状态机插手坐标。
            if (_flying)
            {
                StepFly();
                return;
            }

            if (!_pressed || _dragging || _longFired)
            {
                return;
            }

            if (Time.unscaledTime - _pressTime < UiLayout.LongPressSeconds)
            {
                return;
            }

            _longFired = true;

            if (Host != null)
            {
                Host.OnAuraLongPress(this);
            }
        }
    }
}
