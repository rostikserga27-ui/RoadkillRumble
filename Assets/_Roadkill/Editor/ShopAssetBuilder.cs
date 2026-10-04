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
        const string RevolverPrefabPath = Folder + "/RevolverHeld.prefab";
        const string RevolverDisplayPath = Folder + "/RevolverDisplay.prefab";
        const string RevolverModelPath = "Assets/_Roadkill/Art/Revolver/Revolver.fbx";

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
            if (existing != null)
            {
                EnsureRevolver(existing);
                return existing;
            }

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
            EnsureRevolver(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>
        /// The first gun (added to catalogs made before it existed): Grandpa's Revolver, second in the shop.
        /// The model comes from Blender (Art/Revolver, source in Source~); the held prefab adds RevolverTool.
        /// </summary>
        static void EnsureRevolver(ShopCatalog catalog)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverModelPath);
            if (modelAsset == null) return;   // the model has not been imported yet
            string path = $"{Folder}/Revolver.asset";
            var item = AssetDatabase.LoadAssetAtPath<ShopItemData>(path);
            if (item == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverPrefabPath) ?? BuildRevolverPrefab(modelAsset);
                var display = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverDisplayPath) ?? BuildRevolverDisplay(modelAsset);
                item = ScriptableObject.CreateInstance<ShopItemData>();
                item.Configure("Grandpa's Revolver", 350, ShopItemData.ItemType.Tool, PrimitiveType.Cube,
                    new Vector3(0.05f, 0.12f, 0.3f), new Color(0.86f, 0.64f, 0.24f), prefab, 0, display, 1.6f, new Vector3(0f, 90f, 0f));
                AssetDatabase.CreateAsset(item, path);
            }
            if (catalog.IndexOf(item) >= 0) return;
            var items = new List<ShopItemData>(catalog.Items);
            items.Insert(Mathf.Min(1, items.Count), item);
            catalog.Configure(items);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Grip at the root, barrel along +Z: the Blender model turned around, a bit chunky for first person.</summary>
        static GameObject BuildRevolverPrefab(GameObject modelAsset)
        {
            var root = new GameObject("RevolverHeld");
            var tool = root.AddComponent<RevolverTool>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            AssembleRevolver(model.transform);
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // exported with the barrel along -Z
            model.transform.localScale = Vector3.one * 1.15f;
            tool.Setup(model.transform, model.transform.Find("MuzzlePoint"));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RevolverPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The FBX comes flat (Blender's exporter turns grandchildren's axes, so every part sits under the root);
        /// rebuild the moving chain here, keeping each part where it is: the crane carries the cylinder, the
        /// cylinder carries the ejector, the rounds and the speedloader's socket.
        /// </summary>
        static void AssembleRevolver(Transform model)
        {
            Transform Part(string name) => model.Find(name);
            var crane = Part("Crane");
            var cylinder = Part("Cylinder");
            if (crane == null || cylinder == null) return;
            cylinder.SetParent(crane, true);
            foreach (var name in new[] { "Ejector", "LoaderSocket", "Round0", "Round1", "Round2", "Round3", "Round4", "Round5" })
                Part(name)?.SetParent(cylinder, true);
            Part("SpeedloaderRounds")?.SetParent(Part("Speedloader"), true);
        }

        /// <summary>What hangs in the shop: the model without its spare reload parts (speedloader, spent case).</summary>
        static GameObject BuildRevolverDisplay(GameObject modelAsset)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var part in new[] { "Speedloader", "Casing" })
            {
                var t = model.transform.Find(part);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            model.name = "RevolverDisplay";
            var prefab = PrefabUtility.SaveAsPrefabAsset(model, RevolverDisplayPath);
            Object.DestroyImmediate(model);
            return prefab;
        }

        [MenuItem("Roadkill/Rebuild Revolver")]
        public static void RebuildRevolver()
        {
            AssetDatabase.DeleteAsset(RevolverPrefabPath);
            var item = AssetDatabase.LoadAssetAtPath<ShopItemData>($"{Folder}/Revolver.asset");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverModelPath);
            if (item == null || modelAsset == null) { EnsureAssets(); return; }
            AssetDatabase.DeleteAsset(RevolverDisplayPath);
            var prefab = BuildRevolverPrefab(modelAsset);
            var display = BuildRevolverDisplay(modelAsset);
            item.Configure("Grandpa's Revolver", item.Price, ShopItemData.ItemType.Tool, PrimitiveType.Cube,
                new Vector3(0.05f, 0.12f, 0.3f), new Color(0.86f, 0.64f, 0.24f), prefab, 0, display, 1.6f, new Vector3(0f, 90f, 0f));
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();
            Debug.Log("Roadkill: revolver prefab rebuilt");
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
