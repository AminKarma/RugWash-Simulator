using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Effects;
using RugWash.Player;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>Habillage de l'atelier : postes, mobilier et décor construits en maillages procéduraux détaillés.</summary>
    public static class RugWashDecor
    {
        const float HalfX = RugWashSceneBuilder.RoomHalfX, HalfZ = RugWashSceneBuilder.RoomHalfZ, Height = RugWashSceneBuilder.RoomHeight;

        static GameObject Prop(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 euler = default,
            bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<BoxCollider>();
            MakeStatic(go);
            return go;
        }

        static Mesh Rounded(MeshStore store, Vector3 size, float radius) =>
            store.Add($"RB_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}", () => RoundedBox(size, radius));

        static Mesh Cyl(MeshStore store, float radius, float height) =>
            store.Add($"Cyl_{radius:0.###}x{height:0.###}", () => Cylinder(radius, height, 20));

        static Transform Group(string name, Transform parent, Vector3 position, float yaw = 0f)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        /// <summary>Fût / réservoir côtelé (révolution).</summary>
        static Mesh RibbedDrum(MeshStore store, string key, float radius, float height, int ribs, float neck) =>
            store.Add(key, () =>
            {
                var profile = new List<Vector2> { new Vector2(0f, 0f), new Vector2(radius * 0.92f, 0f), new Vector2(radius, 0.02f) };
                for (int i = 1; i <= ribs * 2; i++)
                {
                    float y = 0.02f + (height * 0.86f) * i / (ribs * 2f);
                    profile.Add(new Vector2(i % 2 == 0 ? radius : radius * 1.03f, y));
                }
                profile.Add(new Vector2(radius * 0.96f, height * 0.93f));
                profile.Add(new Vector2(neck * 1.4f, height * 0.99f));
                profile.Add(new Vector2(neck, height));
                profile.Add(new Vector2(neck, height + 0.03f));
                return Lathe(profile, 28);
            });

        /// <summary>Seau creux avec rebord.</summary>
        static Mesh Bucket(MeshStore store) => store.Add("Bucket", () => Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(0.11f, 0f), new Vector2(0.14f, 0.27f), new Vector2(0.15f, 0.275f), new Vector2(0.15f, 0.29f),
            new Vector2(0.135f, 0.29f), new Vector2(0.125f, 0.27f), new Vector2(0.1f, 0.02f), new Vector2(0f, 0.02f),
        }, 24, capBottom: false, capTop: false));

        static Mesh SprayBottle(MeshStore store) => store.Add("ShelfSpray", () => Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(0.04f, 0f), new Vector2(0.045f, 0.02f), new Vector2(0.045f, 0.16f), new Vector2(0.03f, 0.2f),
            new Vector2(0.015f, 0.22f), new Vector2(0.015f, 0.26f), new Vector2(0f, 0.26f),
        }, 16));

        static Mesh Jug(MeshStore store) => store.Add("ShelfJug", () => Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(0.07f, 0f), new Vector2(0.075f, 0.03f), new Vector2(0.075f, 0.2f), new Vector2(0.05f, 0.25f),
            new Vector2(0.022f, 0.27f), new Vector2(0.022f, 0.3f), new Vector2(0f, 0.3f),
        }, 18));

        static Mesh RolledRug(MeshStore store, float radius, float length) =>
            store.Add($"RolledRug_{radius:0.##}x{length:0.##}", () => Lathe(new[]
            {
                new Vector2(0f, -length * 0.5f), new Vector2(radius * 0.94f, -length * 0.5f), new Vector2(radius, -length * 0.49f),
                new Vector2(radius, length * 0.49f), new Vector2(radius * 0.94f, length * 0.5f), new Vector2(0f, length * 0.5f),
            }, 26, false, false));

        // ------------------------------------------------------------------ Postes

        public static ResourceStation WaterStation(Transform parent, RugWashContentBuilder.Content c, MeshStore m, AudioManager audio)
        {
            var root = Group("WaterStation", parent, new Vector3(-HalfX + 0.55f, 0f, 2.2f));
            Prop("Frame_Top", root, Rounded(m, new Vector3(0.9f, 0.06f, 1.6f), 0.02f), c["DarkMetal"], new Vector3(0f, 0.43f, 0f));
            foreach (var x in new[] { -0.4f, 0.4f })
            foreach (var z in new[] { -0.75f, 0.75f })
                Prop("Leg", root, Rounded(m, new Vector3(0.05f, 0.42f, 0.05f), 0.01f), c["DarkMetal"], new Vector3(x, 0.21f, z));
            Prop("Tank", root, RibbedDrum(m, "WaterTank", 0.34f, 1.1f, 5, 0.1f), c["BluePlastic"], new Vector3(0f, 0.46f, 0.35f));
            Prop("Tank_Cap", root, Cyl(m, 0.13f, 0.05f), c["Rubber"], new Vector3(0f, 1.6f, 0.35f));
            Prop("Sink", root, Rounded(m, new Vector3(0.7f, 0.22f, 0.55f), 0.035f), c["Steel"], new Vector3(0.02f, 0.57f, -0.45f));
            Prop("Sink_Basin", root, Rounded(m, new Vector3(0.6f, 0.02f, 0.45f), 0.02f), c["DarkMetal"], new Vector3(0.02f, 0.675f, -0.45f));
            Prop("Pipe", root, m.Add("WaterPipe", () => Tube(Smooth(new[]
            {
                new Vector3(-0.3f, 0.5f, 0.25f), new Vector3(-0.33f, 0.9f, 0.1f), new Vector3(-0.33f, 1.25f, -0.25f),
                new Vector3(-0.2f, 1.27f, -0.45f), new Vector3(-0.06f, 1.24f, -0.45f), new Vector3(-0.03f, 1.14f, -0.45f),
            }, 8), 0.02f, 12)), c["Chrome"], Vector3.zero);
            Prop("Tap", root, Cyl(m, 0.035f, 0.025f), c["RedPlastic"], new Vector3(-0.27f, 1.3f, -0.42f));
            Prop("Wall_Pipe", root, m.Add("WaterWallPipe", () => Tube(new[]
            {
                new Vector3(-0.42f, 0.5f, 0.25f), new Vector3(-0.42f, Height - 0.3f, 0.25f), new Vector3(-0.42f, Height - 0.3f, 1.8f),
            }, 0.025f, 10)), c["Steel"], Vector3.zero);
            Prop("Hose_Coil", root, m.Add("HoseCoil", () => Tube(Helix(0.22f, 0.035f, 4f), 0.016f, 8)), c["GreyHose"],
                new Vector3(-0.38f, 1.45f, 1.15f), new Vector3(0f, 0f, -90f));
            Prop("Hose_Hook", root, Cyl(m, 0.02f, 0.2f), c["Steel"], new Vector3(-0.35f, 1.62f, 1.15f), new Vector3(0f, 0f, 90f));

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.85f, 0f);
            collider.size = new Vector3(0.9f, 1.7f, 1.6f);

            var pour = CreateParticles("PourEffect", root, c["WaterJet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.35f, 0.5f), Speed = new Vector2(0.4f, 0.8f), Size = new Vector2(0.015f, 0.03f),
                Color = Color.white, Gravity = 1f, MaxParticles = 200, Shape = ParticleSystemShapeType.Cone, Angle = 6f,
                Radius = 0.01f, Rate = 160f, Loop = false, PlayOnAwake = false, Stretch = 0.03f,
            });
            pour.transform.localPosition = new Vector3(-0.03f, 1.12f, -0.45f);
            pour.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var station = root.gameObject.AddComponent<ResourceStation>();
            Wire(station, ("effect", pour), ("audioManager", audio), ("sound", Clip(RugWashAudioGenerator.Refill)));
            SetInt(station, "resource", (int)ToolResource.Water);
            RugWashSceneBuilder.CreateSign(parent, "Sign_Water", "POSTE D'EAU", new Vector3(-HalfX + 0.04f, 2.2f, 1.6f), Quaternion.Euler(0f, -90f, 0f), m, c);
            return station;
        }

        public static ResourceStation SoapStation(Transform parent, RugWashContentBuilder.Content c, MeshStore m, AudioManager audio)
        {
            var root = Group("SoapStation", parent, new Vector3(-HalfX + 0.45f, 0f, -0.9f));
            Pallet(root, c, m, new Vector3(0f, 0f, 0f), new Vector2(0.7f, 0.7f));
            Prop("Drum", root, RibbedDrum(m, "SoapDrum", 0.27f, 0.85f, 4, 0.06f), c["GreenPlastic"], new Vector3(0f, 0.12f, 0f));
            Prop("Pump", root, Cyl(m, 0.025f, 0.22f), c["WhitePlastic"], new Vector3(0f, 1.12f, 0f));
            Prop("Spout", root, m.Add("SoapSpout", () => Tube(Smooth(new[]
            {
                new Vector3(0f, 1.22f, 0f), new Vector3(0.08f, 1.25f, 0f), new Vector3(0.2f, 1.22f, 0f), new Vector3(0.22f, 1.15f, 0f),
            }, 6), 0.012f, 8)), c["WhitePlastic"], Vector3.zero);
            Prop("Jerrycan_A", root, Rounded(m, new Vector3(0.2f, 0.3f, 0.13f), 0.03f), c["RedPlastic"], new Vector3(0.05f, 0.15f, 0.55f), new Vector3(0f, 12f, 0f), true);
            Prop("Jerrycan_B", root, Rounded(m, new Vector3(0.2f, 0.3f, 0.13f), 0.03f), c["WhitePlastic"], new Vector3(0.1f, 0.15f, 0.78f), new Vector3(0f, -8f, 0f), true);

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.6f, 0f);
            collider.size = new Vector3(0.7f, 1.2f, 0.7f);
            var foamDrip = CreateParticles("FoamDrip", root, c["Foam"], new ParticleConfig
            {
                Lifetime = new Vector2(0.4f, 0.7f), Speed = new Vector2(0.1f, 0.3f), Size = new Vector2(0.02f, 0.05f), Color = Color.white,
                Gravity = 0.6f, MaxParticles = 60, Shape = ParticleSystemShapeType.Cone, Angle = 10f, Radius = 0.01f, Rate = 50f, Loop = false,
            });
            foamDrip.transform.localPosition = new Vector3(0.22f, 1.13f, 0f);
            foamDrip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var station = root.gameObject.AddComponent<ResourceStation>();
            Wire(station, ("effect", foamDrip), ("audioManager", audio), ("sound", Clip(RugWashAudioGenerator.Refill)));
            SetInt(station, "resource", (int)ToolResource.Soap);
            RugWashSceneBuilder.CreateSign(parent, "Sign_Soap", "PRODUITS", new Vector3(-HalfX + 0.04f, 2.2f, -0.9f), Quaternion.Euler(0f, -90f, 0f), m, c);
            return station;
        }

        static AudioClip Clip(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        // ------------------------------------------------------------------ Mobilier

        static void Pallet(Transform parent, RugWashContentBuilder.Content c, MeshStore m, Vector3 position, Vector2 size)
        {
            var pallet = Group("Pallet", parent, position);
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-size.y * 0.5f + 0.05f, size.y * 0.5f - 0.05f, i / 4f);
                Prop("Plank", pallet, Rounded(m, new Vector3(size.x, 0.022f, 0.09f), 0.006f), c["Wood"], new Vector3(0f, 0.11f, z));
            }
            foreach (var x in new[] { -size.x * 0.42f, 0f, size.x * 0.42f })
                Prop("Block", pallet, Rounded(m, new Vector3(0.09f, 0.1f, size.y), 0.008f), c["Wood"], new Vector3(x, 0.05f, 0f));
        }

        public static GameObject ToolStorage(Transform parent, RugWashContentBuilder.Content c, MeshStore m)
        {
            var root = Group("ToolStorage", parent, new Vector3(3.6f, 0f, HalfZ - 0.3f));
            Prop("Cabinet", root, Rounded(m, new Vector3(2.2f, 0.9f, 0.55f), 0.025f), c["DarkMetal"], new Vector3(0f, 0.45f, 0f), collider: true);
            for (int col = 0; col < 3; col++)
            for (int row = 0; row < 2; row++)
            {
                float x = (col - 1) * 0.72f, y = 0.24f + row * 0.42f;
                Prop("Drawer", root, Rounded(m, new Vector3(0.68f, 0.38f, 0.02f), 0.01f), c["DoorPaint"], new Vector3(x, y, -0.285f));
                Prop("Drawer_Handle", root, Rounded(m, new Vector3(0.22f, 0.025f, 0.025f), 0.01f), c["Chrome"], new Vector3(x, y + 0.12f, -0.305f));
            }
            Prop("Cabinet_Top", root, Rounded(m, new Vector3(2.26f, 0.04f, 0.6f), 0.012f), c["Wood"], new Vector3(0f, 0.92f, 0f));
            Prop("Pegboard", root, Rounded(m, new Vector3(2.2f, 1.1f, 0.03f), 0.01f), c["Pegboard"], new Vector3(0f, 1.65f, 0.24f));

            var spareBrush = m.Add("SpareBrush", () => Combine("SpareBrush",
                (Lathe(new[] { new Vector2(0.014f, 0f), new Vector2(0.014f, 0.32f), new Vector2(0f, 0.33f) }, 12), Matrix4x4.Translate(new Vector3(0f, 0.04f, 0f))),
                (RoundedBox(new Vector3(0.2f, 0.05f, 0.07f), 0.015f), Matrix4x4.identity)));
            for (int i = 0; i < 3; i++)
                Prop("Spare_Brush", root, spareBrush, i == 1 ? c["BluePlastic"] : c["GreenPlastic"], new Vector3(-0.75f + i * 0.45f, 1.32f, 0.2f));
            Prop("Squeegee", root, m.Add("Squeegee", () => Combine("Squeegee",
                (Lathe(new[] { new Vector2(0.012f, 0f), new Vector2(0.012f, 0.5f), new Vector2(0f, 0.51f) }, 12), Matrix4x4.Translate(new Vector3(0f, 0.02f, 0f))),
                (RoundedBox(new Vector3(0.35f, 0.04f, 0.03f), 0.01f), Matrix4x4.identity))), c["SafetyYellow"], new Vector3(0.75f, 1.25f, 0.2f));
            foreach (var x in new[] { -0.9f, -0.3f, 0.3f, 0.9f })
                Prop("Hook", root, Cyl(m, 0.008f, 0.06f), c["Chrome"], new Vector3(x, 1.95f, 0.2f), new Vector3(90f, 0f, 0f));
            Prop("Bucket_A", root, Bucket(m), c["RedPlastic"], new Vector3(-0.7f, 0.94f, -0.05f));
            Prop("Bucket_B", root, Bucket(m), c["BluePlastic"], new Vector3(0.75f, 0.94f, -0.05f), new Vector3(0f, 40f, 0f));
            RugWashSceneBuilder.CreateSign(parent, "Sign_Tools", "MATÉRIEL & BOUTIQUE", new Vector3(3.6f, 2.45f, HalfZ - 0.04f), Quaternion.identity, m, c);
            return root.gameObject;
        }

        public static GameObject OrderDesk(Transform parent, RugWashContentBuilder.Content c, MeshStore m)
        {
            var root = Group("OrderDesk", parent, new Vector3(4.3f, 0f, -3.7f));
            Prop("Counter", root, Rounded(m, new Vector3(2.4f, 1f, 0.7f), 0.03f), c["DoorPaint"], new Vector3(0f, 0.5f, 0f), collider: true);
            Prop("Counter_Kick", root, Rounded(m, new Vector3(2.36f, 0.1f, 0.66f), 0.01f), c["Rubber"], new Vector3(0f, 0.05f, 0.03f));
            Prop("Counter_Top", root, Rounded(m, new Vector3(2.5f, 0.045f, 0.82f), 0.015f), c["Wood"], new Vector3(0f, 1.02f, 0f));
            Prop("Monitor_Base", root, Rounded(m, new Vector3(0.22f, 0.015f, 0.16f), 0.006f), c["Rubber"], new Vector3(0.6f, 1.05f, 0.1f));
            Prop("Monitor_Stand", root, Cyl(m, 0.015f, 0.16f), c["Rubber"], new Vector3(0.6f, 1.13f, 0.1f));
            Prop("Monitor", root, Rounded(m, new Vector3(0.56f, 0.36f, 0.035f), 0.012f), c["Rubber"], new Vector3(0.6f, 1.33f, 0.12f), new Vector3(-6f, 0f, 0f));
            Prop("Monitor_Screen", root, Rounded(m, new Vector3(0.51f, 0.31f, 0.004f), 0.004f), c["ScreenGlow"], new Vector3(0.6f, 1.33f, 0.14f), new Vector3(-6f, 0f, 0f));
            Prop("Keyboard", root, Rounded(m, new Vector3(0.42f, 0.02f, 0.14f), 0.008f), c["Rubber"], new Vector3(0.55f, 1.054f, 0.28f), new Vector3(0f, 4f, 0f));
            for (int i = 0; i < 4; i++)
                Prop("Paper", root, Rounded(m, new Vector3(0.21f, 0.004f, 0.297f), 0.001f), c["Paper"],
                    new Vector3(-0.5f + i * 0.004f, 1.045f + i * 0.004f, 0.15f), new Vector3(0f, -8f + i * 5f, 0f));
            Prop("Bell", root, m.Add("DeskBell", () => Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.05f, 0.01f), new Vector2(0.045f, 0.03f), new Vector2(0.03f, 0.055f),
                new Vector2(0.008f, 0.065f), new Vector2(0.008f, 0.08f), new Vector2(0f, 0.08f),
            }, 20)), c["Chrome"], new Vector3(-0.05f, 1.043f, 0.25f));

            Pallet(parent, c, m, new Vector3(2.4f, 0f, -3.1f), new Vector2(1.7f, 0.8f));
            var rolled = Group("RolledRugs", parent, new Vector3(2.4f, 0.12f, -3.1f));
            Prop("Rolled_A", rolled, RolledRug(m, 0.14f, 1.6f), c["RugClassic"], new Vector3(0f, 0.14f, -0.18f), new Vector3(0f, 0f, 90f), true);
            Prop("Rolled_B", rolled, RolledRug(m, 0.16f, 1.5f), c["RugOriental"], new Vector3(0.03f, 0.16f, 0.17f), new Vector3(0f, 6f, 90f), true);
            Prop("Rolled_C", rolled, RolledRug(m, 0.12f, 1.4f), c["RugShag"], new Vector3(-0.02f, 0.42f, 0f), new Vector3(0f, -4f, 90f), true);
            Prop("Bin", parent, m.Add("Bin", () => Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.17f, 0f), new Vector2(0.2f, 0.55f), new Vector2(0.21f, 0.56f), new Vector2(0.19f, 0.56f),
                new Vector2(0.18f, 0.54f), new Vector2(0.15f, 0.02f), new Vector2(0f, 0.02f),
            }, 24, false, false)), c["DarkMetal"], new Vector3(5.6f, 0f, -2.9f), collider: true);
            RugWashSceneBuilder.CreateSign(parent, "Sign_Orders", "RÉCEPTION DES COMMANDES", new Vector3(4.3f, 2.45f, -HalfZ + 0.04f), Quaternion.Euler(0f, 180f, 0f), m, c);
            return root.gameObject;
        }

        public static void DryingArea(Transform parent, RugWashContentBuilder.Content c, MeshStore m)
        {
            var root = Group("DryingArea", parent, new Vector3(HalfX - 0.7f, 0f, 0.8f));
            foreach (var z in new[] { -1.35f, 1.35f })
            {
                var post = Prop("Post", root, Cyl(m, 0.03f, 2.2f), c["Steel"], new Vector3(0f, 1.1f, z), collider: true);
                post.GetComponent<BoxCollider>().size = new Vector3(0.08f, 2.2f, 0.08f);
                Prop("Foot", root, Rounded(m, new Vector3(0.6f, 0.04f, 0.08f), 0.015f), c["DarkMetal"], new Vector3(0f, 0.02f, z));
            }
            Prop("Bar_High", root, Cyl(m, 0.022f, 2.76f), c["Steel"], new Vector3(0f, 2.15f, 0f), new Vector3(90f, 0f, 0f));
            Prop("Bar_Low", root, Cyl(m, 0.02f, 2.76f), c["Steel"], new Vector3(0.25f, 1.6f, 0f), new Vector3(90f, 0f, 0f));
            Prop("Drip_Tray", root, Rounded(m, new Vector3(0.8f, 0.06f, 2.8f), 0.02f), c["DarkMetal"], new Vector3(0f, 0.03f, 0f), collider: true);

            var clothA = m.Add("HangingRug_A", () => HangingCloth(1.25f, 1.75f, 0.035f, 3, seed: 3));
            var clothB = m.Add("HangingRug_B", () => HangingCloth(1.2f, 1.5f, 0.05f, 2, seed: 9));
            var rugA = Prop("Hanging_Classic", root, clothA, c["RugClassic"], new Vector3(-0.02f, 2.13f, -0.66f), new Vector3(0f, 90f, 0f));
            var rugB = Prop("Hanging_Shag", root, clothB, c["RugShag"], new Vector3(-0.02f, 2.13f, 0.66f), new Vector3(0f, 90f, 0f));
            rugA.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            rugB.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            RugWashSceneBuilder.CreateSign(parent, "Sign_Drying", "SÉCHAGE", new Vector3(HalfX - 0.04f, 2.6f, 0.8f), Quaternion.Euler(0f, 90f, 0f), m, c);
        }

        public static void Shelves(Transform parent, RugWashContentBuilder.Content c, MeshStore m)
        {
            var root = Group("Shelves", parent, new Vector3(-HalfX + 0.35f, 0f, -2.6f));
            foreach (var x in new[] { -0.22f, 0.22f })
            foreach (var z in new[] { -0.9f, 0.9f })
                Prop("Upright", root, Rounded(m, new Vector3(0.04f, 2.05f, 0.04f), 0.008f), c["DarkMetal"], new Vector3(x, 1.025f, z));
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 1f, 0f);
            collider.size = new Vector3(0.5f, 2f, 1.85f);

            Material[] plastics = { c["BluePlastic"], c["GreenPlastic"], c["RedPlastic"], c["WhitePlastic"], c["SafetyYellow"] };
            var random = new System.Random(17);
            for (int level = 0; level < 4; level++)
            {
                float y = 0.15f + level * 0.55f;
                Prop("Board", root, Rounded(m, new Vector3(0.5f, 0.03f, 1.86f), 0.01f), c["Wood"], new Vector3(0f, y, 0f));
                float z = -0.8f;
                while (z < 0.8f)
                {
                    int kind = random.Next(4);
                    var material = plastics[random.Next(plastics.Length)];
                    float yaw = (float)random.NextDouble() * 360f;
                    switch (kind)
                    {
                        case 0:
                            Prop("Spray", root, SprayBottle(m), material, new Vector3(0.02f, y + 0.015f, z), new Vector3(0f, yaw, 0f));
                            z += 0.13f;
                            break;
                        case 1:
                            Prop("Jug", root, Jug(m), material, new Vector3(0f, y + 0.015f, z + 0.03f), new Vector3(0f, yaw, 0f));
                            z += 0.2f;
                            break;
                        case 2:
                            Prop("Box", root, Rounded(m, new Vector3(0.36f, 0.24f, 0.3f), 0.012f), c["Cardboard"], new Vector3(0f, y + 0.135f, z + 0.12f),
                                new Vector3(0f, (float)random.NextDouble() * 6f - 3f, 0f));
                            z += 0.34f;
                            break;
                        default:
                            z += 0.12f;
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ Structure et détails

        public static void Ceiling(Transform parent, RugWashContentBuilder.Content c, MeshStore m)
        {
            var root = Group("CeilingStructure", parent, Vector3.zero);
            foreach (var z in new[] { -3.8f, -0.9f, 1.9f, 4.4f })
                Prop("Beam", root, Rounded(m, new Vector3(HalfX * 2f, 0.24f, 0.16f), 0.02f), c["DarkMetal"], new Vector3(0f, Height - 0.12f, z));
            Prop("Conduit_Back", root, Cyl(m, 0.028f, HalfX * 2f), c["Steel"], new Vector3(0f, Height - 0.35f, HalfZ - 0.08f), new Vector3(0f, 0f, 90f));
            Prop("Conduit_Right", root, Cyl(m, 0.028f, HalfZ * 2f), c["Steel"], new Vector3(HalfX - 0.08f, Height - 0.35f, 0f), new Vector3(90f, 0f, 0f));
            for (int i = -2; i <= 2; i++)
                Prop("Conduit_Clamp", root, Rounded(m, new Vector3(0.05f, 0.08f, 0.05f), 0.01f), c["DarkMetal"], new Vector3(i * 2.4f, Height - 0.35f, HalfZ - 0.05f));
        }

        /// <summary>Luminaire suspendu (boîtier + deux tubes émissifs + suspentes).</summary>
        public static void LampFixture(Transform fixture, RugWashContentBuilder.Content c, MeshStore m)
        {
            Prop("Housing", fixture, Rounded(m, new Vector3(1.4f, 0.07f, 0.36f), 0.02f), c["DarkMetal"], Vector3.zero);
            foreach (var z in new[] { -0.07f, 0.07f })
                Prop("Tube", fixture, Cyl(m, 0.022f, 1.25f), c["LampPanel"], new Vector3(0f, -0.055f, z), new Vector3(0f, 0f, 90f));
            foreach (var x in new[] { -0.6f, 0.6f })
                Prop("Cable", fixture, Cyl(m, 0.004f, 0.3f), c["DarkMetal"], new Vector3(x, 0.18f, 0f));
        }

        public static void WallDetails(Transform parent, RugWashContentBuilder.Content c, MeshStore m)
        {
            // Affiche pédagogique : la séquence de nettoyage, lisible depuis l'aire de lavage.
            var poster = Group("Poster_Steps", parent, new Vector3(-1.75f, 1.9f, HalfZ - 0.02f));
            Prop("Paper", poster, Rounded(m, new Vector3(0.95f, 1.25f, 0.008f), 0.004f), c["Paper"], new Vector3(0f, 0f, -0.006f));
            Text3D(poster, "LES 5 ÉTAPES", new Vector3(0f, 0.5f, -0.012f), 0.016f, new Color(0.12f, 0.14f, 0.18f));
            string[] steps = { "1  ASPIRER", "2  SAVONNER", "3  BROSSER", "4  RINCER", "5  EXTRAIRE" };
            string[] colors = { "StepBlue", "StepGreen", "StepOrange", "StepCyan", "StepPurple" };
            for (int i = 0; i < steps.Length; i++)
            {
                float y = 0.3f - i * 0.19f;
                Prop("Step", poster, Rounded(m, new Vector3(0.82f, 0.15f, 0.006f), 0.01f), c[colors[i]], new Vector3(0f, y, -0.011f));
                Text3D(poster, steps[i], new Vector3(0f, y, -0.016f), 0.013f, Color.white);
            }

            // Horloge murale animée (heure réelle).
            var clock = Group("WallClock", parent, new Vector3(1.75f, 2.55f, HalfZ - 0.03f));
            Prop("Body", clock, Cyl(m, 0.21f, 0.05f), c["ClockFace"], Vector3.zero, new Vector3(90f, 0f, 0f));
            var hour = Group("Hour_Pivot", clock, new Vector3(0f, 0f, -0.03f));
            Prop("Hour", hour, Rounded(m, new Vector3(0.018f, 0.1f, 0.006f), 0.004f), c["Rubber"], new Vector3(0f, 0.045f, 0f));
            var minute = Group("Minute_Pivot", clock, new Vector3(0f, 0f, -0.035f));
            Prop("Minute", minute, Rounded(m, new Vector3(0.012f, 0.16f, 0.006f), 0.004f), c["Rubber"], new Vector3(0f, 0.07f, 0f));
            var second = Group("Second_Pivot", clock, new Vector3(0f, 0f, -0.04f));
            Prop("Second", second, Rounded(m, new Vector3(0.004f, 0.17f, 0.004f), 0.0015f), c["RedPlastic"], new Vector3(0f, 0.065f, 0f));
            // Les aiguilles bougent : elles ne doivent pas être statiques (batching).
            foreach (var hand in new[] { hour, minute, second })
                foreach (Transform child in hand) UnityEditor.GameObjectUtility.SetStaticEditorFlags(child.gameObject, default);
            var wallClock = clock.gameObject.AddComponent<WallClock>();
            Wire(wallClock, ("hourHand", hour), ("minuteHand", minute), ("secondHand", second));

            // Marquage au sol : allée entre la porte et l'aire de lavage.
            foreach (var x in new[] { -3.85f, -2.55f })
                Prop("Floor_Line", parent, Rounded(m, new Vector3(0.08f, 0.004f, 3.4f), 0.002f), c["FloorMarking"], new Vector3(x, 0.002f, -3.2f));

            // Palette près de l'entrée avec des tapis roulés en attente.
            Pallet(parent, c, m, new Vector3(-4.7f, 0f, -4.1f), new Vector2(1.2f, 0.8f));
            var waiting = Group("WaitingRugs", parent, new Vector3(-4.7f, 0.12f, -4.1f));
            Prop("Rolled_D", waiting, RolledRug(m, 0.15f, 1.2f), c["RugOriental"], new Vector3(0f, 0.15f, -0.18f), new Vector3(0f, 0f, 90f), true);
            Prop("Rolled_E", waiting, RolledRug(m, 0.13f, 1.1f), c["RugShag"], new Vector3(0.04f, 0.13f, 0.16f), new Vector3(0f, 8f, 90f), true);
        }

        static void Text3D(Transform parent, string text, Vector3 position, float size, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = UIFont;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = UIFont.material;
        }
    }
}
