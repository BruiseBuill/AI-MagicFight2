using System;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 地图上的<b>一个节点</b>（2026-10-02）。
    ///
    /// <para><b>它只做三件事</b>：换图标、按状态切显示、把点击冒泡出去。
    /// 「这一个节点现在能不能点」<b>不在这里判</b> —— 那是
    /// <see cref="MapRun.StateOf"/> 的结论，本类只按拿到的 <see cref="MapNodeState"/> 办事
    /// （铁律 3：规则只在引擎一处）。</para>
    ///
    /// <para><b>节点子树（构建器生成，顺序即绘制顺序，越靠后越上层）</b>：</para>
    /// <code>
    /// NodeTemplate                  ← 失活模板；MapView 为每个节点克隆一份
    /// ├─ RingLine                   「可以去」的金色圆角框（CardBox_Line 九宫格，呼吸缩放）
    /// ├─ RingCurrent                「你在这儿」的蓝色圆角框（静态）
    /// ├─ Icon                       节点图（7 选 1，按 MapNodeType 查 Sprite[]）
    /// ├─ Dim                        压暗纱（去不了 / 已走过时盖在图上）
    /// ├─ Label                      节点名（只在「可以去 / 当前」时显示）
    /// └─ Hit                        透明命中区（**唯一**吃射线的东西）
    /// </code>
    ///
    /// <para><b>⚠ 命中区是单独一个节点，不是给 Icon 开 <c>raycastTarget</c></b>：
    /// 图层顺序决定了 Hit 在 Icon 之上，而 <c>raycastTarget</c> 是「吃射线」的唯一开关 ——
    /// 只有一处打开、只有一处关，状态切换时不会漏。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapNodeView : MonoBehaviour, IPointerClickHandler
    {
        [Header("节点子树（构建器接线）")]
        [SerializeField] private Image _ringLine;

        [SerializeField] private Image _ringCurrent;

        [SerializeField] private Image _icon;

        [SerializeField] private Image _dim;

        [SerializeField] private GameObject _labelRoot;

        [SerializeField] private TMP_Text _label;

        [SerializeField] private Image _hit;

        /// <summary>这个视图代表哪个节点（<c>MapGraph.Nodes</c> 的下标）。</summary>
        private int _index = -1;

        /// <summary>当前状态（供 MapView 的呼吸动画读）。</summary>
        public MapNodeState State { get; private set; }

        /// <summary>节点下标；没绑定时是 −1。</summary>
        public int Index { get { return _index; } }

        /// <summary>点了这个节点（参数 = 下标）。能不能去由订阅者（MapView）复核。</summary>
        public event Action<int> Clicked;

        /// <summary>
        /// 绑定一个节点。
        ///
        /// <para><paramref name="icon"/> 允许为 null（素材没配齐时退化成一块空框，
        /// 而不是让整个视图报错）—— 与工程里其它视图的兜底口径一致。</para>
        /// </summary>
        public void Bind(int index, MapNode node, MapNodeState state, Sprite icon)
        {
            _index = index;
            State = state;

            if (_icon != null)
            {
                _icon.sprite = icon;
                _icon.enabled = icon != null;
            }

            // ⚠ 压暗纱**跟图标同一张图**（2026-10-02 修）。
            //
            //   原来它是一块没有 sprite 的纯色矩形 —— 节点图是**椭圆**平台，矩形纱的四个角
            //   会露在平台外面，于是每个「走过 / 去不了」的节点外面都挂着一个深色方块
            //   （截图里一眼就看出来了，但零报错）。用同一张图当掩膜，纱的形状就永远是节点本身。
            //
            //   ⚠ 只换 sprite、不动 color：颜色仍然只写在 prefab 的 Image.color 上
            //   （用户 2026-09-19 的红线）。
            if (_dim != null)
            {
                _dim.sprite = icon;
                _dim.enabled = icon != null;
            }

            if (_label != null)
            {
                _label.text = MapRoutes.DisplayName(node.Type);
            }

            ApplyState(state);
        }

        /// <summary>只切状态（不换图标 / 文案）—— 玩家走一步之后的重刷用它。</summary>
        public void ApplyState(MapNodeState state)
        {
            State = state;

            bool reachable = state == MapNodeState.Reachable;
            bool current = state == MapNodeState.Current;
            bool dimmed = state == MapNodeState.Locked || state == MapNodeState.Resolved;

            if (_ringLine != null)
            {
                _ringLine.gameObject.SetActive(reachable);
            }

            if (_ringCurrent != null)
            {
                _ringCurrent.gameObject.SetActive(current);
            }

            if (_dim != null)
            {
                _dim.gameObject.SetActive(dimmed);
            }

            // 文字只在「能去 / 人在这儿」时出现：节点名每格都摆出来会跟下一行的节点打架
            // （行距 144、节点高 117、小字 22 —— 中间只剩 5 px）。
            if (_labelRoot != null)
            {
                _labelRoot.SetActive(reachable || current);
            }

            // ⚠ 命中区跟着状态开关 —— 这是「只有下一层的节点点得动」的落点。
            //   把它写在状态里而不是写在点击回调里，是为了让射线层与视觉**同时**一致：
            //   否则玩家能点到一个已经压暗的节点，而且点下去没反应（零报错）。
            if (_hit != null)
            {
                _hit.raycastTarget = reachable;
            }
        }

        /// <summary>呼吸动画只动 <paramref name="scale"/>（由 <see cref="MapView"/> 每帧给）。</summary>
        public void SetRingScale(float scale)
        {
            if (_ringLine == null || !_ringLine.gameObject.activeSelf)
            {
                return;
            }

            _ringLine.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_hit == null || !_hit.raycastTarget)
            {
                return;
            }

            Action<int> handler = Clicked;
            if (handler != null)
            {
                handler(_index);
            }
        }
    }
}
