using Steamworks;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Starts the Steam API once per run and pumps its callbacks. The game still works without
    /// Steam (LAN by IP); Steam features just stay off. App ID comes from steam_appid.txt
    /// (480, Valve's Spacewar test app, until the game has its own).
    /// </summary>
    public class SteamBootstrap : MonoBehaviour
    {
        public static bool Initialized { get; private set; }
        public static string Error { get; private set; } = "";

        static SteamBootstrap instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Initialized = false;
            Error = "";
            instance = null;
        }

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("Steam");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SteamBootstrap>();
        }

        void Awake()
        {
            if (!Packsize.Test())
            {
                Error = "Steamworks.NET was built for the wrong platform";
                return;
            }
            Initialized = SteamAPI.InitEx(out string message) == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
            Error = Initialized ? "" : $"Steam is not running or not logged in ({message})";
            if (Initialized)
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                Debug.Log($"Roadkill: Steam ready as {SteamFriends.GetPersonaName()}");
            }
            else
            {
                Debug.LogWarning($"Roadkill: {Error}");
            }
        }

        void Update()
        {
            if (Initialized) SteamAPI.RunCallbacks();
        }

        void OnDestroy()
        {
            if (instance != this) return;
            if (Initialized) SteamAPI.Shutdown();
            Initialized = false;
            instance = null;
        }
    }
}
