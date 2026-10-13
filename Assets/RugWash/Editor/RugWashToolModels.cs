using System.Collections.Generic;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>
    /// Modèles tenus en main des huit outils, construits à partir de maillages procéduraux (repère du support : X droite,
    /// Y haut, Z avant). Les mêmes modèles servent à la vue à la première personne et aux silhouettes des autres joueurs.
    /// Chaque modèle fournit : sa racine (pivot = main), l'extrémité active (buse ou tête), les gants (prise de la main
    /// droite, et de la gauche pour les outils à deux mains) et, pour les outils de contact, un manche télescopique
    /// (groupe « Head » déplacé le long du groupe « Shaft », lui-même étiré) qui amène la tête sur le point visé.
    /// Les gants et la silhouette sont des placeholders procéduraux propres, pas des assets définitifs.
    /// </summary>
    public static class RugWashToolModels
    {
        public sealed class Model
        {
            public Transform Root;
            /// <summary>Point d'émission (jet, brume) ou tête d'aspiration / de brossage.</summary>
            public Transform Tip;
            /// <summary>Outil de contact : tête déplacée le long du manche (null pour une buse).</summary>
            public Transform Head;
            /// <summary>Outil de contact : manche étiré le long de son axe Z local (null pour une buse).</summary>
            public Transform Shaft;
            public float ShaftLength;
            /// <summary>Gant de la main droite, refermé sur la poignée (visible à la première personne et sur les silhouettes).</summary>
            public Transform RightHand;
            /// <summary>Point de soutien de la main gauche (manche, lance, bouteille) pour les silhouettes ; sans maillage. Peut être nul.</summary>
            public Transform LeftHand;
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

        static Transform Root(Transform support, string name, Vector3 position, Vector3 euler)
        {
            var root = new GameObject($"ViewModel_{name}").transform;
            root.SetParent(support, false);
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

        /// <summary>Groupe « Head » (enfant direct de la racine, à l'origine) : la tête d'un outil de contact.</summary>
        static Transform HeadGroup(Transform root)
        {
            var head = new GameObject("Head").transform;
            head.SetParent(root, false);
            return head;
        }

        /// <summary>
        /// Manche droit de <paramref name="start"/> à <paramref name="end"/> (repère de l'outil) : groupe « Shaft » orienté
        /// (Z = axe du manche) contenant un tube, étiré par l'échelle Z pour allonger le manche.
        /// </summary>
        static Transform Shaft(Transform root, MeshStore store, string key, Material material, Vector3 start, Vector3 end, float radius, out float length)
        {
            var shaft = new GameObject("Shaft").transform;
            shaft.SetParent(root, false);
            shaft.localPosition = start;
            shaft.localRotation = Quaternion.LookRotation(end - start, Vector3.up);
            float tubeLength = (end - start).magnitude;
            length = tubeLength;
            Part("Shaft_Tube", shaft, store.Add(key, () => Lathe(new[] { new Vector2(radius, 0f), new Vector2(radius, tubeLength) }, 14)), material,
                Vector3.zero, AlongZ);
            return shaft;
        }

        /// <summary>Tube droit le long de Z (de z0 à z1), via une révolution.</summary>
        static Mesh StraightTube(MeshStore store, string key, float radius, float length) =>
            store.Add(key, () => Lathe(new[] { new Vector2(radius, 0f), new Vector2(radius, length) }, 16));

        /// <summary>Point de soutien de la main gauche (transform vide).</summary>
        static Transform Support(Transform root, Vector3 position)
        {
            var support = new GameObject("Support_L").transform;
            support.SetParent(root, false);
            support.localPosition = position;
            return support;
        }

        /// <summary>
        /// Gant de travail refermé sur une poignée : poing, pouce, manchette. <paramref name="through"/> : axe de la poignée qui
        /// traverse le poing ; <paramref name="toWrist"/> : direction du poignet (vers le joueur). Placeholder procédural.
        /// </summary>
        static Transform Glove(Transform parent, MeshStore store, RugWashContentBuilder.Content c, string name, Vector3 position,
            Vector3 through, Vector3 toWrist, bool left)
        {
            var glove = new GameObject(name).transform;
            glove.SetParent(parent, false);
            glove.localPosition = position;
            glove.localRotation = Quaternion.LookRotation(-toWrist, through);
            float side = left ? -1f : 1f;
            Part("Fist", glove, store.Add("Glove_Fist", () => RoundedBox(new Vector3(0.074f, 0.086f, 0.07f), 0.026f)), c["Glove"],
                Vector3.zero, Quaternion.identity);
            Part("Knuckles", glove, store.Add("Glove_Knuckles", () => RoundedBox(new Vector3(0.03f, 0.08f, 0.04f), 0.012f)), c["Glove"],
                new Vector3(side * 0.022f, -0.002f, 0.022f), Quaternion.identity);
            Part("Thumb", glove, store.Add("Glove_Thumb", () => RoundedBox(new Vector3(0.024f, 0.05f, 0.026f), 0.011f)), c["Glove"],
                new Vector3(-side * 0.03f, 0.03f, 0.018f), Quaternion.Euler(0f, 0f, side * 25f));
            Part("Cuff", glove, store.Add("Glove_Cuff", () => Lathe(new[] { new Vector2(0.036f, 0f), new Vector2(0.04f, 0.05f) }, 14)), c["GloveCuff"],
                new Vector3(0f, 0f, -0.03f), Quaternion.Euler(-90f, 0f, 0f));
            return glove;
        }

        // ------------------------------------------------------------------ Brosse

        public static Model Brush(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Brush", new Vector3(0.27f, -0.3f, 0.48f), new Vector3(10f, -14f, 0f));
            var handleRotation = Quaternion.Euler(96f, 0f, 0f);
            Vector3 axis = handleRotation * Vector3.up;
            var handleStart = new Vector3(0f, 0.03f, -0.02f);
            Part("Grip", root, store.Add("Tool_BrushGrip", () => Lathe(new[]
            {
                new Vector2(0.0f, 0f), new Vector2(0.017f, 0.003f), new Vector2(0.02f, 0.03f), new Vector2(0.017f, 0.07f), new Vector2(0.015f, 0.1f),
            }, 18, capBottom: false, capTop: true)), c["Wood"], handleStart, handleRotation);
            var shaft = Shaft(root, store, "Tool_BrushShaft", c["Wood"], handleStart + axis * 0.098f, handleStart + axis * 0.33f, 0.015f, out float length);

            var head = HeadGroup(root);
            Part("Ferrule", head, store.Add("Tool_BrushFerrule", () => Cylinder(0.019f, 0.03f, 18)), c["Chrome"],
                new Vector3(0f, -0.005f, 0.33f), handleRotation);
            Part("Body", head, store.Add("Tool_BrushBody", () => RoundedBox(new Vector3(0.23f, 0.045f, 0.09f), 0.018f)), c["GreenPlastic"],
                new Vector3(0f, -0.03f, 0.4f), Quaternion.identity);
            Part("Grip_Strip", head, store.Add("Tool_BrushStrip", () => RoundedBox(new Vector3(0.2f, 0.012f, 0.03f), 0.005f)), c["Rubber"],
                new Vector3(0f, -0.005f, 0.4f), Quaternion.identity);
            Part("Bristles", head, store.Add("Tool_BrushBristles", () => BristleBlock(new Vector2(0.21f, 0.075f), 0.045f, 16, 6, 0.009f)), c["Bristle"],
                new Vector3(0f, -0.05f, 0.4f), Quaternion.identity);
            var tip = Tip(head, new Vector3(0f, -0.09f, 0.4f));

            var right = Glove(root, store, c, "Glove_R", handleStart + axis * 0.05f, axis, new Vector3(0.25f, -0.7f, -0.66f), left: false);
            var leftHand = Support(root, handleStart + axis * 0.2f);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right, LeftHand = leftHand };
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
            var gripAxis = Quaternion.Euler(-18f, 0f, 0f) * Vector3.up;
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.07f, -0.03f), gripAxis, new Vector3(0.1f, -0.35f, -0.93f), left: false);
            var leftHand = Support(root, new Vector3(0f, 0.005f, 0.24f));
            // Point d'émission : bouche de la buse (le jet en part, chez soi comme chez les autres).
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.01f, 0.53f)), RightHand = right, LeftHand = leftHand };
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
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.085f, 0.02f), Vector3.up, new Vector3(0.15f, -0.3f, -0.94f), left: false);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.012f, 0.13f)), RightHand = right };
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
            var shaft = Shaft(root, store, "Tool_VacWandStraight", c["Chrome"], new Vector3(0f, 0f, 0.08f), new Vector3(0f, -0.065f, 0.43f), 0.016f, out float length);
            var head = HeadGroup(root);
            Part("Head", head, store.Add("Tool_VacHead", () => RoundedBox(new Vector3(0.25f, 0.038f, 0.075f), 0.016f)), c["DarkMetal"],
                new Vector3(0f, -0.085f, 0.46f), Quaternion.Euler(8f, 0f, 0f));
            Part("Head_Lip", head, store.Add("Tool_VacLip", () => RoundedBox(new Vector3(0.252f, 0.012f, 0.078f), 0.005f)), c["RedPlastic"],
                new Vector3(0f, -0.105f, 0.463f), Quaternion.Euler(8f, 0f, 0f));
            Part("Head_Bristles", head, store.Add("Tool_VacBristles", () => BristleBlock(new Vector2(0.22f, 0.012f), 0.012f, 22, 1, 0.006f)), c["Rubber"],
                new Vector3(0f, -0.11f, 0.47f), Quaternion.Euler(8f, 0f, 0f));
            var tip = Tip(head, new Vector3(0f, -0.116f, 0.47f));
            Part("Hose", root, store.Add("Tool_VacHose", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, 0.02f, -0.07f), new Vector3(0.02f, 0.01f, -0.16f), new Vector3(0.08f, -0.25f, -0.2f), new Vector3(0.15f, -0.8f, -0.05f),
                }, 10), 0.02f, 12)), c["Rubber"], Vector3.zero, Quaternion.identity);
            var gripAxis = Quaternion.Euler(-14f, 0f, 0f) * Vector3.up;
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.06f, -0.05f), gripAxis, new Vector3(0.1f, -0.35f, -0.93f), left: false);
            var wandAxis = (new Vector3(0f, -0.065f, 0.43f) - new Vector3(0f, 0f, 0.08f)).normalized;
            var leftHand = Support(root, new Vector3(0f, 0f, 0.08f) + wandAxis * 0.1f);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right, LeftHand = leftHand };
        }

        // ------------------------------------------------------------------ Extracteur

        public static Model Extractor(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Extractor", new Vector3(0.26f, -0.3f, 0.44f), new Vector3(9f, -12f, 0f));
            Part("Grip", root, store.Add("Tool_ExtGrip", () => RoundedBox(new Vector3(0.042f, 0.12f, 0.06f), 0.016f)), c["Rubber"],
                new Vector3(0f, -0.06f, -0.05f), Quaternion.Euler(-14f, 0f, 0f));
            Part("Valve", root, store.Add("Tool_ExtValve", () => RoundedBox(new Vector3(0.06f, 0.06f, 0.1f), 0.02f)), c["BluePlastic"],
                new Vector3(0f, 0.0f, 0.02f), Quaternion.identity);
            var shaft = Shaft(root, store, "Tool_ExtWandStraight", c["Steel"], new Vector3(0f, -0.005f, 0.06f), new Vector3(0f, -0.075f, 0.43f), 0.018f, out float length);
            var head = HeadGroup(root);
            Part("Head", head, store.Add("Tool_ExtHead", () => RoundedBox(new Vector3(0.28f, 0.05f, 0.06f), 0.02f)), c["BluePlastic"],
                new Vector3(0f, -0.1f, 0.46f), Quaternion.Euler(12f, 0f, 0f));
            Part("Head_Window", head, store.Add("Tool_ExtWindow", () => RoundedBox(new Vector3(0.24f, 0.03f, 0.062f), 0.01f)), c["Glass"],
                new Vector3(0f, -0.093f, 0.463f), Quaternion.Euler(12f, 0f, 0f));
            Part("Head_Slot", head, store.Add("Tool_ExtSlot", () => RoundedBox(new Vector3(0.26f, 0.01f, 0.018f), 0.004f)), c["Rubber"],
                new Vector3(0f, -0.128f, 0.47f), Quaternion.Euler(12f, 0f, 0f));
            var tip = Tip(head, new Vector3(0f, -0.133f, 0.47f));
            Part("Hose", root, store.Add("Tool_ExtHose", () => Tube(Smooth(new[]
                {
                    new Vector3(0f, 0.01f, -0.04f), new Vector3(0.02f, 0.0f, -0.14f), new Vector3(0.09f, -0.28f, -0.2f), new Vector3(0.16f, -0.8f, -0.05f),
                }, 10), 0.022f, 12)), c["GreyHose"], Vector3.zero, Quaternion.identity);
            var gripAxis = Quaternion.Euler(-14f, 0f, 0f) * Vector3.up;
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.06f, -0.05f), gripAxis, new Vector3(0.1f, -0.35f, -0.93f), left: false);
            var wandAxis = (new Vector3(0f, -0.075f, 0.43f) - new Vector3(0f, -0.005f, 0.06f)).normalized;
            var leftHand = Support(root, new Vector3(0f, -0.005f, 0.06f) + wandAxis * 0.11f);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right, LeftHand = leftHand };
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
            var gripAxis = Quaternion.Euler(-18f, 0f, 0f) * Vector3.up;
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.07f, -0.03f), gripAxis, new Vector3(0.1f, -0.35f, -0.93f), left: false);
            var leftHand = Support(root, new Vector3(0f, -0.11f, 0.13f));
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.01f, 0.4f)), RightHand = right, LeftHand = leftHand };
        }

        // ------------------------------------------------------------------ Balai

        public static Model Broom(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Broom", new Vector3(0.2f, -0.3f, 0.42f), new Vector3(10f, -9f, 0f));
            var start = new Vector3(0f, 0.12f, -0.25f);
            var end = new Vector3(0f, -0.17f, 0.56f);
            Vector3 axis = (end - start).normalized;
            var shaft = Shaft(root, store, "Tool_BroomHandleStraight", c["Wood"], start, end, 0.014f, out float length);
            Part("Grip", root, store.Add("Tool_BroomGrip", () => Tube(new[] { new Vector3(0f, 0.1f, -0.2f), new Vector3(0f, 0.06f, -0.08f) }, 0.017f, 12)),
                c["Rubber"], Vector3.zero, Quaternion.identity);
            var head = HeadGroup(root);
            Part("Head", head, store.Add("Tool_BroomHead", () => RoundedBox(new Vector3(0.44f, 0.04f, 0.065f), 0.012f)), c["SafetyYellow"],
                new Vector3(0f, -0.19f, 0.6f), Quaternion.Euler(18f, 0f, 0f));
            Part("Bristles", head, store.Add("Tool_BroomBristles", () => BristleBlock(new Vector2(0.42f, 0.05f), 0.08f, 28, 3, 0.012f)), c["Bristle"],
                new Vector3(0f, -0.215f, 0.608f), Quaternion.Euler(18f, 0f, 0f));
            var tip = Tip(head, new Vector3(0f, -0.29f, 0.63f));
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, 0.08f, -0.14f), axis, new Vector3(0.3f, -0.6f, -0.74f), left: false);
            var leftHand = Support(root, start + axis * 0.5f);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right, LeftHand = leftHand };
        }

        // ------------------------------------------------------------------ Brosse de détail

        public static Model DetailBrush(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "DetailBrush", new Vector3(0.22f, -0.22f, 0.36f), new Vector3(22f, -12f, 0f));
            // Poignée, tige télescopique, puis brosse dure.
            Part("Handle", root, store.Add("Tool_DetailHandle", () => RoundedBox(new Vector3(0.032f, 0.026f, 0.09f), 0.01f)), c["RedPlastic"],
                new Vector3(0f, 0.004f, -0.16f), Quaternion.identity);
            var shaft = Shaft(root, store, "Tool_DetailRod", c["Chrome"], new Vector3(0f, 0.004f, -0.115f), new Vector3(0f, 0.004f, -0.06f), 0.007f, out float length);
            var head = HeadGroup(root);
            Part("Body", head, store.Add("Tool_DetailBody", () => RoundedBox(new Vector3(0.05f, 0.03f, 0.16f), 0.012f)), c["Wood"],
                new Vector3(0f, 0f, 0.02f), Quaternion.identity);
            Part("Grip", head, store.Add("Tool_DetailGrip", () => RoundedBox(new Vector3(0.04f, 0.018f, 0.07f), 0.008f)), c["RedPlastic"],
                new Vector3(0f, 0.018f, -0.02f), Quaternion.identity);
            Part("Bristles", head, store.Add("Tool_DetailBristles", () => BristleBlock(new Vector2(0.044f, 0.12f), 0.03f, 4, 10, 0.006f)), c["Bristle"],
                new Vector3(0f, -0.015f, 0.03f), Quaternion.identity);
            var tip = Tip(head, new Vector3(0f, -0.05f, 0.05f));
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, 0.004f, -0.16f), Vector3.forward, new Vector3(0.2f, -0.55f, -0.8f), left: false);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right };
        }

        /// <summary>Vue à la première personne : couche « Player », sans ombres, sans collisions.</summary>
        public static void Finish(Model model)
        {
            SetLayerRecursively(model.Root.gameObject, RugWashPaths.PlayerLayer);
            DisableShadows(model.Root.gameObject);
        }
    }
}
