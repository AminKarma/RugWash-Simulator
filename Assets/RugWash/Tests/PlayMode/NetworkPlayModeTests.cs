using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Network;
using RugWash.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Jalon 5 — réplication du nettoyage (deux grilles, latence simulée, nettoyage simultané) et vraie session hôte
    /// Netcode for GameObjects (Unity Transport, local). Le jeu à plusieurs processus est vérifié par l'étape « multiplayer ».
    /// </summary>
    public class NetworkPlayModeTests
    {
        GameBootstrap game;
        MultiplayerSession session;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestSaveSandbox.Reset();
            yield return null;
        }

        IEnumerator Load(string scene)
        {
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
            game = UnityEngine.Object.FindAnyObjectByType<GameBootstrap>();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (session != null)
            {
                if (session.InSession) session.Manager.Shutdown();
                if (session.Manager != null) UnityEngine.Object.Destroy(session.Manager.gameObject);
                UnityEngine.Object.Destroy(session.gameObject);
                session = null;
            }
            Time.timeScale = 1f;
            yield return null;
            yield return null;
        }

        // ------------------------------------------------------------------ Réplication (sans réseau)

        sealed class Pipe
        {
            readonly List<(int frame, byte[] data)> queue = new List<(int, byte[])>();
            public int Delay;
            public int Sent;

            public void Send(int frame, ArraySegment<byte> data)
            {
                var copy = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
                queue.Add((frame + Delay, copy));
                Sent++;
            }

            public void Deliver(int frame, Action<byte[]> receive, bool all = false)
            {
                while (queue.Count > 0 && (all || queue[0].frame <= frame))
                {
                    var item = queue[0];
                    queue.RemoveAt(0);
                    receive(item.data);
                }
            }
        }

        [UnityTest]
        public IEnumerator TwoPlayersCleaningTheSameRug_Converge_WithoutDoubleCounting_OrRegression()
        {
            yield return Load("Workshop");
            TestRugs.LoadDefault(game);
            var hostRug = game.Rug;
            var copy = UnityEngine.Object.Instantiate(hostRug.gameObject, hostRug.transform.position + new Vector3(0f, 0f, 20f), hostRug.transform.rotation);
            var guestRug = copy.GetComponent<RugCleaningController>();
            yield return null;
            var definition = hostRug.Definition;

            int frame = 0;
            var toHost = new Pipe { Delay = 3 };
            var toGuest = new Pipe { Delay = 4 };
            const ulong guestId = 7;
            var hostSync = new SurfaceSyncHost(_ => new SurfaceIdentity(definition.id, string.Empty));
            hostSync.Register(0, hostRug);
            var guestSync = new SurfaceSyncClient(data => toHost.Send(frame, data), (_, identity) =>
            {
                guestRug.LoadRug(definition);
                return true;
            }) { LocalClientId = guestId };
            guestSync.Register(0, guestRug);
            var writer = new ByteWriter();

            void HostSends()
            {
                writer.Reset();
                if (hostSync.TryWriteUpdate(0, writer)) toGuest.Send(frame, writer.Segment);
            }

            // Instantané initial : la réplique prend l'état de l'hôte.
            HostSends();
            toGuest.Deliver(int.MaxValue, data => guestSync.Receive(new ArraySegment<byte>(data)), all: true);
            Assert.IsTrue(guestSync.IsReady(0));
            Assert.AreEqual(hostRug.Grid.InitialDirt, guestRug.Grid.InitialDirt, 1e-4f);

            float lastGuestCleanliness = guestRug.Cleanliness01;
            float worstDrop = 0f;
            int guestOps = 0;
            for (frame = 1; frame <= 90; frame++)
            {
                // Les deux joueurs aspirent : zones différentes et une zone commune (actions simultanées).
                var hostVacuum = new VacuumOperation { Table = hostRug.DirtTable, Power = 0.8f, DeltaTime = 0.03f };
                hostRug.ApplyOperation(hostRug.UVToWorld(new Vector2(0.3f + frame * 0.002f, 0.5f)), 0.25f, 4f, ref hostVacuum);
                var guestVacuum = new VacuumOperation { Table = guestRug.DirtTable, Power = 0.8f, DeltaTime = 0.03f };
                float u = frame % 2 == 0 ? 0.32f : 0.7f;
                guestRug.ApplyOperation(guestRug.UVToWorld(new Vector2(u, 0.5f)), 0.25f, 4f, ref guestVacuum);
                guestOps++;

                toHost.Deliver(frame, data => Assert.IsTrue(hostSync.TryApply(guestId, new ArraySegment<byte>(data), out _)));
                if (frame % 3 == 0) HostSends();
                toGuest.Deliver(frame, data => guestSync.Receive(new ArraySegment<byte>(data)));

                float now = guestRug.Cleanliness01;
                worstDrop = Mathf.Max(worstDrop, lastGuestCleanliness - now);
                lastGuestCleanliness = now;
            }

            // Fin : tout est livré, l'hôte envoie le dernier état.
            toHost.Deliver(int.MaxValue, data => hostSync.TryApply(guestId, new ArraySegment<byte>(data), out _), all: true);
            HostSends();
            toGuest.Deliver(int.MaxValue, data => guestSync.Receive(new ArraySegment<byte>(data)), all: true);

            Assert.AreEqual(guestOps, hostSync.OperationsApplied, "Chaque opération de l'invité est appliquée une fois par l'hôte.");
            Assert.AreEqual(0, hostSync.RejectedMessages);
            Assert.AreEqual(0, guestSync.RejectedMessages, "Aucun message de l'hôte refusé.");
            Assert.AreEqual(0, guestSync.Predictor.PendingCount,
                $"Toutes les opérations prédites sont confirmées (accusé hôte {hostSync.Acks.LastApplied(guestId)}, dernière séquence {guestSync.Predictor.LastSequence}, messages appliqués {guestSync.MessagesApplied}).");
            Assert.Less(worstDrop, 0.004f, "La propreté vue par l'invité ne recule pas (au bruit de quantification près).");
            Assert.Greater(hostRug.Cleanliness01, 0.02f, "Le tapis a réellement été nettoyé.");

            int width = hostRug.Grid.Width, height = hostRug.Grid.Height;
            float maxError = 0f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var a = hostRug.Grid.GetCell(x, y);
                var b = guestRug.Grid.GetCell(x, y);
                maxError = Mathf.Max(maxError, Mathf.Abs(a.Dust - b.Dust), Mathf.Abs(a.Stain - b.Stain), Mathf.Abs(a.Loosened - b.Loosened) * 0.5f,
                    Mathf.Abs(a.Moisture - b.Moisture), Mathf.Abs(a.Foam - b.Foam));
            }
            Assert.LessOrEqual(maxError, 0.5f / 255f + 1e-4f, "Les deux tapis sont identiques (à la quantification près).");
            Assert.AreEqual(hostRug.Cleanliness01, guestRug.Cleanliness01, 0.003f, "Même propreté chez l'hôte et l'invité.");

            // Nouveau tapis chez l'hôte : l'invité le recharge (nouvelle époque) et les mises à jour périmées sont ignorées.
            hostRug.LoadRug(definition);
            HostSends();
            toGuest.Deliver(int.MaxValue, data => guestSync.Receive(new ArraySegment<byte>(data)), all: true);
            Assert.AreEqual(hostRug.Cleanliness01, guestRug.Cleanliness01, 0.003f);
            guestSync.Unregister();
            Assert.IsNull(guestRug.Replication);
            hostSync.Unregister();
        }

        // ------------------------------------------------------------------ Session réelle (hôte local)

        [UnityTest]
        public IEnumerator HostSession_SpawnsTheSharedWorkshop_AppliesGuestOperations_PaysOnce_AndLeaves()
        {
            yield return Load("MainMenu");
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsTrue(menu.Multiplayer.Open());
            session = menu.Multiplayer.Session;
            Assert.IsNotNull(session);
            Assert.That(menu.Multiplayer.SteamStatus, Does.StartWith("Steam"), "État de Steam affiché (indisponible si le client Steam n'est pas lancé).");

            Assert.IsTrue(session.HostDirect(7791), session.Message);
            float end = Time.realtimeSinceStartup + 30f;
            while ((NetworkWorkshop.Instance == null || !NetworkWorkshop.Instance.IsSpawned) && Time.realtimeSinceStartup < end) yield return null;
            var workshop = NetworkWorkshop.Instance;
            Assert.IsNotNull(workshop, "L'atelier réseau apparaît chez l'hôte.");
            yield return null;
            game = workshop.Game;
            Assert.AreEqual(SessionState.InGame, session.State);
            Assert.IsTrue(game.GameState.Networked);
            Assert.IsNull(game.Rug.Replication, "L'hôte fait autorité sur le tapis.");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<NetworkPlayerAvatar>().Length, "Silhouette de l'hôte (cachée chez lui).");

            // Pause en ligne : le temps partagé continue.
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.AreEqual(1f, Time.timeScale);
            game.GameState.Resume();
            TestRugs.LoadDefault(game);

            // Opération reçue d'un invité : appliquée par l'hôte, accusée.
            float dirtBefore = game.Rug.Grid.TotalDirt;
            var writer = new ByteWriter();
            var vacuum = new VacuumOperation { Power = 0.8f, DeltaTime = 0.05f };
            var geometry = new SurfaceOpGeometry(game.Rug.UVToWorld(new Vector2(0.5f, 0.5f)), 0.3f, 4f, 1f, Vector3.zero, default);
            Assert.IsTrue(SurfaceOperationCodec.TryWrite(writer, NetworkWorkshop.RugId, 1u, geometry, ref vacuum));
            Assert.IsTrue(workshop.HostSync.TryApply(42, writer.Segment, out _));
            Assert.Less(game.Rug.Grid.TotalDirt, dirtBefore, "L'opération de l'invité nettoie le tapis de l'hôte.");
            Assert.AreEqual(1u, workshop.HostSync.Acks.LastApplied(42));

            // Commande : démarrée et livrée par l'hôte ; une seconde livraison est impossible.
            int epoch = workshop.HostSync.EpochOf(NetworkWorkshop.RugId);
            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(order));
            Assert.Greater(workshop.HostSync.EpochOf(NetworkWorkshop.RugId), epoch, "Nouveau tapis : instantané pour les invités.");
            game.Rug.Grid.Clear();
            int money = game.Wallet.Balance;
            Assert.IsNotNull(game.Orders.Deliver());
            int paid = game.Wallet.Balance - money;
            Assert.Greater(paid, 0, "Commande payée.");
            Assert.IsNull(game.Orders.Deliver(), "Pas de seconde livraison.");
            Assert.AreEqual(money + paid, game.Wallet.Balance, "Pas de double paiement.");

            // Départ : session fermée, retour au menu.
            session.Leave();
            end = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name != "MainMenu" && Time.realtimeSinceStartup < end) yield return null;
            yield return null;
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
            Assert.IsFalse(session.InSession);
            Assert.AreEqual(SessionState.Offline, session.State);
        }

        [UnityTest]
        public IEnumerator MainMenu_StartsSteam_AndAnInviteDuringSoloPlay_SavesThenReturnsToTheMenu()
        {
            yield return Load("MainMenu");
            session = MultiplayerSession.Find();
            Assert.IsNotNull(session, "La session existe dès le menu principal.");
            Assert.IsNotNull(session.Steam, "Steam est initialisé (ou son absence expliquée) sans ouvrir la fenêtre Multijoueur.");
            Assert.IsTrue(session.Steam.IsInitialized || !string.IsNullOrEmpty(session.Steam.InitError));

            // Partie solo, commande en cours, puis invitation acceptée : progression sauvegardée, retour au menu.
            yield return Load("Workshop");
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            game.Wallet.Wallet.Add(37);
            session.AcceptInvite(109775241234567890ul);
            float end = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name != "MainMenu" && Time.realtimeSinceStartup < end) yield return null;
            yield return null;
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name, "Retour au menu pour rejoindre le salon.");
            Assert.IsTrue(File.Exists(TestSaveSandbox.SavePath), "Partie solo sauvegardée avant de partir.");
            var saved = new Save.SaveFileStore(Path.GetDirectoryName(TestSaveSandbox.SavePath), Path.GetFileName(TestSaveSandbox.SavePath)).Load().Data;
            Assert.AreEqual(37, saved.money, "Argent de la partie solo conservé.");
            Assert.IsFalse(session.InSession);
        }

        // ------------------------------------------------------------------ Captures

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_MultiplayerScreens()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);

            yield return Load("MainMenu");
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsTrue(menu.Multiplayer.Open());
            session = menu.Multiplayer.Session;
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Capture(Camera.main, FindCanvas("Menu Canvas"), Path.Combine(folder, "60_multiplayer_menu.png"));
            menu.Multiplayer.Close();

            // Partie hébergée : silhouette d'un autre joueur (instance hors réseau pour la vue) et liste des joueurs.
            Assert.IsTrue(session.HostDirect(7792), session.Message);
            float end = Time.realtimeSinceStartup + 30f;
            while ((NetworkWorkshop.Instance == null || !NetworkWorkshop.Instance.IsSpawned) && Time.realtimeSinceStartup < end) yield return null;
            game = NetworkWorkshop.Instance.Game;
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            // Autre joueur (instance hors réseau pour la vue) : derrière le tapis, il brosse un point du tapis.
            Vector3 aimed = game.Rug.UVToWorld(new Vector2(0.62f, 0.7f));
            Vector3 feet = game.Rug.transform.TransformPoint(new Vector3(game.Rug.SizeMeters.x * 0.12f, 0f, game.Rug.SizeMeters.y * 0.5f + 0.75f));
            feet.y = 0.06f;
            var facing = Quaternion.LookRotation(new Vector3(aimed.x - feet.x, 0f, aimed.z - feet.z));
            var other = UnityEngine.Object.Instantiate(session.Content.avatarPrefab, feet, facing);
            UnityEngine.Object.Destroy(other.GetComponent<Unity.Netcode.Components.NetworkTransform>());
            var avatar = other.GetComponent<NetworkPlayerAvatar>();
            var visuals = other.transform.Find("Visuals");
            visuals.Find("Name").GetComponent<TextMesh>().text = "Invité";
            float horizontal = new Vector2(aimed.x - feet.x, aimed.z - feet.z).magnitude;
            avatar.SetPreviewState(3, true, Mathf.Atan2(avatar.View.position.y - aimed.y, horizontal) * Mathf.Rad2Deg);
            var target = game.Rug.UVToWorld(new Vector2(0.5f, 0.3f));
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.6f), 0f, 12f);
            yield return new WaitForSecondsRealtime(0.8f);
            // Instance de démonstration hors réseau : le nom est tourné vers la caméra comme pour une vraie silhouette.
            var name = visuals.Find("Name");
            name.rotation = Quaternion.LookRotation(name.position - Camera.main.transform.position);
            yield return Capture(Camera.main, FindCanvas("HUD Canvas"), Path.Combine(folder, "61_coop_workshop.png"));
            UnityEngine.Object.Destroy(other);
            session.Leave();
            end = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().name != "MainMenu" && Time.realtimeSinceStartup < end) yield return null;
        }

        static Canvas FindCanvas(string name)
        {
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
                if (canvas.name == name) return canvas;
            return null;
        }

        static IEnumerator Capture(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            yield return new WaitForSecondsRealtime(0.12f);
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
    }
}
