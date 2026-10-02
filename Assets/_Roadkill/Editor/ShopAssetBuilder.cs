using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Creates the default shop: the held-bat prefab, one ShopItemData per item and the ShopCatalog, in
    /// Resources/Shop so the code-built level can load them. Only fills in what is missing, so values
    /// tuned in the Inspector survive. Runs on its own when the catalog is missing; also under the
    /// Roadkill menu.
    /// </summary>
    [InitializeOnLoad]
    public static class ShopAssetBuilder
    {
        const string Folder = "Assets/_Roadkill/Resources/Shop";
        const string CatalogPath = Folder + "/ShopCatalog.asset";
        const string BatPrefabPath = Folder + "/BatHeld.prefab";

        static ShopAssetBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureAssets();
            };
        }

        [MenuItem("Roadkill/Create Missing Shop Assets")]
        static void CreateFromMenu()
        {
            EnsureAssets();
            Debug.Log($"Roadkill: shop assets are in {Folder}");
        }

        public static ShopCatalog EnsureAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            if (existing != null) return existing;

            EnsureFolder(Folder);
            var bat = AssetDatabase.LoadAssetAtPath<GameObject>(BatPrefabPath) ?? BuildBatPrefab();

            var items = new List<ShopItemData>
            {
                Item("BaseballBat", "Baseball Bat", 150, ShopItemData.ItemType.Tool, PrimitiveType.Capsule,
                    new Vector3(0.09f, 0.42f, 0.09f), PropLibrary.Wood, bat),
                Item("HotbarSlotUpgrade", "Hotbar Slot Upgrade", 100, ShopItemData.ItemType.SlotUpgrade, PrimitiveType.Cube,
                    new Vector3(0.3f, 0.3f, 0.3f), new Color(0.3f, 0.8f, 1f), null, 75),
                // Placeholder tools without behaviour, so there is enough to fill the hotbar.
                Item("Wrench", "Wrench", 60, ShopItemData.ItemType.Tool, PrimitiveType.Cube,
                    new Vector3(0.06f, 0.32f, 0.04f), PropLibrary.Metal),
                Item("Flashlight", "Flashlight", 40, ShopItemData.ItemType.Tool, PrimitiveType.Cylinder,
                    new Vector3(0.07f, 0.13f, 0.07f), new Color(0.95f, 0.85f, 0.2f)),
                Item("FishingRod", "Fishing Rod", 80, ShopItemData.ItemType.Tool, PrimitiveType.Cylinder,
                    new Vector3(0.025f, 0.6f, 0.025f), new Color(0.25f, 0.5f, 0.3f)),
            };

            var catalog = ScriptableObject.CreateInstance<ShopCatalog>();
            catalog.Configure(items);
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        static ShopItemData Item(string file, string name, int price, ShopItemData.ItemType type, PrimitiveType shape,
            Vector3 scale, Color color, GameObject held = null, int increase = 0)
        {
            string path = $"{Folder}/{file}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ShopItemData>(path);
            if (existing != null) return existing;
            var item = ScriptableObject.CreateInstance<ShopItemData>();
            item.Configure(name, price, type, shape, scale, color, held, increase);
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        /// <summary>Grip at the root, bat along +Y: a thin handle and a fatter barrel. No colliders: the bat hits by sweeps.</summary>
        static GameObject BuildBatPrefab()
        {
            var root = new GameObject("BatHeld");
            root.AddComponent<BatTool>();
            var wood = NetPrefabBuilder.MaterialFor(PropLibrary.Wood);
            Part(root.transform, "Handle", PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0f), new Vector3(0.035f, 0.2f, 0.035f), wood);
            Part(root.transform, "Barrel", PrimitiveType.Capsule, new Vector3(0f, 0.6f, 0f), new Vector3(0.075f, 0.26f, 0.075f), wood);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, BatPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void Part(Transform parent, string name, PrimitiveType shape, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
