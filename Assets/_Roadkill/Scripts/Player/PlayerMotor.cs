using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// First-person physics body (GDD section 3). Moves a capsule rigidbody at the GDD speeds. When
    /// struck, dropped from height or playing possum, the character's bone ragdoll (RagdollRig) takes
    /// over: the capsule turns into a collider-less ghost that follows the hips, so every limb touches
    /// the world on its own, and the camera rides the head. Without a character model the capsule
    /// itself topples, as before.
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

        State state = State.Normal;
        Rigidbody rb;
        CapsuleCollider capsule;
        PlayerHealth health;
        HandsController hands;
        RagdollRig ragdoll;
        Transform head;
        Vector3 eyeInHead;              // camera pivot in the head bone's space, rest pose
        Quaternion pivotFromHead;
        Vector3 pivotStandUpPosition;
        Quaternion pivotStandUpRotation;
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

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            health = GetComponent<PlayerHealth>();
            hands = GetComponent<HandsController>();
            ragdoll = GetComponentInChildren<RagdollRig>(true);
            var net = GetComponent<PlayerNet>();
            head = net != null ? net.head : null;
            if (ragdoll == null || head == null) ragdoll = null;
            else
            {
                eyeInHead = head.InverseTransformPoint(cameraPivot.position);
                pivotFromHead = Quaternion.Inverse(head.rotation) * cameraPivot.rotation;
            }

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
            else if (RkInput.ClickPressed && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

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
            if (state == State.StandingUp && ragdoll != null)
            {
                // Bring the camera from the head back to the eyes.
                float t = Mathf.SmoothStep(0f, 1f, standUpProgress);
                cameraPivot.localPosition = Vector3.Lerp(pivotStandUpPosition, new Vector3(0f, standingEyeHeight, 0f), t);
                cameraPivot.localRotation = Quaternion.Slerp(pivotStandUpRotation, Quaternion.identity, t);
            }
            if (RkInput.ResetPressed) Respawn();
        }

        void LateUpdate()
        {
            // Down with a bone ragdoll: look out of the head, so the view tumbles with the body.
            if (ragdoll != null && ragdoll.IsActive)
                cameraPivot.SetPositionAndRotation(head.TransformPoint(eyeInHead), head.rotation * pivotFromHead);
            // The body turns in FixedUpdate (50 Hz), so a camera that only inherited its yaw would turn in
            // steps between physics ticks. Aim the view straight from the mouse every frame instead.
            else if (state == State.Normal)
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
                    Vector3 bodyVelocity = ragdoll != null ? ragdoll.Velocity : rb.linearVelocity;
                    bool settled = bodyVelocity.sqrMagnitude < 2.25f || ragdollElapsed > 6f;
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

        /// <summary>The ghost capsule trails the ragdoll's hips, so the network sends where the body is.</summary>
        void FollowBody()
        {
            if (ragdoll == null || !ragdoll.IsActive) return;
            rb.MovePosition(ragdoll.hips.position - Vector3.up * ragdoll.HipsHeight);
        }

        void Move(float dt)
        {
            IsGrounded = CheckGround();
            rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

            Vector2 input = RkInput.Move;
            bool moving = input.sqrMagnitude > 0.01f;
            bool canSprint = hands == null || hands.CanSprint;
            IsSprinting = !IsCrouching && RkInput.Sprint && canSprint && moving && Stamina > 0f;

            float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            if (hands != null) speed *= hands.SpeedMultiplier;
            Stamina = IsSprinting
                ? Mathf.Max(0f, Stamina - dt)
                : Mathf.Min(sprintStaminaSeconds, Stamina + staminaRegenPerSecond * dt);

            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y) * speed;
            Vector3 v = rb.linearVelocity;
            float accel = IsGrounded ? groundAcceleration : airAcceleration;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), wish, accel * dt);

            float vertical = v.y;
            if (jumpQueued && IsGrounded) vertical = Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight);
            jumpQueued = false;

            rb.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
        }

        bool CheckGround()
        {
            float radius = capsule.radius * 0.95f;
            Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);
            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, 0.15f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.attachedRigidbody == rb) continue;
                if (hands != null && hands.IsHolding(hit.rigidbody)) continue;
                if (hit.distance > 0f && hit.normal.y < 0.5f) continue;
                return true;
            }
            return false;
        }

        void UpdateCrouch()
        {
            bool wantCrouch = state == State.Normal && RkInput.Crouch;
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
            return !Physics.SphereCast(origin, capsule.radius * 0.9f, Vector3.up, out _,
                standingHeight - crouchHeight, ~0, QueryTriggerInteraction.Ignore);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (rb == null) return;
            Rigidbody other = collision.rigidbody;
            if (hands != null && hands.IsHolding(other)) return;

            Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.up;
            float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));

            // Landing hard on anything (props are kinematic copies on clients): a fall.
            // Getting hit by a moving prop is decided by the server, see ImpactReporter.
            if (other != null && !other.isKinematic) return;
            float dropHeight = impactSpeed * impactSpeed / (2f * -Physics.gravity.y);
            if (dropHeight >= fallRagdollHeight && normal.y > 0.5f)
            {
                EnterRagdoll(fallDowntime, Vector3.zero);
                if (dropHeight > fallDamageStartHeight && health != null)
                    health.Damage((dropHeight - fallDamageStartHeight) * fallDamagePerMetre, "fall");
            }
        }

        /// <summary>Knock the player down. Infinity seconds = until something calls stand-up (possum).</summary>
        public void EnterRagdoll(float seconds, Vector3 velocityKick, bool possum = false)
        {
            if (rb == null) return;
            if (state == State.Ragdoll || state == State.Possum)
            {
                ragdollTimer = Mathf.Max(ragdollTimer, seconds);
                if (ragdoll != null && ragdoll.IsActive) ragdoll.AddVelocity(velocityKick);
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

            if (ragdoll != null)
            {
                Vector3 velocity = rb.linearVelocity + velocityKick;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                ragdoll.SetActive(true, velocity, pinned: false);
                // Bones have no muscles, so the body crumples by itself; nudge the upper half so it
                // falls over instead of folding straight down.
                Vector3 fall = Vector3.ProjectOnPlane(velocityKick, Vector3.up);
                fall = fall.sqrMagnitude > 0.01f ? fall.normalized
                    : transform.forward * (Random.value < 0.5f ? 1f : -1f) + transform.right * Random.Range(-0.4f, 0.4f);
                ragdoll.Topple(fall.normalized, possum ? 1.5f : 2.5f);
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
            if (ragdoll != null && ragdoll.IsActive)
            {
                BeginStandUpFromBody();
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

        /// <summary>Put the capsule back on its feet where the body lies, facing the way the player last looked.</summary>
        void BeginStandUpFromBody()
        {
            Vector3 hipsPosition = ragdoll.hips.position;
            Vector3 feet = hipsPosition - Vector3.up * ragdoll.HipsHeight;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(hipsPosition + Vector3.up * 0.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.collider.attachedRigidbody;
                if (body == rb || (body != null && body.transform.IsChildOf(ragdoll.transform))) continue;
                if (hit.distance < nearest) { nearest = hit.distance; feet = hit.point; }
            }

            // Move the capsule first: reattaching keeps the bones' world pose, and they blend home from there.
            Vector3 position = feet + Vector3.up * 0.05f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 eyePosition = cameraPivot.position;
            Quaternion eyeRotation = cameraPivot.rotation;
            transform.SetPositionAndRotation(position, rotation);
            cameraPivot.SetPositionAndRotation(eyePosition, eyeRotation);   // the view blends up from the head
            rb.position = position;
            rb.rotation = rotation;
            ragdoll.SetActive(false);
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            state = State.StandingUp;
            standUpProgress = 0f;
            standUpFrom = rotation;
            pitch = 0f;
            pivotStandUpPosition = cameraPivot.localPosition;
            pivotStandUpRotation = cameraPivot.localRotation;
        }

        public void Respawn()
        {
            CameraResetVersion++;
            if (hands != null) hands.ReleaseAll();
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            rb.position = spawnPosition;
            rb.rotation = spawnRotation;
            if (ragdoll != null && ragdoll.IsActive)
            {
                ragdoll.SetActive(false, snapToRest: true);
                rb.isKinematic = false;
                rb.detectCollisions = true;
            }
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            yaw = spawnRotation.eulerAngles.y;
            pitch = 0f;
            cameraPivot.localPosition = new Vector3(0f, standingEyeHeight, 0f);
            cameraPivot.localRotation = Quaternion.identity;
            state = State.Normal;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            capsule.sharedMaterial = slideMaterial;
        }

        /// <summary>Test hook: put the player somewhere and aim the view at a point.</summary>
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
