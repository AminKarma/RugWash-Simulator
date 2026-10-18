using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Effects;
using RugWash.Orders;
using RugWash.Pranks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// V0.2.5, en solo (le réseau est couvert par l'autotest à deux processus) : revente des objets, niveaux d'outil et outils
    /// dorés, lance au niveau maximal, transition vers le tapis suivant depuis un résumé, fût de stock de 40 L, atelier
    /// réservé à l'hôte côté invité, sons préchargés.
    /// </summary>
    public class V025PlayModeTests
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
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null && game.Shop != null) game.Shop.Remote = null;
            yield return null;
        }

        CleaningToolBase Tool(string id)
        {
            foreach (var tool in game.ToolController.Tools)
                if (tool.Definition.id == id) return tool;
            return null;
        }

        void Own(params string[] ids)
        {
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.AddRange(ids);
            game.Upgrades.Restore(owned);
        }

        static ToolAim AimAt(RugCleaningController surface, Vector3 point)
        {
            Vector3 up = surface.transform.up;
            return new ToolAim { HasHit = true, Point = point, Normal = up, Distance = 0.8f, Rug = surface, Ray = new Ray(point + up, -up) };
        }

        // ------------------------------------------------------------------ Revente

        [UnityTest]
        public IEnumerator Resale_HalfBasePrice_OnlyFromTheLocker_OnceAndSaved()
        {
            var squeegee = game.Upgrades.Find("up_tool_squeegee");
            game.Wallet.Wallet.Restore(1000);
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(squeegee));
            yield return null;
            int afterBuy = game.Wallet.Balance;
            var inventory = game.ToolController.Inventory;
            // Porté (inventaire plein ou non) : on le force porté, la revente doit être refusée.
            if (!new List<string>(inventory.Carried).Contains("squeegee"))
            {
                inventory.Store(inventory.Carried[inventory.Carried.Count - 1]);
                Assert.AreEqual(Player.InventoryResult.Done, inventory.Carry("squeegee"));
            }
            Assert.IsTrue(game.ShopUI.Open());
            Assert.IsFalse(game.ShopUI.AskSale(squeegee), "Objet porté : à ranger au casier d'abord.");
            Assert.AreEqual(SaleResult.Carried, game.Shop.CheckSale(squeegee));
            Assert.AreEqual(Player.InventoryResult.Done, inventory.Store("squeegee"));

            // Montant annoncé avant toute vente, puis confirmation.
            Assert.IsTrue(game.ShopUI.AskSale(squeegee));
            Assert.AreEqual(squeegee, game.ShopUI.PendingSale);
            Assert.AreEqual(afterBuy, game.Wallet.Balance, "Annoncer ne vend rien.");
            Assert.AreEqual(SaleResult.Sold, game.ShopUI.ConfirmSale());
            Assert.AreEqual(afterBuy + squeegee.price / 2, game.Wallet.Balance, "Exactement la moitié du prix de base.");
            Assert.IsFalse(game.Upgrades.IsOwned(squeegee));
            Assert.IsFalse(inventory.Stored().Contains("squeegee"), "L'objet a quitté le casier.");
            Assert.AreEqual(SaleResult.Unknown, game.ShopUI.ConfirmSale(), "Plus rien à confirmer : aucune double vente.");
            Assert.AreEqual(SaleResult.NotOwned, game.Shop.TrySell(squeegee));
            Assert.AreEqual(afterBuy + squeegee.price / 2, game.Wallet.Balance);
            game.ShopUI.Close();

            // Kit de départ et améliorations : jamais revendables.
            Assert.AreEqual(SaleResult.NotResellable, game.Shop.TrySell(game.Upgrades.Find("up_water_1")));
            var saved = new Save.SaveFileStore(TestSaveSandbox.Directory, Save.SaveManager.DefaultFileName).Load().Data;
            Assert.IsFalse(saved.upgrades.Contains(squeegee.id), "Revente sauvegardée.");
            Assert.AreEqual(game.Wallet.Balance, saved.money);
        }

        // ------------------------------------------------------------------ Niveaux d'outil et outils dorés

        float Removed(CleaningToolBase tool, params string[] levels)
        {
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.RemoveAll(id => id.StartsWith("up_vacuum") || id.StartsWith("up_rinse") || id.StartsWith("up_soap"));
            owned.AddRange(levels);
            game.Upgrades.Restore(owned);
            var order = game.Orders.Book.Orders[0];
            game.Rug.LoadRug(order.rug, order.dirt, 4242);
            var grid = game.Rug.Grid;
            float before = grid.TotalDirt;
            for (int frame = 0; frame < 240; frame++)
            {
                float t = frame / 240f;
                game.ToolController.Equipment.Water.Refill();
                game.ToolController.Equipment.Soap.Refill();
                tool.Tick(true, AimAt(game.Rug, game.Rug.UVToWorld(new Vector2(0.2f + 0.6f * Mathf.PingPong(t * 6f, 1f), 0.3f + 0.4f * t))), 1f / 60f);
            }
            tool.StopUsing();
            return before - grid.TotalDirt;
        }

        [UnityTest]
        public IEnumerator GoldenTool_IsClearlyStrongerThanLevelTwo_OnTheSameRug()
        {
            var vacuum = Tool("vacuum");
            float level2 = Removed(vacuum, "up_tool_vacuum", "up_vacuum", "up_vacuum_lvl2");
            Assert.AreEqual(1.08f, vacuum.RadiusMultiplier, 1e-4f, "Niveau 2 : zone plus large.");
            float golden = Removed(vacuum, "up_tool_vacuum", "up_vacuum", "up_vacuum_lvl2", "up_vacuum_lvl3");
            Assert.IsTrue(vacuum.IsGolden);
            Assert.AreEqual(1.2f, vacuum.RadiusMultiplier, 1e-4f, "Niveau 3 doré : zone encore plus large.");
            Assert.Greater(golden, level2 * 1.1f, $"Aspirateur doré : nettement plus efficace que le niveau 2 ({golden:0.0} contre {level2:0.0}).");

            var rinse = Tool("rinse");
            float rinse2 = Removed(rinse, "up_tool_rinse", "up_rinse", "up_rinse_lvl2");
            float rinse3 = Removed(rinse, "up_tool_rinse", "up_rinse", "up_rinse_lvl2", "up_rinse_lvl3");
            Assert.Greater(rinse3, rinse2 * 1.1f, $"Lance dorée : nettement plus efficace ({rinse3:0.0} contre {rinse2:0.0}).");

            // Pulvérisateur : plus de mousse par seconde au niveau doré, jamais plus de résidus par seconde.
            var soap = Tool("soap");
            Removed(soap, "up_soap_power", "up_soap_lvl2");
            float residue2 = game.Rug.Grid.ComputeSummary().AverageResidue01;
            float foam2 = game.Rug.Grid.ComputeSummary().AverageFoam01;
            Removed(soap, "up_soap_power", "up_soap_lvl2", "up_soap_lvl3");
            var golden3 = game.Rug.Grid.ComputeSummary();
            Assert.Greater(golden3.AverageFoam01, foam2, "Mousse : le doré en fait plus (et sur une zone plus large).");
            Assert.LessOrEqual(golden3.AverageResidue01 / golden3.AverageFoam01, residue2 / foam2 * 1.01f,
                "Résidus par unité de mousse : pas plus qu'au niveau 2 (avant : +18 % de résidus au niveau doré).");

            // Cohérent après sauvegarde et chargement : les statistiques viennent des achats sauvegardés.
            Own("up_tool_vacuum", "up_vacuum", "up_vacuum_lvl2", "up_vacuum_lvl3");
            Assert.IsTrue(game.SaveSystem.SaveNow("niveaux"));
            var saved = new Save.SaveFileStore(TestSaveSandbox.Directory, Save.SaveManager.DefaultFileName).Load().Data;
            game.Upgrades.Restore(saved.upgrades);
            Assert.IsTrue(vacuum.IsGolden);
            Assert.AreEqual(1.2f, vacuum.RadiusMultiplier, 1e-4f);
            yield return null;
        }

        // ------------------------------------------------------------------ Lance au niveau maximal

        [UnityTest]
        public IEnumerator MaxLevelLance_FlushesSoapInOnePass_LeavesOtherDirtToTheNormalRinse_AndShootsFlat()
        {
            var rinse = (WaterRinseTool)Tool("rinse");
            var order = game.Orders.Book.Orders[0];
            Vector3 point = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));

            float StainAfterOnePass(bool golden, out float foam, out float residue)
            {
                var owned = new List<string> { "up_tool_rinse", "up_rinse", "up_rinse_lvl2" };
                if (golden) owned.Add("up_rinse_lvl3");
                game.Upgrades.Restore(owned);
                game.Rug.LoadRug(order.rug, order.dirt, 99);
                var grid = game.Rug.Grid;
                var soapy = new DepositOperation { Foam = 0.9f, Residue = 0.6f };
                grid.ApplyRect(new RectInt(0, 0, grid.Width, grid.Height), 1f, ref soapy);
                game.ToolController.Equipment.Water.Refill();
                rinse.Tick(true, AimAt(game.Rug, point), 1f / 30f);
                rinse.StopUsing();
                Assert.IsTrue(game.Rug.TrySampleCell(point, out var cell));
                foam = cell.Foam;
                residue = cell.Residue;
                return cell.Stain + cell.Dust + cell.Loosened;
            }

            float normalDirt = StainAfterOnePass(false, out float normalFoam, out _);
            Assert.Greater(normalFoam, 0.1f, "Lance niveau 2 : la mousse demande plusieurs passages.");
            Assert.IsFalse(rinse.IsMaxLevelJet);
            float goldenDirt = StainAfterOnePass(true, out float goldenFoam, out float goldenResidue);
            Assert.IsTrue(rinse.IsMaxLevelJet);
            Assert.AreEqual(0f, goldenFoam, 1e-5f, "Lance dorée : la mousse part d'un seul coup.");
            Assert.AreEqual(0f, goldenResidue, 1e-5f, "…et les résidus de produit avec elle.");
            Assert.Greater(goldenDirt, 0f, "La saleté, elle, n'est pas effacée d'un coup.");
            Assert.Greater(goldenDirt, normalDirt * 0.5f, "Seul le rinçage normal (un peu plus puissant) agit sur la saleté.");

            // Jet tendu : aucune retombée, gouttes rapides, émission avancée sur la buse.
            rinse.Tick(true, AimAt(game.Rug, point), 1f / 60f);
            yield return null;
            var particles = FindJet(rinse);
            Assert.IsNotNull(particles);
            Assert.AreEqual(0f, particles.main.gravityModifierMultiplier, 1e-5f);
            Assert.GreaterOrEqual(particles.main.startSpeed.constantMin, 20f);
            Assert.Greater(particles.shape.position.z, 0.3f, "Gouttes étirées : leur traînée commence sur la buse.");
            rinse.StopUsing();
        }

        static ParticleSystem FindJet(WaterRinseTool tool)
        {
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (system.name == "WaterJet") return system;
            return null;
        }

        // ------------------------------------------------------------------ Transition depuis un résumé

        [UnityTest]
        public IEnumerator AcceptingTheNextRug_ClosesTheSummaryOfThePreviousOne()
        {
            var orders = game.Orders;
            var first = orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.Started, orders.TryStart(first));
            yield return null;
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            Assert.IsNotNull(orders.Deliver());
            yield return null;
            Assert.AreEqual(GameState.Results, game.GameState.State);
            Assert.IsTrue(game.Results.IsVisible);

            // Un autre joueur accepte le tapis suivant pendant que le résumé est ouvert : plus de blocage.
            OrderDefinition next = null;
            foreach (var order in orders.Book.Orders)
                if (order != first && orders.Book.CanStart(order, orders.BoardReputation, int.MaxValue) == OrderStartResult.Started) next = order;
            Assert.IsNotNull(next);
            Assert.AreEqual(OrderStartResult.Started, orders.TryStart(next, 0), "La zone du tapis livré est reprise.");
            yield return null;
            Assert.AreNotEqual(GameState.Results, game.GameState.State, "Retour à l'atelier pour tout le monde.");
            Assert.IsFalse(game.Results.IsVisible, "Le résumé s'est fermé.");
            Assert.AreEqual(next, orders.OrderOf(0));
            Assert.AreEqual(next.rug, game.Rug.Definition, "Le nouveau tapis est chargé une fois, dans la zone.");

            // Invité : livraison reçue après le démarrage du tapis suivant (désordre réseau) → enregistrée sans résumé.
            game.Rug.Unload();
            var summary = new RugCleaningSummary(1f, 1f, 0f, 0f, 0f, 0f, 0f, 10f);
            orders.ApplyRemoteDelivery(first, summary, 30f, 0, enterResults: false);
            Assert.AreNotEqual(GameState.Results, game.GameState.State);
            Assert.IsFalse(game.Results.IsVisible);
        }

        // ------------------------------------------------------------------ Fût de stock de 40 L

        [UnityTest]
        public IEnumerator StockDrum_ShowsTheRealDetergentStock()
        {
            ContainerLevel drum = null;
            foreach (var level in Object.FindObjectsByType<ContainerLevel>(FindObjectsSortMode.None))
                if (level.name == "Drum_Liquid") drum = level;
            Assert.IsNotNull(drum, "Liquide visible dans le fût du poste détergent.");
            var stock = game.Detergent;
            stock.Restore(0f);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0f, drum.ShownFill, 0.01f, "Stock vide : fût vide.");
            stock.Restore(stock.MaxStock * 0.5f);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(0.5f, drum.ShownFill, 0.02f, "Niveau = stock réel / contenance.");
            float before = drum.ShownFill;
            stock.Take(20f);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual((stock.MaxStock * 0.5f - 20f) / stock.MaxStock, drum.ShownFill, 0.02f, "Il baisse de ce qui est réellement prélevé.");
            Assert.Less(drum.ShownFill, before);
        }

        // ------------------------------------------------------------------ Atelier réservé à l'hôte (côté invité)

        sealed class GuestRequests : IWorkshopRequests
        {
            public readonly List<string> Upgrades = new List<string>();
            public void RequestStartOrder(string orderId, int bay) { }
            public void RequestDeliver(int bay) { }
            public void RequestAbandon(int bay) { }
            public void RequestWasher(byte action, int bay) { }
            public void RequestUpgrade(string upgradeId) => Upgrades.Add(upgradeId);
            public void RequestProduct(string productId) { }
            public void RequestRobotToggle() { }
            public void RequestDetergentCan() { }
            public void RequestDetergentRefill(float liters) { }
            public void RequestMachine(string machineId) { }
            public void RequestPrank(PrankRequest request) { }
            public void RequestSale(string upgradeId) { }
            public void RequestPickup(uint dropId) { }
        }

        [UnityTest]
        public IEnumerator Guest_SeesWorkshopUpgrades_ButCannotBuyThem()
        {
            var requests = new GuestRequests();
            game.Shop.Remote = requests;
            game.Wallet.Wallet.Restore(100000);
            var duster = game.Upgrades.Find("up_machine_duster");
            Assert.AreEqual(PurchaseResult.HostOnly, game.Shop.TryPurchase(duster));
            Assert.AreEqual(0, requests.Upgrades.Count, "Aucune demande envoyée.");
            Assert.AreEqual(PurchaseResult.Requested, game.Shop.TryPurchase(game.Upgrades.Find("up_water_1")), "Achat personnel : demandé à l'hôte.");
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(UI.ShopUI.Tab.Workshop);
            yield return null;
            game.ShopUI.Close();
            game.Shop.Remote = null;
            game.Reputation.Restore(duster.requiredReputation);
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(duster), "Hôte (ou solo) : achat normal.");
        }

        // ------------------------------------------------------------------ Sons préchargés

        [UnityTest]
        public IEnumerator ToolSounds_AreLoadedWithTheScene_NotOnFirstUse()
        {
            yield return null;
            int checkedClips = 0;
            foreach (var audio in Object.FindObjectsByType<CleaningAudioController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var clip in new[] { audio.Loop, audio.ContactLoop })
                {
                    if (clip == null) continue;
                    checkedClips++;
                    Assert.AreNotEqual(AudioDataLoadState.Unloaded, clip.loadState, $"{clip.name} chargé avant la première utilisation.");
                }
            Assert.Greater(checkedClips, 5);
        }
    }
}
