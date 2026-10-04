using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Everything the shop sells, in display order. The index doubles as the network id of a held tool,
    /// so every peer must run the same catalog (same build).
    /// </summary>
    [CreateAssetMenu(menuName = "Roadkill/Shop Catalog", fileName = "ShopCatalog")]
    public class ShopCatalog : ScriptableObject
    {
        public const string ResourcePath = "Shop/ShopCatalog";

        [SerializeField] List<ShopItemData> items = new List<ShopItemData>();

        public IReadOnlyList<ShopItemData> Items => items;

        public int IndexOf(ShopItemData item) => item == null ? -1 : items.IndexOf(item);

        public ShopItemData Get(int index) => index >= 0 && index < items.Count ? items[index] : null;

        /// <summary>Used by the editor tool that generates the default shop assets.</summary>
        public void Configure(List<ShopItemData> list) => items = list;
    }
}
