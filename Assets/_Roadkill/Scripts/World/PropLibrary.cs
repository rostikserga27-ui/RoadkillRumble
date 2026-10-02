using System;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Builds the physical props from primitives. The editor tool wraps each one in networking
    /// components and saves it as a prefab; materials come from the caller so the editor can
    /// save them as assets.
    /// </summary>
    public static class PropLibrary
    {
        public static readonly Color Wood = new Color(0.55f, 0.38f, 0.22f);
        public static readonly Color Rubber = new Color(0.12f, 0.12f, 0.13f);
        public static readonly Color Metal = new Color(0.45f, 0.48f, 0.52f);
        public static readonly Color HunterOrange = new Color(1f, 0.45f, 0.05f);

        public struct Definition
        {
            public string Id;
            public Func<Func<Color, Material>, GameObject> Build;
        }

        /// <summary>Every networked prop, keyed by the prefab name it is saved under.</summary>
        public static readonly Definition[] All =
        {
            Simple("Gnome", "Garden Gnome", PrimitiveType.Capsule, new Vector3(0.25f, 0.3f, 0.25f), 3f, new Color(0.2f, 0.4f, 0.9f)),
            new Definition { Id = "Tire", Build = BuildTire },
            Simple("Battery", "Battery", PrimitiveType.Cube, new Vector3(0.35f, 0.4f, 0.25f), 30f, Rubber),
            Simple("FuelTank", "Fuel Tank", PrimitiveType.Cube, new Vector3(0.8f, 0.35f, 0.5f), 50f, Metal),
            Simple("Couch", "Couch", PrimitiveType.Cube, new Vector3(2f, 0.9f, 0.9f), 60f, new Color(0.6f, 0.25f, 0.3f)),
            Simple("Transmission", "Transmission", PrimitiveType.Cube, new Vector3(0.9f, 0.7f, 0.6f), 75f, new Color(0.3f, 0.3f, 0.25f)),
            Simple("Engine", "Engine Block", PrimitiveType.Cube, new Vector3(1f, 0.9f, 0.8f), 180f, new Color(0.25f, 0.27f, 0.3f)),
            Simple("Crate75", "Crate (75 kg)", PrimitiveType.Cube, new Vector3(1f, 0.8f, 0.8f), 75f, Wood),
            Simple("Box5", "Box (5 kg)", PrimitiveType.Cube, Vector3.one * 0.5f, 5f, new Color(0.8f, 0.7f, 0.45f)),
            new Definition { Id = "SwingingLog", Build = BuildSwingingLog },
        };

        static Definition Simple(string id, string displayName, PrimitiveType shape, Vector3 scale, float mass, Color color) =>
            new Definition
            {
                Id = id,
                Build = material =>
                {
                    var go = GameObject.CreatePrimitive(shape);
                    go.transform.localScale = scale;
                    go.GetComponent<Renderer>().sharedMaterial = material(color);
                    AddBody(go, mass, displayName);
                    return go;
                }
            };

        static GameObject BuildTire(Func<Color, Material> material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.localScale = new Vector3(0.66f, 0.12f, 0.66f);
            go.GetComponent<Renderer>().sharedMaterial = material(Rubber);
            // The primitive's capsule collider would make a tire a ball; a convex cylinder rolls like a wheel.
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = true;
            AddBody(go, 12f, "Tire");
            return go;
        }

        public const float SwingRopeLength = 4.7f;

        /// <summary>A 60 kg log on a rope. Its pivot is SwingRopeLength above wherever it is spawned.</summary>
        static GameObject BuildSwingingLog(Func<Color, Material> material)
        {
            var rig = new GameObject();

            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "Log";
            log.transform.SetParent(rig.transform, false);
            log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            log.transform.localScale = new Vector3(0.7f, 1.2f, 0.7f);
            log.GetComponent<Renderer>().sharedMaterial = material(HunterOrange);

            var rope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rope.name = "Rope";
            UnityEngine.Object.DestroyImmediate(rope.GetComponent<Collider>());
            rope.transform.SetParent(rig.transform, false);
            rope.transform.localPosition = new Vector3(0f, SwingRopeLength * 0.5f, 0f);
            rope.transform.localScale = new Vector3(0.05f, SwingRopeLength, 0.05f);
            rope.GetComponent<Renderer>().sharedMaterial = material(Wood);

            var body = AddBody(rig, 60f, "Swinging Log");
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;

            var hinge = rig.AddComponent<HingeJoint>();
            hinge.anchor = new Vector3(0f, SwingRopeLength, 0f);
            hinge.axis = Vector3.right;
            hinge.autoConfigureConnectedAnchor = true;   // pins the rope top to the world where it spawns

            rig.AddComponent<KeepSwinging>().ropeLength = SwingRopeLength;
            return rig;
        }

        static Rigidbody AddBody(GameObject go, float mass, string displayName)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 12;
            go.AddComponent<PhysicsProp>().displayName = displayName;
            go.AddComponent<ImpactReporter>();
            return body;
        }
    }

    /// <summary>Where the server spawns each prop in the greybox level.</summary>
    public static class PropLayout
    {
        public struct Placement
        {
            public string Prefab;
            public Vector3 Position;
            public Quaternion Rotation;
            public Placement(string prefab, Vector3 position) : this(prefab, position, Quaternion.identity) { }
            public Placement(string prefab, Vector3 position, Quaternion rotation)
            {
                Prefab = prefab;
                Position = position;
                Rotation = rotation;
            }
        }

        public static readonly Vector3 SwingPivot = new Vector3(-20f, 6f, -20f);

        public static Placement[] Greybox
        {
            get
            {
                var list = new System.Collections.Generic.List<Placement>
                {
                    new Placement("Gnome", new Vector3(-6f, 0.3f, -3f)),
                    new Placement("Battery", new Vector3(-1f, 0.2f, -3f)),
                    new Placement("FuelTank", new Vector3(1.5f, 0.2f, -3f)),
                    new Placement("Couch", new Vector3(4.5f, 0.45f, -3f)),
                    new Placement("Transmission", new Vector3(7.5f, 0.35f, -3f)),
                    new Placement("Engine", new Vector3(10.5f, 0.45f, -3f)),
                    new Placement("Crate75", new Vector3(-20f, 1.9f, 0.5f)),
                    new Placement("SwingingLog", SwingPivot - Vector3.up * PropLibrary.SwingRopeLength),
                };
                for (int i = 0; i < 4; i++)
                    list.Add(new Placement("Tire", new Vector3(-3.5f, 0.13f + i * 0.26f, -3f)));

                const float size = 0.5f;
                for (int row = 0; row < 4; row++)
                for (int i = 0; i < 4 - row; i++)
                {
                    float x = 8f + (i - (3 - row) * 0.5f) * (size + 0.02f);
                    list.Add(new Placement("Box5", new Vector3(x, size * 0.5f + row * size, 8f)));
                }
                return list.ToArray();
            }
        }
    }
}
