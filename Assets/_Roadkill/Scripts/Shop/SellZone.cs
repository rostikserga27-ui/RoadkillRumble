using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The trigger inside the sell van's cargo area. Server only: a Sellable whose centre of mass stays
    /// inside the box for settleSeconds, with no hand on it, is sold through SellLedger (money, feedback,
    /// despawn). A prop that only clips the edge, or one a player is still carrying, does not sell. Each
    /// peer builds its own van, but only the server's zone ever acts.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SellZone : MonoBehaviour
    {
        [Tooltip("Seconds the item's centre must stay inside, untouched, before it sells.")]
        [SerializeField, Min(0f)] float settleSeconds = 0.15f;

        class Entry
        {
            public int Colliders;
            public float Settled;
        }

        BoxCollider box;
        bool warnedNoLedger;
        // Rigidbody -> its Sellable (or null), so triggers do not look components up again and again.
        readonly Dictionary<Rigidbody, Sellable> lookup = new Dictionary<Rigidbody, Sellable>();
        readonly Dictionary<Sellable, Entry> inside = new Dictionary<Sellable, Entry>();
        readonly List<Sellable> scratch = new List<Sellable>();

        static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void OnDisable() => inside.Clear();

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            var item = Find(other);
            if (item == null || item.IsSold) return;
            if (!inside.TryGetValue(item, out var entry)) inside[item] = entry = new Entry();
            entry.Colliders++;
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            var item = Find(other);
            if (item != null && inside.TryGetValue(item, out var entry) && --entry.Colliders <= 0) inside.Remove(item);
        }

        Sellable Find(Collider other)
        {
            var body = other.attachedRigidbody;
            if (body == null) return null;
            if (!lookup.TryGetValue(body, out var item))
            {
                if (lookup.Count > 512) lookup.Clear();   // despawned props pile up as keys otherwise
                body.TryGetComponent(out item);
                lookup[body] = item;
            }
            return item;
        }

        void FixedUpdate()
        {
            if (inside.Count == 0 || !IsServer) return;
            scratch.Clear();
            scratch.AddRange(inside.Keys);
            foreach (var item in scratch)
            {
                // Despawned some other way (reset props), or sold by another zone.
                if (item == null || item.IsSold || !item.isActiveAndEnabled) { inside.Remove(item); continue; }
                var entry = inside[item];
                if (item.IsHeld || !Contains(item.Body.worldCenterOfMass))
                {
                    entry.Settled = 0f;
                    continue;
                }
                entry.Settled += Time.fixedDeltaTime;
                if (entry.Settled >= settleSeconds) Sell(item);
            }
        }

        /// <summary>Is a world point inside the trigger box?</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        void Sell(Sellable item)
        {
            var ledger = SellLedger.Instance;
            if (ledger == null)
            {
                if (!warnedNoLedger) Debug.LogWarning("Roadkill: no SellLedger (PlaygroundRules prefab); run Roadkill > Rebuild Network Prefabs.");
                warnedNoLedger = true;
                return;
            }
            inside.Remove(item);
            if (!item.ServerTryMarkSold()) return;
            ledger.ServerSell(item, transform.position);
        }
    }
}
