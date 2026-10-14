using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 3 (règles pures) : missions générées sans fin et déterministes, paliers de réputation, offres
    /// sans mission impossible ni doublon immédiat, annonces locales du menu, migration des sauvegardes v1 → v2 (kit de
    /// départ, outils conservés), détergent en bidons.
    /// </summary>
    public class EconomyV012Tests
    {
        readonly List<Object> created = new List<Object>();
        List<RugDefinition> rugs;

        [SetUp]
        public void CreateRugs()
        {
            rugs = new List<RugDefinition>
            {
                Rug("classic", RugPileType.ShortPile, 0f),
                Rug("oriental", RugPileType.Oriental, 0f),
                Rug("shag", RugPileType.LongPile, 0f),
                Rug("luxury", RugPileType.Silk, 0.85f),
            };
        }

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var asset in created)
                if (asset != null) Object.DestroyImmediate(asset);
            created.Clear();
        }

        RugDefinition Rug(string id, RugPileType pile, float fragility)
        {
            var rug = ScriptableObject.CreateInstance<RugDefinition>();
            rug.id = id;
            rug.displayName = id;
            rug.pileType = pile;
            rug.fragility = fragility;
            created.Add(rug);
            return rug;
        }

        OrderDefinition Create(MissionKey key)
        {
            var order = MissionGenerator.Create(key, rugs);
            if (order != null) created.Add(order);
            return order;
        }

        static string Describe(OrderDefinition order)
        {
            var text = $"{order.id}|{order.clientName}|{order.title}|{order.rug.id}|{order.dirtSeed}|{order.difficulty}|{order.baseReward}|" +
                       $"{order.requiredReputation}|{JsonUtility.ToJson(order.objectives)}|{order.description}";
            foreach (var layer in order.dirt) text += "|" + JsonUtility.ToJson(layer);
            return text;
        }

        // ------------------------------------------------------------------ Paliers et clés

        [Test]
        public void Tiers_GrowGradually_AndMatchTheirThreshold()
        {
            Assert.AreEqual(0, MissionRules.Threshold(0));
            int previousGap = 0;
            for (int tier = 1; tier <= 12; tier++)
            {
                int gap = MissionRules.Threshold(tier) - MissionRules.Threshold(tier - 1);
                Assert.Greater(gap, previousGap, "Chaque palier demande un peu plus de réputation que le précédent.");
                Assert.LessOrEqual(gap, 2 * tier + 1, "Progression graduelle : quelques missions réussies par palier.");
                previousGap = gap;
                Assert.AreEqual(tier, MissionRules.TierFor(MissionRules.Threshold(tier)));
                Assert.AreEqual(tier - 1, MissionRules.TierFor(MissionRules.Threshold(tier) - 1));
            }
            Assert.AreEqual(MissionRules.MaxTier, MissionRules.TierFor(int.MaxValue), "Palier maximal borné.");
        }

        [Test]
        public void MissionKey_RoundTrips_AndRejectsInvalidIds()
        {
            var key = new MissionKey(-123456, 42, 5, (int)MissionCapabilities.Extractor, 3);
            Assert.IsTrue(MissionKey.TryParse(key.Id, out var parsed));
            Assert.AreEqual(key, parsed);
            Assert.IsTrue(MissionKey.IsGenerated(key.Id));
            Assert.IsFalse(MissionKey.IsGenerated("order_salon"));
            foreach (string bad in new[]
                     {
                         null, "", "order_salon", "gen.", "gen.1.2.3.4", "gen.1.2.3.4.5.6", "gen.a.2.3.4.5", "gen.1.-2.3.4.5",
                         "gen.1.2.99.4.5", "gen.1.2.3.64.5", "gen.1.2.3.4.8", "gen.1.2.3.4.-1", "gen." + new string('1', 80),
                     })
                Assert.IsFalse(MissionKey.TryParse(bad, out _), $"Identifiant refusé : « {bad} »");
        }

        [Test]
        public void Capabilities_ComeFromUnlockedTools()
        {
            Assert.AreEqual(0, MissionRules.CapabilitiesFrom(null));
            Assert.AreEqual(0, MissionRules.CapabilitiesFrom(_ => false));
            Assert.AreEqual((int)MissionCapabilities.All, MissionRules.CapabilitiesFrom(_ => true));
            Assert.AreEqual((int)MissionCapabilities.Extractor, MissionRules.CapabilitiesFrom(id => id == "extractor"));
        }

        // ------------------------------------------------------------------ Génération

        [Test]
        public void Generation_IsDeterministic_ForTheSameKey()
        {
            for (int sequence = 0; sequence < 25; sequence++)
            {
                var key = new MissionKey(777, sequence, sequence % 7, sequence % 2 == 0 ? (int)MissionCapabilities.All : 0, sequence % 3);
                var first = Create(key);
                var second = Create(key);
                Assert.IsNotNull(first);
                Assert.AreEqual(Describe(first), Describe(second), "Même clé → même mission (redémarrage, invités).");
                Assert.AreEqual(key.Id, first.id);
                Assert.AreEqual(MissionRules.Threshold(key.Tier), first.requiredReputation);
            }
            var seeds = new HashSet<int>();
            for (int sequence = 0; sequence < 5; sequence++)
            {
                seeds.Add(Create(new MissionKey(777, sequence, 2, 0, 0)).dirtSeed);
                seeds.Add(Create(new MissionKey(778, sequence, 2, 0, 0)).dirtSeed);
            }
            Assert.Greater(seeds.Count, 5, "Une autre graine (autre partie) ou un autre numéro donne d'autres salissures.");
        }

        [Test]
        public void Generation_NeverAsksTheImpossible()
        {
            bool moistureWithExtractor = false;
            for (int tier = 0; tier <= 12; tier++)
            for (int sequence = 0; sequence < 40; sequence++)
            {
                var withoutExtractor = Create(new MissionKey(99, sequence, tier, (int)(MissionCapabilities.All & ~MissionCapabilities.Extractor), 0));
                Assert.IsFalse(withoutExtractor.objectives.limitMoisture, "Pas d'objectif de séchage sans extracteur.");
                Assert.IsNotNull(withoutExtractor.rug);
                Assert.That(withoutExtractor.dirt.Count, Is.InRange(2, 4));
                Assert.That(withoutExtractor.objectives.targetCleanliness, Is.InRange(0.7f, 0.95f));
                Assert.That(withoutExtractor.difficulty, Is.InRange(1, 4));
                Assert.Greater(withoutExtractor.baseReward, 0);
                if (tier < 3) Assert.AreNotEqual("luxury", withoutExtractor.rug.id, "Tapis de soie réservé aux clients confirmés.");
                Assert.AreEqual(withoutExtractor.rug.fragility > 0f, withoutExtractor.objectives.limitWear, "Limite d'usure seulement sur tapis fragile.");
                if (tier == 0) Assert.IsFalse(withoutExtractor.objectives.limitResidue, "Premier palier : objectif de propreté seul.");

                var withExtractor = Create(new MissionKey(99, sequence, tier, (int)MissionCapabilities.All, 0));
                moistureWithExtractor |= withExtractor.objectives.limitMoisture;
            }
            Assert.IsTrue(moistureWithExtractor, "Avec l'extracteur, des clients demandent un tapis sec.");
        }

        [Test]
        public void Generation_RewardsAndDemandsGrowWithTier()
        {
            float Average(int tier, System.Func<OrderDefinition, float> value)
            {
                float sum = 0f;
                for (int sequence = 0; sequence < 60; sequence++) sum += value(Create(new MissionKey(5, sequence, tier, (int)MissionCapabilities.All, 0)));
                return sum / 60f;
            }
            Assert.Greater(Average(4, o => o.baseReward), Average(0, o => o.baseReward) * 1.5f);
            Assert.Greater(Average(8, o => o.baseReward), Average(4, o => o.baseReward));
            Assert.Greater(Average(6, o => o.objectives.targetCleanliness), Average(0, o => o.objectives.targetCleanliness));
            Assert.Greater(Average(6, o => o.difficulty), Average(0, o => o.difficulty));
        }

        [Test]
        public void Generation_WithoutRug_ReturnsNull()
        {
            Assert.IsNull(MissionGenerator.Create(new MissionKey(1, 0, 0, 0, 0), new List<RugDefinition>()));
            Assert.IsNull(MissionGenerator.Signature(new MissionKey(1, 0, 0, 0, 0), null));
        }

        // ------------------------------------------------------------------ Offres

        MissionBoard NewBoard(int seed = 2024)
        {
            var board = new MissionBoard();
            board.Restore(seed, 0, null, null);
            return board;
        }

        string Signature(MissionKey key) => MissionGenerator.Signature(key, rugs);

        static List<string> Ids(MissionBoard board)
        {
            var ids = new List<string>();
            foreach (var offer in board.Offers) ids.Add(offer.Id);
            return ids;
        }

        [Test]
        public void Board_OffersThree_TwoOpen_AndOneToAimFor()
        {
            var board = NewBoard();
            Assert.IsTrue(board.Refill(0, 0, Signature, null));
            Assert.AreEqual(MissionBoard.OfferCount, board.Offers.Count);
            Assert.AreEqual(MissionBoard.MinOpen, board.OpenCount(0));
            int locked = 0;
            foreach (var offer in board.Offers)
                if (MissionRules.Threshold(offer.Tier) > 0) locked++;
            Assert.AreEqual(1, locked, "Une offre du palier suivant montre l'objectif de réputation.");
            Assert.AreEqual(3, board.Sequence, "Rien n'est généré d'avance.");
            Assert.IsFalse(board.Refill(0, 0, Signature, null), "Offres complètes : rien ne change.");
        }

        [Test]
        public void Board_ReplacesDeliveredMission_WithoutImmediateDuplicate()
        {
            var board = NewBoard();
            board.Refill(10, (int)MissionCapabilities.All, Signature, null);
            for (int round = 0; round < 30; round++)
            {
                // Une offre accessible est livrée (réputation stable : l'offre « à viser » reste verrouillée).
                var delivered = board.Offers[0];
                foreach (var offer in board.Offers)
                    if (MissionRules.Threshold(offer.Tier) <= 10) { delivered = offer; break; }
                Assert.IsTrue(board.Retire(delivered.Id));
                Assert.IsFalse(board.Contains(delivered.Id));
                int sequence = board.Sequence;
                board.Refill(10, (int)MissionCapabilities.All, Signature, null);
                Assert.AreEqual(sequence + 1, board.Sequence, "Une livraison → une nouvelle mission.");
                var signatures = new HashSet<string>();
                foreach (var offer in board.Offers)
                    Assert.IsTrue(signatures.Add(Signature(offer)), "Pas deux offres identiques (catégorie et tapis).");
                Assert.IsFalse(signatures.Contains(Signature(delivered)), "La mission livrée n'est pas reproposée aussitôt.");
                Assert.AreEqual(MissionBoard.MinOpen, board.OpenCount(10));
            }

            // Réputation en hausse : l'offre visée s'ouvre et une nouvelle offre du palier suivant apparaît.
            int before = board.Sequence;
            board.Refill(MissionRules.Threshold(MissionRules.TierFor(10) + 1), (int)MissionCapabilities.All, Signature, null);
            Assert.AreEqual(before, board.Sequence, "Trois offres déjà affichées : rien de plus tant qu'aucune n'est livrée.");
            Assert.AreEqual(3, board.OpenCount(MissionRules.Threshold(MissionRules.TierFor(10) + 1)));
        }

        [Test]
        public void Board_RestoresSavedOffers_Identically()
        {
            var board = NewBoard(31337);
            board.Refill(4, 0, Signature, null);
            board.Retire(board.Offers[1].Id);
            board.Refill(6, 0, Signature, null);

            var restored = new MissionBoard();
            restored.Restore(board.Seed, board.Sequence, Ids(board), board.LastCompleted);
            CollectionAssert.AreEqual(Ids(board), Ids(restored), "Les offres reviennent telles quelles après un redémarrage.");
            Assert.IsFalse(restored.Refill(6, 0, Signature, null));
            for (int i = 0; i < board.Offers.Count; i++)
                Assert.AreEqual(Describe(Create(board.Offers[i])), Describe(Create(restored.Offers[i])));

            // Suite identique : la prochaine mission ne dépend que de l'état sauvegardé.
            board.Retire(board.Offers[0].Id);
            restored.Retire(restored.Offers[0].Id);
            board.Refill(8, 0, Signature, null);
            restored.Refill(8, 0, Signature, null);
            CollectionAssert.AreEqual(Ids(board), Ids(restored));
        }

        [Test]
        public void Board_Restore_IgnoresInvalidOrForeignIds()
        {
            var board = new MissionBoard();
            board.Restore(10, 0, new[] { "gen.10.5.0.0.0", "gen.10.5.0.0.0", "gen.11.6.0.0.0", "n'importe quoi", "gen.10.2.1.0.1", "gen.10.9.0.0.0" }, null);
            CollectionAssert.AreEqual(new[] { "gen.10.5.0.0.0", "gen.10.2.1.0.1", "gen.10.9.0.0.0" }, Ids(board));
            Assert.AreEqual(10, board.Sequence, "Le numéro suivant ne réutilise jamais une offre affichée.");

            var fresh = new MissionBoard();
            fresh.Restore(0, -5, null, null);
            Assert.AreNotEqual(0, fresh.Seed, "Graine absente : une nouvelle est tirée.");
            Assert.AreEqual(0, fresh.Sequence);
        }

        [Test]
        public void Board_AfterReputationDrop_KeepsTwoOpenOffers_AndNeverDropsTheActiveOne()
        {
            var board = NewBoard();
            board.Refill(MissionRules.Threshold(5), 0, Signature, null);
            Assert.AreEqual(2, board.OpenCount(MissionRules.Threshold(5)));
            string active = board.Offers[0].Id;
            // Grosse baisse de réputation (livraisons refusées) : les offres du palier 5 deviennent verrouillées.
            Assert.IsTrue(board.Refill(1, 0, Signature, active));
            Assert.IsTrue(board.Contains(active), "La commande en cours n'est jamais retirée.");
            int openOrActive = 0;
            foreach (var offer in board.Offers)
                if (MissionRules.Threshold(offer.Tier) <= 1 || offer.Id == active) openOrActive++;
            Assert.GreaterOrEqual(openOrActive, MissionBoard.MinOpen);
            Assert.AreEqual(MissionBoard.OfferCount, board.Offers.Count);
        }

        // ------------------------------------------------------------------ Actualités

        const string News = "{\"format\":1,\"items\":[" +
                            "{\"id\":\"a\",\"date\":\"2026-01-02\",\"version\":\"0.1.0\",\"title\":\"Ancienne\",\"body\":\"Texte A\"}," +
                            "{\"id\":\"b\",\"date\":\"2026-03-04\",\"version\":\"0.1.2\",\"title\":\"Récente <b>gras</b>\",\"body\":\"Texte B\"}," +
                            "{\"id\":\"c\",\"date\":\"pas une date\",\"title\":\"Sans date\",\"body\":\"\"}," +
                            "{\"id\":\"d\",\"date\":\"2026-02-01\",\"title\":\"   \",\"body\":\"titre vide : ignorée\"}," +
                            "{\"id\":\"e\",\"date\":\"2025-12-31\",\"title\":\"La plus ancienne\",\"body\":\"au-delà de trois\"}]}";

        [Test]
        public void News_ParsesValidItems_NewestFirst_AndSanitizes()
        {
            var items = NewsFeed.Parse(News, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(NewsFeed.MaxItems, items.Count, "Titre vide ignoré, trois annonces au plus.");
            CollectionAssert.AreEqual(new[] { "b", "a", "e" }, items.ConvertAll(i => i.id), "Les plus récentes d'abord.");
            Assert.IsFalse(items[0].title.Contains("<"), "Pas de balises de texte enrichi venant du fichier.");
            Assert.AreEqual("04/03/2026 · version 0.1.2", NewsFeed.Meta(items[0]));
            Assert.AreEqual("31/12/2025", NewsFeed.Meta(items[2]));
            StringAssert.Contains("Récente", NewsPanelUI.Render(items, Color.white));

            var undated = NewsFeed.Parse("{\"format\":1,\"items\":[{\"title\":\"Sans date\",\"date\":\"bientôt\"}]}", out error);
            Assert.IsNull(error);
            Assert.AreEqual(1, undated.Count, "Une date invalide est ignorée, l'annonce reste.");
            Assert.AreEqual(string.Empty, undated[0].date);
            Assert.AreEqual(string.Empty, NewsFeed.Meta(undated[0]));
        }

        [Test]
        public void News_InvalidData_NeverThrows()
        {
            foreach (string bad in new[]
                     {
                         null, "", "   ", "{", "pas du json", "{\"format\":2,\"items\":[{\"title\":\"T\"}]}", "{\"format\":1}", "[1,2,3]",
                         "{\"format\":1,\"items\":[{\"title\":\"  \"}]}",
                     })
            {
                var items = NewsFeed.Parse(bad, out string error);
                Assert.IsNotNull(items);
                Assert.AreEqual(0, items.Count, $"Aucune annonce pour « {bad} »");
                Assert.IsNotNull(error, $"Erreur signalée pour « {bad} »");
            }
            Assert.AreEqual("Aucune actualité pour le moment.", NewsPanelUI.Render(new List<NewsItem>(), Color.white));
            string longBody = new string('x', 2000);
            var trimmed = NewsFeed.Parse("{\"format\":1,\"items\":[{\"title\":\"T\",\"body\":\"" + longBody + "\"}]}", out _);
            Assert.AreEqual(NewsFeed.MaxBodyLength, trimmed[0].body.Length, "Texte trop long tronqué.");
            StringAssert.EndsWith("…", trimmed[0].body);
        }

        [Test]
        public void News_ShippedFile_IsValid()
        {
            var asset = Resources.Load<TextAsset>("RugWashNews");
            Assert.IsNotNull(asset, "Resources/RugWashNews.json présent.");
            var items = NewsFeed.Parse(asset.text, out string error);
            Assert.IsNull(error);
            Assert.GreaterOrEqual(items.Count, 1);
            Resources.UnloadAsset(asset);
        }

        // ------------------------------------------------------------------ Sauvegardes et détergent

        [Test]
        public void Migration_V1WithProgress_KeepsToolsAndGetsACan()
        {
            const string v1 = "{\"version\":1,\"profileId\":\"p\",\"money\":250,\"reputation\":4,\"upgrades\":[\"up_brush\"]," +
                              "\"completedOrders\":[\"order_salon\"],\"ordersDelivered\":3}";
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(v1, out var data));
            Assert.AreEqual(SaveMigration.CurrentVersion, data.version);
            Assert.AreEqual(250, data.money);
            foreach (string id in SaveMigration.LegacyToolUnlocks) CollectionAssert.Contains(data.upgrades, id, "Outil déjà utilisé conservé.");
            CollectionAssert.Contains(data.upgrades, "up_brush");
            Assert.AreEqual(SaveMigration.LegacyDetergentStock, data.detergentStock);
            Assert.AreEqual(-1f, data.soapReserve, "Réserve pleine à la reprise.");
            Assert.AreEqual(0, data.missionSeed, "Missions tirées au premier chargement.");
        }

        [Test]
        public void Migration_V1WithoutProgress_StartsWithTheKit()
        {
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse("{\"version\":1,\"profileId\":\"p\"}", out var data));
            Assert.AreEqual(0, data.upgrades.Count, "Kit de départ seul : brosse, détergent, seau.");
            Assert.AreEqual(SaveMigration.StarterDetergentStock, data.detergentStock);
        }

        [Test]
        public void SaveV2_DropsInvalidMissionData()
        {
            const string v2 = "{\"version\":2,\"profileId\":\"p\",\"completedOrders\":[\"order_salon\",\"gen.1.2.0.0.0\"]," +
                              "\"missionSeed\":1,\"missionSequence\":-4,\"missionOffers\":[\"gen.1.3.0.0.0\",\"faux\"],\"detergentStock\":-3}";
            Assert.AreEqual(SaveMigration.Outcome.Migrated, SaveMigration.TryParse(v2, out var data), "v2 → v3 (jalon 4).");
            CollectionAssert.AreEqual(new[] { "order_salon" }, data.completedOrders, "Les missions générées ne sont pas des contrats terminés.");
            CollectionAssert.AreEqual(new[] { "gen.1.3.0.0.0" }, data.missionOffers);
            Assert.AreEqual(0, data.missionSequence);
            Assert.AreEqual(0f, data.detergentStock);
        }

        [Test]
        public void DetergentCan_RespectsFundsAndStorage()
        {
            Assert.AreEqual(ProductPurchaseResult.Purchased, DetergentRules.CheckCan(0f, 40f, 160f, 45, 45));
            Assert.AreEqual(ProductPurchaseResult.InsufficientFunds, DetergentRules.CheckCan(0f, 40f, 160f, 45, 44));
            Assert.AreEqual(ProductPurchaseResult.StockFull, DetergentRules.CheckCan(130f, 40f, 160f, 45, 1000));

            var career = new PlayerCareer { Money = 100, DetergentStock = 10f };
            Assert.AreEqual(ProductPurchaseResult.Purchased, CareerRules.TryBuyDetergentCan(career, 45, 40f, 160f));
            Assert.AreEqual(55, career.Money);
            Assert.AreEqual(50f, career.DetergentStock, 1e-4f);
            Assert.AreEqual(4f, CareerRules.TakeDetergent(career, 4f), 1e-4f, "Remplissage complet de la réserve.");
            career.DetergentStock = 1.5f;
            Assert.AreEqual(1.5f, CareerRules.TakeDetergent(career, 4f), 1e-4f, "Remplissage partiel : le stock restant.");
            Assert.AreEqual(0f, CareerRules.TakeDetergent(career, 4f), "Plus de stock : rien.");
            Assert.AreEqual(0f, CareerRules.TakeDetergent(career, float.NaN));
            var sanitized = CareerRules.Sanitize(new PlayerCareer { DetergentStock = 9999f }, null, null, 160f);
            Assert.AreEqual(160f, sanitized.DetergentStock, "Stock annoncé borné par l'hôte.");
        }
    }
}
