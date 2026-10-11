using System.Collections;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Boucle complète dans la vraie scène, via les vrais boutons d'interface : accepter une commande,
    /// nettoyer, livrer, être payé une seule fois, revenir à l'atelier, acheter une amélioration.
    /// </summary>
    public class GameLoopPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
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
            if (game != null) game.ToolController.AutomationUseOverride = false;
            Time.timeScale = 1f;
            yield return null;
        }

        /// <summary>Nettoyage complet simulé directement sur la grille (le comportement des outils est testé ailleurs).</summary>
        public static void CleanThoroughly(RugCleaningGrid grid)
        {
            var table = new DirtTypeTable();
            for (int i = 0; i < 60; i++)
            {
                var soap = new SoapOperation { FoamPerSecond = 1.4f, DeltaTime = 0.05f };
                var brush = new BrushOperation { Table = table, Power = 0.6f, DeltaTime = 0.1f };
                var rinse = new RinseOperation { Table = table, Power = 0.9f, WettingPerSecond = 0.9f, DeltaTime = 0.1f };
                if (i < 5) grid.ApplyAll(ref soap);
                grid.ApplyAll(ref brush);
                grid.ApplyAll(ref rinse);
            }
            for (int i = 0; i < 40; i++)
            {
                var extract = new ExtractOperation { Power = 0.9f, DeltaTime = 0.1f };
                grid.ApplyAll(ref extract);
            }
        }

        IEnumerator AcceptFirstOrderThroughBoard()
        {
            Assert.IsTrue(game.OrderBoard.Open(), "Le tableau doit s'ouvrir en jeu.");
            yield return null;
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
            Assert.IsTrue(game.OrderBoard.Rows[0].ActionButton.interactable);
            game.OrderBoard.Rows[0].ActionButton.onClick.Invoke();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FullOrder_AcceptCleanDeliver_PaysOnce_AndReturnsToWorkshop()
        {
            int balanceBefore = game.Wallet.Balance;
            yield return AcceptFirstOrderThroughBoard();

            var order = game.Orders.ActiveOrder;
            Assert.IsNotNull(order, "Le bouton « Accepter » doit démarrer la commande.");
            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State);
            Assert.AreEqual(order.rug, game.Rug.Definition, "Le tapis de la commande doit être chargé.");
            Assert.IsFalse(game.OrderBoard.IsOpen);
            Assert.AreEqual(0f, game.Rug.Cleanliness01, 1e-4f);

            // Un peu de vrai travail aux outils, puis nettoyage complet.
            var uv = new Vector2(0.5f, 0.5f);
            Vector3 target = game.Rug.UVToWorld(uv);
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, Mathf.Atan2(1.62f, 1.1f) * Mathf.Rad2Deg);
            game.ToolController.SelectSlot(1);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.5f);
            game.ToolController.AutomationUseOverride = false;
            Assert.Greater(game.Orders.ElapsedSeconds, 0.3f, "Le chronomètre tourne pendant la commande.");
            CleanThoroughly(game.Rug.Grid);
            yield return null;

            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            game.OrderBoard.DeliverButton.onClick.Invoke();
            yield return null;

            var result = game.Orders.LastResult;
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Accepted, $"Note obtenue : {result.Grade} ({result.Score})");
            Assert.AreEqual(GameState.Results, game.GameState.State);
            Assert.IsTrue(game.Results.IsVisible);
            Assert.AreEqual(balanceBefore + result.Reward, game.Wallet.Balance);
            Assert.Greater(game.Reputation.Points, 0);
            Assert.AreEqual(OrderState.Completed, game.Orders.Book.StateOf(order));

            Assert.IsNull(game.Orders.Deliver(), "Aucune double livraison possible.");
            Assert.AreEqual(balanceBefore + result.Reward, game.Wallet.Balance, "La récompense n'est versée qu'une fois.");

            game.Results.WorkshopButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
            Assert.IsFalse(game.Results.IsVisible);
            Assert.IsNull(game.Orders.ActiveOrder);

            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            Assert.IsFalse(game.OrderBoard.Rows[0].ActionButton.gameObject.activeSelf,
                "Une commande terminée ne propose plus « Accepter ».");
            game.OrderBoard.Rows[1].ActionButton.onClick.Invoke();
            yield return null;
            Assert.IsNotNull(game.Orders.ActiveOrder, "Une nouvelle commande peut être lancée après la précédente.");
        }

        [UnityTest]
        public IEnumerator DirtyDelivery_IsRefused_NoPayment_OrderStaysAvailable()
        {
            yield return AcceptFirstOrderThroughBoard();
            var order = game.Orders.ActiveOrder;
            int balance = game.Wallet.Balance;

            var result = game.Orders.Deliver();
            yield return null;
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(balance, game.Wallet.Balance);
            Assert.AreEqual(OrderState.Available, game.Orders.Book.StateOf(order));

            game.Results.NextOrderButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
            Assert.IsTrue(game.OrderBoard.IsOpen, "« Choisir une autre commande » ouvre le tableau.");
            game.OrderBoard.Close();
        }

        [UnityTest]
        public IEnumerator AbandonFromPause_ReturnsOrderAndWorkshop()
        {
            yield return AcceptFirstOrderThroughBoard();
            var order = game.Orders.ActiveOrder;
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Object.FindAnyObjectByType<RugWash.UI.PauseMenuUI>().AbandonOrder();
            yield return null;
            Assert.IsNull(game.Orders.ActiveOrder);
            Assert.AreEqual(OrderState.Available, game.Orders.Book.StateOf(order));
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator LockedOrders_CannotBeAccepted()
        {
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            var orders = game.Orders.Book.Orders;
            int lockedIndex = -1;
            for (int i = 0; i < orders.Count; i++)
                if (orders[i].requiredReputation > game.Reputation.Points) { lockedIndex = i; break; }
            Assert.GreaterOrEqual(lockedIndex, 0, "Le catalogue doit contenir des commandes verrouillées au départ.");
            Assert.IsFalse(game.OrderBoard.Rows[lockedIndex].ActionButton.gameObject.activeSelf);
            Assert.AreEqual(OrderStartResult.ReputationTooLow, game.Orders.TryStart(orders[lockedIndex]));
            game.OrderBoard.Close();
        }

        [UnityTest]
        public IEnumerator Shop_RefusesWithoutFunds_ThenPurchaseAppliesRealEffects()
        {
            var equipment = game.ToolController.Equipment;
            float waterBefore = equipment.Water.Capacity;
            Assert.IsTrue(game.ShopUI.Open());
            yield return null;

            int waterIndex = IndexOf("up_water_1");
            Assert.AreEqual(0, game.Wallet.Balance);
            Assert.AreEqual(PurchaseResult.InsufficientFunds, game.ShopUI.Buy(waterIndex));
            Assert.AreEqual(0, game.Wallet.Balance);
            Assert.AreEqual(waterBefore, equipment.Water.Capacity);
            StringAssert.Contains("Fonds insuffisants", game.ShopUI.LastMessage);

            game.Wallet.Wallet.Add(1000);
            game.ShopUI.Rows[waterIndex].ActionButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(waterBefore + 20f, equipment.Water.Capacity, 1e-4f, "Le réservoir doit réellement grandir.");
            Assert.AreEqual(1000 - game.Upgrades.Catalog[waterIndex].price, game.Wallet.Balance);

            var brush = game.ToolController.Tools[2];
            float powerBefore = brush.EffectivePower;
            Assert.AreEqual(PurchaseResult.Purchased, game.ShopUI.Buy(IndexOf("up_brush")));
            Assert.AreEqual(powerBefore * 1.35f, brush.EffectivePower, 1e-4f, "La brosse doit réellement être plus puissante.");

            var soap = game.ToolController.Tools[1];
            float consumptionBefore = soap.EffectiveConsumptionPerSecond;
            Assert.AreEqual(PurchaseResult.Purchased, game.ShopUI.Buy(IndexOf("up_soap_eco")));
            Assert.AreEqual(consumptionBefore * 0.7f, soap.EffectiveConsumptionPerSecond, 1e-4f);

            int balance = game.Wallet.Balance;
            Assert.AreEqual(PurchaseResult.AlreadyOwned, game.ShopUI.Buy(waterIndex));
            Assert.AreEqual(balance, game.Wallet.Balance);
            game.ShopUI.Close();
            LogAssert.NoUnexpectedReceived();
        }

        int IndexOf(string upgradeId)
        {
            var catalog = game.Upgrades.Catalog;
            for (int i = 0; i < catalog.Count; i++)
                if (catalog[i].id == upgradeId) return i;
            Assert.Fail($"Amélioration {upgradeId} absente du catalogue.");
            return -1;
        }
    }
}
