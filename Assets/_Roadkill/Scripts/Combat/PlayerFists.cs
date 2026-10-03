using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Fists (GDD phase 1, step 10): the left mouse button punches with whichever hand is free, alternating;
    /// a tap is a jab, holding winds up a haymaker. A target under the crosshair within punchRange is "in
    /// reach" (the HUD shows it) and the punch steps in to land on it. The punch is physical: PunchArmDriver swings the active ragdoll's own
    /// arm on every peer, and hits are the fist's real collisions.
    ///
    /// Networking, in the same spirit as the rest of the game (the owner moves itself, the server decides
    /// what hurts):
    ///   1. The owner winds up and strikes at once on its own copy (no input lag) and tells the others
    ///      (WindUpRpc, relayed by the server) and the server (StrikeRpc).
    ///   2. The server checks the strike (cooldown, stamina, charge against the time actually held),
    ///      remembers it and has everyone else play it on their copy of the body (PlayStrikeRpc).
    ///   3. The owner's copy is the one that counts for hits: what you see your fist touch is what you hit.
    ///      Its contacts become hit claims (HitClaimRpc) and the owner predicts the feel (hit-stop, camera
    ///      kick, sparks, the victim's flinch) right away.
    ///   4. The server validates each claim (a strike it accepted, still live, each target once, a plausible
    ///      speed, contact near both players) and resolves it (PunchHitResolver): damage and knockdown go to
    ///      the victim's owner (PunchReaction), props are pushed in the server's physics, and the impulse
    ///      and effects go to everyone (HitFxRpc).
    /// Body physics stay local on every peer, as they already were: each peer applies the same impulse to
    /// its own copy of the struck bone, and a downed body is shown from its owner (RagdollPoseSync), so
    /// copies never drift apart for long.
    /// </summary>
    public class PlayerFists : NetworkBehaviour
    {
        class Fist
        {
            public int Index;
            public PunchArmDriver.Phase Phase;
            public float Time;              // in this phase, frozen during hit-stop
            public float Charge;
            public bool Released;
            public float StrikeSeconds;
            public int Sequence;
            public readonly HashSet<ulong> Hit = new HashSet<ulong>();
            public float HitStop;
            public Vector3 PoseOffset;      // first-person pose, kept for blending between phases
            public float PoseCurl;
            public Vector3 PhaseStartOffset;
            public float PhaseStartCurl;
        }

        class StrikeRecord
        {
            public float Time;
            public float Live;
            public float Charge;
            public int Hand;
            public readonly HashSet<ulong> Targets = new HashSet<ulong>();
        }

        [System.Flags]
        public enum HitFlags : byte { None = 0, Friendly = 1, Harmless = 2, Protected = 4, Comedic = 8, Knockdown = 16, Prop = 32, Capped = 64 }

        public PlayerNet Player { get; private set; }
        public PunchArmDriver Driver { get; private set; }
        /// <summary>0..1, valid on the owner.</summary>
        public float Stamina01 => config != null ? stamina / config.maxStamina : 1f;
        /// <summary>The charge of a fist being wound up (owner), for the HUD.</summary>
        public float Charge { get; private set; }
        /// <summary>Owner: a player, prop or NPC under the crosshair within punching range.</summary>
        public bool TargetInRange { get; private set; }
        public string TargetName { get; private set; } = "";
        /// <summary>Tried to punch without the stamina for it, just now.</summary>
        public bool Tired => Time.time - tiredTime < 0.8f;

        static readonly List<PlayerFists> All = new List<PlayerFists>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        PunchConfig config;
        HandsController hands;
        PunchReaction reaction;
        readonly Fist[] fists = { new Fist { Index = 0 }, new Fist { Index = 1 } };
        int nextHand = 1;          // right first
        bool debugPress;           // test hook: a press of F, then F held for debugHold seconds
        float debugHold, debugUntil = -99f;
        int sequence;
        float stamina;
        float lastSpend = -99f, lastStrike = -99f, tiredTime = -99f;
        bool cursorWasLocked;      // the click that locks the cursor is not a punch
        Vector3 targetPoint;
        // Hits the owner already played locally, so the server's echo does not play them twice.
        readonly List<(int sequence, ulong target, float time)> predicted = new List<(int, ulong, float)>();

        // Server
        readonly Dictionary<int, StrikeRecord> strikes = new Dictionary<int, StrikeRecord>();
        readonly float[] serverWindUp = { -99f, -99f };
        float serverStamina, serverStaminaTime, serverLastSpend = -99f, serverLastStrike = -99f;
        int serverSequence;

        void Awake()
        {
            Player = GetComponent<PlayerNet>();
            hands = GetComponent<HandsController>();
            reaction = GetComponent<PunchReaction>();
        }

        public override void OnNetworkSpawn()
        {
            config = PunchConfig.Current;
            stamina = serverStamina = config.maxStamina;
            serverStaminaTime = Time.time;
            if (Player.body != null)
            {
                Driver = Player.body.gameObject.AddComponent<PunchArmDriver>();
                Driver.Initialize(Player.body, config);
                Driver.FistContact += OnFistContact;
            }
            else
            {
                Debug.LogWarning("Roadkill: this player has no active-ragdoll body; fists are off.");
            }
            foreach (var other in All)
            {
                IgnoreFistsVersusCapsule(this, other);
                IgnoreFistsVersusCapsule(other, this);
            }
            All.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Driver != null) Driver.FistContact -= OnFistContact;
        }

        /// <summary>
        /// Fists hit bodies, not the invisible gameplay capsule around them (which would stop the fist a hand
        /// short of the face and say nothing about where it landed).
        /// </summary>
        static void IgnoreFistsVersusCapsule(PlayerFists puncher, PlayerFists other)
        {
            if (puncher == other || puncher.Driver == null) return;
            var capsule = other.GetComponent<CapsuleCollider>();
            if (capsule == null) return;
            for (int hand = 0; hand < 2; hand++)
                foreach (var c in puncher.Driver.FistColliders(hand))
                    Physics.IgnoreCollision(c, capsule, true);
        }

        // =========================================================================== owner

        void Update()
        {
            if (!IsSpawned || !IsOwner || Driver == null) return;
            float dt = Time.deltaTime;
            RegenerateStamina(ref stamina, ref lastSpend, Time.time - dt, Time.time);

            var fallCamera = Player.FallCamera;
            bool testing = Time.time < debugUntil;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool canAct = ((locked && cursorWasLocked) || testing) && !Player.Motor.IsRagdolled
                          && (fallCamera == null || !fallCamera.IsTransitioning);
            cursorWasLocked = locked;
            SenseTarget();
            if ((RkInput.PunchPressed || debugPress) && canAct) StartWindUp();
            debugPress = false;
            debugHold -= dt;
            bool punchHeld = RkInput.PunchHeld || debugHold > 0f;

            Charge = 0f;
            foreach (var fist in fists)
            {
                if (fist.Phase == PunchArmDriver.Phase.WindUp && (!canAct || hands.IsHolding(HandOf(fist.Index))))
                {
                    CancelFist(fist);   // knocked over, or the hand grabbed something instead
                    continue;
                }
                if (fist.HitStop > 0f)
                {
                    fist.HitStop -= dt;
                    continue;   // the first-person fist freezes on the hit
                }
                fist.Time += dt;
                switch (fist.Phase)
                {
                    case PunchArmDriver.Phase.WindUp:
                        if (!punchHeld) fist.Released = true;
                        float affordable = Mathf.Clamp01(Mathf.InverseLerp(config.jabCost, config.haymakerCost, stamina));
                        fist.Charge = Mathf.Min(affordable, Mathf.Clamp01((fist.Time - config.minWindUpSeconds) / Mathf.Max(0.01f, config.chargeSeconds)));
                        Driver.SetCharge(fist.Index, fist.Charge);
                        Charge = Mathf.Max(Charge, fist.Charge);
                        bool ready = fist.Time >= config.minWindUpSeconds && Time.time - lastStrike >= config.cooldownSeconds;
                        if (ready && (fist.Released || fist.Time >= config.maxHoldSeconds)) Strike(fist);
                        break;
                    case PunchArmDriver.Phase.Strike:
                        if (fist.Time >= fist.StrikeSeconds) EnterPhase(fist, PunchArmDriver.Phase.Recovery);
                        break;
                    case PunchArmDriver.Phase.Recovery:
                        if (fist.Time >= config.recoverySeconds) EnterPhase(fist, PunchArmDriver.Phase.Idle);
                        break;
                }
            }
            UpdateFirstPersonPose();
            for (int i = predicted.Count - 1; i >= 0; i--)
                if (Time.time - predicted[i].time > 3f) predicted.RemoveAt(i);
        }

        HandsController.Hand HandOf(int index) => index == 0 ? hands.Left : hands.Right;

        /// <summary>Test hook: punch as if F were pressed and held for `holdSeconds` (0 = a jab).</summary>
        public void DebugPunch(float holdSeconds)
        {
            if (!IsSpawned || !IsOwner) return;
            debugPress = true;
            debugHold = holdSeconds;
            debugUntil = Time.time + holdSeconds + 1.5f;
        }

        void StartWindUp()
        {
            if (stamina < config.jabCost)
            {
                tiredTime = Time.time;
                return;
            }
            foreach (int candidate in new[] { nextHand, 1 - nextHand })
            {
                var fist = fists[candidate];
                bool free = fist.Phase == PunchArmDriver.Phase.Idle || fist.Phase == PunchArmDriver.Phase.Recovery;
                if (!free || hands.IsHolding(HandOf(candidate))) continue;
                if (fists[1 - candidate].Phase == PunchArmDriver.Phase.WindUp) return;   // one wind-up at a time
                nextHand = 1 - candidate;
                EnterPhase(fist, PunchArmDriver.Phase.WindUp);
                fist.Charge = 0f;
                fist.Released = false;
                Driver.BeginWindUp(candidate);
                if (IsServer) serverWindUp[candidate] = Time.time;
                WindUpRpc(candidate);
                return;
            }
        }

        void Strike(Fist fist)
        {
            stamina = Mathf.Max(0f, stamina - config.Cost(fist.Charge));
            lastSpend = lastStrike = Time.time;
            fist.Sequence = ++sequence;
            fist.StrikeSeconds = config.StrikeSeconds(fist.Charge) + config.StepIn(fist.Charge);
            fist.Hit.Clear();
            EnterPhase(fist, PunchArmDriver.Phase.Strike);

            Vector3 aimPoint = AimPoint();
            Driver.BeginStrike(fist.Index, fist.Charge, aimPoint, fist.Sequence);
            StrikeRpc(fist.Index, fist.Charge, aimPoint, fist.Sequence);
            Vector3 aim = aimPoint - Player.Motor.cameraPivot.position;
            PunchFx.PlayWhoosh(Driver.Body.HandBody(fist.Index).position, fist.Charge);

            // Step into it: a target in range but beyond arm's length is dashed at (the arm waits for the body,
            // PunchArmDriver); otherwise a little step for a jab, a real lunge for a haymaker.
            if (Player.Motor.IsGrounded)
            {
                float gap = Vector3.ProjectOnPlane(targetPoint - transform.position, Vector3.up).magnitude;
                Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up);
                if (TargetInRange && gap > config.comfortableDistance + 0.05f)
                    Player.Motor.Dash(targetPoint, config.comfortableDistance, config.approachSpeed, config.maxApproachSeconds);
                else if (flat.sqrMagnitude > 0.01f)
                    Player.Motor.Shove(flat.normalized * (config.lungeSpeed * Mathf.Lerp(config.jabLungeShare, 1f, fist.Charge)), 0.15f);
            }
        }

        void CancelFist(Fist fist)
        {
            EnterPhase(fist, PunchArmDriver.Phase.Idle);
            Driver.Cancel(fist.Index);
            CancelRpc(fist.Index);
        }

        void EnterPhase(Fist fist, PunchArmDriver.Phase phase)
        {
            fist.Phase = phase;
            fist.Time = 0f;
            fist.HitStop = 0f;
            fist.PhaseStartOffset = fist.PoseOffset;
            fist.PhaseStartCurl = fist.PoseCurl;
        }

        /// <summary>
        /// What is under the crosshair, from the eyes (not the shaking camera): the nearest thing within
        /// punchRange that is not this player. A player, prop or NPC is a target in reach.
        /// </summary>
        void SenseTarget()
        {
            Transform eyes = Player.Motor.cameraPivot;
            Vector3 look = ClampAim(eyes.forward);
            RaycastHit nearest = default;
            float nearestDistance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(eyes.position, look, config.punchRange, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.collider.attachedRigidbody;
                if (body != null && (body.gameObject == gameObject || Driver.Body.OwnsBody(body))) continue;
                if (hit.distance < nearestDistance) { nearestDistance = hit.distance; nearest = hit; }
            }
            TargetInRange = false;
            if (nearestDistance == float.MaxValue)
            {
                targetPoint = eyes.position + look * config.missAimDistance;
                return;
            }
            targetPoint = nearest.point + look * 0.1f;   // a little into it
            if (PunchHitResolver.FindTarget(nearest.collider, nearest.rigidbody, nearest.point, out var target, out var victim, out _, out _)
                && victim != Player && !hands.IsHolding(nearest.rigidbody))
            {
                TargetInRange = true;
                var prop = target.GetComponent<PhysicsProp>();
                TargetName = victim != null ? PlayerNet.NameOf(victim.OwnerClientId) : prop != null ? prop.displayName : target.name;
            }
            else if (nearestDistance > config.missAimDistance)
            {
                targetPoint = eyes.position + look * config.missAimDistance;   // a wall further off: punch the air
            }
        }

        /// <summary>
        /// What the punch flies at: the target under the crosshair (or the wall), else a point in front, so it
        /// lands where you look and not a shoulder's width to the side or at belly height.
        /// </summary>
        Vector3 AimPoint()
        {
            SenseTarget();
            return targetPoint;
        }

        Vector3 ClampAim(Vector3 forward)
        {
            float pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;   // down positive
            pitch = Mathf.Clamp(pitch, config.minAimPitch, config.maxAimPitch);
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        /// <summary>First-person fists: cocked back on the wind-up, jabbed out on the strike, eased back after.</summary>
        void UpdateFirstPersonPose()
        {
            foreach (var fist in fists)
            {
                float side = fist.Index == 0 ? -1f : 1f;
                Vector3 offset = Vector3.zero;
                float curl = 0f;
                switch (fist.Phase)
                {
                    case PunchArmDriver.Phase.WindUp:
                    {
                        float k = Mathf.SmoothStep(0f, 1f, fist.Time / Mathf.Max(0.01f, config.minWindUpSeconds));
                        offset = Vector3.Lerp(fist.PhaseStartOffset, new Vector3(-side * 0.06f, 0.06f, -0.08f), k)
                                 + new Vector3(0f, 0.03f, -0.12f) * fist.Charge;
                        curl = -15f - 25f * fist.Charge;
                        break;
                    }
                    case PunchArmDriver.Phase.Strike:
                    {
                        float e = Mathf.Sin(Mathf.Clamp01(fist.Time / Mathf.Max(0.01f, fist.StrikeSeconds * 0.6f)) * Mathf.PI * 0.5f);
                        offset = Vector3.Lerp(fist.PhaseStartOffset, new Vector3(-side * 0.16f, 0.12f, 0.42f + 0.12f * fist.Charge), e);
                        curl = Mathf.Lerp(fist.PhaseStartCurl, 12f, e);
                        break;
                    }
                    case PunchArmDriver.Phase.Recovery:
                    {
                        float r = Mathf.SmoothStep(0f, 1f, fist.Time / Mathf.Max(0.01f, config.recoverySeconds));
                        offset = Vector3.Lerp(fist.PhaseStartOffset, Vector3.zero, r);
                        curl = Mathf.Lerp(fist.PhaseStartCurl, 0f, r);
                        break;
                    }
                }
                if (fist.HitStop > 0f)
                {
                    offset = fist.PoseOffset;
                    curl = fist.PoseCurl;
                }
                fist.PoseOffset = offset;
                fist.PoseCurl = curl;
                hands.SetFistPose(fist.Index, offset, curl, fist.Phase != PunchArmDriver.Phase.Idle);
            }
        }

        void RegenerateStamina(ref float value, ref float spent, float from, float to)
        {
            float start = Mathf.Max(from, spent + config.staminaRegenDelay);
            if (to > start) value = Mathf.Min(config.maxStamina, value + (to - start) * config.staminaRegenPerSecond);
        }

        // ---- the owner's fist touched something ------------------------------------------------------

        void OnFistContact(PunchArmDriver.Contact c)
        {
            if (!IsSpawned || !IsOwner) return;
            var fist = fists[c.Hand];
            var collision = c.Collision;
            if (fist.Sequence != c.Sequence || collision.contactCount == 0) return;
            var contact = collision.GetContact(0);
            var other = collision.rigidbody;
            if (other != null && hands.IsHolding(other)) return;

            Vector3 targetVelocity = other != null && !other.isKinematic ? other.GetPointVelocity(contact.point) : Vector3.zero;
            float speed = Mathf.Abs(Vector3.Dot(c.FistVelocity - targetVelocity, contact.normal));
            if (speed < config.minImpactSpeed) return;   // a brush, not a punch

            if (!PunchHitResolver.FindTarget(collision.collider, other, contact.point, out var target, out var victim, out int part, out HitZone zone))
            {
                // A wall or the ground: it only hurts your knuckles.
                if (fist.Hit.Add(ulong.MaxValue))
                {
                    PunchFx.PlayImpact(contact.point, 0.2f, false);
                    LocalFeel(fist, 0.15f, false);
                }
                return;
            }
            if (victim == Player) return;
            ulong id = target.NetworkObjectId;
            if (fist.Hit.Count >= config.maxTargetsPerStrike || !fist.Hit.Add(id)) return;

            bool friendly = victim != null && FriendlyFire.AreTeammates(Player, victim);
            bool harmless = friendly && !FriendlyFire.Enabled;
            var outcome = PunchMath.Evaluate(config, speed, Driver.ArmMass(c.Hand), c.Charge, zone, friendly);
            Vector3 direction = PunchMath.KnockDirection(config, c.FistVelocity, Player.Motor.cameraPivot.forward);

            // Predicted: feel it now; the server's verdict follows.
            LocalFeel(fist, harmless ? 0.1f : outcome.Power, true);
            var flags = (friendly ? HitFlags.Friendly : 0) | (harmless ? HitFlags.Harmless : 0)
                        | (outcome.Comedic ? HitFlags.Comedic : 0) | (victim == null ? HitFlags.Prop : 0);
            PlayHitFx(contact.point, direction, flags, outcome.Power, attacker: true);
            if (victim != null && !harmless)
                victim.GetComponent<PunchReaction>().ApplyToBody(part, contact.point, direction * outcome.Impulse,
                    outcome.StaggerSeconds, outcome.StaggerStiffness);
            predicted.Add((c.Sequence, id, Time.time));
            Debug.Log($"Roadkill: fist hit {target.name} ({zone}) at {speed:0.0} m/s, charge {c.Charge:0.00}: " +
                      $"{outcome.Damage:0} dmg, {outcome.Impulse:0} N s{(outcome.KnockdownSeconds > 0f ? ", knockdown" : "")}");

            HitClaimRpc(c.Sequence, target, part, contact.point, c.FistVelocity, speed);
        }

        /// <summary>The attacker's feel: hit-stop on the first-person fist and a camera jolt.</summary>
        void LocalFeel(Fist fist, float power, bool landed)
        {
            fist.HitStop = Mathf.Lerp(config.hitStopSecondsLight, config.hitStopSecondsHeavy, power) * (landed ? 1f : 0.5f);
            if (reaction != null && reaction.CameraFx != null)
                reaction.CameraFx.Hit(Mathf.Lerp(config.shakeLight, config.shakeHeavy, power), config.shakeSeconds,
                    config.attackerCameraKick * Mathf.Max(0.3f, power), fist.Index == 0 ? new Vector3(0.3f, -1f, 0f) : new Vector3(0.3f, 1f, 0f));
        }

        static void PlayHitFx(Vector3 point, Vector3 direction, HitFlags flags, float power, bool attacker)
        {
            var c = PunchConfig.Current;
            bool friendly = (flags & HitFlags.Friendly) != 0;
            bool harmless = (flags & (HitFlags.Harmless | HitFlags.Protected)) != 0;
            if (harmless)
            {
                PunchFx.PlayHarmless(point);
                PunchFx.Sparks(point, -direction, Color.white, 0.1f);
                if (attacker) PunchFx.Popup(point, (flags & HitFlags.Protected) != 0 ? "PROTECTED" : "boop", Color.white, 0.8f);
                return;
            }
            Color color = (flags & HitFlags.Prop) != 0 ? c.propSparkColor : friendly ? c.friendlySparkColor : c.hitSparkColor;
            PunchFx.Sparks(point, -direction, color, power);
            bool comedic = (flags & HitFlags.Comedic) != 0;
            PunchFx.PlayImpact(point, power, comedic);
            if (friendly && attacker) PunchFx.Popup(point, PunchFx.FriendlyWord(), c.friendlySparkColor, comedic ? 1.3f : 1f);
            else if (comedic) PunchFx.Popup(point, PunchFx.HitWord(), c.hitSparkColor, 1.4f);
        }

        // =========================================================================== network

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        void WindUpRpc(int hand)
        {
            if (hand < 0 || hand > 1) return;
            if (IsServer) serverWindUp[hand] = Time.time;
            if (Driver != null) Driver.BeginWindUp(hand);
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        void CancelRpc(int hand)
        {
            if (hand < 0 || hand > 1) return;
            if (IsServer) serverWindUp[hand] = -99f;
            if (Driver != null) Driver.Cancel(hand);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void StrikeRpc(int hand, float charge, Vector3 aimPoint, int seq)
        {
            if (hand < 0 || hand > 1 || seq <= serverSequence) return;
            serverSequence = seq;
            float now = Time.time;
            RegenerateStamina(ref serverStamina, ref serverLastSpend, serverStaminaTime, now);
            serverStaminaTime = now;

            // Allow for jitter between the owner's clock and ours, not for spam.
            if (now - serverLastStrike < config.cooldownSeconds * 0.6f) return;
            float held = serverWindUp[hand] < 0f ? 0f : now - serverWindUp[hand] + 0.25f;
            charge = Mathf.Min(Mathf.Clamp01(charge), Mathf.Clamp01((held - config.minWindUpSeconds) / Mathf.Max(0.01f, config.chargeSeconds)));
            float cost = config.Cost(charge);
            if (serverStamina + 15f < cost) return;
            serverStamina = Mathf.Max(0f, serverStamina - cost);
            serverLastSpend = serverLastStrike = now;
            serverWindUp[hand] = -99f;

            strikes[seq] = new StrikeRecord { Time = now, Live = config.LiveSeconds(charge), Charge = charge, Hand = hand };
            var stale = new List<int>();
            foreach (var pair in strikes)
                if (now - pair.Value.Time > 3f) stale.Add(pair.Key);
            foreach (int key in stale) strikes.Remove(key);

            // An aim point is somewhere in front of the eyes; anything else is nonsense (or a cheat).
            Vector3 eyes = transform.position + Vector3.up * 1.6f;
            if ((aimPoint - eyes).sqrMagnitude > (config.punchRange + 1.5f) * (config.punchRange + 1.5f))
                aimPoint = eyes + transform.forward * config.missAimDistance;
            PlayStrikeRpc(hand, charge, aimPoint, seq);
        }

        /// <summary>Everyone but the owner (who already swung) plays the accepted strike on their copy.</summary>
        [Rpc(SendTo.NotOwner)]
        void PlayStrikeRpc(int hand, float charge, Vector3 aimPoint, int seq)
        {
            if (Driver == null) return;
            Driver.BeginStrike(hand, charge, aimPoint, seq);
            PunchFx.PlayWhoosh(Driver.Body.HandBody(hand).position, charge);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitClaimRpc(int seq, NetworkObjectReference targetRef, int part, Vector3 point, Vector3 fistVelocity, float speed)
        {
            if (!strikes.TryGetValue(seq, out var strike)) { Reject(seq, "no such strike"); return; }
            if (Time.time > strike.Time + strike.Live + config.recoverySeconds + config.claimLatencyAllowance) { Reject(seq, "too late"); return; }
            if (!targetRef.TryGet(out NetworkObject target) || target == NetworkObject) return;
            if (strike.Targets.Count >= config.maxTargetsPerStrike || !strike.Targets.Add(target.NetworkObjectId)) return;
            if (speed > config.maxPlausibleFistSpeed * 1.5f) { Reject(seq, $"speed {speed:0.0} m/s"); return; }
            speed = Mathf.Min(speed, fistVelocity.magnitude + 0.5f, config.maxPlausibleFistSpeed);
            if (!PunchHitResolver.ServerPlausible(this, target, point)) { Reject(seq, $"too far ({target.name} at {point})"); return; }

            PunchHitResolver.ServerResolve(this, target, part, point, fistVelocity, speed, strike.Charge, seq,
                Driver != null ? Driver.ArmMass(strike.Hand) : 2.4f);
        }

        void Reject(int seq, string why) => Debug.LogWarning($"Roadkill: punch {seq} from {PlayerNet.NameOf(OwnerClientId)} rejected: {why}");

        /// <summary>Server: the verdict, to everyone: the struck bone's impulse and the effects.</summary>
        public void ServerBroadcastHit(NetworkObject target, int part, Vector3 point, Vector3 impulse,
            float staggerSeconds, float staggerStiffness, float power, HitFlags flags, int seq)
        {
            HitFxRpc(target, part, point, impulse, staggerSeconds, staggerStiffness, power, (byte)flags, seq);
        }

        [Rpc(SendTo.Everyone)]
        void HitFxRpc(NetworkObjectReference targetRef, int part, Vector3 point, Vector3 impulse,
            float staggerSeconds, float staggerStiffness, float power, byte rawFlags, int seq)
        {
            if (!targetRef.TryGet(out NetworkObject target)) return;
            var flags = (HitFlags)rawFlags;
            bool attacker = IsOwner;
            if (attacker && predicted.RemoveAll(p => p.sequence == seq && p.target == target.NetworkObjectId) > 0)
            {
                // Already felt locally; only say what the server changed.
                if ((flags & HitFlags.Protected) != 0) PunchFx.Popup(point, "PROTECTED", Color.white, 0.8f);
                else if ((flags & HitFlags.Capped) != 0) PunchFx.Popup(point, "they've had enough", Color.white, 0.7f);
                return;
            }
            var victim = target.GetComponent<PunchReaction>();
            if (victim != null && (flags & HitFlags.Harmless) == 0)
                victim.ApplyToBody(part, point, impulse, staggerSeconds, staggerStiffness);
            Vector3 direction = impulse.sqrMagnitude > 0.0001f ? impulse.normalized : transform.forward;
            PlayHitFx(point, direction, flags, power, attacker);
        }
    }
}
