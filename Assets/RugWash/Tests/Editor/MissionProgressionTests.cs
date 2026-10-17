using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 4 : progression des missions (petits tapis peu sales d'abord, grands et presque noirs
    /// ensuite) et difficulté réglable, sans casser la reproduction à l'identique des missions sauvegardées.
    /// </summary>
    public class MissionProgressionTests
    {
        readonly List<RugDefinition> rugs = new List<RugDefinition>();
        RugDefinition shag, classic, oriental, silk, hall;

        RugDefinition Rug(string id, Vector2 size, RugPileType pile, float fragility = 0f)
        {
            var rug = ScriptableObject.CreateInstance<RugDefinition>();
            rug.id = id;
            rug.sizeMeters = size;
            rug.pileType = pile;
            rug.fragility = fragility;
            rugs.Add(rug);
            return rug;
        }

        [SetUp]
        public void CreateRugs()
        {
            rugs.Clear();
            classic = Rug("classic", new Vector2(2.4f, 1.6f), RugPileType.ShortPile);
            oriental = Rug("oriental", new Vector2(3f, 2f), RugPileType.Oriental);
            shag = Rug("shag", new Vector2(2.1f, 1.4f), RugPileType.LongPile);
            silk = Rug("luxury", new Vector2(3.2f, 2.2f), RugPileType.Silk, 0.85f);
            hall = Rug("hall", new Vector2(3.8f, 2.6f), RugPileType.ShortPile);
        }

        [TearDown]
        public void DestroyRugs()
        {
            foreach (var rug in rugs) Object.DestroyImmediate(rug);
        }

        static float Dirt(OrderDefinition order)
        {
            float total = 0f;
            foreach (var layer in order.dirt) total += layer.coverage * layer.intensity;
            return total;
        }

        [Test]
        public void Key_CarriesTheDifficulty_AndOldIdentifiersStayReadable()
        {
            var normal = new MissionKey(7, 3, 2, 5, 1);
            Assert.AreEqual("gen.7.3.2.5.1", normal.Id, "Difficulté normale : identifiant d'avant (sauvegardes existantes).");
            Assert.IsTrue(MissionKey.TryParse(normal.Id, out var back));
            Assert.AreEqual((int)MissionDifficulty.Normal, back.Difficulty);
            var demanding = new MissionKey(7, 3, 2, 5, 1, (int)MissionDifficulty.Demanding);
            Assert.AreEqual("gen.7.3.2.5.1.2", demanding.Id);
            Assert.IsTrue(MissionKey.TryParse(demanding.Id, out back));
            Assert.AreEqual(demanding, back);
            Assert.IsFalse(MissionKey.TryParse("gen.7.3.2.5.1.1", out _), "Un seul identifiant par mission.");
            Assert.IsFalse(MissionKey.TryParse("gen.7.3.2.5.1.9", out _));
        }

        [Test]
        public void FirstMissions_UseTheSmallestRugs_BiggerOnesComeWithTiers()
        {
            Assert.AreEqual(0, MissionGenerator.RugMinTier(shag, rugs));
            Assert.AreEqual(0, MissionGenerator.RugMinTier(classic, rugs));
            Assert.AreEqual(1, MissionGenerator.RugMinTier(oriental, rugs));
            Assert.AreEqual(3, MissionGenerator.RugMinTier(silk, rugs), "Soie : clients confirmés.");
            Assert.AreEqual(4, MissionGenerator.RugMinTier(hall, rugs), "Le plus grand tapis arrive plus tard.");
            for (int sequence = 0; sequence < 60; sequence++)
            {
                var order = MissionGenerator.Create(new MissionKey(11, sequence, 0, 0, 0), rugs);
                Assert.IsTrue(order.rug == shag || order.rug == classic, $"Palier 0 : petit tapis ({order.rug.id}).");
                Object.DestroyImmediate(order);
            }
        }

        [Test]
        public void Dirt_GrowsWithTiers_UpToNearlyBlackRugs()
        {
            float early = 0f, late = 0f;
            bool grime = false;
            for (int sequence = 0; sequence < 80; sequence++)
            {
                var first = MissionGenerator.Create(new MissionKey(21, sequence, 0, 0, 0), rugs);
                var advanced = MissionGenerator.Create(new MissionKey(21, sequence, 8, (int)MissionCapabilities.All, 0), rugs);
                early += Dirt(first);
                late += Dirt(advanced);
                foreach (var layer in advanced.dirt)
                    if (layer.type == DirtType.Grime && layer.coverage >= 0.75f && layer.intensity >= 0.9f) grime = true;
                Assert.Greater(advanced.baseReward, first.baseReward * 0.9f);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(advanced);
            }
            Assert.Greater(late, early * 1.4f, "Les missions avancées sont nettement plus sales.");
            Assert.IsTrue(grime, "Des tapis couverts de crasse noire aux paliers élevés.");
        }

        [Test]
        public void Difficulty_ChangesDirt_Target_Time_AndPay()
        {
            for (int sequence = 0; sequence < 20; sequence++)
            {
                var relaxed = MissionGenerator.Create(new MissionKey(5, sequence, 4, (int)MissionCapabilities.All, 0, (int)MissionDifficulty.Relaxed), rugs);
                var normal = MissionGenerator.Create(new MissionKey(5, sequence, 4, (int)MissionCapabilities.All, 0), rugs);
                var demanding = MissionGenerator.Create(new MissionKey(5, sequence, 4, (int)MissionCapabilities.All, 0, (int)MissionDifficulty.Demanding), rugs);
                Assert.AreSame(normal.rug, MissionGenerator.Create(new MissionKey(5, sequence, 4, (int)MissionCapabilities.All, 0), rugs).rug, "Reproductible.");
                Assert.Less(Dirt(relaxed) / relaxed.dirt.Count, Dirt(demanding) / demanding.dirt.Count + 1e-4f);
                Assert.LessOrEqual(relaxed.objectives.targetCleanliness, demanding.objectives.targetCleanliness);
                StringAssert.Contains("exigeant", demanding.description);
                Assert.Greater(demanding.objectives.bonusTimeSeconds, 0f);
                foreach (var order in new[] { relaxed, normal, demanding }) Object.DestroyImmediate(order);
            }
            var a = MissionGenerator.Create(new MissionKey(9, 2, 3, 0, 0, (int)MissionDifficulty.Relaxed), rugs);
            var b = MissionGenerator.Create(new MissionKey(9, 2, 3, 0, 0, (int)MissionDifficulty.Demanding), rugs);
            if (a.rug == b.rug) Assert.Less(a.baseReward, b.baseReward, "Exigeant : mieux payé.");
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [Test]
        public void Board_GeneratesNewOffers_WithTheChosenDifficulty()
        {
            var board = new MissionBoard { Difficulty = (int)MissionDifficulty.Demanding };
            board.Restore(3, 0, null, null);
            board.Refill(0, 0, key => MissionGenerator.Signature(key, rugs), null);
            foreach (var offer in board.Offers) Assert.AreEqual((int)MissionDifficulty.Demanding, offer.Difficulty);
            var kept = new List<string>();
            foreach (var offer in board.Offers) kept.Add(offer.Id);
            var restored = new MissionBoard();
            restored.Restore(3, board.Sequence, kept, null);
            for (int i = 0; i < kept.Count; i++) Assert.AreEqual(kept[i], restored.Offers[i].Id, "Les offres sauvegardées gardent leur difficulté.");
        }
    }
}
