using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Network;
using RugWash.Player;
using RugWash.Save;
using RugWash.UI;
using RugWash.Workshop;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 5, dans les vraies scènes : musique (lecteur unique, fondus, pause, volume), machines de
    /// l'atelier (achat avec réputation, effets mesurés, un cycle par commande), décor par paliers (cagibi → penthouse),
    /// personnalisation (menu, sauvegarde, gants à la première personne), chute contrôlée et ragdoll des silhouettes.
    /// </summary>
    public class BaseV012PlayModeTests
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

        UpgradeDefinition Upgrade(string id)
        {
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.id == id) return upgrade;
            Assert.Fail($"Amélioration « {id} » absente");
            return null;
        }

        // ------------------------------------------------------------------ Musique

        [UnityTest]
        public IEnumerator Music_IsASinglePlayer_CrossfadingBetweenMenuAndWorkshop_DuckedInPause()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var music = MusicPlayer.Instance;
            Assert.IsNotNull(music, "Musique dès le menu.");
            StringAssert.Contains("menu", music.CurrentClip.name);
            yield return LoadWorkshop(legacyTools: true, resetSave: false);
            Assert.AreSame(music, MusicPlayer.Instance, "Même lecteur d'une scène à l'autre.");
            Assert.AreEqual(1, Object.FindObjectsByType<MusicPlayer>().Length, "Jamais dupliqué.");
            Assert.AreEqual(2, music.GetComponents<AudioSource>().Length, "Deux sources seulement (fondu enchaîné).");
            StringAssert.Contains("workshop", music.CurrentClip.name);
            yield return new WaitForSecondsRealtime(MusicPlayer.FadeSeconds + 0.2f);
            Assert.AreEqual(music.TargetVolume, music.CurrentVolume, 0.01f);
            Assert.LessOrEqual(music.TargetVolume, MusicPlayer.MaxGain, "Sous les sons du jeu.");

            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsTrue(music.Ducked, "Pause : musique atténuée, pas coupée net.");
            game.GameState.Resume();
            yield return null;
            Assert.IsFalse(music.Ducked);

            game.Settings.SetMusicVolume(0f);
            Assert.AreEqual(0f, music.TargetVolume, "Volume « Musique » à 0 : muette.");
            game.Settings.SetMusicVolume(0.6f);
        }

        // ------------------------------------------------------------------ Machines

        [UnityTest]
        public IEnumerator Machines_NeedPurchaseAndReputation_ThenHaveMeasuredEffects_OneCyclePerOrder()
        {
            yield return LoadWorkshop(legacyTools: true);
            var plate = game.FindMachine("duster");
            var blower = game.FindMachine("blower");
            var prewet = game.FindMachine("prewet");
            Assert.IsNotNull(plate);
            Assert.IsNotNull(blower);
            Assert.IsNotNull(prewet);
            Assert.IsFalse(plate.IsOwned, "Cachée tant qu'elle n'est pas achetée.");
            foreach (var renderer in plate.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(renderer.enabled);

            // Réputation requise (atelier) : refus explicite, puis achat.
            game.Wallet.Wallet.Add(5000);
            Assert.AreEqual(PurchaseResult.ReputationTooLow, game.Shop.TryPurchase(Upgrade("up_machine_prewet")));
            game.Reputation.Restore(20);
            foreach (string id in new[] { "up_machine_duster", "up_machine_blower", "up_machine_prewet" })
                Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(Upgrade(id)), id);
            yield return null;
            Assert.IsTrue(plate.IsOwned && blower.IsOwned && prewet.IsOwned);
            Assert.IsTrue(plate.GetComponentInChildren<Renderer>().enabled, "Achetée : visible.");

            // Sans commande : pas de cycle.
            StringAssert.Contains("aucun tapis", plate.Activate());

            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            var grid = game.Rug.Grid;
            float dustBefore = 0f, stainBefore = 0f;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                dustBefore += grid.GetCell(x, y).Dust;
                stainBefore += grid.GetCell(x, y).Stain;
            }
            StringAssert.Contains("cycle lancé", plate.Activate());
            Assert.IsTrue(plate.IsRunning);
            plate.ApplyTick(8f);
            float dustAfter = 0f, stainAfter = 0f;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                dustAfter += grid.GetCell(x, y).Dust;
                stainAfter += grid.GetCell(x, y).Stain;
            }
            Assert.AreEqual(dustBefore * 0.45f, dustAfter, dustBefore * 0.05f, "Environ la moitié de la poussière sèche tombe.");
            Assert.AreEqual(stainBefore, stainAfter, 1e-3f, "Les taches restent : pas de nettoyage complet.");
            yield return new WaitForSeconds(8.3f);
            Assert.IsFalse(plate.IsRunning, "Fin de cycle.");
            StringAssert.Contains("déjà utilisée", plate.Activate(), "Un cycle par commande.");

            // Rampe : humidifie et fait tremper, sans rien retirer.
            float dirt = grid.TotalDirt;
            StringAssert.Contains("cycle lancé", prewet.Activate());
            prewet.ApplyTick(8f);
            grid.RecomputeTotals();
            Assert.Greater(grid.AverageMoisture, 0.4f);
            Assert.AreEqual(dirt, grid.TotalDirt, dirt * 0.01f, "Rien n'est retiré par la rampe.");

            // Souffleur : sèche en marche, rien d'autre.
            float moisture = grid.TotalMoisture;
            StringAssert.Contains("allumé", blower.Activate());
            blower.ApplyTick(5f);
            Assert.Less(grid.TotalMoisture, moisture - 0.1f * grid.CellCount, "Le tapis sèche.");
            Assert.AreEqual(dirt, grid.TotalDirt, dirt * 0.01f);
            StringAssert.Contains("éteint", blower.Activate());
            Assert.IsFalse(blower.IsRunning);
        }

        // ------------------------------------------------------------------ Décor par paliers

        [UnityTest]
        public IEnumerator Decor_StartsInTheCagibi_AndEachTierReallyChangesTheSpace()
        {
            yield return LoadWorkshop(legacyTools: false);
            var decor = game.Decor;
            Assert.AreEqual(0, decor.Tier, "Nouvelle partie : le cagibi.");
            GameObject Group(string name) => decor.transform.Find(name).gameObject;
            Assert.IsTrue(Group("Tier0_Cagibi").activeSelf);
            Assert.IsFalse(Group("Tier4_Penthouse").activeSelf);

            game.ShopUI.Open();
            game.ShopUI.ShowTab(ShopUI.Tab.Workshop);
            yield return null;
            int rows = 0;
            foreach (var row in game.ShopUI.Rows) if (row.gameObject.activeSelf) rows++;
            Assert.AreEqual(11, rows, "Onglet Atelier : 4 paliers, 3 machines, le robot, l'agrandissement, la machine à laver et son programme rapide.");
            StringAssert.Contains("Réputation", game.ShopUI.RowOf(Upgrade("up_base_1")).StatusText, "Condition de réputation affichée.");
            game.ShopUI.Close();

            game.Wallet.Wallet.Add(500);
            game.Reputation.Restore(3);
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(Upgrade("up_base_1")));
            yield return null;
            Assert.AreEqual(1, decor.Tier);
            Assert.IsFalse(Group("Tier0_Cagibi").activeSelf, "Les cartons partent.");
            Assert.IsTrue(Group("Tier1_Range").activeSelf);
            Assert.AreEqual(PurchaseResult.MissingPrerequisite, game.Shop.TryPurchase(Upgrade("up_base_3")), "Paliers dans l'ordre.");

            var owned = new List<string>(game.Upgrades.OwnedIds) { "up_base_2", "up_base_3", "up_base_4" };
            game.Upgrades.Restore(owned);
            yield return null;
            Assert.AreEqual(4, decor.Tier);
            Assert.IsTrue(Group("Tier4_Penthouse").activeSelf, "Vue sur Los Angeles.");
            Assert.IsFalse(Group("Tier0to3_Windows").activeSelf, "Les fenêtres hautes laissent place aux baies.");
            Assert.IsTrue(Group("Tier2_Pro").activeSelf && Group("Tier3_Loft").activeSelf, "Les paliers précédents restent.");
            var saved = new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName);
            game.SaveSystem.SaveNow("test");
            CollectionAssert.Contains(saved.Load().Data.upgrades, "up_base_4");
        }

        // ------------------------------------------------------------------ Personnalisation

        [UnityTest]
        public IEnumerator Character_IsCustomizedInTheMenu_Saved_AndShownOnFirstPersonGloves()
        {
            TestSaveSandbox.Reset(legacyTools: false);
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            menu.CharacterButton.onClick.Invoke();
            var ui = menu.Character;
            Assert.IsTrue(ui.IsOpen);
            yield return null;
            Assert.IsTrue(ui.PreviewAppearance.OutfitRendererCount > 0, "Aperçu 3D de la silhouette.");
            ui.Step(0, 1);
            ui.Step(1, -1);
            ui.Step(2, 2);
            Assert.AreEqual(CharacterPalette.Outfits[1].name, ui.OutfitStepper.Value);
            Assert.AreEqual("Aucune", ui.CapStepper.Value, "Option « pas de casquette ».");
            Assert.IsFalse(ui.PreviewAppearance.CapVisible);
            AssertColor(CharacterPalette.Outfits[1].color, ui.PreviewAppearance.AppliedColor(0), "Tenue appliquée à l'aperçu.");
            float yaw = ui.Yaw;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreNotEqual(yaw, ui.Yaw, "L'aperçu tourne.");
            ui.Close();
            // L'apparence appartient au profil du joueur (commune à tous ses emplacements de sauvegarde).
            var data = new ProfileStore(TestSaveSandbox.Directory).Load(out _);
            Assert.AreEqual(1, data.character.outfit);
            Assert.AreEqual(0, data.character.cap);
            Assert.AreEqual(2, data.character.gloves);

            yield return LoadWorkshop(legacyTools: false, resetSave: false);
            var gloves = game.ToolController.GetComponentInChildren<CharacterAppearance>(true);
            Assert.IsNotNull(gloves);
            Assert.Greater(gloves.GloveRendererCount, 0);
            AssertColor(CharacterPalette.Gloves[2].color, gloves.AppliedColor(2), "Gants de la vue à la première personne.");
        }

        static void AssertColor(Color expected, Color actual, string message) =>
            Assert.Less(((Vector4)expected - (Vector4)actual).magnitude, 1e-3f, $"{message} Attendu {expected}, obtenu {actual}.");

        // ------------------------------------------------------------------ Chute et ragdoll

        [UnityTest]
        public IEnumerator Knockdown_LocksControls_DropsTheCamera_ThenGetsBackUp()
        {
            yield return LoadWorkshop(legacyTools: true);
            var knockdown = game.Knockdown;
            Assert.IsNotNull(knockdown);
            var camera = Camera.main.transform;
            float eye = camera.position.y;
            Assert.IsTrue(knockdown.Knockdown(new Vector3(50f, 0f, 0f), 1.1f, "Test"));
            Assert.IsFalse(knockdown.Knockdown(Vector3.forward, 1f), "Déjà à terre.");
            Assert.IsTrue(game.PlayerController.ControlLocked);
            Assert.IsTrue(game.ToolController.Locked);
            Vector3 start = game.PlayerController.transform.position;
            yield return new WaitForSeconds(0.6f);
            Assert.Less(camera.position.y, eye - 0.8f, "La caméra est au sol.");
            Assert.Greater(camera.position.y, 0.05f, "…sans passer sous le sol.");
            float slide = Vector3.Distance(start, game.PlayerController.transform.position);
            Assert.Less(slide, 6f, "Élan borné : pas de projection.");
            yield return new WaitForSeconds(1.6f);
            Assert.IsFalse(knockdown.IsDown, "Relevé.");
            Assert.IsFalse(game.PlayerController.ControlLocked);
            Assert.IsFalse(game.ToolController.Locked);
            Assert.AreEqual(eye, camera.position.y, 0.08f, "Hauteur des yeux retrouvée.");
        }

        [UnityTest]
        public IEnumerator RemoteAvatar_Ragdolls_ThenReturnsToItsPose()
        {
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var prefab = MultiplayerSession.Find().Content.avatarPrefab;
            yield return LoadWorkshop(legacyTools: true);
            var feet = new Vector3(-3.5f, 0f, -2.5f);
            var avatar = Object.Instantiate(prefab, feet, Quaternion.identity).GetComponent<NetworkPlayerAvatar>();
            avatar.SetPreviewState(3, false, 0f);
            yield return null;
            var ragdoll = avatar.Ragdoll;
            Assert.IsNotNull(ragdoll);
            var body = ragdoll.transform;
            Vector3 rest = body.localPosition;
            avatar.PreviewKnockdown(true, new Vector3(0f, 1f, 40f));
            Assert.IsTrue(ragdoll.IsActive);
            Assert.LessOrEqual(ragdoll.BodyRigidbody.linearVelocity.magnitude, 12.01f, "Élan exagéré mais borné.");
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(Vector3.Angle(body.up, Vector3.up), 30f, "Le corps a basculé.");
            Assert.Greater(body.position.y, -0.3f, "Pas à travers le sol.");
            avatar.PreviewKnockdown(false, Vector3.zero);
            yield return new WaitForSeconds(0.7f);
            Assert.IsFalse(ragdoll.IsBusy);
            Assert.AreEqual(rest, body.localPosition, "Retour à la pose debout.");
            Assert.IsNull(body.GetComponent<Rigidbody>(), "Physique retirée.");
            Object.Destroy(avatar.gameObject);
        }

        // ------------------------------------------------------------------ Captures

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_TiersMachinesAndCharacter()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            yield return LoadWorkshop(legacyTools: false);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;

            // Vue d'ensemble depuis l'avant de la salle, à chaque palier.
            for (int tier = 0; tier <= 4; tier++)
            {
                game.Decor.Apply(tier);
                game.PlayerController.Teleport(new Vector3(1.5f, 0.06f, -4.2f), -25f, 8f);
                yield return null;
                yield return Capture(camera, hud, Path.Combine(folder, $"70_tier_{tier}.png"));
            }
            game.PlayerController.Teleport(new Vector3(1.5f, 0.06f, -2.5f), -10f, -18f);
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "70b_tier_4_view.png"));

            // Machines achetées, vues depuis l'avant gauche de l'aire.
            var owned = new List<string>(game.Upgrades.OwnedIds) { "up_machine_duster", "up_machine_blower", "up_machine_prewet" };
            game.Upgrades.Restore(owned);
            game.Decor.Apply(2);
            game.FindMachine("blower").Activate();
            game.PlayerController.Teleport(new Vector3(-3.6f, 0.06f, -2.6f), 50f, 12f);
            yield return new WaitForSeconds(0.5f);
            yield return Capture(camera, hud, Path.Combine(folder, "71_machines.png"));

            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            menu.Character.Open();
            menu.Character.Step(0, 4);
            yield return new WaitForSecondsRealtime(0.4f);
            Canvas menuCanvas = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "Menu Canvas") menuCanvas = canvas;
            yield return Capture(Camera.main, menuCanvas, Path.Combine(folder, "72_character.png"));
            menu.Character.Close();
        }

        static IEnumerator Capture(Camera camera, Canvas canvas, string path)
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
