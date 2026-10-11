using System;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>
    /// Migration des anciens formats. Chaque version ajoute une étape ; les sauvegardes plus récentes
    /// que le jeu sont refusées (et jamais écrasées).
    ///   v0 : prototype (champs « coins » et « rep », sans profil ni paramètres).
    ///   v1 : format actuel. L'état de l'atelier (robot, plan de travail, sol) est un ajout compatible :
    ///        absent d'une ancienne sauvegarde, il prend ses valeurs par défaut (atelier propre, robot sur sa base).
    /// </summary>
    public static class SaveMigration
    {
        public const int CurrentVersion = 1;

        public enum Outcome
        {
            UpToDate,
            Migrated,
            NewerVersion,
            Invalid,
        }

        [Serializable]
        struct VersionProbe
        {
            public int version;
        }

        [Serializable]
        sealed class SaveDataV0
        {
            public int version;
            public int coins;
            public int rep;
            public string[] upgrades;
            public string[] completedOrders;
        }

        /// <summary>Lit un JSON de n'importe quelle version connue et le convertit au format actuel.</summary>
        public static Outcome TryParse(string json, out SaveData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json)) return Outcome.Invalid;
            int version;
            try
            {
                version = JsonUtility.FromJson<VersionProbe>(json).version;
            }
            catch (Exception)
            {
                return Outcome.Invalid;
            }

            if (version > CurrentVersion) return Outcome.NewerVersion;
            try
            {
                if (version == CurrentVersion)
                {
                    data = JsonUtility.FromJson<SaveData>(json);
                    return Normalize(data) ? Outcome.UpToDate : Outcome.Invalid;
                }
                if (version == 0)
                {
                    data = FromV0(JsonUtility.FromJson<SaveDataV0>(json));
                    return Normalize(data) ? Outcome.Migrated : Outcome.Invalid;
                }
            }
            catch (Exception)
            {
                data = null;
            }
            return Outcome.Invalid;
        }

        static SaveData FromV0(SaveDataV0 old)
        {
            if (old == null) return null;
            var data = new SaveData { money = old.coins, reputation = old.rep };
            if (old.upgrades != null) data.upgrades.AddRange(old.upgrades);
            if (old.completedOrders != null)
            {
                data.completedOrders.AddRange(old.completedOrders);
                data.ordersDelivered = old.completedOrders.Length;
            }
            return data;
        }

        /// <summary>Complète les champs absents et vérifie la cohérence. Faux si les données sont inutilisables.</summary>
        static bool Normalize(SaveData data)
        {
            if (data == null) return false;
            data.version = CurrentVersion;
            data.upgrades ??= new System.Collections.Generic.List<string>();
            data.completedOrders ??= new System.Collections.Generic.List<string>();
            data.settings ??= new SettingsData();
            data.workshop ??= new WorkshopSaveData();
            data.workshop.workSurface ??= string.Empty;
            data.workshop.floor ??= string.Empty;
            if (string.IsNullOrWhiteSpace(data.profileId)) data.profileId = "profile0";
            data.savedAtUtc ??= string.Empty;
            data.upgrades.RemoveAll(string.IsNullOrWhiteSpace);
            data.completedOrders.RemoveAll(string.IsNullOrWhiteSpace);
            SaveValidator.ClampSettings(data.settings);
            return SaveValidator.IsValid(data, out _);
        }
    }
}
