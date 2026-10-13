using System.Diagnostics;
using NUnit.Framework;
using RugWash.Cleaning;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 2 — performance de la mousse et de l'eau : simulation lente répartie sur les images (même
    /// résultat qu'un passage complet, sans à-coup), réplication lente distincte, budget des effets visuels.
    /// </summary>
    public class PerformanceRulesTests
    {
        static RugCleaningGrid FoamedGrid(int width = 205, int height = 141)
        {
            var grid = new RugCleaningGrid(width, height, new Vector2(3.2f, 2.2f));
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid.SetCell(x, y, new RugCell { Foam = 0.8f, Moisture = 0.3f, Stain = 0.3f, StainType = DirtType.Soil });
            grid.TryConsumeDirtyRect(out _);
            grid.TryConsumeNetworkDirtyRect(out _);
            return grid;
        }

        static SurfaceAgingOperation Aging(float seconds) => new SurfaceAgingOperation
        {
            DeltaTime = seconds,
            FoamDecayPerSecond = 0.02f,
            FoamDecayFloor = 0.004f,
            FoamToMoisture = 0.25f,
            DryingPerSecond = 0.012f,
        };

        [Test]
        public void SpreadAging_GivesTheSameResultAsFullPasses_WithAFewRowsPerFrame()
        {
            var full = FoamedGrid();
            var spread = FoamedGrid();
            for (int i = 0; i < 10; i++)
            {
                var op = Aging(0.2f);
                full.ApplyAllTracked(ref op);
            }

            var scheduler = new SurfaceAgingScheduler();
            int worstRows = 0;
            var template = Aging(0f);
            for (int frame = 0; frame < 132; frame++)
            {
                scheduler.Advance(spread, 1f / 60f, 0.2f, true, ref template, slowReplication: true);
                worstRows = Mathf.Max(worstRows, scheduler.RowsLastAdvance);
            }
            Assert.LessOrEqual(worstRows, Mathf.CeilToInt(141 * (1f / 60f) / 0.2f) + 1, "Quelques lignes par image, jamais toute la grille.");
            Assert.Greater(template.FoamLost, 0f, "Compteurs de l'opération cumulés.");
            Assert.AreEqual(full.TotalFoam, spread.TotalFoam, full.TotalFoam * 0.01f, "Même mousse restante (à une période près).");
            Assert.AreEqual(full.TotalMoisture, spread.TotalMoisture, full.TotalMoisture * 0.02f, "Même humidité.");
        }

        [Test]
        public void SpreadAging_NeverCatchesUpIdleTime()
        {
            var grid = new RugCleaningGrid(40, 30, Vector2.one);
            var scheduler = new SurfaceAgingScheduler();
            var template = Aging(0f);
            for (int i = 0; i < 600; i++) scheduler.Advance(grid, 1f / 60f, 0.2f, false, ref template, true);
            grid.SetCell(3, 3, new RugCell { Foam = 0.8f });
            for (int i = 0; i < 3; i++) scheduler.Advance(grid, 1f / 60f, 0.2f, true, ref template, true);
            Assert.Greater(grid.GetCell(3, 3).Foam, 0.79f, "Dix secondes de repos ne sont pas appliquées d'un coup à la mousse nouvelle.");
        }

        [Test]
        public void SpreadAging_CostPerFrame_IsAFractionOfAFullPass()
        {
            // Échauffement (compilation à la volée des deux chemins), puis mesures.
            var warm = FoamedGrid(20, 10);
            var warmOp = Aging(0.2f);
            warm.ApplyAllTracked(ref warmOp);
            var warmScheduler = new SurfaceAgingScheduler();
            for (int i = 0; i < 3; i++) warmScheduler.Advance(warm, 0.1f, 0.2f, true, ref warmOp, true);

            var full = FoamedGrid();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                var op = Aging(0.2f);
                full.ApplyAllTracked(ref op);
            }
            double fullMs = watch.Elapsed.TotalMilliseconds / 5;

            var spread = FoamedGrid();
            var scheduler = new SurfaceAgingScheduler();
            var template = Aging(0f);
            var frames = new System.Collections.Generic.List<double>();
            for (int frame = 0; frame < 120; frame++)
            {
                watch.Restart();
                scheduler.Advance(spread, 1f / 60f, 0.2f, true, ref template, true);
                frames.Add(watch.Elapsed.TotalMilliseconds);
            }
            frames.Sort();
            double average = 0;
            foreach (double f in frames) average += f;
            average /= frames.Count;
            double p95 = frames[(int)(frames.Count * 0.95f)];
            Debug.Log($"[RugWash] Simulation lente, tapis 205×141 couvert de mousse : passage complet {fullMs:0.00} ms d'un coup toutes les 0,2 s ; " +
                      $"réparti : {average:0.000} ms par image en moyenne, {p95:0.000} ms au 95e centile (60 images/s).");
            Assert.Less(average, fullMs / 4, "Le coût par image est une petite fraction d'un passage complet.");
        }

        [Test]
        public void SlowReplication_IsTrackedApartFromTheFastNetworkRect()
        {
            var grid = FoamedGrid(20, 10);
            var op = Aging(0.2f);
            Assert.Greater(grid.ApplyRowTracked(4, ref op, slowReplication: true), 0);
            Assert.IsFalse(grid.TryConsumeNetworkDirtyRect(out _), "La simulation lente ne part pas avec la réplication rapide.");
            Assert.IsTrue(grid.TryConsumeSlowNetworkRect(out RectInt slow));
            Assert.AreEqual(new RectInt(0, 4, 20, 1), slow);
            Assert.IsTrue(grid.TryConsumeDirtyRect(out _), "Le rendu, lui, est toujours mis à jour.");

            var full = Aging(0.2f);
            grid.ApplyAllTracked(ref full);
            Assert.IsTrue(grid.TryConsumeNetworkDirtyRect(out _), "Sans indication contraire, réplication rapide (comportement inchangé).");
        }

        [Test]
        public void EffectBudget_ScalesRateCountAndLifetime_FromTheOriginalValues()
        {
            var go = new GameObject("Effect");
            try
            {
                var system = go.AddComponent<ParticleSystem>();
                var main = system.main;
                main.maxParticles = 200;
                main.startLifetimeMultiplier = 0.5f;
                go.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.5f;

                var tuning = CleaningTuning.CreateDefault();
                tuning.effectDensity = 0.5f;
                tuning.effectLifetime = 0.8f;
                tuning.maxParticleScreenSize = 0.1f;
                EffectBudget.Apply(tuning);
                EffectBudget.Configure(system);
                EffectBudget.Configure(system);
                Assert.AreEqual(100, system.main.maxParticles, "Densité appliquée une seule fois, depuis l'original.");
                Assert.AreEqual(0.4f, system.main.startLifetimeMultiplier, 1e-5f);
                Assert.AreEqual(0.1f, go.GetComponent<ParticleSystemRenderer>().maxParticleSize, 1e-5f, "Taille à l'écran plafonnée.");
                Assert.AreEqual(35f, EffectBudget.Rate(70f), 1e-4f);

                EffectBudget.Apply(CleaningTuning.CreateDefault());
                EffectBudget.Configure(system);
                Assert.AreEqual(200, system.main.maxParticles, "Retour aux réglages nominaux.");
                Object.DestroyImmediate(tuning);
            }
            finally
            {
                EffectBudget.Apply(CleaningTuning.CreateDefault());
                Object.DestroyImmediate(go);
            }
        }
    }
}
