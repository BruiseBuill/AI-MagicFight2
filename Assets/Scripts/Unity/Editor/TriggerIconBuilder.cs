using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicBrawl.App;
using MagicBrawl.Core;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>
    /// 触发图标（α 剑 / β 盾 / γ 感叹号）的落地器。
    ///
    /// <para>做三件事：</para>
    /// <list type="number">
    /// <item>把三合一拼图从成品区移入 `Icons/_Source/`（**用 AssetDatabase.MoveAsset，保证 GUID 不变**）；</item>
    /// <item>把三张切好的 Sprite 的导入设置统一成 UI 口径；</item>
    /// <item>生成 / 刷新 <see cref="TriggerIconLibrary"/> 资产，供 UI 按时机取图。</item>
    /// </list>
    ///
    /// <para>切图本身不在这里做（由 `Tools/art-audit/slice_icons.py` 完成），
    /// 因为那是纯图像处理，不需要 Unity 参与，也方便单独重跑。</para>
    ///
    /// <para>菜单：<c>魔法乱斗/整理 · 建触发图标库</c>（幂等，可反复跑）</para>
    /// </summary>
    public static class TriggerIconBuilder
    {
        private const string IconDir = TriggerIconLibrary.IconDir;          // Assets/Art/Icons
        private const string SourceDir = IconDir + "/_Source";
        private const string ResourcesDir = "Assets/Resources";
        private const string LibraryPath = ResourcesDir + "/" + TriggerIconLibrary.ResourcePath + ".asset";

        /// <summary>未切分前的拼图（要移进 _Source）。</summary>
        private const string SheetOldPath = IconDir + "/IconSheet_盾剑强制_三合一.png";
        private const string SheetNewPath = SourceDir + "/IconSheet_盾剑特殊_三合一.png";

        /// <summary>三个图标：文件名 → 效果时机。顺序 = <see cref="EffectTrigger"/> 枚举序。</summary>
        private static readonly KeyValuePair<string, EffectTrigger>[] Icons =
        {
            new KeyValuePair<string, EffectTrigger>("Icon_01_attack_剑.png", EffectTrigger.Attack),
            new KeyValuePair<string, EffectTrigger>("Icon_02_defend_盾.png", EffectTrigger.Defend),
            new KeyValuePair<string, EffectTrigger>("Icon_03_special_感叹号.png", EffectTrigger.Special),
        };

        // ══════════════════════════════════════════════════════
        //  菜单入口
        // ══════════════════════════════════════════════════════

        [MenuItem("魔法乱斗/整理 · 建触发图标库", priority = 22)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog(
                    "触发图标库",
                    "将执行：\n" +
                    "① 拼图移入 Icons/_Source/（改名为「盾剑特殊」）\n" +
                    "② 统一三张图标的 Sprite 导入设置\n" +
                    "③ 重建 " + LibraryPath + "\n\n确认执行？",
                    "执行", "取消"))
            {
                return;
            }

            EditorUtility.DisplayDialog("触发图标库", RunBuild(), "好");
        }

        /// <summary>无弹窗执行（供 MCP / 脚本化调用）。返回结果摘要。</summary>
        public static string RunBuild()
        {
            var log = new StringBuilder();

            EnsureFolder(ResourcesDir);

            // ── ① 拼图移入 _Source ─────────────────────────────
            if (File.Exists(SheetOldPath))
            {
                EnsureFolder(SourceDir);
                string err = AssetDatabase.MoveAsset(SheetOldPath, SheetNewPath);
                log.AppendLine(string.IsNullOrEmpty(err)
                    ? "① 拼图已留档 → " + SheetNewPath
                    : "① 拼图移动失败：" + err);
            }
            else if (File.Exists(SheetNewPath))
            {
                log.AppendLine("① 拼图已在 " + SheetNewPath);
            }
            else
            {
                log.AppendLine("① 未找到拼图（可能已归档或改名），跳过");
            }

            // ── ② 导入设置 + ③ 建库 ────────────────────────────
            var entries = new List<TriggerIconLibrary.Entry>();
            var missing = new List<string>();

            foreach (KeyValuePair<string, EffectTrigger> kv in Icons)
            {
                string path = IconDir + "/" + kv.Key;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    missing.Add(path);
                    continue;
                }

                ApplySpriteImportSettings(path);
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);   // 重设后再取一次

                entries.Add(new TriggerIconLibrary.Entry
                {
                    Trigger = kv.Value,
                    Icon = sprite,
                });

                log.AppendLine(string.Format("② {0,-18} → {1}  ({2}×{3})",
                    kv.Value, kv.Key, (int)sprite.rect.width, (int)sprite.rect.height));
            }

            if (missing.Count > 0)
            {
                log.AppendLine("⚠ 缺少图标：\n  " + string.Join("\n  ", missing.ToArray()));
                log.AppendLine("  先跑 `python Tools/art-audit/slice_icons.py --apply` 切图。");
            }

            var lib = AssetDatabase.LoadAssetAtPath<TriggerIconLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<TriggerIconLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            lib.SetEntries(entries);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine("③ 图标库已写入 " + LibraryPath + "（" + entries.Count + " 条）");
            Debug.Log("[触发图标库] " + log.ToString().Replace("\n", " | "));
            return log.ToString();
        }

        // ── 导入设置 ────────────────────────────────────────────

        /// <summary>把一张图按「UI 图标」口径设置：Single Sprite、无 mipmap、钳制、Bilinear。</summary>
        private static void ApplySpriteImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            // UI 图标不需要 mipmap（缩小显示靠 UI 自身的过滤）
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                dirty = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                dirty = true;
            }

            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
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
    }
}
