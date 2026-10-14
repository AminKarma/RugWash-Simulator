using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Editor;
using RugWash.Save;
using RugWash.Workshop;
using UnityEditor;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Règles du jalon 2 (V2) : mousse, balai, brosse de détail, plan de travail, sauvegarde de l'atelier, robot.</summary>
    public class WorkshopRulesTests
    {
        static readonly DirtTypeTable Table = new DirtTypeTable();

        static RugCleaningGrid Grid(int w = 40, int h = 40, float meters = 2f) => new RugCleaningGrid(w, h, new Vector2(meters, meters));

        // ------------------------------------------------------------------ Grille

        [Test]
        public void Ellipse_IsWideAcross_NarrowAlong_AndTouchesNothingOutside()
        {
            var grid = Grid();
            var add = new DepositOperation { Foam = 1f };
            int touched = grid.ApplyEllipse(new Vector2(0.5f, 0.5f), 0.5f, 0.15f, 0f, 2f, ref add);
            Assert.Greater(touched, 0);
            // Cellule de 5 cm : 0,4 m sur X est dans l'ellipse, 0,4 m sur Y ne l'est pas.
            Assert.Greater(grid.GetCell(20 + 8, 20).Foam, 0f, "L'éventail s'étend le long de son grand axe.");
            Assert.AreEqual(0f, grid.GetCell(20, 20 + 8).Foam, "Rien au-delà du petit axe.");
            int foamy = 0;
            for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
                if (grid.GetCell(x, y).Foam > 0f) foamy++;
            Assert.AreEqual(touched, foamy, "Seules les cellules de l'ellipse sont modifiées.");
        }

        [Test]
        public void ApplyAllTracked_UpdatesTotals_AndOnlyFlagsChangedCells()
        {
            var grid = Grid();
            grid.SetCell(5, 6, new RugCell { Foam = 0.5f });
            grid.TryConsumeDirtyRect(out _);
            var aging = new SurfaceAgingOperation { Table = Table, DeltaTime = 1f, FoamDecayPerSecond = 0.5f };
            int changed = grid.ApplyAllTracked(ref aging);
            Assert.AreEqual(1, changed);
            Assert.IsTrue(grid.TryConsumeDirtyRect(out RectInt rect));
            Assert.AreEqual(new RectInt(5, 6, 1, 1), rect, "Seule la cellule modifiée est renvoyée au rendu.");
            float total = grid.TotalFoam;
            grid.RecomputeTotals();
            Assert.AreEqual(grid.TotalFoam, total, 1e-5f, "Totaux incrémentaux cohérents.");
        }

        // ------------------------------------------------------------------ Mousse

        [Test]
        public void Foam_LeftInPlace_LiftsSoapReactiveStains_ThenCollapsesIntoWater()
        {
            var cell = new RugCell { Stain = 0.6f, StainType = DirtType.Grease, Foam = 1f };
            var aging = new SurfaceAgingOperation
            {
                Table = Table, DeltaTime = 10f, FoamDecayPerSecond = 0.02f, FoamDecayFloor = 0.004f, FoamDwellPower = 0.012f, FoamToMoisture = 0.25f,
            };
            aging.Apply(ref cell, 1f);
            Assert.Less(cell.Stain, 0.6f, "La mousse au repos détache la graisse.");
            Assert.Greater(cell.Loosened, 0f);
            Assert.Less(cell.Foam, 1f, "La mousse retombe.");
            Assert.Greater(cell.Moisture, 0f, "La mousse retombée devient de l'eau.");

            for (int i = 0; i < 40; i++) aging.Apply(ref cell, 1f);
            Assert.AreEqual(0f, cell.Foam, 1e-6f, "La mousse finit toujours par disparaître.");
        }

        [Test]
        public void Foam_Absent_DoesNotLiftStains()
        {
            var noFoam = new RugCell { Stain = 0.5f, StainType = DirtType.Grease };
            var aging = new SurfaceAgingOperation { Table = Table, DeltaTime = 10f, FoamDwellPower = 0.012f };
            aging.Apply(ref noFoam, 1f);
            Assert.AreEqual(0.5f, noFoam.Stain);
            Assert.AreEqual(0f, aging.Loosened);
        }

        [Test]
        public void Drying_OnlyOnSurfacesThatDry()
        {
            var cell = new RugCell { Moisture = 0.5f };
            var rug = new SurfaceAgingOperation { DeltaTime = 10f };
            rug.Apply(ref cell, 1f);
            Assert.AreEqual(0.5f, cell.Moisture, "Un tapis ne sèche pas seul.");
            var pad = new SurfaceAgingOperation { DeltaTime = 10f, DryingPerSecond = 0.012f };
            pad.Apply(ref cell, 1f);
            Assert.AreEqual(0.38f, cell.Moisture, 1e-5f);
        }

        [Test]
        public void Brushing_WhipsFoam_AndDetailBrushBonusTargetsStubbornStains()
        {
            var foamy = new RugCell { Foam = 0.4f, Moisture = 0.3f };
            var brush = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 0.5f, LatherPerSecond = 0.5f };
            brush.Apply(ref foamy, 1f);
            Assert.Greater(foamy.Foam, 0.4f, "Brosser une zone savonneuse fait monter la mousse.");

            var dry = new RugCell();
            brush.Apply(ref dry, 1f);
            Assert.AreEqual(0f, dry.Foam, "Aucune mousse créée sans savon.");

            var normal = new RugCell { Stain = 0.8f, StainType = DirtType.Embedded };
            var detail = normal;
            var regular = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 0.5f };
            var stubborn = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 0.5f, StubbornBonus = 2.2f };
            regular.Apply(ref normal, 1f);
            stubborn.Apply(ref detail, 1f);
            Assert.AreEqual(2.2f, (0.8f - detail.Stain) / (0.8f - normal.Stain), 1e-3f);

            var drink = new RugCell { Stain = 0.8f, StainType = DirtType.Drink };
            var drinkDetail = drink;
            regular.Apply(ref drink, 1f);
            stubborn.Apply(ref drinkDetail, 1f);
            Assert.AreEqual(drink.Stain, drinkDetail.Stain, 1e-6f, "Le bonus ne concerne que graisse et taches incrustées.");
        }

        [Test]
        public void SoapOperation_ResiduePerFoam_DefaultsToPureDetergent()
        {
            var pure = new RugCell();
            var soap = new SoapOperation { FoamPerSecond = 1f, DeltaTime = 0.5f };
            soap.Apply(ref pure, 1f);
            Assert.AreEqual(0.4f, pure.Residue, 1e-5f);
            var diluted = new RugCell();
            var cannon = new SoapOperation { FoamPerSecond = 1f, DeltaTime = 0.5f, ResiduePerFoam = 0.35f };
            cannon.Apply(ref diluted, 1f);
            Assert.AreEqual(0.175f, diluted.Residue, 1e-5f, "La mousse du canon laisse moins de résidus.");
        }

        // ------------------------------------------------------------------ Balai

        [Test]
        public void Sweep_OnDryRug_PrefersHairAndDebris_LeavesStains()
        {
            var hair = new RugCell { Dust = 0.5f, DustType = DirtType.HairDebris, Stain = 0.4f, StainType = DirtType.Mud };
            var dust = new RugCell { Dust = 0.5f, DustType = DirtType.Dust };
            var sweep = new SweepOperation { Table = Table, Power = 0.9f, DeltaTime = 0.2f };
            sweep.Apply(ref hair, 1f);
            sweep.Apply(ref dust, 1f);
            Assert.Less(hair.Dust, dust.Dust, "Le balai est plus efficace sur les poils que sur la poussière.");
            Assert.AreEqual(0.4f, hair.Stain, "Les taches fixées ne bougent pas.");
        }

        [Test]
        public void Sweep_OnWetRug_IsWeak_ButOnHardFloorPushesWaterMudAndFoam()
        {
            var dryRug = new RugCell { Dust = 0.5f, DustType = DirtType.HairDebris };
            var wetRug = new RugCell { Dust = 0.5f, DustType = DirtType.HairDebris, Moisture = 0.3f };
            var sweep = new SweepOperation { Table = Table, Power = 0.9f, DeltaTime = 0.2f };
            sweep.Apply(ref dryRug, 1f);
            sweep.Apply(ref wetRug, 1f);
            Assert.Less(dryRug.Dust, wetRug.Dust, "L'humidité colle les débris aux fibres.");
            Assert.AreEqual(0.3f, wetRug.Moisture, "Sur un tapis, le balai n'enlève pas l'eau.");

            var floor = new RugCell { Loosened = 0.5f, StainType = DirtType.Mud, Moisture = 0.6f, Foam = 0.5f };
            var hard = new SweepOperation { Table = Table, Power = 0.9f, DeltaTime = 0.2f, HardFloor = true };
            hard.Apply(ref floor, 1f);
            Assert.Less(floor.Loosened, 0.5f);
            Assert.Less(floor.Moisture, 0.6f);
            Assert.Less(floor.Foam, 0.5f);
            Assert.Greater(hard.LiquidRemoved, 0f);
        }

        [Test]
        public void DirtTypes_HaveSweepResponses_InCodeAndInProjectLibrary()
        {
            Assert.Greater(DirtTypeTable.DefaultResponse(DirtType.HairDebris).sweep, DirtTypeTable.DefaultResponse(DirtType.Dust).sweep);
            Assert.AreEqual(0f, DirtTypeTable.DefaultResponse(DirtType.Grease).sweep);
            var library = AssetDatabase.LoadAssetAtPath<DirtTypeLibrary>(RugWashContentBuilder.DirtLibraryPath);
            Assert.IsNotNull(library);
            var table = library.BuildTable();
            Assert.Greater(table.GetResponse(DirtType.HairDebris).sweep, 0f, "Les définitions existantes ont reçu la valeur de balayage.");
        }

        // ------------------------------------------------------------------ Plan de travail

        [Test]
        public void WorkSurface_ClearWaterIsNotDirt_MudFoamAndDebrisAre()
        {
            Assert.IsFalse(WorkSurfaceRules.IsSoiled(new RugCell { Moisture = 1f }));
            Assert.IsTrue(WorkSurfaceRules.IsSoiled(new RugCell { Loosened = 0.1f }));
            Assert.IsTrue(WorkSurfaceRules.IsSoiled(new RugCell { Foam = 0.2f }));
            Assert.IsTrue(WorkSurfaceRules.IsSoiled(new RugCell { Dust = 0.1f }));

            var grid = Grid(10, 10);
            Assert.IsTrue(WorkSurfaceRules.Evaluate(grid).IsClean);
            grid.SetCell(0, 0, new RugCell { Loosened = 0.3f });
            grid.SetCell(1, 0, new RugCell { Loosened = 0.3f });
            var status = WorkSurfaceRules.Evaluate(grid);
            Assert.AreEqual(0.02f, status.SoiledFraction, 1e-5f);
            Assert.IsFalse(status.IsClean);
            Assert.AreEqual(2, status.SoiledPercent);
        }

        [Test]
        public void Runoff_LandsJustOutsideTheNearestRugEdge()
        {
            var half = new Vector2(1.5f, 1f);
            var right = WorkSurfaceRules.EdgeDepositPoint(new Vector2(1.3f, 0.2f), half, 0.2f, 0f);
            Assert.AreEqual(1.7f, right.x, 1e-5f);
            Assert.AreEqual(0.2f, right.y, 1e-5f);
            var bottom = WorkSurfaceRules.EdgeDepositPoint(new Vector2(-0.5f, -0.9f), half, 0.2f, 0f);
            Assert.AreEqual(-0.5f, bottom.x, 1e-5f);
            Assert.AreEqual(-1.2f, bottom.y, 1e-5f);
            var along = WorkSurfaceRules.EdgeDepositPoint(new Vector2(0f, 0.95f), half, 0.2f, 0.3f);
            Assert.AreEqual(1.2f, along.y, 1e-5f);
            Assert.AreEqual(0.3f, along.x, 1e-5f, "Décalage le long du bord pour étaler les flaques.");
            Assert.AreEqual(1.6f, WorkSurfaceRules.EdgeDepositPoint(new Vector2(5f, 0f), half, 0.1f, 0f).x, 1e-5f, "Point hors du tapis : bord le plus proche.");
        }

        static WorkSurfaceStatus Soiled(float fraction) => new WorkSurfaceStatus(fraction, 0f, 0f, 0f, 0f);

        [Test]
        public void Contamination_IsZeroUpToFortyPercentDirt_ThenGrowsAndIsBounded()
        {
            Assert.AreEqual(0f, WorkSurfaceRules.ContaminationAmount(Soiled(0.005f), 0.4f));
            Assert.AreEqual(0f, WorkSurfaceRules.ContaminationAmount(Soiled(0.4f), 0.4f), "Exactement 40 % : pas encore.");
            Assert.AreEqual(WorkSurfaceRules.MinContamination, WorkSurfaceRules.ContaminationAmount(Soiled(0.4001f), 0.4f), 1e-3f);
            float mid = WorkSurfaceRules.ContaminationAmount(Soiled(0.7f), 0.4f);
            Assert.Greater(mid, WorkSurfaceRules.MinContamination);
            Assert.Less(mid, WorkSurfaceRules.MaxContamination);
            Assert.AreEqual(WorkSurfaceRules.MaxContamination, WorkSurfaceRules.ContaminationAmount(Soiled(1f), 0.4f), 1e-6f);
            Assert.AreEqual(0f, WorkSurfaceRules.ContaminationRate(Soiled(0.4f), 0.4f, 0.02f));
            Assert.AreEqual(0.01f, WorkSurfaceRules.ContaminationRate(Soiled(0.7f), 0.4f, 0.02f), 1e-6f);
            Assert.AreEqual(1f / (Mathf.PI * 0.5f), WorkSurfaceRules.PerCellAmount(1f, 1f), 1e-5f);
        }

        [Test]
        public void ZoneConvention_CleanlinessIsOneMinusDirt_AndGatesNewOrdersAtTwentyFivePercent()
        {
            var status = Soiled(0.3f);
            Assert.AreEqual(0.7f, status.Cleanliness01, 1e-6f, "Propreté = 1 − saleté.");
            Assert.AreEqual(70, status.CleanlinessPercent);
            Assert.AreEqual(24, Soiled(0.751f).CleanlinessPercent, "Arrondi vers le bas : 24,9 % s'affiche 24 %.");
            Assert.IsTrue(WorkSurfaceRules.AllowsNewOrder(Soiled(0f), 0.25f));
            Assert.IsTrue(WorkSurfaceRules.AllowsNewOrder(Soiled(0.75f), 0.25f), "Exactement 25 % de propreté : autorisé.");
            Assert.IsFalse(WorkSurfaceRules.AllowsNewOrder(Soiled(0.76f), 0.25f));
            Assert.IsFalse(WorkSurfaceRules.ContaminatesRug(Soiled(0.4f), 0.4f));
            Assert.IsTrue(WorkSurfaceRules.ContaminatesRug(Soiled(0.41f), 0.4f), "Au-delà de 40 % de saleté (sous 60 % de propreté).");
        }

        [Test]
        public void BorderContamination_NeverExceedsItsCap_AndKeepsTheCenterClean()
        {
            var grid = Grid(40, 30);
            var bands = new RectInt[4];
            WorkSurfaceRules.BorderBands(grid.Width, grid.Height, bands);
            var mud = new BorderContaminationOperation { Amount = 0.1f, Cap = 0.3f };
            for (int i = 0; i < 10; i++)
                foreach (var band in bands) grid.ApplyRect(band, 1f, ref mud);
            Assert.AreEqual(0.3f, grid.GetCell(0, 15).Dirt, 1e-5f, "Plafonné.");
            Assert.AreEqual(0.3f, grid.GetCell(39, 0).Dirt, 1e-5f);
            Assert.AreEqual(0f, grid.GetCell(20, 15).Dirt, "Le centre n'est pas touché.");
            Assert.AreEqual(DirtType.Soil, grid.GetCell(0, 15).StainType);
        }

        [Test]
        public void RugFinish_BelowOnePercentRemaining_ClearsTheResidue_ButKeepsWaterAndFoam()
        {
            Assert.IsFalse(RugFinishRules.IsFinished(1f, 100f, 0.01f), "Exactement 1 % : pas encore.");
            Assert.IsTrue(RugFinishRules.IsFinished(0.99f, 100f, 0.01f));
            Assert.IsFalse(RugFinishRules.IsFinished(0f, 0f, 0.01f), "Tapis propre dès le départ : rien à terminer.");

            var grid = Grid(10, 10);
            grid.SetCell(3, 3, new RugCell { Dust = 0.01f, Stain = 0.02f, Loosened = 0.01f, Moisture = 0.5f, Foam = 0.3f, Residue = 0.1f,
                DustType = DirtType.Dust, StainType = DirtType.Soil });
            var clear = new ClearDirtOperation();
            grid.ApplyAll(ref clear);
            var cell = grid.GetCell(3, 3);
            Assert.AreEqual(0f, cell.Dirt);
            Assert.AreEqual(0f, grid.TotalDirt);
            Assert.AreEqual(0.5f, cell.Moisture, 1e-6f);
            Assert.AreEqual(0.3f, cell.Foam, 1e-6f);
            Assert.AreEqual(0.1f, cell.Residue, 1e-6f);
        }

        // ------------------------------------------------------------------ Sauvegarde

        [Test]
        public void SurfaceState_RoundTrips_WithinQuantization_AndRejectsWrongSizeOrGarbage()
        {
            var grid = Grid(30, 20);
            grid.SetCell(3, 4, new RugCell { Dust = 0.25f, DustType = DirtType.Dust, Loosened = 1.6f, StainType = DirtType.Mud, Foam = 0.7f, Moisture = 0.4f, Residue = 0.1f });
            string encoded = SurfaceStateCodec.Encode(grid);

            var copy = Grid(30, 20);
            Assert.IsTrue(SurfaceStateCodec.TryDecode(encoded, copy));
            var cell = copy.GetCell(3, 4);
            Assert.AreEqual(0.25f, cell.Dust, 0.003f);
            Assert.AreEqual(1.6f, cell.Loosened, 0.01f, "La saleté détachée (jusqu'à 2) est conservée.");
            Assert.AreEqual(0.7f, cell.Foam, 0.003f);
            Assert.AreEqual(DirtType.Mud, cell.StainType);
            Assert.AreEqual(grid.TotalDirt, copy.TotalDirt, 0.01f);

            Assert.IsFalse(SurfaceStateCodec.TryDecode(encoded, Grid(31, 20)), "Taille différente : refusée.");
            Assert.IsFalse(SurfaceStateCodec.TryDecode("pas du base64 !", Grid(30, 20)));
            Assert.IsFalse(SurfaceStateCodec.TryDecode(string.Empty, Grid(30, 20)));

            string clean = SurfaceStateCodec.Encode(new RugCleaningGrid(92, 72, new Vector2(4.6f, 3.6f)));
            Assert.Less(clean.Length, 1000, "Un plan de travail propre tient en moins d'1 Ko.");
        }

        [Test]
        public void OldSaveWithoutWorkshop_LoadsWithDefaultWorkshop()
        {
            const string json = "{\"version\":1,\"profileId\":\"profile0\",\"money\":50,\"reputation\":0,\"upgrades\":[],\"completedOrders\":[],\"settings\":{}}";
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(json, out SaveData data), "Version 1 migrée en version 2.");
            Assert.IsNotNull(data.workshop);
            Assert.IsFalse(data.workshop.hasRobot);
            Assert.AreEqual(string.Empty, data.workshop.floor);
            Assert.AreEqual(50, data.money);
        }

        // ------------------------------------------------------------------ Boutique

        [Test]
        public void Upgrades_UnlockTool_AndWidenIt()
        {
            var unlock = ScriptableObject.CreateInstance<UpgradeDefinition>();
            unlock.id = "u";
            unlock.effect = UpgradeEffect.UnlockTool;
            unlock.targetToolId = "detail_brush";
            var wide = ScriptableObject.CreateInstance<UpgradeDefinition>();
            wide.id = "w";
            wide.effect = UpgradeEffect.ToolRadius;
            wide.value = 1.35f;
            wide.targetToolId = "broom";

            var none = EquipmentStats.Compute(40f, 4f, 12f, new UpgradeDefinition[0]);
            Assert.IsFalse(none.IsUnlocked("detail_brush"));
            Assert.AreEqual(1f, none.RadiusMultiplier("broom"));

            var stats = EquipmentStats.Compute(40f, 4f, 12f, new[] { unlock, wide });
            Assert.IsTrue(stats.IsUnlocked("detail_brush"));
            Assert.AreEqual(1.35f, stats.RadiusMultiplier("broom"), 1e-5f);
            Assert.AreEqual(1f, stats.RadiusMultiplier("detail_brush"));
            Object.DestroyImmediate(unlock);
            Object.DestroyImmediate(wide);
        }

        [Test]
        public void Content_NewToolsAndUpgrades_AreConfigured()
        {
            var detail = AssetDatabase.LoadAssetAtPath<ToolDefinition>(RugWashContentBuilder.DetailBrushToolPath);
            var cannon = AssetDatabase.LoadAssetAtPath<ToolDefinition>(RugWashContentBuilder.FoamCannonToolPath);
            var broom = AssetDatabase.LoadAssetAtPath<ToolDefinition>(RugWashContentBuilder.BroomToolPath);
            var rinse = AssetDatabase.LoadAssetAtPath<ToolDefinition>(RugWashContentBuilder.RinseToolPath);
            Assert.IsTrue(detail.requiresUnlock);
            Assert.IsTrue(cannon.requiresUnlock, "v0.1.2 : hors kit de départ (brosse, détergent, seau), à acheter.");
            Assert.AreEqual(ToolResource.Soap, cannon.resource);
            Assert.Greater(cannon.spreadAspect, 1f);
            Assert.Greater(cannon.radius, 0.24f, "Le canon couvre plus large que le détergent.");
            Assert.IsTrue(broom.runoffIsDry);
            Assert.Greater(rinse.runoffDirt, 0f, "Le rinçage fait ruisseler l'eau sale.");
            Assert.AreEqual(new[] { 6, 7, 8 }, new[] { cannon.slot, broom.slot, detail.slot });
            var unlock = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(RugWashPaths.UpgradeDefinitions + "/up_detail_brush.asset");
            Assert.AreEqual(UpgradeEffect.UnlockTool, unlock.effect);
            Assert.AreEqual(detail.id, unlock.targetToolId);
        }

        // ------------------------------------------------------------------ Robot

        static OccupancyGrid Room()
        {
            // 4 m × 3 m, cases de 0,2 m ; un mur vertical en x = 2 m avec un passage en haut.
            var grid = new OccupancyGrid(Vector2.zero, new Vector2(4f, 3f), 0.2f);
            grid.Build(p => p.x > 1.8f && p.x < 2f && p.y < 2.4f);
            return grid;
        }

        [Test]
        public void Pathfinder_GoesAroundWalls_NeverThroughBlockedCells()
        {
            var grid = Room();
            var start = grid.WorldToCell(new Vector2(0.5f, 0.5f));
            var goal = grid.WorldToCell(new Vector2(3.5f, 0.5f));
            var path = GridPathfinder.FindPath(grid, start, goal);
            Assert.IsNotNull(path);
            Assert.AreEqual(start, path[0]);
            Assert.AreEqual(goal, path[path.Count - 1]);
            foreach (var cell in path) Assert.IsFalse(grid.IsBlocked(cell), $"Case bloquée traversée : {cell}");
            Assert.Greater(path.Count, 15, "Le chemin contourne le mur par le passage.");
            for (int i = 1; i < path.Count; i++)
            {
                var step = path[i] - path[i - 1];
                if (step.x != 0 && step.y != 0)
                {
                    Assert.IsFalse(grid.IsBlocked(new Vector2Int(path[i - 1].x + step.x, path[i - 1].y)), "Coin coupé.");
                    Assert.IsFalse(grid.IsBlocked(new Vector2Int(path[i - 1].x, path[i - 1].y + step.y)), "Coin coupé.");
                }
            }

            var simplified = GridPathfinder.Simplify(grid, path);
            Assert.Less(simplified.Count, path.Count);
            for (int i = 1; i < simplified.Count; i++)
                Assert.IsTrue(grid.HasLineOfSight(simplified[i - 1], simplified[i]), "Chaque segment simplifié reste libre.");
        }

        [Test]
        public void Pathfinder_ReturnsNull_WhenUnreachable_AndOutsideIsBlocked()
        {
            var grid = new OccupancyGrid(Vector2.zero, new Vector2(4f, 3f), 0.2f);
            grid.Build(p => p.x > 1.8f && p.x < 2f);
            Assert.IsNull(GridPathfinder.FindPath(grid, new Vector2Int(1, 1), new Vector2Int(18, 1)));
            Assert.IsTrue(grid.IsBlocked(new Vector2Int(-1, 0)), "Hors de la zone autorisée = bloqué.");
            Assert.IsTrue(grid.IsBlocked(grid.WorldToCell(new Vector2(1.9f, 1f))));
            Assert.IsTrue(grid.TryNearestFree(grid.WorldToCell(new Vector2(1.9f, 1f)), 2, out Vector2Int free));
            Assert.IsFalse(grid.IsBlocked(free));
        }

        [Test]
        public void Robot_StartsStops_ReturnsOnLowBattery_ThenResumesWhenCharged()
        {
            var brain = new RobotVacuumBrain { DrainPerSecond = 0.1f, ChargePerSecond = 0.5f };
            var modes = new List<RobotMode>();
            brain.ModeChanged += (_, next) => modes.Add(next);

            Assert.AreEqual(RobotToggleResult.Started, brain.Toggle());
            Assert.AreEqual(RobotMode.Cleaning, brain.Mode);
            brain.Tick(5f, atDock: false);
            Assert.AreEqual(0.5f, brain.Battery01, 1e-5f);
            Assert.AreEqual(RobotToggleResult.Stopped, brain.Toggle());
            Assert.AreEqual(RobotMode.Returning, brain.Mode);
            Assert.IsFalse(brain.ResumeAfterCharge, "Arrêt demandé : pas de reprise automatique.");
            brain.Tick(1f, atDock: true);
            Assert.AreEqual(RobotMode.Docked, brain.Mode);

            brain.Toggle();
            brain.Tick(3.1f, atDock: false);
            Assert.AreEqual(RobotMode.Returning, brain.Mode, "Batterie faible : retour automatique.");
            Assert.IsTrue(brain.ResumeAfterCharge);
            brain.Tick(0.1f, atDock: true);
            Assert.IsTrue(brain.IsCharging);
            brain.Tick(2f, atDock: true);
            Assert.AreEqual(RobotMode.Cleaning, brain.Mode, "Rechargé : il repart seul.");
            Assert.AreEqual(1f, brain.Battery01, 1e-5f);
            CollectionAssert.AreEqual(new[] { RobotMode.Cleaning, RobotMode.Returning, RobotMode.Docked, RobotMode.Cleaning,
                RobotMode.Returning, RobotMode.Docked, RobotMode.Cleaning }, modes);
        }

        [Test]
        public void Robot_LowBatteryStart_WaitsForCharge_AndRestoreIsBounded()
        {
            var brain = new RobotVacuumBrain();
            brain.Restore((int)RobotMode.Docked, 0.1f, false);
            Assert.AreEqual(RobotToggleResult.WillStartWhenCharged, brain.Toggle());
            Assert.AreEqual(RobotMode.Docked, brain.Mode);
            Assert.IsTrue(brain.ResumeAfterCharge);
            Assert.AreEqual(RobotToggleResult.Stopped, brain.Toggle(), "Un second appui annule le départ différé.");
            Assert.IsFalse(brain.ResumeAfterCharge);

            brain.Restore(42, float.NaN, false);
            Assert.AreEqual(RobotMode.Docked, brain.Mode, "Mode inconnu : sur la base.");
            Assert.AreEqual(1f, brain.Battery01);
            brain.Restore((int)RobotMode.Cleaning, 3f, false);
            Assert.AreEqual(1f, brain.Battery01);

            brain.FinishCleaning();
            Assert.AreEqual(RobotMode.Returning, brain.Mode);
            StringAssert.Contains("retour à la base", brain.Describe());
        }
    }
}
