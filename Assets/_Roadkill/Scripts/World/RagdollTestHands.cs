using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Offline stand-in for HandsController in the RagdollTest scene (no network): LMB / RMB grab the
    /// crate you look at (or the nearest one in front of you), carried on a force-capped spring like the
    /// server's hand anchors (20 kg per hand); hold G and let go to throw. Feeds the fisherman's hands
    /// exactly the way PlayerNet does in the game.
    /// </summary>
    public class RagdollTestHands : MonoBehaviour
    {
        public RagdollTestBuilder builder;
        public float reach = 2.6f;
        public float holdDistance = 1.1f;
        public float capacityPerHandKg = 20f;
        public float throwChargeSeconds = 1f;
        public float minThrowSpeed = 3f;
        public float maxThrowSpeed = 14f;
        public float fullSpeedThrowMass = 6f;

        class Hand
        {
            public float side;
            public Rigidbody anchor;
            public Rigidbody held;
            public ConfigurableJoint joint;
            public Vector3 localGrip;
            public bool wasPressed;
        }

        readonly Hand[] hands = { new Hand { side = -0.22f }, new Hand { side = 0.22f } };

        public float ThrowCharge { get; private set; }
        public bool IsHolding(int hand) => hands[hand].held != null;
        public Rigidbody Held(int hand) => hands[hand].held;
        public Vector3 Grip(int hand) => hands[hand].held != null ? hands[hand].held.transform.TransformPoint(hands[hand].localGrip) : Vector3.zero;

        void Start()
        {
            foreach (var hand in hands)
            {
                var go = new GameObject("Test Hand Anchor");
                hand.anchor = go.AddComponent<Rigidbody>();
                hand.anchor.isKinematic = true;
                hand.anchor.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        void Update()
        {
            var motor = builder.Motor;
            var body = builder.Body;
            if (motor == null || body == null) return;
            bool canAct = !motor.IsRagdolled && Cursor.lockState == CursorLockMode.Locked;
            if (motor.IsRagdolled) { Release(0); Release(1); }
            Handle(0, canAct && RkInput.LeftHandHeld);
            Handle(1, canAct && RkInput.RightHandHeld);

            bool holding = IsHolding(0) || IsHolding(1);
            if (holding && RkInput.ThrowHeld) ThrowCharge = Mathf.Min(1f, ThrowCharge + Time.deltaTime / throwChargeSeconds);
            if (RkInput.ThrowReleased)
            {
                if (holding && ThrowCharge > 0f) Throw();
                ThrowCharge = 0f;
            }
            if (!holding) ThrowCharge = 0f;

            for (int i = 0; i < 2; i++) body.SetHandTarget(i, IsHolding(i), Grip(i));
            body.SetThrowCharge(ThrowCharge);
        }

        void Handle(int index, bool pressed)
        {
            var hand = hands[index];
            if (pressed && !hand.wasPressed && hand.held == null) Grab(index);
            if (!pressed && hand.wasPressed) Release(index);
            hand.wasPressed = pressed;
        }

        void FixedUpdate()
        {
            var motor = builder.Motor;
            if (motor == null) return;
            Transform view = motor.cameraPivot;
            foreach (var hand in hands)
            {
                if (hand.anchor == null) continue;
                hand.anchor.MovePosition(view.position + view.rotation * new Vector3(hand.side, -0.15f, holdDistance));
                if (hand.held != null && Vector3.Distance(hand.held.transform.TransformPoint(hand.joint.anchor), hand.anchor.position) > 2.2f)
                    Release(System.Array.IndexOf(hands, hand));
            }
        }

        /// <summary>Grab what the eyes look at, or else the nearest loose body in front.</summary>
        public bool Grab(int index)
        {
            var motor = builder.Motor;
            Transform view = motor.cameraPivot;
            Rigidbody target = null;
            Vector3 point = Vector3.zero;
            foreach (var hit in Physics.RaycastAll(view.position, view.forward, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!Grabbable(hit.rigidbody)) continue;
                target = hit.rigidbody;
                point = hit.point;
                break;
            }
            if (target == null)
            {
                float best = 1.8f;
                foreach (var candidate in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
                {
                    if (!Grabbable(candidate)) continue;
                    Vector3 to = candidate.worldCenterOfMass - motor.transform.position;
                    float distance = new Vector2(to.x, to.z).magnitude;
                    if (distance < best && Vector3.Dot(to.normalized, motor.transform.forward) > 0.3f)
                    {
                        best = distance;
                        target = candidate;
                    }
                }
                if (target == null) return false;
                point = target.GetComponent<Collider>().ClosestPoint(view.position);
            }
            Attach(hands[index], target, point);
            return true;
        }

        bool Grabbable(Rigidbody body)
        {
            if (body == null || body.isKinematic || body == builder.Motor.GetComponent<Rigidbody>()) return false;
            if (body.GetComponent<RagdollBodyPart>() != null) return false;
            return body.mass < 200f;
        }

        void Attach(Hand hand, Rigidbody target, Vector3 point)
        {
            Release(hand);
            hand.anchor.position = point;
            hand.anchor.transform.position = point;
            var joint = target.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = hand.anchor;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = target.transform.InverseTransformPoint(point);
            joint.connectedAnchor = Vector3.zero;
            joint.enableCollision = false;
            float spring = Mathf.Clamp(target.mass * 300f, 600f, 9000f);
            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = 2f * Mathf.Sqrt(spring * Mathf.Min(target.mass, capacityPerHandKg * 2f)) * 0.9f,
                maximumForce = capacityPerHandKg * -Physics.gravity.y * 1.25f
            };
            joint.xDrive = joint.yDrive = joint.zDrive = drive;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = Mathf.Max(1f, target.mass * 0.5f), maximumForce = target.mass * 20f };
            hand.held = target;
            hand.joint = joint;
            hand.localGrip = target.transform.InverseTransformPoint(point);
            IgnoreOwnBody(target, true);
        }

        public void Release(int index) => Release(hands[index]);

        void Release(Hand hand)
        {
            if (hand.joint != null) Destroy(hand.joint);
            var was = hand.held;
            hand.joint = null;
            hand.held = null;
            if (was != null && hands[0].held != was && hands[1].held != was) IgnoreOwnBody(was, false);
        }

        /// <summary>Like HandsController: your own body never collides with what you hold.</summary>
        void IgnoreOwnBody(Rigidbody target, bool ignore)
        {
            var own = builder.Motor.GetComponentsInChildren<Collider>();
            var bodyColliders = builder.Body.GetComponentsInChildren<Collider>();
            foreach (var theirs in target.GetComponentsInChildren<Collider>())
            {
                foreach (var mine in own) Physics.IgnoreCollision(mine, theirs, ignore);
                foreach (var mine in bodyColliders) Physics.IgnoreCollision(mine, theirs, ignore);
            }
        }

        /// <summary>Let go of everything with the charged throw speed, like HandsController's ThrowRpc.</summary>
        public void Throw()
        {
            var thrown = new System.Collections.Generic.HashSet<Rigidbody>();
            foreach (var hand in hands) if (hand.held != null) thrown.Add(hand.held);
            Release(0);
            Release(1);
            float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Mathf.Clamp01(ThrowCharge));
            Vector3 aim = (builder.Motor.cameraPivot.forward + Vector3.up * 0.15f).normalized;
            foreach (var body in thrown)
                body.AddForce(aim * speed * Mathf.Clamp(fullSpeedThrowMass / body.mass, 0.15f, 1f), ForceMode.VelocityChange);
            builder.Body.Throw();
            ThrowCharge = 0f;
        }

        /// <summary>Test hook: set the charge directly (the autotest holds G through the real input instead).</summary>
        public void SetCharge(float charge) => ThrowCharge = Mathf.Clamp01(charge);
    }
}
