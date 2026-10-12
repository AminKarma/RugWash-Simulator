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
    /// <item>Surfaces (tapis, aire de lavage, sol) : l'hôte fait autorité ; les invités envoient leurs opérations de
    /// nettoyage et reçoivent les cellules modifiées (voir <see cref="SurfaceSyncHost"/> / <see cref="SurfaceSyncClient"/>).</item>
    /// <item>Commandes, argent, réputation, améliorations, produits, robot : exécutés par l'hôte uniquement ; les invités
    /// envoient des demandes et reçoivent l'état (variables réseau). Une livraison n'est validée et payée qu'une fois, par l'hôte.</item>
    /// <item>Joueurs : l'hôte fait apparaître la silhouette de chaque joueur une fois sa scène synchronisée.</item>
    /// </list>
    /// L'argent et la réputation sont ceux de l'atelier de l'hôte (partagés) ; un invité ne sauvegarde jamais sa progression.
    /// </summary>
    public sealed class NetworkWorkshop : NetworkBehaviour, IWorkshopRequests
    {
        public const string OperationMessage = "rugwash.op";
        public const string SurfaceMessageName = "rugwash.surface";
        public const byte RugId = 0;
        public const byte WorkSurfaceId = 1;
        public const byte FloorId = 2;

        readonly NetworkVariable<int> money = new NetworkVariable<int>();
        readonly NetworkVariable<int> reputation = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString512Bytes> upgrades = new NetworkVariable<FixedString512Bytes>();
        readonly NetworkVariable<FixedString512Bytes> products = new NetworkVariable<FixedString512Bytes>();
        readonly NetworkVariable<FixedString512Bytes> completedOrders = new NetworkVariable<FixedString512Bytes>();
        readonly NetworkVariable<FixedString64Bytes> activeOrder = new NetworkVariable<FixedString64Bytes>();
        readonly NetworkVariable<float> elapsed = new NetworkVariable<float>();
        readonly NetworkVariable<RobotNetState> robotState = new NetworkVariable<RobotNetState>();

        GameBootstrap game;
        MultiplayerSession session;
        SurfaceSyncHost host;
        SurfaceSyncClient client;
        readonly ByteWriter writer = new ByteWriter(4096);
        readonly List<ulong> syncedClients = new List<ulong>();
        byte[] receiveBuffer = new byte[1024];
        float sendTimer, robotTimer, slowTimer;
        float sendInterval = 1f / 15f;

        public static NetworkWorkshop Instance { get; private set; }
        public SurfaceSyncHost HostSync => host;
        public SurfaceSyncClient ClientSync => client;
        public GameBootstrap Game => game;
        /// <summary>Octets de cellules envoyés par l'hôte depuis le début (mesure de la bande passante).</summary>
        public long SurfaceBytesSent { get; private set; }
        public long OperationBytesSent { get; private set; }
        public int RemoteDeliveries { get; private set; }

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
            if (session != null && session.Content != null) sendInterval = 1f / Mathf.Max(5f, session.Content.surfaceSendRate);
            game.GameState.Networked = true;

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
                    game.Orders.Remote = null;
                    game.Shop.Remote = null;
                    game.Robot.Remote = null;
                    game.SaveSystem.ProgressionLocked = false;
                }
            }
            if (game != null) game.GameState.Networked = false;
        }

        RugCleaningController Surface(byte id) =>
            id == RugId ? game.Rug : id == WorkSurfaceId ? game.WorkSurface.Surface : id == FloorId ? game.Robot.Floor : null;

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
            PushEconomy();
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
            game.Wallet.Wallet.Changed += OnWalletChanged;
            game.Reputation.Changed += OnReputationChanged;
            game.Upgrades.Changed += PushEconomy;
            game.Orders.Changed += PushOrders;
            game.Orders.OrderFinished += OnHostOrderFinished;
            game.Products.Book.Changed += PushProducts;
        }

        void UnhookHostSystems()
        {
            if (game == null) return;
            game.Wallet.Wallet.Changed -= OnWalletChanged;
            game.Reputation.Changed -= OnReputationChanged;
            game.Upgrades.Changed -= PushEconomy;
            game.Orders.Changed -= PushOrders;
            game.Orders.OrderFinished -= OnHostOrderFinished;
            game.Products.Book.Changed -= PushProducts;
        }

        void OnWalletChanged(int balance, int delta) => money.Value = balance;
        void OnReputationChanged(int points) => reputation.Value = points;

        void PushEconomy()
        {
            money.Value = game.Wallet.Balance;
            reputation.Value = game.Reputation.Points;
            upgrades.Value = new FixedString512Bytes(Truncate(string.Join(",", game.Upgrades.OwnedIds), 500));
            PushProducts();
        }

        void PushProducts()
        {
            var text = new StringBuilder();
            foreach (var entry in game.Products.Snapshot())
            {
                if (text.Length > 0) text.Append(';');
                text.Append(entry.Key).Append(':').Append(entry.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
            var value = new FixedString512Bytes(Truncate(text.ToString(), 500));
            if (!products.Value.Equals(value)) products.Value = value;
        }

        void PushOrders()
        {
            var active = game.Orders.ActiveOrder;
            activeOrder.Value = new FixedString64Bytes(active != null ? Truncate(active.id, 60) : string.Empty);
            completedOrders.Value = new FixedString512Bytes(Truncate(string.Join(",", game.Orders.Book.CompletedIds()), 500));
            elapsed.Value = game.Orders.ElapsedSeconds;
        }

        static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);

        void OnClientSynchronized(ulong clientId)
        {
            if (!IsServer || clientId == NetworkManager.LocalClientId || syncedClients.Contains(clientId)) return;
            SpawnAvatar(clientId);
            // État complet des surfaces pour l'arrivant ; ensuite, il reçoit les mises à jour comme les autres.
            for (int i = 0; i < host.SurfaceCount; i++)
            {
                writer.Reset();
                host.WriteSnapshot(i, writer);
                SendSurface(writer, clientId);
            }
            syncedClients.Add(clientId);
            ToastRpc($"{session?.NameOf(clientId) ?? "Un joueur"} a rejoint l'atelier.");
        }

        void OnClientLeft(ulong clientId)
        {
            if (!IsServer || clientId == NetworkManager.LocalClientId) return;
            syncedClients.Remove(clientId);
            host?.Acks.Remove(clientId);
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
            // Produit spécialisé pulvérisé par un invité : prélevé sur le stock partagé de l'atelier.
            if (applied.Product != ProductTable.Standard)
            {
                var product = game.Rug.Products.Get(applied.Product);
                if (product != null) game.Products.Book.ConsumeFrom(product, product.litersPerSecond * applied.DeltaTime);
            }
        }

        byte ProductFilter(byte id)
        {
            if (id == ProductTable.Standard) return id;
            var product = game.Rug.Products.Get(id);
            return product != null && game.Products.Book.StockOf(product) > 0f ? id : ProductTable.Standard;
        }

        void Update()
        {
            if (game == null) return;
            if (IsServer && host != null) HostTick();
        }

        void HostTick()
        {
            float dt = Time.unscaledDeltaTime;
            sendTimer += dt;
            if (sendTimer >= sendInterval)
            {
                sendTimer = 0f;
                for (int i = 0; i < host.SurfaceCount; i++)
                {
                    writer.Reset();
                    if (!host.TryWriteUpdate(i, writer)) continue;
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
                PushProducts();
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
            var summary = game.Rug.Grid.ComputeSummary();
            var order = game.Orders.Book.Find(activeOrder.Value.ToString()) ?? FindLastCompleted();
            if (order == null) return;
            DeliveredRpc(new FixedString64Bytes(order.id), summary.Cleanliness01, summary.CleanCellRatio01, summary.AverageMoisture01,
                summary.AverageFoam01, summary.AverageResidue01, summary.AverageWear01, summary.RemainingDirt, summary.InitialDirt,
                game.Orders.ElapsedSeconds);
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
            game.SaveSystem.ProgressionLocked = true;

            money.OnValueChanged += (_, value) => game.Wallet.Wallet.Restore(value);
            reputation.OnValueChanged += (_, value) => game.Reputation.Restore(value);
            upgrades.OnValueChanged += (_, value) => ApplyUpgrades(value.ToString());
            products.OnValueChanged += (_, value) => ApplyProducts(value.ToString());
            completedOrders.OnValueChanged += (_, value) => game.Orders.ApplyRemoteCompleted(Split(value.ToString()));
            activeOrder.OnValueChanged += (_, value) => ApplyActiveOrder(value.ToString());
            elapsed.OnValueChanged += (_, value) => game.Orders.ApplyRemoteElapsed(value);
            robotState.OnValueChanged += (_, value) => ApplyRobot(value);

            game.Wallet.Wallet.Restore(money.Value);
            game.Reputation.Restore(reputation.Value);
            ApplyUpgrades(upgrades.Value.ToString());
            ApplyProducts(products.Value.ToString());
            game.Orders.ApplyRemoteCompleted(Split(completedOrders.Value.ToString()));
            ApplyActiveOrder(activeOrder.Value.ToString());
            ApplyRobot(robotState.Value);
        }

        static string[] Split(string text) => string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split(',');

        void ApplyUpgrades(string text) => game.Upgrades.Restore(Split(text));

        void ApplyProducts(string text)
        {
            var entries = new List<KeyValuePair<string, float>>();
            if (!string.IsNullOrEmpty(text))
                foreach (var part in text.Split(';'))
                {
                    int colon = part.LastIndexOf(':');
                    if (colon <= 0) continue;
                    if (float.TryParse(part.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float liters))
                        entries.Add(new KeyValuePair<string, float>(part.Substring(0, colon), liters));
                }
            var active = game.Products.Book.Active;
            game.Products.Restore(entries, active != null ? active.id : string.Empty);
        }

        void ApplyActiveOrder(string id)
        {
            var orders = game.Orders;
            if (string.IsNullOrEmpty(id))
            {
                if (orders.ActiveOrder != null && game.GameState.State != GameState.Results) orders.ApplyRemoteReturn();
                return;
            }
            if (orders.ActiveOrder != null && orders.ActiveOrder.id == id) return;
            var order = orders.Book.Find(id);
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

        /// <summary>Recharge localement la surface que l'hôte vient de charger (même définition, même commande).</summary>
        bool LoadSurface(byte id, SurfaceIdentity identity)
        {
            var surface = Surface(id);
            if (surface == null) return false;
            if (id != RugId) return surface.Grid != null;
            var definition = FindRug(identity.DefinitionId);
            if (definition == null)
            {
                Debug.LogError($"[RugWash] Tapis « {identity.DefinitionId} » inconnu de ce poste (version différente ?).");
                return false;
            }
            var order = string.IsNullOrEmpty(identity.OrderId) ? null : game.Orders.Book.Find(identity.OrderId);
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
            client.Receive(new ArraySegment<byte>(receiveBuffer, 0, length));
        }

        // ---------------------------------------------------------------- Demandes des invités (IWorkshopRequests)

        public void RequestStartOrder(string orderId) => StartOrderRpc(new FixedString64Bytes(orderId ?? string.Empty));
        public void RequestDeliver() => DeliverRpc();
        public void RequestAbandon() => AbandonRpc();
        public void RequestUpgrade(string upgradeId) => UpgradeRpc(new FixedString64Bytes(upgradeId ?? string.Empty));
        public void RequestProduct(string productId) => ProductRpc(new FixedString64Bytes(productId ?? string.Empty));
        public void RequestRobotToggle() => RobotToggleRpc();

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
            var result = game.Shop.TryPurchase(upgrade);
            ShopResultRpc(result == PurchaseResult.Purchased ? $"Achat effectué : {upgrade.displayName}" : ShopRules.Describe(result),
                result == PurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
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
            ShopResultRpc(result == ProductPurchaseResult.Purchased ? $"Achat effectué : {product.displayName}" : ShopRules.Describe(result),
                result == ProductPurchaseResult.Purchased, RpcTarget.Single(sender, RpcTargetUse.Temp));
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
            var order = game.Orders.Book.Find(orderId.ToString());
            if (order == null) return;
            var summary = new RugCleaningSummary(cleanliness, cleanCells, moisture, foam, residue, wear, remaining, initial);
            game.Orders.ApplyRemoteDelivery(order, summary, elapsedSeconds);
            RemoteDeliveries++;
        }
    }
}
