using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 舞台视图（M8）：双方信息条 + 中央标记 + 顶部提示。
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

        [Header("顶部提示条")]
        [SerializeField] private GameObject _promptRoot;
        [SerializeField] private TMP_Text _prompt;

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
                _markRoot.SetActive(!string.IsNullOrEmpty(text));
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

        [Header("事件短语（2026-10-29：横幅并入字幕条）")]
        [Tooltip("一句事件短语在字幕条上停留多久（秒）。")]
        [SerializeField] private float _flashSeconds = 1.2f;

        /// <summary>「底稿」文案：事件短语闪完之后要回到的那一句。</summary>
        private string _baseText = string.Empty;

        /// <summary>事件短语是否正在闪（闪的时候 SetPrompt 只记底稿、不抢屏）。</summary>
        private bool _flashing;

        private float _flashUntil;

        /// <summary>
        /// 顶部字幕（常驻那一句）。**短**：≤10 字，例如「你的进攻」「对手打出 6 点」。
        ///
        /// <para>2026-10-29：原来是引擎给的整句口径（「选择本次进攻打出的法术（有牌时必须进攻）」），
        /// 28pt 横在屏幕顶上，说了三件玩家已经看得见的事 —— 现在由 <c>BattleUi</c> 派生短句。</para>
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

        /// <summary>一句话事件（轮到你进攻 / 进攻结束 / 防御失败）。停 <see cref="_flashSeconds"/> 秒后回到底稿。</summary>
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
                _promptRoot.SetActive(!string.IsNullOrEmpty(text));
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
