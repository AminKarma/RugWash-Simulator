using System;
using System.IO;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Save
{
    /// <summary>
    /// Profil du joueur, commun à tous ses emplacements de sauvegarde : niveau (expérience), paramètres, apparence et
    /// derniers emplacements utilisés. Changer d'emplacement ne remet jamais le niveau à zéro.
    /// Ce qui appartient à un emplacement (argent, réputation, achats, atelier, missions…) est dans <see cref="SaveData"/> ;
    /// ce qui n'existe que pendant une partie en ligne (tapis de la session, tableau de l'hôte) n'est écrit nulle part
    /// chez l'invité.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>Expérience totale du joueur (gagnée à chaque livraison acceptée, en solo comme en ligne).</summary>
        public int experience;
        public SettingsData settings = new SettingsData();
        public CharacterLook character = CharacterPalette.Default;
        /// <summary>Dernier mode joué (« solo » / « multi ») et derniers emplacements, pour « Continuer ».</summary>
        public string lastMode = "solo";
        public int lastSoloSlot = 1;
        public int lastMultiSlot = 1;
        public string savedAtUtc = string.Empty;

        public int Level => PlayerLevelRules.LevelFor(experience);

        public SaveSlot LastSlot(SaveMode mode) => new SaveSlot(mode, mode == SaveMode.Solo ? lastSoloSlot : lastMultiSlot);

        public void Remember(SaveSlot slot)
        {
            lastMode = slot.ModeKey;
            if (slot.Mode == SaveMode.Solo) lastSoloSlot = slot.Index;
            else lastMultiSlot = slot.Index;
        }

        public void Normalize()
        {
            settings ??= new SettingsData();
            SaveValidator.ClampSettings(settings);
            character = CharacterPalette.Clamp(character);
            if (experience < 0) experience = 0;
            if (lastMode != "solo" && lastMode != "multi") lastMode = "solo";
            lastSoloSlot = Mathf.Clamp(lastSoloSlot, 1, SaveSlots.CountPerMode);
            lastMultiSlot = Mathf.Clamp(lastMultiSlot, 1, SaveSlots.CountPerMode);
            savedAtUtc ??= string.Empty;
        }
    }

    /// <summary>Règles pures du niveau du joueur (testées en EditMode).</summary>
    public static class PlayerLevelRules
    {
        public const int MaxLevel = 99;

        /// <summary>Expérience à gagner pour passer du niveau <paramref name="level"/> au suivant.</summary>
        public static int StepFor(int level) => 200 + 150 * (Mathf.Max(1, level) - 1);

        /// <summary>Expérience totale nécessaire pour atteindre <paramref name="level"/> (niveau 1 : 0).</summary>
        public static int ThresholdFor(int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            long total = 0;
            for (int l = 1; l < level; l++) total += StepFor(l);
            return (int)Math.Min(int.MaxValue, total);
        }

        public static int LevelFor(int experience)
        {
            int level = 1;
            while (level < MaxLevel && experience >= ThresholdFor(level + 1)) level++;
            return level;
        }

        /// <summary>Progression vers le niveau suivant (0..1).</summary>
        public static float Progress01(int experience)
        {
            int level = LevelFor(experience);
            if (level >= MaxLevel) return 1f;
            int from = ThresholdFor(level);
            return Mathf.Clamp01((experience - from) / (float)StepFor(level));
        }

        /// <summary>Expérience d'une livraison : 0 si refusée, sinon une base plus une part de la récompense (jamais abusable : une livraison = une fois).</summary>
        public static int ForDelivery(bool accepted, int reward) => accepted ? 20 + Mathf.Max(0, reward) / 4 : 0;
    }

    /// <summary>
    /// Fichier du profil : écriture atomique (fichier temporaire relu avant remplacement) et copie de secours, comme les
    /// emplacements. Un profil illisible est isolé et recréé avec les valeurs par défaut (les emplacements ne sont pas touchés).
    /// </summary>
    public sealed class ProfileStore
    {
        public ProfileStore(string directory)
        {
            Directory = directory;
            MainPath = Path.Combine(directory, SaveSlots.ProfileFileName);
            BackupPath = MainPath + ".bak";
            TempPath = MainPath + ".tmp";
        }

        public string Directory { get; }
        public string MainPath { get; }
        public string BackupPath { get; }
        public string TempPath { get; }
        public bool Exists => File.Exists(MainPath) || File.Exists(BackupPath);
        /// <summary>Faux si le profil vient d'une version plus récente du jeu : il n'est jamais écrasé.</summary>
        public bool WritingAllowed { get; private set; } = true;

        public PlayerProfile Load(out string message)
        {
            message = null;
            foreach (string path in new[] { MainPath, BackupPath })
            {
                if (!File.Exists(path)) continue;
                var parsed = TryRead(path, out var profile);
                if (parsed == ReadResult.Newer)
                {
                    WritingAllowed = false;
                    message = "Profil créé par une version plus récente du jeu : il n'est pas modifié.";
                    return new PlayerProfile();
                }
                if (parsed == ReadResult.Ok)
                {
                    if (path == BackupPath) message = "Profil endommagé : la copie de secours a été restaurée.";
                    return profile;
                }
            }
            if (File.Exists(MainPath)) message = "Profil illisible : paramètres par défaut.";
            return new PlayerProfile();
        }

        public bool Save(PlayerProfile profile, out string error)
        {
            error = null;
            if (!WritingAllowed)
            {
                error = "écriture désactivée (profil plus récent)";
                return false;
            }
            try
            {
                profile.Normalize();
                profile.version = PlayerProfile.CurrentVersion;
                profile.savedAtUtc = DateTime.UtcNow.ToString("o");
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(TempPath, JsonUtility.ToJson(profile, true));
                if (TryRead(TempPath, out _) != ReadResult.Ok)
                {
                    File.Delete(TempPath);
                    error = "vérification du profil écrit échouée";
                    return false;
                }
                if (File.Exists(MainPath) && TryRead(MainPath, out _) == ReadResult.Ok) File.Copy(MainPath, BackupPath, true);
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

        enum ReadResult { Ok, Invalid, Newer }

        static ReadResult TryRead(string path, out PlayerProfile profile)
        {
            profile = null;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return ReadResult.Invalid;
                profile = JsonUtility.FromJson<PlayerProfile>(json);
                if (profile == null) return ReadResult.Invalid;
                if (profile.version > PlayerProfile.CurrentVersion) return ReadResult.Newer;
                if (profile.version <= 0) return ReadResult.Invalid;
                profile.Normalize();
                return ReadResult.Ok;
            }
            catch (Exception)
            {
                return ReadResult.Invalid;
            }
        }
    }
}
