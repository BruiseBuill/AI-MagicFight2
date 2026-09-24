using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 手牌区左侧的「已准备光环」标记行（M16，M19 改落区，M21 改摆位与取消手势）。
    ///
    /// <para><b>用户口径（2026-09-20 定稿）</b>：不设光环选择环节 —— 玩家在进攻 / 防御时
    /// <b>随时</b>把 HudBuff 里的光环图标拖到<b>自己的手牌区</b>即视为「这枚光环现在准备用」；
    /// 原图标从 HudBuff 消失，标记<b>飞</b>到<b>手牌区左侧</b>排好（让玩家记住自己准备了什么），
    /// 出牌那一刻随牌一并提交。</para>
    ///
    /// <para><b>取消 = 再拖一下那枚标记</b>（M21 改）：一进入拖拽就当作取消，
    /// 标记连同 HudBuff 里的原图标一起复位回默认位（角色血量徽标旁边那行）。
    /// M19 用的是「把标记拖出判定区」那套落点判定，用户觉得别扭 —— 标记本来就摆在
    /// 判定区外面（手牌区里），「拖出去」多半是误触。改成「拖一下就取消」后语义干脆，
    /// 也不必再持有判定区矩形。</para>
    ///
    /// <para><b>落区与「出牌判定区」不是同一个</b>：出牌拖中央 <c>PlayZone</c>，
    /// 光环拖底部 <c>HandArea</c> —— 判定都在 <see cref="BattleUi"/> 里做，本类只负责显示与取消。</para>
    ///
    /// <para><b>本类不做规则判断</b>（铁律 3）：「这枚能不能用」由 <see cref="BattleUi"/>
    /// 拿引擎给的 Options 比对后决定 —— 它只在 <c>_auraOptions</c> 里查得到时才把图标递进来。
    /// 本类只负责：摆标记、播放「飞入」、转发「再拖一下取消」这一个手势。</para>
    ///
    /// <para><b>节点在场景里预建</b>（<c>BattleCanvas/PreparedAura</c>，由 M8 构建器建出来）：
    /// 它是「把一枚光环拖到手牌区」这条交互的全部可视化，用户要能在 Hierarchy 里
    /// 直接看见、直接调（颜色 / 尺寸 / 层级），所以<b>不运行时 new</b>。
    /// 本类只在接线时接一个引用（图标模板），再把标记摆到槽位上 ——
    /// 槽位（<c>Slot0…Slot7</c>）的坐标由构建器按 <c>UiLayout.PreparedAura*</c> 生成，
    /// 本类一个坐标常量都不写。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PreparedAuraView : MonoBehaviour, IAuraIconHost
    {
        /// <summary>一枚准备中的光环被取消使用（再拖一下那枚标记）。</summary>
        public event Action<AuraIconData> Cancelled;

        /// <summary>
        /// 「取消使用」的那一枚标记<b>飞回默认位并且落地了</b>（参数 = 指示物键，M34）。
        ///
        /// <para>调用方（<see cref="BattleUi"/>）拿它把 HudBuff 里那枚原图标放出来 ——
        /// 落地之前那枚要一直隐着，否则屏幕上是「图标已经回到原位」+「标记还在往那儿飞」
        /// 两份同时存在。落地是瞬间切换的：标记与图标是同一个模板、同一个槽位，
        /// 尺寸/图案完全一致，接缝看不见。</para>
        /// </summary>
        public event Action<long> ReturnLanded;

        private RectTransform _surface;      // 铺满画布的自由层（标记都挂在这下面）
        private AuraIconView _template;

        private readonly List<AuraIconView> _shown = new List<AuraIconView>();
        private readonly List<AuraIconView> _pool = new List<AuraIconView>();

        /// <summary>正在飞回默认位的那几枚（已从 <see cref="_shown"/> 摘掉，落地才收进池子）。</summary>
        private readonly List<AuraIconView> _returning = new List<AuraIconView>();

        /// <summary>飞回中的图标 → 它代表的指示物键（落地事件要带回去）。</summary>
        private readonly Dictionary<AuraIconView, long> _returningKeys =
            new Dictionary<AuraIconView, long>();

        /// <summary>下一枚新标记从哪个屏幕点飞来（一次性，见 <see cref="SetSpawnOrigin"/>）。</summary>
        private long _spawnKey;
        private Vector2 _spawnScreen;
        private bool _spawnPending;

        /// <summary>当前有几枚标记（自测 / 探针用）。</summary>
        public int MarkerCount
        {
            get { return _shown.Count; }
        }

        // ══════════════════════════════════════════════════════
        //  接线
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (_surface == null)
            {
                _surface = (RectTransform)transform;
            }
        }

        /// <summary>
        /// 接上图标模板（幂等，重复调用只是把引用重指一遍）。
        ///
        /// <para><b>为什么这个引用必须运行时给</b>：模板来自
        /// <see cref="HudBuffView.IconTemplate"/> —— 是「引用传递」，不是「造新东西」，
        /// 所以节点预建之后这里只剩接线。</para>
        ///
        /// <para><b>M21 起不再收判定区矩形</b>：取消改成「再拖一下标记」，判定区与标记无关了
        /// （顺手删掉 <c>ZoneHighlight</c> 那层「保留区高亮」—— 它的含义就是「标记必须待在这里」，
        /// 现在没有这个约束）。</para>
        /// </summary>
        public void Configure(AuraIconView template)
        {
            if (_surface == null)
            {
                _surface = (RectTransform)transform;
            }

            _template = template;
            Layout();
        }

        // ══════════════════════════════════════════════════════
        //  绑定
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 指定「下一枚新建的标记从哪个屏幕点飞过来」（M19）。
        ///
        /// <para><see cref="BattleUi"/> 在松手那一刻调一次，紧接着 <see cref="Bind"/> ——
        /// 起点就是玩家松手的落点，于是读感是「这枚图标从手里飞到了判定区左侧」，
        /// 而不是凭空出现在槽位上。不调就照旧原地出现（重建 / 恢复时走这条）。</para>
        /// </summary>
        public void SetSpawnOrigin(long key, Vector2 screenPos)
        {
            _spawnKey = key;
            _spawnScreen = screenPos;
            _spawnPending = true;
        }

        /// <summary>重建标记（幂等；下标对应，顺序 = 准备的先后）。</summary>
        public void Bind(IReadOnlyList<AuraIconData> prepared)
        {
            int want = prepared == null ? 0 : prepared.Count;

            // 多的先收回池子（从尾巴收，下标才对得上）
            while (_shown.Count > want)
            {
                int last = _shown.Count - 1;
                AuraIconView extra = _shown[last];
                _shown.RemoveAt(last);
                Release(extra);
            }

            for (int i = 0; i < want; i++)
            {
                AuraIconView v;
                if (i < _shown.Count)
                {
                    v = _shown[i];
                    if (v == null)
                    {
                        continue;
                    }

                    // 正被拖着 / 正飞着的那一枚不要打断：重建发生在引擎重发本拍之后，
                    // 那时玩家可能已经把标记拎在手上（或上一枚还在飞）。
                    if (v.IsDragging || v.IsFlying)
                    {
                        continue;
                    }
                }
                else
                {
                    v = Take();
                    if (v == null)
                    {
                        continue;
                    }

                    _shown.Add(v);
                }

                v.Bind(prepared[i], prepared[i].GlyphSprite(true), prepared[i].GlyphSprite(false));

                // ⚠ 必须在 Bind **之后**置位：Bind 末尾会调 ResetState() 把状态清零，
                //   而 ResetState 也负责清 CancelOnDrag（池化复用防串味）。
                //   写在 Take() 里会被这一句无声地冲掉 —— 实测表现是「标记照样被拖着走」。
                v.CancelOnDrag = true;
            }

            Layout();
        }

        /// <summary>
        /// 清空标记（决策结束 / 出牌之后）。
        ///
        /// <para>⚠ 连同「正在飞回默认位」的那几枚一起收（<see cref="AbandonReturns"/>）：
        /// 它们已经不在 <see cref="_shown"/> 里了，只 <c>Bind(null)</c> 是收不掉的 ——
        /// 会留下一枚没人管的图标在屏幕上飘到天荒地老。</para>
        /// </summary>
        public void Clear()
        {
            AbandonReturns();
            Bind(null);
        }

        // ══════════════════════════════════════════════════════
        //  M34 · 取消使用时「飞回默认位」
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把某一枚标记从准备位<b>滑回</b>它的默认位（<paramref name="target"/> = HudBuff 那一侧的槽位），
        /// 落地后收进池子并发 <see cref="ReturnLanded"/>。
        ///
        /// <para><b>用户口径（2026-09-23）</b>：取消使用时光环**从左侧立即消失、然后瞬间出现在原位**，
        /// 要求「为它做一个动画」。所以这一段不是「删掉一个图标」，而是「这一枚自己走回去」——
        /// 位移沿用飞入那套（OutCubic 位置），但<b>缩放两端都是 1</b>：它本来就该是这么大，
        /// 只是换了个位置。</para>
        ///
        /// <para>顺带把<b>其余标记往前补的那一格</b>也做成滑行（<c>AuraReflowSeconds</c>）：
        /// 不然取消中间那一枚时，后面几枚会瞬移一格，读感比取消本身还糙。</para>
        ///
        /// <para>返回 false = 没找到可飞的那一枚（或它正被拖着）—— 调用方按「当场收回」处理。</para>
        /// </summary>
        public bool FlyHome(long key, RectTransform target, float duration)
        {
            if (_surface == null || target == null)
            {
                return false;
            }

            int hit = -1;
            for (int i = 0; i < _shown.Count; i++)
            {
                if (_shown[i] != null && _shown[i].Key == key)
                {
                    hit = i;
                    break;
                }
            }

            if (hit < 0)
            {
                return false;
            }

            AuraIconView v = _shown[hit];
            if (v == null || v.IsDragging || v.IsFlying)
            {
                return false;
            }

            // 重排**之前**把其余几枚的世界坐标记下来：重排是瞬跳，滑行的起点只能是旧位置。
            var fromWorld = new Dictionary<AuraIconView, Vector3>();
            for (int i = 0; i < _shown.Count; i++)
            {
                if (_shown[i] != null)
                {
                    fromWorld[_shown[i]] = _shown[i].transform.position;
                }
            }

            _shown.RemoveAt(hit);
            Layout();

            for (int i = 0; i < _shown.Count; i++)
            {
                AuraIconView other = _shown[i];
                if (other == null || other.IsFlying)
                {
                    continue;
                }

                Vector3 old;
                if (!fromWorld.TryGetValue(other, out old))
                {
                    continue;
                }

                RectTransform slot = SlotAt(i);
                if (slot == null)
                {
                    continue;
                }

                other.GlideTo(_surface, old, slot, UiLayout.AuraReflowSeconds, null);
            }

            _returning.Add(v);
            _returningKeys[v] = key;
            v.GlideTo(_surface, target, duration, HandleReturnLanded);
            return true;
        }

        /// <summary>飞回落地：收进池子 + 通知外面「那枚指示物可以放回默认位了」。</summary>
        private void HandleReturnLanded(AuraIconView v)
        {
            if (v == null || !_returning.Remove(v))
            {
                return;
            }

            long key;
            bool has = _returningKeys.TryGetValue(v, out key);
            if (has)
            {
                _returningKeys.Remove(v);
            }

            Release(v);

            if (has && ReturnLanded != null)
            {
                ReturnLanded(key);
            }
        }

        /// <summary>
        /// 放弃所有正在飞回的标记：立刻收回池子并发落地事件。
        ///
        /// <para><b>为什么必须有一条这样的路径</b>：<see cref="AuraIconView.CancelFly"/> 会丢掉落地回调
        /// （被 <c>ResetState</c> 打断时只能这样），所以「等回调收尾」不能是唯一出路 ——
        /// 重开一局 / 决策结束正好落在 0.3 s 的飞行窗口里时，那枚图标就再也没人收了。</para>
        /// </summary>
        public void AbandonReturns()
        {
            while (_returning.Count > 0)
            {
                int last = _returning.Count - 1;
                AuraIconView v = _returning[last];
                _returning.RemoveAt(last);

                long key = 0;
                bool has = v != null && _returningKeys.TryGetValue(v, out key);
                if (v != null)
                {
                    _returningKeys.Remove(v);
                    v.CancelFly();
                    Release(v);
                }

                if (has && ReturnLanded != null)
                {
                    ReturnLanded(key);
                }
            }
        }

        /// <summary>正在飞回默认位的标记数（探针 / 自测用）。</summary>
        public int ReturningCount
        {
            get { return _returning.Count; }
        }

        // ══════════════════════════════════════════════════════
        //  排布
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把标记挂到<b>手牌区左侧的槽位</b>上。
        ///
        /// <para><b>位置不在这里算</b>：<c>PreparedAura</c> 下的空节点 <c>Slot0</c>…<c>Slot7</c>
        /// 由 `BattleUiBuilder.BuildPreparedBar` 按 <c>UiLayout.PreparedAuraX</c> /
        /// <c>PreparedAuraRowCenterY</c> 生成（M21）—— 本类只做「第 i 枚挂到第 i 个槽位」，
        /// 一个坐标常量都不写。</para>
        ///
        /// <para>⚠ M18 时这排槽位是手摆在 Prefab 里的，而 M8 的 <c>Cleanup</c> 会把整个
        /// <c>PreparedAura</c> 删掉重建 —— 于是槽位没了，<see cref="SlotAt"/> 一律返回 null、
        /// 标记停在松手的位置不动。这也是「标记要跟着判定区走」变成「必须由构建器生成」的原因。</para>
        /// </summary>
        public void Layout()
        {
            if (_surface == null)
            {
                return;
            }

            for (int i = 0; i < _shown.Count; i++)
            {
                AuraIconView v = _shown[i];
                if (v == null || v.IsDragging)
                {
                    continue;
                }

                RectTransform slot = SlotAt(i);
                if (slot == null)
                {
                    continue;
                }

                // 刚被拖进来的那一枚：交给飞行代码，落点由它自己收（见 AuraIconView.FlyIn）。
                // 在这里先 continue，否则下面那段「对齐槽位」会把坐标按成 0，图标就要从槽位起飞了。
                if (v.IsFlying)
                {
                    continue;
                }

                if (_spawnPending && v.Key == _spawnKey)
                {
                    _spawnPending = false;
                    v.HomeGroup = slot;
                    v.FlyIn(_surface, ScreenToWorld(_spawnScreen), slot, UiLayout.AuraFlySeconds);
                    continue;
                }

                RectTransform rt = (RectTransform)v.transform;

                if (rt.parent != slot)
                {
                    rt.SetParent(slot, false);
                }

                // 「家」= 槽位本身（图标归位 / 被打断复位时挂回这里）。
                v.HomeGroup = slot;

                // 只做「对齐槽位」——坐标在槽位自身上，这里不写。
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }

            // 没等到对应图标就把「待飞」标掉：留着会在下一次重建时把另一枚图标莫名其妙地拽飞。
            _spawnPending = false;
        }

        /// <summary>屏幕点 → <see cref="_surface"/> 的世界坐标（飞行起点用）。</summary>
        private Vector3 ScreenToWorld(Vector2 screenPos)
        {
            Vector3 world;
            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _surface, screenPos, null, out world);
            return world;
        }

        /// <summary>取第 <paramref name="index"/> 个槽位；编号不够时退回最后一个。</summary>
        private RectTransform SlotAt(int index)
        {
            if (_surface == null)
            {
                return null;
            }

            for (int i = index; i >= 0; i--)
            {
                Transform t = _surface.Find("Slot" + i);
                if (t != null)
                {
                    return (RectTransform)t;
                }
            }

            return null;
        }

        private AuraIconView Take()
        {
            AuraIconView v = null;

            if (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                v = _pool[last];
                _pool.RemoveAt(last);
                v.gameObject.SetActive(true);
            }
            else
            {
                if (_template == null)
                {
                    return null;
                }

                v = Instantiate(_template, _surface);
                v.name = "PreparedAuraIcon";
                v.Host = this;
                v.DragLayer = _surface;
                v.HomeGroup = _surface;
                v.gameObject.SetActive(true);
            }

            // ⚠ CancelOnDrag（「再拖一下就取消」）**不在这里**置位 —— 见 Bind() 里的说明。
            return v;
        }

        private void Release(AuraIconView v)
        {
            if (v == null)
            {
                return;
            }

            v.ResetState();
            v.gameObject.SetActive(false);
            _pool.Add(v);
        }

        // ══════════════════════════════════════════════════════
        //  手势（IAuraIconHost）—— 标记只接「再拖一下 = 取消」
        // ══════════════════════════════════════════════════════

        public void OnAuraPress(AuraIconView icon)
        {
        }

        public void OnAuraLongPress(AuraIconView icon)
        {
            // 标记是「准备中」的临时状态，长按说明由 HudBuff 那枚原图标负责 ——
            // 这里再弹一层浮层会挡在手牌区正上方。
        }

        /// <summary>
        /// 拖拽开始 = <b>取消使用</b>（用户 2026-09-20 口径）。
        ///
        /// <para>标记不是可以自由摆放的东西 —— 它要么「待在手牌区左侧」表示已准备，
        /// 要么不存在。所以「又拖了一下」这个动作本身就是取消意图，不需要再看落点。
        /// 取消后原图标回到 HudBuff 的默认位（角色血量徽标旁那行），标记由随后的
        /// <see cref="Bind"/> 收走。</para>
        ///
        /// <para>这里只发事件、不动坐标：<see cref="BattleUi"/> 收到之后会走
        /// 「从准备集合里删掉 → 重发本拍决策」那条路，最后回到
        /// <see cref="Bind"/> 把这一枚收进池子。拖拽态由
        /// <see cref="AuraIconView.CancelOnDrag"/> 保证没被打开（图标不会被拎着走）。</para>
        /// </summary>
        public void OnAuraDragBegin(AuraIconView icon, Vector2 screenPos)
        {
            if (icon == null)
            {
                return;
            }

            if (Cancelled != null)
            {
                Cancelled(icon.Data);
            }
        }

        public void OnAuraDrag(AuraIconView icon, Vector2 screenPos)
        {
            // 标记不会真的被拖动（见 CancelOnDrag）——这个方法只是接口要求。
        }

        public void OnAuraDrop(AuraIconView icon, Vector2 screenPos)
        {
            Layout();
        }

        public void OnAuraRelease(AuraIconView icon)
        {
        }
    }
}
