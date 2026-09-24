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
    /// <c>ArtLayer/HudBuff</c> 光环图标区（M15）。
    ///
    /// <para><b>用户口径（2026-09-18）</b></para>
    /// <list type="bullet">
    /// <item>双方持有的光环都显示在这里 —— <b>玩家组在左、敌人组在右</b>；</item>
    /// <item><b>相同的 buff 不堆叠</b>：每一枚指示物就是一个图标（不做「同名 ×N」合并）；</item>
    /// <item>用掉 / 消失之后图标跟着移除；</item>
    /// <item><b>长按</b>图标看它的具体内容；</item>
    /// <item>把图标<b>拖到自己的手牌区</b>即视为使用这枚光环（只有本拍确实能用的那几枚才点亮、才拖得动）。</item>
    /// </list>
    ///
    /// <para><b>本类不做规则判断</b>（铁律 3）：「这枚能不能用」由 <see cref="BattleUi"/>
    /// 拿引擎给的 Options 比对后写进 <see cref="AuraIconData.Usable"/>；
    /// 「拖到了没有」由 <see cref="BattleUi"/> 问 <see cref="HandView"/>。
    /// 本类只负责：摆图标、转发手势、弹说明。</para>
    ///
    /// <para><b>为什么图标拖拽要换父节点</b>：图标平时挂在只有几十像素宽的组里，
    /// 拖出组外必须先挂到铺满画布的 <c>HudBuff</c> 根上（见 <see cref="AuraIconView.OnBeginDrag"/>），
    /// 松手再回家、由 <see cref="Layout"/> 重排一次。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudBuffView : MonoBehaviour, IAuraIconHost
    {
        [Header("容器")]
        [Tooltip("玩家组（左上）。锚点 / 轴心都是左上。")]
        [SerializeField] private RectTransform _playerGroup;

        [Tooltip("敌人组（右上）。锚点 / 轴心都是右上。")]
        [SerializeField] private RectTransform _enemyGroup;

        [Tooltip("图标模板（失活节点）。运行时按需实例化 —— 光环数量是 0~8，池化够用。")]
        [SerializeField] private AuraIconView _iconTemplate;

        [Tooltip("长按弹出的说明浮层。")]
        [SerializeField] private AuraTooltipView _tip;

        [Tooltip("M36：AI 用掉一枚光环时，那一枚飞过去的落点（ArtLayer/AuraCastSeat）。")]
        [SerializeField] private RectTransform _castSeat;

        /// <summary>图标开始被拖（<paramref name="screenPos"/> 是指针位置）。</summary>
        public event Action<AuraIconData, Vector2> IconDragBegin;

        /// <summary>拖动中。</summary>
        public event Action<AuraIconData, Vector2> IconDragMove;

        /// <summary>松手（调用方自行判断落点算不算「用掉」）。</summary>
        public event Action<AuraIconData, Vector2> IconDropped;

        /// <summary>这一串手势结束了（收高亮用）。</summary>
        public event Action GestureEnded;

        private readonly List<AuraIconView> _playerIcons = new List<AuraIconView>();
        private readonly List<AuraIconView> _enemyIcons = new List<AuraIconView>();
        private readonly List<AuraIconView> _pool = new List<AuraIconView>();
        private readonly Dictionary<long, AuraIconView> _reuse = new Dictionary<long, AuraIconView>();

        /// <summary>
        /// 已经「准备使用」的指示物（键）。
        ///
        /// <para><b>为什么要单独藏一份</b>（M19）：一枚光环被拖进手牌区之后，它的图标就已经
        /// 挪到判定区左侧去显示了 —— 同一个指示物不能在两处各亮一个（玩家会以为有两枚）。
        /// 所以 HudBuff 这边的那一枚要当场消失，而不是等下一拍数据里没有它才回收：
        /// 引擎那边的指示物其实还在（真正消耗发生在出牌那一刻），数据一直在，光靠快照筛不掉。</para>
        /// </summary>
        private readonly HashSet<long> _hidden = new HashSet<long>();

        private AuraIconView _active;

        /// <summary>当前浮层讲的是哪一枚指示物（0 = 浮层没开）。</summary>
        private long _tipKey;

        /// <summary>当前显示的图标总数（自测 / 探针用）。</summary>
        public int IconCount
        {
            get { return _playerIcons.Count + _enemyIcons.Count; }
        }

        /// <summary>
        /// 图标模板（失活节点）。
        ///
        /// <para><b>M16 起对外暴露</b>：判定区左侧的「已准备光环」标记要画的是同一种图标 ——
        /// 复用同一个模板，比在场景里再接第二个模板资产可靠（少一处会忘改的接线）。</para>
        /// </summary>
        public AuraIconView IconTemplate
        {
            get { return _iconTemplate; }
        }

        public void Configure(RectTransform playerGroup, RectTransform enemyGroup,
            AuraIconView iconTemplate, AuraTooltipView tip)
        {
            Configure(playerGroup, enemyGroup, iconTemplate, tip, null);
        }

        /// <summary>
        /// 完整接线（M36 起多一个「AI 用光环」的落点）。
        ///
        /// <para>保留上面那个四参重载：老调用点（以及只看前四个字段的读者）不必跟着改，
        /// 落点缺省为 null 时 <see cref="PlayCast"/> 直接不演出（少一段提示，不影响对局）。</para>
        /// </summary>
        public void Configure(RectTransform playerGroup, RectTransform enemyGroup,
            AuraIconView iconTemplate, AuraTooltipView tip, RectTransform castSeat)
        {
            _playerGroup = playerGroup;
            _enemyGroup = enemyGroup;
            _iconTemplate = iconTemplate;
            _tip = tip;
            _castSeat = castSeat;

            if (_iconTemplate != null)
            {
                _iconTemplate.gameObject.SetActive(false);
            }

            HideTip();
        }

        // ══════════════════════════════════════════════════════
        //  绑定
        // ══════════════════════════════════════════════════════

        /// <summary>重建双方的光环图标（幂等，按 Key 复用已有图标，避免每拍闪一下）。</summary>
        public void Bind(IReadOnlyList<AuraIconData> player, IReadOnlyList<AuraIconData> enemy)
        {
            _reuse.Clear();

            Collect(_playerIcons, player);
            Collect(_enemyIcons, enemy);

            Layout();

            // 说明浮层的数据源是「按下那一刻的那枚指示物」。它已经被用掉 / 回手之后，
            // 浮层里那几行字就成了假消息 —— 连同它的图标一起收掉。
            if (_tipKey != 0 && !HasKey(player, _tipKey) && !HasKey(enemy, _tipKey))
            {
                HideTip();
            }
        }

        private static bool HasKey(IReadOnlyList<AuraIconData> data, long key)
        {
            AuraIconData ignored;
            return TryFind(data, key, out ignored);
        }

        /// <summary>
        /// 设置「已经挪到判定区左侧显示」的指示物（键）—— 这些图标立即从 HudBuff 隐去。
        ///
        /// <para><see cref="BattleUi"/> 每次改动准备集合后调一次（含清空）。本方法自己会立刻
        /// 把命中的图标失活，不等下一次 <see cref="Bind"/> —— 松手那一刻原图标就该消失，
        /// 否则会出现「手里那份和判定区那份同时亮着」的一两拍。</para>
        /// </summary>
        public void SetHidden(IReadOnlyList<long> keys)
        {
            _hidden.Clear();

            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    _hidden.Add(keys[i]);
                }
            }

            ApplyHidden(_playerIcons);
            ApplyHidden(_enemyIcons);
        }

        private void ApplyHidden(List<AuraIconView> icons)
        {
            for (int i = 0; i < icons.Count; i++)
            {
                AuraIconView v = icons[i];
                if (v != null && v.gameObject.activeSelf && _hidden.Contains(v.Key))
                {
                    v.gameObject.SetActive(false);
                }
            }
        }

        private void Collect(List<AuraIconView> shown, IReadOnlyList<AuraIconData> data)
        {
            // 1) 先把还活着的认出来（按 Key 配对），并刷新它的显示
            for (int i = 0; i < shown.Count; i++)
            {
                AuraIconView v = shown[i];
                if (v == null)
                {
                    continue;
                }

                AuraIconData match;
                if (!_hidden.Contains(v.Key)
                    && TryFind(data, v.Key, out match)
                    && !_reuse.ContainsKey(v.Key))
                {
                    _reuse.Add(v.Key, v);
                    v.Bind(match, GlyphSprite(match, true), GlyphSprite(match, false));
                }
            }

            // 2) 没配上的退回池子
            var keep = new List<AuraIconView>();
            for (int i = 0; i < shown.Count; i++)
            {
                AuraIconView v = shown[i];
                if (v == null)
                {
                    continue;
                }

                if (_reuse.ContainsKey(v.Key))
                {
                    keep.Add(v);
                    continue;
                }

                v.gameObject.SetActive(false);
                v.ResetState();
                _pool.Add(v);
            }

            shown.Clear();

            // 3) 新的补上（被藏起来的那些不补）
            if (data != null)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    if (_hidden.Contains(data[i].Key))
                    {
                        continue;
                    }

                    AuraIconView v;
                    if (!_reuse.TryGetValue(data[i].Key, out v))
                    {
                        v = Take();
                        v.Bind(data[i], GlyphSprite(data[i], true), GlyphSprite(data[i], false));
                    }

                    v.gameObject.SetActive(true);
                    shown.Add(v);
                }
            }
        }

        private static bool TryFind(IReadOnlyList<AuraIconData> data, long key, out AuraIconData match)
        {
            match = default(AuraIconData);

            if (data == null)
            {
                return false;
            }

            for (int i = 0; i < data.Count; i++)
            {
                if (data[i].Key == key)
                {
                    match = data[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>符号图（剑 / 盾 / 感叹号，与卡面 α β γ 同一套）—— 取图口径在 <see cref="AuraIconData.GlyphSprite"/>。</summary>
        private static Sprite GlyphSprite(AuraIconData data, bool primary)
        {
            return data.GlyphSprite(primary);
        }

        private AuraIconView Take()
        {
            AuraIconView v = null;

            if (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                v = _pool[last];
                _pool.RemoveAt(last);
                return v;
            }

            if (_iconTemplate == null)
            {
                return null;
            }

            v = Instantiate(_iconTemplate, transform);
            v.name = "AuraIcon";
            v.Host = this;
            v.DragLayer = DragSurface();
            v.HomeGroup = null;   // 由 Layout() 在摆位时补上
            return v;
        }

        /// <summary>
        /// 拖拽时挂到<b>画布根</b>上，并且要压在所有东西之上。
        ///
        /// <para><b>为什么不能挂在 HudBuff 自己身上</b>：ArtLayer 在 Canvas 里的兄弟序很靠前
        /// （紧跟 Backdrop），M7/M8 的界面全在它上面 —— 图标挂回 ArtLayer 之后，
        /// 一拖到手上就被手牌盖住了（实测截图里就是「拖到一半图标不见了」）。
        /// 画布根的局部坐标与 HudBuff 完全一致（都是铺满屏幕、轴心在中心），
        /// 所以换上去不影响坐标换算。</para>
        /// </summary>
        private RectTransform DragSurface()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return (RectTransform)transform;
            }

            return (RectTransform)canvas.rootCanvas.transform;
        }

        // ══════════════════════════════════════════════════════
        //  排布
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 两个组各自按「一排 5 枚 → 换行」摆。
        /// 玩家侧从左往右长、敌人侧从右往左长（镜像），两边的第一枚都贴自己那一侧的屏幕边。
        /// </summary>
        public void Layout()
        {
            LayoutGroup(_playerIcons, _playerGroup);
            LayoutGroup(_enemyIcons, _enemyGroup);
        }

        /// <summary>
        /// 把这一组的图标挂到<b>场景预置的槽位</b>上（M18）。
        ///
        /// <para><b>位置不在这里算</b>：<c>PlayerGroup</c> / <c>EnemyGroup</c> 下各摆了一排空节点
        /// <c>Slot0</c>…<c>Slot7</c>，坐标 / 尺寸全在 <c>BattleCanvas.prefab</c> 里 ——
        /// 想让图标摆多宽、间距多大、要不要换行，在 Hierarchy 里直接拖槽位就行，
        /// 不用改代码、也不用重跑构建器。本类只做「第 i 枚挂到第 i 个槽位」。</para>
        /// </summary>
        private void LayoutGroup(List<AuraIconView> icons, RectTransform group)
        {
            if (group == null)
            {
                return;
            }

            for (int i = 0; i < icons.Count; i++)
            {
                AuraIconView v = icons[i];
                if (v == null)
                {
                    continue;
                }

                if (v.IsDragging) continue;

                RectTransform slot = SlotAt(group, i);
                if (slot == null)
                {
                    continue;
                }

                RectTransform rt = (RectTransform)v.transform;

                if (rt.parent != slot)
                {
                    rt.SetParent(slot, false);
                }

                // 「家」= 槽位本身：拖拽松手后 AuraIconView.ReturnHome() 会把图标挂回这里，
                // 挂回组的话会落到组的 (0,0)、偏离槽位。
                v.HomeGroup = slot;

                // 这里只做「对齐槽位」——不写任何坐标常量（坐标在槽位自身上）。
                // 缩放任一 / 旋转归零是拖拽后的复位，与摆位无关。
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// 取第 <paramref name="index"/> 个槽位；编号不够时<b>退回最后一个</b> ——
        /// 宁可叠在一起，也不要悄悄滑到画面别处。
        /// </summary>
        private static RectTransform SlotAt(RectTransform group, int index)
        {
            for (int i = index; i >= 0; i--)
            {
                Transform t = group.Find("Slot" + i);
                if (t != null)
                {
                    return (RectTransform)t;
                }
            }

            return null;
        }

        /// <summary>
        /// 玩家侧默认位的第 <paramref name="index"/> 个槽位（M34）。
        ///
        /// <para>给「取消使用」那条路量<b>落点</b>用：那枚标记要飞回它回到默认位之后占的那一格，
        /// 所以调用方给的 <paramref name="index"/> 必须是<b>取消之后的可见序列</b>里的序号
        /// （被藏起来的那些不占格子 —— 见 <see cref="Collect"/> 第 3 步）。</para>
        /// </summary>
        public RectTransform PlayerSlotAt(int index)
        {
            return SlotAt(_playerGroup, index);
        }

        // ══════════════════════════════════════════════════════
        //  M36 · 「AI 用掉了一枚光环」的识别动画
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-23）：AI 使用光环时也应当有一个光环移动的过程来帮玩家识别，
        //  移动到**怪物模型的右侧**。
        //
        //  为什么必须是一段动画：怪物用手里的光环时，屏幕上原来只有提示条一行字
        //  （「光环 seat1 石盾 → AtkPower +2 作用于 —」）—— 玩家既看不出「哪一枚被用掉了」，
        //  也看不出「是怪物用的」。这一趟飞行把「哪一枚」与「谁在用」用同一个动作说完：
        //  那一枚**从它在敌方光环行里的位置**出发，飞到怪物模型右侧，停一下再淡出。
        //
        //  ⚠ 演出用的图标**单独一个池子**（_castPool），绝不与 Bind/Collect 用的 _pool 混：
        //    混在一起的话下一次 Bind 会把这枚正在飞的图标当成「没配上的」回收掉。
        //  ⚠ 图标模板上唯一接射线的是外框，演出图标必须把它关掉（见 AuraIconView.SetRaycastTarget）
        //    —— 它停的位置正压在敌方冷却区上方，接着射线就会吃掉玩家的点击。

        /// <summary>演出图标的池（与 <c>_pool</c> 严格分开，见上一段说明）。</summary>
        private readonly List<AuraIconView> _castPool = new List<AuraIconView>();

        /// <summary>正在演出（飞 / 停留 / 淡出）的图标 —— 重开一局时要把它们立刻收掉。</summary>
        private readonly List<AuraIconView> _casting = new List<AuraIconView>();

        /// <summary>
        /// 取某一枚指示物此刻占屏的世界坐标（给 <see cref="PlayCast"/> 当飞行起点）。
        ///
        /// <para>按<b>来源牌的 uid</b> 找而不是按 <c>MakeKey</c> 找：<c>AuraConsumedEvent</c> 里
        /// 没有「第几枚」这个信息（引擎只报来源牌与光环类型），而同一张牌上的两枚指示物
        /// 在屏幕上就并排挨着 —— 用哪一枚当起点，肉眼没有区别。</para>
        /// </summary>
        public bool TryGetIconWorld(int sourceUid, out Vector3 world)
        {
            world = Vector3.zero;

            return TryGetIconWorld(_playerIcons, sourceUid, ref world)
                   || TryGetIconWorld(_enemyIcons, sourceUid, ref world);
        }

        private static bool TryGetIconWorld(List<AuraIconView> icons, int sourceUid, ref Vector3 world)
        {
            for (int i = 0; i < icons.Count; i++)
            {
                AuraIconView v = icons[i];
                if (v == null || !v.gameObject.activeSelf || v.Data.SourceUid != sourceUid)
                {
                    continue;
                }

                world = v.transform.position;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 演出「某一方用掉了一枚光环」：那一枚从 <paramref name="fromWorld"/> 飞到
        /// <c>AuraCastSeat</c>，停一下、放大一点、再淡出（M36）。
        ///
        /// <para><c>AuraCastSeat</c> 缺失（老 prefab / 没跑构建菜单）时<b>安静地什么都不做</b>
        /// —— 少一段提示，不影响对局。</para>
        /// </summary>
        public void PlayCast(AuraIconData data, Vector3 fromWorld)
        {
            if (_iconTemplate == null || _castSeat == null)
            {
                return;
            }

            AuraIconView icon = TakeCastIcon();
            if (icon == null)
            {
                return;
            }

            icon.Bind(data, GlyphSprite(data, true), GlyphSprite(data, false));
            icon.SetRaycastTarget(false);
            icon.gameObject.SetActive(true);

            CanvasGroup group = CastGroup(icon);
            if (group != null)
            {
                group.alpha = 1f;
            }

            _casting.Add(icon);

            icon.GlideToPoint(DragSurface(), fromWorld, _castSeat.position,
                UiLayout.AuraCastFlySeconds, OnCastLanded);
        }

        /// <summary>把还在演出的图标（含还没回收的）全部立刻收掉 —— 重开一局 / 关界面用。</summary>
        public void ClearCasts()
        {
            StopAllCoroutines();

            // ⚠ 还在飞的那些**不在池里**，光遍历池子会漏掉它们（屏幕上就留着一枚不受管的图标）
            for (int i = 0; i < _casting.Count; i++)
            {
                AuraIconView v = _casting[i];
                if (v != null && !_castPool.Contains(v))
                {
                    _castPool.Add(v);
                }
            }

            _casting.Clear();

            for (int i = 0; i < _castPool.Count; i++)
            {
                AuraIconView v = _castPool[i];
                if (v == null)
                {
                    continue;
                }

                v.CancelFly();
                v.gameObject.SetActive(false);
            }
        }

        private AuraIconView TakeCastIcon()
        {
            AuraIconView v = null;

            while (_castPool.Count > 0 && v == null)
            {
                int last = _castPool.Count - 1;
                v = _castPool[last];
                _castPool.RemoveAt(last);

                if (v == null)
                {
                    continue;
                }

                // 池子里的图标可能还挂着上一次的锚点 / 缩放假象，绑定前先归零
                RectTransform rt = (RectTransform)v.transform;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                CanvasGroup g = CastGroup(v);
                if (g != null)
                {
                    g.alpha = 1f;
                }
            }

            if (v == null)
            {
                v = Instantiate(_iconTemplate, transform);
                v.name = "AuraCastIcon";
                v.Host = null;                                            // 演出图标不接受手势
                v.DragLayer = null;
                CastGroup(v);                                             // 顺手把 CanvasGroup 备好
            }

            return v;
        }

        /// <summary>演出图标上的 <see cref="CanvasGroup"/>（模板没有，运行时补一个；淡出靠它）。</summary>
        private static CanvasGroup CastGroup(AuraIconView icon)
        {
            if (icon == null)
            {
                return null;
            }

            CanvasGroup group = icon.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = icon.gameObject.AddComponent<CanvasGroup>();
            }

            return group;
        }

        private void OnCastLanded(AuraIconView icon)
        {
            if (icon == null || !_casting.Contains(icon))
            {
                // 落地前被 ClearCasts 收走了 → 什么都不做（它已经在池子里了）
                return;
            }

            RectTransform rt = (RectTransform)icon.transform;
            rt.localScale = Vector3.one * UiLayout.AuraCastLandScale;

            StartCoroutine(CoCastHold(icon));
        }

        /// <summary>落地之后：停留 → 淡出 → 回收。</summary>
        private IEnumerator CoCastHold(AuraIconView icon)
        {
            yield return new WaitForSecondsRealtime(UiLayout.AuraCastHoldSeconds);

            if (icon == null || !_casting.Contains(icon))
            {
                yield break;
            }

            CanvasGroup group = CastGroup(icon);
            if (group != null)
            {
                TweenCanvasAlpha fade = CastFadeFor(icon);
                fade.Setup(group.alpha, 0f, UiLayout.AuraCastFadeSeconds, UiEaseKind.OutQuad);
                fade.Play();

                while (fade.IsPlaying)
                {
                    yield return null;
                }
            }

            if (icon != null)
            {
                _casting.Remove(icon);
                icon.gameObject.SetActive(false);
                _castPool.Add(icon);
            }
        }

        /// <summary>缓存的淡出补间（一个图标一支，反复复用）。</summary>
        private readonly Dictionary<AuraIconView, TweenCanvasAlpha> _castFades =
            new Dictionary<AuraIconView, TweenCanvasAlpha>();

        private TweenCanvasAlpha CastFadeFor(AuraIconView icon)
        {
            TweenCanvasAlpha fade;
            if (_castFades.TryGetValue(icon, out fade) && fade != null)
            {
                return fade;
            }

            fade = icon.GetComponent<TweenCanvasAlpha>();
            if (fade == null)
            {
                fade = icon.gameObject.AddComponent<TweenCanvasAlpha>();
            }

            _castFades[icon] = fade;
            return fade;
        }

        // ══════════════════════════════════════════════════════
        //  手势转发（IAuraIconHost）
        // ══════════════════════════════════════════════════════

        public void OnAuraPress(AuraIconView icon)
        {
            HideTip();
        }

        public void OnAuraLongPress(AuraIconView icon)
        {
            if (icon == null || _tip == null)
            {
                return;
            }

            _tipKey = icon.Data.Key;
            _tip.Show(icon.Data, TipAnchor(icon));
        }

        public void OnAuraDragBegin(AuraIconView icon, Vector2 screenPos)
        {
            _active = icon;
            HideTip();

            if (IconDragBegin != null)
            {
                IconDragBegin(icon.Data, screenPos);
            }
        }

        public void OnAuraDrag(AuraIconView icon, Vector2 screenPos)
        {
            if (IconDragMove != null)
            {
                IconDragMove(icon.Data, screenPos);
            }
        }

        public void OnAuraDrop(AuraIconView icon, Vector2 screenPos)
        {
            _active = null;
            Layout();

            if (IconDropped != null)
            {
                IconDropped(icon.Data, screenPos);
            }

            if (GestureEnded != null)
            {
                GestureEnded();
            }
        }

        public void OnAuraRelease(AuraIconView icon)
        {
            HideTip();

            if (_active == null && GestureEnded != null)
            {
                GestureEnded();
            }
        }

        public void HideTip()
        {
            _tipKey = 0;

            if (_tip != null)
            {
                _tip.Hide();
            }
        }

        /// <summary>说明浮层挂在图标「下沿」的位置（本层局部坐标）—— 图标行贴着画布顶，浮层只能往下摆。</summary>
        private Vector2 TipAnchor(AuraIconView icon)
        {
            RectTransform root = (RectTransform)transform;
            RectTransform iconRt = (RectTransform)icon.transform;

            Vector3 bottomCenter = iconRt.TransformPoint(new Vector3(iconRt.rect.center.x, iconRt.rect.yMin, 0f));
            Vector2 iconLocal = root.InverseTransformPoint(bottomCenter);
            return iconLocal + new Vector2(0f, -UiLayout.BuffTipBelow);
        }
    }

}
