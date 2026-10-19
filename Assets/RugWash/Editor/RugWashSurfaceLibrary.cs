using System.IO;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 : bibliothèque de surfaces PBR de l'atelier (textures CC0 Poly Haven retravaillées, voir
    /// <c>Textures/Surfaces/SOURCES.json</c>, et micro-textures tuilables générées). Chaque surface fournit un albédo, une
    /// normale (OpenGL) et un masque (R métal, G occlusion, A lissé) branché à la fois en <c>_MetallicGlossMap</c> et en
    /// <c>_OcclusionMap</c> du shader URP Lit. Les matériaux unis reçoivent un détail (albédo ×2 neutre à 0,5 et normale) qui
    /// casse l'aspect « couleur plate » sans changer leur teinte.
    /// </summary>
    public static class RugWashSurfaceLibrary
    {
        public const string Folder = RugWashPaths.Root + "/Textures/Surfaces";

        public static string Albedo(string alias) => $"{Folder}/{alias}_Albedo.jpg";
        public static string Normal(string alias) => $"{Folder}/{alias}_Normal.jpg";
        public static string Mask(string alias) => $"{Folder}/{alias}_Mask.png";
        public static string MicroNormal(string kind) => $"{Folder}/Micro_{kind}_Normal.png";
        public static string MicroDetail(string kind) => $"{Folder}/Micro_{kind}_Detail.png";

        /// <summary>Réglages d'import (couleur sRGB, normales, masques linéaires, répétition, mipmaps, anisotropie).</summary>
        public static void ConfigureImports()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            foreach (string file in Directory.GetFiles(Folder))
            {
                string path = file.Replace('\\', '/');
                if (path.EndsWith(".meta") || path.EndsWith(".json")) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool normal = name.EndsWith("_Normal");
                bool color = name.EndsWith("_Albedo") || name.StartsWith("Window");
                bool micro = name.StartsWith("Micro_");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                int max = micro ? 512 : name.StartsWith("Floor") || name.StartsWith("WashPad") || name.StartsWith("Plaster") ? 2048 : 1024;
                if (importer.textureType == type && importer.sRGBTexture == color && importer.maxTextureSize == max &&
                    importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == 8 && importer.mipmapEnabled) continue;
                importer.textureType = type;
                importer.sRGBTexture = color;
                importer.alphaSource = name.EndsWith("_Mask") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.maxTextureSize = max;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static Texture2D Load(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"[RugWash] Texture de surface absente : {path}");
            return texture;
        }

        /// <summary>
        /// Surface texturée sur un matériau URP Lit : <paramref name="metersPerTile"/> = taille réelle d'une tuile (les UV des
        /// maillages générés sont en mètres). La teinte <see cref="Material"/>._BaseColor reste celle du matériau.
        /// </summary>
        public static void Apply(Material material, string alias, float metersPerTile, float normalScale = 1f, float smoothness = 1f,
            float occlusion = 1f)
        {
            float tiling = 1f / Mathf.Max(0.01f, metersPerTile);
            material.SetTexture("_BaseMap", Load(Albedo(alias)));
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            material.SetTexture("_BumpMap", Load(Normal(alias)));
            material.SetFloat("_BumpScale", normalScale);
            material.EnableKeyword("_NORMALMAP");
            var mask = Load(Mask(alias));
            material.SetTexture("_MetallicGlossMap", mask);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetTexture("_OcclusionMap", mask);
            material.SetFloat("_OcclusionStrength", occlusion);
            material.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
        }

        /// <summary>Retire les cartes d'une surface (matériau redevenu uni).</summary>
        public static void Clear(Material material)
        {
            foreach (string property in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_DetailAlbedoMap", "_DetailNormalMap" })
                if (material.HasProperty(property)) material.SetTexture(property, null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_OCCLUSIONMAP");
            material.DisableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_DETAIL_SCALED");
        }

        /// <summary>
        /// Détail de surface (URP Lit, mode ×2) : <paramref name="normalKind"/> = Plastic, Rubber, Brushed, Knit ou Cardboard ;
        /// <paramref name="albedoKind"/> = Grime ou Wear (null : pas de patine). <paramref name="tilesPerMeter"/> s'applique aux UV.
        /// </summary>
        public static void Detail(Material material, string normalKind, string albedoKind, float tilesPerMeter, float normalScale = 0.5f,
            float albedoScale = 1f)
        {
            material.SetTexture("_DetailAlbedoMap", albedoKind != null ? Load(MicroDetail(albedoKind)) : null);
            material.SetTexture("_DetailNormalMap", normalKind != null ? Load(MicroNormal(normalKind)) : null);
            material.SetTextureScale("_DetailAlbedoMap", new Vector2(tilesPerMeter, tilesPerMeter));
            material.SetFloat("_DetailAlbedoMapScale", albedoScale);
            material.SetFloat("_DetailNormalMapScale", normalScale);
            material.SetTexture("_DetailMask", null);
            // ×2 simple si l'échelle vaut 1, sinon version mise à l'échelle (URP Lit : _DETAIL_SCALED). Sans carte de normale de
            // base, URP lit quand même le détail de normale (tangentes interpolées dès que _DETAIL est actif).
            bool scaled = !Mathf.Approximately(albedoScale, 1f);
            if (scaled) { material.DisableKeyword("_DETAIL_MULX2"); material.EnableKeyword("_DETAIL_SCALED"); }
            else { material.DisableKeyword("_DETAIL_SCALED"); material.EnableKeyword("_DETAIL_MULX2"); }
            EditorUtility.SetDirty(material);
        }
    }
}
