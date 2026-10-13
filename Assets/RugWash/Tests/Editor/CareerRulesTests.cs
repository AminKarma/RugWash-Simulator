using System;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Network;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2 — progression personnelle en multijoueur : carrière annoncée bornée, achats, consommation et
    /// récompenses appliqués par l'autorité sur la carrière du bon joueur, format réseau robuste.
    /// </summary>
    public class CareerRulesTests
    {
        readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var asset in created) UnityEngine.Object.DestroyImmediate(asset);
            created.Clear();
        }

        UpgradeDefinition Upgrade(string id, int price, UpgradeDefinition prerequisite = null)
        {
            var upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
            upgrade.id = id;
            upgrade.price = price;
            upgrade.prerequisite = prerequisite;
            created.Add(upgrade);
            return upgrade;
        }

        ProductDefinition Product(string id, int price, float pack, float max)
        {
            var product = ScriptableObject.CreateInstance<ProductDefinition>();
            product.id = id;
            product.packPrice = price;
            product.packLiters = pack;
            product.maxStock = max;
            created.Add(product);
            return product;
        }

        [Test]
        public void AnnouncedCareer_IsBounded_AndFilteredOnTheHostCatalog()
        {
            var brush = Upgrade("up_brush", 100);
            var enzyme = Product("product_enzyme", 40, 2f, 6f);
            var raw = new PlayerCareer { Money = -5, Reputation = 3, ActiveProduct = "product_enzyme" };
            raw.Upgrades.Add("up_brush");
            raw.Upgrades.Add("up_hacked");
            raw.Products["product_enzyme"] = 99f;
            raw.Products["product_unknown"] = 2f;

            var clean = CareerRules.Sanitize(raw, new[] { brush }, new[] { enzyme });
            Assert.AreEqual(0, clean.Money, "Argent négatif ramené à zéro.");
            Assert.AreEqual(3, clean.Reputation);
            CollectionAssert.AreEquivalent(new[] { "up_brush" }, clean.Upgrades, "Identifiant inconnu ignoré.");
            Assert.AreEqual(6f, clean.StockOf("product_enzyme"), "Stock ramené à la capacité.");
            Assert.AreEqual(0f, clean.StockOf("product_unknown"));
            Assert.AreEqual("product_enzyme", clean.ActiveProduct);

            raw.Money = int.MaxValue;
            Assert.AreEqual(Save.SaveValidator.MaxMoney, CareerRules.Sanitize(raw, null, null).Money);
        }

        [Test]
        public void Purchases_UseOnlyThisPlayersMoney_AndRespectTheShopRules()
        {
            var first = Upgrade("up_water_1", 300);
            var second = Upgrade("up_water_2", 500, first);
            var guest = new PlayerCareer { Money = 400 };
            var host = new PlayerCareer { Money = 5000 };

            Assert.AreEqual(PurchaseResult.MissingPrerequisite, CareerRules.TryBuyUpgrade(guest, second));
            Assert.AreEqual(PurchaseResult.Purchased, CareerRules.TryBuyUpgrade(guest, first));
            Assert.AreEqual(100, guest.Money);
            Assert.IsTrue(guest.Upgrades.Contains("up_water_1"));
            Assert.AreEqual(PurchaseResult.AlreadyOwned, CareerRules.TryBuyUpgrade(guest, first));
            Assert.AreEqual(PurchaseResult.InsufficientFunds, CareerRules.TryBuyUpgrade(guest, second), "L'argent de l'hôte ne compte pas.");
            Assert.AreEqual(5000, host.Money, "La carrière de l'hôte n'est jamais touchée.");
            Assert.AreEqual(0, host.Upgrades.Count);

            var enzyme = Product("product_enzyme", 60, 2f, 3f);
            Assert.AreEqual(ProductPurchaseResult.Purchased, CareerRules.TryBuyProduct(guest, enzyme));
            Assert.AreEqual(40, guest.Money);
            Assert.AreEqual(2f, guest.StockOf("product_enzyme"));
            guest.Money = 1000;
            Assert.AreEqual(ProductPurchaseResult.StockFull, CareerRules.TryBuyProduct(guest, enzyme), "Le flacon ne tient plus dans le stock.");
            Assert.AreEqual(1000, guest.Money, "Refus : rien n'est débité.");
        }

        [Test]
        public void Consumption_TakesFromThisPlayersStock_AndNeverGoesNegative()
        {
            var career = new PlayerCareer();
            career.Products["product_silk"] = 0.5f;
            Assert.AreEqual(0.2f, CareerRules.Consume(career, "product_silk", 0.2f), 1e-6f);
            Assert.AreEqual(0.3f, career.StockOf("product_silk"), 1e-6f);
            Assert.AreEqual(0.3f, CareerRules.Consume(career, "product_silk", 5f), 1e-6f, "Seulement ce qui reste.");
            Assert.IsFalse(career.Products.ContainsKey("product_silk"), "Stock épuisé : retiré.");
            Assert.AreEqual(0f, CareerRules.Consume(career, "product_silk", 1f));
        }

        [Test]
        public void Reward_PaysAcceptedOrders_AndAppliesReputationToThisPlayer()
        {
            var order = ScriptableObject.CreateInstance<OrderDefinition>();
            created.Add(order);
            var career = new PlayerCareer { Money = 10, Reputation = 1 };
            CareerRules.Reward(career, new OrderResult { Order = order, Grade = OrderGrade.A, Reward = 250, ReputationDelta = 2 });
            Assert.AreEqual(260, career.Money);
            Assert.AreEqual(3, career.Reputation);
            CareerRules.Reward(career, new OrderResult { Order = order, Grade = OrderGrade.Refused, Reward = 0, ReputationDelta = -5 });
            Assert.AreEqual(260, career.Money, "Tapis refusé : pas de paiement.");
            Assert.AreEqual(0, career.Reputation, "Réputation bornée à zéro.");
        }

        [Test]
        public void CareerCodec_RoundTrips_AndRejectsTruncatedOrForgedMessages()
        {
            var career = new PlayerCareer { Money = 12345, Reputation = 17, ActiveProduct = "product_enzyme" };
            career.Upgrades.Add("up_brush");
            career.Upgrades.Add("up_rinse");
            career.Products["product_enzyme"] = 2.5f;
            var data = CareerCodec.Encode(career);
            var back = CareerCodec.Decode(data);
            Assert.AreEqual(12345, back.Money);
            Assert.AreEqual(17, back.Reputation);
            CollectionAssert.AreEquivalent(career.Upgrades, back.Upgrades);
            Assert.AreEqual(2.5f, back.StockOf("product_enzyme"));
            Assert.AreEqual("product_enzyme", back.ActiveProduct);

            Assert.Throws<FormatException>(() => CareerCodec.Decode(new byte[] { CareerCodec.Version, 1, 2 }), "Message tronqué.");
            Assert.Throws<FormatException>(() => CareerCodec.Decode(new byte[] { 99 }), "Version inconnue.");
            var forged = (byte[])data.Clone();
            forged[9] = 200;
            Assert.Throws<FormatException>(() => CareerCodec.Decode(forged), "Nombre d'entrées hors limites.");
            Assert.Throws<FormatException>(() => CareerCodec.Decode(null));
        }
    }
}
