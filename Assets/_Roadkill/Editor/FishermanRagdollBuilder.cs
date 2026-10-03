using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Role = Roadkill.ActiveRagdollController.Role;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Builds the fisherman's active ragdoll onto the imported model and saves it as
    /// Generated/Fisherman_Ragdoll.prefab (used inside NetPlayer and by the RagdollTest scene).
    /// Hand-built rather than Ragdoll Wizard defaults: one rigidbody per major bone, colliders fitted to
    /// the vertices skinned to that bone and shrunk a little (no self-collision jitter), ConfigurableJoints
    /// with one-way elbows and knees, a limited neck and wide shoulders and hips.
    /// </summary>
    public static class FishermanRagdollBuilder
    {
        public const string ModelPath = CharacterModelImport.FishermanFolder + "SK_Fisherman.fbx";
        public const string PrefabPath = "Assets/_Roadkill/Generated/Fisherman_Ragdoll.prefab";
        const string MaterialFolder = "Assets/_Roadkill/Generated/Materials";

        // ConfigurableJoint angular X runs opposite to a world rotation about the joint axis. Measured in Play
        // mode: with -1 a knee driven to fold back folds back, and a kick forward stops at the 3 degree limit.
        const float JointAngleSign = -1f;

        enum Shape { Box, Capsule }

        // Parents before children. mass kg, strength = share of the controller's Joint Spring,
        // angular damping. Hands and feet are light; head medium-heavy so it wobbles instead of steering.
        static readonly (string bone, string toward, Role role, Shape shape, float mass, float strength, float angularDamping)[] Bones =
        {
            ("Hips", "Spine", Role.Hips, Shape.Box, 8f, 1f, 2f),
            ("Spine", "Chest", Role.Spine, Shape.Box, 5f, 2.5f, 2f),
            ("Chest", "Neck", Role.Chest, Shape.Box, 8f, 2f, 2f),
            ("Head", null, Role.Head, Shape.Capsule, 3.5f, 0.35f, 1.2f),
            ("LeftUpperArm", "LeftLowerArm", Role.UpperArm, Shape.Capsule, 1.6f, 0.2f, 0.6f),
            ("LeftLowerArm", "LeftHand", Role.LowerArm, Shape.Capsule, 1.1f, 0.1f, 0.6f),
            ("LeftHand", null, Role.Hand, Shape.Capsule, 0.5f, 0.04f, 2.5f),
            ("RightUpperArm", "RightLowerArm", Role.UpperArm, Shape.Capsule, 1.6f, 0.2f, 0.6f),
            ("RightLowerArm", "RightHand", Role.LowerArm, Shape.Capsule, 1.1f, 0.1f, 0.6f),
            ("RightHand", null, Role.Hand, Shape.Capsule, 0.5f, 0.04f, 2.5f),
            ("LeftUpperLeg", "LeftLowerLeg", Role.UpperLeg, Shape.Capsule, 3.5f, 2.6f, 0.8f),
            ("LeftLowerLeg", "LeftFoot", Role.LowerLeg, Shape.Capsule, 2.2f, 1.4f, 0.8f),
            ("LeftFoot", null, Role.Foot, Shape.Box, 0.8f, 0.3f, 0.8f),
            ("RightUpperLeg", "RightLowerLeg", Role.UpperLeg, Shape.Capsule, 3.5f, 2.6f, 0.8f),
            ("RightLowerLeg", "RightFoot", Role.LowerLeg, Shape.Capsule, 2.2f, 1.4f, 0.8f),
            ("RightFoot", null, Role.Foot, Shape.Box, 0.8f, 0.3f, 0.8f),
        };

        // Bones without a body give their vertices to the body they ride on.
        static readonly Dictionary<string, string> RideAlong = new Dictionary<string, string>
        {
            { "Jaw", "Head" }, { "LeftShoulder", "Chest" }, { "RightShoulder", "Chest" }, { "LeftToes", "LeftFoot" }, { "RightToes", "RightFoot" }
        };

        public static bool ModelAvailable => AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null;

        public static GameObject BuildPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return null;
            // Read/Write stays off in the shipped import; reimport readable just long enough to fit the colliders.
            CharacterModelImport.ForceReadable = true;
            GameObject model = null;
            try
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
                model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            }
            finally
            {
                CharacterModelImport.ForceReadable = false;
            }
            model.name = "Fisherman_Ragdoll";
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                Build(model);
                return PrefabUtility.SaveAsPrefabAsset(model, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(model);   // never leave the temporary build in the open scene
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            }
        }

        static void Build(GameObject model)
        {
            // Posed by physics and code; the Humanoid avatar stays on the import for later retargeting.
            var animator = model.GetComponent<Animator>();
            if (animator != null) Object.DestroyImmediate(animator);

            var root = model.transform;
            var flesh = PhysicsMaterialAsset("Ragdoll_Flesh", 0.6f, 0.7f, 0.15f, PhysicsMaterialCombine.Average);
            // Slippery soles: the feet slide and shuffle instead of catching and tripping him.
            var flipFlop = PhysicsMaterialAsset("Ragdoll_FlipFlop", 0.3f, 0.35f, 0.05f, PhysicsMaterialCombine.Minimum);
            var vertices = VerticesByBone(model);

            var controller = model.AddComponent<ActiveRagdollController>();
            var parts = new List<ActiveRagdollController.Part>();
            var byBone = new Dictionary<Transform, Rigidbody>();

            foreach (var spec in Bones)
            {
                var bone = FindDeep(root, spec.bone);
                var points = vertices.TryGetValue(spec.bone, out var list) ? list : new List<Vector3>();
                Transform toward = spec.toward != null ? FindDeep(root, spec.toward) : null;

                var body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = spec.mass;
                body.linearDamping = spec.role == Role.Foot ? 1.5f : 0.05f;   // flip-flops drag: he shuffles
                body.angularDamping = spec.angularDamping;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                // Pelvis and chest fully continuous; every other bone speculative, so a hard landing after a
                // launch cannot push a limb through the floor.
                body.collisionDetectionMode = spec.role == Role.Hips || spec.role == Role.Chest ? CollisionDetectionMode.ContinuousDynamic
                    : CollisionDetectionMode.ContinuousSpeculative;

                var collider = spec.shape == Shape.Box
                    ? FitBox(bone, points, spec.role == Role.Foot)
                    : FitCapsule(bone, points, Direction(bone, toward, spec.role));
                collider.sharedMaterial = spec.role == Role.Foot ? flipFlop : flesh;

                var joint = spec.role == Role.Hips ? null : AddJoint(bone, ParentBody(bone, byBone), spec.role, toward);
                bone.gameObject.AddComponent<RagdollBodyPart>().controller = controller;

                parts.Add(new ActiveRagdollController.Part
                {
                    role = spec.role,
                    body = body,
                    joint = joint,
                    side = spec.bone.StartsWith("Left") ? -1f : spec.bone.StartsWith("Right") ? 1f : 0f,
                    strength = spec.strength
                });
                byBone[bone] = body;
            }

            controller.parts = parts.ToArray();
            controller.hipHeight = FindDeep(root, "Hips").position.y - root.position.y;

            var tint = model.AddComponent<PaletteTint>();
            tint.renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name.EndsWith("Clothes")).ToArray<Renderer>();
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;   // bounds follow the ragdoll
        }

        static Rigidbody ParentBody(Transform bone, Dictionary<Transform, Rigidbody> byBone)
        {
            for (var p = bone.parent; p != null; p = p.parent)
                if (byBone.TryGetValue(p, out var body)) return body;
            return null;
        }

        /// <summary>Which way a capsule runs: toward the child bone, along the hand, or up for the head.</summary>
        static Vector3 Direction(Transform bone, Transform toward, Role role)
        {
            if (toward != null) return (toward.position - bone.position).normalized;
            if (role == Role.Head) return Vector3.up;
            var lower = bone.parent;   // hand: carry on in the forearm's direction
            return (bone.position - lower.position).normalized;
        }

        // --------------------------------------------------------------------------- joints

        static ConfigurableJoint AddJoint(Transform bone, Rigidbody parent, Role role, Transform toward)
        {
            var joint = bone.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            // Hard landings would otherwise pull light limbs centimetres out of their sockets: project them
            // back, and let a heavy parent count as no more than 3x its child inside this joint.
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.02f;
            joint.projectionAngle = 10f;
            var child = bone.GetComponent<Rigidbody>();
            joint.connectedMassScale = Mathf.Clamp(parent.mass / (child.mass * 3f), 1f, 4f);

            Vector3 right = Vector3.right, up = Vector3.up, forward = Vector3.forward;
            Vector3 along = Direction(bone, toward, role);
            Vector3 axis, secondary;
            float flex, extend, swingY, swingZ;
            switch (role)
            {
                case Role.Spine:
                case Role.Chest:
                    axis = right; secondary = up; flex = 30f; extend = 25f; swingY = 20f; swingZ = 20f; break;
                case Role.Head:   // nod, turn (about up), tilt
                    axis = right; secondary = up; flex = 40f; extend = 40f; swingY = 60f; swingZ = 30f; break;
                case Role.UpperArm:   // wide ball joint: twist about the arm, swing in both other planes
                    axis = along; secondary = forward; flex = 45f; extend = 45f; swingY = 55f; swingZ = 95f; break;
                case Role.LowerArm:   // elbow: folds forward only
                    axis = FlexAxis(along, forward, Vector3.Cross(along, forward)); secondary = along; flex = 135f; extend = 4f; swingY = 15f; swingZ = 6f; break;
                case Role.Hand:
                    axis = FlexAxis(along, forward, Vector3.Cross(along, forward)); secondary = along; flex = 55f; extend = 50f; swingY = 30f; swingZ = 25f; break;
                case Role.UpperLeg:   // hip: wide, more forward than back
                    axis = FlexAxis(along, forward, right); secondary = along; flex = 100f; extend = 30f; swingY = 30f; swingZ = 40f; break;
                case Role.LowerLeg:   // knee: folds backward only
                    axis = FlexAxis(along, -forward, right); secondary = along; flex = 140f; extend = 3f; swingY = 5f; swingZ = 5f; break;
                default:              // foot
                    axis = right; secondary = up; flex = 35f; extend = 35f; swingY = 12f; swingZ = 15f; break;
            }
            joint.axis = bone.InverseTransformDirection(axis);
            joint.secondaryAxis = bone.InverseTransformDirection(secondary);
            joint.lowAngularXLimit = new SoftJointLimit { limit = JointAngleSign > 0f ? -extend : -flex };
            joint.highAngularXLimit = new SoftJointLimit { limit = JointAngleSign > 0f ? flex : extend };
            joint.angularYLimit = new SoftJointLimit { limit = swingY };
            joint.angularZLimit = new SoftJointLimit { limit = swingZ };
            joint.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 1f, maximumForce = 1500f };
            return joint;
        }

        /// <summary>The axis (±candidate) a positive world rotation about which moves the bone's tip toward `toward`.</summary>
        static Vector3 FlexAxis(Vector3 along, Vector3 toward, Vector3 candidate)
        {
            candidate.Normalize();
            Vector3 moved = Quaternion.AngleAxis(10f, candidate) * along - along;
            return Vector3.Dot(moved, toward) >= 0f ? candidate : -candidate;
        }

        // --------------------------------------------------------------------------- colliders

        /// <summary>Capsule along `direction` through the bone's vertices, radius at the 80th percentile, shrunk 10%.</summary>
        static Collider FitCapsule(Transform bone, List<Vector3> points, Vector3 direction)
        {
            var holder = Holder(bone, Quaternion.FromToRotation(Vector3.up, direction));
            var ts = new List<float>();
            var rs = new List<float>();
            foreach (var p in points)
            {
                Vector3 d = p - bone.position;
                float t = Vector3.Dot(d, direction);
                ts.Add(t);
                rs.Add((d - direction * t).magnitude);
            }
            float t0 = Percentile(ts, 0.04f), t1 = Percentile(ts, 0.96f);
            float radius = Percentile(rs, 0.8f) * 0.9f;
            float height = Mathf.Max((t1 - t0) * 0.92f, radius * 2.05f);
            holder.position = bone.position + direction * ((t0 + t1) * 0.5f);
            var capsule = holder.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = height;
            return capsule;
        }

        /// <summary>World-aligned box over the bone's vertices, shrunk 10% sideways. Feet keep their full depth so the soles sit on the ground.</summary>
        static Collider FitBox(Transform bone, List<Vector3> points, bool foot)
        {
            var holder = Holder(bone, Quaternion.identity);
            Vector3 lo, hi;
            lo.x = Percentile(points.Select(p => p.x).ToList(), 0.02f); hi.x = Percentile(points.Select(p => p.x).ToList(), 0.98f);
            lo.y = foot ? points.Min(p => p.y) : Percentile(points.Select(p => p.y).ToList(), 0.02f);
            hi.y = Percentile(points.Select(p => p.y).ToList(), 0.98f);
            lo.z = Percentile(points.Select(p => p.z).ToList(), 0.02f); hi.z = Percentile(points.Select(p => p.z).ToList(), 0.98f);
            Vector3 size = hi - lo;
            Vector3 center = (lo + hi) * 0.5f;
            size.x *= 0.9f; size.z *= 0.9f;
            if (!foot) { size.y *= 0.9f; }
            else { size.y = Mathf.Max(size.y, 0.03f); center.y = lo.y + size.y * 0.5f; }
            holder.position = center;
            var box = holder.gameObject.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        static Transform Holder(Transform bone, Quaternion rotation)
        {
            var holder = new GameObject("Collider").transform;
            holder.SetParent(bone, false);
            holder.rotation = rotation;
            return holder;
        }

        static float Percentile(List<float> values, float q)
        {
            if (values.Count == 0) return 0f;
            values.Sort();
            return values[Mathf.Clamp(Mathf.RoundToInt(q * (values.Count - 1)), 0, values.Count - 1)];
        }

        /// <summary>World-space rest positions of every skinned vertex, grouped by the body bone it is mostly weighted to.</summary>
        static Dictionary<string, List<Vector3>> VerticesByBone(GameObject model)
        {
            var result = new Dictionary<string, List<Vector3>>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = smr.sharedMesh;
                var vertices = mesh.vertices;
                var weights = mesh.GetAllBoneWeights();      // per vertex, heaviest first
                var perVertex = mesh.GetBonesPerVertex();
                var bindposes = mesh.bindposes;
                for (int i = 0, w = 0; i < vertices.Length; w += perVertex[i], i++)
                {
                    int b = weights[w].boneIndex;
                    var bone = smr.bones[b];
                    string name = RideAlong.TryGetValue(bone.name, out var host) ? host : bone.name;
                    if (Bones.All(s => s.bone != name)) continue;   // the neck belongs to nobody
                    Vector3 world = bone.localToWorldMatrix.MultiplyPoint3x4(bindposes[b].MultiplyPoint3x4(vertices[i]));
                    if (!result.TryGetValue(name, out var list)) result[name] = list = new List<Vector3>();
                    list.Add(world);
                }
            }
            return result;
        }

        static PhysicsMaterial PhysicsMaterialAsset(string name, float dynamicFriction, float staticFriction, float bounce, PhysicsMaterialCombine friction)
        {
            string path = $"{MaterialFolder}/{name}.physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }
            material.dynamicFriction = dynamicFriction;
            material.staticFriction = staticFriction;
            material.bounciness = bounce;
            material.frictionCombine = friction;
            material.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);
            return material;
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
    }
}
