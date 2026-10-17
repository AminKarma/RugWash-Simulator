using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Orders;
using RugWash.Workshop;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 3 : règles pures de l'agrandissement (plusieurs commandes actives, offres conservées) et de
    /// la machine à laver (temps qui n'avance qu'avec le jeu, lavage partiel, usure des tapis fragiles).
    /// </summary>
    public class WorkshopExtensionTests
    {
        static OrderDefinition Order(string id)
        {
            var order = ScriptableObject.CreateInstance<OrderDefinition>();
            order.id = id;
            order.title = id;
            return order;
        }

        [Test]
        public void Book_AcceptsOneOrderPerBay_AndClosesEachOneSeparately()
        {
            var a = Order("a");
            var b = Order("b");
            var c = Order("c");
            var book = new OrderBook(new[] { a, b, c });
            Assert.AreEqual(OrderStartResult.Started, book.Start(a, 0, maxActive: 2));
            Assert.AreEqual(OrderStartResult.AnotherOrderActive, book.Start(b, 0), "Une seule zone : comme avant.");
            Assert.AreEqual(OrderStartResult.Started, book.Start(b, 0, maxActive: 2));
            Assert.AreEqual(OrderStartResult.AnotherOrderActive, book.Start(c, 0, maxActive: 2), "Deux zones occupées.");
            CollectionAssert.AreEqual(new[] { a, b }, book.ActiveOrders);

            Assert.IsTrue(book.Complete(new OrderResult { Order = b, Grade = OrderGrade.A }));
            Assert.AreEqual(OrderState.Completed, book.StateOf(b));
            Assert.AreEqual(OrderState.Active, book.StateOf(a), "L'autre commande reste en cours.");
            Assert.IsFalse(book.Complete(new OrderResult { Order = b, Grade = OrderGrade.A }), "Jamais payée deux fois.");
            Assert.IsFalse(book.Remove(a), "Une commande active n'est jamais retirée.");
            Assert.IsTrue(book.Abandon(a));
            Assert.AreEqual(OrderState.Available, book.StateOf(a));
            Assert.AreEqual(0, book.ActiveOrders.Count);

            // Réplication : exclusive (une zone) ou ajoutée (plusieurs zones), puis libérée commande par commande.
            book.ForceActive(a);
            book.ForceActive(c);
            Assert.AreEqual(2, book.ActiveOrders.Count);
            Assert.IsTrue(book.ClearActive(c));
            Assert.AreEqual(OrderState.Available, book.StateOf(c));
            book.ForceActive(c, exclusive: true);
            CollectionAssert.AreEqual(new[] { c }, book.ActiveOrders);
            Assert.AreEqual(OrderState.Available, book.StateOf(a));
            foreach (var order in new[] { a, b, c }) Object.DestroyImmediate(order);
        }

        [Test]
        public void Missions_KeepEveryActiveMission_AndOfferAsManyOthers()
        {
            var rugs = new List<RugDefinition> { ScriptableObject.CreateInstance<RugDefinition>() };
            rugs[0].id = "test";
            string Signature(MissionKey key) => MissionGenerator.Signature(key, rugs);
            var board = new MissionBoard();
            board.Restore(42, 0, null, null);
            board.RefillKeeping(10, 0, Signature, new string[0]);
            Assert.AreEqual(MissionBoard.OfferCount, board.Offers.Count);
            var first = board.Offers[0].Id;
            Assert.IsFalse(board.RefillKeeping(10, 0, Signature, new[] { first }), "Une mission en cours : trois offres, comme avant.");
            var second = board.Offers[1].Id;
            Assert.IsTrue(board.RefillKeeping(10, 0, Signature, new[] { first, second }), "Deuxième zone occupée : une offre de plus.");
            Assert.AreEqual(MissionBoard.OfferCount + 1, board.Offers.Count);
            Assert.IsTrue(board.Contains(first) && board.Contains(second));
            // Baisse de réputation : les missions en cours restent, même verrouillées.
            board.RefillKeeping(0, 0, Signature, new[] { first, second });
            Assert.IsTrue(board.Contains(first) && board.Contains(second));
            Assert.LessOrEqual(board.Offers.Count, MissionBoard.MaxOffers);
            Object.DestroyImmediate(rugs[0]);
        }

        [Test]
        public void Washer_Time_OnlyAdvancesWithPlayedFrames_AndSavedValuesAreBounded()
        {
            Assert.AreEqual(899.5f, WasherRules.Advance(900f, 0.5f), 1e-4f);
            Assert.AreEqual(899.5f, WasherRules.Advance(900f, 3600f), 1e-4f, "Un gel ou un saut d'horloge compte au plus 0,5 s.");
            Assert.AreEqual(900f, WasherRules.Advance(900f, -10f), 1e-4f, "Jamais de temps négatif.");
            Assert.AreEqual(0f, WasherRules.Advance(0.2f, 0.5f));
            Assert.AreEqual(900f, WasherRules.Sanitize(float.NaN, 900f));
            Assert.AreEqual(900f, WasherRules.Sanitize(1e9f, 900f), "Fichier modifié : borné au cycle.");
            Assert.AreEqual(0f, WasherRules.Sanitize(-5f, 900f));
            Assert.AreEqual("15:00", WasherRules.Format(900f));
            Assert.AreEqual("00:01", WasherRules.Format(0.2f));
            Assert.AreEqual(0.5f, WasherRules.Progress01(450f, 900f), 1e-4f);
        }

        [Test]
        public void MachineWash_RemovesMostDirt_LeavesStubbornStains_AndWearsFragileRugs()
        {
            var table = new DirtTypeTable();
            var grid = new RugCleaningGrid(8, 8, new Vector2(1f, 1f));
            var add = new AddDirtOperation { Type = DirtType.Dust, Amount = 0.8f };
            grid.ApplyAll(ref add);
            var stains = new AddDirtOperation { Type = DirtType.Grease, Amount = 0.6f };
            grid.ApplyAll(ref stains);
            grid.CaptureInitialState();
            var before = grid.GetCell(3, 3);
            var wash = new MachineWashOperation { Table = table, DustRemoved = 0.95f, StainRemoved = 0.7f, MoistureAfter = 0.85f, Wear = 0.06f * 0.85f };
            grid.ApplyAll(ref wash);
            var after = grid.GetCell(3, 3);
            Assert.Less(after.Dust, before.Dust * 0.1f, "Poussière presque entièrement partie.");
            Assert.Greater(after.Stain, 0f, "Les taches tenaces restent à traiter à la main.");
            Assert.Less(after.Stain, before.Stain, "Une partie des taches part quand même.");
            Assert.GreaterOrEqual(after.Moisture, 0.85f - 1e-4f, "Le tapis ressort trempé.");
            Assert.AreEqual(0f, after.Foam);
            Assert.Greater(after.Wear, 0.04f, "Un tapis fragile s'use en machine.");
            Assert.Greater(grid.Cleanliness01, 0.4f);
            Assert.Less(grid.Cleanliness01, 1f, "Jamais un tapis terminé par la seule machine.");
        }
    }
}
