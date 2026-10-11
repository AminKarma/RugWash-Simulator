using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Effects;
using RugWash.Save;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Règles du jalon 1 (V2) : scan de saleté, minuterie, paliers de propreté, réglage du balancement.</summary>
    public class GameFeelRulesTests
    {
        static RugCleaningGrid GridWith(params (int x, RugCell cell)[] cells)
        {
            var grid = new RugCleaningGrid(4, 1, new Vector2(1f, 0.25f));
            foreach (var (x, cell) in cells) grid.SetCell(x, 0, cell);
            grid.RecomputeTotals();
            return grid;
        }

        [Test]
        public void DirtScan_FlagsExactlyTheCellsTheGridDoesNotCountAsClean()
        {
            float t = RugCleaningGrid.CleanCellThreshold;
            var grid = GridWith(
                (0, new RugCell { Dust = t * 0.5f }),                 // propre (sous le seuil)
                (1, new RugCell { Stain = t * 0.6f, Dust = t * 0.6f }), // sale (somme au-dessus du seuil)
                (2, new RugCell { Residue = 0.5f }),                  // propre mais résidus
                (3, new RugCell()));                                   // parfaitement propre

            var result = DirtScan.Analyze(grid);
            Assert.AreEqual(1, result.DirtyCells);
            Assert.AreEqual(1, result.ResidueCells);
            Assert.AreEqual(4, result.TotalCells);
            Assert.IsFalse(DirtScan.IsDirty(grid.GetCell(0, 0)), "Une cellule comptée propre ne doit jamais être signalée sale.");
            Assert.IsTrue(DirtScan.IsDirty(grid.GetCell(1, 0)));
            Assert.AreEqual(grid.ComputeSummary().CleanCellRatio01, 1f - result.DirtyFraction, 1e-5f,
                "Le scan et le résumé de la grille doivent utiliser le même seuil.");
        }

        [Test]
        public void DirtScan_ResidueOnDirtyCell_IsReportedAsDirtOnly()
        {
            var grid = GridWith((0, new RugCell { Loosened = 0.4f, Residue = 0.6f }));
            var result = DirtScan.Analyze(grid);
            Assert.AreEqual(1, result.DirtyCells);
            Assert.AreEqual(0, result.ResidueCells);
        }

        [Test]
        public void DirtScan_Describe_SpotlessAndDirty()
        {
            Assert.AreEqual("Scan : aucune saleté détectée", DirtScan.Describe(new DirtScanResult(0, 0, 100)));
            StringAssert.Contains("3 % de la surface encore sale", DirtScan.Describe(new DirtScanResult(3, 0, 100)));
            StringAssert.Contains("résidus", DirtScan.Describe(new DirtScanResult(0, 5, 100)));
        }

        [Test]
        public void ScanTimer_RunsDuration_ThenCooldown_ThenReady()
        {
            var timer = new ScanTimer(duration: 2f, cooldown: 3f);
            Assert.IsTrue(timer.IsReady);
            Assert.IsTrue(timer.TryStart());
            Assert.IsTrue(timer.IsActive);
            Assert.IsFalse(timer.TryStart(), "Pas de second scan pendant le scan.");
            Assert.AreEqual(1f, timer.Strength(0.5f), 1e-5f);

            Assert.IsFalse(timer.Tick(1.75f));
            Assert.AreEqual(0.5f, timer.Strength(0.5f), 1e-4f, "Fondu sur la dernière demi-seconde.");
            Assert.IsTrue(timer.Tick(0.5f), "Fin du scan signalée.");
            Assert.IsFalse(timer.IsActive);
            Assert.AreEqual(3f, timer.CooldownRemaining, 1e-4f);
            Assert.IsFalse(timer.TryStart(), "Pas de scan pendant la recharge.");
            Assert.AreEqual(0f, timer.Readiness01, 1e-4f);

            timer.Tick(1.5f);
            Assert.AreEqual(0.5f, timer.Readiness01, 1e-4f);
            timer.Tick(1.6f);
            Assert.IsTrue(timer.IsReady);
            Assert.IsTrue(timer.TryStart());
        }

        [Test]
        public void ScanTimer_IgnoresPausedFrames()
        {
            var timer = new ScanTimer(1f, 1f);
            timer.TryStart();
            timer.Tick(0f);
            Assert.AreEqual(1f, timer.ActiveRemaining, 1e-5f);
        }

        [Test]
        public void Milestones_AnnounceEachThresholdOnce_AndSkipAlreadyReachedOnes()
        {
            var milestones = new CleanlinessMilestones();
            milestones.Reset(0.3f);
            Assert.IsFalse(milestones.TryAdvance(0.3f, out _), "25 % était déjà atteint au chargement : pas d'annonce.");
            Assert.IsTrue(milestones.TryAdvance(0.52f, out float reached));
            Assert.AreEqual(0.5f, reached);
            Assert.IsFalse(milestones.TryAdvance(0.6f, out _), "Un palier n'est annoncé qu'une fois.");
            Assert.IsTrue(milestones.TryAdvance(0.95f, out reached), "Plusieurs paliers franchis d'un coup : un seul événement.");
            Assert.AreEqual(0.9f, reached);
            Assert.AreEqual(milestones.Count, milestones.ReachedCount);

            milestones.Reset(0f);
            Assert.IsTrue(milestones.TryAdvance(0.25f, out reached));
            Assert.AreEqual(0.25f, reached);
        }

        [Test]
        public void MotionIntensity_DefaultsToOne_IsClamped_AndOldSavesKeepDefault()
        {
            Assert.AreEqual(1f, new SettingsData().motionIntensity);
            var tooHigh = new SettingsData { motionIntensity = 3f };
            SaveValidator.ClampSettings(tooHigh);
            Assert.AreEqual(1f, tooHigh.motionIntensity);
            var negative = new SettingsData { motionIntensity = -1f };
            SaveValidator.ClampSettings(negative);
            Assert.AreEqual(0f, negative.motionIntensity);

            var old = JsonUtility.FromJson<SettingsData>("{\"masterVolume\":0.5,\"mouseSensitivity\":0.2}");
            Assert.AreEqual(1f, old.motionIntensity, "Une sauvegarde antérieure sans ce champ garde la valeur par défaut.");
            Assert.AreEqual(0.5f, old.masterVolume);
        }
    }
}
