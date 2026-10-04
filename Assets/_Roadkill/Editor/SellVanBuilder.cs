using UnityEditor;
using UnityEngine;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Saves the sell van (SellVan.Build: body, open back doors, SellZone trigger, "Sold today" signs, audio
    /// source, all references assigned) as Resources/Shop/SellVan.prefab with material assets. The
    /// code-built greybox places that prefab beside the table shop (GreyboxBuilder), so rebuilding it here
    /// is the whole setup. Runs on its own when the prefab is missing; Roadkill > Build Sell Van rebuilds it.
    /// </summary>
    [InitializeOnLoad]
    public static class SellVanBuilder
    {
        const string PrefabPath = "Assets/_Roadkill/Resources/" + SellVan.ResourcePath + ".prefab";

        static SellVanBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                    Build();
            };
        }

        [MenuItem("Roadkill/Build Sell Van")]
        public static void Build()
        {
            ShopAssetBuilder.EnsureAssets();   // makes the Resources/Shop folder
            var van = SellVan.Build(NetPrefabBuilder.MaterialFor);
            PrefabUtility.SaveAsPrefabAsset(van.gameObject, PrefabPath);
            Object.DestroyImmediate(van.gameObject);
            AssetDatabase.SaveAssets();
            Debug.Log($"Roadkill: sell van saved to {PrefabPath}");
        }
    }
}
