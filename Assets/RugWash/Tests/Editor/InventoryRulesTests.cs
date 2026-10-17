using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Player;
using RugWash.Pranks;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 3 : règles pures de l'inventaire à quatre emplacements (outils et gadgets portés, le reste au
    /// casier), dont l'exception du pipi et l'absence de duplication.
    /// </summary>
    public class InventoryRulesTests
    {
        static readonly HashSet<string> Tools = new HashSet<string> { "vacuum", "soap", "brush", "rinse", "extractor", "bucket", "broom" };
        static bool IsTool(string id) => Tools.Contains(id);

        [Test]
        public void Default_StartsWithDetergentBrushAndBucket_ThenKeyOrder_FourAtMost()
        {
            var kit = InventoryRules.Normalize(null, new[] { "soap", "brush", "bucket" }, IsTool, PlayerInventory.Capacity);
            CollectionAssert.AreEqual(new[] { "soap", "brush", "bucket" }, kit, "Kit de départ porté, un emplacement libre.");

            var veteran = InventoryRules.Normalize(null, new[] { "vacuum", "soap", "brush", "rinse", "extractor", "bucket", "broom", "prank:bat" },
                IsTool, PlayerInventory.Capacity);
            Assert.AreEqual(PlayerInventory.Capacity, veteran.Count);
            CollectionAssert.AreEqual(new[] { "soap", "brush", "bucket", "rinse" }, veteran);
        }

        [Test]
        public void Saved_KeepsOnlyOwnedItems_NoDuplicates_AlwaysAtLeastOneTool()
        {
            var owned = new[] { "soap", "brush", "prank:bat", "prank:blaster" };
            var restored = InventoryRules.Normalize(new[] { "brush", "brush", "vacuum", "prank:bat" }, owned, IsTool, PlayerInventory.Capacity);
            CollectionAssert.AreEqual(new[] { "brush", "prank:bat" }, restored, "Outil non possédé retiré, doublon retiré.");

            var gadgetsOnly = InventoryRules.Normalize(new[] { "prank:bat", "prank:blaster" }, owned, IsTool, PlayerInventory.Capacity);
            Assert.IsTrue(gadgetsOnly.Exists(IsTool), "Jamais sans outil de nettoyage.");

            var tooMany = InventoryRules.Normalize(new[] { "soap", "brush", "prank:bat", "prank:blaster", "rinse" },
                new[] { "soap", "brush", "prank:bat", "prank:blaster", "rinse" }, IsTool, PlayerInventory.Capacity);
            Assert.AreEqual(PlayerInventory.Capacity, tooMany.Count, "Quatre emplacements au plus.");
        }

        [Test]
        public void CarryAndStore_RespectCapacity_Ownership_AndTheLastTool()
        {
            var owned = new HashSet<string> { "soap", "brush", "rinse", "bucket", "vacuum", "prank:bat" };
            var carried = new List<string> { "soap", "brush", "rinse", "bucket" };
            Assert.AreEqual(InventoryResult.Full, InventoryRules.CanCarry(carried, owned, "vacuum", PlayerInventory.Capacity));
            Assert.AreEqual(InventoryResult.AlreadyCarried, InventoryRules.CanCarry(carried, owned, "soap", PlayerInventory.Capacity));
            Assert.AreEqual(InventoryResult.NotOwned, InventoryRules.CanCarry(carried, owned, "extractor", PlayerInventory.Capacity));
            Assert.AreEqual(InventoryResult.Done, InventoryRules.CanStore(carried, "rinse", IsTool));
            Assert.AreEqual(InventoryResult.NotCarried, InventoryRules.CanStore(carried, "vacuum", IsTool));

            var single = new List<string> { "brush", "prank:bat" };
            Assert.AreEqual(InventoryResult.LastTool, InventoryRules.CanStore(single, "brush", IsTool), "Le dernier outil reste en main.");
            Assert.AreEqual(InventoryResult.Done, InventoryRules.CanStore(single, "prank:bat", IsTool));
            Assert.IsNotEmpty(InventoryRules.Describe(InventoryResult.Full));
        }

        [Test]
        public void Pee_IsNeverAnInventoryItem()
        {
            Assert.IsNull(InventoryRules.GadgetId(PrankGadget.Pee), "Le pipi n'a pas d'identifiant d'inventaire : jamais porté ni rangé.");
            Assert.AreEqual(PrankGadget.Bat, InventoryRules.GadgetOf(InventoryRules.GadgetId(PrankGadget.Bat)));
            Assert.AreEqual(PrankGadget.Blaster, InventoryRules.GadgetOf(InventoryRules.GadgetId(PrankGadget.Blaster)));
            Assert.AreEqual(InventoryResult.NotAnItem, InventoryRules.CanCarry(new List<string>(), new HashSet<string>(), null, PlayerInventory.Capacity));
        }
    }
}
