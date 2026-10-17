using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Orders;
using RugWash.Workshop;
using UnityEditor;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>
    /// Machines fonctionnelles de l'atelier (v0.1.2 jalon 5), construites en maillages procéduraux autour de l'aire de
    /// lavage : souffleur de séchage (côté droit), plaque vibrante de dépoussiérage (pupitre avant gauche) et rampe de
    /// pré-mouillage (au-dessus de l'aire, pupitre arrière gauche). Pièces non statiques (pales, cache-visibilité).
    /// </summary>
    public static class RugWashMachineModels
    {
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

        static Transform Group(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = rotation;
            return t;
        }

        static void Label(Transform parent, string text, Vector3 position, float size)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = UIFont;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.95f, 0.93f, 0.85f);
            DepthTestedText(mesh);
        }

        static AudioSource Loop(GameObject owner, AudioClip clip, float volume)
        {
            var source = owner.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 12f;
            source.volume = volume;
            return source;
        }

        /// <summary>Pupitre de commande (interaction E) : colonne, panneau incliné, gros bouton, étiquette.</summary>
        static void Pedestal(Transform root, MeshStore m, RugWashContentBuilder.Content c, string label)
        {
            Part("Column", root, m.Add("Machine_PedestalColumn", () => RoundedBox(new Vector3(0.3f, 0.95f, 0.26f), 0.02f)), c["DarkMetal"],
                new Vector3(0f, 0.475f, 0f), Quaternion.identity);
            Part("Panel", root, m.Add("Machine_PedestalPanel", () => RoundedBox(new Vector3(0.36f, 0.04f, 0.3f), 0.012f)), c["SafetyYellow"],
                new Vector3(0f, 0.97f, 0f), Quaternion.Euler(-25f, 0f, 0f));
            Part("Button", root, m.Add("Machine_Button", () => Cylinder(0.05f, 0.03f, 20)), c["RedPlastic"],
                new Vector3(0f, 1.0f, -0.02f), Quaternion.Euler(-25f, 0f, 0f));
            Label(root, label, new Vector3(0f, 0.72f, -0.135f), 0.012f);
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.size = new Vector3(0.4f, 1.1f, 0.35f);
        }

        static T AddMachine<T>(GameObject root) where T : Component => root.AddComponent<T>();

        /// <summary>Souffleur de séchage : ventilateur sur pied, orienté vers l'aire de lavage.</summary>
        public static WorkshopMachine Blower(Transform parent, MeshStore m, RugWashContentBuilder.Content c, RugCleaningController rug,
            OrderManager orders, AudioClip loop, Vector3 position, float yaw)
        {
            var root = Group("Machine_Blower", parent, position, Quaternion.Euler(0f, yaw, 0f));
            Part("Base", root, m.Add("Blower_Base", () => RoundedBox(new Vector3(0.62f, 0.06f, 0.46f), 0.02f)), c["DarkMetal"],
                new Vector3(0f, 0.03f, 0f), Quaternion.identity);
            foreach (float x in new[] { -0.36f, 0.36f })
                Part("Arm", root, m.Add("Blower_Arm", () => RoundedBox(new Vector3(0.04f, 0.62f, 0.05f), 0.01f)), c["Steel"],
                    new Vector3(x, 0.33f, 0f), Quaternion.identity);
            // Carter rond (anneau), moyeu et grille ; axe du ventilateur = +Z local du groupe « Fan ».
            var fan = Group("Fan", root, new Vector3(0f, 0.62f, 0f), Quaternion.identity);
            var ring = new List<Vector3>();
            for (int i = 0; i <= 36; i++)
            {
                float a = i / 36f * Mathf.PI * 2f;
                ring.Add(new Vector3(Mathf.Cos(a) * 0.34f, Mathf.Sin(a) * 0.34f, 0f));
            }
            Part("Housing", fan, m.Add("Blower_Housing", () => Tube(ring, 0.035f, 10)), c["SafetyYellow"], Vector3.zero, Quaternion.identity);
            Part("Motor", fan, m.Add("Blower_Motor", () => Cylinder(0.09f, 0.16f, 20)), c["DarkMetal"], new Vector3(0f, 0f, -0.1f), Quaternion.Euler(90f, 0f, 0f));
            for (int i = 0; i < 4; i++)
                Part("Grille", fan, m.Add("Blower_Grille", () => RoundedBox(new Vector3(0.66f, 0.01f, 0.01f), 0.003f)), c["Steel"],
                    new Vector3(0f, 0f, 0.05f), Quaternion.Euler(0f, 0f, i * 45f));
            var blades = Group("Blades", fan, new Vector3(0f, 0f, 0.01f), Quaternion.identity);
            Part("Hub", blades, m.Add("Blower_Hub", () => Cylinder(0.06f, 0.05f, 16)), c["Steel"], Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
                Part("Blade", blades, m.Add("Blower_Blade", () => RoundedBox(new Vector3(0.1f, 0.27f, 0.012f), 0.01f)), c["DarkMetal"],
                    Quaternion.Euler(0f, 0f, i * 120f) * new Vector3(0f, 0.17f, 0f), Quaternion.Euler(0f, 0f, i * 120f) * Quaternion.Euler(0f, 18f, 0f));
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.5f, 0f);
            collider.size = new Vector3(0.8f, 1.0f, 0.5f);

            var air = CreateParticles("BlowerAir", fan, c["SoapMist"], new ParticleConfig
            {
                Lifetime = new Vector2(0.6f, 1.1f), Speed = new Vector2(2f, 3.2f), Size = new Vector2(0.08f, 0.16f), Color = new Color(1f, 1f, 1f, 0.12f),
                Gravity = 0f, MaxParticles = 60, Shape = ParticleSystemShapeType.Cone, Angle = 14f, Radius = 0.28f, Rate = 35f, Loop = true,
                SizeGrowth = 2f, Stretch = 0.15f,
            });
            air.transform.localPosition = new Vector3(0f, 0f, 0.08f);

            var machine = AddMachine<WorkshopMachine>(root.gameObject);
            Wire(machine, ("rug", rug), ("orders", orders), ("body", root.gameObject), ("effect", air), ("spinner", blades),
                ("loopSource", Loop(root.gameObject, loop, 0.4f)));
            Configure(machine, MachineKind.Blower, "blower", "Souffleur de séchage");
            SetVector(machine, "spinAxis", Vector3.forward);
            SetFloat(machine, "spinSpeed", 1080f);
            return machine;
        }

        /// <summary>Plaque vibrante de dépoussiérage : pupitre près de l'aire, la plaque est sous l'aire (le tapis tremble).</summary>
        public static WorkshopMachine DustPlate(Transform parent, MeshStore m, RugWashContentBuilder.Content c, RugCleaningController rug,
            OrderManager orders, AudioClip loop, Vector3 position, float yaw, Vector3 rugCenter, Vector2 rugArea)
        {
            var root = Group("Machine_DustPlate", parent, position, Quaternion.Euler(0f, yaw, 0f));
            Pedestal(root, m, c, "DÉPOUSSIÉRAGE");
            var dust = CreateParticles("DustPlateDust", parent, c["Dust"], new ParticleConfig
            {
                Lifetime = new Vector2(0.8f, 1.4f), Speed = new Vector2(0.1f, 0.35f), Size = new Vector2(0.06f, 0.14f), Color = new Color(0.7f, 0.65f, 0.55f, 0.35f),
                Gravity = -0.02f, MaxParticles = 120, Shape = ParticleSystemShapeType.Box, Rate = 45f, Loop = true, SizeGrowth = 1.6f,
            });
            dust.transform.position = rugCenter + Vector3.up * 0.05f;
            dust.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var shape = dust.shape;
            shape.scale = new Vector3(rugArea.x, rugArea.y, 0.02f);
            var machine = AddMachine<WorkshopMachine>(root.gameObject);
            Wire(machine, ("rug", rug), ("orders", orders), ("body", root.gameObject), ("effect", dust), ("loopSource", Loop(root.gameObject, loop, 0.35f)));
            Configure(machine, MachineKind.DustPlate, "duster", "Plaque de dépoussiérage");
            SetFloat(machine, "cycleSeconds", 8f);
            return machine;
        }

        /// <summary>Rampe de pré-mouillage : tube à buses au-dessus de l'aire, suspendu au plafond ; pupitre à côté de l'aire.</summary>
        public static WorkshopMachine PreWet(Transform parent, MeshStore m, RugWashContentBuilder.Content c, RugCleaningController rug,
            OrderManager orders, AudioClip loop, Vector3 position, float yaw, Vector3 barCenter, float barLength, float ceiling)
        {
            var root = Group("Machine_PreWet", parent, position, Quaternion.Euler(0f, yaw, 0f));
            Pedestal(root, m, c, "PRÉ-MOUILLAGE");
            // La rampe fait partie de la machine (cachée avant l'achat), mais sans collider : elle ne gêne ni les joueurs ni le robot.
            var bar = new GameObject("Bar").transform;
            bar.SetParent(root, true);
            bar.position = barCenter;
            bar.rotation = Quaternion.identity;
            Part("Pipe", bar, m.Add("PreWet_Pipe", () => Cylinder(0.035f, barLength, 16)), c["Steel"], Vector3.zero, Quaternion.Euler(0f, 0f, 90f));
            for (int i = 0; i < 9; i++)
            {
                float x = -barLength * 0.45f + barLength * 0.9f * i / 8f;
                Part("Nozzle", bar, m.Add("PreWet_Nozzle", () => Cylinder(0.018f, 0.06f, 10)), c["BluePlastic"], new Vector3(x, -0.05f, 0f), Quaternion.identity);
            }
            float hang = ceiling - barCenter.y;
            foreach (float x in new[] { -barLength * 0.4f, barLength * 0.4f })
                Part("Hanger", bar, m.Add($"PreWet_Hanger_{hang:0.##}", () => Cylinder(0.012f, hang, 8)), c["DarkMetal"],
                    new Vector3(x, hang * 0.5f, 0f), Quaternion.identity);
            var spray = CreateParticles("PreWetSpray", bar, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.7f, 1f), Speed = new Vector2(0.5f, 1.2f), Size = new Vector2(0.015f, 0.03f), Color = new Color(0.8f, 0.88f, 1f, 0.75f),
                Gravity = 1f, MaxParticles = 260, Shape = ParticleSystemShapeType.Box, Rate = 160f, Loop = true, Stretch = 0.06f,
            });
            spray.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            spray.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var shape = spray.shape;
            shape.scale = new Vector3(barLength * 0.9f, 0.4f, 0.02f);
            var machine = AddMachine<WorkshopMachine>(root.gameObject);
            Wire(machine, ("rug", rug), ("orders", orders), ("body", root.gameObject), ("effect", spray), ("loopSource", Loop(root.gameObject, loop, 0.35f)));
            Configure(machine, MachineKind.PreWet, "prewet", "Rampe de pré-mouillage");
            SetFloat(machine, "cycleSeconds", 8f);
            return machine;
        }

        static void Configure(WorkshopMachine machine, MachineKind kind, string id, string displayName)
        {
            var serialized = new SerializedObject(machine);
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.FindProperty("machineId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetVector(Object target, string property, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
