using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Tests d'intégration dans la vraie scène Atelier : composants réels, physique, raycasts,
    /// outils et interface. Les entrées souris sont remplacées par AutomationUseOverride.
    /// </summary>
    public class WorkshopPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsNotNull(game, "GameBootstrap introuvable dans Workshop.");
            Assert.IsTrue(game.IsReady, "La scène n'a pas validé ses références.");
            TestRugs.LoadDefault(game);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null) game.ToolController.AutomationUseOverride = false;
            Time.timeScale = 1f;
            yield return null;
        }

        /// <summary>Oriente le joueur vers un point du tapis, à portée de brosse.</summary>
        void AimAtRug(Vector2 uv)
        {
            var rug = game.Rug;
            Vector3 target = rug.UVToWorld(uv);
            Vector3 position = new Vector3(target.x, 0.06f, target.z - 1.1f);
            float eyeHeight = 1.62f + 0.06f;
            float pitch = Mathf.Atan2(eyeHeight - target.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(position, 0f, pitch);
        }

        /// <summary>
        /// En mode batch, aucune fenêtre n'a le focus : l'OS ne verrouille jamais réellement le curseur.
        /// On vérifie alors la politique (gameplay actif) ; le verrouillage réel est une vérification manuelle.
        /// </summary>
        void AssertCursorLocked(bool expected)
        {
            Assert.AreEqual(expected, game.GameState.IsGameplayActive);
            if (!Application.isBatchMode)
                Assert.AreEqual(expected ? CursorLockMode.Locked : CursorLockMode.None, Cursor.lockState);
        }

        [UnityTest]
        public IEnumerator Workshop_StartsWithDirtyRug_AndNoErrors()
        {
            yield return new WaitForSeconds(0.5f);
            Assert.IsNotNull(game.Rug.Grid);
            Assert.Greater(game.Rug.Grid.InitialDirt, 0f);
            Assert.AreEqual(0f, game.Rug.Cleanliness01, 1e-4f);
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Brush_AimedAtRug_LoosensDirtInTargetArea()
        {
            var uv = new Vector2(0.3f, 0.5f);
            AimAtRug(uv);
            Assert.IsTrue(game.ToolController.SelectSlot(3), "La brosse doit être sur la touche 3.");
            yield return null;

            Vector2Int cell = game.Rug.Grid.UVToCell(uv);
            float stainBefore = game.Rug.Grid.GetCell(cell.x, cell.y).Stain + game.Rug.Grid.GetCell(cell.x, cell.y).Dust;
            var farCell = game.Rug.Grid.GetCell(game.Rug.Grid.Width - 2, 1);

            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(game.ToolController.ActiveTool.IsAffectingRug, "La brosse doit toucher le tapis visé.");
            game.ToolController.AutomationUseOverride = false;
            yield return null;

            var after = game.Rug.Grid.GetCell(cell.x, cell.y);
            Assert.Less(after.Stain + after.Dust, stainBefore, "La saleté visée doit être détachée.");
            Assert.Greater(after.Loosened, 0f);
            Assert.AreEqual(farCell.Dirt, game.Rug.Grid.GetCell(game.Rug.Grid.Width - 2, 1).Dirt, 1e-6f, "Une zone non visée a été modifiée.");
        }

        [UnityTest]
        public IEnumerator Rinse_ConsumesWater_AndIncreasesCleanliness()
        {
            var uv = new Vector2(0.5f, 0.5f);
            AimAtRug(uv);
            game.ToolController.SelectSlot(3);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(1.5f);

            game.ToolController.SelectSlot(4);
            float waterBefore = game.ToolController.Equipment.Water.Amount;
            yield return new WaitForSeconds(1.5f);
            game.ToolController.AutomationUseOverride = false;
            yield return null;

            Assert.Less(game.ToolController.Equipment.Water.Amount, waterBefore, "Le rinçage doit consommer de l'eau.");
            Assert.Greater(game.Rug.Cleanliness01, 0f, "Brosser puis rincer doit retirer de la saleté.");
            Vector2Int cell = game.Rug.Grid.UVToCell(uv);
            Assert.Greater(game.Rug.Grid.GetCell(cell.x, cell.y).Moisture, 0.2f);
        }

        [UnityTest]
        public IEnumerator Rinse_WithEmptyTank_IsBlocked_UntilRefilled()
        {
            AimAtRug(new Vector2(0.5f, 0.5f));
            game.ToolController.SelectSlot(4);
            var water = game.ToolController.Equipment.Water;
            water.Consume(water.Capacity);
            float dirtBefore = game.Rug.Grid.TotalDirt;

            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(game.ToolController.ActiveTool.IsUsing, "Impossible de pulvériser sans eau.");
            Assert.AreEqual(dirtBefore, game.Rug.Grid.TotalDirt, 1e-5f);
            Assert.IsNotNull(game.ToolController.ActiveTool.GetBlockReason());

            var station = System.Array.Find(Object.FindObjectsByType<ResourceStation>(), s => s.Resource == ToolResource.Water);
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            Assert.IsTrue(station.CanInteract(interactor));
            station.Interact(interactor);
            Assert.AreEqual(water.Capacity, water.Amount, 1e-4f);
            yield return null;
            yield return null;
            Assert.IsTrue(game.ToolController.ActiveTool.IsUsing, "Après remplissage, la lance fonctionne.");
        }

        [UnityTest]
        public IEnumerator Pause_FreezesTime_ReleasesCursor_AndStopsTools()
        {
            AimAtRug(new Vector2(0.5f, 0.5f));
            game.ToolController.SelectSlot(3);
            yield return null;
            AssertCursorLocked(true);

            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.AreEqual(0f, Time.timeScale);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            Assert.IsFalse(game.GameState.IsGameplayActive);
            Assert.IsFalse(game.ToolController.ActiveTool.IsUsing);

            game.GameState.Resume();
            yield return null;
            Assert.AreEqual(1f, Time.timeScale);
            AssertCursorLocked(true);
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
        }

        [UnityTest]
        public IEnumerator Inspection_OpensWithBeforeAfter_AndReleasesCursor()
        {
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(game.Inspection.Open());
            yield return null;
            Assert.IsTrue(game.Inspection.IsOpen);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            Assert.IsFalse(game.GameState.IsGameplayActive);
            game.Inspection.Close();
            yield return null;
            Assert.IsTrue(game.GameState.IsGameplayActive);
            Assert.AreEqual(0, game.GameState.OpenModals);
        }

        [UnityTest]
        public IEnumerator LoadingAnotherRug_LoadsNewDefinition_WithFreshDirt()
        {
            var first = game.Rug.Definition;
            AimAtRug(new Vector2(0.5f, 0.5f));
            game.ToolController.SelectSlot(4);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.5f);
            game.ToolController.AutomationUseOverride = false;

            game.Rug.LoadRug(game.AvailableRugs[1]);
            yield return null;
            Assert.AreNotEqual(first, game.Rug.Definition);
            Assert.AreEqual(0f, game.Rug.Cleanliness01, 1e-4f);
            Assert.AreEqual(GameState.Workshop, game.GameState.State);
        }

        [UnityTest]
        public IEnumerator Effects_StayBounded_DuringLongUse()
        {
            AimAtRug(new Vector2(0.5f, 0.5f));
            game.ToolController.SelectSlot(4);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(3f);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
                Assert.LessOrEqual(ps.particleCount, ps.main.maxParticles, ps.name);
            game.ToolController.AutomationUseOverride = false;
            yield return new WaitForSeconds(0.8f);
            var jet = GameObject.Find("WaterJet").GetComponent<ParticleSystem>();
            Assert.AreEqual(0, jet.particleCount, "Le jet doit s'arrêter avec l'outil.");
        }

        /// <summary>Captures d'écran pour la validation visuelle (Logs/ci/screens). Nécessite un GPU (pas -nographics).</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_ReferenceScreenshots()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Pas de GPU (mode -nographics) : captures ignorées.");
            }

            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = game.ToolController.GetComponentInChildren<Camera>();

            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -2.3f), 0f, 22f);
            yield return new WaitForSeconds(0.6f);
            Save(camera, Path.Combine(folder, "01_workshop_start.png"));

            // Nettoyage réel d'une bande avec la séquence complète des cinq outils.
            game.ToolController.AutomationUseOverride = true;
            for (float u = 0.12f; u <= 0.45f; u += 0.06f)
            for (float v = 0.2f; v <= 0.8f; v += 0.12f)
            {
                AimAtRug(new Vector2(u, v));
                foreach (var (slot, seconds) in new[] { (1, 0.3f), (2, 0.25f), (3, 0.6f), (4, 0.45f), (5, 0.5f) })
                {
                    game.ToolController.SelectSlot(slot);
                    yield return new WaitForSeconds(seconds);
                }
                var equipment = game.ToolController.Equipment;
                equipment.Water.Refill();
                equipment.Soap.Refill();
                equipment.Recovery.Empty();
            }

            // Gros plans pendant l'utilisation : savon (mousse), aspirateur, lance.
            AimAtRug(new Vector2(0.62f, 0.5f));
            game.ToolController.SelectSlot(2);
            yield return new WaitForSeconds(0.8f);
            Save(camera, Path.Combine(folder, "02a_soap_foam.png"));
            AimAtRug(new Vector2(0.75f, 0.35f));
            game.ToolController.SelectSlot(1);
            yield return new WaitForSeconds(0.5f);
            Save(camera, Path.Combine(folder, "02b_vacuum.png"));
            AimAtRug(new Vector2(0.55f, 0.5f));
            game.ToolController.SelectSlot(4);
            yield return new WaitForSeconds(0.4f);
            Save(camera, Path.Combine(folder, "02_rinsing_closeup.png"));
            game.ToolController.AutomationUseOverride = false;

            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -2.3f), 0f, 30f);
            yield return new WaitForSeconds(0.4f);
            Save(camera, Path.Combine(folder, "03_partially_cleaned.png"));

            Assert.IsTrue(game.Inspection.Open());
            yield return new WaitForSeconds(0.2f);
            var snapshots = Object.FindAnyObjectByType<RugWash.Effects.RugSnapshotService>();
            SaveTexture(snapshots.Before, Path.Combine(folder, "04_snapshot_before.png"));
            SaveTexture(snapshots.After, Path.Combine(folder, "05_snapshot_after.png"));
            game.Inspection.Close();

            // Interface : le canvas est rendu temporairement par la caméra pour apparaître dans la capture.
            var canvas = Object.FindAnyObjectByType<Canvas>();
            AimAtRug(new Vector2(0.6f, 0.45f));
            game.ToolController.SelectSlot(4);
            yield return new WaitForSeconds(0.3f);
            yield return SaveWithUI(camera, canvas, Path.Combine(folder, "06_hud.png"));

            Assert.IsTrue(game.Inspection.Open());
            yield return new WaitForSeconds(0.1f);
            yield return SaveWithUI(camera, canvas, Path.Combine(folder, "07_inspection.png"));
            game.Inspection.Close();

            game.GameState.Pause();
            yield return null;
            yield return SaveWithUI(camera, canvas, Path.Combine(folder, "08_pause.png"));
            game.GameState.Resume();
            yield return null;

            // Boucle de jeu : tableau des commandes, boutique, résultats.
            game.Wallet.Wallet.Add(400);
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            yield return SaveWithUI(camera, canvas, Path.Combine(folder, "09_order_board.png"));
            game.OrderBoard.Close();
            Assert.IsTrue(game.ShopUI.Open());
            yield return null;
            game.ShopUI.Buy(0);
            yield return null;
            yield return SaveWithUI(camera, canvas, Path.Combine(folder, "10_shop.png"));
            game.ShopUI.Close();
            yield return null;
            game.Orders.TryStart(game.Orders.Book.Orders[0]);
            yield return new WaitForSeconds(0.3f);
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            game.Orders.Deliver();
            yield return new WaitForSeconds(0.2f);
            yield return SaveWithUI(camera, canvas, Path.Combine(folder, "11_results.png"));
            game.Results.BackToWorkshop();
            yield return null;

            Debug.Log($"[RugWash] Captures : {folder} (propreté {game.Rug.Cleanliness01 * 100f:0.0} %)");
            Assert.IsNotNull(game.Orders.LastResult, "La commande de capture doit avoir été livrée.");
        }

        internal static IEnumerator SaveWithUI(Camera camera, Canvas canvas, string path) => SaveWithUI(camera, canvas, path, 1600, 900);

        /// <summary>Capture avec l'interface à une résolution donnée (la mise à l'échelle du canevas suit la cible de rendu).</summary>
        internal static IEnumerator SaveWithUI(Camera camera, Canvas canvas, string path, int width, int height, System.Action<RenderTexture> check = null)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousMode = canvas.renderMode;
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            SaveTexture(rt, path);
            check?.Invoke(rt);
            canvas.renderMode = previousMode;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }

        internal static void Save(Camera camera, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(camera, request);
            SaveTexture(rt, path);
            rt.Release();
            Object.Destroy(rt);
        }

        internal static void SaveTexture(RenderTexture rt, string path)
        {
            Assert.IsNotNull(rt, $"Texture absente pour {path}");
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
        }
    }
}
