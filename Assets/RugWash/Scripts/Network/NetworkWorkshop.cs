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

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Mode);
            serializer.SerializeValue(ref Battery);
            serializer.SerializeValue(ref Resume);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Z);
            serializer.SerializeValue(ref Yaw);
        }

        public bool Equals(RobotNetState other) =>
            Mode == other.Mode && Battery == other.Battery && Resume == other.Resume &&
            Mathf.Abs(X - other.X) < 0.005f && Mathf.Abs(Z - other.Z) < 0.005f && Mathf.Abs(Mathf.DeltaAngle(Yaw, other.Yaw)) < 0.5f;
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
    public sealed class NetworkWorkshop : NetworkBehaviour, IWorkshopRequests
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
        float agingSyncInterval = 2f;

        // Hôte : carrière de chaque invité (validée), invités dont la carrière a changé (envoi groupé), expéditeur en cours.
        readonly Dictionary<ulong, PlayerCareer> careers = new Dictionary<ulong, PlayerCareer>();
        readonly HashSet<ulong> careerDirty = new HashSet<ulong>();
        readonly List<ulong> careerFlush = new List<ulong>();
        ulong operationSender;
        // Invité : carrière validée reçue de l'hôte ; annonce renvoyée tant qu'aucune réponse n'arrive.
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

        /// <summary>Hôte : copie de la carrière tenue pour un invité (diagnostic, tests). Null si inconnue.</summary>
        public PlayerCareer CareerOf(ulong clientId) => careers.TryGetValue(clientId, out var career) ? career.Clone() : null;

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
                host?.Unregister();
            }
            else
            {
                messaging?.UnregisterNamedMessageHandler(SurfaceMessageName);
                client?.Unregister();
                if (game != null)
                {
                    // Fin de partie pour l'invité : sa carrière (validée par l'hôte) est sauvegardée chez lui.
                    if (careerReceived) game.SaveSystem.SaveNow("fin de la partie en ligne");
                    game.Orders.Remote = null;
                    game.Shop.Remote = null;
                    game.Robot.Remote = null;
                    game.Detergent.Remote = null;
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
        }

        void UnhookHostSystems()
        {
            if (game == null) return;
            game.Reputation.Changed -= OnReputationChanged;
            game.Orders.Changed -= PushOrders;
            game.Orders.OrderFinished -= OnHostOrderFinished;
        }

        void OnReputationChanged(int points) => boardReputation.Value = points;

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
        /// État complet pour un invité : instantané de chaque surface, puis sa carrière si elle est connue ; ensuite, il reçoit
        /// les mises à jour comme les autres. Sans effet de bord si on le renvoie (même époque : valeurs simplement réécrites).
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

        /// <summary>Envoie à un invité sa carrière telle que l'hôte la tient. <paramref name="persist"/> : à sauvegarder aussitôt.</summary>
        void SendCareer(ulong clientId, bool persist)
        {
            if (!careers.TryGetValue(clientId, out var career)) return;
            careerDirty.Remove(clientId);
            CareerRpc(CareerCodec.Encode(career), persist, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Server)]
        void CareerHelloRpc(byte[] data, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            // Une seule annonce par invité : une annonce répétée (réponse perdue, ou tentative de se redonner de l'argent)
            // ne remplace jamais la carrière tenue par l'hôte ; elle redemande seulement la carrière actuelle.
            if (careers.ContainsKey(sender))
            {
                if (syncedClients.Contains(sender)) SendCareer(sender, persist: false);
                return;
            }
            PlayerCareer announced;
            try
            {
                announced = CareerCodec.Decode(data);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[RugWash] Carrière illisible reçue du client {sender} : carrière vierge.");
                announced = new PlayerCareer();
            }
            careers[sender] = CareerRules.Sanitize(announced, game.Upgrades.Catalog, game.Products.Book.Catalog, game.Detergent.MaxStock);
            // Réponse seulement à un invité synchronisé (sinon à la fin de sa synchronisation, OnClientSynchronized).
            if (syncedClients.Contains(sender)) SendCareer(sender, persist: false);
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
                // Simulation lente : reproduite par les invités, corrigée seulement à basse fréquence.
                bool includeSlow = agingSyncTimer >= agingSyncInterval;
                if (includeSlow) agingSyncTimer = 0f;
                for (int i = 0; i < host.SurfaceCount; i++)
                {
                    writer.Reset();
                    long timer = Diagnostics.PerfStats.Begin();
                    bool changed = host.TryWriteUpdate(i, writer, includeSlow);
                    Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.NetWrite, timer, writer.Length);
                    if (!changed) continue;
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
                    X = state.x, Z = state.z, Yaw = state.yaw,
                };
                if (!net.Equals(robotState.Value)) robotState.Value = net;
            }

            slowTimer += dt;
            if (slowTimer >= 0.5f)
            {
                slowTimer = 0f;
                if (game.Orders.ActiveOrder != null) elapsed.Value = game.Orders.ElapsedSeconds;
                // Consommations des invités : envoi groupé, au plus deux fois par seconde.
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
            if (game.Rug.Grid == null) return;
            var summary = game.Rug.Grid.ComputeSummary();
            // La commande livrée vient du résultat : une mission générée a déjà quitté le tableau (remplacée).
            var order = result.Order != null ? result.Order : game.Orders.Book.Find(activeOrder.Value.ToString()) ?? FindLastCompleted();
            if (order == null) return;
            DeliveredRpc(new FixedString64Bytes(order.id), summary.Cleanliness01, summary.CleanCellRatio01, summary.AverageMoisture01,
                summary.AverageFoam01, summary.AverageResidue01, summary.AverageWear01, summary.RemainingDirt, summary.InitialDirt,
                game.Orders.ElapsedSeconds);
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

            game.Orders.SetBoardReputation(boardReputation.Value);
            game.Orders.ApplyRemoteCompleted(Split(completedOrders.Value.ToString()));
            game.Orders.ApplyRemoteOffers(Split(missionOffers.Value.ToString()));
            ApplyActiveOrder(activeOrder.Value.ToString());
            ApplyRobot(robotState.Value);

            // Arrivée : contrôles coupés et HUD en chargement jusqu'à réception de l'état de l'hôte et de la carrière validée.
            ShowedLoading = true;
            game.GameState.Synchronizing = true;
            game.Hud.SetLoading("Connexion à l'atelier de l'hôte — synchronisation de l'atelier et de votre progression…");
            CareerHelloRpc(CareerCodec.Encode(CaptureLocalCareer()));
            StateRequestRpc();
        }

        /// <summary>
        /// Diagnostic (vérification automatique du multijoueur) : renvoie à l'hôte une annonce de carrière arbitraire, comme
        /// le ferait un client modifié. L'hôte doit l'ignorer : la carrière tenue en cours de partie ne change pas.
        /// </summary>
        public void SendForgedCareerForDiagnostics(PlayerCareer forged)
        {
            if (!IsServer && forged != null) CareerHelloRpc(CareerCodec.Encode(forged));
        }

        /// <summary>Carrière de ce joueur, lue dans ses propres systèmes (chargés depuis SA sauvegarde).</summary>
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
        void CareerRpc(byte[] data, bool persist, RpcParams rpcParams)
        {
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
            careerReceived = true;
            if (persist) game.SaveSystem.SaveNow("progression en ligne");
        }

        /// <summary>Invité : fin de la synchronisation d'arrivée quand toutes les surfaces et la carrière sont reçues.</summary>
        void GuestTick()
        {
            if (GuestReady || client == null) return;
            float dt = Time.unscaledDeltaTime;
            bool surfaces = client.IsReady(RugId) && client.IsReady(WorkSurfaceId) && client.IsReady(FloorId);
            if (!careerReceived || !surfaces)
            {
                // Réponse perdue ou partie trop tôt : nouvelle demande (l'hôte ne remplace jamais une carrière déjà tenue).
                helloTimer += dt;
                if (helloTimer >= HelloRetrySeconds)
                {
                    helloTimer = 0f;
                    if (!careerReceived) CareerHelloRpc(CareerCodec.Encode(CaptureLocalCareer()));
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
            game.Hud.ShowToast("Connecté à l'atelier de l'hôte — votre progression personnelle est conservée");
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

        void ApplyRobot(RobotNetState state)
        {
            if (game.Robot == null) return;
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
        public void RequestDetergentCan() => DetergentCanRpc();
        public void RequestDetergentRefill(float liters) => DetergentRefillRpc(liters);

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
            if (upgrade == null || !careers.TryGetValue(sender, out var career))
            {
                ShopResultRpc(upgrade == null ? "Article inconnu" : "Progression en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            // Payé avec l'argent de l'invité, validé ici : ni l'argent ni les achats de l'hôte ne sont touchés.
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
