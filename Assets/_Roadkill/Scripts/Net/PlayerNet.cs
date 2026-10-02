using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Glue for a networked player. The owner simulates its own body (owner-authoritative
    /// NetworkTransform), sees through its camera and hides its own body mesh; everyone else sees a
    /// kinematic copy with a head that follows the owner's view. The server delivers knockdowns.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        public Camera playerCamera;
        public AudioListener listener;
        public Transform head;
        public Renderer[] bodyRenderers;
        public Renderer[] tintRenderers;

        public PlayerMotor Motor { get; private set; }
        public HandsController Hands { get; private set; }
        public PlayerHealth Health { get; private set; }

        static readonly Color[] PlayerColors =
        {
            new Color(0.95f, 0.45f, 0.2f),
            new Color(0.25f, 0.6f, 0.95f),
            new Color(0.4f, 0.8f, 0.3f),
            new Color(0.9f, 0.8f, 0.2f),
        };

        float lastKnockdownTime = -99f;

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Hands = GetComponent<HandsController>();
            Health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            bool mine = IsOwner;
            playerCamera.enabled = mine;
            listener.enabled = mine;
            Motor.enabled = mine;
            var hud = GetComponent<DebugHud>();
            if (hud != null) hud.enabled = mine;
            foreach (var r in bodyRenderers) r.enabled = !mine;

            Color color = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
            foreach (var r in tintRenderers) r.material.color = color;
            name = mine ? "Player (you)" : $"Player {OwnerClientId}";

            if (mine)
            {
                // Owner authority: the owner places itself and the NetworkTransform carries it to everyone.
                Vector3 spawn = new Vector3(-2.25f + (OwnerClientId % 4) * 1.5f, 0.05f, -9f);
                transform.SetPositionAndRotation(spawn, Quaternion.identity);
                var body = GetComponent<Rigidbody>();
                body.position = spawn;
                body.rotation = Quaternion.identity;
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned || IsOwner || head == null) return;
            head.rotation = Hands.ViewRotation;
        }

        /// <summary>Server only: knock this player down on their own machine.</summary>
        public void ServerKnockdown(float seconds, Vector3 kick, float damage)
        {
            if (!IsServer || Time.time - lastKnockdownTime < 0.5f) return;
            lastKnockdownTime = Time.time;
            KnockdownRpc(seconds, kick, damage);
        }

        [Rpc(SendTo.Owner)]
        void KnockdownRpc(float seconds, Vector3 kick, float damage)
        {
            Motor.EnterRagdoll(seconds, kick);
            Health.Damage(damage, "struck");
        }
    }
}
