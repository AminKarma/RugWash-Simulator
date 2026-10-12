using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Economy;
using RugWash.Editor;
using RugWash.Orders;
using RugWash.RugDex;
using RugWash.Save;
using UnityEditor;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Règles du jalon 3 (V2) : nouvelles saletés, adhérence, produits spécialisés, tapis fragile, RugDex.</summary>
    public class ContentRulesTests
    {
        static readonly DirtTypeTable Table = new DirtTypeTable();

        static ProductDefinition Product(string id, float boost, float dwell, float residue, float harshness, params DirtType[] targets)
        {
            var product = ScriptableObject.CreateInstance<ProductDefinition>();
            product.id = id;
            product.displayName = id;
            product.targets = new List<DirtType>(targets);
            product.targetBoost = boost;
            product.dwellBoost = dwell;
            product.residueFactor = residue;
            product.harshness = harshness;
            product.packLiters = 2f;
            product.packPrice = 90;
            product.maxStock = 6f;
            product.litersPerSecond = 0.05f;
            return product;
        }

        // ------------------------------------------------------------------ Nouvelles saletés

        [Test]
        public void NewDirtTypes_AreStoredInTheRightChannel_AndBehaveDifferently()
        {
            Assert.IsTrue(DirtTypes.IsLooseDry(DirtType.DustClumps));
            Assert.IsTrue(DirtTypes.IsLooseDry(DirtType.PetHair));
            Assert.IsTrue(DirtTypes.IsLooseDry(DirtType.Lint));
            Assert.IsFalse(DirtTypes.IsLooseDry(DirtType.DriedMud), "La boue sèche est une croûte fixée.");

            // Aspirateur : peluches > poussière légère > amas de poussière > poils d'animaux.
            float Vacuumed(DirtType type)
            {
                var cell = new RugCell { Dust = 0.8f, DustType = type };
                var vacuum = new VacuumOperation { Table = Table, Power = 0.8f, DeltaTime = 0.25f };
                vacuum.Apply(ref cell, 1f);
                return 0.8f - cell.Dust;
            }
            Assert.Greater(Vacuumed(DirtType.Lint), Vacuumed(DirtType.Dust));
            Assert.Greater(Vacuumed(DirtType.Dust), Vacuumed(DirtType.DustClumps));
            Assert.Greater(Vacuumed(DirtType.DustClumps), Vacuumed(DirtType.PetHair));

            // Poils d'animaux : la brosse les décolle bien mieux que l'aspirateur.
            var hair = new RugCell { Dust = 0.8f, DustType = DirtType.PetHair };
            var brush = new BrushOperation { Table = Table, Power = 0.8f, DeltaTime = 0.25f };
            brush.Apply(ref hair, 1f);
            Assert.Greater(0.8f - hair.Dust, Vacuumed(DirtType.PetHair) * 1.5f);

            // Boue sèche : s'effrite au brossage à sec, presque insensible au jet d'eau ; la boue humide c'est l'inverse.
            float Brushed(DirtType type, float moisture)
            {
                var cell = new RugCell { Stain = 0.8f, StainType = type, Moisture = moisture };
                var b = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 0.25f };
                b.Apply(ref cell, 1f);
                return 0.8f - cell.Stain;
            }
            float Rinsed(DirtType type)
            {
                var cell = new RugCell { Stain = 0.8f, StainType = type };
                var r = new RinseOperation { Table = Table, Power = 0.9f, DeltaTime = 0.25f };
                r.Apply(ref cell, 1f);
                return 0.8f - cell.Stain;
            }
            Assert.Greater(Brushed(DirtType.DriedMud, 0f), Brushed(DirtType.Mud, 0f) * 2f, "La boue sèche s'effrite à sec.");
            Assert.Less(Rinsed(DirtType.DriedMud), Rinsed(DirtType.Mud) * 0.5f, "Le jet d'eau glisse sur la croûte.");
        }

        [Test]
        public void EveryDirtType_HasAColorAndAName()
        {
            for (int i = 1; i < DirtTypes.Count; i++)
            {
                var type = (DirtType)i;
                Assert.AreNotEqual(DirtTypeNames.Default(DirtType.None), DirtTypeNames.Default(type), $"Nom manquant pour {type}");
                Assert.AreNotEqual(DirtTypeTable.DefaultColor(DirtType.None), DirtTypeTable.DefaultColor(type), $"Couleur manquante pour {type}");
            }
        }

        [Test]
        public void DryDirt_HasADistinctLook_OnlyForLooseTypes()
        {
            Assert.AreEqual(0f, DirtTypes.LooseLook(DirtType.Dust), "Poussière : voile fin.");
            Assert.AreEqual(0.5f, DirtTypes.LooseLook(DirtType.DustClumps), "Amas : grains.");
            Assert.AreEqual(0.5f, DirtTypes.LooseLook(DirtType.HairDebris), "Débris : grains.");
            Assert.AreEqual(1f, DirtTypes.LooseLook(DirtType.PetHair), "Poils : brins.");
            Assert.AreEqual(1f, DirtTypes.LooseLook(DirtType.Lint), "Peluches : brins.");
            for (int i = 0; i < DirtTypes.Count; i++)
                if (!DirtTypes.IsLooseDry((DirtType)i))
                    Assert.AreEqual(0f, DirtTypes.LooseLook((DirtType)i), $"{(DirtType)i} n'est pas une salissure sèche.");
        }

        // ------------------------------------------------------------------ Adhérence

        [Test]
        public void Adhesion_FromTypeAndLayer_SlowsBrushingAndRinsing_AndResetsWhenClean()
        {
            var layer = new DirtLayer(DirtType.Drink, DirtPattern.Uniform, 1f, 0.6f, 0.3f, adhesionOffset: 0.3f);
            Assert.AreEqual(DirtTypeTable.DefaultAdhesion(DirtType.Drink) + 0.3f, DirtGenerator.AdhesionOf(layer, null), 1e-5f);
            Assert.AreEqual(1f, DirtGenerator.AdhesionOf(new DirtLayer(DirtType.Embedded, DirtPattern.Uniform, 1f, 1f, 1f, 0.9f), null), 1e-5f, "Bornée à 1.");

            var grid = new RugCleaningGrid(10, 10, new Vector2(1f, 1f));
            DirtGenerator.Generate(grid, new[] { layer }, 3);
            var generated = grid.GetCell(5, 5);
            Assert.Greater(generated.Stain, 0f);
            Assert.AreEqual(0.45f, generated.Adhesion, 0.02f, "Chaque cellule porte l'adhérence de sa couche.");

            var loose = new RugCell { Stain = 0.6f, StainType = DirtType.Drink };
            var stuck = new RugCell { Stain = 0.6f, StainType = DirtType.Drink, Adhesion = 0.8f };
            var brush = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 0.5f };
            brush.Apply(ref loose, 1f);
            brush.Apply(ref stuck, 1f);
            Assert.Less(0.6f - stuck.Stain, (0.6f - loose.Stain) * 0.5f, "Une tache incrustée résiste au brossage.");

            var cleaned = new RugCleaningGrid(1, 1, Vector2.one);
            cleaned.SetCell(0, 0, new RugCell { Stain = 0.01f, StainType = DirtType.Drink, Adhesion = 0.7f, Moisture = 1f });
            var rinse = new RinseOperation { Table = Table, Power = 10f, DeltaTime = 1f };
            cleaned.ApplyAll(ref rinse);
            var brushHard = new BrushOperation { Table = Table, Power = 10f, DeltaTime = 1f };
            cleaned.ApplyAll(ref brushHard);
            cleaned.ApplyAll(ref rinse);
            Assert.AreEqual(0f, cleaned.GetCell(0, 0).Adhesion, "Plus de tache, plus d'adhérence.");
        }

        [Test]
        public void Library_CanOverrideAdhesionAndProductSensitivity()
        {
            var table = new DirtTypeTable();
            table.SetBehaviour(DirtType.Grease, 0.9f, 0.5f);
            Assert.AreEqual(0.9f, table.GetAdhesion(DirtType.Grease));
            Assert.AreEqual(0.5f, table.GetProductSensitivity(DirtType.Grease));
            Assert.AreEqual(1f, DirtGenerator.AdhesionOf(new DirtLayer(DirtType.Grease, DirtPattern.Spots, 1f, 1f, 1f, 0.5f), table));
        }

        // ------------------------------------------------------------------ Produits

        [Test]
        public void ProductTable_BoostsOnlyTargets_ScaledBySensitivity()
        {
            var degreaser = Product("deg", 2.5f, 3f, 1.3f, 0.8f, DirtType.Grease, DirtType.Embedded);
            var dirt = new DirtTypeTable();
            dirt.SetBehaviour(DirtType.Embedded, 0.5f, 0.5f);
            var table = new ProductTable(new[] { degreaser }, dirt);
            byte id = table.IdOf(degreaser);
            Assert.AreEqual(1, id);
            Assert.AreEqual(2.5f, table.TargetBoost(id, DirtType.Grease), 1e-5f);
            Assert.AreEqual(1.75f, table.TargetBoost(id, DirtType.Embedded), 1e-5f, "Sensibilité 0,5 : la moitié du bonus.");
            Assert.AreEqual(1f, table.TargetBoost(id, DirtType.Drink), "Pas de bonus hors cible.");
            Assert.AreEqual(3f, table.DwellBoost(id, DirtType.Grease), 1e-5f);
            Assert.AreEqual(1.3f, table.ResidueFactor(id));
            Assert.AreEqual(0.8f, table.Harshness(id));
            Assert.AreEqual(1f, table.TargetBoost(ProductTable.Standard, DirtType.Grease), "Détergent standard : aucun bonus.");
            Assert.AreEqual(0f, table.Harshness(ProductTable.Standard));
            Object.DestroyImmediate(degreaser);
        }

        [Test]
        public void Products_ReallyChangeBrushingDwellAndWear()
        {
            var degreaser = Product("deg", 2.5f, 3f, 1.3f, 0.8f, DirtType.Grease);
            var products = new ProductTable(new[] { degreaser }, Table);

            var standard = new RugCell { Stain = 0.8f, StainType = DirtType.Grease, Foam = 1f, Moisture = 0.3f };
            var withProduct = standard;
            withProduct.Product = 1;
            var brush = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 0.25f, Products = products };
            brush.Apply(ref standard, 1f);
            brush.Apply(ref withProduct, 1f);
            Assert.AreEqual(2.5f, (0.8f - withProduct.Stain) / (0.8f - standard.Stain), 0.01f, "Brossage sur mousse ×2,5 sur la graisse.");

            var restA = new RugCell { Stain = 0.8f, StainType = DirtType.Grease, Foam = 1f };
            var restB = restA;
            restB.Product = 1;
            var aging = new SurfaceAgingOperation { Table = Table, DeltaTime = 5f, FoamDwellPower = 0.012f, Products = products };
            aging.Apply(ref restA, 1f);
            aging.Apply(ref restB, 1f);
            Assert.AreEqual(3f, (0.8f - restB.Stain) / (0.8f - restA.Stain), 0.01f, "Action au repos ×3.");
            Assert.AreEqual(0f, restB.Wear, "Tapis robuste : aucune usure.");

            var silk = new RugCell { Stain = 0.8f, StainType = DirtType.Grease, Foam = 1f, Product = 1 };
            var fragile = new SurfaceAgingOperation { Table = Table, DeltaTime = 5f, Products = products, Fragility = 0.85f };
            fragile.Apply(ref silk, 1f);
            Assert.Greater(silk.Wear, 0f, "Produit agressif laissé sur de la soie : les fibres s'usent.");
            Object.DestroyImmediate(degreaser);
        }

        [Test]
        public void Soap_RecordsTheProduct_WhenItsFoamDominates()
        {
            var cell = new RugCell();
            var soap = new SoapOperation { FoamPerSecond = 1f, DeltaTime = 0.5f, Product = 2 };
            soap.Apply(ref cell, 1f);
            Assert.AreEqual(2, cell.Product);
            var tiny = new SoapOperation { FoamPerSecond = 0.1f, DeltaTime = 0.1f, Product = 3 };
            tiny.Apply(ref cell, 1f);
            Assert.AreEqual(2, cell.Product, "Un peu de mousse d'un autre produit ne remplace pas le produit en place.");
        }

        [Test]
        public void ProductDescription_IsGeneratedFromRealValues()
        {
            var gentle = Product("silk", 1.3f, 1.2f, 0.5f, 0f, DirtType.Dust);
            var harsh = Product("deg", 2.5f, 3f, 1.3f, 0.8f, DirtType.Grease);
            string g = ProductTable.Describe(gentle), h = ProductTable.Describe(harsh);
            StringAssert.Contains("poussière légère", g);
            StringAssert.Contains("×1,3", g.Replace('.', ','));
            StringAssert.DoesNotContain("abîme", g, "Aucune restriction annoncée pour un produit doux.");
            StringAssert.Contains("taches grasses", h);
            StringAssert.Contains("abîme les tapis fragiles", h);
            Object.DestroyImmediate(gentle);
            Object.DestroyImmediate(harsh);
        }

        [Test]
        public void ProductStock_CapsConsumesDepletesAndCycles()
        {
            var a = Product("a", 2f, 2f, 1f, 0f, DirtType.Grease);
            var b = Product("b", 2f, 2f, 1f, 0f, DirtType.Drink);
            var book = new ProductStockBook(new[] { a, b });
            Assert.IsNull(book.Active);
            Assert.AreEqual(2f, book.Add(a, 2f));
            Assert.AreEqual(4f, book.Add(a, 5f), 1e-5f, "Borné au stock maximal (6 L).");
            Assert.AreEqual(a, book.SelectNext(), "Le premier produit en stock est sélectionné.");
            Assert.AreEqual(a, book.Active);
            Assert.IsNull(book.SelectNext(), "b n'est pas en stock : retour au détergent standard.");
            Assert.IsNull(book.Active);
            Assert.IsFalse(book.Select(b), "Pas de sélection d'un produit absent du stock.");

            book.Select(a);
            ProductDefinition depleted = null;
            book.Depleted += p => depleted = p;
            Assert.AreEqual(5.9f, book.Consume(5.9f), 1e-4f);
            Assert.AreEqual(0.1f, book.Consume(1f), 1e-4f, "On ne prélève que ce qui reste.");
            Assert.AreEqual(a, depleted);
            Assert.IsNull(book.Active, "Épuisé : retour au détergent standard.");
            Assert.AreEqual(0f, book.Consume(1f));
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [Test]
        public void ProductStock_SnapshotRestore_IgnoresUnknownAndClamps()
        {
            var a = Product("a", 2f, 2f, 1f, 0f, DirtType.Grease);
            var book = new ProductStockBook(new[] { a });
            book.Restore(new[] { new KeyValuePair<string, float>("a", 99f), new KeyValuePair<string, float>("ghost", 3f) }, "a");
            Assert.AreEqual(6f, book.StockOf(a), 1e-5f);
            Assert.AreEqual(a, book.Active);
            var snapshot = book.Snapshot();
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual("a", snapshot[0].Key);
            Object.DestroyImmediate(a);
        }

        [Test]
        public void ShopRules_ProductPurchase_NeedsRoomForAWholePackAndFunds()
        {
            var p = Product("a", 2f, 2f, 1f, 0f, DirtType.Grease);
            var rich = new Wallet(500);
            var poor = new Wallet(10);
            Assert.AreEqual(ProductPurchaseResult.Purchased, ShopRules.CheckProduct(p, 6f, rich));
            Assert.AreEqual(ProductPurchaseResult.StockFull, ShopRules.CheckProduct(p, 1.5f, rich));
            Assert.AreEqual(ProductPurchaseResult.InsufficientFunds, ShopRules.CheckProduct(p, 6f, poor));
            Assert.AreEqual(ProductPurchaseResult.Unknown, ShopRules.CheckProduct(null, 6f, rich));
            Object.DestroyImmediate(p);
        }

        // ------------------------------------------------------------------ Tapis fragile

        [Test]
        public void FragileRug_DryBrushingWears_WetOrFoamyDoesNot()
        {
            var dry = new RugCell { Stain = 0.3f, StainType = DirtType.Drink };
            var wet = new RugCell { Stain = 0.3f, StainType = DirtType.Drink, Moisture = 0.5f };
            var foamy = new RugCell { Stain = 0.3f, StainType = DirtType.Drink, Foam = 0.6f };
            var brush = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 1f, FiberWearEnabled = true, WearPerSecond = 0.06f * 0.85f };
            brush.Apply(ref dry, 1f);
            brush.Apply(ref wet, 1f);
            brush.Apply(ref foamy, 1f);
            Assert.Greater(dry.Wear, 0f);
            Assert.AreEqual(0f, wet.Wear);
            Assert.AreEqual(0f, foamy.Wear);

            var robust = new RugCell { Stain = 0.3f, StainType = DirtType.Drink };
            var normal = new BrushOperation { Table = Table, Power = 0.6f, DeltaTime = 1f };
            normal.Apply(ref robust, 1f);
            Assert.AreEqual(0f, robust.Wear, "Tapis robuste : pas d'usure.");
        }

        [Test]
        public void Evaluator_WearObjective_CountsOnlyWhenRequired()
        {
            var order = ScriptableObject.CreateInstance<OrderDefinition>();
            order.baseReward = 100;
            order.objectives = new OrderObjectives { targetCleanliness = 0.8f, limitWear = true, maxWear = 0.04f };
            var gentle = new RugCleaningSummary(1f, 1f, 0f, 0f, 0f, 0.01f, 0f, 1f);
            var damaged = new RugCleaningSummary(1f, 1f, 0f, 0f, 0f, 0.15f, 0f, 1f);
            var ok = OrderEvaluator.Evaluate(order, gentle, 10f);
            var bad = OrderEvaluator.Evaluate(order, damaged, 10f);
            Assert.AreEqual(OrderGrade.S, ok.Grade);
            Assert.Less(bad.Score, ok.Score);
            Assert.AreNotEqual(OrderGrade.S, bad.Grade, "Des fibres abîmées empêchent la note S.");
            Assert.IsTrue(bad.Lines.Exists(l => l.Label == "Fibres abîmées" && !l.Met));

            order.objectives.limitWear = false;
            var free = OrderEvaluator.Evaluate(order, damaged, 10f);
            Assert.IsFalse(free.Lines.Exists(l => l.Label == "Fibres abîmées"), "Critère désactivé : jamais pénalisé.");
            Object.DestroyImmediate(order);
        }

        // ------------------------------------------------------------------ RugDex

        [Test]
        public void RugDex_DiscoversOnce_KeepsBestGrade_AndCountsOnlyCatalog()
        {
            var book = new RugDexBook();
            int events = 0;
            book.Discovered += _ => events++;
            Assert.IsTrue(book.Discover("oriental"));
            Assert.IsFalse(book.Discover("oriental"));
            Assert.AreEqual(1, events);

            book.RecordDelivery("classic", OrderGrade.B);
            book.RecordDelivery("classic", OrderGrade.Refused);
            book.RecordDelivery("classic", OrderGrade.A);
            book.RecordDelivery("classic", OrderGrade.C);
            var record = book.Get("classic");
            Assert.IsTrue(record.discovered, "Livrer un tapis le fait connaître.");
            Assert.AreEqual(3, record.timesDelivered, "Les refus ne comptent pas.");
            Assert.AreEqual("A", record.bestGrade);

            var classic = ScriptableObject.CreateInstance<RugDefinition>();
            classic.id = "classic";
            var luxury = ScriptableObject.CreateInstance<RugDefinition>();
            luxury.id = "luxury";
            book.Discover("hors_catalogue");
            Assert.AreEqual(1, book.DiscoveredCount(new[] { classic, luxury }));
            Assert.AreEqual(50f, RugDexBook.CompletionPercent(1, 2));
            Object.DestroyImmediate(classic);
            Object.DestroyImmediate(luxury);
        }

        [Test]
        public void RugDex_SnapshotRestore_RoundTrips_AndSanitizes()
        {
            var book = new RugDexBook();
            book.RecordDelivery("shag", OrderGrade.S);
            var copy = new RugDexBook();
            copy.Restore(book.Snapshot());
            Assert.AreEqual("S", copy.Get("shag").bestGrade);
            Assert.AreEqual(1, copy.Get("shag").timesDelivered);

            copy.Restore(new[]
            {
                new RugDexRecord { id = " ", discovered = true },
                new RugDexRecord { id = "x", timesDelivered = -4, bestGrade = "Z" },
                null,
            });
            Assert.IsNull(copy.Get(" "));
            Assert.AreEqual(0, copy.Get("x").timesDelivered);
            Assert.AreEqual(string.Empty, copy.Get("x").bestGrade);
        }

        [Test]
        public void OldSave_LoadsWithEmptyProductsAndRugDex()
        {
            const string json = "{\"version\":1,\"profileId\":\"p\",\"money\":5,\"upgrades\":[],\"completedOrders\":[],\"settings\":{}}";
            Assert.AreEqual(SaveMigration.Outcome.UpToDate, SaveMigration.TryParse(json, out SaveData data));
            Assert.IsNotNull(data.products);
            Assert.AreEqual(0, data.products.Count);
            Assert.IsNotNull(data.rugDex);
            Assert.AreEqual(string.Empty, data.activeProduct);
        }

        [Test]
        public void SurfaceState_KeepsAdhesionAndProduct()
        {
            var grid = new RugCleaningGrid(4, 4, Vector2.one);
            grid.SetCell(1, 2, new RugCell { Stain = 0.5f, StainType = DirtType.Drink, Adhesion = 0.6f, Foam = 0.4f, Product = 3 });
            var copy = new RugCleaningGrid(4, 4, Vector2.one);
            Assert.IsTrue(SurfaceStateCodec.TryDecode(SurfaceStateCodec.Encode(grid), copy));
            Assert.AreEqual(0.6f, copy.GetCell(1, 2).Adhesion, 0.003f);
            Assert.AreEqual(3, copy.GetCell(1, 2).Product);
        }

        // ------------------------------------------------------------------ Contenu du projet

        [Test]
        public void Content_LuxuryRug_IsDistinct_AndItsOrderIsTheMostDemanding()
        {
            var luxury = AssetDatabase.LoadAssetAtPath<RugDefinition>(RugWashContentBuilder.LuxuryRugPath);
            var oriental = AssetDatabase.LoadAssetAtPath<RugDefinition>(RugWashContentBuilder.OrientalRugPath);
            Assert.IsNotNull(luxury);
            Assert.AreEqual(RugPileType.Silk, luxury.pileType);
            Assert.Greater(luxury.fragility, 0.5f);
            Assert.Greater(luxury.sheen, 0f);
            Assert.AreNotEqual(oriental.patternTexture, luxury.patternTexture, "Motif propre au tapis de luxe.");
            Assert.Greater(luxury.sizeMeters.x * luxury.sizeMeters.y, oriental.sizeMeters.x * oriental.sizeMeters.y);

            var palace = AssetDatabase.LoadAssetAtPath<OrderDefinition>(RugWashPaths.OrderDefinitions + "/order_palace.asset");
            Assert.AreEqual(luxury, palace.rug);
            Assert.IsTrue(palace.objectives.limitWear);
            Assert.AreEqual(4, palace.difficulty);
            foreach (var guid in AssetDatabase.FindAssets("t:OrderDefinition"))
            {
                var order = AssetDatabase.LoadAssetAtPath<OrderDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (order == palace) continue;
                Assert.Less(order.baseReward, palace.baseReward, "La commande de luxe est la mieux payée.");
                Assert.LessOrEqual(order.requiredReputation, palace.requiredReputation);
            }
        }

        [Test]
        public void Content_NewDirtProductsAndCatalog_AreInPlace()
        {
            var library = AssetDatabase.LoadAssetAtPath<DirtTypeLibrary>(RugWashContentBuilder.DirtLibraryPath);
            var defined = new HashSet<DirtType>();
            foreach (var definition in library.definitions) defined.Add(definition.type);
            foreach (var type in new[] { DirtType.DustClumps, DirtType.DriedMud, DirtType.PetHair, DirtType.Lint })
                Assert.IsTrue(defined.Contains(type), $"{type} absent de la bibliothèque.");

            var products = AssetDatabase.LoadAssetAtPath<ProductLibrary>(RugWashContentBuilder.ProductLibraryPath);
            Assert.GreaterOrEqual(products.products.Count, 3);
            foreach (var product in products.products)
            {
                Assert.Greater(product.targets.Count, 0, $"{product.displayName} doit cibler au moins un type.");
                Assert.Greater(product.packPrice, 0);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<RugCatalog>(RugWashContentBuilder.RugCatalogPath);
            Assert.AreEqual(4, catalog.rugs.Count);
            Assert.Contains(AssetDatabase.LoadAssetAtPath<RugDefinition>(RugWashContentBuilder.LuxuryRugPath), catalog.rugs);
        }
    }
}
