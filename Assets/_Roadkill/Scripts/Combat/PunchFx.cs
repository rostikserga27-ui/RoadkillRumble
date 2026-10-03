using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Punch feedback, all placeholders until real art and sound arrive: a burst of cartoon sparks,
    /// popups ("BONK!", "OOPS!") drawn over the world, and sounds (PunchConfig clips if assigned,
    /// otherwise little generated ones: a thud, a boing and a harmless boop; no swing sound without a clip).
    /// Purely local: callers decide on which peers to play them.
    /// </summary>
    public class PunchFx : MonoBehaviour
    {
        static PunchFx instance;
        static AudioClip thud, boing, boop;

        static readonly string[] HitWords = { "BONK!", "POW!", "WHAM!", "SMACK!", "THWACK!" };
        static readonly string[] FriendlyWords = { "OOPS!", "SORRY!", "MY BAD!", "FRIEND!?", "WHOOPS!" };

        struct PopupEntry
        {
            public Vector3 Position;
            public string Text;
            public Color Color;
            public float Size;
            public float Born;
        }

        readonly List<PopupEntry> popups = new List<PopupEntry>();
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        static PunchFx Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("PunchFx");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<PunchFx>();
                }
                return instance;
            }
        }

        // ---- sparks ------------------------------------------------------------------------------------

        /// <summary>A puff of little cubes flying off the contact, bigger and more for harder hits.</summary>
        public static void Sparks(Vector3 point, Vector3 normal, Color color, float power)
        {
            int count = 4 + Mathf.RoundToInt(6f * power);
            var material = RkMaterials.Get(color);
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "PunchSpark";
                go.transform.position = point;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.04f, 0.08f, power);
                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 direction = (normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up) + Random.insideUnitSphere * 0.9f;
                go.AddComponent<Spark>().velocity = direction.normalized * Random.Range(2f, 4f + 4f * power);
            }
        }

        class Spark : MonoBehaviour
        {
            public Vector3 velocity;
            float age;
            Vector3 scale;

            void Start() => scale = transform.localScale;

            void Update()
            {
                age += Time.deltaTime;
                velocity += Physics.gravity * (0.5f * Time.deltaTime);
                transform.position += velocity * Time.deltaTime;
                transform.localScale = scale * Mathf.Clamp01(1f - age / 0.3f);
                if (age > 0.3f) Destroy(gameObject);
            }
        }

        // ---- popups ------------------------------------------------------------------------------------

        public static void Popup(Vector3 position, string text, Color color, float size = 1f)
        {
            Instance.popups.Add(new PopupEntry { Position = position, Text = text, Color = color, Size = size, Born = Time.time });
        }

        public static string HitWord() => HitWords[Random.Range(0, HitWords.Length)];
        public static string FriendlyWord() => FriendlyWords[Random.Range(0, FriendlyWords.Length)];

        void OnGUI()
        {
            if (popups.Count == 0) return;
            var cam = Camera.main;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                float age = Time.time - p.Born;
                if (age > 1.1f || cam == null)
                {
                    popups.RemoveAt(i);
                    continue;
                }
                Vector3 screen = cam.WorldToScreenPoint(p.Position + Vector3.up * (0.6f * age));
                if (screen.z <= 0f) continue;
                // Pop in big, settle, fade.
                float pop = 1f + 0.6f * Mathf.Exp(-age * 14f);
                style.fontSize = Mathf.RoundToInt(26f * p.Size * pop);
                var previous = GUI.color;
                var rect = new Rect(screen.x - 150f, Screen.height - screen.y - 30f, 300f, 60f);
                GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(1.1f - age) * 0.8f);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), p.Text, style);
                GUI.color = new Color(p.Color.r, p.Color.g, p.Color.b, Mathf.Clamp01(1.1f - age));
                GUI.Label(rect, p.Text, style);
                GUI.color = previous;
            }
        }

        // ---- sound -------------------------------------------------------------------------------------

        /// <summary>Hook: a swing through the air. Silent unless PunchConfig has a whoosh clip (the generated one was not nice).</summary>
        public static void PlayWhoosh(Vector3 position, float charge)
        {
            var c = PunchConfig.Current;
            if (c.whooshClip == null) return;
            Play(c.whooshClip, position, c.volume * Mathf.Lerp(0.15f, 0.35f, charge), Mathf.Lerp(1.15f, 0.9f, charge));
        }

        /// <summary>Hook: a meaty impact, louder and lower the harder the hit; very hard hits add a comedic sound.</summary>
        public static void PlayImpact(Vector3 position, float power, bool comedic)
        {
            var c = PunchConfig.Current;
            Play(c.impactClip != null ? c.impactClip : Clip(ref thud, MakeThud), position, c.volume * Mathf.Lerp(0.4f, 1f, power), Mathf.Lerp(1.25f, 0.8f, power));
            if (comedic) Play(c.comedicClip != null ? c.comedicClip : Clip(ref boing, MakeBoing), position, c.volume, Random.Range(0.9f, 1.15f));
        }

        /// <summary>Hook: a punch that does nothing (friendly fire off, spawn protection).</summary>
        public static void PlayHarmless(Vector3 position)
        {
            var c = PunchConfig.Current;
            Play(c.harmlessClip != null ? c.harmlessClip : Clip(ref boop, MakeBoop), position, c.volume * 0.6f, Random.Range(0.95f, 1.1f));
        }

        /// <summary>The generated placeholder, made once (Unity's null check, in case it was unloaded).</summary>
        static AudioClip Clip(ref AudioClip cache, System.Func<AudioClip> make)
        {
            if (cache == null) cache = make();
            return cache;
        }

        static void Play(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            var go = new GameObject("PunchSound");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 30f;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        const int Rate = 44100;

        static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeThud()
        {
            // A meaty punch: a low thump that drops in pitch, a short knuckle knock on top and a 4 ms click
            // for the contact, lightly saturated. No long noise tail.
            var random = new System.Random(11);
            float phase = 0f;
            return Make("PunchThud", 0.2f, t =>
            {
                float pitch = 55f + 110f * Mathf.Exp(-t * 30f);
                phase += 2f * Mathf.PI * pitch / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 22f);
                float knock = Mathf.Sin(2f * Mathf.PI * 320f * t) * Mathf.Exp(-t * 90f) * 0.5f;
                float click = t < 0.004f ? ((float)random.NextDouble() * 2f - 1f) * (1f - t / 0.004f) * 0.5f : 0f;
                return (float)System.Math.Tanh((thump + knock + click) * 1.8f) * 0.8f;
            });
        }

        static AudioClip MakeBoing()
        {
            return Make("PunchBoing", 0.5f, t =>
            {
                float pitch = 220f + 260f * Mathf.Exp(-t * 6f) + 25f * Mathf.Sin(t * 45f);
                return Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 5f) * 0.6f;
            });
        }

        static AudioClip MakeBoop()
        {
            return Make("PunchBoop", 0.12f, t => Mathf.Sin(2f * Mathf.PI * 520f * t) * Mathf.Exp(-t * 30f) * 0.5f);
        }
    }

    /// <summary>
    /// The local player's camera shake and kick (attacker and victim), applied on top of whatever placed
    /// the camera this frame (PlayerMotor's look, PossumCamera), and taken off again before the next.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class PunchCameraFx : MonoBehaviour
    {
        public Transform view;
        float shake, shakeTime, shakeDuration;
        Vector3 kickAxis;
        float kick;            // degrees, decays
        Vector3 appliedPosition;
        Quaternion appliedRotation = Quaternion.identity;
        float seed;

        void Awake() => seed = Random.value * 100f;

        /// <summary>Shake by `amplitude` metres for `seconds`, and kick the view `degrees` about `axis` (view space).</summary>
        public void Hit(float amplitude, float seconds, float degrees, Vector3 axis)
        {
            if (amplitude >= shake * Mathf.Clamp01(1f - shakeTime / Mathf.Max(0.01f, shakeDuration)))
            {
                shake = amplitude;
                shakeTime = 0f;
                shakeDuration = seconds;
            }
            if (Mathf.Abs(degrees) >= Mathf.Abs(kick))
            {
                kick = degrees;
                kickAxis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.right;
            }
        }

        void Update()
        {
            // Take last frame's offset off before anyone moves the camera again.
            if (view == null) return;
            view.localPosition -= appliedPosition;
            view.localRotation *= Quaternion.Inverse(appliedRotation);
            appliedPosition = Vector3.zero;
            appliedRotation = Quaternion.identity;
        }

        void LateUpdate()
        {
            if (view == null) return;
            // Undo anything still applied (another script may not have reset the camera this frame).
            view.localPosition -= appliedPosition;
            view.localRotation *= Quaternion.Inverse(appliedRotation);

            shakeTime += Time.deltaTime;
            float fade = shakeDuration > 0f ? Mathf.Clamp01(1f - shakeTime / shakeDuration) : 0f;
            float t = Time.time * 35f;
            appliedPosition = new Vector3(Mathf.PerlinNoise(seed, t) - 0.5f, Mathf.PerlinNoise(t, seed) - 0.5f, 0f) * (2f * shake * fade * fade);
            kick = Mathf.MoveTowards(kick, 0f, Mathf.Max(Mathf.Abs(kick), 1f) * 10f * Time.deltaTime);
            appliedRotation = Quaternion.AngleAxis(kick, kickAxis);
            view.localPosition += appliedPosition;
            view.localRotation *= appliedRotation;
        }
    }
}
