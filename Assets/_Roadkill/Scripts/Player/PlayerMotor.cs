using Unity.Netcode.Components;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// First-person physics body (GDD section 3). Moves a capsule rigidbody at the GDD speeds and
    /// knocks the player down when struck, dropped from height or playing possum. Standing, the capsule
    /// is the gameplay and network body and the visible fisherman (ActiveRagdollController) follows it.
    /// Down, the lead flips: the limp body falls freely and the capsule (collision-free) trails its hips,
    /// so the network carries where the body lies and nothing rolls like a capsule. Without a body the
    /// capsule itself topples, as before. The possum camera pulls back to show the fall.
    /// Playground surfaces (PlaygroundSurface) change the ground: grip, a moving floor that carries you,
    /// trampolines and soft landings; PlaygroundRules scales jump, speed and the fall threshold.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class PlayerMotor : MonoBehaviour
    {
        enum State { Normal, Ragdoll, Possum, StandingUp }

        [Header("Movement")]
        public float walkSpeed = 4.5f;
        public float sprintSpeed = 7f;
        public float crouchSpeed = 2.5f;
        public float sprintStaminaSeconds = 6f;
        public float staminaRegenPerSecond = 1.5f;
        public float jumpHeight = 1.1f;
        public float groundAcceleration = 40f;
        public float airAcceleration = 8f;

        [Tooltip("The fisherman's active-ragdoll body, if any: it takes over while the player is down.")]
        public ActiveRagdollController body;

        [Header("Look")]
        public Transform cameraPivot;
        public float mouseSensitivity = 2f;
        public float standingEyeHeight = 1.6f;
        public float crouchEyeHeight = 1.05f;

        [Header("Body")]
        public float standingHeight = 1.8f;
        public float crouchHeight = 1.2f;

        [Header("Ragdoll")]
        public float fallRagdollHeight = 4f;
        public float fallDowntime = 2f;
        public float fallDamageStartHeight = 6f;
        public float fallDamagePerMetre = 10f;
        [Tooltip("Mashing Jump while down recovers this much faster.")]
        public float mashRecoveryBonus = 0.4f;
        public float standUpSeconds = 0.35f;

        public float Stamina { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsRagdolled => state != State.Normal;
        public bool IsPossum => state == State.Possum;
        public int CameraResetVersion { get; private set; }
        [Tooltip("Test hook: crouch as if Ctrl were held (NetTest).")]
        public bool debugCrouch;
        /// <summary>Set by debug panels while the mouse is over them, so clicking a button does not recapture the cursor.</summary>
        public static bool UiHasCursor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => UiHasCursor = false;

        State state = State.Normal;
        Rigidbody rb;
        CapsuleCollider capsule;
        PlayerHealth health;
        HandsController hands;
        bool bodyLeads;   // down: the capsule trails the body's hips
        PhysicsMaterial slideMaterial;
        PhysicsMaterial ragdollMaterial;
        float yaw;
        float pitch;
        bool jumpQueued;
        float ragdollTimer;
        float ragdollElapsed;
        float mashBoostTimer;
        float standUpProgress;
        Quaternion standUpFrom;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        PlaygroundSurface groundSurface;
        Vector3 groundPoint;
        float bounceSpeed;        // > 0: a trampoline landing to bounce off on the next step
        bool launched;            // thrown by a pad or trampoline: little air control until landing
        float launchGrace;
        float bodyFallSpeed;      // down: the hips' downward speed going into this physics step
        float bodyLandCooldown;
        float stumbleTimer;       // shoved: the feet barely grip, so the push carries

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            health = GetComponent<PlayerHealth>();
            hands = GetComponent<HandsController>();

            // Frictionless while walking so the capsule never sticks to walls; grippy while ragdolled.
            slideMaterial = new PhysicsMaterial("PlayerSlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            ragdollMaterial = new PhysicsMaterial("PlayerRagdoll")
            {
                dynamicFriction = 0.6f,
                staticFriction = 0.6f,
                frictionCombine = PhysicsMaterialCombine.Average
            };
            capsule.sharedMaterial = slideMaterial;

            Stamina = sprintStaminaSeconds;
            yaw = transform.eulerAngles.y;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            LockCursor(true);
        }

        void Update()
        {
            if (RkInput.EscapePressed) LockCursor(false);
            else if (RkInput.ClickPressed && Cursor.lockState != CursorLockMode.Locked && !UiHasCursor) LockCursor(true);

            switch (state)
            {
                case State.Normal:
                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        Vector2 look = RkInput.Look * mouseSensitivity;
                        yaw += look.x;
                        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
                    }
                    if (RkInput.JumpPressed) jumpQueued = true;
                    if (RkInput.Possum) EnterRagdoll(Mathf.Infinity, Vector3.zero, possum: true);
                    break;
                case State.Possum:
                    if (!RkInput.Possum && (health == null || !health.IsDown)) BeginStandUp();
                    break;
                case State.Ragdoll:
                    if (RkInput.JumpPressed) mashBoostTimer = 0.35f;
                    break;
            }

            UpdateCrouch();
            if (RkInput.ResetPressed) Respawn();
        }

        void LateUpdate()
        {
            // The body turns in FixedUpdate (50 Hz), so a camera that only inherited its yaw would turn in
            // steps between physics ticks. Aim the view straight from the mouse every frame instead.
            if (state == State.Normal)
                cameraPivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            switch (state)
            {
                case State.Normal:
                    Move(dt);
                    break;
                case State.Possum:
                    FollowBody();
                    break;
                case State.Ragdoll:
                    FollowBody();
                    ragdollElapsed += dt;
                    mashBoostTimer -= dt;
                    ragdollTimer -= dt * (mashBoostTimer > 0f ? 1f + mashRecoveryBonus : 1f);
                    Vector3 fallVelocity = bodyLeads ? body.Hips.linearVelocity : rb.linearVelocity;
                    bool settled = fallVelocity.sqrMagnitude < 2.25f || ragdollElapsed > 6f;
                    if (ragdollTimer <= 0f && settled && (health == null || !health.IsDown)) BeginStandUp();
                    break;
                case State.StandingUp:
                    standUpProgress += dt / standUpSeconds;
                    float t = Mathf.SmoothStep(0f, 1f, standUpProgress);
                    rb.MoveRotation(Quaternion.Slerp(standUpFrom, Quaternion.Euler(0f, yaw, 0f), t));
                    if (standUpProgress >= 1f) state = State.Normal;
                    break;
            }
        }

        /// <summary>Down: the ghost capsule stays upright under the body's hips, so the network sends where the body lies.</summary>
        void FollowBody()
        {
            if (!bodyLeads) return;
            bodyFallSpeed = Mathf.Max(0f, -body.Hips.linearVelocity.y);
            bodyLandCooldown -= Time.fixedDeltaTime;
            rb.MovePosition(body.Hips.position - Vector3.up * body.hipHeight);
            rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
        }

        void Move(float dt)
        {
            IsGrounded = CheckGround();
            rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

            Vector2 input = Time.time < debugMoveUntil ? debugMove : RkInput.Move;
            bool moving = input.sqrMagnitude > 0.01f;
            bool canSprint = hands == null || hands.CanSprint;
            IsSprinting = !IsCrouching && RkInput.Sprint && canSprint && moving && Stamina > 0f;

            float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            if (hands != null) speed *= hands.SpeedMultiplier;
            speed *= PlaygroundRules.SpeedScale;
            Stamina = IsSprinting
                ? Mathf.Max(0f, Stamina - dt)
                : Mathf.Min(sprintStaminaSeconds, Stamina + staminaRegenPerSecond * dt);

            launchGrace -= dt;
            if (launched && IsGrounded && launchGrace <= 0f) launched = false;
            stumbleTimer -= dt;

            // Steering is relative to the floor: a moving floor carries you, a slippery one barely lets you steer.
            float grip = Mathf.Min(groundSurface != null ? groundSurface.grip : 1f, PlaygroundRules.MaxGrip);
            Vector3 carry = IsGrounded && groundSurface != null ? Vector3.ProjectOnPlane(groundSurface.VelocityAt(groundPoint), Vector3.up) : Vector3.zero;
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y) * speed;
            Vector3 v = rb.linearVelocity;

            // Dashing (a punch closing in): straight at the point, braking so it stops at the set distance.
            bool dashing = Time.time < dashUntil && IsGrounded;
            float dashBrake = groundAcceleration * 2f * Mathf.Max(0.05f, grip);
            if (dashing)
            {
                Vector3 to = Vector3.ProjectOnPlane(dashTarget - transform.position, Vector3.up);
                float left = to.magnitude - dashStop;
                if (left <= 0.02f) { dashUntil = -1f; dashing = false; wish = Vector3.zero; }
                else wish = to / to.magnitude * Mathf.Min(dashSpeed, Mathf.Sqrt(2f * dashBrake * left));
            }

            // Hitting grippy ground much faster than legs can run (flung off the merry-go-round, off the
            // ice slide): trip and tumble instead of stopping dead.
            Vector3 slip = new Vector3(v.x, 0f, v.z) - carry;
            if (IsGrounded && grip >= 0.5f && launchGrace <= 0f && slip.magnitude > sprintSpeed * PlaygroundRules.SpeedScale + 3f)
            {
                EnterRagdoll(1.5f, slip.normalized * 0.5f);
                return;
            }
            // In the air you steer, but extra momentum (a launch, a fling off the merry-go-round) is kept.
            bool flying = launched || new Vector2(v.x, v.z).magnitude > speed + 1f;
            float accel = dashing ? dashBrake
                : IsGrounded ? groundAcceleration * Mathf.Max(0.05f, grip) * (stumbleTimer > 0f ? 0.12f : 1f)
                : airAcceleration * (flying ? 0.25f : 1f);
            Vector3 horizontal = carry + Vector3.MoveTowards(new Vector3(v.x, 0f, v.z) - carry, wish, accel * dt);

            float vertical = v.y;
            float g = -Physics.gravity.y;
            if (jumpQueued && IsGrounded) vertical = Mathf.Sqrt(2f * g * jumpHeight * PlaygroundRules.JumpScale);
            jumpQueued = false;
            if (bounceSpeed > 0f)
            {
                // Holding Space on landing bounces higher. Heights stay the same in moon gravity.
                vertical = bounceSpeed * (RkInput.JumpHeld ? 1.2f : 1f);
                bounceSpeed = 0f;
                launched = true;
                launchGrace = 0.2f;
            }

            Vector3 velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            // Thrown up hard (trampoline, super jump): the body comes along, unless it already bounced itself.
            if (body != null && body.enabled && (velocity - v).y > 6f)
                body.AddVelocity(Vector3.up * Mathf.Max(0f, velocity.y - body.Hips.linearVelocity.y));
            rb.linearVelocity = velocity;
        }

        bool CheckGround()
        {
            float radius = capsule.radius * 0.95f;
            Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);
            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, 0.15f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.attachedRigidbody == rb) continue;
                if (IsOwnBody(hit.collider)) continue;
                if (hands != null && hands.IsHolding(hit.rigidbody)) continue;
                if (hit.distance > 0f && hit.normal.y < 0.5f) continue;
                groundSurface = PlaygroundSurface.Of(hit.collider);
                groundPoint = hit.distance > 0f ? hit.point : transform.position;
                return true;
            }
            groundSurface = null;
            return false;
        }

        void UpdateCrouch()
        {
            bool wantCrouch = state == State.Normal && (RkInput.Crouch || debugCrouch);
            if (!wantCrouch && IsCrouching && state == State.Normal && !HasHeadroom()) wantCrouch = true;
            IsCrouching = wantCrouch;

            float height = IsCrouching ? crouchHeight : standingHeight;
            capsule.height = height;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);

            float eye = IsCrouching ? crouchEyeHeight : standingEyeHeight;
            Vector3 p = cameraPivot.localPosition;
            cameraPivot.localPosition = new Vector3(p.x, Mathf.MoveTowards(p.y, eye, 4f * Time.deltaTime), p.z);
        }

        bool HasHeadroom()
        {
            Vector3 origin = transform.position + Vector3.up * (crouchHeight - capsule.radius);
            foreach (var hit in Physics.SphereCastAll(origin, capsule.radius * 0.9f, Vector3.up,
                standingHeight - crouchHeight, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody == rb || IsOwnBody(hit.collider)) continue;
                return false;
            }
            return true;
        }

        /// <summary>The player's own active-ragdoll body (it follows the capsule and is never ground or ceiling).</summary>
        bool IsOwnBody(Collider c)
        {
            var part = c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<RagdollBodyPart>() : null;
            return part != null && part.controller != null && part.controller.root == transform;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (rb == null) return;
            Rigidbody other = collision.rigidbody;
            if (hands != null && hands.IsHolding(other)) return;

            Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.up;
            float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));

            var surface = PlaygroundSurface.Of(collision.collider);
            if (surface != null && state == State.Normal && normal.y > 0.5f)
            {
                if (surface.bounceSpeed > 0f)
                {
                    // Trampoline: at least its own bounce, more if you came down fast, but never runaway.
                    float scale = Mathf.Sqrt(-Physics.gravity.y / 9.81f);
                    bounceSpeed = Mathf.Min(Mathf.Max(surface.bounceSpeed * scale, impactSpeed * 0.85f), surface.bounceSpeed * scale * 1.3f);
                    return;
                }
                if (surface.softLanding) return;
            }

            // Landing hard on anything (props are kinematic copies on clients): a fall.
            // Getting hit by a moving prop is decided by the server, see ImpactReporter.
            if (other != null && !other.isKinematic) return;
            float dropHeight = impactSpeed * impactSpeed / (2f * -Physics.gravity.y);
            if (dropHeight >= fallRagdollHeight * PlaygroundRules.FallHeightScale && normal.y > 0.5f)
            {
                EnterRagdoll(fallDowntime, Vector3.zero);
                if (dropHeight > fallDamageStartHeight && health != null)
                    health.Damage((dropHeight - fallDamageStartHeight) * fallDamagePerMetre, "fall");
            }
        }

        /// <summary>
        /// A bone of the leading body hit something (ActiveRagdollController). While down the capsule is a
        /// collision-free ghost, so a fall that ends in ragdoll or possum is judged here by the same rule:
        /// landing on the ground from fallRagdollHeight keeps you down, past fallDamageStartHeight it hurts.
        /// </summary>
        public void OnBodyImpact(Collision collision)
        {
            if (!bodyLeads || bodyLandCooldown > 0f || collision.contactCount == 0) return;
            Vector3 normal = collision.GetContact(0).normal;
            Rigidbody other = collision.rigidbody;
            if (normal.y < 0.5f || (other != null && !other.isKinematic)) return;
            bodyLandCooldown = 0.4f;   // one landing, however many bones touch down

            var surface = PlaygroundSurface.Of(collision.collider);
            if (surface != null && (surface.softLanding || surface.bounceSpeed > 0f)) return;
            float gravity = -Physics.gravity.y * body.gravityMultiplier;   // the body falls under its own gravity
            float dropHeight = bodyFallSpeed * bodyFallSpeed / (2f * gravity);
            if (dropHeight < fallRagdollHeight * PlaygroundRules.FallHeightScale) return;

            // A hard landing is not a nap: playing possum mid-fall does not let you pop back up unhurt.
            if (state == State.Possum) state = State.Ragdoll;
            ragdollTimer = Mathf.Max(ragdollTimer, fallDowntime);
            if (dropHeight > fallDamageStartHeight && health != null)
                health.Damage((dropHeight - fallDamageStartHeight) * fallDamagePerMetre, "fall");
        }

        /// <summary>Knock the player down. Infinity seconds = until something calls stand-up (possum).</summary>
        public void EnterRagdoll(float seconds, Vector3 velocityKick, bool possum = false)
        {
            if (rb == null) return;
            if (state == State.Ragdoll || state == State.Possum)
            {
                ragdollTimer = Mathf.Max(ragdollTimer, seconds);
                if (bodyLeads) body.AddVelocity(velocityKick);
                else rb.AddForce(velocityKick, ForceMode.VelocityChange);
                return;
            }

            state = possum ? State.Possum : State.Ragdoll;
            ragdollTimer = seconds;
            ragdollElapsed = 0f;
            mashBoostTimer = 0f;
            jumpQueued = false;
            IsSprinting = false;

            if (hands != null) hands.ReleaseAll();

            if (body != null && body.enabled)
            {
                // The body takes over: it keeps the capsule's motion plus the kick, and is tipped over so
                // it falls instead of folding straight down. The capsule turns into a ghost behind it.
                bodyLeads = true;
                body.BodyLeads = true;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                body.AddVelocity(velocityKick);
                Vector3 fall = Vector3.ProjectOnPlane(velocityKick, Vector3.up);
                fall = fall.sqrMagnitude > 0.01f ? fall.normalized
                    : (transform.forward * (Random.value < 0.5f ? 1f : -1f) + transform.right * Random.Range(-0.4f, 0.4f)).normalized;
                body.Topple(fall, possum ? 1.5f : 2.5f);
                return;
            }

            rb.constraints = RigidbodyConstraints.None;
            capsule.sharedMaterial = ragdollMaterial;
            rb.AddForce(velocityKick, ForceMode.VelocityChange);
            // Tip the capsule so it actually falls instead of balancing on its base.
            Vector3 tipAxis = velocityKick.sqrMagnitude > 0.01f
                ? Vector3.Cross(Vector3.up, velocityKick.normalized)
                : transform.right * (Random.value < 0.5f ? 1f : -1f);
            rb.AddTorque(tipAxis * (possum ? 2.5f : 4f) + Random.insideUnitSphere * 0.5f, ForceMode.VelocityChange);
        }

        void BeginStandUp()
        {
            if (bodyLeads)
            {
                StandUpWhereTheBodyLies();
                return;
            }
            state = State.StandingUp;
            standUpProgress = 0f;
            standUpFrom = rb.rotation;

            Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
            yaw = Quaternion.LookRotation(facing.normalized, Vector3.up).eulerAngles.y;
            pitch = 0f;
            cameraPivot.localRotation = Quaternion.identity;

            rb.angularVelocity = Vector3.zero;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            capsule.sharedMaterial = slideMaterial;
            rb.position += Vector3.up * 0.15f;
        }

        /// <summary>Put the capsule back on its feet on the ground under the body's hips; the body then gets up onto it.</summary>
        void StandUpWhereTheBodyLies()
        {
            Vector3 hipsPosition = body.Hips.position;
            Vector3 feet = hipsPosition - Vector3.up * body.hipHeight;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(hipsPosition + Vector3.up * 0.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody == rb || IsOwnBody(hit.collider)) continue;
                if (hit.distance < nearest) { nearest = hit.distance; feet = hit.point; }
            }
            Vector3 position = feet + Vector3.up * 0.05f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(position, rotation);
            rb.position = position;
            rb.rotation = rotation;
            LeaveBody();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            state = State.StandingUp;
            standUpProgress = 0f;
            standUpFrom = rotation;
            pitch = 0f;
            cameraPivot.localRotation = Quaternion.identity;
        }

        /// <summary>The capsule is solid and in charge again.</summary>
        void LeaveBody()
        {
            if (!bodyLeads) return;
            bodyLeads = false;
            body.BodyLeads = false;
            rb.isKinematic = false;
            rb.detectCollisions = true;
        }

        public void Respawn() => TeleportTo(spawnPosition, spawnRotation);

        /// <summary>Throw the standing player through the air (jump pads): keeps the speed, little air control until landing.</summary>
        public void Launch(Vector3 velocity)
        {
            if (rb == null || state != State.Normal) return;
            Vector3 change = velocity - rb.linearVelocity;
            rb.linearVelocity = velocity;
            if (body != null && body.enabled) body.AddVelocity(change);
            launched = true;
            launchGrace = 0.2f;
            jumpQueued = false;
        }

        /// <summary>
        /// A push from outside (a punch, or your own lunge into one): adds speed, and for `stumbleSeconds` the
        /// feet barely grip so the push carries instead of being walked off at once. While down the body takes it.
        /// </summary>
        public void Shove(Vector3 velocity, float stumbleSeconds)
        {
            if (rb == null) return;
            if (state != State.Normal)
            {
                if (bodyLeads) body.AddVelocity(velocity);
                else if (!rb.isKinematic) rb.AddForce(velocity, ForceMode.VelocityChange);
                return;
            }
            rb.linearVelocity += velocity;
            stumbleTimer = Mathf.Max(stumbleTimer, stumbleSeconds);
            if (velocity.y > 1f)
            {
                // Lifted off the feet: keep the momentum through the air.
                launched = true;
                launchGrace = 0.2f;
            }
        }

        /// <summary>Put the player somewhere, standing, with empty hands (respawn, gathering).</summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            if (rb == null) return;
            CameraResetVersion++;
            if (hands != null) hands.ReleaseAll();
            transform.SetPositionAndRotation(position, rotation);
            rb.position = position;
            rb.rotation = rotation;
            NetworkTeleport(transform, position, rotation);
            LeaveBody();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            launched = false;
            bounceSpeed = 0f;
            yaw = rotation.eulerAngles.y;
            pitch = 0f;
            cameraPivot.localPosition = new Vector3(0f, standingEyeHeight, 0f);
            cameraPivot.localRotation = Quaternion.identity;
            state = State.Normal;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            capsule.sharedMaterial = slideMaterial;
            if (body != null && body.enabled) body.ResetPose();
        }

        /// <summary>
        /// A jump, not a move: other players' copies appear at the new spot instead of being interpolated
        /// across the map (which dragged their ragdoll along and snapped it back).
        /// </summary>
        public static void NetworkTeleport(Transform player, Vector3 position, Quaternion rotation)
        {
            var networkTransform = player.GetComponent<NetworkTransform>();
            if (networkTransform != null && networkTransform.IsSpawned && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, player.localScale);
        }

        /// <summary>Test hook: put the player somewhere and aim the view at a point.</summary>
        Vector3 dashTarget;
        float dashStop, dashSpeed, dashUntil = -1f;

        /// <summary>
        /// A short committed step (a punch closing in on its target): straight at `point` at `speed`, braking to
        /// stop `stop` metres short of it (horizontally), for at most `seconds`. Walls and bodies still stop it.
        /// </summary>
        public void Dash(Vector3 point, float stop, float speed, float seconds)
        {
            if (state != State.Normal) return;
            dashTarget = point;
            dashStop = stop;
            dashSpeed = speed;
            dashUntil = Time.time + seconds;
        }

        Vector2 debugMove;
        float debugMoveUntil = -1f;

        /// <summary>Test hook: walk as if WASD gave `move` (x right, y forward) for `seconds`.</summary>
        public void DebugMove(Vector2 move, float seconds)
        {
            debugMove = Vector2.ClampMagnitude(move, 1f);
            debugMoveUntil = Time.time + seconds;
        }

        /// <summary>Test hook: turn the view toward a point, as the mouse would (no teleport).</summary>
        public void DebugLook(Vector3 point)
        {
            Vector3 direction = point - cameraPivot.position;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -85f, 85f);
        }

        public void DebugPlace(Vector3 position, Vector3 lookAt)
        {
            if (rb == null) return;
            transform.position = position;
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            Vector3 eye = position + Vector3.up * standingEyeHeight;
            Vector3 direction = lookAt - eye;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            rb.rotation = transform.rotation;
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
