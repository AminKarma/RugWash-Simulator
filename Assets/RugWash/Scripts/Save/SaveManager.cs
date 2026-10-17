using System;
using RugWash.Economy;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>
    /// Sauvegarde locale de la scène. Deux fichiers : le <b>profil</b> du joueur (niveau, paramètres, apparence — commun à
    /// tous ses emplacements) et l'<b>emplacement</b> choisi au menu (<see cref="SaveSlots.Selected"/> : trois en solo,
    /// trois en multijoueur, jamais mélangés). Au démarrage, charge l'un et l'autre et les applique aux systèmes présents
    /// (atelier : portefeuille, réputation, améliorations, commandes ; partout : paramètres) ; sauvegarde après chaque
    /// opération importante — jamais à chaque image. Au menu, seul le profil est écrit.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        /// <summary>Fichier de l'emplacement par défaut (Solo 1), aussi utilisé par les tests pour préparer une sauvegarde.</summary>
        public static readonly string DefaultFileName = new SaveSlot(SaveMode.Solo, 1).FileName;

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
        [Tooltip("Inventaire porté (4 emplacements) ; le reste est au casier.")]
        [SerializeField] Player.PlayerInventory inventory;

        SaveFileStore store;
        ProfileStore profileStore;
        bool applied;
        bool profileCreated;

        /// <summary>Progression de l'emplacement chargé.</summary>
        public SaveData Current { get; private set; }
        /// <summary>Profil du joueur (niveau, paramètres, apparence).</summary>
        public PlayerProfile Profile { get; private set; }
        /// <summary>Emplacement chargé (au menu : le dernier joué, pour « Continuer » et le RugDex).</summary>
        public SaveSlot Slot { get; private set; }
        public LoadStatus LastLoadStatus { get; private set; }
        public string LastLoadMessage { get; private set; }
        public string FilePath => store?.MainPath;
        public string ProfilePath => profileStore?.MainPath;
        public int SaveCount { get; private set; }

        /// <summary>(succès, message)</summary>
        public event Action<bool, string> Saved;
        /// <summary>Le joueur gagne de l'expérience : (expérience gagnée, nouveau niveau, niveau franchi).</summary>
        public event Action<int, int, bool> ExperienceGained;

        bool HasGameSystems => wallet != null && reputation != null && upgrades != null && orders != null;

        public bool IsConfigured(out string missing)
        {
            missing = settings == null ? nameof(settings) : null;
            return missing == null;
        }

        void Awake()
        {
            string directory = SaveSlots.Directory;
            var legacy = SaveSlots.MigrateLegacy(directory);
            profileStore = new ProfileStore(directory);
            bool newProfile = !profileStore.Exists;
            profileCreated = newProfile;
            Profile = profileStore.Load(out string profileMessage);
            if (profileMessage != null) Debug.LogWarning($"[RugWash] {profileMessage} ({profileStore.MainPath})");

            Slot = HasGameSystems ? ResolveGameSlot() : Profile.LastSlot(Profile.lastMode == "multi" ? SaveMode.Multiplayer : SaveMode.Solo);
            store = new SaveFileStore(directory, Slot.FileName);
            var result = store.Load();
            Current = result.Data;
            LastLoadStatus = result.Status;
            LastLoadMessage = result.Message ?? profileMessage;
            if (!string.IsNullOrEmpty(Current.mode) && Current.mode != Slot.ModeKey)
            {
                // Fichier d'un autre mode placé à la main dans cet emplacement : jamais chargé ni écrasé.
                store.ProtectFromWriting();
                LastLoadStatus = LoadStatus.NewerVersion;
                LastLoadMessage = $"{Slot.DisplayName} contient une sauvegarde d'un autre mode de jeu : elle n'est ni chargée ni modifiée.";
                Current = new SaveData();
            }
            Current.mode = Slot.ModeKey;
            Current.slotIndex = Slot.Index;
            if (result.Message != null) Debug.LogWarning($"[RugWash] {result.Message} ({store.MainPath})");

            // Premier profil : paramètres et apparence repris de l'ancienne sauvegarde (ou de l'emplacement chargé).
            if (newProfile)
            {
                var source = legacy ?? (LastLoadStatus == LoadStatus.NewProfile ? null : Current);
                if (source != null)
                {
                    Profile.settings = (source.settings ?? new SettingsData()).Clone();
                    Profile.character = source.character;
                    Profile.Normalize();
                }
            }
        }

        /// <summary>
        /// Atelier : l'emplacement choisi au menu, dans le bon mode. Une partie en ligne n'utilise jamais un emplacement
        /// solo (et inversement) : sans choix du bon mode, le dernier emplacement de ce mode est pris.
        /// </summary>
        SaveSlot ResolveGameSlot()
        {
            var session = Network.MultiplayerSession.Find();
            var mode = session != null && (session.InSession || session.IsGuest) ? SaveMode.Multiplayer : SaveMode.Solo;
            var selected = SaveSlots.Selected;
            if (selected.Mode != mode) selected = Profile.LastSlot(mode);
            SaveSlots.Selected = selected;
            return selected;
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
            if (HasGameSystems)
            {
                Profile.Remember(Slot);
                SaveProfile();
            }
            // Premier lancement (ou ancienne sauvegarde convertie) : le profil est écrit tout de suite, même au menu.
            else if (profileCreated) SaveProfile();
        }

        void Update()
        {
            // Temps de jeu de l'emplacement : passé dans l'atelier (bornes : pas de saut après une pause du processus).
            if (applied && HasGameSystems) Current.playSeconds += Mathf.Min(Time.unscaledDeltaTime, 1f);
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
            settings.Apply(Profile.settings);
            if (firstPersonAppearance != null) firstPersonAppearance.Apply(Profile.character);
            // Invité en ligne : l'atelier affiché (aire de lavage, sol, robot, machines, tableau des commandes) est celui de
            // l'hôte ; il arrive par le réseau. Seule la progression de cet emplacement est chargée.
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
                // Après les achats (qui disent ce qui est possédé) : ce qui est porté, le reste au casier.
                if (inventory != null) inventory.Restore(Current.carried);
                if (!GuestCareerMode)
                {
                    orders.Book.MarkCompleted(Current.completedOrders);
                    // Après la réputation et les outils : les nouvelles offres en dépendent ; les offres sauvegardées reviennent telles quelles.
                    orders.RestoreMissions(Current.missionSeed, Current.missionSequence, Current.missionOffers, Current.lastMission);
                    // Après les commandes : le tapis resté dans la machine à laver garde sa commande active.
                    if (workshop != null) workshop.RestoreWasher(Current.workshop);
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
        /// Multijoueur, côté invité : la progression de cet emplacement (argent, réputation, améliorations, produits, RugDex,
        /// commandes livrées) reste celle de CE joueur — copiée une seule fois depuis l'hôte à la première arrivée, puis
        /// indépendante — validée par l'hôte pendant la partie et sauvegardée ici. L'atelier de l'hôte (aire de lavage, sol,
        /// robot, machines, tableau des commandes) n'est jamais écrit dans la sauvegarde de l'invité.
        /// </summary>
        public bool GuestCareerMode { get; set; }

        /// <summary>Emplacement multijoueur pas encore initialisé : la prochaine arrivée chez un hôte copiera sa progression.</summary>
        public bool NeedsInitialSync => Slot.Mode == SaveMode.Multiplayer && !Current.multiplayerInitialized;

        /// <summary>Progression de départ copiée depuis l'hôte : marquée une fois pour toutes, puis sauvegardée.</summary>
        public void MarkMultiplayerInitialized()
        {
            if (Slot.Mode != SaveMode.Multiplayer || Current.multiplayerInitialized) return;
            Current.multiplayerInitialized = true;
            SaveNow("progression de départ copiée depuis l'hôte");
        }

        /// <summary>Recopie l'état courant des systèmes dans les données à sauvegarder.</summary>
        void Capture()
        {
            Profile.settings = settings.Current.Clone();
            if (workshop != null && !GuestCareerMode) workshop.Capture(Current.workshop ??= new WorkshopSaveData());
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
            if (inventory != null) Current.carried = inventory.Snapshot();
            Current.money = wallet.Balance;
            Current.reputation = reputation.Points;
            // Achats inconnus du catalogue de ce build (contenu pas encore généré, version plus ancienne) : conservés tels
            // quels plutôt qu'effacés — un équipement gagné ne disparaît jamais d'une sauvegarde.
            var kept = new System.Collections.Generic.List<string>(upgrades.OwnedIds);
            foreach (string id in Current.upgrades)
                if (!upgrades.IsKnown(id) && !kept.Contains(id)) kept.Add(id);
            Current.upgrades = kept;
            // Invité : ses commandes livrées sont ajoutées à sa liste (OnOrderFinished), le tableau de l'hôte n'est pas copié.
            if (GuestCareerMode) return;
            Current.completedOrders = new System.Collections.Generic.List<string>(orders.Book.CompletedIds());
            Current.completedOrders.RemoveAll(Orders.MissionKey.IsGenerated);
            if (!orders.Missions.IsInitialized) return;
            Current.missionSeed = orders.Missions.Seed;
            Current.missionSequence = orders.Missions.Sequence;
            Current.lastMission = orders.Missions.LastCompleted;
            Current.missionOffers = new System.Collections.Generic.List<string>();
            foreach (var offer in orders.Missions.Offers) Current.missionOffers.Add(offer.Id);
        }

        /// <summary>Sauvegarde l'emplacement (dans l'atelier) et le profil.</summary>
        public bool SaveNow(string reason)
        {
            if (!applied) return false;
            Capture();
            bool ok = true;
            string error = null;
            if (HasGameSystems)
            {
                ok = store.Save(Current, out error);
                if (ok) SaveCount++;
                else Debug.LogError($"[RugWash] Échec de la sauvegarde ({reason}) : {error}");
            }
            bool profileOk = SaveProfile();
            Saved?.Invoke(ok && profileOk, ok ? "Progression sauvegardée" : $"Échec de la sauvegarde : {error}");
            return ok && profileOk;
        }

        /// <summary>Écrit le profil seul (paramètres, apparence, niveau, derniers emplacements).</summary>
        public bool SaveProfile()
        {
            if (settings != null && settings.Current != null) Profile.settings = settings.Current.Clone();
            if (profileStore.Save(Profile, out string error)) return true;
            Debug.LogError($"[RugWash] Échec de l'écriture du profil : {error}");
            return false;
        }

        /// <summary>
        /// Nouvelle partie sur un emplacement : progression remise à zéro (le profil, donc le niveau, est conservé), puis
        /// sauvegarde immédiate. Sans emplacement : celui actuellement chargé.
        /// </summary>
        public bool ResetProgress() => ResetSlot(Slot);

        public bool ResetSlot(SaveSlot slot)
        {
            var fresh = new SaveData { mode = slot.ModeKey, slotIndex = slot.Index };
            var target = slot.Equals(Slot) ? store : new SaveFileStore(SaveSlots.Directory, slot.FileName);
            bool ok = target.Save(fresh, out string error);
            if (!ok) Debug.LogError($"[RugWash] Nouvelle partie non sauvegardée ({slot}) : {error}");
            else if (slot.Equals(Slot)) Current = fresh;
            return ok;
        }

        /// <summary>Ajoute de l'expérience au profil (livraison acceptée) et l'enregistre.</summary>
        public void AddExperience(int amount)
        {
            if (amount <= 0) return;
            int before = Profile.Level;
            Profile.experience = (int)Math.Min(int.MaxValue, (long)Profile.experience + amount);
            int after = Profile.Level;
            SaveProfile();
            ExperienceGained?.Invoke(amount, after, after > before);
        }

        bool settingsDirty;

        /// <summary>Les paramètres modifiés (curseurs déplacés…) ne sont écrits qu'à la fermeture de la fenêtre.</summary>
        void OnSettingsChanged() => settingsDirty = true;

        public bool SaveSettingsIfDirty()
        {
            if (!settingsDirty) return false;
            settingsDirty = false;
            return applied ? SaveProfile() : false;
        }

        void OnOrderFinished(OrderResult result)
        {
            int experience = PlayerLevelRules.ForDelivery(result.Accepted, result.Reward);
            int before = Profile.Level;
            if (result.Accepted)
            {
                Current.ordersDelivered++;
                Current.totalEarned += result.Reward;
                // Invité : la commande de session livrée entre dans SON historique.
                if (GuestCareerMode && result.Order != null && !Orders.MissionKey.IsGenerated(result.Order.id)
                    && !Current.completedOrders.Contains(result.Order.id))
                    Current.completedOrders.Add(result.Order.id);
                // Niveau du joueur (profil) : chaque joueur présent gagne l'expérience de la livraison, une fois.
                Profile.experience = (int)Math.Min(int.MaxValue, (long)Profile.experience + experience);
            }
            SaveNow("commande livrée");
            if (experience > 0) ExperienceGained?.Invoke(experience, Profile.Level, Profile.Level > before);
        }

        void OnPurchaseAttempted(UpgradeDefinition upgrade, PurchaseResult result)
        {
            if (result == PurchaseResult.Purchased) SaveNow("achat");
        }

        void OnApplicationQuit() => SaveNow("fermeture du jeu");
    }
}
