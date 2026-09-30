using System;
using System.Collections;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// <b>强化动画</b>（2026-09-30）：确认之后盖在场景上播的那一段，约 1.2 秒。
    ///
    /// <para><b>时间线</b>（四段串行，时长全在 <c>UiLayout.UpgradeFx*Duration</c>）：</para>
    /// <list type="number">
    /// <item><b>蓄力</b> 0.18 s —— 整体淡入，卡面轻缩到 0.90（那一缩是在「憋劲」）；</item>
    /// <item><b>光爆</b> 0.30 s —— 暖金星芒由小炸到大、卡面在光里换成**新卡**；</item>
    /// <item><b>亮相</b> 0.42 s —— 光散去，卡面弹回 1.0；</item>
    /// <item><b>读数</b> 0.36 s —— 力量数字从旧值滚到新值，颜色由灰转暖金。</item>
    /// </list>
    ///
    /// <para><b>⚠ 为什么用协程而不是 <c>UiTween</c></b>：<c>UiTween</c> 是「一个组件演一条曲线」，
    /// 而这里是**四段带状态切换**的连续演出（第二段中途还要换卡面），
    /// 拆成四个组件反而要把中间状态在组件之间传递。四段的缓动仍复用 <see cref="UiEase"/>，
    /// 不引第三方补间（本工程口径：表现层零第三方依赖）。</para>
    ///
    /// <para><b>⚠ 用 <c>Time.unscaledDeltaTime</c></b>：与 <c>UiTween</c> 同口径 ——
    /// 演出不该被 <c>Time.timeScale</c>（暂停 / 结算慢放）改变节奏。</para>
    ///
    /// <para><b>⚠ 节点默认失活</b>：<see cref="Play"/> 会先 <c>SetActive(true)</c> 再起协程
    /// （失活的物体上 <c>StartCoroutine</c> 会直接抛异常），播完自己 <c>SetActive(false)</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeFxView : MonoBehaviour
    {
        /// <summary>整体淡入淡出（挂在本节点上）。</summary>
        [SerializeField] private CanvasGroup _group;

        /// <summary>卡面的容器（缩放作用在它上面，不动 CardView 自己的 CardRoot 缩放）。</summary>
        [SerializeField] private RectTransform _cardSlot;

        /// <summary>卡面（唯一那份 <c>CardView_Hand.prefab</c> 的实例）。</summary>
        [SerializeField] private CardView _card;

        /// <summary>光爆图案（暖金星芒；默认失活）。</summary>
        [SerializeField] private Image _glow;

        /// <summary>卡面下方那个大号力量读数。</summary>
        [SerializeField] private TMP_Text _power;

        /// <summary>读数上的说明字（「力量」）。</summary>
        [SerializeField] private TMP_Text _caption;

        private Coroutine _routine;

        /// <summary>正在播。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>播完了（上层拿它推进流程）。</summary>
        public event Action Finished;

        /// <summary>
        /// 播一段强化演出：<paramref name="before"/> 是被强化前的样子，<paramref name="after"/> 是强化后的新卡。
        ///
        /// <para>卡面会先在光爆里保持 <paramref name="before"/>，光最亮那一刻换成
        /// <paramref name="after"/> —— 「旧卡在光里变成新卡」是这个演出唯一要讲的事。</para>
        /// </summary>
        public void Play(CardDef before, CardDef after)
        {
            if (!isActiveAndEnabled)
            {
                gameObject.SetActive(true);
            }

            Stop();
            transform.SetAsLastSibling();       // 演出必须盖在最上层（弹窗刚关，兄弟序会变）
            _routine = StartCoroutine(Run(before, after));
        }

        /// <summary>停掉并回到隐藏态（重复调用幂等）。</summary>
        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            IsPlaying = false;
        }

        /// <summary>
        /// 跳过动画直接落到终态（探针 / 以后「关闭动画」的设置项用）。
        /// 不播也不发事件 —— 调用方自己要接着往下走。
        /// </summary>
        public void SnapTo(CardDef after)
        {
            Stop();
            ShowCard(after);
            SetGlow(0f, 0f);
            SetPower(after.Power, after.Power, 1f);
            SetAlpha(0f);
            gameObject.SetActive(false);
        }

        private IEnumerator Run(CardDef before, CardDef after)
        {
            IsPlaying = true;

            // ── ① 蓄力 ──────────────────────────────────────────
            ShowCard(before);
            SetGlow(0f, 0f);
            SetPower(before.Power, before.Power, 0f);
            SetAlpha(0f);

            yield return Phase(UiLayout.UpgradeFxShrinkDuration, UiEaseKind.OutQuad, t =>
            {
                SetAlpha(t);
                SetCardScale(Mathf.Lerp(1f, 0.90f, t));
            });

            // ── ② 光爆（光最亮那一刻换卡面）──────────────────────
            float half = UiLayout.UpgradeFxBurstDuration * 0.5f;

            yield return Phase(half, UiEaseKind.OutCubic, t =>
            {
                SetGlow(t * 0.95f, Mathf.Lerp(0.15f, 1.05f, t));
                SetCardScale(Mathf.Lerp(0.90f, 0.86f, t));
            });

            ShowCard(after);                    // ← 就在这里换成新卡

            yield return Phase(UiLayout.UpgradeFxBurstDuration - half, UiEaseKind.OutQuad, t =>
            {
                SetGlow(0.95f, Mathf.Lerp(1.05f, 1.18f, t));
                SetCardScale(0.86f);
            });

            // ── ③ 亮相（光散去、卡面弹回）───────────────────────
            yield return Phase(UiLayout.UpgradeFxRevealDuration, UiEaseKind.OutBack, t =>
            {
                SetGlow(Mathf.Lerp(0.95f, 0f, t), Mathf.Lerp(1.18f, 1.32f, t));
                SetCardScale(Mathf.Lerp(0.86f, 1f, t));
            });

            SetGlow(0f, 0f);

            // ── ④ 读数（力量从旧值滚到新值）─────────────────────
            int from = before != null ? before.Power : 0;
            int to = after != null ? after.Power : 0;

            yield return Phase(UiLayout.UpgradeFxCountDuration, UiEaseKind.OutCubic, t =>
            {
                SetPower(from, to, t);
            });

            SetPower(to, to, 1f);

            // ── 收尾：淡出再失活 ────────────────────────────────
            yield return Phase(0.16f, UiEaseKind.InQuad, t => SetAlpha(1f - t));

            _routine = null;
            IsPlaying = false;
            SetAlpha(0f);
            gameObject.SetActive(false);

            Action handler = Finished;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>一段固定时长的插值（用非缩放时间）。</summary>
        private IEnumerator Phase(float duration, UiEaseKind ease, Action<float> sample)
        {
            if (duration <= 0f)
            {
                sample(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                sample(UiEase.Evaluate(ease, t));
                yield return null;
            }

            sample(1f);
        }

        // ── 画 ──────────────────────────────────────────────────

        private void ShowCard(CardDef def)
        {
            if (_card == null || def == null)
            {
                return;
            }

            CardSnapshot snap = CardSnapshot.FromDef(def, 0, def.Cooldown);
            _card.Bind(snap, CardView.ViewMode.Hand, 0);
            _card.SetFaceWidth(UiLayout.UpgradeFxCardWidth);
            _card.SetInteractable(false);
            _card.SetSelected(false);
            _card.SetDimmed(false);
            _card.SetPresentationHidden(false);
        }

        private void SetCardScale(float scale)
        {
            if (_cardSlot != null)
            {
                _cardSlot.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void SetGlow(float alpha, float scale)
        {
            if (_glow == null)
            {
                return;
            }

            bool visible = alpha > 0.001f;
            if (_glow.gameObject.activeSelf != visible)
            {
                _glow.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            _glow.color = UiTheme.WithAlpha(UiTheme.UpgradeAccent, Mathf.Clamp01(alpha));
            _glow.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        private void SetPower(int from, int to, float t)
        {
            if (_power != null)
            {
                _power.text = Mathf.RoundToInt(Mathf.Lerp(from, to, t)).ToString();
                _power.color = Color.Lerp(UiTheme.UpgradePowerOld, UiTheme.UpgradePowerNew,
                    Mathf.Clamp01(t));
            }

            if (_caption != null)
            {
                _caption.text = "力量";
            }
        }

        private void SetAlpha(float alpha)
        {
            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01(alpha);
                // 演出期间不吃射线（不然玩家会在动画里点到台面）
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
        }
    }
}
