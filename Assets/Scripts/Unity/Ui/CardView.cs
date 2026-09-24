using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 只读快照驱动的完整牌面。
    ///
    /// <para><b>M16 起是「组成式卡面」</b>：插画铺满整卡，卡名 / 力量 / 冷却 / 效果文字
    /// 一律由 TMP 现场渲染（效果文字里的 α / β / γ 走 <see cref="TriggerSpriteLibrary"/> 的
    /// 图文混排）。不再像 M12–M15 那样把 `Art/Cards` 里那张 760×1056 的**成品卡面整图**
    /// 塞进一个 <see cref="Image"/> —— 缩到 196 px 之后图上烘焙的字只有 ~8 px，等于没有。</para>
    ///
    /// <para><b>手牌、冷却迷你卡、放大查看共用同一棵节点树</b>（<see cref="ViewMode"/> 只留作
    /// 「这张牌画在哪个区域」的标记）：旧版靠「手牌一套 / 迷你一套 + 迷你只留一张脸」来省事，
    /// 代价是两套版式要各维护一遍，改一处漏一处。现在三处都显示同一批信息，坐标由 UiLayout
    /// 的设计空间统一给，<b>卡面上的数一律是卡表里的静态值</b>（力量 = 卡面力量、冷却 = 基础冷却）——
    /// 玩家在哪一处看到的都是同一张牌的样子（用户口径 2026-09-20）。</para>
    ///
    /// <para><b>数值口径来自快照，卡名与效果文字来自卡表</b>：力量必须是快照的
    /// （模仿复制成功后会被改写、未复制时显示 X），而效果文字是静态的规则文案，
    /// 卡表里才有 —— 快照不背这个包袱。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardView : MonoBehaviour
    {
        public enum ViewMode
        {
            Hand = 0,
            Mini = 1,
        }

        // ── 组成式卡面的零件（两种形态共用）──────────────────
        [Header("卡面零件")]

        /// <summary>插画（铺满整卡，圆角由插画槽的遮罩裁）。</summary>
        [SerializeField] private Image _art;

        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _power;

        /// <summary>冷却徽标里的数：<b>基础冷却值</b>（三处形态一致，2026-09-20）。</summary>
        [SerializeField] private TMP_Text _cool;

        [SerializeField] private TMP_Text _effect;

        // ── 交互态 ───────────────────────────────────────────
        [Header("交互态")]
        /// <summary>选中高亮边框（由 4 根细条拼成，不是单张 Image）。</summary>
        [SerializeField] private GameObject _glow;
        [SerializeField] private GameObject _dim;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Button _button;

        /// <summary>点击回调（M8 接出牌 / 选目标）。由 <see cref="_button"/> 触发。</summary>
        public event Action<CardView> Clicked;

        /// <summary>当前绑定的卡（默认快照 = 空）。</summary>
        public CardSnapshot Card { get; private set; }

        public ViewMode Mode { get; private set; }

        /// <summary>在所在区域里的槽位序号（手牌从左到右 / 冷却区顺序）。</summary>
        public int SlotIndex { get; set; }

        public bool HasCard { get; private set; }

        /// <summary>「这张牌现在能不能出」（由 <see cref="SetPlayable"/> 一路透传过来）。</summary>
        private bool _interactable = true;

        /// <summary>「这一次指针抬起不算点击」（拖拽中 / 长按已触发）。</summary>
        private bool _gestureHold;

        private void Awake()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(() =>
                {
                    Action<CardView> handler = Clicked;
                    if (handler != null)
                    {
                        handler(this);
                    }
                });
            }
        }

        // ══════════════════════════════════════════════════════
        //  绑定
        // ══════════════════════════════════════════════════════

        public void Bind(CardSnapshot card, ViewMode mode, int slotIndex)
        {
            Card = card;
            Mode = mode;
            SlotIndex = slotIndex;
            HasCard = true;

            CardDef def = Lookup(card.CardId);

            ApplyIllustration(card.CardId);

            if (_name != null)
            {
                _name.text = NameOf(card, def);
            }

            if (_power != null)
            {
                _power.text = card.PowerText;
            }

            // 力量徽标的颜色是「卡面原色」，每次重绑都复位一次 —— 之后由
            // SetPowerPreview 按需要在它上面盖一层高亮色（M24 #4）。
            ApplyPowerColor();

            if (_effect != null)
            {
                _effect.text = TriggerSpriteLibrary.EffectRichText(def);
            }

            RefreshCooldown(card);

            SetSelected(false);
            SetDimmed(false);
        }

        /// <summary>
        /// 刷新冷却数（冷却区每次 tick 都会调）。
        ///
        /// <para>刻意不和 <see cref="Bind"/> 合成一个方法：冷却区一帧里可能重刷整列，
        /// 在那里重查卡表、重排效果文字是纯浪费 —— 插画、卡名、效果文字都不会变。</para>
        ///
        /// <para><b>2026-09-20 用户口径</b>：显示的<b>不是剩余冷却，而是这张牌的基础冷却值</b>——
        /// 卡面上写多少就是多少，玩家在哪一处看到的都一样。想在冷却区里判断「还剩几回合」
        /// 看的是它<b>排在第几行</b>（行 = 4 − 剩余冷却，见 <c>CooldownView.BindGrouped</c>），
        /// 不需要把那个数画在卡上。</para>
        /// </summary>
        public void RefreshCooldown(CardSnapshot card)
        {
            Card = card;

            if (_cool != null)
            {
                _cool.text = card.BaseCooldown.ToString();
            }

            // 力量数值可能被「已准备光环」的预览改写（M24 #4）——重刷这张卡时按当前
            // 预览值重下一次，否则冷却区一 tick 就把手牌上那份预览冲成卡面原值。
            ApplyPowerText();
        }

        // ══════════════════════════════════════════════════════
        //  取数与铺图
        // ══════════════════════════════════════════════════════

        private static CardDef Lookup(string cardId)
        {
            CardDef def;
            return !string.IsNullOrEmpty(cardId) && CardLibrary.TryGet(cardId, out def) ? def : null;
        }

        private static string NameOf(CardSnapshot card, CardDef def)
        {
            if (!string.IsNullOrEmpty(card.Name))
            {
                return card.Name;
            }

            return def == null ? string.Empty : def.Name;
        }

        /// <summary>
        /// 把插画铺满插画槽。
        ///
        /// <para>用 <b>cover</b>（填满 + 溢出裁掉）而不是拉伸：插画原图是 784×1168（比例 0.671），
        /// 卡面是 0.713 —— 拉满会横向挤 6%，插画是整屏铺底，这点形变落在斜向构图上看得出来。
        /// 溢出部分由插画槽外层的圆角遮罩吃掉。</para>
        ///
        /// <para>退化链：插画原图 → 成品卡面 → 纯色底。少了任何一环都不会让整张卡消失。</para>
        /// </summary>
        private void ApplyIllustration(string cardId)
        {
            if (_art == null)
            {
                return;
            }

            CardArtLibrary lib = CardArtLibrary.Instance;
            Sprite sprite = lib == null ? null : lib.GetIllustration(cardId);

            if (sprite == null && lib != null)
            {
                sprite = lib.GetArt(cardId);
            }

            _art.sprite = sprite;
            _art.preserveAspect = false;
            _art.color = sprite == null ? UiTheme.MiniCardFace : Color.white;

            if (sprite == null)
            {
                return;
            }

            // 基准尺寸取「设计空间里的插画槽尺寸」，再用当前实际尺寸按比例还原 ——
            // 这样无论是手牌还是迷你卡（同一棵树、不同缩放比）都算得出正确的 cover 尺寸
            RectTransform rt = _art.rectTransform;
            Vector2 slot = rt.sizeDelta;
            if (slot.x <= 0f || slot.y <= 0f)
            {
                return;
            }

            float spriteAspect = sprite.rect.width / sprite.rect.height;
            float slotAspect = slot.x / slot.y;

            rt.sizeDelta = spriteAspect > slotAspect
                ? new Vector2(slot.y * spriteAspect, slot.y)   // 图更宽 → 按高对齐，横向溢出
                : new Vector2(slot.x, slot.x / spriteAspect);  // 图更高 → 按宽对齐，竖向溢出
        }

        public bool PresentationHidden { get; private set; }

        public void SetPresentationHidden(bool hidden)
        {
            PresentationHidden = hidden;
            if (_group != null)
            {
                _group.alpha = hidden ? 0f : 1f;
                _group.blocksRaycasts = !hidden;
            }
        }

        public void Clear()
        {
            SetPresentationHidden(false);
            HasCard = false;
            Card = default(CardSnapshot);
            SetGestureHold(false);
            _powerPreview = 0;

            if (_art != null)
            {
                _art.sprite = null;
            }

            TMP_Text[] texts = { _name, _power, _cool, _effect };
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    texts[i].text = string.Empty;
                }
            }

            SetSelected(false);
            SetDimmed(false);
        }

        // ══════════════════════════════════════════════════════
        //  交互态
        // ══════════════════════════════════════════════════════

        /// <summary>是否可点（不可点时压暗并吃掉点击）。</summary>
        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            ApplyButtonState();
            SetDimmed(!interactable && HasCard);
        }

        /// <summary>
        /// <b>临时</b>吃掉点击（M12 拖拽 / 长按用）。
        ///
        /// <para>实现方式是<b>停用 Button 组件</b>而不是把它设成 <c>interactable = false</c>：
        /// <c>Button.Press()</c> 的第一句就是 <c>if (!IsActive() || !IsInteractable()) return;</c>，
        /// 停用组件同样能拦住 <c>onClick</c>，而且<b>不会触发「不可用态」的颜色切换</b> ——
        /// 否则拖到一半整张牌会闪一下灰，像是不让打了。</para>
        ///
        /// <para>与 <see cref="SetInteractable"/> 正交：那个管「这张牌现在能不能出」，
        /// 这个管「这一次指针抬起不算点击」。两者任一为假，点击都不生效。</para>
        /// </summary>
        public void SetGestureHold(bool hold)
        {
            _gestureHold = hold;
            ApplyButtonState();
        }

        private void ApplyButtonState()
        {
            if (_button != null)
            {
                _button.enabled = !_gestureHold;
                _button.interactable = _interactable;
            }
        }

        /// <summary>
        /// 选中高亮（辉光边框）。<b>幂等</b>：只在状态真的翻转时才动节点 ——
        /// 手牌区每帧都要重算选中态，直接 SetActive 会是白费的每帧写操作。
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (_glow != null && _glow.activeSelf != selected)
            {
                _glow.SetActive(selected);
            }
        }

        public void SetDimmed(bool dimmed)
        {
            if (_dim != null && _dim.activeSelf != dimmed)
            {
                _dim.SetActive(dimmed);
            }
        }

        public void SetAlpha(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = alpha;
            }
        }

        // ══════════════════════════════════════════════════════
        //  力量预览（M24 #4）
        // ══════════════════════════════════════════════════════
        //
        //  用户口径（2026-09-20）：把「增加进攻 / 防御力量」的光环拖到准备区之后，
        //  手牌上显示的力量数值要**同步**变过去，让玩家在出牌前就看得见「加上这枚光环
        //  能涨到多少」。
        //
        //  ⚠ 这是**纯表现态**，不改任何规则数据：
        //    · 光环的加值本来就不永久改造牌面（`rules/01-规则基线.md` §6.2
        //      「加成仅作用于当时正在打出的那一张牌，不永久改造牌面」）；
        //    · 真正加不加、加几张，仍然只由引擎在提交那一刻按 `Options` 决定
        //      —— `_atk.AuraBonus` 才是唯一算数的地方（铁律 3）。
        //  所以这里只是「把玩家自己准备的东西画出来」，读的还是引擎给的
        //  已准备光环合值（`BattleUi` 从 `_preparedKeys` 对应的选项里数出来的），
        //  本类不做任何合法性判断。

        /// <summary>力量加值预览（0 = 无预览，按卡面原值显示）。</summary>
        private int _powerPreview;

        /// <summary>卡面原色（由构建器写进 Prefab，这里只读一次备用）。</summary>
        private Color _powerColor = Color.white;

        private bool _powerColorCaptured;

        /// <summary>
        /// 设置力量预览：显示 <c>原值 + bonus</c> 并染高亮色；传 0 恢复卡面原值与原色。
        ///
        /// <para>「模仿」的力量文本是 <c>X</c>，加值算不出来 —— 那种情况保持原样不预览，
        /// 免得画出「X+3」这种卡表里并不存在的东西。</para>
        /// </summary>
        public void SetPowerPreview(int bonus)
        {
            _powerPreview = bonus;
            ApplyPowerText();
        }

        private void ApplyPowerText()
        {
            if (_power == null)
            {
                return;
            }

            if (_powerPreview > 0 && HasCard && IsNumericPower(Card.PowerText))
            {
                _power.text = (Card.Power + _powerPreview).ToString();
                ApplyPowerColor();
                return;
            }

            _power.text = HasCard ? Card.PowerText : string.Empty;
            ApplyPowerColor();
        }

        /// <summary>
        /// 高亮 / 复位力量徽标文字色。
        ///
        /// <para>颜色取自 <see cref="UiTheme"/>（唯一颜色来源）；原色在第一次用到时
        /// 从 Prefab 上读一次并记住 —— 构建器写进去的是什么色，复位就回什么色，
        /// 不在代码里另写一份「原色」常量（那会让改 Prefab 失效）。</para>
        /// </summary>
        private void ApplyPowerColor()
        {
            if (_power == null)
            {
                return;
            }

            if (!_powerColorCaptured)
            {
                _powerColor = _power.color;
                _powerColorCaptured = true;
            }

            _power.color = _powerPreview > 0 && HasCard && IsNumericPower(Card.PowerText)
                ? UiTheme.PowerPreview
                : _powerColor;
        }

        /// <summary>力量文本是不是纯数字（「模仿」是 X，不参与预览）。</summary>
        private static bool IsNumericPower(string powerText)
        {
            if (string.IsNullOrEmpty(powerText))
            {
                return false;
            }

            for (int i = 0; i < powerText.Length; i++)
            {
                if (powerText[i] < '0' || powerText[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
