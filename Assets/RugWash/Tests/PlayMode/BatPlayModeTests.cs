using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Pranks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 3 : batte plus réactive — coups dans le vide enchaînables sans recharge, cible cherchée au
    /// moment de l'impact, recharge démarrée seulement par un coup qui touche, cadence bornée par l'autorité.
    /// </summary>
    public class BatPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            var owned = new List<string>(game.Upgrades.OwnedIds) { "up_prank_bat" };
            game.Upgrades.Restore(owned);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null && game.Gadgets != null) game.Gadgets.Unequip();
            if (game != null && game.Pranks != null) game.Pranks.Clock = () => Time.time;
            yield return null;
        }

        sealed class Actor : IPrankActor
        {
            public ulong Id { get; set; } = 7;
            public string Name => "Batteur";
            public Vector3 Eye { get; set; }
            public bool IsDown => false;
            public int Money => 0;
            public bool Owns(PrankGadget gadget) => true;
            public bool TrySpend(int amount) => amount <= 0;
        }

        sealed class Victims : IPrankVictims
        {
            public readonly Dictionary<ulong, Vector3> Feet = new Dictionary<ulong, Vector3>();
            public int Knocks;
            public bool TryGetFeet(ulong clientId, out Vector3 feet) => Feet.TryGetValue(clientId, out feet);
            public bool IsDown(ulong clientId) => false;
            public void CollectPlayers(List<ulong> into) => into.AddRange(Feet.Keys);
            public void Splash(ulong clientId, float intensity, float seconds, float fadeSeconds, string from) { }
            public void Knock(ulong clientId, Vector3 impulse, float seconds, string from, PrankGadget cause) => Knocks++;
        }

        [UnityTest]
        public IEnumerator SwingsInTheVoid_ChainWithoutCooldown_AHitOnTheRugStartsIt()
        {
            var gadgets = game.Gadgets;
            Assert.IsTrue(gadgets.Equip(PrankGadget.Bat));
            // Vers le plafond : rien à frapper.
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, -2.5f), 0f, -80f);
            yield return null;
            int whooshes = gadgets.Effects.SwingsPlayed;
            Assert.IsTrue(gadgets.TryUse(), "Le coup part même dans le vide.");
            Assert.IsFalse(gadgets.TryUse(), "Pas deux coups dans le même mouvement.");
            yield return new WaitForSeconds(game.Pranks.Tuning.batSwingInterval + 0.1f);
            Assert.AreEqual(1, gadgets.MissedSwings, "Coup dans le vide résolu à l'impact.");
            Assert.AreEqual(0f, gadgets.CooldownRemaining(PrankGadget.Bat), 1e-4f, "Aucune recharge pour un coup dans le vide.");
            Assert.AreEqual(whooshes + 1, gadgets.Effects.SwingsPlayed, "Sifflement joué même sans cible.");
            Assert.IsTrue(gadgets.TryUse(), "Les coups dans le vide s'enchaînent.");
            yield return new WaitForSeconds(game.Pranks.Tuning.batSwingInterval + 0.1f);
            Assert.AreEqual(2, gadgets.MissedSwings);

            // Sur le tapis de la commande : la cible est trouvée à l'impact, puis la recharge démarre.
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            Vector3 center = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            game.PlayerController.Teleport(new Vector3(center.x, 0.06f, center.z - 1.1f), 0f, Mathf.Atan2(1.68f - center.y, 1.1f) * Mathf.Rad2Deg);
            yield return null;
            yield return null;
            float dirt = game.Rug.Grid.TotalDirt;
            Assert.IsTrue(gadgets.TryUse());
            Assert.IsTrue(gadgets.SwingPending, "Impact pas encore résolu au départ du mouvement.");
            yield return new WaitForSeconds(game.Pranks.Tuning.batSwingInterval + 0.1f);
            Assert.AreEqual(PrankResult.Done, gadgets.LastResult);
            Assert.Greater(game.Rug.Grid.TotalDirt, dirt, "Tache de faux sang sur le tapis.");
            Assert.Greater(gadgets.CooldownRemaining(PrankGadget.Bat), 1f, "Recharge démarrée par le coup qui touche.");

            // Pendant la recharge, le geste part encore mais le coup est sans effet.
            float after = game.Rug.Grid.TotalDirt;
            Assert.IsTrue(gadgets.TryUse(), "Le mouvement reste possible.");
            yield return new WaitForSeconds(game.Pranks.Tuning.batSwingInterval + 0.1f);
            Assert.AreEqual(PrankResult.Cooldown, gadgets.LastResult);
            Assert.AreEqual(after, game.Rug.Grid.TotalDirt, 1e-4f, "Coup sans effet pendant la recharge.");
        }

        [UnityTest]
        public IEnumerator Authority_RefusesASecondHit_EvenForAModifiedClient()
        {
            var service = game.Pranks;
            float now = 100f;
            service.Clock = () => now;
            var victims = new Victims();
            victims.Feet[9] = new Vector3(0f, 0f, 1f);
            service.Victims = victims;
            var actor = new Actor { Eye = new Vector3(0f, 1.6f, 0f) };
            var request = new PrankRequest { Gadget = PrankGadget.Bat, Target = PrankTarget.Player, Victim = 42 };
            Assert.AreEqual(PrankResult.NoTarget, service.Execute(actor, request, out _), "Joueur inconnu.");
            now += 0.05f;
            request.Victim = 9;
            Assert.AreEqual(PrankResult.Done, service.Execute(actor, request, out _), "Un coup dans le vide ne déclenche aucune recharge.");
            Assert.AreEqual(1, victims.Knocks);
            // Client modifié qui renvoie un coup aussitôt (sa recharge locale ignorée) : l'autorité refuse.
            now += service.Tuning.batSwingInterval;
            Assert.AreEqual(PrankResult.Cooldown, service.Execute(actor, request, out _), "Deuxième coup pendant la recharge : refusé.");
            Assert.AreEqual(1, victims.Knocks);
            now += service.Tuning.Get(PrankGadget.Bat).cooldownSeconds + 0.1f;
            Assert.AreEqual(PrankResult.Done, service.Execute(actor, request, out _), "Recharge terminée : accepté.");
            Assert.AreEqual(2, victims.Knocks);
            service.Victims = null;
            yield return null;
        }
    }
}
