using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 一张生成好的冒险地图（2026-10-02）。
    ///
    /// <para><b>它是什么</b>：一个「分层 DAG」—— 节点按层（<see cref="MapNode.Layer"/>）
    /// 排列，边<b>只从第 L 层连到第 L+1 层</b>（永不回边、永不跨层）。
    /// 起点层与 Boss 层各只有 1 个节点，中间层 2–4 个。</para>
    ///
    /// <para><b>四条不变量</b>（生成器负责，<c>MapScenario</c> 逐条验、1000 个种子）：
    /// <list type="number">
    /// <item>起点唯一（<see cref="MapNodeType.Camp"/>）、终点唯一（<see cref="MapNodeType.Boss"/>）；</item>
    /// <item>每条边的两端行号差 <b>≤ 1</b> —— 这是「桥一定画得出来」的几何前提；</item>
    /// <item>没有交叉边（同两层之间不存在 <c>(a−c)·(b−d) &lt; 0</c> 的两条边）；</item>
    /// <item>每个节点都从起点可达、也都能走到 Boss（没有死路、没有孤岛）。</item>
    /// </list></para>
    ///
    /// <para><b>⚠ 这是「结果」而不是「模板 + 种子」</b>：正式接入冒险时要<b>整份存进存档</b>
    /// （<c>Docs/design/冒险模式实施规格.md</c> §6.2 第 9 条）——
    /// 只存种子会在版本变化后悄悄换图，而玩家已经走过一半了。</para>
    /// </summary>
    public sealed class MapGraph
    {
        /// <summary>生成这张图用的种子（同种子 → 同图）。</summary>
        public int Seed;

        /// <summary>层数（含起点层与 Boss 层）。默认 9：起点 + 7 个中间层 + Boss。</summary>
        public readonly int LayerCount;

        /// <summary>每层最多几行（= 中间层的最大宽度）。默认 4。</summary>
        public readonly int RowCount;

        /// <summary>全部节点。下标 = <see cref="MapNode.Index"/>。</summary>
        public readonly List<MapNode> Nodes;

        /// <summary><c>_next[i]</c> = 第 i 个节点能去哪些节点（层 L → L+1）。</summary>
        private readonly List<int>[] _next;

        /// <summary><c>_prev[i]</c> = 哪些节点能到第 i 个节点。</summary>
        private readonly List<int>[] _prev;

        /// <summary>总边数。</summary>
        public int EdgeCount { get; private set; }

        internal MapGraph(int seed, int layerCount, int rowCount, List<MapNode> nodes)
        {
            Seed = seed;
            LayerCount = layerCount;
            RowCount = rowCount;
            Nodes = nodes;

            _next = new List<int>[nodes.Count];
            _prev = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                _next[i] = new List<int>();
                _prev[i] = new List<int>();
            }
        }

        internal void AddEdge(int from, int to)
        {
            if (!_next[from].Contains(to))
            {
                _next[from].Add(to);
                EdgeCount++;
            }

            if (!_prev[to].Contains(from))
            {
                _prev[to].Add(from);
            }
        }

        /// <summary>第 i 个节点能去的节点（第 L+1 层）。</summary>
        public IReadOnlyList<int> Next(int index)
        {
            return _next[index];
        }

        /// <summary>能到第 i 个节点的节点（第 L−1 层）。</summary>
        public IReadOnlyList<int> Prev(int index)
        {
            return _prev[index];
        }

        /// <summary>起点（营地）节点的下标。生成器保证存在。</summary>
        public int StartIndex
        {
            get
            {
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (Nodes[i].IsStart)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        /// <summary>Boss 节点的下标。生成器保证存在。</summary>
        public int BossIndex
        {
            get
            {
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (Nodes[i].IsBoss)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        /// <summary>找 (层, 行) 上的节点下标；没有则 −1。</summary>
        public int IndexOf(int layer, int row)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Layer == layer && Nodes[i].Row == row)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>某一层上的全部节点（按行升序）。</summary>
        public List<MapNode> NodesInLayer(int layer)
        {
            var list = new List<MapNode>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Layer == layer)
                {
                    list.Add(Nodes[i]);
                }
            }

            list.Sort(CompareByRow);
            return list;
        }

        private static int CompareByRow(MapNode a, MapNode b)
        {
            return a.Row.CompareTo(b.Row);
        }

        /// <summary>
        /// 起点到每个节点是否可达（广度优先，只沿出边走）。
        /// 自测用它验「没有孤岛」；运行期不用 —— 别拿它当每帧的判据。
        /// </summary>
        public bool[] ReachableFromStart()
        {
            var seen = new bool[Nodes.Count];
            int start = StartIndex;
            if (start < 0)
            {
                return seen;
            }

            var queue = new Queue<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                List<int> outs = _next[cur];
                for (int i = 0; i < outs.Count; i++)
                {
                    if (!seen[outs[i]])
                    {
                        seen[outs[i]] = true;
                        queue.Enqueue(outs[i]);
                    }
                }
            }

            return seen;
        }

        /// <summary>每个节点能不能走到 Boss（把边反向后的广度优先）。自测用。</summary>
        public bool[] CanReachBoss()
        {
            var seen = new bool[Nodes.Count];
            int boss = BossIndex;
            if (boss < 0)
            {
                return seen;
            }

            var queue = new Queue<int>();
            seen[boss] = true;
            queue.Enqueue(boss);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                List<int> ins = _prev[cur];
                for (int i = 0; i < ins.Count; i++)
                {
                    if (!seen[ins[i]])
                    {
                        seen[ins[i]] = true;
                        queue.Enqueue(ins[i]);
                    }
                }
            }

            return seen;
        }
    }
}
