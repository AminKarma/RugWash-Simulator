using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 — modèles venus de Blender (<c>Tools/Blender</c>, scripts de modélisation et d'import) : fichiers
    /// <c>Assets/RugWash/Models/*.json</c> (une pièce par objet, repère Unity, normales et UV), convertis en maillages du
    /// <see cref="MeshStore"/> de la scène comme les maillages procéduraux. Les accessoires Poly Haven (CC0,
    /// <c>Models/Props</c>) reçoivent leurs matériaux URP Lit (textures de <c>Textures/Props</c>, manifeste des matériaux).
    /// Un modèle absent ne casse rien : l'appelant garde son maillage procédural.
    /// </summary>
    public static class RugWashModelLibrary
    {
        public const string Folder = RugWashPaths.Root + "/Models";
        public const string PropsFolder = Folder + "/Props";
        public const string PropTextures = RugWashPaths.Root + "/Textures/Props";
        public const string PropMaterials = RugWashPaths.Materials + "/Props";

        [Serializable] public sealed class PartData { public string name; public float[] v; public float[] n; public float[] uv; public int[] t; public string material; }
        [Serializable] sealed class ModelData { public string source; public PartData[] parts; }

        [Serializable] sealed class MaterialEntry { public string stem; public float[] color; public float metallic; public float roughness; public bool doubleSided; public bool cutout; }

        static readonly Dictionary<string, ModelData> Cache = new Dictionary<string, ModelData>();

        static ModelData Load(string file)
        {
            if (Cache.TryGetValue(file, out var cached)) return cached;
            string path = $"{Folder}/{file}.json";
            ModelData data = null;
            if (File.Exists(path)) data = JsonUtility.FromJson<ModelData>(File.ReadAllText(path));
            Cache[file] = data;
            return data;
        }

        /// <summary>Le fichier existe et contient la pièce demandée.</summary>
        public static bool Has(string file, string part = null)
        {
            var data = Load(file);
            if (data?.parts == null) return false;
            if (part == null) return data.parts.Length > 0;
            foreach (var p in data.parts) if (p.name == part) return true;
            return false;
        }

        public static IReadOnlyList<PartData> Parts(string file) => Load(file)?.parts ?? Array.Empty<PartData>();

        /// <summary>Maillage d'une pièce (stocké dans <paramref name="store"/>) ; <paramref name="mirrorX"/> : copie miroir (main gauche).</summary>
        public static Mesh Mesh(MeshStore store, string file, string part, bool mirrorX = false)
        {
            var data = Load(file);
            if (data?.parts == null) return null;
            foreach (var p in data.parts)
                if (p.name == part) return store.Add($"Model_{file.Replace('/', '_')}_{part}{(mirrorX ? "_Mirror" : string.Empty)}", () => Build(p, mirrorX));
            return null;
        }

        static Mesh Build(PartData p, bool mirrorX)
        {
            int count = p.v.Length / 3;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            float sx = mirrorX ? -1f : 1f;
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(p.v[i * 3] * sx, p.v[i * 3 + 1], p.v[i * 3 + 2]);
                normals[i] = new Vector3(p.n[i * 3] * sx, p.n[i * 3 + 1], p.n[i * 3 + 2]);
                uvs[i] = new Vector2(p.uv[i * 2], p.uv[i * 2 + 1]);
            }
            var triangles = (int[])p.t.Clone();
            if (mirrorX)
                for (int i = 0; i < triangles.Length; i += 3) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            var mesh = new Mesh { name = p.name };
            if (count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        // ------------------------------------------------------------------ Accessoires Poly Haven

        static Dictionary<string, Dictionary<string, MaterialEntry>> manifest;

        static Dictionary<string, MaterialEntry> MaterialsOf(string alias)
        {
            if (manifest == null)
            {
                manifest = new Dictionary<string, Dictionary<string, MaterialEntry>>();
                string path = $"{PropsFolder}/manifest.json";
                if (File.Exists(path)) ParseManifest(File.ReadAllText(path));
            }
            return manifest.TryGetValue(alias, out var entry) ? entry : null;
        }

        /// <summary>Manifeste { alias : { materials : { nom : {...} } } } lu sans dépendance JSON (JsonUtility ne lit pas les dictionnaires).</summary>
        static void ParseManifest(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) return;
            foreach (var (alias, value) in root)
            {
                if (!(value is Dictionary<string, object> entry) || !(entry.TryGetValue("materials", out var mats) && mats is Dictionary<string, object> materials)) continue;
                var map = new Dictionary<string, MaterialEntry>();
                foreach (var (name, raw) in materials)
                {
                    if (!(raw is Dictionary<string, object> m)) continue;
                    var e = new MaterialEntry
                    {
                        stem = m.TryGetValue("stem", out var s) ? s as string : null,
                        metallic = m.TryGetValue("metallic", out var me) ? Convert.ToSingle(me) : 0f,
                        roughness = m.TryGetValue("roughness", out var r) ? Convert.ToSingle(r) : 1f,
                        doubleSided = m.TryGetValue("doubleSided", out var d) && d is bool b && b,
                        cutout = m.TryGetValue("cutout", out var c) && c is bool cb && cb,
                    };
                    if (m.TryGetValue("color", out var col) && col is List<object> list && list.Count >= 3)
                        e.color = new[] { Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]), list.Count > 3 ? Convert.ToSingle(list[3]) : 1f };
                    map[name] = e;
                }
                manifest[alias] = map;
            }
        }

        static void ConfigureTexture(string path, bool normal, bool color)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.sRGBTexture == color && importer.anisoLevel == 4) return;
            importer.textureType = type;
            importer.sRGBTexture = color;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static Material PropMaterial(string alias, string materialName)
        {
            var entries = MaterialsOf(alias);
            MaterialEntry entry = null;
            entries?.TryGetValue(materialName ?? string.Empty, out entry);
            string stem = entry?.stem ?? alias;
            if (!AssetDatabase.IsValidFolder(PropMaterials)) AssetDatabase.CreateFolder(RugWashPaths.Materials, "Props");
            string path = $"{PropMaterials}/M_{stem}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + stem };
                AssetDatabase.CreateAsset(material, path);
            }
            string albedo = $"{PropTextures}/{stem}_Albedo.jpg", normal = $"{PropTextures}/{stem}_Normal.jpg", mask = $"{PropTextures}/{stem}_Mask.png";
            if (!File.Exists(albedo)) albedo = $"{PropTextures}/{stem}_Albedo.png";
            ConfigureTexture(albedo, false, true);
            ConfigureTexture(normal, true, false);
            ConfigureTexture(mask, false, false);
            var color = entry?.color != null ? new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]) : Color.white;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            if (normalMap != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            var maskMap = AssetDatabase.LoadAssetAtPath<Texture2D>(mask);
            material.SetTexture("_MetallicGlossMap", maskMap);
            material.SetTexture("_OcclusionMap", maskMap);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_OcclusionStrength", 1f);
            if (maskMap != null) { material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.EnableKeyword("_OCCLUSIONMAP"); }
            else { material.DisableKeyword("_METALLICSPECGLOSSMAP"); material.DisableKeyword("_OCCLUSIONMAP"); material.SetFloat("_Smoothness", 0.35f); }
            bool twoSided = entry != null && entry.doubleSided;
            material.SetFloat("_Cull", twoSided ? 0f : 2f);
            material.doubleSidedGI = twoSided;
            bool cutout = entry != null && entry.cutout;
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            if (cutout) { material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest; }
            else { material.DisableKeyword("_ALPHATEST_ON"); material.renderQueue = -1; }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Accessoire du décor (pivot au centre de la base, face avant vers -Z local). <paramref name="collider"/> : boîte englobante.
        /// Retourne null si le modèle n'a pas été exporté.
        /// </summary>
        public static GameObject Prop(Transform parent, MeshStore store, string alias, Vector3 position, float yaw = 0f, float scale = 1f,
            bool collider = false, bool isStatic = true, bool shadows = true)
        {
            var parts = Parts($"Props/{alias}");
            if (parts.Count == 0) return null;
            var root = new GameObject("Prop_" + alias);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = Vector3.one * scale;
            var bounds = new Bounds();
            bool first = true;
            foreach (var part in parts)
            {
                var mesh = Mesh(store, $"Props/{alias}", part.name);
                if (mesh == null || mesh.vertexCount == 0) continue;
                var go = new GameObject(part.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = PropMaterial(alias, part.material);
                if (!shadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (isStatic) MakeStatic(go);
                if (first) { bounds = mesh.bounds; first = false; }
                else bounds.Encapsulate(mesh.bounds);
            }
            if (collider)
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            if (isStatic) MakeStatic(root);
            return root;
        }
    }

    /// <summary>Lecteur JSON minimal (objets, tableaux, nombres, chaînes, booléens, null) pour le manifeste des accessoires.</summary>
    static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            return Value(json, ref i);
        }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var obj = new Dictionary<string, object>();
                i++;
                while (true)
                {
                    Skip(s, ref i);
                    if (s[i] == '}') { i++; return obj; }
                    string key = (string)Value(s, ref i);
                    Skip(s, ref i);
                    i++; // ':'
                    obj[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i] == ',') i++;
                }
            }
            if (c == '[')
            {
                var list = new List<object>();
                i++;
                while (true)
                {
                    Skip(s, ref i);
                    if (s[i] == ']') { i++; return list; }
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') i++;
                }
            }
            if (c == '"')
            {
                var sb = new System.Text.StringBuilder();
                i++;
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
