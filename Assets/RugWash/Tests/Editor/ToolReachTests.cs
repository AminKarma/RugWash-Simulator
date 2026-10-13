using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>
    /// Update v0.1.2 (1.4) — cause racine du « l'outil pointe vers le bas de l'écran » : l'outil ne faisait que pivoter
    /// autour de la main alors que sa longueur (0,5 à 0,9 m) est bien inférieure à la portée de nettoyage (2,1 à 2,4 m).
    /// Ces tests vérifient la portée télescopique et l'orientation, sans scène.
    /// </summary>
    public class ToolReachTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in created) Object.DestroyImmediate(go);
            created.Clear();
        }

        Transform Node(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            if (parent == null) created.Add(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            return go.transform;
        }

        [Test]
        public void Extension_PutsTheTipExactlyAtTheRequestedDistance_AndIsBounded()
        {
            var tip = new Vector3(0f, -0.09f, 0.4f);
            var axis = new Vector3(0f, -0.1045f, 0.9945f).normalized;
            foreach (float distance in new[] { 0.6f, 1.0f, 1.8f })
            {
                float e = ToolReach.SolveExtension(tip, axis, distance, -1f, 5f);
                Assert.AreEqual(distance, (tip + axis * e).magnitude, 1e-4f, $"distance {distance}");
            }
            Assert.AreEqual(1f, ToolReach.SolveExtension(tip, axis, 10f, 0f, 1f), "Allongement borné.");
            Assert.AreEqual(0f, ToolReach.SolveExtension(new Vector3(0.3f, 0f, 0f), Vector3.forward, 0.1f, 0f, 2f),
                "Distance inatteignable : le plus proche possible, dans les bornes.");
        }

        [Test]
        public void Target_ContactToolsStopAtTheirRange_NozzlesAimAtAnyDistance()
        {
            var ray = new Ray(Vector3.zero, Vector3.forward);
            Assert.AreEqual(new Vector3(0f, 0f, 1.5f), ToolReach.Target(true, true, new Vector3(0f, 0f, 1.5f), 1.5f, ray, 2.2f));
            Assert.AreEqual(new Vector3(0f, 0f, 2.2f), ToolReach.Target(true, true, new Vector3(0f, 0f, 4f), 4f, ray, 2.2f),
                "Trop loin : la tête s'arrête à la portée, sur le rayon.");
            Assert.AreEqual(new Vector3(0f, 0f, 2.2f), ToolReach.Target(true, false, default, 0f, ray, 2.2f));
            Assert.AreEqual(new Vector3(0f, 0f, 4f), ToolReach.Target(false, true, new Vector3(0f, 0f, 4f), 4f, ray, 3f), "Buse : le jet va au point visé.");
            Assert.AreEqual(new Vector3(0f, 0f, 5f), ToolReach.Target(false, false, default, 0f, ray, 3f));
        }

        [Test]
        public void ContactToolPose_LandsTheHeadOnTheTarget_ForAnyCameraOrientation()
        {
            var camera = Node("Camera", null, Vector3.zero, Quaternion.identity);
            var model = Node("Model", camera, new Vector3(0.27f, -0.3f, 0.48f), Quaternion.Euler(10f, -14f, 0f));
            var head = Node("Head", model, Vector3.zero, Quaternion.identity);
            var tip = Node("Tip", head, new Vector3(0f, -0.09f, 0.4f), Quaternion.identity);
            var axis = Quaternion.Euler(96f, 0f, 0f) * Vector3.up;
            var shaft = Node("Shaft", model, new Vector3(0f, 0.02f, 0.08f), Quaternion.LookRotation(axis, Vector3.up));
            var pose = new ToolPose(model, tip, head, shaft, 0.25f, 3f) { MaxAngle = 80f };

            var targetsInCamera = new[]
            {
                new Vector3(0f, -1.2f, 1.0f), new Vector3(0.3f, -1.0f, 1.6f), new Vector3(-0.4f, -1.4f, 1.2f), new Vector3(0.1f, -0.6f, 0.9f),
            };
            foreach (var orientation in new[] { Quaternion.identity, Quaternion.Euler(40f, 30f, 0f), Quaternion.Euler(-10f, 200f, 0f) })
            {
                camera.SetPositionAndRotation(new Vector3(1f, 1.62f, -2f), orientation);
                foreach (var local in targetsInCamera)
                {
                    Vector3 target = camera.TransformPoint(local);
                    pose.ResetToRest();
                    pose.Solve(target, 0f, snap: true);
                    pose.Apply();
                    Assert.Less(Vector3.Distance(tip.position, target), 0.002f, $"cible {local}, orientation {orientation.eulerAngles}");
                    Assert.Greater(pose.Extension, 0f, "Le manche s'allonge pour atteindre le point.");
                }
            }
            pose.ResetToRest();
            Assert.AreEqual(Vector3.zero, head.localPosition, "Retour au repos : manche rentré.");
            Assert.AreEqual(Vector3.one, shaft.localScale);
        }

        [Test]
        public void NozzlePose_PointsTheMuzzleAtTheTarget_WithoutStretching()
        {
            var camera = Node("Camera", null, new Vector3(0f, 1.62f, 0f), Quaternion.Euler(30f, 0f, 0f));
            var model = Node("Model", camera, new Vector3(0.25f, -0.25f, 0.4f), Quaternion.Euler(4f, -7f, 0f));
            var tip = Node("Tip", model, new Vector3(0f, 0.01f, 0.53f), Quaternion.identity);
            var pose = new ToolPose(model, tip);
            Vector3 target = camera.TransformPoint(new Vector3(-0.2f, 0.1f, 3f));
            pose.ResetToRest();
            pose.Solve(target, 0f, snap: true);
            pose.Apply();
            Vector3 pivot = camera.TransformPoint(new Vector3(0.25f, -0.25f, 0.4f));
            Assert.Less(Vector3.Angle(tip.position - pivot, target - pivot), 0.05f);
            Assert.AreEqual(0f, pose.Extension);
            Assert.IsFalse(pose.HasReach);
        }
    }
}
