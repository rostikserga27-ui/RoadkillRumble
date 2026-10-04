using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Roadkill
{
    /// <summary>
    /// A little parked mini-van beside the capybara, back doors swung open: throw loot into the cargo
    /// area to sell it (SellZone). Built from primitives with static colliders, so props bounce off the
    /// sides and roof; only the cargo has the trigger. Plays the sale feedback (cha-ching, a burst of
    /// cash, "+$X") for sales near it and shows "Sold today" on both sides. Local scenery like the shops:
    /// the editor tool Roadkill > Build Sell Van saves it as Resources/Shop/SellVan, and the greybox
    /// places that prefab (or builds one on the spot if the prefab is missing). The van faces its local
    /// +Z; the open back is at -Z.
    /// </summary>
    public class SellVan : MonoBehaviour
    {
        public const string ResourcePath = "Shop/SellVan";

        static readonly Color Paint = new Color(0.52f, 0.77f, 0.72f);
        static readonly Color Cream = new Color(0.96f, 0.92f, 0.8f);
        static readonly Color Glass = new Color(0.16f, 0.22f, 0.3f);
        static readonly Color Chrome = new Color(0.75f, 0.76f, 0.78f);
        static readonly Color TailLight = new Color(0.85f, 0.12f, 0.1f);
        static readonly Color HeadLight = new Color(1f, 0.93f, 0.6f);
        static readonly Color CashGreen = new Color(0.35f, 0.85f, 0.35f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.25f);

        // Body dimensions (metres).
        const float Width = 1.9f, Length = 4.3f, FloorTop = 0.55f, RoofTop = 2f, Wall = 0.08f;
        public const float Rear = -Length * 0.5f, CargoFront = 0.55f;   // the open back, the bulkhead behind the seats

        [SerializeField] SellZone zone;
        [SerializeField] TextMesh[] counters;
        [SerializeField] AudioSource audioSource;
        [Tooltip("Sales this close to the van play their feedback here.")]
        [SerializeField] float feedbackRange = 8f;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        static AudioClip chaChing;
        static Material textMaterial;
        int shownTotal = -1;

        public SellZone Zone => zone;

        /// <summary>Put the van in the world: the saved prefab if there is one, otherwise built from code.</summary>
        public static SellVan Place(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var prefab = Resources.Load<SellVan>(ResourcePath);
            SellVan van = prefab != null ? Instantiate(prefab, position, rotation, parent) : Build(RkMaterials.Get, parent);
            van.name = "Sell Van";
            van.transform.SetPositionAndRotation(position, rotation);
            return van;
        }

        /// <summary>Build the van from primitives. `material` gives a material per colour (assets in the editor).</summary>
        public static SellVan Build(Func<Color, Material> material, Transform parent = null)
        {
            var root = new GameObject("Sell Van").transform;
            root.SetParent(parent, false);
            var van = root.gameObject.AddComponent<SellVan>();

            // Chassis and cargo floor, one piece from bumper to bumper; the wheels poke out below.
            Part(root, "Chassis", new Vector3(0f, 0.42f, 0f), new Vector3(Width, 0.26f, Length), Paint, material, true);
            Part(root, "Cargo Floor", new Vector3(0f, FloorTop + 0.005f, (Rear + CargoFront) * 0.5f),
                new Vector3(Width - Wall * 2f, 0.01f, CargoFront - Rear), PropLibrary.Wood, material, false);
            foreach (float x in new[] { -0.82f, 0.82f })
            foreach (float z in new[] { -1.35f, 1.35f })
            {
                Part(root, "Wheel", new Vector3(x, 0.33f, z), new Vector3(0.66f, 0.12f, 0.66f), PropLibrary.Rubber, material, false,
                    Quaternion.Euler(0f, 0f, 90f), PrimitiveType.Cylinder);
                Part(root, "Hubcap", new Vector3(x + Mathf.Sign(x) * 0.13f, 0.33f, z), new Vector3(0.3f, 0.02f, 0.3f), Chrome, material, false,
                    Quaternion.Euler(0f, 0f, 90f), PrimitiveType.Cylinder);
            }

            // Cargo box: two side walls, a bulkhead behind the seats and the roof. The back is open.
            float wallHeight = RoofTop - FloorTop;
            float cargoLength = CargoFront - Rear;
            foreach (float side in new[] { -1f, 1f })
            {
                Part(root, "Side Wall", new Vector3(side * (Width - Wall) * 0.5f, FloorTop + wallHeight * 0.5f, (Rear + CargoFront) * 0.5f),
                    new Vector3(Wall, wallHeight, cargoLength), Paint, material, true);
                Part(root, "Stripe", new Vector3(side * (Width * 0.5f + 0.002f), FloorTop + 0.35f, (Rear + CargoFront) * 0.5f + 0.4f),
                    new Vector3(0.01f, 0.16f, Length - 0.9f), Cream, material, false);
            }
            Part(root, "Bulkhead", new Vector3(0f, FloorTop + wallHeight * 0.5f, CargoFront + Wall * 0.5f),
                new Vector3(Width, wallHeight, Wall), Paint, material, true);
            Part(root, "Roof", new Vector3(0f, RoofTop + 0.05f, (Rear + 1.45f) * 0.5f), new Vector3(Width + 0.04f, 0.1f, 1.45f - Rear),
                Cream, material, true);

            // Cab: seats behind a sloped windscreen, then a short bonnet.
            Part(root, "Cab", new Vector3(0f, FloorTop + 0.45f, 1.0f), new Vector3(Width, 0.9f, 0.9f), Paint, material, true);
            Part(root, "Windscreen", new Vector3(0f, 1.6f, 1.38f), new Vector3(Width - 0.14f, 0.85f, 0.06f), Glass, material, true,
                Quaternion.Euler(-28f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                Part(root, "Cab Window", new Vector3(side * (Width * 0.5f + 0.003f), 1.55f, 0.98f), new Vector3(0.01f, 0.55f, 0.7f),
                    Glass, material, false);
                Part(root, "Cab Side", new Vector3(side * (Width - Wall) * 0.5f, 1.55f, 0.98f), new Vector3(Wall, 0.9f, 0.86f),
                    Paint, material, true);
            }
            Part(root, "Bonnet", new Vector3(0f, 0.85f, 1.78f), new Vector3(Width, 0.6f, 0.74f), Paint, material, true);
            Part(root, "Grille", new Vector3(0f, 0.8f, Length * 0.5f + 0.005f), new Vector3(0.9f, 0.25f, 0.02f), Chrome, material, false);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(root, "Headlight", new Vector3(side * 0.7f, 0.85f, Length * 0.5f + 0.01f), new Vector3(0.26f, 0.18f, 0.02f),
                    HeadLight, material, false);
                Part(root, "Tail Light", new Vector3(side * (Width * 0.5f - 0.06f), 1.0f, Rear - 0.01f), new Vector3(0.1f, 0.3f, 0.02f),
                    TailLight, material, false);
            }
            Part(root, "Front Bumper", new Vector3(0f, 0.35f, Length * 0.5f + 0.06f), new Vector3(Width + 0.06f, 0.14f, 0.12f), Chrome, material, true);
            Part(root, "Rear Bumper", new Vector3(0f, 0.35f, Rear - 0.06f), new Vector3(Width + 0.06f, 0.14f, 0.12f), Chrome, material, true);

            // Back doors swung wide open on their hinges.
            float doorWidth = Width * 0.5f - 0.02f;
            foreach (float side in new[] { -1f, 1f })
            {
                var hinge = new GameObject("Door Hinge").transform;
                hinge.SetParent(root, false);
                hinge.localPosition = new Vector3(side * Width * 0.5f, 0f, Rear);
                hinge.localRotation = Quaternion.Euler(0f, side * -115f, 0f);
                Part(hinge, "Back Door", new Vector3(-side * doorWidth * 0.5f, FloorTop + wallHeight * 0.5f - 0.02f, -0.03f),
                    new Vector3(doorWidth, wallHeight - 0.08f, 0.05f), Paint, material, true);
                Part(hinge, "Back Window", new Vector3(-side * doorWidth * 0.5f, 1.55f, -0.06f), new Vector3(doorWidth - 0.2f, 0.4f, 0.01f),
                    Glass, material, false);
            }

            // The sale trigger: the cargo space, set in a little from the opening so edge clips do not sell.
            var zoneGo = new GameObject("Sell Zone");
            zoneGo.layer = 2;   // Ignore Raycast: aiming and grabbing look straight through it
            zoneGo.transform.SetParent(root, false);
            var box = zoneGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, FloorTop + (wallHeight - 0.1f) * 0.5f, (Rear + 0.15f + CargoFront) * 0.5f);
            box.size = new Vector3(Width - Wall * 2f - 0.04f, wallHeight - 0.1f, CargoFront - Rear - 0.15f);
            van.zone = zoneGo.AddComponent<SellZone>();

            // "Sold today" on both sides, and a sign over the open back.
            van.counters = new[]
            {
                Text(root, "Counter L", new Vector3(-Width * 0.5f - 0.01f, 1.35f, -0.75f), Quaternion.LookRotation(Vector3.right), 0.05f),
                Text(root, "Counter R", new Vector3(Width * 0.5f + 0.01f, 1.35f, -0.75f), Quaternion.LookRotation(Vector3.left), 0.05f),
            };
            var sign = Text(root, "Sign", new Vector3(0f, RoofTop + 0.45f, Rear + 0.1f), Quaternion.LookRotation(Vector3.forward), 0.06f);
            sign.text = "THROW LOOT IN";
            sign.color = Gold;

            van.audioSource = root.gameObject.AddComponent<AudioSource>();
            van.audioSource.playOnAwake = false;
            van.audioSource.spatialBlend = 1f;
            van.audioSource.rolloffMode = AudioRolloffMode.Linear;
            van.audioSource.minDistance = 3f;
            van.audioSource.maxDistance = 35f;
            van.RefreshCounter(true);
            return van;
        }

        static GameObject Part(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Func<Color, Material> material,
            bool solid, Quaternion? rotation = null, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            if (!solid) Remove(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material(color);
            return go;
        }

        static void Remove(Object thing)
        {
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }

        static TextMesh Text(Transform parent, string name, Vector3 position, Quaternion rotation, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 48;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Cream;
            return text;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => textMaterial = null;

        void Awake()
        {
            // The signs sit on the body: draw them with depth, not through the van like the font's own material.
            foreach (var text in GetComponentsInChildren<TextMesh>())
                text.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial(text.font);
        }

        static Material TextMaterial(Font font)
        {
            if (textMaterial != null) return textMaterial;
            var shader = Shader.Find("Roadkill/World Text");
            if (shader == null) return font.material;
            textMaterial = new Material(shader) { mainTexture = font.material.mainTexture };
            return textMaterial;
        }

        void OnEnable() => SellLedger.Sold += OnSold;
        void OnDisable() => SellLedger.Sold -= OnSold;

        void Update() => RefreshCounter(false);

        void RefreshCounter(bool force)
        {
            int total = SellLedger.SoldToday;
            if (!force && total == shownTotal) return;
            shownTotal = total;
            string text = $"CAPY CASH\nSOLD TODAY: ${total}";
            foreach (var counter in counters)
                if (counter != null) counter.text = text;
        }

        void OnSold(SellLedger.Sale sale)
        {
            if ((sale.Point - transform.position).sqrMagnitude > feedbackRange * feedbackRange) return;
            Vector3 cargo = zone != null ? zone.transform.TransformPoint(zone.GetComponent<BoxCollider>().center) : transform.position;

            if (audioSource != null) audioSource.PlayOneShot(chaChing != null ? chaChing : chaChing = MakeChaChing(), volume);
            PunchFx.Sparks(cargo, Vector3.up, CashGreen, 1f);
            PunchFx.Sparks(cargo, Vector3.up, Gold, 0.6f);

            Vector3 above = transform.position + Vector3.up * (RoofTop + 1.5f);
            PunchFx.Popup(above, $"+${sale.Value}", CashGreen, 1.4f);
            string who = sale.Seller != Sellable.NoOwner ? $" ({PlayerNet.NameOf(sale.Seller)})" : "";
            PunchFx.Popup(above + Vector3.down * 0.45f, sale.ItemName + who, Cream, 0.7f);
        }

        /// <summary>A placeholder till sound: the drawer's rattle ("cha") then a bright double bell ("ching").</summary>
        static AudioClip MakeChaChing()
        {
            const int rate = 44100;
            const float seconds = 0.9f;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var random = new System.Random(7);
            float noise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // "Cha": two short, filtered noise rattles.
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                noise += (white - noise) * 0.35f;
                float rattle = noise * (Burst(t, 0f, 0.05f) + 0.8f * Burst(t, 0.07f, 0.05f)) * 0.9f;
                // "Ching": a bell at C7 with a bright partial, then a second, higher strike.
                float bell = 0f;
                if (t > 0.13f)
                {
                    float b = t - 0.13f;
                    bell += (Mathf.Sin(2f * Mathf.PI * 2093f * b) + 0.5f * Mathf.Sin(2f * Mathf.PI * 5274f * b)) * Mathf.Exp(-b * 7f) * 0.35f;
                }
                if (t > 0.21f)
                {
                    float b = t - 0.21f;
                    bell += (Mathf.Sin(2f * Mathf.PI * 2637f * b) + 0.4f * Mathf.Sin(2f * Mathf.PI * 6645f * b)) * Mathf.Exp(-b * 5f) * 0.35f;
                }
                data[i] = Mathf.Clamp(rattle + bell, -1f, 1f);
            }
            var clip = AudioClip.Create("ChaChing", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Burst(float t, float start, float length)
        {
            float x = (t - start) / length;
            return x < 0f || x > 1f ? 0f : Mathf.Sin(x * Mathf.PI) * Mathf.Exp(-x * 3f);
        }
    }
}
