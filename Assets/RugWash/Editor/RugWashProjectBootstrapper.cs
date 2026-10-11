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

            // Un niveau de qualité avec un autre pipeline écraserait le pipeline par défaut.
            int currentLevel = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var levelPipeline = QualitySettings.GetRenderPipelineAssetAt(i);
                if (levelPipeline == null || levelPipeline == pipeline)
                    continue;
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(currentLevel, false);

            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
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

            var serialized = new SerializedObject(pipeline);
            var additionalShadows = serialized.FindProperty("m_AdditionalLightShadowsSupported");
            if (additionalShadows != null && !additionalShadows.boolValue)
            {
                additionalShadows.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
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
