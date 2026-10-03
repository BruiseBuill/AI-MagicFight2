using System;
using System.Collections.Generic;
using System.Text;
using MagicBrawl.Core;

namespace MagicBrawl.SelfTest
{
    /// <summary>
    /// 2026-10-02 建 · <b>冒险地图生成器</b>的定点断言。
    ///
    /// <para><b>为什么这条线要单独脚本化</b>：地图生成完全不进战斗，
    /// 万局随机对局一个缺陷也抓不到。而它的失效方式<b>全是无声的</b>：</para>
    /// <list type="bullet">
    /// <item><description>某一步的行号差变成 2 → 版式上那两个节点<b>画不出桥</b>，
    /// 地图上出现一条断掉的路线（不报错、不崩，只是看着像少画了一根线）；</description></item>
    /// <item><description>出现交叉边 → 两条路线在视觉上「穿过对方」，玩家分不清谁连谁；</description></item>
    /// <item><description>某个中间节点只有进边没有出边 → <b>走进去就卡死</b>，而地图上看不出来；</description></item>
    /// <item><description>Boss 那一层冒出第二个节点 / 起点层冒出第二个 → 用户明确要求「只有起点和终点是固定的」；</description></item>
    /// <item><description>类型分配漏了保底 → 整局看不到商店（用户专门做了那张图）。</description></item>
    /// </list>
    ///
    /// <para>断言分五层：① <b>1000 个种子</b>逐张过四条不变量；② 同种子同图 / 换种子换图；
    /// ③ 类型的固定项与保底项；④ 分辨率边界（最小的 3 层 2 行 / 最大的 14 层配置都要能出图）；
    /// ⑤ 一张 <b>可读的 ASCII 形状</b>打进报告，便于人工扫一眼对不对。</para>
    /// </summary>
    public static class MapScenario
    {
        /// <summary>跑多少个种子。用户/规格要的是 1000（`冒险模式实施规格.md` §14.4 第 18 条）。</summary>
        private const int SeedCount = 1000;

        public static bool Run(List<string> report)
        {
            var bad = new List<string>();
            var notes = new List<string>();

            CheckInvariants(bad, notes);
            CheckDeterminism(bad);
            CheckTypes(bad, notes);
            CheckSizes(bad, notes);
            Shape(notes);

            report.Add((bad.Count == 0 ? "  [PASS] " : "  [FAIL] ")
                       + "冒险地图生成（四条不变量 · " + SeedCount + " 个种子 · 固定起终点 · 类型保底）");
            for (int i = 0; i < bad.Count; i++)
            {
                report.Add("      · " + bad[i]);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                report.Add("      ~ " + notes[i]);
            }

            return bad.Count == 0;
        }

        // ── ① 四条不变量 ────────────────────────────────────────────

        private static void CheckInvariants(List<string> bad, List<string> notes)
        {
            int minNodes = int.MaxValue;
            int maxNodes = 0;
            int minEdges = int.MaxValue;
            int maxEdges = 0;
            int minWidth = int.MaxValue;
            int maxWidth = 0;
            var widthSeen = new HashSet<int>();

            for (int i = 0; i < SeedCount; i++)
            {
                int seed = 20261002 + i * 37;
                MapGraph g = MapGenerator.Generate(seed);
                string tag = "seed " + seed;

                // ① 起点 / 终点唯一
                int starts = 0;
                int bosses = 0;
                for (int k = 0; k < g.Nodes.Count; k++)
                {
                    if (g.Nodes[k].IsStart)
                    {
                        starts++;
                        if (g.Nodes[k].Type != MapNodeType.Camp)
                        {
                            bad.Add(tag + "：起点层节点不是 Camp，而是 " + g.Nodes[k].Type);
                        }
                    }

                    if (g.Nodes[k].IsBoss)
                    {
                        bosses++;
                    }
                }

                if (starts != 1)
                {
                    bad.Add(tag + "：起点节点 " + starts + " 个（应为 1）");
                }

                if (bosses != 1)
                {
                    bad.Add(tag + "：Boss 节点 " + bosses + " 个（应为 1）");
                }

                // ② 行号差 ≤ 1（= 「桥一定画得出来」）+ ③ 无交叉边
                for (int L = 0; L < g.LayerCount - 1; L++)
                {
                    List<MapNode> layer = g.NodesInLayer(L);
                    var edges = new List<int[]>();
                    for (int a = 0; a < layer.Count; a++)
                    {
                        IReadOnlyList<int> outs = g.Next(layer[a].Index);
                        for (int b = 0; b < outs.Count; b++)
                        {
                            MapNode to = g.Nodes[outs[b]];
                            if (to.Layer != L + 1)
                            {
                                bad.Add(tag + "：存在跨层边 L" + L + " → L" + to.Layer);
                            }

                            int delta = Math.Abs(to.Row - layer[a].Row);
                            if (delta > 1)
                            {
                                bad.Add(tag + "：边 (" + L + "," + layer[a].Row + ") → ("
                                        + to.Layer + "," + to.Row + ") 行号差 " + delta + " > 1（桥画不出来）");
                            }

                            edges.Add(new[] { layer[a].Row, to.Row });
                        }
                    }

                    for (int x = 0; x < edges.Count; x++)
                    {
                        for (int y = x + 1; y < edges.Count; y++)
                        {
                            if (edges[x][0] == edges[y][0] || edges[x][1] == edges[y][1])
                            {
                                continue;
                            }

                            if ((edges[x][0] - edges[y][0]) * (edges[x][1] - edges[y][1]) < 0)
                            {
                                bad.Add(tag + "：第 " + L + " 层与第 " + (L + 1) + " 层之间存在交叉边");
                            }
                        }
                    }
                }

                // ④ 双向可达 + 每层有节点 + 中间节点有进有出
                bool[] fromStart = g.ReachableFromStart();
                bool[] toBoss = g.CanReachBoss();
                for (int k = 0; k < g.Nodes.Count; k++)
                {
                    if (!fromStart[k])
                    {
                        bad.Add(tag + "：节点 " + k + " 从起点不可达");
                    }

                    if (!toBoss[k])
                    {
                        bad.Add(tag + "：节点 " + k + " 走不到 Boss");
                    }
                }

                for (int L = 0; L < g.LayerCount; L++)
                {
                    List<MapNode> layer = g.NodesInLayer(L);
                    if (layer.Count == 0)
                    {
                        bad.Add(tag + "：第 " + L + " 层一个节点都没有");
                        continue;
                    }

                    if (L == 0 || L == g.LayerCount - 1)
                    {
                        if (layer.Count != 1)
                        {
                            bad.Add(tag + "：第 " + L + " 层有 " + layer.Count + " 个节点（起终点层必须唯一）");
                        }

                        continue;
                    }

                    if (layer.Count < MapGenerator.MinLayerWidth || layer.Count > g.RowCount)
                    {
                        bad.Add(tag + "：第 " + L + " 层宽度 " + layer.Count + "（应在 "
                                + MapGenerator.MinLayerWidth + ".." + g.RowCount + "）");
                    }

                    // 行号在层内必须互不相同
                    for (int x = 0; x < layer.Count; x++)
                    {
                        for (int y = x + 1; y < layer.Count; y++)
                        {
                            if (layer[x].Row == layer[y].Row)
                            {
                                bad.Add(tag + "：第 " + L + " 层第 " + layer[x].Row + " 行有两个节点");
                            }
                        }
                    }

                    minWidth = Math.Min(minWidth, layer.Count);
                    maxWidth = Math.Max(maxWidth, layer.Count);
                    widthSeen.Add(layer.Count);

                    for (int k = 0; k < layer.Count; k++)
                    {
                        if (g.Prev(layer[k].Index).Count == 0)
                        {
                            bad.Add(tag + "：中间节点 (" + L + "," + layer[k].Row + ") 没有进边");
                        }

                        if (g.Next(layer[k].Index).Count == 0)
                        {
                            bad.Add(tag + "：中间节点 (" + L + "," + layer[k].Row + ") 没有出边（走进去会卡死）");
                        }
                    }
                }

                minNodes = Math.Min(minNodes, g.Nodes.Count);
                maxNodes = Math.Max(maxNodes, g.Nodes.Count);
                minEdges = Math.Min(minEdges, g.EdgeCount);
                maxEdges = Math.Max(maxEdges, g.EdgeCount);

                if (bad.Count > 40)
                {
                    bad.Add("（已经很多条了，先停下 —— 后面还有 " + (SeedCount - i - 1) + " 个种子没验）");
                    return;
                }
            }

            if (bad.Count == 0)
            {
                notes.Add(SeedCount + " 个种子全部通过 · 节点 " + minNodes + "–" + maxNodes
                          + " 个 · 边 " + minEdges + "–" + maxEdges
                          + " 条 · 中间层宽度 " + minWidth + "–" + maxWidth
                          + "（出现过 " + Sorted(widthSeen) + "）");
            }
        }

        private static string Sorted(HashSet<int> set)
        {
            var list = new List<int>(set);
            list.Sort();
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('/');
                }

                sb.Append(list[i]);
            }

            return sb.ToString();
        }

        // ── ② 同种子同图 ────────────────────────────────────────────

        private static void CheckDeterminism(List<string> bad)
        {
            for (int i = 0; i < 60; i++)
            {
                int seed = 7717 + i * 131;
                string a = Signature(MapGenerator.Generate(seed));
                string b = Signature(MapGenerator.Generate(seed));
                if (a != b)
                {
                    bad.Add("同种子 " + seed + " 两次生成的图不一样（随机没走 Core 的 Rng？）");
                    return;
                }
            }

            // 换种子必须换图（否则「随机生成」是假的）—— 允许少量碰撞，但不能普遍相同。
            string first = Signature(MapGenerator.Generate(9001));
            int same = 0;
            for (int i = 1; i <= 40; i++)
            {
                if (Signature(MapGenerator.Generate(9001 + i * 17)) == first)
                {
                    same++;
                }
            }

            if (same > 4)
            {
                bad.Add("40 个不同种子里有 " + same + " 张图与首张完全相同 —— 生成器没有真正随机");
            }
        }

        private static string Signature(MapGraph g)
        {
            var sb = new StringBuilder();
            sb.Append(g.LayerCount).Append('|').Append(g.RowCount).Append('|');
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                MapNode n = g.Nodes[i];
                sb.Append(n.Layer).Append(',').Append(n.Row).Append(',').Append((int)n.Type).Append(';');
                IReadOnlyList<int> outs = g.Next(i);
                for (int k = 0; k < outs.Count; k++)
                {
                    sb.Append(outs[k]).Append('/');
                }

                sb.Append('|');
            }

            return sb.ToString();
        }

        // ── ③ 类型 ──────────────────────────────────────────────────

        private static void CheckTypes(List<string> bad, List<string> notes)
        {
            int shopMissing = 0;
            int witchMissing = 0;
            int altarMissing = 0;
            int firstLayerNotBattle = 0;
            var counts = new Dictionary<MapNodeType, int>();

            for (int i = 0; i < SeedCount; i++)
            {
                int seed = 31415 + i * 7;
                MapGraph g = MapGenerator.Generate(seed);

                bool hasShop = false;
                bool hasWitch = false;
                bool hasAltar = false;

                for (int k = 0; k < g.Nodes.Count; k++)
                {
                    MapNode n = g.Nodes[k];
                    int cur;
                    counts.TryGetValue(n.Type, out cur);
                    counts[n.Type] = cur + 1;

                    if (n.Type == MapNodeType.Shop)
                    {
                        hasShop = true;
                    }

                    if (n.Type == MapNodeType.Witch)
                    {
                        hasWitch = true;
                    }

                    if (n.Type == MapNodeType.Altar)
                    {
                        hasAltar = true;
                    }

                    if (n.Layer == 1 && n.Type != MapNodeType.Battle)
                    {
                        firstLayerNotBattle++;
                    }

                    if (n.Layer == 0 && n.Type != MapNodeType.Camp)
                    {
                        bad.Add("seed " + seed + "：起点不是 Camp");
                    }

                    if (n.Layer == g.LayerCount - 1 && n.Type != MapNodeType.Boss)
                    {
                        bad.Add("seed " + seed + "：终点不是 Boss");
                    }
                }

                if (!hasShop)
                {
                    shopMissing++;
                }

                if (!hasWitch)
                {
                    witchMissing++;
                }

                if (!hasAltar)
                {
                    altarMissing++;
                }
            }

            if (firstLayerNotBattle > 0)
            {
                bad.Add("第 1 层出现了 " + firstLayerNotBattle + " 个非普通战斗节点（该层应固定为战斗）");
            }

            if (shopMissing > 0)
            {
                bad.Add(shopMissing + " 张图里一个商店都没有（保底失效）");
            }

            if (witchMissing > 0)
            {
                bad.Add(witchMissing + " 张图里一个女巫工坊都没有（保底失效）");
            }

            if (altarMissing > 0)
            {
                bad.Add(altarMissing + " 张图里一个石台都没有（保底失效）");
            }

            if (bad.Count == 0)
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<MapNodeType, int> kv in counts)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(" · ");
                    }

                    sb.Append(kv.Key).Append(' ').Append(kv.Value);
                }

                notes.Add("类型分布（" + SeedCount + " 张图合计）：" + sb
                          + " · 三项保底全中 · 第 1 层固定战斗");
            }
        }

        // ── ④ 尺寸边界 ──────────────────────────────────────────────

        private static void CheckSizes(List<string> bad, List<string> notes)
        {
            var sizes = new List<int[]> { new[] { 3, 2 }, new[] { 4, 2 }, new[] { 8, 3 }, new[] { 14, 4 }, new[] { 9, 5 } };
            for (int s = 0; s < sizes.Count; s++)
            {
                int layers = sizes[s][0];
                int rows = sizes[s][1];
                for (int i = 0; i < 40; i++)
                {
                    int seed = 555 + i * 13;
                    MapGraph g = MapGenerator.Generate(seed, layers, rows, MapGenerator.DefaultPathCount);
                    if (g.LayerCount != layers || g.RowCount != rows)
                    {
                        bad.Add("尺寸 " + layers + "×" + rows + " 生成出来的图是 "
                                + g.LayerCount + "×" + g.RowCount);
                        continue;
                    }

                    if (g.StartIndex < 0 || g.BossIndex < 0)
                    {
                        bad.Add("尺寸 " + layers + "×" + rows + "（seed " + seed + "）缺起点或 Boss");
                    }

                    bool[] fromStart = g.ReachableFromStart();
                    for (int k = 0; k < fromStart.Length; k++)
                    {
                        if (!fromStart[k])
                        {
                            bad.Add("尺寸 " + layers + "×" + rows + "（seed " + seed + "）有不可达节点");
                            break;
                        }
                    }
                }
            }

            if (bad.Count == 0)
            {
                notes.Add("尺寸边界 3×2 / 4×2 / 8×3 / 9×5 / 14×4 各 40 个种子：全部出图且连通");
            }
        }

        // ── ⑤ 形状（人工扫一眼） ────────────────────────────────────

        private static void Shape(List<string> notes)
        {
            var seen = new List<int>();
            for (int seed = 20261002; seed < 20261102 && seen.Count < 2; seed++)
            {
                MapGraph g = MapGenerator.Generate(seed);
                if (seen.Contains(g.Nodes.Count))
                {
                    continue;
                }

                seen.Add(g.Nodes.Count);
                notes.Add("seed " + seed + " 的形状（上=高行，● 节点，竖线=同列）：");
                for (int L = 0; L < g.LayerCount; L++)
                {
                    var line = new StringBuilder();
                    line.Append("        L").Append(L).Append(' ');
                    for (int r = g.RowCount - 1; r >= 0; r--)
                    {
                        int idx = g.IndexOf(L, r);
                        line.Append(idx >= 0 ? Glyph(g.Nodes[idx].Type) : '·');
                        line.Append(' ');
                    }

                    line.Append("   ");
                    List<MapNode> layer = g.NodesInLayer(L);
                    for (int k = 0; k < layer.Count; k++)
                    {
                        line.Append(layer[k].Type).Append(' ');
                    }

                    notes.Add(line.ToString());
                }
            }
        }

        private static char Glyph(MapNodeType t)
        {
            switch (t)
            {
                case MapNodeType.Camp: return 'C';
                case MapNodeType.Battle: return 'B';
                case MapNodeType.Shop: return 'S';
                case MapNodeType.Witch: return 'W';
                case MapNodeType.Altar: return 'A';
                case MapNodeType.Unknown: return '?';
                default: return 'X';
            }
        }
    }
}
