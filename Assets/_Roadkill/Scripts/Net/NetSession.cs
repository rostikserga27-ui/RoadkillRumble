using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Creates the NetworkManager from code, shows the lobby and, on the server, spawns the networked
    /// props. Two ways to play: through Steam (host, then invite friends with F4; no IPs or ports) or
    /// by IP on a LAN / VPN. Prefabs come from Resources/RoadkillNet, which the editor tool
    /// Roadkill > Rebuild Network Prefabs generates (it also runs on its own when they are missing).
    /// </summary>
    public class NetSession : MonoBehaviour
    {
        public const string ResourceFolder = "RoadkillNet";
        public const string PlayerPrefabName = "NetPlayer";
        public const ushort DefaultPort = 7777;

        NetworkManager manager;
        UnityTransport ipTransport;
        SteamP2PTransport steamTransport;
        SteamLobby steamLobby;
        bool usingSteam;

        string address = "127.0.0.1";
        string portText = DefaultPort.ToString();
        string status = "";
        GUIStyle style;
        GUIStyle hint;
        Camera lobbyCamera;

        void Awake()
        {
            CreateLobbyCamera();
            SteamBootstrap.Ensure();
            manager = NetworkManager.Singleton != null ? NetworkManager.Singleton : CreateManager();
            if (manager == null) return;

            ipTransport = manager.GetComponent<UnityTransport>();
            steamTransport = manager.GetComponent<SteamP2PTransport>();
            steamLobby = gameObject.AddComponent<SteamLobby>();
            steamLobby.HostFound += StartSteamClient;

            manager.OnServerStarted += SpawnWorld;
            manager.OnClientDisconnectCallback += OnClientDisconnect;
        }

        /// <summary>Command line: -rkhost, or -rkjoin [ip]; -rktest runs the scripted gate check.</summary>
        void Start()
        {
            if (manager == null) return;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-rkhost") StartHost();
                if (args[i] == "-rkjoin")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) address = args[i + 1];
                    StartClient();
                }
                if (args[i] == "-rktest") gameObject.AddComponent<NetTest>();
                if (args[i] == "-rktestlog") gameObject.AddComponent<NetTest>().logMode = true;
                if (args[i] == "-rktestwalk") gameObject.AddComponent<NetTest>().walkMode = true;
            }
        }

        void OnDestroy()
        {
            if (manager == null) return;
            manager.OnServerStarted -= SpawnWorld;
            manager.OnClientDisconnectCallback -= OnClientDisconnect;
            if (steamLobby != null) steamLobby.HostFound -= StartSteamClient;
        }

        /// <summary>Overview shot shown in the lobby until the local player (who brings a camera) spawns.</summary>
        void CreateLobbyCamera()
        {
            var go = new GameObject("LobbyCamera");
            go.transform.SetPositionAndRotation(new Vector3(0f, 14f, -30f), Quaternion.Euler(25f, 0f, 0f));
            // No AudioListener: the player's camera brings the only one.
            lobbyCamera = go.AddComponent<Camera>();
        }

        void Update()
        {
            bool hasLocalPlayer = manager != null && manager.IsClient && manager.LocalClient != null
                && manager.LocalClient.PlayerObject != null;
            if (lobbyCamera.gameObject.activeSelf == hasLocalPlayer) lobbyCamera.gameObject.SetActive(!hasLocalPlayer);

            if (manager == null || steamLobby == null) return;
            if (usingSteam && manager.IsServer && RkInput.InvitePressed) steamLobby.Invite();
            // Back in the menu: let go of any Steam lobby we were in.
            if (!manager.IsListening && steamLobby.InLobby && !manager.ShutdownInProgress && !Joining) steamLobby.Leave();
        }

        bool Joining => steamLobby.Status.StartsWith("Joining");

        NetworkManager CreateManager()
        {
            var player = Resources.Load<GameObject>($"{ResourceFolder}/{PlayerPrefabName}");
            if (player == null)
            {
                status = "Network prefabs are missing. Run Roadkill > Rebuild Network Prefabs.";
                Debug.LogError($"Roadkill: {status}");
                return null;
            }

            // Configure while inactive: NetworkManager reads its config in Awake.
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            var utp = go.AddComponent<UnityTransport>();
            // Voice plus physics state overflows the default 128-packet queue.
            utp.MaxPacketQueueSize = 512;
            go.AddComponent<SteamP2PTransport>();
            var networkManager = go.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                PlayerPrefab = player,
                // Every peer builds the same greybox itself, so there is no scene to synchronise.
                EnableSceneManagement = false,
                TickRate = 30
            };
            // LoadAll returns the same assets in the same order on every peer.
            foreach (var prefab in Resources.LoadAll<GameObject>(ResourceFolder))
            {
                if (prefab != player) networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
            }
            go.SetActive(true);
            return networkManager;
        }

        void SpawnWorld()
        {
            foreach (var placement in PropLayout.Greybox)
            {
                var prefab = Resources.Load<GameObject>($"{ResourceFolder}/{placement.Prefab}");
                if (prefab == null)
                {
                    Debug.LogWarning($"Roadkill: no network prefab named {placement.Prefab}");
                    continue;
                }
                var instance = Instantiate(prefab, placement.Position, placement.Rotation);
                instance.GetComponent<NetworkObject>().Spawn(true);
            }
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (manager.IsServer || clientId != manager.LocalClientId) return;
            status = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected." : manager.DisconnectReason;
        }

        // ---- menu ------------------------------------------------------------------------------

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
                hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperCenter };
                hint.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            }

            if (manager != null && (manager.IsServer || manager.IsConnectedClient))
            {
                if (usingSteam && manager.IsServer)
                    GUI.Label(new Rect(0f, 8f, Screen.width, 24f), "F4 — invite Steam friends", hint);
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var area = new Rect(Screen.width * 0.5f - 190f, Screen.height * 0.5f - 190f, 380f, 380f);
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 12f, area.width - 32f, area.height - 24f));
            GUILayout.Label("ROADKILL RUMBLE — prototype", style);

            if (manager == null)
            {
                GUILayout.Label(status, style);
                GUILayout.EndArea();
                return;
            }

            bool connecting = manager.IsClient && !manager.IsConnectedClient;
            GUI.enabled = !connecting;

            // Steam
            if (SteamBootstrap.Initialized)
            {
                GUILayout.Label($"Steam: {Steamworks.SteamFriends.GetPersonaName()}", style);
                if (GUILayout.Button("Host via Steam", GUILayout.Height(40f))) StartSteamHost();
                GUILayout.Label("Friends join from your Steam invite or \"Join Game\" in the friends list.", hint);
            }
            else
            {
                GUILayout.Label($"Steam off: {SteamBootstrap.Error}", style);
            }

            // LAN / IP
            GUILayout.Space(12f);
            GUILayout.Label("By IP (LAN or VPN)", style);
            if (GUILayout.Button("Host", GUILayout.Height(32f))) StartHost();
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP", GUILayout.Width(24f));
            address = GUILayout.TextField(address, GUILayout.Width(170f));
            GUILayout.Label("Port", GUILayout.Width(36f));
            portText = GUILayout.TextField(portText);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Join", GUILayout.Height(32f))) StartClient();
            GUI.enabled = true;

            string lobbyStatus = steamLobby != null ? steamLobby.Status : "";
            if (connecting)
            {
                GUILayout.Label(usingSteam ? "Connecting through Steam…" : $"Connecting to {address}:{portText}…", style);
                if (GUILayout.Button("Cancel")) manager.Shutdown();
            }
            else if (status.Length > 0)
            {
                GUILayout.Label(status, style);
            }
            else if (lobbyStatus.Length > 0)
            {
                GUILayout.Label(lobbyStatus, style);
            }
            GUILayout.EndArea();
        }

        // ---- starting a session -------------------------------------------------------------------

        void StartSteamHost()
        {
            UseSteam(true);
            if (manager.StartHost())
            {
                status = "";
                steamLobby.Create();
            }
            else
            {
                status = "Could not host through Steam.";
            }
        }

        void StartSteamClient(ulong hostSteamId)
        {
            if (manager.IsListening) return;
            UseSteam(true);
            steamTransport.TargetSteamId = hostSteamId;
            status = manager.StartClient() ? "" : "Could not connect through Steam.";
        }

        public void StartHost()
        {
            UseSteam(false);
            // Listen on every interface so friends on the LAN can join.
            ipTransport.SetConnectionData("127.0.0.1", Port(), "0.0.0.0");
            status = manager.StartHost() ? "" : "Could not host. Is the port already in use?";
        }

        void StartClient()
        {
            UseSteam(false);
            ipTransport.SetConnectionData(address.Trim(), Port());
            status = manager.StartClient() ? "" : "Could not start the client.";
        }

        void UseSteam(bool steam)
        {
            usingSteam = steam;
            manager.NetworkConfig.NetworkTransport = steam ? steamTransport : ipTransport;
        }

        ushort Port() => ushort.TryParse(portText, out ushort port) ? port : DefaultPort;
    }
}
