using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Diagnostics (V0.2.5), lancés à la main seulement (<c>-testFilter FirstUseDiagnostics</c>) : ce ne sont pas des mesures
    /// de performance, mais des relevés de ce que le moteur fait au premier contact d'un outil avec un tapis jamais nettoyé
    /// (marqueurs du profileur qui n'apparaissent qu'à ce moment : création de programmes de shader, chargements,
    /// instanciations, allocations), et de l'effet réel de chaque niveau d'outil sur un même tapis.
    /// </summary>
    [Explicit("Diagnostic lancé à la main (-testFilter FirstUseDiagnostics)"), Category("Diagnostic")]
    public class FirstUseDiagnosticsPlayModeTests
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
            yield return null;
        }

        sealed class Marker
        {
            public string Name;
            public ProfilerRecorder Recorder;
            public double First, Later;
            public long FirstCount, LaterCount;
        }

        static List<Marker> StartMarkers()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var markers = new List<Marker>();
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                var recorder = new ProfilerRecorder(description.Category, description.Name, 1, ProfilerRecorderOptions.Default);
                recorder.Start();
                markers.Add(new Marker { Name = $"{description.Category.Name}/{description.Name}", Recorder = recorder });
            }
            return markers;
        }

        static void Sample(List<Marker> markers, bool first)
        {
            foreach (var marker in markers)
            {
                if (!marker.Recorder.Valid) continue;
                long value = marker.Recorder.LastValue;
                if (value <= 0) continue;
                if (first)
                {
                    marker.First += value / 1e6;
                    marker.FirstCount++;
                }
                else
                {
                    marker.Later += value / 1e6;
                    marker.LaterCount++;
                }
            }
        }

        [UnityTest]
        public IEnumerator FirstContact_WhatTheEngineDoes()
        {
#if UNITY_EDITOR
            // Comme dans le jeu compilé : un shader jamais utilisé est préparé au moment où il sert (pas d'attente asynchrone).
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            var tools = game.ToolController;
            Player.PlayerInventory.AutomationCarriesOnSelect = true;
            var report = new StringBuilder("[RugWash Diagnostic] Premier contact d'un outil avec un tapis jamais nettoyé\n");
            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(order));
            yield return new WaitForSeconds(1f);
            var markers = StartMarkers();
            report.AppendLine($"{markers.Count} marqueurs suivis.");
            foreach (string id in new[] { "bucket", "soap", "rotary", "rinse", "brush", "vacuum", "foam_cannon" })
            {
                CleaningToolBase tool = null;
                foreach (var candidate in tools.Tools)
                    if (candidate.Definition.id == id) tool = candidate;
                if (tool == null) continue;
                // Tapis neuf (jamais nettoyé) à chaque outil.
                game.Rug.LoadRug(order.rug, order.dirt, order.dirtSeed + id.GetHashCode());
                tools.Inventory.CarryForAutomation(tool);
                tools.SelectIndex(IndexOf(tools.Tools, tool));
                tools.Equipment.Water.Refill();
                tools.Equipment.Soap.Refill();
                Vector3 target = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
                float pitch = Mathf.Atan2(1.68f - target.y, 1.0f) * Mathf.Rad2Deg;
                game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.0f), 0f, pitch);
                yield return new WaitForSeconds(0.6f);
                foreach (var marker in markers) marker.First = marker.Later = marker.FirstCount = marker.LaterCount = 0;

                var frames = new List<float>();
                tools.AutomationUseOverride = true;
                for (int frame = 0; frame < 90; frame++)
                {
                    float t = frame / 90f;
                    target = game.Rug.UVToWorld(new Vector2(0.35f + 0.3f * Mathf.PingPong(t * 4f, 1f), 0.5f));
                    game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.0f), 0f, pitch);
                    var started = System.Diagnostics.Stopwatch.StartNew();
                    yield return null;
                    frames.Add((float)started.Elapsed.TotalMilliseconds);
                    if (frame < 20) Sample(markers, first: true);
                    else if (frame >= 60 && frame < 80) Sample(markers, first: false);
                }
                tools.AutomationUseOverride = false;
                yield return new WaitForSeconds(0.3f);

                float worstFirst = 0f, worstLater = 0f;
                for (int i = 0; i < frames.Count; i++)
                    if (i < 20) worstFirst = Mathf.Max(worstFirst, frames[i]);
                    else if (i >= 60) worstLater = Mathf.Max(worstLater, frames[i]);
                report.AppendLine($"== {id} (outil actif : {tools.ActiveTool == tool}) : pire image des 20 premières {worstFirst:0.0} ms, ensuite {worstLater:0.0} ms");
                markers.Sort((a, b) => (b.First - b.Later).CompareTo(a.First - a.Later));
                int shown = 0;
                foreach (var marker in markers)
                {
                    if (shown >= 18 || marker.First - marker.Later < 0.5) break;
                    report.AppendLine($"   {marker.Name} : {marker.First:0.00} ms ({marker.FirstCount} images) puis {marker.Later:0.00} ms ({marker.LaterCount})");
                    shown++;
                }
            }
            foreach (var marker in markers) marker.Recorder.Dispose();
            Player.PlayerInventory.AutomationCarriesOnSelect = false;
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = true;
#endif
            Debug.Log(report.ToString());
        }

        static int IndexOf(IReadOnlyList<CleaningToolBase> list, CleaningToolBase tool)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == tool) return i;
            return -1;
        }

        /// <summary>Même tapis, même trajet : ce que fait chaque outil aux niveaux 1, 2 et 3 (doré).</summary>
        [UnityTest]
        public IEnumerator ToolLevels_MeasuredOnTheSameRug()
        {
            var report = new StringBuilder("[RugWash Diagnostic] Effet des niveaux d'outil (même tapis, même trajet, 4 s)\n");
            var order = game.Orders.Book.Orders[0];
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(order));
            yield return null;
            var levels = new Dictionary<string, string[]>
            {
                { "vacuum", new[] { "up_tool_vacuum", "up_vacuum", "up_vacuum_lvl2", "up_vacuum_lvl3" } },
                { "brush", new[] { "up_brush", "up_brush_lvl2", "up_brush_lvl3" } },
                { "rinse", new[] { "up_tool_rinse", "up_rinse", "up_rinse_lvl2", "up_rinse_lvl3" } },
                { "soap", new[] { "up_soap_power", "up_soap_lvl2", "up_soap_lvl3" } },
                { "rotary", new[] { "up_tool_rotary", "up_rotary_power", "up_rotary_lvl2", "up_rotary_lvl3" } },
            };
            var baseOwned = new List<string>(game.Upgrades.OwnedIds);
            foreach (var pair in levels)
            {
                CleaningToolBase tool = null;
                foreach (var candidate in game.ToolController.Tools)
                    if (candidate.Definition.id == pair.Key) tool = candidate;
                if (tool == null) continue;
                bool unlockFirst = pair.Value[0].StartsWith("up_tool_");
                int first = unlockFirst ? 1 : 0;
                for (int level = 0; level <= pair.Value.Length - first; level++)
                {
                    var owned = new List<string>(baseOwned);
                    if (unlockFirst) owned.Add(pair.Value[0]);
                    for (int i = 0; i < level; i++) owned.Add(pair.Value[first + i]);
                    game.Upgrades.Restore(owned);
                    game.Rug.LoadRug(order.rug, order.dirt, 4242);
                    var grid = game.Rug.Grid;
                    // Préparation identique : fibres mouillées et savonneuses pour la brosse et la monobrosse.
                    if (pair.Key == "brush" || pair.Key == "rotary")
                    {
                        var wet = new DepositOperation { Water = 0.5f, Foam = 0.4f };
                        grid.ApplyRect(new RectInt(0, 0, grid.Width, grid.Height), 1f, ref wet);
                    }
                    var before = grid.ComputeSummary();
                    float dt = 1f / 60f;
                    for (int frame = 0; frame < 240; frame++)
                    {
                        float t = frame / 240f;
                        Vector3 point = game.Rug.UVToWorld(new Vector2(0.2f + 0.6f * Mathf.PingPong(t * 6f, 1f), 0.3f + 0.4f * t));
                        Vector3 up = game.Rug.transform.up;
                        game.ToolController.Equipment.Water.Refill();
                        game.ToolController.Equipment.Soap.Refill();
                        tool.Tick(true, new ToolAim { HasHit = true, Point = point, Normal = up, Distance = 0.8f, Rug = game.Rug, Ray = new Ray(point + up, -up) }, dt);
                    }
                    tool.StopUsing();
                    var after = grid.ComputeSummary();
                    report.AppendLine($"{pair.Key} niveau {tool.Level}{(tool.IsGolden ? " (doré)" : string.Empty)} : puissance ×{tool.PowerMultiplier:0.00} · saleté {before.RemainingDirt:0.0} → {after.RemainingDirt:0.0}" +
                                      $" · propreté {after.Cleanliness01 * 100f:0.0} % · mousse {after.AverageFoam01:0.000} · résidus {after.AverageResidue01:0.000} · humidité {after.AverageMoisture01:0.000}");
                    yield return null;
                }
            }
            game.Upgrades.Restore(baseOwned);
            Debug.Log(report.ToString());
        }
    }
}
