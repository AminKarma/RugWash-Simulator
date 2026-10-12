using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>
    /// Transport Netcode for GameObjects sur Steam Networking Sockets (pair à pair via les relais de Steam) :
    /// connexion par identifiant Steam, sans adresse IP ni ouverture de port, à travers Internet.
    /// L'hôte écoute (CreateListenSocketP2P) ; un invité se connecte à l'identifiant Steam de l'hôte (ConnectP2P).
    /// Nécessite Steam initialisé (<see cref="SteamPlatform"/>) et ses rappels exécutés chaque frame.
    /// </summary>
    public sealed class SteamSocketsTransport : NetworkTransport
    {
        const int ReceiveBatch = 64;

        /// <summary>Identifiant Steam de l'hôte (côté invité, avant StartClient).</summary>
        public ulong HostSteamId { get; set; }

        public override ulong ServerClientId => 0;

        Callback<SteamNetConnectionStatusChangedCallback_t> statusChanged;
        HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
        HSteamNetPollGroup pollGroup = HSteamNetPollGroup.Invalid;
        HSteamNetConnection serverConnection = HSteamNetConnection.Invalid;
        bool isServer;
        ulong nextClientId = 1;
        readonly Dictionary<ulong, HSteamNetConnection> clients = new Dictionary<ulong, HSteamNetConnection>();
        readonly Dictionary<uint, ulong> connectionToClient = new Dictionary<uint, ulong>();
        readonly Queue<(NetworkEvent type, ulong clientId)> events = new Queue<(NetworkEvent, ulong)>();
        readonly IntPtr[] messages = new IntPtr[ReceiveBatch];
        readonly Queue<(ulong clientId, byte[] data)> received = new Queue<(ulong, byte[])>();

        public override void Initialize(NetworkManager networkManager = null)
        {
            statusChanged ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
        }

        public override bool StartServer()
        {
            isServer = true;
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            pollGroup = SteamNetworkingSockets.CreatePollGroup();
            return listenSocket != HSteamListenSocket.Invalid && pollGroup != HSteamNetPollGroup.Invalid;
        }

        public override bool StartClient()
        {
            isServer = false;
            if (HostSteamId == 0) return false;
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(HostSteamId));
            serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
            return serverConnection != HSteamNetConnection.Invalid;
        }

        void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            var connection = data.m_hConn;
            var state = data.m_info.m_eState;
            if (isServer)
            {
                switch (state)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                        if (data.m_info.m_hListenSocket == listenSocket && SteamNetworkingSockets.AcceptConnection(connection) == EResult.k_EResultOK)
                            SteamNetworkingSockets.SetConnectionPollGroup(connection, pollGroup);
                        else
                            SteamNetworkingSockets.CloseConnection(connection, 0, "Connexion refusée", false);
                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                        ulong id = nextClientId++;
                        clients[id] = connection;
                        connectionToClient[connection.m_HSteamNetConnection] = id;
                        events.Enqueue((NetworkEvent.Connect, id));
                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                        if (connectionToClient.TryGetValue(connection.m_HSteamNetConnection, out ulong lost))
                        {
                            connectionToClient.Remove(connection.m_HSteamNetConnection);
                            clients.Remove(lost);
                            events.Enqueue((NetworkEvent.Disconnect, lost));
                        }
                        SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                        break;
                }
                return;
            }

            if (connection != serverConnection) return;
            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    events.Enqueue((NetworkEvent.Connect, ServerClientId));
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    events.Enqueue((NetworkEvent.Disconnect, ServerClientId));
                    SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                    serverConnection = HSteamNetConnection.Invalid;
                    break;
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            HSteamNetConnection connection;
            if (isServer)
            {
                if (!clients.TryGetValue(clientId, out connection)) return;
            }
            else connection = serverConnection;
            if (connection == HSteamNetConnection.Invalid || payload.Count == 0) return;

            int flags = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced
                ? Constants.k_nSteamNetworkingSend_Unreliable
                : Constants.k_nSteamNetworkingSend_Reliable;
            var handle = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
            try
            {
                IntPtr pointer = handle.AddrOfPinnedObject() + payload.Offset;
                SteamNetworkingSockets.SendMessageToConnection(connection, pointer, (uint)payload.Count, flags, out _);
            }
            finally
            {
                handle.Free();
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            if (events.Count > 0)
            {
                var next = events.Dequeue();
                clientId = next.clientId;
                return next.type;
            }
            if (received.Count == 0) ReceiveBatchOfMessages();
            if (received.Count > 0)
            {
                var message = received.Dequeue();
                clientId = message.clientId;
                payload = new ArraySegment<byte>(message.data);
                return NetworkEvent.Data;
            }
            clientId = 0;
            return NetworkEvent.Nothing;
        }

        void ReceiveBatchOfMessages()
        {
            int count = isServer
                ? (pollGroup != HSteamNetPollGroup.Invalid ? SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, messages, ReceiveBatch) : 0)
                : (serverConnection != HSteamNetConnection.Invalid ? SteamNetworkingSockets.ReceiveMessagesOnConnection(serverConnection, messages, ReceiveBatch) : 0);
            for (int i = 0; i < count; i++)
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(messages[i]);
                try
                {
                    ulong from = ServerClientId;
                    if (isServer && !connectionToClient.TryGetValue(message.m_conn.m_HSteamNetConnection, out from)) continue;
                    var data = new byte[message.m_cbSize];
                    Marshal.Copy(message.m_pData, data, 0, message.m_cbSize);
                    received.Enqueue((from, data));
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(messages[i]);
                }
            }
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!clients.TryGetValue(clientId, out var connection)) return;
            SteamNetworkingSockets.CloseConnection(connection, 0, "Déconnecté par l'hôte", true);
            clients.Remove(clientId);
            connectionToClient.Remove(connection.m_HSteamNetConnection);
        }

        public override void DisconnectLocalClient()
        {
            if (serverConnection == HSteamNetConnection.Invalid) return;
            SteamNetworkingSockets.CloseConnection(serverConnection, 0, "Départ du joueur", true);
            serverConnection = HSteamNetConnection.Invalid;
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            var connection = isServer ? (clients.TryGetValue(clientId, out var c) ? c : HSteamNetConnection.Invalid) : serverConnection;
            if (connection == HSteamNetConnection.Invalid) return 0;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK
                ? (ulong)Mathf.Max(0, status.m_nPing) : 0;
        }

        public override void Shutdown()
        {
            foreach (var connection in clients.Values)
                SteamNetworkingSockets.CloseConnection(connection, 0, "Fin de la partie", false);
            clients.Clear();
            connectionToClient.Clear();
            if (serverConnection != HSteamNetConnection.Invalid)
                SteamNetworkingSockets.CloseConnection(serverConnection, 0, "Fin de la partie", false);
            serverConnection = HSteamNetConnection.Invalid;
            if (listenSocket != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listenSocket);
            listenSocket = HSteamListenSocket.Invalid;
            if (pollGroup != HSteamNetPollGroup.Invalid) SteamNetworkingSockets.DestroyPollGroup(pollGroup);
            pollGroup = HSteamNetPollGroup.Invalid;
            events.Clear();
            received.Clear();
            nextClientId = 1;
        }

        void OnDestroy()
        {
            statusChanged?.Dispose();
            statusChanged = null;
        }
    }
}
