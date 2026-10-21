using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Network;
using RugWash.Player;
using RugWash.Save;
using RugWash.Workshop;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 4, dans la vraie scène de l'atelier : raclette qui pousse réellement l'eau et la boue jusqu'au
    /// caniveau (et rien sur un tapis), monobrosse (fibres sèches / humides, mousse, machine lourde), bottes et traces
    /// boueuses sur le tapis, robot aspirateur à acheter, accessoire anti-poils, onze emplacements d'outils.
    /// </summary>
    public class ToolsV012PlayModeTests
    {
        GameBootstrap game;

        IEnumerator LoadWorkshop(bool legacyTools)
        {
            TestSaveSandbox.Reset(legacyTools);
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

        void Grant(params string[] ids)
        {
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.AddRange(ids);
            game.Upgrades.Restore(owned);
        }

        T Tool<T>(string id) where T : CleaningToolBase
        {
            foreach (var tool in game.ToolController.Tools)
                if (tool.Definition.id == id) return (T)tool;
            Assert.Fail($"Outil « {id} » absent");
            return null;
        }

        static ToolAim AimAt(RugCleaningController surface, Vector3 point)
        {
            Vector3 up = surface.transform.up;
            return new ToolAim { HasHit = true, Point = point, Normal = up, Distance = 1f, Rug = surface, Ray = new Ray(point + up, -up) };
        }

        static float WaterIn(RugCleaningController surface, float uMin, float uMax)
        {
            var grid = surface.Grid;
            float sum = 0f;
            for (int y = 0; y < grid.Height; y++)
            for (int x = Mathf.FloorToInt(uMin * grid.Width); x < Mathf.Min(grid.Width, Mathf.CeilToInt(uMax * grid.Width)); x++)
                sum += grid.GetCell(x, y).Moisture;
            return sum;
        }

        // ------------------------------------------------------------------ Raclette

        [UnityTest]
        public IEnumerator Squeegee_PushesWaterAlongTheStroke_IntoTheDrain_AndOnlyWipesWaterFromARug()
        {
            yield return LoadWorkshop(legacyTools: true);
            Grant("up_tool_squeegee");
            var squeegee = Tool<SqueegeeTool>("squeegee");
            Assert.IsTrue(squeegee.IsUnlocked);
            Assert.IsNotNull(squeegee.Drain, "Le caniveau de l'aire de lavage est connu de la raclette.");
            Assert.IsTrue(game.ToolController.SelectSlot(10), "Touche 0 : la raclette.");
            Assert.AreSame(squeegee, game.ToolController.ActiveTool);
            game.ToolController.SelectSlot(3);

            var pad = game.WorkSurface.Surface;
            pad.Grid.Clear();
            pad.Grid.RecomputeTotals();
            // Bande d'eau sale en travers de l'aire (u de 0,30 à 0,40), le caniveau étant au bord droit (u ≈ 0,95).
            var wet = new DepositOperation { Type = DirtType.Mud, Water = 0.7f, Mud = 0.3f };
            pad.Grid.ApplyRect(new RectInt(Mathf.RoundToInt(0.3f * pad.Grid.Width), 0, Mathf.RoundToInt(0.1f * pad.Grid.Width), pad.Grid.Height), 1f, ref wet);
            pad.Grid.RecomputeTotals();
            float bandBefore = WaterIn(pad, 0f, 0.42f);
            float totalBefore = pad.Grid.TotalMoisture;

            const float dt = 1f / 60f;
            // Immobile : la lame ne pousse rien.
            for (int i = 0; i < 20; i++) squeegee.Tick(true, AimAt(pad, pad.UVToWorld(new Vector2(0.28f, 0.5f))), dt);
            Assert.AreEqual(bandBefore, WaterIn(pad, 0f, 0.42f), bandBefore * 1e-3f, "Sans geste de raclage, rien ne bouge.");

            // Raclage vers le caniveau sur plusieurs passes (la lame couvre une bande de ~0,6 m).
            for (float v = 0.2f; v <= 0.8f; v += 0.15f)
                for (float u = 0.25f; u <= 0.62f; u += 0.004f)
                    squeegee.Tick(true, AimAt(pad, pad.UVToWorld(new Vector2(u, v))), dt);
            squeegee.Tick(false, default, dt);
            float bandAfter = WaterIn(pad, 0f, 0.42f);
            Assert.Less(bandAfter, bandBefore * 0.5f, "L'eau quitte la zone raclée…");
            Assert.Greater(WaterIn(pad, 0.5f, 0.75f), bandBefore * 0.12f, "…et une partie se retrouve devant la lame…");
            Assert.Less(pad.Grid.TotalMoisture, totalBefore * 0.95f, "…pendant qu'une partie est réellement essuyée (mission de corrections).");
            Assert.Greater(pad.Grid.TotalMoisture, totalBefore * 0.15f, "Retrait progressif (selon la distance raclée), pas instantané.");

            for (float v = 0.2f; v <= 0.8f; v += 0.15f)
                for (float u = 0.45f; u <= 0.99f; u += 0.004f)
                    squeegee.Tick(true, AimAt(pad, pad.UVToWorld(new Vector2(u, v))), dt);
            squeegee.Tick(false, default, dt);
            Assert.Greater(squeegee.TotalDrained, 0f, "Ce qui arrive au caniveau est évacué.");
            Assert.Less(pad.Grid.TotalMoisture, totalBefore * 0.6f, "L'aire de lavage est réellement asséchée.");

            // Sur un tapis : l'eau seulement (mission de corrections), jamais la saleté.
            TestRugs.LoadDefault(game);
            var wetRug = new DepositOperation { Water = 0.5f };
            game.Rug.Grid.ApplyAll(ref wetRug);
            game.Rug.Grid.RecomputeTotals();
            float rugWater = game.Rug.Grid.TotalMoisture, rugDirt = game.Rug.Grid.TotalDirt;
            for (float u = 0.2f; u <= 0.8f; u += 0.01f)
                squeegee.Tick(true, AimAt(game.Rug, game.Rug.UVToWorld(new Vector2(u, 0.5f))), dt);
            Assert.IsTrue(squeegee.AimingAtRug);
            StringAssert.Contains("seulement l'eau", squeegee.GetStatusLine());
            squeegee.Tick(false, default, dt);
            Assert.Less(game.Rug.Grid.TotalMoisture, rugWater * 0.9f, "L'eau du tapis est chassée.");
            Assert.AreEqual(rugDirt, game.Rug.Grid.TotalDirt, 1e-3f, "La saleté du tapis n'est pas touchée.");
        }

        // ------------------------------------------------------------------ Monobrosse

        [UnityTest]
        public IEnumerator Rotary_ScrubsWetFibersFar_BetterThanDry_LathersFoam_AndSlowsThePlayer()
        {
            yield return LoadWorkshop(legacyTools: true);
            Grant("up_tool_rotary");
            var rotary = Tool<RotaryScrubberTool>("rotary");
            TestRugs.LoadDefault(game);
            var rug = game.Rug;
            const float dt = 1f / 60f;

            float Scrub(Vector2 uv)
            {
                float loosened = 0f;
                var aim = AimAt(rug, rug.UVToWorld(uv));
                for (int i = 0; i < 10; i++)
                {
                    rotary.Tick(true, aim, dt);
                    loosened += rotary.LastLoosenedPerSecond * dt;
                }
                rotary.Tick(false, default, dt);
                return loosened;
            }

            float dry = Scrub(new Vector2(0.25f, 0.5f));
            Assert.Less(rotary.LastEfficiency, 1f);
            var water = new DepositOperation { Water = 0.6f };
            rug.ApplyOperation(rug.UVToWorld(new Vector2(0.75f, 0.5f)), 0.5f, 2f, ref water);
            float wet = Scrub(new Vector2(0.75f, 0.5f));
            Assert.Greater(rotary.LastEfficiency, 0.9f, "Fibres humides : pleine efficacité.");
            Assert.Greater(wet, dry * 1.6f, "Sur fibres humides, la monobrosse détache bien plus qu'à sec.");
            Assert.Greater(dry, 0f);

            // Mousse : elle monte fortement sur une zone savonneuse.
            var soap = new SoapOperation { FoamPerSecond = 1f, DeltaTime = 0.2f };
            var foamPoint = rug.UVToWorld(new Vector2(0.5f, 0.3f));
            rug.ApplyOperation(foamPoint, 0.35f, 2f, ref soap);
            rug.TrySampleCell(foamPoint, out RugCell before);
            Scrub(new Vector2(0.5f, 0.3f));
            rug.TrySampleCell(foamPoint, out RugCell after);
            Assert.Greater(after.Foam, before.Foam, "La mousse monte sous le disque.");

            // Machine lourde : on avance moins vite pendant qu'elle tourne.
            Assert.IsTrue(game.ToolController.SelectSlot(rotary.Definition.slot));
            game.ToolController.AutomationUseOverride = true;
            yield return null;
            yield return null;
            Assert.IsTrue(rotary.IsUsing);
            Assert.Less(game.PlayerController.SpeedMultiplier, 0.8f);
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(rotary.SpinSpeed, 0f, "Le disque tourne.");
            game.ToolController.AutomationUseOverride = false;
            yield return null;
            yield return null;
            Assert.AreEqual(1f, game.PlayerController.SpeedMultiplier, 1e-6f);
        }

        // ------------------------------------------------------------------ Bottes et traces boueuses

        [UnityTest]
        public IEnumerator MuddySoles_MarkTheRug_AndBootsReduceTheTransfer()
        {
            yield return LoadWorkshop(legacyTools: true);
            TestRugs.LoadDefault(game);
            var trail = Object.FindAnyObjectByType<FootprintTrail>();
            var pad = game.WorkSurface.Surface;
            var rug = game.Rug;
            Vector3 mudSpot = pad.UVToWorld(new Vector2(0.5f, 0.04f));
            var mud = new DepositOperation { Type = DirtType.Mud, Mud = 0.9f };
            pad.ApplyOperation(mudSpot, 0.4f, 2f, ref mud);
            Vector3 onRug = rug.UVToWorld(new Vector2(0.5f, 0.5f));

            IEnumerator WalkFromMudOntoTheRug()
            {
                trail.CleanSoles();
                game.PlayerController.Teleport(new Vector3(mudSpot.x, 0.06f, mudSpot.z), 0f, 0f);
                yield return null;
                trail.Step(Vector3.forward);
                Assert.Greater(trail.MudLoad, 0.05f, "Semelles chargées dans la boue de l'aire.");
                game.PlayerController.Teleport(new Vector3(onRug.x, 0.06f, onRug.z), 0f, 0f);
                yield return null;
                trail.Step(Vector3.forward);
            }

            float dirtBefore = rug.Grid.TotalDirt;
            int prints = trail.RugFootprintsLeft;
            float deposited = trail.MudDeposited;
            yield return WalkFromMudOntoTheRug();
            Assert.AreEqual(prints + 1, trail.RugFootprintsLeft, "Une empreinte boueuse sur le tapis.");
            Assert.Greater(rug.Grid.TotalDirt, dirtBefore, "Vraie salissure du tapis, à nettoyer.");
            float shoes = trail.MudDeposited - deposited;

            Grant("up_boots");
            Assert.AreEqual(0.3f, trail.Transfer, 1e-6f);
            deposited = trail.MudDeposited;
            yield return WalkFromMudOntoTheRug();
            float boots = trail.MudDeposited - deposited;
            Assert.Greater(boots, 0f, "Les bottes réduisent le transfert sans l'annuler.");
            Assert.AreEqual(shoes * 0.3f, boots, shoes * 0.05f);

            // Le sol de la salle (hors zone de nettoyage) n'est jamais marqué.
            var floor = game.RoomFloor;
            float floorDirt = floor.Grid != null ? floor.Grid.TotalDirt : 0f;
            game.PlayerController.Teleport(new Vector3(-3.5f, 0.06f, -2.5f), 0f, 0f);
            yield return null;
            trail.Step(Vector3.forward);
            Assert.AreEqual(floorDirt, floor.Grid != null ? floor.Grid.TotalDirt : 0f, 1e-5f);
        }

        // ------------------------------------------------------------------ Robot aspirateur achetable

        [UnityTest]
        public IEnumerator Robot_MustBeBought_ThenRuns_AndTheMigratedSaveKeepsIt()
        {
            yield return LoadWorkshop(legacyTools: false);
            var robot = game.Robot;
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            Assert.IsFalse(robot.IsOwned, "Nouvelle partie : pas de robot.");
            foreach (var renderer in robot.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(renderer.enabled, "Robot caché tant qu'il n'est pas acheté.");
            // V0.4.2 : sa base est dans la zone 2 (annexe, cachée tant que l'agrandissement n'est pas acheté).
            StringAssert.Contains("acheter sur l'ordinateur", robot.GetPrompt(interactor));
            Assert.AreEqual(RobotVacuum.NotOwnedMessage, robot.Toggle());
            Assert.AreEqual(RobotMode.Docked, robot.Mode);

            UpgradeDefinition robotUpgrade = null;
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.id == "up_robot") robotUpgrade = upgrade;
            Assert.IsNotNull(robotUpgrade);
            StringAssert.Contains("robot aspirateur", game.Upgrades.DescribeEffect(robotUpgrade));
            Assert.AreEqual(PurchaseResult.InsufficientFunds, game.Shop.TryPurchase(robotUpgrade));
            game.Wallet.Wallet.Add(robotUpgrade.price);
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(robotUpgrade));
            yield return null;
            Assert.IsTrue(robot.IsOwned);
            // V0.4.2 : réservé à la zone 2 : sans l'agrandissement, il attend (message clair), caché.
            Assert.AreEqual(RobotVacuum.ZoneLockedMessage, robot.Toggle());
            Assert.AreEqual(RobotMode.Docked, robot.Mode);
            Assert.IsFalse(robot.GetComponentInChildren<Renderer>().enabled, "Zone 2 fermée : robot caché.");
            game.Upgrades.Grant("up_extension");
            yield return null;
            Assert.IsTrue(robot.GetComponentInChildren<Renderer>().enabled);
            var dock = Object.FindAnyObjectByType<RobotDock>();
            Assert.IsNotNull(dock, "Zone 2 ouverte : base du robot accessible.");
            dock.Interact(interactor);
            yield return null;
            Assert.AreNotEqual(RobotMode.Docked, robot.Mode, "Acheté, le robot démarre.");
            var saved = new SaveFileStore(TestSaveSandbox.Directory, SaveManager.DefaultFileName).Load().Data;
            CollectionAssert.Contains(saved.upgrades, "up_robot");

            // Partie déjà commencée (sauvegarde d'avant le jalon 4) : le robot reste acquis.
            yield return LoadWorkshop(legacyTools: true);
            Assert.IsTrue(game.Robot.IsOwned);
        }

        // ------------------------------------------------------------------ Captures (vérification visuelle)

        void LookAt(Vector3 target, float standoff)
        {
            float pitch = Mathf.Atan2(1.68f - target.y, standoff) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x - standoff, 0.06f, target.z), 90f, pitch);
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_SqueegeeAndRotary()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            yield return LoadWorkshop(legacyTools: true);
            Grant("up_tool_squeegee", "up_tool_rotary");
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            System.IO.Directory.CreateDirectory(folder);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;

            // Raclette : bande d'eau boueuse poussée vers le caniveau (bord droit de l'aire).
            var pad = game.WorkSurface.Surface;
            pad.Grid.Clear();
            var wet = new DepositOperation { Type = DirtType.Mud, Water = 0.7f, Mud = 0.35f };
            pad.Grid.ApplyRect(new RectInt(Mathf.RoundToInt(0.55f * pad.Grid.Width), 0, Mathf.RoundToInt(0.08f * pad.Grid.Width), pad.Grid.Height), 1f, ref wet);
            pad.Grid.RecomputeTotals();
            Assert.IsTrue(game.ToolController.SelectSlot(10));
            game.ToolController.AutomationUseOverride = true;
            for (float u = 0.5f; u <= 0.72f; u += 0.004f)
            {
                LookAt(pad.UVToWorld(new Vector2(u, 0.5f)), 1.2f);
                yield return null;
            }
            yield return CaptureFrame(camera, hud, System.IO.Path.Combine(folder, "65_squeegee_push.png"));
            // Contrôle : même outil, regard selon +Z.
            var probe = pad.UVToWorld(new Vector2(0.4f, 0.3f));
            game.PlayerController.Teleport(new Vector3(probe.x, 0.06f, probe.z - 1.2f), 0f, Mathf.Atan2(1.68f - probe.y, 1.2f) * Mathf.Rad2Deg);
            for (int i = 0; i < 10; i++) yield return null;
            yield return CaptureFrame(camera, hud, System.IO.Path.Combine(folder, "65b_squeegee_front.png"));
            Assert.Greater(Tool<SqueegeeTool>("squeegee").AimTip.position.y, pad.UVToWorld(new Vector2(0.4f, 0.3f)).y - 0.03f,
                "La lame reste posée sur l'aire de lavage, pas enfoncée dedans.");
            for (float u = 0.72f; u <= 0.93f; u += 0.004f)
            {
                LookAt(pad.UVToWorld(new Vector2(u, 0.5f)), 1.2f);
                yield return null;
            }
            yield return CaptureFrame(camera, hud, System.IO.Path.Combine(folder, "66_squeegee_drain.png"));
            game.ToolController.AutomationUseOverride = false;

            // Monobrosse sur un tapis mouillé.
            TestRugs.LoadDefault(game);
            var water = new DepositOperation { Water = 0.5f };
            game.Rug.Grid.ApplyAll(ref water);
            game.Rug.Grid.RecomputeTotals();
            Assert.IsTrue(game.ToolController.SelectSlot(11));
            LookAt(game.Rug.UVToWorld(new Vector2(0.5f, 0.5f)), 1.1f);
            game.ToolController.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.6f);
            yield return CaptureFrame(camera, hud, System.IO.Path.Combine(folder, "67_rotary.png"));
            game.ToolController.AutomationUseOverride = false;
        }

        static IEnumerator CaptureFrame(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }

        // ------------------------------------------------------------------ Poils et accessoire

        [UnityTest]
        public IEnumerator FiberAccessory_DoublesHairPickup_OfTheVacuum()
        {
            yield return LoadWorkshop(legacyTools: true);
            var vacuum = Tool<VacuumTool>("vacuum");
            Assert.AreEqual(1f, vacuum.FiberMultiplier);
            UpgradeDefinition accessory = null;
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.id == "up_vacuum_fiber") accessory = upgrade;
            Assert.IsNotNull(accessory);
            StringAssert.Contains("Poils et peluches", game.Upgrades.DescribeEffect(accessory));
            game.Wallet.Wallet.Add(accessory.price);
            Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(accessory));
            Assert.AreEqual(2f, vacuum.FiberMultiplier, 1e-6f);

            // Mesure réelle : poils d'animaux aspirés plus vite avec l'accessoire.
            TestRugs.LoadDefault(game);
            var rug = game.Rug;
            for (int y = 0; y < rug.Grid.Height; y++)
            for (int x = 0; x < rug.Grid.Width; x++)
            {
                var cell = rug.Grid.GetCell(x, y);
                cell.Dust = 0.6f;
                cell.DustType = DirtType.PetHair;
                rug.Grid.SetCell(x, y, cell);
            }
            rug.Grid.RecomputeTotals();
            float Vacuum(float multiplier, Vector2 uv)
            {
                vacuum.FiberMultiplier = multiplier;
                float before = rug.Grid.TotalDirt;
                var aim = AimAt(rug, rug.UVToWorld(uv));
                for (int i = 0; i < 20; i++) vacuum.Tick(true, aim, 1f / 60f);
                vacuum.Tick(false, default, 1f / 60f);
                return before - rug.Grid.TotalDirt;
            }
            float plain = Vacuum(1f, new Vector2(0.25f, 0.5f));
            float turbo = Vacuum(2f, new Vector2(0.75f, 0.5f));
            Assert.Greater(turbo, plain * 1.3f, "L'accessoire décolle davantage de poils.");
        }
    }
}
