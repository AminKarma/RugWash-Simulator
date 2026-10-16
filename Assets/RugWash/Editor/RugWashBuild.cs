using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>Build Windows 64 bits (menu ou ligne de commande : -executeMethod RugWash.Editor.RugWashBuild.BuildWindows).</summary>
    public static class RugWashBuild
    {
        public const string OutputDirectory = "Builds/Windows";
        /// <summary>Build Development (profileur) réservé aux mesures : le build livré reste dans <see cref="OutputDirectory"/>.</summary>
        public const string ProfilingOutputDirectory = "Builds/WindowsProfiling";
        public const string ExecutableName = "RugWashSimulator.exe";

        [MenuItem("RugWash/Build/Build Windows (64 bits)", priority = 100)]
        static void BuildFromMenu()
        {
            var report = Build();
            EditorUtility.DisplayDialog("RugWash", report.summary.result == BuildResult.Succeeded
                ? $"Build réussi : {OutputDirectory}/{ExecutableName}"
                : $"Build échoué : {report.summary.totalErrors} erreur(s). Voir la console.", "OK");
        }

        /// <summary>Point d'entrée en mode batch : code de sortie 0 si le build réussit.</summary>
        public static void BuildWindows()
        {
            try
            {
                var report = Build();
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Build Development pour les captures du Profiler Unity (-executeMethod RugWash.Editor.RugWashBuild.BuildWindowsProfiling).</summary>
        public static void BuildWindowsProfiling()
        {
            try
            {
                var report = Build(development: true);
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static BuildReport Build(bool development = false)
        {
            RugWashProjectBootstrapper.SyncBuildScenes();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || !scenes[0].EndsWith("MainMenu.unity"))
                throw new InvalidOperationException("[RugWash] Le build doit démarrer sur MainMenu.unity.");

            // Temps GPU par image disponible pour le benchmark (FrameTimingManager), coût négligeable.
            PlayerSettings.enableFrameTimingStats = true;
            string output = development ? ProfilingOutputDirectory : OutputDirectory;
            if (Directory.Exists(output)) Directory.Delete(output, true);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(output, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[RugWash Build] {summary.result} — {summary.totalSize / (1024f * 1024f):0.0} Mo, " +
                      $"{summary.totalErrors} erreur(s), {summary.totalWarnings} avertissement(s), durée {summary.totalTime:mm\\:ss}");
            return report;
        }
    }
}
