using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Prototype HUD drawn with IMGUI: crosshair, fuel-gauge health, tachometer stamina, what each hand
    /// holds, the grab hint under the crosshair and controls help. The dashboard HUD from GDD section 12
    /// replaces this once the look is in.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        public bool showHelp = true;

        const string Help =
            "WASD move   Shift sprint   Ctrl crouch   Space jump\n" +
            "LMB / RMB hold: grab with left / right hand\n" +
            "G hold, then release: throw what you hold\n" +
            "F tap: jab   F hold: haymaker (fists take turns)\n" +
            "C hold: play possum   Space while down: get up faster\n" +
            "F2 open mic / push-to-talk (V)   F3 hear yourself\n" +
            "F9 playground rules   R respawn   F1 hide help   Esc free the mouse";

        PlayerMotor motor;
        PlayerHealth health;
        HandsController hands;
        PlayerFists fists;
        VoiceChat voice;
        Camera viewCamera;
        GUIStyle label;
        GUIStyle banner;
        string aimHint = "";

        void Start()
        {
            motor = GetComponent<PlayerMotor>();
            health = GetComponent<PlayerHealth>();
            hands = GetComponent<HandsController>();
            fists = GetComponent<PlayerFists>();
            voice = GetComponent<VoiceChat>();
            viewCamera = hands != null ? hands.viewCamera : Camera.main;
        }

        void Update()
        {
            if (RkInput.HelpPressed) showHelp = !showHelp;
            aimHint = BuildAimHint();
        }

        string BuildAimHint()
        {
            if (viewCamera == null || hands == null || motor.IsRagdolled) return "";
            if (hands.IsHolding(hands.Left) && hands.IsHolding(hands.Right)) return "";
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, hands.reach, ~0, QueryTriggerInteraction.Ignore)) return "";
            var prop = hit.collider.GetComponentInParent<PhysicsProp>();
            var body = prop != null ? prop.GetComponent<Rigidbody>() : null;
            return body != null ? $"{prop.displayName}  {body.mass:0} kg" : "";
        }

        void OnGUI()
        {
            if (motor == null || health == null || hands == null || hands.Left == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                label.normal.textColor = Color.white;
                banner = new GUIStyle(label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            float w = Screen.width, h = Screen.height;
            Fill(new Rect(w * 0.5f - 2f, h * 0.5f - 2f, 4f, 4f), Color.white);
            if (aimHint.Length > 0)
                GUI.Label(new Rect(w * 0.5f + 14f, h * 0.5f - 10f, 300f, 24f), aimHint, label);

            Bar(new Rect(20f, h - 64f, 240f, 20f), health.Health / health.maxHealth, new Color(1f, 0.55f, 0.1f),
                $"FUEL {Mathf.CeilToInt(health.Health)}");
            Bar(new Rect(20f, h - 38f, 240f, 14f), motor.Stamina / motor.sprintStaminaSeconds, new Color(0.3f, 0.8f, 1f), "RPM");
            if (fists != null)
            {
                Bar(new Rect(20f, h - 88f, 240f, 14f), fists.Stamina01, fists.Tired ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.85f, 0.3f),
                    fists.Tired ? "FISTS - too tired" : "FISTS");
                if (fists.Charge > 0f)
                    Bar(new Rect(w * 0.5f - 80f, h * 0.5f + 42f, 160f, 8f), fists.Charge, new Color(1f, 0.5f, 0.2f), "");
            }

            GUI.Label(new Rect(w - 380f, h - 62f, 360f, 24f), HandText("L", hands, hands.Left), label);
            GUI.Label(new Rect(w - 380f, h - 38f, 360f, 24f), HandText("R", hands, hands.Right), label);

            if (voice != null)
            {
                string mode = voice.PushToTalk ? "push-to-talk (V)" : "open mic";
                string loop = voice.Loopback ? ", hearing yourself" : "";
                GUI.Label(new Rect(280f, h - 62f, 420f, 24f), $"{voice.Status}, {mode}{loop}", label);
                Bar(new Rect(280f, h - 36f, 160f, 10f), voice.InputLevel * 12f,
                    voice.Transmitting ? new Color(0.3f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f), "");
            }
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening)
            {
                string role = network.IsHost ? "Host" : "Client";
                int players = HandsController.PlayerCount;
                GUI.Label(new Rect(w - 380f, 20f, 360f, 24f), $"{role} #{network.LocalClientId} · players: {players}", label);
            }
            if (hands.HeaviestHeldMass > 0f)
                GUI.Label(new Rect(w - 380f, h - 86f, 360f, 24f),
                    $"Speed x{hands.SpeedMultiplier:0.00}{(hands.CanSprint ? "" : ", no sprint")}", label);

            if (hands.ThrowCharge > 0f)
                Bar(new Rect(w * 0.5f - 80f, h * 0.5f + 28f, 160f, 10f), hands.ThrowCharge, Color.yellow, "");

            Rect center = new Rect(0f, h * 0.3f, w, 40f);
            if (health.IsDown) GUI.Label(center, "KNOCKED OUT", banner);
            else if (motor.IsPossum) GUI.Label(center, "PLAYING POSSUM", banner);
            else if (motor.IsRagdolled) GUI.Label(center, "OOF!  (mash Space)", banner);

            if (Time.time - health.LastDamageTime < 2f)
            {
                var previous = GUI.color;
                GUI.color = new Color(1f, 0.35f, 0.3f);
                GUI.Label(new Rect(0f, h * 0.3f + 40f, w, 40f), health.LastDamageText, banner);
                GUI.color = previous;
            }

            if (showHelp) GUI.Label(new Rect(20f, 20f, 600f, 140f), Help, label);
            string rules = PlaygroundRules.Summary();
            if (!FriendlyFire.Enabled) rules = rules.Length > 0 ? rules + ", friendly fire off" : "Friendly fire off";
            if (rules.Length > 0)
                GUI.Label(new Rect(w - 380f, 44f, 360f, 48f), $"Rules: {rules}", label);
            if (Cursor.lockState != CursorLockMode.Locked && !PlaygroundRules.PanelOpen)
                GUI.Label(new Rect(0f, h * 0.5f + 40f, w, 40f), "Click to play", banner);
        }

        static string HandText(string side, HandsController controller, HandsController.Hand hand)
        {
            var held = controller.HeldBody(hand);
            if (held == null) return $"{side}: empty";
            var prop = held.GetComponent<PhysicsProp>();
            string name = prop != null ? prop.displayName : held.name;
            int count = HandsController.HolderCount(held);
            return $"{side}: {name}, {held.mass:0} kg ({count} hand{(count == 1 ? "" : "s")} on it)";
        }

        static void Bar(Rect rect, float fraction, Color color, string text)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
            if (text.Length > 0) GUI.Label(new Rect(rect.x + 6f, rect.y - 2f, rect.width, rect.height + 4f), text);
        }

        static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
