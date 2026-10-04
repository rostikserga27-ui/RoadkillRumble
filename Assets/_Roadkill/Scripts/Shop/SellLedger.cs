using System;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The server side of selling, on the PlaygroundRules object (the vans are local scenery and cannot
    /// send RPCs). The server books a sale into the team's money (TeamWallet), tells every peer about it
    /// for the feedback and despawns the prop. "Sold today" is a NetworkVariable, so every van shows the
    /// same total.
    /// </summary>
    public class SellLedger : NetworkBehaviour
    {
        public struct Sale
        {
            public int Value;
            /// <summary>Who last held or threw it, or Sellable.NoOwner (nudged in).</summary>
            public ulong Seller;
            public string ItemName;
            public Vector3 Point;
        }

        public static SellLedger Instance { get; private set; }

        /// <summary>A sale happened (every peer): vans play their feedback, capybaras comment.</summary>
        public static event Action<Sale> Sold;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Sold = null;
        }

        // Written by the server: money made by selling this session, and how many things were sold.
        NetworkVariable<int> soldToday = new NetworkVariable<int>(0);
        NetworkVariable<int> itemsSold = new NetworkVariable<int>(0);

        public static int SoldToday => Instance != null ? Instance.soldToday.Value : 0;
        public static int ItemsSold => Instance != null ? Instance.itemsSold.Value : 0;

        public override void OnNetworkSpawn() => Instance = this;

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Server: sell this prop (SellZone has already claimed it, so this runs once per prop).</summary>
        public void ServerSell(Sellable item, Vector3 point)
        {
            if (!IsServer || item == null) return;
            int value = item.SellValue;
            ulong seller = item.LastOwner;
            if (seller != Sellable.NoOwner && !NetworkManager.ConnectedClients.ContainsKey(seller)) seller = Sellable.NoOwner;

            if (TeamWallet.Instance != null) TeamWallet.Instance.ServerAdd(value);
            else Debug.LogWarning("Roadkill: no TeamWallet (PlaygroundRules prefab); run Roadkill > Rebuild Network Prefabs.");
            soldToday.Value += value;
            itemsSold.Value++;
            string itemName = item.DisplayName;
            Debug.Log($"Roadkill: {(seller != Sellable.NoOwner ? PlayerNet.NameOf(seller) : "nobody")} sold {itemName} for ${value} to the team");
            SoldRpc(value, seller, itemName, point);

            if (item.NetworkObject != null && item.NetworkObject.IsSpawned) item.NetworkObject.Despawn(true);
            else Destroy(item.gameObject);
        }

        [Rpc(SendTo.Everyone)]
        void SoldRpc(int value, ulong seller, string itemName, Vector3 point)
        {
            Sold?.Invoke(new Sale { Value = value, Seller = seller, ItemName = itemName, Point = point });
        }
    }
}
