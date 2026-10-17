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
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RugWash.Diagnostics
{
    /// <summary>
    /// Vérification automatique d'un build (argument « -rugwash-benchmark &lt;rapport.json&gt; ») :
    /// parcours réel menu → atelier → commande → livraison → sauvegarde → changement de résolution,
    /// et mesure des temps de frame dans plusieurs scénarios. Sans l'argument, rien ne se passe.
    /// La sauvegarde est redirigée vers un dossier temporaire : celle du joueur n'est jamais touchée.
    /// Options (mission de corrections, protocole de mesure) :
    /// « -rugwash-benchmark-mode full|foam|graphics|quick » (parcours complet par défaut ; diagnostic de la mousse ;
    /// balayage des réglages graphiques ; scénarios représentatifs pour comparer les niveaux de qualité),
    /// « -rugwash-benchmark-quality &lt;n&gt; » (niveau de qualité imposé), « -rugwash-benchmark-renderscale &lt;x&gt; »
    /// (multiplie l'échelle de rendu du niveau : 2 = quatre fois plus de pixels, pour approcher une carte graphique moins
    /// puissante ; plafonnée à 2 par l'URP),
    /// « -rugwash-benchmark-profile &lt;dossier&gt; » (build Development : capture du Profiler Unity par scénario).
    /// </summary>
    public static class BuildBenchmark
    {
        public const string Argument = "-rugwash-benchmark";
        public const string ModeArgument = "-rugwash-benchmark-mode";
        public const string QualityArgument = "-rugwash-benchmark-quality";
        public const string RenderScaleArgument = "-rugwash-benchmark-renderscale";
        public const string ProfileArgument = "-rugwash-benchmark-profile";

        public static string ReportPath { get; private set; }
        public static string Mode { get; private set; } = "full";
        public static int Quality { get; private set; } = -1;
        public static float RenderScale { get; private set; }
        public static string ProfileDirectory { get; private set; }

        static string ValueOf(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Argument);
            if (index < 0) return;
            ReportPath = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "benchmark.json");
            Mode = (ValueOf(args, ModeArgument) ?? "full").ToLowerInvariant();
            if (int.TryParse(ValueOf(args, QualityArgument), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality)) Quality = quality;
            if (float.TryParse(ValueOf(args, RenderScaleArgument), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)) RenderScale = scale;
            ProfileDirectory = ValueOf(args, ProfileArgument);

            string sandbox = Path.Combine(Application.temporaryCachePath, "benchmark-save");
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
            Directory.CreateDirectory(sandbox);
            SaveManager.DirectoryOverride = sandbox;
            // Les scénarios choisissent leurs outils directement (inventaire à 4 emplacements contourné pour la mesure).
            Player.PlayerInventory.AutomationCarriesOnSelect = true;

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
            /// <summary>Temps CPU du fil principal et du fil de rendu (FrameTimingManager), en ms.</summary>
            public readonly List<float> MainMs = new List<float>();
            public readonly List<float> RenderMs = new List<float>();
            /// <summary>Compteurs du profileur (rendu, allocations) : somme et nombre d'échantillons ; -1 si indisponibles.</summary>
            public double Batches, SetPass, DrawCalls, GcAlloc;
            public int CounterSamples;
            public bool CountersValid, GcValid;
            /// <summary>Croissance du tas géré entre deux images (estimation des allocations quand le compteur manque).</summary>
            public double HeapGrowth;
            public string ProfileFile;
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

        ProfilerRecorder batchesRecorder, setPassRecorder, drawCallsRecorder, gcRecorder;
        long lastHeap;

        void OnEnable() => StartRecorders();

        /// <summary>
        /// Compteurs du profileur. Relancés au début de chaque scénario s'ils n'étaient pas encore disponibles (un compteur
        /// de rendu n'existe qu'après le premier rendu) ; certains n'existent que dans un build Development.
        /// </summary>
        void StartRecorders()
        {
            Restart(ref batchesRecorder, ProfilerCategory.Render, "Batches Count");
            Restart(ref setPassRecorder, ProfilerCategory.Render, "SetPass Calls Count");
            Restart(ref drawCallsRecorder, ProfilerCategory.Render, "Draw Calls Count");
            Restart(ref gcRecorder, ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        static void Restart(ref ProfilerRecorder recorder, ProfilerCategory category, string name)
        {
            if (recorder.Valid) return;
            recorder.Dispose();
            recorder = ProfilerRecorder.StartNew(category, name);
        }

        void OnDisable()
        {
            batchesRecorder.Dispose();
            setPassRecorder.Dispose();
            drawCallsRecorder.Dispose();
            gcRecorder.Dispose();
        }

        /// <summary>Niveau de qualité et échelle de rendu imposés par la ligne de commande (protocole de mesure).</summary>
        static void ApplyGraphicsOverrides()
        {
            if (BuildBenchmark.Quality >= 0)
                QualitySettings.SetQualityLevel(Mathf.Clamp(BuildBenchmark.Quality, 0, QualitySettings.names.Length - 1), true);
            // Après le niveau de qualité : chaque niveau porte son propre réglage de synchronisation verticale.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (BuildBenchmark.RenderScale > 0f && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale *= BuildBenchmark.RenderScale;
        }

        IEnumerator Start()
        {
            ApplyGraphicsOverrides();
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
            // Emplacement Solo 1 (vide dans le dossier temporaire du benchmark) : nouvelle partie.
            menu.PlaySlot(new SaveSlot(SaveMode.Solo, 1));
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "Workshop" && FindAnyObjectByType<GameBootstrap>() != null, 30f);
            var game = FindAnyObjectByType<GameBootstrap>();
            yield return null;
            Record("Atelier chargé et câblé", game != null && game.IsReady);
            if (game == null || !game.IsReady) { fatal = "atelier non prêt"; yield break; }
            Record("Nouvelle partie : seulement le kit de départ (brosse, détergent, seau)", game.Upgrades.OwnedIds.Count == 0,
                $"{game.Upgrades.OwnedIds.Count} amélioration(s)");
            // Banc d'essai : les outils mesurés (lance, extracteur, canon à mousse…) sont débloqués sans achat.
            var owned = new List<string>(game.Upgrades.OwnedIds);
            owned.AddRange(SaveMigration.LegacyToolUnlocks);
            game.Upgrades.Restore(owned);

            // 3. Commande acceptée.
            var order = game.Orders.Book.Orders[0];
            var start = game.Orders.TryStart(order);
            Record("Commande acceptée", start == OrderStartResult.Started, order.title);
            yield return new WaitForSeconds(3f);

            switch (BuildBenchmark.Mode)
            {
                case "foam": yield return FoamDiagnosis(game); yield break;
                case "graphics": yield return GraphicsSweep(game); yield break;
                case "quick": yield return QuickSet(game); yield break;
            }

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

            // Mission de corrections (2.10) : quantités de mousse contrôlées, particules isolées, vue rapprochée.
            yield return FoamDiagnosis(game);

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

        /// <summary>Mousse imposée (diagnostic) : quantité fixe, humidité au moins égale ; poids = bord d'une zone.</summary>
        struct FoamFill : ICellOperation
        {
            public float Foam, Moisture;

            public void Apply(ref RugCell cell, float weight)
            {
                cell.Foam = Foam * weight;
                cell.Moisture = Mathf.Max(cell.Moisture, Moisture * weight);
            }
        }

        /// <summary>Couvre de mousse une part des lignes de la grille (0 = retire toute la mousse).</summary>
        static void FillFoam(RugCleaningController surface, float foam, float fraction)
        {
            if (surface == null || surface.Grid == null) return;
            var grid = surface.Grid;
            var clear = new FoamFill { Foam = 0f };
            grid.ApplyRect(new RectInt(0, 0, grid.Width, grid.Height), 1f, ref clear);
            if (foam <= 0f || fraction <= 0f) return;
            var fill = new FoamFill { Foam = foam, Moisture = 0.5f };
            int rows = Mathf.Clamp(Mathf.RoundToInt(grid.Height * fraction), 1, grid.Height);
            int start = (grid.Height - rows) / 2;
            grid.ApplyRect(new RectInt(0, start, grid.Width, rows), 1f, ref fill);
        }

        /// <summary>Mousse sur les trois surfaces : tapis, aire de lavage, sol de l'atelier.</summary>
        static void FillFoamEverywhere(GameBootstrap game, float foam, float rugFraction, float floorFraction)
        {
            FillFoam(game.Rug, foam, rugFraction);
            FillFoam(game.WorkSurface != null ? game.WorkSurface.Surface : null, foam, floorFraction);
            FillFoam(game.RoomFloor, foam, floorFraction * 0.5f);
        }

        static void SetParticleRenderers(bool visible)
        {
            foreach (var renderer in FindObjectsByType<ParticleSystemRenderer>()) renderer.enabled = visible;
        }

        /// <summary>
        /// Diagnostic de la mousse (2.10) : mêmes vues avec aucune, peu et beaucoup de mousse, sur le tapis et sur les surfaces
        /// voisines ; vue rapprochée (tapis plein écran) ; canon à mousse avec et sans le rendu des particules ; canon à bout portant
        /// (particules près de la caméra) ; rinçage d'un tapis couvert (ruissellement). La mousse est remise avant chaque mesure.
        /// </summary>
        IEnumerator FoamDiagnosis(GameBootstrap game)
        {
            var player = game.PlayerController;
            var tools = game.ToolController;
            tools.AutomationUseOverride = false;
            void Overview(float t) => player.Teleport(new Vector3(0f, 0.06f, -2.3f), Mathf.Sin(t * 0.6f) * 10f, 22f);

            FillFoamEverywhere(game, 0f, 0f, 0f);
            yield return Measure("Mousse : aucune (référence, vue d'ensemble)", 5f, Overview);
            FillFoamEverywhere(game, 0.6f, 0.15f, 0f);
            yield return Measure("Mousse : peu (15 % du tapis, vue d'ensemble)", 5f, Overview);
            FillFoamEverywhere(game, 1f, 1f, 1f);
            yield return Measure("Mousse : beaucoup (tapis, aire de lavage et sol couverts, vue d'ensemble)", 5f, Overview);

            FillFoamEverywhere(game, 0f, 0f, 0f);
            yield return Measure("Mousse : aucune (vue rapprochée, tapis plein écran)", 5f, t => AimAt(game, new Vector2(0.5f, 0.5f)));
            FillFoamEverywhere(game, 1f, 1f, 1f);
            yield return Measure("Mousse : beaucoup (vue rapprochée, tapis plein écran)", 5f, t => AimAt(game, new Vector2(0.5f, 0.5f)));

            Action<float> spray = t =>
            {
                AimAt(game, new Vector2(0.12f + 0.76f * Mathf.PingPong(t * 0.3f, 1f), 0.2f + 0.6f * Mathf.PingPong(t * 0.11f, 1f)));
                tools.SelectSlot(6);
                tools.AutomationUseOverride = true;
                tools.Equipment.Soap.Refill();
            };
            FillFoamEverywhere(game, 1f, 1f, 1f);
            yield return Measure("Mousse : beaucoup + canon à mousse", 6f, spray);
            FillFoamEverywhere(game, 1f, 1f, 1f);
            SetParticleRenderers(false);
            yield return Measure("Mousse : beaucoup + canon à mousse, rendu des particules masqué (diagnostic)", 6f, spray);
            SetParticleRenderers(true);

            yield return Measure("Mousse : canon à bout portant (sol à 0,6 m, particules près de la caméra)", 6f, t =>
            {
                Vector3 target = game.Rug.UVToWorld(new Vector2(0.5f, 0.15f + 0.1f * Mathf.PingPong(t * 0.4f, 1f)));
                player.Teleport(new Vector3(target.x, 0.06f, target.z - 0.35f), 0f, 68f);
                tools.SelectSlot(6);
                tools.AutomationUseOverride = true;
                tools.Equipment.Soap.Refill();
            });
            tools.AutomationUseOverride = false;

            FillFoamEverywhere(game, 1f, 1f, 0f);
            yield return Measure("Mousse : rinçage d'un tapis couvert (ruissellement vers l'aire de lavage)", 8f, t =>
            {
                AimAt(game, new Vector2(0.12f + 0.76f * Mathf.PingPong(t * 0.3f, 1f), 0.08f + 0.2f * Mathf.PingPong(t * 0.13f, 1f)));
                tools.SelectSlot(4);
                tools.AutomationUseOverride = true;
                tools.Equipment.Water.Refill();
            });
            tools.AutomationUseOverride = false;
            FillFoamEverywhere(game, 0f, 0f, 0f);
        }

        /// <summary>Scénario de jeu typique et répétable : rinçage balayant un tapis couvert de mousse, aire de lavage visible.</summary>
        IEnumerator TypicalGameplay(GameBootstrap game, string name, float seconds)
        {
            var tools = game.ToolController;
            FillFoamEverywhere(game, 1f, 1f, 0.4f);
            yield return Measure(name, seconds, t =>
            {
                AimAt(game, new Vector2(0.12f + 0.76f * Mathf.PingPong(t * 0.3f, 1f), 0.25f + 0.5f * Mathf.PingPong(t * 0.13f, 1f)));
                tools.SelectSlot(4);
                tools.AutomationUseOverride = true;
                tools.Equipment.Water.Refill();
            });
            tools.AutomationUseOverride = false;
        }

        /// <summary>
        /// Balayage des réglages graphiques (2.11) : le même scénario de jeu, un réglage désactivé à la fois, puis tous ensemble.
        /// Les réglages sont modifiés en mémoire (rien n'est enregistré) et rétablis après chaque mesure.
        /// </summary>
        IEnumerator GraphicsSweep(GameBootstrap game)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var ssao = ActiveSsao();
            var shadowed = new List<Light>();
            var shadowModes = new List<LightShadows>();
            foreach (var light in FindObjectsByType<Light>())
                if (light.shadows != LightShadows.None) { shadowed.Add(light); shadowModes.Add(light.shadows); }
            Bloom bloom = null;
            foreach (var volume in FindObjectsByType<Volume>())
                if (volume.sharedProfile != null && volume.sharedProfile.TryGet(out Bloom found) && found.active) bloom = found;
            var cameraData = Camera.main != null ? Camera.main.GetComponent<UniversalAdditionalCameraData>() : null;
            int msaa = urp != null ? urp.msaaSampleCount : 1;
            bool hdr = urp != null && urp.supportsHDR;
            float scale = urp != null ? urp.renderScale : 1f;

            void Restore()
            {
                if (urp != null) { urp.msaaSampleCount = msaa; urp.supportsHDR = hdr; urp.renderScale = scale; }
                foreach (var feature in ssao) feature.SetActive(true);
                for (int i = 0; i < shadowed.Count; i++) shadowed[i].shadows = shadowModes[i];
                if (bloom != null) bloom.active = true;
                if (cameraData != null) cameraData.renderPostProcessing = true;
            }

            Restore();
            yield return TypicalGameplay(game, "Réglages : référence (niveau actif)", 6f);
            if (urp != null && msaa > 1) { urp.msaaSampleCount = 1; yield return TypicalGameplay(game, $"Réglages : MSAA {msaa}× → désactivé", 6f); Restore(); }
            if (ssao.Count > 0) { foreach (var f in ssao) f.SetActive(false); yield return TypicalGameplay(game, "Réglages : occlusion ambiante (SSAO) désactivée", 6f); Restore(); }
            if (shadowed.Count > 0) { foreach (var l in shadowed) l.shadows = LightShadows.None; yield return TypicalGameplay(game, $"Réglages : ombres des projecteurs désactivées ({shadowed.Count})", 6f); Restore(); }
            if (urp != null && hdr) { urp.supportsHDR = false; yield return TypicalGameplay(game, "Réglages : HDR désactivé", 6f); Restore(); }
            if (bloom != null) { bloom.active = false; yield return TypicalGameplay(game, "Réglages : bloom désactivé", 6f); Restore(); }
            if (cameraData != null) { cameraData.renderPostProcessing = false; yield return TypicalGameplay(game, "Réglages : post-traitement désactivé", 6f); Restore(); }
            if (urp != null) { urp.renderScale = scale * 0.75f; yield return TypicalGameplay(game, "Réglages : échelle de rendu × 0,75", 6f); Restore(); }
            if (urp != null)
            {
                urp.msaaSampleCount = 1;
                foreach (var f in ssao) f.SetActive(false);
                foreach (var l in shadowed) l.shadows = LightShadows.None;
                yield return TypicalGameplay(game, "Réglages : MSAA, SSAO et ombres désactivés ensemble", 6f);
                Restore();
            }
        }

        /// <summary>Scénarios représentatifs, courts, pour comparer les niveaux de qualité (2.11).</summary>
        IEnumerator QuickSet(GameBootstrap game)
        {
            var player = game.PlayerController;
            var tools = game.ToolController;
            FillFoamEverywhere(game, 0f, 0f, 0f);
            yield return Measure("Atelier au repos (vue d'ensemble)", 5f, t => player.Teleport(new Vector3(0f, 0.06f, -2.3f), Mathf.Sin(t * 0.6f) * 10f, 22f));
            yield return TypicalGameplay(game, "Rinçage d'un tapis couvert de mousse", 8f);
            FillFoamEverywhere(game, 0.5f, 0.5f, 0f);
            yield return Measure("Canon à mousse balayant le tapis", 8f, t =>
            {
                AimAt(game, new Vector2(0.12f + 0.76f * Mathf.PingPong(t * 0.3f, 1f), 0.2f + 0.6f * Mathf.PingPong(t * 0.11f, 1f)));
                tools.SelectSlot(6);
                tools.AutomationUseOverride = true;
                tools.Equipment.Soap.Refill();
            });
            tools.AutomationUseOverride = false;
            FillFoamEverywhere(game, 1f, 1f, 1f);
            yield return Measure("Mousse : beaucoup (vue rapprochée, tapis plein écran)", 5f, t => AimAt(game, new Vector2(0.5f, 0.5f)));
            yield return Measure("Tour complet de l'atelier", 8f, t => player.Teleport(new Vector3(0f, 0.06f, -2.3f), t / 8f * 360f, 8f));
            FillFoamEverywhere(game, 0f, 0f, 0f);
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
            StartRecorders();
            scenario.GcCollections = GC.CollectionCount(0);
            lastHeap = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            current = scenario;
            // Build Development : capture du Profiler Unity propre à ce scénario (analysée ensuite dans l'éditeur).
            if (!string.IsNullOrEmpty(BuildBenchmark.ProfileDirectory) && Debug.isDebugBuild)
            {
                Directory.CreateDirectory(BuildBenchmark.ProfileDirectory);
                scenario.ProfileFile = Path.Combine(BuildBenchmark.ProfileDirectory, $"scenario-{scenarios.Count:00}");
                UnityEngine.Profiling.Profiler.maxUsedMemory = 512 * 1024 * 1024;
                UnityEngine.Profiling.Profiler.logFile = scenario.ProfileFile;
                UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                UnityEngine.Profiling.Profiler.enabled = true;
            }
            return scenario;
        }

        /// <summary>Une image mesurée : durée, temps GPU, particules vivantes (échantillonnées).</summary>
        void SampleFrame(Scenario scenario, float dt)
        {
            scenario.Frames.Add(dt);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                if (timings[0].gpuFrameTime > 0) scenario.GpuMs.Add((float)timings[0].gpuFrameTime);
                if (timings[0].cpuMainThreadFrameTime > 0) scenario.MainMs.Add((float)timings[0].cpuMainThreadFrameTime);
                if (timings[0].cpuRenderThreadFrameTime > 0) scenario.RenderMs.Add((float)timings[0].cpuRenderThreadFrameTime);
            }
            scenario.CounterSamples++;
            scenario.CountersValid = batchesRecorder.Valid && setPassRecorder.Valid && drawCallsRecorder.Valid;
            if (scenario.CountersValid)
            {
                scenario.Batches += batchesRecorder.LastValue;
                scenario.SetPass += setPassRecorder.LastValue;
                scenario.DrawCalls += drawCallsRecorder.LastValue;
            }
            scenario.GcValid = gcRecorder.Valid;
            if (scenario.GcValid) scenario.GcAlloc += gcRecorder.LastValue;
            long heap = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            if (heap > lastHeap) scenario.HeapGrowth += heap - lastHeap;
            lastHeap = heap;
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
            if (scenario.ProfileFile != null)
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                UnityEngine.Profiling.Profiler.logFile = string.Empty;
            }
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
            json.Append($"  \"mode\": \"{Escape(BuildBenchmark.Mode)}\",\n");
            json.Append($"  \"developmentBuild\": {(Debug.isDebugBuild ? "true" : "false")},\n");
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
                json.Append(string.Format(inv, "  \"pipeline\": {{ \"name\": \"{0}\", \"renderScale\": {1:0.00}, \"msaa\": {2}, \"hdr\": {3}, " +
                    "\"mainShadowResolution\": {4}, \"additionalShadowResolution\": {5}, \"ssao\": {6} }},\n",
                    Escape(pipeline.name), pipeline.renderScale, pipeline.msaaSampleCount, pipeline.supportsHDR ? "true" : "false",
                    pipeline.mainLightShadowmapResolution, pipeline.additionalLightsShadowmapResolution, SsaoDescription()));
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
                float p95 = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.95f) - 1, 0, sorted.Count - 1)];
                float median = sorted[sorted.Count / 2];
                int hitches = 0;
                foreach (float f in frames) if (f > median * 2f && f > 1f / 60f) hitches++;
                float worst = sorted[sorted.Count - 1];
                var scenario = scenarios[i];
                float gpu = Average(scenario.GpuMs), main = Average(scenario.MainMs), render = Average(scenario.RenderMs);
                int samples = Mathf.Max(1, scenario.CounterSamples);
                string counters = scenario.CountersValid
                    ? string.Format(inv, "\"batches\": {0:0}, \"setPassCalls\": {1:0}, \"drawCalls\": {2:0}", scenario.Batches / samples, scenario.SetPass / samples, scenario.DrawCalls / samples)
                    : "\"batches\": -1, \"setPassCalls\": -1, \"drawCalls\": -1";
                string allocation = string.Format(inv, "\"gcAllocKBPerFrame\": {0:0.00}, \"heapGrowthKBPerFrame\": {1:0.00}",
                    scenario.GcValid ? scenario.GcAlloc / samples / 1024.0 : -1.0, scenario.HeapGrowth / samples / 1024.0);
                string profile = scenario.ProfileFile != null ? $", \"profileFile\": \"{Escape(scenario.ProfileFile.Replace('\\', '/'))}\"" : string.Empty;
                var slots = new StringBuilder();
                for (int s = 0; s < (int)PerfStats.Slot.Count; s++)
                {
                    if (s > 0) slots.Append(", ");
                    slots.Append(string.Format(inv, "\"{0}\": {{ \"msPerFrame\": {1:0.000}, \"processedPerFrame\": {2:0} }}",
                        (PerfStats.Slot)s, scenario.SlotMs[s] / Mathf.Max(1, frames.Count), scenario.SlotProcessed[s] / (double)Mathf.Max(1, frames.Count)));
                }
                json.Append(string.Format(inv,
                    "    {{ \"name\": \"{0}\", \"resolution\": \"{7}\", \"frames\": {1}, \"avgFps\": {2:0.0}, \"onePercentLowFps\": {3:0.0}, \"avgFrameMs\": {4:0.00}, \"worstFrameMs\": {5:0.00}, " +
                    "\"avgGpuMs\": {8:0.00}, \"gcCollections\": {9}, \"maxParticles\": {10}, \"systems\": {{ {11} }}, " +
                    "\"p95FrameMs\": {12:0.00}, \"hitches\": {13}, \"avgMainThreadMs\": {14:0.00}, \"avgRenderThreadMs\": {15:0.00}, {16}, {17}{18} }}{6}\n",
                    Escape(scenario.Name), frames.Count, avgFps, 1f / p99, total / frames.Count * 1000f, worst * 1000f, i < scenarios.Count - 1 ? "," : "",
                    scenario.Resolution, gpu, scenario.GcCollections, scenario.MaxParticles, slots,
                    p95 * 1000f, hitches, main, render, counters, allocation, profile));
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

        static float Average(List<float> values)
        {
            if (values.Count == 0) return -1f;
            float sum = 0f;
            foreach (float v in values) sum += v;
            return sum / values.Count;
        }

        static string SsaoDescription() => ActiveSsao().Count > 0 ? "true" : "false";

        /// <summary>Occlusion ambiante active dans le pipeline courant (celui du niveau de qualité).</summary>
        static List<ScreenSpaceAmbientOcclusion> ActiveSsao()
        {
            var list = new List<ScreenSpaceAmbientOcclusion>();
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                foreach (var data in urp.rendererDataList)
                    if (data != null)
                        foreach (var feature in data.rendererFeatures)
                            if (feature is ScreenSpaceAmbientOcclusion ao && ao.isActive) list.Add(ao);
            return list;
        }
    }
}
