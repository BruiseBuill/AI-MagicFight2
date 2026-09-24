using System;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 极简帧动画播放器（M11）。挂在带 <see cref="Image"/> 的节点上，按固定帧率换 <c>sprite</c>。
    ///
    /// <para><b>为什么不生成 AnimationClip + Animator</b>：本工程的界面全部由编辑器脚本
    /// 程序化构建（`UiKitBuilder` / `BattleUiBuilder` / `BattleArtLayerBuilder`），
    /// 而 AnimationClip 资产要额外维护 controller、状态机与过渡，成本远高于收益。
    /// 帧序列已经在切图时按「同一画布 + 同一锚点」对齐，这里只要换图就是干净的循环。</para>
    ///
    /// <para><b>为什么改 RectTransform 的 pivot 而不是 sprite 的 pivot</b>：
    /// 简单 <see cref="Image"/> 是按 RectTransform 铺图的，sprite 自带 pivot 不参与绘制。
    /// 所以锚点对齐要靠 RectTransform 的 pivot —— 它一改，rect 会整体平移，
    /// 而 <c>anchoredPosition</c>（= pivot 点的位置）保持不变，正好等价于「脚底钉在原地」。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpriteAnimator : MonoBehaviour
    {
        [SerializeField] private Image _target;

        [SerializeField] private float _fallbackFps = 8f;

        private BattleArtLibrary.Clip _clip;
        private RectTransform _rect;
        private float _scale = 1f;
        private float _time;
        private int _index;
        private bool _playing;
        private bool _loop;
        private Action _onDone;

        /// <summary>锚点是否随当前帧切换（关闭后 pivot 保持第一次播放时的值）。</summary>
        [SerializeField] private bool _applyPivot = true;

        public bool IsPlaying
        {
            get { return _playing; }
        }

        public Image Target
        {
            get { return _target; }
        }

        public void SetTarget(Image image)
        {
            _target = image;
        }

        public void SetPivotFollow(bool on)
        {
            _applyPivot = on;
        }

        /// <summary>只要一帧（不播），用于静态摆位。</summary>
        public void SetStatic(BattleArtLibrary.Clip clip, float scale)
        {
            _playing = false;
            _onDone = null;
            _clip = clip;
            _scale = scale <= 0f ? 1f : scale;
            _index = 0;
            ApplyFrame();
        }

        /// <summary>
        /// 播一组帧。
        /// <paramref name="loop"/> 为 false 时播完停在最后一帧并回调 <paramref name="onDone"/>。
        /// </summary>
        public void Play(BattleArtLibrary.Clip clip, float scale, bool loop, Action onDone = null)
        {
            if (clip == null || !clip.IsValid)
            {
                if (onDone != null)
                {
                    onDone();
                }

                return;
            }

            _clip = clip;
            _scale = scale <= 0f ? 1f : scale;
            _loop = loop;
            _onDone = onDone;
            _index = 0;
            _time = 0f;
            _playing = true;
            ApplyFrame();
        }

        public void Stop()
        {
            _playing = false;
            _onDone = null;
        }

        private void ApplyFrame()
        {
            if (_clip == null || !_clip.IsValid)
            {
                return;
            }

            if (_rect == null)
            {
                _rect = transform as RectTransform;
            }

            if (_rect != null)
            {
                _rect.sizeDelta = _clip.Size * _scale;
                if (_applyPivot)
                {
                    _rect.pivot = _clip.Pivot;
                }
            }

            if (_target != null)
            {
                Sprite s = _clip.Frames[_index];
                if (s != null)
                {
                    _target.sprite = s;
                }
            }
        }

        private void Update()
        {
            if (!_playing || _clip == null || !_clip.IsValid)
            {
                return;
            }

            float fps = _clip.Fps > 0f ? _clip.Fps : _fallbackFps;
            _time += Time.deltaTime * fps;

            while (_time >= 1f)
            {
                _time -= 1f;
                _index++;

                if (_index >= _clip.FrameCount)
                {
                    if (_loop)
                    {
                        _index = 0;
                    }
                    else
                    {
                        _index = _clip.FrameCount - 1;
                        _playing = false;
                        ApplyFrame();
                        Action cb = _onDone;
                        _onDone = null;
                        if (cb != null)
                        {
                            cb();
                        }

                        return;
                    }
                }

                ApplyFrame();
            }
        }
    }
}
