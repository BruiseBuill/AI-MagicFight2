namespace MagicBrawl.Core
{
    /// <summary>
    /// 地图上的一个节点（2026-10-02 · 冒险地图）。
    ///
    /// <para><b>只有「第几层、第几行、什么类型」三件事</b>，<b>不含任何像素坐标</b>。
    /// 这是刻意的：<c>Core</c> 不许出现 UnityEngine（红线 1），画布坐标属于 <c>UiLayout</c>。
    /// 表现层拿 <see cref="Layer"/> / <see cref="Row"/> 去查版式常量算出位置，
    /// 于是「同一张图在任何分辨率下都长一样」由版式层保证，内核只保证**拓扑**。</para>
    ///
    /// <para><b>行（<see cref="Row"/>）的语义</b>：0 = 最下，往上递增，
    /// 上限是 <see cref="MapGraph.RowCount"/> − 1。它在版式上就是纵向的第几格，
    /// 所以「相邻两层的行号差 ≤ 1」这条生成约束<b>同时也是</b>「两个节点之间的桥画得出来」
    /// 的保证 —— 桥的两端永远落在同一个 190 × 144 的格子上或它的上下邻格里。</para>
    /// </summary>
    public sealed class MapNode
    {
        /// <summary>在 <see cref="MapGraph.Nodes"/> 里的下标（= 稳定的节点 ID）。</summary>
        public readonly int Index;

        /// <summary>第几层（竖着的一列）。0 = 起点层，<c>LayerCount − 1</c> = Boss 层。</summary>
        public readonly int Layer;

        /// <summary>层内第几行（0 = 最下）。</summary>
        public readonly int Row;

        /// <summary>节点类型。生成时定好，之后不改。</summary>
        public MapNodeType Type;

        public MapNode(int index, int layer, int row, MapNodeType type)
        {
            Index = index;
            Layer = layer;
            Row = row;
            Type = type;
        }

        /// <summary>是不是起点（营地）。</summary>
        public bool IsStart
        {
            get { return Layer == 0; }
        }

        /// <summary>是不是终点（Boss）。</summary>
        public bool IsBoss
        {
            get { return Type == MapNodeType.Boss; }
        }

        /// <summary>这个节点有没有「进去之后会切场景」的去处。没有 = 点完只是走过去。</summary>
        public bool HasScene
        {
            get
            {
                return Type == MapNodeType.Battle || Type == MapNodeType.Boss
                       || Type == MapNodeType.Shop || Type == MapNodeType.Witch
                       || Type == MapNodeType.Altar;
            }
        }
    }
}
