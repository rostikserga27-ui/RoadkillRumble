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
        const string BuildVersion = "2";

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

            foreach (var definition in PropLibrary.All)
            {
                var go = definition.Build(MaterialFor);
                go.name = definition.Id;
                AddNetworking(go, NetworkTransform.AuthorityModes.Server);
                Save(go, definition.Id);
            }
            Save(BuildPlayer(), NetSession.PlayerPrefabName);

            File.WriteAllText(VersionFile, BuildVersion);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Roadkill: network prefabs built in {Root}");
        }

        static GameObject BuildPlayer()
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

            // What other players see: a goofy capsule with a head that follows the owner's view.
            var renderers = new List<Renderer>();
            var torso = Part(PrimitiveType.Capsule, "Torso", go.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 0.75f, 0.7f), Color.white, renderers);
            var head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, Vector3.one * 0.42f, Skin, renderers);
            Part(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.09f, 0.05f, 0.18f), Vector3.one * 0.11f, Color.white, renderers);
            Part(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.09f, 0.05f, 0.18f), Vector3.one * 0.11f, Color.white, renderers);
            Part(PrimitiveType.Sphere, "PupilL", head, new Vector3(-0.09f, 0.05f, 0.23f), Vector3.one * 0.05f, Color.black, renderers);
            Part(PrimitiveType.Sphere, "PupilR", head, new Vector3(0.09f, 0.05f, 0.23f), Vector3.one * 0.05f, Color.black, renderers);
            var mouth = Part(PrimitiveType.Cube, "Mouth", head, new Vector3(0f, -0.09f, 0.19f), new Vector3(0.14f, 0.03f, 0.04f), new Color(0.35f, 0.08f, 0.1f), renderers);

            AddNetworking(go, NetworkTransform.AuthorityModes.Owner);

            go.AddComponent<PlayerHealth>();
            var motor = go.AddComponent<PlayerMotor>();
            motor.cameraPivot = pivot;
            motor.enabled = false;
            var hands = go.AddComponent<HandsController>();
            hands.viewCamera = cam;
            var voice = go.AddComponent<VoiceChat>();
            voice.mouthPoint = head;
            voice.mouthVisual = mouth.transform;
            var hud = go.AddComponent<DebugHud>();
            hud.enabled = false;
            var net = go.AddComponent<PlayerNet>();
            net.playerCamera = cam;
            net.listener = listener;
            net.head = head;
            net.bodyRenderers = renderers.ToArray();
            net.tintRenderers = new[] { torso.GetComponent<Renderer>() };
            return go;
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
