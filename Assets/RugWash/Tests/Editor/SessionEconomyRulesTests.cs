using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Economy;
using RugWash.Network;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// V0.2.5 : règles pures de la sauvegarde de session (achats communs ou personnels, atelier réservé à l'hôte), de la
    /// revente (50 % du prix de base, une seule fois, depuis le casier) et des objets qui peuvent tomber sous la batte.
    /// </summary>
    public class SessionEconomyRulesTests
    {
        readonly List<UpgradeDefinition> created = new List<UpgradeDefinition>();

        UpgradeDefinition Make(string id, UpgradeEffect effect, int price, string target = null)
        {
            var upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
            upgrade.id = id;
            upgrade.effect = effect;
            upgrade.price = price;
            upgrade.targetToolId = target;
            created.Add(upgrade);
            return upgrade;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var upgrade in created) Object.DestroyImmediate(upgrade);
            created.Clear();
        }

        [Test]
        public void Resale_IsHalfTheBasePrice_OnlyForCarriableBoughtItems()
        {
            var vacuum = Make("up_tool_vacuum", UpgradeEffect.UnlockTool, 150, "vacuum");
            var bat = Make("up_prank_bat", UpgradeEffect.PrankGadget, 4000, "bat");
            var odd = Make("up_tool_odd", UpgradeEffect.UnlockTool, 91, "odd");
            var power = Make("up_vacuum", UpgradeEffect.ToolPower, 220, "vacuum");
            var robot = Make("up_robot", UpgradeEffect.WorkshopEquipment, 650, "robot");
            var free = Make("up_free", UpgradeEffect.UnlockTool, 0, "brush");

            Assert.AreEqual(75, SessionEconomyRules.ResaleValue(vacuum));
            Assert.AreEqual(2000, SessionEconomyRules.ResaleValue(bat), "Gadget de farce : même règle.");
            Assert.AreEqual(45, SessionEconomyRules.ResaleValue(odd), "Prix impair : arrondi à l'euro inférieur.");
            Assert.IsFalse(SessionEconomyRules.IsResellable(power), "Une amélioration n'est pas un objet.");
            Assert.IsFalse(SessionEconomyRules.IsResellable(robot), "L'atelier ne se revend pas.");
            Assert.IsFalse(SessionEconomyRules.IsResellable(free), "Rien de gratuit ne se revend.");
            Assert.AreEqual(0, SessionEconomyRules.ResaleValue(power));
            Assert.IsTrue(SessionEconomyRules.IsDroppable(vacuum));
            Assert.IsFalse(SessionEconomyRules.IsDroppable(power));
        }

        [Test]
        public void Sale_RemovesTheItem_CreditsOnce_RefusesCarriedOrMissingItems()
        {
            var vacuum = Make("up_tool_vacuum", UpgradeEffect.UnlockTool, 150, "vacuum");
            var owned = new HashSet<string> { vacuum.id };
            var wallet = new Wallet(10);

            Assert.AreEqual(SaleResult.Carried, SessionEconomyRules.TrySell(vacuum, owned, true, wallet), "Porté : à ranger d'abord.");
            Assert.AreEqual(10, wallet.Balance);
            Assert.AreEqual(SaleResult.Sold, SessionEconomyRules.TrySell(vacuum, owned, false, wallet));
            Assert.AreEqual(85, wallet.Balance, "Moitié du prix de base, versée une fois.");
            Assert.IsFalse(owned.Contains(vacuum.id), "L'objet quitte les achats.");
            Assert.AreEqual(SaleResult.NotOwned, SessionEconomyRules.TrySell(vacuum, owned, false, wallet), "Seconde revente refusée.");
            Assert.AreEqual(85, wallet.Balance, "Aucun double crédit.");
            Assert.AreEqual(SaleResult.Unknown, SessionEconomyRules.TrySell(null, owned, false, wallet));
        }

        [Test]
        public void Guest_SeesTheHostWorkshopAndOnlyItsOwnPersonalItems_WorkshopIsHostOnly()
        {
            var vacuum = Make("up_tool_vacuum", UpgradeEffect.UnlockTool, 150, "vacuum");
            var water = Make("up_water_1", UpgradeEffect.WaterCapacity, 150);
            var robot = Make("up_robot", UpgradeEffect.WorkshopEquipment, 650, "robot");
            var tier = Make("up_base_1", UpgradeEffect.WorkshopTier, 400);
            var catalog = new List<UpgradeDefinition> { vacuum, water, robot, tier };

            var hostOwned = new[] { "up_tool_vacuum", "up_robot", "up_base_1" };
            var personal = new[] { "up_water_1", "up_robot" };
            var seen = SessionEconomyRules.GuestOwned(hostOwned, personal, catalog);
            CollectionAssert.AreEquivalent(new[] { "up_water_1", "up_robot", "up_base_1" }, seen,
                "Atelier de l'hôte (commun) + achats personnels de l'invité ; les outils de l'hôte restent les siens.");

            var sanitized = SessionEconomyRules.SanitizePersonal(new[] { "up_robot", "up_water_1", "up_water_1", "up_inconnu" }, catalog);
            CollectionAssert.AreEqual(new[] { "up_water_1" }, sanitized, "Fiche : achats personnels connus, sans doublon ni atelier.");

            var wallet = new Wallet(5000);
            Assert.AreEqual(PurchaseResult.HostOnly, SessionEconomyRules.CheckGuestPurchase(robot, seen, wallet, 99), "Atelier : hôte seulement.");
            Assert.AreEqual(PurchaseResult.Purchased, SessionEconomyRules.CheckGuestPurchase(vacuum, seen, wallet, 99), "Outil : achat personnel possible.");
            Assert.AreEqual(PurchaseResult.AlreadyOwned, SessionEconomyRules.CheckGuestPurchase(water, seen, wallet, 99));
            Assert.AreEqual(PurchaseResult.InsufficientFunds, SessionEconomyRules.CheckGuestPurchase(vacuum, seen, new Wallet(10), 99),
                "Argent de la session.");
            Assert.AreEqual(5000, wallet.Balance, "Une vérification ne débite rien.");
        }

        [Test]
        public void InventoryIds_OfBoughtItems()
        {
            Assert.AreEqual("vacuum", InventoryRules.ItemIdOf(Make("a", UpgradeEffect.UnlockTool, 1, "vacuum")));
            Assert.AreEqual(InventoryRules.GadgetId(Pranks.PrankGadget.Bat), InventoryRules.ItemIdOf(Make("b", UpgradeEffect.PrankGadget, 1, "bat")));
            Assert.IsNull(InventoryRules.ItemIdOf(Make("c", UpgradeEffect.PrankGadget, 1, "pee")), "Le pipi n'occupe aucun emplacement.");
            Assert.IsNull(InventoryRules.ItemIdOf(Make("d", UpgradeEffect.ToolPower, 1, "vacuum")));
        }

        [Test]
        public void SessionView_RoundTrips_WithCarriedItemsAndReserve()
        {
            var view = new PlayerCareer { Money = 468, Reputation = 3, DetergentStock = 52f, SoapReserve = 2.5f, ActiveProduct = "product_silk" };
            view.Upgrades.Add("up_water_1");
            view.Products["product_silk"] = 0.75f;
            view.Carried.AddRange(new[] { "soap", "brush", "vacuum" });
            var back = CareerCodec.Decode(CareerCodec.Encode(view));
            Assert.AreEqual(468, back.Money);
            Assert.AreEqual(3, back.Reputation);
            Assert.AreEqual(52f, back.DetergentStock, 1e-4f);
            Assert.AreEqual(2.5f, back.SoapReserve, 1e-4f);
            CollectionAssert.AreEqual(view.Carried, back.Carried);
            CollectionAssert.AreEquivalent(view.Upgrades, back.Upgrades);
            var truncated = CareerCodec.Encode(view);
            System.Array.Resize(ref truncated, truncated.Length - 3);
            Assert.Throws<System.FormatException>(() => CareerCodec.Decode(truncated), "Message tronqué : ignoré.");
        }
    }
}
