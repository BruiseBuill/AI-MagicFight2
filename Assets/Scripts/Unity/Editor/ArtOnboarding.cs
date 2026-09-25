using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// 新导入美术资源的整理器：重命名 + 归位。
    ///
    /// <para>为什么要用编辑器脚本而不是直接改盘：Unity 正在运行，直接改名会让资源被当成
    /// 「删除 + 新建」，GUID 变化会切断已有引用。走 <see cref="AssetDatabase.MoveAsset"/>
    /// 则 .meta 与 GUID 一并迁移，引用零断裂。</para>
    ///
    /// <para>整理规则（与 <c>Docs/engineering/06-美术与字体规范.md</c> 一致）：</para>
    /// <list type="bullet">
    /// <item>元素插画原图 → <c>Assets/Art/CardArt/Card_&lt;两位序号&gt;_&lt;卡ID&gt;_&lt;卡名&gt;</c>，
    /// 序号由 <see cref="CardLibrary"/> 实时给出，脚本不会与卡表脱节。</item>
    /// <item>无对应卡的插画 → <c>Assets/Art/CardArt/_Draft/Draft_&lt;元素&gt;_&lt;中文名&gt;</c>。</item>
    /// <item>第三方素材包 → <c>Assets/ThirdParty/</c>，与自产美术分开。</item>
    /// </list>
    ///
    /// <para>菜单（先 dry-run 看清单，确认后再执行）：</para>
    /// <list type="bullet">
    /// <item><c>魔法乱斗/整理 · 新导入美术（预览）</c></item>
    /// <item><c>魔法乱斗/整理 · 新导入美术（执行）</c></item>
    /// </list>
    /// </summary>
    public static class ArtOnboarding
    {
        // ── 路径常量 ────────────────────────────────────────────

        private const string ArtRoot = "Assets/Art";
        private const string CardArtDir = "Assets/Art/CardArt";
        private const string DraftDir = "Assets/Art/CardArt/_Draft";
        private const string FxDir = "Assets/Art/Fx";
        private const string IconsDir = "Assets/Art/Icons";

        private const string ThirdPartyDir = "Assets/ThirdParty";
        private const string KitDir = "Assets/ThirdParty/MagicCardKit";
        private const string KitMatDir = "Assets/ThirdParty/MagicCardKit/Materials";
        private const string KitShaderDir = "Assets/ThirdParty/MagicCardKit/Shaders";
        private const string KitPrefabDir = "Assets/ThirdParty/MagicCardKit/Prefabs";
        private const string PolySpriteSrc = "Assets/Art/PolySprite";
        private const string PolySpriteDst = "Assets/ThirdParty/PolySprite";

        private const string ReportPath = "Artifacts/audits/art/onboarding-report.md";

        /// <summary>元素插画所在的子目录名（英文，同时用作草稿素材的前缀）。</summary>
        private static readonly string[] ElementFolders =
        {
            "Ice", "Water", "Electric", "Fire", "Grass", "Stone", "Sound",
        };

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg" };

        /// <summary>Assets 根目录下属于第三方「魔法卡 UI」套件的材质（键=现路径，值=套件内新名）。</summary>
        private static readonly KeyValuePair<string, string>[] KitMaterials =
        {
            Kv("Assets/BGCircle.mat", "BGCircle.mat"),
            Kv("Assets/Box.mat", "Box.mat"),
            Kv("Assets/CardEdge.mat", "CardEdge.mat"),
            Kv("Assets/CardMask.mat", "CardMask.mat"),
            Kv("Assets/EdgeEnhance.mat", "EdgeEnhance.mat"),
            Kv("Assets/EdgeEnhance 1.mat", "EdgeEnhance_alt.mat"),   // 与上一行参数不同，是另一套发光参数
            Kv("Assets/EdgeFade 1.mat", "EdgeFade.mat"),
            Kv("Assets/NameBox.mat", "NameBox.mat"),
            Kv("Assets/NameBoxLine.mat", "NameBoxLine.mat"),
            Kv("Assets/TextBox.mat", "TextBox.mat"),
            Kv("Assets/TextBoxLine.mat", "TextBoxLine.mat"),
        };

        private static readonly string[] KitShaders =
        {
            "EdgeEnhance_Circle",
            "EdgeFade_Circle",
            "RandomCircle",
            "RoundBox",
            "RoundBoxLine",
        };

        /// <summary>Art 根目录散落图片的重命名映射。</summary>
        private static readonly KeyValuePair<string, string>[] StrayArt =
        {
            Kv("Explose_Light.png", "Assets/Art/Fx/Fx_Explosion_Light.png"),
            Kv("ChatGPT Image 2025年4月24日 12_15_15.png",
               "Assets/Art/Icons/IconSheet_盾剑强制_三合一.png"),
        };

        // ── 菜单入口 ────────────────────────────────────────────

        [MenuItem("魔法乱斗/整理 · 新导入美术（预览，不改盘）", priority = 20)]
        public static void Preview()
        {
            List<Move> plan = BuildPlan(out List<string> notes);
            Report(plan, notes, false);
        }

        [MenuItem("魔法乱斗/整理 · 新导入美术（执行）", priority = 21)]
        public static void Apply()
        {
            List<Move> plan = BuildPlan(out List<string> notes);

            if (plan.Count == 0)
            {
                EditorUtility.DisplayDialog("整理美术资源", "没有需要移动的资源。", "好");
                return;
            }

            // ⚠ 弹窗只放在菜单入口这一层：脚本化调用（MCP / batchmode）走 RunApply()，
            //    否则 DisplayDialog 会阻塞主线程，把调用方一起挂住。
            if (!EditorUtility.DisplayDialog(
                    "整理美术资源",
                    "将执行 " + plan.Count + " 项移动/重命名。\n" +
                    "GUID 会随之保留，已有引用不会断裂。\n\n确认执行？",
                    "执行", "取消"))
            {
                return;
            }

            string summary = RunApply();
            EditorUtility.DisplayDialog("整理美术资源", summary + "\n\n详见 " + ReportPath, "好");
        }

        /// <summary>
        /// 无弹窗执行（供 MCP / 脚本化调用）。返回人类可读的结果摘要。
        /// </summary>
        public static string RunApply()
        {
            List<Move> plan = BuildPlan(out List<string> notes);
            Report(plan, notes, false);

            if (plan.Count == 0)
            {
                return "没有需要移动的资源。";
            }

            int ok = 0;
            var failed = new List<string>();
            foreach (Move m in plan)
            {
                EnsureFolder(Path.GetDirectoryName(m.To).Replace('\\', '/'));
                string err = AssetDatabase.MoveAsset(m.From, m.To);
                if (string.IsNullOrEmpty(err))
                {
                    ok++;
                }
                else
                {
                    failed.Add(m.From + " → " + m.To + " ：" + err);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            PruneEmptyFolders();

            Debug.Log("[整理美术] 完成：成功 " + ok + " / 失败 " + failed.Count
                      + "（共 " + plan.Count + " 项）");
            Report(plan, notes, true);

            string msg = "成功 " + ok + " 项，失败 " + failed.Count + " 项。";
            if (failed.Count > 0)
            {
                msg += "\n\n失败明细：\n" + string.Join("\n", failed.ToArray());
            }

            return msg;
        }

        // ── 计划构建 ────────────────────────────────────────────

        private sealed class Move
        {
            public string From;
            public string To;
            public string Note;
        }

        private static List<Move> BuildPlan(out List<string> notes)
        {
            var plan = new List<Move>();
            notes = new List<string>();

            // ── ① 元素插画原图 ──────────────────────────────
            var matched = new HashSet<string>();
            foreach (string el in ElementFolders)
            {
                string dir = ArtRoot + "/" + el;
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(dir);
                foreach (string abs in files)
                {
                    string ext = Path.GetExtension(abs).ToLowerInvariant();
                    if (Array.IndexOf(ImageExts, ext) < 0)
                    {
                        continue;
                    }

                    string stem = Path.GetFileNameWithoutExtension(abs);
                    string from = dir + "/" + Path.GetFileName(abs);

                    CardDef def = FindCardByName(stem);
                    if (def != null)
                    {
                        matched.Add(def.Id);
                        string seq = (def.Index + 1).ToString("D2");
                        plan.Add(new Move
                        {
                            From = from,
                            To = CardArtDir + "/Card_" + seq + "_" + def.Id + "_" + def.Name + ext,
                            Note = el + " 插画 → 卡 " + def.Id,
                        });
                    }
                    else
                    {
                        plan.Add(new Move
                        {
                            From = from,
                            To = DraftDir + "/Draft_" + el + "_" + stem + ext,
                            Note = "无对应卡（草稿）",
                        });
                    }
                }
            }

            // 卡表里没有插画原图的卡 —— 这是一条真正有价值的告警
            foreach (CardDef d in CardLibrary.All)
            {
                if (!matched.Contains(d.Id))
                {
                    notes.Add("⚠ 卡 " + d.Id + "·" + d.Name + " 没有对应的插画原图");
                }
            }

            // ── ② Art 根目录散落图片 ────────────────────────
            foreach (KeyValuePair<string, string> kv in StrayArt)
            {
                string from = ArtRoot + "/" + kv.Key;
                if (File.Exists(from))
                {
                    plan.Add(new Move { From = from, To = kv.Value, Note = "根目录散落图 → 归位" });
                }
            }

            // ── ③ 第三方「魔法卡 UI」套件 ───────────────────
            foreach (KeyValuePair<string, string> kv in KitMaterials)
            {
                if (File.Exists(kv.Key))
                {
                    plan.Add(new Move
                    {
                        From = kv.Key,
                        To = KitMatDir + "/" + kv.Value,
                        Note = "第三方套件材质",
                    });
                }
            }

            foreach (string shader in KitShaders)
            {
                string from = "Assets/" + shader + ".shadergraph";
                if (File.Exists(from))
                {
                    plan.Add(new Move
                    {
                        From = from,
                        To = KitShaderDir + "/" + shader + ".shadergraph",
                        Note = "第三方套件着色器",
                    });
                }
            }

            const string bigCard = "Assets/BigMagicCard 1.prefab";
            if (File.Exists(bigCard))
            {
                plan.Add(new Move
                {
                    From = bigCard,
                    To = KitPrefabDir + "/BigMagicCard.prefab",
                    Note = "第三方套件参考 Prefab",
                });
            }

            // ── ④ PolySprite 整体迁出 Art ───────────────────
            if (AssetDatabase.IsValidFolder(PolySpriteSrc))
            {
                plan.Add(new Move
                {
                    From = PolySpriteSrc,
                    To = PolySpriteDst,
                    Note = "第三方形状包（整目录）",
                });
            }

            return plan;
        }

        private static CardDef FindCardByName(string name)
        {
            for (int i = 0; i < CardLibrary.All.Count; i++)
            {
                if (string.Equals(CardLibrary.All[i].Name, name, StringComparison.Ordinal))
                {
                    return CardLibrary.All[i];
                }
            }

            return null;
        }

        // ── 报告 ────────────────────────────────────────────────

        private static void Report(List<Move> plan, List<string> notes, bool applied)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 新导入美术资源整理 —— 映射表");
            sb.AppendLine();
            sb.AppendLine("> 由 `Assets/Scripts/Unity/Editor/ArtOnboarding.cs` 生成"
                          + (applied ? "（**已执行**）" : "（**预览**，未改盘）"));
            sb.AppendLine();
            sb.AppendLine("合计 " + plan.Count + " 项，其中重命名/移动明细如下。");
            sb.AppendLine();
            sb.AppendLine("| # | 原路径 | 新路径 | 备注 |");
            sb.AppendLine("|---|---|---|---|");

            int i = 0;
            foreach (Move m in plan)
            {
                i++;
                sb.AppendLine("| " + i + " | `" + m.From + "` | `" + m.To + "` | " + m.Note + " |");
            }

            sb.AppendLine();
            if (notes.Count > 0)
            {
                sb.AppendLine("## 告警");
                sb.AppendLine();
                foreach (string n in notes)
                {
                    sb.AppendLine("- " + n);
                }

                sb.AppendLine();
            }

            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, sb.ToString(), new UTF8Encoding(false));

            Debug.Log("[整理美术] 映射表已写入 " + ReportPath + "（" + plan.Count + " 项）\n"
                      + string.Join("\n", plan.ConvertAll(m => m.From + "  →  " + m.To).ToArray()));
        }

        // ── 工具 ────────────────────────────────────────────────

        /// <summary>清掉整理后剩下的空目录（例如 Art/Ice）。</summary>
        private static void PruneEmptyFolders()
        {
            foreach (string el in ElementFolders)
            {
                string dir = ArtRoot + "/" + el;
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(dir);
                string[] subs = Directory.GetDirectories(dir);

                // 只留下 .meta 也算空（.meta 会随资源一起被删）
                bool hasReal = false;
                foreach (string f in files)
                {
                    if (!f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        hasReal = true;
                        break;
                    }
                }

                if (!hasReal && subs.Length == 0)
                {
                    AssetDatabase.DeleteAsset(dir);
                    Debug.Log("[整理美术] 删除空目录 " + dir);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static KeyValuePair<string, string> Kv(string k, string v)
        {
            return new KeyValuePair<string, string>(k, v);
        }
    }
}
