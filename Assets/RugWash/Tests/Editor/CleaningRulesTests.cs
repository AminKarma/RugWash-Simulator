using NUnit.Framework;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Tests unitaires des règles de nettoyage (grille + opérations), sans scène.</summary>
    public class CleaningRulesTests
    {
        static readonly DirtTypeTable Table = new DirtTypeTable();

        static RugCleaningGrid NewGrid(int width = 40, int height = 20) => new RugCleaningGrid(width, height, new Vector2(2f, 1f));

        static RugCleaningGrid GridFilledWith(DirtType type, float amount)
        {
            var grid = NewGrid();
            var op = new AddDirtOperation { Type = type, Amount = amount };
            grid.ApplyAll(ref op);
            grid.CaptureInitialState();
            return grid;
        }

        static float Brush(RugCleaningGrid grid, Vector2 uv, float seconds, float radius = 0.2f, float power = 0.6f)
        {
            float loosened = 0f;
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt)
            {
                var op = new BrushOperation { Table = Table, Power = power, DeltaTime = dt };
                grid.ApplyCircle(uv, radius, 3f, ref op);
                loosened += op.Loosened;
            }
            return loosened;
        }

        static float Rinse(RugCleaningGrid grid, Vector2 uv, float seconds, float radius = 0.2f, float power = 0.9f, float wetting = 0.9f)
        {
            float removed = 0f;
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt)
            {
                var op = new RinseOperation { Table = Table, Power = power, WettingPerSecond = wetting, DeltaTime = dt };
                grid.ApplyCircle(uv, radius, 2f, ref op);
                removed += op.Removed;
            }
            return removed;
        }

        // ---------------------------------------------------------------- A. Nettoyage

        [Test]
        public void GeneratedRug_StartsWithDirt()
        {
            var grid = NewGrid();
            DirtGenerator.Generate(grid, new[]
            {
                new DirtLayer(DirtType.Dust, DirtPattern.Uniform, 0.8f, 0.4f, 0.3f),
                new DirtLayer(DirtType.Mud, DirtPattern.Blotches, 0.3f, 0.8f, 0.3f),
            }, seed: 5);
            Assert.Greater(grid.InitialDirt, 0f);
            Assert.Greater(grid.TotalDirt, 0f);
            Assert.AreEqual(0f, grid.Cleanliness01, 1e-4f, "Un tapis neuf doit afficher 0 % de saleté retirée.");
        }

        [Test]
        public void DirtGeneration_IsDeterministicForSameSeed()
        {
            var layers = new[] { new DirtLayer(DirtType.Drink, DirtPattern.Spots, 0.3f, 0.9f, 0.15f) };
            var a = NewGrid();
            var b = NewGrid();
            DirtGenerator.Generate(a, layers, 42);
            DirtGenerator.Generate(b, layers, 42);
            Assert.AreEqual(a.TotalDirt, b.TotalDirt, 1e-5f);
            for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                Assert.AreEqual(a.GetCell(x, y).Stain, b.GetCell(x, y).Stain, 1e-6f);
        }

        [Test]
        public void Tool_AffectsOnlyCellsInsideRadius()
        {
            var grid = GridFilledWith(DirtType.Mud, 0.8f);
            var center = new Vector2(0.5f, 0.5f);
            const float radius = 0.15f;
            Brush(grid, center, 1f, radius);

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                float px = (x + 0.5f) / grid.Width * grid.SizeMeters.x - center.x * grid.SizeMeters.x;
                float py = (y + 0.5f) / grid.Height * grid.SizeMeters.y - center.y * grid.SizeMeters.y;
                bool inside = Mathf.Sqrt(px * px + py * py) < radius;
                var cell = grid.GetCell(x, y);
                if (!inside) Assert.AreEqual(0.8f, cell.Stain, 1e-6f, $"La cellule ({x},{y}) hors du rayon a été modifiée.");
            }
            Assert.Less(grid.GetCell(20, 10).Stain, 0.8f, "Le centre visé doit être traité.");
        }

        [Test]
        public void Brush_LoosensCompatibleDirt_WithoutRemovingIt()
        {
            var grid = GridFilledWith(DirtType.Mud, 0.8f);
            float before = grid.TotalDirt;
            float loosened = Brush(grid, new Vector2(0.5f, 0.5f), 2f);
            Assert.Greater(loosened, 0f);
            Assert.AreEqual(before, grid.TotalDirt, before * 1e-4f, "Le brossage détache la saleté mais ne l'évacue pas.");
            var cell = grid.GetCell(20, 10);
            Assert.Less(cell.Stain, 0.8f);
            Assert.Greater(cell.Loosened, 0f);
        }

        [Test]
        public void Brush_IsMoreEffectiveOnWetFibers()
        {
            var dry = GridFilledWith(DirtType.Mud, 0.8f);
            var wet = GridFilledWith(DirtType.Mud, 0.8f);
            var wetting = new SetMoistureOperation { Moisture = 1f };
            wet.ApplyAll(ref wetting);
            float dryLoosened = Brush(dry, new Vector2(0.5f, 0.5f), 0.5f);
            float wetLoosened = Brush(wet, new Vector2(0.5f, 0.5f), 0.5f);
            Assert.Greater(wetLoosened, dryLoosened * 2f);
        }

        [Test]
        public void Brush_BarelyAffectsGreaseWithoutDetergent()
        {
            var grease = GridFilledWith(DirtType.Grease, 0.8f);
            var mud = GridFilledWith(DirtType.Mud, 0.8f);
            Assert.Less(Brush(grease, new Vector2(0.5f, 0.5f), 1f), Brush(mud, new Vector2(0.5f, 0.5f), 1f) * 0.3f);
        }

        [Test]
        public void Rinse_RemovesLoosenedDirtAndFoam_AndWetsFibers()
        {
            var grid = NewGrid();
            var setup = new SetCellOperation { Cell = new RugCell { Loosened = 0.6f, Foam = 0.7f, StainType = DirtType.Mud } };
            grid.ApplyAll(ref setup);
            grid.CaptureInitialState();

            float removed = Rinse(grid, new Vector2(0.5f, 0.5f), 2f);
            var cell = grid.GetCell(20, 10);
            Assert.Greater(removed, 0f);
            Assert.Less(cell.Loosened, 0.05f);
            Assert.Less(cell.Foam, 0.05f);
            Assert.Greater(cell.Moisture, 0.5f);
            Assert.Greater(grid.Cleanliness01, 0f);
        }

        [Test]
        public void Rinse_AloneDoesNotRemoveGrease()
        {
            var grid = GridFilledWith(DirtType.Grease, 0.8f);
            Rinse(grid, new Vector2(0.5f, 0.5f), 3f);
            Assert.Greater(grid.GetCell(20, 10).Stain, 0.7f, "La graisse doit être brossée (avec produit) avant rinçage.");
        }

        [Test]
        public void BrushThenRinse_CleansTargetedArea()
        {
            var grid = GridFilledWith(DirtType.Mud, 0.8f);
            var uv = new Vector2(0.5f, 0.5f);
            Rinse(grid, uv, 1f);
            Brush(grid, uv, 4f);
            Rinse(grid, uv, 3f);
            Assert.Less(grid.GetCell(20, 10).Dirt, RugCleaningGrid.CleanCellThreshold);
            Assert.Greater(grid.Cleanliness01, 0f);
        }

        [Test]
        public void CleanArea_GivesNoInfiniteProgress()
        {
            var grid = GridFilledWith(DirtType.Mud, 0.8f);
            var uv = new Vector2(0.5f, 0.5f);
            Rinse(grid, uv, 1f);
            Brush(grid, uv, 6f);
            Rinse(grid, uv, 6f);
            float cleanliness = grid.Cleanliness01;

            // Repasse sur le cœur déjà propre de la zone (rayon réduit : bord de zone exclu).
            float loosened = Brush(grid, uv, 5f, radius: 0.1f);
            float removed = Rinse(grid, uv, 5f, radius: 0.1f);
            Assert.Less(loosened + removed, 1e-3f);
            Assert.AreEqual(cleanliness, grid.Cleanliness01, 1e-4f);
        }

        [Test]
        public void Cleanliness_MatchesUnderlyingData()
        {
            var grid = GridFilledWith(DirtType.Soil, 0.7f);
            Rinse(grid, new Vector2(0.3f, 0.4f), 1f);
            Brush(grid, new Vector2(0.3f, 0.4f), 3f);
            Rinse(grid, new Vector2(0.3f, 0.4f), 2f);
            Brush(grid, new Vector2(0.7f, 0.6f), 2f);

            float incremental = grid.Cleanliness01;
            float sum = 0f;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                sum += grid.GetCell(x, y).Dirt;
            float expected = 1f - sum / grid.InitialDirt;

            Assert.AreEqual(expected, incremental, 1e-3f);
            Assert.AreEqual(expected, grid.ComputeSummary().Cleanliness01, 1e-4f);
            Assert.That(incremental, Is.InRange(0f, 1f));
        }

        [Test]
        public void DirtyRect_CoversOnlyModifiedCells()
        {
            var grid = GridFilledWith(DirtType.Mud, 0.8f);
            grid.TryConsumeDirtyRect(out _);
            Brush(grid, new Vector2(0.25f, 0.5f), 0.1f, radius: 0.1f);
            Assert.IsTrue(grid.TryConsumeDirtyRect(out RectInt rect));
            Assert.Less(rect.width * rect.height, grid.CellCount / 4, "La mise à jour visuelle doit rester localisée.");
            Assert.IsFalse(grid.TryConsumeDirtyRect(out _));
        }

        [Test]
        public void ApplyCircle_OutsideRug_TouchesNothing()
        {
            var grid = GridFilledWith(DirtType.Mud, 0.8f);
            var op = new BrushOperation { Table = Table, Power = 1f, DeltaTime = 1f };
            Assert.AreEqual(0, grid.ApplyCircle(new Vector2(3f, 3f), 0.2f, 3f, ref op));
        }

        // ---------------------------------------------------------------- Ressources

        [Test]
        public void ResourceTank_NeverProvidesMoreThanItContains()
        {
            var tank = new ResourceTank("Eau", "L", 10f, 3f);
            Assert.AreEqual(3f, tank.Consume(5f), 1e-5f);
            Assert.IsTrue(tank.IsEmpty);
            Assert.AreEqual(0f, tank.Consume(1f));
            tank.Refill();
            Assert.AreEqual(10f, tank.Amount, 1e-5f);
        }

        [Test]
        public void ResourceTank_CapacityChangeClampsAmount()
        {
            var tank = new ResourceTank("Eau", "L", 40f, 40f);
            tank.SetCapacity(25f);
            Assert.AreEqual(25f, tank.Amount, 1e-5f);
            Assert.AreEqual(1f, tank.Fraction01, 1e-5f);
        }

        // ---------------------------------------------------------------- Opérations de test

        struct SetMoistureOperation : ICellOperation
        {
            public float Moisture;
            public void Apply(ref RugCell cell, float weight) => cell.Moisture = Moisture;
        }

        struct SetCellOperation : ICellOperation
        {
            public RugCell Cell;
            public void Apply(ref RugCell cell, float weight) => cell = Cell;
        }
    }
}
