using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 舞台信息：阶段、攻防读数、手牌选择与对象附近的短反馈。
    ///
    /// <para>它只吃 <see cref="PlayerSnapshot"/> 与舞台文案，不碰规则 ——
    /// 「现在该谁操作」这件事由 <c>BattleDriver</c> 的决策请求推过来（<c>Prompt</c> 字段就是引擎给的）。</para>
    ///
    /// <para><b>M20 删掉了「整个屏幕红色闪烁」</b>（2026-09-20 用户口径）：掉血的反馈改成
    /// <b>挨打的那个角色播受击动画</b>（见 <c>BattleUi.PlayCharacterBeats</c> →
    /// <c>CharacterPose.BeHit</c>，死亡走 <c>CharacterPose.Death</c>）。
    /// 全屏压一层红跟「谁掉的血」完全无关，玩家和 AI 掉血看到的画面一模一样，
    /// 反而盖住了真正的信息载体 —— 角色自己的动作。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageView : MonoBehaviour
    {
        [Header("信息条")]
        [SerializeField] private PlayerBarView _playerBar;
        [SerializeField] private PlayerBarView _enemyBar;

        [Header("中央标记")]
        [SerializeField] private GameObject _markRoot;
        [SerializeField] private TMP_Text _mark;
        [SerializeField] private Image _markBackdrop;

        [Header("手牌上方的当前选择")]
        [SerializeField] private GameObject _promptRoot;
        [SerializeField] private TMP_Text _prompt;

        [Header("阶段与战况")]
        [SerializeField] private TMP_Text _phaseLabel;
        [SerializeField] private TMP_Text _attackLabel;
        [SerializeField] private TMP_Text _defenseLabel;
        [SerializeField] private FloatTipView[] _actorFeedback;
        [SerializeField] private FloatTipView[] _cooldownFeedback;
        private int _turn;
        private bool _combo;

        public void SetPhase(string text, int turn = 0)
        {
            if (turn > 0) _turn = turn;
            SetLabel(_phaseLabel, string.IsNullOrEmpty(text) ? string.Empty
                : (_turn > 0 ? "第 " + _turn + " 回合 · " : string.Empty) + text);
        }

        public void BeginTurn(bool local, bool combo, int turn)
        {
            _combo = combo;
            ClearContext();
            SetAttack(string.Empty);
            SetPhase((local ? "你" : "对手") + (combo ? " · 连击进攻" : " · 进攻"), turn);
        }

        public void ShowLocalAttack()
        {
            SetAttack(string.Empty);
            SetPhase(_combo ? "你 · 连击进攻" : "你的进攻");
        }

        public void SetAttack(string text)
        {
            SetLabel(_attackLabel, text);
            SetLabel(_defenseLabel, string.Empty);
        }

        public void SetDefense(string text)
        {
            SetLabel(_defenseLabel, text);
        }

        public void ShowFeedback(bool local, string text, bool cooldown = false)
        {
            var views = cooldown ? _cooldownFeedback : _actorFeedback;
            int index = local ? 0 : 1;
            if (views != null && index < views.Length && views[index] != null)
                views[index].Show(text);
        }

        public void ClearContext()
        {
            _flashing = false;
            SetPrompt(string.Empty);
            SetDefense(string.Empty);
        }

        public void ResetPresentation()
        {
            _turn = 0;
            _combo = false;
            ClearContext();
            SetAttack(string.Empty);
            SetPhase(string.Empty);
            if (_actorFeedback != null)
                foreach (var view in _actorFeedback) if (view != null) view.Hide();
            if (_cooldownFeedback != null)
                foreach (var view in _cooldownFeedback) if (view != null) view.Hide();
        }

        private static void SetLabel(TMP_Text label, string text)
        {
            if (label == null) return;
            label.text = text ?? string.Empty;
            UiPanelAnimator.SetVisible(label.transform.parent.gameObject, !string.IsNullOrEmpty(text), interactive: false);
        }

        /// <summary>绑双方信息条。</summary>
        public void BindBars(PlayerSnapshot player, PlayerSnapshot enemy)
        {
            if (_playerBar != null)
            {
                _playerBar.Bind(player);
            }

            if (_enemyBar != null)
            {
                _enemyBar.Bind(enemy);
            }
        }

        /// <summary>中央标记（"你的回合" / "AI 进攻" / "VS"）。传空字符串则隐藏。</summary>
        public void SetMark(string text, Color color)
        {
            if (_markRoot != null)
            {
                UiPanelAnimator.SetVisible(_markRoot, !string.IsNullOrEmpty(text), interactive: false);
            }

            if (_mark != null)
            {
                _mark.text = text;
                _mark.color = color;
            }

            if (_markBackdrop != null)
            {
                _markBackdrop.color = UiTheme.WithAlpha(color, 0.12f);
            }
        }

        public void SetMark(string text)
        {
            SetMark(text, UiTheme.TextSecondary);
        }

        [Header("手牌操作反馈")]
        [Tooltip("操作反馈停留时间，随后恢复当前选择进度。")]
        [SerializeField] private float _flashSeconds = 1.2f;

        /// <summary>「底稿」文案：事件短语闪完之后要回到的那一句。</summary>
        private string _baseText = string.Empty;

        /// <summary>事件短语是否正在闪（闪的时候 SetPrompt 只记底稿、不抢屏）。</summary>
        private bool _flashing;

        private float _flashUntil;

        /// <summary>
        /// 手牌上方的选择状态；回合信息和结算事件使用各自的展示位置。
        /// </summary>
        public void SetPrompt(string text)
        {
            _baseText = text ?? string.Empty;

            if (_flashing)
            {
                return;
            }

            Apply(_baseText, UiTheme.InkOnIvory);
        }

        /// <summary>短暂操作反馈，结束后恢复选择状态。</summary>
        public void Flash(string text)
        {
            Flash(text, UiTheme.InkOnIvoryAccent);
        }

        /// <inheritdoc cref="Flash(string)"/>
        public void Flash(string text, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _flashing = true;
            _flashUntil = Time.unscaledTime + Mathf.Max(0.1f, _flashSeconds);
            Apply(text, color);
        }

        private void Apply(string text, Color color)
        {
            if (_promptRoot != null)
            {
                UiPanelAnimator.SetVisible(_promptRoot, !string.IsNullOrEmpty(text), interactive: false);
            }

            if (_prompt != null)
            {
                _prompt.text = text ?? string.Empty;
                _prompt.color = color;
            }
        }

        private void Update()
        {
            if (!_flashing)
            {
                return;
            }

            if (Time.unscaledTime < _flashUntil)
            {
                return;
            }

            _flashing = false;
            Apply(_baseText, UiTheme.InkOnIvory);
        }
    }
}
