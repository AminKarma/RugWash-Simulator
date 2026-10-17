using System;
using System.IO;
using UnityEngine;

namespace RugWash.Save
{
    public enum LoadStatus
    {
        /// <summary>Aucune sauvegarde : nouvelle installation.</summary>
        NewProfile,
        Loaded,
        /// <summary>Fichier principal ancien format, converti.</summary>
        Migrated,
        /// <summary>Fichier principal illisible, copie de secours utilisée.</summary>
        RestoredFromBackup,
        /// <summary>Fichier principal et copie de secours illisibles : nouveau profil (fichiers endommagés conservés à part).</summary>
        CorruptedReset,
        /// <summary>Sauvegarde d'une version plus récente du jeu : chargée en lecture seule, jamais écrasée.</summary>
        NewerVersion,
    }

    public readonly struct LoadResult
    {
        public readonly LoadStatus Status;
        public readonly SaveData Data;
        public readonly string Message;

        public LoadResult(LoadStatus status, SaveData data, string message)
        {
            Status = status;
            Data = data;
            Message = message;
        }
    }

    /// <summary>
    /// Fichier de sauvegarde local : écriture atomique (fichier temporaire relu et validé avant remplacement),
    /// copie de secours de la dernière sauvegarde valide, récupération des fichiers endommagés.
    /// </summary>
    public sealed class SaveFileStore
    {
        public SaveFileStore(string directory, string fileName)
        {
            Directory = directory;
            MainPath = Path.Combine(directory, fileName);
            BackupPath = MainPath + ".bak";
            TempPath = MainPath + ".tmp";
        }

        public string Directory { get; }
        public string MainPath { get; }
        public string BackupPath { get; }
        public string TempPath { get; }

        /// <summary>Faux après détection d'une sauvegarde plus récente : protège les données du joueur.</summary>
        public bool WritingAllowed { get; private set; } = true;

        public bool Exists => File.Exists(MainPath) || File.Exists(BackupPath);

        /// <summary>Interdit toute écriture (fichier d'un autre mode de jeu trouvé dans cet emplacement).</summary>
        public void ProtectFromWriting() => WritingAllowed = false;

        public LoadResult Load()
        {
            if (!File.Exists(MainPath) && !File.Exists(BackupPath))
                return new LoadResult(LoadStatus.NewProfile, new SaveData(), null);

            var main = Read(MainPath, out SaveData mainData);
            if (main == SaveMigration.Outcome.NewerVersion)
            {
                WritingAllowed = false;
                return new LoadResult(LoadStatus.NewerVersion, new SaveData(),
                    "Sauvegarde créée par une version plus récente du jeu : elle n'est ni chargée ni modifiée.");
            }
            if (main == SaveMigration.Outcome.UpToDate) return new LoadResult(LoadStatus.Loaded, mainData, null);
            if (main == SaveMigration.Outcome.Migrated)
                return new LoadResult(LoadStatus.Migrated, mainData, "Sauvegarde convertie depuis un ancien format.");

            // Fichier principal absent ou illisible : copie de secours.
            if (File.Exists(MainPath)) Quarantine(MainPath);
            var backup = Read(BackupPath, out SaveData backupData);
            if (backup == SaveMigration.Outcome.UpToDate || backup == SaveMigration.Outcome.Migrated)
                return new LoadResult(LoadStatus.RestoredFromBackup, backupData,
                    "Sauvegarde endommagée : la copie de secours a été restaurée.");

            if (File.Exists(BackupPath)) Quarantine(BackupPath);
            return new LoadResult(LoadStatus.CorruptedReset, new SaveData(),
                "Sauvegarde illisible : une nouvelle partie commence (fichiers endommagés conservés).");
        }

        /// <summary>Écrit une sauvegarde valide de façon atomique. Ne touche à rien si les données sont invalides.</summary>
        /// <param name="stampTime">Faux : conserve la date de la dernière session (migration d'une ancienne sauvegarde).</param>
        public bool Save(SaveData data, out string error, bool stampTime = true)
        {
            error = null;
            if (!WritingAllowed)
            {
                error = "écriture désactivée (sauvegarde plus récente détectée)";
                return false;
            }
            if (!SaveValidator.IsValid(data, out string reason))
            {
                error = $"données invalides : {reason}";
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                data.version = SaveMigration.CurrentVersion;
                if (stampTime || string.IsNullOrEmpty(data.savedAtUtc)) data.savedAtUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(TempPath, JsonUtility.ToJson(data, true));

                // Relecture du fichier temporaire avant de remplacer quoi que ce soit.
                if (Read(TempPath, out _) != SaveMigration.Outcome.UpToDate)
                {
                    File.Delete(TempPath);
                    error = "vérification du fichier écrit échouée";
                    return false;
                }

                // La sauvegarde principale actuelle, si elle est valide, devient la copie de secours.
                if (File.Exists(MainPath) && Read(MainPath, out _) == SaveMigration.Outcome.UpToDate)
                    File.Copy(MainPath, BackupPath, true);
                if (File.Exists(MainPath)) File.Delete(MainPath);
                File.Move(TempPath, MainPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        static SaveMigration.Outcome Read(string path, out SaveData data)
        {
            data = null;
            if (!File.Exists(path)) return SaveMigration.Outcome.Invalid;
            try
            {
                return SaveMigration.TryParse(File.ReadAllText(path), out data);
            }
            catch (Exception)
            {
                return SaveMigration.Outcome.Invalid;
            }
        }

        /// <summary>Renomme un fichier endommagé (jamais supprimé, pour diagnostic).</summary>
        static void Quarantine(string path)
        {
            try
            {
                string target = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                if (File.Exists(target)) File.Delete(target);
                File.Move(path, target);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RugWash] Impossible d'isoler le fichier endommagé {path} : {e.Message}");
            }
        }
    }
}
