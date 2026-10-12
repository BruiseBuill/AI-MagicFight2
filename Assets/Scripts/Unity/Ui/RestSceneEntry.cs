using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 休息事件场景的<b>单场景入口</b>（2026-10-09 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>它做什么</b>：把「点休息」这一下翻译成「把局外当前生命写满」
    /// （<see cref="MapRun.RestoreFullHp"/>），并把大键切成「已休息」。
    /// 点「离开」＝完成这个节点并回地图。</para>
    ///
    /// <para><b>为什么需要它</b>：与宝箱（<c>TreasureSceneEntry</c>）逐条同源 ——
    /// 休息是冒险模式里的一个事件节点，正式流程中它会从地图层接到一份 <c>RunSnapshot</c>；
    /// 而那套链路还没接，每个事件类型又都必须能被<b>单独进 Play 调试</b>（架构文档 §3），
    /// 否则想知道「点一下到底回不回血」就得先把整条冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：进 Play 什么都不用先准备，黑屏 + 那个键就是全部内容；</item>
    /// <item><b>E2 不依赖前一节点</b>：不读路线、不假设第几层；</item>
    /// <item><b>E3 不写真实存档</b>：本事件<b>只在内存里改这一趟冒险的生命</b>
    /// （<see cref="MapRun.RestoreFullHp"/>），不碰任何落盘文件 ——
    /// 「一个存档 = 一个玩家卡池」那条线跟生命无关；</item>
    /// <item><b>E4 Core 一行不改</b>：这里没有第二份规则实现 ——
    /// 「恢复满」的写法只有 <see cref="MapRun.RestoreFullHp"/> 一处。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：正式接入时这一次回血应当走冒险命令
    /// （与战斗回写生命是同一本账）。现在直接写 <see cref="MapRun.RestoreFullHp"/> ——
    /// 与商店扣钱 / 宝箱加钱的做法逐字同源，所以地图顶栏读到的生命是对的。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：构建器把它挂在 <b>Canvas 的父级（场景根）</b>，
    /// 而存 Prefab 时只保存 Canvas 那一棵子树 —— 它天然不会进 Prefab。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RestSceneEntry : MonoBehaviour
    {
        // ── 调试参数 ─────────────────────────────────────────────────

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「点休息 / 离开」两下打到 Console —— 方便在单场景里确认事件通了"
                 + "（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logClicks = true;

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private RestView _view;

        /// <summary>这一趟事件里已经休息过（再点不重复回血）。</summary>
        private bool _rested;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<RestView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[RestSceneEntry] 没有接 RestView —— "
                               + "先跑 `魔法乱斗/P9 · 构建 Rest 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）。
            _view.RestClicked += OnRestClicked;
            _view.LeaveClicked += OnLeaveClicked;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.RestClicked -= OnRestClicked;
            _view.LeaveClicked -= OnLeaveClicked;
        }

        /// <summary>把事件恢复到「还没休息」的初始态（Inspector 的右键菜单也可以调）。</summary>
        [ContextMenu("重置事件")]
        public void Rebuild()
        {
            if (_view == null)
            {
                return;
            }

            _rested = false;
            _view.SetTitle("休息");
            _view.SetLeaveLabel("离开");
            _view.SetRestLabel("休息");
            _view.SetRestInteractable(true);
            _view.SetHint("点击休息，恢复全部生命");
            _view.SetHintVisible(true);

            Log("休息事件 · 恢复满生命 · "
                + (MapRun.HasActiveRun
                    ? "当前生命 " + MapRun.CurrentHp + " / " + MapRun.CurrentHpMax
                    : "单场景调试（没有冒险在跑，不记账）"));
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 休息。<b>只回一次血</b> —— 视图那把大键已经关掉可点，这里是第二道闸
        /// （双击 / 脚本误调都不会重复记账）。
        /// </summary>
        private void OnRestClicked()
        {
            if (_rested)
            {
                return;
            }

            _rested = true;
            _view.SetRestLabel("已休息");
            _view.SetRestDone();

            // 2026-10-09：从地图走进来的话，这一次回血写在**冒险那本账**上 ——
            // 地图顶栏读的就是它，所以「休息完回地图，生命满了」是通的。
            // ⚠ 单向：只在 HasActiveRun 时写。单独打开 Rest.unity 调试时 MapRun 是空的，
            //   就只打日志（独立场景契约 E1/E2/E3）。
            if (MapRun.HasActiveRun)
            {
                MapRun.RestoreFullHp();
                _view.SetHint("生命已恢复满（" + MapRun.CurrentHp + " / " + MapRun.CurrentHpMax + "）");
                Log("休息 → 生命恢复满（" + MapRun.CurrentHp + " / " + MapRun.CurrentHpMax + "）。");
                return;
            }

            _view.SetHint("生命已恢复满（单场景调试：没有冒险在跑，只打日志不记账）");
            Log("休息（单场景调试：没有冒险在跑，只打日志不记账）。");
        }

        private void OnLeaveClicked()
        {
            // 2026-10-02 起：从地图走进来的话，「离开」= 完成这个节点并回地图
            //（地图那边 MapSceneEntry.Awake 会结算这一步）。
            if (MapRoutes.LeaveToMap())
            {
                Log("离开休息点，回地图（生命 " + MapRun.CurrentHp + " / " + MapRun.CurrentHpMax + "）。");
                return;
            }

            Log("点了「离开」—— 单场景调试下到此为止；从地图走进来时这一下会回地图层。");
        }

        private void Log(string message)
        {
            if (_logClicks)
            {
                Debug.Log("[RestSceneEntry] " + message);
            }
        }
    }
}
