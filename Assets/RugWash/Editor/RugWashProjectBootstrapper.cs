using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RugWash.Editor
{
    /// <summary>Chemins canoniques du projet RugWash (source unique pour l'outillage et les tests).</summary>
    public static class RugWashPaths
    {
        public const string Root = "Assets/RugWash";
        public const string Materials = Root + "/Materials";
        public const string Scenes = Root + "/Scenes";
        public const string Shaders = Root + "/Shaders";
        public const string GeneratedTextures = Root + "/Textures/Generated";
        public const string RenderingSettings = Root + "/Settings/Rendering";
        public const string RugDefinitions = Root + "/ScriptableObjects/Rugs";
        public const string ToolDefinitions = Root + "/ScriptableObjects/Tools";
        public const string DirtTypeDefinitions = Root + "/ScriptableObjects/DirtTypes";
        public const string OrderDefinitions = Root + "/ScriptableObjects/Orders";
        public const string UpgradeDefinitions = Root + "/ScriptableObjects/Upgrades";

        public const string MainMenuScene = Scenes + "/MainMenu.unity";
        public const string WorkshopScene = Scenes + "/Workshop.unity";

        public const string UrpAsset = RenderingSettings + "/RugWash_URP.asset";
        public const string UrpRendererData = RenderingSettings + "/RugWash_URP_Renderer.asset";

        /// <summary>Pipeline d'un palier de qualité (Low, Medium, High) ; Ultra = <see cref="UrpAsset"/>.</summary>
        public static string UrpTierAsset(string tier) => $"{RenderingSettings}/RugWash_URP_{tier}.asset";
        public static string UrpTierRendererData(string tier) => $"{RenderingSettings}/RugWash_URP_Renderer_{tier}.asset";
        public const string WorkshopVolumeProfile = RenderingSettings + "/Workshop_VolumeProfile.asset";

        /// <summary>Calques physiques/rendu réservés par le projet.</summary>
        public const int RugLayer = 8;
        public const string RugLayerName = "Rug";
        public const int PlayerLayer = 9;
        public const string PlayerLayerName = "Player";

        /// <summary>Ordre parent → enfant, requis par AssetDatabase.CreateFolder.</summary>
        public static readonly string[] Folders =
        {
            Root,
            Root + "/Audio",
            Root + "/Audio/Generated",
            Root + "/Documentation",
            Root + "/Editor",
            Root + "/Materials",
            Root + "/Prefabs",
            Root + "/Scenes",
            Root + "/ScriptableObjects",
            Root + "/ScriptableObjects/Rugs",
            Root + "/ScriptableObjects/Tools",
            Root + "/ScriptableObjects/DirtTypes",
            Root + "/ScriptableObjects/Orders",
            Root + "/ScriptableObjects/Upgrades",
            Root + "/Scripts",
            Root + "/Scripts/Audio",
            Root + "/Scripts/Cleaning",
            Root + "/Scripts/Core",
            Root + "/Scripts/Diagnostics",
            Root + "/Scripts/Economy",
            Root + "/Scripts/Orders",
            Root + "/Scripts/Effects",
            Root + "/Scripts/Player",
            Root + "/Scripts/Save",
            Root + "/Scripts/UI",
            Root + "/Settings",
            Root + "/Settings/Rendering",
            Root + "/Shaders",
            Root + "/Tests",
            Root + "/Tests/Editor",
            Root + "/Tests/PlayMode",
            Root + "/Textures",
            Root + "/Textures/Generated",
        };

        /// <summary>Ordre des scènes dans les Build Settings.</summary>
        /// <summary>Les résultats s'affichent dans l'atelier (fenêtre) : pas de scène dédiée.</summary>
        public static readonly string[] BuildScenes = { MainMenuScene, WorkshopScene };
    }

    public static class RugWashProjectBootstrapper
    {
        /// <summary>0 = Input Manager (legacy), 1 = Input System Package, 2 = Both.</summary>
        public const int InputHandlerInputSystemOnly = 1;

        static readonly GraphicsDeviceType[] WindowsGraphicsApis =
        {
            GraphicsDeviceType.Direct3D11,
            GraphicsDeviceType.Direct3D12,
            GraphicsDeviceType.Vulkan,
        };

        [MenuItem("RugWash/Setup/Initialiser le Projet et Dossiers", priority = 0)]
        static void InitializeFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            bool restartRequired = Initialize();

            if (restartRequired && EditorUtility.DisplayDialog("RugWash",
                    "Le backend d'entrée a été basculé sur l'Input System Package.\n" +
                    "Unity doit redémarrer pour appliquer ce changement.", "Redémarrer maintenant", "Plus tard"))
            {
                EditorApplication.OpenProject(Directory.GetCurrentDirectory());
                return;
            }

            EditorUtility.DisplayDialog("RugWash", "Projet initialisé : dossiers, Player Settings, URP et Build Settings.", "OK");
        }

        /// <summary>Idempotent. Retourne true si un redémarrage de l'éditeur est requis.</summary>
        public static bool Initialize()
        {
            EnsureFolders();
            EnsureLayers();
            bool restartRequired = ConfigurePlayerSettings();
            EnsureUniversalRenderPipeline();
            SyncBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[RugWash] Initialisation du projet terminée." +
                      (restartRequired ? " Redémarrage requis (backend d'entrée modifié)." : ""));
            return restartRequired;
        }

        public static void EnsureFolders()
        {
            foreach (string folder in RugWashPaths.Folders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                    continue;
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        /// <summary>Réserve les calques Rug et Player (TagManager).</summary>
        public static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            bool changed = false;
            changed |= SetLayer(layers, RugWashPaths.RugLayer, RugWashPaths.RugLayerName);
            changed |= SetLayer(layers, RugWashPaths.PlayerLayer, RugWashPaths.PlayerLayerName);
            if (changed) tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool SetLayer(SerializedProperty layers, int index, string layerName)
        {
            var element = layers.GetArrayElementAtIndex(index);
            if (element.stringValue == layerName) return false;
            if (!string.IsNullOrEmpty(element.stringValue))
                Debug.LogWarning($"[RugWash] Le calque {index} '{element.stringValue}' est remplacé par '{layerName}'.");
            element.stringValue = layerName;
            return true;
        }

        /// <summary>Retourne true si le backend d'entrée a changé (redémarrage requis).</summary>
        public static bool ConfigurePlayerSettings()
        {
            if (PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "RugWash Studio";
            PlayerSettings.productName = "RugWash Simulator";

            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, WindowsGraphicsApis);

            return EnsureActiveInputHandler(InputHandlerInputSystemOnly);
        }

        public static int GetActiveInputHandler()
        {
            var property = GetPlayerSettingsObject().FindProperty("activeInputHandler");
            return property != null ? property.intValue : -1;
        }

        static bool EnsureActiveInputHandler(int value)
        {
            var playerSettings = GetPlayerSettingsObject();
            var property = playerSettings.FindProperty("activeInputHandler");
            if (property == null)
            {
                Debug.LogWarning("[RugWash] Propriété 'activeInputHandler' introuvable : réglez Player > Active Input Handling manuellement.");
                return false;
            }
            if (property.intValue == value)
                return false;

            property.intValue = value;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static SerializedObject GetPlayerSettingsObject() =>
            new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));

        public static UniversalRenderPipelineAsset EnsureUniversalRenderPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RugWashPaths.UrpAsset);
            if (pipeline == null)
            {
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RugWashPaths.UrpRendererData);
                if (rendererData == null)
                {
                    rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, RugWashPaths.UrpRendererData);
                }
                EnsurePostProcessData(rendererData);

                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.supportsHDR = true;
                pipeline.msaaSampleCount = 4;
                pipeline.renderScale = 1f;
                pipeline.shadowDistance = 40f;
                AssetDatabase.CreateAsset(pipeline, RugWashPaths.UrpAsset);
            }

            ConfigurePipeline(pipeline);

            if (GraphicsSettings.defaultRenderPipeline != pipeline)
                GraphicsSettings.defaultRenderPipeline = pipeline;

            EnsureQualityTiers(pipeline);

            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            return pipeline;
        }

        /// <summary>
        /// Occlusion ambiante d'un palier : absente ; à mi-résolution avec peu d'échantillons et un flou simple ; à mi-résolution
        /// avec la qualité d'Ultra ; complète (Ultra).
        /// </summary>
        public enum AmbientOcclusionTier { Off, HalfLow, HalfMedium, Full }

        /// <summary>Réglages d'un palier de rendu (valeurs fixées ici, appliquées à chaque initialisation).</summary>
        public readonly struct QualityTier
        {
            public readonly string Name;
            public readonly int Msaa;
            public readonly float RenderScale;
            public readonly bool SpotShadows;
            public readonly int ShadowResolution;
            public readonly AmbientOcclusionTier AmbientOcclusion;

            public QualityTier(string name, int msaa, float renderScale, bool spotShadows, int shadowResolution, AmbientOcclusionTier ambientOcclusion)
            {
                Name = name;
                Msaa = msaa;
                RenderScale = renderScale;
                SpotShadows = spotShadows;
                ShadowResolution = shadowResolution;
                AmbientOcclusion = ambientOcclusion;
            }
        }

        /// <summary>
        /// Paliers de rendu (mission de corrections, 2.11). Avant, les six niveaux de qualité utilisaient le même pipeline
        /// (MSAA 4×, SSAO pleine résolution, ombres 2048) : choisir « Moyen » ne réduisait presque rien. Les réglages les plus
        /// coûteux ont été mesurés un par un (benchmark « graphics ») ; Ultra reste le pipeline d'origine, inchangé.
        /// Mesure (charge GPU ×4) : un palier Élevé à occlusion complète et MSAA 2× coûtait autant qu'Ultra ; il passe donc
        /// à l'occlusion à mi-résolution (qualité d'Ultra conservée).
        /// </summary>
        public static readonly QualityTier[] QualityTiers =
        {
            new QualityTier("Low", 1, 0.85f, false, 1024, AmbientOcclusionTier.Off),
            new QualityTier("Medium", 1, 1f, true, 1024, AmbientOcclusionTier.HalfLow),
            new QualityTier("High", 2, 1f, true, 2048, AmbientOcclusionTier.HalfMedium),
        };

        /// <summary>Palier de chaque niveau de qualité (Very Low, Low, Medium, High, Very High, Ultra) ; -1 = Ultra.</summary>
        public static readonly int[] TierOfLevel = { 0, 0, 1, 2, -1, -1 };

        /// <summary>Crée (copie d'Ultra) et règle les pipelines des paliers, puis les assigne aux niveaux de qualité.</summary>
        static void EnsureQualityTiers(UniversalRenderPipelineAsset ultra)
        {
            var tiers = new UniversalRenderPipelineAsset[QualityTiers.Length];
            for (int i = 0; i < QualityTiers.Length; i++) tiers[i] = EnsureTier(QualityTiers[i]);

            int currentLevel = QualitySettings.GetQualityLevel();
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                int tier = level < TierOfLevel.Length ? TierOfLevel[level] : -1;
                var wanted = tier >= 0 && tiers[tier] != null ? tiers[tier] : ultra;
                if (QualitySettings.GetRenderPipelineAssetAt(level) == wanted) continue;
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = wanted;
            }
            QualitySettings.SetQualityLevel(currentLevel, false);
        }

        static UniversalRenderPipelineAsset EnsureTier(QualityTier tier)
        {
            string rendererPath = RugWashPaths.UrpTierRendererData(tier.Name), pipelinePath = RugWashPaths.UrpTierAsset(tier.Name);
            if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath) == null && !AssetDatabase.CopyAsset(RugWashPaths.UrpRendererData, rendererPath))
                return null;
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath) == null && !AssetDatabase.CopyAsset(RugWashPaths.UrpAsset, pipelinePath))
                return null;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);

            foreach (var feature in renderer.rendererFeatures)
            {
                if (!(feature is ScreenSpaceAmbientOcclusion ssao)) continue;
                var serializedFeature = new SerializedObject(ssao);
                serializedFeature.FindProperty("m_Active").boolValue = tier.AmbientOcclusion != AmbientOcclusionTier.Off;
                bool half = tier.AmbientOcclusion == AmbientOcclusionTier.HalfLow || tier.AmbientOcclusion == AmbientOcclusionTier.HalfMedium;
                serializedFeature.FindProperty("m_Settings.Downsample").boolValue = half;
                // Échantillons et flou : 0 élevé, 1 moyen, 2 bas (énumérations de l'URP) ; Ultra : échantillons moyens, flou élevé.
                bool low = tier.AmbientOcclusion == AmbientOcclusionTier.HalfLow;
                serializedFeature.FindProperty("m_Settings.Samples").intValue = low ? 2 : 1;
                serializedFeature.FindProperty("m_Settings.BlurQuality").intValue = low ? 2 : 0;
                serializedFeature.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(renderer);

            var serialized = new SerializedObject(pipeline);
            var renderers = serialized.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.FindProperty("m_MSAA").intValue = tier.Msaa;
            serialized.FindProperty("m_RenderScale").floatValue = tier.RenderScale;
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = tier.SpotShadows;
            serialized.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = tier.ShadowResolution;
            serialized.FindProperty("m_MainLightShadowmapResolution").intValue = tier.ShadowResolution;
            // Résolution d'une ombre de projecteur (palier « élevé » de ses lumières) : la moitié de l'atlas, comme Ultra.
            serialized.FindProperty("m_AdditionalLightsShadowResolutionTierHigh").intValue = Mathf.Min(1024, tier.ShadowResolution / 2);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        /// <summary>Forward+ (pas de limite de lumières par objet) et ombres des lumières additionnelles.</summary>
        static void ConfigurePipeline(UniversalRenderPipelineAsset pipeline)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RugWashPaths.UrpRendererData);
            if (rendererData != null && rendererData.renderingMode != RenderingMode.ForwardPlus)
            {
                rendererData.renderingMode = RenderingMode.ForwardPlus;
                EditorUtility.SetDirty(rendererData);
            }

            if (rendererData != null) EnsureAmbientOcclusion(rendererData);

            var serialized = new SerializedObject(pipeline);
            var additionalShadows = serialized.FindProperty("m_AdditionalLightShadowsSupported");
            if (additionalShadows != null && !additionalShadows.boolValue)
            {
                additionalShadows.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>Occlusion ambiante en espace écran (SSAO) : contact des objets avec le sol et les murs.</summary>
        static void EnsureAmbientOcclusion(UniversalRendererData rendererData)
        {
            if (rendererData.rendererFeatures.Exists(f => f is ScreenSpaceAmbientOcclusion)) return;
            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "ScreenSpaceAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(ssao, rendererData);
            rendererData.rendererFeatures.Add(ssao);

            // Table des identifiants attendue par l'éditeur de renderer (sinon la fonctionnalité est signalée invalide).
            var serialized = new SerializedObject(rendererData);
            var map = serialized.FindProperty("m_RendererFeatureMap");
            if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId))
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }

        static void EnsurePostProcessData(UniversalRendererData rendererData)
        {
            var serialized = new SerializedObject(rendererData);
            var property = serialized.FindProperty("postProcessData");
            if (property == null || property.objectReferenceValue != null)
                return;

            const string defaultPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
            var postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(defaultPath);
            if (postProcessData == null)
            {
                Debug.LogWarning($"[RugWash] PostProcessData introuvable ({defaultPath}) : assignez-le sur {RugWashPaths.UrpRendererData}.");
                return;
            }
            property.objectReferenceValue = postProcessData;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Place MainMenu → Workshop en tête des Build Settings (scènes existantes uniquement).</summary>
        public static void SyncBuildScenes()
        {
            var ordered = RugWashPaths.BuildScenes.Where(File.Exists).ToList();
            var scenes = ordered.Select(path => new EditorBuildSettingsScene(path, true)).ToList();
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => !ordered.Contains(s.path) && File.Exists(s.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
