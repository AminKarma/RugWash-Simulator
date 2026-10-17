using System.Collections.Generic;
using RugWash.Workshop;
using UnityEditor;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>
    /// Décor de l'atelier par paliers (v0.1.2 jalon 5), du cagibi au penthouse avec vue sur Los Angeles. Chaque palier est
    /// un groupe d'objets réels activé par <see cref="WorkshopDecor"/> ; aucun ne gêne les postes, l'aire de lavage, le
    /// bureau ou la porte (emplacements choisis dans les zones libres de la salle).
    /// </summary>
    public static class RugWashWorkshopTiers
    {
        const float HalfX = RugWashSceneBuilder.RoomHalfX, HalfZ = RugWashSceneBuilder.RoomHalfZ, Height = RugWashSceneBuilder.RoomHeight;

        static GameObject Prop(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Mesh Box(MeshStore m, Vector3 size, float radius = 0.01f) =>
            m.Add($"Tier_RB_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}", () => RoundedBox(size, radius));

        static Mesh Cyl(MeshStore m, float radius, float height) => m.Add($"Tier_Cyl_{radius:0.###}x{height:0.###}", () => Cylinder(radius, height, 20));

        static Mesh Quad(MeshStore m, Vector2 size) => m.Add($"Tier_Quad_{size.x:0.##}x{size.y:0.##}", () => BuildQuad(size));

        static Mesh BuildQuad(Vector2 size)
        {
            var mesh = new Mesh();
            float x = size.x * 0.5f, y = size.y * 0.5f;
            mesh.vertices = new[] { new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f), new Vector3(x, y, 0f), new Vector3(-x, y, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static void Text(Transform parent, string text, Vector3 position, Quaternion rotation, float size, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = UIFont;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = color;
            DepthTestedText(mesh);
        }

        static Light PointLight(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            return light;
        }

        static void BoxStack(Transform parent, MeshStore m, Material material, Vector3 at, params Vector3[] sizes)
        {
            float y = 0f;
            for (int i = 0; i < sizes.Length; i++)
            {
                Prop("Box", parent, Box(m, sizes[i], 0.015f), material, at + new Vector3(0f, y + sizes[i].y * 0.5f, 0f), new Vector3(0f, i * 9f - 6f, 0f));
                y += sizes[i].y;
            }
        }

        /// <summary>Construit les paliers et le composant de décor ; <paramref name="room"/> contient les fenêtres d'origine.</summary>
        public static WorkshopDecor Build(Transform parent, Transform room, RugWashContentBuilder.Content c, MeshStore m, List<Light> roomLights)
        {
            var root = Group("WorkshopDecor", parent);
            var groups = new List<WorkshopDecor.TierGroup>();
            void Add(Transform group, int min, int max) => groups.Add(new WorkshopDecor.TierGroup { root = group.gameObject, minTier = min, maxTier = max });

            // ---- Palier 0 : cagibi encombré, fenêtres condamnées, ampoule nue, murs tachés.
            var cagibi = Group("Tier0_Cagibi", root);
            BoxStack(cagibi, m, c["Cardboard"], new Vector3(-4.3f, 0f, HalfZ - 0.6f), new Vector3(0.62f, 0.46f, 0.5f), new Vector3(0.52f, 0.4f, 0.45f), new Vector3(0.4f, 0.3f, 0.38f));
            BoxStack(cagibi, m, c["Cardboard"], new Vector3(-2.95f, 0f, HalfZ - 0.45f), new Vector3(0.56f, 0.5f, 0.45f), new Vector3(0.46f, 0.36f, 0.4f));
            BoxStack(cagibi, m, c["Cardboard"], new Vector3(0.95f, 0f, -HalfZ + 0.45f), new Vector3(0.6f, 0.44f, 0.5f), new Vector3(0.5f, 0.42f, 0.42f), new Vector3(0.36f, 0.28f, 0.34f));
            BoxStack(cagibi, m, c["Cardboard"], new Vector3(HalfX - 0.6f, 0f, HalfZ - 0.7f), new Vector3(0.6f, 0.5f, 0.5f), new Vector3(0.5f, 0.38f, 0.44f));
            foreach (float x in new[] { -3.6f, 0f })
                for (int i = 0; i < 3; i++)
                    Prop("Plank", cagibi, Box(m, new Vector3(2.02f, 0.2f, 0.03f), 0.006f), c["Plywood"], new Vector3(x, 2.38f + i * 0.27f, HalfZ - 0.07f),
                        new Vector3(0f, 0f, i == 1 ? -3f : 4f));
            Prop("BulbCable", cagibi, Cyl(m, 0.006f, 0.55f), c["Rubber"], new Vector3(-1f, Height - 0.275f, -2.6f));
            Prop("Bulb", cagibi, m.Add("Tier_Bulb", () => Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.01f), new Vector2(0.04f, 0.05f), new Vector2(0.015f, 0.09f), new Vector2(0f, 0.09f) }, 16)),
                c["WarmBulb"], new Vector3(-1f, Height - 0.64f, -2.6f));
            PointLight(cagibi, "BulbLight", new Vector3(-1f, Height - 0.7f, -2.6f), new Color(1f, 0.78f, 0.52f), 1.6f, 7f);
            // Taches d'humidité : quadrilatères tournés vers la salle, découpés par la texture (contour irrégulier).
            Prop("Stain", cagibi, Quad(m, new Vector2(1.3f, 1.1f)), c["WallStain"], new Vector3(0.4f, 1.5f, -HalfZ + 0.012f), new Vector3(0f, 180f, 0f));
            Prop("Stain", cagibi, Quad(m, new Vector2(1.5f, 1.1f)), c["WallStain"], new Vector3(-HalfX + 0.012f, 2.75f, 0.6f), new Vector3(0f, -90f, 0f));
            Prop("Stain", cagibi, Quad(m, new Vector2(1.1f, 0.95f)), c["WallStain"], new Vector3(2.4f, 1.3f, HalfZ - 0.012f));
            // Hors de l'ouverture vers l'annexe (grande mise à jour : z de -2,15 à -0,75).
            Prop("Stain", cagibi, Quad(m, new Vector2(1.2f, 1.1f)), c["WallStain"], new Vector3(HalfX - 0.012f, 2.8f, -3.0f), new Vector3(0f, 90f, 0f));
            Add(cagibi, 0, 0);

            // ---- Palier 1 et plus : rangement (étagère neuve à la place des cartons), tableau de liège.
            var tidy = Group("Tier1_Range", root);
            var shelf = Group("Shelving", tidy);
            shelf.localPosition = new Vector3(-4.3f, 0f, HalfZ - 0.3f);
            foreach (float x in new[] { -0.78f, 0.78f })
                Prop("Upright", shelf, Box(m, new Vector3(0.04f, 1.9f, 0.4f), 0.008f), c["Steel"], new Vector3(x, 0.95f, 0f));
            for (int i = 0; i < 4; i++)
            {
                Prop("Shelf", shelf, Box(m, new Vector3(1.6f, 0.03f, 0.42f), 0.006f), c["Wood"], new Vector3(0f, 0.25f + i * 0.5f, 0f));
                for (int b = 0; b < 3; b++)
                    Prop("Bin", shelf, Box(m, new Vector3(0.42f, 0.22f, 0.32f), 0.02f), b == 1 ? c["BluePlastic"] : c["GreenPlastic"],
                        new Vector3(-0.5f + b * 0.5f, 0.38f + i * 0.5f, 0f));
            }
            Prop("Corkboard", tidy, Box(m, new Vector3(1.1f, 0.75f, 0.03f), 0.01f), c["Wood"], new Vector3(1.4f, 1.55f, -HalfZ + 0.06f));
            for (int i = 0; i < 4; i++)
                Prop("Note", tidy, Box(m, new Vector3(0.18f, 0.22f, 0.004f), 0.002f), c["Paper"], new Vector3(1.05f + i * 0.24f, 1.55f + (i % 2) * 0.12f, -HalfZ + 0.085f),
                    new Vector3(0f, 0f, i * 5f - 7f));
            Add(tidy, 1, 4);

            // ---- Palier 2 et plus : atelier professionnel (enseigne, banc d'attente, coin café, plantes).
            var pro = Group("Tier2_Pro", root);
            var sign = Group("Banner", pro);
            sign.localPosition = new Vector3(-HalfX + 0.03f, 2.95f, 0.65f);
            sign.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Prop("Panel", sign, Box(m, new Vector3(3.2f, 0.5f, 0.03f), 0.02f), c["Signage"], Vector3.zero);
            Text(sign, "RUGWASH · NETTOYAGE DE TAPIS", new Vector3(0f, 0f, -0.02f), Quaternion.identity, 0.03f, new Color(1f, 0.85f, 0.35f));
            var bench = Group("Bench", pro);
            bench.localPosition = new Vector3(-1.3f, 0f, -HalfZ + 0.35f);
            Prop("Seat", bench, Box(m, new Vector3(1.3f, 0.06f, 0.4f), 0.015f), c["Wood"], new Vector3(0f, 0.45f, 0f));
            Prop("Back", bench, Box(m, new Vector3(1.3f, 0.4f, 0.05f), 0.015f), c["Wood"], new Vector3(0f, 0.72f, -0.2f), new Vector3(-8f, 0f, 0f));
            foreach (float x in new[] { -0.55f, 0.55f })
                Prop("Leg", bench, Box(m, new Vector3(0.05f, 0.45f, 0.36f), 0.008f), c["Steel"], new Vector3(x, 0.225f, 0f));
            var coffee = Group("Coffee", pro);
            coffee.localPosition = new Vector3(0.3f, 0f, -HalfZ + 0.32f);
            Prop("Counter", coffee, Box(m, new Vector3(0.8f, 0.9f, 0.45f), 0.015f), c["DoorPaint"], new Vector3(0f, 0.45f, 0f));
            Prop("Machine", coffee, Box(m, new Vector3(0.3f, 0.36f, 0.3f), 0.03f), c["DarkMetal"], new Vector3(-0.15f, 1.08f, 0f));
            Prop("Cup", coffee, Cyl(m, 0.035f, 0.08f), c["WhitePlastic"], new Vector3(0.18f, 0.94f, 0.05f));
            foreach (var at in new[] { new Vector3(1.25f, 0f, -HalfZ + 0.35f), new Vector3(HalfX - 0.4f, 0f, -HalfZ + 0.4f) })
            {
                var plant = Group("Plant", pro);
                plant.localPosition = at;
                Prop("Pot", plant, m.Add("Tier_Pot", () => Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.2f, 0.36f), new Vector2(0.18f, 0.36f), new Vector2(0f, 0.34f) }, 20)),
                    c["Terracotta"], Vector3.zero);
                for (int i = 0; i < 6; i++)
                    Prop("Leaf", plant, Box(m, new Vector3(0.1f, 0.55f, 0.02f), 0.03f), c["Plant"], new Vector3(0f, 0.6f, 0f),
                        new Vector3(25f, i * 60f, 0f));
            }
            Add(pro, 2, 4);

            // ---- Palier 3 : loft industriel (mur de briques, coin salon, suspensions, tapis exposés).
            var loft = Group("Tier3_Loft", root);
            Prop("BrickWall", loft, Box(m, new Vector3(5.3f, Height - 0.1f, 0.02f), 0.002f), c["Brick"], new Vector3(0.22f, (Height - 0.1f) * 0.5f, -HalfZ + 0.015f));
            var lounge = Group("Lounge", loft);
            lounge.localPosition = new Vector3(-4.3f, 0f, 3.45f);
            Prop("Rug", lounge, Box(m, new Vector3(2.2f, 0.01f, 1.5f), 0.005f), c["RugOriental"], new Vector3(0f, 0.005f, -0.35f));
            Prop("SofaBase", lounge, Box(m, new Vector3(1.9f, 0.4f, 0.8f), 0.06f), c["Leather"], new Vector3(0f, 0.22f, 0.15f));
            Prop("SofaBack", lounge, Box(m, new Vector3(1.9f, 0.5f, 0.2f), 0.06f), c["Leather"], new Vector3(0f, 0.6f, 0.48f));
            foreach (float x in new[] { -0.95f, 0.95f })
                Prop("SofaArm", lounge, Box(m, new Vector3(0.18f, 0.55f, 0.8f), 0.06f), c["Leather"], new Vector3(x, 0.3f, 0.15f));
            Prop("Table", lounge, Box(m, new Vector3(0.9f, 0.05f, 0.5f), 0.02f), c["Wood"], new Vector3(0f, 0.42f, -0.65f));
            foreach (float x in new[] { -0.38f, 0.38f })
                Prop("TableLeg", lounge, Box(m, new Vector3(0.04f, 0.4f, 0.4f), 0.006f), c["DarkMetal"], new Vector3(x, 0.2f, -0.65f));
            foreach (float x in new[] { -4.9f, -3.7f })
            {
                Prop("PendantCable", loft, Cyl(m, 0.005f, 0.7f), c["Rubber"], new Vector3(x, Height - 0.35f, 3.2f));
                Prop("PendantShade", loft, m.Add("Tier_Shade", () => Lathe(new[] { new Vector2(0.02f, 0.18f), new Vector2(0.05f, 0.17f), new Vector2(0.17f, 0f), new Vector2(0.16f, 0f) }, 20, capBottom: false, capTop: false)),
                    c["DarkMetal"], new Vector3(x, Height - 0.88f, 3.2f));
                Prop("PendantBulb", loft, Cyl(m, 0.03f, 0.05f), c["WarmBulb"], new Vector3(x, Height - 0.86f, 3.2f));
                PointLight(loft, "PendantLight", new Vector3(x, Height - 0.95f, 3.2f), new Color(1f, 0.8f, 0.58f), 1.2f, 5f);
            }
            Add(loft, 3, 4);
            var art = Group("Tier3_Artworks", root);
            // Tableau avant décalé vers le bureau : il ne recouvre pas l'ouverture vers l'annexe.
            foreach (var (z, material) in new[] { (-3.4f, c["RugClassic"]), (3.0f, c["RugShag"]) })
            {
                Prop("Frame", art, Box(m, new Vector3(0.04f, 0.9f, 1.3f), 0.01f), c["DarkMetal"], new Vector3(HalfX - 0.03f, 2.55f, z));
                Prop("Canvas", art, Box(m, new Vector3(0.01f, 0.8f, 1.2f), 0.002f), material, new Vector3(HalfX - 0.055f, 2.55f, z));
            }
            Add(art, 3, 3);

            // ---- Palier 4 : penthouse, baies panoramiques sur Los Angeles au crépuscule.
            var penthouse = Group("Tier4_Penthouse", root);
            var back = Group("PanoramaBack", penthouse);
            back.localPosition = new Vector3(-2.1f, 3.0f, HalfZ - 0.04f);
            // Vue seule (pas de vitre : le matériau « Glass » de l'atelier est opaque et masquerait la ville).
            Prop("View", back, Quad(m, new Vector2(7f, 0.9f)), c["Skyline"], Vector3.zero);
            for (int i = 0; i <= 5; i++)
                Prop("Mullion", back, Box(m, new Vector3(0.05f, 0.95f, 0.05f), 0.01f), c["DarkMetal"], new Vector3(-3.5f + i * 1.4f, 0f, -0.02f));
            foreach (float y in new[] { -0.47f, 0.47f })
                Prop("Rail", back, Box(m, new Vector3(7.1f, 0.05f, 0.06f), 0.01f), c["DarkMetal"], new Vector3(0f, y, -0.02f));
            var side = Group("PanoramaRight", penthouse);
            side.localPosition = new Vector3(HalfX - 0.04f, 2.9f, 0f);
            side.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Prop("View", side, Quad(m, new Vector2(7.2f, 1.1f)), c["Skyline"], Vector3.zero);
            for (int i = 0; i <= 4; i++)
                Prop("Mullion", side, Box(m, new Vector3(0.05f, 1.15f, 0.05f), 0.01f), c["DarkMetal"], new Vector3(-3.6f + i * 1.8f, 0f, -0.02f));
            foreach (float y in new[] { -0.57f, 0.57f })
                Prop("Rail", side, Box(m, new Vector3(7.3f, 0.05f, 0.06f), 0.01f), c["DarkMetal"], new Vector3(0f, y, -0.02f));
            Prop("GoldTrimBack", penthouse, Box(m, new Vector3(HalfX * 2f, 0.03f, 0.02f), 0.004f), c["Gold"], new Vector3(0f, 1.12f, HalfZ - 0.03f));
            Prop("GoldTrimLeft", penthouse, Box(m, new Vector3(0.02f, 0.03f, HalfZ * 2f), 0.004f), c["Gold"], new Vector3(-HalfX + 0.03f, 1.12f, 0f));
            var palm = Group("IndoorPalm", penthouse);
            palm.localPosition = new Vector3(0.9f, 0f, HalfZ - 0.5f);
            Prop("Pot", palm, m.Add("Tier_BigPot", () => Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.22f, 0f), new Vector2(0.26f, 0.5f), new Vector2(0.24f, 0.5f), new Vector2(0f, 0.47f) }, 24)),
                c["DarkMetal"], Vector3.zero);
            Prop("Trunk", palm, Cyl(m, 0.05f, 1.4f), c["Wood"], new Vector3(0f, 1.15f, 0f));
            for (int i = 0; i < 7; i++)
                Prop("Frond", palm, Box(m, new Vector3(0.16f, 0.9f, 0.02f), 0.05f), c["Plant"], Quaternion.Euler(0f, i * 51f, 0f) * new Vector3(0f, 1.95f, 0.25f),
                    new Vector3(60f, i * 51f, 0f));
            var plaque = Group("TowerSign", penthouse);
            plaque.localPosition = new Vector3(0.2f, 3.15f, -HalfZ + 0.06f);
            Prop("Panel", plaque, Box(m, new Vector3(2.6f, 0.4f, 0.03f), 0.02f), c["DarkMetal"], Vector3.zero);
            Text(plaque, "RUGWASH TOWER · LOS ANGELES", new Vector3(0f, 0f, 0.02f), Quaternion.Euler(0f, 180f, 0f), 0.026f, new Color(0.95f, 0.78f, 0.38f));
            Add(penthouse, 4, 4);

            // Fenêtres hautes d'origine : remplacées par les baies panoramiques au palier 4.
            var windows = Group("Tier0to3_Windows", root);
            foreach (string name in new[] { "Window_-4_Frame", "Window_0_Frame" })
            {
                var window = room != null ? room.Find(name) : null;
                if (window != null) window.SetParent(windows, true);
            }
            Add(windows, 0, 3);

            var decor = root.gameObject.AddComponent<WorkshopDecor>();
            var serialized = new SerializedObject(decor);
            var list = serialized.FindProperty("groups");
            list.arraySize = groups.Count;
            for (int i = 0; i < groups.Count; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("root").objectReferenceValue = groups[i].root;
                entry.FindPropertyRelative("minTier").intValue = groups[i].minTier;
                entry.FindPropertyRelative("maxTier").intValue = groups[i].maxTier;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            WireList(decor, "roomLights", roomLights);
            // État de départ dans la scène : palier 0 visible seul (le jeu applique ensuite le palier du joueur).
            foreach (var group in groups) group.root.SetActive(group.minTier == 0);
            return decor;
        }
    }
}
