using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// V0.3.0 (refonte graphique) : galerie de contrôle visuel (Logs/ci/screens/v30_*, à relire à l'œil) — vues larges de
    /// l'atelier, aire de lavage, tapis sale puis nettoyé de près, HUD, chaque outil tenu en main. Nécessite un GPU.
    /// </summary>
    public class V030CapturePlayModeTests
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

        static string Folder()
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            return folder;
        }

        void Aim(Vector2 uv, float distance)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, distance) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - distance), 0f, pitch);
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Workshop()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var canvas = game.Hud.GetComponentInParent<Canvas>();
            game.Rug.LoadRug(game.AvailableRugs[0]);

            var views = new (string name, Vector3 position, float yaw, float pitch)[]
            {
                ("v30_01_entrance", new Vector3(-4.6f, 0.06f, -4.2f), 42f, 6f),
                ("v30_02_washbay", new Vector3(0f, 0.06f, -2.3f), 0f, 22f),
                ("v30_03_left_wall", new Vector3(2.2f, 0.06f, -0.6f), -95f, 4f),
                ("v30_04_back_left", new Vector3(2.8f, 0.06f, -3.2f), -40f, 4f),
                ("v30_05_front_wall", new Vector3(0.5f, 0.06f, 3.6f), 180f, 4f),
                ("v30_06_right_wall", new Vector3(-2.6f, 0.06f, 0.6f), 90f, 4f),
            };
            foreach (var view in views)
            {
                game.PlayerController.Teleport(view.position, view.yaw, view.pitch);
                yield return new WaitForSeconds(0.5f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, view.name + ".png"));
            }

            // Tapis sale de près, puis une bande nettoyée (savon, brosse, rinçage).
            Aim(new Vector2(0.5f, 0.5f), 0.9f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_07_rug_dirty_close.png"));
            game.ToolController.AutomationUseOverride = true;
            for (float u = 0.2f; u <= 0.5f; u += 0.07f)
            for (float v = 0.25f; v <= 0.75f; v += 0.12f)
            {
                Aim(new Vector2(u, v), 1.1f);
                foreach (var (slot, seconds) in new[] { (1, 0.25f), (2, 0.25f), (3, 0.45f), (4, 0.4f) })
                {
                    game.ToolController.SelectSlot(slot);
                    yield return new WaitForSeconds(seconds);
                }
                game.ToolController.Equipment.Water.Refill();
                game.ToolController.Equipment.Soap.Refill();
                game.ToolController.Equipment.Recovery.Empty();
            }
            game.ToolController.AutomationUseOverride = false;
            Aim(new Vector2(0.45f, 0.5f), 1.2f);
            yield return new WaitForSeconds(0.5f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_08_rug_half_clean.png"));
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_09_hud.png"));

            // Mousse de près.
            Aim(new Vector2(0.7f, 0.5f), 0.9f);
            game.ToolController.SelectSlot(1);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.8f);
            game.ToolController.AutomationUseOverride = false;
            yield return new WaitForSeconds(0.2f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_10_foam_close.png"));
        }

        /// <summary>Fenêtres de la charte UI : ordinateur, commandes, boutique, casier, pause, résultats.</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Windows()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var canvas = game.Hud.GetComponentInParent<Canvas>();
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -2.3f), 0f, 18f);
            game.Wallet.Wallet.Add(650);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(game.Computer.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_20_computer.png"));
            game.Computer.Close();
            Assert.IsTrue(game.OrderBoard.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_21_orders.png"));
            game.OrderBoard.Close();
            Assert.IsTrue(game.ShopUI.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_22_shop.png"));
            game.ShopUI.Close();
            Assert.IsTrue(game.Locker.Open());
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_23_locker.png"));
            game.Locker.Close();
            game.GameState.Pause();
            yield return new WaitForSecondsRealtime(0.15f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_24_pause.png"));
            game.GameState.Resume();
            yield return null;
            game.Orders.TryStart(game.Orders.Book.Orders[0]);
            yield return new WaitForSeconds(0.3f);
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            game.Orders.Deliver();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return WorkshopPlayModeTests.SaveWithUI(camera, canvas, Path.Combine(folder, "v30_25_results.png"));
            game.Results.BackToWorkshop();
            yield return null;
        }

        /// <summary>Menu principal (charte UI, décor) et fenêtre Personnage (silhouette).</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Menu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1f);
            Canvas menuCanvas = null;
            foreach (var c in Object.FindObjectsByType<Canvas>())
                if (c.name == "Menu Canvas") menuCanvas = c;
            yield return WorkshopPlayModeTests.SaveWithUI(Camera.main, menuCanvas, Path.Combine(folder, "v30_30_main_menu.png"));
            var menu = Object.FindAnyObjectByType<RugWash.UI.MainMenuController>();
            menu.Character.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return WorkshopPlayModeTests.SaveWithUI(Camera.main, menuCanvas, Path.Combine(folder, "v30_31_character.png"));
            menu.Character.Close();
            menu.Settings.Open();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return WorkshopPlayModeTests.SaveWithUI(Camera.main, menuCanvas, Path.Combine(folder, "v30_32_settings.png"));
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_Tools()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var tools = game.ToolController;
            Player.PlayerInventory.AutomationCarriesOnSelect = true;
            var owned = new List<string>(game.Upgrades.OwnedIds)
            {
                "up_tool_vacuum", "up_tool_rinse", "up_tool_extractor", "up_tool_foam", "up_tool_broom", "up_tool_squeegee",
                "up_tool_rotary",
            };
            game.Upgrades.Restore(owned);
            game.Rug.LoadRug(game.AvailableRugs[0]);
            yield return null;
            for (int i = 0; i < tools.Tools.Count; i++)
            {
                var tool = tools.Tools[i];
                tools.Inventory.CarryForAutomation(tool);
                tools.SelectIndex(i);
                Aim(new Vector2(0.5f, 0.45f), 1.25f);
                yield return new WaitForSeconds(0.6f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_tool_{i:00}_{tool.Definition.id}.png"));
                // Au repos (rien de visé à portée) : l'outil tenu en main, de près.
                game.PlayerController.Teleport(new Vector3(0f, 0.06f, 3.6f), 180f, -4f);
                yield return new WaitForSeconds(0.5f);
                WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_toolrest_{i:00}_{tool.Definition.id}.png"));
            }
            Player.PlayerInventory.AutomationCarriesOnSelect = false;
        }

        /// <summary>Modèles Blender (MCP) intégrés à l'atelier : casier (fermé, ouvert), caisse à outils, machine à laver de l'annexe.</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_V030_BlenderModels()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            string folder = Folder();
            var camera = game.ToolController.GetComponentInChildren<Camera>();
            var locker = Object.FindAnyObjectByType<Workshop.Locker>();
            var hinge = locker.transform.Find("Locker_DoorHinge");
            Assert.IsNotNull(hinge, "Casier FBX : porte à charnière.");
            game.PlayerController.Teleport(new Vector3(1.85f, 0.06f, 2.0f), 0f, 10f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_locker.png"));
            locker.enabled = false;
            hinge.localRotation = Quaternion.Euler(0f, -100f, 0f);
            yield return null;
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_locker_open.png"));
            locker.enabled = true;
            game.PlayerController.Teleport(new Vector3(3.6f, 0.06f, 3.0f), 0f, 22f);
            yield return new WaitForSeconds(0.4f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_toolstorage.png"));
            Object.FindAnyObjectByType<Workshop.WorkshopExtension>().Apply(true, true, false);
            game.PlayerController.Teleport(new Vector3(10.9f, 0.06f, -0.3f), 0f, 18f);
            yield return new WaitForSeconds(0.6f);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, "v30_fbx_ingame_washer.png"));
        }
    }
}
