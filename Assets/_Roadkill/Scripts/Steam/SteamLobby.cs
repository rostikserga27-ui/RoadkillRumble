using System;
using Steamworks;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Friends-only Steam lobby that carries the host's Steam ID. The host creates it and invites
    /// friends through the Steam overlay; a friend who accepts the invite, clicks "Join Game" in the
    /// friends list, or launches the game from an invite (+connect_lobby) joins it, reads the host's
    /// Steam ID and connects through SteamP2PTransport.
    /// </summary>
    public class SteamLobby : MonoBehaviour
    {
        const string HostKey = "rk_host";

        public CSteamID LobbyId { get; private set; } = CSteamID.Nil;
        public bool InLobby => LobbyId != CSteamID.Nil;
        public string Status { get; private set; } = "";

        /// <summary>Raised on a joining friend with the host's Steam ID.</summary>
        public event Action<ulong> HostFound;

        CallResult<LobbyCreated_t> lobbyCreated;
        CallResult<LobbyEnter_t> lobbyEntered;
        Callback<GameLobbyJoinRequested_t> joinRequested;

        void Start()
        {
            if (!SteamBootstrap.Initialized) return;
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(data => Join(data.m_steamIDLobby));

            // Launched by accepting an invite while the game was closed.
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobby))
                    Join(new CSteamID(lobby));
            }
        }

        void OnDestroy()
        {
            Leave();
            lobbyCreated?.Dispose();
            lobbyEntered?.Dispose();
            joinRequested?.Dispose();
        }

        public void Create()
        {
            if (!SteamBootstrap.Initialized) return;
            Status = "Opening a Steam lobby…";
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4));
        }

        void OnLobbyCreated(LobbyCreated_t data, bool ioFailure)
        {
            if (ioFailure || data.m_eResult != EResult.k_EResultOK)
            {
                Status = "Could not open a Steam lobby.";
                return;
            }
            LobbyId = new CSteamID(data.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(LobbyId, HostKey, SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(LobbyId, "name", $"{SteamFriends.GetPersonaName()}'s wreck");
            SteamFriends.SetRichPresence("status", "Stealing a car back");
            Status = "Lobby open. Press F4 to invite friends.";
            Debug.Log($"Roadkill: Steam lobby {LobbyId} created");
        }

        public void Invite()
        {
            if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
        }

        public void Join(CSteamID lobby)
        {
            if (!SteamBootstrap.Initialized) return;
            Leave();
            Status = "Joining your friend's lobby…";
            lobbyEntered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        void OnLobbyEntered(LobbyEnter_t data, bool ioFailure)
        {
            if (ioFailure || data.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = "Could not join that lobby. It may be full or closed.";
                return;
            }
            LobbyId = new CSteamID(data.m_ulSteamIDLobby);
            string host = SteamMatchmaking.GetLobbyData(LobbyId, HostKey);
            if (!ulong.TryParse(host, out ulong hostId))
            {
                Status = "That lobby has no host.";
                return;
            }
            if (hostId == SteamUser.GetSteamID().m_SteamID) return;   // our own lobby
            Status = "Connecting through Steam…";
            HostFound?.Invoke(hostId);
        }

        public void Leave()
        {
            if (!InLobby) return;
            if (SteamBootstrap.Initialized) SteamMatchmaking.LeaveLobby(LobbyId);
            LobbyId = CSteamID.Nil;
        }
    }
}
