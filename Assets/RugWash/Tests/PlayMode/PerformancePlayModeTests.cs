using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2, jalon 2 — performance et fiabilité dans la vraie scène : la mousse au repos n'est plus renvoyée aux
    /// invités plusieurs fois par seconde (chaque poste la reproduit, l'hôte corrige à basse fréquence) ; une longue session
    /// de nettoyage ne fait croître ni le nombre d'objets, ni les textures, ni les maillages, ni la mémoire.
    /// </summary>
    public class PerformancePlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = UnityEngine.Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null) game.ToolController.AutomationUseOverride = false;
            yield return null;
        }

        void AimAtRugCenter(Vector2 uv)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, pitch);
        }

        // ------------------------------------------------------------------ Réseau : mousse au repos

        [UnityTest]
        public IEnumerator FoamAtRest_IsReproducedByTheGuest_AndOnlyCorrectedAtLowRate()
        {
            TestRugs.LoadDefault(game);
            var hostRug = game.Rug;
            var copy = UnityEngine.Object.Instantiate(hostRug.gameObject, hostRug.transform.position + new Vector3(0f, 0f, 20f), hostRug.transform.rotation);
            var guestRug = copy.GetComponent<RugCleaningController>();
            yield return null;
            var definition = hostRug.Definition;
            var hostSync = new SurfaceSyncHost(_ => new SurfaceIdentity(definition.id, string.Empty));
            hostSync.Register(0, hostRug);
            var guestSync = new SurfaceSyncClient(_ => { }, (_, identity) =>
            {
                guestRug.LoadRug(definition);
                return true;
            }) { LocalClientId = 7 };
            guestSync.Register(0, guestRug);
            var writer = new ByteWriter(4096);

            long Send(bool includeSlow)
            {
                writer.Reset();
                if (!hostSync.TryWriteUpdate(0, writer, includeSlow)) return 0;
                Assert.IsTrue(guestSync.Receive(writer.Segment));
                return writer.Length;
            }

            Send(true);
            // Tout le tapis couvert de mousse (comme après un passage au canon), transmis une fois.
            var soap = new SoapOperation { FoamPerSecond = 2f, WettingPerSecond = 0.3f, DeltaTime = 0.5f, ResiduePerFoam = 0.3f };
            hostRug.Grid.ApplyAll(ref soap);
            Send(true);
            Assert.Greater(guestRug.Grid.TotalFoam, hostRug.Grid.CellCount * 0.5f);
            float guestFoamStart = guestRug.Grid.TotalFoam;

            // Avant (référence) : la simulation lente partait avec chaque envoi (15 par seconde).
            long everySend = 0;
            for (float t = 0f, next = 0f; t < 3f; t += Time.deltaTime)
            {
                if (t >= next)
                {
                    everySend += Send(true);
                    next += 1f / 15f;
                }
                yield return null;
            }
            // Maintenant : chaque poste la reproduit ; correction de l'hôte toutes les 2 s seulement.
            long corrected = 0;
            float slowTimer = 0f;
            for (float t = 0f, next = 0f; t < 3f; t += Time.deltaTime)
            {
                slowTimer += Time.deltaTime;
                if (t >= next)
                {
                    bool slow = slowTimer >= 2f;
                    if (slow) slowTimer = 0f;
                    corrected += Send(slow);
                    next += 1f / 15f;
                }
                yield return null;
            }
            Debug.Log($"[RugWash] Mousse au repos, 3 s : {everySend / 1024f:0.0} Ko envoyés avant, {corrected / 1024f:0.0} Ko maintenant.");
            Assert.Less(corrected * 4, everySend, "Au moins 4 fois moins de données pour la mousse qui retombe.");
            Assert.Greater(guestRug.Grid.AverageFoam, 0f);
            Assert.Less(guestRug.Grid.TotalFoam, guestFoamStart * 0.99f, "La mousse retombe aussi chez l'invité (reproduite localement).");

            // Correction : les deux tapis sont identiques à la quantification près.
            Send(true);
            float maxError = 0f;
            for (int y = 0; y < hostRug.Grid.Height; y++)
            for (int x = 0; x < hostRug.Grid.Width; x++)
            {
                var a = hostRug.Grid.GetCell(x, y);
                var b = guestRug.Grid.GetCell(x, y);
                maxError = Mathf.Max(maxError, Mathf.Abs(a.Foam - b.Foam), Mathf.Abs(a.Moisture - b.Moisture), Mathf.Abs(a.Stain - b.Stain));
            }
            Assert.LessOrEqual(maxError, 0.5f / 255f + 1e-4f, "Après correction, l'invité a exactement l'état de l'hôte (quantifié).");
            guestSync.Unregister();
            hostSync.Unregister();
            UnityEngine.Object.Destroy(copy);
        }

        /// <summary>Mousse imposée sur toute la grille, marquée comme simulation lente (comme la mousse qui retombe).</summary>
        struct SlowFoam : ICellOperation
        {
            public float Foam;

            public void Apply(ref RugCell cell, float weight)
            {
                cell.Foam = Foam;
                cell.Moisture = 0.4f;
            }
        }

        [UnityTest]
        public IEnumerator SlowCorrection_WithLotsOfFoam_IsSpreadInRowChunks_AndTheGuestConvergesExactly()
        {
            TestRugs.LoadDefault(game);
            var hostRug = game.Rug;
            var copy = UnityEngine.Object.Instantiate(hostRug.gameObject, hostRug.transform.position + new Vector3(0f, 0f, 20f), hostRug.transform.rotation);
            var guestRug = copy.GetComponent<RugCleaningController>();
            yield return null;
            var definition = hostRug.Definition;
            var hostSync = new SurfaceSyncHost(_ => new SurfaceIdentity(definition.id, string.Empty));
            hostSync.Register(0, hostRug);
            var guestSync = new SurfaceSyncClient(_ => { }, (_, identity) =>
            {
                guestRug.LoadRug(definition);
                return true;
            }) { LocalClientId = 7 };
            guestSync.Register(0, guestRug);
            var writer = new ByteWriter(4096);
            writer.Reset();
            Assert.IsTrue(hostSync.TryWriteUpdate(0, writer));
            Assert.IsTrue(guestSync.Receive(writer.Segment));

            // Tout le tapis couvert : la zone de correction lente est la grille entière.
            var foam = new SlowFoam { Foam = 0.9f };
            hostRug.Grid.ApplyAllTracked(ref foam, slowReplication: true);
            writer.Reset();
            Assert.IsFalse(hostSync.TryWriteUpdate(0, writer, includeSlow: false), "Rien en réplication rapide.");

            const int budget = 4096;
            hostSync.BeginSlowCorrection(0);
            int chunks = 0, total = 0, biggest = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double worstMs = 0;
            while (hostSync.SlowCorrectionPending(0))
            {
                writer.Reset();
                long start = sw.ElapsedTicks;
                int written = hostSync.TryWriteSlowChunk(0, writer, budget);
                worstMs = Math.Max(worstMs, (sw.ElapsedTicks - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                Assert.Greater(written, 0);
                Assert.LessOrEqual(written, Mathf.Max(budget, hostRug.Grid.Width), "Une tranche respecte le budget (au moins une ligne).");
                Assert.IsTrue(guestSync.Receive(writer.Segment));
                biggest = Mathf.Max(biggest, writer.Length);
                total += written;
                chunks++;
                Assert.Less(chunks, 1000);
            }
            Debug.Log($"[RugWash] Correction lente d'un tapis couvert ({hostRug.Grid.CellCount} cellules) : {chunks} tranches, " +
                      $"plus grand message {biggest / 1024f:0.0} Ko, pire écriture {worstMs:0.00} ms.");
            Assert.AreEqual(hostRug.Grid.CellCount, total, "Toute la grille est corrigée, une fois.");
            Assert.Greater(chunks, 1, "Répartie sur plusieurs envois.");
            Assert.AreEqual(0, hostSync.TryWriteSlowChunk(0, writer, budget), "Cycle terminé.");

            float maxError = 0f;
            for (int y = 0; y < hostRug.Grid.Height; y++)
            for (int x = 0; x < hostRug.Grid.Width; x++)
            {
                var a = hostRug.Grid.GetCell(x, y);
                var b = guestRug.Grid.GetCell(x, y);
                maxError = Mathf.Max(maxError, Mathf.Abs(a.Foam - b.Foam), Mathf.Abs(a.Moisture - b.Moisture));
            }
            Assert.LessOrEqual(maxError, 0.5f / 255f + 1e-4f, "Même résultat qu'une correction d'un seul bloc.");
            guestSync.Unregister();
            hostSync.Unregister();
            UnityEngine.Object.Destroy(copy);
        }

        // ------------------------------------------------------------------ Session prolongée

        static (int objects, int systems, int textures, int meshes, int materials, long managed) Census()
        {
            GC.Collect();
            return (UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length,
                UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length,
                Resources.FindObjectsOfTypeAll<Texture2D>().Length,
                Resources.FindObjectsOfTypeAll<Mesh>().Length,
                Resources.FindObjectsOfTypeAll<Material>().Length,
                GC.GetTotalMemory(true));
        }

        [UnityTest]
        public IEnumerator LongCleaningSession_KeepsObjectsParticlesTexturesAndMemoryBounded()
        {
            var tools = game.ToolController;
            var systems = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include);
            int particleBudget = 0;
            foreach (var system in systems) particleBudget += system.main.maxParticles;

            // Un premier cycle « à vide » : les caches (matériaux, textures du premier tapis) existent ensuite.
            TestRugs.LoadDefault(game);
            yield return null;
            var before = Census();
            int peakParticles = 0;
            for (int cycle = 0; cycle < 4; cycle++)
            {
                game.Rug.LoadRug(game.AvailableRugs[cycle % game.AvailableRugs.Count]);
                foreach (var (slot, seconds) in new[] { (6, 1.5f), (4, 1.5f), (5, 0.8f), (1, 0.6f) })
                {
                    Assert.IsTrue(tools.SelectSlot(slot));
                    tools.AutomationUseOverride = true;
                    for (float t = 0f; t < seconds; t += Time.deltaTime)
                    {
                        AimAtRugCenter(new Vector2(0.3f + 0.4f * Mathf.PingPong(t, 1f), 0.5f));
                        tools.Equipment.Water.Refill();
                        tools.Equipment.Soap.Refill();
                        tools.Equipment.Recovery.Empty();
                        int alive = 0;
                        foreach (var system in systems) alive += system.particleCount;
                        peakParticles = Mathf.Max(peakParticles, alive);
                        yield return null;
                    }
                    tools.AutomationUseOverride = false;
                }
                game.Rug.Unload();
                yield return null;
                game.Rug.LoadRug(game.AvailableRugs[0]);
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            var after = Census();
            Debug.Log($"[RugWash] Session prolongée : objets {before.objects}→{after.objects}, systèmes {before.systems}→{after.systems}, " +
                      $"textures {before.textures}→{after.textures}, maillages {before.meshes}→{after.meshes}, matériaux {before.materials}→{after.materials}, " +
                      $"mémoire gérée {(after.managed - before.managed) / 1048576f:+0.0;-0.0} Mo, pic de particules {peakParticles} / {particleBudget}.");
            Assert.AreEqual(before.objects, after.objects, "Aucun objet créé ou oublié.");
            Assert.AreEqual(before.systems, after.systems, "Aucun système de particules en plus (effets réutilisés).");
            Assert.LessOrEqual(after.textures, before.textures, "Aucune texture qui s'accumule.");
            Assert.LessOrEqual(after.meshes, before.meshes, "Aucun maillage qui s'accumule (tapis rechargés).");
            Assert.LessOrEqual(after.materials, before.materials, "Aucun matériau qui s'accumule.");
            Assert.Less(after.managed - before.managed, 8L * 1048576L, "Mémoire gérée stable.");
            Assert.LessOrEqual(peakParticles, particleBudget, "Particules toujours bornées par le budget.");
        }
    }
}
