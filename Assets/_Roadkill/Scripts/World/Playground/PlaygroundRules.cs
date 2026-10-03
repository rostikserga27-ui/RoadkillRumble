using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Sandbox switches for playing around with friends (F9): moon gravity, super jump, jelly legs and so
    /// on, plus a few buttons (reset the props, knock everyone over, gather everyone to you). Anyone can
    /// flip them; the server keeps the switches in one NetworkVariable and every peer applies them
    /// (gameplay code reads the static helpers below), so all players play by the same rules.
    /// Spawned by NetSession on the server; without it every helper returns the normal value.
    /// </summary>
    public class PlaygroundRules : NetworkBehaviour
    {
        public const string PrefabName = "PlaygroundRules";

        public enum Rule { MoonGravity, SuperJump, Turbo, StrongArms, GlassJaw, JellyLegs, IceWorld, NoDamage }
        public enum Action { ResetProps, FlopEveryone, GatherHere }

        static readonly string[] Titles =
            { "Moon gravity", "Super jump", "Turbo legs", "Strong arms", "Glass jaw", "Jelly legs", "Ice world", "No damage" };
        static readonly string[] Hints =
        {
            "gravity x0.35", "jump x3", "run x1.6", "carry x4, throw x1.8", "any hit or 1.6 m fall knocks you down",
            "wobbly bodies", "the whole world is slippery", "nobody gets hurt"
        };

        public static PlaygroundRules Instance { get; private set; }
        public static bool PanelOpen => Instance != null && Instance.open;
        public static int Flags { get; private set; }
        public static bool Has(Rule rule) => (Flags & (1 << (int)rule)) != 0;

        // What gameplay code multiplies by (1 when the rule is off or there is no rules object).
        public static float JumpScale => Has(Rule.SuperJump) ? 3f : 1f;
        public static float SpeedScale => Has(Rule.Turbo) ? 1.6f : 1f;
        public static float CarryScale => Has(Rule.StrongArms) ? 4f : 1f;
        public static float ThrowScale => Has(Rule.StrongArms) ? 1.8f : 1f;
        public static float ThrowMassScale => Has(Rule.StrongArms) ? 8f : 1f;
        public static float FallHeightScale => Has(Rule.GlassJaw) ? 0.4f : 1f;
        public static float BalanceScale => Has(Rule.JellyLegs) ? 0.35f : 1f;
        public static float JointScale => Has(Rule.JellyLegs) ? 0.45f : 1f;
        public static float WobbleScale => Has(Rule.JellyLegs) ? 3f : 1f;
        public static float MaxGrip => Has(Rule.IceWorld) ? 0.1f : 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Flags = 0;
            gravitySaved = false;
        }

        static Vector3 normalGravity;
        static bool gravitySaved;

        // Written by the server: one bit per Rule.
        NetworkVariable<int> flags = new NetworkVariable<int>(0);
        readonly List<(string text, float time)> feed = new List<(string, float)>();
        bool open;
        GUIStyle feedStyle;
        GUIStyle titleStyle;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!gravitySaved)
            {
                normalGravity = Physics.gravity;
                gravitySaved = true;
            }
            flags.OnValueChanged += OnFlagsChanged;
            Apply(flags.Value);
        }

        public override void OnNetworkDespawn()
        {
            flags.OnValueChanged -= OnFlagsChanged;
            if (Instance == this) Instance = null;
            Apply(0);
            if (open) SetOpen(false);
        }

        void OnFlagsChanged(int previous, int current) => Apply(current);

        static void Apply(int value)
        {
            Flags = value;
            if (gravitySaved) Physics.gravity = normalGravity * (Has(Rule.MoonGravity) ? 0.35f : 1f);
        }

        /// <summary>One-line list of the rules in play, for the HUD.</summary>
        public static string Summary()
        {
            if (Flags == 0) return "";
            var on = new List<string>();
            for (int i = 0; i < Titles.Length; i++)
                if ((Flags & (1 << i)) != 0) on.Add(Titles[i]);
            return string.Join(", ", on);
        }

        // ---- requests (anyone) -----------------------------------------------------------------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ToggleRpc(int rule, RpcParams rpcParams = default)
        {
            if (rule < 0 || rule >= Titles.Length) return;
            flags.Value ^= 1 << rule;
            bool on = (flags.Value & (1 << rule)) != 0;
            AnnounceRpc($"{PlayerNet.NameOf(rpcParams.Receive.SenderClientId)} turned {(on ? "on" : "off")} {Titles[rule]}");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ActionRpc(int action, RpcParams rpcParams = default) => ServerAction((Action)action, rpcParams.Receive.SenderClientId);

        [Rpc(SendTo.Everyone)]
        void AnnounceRpc(string text)
        {
            feed.Add((text, Time.time));
            if (feed.Count > 5) feed.RemoveAt(0);
        }

        // ---- server ----------------------------------------------------------------------------

        public void ServerAction(Action action, ulong by)
        {
            if (!IsServer) return;
            string who = PlayerNet.NameOf(by);
            switch (action)
            {
                case Action.ResetProps:
                    var session = FindAnyObjectByType<NetSession>();
                    if (session != null) session.ResetProps();
                    AnnounceRpc($"{who} reset the props");
                    break;
                case Action.FlopEveryone:
                    foreach (var player in FindObjectsByType<PlayerNet>(FindObjectsInactive.Exclude))
                    {
                        if (!player.IsSpawned) continue;
                        Vector2 side = Random.insideUnitCircle.normalized * Random.Range(1.5f, 3.5f);
                        player.ServerKnockdown(2.5f, new Vector3(side.x, Random.Range(2f, 4f), side.y), 0f);
                    }
                    AnnounceRpc($"{who}: EVERYBODY FLOP!");
                    break;
                case Action.GatherHere:
                    if (!NetworkManager.ConnectedClients.TryGetValue(by, out var client) || client.PlayerObject == null) return;
                    Transform leader = client.PlayerObject.transform;
                    int index = 0;
                    foreach (var player in FindObjectsByType<PlayerNet>(FindObjectsInactive.Exclude))
                    {
                        if (!player.IsSpawned || player.OwnerClientId == by) continue;
                        float angle = -60f + 40f * index++;
                        Vector3 spot = leader.position + Quaternion.Euler(0f, angle, 0f) * leader.forward * 2.5f;
                        player.TeleportRpc(spot, Quaternion.LookRotation(leader.position - spot, Vector3.up).eulerAngles.y);
                    }
                    AnnounceRpc($"{who} called everyone over");
                    break;
            }
        }

        // ---- panel -----------------------------------------------------------------------------

        void Update()
        {
            if (RkInput.RulesPanelPressed) SetOpen(!open);
        }

        void SetOpen(bool value)
        {
            open = value;
            PlayerMotor.UiHasCursor = false;
            Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = value;
        }

        void OnGUI()
        {
            if (feedStyle == null)
            {
                feedStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            }

            float y = 36f;
            foreach (var (text, time) in feed)
            {
                float age = Time.time - time;
                if (age > 5f) continue;
                var previous = GUI.color;
                GUI.color = new Color(1f, 0.9f, 0.4f, Mathf.Clamp01(5f - age));
                GUI.Label(new Rect(0f, y, Screen.width, 26f), text, feedStyle);
                GUI.color = previous;
                y += 24f;
            }

            if (!open) return;
            var area = new Rect(Screen.width - 470f, 80f, 450f, 44f + Titles.Length * 28f + 6f + 4f * 34f + 20f);
            PlayerMotor.UiHasCursor = area.Contains(Event.current.mousePosition);
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, area.height - 16f));
            GUILayout.Label("PLAYGROUND RULES (F9) — for everyone", titleStyle);
            for (int i = 0; i < Titles.Length; i++)
            {
                bool on = (Flags & (1 << i)) != 0;
                bool now = GUILayout.Toggle(on, $" {Titles[i]}  ·  {Hints[i]}", GUILayout.Height(26f));
                if (now != on) ToggleRpc(i);
            }
            GUILayout.Space(6f);
            if (GUILayout.Button("Reset all props", GUILayout.Height(30f))) ActionRpc((int)Action.ResetProps);
            if (GUILayout.Button("Everybody flop!", GUILayout.Height(30f))) ActionRpc((int)Action.FlopEveryone);
            if (GUILayout.Button("Gather everyone to me", GUILayout.Height(30f))) ActionRpc((int)Action.GatherHere);
            if (GUILayout.Button("Close", GUILayout.Height(30f))) SetOpen(false);
            GUILayout.EndArea();
        }
    }
}
