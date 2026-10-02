using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Server-side "struck" rule (GDD section 3): an object over 20 kg moving toward a player faster
    /// than 3 m/s knocks them down. Props are only simulated on the server, so the server decides and
    /// tells the struck player's owner. Walking into a parked battery never counts, because only the
    /// prop's own velocity before the hit is used.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactReporter : MonoBehaviour
    {
        public float massThreshold = 20f;
        public float speedThreshold = 3f;
        public float downtime = 1.5f;
        [Tooltip("Share of mass x speed x 0.8 dealt as damage.")]
        public float damageFactor = 0.15f;

        Rigidbody body;
        Vector3 lastVelocity;
        Vector3 lastAngularVelocity;

        void Awake() => body = GetComponent<Rigidbody>();

        void FixedUpdate()
        {
            if (body.isKinematic) return;
            lastVelocity = body.linearVelocity;
            lastAngularVelocity = body.angularVelocity;
        }

        void OnCollisionEnter(Collision collision)
        {
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || body.isKinematic || body.mass <= massThreshold) return;

            var player = collision.collider.GetComponentInParent<PlayerNet>();
            if (player == null)
            {
                // A ragdolled body is detached from its player; its rig remembers whose it is.
                var rig = collision.collider.GetComponentInParent<RagdollRig>();
                if (rig != null) player = rig.Player;
            }
            if (player == null || !player.IsSpawned) return;
            if (player.Hands.IsHolding(body)) return;   // your own load bumping you is not a hit

            // Speed into the player along the contact normal, so hits on the head, from above or at the
            // edge of a swing count the same as a square hit to the chest.
            var contact = collision.GetContact(0);
            Vector3 contactVelocity = lastVelocity + Vector3.Cross(lastAngularVelocity, contact.point - body.worldCenterOfMass);
            float closing = Mathf.Abs(Vector3.Dot(contactVelocity, contact.normal));
            if (closing <= speedThreshold) return;

            Vector3 flat = Vector3.ProjectOnPlane(contactVelocity, Vector3.up);
            Vector3 kick = (flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero) + Vector3.up * 0.3f;
            float damage = Mathf.Min(80f, body.mass * closing * 0.8f * damageFactor);
            player.ServerKnockdown(downtime, kick * Mathf.Min(closing, 6f), damage);
        }
    }
}
