using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Player;
using RugWash.Pranks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 3, dans la vraie scène : inventaire à quatre emplacements (outils et gadgets), casier,
    /// exception du pipi, sauvegarde — sans le raccourci des tests (le joueur passe réellement par le casier).
    /// </summary>
    public class InventoryPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            TestSaveSandbox.Reset(legacyTools: false);
            PlayerInventory.AutomationCarriesOnSelect = false;
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            PlayerInventory.AutomationCarriesOnSelect = true;
            if (game != null && game.Locker != null) game.Locker.Close();
            yield return null;
        }

        UpgradeDefinition Upgrade(string id)
        {
            foreach (var upgrade in game.Upgrades.Catalog) if (upgrade != null && upgrade.id == id) return upgrade;
            return null;
        }

        int Slot(string id)
        {
            for (int i = 0; i < game.Inventory.Carried.Count; i++) if (game.Inventory.Carried[i] == id) return i;
            return -1;
        }

        void Buy(string id)
        {
            var upgrade = Upgrade(id);
            Assert.IsNotNull(upgrade, id);
            game.Wallet.Wallet.Add(upgrade.price + 100);
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(upgrade), id);
        }

        int CountEverywhere(string id)
        {
            int count = 0;
            foreach (string carried in game.Inventory.Carried) if (carried == id) count++;
            foreach (string stored in game.Inventory.Stored()) if (stored == id) count++;
            return count;
        }

        CleaningToolIndex Tool(string id)
        {
            var tools = game.ToolController.Tools;
            for (int i = 0; i < tools.Count; i++) if (tools[i].Definition.id == id) return new CleaningToolIndex(i);
            Assert.Fail($"Outil {id} introuvable");
            return default;
        }

        readonly struct CleaningToolIndex
        {
            public readonly int Value;
            public CleaningToolIndex(int value) => Value = value;
        }

        [UnityTest]
        public IEnumerator NewGame_CarriesTheKit_PurchasesFillTheFreeSlotThenGoToTheLocker()
        {
            // V0.4.2 : brosse de détail au départ (à la place de la brosse standard).
            CollectionAssert.AreEqual(new[] { "soap", "bucket", "detail_brush" }, game.Inventory.Carried, "Kit de départ porté.");
            Assert.AreEqual("4  —", game.Toolbar.SlotText(3), "Quatrième emplacement libre.");

            Buy("up_tool_vacuum");
            yield return null;
            Assert.AreEqual(PlayerInventory.Capacity, game.Inventory.Carried.Count);
            Assert.AreEqual("vacuum", game.Inventory.Carried[3], "Premier achat : dans l'emplacement libre.");
            StringAssert.Contains("Aspirateur", game.Toolbar.SlotText(3));

            string toast = null;
            game.Inventory.Message += m => toast = m;
            Buy("up_tool_rinse");
            yield return null;
            CollectionAssert.DoesNotContain(game.Inventory.Carried, "rinse", "Inventaire plein : au casier.");
            CollectionAssert.Contains(game.Inventory.Stored(), "rinse");
            StringAssert.Contains("casier", toast);
            foreach (string id in new[] { "soap", "detail_brush", "bucket", "vacuum", "rinse" })
                Assert.AreEqual(1, CountEverywhere(id), $"{id} : soit porté, soit au casier — jamais les deux.");

            // Un outil au casier ne se sort pas d'une touche.
            var active = game.ToolController.ActiveTool;
            game.ToolController.SelectIndex(Tool("rinse").Value);
            Assert.AreSame(active, game.ToolController.ActiveTool, "Outil rangé : non sélectionnable avant de le prendre au casier.");
        }

        [UnityTest]
        public IEnumerator Locker_StoresAndTakes_RefusesWhenFull_KeepsALastTool_AndPersists()
        {
            Buy("up_tool_vacuum");
            Buy("up_tool_rinse");
            yield return null;
            Assert.IsTrue(game.Locker.Open(), "Le casier s'ouvre.");
            yield return null;
            Assert.IsFalse(game.Locker.StoredRows[0].ActionButton.interactable, "Inventaire plein : « Prendre » indisponible.");
            Assert.AreEqual(InventoryResult.Full, game.Inventory.Carry("rinse"));

            // Ranger la brosse de détail (bouton de sa ligne), puis prendre la lance.
            int brush = Slot("detail_brush");
            game.Locker.CarriedRows[brush].ActionButton.onClick.Invoke();
            CollectionAssert.DoesNotContain(game.Inventory.Carried, "detail_brush");
            Assert.AreEqual(InventoryResult.Done, game.Inventory.Carry("rinse"));
            CollectionAssert.Contains(game.Inventory.Carried, "rinse");
            CollectionAssert.Contains(game.Inventory.Stored(), "detail_brush");

            // Jamais les mains vides : le dernier outil reste porté.
            foreach (string id in new List<string>(game.Inventory.Carried))
                if (id != "soap") Assert.AreEqual(InventoryResult.Done, game.Inventory.Store(id));
            Assert.AreEqual(InventoryResult.LastTool, game.Inventory.Store("soap"));
            Assert.IsNotNull(game.ToolController.ActiveTool);
            Assert.AreEqual("soap", game.ToolController.ActiveTool.Definition.id, "L'outil en main suit l'inventaire.");
            game.Locker.Close();
            Assert.AreEqual(InventoryResult.Done, game.Inventory.Carry("vacuum"));

            Assert.IsTrue(game.SaveSystem.SaveNow("test"));
            var carried = new List<string>(game.Inventory.Carried);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            yield return null;
            CollectionAssert.AreEqual(carried, game.Inventory.Carried, "Inventaire et casier sauvegardés.");
            CollectionAssert.Contains(game.Inventory.Stored(), "detail_brush");
        }

        [UnityTest]
        public IEnumerator Pee_NeverTakesASlot_BatMustBeCarried()
        {
            game.Reputation.Restore(500);
            Buy("up_tool_vacuum");
            Buy("up_prank_pee");
            Buy("up_prank_bat");
            yield return null;
            Assert.AreEqual(PlayerInventory.Capacity, game.Inventory.Carried.Count);
            CollectionAssert.DoesNotContain(game.Inventory.Carried, "prank:pee");
            CollectionAssert.DoesNotContain(game.Inventory.Stored(), "prank:pee", "Le pipi n'est ni porté ni rangé : hors inventaire.");
            Assert.IsTrue(game.Gadgets.IsAtHand(PrankGadget.Pee), "Le pipi reste toujours disponible.");
            Assert.IsTrue(game.Gadgets.Equip(PrankGadget.Pee));
            game.Gadgets.Unequip();

            CollectionAssert.Contains(game.Inventory.Stored(), "prank:bat", "Batte achetée inventaire plein : au casier.");
            Assert.IsFalse(game.Gadgets.Equip(PrankGadget.Bat), "Au casier : pas en main.");
            Assert.AreEqual(InventoryResult.Done, game.Inventory.Store("bucket"));
            Assert.AreEqual(InventoryResult.Done, game.Inventory.Carry("prank:bat"));
            Assert.IsTrue(game.Gadgets.Equip(PrankGadget.Bat), "Portée : utilisable.");
            StringAssert.Contains("Batte", game.Toolbar.SlotText(Slot("prank:bat")));

            // Ranger la batte en main la range vraiment (et revient aux outils).
            Assert.AreEqual(InventoryResult.Done, game.Inventory.Store("prank:bat"));
            Assert.AreEqual(PrankGadget.None, game.Gadgets.Equipped);
            Assert.IsFalse(game.ToolController.Suspended);
        }
    }
}
