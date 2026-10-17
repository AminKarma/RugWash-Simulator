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
        // Hôte : identité stable de chaque joueur connecté (reconnexion après un crash : l'ancienne connexion est fermée).
        readonly Dictionary<ulong, string> playerKeys = new Dictionary<ulong, string>();
        // Grande mise à jour : joueurs expulsés de cette partie (par identité), partie ouverte ou fermée, réservée au Premium.
        readonly HashSet<string> kickedKeys = new HashSet<string>();
        readonly List<PendingPremium> pendingPremium = new List<PendingPremium>();
        Steamworks.Callback<Steamworks.ValidateAuthTicketResponse_t> authResponse;
        Steamworks.HAuthTicket localTicket = Steamworks.HAuthTicket.Invalid;

        /// <summary>Admission Premium en attente de la réponse de Steam (ticket validé, puis licence vérifiée).</summary>
        sealed class PendingPremium
        {
            public ulong ClientId;
            public ulong SteamId;
            public NetworkManager.ConnectionApprovalResponse Response;
            public float StartedAt;
            public string Name;
            public string Key;
        }

        /// <summary>Délai maximal de la vérification Premium par Steam avant refus (s).</summary>
        public const float PremiumTimeoutSeconds = 12f;

        /// <summary>Délai avant de considérer une connexion directe comme perdue (crash, câble) : 10 s au lieu de 30.</summary>
        public const int DisconnectTimeoutMS = 10000;

        /// <summary>Hôte : anciennes connexions fermées parce que le même joueur s'est reconnecté (diagnostic).</summary>
        public int StaleConnectionsEvicted { get; private set; }
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

        /// <summary>Hôte : la partie accepte de nouveaux joueurs (fermée : refus côté hôte, et salon Steam non rejoignable).</summary>
        public bool IsOpen { get; private set; } = true;
        /// <summary>Hôte : seuls les comptes Steam qui possèdent l'édition Premium sont admis (vérification par Steam).</summary>
        public bool PremiumOnly { get; private set; }
        /// <summary>
        /// La vérification Premium est possible : une édition Premium est déclarée (App ID du DLC dans le contenu réseau) et la
        /// partie passe par Steam. Sinon l'option reste indisponible, avec son explication (<see cref="PremiumUnavailableReason"/>).
        /// </summary>
        public bool PremiumAvailable => content != null && content.premiumAppId != 0 && Steam != null && Steam.IsInitialized;
        public string PremiumUnavailableReason =>
            content == null || content.premiumAppId == 0
                ? "Indisponible : aucune édition Premium (DLC Steam) n'est déclarée pour ce jeu. Le jeu utilise l'App ID de test 480 ; " +
                  "la vérification de propriété par Steam s'activera dès qu'un DLC Premium sera publié et renseigné (NetworkContent.premiumAppId)."
                : "Indisponible : Steam n'est pas lancé (la propriété ne peut être vérifiée que par Steam).";

        public bool InSession => manager != null && (manager.IsServer || manager.IsClient);
        public bool IsHost => manager != null && manager.IsServer;
        public bool IsGuest => manager != null && manager.IsClient && !manager.IsServer;
        public bool CanReconnect => !InSession && (reconnectLobby != 0 || !string.IsNullOrEmpty(reconnectAddress));

        /// <summary>État, message ou membres modifiés (pour l'interface).</summary>
        public event Action Changed;

        /// <summary>
        /// Invitation Steam acceptée pendant une partie solo : l'atelier solo doit être quitté proprement (commande en cours
        /// abandonnée, progression sauvegardée, retour au menu) ; la connexion au salon suit au chargement du menu.
        /// </summary>
        public event Action SoloExitRequested;
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

        void Update()
        {
            Steam?.RunCallbacks();
            ExpirePremiumChecks();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnUnitySceneLoaded;

        void OnDisable() => SceneManager.sceneLoaded -= OnUnitySceneLoaded;

        /// <summary>Menu principal chargé : rejoint le salon d'une invitation acceptée pendant le jeu.</summary>
        void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MainMenuScene || pendingLobbyJoin == 0) return;
            ulong lobby = pendingLobbyJoin;
            pendingLobbyJoin = 0;
            JoinSteamLobby(lobby);
        }

        // ---------------------------------------------------------------- NetworkManager

        void EnsureManager()
        {
            if (manager != null) return;
            var go = new GameObject("NetworkManager");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            manager = go.AddComponent<NetworkManager>();
            directTransport = go.AddComponent<UnityTransport>();
            // Crash d'un joueur : détecté en 10 s (30 s par défaut), son personnage n'attend pas plus longtemps.
            directTransport.DisconnectTimeoutMS = DisconnectTimeoutMS;
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
        static string GameVersion => Application.version;

        /// <summary>
        /// Identité stable de ce joueur : compte Steam si Steam est actif, sinon un identifiant tiré au hasard une fois par
        /// installation. « -rugwash-player-key » la remplace (tests à plusieurs processus sur une même machine).
        /// </summary>
        public string LocalPlayerKey
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-rugwash-player-key");
                if (index >= 0 && index + 1 < args.Length) return args[index + 1];
                if (Steam != null && Steam.IsInitialized) return "steam:" + Steam.LocalSteamId;
                const string Pref = "rugwash.playerKey";
                string key = PlayerPrefs.GetString(Pref, string.Empty);
                if (string.IsNullOrEmpty(key))
                {
                    key = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(Pref, key);
                    PlayerPrefs.Save();
                }
                return key;
            }
        }
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
            ConnectionPayload.TryDecode(request.Payload, out var announced);
            // Grande mise à jour : partie fermée, joueur expulsé (refus décidés ici, par l'hôte seul).
            string refusal = SessionRules.HostRefusal(IsOpen, kickedKeys, announced.PlayerKey);
            if (refusal != null)
            {
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Reason = refusal;
                Debug.Log($"[RugWash] Connexion refusée (client {request.ClientNetworkId}) : {refusal}");
                return;
            }
            bool stale = SessionRules.TryFindStaleConnection(playerKeys, announced.PlayerKey, request.ClientNetworkId, out ulong staleId);
            if (stale) connected--;
            bool ok = SessionRules.Approve(request.Payload, Version, GameVersion, connected, MaxPlayers, out string name, out string reason, out _);
            response.Approved = ok;
            response.CreatePlayerObject = false;
            response.Reason = reason;
            Debug.Log(ok ? $"[RugWash] Joueur admis (client {request.ClientNetworkId}){(stale ? $", ancienne connexion {staleId} fermée" : string.Empty)}."
                : $"[RugWash] Connexion refusée (client {request.ClientNetworkId}) : {reason}");
            if (!ok) return;
            if (PremiumOnly && !BeginPremiumCheck(request.ClientNetworkId, announced, name, response)) return;
            names[request.ClientNetworkId] = name;
            if (!string.IsNullOrEmpty(announced.PlayerKey)) playerKeys[request.ClientNetworkId] = announced.PlayerKey;
            // Le même joueur revient (crash, perte de réseau) alors que son ancienne connexion n'a pas encore expiré : elle est
            // fermée tout de suite, son personnage disparaît (aucun doublon). Sa progression n'est pas concernée (session).
            if (stale)
            {
                playerKeys.Remove(staleId);
                StaleConnectionsEvicted++;
                manager.DisconnectClient(staleId, "Reconnexion du même joueur : ancienne connexion fermée.");
            }
        }

        bool StartHost()
        {
            leaveRequested = false;
            startedAsHost = true;
            names.Clear();
            playerKeys.Clear();
            kickedKeys.Clear();
            pendingPremium.Clear();
            IsOpen = true;
            StaleConnectionsEvicted = 0;
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
            manager.NetworkConfig.ConnectionData = new ConnectionPayload(Version, LocalName, GameVersion, LocalPlayerKey, LocalAuthTicket()).Encode();
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
                Steam.JoinRequested += AcceptInvite;
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
            // Salon d'une autre version (invitation, ancienne liste) : refus clair avant toute connexion au jeu.
            string lobbyVersion = Steam.GetLobbyData(SteamPlatform.VersionKey);
            if (!string.IsNullOrEmpty(lobbyVersion) && lobbyVersion != Version)
            {
                LastEndedWithError = true;
                LeaveLobbyOnly(SessionRules.VersionMismatch(GameVersion, Version, null, lobbyVersion));
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

        /// <summary>
        /// Invitation Steam acceptée (ou « Rejoindre la partie » d'un ami) pendant que le jeu tourne : la partie en cours,
        /// en ligne ou solo, est quittée proprement, puis le salon est rejoint depuis le menu principal.
        /// </summary>
        public void AcceptInvite(ulong lobbyId)
        {
            if (lobbyId == 0) return;
            if (InSession)
            {
                pendingLobbyJoin = lobbyId;
                Leave("Invitation acceptée : connexion au salon de votre ami…");
                return;
            }
            if (SceneManager.GetActiveScene().name != MainMenuScene)
            {
                pendingLobbyJoin = lobbyId;
                Message = "Invitation acceptée : partie solo sauvegardée, connexion au salon…";
                Changed?.Invoke();
                if (SoloExitRequested != null) SoloExitRequested.Invoke();
                else SceneManager.LoadScene(MainMenuScene);
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

        // ---------------------------------------------------------------- Modération (grande mise à jour)

        /// <summary>
        /// Hôte : expulse un joueur (confirmation demandée par l'interface). Vérifié ici : seul l'hôte expulse, jamais lui-même,
        /// seulement un joueur connecté ; le joueur reçoit le motif et ne peut plus rejoindre cette partie (même identité).
        /// </summary>
        public bool Kick(ulong clientId)
        {
            if (!IsHost || clientId == NetworkManager.ServerClientId || !IsConnected(clientId)) return false;
            string name = NameOf(clientId);
            if (playerKeys.TryGetValue(clientId, out string key) && !string.IsNullOrEmpty(key)) kickedKeys.Add(key);
            manager.DisconnectClient(clientId, SessionRules.KickedReason);
            Message = $"{name} a été expulsé de la partie.";
            Changed?.Invoke();
            return true;
        }

        bool IsConnected(ulong clientId)
        {
            foreach (ulong id in manager.ConnectedClientsIds)
                if (id == clientId) return true;
            return false;
        }

        /// <summary>Hôte : ouvre ou ferme la partie aux nouveaux joueurs (refus côté hôte ; salon Steam non rejoignable).</summary>
        public bool SetOpen(bool open)
        {
            if (!IsHost) return false;
            IsOpen = open;
            if (UsesSteamTransport && Steam != null && Steam.IsLobbyOwner)
            {
                Steam.SetJoinable(open);
                Steam.SetLobbyData(SteamPlatform.LockKey, open ? "0" : "1");
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Hôte (avant de lancer la partie Steam, ou en cours) : réserve la partie aux possesseurs de l'édition Premium. Refusé
        /// (faux, avec explication) si la vérification n'est pas possible : jamais de vérification factice.
        /// </summary>
        public bool SetPremiumOnly(bool premium)
        {
            if (premium && !PremiumAvailable)
            {
                Message = PremiumUnavailableReason;
                PremiumOnly = false;
                Changed?.Invoke();
                return false;
            }
            PremiumOnly = premium;
            if (Steam != null && Steam.IsInitialized && Steam.InLobby && Steam.IsLobbyOwner) Steam.SetLobbyData(SteamPlatform.PremiumKey, premium ? "1" : "0");
            Changed?.Invoke();
            return true;
        }

        /// <summary>Ticket Steam joint à la connexion (l'hôte ne s'en sert que pour une partie Premium) ; vide sans Steam.</summary>
        byte[] LocalAuthTicket()
        {
            if (Steam == null || !Steam.IsInitialized) return null;
            if (localTicket != Steamworks.HAuthTicket.Invalid) Steamworks.SteamUser.CancelAuthTicket(localTicket);
            var ticket = new byte[ConnectionPayload.MaxTicketBytes];
            var identity = new Steamworks.SteamNetworkingIdentity();
            localTicket = Steamworks.SteamUser.GetAuthSessionTicket(ticket, ticket.Length, out uint size, ref identity);
            if (localTicket == Steamworks.HAuthTicket.Invalid || size == 0) return null;
            Array.Resize(ref ticket, (int)size);
            return ticket;
        }

        /// <summary>
        /// Partie Premium : l'admission attend Steam. Le ticket du joueur est validé par Steam (il prouve son identité), puis
        /// la licence du DLC Premium de ce compte est vérifiée ; un ticket absent, falsifié ou une licence manquante est refusé.
        /// </summary>
        bool BeginPremiumCheck(ulong clientId, ConnectionPayload announced, string name, NetworkManager.ConnectionApprovalResponse response)
        {
            ulong steamId = SessionRules.SteamIdOf(announced.PlayerKey);
            if (!PremiumAvailable || steamId == 0 || announced.AuthTicket.Length == 0)
            {
                response.Approved = false;
                response.Reason = steamId == 0 || announced.AuthTicket.Length == 0 ? SessionRules.PremiumUnverifiedReason : SessionRules.PremiumReason;
                return false;
            }
            authResponse ??= Steamworks.Callback<Steamworks.ValidateAuthTicketResponse_t>.Create(OnAuthTicketResponse);
            var begin = Steamworks.SteamUser.BeginAuthSession(announced.AuthTicket, announced.AuthTicket.Length, new Steamworks.CSteamID(steamId));
            if (begin != Steamworks.EBeginAuthSessionResult.k_EBeginAuthSessionResultOK)
            {
                response.Approved = false;
                response.Reason = SessionRules.PremiumUnverifiedReason;
                return false;
            }
            response.Approved = false;
            response.Pending = true;
            pendingPremium.Add(new PendingPremium
            {
                ClientId = clientId, SteamId = steamId, Response = response, StartedAt = Time.unscaledTime, Name = name, Key = announced.PlayerKey,
            });
            Debug.Log($"[RugWash] Vérification Premium du client {clientId} par Steam…");
            return false;
        }

        void OnAuthTicketResponse(Steamworks.ValidateAuthTicketResponse_t result)
        {
            for (int i = pendingPremium.Count - 1; i >= 0; i--)
            {
                var pending = pendingPremium[i];
                if (pending.SteamId != result.m_SteamID.m_SteamID) continue;
                pendingPremium.RemoveAt(i);
                bool valid = result.m_eAuthSessionResponse == Steamworks.EAuthSessionResponse.k_EAuthSessionResponseOK;
                bool licensed = valid && Steamworks.SteamUser.UserHasLicenseForApp(result.m_SteamID, new Steamworks.AppId_t(content.premiumAppId))
                    == Steamworks.EUserHasLicenseForAppResult.k_EUserHasLicenseResultHasLicense;
                pending.Response.Approved = licensed;
                pending.Response.CreatePlayerObject = false;
                pending.Response.Reason = licensed ? string.Empty : valid ? SessionRules.PremiumReason : SessionRules.PremiumUnverifiedReason;
                if (licensed)
                {
                    names[pending.ClientId] = pending.Name;
                    if (!string.IsNullOrEmpty(pending.Key)) playerKeys[pending.ClientId] = pending.Key;
                }
                else Steamworks.SteamUser.EndAuthSession(result.m_SteamID);
                pending.Response.Pending = false;
                Debug.Log($"[RugWash] Vérification Premium du client {pending.ClientId} : {(licensed ? "licence confirmée" : pending.Response.Reason)}");
            }
        }

        void ExpirePremiumChecks()
        {
            for (int i = pendingPremium.Count - 1; i >= 0; i--)
            {
                var pending = pendingPremium[i];
                if (Time.unscaledTime - pending.StartedAt < PremiumTimeoutSeconds) continue;
                pendingPremium.RemoveAt(i);
                Steamworks.SteamUser.EndAuthSession(new Steamworks.CSteamID(pending.SteamId));
                pending.Response.Approved = false;
                pending.Response.Reason = SessionRules.PremiumUnverifiedReason;
                pending.Response.Pending = false;
            }
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
                    if (playerKeys.TryGetValue(clientId, out string key) && PremiumOnly && Steam != null && Steam.IsInitialized)
                    {
                        ulong steamId = SessionRules.SteamIdOf(key);
                        if (steamId != 0) Steamworks.SteamUser.EndAuthSession(new Steamworks.CSteamID(steamId));
                    }
                    playerKeys.Remove(clientId);
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
            // Une invitation en attente est traitée une fois le menu chargé (OnUnitySceneLoaded).
            if (SceneManager.GetActiveScene().name != MainMenuScene)
            {
                SceneManager.LoadScene(MainMenuScene);
                return;
            }
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
            if (localTicket != Steamworks.HAuthTicket.Invalid && Steam != null && Steam.IsInitialized) Steamworks.SteamUser.CancelAuthTicket(localTicket);
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
