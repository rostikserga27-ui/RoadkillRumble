using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// One thing the shop sells. Tools go into the hotbar and appear in the hand; a slot upgrade adds a
    /// hotbar slot and gets pricier with every purchase. The shop shows shopModel if there is one (the
    /// real art), otherwise a placeholder primitive built from shape, scale and colour; a Tool's held
    /// version is heldPrefab, or the same primitive if empty.
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
        [Tooltip("The model hanging in the shop (no scripts or colliders). Empty = the placeholder primitive.")]
        [SerializeField] GameObject shopModel;
        [SerializeField, Min(0.01f)] float shopModelScale = 1f;
        [Tooltip("Turns the shop model so its best side faces the customer.")]
        [SerializeField] Vector3 shopModelEuler;
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

        /// <summary>What the shop shows: the item's model centred on `parent`, or a collider-free coloured primitive.</summary>
        public GameObject CreatePlaceholder(Transform parent)
        {
            if (shopModel != null)
            {
                var holder = new GameObject(displayName);
                holder.transform.SetParent(parent, false);
                var model = Instantiate(shopModel, holder.transform, false);
                model.transform.localScale *= shopModelScale;
                model.transform.localRotation = Quaternion.Euler(shopModelEuler) * model.transform.localRotation;
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    model.transform.position -= bounds.center - holder.transform.position;
                }
                return holder;
            }
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
            Color color, GameObject held = null, int increasePerPurchase = 0, GameObject model = null, float modelScale = 1f,
            Vector3 modelEuler = default)
        {
            shopModel = model;
            shopModelScale = modelScale;
            shopModelEuler = modelEuler;
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
