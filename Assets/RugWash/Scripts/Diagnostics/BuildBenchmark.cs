using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RugWash.Diagnostics
{
    /// <summary>
    /// Vérification automatique d'un build (argument « -rugwash-benchmark &lt;rapport.json&gt; ») :
    /// parcours réel menu → atelier → commande → livraison → sauvegarde → changement de résolution,
    /// et mesure des temps de frame dans plusieurs scénarios. Sans l'argument, rien ne se passe.
    /// La sauvegarde est redirigée vers un dossier temporaire : celle du joueur n'est jamais touchée.
    /// </summary>
    public static class BuildBenchmark
    {
        public const string Argument = "-rugwash-benchmark";

        public static string ReportPath { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Argument);
            if (index < 0) return;
            ReportPath = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "benchmark.json");

            string sandbox = Path.Combine(Application.temporaryCachePath, "benchmark-save");
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
            Directory.CreateDirectory(sandbox);
            SaveManager.DirectoryOverride = sandbox;

            var runner = new GameObject("[BuildBenchmark]");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<BuildBenchmarkRunner>();
        }
    }

    sealed class BuildBenchmarkRunner : MonoBehaviour
    {
        readonly struct Check
        {
            public readonly string Name;
            public readonly bool Passed;
            public readonly string Detail;

            public Check(string name, bool passed, string detail)
            {
                Name = name;
                Passed = passed;
                Detail = detail;
            }
        }

        sealed class Scenario
        {
            public string Name;
            public string Resolution;
            public readonly List<float> Frames = new List<float>();
            public readonly List<float> GpuMs = new List<float>();
            /// <summary>Temps CPU par système (ms cumulées) et cellules traitées, sur tout le scénario.</summary>
            public readonly double[] SlotMs = new double[(int)PerfStats.Slot.Count];
            public readonly long[] SlotProcessed = new long[(int)PerfStats.Slot.Count];
            public int GcCollections;
            public int MaxParticles;
        }

        /// <summary>État des ressources de la scène (fuites : objets, textures, maillages, mémoire).</summary>
        readonly struct Census
        {
            public readonly int GameObjects, ParticleSystems, Textures, Meshes, Materials;
            public readonly long ManagedBytes, NativeBytes;

            public Census(int gameObjects, int particleSystems, int textures, int meshes, int materials, long managed, long native)
            {
                GameObjects = gameObjects;
                ParticleSystems = particleSystems;
                Textures = textures;
                Meshes = meshes;
                Materials = materials;
                ManagedBytes = managed;
                NativeBytes = native;
            }

            public override string ToString() =>
                $"{GameObjects} objets, {ParticleSystems} systèmes de particules, {Textures} textures, {Meshes} maillages, {Materials} matériaux, " +
                $"mémoire gérée {ManagedBytes / 1048576f:0.0} Mo, native {NativeBytes / 1048576f:0.0} Mo";
        }

        static Census TakeCensus()
        {
            GC.Collect();
            return new Census(
                FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length,
                FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length,
                Resources.FindObjectsOfTypeAll<Texture2D>().Length,
                Resources.FindObjectsOfTypeAll<Mesh>().Length,
                Resources.FindObjectsOfTypeAll<Material>().Length,
                GC.GetTotalMemory(true),
                UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
        }

        readonly List<string> census = new List<string>();

        readonly List<Check> checks = new List<Check>();
        readonly List<Scenario> scenarios = new List<Scenario>();
        string fatal;

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            yield return Run();
            WriteReport();
            Application.Quit(fatal == null && checks.TrueForAll(c => c.Passed) ? 0 : 1);
        }

        void Record(string name, bool passed, string detail = "")
        {
            checks.Add(new Check(name, passed, detail));
            Debug.Log($"[RugWash Benchmark] {(passed ? "OK" : "ÉCHEC")} {name} {detail}");
        }

        IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Run()
        {
            // 1. Menu principal.
            yield return WaitFor(() => FindAnyObjectByType<MainMenuController>() != null, 20f);
            var menu = FindAnyObjectByType<MainMenuController>();
            Record("Menu principal chargé", menu != null, SceneManager.GetActiveScene().name);
            if (menu == null) { fatal = "menu principal introuvable"; yield break; }
            yield return new WaitForSeconds(1f);
            Record("« Continuer » désactivé sans sauvegarde", !menu.ContinueButton.interactable);

            // 2. Nouvelle partie → atelier.
            menu.PlayButton.onClick.Invoke();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "Workshop" && FindAnyObjectByType<GameBootstrap>() != null, 30f);
            var game = FindAnyObjectByType<GameBootstrap>();
            yield return null;
            Record("Atelier chargé et câblé", game != null && game.IsReady);
            if (game == null || !game.IsReady) { fatal = "atelier non prêt"; yield break; }

            // 3. Commande acceptée.
            var order = game.Orders.Book.Orders[0];
            var start = game.Orders.TryStart(order);
            Record("Commande acceptée", start == OrderStartResult.Started, order.title);
            yield return new WaitForSeconds(3f);

            // 4. Mesures (vSync désactivé : capacité réelle de la machine).
            var player = game.PlayerController;
            var tools = game.ToolController;
            player.Teleport(new Vector3(0f, 0.06f, -2.3f), 0f, 22f);
            yield return Measure("Atelier au repos (vue d'ensemble)", 5f, null);

            yield return Measure("Rinçage intensif balayant le tapis", 10f, t =>
            {
                AimAt(game, new Vector2(0.15f + 0.7f * Mathf.PingPong(t * 0.25f, 1f), 0.5f + 0.3f * Mathf.Sin(t)));
                tools.SelectSlot(4);
                tools.AutomationUseOverride = true;
                tools.Equipment.Water.Refill();
            });

            yield return Measure("Savon puis brossage", 8f, t =>
            {
                AimAt(game, new Vector2(0.3f + 0.4f * Mathf.PingPong(t * 0.2f, 1f), 0.45f));
                tools.SelectSlot(t % 2f < 1f ? 2 : 3);
                tools.AutomationUseOverride = true;
                tools.Equipment.Soap.Refill();
            });
            tools.AutomationUseOverride = false;

            yield return Measure("Tour complet de l'atelier", 8f, t =>
                player.Teleport(new Vector3(0f, 0.06f, -2.3f), t / 8f * 360f, 8f));

            // Mousse et eau (jalon 2 v0.1.2) : production de mousse sur tout le tapis, mousse au repos, puis rinçage.
            yield return Measure("Canon à mousse balayant le tapis", 10f, t =>
            {
                AimAt(game, new Vector2(0.12f + 0.76f * Mathf.PingPong(t * 0.3f, 1f), 0.2f + 0.6f * Mathf.PingPong(t * 0.11f, 1f)));
                tools.SelectSlot(6);
                tools.AutomationUseOverride = true;
                tools.Equipment.Soap.Refill();
            });
            tools.AutomationUseOverride = false;
            yield return Measure("Mousse au repos (tapis couvert)", 6f, t => AimAt(game, new Vector2(0.5f, 0.5f)));
            yield return Measure("Rinçage de la mousse", 8f, t =>
            {
                AimAt(game, new Vector2(0.12f + 0.76f * Mathf.PingPong(t * 0.3f, 1f), 0.25f + 0.5f * Mathf.PingPong(t * 0.13f, 1f)));
                tools.SelectSlot(4);
                tools.AutomationUseOverride = true;
                tools.Equipment.Water.Refill();
            });
            tools.AutomationUseOverride = false;

            // 5. Nettoyage complet et livraison → paiement et sauvegarde.
            CleanThoroughly(game.Rug.Grid);
            yield return null;
            var result = game.Orders.Deliver();
            yield return null;
            Record("Livraison évaluée et payée", result != null && result.Accepted && game.Wallet.Balance == result.Reward,
                result == null ? "aucun résultat" : $"note {OrderEvaluator.GradeLabel(result.Grade)}, {result.Reward} €");
            string savePath = game.SaveSystem.FilePath;
            Record("Sauvegarde écrite", File.Exists(savePath), savePath);
            Record("Écran de résultats affiché", game.Results.IsVisible);
            game.Results.BackToWorkshop();
            yield return null;

            // 5 bis. Session prolongée : plusieurs commandes enchaînées (mousse, brosse, rinçage, extraction, livraison).
            // Le nombre d'objets, de textures, de maillages et la mémoire ne doivent pas croître d'une commande à l'autre.
            yield return LongSession(game);

            // 6. Résolution et mode d'affichage (non vérifiables dans l'éditeur).
            var settings = game.Settings;
            settings.SetDisplayMode(FullScreenMode.Windowed);
            settings.SetResolution(new Vector2Int(1280, 720));
            yield return new WaitForSeconds(1f);
            Record("Résolution 1280×720 appliquée", Screen.width == 1280 && Screen.height == 720, $"{Screen.width}×{Screen.height}");
            Record("Mode fenêtré appliqué", Screen.fullScreenMode == FullScreenMode.Windowed, Screen.fullScreenMode.ToString());
            settings.SetResolution(new Vector2Int(1920, 1080));
            yield return new WaitForSeconds(1f);
            // Une fenêtre 1920×1080 peut être réduite par la barre des tâches sur un écran 1080p : on vérifie le changement effectif.
            Record("Retour à la grande résolution", Screen.width > 1280 && Screen.height > 720, $"{Screen.width}×{Screen.height}");

            // 7. Retour au menu : la progression est reprise.
            FindAnyObjectByType<PauseMenuUI>().ReturnToMainMenu();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu" && FindAnyObjectByType<MainMenuController>() != null, 30f);
            yield return new WaitForSeconds(0.5f);
            menu = FindAnyObjectByType<MainMenuController>();
            Record("« Continuer » disponible après la partie", menu != null && menu.ContinueButton.interactable);
        }

        static void AimAt(GameBootstrap game, Vector2 uv)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, pitch);
        }

        readonly FrameTiming[] timings = new FrameTiming[1];
        Scenario current;

        Scenario BeginScenario(string name)
        {
            var scenario = new Scenario { Name = name, Resolution = $"{Screen.width}x{Screen.height} {Screen.fullScreenMode}" };
            scenarios.Add(scenario);
            Debug.Log($"[RugWash Benchmark] Scénario : {name}");
            PerfStats.Reset();
            PerfStats.Enabled = true;
            scenario.GcCollections = GC.CollectionCount(0);
            current = scenario;
            return scenario;
        }

        /// <summary>Une image mesurée : durée, temps GPU, particules vivantes (échantillonnées).</summary>
        void SampleFrame(Scenario scenario, float dt)
        {
            scenario.Frames.Add(dt);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) scenario.GpuMs.Add((float)timings[0].gpuFrameTime);
            if (scenario.Frames.Count % 30 == 0)
            {
                int alive = 0;
                foreach (var system in FindObjectsByType<ParticleSystem>()) alive += system.particleCount;
                scenario.MaxParticles = Mathf.Max(scenario.MaxParticles, alive);
            }
        }

        void EndScenario(Scenario scenario)
        {
            for (int i = 0; i < (int)PerfStats.Slot.Count; i++)
            {
                scenario.SlotMs[i] = PerfStats.Milliseconds((PerfStats.Slot)i);
                scenario.SlotProcessed[i] = PerfStats.Processed((PerfStats.Slot)i);
            }
            scenario.GcCollections = GC.CollectionCount(0) - scenario.GcCollections;
            PerfStats.Enabled = false;
            current = null;
        }

        IEnumerator Measure(string name, float seconds, Action<float> step)
        {
            var scenario = BeginScenario(name);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                step?.Invoke(elapsed);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                SampleFrame(scenario, dt);
            }
            EndScenario(scenario);
        }

        /// <summary>Actions chronométrées d'une session (sans recensement pendant la mesure).</summary>
        IEnumerator Phase(float seconds, Action<float> step)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                step?.Invoke(elapsed);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                if (current != null) SampleFrame(current, dt);
            }
        }

        IEnumerator LongSession(GameBootstrap game)
        {
            var tools = game.ToolController;
            var before = TakeCensus();
            census.Add($"Avant la session : {before}");
            var scenario = BeginScenario("Session prolongée (3 commandes : mousse, brosse, rinçage, extraction, livraison)");
            int delivered = 0;
            for (int round = 0; round < 3; round++)
            {
                OrderDefinition order = null;
                foreach (var candidate in game.Orders.Book.Orders)
                    if (game.Orders.Book.CanStart(candidate, game.Orders.BoardReputation) == OrderStartResult.Started) { order = candidate; break; }
                if (order == null) break;
                // Raccourci de mesure : l'aire de lavage est remise propre entre deux commandes (seuil de 25 %).
                CleanThoroughly(game.WorkSurface.Surface.Grid);
                game.WorkSurface.RefreshStatus(force: true);
                if (game.Orders.TryStart(order) != OrderStartResult.Started) break;
                yield return Phase(8f, t =>
                {
                    AimAt(game, new Vector2(0.15f + 0.7f * Mathf.PingPong(t * 0.3f, 1f), 0.3f + 0.4f * Mathf.PingPong(t * 0.12f, 1f)));
                    tools.SelectSlot(6);
                    tools.AutomationUseOverride = true;
                    tools.Equipment.Soap.Refill();
                });
                yield return Phase(4f, t =>
                {
                    AimAt(game, new Vector2(0.3f + 0.4f * Mathf.PingPong(t * 0.25f, 1f), 0.5f));
                    tools.SelectSlot(3);
                });
                yield return Phase(8f, t =>
                {
                    AimAt(game, new Vector2(0.15f + 0.7f * Mathf.PingPong(t * 0.3f, 1f), 0.3f + 0.4f * Mathf.PingPong(t * 0.12f, 1f)));
                    tools.SelectSlot(4);
                    tools.Equipment.Water.Refill();
                });
                yield return Phase(4f, t =>
                {
                    AimAt(game, new Vector2(0.3f + 0.4f * Mathf.PingPong(t * 0.25f, 1f), 0.5f));
                    tools.SelectSlot(5);
                    tools.Equipment.Recovery.Empty();
                });
                tools.AutomationUseOverride = false;
                CleanThoroughly(game.Rug.Grid);
                yield return null;
                if (game.Orders.Deliver() != null) delivered++;
                yield return Phase(0.5f, null);
                game.Results.BackToWorkshop();
                yield return Phase(0.5f, null);
            }
            EndScenario(scenario);
            var after = TakeCensus();
            census.Add($"Après {delivered} commandes : {after}");
            Record("Session prolongée : 3 commandes enchaînées", delivered == 3, $"{delivered} livrée(s)");
            Record("Session prolongée : aucun objet, système de particules, texture ou maillage en plus",
                after.GameObjects == before.GameObjects && after.ParticleSystems == before.ParticleSystems
                && after.Textures <= before.Textures && after.Meshes <= before.Meshes && after.Materials <= before.Materials,
                $"objets {before.GameObjects}→{after.GameObjects}, particules {before.ParticleSystems}→{after.ParticleSystems}, " +
                $"textures {before.Textures}→{after.Textures}, maillages {before.Meshes}→{after.Meshes}, matériaux {before.Materials}→{after.Materials}");
            long growth = after.ManagedBytes - before.ManagedBytes;
            Record("Session prolongée : mémoire gérée stable (croissance < 8 Mo)", growth < 8L * 1048576L, $"{growth / 1048576f:+0.0;-0.0} Mo");
        }

        static void CleanThoroughly(RugCleaningGrid grid)
        {
            var table = new DirtTypeTable();
            for (int i = 0; i < 60; i++)
            {
                var soap = new SoapOperation { FoamPerSecond = 1.4f, DeltaTime = 0.05f };
                var brush = new BrushOperation { Table = table, Power = 0.6f, DeltaTime = 0.1f };
                var rinse = new RinseOperation { Table = table, Power = 0.9f, WettingPerSecond = 0.9f, DeltaTime = 0.1f };
                if (i < 5) grid.ApplyAll(ref soap);
                grid.ApplyAll(ref brush);
                grid.ApplyAll(ref rinse);
            }
            for (int i = 0; i < 40; i++)
            {
                var extract = new ExtractOperation { Power = 0.9f, DeltaTime = 0.1f };
                grid.ApplyAll(ref extract);
            }
        }

        void WriteReport()
        {
            var inv = CultureInfo.InvariantCulture;
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"unityVersion\": \"{Application.unityVersion}\",\n");
            json.Append($"  \"gpu\": \"{Escape(SystemInfo.graphicsDeviceName)}\",\n");
            json.Append($"  \"graphicsApi\": \"{SystemInfo.graphicsDeviceType}\",\n");
            json.Append($"  \"cpu\": \"{Escape(SystemInfo.processorType)}\",\n");
            json.Append($"  \"memoryMB\": {SystemInfo.systemMemorySize},\n");
            json.Append($"  \"quality\": \"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\",\n");
            json.Append($"  \"fatal\": {(fatal == null ? "null" : $"\"{Escape(fatal)}\"")},\n");
            json.Append("  \"scenarios\": [\n");
            for (int i = 0; i < scenarios.Count; i++)
            {
                var frames = scenarios[i].Frames;
                frames.RemoveAt(0);
                var sorted = new List<float>(frames);
                sorted.Sort();
                float total = 0f;
                foreach (float f in frames) total += f;
                float avgFps = frames.Count / Mathf.Max(1e-6f, total);
                float p99 = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, sorted.Count - 1)];
                float worst = sorted[sorted.Count - 1];
                var scenario = scenarios[i];
                float gpu = 0f;
                foreach (float g in scenario.GpuMs) gpu += g;
                gpu = scenario.GpuMs.Count > 0 ? gpu / scenario.GpuMs.Count : -1f;
                var slots = new StringBuilder();
                for (int s = 0; s < (int)PerfStats.Slot.Count; s++)
                {
                    if (s > 0) slots.Append(", ");
                    slots.Append(string.Format(inv, "\"{0}\": {{ \"msPerFrame\": {1:0.000}, \"processedPerFrame\": {2:0} }}",
                        (PerfStats.Slot)s, scenario.SlotMs[s] / Mathf.Max(1, frames.Count), scenario.SlotProcessed[s] / (double)Mathf.Max(1, frames.Count)));
                }
                json.Append(string.Format(inv,
                    "    {{ \"name\": \"{0}\", \"resolution\": \"{7}\", \"frames\": {1}, \"avgFps\": {2:0.0}, \"onePercentLowFps\": {3:0.0}, \"avgFrameMs\": {4:0.00}, \"worstFrameMs\": {5:0.00}, " +
                    "\"avgGpuMs\": {8:0.00}, \"gcCollections\": {9}, \"maxParticles\": {10}, \"systems\": {{ {11} }} }}{6}\n",
                    Escape(scenario.Name), frames.Count, avgFps, 1f / p99, total / frames.Count * 1000f, worst * 1000f, i < scenarios.Count - 1 ? "," : "",
                    scenario.Resolution, gpu, scenario.GcCollections, scenario.MaxParticles, slots));
            }
            json.Append("  ],\n  \"census\": [\n");
            for (int i = 0; i < census.Count; i++)
                json.Append($"    \"{Escape(census[i])}\"{(i < census.Count - 1 ? "," : "")}\n");
            json.Append("  ],\n  \"checks\": [\n");
            for (int i = 0; i < checks.Count; i++)
                json.Append($"    {{ \"name\": \"{Escape(checks[i].Name)}\", \"passed\": {(checks[i].Passed ? "true" : "false")}, \"detail\": \"{Escape(checks[i].Detail)}\" }}{(i < checks.Count - 1 ? "," : "")}\n");
            json.Append("  ]\n}\n");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(BuildBenchmark.ReportPath)));
            File.WriteAllText(BuildBenchmark.ReportPath, json.ToString(), new UTF8Encoding(false));
        }

        static string Escape(string text) => (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
