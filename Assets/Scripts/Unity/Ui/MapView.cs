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
    /// <para><b>⚠ 桥的摆法（2026-10-06 随六向格定稿，这一段是全场唯一需要注意几何的地方）</b></para>
    /// <para><b>版式先保证「任意一条边等长」</b>：节点摆在<b>六向格</b>上
    /// （列距 = 边长、行距 = 边长 × √3/2、每上一行左移半格），桥的两端只可能是
    /// 正右 0° 或右上 60°（反向 180° / 240°），三种情况<b>长度都是
    /// <see cref="UiLayout.MapEdgeLength"/></b>。于是「两块平台之间的空隙」
    /// <c>边长 − 2 × 平台半径</c> 对每条边都是同一个数。</para>
    /// <para><b>再只用一个缩放系数</b>：直桥素材两端深色桥墩的<b>外缘</b>距是
    /// <see cref="UiLayout.MapBridgeVisibleWidth"/>，把它缩成上面那个空隙，
    /// 桥墩外缘就正好落在平台边缘上 —— 两块桥墩<b>整块露在平台之外</b>，
    /// 中间的木板把空隙填满；全图所有桥<b>同宽同长</b>
    /// （用户 2026-10-05：「桥的缩放应该都是一致大小的」「两端应当较为紧贴节点」；
    /// 2026-10-08 订正分母 —— 以前误用木板区宽当分母，桥墩被整块缩进平台里）。</para>
    /// <para><b>斜桥素材不再使用</b>：两种素材的「单位长端帽距」差 1.76 倍
    /// （直桥 215.2、斜桥 378.6），同一个空隙下必然缩成两种粗细；而直桥转到任意角度
    /// 都不变形（端帽偏移就在本地 x 轴上），所以<b>所有边一律用直桥素材转角度</b>。</para>
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

        /// <summary>
        /// 顶栏那格数字 —— <b>生命</b>（2026-10-05 起，之前显示的是层数进度）。
        ///
        /// <para>用户口径：地图上的生命 = 战斗里的生命；<b>不要用爱心图标</b>、
        /// 也<b>不需要显示进度</b>，所以这里只有一个纯文本的「生命 3 / 4」。</para>
        /// </summary>
        [SerializeField] private TMP_Text _hpText;

        [SerializeField] private TMP_Text _goldText;

        [Header("结束浮层（默认失活）")]
        [SerializeField] private GameObject _endLayer;

        [SerializeField] private TMP_Text _endTitle;

        [SerializeField] private TMP_Text _endBody;

        [SerializeField] private Button _endButton;

        [SerializeField] private TMP_Text _endButtonLabel;

        [Header("素材（构建器填；运行时也可以覆盖）")]
        [Tooltip("下标 = MapNodeType 的值：Camp / Battle / Shop / Witch / Altar / Unknown / Boss / Treasure / Rest")]
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
            // ⚠ 所有边一律用直桥素材（见类注释）。斜桥模板只当直桥缺图时的兜底 ——
            //   这样它也不至于变成一个「谁都不读」的序列化字段。
            RectTransform template = _bridgeStraightTemplate != null
                ? _bridgeStraightTemplate
                : _bridgeDiagonalTemplate;

            if (_bridgeRoot == null || template == null)
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

                        RectTransform bridge = Instantiate(template, _bridgeRoot);
                        bridge.name = "Bridge_" + L + "_" + layer[i].Row + "_" + to.Row;
                        bridge.gameObject.SetActive(true);
                        PlaceBridge(bridge, a, b);
                    }
                }
            }
        }

        /// <summary>
        /// 把一根桥摆成「<b>桥墩外缘正好落在两块平台的边缘、桥身转到位</b>」——
        /// <b>全图共用同一个缩放系数</b>（见类注释）。
        ///
        /// <para>之所以敢用一个系数，是因为版式已经保证「任意一条边等长」
        /// （六向格：<see cref="UiLayout.MapEdgeLength"/> 对 0° 与 ±60° 都成立），
        /// 空隙 = 边长 − 2×平台半径 因此对每一条边都是同一个数。</para>
        /// </summary>
        private static void PlaceBridge(RectTransform bridge, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float phi = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            // 空隙 = 边长 − 2 × 平台半径；夹一个下限，免得极端布局缩成针尖。
            float gap = UiLayout.MapEdgeLength - 2f * UiLayout.MapBridgePlatformRadius;
            if (gap < UiLayout.MapBridgeMinLength)
            {
                gap = UiLayout.MapBridgeMinLength;
            }

            // 直桥素材两端深色桥墩的**外缘**相距 MapBridgeVisibleWidth ——
            // 把这段距离缩成 gap，桥墩外缘就正好落在平台边缘上：
            // 两块桥墩整块露在平台之外，中间的木板把空隙填满。
            // ⚠ 2026-10-08 订正：以前拿「木板区宽（215）」当分母，系数大了 1.42 倍，
            //   桥墩被整块缩进平台里（症状：只看得见木板，桥的两端不见了）。
            float scale = gap / UiLayout.MapBridgeVisibleWidth;

            bridge.anchorMin = Vector2.zero;
            bridge.anchorMax = Vector2.zero;
            bridge.pivot = new Vector2(0.5f, 0.5f);
            bridge.anchoredPosition = (a + b) * 0.5f;
            bridge.localScale = new Vector3(scale, scale, 1f);
            bridge.localRotation = Quaternion.Euler(0f, 0f, phi);
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
            if (_hpText != null)
            {
                // 生命 = 局外持久生命（第一场之前满血）。战斗中掉的血由战斗场景在结算时
                // 写回 MapRun，所以「打剩 2 血回地图就是 2 / 4」；上限每场恢复成初始上限。
                _hpText.text = MapRun.HasRun
                    ? "生命  " + MapRun.CurrentHp + " / " + MapRun.CurrentHpMax
                    : "生命  —";
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
