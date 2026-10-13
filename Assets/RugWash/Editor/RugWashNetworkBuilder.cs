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
            // Version imposée à chaque génération : une valeur sérialisée ancienne ne reste jamais en place.
            settings.protocolVersion = NetworkContent.CurrentProtocol;
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

            // Silhouette procédurale (placeholder propre, pas un asset définitif) : tenue de travail, casquette, gants.
            // Proportions d'un adulte de 1,75 m ; les yeux (point de vue des outils) sont à 1,62 m, comme la caméra du joueur.
            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(root.transform, false);

            Transform Leg(string name, float x)
            {
                var hip = new GameObject(name).transform;
                hip.SetParent(visuals, false);
                hip.localPosition = new Vector3(x, 0.9f, 0f);
                Part("Trouser", hip, meshes.Add("Avatar_Trouser", () => Lathe(new[]
                {
                    new Vector2(0.062f, -0.84f), new Vector2(0.066f, -0.45f), new Vector2(0.078f, -0.08f), new Vector2(0.07f, 0f),
                }, 14)), c["Workwear"], Vector3.zero);
                Part("Boot", hip, meshes.Add("Avatar_Boot", () => RoundedBox(new Vector3(0.12f, 0.11f, 0.26f), 0.035f)), c["Boot"],
                    new Vector3(0f, -0.845f, 0.045f));
                return hip;
            }
            var legLeft = Leg("Leg_L", -0.1f);
            var legRight = Leg("Leg_R", 0.1f);

            Part("Pelvis", visuals, meshes.Add("Avatar_Pelvis", () => RoundedBox(new Vector3(0.34f, 0.17f, 0.22f), 0.06f)), c["Workwear"], new Vector3(0f, 0.93f, 0f));
            Part("Belt", visuals, meshes.Add("Avatar_Belt", () => RoundedBox(new Vector3(0.355f, 0.05f, 0.232f), 0.02f)), c["Rubber"], new Vector3(0f, 1.0f, 0f));
            Part("Torso", visuals, meshes.Add("Avatar_Torso", () => RoundedBox(new Vector3(0.4f, 0.46f, 0.24f), 0.085f)), c["Shirt"], new Vector3(0f, 1.24f, 0f));
            Part("Bib_Front", visuals, meshes.Add("Avatar_Bib", () => RoundedBox(new Vector3(0.28f, 0.28f, 0.03f), 0.012f)), c["Workwear"], new Vector3(0f, 1.15f, 0.112f));
            Part("Bib_Back", visuals, meshes.Add("Avatar_Bib", () => RoundedBox(new Vector3(0.28f, 0.28f, 0.03f), 0.012f)), c["Workwear"], new Vector3(0f, 1.15f, -0.112f));
            var strap = meshes.Add("Avatar_Strap", () => RoundedBox(new Vector3(0.045f, 0.2f, 0.02f), 0.008f));
            Part("Strap_FL", visuals, strap, c["Workwear"], new Vector3(-0.1f, 1.36f, 0.115f));
            Part("Strap_FR", visuals, strap, c["Workwear"], new Vector3(0.1f, 1.36f, 0.115f));
            Part("Strap_BL", visuals, strap, c["Workwear"], new Vector3(-0.1f, 1.36f, -0.115f));
            Part("Strap_BR", visuals, strap, c["Workwear"], new Vector3(0.1f, 1.36f, -0.115f));
            Part("Reflective", visuals, meshes.Add("Avatar_Reflective", () => RoundedBox(new Vector3(0.36f, 0.025f, 0.236f), 0.01f)), c["Reflective"], new Vector3(0f, 1.07f, 0f));
            Part("Neck", visuals, meshes.Add("Avatar_Neck", () => Cylinder(0.05f, 0.08f, 14)), c["Skin"], new Vector3(0f, 1.5f, 0f));

            var head = new GameObject("Head").transform;
            head.SetParent(visuals, false);
            head.localPosition = new Vector3(0f, 1.55f, 0f);
            Part("Face", head, meshes.Add("Avatar_Face", () => RoundedBox(new Vector3(0.19f, 0.23f, 0.21f), 0.085f)), c["Skin"], new Vector3(0f, 0.08f, 0f));
            var ear = meshes.Add("Avatar_Ear", () => RoundedBox(new Vector3(0.028f, 0.055f, 0.04f), 0.012f));
            Part("Ear_L", head, ear, c["Skin"], new Vector3(-0.1f, 0.08f, 0f));
            Part("Ear_R", head, ear, c["Skin"], new Vector3(0.1f, 0.08f, 0f));
            Part("Nose", head, meshes.Add("Avatar_Nose", () => RoundedBox(new Vector3(0.028f, 0.04f, 0.03f), 0.012f)), c["Skin"], new Vector3(0f, 0.07f, 0.108f));
            var eye = meshes.Add("Avatar_Eye", () => RoundedBox(new Vector3(0.03f, 0.016f, 0.01f), 0.005f));
            Part("Eye_L", head, eye, c["RobotDark"], new Vector3(-0.042f, 0.1f, 0.104f));
            Part("Eye_R", head, eye, c["RobotDark"], new Vector3(0.042f, 0.1f, 0.104f));
            Part("Cap", head, meshes.Add("Avatar_Cap", () => Lathe(new[]
            {
                new Vector2(0.112f, 0f), new Vector2(0.108f, 0.035f), new Vector2(0.09f, 0.07f), new Vector2(0.05f, 0.092f), new Vector2(0f, 0.098f),
            }, 18, capBottom: false)), c["CapFabric"], new Vector3(0f, 0.13f, 0f));
            Part("Cap_Visor", head, meshes.Add("Avatar_Visor", () => RoundedBox(new Vector3(0.16f, 0.012f, 0.1f), 0.005f)), c["CapFabric"],
                new Vector3(0f, 0.14f, 0.13f), new Vector3(-6f, 0f, 0f));
            var view = new GameObject("View").transform;
            view.SetParent(head, false);
            view.localPosition = new Vector3(0f, 0.07f, 0f);

            // Bras : épaule fixe, segments orientés chaque image (deux segments, coude calculé) ; gant propre à la silhouette.
            var upperMesh = meshes.Add("Avatar_UpperArm", () => Lathe(new[] { new Vector2(0.055f, 0f), new Vector2(0.047f, 0.3f) }, 12));
            var foreMesh = meshes.Add("Avatar_ForeArm", () => Lathe(new[] { new Vector2(0.046f, 0f), new Vector2(0.038f, 0.3f) }, 12));
            var handMesh = meshes.Add("Avatar_Hand", () => RoundedBox(new Vector3(0.074f, 0.086f, 0.08f), 0.026f));
            Transform Empty(string name, Transform parent, Vector3 position)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = position;
                return t;
            }
            Transform Segment(string name, Mesh mesh, Material material)
            {
                var segment = Empty(name, visuals, Vector3.zero);
                Part("Mesh", segment, mesh, material, Vector3.zero, new Vector3(90f, 0f, 0f));
                return segment;
            }
            var shoulderLeft = Empty("Shoulder_L", visuals, new Vector3(-0.235f, 1.4f, 0f));
            var shoulderRight = Empty("Shoulder_R", visuals, new Vector3(0.235f, 1.4f, 0f));
            var upperLeft = Segment("UpperArm_L", upperMesh, c["Shirt"]);
            var upperRight = Segment("UpperArm_R", upperMesh, c["Shirt"]);
            var foreLeft = Segment("ForeArm_L", foreMesh, c["Shirt"]);
            var foreRight = Segment("ForeArm_R", foreMesh, c["Shirt"]);
            upperLeft.localPosition = shoulderLeft.localPosition;
            upperRight.localPosition = shoulderRight.localPosition;
            upperLeft.localRotation = upperRight.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreLeft.localPosition = shoulderLeft.localPosition + Vector3.down * 0.3f;
            foreRight.localPosition = shoulderRight.localPosition + Vector3.down * 0.3f;
            foreLeft.localRotation = foreRight.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part("Hand", foreLeft, handMesh, c["Glove"], new Vector3(0f, 0f, 0.34f));
            var handRight = Part("Hand", foreRight, handMesh, c["Glove"], new Vector3(0f, 0f, 0.34f));

            // Les huit outils, mêmes modèles détaillés que la vue à la première personne, portés devant les yeux.
            var definitions = new[] { c.Vacuum, c.Soap, c.Brush, c.Rinse, c.Extractor, c.FoamCannon, c.Broom, c.DetailBrush };
            var models = new[]
            {
                RugWashToolModels.Vacuum(view, meshes, c), RugWashToolModels.Soap(view, meshes, c), RugWashToolModels.Brush(view, meshes, c),
                RugWashToolModels.Rinse(view, meshes, c), RugWashToolModels.Extractor(view, meshes, c), RugWashToolModels.FoamCannon(view, meshes, c),
                RugWashToolModels.Broom(view, meshes, c), RugWashToolModels.DetailBrush(view, meshes, c),
            };
            var effects = new[] { 2, 1, 2, 1, 2, 1, 2, 2 };
            foreach (var model in models) DisableShadows(model.Root.gameObject);

            var labelGo = new GameObject("Name");
            labelGo.transform.SetParent(visuals, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.98f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font;
            label.fontSize = 64;
            label.characterSize = 0.02f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
            label.text = "Joueur";
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            // Effets d'outil : repositionnés chaque image sur la bouche ou la tête de l'outil affiché.
            var fx = new GameObject("Effects").transform;
            fx.SetParent(visuals, false);
            var liquid = CreateParticles("Liquid", fx, c["Droplet"], new ParticleConfig
            {
                Lifetime = new Vector2(0.25f, 0.45f), Speed = new Vector2(2.5f, 4f), Size = new Vector2(0.02f, 0.05f), Color = new Color(0.85f, 0.92f, 1f, 0.8f),
                Gravity = 0.6f, MaxParticles = 80, Shape = ParticleSystemShapeType.Cone, Angle = 9f, Radius = 0.01f, Loop = true, Rate = 70f, Stretch = 0.08f,
            });
            var dust = CreateParticles("Dust", fx, c["Dust"], new ParticleConfig
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
            Wire(avatar, ("visuals", visuals.gameObject), ("head", head), ("view", view), ("nameLabel", label), ("liquidEffect", liquid),
                ("dustEffect", dust), ("toolAudio", audio), ("legLeft", legLeft), ("legRight", legRight), ("shoulderLeft", shoulderLeft),
                ("shoulderRight", shoulderRight), ("upperArmLeft", upperLeft), ("upperArmRight", upperRight), ("foreArmLeft", foreLeft),
                ("foreArmRight", foreRight), ("handRight", handRight));
            SetInt(avatar, "aimMask", ~((1 << RugWashPaths.PlayerLayer) | (1 << 2) | (1 << 5)));
            var serialized = new SerializedObject(avatar);
            var list = serialized.FindProperty("tools");
            list.arraySize = models.Length;
            for (int i = 0; i < models.Length; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                var model = models[i];
                entry.FindPropertyRelative("root").objectReferenceValue = model.Root.gameObject;
                entry.FindPropertyRelative("tip").objectReferenceValue = model.Tip;
                entry.FindPropertyRelative("head").objectReferenceValue = model.Head;
                entry.FindPropertyRelative("shaft").objectReferenceValue = model.Shaft;
                entry.FindPropertyRelative("shaftLength").floatValue = model.ShaftLength;
                entry.FindPropertyRelative("range").floatValue = definitions[i].range;
                entry.FindPropertyRelative("rightHand").objectReferenceValue = model.RightHand;
                entry.FindPropertyRelative("leftHand").objectReferenceValue = model.LeftHand;
                entry.FindPropertyRelative("effect").intValue = effects[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            WireList(avatar, "toolLoops", new[]
            {
                Clip(RugWashAudioGenerator.VacuumLoop), Clip(RugWashAudioGenerator.SoapLoop), Clip(RugWashAudioGenerator.BrushLoop),
                Clip(RugWashAudioGenerator.RinseLoop), Clip(RugWashAudioGenerator.ExtractorLoop), Clip(RugWashAudioGenerator.FoamCannonLoop),
                Clip(RugWashAudioGenerator.BroomLoop), Clip(RugWashAudioGenerator.BrushLoop),
            });
            foreach (var model in models) model.Root.gameObject.SetActive(false);
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

    }
}
