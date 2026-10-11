using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Sauvegarde et menus dans les vraies scènes : les scènes sont réellement rechargées pour vérifier
    /// la reprise de la progression. Toutes les écritures vont dans un dossier temporaire.
    /// </summary>
    public class SaveAndMenuPlayModeTests
    {
        [SetUp]
        public void Sandbox() => TestSaveSandbox.Reset();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        static IEnumerator LoadScene(string name)
        {
            SceneManager.LoadScene(name, LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
        }

        static IEnumerator WaitForScene(string name, float timeout = 20f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name, $"La scène {name} n'a pas été chargée.");
            yield return null;
            yield return null;
        }

        static GameBootstrap Game()
        {
            var game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady, "Atelier non prêt.");
            return game;
        }

        [UnityTest]
        public IEnumerator NewInstall_StartsFresh_WithoutSaveFile()
        {
            yield return LoadScene("Workshop");
            var game = Game();
            Assert.AreEqual(LoadStatus.NewProfile, game.SaveSystem.LastLoadStatus);
            Assert.AreEqual(0, game.Wallet.Balance);
            Assert.AreEqual(0, game.Reputation.Points);
            Assert.IsFalse(File.Exists(TestSaveSandbox.SavePath), "Aucune écriture sans opération importante.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DeliveredOrder_IsSaved_RestoredAfterReload_WithoutDuplicateReward()
        {
            yield return LoadScene("Workshop");
            var game = Game();
            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(OrderStartResult.Started, game.Orders.TryStart(order));
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            var result = game.Orders.Deliver();
            Assert.IsTrue(result.Accepted);
            int balance = game.Wallet.Balance;
            int reputation = game.Reputation.Points;
            Assert.IsTrue(File.Exists(TestSaveSandbox.SavePath), "La livraison doit déclencher une sauvegarde.");

            yield return LoadScene("Workshop");
            game = Game();
            Assert.AreEqual(LoadStatus.Loaded, game.SaveSystem.LastLoadStatus);
            Assert.AreEqual(balance, game.Wallet.Balance, "Le solde doit être restauré tel quel (aucune récompense rejouée).");
            Assert.AreEqual(reputation, game.Reputation.Points);
            var restored = game.Orders.Book.Find(order.id);
            Assert.AreEqual(OrderState.Completed, game.Orders.Book.StateOf(restored));
            Assert.AreEqual(OrderStartResult.NotAvailable, game.Orders.TryStart(restored), "Une commande terminée ne peut pas être refaite.");
            Assert.AreEqual(balance, game.Wallet.Balance);
            Assert.AreEqual(1, game.SaveSystem.Current.ordersDelivered);
        }

        [UnityTest]
        public IEnumerator Purchase_PersistsAcrossReload()
        {
            yield return LoadScene("Workshop");
            var game = Game();
            game.Wallet.Wallet.Add(500);
            Assert.IsTrue(game.ShopUI.Open());
            int index = -1;
            for (int i = 0; i < game.Upgrades.Catalog.Count; i++)
                if (game.Upgrades.Catalog[i].id == "up_water_1") index = i;
            Assert.AreEqual(PurchaseResult.Purchased, game.ShopUI.Buy(index));
            game.ShopUI.Close();
            int balance = game.Wallet.Balance;

            yield return LoadScene("Workshop");
            game = Game();
            Assert.AreEqual(balance, game.Wallet.Balance);
            Assert.AreEqual(60f, game.ToolController.Equipment.Water.Capacity, 1e-4f, "L'amélioration doit être réappliquée au chargement.");
            Assert.IsTrue(game.Upgrades.OwnedIds.Contains("up_water_1"));
        }

        [UnityTest]
        public IEnumerator Settings_ChangedInUI_AreAppliedSavedAndRestored()
        {
            yield return LoadScene("Workshop");
            var game = Game();
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsTrue(game.SettingsUI.Open());
            game.SettingsUI.SensitivitySlider.value = 0.3f;
            game.SettingsUI.MasterSlider.value = 0.5f;
            game.SettingsUI.InvertYStepper.NextButton.onClick.Invoke();
            Assert.AreEqual(0.3f, game.PlayerController.LookSensitivity, 1e-4f, "La sensibilité s'applique immédiatement.");
            Assert.IsTrue(game.PlayerController.InvertY);
            Assert.AreEqual(0.5f, AudioListener.volume, 1e-4f, "Le volume général s'applique immédiatement.");
            game.SettingsUI.CloseButton.onClick.Invoke();
            Assert.IsTrue(File.Exists(TestSaveSandbox.SavePath), "Fermer les paramètres sauvegarde les changements.");
            game.GameState.Resume();

            yield return LoadScene("Workshop");
            game = Game();
            Assert.AreEqual(0.3f, game.PlayerController.LookSensitivity, 1e-4f);
            Assert.IsTrue(game.PlayerController.InvertY);
            Assert.AreEqual(0.5f, game.Settings.Current.masterVolume, 1e-4f);
        }

        [UnityTest]
        public IEnumerator KeyBinding_Override_IsSavedAndReapplied()
        {
            yield return LoadScene("Workshop");
            var game = Game();
            var interact = game.Settings.BindingAsset.FindAction("Interact");
            interact.ApplyBindingOverride(0, "<Keyboard>/g");
            game.Settings.CaptureBindings();
            Assert.IsTrue(game.SaveSystem.SaveSettingsIfDirty());

            yield return LoadScene("Workshop");
            game = Game();
            var reloaded = game.Settings.BindingAsset.FindAction("Interact");
            Assert.AreEqual("<Keyboard>/g", reloaded.bindings[0].effectivePath);

            game.Settings.ResetBindings();
            Assert.AreEqual("<Keyboard>/e", reloaded.bindings[0].effectivePath);
        }

        [UnityTest]
        public IEnumerator CorruptedSave_DoesNotBlockStartup()
        {
            File.WriteAllText(TestSaveSandbox.SavePath, "{ fichier tronqué");
            yield return LoadScene("Workshop");
            var game = Game();
            Assert.AreEqual(LoadStatus.CorruptedReset, game.SaveSystem.LastLoadStatus);
            Assert.AreEqual(0, game.Wallet.Balance);
            Assert.IsNotEmpty(Directory.GetFiles(TestSaveSandbox.Directory, "*.corrupt-*"));
        }

        [UnityTest]
        public IEnumerator MainMenu_FullCycle_NewGame_Progress_ReturnToMenu_Continue()
        {
            yield return LoadScene("MainMenu");
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsNotNull(menu);
            Assert.IsFalse(menu.ContinueButton.interactable, "Sans sauvegarde, « Continuer » est désactivé.");
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);

            menu.PlayButton.onClick.Invoke();
            yield return WaitForScene("Workshop");
            var game = Game();
            game.Wallet.Wallet.Add(250);
            game.Reputation.Add(2);

            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Object.FindAnyObjectByType<PauseMenuUI>().ReturnToMainMenu();
            yield return WaitForScene("MainMenu");

            menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsTrue(menu.ContinueButton.interactable, "Après une partie sauvegardée, « Continuer » est disponible.");
            Assert.AreEqual(1f, Time.timeScale);

            menu.PlayButton.onClick.Invoke();
            Assert.IsTrue(menu.IsConfirmVisible, "Une nouvelle partie demande confirmation si une progression existe.");
            menu.ConfirmNoButton.onClick.Invoke();
            Assert.IsFalse(menu.IsConfirmVisible);

            menu.ContinueButton.onClick.Invoke();
            yield return WaitForScene("Workshop");
            game = Game();
            Assert.AreEqual(250, game.Wallet.Balance);
            Assert.AreEqual(2, game.Reputation.Points);
        }

        [UnityTest]
        public IEnumerator MainMenu_NewGame_AfterConfirmation_ResetsProgress_KeepsSettings()
        {
            var seed = new SaveData { money = 900, reputation = 5 };
            seed.settings.mouseSensitivity = 0.33f;
            Assert.IsTrue(new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName).Save(seed, out _));

            yield return LoadScene("MainMenu");
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsTrue(menu.ContinueButton.interactable);
            menu.PlayButton.onClick.Invoke();
            menu.ConfirmYesButton.onClick.Invoke();
            yield return WaitForScene("Workshop");
            var game = Game();
            Assert.AreEqual(0, game.Wallet.Balance);
            Assert.AreEqual(0, game.Reputation.Points);
            Assert.AreEqual(0.33f, game.PlayerController.LookSensitivity, 1e-4f, "Les paramètres survivent à une nouvelle partie.");
        }

        [UnityTest]
        public IEnumerator ReturnToMenu_DuringOrder_AbandonsOrder_AndKeepsItAvailable()
        {
            yield return LoadScene("Workshop");
            var game = Game();
            var order = game.Orders.Book.Orders[0];
            game.Orders.TryStart(order);
            game.GameState.Pause();
            yield return null;
            Object.FindAnyObjectByType<PauseMenuUI>().ReturnToMainMenu();
            yield return WaitForScene("MainMenu");
            yield return LoadScene("Workshop");
            game = Game();
            Assert.IsNull(game.Orders.ActiveOrder);
            Assert.AreEqual(OrderState.Available, game.Orders.Book.StateOf(game.Orders.Book.Find(order.id)));
        }

        /// <summary>Captures du menu principal et des paramètres (validation visuelle ; nécessite un GPU).</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_MenuAndSettings()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);

            yield return LoadScene("MainMenu");
            yield return new WaitForSeconds(1f);
            var camera = Camera.main;
            var canvas = Object.FindAnyObjectByType<Canvas>();
            yield return CaptureWithUI(camera, canvas, Path.Combine(folder, "12_main_menu.png"));
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            menu.Settings.Open();
            yield return null;
            yield return CaptureWithUI(camera, canvas, Path.Combine(folder, "13_settings.png"));
            menu.Settings.Close();
        }

        static IEnumerator CaptureWithUI(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
