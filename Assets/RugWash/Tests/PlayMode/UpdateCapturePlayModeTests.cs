using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Effects;
using RugWash.Orders;
using RugWash.Workshop;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Captures d'écran de la grande mise à jour (Logs/ci/screens, à relire à l'œil) : annexe et machine à laver, icônes 3D,
    /// boutique, casier, flacon translucide, outil doré, paramètres graphiques, tapis presque noir. Nécessite un GPU.
    /// </summary>
    public class UpdateCapturePlayModeTests
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

        void Buy(params string[] ids)
        {
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.AddRange(ids);
            game.Upgrades.Restore(owned);
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_GrandeMiseAJour()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var canvas = game.Hud.GetComponentInParent<Canvas>();

            // Avant l'achat : la cloison ferme l'ouverture du mur droit.
            game.PlayerController.Teleport(new Vector3(3.6f, 0.06f, -1.45f), 90f, 8f);
            yield return new WaitForSeconds(0.5f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "40_extension_closed.png"));

            Buy("up_extension", "up_washer", "up_washer_fast", "up_vacuum", "up_vacuum_lvl2", "up_vacuum_lvl3");
            yield return new WaitForSeconds(0.3f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "41_extension_open.png"));

            // Deux commandes : zone 1 et zone 2 ; le tapis de la zone 2 vu depuis l'entrée de l'annexe.
            var startable = new List<OrderDefinition>();
            foreach (var order in game.Orders.Book.Orders)
                if (game.Orders.Book.CanStart(order, game.Orders.BoardReputation, int.MaxValue) == OrderStartResult.Started && startable.Count < 2) startable.Add(order);
            foreach (var order in startable) game.Orders.TryStart(order);
            game.PlayerController.Teleport(new Vector3(6.6f, 0.06f, -1.5f), 90f, 18f);
            yield return new WaitForSeconds(0.6f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "42_annex_zone2_hud.png"));

            game.PlayerController.Teleport(new Vector3(10.4f, 0.06f, -0.2f), 20f, 12f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "43_washer.png"));
            Assert.IsTrue(game.WasherUI.Open(game.Washer));
            yield return null;
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "44_washer_ui.png"));
            game.WasherUI.Close();
            game.Washer.Execute(WasherAction.Load, 1);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "45_washer_running.png"));

            // Ordinateur : deux commandes (zone 1, machine à laver).
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "46_order_board_bays.png"));
            game.OrderBoard.Close();

            // Boutique : améliorations (icônes, niveaux) et atelier (textes longs, lignes agrandies).
            Assert.IsTrue(game.ShopUI.Open());
            game.ShopUI.ShowTab(RugWash.UI.ShopUI.Tab.Upgrades);
            yield return null;
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "47_shop_upgrades.png"));
            game.ShopUI.ShowTab(RugWash.UI.ShopUI.Tab.Workshop);
            yield return null;
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "48_shop_workshop.png"));
            game.ShopUI.Close();

            Assert.IsTrue(game.Locker.Open());
            yield return null;
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "49_locker_icons.png"));
            game.Locker.Close();

            // Flacon translucide (pulvérisateur) au tiers, puis aspirateur doré, barre d'outils avec icônes.
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -2.6f), 0f, 30f);
            Cleaning.CleaningToolBase soap = null, vacuum = null;
            foreach (var tool in game.ToolController.Tools)
            {
                if (tool.Definition.id == "soap") soap = tool;
                if (tool.Definition.id == "vacuum") vacuum = tool;
            }
            game.ToolController.SelectSlot(soap.Definition.slot);
            game.ToolController.Equipment.Soap.Empty();
            game.ToolController.Equipment.Soap.Add(game.ToolController.Equipment.Soap.Capacity * 0.35f);
            yield return new WaitForSeconds(1.2f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "50_soap_bottle_hud.png"));
            game.ToolController.SelectSlot(vacuum.Definition.slot);
            yield return new WaitForSeconds(0.6f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "51_golden_vacuum.png"));
            var icons = Object.FindAnyObjectByType<ToolIconRenderer>();
            WorkshopPlayModeTests.SaveTexture((RenderTexture)icons.Atlas, Path.Combine(folder, "52_tool_icons_atlas.png"));

            // Paramètres → Graphismes.
            Assert.IsTrue(game.SettingsUI.Open());
            game.SettingsUI.ShowTab(1);
            yield return null;
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "53_settings_graphics.png"));
            game.SettingsUI.Close();

            // Tapis presque noir (thème « Grand nettoyage »), zone 1.
            game.Orders.Abandon(0);
            var grime = new List<DirtLayer>
            {
                new DirtLayer(DirtType.Grime, DirtPattern.Uniform, 0.95f, 0.97f, 0.35f, 0.3f),
                new DirtLayer(DirtType.DustClumps, DirtPattern.Edges, 0.45f, 0.5f, 0.25f),
                new DirtLayer(DirtType.Grease, DirtPattern.Spots, 0.3f, 0.9f, 0.14f, 0.25f),
            };
            game.Rug.LoadRug(game.AvailableRugs[0], grime, 77);
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -1.7f), 0f, 38f);
            yield return new WaitForSeconds(0.5f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "54_grime_rug.png"));
            Debug.Log($"[RugWash] Captures de la grande mise à jour : {folder}");
        }

        /// <summary>V0.2.5 : fût de stock (plein, à moitié, vide), jet de la lance (niveau 2, puis dorée), objet tombé au sol.</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V025()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = game.ToolController.GetComponentInChildren<Camera>();

            // Fût du poste détergent (contre le mur gauche).
            game.PlayerController.Teleport(new Vector3(-4.3f, 0.06f, -0.9f), 270f, 22f);
            game.Detergent.Restore(game.Detergent.MaxStock);
            yield return new WaitForSeconds(0.8f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "80_stock_drum_full.png"));
            game.Detergent.Restore(game.Detergent.MaxStock * 0.4f);
            yield return new WaitForSeconds(0.8f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "81_stock_drum_40.png"));
            game.Detergent.Restore(0f);
            yield return new WaitForSeconds(0.8f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "82_stock_drum_empty.png"));

            // Jet de la lance vers le tapis : niveau 2, puis dorée (jet tendu).
            Player.PlayerInventory.AutomationCarriesOnSelect = true;
            var owned = new List<string>(game.Upgrades.OwnedIds) { "up_tool_rinse", "up_rinse", "up_rinse_lvl2" };
            game.Upgrades.Restore(owned);
            game.Rug.LoadRug(game.AvailableRugs[0]);
            game.ToolController.SelectSlot(4);
            game.PlayerController.Teleport(new Vector3(0.4f, 0.06f, -2.4f), 0f, 26f);
            yield return new WaitForSeconds(0.5f);
            game.ToolController.Equipment.Water.Refill();
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.45f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "83_lance_jet_level2.png"));
            game.ToolController.AutomationUseOverride = false;
            owned.Add("up_rinse_lvl3");
            game.Upgrades.Restore(owned);
            game.ToolController.SelectSlot(4);
            yield return new WaitForSeconds(0.4f);
            game.ToolController.Equipment.Water.Refill();
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.45f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "84_lance_jet_golden.png"));
            game.ToolController.AutomationUseOverride = false;
            Player.PlayerInventory.AutomationCarriesOnSelect = false;

            // Objet tombé au sol (copie posée par les parties en ligne), vu de près.
            var holder = new GameObject("CaptureDrop");
            holder.transform.SetPositionAndRotation(new Vector3(0.3f, 0.02f, -3.3f), Quaternion.Euler(0f, 30f, 0f));
            var copy = game.ToolIcons.CreateWorldCopy("vacuum", holder.transform);
            Assert.IsNotNull(copy, "Copie posée au sol de l'aspirateur.");
            copy.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            game.PlayerController.Teleport(new Vector3(0.3f, 0.06f, -4.6f), 0f, 40f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "85_dropped_item.png"));
            Object.Destroy(holder);
        }
    }
}
