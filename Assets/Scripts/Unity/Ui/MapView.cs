using System;
using System.Collections.Generic;
using MagicBrawl.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>
    /// 地图场景的<b>视图总装</b>（2026-10-02 · P7）。
    ///
    /// <para><b>它做什么</b>：拿一张 <see cref="MapGraph"/>，
    /// ① 给每个节点克隆一份 <see cref="MapNodeView"/>、② 给每条边摆一根桥、
    /// ③ 把主角棋子放到当前节点、④ 按 <see cref="MapRun"/> 的状态刷亮 / 压暗。</para>
    ///
    /// <para><b>它不做什么</b>：不生成地图（那是 <c>Core/Map/MapGenerator</c>）、
    /// 不判断哪一步能走（那是 <see cref="MapRun.IsReachable"/>）、
    /// 不切场景（那是 <see cref="MapSceneEntry"/>）。这里只有「把事实画出来」。</para>
    ///
    /// <para><b>⚠ 桥的两套摆法（这一段是全场唯一需要注意几何的地方）</b></para>
    /// <para>素材量出来的事实（<see cref="UiLayout"/> 里记了原始数据）：
    /// 直桥的两个端帽在<b>本地 x 轴上</b>（±107.6, 0），所以「只缩 x」既精确对齐方向、
    /// 又保持木板厚度不变 —— 任何角度都能用；斜桥的两个端帽在 (±80.75, ±49.35)，
    /// <b>不在任何一个轴上</b>，只缩 x 会把角度压斜（缩 1.5 倍时 31.4° 掉到 21.5°），
    /// 所以必须反解：先按「端帽距离 = 两节点距离」解出 s，再把旋转补回 α 那一段。</para>
    /// <para>结果是一样的：<b>两个端帽正好落在两个节点的中心</b>。
    /// 端帽本来就被 128 px 的节点图盖住，所以素材被拉伸的痕迹看不见 ——
    /// 这也是为什么可以放心用「缩放」而不是「按素材原样拼」。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapView : MonoBehaviour
    {
        [Header("节点")]
        [SerializeField] private RectTransform _bridgeRoot;

        [SerializeField] private RectTransform _nodeRoot;

        [SerializeField] private MapNodeView _nodeTemplate;

        [Header("桥（失活模板，两种各一份）")]
        [SerializeField] private RectTransform _bridgeStraightTemplate;

        [SerializeField] private RectTransform _bridgeDiagonalTemplate;

        [Header("主角棋子")]
        [SerializeField] private RectTransform _player;

        [SerializeField] private Image _playerImage;

        [Header("顶栏 / 标题 / 提示")]
        [SerializeField] private TMP_Text _title;

        [SerializeField] private TMP_Text _hint;

        [SerializeField] private TMP_Text _progressText;

        [SerializeField] private TMP_Text _goldText;

        [Header("结束浮层（默认失活）")]
        [SerializeField] private GameObject _endLayer;

        [SerializeField] private TMP_Text _endTitle;

        [SerializeField] private TMP_Text _endBody;

        [SerializeField] private Button _endButton;

        [SerializeField] private TMP_Text _endButtonLabel;

        [Header("素材（构建器填；运行时也可以覆盖）")]
        [Tooltip("下标 = MapNodeType 的值：Camp / Battle / Shop / Witch / Altar / Unknown / Boss")]
        [SerializeField] private Sprite[] _nodeIcons = new Sprite[7];

        /// <summary>点了某个<b>可达</b>节点（已经过可达性复核）。</summary>
        public event Action<int> NodeClicked;

        /// <summary>结束了的那局里点了「重新开始」。</summary>
        public event Action RestartClicked;

        private MapGraph _graph;
        private readonly List<MapNodeView> _nodes = new List<MapNodeView>();
        private TweenAnchoredPosition _moveTween;
        private Action _onArrived;
        private float _pulse;
        private bool _ready;

        /// <summary>结束浮层关着吗（= 正常的地图界面）。</summary>
        public bool IsEndVisible
        {
            get { return _endLayer != null && _endLayer.activeSelf; }
        }

        private void Awake()
        {
            EnsureInit();
        }

        /// <summary>
        /// 把必要的缓存补齐。
        ///
        /// <para><b>为什么不在 Awake 里一次性做完</b>：<c>MapSceneEntry</c>（挂在场景根）
        /// 与 <c>MapView</c>（挂在 Canvas 上）的 <c>Awake</c> 顺序是<b>不确定的</b> ——
        /// 入口先醒的话，它会直接调 <see cref="Build"/>，而这时候本类的 <c>Awake</c> 还没跑。
        /// 所以每个入口都先调一次它，幂等。</para>
        /// </summary>
        private void EnsureInit()
        {
            if (_ready)
            {
                return;
            }

            _ready = true;

            if (_player != null)
            {
                _moveTween = _player.GetComponent<TweenAnchoredPosition>();
                if (_moveTween == null)
                {
                    _moveTween = _player.gameObject.AddComponent<TweenAnchoredPosition>();
                }

                _moveTween.Finished -= HandlePlayerArrived;
                _moveTween.Finished += HandlePlayerArrived;
            }

            if (_endButton != null)
            {
                _endButton.onClick.RemoveListener(HandleEndButton);
                _endButton.onClick.AddListener(HandleEndButton);
            }

            if (_nodeTemplate != null)
            {
                _nodeTemplate.gameObject.SetActive(false);
            }

            if (_endLayer != null)
            {
                _endLayer.SetActive(false);
            }
        }

        /// <summary>接上（或换一张）地图。可以重复调 —— 每次都会把上一张的节点与桥清干净。</summary>
        public void Build(MapGraph graph)
        {
            EnsureInit();

            _graph = graph;

            // ⚠ 两个 root 里**只有运行时克隆出来的**节点（模板挂在另一个 Templates 节点下，
            //   不是这两个 root 的子节点）—— 所以这里可以无脑清空。
            //   把模板塞进 root 再靠「跳过某一个子节点」保命是行不通的：
            //   迟早有人加第二种模板，然后漏掉一处。
            ClearChildren(_nodeRoot);
            ClearChildren(_bridgeRoot);

            _nodes.Clear();
            if (graph == null)
            {
                RefreshTexts();
                return;
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                MapNode node = graph.Nodes[i];
                MapNodeView view = Instantiate(_nodeTemplate, _nodeRoot);
                view.name = "Node_" + i + "_" + node.Layer + "_" + node.Row;
                view.gameObject.SetActive(true);

                RectTransform rt = (RectTransform)view.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = UiLayout.MapNodePosition(node.Layer, node.Row);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;

                view.Clicked -= HandleNodeClicked;
                view.Clicked += HandleNodeClicked;
                view.Bind(i, node, MapNodeState.Locked, IconFor(node.Type));
                _nodes.Add(view);
            }

            BuildBridges(graph);
            PlacePlayer(MapRun.CurrentIndex, false, null);
            RefreshStates();
        }

        /// <summary>按 <see cref="MapRun"/> 当前的状态重刷所有节点与顶栏。走完一步调它。</summary>
        public void RefreshStates()
        {
            EnsureInit();

            if (_graph == null)
            {
                RefreshTexts();
                return;
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                MapNodeView view = _nodes[i];
                view.ApplyState(MapRun.StateOf(view.Index));
            }

            RefreshTexts();
        }

        /// <summary>
        /// 把主角棋子挪到某个节点。
        /// <paramref name="animate"/> = false 时立刻到位（进场景 / 重刷用）。
        /// </summary>
        public void PlacePlayer(int index, bool animate, Action onArrived)
        {
            EnsureInit();

            if (_player == null)
            {
                if (onArrived != null)
                {
                    onArrived();
                }

                return;
            }

            if (_graph == null || index < 0 || index >= _graph.Nodes.Count)
            {
                _player.gameObject.SetActive(false);
                if (onArrived != null)
                {
                    onArrived();
                }

                return;
            }

            MapNode node = _graph.Nodes[index];
            Vector2 target = UiLayout.MapNodePosition(node.Layer, node.Row)
                             + new Vector2(UiLayout.MapPlayerOffsetX, UiLayout.MapPlayerOffsetY);

            _player.gameObject.SetActive(true);

            if (!animate || _moveTween == null)
            {
                _player.anchoredPosition = target;
                if (onArrived != null)
                {
                    onArrived();
                }

                return;
            }

            _onArrived = onArrived;
            _moveTween.Setup(_player.anchoredPosition, target, UiLayout.MapMoveSeconds,
                UiEaseKind.OutCubic);
            _moveTween.Play();
        }

        /// <summary>顶栏那行提示（「点亮的节点可以前往」之类）。</summary>
        public void SetHint(string text)
        {
            if (_hint != null)
            {
                _hint.text = text;
            }
        }

        /// <summary>弹出「通关 / 旅程结束」。</summary>
        public void ShowEnd()
        {
            EnsureInit();

            if (_endLayer == null)
            {
                return;
            }

            bool won = MapRun.Won;
            if (_endTitle != null)
            {
                _endTitle.text = won ? "通　关" : "旅程结束";
                _endTitle.color = won ? UiTheme.MapEndWin : UiTheme.MapEndLose;
            }

            if (_endBody != null)
            {
                _endBody.text = BuildEndBody(won);
            }

            if (_endButtonLabel != null)
            {
                _endButtonLabel.text = "重新开始";
            }

            _endLayer.SetActive(true);
        }

        public void HideEnd()
        {
            if (_endLayer != null)
            {
                _endLayer.SetActive(false);
            }
        }

        private static string BuildEndBody(bool won)
        {
            int walked = MapRun.Resolved.Count;
            string head = won
                ? "你击败了守在最后的 Boss，这趟冒险到此结束。"
                : "你在第 " + MapRun.Progress + " 层倒下了。";

            return head + "\n走过的节点：" + walked + " 个　·　地图种子：" + MapRun.Seed;
        }

        // ══════════════════════════════════════════════════════
        //  桥
        // ══════════════════════════════════════════════════════

        private void BuildBridges(MapGraph graph)
        {
            if (_bridgeRoot == null || _bridgeStraightTemplate == null || _bridgeDiagonalTemplate == null)
            {
                return;
            }

            for (int L = 0; L < graph.LayerCount - 1; L++)
            {
                List<MapNode> layer = graph.NodesInLayer(L);
                for (int i = 0; i < layer.Count; i++)
                {
                    IReadOnlyList<int> outs = graph.Next(layer[i].Index);
                    for (int k = 0; k < outs.Count; k++)
                    {
                        MapNode to = graph.Nodes[outs[k]];
                        Vector2 a = UiLayout.MapNodePosition(layer[i].Layer, layer[i].Row);
                        Vector2 b = UiLayout.MapNodePosition(to.Layer, to.Row);
                        float phi = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

                        bool diagonal = phi > UiLayout.MapBridgeDiagonalMinAngle;
                        RectTransform template = diagonal ? _bridgeDiagonalTemplate : _bridgeStraightTemplate;

                        RectTransform bridge = Instantiate(template, _bridgeRoot);
                        bridge.name = "Bridge_" + L + "_" + layer[i].Row + "_" + to.Row;
                        bridge.gameObject.SetActive(true);
                        PlaceBridge(bridge, a, b, diagonal);
                    }
                }
            }
        }

        /// <summary>
        /// 把一根桥摆成「两端正好落在两个节点中心」。
        ///
        /// <para>两种摆法的推导见类注释；一句话：<b>直桥只缩 x（方向天然精确），
        /// 斜桥先解出缩放系数再补旋转</b>。谁都不许在这些数上「差不多就行」——
        /// 差一点就是桥端露在节点外面。</para>
        /// </summary>
        private static void PlaceBridge(RectTransform bridge, Vector2 a, Vector2 b, bool diagonal)
        {
            Vector2 delta = b - a;
            float len = delta.magnitude;
            if (len < 1f)
            {
                len = 1f;
            }

            float phi = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            bridge.anchorMin = Vector2.zero;
            bridge.anchorMax = Vector2.zero;
            bridge.pivot = new Vector2(0.5f, 0.5f);
            bridge.anchoredPosition = (a + b) * 0.5f;

            if (diagonal)
            {
                float capX = UiLayout.MapBridgeDiagonalCapX;
                float capY = UiLayout.MapBridgeDiagonalCapY;

                // 端帽向量 (capX·s, capY) 的模长必须等于 len → 反解 s。
                // len ≤ capY 时无解（边比端帽的纵向跨度还短），夹一下别让 sqrt 出负数。
                float inner = len * len - capY * capY;
                float s = Mathf.Sqrt(inner > 1f ? inner : 1f) / capX;
                float alpha = Mathf.Atan2(capY, capX * s) * Mathf.Rad2Deg;

                bridge.localRotation = Quaternion.Euler(0f, 0f, phi - alpha);
                bridge.localScale = new Vector3(s, 1f, 1f);
                return;
            }

            bridge.localRotation = Quaternion.Euler(0f, 0f, phi);
            bridge.localScale = new Vector3(len / UiLayout.MapBridgeStraightCapSpan, 1f, 1f);
        }

        // ══════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════

        private Sprite IconFor(MapNodeType type)
        {
            int index = (int)type;
            if (_nodeIcons == null || index < 0 || index >= _nodeIcons.Length)
            {
                return null;
            }

            return _nodeIcons[index];
        }

        private void HandleNodeClicked(int index)
        {
            // ⚠ 复核一次（视图上的命中区本来就已经按状态开关了，这里是双保险）：
            //   真正的判据只有 MapRun 一处，界面不自己发明规则。
            if (!MapRun.IsReachable(index))
            {
                return;
            }

            Action<int> handler = NodeClicked;
            if (handler != null)
            {
                handler(index);
            }
        }

        private void HandleEndButton()
        {
            Action handler = RestartClicked;
            if (handler != null)
            {
                handler();
            }
        }

        private void HandlePlayerArrived()
        {
            Action callback = _onArrived;
            _onArrived = null;
            if (callback != null)
            {
                callback();
            }
        }

        private void RefreshTexts()
        {
            if (_progressText != null)
            {
                _progressText.text = MapRun.HasRun
                    ? "进度  " + MapRun.Progress + " / " + MapRun.ProgressTotal
                    : "进度  —";
            }

            if (_goldText != null)
            {
                _goldText.text = MapRun.HasRun ? MapRun.Gold.ToString() : "—";
            }
        }

        private void Update()
        {
            if (_graph == null || _nodes.Count == 0)
            {
                return;
            }

            _pulse += Time.unscaledDeltaTime / UiLayout.MapRingPulseSeconds;
            if (_pulse > 1f)
            {
                _pulse -= 1f;
            }

            float scale = 1f + UiLayout.MapRingPulse * Mathf.Sin(_pulse * Mathf.PI * 2f);
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].State == MapNodeState.Reachable)
                {
                    _nodes[i].SetRingScale(scale);
                }
            }
        }

        /// <summary>清掉某个容器里的全部子节点（运行时克隆出来的都是临时的，整批清）。</summary>
        private static void ClearChildren(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Destroy(root.GetChild(i).gameObject);
            }
        }
    }
}
