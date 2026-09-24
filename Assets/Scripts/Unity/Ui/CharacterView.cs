using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 舞台上一名角色的表现层（M11）：一张 <see cref="Image"/> + 一个 <see cref="SpriteAnimator"/>
    /// + 头顶徽标。
    ///
    /// <para><b>动作优先级</b>：待机是常驻循环，「出招 / 挨打」是一次性插播，
    /// 播完自动回到待机。所以这里自己记住 <c>_idlePlaying</c>，
    /// 免得每来一个事件都要上层重新排一次待机。</para>
    ///
    /// <para><b>摆位口径</b>：RectTransform 的 pivot 跟着当前动作切（见 <see cref="SpriteAnimator"/>），
    /// 所以 <c>anchoredPosition</c> 始终等于「脚底中心」在屏幕上的位置 —— 换动作时角色不会跳。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterView : MonoBehaviour
    {
        [SerializeField] private Image _image;
        [SerializeField] private SpriteAnimator _animator;
        [SerializeField] private RectTransform _badgeRoot;
        [SerializeField] private TMP_Text _badgeText;
        [SerializeField] private Image _badgeIcon;

        [Header("手牌数（M20，只有怪物一侧建了节点）")]
        [SerializeField] private GameObject _handCountRoot;
        [SerializeField] private TMP_Text _handCountText;

        [Tooltip("角色显示缩放（切图后的画布像素 → 界面像素）。")]
        [SerializeField] private float _scale = 1.3f;

        [SerializeField] private float _idleFps = 6f;
        [SerializeField] private float _actionFps = 11f;

        private BattleArtLibrary.CharacterSet _set;
        private bool _idlePlaying;

        /// <summary>已经播过倒地动作。挡住后续所有「回待机」的请求。</summary>
        private bool _dead;

        public void Configure(Image image, SpriteAnimator animator, RectTransform badgeRoot,
                              TMP_Text badgeText, Image badgeIcon)
        {
            _image = image;
            _animator = animator;
            _badgeRoot = badgeRoot;
            _badgeText = badgeText;
            _badgeIcon = badgeIcon;
            if (_animator != null)
            {
                _animator.SetTarget(image);
            }
        }

        public void SetScale(float scale)
        {
            _scale = scale <= 0f ? 1f : scale;
        }

        public void SetFps(float idleFps, float actionFps)
        {
            _idleFps = idleFps;
            _actionFps = actionFps;
        }

        /// <summary>绑角色素材；<paramref name="playIdle"/> 为真时立刻进待机循环。</summary>
        public void Bind(BattleArtLibrary.CharacterSet set, bool playIdle = true)
        {
            _set = set;
            _idlePlaying = false;
            _dead = false;

            if (playIdle)
            {
                PlayIdle();
            }
        }

        /// <summary>切到待机循环。重复调用是幂等的（正在播待机就不重来）。</summary>
        public void PlayIdle()
        {
            // 已经躺下了就不再切回待机 —— 每次掉血都会走一遍刷新，
            // 少了这道闸，死了的角色会在下一拍自己站起来。
            if (_dead || _set == null || _animator == null)
            {
                return;
            }

            BattleArtLibrary.Clip clip = _set.Idle;
            if (clip == null || !clip.IsValid)
            {
                return;
            }

            if (_idlePlaying && _animator.IsPlaying)
            {
                return;
            }

            _idlePlaying = true;
            UseFps(clip, _idleFps);
            _animator.Play(clip, _scale, true);
        }

        /// <summary>插播一次动作，播完自动回待机。</summary>
        public void PlayPose(CharacterPose pose)
        {
            Play(pose, PlayIdle);
        }

        /// <summary>
        /// 插播一次动作并**停在最后一帧**（不回调待机）。
        ///
        /// <para>给死亡用。放下之后这个角色就定在倒地姿势上，直到
        /// <see cref="Bind"/> 换一套素材或 <see cref="Revive"/> 复位 —— 否则下一拍刷新
        /// 会把尸体拉起来站着。</para>
        /// </summary>
        public void PlayPoseHold(CharacterPose pose)
        {
            if (Play(pose, null))
            {
                _dead = true;
            }
        }

        /// <summary>复位「已倒下」状态（下一局开局用）。</summary>
        public void Revive()
        {
            _dead = false;
        }

        private bool Play(CharacterPose pose, Action onDone)
        {
            if (_set == null || _animator == null || pose == CharacterPose.Idle)
            {
                PlayIdle();
                return false;
            }

            BattleArtLibrary.Clip clip = _set.Get(pose);
            if (clip == null || !clip.IsValid)
            {
                // 该角色没有这套动作（怪物目前只有待机 / 攻击 / 防御）→ 安静退回待机，
                // 不要留一个播不动的空 clip 挂在 animator 上
                PlayIdle();
                return false;
            }

            UseFps(clip, _actionFps);
            _idlePlaying = false;
            _animator.Play(clip, _scale, false, onDone);
            return true;
        }

        /// <summary>
        /// 帧率口径：**以素材自带的 Fps 为准**（生成器按动作分别配好 —— 死亡比出招慢、
        /// 受击比出招快），素材没写才用 <paramref name="fallback"/> 兜底。
        /// </summary>
        private static void UseFps(BattleArtLibrary.Clip clip, float fallback)
        {
            if (clip.Fps <= 0f)
            {
                clip.Fps = fallback;
            }
        }

        /// <summary>构建器接线用（与 <see cref="Configure"/> 分开：只有怪物这一侧有手牌数标签）。</summary>
        public void ConfigureHandCount(GameObject root, TMP_Text text)
        {
            _handCountRoot = root;
            _handCountText = text;
            SetHandCount(0, false);
        }

        /// <summary>
        /// 手牌数标签的根节点（M28）。<see cref="BattleUi"/> 要往它上面挂「按住查看手牌」
        /// 的手势组件 —— 它需要能吃射线（<c>raycastTarget</c>），所以底图不能是纯装饰。
        /// 玩家一侧没有这个节点，返回 null。
        /// </summary>
        public GameObject HandCountRoot
        {
            get { return _handCountRoot; }
        }

        /// <summary>
        /// 手牌数标签（M20）：摆在角色<b>模型的右下角</b>，与头顶徽标同一层（挂在 ArtLayer 上）。
        ///
        /// <para><b>为什么不挂在角色节点下</b>：与头顶徽标同因 —— 角色的 pivot 每帧随动作帧切，
        /// 挂进去标签会跟着抖几个像素。怪物不移动，所以一个固定锚点就够。</para>
        ///
        /// <para>玩家一侧没有这个节点，<see cref="SetHandCount"/> 会被安静忽略
        /// （手牌就在屏幕上摆着，不必再报一次数）。</para>
        /// </summary>
        public void SetHandCount(int count, bool show)
        {
            if (_handCountRoot != null && _handCountRoot.activeSelf != show)
            {
                _handCountRoot.SetActive(show);
            }

            if (_handCountText != null)
            {
                _handCountText.text = "手牌 ×" + Mathf.Max(0, count);
            }
        }

        /// <summary>头顶徽标（生命 / 光环）。<paramref name="text"/> 为空则整块隐藏。</summary>
        public void SetBadge(string text, Color color, Sprite icon = null)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (_badgeRoot != null && _badgeRoot.gameObject.activeSelf != show)
            {
                _badgeRoot.gameObject.SetActive(show);
            }

            if (_badgeText != null)
            {
                _badgeText.text = text ?? string.Empty;
                _badgeText.color = color;
            }

            if (_badgeIcon != null && icon != null)
            {
                _badgeIcon.sprite = icon;
                _badgeIcon.enabled = true;
            }
            else if (_badgeIcon != null)
            {
                _badgeIcon.enabled = false;
            }
        }

        /// <summary>把「生命 n/m」渲染成头顶徽标。对方血量必须能看见，否则玩家无法判断能否收尾。</summary>
        public void SetHpBadge(int hp, int maxHp, Sprite icon)
        {
            Color c = UiTheme.TextPrimary;
            if (hp <= 0)
            {
                c = UiTheme.LoseAccent;
            }
            else if (hp <= 1)
            {
                c = UiTheme.WarnRed;
            }

            SetBadge(hp + "/" + Mathf.Max(0, maxHp), c, icon);
        }

        // ══════════════════════════════════════════════════════
        //  拖拽落点高亮（M12）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 拖牌找到落点时的反馈。
        ///
        /// <para><b>为什么用「放大 + 提亮」而不是加一个光圈节点</b>：角色的参考画布每一帧都在变
        /// （不同动作的帧大小不一样，<see cref="SpriteAnimator"/> 会重设 <c>sizeDelta</c>），
        /// 挂个固定尺寸的高亮框必然对不齐。改 <c>localScale</c> 与 <c>Image.color</c> 两个值，
        /// 跟尺寸变化完全解耦，而且乘一层浅色 tint 在深色角色上也读得出来。</para>
        ///
        /// <para><b>为什么改 localScale 是安全的</b>：<see cref="SpriteAnimator"/> 只改
        /// <c>sizeDelta</c> / <c>pivot</c>，从不碰 <c>localScale</c>；头顶徽标也不在本节点下
        /// （挂在 ArtLayer 上），所以不会跟着一起放大。</para>
        /// </summary>
        public void SetHighlight(bool on)
        {
            if (_image != null)
            {
                _image.color = on ? UiTheme.DropTargetTint : Color.white;
            }

            transform.localScale = on
                ? new Vector3(HighlightScale, HighlightScale, 1f)
                : Vector3.one;
        }

        /// <summary>落点高亮的放大倍率。</summary>
        private const float HighlightScale = 1.06f;
    }
}
