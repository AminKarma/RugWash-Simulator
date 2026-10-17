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
    /// <item>Progression personnelle (carrière : argent, réputation, améliorations, produits) : propre à chaque joueur.
    /// À son arrivée, l'invité annonce la carrière lue dans SA sauvegarde ; l'hôte la borne, puis applique lui-même les
    /// achats, consommations et récompenses de cet invité et lui renvoie le résultat, que l'invité sauvegarde chez lui.
    /// Un invité ne peut donc pas se déclarer de l'argent ou un outil en cours de partie ; la carrière de l'hôte n'est
    /// jamais copiée chez un invité.</item>
    /// <item>Livraison : évaluée et récompensée une seule fois par l'hôte ; chaque joueur présent reçoit la récompense
    /// complète et la variation de réputation dans SA carrière.</item>
    /// <item>Arrivée : l'invité reste en « synchronisation » (contrôles coupés, HUD en chargement) jusqu'à réception de
    /// toutes les surfaces et de sa carrière validée.</item>
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

        // Progression (grande mise à jour) : chaque joueur a SA carrière (argent, réputation, achats, produits, détergent),
        // tenue et validée par l'hôte pendant la partie, sauvegardée dans l'emplacement multijoueur du joueur. À la
        // première arrivée sur un emplacement multijoueur, cette carrière est une copie de celle de l'hôte ; ensuite elle
        // est indépendante (les achats de l'un ne changent jamais la progression de l'autre).
        readonly Dictionary<ulong, PlayerCareer> careers = new Dictionary<ulong, PlayerCareer>();
        readonly HashSet<ulong> careerDirty = new HashSet<ulong>();
        readonly List<ulong> careerFlush = new List<ulong>();
        readonly HashSet<ulong> initialSyncs = new HashSet<ulong>();
        ulong operationSender;
        // Invité : carrière validée reçue (fin de la synchronisation d'arrivée) ; annonce répétée tant qu'elle manque.
        bool careerReceived;
        float helloTimer, readyTimer;
        bool readyWarned;
        const float HelloRetrySeconds = 3f;

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

        /// <summary>Hôte : carrière tenue pour un invité (copie), ou null.</summary>
        public PlayerCareer CareerOf(ulong clientId) => careers.TryGetValue(clientId, out var career) ? career.Clone() : null;
        /// <summary>Hôte : vrai si la carrière de cet invité a été initialisée par copie de celle de l'hôte.</summary>
        public bool WasInitialSync(ulong clientId) => initialSyncs.Contains(clientId);
        /// <summary>Invité : carrière validée reçue au moins une fois.</summary>
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
            var messaging = NetworkManager != null ? NetworkManager.CustomMessagingManager : null;
            if (IsServer)
            {
                messaging?.UnregisterNamedMessageHandler(OperationMessage);
                if (NetworkManager != null && NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
                if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
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
                    // Fin de partie pour l'invité : sa carrière validée est écrite dans SON emplacement multijoueur (jamais
                    // l'atelier de l'hôte) ; une carrière jamais reçue n'écrase rien.
                    if (careerReceived) game.SaveSystem.SaveNow("fin de la partie en ligne");
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
            game.Upgrades.Changed += PushHostGolden;
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
            game.Upgrades.Changed -= PushHostGolden;
            UnhookWasher();
        }

        void OnReputationChanged(int points) => boardReputation.Value = points;

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
            // Carrière annoncée avant la fin de la synchronisation : la réponse part maintenant.
            if (careers.ContainsKey(clientId)) SendCareer(clientId, persist: false);
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
            careers.Remove(clientId);
            careerDirty.Remove(clientId);
            initialSyncs.Remove(clientId);
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
            operationSender = senderClientId;
            if (!host.TryApply(senderClientId, new ArraySegment<byte>(receiveBuffer, 0, length), out var applied, ProductFilter)) return;
            // Produit spécialisé pulvérisé par un invité : prélevé sur SON stock (carrière tenue par l'hôte).
            if (applied.Product != ProductTable.Standard && careers.TryGetValue(senderClientId, out var career))
            {
                var product = game.Rug.Products.Get(applied.Product);
                if (product != null && CareerRules.Consume(career, product.id, product.litersPerSecond * applied.DeltaTime) > 0f)
                    careerDirty.Add(senderClientId);
            }
        }

        /// <summary>Un invité ne peut utiliser un produit spécialisé que s'il en a dans SON stock ; sinon, détergent standard.</summary>
        byte ProductFilter(byte id)
        {
            if (id == ProductTable.Standard) return id;
            var product = game.Rug.Products.Get(id);
            return product != null && careers.TryGetValue(operationSender, out var career) && career.StockOf(product.id) > 0f
                ? id : ProductTable.Standard;
        }

        // ---------------------------------------------------------------- Carrières (hôte)

        /// <summary>
        /// Envoie à un invité sa carrière telle que l'hôte la tient. <paramref name="persist"/> : à sauvegarder aussitôt.
        /// La première réponse après une copie de la carrière de l'hôte porte le marqueur « synchronisation initiale ».
        /// </summary>
        void SendCareer(ulong clientId, bool persist)
        {
            if (!careers.TryGetValue(clientId, out var career)) return;
            careerDirty.Remove(clientId);
            // Outils dorés de cet invité, d'après SA carrière (achats validés ici) : visibles par tous sur sa silhouette.
            var avatar = AvatarOf(clientId);
            if (avatar != null) avatar.SetGoldenTools(game.Upgrades.GoldenMaskFor(career.Upgrades));
            CareerRpc(CareerCodec.Encode(career), persist, initialSyncs.Contains(clientId), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>
        /// Arrivée d'un invité. <paramref name="initialSync"/> : son emplacement multijoueur n'a encore jamais joué — sa
        /// carrière de départ est une copie de celle de l'hôte (une seule fois). Sinon, la carrière annoncée (celle de SON
        /// emplacement) est bornée par l'hôte. Une annonce répétée (réponse perdue, ou tentative de se redonner de l'argent)
        /// ne remplace jamais la carrière déjà tenue : elle redemande seulement la carrière actuelle.
        /// </summary>
        [Rpc(SendTo.Server)]
        void CareerHelloRpc(byte[] data, bool initialSync, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender == NetworkManager.LocalClientId) return;
            if (careers.ContainsKey(sender))
            {
                if (syncedClients.Contains(sender)) SendCareer(sender, persist: false);
                return;
            }
            PlayerCareer announced = null;
            if (!initialSync)
            {
                try
                {
                    announced = CareerCodec.Decode(data);
                }
                catch (FormatException)
                {
                    Debug.LogWarning($"[RugWash] Carrière illisible reçue du client {sender} : carrière vierge.");
                    announced = new PlayerCareer();
                }
            }
            careers[sender] = CareerRules.Arrival(initialSync, announced, CaptureHostCareer(), game.Upgrades.Catalog,
                game.Products.Book.Catalog, game.Detergent.MaxStock);
            if (initialSync) initialSyncs.Add(sender);
            Debug.Log($"[RugWash] Carrière de l'invité {sender} : {(initialSync ? "copie de celle de l'hôte (première partie sur cet emplacement)" : "la sienne, bornée")}.");
            // Réponse seulement à un invité synchronisé (sinon à la fin de sa synchronisation, OnClientSynchronized).
            if (syncedClients.Contains(sender)) SendCareer(sender, persist: initialSync);
        }

        /// <summary>Carrière de l'hôte, lue dans ses systèmes (copiée pour un invité qui commence un emplacement multijoueur).</summary>
        PlayerCareer CaptureHostCareer() => CaptureLocalCareer();

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
                // Carrières modifiées par les consommations (produits, farces) : envoi groupé.
                careerFlush.Clear();
                careerFlush.AddRange(careerDirty);
                foreach (ulong id in careerFlush) SendCareer(id, persist: false);
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
                result.ElapsedSeconds, (byte)Mathf.Clamp(result.Bay, 0, 255));
            // Règle de participation : chaque invité présent à la livraison reçoit la même récompense et la même variation de
            // réputation que l'hôte, dans SA carrière (calculées une seule fois, ici).
            foreach (ulong id in syncedClients)
                if (careers.TryGetValue(id, out var career))
                {
                    CareerRules.Reward(career, result);
                    SendCareer(id, persist: true);
                }
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
            // La carrière affichée et sauvegardée est celle de l'emplacement multijoueur de ce joueur.
            game.SaveSystem.GuestCareerMode = true;

            boardReputation.OnValueChanged += (_, value) => game.Orders.SetBoardReputation(value);
            completedOrders.OnValueChanged += (_, value) => game.Orders.ApplyRemoteCompleted(Split(value.ToString()));
            missionOffers.OnValueChanged += (_, value) => game.Orders.ApplyRemoteOffers(Split(value.ToString()));
            activeOrder.OnValueChanged += (_, value) => ApplyActiveOrder(0, value.ToString());
            activeOrder2.OnValueChanged += (_, value) => ApplyActiveOrder(1, value.ToString());
            elapsed.OnValueChanged += (_, value) => game.Orders.ApplyRemoteElapsed(value, 0);
            elapsed2.OnValueChanged += (_, value) => game.Orders.ApplyRemoteElapsed(value, 1);
            washerState.OnValueChanged += (_, value) => ApplyWasher(value);
            robotState.OnValueChanged += (_, value) => ApplyRobot(value);
            // Atelier de l'hôte : décor et machines (les achats de l'invité restent dans sa carrière).
            game.WorkshopFromHost = true;
            foreach (var machine in game.Machines)
                if (machine != null) machine.Remote = this;
            if (Washer != null) Washer.Remote = this;
            workshopState.OnValueChanged += (_, value) => ApplyWorkshop(value);
            ApplyWorkshop(workshopState.Value);
            SetUpGuestPranks();
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
            game.Hud.SetLoading(game.SaveSystem.NeedsInitialSync
                ? "Connexion à l'atelier de l'hôte — première partie sur cet emplacement : copie de la progression de l'hôte…"
                : "Connexion à l'atelier de l'hôte — synchronisation de l'atelier et de votre progression…");
            SendHello();
            StateRequestRpc();
        }

        /// <summary>Annonce de l'invité : sa carrière (emplacement déjà joué) ou une demande de copie (premier passage).</summary>
        void SendHello() => CareerHelloRpc(CareerCodec.Encode(CaptureLocalCareer()), game.SaveSystem.NeedsInitialSync);

        /// <summary>
        /// Diagnostic (vérification automatique du multijoueur) : renvoie à l'hôte une annonce de carrière arbitraire, comme
        /// le ferait un client modifié. L'hôte doit l'ignorer : la carrière tenue en cours de partie ne change pas.
        /// </summary>
        public void SendForgedCareerForDiagnostics(PlayerCareer forged)
        {
            if (!IsServer && forged != null) CareerHelloRpc(CareerCodec.Encode(forged), false);
        }

        /// <summary>Carrière de ce joueur, lue dans ses propres systèmes (chargés depuis SON emplacement).</summary>
        PlayerCareer CaptureLocalCareer()
        {
            var career = new PlayerCareer { Money = game.Wallet.Balance, Reputation = game.Reputation.Points, DetergentStock = game.Detergent.Stock };
            career.Upgrades.UnionWith(game.Upgrades.OwnedIds);
            foreach (var entry in game.Products.Snapshot()) career.Products[entry.Key] = entry.Value;
            career.ActiveProduct = game.Products.Book.Active != null ? game.Products.Book.Active.id : string.Empty;
            return career;
        }

        /// <summary>Applique la carrière validée par l'hôte (le produit sélectionné reste le choix local).</summary>
        void ApplyCareer(PlayerCareer career)
        {
            game.Wallet.Wallet.Restore(career.Money);
            game.Reputation.Restore(career.Reputation);
            game.Detergent.Restore(career.DetergentStock);
            game.Upgrades.Restore(career.Upgrades);
            var entries = new List<KeyValuePair<string, float>>();
            foreach (var pair in career.Products) entries.Add(pair);
            var active = game.Products.Book.Active;
            game.Products.Restore(entries, active != null ? active.id : career.ActiveProduct);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void CareerRpc(byte[] data, bool persist, bool initialSync, RpcParams rpcParams)
        {
            if (IsServer) return;
            PlayerCareer career;
            try
            {
                career = CareerCodec.Decode(data);
            }
            catch (FormatException)
            {
                Debug.LogWarning("[RugWash] Carrière illisible reçue de l'hôte : ignorée.");
                return;
            }
            ApplyCareer(career);
            bool first = !careerReceived;
            careerReceived = true;
            // Copie de départ reçue : l'emplacement est marqué « initialisé » et sauvegardé (jamais recopié ensuite).
            if (initialSync && game.SaveSystem.NeedsInitialSync)
            {
                game.SaveSystem.MarkMultiplayerInitialized();
                if (first) game.Hud.ShowToast("Première partie sur cet emplacement : progression de départ copiée depuis l'hôte. Elle vous appartient désormais.");
            }
            else if (persist) game.SaveSystem.SaveNow("progression en ligne");
        }

        /// <summary>Invité : fin de la synchronisation d'arrivée quand toutes les surfaces et la carrière sont reçues.</summary>
        void GuestTick()
        {
            if (GuestReady || client == null) return;
            float dt = Time.unscaledDeltaTime;
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
            game.Hud.ShowToast($"Connecté à l'atelier de l'hôte — progression de {game.SaveSystem.Slot.DisplayName} (la vôtre)");
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

        /// <summary>Bidon acheté par un invité : payé avec SON argent, ajouté à SON stock (carrière tenue par l'hôte).</summary>
        [Rpc(SendTo.Server)]
        void DetergentCanRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!careers.TryGetValue(sender, out var career))
            {
                ShopResultRpc("Progression en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            var supply = game.Detergent;
            var result = CareerRules.TryBuyDetergentCan(career, supply.CanPrice, supply.CanLiters, supply.MaxStock);
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Bidon de détergent acheté : +{supply.CanLiters:0} L" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == ProductPurchaseResult.Purchased) SendCareer(sender, persist: true);
        }

        /// <summary>Remplissage au poste par un invité : prélevé sur SON stock (jamais plus que la réserve ne peut contenir).</summary>
        [Rpc(SendTo.Server)]
        void DetergentRefillRpc(float liters, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!careers.TryGetValue(sender, out var career)) return;
            float granted = CareerRules.TakeDetergent(career, Mathf.Clamp(liters, 0f, MaxReserveLiters));
            DetergentGrantedRpc(granted, RpcTarget.Single(sender, RpcTargetUse.Temp));
            SendCareer(sender, persist: false);
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
            if (upgrade == null || !careers.TryGetValue(sender, out var career))
            {
                ShopResultRpc(upgrade == null ? "Article inconnu" : "Progression en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            // Payé avec l'argent de l'invité, validé ici (prix, prérequis, réputation de SA carrière) : ni l'argent ni les
            // achats de l'hôte ne sont touchés. Un même achat demandé deux fois n'est payé qu'une fois (déjà possédé).
            var result = CareerRules.TryBuyUpgrade(career, upgrade);
            ShopResultRpc(result == PurchaseResult.Purchased ? $"Achat effectué : {upgrade.displayName}" : ShopRules.Describe(result),
                result == PurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == PurchaseResult.Purchased) SendCareer(sender, persist: true);
        }

        [Rpc(SendTo.Server)]
        void ProductRpc(FixedString64Bytes productId, RpcParams rpcParams = default)
        {
            ProductDefinition product = null;
            foreach (var candidate in game.Products.Book.Catalog)
                if (candidate != null && candidate.id == productId.ToString()) product = candidate;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (product == null || !careers.TryGetValue(sender, out var career))
            {
                ShopResultRpc(product == null ? "Article inconnu" : "Progression en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            var result = CareerRules.TryBuyProduct(career, product);
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Achat effectué : {product.displayName}" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == ProductPurchaseResult.Purchased) SendCareer(sender, persist: true);
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
            float remaining, float initial, float elapsedSeconds, byte bay)
        {
            var order = game.Orders.Resolve(orderId.ToString());
            if (order == null) return;
            var summary = new RugCleaningSummary(cleanliness, cleanCells, moisture, foam, residue, wear, remaining, initial);
            game.Orders.ApplyRemoteDelivery(order, summary, elapsedSeconds, bay);
            RemoteDeliveries++;
        }
    }
}
