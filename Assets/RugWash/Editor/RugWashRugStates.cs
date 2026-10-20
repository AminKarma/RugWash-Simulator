using System.IO;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 — états visuels fixes des tapis (sale, boueux, taché, mouillé, en cours de nettoyage, propre) pour les présentations
    /// (décor, aperçus, galeries). Ils utilisent le shader du jeu (« RugWash/Rug Surface ») avec des textures de contrôle
    /// composées à partir des six masques cuits dans Blender (Textures/Rugs/States/Rug_Mask_*.png) : aucune géométrie en plus,
    /// et exactement le rendu de la saleté, de la mousse et de l'humidité en jeu (ControlA : poussière, saleté fixée, saleté
    /// détachée, mousse ; ControlB : couleur de la saleté, humidité). Le nettoyage en jeu reste calculé par la grille.
    /// </summary>
    public static class RugWashRugStates
    {
        const string Masks = RugWashPaths.Root + "/Textures/Rugs/States";
        const string Out = RugWashPaths.Root + "/Textures/Rugs/States/Generated";
        const int Size = 512;

        public static readonly string[] States = { "Propre", "Sale", "Boueux", "Tache", "Mouille", "EnCours" };

        static float[] Load(string name)
        {
            string path = $"{Masks}/Rug_Mask_{name}.png";
            var values = new float[Size * Size];
            if (!File.Exists(path)) return values;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(path));
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                values[y * Size + x] = texture.GetPixelBilinear((x + 0.5f) / Size, (y + 0.5f) / Size).r;
            Object.DestroyImmediate(texture);
            return values;
        }

        static Texture2D Save(string name, Color[] pixels)
        {
            string path = $"{Out}/{name}.png";
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels);
            texture.Apply();
            Directory.CreateDirectory(Out);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && (importer.sRGBTexture || importer.textureCompression != TextureImporterCompression.Uncompressed))
            {
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Crée ou met à jour les matériaux M_RugState_* (motif oriental, réglages du matériau de tapis du jeu).</summary>
        public static void Ensure(Material rugTemplate, Texture2D pattern, Vector2 sizeMeters)
        {
            if (!Directory.Exists(Masks) || rugTemplate == null) return;
            var dust = Load("Dust");
            var stains = Load("Stains");
            var mud = Load("Mud");
            var foam = Load("Foam");
            var wet = Load("Wet");
            var cleaned = Load("Cleaned");
            // Couleurs de saleté (linéaires) : terre, boue, boisson renversée.
            var soil = new Color(0.16f, 0.1f, 0.06f);
            var mudColor = new Color(0.09f, 0.06f, 0.035f);
            var drink = new Color(0.22f, 0.07f, 0.06f);

            foreach (string state in States)
            {
                var a = new Color[Size * Size];
                var b = new Color[Size * Size];
                for (int i = 0; i < a.Length; i++)
                {
                    float d = 0f, s = 0f, l = 0f, f = 0f, m = 0f;
                    Color tint = soil;
                    switch (state)
                    {
                        case "Sale": d = dust[i] * 0.55f; s = stains[i] * 0.45f; l = mud[i] * 0.2f; break;
                        case "Boueux": d = dust[i] * 0.3f; l = mud[i] * 0.95f; s = mud[i] * 0.35f; m = mud[i] * 0.55f; tint = mudColor; break;
                        case "Tache": s = stains[i] * 0.95f; d = dust[i] * 0.12f; tint = drink; break;
                        case "Mouille": m = 0.35f + wet[i] * 0.65f; d = dust[i] * 0.05f; break;
                        case "EnCours":
                            float c = cleaned[i];
                            d = dust[i] * 0.55f * (1f - c); s = stains[i] * 0.45f * (1f - c); l = mud[i] * 0.2f * (1f - c);
                            f = foam[i] * 0.85f * Mathf.Clamp01(c * 1.5f) * (1f - Mathf.Clamp01((c - 0.6f) * 2.5f) * 0.6f);
                            m = c * (0.55f + wet[i] * 0.35f);
                            break;
                    }
                    a[i] = new Color(d, s, l, f);
                    b[i] = new Color(tint.r, tint.g, tint.b, m);
                }
                var controlA = Save($"Rug_State_{state}_A", a);
                var controlB = Save($"Rug_State_{state}_B", b);
                string matPath = $"{RugWashPaths.Materials}/M_RugState_{state}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material == null)
                {
                    material = new Material(rugTemplate) { name = $"M_RugState_{state}" };
                    AssetDatabase.CreateAsset(material, matPath);
                }
                else material.CopyPropertiesFromMaterial(rugTemplate);
                material.SetTexture("_BaseMap", pattern);
                material.SetTexture("_ControlA", controlA);
                material.SetTexture("_ControlB", controlB);
                material.SetVector("_RugSize", new Vector4(sizeMeters.x, sizeMeters.y, 0f, 0f));
                material.SetFloat("_ScanResponse", 0f);
                material.SetFloat("_CleanBoost", state == "Propre" ? 0.12f : 0.06f);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
