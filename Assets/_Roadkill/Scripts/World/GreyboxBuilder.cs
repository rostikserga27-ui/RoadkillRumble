using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Phase 0 test level. Every peer builds the same static geometry here; the server spawns the
    /// networked props from PropLayout. Each corner tests one rule from GDD section 3:
    /// the prop row (carry weights), two drop towers (fall ragdoll at 4 m, fall damage past 6 m),
    /// the log bridge (the phase 0 gate: carry 75 kg across a log), the swinging log (struck ragdoll)
    /// and a box pyramid (throwing).
    /// </summary>
    public class GreyboxBuilder : MonoBehaviour
    {
        static readonly Color Ground = new Color(0.36f, 0.45f, 0.3f);
        static readonly Color Concrete = new Color(0.62f, 0.62f, 0.6f);

        void Awake()
        {
            BuildLighting();
            Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(140f, 1f, 140f), Ground);
            BuildDropTowers();
            BuildLogBridge();
            BuildSwingFrame();
            BuildTrees();
            ShopStall.Build(Resources.Load<ShopCatalog>(ShopCatalog.ResourcePath), new Vector3(-10f, 0f, -7f), Quaternion.identity);

            Label("CARRY TEST: 1 hand lifts 20 kg, 1 player 40 kg, 2 players 80 kg", new Vector3(0f, 3f, -3f));
            Label("THROW TEST: hold G, release", new Vector3(8f, 3f, 8f));

            if (FindAnyObjectByType<NetSession>() == null) new GameObject("NetSession").AddComponent<NetSession>();
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

        void BuildDropTowers()
        {
            Tower("4.5 m: ragdoll, no damage", new Vector3(20f, 0f, 10f), 4.5f);
            Tower("8 m: ragdoll and 20 damage", new Vector3(32f, 0f, 14f), 8f);
        }

        void Tower(string text, Vector3 baseCenter, float height)
        {
            const float size = 4f;
            Block("Tower", baseCenter + Vector3.up * (height * 0.5f), new Vector3(size, height, size), Concrete);
            float front = baseCenter.z - size * 0.5f;
            float run = height / Mathf.Tan(25f * Mathf.Deg2Rad);
            Ramp(new Vector3(baseCenter.x, 0f, front - run), new Vector3(baseCenter.x, height, front), 2.5f);
            Label(text, baseCenter + Vector3.up * (height + 1.5f));
        }

        void BuildLogBridge()
        {
            const float x = -20f, deck = 1.5f;
            Block("Bridge Deck A", new Vector3(x, deck * 0.5f, 0f), new Vector3(4f, deck, 4f), Concrete);
            Block("Bridge Deck B", new Vector3(x, deck * 0.5f, 12f), new Vector3(4f, deck, 4f), Concrete);
            Ramp(new Vector3(x, 0f, -2f - 3.5f), new Vector3(x, deck, -2f), 2.5f);
            Ramp(new Vector3(x, 0f, 14f + 3.5f), new Vector3(x, deck, 14f), 2.5f);

            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "Log";
            log.transform.SetPositionAndRotation(new Vector3(x, deck - 0.3f, 6f), Quaternion.Euler(90f, 0f, 0f));
            log.transform.localScale = new Vector3(0.6f, 4f, 0.6f);
            log.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(PropLibrary.Wood);

            Label("PHASE 0 GATE: two players carry the 75 kg crate across the log", new Vector3(x, deck + 3f, 6f));
        }

        void BuildSwingFrame()
        {
            Vector3 pivot = PropLayout.SwingPivot;
            Block("Swing Post L", new Vector3(pivot.x - 2f, 3f, pivot.z), new Vector3(0.3f, 6f, 0.3f), PropLibrary.Wood);
            Block("Swing Post R", new Vector3(pivot.x + 2f, 3f, pivot.z), new Vector3(0.3f, 6f, 0.3f), PropLibrary.Wood);
            Block("Swing Beam", pivot, new Vector3(4.3f, 0.3f, 0.3f), PropLibrary.Wood);
            Label("STRUCK TEST: 60 kg log, stand in its path", pivot + Vector3.up * 1.5f);
        }

        void BuildTrees()
        {
            var rng = new System.Random(13);
            for (int i = 0; i < 40; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float radius = 45f + (float)rng.NextDouble() * 20f;
                Vector3 p = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Block("Trunk", p + Vector3.up * 3f, new Vector3(0.6f, 6f, 0.6f), PropLibrary.Wood);
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Crown";
                crown.transform.position = p + Vector3.up * 7f;
                crown.transform.localScale = Vector3.one * 4f;
                crown.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(new Color(0.18f, 0.42f, 0.2f));
            }
        }

        // ---- helpers -------------------------------------------------------------------------

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

    /// <summary>Turns a world label toward the active camera.</summary>
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
