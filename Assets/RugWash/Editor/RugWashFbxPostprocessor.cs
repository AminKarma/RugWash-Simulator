using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 — import des FBX modélisés dans Blender via le MCP (Assets/RugWash/Models/FBX, sources Art/Blender/*.blend) :
    /// échelle réelle (1 unité = 1 m), normales du fichier, matériaux décrits par le FBX (couleur, rugosité, métal).
    /// Les matériaux de bâti et de tapis sont remplacés par ceux du projet (textures PBR réelles, motifs des tapis) : l'export
    /// FBX ne transporte ni les nœuds procéduraux de Blender ni, de façon fiable, le chemin des images. Les éléments
    /// d'environnement reçoivent des collisions (maillage).
    /// </summary>
    public sealed class RugWashFbxPostprocessor : AssetPostprocessor
    {
        const string Folder = "Assets/RugWash/Models/FBX/";

        // Changer la version réimporte les FBX concernés.
        public override uint GetVersion() => 4;

        /// <summary>Matériau Blender → matériau du projet (même rôle, textures PBR du jeu).</summary>
        static readonly (string source, string target)[] Remaps =
        {
            ("RW_RugOriental", "M_RugOriental_Display"), ("RW_RugClassic", "M_RugClassic_Display"), ("RW_RugShag", "M_RugShag_Display"),
            ("RW_RugFringe", "M_RugFringe"),
            ("RW_EnvConcrete", "M_Concrete"), ("RW_EnvPlaster", "M_Plaster"), ("RW_EnvWainscot", "M_Wainscot"), ("RW_EnvWood", "M_Wood"),
            ("RW_EnvMetalPaint", "M_DarkMetal"), ("RW_EnvSteel", "M_Steel"), ("RW_EnvHazard", "M_Hazard"),
        };

        /// <summary>Matériau Blender → texture d'albédo du projet (matériaux gardés du FBX mais texturés).</summary>
        static string TextureFor(string material)
        {
            switch (material)
            {
                case "RW_RugModern": return "Assets/RugWash/Textures/Rugs/Rug_Modern_Albedo.png";
                case "RW_RugHall": return "Assets/RugWash/Textures/Rugs/Rug_Hall_Albedo.png";
                default: return null;
            }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = assetPath.StartsWith(Folder + "Environment/");
            // Outils et accessoires tenus en main : maillages lisibles (les tests de portée mesurent le point le plus bas des têtes).
            importer.isReadable = assetPath.StartsWith(Folder + "Tools/") || assetPath.StartsWith(Folder + "Props/");
            foreach (var (source, target) in Remaps)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{RugWashPaths.Materials}/{target}.mat");
                if (material != null) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source), material);
            }
        }

        void OnPostprocessMaterial(Material material)
        {
            if (!assetPath.StartsWith(Folder)) return;
            string path = TextureFor(material.name);
            if (path == null) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.08f);
        }
    }
}
