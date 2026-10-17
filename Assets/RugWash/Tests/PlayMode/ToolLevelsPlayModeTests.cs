using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Effects;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 3 : améliorations d'outil par niveaux (effet mesurable, prix croissants, sauvegardées) et
    /// outil doré au niveau maximal (modèle à la première personne, barre d'outils, masque diffusé aux autres joueurs).
    /// </summary>
    public class ToolLevelsPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
        }

        Cleaning.CleaningToolBase Tool(string id)
        {
            foreach (var tool in game.ToolController.Tools)
                if (tool.Definition.id == id) return tool;
            return null;
        }

        [UnityTest]
        public IEnumerator Levels_RaiseThePower_AndTheLastOneMakesTheToolGolden()
        {
            var upgrades = game.Upgrades;
            var vacuum = Tool("vacuum");
            Assert.AreEqual(3, upgrades.MaxToolLevel("vacuum"), "Trois niveaux d'aspirateur.");
            Assert.AreEqual(0, vacuum.Level);
            Assert.IsFalse(vacuum.IsGolden);
            float basePower = vacuum.PowerMultiplier;

            var owned = new List<string>(upgrades.OwnedIds) { "up_vacuum", "up_vacuum_lvl2" };
            upgrades.Restore(owned);
            Assert.AreEqual(2, vacuum.Level);
            Assert.IsFalse(vacuum.IsGolden, "Pas encore au niveau maximal.");
            Assert.AreEqual(basePower * 1.4f * 1.25f, vacuum.PowerMultiplier, 1e-3f, "Effet mesurable de chaque niveau.");

            var third = new List<UpgradeDefinition>(upgrades.Catalog).Find(u => u.id == "up_vacuum_lvl3");
            Assert.IsNotNull(third);
            Assert.Greater(third.price, new List<UpgradeDefinition>(upgrades.Catalog).Find(u => u.id == "up_vacuum_lvl2").price, "Prix croissants.");
            StringAssert.Contains("Niveau 3/3", upgrades.DescribeEffect(third));
            StringAssert.Contains("doré", upgrades.DescribeEffect(third));

            owned.Add("up_vacuum_lvl3");
            upgrades.Restore(owned);
            yield return null;
            Assert.AreEqual(3, vacuum.Level);
            Assert.IsTrue(vacuum.IsGolden, "Niveau maximal : outil doré.");
            Assert.AreEqual(basePower * 1.4f * 1.25f * 1.2f, vacuum.PowerMultiplier, 1e-3f);
            int golden = 0;
            foreach (var candidate in Object.FindObjectsByType<GoldenToolSkin>(FindObjectsInactive.Include))
                if (candidate.IsGolden) golden++;
            Assert.AreEqual(1, golden, "Seul l'aspirateur est doré à la première personne.");

            // Masque des silhouettes (calculé par l'hôte à partir des achats) : l'aspirateur est à l'emplacement 1.
            int mask = upgrades.GoldenMaskFor(owned);
            Assert.AreEqual(1 << (vacuum.Definition.slot - 1), mask);
            Assert.AreEqual(0, upgrades.GoldenMaskFor(new List<string> { "up_vacuum", "up_vacuum_lvl2" }));

            // Barre d'outils : le nom de l'outil doré est marqué.
            game.Inventory.CarryForAutomation(vacuum);
            game.Toolbar.Refresh();
            bool marked = false;
            for (int i = 0; i < RugWash.Player.PlayerInventory.Capacity; i++)
                if (game.Toolbar.SlotText(i).Contains("Aspirateur") && game.Toolbar.SlotText(i).Contains("★")) marked = true;
            Assert.IsTrue(marked, "L'aspirateur doré est marqué dans la barre d'outils.");

            // Sauvegarde : les niveaux sont des achats comme les autres.
            Assert.IsTrue(game.SaveSystem.SaveNow("test niveaux"));
            var saved = new Save.SaveFileStore(TestSaveSandbox.Directory, Save.SaveManager.DefaultFileName).Load().Data;
            CollectionAssert.Contains(saved.upgrades, "up_vacuum_lvl3");
        }

        [UnityTest]
        public IEnumerator Shop_RefusesALevelWithoutThePreviousOne()
        {
            var upgrades = game.Upgrades;
            var second = new List<UpgradeDefinition>(upgrades.Catalog).Find(u => u.id == "up_brush_lvl2");
            Assert.IsNotNull(second);
            game.Wallet.Wallet.Restore(100000);
            game.Reputation.Restore(100);
            Assert.AreEqual(PurchaseResult.MissingPrerequisite, game.Shop.TryPurchase(second), "Niveau 1 d'abord.");
            var first = new List<UpgradeDefinition>(upgrades.Catalog).Find(u => u.id == "up_brush");
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(first));
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(second));
            Assert.AreEqual(2, Tool("brush").Level);
            yield return null;
        }
    }
}
