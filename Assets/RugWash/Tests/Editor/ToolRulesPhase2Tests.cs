using NUnit.Framework;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Règles de l'aspirateur, du détergent, de l'extracteur et des cuves (phase 2).</summary>
    public class ToolRulesPhase2Tests
    {
        static readonly DirtTypeTable Table = new DirtTypeTable();
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        const float Dt = 1f / 60f;

        static RugCleaningGrid Grid(RugCell fill)
        {
            var grid = new RugCleaningGrid(40, 20, new Vector2(2f, 1f));
            var op = new FillOperation { Cell = fill };
            grid.ApplyAll(ref op);
            grid.CaptureInitialState();
            return grid;
        }

        static RugCell Mid(RugCleaningGrid grid) => grid.GetCell(20, 10);

        static T Run<T>(RugCleaningGrid grid, T op, float seconds, float radius = 0.2f) where T : struct, ICellOperation
        {
            for (float t = 0f; t < seconds; t += Dt) grid.ApplyCircle(Center, radius, 3f, ref op);
            return op;
        }

        // ---------------------------------------------------------------- Aspirateur

        [Test]
        public void Vacuum_RemovesDust()
        {
            var grid = Grid(new RugCell { Dust = 0.6f, DustType = DirtType.Dust });
            var op = Run(grid, new VacuumOperation { Table = Table, Power = 0.8f, DeltaTime = Dt }, 2f);
            Assert.Greater(op.Removed, 0f);
            Assert.Less(Mid(grid).Dust, 0.02f);
            Assert.Greater(grid.Cleanliness01, 0f);
            Assert.Greater(op.CellsCleaned, 0);
        }

        [Test]
        public void Vacuum_DoesNotRemoveEmbeddedStains()
        {
            var grid = Grid(new RugCell { Stain = 0.7f, StainType = DirtType.Embedded });
            var op = Run(grid, new VacuumOperation { Table = Table, Power = 0.8f, DeltaTime = Dt }, 3f);
            Assert.AreEqual(0f, op.Removed, 1e-6f);
            Assert.AreEqual(0.7f, Mid(grid).Stain, 1e-6f);
        }

        [Test]
        public void Vacuum_PicksUpLoosenedDirtOnlyWhenDry()
        {
            var dry = Grid(new RugCell { Loosened = 0.5f, StainType = DirtType.Soil });
            var wet = Grid(new RugCell { Loosened = 0.5f, StainType = DirtType.Soil, Moisture = 0.8f });
            Run(dry, new VacuumOperation { Table = Table, Power = 0.8f, DeltaTime = Dt }, 2f);
            Run(wet, new VacuumOperation { Table = Table, Power = 0.8f, DeltaTime = Dt }, 2f);
            Assert.Less(Mid(dry).Loosened, 0.05f);
            Assert.AreEqual(0.5f, Mid(wet).Loosened, 1e-6f, "Une fibre mouillée retient la saleté détachée.");
        }

        // ---------------------------------------------------------------- Détergent

        [Test]
        public void Soap_AddsFoamAndResidue_WithoutRemovingDirt()
        {
            var grid = Grid(new RugCell { Stain = 0.6f, StainType = DirtType.Grease });
            float dirt = grid.TotalDirt;
            var op = Run(grid, new SoapOperation { FoamPerSecond = 1.4f, WettingPerSecond = 0.15f, DeltaTime = Dt }, 1f);
            var cell = Mid(grid);
            Assert.Greater(op.FoamAdded, 0f);
            Assert.Greater(cell.Foam, 0.9f);
            Assert.Greater(cell.Residue, 0.5f);
            Assert.AreEqual(dirt, grid.TotalDirt, 1e-4f, "Le savon seul ne nettoie pas.");
        }

        [Test]
        public void Soap_MakesBrushEffectiveOnGrease()
        {
            var plain = Grid(new RugCell { Stain = 0.8f, StainType = DirtType.Grease });
            var soapy = Grid(new RugCell { Stain = 0.8f, StainType = DirtType.Grease });
            Run(soapy, new SoapOperation { FoamPerSecond = 1.4f, DeltaTime = Dt }, 1f);
            var plainBrush = Run(plain, new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = Dt }, 1f);
            var soapyBrush = Run(soapy, new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = Dt }, 1f);
            Assert.Greater(soapyBrush.Loosened, plainBrush.Loosened * 8f);
        }

        [Test]
        public void Rinse_RemovesFoamFasterThanResidue()
        {
            var grid = Grid(new RugCell { Foam = 1f, Residue = 0.8f });
            Run(grid, new RinseOperation { Table = Table, Power = 0.9f, WettingPerSecond = 0.9f, DeltaTime = Dt }, 0.8f);
            var cell = Mid(grid);
            Assert.Less(cell.Foam, 0.05f);
            Assert.Greater(cell.Residue, 0.3f, "Un rinçage bref laisse des résidus : il faut insister ou extraire.");
        }

        // ---------------------------------------------------------------- Extracteur

        [Test]
        public void Extractor_ReducesMoisture_AndCarriesResidueAndLoosenedDirt()
        {
            var grid = Grid(new RugCell { Moisture = 1f, Residue = 0.5f, Loosened = 0.3f, StainType = DirtType.Mud });
            var op = Run(grid, new ExtractOperation { Power = 0.9f, DeltaTime = Dt }, 1f);
            var cell = Mid(grid);
            Assert.Less(cell.Moisture, 0.2f);
            Assert.Less(cell.Residue, 0.5f);
            Assert.Less(cell.Loosened, 0.3f);
            Assert.Greater(op.MoistureRemoved, 0f);
            Assert.Greater(op.DirtRemoved, 0f);
        }

        [Test]
        public void Extractor_HasNoEffectOnDryRug()
        {
            var grid = Grid(new RugCell { Residue = 0.5f, Loosened = 0.3f, StainType = DirtType.Mud });
            var op = Run(grid, new ExtractOperation { Power = 0.9f, DeltaTime = Dt }, 2f);
            Assert.AreEqual(0f, op.MoistureRemoved);
            Assert.AreEqual(0.5f, Mid(grid).Residue, 1e-6f);
            Assert.AreEqual(0.3f, Mid(grid).Loosened, 1e-6f);
        }

        [Test]
        public void FullCleaningSequence_ReachesCleanDryState_OnGrease()
        {
            var grid = Grid(new RugCell { Stain = 0.8f, StainType = DirtType.Grease, Dust = 0.3f, DustType = DirtType.Dust });
            Run(grid, new VacuumOperation { Table = Table, Power = 0.8f, DeltaTime = Dt }, 1f);
            Run(grid, new SoapOperation { FoamPerSecond = 1.4f, WettingPerSecond = 0.15f, DeltaTime = Dt }, 1f);
            Run(grid, new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = Dt }, 3f);
            Run(grid, new RinseOperation { Table = Table, Power = 0.9f, WettingPerSecond = 0.9f, DeltaTime = Dt }, 3f);
            Run(grid, new ExtractOperation { Power = 0.9f, DeltaTime = Dt }, 3f);
            var cell = Mid(grid);
            Assert.Less(cell.Dirt, RugCleaningGrid.CleanCellThreshold);
            Assert.Less(cell.Foam, 0.01f);
            Assert.Less(cell.Residue, 0.05f);
            Assert.Less(cell.Moisture, 0.05f);
        }

        // ---------------------------------------------------------------- Cuves

        [Test]
        public void RecoveryTank_AcceptsOnlyUpToCapacity_AndEmpties()
        {
            var tank = new ResourceTank("Cuve", "L", 12f, 0f);
            Assert.AreEqual(10f, tank.Add(10f), 1e-5f);
            Assert.AreEqual(2f, tank.Add(5f), 1e-5f);
            Assert.IsTrue(tank.IsFull);
            Assert.AreEqual(0f, tank.Add(1f));
            tank.Empty();
            Assert.IsTrue(tank.IsEmpty);
        }

        struct FillOperation : ICellOperation
        {
            public RugCell Cell;
            public void Apply(ref RugCell cell, float weight) => cell = Cell;
        }
    }
}
