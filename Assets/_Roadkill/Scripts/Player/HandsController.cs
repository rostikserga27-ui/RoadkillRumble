using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Two independent first-person hands (GDD section 3), networked with the server in charge of physics.
    ///
    /// Owner: reads input, aims, asks the server to grab / release / throw, publishes its view pose and
    /// draws the hands. Server: keeps one kinematic anchor per hand in front of that player's view and
    /// pulls the held prop toward it through a force-capped joint. Lifting power therefore adds up
    /// physically on the server: one hand lifts 20 kg, one player 40 kg, two players 80 kg, four 160 kg.
    /// Anything heavier sags and drags: the carry table's "needs more friends or a dolly" rule.
    /// </summary>
    public class HandsController : NetworkBehaviour
    {
        public const ulong NoObject = ulong.MaxValue;
        const float LiftHeadroom = 1.25f;

        public class Hand
        {
            public readonly int Index;
            public readonly Vector3 RestOffset;   // view-local idle position
            public readonly float SideOffset;     // left / right spread while holding

            // Owner side
            public Transform Visual;
            public bool WasPressed;
            public Vector3 LocalGrip;             // grip point in the held prop's space

            // Server side
            public Rigidbody Anchor;
            public ConfigurableJoint Joint;
            public Rigidbody ServerHeld;
            public float HoldDistance;

            public Hand(int index, Vector3 restOffset, float sideOffset)
            {
                Index = index;
                RestOffset = restOffset;
                SideOffset = sideOffset;
            }
        }

        [Header("Grab")]
        public Camera viewCamera;
        public float reach = 2.6f;
        public float capacityPerHandKg = 20f;
        [Tooltip("Let go automatically when the grip is pulled this far from the hand.")]
        public float maxStretch = 2.2f;
        public float minHoldDistance = 0.9f;
        public float maxHoldDistance = 1.8f;

        [Header("Throw")]
        public float throwChargeSeconds = 1f;
        public float minThrowSpeed = 3f;
        public float maxThrowSpeed = 14f;
        [Tooltip("Objects up to this mass leave at full throw speed; heavier ones slow down proportionally.")]
        public float fullSpeedThrowMass = 6f;

        // Written by the server: what each hand holds (NetworkObjectId), readable everywhere.
        NetworkVariable<ulong> leftHeld = new NetworkVariable<ulong>(NoObject);
        NetworkVariable<ulong> rightHeld = new NetworkVariable<ulong>(NoObject);
        // Written by the owner: where it is looking, so the server can place the hand anchors.
        NetworkVariable<Vector3> viewPosition = new NetworkVariable<Vector3>(Vector3.zero,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        NetworkVariable<Quaternion> viewRotation = new NetworkVariable<Quaternion>(Quaternion.identity,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public Hand Left { get; private set; }
        public Hand Right { get; private set; }
        public Quaternion ViewRotation => viewRotation.Value;
        public float ThrowCharge { get; private set; }
        public float SpeedMultiplier { get; private set; } = 1f;
        public bool CanSprint { get; private set; } = true;
        public float HeaviestHeldMass { get; private set; }

        static readonly List<HandsController> All = new List<HandsController>();
        public static int PlayerCount => All.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        PlayerMotor motor;
        Collider[] ownColliders;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            ownColliders = GetComponentsInChildren<Collider>();
            Left = new Hand(0, new Vector3(-0.28f, -0.3f, 0.55f), -0.22f);
            Right = new Hand(1, new Vector3(0.28f, -0.3f, 0.55f), 0.22f);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            leftHeld.OnValueChanged += OnHeldChanged;
            rightHeld.OnValueChanged += OnHeldChanged;
            if (IsServer)
            {
                Left.Anchor = CreateAnchor("Left");
                Right.Anchor = CreateAnchor("Right");
            }
            if (IsOwner)
            {
                CreateVisual(Left, "Left");
                CreateVisual(Right, "Right");
                PublishView();
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            leftHeld.OnValueChanged -= OnHeldChanged;
            rightHeld.OnValueChanged -= OnHeldChanged;
            if (IsServer)
            {
                DropJoint(Left);
                DropJoint(Right);
                if (Left.Anchor != null) Destroy(Left.Anchor.gameObject);
                if (Right.Anchor != null) Destroy(Right.Anchor.gameObject);
            }
        }

        // ---- queries (valid on every peer) ---------------------------------------------------

        ulong HeldId(Hand hand) => hand.Index == 0 ? leftHeld.Value : rightHeld.Value;

        public Rigidbody HeldBody(Hand hand) => BodyFor(HeldId(hand));

        public bool IsHolding(Hand hand) => HeldBody(hand) != null;

        public bool IsHolding(Rigidbody body)
        {
            if (body == null || !body.TryGetComponent(out NetworkObject networkObject)) return false;
            ulong id = networkObject.NetworkObjectId;
            return leftHeld.Value == id || rightHeld.Value == id;
        }

        /// <summary>How many hands, across all players, are on this body.</summary>
        public static int HolderCount(Rigidbody body)
        {
            if (body == null || !body.TryGetComponent(out NetworkObject networkObject)) return 0;
            ulong id = networkObject.NetworkObjectId;
            int count = 0;
            foreach (var hands in All)
            {
                if (hands.leftHeld.Value == id) count++;
                if (hands.rightHeld.Value == id) count++;
            }
            return count;
        }

        Rigidbody BodyFor(ulong id)
        {
            if (id == NoObject || NetworkManager == null) return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var networkObject)
                ? networkObject.GetComponent<Rigidbody>()
                : null;
        }

        /// <summary>Your own body never collides with what you are holding, on any peer.</summary>
        void OnHeldChanged(ulong previous, ulong current)
        {
            SetIgnoreCollision(BodyFor(previous), false);
            SetIgnoreCollision(BodyFor(current), true);
        }

        void SetIgnoreCollision(Rigidbody body, bool ignore)
        {
            if (body == null) return;
            if (!ignore && IsHolding(body)) return;   // still held by the other hand
            foreach (var mine in ownColliders)
            foreach (var theirs in body.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(mine, theirs, ignore);
        }

        // ---- owner ---------------------------------------------------------------------------

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            PublishView();

            bool canAct = (motor == null || !motor.IsRagdolled) && Cursor.lockState == CursorLockMode.Locked;
            HandleHand(Left, canAct && RkInput.LeftHandHeld);
            HandleHand(Right, canAct && RkInput.RightHandHeld);

            bool holding = IsHolding(Left) || IsHolding(Right);
            if (holding && RkInput.ThrowHeld)
                ThrowCharge = Mathf.Min(1f, ThrowCharge + Time.deltaTime / throwChargeSeconds);
            if (RkInput.ThrowReleased)
            {
                if (holding && ThrowCharge > 0f) ThrowRpc(ThrowCharge, viewCamera.transform.forward);
                ThrowCharge = 0f;
            }
            if (!holding) ThrowCharge = 0f;

            UpdateLoad();
            UpdateVisual(Left);
            UpdateVisual(Right);
        }

        void PublishView()
        {
            Transform view = viewCamera.transform;
            if ((viewPosition.Value - view.position).sqrMagnitude > 0.0001f) viewPosition.Value = view.position;
            if (Quaternion.Angle(viewRotation.Value, view.rotation) > 0.2f) viewRotation.Value = view.rotation;
        }

        void HandleHand(Hand hand, bool pressed)
        {
            if (pressed && !hand.WasPressed && !IsHolding(hand)) TryGrab(hand);
            if (!pressed && hand.WasPressed) ReleaseRpc(hand.Index);
            hand.WasPressed = pressed;
        }

        void TryGrab(Hand hand)
        {
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (System.Array.IndexOf(ownColliders, hit.collider) >= 0) continue;
                var prop = hit.collider.GetComponentInParent<PhysicsProp>();
                var networkObject = prop != null ? prop.GetComponent<NetworkObject>() : null;
                if (networkObject == null) return;   // world geometry or another player blocks the grab
                hand.LocalGrip = prop.transform.InverseTransformPoint(hit.point);
                GrabRpc(hand.Index, networkObject, hand.LocalGrip,
                    Mathf.Clamp(hit.distance, minHoldDistance, maxHoldDistance));
                return;
            }
        }

        /// <summary>Test hook: grab whatever is under the crosshair with hand 0 (left) or 1 (right).</summary>
        public void DebugGrab(int handIndex)
        {
            if (IsSpawned && IsOwner) TryGrab(HandAt(handIndex));
        }

        /// <summary>Owner: drop everything (called when you ragdoll or respawn).</summary>
        public void ReleaseAll()
        {
            if (IsSpawned && IsOwner) ReleaseAllRpc();
        }

        /// <summary>Speed penalty by the heaviest object in hand, per the GDD carry table.</summary>
        void UpdateLoad()
        {
            float heaviest = 0f;
            var left = HeldBody(Left);
            var right = HeldBody(Right);
            if (left != null) heaviest = left.mass;
            if (right != null) heaviest = Mathf.Max(heaviest, right.mass);
            HeaviestHeldMass = heaviest;

            if (heaviest <= 5f) { SpeedMultiplier = 1f; CanSprint = true; }
            else if (heaviest <= 30f) { SpeedMultiplier = 0.75f; CanSprint = false; }
            else if (heaviest <= 80f) { SpeedMultiplier = 0.65f; CanSprint = false; }
            else if (heaviest <= 160f) { SpeedMultiplier = 0.55f; CanSprint = false; }
            else { SpeedMultiplier = 0.45f; CanSprint = false; }
        }

        void CreateVisual(Hand hand, string handName)
        {
            // Placeholder cartoon hand: a chunky skin-coloured block in the corner of the view.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = $"{handName}Hand";
            DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(viewCamera.transform, false);
            visual.transform.localPosition = hand.RestOffset;
            visual.transform.localScale = new Vector3(0.12f, 0.07f, 0.16f);
            var visualRenderer = visual.GetComponent<Renderer>();
            visualRenderer.sharedMaterial = RkMaterials.Get(new Color(1f, 0.8f, 0.62f));
            visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hand.Visual = visual.transform;
        }

        void UpdateVisual(Hand hand)
        {
            if (hand.Visual == null) return;
            Transform view = viewCamera.transform;
            var held = HeldBody(hand);
            Vector3 targetWorld = held != null ? held.transform.TransformPoint(hand.LocalGrip) : view.TransformPoint(hand.RestOffset);
            Vector3 local = Vector3.ClampMagnitude(view.InverseTransformPoint(targetWorld), 1.2f);
            local.z = Mathf.Max(local.z, 0.3f);
            hand.Visual.localPosition = Vector3.Lerp(hand.Visual.localPosition, local, 1f - Mathf.Exp(-20f * Time.deltaTime));
        }

        // ---- server --------------------------------------------------------------------------

        Hand HandAt(int index) => index == 0 ? Left : index == 1 ? Right : null;

        Rigidbody CreateAnchor(string handName)
        {
            var go = new GameObject($"{name} {handName}HandAnchor");
            var anchor = go.AddComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.interpolation = RigidbodyInterpolation.Interpolate;
            return anchor;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            MoveAnchor(Left);
            MoveAnchor(Right);
        }

        void MoveAnchor(Hand hand)
        {
            Vector3 position = viewPosition.Value;
            Quaternion rotation = viewRotation.Value;
            Vector3 target = hand.Joint != null
                ? position + rotation * new Vector3(hand.SideOffset, -0.15f, hand.HoldDistance)
                : position + rotation * hand.RestOffset;
            hand.Anchor.MovePosition(target);

            if (hand.Joint == null) return;
            if (hand.ServerHeld == null) { ServerRelease(hand); return; }
            Vector3 grip = hand.ServerHeld.transform.TransformPoint(hand.Joint.anchor);
            if (Vector3.Distance(grip, target) > maxStretch) ServerRelease(hand);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void GrabRpc(int handIndex, NetworkObjectReference targetRef, Vector3 localPoint, float holdDistance)
        {
            var hand = HandAt(handIndex);
            if (hand == null || !targetRef.TryGet(out NetworkObject target)) return;
            var body = target.GetComponent<Rigidbody>();
            if (body == null || target.GetComponent<PhysicsProp>() == null) return;

            Vector3 point = target.transform.TransformPoint(localPoint);
            if (Vector3.Distance(point, viewPosition.Value) > reach + 1.5f) return;   // allow for lag, not for cheats

            ServerRelease(hand);
            Attach(hand, body, point, Mathf.Clamp(holdDistance, minHoldDistance, maxHoldDistance));
            Debug.Log($"Roadkill: client {OwnerClientId} grabbed {target.name} ({body.mass:0} kg) with hand {handIndex}");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReleaseRpc(int handIndex) => ServerRelease(HandAt(handIndex));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReleaseAllRpc()
        {
            ServerRelease(Left);
            ServerRelease(Right);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ThrowRpc(float charge, Vector3 direction)
        {
            var thrown = new HashSet<Rigidbody>();
            if (Left.ServerHeld != null) thrown.Add(Left.ServerHeld);
            if (Right.ServerHeld != null) thrown.Add(Right.ServerHeld);
            ServerRelease(Left);
            ServerRelease(Right);

            float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Mathf.Clamp01(charge));
            Vector3 aim = (direction.normalized + Vector3.up * 0.15f).normalized;
            foreach (var body in thrown)
            {
                if (HolderCount(body) > 0) continue;   // a friend still has it: you only let go
                float massFactor = Mathf.Clamp(fullSpeedThrowMass / body.mass, 0.15f, 1f);
                body.AddForce(aim * speed * massFactor, ForceMode.VelocityChange);
            }
        }

        void Attach(Hand hand, Rigidbody target, Vector3 point, float holdDistance)
        {
            hand.HoldDistance = holdDistance;
            // Start the anchor at the grip so the object does not snap toward the player.
            hand.Anchor.position = point;
            hand.Anchor.transform.position = point;

            var joint = target.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = hand.Anchor;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = target.transform.InverseTransformPoint(point);
            joint.connectedAnchor = Vector3.zero;
            joint.enableCollision = false;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;

            float mass = target.mass;
            float spring = Mathf.Clamp(mass * 300f, 600f, 9000f);
            float dampedMass = Mathf.Min(mass, capacityPerHandKg * 2f);
            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = 2f * Mathf.Sqrt(spring * dampedMass) * 0.9f,
                // The cap is what makes weight matter: one hand can only pull this hard.
                // 25% headroom over its rated load, so a rated load can actually be lifted, not just held.
                maximumForce = capacityPerHandKg * -Physics.gravity.y * LiftHeadroom
            };
            joint.xDrive = drive;
            joint.yDrive = drive;
            joint.zDrive = drive;

            // Damp spinning without forcing an orientation, so loads still swing and twist.
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 0f,
                positionDamper = Mathf.Max(1f, mass * 0.5f),
                maximumForce = mass * 20f
            };

            target.WakeUp();
            hand.ServerHeld = target;
            hand.Joint = joint;
            SetHeld(hand, target.GetComponent<NetworkObject>().NetworkObjectId);
        }

        void ServerRelease(Hand hand)
        {
            if (hand == null) return;
            DropJoint(hand);
            if (HeldId(hand) != NoObject) SetHeld(hand, NoObject);
        }

        static void DropJoint(Hand hand)
        {
            if (hand.Joint != null) Destroy(hand.Joint);
            hand.Joint = null;
            hand.ServerHeld = null;
        }

        void SetHeld(Hand hand, ulong id)
        {
            if (hand.Index == 0) leftHeld.Value = id;
            else rightHeld.Value = id;
        }
    }
}
