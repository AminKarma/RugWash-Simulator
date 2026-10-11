using System;
using System.Collections.Generic;
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
        public float mouseSensitivity = 0.1f;
        public bool invertY;
        /// <summary>Intensité du balancement caméra/outils (0 = désactivé).</summary>
        public float motionIntensity = 1f;
        /// <summary>-1 = niveau de qualité par défaut du projet.</summary>
        public int qualityLevel = -1;
        /// <summary>0 = résolution actuelle de l'écran.</summary>
        public int resolutionWidth;
        public int resolutionHeight;
        /// <summary>-1 = mode d'affichage actuel ; sinon valeur de <see cref="FullScreenMode"/>.</summary>
        public int fullScreenMode = -1;
        /// <summary>Liaisons de touches modifiées (format JSON de l'Input System).</summary>
        public string bindingOverrides = string.Empty;

        public SettingsData Clone() => (SettingsData)MemberwiseClone();
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
    }

    /// <summary>
    /// Données persistantes, au format JSON versionné. Aucune donnée visuelle ou de scène :
    /// uniquement la progression du joueur et ses paramètres.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int version = SaveMigration.CurrentVersion;
        public string profileId = "profile0";
        public string savedAtUtc = string.Empty;

        public int money;
        public int reputation;
        public List<string> upgrades = new List<string>();
        public List<string> completedOrders = new List<string>();
        public int ordersDelivered;
        public int totalEarned;

        public SettingsData settings = new SettingsData();
        public WorkshopSaveData workshop = new WorkshopSaveData();

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
            return reason == null;
        }

        /// <summary>Ramène les paramètres dans des bornes jouables (valeurs éditées à la main, anciennes versions).</summary>
        public static void ClampSettings(SettingsData s)
        {
            s.masterVolume = Mathf.Clamp01(s.masterVolume);
            s.effectsVolume = Mathf.Clamp01(s.effectsVolume);
            s.interfaceVolume = Mathf.Clamp01(s.interfaceVolume);
            s.mouseSensitivity = Mathf.Clamp(s.mouseSensitivity, 0.01f, 0.5f);
            s.motionIntensity = Mathf.Clamp01(s.motionIntensity);
            if (s.bindingOverrides == null) s.bindingOverrides = string.Empty;
        }
    }
}
