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

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tier);
            serializer.SerializeValue(ref MachinesOwned);
            serializer.SerializeValue(ref MachinesRunning);
        }

        public bool Equals(WorkshopNetState other) => Tier == other.Tier && MachinesOwned == other.MachinesOwned && MachinesRunning == other.MachinesRunning;
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

        /// <summary>Réputation de l'hôte : elle déverrouille les commandes du tableau de la session (pas la carrière des invités).</summary>
        readonly NetworkVariable<int> boardReputation = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString512Bytes> completedOrders = new NetworkVariable<FixedString512Bytes>();
        readonly NetworkVariable<FixedString64Bytes> activeOrder = new NetworkVariable<FixedString64Bytes>();
        /// <summary>Missions générées proposées sur le tableau de l'hôte (identifiants : les invités les régénèrent à l'identique).</summary>
        readonly NetworkVariable<FixedString128Bytes> missionOffers = new NetworkVariable<FixedString128Bytes>();
        readonly NetworkVariable<float> elapsed = new NetworkVariable<float>();
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

        // Économie de session (règle des sauvegardes) : celle de la sauvegarde de l'hôte — argent, réputation, achats,
        // produits, stock de détergent. Tous les joueurs l'utilisent ; seul l'hôte la modifie et la sauvegarde. Diffusée
        // aux invités dès qu'elle change (envoi groupé, au plus quatre fois par seconde).
        bool economyDirty;
        float economyTimer;
        const float EconomySendInterval = 0.25f;
        // Invité : économie de l'hôte reçue (fin de la synchronisation d'arrivée) ; état redemandé tant qu'elle manque.
        bool economyReceived;
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

        /// <summary>Économie de session telle que ce poste l'utilise (hôte : la sienne ; invité : celle reçue de l'hôte).</summary>
        public PlayerCareer SessionEconomy => CaptureEconomy();
        /// <summary>Invité : économie de l'hôte reçue au moins une fois.</summary>
        public bool EconomyReceived => economyReceived;

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
                    // Fin de partie pour l'invité : rien de la session n'est écrit dans SA sauvegarde (règle des sauvegardes) ;
                    // sa progression personnelle est relue du disque au prochain chargement de l'atelier.
                    game.Orders.Remote = null;
                    game.Shop.Remote = null;
                    game.Robot.Remote = null;
                    game.Detergent.Remote = null;
                    game.WorkshopFromHost = false;
                    foreach (var machine in game.Machines)
                        if (machine != null) machine.Remote = null;
                    TearDownPranks();
                    game.Orders.SetBoardReputation(-1);
                    game.GameState.Synchronizing = false;
                    game.Hud.SetLoading(null);
                }
            }
            if (game != null) game.GameState.Networked = false;
        }

        RugCleaningController Surface(byte id) =>
            id == RugId ? game.Rug : id == WorkSurfaceId ? game.WorkSurface.Surface : id == FloorId ? game.RoomFloor : null;

        // ---------------------------------------------------------------- Hôte

        void SetUpHost()
        {
            host = new SurfaceSyncHost(Identify);
            host.Register(RugId, Surface(RugId));
            host.Register(WorkSurfaceId, Surface(WorkSurfaceId));
            host.Register(FloorId, Surface(FloorId));
            NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(OperationMessage, OnOperationMessage);
            NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronized;
            NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            HookHostSystems();
            SetUpHostPranks();
            boardReputation.Value = game.Reputation.Points;
            PushOrders();
            SpawnAvatar(NetworkManager.LocalClientId);
            // Invités déjà connectés (arrivés pendant le chargement de l'atelier).
            foreach (var id in NetworkManager.ConnectedClientsIds)
                if (id != NetworkManager.LocalClientId) OnClientSynchronized(id);
        }

        SurfaceIdentity Identify(byte id)
        {
            var surface = Surface(id);
            string definition = surface != null && surface.Definition != null ? surface.Definition.id : string.Empty;
            string order = id == RugId && game.Orders.ActiveOrder != null ? game.Orders.ActiveOrder.id : string.Empty;
            return new SurfaceIdentity(definition, order);
        }

        void HookHostSystems()
        {
            game.Reputation.Changed += OnReputationChanged;
            game.Orders.Changed += PushOrders;
            game.Orders.OrderFinished += OnHostOrderFinished;
            game.Wallet.Wallet.Changed += OnHostWalletChanged;
            game.Upgrades.Changed += MarkEconomyDirty;
            game.Products.Book.Changed += MarkEconomyDirty;
            game.Detergent.Changed += MarkEconomyDirty;
        }

        void OnHostWalletChanged(int balance, int delta) => economyDirty = true;
        void MarkEconomyDirty() => economyDirty = true;

        void UnhookHostSystems()
        {
            if (game == null) return;
            game.Reputation.Changed -= OnReputationChanged;
            game.Orders.Changed -= PushOrders;
            game.Orders.OrderFinished -= OnHostOrderFinished;
            game.Wallet.Wallet.Changed -= OnHostWalletChanged;
            game.Upgrades.Changed -= MarkEconomyDirty;
            game.Products.Book.Changed -= MarkEconomyDirty;
            game.Detergent.Changed -= MarkEconomyDirty;
        }

        void OnReputationChanged(int points)
        {
            boardReputation.Value = points;
            economyDirty = true;
        }

        void PushOrders()
        {
            var active = game.Orders.ActiveOrder;
            activeOrder.Value = new FixedString64Bytes(active != null ? Truncate(active.id, 60) : string.Empty);
            completedOrders.Value = new FixedString512Bytes(Truncate(string.Join(",", game.Orders.Book.CompletedIds()), 500));
            var offers = new List<string>();
            foreach (var offer in game.Orders.Missions.Offers) offers.Add(offer.Id);
            missionOffers.Value = new FixedString128Bytes(Truncate(string.Join(",", offers), 120));
            elapsed.Value = game.Orders.ElapsedSeconds;
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
        /// État complet pour un invité : instantané de chaque surface, puis l'économie de session ; ensuite, il reçoit les
        /// mises à jour comme les autres. Sans effet de bord si on le renvoie (mêmes valeurs simplement réécrites).
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
            EconomyToRpc(CareerCodec.Encode(CaptureEconomy()), RpcTarget.Single(clientId, RpcTargetUse.Temp));
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
            // Produit spécialisé pulvérisé par un invité : prélevé sur le stock de l'atelier (économie de session).
            if (applied.Product != ProductTable.Standard)
            {
                var product = game.Rug.Products.Get(applied.Product);
                if (product != null) game.Products.Book.ConsumeFrom(product, product.litersPerSecond * applied.DeltaTime);
            }
        }

        /// <summary>Un produit spécialisé n'est utilisé que s'il en reste dans le stock de l'atelier ; sinon, détergent standard.</summary>
        byte ProductFilter(byte id)
        {
            if (id == ProductTable.Standard) return id;
            var product = game.Rug.Products.Get(id);
            return product != null && game.Products.StockOf(product) > 0f ? id : ProductTable.Standard;
        }

        // ---------------------------------------------------------------- Économie de session (hôte → invités)

        /// <summary>L'économie de l'hôte a changé : diffusée à tous les invités (envoi groupé).</summary>
        void FlushEconomy(float dt)
        {
            economyTimer += dt;
            if (!economyDirty || economyTimer < EconomySendInterval) return;
            economyTimer = 0f;
            economyDirty = false;
            if (syncedClients.Count > 0) EconomyRpc(CareerCodec.Encode(CaptureEconomy()));
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
                if (game.Orders.ActiveOrder != null) elapsed.Value = game.Orders.ElapsedSeconds;
            }
            FlushEconomy(dt);
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
            if (game.Rug.Grid == null) return;
            var summary = game.Rug.Grid.ComputeSummary();
            // La commande livrée vient du résultat : une mission générée a déjà quitté le tableau (remplacée).
            var order = result.Order != null ? result.Order : game.Orders.Book.Find(activeOrder.Value.ToString()) ?? FindLastCompleted();
            if (order == null) return;
            DeliveredRpc(new FixedString64Bytes(order.id), summary.Cleanliness01, summary.CleanCellRatio01, summary.AverageMoisture01,
                summary.AverageFoam01, summary.AverageResidue01, summary.AverageWear01, summary.RemainingDirt, summary.InitialDirt,
                game.Orders.ElapsedSeconds);
            // Récompense et réputation versées une seule fois, dans l'économie de l'hôte (OrderManager de l'hôte) : les invités
            // la voient par la diffusion de l'économie ; rien n'entre dans leur sauvegarde personnelle.
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
            client.Register(RugId, Surface(RugId));
            client.Register(WorkSurfaceId, Surface(WorkSurfaceId));
            client.Register(FloorId, Surface(FloorId));
            NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(SurfaceMessageName, OnSurfaceMessage);

            game.Orders.Remote = this;
            game.Shop.Remote = this;
            game.Robot.Remote = this;
            game.Detergent.Remote = this;
            // La carrière affichée et sauvegardée reste celle de ce joueur (jamais celle de l'hôte).
            game.SaveSystem.GuestCareerMode = true;

            boardReputation.OnValueChanged += (_, value) => game.Orders.SetBoardReputation(value);
            completedOrders.OnValueChanged += (_, value) => game.Orders.ApplyRemoteCompleted(Split(value.ToString()));
            missionOffers.OnValueChanged += (_, value) => game.Orders.ApplyRemoteOffers(Split(value.ToString()));
            activeOrder.OnValueChanged += (_, value) => ApplyActiveOrder(value.ToString());
            elapsed.OnValueChanged += (_, value) => game.Orders.ApplyRemoteElapsed(value);
            robotState.OnValueChanged += (_, value) => ApplyRobot(value);
            // Atelier de l'hôte : décor et machines (les achats de l'invité restent dans sa carrière).
            game.WorkshopFromHost = true;
            foreach (var machine in game.Machines)
                if (machine != null) machine.Remote = this;
            workshopState.OnValueChanged += (_, value) => ApplyWorkshop(value);
            ApplyWorkshop(workshopState.Value);
            SetUpGuestPranks();

            game.Orders.SetBoardReputation(boardReputation.Value);
            game.Orders.ApplyRemoteCompleted(Split(completedOrders.Value.ToString()));
            game.Orders.ApplyRemoteOffers(Split(missionOffers.Value.ToString()));
            ApplyActiveOrder(activeOrder.Value.ToString());
            ApplyRobot(robotState.Value);

            // Arrivée : contrôles coupés et HUD en chargement jusqu'à réception de l'état de l'hôte et de la carrière validée.
            ShowedLoading = true;
            game.GameState.Synchronizing = true;
            game.Hud.SetLoading("Connexion à l'atelier de l'hôte — synchronisation de l'atelier…");
            StateRequestRpc();
        }

        /// <summary>Économie lue dans les systèmes de ce poste (hôte : celle de sa sauvegarde ; invité : celle reçue).</summary>
        PlayerCareer CaptureEconomy()
        {
            var career = new PlayerCareer { Money = game.Wallet.Balance, Reputation = game.Reputation.Points, DetergentStock = game.Detergent.Stock };
            career.Upgrades.UnionWith(game.Upgrades.OwnedIds);
            foreach (var entry in game.Products.Snapshot()) career.Products[entry.Key] = entry.Value;
            career.ActiveProduct = game.Products.Book.Active != null ? game.Products.Book.Active.id : string.Empty;
            return career;
        }

        /// <summary>
        /// Invité : applique l'économie de l'hôte pour la durée de la session (le produit sélectionné reste le choix local).
        /// La sauvegarde de l'invité ne l'enregistre jamais (<see cref="Save.SaveManager.GuestCareerMode"/>).
        /// </summary>
        void ApplyEconomy(PlayerCareer career)
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

        [Rpc(SendTo.NotServer)]
        void EconomyRpc(byte[] data) => ReceiveEconomy(data);

        [Rpc(SendTo.SpecifiedInParams)]
        void EconomyToRpc(byte[] data, RpcParams rpcParams) => ReceiveEconomy(data);

        void ReceiveEconomy(byte[] data)
        {
            if (IsServer) return;
            PlayerCareer economy;
            try
            {
                economy = CareerCodec.Decode(data);
            }
            catch (FormatException)
            {
                Debug.LogWarning("[RugWash] Économie de session illisible reçue de l'hôte : ignorée.");
                return;
            }
            ApplyEconomy(economy);
            economyReceived = true;
        }

        /// <summary>Invité : fin de la synchronisation d'arrivée quand toutes les surfaces et la carrière sont reçues.</summary>
        void GuestTick()
        {
            if (GuestReady || client == null) return;
            float dt = Time.unscaledDeltaTime;
            bool surfaces = client.IsReady(RugId) && client.IsReady(WorkSurfaceId) && client.IsReady(FloorId);
            if (!economyReceived || !surfaces)
            {
                // Réponse perdue ou partie trop tôt : l'état complet (surfaces et économie) est redemandé.
                helloTimer += dt;
                if (helloTimer >= HelloRetrySeconds)
                {
                    helloTimer = 0f;
                    StateRequestRpc();
                }
            }
            if (!economyReceived || !surfaces)
            {
                readyTimer += dt;
                if (readyTimer > 10f && !readyWarned)
                {
                    readyWarned = true;
                    Debug.LogWarning($"[RugWash] Synchronisation de l'invité toujours en cours après 10 s : économie {(economyReceived ? "reçue" : "en attente")}, " +
                                     $"tapis {client.IsReady(RugId)}, aire {client.IsReady(WorkSurfaceId)}, sol {client.IsReady(FloorId)}.");
                }
                return;
            }
            GuestReady = true;
            game.GameState.Synchronizing = false;
            game.Hud.SetLoading(null);
            game.WorkSurface.RefreshStatus(force: true);
            game.Hud.ShowToast("Atelier de l'hôte : vous jouez avec son argent et son équipement — votre sauvegarde reste intacte");
        }

        static string[] Split(string text) => string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split(',');

        void ApplyActiveOrder(string id)
        {
            var orders = game.Orders;
            if (string.IsNullOrEmpty(id))
            {
                if (orders.ActiveOrder != null && game.GameState.State != GameState.Results) orders.ApplyRemoteReturn();
                return;
            }
            if (orders.ActiveOrder != null && orders.ActiveOrder.id == id) return;
            var order = orders.Resolve(id);
            if (order != null) orders.ApplyRemoteStart(order, elapsed.Value);
        }

        WorkshopNetState CaptureWorkshop()
        {
            var state = new WorkshopNetState { Tier = (byte)(game.Decor != null ? Mathf.Max(0, game.Decor.Tier) : 0) };
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
            if (id != RugId) return surface.Grid != null;
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

        public void RequestStartOrder(string orderId) => StartOrderRpc(new FixedString64Bytes(orderId ?? string.Empty));
        public void RequestDeliver() => DeliverRpc();
        public void RequestAbandon() => AbandonRpc();
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

        /// <summary>Bidon acheté par un invité : payé avec l'argent de l'atelier, ajouté au stock de l'atelier (sauvegarde de l'hôte).</summary>
        [Rpc(SendTo.Server)]
        void DetergentCanRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var supply = game.Detergent;
            var result = game.Shop.TryBuyDetergentCan();
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Bidon de détergent acheté : +{supply.CanLiters:0} L" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == ProductPurchaseResult.Purchased) NotifyOthers(sender, "un bidon de détergent");
        }

        /// <summary>Remplissage au poste par un invité : prélevé sur le stock de l'atelier (jamais plus que la réserve ne peut contenir).</summary>
        [Rpc(SendTo.Server)]
        void DetergentRefillRpc(float liters, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            float granted = game.Detergent.Take(Mathf.Clamp(liters, 0f, MaxReserveLiters));
            DetergentGrantedRpc(granted, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        /// <summary>Les autres joueurs voient ce qu'un invité a acheté avec l'argent de l'atelier.</summary>
        void NotifyOthers(ulong buyer, string item)
        {
            string message = $"{session?.NameOf(buyer) ?? "Un joueur"} a acheté {item} (argent de l'atelier).";
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                if (id != buyer) ToastToRpc(message, RpcTarget.Single(id, RpcTargetUse.Temp));
        }

        /// <summary>Borne d'un remplissage (réserve maximale du catalogue d'améliorations).</summary>
        const float MaxReserveLiters = 12f;

        [Rpc(SendTo.SpecifiedInParams)]
        void DetergentGrantedRpc(float liters, RpcParams rpcParams)
        {
            game.Hud.ShowToast(Player.ResourceStation.ApplyGrantedDetergent(game.ToolController.Equipment.Soap, liters));
        }

        [Rpc(SendTo.Server)]
        void StartOrderRpc(FixedString64Bytes orderId, RpcParams rpcParams = default)
        {
            var order = game.Orders.Book.Find(orderId.ToString());
            ulong sender = rpcParams.Receive.SenderClientId;
            if (order == null)
            {
                ToastToRpc("Commande inconnue.", RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            var result = game.Orders.TryStart(order);
            if (result == OrderStartResult.Started) ToastRpc($"{session?.NameOf(sender) ?? "Un joueur"} a accepté « {order.title} ».");
            else ToastToRpc(OrderManager.StartResultMessage(result), RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Server)]
        void DeliverRpc(RpcParams rpcParams = default)
        {
            // Une seule livraison possible : sans commande active, la demande est ignorée (double appui, deux joueurs).
            if (game.Orders.ActiveOrder == null) return;
            game.Orders.Deliver();
        }

        [Rpc(SendTo.Server)]
        void AbandonRpc(RpcParams rpcParams = default)
        {
            if (game.Orders.ActiveOrder == null) return;
            string title = game.Orders.ActiveOrder.title;
            if (game.Orders.Abandon()) ToastRpc($"{session?.NameOf(rpcParams.Receive.SenderClientId) ?? "Un joueur"} a abandonné « {title} ».");
        }

        [Rpc(SendTo.Server)]
        void UpgradeRpc(FixedString64Bytes upgradeId, RpcParams rpcParams = default)
        {
            UpgradeDefinition upgrade = null;
            foreach (var candidate in game.Upgrades.Catalog)
                if (candidate != null && candidate.id == upgradeId.ToString()) upgrade = candidate;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (upgrade == null)
            {
                ShopResultRpc("Article inconnu", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            // Achat de session : payé avec l'argent de l'atelier, ajouté aux achats de l'atelier (sauvegarde de l'hôte),
            // avec les mêmes règles qu'un achat de l'hôte (prix, prérequis, réputation de l'atelier).
            var result = game.Shop.TryPurchase(upgrade);
            ShopResultRpc(result == PurchaseResult.Purchased ? $"Achat effectué : {upgrade.displayName} (argent de l'atelier)" : ShopRules.Describe(result),
                result == PurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == PurchaseResult.Purchased) NotifyOthers(sender, $"« {upgrade.displayName} »");
        }

        [Rpc(SendTo.Server)]
        void ProductRpc(FixedString64Bytes productId, RpcParams rpcParams = default)
        {
            ProductDefinition product = null;
            foreach (var candidate in game.Products.Book.Catalog)
                if (candidate != null && candidate.id == productId.ToString()) product = candidate;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (product == null)
            {
                ShopResultRpc("Article inconnu", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            var result = game.Shop.TryBuyProduct(product);
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Achat effectué : {product.displayName} (argent de l'atelier)" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
            if (result == ProductPurchaseResult.Purchased) NotifyOthers(sender, $"« {product.displayName} »");
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
        void ToastRpc(string message) => game.Hud.ShowToast(message);

        [Rpc(SendTo.NotServer)]
        void DeliveredRpc(FixedString64Bytes orderId, float cleanliness, float cleanCells, float moisture, float foam, float residue, float wear,
            float remaining, float initial, float elapsedSeconds)
        {
            var order = game.Orders.Resolve(orderId.ToString());
            if (order == null) return;
            var summary = new RugCleaningSummary(cleanliness, cleanCells, moisture, foam, residue, wear, remaining, initial);
            game.Orders.ApplyRemoteDelivery(order, summary, elapsedSeconds);
            RemoteDeliveries++;
        }
    }
}
