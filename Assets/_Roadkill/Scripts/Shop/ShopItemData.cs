using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// One thing the shop sells. Tools go into the hotbar and appear in the hand; a slot upgrade adds a
    /// hotbar slot and gets pricier with every purchase. The shop shows a placeholder primitive built
    /// from shape, scale and colour; a Tool's held version is heldPrefab, or the same primitive if empty.
    /// </summary>
    [CreateAssetMenu(menuName = "Roadkill/Shop Item", fileName = "ShopItem")]
    public class ShopItemData : ScriptableObject
    {
        public enum ItemType { Tool, SlotUpgrade }

        [SerializeField] string displayName = "Item";
        [SerializeField, Min(0)] int price = 50;
        [SerializeField] ItemType type = ItemType.Tool;
        [Tooltip("Tool only: what the player holds. Empty = the placeholder primitive.")]
        [SerializeField] GameObject heldPrefab;
        [SerializeField] PrimitiveType placeholderShape = PrimitiveType.Cube;
        [SerializeField] Vector3 placeholderScale = new Vector3(0.25f, 0.25f, 0.25f);
        [SerializeField] Color iconColor = Color.white;
        [Tooltip("Slot upgrade only: added to the price after each purchase.")]
        [SerializeField, Min(0)] int priceIncreasePerPurchase = 0;

        public string DisplayName => displayName;
        public int Price => price;
        public ItemType Type => type;
        public GameObject HeldPrefab => heldPrefab;
        public Color IconColor => iconColor;

        /// <summary>Price once the player has already bought this item <paramref name="purchases"/> times.</summary>
        public int PriceAfter(int purchases) => price + priceIncreasePerPurchase * Mathf.Max(0, purchases);

        /// <summary>A collider-free coloured primitive standing in for the item's art.</summary>
        public GameObject CreatePlaceholder(Transform parent)
        {
            var go = GameObject.CreatePrimitive(placeholderShape);
            Destroy(go.GetComponent<Collider>());
            go.name = displayName;
            go.transform.SetParent(parent, false);
            go.transform.localScale = placeholderScale;
            go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(iconColor);
            return go;
        }

        /// <summary>Used by the editor tool that generates the default shop assets.</summary>
        public void Configure(string itemName, int itemPrice, ItemType itemType, PrimitiveType shape, Vector3 scale,
            Color color, GameObject held = null, int increasePerPurchase = 0)
        {
            displayName = itemName;
            price = itemPrice;
            type = itemType;
            placeholderShape = shape;
            placeholderScale = scale;
            iconColor = color;
            heldPrefab = held;
            priceIncreasePerPurchase = increasePerPurchase;
        }
    }
}
