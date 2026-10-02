using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Netcode transport over Steam Networking Sockets. Players connect by Steam ID, and traffic goes
    /// through Valve's relay network, so nobody opens ports or shares an IP. The host listens on a
    /// P2P virtual port; clients connect to the host's Steam ID (taken from the Steam lobby).
    /// </summary>
    public class SteamP2PTransport : NetworkTransport
    {
        const int VirtualPort = 0;

        /// <summary>Client side: the Steam ID of the host to connect to.</summary>
        public ulong TargetSteamId;

        public override ulong ServerClientId => 0;

        struct PendingEvent
        {
            public NetworkEvent Type;
            public ulong ClientId;
            public ArraySegment<byte> Payload;
        }

        readonly Queue<PendingEvent> events = new Queue<PendingEvent>();
        readonly Dictionary<ulong, HSteamNetConnection> connectionsById = new Dictionary<ulong, HSteamNetConnection>();
        readonly Dictionary<HSteamNetConnection, ulong> idsByConnection = new Dictionary<HSteamNetConnection, ulong>();
        readonly List<KeyValuePair<HSteamNetConnection, ulong>> receiveList = new List<KeyValuePair<HSteamNetConnection, ulong>>();
        readonly IntPtr[] messages = new IntPtr[256];

        Callback<SteamNetConnectionStatusChangedCallback_t> statusChanged;
        HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
        HSteamNetConnection serverConnection = HSteamNetConnection.Invalid;
        bool isServer;
        ulong nextClientId = 1;

        public override void Initialize(NetworkManager networkManager = null) { }

        public override bool StartServer()
        {
            if (!SteamBootstrap.Initialized) return Fail("Steam is not running.");
            isServer = true;
            ListenForStatus();
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, null);
            return listenSocket != HSteamListenSocket.Invalid || Fail("Could not open a Steam listen socket.");
        }

        public override bool StartClient()
        {
            if (!SteamBootstrap.Initialized) return Fail("Steam is not running.");
            if (TargetSteamId == 0) return Fail("No host Steam ID to connect to.");
            isServer = false;
            ListenForStatus();
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(TargetSteamId));
            serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, null);
            return serverConnection != HSteamNetConnection.Invalid || Fail("Could not start a Steam connection.");
        }

        static bool Fail(string message)
        {
            Debug.LogError($"Roadkill Steam transport: {message}");
            return false;
        }

        void ListenForStatus()
        {
            if (statusChanged == null)
                statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
        }

        void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            HSteamNetConnection connection = data.m_hConn;
            switch (data.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (isServer && data.m_info.m_hListenSocket == listenSocket)
                        SteamNetworkingSockets.AcceptConnection(connection);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (isServer)
                    {
                        if (idsByConnection.ContainsKey(connection)) break;
                        ulong clientId = nextClientId++;
                        idsByConnection[connection] = clientId;
                        connectionsById[clientId] = connection;
                        Enqueue(NetworkEvent.Connect, clientId, default);
                    }
                    else if (connection == serverConnection)
                    {
                        Enqueue(NetworkEvent.Connect, ServerClientId, default);
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Forget(connection);
                    SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                    break;
            }
        }

        void Forget(HSteamNetConnection connection)
        {
            if (isServer)
            {
                if (!idsByConnection.TryGetValue(connection, out ulong clientId)) return;
                idsByConnection.Remove(connection);
                connectionsById.Remove(clientId);
                Enqueue(NetworkEvent.Disconnect, clientId, default);
            }
            else if (connection == serverConnection)
            {
                serverConnection = HSteamNetConnection.Invalid;
                Enqueue(NetworkEvent.Disconnect, ServerClientId, default);
            }
        }

        void Enqueue(NetworkEvent type, ulong clientId, ArraySegment<byte> payload) =>
            events.Enqueue(new PendingEvent { Type = type, ClientId = clientId, Payload = payload });

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            if (events.Count == 0) ReceiveAll();
            receiveTime = Time.realtimeSinceStartup;
            if (events.Count > 0)
            {
                var next = events.Dequeue();
                clientId = next.ClientId;
                payload = next.Payload;
                return next.Type;
            }
            clientId = 0;
            payload = default;
            return NetworkEvent.Nothing;
        }

        void ReceiveAll()
        {
            if (isServer)
            {
                receiveList.Clear();
                receiveList.AddRange(idsByConnection);
                foreach (var pair in receiveList) Receive(pair.Key, pair.Value);
            }
            else if (serverConnection != HSteamNetConnection.Invalid)
            {
                Receive(serverConnection, ServerClientId);
            }
        }

        void Receive(HSteamNetConnection connection, ulong clientId)
        {
            int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, messages, messages.Length);
            for (int i = 0; i < count; i++)
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(messages[i]);
                var data = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, data, 0, message.m_cbSize);
                SteamNetworkingMessage_t.Release(messages[i]);
                Enqueue(NetworkEvent.Data, clientId, new ArraySegment<byte>(data));
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            HSteamNetConnection connection;
            if (isServer)
            {
                if (!connectionsById.TryGetValue(clientId, out connection)) return;
            }
            else
            {
                connection = serverConnection;
                if (connection == HSteamNetConnection.Invalid) return;
            }

            bool unreliable = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced;
            int flags = unreliable ? Constants.k_nSteamNetworkingSend_UnreliableNoNagle : Constants.k_nSteamNetworkingSend_ReliableNoNagle;

            var handle = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
            try
            {
                IntPtr start = handle.AddrOfPinnedObject() + payload.Offset;
                SteamNetworkingSockets.SendMessageToConnection(connection, start, (uint)payload.Count, flags, out _);
            }
            finally
            {
                handle.Free();
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            HSteamNetConnection connection = isServer
                ? (connectionsById.TryGetValue(clientId, out var c) ? c : HSteamNetConnection.Invalid)
                : serverConnection;
            if (connection == HSteamNetConnection.Invalid) return 0;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK
                ? (ulong)Mathf.Max(0, status.m_nPing)
                : 0;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!connectionsById.TryGetValue(clientId, out var connection)) return;
            connectionsById.Remove(clientId);
            idsByConnection.Remove(connection);
            SteamNetworkingSockets.CloseConnection(connection, 0, "Kicked", false);
        }

        public override void DisconnectLocalClient()
        {
            if (serverConnection == HSteamNetConnection.Invalid) return;
            SteamNetworkingSockets.CloseConnection(serverConnection, 0, "Left", true);
            serverConnection = HSteamNetConnection.Invalid;
        }

        public override void Shutdown()
        {
            if (SteamBootstrap.Initialized)
            {
                foreach (var connection in idsByConnection.Keys) SteamNetworkingSockets.CloseConnection(connection, 0, "Host left", false);
                if (serverConnection != HSteamNetConnection.Invalid) SteamNetworkingSockets.CloseConnection(serverConnection, 0, "Left", false);
                if (listenSocket != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listenSocket);
            }
            statusChanged?.Dispose();
            statusChanged = null;
            listenSocket = HSteamListenSocket.Invalid;
            serverConnection = HSteamNetConnection.Invalid;
            idsByConnection.Clear();
            connectionsById.Clear();
            events.Clear();
            isServer = false;
            nextClientId = 1;
        }
    }
}
