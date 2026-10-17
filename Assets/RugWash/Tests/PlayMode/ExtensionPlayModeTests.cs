using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Pranks;
using RugWash.Save;
using RugWash.Workshop;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 3 : agrandissement de l'atelier (zone de lavage 2, deux commandes en même temps) et
    /// machine à laver (chargement, cycle, sortie, sauvegarde, réplique d'un invité).
    /// </summary>
    public class ExtensionPlayModeTests
    {
        GameBootstrap game;

        IEnumerator LoadWorkshop(bool resetSave = true)
        {
            if (resetSave) TestSaveSandbox.Reset(legacyTools: true);
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
            if (game != null && game.Orders != null) game.Orders.Remote = null;
            yield return null;
        }

        void Buy(params string[] upgradeIds)
        {
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.AddRange(upgradeIds);
            game.Upgrades.Restore(owned);
        }

        /// <summary>Commandes acceptables maintenant (réputation, état), dans l'ordre du carnet.</summary>
        List<OrderDefinition> Startable(int count)
        {
            var list = new List<OrderDefinition>();
            foreach (var order in game.Orders.Book.Orders)
                if (game.Orders.Book.CanStart(order, game.Orders.BoardReputation, int.MaxValue) == OrderStartResult.Started)
                {
                    list.Add(order);
                    if (list.Count == count) break;
                }
            Assert.AreEqual(count, list.Count, "Assez de commandes accessibles pour le test.");
            return list;
        }

        [UnityTest]
        public IEnumerator BeforePurchase_OneBay_ClosedWall_AndASecondOrderIsRefused()
        {
            yield return LoadWorkshop();
            var extension = game.Extension;
            Assert.IsFalse(extension.IsUnlocked);
            Assert.AreEqual(1, game.Orders.UnlockedBayCount);
            Assert.AreEqual(2, game.Orders.BayCount, "La zone 2 existe dans la scène (verrouillée).");
            Assert.IsFalse(game.Washer.IsOwned);
            var orders = Startable(2);
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(orders[0]));
            Assert.AreEqual(0, game.Orders.BayOf(orders[0]));
            Assert.AreEqual(OrderStartResult.AnotherOrderActive, game.Orders.TryStart(orders[1]), "Une seule zone avant l'agrandissement.");
            Assert.IsFalse(game.Orders.RugOf(1).IsLoaded);
        }

        [UnityTest]
        public IEnumerator Extension_TwoOrdersAtOnce_EachBayDeliveredSeparately()
        {
            yield return LoadWorkshop();
            Buy("up_extension");
            yield return null;
            Assert.IsTrue(game.Extension.IsUnlocked, "Achat : l'annexe s'ouvre.");
            Assert.AreEqual(2, game.Orders.UnlockedBayCount);
            Assert.IsFalse(game.Washer.IsOwned, "La machine est un achat séparé.");

            var orders = Startable(3);
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(orders[0]));
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(orders[1]));
            Assert.AreEqual(0, game.Orders.BayOf(orders[0]), "Zone du joueur d'abord (il est dans l'atelier).");
            Assert.AreEqual(1, game.Orders.BayOf(orders[1]), "Puis la zone libre suivante.");
            Assert.IsTrue(game.Orders.RugOf(1).IsLoaded, "Tapis posé dans l'annexe.");
            Assert.AreSame(orders[1].rug, game.Orders.RugOf(1).Definition);
            Assert.IsFalse(game.Orders.HasFreeBay);
            Assert.AreEqual(OrderStartResult.AnotherOrderActive, game.Orders.TryStart(orders[2]), "Les deux zones sont occupées.");
            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State);

            // Le joueur entre dans l'annexe : la zone courante devient la zone 2.
            game.PlayerController.Teleport(game.Orders.RugOf(1).transform.position + new Vector3(-2.6f, 0.06f, 0f), 90f);
            yield return null;
            Assert.AreEqual(1, game.Orders.FocusBay);
            Assert.AreSame(orders[1], game.Orders.ActiveOrder);
            yield return new WaitForSeconds(0.25f);
            StringAssert.Contains("Zone 2", game.Hud.OrderText);
            StringAssert.Contains("Zone 1", game.Hud.OrderText, "L'autre zone est rappelée.");

            GameLoopPlayModeTests.CleanThoroughly(game.Orders.RugOf(1).Grid);
            int balance = game.Wallet.Balance;
            var result = game.Orders.Deliver(1);
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Bay);
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(balance + result.Reward, game.Wallet.Balance);
            Assert.AreSame(orders[0], game.Orders.OrderOf(0), "La zone 1 garde sa commande.");
            Assert.AreEqual(GameState.Results, game.GameState.State);
            game.Results.WorkshopButton.onClick.Invoke();
            yield return null;
            Assert.IsFalse(game.Orders.RugOf(1).IsLoaded, "Tapis livré retiré.");
            Assert.IsTrue(game.Orders.RugOf(0).IsLoaded, "Le tapis de la zone 1 reste posé.");
            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State, "Une commande reste en cours.");
            Assert.IsNull(game.Orders.Deliver(1), "Rien à livrer deux fois.");

            Assert.IsTrue(game.Orders.Abandon(0));
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
            Assert.AreEqual(OrderState.Available, game.Orders.Book.StateOf(orders[0]));
        }

        [UnityTest]
        public IEnumerator Pranks_StainTheRugOfZone2()
        {
            yield return LoadWorkshop();
            Buy("up_extension", "up_prank_pee");
            var orders = Startable(2);
            game.Orders.TryStart(orders[0]);
            game.Orders.TryStart(orders[1]);
            yield return null;
            var rug2 = game.Orders.RugOf(1);
            var actor = new TestPrankActor { Eye = rug2.transform.position + new Vector3(0f, 1.6f, -1.2f) };
            var request = new PrankRequest { Gadget = PrankGadget.Pee, Target = PrankTarget.Rug, Point = rug2.UVToWorld(new Vector2(0.5f, 0.5f)), Origin = actor.Eye };
            float dirt = rug2.Grid.TotalDirt;
            Assert.AreEqual(PrankResult.Done, game.Pranks.Execute(actor, request, out var outcome));
            Assert.Greater(rug2.Grid.TotalDirt, dirt, "Tache sur le tapis de la zone 2.");
            Assert.Greater(game.Pranks.AddedTo(rug2), 0f);
            Assert.AreEqual(0f, game.Pranks.AddedToRug, "Limite par tapis : celle de la zone 1 n'est pas entamée.");
        }

        sealed class TestPrankActor : IPrankActor
        {
            public ulong Id { get; set; } = 3;
            public string Name => "Farceur";
            public Vector3 Eye { get; set; }
            public bool IsDown => false;
            public int Money => 0;
            public bool Owns(PrankGadget gadget) => true;
            public bool TrySpend(int amount) => amount <= 0;
        }

        [UnityTest]
        public IEnumerator Washer_FreesTheBay_Washes_AndReturnsTheRugToAFreeBay()
        {
            yield return LoadWorkshop();
            Buy("up_extension", "up_washer");
            yield return null;
            var washer = game.Washer;
            Assert.IsTrue(washer.IsOwned);
            Assert.AreEqual(WasherState.Free, washer.State);
            var orders = Startable(2);
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(orders[0]));
            var rug = game.Orders.RugOf(0);
            float initial = rug.Grid.InitialDirt;
            float dirtBefore = rug.Grid.TotalDirt;
            yield return new WaitForSeconds(0.3f);
            float elapsed = game.Orders.ElapsedOf(0);

            Assert.IsTrue(game.WasherUI.Open(washer));
            yield return null;
            game.WasherUI.BayRows[0].ActionButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(WasherState.Washing, washer.State, game.WasherUI.Message);
            Assert.AreEqual(900f, washer.CycleSeconds, 1e-3f, "Cycle de 15 minutes.");
            Assert.IsNull(game.Orders.OrderOf(0), "La zone 1 est libérée…");
            Assert.IsFalse(rug.IsLoaded);
            Assert.AreEqual(OrderState.Active, game.Orders.Book.StateOf(orders[0]), "…mais la commande reste en cours (en machine).");
            Assert.IsTrue(game.Orders.IsHeld(orders[0]));
            Assert.IsTrue(game.Orders.HasFreeBay);
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(orders[1]), "Un autre tapis pendant le lavage.");
            Assert.AreEqual(0, game.Orders.BayOf(orders[1]));
            game.WasherUI.Close();

            washer.AdvanceForTests(899f);
            Assert.AreEqual(WasherState.Washing, washer.State);
            Assert.Greater(washer.Remaining, 0f);
            washer.AdvanceForTests(2f);
            Assert.AreEqual(WasherState.Done, washer.State);

            StringAssert.Contains("posé", washer.Execute(WasherAction.Collect, 1));
            Assert.AreEqual(WasherState.Free, washer.State);
            Assert.AreEqual(1, game.Orders.BayOf(orders[0]), "Tapis lavé reposé dans la zone libre (zone 2).");
            var washed = game.Orders.RugOf(1);
            Assert.AreEqual(initial, washed.Grid.InitialDirt, 1e-3f, "La propreté se mesure toujours depuis l'état d'origine.");
            Assert.Greater(washer.LastWashRemoved, 0f);
            Assert.Less(washed.Grid.TotalDirt, dirtBefore * 0.85f, "La machine a vraiment lavé.");
            Assert.Greater(washed.Grid.TotalDirt, 0f, "Il reste des taches à finir.");
            Assert.Greater(washed.Grid.AverageMoisture, 0.5f, "Le tapis ressort trempé.");
            Assert.AreEqual(elapsed, game.Orders.ElapsedOf(1), 0.25f, "Chronomètre suspendu pendant le lavage.");
            Assert.IsFalse(game.Orders.IsHeld(orders[0]));
        }

        [UnityTest]
        public IEnumerator Washer_StateIsSaved_AndTheCycleDoesNotAdvanceWhileTheGameIsClosed()
        {
            yield return LoadWorkshop();
            Buy("up_extension", "up_washer");
            yield return null;
            var order = Startable(1)[0];
            game.Orders.TryStart(order);
            yield return null;
            StringAssert.Contains("en machine", game.Washer.Execute(WasherAction.Load, 0));
            game.Washer.AdvanceForTests(300f);
            float remaining = game.Washer.Remaining;
            Assert.IsTrue(game.SaveSystem.SaveNow("test machine"));
            var saved = new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName).Load().Data;
            Assert.AreEqual((int)WasherState.Washing, saved.workshop.washer.state);
            Assert.AreEqual(order.id, saved.workshop.washer.orderId);
            Assert.IsNotEmpty(saved.workshop.washer.grid, "État du tapis conservé.");

            yield return LoadWorkshop(resetSave: false);
            var washer = game.Washer;
            Assert.IsTrue(washer.IsOwned);
            Assert.AreEqual(WasherState.Washing, washer.State);
            Assert.AreEqual(order.id, washer.OrderId);
            Assert.AreEqual(remaining, washer.Remaining, 1.5f, "Le temps passé hors du jeu ne compte pas.");
            var restored = game.Orders.Resolve(order.id);
            Assert.AreEqual(OrderState.Active, game.Orders.Book.StateOf(restored), "Commande en machine toujours en cours.");
            Assert.IsTrue(game.Orders.IsHeld(restored));
            Assert.AreEqual(OrderStartResult.NotAvailable, game.Orders.TryStart(restored), "Pas acceptable deux fois.");

            StringAssert.Contains("abandonnée", washer.Execute(WasherAction.Abandon, 0));
            Assert.AreEqual(OrderState.Available, game.Orders.Book.StateOf(restored));
        }

        sealed class FakeRequests : IWorkshopRequests
        {
            public readonly List<string> Calls = new List<string>();
            public void RequestStartOrder(string orderId, int bay) => Calls.Add($"start {orderId} {bay}");
            public void RequestDeliver(int bay) => Calls.Add($"deliver {bay}");
            public void RequestAbandon(int bay) => Calls.Add($"abandon {bay}");
            public void RequestWasher(byte action, int bay) => Calls.Add($"washer {action} {bay}");
            public void RequestUpgrade(string upgradeId) { }
            public void RequestProduct(string productId) { }
            public void RequestRobotToggle() { }
            public void RequestDetergentCan() { }
            public void RequestDetergentRefill(float liters) { }
            public void RequestMachine(string machineId) { }
            public void RequestPrank(PrankRequest request) { }
        }

        [UnityTest]
        public IEnumerator Guest_ReplicatesBothBaysAndTheWasher_AndOnlySendsRequests()
        {
            yield return LoadWorkshop();
            Buy("up_extension", "up_washer");
            var requests = new FakeRequests();
            game.Orders.Remote = requests;
            game.Washer.Remote = requests;
            var orders = Startable(3);

            // L'hôte a posé deux commandes et en a une dans sa machine.
            game.Orders.ApplyRemoteStart(orders[0], 12f, 0);
            game.Orders.ApplyRemoteStart(orders[1], 30f, 1);
            game.Washer.ApplyRemote(WasherState.Washing, 600f, 900f, orders[2].id);
            Assert.AreSame(orders[1], game.Orders.OrderOf(1));
            Assert.AreEqual(30f, game.Orders.ElapsedOf(1), 1e-3f);
            Assert.AreEqual(OrderState.Active, game.Orders.Book.StateOf(orders[2]), "Commande en machine : pas acceptable chez l'invité.");
            Assert.AreEqual(WasherState.Washing, game.Washer.State);

            // Demandes : jamais exécutées localement.
            game.Orders.Deliver(1);
            game.Orders.Abandon(0);
            game.Washer.Request(WasherAction.Collect, 1);
            CollectionAssert.AreEqual(new[] { "deliver 1", "abandon 0", "washer 2 1" }, requests.Calls);
            Assert.AreSame(orders[1], game.Orders.OrderOf(1), "L'hôte décide.");

            // Le tapis sort de la machine vers la zone 2 (ordre d'arrivée quelconque des mises à jour).
            game.Orders.ApplyRemoteReturn(1);
            Assert.AreEqual(OrderState.Available, game.Orders.Book.StateOf(orders[1]), "Commande abandonnée chez l'hôte.");
            game.Orders.ApplyRemoteStart(orders[2], 40f, 1);
            Assert.AreEqual(OrderState.Active, game.Orders.Book.StateOf(orders[2]));
            game.Washer.ApplyRemote(WasherState.Free, 0f, 0f, string.Empty);
            Assert.AreEqual(OrderState.Active, game.Orders.Book.StateOf(orders[2]), "Toujours en cours : elle est dans la zone 2.");
            Assert.AreSame(orders[2], game.Orders.OrderOf(1));
            Assert.IsFalse(game.Orders.IsHeld(orders[2]));
            yield return null;
        }
    }
}
