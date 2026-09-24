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

        /// <summary>顶部提示条（由决策请求的 Prompt 驱动）。</summary>
        public void SetPrompt(string text)
        {
            if (_promptRoot != null)
            {
                _promptRoot.SetActive(!string.IsNullOrEmpty(text));
            }

            if (_prompt != null)
            {
                _prompt.text = text ?? string.Empty;
            }
        }
    }
}
