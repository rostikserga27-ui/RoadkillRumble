using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Glue for a networked player. The owner simulates its own body (owner-authoritative
    /// NetworkTransform), sees through its camera and shows its body during the possum camera; everyone else sees a
    /// kinematic copy whose head follows the owner's view and whose bones flop (RagdollRig) whenever
    /// the owner is ragdolled. The owner's own ragdoll is simulated by PlayerMotor; copies pin theirs to
    /// the networked capsule. The server delivers knockdowns.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after CharacterAnimator, so the head aim wins
    public class PlayerNet : NetworkBehaviour
    {
        public Camera playerCamera;
        public AudioListener listener;
        public Transform head;
        public Renderer[] bodyRenderers;
        public Renderer[] tintRenderers;
        public RagdollRig ragdoll;
        public CharacterAnimator animator;

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
        Vector3 lastPosition;
        Vector3 velocity;

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
            // The owner sees the body during the possum camera transition. Its bone colliders only
            // switch on while ragdolled, so they never trip the ground check or block the grab ray.
            if (mine)
            {
                if (ragdoll != null)
                {
                    ragdoll.collidersWhenIdle = false;
                    ragdoll.SetCollidersEnabled(false);
                }
                if (animator != null) animator.enabled = true;
                FallCamera = gameObject.AddComponent<PossumCamera>();
                FallCamera.Initialize(this);
            }

            Color color = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
            foreach (var r in tintRenderers) r.material.color = color;
            name = mine ? "Player (you)" : $"Player {OwnerClientId}";
            lastPosition = transform.position;

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

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            velocity = Vector3.Lerp(velocity, (transform.position - lastPosition) / dt, 0.5f);
            lastPosition = transform.position;
            if (ragdoll != null) ragdoll.SetActive(ragdolled.Value, velocity);
        }

        void LateUpdate()
        {
            if (!IsSpawned || head == null) return;
            if (ragdoll != null && ragdoll.IsActive) return;
            // Eased, so a head left twisted by the ragdoll turns back to the view instead of snapping.
            // The owner needs it too: their body shows in the fall camera.
            Quaternion aim = Hands.ViewRotation * headRestRelative;
            head.rotation = Quaternion.Slerp(head.rotation, aim, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        public override void OnDestroy()
        {
            // A ragdolled body lives outside the player hierarchy; do not leave it lying around.
            if (ragdoll != null && ragdoll.IsActive) Destroy(ragdoll.gameObject);
            base.OnDestroy();
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
