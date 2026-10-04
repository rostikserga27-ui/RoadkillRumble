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
    /// catalog by a networked index, and swings or fires when the owner does. Hits on props go to the
    /// server (it alone simulates them); a bat knocks other players down, a revolver shot is checked by the
    /// server against the gun's own numbers and hurts them (friendly-fire rules as for punches).
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
        RevolverTool revolver;
        int shownIndex = -1;
        float lastServerShot = -99f;
        readonly RaycastHit[] shotHits = new RaycastHit[24];

        /// <summary>Holding a tool takes the hands: no grabbing until an empty slot is picked.</summary>
        public bool BlocksGrab => IsOwner && heldVisual != null;
        /// <summary>A gun is out: R reloads instead of respawning (PlayerMotor).</summary>
        public bool HoldsGun => IsOwner && revolver != null && ToolOut();
        public RevolverTool Revolver => revolver;
        /// <summary>Where the owner's right hand should sit, or null to leave it at rest.</summary>
        public Transform GripPoint => IsOwner && heldVisual != null && socket.gameObject.activeSelf ? (revolver != null ? revolver.Grip : socket) : null;
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
            revolver = null;

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
            revolver = heldVisual.GetComponent<RevolverTool>();
            if (revolver != null) revolver.Init(this, IsOwner);
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

        // ---- shots ----------------------------------------------------------------------------

        /// <summary>Owner: kick the view up (and a little sideways) after a shot.</summary>
        public void Recoil(float pitchUp, float yaw)
        {
            if (motor != null) motor.AddRecoil(pitchUp, yaw);
        }

        public void BroadcastReload() => ReloadRpc();

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        void ReloadRpc()
        {
            if (revolver != null) revolver.PlayReload();
        }

        /// <summary>
        /// Owner: the first thing a bullet from the eyes meets, skipping our own capsule and body. A hit on
        /// another player's capsule gives way to one of their bones just behind it, so head shots count.
        /// </summary>
        public bool FindShotTarget(Vector3 origin, Vector3 direction, float range, out RaycastHit found)
        {
            found = default;
            int n = Physics.RaycastNonAlloc(origin, direction, shotHits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (shotHits[i].distance >= best || IsOwn(shotHits[i].collider)) continue;
                best = shotHits[i].distance;
                found = shotHits[i];
            }
            if (best == float.MaxValue) return false;

            var capsuleOwner = found.collider.GetComponent<RagdollBodyPart>() == null ? found.collider.GetComponentInParent<PlayerNet>() : null;
            if (capsuleOwner != null)
            {
                float boneBest = best + 1f;
                for (int i = 0; i < n; i++)
                {
                    var part = shotHits[i].collider.GetComponentInParent<RagdollBodyPart>();
                    if (part == null || part.Player != capsuleOwner || shotHits[i].distance >= boneBest) continue;
                    boneBest = shotHits[i].distance;
                    found = shotHits[i];
                }
            }
            return true;
        }

        bool IsOwn(Collider collider)
        {
            if (collider.transform.IsChildOf(transform)) return true;
            var part = collider.GetComponentInParent<RagdollBodyPart>();
            return part != null && part.Player == net;
        }

        /// <summary>Owner: a shot was fired (the flash already played here). Show it to everyone else and claim the hit.</summary>
        public void ReportShot(RevolverTool gun, Vector3 origin, Vector3 direction, Collider hit, Vector3 point, Vector3 normal)
        {
            ShotFxRpc(point, normal, hit != null);
            if (hit == null) return;

            var part = hit.GetComponentInParent<RagdollBodyPart>();
            var player = part != null ? part.Player : hit.GetComponentInParent<PlayerNet>();
            if (player != null)
            {
                if (player == net) return;
                int index = part != null && player.body != null ? player.body.PartIndexOf(hit.attachedRigidbody) : -1;
                ShootPlayerRpc(player.NetworkObject, index, origin, point, direction);
                return;
            }
            var body = hit.attachedRigidbody;
            if (body == null) return;   // static world: the sparks were all
            var networkObject = body.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned) ShootPropRpc(networkObject, origin, point, direction);
            else if (!body.isKinematic) ApplyHit(body, point, direction.normalized * gun.propImpulse, Vector3.zero);
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        void ShotFxRpc(Vector3 end, Vector3 normal, bool impact)
        {
            if (revolver != null) revolver.PlayShot(end, normal, impact);
        }

        /// <summary>Server: the gun this player has out, as its prefab (the numbers are read from there, not from the client).</summary>
        RevolverTool ServerGun()
        {
            var data = catalog != null ? catalog.Get(heldIndex.Value) : null;
            return data != null && data.HeldPrefab != null ? data.HeldPrefab.GetComponent<RevolverTool>() : null;
        }

        /// <summary>Server: is this a shot the player could have fired (gun out, not too fast, from their eyes, in range)?</summary>
        bool ServerShotOk(RevolverTool gun, Vector3 origin, Vector3 point)
        {
            if (gun == null || Time.time - lastServerShot < gun.fanInterval * 0.5f) return false;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2.5f) return false;
            if (Vector3.Distance(origin, point) > gun.range + 1f) return false;
            lastServerShot = Time.time;
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ShootPropRpc(NetworkObjectReference targetRef, Vector3 origin, Vector3 point, Vector3 direction)
        {
            var gun = ServerGun();
            if (!targetRef.TryGet(out NetworkObject target) || !ServerShotOk(gun, origin, point)) return;
            var body = target.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || Vector3.Distance(body.ClosestPointOnBounds(point), point) > 1.5f) return;
            // A little lift, so a shot from above knocks a prop away instead of pressing it into the ground.
            Vector3 push = (direction.normalized + Vector3.up * 0.35f).normalized;
            ApplyHit(body, point, push * gun.propImpulse, Random.insideUnitSphere * 3f);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ShootPlayerRpc(NetworkObjectReference targetRef, int part, Vector3 origin, Vector3 point, Vector3 direction)
        {
            var gun = ServerGun();
            if (!targetRef.TryGet(out NetworkObject target) || !ServerShotOk(gun, origin, point)) return;
            var victim = target.GetComponent<PlayerNet>();
            if (victim == null || victim == net || Vector3.Distance(victim.transform.position, point) > 3f) return;

            var c = PunchConfig.Current;
            var ragdoll = victim.body;
            if (ragdoll == null || part >= ragdoll.PartCount) part = -1;
            bool head = part >= 0 && ragdoll.PartRole(part) == ActiveRagdollController.Role.Head;
            float damage = gun.damage * (head ? gun.headMultiplier : 1f);
            float knockdown = head ? gun.headKnockdownSeconds : 0f;
            float push = 1f;
            if (FriendlyFire.AreTeammates(net, victim))
            {
                if (!FriendlyFire.Enabled)
                {
                    ImpactRpc(target, part, point, Vector3.zero, head, true);
                    return;
                }
                if (FriendlyFire.IsProtected(victim.OwnerClientId))
                {
                    damage = 0f;
                    knockdown = 0f;
                    push = 0.25f;
                }
                else
                {
                    damage = FriendlyFire.ServerCapDamage(OwnerClientId, victim.OwnerClientId, damage * c.friendlyDamageMultiplier);
                    push = c.friendlyKnockbackMultiplier;
                }
            }

            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero;
            Vector3 shove = flat * (gun.playerShove * push);
            Vector3 kick = flat * (3f * push) + Vector3.up * 1.5f;
            var reaction = victim.GetComponent<PunchReaction>();
            if (reaction != null) reaction.ServerReact(damage, knockdown, kick, shove, OwnerClientId, head ? 1f : 0.5f, "shot");
            ImpactRpc(target, part, point, direction.normalized * (gun.boneImpulse * push), head, false);
            Debug.Log($"Roadkill: {PlayerNet.NameOf(OwnerClientId)} shot {PlayerNet.NameOf(victim.OwnerClientId)} " +
                      $"({(head ? "head" : part >= 0 ? ragdoll.PartRole(part).ToString() : "body")}): {damage:0} dmg" +
                      $"{(knockdown > 0f ? $", down {knockdown:0.0} s" : "")}");
        }

        /// <summary>Every peer: a bullet struck this player: knock the bone, puff, sound, a word.</summary>
        [Rpc(SendTo.Everyone)]
        void ImpactRpc(NetworkObjectReference targetRef, int part, Vector3 point, Vector3 impulse, bool head, bool harmless)
        {
            if (!targetRef.TryGet(out NetworkObject target)) return;
            var reaction = target.GetComponent<PunchReaction>();
            if (reaction != null && impulse.sqrMagnitude > 0f) reaction.ApplyToBody(part, point, impulse, 0.3f, 0.6f);
            var c = PunchConfig.Current;
            Vector3 back = impulse.sqrMagnitude > 0f ? -impulse.normalized : Vector3.up;
            PunchFx.Sparks(point, back, harmless ? c.friendlySparkColor : new Color(1f, 0.3f, 0.25f), harmless ? 0.2f : head ? 1f : 0.6f);
            if (harmless) PunchFx.PlayHarmless(point);
            else PunchFx.PlayImpact(point, head ? 1f : 0.5f, false);
            PunchFx.Popup(point + Vector3.up * 0.3f, harmless ? "PLINK" : head ? "HEADSHOT!" : "OW!",
                harmless ? c.friendlySparkColor : new Color(1f, 0.85f, 0.3f), head ? 1.3f : 0.9f);
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
