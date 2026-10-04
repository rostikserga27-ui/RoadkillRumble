using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Builds a shop from the catalog, How To Fish style: walk up, look at a thing, press E. Three looks,
    /// to suit different locations, each with a capybara behind or beside it:
    ///   Door  - things hang on hooks on a wooden door set in a stone wall (a hut, a tower);
    ///   Stall - a market counter under a striped awning, things hanging from its front beam;
    ///   Table - things laid out on a table under a parasol (a camp, a roadside).
    /// Local scenery like the rest of the test level, so each peer builds its own copy. The shop faces its
    /// local +Z: customers stand in front of it.
    /// </summary>
    public static class ShopStall
    {
        public enum Style { Door, Stall, Table }

        static readonly Color Stone = new Color(0.84f, 0.82f, 0.78f);
        static readonly Color StoneDark = new Color(0.62f, 0.6f, 0.57f);
        static readonly Color DoorWood = new Color(0.33f, 0.22f, 0.15f);
        static readonly Color DoorWoodDark = new Color(0.22f, 0.14f, 0.09f);
        static readonly Color Iron = new Color(0.18f, 0.18f, 0.2f);
        static readonly Color String = new Color(0.9f, 0.9f, 0.85f);
        static readonly Color AwningRed = new Color(0.82f, 0.22f, 0.2f);
        static readonly Color AwningWhite = new Color(0.95f, 0.93f, 0.88f);

        public static void Build(ShopCatalog catalog, Vector3 center, Quaternion facing, Style style = Style.Stall)
        {
            if (catalog == null || catalog.Items.Count == 0)
            {
                Debug.LogWarning("Roadkill: no shop catalog in Resources/Shop. Run Roadkill > Create Missing Shop Assets.");
                return;
            }
            var root = new GameObject($"Shop ({style})").transform;
            root.SetPositionAndRotation(center, facing);
            switch (style)
            {
                case Style.Door: BuildDoor(catalog, root); break;
                case Style.Stall: BuildStall(catalog, root); break;
                default: BuildTable(catalog, root); break;
            }
        }

        // ---- door in a wall --------------------------------------------------------------------------

        static void BuildDoor(ShopCatalog catalog, Transform root)
        {
            Part(PrimitiveType.Cube, "Wall", root, new Vector3(0f, 1.7f, -0.25f), new Vector3(3.6f, 3.4f, 0.5f), Stone, true);
            Part(PrimitiveType.Cube, "Wall Cap", root, new Vector3(0f, 3.45f, -0.25f), new Vector3(3.8f, 0.15f, 0.6f), StoneDark, true);
            Part(PrimitiveType.Cube, "Frame L", root, new Vector3(-0.85f, 1.25f, 0.02f), new Vector3(0.14f, 2.5f, 0.1f), StoneDark, true);
            Part(PrimitiveType.Cube, "Frame R", root, new Vector3(0.85f, 1.25f, 0.02f), new Vector3(0.14f, 2.5f, 0.1f), StoneDark, true);
            Part(PrimitiveType.Cube, "Frame Top", root, new Vector3(0f, 2.55f, 0.02f), new Vector3(1.84f, 0.16f, 0.1f), StoneDark, true);
            Part(PrimitiveType.Cube, "Door", root, new Vector3(0f, 1.24f, 0f), new Vector3(1.56f, 2.46f, 0.08f), DoorWood, true);
            for (int i = 0; i < 4; i++)   // planks
                Part(PrimitiveType.Cube, "Plank Gap", root, new Vector3(-0.585f + i * 0.39f, 1.24f, 0.042f), new Vector3(0.02f, 2.4f, 0.01f), DoorWoodDark, false);
            Part(PrimitiveType.Cube, "Handle", root, new Vector3(-0.55f, 1.15f, 0.08f), new Vector3(0.22f, 0.04f, 0.04f), Iron, false);
            Label(root, "SHOP", new Vector3(0f, 2.95f, 0.05f), 0.07f);
            Crate(root, new Vector3(-1.45f, 0f, 0.55f));

            // Two columns of hooks on the door.
            int n = catalog.Items.Count;
            for (int i = 0; i < n; i++)
            {
                int column = i % 2, row = i / 2;
                var hook = new Vector3(column == 0 ? -0.36f : 0.36f, 2.2f - row * 0.62f, 0.07f);
                Part(PrimitiveType.Cylinder, "Hook", root, hook + new Vector3(0f, 0f, 0.03f), new Vector3(0.03f, 0.04f, 0.03f), Iron, false,
                    Quaternion.Euler(90f, 0f, 0f));
                BuildHanging(catalog.Items[i], root, hook + new Vector3(0f, 0f, 0.08f), 0.12f, 0.025f, swayDegrees: 3f);
            }
            CapybaraMerchant.Build(root, new Vector3(1.45f, 0f, 0.65f), -25f, root);
        }

        // ---- market stall ---------------------------------------------------------------------------

        static void BuildStall(ShopCatalog catalog, Transform root)
        {
            int n = catalog.Items.Count;
            float width = Mathf.Max(3.2f, n * 0.75f + 0.6f);
            Part(PrimitiveType.Cube, "Counter", root, new Vector3(0f, 0.5f, 0f), new Vector3(width, 1f, 0.7f), PropLibrary.Wood, true);
            Part(PrimitiveType.Cube, "Counter Top", root, new Vector3(0f, 1.02f, 0.02f), new Vector3(width + 0.1f, 0.06f, 0.8f), DoorWood, true);
            foreach (float x in new[] { -width * 0.5f, width * 0.5f })
            {
                Part(PrimitiveType.Cube, "Post Front", root, new Vector3(x, 1.25f, 0.42f), new Vector3(0.12f, 2.5f, 0.12f), PropLibrary.Wood, true);
                Part(PrimitiveType.Cube, "Post Back", root, new Vector3(x, 1.45f, -1.1f), new Vector3(0.12f, 2.9f, 0.12f), PropLibrary.Wood, true);
            }
            Part(PrimitiveType.Cube, "Front Beam", root, new Vector3(0f, 2.45f, 0.42f), new Vector3(width + 0.15f, 0.12f, 0.12f), PropLibrary.Wood, true);
            // Striped awning sloping down to the front.
            int stripes = Mathf.CeilToInt(width / 0.55f);
            float stripe = (width + 0.4f) / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float x = -(width + 0.4f) * 0.5f + stripe * (i + 0.5f);
                Part(PrimitiveType.Cube, "Awning", root, new Vector3(x, 2.72f, -0.35f), new Vector3(stripe, 0.05f, 1.75f),
                    i % 2 == 0 ? AwningRed : AwningWhite, false, Quaternion.Euler(-14f, 0f, 0f));
            }
            Label(root, "SHOP", new Vector3(0f, 3.25f, 0.4f), 0.07f);

            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * (width - 0.6f) / Mathf.Max(1, n);
                BuildHanging(catalog.Items[i], root, new Vector3(x, 2.38f, 0.42f), 0.55f, 0.028f, swayDegrees: 6f);
            }
            // The capybara sits behind the counter on a crate, head and hat above it.
            Crate(root, new Vector3(0.5f, 0f, -0.75f));
            CapybaraMerchant.Build(root, new Vector3(0.5f, 0.5f, -0.75f), 0f, root);
        }

        // ---- table -----------------------------------------------------------------------------------

        static void BuildTable(ShopCatalog catalog, Transform root)
        {
            int n = catalog.Items.Count;
            float width = Mathf.Max(2.4f, n * 0.62f + 0.4f);
            Part(PrimitiveType.Cube, "Table Top", root, new Vector3(0f, 0.8f, 0f), new Vector3(width, 0.08f, 0.95f), PropLibrary.Wood, true);
            foreach (float x in new[] { -width * 0.5f + 0.1f, width * 0.5f - 0.1f })
            foreach (float z in new[] { -0.38f, 0.38f })
                Part(PrimitiveType.Cube, "Table Leg", root, new Vector3(x, 0.38f, z), new Vector3(0.08f, 0.76f, 0.08f), DoorWood, true);
            // Parasol.
            Part(PrimitiveType.Cylinder, "Parasol Pole", root, new Vector3(-width * 0.5f - 0.35f, 1.2f, -0.2f), new Vector3(0.05f, 1.2f, 0.05f), Iron, true);
            Part(PrimitiveType.Cylinder, "Parasol", root, new Vector3(-width * 0.5f - 0.35f + 0.6f, 2.4f, -0.2f), new Vector3(2.4f, 0.04f, 2.4f), AwningRed, false,
                Quaternion.Euler(0f, 0f, -8f));
            Label(root, "SHOP", new Vector3(0f, 1.9f, -0.2f), 0.06f);

            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * (width - 0.4f) / Mathf.Max(1, n);
                BuildLying(catalog.Items[i], root, new Vector3(x, 0.84f, 0.12f));
            }
            CapybaraMerchant.Build(root, new Vector3(0.3f, 0f, -0.95f), 0f, root);
        }

        // ---- items ------------------------------------------------------------------------------------

        /// <summary>An item hanging on a string from a hook point; it sways and bobs.</summary>
        static void BuildHanging(ShopItemData data, Transform root, Vector3 hookPosition, float stringLength, float labelSize, float swayDegrees)
        {
            var slot = new GameObject($"Shop Item: {data.DisplayName}").transform;
            slot.SetParent(root, false);
            slot.localPosition = hookPosition;

            var hanger = new GameObject("Hanger").transform;
            hanger.SetParent(slot, false);
            Part(PrimitiveType.Cylinder, "String", hanger, new Vector3(0f, -stringLength * 0.5f, 0f),
                new Vector3(0.012f, stringLength * 0.5f, 0.012f), String, false);

            var item = new GameObject("Item").transform;
            item.SetParent(hanger, false);
            var shape = data.CreatePlaceholder(item);
            // Long things (a bat, a rod) hang sideways from their middle instead of dangling to the floor.
            if (BoundsOf(shape.transform).size.y > 0.5f) item.localRotation = Quaternion.Euler(0f, 0f, 90f);
            item.localPosition = new Vector3(0f, -stringLength - HalfHeight(item), 0f);
            AddHitBox(item);

            var label = Label(slot, "", new Vector3(0f, -stringLength - HalfHeight(item) * 2f - 0.1f, 0.05f), labelSize);
            var shopItem = slot.gameObject.AddComponent<ShopItem>();
            shopItem.Setup(data, hanger, item, label);
            shopItem.SetMotion(swayDegrees, 0.02f);
            shopItem.SetLabel("$" + data.Price);
        }

        /// <summary>An item lying on a table, with its price tag standing up behind it.</summary>
        static void BuildLying(ShopItemData data, Transform root, Vector3 position)
        {
            var slot = new GameObject($"Shop Item: {data.DisplayName}").transform;
            slot.SetParent(root, false);
            slot.localPosition = position;
            var holder = new GameObject("Holder").transform;
            holder.SetParent(slot, false);

            var item = new GameObject("Item").transform;
            item.SetParent(holder, false);
            var shape = data.CreatePlaceholder(item);
            // Long things lie down along the table.
            var size = BoundsOf(shape.transform).size;
            if (size.y > Mathf.Max(size.x, size.z) * 1.4f) item.localRotation = Quaternion.Euler(0f, 0f, 90f);
            item.localPosition = new Vector3(0f, HalfHeight(item), 0f);
            AddHitBox(item);

            var label = Label(slot, "", new Vector3(0f, 0.08f, 0.32f), 0.022f);   // a tag at the table's front edge
            var shopItem = slot.gameObject.AddComponent<ShopItem>();
            shopItem.Setup(data, holder, item, label);
            shopItem.SetMotion(0f, 0f);
            shopItem.SetLabel("$" + data.Price);
        }

        /// <summary>World bounds of everything drawn under `item` (a model can have several parts).</summary>
        static Bounds BoundsOf(Transform item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(item.position, Vector3.one * 0.3f);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static float HalfHeight(Transform item) => BoundsOf(item).extents.y;

        /// <summary>A generous box, so the buy ray finds thin items like the bat.</summary>
        static void AddHitBox(Transform item)
        {
            var box = item.gameObject.AddComponent<BoxCollider>();
            var bounds = BoundsOf(item);
            Vector3 size = item.InverseTransformVector(bounds.size);
            box.center = item.InverseTransformPoint(bounds.center);
            box.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.35f), Mathf.Max(Mathf.Abs(size.y), 0.35f), Mathf.Max(Mathf.Abs(size.z), 0.35f));
        }

        static void Crate(Transform root, Vector3 position)
        {
            Part(PrimitiveType.Cube, "Crate", root, position + Vector3.up * 0.25f, new Vector3(0.6f, 0.5f, 0.6f), PropLibrary.Wood, true);
        }

        static GameObject Part(PrimitiveType shape, string name, Transform parent, Vector3 localPosition, Vector3 scale, Color color, bool solid,
            Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            if (!solid) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(color);
            return go;
        }

        static TextMesh Label(Transform parent, string text, Vector3 localPosition, float size)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
            mesh.fontSize = 48;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color.white;
            go.AddComponent<FaceCamera>();
            return mesh;
        }
    }
}
