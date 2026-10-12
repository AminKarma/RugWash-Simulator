using System.Collections.Generic;
using RugWash.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>
    /// Contenu du multijoueur : réglages (NetworkContent) et préfabriqués réseau — l'atelier réseau et la silhouette d'un
    /// joueur (corps, tête, outil en main, nom, effets et son d'outil). Reconstruits à chaque génération.
    /// </summary>
    public static class RugWashNetworkBuilder
    {
        public const string Folder = RugWashPaths.Root + "/Prefabs/Network";
        public const string ContentPath = RugWashPaths.Root + "/ScriptableObjects/NetworkContent.asset";
        public const string WorkshopPrefabPath = Folder + "/NetworkWorkshop.prefab";
        public const string AvatarPrefabPath = Folder + "/PlayerAvatar.prefab";
        const string MeshPath = Folder + "/Network_GeneratedMeshes.asset";

        public static NetworkContent Ensure(RugWashContentBuilder.Content content)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(RugWashPaths.Root + "/Prefabs", "Network");
            var settings = AssetDatabase.LoadAssetAtPath<NetworkContent>(ContentPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<NetworkContent>();
                AssetDatabase.CreateAsset(settings, ContentPath);
            }
            settings.workshopPrefab = BuildWorkshopPrefab();
            settings.avatarPrefab = BuildAvatarPrefab(content);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        static GameObject BuildWorkshopPrefab()
        {
            var go = new GameObject("NetworkWorkshop");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkWorkshop>();
            return SaveNetworkPrefab(go, WorkshopPrefabPath);
        }

        static GameObject BuildAvatarPrefab(RugWashContentBuilder.Content c)
        {
            var meshes = new MeshStore(MeshPath);
            var root = new GameObject("PlayerAvatar");
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent<NetworkTransform>();
            sync.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            sync.SyncPositionX = sync.SyncPositionY = sync.SyncPositionZ = true;
            sync.SyncRotAngleX = false;
            sync.SyncRotAngleY = true;
            sync.SyncRotAngleZ = false;
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            sync.Interpolate = true;

            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(root.transform, false);
            Part("Torso", visuals, meshes.Add("Avatar_Torso", () => RoundedBox(new Vector3(0.44f, 0.62f, 0.27f), 0.08f)), c["BluePlastic"], new Vector3(0f, 1.12f, 0f));
            Part("Belt", visuals, meshes.Add("Avatar_Belt", () => RoundedBox(new Vector3(0.45f, 0.08f, 0.28f), 0.03f)), c["Rubber"], new Vector3(0f, 0.84f, 0f));
            var leg = meshes.Add("Avatar_Leg", () => Cylinder(0.085f, 0.8f, 14));
            Part("LegL", visuals, leg, c["DarkMetal"], new Vector3(-0.11f, 0.42f, 0f));
            Part("LegR", visuals, leg, c["DarkMetal"], new Vector3(0.11f, 0.42f, 0f));
            var arm = meshes.Add("Avatar_Arm", () => Cylinder(0.06f, 0.55f, 12));
            Part("ArmL", visuals, arm, c["BluePlastic"], new Vector3(-0.27f, 1.12f, 0.05f), new Vector3(20f, 0f, 0f));
            Part("ArmR", visuals, arm, c["BluePlastic"], new Vector3(0.27f, 1.08f, 0.14f), new Vector3(60f, 0f, 0f));

            var head = new GameObject("Head").transform;
            head.SetParent(visuals, false);
            head.localPosition = new Vector3(0f, 1.5f, 0f);
            Part("Skull", head, meshes.Add("Avatar_Head", () => RoundedBox(new Vector3(0.26f, 0.28f, 0.26f), 0.1f)), c["WhitePlastic"], new Vector3(0f, 0.12f, 0f));
            Part("Visor", head, meshes.Add("Avatar_Visor", () => RoundedBox(new Vector3(0.21f, 0.07f, 0.03f), 0.015f)), c["RobotDark"], new Vector3(0f, 0.14f, 0.13f));

            var hand = new GameObject("Hand").transform;
            hand.SetParent(visuals, false);
            hand.localPosition = new Vector3(0.27f, 0.95f, 0.36f);
            var tools = new List<GameObject>
            {
                Tool(hand, "Vacuum", (meshes.Add("Tool_Vacuum", () => Cylinder(0.09f, 0.32f, 16)), c["RedPlastic"], new Vector3(0f, 0f, 0.05f), new Vector3(90f, 0f, 0f)),
                    (meshes.Add("Tool_VacuumNozzle", () => Cylinder(0.03f, 0.4f, 10)), c["DarkMetal"], new Vector3(0f, -0.1f, 0.35f), new Vector3(120f, 0f, 0f))),
                Tool(hand, "Soap", (meshes.Add("Tool_SoapBottle", () => RoundedBox(new Vector3(0.09f, 0.22f, 0.09f), 0.03f)), c["WhitePlastic"], Vector3.zero, Vector3.zero),
                    (meshes.Add("Tool_SoapHead", () => RoundedBox(new Vector3(0.05f, 0.06f, 0.1f), 0.015f)), c["GreenPlastic"], new Vector3(0f, 0.13f, 0.04f), Vector3.zero)),
                Tool(hand, "Brush", (meshes.Add("Tool_Handle", () => Cylinder(0.018f, 0.75f, 8)), c["Wood"], new Vector3(0f, -0.05f, 0.3f), new Vector3(110f, 0f, 0f)),
                    (meshes.Add("Tool_BrushHead", () => RoundedBox(new Vector3(0.22f, 0.05f, 0.09f), 0.015f)), c["GreenPlastic"], new Vector3(0f, -0.19f, 0.66f), Vector3.zero)),
                Tool(hand, "Rinse", (meshes.Add("Tool_Lance", () => Cylinder(0.022f, 0.7f, 8)), c["BluePlastic"], new Vector3(0f, -0.04f, 0.3f), new Vector3(100f, 0f, 0f)),
                    (meshes.Add("Tool_Nozzle", () => Cylinder(0.03f, 0.06f, 10)), c["Chrome"], new Vector3(0f, -0.1f, 0.65f), new Vector3(100f, 0f, 0f))),
                Tool(hand, "Extractor", (meshes.Add("Tool_ExtractorBody", () => RoundedBox(new Vector3(0.16f, 0.16f, 0.3f), 0.04f)), c["SafetyYellow"], new Vector3(0f, 0f, 0.1f), Vector3.zero),
                    (meshes.Add("Tool_ExtractorWand", () => Cylinder(0.025f, 0.45f, 8)), c["Steel"], new Vector3(0f, -0.12f, 0.38f), new Vector3(120f, 0f, 0f))),
                Tool(hand, "FoamCannon", (meshes.Add("Tool_CannonBody", () => RoundedBox(new Vector3(0.1f, 0.12f, 0.32f), 0.03f)), c["BluePlastic"], new Vector3(0f, 0f, 0.1f), Vector3.zero),
                    (meshes.Add("Tool_CannonBottle", () => Cylinder(0.055f, 0.18f, 14)), c["WhitePlastic"], new Vector3(0f, -0.1f, 0.05f), Vector3.zero)),
                Tool(hand, "Broom", (meshes.Add("Tool_BroomHandle", () => Cylinder(0.018f, 1.1f, 8)), c["Wood"], new Vector3(0f, -0.2f, 0.32f), new Vector3(125f, 0f, 0f)),
                    (meshes.Add("Tool_BroomHead", () => RoundedBox(new Vector3(0.32f, 0.08f, 0.07f), 0.02f)), c["Bristle"], new Vector3(0f, -0.53f, 0.78f), Vector3.zero)),
                Tool(hand, "DetailBrush", (meshes.Add("Tool_DetailHandle", () => RoundedBox(new Vector3(0.04f, 0.04f, 0.2f), 0.012f)), c["Wood"], new Vector3(0f, 0f, 0.1f), Vector3.zero),
                    (meshes.Add("Tool_DetailHead", () => RoundedBox(new Vector3(0.07f, 0.04f, 0.07f), 0.012f)), c["Bristle"], new Vector3(0f, -0.03f, 0.22f), Vector3.zero)),
            };

            var labelGo = new GameObject("Name");
            labelGo.transform.SetParent(visuals, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font;
            label.fontSize = 64;
            label.characterSize = 0.022f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
            label.text = "Joueur";
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            var effects = new GameObject("Effects").transform;
            effects.SetParent(hand, false);
            effects.localPosition = new Vector3(0f, -0.15f, 0.6f);
            effects.localRotation = Quaternion.Euler(50f, 0f, 0f);
            var liquid = CreateParticles("Liquid", effects, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.45f), Speed = new Vector2(2.5f, 4f), Size = new Vector2(0.02f, 0.05f), Color = new Color(0.85f, 0.92f, 1f, 0.8f),
                Gravity = 0.6f, MaxParticles = 80, Shape = ParticleSystemShapeType.Cone, Angle = 9f, Radius = 0.01f, Loop = true, Rate = 70f, Stretch = 0.08f,
            });
            var dust = CreateParticles("Dust", effects, c["Dust"], new ParticleConfig
            {
                Lifetime = new Vector2(0.4f, 0.8f), Speed = new Vector2(0.2f, 0.6f), Size = new Vector2(0.05f, 0.12f), Color = new Color(0.65f, 0.58f, 0.48f, 0.5f),
                Gravity = -0.05f, MaxParticles = 40, Shape = ParticleSystemShapeType.Sphere, Radius = 0.08f, Loop = true, Rate = 25f, SizeGrowth = 1.8f,
            });

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = true;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 1f;
            audio.maxDistance = 14f;
            audio.volume = 0.45f;

            var avatar = root.AddComponent<NetworkPlayerAvatar>();
            Wire(avatar, ("visuals", visuals.gameObject), ("head", head), ("nameLabel", label), ("liquidEffect", liquid), ("dustEffect", dust),
                ("toolAudio", audio));
            WireList(avatar, "toolModels", tools);
            WireList(avatar, "toolLoops", new[]
            {
                Clip(RugWashAudioGenerator.VacuumLoop), Clip(RugWashAudioGenerator.SoapLoop), Clip(RugWashAudioGenerator.BrushLoop),
                Clip(RugWashAudioGenerator.RinseLoop), Clip(RugWashAudioGenerator.ExtractorLoop), Clip(RugWashAudioGenerator.FoamCannonLoop),
                Clip(RugWashAudioGenerator.BroomLoop), Clip(RugWashAudioGenerator.BrushLoop),
            });
            foreach (var tool in tools) tool.SetActive(false);
            DisableShadows(labelGo);

            return SaveNetworkPrefab(root, AvatarPrefabPath);
        }

        /// <summary>
        /// Enregistre un préfabriqué réseau puis calcule son identifiant global (GlobalObjectIdHash) comme le fait l'éditeur
        /// (NetworkObject.OnValidate, interne) : en génération automatique, il resterait à 0 et l'objet ne pourrait pas apparaître.
        /// </summary>
        static GameObject SaveNetworkPrefab(GameObject instance, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var validate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var networkObject in prefab.GetComponentsInChildren<NetworkObject>(true))
            {
                validate?.Invoke(networkObject, null);
                if (networkObject.PrefabIdHash == 0)
                    throw new System.InvalidOperationException($"[RugWash] Identifiant réseau non généré pour {path}.");
                EditorUtility.SetDirty(networkObject);
            }
            AssetDatabase.SaveAssets();
            return prefab;
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static GameObject Tool(Transform hand, string name, params (Mesh mesh, Material material, Vector3 position, Vector3 euler)[] parts)
        {
            var go = new GameObject($"Tool_{name}");
            go.transform.SetParent(hand, false);
            for (int i = 0; i < parts.Length; i++) Part($"{name}_{i}", go.transform, parts[i].mesh, parts[i].material, parts[i].position, parts[i].euler);
            return go;
        }
    }
}
