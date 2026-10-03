using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Two independent first-person hands (GDD section 3), networked with the server in charge of physics.
    ///
    /// Owner: reads input (E takes hold with both hands and lets go, G throws), aims, asks the server to
    /// grab / release / throw, publishes its view pose and draws the hands. Server: keeps one kinematic anchor per hand in front of that player's view and
    /// pulls the held prop toward it through a force-capped joint. Lifting power therefore adds up
    /// physically on the server: one hand lifts 20 kg, one player 40 kg, two players 80 kg, four 160 kg.
    /// Anything heavier sags and drags: the carry table's "needs more friends or a dolly" rule.
    /// Everyone sees the throw too: the owner shares its charge and each throw, which the fisherman's
    /// body (ActiveRagdollController, via PlayerNet) turns into a wind-up and a fling.
    /// A friend who is out cold can be grabbed the same way (E on their body): the server records which bone
    /// each hand holds, and the downed player's own machine drags its body there (PlayerKnockout).
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
            public Quaternion VisualRestRotation;
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
        [Tooltip("First-person sleeve-and-hand model (left hand; mirrored for the right). Cubes if empty.")]
        public GameObject handModel;
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
        // Written by the server while a hand holds a body: which bone, and where on it (bone space).
        NetworkVariable<int> leftGripPart = new NetworkVariable<int>(-1);
        NetworkVariable<int> rightGripPart = new NetworkVariable<int>(-1);
        NetworkVariable<Vector3> leftGripLocal = new NetworkVariable<Vector3>(Vector3.zero);
        NetworkVariable<Vector3> rightGripLocal = new NetworkVariable<Vector3>(Vector3.zero);
        // Written by the owner: where it is looking, so the server can place the hand anchors.
        NetworkVariable<Vector3> viewPosition = new NetworkVariable<Vector3>(Vector3.zero,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        NetworkVariable<Quaternion> viewRotation = new NetworkVariable<Quaternion>(Quaternion.identity,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // Written by the owner: throw charge (0..255) and a count of throws, so other players see the wind-up and the fling.
        NetworkVariable<byte> throwChargeShared = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        NetworkVariable<int> throwCount = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public Hand Left { get; private set; }
        public Hand Right { get; private set; }
        public Quaternion ViewRotation => viewRotation.Value;
        public float ThrowCharge { get; private set; }
        /// <summary>Throw charge as every peer sees it (exact on the owner, shared for the others).</summary>
        public float VisibleThrowCharge => IsOwner ? ThrowCharge : throwChargeShared.Value / 255f;
        /// <summary>Goes up by one on every throw, on every peer.</summary>
        public int ThrowCount => throwCount.Value;
        public float SpeedMultiplier { get; private set; } = 1f;
        public bool CanSprint { get; private set; } = true;
        public float HeaviestHeldMass { get; private set; }

        static readonly List<HandsController> All = new List<HandsController>();
        public static int PlayerCount => All.Count;
        public static IReadOnlyList<HandsController> Players => All;

        /// <summary>The most one hand pulls with (N): its rated load plus lifting headroom.</summary>
        public float HandForceCap => capacityPerHandKg * PlaygroundRules.CarryScale * -Physics.gravity.y * LiftHeadroom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        PlayerMotor motor;
        Collider[] ownColliders;
        float throwPunch;   // first-person hands: seconds left of the throw's forward jab
        readonly Vector3[] fistOffset = new Vector3[2];   // first-person punch pose (PlayerFists)
        readonly float[] fistCurl = new float[2];
        readonly bool[] fistActive = new bool[2];

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

        /// <summary>The player whose body this hand holds (a friend who is out cold), if any.</summary>
        public PlayerNet HeldPlayer(Hand hand)
        {
            var held = HeldBody(hand);
            return held != null ? held.GetComponent<PlayerNet>() : null;
        }

        /// <summary>Does this hand (0 left, 1 right) hold the body of the player with this NetworkObjectId, and where?</summary>
        public bool TryGetBodyGrip(int handIndex, ulong playerId, out int part, out Vector3 local)
        {
            var hand = HandAt(handIndex);
            part = handIndex == 0 ? leftGripPart.Value : rightGripPart.Value;
            local = handIndex == 0 ? leftGripLocal.Value : rightGripLocal.Value;
            return hand != null && HeldId(hand) == playerId && part >= 0;
        }

        /// <summary>Where this player holds things: in front of the eyes, the hand's side out, a little low.</summary>
        public Vector3 HoldPoint(int handIndex, float distance)
        {
            var hand = HandAt(handIndex);
            return viewPosition.Value + viewRotation.Value * new Vector3(hand != null ? hand.SideOffset : 0f, -0.15f, distance);
        }

        /// <summary>A held body's grip in the world (on this peer's copy of the bone), if this hand holds one.</summary>
        bool TryGetBodyGripPoint(Hand hand, out Vector3 point)
        {
            point = default;
            var player = HeldPlayer(hand);
            if (player == null || player.body == null) return false;
            int part = hand.Index == 0 ? leftGripPart.Value : rightGripPart.Value;
            if (part < 0 || part >= player.body.PartCount) return false;
            point = player.body.PartBody(part).transform.TransformPoint(hand.Index == 0 ? leftGripLocal.Value : rightGripLocal.Value);
            return true;
        }

        /// <summary>
        /// Where a hand holds its object, for the body's arm to reach to: the exact grip on the owner, the
        /// nearest point on the object to `from` (the shoulder) on other peers, which do not know the grip.
        /// </summary>
        public bool TryGetGrip(Hand hand, Vector3 from, out Vector3 point)
        {
            point = default;
            var held = HeldBody(hand);
            if (held == null) return false;
            if (TryGetBodyGripPoint(hand, out point)) return true;
            if (IsOwner)
            {
                point = held.transform.TransformPoint(hand.LocalGrip);
                return true;
            }
            float best = float.MaxValue;
            foreach (var c in held.GetComponentsInChildren<Collider>())
            {
                bool exact = !(c is MeshCollider mesh) || mesh.convex;
                Vector3 p = exact ? c.ClosestPoint(from) : c.bounds.ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; point = p; }
            }
            if (best == float.MaxValue) point = held.worldCenterOfMass;
            return true;
        }

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
            var theirs = new List<Collider>(body.GetComponentsInChildren<Collider>());
            // A friend's body lives outside their player object: you do not trip over the man you drag.
            var player = body.GetComponent<PlayerNet>();
            if (player != null && player.body != null)
                for (int i = 0; i < player.body.PartCount; i++)
                    theirs.AddRange(player.body.PartBody(i).GetComponentsInChildren<Collider>());
            foreach (var mine in ownColliders)
            foreach (var other in theirs)
                if (mine != null && other != null) Physics.IgnoreCollision(mine, other, ignore);
        }

        // ---- owner ---------------------------------------------------------------------------

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            PublishView();

            var fallCamera = GetComponent<PossumCamera>();
            bool cameraReady = fallCamera == null || !fallCamera.IsTransitioning;
            bool canAct = cameraReady && (motor == null || !motor.IsRagdolled) && Cursor.lockState == CursorLockMode.Locked;
            if (canAct && RkInput.GrabPressed) ToggleGrab();

            bool holding = IsHolding(Left) || IsHolding(Right);
            if (holding && RkInput.ThrowHeld)
                ThrowCharge = Mathf.Min(1f, ThrowCharge + Time.deltaTime / throwChargeSeconds);
            if (RkInput.ThrowReleased)
            {
                if (holding && ThrowCharge > 0f)
                {
                    ThrowRpc(ThrowCharge, viewCamera.transform.forward);
                    throwCount.Value++;
                    throwPunch = 0.25f;
                }
                ThrowCharge = 0f;
            }
            if (!holding) ThrowCharge = 0f;
            byte shared = (byte)Mathf.RoundToInt(ThrowCharge * 255f);
            if (Mathf.Abs(shared - throwChargeShared.Value) > 12 || (shared == 0) != (throwChargeShared.Value == 0))
                throwChargeShared.Value = shared;
            throwPunch = Mathf.Max(0f, throwPunch - Time.deltaTime);

            UpdateLoad();
            UpdateVisual(Left);
            UpdateVisual(Right);
        }

        void PublishView()
        {
            // Network aiming stays at the eyes even while the presentation camera pulls back.
            Transform view = motor != null && motor.cameraPivot != null ? motor.cameraPivot : viewCamera.transform;
            if ((viewPosition.Value - view.position).sqrMagnitude > 0.0001f) viewPosition.Value = view.position;
            if (Quaternion.Angle(viewRotation.Value, view.rotation) > 0.2f) viewRotation.Value = view.rotation;
        }

        /// <summary>E: both hands take hold of what is under the crosshair, about a shoulder's width apart; E again lets go.</summary>
        void ToggleGrab()
        {
            if (IsHolding(Left) || IsHolding(Right))
            {
                ReleaseAllRpc();
                return;
            }
            if (TryGrabBody()) return;
            if (!FindGrab(out var prop, out var networkObject, out var hit)) return;
            float holdDistance = Mathf.Clamp(hit.distance, minHoldDistance, maxHoldDistance);
            foreach (var hand in new[] { Left, Right })
            {
                Vector3 grip = PointOn(prop, hit.point + viewCamera.transform.right * (hand.SideOffset * 0.8f), hit.point);
                hand.LocalGrip = prop.transform.InverseTransformPoint(grip);
                GrabRpc(hand.Index, networkObject, hand.LocalGrip, holdDistance);
            }
        }

        /// <summary>E on a friend who is out cold: both hands take hold of the bone under the crosshair.</summary>
        bool TryGrabBody()
        {
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (System.Array.IndexOf(ownColliders, hit.collider) >= 0) continue;
                var knockout = PlayerKnockout.Of(hit.collider);
                if (knockout == null || knockout.gameObject == gameObject || !knockout.IsUnconscious || knockout.Player.body == null) return false;
                var ragdoll = knockout.Player.body;
                int part = hit.rigidbody != null ? ragdoll.PartIndexOf(hit.rigidbody) : -1;
                if (part < 0) part = ragdoll.PartIndexOf(ragdoll.Chest);
                var bone = ragdoll.PartBody(part);
                foreach (var hand in new[] { Left, Right })
                {
                    Vector3 wanted = hit.point + viewCamera.transform.right * (hand.SideOffset * 0.4f);
                    Vector3 grip = hit.point;
                    float bestDistance = float.MaxValue;
                    foreach (var c in bone.GetComponentsInChildren<Collider>())
                    {
                        if (c.attachedRigidbody != bone) continue;
                        Vector3 p = c.ClosestPoint(wanted);
                        if ((p - wanted).sqrMagnitude < bestDistance) { bestDistance = (p - wanted).sqrMagnitude; grip = p; }
                    }
                    GrabBodyRpc(hand.Index, knockout.NetworkObject, part, bone.transform.InverseTransformPoint(grip));
                }
                return true;
            }
            return false;
        }

        /// <summary>Test hook: press E.</summary>
        public void DebugToggleGrab()
        {
            if (IsSpawned && IsOwner) ToggleGrab();
        }

        void TryGrab(Hand hand)
        {
            if (!FindGrab(out var prop, out var networkObject, out var hit)) return;
            hand.LocalGrip = prop.transform.InverseTransformPoint(hit.point);
            GrabRpc(hand.Index, networkObject, hand.LocalGrip, Mathf.Clamp(hit.distance, minHoldDistance, maxHoldDistance));
        }

        /// <summary>The prop under the crosshair within reach, if nothing else is in the way.</summary>
        bool FindGrab(out PhysicsProp prop, out NetworkObject networkObject, out RaycastHit found)
        {
            prop = null;
            networkObject = null;
            found = default;
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (System.Array.IndexOf(ownColliders, hit.collider) >= 0) continue;
                prop = hit.collider.GetComponentInParent<PhysicsProp>();
                networkObject = prop != null ? prop.GetComponent<NetworkObject>() : null;
                found = hit;
                return networkObject != null;   // world geometry or another player blocks the grab
            }
            return false;
        }

        /// <summary>The point on the prop's surface nearest `wanted` (a hand's grip), or `fallback`.</summary>
        static Vector3 PointOn(PhysicsProp prop, Vector3 wanted, Vector3 fallback)
        {
            Vector3 best = fallback;
            float bestDistance = float.MaxValue;
            foreach (var c in prop.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || (c is MeshCollider mesh && !mesh.convex)) continue;
                Vector3 p = c.ClosestPoint(wanted);
                float d = (p - wanted).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            return best;
        }

        /// <summary>Test hook: grab whatever is under the crosshair with hand 0 (left) or 1 (right).</summary>
        public void DebugGrab(int handIndex)
        {
            if (IsSpawned && IsOwner) TryGrab(HandAt(handIndex));
        }

        /// <summary>Server: let go of everything (props are about to be reset).</summary>
        public void ServerReleaseAll()
        {
            if (!IsServer) return;
            ServerRelease(Left);
            ServerRelease(Right);
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

            // Dragging a body that lags behind (snagged, too heavy for one): the pull holds you back, down to
            // a crawl, so you tug it along instead of walking off and losing your grip.
            float stretch = 0f;
            foreach (var hand in new[] { Left, Right })
                if (TryGetBodyGripPoint(hand, out Vector3 grip))
                    stretch = Mathf.Max(stretch, Vector3.Distance(grip, HoldPoint(hand.Index, BodyHoldDistance)));
            if (stretch > 0f) SpeedMultiplier *= Mathf.Clamp(1.4f - stretch * 0.5f, 0.15f, 1f);
        }

        /// <summary>How far in front of the eyes a dragged body's grip is held (PlayerKnockout pulls it there).</summary>
        public const float BodyHoldDistance = 1f;

        /// <summary>First-person punch pose of a hand (PlayerFists): an offset in view space and a wrist curl in degrees.</summary>
        public void SetFistPose(int hand, Vector3 offset, float curl, bool active)
        {
            if (hand < 0 || hand > 1) return;
            fistOffset[hand] = offset;
            fistCurl[hand] = curl;
            fistActive[hand] = active;
        }

        void CreateVisual(Hand hand, string handName)
        {
            GameObject visual;
            if (handModel != null)
            {
                // Sleeve-and-hand model; the right one is a mirror of the left.
                visual = Instantiate(handModel);
                visual.transform.SetParent(viewCamera.transform, false);
                // Keep the import's own axis fix-up and turn it so the sleeve runs back toward the camera.
                visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * handModel.transform.localRotation;
                visual.transform.localScale = new Vector3(hand.Index == 1 ? -1f : 1f, 1f, 1f);
            }
            else
            {
                // Placeholder cartoon hand: a chunky skin-coloured block in the corner of the view.
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(visual.GetComponent<Collider>());
                visual.transform.SetParent(viewCamera.transform, false);
                visual.transform.localScale = new Vector3(0.12f, 0.07f, 0.16f);
                visual.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(new Color(1f, 0.8f, 0.62f));
            }
            visual.name = $"{handName}Hand";
            visual.transform.localPosition = hand.RestOffset;
            hand.VisualRestRotation = visual.transform.localRotation;
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hand.Visual = visual.transform;
        }

        void UpdateVisual(Hand hand)
        {
            if (hand.Visual == null) return;
            Transform view = viewCamera.transform;
            var held = HeldBody(hand);
            Vector3 targetWorld = held == null ? view.TransformPoint(hand.RestOffset)
                : TryGetBodyGripPoint(hand, out Vector3 bodyGrip) ? bodyGrip : held.transform.TransformPoint(hand.LocalGrip);
            Vector3 local = Vector3.ClampMagnitude(view.InverseTransformPoint(targetWorld), 1.2f);
            local.z = Mathf.Max(local.z, 0.3f);
            // Winding up a throw draws the hands back and up; letting go jabs them forward.
            local += new Vector3(0f, 0.07f, -0.2f) * ThrowCharge;
            local += Vector3.forward * (0.3f * Mathf.Sin(Mathf.Clamp01(throwPunch / 0.25f) * Mathf.PI));
            if (held == null) local += fistOffset[hand.Index];
            // A punch has to snap: follow it much more tightly than the idle sway.
            float k = 1f - Mathf.Exp(-(fistActive[hand.Index] ? 60f : 20f) * Time.deltaTime);
            hand.Visual.localPosition = Vector3.Lerp(hand.Visual.localPosition, local, k);
            // Gripping curls the wrist down; a wind-up cocks it back.
            float curl = (held != null ? 20f : 0f) - 35f * ThrowCharge + (held == null ? fistCurl[hand.Index] : 0f);
            Quaternion pose = Quaternion.AngleAxis(curl, Vector3.right) * hand.VisualRestRotation;
            hand.Visual.localRotation = Quaternion.Slerp(hand.Visual.localRotation, pose, k);
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
            CheckBodyGrip(Left);
            CheckBodyGrip(Right);
        }

        /// <summary>Server: let go of a body once its player is up again, or once it is left far behind.</summary>
        void CheckBodyGrip(Hand hand)
        {
            var player = HeldPlayer(hand);
            if (player == null) return;
            var knockout = player.GetComponent<PlayerKnockout>();
            bool far = Vector3.Distance(viewPosition.Value, player.transform.position) > reach + 3f;
            if (knockout != null && knockout.IsUnconscious && !far) return;
            Debug.Log($"Roadkill: {PlayerNet.NameOf(OwnerClientId)} let go of {PlayerNet.NameOf(player.OwnerClientId)}'s body ({(far ? $"left behind, {Vector3.Distance(viewPosition.Value, player.transform.position):0.0} m" : "they are up")})");
            ServerRelease(hand);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void GrabBodyRpc(int handIndex, NetworkObjectReference targetRef, int part, Vector3 localPoint)
        {
            var hand = HandAt(handIndex);
            if (hand == null || !targetRef.TryGet(out NetworkObject target) || target == NetworkObject) return;
            var knockout = target.GetComponent<PlayerKnockout>();
            var ragdoll = knockout != null ? knockout.Player.body : null;
            if (ragdoll == null || !knockout.IsUnconscious || part < 0 || part >= ragdoll.PartCount) return;
            Vector3 point = ragdoll.PartBody(part).transform.TransformPoint(localPoint);
            if (Vector3.Distance(point, viewPosition.Value) > reach + 1.5f) return;   // allow for lag, not for cheats

            ServerRelease(hand);
            hand.HoldDistance = 1f;
            if (hand.Index == 0) { leftGripPart.Value = part; leftGripLocal.Value = localPoint; }
            else { rightGripPart.Value = part; rightGripLocal.Value = localPoint; }
            SetHeld(hand, target.NetworkObjectId);
            Debug.Log($"Roadkill: {PlayerNet.NameOf(OwnerClientId)} grabbed {PlayerNet.NameOf(target.OwnerClientId)}'s body with hand {handIndex}");
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
            var bodies = new HashSet<PlayerKnockout>();
            foreach (var hand in new[] { Left, Right })
            {
                var player = HeldPlayer(hand);
                if (player != null) bodies.Add(player.GetComponent<PlayerKnockout>());
            }
            ServerRelease(Left);
            ServerRelease(Right);

            float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Mathf.Clamp01(charge)) * PlaygroundRules.ThrowScale;
            Vector3 aim = (direction.normalized + Vector3.up * 0.15f).normalized;
            foreach (var body in thrown)
            {
                if (HolderCount(body) > 0) continue;   // a friend still has it: you only let go
                float massFactor = Mathf.Clamp(fullSpeedThrowMass * PlaygroundRules.ThrowMassScale / body.mass, 0.15f, 1f);
                body.AddForce(aim * speed * massFactor, ForceMode.VelocityChange);
            }
            // A friend's body flies on its owner's machine, as heavy as it is to carry.
            foreach (var knockout in bodies)
            {
                if (knockout == null || HolderCount(knockout.GetComponent<Rigidbody>()) > 0) continue;
                float massFactor = Mathf.Clamp(fullSpeedThrowMass * PlaygroundRules.ThrowMassScale / knockout.cargoMass, 0.15f, 1f);
                knockout.ServerThrow(aim * speed * Mathf.Max(massFactor, 0.35f));
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
                maximumForce = capacityPerHandKg * PlaygroundRules.CarryScale * -Physics.gravity.y * LiftHeadroom
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
            var part = hand.Index == 0 ? leftGripPart : rightGripPart;
            if (part.Value != -1) part.Value = -1;
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
