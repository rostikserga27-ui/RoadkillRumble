using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// A loose prop the capybara buys: throw or drop it into the open back of the sell van (SellZone) and
    /// it is gone, and its value goes into the team's money. Server state only: the server sees the grabs
    /// (HandsController sets LastOwner, shown as the seller), decides the sale and despawns the prop.
    /// Hotbar tools are never physical props, so they can never be sold; props without this just bounce.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Sellable : MonoBehaviour
    {
        public const ulong NoOwner = ulong.MaxValue;

        [SerializeField, Min(1)] int sellValue = 25;
        [Tooltip("Empty = the PhysicsProp's name.")]
        [SerializeField] string displayName = "";
        [Tooltip("Optional: sell for a share of this shop item's price instead of sellValue.")]
        [SerializeField] ShopItemData priceFrom;
        [SerializeField, Range(0f, 1f)] float resaleShare = 0.6f;

        public int SellValue => priceFrom != null ? Mathf.Max(1, Mathf.RoundToInt(priceFrom.Price * resaleShare)) : sellValue;
        public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName : prop != null ? prop.displayName : name;
        public Rigidbody Body { get; private set; }
        public NetworkObject NetworkObject { get; private set; }

        /// <summary>Server: already sold (despawning), so a second zone or collider cannot sell it again.</summary>
        public bool IsSold { get; private set; }
        /// <summary>Server: the client who last picked it up or threw it (credited as the seller), or NoOwner.</summary>
        public ulong LastOwner { get; private set; } = NoOwner;
        /// <summary>Some player's hand is on it (valid on every peer).</summary>
        public bool IsHeld => HandsController.HolderCount(Body) > 0;

        PhysicsProp prop;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            NetworkObject = GetComponent<NetworkObject>();
            prop = GetComponent<PhysicsProp>();
        }

        /// <summary>Used by the editor tool that builds the network prefabs.</summary>
        public void Configure(int value) => sellValue = Mathf.Max(1, value);

        public void ServerSetLastOwner(ulong clientId) => LastOwner = clientId;

        /// <summary>Server: claim the sale. False if it was already sold.</summary>
        public bool ServerTryMarkSold()
        {
            if (IsSold) return false;
            IsSold = true;
            return true;
        }
    }
}
