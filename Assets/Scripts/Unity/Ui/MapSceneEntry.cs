using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicBrawl.App
{
    /// <summary>
    /// 地图场景的<b>单场景入口</b>（2026-10-02 · P7）。
    ///
    /// <para><b>它把整条链路串起来</b>：地图 → （点节点 → 棋子走过去）→ 事件 / 战斗场景
    /// → （点「离开」/ 打完一局）→ 回地图 → 结算这一步 → 下一层点亮。
    /// 五个场景之间的「谁记得我走到哪儿了」全在 <see cref="MapRun"/>（静态，不随场景销毁）。</para>
    ///
    /// <para><b>三种进场景的情形</b>（<see cref="Boot"/> 按顺序判）：</para>
    /// <list type="number">
    /// <item><b>第一次进来</b>（<see cref="MapRun.HasRun"/> 为假）：开一趟新冒险；</item>
    /// <item><b>从某个节点的场景回来的</b>（有 <see cref="MapRun.PendingIndex"/>）：
    /// 结算那一步 —— 标成走过、把棋子挪过去；战斗则先看胜负；</item>
    /// <item><b>别的路径进来的</b>（比如直接从编辑器点开 <c>Map.unity</c>）：
    /// 沿用手里那张图，什么都不重生成。</item>
    /// </list>
    ///
    /// <para><b>⚠ 为什么「开工」这一段留在这里而不是引擎里</b>：这与商店 / 石台 / 女巫三个
    /// 事件场景同一个理由（<c>Docs/design/冒险事件架构.md</c> §4）——
    /// 每个场景都要能<b>单独进 Play 调试</b>，所以「没有冒险在跑」时由场景自己造一个。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapSceneEntry : MonoBehaviour
    {
        [Header("调试 · 开局")]
        [Tooltip("没有在跑的冒险时，进 Play 自动开一局（默认开）。\n"
                 + "取消勾选 = 什么都不建，只看空场景（几乎不用）。")]
        [SerializeField] private bool _startNewRun = true;

        [Tooltip("固定种子 —— 同一个种子出同一张图，方便复现「上次那张图」和截图对读。")]
        [SerializeField] private int _seed = 20261002;

        [Tooltip("勾上 = 每次进 Play 随机换一张图（_seed 失效）。默认开 —— 用户口径是「地图随机生成」。\n"
                 + "取消勾选 = 固定用下面的 _seed，同一张图可复现（调版式 / 截图对读时用）。")]
        [SerializeField] private bool _randomSeed = true;

        [Tooltip("重开一局时换一张新图（默认开）。取消勾选 = 重开还是同一张图（调试用）。")]
        [SerializeField] private bool _newMapOnRestart = true;

        [Tooltip("勾上 = 不走/不等，点了节点直接切场景（调 UI 时省时间）。默认关。")]
        [SerializeField] private bool _skipWalkAnimation;

        [Header("调试 · 日志")]
        [SerializeField] private bool _logClicks = true;

        [Header("视图（构建器接线）")]
        [SerializeField] private MapView _view;

        /// <summary>正在走 / 正在切场景 —— 这期间不再接受第二次点击。</summary>
        private bool _busy;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<MapView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[MapSceneEntry] 没有接 MapView —— 先跑 `魔法乱斗/P7 · 构建 Map 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）：构建器接的那一份不是序列化数据。
            _view.NodeClicked += OnNodeClicked;
            _view.RestartClicked += OnRestartClicked;

            Boot();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.NodeClicked -= OnNodeClicked;
            _view.RestartClicked -= OnRestartClicked;
        }

        /// <summary>进场景时决定「这是新开局 / 还是刚从一个节点回来」，然后把界面铺出来。</summary>
        private void Boot()
        {
            if (!MapRun.HasRun)
            {
                if (!_startNewRun)
                {
                    return;
                }

                MapRun.StartNew(NextSeed(false));
                Log("开一趟新冒险 · seed " + MapRun.Seed);
            }
            else if (MapRun.HasPending)
            {
                int cameFrom = MapRun.PendingIndex;
                MapRun.SettleReturn();
                Log("从节点 " + cameFrom + " 回到地图 → " + DescribeRun());
            }

            _view.Build(MapRun.Graph);
            _view.PlacePlayer(MapRun.CurrentIndex, false, null);
            _view.RefreshStates();

            if (MapRun.IsOver)
            {
                _view.SetHint(string.Empty);
                _view.ShowEnd();
                return;
            }

            _view.SetHint(DefaultHint());
        }

        /// <summary>
        /// 这一趟用哪个种子。
        ///
        /// <para><b>⚠ 重开一定要换图</b>（除非显式关掉 <see cref="_newMapOnRestart"/>）——
        /// 「重新开始」而看到和刚才一模一样的地图，玩家会以为按钮坏了。
        /// 第一次进场景则听 <see cref="_randomSeed"/>：默认随机（用户口径「地图随机生成」），
        /// 关掉就固定用 <see cref="_seed"/> 复现同一张图。</para>
        /// </summary>
        private int NextSeed(bool restart)
        {
            if (_randomSeed || (restart && _newMapOnRestart))
            {
                return Random.Range(int.MinValue, int.MaxValue);
            }

            return _seed;
        }

        // ══════════════════════════════════════════════════════
        //  点节点 → 走过去 → 进场景
        // ══════════════════════════════════════════════════════

        private void OnNodeClicked(int index)
        {
            if (_busy || MapRun.IsOver)
            {
                return;
            }

            // ⚠ 唯一的通行判据在 MapRun 里（视图上的命中区也按它开关，这里是复核）。
            if (!MapRun.BeginEnter(index))
            {
                return;
            }

            _busy = true;
            MapNode node = MapRun.Graph.Nodes[index];
            _view.SetHint("前往「" + MapRoutes.DisplayName(node.Type) + "」…");

            if (_logClicks)
            {
                // ⚠ 括号不能省：`+` 的优先级比 `??` 高，写成 `"…" + a ?? "b"` 会变成
                //   `(整串) ?? "b"` —— 左边永远非 null，兜底那句话永远不出现（零报错）。
                string scene = MapRoutes.SceneFor(node.Type);
                Debug.Log("[MapSceneEntry] 前往 (" + node.Layer + "," + node.Row + ") " + node.Type
                          + " → 场景 " + (scene == null ? "（没有场景，走过去直接结算）" : scene));
            }

            if (_skipWalkAnimation)
            {
                _view.PlacePlayer(index, false, null);
                ArriveAtNode();
                return;
            }

            _view.PlacePlayer(index, true, ArriveAtNode);
        }

        /// <summary>棋子走到了：有场景就切过去，没有就当场结算这一步。</summary>
        private void ArriveAtNode()
        {
            if (_view != null)
            {
                _view.RefreshStates();
            }

            string scene = MapRun.PendingScene();
            if (string.IsNullOrEmpty(scene))
            {
                MapRun.SettleReturn();
                _busy = false;
                _view.PlacePlayer(MapRun.CurrentIndex, false, null);
                _view.RefreshStates();
                _view.SetHint(DefaultHint());

                if (MapRun.IsOver)
                {
                    _view.SetHint(string.Empty);
                    _view.ShowEnd();
                }

                return;
            }

            StartCoroutine(LoadSceneAfterHold(scene));
        }

        /// <summary>
        /// 停一下再切场景。
        ///
        /// <para>不留这一下的话，「走到哪一格」这件事玩家根本来不及看 —— 位移刚结束画面就换了。
        /// 时长是 <see cref="UiLayout.MapMoveHoldSeconds"/>（0.24 s）。</para>
        /// </summary>
        private System.Collections.IEnumerator LoadSceneAfterHold(string scene)
        {
            if (!_skipWalkAnimation)
            {
                yield return new WaitForSecondsRealtime(UiLayout.MapMoveHoldSeconds);
            }

            Log("切到场景 " + scene);
            SceneManager.LoadScene(scene);
        }

        private void OnRestartClicked()
        {
            MapRun.Clear();
            MapRun.StartNew(NextSeed(true));
            _busy = false;
            _view.HideEnd();
            Boot();
            _view.SetHint(DefaultHint());
            Log("重新开一趟 · seed " + MapRun.Seed);
        }

        private static string DefaultHint()
        {
            return "点亮的节点可以前往　·　营地出发，一路走到 Boss";
        }

        private string DescribeRun()
        {
            return "进度 " + MapRun.Progress + "/" + MapRun.ProgressTotal
                   + " · 已走过 " + MapRun.Resolved.Count + " 个节点 · 金币 " + MapRun.Gold
                   + (MapRun.IsOver ? (MapRun.Won ? " · 已通关" : " · 已失败") : string.Empty);
        }

        private void Log(string message)
        {
            if (_logClicks)
            {
                Debug.Log("[MapSceneEntry] " + message);
            }
        }
    }
}
