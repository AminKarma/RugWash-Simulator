using UnityEditor;
using UnityEngine;
using L = RugWash.Editor.RugWashSurfaceLibrary;

namespace RugWash.Editor
{
    /// <summary>
    /// V0.3.0 — direction artistique « atelier artisanal moderne » appliquée aux matériaux générés : réalisme stylisé,
    /// surfaces PBR réelles pour le bâti (béton, plâtre peint, tôle, bois), plastiques et métaux unis enrichis d'un
    /// micro-relief et d'une légère patine, palette resserrée autour du pétrole (marque), du safran (accent) et du crème.
    /// Appelé à chaque génération du contenu : les valeurs ci-dessous font foi (les matériaux sont des assets générés).
    /// </summary>
    public static class RugWashArtDirection
    {
        // Palette (sRGB).
        public static readonly Color Petrol = new Color(0.118f, 0.306f, 0.361f);
        public static readonly Color PetrolDeep = new Color(0.086f, 0.227f, 0.271f);
        public static readonly Color Saffron = new Color(0.949f, 0.663f, 0.231f);
        public static readonly Color Cream = new Color(0.953f, 0.922f, 0.867f);
        public static readonly Color Charcoal = new Color(0.082f, 0.098f, 0.118f);
        public static readonly Color Copper = new Color(0.761f, 0.392f, 0.227f);

        static void Lit(Material m, Color color, float smoothness, float metallic = 0f)
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(m);
        }

        /// <summary>Plastique moulé : micro-grain, patine très légère.</summary>
        static void Plastic(Material m, Color color, float smoothness)
        {
            L.Clear(m);
            Lit(m, color, smoothness);
            L.Detail(m, "Plastic", "Grime", 9f, normalScale: 0.35f, albedoScale: 0.8f);
        }

        static void Metal(Material m, Color color, float smoothness, float metallic, string micro = "Brushed", float strength = 0.3f)
        {
            L.Clear(m);
            Lit(m, color, smoothness, metallic);
            L.Detail(m, micro, "Grime", 6f, normalScale: strength, albedoScale: 0.6f);
        }

        /// <summary>Peinture sur métal (poudre époxy) : texture de tôle peinte, teinte libre, non métallique.</summary>
        static void PaintedMetal(Material m, Color color, float smoothness, float tile = 1.2f)
        {
            L.Clear(m);
            Lit(m, color, smoothness);
            L.Apply(m, "PaintedMetal", tile, normalScale: 0.6f, smoothness: smoothness * 1.4f, occlusion: 0.6f);
            L.Detail(m, "Plastic", "Wear", 3f, normalScale: 0.25f, albedoScale: 0.9f);
        }

        static void Surface(Material m, string alias, Color tint, float tile, float smoothness = 1f, float normal = 1f, float occlusion = 1f)
        {
            L.Clear(m);
            Lit(m, tint, smoothness);
            L.Apply(m, alias, tile, normal, smoothness, occlusion);
        }

        static void Fabric(Material m, Color color, string alias, float tile, float smoothness)
        {
            L.Clear(m);
            Lit(m, color, smoothness);
            L.Apply(m, alias, tile, normalScale: 1f, smoothness: smoothness * 2f, occlusion: 0.8f);
        }

        public static void Apply(RugWashContentBuilder.Content c)
        {
            L.ConfigureImports();
            var m = c.Materials;
            Material Get(string key) => m.TryGetValue(key, out var material) ? material : null;
            void Do(string key, System.Action<Material> action)
            {
                var material = Get(key);
                if (material != null && material.shader != null && material.shader.name == "Universal Render Pipeline/Lit") action(material);
            }

            // Bâti.
            Do("Concrete", x => Surface(x, "Floor", new Color(0.93f, 0.91f, 0.88f), 3f, smoothness: 0.7f));
            Do("WashPad", x => Surface(x, "WashPad", new Color(0.86f, 0.88f, 0.9f), 2.5f, smoothness: 0.8f));
            Do("Plaster", x => Surface(x, "Plaster", new Color(0.95f, 0.9f, 0.82f), 2f, smoothness: 0.35f, normal: 0.8f));
            Do("Wainscot", x => Surface(x, "Plaster", new Color(0.16f, 0.36f, 0.4f), 2f, smoothness: 1.1f, normal: 0.6f));
            Do("Ceiling", x => Surface(x, "CeilingSheet", new Color(0.7f, 0.72f, 0.74f), 2f, smoothness: 0.8f));
            Do("Brick", x => Surface(x, "Brick", Color.white, 3f, smoothness: 0.6f));
            Do("Wood", x => Surface(x, "Wood", new Color(1f, 0.93f, 0.85f), 0.6f, smoothness: 0.85f));
            Do("Plywood", x => Surface(x, "Plywood", Color.white, 0.5f, smoothness: 0.6f));
            Do("Pegboard", x => Surface(x, "Plywood", new Color(0.92f, 0.8f, 0.64f), 0.5f, smoothness: 0.5f));
            Do("Leather", x => Surface(x, "Leather", new Color(0.62f, 0.4f, 0.3f), 0.4f, smoothness: 1f));
            Do("Boot", x => Surface(x, "Leather", new Color(0.32f, 0.22f, 0.16f), 0.3f, smoothness: 0.9f));

            // Métaux.
            Do("Steel", x => Metal(x, new Color(0.68f, 0.7f, 0.72f), 0.66f, 0.9f));
            Do("Chrome", x => Metal(x, new Color(0.86f, 0.87f, 0.89f), 0.9f, 1f, strength: 0.08f));
            Do("Gold", x => Metal(x, new Color(1f, 0.78f, 0.38f), 0.84f, 1f, strength: 0.12f));
            Do("DarkMetal", x => PaintedMetal(x, new Color(0.17f, 0.185f, 0.2f), 0.45f));
            Do("DoorPaint", x => PaintedMetal(x, PetrolDeep, 0.5f, 2f));
            Do("Signage", x => PaintedMetal(x, Charcoal, 0.4f));
            Do("Hair", x => { L.Clear(x); Lit(x, new Color(0.17f, 0.12f, 0.085f), 0.32f); L.Detail(x, "Brushed", null, 60f, normalScale: 0.6f); });
            Do("BeamPaint", x => PaintedMetal(x, new Color(0.38f, 0.41f, 0.44f), 0.45f, 2f));
            // Fenêtres : verre armé dépoli, lumineux (émission texturée), au lieu d'un aplat.
            Do("Window", x =>
            {
                L.Clear(x);
                var glass = AssetDatabase.LoadAssetAtPath<Texture2D>(L.Folder + "/Window_Wired.png");
                Lit(x, new Color(0.72f, 0.8f, 0.88f), 0.85f);
                x.SetTexture("_BaseMap", glass);
                x.SetTextureScale("_BaseMap", new Vector2(2.2f, 2.2f));
                x.SetTexture("_EmissionMap", glass);
                x.SetColor("_EmissionColor", new Color(0.62f, 0.74f, 0.9f) * 0.95f);
                x.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(x);
            });

            // Plastiques (outils, machines, accessoires).
            Do("SafetyYellow", x => Plastic(x, Saffron, 0.5f));
            Do("BluePlastic", x => Plastic(x, new Color(0.11f, 0.33f, 0.56f), 0.55f));
            Do("WhitePlastic", x => Plastic(x, new Color(0.9f, 0.9f, 0.88f), 0.5f));
            Do("RedPlastic", x => Plastic(x, new Color(0.74f, 0.19f, 0.13f), 0.52f));
            Do("GreenPlastic", x => Plastic(x, new Color(0.2f, 0.5f, 0.36f), 0.52f));
            Do("RobotShell", x => Plastic(x, new Color(0.92f, 0.92f, 0.9f), 0.62f));
            Do("RobotDark", x => Plastic(x, new Color(0.08f, 0.085f, 0.09f), 0.55f));
            Do("FoamCanister", x => Plastic(x, new Color(0.84f, 0.9f, 0.94f), 0.8f));
            Do("Terracotta", x => Plastic(x, Copper, 0.2f));
            Do("Plant", x => Plastic(x, new Color(0.22f, 0.44f, 0.2f), 0.35f));
            foreach (var (key, color) in new[]
            {
                ("FloorMarking", Saffron), ("StepBlue", new Color(0.2f, 0.42f, 0.7f)), ("StepGreen", new Color(0.27f, 0.58f, 0.38f)),
                ("StepOrange", new Color(0.92f, 0.52f, 0.18f)), ("StepCyan", new Color(0.2f, 0.62f, 0.68f)), ("StepPurple", new Color(0.5f, 0.34f, 0.66f)),
            })
                Do(key, x => Plastic(x, color, 0.35f));

            // Caoutchoucs, textiles.
            Do("Rubber", x => { L.Clear(x); Lit(x, new Color(0.075f, 0.075f, 0.08f), 0.32f); L.Detail(x, "Rubber", "Grime", 10f, normalScale: 0.6f); });
            Do("GreyHose", x => { L.Clear(x); Lit(x, new Color(0.3f, 0.32f, 0.34f), 0.4f); L.Detail(x, "Rubber", "Grime", 10f, normalScale: 0.45f); });
            Do("Bristle", x => { L.Clear(x); Lit(x, new Color(0.88f, 0.58f, 0.2f), 0.3f); L.Detail(x, "Brushed", null, 20f, normalScale: 0.6f); });
            Do("Cardboard", x => { L.Clear(x); Lit(x, new Color(0.64f, 0.48f, 0.32f), 0.12f); L.Detail(x, "Cardboard", "Wear", 4f, normalScale: 0.5f); });
            Do("Workwear", x => Fabric(x, new Color(0.17f, 0.27f, 0.38f), "Twill", 0.22f, 0.2f));
            Do("CapFabric", x => Fabric(x, new Color(0.76f, 0.22f, 0.15f), "Twill", 0.18f, 0.2f));
            Do("Shirt", x => { L.Clear(x); Lit(x, new Color(0.7f, 0.72f, 0.74f), 0.15f); L.Detail(x, "Knit", "Grime", 30f, normalScale: 0.5f); });
            Do("Glove", x => { L.Clear(x); Lit(x, new Color(0.86f, 0.5f, 0.17f), 0.3f); L.Detail(x, "Knit", "Grime", 40f, normalScale: 0.6f); });
            Do("GloveCuff", x => { L.Clear(x); Lit(x, new Color(0.15f, 0.19f, 0.26f), 0.2f); L.Detail(x, "Knit", null, 40f, normalScale: 0.5f); });
            Do("Skin", x => { L.Clear(x); Lit(x, new Color(0.87f, 0.68f, 0.55f), 0.38f); L.Detail(x, "Plastic", null, 30f, normalScale: 0.15f); });
            Do("Reflective", x => { L.Clear(x); Lit(x, new Color(0.86f, 0.9f, 0.42f), 0.6f); L.Detail(x, "Knit", null, 40f, normalScale: 0.3f); });
            AssetDatabase.SaveAssets();
        }
    }
}
