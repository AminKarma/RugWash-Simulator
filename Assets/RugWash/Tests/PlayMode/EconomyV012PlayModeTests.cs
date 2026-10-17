using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Effects;
using RugWash.Network;
using RugWash.Orders;
using RugWash.Player;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 3, dans les vraies scènes : kit de départ (brosse, détergent, seau), détergent consommable
    /// (réserve de 4 L, bidons de 40 L, refus visuel et sonore, niveau de liquide), ordinateur à deux icônes seul accès aux
    /// missions et à la boutique, missions générées remplacées à la livraison et identiques après un redémarrage,
    /// actualités du menu principal.
    /// </summary>
    public class EconomyV012PlayModeTests
    {
        GameBootstrap game;

        IEnumerator LoadWorkshop(bool legacyTools, bool resetSave = true)
        {
            if (resetSave) TestSaveSandbox.Reset(legacyTools);
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
            if (game != null && game.ToolController != null) game.ToolController.AutomationUseOverride = false;
            var session = MultiplayerSession.Find();
            if (session != null)
            {
                if (session.InSession) session.Manager.Shutdown();
                if (session.Manager != null) Object.Destroy(session.Manager.gameObject);
                Object.Destroy(session.gameObject);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        CleaningToolBase Tool(string id)
        {
            foreach (var tool in game.ToolController.Tools)
                if (tool.Definition.id == id) return tool;
            Assert.Fail($"Outil « {id} » absent");
            return null;
        }

        ResourceStation SoapStation => System.Array.Find(Object.FindObjectsByType<ResourceStation>(), s => s.Resource == ToolResource.Soap);

        static List<string> OfferIds(MissionBoard board)
        {
            var ids = new List<string>();
            foreach (var offer in board.Offers) ids.Add(offer.Id);
            return ids;
        }

        // ------------------------------------------------------------------ Kit de départ

        [UnityTest]
        public IEnumerator NewGame_StartsWithBrushDetergentAndBucketOnly()
        {
            yield return LoadWorkshop(legacyTools: false);
            var unlocked = new List<string>();
            foreach (var tool in game.ToolController.Tools)
                if (tool.IsUnlocked) unlocked.Add(tool.Definition.id);
            CollectionAssert.AreEquivalent(new[] { "brush", "soap", "bucket" }, unlocked, "Kit de départ : brosse, détergent, seau d'eau.");
            Assert.AreEqual(11, game.ToolController.Tools.Count, "Les autres outils existent toujours (à acheter).");
            Assert.AreEqual(0, game.Upgrades.OwnedIds.Count);
            Assert.AreEqual(SaveMigration.StarterDetergentStock, game.Detergent.Stock, 1e-4f);
            var soap = game.ToolController.Equipment.Soap;
            Assert.AreEqual(4f, soap.Capacity, 1e-4f, "Réserve de détergent de 4 L.");
            Assert.AreEqual(soap.Capacity, soap.Amount, 1e-4f, "Réserve pleine au départ.");

            // Outil non acheté : refus avec la raison et le lieu d'achat.
            Assert.IsFalse(game.ToolController.SelectSlot(Tool("vacuum").Definition.slot));
            yield return null;
            StringAssert.Contains("ordinateur (Boutique)", game.Hud.ToastText);
            Assert.IsTrue(game.ToolController.SelectSlot(Tool("bucket").Definition.slot), "Le seau est utilisable.");

            // Achat d'un outil sur l'ordinateur : il devient utilisable.
            game.Wallet.Wallet.Add(1000);
            UpgradeDefinition vacuumUnlock = null;
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.id == "up_tool_vacuum") vacuumUnlock = upgrade;
            Assert.IsNotNull(vacuumUnlock);
            Assert.IsTrue(game.Computer.Open());
            game.Computer.OpenShop();
            yield return null;
            Assert.AreSame(game.ShopUI.Rows[0], game.ShopUI.RowOf(FirstToolUnlock()), "Outils à acheter en tête de la boutique.");
            game.ShopUI.RowOf(vacuumUnlock).ActionButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(Tool("vacuum").IsUnlocked);
            Assert.AreEqual(1000 - vacuumUnlock.price, game.Wallet.Balance);
            game.ShopUI.Close();
        }

        UpgradeDefinition FirstToolUnlock()
        {
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.effect == UpgradeEffect.UnlockTool) return upgrade;
            return null;
        }

        // ------------------------------------------------------------------ Détergent

        [UnityTest]
        public IEnumerator Detergent_ReserveRefillsFromStock_EmptyIsRefused_AndCansAreBought()
        {
            yield return LoadWorkshop(legacyTools: false);
            var soap = game.ToolController.Equipment.Soap;
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            var station = SoapStation;

            soap.Empty();
            Assert.IsNotNull(Tool("soap").GetBlockReason(), "Réserve vide : le pulvérisateur est bloqué.");
            StringAssert.Contains("stock 12", station.GetPrompt(interactor), "Le poste affiche le stock restant.");
            station.Interact(interactor);
            Assert.AreEqual(4f, soap.Amount, 1e-4f, "Réserve remplie depuis le stock.");
            Assert.AreEqual(8f, game.Detergent.Stock, 1e-4f, "Le stock baisse d'autant.");
            Assert.IsNull(Tool("soap").GetBlockReason());

            // Remplissage partiel : il ne reste que 1,5 L.
            game.Detergent.Take(game.Detergent.Stock - 1.5f);
            soap.Empty();
            station.Interact(interactor);
            Assert.AreEqual(1.5f, soap.Amount, 1e-4f);
            Assert.AreEqual(0f, game.Detergent.Stock, 1e-4f);

            // Plus rien : refus explicite, rien n'est inventé.
            soap.Empty();
            StringAssert.Contains("achetez un bidon de 40 L", station.Interact(interactor));
            Assert.AreEqual(0f, soap.Amount);
            StringAssert.Contains("bidon de 40 L", Tool("soap").GetBlockReason());

            // Boutique (via l'ordinateur) : refus sans argent, puis achat d'un bidon.
            Assert.IsTrue(game.Computer.Open());
            game.Computer.OpenShop();
            game.ShopUI.ShowTab(true);
            yield return null;
            Assert.AreEqual(ProductPurchaseResult.InsufficientFunds, game.ShopUI.BuyDetergentCan());
            Assert.AreEqual(0f, game.Detergent.Stock);
            game.Wallet.Wallet.Add(100);
            Assert.AreEqual(ProductPurchaseResult.Purchased, game.ShopUI.BuyDetergentCan());
            Assert.AreEqual(100 - game.Detergent.CanPrice, game.Wallet.Balance);
            Assert.AreEqual(40f, game.Detergent.Stock, 1e-4f);
            game.ShopUI.Close();

            var saved = new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName).Load().Data;
            Assert.AreEqual(40f, saved.detergentStock, 1e-3f, "Stock sauvegardé à l'achat.");
            Assert.AreEqual(0f, saved.soapReserve, 1e-3f, "Réserve emportée sauvegardée.");

            // Redémarrage : stock et réserve reviennent tels quels.
            yield return LoadWorkshop(legacyTools: false, resetSave: false);
            Assert.AreEqual(40f, game.Detergent.Stock, 1e-3f);
            Assert.AreEqual(0f, game.ToolController.Equipment.Soap.Amount, 1e-3f);
        }

        [UnityTest]
        public IEnumerator EmptyReserve_PlaysTheDenySound_AndTheLiquidLevelFollowsTheReserve()
        {
            yield return LoadWorkshop(legacyTools: false);
            TestRugs.LoadDefault(game);
            Vector3 target = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, Mathf.Atan2(1.62f, 1.1f) * Mathf.Rad2Deg);
            var soapTool = Tool("soap");
            Assert.IsTrue(game.ToolController.SelectSlot(soapTool.Definition.slot));
            var soap = game.ToolController.Equipment.Soap;
            yield return new WaitForSeconds(0.3f);

            // Niveau de liquide : la valeur réelle de la réserve, transmise au shader du contenant.
            ContainerLevel level = null;
            foreach (var candidate in Object.FindObjectsByType<ContainerLevel>())
                if (candidate.isActiveAndEnabled && candidate.name == "Bottle") level = candidate;
            Assert.IsNotNull(level, "Contenant à niveau visible sur le pulvérisateur.");
            Assert.AreEqual(1f, level.ShownFill, 0.02f);
            var renderer = level.GetComponent<Renderer>();
            Assert.AreEqual("RugWash/Liquid Container", renderer.sharedMaterial.shader.name);
            // Grande mise à jour : liquide en volume dans un flacon en plastique translucide.
            var shell = level.transform.parent.Find("Bottle_Shell");
            Assert.IsNotNull(shell, "Paroi translucide du flacon.");
            Assert.AreEqual("RugWash/Translucent Plastic", shell.GetComponent<Renderer>().sharedMaterial.shader.name);
            Assert.Greater(renderer.sharedMaterial.GetColor("_LiquidColor").g, renderer.sharedMaterial.GetColor("_LiquidColor").r, "Liquide vert.");
            soap.Empty();
            soap.Add(1f);
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(0.25f, level.ShownFill, 0.03f, "1 L sur 4 L : le liquide descend au quart.");
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.AreEqual(level.ShownFill, block.GetFloat("_Fill"), 1e-4f);

            // Réserve vide : appui sur « utiliser » → raison affichée et son de refus.
            soap.Empty();
            var audio = Object.FindAnyObjectByType<Audio.AudioManager>();
            game.ToolController.AutomationUseOverride = true;
            yield return null;
            yield return null;
            game.ToolController.AutomationUseOverride = false;
            Assert.IsFalse(soapTool.IsUsing);
            Assert.IsNotNull(audio.LastPlayedClip);
            StringAssert.Contains("deny", audio.LastPlayedClip.name);
            // Le HUD se rafraîchit toutes les 0,1 s (temps réel) : on attend un rafraîchissement.
            yield return new WaitForSecondsRealtime(0.15f);
            StringAssert.Contains("Réserve de détergent vide", game.Hud.PromptText, "Raison visible à l'écran.");
            StringAssert.Contains("stock", game.Hud.ToolStatusText, "Stock en bidons affiché avec la réserve.");
        }

        // ------------------------------------------------------------------ Ordinateur

        [UnityTest]
        public IEnumerator Computer_HasExactlyTwoIcons_AndIsTheOnlyAccessToMissionsAndShop()
        {
            yield return LoadWorkshop(legacyTools: true);
            var panels = Object.FindObjectsByType<PanelInteractable>();
            Assert.AreEqual(1, panels.Length, "Plus de boutique sur le meuble à outils : un seul accès.");
            Assert.AreEqual(PanelInteractable.Target.Computer, panels[0].Opens);
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            StringAssert.StartsWith("Ordinateur", panels[0].GetPrompt(interactor));
            StringAssert.Contains("ordinateur", game.Hud.OrderText);

            panels[0].Interact(interactor);
            Assert.IsTrue(game.Computer.IsOpen);
            Assert.IsFalse(game.GameState.IsGameplayActive, "Fenêtre modale : curseur libéré.");
            yield return null;
            var icons = game.Computer.MissionsButton.transform.parent.GetComponentsInChildren<Button>();
            Assert.AreEqual(2, icons.Length, "Exactement deux icônes.");
            StringAssert.Contains("Missions", game.Computer.MissionsButton.GetComponentInChildren<Text>().text);
            StringAssert.Contains("Boutique", game.Computer.ShopButton.GetComponentInChildren<Text>().text);
            StringAssert.Contains("disponible", game.Computer.MissionsStatus);
            StringAssert.Contains("Solde", game.Computer.ShopStatus);
            StringAssert.Contains("Détergent en stock", game.Computer.ShopStatus);

            game.Computer.MissionsButton.onClick.Invoke();
            Assert.IsFalse(game.Computer.IsOpen);
            Assert.IsTrue(game.OrderBoard.IsOpen, "Icône Missions → tableau des commandes.");
            game.OrderBoard.Close();

            Assert.IsTrue(game.Computer.Open());
            game.Computer.ShopButton.onClick.Invoke();
            Assert.IsFalse(game.Computer.IsOpen);
            Assert.IsTrue(game.ShopUI.IsOpen, "Icône Boutique → boutique.");
            yield return null;
            Assert.IsNotNull(game.ShopUI.Rows[0].GetComponentInParent<ScrollRect>(), "Boutique défilante (catalogue plus long que la fenêtre).");
            game.ShopUI.Close();

            Assert.IsTrue(game.Computer.Open());
            game.Computer.Close();
            Assert.IsTrue(game.GameState.IsGameplayActive, "Fermeture : retour au jeu.");
        }

        // ------------------------------------------------------------------ Missions générées

        [UnityTest]
        public IEnumerator Missions_AreOfferedFirst_ReplacedOnDelivery_AndIdenticalAfterRestart()
        {
            yield return LoadWorkshop(legacyTools: true);
            var missions = game.Orders.Missions;
            Assert.IsTrue(missions.IsInitialized);
            Assert.AreEqual(MissionBoard.OfferCount, missions.Offers.Count);
            int generated = 0;
            foreach (var order in game.Orders.Book.Orders)
                if (MissionKey.IsGenerated(order.id)) generated++;
            Assert.AreEqual(MissionBoard.OfferCount, generated, "Les offres sont sur le tableau, en plus des contrats.");

            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            var mission = game.OrderBoard.DisplayedOrders[0];
            Assert.IsTrue(MissionKey.IsGenerated(mission.id), "Missions accessibles en tête du tableau.");
            StringAssert.StartsWith("Mission", game.OrderBoard.Rows[0].GetComponentsInChildren<Text>()[1].text);
            game.OrderBoard.Rows[0].ActionButton.onClick.Invoke();
            yield return null;
            Assert.AreSame(mission, game.Orders.ActiveOrder);
            Assert.AreEqual(mission.rug, game.Rug.Definition, "Le tapis de la mission est posé.");

            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            int sequence = missions.Sequence;
            int balance = game.Wallet.Balance;
            var result = game.Orders.Deliver();
            Assert.IsTrue(result.Accepted, $"Note : {result.Grade}");
            Assert.AreEqual(balance + result.Reward, game.Wallet.Balance);
            Assert.IsFalse(missions.Contains(mission.id), "Mission livrée retirée…");
            Assert.IsNull(game.Orders.Book.Find(mission.id));
            Assert.AreEqual(MissionBoard.OfferCount, missions.Offers.Count, "…et remplacée.");
            Assert.AreEqual(sequence + 1, missions.Sequence);
            Assert.AreEqual(mission.id, missions.LastCompleted);
            game.Results.WorkshopButton.onClick.Invoke();
            yield return null;

            var saved = new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName).Load().Data;
            CollectionAssert.AreEqual(OfferIds(missions), saved.missionOffers, "Offres sauvegardées à la livraison.");
            Assert.AreEqual(missions.Seed, saved.missionSeed);
            Assert.AreEqual(missions.Sequence, saved.missionSequence);
            CollectionAssert.DoesNotContain(saved.completedOrders, mission.id, "Une mission générée n'encombre pas l'historique des contrats.");

            var offers = OfferIds(missions);
            var titles = new List<string>();
            foreach (string id in offers) titles.Add(game.Orders.Book.Find(id).title + game.Orders.Book.Find(id).baseReward);

            // Redémarrage : mêmes offres, mêmes missions.
            yield return LoadWorkshop(legacyTools: true, resetSave: false);
            CollectionAssert.AreEqual(offers, OfferIds(game.Orders.Missions));
            Assert.AreEqual(sequence + 1, game.Orders.Missions.Sequence);
            for (int i = 0; i < offers.Count; i++)
                Assert.AreEqual(titles[i], game.Orders.Book.Find(offers[i]).title + game.Orders.Book.Find(offers[i]).baseReward);
        }

        // ------------------------------------------------------------------ Actualités

        [UnityTest]
        public IEnumerator MainMenu_ShowsLocalNews_WithoutBlocking()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var news = Object.FindAnyObjectByType<NewsPanelUI>();
            Assert.IsNotNull(news, "Zone « Actualités » dans le menu principal.");
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsTrue(menu.PlayButton.interactable, "Le menu reste utilisable pendant le chargement.");
            float end = Time.realtimeSinceStartup + 5f;
            while (!news.IsLoaded && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(news.IsLoaded);
            Assert.IsNull(news.Error);
            Assert.GreaterOrEqual(news.Items.Count, 1);
            StringAssert.Contains(news.Items[0].title, news.DisplayedText);
            bool labelledLocal = false;
            foreach (var text in news.GetComponentsInChildren<Text>())
                labelledLocal |= text.text.Contains("locales");
            Assert.IsTrue(labelledLocal, "Présentées comme des annonces locales (aucune connexion simulée).");
        }
    }
}
