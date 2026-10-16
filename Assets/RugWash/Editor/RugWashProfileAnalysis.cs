using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// Analyse des captures du Profiler Unity écrites par le benchmark d'un build Development
    /// (« -rugwash-benchmark-profile &lt;dossier&gt; », une capture par scénario) : pour chaque capture, temps propre moyen par
    /// image des marqueurs les plus coûteux sur le fil principal et sur le fil de rendu, et nombre d'appels.
    /// Ligne de commande : -executeMethod RugWash.Editor.RugWashProfileAnalysis.Analyze
    /// -rugwash-profile-dir &lt;dossier&gt; [-rugwash-profile-report &lt;fichier&gt;].
    /// </summary>
    public static class RugWashProfileAnalysis
    {
        const int TopMarkers = 20;
        /// <summary>Premières images ignorées (démarrage de la capture).</summary>
        const int SkippedFrames = 5;

        public static void Analyze()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string directory = ValueOf(args, "-rugwash-profile-dir");
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    throw new DirectoryNotFoundException($"[RugWash Profile] dossier de captures introuvable : {directory}");
                string report = ValueOf(args, "-rugwash-profile-report") ?? Path.Combine(directory, "profile-analysis.txt");
                File.WriteAllText(report, AnalyzeDirectory(directory), new UTF8Encoding(false));
                Debug.Log($"[RugWash Profile] Rapport : {report}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static string ValueOf(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        sealed class Marker
        {
            public double SelfMs;
            public long Calls;
        }

        public static string AnalyzeDirectory(string directory)
        {
            var inv = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            foreach (string file in Directory.GetFiles(directory, "*.raw").OrderBy(f => f, StringComparer.Ordinal))
            {
                ProfilerDriver.ClearAllFrames();
                if (!ProfilerDriver.LoadProfile(file, false))
                {
                    text.AppendLine($"== {Path.GetFileName(file)} : capture illisible");
                    continue;
                }
                int first = ProfilerDriver.firstFrameIndex + SkippedFrames, last = ProfilerDriver.lastFrameIndex;
                int renderThread = first <= last ? FindThread(first, "Render Thread") : -1;
                var main = new Dictionary<string, Marker>();
                var render = new Dictionary<string, Marker>();
                int frames = 0;
                double frameMs = 0;
                for (int frame = first; frame <= last; frame++)
                {
                    using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0,
                               HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
                    {
                        if (view == null || !view.valid) continue;
                        frames++;
                        frameMs += view.frameTimeMs;
                        Accumulate(view, main);
                    }
                    if (renderThread < 0) continue;
                    using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, renderThread,
                               HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
                        if (view != null && view.valid) Accumulate(view, render);
                }
                text.AppendLine(string.Format(inv, "== {0} : {1} images analysées, image moyenne {2:0.00} ms (fil principal)",
                    Path.GetFileName(file), frames, frames > 0 ? frameMs / frames : 0));
                Write(text, "Fil principal", main, frames, inv);
                if (renderThread >= 0) Write(text, "Fil de rendu", render, frames, inv);
                else text.AppendLine("  Fil de rendu : absent de la capture");
                text.AppendLine();
            }
            ProfilerDriver.ClearAllFrames();
            return text.ToString();
        }

        /// <summary>Temps propre et appels par nom de marqueur (arbre complet du fil, sans la racine).</summary>
        static void Accumulate(HierarchyFrameDataView view, Dictionary<string, Marker> markers)
        {
            var stack = new Stack<int>();
            var children = new List<int>();
            view.GetItemChildren(view.GetRootItemID(), children);
            foreach (int child in children) stack.Push(child);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                string name = view.GetItemName(id);
                if (!markers.TryGetValue(name, out var marker)) markers[name] = marker = new Marker();
                marker.SelfMs += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                marker.Calls += (long)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                children.Clear();
                view.GetItemChildren(id, children);
                foreach (int child in children) stack.Push(child);
            }
        }

        static void Write(StringBuilder text, string title, Dictionary<string, Marker> markers, int frames, CultureInfo inv)
        {
            text.AppendLine($"  {title} (temps propre moyen par image, appels par image) :");
            foreach (var pair in markers.OrderByDescending(p => p.Value.SelfMs).Take(TopMarkers))
                text.AppendLine(string.Format(inv, "    {0,7:0.000} ms  {1,7:0.0}×  {2}", pair.Value.SelfMs / Math.Max(1, frames),
                    pair.Value.Calls / (double)Math.Max(1, frames), pair.Key));
        }

        static int FindThread(int frame, string threadName)
        {
            for (int thread = 0; thread < 256; thread++)
            {
                using (var raw = ProfilerDriver.GetRawFrameDataView(frame, thread))
                {
                    if (raw == null || !raw.valid) return -1;
                    if (raw.threadName == threadName) return thread;
                }
            }
            return -1;
        }
    }
}
