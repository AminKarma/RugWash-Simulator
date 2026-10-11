using System;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// Points d'entrée batch-mode (-executeMethod). Code de sortie 0 = succès, 1 = échec.
    /// Exemple : Unity.exe -batchmode -nographics -projectPath . -executeMethod RugWash.Editor.RugWashCI.Bootstrap -logFile Logs/ci/bootstrap.log
    /// </summary>
    public static class RugWashCI
    {
        /// <summary>Si ce code s'exécute, tous les assemblies du projet ont compilé.</summary>
        public static void CompileCheck() => Run("CompileCheck", () =>
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new Exception("Erreurs de compilation détectées.");
        });

        public static void Bootstrap() => Run("Bootstrap", () => RugWashProjectBootstrapper.Initialize());

        public static void BuildWorkshopScene() => Run("BuildWorkshopScene", () => RugWashSceneBuilder.BuildWorkshopScene());

        static void Run(string step, Action action)
        {
            try
            {
                action();
                AssetDatabase.SaveAssets();
                Debug.Log($"[RugWash CI] {step} : OK");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[RugWash CI] {step} : ÉCHEC");
                EditorApplication.Exit(1);
            }
        }
    }
}
