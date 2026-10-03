using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Out cold (GDD phase 1, step 11). At 0 HP a player is not respawned at once: they lie unconscious
    /// for unconsciousSeconds (PlayerHealth keeps the clock), and friends can
    ///   - drag the body: E on it takes hold with both hands, like a 70 kg load (HandsController). One
    ///     player drags it along the ground; two can carry it;
    ///   - revive them: hold F next to the body for reviveSeconds; they get up with reviveHealth;
    ///   - throw it (G), for the comedy.
    /// The downed player can hold R to give up and respawn. When the clock runs out they respawn too
    /// (later: on the car's back seat).
    ///
    /// The body is simulated by its owner while it is down (the limp body leads, RagdollPoseSync shows it to
    /// everyone), so the drag is physical on the owner's machine: every hand that the server says holds this
    /// body gets a kinematic anchor at that player's hand position (their view pose is already shared) and a
    /// force-capped joint to the grabbed bone, scaled so the body weighs cargoMass to the hands.
    /// </summary>
    public class PlayerKnockout : NetworkBehaviour
    {
        [Header("Revive")]
        public float reviveSeconds = 3f;
        public float reviveHealth = 35f;
        [Tooltip("Revive a friend whose body lies within this distance (metres, from your feet to their hips).")]
        public float reviveRange = 2.2f;
        [Tooltip("Hold R this long while out cold to give up and respawn.")]
        public float giveUpSeconds = 2f;

        [Header("Dragging")]
        [Tooltip("What the body weighs to the hands holding it (kg): one player (40 kg) drags it, two (80 kg) carry it.")]
        public float cargoMass = 70f;

        // Written by the owner: out cold, and the whole seconds left on the clock (for everyone's labels).
        NetworkVariable<bool> unconscious = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        NetworkVariable<byte> secondsLeft = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Out cold (as the owner reports it). Valid on every peer.</summary>
        public bool IsUnconscious => unconscious.Value;
        public int SecondsLeft => secondsLeft.Value;
        public PlayerNet Player { get; private set; }

        /// <summary>Owner: the friend in reach to revive, and how far along the revive is (0..1).</summary>
        public PlayerKnockout ReviveTarget { get; private set; }
        public float ReviveProgress { get; private set; }
        /// <summary>Owner, while out cold: how far along giving up is (0..1).</summary>
        public float GiveUpProgress { get; private set; }

        static readonly List<PlayerKnockout> All = new List<PlayerKnockout>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        class Grip
        {
            public HandsController By;
            public int Hand;
            public int Part;
            public Vector3 Local;
            public Rigidbody Anchor;
            public ConfigurableJoint Joint;
        }

        readonly List<Grip> grips = new List<Grip>();
        float giveUp, reviveHeld, debugReviveUntil = -1f;
        GUIStyle labelStyle;

        void Awake() => Player = GetComponent<PlayerNet>();

        public override void OnNetworkSpawn() => All.Add(this);

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            ClearGrips();
        }

        /// <summary>The knockout state of the player a collider (capsule or body bone) belongs to.</summary>
        public static PlayerKnockout Of(Collider collider)
        {
            var bone = collider.GetComponentInParent<RagdollBodyPart>();
            var player = bone != null ? bone.Player : collider.GetComponentInParent<PlayerNet>();
            return player != null ? player.GetComponent<PlayerKnockout>() : null;
        }

        // ---- owner -----------------------------------------------------------------------------------

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var health = Player.Health;
            if (unconscious.Value != health.IsDown) unconscious.Value = health.IsDown;
            byte left = (byte)Mathf.CeilToInt(Mathf.Clamp(health.DownSecondsLeft, 0f, 255f));
            if (secondsLeft.Value != left) secondsLeft.Value = left;

            // Out cold: hold R to give up.
            giveUp = health.IsDown && RkInput.ResetHeld ? giveUp + Time.deltaTime : 0f;
            GiveUpProgress = Mathf.Clamp01(giveUp / giveUpSeconds);
            if (giveUp >= giveUpSeconds)
            {
                giveUp = 0f;
                health.RespawnNow();
            }

            UpdateRevive();
        }

        /// <summary>Hold F next to a friend who is out cold to get them up.</summary>
        void UpdateRevive()
        {
            ReviveTarget = null;
            bool able = !Player.Health.IsDown && !Player.Motor.IsRagdolled;
            float best = reviveRange;
            if (able)
            {
                foreach (var other in All)
                {
                    if (other == this || !other.IsUnconscious || other.Player.body == null) continue;
                    Vector3 offset = Vector3.ProjectOnPlane(other.Player.body.Hips.position - transform.position, Vector3.up);
                    if (offset.magnitude < best)
                    {
                        best = offset.magnitude;
                        ReviveTarget = other;
                    }
                }
            }
            bool holding = ReviveTarget != null && (RkInput.ReviveHeld || Time.time < debugReviveUntil);
            reviveHeld = holding ? reviveHeld + Time.deltaTime : 0f;
            ReviveProgress = Mathf.Clamp01(reviveHeld / reviveSeconds);
            if (holding && reviveHeld >= reviveSeconds)
            {
                reviveHeld = 0f;
                ReviveRpc(ReviveTarget.NetworkObject);
            }
        }

        /// <summary>Test hook: hold F for `seconds`.</summary>
        public void DebugRevive(float seconds) => debugReviveUntil = Time.time + seconds;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReviveRpc(NetworkObjectReference targetRef)
        {
            if (!targetRef.TryGet(out NetworkObject target)) return;
            var knockout = target.GetComponent<PlayerKnockout>();
            if (knockout == null || knockout == this || !knockout.IsUnconscious) return;
            // The capsule of a downed player follows its body's hips. Allow for lag, not for reviving across the map.
            if (Vector3.Distance(transform.position, target.transform.position) > reviveRange + 1.5f) return;
            knockout.WakeRpc(knockout.reviveHealth);
            if (PlaygroundRules.Instance != null)
                PlaygroundRules.Instance.Announce($"{PlayerNet.NameOf(OwnerClientId)} got {PlayerNet.NameOf(target.OwnerClientId)} back up");
        }

        [Rpc(SendTo.Owner)]
        void WakeRpc(float health) => Player.Health.Revive(health);

        /// <summary>Server: a friend threw this body (G while dragging it).</summary>
        public void ServerThrow(Vector3 velocity)
        {
            if (IsServer && IsUnconscious) ThrowRpc(velocity);
        }

        [Rpc(SendTo.Owner)]
        void ThrowRpc(Vector3 velocity)
        {
            if (Player.body != null && Player.Health.IsDown) Player.body.AddVelocity(velocity);
        }

        // ---- owner: the body is dragged by friends' hands -------------------------------------------

        void FixedUpdate()
        {
            if (!IsSpawned || !IsOwner) return;
            var body = Player.body;
            bool draggable = IsUnconscious && Player.Health.IsDown && body != null && body.BodyLeads;
            if (!draggable)
            {
                if (grips.Count > 0) ClearGrips();
                return;
            }

            // Which hands hold this body right now (the server decides; every peer knows).
            for (int i = grips.Count - 1; i >= 0; i--)
            {
                var g = grips[i];
                if (g.By == null || !g.By.TryGetBodyGrip(g.Hand, NetworkObjectId, out int part, out Vector3 local) || part != g.Part || local != g.Local)
                {
                    DropGrip(g);
                    grips.RemoveAt(i);
                }
            }
            foreach (var hands in HandsController.Players)
            for (int h = 0; h < 2; h++)
            {
                if (!hands.TryGetBodyGrip(h, NetworkObjectId, out int part, out Vector3 local) || part < 0 || part >= body.PartCount) continue;
                if (grips.Exists(g => g.By == hands && g.Hand == h)) continue;
                grips.Add(MakeGrip(hands, h, part, local));
            }

            foreach (var g in grips)
                g.Anchor.MovePosition(g.By.HoldPoint(g.Hand, HandsController.BodyHoldDistance));
        }

        Grip MakeGrip(HandsController by, int hand, int part, Vector3 local)
        {
            var body = Player.body;
            var bone = body.PartBody(part);
            var anchor = new GameObject($"{name} DragAnchor").AddComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.interpolation = RigidbodyInterpolation.Interpolate;
            anchor.position = bone.transform.TransformPoint(local);   // start at the grip: no snap
            anchor.transform.position = anchor.position;

            var joint = bone.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = anchor;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = local;
            joint.connectedAnchor = Vector3.zero;
            joint.enableCollision = false;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;

            // The same pull a hand has on a load (HandsController), scaled so that this ragdoll (lighter than a
            // man, under its own extra gravity) weighs cargoMass to it.
            float weight = body.TotalMass * -Physics.gravity.y * body.gravityMultiplier;
            float scale = weight / (cargoMass * -Physics.gravity.y);
            float spring = 3000f;
            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = 2f * Mathf.Sqrt(spring * body.TotalMass * 0.5f) * 0.9f,
                maximumForce = by.HandForceCap * scale
            };
            joint.xDrive = joint.yDrive = joint.zDrive = drive;
            bone.WakeUp();
            return new Grip { By = by, Hand = hand, Part = part, Local = local, Anchor = anchor, Joint = joint };
        }

        static void DropGrip(Grip g)
        {
            if (g.Joint != null) Destroy(g.Joint);
            if (g.Anchor != null) Destroy(g.Anchor.gameObject);
        }

        void ClearGrips()
        {
            foreach (var g in grips) DropGrip(g);
            grips.Clear();
        }

        // ---- labels over bodies that are out cold -------------------------------------------------------

        void OnGUI()
        {
            if (!IsSpawned || IsOwner || !IsUnconscious || Player.body == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 at = Player.body.Hips.position + Vector3.up * 0.6f;
            if (Vector3.Distance(cam.transform.position, at) > 20f) return;
            Vector3 screen = cam.WorldToScreenPoint(at);
            if (screen.z <= 0f) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
                labelStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            }
            GUI.Label(new Rect(screen.x - 150f, Screen.height - screen.y - 20f, 300f, 40f),
                $"{PlayerNet.NameOf(OwnerClientId)} is out cold — {SecondsLeft} s\nE drag · hold F revive", labelStyle);
        }
    }
}
