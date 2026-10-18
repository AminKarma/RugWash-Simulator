using System;
using System.IO;
using NUnit.Framework;
using RugWash.Economy;
using RugWash.Save;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 2 : emplacements de sauvegarde (trois solo, trois multijoueur), profil du joueur (niveau,
    /// paramètres) commun à tous les emplacements, migration de l'ancienne sauvegarde, résumés lus sur le disque et règle
    /// d'arrivée d'un invité (copie de la carrière de l'hôte une seule fois, puis indépendance).
    /// </summary>
    public class SaveSlotsTests
    {
        string directory;
        string previousOverride;

        [SetUp]
        public void SetUp()
        {
            previousOverride = SaveManager.DirectoryOverride;
            directory = Path.Combine(Path.GetTempPath(), "RugWashSlotTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SaveManager.DirectoryOverride = directory;
        }

        [TearDown]
        public void TearDown()
        {
            SaveManager.DirectoryOverride = previousOverride;
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }

        [Test]
        public void Slots_HaveDistinctFiles_PerModeAndIndex_AndDefaultIsSoloOne()
        {
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (SaveMode mode in Enum.GetValues(typeof(SaveMode)))
                for (int i = 1; i <= SaveSlots.CountPerMode; i++)
                    Assert.IsTrue(names.Add(new SaveSlot(mode, i).FileName), "Un fichier par emplacement.");
            Assert.AreEqual(6, names.Count);
            Assert.AreEqual(new SaveSlot(SaveMode.Solo, 1).FileName, SaveManager.DefaultFileName);
            Assert.AreEqual(3, new SaveSlot(SaveMode.Multiplayer, 9).Index, "Numéro borné.");
            Assert.IsFalse(names.Contains(SaveSlots.LegacyFileName));
        }

        [Test]
        public void LegacySave_BecomesSoloOne_KeepsItsDate_AndIsArchivedNotDeleted()
        {
            var old = new SaveData { version = 4, money = 777, reputation = 3, savedAtUtc = "2026-10-01T10:00:00.0000000Z" };
            old.settings.mouseSensitivity = 0.27f;
            string json = JsonUtility.ToJson(old);
            File.WriteAllText(Path.Combine(directory, SaveSlots.LegacyFileName), json);

            var migrated = SaveSlots.MigrateLegacy(directory);
            Assert.IsNotNull(migrated);
            Assert.AreEqual(0.27f, migrated.settings.mouseSensitivity, 1e-5f, "Les paramètres servent à créer le profil.");
            var summary = SaveSlots.ReadSummary(new SaveSlot(SaveMode.Solo, 1));
            Assert.IsTrue(summary.Exists && summary.Readable);
            Assert.AreEqual(777, summary.Data.money);
            Assert.AreEqual("solo", summary.Data.mode);
            Assert.AreEqual("2026-10-01T10:00:00.0000000Z", summary.Data.savedAtUtc, "Date réelle de la dernière session conservée.");
            Assert.IsFalse(File.Exists(Path.Combine(directory, SaveSlots.LegacyFileName)));
            Assert.IsTrue(File.Exists(Path.Combine(directory, SaveSlots.LegacyFileName + ".migrated")), "Original conservé.");
            Assert.IsNull(SaveSlots.MigrateLegacy(directory), "Une seule fois.");
        }

        [Test]
        public void LegacySave_NeverOverwritesAnExistingSoloOne()
        {
            Assert.IsTrue(new SaveFileStore(directory, SaveManager.DefaultFileName).Save(new SaveData { money = 5, mode = "solo", slotIndex = 1 }, out _));
            File.WriteAllText(Path.Combine(directory, SaveSlots.LegacyFileName), JsonUtility.ToJson(new SaveData { version = 4, money = 9999 }));
            SaveSlots.MigrateLegacy(directory);
            Assert.AreEqual(5, SaveSlots.ReadSummary(new SaveSlot(SaveMode.Solo, 1)).Data.money);
        }

        [Test]
        public void Summary_ComesFromTheFile_AndRefusesOtherModesAndNewerVersions()
        {
            var empty = SaveSlots.ReadSummary(new SaveSlot(SaveMode.Solo, 2));
            Assert.IsFalse(empty.Exists);
            Assert.IsFalse(empty.HasProgress);

            var data = new SaveData { mode = "solo", slotIndex = 2, money = 1500, reputation = 4, ordersDelivered = 6, playSeconds = 3725 };
            Assert.IsTrue(new SaveFileStore(directory, new SaveSlot(SaveMode.Solo, 2).FileName).Save(data, out _));
            var used = SaveSlots.ReadSummary(new SaveSlot(SaveMode.Solo, 2));
            Assert.IsTrue(used.Exists && used.Readable && used.HasProgress);
            Assert.AreEqual("1 h 02 min", used.PlayTimeText);
            Assert.IsNotEmpty(used.LastSessionText, "Date de la dernière session écrite par la sauvegarde.");
            StringAssert.Contains("6 commande(s)", RugWash.UI.SaveSlotsUI.Describe(used));

            // Une sauvegarde multijoueur copiée à la main dans un emplacement solo n'est jamais lue comme du solo.
            var multi = new SaveData { mode = "multi", money = 42 };
            Assert.IsTrue(new SaveFileStore(directory, new SaveSlot(SaveMode.Solo, 3).FileName).Save(multi, out _));
            var wrong = SaveSlots.ReadSummary(new SaveSlot(SaveMode.Solo, 3));
            Assert.IsTrue(wrong.Exists);
            Assert.IsFalse(wrong.Readable);

            File.WriteAllText(Path.Combine(directory, new SaveSlot(SaveMode.Multiplayer, 1).FileName), "{\"version\": 999}");
            Assert.IsFalse(SaveSlots.ReadSummary(new SaveSlot(SaveMode.Multiplayer, 1)).Readable, "Version plus récente : jamais chargée.");

            Assert.IsTrue(SaveSlots.Delete(new SaveSlot(SaveMode.Solo, 2)));
            Assert.IsFalse(SaveSlots.ReadSummary(new SaveSlot(SaveMode.Solo, 2)).Exists);
        }

        [Test]
        public void SaveV4_MigratesToV5_WithEmptyModeAndNoPlayTime()
        {
            var v4 = JsonUtility.ToJson(new SaveData { version = 4, money = 12 });
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(v4, out var data));
            Assert.AreEqual(SaveMigration.CurrentVersion, data.version);
            Assert.AreEqual(string.Empty, data.mode);
            Assert.AreEqual(0, data.playSeconds);
            Assert.IsNotNull(data.members, "v6 : liste des fiches de joueurs (vide).");
            Assert.AreEqual(0, data.members.Count);
            Assert.IsFalse(SaveValidator.IsValid(new SaveData { mode = "coop" }, out _), "Mode inconnu refusé.");
        }

        [Test]
        public void Profile_RoundTrips_RestoresFromBackup_AndNeverOverwritesANewerVersion()
        {
            var store = new ProfileStore(directory);
            var profile = new PlayerProfile { experience = 1234, lastSoloSlot = 2, lastMultiSlot = 3 };
            profile.settings.musicVolume = 0.2f;
            Assert.IsTrue(store.Save(profile, out _));
            Assert.IsTrue(store.Save(profile, out _), "Deuxième écriture : la précédente devient la copie de secours.");
            var loaded = new ProfileStore(directory).Load(out string message);
            Assert.IsNull(message);
            Assert.AreEqual(1234, loaded.experience);
            Assert.AreEqual(new SaveSlot(SaveMode.Multiplayer, 3), loaded.LastSlot(SaveMode.Multiplayer));
            Assert.AreEqual(0.2f, loaded.settings.musicVolume, 1e-5f);

            File.WriteAllText(store.MainPath, "{ endommagé");
            var restored = new ProfileStore(directory).Load(out message);
            Assert.AreEqual(1234, restored.experience, "Copie de secours restaurée.");
            Assert.IsNotNull(message);

            File.WriteAllText(store.MainPath, "{\"version\": 99, \"experience\": 5}");
            var newer = new ProfileStore(directory);
            newer.Load(out _);
            Assert.IsFalse(newer.Save(new PlayerProfile(), out _), "Profil d'une version plus récente : jamais écrasé.");
        }

        [Test]
        public void PlayerLevel_GrowsWithExperience_AndADeliveryGivesItOnce()
        {
            Assert.AreEqual(1, PlayerLevelRules.LevelFor(0));
            int previous = 0;
            for (int level = 2; level <= 20; level++)
            {
                int threshold = PlayerLevelRules.ThresholdFor(level);
                Assert.Greater(threshold, previous, "Seuils croissants.");
                Assert.AreEqual(level, PlayerLevelRules.LevelFor(threshold));
                Assert.AreEqual(level - 1, PlayerLevelRules.LevelFor(threshold - 1));
                previous = threshold;
            }
            Assert.AreEqual(PlayerLevelRules.MaxLevel, PlayerLevelRules.LevelFor(int.MaxValue));
            Assert.AreEqual(0, PlayerLevelRules.ForDelivery(false, 500), "Livraison refusée : rien.");
            Assert.Greater(PlayerLevelRules.ForDelivery(true, 500), PlayerLevelRules.ForDelivery(true, 100));
            Assert.AreEqual(0f, PlayerLevelRules.Progress01(0), 1e-5f);
        }

        [Test]
        public void SaveV5_MigratesToV6_MembersAndDropsAreCleanedUp()
        {
            var v5 = JsonUtility.ToJson(new SaveData { version = 5, money = 12 });
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(v5, out var data));
            Assert.AreEqual(SaveMigration.CurrentVersion, data.version);
            Assert.AreEqual(0, data.members.Count);
            Assert.AreEqual(0, data.workshop.drops.Count);

            // Fiches abîmées (clé vide, doublon) et objets au sol invalides : nettoyés à la lecture.
            var raw = new SaveData();
            raw.members.Add(new SessionMemberData { key = "steam:1", upgrades = new System.Collections.Generic.List<string> { "up_brush" } });
            raw.members.Add(new SessionMemberData { key = "" });
            raw.members.Add(new SessionMemberData { key = "steam:1", upgrades = new System.Collections.Generic.List<string> { "up_water_1", " " } });
            raw.workshop.drops.Add(new DroppedItemData { upgrade = "up_tool_vacuum", x = 1f });
            raw.workshop.drops.Add(new DroppedItemData { upgrade = "" });
            Assert.AreEqual(SaveMigration.Outcome.UpToDate, SaveMigration.TryParse(JsonUtility.ToJson(raw), out var clean));
            Assert.AreEqual(1, clean.members.Count, "Une fiche par joueur.");
            CollectionAssert.AreEqual(new[] { "up_water_1" }, clean.members[0].upgrades, "La dernière fiche lue l'emporte, sans identifiant vide.");
            Assert.AreEqual(1, clean.workshop.drops.Count);
        }
    }
}
