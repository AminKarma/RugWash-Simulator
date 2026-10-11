using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RugWash.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace RugWash.Tests
{
    /// <summary>Tests de fumée : le projet compile, les packages clés sont chargés et l'environnement est configuré.</summary>
    public class ProjectHealthTest
    {
        static readonly string[] RequiredPackages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.ugui",
            "com.unity.test-framework",
            "com.unity.mathematics",
        };

        static IEnumerable<TestCaseData> PackageAssemblies()
        {
            yield return new TestCaseData(typeof(UniversalRenderPipelineAsset), "com.unity.render-pipelines.universal").SetName("Loaded_URP");
            yield return new TestCaseData(typeof(UnityEngine.InputSystem.InputSystem), "com.unity.inputsystem").SetName("Loaded_InputSystem");
            yield return new TestCaseData(typeof(UnityEngine.UI.Button), "com.unity.ugui").SetName("Loaded_uGUI");
            yield return new TestCaseData(typeof(UnityEngine.TestTools.LogAssert), "com.unity.test-framework").SetName("Loaded_TestFramework");
        }

        [Test]
        public void Scripts_CompileWithoutErrors()
        {
            Assert.IsFalse(EditorUtility.scriptCompilationFailed, "Des scripts du projet ne compilent pas.");
            Assert.IsNotNull(typeof(RugWash.Core.GameBootstrap).Assembly, "RugWash.Runtime n'est pas chargé.");
        }

        /// <summary>
        /// Depuis Unity 6.5, Unity.Mathematics est fourni par le module moteur UnityEngine.MathematicsModule ;
        /// on vérifie donc que le type est disponible plutôt que son package d'origine.
        /// </summary>
        [Test]
        public void Mathematics_IsAvailable()
        {
            var value = new Unity.Mathematics.float3(1f, 2f, 3f);
            Assert.AreEqual(6f, Unity.Mathematics.math.csum(value));
        }

        [TestCaseSource(nameof(RequiredPackages))]
        public void RequiredPackage_IsDeclaredInManifest(string packageId)
        {
            StringAssert.Contains($"\"{packageId}\"", File.ReadAllText("Packages/manifest.json"));
        }

        [TestCaseSource(nameof(PackageAssemblies))]
        public void RequiredPackage_IsLoaded(Type typeFromPackage, string packageId)
        {
            var info = PackageInfo.FindForAssembly(typeFromPackage.Assembly);
            Assert.IsNotNull(info, $"{packageId} : assembly {typeFromPackage.Assembly.GetName().Name} hors package.");
            Assert.AreEqual(packageId, info.name);
        }

        [Test]
        public void FolderStructure_Exists()
        {
            foreach (string folder in RugWashPaths.Folders)
                Assert.IsTrue(AssetDatabase.IsValidFolder(folder), $"Dossier manquant : {folder}");
        }

        [Test]
        public void UniversalRenderPipeline_IsDefault()
        {
            Assert.IsInstanceOf<UniversalRenderPipelineAsset>(GraphicsSettings.defaultRenderPipeline,
                "URP non assigné : lancez RugWash/Setup/Initialiser le Projet et Dossiers.");
        }

        [Test]
        public void InputSystemPackage_IsActiveBackend()
        {
            Assert.AreEqual(RugWashProjectBootstrapper.InputHandlerInputSystemOnly, RugWashProjectBootstrapper.GetActiveInputHandler(),
                "Player > Active Input Handling doit valoir 'Input System Package'.");
#if !ENABLE_INPUT_SYSTEM
            Assert.Fail("ENABLE_INPUT_SYSTEM absent : redémarrez l'éditeur après l'initialisation.");
#endif
        }

        [Test]
        public void WindowsGraphicsApis_AreConfigured()
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            Assert.IsTrue(apis.Contains(GraphicsDeviceType.Direct3D11) || apis.Contains(GraphicsDeviceType.Direct3D12),
                $"APIs Windows inattendues : {string.Join(", ", apis)}");
        }

        [Test]
        public void ExistingGameScenes_AreInBuildSettings()
        {
            var registered = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            foreach (string scene in RugWashPaths.BuildScenes.Where(File.Exists))
                CollectionAssert.Contains(registered, scene, $"{scene} absente des Build Settings.");
        }

        [Test]
        public void WorkshopScene_IsFullyWired()
        {
            if (!File.Exists(RugWashPaths.WorkshopScene))
                Assert.Ignore("Workshop.unity pas encore générée (RugWash/Setup/Générer la Scène Atelier Complète).");

            var scene = EditorSceneManager.OpenScene(RugWashPaths.WorkshopScene, OpenSceneMode.Additive);
            try
            {
                var bootstrap = scene.GetRootGameObjects()
                    .Select(root => root.GetComponentInChildren<RugWash.Core.GameBootstrap>(true))
                    .FirstOrDefault(c => c != null);
                Assert.IsNotNull(bootstrap, "GameBootstrap absent de la scène.");
                Assert.IsTrue(bootstrap.ValidateReferences(out string missing), $"Références manquantes : {missing}");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
