using System;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>
    /// Migration des anciens formats. Chaque version ajoute une étape ; les sauvegardes plus récentes
    /// que le jeu sont refusées (et jamais écrasées).
    ///   v0 : prototype (champs « coins » et « rep », sans profil ni paramètres).
    ///   v1 : l'état de l'atelier (robot, plan de travail, sol) est un ajout compatible :
    ///        absent d'une ancienne sauvegarde, il prend ses valeurs par défaut (atelier propre, robot sur sa base).
    ///        Même principe pour les produits spécialisés (aucun en stock) et le RugDex (vide).
    ///   v2 (v0.1.2) : kit de départ réduit (brosse, détergent, seau d'eau) et détergent consommable. Une partie déjà
    ///        commencée garde les outils qu'elle avait gratuitement (aspirateur, lance, extracteur, canon à mousse, balai :
    ///        leurs déblocages sont ajoutés) et reçoit un bidon de 40 L (le détergent était illimité).
    /// </summary>
    public static class SaveMigration
    {
        public const int CurrentVersion = 2;

        /// <summary>Stock de détergent d'une nouvelle partie (litres) : trois remplissages de la réserve de 4 L.</summary>
        public const float StarterDetergentStock = 12f;
        /// <summary>Stock offert à une partie v1 déjà commencée (le détergent était illimité) : un bidon.</summary>
        public const float LegacyDetergentStock = 40f;

        /// <summary>Déblocages des outils gratuits avant la v0.1.2, accordés aux parties déjà commencées.</summary>
        public static readonly string[] LegacyToolUnlocks =
        {
            "up_tool_vacuum", "up_tool_rinse", "up_tool_extractor", "up_tool_foam", "up_tool_broom",
        };

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
                if (version == 1)
                {
                    data = FromV1(JsonUtility.FromJson<SaveData>(json));
                    return Normalize(data) ? Outcome.Migrated : Outcome.Invalid;
                }
                if (version == 0)
                {
                    data = FromV1(FromV0(JsonUtility.FromJson<SaveDataV0>(json)));
                    return Normalize(data) ? Outcome.Migrated : Outcome.Invalid;
                }
            }
            catch (Exception)
            {
                data = null;
            }
            return Outcome.Invalid;
        }

        /// <summary>
        /// v1 → v2 : une partie déjà commencée garde ses outils (déblocages ajoutés) et reçoit un bidon de détergent ; une
        /// sauvegarde v1 sans progression devient une nouvelle partie avec le kit de départ.
        /// </summary>
        public static SaveData FromV1(SaveData data)
        {
            if (data == null) return null;
            bool started = data.HasProgress;
            data.soapReserve = -1f;
            data.detergentStock = started ? LegacyDetergentStock : StarterDetergentStock;
            if (started)
            {
                data.upgrades ??= new System.Collections.Generic.List<string>();
                foreach (string id in LegacyToolUnlocks)
                    if (!data.upgrades.Contains(id)) data.upgrades.Add(id);
            }
            return data;
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
            data.products ??= new System.Collections.Generic.List<ProductStockData>();
            data.products.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.id) || float.IsNaN(p.liters) || p.liters < 0f);
            data.activeProduct ??= string.Empty;
            data.rugDex ??= new System.Collections.Generic.List<RugDex.RugDexRecord>();
            data.rugDex.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.id));
            data.workshop.workSurface ??= string.Empty;
            data.workshop.floor ??= string.Empty;
            if (string.IsNullOrWhiteSpace(data.profileId)) data.profileId = "profile0";
            data.savedAtUtc ??= string.Empty;
            data.upgrades.RemoveAll(string.IsNullOrWhiteSpace);
            data.completedOrders.RemoveAll(string.IsNullOrWhiteSpace);
            // Les missions générées ne sont jamais « terminées » dans l'historique : elles sont remplacées.
            data.completedOrders.RemoveAll(Orders.MissionKey.IsGenerated);
            data.missionOffers ??= new System.Collections.Generic.List<string>();
            data.missionOffers.RemoveAll(id => !Orders.MissionKey.TryParse(id, out _));
            data.lastMission ??= string.Empty;
            if (data.missionSequence < 0) data.missionSequence = 0;
            if (float.IsNaN(data.detergentStock) || data.detergentStock < 0f) data.detergentStock = 0f;
            if (float.IsNaN(data.soapReserve)) data.soapReserve = -1f;
            SaveValidator.ClampSettings(data.settings);
            return SaveValidator.IsValid(data, out _);
        }
    }
}
