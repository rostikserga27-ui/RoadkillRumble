using System;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The physical punch on one active-ragdoll body (on every peer's copy of the player). No clips and no
    /// hitboxes: the fist is the ragdoll's own hand and forearm, moved by forces.
    ///
    ///   Wind-up   the arm is taken off the gait (ActiveRagdollController.SetArmDriven) and a PD force pulls
    ///             the hand back to a guard by the chin, cocked further with charge; the torso twists the
    ///             punching shoulder back and leans back a little.
    ///   Strike    the arm is thrown at the aim point (what is under the crosshair) from wherever the fist
    ///             is, at jab .. haymaker speed, with a share pushed back into the chest as recoil, and held at
    ///             that speed until the arm is straight; the hips and chest snap round and lean in. The fist and forearm switch to continuous collision so they cannot
    ///             tunnel, and the fist's speed relative to the body is clamped so nothing explodes.
    ///   Recovery  a soft PD back to the guard, easing out, then the arm goes back to the gait.
    ///
    /// Contacts of the fist and forearm while the strike is live are reported through FistContact; on the
    /// owner's copy, PlayerFists turns them into hit claims for the server. The first real contact ends the
    /// strike, so one punch does not keep pushing into its target.
    /// </summary>
    [DefaultExecutionOrder(10)]   // after ActiveRagdollController, which sets the arm drives it softens
    public class PunchArmDriver : MonoBehaviour
    {
        public enum Phase { Idle, WindUp, Strike, Recovery }

        public struct Contact
        {
            public int Hand;
            public int Sequence;
            public float Charge;
            public Rigidbody Fist;
            public Vector3 FistVelocity;   // before the contact
            public Collision Collision;
        }

        class Arm
        {
            public int Index;
            public float Side;              // -1 left, +1 right
            public Phase Phase;
            public float Time;              // seconds in this phase
            public float Charge;
            public float StrikeSeconds;
            public Vector3 Aim = Vector3.forward;   // shoulder to aim point, at the start of the strike
            public Vector3 AimPoint;
            public bool Thrown;             // the arm has been thrown (a haymaker, or a punch at a target out of reach, steps in first)
            public float ThrowTime;
            public int Sequence;
            public bool Fixed;              // the charge is the owner's (not guessed from time)
            public Rigidbody Hand, Forearm, Upper;
            public ConfigurableJoint Wrist;
            public Vector3 HandVelocity, ForearmVelocity;   // last step, i.e. before this step's contacts
            public CollisionDetectionMode HandMode, ForearmMode;
        }

        /// <summary>Tests: log every live contact.</summary>
        public static bool LogContacts;

        /// <summary>A fist or forearm touched something while its strike was live.</summary>
        public event Action<Contact> FistContact;

        public ActiveRagdollController Body { get; private set; }
        PunchConfig config;
        readonly Arm[] arms = new Arm[2];
        float lean, twist;

        public void Initialize(ActiveRagdollController body, PunchConfig punchConfig)
        {
            Body = body;
            config = punchConfig;
            for (int i = 0; i < 2; i++)
            {
                var arm = new Arm
                {
                    Index = i,
                    Side = i == 0 ? -1f : 1f,
                    Hand = body.HandBody(i),
                    Forearm = body.LowerArmBody(i),
                    Upper = body.UpperArmBody(i)
                };
                arm.Wrist = arm.Hand.GetComponent<ConfigurableJoint>();
                arm.HandMode = arm.Hand.collisionDetectionMode;
                arm.ForearmMode = arm.Forearm.collisionDetectionMode;
                AddRelay(arm.Hand, i);
                AddRelay(arm.Forearm, i);
                arms[i] = arm;
            }
            IgnoreSelfCollisions();
        }

        void AddRelay(Rigidbody part, int hand)
        {
            var relay = part.gameObject.AddComponent<FistContactRelay>();
            relay.driver = this;
            relay.hand = hand;
        }

        /// <summary>
        /// A hook or a wind-up by the chin would otherwise punch your own head or chest and jitter: the fists
        /// and forearms never touch the torso, the head or the other arm.
        /// </summary>
        void IgnoreSelfCollisions()
        {
            for (int a = 0; a < 2; a++)
            {
                foreach (var fistPart in new[] { arms[a].Hand, arms[a].Forearm })
                for (int i = 0; i < Body.PartCount; i++)
                {
                    var other = Body.PartBody(i);
                    var role = Body.PartRole(i);
                    bool torso = role == ActiveRagdollController.Role.Hips || role == ActiveRagdollController.Role.Spine
                                 || role == ActiveRagdollController.Role.Chest || role == ActiveRagdollController.Role.Head;
                    bool otherArm = (role == ActiveRagdollController.Role.Hand || role == ActiveRagdollController.Role.LowerArm
                                     || role == ActiveRagdollController.Role.UpperArm) && (Body.PartSide(i) < 0f ? 0 : 1) != a;
                    if (!torso && !otherArm) continue;
                    foreach (var mine in Colliders(fistPart))
                    foreach (var theirs in Colliders(other))
                        Physics.IgnoreCollision(mine, theirs, true);
                }
            }
        }

        static Collider[] Colliders(Rigidbody body) =>
            Array.FindAll(body.GetComponentsInChildren<Collider>(), c => c.attachedRigidbody == body);

        /// <summary>The fist's colliders (hand and forearm), for ignoring other players' capsules.</summary>
        public Collider[] FistColliders(int hand)
        {
            var arm = arms[Mathf.Clamp(hand, 0, 1)];
            var hands = Colliders(arm.Hand);
            var forearm = Colliders(arm.Forearm);
            var all = new Collider[hands.Length + forearm.Length];
            hands.CopyTo(all, 0);
            forearm.CopyTo(all, hands.Length);
            return all;
        }

        // ---- commands (every peer) -----------------------------------------------------------------

        public Phase PhaseOf(int hand) => arms[hand].Phase;
        public bool IsBusy(int hand) => arms[hand].Phase != Phase.Idle;
        public int SequenceOf(int hand) => arms[hand].Sequence;
        /// <summary>Arm mass behind a fist: hand, forearm and half the upper arm (kg).</summary>
        public float ArmMass(int hand) => arms[hand].Hand.mass + arms[hand].Forearm.mass + arms[hand].Upper.mass * 0.5f;

        public void BeginWindUp(int hand)
        {
            var arm = arms[hand];
            arm.Phase = Phase.WindUp;
            arm.Time = 0f;
            arm.Charge = 0f;
            arm.Fixed = false;
        }

        /// <summary>Owner only: the exact charge while winding up (others guess it from the time held).</summary>
        public void SetCharge(int hand, float charge)
        {
            arms[hand].Charge = Mathf.Clamp01(charge);
            arms[hand].Fixed = true;
        }

        /// <summary>Throw the punch at a world point (the owner's crosshair point, the same on every peer).</summary>
        public void BeginStrike(int hand, float charge, Vector3 aimPoint, int sequence)
        {
            var arm = arms[hand];
            arm.Phase = Phase.Strike;
            arm.Time = 0f;
            arm.Charge = Mathf.Clamp01(charge);
            arm.AimPoint = aimPoint;
            Vector3 fromShoulder = aimPoint - arm.Upper.position;
            arm.Aim = fromShoulder.sqrMagnitude > 0.0001f ? fromShoulder.normalized : Body.FacingRotation * Vector3.forward;
            arm.Sequence = sequence;
            arm.StrikeSeconds = config.StrikeSeconds(arm.Charge);
            arm.Thrown = false;
            if (!Usable) return;

            Body.SetArmDriven(hand, true);
            SetContinuous(arm, true);
            // A charged punch steps in and turns the hips before the arm goes, and any punch waits (briefly) for
            // the body to step into reach of its target (Drive throws it then).
            if (config.StepIn(arm.Charge) <= 0f && InReach(arm)) Throw(arm);
        }

        /// <summary>
        /// The kick: the whole arm is thrown at the aim point, hand fastest (a hand-only kick would just be shared
        /// out along the arm), from where the fist is now: a fist still low from hanging goes up to it. Momentum
        /// handed to the arm is taken partly back out of the chest, and the hips and chest snap round.
        /// </summary>
        bool InReach(Arm arm) => Vector3.Distance(arm.Upper.position, arm.AimPoint) <= Body.ArmLength(arm.Index) + config.throwReach;

        void Throw(Arm arm)
        {
            arm.Thrown = true;
            arm.ThrowTime = arm.Time;
            float speed = config.StrikeSpeed(arm.Charge);
            Vector3 toPoint = arm.AimPoint - arm.Hand.position;
            Vector3 direction = toPoint.sqrMagnitude > 0.01f ? toPoint.normalized : arm.Aim;
            float along = Vector3.Dot(arm.Hand.linearVelocity - Body.RootVelocity, direction);
            float kick = Mathf.Clamp(speed - along, 0f, speed);
            Vector3 sideways = Vector3.ProjectOnPlane(arm.Hand.linearVelocity - Body.RootVelocity, direction);
            arm.Hand.AddForce(direction * kick - sideways, ForceMode.VelocityChange);
            arm.Forearm.AddForce(direction * (kick * 0.85f), ForceMode.VelocityChange);
            arm.Upper.AddForce(direction * (kick * 0.5f), ForceMode.VelocityChange);
            var chest = Body.Chest;
            if (chest != null)
            {
                float momentum = arm.Hand.mass * kick + arm.Forearm.mass * kick * 0.85f + arm.Upper.mass * kick * 0.5f;
                chest.AddForce(-arm.Aim * (momentum * config.strikeRecoil), ForceMode.Impulse);
                // Hips and chest snap round first, carrying the punching shoulder forward.
                Vector3 snap = Vector3.up * (-arm.Side * config.strikeTwistKick * Mathf.Lerp(0.7f, 1f, arm.Charge));
                chest.AddTorque(snap, ForceMode.VelocityChange);
                Body.Hips.AddTorque(snap * 0.5f, ForceMode.VelocityChange);
            }
        }

        public void Cancel(int hand)
        {
            var arm = arms[hand];
            if (arm.Phase == Phase.Idle) return;
            arm.Phase = Phase.Idle;
            if (Body != null) Body.SetArmDriven(hand, false);
            SetContinuous(arm, false);
        }

        public void CancelAll()
        {
            Cancel(0);
            Cancel(1);
        }

        bool Usable => Body != null && Body.IsSimulated && !Body.IsPuppet && !Body.IsLimp;

        // ---- physics ---------------------------------------------------------------------------------

        void FixedUpdate()
        {
            if (Body == null || arms[0] == null) return;
            float dt = Time.fixedDeltaTime;
            if (!Usable)
            {
                // Knocked down, puppeted or switched off mid-punch: let the arm go.
                CancelAll();
                Body.SetTorsoOffset(0f, 0f);
                lean = twist = 0f;
                return;
            }

            float wantLean = 0f, wantTwist = 0f;
            foreach (var arm in arms)
            {
                arm.HandVelocity = arm.Hand.linearVelocity;
                arm.ForearmVelocity = arm.Forearm.linearVelocity;
                if (arm.Phase == Phase.Idle) continue;
                // A fist, not a limp hand: the wrist is held in line with the forearm (the controller set the
                // drives earlier this step and sets them again next step, so this only lasts while punching).
                if (arm.Wrist != null)
                    arm.Wrist.slerpDrive = new JointDrive { positionSpring = config.wristSpring, positionDamper = config.wristSpring * 0.05f, maximumForce = 2000f };
                arm.Time += dt;
                if (!arm.Fixed && arm.Phase == Phase.WindUp)
                    arm.Charge = Mathf.Clamp01((arm.Time - config.minWindUpSeconds) / Mathf.Max(0.01f, config.chargeSeconds));
                Drive(arm, ref wantLean, ref wantTwist);
            }
            // Ease the torso, so a twist reads as a body turn, not a snap.
            float k = 1f - Mathf.Exp(-18f * dt);
            lean = Mathf.Lerp(lean, wantLean, k);
            twist = Mathf.Lerp(twist, wantTwist, k);
            // Leaning into a punch, the hips step ahead of the capsule too (players' capsules keep them 0.7 m
            // apart, and a short arm needs that body weight going forward to reach a face).
            float step = Mathf.Max(0f, lean) / Mathf.Max(1f, config.strikeLean) * config.strikeStepForward;
            Body.SetTorsoOffset(lean, twist, step);
        }

        void Drive(Arm arm, ref float wantLean, ref float wantTwist)
        {
            Quaternion facing = Body.FacingRotation;
            Vector3 forward = facing * Vector3.forward, right = facing * Vector3.right;
            Vector3 shoulder = arm.Upper.position;
            float length = Body.ArmLength(arm.Index);
            // Guard: fist up by the chin, a little in front and toward the middle.
            Vector3 guard = shoulder + forward * (length * 0.35f) + Vector3.up * 0.06f - right * (arm.Side * 0.1f);

            switch (arm.Phase)
            {
                case Phase.WindUp:
                {
                    Body.SetArmDriven(arm.Index, true);
                    Vector3 cocked = guard - forward * (config.windUpPullBack * arm.Charge) + Vector3.up * (0.05f * arm.Charge);
                    Pull(arm, cocked, config.windUpSpring, config.windUpDamping);
                    wantTwist += arm.Side * config.windUpTwist * (0.4f + 0.6f * arm.Charge);
                    wantLean -= config.windUpLean * arm.Charge;
                    if (arm.Time > config.maxHoldSeconds + 2f) Cancel(arm.Index);   // never told to strike: give up
                    break;
                }
                case Phase.Strike:
                {
                    if (!arm.Thrown)
                    {
                        // Stepping in: the fist stays cocked while the body goes forward and starts to turn.
                        Vector3 cocked = guard - forward * (config.windUpPullBack * arm.Charge);
                        Pull(arm, cocked, config.windUpSpring, config.windUpDamping);
                        wantTwist -= arm.Side * config.strikeTwist * 0.3f;
                        wantLean += config.strikeLean * 0.5f;
                        float stepIn = config.StepIn(arm.Charge);
                        if (arm.Time >= stepIn && (InReach(arm) || arm.Time >= stepIn + config.maxApproachSeconds)) Throw(arm);
                        break;
                    }
                    // Hold the fist at strike speed, homing on the aim point (a PD on velocity, not position, so it
                    // does not slow down as it nears what it is about to hit); past the point it carries on the way
                    // it was going. It is never braked: once the arm is straight the same capped push goes through
                    // the shoulder into the body, which follows the punch through for the rest of the strike, so a
                    // target at the very end of the reach still takes it at speed.
                    Vector3 toPoint = arm.AimPoint - arm.Hand.position;
                    Vector3 direction = Vector3.Dot(toPoint, arm.Aim) > 0.05f ? toPoint.normalized : arm.Aim;
                    Vector3 wanted = Body.RootVelocity + direction * config.StrikeSpeed(arm.Charge);
                    Accelerate(arm, (wanted - arm.Hand.linearVelocity) * config.strikeVelocityGain);
                    ClampFistSpeed(arm);
                    wantTwist -= arm.Side * config.strikeTwist * (0.6f + 0.4f * arm.Charge);
                    wantLean += config.strikeLean * (0.5f + 0.5f * arm.Charge);
                    if (arm.Time - arm.ThrowTime >= arm.StrikeSeconds) EnterRecovery(arm);
                    break;
                }
                case Phase.Recovery:
                {
                    float left = 1f - Mathf.Clamp01(arm.Time / Mathf.Max(0.01f, config.recoverySeconds));
                    Pull(arm, guard, config.recoverySpring * left, config.recoveryDamping);
                    wantTwist -= arm.Side * config.strikeTwist * 0.6f * left;
                    wantLean += config.strikeLean * 0.5f * left;
                    if (left <= 0f)
                    {
                        arm.Phase = Phase.Idle;
                        Body.SetArmDriven(arm.Index, false);
                    }
                    break;
                }
            }
        }

        void EnterRecovery(Arm arm)
        {
            if (arm.Phase != Phase.Strike) return;
            arm.Phase = Phase.Recovery;
            arm.Time = 0f;
            SetContinuous(arm, false);
        }

        /// <summary>PD pull of the hand toward a point.</summary>
        void Pull(Arm arm, Vector3 target, float spring, float damping)
        {
            Accelerate(arm, (target - arm.Hand.position) * spring + (Body.RootVelocity - arm.Hand.linearVelocity) * damping);
        }

        /// <summary>
        /// Accelerate the arm, capped: the hand, forearm and (half) the upper arm each get their share, so the
        /// chain moves as one instead of the light hand being yanked ahead and bouncing on its joints.
        /// </summary>
        void Accelerate(Arm arm, Vector3 acceleration)
        {
            acceleration = Vector3.ClampMagnitude(acceleration, config.maxArmAcceleration);
            arm.Hand.AddForce(acceleration, ForceMode.Acceleration);
            arm.Forearm.AddForce(acceleration, ForceMode.Acceleration);
            arm.Upper.AddForce(acceleration * 0.5f, ForceMode.Acceleration);
        }

        void ClampFistSpeed(Arm arm)
        {
            Vector3 relative = arm.Hand.linearVelocity - Body.RootVelocity;
            if (relative.sqrMagnitude <= config.maxFistSpeed * config.maxFistSpeed) return;
            arm.Hand.linearVelocity = Body.RootVelocity + relative.normalized * config.maxFistSpeed;
        }

        /// <summary>Fast fists use full continuous collision (no tunnelling); speculative the rest of the time (no ghost contacts).</summary>
        static void SetContinuous(Arm arm, bool on)
        {
            if (arm.Hand == null || arm.Hand.isKinematic) return;
            arm.Hand.collisionDetectionMode = on ? CollisionDetectionMode.ContinuousDynamic : arm.HandMode;
            arm.Forearm.collisionDetectionMode = on ? CollisionDetectionMode.ContinuousDynamic : arm.ForearmMode;
        }

        // ---- contacts --------------------------------------------------------------------------------

        internal void OnFistCollision(int hand, Rigidbody part, Collision collision)
        {
            var arm = arms[hand];
            if (arm == null || arm.Phase != Phase.Strike || !Usable) return;
            var other = collision.rigidbody;
            if (other != null && Body.OwnsBody(other)) return;
            if (collision.collider == Body.RootCollider) return;

            var contact = new Contact
            {
                Hand = hand,
                Sequence = arm.Sequence,
                Charge = arm.Charge,
                Fist = part,
                FistVelocity = part == arm.Hand ? arm.HandVelocity : arm.ForearmVelocity,
                Collision = collision
            };
            if (LogContacts && collision.contactCount > 0)
                Debug.Log($"RK-PUNCH   contact {part.name} -> {collision.collider.name} ({collision.collider.attachedRigidbody?.name}), " +
                          $"normal speed {Mathf.Abs(Vector3.Dot(contact.FistVelocity, collision.GetContact(0).normal)):0.0} m/s at {collision.GetContact(0).point:F2}, hand {part.position:F2}, root {Body.root.position:F2}");
            FistContact?.Invoke(contact);
            // The punch landed: stop driving into the target (a brush along a wall does not count).
            if (collision.contactCount > 0)
            {
                Vector3 normal = collision.GetContact(0).normal;
                if (Mathf.Abs(Vector3.Dot(contact.FistVelocity, normal)) >= config.minImpactSpeed) EnterRecovery(arm);
            }
        }
    }

    /// <summary>Sits on a fist or forearm bone and passes its contacts to the PunchArmDriver.</summary>
    public class FistContactRelay : MonoBehaviour
    {
        public PunchArmDriver driver;
        public int hand;
        Rigidbody body;

        void Awake() => body = GetComponent<Rigidbody>();

        void OnCollisionEnter(Collision collision) => Forward(collision);
        // Point blank: the fist may already touch the target when the strike starts.
        void OnCollisionStay(Collision collision) => Forward(collision);

        void Forward(Collision collision)
        {
            if (driver != null) driver.OnFistCollision(hand, body, collision);
        }
    }
}
