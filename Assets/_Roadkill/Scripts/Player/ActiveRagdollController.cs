using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Active ragdoll for the visible character (GDD section 3). Every bone is a physical body on a
    /// ConfigurableJoint; the character "animates" by steering those joints toward a procedural target
    /// pose (shuffling gait, loose arm swing, airborne flailing, idle wobble) while a balance force keeps
    /// the hips near the player's capsule, stacking forces keep the torso over the hips and foot
    /// placement keeps the flip-flops under him.
    ///
    /// Gameplay physics stays on the capsule (PlayerMotor, networked by the owner). These forces only
    /// ever act on the ragdoll, so the floppy body can lag, stumble and flail without moving the real
    /// player. Because the capsule moves (and interpolates) every frame, the ragdoll detaches from it at
    /// Start and follows by force alone; it destroys itself when the capsule goes away.
    ///
    /// The gait follows the direction of travel relative to the facing (strafing side-steps, walking
    /// backwards reverses the swing). Hands reach for whatever the player holds, and a throw winds up
    /// (lean back, arms up) and flings forward.
    ///
    /// While the local player is down, PlayerMotor hands the lead to the body (BodyLeads): the limp body
    /// lies where it falls and the capsule follows its hips, so it never rolls like a capsule.
    ///
    /// Active and limp ("Ragdoll Mode") blend over modeBlendSeconds. Limp comes from SetLimp, from the
    /// player being ragdolled (PlayerMotor locally, PlayerNet for remote copies) or from a hard knock.
    ///
    /// Punches (PunchArmDriver) take an arm over with SetArmDriven and twist and lean the torso with
    /// SetTorsoOffset; a punched body staggers (Stagger) with its joints softened for a moment.
    /// Built by the prefab generator (FishermanRagdollBuilder); values below are the tuned defaults.
    /// </summary>
    public class ActiveRagdollController : MonoBehaviour
    {
        public enum Role { Hips, Spine, Chest, Head, UpperArm, LowerArm, Hand, UpperLeg, LowerLeg, Foot }

        [System.Serializable]
        public class Part
        {
            public Role role;
            public Rigidbody body;
            public ConfigurableJoint joint;   // none on the hips
            [Tooltip("-1 the character's left, +1 right, 0 centre.")]
            public float side;
            [Tooltip("Share of Joint Spring / Joint Damper this joint gets.")]
            public float strength = 1f;

            [System.NonSerialized] public Quaternion startLocal;      // rest rotation relative to the connected body
            [System.NonSerialized] public Quaternion jointFrame;
            [System.NonSerialized] public Vector3 right, forward, up; // character axes in the connected body's frame
            [System.NonSerialized] public Quaternion restRotation;    // relative to the root
            [System.NonSerialized] public Vector3 restPosition;       // relative to the root
            [System.NonSerialized] public Vector3 armRestDirection;   // upper arms: shoulder to wrist at rest, in the parent's frame
            [System.NonSerialized] public float seed;
        }

        [Header("Links")]
        [Tooltip("What the body follows: the player's capsule.")]
        public Transform root;
        [Tooltip("The capsule's rigidbody, for its velocity (optional).")]
        public Rigidbody rootBody;
        [Tooltip("Local player only: ragdoll state, ground check and Launch go through the real PlayerMotor.")]
        public PlayerMotor motor;
        public Part[] parts;
        [Tooltip("Hips height above the root in the rest pose (metres).")]
        public float hipHeight = 0.75f;

        [Header("Tuning")]
        [Range(0f, 1f)] public float balanceStrength = 0.8f;
        [Range(0f, 2000f)] public float jointSpring = 260f;
        [Range(0f, 100f)] public float jointDamper = 9f;
        [Range(0f, 5000f)] public float maxForce = 1500f;
        [Range(0f, 1f)] public float wobbleAmount = 0.45f;
        [Range(0f, 1f)] public float headFloppiness = 0.6f;
        [Range(0.5f, 3f)] public float gravityMultiplier = 1.3f;

        [Header("Balance")]
        public float hipSpring = 90f;         // 1/s^2: how hard the hips chase their target
        public float hipDamping = 17f;        // 1/s: close to critical, so quick direction changes do not fling the hips
        public float maxHipAcceleration = 45f;
        public float stackSpring = 220f;      // 1/s^2: spine, chest and head pulled back over the hips
        public float stackDamping = 24f;
        public float maxStackAcceleration = 90f;
        [Range(0f, 1f), Tooltip("Share of the hips' acceleration the upper body gets up front, so quick direction changes do not leave the torso behind.")]
        public float stackFeedForward = 0.9f;
        public float uprightSpring = 120f;    // facing torque on hips and chest
        public float uprightDamping = 22f;    // critical for the spring: rights itself without wobbling past

        [Header("Stability")]
        [Tooltip("Extra damping of spin about the vertical on the hips, spine and chest (1/s): facing comes from the player's aim, not from being hit.")]
        public float yawDamping = 18f;
        [Tooltip("And how hard they are turned back to where the player faces (1/s^2).")]
        public float yawSpring = 200f;
        [Tooltip("No bone spins faster than this (rad/s).")]
        public float maxPartAngularVelocity = 14f;
        [Tooltip("Standing, no bone moves faster than this relative to the capsule (m/s); a knockdown goes limp first, so it can still fly.")]
        public float maxPartSpeed = 9f;
        [Tooltip("However hard a hit, balance stays at least this strong while staggering (0..1): a wobble, not a noodle.")]
        [Range(0f, 1f)] public float minStaggerBalance = 0.8f;

        [Header("Gait")]
        public float strideLength = 1.6f;     // metres per full cycle: short, the flip-flops shuffle
        public float maxCadence = 3.2f;       // cycles per second at most, so sprinting doesn't blur the legs
        public float fullStrideSpeed = 3.5f;
        public float legSwing = 36f;
        public float kneeBend = 55f;
        public float armSwing = 45f;
        public float armHang = 32f;           // arms drop from the A-pose toward the body
        public float elbowBend = 22f;
        public float lean = 4f;               // degrees forward at full stride, plus acceleration lean
        public float crouchLean = 18f;        // degrees forward while crouching
        public float crouchThigh = 50f;       // thighs forward, knees and ankles fold to match
        public float flail = 40f;             // limb thrash while airborne
        public float sink = 0.03f;            // hips held a little low so the legs carry weight: springy knees

        [Header("Feet")]
        public float footSpring = 140f;       // 1/s^2: each foot pulled to its gait spot on the ground under the hips
        public float footDamping = 16f;
        public float maxFootAcceleration = 90f;
        public float stepLength = 0.3f;       // half a stride at full speed (metres): short, he shuffles
        public float stepHeight = 0.1f;       // how high the swinging foot lifts
        public float stanceWidth = 0.11f;

        [Header("Hands")]
        public float reachSpring = 150f;      // 1/s^2: a hand pulled onto what it holds
        public float reachDamping = 16f;
        public float maxReachAcceleration = 120f;
        public float throwWindUp = 16f;       // degrees he leans back at full charge
        public float throwLunge = 20f;        // degrees he lunges forward on the throw
        public float throwSwingSeconds = 0.35f;
        [Tooltip("Degrees he leans toward a held object per metre it is beyond his reach (the game holds loads out in front).")]
        public float carryLeanPerMetre = 45f;
        public float maxCarryLean = 20f;

        [Header("Arms out (How To Fish)")]
        [Tooltip("Empty hands are held out in front of the chest, toward where the player looks, instead of hanging and swinging.")]
        public bool armsOut = true;
        [Tooltip("The upper arms point this far below horizontal (degrees): mostly down, a little forward.")]
        public float armsOutDrop = 72f;
        [Tooltip("And this far out from straight ahead (degrees): about shoulder width.")]
        public float armsOutSpread = 12f;
        [Tooltip("Elbows bent this much (degrees): fists up in front of the chest, a scrapper's guard (How To Fish).")]
        public float armsOutElbow = 115f;
        [Tooltip("Arm joints this much stiffer while held out, so they do not sag under their own weight.")]
        public float armsOutStiffness = 5f;
        [Tooltip("Looking up or down, the arms follow at most this far (degrees).")]
        public float armsOutMaxPitch = 30f;
        [Tooltip("Share of the held-out arms' weight taken off them, so the joints hold the pose without sagging or tipping him back.")]
        [Range(0f, 1f)] public float armsOutWeightless = 0.85f;
        [Range(0f, 1f), Tooltip("Side-steps are this much shorter than forward steps, so the feet do not cross.")]
        public float sideStepScale = 0.3f;
        [Range(0f, 1f), Tooltip("How much he leans into sideways movement and acceleration (forward lean is full).")]
        public float sideLean = 0.4f;

        [Header("Modes")]
        public float modeBlendSeconds = 0.3f;
        [Tooltip("Off on the network: there the server decides knockdowns and hits only stagger the body.")]
        public bool knockoutOnImpact = true;
        public float knockoutVelocity = 9f;
        public float knockoutSeconds = 2.5f;
        public float staggerVelocity = 4f;
        [Tooltip("While limp the hips may drift this far from the capsule before being pulled back (metres).")]
        public float limpLeash = 0.45f;
        public bool detachFromRoot = true;

        public bool IsLimp => manualLimp || rootLimp || knockoutTimer > 0f;
        public bool IsKnockedOut => knockoutTimer > 0f;
        public bool IsGrounded { get; private set; }
        /// <summary>Last hit above staggerVelocity, for tuning: "7.2 m/s Head vs Crate".</summary>
        public string LastImpact { get; private set; } = "";
        /// <summary>Torso within 30 degrees of vertical and hips near standing height.</summary>
        public bool IsUpright => chest != null && Vector3.Angle(chest.body.rotation * Quaternion.Inverse(chest.restRotation) * Vector3.up, Vector3.up) < 30f
                                 && Mathf.Abs(hips.body.position.y - root.position.y - hipHeight) < 0.15f;
        public float ActiveWeight => weight;
        /// <summary>Set by PlayerMotor while the player is down: the body lies free and the capsule follows its hips.</summary>
        public bool BodyLeads { get; set; }
        /// <summary>Right after a throw, while the arms fling forward.</summary>
        public bool IsThrowing => throwSwingTimer > 0f;
        /// <summary>Shoulder to palm at full stretch (metres).</summary>
        public float ReachLength(int hand) => Vector3.Distance(upperArms[hand].restPosition, hands[hand].restPosition) + 0.08f;
        /// <summary>Direction of travel in the character's frame (x = right, z = forward), unit length.</summary>
        public Vector3 MoveDirection => move;
        public float Speed => speed;
        public Rigidbody Hips => hips.body;
        /// <summary>The networked player this body belongs to, if any (hits on the body count as hits on them).</summary>
        public PlayerNet Player { get; private set; }

        Part hips, chest, head;
        readonly Part[] upperArms = new Part[2], lowerArms = new Part[2], hands = new Part[2];   // 0 left, 1 right
        readonly bool[] reaching = new bool[2];
        readonly Vector3[] reachTargets = new Vector3[2];
        readonly float[] reachWeight = new float[2];
        float throwCharge, throwSwingTimer;
        Vector3 move = Vector3.forward;
        float armMass;
        float phaseRate;
        Collider rootCollider;
        readonly HashSet<Rigidbody> ownBodies = new HashSet<Rigidbody>();
        readonly RaycastHit[] groundHits = new RaycastHit[8];
        float totalMass;
        float weight = 1f;
        bool manualLimp, rootLimp, simulated = true;
        float knockoutTimer, staggerTimer;
        const float DefaultStaggerStiffness = 0.45f;
        float staggerStiffness = DefaultStaggerStiffness;   // joint strength while staggered
        readonly bool[] armDriven = new bool[2];             // a punch has this arm
        float extraLean, extraTwist;                         // degrees, from a punch
        float extraStep;                                     // metres the hips go ahead of the capsule, from a punch
        Vector3 lastRootPosition, rootVelocity, rootAcceleration;
        float speed, phase, air;
        Quaternion look;
        bool hasLook;
        bool remoteCrouch;
        float crouch;          // 0..1, eased
        bool puppet;           // remote copy replaying the owner's limp pose (RagdollPoseSync)

        void Awake()
        {
            if (root == null) root = transform.parent != null ? transform.parent : transform;
            rootCollider = root.GetComponent<Collider>();
            Player = root.GetComponentInParent<PlayerNet>();
            Quaternion rootInverse = Quaternion.Inverse(root.rotation);

            foreach (var p in parts)
            {
                if (p.role == Role.Hips) hips = p;
                if (p.role == Role.Chest) chest = p;
                if (p.role == Role.Head) head = p;
                if (p.role == Role.UpperArm || p.role == Role.LowerArm || p.role == Role.Hand) armMass += p.body.mass;
                int sideIndex = p.side < 0f ? 0 : 1;
                if (p.role == Role.UpperArm) upperArms[sideIndex] = p;
                if (p.role == Role.LowerArm) lowerArms[sideIndex] = p;
                if (p.role == Role.Hand) hands[sideIndex] = p;
                ownBodies.Add(p.body);
                totalMass += p.body.mass;
                p.body.maxAngularVelocity = maxPartAngularVelocity;
                p.body.solverIterations = 20;
                p.body.solverVelocityIterations = 10;
                p.restRotation = rootInverse * p.body.rotation;
                p.restPosition = rootInverse * (p.body.position - root.position);
                p.seed = Random.value * 100f;

                foreach (var c in p.body.GetComponentsInChildren<Collider>())
                {
                    if (c.attachedRigidbody == p.body && rootCollider != null) Physics.IgnoreCollision(rootCollider, c, true);
                }

                if (p.joint == null) continue;
                Quaternion parentInverse = Quaternion.Inverse(p.joint.connectedBody.rotation);
                p.startLocal = parentInverse * p.body.rotation;
                Vector3 axis = p.joint.axis.normalized;
                Vector3 jointForward = Vector3.Cross(p.joint.axis, p.joint.secondaryAxis).normalized;
                p.jointFrame = Quaternion.LookRotation(jointForward, Vector3.Cross(jointForward, axis).normalized);
                p.right = parentInverse * root.right;
                p.forward = parentInverse * root.forward;
                p.up = parentInverse * root.up;
            }
            IgnoreNeighbourCollisions();
            foreach (var p in parts)
            {
                if (p.role != Role.UpperArm || p.joint == null) continue;
                var hand = hands[p.side < 0f ? 0 : 1];
                if (hand != null)
                    p.armRestDirection = (Quaternion.Inverse(p.joint.connectedBody.rotation) * (hand.body.position - p.body.position)).normalized;
            }
            lastRootPosition = root.position;
        }

        void Start()
        {
            // A moving, interpolated parent would drag the bodies around outside the physics step.
            if (simulated && detachFromRoot && transform.parent != null) transform.SetParent(null, true);
        }

        /// <summary>Bones next to each other in the skeleton (parent, grandparent, siblings) never collide.</summary>
        void IgnoreNeighbourCollisions()
        {
            Rigidbody Parent(Rigidbody b)
            {
                foreach (var p in parts) if (p.body == b && p.joint != null) return p.joint.connectedBody;
                return null;
            }
            foreach (var a in parts)
            foreach (var b in parts)
            {
                if (a == b) continue;
                Rigidbody pa = Parent(a.body), pb = Parent(b.body);
                bool near = pa == b.body || pb == a.body || (pa != null && pa == pb) || Parent(pa) == b.body || Parent(pb) == a.body;
                if (!near) continue;
                foreach (var ca in a.body.GetComponentsInChildren<Collider>())
                foreach (var cb in b.body.GetComponentsInChildren<Collider>())
                    if (ca.attachedRigidbody == a.body && cb.attachedRigidbody == b.body) Physics.IgnoreCollision(ca, cb, true);
            }
        }

        void LateUpdate()
        {
            if (root == null) Destroy(gameObject);   // the player despawned; the detached body goes too
        }

        void FixedUpdate()
        {
            if (!simulated || root == null) return;
            float dt = Time.fixedDeltaTime;
            if (motor != null && motor.enabled)
            {
                rootLimp = motor.IsRagdolled;
                if (motor.cameraPivot != null) { look = motor.cameraPivot.rotation; hasLook = true; }
            }

            knockoutTimer = Mathf.Max(0f, knockoutTimer - dt);
            if (puppet)
            {
                // Bones are placed by RagdollPoseSync; stay limp so getting up blends in as usual.
                weight = 0f;
                lastRootPosition = root.position;
                return;
            }
            bool crouching = motor != null && motor.enabled ? motor.IsCrouching : remoteCrouch;
            crouch = Mathf.MoveTowards(crouch, crouching && !IsLimp ? 1f : 0f, dt * 5f);
            staggerTimer = Mathf.Max(0f, staggerTimer - dt);
            if (staggerTimer <= 0f) staggerStiffness = DefaultStaggerStiffness;
            throwSwingTimer = Mathf.Max(0f, throwSwingTimer - dt);
            for (int i = 0; i < 2; i++)
                reachWeight[i] = armDriven[i] ? 1f : Mathf.MoveTowards(reachWeight[i], reaching[i] || throwSwingTimer > 0f ? 1f : 0f, dt * 6f);
            weight = Mathf.MoveTowards(weight, IsLimp ? 0f : 1f, dt / Mathf.Max(0.01f, modeBlendSeconds));
            TrackRoot(dt);

            if ((hips.body.position - (root.position + Vector3.up * hipHeight)).sqrMagnitude > 16f) SnapToRoot();

            foreach (var p in parts)
            {
                float g = gravityMultiplier - 1f;
                // Arms held out are mostly weightless while he stands (they drop again when he goes limp).
                if (armsOut && (p.role == Role.UpperArm || p.role == Role.LowerArm || p.role == Role.Hand))
                    g -= gravityMultiplier * armsOutWeightless * weight;
                p.body.AddForce(Physics.gravity * g, ForceMode.Acceleration);
            }

            UpdateDrives();
            if (weight > 0f)
            {
                Balance();
                Pose();
                Step();
                Reach();
            }
            if (weight < 1f && !BodyLeads) Leash();
        }

        void TrackRoot(float dt)
        {
            Vector3 position = root.position;
            // The capsule jumped (respawn, gathered, a network teleport): jump with it rather than be dragged.
            if ((position - lastRootPosition).sqrMagnitude > TeleportStep * TeleportStep)
            {
                SnapToRoot();
                position = root.position;
            }
            bool dynamicRoot = rootBody != null && !rootBody.isKinematic;
            Vector3 velocity = dynamicRoot ? rootBody.linearVelocity : (position - lastRootPosition) / dt;
            lastRootPosition = position;
            // Remote copies move by interpolated network updates: smooth what we read from them.
            Vector3 smoothed = Vector3.Lerp(rootVelocity, velocity, dynamicRoot ? 1f : 0.3f);
            rootAcceleration = Vector3.Lerp(rootAcceleration, (smoothed - rootVelocity) / dt, 0.15f);
            rootVelocity = smoothed;

            IsGrounded = motor != null && motor.enabled ? motor.IsGrounded : GroundBelow(position);
            air = Mathf.MoveTowards(air, IsGrounded ? 0f : 1f, dt * 6f);
            float flat = new Vector2(rootVelocity.x, rootVelocity.z).magnitude;
            speed = Mathf.Lerp(speed, flat, 1f - Mathf.Exp(-8f * dt));
            if (flat > 0.3f)
            {
                // Travel direction in the character's own frame, turned (on the ground plane) at a limited rate,
                // so a quick turn or an A/D reversal does not flip the legs.
                Vector3 local = Quaternion.Inverse(Facing()) * new Vector3(rootVelocity.x, 0f, rootVelocity.z);
                float angle = Mathf.MoveTowardsAngle(Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg,
                    Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, 720f * dt);
                move = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));
            }
            phaseRate = Mathf.Min(speed / strideLength, maxCadence) * Mathf.PI * 2f;
            if (IsGrounded) phase += phaseRate * dt;
        }

        bool GroundBelow(Vector3 position)
        {
            int n = Physics.RaycastNonAlloc(position + Vector3.up * 0.25f, Vector3.down, groundHits, 0.45f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = groundHits[i].collider;
                if (c == rootCollider || (c.attachedRigidbody != null && ownBodies.Contains(c.attachedRigidbody))) continue;
                return true;
            }
            return false;
        }

        void UpdateDrives()
        {
            float soft = Mathf.Lerp(1f, 0.5f, air) * (staggerTimer > 0f ? staggerStiffness : 1f);
            foreach (var p in parts)
            {
                if (p.joint == null) continue;
                float k = p.strength * weight * soft * PlaygroundRules.JointScale;
                float kd = 1f;   // damping to match a stiffer spring
                if (p.role == Role.Head) k *= Mathf.Lerp(1f, 0.2f, headFloppiness);
                if (p.role == Role.UpperArm || p.role == Role.LowerArm || p.role == Role.Hand)
                {
                    float reach = reachWeight[p.side < 0f ? 0 : 1];
                    if (armsOut)
                    {
                        k *= Mathf.Lerp(armsOutStiffness, 1f, reach);   // held out against gravity, wrists in line
                        kd = Mathf.Lerp(Mathf.Sqrt(armsOutStiffness) * 1.5f, 1f, reach);   // and damped to match, so they do not bob
                    }
                    k *= Mathf.Lerp(1f, 0.25f, reach);   // let the reach pull the arm
                }
                p.joint.slerpDrive = new JointDrive
                {
                    positionSpring = jointSpring * k,
                    positionDamper = jointDamper * p.strength * kd * Mathf.Lerp(0.15f, 1f, weight),
                    maximumForce = maxForce * p.strength
                };
            }
        }

        /// <summary>Hips chase the capsule; hips and chest are righted toward the capsule's facing.</summary>
        void Balance()
        {
            float s = weight * balanceStrength * PlaygroundRules.BalanceScale * (staggerTimer > 0f ? Mathf.Max(minStaggerBalance, staggerStiffness + 0.05f) : 1f);
            float walk = Mathf.Clamp01(speed / fullStrideSpeed);
            float t = Time.time;

            float bob = IsGrounded ? Mathf.Abs(Mathf.Sin(phase)) * 0.035f * walk : 0f;
            float height = hipHeight - (IsGrounded ? sink * (1f - walk) : 0f) + bob;
            height *= Mathf.Lerp(1f, 0.72f, crouch);
            Vector3 sway = new Vector3(Mathf.PerlinNoise(t * 0.6f, 3.1f) - 0.5f, 0f, Mathf.PerlinNoise(7.7f, t * 0.6f) - 0.5f) * (0.06f * wobbleAmount);
            Vector3 target = root.position + Vector3.up * height + root.rotation * sway + Facing() * Vector3.forward * extraStep;

            var body = hips.body;
            Vector3 acceleration = (target - body.position) * hipSpring + (rootVelocity - body.linearVelocity) * hipDamping;
            acceleration = Vector3.ClampMagnitude(acceleration, maxHipAcceleration);
            Vector3 lift = -Physics.gravity * gravityMultiplier;   // carries the whole body
            // Leading (going down), the capsule trails the hips: chasing it would brake a fall or a flight.
            if (BodyLeads) acceleration = Vector3.zero;
            else body.AddForce((acceleration + lift) * (totalMass * s), ForceMode.Force);

            // Lean into the direction of travel (and of acceleration), back while winding up a throw,
            // forward as it lets go. A punch adds its own lean and twists the torso about the vertical.
            Quaternion facing = Facing();
            Vector3 accel = Quaternion.Inverse(facing) * rootAcceleration;
            float leanForward = lean * walk * move.z + Mathf.Clamp(accel.z, -6f, 10f) + crouchLean * crouch
                                - throwWindUp * throwCharge + (throwSwingTimer > 0f ? throwLunge : 0f) + extraLean;
            leanForward += CarryLean(facing * Vector3.forward);
            float leanSide = (lean * walk * move.x + Mathf.Clamp(accel.x, -6f, 6f)) * sideLean;
            Quaternion upright = facing * Quaternion.AngleAxis(extraTwist, Vector3.up)
                                 * Quaternion.AngleAxis(leanForward, Vector3.right) * Quaternion.AngleAxis(-leanSide, Vector3.forward);

            // Stack the upper body over the hips: each torso part is pulled toward where it sits above the
            // hips in the rest pose (turned to the facing and lean). Forces through the joints right the
            // whole torso, however far it has tipped; a torque on one light body could not.
            foreach (var p in parts)
            {
                if (p.role != Role.Spine && p.role != Role.Chest && p.role != Role.Head) continue;
                Vector3 wanted = hips.body.position + upright * (p.restPosition - hips.restPosition);
                Vector3 pull = (wanted - p.body.position) * stackSpring + (hips.body.linearVelocity - p.body.linearVelocity) * stackDamping
                               + acceleration * stackFeedForward;   // move with the hips, not after them
                pull = Vector3.ClampMagnitude(pull, maxStackAcceleration);
                float mass = p.body.mass + (p.role == Role.Chest ? armMass : 0f);   // the chest carries the arms
                float share = p.role == Role.Head ? 1f - 0.75f * headFloppiness : 1f;
                p.body.AddForce(pull * (mass * s * share), ForceMode.Force);
            }
            // The hips face where the player aims; only the chest takes a punch's twist.
            Quaternion uprightHips = facing * Quaternion.AngleAxis(leanForward, Vector3.right) * Quaternion.AngleAxis(-leanSide, Vector3.forward);
            Right(hips, uprightHips, uprightSpring, uprightDamping, s);
            Right(chest, upright, uprightSpring * 0.6f, uprightDamping * 0.8f, s);

            // Whatever hits him, he does not spin like a top: damp the torso's turn about the vertical, and
            // keep every bone within a sane speed of the capsule while he is on his feet.
            foreach (var p in parts)
            {
                if (p.role == Role.Hips || p.role == Role.Spine || p.role == Role.Chest)
                {
                    // Facing on the ground plane: the hips (and spine) to the aim, the chest to the aim plus a punch's twist.
                    Quaternion wantedFacing = p.role == Role.Chest ? facing * Quaternion.AngleAxis(extraTwist, Vector3.up) : facing;
                    Vector3 have = Vector3.ProjectOnPlane(p.body.rotation * Quaternion.Inverse(p.restRotation) * Vector3.forward, Vector3.up);
                    float error = have.sqrMagnitude > 0.01f ? Vector3.SignedAngle(have, wantedFacing * Vector3.forward, Vector3.up) * Mathf.Deg2Rad : 0f;
                    p.body.AddTorque(Vector3.up * ((error * yawSpring - p.body.angularVelocity.y * yawDamping) * s), ForceMode.Acceleration);
                }
                if (weight > 0.9f && !BodyLeads)
                {
                    Vector3 relative = p.body.linearVelocity - rootVelocity;
                    if (relative.sqrMagnitude > maxPartSpeed * maxPartSpeed)
                        p.body.linearVelocity = rootVelocity + relative.normalized * maxPartSpeed;
                }
            }
        }

        static void Right(Part p, Quaternion characterRotation, float spring, float damping, float strength)
        {
            Quaternion delta = characterRotation * p.restRotation * Quaternion.Inverse(p.body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) axis = Vector3.zero;
            Vector3 acceleration = axis * (angle * Mathf.Deg2Rad * spring) - p.body.angularVelocity * damping;
            p.body.AddTorque(Vector3.ClampMagnitude(acceleration, 300f) * strength, ForceMode.Acceleration);
        }

        /// <summary>Joint targets: shuffle, swing, flail and wobble. "Animation" without clips.</summary>
        void Pose()
        {
            float walk = Mathf.Clamp01(speed / fullStrideSpeed);
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float t = Time.time;
            float idle = 1f - walk * 0.5f;
            float sideways = Mathf.Abs(move.x);

            foreach (var p in parts)
            {
                if (p.joint == null) continue;
                float legSign = p.side < 0f ? 1f : -1f;   // left leg leads with +sin, the right with -sin
                // The thigh swings forward while its angle falls: that is the leg's swing phase (knee folds, foot lifts).
                float swing = Mathf.Max(0f, -legSign * c);
                float thrash = Mathf.Sin(t * 13f + p.seed) * flail * air;
                float wob = wobbleAmount * PlaygroundRules.WobbleScale * 6f * idle;
                float nx = (Mathf.PerlinNoise(t * 0.8f, p.seed) - 0.5f) * 2f * wob;
                float nz = (Mathf.PerlinNoise(p.seed, t * 0.8f) - 0.5f) * 2f * wob;
                // Positive angles about the character's right swing a limb backward. The stride swings about
                // the axis across the direction of travel instead: right when walking forward, the forward
                // axis when side-stepping, reversed when walking backwards.
                float aboutRight = nx, aboutForward = nz, aboutUp = 0f, stride = 0f;
                Vector3 strideAxis = p.right * move.z - p.forward * move.x;

                switch (p.role)
                {
                    case Role.Spine:
                    case Role.Chest:
                        aboutRight += Mathf.Sin(t * 2.2f) * 1.5f * (1f - walk);
                        aboutForward += Mathf.Sin(phase) * 3f * walk * (1f - sideways);   // no hip-roll on a side-step
                        break;
                    case Role.Head:
                        if (hasLook)
                        {
                            Quaternion wanted = look * p.restRotation;
                            Quaternion local = Quaternion.Inverse(p.joint.connectedBody.rotation) * wanted;
                            Quaternion noise = Quaternion.AngleAxis(nx * (1f + 3f * headFloppiness), p.right) * Quaternion.AngleAxis(nz * (1f + 3f * headFloppiness), p.forward);
                            SetTarget(p, noise * local);
                            continue;
                        }
                        aboutRight *= 1f + 3f * headFloppiness;
                        aboutForward *= 1f + 3f * headFloppiness;
                        break;
                    case Role.UpperLeg:
                        stride = legSign * s * legSwing * walk * Mathf.Lerp(1f, sideStepScale, sideways);
                        aboutRight += thrash - crouchThigh * crouch;                          // crouch: thighs forward
                        break;
                    case Role.LowerLeg:
                        aboutRight += swing * kneeBend * walk + 6f + air * (25f + Mathf.Abs(thrash) * 0.6f)
                                      + crouchThigh * 1.8f * crouch;                           // knees fold
                        break;
                    case Role.Foot:
                        aboutRight -= swing * 12f * walk + crouchThigh * 0.8f * crouch;       // feet stay flat
                        break;
                    case Role.UpperArm:
                        if (armsOut && p.armRestDirection != Vector3.zero)
                        {
                            // Held out in front (How To Fish): turn the arm from its rest direction to point ahead,
                            // a little down and out, following where the player looks; a small wobble on top.
                            Vector3 wanted = ArmsOutDirection(p.side, p.right, p.up, p.forward);
                            Quaternion held = Quaternion.FromToRotation(p.armRestDirection, wanted);
                            Quaternion jiggle = Quaternion.AngleAxis(nx * 0.5f + thrash * 0.3f, p.right) * Quaternion.AngleAxis(nz * 0.5f, p.up);
                            SetTarget(p, jiggle * held * p.startLocal);
                            continue;
                        }
                        stride = -legSign * s * armSwing * walk * Mathf.Lerp(1f, 0.3f, sideways);   // arms barely swing on a side-step
                        aboutRight += thrash;
                        aboutForward += -p.side * (armHang - air * 75f);          // hang by the sides, fly up when airborne
                        break;
                    case Role.LowerArm:
                        float bend = armsOut ? armsOutElbow : elbowBend + Mathf.Max(0f, -legSign * s) * elbowBend * walk + Mathf.Abs(thrash) * 0.5f;
                        aboutRight -= Mathf.Lerp(bend, 8f, reachWeight[p.side < 0f ? 0 : 1]);   // reaching: arm almost straight
                        break;
                    case Role.Hand:
                        aboutRight *= 2f;
                        break;
                }
                Quaternion offset = Quaternion.AngleAxis(stride, strideAxis) * Quaternion.AngleAxis(aboutUp, p.up)
                                    * Quaternion.AngleAxis(aboutForward, p.forward) * Quaternion.AngleAxis(aboutRight, p.right);
                SetTarget(p, offset * p.startLocal);
            }
        }

        /// <summary>
        /// Foot placement: each foot is pulled toward its spot on the ground under the hips, forward or back
        /// with the stride, lifted a little on the swing. Keeps the feet under him (instead of trailing
        /// behind a body that is towed along) and gives the flip-flop shuffle; joint targets shape the knees.
        /// </summary>
        void Step()
        {
            float s = weight * (1f - air) * (staggerTimer > 0f ? 0.5f : 1f);
            if (s <= 0f) return;
            float walk = Mathf.Clamp01(speed / fullStrideSpeed);
            Quaternion frame = Facing();
            Vector3 right = frame * Vector3.right;
            Vector3 travel = frame * move;                                          // step along the direction of travel
            float stepScale = stepLength * walk * Mathf.Lerp(1f, sideStepScale, Mathf.Abs(move.x));
            float sin = Mathf.Sin(phase), cos = Mathf.Cos(phase);
            Vector3 ground = new Vector3(hips.body.position.x, root.position.y, hips.body.position.z);

            foreach (var p in parts)
            {
                if (p.role != Role.Foot) continue;
                float legSign = p.side < 0f ? 1f : -1f;
                float swing = Mathf.Max(0f, -legSign * cos);
                float ahead = -legSign * sin * stepScale;
                Vector3 target = ground + travel * ahead + right * (p.side * stanceWidth)
                                 + Vector3.up * (p.restPosition.y + swing * stepHeight * walk);
                Vector3 velocity = rootVelocity + travel * (-legSign * cos * stepScale * phaseRate);
                Vector3 pull = (target - p.body.position) * footSpring + (velocity - p.body.linearVelocity) * footDamping;
                pull = Vector3.ClampMagnitude(pull, maxFootAcceleration);
                float mass = p.body.mass + p.joint.connectedBody.mass;   // foot and shin
                p.body.AddForce(pull * (mass * s), ForceMode.Force);
            }
        }

        /// <summary>Lean toward a held object that is out of arm's reach in front, so he strains to hold it out.</summary>
        float CarryLean(Vector3 forward)
        {
            float beyond = 0f;
            for (int i = 0; i < 2; i++)
            {
                if (!reaching[i] || hands[i] == null) continue;
                Vector3 to = reachTargets[i] - upperArms[i].body.position;
                if (Vector3.Dot(to, forward) <= 0f) continue;
                beyond = Mathf.Max(beyond, (to.magnitude - ReachLength(i)) * reachWeight[i]);
            }
            return Mathf.Clamp(beyond * carryLeanPerMetre, 0f, maxCarryLean) * (1f - throwCharge);
        }

        /// <summary>The capsule's yaw: the character's frame for gait, lean and reach.</summary>
        Quaternion Facing()
        {
            Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(hips.body.rotation * Quaternion.Inverse(hips.restRotation) * Vector3.forward, Vector3.up);
            return forward.sqrMagnitude < 0.001f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>
        /// Hands onto what they hold: the wrist is pulled so the palm lands on the grip point (within arm's
        /// reach of the shoulder). Winding up a throw lifts the hands overhead; the throw flings them forward.
        /// </summary>
        void Reach()
        {
            Vector3 forward = Facing() * Vector3.forward;
            for (int i = 0; i < 2; i++)
            {
                var hand = hands[i];
                if (hand == null || upperArms[i] == null || reachWeight[i] <= 0f || armDriven[i]) continue;
                if (!reaching[i] && throwSwingTimer <= 0f) continue;   // letting go: the arm just eases back
                Vector3 shoulder = upperArms[i].body.position;
                float armLength = Vector3.Distance(upperArms[i].restPosition, hand.restPosition);
                Vector3 target = reaching[i] ? reachTargets[i] : shoulder + forward * armLength;
                if (throwCharge > 0f) target = Vector3.Lerp(target, shoulder + Vector3.up * armLength - forward * 0.15f, throwCharge * 0.8f);
                if (throwSwingTimer > 0f) target = shoulder + forward * armLength + Vector3.up * 0.05f;

                Vector3 offset = target - shoulder;
                float distance = offset.magnitude;
                Vector3 direction = distance > 0.0001f ? offset / distance : forward;
                Vector3 wrist = shoulder + direction * Mathf.Clamp(distance - 0.08f, 0.1f, armLength);   // the palm, not the wrist, touches
                Vector3 pull = (wrist - hand.body.position) * reachSpring + (rootVelocity - hand.body.linearVelocity) * reachDamping;
                pull = Vector3.ClampMagnitude(pull, maxReachAcceleration);
                float mass = hand.body.mass + lowerArms[i].body.mass + upperArms[i].body.mass * 0.5f;
                hand.body.AddForce(pull * (mass * weight * reachWeight[i]), ForceMode.Force);
            }
        }

        /// <summary>
        /// Where an empty hand is held (armsOut): in front of its shoulder toward where the player looks (pitch
        /// limited), a little low and in toward the middle, like How To Fish's arms-out stance. Punches start
        /// from here and come back to it (PunchArmDriver).
        /// </summary>
        public Vector3 ArmsOutPoint(int hand)
        {
            hand = Mathf.Clamp(hand, 0, 1);
            Quaternion facing = Facing();
            // Elbow under and ahead of the shoulder, forearm out in front: the hand sits before the belly.
            Vector3 upper = ArmsOutDirection(hands[hand].side, facing * Vector3.right, Vector3.up, facing * Vector3.forward);
            Vector3 elbow = upperArms[hand].body.position + upper * Vector3.Distance(upperArms[hand].restPosition, lowerArms[hand].restPosition);
            Vector3 forearm = Quaternion.AngleAxis(-armsOutElbow, facing * Vector3.right) * upper;
            return elbow + forearm * Vector3.Distance(lowerArms[hand].restPosition, hands[hand].restPosition);
        }

        /// <summary>Which way a held-out arm points, given the character's right, up and forward axes in some frame.</summary>
        Vector3 ArmsOutDirection(float side, Vector3 right, Vector3 up, Vector3 forward)
        {
            float pitch = 0f;   // looking up: positive
            if (hasLook)
            {
                Vector3 aim = look * Vector3.forward;
                pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg, -armsOutMaxPitch, armsOutMaxPitch);
            }
            float down = (armsOutDrop - pitch) * Mathf.Deg2Rad, outward = armsOutSpread * Mathf.Deg2Rad;
            Vector3 ahead = forward * Mathf.Cos(outward) + right * (side * Mathf.Sin(outward));
            return (ahead * Mathf.Cos(down) - up * Mathf.Sin(down)).normalized;
        }

        /// <summary>Drive a joint toward a rotation relative to its connected body.</summary>
        static void SetTarget(Part p, Quaternion local)
        {
            p.joint.targetRotation = Quaternion.Inverse(p.jointFrame) * Quaternion.Inverse(local) * p.startLocal * p.jointFrame;
        }

        /// <summary>While limp, the hips stay within limpLeash of the capsule so the body never wanders off the networked position.</summary>
        void Leash()
        {
            Vector3 anchor = rootCollider != null ? rootCollider.bounds.center : root.position + Vector3.up * hipHeight;
            Vector3 offset = hips.body.position - anchor;
            float excess = offset.magnitude - limpLeash;
            if (excess <= 0f) return;
            Vector3 direction = offset.normalized;
            float closing = Mathf.Max(0f, Vector3.Dot(hips.body.linearVelocity - rootVelocity, direction));
            Vector3 acceleration = -direction * (excess * 80f + closing * 8f);
            hips.body.AddForce(acceleration * (totalMass * (1f - weight)), ForceMode.Force);
        }

        /// <summary>Hit reports from RagdollBodyPart: a hard knock flops the body, a lighter one makes it stagger.</summary>
        public void OnPartCollision(RagdollBodyPart part, Collision collision)
        {
            if (!simulated || puppet) return;
            var other = collision.rigidbody;
            if (other != null && ownBodies.Contains(other)) return;
            if (collision.collider == rootCollider) return;
            // Down and leading, the body is what lands: the player's fall rules are checked from here.
            if (BodyLeads && motor != null && motor.enabled) motor.OnBodyImpact(collision);
            // Speed along the contact normal, so feet sliding over the ground while walking don't count.
            Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.up;
            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
            if (other != null) impact *= Mathf.Clamp(other.mass / 15f, 0.3f, 1f);   // a gnome is not a car
            impact *= Sensitivity(part);                                          // a hand slapping the ground is not a head hit
            if (impact <= staggerVelocity) return;
            LastImpact = $"{impact:0.0} m/s {part.name} vs {collision.collider.name}";
            if (knockoutOnImpact && impact > knockoutVelocity && knockoutTimer <= 0f) Knockout(knockoutSeconds);
            else staggerTimer = Mathf.Max(staggerTimer, 0.3f + 0.08f * impact);
        }

        float Sensitivity(RagdollBodyPart part)
        {
            foreach (var p in parts)
            {
                if (p.body.gameObject != part.gameObject) continue;
                switch (p.role)
                {
                    case Role.Head: return 1.2f;
                    case Role.Hips: case Role.Spine: case Role.Chest: return 1f;
                    case Role.UpperArm: case Role.UpperLeg: case Role.LowerLeg: return 0.6f;
                    default: return 0.35f;   // forearms, hands, feet
                }
            }
            return 1f;
        }

        // --------------------------------------------------------------------------- public API

        /// <summary>Push one part (the hips if null).</summary>
        public void AddImpulse(Vector3 force, Rigidbody part)
        {
            var body = part != null ? part : hips.body;
            body.AddForce(force, ForceMode.Impulse);
            float kick = force.magnitude / Mathf.Max(1f, totalMass);
            if (kick > 1f) staggerTimer = Mathf.Max(staggerTimer, 0.4f + 0.2f * kick);
        }

        /// <summary>Send him flying. On the local player the real player is knocked down too (PlayerMotor hands the kick to the body).</summary>
        public void Launch(Vector3 velocity)
        {
            Knockout(knockoutSeconds);
            bool viaMotor = motor != null && motor.enabled && motor.body == this;
            if (viaMotor) motor.EnterRagdoll(knockoutSeconds, velocity);
            foreach (var p in parts)
            {
                if (!viaMotor) p.body.AddForce(velocity, ForceMode.VelocityChange);
                p.body.AddTorque(Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
            }
        }

        /// <summary>The same velocity change for every bone (a knock or a hit while down).</summary>
        public void AddVelocity(Vector3 velocity)
        {
            foreach (var p in parts) p.body.AddForce(velocity, ForceMode.VelocityChange);
        }

        /// <summary>Tip the body over: bones get speed in proportion to their height above the hips.</summary>
        public void Topple(Vector3 direction, float speedPerMetre)
        {
            foreach (var p in parts)
            {
                float height = Mathf.Max(0f, p.body.position.y - hips.body.position.y);
                p.body.AddForce(direction * (height * speedPerMetre), ForceMode.VelocityChange);
            }
        }

        /// <summary>Fully limp (true) or back to active balance (false), blended over modeBlendSeconds.</summary>
        public void SetLimp(bool limp) => manualLimp = limp;

        /// <summary>Remote copies: limp while the owner's player is ragdolled.</summary>
        public void FollowRootRagdoll(bool ragdolled) => rootLimp = ragdolled;

        /// <summary>Hand 0 (left) or 1 (right): reach for a world point (a grip on a held object), or let go.</summary>
        public void SetHandTarget(int hand, bool active, Vector3 worldPoint)
        {
            if (hand < 0 || hand > 1) return;
            reaching[hand] = active;
            reachTargets[hand] = worldPoint;
        }

        /// <summary>Shoulder of hand 0 (left) or 1 (right), to find the nearest grip on a held object.</summary>
        public Vector3 ShoulderPosition(int hand) => upperArms[Mathf.Clamp(hand, 0, 1)].body.position;

        /// <summary>0..1 while a throw charges: he leans back and lifts the load overhead.</summary>
        public void SetThrowCharge(float charge) => throwCharge = Mathf.Clamp01(charge);

        /// <summary>Let go of a throw: lunge forward and fling both arms.</summary>
        public void Throw()
        {
            throwCharge = 0f;
            throwSwingTimer = throwSwingSeconds;
            if (!simulated) return;
            Vector3 fling = Facing() * new Vector3(0f, 0.25f, 1f) * 4f;
            foreach (var hand in hands)
                if (hand != null) hand.body.AddForce(fling, ForceMode.VelocityChange);
        }

        /// <summary>Remote copies: crouch like the owner does.</summary>
        public void SetCrouch(bool crouching) => remoteCrouch = crouching;

        // --------------------------------------------------------------------------- punching

        public Rigidbody HandBody(int hand) => hands[Mathf.Clamp(hand, 0, 1)]?.body;
        public Rigidbody LowerArmBody(int hand) => lowerArms[Mathf.Clamp(hand, 0, 1)]?.body;
        public Rigidbody UpperArmBody(int hand) => upperArms[Mathf.Clamp(hand, 0, 1)]?.body;
        public Rigidbody Chest => chest?.body;
        public Rigidbody Head => head?.body;
        /// <summary>How far the chest leans from upright (degrees), whatever the bone's own axes.</summary>
        public float TorsoTilt => chest == null ? 0f : Vector3.Angle(chest.body.rotation * Quaternion.Inverse(chest.restRotation) * Vector3.up, Vector3.up);
        public float TotalMass => totalMass;
        /// <summary>Shoulder to wrist at full stretch (metres).</summary>
        public float ArmLength(int hand) => Vector3.Distance(upperArms[Mathf.Clamp(hand, 0, 1)].restPosition, hands[Mathf.Clamp(hand, 0, 1)].restPosition);
        /// <summary>The character's frame: the capsule's yaw.</summary>
        public Quaternion FacingRotation => Facing();
        /// <summary>The capsule's velocity as the body tracks it (smoothed for remote copies).</summary>
        public Vector3 RootVelocity => rootVelocity;
        public Collider RootCollider => rootCollider;
        public bool IsSimulated => simulated;
        public bool OwnsBody(Rigidbody body) => body != null && ownBodies.Contains(body);
        /// <summary>Is hand 0 (left) or 1 (right) reaching for something it holds?</summary>
        public bool IsReaching(int hand) => reaching[Mathf.Clamp(hand, 0, 1)];
        public Role PartRole(int index) => parts[index].role;
        public float PartSide(int index) => parts[index].side;

        public int PartIndexOf(Rigidbody body)
        {
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].body == body) return i;
            return -1;
        }

        /// <summary>A punch takes this arm over: its joints go soft and the gait, reach and throw leave it alone.</summary>
        public void SetArmDriven(int hand, bool driven)
        {
            if (hand >= 0 && hand < 2) armDriven[hand] = driven;
        }

        /// <summary>
        /// Extra torso lean (degrees, forward positive), twist about the vertical (degrees) and a step of the hips
        /// ahead of the capsule (metres) from a punch: the body steps into it while the gameplay capsule stays put.
        /// </summary>
        public void SetTorsoOffset(float leanDegrees, float twistDegrees, float stepMetres = 0f)
        {
            extraLean = leanDegrees;
            extraTwist = twistDegrees;
            extraStep = stepMetres;
        }

        /// <summary>Wobble after a hit: joints at `stiffness` (0..1) of their strength and balance about as weak, for `seconds`.</summary>
        public void Stagger(float seconds, float stiffness)
        {
            if (seconds <= 0f) return;
            staggerStiffness = staggerTimer > 0f ? Mathf.Min(staggerStiffness, stiffness) : stiffness;
            staggerTimer = Mathf.Max(staggerTimer, seconds);
        }

        public int PartCount => parts.Length;
        public Rigidbody PartBody(int index) => parts[index].body;
        public bool IsPuppet => puppet;

        /// <summary>
        /// Remote copies while the owner is down: on, every bone turns kinematic and is placed from the
        /// owner's streamed pose (RagdollPoseSync); off, the bones are physical again, moving at `velocity`,
        /// and the body blends back to active balance from limp.
        /// </summary>
        public void SetPuppet(bool on, Vector3 velocity)
        {
            if (on == puppet || !simulated) return;
            puppet = on;
            foreach (var p in parts)
            {
                p.body.isKinematic = on;
                if (on) continue;
                p.body.linearVelocity = velocity;
                p.body.angularVelocity = Vector3.zero;
            }
            weight = 0f;
            rootVelocity = velocity;
            lastRootPosition = root.position;
        }

        /// <summary>Puppet mode: move one bone to where the owner's copy has it.</summary>
        public void MovePuppet(int index, Vector3 position, Quaternion rotation)
        {
            var body = parts[index].body;
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        /// <summary>Where the head should look (world rotation of the view).</summary>
        public void SetLook(Quaternion view) { look = view; hasLook = true; }

        public void Knockout(float seconds) => knockoutTimer = Mathf.Max(knockoutTimer, seconds);

        /// <summary>Off: every bone kinematic and collision-free at the rest pose (the owner never sees their own body).</summary>
        public void SetSimulated(bool on)
        {
            simulated = on;
            enabled = on;
            foreach (var p in parts)
            {
                p.body.isKinematic = !on;
                p.body.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                foreach (var c in p.body.GetComponentsInChildren<Collider>())
                    if (c.attachedRigidbody == p.body) c.enabled = on;
            }
        }

        /// <summary>Teleport the whole body to its rest pose at the capsule (respawn, reset, falling out of range).</summary>
        [ContextMenu("Reset Pose")]
        public void ResetPose() => SnapToRoot(keepPose: false);

        /// <summary>
        /// Put the body at the capsule in whatever pose it has now, stood upright (respawn, being gathered): it
        /// gets back into its stance from there instead of flashing the model's stiff A-pose.
        /// </summary>
        public void MoveToRoot() => SnapToRoot(keepPose: true);

        const float TeleportStep = 1f;   // metres in one physics step: nothing walks or flies that fast

        /// <summary>Tests: average degrees between each joint and its rest pose (0 = standing in the model's A-pose).</summary>
        public float RestPoseError
        {
            get
            {
                float sum = 0f;
                int n = 0;
                foreach (var p in parts)
                {
                    if (p.joint == null || p.role == Role.Hand || p.role == Role.Foot) continue;
                    sum += Quaternion.Angle(Quaternion.Inverse(p.joint.connectedBody.rotation) * p.body.rotation, p.startLocal);
                    n++;
                }
                return n > 0 ? sum / n : 0f;
            }
        }

        /// <summary>How many times the body was teleported back onto the capsule (tests).</summary>
        public int SnapCount { get; private set; }

        void SnapToRoot(bool keepPose = true)
        {
            SnapCount++;
            if (root == null) return;
            // A teleport: forget the velocity tracked before it, take the capsule's own.
            rootVelocity = rootBody != null && !rootBody.isKinematic ? rootBody.linearVelocity : Vector3.zero;
            rootAcceleration = Vector3.zero;
            lastRootPosition = root.position;
            knockoutTimer = staggerTimer = 0f;

            // Keeping the pose: turn the whole body about its hips so the hips stand as at rest, move it onto the
            // capsule, and lift it if a sprawled limb would end up in the ground.
            Vector3 hipsTarget = root.position + root.rotation * hips.restPosition;
            Quaternion turn = (root.rotation * hips.restRotation) * Quaternion.Inverse(hips.body.rotation);
            Vector3 hipsNow = hips.body.position;
            float lift = 0f;
            if (keepPose)
                foreach (var p in parts)
                    lift = Mathf.Max(lift, root.position.y + 0.03f - (hipsTarget + turn * (p.body.position - hipsNow)).y);

            foreach (var p in parts)
            {
                Vector3 position = keepPose ? hipsTarget + turn * (p.body.position - hipsNow) + Vector3.up * lift
                                            : root.position + root.rotation * p.restPosition;
                Quaternion rotation = keepPose ? turn * p.body.rotation : root.rotation * p.restRotation;
                p.body.transform.SetPositionAndRotation(position, rotation);
                p.body.position = position;
                p.body.rotation = rotation;
                if (!p.body.isKinematic)
                {
                    p.body.linearVelocity = rootVelocity;
                    p.body.angularVelocity = Vector3.zero;
                }
            }
        }

        [ContextMenu("Toggle Ragdoll")]
        void ToggleRagdoll() => SetLimp(!manualLimp);

        [ContextMenu("Launch")]
        void LaunchForward() => Launch((root != null ? root.forward : transform.forward) * 6f + Vector3.up * 7f);
    }
}
