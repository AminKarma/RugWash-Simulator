using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>Mode de jeu d'un emplacement de sauvegarde : jamais de mélange entre progression solo et multijoueur.</summary>
    public enum SaveMode
    {
        Solo,
        Multiplayer,
    }

    /// <summary>Emplacement de sauvegarde (mode + numéro 1..<see cref="SaveSlots.CountPerMode"/>), un fichier par emplacement.</summary>
    public readonly struct SaveSlot : IEquatable<SaveSlot>
    {
        public readonly SaveMode Mode;
        public readonly int Index;

        public SaveSlot(SaveMode mode, int index)
        {
            Mode = mode;
            Index = Mathf.Clamp(index, 1, SaveSlots.CountPerMode);
        }

        /// <summary>Valeur enregistrée dans le fichier (champ <see cref="SaveData.mode"/>).</summary>
        public string ModeKey => KeyOf(Mode);
        public string FileName => $"rugwash_{ModeKey}_{Index}.json";
        public string DisplayName => $"{(Mode == SaveMode.Solo ? "Solo" : "Multijoueur")} — emplacement {Index}";

        public static string KeyOf(SaveMode mode) => mode == SaveMode.Solo ? "solo" : "multi";

        public static bool TryParseMode(string key, out SaveMode mode)
        {
            mode = SaveMode.Solo;
            if (key == "solo") return true;
            if (key != "multi") return false;
            mode = SaveMode.Multiplayer;
            return true;
        }

        public bool Equals(SaveSlot other) => Mode == other.Mode && Index == other.Index;
        public override bool Equals(object obj) => obj is SaveSlot other && Equals(other);
        public override int GetHashCode() => (int)Mode * 31 + Index;
        public override string ToString() => DisplayName;
    }

    /// <summary>État d'un emplacement lu sur le disque, pour l'écran de choix (rien n'est inventé : tout vient du fichier).</summary>
    public readonly struct SlotSummary
    {
        public readonly SaveSlot Slot;
        public readonly bool Exists;
        /// <summary>Lisible dans cette version du jeu (faux : plus récente, autre mode, ou endommagée sans copie de secours).</summary>
        public readonly bool Readable;
        public readonly string Problem;
        public readonly SaveData Data;

        public SlotSummary(SaveSlot slot, bool exists, bool readable, string problem, SaveData data)
        {
            Slot = slot;
            Exists = exists;
            Readable = readable;
            Problem = problem;
            Data = data;
        }

        public bool HasProgress => Readable && Data != null && (Data.HasProgress || Data.playSeconds > 0.5);

        /// <summary>Date de la dernière session, en heure locale (vide si inconnue).</summary>
        public string LastSessionText
        {
            get
            {
                if (Data == null || !DateTime.TryParse(Data.savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc)) return string.Empty;
                return utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
            }
        }

        public string PlayTimeText => SaveSlots.FormatPlayTime(Data != null ? Data.playSeconds : 0);
    }

    /// <summary>
    /// Emplacements de sauvegarde : trois en solo, trois en multijoueur, plus le profil du joueur (niveau, paramètres,
    /// apparence), commun à tous les emplacements. L'emplacement à charger dans l'atelier est choisi au menu
    /// (<see cref="Selected"/>, conservé entre les scènes). Une ancienne sauvegarde unique (« rugwash_save.json ») devient
    /// l'emplacement Solo 1 et fournit les paramètres du profil ; elle est conservée telle quelle (renommée « .migrated »).
    /// </summary>
    public static class SaveSlots
    {
        public const int CountPerMode = 3;
        public const string LegacyFileName = "rugwash_save.json";
        public const string ProfileFileName = "rugwash_profile.json";

        /// <summary>Emplacement chargé par le prochain atelier (choisi au menu ; Solo 1 par défaut).</summary>
        public static SaveSlot Selected { get; set; } = new SaveSlot(SaveMode.Solo, 1);

        public static string Directory =>
            string.IsNullOrEmpty(SaveManager.DirectoryOverride) ? Application.persistentDataPath : SaveManager.DirectoryOverride;

        public static string PathOf(SaveSlot slot) => Path.Combine(Directory, slot.FileName);

        /// <summary>Lit l'état d'un emplacement sans rien modifier (ni migration écrite, ni isolement de fichier).</summary>
        public static SlotSummary ReadSummary(SaveSlot slot)
        {
            string path = PathOf(slot), backup = path + ".bak";
            bool exists = File.Exists(path) || File.Exists(backup);
            if (!exists) return new SlotSummary(slot, false, true, null, null);
            foreach (string candidate in new[] { path, backup })
            {
                if (!File.Exists(candidate)) continue;
                SaveMigration.Outcome outcome;
                SaveData data;
                try
                {
                    outcome = SaveMigration.TryParse(File.ReadAllText(candidate), out data);
                }
                catch (Exception)
                {
                    continue;
                }
                if (outcome == SaveMigration.Outcome.NewerVersion)
                    return new SlotSummary(slot, true, false, "Sauvegarde d'une version plus récente du jeu", null);
                if (outcome == SaveMigration.Outcome.Invalid) continue;
                if (!string.IsNullOrEmpty(data.mode) && data.mode != slot.ModeKey)
                    return new SlotSummary(slot, true, false, "Sauvegarde d'un autre mode de jeu", null);
                return new SlotSummary(slot, true, true, null, data);
            }
            return new SlotSummary(slot, true, false, "Sauvegarde illisible", null);
        }

        /// <summary>Efface un emplacement (fichier, copie de secours, fichier temporaire). Les fichiers endommagés isolés restent.</summary>
        public static bool Delete(SaveSlot slot)
        {
            try
            {
                string path = PathOf(slot);
                foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
                    if (File.Exists(file)) File.Delete(file);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RugWash] Impossible d'effacer {slot} : {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ancienne sauvegarde unique : copiée vers Solo 1 (si cet emplacement est vide), puis renommée « .migrated ».
        /// Retourne les données migrées (pour les paramètres du profil), ou null.
        /// </summary>
        public static SaveData MigrateLegacy(string directory)
        {
            string legacy = Path.Combine(directory, LegacyFileName);
            if (!File.Exists(legacy)) return null;
            try
            {
                var outcome = SaveMigration.TryParse(File.ReadAllText(legacy), out var data);
                if (outcome == SaveMigration.Outcome.NewerVersion || outcome == SaveMigration.Outcome.Invalid) return null;
                var solo = new SaveSlot(SaveMode.Solo, 1);
                string target = Path.Combine(directory, solo.FileName);
                if (!File.Exists(target) && !File.Exists(target + ".bak"))
                {
                    data.mode = solo.ModeKey;
                    data.slotIndex = solo.Index;
                    if (!new SaveFileStore(directory, solo.FileName).Save(data, out string error, stampTime: false))
                    {
                        Debug.LogWarning($"[RugWash] Migration de l'ancienne sauvegarde impossible : {error}");
                        return null;
                    }
                }
                string archived = legacy + ".migrated";
                if (File.Exists(archived)) File.Delete(archived);
                File.Move(legacy, archived);
                if (File.Exists(legacy + ".bak")) File.Delete(legacy + ".bak");
                Debug.Log($"[RugWash] Ancienne sauvegarde convertie : {solo.DisplayName} (original conservé : {archived}).");
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RugWash] Migration de l'ancienne sauvegarde impossible : {e.Message}");
                return null;
            }
        }

        public static string FormatPlayTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long total = (long)seconds;
            long hours = total / 3600, minutes = total / 60 % 60;
            return hours > 0 ? $"{hours} h {minutes:00} min" : $"{Math.Max(0, minutes)} min";
        }
    }
}
