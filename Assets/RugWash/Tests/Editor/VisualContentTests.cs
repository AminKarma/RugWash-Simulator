using System;
using NUnit.Framework;
using RugWash.Audio;
using RugWash.Editor;
using RugWash.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RugWash.Tests
{
    /// <summary>Maillages procéduraux, cadence des pas, horloge et réglages de rendu de la phase 5.</summary>
    public class VisualContentTests
    {
        static void AssertValid(Mesh mesh)
        {
            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(mesh.triangles.Length, 0);
            Assert.AreEqual(0, mesh.triangles.Length % 3);
            foreach (var v in mesh.vertices) Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z), "Sommet NaN");
            foreach (var n in mesh.normals) Assert.AreEqual(1f, n.magnitude, 1e-3f, "Normale non unitaire");
            Assert.AreEqual(mesh.vertexCount, mesh.uv.Length);
        }

        [Test]
        public void RoundedBox_HasExpectedBounds_AndOutwardNormals()
        {
            var mesh = RugWashProceduralMeshes.RoundedBox(new Vector3(0.4f, 0.2f, 0.1f), 0.03f);
            try
            {
                AssertValid(mesh);
                Assert.AreEqual(0.4f, mesh.bounds.size.x, 1e-3f);
                Assert.AreEqual(0.2f, mesh.bounds.size.y, 1e-3f);
                Assert.AreEqual(0.1f, mesh.bounds.size.z, 1e-3f);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                for (int i = 0; i < vertices.Length; i++)
                    Assert.Greater(Vector3.Dot(normals[i], vertices[i]), 0f, "Les normales doivent pointer vers l'extérieur.");

                // Les triangles suivent la convention de Unity (horaire vu de l'extérieur) : leur normale géométrique rejoint la normale stockée.
                var tris = mesh.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = vertices[tris[t]], b = vertices[tris[t + 1]], c = vertices[tris[t + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    if (face.sqrMagnitude < 1e-14f) continue;
                    Assert.Greater(Vector3.Dot(face, normals[tris[t]] + normals[tris[t + 1]] + normals[tris[t + 2]]), 0f, $"Triangle {t / 3} inversé");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Lathe_And_Tube_AreWellFormed()
        {
            var lathe = RugWashProceduralMeshes.Lathe(new[] { new Vector2(0.1f, 0f), new Vector2(0.12f, 0.3f), new Vector2(0.05f, 0.4f) }, 16);
            var tube = RugWashProceduralMeshes.Tube(RugWashProceduralMeshes.Smooth(new[] { Vector3.zero, new Vector3(0f, 0.5f, 0.2f), new Vector3(0.3f, 1f, 0f) }), 0.02f);
            var helix = RugWashProceduralMeshes.Tube(RugWashProceduralMeshes.Helix(0.2f, 0.03f, 3f), 0.01f);
            try
            {
                AssertValid(lathe);
                AssertValid(tube);
                AssertValid(helix);
                Assert.AreEqual(0.24f, lathe.bounds.size.x, 1e-3f);
                Assert.AreEqual(0.4f, lathe.bounds.size.y, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(lathe);
                Object.DestroyImmediate(tube);
                Object.DestroyImmediate(helix);
            }
        }

        [Test]
        public void HangingCloth_IsDoubleSided_AndMapsFullTexture()
        {
            var cloth = RugWashProceduralMeshes.HangingCloth(1.2f, 1.6f, 0.04f, 3, 10, 6);
            try
            {
                AssertValid(cloth);
                Assert.AreEqual(2 * 11 * 7, cloth.vertexCount, "Recto et verso");
                var uv = cloth.uv;
                Assert.AreEqual(0f, Mathf.Min(uv[0].x, uv[uv.Length - 1].x), 1e-5f);
                Assert.AreEqual(1.6f, cloth.bounds.size.y, 0.05f);
            }
            finally { Object.DestroyImmediate(cloth); }
        }

        [Test]
        public void Footsteps_FollowDistance_NotTime()
        {
            var cadence = new FootstepCadence(0.7f);
            int steps = 0;
            for (int i = 0; i < 200; i++) if (cadence.Advance(3.5f, true, 0.01f)) steps++;
            Assert.AreEqual(10, steps, "7 m parcourus = 10 pas de 0,7 m");

            int idle = 0;
            for (int i = 0; i < 500; i++) if (cadence.Advance(0f, true, 0.01f)) idle++;
            Assert.AreEqual(0, idle, "Aucun pas à l'arrêt.");
            int airborne = 0;
            for (int i = 0; i < 500; i++) if (cadence.Advance(4f, false, 0.01f)) airborne++;
            Assert.AreEqual(0, airborne, "Aucun pas en l'air.");
        }

        [Test]
        public void WallClock_ShowsGivenTime()
        {
            var root = new GameObject("Clock");
            var hour = new GameObject("H").transform;
            var minute = new GameObject("M").transform;
            hour.SetParent(root.transform);
            minute.SetParent(root.transform);
            try
            {
                var clock = root.AddComponent<WallClock>();
                var serialized = new SerializedObject(clock);
                serialized.FindProperty("hourHand").objectReferenceValue = hour;
                serialized.FindProperty("minuteHand").objectReferenceValue = minute;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                clock.Apply(new DateTime(2026, 10, 11, 15, 30, 0));
                Assert.AreEqual(-105f, Mathf.DeltaAngle(0f, hour.localEulerAngles.z), 0.01f, "15 h 30 → aiguille des heures à 3,5 h");
                Assert.AreEqual(180f, Mathf.Abs(Mathf.DeltaAngle(0f, minute.localEulerAngles.z)), 0.01f, "30 min → en bas");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Renderer_HasScreenSpaceAmbientOcclusion()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RugWashPaths.UrpRendererData);
            Assert.IsNotNull(data);
            Assert.IsTrue(data.rendererFeatures.Exists(f => f is ScreenSpaceAmbientOcclusion), "SSAO absente du renderer URP.");
        }
    }
}
