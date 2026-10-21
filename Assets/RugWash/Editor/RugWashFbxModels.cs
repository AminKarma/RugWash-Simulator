using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 — intégration au jeu des modèles Blender (MCP) exportés en FBX (Assets/RugWash/Models/FBX) : instanciation d'un
    /// FBX ou de certaines de ses pièces avec les matériaux du projet quand le rôle est le même (plastiques, métaux, caoutchouc,
    /// bois : la finition dorée des outils et l'étalonnage de l'atelier s'appliquent donc aussi aux nouveaux modèles). Les FBX
    /// ont leur face avant vers +Z local (outils : buse vers +Z). Si un FBX manque, l'appelant garde le modèle procédural.
    /// </summary>
    public static class RugWashFbxModels
    {
        public const string Root = RugWashPaths.Root + "/Models/FBX/";

        /// <summary>Matériau Blender → matériau de l'atelier (même rôle).</summary>
        static readonly Dictionary<string, string> ProjectMaterials = new Dictionary<string, string>
        {
            { "RW_BluePlastic", "BluePlastic" }, { "RW_YellowPlastic", "SafetyYellow" }, { "RW_BlackMetal", "DarkMetal" },
            { "RW_Rubber", "Rubber" }, { "RW_BlackPlastic", "Rubber" }, { "RW_Aluminium", "Chrome" }, { "RW_Steel", "Steel" },
            { "RW_WhitePlastic", "WhitePlastic" }, { "RW_RedPlastic", "RedPlastic" }, { "RW_Wood", "Wood" }, { "RW_Gold", "Gold" },
            { "RW_EnvYellowPaint", "SafetyYellow" }, { "RW_EnvRubber", "Rubber" }, { "RW_EnvWhiteEnamel", "WhitePlastic" },
            { "RW_EnvGlass", "Glass" }, { "RW_EnvLampGlow", "LampPanel" },
        };

        public static bool Has(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + path + ".fbx") != null;

        static Material ProjectMaterial(Material source, RugWashContentBuilder.Content c)
        {
            if (source == null) return null;
            return ProjectMaterials.TryGetValue(source.name, out var key) && c.Materials.TryGetValue(key, out var material) && material != null
                ? material : source;
        }

        /// <summary>
        /// Copie du FBX <paramref name="path"/> (ex. « Props/Casier ») sous <paramref name="parent"/> ; seules les pièces dont le
        /// nom est dans <paramref name="only"/> sont gardées si la liste est fournie. Null si le FBX est absent.
        /// </summary>
        public static GameObject Place(string path, Transform parent, RugWashContentBuilder.Content c, Vector3 position, Quaternion rotation,
            string name = null, ICollection<string> only = null, ICollection<string> except = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + path + ".fbx");
            if (asset == null) return null;
            var go = Object.Instantiate(asset);
            go.name = name ?? Path.GetFileName(path);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.one;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if ((only != null && !only.Contains(renderer.name)) || (except != null && except.Contains(renderer.name)))
                {
                    Object.DestroyImmediate(renderer.gameObject);
                    continue;
                }
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = ProjectMaterial(materials[i], c);
                renderer.sharedMaterials = materials;
            }
            return go;
        }

        /// <summary>
        /// Décor : FBX posé (lacet <paramref name="yaw"/>) puis abaissé pour que son point le plus bas touche
        /// <paramref name="surfaceY"/> ; rendus statiques ; boîte de collision englobante si <paramref name="collider"/>.
        /// </summary>
        public static GameObject PlaceDecor(string path, Transform parent, RugWashContentBuilder.Content c, Vector3 position, Vector3 euler,
            bool collider, float surfaceY = float.NaN, string name = null, ICollection<string> only = null)
        {
            var go = Place(path, parent, c, position, Quaternion.Euler(euler), name, only);
            if (go == null) return null;
            if (!float.IsNaN(surfaceY))
            {
                var world = LocalBounds(go, parent);
                go.transform.localPosition += new Vector3(0f, surfaceY - world.min.y, 0f);
            }
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) RugWashBuildHelpers.MakeStatic(renderer.gameObject);
            RugWashBuildHelpers.MakeStatic(go);
            if (collider)
            {
                var b = LocalBounds(go, go.transform);
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
            }
            return go;
        }

        /// <summary>Pièce <paramref name="part"/> d'un FBX, détachée et posée sous <paramref name="parent"/> (pivot du FBX conservé).</summary>
        public static Transform Detach(GameObject model, string part, Transform parent)
        {
            var t = FindDeep(model.transform, part);
            if (t == null) return null;
            t.SetParent(parent, true);
            return t;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Encombrement (repère de <paramref name="frame"/>) des maillages de <paramref name="go"/>.</summary>
        public static Bounds LocalBounds(GameObject go, Transform frame)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var m = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (first) { bounds = skinned.bounds; first = false; }
            return bounds;
        }

        /// <summary>CI : hiérarchie, pivots, encombrements et matériaux de chaque FBX (Logs/ci/fbx_dump.txt).</summary>
        public static void Dump()
        {
            var report = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                var go = Object.Instantiate(asset);
                var all = LocalBounds(go, go.transform);
                report.AppendLine($"== {path.Substring(Root.Length)}  bounds c={all.center:F3} s={all.size:F3}");
                void Walk(Transform t, string indent)
                {
                    var line = $"{indent}{t.name} p={t.localPosition:F3} r={t.localEulerAngles:F0} s={t.localScale:F2}";
                    var filter = t.GetComponent<MeshFilter>();
                    var renderer = t.GetComponent<Renderer>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        var b = LocalBounds(t.gameObject, go.transform);
                        line += $" | mesh c={b.center:F3} s={b.size:F3} v={filter.sharedMesh.vertexCount}";
                    }
                    if (renderer != null)
                    {
                        var names = new List<string>();
                        foreach (var m in renderer.sharedMaterials) names.Add(m != null ? m.name : "null");
                        line += " [" + string.Join(",", names) + "]";
                    }
                    report.AppendLine(line);
                    foreach (Transform child in t) Walk(child, indent + "  ");
                }
                if (!path.Contains("/Characters/")) Walk(go.transform, "  ");
                Object.DestroyImmediate(go);
            }
            string file = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "fbx_dump.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log("[FBX DUMP] " + file);
        }
    }
}
