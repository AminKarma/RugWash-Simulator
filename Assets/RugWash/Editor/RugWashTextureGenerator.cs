using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// Génère les textures originales du projet (motifs de tapis, normales de fibres, détails de saleté,
    /// matériaux d'atelier, sprites d'interface). Entièrement procédural : aucune ressource externe.
    /// </summary>
    public static class RugWashTextureGenerator
    {
        public const string OrientalAlbedo = RugWashPaths.GeneratedTextures + "/Rug_Oriental_Albedo.png";
        public const string ClassicAlbedo = RugWashPaths.GeneratedTextures + "/Rug_Classic_Albedo.png";
        public const string ShagAlbedo = RugWashPaths.GeneratedTextures + "/Rug_Shag_Albedo.png";
        public const string LuxuryAlbedo = RugWashPaths.GeneratedTextures + "/Rug_Luxury_Albedo.png";
        public const string FiberShortNormal = RugWashPaths.GeneratedTextures + "/Fiber_Short_Normal.png";
        public const string FiberLongNormal = RugWashPaths.GeneratedTextures + "/Fiber_Long_Normal.png";
        public const string DirtDetail = RugWashPaths.GeneratedTextures + "/Dirt_Detail.png";
        public const string ConcreteAlbedo = RugWashPaths.GeneratedTextures + "/Concrete_Albedo.png";
        public const string ConcreteNormal = RugWashPaths.GeneratedTextures + "/Concrete_Normal.png";
        public const string PlasterAlbedo = RugWashPaths.GeneratedTextures + "/Plaster_Albedo.png";
        public const string WoodAlbedo = RugWashPaths.GeneratedTextures + "/Wood_Albedo.png";
        public const string GrateAlbedo = RugWashPaths.GeneratedTextures + "/Grate_Albedo.png";
        public const string Hazard = RugWashPaths.GeneratedTextures + "/Hazard_Stripes.png";
        public const string ClockFace = RugWashPaths.GeneratedTextures + "/Clock_Face.png";
        public const string Paper = RugWashPaths.GeneratedTextures + "/Paper.png";
        public const string UIWhite = RugWashPaths.GeneratedTextures + "/UI_White.png";
        public const string UIRounded = RugWashPaths.GeneratedTextures + "/UI_Rounded.png";
        /// <summary>Décor de l'atelier (v0.1.2 jalon 5) : briques du loft, vue sur Los Angeles au crépuscule (penthouse).</summary>
        public const string BrickAlbedo = RugWashPaths.GeneratedTextures + "/Brick_Albedo.png";
        public const string Skyline = RugWashPaths.GeneratedTextures + "/Skyline_LosAngeles.png";
        public const string WallStain = RugWashPaths.GeneratedTextures + "/Wall_Stain.png";

        enum Kind { Color, Linear, Normal, Sprite }

        [MenuItem("RugWash/Setup/Regénérer les Textures Procédurales", priority = 40)]
        static void RegenerateAllMenu() => GenerateAll(force: true);

        public static void GenerateAll(bool force)
        {
            RugWashProjectBootstrapper.EnsureFolders();
            Generate(OrientalAlbedo, 1536, 1024, force, Kind.Color, false, OrientalPixel);
            Generate(ClassicAlbedo, 1536, 1024, force, Kind.Color, false, ClassicPixel);
            Generate(ShagAlbedo, 1536, 1024, force, Kind.Color, false, ShagPixel);
            Generate(LuxuryAlbedo, 2048, 1408, force, Kind.Color, false, LuxuryPixel);
            GenerateHeightToNormal(FiberShortNormal, 512, force, ShortFiberHeight, 1.5f);
            GenerateHeightToNormal(FiberLongNormal, 512, force, LongFiberHeight, 3.2f);
            Generate(DirtDetail, 512, 512, force, Kind.Linear, true, DirtDetailPixel);
            Generate(ConcreteAlbedo, 1024, 1024, force, Kind.Color, true, ConcretePixel);
            GenerateHeightToNormal(ConcreteNormal, 1024, force, ConcreteHeight, 1.2f);
            Generate(PlasterAlbedo, 512, 512, force, Kind.Color, true, PlasterPixel);
            Generate(WoodAlbedo, 512, 512, force, Kind.Color, true, WoodPixel);
            Generate(GrateAlbedo, 256, 256, force, Kind.Color, true, GratePixel);
            Generate(Hazard, 256, 256, force, Kind.Color, true, HazardPixel);
            Generate(ClockFace, 256, 256, force, Kind.Color, false, ClockFacePixel);
            Generate(Paper, 256, 256, force, Kind.Color, true, PaperPixel);
            Generate(UIWhite, 8, 8, force, Kind.Sprite, false, (u, v) => Color.white);
            Generate(UIRounded, 64, 64, force, Kind.Sprite, false, RoundedPixel);
            Generate(BrickAlbedo, 512, 512, force, Kind.Color, true, BrickPixel);
            Generate(Skyline, 2048, 256, force, Kind.Color, false, SkylinePixel);
            Generate(WallStain, 256, 256, force, Kind.Color, false, WallStainPixel);
        }

        // ------------------------------------------------------------------ Décor de l'atelier

        /// <summary>Briques en quinconce (8 rangées × 4 briques par tuile), joints clairs, teinte variable par brique.</summary>
        static Color BrickPixel(float u, float v)
        {
            const int rows = 8, columns = 4;
            float y = v * rows;
            int row = Mathf.FloorToInt(y);
            float x = u * columns + (row % 2 == 0 ? 0f : 0.5f);
            int column = Mathf.FloorToInt(x);
            float fx = x - column, fy = y - row;
            bool mortar = fx < 0.04f || fx > 0.96f || fy < 0.08f || fy > 0.92f;
            if (mortar) return new Color(0.72f, 0.69f, 0.64f) * (0.9f + 0.1f * ValueNoise(u, v, 64, 701));
            float tint = Hash(column % columns, row, 703);
            var brick = Color.Lerp(new Color(0.52f, 0.22f, 0.15f), new Color(0.66f, 0.33f, 0.22f), tint);
            return brick * (0.85f + 0.15f * ValueNoise(u, v, 128, 707));
        }

        /// <summary>
        /// Tache d'humidité au mur (cagibi) : contour irrégulier découpé par l'alpha, auréole plus sombre sur le bord,
        /// coulure vers le bas. Le matériau la découpe (alpha clip) : aucune forme rectangulaire visible.
        /// </summary>
        static Color WallStainPixel(float u, float v)
        {
            float x = (u - 0.5f) * 2f, y = (v - 0.55f) * 2f;
            // Coulure : la tache s'étire vers le bas, plus étroite.
            float drip = y < 0f ? Mathf.Abs(x) * (1f + -y * 1.6f) : Mathf.Abs(x);
            float d = Mathf.Sqrt(drip * drip + y * y * (y < 0f ? 0.45f : 1.15f));
            d += (Fbm(u, v, 4, 4, 913) - 0.5f) * 0.55f;
            float alpha = 1f - Smooth(0.7f, 0.76f, d);
            float ring = Smooth(0.45f, 0.72f, d);
            float grain = 0.85f + 0.15f * ValueNoise(u, v, 64, 917);
            // Proche de l'enduit : une auréole jaunie, plus marquée sur le bord, pas un trou sombre.
            var inner = new Color(0.72f, 0.64f, 0.48f);
            var edge = new Color(0.5f, 0.41f, 0.28f);
            var color = Color.Lerp(inner, edge, ring) * grain;
            color.a = alpha;
            return color;
        }

        static readonly float[] PalmPositions = { 0.06f, 0.13f, 0.31f, 0.45f, 0.86f, 0.93f };

        /// <summary>
        /// Vue sur Los Angeles au crépuscule (panorama 8:1) : ciel orangé vers violet, collines lointaines, grands immeubles
        /// du centre éclairés, ville basse étendue, silhouettes de palmiers au premier plan. Image générée (aucune photo).
        /// </summary>
        static Color SkylinePixel(float u, float v)
        {
            // Ciel : horizon orange, puis rose, violet et bleu nuit ; halo du soleil couchant.
            var color = v < 0.45f ? Color.Lerp(new Color(1f, 0.62f, 0.32f), new Color(0.95f, 0.45f, 0.45f), Mathf.InverseLerp(0.25f, 0.45f, v))
                : v < 0.75f ? Color.Lerp(new Color(0.95f, 0.45f, 0.45f), new Color(0.38f, 0.22f, 0.48f), Mathf.InverseLerp(0.45f, 0.75f, v))
                : Color.Lerp(new Color(0.38f, 0.22f, 0.48f), new Color(0.12f, 0.12f, 0.3f), Mathf.InverseLerp(0.75f, 1f, v));
            float sun = Mathf.Exp(-(Sq((u - 0.72f) * 8f) + Sq((v - 0.3f) * 1f)) * 6f);
            color = Color.Lerp(color, new Color(1f, 0.85f, 0.55f), sun * 0.7f);

            // Collines lointaines.
            float hills = 0.3f + 0.05f * Mathf.Sin(u * 7f + 1f) + 0.03f * Mathf.Sin(u * 19f + 2f) + 0.015f * ValueNoise(u, 0.5f, 64, 711);
            if (v < hills) color = Color.Lerp(new Color(0.3f, 0.2f, 0.34f), new Color(0.22f, 0.15f, 0.28f), Mathf.InverseLerp(hills, 0.1f, v));

            // Ville : immeubles (le centre, plus haut, vers u = 0,62), fenêtres éclairées.
            int building = Mathf.FloorToInt(u * 110f);
            float local = u * 110f - building;
            float downtown = Mathf.Exp(-Sq((u - 0.62f) * 9f));
            float height = 0.16f + 0.08f * Hash(building, 1, 713) + downtown * (0.25f + 0.35f * Hash(building, 2, 717));
            bool gap = local < 0.06f && Hash(building, 3, 719) > 0.5f;
            if (v < height && !gap)
            {
                color = Color.Lerp(new Color(0.1f, 0.11f, 0.18f), new Color(0.16f, 0.15f, 0.24f), Hash(building, 4, 721));
                int wx = Mathf.FloorToInt(local * 6f), wy = Mathf.FloorToInt(v * 220f);
                float wfx = local * 6f - wx, wfy = v * 220f - wy;
                bool window = wfx > 0.25f && wfx < 0.75f && wfy > 0.2f && wfy < 0.8f;
                if (window && Hash(building * 7 + wx, wy, 727) > 0.62f) color = Color.Lerp(new Color(1f, 0.82f, 0.45f), new Color(0.9f, 0.95f, 1f), Hash(wx, wy, 729) * 0.4f);
                // Feu rouge au sommet des plus hautes tours.
                if (downtown > 0.5f && Mathf.Abs(v - height + 0.008f) < 0.006f && Mathf.Abs(local - 0.5f) < 0.08f) color = new Color(1f, 0.2f, 0.15f);
            }

            // Sol de la ville et lampadaires.
            if (v < 0.07f)
            {
                color = new Color(0.07f, 0.07f, 0.11f);
                if (Hash(Mathf.FloorToInt(u * 400f), 5, 731) > 0.8f && Mathf.Abs(v - 0.045f) < 0.006f) color = new Color(1f, 0.75f, 0.4f);
            }

            // Palmiers en silhouette (le panorama est 8 fois plus large que haut : distances corrigées).
            foreach (float px in PalmPositions)
            {
                float dx = (u - px) * 8f;
                if (Mathf.Abs(dx) > 0.5f) continue;
                float top = 0.62f + 0.12f * Hash(Mathf.RoundToInt(px * 100f), 6, 733);
                float lean = 0.04f * Mathf.Sin(px * 40f);
                if (v < top && Mathf.Abs(dx - lean * v) < 0.012f + 0.006f * (1f - v)) return new Color(0.04f, 0.03f, 0.06f);
                for (int f = 0; f < 9; f++)
                {
                    float angle = Mathf.PI * (0.05f + 0.9f * f / 8f);
                    for (int s = 1; s <= 8; s++)
                    {
                        float k = s / 8f;
                        float fx = lean * top + Mathf.Cos(angle) * 0.22f * k, fy = top + Mathf.Sin(angle) * 0.12f * k - 0.1f * k * k;
                        if (Sq(dx - fx) + Sq(v - fy) < Sq(0.012f * (1.1f - k))) return new Color(0.04f, 0.03f, 0.06f);
                    }
                }
            }
            return color;
        }

        static float Sq(float x) => x * x;

        // ------------------------------------------------------------------ Écriture / import

        static void Generate(string path, int width, int height, bool force, Kind kind, bool tileable, Func<float, float, Color> pixel)
        {
            if (!force && File.Exists(path)) return;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, kind != Kind.Color && kind != Kind.Sprite);
            var colors = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                colors[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            texture.SetPixels(colors);
            Write(texture, path);
            ConfigureImporter(path, kind, tileable, Mathf.Max(width, height));
        }

        static void GenerateHeightToNormal(string path, int size, bool force, Func<float, float, float> height, float strength)
        {
            if (!force && File.Exists(path)) return;
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                heights[y * size + x] = height((x + 0.5f) / size, (y + 0.5f) / size);

            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = heights[y * size + (x - 1 + size) % size], r = heights[y * size + (x + 1) % size];
                float d = heights[((y - 1 + size) % size) * size + x], u = heights[((y + 1) % size) * size + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                colors[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels(colors);
            Write(texture, path);
            ConfigureImporter(path, Kind.Normal, true, size);
        }

        static void Write(Texture2D texture, string assetPath)
        {
            texture.Apply(false);
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        static void ConfigureImporter(string path, Kind kind, bool tileable, int size)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = kind == Kind.Normal ? TextureImporterType.NormalMap
                : kind == Kind.Sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.sRGBTexture = kind == Kind.Color || kind == Kind.Sprite;
            importer.wrapMode = tileable ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = kind != Kind.Sprite;
            importer.anisoLevel = kind == Kind.Sprite ? 0 : 8;
            importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(size));
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (kind == Kind.Sprite)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Bilinear;
                importer.spriteBorder = path == UIRounded ? new Vector4(20, 20, 20, 20) : Vector4.zero;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ Bruit tuilable

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Wrap(int v, int period) => ((v % period) + period) % period;

        /// <summary>Bruit de valeur périodique (u,v dans [0,1[, période en cellules).</summary>
        static float ValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int ax = Wrap(x0, period), bx = Wrap(x0 + 1, period), ay = Wrap(y0, period), by = Wrap(y0 + 1, period);
            float a = Hash(ax, ay, seed), b = Hash(bx, ay, seed), c = Hash(ax, by, seed), d = Hash(bx, by, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(u, v, period, seed + i * 31);
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>Distance au point de cellule le plus proche (Worley), périodique.</summary>
        static float Cellular(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float best = 10f;
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                float px = gx + Hash(Wrap(gx, period), Wrap(gy, period), seed);
                float py = gy + Hash(Wrap(gx, period), Wrap(gy, period), seed + 7);
                float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        static float Repeat(float v, float period) => v - period * Mathf.Floor(v / period);
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
        static Color Hex(float r, float g, float b) => new Color(r, g, b, 1f);

        // ------------------------------------------------------------------ Tapis oriental (3 m x 2 m)

        static readonly Color Madder = Hex(0.55f, 0.11f, 0.10f);
        static readonly Color MadderDark = Hex(0.40f, 0.07f, 0.07f);
        static readonly Color Indigo = Hex(0.09f, 0.13f, 0.27f);
        static readonly Color Ivory = Hex(0.90f, 0.84f, 0.70f);
        static readonly Color Gold = Hex(0.80f, 0.58f, 0.22f);
        static readonly Color Teal = Hex(0.10f, 0.38f, 0.40f);

        static Color OrientalPixel(float u, float v)
        {
            const float hx = 1.5f, hy = 1.0f, knot = 0.0055f;
            float x = (u - 0.5f) * hx * 2f, y = (v - 0.5f) * hy * 2f;
            int kx = Mathf.FloorToInt(x / knot), ky = Mathf.FloorToInt(y / knot);
            x = (kx + 0.5f) * knot;
            y = (ky + 0.5f) * knot;

            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            float d = Mathf.Min(hx - ax, hy - ay);
            float along = hx - ax < hy - ay ? y : x;
            Color c;

            if (d < 0.025f)
            {
                bool dot = d > 0.008f && d < 0.018f && Repeat(along, 0.022f) < 0.011f;
                c = dot ? Ivory : Indigo;
            }
            else if (d < 0.04f) c = MadderDark;
            else if (d < 0.20f)
            {
                float s = Repeat(along, 0.16f) - 0.08f;
                float t = d - 0.12f;
                float r = Mathf.Sqrt(s * s + t * t);
                float theta = Mathf.Atan2(t, s);
                c = Indigo;
                float vine = t - 0.045f * Mathf.Sin(along / 0.16f * Mathf.PI * 2f);
                if (Mathf.Abs(vine) < 0.0055f) c = Ivory;
                float leaf = Mathf.Sqrt(Mathf.Pow(s - 0.055f, 2) + Mathf.Pow(t + 0.03f, 2));
                if (leaf < 0.016f) c = Teal;
                if (r < 0.052f * (0.78f + 0.22f * Mathf.Cos(8f * theta))) c = Gold;
                if (r < 0.03f * (0.8f + 0.2f * Mathf.Cos(6f * theta))) c = Madder;
                if (r < 0.011f) c = Ivory;
            }
            else if (d < 0.212f) c = Gold;
            else if (d < 0.236f)
            {
                float zig = Mathf.Abs(Repeat(along, 0.024f) - 0.012f);
                c = Mathf.Abs((d - 0.214f) - zig) < 0.0035f ? Madder : Ivory;
            }
            else if (d < 0.246f) c = Indigo;
            else c = OrientalField(x, y, ax, ay);

            float knotNoise = 0.93f + 0.12f * Hash(kx, ky, 3);
            float abrash = 0.95f + 0.05f * Mathf.Sin(y * 7.3f + Fbm(u, v, 3, 2, 9) * 4f);
            float wear = 0.96f + 0.06f * Fbm(u, v, 4, 3, 21);
            return c * (knotNoise * abrash * wear);
        }

        static Color OrientalField(float x, float y, float ax, float ay)
        {
            const float fhx = 1.254f, fhy = 0.754f;
            Color c = Madder;

            // Treillis de petits losanges.
            float row = Mathf.Floor(y / 0.11f);
            float gx = Repeat(x + (row % 2f) * 0.055f, 0.11f) - 0.055f;
            float gy = Repeat(y, 0.11f) - 0.055f;
            float diamond = Mathf.Abs(gx) + Mathf.Abs(gy);
            if (diamond < 0.026f) c = Indigo;
            if (diamond < 0.011f) c = Ivory;
            if (Mathf.Abs(gx) > 0.048f && Mathf.Abs(gy) > 0.048f) c = Teal;

            // Écoinçons.
            float ex = (ax - fhx) / 0.44f, ey = (ay - fhy) / 0.32f;
            float rc = Mathf.Sqrt(ex * ex + ey * ey);
            float tc = Mathf.Atan2(ey, ex);
            float corner = 1f + 0.07f * Mathf.Cos(18f * tc);
            if (rc < corner)
            {
                c = rc > corner - 0.07f ? Gold : Indigo;
                if (rc < corner - 0.07f)
                {
                    float fx = Repeat(x, 0.06f) - 0.03f, fy = Repeat(y, 0.06f) - 0.03f;
                    float flower = Mathf.Sqrt(fx * fx + fy * fy);
                    if (flower < 0.011f) c = Ivory;
                    else if (flower < 0.016f) c = MadderDark;
                }
            }

            // Pendentifs du médaillon.
            float pendant = ax / 0.075f + Mathf.Abs(ay - 0.6f) / 0.095f;
            if (pendant < 1f) c = pendant < 0.45f ? Gold : Indigo;

            // Médaillon central.
            float mx = x / 0.72f, my = y / 0.5f;
            float r = Mathf.Sqrt(mx * mx + my * my);
            float theta = Mathf.Atan2(my, mx);
            float star = 0.92f + 0.08f * Mathf.Cos(16f * theta);
            if (r < star)
            {
                if (r > star - 0.05f) c = Gold;
                else if (r > 0.72f)
                {
                    c = Indigo;
                    if (Mathf.Abs(r - 0.81f) < 0.035f && Mathf.Cos(24f * theta) > 0.6f) c = Ivory;
                }
                else if (r > 0.67f) c = Gold;
                else if (r > 0.3f)
                {
                    c = Ivory;
                    float petal = 0.3f + 0.3f * Mathf.Abs(Mathf.Cos(4f * theta));
                    if (r < petal) c = Madder;
                    if (r < petal && r > petal - 0.04f) c = MadderDark;
                    if (Mathf.Abs(Mathf.Cos(8f * theta)) > 0.94f && r > 0.42f && r < 0.62f) c = Teal;
                }
                else
                {
                    c = Indigo;
                    if (r < 0.2f * (0.55f + 0.45f * Mathf.Abs(Mathf.Cos(4f * theta)))) c = Gold;
                    if (r < 0.06f) c = Madder;
                }
            }
            return c;
        }

        // ------------------------------------------------------------------ Tapis de luxe en soie (3,2 m x 2,2 m)

        static readonly Color SilkIvory = Hex(0.93f, 0.89f, 0.80f);
        static readonly Color SilkNavy = Hex(0.07f, 0.11f, 0.30f);
        static readonly Color SilkWine = Hex(0.45f, 0.08f, 0.14f);
        static readonly Color SilkRose = Hex(0.78f, 0.42f, 0.45f);
        static readonly Color SilkSage = Hex(0.48f, 0.58f, 0.45f);
        static readonly Color SilkGold = Hex(0.88f, 0.72f, 0.38f);
        static readonly Color SilkSky = Hex(0.45f, 0.62f, 0.78f);

        /// <summary>
        /// Soie nouée très fine (nœuds de 3 mm, deux fois plus fins que le tapis oriental) : bordure bordeaux à arabesques
        /// dorées et palmettes, champ ivoire à treillis floral, médaillon étoilé à seize lobes, écoinçons et reflets soyeux.
        /// </summary>
        static Color LuxuryPixel(float u, float v)
        {
            const float hx = 1.6f, hy = 1.1f, knot = 0.003f;
            float x = (u - 0.5f) * hx * 2f, y = (v - 0.5f) * hy * 2f;
            int kx = Mathf.FloorToInt(x / knot), ky = Mathf.FloorToInt(y / knot);
            x = (kx + 0.5f) * knot;
            y = (ky + 0.5f) * knot;

            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            float d = Mathf.Min(hx - ax, hy - ay);
            float along = hx - ax < hy - ay ? y : x;
            Color c;

            if (d < 0.006f) c = SilkGold;
            else if (d < 0.034f)
            {
                bool dot = d > 0.014f && d < 0.026f && Repeat(along, 0.026f) < 0.012f;
                c = dot ? SilkIvory : SilkNavy;
            }
            else if (d < 0.04f) c = SilkGold;
            else if (d < 0.24f) c = LuxuryBorder(along, d - 0.14f);
            else if (d < 0.246f) c = SilkGold;
            else if (d < 0.272f)
            {
                float s = Repeat(along, 0.03f) - 0.015f, t = d - 0.259f;
                float r = Mathf.Sqrt(s * s + t * t);
                c = r < 0.0075f ? SilkNavy : r < 0.0105f ? SilkRose : SilkIvory;
            }
            else if (d < 0.278f) c = SilkNavy;
            else c = LuxuryField(x, y, ax, ay);

            // Soie : très faible abrash, reflets doux qui suivent le sens du velours.
            float knotNoise = 0.96f + 0.06f * Hash(kx, ky, 5);
            float abrash = 0.98f + 0.02f * Mathf.Sin(y * 5.1f + Fbm(u, v, 3, 2, 31) * 3f);
            float sheen = 1f + 0.05f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(x * 2.3f + y * 1.7f + Fbm(u, v, 2, 2, 41) * 2f)), 6f);
            return c * (knotNoise * abrash * sheen);
        }

        static Color LuxuryBorder(float along, float t)
        {
            Color c = SilkWine;
            float period = 0.2f;
            float s = Repeat(along, period) - period * 0.5f;
            // Arabesque dorée continue.
            float vine = t - 0.055f * Mathf.Sin(along / period * Mathf.PI * 2f);
            if (Mathf.Abs(vine) < 0.0045f) c = SilkGold;
            // Palmettes alternées (rose / sauge) entre les ondulations.
            bool odd = Mathf.Repeat(Mathf.Floor(along / period), 2f) > 0.5f;
            float ps = s, pt = t + (odd ? 0.035f : -0.035f);
            float pr = Mathf.Sqrt(ps * ps * 1.6f + pt * pt);
            float ptheta = Mathf.Atan2(pt, ps);
            float petal = 0.045f * (0.7f + 0.3f * Mathf.Abs(Mathf.Cos(3f * ptheta)));
            if (pr < petal) c = pr > petal - 0.008f ? SilkGold : odd ? SilkRose : SilkSage;
            if (pr < 0.012f) c = SilkIvory;
            // Petites fleurs bleues sur la tige.
            float fs = Repeat(along + period * 0.25f, period * 0.5f) - period * 0.25f, ft = vine;
            if (Mathf.Sqrt(fs * fs + ft * ft) < 0.009f) c = SilkSky;
            return c;
        }

        static Color LuxuryField(float x, float y, float ax, float ay)
        {
            Color c = SilkIvory;

            // Treillis floral en losanges : tiges sauge fines, palmettes aux nœuds.
            const float cell = 0.16f;
            float gx = Repeat(x, cell) - cell * 0.5f, gy = Repeat(y, cell) - cell * 0.5f;
            float lattice = Mathf.Abs(Mathf.Abs(gx) + Mathf.Abs(gy) - cell * 0.5f);
            if (lattice < 0.004f) c = SilkSage;
            float node = Mathf.Sqrt(gx * gx + gy * gy);
            float nodeTheta = Mathf.Atan2(gy, gx);
            if (node < 0.024f * (0.75f + 0.25f * Mathf.Cos(8f * nodeTheta))) c = SilkRose;
            if (node < 0.009f) c = SilkGold;
            float cornerX = Mathf.Abs(gx) - cell * 0.5f, cornerY = Mathf.Abs(gy) - cell * 0.5f;
            if (Mathf.Sqrt(cornerX * cornerX + cornerY * cornerY) < 0.012f) c = SilkNavy;

            // Écoinçons : quarts de médaillon bleu nuit à bord doré.
            const float fhx = 1.322f, fhy = 0.822f;
            float ex = (ax - fhx) / 0.5f, ey = (ay - fhy) / 0.36f;
            float rc = Mathf.Sqrt(ex * ex + ey * ey);
            float corner = 1f + 0.06f * Mathf.Cos(20f * Mathf.Atan2(ey, ex));
            if (rc < corner)
            {
                c = rc > corner - 0.06f ? SilkGold : SilkNavy;
                if (rc < corner - 0.06f && Mathf.Abs(rc - 0.55f) < 0.05f && Mathf.Cos(16f * Mathf.Atan2(ey, ex)) > 0.5f) c = SilkRose;
            }

            // Pendentifs du médaillon.
            float pendant = ax / 0.06f + Mathf.Abs(ay - 0.62f) / 0.1f;
            if (pendant < 1f) c = pendant < 0.45f ? SilkGold : SilkNavy;

            // Médaillon central à seize lobes.
            float mx = x / 0.68f, my = y / 0.5f;
            float r = Mathf.Sqrt(mx * mx + my * my);
            float theta = Mathf.Atan2(my, mx);
            float star = 0.9f + 0.1f * Mathf.Cos(16f * theta);
            if (r < star)
            {
                if (r > star - 0.045f) c = SilkGold;
                else if (r > 0.7f)
                {
                    c = SilkNavy;
                    if (Mathf.Abs(r - 0.78f) < 0.03f && Mathf.Cos(32f * theta) > 0.55f) c = SilkSky;
                }
                else if (r > 0.66f) c = SilkGold;
                else if (r > 0.28f)
                {
                    c = SilkIvory;
                    float petal = 0.28f + 0.32f * Mathf.Abs(Mathf.Cos(4f * theta));
                    if (r < petal) c = SilkRose;
                    if (r < petal && r > petal - 0.035f) c = SilkWine;
                    if (Mathf.Abs(Mathf.Cos(8f * theta)) > 0.95f && r > 0.4f && r < 0.62f) c = SilkSage;
                }
                else
                {
                    c = SilkNavy;
                    if (r < 0.2f * (0.55f + 0.45f * Mathf.Abs(Mathf.Cos(6f * theta)))) c = SilkGold;
                    if (r < 0.055f) c = SilkWine;
                }
            }
            return c;
        }

        // ------------------------------------------------------------------ Tapis classique (2,4 m x 1,6 m)

        static Color ClassicPixel(float u, float v)
        {
            const float hx = 1.2f, hy = 0.8f;
            float x = (u - 0.5f) * hx * 2f, y = (v - 0.5f) * hy * 2f;
            float d = Mathf.Min(hx - Mathf.Abs(x), hy - Mathf.Abs(y));
            Color sage = Hex(0.40f, 0.50f, 0.43f), charcoal = Hex(0.16f, 0.17f, 0.18f), cream = Hex(0.88f, 0.84f, 0.74f),
                terracotta = Hex(0.72f, 0.36f, 0.22f), mustard = Hex(0.80f, 0.62f, 0.25f);
            Color c;
            if (d < 0.07f) c = charcoal;
            else if (d < 0.085f) c = cream;
            else if (d < 0.11f) c = Repeat((Mathf.Abs(x) > Mathf.Abs(y) * 1.5f ? y : x), 0.05f) < 0.025f ? terracotta : charcoal;
            else if (d < 0.122f) c = cream;
            else
            {
                c = sage;
                float a = x + y, b = x - y;
                float ra = Repeat(a, 0.22f) - 0.11f, rb = Repeat(b, 0.22f) - 0.11f;
                int ia = Mathf.FloorToInt(a / 0.22f), ib = Mathf.FloorToInt(b / 0.22f);
                if (Mathf.Abs(ra) + Mathf.Abs(rb) < 0.05f) c = ((ia + ib) & 1) == 0 ? terracotta : mustard;
                if (Mathf.Abs(ra) < 0.006f || Mathf.Abs(rb) < 0.006f) c = cream;
            }
            float tone = 0.94f + 0.1f * Fbm(u, v, 6, 4, 41) + 0.04f * (Hash(Mathf.FloorToInt(u * 900), Mathf.FloorToInt(v * 600), 5) - 0.5f);
            return c * tone;
        }

        // ------------------------------------------------------------------ Tapis à poils longs (2,1 m x 1,4 m)

        static Color ShagPixel(float u, float v)
        {
            Color cream = Hex(0.84f, 0.78f, 0.68f), warm = Hex(0.76f, 0.68f, 0.56f);
            float cloud = Fbm(u * 1.5f, v, 3, 5, 61);
            float streak = Fbm(u * 6f, v * 0.7f, 8, 3, 77);
            Color c = Color.Lerp(cream, warm, Smooth(0.35f, 0.75f, cloud));
            float edge = Mathf.Min(Mathf.Min(u, 1f - u) * 2.1f, Mathf.Min(v, 1f - v) * 1.4f);
            c *= Mathf.Lerp(0.85f, 1f, Smooth(0f, 0.06f, edge));
            return c * (0.9f + 0.16f * streak);
        }

        // ------------------------------------------------------------------ Normales de fibres

        static float ShortFiberHeight(float u, float v)
        {
            // Touffes irrégulières (Worley) : pas de grille régulière, donc pas de moiré à distance.
            float tufts = 1f - Mathf.Clamp01(Cellular(u, v, 40, 13) * 1.3f);
            float fine = 1f - Mathf.Clamp01(Cellular(u, v, 97, 15) * 1.4f);
            return tufts * 0.45f + fine * 0.25f + ValueNoise(u, v, 128, 17) * 0.3f;
        }

        static float LongFiberHeight(float u, float v)
        {
            float warp = Fbm(u, v, 4, 3, 91) * 0.25f;
            float strands = ValueNoise(u * 1f + warp, v + warp * 0.5f, 96, 23);
            return Mathf.Pow(strands, 1.6f) * 0.8f + Fbm(u, v, 16, 3, 29) * 0.4f;
        }

        // ------------------------------------------------------------------ Détail de saleté (linéaire)

        static Color DirtDetailPixel(float u, float v)
        {
            float blotch = Smooth(0.25f, 0.75f, Fbm(u, v, 4, 5, 101));
            float speckle = Mathf.Clamp01(ValueNoise(u, v, 96, 103) * 0.6f + ValueNoise(u, v, 192, 107) * 0.6f - 0.1f);
            float bubbles = Mathf.Max(1f - Cellular(u, v, 24, 109) * 1.6f, 1f - Cellular(u, v, 52, 113) * 1.9f);
            float grain = ValueNoise(u, v, 64, 127);
            return new Color(blotch, speckle, Mathf.Clamp01(bubbles), grain);
        }

        // ------------------------------------------------------------------ Atelier

        static Color ConcretePixel(float u, float v)
        {
            float large = Fbm(u, v, 3, 5, 201);
            float stain = Smooth(0.55f, 0.8f, Fbm(u, v, 5, 4, 207));
            float speck = Hash(Mathf.FloorToInt(u * 1024), Mathf.FloorToInt(v * 1024), 211);
            float g = 0.50f + (large - 0.5f) * 0.16f - stain * 0.08f + (speck > 0.97f ? 0.08f : speck < 0.03f ? -0.08f : 0f);
            return new Color(g * 1.0f, g * 0.985f, g * 0.95f, 1f);
        }

        static float ConcreteHeight(float u, float v) => Fbm(u, v, 16, 4, 221) * 0.6f + ValueNoise(u, v, 256, 223) * 0.4f;

        static Color PlasterPixel(float u, float v)
        {
            float n = Fbm(u, v, 4, 5, 301);
            float g = 0.84f + (n - 0.5f) * 0.08f;
            return new Color(g, g * 0.98f, g * 0.94f, 1f);
        }

        static Color WoodPixel(float u, float v)
        {
            int plank = Mathf.FloorToInt(v * 4f);
            float tone = 0.85f + 0.3f * Hash(plank, 0, 401);
            float grain = Fbm(u * 0.5f, v * 4f, 2, 3, 403 + plank);
            float rings = 0.5f + 0.5f * Mathf.Sin((u * 26f + grain * 9f) + plank * 3f);
            Color c = Color.Lerp(Hex(0.50f, 0.33f, 0.19f), Hex(0.62f, 0.43f, 0.26f), rings) * tone;
            float seam = Repeat(v * 4f, 1f);
            if (seam < 0.02f || seam > 0.98f) c *= 0.55f;
            return c;
        }

        static Color GratePixel(float u, float v)
        {
            float slot = Repeat(u * 8f, 1f);
            bool hole = slot > 0.2f && slot < 0.8f && v > 0.08f && v < 0.92f;
            float g = hole ? 0.04f : 0.32f + 0.08f * ValueNoise(u, v, 16, 501);
            return new Color(g, g, g * 1.05f, 1f);
        }

        static Color HazardPixel(float u, float v)
        {
            // Bandes diagonales jaune / noir, usées par endroits (texture tuilable de 0,5 m).
            bool yellow = Repeat(u + v, 0.5f) < 0.25f;
            float wear = Smooth(0.62f, 0.8f, Fbm(u, v, 6, 4, 601));
            Color c = yellow ? Hex(0.93f, 0.7f, 0.1f) : Hex(0.08f, 0.08f, 0.08f);
            return Color.Lerp(c, Hex(0.45f, 0.45f, 0.43f), wear * 0.6f) * (0.92f + 0.1f * ValueNoise(u, v, 64, 607));
        }

        static Color ClockFacePixel(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float r = Mathf.Sqrt(x * x + y * y) * 2f;
            if (r > 1f) return Hex(0.1f, 0.1f, 0.11f);
            if (r > 0.92f) return Hex(0.15f, 0.15f, 0.17f);
            float angle = Mathf.Atan2(y, x) / (Mathf.PI * 2f) * 60f;
            float minute = Mathf.Abs(angle - Mathf.Round(angle));
            bool hourMark = Mathf.Abs(angle / 5f - Mathf.Round(angle / 5f)) * 5f < 0.18f;
            if (r > 0.72f && r < 0.86f && hourMark) return Hex(0.12f, 0.12f, 0.13f);
            if (r > 0.8f && r < 0.86f && minute < 0.08f) return Hex(0.3f, 0.3f, 0.32f);
            return Hex(0.95f, 0.94f, 0.9f);
        }

        static Color PaperPixel(float u, float v)
        {
            float g = 0.93f + 0.04f * Fbm(u, v, 8, 3, 701) - 0.03f * ValueNoise(u, v, 128, 703);
            return new Color(g, g * 0.985f, g * 0.95f, 1f);
        }

        static Color RoundedPixel(float u, float v)
        {
            const float radius = 0.3f;
            float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
            float dx = Mathf.Max(0f, x - (1f - radius)), dy = Mathf.Max(0f, y - (1f - radius));
            float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
            return new Color(1f, 1f, 1f, 1f - Smooth(0.85f, 1f, d));
        }
    }
}
