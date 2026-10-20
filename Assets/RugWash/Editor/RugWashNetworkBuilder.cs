using System.Collections.Generic;
using RugWash.Network;
using RugWash.Player;
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

            var parts = BuildAvatarVisuals(root.transform, c, meshes);
            var visuals = parts.Visuals;
            var head = parts.Head;
            var view = parts.View;
            var legLeft = parts.LegLeft;
            var legRight = parts.LegRight;
            var shoulderLeft = parts.ShoulderLeft;
            var shoulderRight = parts.ShoulderRight;
            var upperLeft = parts.UpperLeft;
            var upperRight = parts.UpperRight;
            var foreLeft = parts.ForeLeft;
            var foreRight = parts.ForeRight;
            var handRight = parts.HandRight;

            // Les huit outils, mêmes modèles détaillés que la vue à la première personne, portés devant les yeux.
            var definitions = new[] { c.Vacuum, c.Soap, c.Brush, c.Rinse, c.Extractor, c.FoamCannon, c.Broom, c.DetailBrush, c.Bucket, c.Squeegee, c.Rotary };
            var models = new[]
            {
                RugWashToolModels.Vacuum(view, meshes, c), RugWashToolModels.Soap(view, meshes, c), RugWashToolModels.Brush(view, meshes, c),
                RugWashToolModels.Rinse(view, meshes, c), RugWashToolModels.Extractor(view, meshes, c), RugWashToolModels.FoamCannon(view, meshes, c),
                RugWashToolModels.Broom(view, meshes, c), RugWashToolModels.DetailBrush(view, meshes, c), RugWashToolModels.Bucket(view, meshes, c),
                RugWashToolModels.Squeegee(view, meshes, c), RugWashToolModels.Rotary(view, meshes, c),
                // Gadgets de farce (v0.1.2) : emplacements 12 (batte) et 13 (fusil-jouet) ; 14 (pipi) : main libre.
                RugWashToolModels.Bat(view, meshes, c), RugWashToolModels.Blaster(view, meshes, c),
            };
            var effects = new[] { 2, 1, 2, 1, 2, 1, 2, 2, 1, 0, 2, 0, 0 };
            var gadgetRanges = new[] { c.Pranks.bat.range, c.Pranks.blaster.range };
            foreach (var model in models) DisableShadows(model.Root.gameObject);
            // Outils dorés (niveau maximal) : même finition que la vue à la première personne, état diffusé par l'hôte.
            for (int i = 0; i < definitions.Length; i++) RugWashToolModels.Gild(models[i], c);
            var appearance = AddAppearance(visuals, c);
            // Ragdoll : le corps bascule, jambes et bras articulés ; avant-bras rattachés aux bras pendant la chute.
            var ragdoll = visuals.gameObject.AddComponent<AvatarRagdoll>();
            Wire(ragdoll, ("body", visuals));
            WireList(ragdoll, "limbs", new[] { legLeft, legRight, upperLeft, upperRight });
            WireList(ragdoll, "attached", new[] { foreLeft, foreRight });

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

            // KO du fusil de farces : gerbe de confettis rouge cerise et blancs à la place de la tête (dessin animé, sans gore).
            var headPop = CreateParticles("HeadPop", fx, c["Debris"], new ParticleConfig
            {
                Lifetime = new Vector2(0.6f, 1.1f), Speed = Vector2.zero, Size = new Vector2(0.03f, 0.07f), Color = Color.white, Gravity = 1.2f,
                MaxParticles = 80, Shape = ParticleSystemShapeType.Sphere, Radius = 0.05f, Loop = true, PlayOnAwake = true, Rate = 0f,
            });
            var popMain = headPop.main;
            popMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.08f, 0.15f, 1f), new Color(1f, 0.97f, 0.9f, 1f));
            DisableShadows(headPop.gameObject);

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = true;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 1f;
            audio.maxDistance = 14f;
            audio.volume = 0.45f;

            // Chat vocal de proximité (grande mise à jour) : la voix de ce joueur sort de sa tête (source 3D).
            var mouth = new GameObject("Voice");
            mouth.transform.SetParent(head, false);
            mouth.AddComponent<AudioSource>();
            var voice = mouth.AddComponent<VoicePlayback>();
            var avatar = root.AddComponent<NetworkPlayerAvatar>();
            Wire(avatar, ("visuals", visuals.gameObject), ("head", head), ("view", view), ("nameLabel", label), ("liquidEffect", liquid),
                ("dustEffect", dust), ("toolAudio", audio), ("legLeft", legLeft), ("legRight", legRight), ("shoulderLeft", shoulderLeft),
                ("shoulderRight", shoulderRight), ("upperArmLeft", upperLeft), ("upperArmRight", upperRight), ("foreArmLeft", foreLeft),
                ("foreArmRight", foreRight), ("handRight", handRight), ("appearance", appearance), ("ragdoll", ragdoll), ("headPop", headPop),
                ("voice", voice));
            SetInt(avatar, "aimMask", ~((1 << RugWashPaths.PlayerLayer) | (1 << 2) | (1 << 5)));
            WireList(avatar, "skeletons", visuals.GetComponentsInChildren<AvatarSkeleton>(true));
            if (parts.UpperArmLength > 0f)
            {
                SetFloat(avatar, "upperArmLength", parts.UpperArmLength);
                SetFloat(avatar, "foreArmLength", parts.ForeArmLength);
                SetFloat(avatar, "maxArmStretch", 1.35f);
            }
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
                entry.FindPropertyRelative("range").floatValue = i < definitions.Length ? definitions[i].range : gadgetRanges[i - definitions.Length];
                entry.FindPropertyRelative("rightHand").objectReferenceValue = model.RightHand;
                entry.FindPropertyRelative("leftHand").objectReferenceValue = model.LeftHand;
                entry.FindPropertyRelative("effect").intValue = effects[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            WireList(avatar, "toolLoops", new[]
            {
                Clip(RugWashAudioGenerator.VacuumLoop), Clip(RugWashAudioGenerator.SoapLoop), Clip(RugWashAudioGenerator.BrushLoop),
                Clip(RugWashAudioGenerator.RinseLoop), Clip(RugWashAudioGenerator.ExtractorLoop), Clip(RugWashAudioGenerator.FoamCannonLoop),
                Clip(RugWashAudioGenerator.BroomLoop), Clip(RugWashAudioGenerator.BrushLoop), Clip(RugWashAudioGenerator.RinseLoop),
                Clip(RugWashAudioGenerator.SqueegeeLoop), Clip(RugWashAudioGenerator.RotaryLoop),
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

        /// <summary>Pièces de la silhouette (utilisées par la silhouette réseau et l'aperçu du menu « Personnage »).</summary>
        internal sealed class AvatarVisuals
        {
            public Transform Visuals, Head, View, LegLeft, LegRight, ShoulderLeft, ShoulderRight, UpperLeft, UpperRight, ForeLeft, ForeRight;
            public GameObject HandRight;
            /// <summary>V0.3.0 : longueurs des segments du bras du personnage à squelette (0 : silhouette procédurale).</summary>
            public float UpperArmLength, ForeArmLength;
        }

        /// <summary>Ouvriers modélisés dans Blender via le MCP (Art/Blender/Characters/Ouvrier.blend), même squelette.</summary>
        internal static readonly string[] SkinnedModels = { "Characters/Ouvrier_Salopette", "Characters/Ouvrier_Combinaison" };

        /// <summary>
        /// V0.3.0 : silhouette à squelette. Les pièces pilotes de la silhouette (jambes, épaules, bras, avant-bras, tête, point
        /// de vue) restent celles du jeu — même animation, même ragdoll, mêmes outils portés devant les yeux — mais n'ont plus
        /// de maillage : les deux ouvriers (salopette, combinaison) suivent leurs orientations (<see cref="AvatarSkeleton"/>),
        /// le modèle affiché dépendant de la tenue choisie. Épaules et longueurs des bras reprises du squelette, pour que les
        /// mains se referment toujours sur l'outil. Le gant droit est rangé dans « Hand_R » (masqué quand un outil est tenu,
        /// dont le modèle porte son propre gant).
        /// </summary>
        static AvatarVisuals BuildSkinnedVisuals(Transform visuals, RugWashContentBuilder.Content c)
        {
            Transform Empty(string name, Transform parent, Vector3 position)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = position;
                return t;
            }
            var legLeft = Empty("Leg_L", visuals, new Vector3(-0.1f, 0.9f, 0f));
            var legRight = Empty("Leg_R", visuals, new Vector3(0.1f, 0.9f, 0f));
            var head = Empty("Head", visuals, new Vector3(0f, 1.55f, 0f));
            var view = Empty("View", head, new Vector3(0f, 0.07f, 0f));
            var models = new GameObject[SkinnedModels.Length];
            for (int i = 0; i < models.Length; i++)
                models[i] = RugWashFbxModels.Place(SkinnedModels[i], visuals, c, Vector3.zero, Quaternion.identity, System.IO.Path.GetFileName(SkinnedModels[i]));
            Transform Bone(GameObject model, string name) =>
                RugWashFbxModels.FindDeep(model.transform, name) ?? throw new System.InvalidOperationException($"[RugWash] Os absent : {model.name}/{name}");
            Vector3 Local(Transform t) => visuals.InverseTransformPoint(t.position);
            var reference = models[0];
            var shoulderLeft = Empty("Shoulder_L", visuals, Local(Bone(reference, "LeftUpperArm")));
            var shoulderRight = Empty("Shoulder_R", visuals, Local(Bone(reference, "RightUpperArm")));
            // Segment pilote : Z le long de l'os, Y vers le pôle du coude (même règle que NetworkPlayerAvatar.SolveArm).
            Transform Segment(string name, string from, string to, float side, out float length)
            {
                Vector3 a = Local(Bone(reference, from)), b = Local(Bone(reference, to));
                length = (b - a).magnitude;
                Vector3 dir = (b - a).normalized;
                var segment = Empty(name, visuals, a);
                segment.localRotation = Quaternion.LookRotation(dir, Vector3.ProjectOnPlane(new Vector3(side, -0.8f, -0.3f), dir).normalized);
                return segment;
            }
            var upperLeft = Segment("UpperArm_L", "LeftUpperArm", "LeftLowerArm", -1f, out float upperLength);
            var upperRight = Segment("UpperArm_R", "RightUpperArm", "RightLowerArm", 1f, out _);
            var foreLeft = Segment("ForeArm_L", "LeftLowerArm", "LeftHand", -1f, out float foreLength);
            var foreRight = Segment("ForeArm_R", "RightLowerArm", "RightHand", 1f, out _);
            if (shoulderLeft.localPosition.x > 0f)
                throw new System.InvalidOperationException("[RugWash] Squelette inversé : l'épaule gauche doit être du côté -X.");

            // Axe local (unitaire, positif) de la longueur d'un os : celui qui pointe vers l'os suivant.
            Vector3 LengthAxis(Transform bone, Transform next)
            {
                Vector3 d = bone.InverseTransformPoint(next.position);
                Vector3 a = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
                return a.x >= a.y && a.x >= a.z ? Vector3.right : a.y >= a.z ? Vector3.up : Vector3.forward;
            }
            var hand = new GameObject("Hand_R");
            hand.transform.SetParent(visuals, false);
            foreach (var model in models)
            {
                var skeleton = model.AddComponent<AvatarSkeleton>();
                skeleton.AddLink(Bone(model, "LeftUpperLeg"), legLeft);
                skeleton.AddLink(Bone(model, "RightUpperLeg"), legRight);
                // Bras étirables sans trou entre manche et gant : avant-bras et main détachés de leur parent (aucune échelle
                // héritée), chaque segment s'allonge le long de son axe comme la pièce pilote.
                foreach (string side in new[] { "Left", "Right" })
                {
                    Bone(model, side + "LowerArm").SetParent(model.transform, true);
                    Bone(model, side + "Hand").SetParent(model.transform, true);
                }
                skeleton.AddStretchLink(Bone(model, "LeftUpperArm"), upperLeft, LengthAxis(Bone(model, "LeftUpperArm"), Bone(model, "LeftLowerArm")));
                skeleton.AddStretchLink(Bone(model, "RightUpperArm"), upperRight, LengthAxis(Bone(model, "RightUpperArm"), Bone(model, "RightLowerArm")));
                // Avant-bras : orienté et placé sur le coude ; main placée au poignet (bras étirés pour atteindre l'outil).
                skeleton.AddAnchor(Bone(model, "LeftLowerArm"), foreLeft, true, LengthAxis(Bone(model, "LeftLowerArm"), Bone(model, "LeftHand")));
                skeleton.AddAnchor(Bone(model, "RightLowerArm"), foreRight, true, LengthAxis(Bone(model, "RightLowerArm"), Bone(model, "RightHand")));
                skeleton.AddAnchor(Bone(model, "LeftHand"), foreLeft, true);
                skeleton.AddAnchor(Bone(model, "RightHand"), foreRight, true);
                // Tangage partagé : le cou en prend 40 %, la tête le reste (tête longue : moins de « cassure » au cou).
                skeleton.AddLink(Bone(model, "Neck"), head, 0.4f);
                skeleton.AddLink(Bone(model, "Head"), head);
                skeleton.AddKnee(Bone(model, "LeftLowerLeg"), legLeft, visuals.right);
                skeleton.AddKnee(Bone(model, "RightLowerLeg"), legRight, visuals.right);
                skeleton.SetHeadBone(Bone(model, "Head"));
                foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinned.updateWhenOffscreen = false;
                    if (skinned.name.EndsWith("_Glove_R")) skinned.transform.SetParent(hand.transform, true);
                }
            }
            return new AvatarVisuals
            {
                Visuals = visuals, Head = head, View = view, LegLeft = legLeft, LegRight = legRight, ShoulderLeft = shoulderLeft,
                ShoulderRight = shoulderRight, UpperLeft = upperLeft, UpperRight = upperRight, ForeLeft = foreLeft, ForeRight = foreRight,
                HandRight = hand, UpperArmLength = upperLength, ForeArmLength = foreLength,
            };
        }

        internal static AvatarVisuals BuildAvatarVisuals(Transform root, RugWashContentBuilder.Content c, MeshStore meshes)
        {
            // Silhouette procédurale (placeholder propre, pas un asset définitif) : tenue de travail, casquette, gants.
            // Proportions d'un adulte de 1,75 m ; les yeux (point de vue des outils) sont à 1,62 m, comme la caméra du joueur.
            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(root, false);
            if (System.Array.TrueForAll(SkinnedModels, RugWashFbxModels.Has)) return BuildSkinnedVisuals(visuals, c);
            // V0.3.0 : pièces modélisées dans Blender (Tools/Blender/models_character.py), mêmes pivots que les pièces procédurales.
            Mesh Model(string key, bool mirror = false) => RugWashModelLibrary.Mesh(meshes, "Character", key, mirror);
            bool modeled = RugWashModelLibrary.Has("Character", "Avatar_Face");

            Transform Leg(string name, float x)
            {
                var hip = new GameObject(name).transform;
                hip.SetParent(visuals, false);
                hip.localPosition = new Vector3(x, 0.9f, 0f);
                Part("Trouser", hip, Model("Avatar_Trouser", x < 0f) ?? meshes.Add("Avatar_Trouser", () => Lathe(new[]
                {
                    new Vector2(0.062f, -0.84f), new Vector2(0.066f, -0.45f), new Vector2(0.078f, -0.08f), new Vector2(0.07f, 0f),
                }, 14)), c["Workwear"], Vector3.zero);
                Part("Boot", hip, Model("Avatar_Boot") ?? meshes.Add("Avatar_Boot", () => RoundedBox(new Vector3(0.12f, 0.11f, 0.26f), 0.035f)), c["Boot"],
                    new Vector3(0f, -0.845f, 0.045f));
                var sole = Model("Avatar_BootSole");
                if (sole != null) Part("Boot_Sole", hip, sole, c["Rubber"], new Vector3(0f, -0.845f, 0.045f));
                return hip;
            }
            var legLeft = Leg("Leg_L", -0.1f);
            var legRight = Leg("Leg_R", 0.1f);

            Part("Pelvis", visuals, meshes.Add("Avatar_Pelvis", () => RoundedBox(new Vector3(0.34f, 0.17f, 0.22f), 0.06f)), c["Workwear"], new Vector3(0f, 0.93f, 0f));
            Part("Belt", visuals, meshes.Add("Avatar_Belt", () => RoundedBox(new Vector3(0.355f, 0.05f, 0.232f), 0.02f)), c["Rubber"], new Vector3(0f, 1.0f, 0f));
            Part("Torso", visuals, Model("Avatar_Torso") ?? meshes.Add("Avatar_Torso", () => RoundedBox(new Vector3(0.4f, 0.46f, 0.24f), 0.085f)), c["Shirt"], new Vector3(0f, 1.24f, 0f));
            Part("Bib_Front", visuals, Model("Avatar_BibFront") ?? meshes.Add("Avatar_Bib", () => RoundedBox(new Vector3(0.28f, 0.28f, 0.03f), 0.012f)), c["Workwear"], new Vector3(0f, 1.15f, 0.112f));
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
            Part("Face", head, Model("Avatar_Face") ?? meshes.Add("Avatar_Face", () => RoundedBox(new Vector3(0.19f, 0.23f, 0.21f), 0.085f)), c["Skin"], new Vector3(0f, 0.08f, 0f));
            if (modeled)
            {
                // Le visage modélisé porte oreilles, nez et orbites ; cheveux courts sous la casquette.
                Part("Hair", head, Model("Avatar_Hair"), c["Hair"], new Vector3(0f, 0.08f, 0f));
            }
            else
            {
                var ear = meshes.Add("Avatar_Ear", () => RoundedBox(new Vector3(0.028f, 0.055f, 0.04f), 0.012f));
                Part("Ear_L", head, ear, c["Skin"], new Vector3(-0.1f, 0.08f, 0f));
                Part("Ear_R", head, ear, c["Skin"], new Vector3(0.1f, 0.08f, 0f));
                Part("Nose", head, meshes.Add("Avatar_Nose", () => RoundedBox(new Vector3(0.028f, 0.04f, 0.03f), 0.012f)), c["Skin"], new Vector3(0f, 0.07f, 0.108f));
            }
            var eye = Model("Avatar_Eye") ?? meshes.Add("Avatar_Eye", () => RoundedBox(new Vector3(0.03f, 0.016f, 0.01f), 0.005f));
            float eyeX = modeled ? 0.038f : 0.042f, eyeZ = modeled ? 0.094f : 0.104f;
            Part("Eye_L", head, eye, c["RobotDark"], new Vector3(-eyeX, modeled ? 0.098f : 0.1f, eyeZ));
            Part("Eye_R", head, eye, c["RobotDark"], new Vector3(eyeX, modeled ? 0.098f : 0.1f, eyeZ));
            Part("Cap", head, Model("Avatar_Cap") ?? meshes.Add("Avatar_Cap", () => Lathe(new[]
            {
                new Vector2(0.112f, 0f), new Vector2(0.108f, 0.035f), new Vector2(0.09f, 0.07f), new Vector2(0.05f, 0.092f), new Vector2(0f, 0.098f),
            }, 18, capBottom: false)), c["CapFabric"], new Vector3(0f, 0.13f, 0f));
            var visor = Model("Avatar_Visor");
            if (visor != null) Part("Cap_Visor", head, visor, c["CapFabric"], new Vector3(0f, 0.13f, 0f));
            else Part("Cap_Visor", head, meshes.Add("Avatar_Visor", () => RoundedBox(new Vector3(0.16f, 0.012f, 0.1f), 0.005f)), c["CapFabric"],
                new Vector3(0f, 0.14f, 0.13f), new Vector3(-6f, 0f, 0f));
            var view = new GameObject("View").transform;
            view.SetParent(head, false);
            view.localPosition = new Vector3(0f, 0.07f, 0f);

            // Bras : épaule fixe, segments orientés chaque image (deux segments, coude calculé) ; gant propre à la silhouette.
            var upperMesh = meshes.Add("Avatar_UpperArm", () => Lathe(new[] { new Vector2(0.055f, 0f), new Vector2(0.047f, 0.3f) }, 12));
            var foreMesh = meshes.Add("Avatar_ForeArm", () => Lathe(new[] { new Vector2(0.046f, 0f), new Vector2(0.038f, 0.3f) }, 12));
            // V0.3.0 : poing ganté modélisé (Blender), comme les gants des outils tenus.
            var handMesh = RugWashModelLibrary.Mesh(meshes, "Glove", "Fist")
                           ?? meshes.Add("Avatar_Hand", () => RoundedBox(new Vector3(0.074f, 0.086f, 0.08f), 0.026f));
            var handMeshLeft = RugWashModelLibrary.Mesh(meshes, "Glove", "Fist", mirrorX: true) ?? handMesh;
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
            Part("Hand", foreLeft, handMeshLeft, c["Glove"], new Vector3(0f, 0f, 0.34f));
            var handRight = Part("Hand", foreRight, handMesh, c["Glove"], new Vector3(0f, 0f, 0.34f));
            return new AvatarVisuals
            {
                Visuals = visuals, Head = head, View = view, LegLeft = legLeft, LegRight = legRight, ShoulderLeft = shoulderLeft,
                ShoulderRight = shoulderRight, UpperLeft = upperLeft, UpperRight = upperRight, ForeLeft = foreLeft, ForeRight = foreRight,
                HandRight = handRight,
            };
        }

        /// <summary>
        /// Personnalisation (v0.1.2) : relie les rendus de la tenue, de la casquette et des gants (y compris ceux des outils
        /// tenus) au composant d'apparence, d'après leurs matériaux.
        /// </summary>
        internal static CharacterAppearance AddAppearance(Transform target, RugWashContentBuilder.Content c)
        {
            var appearance = target.gameObject.AddComponent<CharacterAppearance>();
            var outfit = new List<Renderer>();
            var cap = new List<Renderer>();
            var gloves = new List<Renderer>();
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial == c["Workwear"]) outfit.Add(renderer);
                else if (renderer.sharedMaterial == c["CapFabric"]) cap.Add(renderer);
                else if (renderer.sharedMaterial == c["Glove"]) gloves.Add(renderer);
            }
            // V0.3.0 : personnage à squelette — rendus repérés par leur nom (exportés depuis Blender par rôle), modèle par tenue.
            var capDetails = new List<Renderer>();
            var styleObjects = new List<GameObject>();
            var styleIndices = new List<int>();
            int StyleOf(string name) => name.Contains("Combinaison") ? CharacterPalette.Coverall : CharacterPalette.Overalls;
            foreach (var skinned in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string name = skinned.name;
                if (name.EndsWith("_Outfit")) outfit.Add(skinned);
                else if (name.EndsWith("_CapDetail")) capDetails.Add(skinned);
                else if (name.EndsWith("_Cap")) cap.Add(skinned);
                else if (name.Contains("_Glove_")) gloves.Add(skinned);
                if (name.EndsWith("_Glove_R"))
                {
                    styleObjects.Add(skinned.gameObject);
                    styleIndices.Add(StyleOf(name));
                }
            }
            foreach (var skeleton in target.GetComponentsInChildren<AvatarSkeleton>(true))
            {
                styleObjects.Add(skeleton.gameObject);
                styleIndices.Add(StyleOf(skeleton.name));
            }
            WireList(appearance, "outfitRenderers", outfit);
            WireList(appearance, "capRenderers", cap);
            WireList(appearance, "gloveRenderers", gloves);
            WireList(appearance, "capDetailRenderers", capDetails);
            WireList(appearance, "styleObjects", styleObjects);
            var serializedAppearance = new SerializedObject(appearance);
            var indices = serializedAppearance.FindProperty("styleIndices");
            indices.arraySize = styleIndices.Count;
            for (int i = 0; i < styleIndices.Count; i++) indices.GetArrayElementAtIndex(i).intValue = styleIndices[i];
            serializedAppearance.ApplyModifiedPropertiesWithoutUndo();
            // Modèle par défaut affiché dans le préfabriqué (l'apparence réelle est appliquée au lancement).
            for (int i = 0; i < styleObjects.Count; i++) styleObjects[i].SetActive(styleIndices[i] == CharacterPalette.Overalls);
            return appearance;
        }

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
