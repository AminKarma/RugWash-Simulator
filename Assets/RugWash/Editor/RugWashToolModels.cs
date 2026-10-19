using System.Collections.Generic;
using RugWash.Effects;
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

        /// <summary>
        /// V0.3.0 : pièce modélisée dans Blender (Tools/Blender/models_tools.py, même encombrement et même repère que la pièce
        /// procédurale) si elle a été exportée, sinon la pièce procédurale d'origine.
        /// </summary>
        static Mesh Modeled(MeshStore store, string key, System.Func<Mesh> procedural) =>
            RugWashModelLibrary.Mesh(store, "Tools", key) ?? RugWashModelLibrary.Mesh(store, "Machines", key) ?? store.Add(key, procedural);

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
            // V0.3.0 : gant modélisé dans Blender (Tools/Blender/models_hands.py) — doigts refermés sur la poignée, pouce,
            // manchette épaisse — dans le même repère que l'ancien gant ; miroir pour la main gauche.
            var fist = RugWashModelLibrary.Mesh(store, "Glove", "Fist", mirrorX: left);
            var cuffMesh = RugWashModelLibrary.Mesh(store, "Glove", "Cuff", mirrorX: left);
            if (fist != null && cuffMesh != null)
            {
                Part("Fist", glove, fist, c["Glove"], Vector3.zero, Quaternion.identity);
                Part("Cuff", glove, cuffMesh, c["GloveCuff"], Vector3.zero, Quaternion.identity);
                return glove;
            }
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
            Part("Body", head, Modeled(store, "Tool_BrushBody", () => RoundedBox(new Vector3(0.23f, 0.045f, 0.09f), 0.018f)), c["GreenPlastic"],
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
            Part("Grip", root, Modeled(store, "Tool_GunGrip", () => RoundedBox(new Vector3(0.036f, 0.12f, 0.052f), 0.014f)), c["Rubber"],
                new Vector3(0f, -0.075f, -0.03f), Quaternion.Euler(-18f, 0f, 0f));
            Part("Body", root, Modeled(store, "Tool_GunBody", () => RoundedBox(new Vector3(0.056f, 0.068f, 0.17f), 0.02f)), c["SafetyYellow"],
                new Vector3(0f, 0f, 0.04f), Quaternion.identity);
            Part("Trigger", root, Modeled(store, "Tool_GunTrigger", () => RoundedBox(new Vector3(0.012f, 0.048f, 0.022f), 0.005f)), c["DarkMetal"],
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
            // Grande mise à jour : flacon en plastique vert translucide, et le liquide en volume à l'intérieur (son niveau suit la
            // réserve du joueur, sa surface reste à plat et ballotte).
            Part("Bottle_Shell", root, store.Add("Tool_SoapBottle", () => Lathe(new[]
                {
                    new Vector2(0f, -0.2f), new Vector2(0.038f, -0.2f), new Vector2(0.044f, -0.19f), new Vector2(0.044f, -0.06f),
                    new Vector2(0.036f, -0.03f), new Vector2(0.016f, -0.012f), new Vector2(0.016f, 0f),
                }, 24, capTop: false)), c["BottleShell"], new Vector3(0f, -0.04f, 0.02f), Quaternion.identity);
            Part("Bottle", root, store.Add("Tool_SoapLiquid", () => Lathe(new[]
                {
                    new Vector2(0f, -0.196f), new Vector2(0.035f, -0.196f), new Vector2(0.0405f, -0.188f), new Vector2(0.0405f, -0.062f),
                    new Vector2(0.033f, -0.034f), new Vector2(0.013f, -0.017f), new Vector2(0f, -0.017f),
                }, 24)), c["LiquidBottle"], new Vector3(0f, -0.04f, 0.02f), Quaternion.identity);
            Part("Label", root, store.Add("Tool_SoapLabel", () => Lathe(new[] { new Vector2(0.0445f, -0.16f), new Vector2(0.0445f, -0.09f) }, 24, false, false)),
                c["WhitePlastic"], new Vector3(0f, -0.04f, 0.02f), Quaternion.identity);
            Part("Cap", root, Modeled(store, "Tool_SoapCap", () => Cylinder(0.02f, 0.02f, 18)), c["WhitePlastic"], new Vector3(0f, -0.03f, 0.02f), Quaternion.identity);
            Part("Head", root, Modeled(store, "Tool_SoapHead", () => RoundedBox(new Vector3(0.034f, 0.046f, 0.11f), 0.012f)), c["WhitePlastic"],
                new Vector3(0f, 0.005f, 0.05f), Quaternion.Euler(-4f, 0f, 0f));
            Part("Trigger", root, Modeled(store, "Tool_SoapTrigger", () => RoundedBox(new Vector3(0.014f, 0.055f, 0.02f), 0.006f)), c["BluePlastic"],
                new Vector3(0f, -0.03f, 0.085f), Quaternion.Euler(-22f, 0f, 0f));
            Part("Nozzle", root, StraightTube(store, "Tool_SoapNozzle", 0.008f, 0.02f), c["BluePlastic"], new Vector3(0f, 0.012f, 0.105f), AlongZ);
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.085f, 0.02f), Vector3.up, new Vector3(0.15f, -0.3f, -0.94f), left: false);
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, 0.012f, 0.13f)), RightHand = right };
        }

        // ------------------------------------------------------------------ Aspirateur

        public static Model Vacuum(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Vacuum", new Vector3(0.26f, -0.29f, 0.44f), new Vector3(8f, -12f, 0f));
            Part("Grip", root, Modeled(store, "Tool_VacGrip", () => RoundedBox(new Vector3(0.04f, 0.12f, 0.058f), 0.016f)), c["RedPlastic"],
                new Vector3(0f, -0.06f, -0.05f), Quaternion.Euler(-14f, 0f, 0f));
            Part("Body", root, Modeled(store, "Tool_VacBody", () => RoundedBox(new Vector3(0.075f, 0.085f, 0.15f), 0.028f)), c["DarkMetal"],
                new Vector3(0f, 0.005f, 0.01f), Quaternion.identity);
            Part("Body_Stripe", root, store.Add("Tool_VacStripe", () => RoundedBox(new Vector3(0.077f, 0.02f, 0.12f), 0.008f)), c["RedPlastic"],
                new Vector3(0f, 0.03f, 0.01f), Quaternion.identity);
            var shaft = Shaft(root, store, "Tool_VacWandStraight", c["Chrome"], new Vector3(0f, 0f, 0.08f), new Vector3(0f, -0.065f, 0.43f), 0.016f, out float length);
            var head = HeadGroup(root);
            Part("Head", head, Modeled(store, "Tool_VacHead", () => RoundedBox(new Vector3(0.25f, 0.038f, 0.075f), 0.016f)), c["DarkMetal"],
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
            Part("Grip", root, Modeled(store, "Tool_GunGrip", () => RoundedBox(new Vector3(0.036f, 0.12f, 0.052f), 0.014f)), c["Rubber"],
                new Vector3(0f, -0.075f, -0.03f), Quaternion.Euler(-18f, 0f, 0f));
            Part("Body", root, Modeled(store, "Tool_CannonBody", () => RoundedBox(new Vector3(0.06f, 0.07f, 0.2f), 0.022f)), c["BluePlastic"],
                new Vector3(0f, 0f, 0.05f), Quaternion.identity);
            Part("Trigger", root, Modeled(store, "Tool_GunTrigger", () => RoundedBox(new Vector3(0.012f, 0.048f, 0.022f), 0.005f)), c["DarkMetal"],
                new Vector3(0f, -0.05f, 0.04f), Quaternion.Euler(-12f, 0f, 0f));
            // Bouteille de mousse translucide sous le corps, avec sa bague.
            Part("Canister_Shell", root, store.Add("Tool_CannonCanister", () => Lathe(new[]
                {
                    new Vector2(0f, -0.17f), new Vector2(0.042f, -0.17f), new Vector2(0.048f, -0.155f), new Vector2(0.048f, -0.04f),
                    new Vector2(0.03f, -0.015f), new Vector2(0.018f, 0f),
                }, 24, capTop: false)), c["CanisterShell"], new Vector3(0f, -0.04f, 0.13f), Quaternion.identity);
            Part("Canister", root, store.Add("Tool_CannonLiquid", () => Lathe(new[]
                {
                    new Vector2(0f, -0.166f), new Vector2(0.039f, -0.166f), new Vector2(0.0445f, -0.153f), new Vector2(0.0445f, -0.042f),
                    new Vector2(0.027f, -0.019f), new Vector2(0f, -0.019f),
                }, 24)), c["LiquidCanister"], new Vector3(0f, -0.04f, 0.13f), Quaternion.identity);
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
            Part("Body", head, Modeled(store, "Tool_DetailBody", () => RoundedBox(new Vector3(0.05f, 0.03f, 0.16f), 0.012f)), c["Wood"],
                new Vector3(0f, 0f, 0.02f), Quaternion.identity);
            Part("Grip", head, store.Add("Tool_DetailGrip", () => RoundedBox(new Vector3(0.04f, 0.018f, 0.07f), 0.008f)), c["RedPlastic"],
                new Vector3(0f, 0.018f, -0.02f), Quaternion.identity);
            Part("Bristles", head, store.Add("Tool_DetailBristles", () => BristleBlock(new Vector2(0.044f, 0.12f), 0.03f, 4, 10, 0.006f)), c["Bristle"],
                new Vector3(0f, -0.015f, 0.03f), Quaternion.identity);
            var tip = Tip(head, new Vector3(0f, -0.05f, 0.05f));
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, 0.004f, -0.16f), Vector3.forward, new Vector3(0.2f, -0.55f, -0.8f), left: false);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right };
        }

        // ------------------------------------------------------------------ Seau d'eau (kit de départ)

        public static Model Bucket(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            // Pivot = la main, sur l'anse : le seau pend dessous et bascule vers l'avant pour verser.
            var root = Root(camera, "Bucket", new Vector3(0.24f, -0.27f, 0.46f), new Vector3(4f, -10f, 0f));
            Part("Body", root, Modeled(store, "Tool_BucketBody", () => Lathe(new[]
            {
                new Vector2(0f, -0.36f), new Vector2(0.1f, -0.36f), new Vector2(0.105f, -0.35f), new Vector2(0.13f, -0.13f), new Vector2(0.134f, -0.12f),
            }, 28, capTop: false)), c["BluePlastic"], Vector3.zero, Quaternion.identity);
            var rim = new List<Vector3>();
            for (int i = 0; i <= 28; i++)
            {
                float a = i / 28f * Mathf.PI * 2f;
                rim.Add(new Vector3(Mathf.Cos(a) * 0.134f, -0.12f, Mathf.Sin(a) * 0.134f));
            }
            Part("Rim", root, store.Add("Tool_BucketRim", () => Tube(rim, 0.007f, 8)), c["DarkMetal"], Vector3.zero, Quaternion.identity);
            Part("Handle", root, store.Add("Tool_BucketHandle", () => Tube(Smooth(new[]
                {
                    new Vector3(-0.134f, -0.12f, 0f), new Vector3(-0.1f, -0.03f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0.1f, -0.03f, 0f),
                    new Vector3(0.134f, -0.12f, 0f),
                }, 8), 0.005f, 8)), c["Steel"], Vector3.zero, Quaternion.identity);
            Part("Water", root, store.Add("Tool_BucketWater", () => Cylinder(0.126f, 0.004f, 28)), c["Glass"], new Vector3(0f, -0.17f, 0f), Quaternion.identity);
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, 0.005f, 0f), Vector3.right, new Vector3(0.25f, 0.55f, -0.8f), left: false);
            // Point de versement : le bord avant du seau.
            return new Model { Root = root, Tip = Tip(root, new Vector3(0f, -0.12f, 0.136f)), RightHand = right };
        }

        // ------------------------------------------------------------------ Raclette (v0.1.2 jalon 4)

        public static Model Squeegee(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Squeegee", new Vector3(0.2f, -0.3f, 0.42f), new Vector3(10f, -9f, 0f));
            var start = new Vector3(0f, 0.12f, -0.25f);
            var end = new Vector3(0f, -0.17f, 0.56f);
            Vector3 axis = (end - start).normalized;
            var shaft = Shaft(root, store, "Tool_SqueegeeHandle", c["Steel"], start, end, 0.012f, out float length);
            Part("Grip", root, store.Add("Tool_SqueegeeGrip", () => Tube(new[] { new Vector3(0f, 0.1f, -0.2f), new Vector3(0f, 0.06f, -0.08f) }, 0.016f, 12)),
                c["BluePlastic"], Vector3.zero, Quaternion.identity);
            var head = HeadGroup(root);
            // Support en plastique et lame en caoutchouc, larges, perpendiculaires au manche.
            Part("Holder", head, Modeled(store, "Tool_SqueegeeHolder", () => RoundedBox(new Vector3(0.55f, 0.035f, 0.045f), 0.01f)), c["BluePlastic"],
                new Vector3(0f, -0.19f, 0.6f), Quaternion.Euler(18f, 0f, 0f));
            Part("Blade", head, store.Add("Tool_SqueegeeBlade", () => RoundedBox(new Vector3(0.57f, 0.05f, 0.012f), 0.004f)), c["Rubber"],
                new Vector3(0f, -0.225f, 0.612f), Quaternion.Euler(18f, 0f, 0f));
            var tip = Tip(head, new Vector3(0f, -0.255f, 0.622f));
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, 0.08f, -0.14f), axis, new Vector3(0.3f, -0.6f, -0.74f), left: false);
            var leftHand = Support(root, start + axis * 0.5f);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right, LeftHand = leftHand };
        }

        // ------------------------------------------------------------------ Monobrosse (v0.1.2 jalon 4)

        /// <summary>Monobrosse : guidon à deux mains, manche vers la machine (moteur, jupe, disque rotatif « Head/Disc »).</summary>
        public static Model Rotary(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Rotary", new Vector3(0.1f, -0.34f, 0.4f), new Vector3(8f, -4f, 0f));
            var start = new Vector3(0f, 0.06f, -0.2f);
            var end = new Vector3(0f, -0.2f, 0.5f);
            var shaft = Shaft(root, store, "Tool_RotaryShaft", c["Steel"], start, end, 0.018f, out float length);
            Part("Handlebar", root, store.Add("Tool_RotaryHandlebar", () => Tube(new[] { new Vector3(-0.17f, 0.06f, -0.2f), new Vector3(0.17f, 0.06f, -0.2f) }, 0.015f, 12)),
                c["Rubber"], Vector3.zero, Quaternion.identity);
            Part("Switch", root, store.Add("Tool_RotarySwitch", () => RoundedBox(new Vector3(0.05f, 0.03f, 0.04f), 0.008f)), c["RedPlastic"],
                new Vector3(0f, 0.075f, -0.19f), Quaternion.identity);
            var head = HeadGroup(root);
            Part("Motor", head, Modeled(store, "Tool_RotaryMotor", () => Lathe(new[]
            {
                new Vector2(0f, 0.17f), new Vector2(0.05f, 0.165f), new Vector2(0.1f, 0.13f), new Vector2(0.12f, 0.06f), new Vector2(0.12f, 0f),
            }, 28, capBottom: false)), c["SafetyYellow"], new Vector3(0f, -0.43f, 0.56f), Quaternion.identity);
            Part("Skirt", head, store.Add("Tool_RotarySkirt", () => Cylinder(0.2f, 0.05f, 32)), c["DarkMetal"], new Vector3(0f, -0.455f, 0.56f), Quaternion.identity);
            // Disque rotatif : plateau et brosse, plus large que la jupe pour que ses repères tournent à la vue.
            var disc = new GameObject("Disc").transform;
            disc.SetParent(head, false);
            disc.localPosition = new Vector3(0f, -0.49f, 0.56f);
            Part("Plate", disc, store.Add("Tool_RotaryPlate", () => Cylinder(0.215f, 0.02f, 32)), c["RedPlastic"], Vector3.zero, Quaternion.identity);
            Part("Pad", disc, store.Add("Tool_RotaryPad", () => Cylinder(0.205f, 0.02f, 32)), c["Bristle"], new Vector3(0f, -0.018f, 0f), Quaternion.identity);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Part($"Marker_{i}", disc, store.Add("Tool_RotaryMarker", () => RoundedBox(new Vector3(0.03f, 0.024f, 0.03f), 0.006f)), c["Chrome"],
                    new Vector3(Mathf.Cos(a) * 0.21f, 0.002f, Mathf.Sin(a) * 0.21f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
            }
            var tip = Tip(head, new Vector3(0f, -0.515f, 0.56f));
            var right = Glove(root, store, c, "Glove_R", new Vector3(0.13f, 0.06f, -0.2f), Vector3.right, new Vector3(0.15f, -0.45f, -0.88f), left: false);
            var left = Glove(root, store, c, "Glove_L", new Vector3(-0.13f, 0.06f, -0.2f), Vector3.right, new Vector3(-0.15f, -0.45f, -0.88f), left: true);
            return new Model { Root = root, Tip = tip, Head = head, Shaft = shaft, ShaftLength = length, RightHand = right, LeftHand = left };
        }

        // ------------------------------------------------------------------ Gadgets de farce (v0.1.2 jalon 6)

        /// <summary>Batte de baseball en bois, poignée scotchée : tenue droite, prête à frapper (rotation autour de la poignée).</summary>
        public static Model Bat(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Bat", new Vector3(0.3f, -0.3f, 0.36f), new Vector3(-58f, -14f, -8f));
            Part("Bat", root, store.Add("Prank_Bat", () => Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.028f, 0f), new Vector2(0.03f, 0.012f), new Vector2(0.017f, 0.03f), new Vector2(0.015f, 0.06f),
                new Vector2(0.016f, 0.3f), new Vector2(0.022f, 0.44f), new Vector2(0.031f, 0.58f), new Vector2(0.036f, 0.72f),
                new Vector2(0.035f, 0.8f), new Vector2(0.027f, 0.825f), new Vector2(0f, 0.832f),
            }, 22, capBottom: false, capTop: false)), c["Wood"], Vector3.zero, AlongZ);
            Part("Tape", root, store.Add("Prank_BatTape", () => Lathe(new[] { new Vector2(0.0175f, 0.035f), new Vector2(0.0175f, 0.25f) }, 18)), c["Rubber"],
                Vector3.zero, AlongZ);
            var tip = Tip(root, new Vector3(0f, 0f, 0.78f));
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, 0f, 0.09f), Vector3.forward, new Vector3(0.2f, -0.6f, -0.75f), left: false);
            var left = Support(root, new Vector3(0f, 0f, 0.17f));
            return new Model { Root = root, Tip = tip, RightHand = right, LeftHand = left };
        }

        /// <summary>
        /// Fusil à pompe de farces et attrapes : plastique bleu et blanc, embout orange (jouet), pompe orange. Il ne tire que du
        /// faux sang de théâtre sur les tapis ; « Muzzle » marque la bouche.
        /// </summary>
        public static Model Blaster(Transform camera, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = Root(camera, "Blaster", new Vector3(0.22f, -0.24f, 0.36f), new Vector3(2f, -5f, 0f));
            Part("Receiver", root, store.Add("Prank_Receiver", () => RoundedBox(new Vector3(0.055f, 0.075f, 0.24f), 0.018f)), c["BluePlastic"],
                new Vector3(0f, 0f, 0.05f), Quaternion.identity);
            Part("Stock", root, store.Add("Prank_Stock", () => RoundedBox(new Vector3(0.045f, 0.085f, 0.17f), 0.02f)), c["BluePlastic"],
                new Vector3(0f, -0.025f, -0.14f), Quaternion.Euler(-8f, 0f, 0f));
            Part("Grip", root, store.Add("Prank_Grip", () => RoundedBox(new Vector3(0.035f, 0.09f, 0.045f), 0.012f)), c["Rubber"],
                new Vector3(0f, -0.07f, -0.03f), Quaternion.Euler(-18f, 0f, 0f));
            Part("Barrel", root, StraightTube(store, "Prank_Barrel", 0.019f, 0.34f), c["WhitePlastic"], new Vector3(0f, 0.012f, 0.16f), AlongZ);
            Part("Tube", root, StraightTube(store, "Prank_Magazine", 0.014f, 0.26f), c["WhitePlastic"], new Vector3(0f, -0.022f, 0.16f), AlongZ);
            Part("Pump", root, store.Add("Prank_Pump", () => RoundedBox(new Vector3(0.05f, 0.045f, 0.11f), 0.015f)), c["StepOrange"],
                new Vector3(0f, -0.022f, 0.3f), Quaternion.identity);
            Part("OrangeTip", root, store.Add("Prank_OrangeTip", () => Cylinder(0.024f, 0.04f, 18)), c["StepOrange"],
                new Vector3(0f, 0.012f, 0.515f), AlongZ);
            Part("Stripe", root, store.Add("Prank_Stripe", () => RoundedBox(new Vector3(0.057f, 0.016f, 0.16f), 0.006f)), c["SafetyYellow"],
                new Vector3(0f, 0.02f, 0.05f), Quaternion.identity);
            var tip = Tip(root, new Vector3(0f, 0.012f, 0.54f));
            tip.name = "Muzzle";
            var right = Glove(root, store, c, "Glove_R", new Vector3(0f, -0.07f, -0.03f), new Vector3(0f, 0.95f, -0.3f), new Vector3(0.25f, -0.55f, -0.8f), left: false);
            var left = Support(root, new Vector3(0f, -0.04f, 0.3f));
            return new Model { Root = root, Tip = tip, RightHand = right, LeftHand = left };
        }

        /// <summary>Matériaux des pièces d'outil qui passent à l'or au niveau maximal (gants, poils, tuyaux, liquides : non).</summary>
        static readonly string[] GildedMaterials =
            { "BluePlastic", "GreenPlastic", "RedPlastic", "WhitePlastic", "SafetyYellow", "Steel", "Chrome", "DarkMetal", "FoamCanister", "Wood", "Plywood" };

        /// <summary>Finition dorée (grande mise à jour) : pièces dorables du modèle, matériau or de l'atelier.</summary>
        public static GoldenToolSkin Gild(Model model, RugWashContentBuilder.Content c)
        {
            var gilded = new List<Material>();
            foreach (string name in GildedMaterials)
                if (c.Materials.TryGetValue(name, out var material) && material != null) gilded.Add(material);
            var renderers = new List<Renderer>();
            foreach (var renderer in model.Root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (gilded.Contains(material))
                    {
                        renderers.Add(renderer);
                        break;
                    }
            var skin = model.Root.gameObject.AddComponent<GoldenToolSkin>();
            Wire(skin, ("gold", c["Gold"]));
            WireList(skin, "renderers", renderers);
            WireList(skin, "gilded", gilded);
            return skin;
        }

        /// <summary>Vue à la première personne : couche « Player », sans ombres, sans collisions.</summary>
        public static void Finish(Model model)
        {
            SetLayerRecursively(model.Root.gameObject, RugWashPaths.PlayerLayer);
            DisableShadows(model.Root.gameObject);
        }
    }
}
