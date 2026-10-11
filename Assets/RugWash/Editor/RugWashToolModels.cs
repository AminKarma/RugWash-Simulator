using System.Collections.Generic;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>
    /// Modèles tenus en main des huit outils, construits à partir de maillages procéduraux
    /// (repère caméra : X droite, Y haut, Z avant). Retourne la racine et les points utiles (buse, sortie du jet).
    /// </summary>
    public static class RugWashToolModels
    {
        public sealed class Model
        {
            public Transform Root;
            /// <summary>Point d'émission (jet, brume) ou tête d'aspiration.</summary>
            public Transform Tip;
        }

        static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);

        static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Transform Root(Transform camera, string name, Vector3 position, Vector3 euler)
        {
            var root = new GameObject($"ViewModel_{name}").transform;
            root.SetParent(camera, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(euler);
            return root;
        }

        static Transform Tip(Transform parent, Vector3 position)
        {
            var tip = new GameObject("Tip").transform;
            tip.SetParent(parent, false);
            tip.localPosition = position;
            return tip;
        }

        /// <summary>Tube droit le long de Z (de z0 à z1), via une révolution.</summary>
        static Mesh StraightTube(MeshStore store, string key, float radius, float length) =>
            store.Add(key, () => Lathe(new[] { new Vector2(radius, 0f), new Vector2(radius, length) }, 16));

        // ------------------------------------------------------------------ Brosse

        public static Model Brush(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Brush", new Vector3(0.27f, -0.3f, 0.48f), new Vector3(10f, -14f, 0f));
            var handle = store.Add("Tool_BrushHandle", () => Lathe(new[]
            {
                new Vector2(0.0f, 0f), new Vector2(0.017f, 0.003f), new Vector2(0.02f, 0.03f), new Vector2(0.017f, 0.07f),
                new Vector2(0.015f, 0.3f), new Vector2(0.016f, 0.34f), new Vector2(0.012f, 0.36f), new Vector2(0f, 0.362f),
            }, 18, capBottom: false, capTop: false));
            Part("Handle", root, handle, c["Wood"], new Vector3(0f, 0.03f, -0.02f), Quaternion.Euler(96f, 0f, 0f));
            Part("Ferrule", root, store.Add("Tool_BrushFerrule", () => Cylinder(0.019f, 0.03f, 18)), c["Chrome"],
                new Vector3(0f, -0.005f, 0.33f), Quaternion.Euler(96f, 0f, 0f));
            Part("Body", root, store.Add("Tool_BrushBody", () => RoundedBox(new Vector3(0.23f, 0.045f, 0.09f), 0.018f)), c["GreenPlastic"],
                new Vector3(0f, -0.03f, 0.4f), Quaternion.identity);
            Part("Grip_Strip", root, store.Add("Tool_BrushStrip", () => RoundedBox(new Vector3(0.2f, 0.012f, 0.03f), 0.005f)), c["Rubber"],
                new Vector3(0f, -0.005f, 0.4f), Quaternion.identity);
            Part("Bristles", root, store.Add("Tool_BrushBristles", () => BristleBlock(new Vector2(0.21f, 0.075f), 0.045f, 16, 6, 0.009f)), c["Bristle"],
                new Vector3(0f, -0.05f, 0.4f), Quaternion.identity);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, -0.09f, 0.4f)) };
        }

        // ------------------------------------------------------------------ Lance de rinçage

        public static Model Rinse(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Rinse", new Vector3(0.25f, -0.25f, 0.4f), new Vector3(4f, -7f, 0f));
            Part("Grip", root, store.Add("Tool_GunGrip", () => RoundedBox(new Vector3(0.036f, 0.12f, 0.052f), 0.014f)), c["Rubber"],
                new Vector3(0f, -0.075f, -0.03f), Quaternion.Euler(-18f, 0f, 0f));
            Part("Body", root, store.Add("Tool_GunBody", () => RoundedBox(new Vector3(0.056f, 0.068f, 0.17f), 0.02f)), c["SafetyYellow"],
                new Vector3(0f, 0f, 0.04f), Quaternion.identity);
            Part("Trigger", root, store.Add("Tool_GunTrigger", () => RoundedBox(new Vector3(0.012f, 0.048f, 0.022f), 0.005f)), c["DarkMetal"],
                new Vector3(0f, -0.05f, 0.04f), Quaternion.Euler(-12f, 0f, 0f));
            Part("Guard", root, store.Add("Tool_GunGuard", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, -0.032f, -0.005f), new Vector3(0f, -0.085f, 0.01f), new Vector3(0f, -0.088f, 0.06f), new Vector3(0f, -0.034f, 0.09f),
                }, 6), 0.005f, 8)), c["SafetyYellow"], Vector3.zero, Quaternion.identity);
            Part("Lance", root, StraightTube(store, "Tool_Lance", 0.011f, 0.37f), c["Steel"], new Vector3(0f, 0.01f, 0.12f), AlongZ);
            Part("Lance_Collar", root, store.Add("Tool_LanceCollar", () => Cylinder(0.018f, 0.03f, 16)), c["DarkMetal"],
                new Vector3(0f, 0.01f, 0.13f), AlongZ);
            Part("Nozzle", root, store.Add("Tool_Nozzle", () => Lathe(new[]
                {
                    new Vector2(0.012f, 0f), new Vector2(0.016f, 0.01f), new Vector2(0.02f, 0.035f), new Vector2(0.012f, 0.045f), new Vector2(0.006f, 0.046f),
                }, 16)), c["DarkMetal"], new Vector3(0f, 0.01f, 0.48f), AlongZ);
            Part("Hose", root, store.Add("Tool_GunHose", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, -0.13f, -0.06f), new Vector3(0.01f, -0.25f, -0.12f), new Vector3(0.05f, -0.45f, -0.1f), new Vector3(0.1f, -0.8f, 0.05f),
                }, 10), 0.013f, 10)), c["Rubber"], Vector3.zero, Quaternion.identity);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.01f, 0.53f)) };
        }

        // ------------------------------------------------------------------ Pulvérisateur de détergent

        public static Model Soap(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Soap", new Vector3(0.24f, -0.2f, 0.38f), new Vector3(6f, -10f, 0f));
            Part("Bottle", root, store.Add("Tool_SoapBottle", () => Lathe(new[]
                {
                    new Vector2(0f, -0.2f), new Vector2(0.038f, -0.2f), new Vector2(0.044f, -0.19f), new Vector2(0.044f, -0.06f),
                    new Vector2(0.036f, -0.03f), new Vector2(0.016f, -0.012f), new Vector2(0.016f, 0f),
                }, 24, capTop: false)), c["GreenPlastic"], new Vector3(0f, -0.04f, 0.02f), Quaternion.identity);
            Part("Label", root, store.Add("Tool_SoapLabel", () => Lathe(new[] { new Vector2(0.0445f, -0.16f), new Vector2(0.0445f, -0.09f) }, 24, false, false)),
                c["WhitePlastic"], new Vector3(0f, -0.04f, 0.02f), Quaternion.identity);
            Part("Cap", root, store.Add("Tool_SoapCap", () => Cylinder(0.02f, 0.02f, 18)), c["WhitePlastic"], new Vector3(0f, -0.03f, 0.02f), Quaternion.identity);
            Part("Head", root, store.Add("Tool_SoapHead", () => RoundedBox(new Vector3(0.034f, 0.046f, 0.11f), 0.012f)), c["WhitePlastic"],
                new Vector3(0f, 0.005f, 0.05f), Quaternion.Euler(-4f, 0f, 0f));
            Part("Trigger", root, store.Add("Tool_SoapTrigger", () => RoundedBox(new Vector3(0.014f, 0.055f, 0.02f), 0.006f)), c["BluePlastic"],
                new Vector3(0f, -0.03f, 0.085f), Quaternion.Euler(-22f, 0f, 0f));
            Part("Nozzle", root, StraightTube(store, "Tool_SoapNozzle", 0.008f, 0.02f), c["BluePlastic"], new Vector3(0f, 0.012f, 0.105f), AlongZ);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.012f, 0.13f)) };
        }

        // ------------------------------------------------------------------ Aspirateur

        public static Model Vacuum(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Vacuum", new Vector3(0.26f, -0.29f, 0.44f), new Vector3(8f, -12f, 0f));
            Part("Grip", root, store.Add("Tool_VacGrip", () => RoundedBox(new Vector3(0.04f, 0.12f, 0.058f), 0.016f)), c["RedPlastic"],
                new Vector3(0f, -0.06f, -0.05f), Quaternion.Euler(-14f, 0f, 0f));
            Part("Body", root, store.Add("Tool_VacBody", () => RoundedBox(new Vector3(0.075f, 0.085f, 0.15f), 0.028f)), c["DarkMetal"],
                new Vector3(0f, 0.005f, 0.01f), Quaternion.identity);
            Part("Body_Stripe", root, store.Add("Tool_VacStripe", () => RoundedBox(new Vector3(0.077f, 0.02f, 0.12f), 0.008f)), c["RedPlastic"],
                new Vector3(0f, 0.03f, 0.01f), Quaternion.identity);
            Part("Wand", root, store.Add("Tool_VacWand", () => Tube(new[] { new Vector3(0f, 0f, 0.08f), new Vector3(0f, -0.065f, 0.43f) }, 0.016f, 16)),
                c["Chrome"], Vector3.zero, Quaternion.identity);
            var head = Part("Head", root, store.Add("Tool_VacHead", () => RoundedBox(new Vector3(0.25f, 0.038f, 0.075f), 0.016f)), c["DarkMetal"],
                new Vector3(0f, -0.085f, 0.46f), Quaternion.Euler(8f, 0f, 0f));
            Part("Head_Lip", root, store.Add("Tool_VacLip", () => RoundedBox(new Vector3(0.252f, 0.012f, 0.078f), 0.005f)), c["RedPlastic"],
                new Vector3(0f, -0.105f, 0.463f), Quaternion.Euler(8f, 0f, 0f));
            Part("Head_Bristles", root, store.Add("Tool_VacBristles", () => BristleBlock(new Vector2(0.22f, 0.012f), 0.012f, 22, 1, 0.006f)), c["Rubber"],
                new Vector3(0f, -0.11f, 0.47f), Quaternion.Euler(8f, 0f, 0f));
            Part("Hose", root, store.Add("Tool_VacHose", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, 0.02f, -0.07f), new Vector3(0.02f, 0.01f, -0.16f), new Vector3(0.08f, -0.25f, -0.2f), new Vector3(0.15f, -0.8f, -0.05f),
                }, 10), 0.02f, 12)), c["Rubber"], Vector3.zero, Quaternion.identity);
            return new Model { Root = root, Tip = head.transform };
        }

        // ------------------------------------------------------------------ Extracteur

        public static Model Extractor(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Extractor", new Vector3(0.26f, -0.3f, 0.44f), new Vector3(9f, -12f, 0f));
            Part("Grip", root, store.Add("Tool_ExtGrip", () => RoundedBox(new Vector3(0.042f, 0.12f, 0.06f), 0.016f)), c["Rubber"],
                new Vector3(0f, -0.06f, -0.05f), Quaternion.Euler(-14f, 0f, 0f));
            Part("Valve", root, store.Add("Tool_ExtValve", () => RoundedBox(new Vector3(0.06f, 0.06f, 0.1f), 0.02f)), c["BluePlastic"],
                new Vector3(0f, 0.0f, 0.02f), Quaternion.identity);
            Part("Wand", root, store.Add("Tool_ExtWand", () => Tube(new[] { new Vector3(0f, -0.005f, 0.06f), new Vector3(0f, -0.075f, 0.43f) }, 0.018f, 16)),
                c["Steel"], Vector3.zero, Quaternion.identity);
            var head = Part("Head", root, store.Add("Tool_ExtHead", () => RoundedBox(new Vector3(0.28f, 0.05f, 0.06f), 0.02f)), c["BluePlastic"],
                new Vector3(0f, -0.1f, 0.46f), Quaternion.Euler(12f, 0f, 0f));
            Part("Head_Window", root, store.Add("Tool_ExtWindow", () => RoundedBox(new Vector3(0.24f, 0.03f, 0.062f), 0.01f)), c["Glass"],
                new Vector3(0f, -0.093f, 0.463f), Quaternion.Euler(12f, 0f, 0f));
            Part("Head_Slot", root, store.Add("Tool_ExtSlot", () => RoundedBox(new Vector3(0.26f, 0.01f, 0.018f), 0.004f)), c["Rubber"],
                new Vector3(0f, -0.128f, 0.47f), Quaternion.Euler(12f, 0f, 0f));
            Part("Hose", root, store.Add("Tool_ExtHose", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, 0.01f, -0.04f), new Vector3(0.02f, 0.0f, -0.14f), new Vector3(0.09f, -0.28f, -0.2f), new Vector3(0.16f, -0.8f, -0.05f),
                }, 10), 0.022f, 12)), c["GreyHose"], Vector3.zero, Quaternion.identity);
            return new Model { Root = root, Tip = head.transform };
        }

        // ------------------------------------------------------------------ Canon à mousse

        public static Model FoamCannon(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "FoamCannon", new Vector3(0.25f, -0.25f, 0.4f), new Vector3(4f, -8f, 0f));
            Part("Grip", root, store.Add("Tool_GunGrip", () => RoundedBox(new Vector3(0.036f, 0.12f, 0.052f), 0.014f)), c["Rubber"],
                new Vector3(0f, -0.075f, -0.03f), Quaternion.Euler(-18f, 0f, 0f));
            Part("Body", root, store.Add("Tool_CannonBody", () => RoundedBox(new Vector3(0.06f, 0.07f, 0.2f), 0.022f)), c["BluePlastic"],
                new Vector3(0f, 0f, 0.05f), Quaternion.identity);
            Part("Trigger", root, store.Add("Tool_GunTrigger", () => RoundedBox(new Vector3(0.012f, 0.048f, 0.022f), 0.005f)), c["DarkMetal"],
                new Vector3(0f, -0.05f, 0.04f), Quaternion.Euler(-12f, 0f, 0f));
            // Bouteille de mousse translucide sous le corps, avec sa bague.
            Part("Canister", root, store.Add("Tool_CannonCanister", () => Lathe(new[]
                {
                    new Vector2(0f, -0.17f), new Vector2(0.042f, -0.17f), new Vector2(0.048f, -0.155f), new Vector2(0.048f, -0.04f),
                    new Vector2(0.03f, -0.015f), new Vector2(0.018f, 0f),
                }, 24, capTop: false)), c["FoamCanister"], new Vector3(0f, -0.04f, 0.13f), Quaternion.identity);
            Part("Canister_Ring", root, store.Add("Tool_CannonRing", () => Cylinder(0.026f, 0.02f, 18)), c["DarkMetal"],
                new Vector3(0f, -0.045f, 0.13f), Quaternion.identity);
            Part("Lance", root, StraightTube(store, "Tool_CannonLance", 0.012f, 0.2f), c["Steel"], new Vector3(0f, 0.01f, 0.15f), AlongZ);
            // Buse plate en éventail.
            Part("Nozzle", root, store.Add("Tool_CannonNozzle", () => RoundedBox(new Vector3(0.07f, 0.022f, 0.06f), 0.008f)), c["DarkMetal"],
                new Vector3(0f, 0.01f, 0.37f), Quaternion.identity);
            Part("Hose", root, store.Add("Tool_GunHose", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, -0.13f, -0.06f), new Vector3(0.01f, -0.25f, -0.12f), new Vector3(0.05f, -0.45f, -0.1f), new Vector3(0.1f, -0.8f, 0.05f),
                }, 10), 0.013f, 10)), c["Rubber"], Vector3.zero, Quaternion.identity);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.01f, 0.4f)) };
        }

        // ------------------------------------------------------------------ Balai

        public static Model Broom(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Broom", new Vector3(0.2f, -0.3f, 0.42f), new Vector3(10f, -9f, 0f));
            Part("Handle", root, store.Add("Tool_BroomHandle", () => Tube(new[] { new Vector3(0f, 0.12f, -0.25f), new Vector3(0f, -0.17f, 0.56f) }, 0.014f, 12)),
                c["Wood"], Vector3.zero, Quaternion.identity);
            Part("Grip", root, store.Add("Tool_BroomGrip", () => Tube(new[] { new Vector3(0f, 0.1f, -0.2f), new Vector3(0f, 0.06f, -0.08f) }, 0.017f, 12)),
                c["Rubber"], Vector3.zero, Quaternion.identity);
            Part("Head", root, store.Add("Tool_BroomHead", () => RoundedBox(new Vector3(0.44f, 0.04f, 0.065f), 0.012f)), c["SafetyYellow"],
                new Vector3(0f, -0.19f, 0.6f), Quaternion.Euler(18f, 0f, 0f));
            Part("Bristles", root, store.Add("Tool_BroomBristles", () => BristleBlock(new Vector2(0.42f, 0.05f), 0.08f, 28, 3, 0.012f)), c["Bristle"],
                new Vector3(0f, -0.215f, 0.608f), Quaternion.Euler(18f, 0f, 0f));
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, -0.29f, 0.63f)) };
        }

        // ------------------------------------------------------------------ Brosse de détail

        public static Model DetailBrush(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "DetailBrush", new Vector3(0.22f, -0.22f, 0.36f), new Vector3(22f, -12f, 0f));
            Part("Body", root, store.Add("Tool_DetailBody", () => RoundedBox(new Vector3(0.05f, 0.03f, 0.16f), 0.012f)), c["Wood"],
                new Vector3(0f, 0f, 0.02f), Quaternion.identity);
            Part("Grip", root, store.Add("Tool_DetailGrip", () => RoundedBox(new Vector3(0.04f, 0.018f, 0.07f), 0.008f)), c["RedPlastic"],
                new Vector3(0f, 0.018f, -0.02f), Quaternion.identity);
            Part("Bristles", root, store.Add("Tool_DetailBristles", () => BristleBlock(new Vector2(0.044f, 0.12f), 0.03f, 4, 10, 0.006f)), c["Bristle"],
                new Vector3(0f, -0.015f, 0.03f), Quaternion.identity);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, -0.05f, 0.05f)) };
        }

        /// <summary>Couche « Player », sans ombres, sans collisions (le modèle traverse les murs sans gêner).</summary>
        public static void Finish(Model model)
        {
            SetLayerRecursively(model.Root.gameObject, RugWashPaths.PlayerLayer);
            DisableShadows(model.Root.gameObject);
        }
    }
}
