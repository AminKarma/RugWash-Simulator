using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Network;
using RugWash.Pranks;
using RugWash.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 6, dans la vraie scène : gadgets de farce à acheter (onglet Farces), pipi sur le tapis (tache
    /// jaune humide, nettoyable) ou sur un joueur (gouttes à l'écran, temporaires), fusil-jouet (faux sang stylisé, coût par
    /// tir, jamais sur un joueur), batte (chute comique, relevé rapide), garde-fous de l'autorité (achat, recharge, portée,
    /// protection des victimes, plafond par tapis, tapis terminé), chemin réseau de l'hôte, captures.
    /// </summary>
    public class PrankV012PlayModeTests
    {
        GameBootstrap game;
        MultiplayerSession session;

        IEnumerator LoadWorkshop(bool legacyTools = true)
        {
            TestSaveSandbox.Reset(legacyTools);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null && game.ToolController != null) game.ToolController.AutomationUseOverride = false;
            var current = MultiplayerSession.Find();
            if (current != null)
            {
                if (current.InSession) current.Manager.Shutdown();
                if (current.Manager != null) Object.Destroy(current.Manager.gameObject);
                Object.Destroy(current.gameObject);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        UpgradeDefinition Upgrade(string id)
        {
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.id == id) return upgrade;
            Assert.Fail($"Amélioration « {id} » absente");
            return null;
        }

        void Own(params string[] ids)
        {
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.AddRange(ids);
            game.Upgrades.Restore(owned);
        }

        IEnumerator StartOrderAndAimAtTheRug()
        {
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            // Visée du centre réel du tapis (sa taille dépend de la commande) : 1,1 m devant, à hauteur des yeux.
            Vector3 center = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            float pitch = Mathf.Atan2(1.68f - center.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(center.x, 0.06f, center.z - 1.1f), 0f, pitch);
            yield return null;
            yield return null;
        }

        float StainOf(DirtType type, out int cells)
        {
            var grid = game.Rug.Grid;
            float total = 0f;
            cells = 0;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                if (cell.StainType != type) continue;
                total += cell.Stain + cell.Loosened;
                cells++;
            }
            return total;
        }

        Vector3 AimPoint()
        {
            var camera = Camera.main;
            Assert.IsTrue(Physics.Raycast(camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit hit, 8f,
                ~((1 << 8) | (1 << 2) | (1 << 5)), QueryTriggerInteraction.Ignore));
            return hit.point;
        }

        sealed class FakeActor : IPrankActor
        {
            public ulong Id { get; set; } = 7;
            public string Name => "Farceur";
            public Vector3 Eye { get; set; }
            public bool IsDown { get; set; }
            public int Money { get; set; } = 10000;
            public readonly HashSet<PrankGadget> Owned = new HashSet<PrankGadget> { PrankGadget.Pee, PrankGadget.Bat, PrankGadget.Blaster };
            public bool Owns(PrankGadget gadget) => Owned.Contains(gadget);

            public bool TrySpend(int amount)
            {
                if (Money < amount) return false;
                Money -= amount;
                return true;
            }
        }

        sealed class FakeVictims : IPrankVictims
        {
            public readonly Dictionary<ulong, Vector3> Feet = new Dictionary<ulong, Vector3>();
            public readonly HashSet<ulong> Down = new HashSet<ulong>();
            public int Splashes, Knocks;
            public float LastIntensity, LastSeconds;
            public Vector3 LastImpulse;
            public PrankGadget LastCause;
            public bool TryGetFeet(ulong clientId, out Vector3 feet) => Feet.TryGetValue(clientId, out feet);
            public bool IsDown(ulong clientId) => Down.Contains(clientId);
            public void CollectPlayers(List<ulong> into) => into.AddRange(Feet.Keys);
            /// <summary>Joueurs mis KO (identifiants, dans l'ordre).</summary>
            public readonly List<ulong> Knocked = new List<ulong>();

            public void Splash(ulong clientId, float intensity, float seconds, float fadeSeconds, string from)
            {
                Splashes++;
                LastIntensity = intensity;
            }

            public void Knock(ulong clientId, Vector3 impulse, float seconds, string from, PrankGadget cause)
            {
                Knocks++;
                Knocked.Add(clientId);
                LastImpulse = impulse;
                LastSeconds = seconds;
                LastCause = cause;
            }
        }

        static PrankRequest AtPlayer(PrankGadget gadget, ulong victim) => new PrankRequest { Gadget = gadget, Target = PrankTarget.Player, Victim = victim };

        // ------------------------------------------------------------------ Achat et sélection

        [UnityTest]
        public IEnumerator Gadgets_MustBeBought_InTheirOwnTab_ThenReplaceTheToolWhileOut()
        {
            yield return LoadWorkshop();
            var gadgets = game.Gadgets;
            string message = null;
            gadgets.Message += m => message = m;
            gadgets.CycleNext();
            Assert.AreEqual(PrankGadget.None, gadgets.Equipped, "Rien à sortir sans achat.");
            StringAssert.Contains("Farces", message);

            game.ShopUI.Open();
            game.ShopUI.ShowTab(ShopUI.Tab.Pranks);
            yield return null;
            int rows = 0;
            foreach (var row in game.ShopUI.Rows) if (row.gameObject.activeSelf) rows++;
            Assert.AreEqual(4, rows, "Onglet Farces : pipi, batte, fusil-jouet, dynamite (V0.4.2).");
            StringAssert.Contains("Réputation", game.ShopUI.RowOf(Upgrade("up_prank_blaster")).StatusText);
            game.ShopUI.Close();

            int maxToolPrice = 0;
            foreach (var upgrade in game.Upgrades.Catalog)
                if (upgrade.effect == UpgradeEffect.UnlockTool) maxToolPrice = Mathf.Max(maxToolPrice, upgrade.price);
            foreach (string id in new[] { "up_prank_pee", "up_prank_bat", "up_prank_blaster" })
            {
                Assert.GreaterOrEqual(Upgrade(id).price, 2 * maxToolPrice, $"{id} : nettement plus cher qu'un outil.");
                Assert.Greater(Upgrade(id).requiredReputation, 0);
            }

            game.Wallet.Wallet.Add(20000);
            Assert.AreEqual(PurchaseResult.ReputationTooLow, game.Shop.TryPurchase(Upgrade("up_prank_bat")));
            game.Reputation.Restore(25);
            foreach (string id in new[] { "up_prank_pee", "up_prank_bat", "up_prank_blaster" })
                Assert.AreEqual(PurchaseResult.Purchased, game.Shop.TryPurchase(Upgrade(id)), id);
            yield return null;

            var tool = game.ToolController.ActiveTool;
            gadgets.CycleNext();
            Assert.AreEqual(PrankGadget.Pee, gadgets.Equipped);
            Assert.IsTrue(game.ToolController.Suspended);
            Assert.IsFalse(tool.IsEquipped, "L'outil est rangé pendant la farce.");
            yield return new WaitForSecondsRealtime(0.15f);
            StringAssert.Contains("Farce · Prête", game.Hud.ToolStatusText, "HUD : gadget et recharge.");
            gadgets.CycleNext();
            Assert.AreEqual(PrankGadget.Bat, gadgets.Equipped);
            yield return null;
            Assert.IsTrue(GameObject.Find("ViewModel_Bat") != null, "Batte en main.");
            gadgets.CycleNext();
            Assert.AreEqual(PrankGadget.Blaster, gadgets.Equipped);
            yield return new WaitForSecondsRealtime(0.15f);
            StringAssert.Contains("60 € par tir", game.Hud.ToolStatusText);
            gadgets.CycleNext();
            Assert.AreEqual(PrankGadget.None, gadgets.Equipped, "Après le dernier gadget : retour aux outils.");
            Assert.IsFalse(game.ToolController.Suspended);
            Assert.IsTrue(tool.IsEquipped);
        }

        // ------------------------------------------------------------------ Pipi

        [UnityTest]
        public IEnumerator Pee_OnTheRug_LeavesAWetYellowStain_ThatDetergentBrushAndRinseRemove()
        {
            yield return LoadWorkshop();
            Own("up_prank_pee");
            yield return StartOrderAndAimAtTheRug();
            var gadgets = game.Gadgets;
            Assert.IsTrue(gadgets.Equip(PrankGadget.Pee));
            float dirt = game.Rug.Grid.TotalDirt;
            Vector3 point = AimPoint();
            Assert.IsTrue(game.Rug.TrySampleCell(point, out RugCell before));

            Assert.IsTrue(gadgets.TryUse(), gadgets.LastResult.ToString());
            Assert.AreEqual(PrankResult.Done, gadgets.LastResult);
            float urine = StainOf(DirtType.Urine, out int cells);
            Assert.Greater(cells, 10, "Une vraie tache sur le tapis.");
            Assert.IsTrue(game.Rug.TrySampleCell(point, out RugCell after));
            Assert.Greater(after.Moisture, before.Moisture + 0.3f, "Elle est humide.");
            Assert.Greater(game.Pranks.AddedToRug, 1f, "Saleté réellement ajoutée.");
            Assert.AreEqual(dirt + game.Pranks.AddedToRug, game.Rug.Grid.TotalDirt, 0.5f, "Elle compte dans la saleté du tapis : il faut la nettoyer.");
            Assert.AreEqual(1, gadgets.Effects.ActiveStreams, "Jet en arc (dessin animé).");
            Assert.IsFalse(gadgets.TryUse(), "Recharge.");
            Assert.AreEqual(PrankResult.Cooldown, gadgets.LastResult);
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(0, gadgets.Effects.ActiveStreams, "Jet terminé.");

            // Nettoyage par les opérations des outils du kit (détergent, brosse, rinçage) sur la zone.
            var rug = game.Rug;
            for (int i = 0; i < 60; i++)
            {
                var soap = new SoapOperation { FoamPerSecond = 1.2f, WettingPerSecond = 0.1f, DeltaTime = 0.1f, ResiduePerFoam = 0.02f };
                rug.ApplyOperation(point, 0.5f, 2f, ref soap);
                var brush = new BrushOperation { Table = rug.DirtTable, Products = rug.Products, Power = 0.6f, DeltaTime = 0.1f, LatherPerSecond = 0.2f };
                rug.ApplyOperation(point, 0.5f, 2f, ref brush);
                var rinse = new RinseOperation { Table = rug.DirtTable, Power = 0.9f, WettingPerSecond = 0.5f, DeltaTime = 0.1f };
                rug.ApplyOperation(point, 0.5f, 2f, ref rinse);
            }
            float remaining = StainOf(DirtType.Urine, out _);
            Assert.Less(remaining, urine * 0.2f, $"Tache nettoyable : {urine:0.00} → {remaining:0.00}.");
        }

        // ------------------------------------------------------------------ Fusil-jouet

        [UnityTest]
        public IEnumerator Blaster_SplattersStylizedFakeBlood_CostsPerShot_AndKnocksAPlayerOutForFiveSeconds()
        {
            yield return LoadWorkshop();
            Own("up_prank_blaster");
            yield return StartOrderAndAimAtTheRug();
            game.Wallet.Wallet.Restore(100);
            var gadgets = game.Gadgets;
            Assert.IsTrue(gadgets.Equip(PrankGadget.Blaster));
            Assert.IsTrue(gadgets.TryUse(), gadgets.LastResult.ToString());
            Assert.AreEqual(40, game.Wallet.Balance, "60 € la cartouche, débités sur la carrière du tireur.");
            StainOf(DirtType.FakeBlood, out int cells);
            Assert.Greater(cells, 20, "Éclaboussures sur le tapis.");

            // Taches dispersées autour du point visé (plusieurs éclaboussures, pas une seule flaque).
            var grid = game.Rug.Grid;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid.GetCell(x, y).StainType == DirtType.FakeBlood)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            float cell = Mathf.Sqrt(grid.CellAreaM2);
            Assert.Greater(Mathf.Max(maxX - minX, maxY - minY) * cell, 0.3f, "Gerbe de taches.");

            Assert.IsFalse(gadgets.TryUse());
            Assert.AreEqual(PrankResult.Cooldown, gadgets.LastResult);
            Assert.AreEqual(40, game.Wallet.Balance, "Refus : rien n'est débité.");
            yield return new WaitForSeconds(0.1f);
            game.Pranks.Clock = () => Time.time + 1000f;
            Assert.AreEqual(PrankResult.InsufficientFunds, game.Pranks.Execute(gadgets.Actor, gadgets.LastRequest, out _));
            Assert.AreEqual(40, game.Wallet.Balance, "Fonds insuffisants : refus sans débit.");

            var actor = new FakeActor { Eye = game.PlayerController.transform.position + Vector3.up * 1.6f };
            var victims = new FakeVictims();
            victims.Feet[9] = game.PlayerController.transform.position + Vector3.forward;
            game.Pranks.Victims = victims;
            Assert.AreEqual(PrankResult.Done, game.Pranks.Execute(actor, AtPlayer(PrankGadget.Blaster, 9), out var hit), "Mission de corrections : KO.");
            Assert.IsTrue(hit.HitPlayer);
            Assert.AreEqual(1, victims.Knocks);
            Assert.AreEqual(PrankGadget.Blaster, victims.LastCause, "Cause transmise : la tête éclate chez les autres.");
            Assert.AreEqual(5f, victims.LastSeconds, 1e-4f, "KO de 5 s.");
            Assert.Greater(victims.LastImpulse.magnitude, game.Pranks.Tuning.knockImpulse, "Élan plus fort qu'un coup de batte.");
            Assert.AreEqual(PrankResult.TargetProtected, game.Pranks.Execute(new FakeActor { Id = 8, Eye = actor.Eye }, AtPlayer(PrankGadget.Blaster, 9), out _),
                "Pas de KO en chaîne.");
            game.Pranks.Victims = null;
        }

        // ------------------------------------------------------------------ Autorité et garde-fous

        [UnityTest]
        public IEnumerator Authority_ValidatesTargets_ProtectsVictims_CapsTheRug_AndSparesFinishedRugs()
        {
            yield return LoadWorkshop();
            var service = game.Pranks;
            float now = 100f;
            service.Clock = () => now;
            var victims = new FakeVictims();
            service.Victims = victims;
            var origin = new Vector3(0f, 1.6f, -1f);
            var actor = new FakeActor { Id = 7, Eye = origin };
            var other = new FakeActor { Id = 8, Eye = origin };
            victims.Feet[9] = new Vector3(0f, 0f, 0.4f);

            Assert.AreEqual(PrankResult.Done, service.Execute(actor, AtPlayer(PrankGadget.Pee, 9), out var outcome));
            Assert.IsTrue(outcome.HitPlayer);
            Assert.AreEqual(1, victims.Splashes);
            Assert.AreEqual(service.Tuning.splashIntensity, victims.LastIntensity);
            Assert.AreEqual(PrankResult.TargetProtected, service.Execute(other, AtPlayer(PrankGadget.Pee, 9), out _), "Pas d'acharnement.");
            Assert.AreEqual(PrankResult.Cooldown, service.Execute(actor, AtPlayer(PrankGadget.Bat, 9), out _), "Délai entre deux farces.");
            now += 4f;
            Assert.AreEqual(PrankResult.Done, service.Execute(actor, AtPlayer(PrankGadget.Bat, 9), out _));
            Assert.AreEqual(1, victims.Knocks);
            var push = victims.LastImpulse;
            Assert.AreEqual(service.Tuning.knockImpulse, new Vector3(push.x, 0f, push.z).magnitude, 1e-3f, "Élan borné, dirigé vers la victime.");
            Assert.Greater(push.z, 0f);
            Assert.AreEqual(PrankResult.TargetProtected, service.Execute(other, AtPlayer(PrankGadget.Bat, 9), out _), "Protégé après un coup.");
            now += service.Tuning.knockProtectionSeconds + 1f;
            victims.Down.Add(9);
            Assert.AreEqual(PrankResult.TargetProtected, service.Execute(other, AtPlayer(PrankGadget.Bat, 9), out _), "Jamais un joueur à terre.");
            Assert.AreEqual(PrankResult.TargetProtected, service.Execute(other, AtPlayer(PrankGadget.Blaster, 9), out _),
                "Fusil : jamais un joueur déjà à terre (pas de KO enchaîné).");
            victims.Down.Clear();

            victims.Feet[10] = new Vector3(0f, 0f, 6f);
            Assert.AreEqual(PrankResult.OutOfRange, service.Execute(other, AtPlayer(PrankGadget.Bat, 10), out _));
            Assert.AreEqual(PrankResult.NoTarget, service.Execute(other, AtPlayer(PrankGadget.Bat, 8), out _), "Pas soi-même.");
            Assert.AreEqual(PrankResult.NoTarget, service.Execute(other, AtPlayer(PrankGadget.Bat, 42), out _), "Joueur inconnu.");
            other.Owned.Clear();
            Assert.AreEqual(PrankResult.NotOwned, service.Execute(other, AtPlayer(PrankGadget.Pee, 9), out _));
            actor.IsDown = true;
            Assert.AreEqual(PrankResult.Unavailable, service.Execute(actor, AtPlayer(PrankGadget.Pee, 9), out _), "Pas de farce à terre.");
            actor.IsDown = false;
            Assert.AreEqual(10000, other.Money, "Aucune farce ne touche à l'argent d'un autre joueur.");

            // Tapis : sans commande, rien à salir ; puis plafond par tapis.
            var rugRequest = new PrankRequest { Gadget = PrankGadget.Pee, Target = PrankTarget.Rug, Point = game.Rug.transform.position };
            now += 100f;
            Assert.AreEqual(PrankResult.NoTarget, service.Execute(actor, rugRequest, out _));
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            rugRequest.Point = game.Rug.transform.position + Vector3.up * 0.02f;
            float cap = PrankRules.CapRemaining(0f, game.Rug.Grid.InitialDirt, service.Tuning.rugCapFraction, service.Tuning.rugCapMinimum);
            int done = 0;
            PrankResult result;
            float largest = 0f;
            do
            {
                now += 100f;
                rugRequest.Point = game.Rug.transform.position + new Vector3((done % 5 - 2) * 0.4f, 0.02f, (done / 5 % 3 - 1) * 0.4f);
                result = service.Execute(actor, rugRequest, out var stain);
                if (result == PrankResult.Done)
                {
                    done++;
                    largest = Mathf.Max(largest, stain.Added);
                }
            } while (result == PrankResult.Done && done < 200);
            Assert.AreEqual(PrankResult.CapReached, result);
            Assert.Greater(done, 1);
            Assert.LessOrEqual(service.AddedToRug, cap + largest + 1e-3f, "Plafond : au plus une farce au-delà.");

            // Tapis terminé : on ne salit plus un travail fini.
            var clear = new ClearDirtOperation();
            game.Rug.ApplyOperation(game.Rug.transform.position, 6f, 1f, ref clear);
            Assert.IsTrue(game.Rug.IsFinished);
            game.Orders.Abandon();
            yield return null;
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            Assert.AreEqual(0f, service.AddedToRug, "Nouveau tapis : plafond remis à zéro.");
            game.Rug.ApplyOperation(game.Rug.transform.position, 6f, 1f, ref clear);
            now += 100f;
            Assert.AreEqual(PrankResult.RugFinished, service.Execute(actor, rugRequest, out _));
            service.Victims = null;
        }

        // ------------------------------------------------------------------ Farces subies par le joueur local

        [UnityTest]
        public IEnumerator Bat_KnocksTheLocalPlayerDown_WhoCanGetUpQuickly()
        {
            yield return LoadWorkshop();
            string message = null;
            game.Gadgets.Message += m => message = m;
            game.Knockdown.Message += m => message = m;
            game.Gadgets.ReceiveKnock(Vector3.forward * 3.5f + Vector3.up, 1.4f, "Farceur");
            Assert.IsTrue(game.Knockdown.IsDown);
            StringAssert.Contains("Farceur", message);
            StringAssert.Contains("relever", message);
            Assert.IsFalse(game.Knockdown.TryQuickRecover(), "Pas pendant la chute.");
            yield return new WaitForSeconds(0.7f);
            Assert.IsTrue(game.Knockdown.TryQuickRecover(), "Relevé rapide (Interagir).");
            yield return new WaitForSeconds(0.75f);
            Assert.IsFalse(game.Knockdown.IsDown, "Debout bien avant la fin de la chute normale (2,35 s).");
        }

        [UnityTest]
        public IEnumerator Splash_OnTheLocalScreen_IsPartial_Temporary_AndCleared()
        {
            yield return LoadWorkshop();
            var splash = game.Gadgets.Splash;
            game.Gadgets.ReceiveSplash(0.65f, 1.5f, 0.6f, "Farceur");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(0.65f, splash.Alpha, 0.03f);
            Assert.LessOrEqual(splash.Alpha, 0.85f, "Jamais opaque.");
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.Less(splash.Alpha, 0.65f, "Fondu de sortie.");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(0f, splash.Alpha, "Durée limitée.");
            Assert.IsFalse(splash.IsActive);
            game.Gadgets.ReceiveSplash(2f, 5f, 1f, "Farceur");
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.LessOrEqual(splash.Alpha, 0.85f, "Intensité plafonnée.");
            game.Gadgets.ClearReceived();
            Assert.AreEqual(0f, splash.Alpha, "Effacées (fin de partie, replacement).");
        }

        // ------------------------------------------------------------------ Hôte en ligne

        [UnityTest]
        public IEnumerator Host_AppliesAGuestsPrankToTheHostPlayer_ThroughTheNetworkWorkshop()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsTrue(menu.Multiplayer.Open());
            session = menu.Multiplayer.Session;
            Assert.IsTrue(session.HostDirect(7793), session.Message);
            float end = Time.realtimeSinceStartup + 30f;
            while ((NetworkWorkshop.Instance == null || !NetworkWorkshop.Instance.IsSpawned) && Time.realtimeSinceStartup < end) yield return null;
            var workshop = NetworkWorkshop.Instance;
            Assert.IsNotNull(workshop);
            yield return null;
            game = workshop.Game;
            Assert.AreSame(workshop, game.Pranks.Victims, "L'hôte fait autorité sur les farces.");
            ulong hostId = workshop.NetworkManager.LocalClientId;
            Assert.AreEqual(hostId, game.Gadgets.LocalId);
            Assert.AreEqual(0, game.Gadgets.RemotePlayers().Count, "Aucun autre joueur à viser.");

            // Un invité (simulé) vise l'hôte : position et état lus par l'atelier réseau, effets appliqués chez l'hôte.
            var guest = new FakeActor { Id = 99, Eye = game.PlayerController.transform.position + new Vector3(0f, 1.6f, -1.2f) };
            Assert.AreEqual(PrankResult.Done, game.Pranks.Execute(guest, AtPlayer(PrankGadget.Pee, hostId), out _));
            yield return null;
            Assert.IsTrue(game.Gadgets.Splash.IsActive, "Gouttes sur l'écran de l'hôte.");
            game.Pranks.Clock = () => Time.time + 10f;
            Assert.AreEqual(PrankResult.Done, game.Pranks.Execute(guest, AtPlayer(PrankGadget.Bat, hostId), out _));
            Assert.IsTrue(game.Knockdown.IsDown, "L'hôte chute.");
            Assert.AreEqual(PrankResult.TargetProtected, game.Pranks.Execute(new FakeActor { Id = 98, Eye = guest.Eye }, AtPlayer(PrankGadget.Bat, hostId), out _));
            Assert.AreEqual(PrankResult.NoTarget, game.Pranks.Execute(new FakeActor { Id = 97, Eye = guest.Eye }, AtPlayer(PrankGadget.Bat, 12345), out _),
                "Client inconnu.");
            session.Leave();
            float leave = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != "MainMenu" && Time.realtimeSinceStartup < leave) yield return null;
        }

        // ------------------------------------------------------------------ Captures

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_GadgetsStainsAndScreenSplash()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            yield return LoadWorkshop();
            Own("up_prank_pee", "up_prank_bat", "up_prank_blaster");
            yield return StartOrderAndAimAtTheRug();
            game.Wallet.Wallet.Add(1000);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;
            var gadgets = game.Gadgets;

            gadgets.Equip(PrankGadget.Bat);
            yield return new WaitForSeconds(0.2f);
            yield return Capture(camera, hud, Path.Combine(folder, "73_bat.png"));
            gadgets.Equip(PrankGadget.Blaster);
            yield return new WaitForSeconds(0.2f);
            yield return Capture(camera, hud, Path.Combine(folder, "73b_blaster.png"));
            Assert.IsTrue(gadgets.TryUse());
            yield return new WaitForSeconds(0.08f);
            yield return Capture(camera, hud, Path.Combine(folder, "73c_blaster_shot.png"));
            gadgets.Equip(PrankGadget.Pee);
            game.Pranks.Clock = () => Time.time + 1000f;
            game.PlayerController.Teleport(new Vector3(0.9f, 0.06f, -1f), -10f, 55f);
            yield return null;
            Assert.IsTrue(gadgets.TryUse());
            yield return new WaitForSeconds(0.6f);
            yield return Capture(camera, hud, Path.Combine(folder, "74_pee_stream.png"));
            yield return new WaitForSeconds(0.8f);
            gadgets.Unequip();
            game.PlayerController.Teleport(new Vector3(0.3f, 0.06f, -1.2f), 0f, 50f);
            yield return new WaitForSeconds(0.2f);
            yield return Capture(camera, hud, Path.Combine(folder, "74b_prank_stains.png"));
            gadgets.ReceiveSplash(0.65f, 6f, 2f, "Farceur");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture(camera, hud, Path.Combine(folder, "75_screen_splash.png"));
            gadgets.ClearReceived();
            game.ShopUI.Open();
            game.ShopUI.ShowTab(ShopUI.Tab.Pranks);
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Capture(camera, hud, Path.Combine(folder, "76_shop_pranks.png"));
            game.ShopUI.Close();
        }

        static IEnumerator Capture(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
