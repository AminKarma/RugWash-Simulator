using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Règles métier des commandes, de l'évaluation, du portefeuille, de la boutique et des améliorations.</summary>
    public class GameLoopRulesTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        OrderDefinition Order(string id, int reward = 200, int reputation = 0, float target = 0.8f,
            float maxResidue = -1f, float maxMoisture = -1f, float bonusTime = 0f)
        {
            var order = ScriptableObject.CreateInstance<OrderDefinition>();
            order.id = id;
            order.title = id;
            order.baseReward = reward;
            order.requiredReputation = reputation;
            order.objectives = new OrderObjectives
            {
                targetCleanliness = target,
                limitResidue = maxResidue >= 0f, maxResidue = Mathf.Max(0f, maxResidue),
                limitMoisture = maxMoisture >= 0f, maxMoisture = Mathf.Max(0f, maxMoisture),
                bonusTimeSeconds = bonusTime,
            };
            created.Add(order);
            return order;
        }

        UpgradeDefinition Upgrade(string id, int price, UpgradeEffect effect, float value, string tool = null, UpgradeDefinition prerequisite = null)
        {
            var upgrade = ScriptableObject.CreateInstance<UpgradeDefinition>();
            upgrade.id = id;
            upgrade.price = price;
            upgrade.effect = effect;
            upgrade.value = value;
            upgrade.targetToolId = tool;
            upgrade.prerequisite = prerequisite;
            created.Add(upgrade);
            return upgrade;
        }

        static RugCleaningSummary Summary(float cleanliness, float residue = 0f, float moisture = 0f) =>
            new RugCleaningSummary(cleanliness, cleanliness, moisture, 0f, residue, 0f, 1f - cleanliness, 1f);

        // ---------------------------------------------------------------- Évaluation

        [Test]
        public void PerfectJob_GetsS_AndFullRewardWithBonus()
        {
            var order = Order("o", reward: 200, maxResidue: 0.1f, maxMoisture: 0.3f, bonusTime: 300f);
            var result = OrderEvaluator.Evaluate(order, Summary(1f), 120f);
            Assert.AreEqual(OrderGrade.S, result.Grade);
            Assert.AreEqual(100f, result.Score, 0.01f);
            Assert.IsTrue(result.TimeBonus);
            Assert.AreEqual(Mathf.RoundToInt(200 * 1.25f + 200 * OrderEvaluator.TimeBonusRatio), result.Reward);
        }

        [Test]
        public void BarelyCleanedRug_IsRefused_WithoutPayment()
        {
            var result = OrderEvaluator.Evaluate(Order("o"), Summary(0.1f), 60f);
            Assert.AreEqual(OrderGrade.Refused, result.Grade);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(0, result.Reward);
        }

        [Test]
        public void ResidueAndMoisture_ArePenalized_OnlyWhenEnabled()
        {
            var strict = Order("strict", maxResidue: 0.05f, maxMoisture: 0.2f);
            var lenient = Order("lenient");
            var summary = Summary(0.9f, residue: 0.3f, moisture: 0.7f);
            var strictResult = OrderEvaluator.Evaluate(strict, summary, 100f);
            var lenientResult = OrderEvaluator.Evaluate(lenient, summary, 100f);
            Assert.Less(strictResult.Score, lenientResult.Score);
            Assert.AreEqual(100f, lenientResult.Score, 0.01f, "Un critère non demandé ne doit pas pénaliser.");
            Assert.AreEqual(1, lenientResult.Lines.Count);
            Assert.AreEqual(3, strictResult.Lines.Count);
        }

        [Test]
        public void SGrade_RequiresAllObjectives()
        {
            Assert.AreEqual(OrderGrade.A, OrderEvaluator.GradeFor(96f, allObjectivesMet: false));
            Assert.AreEqual(OrderGrade.S, OrderEvaluator.GradeFor(96f, allObjectivesMet: true));
        }

        [Test]
        public void SlowJob_GetsNoTimeBonus()
        {
            var result = OrderEvaluator.Evaluate(Order("o", bonusTime: 100f), Summary(1f), 500f);
            Assert.IsFalse(result.TimeBonus);
        }

        // ---------------------------------------------------------------- Commandes

        [Test]
        public void AvailableOrder_CanBeStarted_OnlyOneAtATime()
        {
            var a = Order("a");
            var b = Order("b");
            var book = new OrderBook(new[] { a, b });
            Assert.AreEqual(OrderStartResult.Started, book.Start(a, 0));
            Assert.AreEqual(OrderState.Active, book.StateOf(a));
            Assert.AreEqual(OrderStartResult.AnotherOrderActive, book.Start(b, 0));
        }

        [Test]
        public void LockedOrder_RequiresReputation()
        {
            var locked = Order("locked", reputation: 3);
            var book = new OrderBook(new[] { locked });
            Assert.AreEqual(OrderStartResult.ReputationTooLow, book.Start(locked, 2));
            Assert.AreEqual(OrderStartResult.Started, book.Start(locked, 3));
        }

        [Test]
        public void CompletedOrder_PaysOnce_AndCannotBeRedone()
        {
            var order = Order("o");
            var book = new OrderBook(new[] { order });
            var wallet = new Wallet();
            book.Start(order, 0);
            var result = OrderEvaluator.Evaluate(order, Summary(0.95f), 100f);

            Assert.IsTrue(book.Complete(result));
            wallet.Add(result.Reward);
            Assert.IsFalse(book.Complete(result), "Une seconde validation ne doit rien faire.");
            Assert.AreEqual(result.Reward, wallet.Balance);
            Assert.AreEqual(OrderState.Completed, book.StateOf(order));
            Assert.AreEqual(OrderStartResult.NotAvailable, book.Start(order, 0));
        }

        [Test]
        public void RefusedOrder_BecomesAvailableAgain_AndNextOrderCanStart()
        {
            var a = Order("a");
            var b = Order("b");
            var book = new OrderBook(new[] { a, b });
            book.Start(a, 0);
            book.Complete(OrderEvaluator.Evaluate(a, Summary(0.05f), 10f));
            Assert.AreEqual(OrderState.Available, book.StateOf(a));
            Assert.IsNull(book.Active);
            Assert.AreEqual(OrderStartResult.Started, book.Start(b, 0));
        }

        [Test]
        public void AbandonedOrder_ReturnsToAvailable()
        {
            var a = Order("a");
            var book = new OrderBook(new[] { a });
            book.Start(a, 0);
            Assert.IsTrue(book.Abandon());
            Assert.AreEqual(OrderState.Available, book.StateOf(a));
            Assert.IsFalse(book.Abandon());
        }

        // ---------------------------------------------------------------- Portefeuille et boutique

        [Test]
        public void Wallet_RefusesOverspend_WithoutChangingBalance()
        {
            var wallet = new Wallet(100);
            Assert.IsFalse(wallet.TrySpend(150));
            Assert.AreEqual(100, wallet.Balance);
            Assert.IsFalse(wallet.Add(-50));
            Assert.IsFalse(wallet.TrySpend(-10));
            Assert.IsTrue(wallet.TrySpend(100));
            Assert.AreEqual(0, wallet.Balance);
        }

        [Test]
        public void Purchase_WithEnoughFunds_DebitsAndOwns()
        {
            var upgrade = Upgrade("tank", 150, UpgradeEffect.WaterCapacity, 20f);
            var wallet = new Wallet(200);
            var owned = new HashSet<string>();
            Assert.AreEqual(PurchaseResult.Purchased, ShopRules.TryPurchase(upgrade, owned, wallet));
            Assert.AreEqual(50, wallet.Balance);
            Assert.IsTrue(owned.Contains("tank"));
            Assert.AreEqual(PurchaseResult.AlreadyOwned, ShopRules.TryPurchase(upgrade, owned, wallet));
            Assert.AreEqual(50, wallet.Balance);
        }

        [Test]
        public void Purchase_WithoutFunds_IsRefused_BalanceUnchanged()
        {
            var upgrade = Upgrade("tank", 150, UpgradeEffect.WaterCapacity, 20f);
            var wallet = new Wallet(149);
            var owned = new HashSet<string>();
            Assert.AreEqual(PurchaseResult.InsufficientFunds, ShopRules.TryPurchase(upgrade, owned, wallet));
            Assert.AreEqual(149, wallet.Balance);
            Assert.IsEmpty(owned);
        }

        [Test]
        public void Purchase_RequiresPrerequisite()
        {
            var first = Upgrade("t1", 10, UpgradeEffect.WaterCapacity, 20f);
            var second = Upgrade("t2", 10, UpgradeEffect.WaterCapacity, 20f, prerequisite: first);
            var wallet = new Wallet(100);
            var owned = new HashSet<string>();
            Assert.AreEqual(PurchaseResult.MissingPrerequisite, ShopRules.TryPurchase(second, owned, wallet));
            Assert.AreEqual(100, wallet.Balance);
            ShopRules.TryPurchase(first, owned, wallet);
            Assert.AreEqual(PurchaseResult.Purchased, ShopRules.TryPurchase(second, owned, wallet));
        }

        [Test]
        public void Upgrades_ProduceAnnouncedEffects()
        {
            var owned = new[]
            {
                Upgrade("w1", 1, UpgradeEffect.WaterCapacity, 20f),
                Upgrade("w2", 1, UpgradeEffect.WaterCapacity, 20f),
                Upgrade("s", 1, UpgradeEffect.SoapCapacity, 2f),
                Upgrade("r", 1, UpgradeEffect.RecoveryCapacity, 6f),
                Upgrade("b", 1, UpgradeEffect.ToolPower, 1.35f, "brush"),
                Upgrade("e", 1, UpgradeEffect.ToolConsumption, 0.7f, "soap"),
            };
            var stats = EquipmentStats.Compute(40f, 4f, 12f, owned);
            Assert.AreEqual(80f, stats.WaterCapacity, 1e-4f);
            Assert.AreEqual(6f, stats.SoapCapacity, 1e-4f);
            Assert.AreEqual(18f, stats.RecoveryCapacity, 1e-4f);
            Assert.AreEqual(1.35f, stats.PowerMultiplier("brush"), 1e-4f);
            Assert.AreEqual(1f, stats.PowerMultiplier("vacuum"), 1e-4f, "Une amélioration ne touche que l'outil ciblé.");
            Assert.AreEqual(0.7f, stats.ConsumptionMultiplier("soap"), 1e-4f);
        }
    }
}
