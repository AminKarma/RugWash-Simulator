using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Player;
using RugWash.UI;
using RugWash.Workshop;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RugWash.Tests
{
    /// <summary>
    /// Jalon 2 (V2) dans la vraie scène : canon à mousse, mousse au repos, ruissellement vers l'aire de lavage,
    /// balai, recontamination sans blocage, brosse de détail à acheter, traces de pas, robot aspirateur, sauvegarde.
    /// </summary>
    public class WorkshopV2PlayModeTests
    {
        GameBootstrap game;
        PlayerToolController tools;
        Keyboard keyboard;
        InputSettings originalInputSettings;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            yield return Load();
            TestRugs.LoadDefault(game);
        }

        IEnumerator Load()
        {
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            tools = game.ToolController;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (tools != null) tools.AutomationUseOverride = false;
            if (keyboard != null)
            {
                InputSystem.RemoveDevice(keyboard);
                keyboard = null;
            }
            if (originalInputSettings != null)
            {
                var temporary = InputSystem.settings;
                InputSystem.settings = originalInputSettings;
                originalInputSettings = null;
                Object.Destroy(temporary);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        // ------------------------------------------------------------------ Aides

        Vector3 AimAt(Vector3 target, float yaw, float standoff)
        {
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back * standoff;
            var feet = new Vector3(target.x + back.x, 0.06f, target.z + back.z);
            float pitch = Mathf.Atan2(feet.y + 1.62f - target.y, standoff) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(feet, yaw, pitch);
            return target;
        }

        Vector3 AimAtRug(Vector2 uv, float yaw = 0f, float standoff = 1.2f) => AimAt(game.Rug.UVToWorld(uv), yaw, standoff);

        IEnumerator Use(float seconds)
        {
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(seconds);
            tools.AutomationUseOverride = false;
            yield return null;
        }

        static RugCell CellAt(RugCleaningController surface, Vector3 world)
        {
            Assert.IsTrue(surface.TrySampleCell(world, out RugCell cell), $"Point hors de la surface {surface.name} : {world}");
            return cell;
        }

        RugCleaningController Pad => game.WorkSurface.Surface;

        /// <summary>Dépose des débris secs (aspirables) sur le pourtour de l'aire de lavage, hors du tapis.</summary>
        void SeedDryDebrisAroundRug()
        {
            var grid = Pad.Grid;
            var debris = new DepositOperation { Type = DirtType.Dust, Debris = 0.3f };
            grid.ApplyRect(new RectInt(0, 0, grid.Width, 6), 1f, ref debris);
            grid.ApplyRect(new RectInt(0, grid.Height - 6, grid.Width, 6), 1f, ref debris);
            grid.ApplyRect(new RectInt(0, 6, 6, grid.Height - 12), 1f, ref debris);
            grid.ApplyRect(new RectInt(grid.Width - 6, 6, 6, grid.Height - 12), 1f, ref debris);
            game.WorkSurface.RefreshStatus(force: true);
        }

        /// <summary>Salit une bande de l'aire de lavage devant le tapis (côté joueur).</summary>
        void SoilFrontOfWashArea(float mud = 0.4f)
        {
            var deposit = new DepositOperation { Type = DirtType.Mud, Mud = mud, Water = 0.6f, Foam = 0.5f };
            Pad.Grid.ApplyRect(new RectInt(30, 2, 30, 10), 1f, ref deposit);
            game.WorkSurface.RefreshStatus(force: true);
        }

        Keyboard VirtualKeyboard()
        {
            if (keyboard != null) return keyboard;
            originalInputSettings = InputSystem.settings;
            var settings = Object.Instantiate(originalInputSettings);
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = settings;
            keyboard = InputSystem.AddDevice<Keyboard>("RugWashTestKeyboard2");
            return keyboard;
        }

        IEnumerator Hold(Key key, float seconds)
        {
            InputSystem.QueueStateEvent(VirtualKeyboard(), new KeyboardState(key));
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        static float SumLoosened(RugCleaningGrid grid)
        {
            float sum = 0f;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                sum += grid.GetCell(x, y).Loosened;
            return sum;
        }

        // ------------------------------------------------------------------ 2.1 / 2.2 Mousse

        [UnityTest]
        public IEnumerator FoamCannon_SpraysAWideFan_AndConsumesDetergent()
        {
            Vector3 target = AimAtRug(new Vector2(0.5f, 0.5f), 0f, 1.4f);
            Assert.IsTrue(tools.SelectSlot(6));
            Assert.IsInstanceOf<FoamCannonTool>(tools.ActiveTool);
            yield return new WaitForSeconds(0.3f);
            float soapBefore = tools.Equipment.Soap.Amount;
            yield return Use(0.5f);

            Assert.Less(tools.Equipment.Soap.Amount, soapBefore, "Le canon consomme du détergent.");
            Assert.Greater(CellAt(game.Rug, target).Foam, 0.3f, "Mousse épaisse au point visé.");
            Assert.Greater(CellAt(game.Rug, target + new Vector3(0.38f, 0f, 0f)).Foam, 0.05f, "L'éventail couvre large en travers de la visée.");
            Assert.AreEqual(0f, CellAt(game.Rug, target + new Vector3(0f, 0f, 0.38f)).Foam, "…et reste étroit dans l'axe de la visée.");
        }

        [UnityTest]
        public IEnumerator Foam_LeftInPlace_LiftsGrease_ThenFades()
        {
            var grid = game.Rug.Grid;
            grid.SetCell(10, 10, new RugCell { Stain = 0.6f, StainType = DirtType.Grease, Foam = 1f });
            game.Rug.Age(10f);
            var cell = grid.GetCell(10, 10);
            Assert.Less(cell.Stain, 0.6f, "La mousse au repos détache la graisse.");
            Assert.Greater(game.Rug.FoamDwellLoosened, 0f);
            for (int i = 0; i < 30; i++) game.Rug.Age(10f);
            Assert.AreEqual(0f, grid.GetCell(10, 10).Foam, 1e-6f, "La mousse disparaît progressivement.");

            // La simulation lente tourne aussi seule dans la scène.
            grid.SetCell(12, 12, new RugCell { Foam = 0.8f });
            yield return new WaitForSeconds(0.6f);
            Assert.Less(grid.GetCell(12, 12).Foam, 0.8f);
        }

        // ------------------------------------------------------------------ 2.4 Plan de travail

        [UnityTest]
        public IEnumerator RinsingNearAnEdge_RunsOffOntoTheWashArea_OnThatSide()
        {
            AimAtRug(new Vector2(0.5f, 0.08f), 0f, 1.2f);
            tools.SelectSlot(2);
            yield return Use(0.6f);
            tools.SelectSlot(4);
            yield return Use(1.2f);
            game.WorkSurface.FlushRunoff();
            Assert.Greater(game.WorkSurface.DepositCount, 0, "Le rinçage a fait ruisseler de l'eau sur l'aire de lavage.");

            Vector3 nearSide = game.Rug.transform.TransformPoint(new Vector3(0f, 0f, -game.Rug.SizeMeters.y * 0.5f - 0.2f));
            Vector3 farSide = game.Rug.transform.TransformPoint(new Vector3(0f, 0f, game.Rug.SizeMeters.y * 0.5f + 0.2f));
            var near = CellAt(Pad, nearSide);
            Assert.Greater(near.Moisture, 0.05f, "Eau sur l'aire de lavage, du côté rincé.");
            Assert.Greater(near.Foam + near.Loosened, 0.01f, "Mousse ou eau sale emportée avec l'eau.");
            var far = CellAt(Pad, farSide);
            Assert.AreEqual(0f, far.Moisture + far.Foam + far.Loosened, 1e-6f, "Rien de l'autre côté du tapis.");
        }

        [UnityTest]
        public IEnumerator Broom_ClearsTheWashArea_AndTheHudFollowsItsState()
        {
            SoilFrontOfWashArea();
            Assert.IsFalse(game.WorkSurface.Status.IsClean);
            yield return null;
            StringAssert.Contains($"Aire de lavage : {game.WorkSurface.Status.CleanlinessPercent} % propre", game.Hud.WorkSurfaceAlert);

            Assert.IsTrue(tools.SelectSlot(7));
            Assert.IsInstanceOf<BroomTool>(tools.ActiveTool);
            var spot = Pad.UVToWorld(new Vector2(45f / 92f, 7f / 72f));
            var before = CellAt(Pad, spot);
            float soilBefore = SumLoosened(Pad.Grid);
            AimAt(spot, 0f, 1.2f);
            yield return new WaitForSeconds(0.2f);
            yield return Use(1.5f);
            var after = CellAt(Pad, spot);
            Assert.Less(after.Loosened, before.Loosened * 0.3f, "Le balai pousse la boue.");
            Assert.Less(after.Moisture, before.Moisture * 0.5f, "…et l'eau.");
            Assert.Less(after.Foam, before.Foam * 0.5f, "…et la mousse.");
            Assert.Less(SumLoosened(Pad.Grid), soilBefore);

            // Une fois l'aire propre, l'alerte disparaît.
            var clean = new SweepOperation { Table = Pad.DirtTable, Power = 10f, DeltaTime = 1f, HardFloor = true };
            Pad.Grid.ApplyAll(ref clean);
            game.WorkSurface.RefreshStatus(force: true);
            yield return null;
            Assert.IsTrue(game.WorkSurface.Status.IsClean);
            Assert.AreEqual("Aire de lavage : propre", game.Hud.WorkSurfaceAlert);
        }

        /// <summary>Salit une part donnée de l'aire de lavage (rangées complètes, boue et eau sale).</summary>
        void SoilWashAreaFraction(float fraction)
        {
            var grid = Pad.Grid;
            var deposit = new DepositOperation { Type = DirtType.Mud, Mud = 0.4f, Water = 0.3f };
            int rows = Mathf.RoundToInt(grid.Height * fraction);
            grid.ApplyRect(new RectInt(0, 0, grid.Width, rows), 1f, ref deposit);
            game.WorkSurface.RefreshStatus(force: true);
        }

        [UnityTest]
        public IEnumerator WashAreaAboveFortyPercentDirt_RecontaminatesTheRugEdges_WithoutBlockingTheOrder()
        {
            // 50 % de saleté = 50 % de propreté : la commande démarre (≥ 25 %), mais le tapis se salit (> 40 % de saleté).
            SoilWashAreaFraction(0.5f);
            Assert.AreEqual(0.5f, game.WorkSurface.Status.SoiledFraction, 0.02f);
            Assert.IsTrue(game.WorkSurface.AllowsNewOrder);
            Assert.IsTrue(game.WorkSurface.ContaminatesRug);
            game.Orders.ReturnToWorkshop();
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            var header = game.OrderBoard.GetComponentsInChildren<Text>(true);
            bool warned = false;
            foreach (var text in header) warned |= text.text.Contains("le tapis posé dessus se salit");
            Assert.IsTrue(warned, "Le comptoir prévient que le tapis se salira.");

            game.OrderBoard.Rows[0].ActionButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State, "Propreté ≥ 25 % : la commande démarre.");
            Assert.Greater(game.WorkSurface.LastContamination, 0f);
            var grid = game.Rug.Grid;
            float border = grid.GetCell(1, grid.Height / 2).Loosened;
            float center = grid.GetCell(grid.Width / 2, grid.Height / 2).Loosened;
            Assert.GreaterOrEqual(border, game.WorkSurface.LastContamination * 0.99f, "Bordure recontaminée.");
            Assert.Less(center, border, "Le centre du tapis n'est pas touché.");
            Assert.AreEqual(0f, game.Rug.Cleanliness01, 1e-3f, "La propreté se mesure à partir du tapis recontaminé.");
        }

        [UnityTest]
        public IEnumerator WashAreaBelowFortyPercentDirt_DoesNotTouchTheNextRug()
        {
            Assert.IsTrue(game.WorkSurface.Status.IsClean);
            // Sale mais sous le seuil : 30 % de saleté (70 % de propreté).
            SoilWashAreaFraction(0.3f);
            Assert.IsFalse(game.WorkSurface.Status.IsClean);
            Assert.IsFalse(game.WorkSurface.ContaminatesRug);
            game.Orders.ReturnToWorkshop();
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            game.OrderBoard.Rows[0].ActionButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State);
            Assert.AreEqual(0f, game.WorkSurface.LastContamination);
            float dirt = game.Rug.Grid.TotalDirt;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(dirt, game.Rug.Grid.TotalDirt, 1e-4f, "Aucune saleté ajoutée en continu sous le seuil.");
        }

        // ------------------------------------------------------------------ 2.3 Balais et brosses

        [UnityTest]
        public IEnumerator DetailBrush_IsLockedUntilBought_ThenSelectable()
        {
            var detail = tools.Tools[7];
            Assert.IsFalse(detail.IsUnlocked);
            CleaningToolBase requested = null;
            tools.LockedToolRequested += t => requested = t;
            Assert.IsFalse(tools.SelectSlot(8), "Outil pas encore acheté.");
            Assert.AreEqual(detail, requested);
            var toolbar = Object.FindAnyObjectByType<ToolSelectionUI>();
            // Mission de corrections : la barre n'affiche que les outils possédés.
            CollectionAssert.DoesNotContain(toolbar.Shown, detail, "Outil non possédé : absent de la barre.");

            game.Wallet.Wallet.Add(500);
            Assert.IsTrue(game.ShopUI.Open());
            int index = -1;
            for (int i = 0; i < game.Upgrades.Catalog.Count; i++)
                if (game.Upgrades.Catalog[i].id == "up_detail_brush") index = i;
            Assert.GreaterOrEqual(index, 0);
            Assert.AreEqual(PurchaseResult.Purchased, game.ShopUI.Buy(index));
            game.ShopUI.Close();
            yield return null;

            Assert.IsTrue(detail.IsUnlocked);
            Assert.IsTrue(tools.SelectSlot(8));
            Assert.AreEqual(detail, tools.ActiveTool);
            CollectionAssert.Contains(toolbar.Shown, detail, "Acheté : il apparaît aussitôt dans la barre.");
            foreach (var label in toolbar.GetComponentsInChildren<Text>()) StringAssert.DoesNotContain("(boutique)", label.text);
        }

        [UnityTest]
        public IEnumerator DetailBrush_BeatsTheRegularBrushOnGrease()
        {
            game.Upgrades.Restore(new[] { "up_detail_brush" });
            var grid = game.Rug.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                grid.SetCell(x, y, new RugCell { Stain = 0.8f, StainType = DirtType.Grease });

            var a = new Vector2(0.3f, 0.5f);
            var b = new Vector2(0.7f, 0.5f);
            Assert.IsTrue(tools.SelectSlot(3));
            AimAtRug(a, 0f, 0.6f);
            yield return new WaitForSeconds(0.2f);
            yield return Use(1f);
            Assert.IsTrue(tools.SelectSlot(8));
            AimAtRug(b, 0f, 0.6f);
            yield return new WaitForSeconds(0.2f);
            yield return Use(1f);

            float regular = 0.8f - CellAt(game.Rug, game.Rug.UVToWorld(a)).Stain;
            float detail = 0.8f - CellAt(game.Rug, game.Rug.UVToWorld(b)).Stain;
            Assert.Greater(regular, 0f);
            Assert.Greater(detail, regular * 1.5f, $"Brosse de détail {detail:0.000} contre brosse {regular:0.000} sur la graisse.");
        }

        // ------------------------------------------------------------------ Traces de pas

        [UnityTest]
        public IEnumerator WalkingThroughMud_LeavesFootprintsOnTheWashArea_NeverOnTheRoomFloor()
        {
            game.Rug.Unload();
            var deposit = new DepositOperation { Type = DirtType.Mud, Mud = 0.8f, Water = 0.5f };
            Pad.Grid.ApplyRect(new RectInt(78, 0, 13, 72), 1f, ref deposit);
            var floor = game.RoomFloor;
            Assert.AreEqual(0f, floor.Grid.TotalDirt, "Le sol de la salle n'a plus de saleté décorative.");
            var trail = Object.FindAnyObjectByType<FootprintTrail>();

            // Marche le long de la bande boueuse (semelles chargées), puis vers l'intérieur propre de l'aire.
            game.PlayerController.Teleport(new Vector3(1.95f, 0.08f, -0.7f), 0f, 20f);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(Key.W, 1f);
            Assert.Greater(trail.MudLoad, 0.05f, "Les semelles se chargent de boue.");
            float padMud = SumLoosened(Pad.Grid);
            int prints = trail.FootprintsLeft;
            yield return Hold(Key.A, 1.2f);
            Assert.Greater(trail.FootprintsLeft, prints, "Empreintes sur l'aire de lavage.");

            // Puis sortie de l'aire : le sol de la salle reste intact.
            yield return Hold(Key.D, 2.6f);
            Assert.Greater(game.PlayerController.transform.position.x, 2.6f, "Le joueur est sorti de l'aire de lavage.");
            Assert.AreEqual(0f, floor.Grid.TotalDirt + floor.Grid.TotalMoisture + floor.Grid.TotalFoam, 1e-6f,
                "Aucune trace ni eau hors de la zone de nettoyage.");
            Assert.Greater(padMud, 0f);
        }

        [UnityTest]
        public IEnumerator RoomFloor_IsOutsideTheCleaningZone_ToolsAndDepositsDoNothing()
        {
            var floor = game.RoomFloor;
            Assert.IsFalse(floor.AcceptsDirt);
            Assert.AreEqual(0f, floor.Grid.TotalDirt, "Plus de poussière décorative sur le sol.");
            var deposit = new DepositOperation { Type = DirtType.Mud, Mud = 1f, Water = 1f, Foam = 1f, Debris = 1f };
            Assert.AreEqual(0, floor.ApplyOperation(new Vector3(-3f, 0f, 2f), 0.5f, 2f, ref deposit), "Aucune opération sur le sol.");
            Assert.AreEqual(0f, floor.Grid.TotalDirt + floor.Grid.TotalMoisture + floor.Grid.TotalFoam, 1e-6f);

            // Rinçage dirigé vers le sol : la visée n'en fait pas une cible (seul l'effet visuel du jet existe).
            AimAt(new Vector3(-3f, 0f, 2f), 0f, 1.5f);
            tools.SelectSlot(4);
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(tools.CurrentAim.HasHit);
            Assert.IsNull(tools.CurrentAim.Rug, "Le sol n'est pas une cible de nettoyage.");
            yield return Use(0.6f);
            Assert.AreEqual(0f, floor.Grid.TotalMoisture, 1e-6f);
        }

        // ------------------------------------------------------------------ 2.5 Robot aspirateur

        [UnityTest]
        public IEnumerator Robot_StartsFromItsDock_CleansTheWashArea_AvoidsTheRug_AndReturnsWhenStopped()
        {
            var robot = game.Robot;
            SeedDryDebrisAroundRug();
            float debrisBefore = Pad.Grid.TotalDirt;
            Assert.AreEqual(Pad, robot.Floor, "Le robot travaille sur l'aire de lavage.");
            // Le tapis posé est un obstacle de la grille d'occupation.
            var rugCell = robot.Occupancy.WorldToCell(new Vector2(game.Rug.transform.position.x, game.Rug.transform.position.z));
            Assert.IsTrue(robot.Occupancy.IsBlocked(rugCell), "Le tapis posé est un obstacle.");
            Assert.AreEqual(RobotMode.Docked, robot.Mode);
            Assert.IsTrue(robot.IsAtDock);
            var dock = Object.FindAnyObjectByType<RobotDock>();
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            StringAssert.Contains("Démarrer", dock.GetPrompt(interactor));
            StringAssert.Contains("démarré", dock.Interact(interactor));
            Assert.AreEqual(RobotMode.Cleaning, robot.Mode);

            Vector3 start = robot.transform.position;
            float end = Time.time + 8f;
            while (Time.time < end)
            {
                yield return null;
                var cell = robot.Occupancy.WorldToCell(new Vector2(robot.transform.position.x, robot.transform.position.z));
                Assert.IsFalse(robot.Occupancy.IsBlocked(cell), $"Le robot est entré dans une case bloquée : {cell}");
                Assert.IsTrue(Pad.TryWorldToUV(robot.transform.position, out _), "Le robot reste sur l'aire de lavage.");
            }
            Assert.Greater(Vector3.Distance(start, robot.transform.position), 0.3f, "Le robot s'est éloigné de sa base.");
            Assert.Greater(robot.TotalCleaned, 0f, "Il a réellement aspiré les débris de l'aire de lavage.");
            Assert.Less(Pad.Grid.TotalDirt, debrisBefore);

            StringAssert.Contains("arrêté", robot.Toggle());
            Assert.AreEqual(RobotMode.Returning, robot.Mode);
            end = Time.time + 45f;
            while (robot.Mode != RobotMode.Docked && Time.time < end) yield return null;
            Assert.AreEqual(RobotMode.Docked, robot.Mode, "Le robot est rentré à sa base.");
            Assert.IsTrue(robot.IsAtDock);
        }

        [UnityTest]
        public IEnumerator Robot_StopsForThePlayer_ThenCarriesOn()
        {
            var robot = game.Robot;
            SeedDryDebrisAroundRug();
            robot.Toggle();
            // Attend un trajet en ligne droite d'au moins 0,8 m, robot déjà orienté vers son prochain point.
            Vector2 heading = default;
            float wait = Time.time + 20f;
            while (Time.time < wait)
            {
                yield return null;
                var next = robot.NextWaypoint;
                if (!next.HasValue) continue;
                var here = new Vector2(robot.transform.position.x, robot.transform.position.z);
                heading = next.Value - here;
                var forward = new Vector2(robot.transform.forward.x, robot.transform.forward.z);
                if (heading.magnitude > 0.6f && Vector2.Angle(forward, heading) < 10f) break;
            }
            Assert.Greater(heading.magnitude, 0.6f, "Le robot doit être en route.");
            int bumps = robot.BumpCount;
            Vector3 ahead = robot.transform.position + new Vector3(heading.x, 0f, heading.y).normalized * 0.5f;
            game.PlayerController.Teleport(new Vector3(ahead.x, 0.06f, ahead.z), 0f, 0f);
            float end = Time.time + 2f;
            while (robot.BumpCount == bumps && Time.time < end) yield return null;
            Assert.Greater(robot.BumpCount, bumps, "Le robot détecte le joueur devant lui.");
            Vector3 stopped = robot.transform.position;
            yield return new WaitForSeconds(0.4f);
            Assert.Less(Vector3.Distance(stopped, robot.transform.position), 0.05f, "Il attend au lieu de pousser le joueur.");

            game.PlayerController.Teleport(new Vector3(-4f, 0.06f, -3.5f), 0f, 0f);
            yield return new WaitForSeconds(3f);
            Assert.Greater(Vector3.Distance(stopped, robot.transform.position), 0.1f, "Il repart une fois la voie libre.");
        }

        [UnityTest]
        public IEnumerator Robot_LowBattery_ReturnsToCharge_ThenRestartsByItself()
        {
            var robot = game.Robot;
            SeedDryDebrisAroundRug();
            robot.Toggle();
            yield return new WaitForSeconds(1.5f);
            robot.Brain.Restore((int)RobotMode.Cleaning, 0.201f, false);
            float end = Time.time + 3f;
            while (robot.Mode == RobotMode.Cleaning && Time.time < end) yield return null;
            Assert.AreEqual(RobotMode.Returning, robot.Mode);
            Assert.IsTrue(robot.Brain.ResumeAfterCharge);

            end = Time.time + 45f;
            while (robot.Mode != RobotMode.Docked && Time.time < end) yield return null;
            Assert.AreEqual(RobotMode.Docked, robot.Mode);
            robot.Brain.ChargePerSecond = 2f;
            end = Time.time + 3f;
            while (robot.Mode != RobotMode.Cleaning && Time.time < end) yield return null;
            Assert.AreEqual(RobotMode.Cleaning, robot.Mode, "Rechargé, il repart seul.");
        }

        [UnityTest]
        public IEnumerator RobotAndWashArea_AreSavedAndRestored()
        {
            var robot = game.Robot;
            SeedDryDebrisAroundRug();
            robot.Toggle();
            yield return new WaitForSeconds(4f);
            SoilFrontOfWashArea();
            float soiled = game.WorkSurface.Status.SoiledFraction;
            Vector3 position = robot.transform.position;
            float battery = robot.Brain.Battery01;
            Assert.IsTrue(game.SaveSystem.SaveNow("test atelier"));

            yield return Load();
            robot = game.Robot;
            Assert.AreEqual(RobotMode.Cleaning, robot.Mode, "Le robot reprend son travail.");
            Assert.Less(Vector3.Distance(position, robot.transform.position), 0.1f, "À la position où il était.");
            Assert.AreEqual(battery, robot.Brain.Battery01, 0.02f);
            game.WorkSurface.RefreshStatus(force: true);
            Assert.AreEqual(soiled, game.WorkSurface.Status.SoiledFraction, 0.002f, "L'aire de lavage reste sale après rechargement.");
        }

        // ------------------------------------------------------------------ Captures

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_WorkshopGallery()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;

            // Canon à mousse en action, puis mousse au repos de près.
            AimAtRug(new Vector2(0.45f, 0.5f), 10f, 1.5f);
            tools.SelectSlot(6);
            yield return new WaitForSeconds(0.4f);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.7f);
            yield return Capture(camera, hud, Path.Combine(folder, "30_foam_cannon.png"));
            tools.AutomationUseOverride = false;
            yield return new WaitForSeconds(0.5f);
            AimAtRug(new Vector2(0.45f, 0.5f), 10f, 0.7f);
            yield return new WaitForSeconds(0.3f);
            yield return Capture(camera, hud, Path.Combine(folder, "31_foam_close.png"));

            // Ruissellement : rinçage près du bord, aire de lavage vue d'en haut.
            AimAtRug(new Vector2(0.4f, 0.1f), 0f, 1.2f);
            tools.SelectSlot(4);
            yield return Use(2.5f);
            game.WorkSurface.FlushRunoff();
            game.PlayerController.Teleport(new Vector3(-0.6f, 0.06f, -2.6f), 0f, 48f);
            yield return new WaitForSeconds(0.4f);
            yield return Capture(camera, hud, Path.Combine(folder, "32_wash_area_runoff.png"));

            // Balai sur l'aire de lavage.
            SoilFrontOfWashArea();
            AimAt(Pad.UVToWorld(new Vector2(0.45f, 0.1f)), 0f, 1.1f);
            tools.SelectSlot(7);
            yield return new WaitForSeconds(0.3f);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.8f);
            yield return Capture(camera, hud, Path.Combine(folder, "33_broom_wash_area.png"));
            tools.AutomationUseOverride = false;

            // Traces de pas sur le sol.
            var deposit = new DepositOperation { Type = DirtType.Mud, Mud = 0.8f, Water = 0.5f };
            Pad.Grid.ApplyRect(new RectInt(78, 0, 13, 72), 1f, ref deposit);
            game.PlayerController.Teleport(new Vector3(1.95f, 0.08f, -0.7f), 0f, 20f);
            yield return new WaitForSeconds(0.2f);
            yield return Hold(Key.W, 1f);
            yield return Hold(Key.D, 1.3f);
            // Vue de dessus du parcours : bande boueuse de l'aire de lavage puis empreintes sur le sol.
            var walkEnd = game.PlayerController.transform.position;
            game.PlayerController.Teleport(new Vector3(-4f, 0.06f, -3.5f), 0f, 0f);
            yield return new WaitForSeconds(0.2f);
            yield return CaptureTopDown(new Vector3((1.9f + walkEnd.x) * 0.5f, 0f, walkEnd.z - 0.4f), 1.6f, Path.Combine(folder, "34_footprints.png"));

            // Robot : sur sa base, puis en nettoyage (trace propre dans la poussière).
            var robot = game.Robot;
            SeedDryDebrisAroundRug();
            tools.SelectSlot(2);
            var dockView = robot.transform.position + new Vector3(1.2f, 0f, -1.1f);
            game.PlayerController.Teleport(new Vector3(dockView.x, 0.06f, dockView.z), Quaternion.LookRotation(robot.transform.position - dockView).eulerAngles.y, 42f);
            yield return new WaitForSeconds(0.4f);
            yield return Capture(camera, hud, Path.Combine(folder, "35_robot_docked.png"));
            robot.Toggle();
            yield return new WaitForSeconds(20f);
            Vector3 r = robot.transform.position;
            // Recul vers le centre de l'aire de lavage, regard vers le robot.
            Vector3 toCenter = new Vector3(-r.x, 0f, 0.8f - r.z).normalized;
            Vector3 eye = r + toCenter * 1.3f;
            float yaw = Quaternion.LookRotation(-toCenter).eulerAngles.y;
            game.PlayerController.Teleport(new Vector3(eye.x, 0.06f, eye.z), yaw, 48f);
            yield return new WaitForSeconds(0.2f);
            yield return Capture(camera, hud, Path.Combine(folder, "36_robot_cleaning.png"));
            yield return CaptureTopDown(robot.transform.position, 2.2f, Path.Combine(folder, "36b_robot_trail_top.png"));

            // Boutique (12 articles) et comptoir avec alerte.
            game.Wallet.Wallet.Add(300);
            Assert.IsTrue(game.ShopUI.Open());
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "37_shop.png"));
            game.ShopUI.Close();
            Assert.IsTrue(game.Computer.Open());
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "39_computer.png"));
            game.Computer.Close();
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "38_order_board_warning.png"));
            game.OrderBoard.Close();
        }

        /// <summary>Vue de dessus orthographique (caméra temporaire de test) pour juger les traces au sol.</summary>
        static IEnumerator CaptureTopDown(Vector3 center, float halfSize, string path)
        {
            var go = new GameObject("TestTopDownCamera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = halfSize;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            camera.cullingMask = ~(1 << 9);
            go.transform.SetPositionAndRotation(center + Vector3.up * 3.2f, Quaternion.Euler(90f, 0f, 0f));
            yield return null;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(go);
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
