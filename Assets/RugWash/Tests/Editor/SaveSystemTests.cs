using System;
using System.IO;
using NUnit.Framework;
using RugWash.Player;
using RugWash.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Tests
{
    /// <summary>Sauvegarde locale : fichier réel dans un dossier temporaire.</summary>
    public class SaveSystemTests
    {
        string directory;
        SaveFileStore store;

        [SetUp]
        public void CreateSandbox()
        {
            directory = Path.Combine(Path.GetTempPath(), "RugWashSaveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            store = new SaveFileStore(directory, "save.json");
        }

        [TearDown]
        public void DeleteSandbox()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        static SaveData Sample(int money = 450)
        {
            var data = new SaveData { money = money, reputation = 4, ordersDelivered = 2, totalEarned = 600 };
            data.upgrades.Add("up_water_1");
            data.completedOrders.Add("order_salon");
            data.completedOrders.Add("order_cafe");
            data.settings.masterVolume = 0.4f;
            data.settings.mouseSensitivity = 0.25f;
            data.settings.invertY = true;
            return data;
        }

        [Test]
        public void NewInstall_LoadsDefaults_WithoutFile()
        {
            var result = store.Load();
            Assert.AreEqual(LoadStatus.NewProfile, result.Status);
            Assert.AreEqual(0, result.Data.money);
            Assert.IsFalse(result.Data.HasProgress);
            Assert.IsFalse(File.Exists(store.MainPath), "Charger ne doit rien écrire.");
        }

        [Test]
        public void SavedProgress_IsRestoredIdentically()
        {
            Assert.IsTrue(store.Save(Sample(), out string error), error);
            var result = new SaveFileStore(directory, "save.json").Load();
            Assert.AreEqual(LoadStatus.Loaded, result.Status);
            var data = result.Data;
            Assert.AreEqual(450, data.money);
            Assert.AreEqual(4, data.reputation);
            CollectionAssert.AreEqual(new[] { "up_water_1" }, data.upgrades);
            CollectionAssert.AreEqual(new[] { "order_salon", "order_cafe" }, data.completedOrders);
            Assert.AreEqual(0.4f, data.settings.masterVolume, 1e-5f);
            Assert.IsTrue(data.settings.invertY);
            Assert.AreEqual(SaveMigration.CurrentVersion, data.version);
            Assert.IsNotEmpty(data.savedAtUtc);
        }

        [Test]
        public void SecondSave_KeepsPreviousAsBackup()
        {
            store.Save(Sample(100), out _);
            store.Save(Sample(200), out _);
            Assert.IsTrue(File.Exists(store.BackupPath));
            Assert.AreEqual(SaveMigration.Outcome.UpToDate, SaveMigration.TryParse(File.ReadAllText(store.BackupPath), out var backup));
            Assert.AreEqual(100, backup.money);
            Assert.IsFalse(File.Exists(store.TempPath), "Le fichier temporaire ne doit pas subsister.");
        }

        [Test]
        public void CorruptedMain_RestoresBackup_AndKeepsCorruptFile()
        {
            store.Save(Sample(100), out _);
            store.Save(Sample(200), out _);
            File.WriteAllText(store.MainPath, "{\"version\":1,\"money\":");

            var result = new SaveFileStore(directory, "save.json").Load();
            Assert.AreEqual(LoadStatus.RestoredFromBackup, result.Status);
            Assert.AreEqual(100, result.Data.money);
            Assert.IsNotNull(result.Message);
            Assert.IsNotEmpty(Directory.GetFiles(directory, "save.json.corrupt-*"), "Le fichier endommagé doit être conservé.");
        }

        [Test]
        public void CorruptedMainAndBackup_StartsNewProfile_WithoutCrash()
        {
            File.WriteAllText(store.MainPath, "ceci n'est pas du JSON");
            File.WriteAllText(store.BackupPath, "\u0000\u0001\u0002");
            var result = store.Load();
            Assert.AreEqual(LoadStatus.CorruptedReset, result.Status);
            Assert.IsNotNull(result.Data);
            Assert.AreEqual(0, result.Data.money);
            Assert.AreEqual(2, Directory.GetFiles(directory, "*.corrupt-*").Length);
        }

        [Test]
        public void InvalidData_IsNeverWritten_AndValidSaveSurvives()
        {
            store.Save(Sample(300), out _);
            var invalid = Sample(-5);
            Assert.IsFalse(store.Save(invalid, out string error));
            StringAssert.Contains("solde", error);
            var invalidIds = Sample();
            invalidIds.completedOrders.Add("");
            Assert.IsFalse(store.Save(invalidIds, out _));

            var result = store.Load();
            Assert.AreEqual(300, result.Data.money, "La sauvegarde valide précédente doit être intacte.");
        }

        [Test]
        public void NewerVersion_IsNotLoaded_NorOverwritten()
        {
            string newer = "{\"version\":99,\"money\":12345}";
            File.WriteAllText(store.MainPath, newer);
            var result = store.Load();
            Assert.AreEqual(LoadStatus.NewerVersion, result.Status);
            Assert.IsFalse(store.WritingAllowed);
            Assert.IsFalse(store.Save(Sample(), out _));
            Assert.AreEqual(newer, File.ReadAllText(store.MainPath));
        }

        [Test]
        public void VersionZero_IsMigrated()
        {
            File.WriteAllText(store.MainPath, "{\"version\":0,\"coins\":75,\"rep\":2,\"upgrades\":[\"up_brush\"],\"completedOrders\":[\"order_salon\"]}");
            var result = store.Load();
            Assert.AreEqual(LoadStatus.Migrated, result.Status);
            Assert.AreEqual(75, result.Data.money);
            Assert.AreEqual(2, result.Data.reputation);
            // v0 → v1 → v2 : partie commencée, les outils hors kit de départ restent acquis.
            var expected = new System.Collections.Generic.List<string> { "up_brush" };
            expected.AddRange(SaveMigration.LegacyToolUnlocks);
            expected.AddRange(SaveMigration.LegacyWorkshopUnlocks);
            expected.AddRange(SaveMigration.LegacyDecorUnlocks);
            // V0.4.2 (v7) : la brosse standard, devenue un achat, reste acquise.
            expected.AddRange(SaveMigration.LegacyStarterUnlocks);
            CollectionAssert.AreEqual(expected, result.Data.upgrades);
            Assert.AreEqual(SaveMigration.LegacyDetergentStock, result.Data.detergentStock);
            Assert.AreEqual(1, result.Data.ordersDelivered);
            Assert.AreEqual(SaveMigration.CurrentVersion, result.Data.version);
            Assert.IsNotNull(result.Data.settings, "Les champs absents de l'ancien format reçoivent des valeurs par défaut.");
        }

        [Test]
        public void OutOfRangeSettings_AreClamped()
        {
            var data = Sample();
            data.settings.masterVolume = 7f;
            data.settings.mouseSensitivity = -3f;
            store.Save(data, out _);
            var loaded = store.Load().Data.settings;
            Assert.AreEqual(1f, loaded.masterVolume, 1e-5f);
            Assert.AreEqual(0.01f, loaded.mouseSensitivity, 1e-5f);
        }

        /// <summary>Régression : les actions recréées à chaque lancement ont de nouveaux identifiants.</summary>
        [Test]
        public void BindingOverrides_SurviveRecreatedActionAssets()
        {
            var first = RugWashInputActions.Create();
            var second = RugWashInputActions.Create();
            try
            {
                first.FindAction("Interact").ApplyBindingOverride(0, "<Keyboard>/g");
                var move = first.FindAction("Move");
                int up = SettingsManager.FindBindingIndex(move, new RebindableAction("Avancer", "Move", "up"));
                move.ApplyBindingOverride(up, "<Keyboard>/t");
                string json = JsonUtility.ToJson(BindingOverrideSet.Capture(first));

                int applied = JsonUtility.FromJson<BindingOverrideSet>(json).ApplyTo(second);
                Assert.AreEqual(2, applied);
                Assert.AreEqual("<Keyboard>/g", second.FindAction("Interact").bindings[0].effectivePath);
                Assert.AreEqual("<Keyboard>/t", second.FindAction("Move").bindings[up].effectivePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void NewGame_ResetsProgress_ButKeepsSettings()
        {
            var fresh = Sample().FreshProgressKeepingSettings();
            Assert.IsFalse(fresh.HasProgress);
            Assert.AreEqual(0, fresh.money);
            Assert.IsEmpty(fresh.completedOrders);
            Assert.AreEqual(0.4f, fresh.settings.masterVolume, 1e-5f);
            Assert.IsTrue(fresh.settings.invertY);
        }
    }
}
