using System.Collections.Generic;
using MagicBrawl.Core;

namespace MagicBrawl.App
{
    /// <summary>地图上一个节点当前处于什么状态（表现层只认这一套）。</summary>
    public enum MapNodeState
    {
        /// <summary>去不了（不在当前节点的下一层，或已经过了那一层）。压暗、不吃射线。</summary>
        Locked = 0,

        /// <summary>可以去。<b>唯一能点的状态</b>。</summary>
        Reachable = 1,

        /// <summary>玩家正站在这里。</summary>
        Current = 2,

        /// <summary>已经走过了。压暗、不吃射线。</summary>
        Resolved = 3,
    }

    /// <summary>
    /// 一趟冒险的<b>跨场景运行态</b>（2026-10-02）。
    ///
    /// <para><b>为什么是静态类而不是 MonoBehaviour</b>：地图 / 战斗 / 商店 / 石台 / 女巫工坊
    /// 是<b>五个独立场景</b>（<c>Docs/design/冒险事件架构.md</c> §1 A3）——
    /// 换场景会把场景里的对象全部销毁，所以「我走到哪儿了、金币多少」必须放在
    /// <b>不随场景销毁</b>的地方。这里选了「静态字段」而不是
    /// <c>DontDestroyOnLoad</c> 的单例：它没有生命周期、没有 Awake 顺序问题，
    /// 而且每个字段都是可读值，不会被谁在背地里 Reset。</para>
    ///
    /// <para><b>⚠ 它现在只在一次 Play 内有效</b>（退出 Play / 重开编辑器就归零）。
    /// 真正的存档要落盘（<c>冒险模式实施规格.md</c> §5.5），那是 P6 的事；
    /// 本轮的目标是「五个场景能串成一条链路」，不是「能存能读」。
    /// 刷新一下就能重开一局，反而方便调试。</para>
    ///
    /// <para><b>⚠ 三条铁律</b>：</para>
    /// <list type="number">
    /// <item><b>能点哪些节点是这里算的</b>（<see cref="IsReachable"/>）——
    /// 地图不许自己再判一遍「这一步能不能走」（铁律 3）；</item>
    /// <item><b>事件场景只在 <see cref="HasActiveRun"/> 为真时才认这里</b> ——
    /// 单独打开 <c>Shop.unity</c> 调试时这个类整个是空的，商店仍按它自己的调试参数跑
    /// （独立场景契约 E1/E2）；</item>
    /// <item>写状态的方法只有下面这几个，别在界面里直接改字段。</item>
    /// </list>
    /// </summary>
    public static class MapRun
    {
        /// <summary>开局金币。与商店场景的调试默认值同值（50），这样两边读起来是一致的。</summary>
        public const int DefaultGold = 50;

        /// <summary>当前这趟冒险的地图。<c>null</c> = 没有冒险在跑。</summary>
        public static MapGraph Graph;

        /// <summary>生成这张图用的种子。</summary>
        public static int Seed;

        /// <summary>玩家当前所在的节点下标（−1 = 还没出发）。</summary>
        public static int CurrentIndex = -1;

        /// <summary>
        /// 玩家<b>正在进入</b>的节点下标（−1 = 没有）。
        ///
        /// <para>从「点了节点」到「从那个节点的场景回到地图」这一整段里它都不为 −1 ——
        /// 所以 <see cref="HasPending"/> 同时表达了两件事：「玩家在路上」与「有一趟外出没回来」。
        /// 地图读到它就知道该结算这个节点，而不是重开一局。</para>
        /// </summary>
        public static int PendingIndex = -1;

        /// <summary>已经走过（完成）的节点。</summary>
        public static readonly HashSet<int> Resolved = new HashSet<int>();

        /// <summary>金币。商店买卡会写回这里，地图顶栏读它。</summary>
        public static int Gold = DefaultGold;

        /// <summary>战斗场景报回来的一局结果是否还没被地图消费。</summary>
        public static bool BattleResultPending;

        /// <summary>那一局赢了没有（只在 <see cref="BattleResultPending"/> 为真时有意义）。</summary>
        public static bool BattleWon;

        /// <summary>整趟冒险已经打完 Boss（通关）。</summary>
        public static bool Won;

        /// <summary>整趟冒险已经结束（战斗失败）。</summary>
        public static bool Lost;

        /// <summary>有没有一张图（不管是新的还是已经打完的）。</summary>
        public static bool HasRun
        {
            get { return Graph != null; }
        }

        /// <summary>这趟冒险是不是已经终局。</summary>
        public static bool IsOver
        {
            get { return Won || Lost; }
        }

        /// <summary>
        /// 有没有一趟<b>还在进行</b>的冒险 —— 事件场景与战斗场景判断
        /// 「我是不是从地图进来的」就看这一个。
        /// </summary>
        public static bool HasActiveRun
        {
            get { return Graph != null && !IsOver; }
        }

        /// <summary>玩家正在进入一个节点（还没回来）。</summary>
        public static bool HasPending
        {
            get { return Graph != null && PendingIndex >= 0 && PendingIndex < Graph.Nodes.Count; }
        }

        /// <summary>
        /// 当前这趟外出<b>是去打架</b>（普通战斗或 Boss）。
        /// 战斗场景用它决定结算面板上那个键是「再来一局」还是「回到地图」。
        /// </summary>
        public static bool InMapBattle
        {
            get
            {
                if (!HasPending)
                {
                    return false;
                }

                MapNodeType type = Graph.Nodes[PendingIndex].Type;
                return type == MapNodeType.Battle || type == MapNodeType.Boss;
            }
        }

        /// <summary>当前所在节点是几层（1..LayerCount−1；0 = 还在起点）。</summary>
        public static int Progress
        {
            get
            {
                if (!HasRun || CurrentIndex < 0 || CurrentIndex >= Graph.Nodes.Count)
                {
                    return 0;
                }

                return Graph.Nodes[CurrentIndex].Layer;
            }
        }

        /// <summary>终点层号（显示「进度 x / y」的分母）。</summary>
        public static int ProgressTotal
        {
            get { return HasRun ? Graph.LayerCount - 1 : 0; }
        }

        /// <summary>开一趟新的冒险（换一张图）。</summary>
        public static void StartNew(int seed)
        {
            Graph = MapGenerator.Generate(seed);
            Seed = seed;
            Resolved.Clear();

            CurrentIndex = Graph.StartIndex;
            PendingIndex = -1;
            if (CurrentIndex >= 0)
            {
                Resolved.Add(CurrentIndex);      // 起点是「已经在这儿了」，不是待完成的节点
            }

            Gold = DefaultGold;
            BattleResultPending = false;
            BattleWon = false;
            Won = false;
            Lost = false;
        }

        /// <summary>把这趟冒险整个忘掉（回到「没有冒险」的初始态）。</summary>
        public static void Clear()
        {
            Graph = null;
            Seed = 0;
            CurrentIndex = -1;
            PendingIndex = -1;
            Resolved.Clear();
            Gold = DefaultGold;
            BattleResultPending = false;
            BattleWon = false;
            Won = false;
            Lost = false;
        }

        /// <summary>这个节点走过没有。</summary>
        public static bool IsResolved(int index)
        {
            return Resolved.Contains(index);
        }

        /// <summary>
        /// 这个节点现在可不可以去。
        ///
        /// <para><b>唯一的判据</b>：它必须是「当前节点的<b>直接后继</b>」且没走过。
        /// 也就是说玩家不能跳层、不能往回走、不能在同一层里横跳 ——
        /// 地图上的高亮与点击<b>都</b>调这一个函数，不存在第二处判断。</para>
        /// </summary>
        public static bool IsReachable(int index)
        {
            // HasActiveRun 已经把「没图 / 已通关 / 已失败」三种都挡掉了。
            if (!HasActiveRun || HasPending)
            {
                return false;
            }

            if (index < 0 || index >= Graph.Nodes.Count || Resolved.Contains(index))
            {
                return false;
            }

            if (CurrentIndex < 0 || CurrentIndex >= Graph.Nodes.Count)
            {
                return false;
            }

            IReadOnlyList<int> outs = Graph.Next(CurrentIndex);
            for (int i = 0; i < outs.Count; i++)
            {
                if (outs[i] == index)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>算一个节点当前的状态（<see cref="MapNodeView"/> 只按它切显示）。</summary>
        public static MapNodeState StateOf(int index)
        {
            if (Graph == null || index < 0 || index >= Graph.Nodes.Count)
            {
                return MapNodeState.Locked;
            }

            if (index == CurrentIndex)
            {
                return MapNodeState.Current;
            }

            if (Resolved.Contains(index))
            {
                return MapNodeState.Resolved;
            }

            return IsReachable(index) ? MapNodeState.Reachable : MapNodeState.Locked;
        }

        /// <summary>
        /// 玩家点了某个可达节点：<b>开始走过去</b>。
        /// 这时候还<b>不</b>算完成 —— 完成要等走到 / 从那个节点的场景回来，
        /// 见 <see cref="SettleReturn"/>。
        /// </summary>
        public static bool BeginEnter(int index)
        {
            if (!IsReachable(index))
            {
                return false;
            }

            PendingIndex = index;
            return true;
        }

        /// <summary>正在进入的那个节点要用哪个场景；<c>null</c> = 不需要切场景。</summary>
        public static string PendingScene()
        {
            if (!HasPending)
            {
                return null;
            }

            return MapRoutes.SceneFor(Graph.Nodes[PendingIndex].Type);
        }

        /// <summary>
        /// 玩家从那个节点的场景回到地图了（或者那个节点本来就没有场景）：
        /// <b>结算这一步</b> —— 标成走过、把玩家挪过去。
        ///
        /// <para>结算结果有三种：普通节点 / 战斗赢 / 战斗输或打完 Boss。
        /// 战斗结果由战斗场景先写进来（<see cref="ReportBattle"/>）。</para>
        /// </summary>
        public static void SettleReturn()
        {
            if (!HasPending)
            {
                BattleResultPending = false;
                return;
            }

            MapNodeType type = Graph.Nodes[PendingIndex].Type;
            bool isBattle = type == MapNodeType.Battle || type == MapNodeType.Boss;

            if (isBattle && BattleResultPending)
            {
                bool won = BattleWon;
                BattleResultPending = false;

                if (!won)
                {
                    Finish(false);
                    return;
                }

                if (type == MapNodeType.Boss)
                {
                    Finish(true);
                    return;
                }
            }

            Arrive();
        }

        private static void Arrive()
        {
            Resolved.Add(PendingIndex);
            CurrentIndex = PendingIndex;
            PendingIndex = -1;
        }

        private static void Finish(bool won)
        {
            if (PendingIndex >= 0)
            {
                Resolved.Add(PendingIndex);
                CurrentIndex = PendingIndex;
            }

            PendingIndex = -1;
            BattleResultPending = false;
            Won = won;
            Lost = !won;
        }

        /// <summary>战斗场景在结算那一刻调它，把这一局的胜负留给地图。</summary>
        public static void ReportBattle(bool won)
        {
            BattleResultPending = true;
            BattleWon = won;
        }

        /// <summary>商店买卡之后把余额写回来（地图顶栏显示的就是它）。</summary>
        public static void SetGold(int gold)
        {
            Gold = gold < 0 ? 0 : gold;
        }
    }
}
