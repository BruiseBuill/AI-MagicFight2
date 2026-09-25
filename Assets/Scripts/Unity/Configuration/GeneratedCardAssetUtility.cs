using System.IO;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    public static class GeneratedCardAssetUtility
    {
        private const string Folder = "Assets/Resources/Cards/Generated";

        [MenuItem("魔法乱斗/卡牌/创建动态卡牌资产")]
        public static void CreateCardAsset()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Cards");
            EnsureFolder(Folder);
            var asset = ScriptableObject.CreateInstance<CardDefinitionAsset>();
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/GeneratedCard.asset");
            AssetDatabase.CreateAsset(asset, path);
            string[] catalogs = AssetDatabase.FindAssets("t:CardCatalogAsset", new[] { "Assets/Resources" });
            CardCatalogAsset catalog = catalogs.Length == 0 ? null
                : AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(AssetDatabase.GUIDToAssetPath(catalogs[0]));
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CardCatalogAsset>();
                AssetDatabase.CreateAsset(catalog, "Assets/Resources/CardCatalog.asset");
            }
            var generated = new System.Collections.Generic.List<CardDefinitionAsset>(catalog.generatedCards ?? new CardDefinitionAsset[0]);
            generated.Add(asset);
            catalog.generatedCards = generated.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
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
