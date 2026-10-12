using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RugWash.Network
{
    /// <summary>
    /// Session multijoueur, conservée d'une scène à l'autre (le menu principal la crée, l'atelier la retrouve).
    /// Crée et configure le NetworkManager (Netcode for GameObjects), choisit le transport — Steam Networking Sockets
    /// pour jouer par Internet via Steam, Unity Transport pour la connexion directe en réseau local — et gère le cycle de
    /// vie : salon Steam, hôte, invité, admission (version, places), départs, déconnexions et retour au menu.
    /// L'appartenance à un salon Steam et la connexion au jeu sont deux états distincts (<see cref="SessionState"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerSession : MonoBehaviour
    {
        public const string WorkshopScene = "Workshop";
        public const string MainMenuScene = "MainMenu";

        NetworkContent content;
        NetworkManager manager;
        UnityTransport directTransport;
        SteamSocketsTransport steamTransport;
        readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
        bool leaveRequested;
        bool startedAsHost;
        string reconnectAddress;
        ushort reconnectPort;
        ulong reconnectLobby;
        ulong pendingLobbyJoin;

        public NetworkContent Content => content;
        public NetworkManager Manager => manager;
        public SteamPlatform Steam { get; private set; }
        public SessionState State { get; private set; } = SessionState.Offline;
        /// <summary>Dernier message à afficher (progression, erreur, raison d'une déconnexion).</summary>
        public string Message { get; private set; } = string.Empty;
        public bool LastEndedWithError { get; private set; }
        public string LocalName { get; set; } = "Joueur";
        public bool UsesSteamTransport { get; private set; }

        public bool InSession => manager != null && (manager.IsServer || manager.IsClient);
        public bool IsHost => manager != null && manager.IsServer;
        public bool IsGuest => manager != null && manager.IsClient && !manager.IsServer;
        public bool CanReconnect => !InSession && (reconnectLobby != 0 || !string.IsNullOrEmpty(reconnectAddress));

        /// <summary>État, message ou membres modifiés (pour l'interface).</summary>
        public event Action Changed;
        public event Action<List<LobbySummary>> LobbiesFound;

        public static MultiplayerSession Find() => FindAnyObjectByType<MultiplayerSession>();

        /// <summary>Retourne la session existante ou la crée (objet conservé entre les scènes).</summary>
        public static MultiplayerSession GetOrCreate(NetworkContent networkContent)
        {
            var session = Find();
            if (session == null)
            {
                var go = new GameObject("[Multijoueur]");
                DontDestroyOnLoad(go);
                session = go.AddComponent<MultiplayerSession>();
            }
            if (networkContent != null) session.content = networkContent;
            return session;
        }

        /// <summary>Nom affiché d'un joueur connu de l'hôte (identifiant Netcode).</summary>
        public string NameOf(ulong clientId) => names.TryGetValue(clientId, out var name) ? name : $"Joueur {clientId + 1}";

        void SetState(SessionState state, string message = null)
        {
            State = state;
            if (message != null) Message = message;
            Changed?.Invoke();
        }

        void Update() => Steam?.RunCallbacks();

        // ---------------------------------------------------------------- NetworkManager

        void EnsureManager()
        {
            if (manager != null) return;
            var go = new GameObject("NetworkManager");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            manager = go.AddComponent<NetworkManager>();
            directTransport = go.AddComponent<UnityTransport>();
            steamTransport = go.AddComponent<SteamSocketsTransport>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = directTransport,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                TickRate = 30,
                ClientConnectionBufferTimeout = 15,
            };
            go.SetActive(true);
            if (content != null)
            {
                if (content.workshopPrefab != null) manager.AddNetworkPrefab(content.workshopPrefab);
                if (content.avatarPrefab != null) manager.AddNetworkPrefab(content.avatarPrefab);
            }
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientDisconnectCallback += OnClientDisconnect;
            manager.OnTransportFailure += OnTransportFailure;
        }

        string Version => content != null ? content.protocolVersion : "rugwash-coop";
        int MaxPlayers => content != null ? content.maxPlayers : 4;

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // L'hôte lui-même passe aussi par ici : il est toujours accepté.
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                names[request.ClientNetworkId] = SessionRules.CleanName(LocalName);
                response.Approved = true;
                response.CreatePlayerObject = false;
                return;
            }
            int connected = manager.ConnectedClientsIds.Count;
            bool ok = SessionRules.Approve(request.Payload, Version, connected, MaxPlayers, out string name, out string reason);
            response.Approved = ok;
            response.CreatePlayerObject = false;
            response.Reason = reason;
            if (ok) names[request.ClientNetworkId] = name;
        }

        bool StartHost()
        {
            leaveRequested = false;
            startedAsHost = true;
            names.Clear();
            SetState(SessionState.Connecting, "Démarrage de la partie…");
            if (!manager.StartHost())
            {
                Fail("Impossible de démarrer la partie (port occupé ou réseau indisponible).");
                return false;
            }
            manager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
            manager.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
            var status = manager.SceneManager.LoadScene(WorkshopScene, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Fail($"Chargement de l'atelier impossible ({status}).");
                return false;
            }
            return true;
        }

        /// <summary>Hôte : l'atelier est chargé chez tous les joueurs présents → l'objet réseau de l'atelier apparaît.</summary>
        void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != WorkshopScene || manager == null || !manager.IsServer) return;
            if (NetworkWorkshop.Instance != null || content == null || content.workshopPrefab == null) return;
            var instance = Instantiate(content.workshopPrefab);
            instance.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
        }

        bool StartClient()
        {
            leaveRequested = false;
            startedAsHost = false;
            manager.NetworkConfig.ConnectionData = new ConnectionPayload(Version, LocalName).Encode();
            SetState(SessionState.Connecting, "Connexion à la partie…");
            if (!manager.StartClient())
            {
                Fail("Connexion impossible.");
                return false;
            }
            return true;
        }

        /// <summary>L'atelier réseau est prêt sur ce poste (objet réseau de l'atelier apparu).</summary>
        public void MarkInGame()
        {
            if (State == SessionState.Connecting) SetState(SessionState.InGame, IsHost ? "Partie en cours (hôte)." : "Connecté à la partie.");
            if (IsHost && UsesSteamTransport && Steam != null)
            {
                Steam.SetLobbyData(SteamPlatform.HostKey, Steam.LocalSteamId.ToString());
                Steam.SetLobbyData(SteamPlatform.StateKey, "playing");
            }
        }

        // ---------------------------------------------------------------- Connexion directe (réseau local)

        public bool HostDirect(ushort port)
        {
            if (InSession) return false;
            EnsureManager();
            UsesSteamTransport = false;
            manager.NetworkConfig.NetworkTransport = directTransport;
            directTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            reconnectAddress = null;
            reconnectLobby = 0;
            return StartHost();
        }

        public bool JoinDirect(string address, ushort port)
        {
            if (InSession) return false;
            EnsureManager();
            UsesSteamTransport = false;
            manager.NetworkConfig.NetworkTransport = directTransport;
            directTransport.SetConnectionData(address, port);
            reconnectAddress = address;
            reconnectPort = port;
            reconnectLobby = 0;
            return StartClient();
        }

        // ---------------------------------------------------------------- Steam

        /// <summary>Initialise Steam si possible. Faux (message explicite) sans client Steam lancé.</summary>
        public bool EnsureSteam()
        {
            if (Steam == null)
            {
                Steam = new SteamPlatform();
                Steam.LobbyCreated += OnLobbyCreated;
                Steam.LobbyEntered += OnLobbyEntered;
                Steam.LobbyMembersChanged += () => Changed?.Invoke();
                Steam.LobbyDataChanged += OnLobbyDataChanged;
                Steam.LobbyListReceived += list => LobbiesFound?.Invoke(list);
                Steam.JoinRequested += OnJoinRequested;
                Steam.Error += message => Fail(message);
            }
            if (Steam.IsInitialized) return true;
            bool ok = Steam.Initialize(content != null ? content.steamAppId : 480u);
            if (ok)
            {
                LocalName = Steam.PersonaName;
                ulong launchLobby = SteamPlatform.LobbyFromCommandLine(Environment.GetCommandLineArgs());
                if (launchLobby != 0) JoinSteamLobby(launchLobby);
            }
            Changed?.Invoke();
            return ok;
        }

        public void CreateSteamLobby(bool publicLobby)
        {
            if (InSession || !EnsureSteam()) return;
            SetState(SessionState.JoiningLobby, "Création du salon Steam…");
            Steam.CreateLobby(publicLobby, MaxPlayers, Version);
        }

        public void SearchSteamLobbies()
        {
            if (!EnsureSteam()) return;
            Message = "Recherche des parties…";
            Changed?.Invoke();
            Steam.RequestLobbies(Version);
        }

        public void JoinSteamLobby(ulong lobbyId)
        {
            if (InSession || !EnsureSteam() || lobbyId == 0) return;
            if (Steam.InLobby) Steam.LeaveLobby();
            reconnectLobby = lobbyId;
            reconnectAddress = null;
            SetState(SessionState.JoiningLobby, "Connexion au salon Steam…");
            Steam.JoinLobby(lobbyId);
        }

        public bool InviteFriends() => Steam != null && Steam.OpenInviteOverlay();

        void OnLobbyCreated(ulong lobbyId)
        {
            reconnectLobby = 0;
            SetState(SessionState.InLobby, "Salon créé : invitez vos amis, puis lancez la partie.");
        }

        void OnLobbyEntered(ulong lobbyId)
        {
            if (Steam.IsLobbyOwner)
            {
                SetState(SessionState.InLobby);
                return;
            }
            SetState(SessionState.InLobby, "Dans le salon : en attente du lancement par l'hôte…");
            TryConnectToLobbyHost();
        }

        void OnLobbyDataChanged()
        {
            Changed?.Invoke();
            if (Steam.IsLobbyOwner) return;
            if (State == SessionState.InLobby) TryConnectToLobbyHost();
            if (Steam.GetLobbyData(SteamPlatform.StateKey) == "closed" && !InSession) LeaveLobbyOnly("L'hôte a fermé le salon.");
        }

        void TryConnectToLobbyHost()
        {
            if (InSession) return;
            if (Steam.GetLobbyData(SteamPlatform.StateKey) != "playing") return;
            if (!ulong.TryParse(Steam.GetLobbyData(SteamPlatform.HostKey), out ulong hostId) || hostId == 0) return;
            EnsureManager();
            UsesSteamTransport = true;
            steamTransport.HostSteamId = hostId;
            manager.NetworkConfig.NetworkTransport = steamTransport;
            StartClient();
        }

        void OnJoinRequested(ulong lobbyId)
        {
            // Invitation acceptée depuis Steam pendant que le jeu tourne : on quitte proprement la partie en cours.
            if (InSession)
            {
                pendingLobbyJoin = lobbyId;
                Leave("Invitation acceptée : connexion au salon de votre ami…");
                return;
            }
            JoinSteamLobby(lobbyId);
        }

        /// <summary>Hôte d'un salon Steam : démarre la partie réseau (transport Steam) et charge l'atelier.</summary>
        public bool StartSteamGame()
        {
            if (InSession || Steam == null || !Steam.IsLobbyOwner) return false;
            EnsureManager();
            UsesSteamTransport = true;
            manager.NetworkConfig.NetworkTransport = steamTransport;
            return StartHost();
        }

        void LeaveLobbyOnly(string message)
        {
            Steam?.LeaveLobby();
            SetState(SessionState.Offline, message);
        }

        // ---------------------------------------------------------------- Départs et déconnexions

        /// <summary>Quitte la partie (et le salon) puis revient au menu principal.</summary>
        public void Leave(string message = null)
        {
            leaveRequested = true;
            bool wasHost = IsHost;
            SetState(SessionState.Leaving);
            if (InSession)
            {
                if (wasHost)
                {
                    foreach (var id in new List<ulong>(manager.ConnectedClientsIds))
                        if (id != NetworkManager.ServerClientId) manager.DisconnectClient(id, "L'hôte a fermé la partie.");
                }
                manager.Shutdown();
            }
            if (Steam != null && Steam.InLobby)
            {
                if (Steam.IsLobbyOwner)
                {
                    Steam.SetLobbyData(SteamPlatform.StateKey, "closed");
                    Steam.SetJoinable(false);
                }
                Steam.LeaveLobby();
            }
            names.Clear();
            LastEndedWithError = false;
            SetState(SessionState.Offline, message ?? SessionRules.DisconnectMessage(wasHost, true, null));
            ReturnToMenu();
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (manager == null) return;
            if (manager.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId)
                {
                    names.Remove(clientId);
                    Changed?.Invoke();
                }
                return;
            }
            // Invité : c'est sa propre connexion qui s'arrête (refus, hôte parti, réseau).
            if (leaveRequested) return;
            string reason = manager.DisconnectReason;
            EndWithError(SessionRules.DisconnectMessage(false, false, reason));
        }

        void OnTransportFailure()
        {
            if (leaveRequested) return;
            EndWithError("Erreur réseau : la session a été interrompue.");
        }

        void EndWithError(string message)
        {
            bool wasHost = startedAsHost;
            if (manager != null && (manager.IsServer || manager.IsClient)) manager.Shutdown();
            if (Steam != null && Steam.InLobby && !Steam.IsLobbyOwner) Steam.LeaveLobby();
            names.Clear();
            LastEndedWithError = true;
            SetState(SessionState.Offline, message ?? SessionRules.DisconnectMessage(wasHost, false, null));
            ReturnToMenu();
        }

        void Fail(string message)
        {
            if (InSession) manager.Shutdown();
            SetState(SessionState.Offline, message);
            LastEndedWithError = true;
        }

        /// <summary>Nouvelle tentative vers la dernière partie (après une déconnexion).</summary>
        public bool Reconnect()
        {
            if (InSession) return false;
            if (reconnectLobby != 0)
            {
                JoinSteamLobby(reconnectLobby);
                return true;
            }
            return !string.IsNullOrEmpty(reconnectAddress) && JoinDirect(reconnectAddress, reconnectPort);
        }

        void ReturnToMenu()
        {
            if (SceneManager.GetActiveScene().name != MainMenuScene) SceneManager.LoadScene(MainMenuScene);
            if (pendingLobbyJoin != 0)
            {
                ulong lobby = pendingLobbyJoin;
                pendingLobbyJoin = 0;
                JoinSteamLobby(lobby);
            }
        }

        void OnApplicationQuit()
        {
            leaveRequested = true;
            if (InSession) manager.Shutdown();
            Steam?.Dispose();
        }

        void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnClientDisconnectCallback -= OnClientDisconnect;
                manager.OnTransportFailure -= OnTransportFailure;
            }
        }
    }
}
