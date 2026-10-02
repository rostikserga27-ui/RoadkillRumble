using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Offline playground for the fisherman's active ragdoll (Scenes/RagdollTest.unity, no network).
    /// Builds ground, a ramp ending in a drop, a steep slope, a crate pyramid, a bouncy pad and a box
    /// cannon, then spawns a local player made exactly like NetPlayer: the same capsule and PlayerMotor
    /// (same input and movement code) with the Fisherman_Ragdoll prefab following it, seen through a
    /// third-person follow camera. RagdollTestHands gives it the game's grab and throw (LMB / RMB, G);
    /// RagdollDebugPanel adds the buttons and live sliders.
    /// </summary>
    public class RagdollTestBuilder : MonoBehaviour
    {
        public GameObject fishermanPrefab;

        static readonly Color Grass = new Color(0.36f, 0.45f, 0.3f);
        static readonly Color Concrete = new Color(0.62f, 0.62f, 0.6f);
        static readonly Color Bouncy = new Color(0.85f, 0.35f, 0.6f);
        static readonly Color Crate = new Color(0.66f, 0.5f, 0.3f);

        public PlayerMotor Motor { get; private set; }
        public ActiveRagdollController Body { get; private set; }
        public RagdollRemoteStandIn Remote { get; private set; }
        public RagdollTestHands TestHands { get; private set; }

        Transform cannon;
        PhysicsMaterial crateMaterial;

        void Awake()
        {
            BuildLighting();
            crateMaterial = new PhysicsMaterial("Crate") { dynamicFriction = 0.5f, staticFriction = 0.6f, bounciness = 0.2f };
            Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f), Grass);

            // Ramp up to a 2.5 m ledge, then nothing: walk off it.
            Ramp(new Vector3(-7f, 0f, 2f), new Vector3(-7f, 2.5f, 9f), 3f);
            Block("Ledge", new Vector3(-7f, 1.25f, 10.5f), new Vector3(3f, 2.5f, 3f), Concrete);
            Label("RAMP + DROP", new Vector3(-7f, 4.5f, 10.5f));

            // A 32 degree slope to tumble down.
            Ramp(new Vector3(7f, 0f, 2f), new Vector3(7f, 5f, 10f), 4f);
            Block("Slope Top", new Vector3(7f, 2.5f, 11.5f), new Vector3(4f, 5f, 3f), Concrete);
            Label("STEEP SLOPE", new Vector3(7f, 7f, 11.5f));

            BuildCratePyramid(new Vector3(0f, 0f, 9f));
            Label("CRATES", new Vector3(0f, 3f, 9f));

            var pad = Block("Bouncy Pad", new Vector3(0f, 0.1f, -6f), new Vector3(3.5f, 0.2f, 3.5f), Bouncy);
            pad.GetComponent<Collider>().sharedMaterial = new PhysicsMaterial("Bouncy")
            {
                bounciness = 0.95f, dynamicFriction = 0.3f, staticFriction = 0.3f, bounceCombine = PhysicsMaterialCombine.Maximum
            };
            Label("BOUNCY", new Vector3(0f, 2f, -6f));

            cannon = Block("Box Cannon", new Vector3(-4f, 1f, -10f), new Vector3(0.8f, 0.8f, 1.2f), new Color(0.25f, 0.25f, 0.28f)).transform;
            Label("BOX CANNON (F8)", new Vector3(-4f, 2.2f, -10f));

            SpawnPlayer(new Vector3(0f, 0.05f, -1f));
            SpawnRemoteStandIn();
            Label("REMOTE PLAYER VIEW", new Vector3(12f, 3f, -6f));
        }

        /// <summary>A second fisherman on a kinematic, script-moved capsule: what other players see over the network.</summary>
        void SpawnRemoteStandIn()
        {
            var go = new GameObject("Remote Player Stand-in");
            go.transform.position = new Vector3(16f, 0.02f, -6f);
            go.AddComponent<Rigidbody>();
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            Remote = go.AddComponent<RagdollRemoteStandIn>();

            var model = Instantiate(fishermanPrefab, go.transform);
            model.name = "Fisherman (remote)";
            var body = model.GetComponent<ActiveRagdollController>();
            body.root = go.transform;
            body.rootBody = go.GetComponent<Rigidbody>();
            body.knockoutOnImpact = false;   // as in NetPlayer: on the network the server decides knockdowns
            Remote.body = body;
            var palette = model.GetComponent<PaletteTint>();
            if (palette != null) palette.Apply(new Color(0.20f, 0.55f, 0.95f));   // player 2's blue
        }

        void BuildLighting()
        {
            if (FindAnyObjectByType<Light>() != null) return;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        void BuildCratePyramid(Vector3 basePoint)
        {
            const float size = 0.5f;
            for (int row = 0; row < 3; row++)
            for (int i = 0; i < 3 - row; i++)
            {
                float x = (i - (2 - row) * 0.5f) * (size + 0.02f);
                var crate = Block("Crate", basePoint + new Vector3(x, size * (row + 0.5f), 0f), Vector3.one * size, Crate);
                crate.GetComponent<Collider>().sharedMaterial = crateMaterial;
                var body = crate.AddComponent<Rigidbody>();
                body.mass = 5f;
            }
        }

        void SpawnPlayer(Vector3 position)
        {
            // Same capsule as NetPrefabBuilder.BuildPlayer, minus networking, hands and voice.
            var go = new GameObject("Test Player");
            go.transform.position = position;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 70f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);

            go.AddComponent<PlayerHealth>();
            Motor = go.AddComponent<PlayerMotor>();
            Motor.cameraPivot = pivot;

            var model = Instantiate(fishermanPrefab, go.transform);
            model.name = "Fisherman";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            Body = model.GetComponent<ActiveRagdollController>();
            Body.root = go.transform;
            Body.rootBody = body;
            Body.motor = Motor;
            Motor.body = Body;
            var palette = model.GetComponent<PaletteTint>();
            if (palette != null) palette.Apply(new Color(1f, 0.45f, 0.08f));   // player 1's orange, as in the game

            var cam = new GameObject("Follow Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>().nearClipPlane = 0.05f;
            cam.AddComponent<AudioListener>();
            var follow = cam.AddComponent<RagdollTestCamera>();
            follow.motor = Motor;
            follow.body = Body;

            TestHands = gameObject.AddComponent<RagdollTestHands>();
            TestHands.builder = this;

            var panel = gameObject.AddComponent<RagdollDebugPanel>();
            panel.builder = this;
            panel.autotest = gameObject.AddComponent<RagdollAutotest>();
            panel.autotest.builder = this;
        }

        /// <summary>A loose crate (grab and throw test).</summary>
        public Rigidbody SpawnCrate(Vector3 position, float mass = 8f)
        {
            var crate = Block("Crate", position, Vector3.one * 0.5f, Crate);
            crate.GetComponent<Collider>().sharedMaterial = crateMaterial;
            var body = crate.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return body;
        }

        /// <summary>Fire a 25 kg box at the fisherman (hit test).</summary>
        public void FireBox()
        {
            if (Body == null) return;
            var box = Block("Cannon Box", cannon.position + cannon.forward * 0.9f, Vector3.one * 0.6f, Crate);
            box.GetComponent<Collider>().sharedMaterial = crateMaterial;
            var body = box.AddComponent<Rigidbody>();
            body.mass = 25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Vector3 target = Body.Hips.position + Vector3.up * 0.2f;
            Vector3 direction = (target - box.transform.position).normalized;
            body.linearVelocity = direction * 13f + Vector3.up * 1.5f;
            cannon.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, Vector3.up));
            Destroy(box, 12f);
        }

        // ---- helpers (same shapes as GreyboxBuilder) ---------------------------------------------

        static GameObject Block(string name, Vector3 center, Vector3 size, Color color, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(color);
            return go;
        }

        static void Ramp(Vector3 bottom, Vector3 top, float width)
        {
            const float thickness = 0.3f;
            Vector3 along = top - bottom;
            Quaternion rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            Vector3 center = (bottom + top) * 0.5f - rotation * Vector3.up * (thickness * 0.5f);
            Block("Ramp", center, new Vector3(width, thickness, along.magnitude + 0.2f), Concrete, rotation);
        }

        static void Label(string text, Vector3 position)
        {
            var go = new GameObject($"Label: {text}");
            go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
            mesh.fontSize = 48;
            mesh.characterSize = 0.06f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = Color.white;
            go.AddComponent<FaceCamera>();
        }
    }
}
