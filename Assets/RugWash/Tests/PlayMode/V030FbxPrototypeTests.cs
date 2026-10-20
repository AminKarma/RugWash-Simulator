#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// V0.3.0 — assets modélisés dans Blender via le MCP et exportés en FBX (Assets/RugWash/Models/FBX) : import, échelle,
    /// matériaux, squelette des personnages, collisions de l'environnement, états des tapis ; puis galerie de captures dans un
    /// studio neutre (Logs/ci/screens/v30_fbx_*), à relire à l'œil.
    /// </summary>
    public class V030FbxPrototypeTests
    {
        const string Root = "Assets/RugWash/Models/FBX/";

        static readonly string[] Tools = { "LanceHP", "Pistolet", "LanceBrosse", "BrosseRotative", "BrosseManuelle", "Monobrosse", "Raclette", "PulverisateurMousse", "AspirateurEau" };
        static readonly string[] Props = { "Batte", "Pompe", "CaisseOutils", "Casier", "Bidon40L", "Seau", "Eponge", "Chiffons", "Argent", "Telephone", "Cles", "Badge" };
        static readonly string[] Carpets = { "RugClassic", "RugOriental", "RugShag", "RugModern" };
        static readonly string[] Characters = { "Technicien_A", "Technicien_B", "Technicien_C", "Technicien_D" };
        static readonly string[] Environment = { "Env_FloorTile", "Env_WallPanel", "Env_Partition", "Env_PendantLamp", "Env_Shelf", "Env_Workbench", "Env_Washer", "Env_Dryer", "Env_WashBay", "Env_ToolWall", "Env_Signs" };
        static readonly string[] States = { "Sale", "Boueux", "Tache", "Mouille", "EnCours", "Propre" };

        Camera camera;

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject Spawn(string path, float yaw)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + path + ".fbx");
            Assert.IsNotNull(asset, $"FBX absent ou non importé : {path}");
            var go = Object.Instantiate(asset, Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials) Assert.IsNotNull(m, $"Matériau manquant : {path}/{r.name}");
            return go;
        }

        /// <summary>Rangée d'objets posés au sol, espacés selon leur largeur, centrée sur x = 0.</summary>
        static List<GameObject> Row(IList<string> paths, float z, float gap, float yaw = 0f)
        {
            var items = new List<GameObject>();
            foreach (var path in paths) items.Add(Spawn(path, yaw));
            float total = 0f;
            foreach (var go in items) total += BoundsOf(go).size.x + gap;
            float x = -total * 0.5f;
            foreach (var go in items)
            {
                var b = BoundsOf(go);
                go.transform.position += new Vector3(x + b.extents.x - b.center.x, -b.min.y, z - b.center.z);
                x += b.size.x + gap;
            }
            return items;
        }

        static List<string> In(string folder, IEnumerable<string> names)
        {
            var list = new List<string>();
            foreach (var n in names) list.Add(folder + "/" + n);
            return list;
        }

        IEnumerator Shot(List<GameObject> group, string name, Vector3 direction, float margin = 1f)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            var b = BoundsOf(group[0]);
            foreach (var go in group) b.Encapsulate(BoundsOf(go));
            float radius = b.extents.magnitude * margin;
            float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.transform.position = b.center + direction.normalized * distance;
            camera.transform.LookAt(b.center);
            yield return null;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            WorkshopPlayModeTests.Save(camera, Path.Combine(folder, $"v30_fbx_{name}.png"));
        }

        static void Clear(List<GameObject> group)
        {
            foreach (var go in group) Object.Destroy(go);
            group.Clear();
        }

        [UnityTest, Category("Capture")]
        public IEnumerator Assets_ImportCorrectly_AndRenderInAStudio()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            var scene = SceneManager.CreateScene("V030_FbxStudio");
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.18f);
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2.2f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.6f;
            fill.color = new Color(0.75f, 0.85f, 1f);
            fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/RugWash/Materials/M_Concrete.mat");
            camera = new GameObject("StudioCamera").AddComponent<Camera>();
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 80f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            yield return null;

            var report = new System.Text.StringBuilder("[V030 FBX] Dimensions (m)\n");
            void Log(List<GameObject> items, IList<string> names)
            {
                for (int i = 0; i < items.Count; i++) report.AppendLine($"  {names[i]} : {BoundsOf(items[i]).size}");
            }

            // Outils, présentés de profil.
            var group = Row(In("Tools", Tools), 0f, 0.25f, -90f);
            Log(group, Tools);
            Assert.That(BoundsOf(group[0]).size.x, Is.InRange(0.7f, 0.9f), "Lance haute pression : ~0,8 m.");
            yield return Shot(group.GetRange(0, 5), "tools_a", new Vector3(0.25f, 0.45f, -1f));
            yield return Shot(group.GetRange(5, 4), "tools_b", new Vector3(0.25f, 0.45f, -1f));
            Clear(group);

            group = Row(In("Props", Props), 0f, 0.15f);
            Log(group, Props);
            Assert.That(BoundsOf(group[3]).size.y, Is.InRange(1.7f, 1.9f), "Casier : ~1,8 m.");
            Assert.That(BoundsOf(group[4]).size.y, Is.InRange(0.5f, 0.6f), "Bidon 40 L : ~0,55 m.");
            yield return Shot(group.GetRange(0, 6), "props_a", new Vector3(0.2f, 0.5f, -1f));
            yield return Shot(group.GetRange(6, 6), "props_b", new Vector3(0.2f, 0.9f, -1f), 0.8f);
            Clear(group);

            group = Row(In("Carpets", Carpets), 0f, 0.3f);
            Log(group, Carpets);
            yield return Shot(group, "carpets", new Vector3(0f, 1.4f, -1f), 0.8f);
            Clear(group);

            // États des tapis : même maillage oriental, matériaux d'état (shader du jeu).
            var oriental = new List<string>();
            foreach (var _ in States) oriental.Add("Carpets/RugOriental");
            group = Row(oriental, 0f, 0.25f);
            for (int i = 0; i < States.Length; i++)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/RugWash/Materials/M_RugState_{States[i]}.mat");
                Assert.IsNotNull(material, $"Matériau d'état absent : {States[i]}");
                var rug = group[i].transform.Find("RugOriental") ?? group[i].transform;
                rug.GetComponent<Renderer>().sharedMaterial = material;
            }
            yield return Shot(group.GetRange(0, 3), "states_a", new Vector3(0f, 1.6f, -1f), 0.75f);
            yield return Shot(group.GetRange(3, 3), "states_b", new Vector3(0f, 1.6f, -1f), 0.75f);
            Clear(group);

            group = Row(In("Characters", Characters), 0f, 0.35f, 180f);
            Log(group, Characters);
            foreach (var go in group)
            {
                Assert.That(BoundsOf(go).size.y, Is.InRange(1.7f, 1.9f), $"{go.name} : ~1,8 m.");
                var skinned = go.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.IsNotNull(skinned, $"{go.name} : corps avec squelette.");
                Assert.GreaterOrEqual(skinned.bones.Length, 20, $"{go.name} : os importés.");
            }
            yield return Shot(group, "characters", new Vector3(0.15f, 0.25f, -1f), 0.9f);
            yield return Shot(group.GetRange(0, 2), "characters_close", new Vector3(0.1f, 0.15f, -1f), 0.7f);
            Clear(group);

            var env = In("Environment", Environment);
            group = Row(env.GetRange(0, 6), 0f, 0.4f);
            var group2 = Row(env.GetRange(6, 5), -5f, 0.4f);
            Log(group, Environment);
            Log(group2, env.GetRange(6, 5));
            foreach (var go in group) Assert.IsNotEmpty(go.GetComponentsInChildren<MeshCollider>(), $"{go.name} : collisions générées.");
            yield return Shot(group, "environment_a", new Vector3(0.2f, 0.55f, -1f), 0.85f);
            yield return Shot(group2, "environment_b", new Vector3(0.2f, 0.7f, -1f), 0.85f);
            Clear(group);
            Clear(group2);
            Debug.Log(report.ToString());
            yield return null;
        }
    }
}
#endif
