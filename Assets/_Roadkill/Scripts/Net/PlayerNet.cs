using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Glue for a networked player. The owner simulates its own capsule (owner-authoritative
    /// NetworkTransform) and sees through its camera; its own active-ragdoll body runs too but stays
    /// hidden, except while the possum camera pulls back to show a fall. Everyone else sees a kinematic
    /// capsule copy followed by the fisherman's active ragdoll, whose head looks where the owner looks
    /// and which goes limp whenever the owner is ragdolled. The server delivers knockdowns.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PlayerNet : NetworkBehaviour
    {
        public Camera playerCamera;
        public AudioListener listener;
        public Transform head;
        public Renderer[] bodyRenderers;
        [Tooltip("Primitive stand-in body only: renderers tinted in the player's colour.")]
        public Renderer[] tintRenderers;
        [Tooltip("Fisherman: his shirt is repainted in the player's colour.")]
        public PaletteTint palette;
        public ActiveRagdollController body;

        public PlayerMotor Motor { get; private set; }
        public HandsController Hands { get; private set; }
        public PlayerHealth Health { get; private set; }
        public PossumCamera FallCamera { get; private set; }

        static readonly Color[] PlayerColors =
        {
            new Color(1.00f, 0.45f, 0.08f),   // orange
            new Color(0.20f, 0.55f, 0.95f),   // blue
            new Color(0.35f, 0.80f, 0.25f),   // green
            new Color(0.95f, 0.80f, 0.15f),   // yellow
        };

        // Written by the owner: is this player down right now? Drives everyone else's ragdoll.
        NetworkVariable<bool> ragdolled = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        float lastKnockdownTime = -99f;
        Quaternion headRestRelative = Quaternion.identity;

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Hands = GetComponent<HandsController>();
            Health = GetComponent<PlayerHealth>();
            // The head bone's rest orientation relative to the body, whatever axes the model uses.
            if (head != null) headRestRelative = Quaternion.Inverse(transform.rotation) * head.rotation;
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
            if (mine)
            {
                // The owner's body follows its PlayerMotor (ragdoll state, look); PlayerMotor and the
                // grab ray ignore its colliders. The fall camera shows it while the player is down.
                FallCamera = gameObject.AddComponent<PossumCamera>();
                FallCamera.Initialize(this);
            }

            Color color = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
            foreach (var r in tintRenderers) r.material.color = color;
            if (palette != null) palette.Apply(color);
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

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                if (ragdolled.Value != Motor.IsRagdolled) ragdolled.Value = Motor.IsRagdolled;
                return;
            }

            if (body != null)
            {
                body.FollowRootRagdoll(ragdolled.Value);
                body.SetLook(Hands.ViewRotation);
            }
        }

        void LateUpdate()
        {
            // The fisherman's head is aimed through its neck joint (ActiveRagdollController); this is for the stand-in.
            if (!IsSpawned || IsOwner || head == null || body != null) return;
            head.rotation = Hands.ViewRotation * headRestRelative;
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
