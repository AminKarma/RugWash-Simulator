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

        void Start()
        {
            ApplyLoadedData();
            settings.Changed += OnSettingsChanged;
            if (orders != null) orders.OrderFinished += OnOrderFinished;
            if (shop != null) shop.PurchaseAttempted += OnPurchaseAttempted;
        }

        void OnDestroy()
        {
            if (settings != null) settings.Changed -= OnSettingsChanged;
            if (orders != null) orders.OrderFinished -= OnOrderFinished;
            if (shop != null) shop.PurchaseAttempted -= OnPurchaseAttempted;
        }

        void ApplyLoadedData()
        {
            settings.Apply(Current.settings);
            if (workshop != null) workshop.Restore(Current.workshop);
            if (HasGameSystems)
            {
                wallet.Wallet.Restore(Current.money);
                reputation.Restore(Current.reputation);
                upgrades.Restore(Current.upgrades);
                orders.Book.MarkCompleted(Current.completedOrders);
            }
            applied = true;
        }

        /// <summary>Recopie l'état courant des systèmes dans les données à sauvegarder.</summary>
        void Capture()
        {
            Current.settings = settings.Current.Clone();
            if (workshop != null) workshop.Capture(Current.workshop ??= new WorkshopSaveData());
            if (!HasGameSystems) return;
            Current.money = wallet.Balance;
            Current.reputation = reputation.Points;
            Current.upgrades = new System.Collections.Generic.List<string>(upgrades.OwnedIds);
            Current.completedOrders = new System.Collections.Generic.List<string>(orders.Book.CompletedIds());
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
