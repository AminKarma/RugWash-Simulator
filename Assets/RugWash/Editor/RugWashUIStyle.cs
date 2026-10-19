using System.IO;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 — ressources de la charte UI : polices Inter (OFL, <c>Assets/RugWash/Fonts</c>) et sprites blancs teintables
    /// (<c>Assets/RugWash/Textures/UI</c> : panneaux arrondis, contours, pastilles, ombre portée, dégradés, icônes). Les
    /// couleurs sont dans <see cref="RugWash.UI.UITheme"/> (partagées avec l'exécution).
    /// </summary>
    public static class RugWashUIStyle
    {
        public const string FontsFolder = RugWashPaths.Root + "/Fonts";
        public const string SpritesFolder = RugWashPaths.Root + "/Textures/UI";

        static Font Load(string file)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsFolder}/{file}");
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>Texte courant.</summary>
        public static Font Regular => Load("Inter-Regular.ttf");
        /// <summary>Texte appuyé (libellés, boutons, valeurs).</summary>
        public static Font SemiBold => Load("Inter-SemiBold.ttf");
        /// <summary>Titres et grands nombres.</summary>
        public static Font Display => Load("Inter-ExtraBold.ttf");

        public static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/{name}.png");

        /// <summary>Bordures 9-slice de chaque sprite (pixels) ; 0 = image simple.</summary>
        static int BorderOf(string name)
        {
            switch (name)
            {
                case "UI_Panel":
                case "UI_PanelOutline": return 20;
                case "UI_Pill":
                case "UI_PillOutline": return 31;
                case "UI_Bar": return 12;
                case "UI_Shadow": return 60;
                default: return 0;
            }
        }

        /// <summary>Import des sprites (bordures, sans compression ni mipmaps) et des polices (dynamiques).</summary>
        public static void ConfigureImports()
        {
            if (AssetDatabase.IsValidFolder(SpritesFolder))
                foreach (string file in Directory.GetFiles(SpritesFolder, "*.png"))
                {
                    string path = file.Replace('\\', '/');
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                    int border = BorderOf(Path.GetFileNameWithoutExtension(path));
                    var wanted = new Vector4(border, border, border, border);
                    if (importer.textureType == TextureImporterType.Sprite && importer.spriteBorder == wanted && !importer.mipmapEnabled) continue;
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spriteBorder = wanted;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            if (AssetDatabase.IsValidFolder(FontsFolder))
                foreach (string file in Directory.GetFiles(FontsFolder, "*.ttf"))
                {
                    string path = file.Replace('\\', '/');
                    if (!(AssetImporter.GetAtPath(path) is TrueTypeFontImporter importer)) continue;
                    if (importer.fontTextureCase == FontTextureCase.Dynamic && importer.includeFontData) continue;
                    importer.fontTextureCase = FontTextureCase.Dynamic;
                    importer.includeFontData = true;
                    importer.SaveAndReimport();
                }
        }
    }
}
