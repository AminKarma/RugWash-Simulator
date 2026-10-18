using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.Workshop;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>État du robot diffusé par l'hôte.</summary>
    public struct RobotNetState : INetworkSerializable, IEquatable<RobotNetState>
    {
        public byte Mode;
        public byte Battery;
        public bool Resume;
        public float X;
        public float Z;
        public float Yaw;
        /// <summary>Robot acheté par l'hôte (sinon caché chez tous).</summary>
        public bool Owned;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Owned);
            serializer.SerializeValue(ref Mode);
            serializer.SerializeValue(ref Battery);
            serializer.SerializeValue(ref Resume);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Z);
            serializer.SerializeValue(ref Yaw);
        }

        public bool Equals(RobotNetState other) =>
            Owned == other.Owned && Mode == other.Mode && Battery == other.Battery && Resume == other.Resume &&
            Mathf.Abs(X - other.X) < 0.005f && Mathf.Abs(Z - other.Z) < 0.005f && Mathf.Abs(Mathf.DeltaAngle(Yaw, other.Yaw)) < 0.5f;
    }

    /// <summary>Atelier de l'hôte vu par les invités (v0.1.2) : palier de décor, machines achetées et en marche.</summary>
    public struct WorkshopNetState : INetworkSerializable, IEquatable<WorkshopNetState>
    {
        public byte Tier;
        public byte MachinesOwned;
        public byte MachinesRunning;
        /// <summary>Grande mise à jour : agrandissement (bit 0), machine à laver (bit 1), programme rapide (bit 2).</summary>
        public byte Facilities;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tier);
            serializer.SerializeValue(ref MachinesOwned);
            serializer.SerializeValue(ref MachinesRunning);
            serializer.SerializeValue(ref Facilities);
        }

        public bool Equals(WorkshopNetState other) => Tier == other.Tier && MachinesOwned == other.MachinesOwned && MachinesRunning == other.MachinesRunning
                                                      && Facilities == other.Facilities;
    }

    /// <summary>
    /// Atelier partagé : objet réseau créé par l'hôte une fois l'atelier chargé.
    /// <list type="bullet">
    /// <item>État de session partagé (l'hôte fait autorité) : surfaces (tapis, aire de lavage, sol), commande active et
    /// tableau des commandes, robot. Les invités envoient leurs opérations de nettoyage et leurs demandes, et reçoivent
    /// l'état (voir <see cref="SurfaceSyncHost"/> / <see cref="SurfaceSyncClient"/>).</item>
    /// <item>Sauvegarde de session (V0.2.5) : l'emplacement multijoueur choisi par l'hôte, et lui seul. Argent,
    /// réputation, atelier, produits et stock de détergent y sont communs à tous ; chaque joueur y a sa fiche (outils,
    /// améliorations d'outils, équipement, gadgets, objets portés), retrouvée par son identité à chaque arrivée. L'hôte
    /// applique tous les achats et toutes les dépenses ; un invité ne fait que demander, et rien n'est écrit chez lui.</item>
    /// <item>Livraison : évaluée et payée une seule fois par l'hôte, dans l'argent et la réputation de la session.</item>
    /// <item>Arrivée : l'invité reste en « synchronisation » (contrôles coupés, HUD en chargement) jusqu'à réception de
    /// toutes les surfaces et de sa vue de la session (argent, réputation, ses outils).</item>
    /// </list>
    /// </summary>
    public sealed partial class NetworkWorkshop : NetworkBehaviour, IWorkshopRequests
    {
        public const string OperationMessage = "rugwash.op";
        public const string SurfaceMessageName = "rugwash.surface";
        public const byte RugId = 0;
        public const byte WorkSurfaceId = 1;
        public const byte FloorId = 2;
        /// <summary>Zone de lavage 2 (agrandissement de l'atelier) : tapis et aire de lavage.</summary>
        public const byte Rug2Id = 3;
        public const byte WorkSurface2Id = 4;

        /// <summary>Réputation de l'hôte : elle déverrouille les commandes du tableau de la session (pas la carrière des invités).</summary>
        readonly NetworkVariable<int> boardReputation = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString512Bytes> completedOrders = new NetworkVariable<FixedString512Bytes>();
        readonly NetworkVariable<FixedString64Bytes> activeOrder = new NetworkVariable<FixedString64Bytes>();
        /// <summary>Commande de la zone de lavage 2 (agrandissement).</summary>
        readonly NetworkVariable<FixedString64Bytes> activeOrder2 = new NetworkVariable<FixedString64Bytes>();
        /// <summary>Missions générées proposées sur le tableau de l'hôte (identifiants : les invités les régénèrent à l'identique).</summary>
        readonly NetworkVariable<FixedString512Bytes> missionOffers = new NetworkVariable<FixedString512Bytes>();
        readonly NetworkVariable<float> elapsed = new NetworkVariable<float>();
        readonly NetworkVariable<float> elapsed2 = new NetworkVariable<float>();
        readonly NetworkVariable<RobotNetState> robotState = new NetworkVariable<RobotNetState>();
        /// <summary>
        /// V0.2.5 : numéro de la dernière commande acceptée (hôte). Chaque nouveau tapis fait revenir tous les joueurs à
        /// l'atelier, même ceux restés sur le résumé du précédent ; une livraison reçue après coup n'ouvre plus de résumé.
        /// </summary>
        readonly NetworkVariable<int> orderEpoch = new NetworkVariable<int>();
        readonly NetworkVariable<WorkshopNetState> workshopState = new NetworkVariable<WorkshopNetState>();

        GameBootstrap game;
        MultiplayerSession session;
        SurfaceSyncHost host;
        SurfaceSyncClient client;
        readonly ByteWriter writer = new ByteWriter(4096);
        readonly List<ulong> syncedClients = new List<ulong>();
        readonly HashSet<ulong> spawnedAvatars = new HashSet<ulong>();
        byte[] receiveBuffer = new byte[1024];
        float sendTimer, robotTimer, slowTimer, agingSyncTimer;
        float sendInterval = 1f / 15f;
        /// <summary>
        /// Cellules de correction lente envoyées au plus par envoi, toutes surfaces confondues (~4 000 : moins d'1 ms de
        /// compression ; tapis, aire de lavage et sol entièrement couverts sont corrigés en ~1 s, avant la correction suivante).
        /// </summary>
        public const int SlowCorrectionCellsPerSend = 4096;
        float agingSyncInterval = 2f;

        // Sauvegarde de session (V0.2.5) : fiche de chaque invité présent (ses achats personnels, ce qu'il porte), tirée de
        // l'emplacement de l'hôte à son arrivée et réécrite dedans. Vues à renvoyer (argent, réputation, atelier changés).
        readonly Dictionary<ulong, SessionMember> members = new Dictionary<ulong, SessionMember>();
        readonly HashSet<ulong> viewDirty = new HashSet<ulong>();
        readonly List<ulong> viewFlush = new List<ulong>();
        bool memberSaveDirty;
        float memberSaveTimer;
        // Invité : vue de la session reçue (fin de la synchronisation d'arrivée) ; demande répétée tant qu'elle manque.
        bool careerReceived;
        float helloTimer, readyTimer;
        bool readyWarned;
        const float HelloRetrySeconds = 3f;
        // Invité : inventaire et réserve de détergent signalés à l'hôte (ils sont enregistrés dans sa sauvegarde).
        bool inventoryDirty;
        float inventoryTimer;
        float lastReportedSoap = -2f;

        public static NetworkWorkshop Instance { get; private set; }
        public SurfaceSyncHost HostSync => host;
        public SurfaceSyncClient ClientSync => client;
        public GameBootstrap Game => game;
        /// <summary>Octets de cellules envoyés par l'hôte depuis le début (mesure de la bande passante).</summary>
        public long SurfaceBytesSent { get; private set; }
        public long OperationBytesSent { get; private set; }
        public int RemoteDeliveries { get; private set; }
        /// <summary>Invité : l'état de l'hôte est entièrement reçu (surfaces, carrière validée).</summary>
        public bool GuestReady { get; private set; }
        /// <summary>Invité : la synchronisation d'arrivée a eu lieu (état de chargement affiché).</summary>
        public bool ShowedLoading { get; private set; }

        /// <summary>Hôte : la session telle que cet invité la voit (argent et réputation communs, ses outils), ou null.</summary>
        public PlayerCareer CareerOf(ulong clientId) => members.TryGetValue(clientId, out var member) ? ViewFor(member) : null;
        /// <summary>Hôte : fiche de cet invité dans la sauvegarde de la session (copie), ou null.</summary>
        public Save.SessionMemberData MemberOf(ulong clientId) => members.TryGetValue(clientId, out var member) ? Snapshot(member) : null;
        /// <summary>Invité : vue de la session reçue au moins une fois.</summary>
        public bool CareerReceived => careerReceived;

        // ---------------------------------------------------------------- Apparition

        public override void OnNetworkSpawn()
        {
            Instance = this;
            game = FindAnyObjectByType<GameBootstrap>();
            session = MultiplayerSession.Find();
            if (game == null || !game.IsReady)
            {
                Debug.LogError("[RugWash] Atelier réseau : scène de l'atelier introuvable ou incomplète.");
                return;
            }
            if (session != null && session.Content != null)
            {
                sendInterval = 1f / Mathf.Max(5f, session.Content.surfaceSendRate);
                agingSyncInterval = Mathf.Max(0.5f, session.Content.slowSurfaceSyncSeconds);
            }
            game.GameState.Networked = true;
            // L'objet réseau peut apparaître avant le Start de la scène : la sauvegarde du joueur (sa carrière) doit déjà
            // être appliquée, sinon un invité annoncerait une carrière vide.
            game.SaveSystem.EnsureApplied();

            if (IsServer) SetUpHost();
            else SetUpGuest();
            session?.MarkInGame();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (game != null) TearDownVoice();
            if (game != null) TearDownItems();
            var messaging = NetworkManager != null ? NetworkManager.CustomMessagingManager : null;
            if (IsServer)
            {
                messaging?.UnregisterNamedMessageHandler(OperationMessage);
                if (NetworkManager != null && NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
                if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
                // Fin de la partie : les fiches des joueurs encore là sont écrites dans la sauvegarde de la session.
                if (game != null && members.Count > 0)
                {
                    foreach (var member in members.Values) StoreMember(member);
                    game.SaveSystem.SaveNow("fin de la partie en ligne");
                }
                UnhookHostSystems();
                TearDownPranks();
                host?.Unregister();
            }
            else
            {
                messaging?.UnregisterNamedMessageHandler(SurfaceMessageName);
                client?.Unregister();
                if (game != null)
                {
                    // Fin de partie pour l'invité : rien à écrire chez lui (la partie appartient à la sauvegarde de l'hôte).
                    if (game.ToolController != null && game.ToolController.Inventory != null) game.ToolController.Inventory.Changed -= OnGuestInventoryChanged;
                    game.Orders.Remote = null;
                    game.Shop.Remote = null;
                    game.Robot.Remote = null;
                    game.Detergent.Remote = null;
                    game.WorkshopFromHost = false;
                    foreach (var machine in game.Machines)
                        if (machine != null) machine.Remote = null;
                    if (Washer != null) Washer.Remote = null;
                    TearDownPranks();
                    game.Orders.SetBoardReputation(-1);
                    game.GameState.Synchronizing = false;
                    game.Hud.SetLoading(null);
                }
            }
            if (game != null) game.GameState.Networked = false;
        }

        RugCleaningController Surface(byte id)
        {
            switch (id)
            {
                case RugId: return game.Rug;
                case WorkSurfaceId: return game.WorkSurface.Surface;
                case FloorId: return game.RoomFloor;
                case Rug2Id: return game.Orders.RugOf(1);
                case WorkSurface2Id:
                    var surface = game.Orders.WorkSurfaceOf(1);
                    return surface != null ? surface.Surface : null;
                default: return null;
            }
        }

        /// <summary>Surfaces partagées présentes dans cette scène (la zone 2 n'existe que si l'atelier peut s'agrandir).</summary>
        IEnumerable<byte> SharedSurfaces()
        {
            foreach (byte id in new[] { RugId, WorkSurfaceId, FloorId, Rug2Id, WorkSurface2Id })
                if (Surface(id) != null) yield return id;
        }

        static int BayOfSurface(byte id) => id == RugId ? 0 : id == Rug2Id ? 1 : -1;

        // ---------------------------------------------------------------- Hôte

        void SetUpHost()
        {
            host = new SurfaceSyncHost(Identify);
            foreach (byte id in SharedSurfaces()) host.Register(id, Surface(id));
            NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(OperationMessage, OnOperationMessage);
            NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronized;
            NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            HookHostSystems();
            SetUpHostPranks();
            SetUpItems();
            SetUpVoice();
            boardReputation.Value = game.Reputation.Points;
            PushOrders();
            SpawnAvatar(NetworkManager.LocalClientId);
            PushHostGolden();
            // Invités déjà connectés (arrivés pendant le chargement de l'atelier).
            foreach (var id in NetworkManager.ConnectedClientsIds)
                if (id != NetworkManager.LocalClientId) OnClientSynchronized(id);
        }

        SurfaceIdentity Identify(byte id)
        {
            var surface = Surface(id);
            string definition = surface != null && surface.Definition != null ? surface.Definition.id : string.Empty;
            var active = game.Orders.OrderOf(BayOfSurface(id));
            return new SurfaceIdentity(definition, active != null ? active.id : string.Empty);
        }

        void HookHostSystems()
        {
            game.Reputation.Changed += OnReputationChanged;
            game.Orders.Changed += PushOrders;
            game.Orders.OrderFinished += OnHostOrderFinished;
            game.Orders.OrderStarted += OnHostOrderStarted;
            game.Upgrades.Changed += PushHostGolden;
            // Économie de la session (V0.2.5) : tout changement est renvoyé aux invités avec leur vue.
            game.Upgrades.Changed += MarkViewsDirty;
            game.Wallet.Wallet.Changed += OnSessionMoneyChanged;
            game.Products.Book.Changed += MarkViewsDirty;
            game.Detergent.Changed += MarkViewsDirty;
            HookWasher();
        }

        /// <summary>Hôte : ses propres outils dorés, sur sa silhouette.</summary>
        void PushHostGolden()
        {
            var avatar = AvatarOf(NetworkManager.LocalClientId);
            if (avatar != null) avatar.SetGoldenTools(game.Upgrades.GoldenMaskFor(game.Upgrades.OwnedIds));
        }

        void UnhookHostSystems()
        {
            if (game == null) return;
            game.Reputation.Changed -= OnReputationChanged;
            game.Orders.Changed -= PushOrders;
            game.Orders.OrderFinished -= OnHostOrderFinished;
            game.Orders.OrderStarted -= OnHostOrderStarted;
            game.Upgrades.Changed -= PushHostGolden;
            game.Upgrades.Changed -= MarkViewsDirty;
            if (game.Wallet != null && game.Wallet.Wallet != null) game.Wallet.Wallet.Changed -= OnSessionMoneyChanged;
            if (game.Products != null) game.Products.Book.Changed -= MarkViewsDirty;
            if (game.Detergent != null) game.Detergent.Changed -= MarkViewsDirty;
            UnhookWasher();
        }

        /// <summary>Hôte : nouveau tapis accepté (par n'importe quel joueur) → transition de session diffusée à tous.</summary>
        void OnHostOrderStarted(OrderDefinition order) => orderEpoch.Value++;

        void OnReputationChanged(int points)
        {
            boardReputation.Value = points;
            MarkViewsDirty();
        }

        void PushOrders()
        {
            var orders = game.Orders;
            var first = orders.OrderOf(0);
            var second = orders.OrderOf(1);
            activeOrder.Value = new FixedString64Bytes(first != null ? Truncate(first.id, 60) : string.Empty);
            activeOrder2.Value = new FixedString64Bytes(second != null ? Truncate(second.id, 60) : string.Empty);
            completedOrders.Value = new FixedString512Bytes(Truncate(string.Join(",", orders.Book.CompletedIds()), 500));
            var offers = new List<string>();
            foreach (var offer in orders.Missions.Offers) offers.Add(offer.Id);
            missionOffers.Value = new FixedString512Bytes(Truncate(string.Join(",", offers), 500));
            elapsed.Value = orders.ElapsedOf(0);
            elapsed2.Value = orders.ElapsedOf(1);
            PushWasher();
        }

        static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);

        void OnClientSynchronized(ulong clientId)
        {
            if (!IsServer || clientId == NetworkManager.LocalClientId) return;
            if (spawnedAvatars.Add(clientId))
            {
                SpawnAvatar(clientId);
                ToastRpc($"{session?.NameOf(clientId) ?? "Un joueur"} a rejoint l'atelier.");
            }
            SendFullState(clientId);
        }

        /// <summary>
        /// État complet pour un invité : instantané de chaque surface, puis sa carrière (si déjà annoncée) ; ensuite, il reçoit
        /// les mises à jour comme les autres. Sans effet de bord si on le renvoie (mêmes valeurs simplement réécrites).
        /// </summary>
        void SendFullState(ulong clientId)
        {
            for (int i = 0; i < host.SurfaceCount; i++)
            {
                writer.Reset();
                host.WriteSnapshot(i, writer);
                SendSurface(writer, clientId);
            }
            if (!syncedClients.Contains(clientId)) syncedClients.Add(clientId);
            // Arrivée annoncée avant la fin de la synchronisation : la vue de la session part maintenant.
            if (members.ContainsKey(clientId)) SendView(clientId, inventory: true);
        }

        /// <summary>
        /// Invité prêt à recevoir les surfaces (gestionnaire de messages enregistré) : l'état complet lui est (r)envoyé. Couvre
        /// l'invité connecté pendant le chargement de l'atelier de l'hôte, à qui les premiers instantanés ont pu partir trop tôt.
        /// </summary>
        [Rpc(SendTo.Server)]
        void StateRequestRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (host == null || sender == NetworkManager.LocalClientId) return;
            SendFullState(sender);
        }

        void OnClientLeft(ulong clientId)
        {
            if (!IsServer || clientId == NetworkManager.LocalClientId) return;
            syncedClients.Remove(clientId);
            spawnedAvatars.Remove(clientId);
            host?.Acks.Remove(clientId);
            // Sa fiche reste dans la sauvegarde de la session : il la retrouvera à son retour (reconnexion comprise).
            if (members.TryGetValue(clientId, out var leaving))
            {
                StoreMember(leaving);
                members.Remove(clientId);
                game.SaveSystem.SaveNow("départ d'un joueur");
            }
            viewDirty.Remove(clientId);
            ForgetSocial(clientId);
            if (game != null && game.Pranks != null) game.Pranks.Forget(clientId);
            ToastRpc($"{session?.NameOf(clientId) ?? "Un joueur"} a quitté l'atelier.");
        }

        void SpawnAvatar(ulong clientId)
        {
            if (session == null || session.Content == null || session.Content.avatarPrefab == null) return;
            var instance = Instantiate(session.Content.avatarPrefab);
            var avatar = instance.GetComponent<NetworkPlayerAvatar>();
            avatar.SetInitialName(session.NameOf(clientId));
            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
        }

        void OnOperationMessage(ulong senderClientId, FastBufferReader reader)
        {
            int length = reader.Length - reader.Position;
            if (length <= 0 || length > 4096) return;
            if (receiveBuffer.Length < length) receiveBuffer = new byte[Mathf.Max(length, receiveBuffer.Length * 2)];
            reader.ReadBytesSafe(ref receiveBuffer, length);
            if (!host.TryApply(senderClientId, new ArraySegment<byte>(receiveBuffer, 0, length), out var applied, ProductFilter)) return;
            // Produit spécialisé pulvérisé par un invité : prélevé sur le stock de la session (celui de la sauvegarde de l'hôte).
            if (applied.Product != ProductTable.Standard)
            {
                var product = game.Rug.Products.Get(applied.Product);
                if (product != null) game.Products.Book.ConsumeFrom(product, product.litersPerSecond * applied.DeltaTime);
            }
        }

        /// <summary>Un produit spécialisé n'est utilisable que s'il en reste dans le stock de la session ; sinon, détergent standard.</summary>
        byte ProductFilter(byte id)
        {
            if (id == ProductTable.Standard) return id;
            var product = game.Rug.Products.Get(id);
            return product != null && game.Products.StockOf(product) > 0f ? id : ProductTable.Standard;
        }

        // ---------------------------------------------------------------- Joueurs de la session (hôte, V0.2.5)

        /// <summary>Fiche d'un invité présent : celle de la sauvegarde de la session, tenue par l'hôte pendant la partie.</summary>
        sealed class SessionMember
        {
            public Save.SessionMemberData Data;
            public readonly HashSet<string> Personal = new HashSet<string>();
        }

        /// <summary>
        /// Fiche d'un invité qui arrive : retrouvée dans la sauvegarde de la session par son identité de joueur (outils et
        /// améliorations achetés lors de ses venues précédentes), ou nouvelle (kit de départ). Rien n'est lu chez l'invité.
        /// </summary>
        SessionMember LoadMember(ulong clientId)
        {
            string key = session != null ? session.KeyOf(clientId) : string.Empty;
            if (string.IsNullOrEmpty(key)) key = $"client:{clientId}";
            var data = game.SaveSystem.FindMember(key) ?? new Save.SessionMemberData();
            data.key = key;
            data.name = session?.NameOf(clientId) ?? data.name;
            data.upgrades = SessionEconomyRules.SanitizePersonal(data.upgrades, game.Upgrades.Catalog);
            var member = new SessionMember { Data = data };
            member.Personal.UnionWith(data.upgrades);
            return member;
        }

        void StoreMember(SessionMember member)
        {
            member.Data.upgrades = new List<string>(member.Personal);
            game.SaveSystem.StoreMember(member.Data);
        }

        static Save.SessionMemberData Snapshot(SessionMember member)
        {
            var copy = member.Data.Clone();
            copy.upgrades = new List<string>(member.Personal);
            return copy;
        }

        /// <summary>Achats vus par cet invité : l'atelier de la session et ses achats personnels.</summary>
        HashSet<string> OwnedBy(SessionMember member) =>
            SessionEconomyRules.GuestOwned(game.Upgrades.OwnedIds, member.Personal, game.Upgrades.Catalog);

        /// <summary>Vue de la session pour un invité : argent, réputation, atelier, produits et stock communs ; ses outils et ce qu'il porte.</summary>
        PlayerCareer ViewFor(SessionMember member)
        {
            var view = new PlayerCareer
            {
                Money = game.Wallet.Balance, Reputation = game.Reputation.Points, DetergentStock = game.Detergent.Stock,
                SoapReserve = member.Data.soapReserve,
            };
            view.Upgrades.UnionWith(OwnedBy(member));
            foreach (var entry in game.Products.Snapshot()) view.Products[entry.Key] = entry.Value;
            view.ActiveProduct = game.Products.Active != null ? game.Products.Active.id : string.Empty;
            view.Carried.AddRange(member.Data.carried);
            return view;
        }

        /// <summary>Envoie sa vue de la session à un invité ; <paramref name="inventory"/> : ses objets portés sont imposés (arrivée, objet perdu ou revendu).</summary>
        void SendView(ulong clientId, bool inventory)
        {
            if (!members.TryGetValue(clientId, out var member)) return;
            viewDirty.Remove(clientId);
            // Outils dorés de cet invité, d'après SES achats (validés ici) : visibles par tous sur sa silhouette.
            var avatar = AvatarOf(clientId);
            if (avatar != null) avatar.SetGoldenTools(game.Upgrades.GoldenMaskFor(OwnedBy(member)));
            CareerRpc(CareerCodec.Encode(ViewFor(member)), inventory, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>Argent, réputation, atelier, produits ou stock de la session changés : chaque invité reçoit sa vue (groupé).</summary>
        void MarkViewsDirty()
        {
            foreach (ulong id in members.Keys) viewDirty.Add(id);
        }

        void OnSessionMoneyChanged(int previous, int current) => MarkViewsDirty();

        /// <summary>
        /// Arrivée d'un invité : sa fiche est chargée depuis la sauvegarde de la session (une seule fois par connexion). Une
        /// demande répétée (réponse perdue) renvoie seulement la vue actuelle ; un invité n'annonce jamais de progression.
        /// </summary>
        [Rpc(SendTo.Server)]
        void SessionHelloRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender == NetworkManager.LocalClientId) return;
            if (!members.ContainsKey(sender))
            {
                var member = LoadMember(sender);
                members[sender] = member;
                Debug.Log($"[RugWash] Joueur {sender} ({member.Data.key}) : {member.Personal.Count} achat(s) personnel(s) dans la sauvegarde de la session.");
            }
            // Réponse seulement à un invité synchronisé (sinon à la fin de sa synchronisation, OnClientSynchronized).
            if (syncedClients.Contains(sender)) SendView(sender, inventory: true);
        }

        /// <summary>Objets que cet invité peut porter : outils du kit de départ, outils et gadgets qu'il a achetés.</summary>
        List<string> CarriableBy(SessionMember member)
        {
            var owned = OwnedBy(member);
            var items = new List<string>();
            foreach (var tool in game.ToolController.Tools)
                if (tool != null && tool.Definition != null && (!tool.Definition.requiresUnlock || OwnsUnlock(owned, tool.Definition.id)))
                    items.Add(tool.Definition.id);
            var tuning = game.Pranks != null ? game.Pranks.Tuning : null;
            if (tuning != null)
                foreach (var gadget in Pranks.PrankTuning.All)
                {
                    var settings = tuning.Get(gadget);
                    string id = Player.InventoryRules.GadgetId(gadget);
                    if (id != null && settings != null && owned.Contains(settings.upgradeId)) items.Add(id);
                }
            return items;
        }

        bool OwnsUnlock(ICollection<string> owned, string toolId)
        {
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade != null && upgrade.effect == UpgradeEffect.UnlockTool && upgrade.targetToolId == toolId && owned.Contains(upgrade.id)) return true;
            return false;
        }

        bool IsToolId(string id)
        {
            foreach (var tool in game.ToolController.Tools)
                if (tool != null && tool.Definition != null && tool.Definition.id == id) return true;
            return false;
        }

        /// <summary>
        /// Ce que porte un invité (et sa réserve de détergent), enregistré dans sa fiche : seulement des objets qu'il possède,
        /// au plus quatre (vérifié ici, comme au chargement d'une sauvegarde).
        /// </summary>
        [Rpc(SendTo.Server)]
        void InventoryRpc(FixedString512Bytes carried, float soapReserve, RpcParams rpcParams = default)
        {
            if (!members.TryGetValue(rpcParams.Receive.SenderClientId, out var member)) return;
            member.Data.carried = Player.InventoryRules.Normalize(Split(carried.ToString()), CarriableBy(member), IsToolId, Player.PlayerInventory.Capacity);
            member.Data.soapReserve = float.IsNaN(soapReserve) || float.IsInfinity(soapReserve) ? -1f : Mathf.Clamp(soapReserve, 0f, MaxReserveLiters);
            memberSaveDirty = true;
        }

        void Update()
        {
            if (game == null || !IsSpawned) return;
            if (IsServer && host != null) HostTick();
            else if (!IsServer) GuestTick();
        }

        void HostTick()
        {
            float dt = Time.unscaledDeltaTime;
            sendTimer += dt;
            agingSyncTimer += dt;
            if (sendTimer >= sendInterval)
            {
                sendTimer = 0f;
                // Simulation lente : reproduite par les invités, corrigée seulement à basse fréquence, et par tranches de
                // lignes réparties sur les envois suivants (mission de corrections : plus d'à-coup toutes les 2 s).
                bool includeSlow = agingSyncTimer >= agingSyncInterval;
                if (includeSlow) agingSyncTimer = 0f;
                int slowBudget = SlowCorrectionCellsPerSend;
                for (int i = 0; i < host.SurfaceCount; i++)
                {
                    writer.Reset();
                    long timer = Diagnostics.PerfStats.Begin();
                    bool changed = host.TryWriteUpdate(i, writer, includeSlow: false);
                    Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.NetWrite, timer, writer.Length);
                    if (changed && syncedClients.Count > 0) SendSurface(writer, syncedClients);

                    if (includeSlow) host.BeginSlowCorrection(i);
                    if (slowBudget <= 0) continue;
                    writer.Reset();
                    timer = Diagnostics.PerfStats.Begin();
                    int written = host.TryWriteSlowChunk(i, writer, slowBudget);
                    Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.NetWrite, timer, writer.Length);
                    if (written <= 0) continue;
                    slowBudget -= written;
                    if (syncedClients.Count > 0) SendSurface(writer, syncedClients);
                }
            }

            robotTimer += dt;
            if (robotTimer >= 0.1f)
            {
                robotTimer = 0f;
                var state = game.Robot.CaptureState();
                var net = new RobotNetState
                {
                    Mode = (byte)state.mode, Battery = (byte)Mathf.RoundToInt(Mathf.Clamp01(state.battery) * 255f), Resume = state.resumeAfterCharge,
                    X = state.x, Z = state.z, Yaw = state.yaw, Owned = game.Robot.IsOwned,
                };
                if (!net.Equals(robotState.Value)) robotState.Value = net;
                var workshop = CaptureWorkshop();
                if (!workshop.Equals(workshopState.Value)) workshopState.Value = workshop;
            }

            slowTimer += dt;
            if (slowTimer >= 0.5f)
            {
                slowTimer = 0f;
                if (game.Orders.OrderOf(0) != null) elapsed.Value = game.Orders.ElapsedOf(0);
                if (game.Orders.OrderOf(1) != null) elapsed2.Value = game.Orders.ElapsedOf(1);
                PushWasher();
                // Session modifiée (argent, réputation, atelier, produits, stock) : chaque invité reçoit sa vue, groupée.
                viewFlush.Clear();
                viewFlush.AddRange(viewDirty);
                foreach (ulong id in viewFlush)
                    if (syncedClients.Contains(id)) SendView(id, inventory: false);
                // Objets portés des invités : écrits dans la sauvegarde de la session au plus toutes les 5 s.
                memberSaveTimer += 0.5f;
                if (memberSaveDirty && memberSaveTimer >= 5f)
                {
                    memberSaveTimer = 0f;
                    memberSaveDirty = false;
                    foreach (var member in members.Values) StoreMember(member);
                    game.SaveSystem.SaveNow("objets des joueurs");
                }
            }
        }

        void SendSurface(ByteWriter data, ulong clientId)
        {
            using var buffer = new FastBufferWriter(data.Length, Allocator.Temp);
            buffer.WriteBytesSafe(data.Buffer, data.Length);
            NetworkManager.CustomMessagingManager.SendNamedMessage(SurfaceMessageName, clientId, buffer, NetworkDelivery.ReliableFragmentedSequenced);
            SurfaceBytesSent += data.Length;
        }

        void SendSurface(ByteWriter data, IReadOnlyList<ulong> clientIds)
        {
            using var buffer = new FastBufferWriter(data.Length, Allocator.Temp);
            buffer.WriteBytesSafe(data.Buffer, data.Length);
            NetworkManager.CustomMessagingManager.SendNamedMessage(SurfaceMessageName, clientIds, buffer, NetworkDelivery.ReliableFragmentedSequenced);
            SurfaceBytesSent += (long)data.Length * clientIds.Count;
        }

        void OnHostOrderFinished(OrderResult result)
        {
            // Les mesures exactes de l'hôte accompagnent la livraison : les invités affichent le même résultat,
            // sans rien payer eux-mêmes.
            // Zone de la commande livrée (grande mise à jour : plusieurs zones) ; son tapis reste posé jusqu'au retour.
            var delivered = game.Orders.RugOf(result.Bay);
            if (delivered == null || delivered.Grid == null) return;
            var summary = delivered.Grid.ComputeSummary();
            // La commande livrée vient du résultat : une mission générée a déjà quitté le tableau (remplacée).
            var order = result.Order != null ? result.Order : FindLastCompleted();
            if (order == null) return;
            DeliveredRpc(new FixedString64Bytes(order.id), summary.Cleanliness01, summary.CleanCellRatio01, summary.AverageMoisture01,
                summary.AverageFoam01, summary.AverageResidue01, summary.AverageWear01, summary.RemainingDirt, summary.InitialDirt,
                result.ElapsedSeconds, (byte)Mathf.Clamp(result.Bay, 0, 255), orderEpoch.Value);
            // V0.2.5 : la récompense est versée une seule fois, dans l'argent et la réputation de la session (déjà fait par
            // la livraison) ; chaque invité reçoit le nouveau solde avec sa vue.
            MarkViewsDirty();
            PushOrders();
        }

        OrderDefinition FindLastCompleted()
        {
            OrderDefinition last = null;
            foreach (var order in game.Orders.Book.Orders)
                if (game.Orders.Book.StateOf(order) == OrderState.Completed) last = order;
            return last;
        }

        // ---------------------------------------------------------------- Invité

        void SetUpGuest()
        {
            client = new SurfaceSyncClient(SendOperation, LoadSurface) { LocalClientId = NetworkManager.LocalClientId };
            foreach (byte id in SharedSurfaces()) client.Register(id, Surface(id));
            NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(SurfaceMessageName, OnSurfaceMessage);

            game.Orders.Remote = this;
            game.Shop.Remote = this;
            game.Robot.Remote = this;
            game.Detergent.Remote = this;
            // V0.2.5 : la partie est celle de la sauvegarde de l'hôte ; aucun emplacement de ce joueur n'est écrit.
            game.SaveSystem.SessionGuest = true;
            if (game.ToolController.Inventory != null) game.ToolController.Inventory.Changed += OnGuestInventoryChanged;

            boardReputation.OnValueChanged += (_, value) => game.Orders.SetBoardReputation(value);
            completedOrders.OnValueChanged += (_, value) => game.Orders.ApplyRemoteCompleted(Split(value.ToString()));
            missionOffers.OnValueChanged += (_, value) => game.Orders.ApplyRemoteOffers(Split(value.ToString()));
            activeOrder.OnValueChanged += (_, value) => ApplyActiveOrder(0, value.ToString());
            activeOrder2.OnValueChanged += (_, value) => ApplyActiveOrder(1, value.ToString());
            elapsed.OnValueChanged += (_, value) => game.Orders.ApplyRemoteElapsed(value, 0);
            elapsed2.OnValueChanged += (_, value) => game.Orders.ApplyRemoteElapsed(value, 1);
            washerState.OnValueChanged += (_, value) => ApplyWasher(value);
            // Nouveau tapis accepté chez l'hôte : retour à l'atelier, même depuis le résumé du tapis précédent (idempotent).
            orderEpoch.OnValueChanged += (_, value) =>
            {
                if (game.GameState.State == GameState.Results) game.Orders.ReturnToWorkshop();
            };
            robotState.OnValueChanged += (_, value) => ApplyRobot(value);
            // Atelier de l'hôte : décor et machines (les achats de l'invité restent dans sa carrière).
            game.WorkshopFromHost = true;
            foreach (var machine in game.Machines)
                if (machine != null) machine.Remote = this;
            if (Washer != null) Washer.Remote = this;
            workshopState.OnValueChanged += (_, value) => ApplyWorkshop(value);
            ApplyWorkshop(workshopState.Value);
            SetUpGuestPranks();
            SetUpItems();
            SetUpVoice();

            game.Orders.SetBoardReputation(boardReputation.Value);
            game.Orders.ApplyRemoteCompleted(Split(completedOrders.Value.ToString()));
            game.Orders.ApplyRemoteOffers(Split(missionOffers.Value.ToString()));
            ApplyActiveOrder(0, activeOrder.Value.ToString());
            ApplyActiveOrder(1, activeOrder2.Value.ToString());
            ApplyWasher(washerState.Value);
            ApplyRobot(robotState.Value);

            // Arrivée : contrôles coupés et HUD en chargement jusqu'à réception de l'état de l'hôte et de la carrière validée.
            ShowedLoading = true;
            game.GameState.Synchronizing = true;
            game.Hud.SetLoading("Connexion à l'atelier de l'hôte — synchronisation de la partie (sauvegarde de l'hôte)…");
            SendHello();
            StateRequestRpc();
        }

        /// <summary>Arrivée de l'invité : demande de sa vue de la session (rien n'est annoncé : sa fiche est chez l'hôte).</summary>
        void SendHello() => SessionHelloRpc();

        /// <summary>Applique la vue de la session envoyée par l'hôte (le produit sélectionné reste le choix local).</summary>
        void ApplyView(PlayerCareer view, bool inventory)
        {
            game.Wallet.Wallet.Restore(view.Money);
            game.Reputation.Restore(view.Reputation);
            game.Detergent.Restore(view.DetergentStock);
            game.Upgrades.Restore(view.Upgrades);
            var entries = new List<KeyValuePair<string, float>>();
            foreach (var pair in view.Products) entries.Add(pair);
            var active = game.Products.Book.Active;
            game.Products.Restore(entries, active != null ? active.id : view.ActiveProduct);
            if (!inventory) return;
            // Arrivée (ou objet perdu, ramassé, revendu) : ce que porte ce joueur est celui de sa fiche chez l'hôte.
            game.ToolController.Inventory?.Restore(view.Carried);
            var soap = game.ToolController.Equipment.Soap;
            soap.Empty();
            soap.Add(view.SoapReserve >= 0f ? view.SoapReserve : soap.Capacity);
            lastReportedSoap = soap.Amount;
            inventoryDirty = false;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void CareerRpc(byte[] data, bool inventory, RpcParams rpcParams)
        {
            if (IsServer) return;
            PlayerCareer view;
            try
            {
                view = CareerCodec.Decode(data);
            }
            catch (FormatException)
            {
                Debug.LogWarning("[RugWash] Vue de la session illisible reçue de l'hôte : ignorée.");
                return;
            }
            ApplyView(view, inventory || !careerReceived);
            careerReceived = true;
        }

        void OnGuestInventoryChanged() => inventoryDirty = true;

        /// <summary>Invité : signale à l'hôte ce qu'il porte (et sa réserve de détergent), pour sa fiche dans la sauvegarde de la session.</summary>
        void ReportInventory(float dt)
        {
            if (!careerReceived || game.ToolController.Inventory == null) return;
            inventoryTimer += dt;
            float soap = game.ToolController.Equipment.Soap.Amount;
            bool soapChanged = Mathf.Abs(soap - lastReportedSoap) > 0.05f;
            if (inventoryTimer < (inventoryDirty ? 0.3f : 3f) || (!inventoryDirty && !soapChanged)) return;
            inventoryTimer = 0f;
            inventoryDirty = false;
            lastReportedSoap = soap;
            InventoryRpc(new FixedString512Bytes(Truncate(string.Join(",", game.ToolController.Inventory.Carried), 500)), soap);
        }

        /// <summary>Invité : fin de la synchronisation d'arrivée quand toutes les surfaces et la vue de la session sont reçues.</summary>
        void GuestTick()
        {
            if (client == null) return;
            float dt = Time.unscaledDeltaTime;
            ReportInventory(dt);
            if (GuestReady) return;
            bool surfaces = true;
            foreach (byte id in SharedSurfaces()) surfaces &= client.IsReady(id);
            if (!careerReceived || !surfaces)
            {
                // Réponse perdue ou partie trop tôt : nouvelle demande (l'hôte ne remplace jamais une carrière déjà tenue).
                helloTimer += dt;
                if (helloTimer >= HelloRetrySeconds)
                {
                    helloTimer = 0f;
                    if (!careerReceived) SendHello();
                    StateRequestRpc();
                }
            }
            if (!careerReceived || !surfaces)
            {
                readyTimer += dt;
                if (readyTimer > 10f && !readyWarned)
                {
                    readyWarned = true;
                    Debug.LogWarning($"[RugWash] Synchronisation de l'invité toujours en cours après 10 s : carrière {(careerReceived ? "reçue" : "en attente")}, " +
                                     $"tapis {client.IsReady(RugId)}, aire {client.IsReady(WorkSurfaceId)}, sol {client.IsReady(FloorId)}.");
                }
                return;
            }
            GuestReady = true;
            game.GameState.Synchronizing = false;
            game.Hud.SetLoading(null);
            game.WorkSurface.RefreshStatus(force: true);
            var secondArea = game.Orders.WorkSurfaceOf(1);
            if (secondArea != null) secondArea.RefreshStatus(force: true);
            game.Hud.ShowToast("Connecté à l'atelier de l'hôte — vous jouez sur sa sauvegarde ; la vôtre n'est pas modifiée");
        }

        static string[] Split(string text) => string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split(',');

        void ApplyActiveOrder(int bay, string id)
        {
            var orders = game.Orders;
            var current = orders.OrderOf(bay);
            if (string.IsNullOrEmpty(id))
            {
                if (current != null && game.GameState.State != GameState.Results) orders.ApplyRemoteReturn(bay);
                return;
            }
            if (current != null && current.id == id) return;
            var order = orders.Resolve(id);
            if (order != null) orders.ApplyRemoteStart(order, bay == 0 ? elapsed.Value : elapsed2.Value, bay);
        }

        WorkshopNetState CaptureWorkshop()
        {
            var state = new WorkshopNetState
            {
                Tier = (byte)(game.Decor != null ? Mathf.Max(0, game.Decor.Tier) : 0),
                Facilities = game.Extension != null ? game.Extension.Facilities : (byte)0,
            };
            for (int i = 0; i < game.Machines.Count && i < 8; i++)
            {
                var machine = game.Machines[i];
                if (machine == null) continue;
                if (machine.IsOwned) state.MachinesOwned |= (byte)(1 << i);
                if (machine.IsRunning) state.MachinesRunning |= (byte)(1 << i);
            }
            return state;
        }

        void ApplyWorkshop(WorkshopNetState state)
        {
            if (game.Decor != null) game.Decor.Apply(state.Tier);
            if (game.Extension != null) game.Extension.ApplyFacilities(state.Facilities);
            for (int i = 0; i < game.Machines.Count && i < 8; i++)
                if (game.Machines[i] != null)
                    game.Machines[i].ApplyRemoteState((state.MachinesOwned & (1 << i)) != 0, (state.MachinesRunning & (1 << i)) != 0);
        }

        void ApplyRobot(RobotNetState state)
        {
            if (game.Robot == null) return;
            game.Robot.SetOwned(state.Owned);
            game.Robot.ApplyRemote(new RobotState
            {
                mode = state.Mode, battery = state.Battery / 255f, resumeAfterCharge = state.Resume, x = state.X, z = state.Z, yaw = state.Yaw,
            });
        }

        /// <summary>
        /// Recharge localement la surface que l'hôte vient de charger (même définition, même commande), ou la retire si
        /// l'hôte n'a plus de tapis posé (identité vide).
        /// </summary>
        bool LoadSurface(byte id, SurfaceIdentity identity)
        {
            var surface = Surface(id);
            if (surface == null) return false;
            if (BayOfSurface(id) < 0) return surface.Grid != null;
            if (string.IsNullOrEmpty(identity.DefinitionId))
            {
                surface.Unload();
                return true;
            }
            var definition = FindRug(identity.DefinitionId);
            if (definition == null)
            {
                Debug.LogError($"[RugWash] Tapis « {identity.DefinitionId} » inconnu de ce poste (version différente ?).");
                return false;
            }
            var order = string.IsNullOrEmpty(identity.OrderId) ? null : game.Orders.Resolve(identity.OrderId);
            if (order != null) surface.LoadRug(definition, order.dirt, order.dirtSeed);
            else surface.LoadRug(definition);
            return true;
        }

        RugDefinition FindRug(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var rug in game.AvailableRugs)
                if (rug != null && rug.id == id) return rug;
            var catalog = game.RugDexService.Catalog;
            if (catalog != null)
                foreach (var rug in catalog.rugs)
                    if (rug != null && rug.id == id) return rug;
            foreach (var order in game.Orders.Book.Orders)
                if (order.rug != null && order.rug.id == id) return order.rug;
            return null;
        }

        void SendOperation(ArraySegment<byte> data)
        {
            if (NetworkManager == null || !NetworkManager.IsConnectedClient) return;
            using var buffer = new FastBufferWriter(data.Count, Allocator.Temp);
            buffer.WriteBytesSafe(data.Array, data.Count, data.Offset);
            NetworkManager.CustomMessagingManager.SendNamedMessage(OperationMessage, NetworkManager.ServerClientId, buffer, NetworkDelivery.ReliableSequenced);
            OperationBytesSent += data.Count;
        }

        void OnSurfaceMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            int length = reader.Length - reader.Position;
            if (length <= 0) return;
            if (receiveBuffer.Length < length) receiveBuffer = new byte[Mathf.Max(length, receiveBuffer.Length * 2)];
            reader.ReadBytesSafe(ref receiveBuffer, length);
            long timer = Diagnostics.PerfStats.Begin();
            client.Receive(new ArraySegment<byte>(receiveBuffer, 0, length));
            Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.NetRead, timer, length);
        }

        // ---------------------------------------------------------------- Demandes des invités (IWorkshopRequests)

        public void RequestStartOrder(string orderId, int bay) => StartOrderRpc(new FixedString64Bytes(orderId ?? string.Empty), (byte)Mathf.Clamp(bay, 0, 255));
        public void RequestDeliver(int bay) => DeliverRpc((byte)Mathf.Clamp(bay, 0, 255));
        public void RequestAbandon(int bay) => AbandonRpc((byte)Mathf.Clamp(bay, 0, 255));
        public void RequestWasher(byte action, int bay) => WasherRpc(action, (byte)Mathf.Clamp(bay, 0, 255));
        public void RequestUpgrade(string upgradeId) => UpgradeRpc(new FixedString64Bytes(upgradeId ?? string.Empty));
        public void RequestProduct(string productId) => ProductRpc(new FixedString64Bytes(productId ?? string.Empty));
        public void RequestRobotToggle() => RobotToggleRpc();
        public void RequestMachine(string machineId) => MachineRpc(new FixedString32Bytes(machineId ?? string.Empty));

        [Rpc(SendTo.Server)]
        void MachineRpc(FixedString32Bytes machineId, RpcParams rpcParams = default)
        {
            var machine = game.FindMachine(machineId.ToString());
            ulong sender = rpcParams.Receive.SenderClientId;
            if (machine == null)
            {
                ToastToRpc("Machine inconnue.", RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            // L'hôte décide (machine achetée, tapis, cycle déjà fait) ; l'état revient par la variable d'atelier.
            string message = machine.ActivateLocal();
            ToastToRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        public void RequestDetergentCan() => DetergentCanRpc();
        public void RequestDetergentRefill(float liters) => DetergentRefillRpc(liters);

        /// <summary>Bidon acheté par un invité : payé avec l'argent de la session, ajouté au stock de la session (sauvegarde de l'hôte).</summary>
        [Rpc(SendTo.Server)]
        void DetergentCanRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!members.ContainsKey(sender))
            {
                ShopResultRpc("Partie en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            var result = game.Shop.TryBuyDetergentCan(silent: true);
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Bidon de détergent acheté : +{game.Detergent.CanLiters:0} L" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == ProductPurchaseResult.Purchased) SendView(sender, inventory: false);
        }

        /// <summary>Remplissage au poste par un invité : prélevé sur le stock de la session (jamais plus que la réserve ne peut contenir).</summary>
        [Rpc(SendTo.Server)]
        void DetergentRefillRpc(float liters, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!members.ContainsKey(sender) || float.IsNaN(liters)) return;
            float granted = game.Detergent.Take(Mathf.Clamp(liters, 0f, MaxReserveLiters));
            DetergentGrantedRpc(granted, RpcTarget.Single(sender, RpcTargetUse.Temp));
            SendView(sender, inventory: false);
        }

        /// <summary>Borne d'un remplissage (réserve maximale du catalogue d'améliorations).</summary>
        const float MaxReserveLiters = 12f;

        [Rpc(SendTo.SpecifiedInParams)]
        void DetergentGrantedRpc(float liters, RpcParams rpcParams)
        {
            game.Hud.ShowToast(Player.ResourceStation.ApplyGrantedDetergent(game.ToolController.Equipment.Soap, liters));
        }

        [Rpc(SendTo.Server)]
        void StartOrderRpc(FixedString64Bytes orderId, byte bay, RpcParams rpcParams = default)
        {
            var order = game.Orders.Book.Find(orderId.ToString());
            ulong sender = rpcParams.Receive.SenderClientId;
            if (order == null)
            {
                ToastToRpc("Commande inconnue.", RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            var result = game.Orders.TryStart(order, bay);
            int placed = game.Orders.BayOf(order);
            string where = game.Orders.UnlockedBayCount > 1 && placed >= 0 ? $" ({OrderManager.BayLabel(placed).ToLowerInvariant()})" : string.Empty;
            if (result == OrderStartResult.Started) ToastRpc($"{session?.NameOf(sender) ?? "Un joueur"} a accepté « {order.title} »{where}.");
            else ToastToRpc(OrderManager.StartResultMessage(result), RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Server)]
        void DeliverRpc(byte bay, RpcParams rpcParams = default)
        {
            // Une seule livraison possible : sans commande dans cette zone, la demande est ignorée (double appui, deux joueurs).
            if (game.Orders.OrderOf(bay) == null) return;
            game.Orders.Deliver(bay);
        }

        [Rpc(SendTo.Server)]
        void AbandonRpc(byte bay, RpcParams rpcParams = default)
        {
            var order = game.Orders.OrderOf(bay);
            if (order == null) return;
            if (game.Orders.Abandon(bay)) ToastRpc($"{session?.NameOf(rpcParams.Receive.SenderClientId) ?? "Un joueur"} a abandonné « {order.title} ».");
        }

        [Rpc(SendTo.Server)]
        void UpgradeRpc(FixedString64Bytes upgradeId, RpcParams rpcParams = default)
        {
            UpgradeDefinition upgrade = null;
            foreach (var candidate in game.Upgrades.Catalog)
                if (candidate != null && candidate.id == upgradeId.ToString()) upgrade = candidate;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (upgrade == null || !members.TryGetValue(sender, out var member))
            {
                ShopResultRpc(upgrade == null ? "Article inconnu" : "Partie en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            // Vérifié ici : améliorations de l'atelier réservées à l'hôte (même si le bouton de l'invité était actif) ;
            // sinon prix, prérequis et réputation, avec l'argent et la réputation de la session et les achats de cet invité.
            // Un même achat demandé deux fois n'est payé qu'une fois (déjà possédé).
            var result = SessionEconomyRules.CheckGuestPurchase(upgrade, OwnedBy(member), game.Wallet.Wallet, game.Reputation.Points);
            if (result == PurchaseResult.Purchased && !game.Wallet.Wallet.TrySpend(upgrade.price)) result = PurchaseResult.InsufficientFunds;
            if (result == PurchaseResult.Purchased)
            {
                member.Personal.Add(upgrade.id);
                StoreMember(member);
                game.SaveSystem.SaveNow("achat d'un joueur");
            }
            ShopResultRpc(result == PurchaseResult.Purchased ? $"Achat effectué : {upgrade.displayName}" : ShopRules.Describe(result),
                result == PurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == PurchaseResult.Purchased) SendView(sender, inventory: false);
        }

        [Rpc(SendTo.Server)]
        void ProductRpc(FixedString64Bytes productId, RpcParams rpcParams = default)
        {
            ProductDefinition product = null;
            foreach (var candidate in game.Products.Book.Catalog)
                if (candidate != null && candidate.id == productId.ToString()) product = candidate;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (product == null || !members.ContainsKey(sender))
            {
                ShopResultRpc(product == null ? "Article inconnu" : "Partie en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            // Flacon payé avec l'argent de la session, rangé dans le stock de la session (sauvegarde de l'hôte).
            var result = game.Shop.TryBuyProduct(product, silent: true);
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Achat effectué : {product.displayName}" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == ProductPurchaseResult.Purchased) SendView(sender, inventory: false);
        }

        [Rpc(SendTo.Server)]
        void RobotToggleRpc(RpcParams rpcParams = default)
        {
            string message = game.Robot.Toggle();
            ToastToRpc(message, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ShopResultRpc(string message, bool success, RpcParams rpcParams)
        {
            game.Shop.NotifyRemoteResult(message, success);
            game.Hud.ShowToast(message);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ToastToRpc(string message, RpcParams rpcParams) => game.Hud.ShowToast(message);

        [Rpc(SendTo.Everyone)]
        void ToastRpc(string message)
        {
            game.Hud.ShowToast(message);
            // Arrivées, départs, commandes acceptées : aussi dans le chat (historique de la session).
            AddChatLine(null, message, true);
        }

        [Rpc(SendTo.NotServer)]
        void DeliveredRpc(FixedString64Bytes orderId, float cleanliness, float cleanCells, float moisture, float foam, float residue, float wear,
            float remaining, float initial, float elapsedSeconds, byte bay, int epoch)
        {
            var order = game.Orders.Resolve(orderId.ToString());
            if (order == null) return;
            var summary = new RugCleaningSummary(cleanliness, cleanCells, moisture, foam, residue, wear, remaining, initial);
            // Livraison reçue après qu'un nouveau tapis a été accepté (désordre réseau) : enregistrée sans résumé bloquant.
            game.Orders.ApplyRemoteDelivery(order, summary, elapsedSeconds, bay, enterResults: epoch >= orderEpoch.Value);
            RemoteDeliveries++;
        }
    }
}
