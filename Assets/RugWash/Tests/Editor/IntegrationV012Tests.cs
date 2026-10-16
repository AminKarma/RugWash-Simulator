using NUnit.Framework;
using RugWash.Player;
using RugWash.Save;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 7 (intégration) : toutes les versions de sauvegarde publiées se chargent dans le format actuel
    /// sans perdre la progression ; une partie commencée garde ce qui était gratuit avant (outils, robot, atelier rangé),
    /// une nouvelle partie démarre avec le kit seul ; une version plus récente n'est jamais chargée ; une sauvegarde
    /// actuelle (achats d'atelier et de farces, apparence) relue à l'identique.
    /// </summary>
    public class IntegrationV012Tests
    {
        [TestCase(0, "{\"version\":0,\"coins\":250,\"rep\":4,\"upgrades\":[\"up_brush\"],\"completedOrders\":[\"order_salon\"]}")]
        [TestCase(1, "{\"version\":1,\"profileId\":\"p\",\"money\":250,\"reputation\":4,\"upgrades\":[\"up_brush\"],\"completedOrders\":[\"order_salon\"]}")]
        [TestCase(2, "{\"version\":2,\"profileId\":\"p\",\"money\":250,\"reputation\":4,\"upgrades\":[\"up_brush\"],\"completedOrders\":[\"order_salon\"]}")]
        [TestCase(3, "{\"version\":3,\"profileId\":\"p\",\"money\":250,\"reputation\":4,\"upgrades\":[\"up_brush\",\"up_robot\"],\"completedOrders\":[\"order_salon\"]}")]
        public void EveryPastVersion_LoadsToTheCurrentFormat_KeepingTheCareer(int version, string json)
        {
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(json, out var data), $"v{version}");
            Assert.AreEqual(SaveMigration.CurrentVersion, data.version);
            Assert.AreEqual(250, data.money, "Argent conservé.");
            Assert.AreEqual(4, data.reputation, "Réputation conservée.");
            CollectionAssert.Contains(data.upgrades, "up_brush", "Achats conservés.");
            CollectionAssert.Contains(data.completedOrders, "order_salon");
            // Ce qui était gratuit avant une version reste acquis pour une partie commencée.
            if (version < 2) CollectionAssert.IsSubsetOf(SaveMigration.LegacyToolUnlocks, data.upgrades, "Outils gratuits avant le jalon 3.");
            if (version < 3) CollectionAssert.IsSubsetOf(SaveMigration.LegacyWorkshopUnlocks, data.upgrades, "Robot gratuit avant le jalon 4.");
            CollectionAssert.IsSubsetOf(SaveMigration.LegacyDecorUnlocks, data.upgrades, "Atelier rangé avant le jalon 5.");
            Assert.AreEqual(CharacterPalette.Default, data.character, "Apparence par défaut.");
            Assert.IsNotNull(data.settings);
            Assert.Greater(data.settings.musicVolume, 0f, "Musique audible par défaut.");
        }

        [Test]
        public void NewGame_StartsWithTheKitOnly_InTheCagibi()
        {
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":1,\"profileId\":\"p\"}", out var data));
            CollectionAssert.IsEmpty(data.upgrades, "Aucune progression : ni outil, ni robot, ni décor offerts.");
        }

        [Test]
        public void NewerSave_IsNeverLoaded()
        {
            Assert.AreEqual(SaveMigration.Outcome.NewerVersion,
                SaveMigration.TryParse($"{{\"version\":{SaveMigration.CurrentVersion + 1},\"profileId\":\"p\",\"money\":5}}", out _));
        }

        [Test]
        public void CurrentSave_RoundTrips_WorkshopPrankPurchasesAndLook()
        {
            var data = new SaveData { money = 1234, reputation = 40 };
            data.upgrades.AddRange(new[] { "up_base_3", "up_machine_blower", "up_prank_bat", "up_prank_blaster" });
            data.character = new CharacterLook { outfit = 2, cap = 0, gloves = 2 };
            data.settings.musicVolume = 0.3f;
            string json = UnityEngine.JsonUtility.ToJson(data);
            Assert.AreEqual(SaveMigration.Outcome.UpToDate, SaveMigration.TryParse(json, out var back));
            Assert.AreEqual(1234, back.money);
            CollectionAssert.AreEquivalent(data.upgrades, back.upgrades);
            Assert.AreEqual(data.character, back.character);
            Assert.AreEqual(0.3f, back.settings.musicVolume, 1e-5f);
        }
    }
}
