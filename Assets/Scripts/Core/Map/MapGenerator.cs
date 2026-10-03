using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 冒险地图生成器（2026-10-02 · 用户口径）。
    ///
    /// <para><b>用户要的四条</b>（原话）：</para>
    /// <list type="number">
    /// <item>「地图是随机生成的，除了起始节点和最终的节点」→ 起点固定
    /// <see cref="MapNodeType.Camp"/>、终点固定 <see cref="MapNodeType.Boss"/>，中间全随机；</item>
    /// <item>「起始节点一定是 camp」→ 见上；</item>
    /// <item>「中间虽然是随机生成，但摆的位置一定要能够用桥连接上」→ 见下「行号差 ≤ 1」；</item>
    /// <item>「玩家从左下角出发一直到右上角迎接 Boss 战」→ 起点在第 0 层第 0 行（左下），
    /// Boss 在最后一层最后一行（右上），中间层的行号整体随层号上漂。</item>
    /// </list>
    ///
    /// <para><b>算法：Slay-the-Spire 式的「多条路径并行游走」</b>。不是「先定节点再连边」，
    /// 而是反过来 —— 先撒 <see cref="DefaultPathCount"/> 条从起点走到 Boss 的完整路径，
    /// <b>路径经过的格子就是节点、路径的每一步就是一条边</b>。这样「每个节点都能从起点走到、
    /// 也都能走到 Boss」是<b>构造出来的</b>，不需要事后修补（事后补边正是「连出一条死路」的来源）。</para>
    ///
    /// <para><b>让它「一定画得出桥」的那条约束</b>：每一步的行号变化只允许 ∈ {−1, 0, +1}，
    /// 且同两层之间不允许交叉边。于是任意一条边的两端，在版式上永远落在
    /// 「同一行 / 上下邻行」这两种相对位置之一 —— 而表现层对这两种位置各有一套画法
    /// （见 <c>MapView.BuildBridges</c>）。（第三处保证是版式：行距 144、列距 190 都远大于节点直径。）</para>
    ///
    /// <para><b>⚠ 「爬升」是硬约束不是偏好</b>：Boss 固定在最后一行，所以倒数第 L 层的节点
    /// <b>必须</b>在第 <c>bossRow − (last − L)</c> 行之上，否则最后一步会出现「行号差 &gt; 1」——
    /// 那样的图<b>会被整张丢掉重生成</b>（<see cref="MaxAttempts"/> 次）。这条「最晚什么时候必须爬到哪」
    /// 由 <see cref="NeedRow"/> 一处给出，走路径时每步都夹一次。</para>
    /// </summary>
    public static class MapGenerator
    {
        /// <summary>层数（含起点层与 Boss 层）。9 = 起点 + 7 个中间层 + Boss。</summary>
        public const int DefaultLayerCount = 9;

        /// <summary>每层最多几行（= 中间层最大宽度）。</summary>
        public const int DefaultRowCount = 4;

        /// <summary>撒几条路径。越多 → 越宽、分岔越多。</summary>
        public const int DefaultPathCount = 6;

        /// <summary>
        /// 中间层的最少节点数（口径来自 <c>Docs/design/冒险模式实施规格.md</c> §6.1 的
        /// <c>MinWidth = 2</c>）。
        ///
        /// <para><b>为什么必须有下限</b>：宽度 1 的那一层是**单行道** —— 玩家在那里没有选择，
        /// 而且从版式上看那一层会「空掉一边」，像漏画了节点。6 条路径独立随机时，
        /// 「6 条全选了同一行」的概率约 3%，7 个中间层乘起来约 20% 的图会中招 ——
        /// 所以它<b>不是</b>理论风险，而是每五张图就有一张。
        /// <see cref="Builder.Validate"/> 会把它当硬约束，不过就整张重生成。</para>
        /// </summary>
        public const int MinLayerWidth = 2;

        /// <summary>单张图最多重生成几次。超限就走 <see cref="BuildFallback"/>。</summary>
        public const int MaxAttempts = 200;

        // 节点类型权重（只在第 2 层..倒数第 2 层之间生效）。
        // 第 1 层固定普通战斗（与 `冒险模式实施规格.md` §6.3「第 1 层禁连」同口径）。
        private const int WBattle = 46;
        private const int WUnknown = 18;
        private const int WShop = 14;
        private const int WWitch = 12;
        private const int WAltar = 10;

        /// <summary>用默认尺寸生成一张图。</summary>
        public static MapGraph Generate(int seed)
        {
            return Generate(seed, DefaultLayerCount, DefaultRowCount, DefaultPathCount);
        }

        public static MapGraph Generate(int seed, int layerCount, int rowCount, int pathCount)
        {
            if (layerCount < 3)
            {
                layerCount = 3;
            }

            if (rowCount < 2)
            {
                rowCount = 2;
            }

            if (pathCount < 1)
            {
                pathCount = 1;
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var builder = new Builder(Mix(seed, attempt), layerCount, rowCount, pathCount);
                MapGraph graph = builder.Build();
                if (graph != null)
                {
                    graph.Seed = seed;
                    return graph;
                }
            }

            MapGraph fallback = BuildFallback(seed, layerCount, rowCount);
            return fallback;
        }

        /// <summary>
        /// 第 <paramref name="layer"/> 层的节点<b>至少</b>要在第几行，才能在第
        /// <paramref name="last"/> 层（Boss 层，行 <paramref name="bossRow"/>）被一步走到。
        /// </summary>
        private static int NeedRow(int layer, int last, int bossRow)
        {
            int remaining = last - layer;
            int need = bossRow - remaining;
            return need < 0 ? 0 : need;
        }

        /// <summary>把 (种子, 第几次尝试) 混成一个新种子 —— 保证「同种子同图」。</summary>
        private static int Mix(int seed, int attempt)
        {
            unchecked
            {
                ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL
                          + (ulong)(uint)(attempt + 1) * 0xD6E8FEB86659FD93UL;
                z ^= z >> 33;
                z *= 0xFF51AFD7ED558CCDUL;
                z ^= z >> 33;
                z *= 0xC4CEB9FE1A85EC53UL;
                z ^= z >> 33;
                return (int)(uint)(z ^ (z >> 32));
            }
        }

        /// <summary>
        /// 兜底图：<b>两条平行的链</b>（起点 → 两根链 → Boss）。
        /// <see cref="MaxAttempts"/> 次都失败才会走到这里 —— 正常永远不触发，
        /// 所以刻意做成「形状最笨但一定合法」而不是「尽量好看」。
        /// </summary>
        private static MapGraph BuildFallback(int seed, int layerCount, int rowCount)
        {
            int last = layerCount - 1;
            int bossRow = rowCount - 1;

            var nodes = new List<MapNode>();
            var at = new int[layerCount, 2];

            for (int L = 0; L < layerCount; L++)
            {
                if (L == 0 || L == last)
                {
                    at[L, 0] = AddNode(nodes, L, L == 0 ? 0 : bossRow, nodes.Count);
                    at[L, 1] = -1;
                    continue;
                }

                int baseRow = L * bossRow / last;
                if (baseRow > bossRow)
                {
                    baseRow = bossRow;
                }

                at[L, 0] = AddNode(nodes, L, baseRow, nodes.Count);
                int second = baseRow + 1;
                at[L, 1] = second <= bossRow ? AddNode(nodes, L, second, nodes.Count) : -1;
            }

            var graph = new MapGraph(seed, layerCount, rowCount, nodes);
            for (int L = 0; L < last; L++)
            {
                for (int i = 0; i < 2; i++)
                {
                    int from = at[L, i];
                    if (from < 0)
                    {
                        continue;
                    }

                    // 同一条链往下走；末层两条链汇到 Boss。
                    if (L + 1 == last)
                    {
                        graph.AddEdge(from, at[last, 0]);
                        continue;
                    }

                    int straight = at[L + 1, i] >= 0 ? at[L + 1, i] : at[L + 1, 0];
                    graph.AddEdge(from, straight);
                }
            }

            AssignTypes(graph, new Rng(Mix(seed, 7919)), layerCount, rowCount);
            return graph;
        }

        private static int AddNode(List<MapNode> nodes, int layer, int row, int index)
        {
            nodes.Add(new MapNode(index, layer, row, MapNodeType.Battle));
            return index;
        }

        /// <summary>
        /// 分配节点类型（用户口径：起点 Camp、终点 Boss、中间随机）。
        ///
        /// <para><b>三项保底</b>：整张图至少各有 1 个商店 / 女巫 / 石台 ——
        /// 理由很实际：这三张节点图是用户专门做的，如果某次随机一个都没抽到，
        /// 玩家就整局看不到它们；而「随机」在这轮要表达的是<b>路线与分岔</b>，
        /// 不是「这次没有商店」。正式接入冒险经济系统时这两行可以去掉。</para>
        /// </summary>
        private static void AssignTypes(MapGraph graph, Rng rng, int layerCount, int rowCount)
        {
            int last = layerCount - 1;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                MapNode node = graph.Nodes[i];
                if (node.Layer == 0)
                {
                    node.Type = MapNodeType.Camp;
                }
                else if (node.Layer == last)
                {
                    node.Type = MapNodeType.Boss;
                }
                else if (node.Layer == 1)
                {
                    node.Type = MapNodeType.Battle;
                }
                else
                {
                    node.Type = RollType(rng);
                }
            }

            EnsureType(graph, rng, MapNodeType.Shop, last);
            EnsureType(graph, rng, MapNodeType.Witch, last);
            EnsureType(graph, rng, MapNodeType.Altar, last);
        }

        private static MapNodeType RollType(Rng rng)
        {
            int roll = rng.Next(WBattle + WUnknown + WShop + WWitch + WAltar);
            if (roll < WBattle)
            {
                return MapNodeType.Battle;
            }

            roll -= WBattle;
            if (roll < WUnknown)
            {
                return MapNodeType.Unknown;
            }

            roll -= WUnknown;
            if (roll < WShop)
            {
                return MapNodeType.Shop;
            }

            roll -= WShop;
            if (roll < WWitch)
            {
                return MapNodeType.Witch;
            }

            return MapNodeType.Altar;
        }

        private static void EnsureType(MapGraph graph, Rng rng, MapNodeType want, int last)
        {
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (graph.Nodes[i].Type == want)
                {
                    return;
                }
            }

            // 先找普通战斗（最好改的），没有再退而求其次找未知。
            var first = new List<int>();
            var second = new List<int>();
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                MapNode n = graph.Nodes[i];
                if (n.Layer <= 1 || n.Layer >= last)
                {
                    continue;
                }

                if (n.Type == MapNodeType.Battle)
                {
                    first.Add(i);
                }
                else if (n.Type == MapNodeType.Unknown)
                {
                    second.Add(i);
                }
            }

            List<int> pick = first.Count > 0 ? first : second;
            if (pick.Count == 0)
            {
                return;
            }

            graph.Nodes[pick[rng.Next(pick.Count)]].Type = want;
        }

        // ══════════════════════════════════════════════════════
        //  一次尝试
        // ══════════════════════════════════════════════════════

        private sealed class Builder
        {
            private readonly Rng _rng;
            private readonly int _layerCount;
            private readonly int _rowCount;
            private readonly int _pathCount;
            private readonly int _last;
            private readonly int _bossRow;

            private readonly Dictionary<int, int> _nodeAt = new Dictionary<int, int>();
            private readonly List<MapNode> _nodes = new List<MapNode>();
            private readonly List<int[]>[] _edges;
            private readonly int[,] _layerWidth;

            public Builder(int seed, int layerCount, int rowCount, int pathCount)
            {
                _rng = new Rng(seed);
                _layerCount = layerCount;
                _rowCount = rowCount;
                _pathCount = pathCount;
                _last = layerCount - 1;
                _bossRow = rowCount - 1;
                _edges = new List<int[]>[layerCount];
                _layerWidth = new int[layerCount, rowCount];
                for (int L = 0; L < layerCount; L++)
                {
                    _edges[L] = new List<int[]>();
                }
            }

            /// <summary>造一张图；任何一步走不通就返回 <c>null</c>（外层换种子重来）。</summary>
            public MapGraph Build()
            {
                for (int p = 0; p < _pathCount; p++)
                {
                    if (!WalkOnePath(p))
                    {
                        return null;
                    }
                }

                if (!Validate())
                {
                    return null;
                }

                var graph = new MapGraph(_rng.Seed, _layerCount, _rowCount, _nodes);
                for (int L = 0; L < _last; L++)
                {
                    for (int i = 0; i < _edges[L].Count; i++)
                    {
                        graph.AddEdge(_edges[L][i][0], _edges[L][i][1]);
                    }
                }

                AssignTypes(graph, _rng, _layerCount, _rowCount);
                return graph;
            }

            private static int Key(int layer, int row)
            {
                return layer * 64 + row;
            }

            private int EnsureNode(int layer, int row)
            {
                int k = Key(layer, row);
                int index;
                if (_nodeAt.TryGetValue(k, out index))
                {
                    return index;
                }

                index = _nodes.Count;
                _nodes.Add(new MapNode(index, layer, row, MapNodeType.Battle));
                _nodeAt.Add(k, index);
                _layerWidth[layer, row] = 1;
                return index;
            }

            /// <summary>撒一条从起点到 Boss 的路径。走不通 → false。</summary>
            private bool WalkOnePath(int pathIndex)
            {
                int start = EnsureNode(0, 0);

                // 第一步：只允许走到第 0/1 行（起点在第 0 行，行号差 ≤ 1）。
                //
                // ⚠ **轮到第几条路径就取第几行**（round-robin），不是随机取。
                //   随机取的话 6 条路径有 3% 的概率全落在同一行 → 第 1 层宽度 1 →
                //   紧接着一列全是单行道（`MinLayerWidth` 的注释里记了这个概率账）。
                //   起点层只有 1 个节点，它是**唯一**没法靠「多撒几条路径」自然撑开的层，
                //   所以在这里直接铺开。rowCount == 1 的退化情形另算（下面那个夹取）。
                int row = _rowCount > 1 ? pathIndex % 2 : 0;
                if (row >= _rowCount)
                {
                    row = _rowCount - 1;
                }

                int node = EnsureNode(1, row);
                AddEdge(0, start, node);

                for (int layer = 1; layer < _last - 1; layer++)
                {
                    int next = PickNextRow(layer, row);
                    if (next < 0)
                    {
                        return false;
                    }

                    int nextNode = EnsureNode(layer + 1, next);
                    AddEdge(layer, node, nextNode);
                    node = nextNode;
                    row = next;
                }

                // 最后一步：直接进 Boss（唯一节点，固定行）。
                int boss = EnsureNode(_last, _bossRow);
                AddEdge(_last - 1, node, boss);
                return true;
            }

            /// <summary>
            /// 选下一步的行号。候选 = {上一行, 本行, 下一行} ∩ [0, rowCount−1] ∩
            /// 「不早于 <see cref="NeedRow"/>」∩「不与已有边交叉」。
            ///
            /// <para>带权顺序：上 5 / 平 4 / 下 2（洗牌后逐个试）——
            /// 往上偏一点，地图才会整体「从左下角爬到右上角」；完全无偏的随机游走
            /// 会让一半路径贴着最下面一行走到头。</para>
            /// </summary>
            private int PickNextRow(int layer, int row)
            {
                int need = NeedRow(layer + 1, _last, _bossRow);

                var pool = new List<int>();     // 候选「行号增量」
                for (int i = 0; i < 5; i++)
                {
                    pool.Add(1);
                }

                for (int i = 0; i < 4; i++)
                {
                    pool.Add(0);
                }

                for (int i = 0; i < 2; i++)
                {
                    pool.Add(-1);
                }

                _rng.Shuffle(pool);

                for (int i = 0; i < pool.Count; i++)
                {
                    int candidate = row + pool[i];
                    if (candidate < 0 || candidate >= _rowCount || candidate < need)
                    {
                        continue;
                    }

                    if (Crosses(layer, row, candidate))
                    {
                        continue;
                    }

                    return candidate;
                }

                // 三条增量被挡光时再兜一次底：把「恰好等于 need」那一格直接放行
                // （它一定与上一层相邻，且 need 的爬升每层最多 +1）。
                if (need >= 0 && need < _rowCount
                    && (need == row || need == row + 1 || need == row - 1)
                    && !Crosses(layer, row, need))
                {
                    return need;
                }

                return -1;
            }

            /// <summary>
            /// 新边 (layer,row) → (layer+1,candidate) 会不会与同两层之间已有的边交叉。
            /// 判据 <c>(a − c) · (b − d) &lt; 0</c>；共享端点的分岔 / 汇合不算交叉。
            /// </summary>
            private bool Crosses(int layer, int row, int candidate)
            {
                List<int[]> list = _edges[layer];
                for (int i = 0; i < list.Count; i++)
                {
                    int a = _nodes[list[i][0]].Row;
                    int b = _nodes[list[i][1]].Row;
                    if (a == row || b == candidate)
                    {
                        continue;
                    }

                    if ((row - a) * (candidate - b) < 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            private void AddEdge(int layer, int from, int to)
            {
                List<int[]> list = _edges[layer];
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i][0] == from && list[i][1] == to)
                    {
                        return;
                    }
                }

                list.Add(new[] { from, to });
            }

            /// <summary>收尾自检：四条不变量逐条过一遍，任一条不过就整张作废。</summary>
            private bool Validate()
            {
                if (_nodes.Count == 0)
                {
                    return false;
                }

                // ① 起点 / 终点唯一
                int starts = 0;
                int bosses = 0;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].Layer == 0)
                    {
                        starts++;
                    }

                    if (_nodes[i].Layer == _last)
                    {
                        bosses++;
                    }
                }

                if (starts != 1 || bosses != 1)
                {
                    return false;
                }

                // ② 行号差 ≤ 1；③ 无交叉
                for (int L = 0; L < _last; L++)
                {
                    List<int[]> list = _edges[L];
                    for (int i = 0; i < list.Count; i++)
                    {
                        int a = _nodes[list[i][0]].Row;
                        int b = _nodes[list[i][1]].Row;
                        if (a - b > 1 || b - a > 1)
                        {
                            return false;
                        }
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            int a = _nodes[list[i][0]].Row;
                            int b = _nodes[list[i][1]].Row;
                            int c = _nodes[list[j][0]].Row;
                            int d = _nodes[list[j][1]].Row;
                            if (a == c || b == d)
                            {
                                continue;
                            }

                            if ((a - c) * (b - d) < 0)
                            {
                                return false;
                            }
                        }
                    }
                }

                // ④ 每层都要有节点、中间层宽度 ≥ 2；每个中间节点都要有进有出
                int minWidth = _rowCount < MinLayerWidth ? _rowCount : MinLayerWidth;
                for (int L = 0; L < _layerCount; L++)
                {
                    int width = 0;
                    for (int r = 0; r < _rowCount; r++)
                    {
                        width += _layerWidth[L, r];
                    }

                    if (width == 0)
                    {
                        return false;
                    }

                    if (L > 0 && L < _last && width < minWidth)
                    {
                        return false;
                    }
                }

                var indeg = new int[_nodes.Count];
                var outdeg = new int[_nodes.Count];
                for (int L = 0; L < _last; L++)
                {
                    for (int i = 0; i < _edges[L].Count; i++)
                    {
                        outdeg[_edges[L][i][0]]++;
                        indeg[_edges[L][i][1]]++;
                    }
                }

                for (int i = 0; i < _nodes.Count; i++)
                {
                    bool isStart = _nodes[i].Layer == 0;
                    bool isBoss = _nodes[i].Layer == _last;
                    if (!isStart && indeg[i] == 0)
                    {
                        return false;
                    }

                    if (!isBoss && outdeg[i] == 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
