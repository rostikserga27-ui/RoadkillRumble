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
        const string BuildVersion = "7";

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

            // What other players see: the Blender character if it is imported, otherwise primitives.
            var renderers = new List<Renderer>();
            Transform head, mouth;
            Renderer tint;
            RagdollRig ragdoll = null;
            CharacterAnimator animator = null;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
            if (model != null)
                BuildCharacterModel(go, body, model, renderers, out head, out mouth, out tint, out ragdoll, out animator);
            else
                BuildPrimitiveBody(go, renderers, out head, out mouth, out tint);

            AddNetworking(go, NetworkTransform.AuthorityModes.Owner);

            go.AddComponent<PlayerHealth>();
            var motor = go.AddComponent<PlayerMotor>();
            motor.cameraPivot = pivot;
            motor.enabled = false;
            var hands = go.AddComponent<HandsController>();
            hands.viewCamera = cam;
            hands.handModel = AssetDatabase.LoadAssetAtPath<GameObject>(HandModelPath);
            var voice = go.AddComponent<VoiceChat>();
            voice.mouthPoint = head;
            voice.mouthVisual = mouth;
            var hud = go.AddComponent<DebugHud>();
            hud.enabled = false;

            // Shop, wallet and hotbar: switched on for the local player only (PlayerNet).
            var money = go.AddComponent<MoneyManager>();
            money.enabled = false;
            var hotbar = go.AddComponent<HotbarInventory>();
            hotbar.enabled = false;
            var shop = go.AddComponent<ShopInteractor>();
            shop.Setup(cam, money, hotbar, motor);
            shop.enabled = false;
            var hotbarUI = go.AddComponent<HotbarUI>();
            hotbarUI.Setup(hotbar, money);
            hotbarUI.enabled = false;
            go.AddComponent<PlayerTools>().Setup(ShopAssetBuilder.EnsureAssets(), hotbar, cam);
            var net = go.AddComponent<PlayerNet>();
            net.playerCamera = cam;
            net.listener = listener;
            net.head = head;
            net.bodyRenderers = renderers.ToArray();
            net.tintRenderers = new[] { tint };
            net.ragdoll = ragdoll;
            net.animator = animator;
            return go;
        }

        const string CharacterModelPath = "Assets/_Roadkill/Art/Character/Character.fbx";
        const string HandModelPath = "Assets/_Roadkill/Art/Character/FPArm.fbx";

        // Ragdoll bones, parents before children: bone, the bone it points at, radius (m), mass (kg),
        // twist and swing limits (degrees). Hands, feet, neck and jaw ride on their parents.
        // Masses add up to about the player's 70 kg, since the ragdoll is now the body props hit.
        static readonly (string bone, string toward, float radius, float mass, float twist, float swing)[] RagdollBones =
        {
            ("Hips", "Spine", 0.13f, 10f, 15f, 15f),
            ("Spine", "Chest", 0.12f, 8f, 15f, 20f),
            ("Chest", "Neck", 0.13f, 8f, 15f, 20f),
            ("Head", null, 0.19f, 6f, 30f, 40f),
            ("UpperArm_L", "LowerArm_L", 0.05f, 2.5f, 60f, 70f),
            ("LowerArm_L", "Hand_L", 0.045f, 2f, 80f, 15f),
            ("UpperArm_R", "LowerArm_R", 0.05f, 2.5f, 60f, 70f),
            ("LowerArm_R", "Hand_R", 0.045f, 2f, 80f, 15f),
            ("UpperLeg_L", "LowerLeg_L", 0.07f, 7f, 40f, 50f),
            ("LowerLeg_L", "Foot_L", 0.06f, 5f, 80f, 10f),
            ("UpperLeg_R", "LowerLeg_R", 0.07f, 7f, 40f, 50f),
            ("LowerLeg_R", "Foot_R", 0.06f, 5f, 80f, 10f),
        };

        static void BuildCharacterModel(GameObject player, Rigidbody playerBody, GameObject modelAsset, List<Renderer> renderers,
            out Transform head, out Transform mouth, out Renderer tint, out RagdollRig ragdoll, out CharacterAnimator animator)
        {
            var model = Object.Instantiate(modelAsset, player.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            // CharacterAnimator drives the bones; an idle Animator would only cost time.
            var importedAnimator = model.GetComponent<Animator>();
            if (importedAnimator != null) Object.DestroyImmediate(importedAnimator);

            head = FindDeep(model.transform, "Head");
            mouth = FindDeep(model.transform, "Jaw");
            // The jaw sits in front of the head; if it ended up behind, the export faces backwards.
            if (player.transform.InverseTransformPoint(mouth.position).z < player.transform.InverseTransformPoint(head.position).z)
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            tint = null;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                renderers.Add(r);
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;   // bounds follow the ragdoll
                if (r.name == "Overalls") tint = r;
            }

            var bodies = new List<Rigidbody>();
            var colliders = new List<Collider>();
            var byBone = new Dictionary<Transform, Rigidbody>();
            Transform up = player.transform;
            foreach (var spec in RagdollBones)
            {
                var bone = FindDeep(model.transform, spec.bone);
                if (bone == null) continue;
                float unit = 1f / Mathf.Max(0.0001f, bone.lossyScale.x);   // metres to bone-local units

                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = spec.mass;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.angularDamping = 0.5f;
                rb.solverIterations = 10;

                Collider collider;
                if (spec.toward == null)
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = spec.radius * unit;
                    sphere.center = bone.InverseTransformPoint(bone.position + up.up * 0.14f);
                    collider = sphere;
                }
                else
                {
                    var target = FindDeep(model.transform, spec.toward);
                    Vector3 local = bone.InverseTransformPoint(target.position);
                    Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                    var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : abs.y > abs.z ? 1 : 2;
                    capsule.center = local * 0.5f;
                    capsule.radius = spec.radius * unit;
                    capsule.height = local.magnitude + spec.radius * unit * 2f;
                    collider = capsule;
                }

                for (var parent = bone.parent; parent != null && parent != model.transform; parent = parent.parent)
                {
                    if (!byBone.TryGetValue(parent, out var parentBody)) continue;
                    var joint = bone.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = parentBody;
                    joint.axis = bone.InverseTransformDirection(up.right);
                    joint.swingAxis = bone.InverseTransformDirection(up.forward);
                    joint.lowTwistLimit = new SoftJointLimit { limit = -spec.twist };
                    joint.highTwistLimit = new SoftJointLimit { limit = spec.twist };
                    joint.swing1Limit = new SoftJointLimit { limit = spec.swing };
                    joint.swing2Limit = new SoftJointLimit { limit = spec.swing };
                    joint.enableProjection = true;
                    break;
                }

                bodies.Add(rb);
                colliders.Add(collider);
                byBone[bone] = rb;
            }

            ragdoll = model.AddComponent<RagdollRig>();
            ragdoll.root = playerBody;
            ragdoll.hips = byBone[FindDeep(model.transform, "Hips")];
            ragdoll.bodies = bodies.ToArray();
            ragdoll.colliders = colliders.ToArray();

            animator = model.AddComponent<CharacterAnimator>();
            animator.root = player.transform;
            animator.ragdoll = ragdoll;
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

        internal static Material MaterialFor(Color color)
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
