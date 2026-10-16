using System;
using RugWash.Economy;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>
    /// Sauvegarde locale de la scène : charge le profil au démarrage, l'applique aux systèmes présents
    /// (atelier : portefeuille, réputation, améliorations, commandes terminées ; partout : paramètres),
    /// et sauvegarde après chaque opération importante — jamais à chaque frame.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        public const string DefaultFileName = "rugwash_save.json";

        /// <summary>Dossier de sauvegarde forcé (tests automatisés uniquement). Null = Application.persistentDataPath.</summary>
        public static string DirectoryOverride;

        [SerializeField] SettingsManager settings;
        [Header("Atelier (optionnel dans le menu)")]
        [SerializeField] WalletManager wallet;
        [SerializeField] ReputationManager reputation;
        [SerializeField] UpgradeManager upgrades;
        [SerializeField] OrderManager orders;
        [Tooltip("Gants de la vue à la première personne : couleur choisie dans « Personnage » (optionnel).")]
        [SerializeField] Player.CharacterAppearance firstPersonAppearance;
        [SerializeField] ShopManager shop;
        [Tooltip("Robot aspirateur, plan de travail et sol.")]
        [SerializeField] WorkshopStateService workshop;
        [SerializeField] Player.ProductInventory products;
        [SerializeField] RugDex.RugDexService rugDex;
        [Tooltip("Réservoirs du joueur (réserve de détergent sauvegardée).")]
        [SerializeField] Player.PlayerEquipment equipment;
        [Tooltip("Stock de détergent en bidons.")]
        [SerializeField] Player.DetergentSupply detergent;

        SaveFileStore store;
        bool applied;

        public SaveData Current { get; private set; }
        public LoadStatus LastLoadStatus { get; private set; }
        public string LastLoadMessage { get; private set; }
        public string FilePath => store?.MainPath;
        public int SaveCount { get; private set; }

        /// <summary>(succès, message)</summary>
        public event Action<bool, string> Saved;

        bool HasGameSystems => wallet != null && reputation != null && upgrades != null && orders != null;

        public bool IsConfigured(out string missing)
        {
            missing = settings == null ? nameof(settings) : null;
            return missing == null;
        }

        void Awake()
        {
            string directory = string.IsNullOrEmpty(DirectoryOverride) ? Application.persistentDataPath : DirectoryOverride;
            store = new SaveFileStore(directory, DefaultFileName);
            var result = store.Load();
            Current = result.Data;
            LastLoadStatus = result.Status;
            LastLoadMessage = result.Message;
            if (result.Message != null) Debug.LogWarning($"[RugWash] {result.Message} ({store.MainPath})");
        }

        /// <summary>
        /// Applique la sauvegarde chargée si ce n'est pas déjà fait (sans effet ensuite). En ligne, l'atelier réseau peut
        /// apparaître avant le Start de la scène : il appelle cette méthode avant de lire la carrière du joueur.
        /// </summary>
        public void EnsureApplied()
        {
            if (!applied) ApplyLoadedData();
        }

        void Start()
        {
            EnsureApplied();
            settings.Changed += OnSettingsChanged;
            if (orders != null) orders.OrderFinished += OnOrderFinished;
            if (shop != null)
            {
                shop.PurchaseAttempted += OnPurchaseAttempted;
                shop.ProductPurchaseAttempted += OnProductPurchaseAttempted;
                shop.DetergentPurchaseAttempted += OnDetergentPurchaseAttempted;
            }
        }

        void OnDetergentPurchaseAttempted(ProductPurchaseResult result)
        {
            if (result == ProductPurchaseResult.Purchased) SaveNow("achat de détergent");
        }

        void OnProductPurchaseAttempted(Cleaning.ProductDefinition product, ProductPurchaseResult result)
        {
            if (result == ProductPurchaseResult.Purchased) SaveNow("achat de produit");
        }

        void OnDestroy()
        {
            if (settings != null) settings.Changed -= OnSettingsChanged;
            if (orders != null) orders.OrderFinished -= OnOrderFinished;
            if (shop != null)
            {
                shop.PurchaseAttempted -= OnPurchaseAttempted;
                shop.ProductPurchaseAttempted -= OnProductPurchaseAttempted;
                shop.DetergentPurchaseAttempted -= OnDetergentPurchaseAttempted;
            }
        }

        void ApplyLoadedData()
        {
            settings.Apply(Current.settings);
            if (firstPersonAppearance != null) firstPersonAppearance.Apply(Current.character);
            // Invité en ligne : l'atelier affiché (aire de lavage, sol, robot, tableau des commandes) est celui de l'hôte ;
            // il arrive par le réseau. Seule la carrière personnelle de ce joueur est chargée.
            var session = Network.MultiplayerSession.Find();
            GuestCareerMode = session != null && session.IsGuest;
            if (workshop != null && !GuestCareerMode) workshop.Restore(Current.workshop);
            if (products != null)
            {
                var entries = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
                foreach (var stock in Current.products) entries.Add(new System.Collections.Generic.KeyValuePair<string, float>(stock.id, stock.liters));
                products.Restore(entries, Current.activeProduct);
            }
            if (rugDex != null)
            {
                rugDex.Book.Restore(Current.rugDex);
                rugDex.DiscoverCurrentRug();
            }
            if (HasGameSystems)
            {
                wallet.Wallet.Restore(Current.money);
                reputation.Restore(Current.reputation);
                upgrades.Restore(Current.upgrades);
                if (!GuestCareerMode)
                {
                    orders.Book.MarkCompleted(Current.completedOrders);
                    // Après la réputation et les outils : les nouvelles offres en dépendent ; les offres sauvegardées reviennent telles quelles.
                    orders.RestoreMissions(Current.missionSeed, Current.missionSequence, Current.missionOffers, Current.lastMission);
                }
            }
            // Détergent : stock en bidons et réserve emportée (après les améliorations, qui fixent la capacité de la réserve).
            if (detergent != null) detergent.Restore(Current.detergentStock);
            if (equipment != null && Current.soapReserve >= 0f)
            {
                equipment.Soap.Empty();
                equipment.Soap.Add(Current.soapReserve);
            }
            applied = true;
        }

        /// <summary>
        /// Multijoueur, côté invité (règle des sauvegardes) : pendant la session, ce joueur utilise l'économie de l'hôte
        /// (argent, réputation, achats, produits, détergent) et l'atelier de l'hôte. Rien de tout cela n'est écrit dans SA
        /// sauvegarde : seules ses préférences (paramètres) le sont. Sa progression personnelle reste celle du disque et
        /// revient telle quelle au prochain chargement — même après un crash ou une déconnexion.
        /// </summary>
        public bool GuestCareerMode { get; set; }

        /// <summary>Recopie l'état courant des systèmes dans les données à sauvegarder.</summary>
        void Capture()
        {
            Current.settings = settings.Current.Clone();
            // Invité en ligne : la progression personnelle (chargée du disque, jamais modifiée) est réécrite telle quelle.
            if (GuestCareerMode) return;
            if (workshop != null) workshop.Capture(Current.workshop ??= new WorkshopSaveData());
            if (products != null)
            {
                Current.products = new System.Collections.Generic.List<ProductStockData>();
                foreach (var entry in products.Snapshot()) Current.products.Add(new ProductStockData { id = entry.Key, liters = entry.Value });
                Current.activeProduct = products.Active != null ? products.Active.id : string.Empty;
            }
            if (rugDex != null) Current.rugDex = rugDex.Book.Snapshot();
            if (detergent != null) Current.detergentStock = detergent.Stock;
            if (equipment != null) Current.soapReserve = equipment.Soap.Amount;
            if (!HasGameSystems) return;
            Current.money = wallet.Balance;
            Current.reputation = reputation.Points;
            // Achats inconnus du catalogue de ce build (contenu pas encore généré, version plus ancienne) : conservés tels
            // quels plutôt qu'effacés — un équipement gagné ne disparaît jamais d'une sauvegarde.
            var kept = new System.Collections.Generic.List<string>(upgrades.OwnedIds);
            foreach (string id in Current.upgrades)
                if (!upgrades.IsKnown(id) && !kept.Contains(id)) kept.Add(id);
            Current.upgrades = kept;
            Current.completedOrders = new System.Collections.Generic.List<string>(orders.Book.CompletedIds());
            Current.completedOrders.RemoveAll(Orders.MissionKey.IsGenerated);
            if (!orders.Missions.IsInitialized) return;
            Current.missionSeed = orders.Missions.Seed;
            Current.missionSequence = orders.Missions.Sequence;
            Current.lastMission = orders.Missions.LastCompleted;
            Current.missionOffers = new System.Collections.Generic.List<string>();
            foreach (var offer in orders.Missions.Offers) Current.missionOffers.Add(offer.Id);
        }

        public bool SaveNow(string reason)
        {
            if (!applied) return false;
            Capture();
            bool ok = store.Save(Current, out string error);
            if (ok) SaveCount++;
            else Debug.LogError($"[RugWash] Échec de la sauvegarde ({reason}) : {error}");
            Saved?.Invoke(ok, ok ? "Progression sauvegardée" : $"Échec de la sauvegarde : {error}");
            return ok;
        }

        /// <summary>Nouvelle partie : progression remise à zéro, paramètres conservés, puis sauvegarde immédiate.</summary>
        public bool ResetProgress()
        {
            Current = Current.FreshProgressKeepingSettings();
            Current.settings = settings.Current.Clone();
            bool ok = store.Save(Current, out string error);
            if (!ok) Debug.LogError($"[RugWash] Nouvelle partie non sauvegardée : {error}");
            return ok;
        }

        bool settingsDirty;

        /// <summary>Les paramètres modifiés (curseurs déplacés…) ne sont écrits qu'à la fermeture de la fenêtre.</summary>
        void OnSettingsChanged() => settingsDirty = true;

        public bool SaveSettingsIfDirty()
        {
            if (!settingsDirty) return false;
            settingsDirty = false;
            return SaveNow("paramètres");
        }

        void OnOrderFinished(OrderResult result)
        {
            // Invité : la livraison appartient à la session de l'hôte (payée et sauvegardée chez lui).
            if (GuestCareerMode) return;
            if (result.Accepted)
            {
                Current.ordersDelivered++;
                Current.totalEarned += result.Reward;
            }
            SaveNow("commande livrée");
        }

        void OnPurchaseAttempted(UpgradeDefinition upgrade, PurchaseResult result)
        {
            if (result == PurchaseResult.Purchased) SaveNow("achat");
        }

        void OnApplicationQuit() => SaveNow("fermeture du jeu");
    }
}
