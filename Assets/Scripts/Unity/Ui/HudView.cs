using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 顶部状态栏 + 能量球的视图（M11）。
    ///
    /// <para><b>栏底是擦过字的成品图</b>：原图集上烘着「旅者 / 铁卫 / 72-80 / 125」，
    /// 这几个是动态值，切图时用扩散补洞擦掉了（见 `Tools/art-audit/slice_ui_kit.py`），
    /// 这里再用 TMP 按原位置重排。</para>
    ///
    /// <para><b>槽位映射</b>（本作没有「金币」概念，用最接近的资源顶替，别当成参考图的抄写）：</para>
    /// <list type="bullet">
    /// <item>❤ 数字 = <c>Hp/MaxHp</c>（规则固定 4 点，显示成 3/4 而不是参考图的 72/80）</item>
    /// <item>💰 数字 = 冷却区里未使用的光环指示物数（本作唯一的「可消耗资源」）</item>
    /// <item>名字下的小字 = 手牌数（对方手牌数本来就是公开信息）</item>
    /// <item>能量球 = 当前进攻出牌次数 / 最大出牌次数（1）</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _subtitle;
        [SerializeField] private TMP_Text _hpNumber;
        [SerializeField] private TMP_Text _resourceNumber;
        [SerializeField] private TMP_Text _orbNumber;
        [SerializeField] private Button _gearButton;

        // ⚠ M15 删掉了这里的 _buffIcons / BindBuffSlots：那 5 个状态图标槽是占位
        //   （语义一直是「前 2 个常态 + 未用光环数点亮」），用户要求把光环<b>真正</b>
        //   摆出来之后，它们就变成了同一件事的第二套假实现 —— 见 Ui/HudBuffView.cs。

        /// <summary>齿轮 = 设置入口（当前接的是战斗日志开关，M10 接真正的设置面板）。</summary>
        public event Action GearClicked;

        public void Configure(TMP_Text name, TMP_Text subtitle, TMP_Text hpNumber,
                              TMP_Text resourceNumber, TMP_Text orbNumber,
                              Button gearButton)
        {
            _name = name;
            _subtitle = subtitle;
            _hpNumber = hpNumber;
            _resourceNumber = resourceNumber;
            _orbNumber = orbNumber;
            SetGear(gearButton);
        }

        /// <summary>
        /// 齿轮的点击<b>在运行时自己再接一次</b>（M34）。
        ///
        /// <para><b>这是一个「零报错的错」</b>：构建器（M11 菜单）在<b>编辑模式</b>里走
        /// <see cref="Configure"/> → <see cref="SetGear"/> → <c>gear.onClick.AddListener(OnGear)</c>，
        /// 而 <c>UnityEvent</c> 的运行时监听器（<c>m_RuntimeCalls</c>）<b>不是序列化数据</b> ——
        /// 存 Prefab / 存场景时被直接丢掉。于是进 Play 之后：</para>
        ///
        /// <list type="bullet">
        /// <item>GearButton 在、<c>interactable</c> 在、<c>targetGraphic</c> 在、射线在（点上去只命中它）、
        /// <c>HudView._gearButton</c> 也接得好好的 —— 每一项检查都通过；</item>
        /// <item>只有 <c>onClick</c> 上是 <b>0 个监听器</b>，所以点齿轮<b>什么都不会发生</b>。</item>
        /// </list>
        ///
        /// <para>2026-09-23 用户报的就是这条（「点齿轮游戏没有重新启动」）。
        /// 判据：进 Play 读 <c>gear.onClick</c> 的运行时调用数；修法就是这里 ——
        /// <b>序列化字段才是真值来源</b>，凡「把监听器交给构建器去接」的写法一律会在存盘时蒸发。</para>
        ///
        /// <para><see cref="SetGear"/> 先 Remove 再 Add，所以跟构建器留下的那一份不冲突（幂等）。</para>
        /// </summary>
        private void Awake()
        {
            SetGear(_gearButton);
        }

        private void SetGear(Button gear)
        {
            if (_gearButton != null)
            {
                _gearButton.onClick.RemoveListener(OnGear);
            }

            _gearButton = gear;

            if (_gearButton != null)
            {
                _gearButton.onClick.AddListener(OnGear);
            }
        }

        private void OnGear()
        {
            if (GearClicked != null)
            {
                GearClicked();
            }
        }

        /// <summary>绑定窗口自己的这一侧（玩家）。</summary>
        public void Bind(PlayerSnapshot me, int handCount, int attacksRemaining)
        {
            if (_name != null)
            {
                _name.text = string.IsNullOrEmpty(me.Name) ? "你" : me.Name;
            }

            if (_subtitle != null)
            {
                _subtitle.text = "手牌 ×" + handCount;
            }

            if (_hpNumber != null)
            {
                _hpNumber.text = me.Hp + "/" + Mathf.Max(0, me.MaxHp);
                _hpNumber.color = me.Hp <= 1 ? UiTheme.HpFull : UiTheme.TextPrimary;
            }

            if (_resourceNumber != null)
            {
                _resourceNumber.text = me.AuraTokensReady.ToString();
                _resourceNumber.color = me.AuraTokensReady > 0
                    ? UiTheme.AuraReady
                    : UiTheme.TextSecondary;
            }

            if (_orbNumber != null)
            {
                SetAttacksRemaining(attacksRemaining);
            }
        }
        private int _lastAttacks = -1;
        private float _orbPulse;

        public void SetAttacksRemaining(int count)
        {
            count = Mathf.Clamp(count, 0, 1);
            if (_orbNumber == null) return;
            _orbNumber.text = count + "/1";
            _orbNumber.color = count > 0 ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            if (count != _lastAttacks) _orbPulse = 0.3f;
            _lastAttacks = count;
        }

        private void Update()
        {
            if (_orbNumber == null || _orbPulse <= 0f) return;
            _orbPulse = Mathf.Max(0f, _orbPulse - Time.unscaledDeltaTime);
            float scale = 1f + Mathf.Sin(_orbPulse / 0.3f * Mathf.PI) * 0.16f;
            _orbNumber.transform.localScale = Vector3.one * scale;
        }
    }
}
