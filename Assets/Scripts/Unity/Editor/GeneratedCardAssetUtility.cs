using System.IO;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    public static class GeneratedCardAssetUtility
    {
        /// <summary>动态卡牌的落盘目录（不落在 Resources 根 —— 那儿以后要放别的表）。</summary>
        public const string Folder = "Assets/Resources/Cards/Generated";

        /// <summary>唯一那份卡目录资产（<c>Resources.Load&lt;CardCatalogAsset&gt;("CardCatalog")</c> 按名取它）。</summary>
        public const string CatalogPath = "Assets/Resources/CardCatalog.asset";

        [MenuItem("魔法乱斗/卡牌/创建动态卡牌资产")]
        public static void CreateCardAsset()
        {
            var asset = ScriptableObject.CreateInstance<CardDefinitionAsset>();
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/GeneratedCard.asset");
            AssetDatabase.CreateAsset(asset, path);
            CardCatalogAsset catalog = EnsureCatalog();
            Register(catalog, asset);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// 取（不存在就建）唯一那份卡目录资产。
        ///
        /// <para><b>2026-09-30 从 <see cref="CreateCardAsset"/> 里提出来</b>：
        /// 强化卡（<c>GeneratedUpgradeCardUtility</c>）也要往同一个目录里登记，
        /// 各写一份「找不到就建」就会在两条链路先后跑时各建一个（后建的那个没人用，
        /// 而先建的那个还指着旧的清单）。</para>
        /// </summary>
        public static CardCatalogAsset EnsureCatalog()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Cards");
            EnsureFolder(Folder);

            CardCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(CatalogPath);
            if (catalog == null)
            {
                string[] found = AssetDatabase.FindAssets("t:CardCatalogAsset", new[] { "Assets/Resources" });
                catalog = found.Length == 0
                    ? null
                    : AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(AssetDatabase.GUIDToAssetPath(found[0]));
            }

            if (catalog == null && System.IO.File.Exists(CatalogPath))
            {
                // ⚠ 文件在、但读不出 `CardCatalogAsset` → 它是一具**空壳**：
                //   2026-09-30 那次 `CardCatalogAsset` 与 `CardDefinitionAsset` 挤在同一个 .cs 里，
                //   Unity 报 `No script asset for CardCatalogAsset` 并把 m_Script 写成 {fileID: 0}。
                //   这种资产永远修不好（脚本引用没有归属），只能删掉重建 ——
                //   留着的后果是「强化卡整批从卡池消失」，而且零报错。
                Debug.LogWarning("[GeneratedCard] 卡目录资产读不出来（m_Script 无效）→ 删掉重建：" + CatalogPath);
                AssetDatabase.DeleteAsset(CatalogPath);
            }

            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CardCatalogAsset>();
                catalog.includeBuiltinCards = true;
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            return catalog;
        }

        /// <summary>把一个卡定义资产登记进目录的「动态卡」清单（幂等）。</summary>
        public static void Register(CardCatalogAsset catalog, CardDefinitionAsset asset)
        {
            if (catalog == null || asset == null)
            {
                return;
            }

            var generated = new System.Collections.Generic.List<CardDefinitionAsset>(
                catalog.generatedCards ?? new CardDefinitionAsset[0]);
            if (generated.Contains(asset))
            {
                return;
            }

            generated.Add(asset);
            catalog.generatedCards = generated.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// <b>重建卡目录</b>：以 <see cref="Folder"/> 里的实际文件为准，重写「动态卡」清单。
        ///
        /// <para><b>为什么需要它</b>：目录资产一旦损坏 / 被删（例如那次
        /// <c>m_Script: {fileID: 0}</c>），卡定义资产还好好躺在磁盘上，但没人引用它们 ——
        /// 症状是「强化卡凭空消失」，而且没有任何报错。清单可推导的东西就不要靠手工维护：
        /// 目录的 <c>generatedCards</c> 恒等于那个文件夹里的内容，这里按文件名排序重建。</para>
        ///
        /// <para>⚠ 只动 <c>generatedCards</c>，不碰 <c>cards</c>（那是手工登记的额外卡）。</para>
        /// </summary>
        public static CardCatalogAsset RebuildCatalog()
        {
            CardCatalogAsset catalog = EnsureCatalog();
            var found = new System.Collections.Generic.List<CardDefinitionAsset>();
            var paths = new System.Collections.Generic.List<string>();

            if (AssetDatabase.IsValidFolder(Folder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:CardDefinitionAsset", new[] { Folder }))
                {
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            paths.Sort(System.StringComparer.Ordinal);
            foreach (string path in paths)
            {
                CardDefinitionAsset card = AssetDatabase.LoadAssetAtPath<CardDefinitionAsset>(path);
                if (card != null)
                {
                    found.Add(card);
                }
            }

            catalog.generatedCards = found.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>菜单：把磁盘上的动态卡重新扫进目录（目录丢了 / 手删了文件时用）。</summary>
        [MenuItem("魔法乱斗/卡牌/从 Generated 目录重建卡目录", false, 42)]
        public static void RebuildCatalogMenu()
        {
            CardCatalogAsset catalog = RebuildCatalog();
            Debug.Log("[GeneratedCard] 卡目录已重建：" + (catalog.generatedCards == null
                ? 0 : catalog.generatedCards.Length) + " 张动态卡 → " + CatalogPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
