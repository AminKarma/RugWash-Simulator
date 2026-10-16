using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Mesures (mission de corrections) : position réelle de la tête des outils de contact par rapport à la surface visée,
    /// et origine du jet d'eau par rapport à la buse, à plusieurs distances. Écrit Logs/ci/tool-diagnostics.txt.
    /// </summary>
    public class ToolDiagnosticsPlayModeTests
    {
        GameBootstrap game;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null && game.ToolController != null) game.ToolController.AutomationUseOverride = false;
            yield return null;
        }

        static float LowestPoint(Transform root)
        {
            float lowest = float.MaxValue;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !filter.GetComponent<MeshRenderer>().enabled) continue;
                var matrix = filter.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) lowest = Mathf.Min(lowest, matrix.MultiplyPoint3x4(v).y);
            }
            return lowest;
        }

        [UnityTest]
        public IEnumerator Measure_ContactHeads_AndWaterJetOrigin()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            var owned = new List<string>(game.Upgrades.OwnedIds) { "up_tool_squeegee", "up_tool_rotary", "up_detail_brush" };
            game.Upgrades.Restore(owned);
            Assert.AreEqual(Orders.OrderStartResult.Started, game.Orders.TryStart(game.Orders.Book.Orders[0]));
            yield return null;
            var report = new StringBuilder();
            var tools = game.ToolController.Tools;
            Vector3 center = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            foreach (var tool in tools)
            {
                if (!tool.IsUnlocked) continue;
                game.ToolController.SelectIndex(IndexOf(tools, tool));
                foreach (float distance in new[] { 0.8f, 1.4f, 2.0f, 2.6f })
                {
                    float pitch = Mathf.Atan2(1.68f - center.y, distance) * Mathf.Rad2Deg;
                    game.PlayerController.Teleport(new Vector3(center.x, 0.06f, center.z - distance), 0f, pitch);
                    game.ToolController.AutomationUseOverride = true;
                    for (int i = 0; i < 25; i++) yield return null;
                    var aim = game.ToolController.CurrentAim;
                    string line;
                    if (tool.IsContactTool && tool.AimTip != null)
                    {
                        var head = tool.AimTip.parent;
                        float lowest = LowestPoint(head);
                        line = $"{tool.name,-12} d={distance:0.0} cible y={aim.Point.y:0.000} tête min y={lowest:0.000} pénétration={(aim.Point.y - lowest) * 100f:0.0} cm " +
                               $"allongement={tool.ReachExtension:0.00} m extrémité→cible={tool.TipToTargetMeters * 100f:0.0} cm";
                    }
                    else
                    {
                        var jet = tool.GetComponentInChildren<ParticleSystem>(true);
                        var systems = tool.AimTip != null ? tool.AimTip.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
                        string origin = systems.Length > 0 ? $"émetteur→buse={Vector3.Distance(systems[0].transform.position, tool.AimTip.position) * 100f:0.0} cm" : "pas d'émetteur sur la buse";
                        line = $"{tool.name,-12} d={distance:0.0} buse={tool.AimTip?.position} {origin} écart visée={tool.AimErrorDegrees:0.0}°";
                    }
                    report.AppendLine(line);
                    game.ToolController.AutomationUseOverride = false;
                    yield return null;
                }
            }
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "tool-diagnostics.txt");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        static int IndexOf(IReadOnlyList<CleaningToolBase> list, CleaningToolBase tool)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == tool) return i;
            return -1;
        }
    }
}
