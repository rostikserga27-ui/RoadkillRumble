using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Friendly-fire switches for the session, chosen by the host (lobby checkbox, then the F9 panel):
    /// friendly fire itself (on by default), spawn protection and a cap on how much one teammate can hurt
    /// you. Every player is on the same team in this game, so every punch on a player is friendly; the
    /// multipliers live in PunchConfig. The switches sit in one server-written NetworkVariable on the
    /// PlaygroundRules object, so everyone sees the same state; only the server reads the anti-grief
    /// bookkeeping (who hurt whom, who is protected).
    /// </summary>
    public class FriendlyFire : NetworkBehaviour
    {
        public enum Switch { FriendlyFire, SpawnProtection, TeammateCap }

        static readonly string[] Titles = { "Friendly fire", "Spawn protection", "Teammate damage cap" };

        /// <summary>The lobby checkbox: what the host starts the session with.</summary>
        public static bool LobbyFriendlyFire = true;
        static bool lobbyInitialised;

        public static FriendlyFire Instance { get; private set; }

        /// <summary>Without a rules object (offline tests) the config's defaults apply.</summary>
        public static bool Has(Switch s)
        {
            if (Instance != null) return (Instance.flags.Value & (1 << (int)s)) != 0;
            var c = PunchConfig.Current;
            return s == Switch.FriendlyFire ? LobbyFriendlyFire : s == Switch.SpawnProtection ? c.spawnProtectionByDefault : c.teammateDamageCapByDefault;
        }

        public static bool Enabled => Has(Switch.FriendlyFire);

        /// <summary>Everyone is on the same team: the hook for team modes later.</summary>
        public static bool AreTeammates(PlayerNet a, PlayerNet b) => a != null && b != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            lobbyInitialised = false;
            ProtectedUntil.Clear();
            Ledger.Clear();
        }

        /// <summary>Call before showing the lobby so the checkbox starts at the config's default.</summary>
        public static void InitLobbyDefault()
        {
            if (lobbyInitialised) return;
            lobbyInitialised = true;
            LobbyFriendlyFire = PunchConfig.Current.friendlyFireByDefault;
        }

        // Written by the server: one bit per Switch.
        NetworkVariable<int> flags = new NetworkVariable<int>(0);

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            var c = PunchConfig.Current;
            InitLobbyDefault();
            int value = 0;
            if (LobbyFriendlyFire) value |= 1 << (int)Switch.FriendlyFire;
            if (c.spawnProtectionByDefault) value |= 1 << (int)Switch.SpawnProtection;
            if (c.teammateDamageCapByDefault) value |= 1 << (int)Switch.TeammateCap;
            flags.Value = value;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (IsServer)
            {
                ProtectedUntil.Clear();
                Ledger.Clear();
            }
        }

        /// <summary>Host only (the panel greys the toggles out for everyone else).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ToggleRpc(int which, RpcParams rpcParams = default)
        {
            if (which < 0 || which >= Titles.Length) return;
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            flags.Value ^= 1 << which;
            if (PlaygroundRules.Instance != null)
                PlaygroundRules.Instance.Announce($"Host turned {(Has((Switch)which) ? "on" : "off")} {Titles[which]}");
        }

        /// <summary>Server: set a switch directly (tests, host code).</summary>
        public void ServerSet(Switch which, bool on)
        {
            if (!IsServer) return;
            int bit = 1 << (int)which;
            flags.Value = on ? flags.Value | bit : flags.Value & ~bit;
        }

        // ---- anti-grief bookkeeping (server) -------------------------------------------------------------

        static readonly Dictionary<ulong, float> ProtectedUntil = new Dictionary<ulong, float>();
        // (attacker, victim) -> damage still counted against the cap, and when it was last updated.
        static readonly Dictionary<(ulong, ulong), (float amount, float time)> Ledger = new Dictionary<(ulong, ulong), (float, float)>();

        /// <summary>Server: this player just (re)spawned or was moved; protect them for a moment.</summary>
        public static void ServerProtect(ulong clientId)
        {
            ProtectedUntil[clientId] = Time.time + PunchConfig.Current.spawnProtectionSeconds;
        }

        public static bool IsProtected(ulong clientId) =>
            Has(Switch.SpawnProtection) && ProtectedUntil.TryGetValue(clientId, out float until) && Time.time < until;

        /// <summary>
        /// Server: how much of `damage` from this teammate still gets through the cap (and book it).
        /// The cap drains away over the window, so a friend can hurt you again later.
        /// </summary>
        public static float ServerCapDamage(ulong attacker, ulong victim, float damage)
        {
            if (!Has(Switch.TeammateCap) || damage <= 0f) return damage;
            var c = PunchConfig.Current;
            float drainPerSecond = c.teammateDamageCap / Mathf.Max(1f, c.teammateDamageWindowSeconds);
            Ledger.TryGetValue((attacker, victim), out var entry);
            float counted = Mathf.Max(0f, entry.amount - (Time.time - entry.time) * drainPerSecond);
            float allowed = Mathf.Clamp(c.teammateDamageCap - counted, 0f, damage);
            Ledger[(attacker, victim)] = (counted + allowed, Time.time);
            return allowed;
        }

        // ---- panel (beside the F9 rules) ------------------------------------------------------------------

        static Rect PanelRect => new Rect(Screen.width - 470f - 300f, 80f, 290f, 44f + Titles.Length * 28f + 30f);

        /// <summary>Is the mouse over this panel (so a click does not recapture the cursor)?</summary>
        public static bool PanelContains(Vector2 mouse) => Instance != null && PlaygroundRules.PanelOpen && PanelRect.Contains(mouse);

        GUIStyle titleStyle;

        void OnGUI()
        {
            if (!PlaygroundRules.PanelOpen) return;
            if (titleStyle == null) titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            var area = PanelRect;
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, area.height - 16f));
            GUILayout.Label("FISTS — host decides", titleStyle);
            bool host = IsServer;
            GUI.enabled = host;
            for (int i = 0; i < Titles.Length; i++)
            {
                bool on = Has((Switch)i);
                bool now = GUILayout.Toggle(on, $" {Titles[i]}", GUILayout.Height(26f));
                if (now != on) ToggleRpc(i);
            }
            GUI.enabled = true;
            var c = PunchConfig.Current;
            GUILayout.Label($"Friends take {c.friendlyDamageMultiplier * 100f:0}% damage, {c.friendlyKnockbackMultiplier * 100f:0}% knockback");
            GUILayout.EndArea();
        }
    }
}
