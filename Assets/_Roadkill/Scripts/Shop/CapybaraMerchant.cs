using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The shopkeeper: a capybara in a straw hat, built from primitives like the rest of the greybox. It
    /// breathes and blinks, turns its head to the nearest player, says hello when someone walks up and
    /// thanks them when they buy something from its shop. Local scenery: every peer builds its own and it
    /// reacts to what this peer sees (its own purchases).
    /// </summary>
    public class CapybaraMerchant : MonoBehaviour
    {
        static readonly Color Fur = new Color(0.62f, 0.45f, 0.3f);
        static readonly Color FurDark = new Color(0.46f, 0.32f, 0.21f);
        static readonly Color Nose = new Color(0.18f, 0.13f, 0.1f);
        static readonly Color Straw = new Color(0.93f, 0.82f, 0.47f);
        static readonly Color Band = new Color(0.75f, 0.2f, 0.18f);

        static readonly string[] Hellos = { "Hey, pal.", "Take a look.", "Fresh stock!", "No rush.", "Mm. Customer." };
        static readonly string[] Thanks = { "Pleasure.", "Good choice.", "Come back soon!", "Thanks, pal.", "Mm. Nice." };
        static readonly string[] Buys = { "Ooh. {0}.", "I'll take it.", "{0}? Sure.", "Mm. Into the van.", "Nice {0}." };

        [Tooltip("Says hello to players closer than this (metres), turns its head to them within lookRange.")]
        public float greetRange = 4f;
        public float lookRange = 9f;
        public float maxHeadTurn = 65f;

        Transform body, head;
        Transform[] eyes;
        TextMesh speech;
        Transform shop;
        Vector3 bodyScale;
        float nextBlink, blinkUntil, speechUntil, headYaw;
        bool someoneClose;

        /// <summary>Build a capybara at a local position under `parent`, facing its local +Z, serving `shopRoot`.</summary>
        public static CapybaraMerchant Build(Transform parent, Vector3 localPosition, float yaw, Transform shopRoot)
        {
            var root = new GameObject("Capybara Merchant").transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var merchant = root.gameObject.AddComponent<CapybaraMerchant>();
            merchant.shop = shopRoot;

            // Sitting on its haunches, chunky and calm.
            merchant.body = new GameObject("Body").transform;
            merchant.body.SetParent(root, false);
            Part(PrimitiveType.Sphere, "Haunch", merchant.body, new Vector3(0f, 0.28f, -0.1f), new Vector3(0.78f, 0.52f, 0.8f), Fur);
            Part(PrimitiveType.Sphere, "Belly", merchant.body, new Vector3(0f, 0.5f, 0.02f), new Vector3(0.66f, 0.78f, 0.7f), Fur, Quaternion.Euler(-18f, 0f, 0f));
            Part(PrimitiveType.Capsule, "Leg L", merchant.body, new Vector3(-0.17f, 0.17f, 0.27f), new Vector3(0.13f, 0.17f, 0.13f), FurDark);
            Part(PrimitiveType.Capsule, "Leg R", merchant.body, new Vector3(0.17f, 0.17f, 0.27f), new Vector3(0.13f, 0.17f, 0.13f), FurDark);
            Part(PrimitiveType.Sphere, "Foot L", merchant.body, new Vector3(-0.17f, 0.03f, 0.33f), new Vector3(0.16f, 0.06f, 0.2f), FurDark);
            Part(PrimitiveType.Sphere, "Foot R", merchant.body, new Vector3(0.17f, 0.03f, 0.33f), new Vector3(0.16f, 0.06f, 0.2f), FurDark);

            // The big blocky head, on a pivot at the neck so it can look around.
            merchant.head = new GameObject("Head").transform;
            merchant.head.SetParent(merchant.body, false);
            merchant.head.localPosition = new Vector3(0f, 0.86f, 0.08f);
            Part(PrimitiveType.Cube, "Skull", merchant.head, new Vector3(0f, 0.08f, 0.14f), new Vector3(0.36f, 0.32f, 0.46f), Fur);
            Part(PrimitiveType.Cube, "Snout", merchant.head, new Vector3(0f, 0.03f, 0.41f), new Vector3(0.32f, 0.26f, 0.16f), FurDark);
            Part(PrimitiveType.Cube, "Nose", merchant.head, new Vector3(0f, 0.1f, 0.495f), new Vector3(0.18f, 0.07f, 0.03f), Nose);
            Part(PrimitiveType.Cube, "Mouth", merchant.head, new Vector3(0f, -0.04f, 0.495f), new Vector3(0.1f, 0.02f, 0.02f), Nose);
            merchant.eyes = new[]
            {
                Part(PrimitiveType.Sphere, "Eye L", merchant.head, new Vector3(-0.17f, 0.15f, 0.27f), Vector3.one * 0.065f, Nose).transform,
                Part(PrimitiveType.Sphere, "Eye R", merchant.head, new Vector3(0.17f, 0.15f, 0.27f), Vector3.one * 0.065f, Nose).transform
            };
            Part(PrimitiveType.Sphere, "Ear L", merchant.head, new Vector3(-0.15f, 0.25f, -0.02f), new Vector3(0.1f, 0.1f, 0.05f), FurDark);
            Part(PrimitiveType.Sphere, "Ear R", merchant.head, new Vector3(0.15f, 0.25f, -0.02f), new Vector3(0.1f, 0.1f, 0.05f), FurDark);
            Part(PrimitiveType.Cylinder, "Hat Brim", merchant.head, new Vector3(0f, 0.255f, 0.1f), new Vector3(0.62f, 0.012f, 0.62f), Straw);
            Part(PrimitiveType.Cylinder, "Hat Crown", merchant.head, new Vector3(0f, 0.32f, 0.1f), new Vector3(0.32f, 0.06f, 0.32f), Straw);
            Part(PrimitiveType.Cylinder, "Hat Band", merchant.head, new Vector3(0f, 0.29f, 0.1f), new Vector3(0.33f, 0.02f, 0.33f), Band);

            // Players bump into it rather than walk through it.
            var collider = root.gameObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.6f, 0.05f);
            collider.radius = 0.38f;
            collider.height = 1.25f;

            merchant.speech = SpeechBubble(root);
            merchant.bodyScale = merchant.body.localScale;
            return merchant;
        }

        static GameObject Part(PrimitiveType shape, string name, Transform parent, Vector3 position, Vector3 scale, Color color, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(color);
            return go;
        }

        static TextMesh SpeechBubble(Transform root)
        {
            var go = new GameObject("Speech");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 1.75f, 0.1f);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 48;
            text.characterSize = 0.035f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(1f, 0.95f, 0.75f);
            text.text = "";
            go.AddComponent<FaceCamera>();
            return text;
        }

        void OnEnable()
        {
            ShopInteractor.Bought += OnBought;
            SellLedger.Sold += OnSold;
        }

        void OnDisable()
        {
            ShopInteractor.Bought -= OnBought;
            SellLedger.Sold -= OnSold;
        }

        /// <summary>Someone sold something in a van close by.</summary>
        void OnSold(SellLedger.Sale sale)
        {
            if ((sale.Point - transform.position).sqrMagnitude < 9f * 9f)
                Say(string.Format(Buys[Random.Range(0, Buys.Length)], sale.ItemName));
        }

        void OnBought(ShopItem item)
        {
            if (shop != null && item != null && item.transform.IsChildOf(shop)) Say(Thanks[Random.Range(0, Thanks.Length)]);
        }

        void Say(string line)
        {
            speech.text = line;
            speechUntil = Time.time + 2.5f;
        }

        void Update()
        {
            float t = Time.time;
            // Breathing.
            body.localScale = new Vector3(bodyScale.x, bodyScale.y * (1f + Mathf.Sin(t * 1.6f) * 0.015f), bodyScale.z);

            // Blinking.
            if (t > nextBlink)
            {
                blinkUntil = t + 0.12f;
                nextBlink = t + Random.Range(2.5f, 6f);
            }
            float eyeHeight = t < blinkUntil ? 0.012f : 0.065f;
            foreach (var eye in eyes) eye.localScale = new Vector3(0.065f, eyeHeight, 0.065f);

            // Watch the nearest player; greet anyone who walks up.
            Vector3? nearest = null;
            float best = lookRange;
            foreach (var player in HandsController.Players)
            {
                float d = Vector3.Distance(player.transform.position, transform.position);
                if (d < best) { best = d; nearest = player.transform.position; }
            }
            float wantYaw = 0f;
            if (nearest.HasValue)
            {
                Vector3 local = transform.InverseTransformPoint(nearest.Value);
                wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxHeadTurn, maxHeadTurn);
            }
            headYaw = Mathf.MoveTowardsAngle(headYaw, wantYaw, 120f * Time.deltaTime);
            head.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 3f, headYaw, Mathf.Sin(t * 0.5f) * 2f);

            bool close = nearest.HasValue && best < greetRange;
            if (close && !someoneClose) Say(Hellos[Random.Range(0, Hellos.Length)]);
            someoneClose = close;

            if (t > speechUntil && speech.text.Length > 0) speech.text = "";
        }
    }
}
