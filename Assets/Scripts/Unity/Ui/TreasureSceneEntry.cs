using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>
    /// 宝箱事件场景的<b>单场景入口</b>（2026-10-09 · 独立场景契约 E1–E4）。
    ///
    /// <para><b>它做什么</b>：把「点箱子」这一下翻译成「把箱子换成开着的图 + 弹 +N +
    /// 往这一趟冒险的账上加 N 金」。点「离开」＝完成这个节点并回地图。</para>
    ///
    /// <para><b>为什么需要它</b>：宝箱是冒险模式里的一个事件节点，正式流程中它会从地图层
    /// 接到一份 <c>RunSnapshot</c>。而那套链路还没接，每个事件类型又都必须能被<b>单独进
    /// Play 调试</b>（架构文档 §3），否则想知道「点一下箱子到底加不加钱」就得先把整条
    /// 冒险链路跑通。</para>
    ///
    /// <para><b>独立场景契约</b>（<c>Docs/design/冒险事件架构.md</c> §3）：</para>
    /// <list type="number">
    /// <item><b>E1 不依赖地图</b>：进 Play 什么都不用先准备，箱子本身就是全部内容；</item>
    /// <item><b>E2 不依赖前一节点</b>：不读路线、不假设第几层；</item>
    /// <item><b>E3 不写真实存档</b>：本事件<b>只在内存里改这一趟冒险的余额</b>
    /// （<see cref="MapRun.SetGold"/>），不碰任何落盘文件 —— 「一个存档 = 一个玩家卡池」
    /// 那条线跟金币无关；</item>
    /// <item><b>E4 Core 一行不改</b>：这里没有第二份规则实现 —— 金币数额是内容
    /// （<c>UiLayout.TreasureGoldGain</c>，将来搬进冒险配置），加减法只有这一处。</item>
    /// </list>
    ///
    /// <para><b>⚠ 与正式链路的边界</b>：正式接入时这一笔应当走冒险命令
    /// （与商店扣钱是同一本账）。现在直接写 <see cref="MapRun.SetGold"/> ——
    /// 与商店 <c>ShopSceneEntry</c> 的做法逐字同源，所以地图顶栏读到的余额是对的。</para>
    ///
    /// <para><b>⚠ 本类不参与最终的 Prefab</b>：构建器把它挂在 <b>Canvas 的父级（场景根）</b>，
    /// 而存 Prefab 时只保存 Canvas 那一棵子树 —— 它天然不会进 Prefab。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreasureSceneEntry : MonoBehaviour
    {
        // ── 调试参数（正式接入后由地图层 / 存档给）────────────────────

        [Header("调试 · 内容")]
        [Tooltip("开一次箱子给多少金币。默认 = UiLayout.TreasureGoldGain（用户口径 50）。\n"
                 + "想测「给 0 块 / 给一大笔」在 Inspector 里改这里即可。")]
        [SerializeField] private int _gain = UiLayout.TreasureGoldGain;

        [Header("调试 · 入口")]
        [Tooltip("进 Play 后把「点箱子 / 离开」两下打到 Console —— 方便在单场景里确认事件通了"
                 + "（离开的真实语义是回地图层）。")]
        [SerializeField] private bool _logClicks = true;

        // ── 视图 ─────────────────────────────────────────────────────

        [SerializeField] private TreasureView _view;

        /// <summary>这一趟事件里箱子已经被打开过（再点不重复给钱）。</summary>
        private bool _opened;

        private void Awake()
        {
            if (_view == null)
            {
                _view = GetComponentInChildren<TreasureView>(true);
            }

            if (_view == null)
            {
                Debug.LogError("[TreasureSceneEntry] 没有接 TreasureView —— "
                               + "先跑 `魔法乱斗/P8 · 构建 Treasure 场景`。");
                return;
            }

            // ⚠ 监听器一律在运行时按 SerializeField 自己接（铁律 8）。
            _view.ChestClicked += OnChestClicked;
            _view.LeaveClicked += OnLeaveClicked;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_view == null)
            {
                return;
            }

            _view.ChestClicked -= OnChestClicked;
            _view.LeaveClicked -= OnLeaveClicked;
        }

        /// <summary>把事件恢复到「箱子还关着」的初始态（Inspector 的右键菜单也可以调）。</summary>
        [ContextMenu("重置事件")]
        public void Rebuild()
        {
            if (_view == null)
            {
                return;
            }

            _opened = false;
            _view.SetTitle("宝箱");
            _view.SetLeaveLabel("离开");
            _view.SetHint("点击宝箱，把它打开");
            _view.SetHintVisible(true);
            _view.HideGain();
            _view.SetChestInteractable(true);

            Log("宝箱事件 · 开箱给 " + _gain + " 金 · "
                + (MapRun.HasActiveRun
                    ? "冒险余额 " + MapRun.Gold
                    : "单场景调试（没有冒险在跑，不记账）"));
        }

        // ══════════════════════════════════════════════════════
        //  交互
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 开箱。<b>只给一次钱</b> —— 视图开过之后就把点击关掉了，这里是第二道闸
        /// （双击 / 脚本误调都不会重复记账）。
        /// </summary>
        private void OnChestClicked()
        {
            if (_opened)
            {
                return;
            }

            _opened = true;
            _view.SetOpened();
            _view.ShowGain(_gain);
            _view.SetHint("箱子里装着 " + _gain + " 枚金币，已经归你了");

            // 2026-10-09：从地图走进来的话，这一笔记进**冒险那本账** ——
            // 地图顶栏读的就是它，所以「开完箱回地图，金币多了 50」是通的。
            // ⚠ 单向：只在 HasActiveRun 时写。单独打开 Treasure.unity 调试时 MapRun 是空的，
            //   就只打日志（独立场景契约 E1/E2/E3）。
            if (MapRun.HasActiveRun)
            {
                MapRun.SetGold(MapRun.Gold + _gain);
                Log("开箱 +" + _gain + " 金 → 冒险余额 " + MapRun.Gold + "。");
                return;
            }

            Log("开箱 +" + _gain + " 金（单场景调试：没有冒险在跑，只打日志不记账）。");
        }

        private void OnLeaveClicked()
        {
            // 2026-10-02 起：从地图走进来的话，「离开」= 完成这个节点并回地图
            //（地图那边 MapSceneEntry.Awake 会结算这一步）。
            if (MapRoutes.LeaveToMap())
            {
                Log("离开宝箱，回地图（余额 " + MapRun.Gold + " 金）。");
                return;
            }

            Log("点了「离开」—— 单场景调试下到此为止；从地图走进来时这一下会回地图层。");
        }

        private void Log(string message)
        {
            if (_logClicks)
            {
                Debug.Log("[TreasureSceneEntry] " + message);
            }
        }
    }
}
