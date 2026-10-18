using System;
using System.Collections.Generic;
using RugWash.RugDex;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>Paramètres du joueur (persistés avec la progression).</summary>
    [Serializable]
    public sealed class SettingsData
    {
        public float masterVolume = 0.9f;
        public float effectsVolume = 0.8f;
        public float interfaceVolume = 0.7f;
        /// <summary>Musique de fond (v0.1.2), plafonnée sous les sons du jeu.</summary>
        public float musicVolume = 0.6f;
        public float mouseSensitivity = 0.1f;
        public bool invertY;
        /// <summary>Intensité du balancement caméra/outils (0 = désactivé).</summary>
        public float motionIntensity = 1f;
        /// <summary>-1 = niveau de qualité par défaut du projet ; sinon l'un des quatre préréglages (Très bas, Bas, Moyen, Ultra).</summary>
        public int qualityLevel = -1;
        /// <summary>Synchronisation verticale (grande mise à jour) : indépendante du niveau de qualité.</summary>
        public bool vSync = true;
        /// <summary>Limite d'images par seconde sans synchronisation verticale (0 = illimitée).</summary>
        public int frameRateCap;
        /// <summary>Effets de post-traitement de la caméra (bloom, vignette, étalonnage).</summary>
        public bool postProcessing = true;
        /// <summary>0 = résolution actuelle de l'écran.</summary>
        public int resolutionWidth;
        public int resolutionHeight;
        /// <summary>-1 = mode d'affichage actuel ; sinon valeur de <see cref="FullScreenMode"/>.</summary>
        public int fullScreenMode = -1;
        /// <summary>Liaisons de touches modifiées (format JSON de l'Input System).</summary>
        public string bindingOverrides = string.Empty;
        /// <summary>Vitesse de la caméra au stick droit poussé à fond (degrés par seconde).</summary>
        public float gamepadLookSpeed = 150f;
        /// <summary>Zone morte radiale des sticks (0..1).</summary>
        public float stickDeadZone = 0.15f;
        /// <summary>Intensité des vibrations de la manette (0 = désactivées).</summary>
        public float vibration = 1f;
        /// <summary>Micro du chat vocal en ligne (0 appuyer pour parler, 1 toujours ouvert, 2 coupé).</summary>
        public int voiceMode;
        /// <summary>Volume des voix des autres joueurs (0 = coupées).</summary>
        public float voiceVolume = 1f;

        public SettingsData Clone() => (SettingsData)MemberwiseClone();
    }

    /// <summary>
    /// Fiche d'un joueur dans la sauvegarde d'une partie en ligne (V0.2.5), retrouvée par son identité de joueur à chaque
    /// arrivée : ses achats personnels (outils, améliorations d'outils, équipement, gadgets), ce qu'il porte et sa réserve de
    /// détergent. Rien de cette fiche n'est écrit chez le joueur : elle appartient à la sauvegarde de l'hôte.
    /// </summary>
    [Serializable]
    public sealed class SessionMemberData
    {
        /// <summary>Identité du joueur (Steam ou clé locale), jamais vide.</summary>
        public string key = string.Empty;
        /// <summary>Dernier nom affiché (repère dans la sauvegarde).</summary>
        public string name = string.Empty;
        public List<string> upgrades = new List<string>();
        public List<string> carried = new List<string>();
        /// <summary>Réserve de détergent emportée (litres) ; -1 = pleine (premier passage).</summary>
        public float soapReserve = -1f;

        public SessionMemberData Clone() => new SessionMemberData
        {
            key = key, name = name, upgrades = new List<string>(upgrades ?? new List<string>()),
            carried = new List<string>(carried ?? new List<string>()), soapReserve = soapReserve,
        };
    }

    /// <summary>Stock d'un produit spécialisé (litres).</summary>
    [Serializable]
    public sealed class ProductStockData
    {
        public string id = string.Empty;
        public float liters;
    }

    /// <summary>État de l'atelier : robot aspirateur, plan de travail et sol (grilles compressées, voir SurfaceStateCodec).</summary>
    [Serializable]
    public sealed class WorkshopSaveData
    {
        public bool hasRobot;
        public int robotMode;
        public float robotBattery = 1f;
        public bool robotResume;
        public float robotX;
        public float robotZ;
        public float robotYaw;
        /// <summary>Grille du plan de travail encodée (vide = propre).</summary>
        public string workSurface = string.Empty;
        /// <summary>Grille du sol encodée (vide = état initial).</summary>
        public string floor = string.Empty;
        /// <summary>Aire de lavage de la zone 2 (agrandissement, grande mise à jour) ; vide = propre.</summary>
        public string workSurface2 = string.Empty;
        /// <summary>Machine à laver : tapis en cours de lavage et temps restant (grande mise à jour).</summary>
        public Workshop.WasherSaveData washer = new Workshop.WasherSaveData();
        /// <summary>Objets tombés au sol (V0.2.5, parties en ligne) : ils y restent jusqu'à ce qu'un joueur les ramasse.</summary>
        public List<DroppedItemData> drops = new List<DroppedItemData>();
    }

    /// <summary>Objet tombé au sol (coup de batte en partie en ligne), sans propriétaire tant qu'il n'est pas ramassé.</summary>
    [Serializable]
    public sealed class DroppedItemData
    {
        /// <summary>Achat que représente l'objet (outil ou gadget).</summary>
        public string upgrade = string.Empty;
        public float x;
        public float y;
        public float z;
        public float yaw;
    }

    /// <summary>
    /// Données persistantes d'un emplacement de sauvegarde (<see cref="SaveSlot"/>), au format JSON versionné : la
    /// progression jouée sur cet emplacement (argent, réputation, achats, atelier, missions…). Le niveau, les paramètres
    /// et l'apparence du joueur sont dans son profil (<see cref="PlayerProfile"/>), commun à tous les emplacements ;
    /// <see cref="settings"/> et <see cref="character"/> ne servent plus qu'à migrer une ancienne sauvegarde.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int version = SaveMigration.CurrentVersion;
        public string profileId = "profile0";
        public string savedAtUtc = string.Empty;

        /// <summary>Mode de l'emplacement (« solo » / « multi ») ; vide : ancienne sauvegarde, rattachée à l'emplacement qui la charge.</summary>
        public string mode = string.Empty;
        public int slotIndex;
        /// <summary>Temps de jeu cumulé sur cet emplacement (secondes passées dans l'atelier).</summary>
        public double playSeconds;
        /// <summary>
        /// Emplacement multijoueur hébergé (V0.2.5) : fiche de chaque joueur venu dans cette partie (ses outils et leurs
        /// améliorations). L'argent, la réputation et l'atelier de l'emplacement sont communs à la session.
        /// </summary>
        public List<SessionMemberData> members = new List<SessionMemberData>();
        /// <summary>Inventaire porté (grande mise à jour : 4 emplacements, outils et gadgets) ; le reste est au casier.</summary>
        public List<string> carried = new List<string>();

        public int money;
        public int reputation;
        public List<string> upgrades = new List<string>();
        public List<string> completedOrders = new List<string>();
        public int ordersDelivered;
        public int totalEarned;

        public SettingsData settings = new SettingsData();
        public WorkshopSaveData workshop = new WorkshopSaveData();
        /// <summary>Produits spécialisés en stock et produit sélectionné (vide = détergent standard).</summary>
        public List<ProductStockData> products = new List<ProductStockData>();
        public string activeProduct = string.Empty;
        /// <summary>Progression du RugDex.</summary>
        public List<RugDexRecord> rugDex = new List<RugDexRecord>();

        /// <summary>Réserve de détergent emportée (litres) ; -1 = pleine (nouvelle partie).</summary>
        public float soapReserve = -1f;
        /// <summary>Stock de détergent en bidons (litres), qui sert à remplir la réserve au poste détergent.</summary>
        public float detergentStock = SaveMigration.StarterDetergentStock;

        /// <summary>Missions générées : graine (0 = à tirer), numéro de la prochaine mission, offres affichées, dernière livrée.</summary>
        /// <summary>Personnalisation du personnage (v0.1.2), propre à ce joueur.</summary>
        public Player.CharacterLook character = Player.CharacterPalette.Default;

        public int missionSeed;
        public int missionSequence;
        public List<string> missionOffers = new List<string>();
        public string lastMission = string.Empty;

        /// <summary>Vrai si le joueur a commencé à progresser (sert à « Continuer » et à confirmer « Nouvelle partie »).</summary>
        public bool HasProgress => money > 0 || reputation > 0 || ordersDelivered > 0 || upgrades.Count > 0 || completedOrders.Count > 0;

        /// <summary>Remet la progression à zéro en conservant les paramètres.</summary>
        public SaveData FreshProgressKeepingSettings() => new SaveData { profileId = profileId, settings = settings.Clone() };
    }

    /// <summary>Validation avant écriture : une sauvegarde invalide n'est jamais écrite.</summary>
    public static class SaveValidator
    {
        public const int MaxMoney = 1_000_000_000;

        public static bool IsValid(SaveData data, out string reason)
        {
            reason = null;
            if (data == null) reason = "données absentes";
            else if (data.version <= 0 || data.version > SaveMigration.CurrentVersion) reason = $"version {data.version} invalide";
            else if (string.IsNullOrWhiteSpace(data.profileId)) reason = "profil sans identifiant";
            else if (data.money < 0 || data.money > MaxMoney) reason = $"solde invalide ({data.money})";
            else if (data.reputation < 0) reason = "réputation négative";
            else if (data.ordersDelivered < 0 || data.totalEarned < 0) reason = "statistiques négatives";
            else if (data.upgrades == null || data.completedOrders == null || data.settings == null) reason = "liste absente";
            else if (data.upgrades.Exists(string.IsNullOrWhiteSpace) || data.completedOrders.Exists(string.IsNullOrWhiteSpace)) reason = "identifiant vide";
            else if (!string.IsNullOrEmpty(data.mode) && !SaveSlot.TryParseMode(data.mode, out _)) reason = $"mode inconnu ({data.mode})";
            else if (double.IsNaN(data.playSeconds) || data.playSeconds < 0) reason = "temps de jeu invalide";
            return reason == null;
        }

        /// <summary>Ramène les paramètres dans des bornes jouables (valeurs éditées à la main, anciennes versions).</summary>
        public static void ClampSettings(SettingsData s)
        {
            s.masterVolume = Mathf.Clamp01(s.masterVolume);
            s.effectsVolume = Mathf.Clamp01(s.effectsVolume);
            s.interfaceVolume = Mathf.Clamp01(s.interfaceVolume);
            s.musicVolume = Mathf.Clamp01(s.musicVolume);
            s.mouseSensitivity = Mathf.Clamp(s.mouseSensitivity, 0.01f, 0.5f);
            s.motionIntensity = Mathf.Clamp01(s.motionIntensity);
            if (s.bindingOverrides == null) s.bindingOverrides = string.Empty;
            s.gamepadLookSpeed = Mathf.Clamp(s.gamepadLookSpeed, 30f, 360f);
            s.stickDeadZone = Mathf.Clamp(s.stickDeadZone, 0.02f, 0.5f);
            s.vibration = Mathf.Clamp01(s.vibration);
            if (System.Array.IndexOf(SettingsRules.FrameRateCaps, s.frameRateCap) < 0) s.frameRateCap = 0;
            s.voiceMode = Mathf.Clamp(s.voiceMode, 0, 2);
            s.voiceVolume = Mathf.Clamp01(s.voiceVolume);
        }
    }
}
