using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Grandpa's Revolver (GDD section 4), on the held-revolver prefab. LMB fires an aimed shot, two a second;
    /// holding RMB fans the hammer: faster, with three times the spread. Six in the cylinder, then a reload
    /// (R, or on its own when the hammer clicks on an empty chamber); spare rounds are endless for now.
    /// Shots are hitscan from the eyes: the owner sees the flash, tracer and impact at once, and PlayerTools
    /// sends the hit to the server, which applies the damage with this prefab's own numbers. Everyone else
    /// sees the same shot.
    ///
    /// The reload is a real swing-out reload, all procedural on the Blender model's parts: the gun tips over,
    /// the left hand pushes the cylinder out on its crane, muzzle up and the ejector throws six cases (real
    /// little rigidbodies that clink on the ground), muzzle down and a speedloader goes in and twists, then a
    /// flick of the wrist slams the cylinder home and it freewheels to a stop. Other players see the same on
    /// their copy. The prefab's root is the grip, the barrel along its local +Z.
    /// </summary>
    public class RevolverTool : MonoBehaviour
    {
        [Header("Damage (the server reads these from the prefab)")]
        public float damage = 40f;
        public float headMultiplier = 1.5f;
        public float range = 60f;
        [Tooltip("Push on a hit prop, N·s.")]
        public float propImpulse = 30f;
        [Tooltip("Shove on a hit player, m/s.")]
        public float playerShove = 2.5f;
        [Tooltip("Knock on the struck bone, N·s (the hit reaction everyone sees).")]
        public float boneImpulse = 14f;
        [Tooltip("A head shot knocks a player down for this long (s).")]
        public float headKnockdownSeconds = 1.5f;

        [Header("Handling")]
        public int capacity = 6;
        [Tooltip("Seconds between aimed shots (LMB).")]
        public float aimedInterval = 0.5f;
        [Tooltip("Seconds between fanned shots (RMB held).")]
        public float fanInterval = 0.16f;
        public float spreadDegrees = 0.6f;
        public float fanSpreadMultiplier = 3f;
        [Tooltip("Length of the whole reload; the animation is timed for 1.8 s and scaled to this.")]
        public float reloadSeconds = 1.8f;

        [Header("Feel")]
        [Tooltip("The view kicks up this many degrees per shot.")]
        public float recoilPitch = 3.5f;
        public float fanRecoilPitch = 2f;
        public float recoilYaw = 0.8f;
        [Tooltip("The gun flips up this far and slides back this much, then settles.")]
        public float kickDegrees = 24f;
        public float kickBack = 0.06f;
        public float kickRecover = 12f;
        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("First person: moved this far from the tool socket (camera space), so the gun sits up in view.")]
        public Vector3 firstPersonShift = new Vector3(-0.07f, 0.1f, 0.02f);
        [Tooltip("Where the right hand's fist sits, in the model's space (its origin is the top of the grip).")]
        public Vector3 handGripOffset = new Vector3(-0.035f, -0.05f, 0.055f);
        [Tooltip("Turns the gripping hand: tilt about the gun's side axis, then about the palm, degrees.")]
        public float handTilt = 20f, handPitch = 0f;
        [Tooltip("How far the cylinder swings out on its crane, degrees.")]
        public float craneOpenDegrees = 82f;
        [Tooltip("Seconds a thrown-out case stays on the ground.")]
        public float caseLifetime = 8f;

        [Header("Model parts (set by the editor tool)")]
        [SerializeField] Transform model;
        [SerializeField] Transform crane, cylinder, ejector, muzzle, loader, loaderRounds, loaderSocket, casingTemplate;
        [SerializeField] Transform[] rounds = new Transform[0];

        /// <summary>Tests: ignore the mouse and keyboard (shots come from DebugFire only).</summary>
        public static bool IgnoreInput;

        /// <summary>Where the right hand holds the gun (the model's origin is the grip).</summary>
        public Transform Grip => handGrip != null ? handGrip : model != null ? model : transform;
        /// <summary>The right hand's long axis (wrist to knuckles) and the way its palm faces, in the world.</summary>
        public Vector3 HandAlong => Quaternion.AngleAxis(handPitch, HandPalm) * Quaternion.AngleAxis(handTilt, model.TransformDirection(Vector3.right)) * BoreWorld;
        public Vector3 HandPalm => model.TransformDirection(Vector3.right);   // toward the gun's left side
        public int Rounds { get; private set; }
        public bool Reloading { get; private set; }
        /// <summary>Shots this copy has shown (tests: did the other players see them?).</summary>
        public int ShotsShown { get; private set; }

        const float Timeline = 1.8f;   // seconds the reload animation is authored for

        static AudioClip bang, click, latch, slide, clack, slam, tick, clink;

        PlayerTools owner;
        HandsController hands;
        Transform handGrip;
        bool isLocal, ready;
        float nextShot, kick, flashUntil, tracerUntil;
        GameObject flash;
        LineRenderer tracer;
        AudioSource audioSource;
        GUIStyle hudStyle;
        readonly List<Collider> ownColliders = new List<Collider>();

        // Rest poses and the bore's direction in each moving part's parent space.
        Vector3 modelRestPosition, ejectorRest, craneSwingAxis, spinAxis, ejectAxis;
        Quaternion modelRestRotation, craneRest, cylinderRest, loaderToCylinder, casingToRound;
        Vector3[] roundRest = new Vector3[0];
        float craneSign = 1f;

        // Animated state.
        float reloadTime = -1f, craneAngle, ejectPush, spinAngle, spinTarget, spinVelocity, lastTick;
        bool freewheel;

        /// <summary>Used by the editor tool that builds the prefab.</summary>
        public void Setup(Transform gunModel, Transform gunMuzzle)
        {
            model = gunModel;
            muzzle = gunMuzzle;
            crane = Find("Crane");
            cylinder = Find("Cylinder");
            ejector = Find("Ejector");
            loader = Find("Speedloader");
            loaderRounds = Find("SpeedloaderRounds");
            loaderSocket = Find("LoaderSocket");
            casingTemplate = Find("Casing");
            var list = new List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                var round = Find($"Round{i}");
                if (round != null) list.Add(round);
            }
            rounds = list.ToArray();
        }

        Transform Find(string partName)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == partName) return t;
            return null;
        }

        public void Init(PlayerTools tools, bool local)
        {
            owner = tools;
            isLocal = local;
            hands = tools.GetComponent<HandsController>();
            Rounds = capacity;
            if (model == null) model = transform;
            if (local) transform.localPosition += firstPersonShift;
            if (muzzle == null) muzzle = transform;
            handGrip = new GameObject("Hand Grip").transform;
            handGrip.SetParent(model, false);
            handGrip.localPosition = handGripOffset;
            CaptureRest();
            BuildEffects();
            if (local)
            {
                ownColliders.AddRange(tools.GetComponentsInChildren<Collider>());
                foreach (var part in FindObjectsByType<RagdollBodyPart>())
                    if (part.Player != null && part.Player.gameObject == tools.gameObject) ownColliders.AddRange(part.GetComponents<Collider>());
            }
        }

        /// <summary>The model's parts as they came from Blender; every move is relative to this.</summary>
        void CaptureRest()
        {
            modelRestPosition = model.localPosition;
            modelRestRotation = model.localRotation;
            // The bore runs along -Z of the imported model (the prefab turns the model round).
            Vector3 bore = model.TransformDirection(Vector3.back);
            if (crane != null && cylinder != null)
            {
                craneRest = crane.localRotation;
                craneSwingAxis = crane.parent.InverseTransformDirection(bore);
                // Which way is out? The cylinder has to swing to the gun's left (the model's +X).
                Vector3 before = model.InverseTransformPoint(cylinder.position);
                crane.localRotation = Quaternion.AngleAxis(20f, craneSwingAxis) * craneRest;
                Vector3 after = model.InverseTransformPoint(cylinder.position);
                crane.localRotation = craneRest;
                craneSign = after.x > before.x ? 1f : -1f;
            }
            if (cylinder != null)
            {
                cylinderRest = cylinder.localRotation;
                spinAxis = cylinder.parent.InverseTransformDirection(bore);
            }
            if (ejector != null)
            {
                ejectorRest = ejector.localPosition;
                ejectAxis = ejector.parent.InverseTransformDirection(bore);
            }
            roundRest = new Vector3[rounds.Length];
            for (int i = 0; i < rounds.Length; i++) roundRest[i] = rounds[i].localPosition;
            if (loader != null && cylinder != null)
            {
                loaderToCylinder = Quaternion.Inverse(cylinder.rotation) * loader.rotation;
                loader.gameObject.SetActive(false);
            }
            if (casingTemplate != null)
            {
                if (rounds.Length > 0) casingToRound = Quaternion.Inverse(rounds[0].rotation) * casingTemplate.rotation;
                casingTemplate.gameObject.SetActive(false);
            }
            ready = crane != null && cylinder != null && ejector != null && loader != null && loaderRounds != null
                    && loaderSocket != null && rounds.Length == 6;
        }

        void BuildEffects()
        {
            flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Muzzle Flash";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.SetParent(muzzle, false);
            flash.transform.localPosition = Vector3.zero;
            flash.transform.localScale = new Vector3(0.07f, 0.07f, 0.12f);
            var flashRenderer = flash.GetComponent<Renderer>();
            flashRenderer.sharedMaterial = RkMaterials.Get(new Color(1f, 0.85f, 0.35f));
            flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 4f;
            light.color = new Color(1f, 0.8f, 0.45f);
            flash.SetActive(false);

            var tracerGo = new GameObject("Tracer");
            tracerGo.transform.SetParent(transform, false);
            tracer = tracerGo.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.startWidth = 0.012f;
            tracer.endWidth = 0.005f;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var shader = Shader.Find("Sprites/Default");
            tracer.sharedMaterial = shader != null ? new Material(shader) : RkMaterials.Get(Color.white);
            tracer.enabled = false;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = isLocal ? 0.3f : 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 4f;
            audioSource.maxDistance = 80f;
        }

        void OnDisable()
        {
            // Put away mid-reload (ragdoll, slot change): everything snaps back and the reload starts over next time.
            StopAllCoroutines();
            Reloading = false;
            reloadTime = -1f;
            kick = 0f;
            craneAngle = 0f;
            ejectPush = 0f;
            freewheel = false;
            spinAngle = spinTarget = Mathf.Round(spinTarget / 60f) * 60f;
            if (flash != null) flash.SetActive(false);
            if (tracer != null) tracer.enabled = false;
            if (loader != null) loader.gameObject.SetActive(false);
            foreach (var round in rounds) if (round != null) round.gameObject.SetActive(true);
            if (hands != null && isLocal) hands.SetHandTarget(0, false, Vector3.zero);
            if (owner != null) Animate(0f);
        }

        void Update()
        {
            if (owner == null) return;
            Animate(Time.deltaTime);
            if (!isLocal || Reloading || !owner.CanUseTool || IgnoreInput) return;

            if (RkInput.ReloadPressed && Rounds < capacity)
            {
                StartReload();
                return;
            }
            bool fan = RkInput.FanHeld;
            if (fan || RkInput.ClickPressed) Pull(fan);
        }

        /// <summary>Test hook: pull the trigger now (aimed, or fanned), as the owner would.</summary>
        public void DebugFire(bool fan)
        {
            if (isLocal && owner != null && !Reloading) Pull(fan);
        }

        /// <summary>Test hook: reload now.</summary>
        public void DebugReload()
        {
            if (isLocal && owner != null) StartReload();
        }

        void Pull(bool fan)
        {
            if (Time.time < nextShot) return;
            if (Rounds <= 0)
            {
                Play(Clip(ref click, MakeClick), 0.8f, 1f);
                StartReload();
                return;
            }
            Fire(fan);
        }

        void Fire(bool fan)
        {
            Rounds--;
            nextShot = Time.time + (fan ? fanInterval : aimedInterval);
            Vector3 origin = owner.ViewOrigin;
            Vector3 direction = Spread(owner.ViewForward, spreadDegrees * (fan ? fanSpreadMultiplier : 1f));
            bool hit = owner.FindShotTarget(origin, direction, range, out RaycastHit info);
            Vector3 end = hit ? info.point : origin + direction * range;
            Vector3 normal = hit ? info.normal : -direction;
            PlayShot(end, normal, hit);
            owner.ReportShot(this, origin, direction, hit ? info.collider : null, end, normal);
            owner.Recoil(fan ? fanRecoilPitch : recoilPitch, Random.Range(-recoilYaw, recoilYaw));
        }

        static Vector3 Spread(Vector3 forward, float degrees)
        {
            if (degrees <= 0f) return forward;
            Vector2 offset = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
            var basis = Quaternion.LookRotation(forward);
            return (forward + basis * new Vector3(offset.x, offset.y, 0f)).normalized;
        }

        /// <summary>The bang: flash, tracer to `end`, the gun kicking and the cylinder turning; sparks where it hit. Every peer.</summary>
        public void PlayShot(Vector3 end, Vector3 normal, bool impact)
        {
            if (!isActiveAndEnabled || flash == null) return;
            ShotsShown++;
            kick = 1f;
            spinTarget += 60f;
            flash.SetActive(true);
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flashUntil = Time.time + 0.05f;
            tracer.SetPosition(0, muzzle.position);
            tracer.SetPosition(1, end);
            tracer.enabled = true;
            tracerUntil = Time.time + 0.07f;
            Play(Clip(ref bang, MakeBang), 1f, Random.Range(0.93f, 1.07f));
            if (impact) PunchFx.Sparks(end, normal, new Color(1f, 0.85f, 0.45f), 0.35f);
        }

        void StartReload()
        {
            if (Reloading) return;
            StartCoroutine(Reload(true));
            if (isLocal) owner.BroadcastReload();
        }

        /// <summary>Other players: show the reload on this player's copy.</summary>
        public void PlayReload()
        {
            if (isActiveAndEnabled && !Reloading) StartCoroutine(Reload(false));
        }

        // ---- the reload ------------------------------------------------------------------------------------

        // Gun pose keys over the 1.8 s timeline: time, offset (root space, metres), euler (pitch, yaw, roll).
        // Pitch below zero lifts the muzzle; roll above zero turns the top to the right, showing the left side.
        static readonly float[] KeyTime = { 0f, 0.15f, 0.34f, 0.47f, 0.62f, 0.8f, 1.2f, 1.34f, 1.42f, 1.58f, 1.8f };
        static readonly Vector3[] KeyPos =
        {
            Vector3.zero, new Vector3(-0.05f, 0.05f, 0.01f), new Vector3(-0.1f, 0.11f, 0.03f), new Vector3(-0.1f, 0.08f, 0.03f),
            new Vector3(-0.1f, 0.08f, 0.03f), new Vector3(-0.08f, 0.15f, 0.05f), new Vector3(-0.08f, 0.145f, 0.05f),
            new Vector3(-0.09f, 0.11f, 0.03f), new Vector3(-0.07f, 0.09f, 0.02f), new Vector3(-0.02f, 0.02f, 0f), Vector3.zero
        };
        static readonly Vector3[] KeyEuler =
        {
            Vector3.zero, new Vector3(-8f, -10f, 14f), new Vector3(-10f, -22f, 34f), new Vector3(-48f, -18f, 22f),
            new Vector3(-52f, -18f, 22f), new Vector3(22f, -18f, 26f), new Vector3(26f, -18f, 24f),
            new Vector3(16f, -20f, 28f), new Vector3(-8f, -22f, -16f), new Vector3(3f, -5f, -3f), Vector3.zero
        };

        IEnumerator Reload(bool mine)
        {
            Reloading = true;
            float speed = Timeline / Mathf.Max(0.3f, reloadSeconds);
            reloadTime = 0f;
            bool latched = false, swung = false, pushed = false, thrown = false, shown = false, seated = false, twisted = false, slammed = false;
            var loaderStart = Vector3.zero;
            while (reloadTime < Timeline)
            {
                reloadTime = Mathf.Min(Timeline, reloadTime + Time.deltaTime * speed);
                float t = reloadTime;
                if (!ready) { yield return null; continue; }

                // Cylinder out: the latch, then a swing that overshoots and settles; slammed home on the flick.
                if (!latched && t >= 0.18f) { latched = true; Play(Clip(ref latch, MakeLatch), 0.7f, 1f); }
                if (!swung && t >= 0.24f) { swung = true; Play(Clip(ref slide, MakeSlide), 0.6f, 1f); }
                if (t < 0.22f) craneAngle = 0f;
                else if (t < 0.36f) craneAngle = craneOpenDegrees * EaseOutBack((t - 0.22f) / 0.14f);
                else if (t < 1.36f) craneAngle = craneOpenDegrees;
                else if (t < 1.42f) craneAngle = craneOpenDegrees * (1f - EaseIn((t - 1.36f) / 0.06f));
                else craneAngle = 0f;
                if (!slammed && t >= 1.42f)
                {
                    slammed = true;
                    Play(Clip(ref slam, MakeSlam), 1f, 1f);
                    freewheel = true;
                    spinVelocity = 1100f;
                    if (isLocal) owner.Recoil(-1.5f, 0f);   // the flick nods the view
                }

                // Ejector: a hard push, the cases fly, the rod comes back.
                if (t < 0.5f) ejectPush = 0f;
                else if (t < 0.55f) ejectPush = EaseOut((t - 0.5f) / 0.05f);
                else if (t < 0.6f) ejectPush = 1f;
                else if (t < 0.68f) ejectPush = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.08f);
                else ejectPush = 0f;
                if (!pushed && t >= 0.5f) { pushed = true; Play(Clip(ref clack, MakeClack), 0.8f, 1.2f); }
                if (!thrown && t >= 0.55f) { thrown = true; ThrowCases(); }

                // Speedloader: up from below, lined up behind the cylinder, in, a twist, away.
                if (!shown && t >= 0.72f)
                {
                    shown = true;
                    loader.gameObject.SetActive(true);
                    loaderRounds.gameObject.SetActive(true);
                    loaderStart = transform.InverseTransformPoint(loaderSocket.position) + new Vector3(-0.18f, -0.1f, -0.06f);
                }
                if (shown && t < 1.36f) PlaceLoader(t, loaderStart);
                if (!seated && t >= 1.04f) { seated = true; Play(Clip(ref clack, MakeClack), 0.9f, 0.85f); }
                if (!twisted && t >= 1.1f)
                {
                    twisted = true;
                    Play(Clip(ref latch, MakeLatch), 0.8f, 1.3f);
                    loaderRounds.gameObject.SetActive(false);
                    foreach (var round in rounds) round.gameObject.SetActive(true);
                    if (mine) Rounds = capacity;
                }
                if (t >= 1.36f && loader.gameObject.activeSelf) loader.gameObject.SetActive(false);

                if (mine && isLocal && hands != null) DriveLeftHand(t);
                yield return null;
            }
            if (mine) Rounds = capacity;
            reloadTime = -1f;
            Reloading = false;
            if (isLocal && hands != null) hands.SetHandTarget(0, false, Vector3.zero);
        }

        void PlaceLoader(float t, Vector3 start)
        {
            Vector3 bore = BoreWorld;
            Vector3 socket = transform.InverseTransformPoint(loaderSocket.position);
            Vector3 behind = transform.InverseTransformPoint(loaderSocket.position - bore * 0.05f);
            Vector3 local;
            float twist = 0f;
            if (t < 0.95f) local = Vector3.Lerp(start, behind, EaseOut(Mathf.InverseLerp(0.72f, 0.95f, t)));
            else if (t < 1.04f) local = Vector3.Lerp(behind, socket, EaseIn(Mathf.InverseLerp(0.95f, 1.04f, t)));
            else if (t < 1.1f)
            {
                local = socket;
                twist = 35f * EaseOut(Mathf.InverseLerp(1.04f, 1.1f, t));
            }
            else
            {
                twist = 35f;
                float away = Mathf.InverseLerp(1.1f, 1.36f, t);
                local = Vector3.Lerp(socket, behind, EaseOut(Mathf.Clamp01(away * 2f)))
                        + new Vector3(-0.06f, -0.18f, -0.02f) * EaseIn(Mathf.Clamp01(away * 1.4f - 0.3f));
            }
            loader.position = transform.TransformPoint(local);
            loader.rotation = Quaternion.AngleAxis(twist, bore) * cylinder.rotation * loaderToCylinder;
        }

        Vector3 BoreWorld => model.TransformDirection(Vector3.back);

        /// <summary>First person: the left hand pushes the cylinder out, slaps the ejector, brings the speedloader.</summary>
        void DriveLeftHand(float t)
        {
            Vector3 bore = BoreWorld;
            Vector3 side = transform.TransformDirection(Vector3.left);
            if (t > 0.08f && t < 0.42f)
                hands.SetHandTarget(0, true, cylinder.position + side * 0.05f - bore * 0.02f, -10f);
            else if (t >= 0.42f && t < 0.66f)
                hands.SetHandTarget(0, true, ejector.position + bore * (0.11f - 0.03f * ejectPush) + side * 0.03f, 25f);
            else if (t >= 0.7f && t < 1.24f)
                hands.SetHandTarget(0, true, loader.position - bore * 0.03f + side * 0.07f + transform.TransformDirection(Vector3.down) * 0.02f, 10f);
            else
                hands.SetHandTarget(0, false, Vector3.zero);
        }

        /// <summary>Six cases leave the chambers and fall out of the back, each a real little rigidbody.</summary>
        void ThrowCases()
        {
            Vector3 bore = BoreWorld;
            foreach (var round in rounds)
            {
                round.gameObject.SetActive(false);
                if (casingTemplate == null) continue;
                var copy = Instantiate(casingTemplate.gameObject, round.position, round.rotation * casingToRound);
                copy.name = "Spent Case";
                copy.transform.localScale = casingTemplate.lossyScale;
                copy.SetActive(true);
                var mesh = copy.GetComponent<MeshFilter>().sharedMesh;
                var collider = copy.AddComponent<CapsuleCollider>();
                collider.direction = 2;
                collider.center = mesh.bounds.center;
                collider.height = mesh.bounds.size.z;
                collider.radius = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y) * 0.5f;
                foreach (var own in ownColliders)
                    if (own != null) Physics.IgnoreCollision(collider, own);
                var body = copy.AddComponent<Rigidbody>();
                body.mass = 0.012f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.linearVelocity = -bore * Random.Range(1f, 1.6f) + Random.insideUnitSphere * 0.35f;
                body.angularVelocity = Random.insideUnitSphere * 25f;
                copy.AddComponent<SpentCase>().Init(Clip(ref clink, MakeClink), volume * 0.5f, caseLifetime);
            }
        }

        // ---- every frame: put the parts where the shot and the reload want them -------------------------------

        void Animate(float dt)
        {
            kick = Mathf.MoveTowards(kick, 0f, dt * kickRecover * Mathf.Max(0.2f, kick));
            Vector3 pos = Vector3.zero, euler = Vector3.zero;
            if (reloadTime >= 0f) Sample(reloadTime, out pos, out euler);
            if (model != null)
            {
                model.localRotation = Quaternion.Euler(euler) * Quaternion.AngleAxis(-kick * kickDegrees, Vector3.right) * modelRestRotation;
                model.localPosition = modelRestPosition + pos + Vector3.back * (kick * kickBack);
            }
            if (flash != null && flash.activeSelf && Time.time > flashUntil) flash.SetActive(false);
            if (tracer != null && tracer.enabled)
            {
                float left = Mathf.Clamp01((tracerUntil - Time.time) / 0.07f);
                var color = new Color(1f, 0.9f, 0.6f, left);
                tracer.startColor = color;
                tracer.endColor = new Color(color.r, color.g, color.b, left * 0.3f);
                if (left <= 0f) tracer.enabled = false;
            }
            if (!ready) return;

            crane.localRotation = Quaternion.AngleAxis(craneAngle * craneSign, craneSwingAxis) * craneRest;

            // The cylinder: indexes 60° a shot, or freewheels after the slam until it clicks into a chamber.
            if (freewheel)
            {
                spinAngle += spinVelocity * dt;
                spinVelocity *= Mathf.Exp(-2.2f * dt);
                if (spinAngle - lastTick >= 60f)
                {
                    lastTick = Mathf.Floor(spinAngle / 60f) * 60f;
                    Play(Clip(ref tick, MakeTick), 0.35f, Random.Range(0.95f, 1.1f));
                }
                if (spinVelocity < 160f)
                {
                    freewheel = false;
                    spinTarget = Mathf.Ceil(spinAngle / 60f) * 60f;
                }
            }
            else
            {
                float before = spinAngle;
                spinAngle = Mathf.MoveTowards(spinAngle, spinTarget, dt * 900f);
                if (before != spinAngle && spinAngle == spinTarget && reloadTime > 1.42f) Play(Clip(ref tick, MakeTick), 0.6f, 0.8f);
                lastTick = Mathf.Floor(spinAngle / 60f) * 60f;
            }
            cylinder.localRotation = Quaternion.AngleAxis(spinAngle, spinAxis) * cylinderRest;

            // Ejector and the cases ride back out of the chambers together.
            Vector3 push = -ejectAxis * (0.022f * ejectPush);
            ejector.localPosition = ejectorRest + push;
            Vector3 roundPush = cylinder.InverseTransformVector(ejector.parent.TransformVector(push));
            for (int i = 0; i < rounds.Length; i++) rounds[i].localPosition = roundRest[i] + roundPush;
        }

        /// <summary>The gun's pose at time t of the reload: Catmull-Rom through the keys, so it flows.</summary>
        static void Sample(float t, out Vector3 pos, out Vector3 euler)
        {
            int i = 0;
            while (i < KeyTime.Length - 2 && t > KeyTime[i + 1]) i++;
            float u = Mathf.Clamp01((t - KeyTime[i]) / (KeyTime[i + 1] - KeyTime[i]));
            int a = Mathf.Max(0, i - 1), b = i, c = i + 1, d = Mathf.Min(KeyTime.Length - 1, i + 2);
            pos = CatmullRom(KeyPos[a], KeyPos[b], KeyPos[c], KeyPos[d], u);
            euler = CatmullRom(KeyEuler[a], KeyEuler[b], KeyEuler[c], KeyEuler[d], u);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        static float EaseIn(float x) => x * x;
        static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

        static float EaseOutBack(float x)
        {
            const float c1 = 2.2f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        void Play(AudioClip clip, float volumeScale, float pitch)
        {
            if (audioSource == null) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volumeScale * volume);
        }

        void OnGUI()
        {
            if (!isLocal || owner == null || !owner.CanUseTool) return;
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight, richText = true };
                hudStyle.normal.textColor = new Color(1f, 0.92f, 0.7f);
            }
            var shown = new System.Text.StringBuilder();
            for (int i = 0; i < capacity; i++) shown.Append(i < Rounds ? "● " : "○ ");
            string text = Reloading ? "RELOADING..." : $"{shown}  {Rounds}/{capacity}\n<size=13>LMB shoot   RMB hold: fan   R reload</size>";
            GUI.Label(new Rect(Screen.width - 420f, Screen.height - 150f, 400f, 60f), text, hudStyle);
        }

        // ---- placeholder sounds ------------------------------------------------------------------------

        const int Rate = 44100;

        static AudioClip Clip(ref AudioClip cache, System.Func<AudioClip> make)
        {
            if (cache == null) cache = make();
            return cache;
        }

        static AudioClip MakeClip(string name, float seconds, System.Func<float, float, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var random = new System.Random(5);
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate, (float)(random.NextDouble() * 2.0 - 1.0)), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Ring(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);

        /// <summary>A crack, a low boom under it and a short rolling tail.</summary>
        static AudioClip MakeBang()
        {
            float low = 0f, tail = 0f;
            return MakeClip("RevolverBang", 0.6f, (t, white) =>
            {
                low += (white - low) * 0.08f;
                tail += (white - tail) * 0.02f;
                float crack = white * Mathf.Exp(-t * 60f);
                float boom = Mathf.Sin(2f * Mathf.PI * (60f + 90f * Mathf.Exp(-t * 25f)) * t) * Mathf.Exp(-t * 14f);
                float body = low * 3f * Mathf.Exp(-t * 18f);
                float roll = tail * 6f * Mathf.Exp(-t * 5f) * Mathf.Clamp01(t * 20f);
                return (crack * 0.9f + boom * 0.8f + body * 0.6f + roll * 0.35f) * 0.9f;
            });
        }

        /// <summary>A dry metal click (empty chamber).</summary>
        static AudioClip MakeClick() => MakeClip("RevolverClick", 0.05f, (t, white) =>
            (white * 0.5f + Ring(t, 3200f) * 0.5f) * Mathf.Exp(-t * 180f) * 0.8f);

        /// <summary>The cylinder latch: two quick small clicks.</summary>
        static AudioClip MakeLatch() => MakeClip("RevolverLatch", 0.06f, (t, white) =>
            (white * 0.4f + Ring(t, 4100f) * 0.6f) * (Mathf.Exp(-t * 300f) + (t > 0.025f ? 0.7f * Mathf.Exp(-(t - 0.025f) * 300f) : 0f)) * 0.7f);

        /// <summary>The crane swinging out: a short oily metal slide ending in a tap.</summary>
        static AudioClip MakeSlide()
        {
            float band = 0f;
            return MakeClip("RevolverSlide", 0.12f, (t, white) =>
            {
                band += (white - band) * 0.3f;
                float swish = band * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.09f)) * 0.5f;
                float tap = t > 0.09f ? (Ring(t, 2400f) * 0.6f + white * 0.3f) * Mathf.Exp(-(t - 0.09f) * 120f) : 0f;
                return swish + tap;
            });
        }

        /// <summary>A solid clack (the ejector, the speedloader seating).</summary>
        static AudioClip MakeClack() => MakeClip("RevolverClack", 0.1f, (t, white) =>
            white * 0.6f * Mathf.Exp(-t * 90f) + (Ring(t, 1700f) + 0.5f * Ring(t, 2900f)) * 0.35f * Mathf.Exp(-t * 50f));

        /// <summary>The cylinder slammed home: a loud clack with weight under it.</summary>
        static AudioClip MakeSlam() => MakeClip("RevolverSlam", 0.18f, (t, white) =>
            white * 0.8f * Mathf.Exp(-t * 70f) + (Ring(t, 1300f) + 0.6f * Ring(t, 2300f)) * 0.4f * Mathf.Exp(-t * 35f)
            + Ring(t, 140f) * 0.5f * Mathf.Exp(-t * 30f));

        /// <summary>The ratchet ticking past a chamber.</summary>
        static AudioClip MakeTick() => MakeClip("RevolverTick", 0.03f, (t, white) =>
            (white * 0.3f + Ring(t, 5200f) * 0.7f) * Mathf.Exp(-t * 400f));

        /// <summary>A brass case landing: a bright little ring.</summary>
        static AudioClip MakeClink() => MakeClip("CaseClink", 0.35f, (t, white) =>
            (Ring(t, 4600f) * 0.5f + Ring(t, 6900f) * 0.3f + Ring(t, 9300f) * 0.15f) * Mathf.Exp(-t * 14f) + white * 0.3f * Mathf.Exp(-t * 400f));
    }

    /// <summary>A thrown-out case: clinks when it lands (a few times, quieter each bounce), then goes away.</summary>
    public class SpentCase : MonoBehaviour
    {
        AudioSource source;
        AudioClip clip;
        float volume;
        int clinks;

        public void Init(AudioClip clinkClip, float clinkVolume, float lifetime)
        {
            clip = clinkClip;
            volume = clinkVolume;
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 20f;
            Destroy(gameObject, lifetime);
        }

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (clip == null || speed < 0.4f || clinks >= 4) return;
            source.pitch = Random.Range(0.85f, 1.2f);
            source.PlayOneShot(clip, volume * Mathf.Clamp01(speed / 3f) / (1f + clinks));
            clinks++;
        }
    }
}
