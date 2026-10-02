using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Shows the selected hotbar tool in the player's hand and carries tool hits to the server.
    ///
    /// Owner: the tool sits in front of the camera where the right hand rests, and the right hand grips
    /// it. Everyone else: the same tool hangs off the character's right hand bone, picked from the
    /// catalog by a networked index, and swings when the owner swings. Hits on props go to the server
    /// (it alone simulates them); hitting another player knocks them down.
    /// </summary>
    public class PlayerTools : NetworkBehaviour
    {
        [SerializeField] ShopCatalog catalog;
        [SerializeField] HotbarInventory hotbar;
        [SerializeField] Camera viewCamera;
        [Tooltip("Where the held tool's grip sits in camera space (the right hand's rest spot).")]
        [SerializeField] Vector3 heldOffset = new Vector3(0.3f, -0.32f, 0.55f);
        [SerializeField] Vector3 placeholderEuler = new Vector3(60f, 0f, 0f);
        [SerializeField] string handBone = "Hand_R";

        [Header("Hit rules (checked on the server)")]
        [SerializeField] float maxHitDistance = 4f;
        [Tooltip("Caps the speed a hit can give a prop, m/s.")]
        [SerializeField] float maxLaunchSpeed = 16f;
        [SerializeField] bool knockDownPlayers = true;
        [SerializeField] float playerDowntime = 1.2f;
        [SerializeField] float playerDamage = 10f;
        [SerializeField] float playerKick = 4f;

        // Catalog index of what is in hand, -1 for nothing. Written by the owner.
        readonly NetworkVariable<int> heldIndex = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        PlayerNet net;
        PlayerMotor motor;
        Transform socket;
        GameObject heldVisual;
        BatTool bat;
        int shownIndex = -1;

        /// <summary>Holding a tool takes the hands: no grabbing until an empty slot is picked.</summary>
        public bool BlocksGrab => IsOwner && heldVisual != null;
        /// <summary>Where the owner's right hand should sit, or null to leave it at rest.</summary>
        public Transform GripPoint => IsOwner && heldVisual != null && socket.gameObject.activeSelf ? socket : null;
        public Vector3 ViewForward => viewCamera != null ? viewCamera.transform.forward : transform.forward;
        public Vector3 ViewOrigin => viewCamera != null ? viewCamera.transform.position : transform.position + Vector3.up * 1.6f;

        public bool CanUseTool => Cursor.lockState == CursorLockMode.Locked && ToolOut();

        public void Setup(ShopCatalog shopCatalog, HotbarInventory inventory, Camera view)
        {
            catalog = shopCatalog;
            hotbar = inventory;
            viewCamera = view;
        }

        void Awake()
        {
            net = GetComponent<PlayerNet>();
            motor = GetComponent<PlayerMotor>();
        }

        public override void OnNetworkSpawn()
        {
            heldIndex.OnValueChanged += OnHeldChanged;
            if (IsOwner)
            {
                hotbar.Changed += OnHotbarChanged;
                OnHotbarChanged();
            }
            else
            {
                Show(heldIndex.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            heldIndex.OnValueChanged -= OnHeldChanged;
            if (hotbar != null) hotbar.Changed -= OnHotbarChanged;
        }

        void Update()
        {
            // Put the tool away while down, like the first-person hands.
            if (IsOwner && socket != null) socket.gameObject.SetActive(ToolOut());
        }

        /// <summary>Standing and looking through the eyes (not down, not in the fall camera).</summary>
        bool ToolOut()
        {
            if (motor != null && motor.IsRagdolled) return false;
            var fallCamera = GetComponent<PossumCamera>();
            return fallCamera == null || !fallCamera.IsTransitioning;
        }

        // ---- held tool -----------------------------------------------------------------------

        void OnHotbarChanged()
        {
            int index = catalog != null ? catalog.IndexOf(hotbar.SelectedItem) : -1;
            if (heldIndex.Value != index) heldIndex.Value = index;
            Show(index);
        }

        void OnHeldChanged(int previous, int current)
        {
            if (!IsOwner) Show(current);
        }

        void Show(int index)
        {
            if (index == shownIndex) return;
            shownIndex = index;
            if (heldVisual != null) Destroy(heldVisual);
            heldVisual = null;
            bat = null;

            var data = catalog != null ? catalog.Get(index) : null;
            if (data == null) return;
            EnsureSocket();
            if (data.HeldPrefab != null)
            {
                heldVisual = Instantiate(data.HeldPrefab, socket, false);
                heldVisual.name = data.DisplayName;
            }
            else
            {
                heldVisual = data.CreatePlaceholder(socket);
                heldVisual.transform.localRotation = Quaternion.Euler(placeholderEuler);
            }

            if (IsOwner)
            {
                foreach (var r in heldVisual.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            bat = heldVisual.GetComponent<BatTool>();
            if (bat != null) bat.Init(this, IsOwner);
        }

        void EnsureSocket()
        {
            if (socket != null) return;
            socket = new GameObject("ToolSocket").transform;
            if (IsOwner)
            {
                socket.SetParent(viewCamera.transform, false);
                socket.localPosition = heldOffset;
                return;
            }

            // On the character's hand, turned to face where the player faces, at world scale 1
            // whatever scale the imported bones carry.
            var bone = FindDeep(transform, handBone);
            if (bone == null)
            {
                socket.SetParent(transform, false);
                socket.localPosition = new Vector3(0.3f, 1.1f, 0.3f);
                return;
            }
            socket.SetParent(bone, false);
            socket.SetPositionAndRotation(bone.position, transform.rotation);
            socket.localScale = Vector3.one / Mathf.Max(0.0001f, bone.lossyScale.x);
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ---- swings and hits -----------------------------------------------------------------

        /// <summary>Owner: play the swing on everyone else's copy of this player.</summary>
        public void BroadcastSwing() => SwingRpc();

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        void SwingRpc()
        {
            if (bat != null) bat.PlaySwing();
        }

        /// <summary>
        /// Owner: a tool touched this collider. Works out what was hit (a prop, another player or a loose
        /// local body), skips our own body and anything already hit this swing, and applies the hit.
        /// </summary>
        public void ReportHit(Collider collider, Vector3 point, Vector3 impulse, Vector3 spin, HashSet<Object> alreadyHit, bool log)
        {
            var player = collider.GetComponentInParent<PlayerNet>();
            if (player == null)
            {
                var rig = collider.GetComponentInParent<RagdollRig>();   // a detached ragdoll body
                if (rig != null) player = rig.Player;
            }
            if (player != null)
            {
                if (player == net || !alreadyHit.Add(player)) return;
                if (log) Debug.Log($"Roadkill: bat hit {player.name}");
                HitPlayerRpc(player.NetworkObject, impulse);
                return;
            }

            var body = collider.attachedRigidbody;
            if (body == null) return;   // static world
            var networkObject = body.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                if (!alreadyHit.Add(networkObject)) return;
                if (log) Debug.Log($"Roadkill: bat hit {body.name} ({body.mass:0} kg)");
                HitPropRpc(networkObject, point, impulse, spin);
            }
            else if (!body.isKinematic)
            {
                if (!alreadyHit.Add(body)) return;
                if (log) Debug.Log($"Roadkill: bat hit local body {body.name}");
                ApplyHit(body, point, impulse, spin);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitPropRpc(NetworkObjectReference targetRef, Vector3 point, Vector3 impulse, Vector3 spin)
        {
            if (!targetRef.TryGet(out NetworkObject target)) return;
            var body = target.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) return;
            // Allow for lag, not for hitting things across the map.
            if (Vector3.Distance(point, transform.position + Vector3.up) > maxHitDistance) return;
            if (Vector3.Distance(body.ClosestPointOnBounds(point), point) > 1.5f) return;
            ApplyHit(body, point, impulse, spin);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitPlayerRpc(NetworkObjectReference targetRef, Vector3 impulse)
        {
            if (!knockDownPlayers || !targetRef.TryGet(out NetworkObject target)) return;
            var victim = target.GetComponent<PlayerNet>();
            if (victim == null || victim == net) return;
            if (Vector3.Distance(victim.transform.position, transform.position) > maxHitDistance + 1f) return;
            Vector3 flat = Vector3.ProjectOnPlane(impulse, Vector3.up).normalized;
            victim.ServerKnockdown(playerDowntime, flat * playerKick + Vector3.up * 1.5f, playerDamage);
        }

        void ApplyHit(Rigidbody body, Vector3 point, Vector3 impulse, Vector3 spin)
        {
            float speed = impulse.magnitude / body.mass;
            if (speed > maxLaunchSpeed) impulse *= maxLaunchSpeed / speed;
            body.WakeUp();
            body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            // Light things tumble; an engine block barely turns.
            float tumble = Mathf.Clamp01(impulse.magnitude / body.mass / 6f);
            body.AddTorque(spin * tumble, ForceMode.VelocityChange);
        }
    }
}
