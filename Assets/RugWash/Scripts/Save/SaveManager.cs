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
        [SerializeField] ShopManager shop;
        [Tooltip("Robot aspirateur, plan de travail et sol.")]
        [SerializeField] WorkshopStateService workshop;
        [SerializeField] Player.ProductInventory products;
        [SerializeField] RugDex.RugDexService rugDex;

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
            }
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
            }
        }

        void ApplyLoadedData()
        {
            settings.Apply(Current.settings);
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
                if (!GuestCareerMode) orders.Book.MarkCompleted(Current.completedOrders);
            }
            applied = true;
        }

        /// <summary>
        /// Multijoueur, côté invité : la progression de carrière reste celle de CE joueur (argent, réputation, améliorations,
        /// produits, RugDex, commandes livrées), validée par l'hôte puis sauvegardée ici. L'atelier de l'hôte (aire de
        /// lavage, sol, robot, tableau des commandes) n'est jamais écrit dans la sauvegarde de l'invité.
        /// </summary>
        public bool GuestCareerMode { get; set; }

        /// <summary>Recopie l'état courant des systèmes dans les données à sauvegarder.</summary>
        void Capture()
        {
            Current.settings = settings.Current.Clone();
            if (workshop != null && !GuestCareerMode) workshop.Capture(Current.workshop ??= new WorkshopSaveData());
            if (products != null)
            {
                Current.products = new System.Collections.Generic.List<ProductStockData>();
                foreach (var entry in products.Snapshot()) Current.products.Add(new ProductStockData { id = entry.Key, liters = entry.Value });
                Current.activeProduct = products.Active != null ? products.Active.id : string.Empty;
            }
            if (rugDex != null) Current.rugDex = rugDex.Book.Snapshot();
            if (!HasGameSystems) return;
            Current.money = wallet.Balance;
            Current.reputation = reputation.Points;
            Current.upgrades = new System.Collections.Generic.List<string>(upgrades.OwnedIds);
            // Invité : ses commandes livrées sont ajoutées à sa liste (OnOrderFinished), le tableau de l'hôte n'est pas copié.
            if (!GuestCareerMode) Current.completedOrders = new System.Collections.Generic.List<string>(orders.Book.CompletedIds());
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
            if (result.Accepted)
            {
                Current.ordersDelivered++;
                Current.totalEarned += result.Reward;
                // Invité : la commande de session livrée entre dans SON historique de carrière.
                if (GuestCareerMode && result.Order != null && !Current.completedOrders.Contains(result.Order.id))
                    Current.completedOrders.Add(result.Order.id);
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
