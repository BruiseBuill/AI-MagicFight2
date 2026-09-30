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
