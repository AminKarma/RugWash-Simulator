using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 2, dans la vraie scène : chaque emplacement a sa progression, le niveau suit le joueur
    /// (profil) d'un emplacement à l'autre, le temps de jeu et le mode sont écrits dans l'emplacement.
    /// </summary>
    public class SaveSlotsPlayModeTests
    {
        [SetUp]
        public void Sandbox() => TestSaveSandbox.Reset(legacyTools: false);

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            SaveSlots.Selected = new SaveSlot(SaveMode.Solo, 1);
            yield return null;
        }

        static IEnumerator LoadWorkshop(SaveSlot slot)
        {
            SaveSlots.Selected = slot;
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
        }

        static GameBootstrap Game()
        {
            var game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady, "Atelier non prêt.");
            return game;
        }

        [UnityTest]
        public IEnumerator SoloSlots_HaveIndependentProgress_ButTheLevelFollowsThePlayer()
        {
            var second = new SaveSlot(SaveMode.Solo, 2);
            yield return LoadWorkshop(second);
            var game = Game();
            Assert.AreEqual(second, game.SaveSystem.Slot);
            Assert.AreEqual(0, game.Wallet.Balance, "Nouvel emplacement : nouvelle partie.");
            game.Wallet.Wallet.Add(500);
            game.SaveSystem.AddExperience(PlayerLevelRules.ThresholdFor(3));
            Assert.AreEqual(3, game.SaveSystem.Profile.Level);
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(game.SaveSystem.SaveNow("test"));

            var file = SaveSlots.ReadSummary(second);
            Assert.AreEqual(500, file.Data.money);
            Assert.AreEqual("solo", file.Data.mode);
            Assert.AreEqual(2, file.Data.slotIndex);
            Assert.Greater(file.Data.playSeconds, 1.0, "Temps de jeu réellement mesuré.");

            yield return LoadWorkshop(new SaveSlot(SaveMode.Solo, 1));
            game = Game();
            Assert.AreEqual(0, game.Wallet.Balance, "L'argent de l'emplacement 2 n'apparaît pas dans l'emplacement 1.");
            Assert.AreEqual(3, game.SaveSystem.Profile.Level, "Le niveau appartient au joueur, pas à l'emplacement.");
            StringAssert.Contains("niveau 3", game.Hud.EconomyText);
            Assert.AreEqual(1, game.SaveSystem.Profile.lastSoloSlot, "Le profil retient le dernier emplacement joué.");

            yield return LoadWorkshop(second);
            Assert.AreEqual(500, Game().Wallet.Balance, "L'emplacement 2 retrouve sa progression.");
        }

        [UnityTest]
        public IEnumerator WorkshopWithoutASession_NeverLoadsAMultiplayerSlot()
        {
            yield return LoadWorkshop(new SaveSlot(SaveMode.Multiplayer, 2));
            var game = Game();
            Assert.AreEqual(SaveMode.Solo, game.SaveSystem.Slot.Mode, "Pas de partie en ligne : jamais un emplacement multijoueur.");
            Assert.IsFalse(File.Exists(SaveSlots.PathOf(new SaveSlot(SaveMode.Multiplayer, 2))));
        }

        [UnityTest]
        public IEnumerator DeliveredOrder_GivesExperienceOnce_SavedInTheProfile()
        {
            yield return LoadWorkshop(new SaveSlot(SaveMode.Solo, 1));
            var game = Game();
            int before = game.SaveSystem.Profile.experience;
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            var grid = game.Rug.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                cell.Dust = cell.Stain = cell.Loosened = cell.Residue = cell.Moisture = cell.Foam = 0f;
                grid.SetCell(x, y, cell);
            }
            yield return null;
            var result = game.Orders.Deliver();
            Assert.IsTrue(result != null && result.Accepted);
            int gained = game.SaveSystem.Profile.experience - before;
            Assert.AreEqual(PlayerLevelRules.ForDelivery(true, result.Reward), gained);
            Assert.IsNull(game.Orders.Deliver(), "Pas de seconde livraison…");
            Assert.AreEqual(before + gained, game.SaveSystem.Profile.experience, "… donc pas d'expérience en double.");
            var profile = new ProfileStore(TestSaveSandbox.Directory).Load(out _);
            Assert.AreEqual(before + gained, profile.experience, "Écrite dans le profil.");
        }
    }
}
