using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Generates the network prefabs (player and props) into Resources/RoadkillNet, plus their
    /// materials. Netcode can only spawn prefab assets, so the code-built objects are saved here.
    /// Runs on its own when the prefabs are missing or BuildVersion changes; also under the
    /// Roadkill menu.
    /// </summary>
    [InitializeOnLoad]
    public static class NetPrefabBuilder
    {
        const string Root = "Assets/_Roadkill/Resources/" + NetSession.ResourceFolder;
        const string MaterialFolder = "Assets/_Roadkill/Generated/Materials";
        const string VersionFile = "Assets/_Roadkill/Generated/NetPrefabVersion.txt";
        // Bump when the player or prop builders change, so every machine regenerates.
        const string BuildVersion = "8";

        static readonly Color Skin = new Color(1f, 0.8f, 0.62f);

        static NetPrefabBuilder()
        {
            EditorApplication.delayCall += BuildIfStale;
        }

        static void BuildIfStale()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool missing = !File.Exists($"{Root}/{NetSession.PlayerPrefabName}.prefab");
            bool stale = !File.Exists(VersionFile) || File.ReadAllText(VersionFile).Trim() != BuildVersion;
            if (missing || stale) Build();
        }

        [MenuItem("Roadkill/Rebuild Network Prefabs")]
        public static void Build()
        {
            EnsureFolder(Root);
            EnsureFolder(MaterialFolder);

            var ragdollPrefab = FishermanRagdollBuilder.BuildPrefab();
            foreach (var definition in PropLibrary.All)
            {
                var go = definition.Build(MaterialFor);
                go.name = definition.Id;
                AddNetworking(go, NetworkTransform.AuthorityModes.Server);
                Save(go, definition.Id);
            }
            Save(BuildPlayer(ragdollPrefab), NetSession.PlayerPrefabName);

            File.WriteAllText(VersionFile, BuildVersion);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssignNetworkIds();
            Debug.Log($"Roadkill: network prefabs built in {Root}");
        }

        /// <summary>
        /// Netcode tells prefabs apart by GlobalObjectIdHash, which NetworkObject.OnValidate derives from
        /// the saved asset. A prefab saved straight from code keeps 0 (or a stale copied value), and then
        /// every prefab shares one id and clients spawn the wrong object. Run it on the saved assets.
        /// </summary>
        static void AssignNetworkIds()
        {
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (onValidate == null)
            {
                Debug.LogWarning("Roadkill: NetworkObject.OnValidate not found; network prefab ids were not assigned.");
                return;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
                if (networkObject == null) continue;
                onValidate.Invoke(networkObject, null);
                EditorUtility.SetDirty(prefab);
            }
            AssetDatabase.SaveAssets();
        }

        static GameObject BuildPlayer(GameObject ragdollPrefab)
        {
            var go = new GameObject(NetSession.PlayerPrefabName);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 70f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);

            // First-person view (enabled only for the owner at spawn).
            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);
            var cameraGo = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cameraGo.transform.SetParent(pivot, false);
            var cam = cameraGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(90f, 16f / 9f);   // 90 degrees horizontal
            cam.enabled = false;
            var listener = cameraGo.AddComponent<AudioListener>();
            listener.enabled = false;

            // What other players see: the fisherman's active ragdoll if the model is imported, otherwise primitives.
            var renderers = new List<Renderer>();
            Transform head, mouth;
            Renderer tint = null;
            ActiveRagdollController ragdoll = null;
            PaletteTint palette = null;
            if (ragdollPrefab != null)
                BuildCharacterModel(go, body, ragdollPrefab, renderers, out head, out mouth, out palette, out ragdoll);
            else
                BuildPrimitiveBody(go, renderers, out head, out mouth, out tint);

            AddNetworking(go, NetworkTransform.AuthorityModes.Owner);

            go.AddComponent<PlayerHealth>();
            var motor = go.AddComponent<PlayerMotor>();
            motor.cameraPivot = pivot;
            motor.enabled = false;
            if (ragdoll != null) ragdoll.motor = motor;
            var hands = go.AddComponent<HandsController>();
            hands.viewCamera = cam;
            hands.handModel = AssetDatabase.LoadAssetAtPath<GameObject>(HandModelPath);
            var voice = go.AddComponent<VoiceChat>();
            voice.mouthPoint = head;
            voice.mouthVisual = mouth;
            var hud = go.AddComponent<DebugHud>();
            hud.enabled = false;
            var net = go.AddComponent<PlayerNet>();
            net.playerCamera = cam;
            net.listener = listener;
            net.head = head;
            net.bodyRenderers = renderers.ToArray();
            net.tintRenderers = tint != null ? new[] { tint } : new Renderer[0];
            net.palette = palette;
            net.body = ragdoll;
            return go;
        }

        const string HandModelPath = CharacterModelImport.FishermanFolder + "FisherFPArm.fbx";

        /// <summary>
        /// The fisherman (Generated/Fisherman_Ragdoll.prefab, nested so it stays one source of truth) wired
        /// to this player's capsule. Remote copies run the active ragdoll; the owner switches it off at spawn.
        /// </summary>
        static void BuildCharacterModel(GameObject player, Rigidbody playerBody, GameObject ragdollPrefab, List<Renderer> renderers,
            out Transform head, out Transform mouth, out PaletteTint palette, out ActiveRagdollController ragdoll)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(ragdollPrefab, player.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            head = FindDeep(model.transform, "Head");
            mouth = FindDeep(model.transform, "Jaw");
            var toes = FindDeep(model.transform, "LeftToes");
            if (toes != null && player.transform.InverseTransformPoint(toes.position).z < 0f)
                Debug.LogWarning("Roadkill: the fisherman model faces backwards; re-export it facing +Z (see ArtSource/Fisherman).");

            foreach (var r in model.GetComponentsInChildren<Renderer>()) renderers.Add(r);

            ragdoll = model.GetComponent<ActiveRagdollController>();
            ragdoll.root = player.transform;
            ragdoll.rootBody = playerBody;
            // On the network the server decides knockdowns (ImpactReporter -> PlayerMotor); local hits only stagger.
            ragdoll.knockoutOnImpact = false;
            palette = model.GetComponent<PaletteTint>();
        }

        /// <summary>The old stand-in body: capsule, sphere head, googly eyes.</summary>
        static void BuildPrimitiveBody(GameObject go, List<Renderer> renderers, out Transform head, out Transform mouth, out Renderer tint)
        {
            var torso = Part(PrimitiveType.Capsule, "Torso", go.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 0.75f, 0.7f), Color.white, renderers);
            head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, Vector3.one * 0.42f, Skin, renderers);
            Part(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.09f, 0.05f, 0.18f), Vector3.one * 0.11f, Color.white, renderers);
            Part(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.09f, 0.05f, 0.18f), Vector3.one * 0.11f, Color.white, renderers);
            Part(PrimitiveType.Sphere, "PupilL", head, new Vector3(-0.09f, 0.05f, 0.23f), Vector3.one * 0.05f, Color.black, renderers);
            Part(PrimitiveType.Sphere, "PupilR", head, new Vector3(0.09f, 0.05f, 0.23f), Vector3.one * 0.05f, Color.black, renderers);
            mouth = Part(PrimitiveType.Cube, "Mouth", head, new Vector3(0f, -0.09f, 0.19f), new Vector3(0.14f, 0.03f, 0.04f), new Color(0.35f, 0.08f, 0.1f), renderers).transform;
            tint = torso.GetComponent<Renderer>();
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

        static GameObject Part(PrimitiveType shape, string name, Transform parent, Vector3 localPosition, Vector3 scale,
            Color color, List<Renderer> renderers)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            var r = part.GetComponent<Renderer>();
            r.sharedMaterial = MaterialFor(color);
            renderers.Add(r);
            return part;
        }

        static void AddNetworking(GameObject go, NetworkTransform.AuthorityModes authority)
        {
            go.AddComponent<NetworkObject>();
            var transform = go.AddComponent<NetworkTransform>();
            transform.AuthorityMode = authority;
            transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
            var rigidbody = go.AddComponent<NetworkRigidbody>();
            rigidbody.UseRigidBodyForMotion = true;
        }

        static void Save(GameObject go, string prefabName)
        {
            PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/{prefabName}.prefab");
            Object.DestroyImmediate(go);
        }

        static Material MaterialFor(Color color)
        {
            string path = $"{MaterialFolder}/Flat_{ColorUtility.ToHtmlStringRGB(color)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(RkMaterials.Get(color));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
