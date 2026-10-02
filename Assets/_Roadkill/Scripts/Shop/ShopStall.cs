using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Builds the greybox shop: two posts and a beam with every catalog item hanging from it on a string.
    /// Local scenery like the rest of the test level, so each peer builds its own copy.
    /// </summary>
    public static class ShopStall
    {
        const float BeamHeight = 2.7f;
        const float StringLength = 1.05f;
        const float Spacing = 1.6f;

        public static void Build(ShopCatalog catalog, Vector3 center, Quaternion facing)
        {
            if (catalog == null || catalog.Items.Count == 0)
            {
                Debug.LogWarning("Roadkill: no shop catalog in Resources/Shop. Run Roadkill > Create Missing Shop Assets.");
                return;
            }

            var root = new GameObject("Shop").transform;
            root.SetPositionAndRotation(center, facing);
            float width = (catalog.Items.Count - 1) * Spacing + 1.6f;

            Part(PrimitiveType.Cube, "Post L", root, new Vector3(-width * 0.5f, BeamHeight * 0.5f, 0f), new Vector3(0.2f, BeamHeight, 0.2f), PropLibrary.Wood, true);
            Part(PrimitiveType.Cube, "Post R", root, new Vector3(width * 0.5f, BeamHeight * 0.5f, 0f), new Vector3(0.2f, BeamHeight, 0.2f), PropLibrary.Wood, true);
            Part(PrimitiveType.Cube, "Beam", root, new Vector3(0f, BeamHeight, 0f), new Vector3(width + 0.2f, 0.18f, 0.18f), PropLibrary.Wood, true);
            Label(root, "SHOP   [E] buy", new Vector3(0f, BeamHeight + 0.6f, 0f), 0.05f);

            for (int i = 0; i < catalog.Items.Count; i++)
            {
                float x = (i - (catalog.Items.Count - 1) * 0.5f) * Spacing;
                BuildItem(catalog.Items[i], root, new Vector3(x, BeamHeight - 0.09f, 0f));
            }
        }

        static void BuildItem(ShopItemData data, Transform root, Vector3 hookPosition)
        {
            var slot = new GameObject($"Shop Item: {data.DisplayName}").transform;
            slot.SetParent(root, false);
            slot.localPosition = hookPosition;

            var hanger = new GameObject("Hanger").transform;
            hanger.SetParent(slot, false);
            Part(PrimitiveType.Cylinder, "String", hanger, new Vector3(0f, -StringLength * 0.5f, 0f),
                new Vector3(0.015f, StringLength * 0.5f, 0.015f), new Color(0.9f, 0.9f, 0.85f), false);

            var item = new GameObject("Item").transform;
            item.SetParent(hanger, false);
            item.localPosition = new Vector3(0f, -StringLength - 0.25f, 0f);
            data.CreatePlaceholder(item);
            // A generous box so the buy ray finds thin items like the bat.
            var hitBox = item.gameObject.AddComponent<BoxCollider>();
            hitBox.size = new Vector3(0.45f, 0.6f, 0.45f);

            var label = Label(slot, data.DisplayName, new Vector3(0f, -StringLength - 0.85f, 0f), 0.035f);
            var shopItem = slot.gameObject.AddComponent<ShopItem>();
            shopItem.Setup(data, hanger, item, label);
            shopItem.SetLabel($"{data.DisplayName}\n${data.Price}");
        }

        static GameObject Part(PrimitiveType shape, string name, Transform parent, Vector3 localPosition, Vector3 scale, Color color, bool solid)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            if (!solid) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
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
