using NUnit.Framework;
using RugWash.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RugWash.Tests
{
    /// <summary>
    /// Mission de corrections (2.11) : chaque niveau de qualité a son pipeline ; les niveaux bas coûtent réellement moins
    /// (MSAA, occlusion ambiante, ombres, échelle de rendu) ; Ultra reste le pipeline d'origine.
    /// </summary>
    public class QualityTierTests
    {
        static UniversalRenderPipelineAsset LevelPipeline(int level) => QualitySettings.GetRenderPipelineAssetAt(level) as UniversalRenderPipelineAsset;

        static ScreenSpaceAmbientOcclusion Ssao(UniversalRenderPipelineAsset pipeline)
        {
            foreach (var data in pipeline.rendererDataList)
                if (data != null)
                    foreach (var feature in data.rendererFeatures)
                        if (feature is ScreenSpaceAmbientOcclusion ssao) return ssao;
            return null;
        }

        static bool SsaoDownsampled(ScreenSpaceAmbientOcclusion ssao) =>
            new SerializedObject(ssao).FindProperty("m_Settings.Downsample").boolValue;

        [Test]
        public void EveryQualityLevel_HasItsOwnUrpPipeline_AndUltraIsTheOriginal()
        {
            var ultra = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RugWashPaths.UrpAsset);
            Assert.IsNotNull(ultra);
            Assert.AreEqual(ultra, GraphicsSettings.defaultRenderPipeline, "Pipeline par défaut inchangé.");
            for (int level = 0; level < QualitySettings.names.Length; level++)
                Assert.IsNotNull(LevelPipeline(level), $"Niveau {QualitySettings.names[level]} : pipeline URP assigné.");
            Assert.AreEqual(ultra, LevelPipeline(QualitySettings.names.Length - 1), "Ultra = pipeline d'origine.");
            Assert.AreEqual(4, ultra.msaaSampleCount, "Ultra garde son MSAA 4×.");
            Assert.IsTrue(Ssao(ultra).isActive);
            Assert.IsFalse(SsaoDownsampled(Ssao(ultra)), "Ultra garde son occlusion pleine résolution.");
        }

        [Test]
        public void LowerLevels_ReallyCostLess()
        {
            var ultra = LevelPipeline(QualitySettings.names.Length - 1);
            int medium = System.Array.IndexOf(QualitySettings.names, "Medium"), low = System.Array.IndexOf(QualitySettings.names, "Low");
            Assume.That(medium >= 0 && low >= 0, "Niveaux Unity standard attendus.");
            var mediumPipeline = LevelPipeline(medium);
            var lowPipeline = LevelPipeline(low);
            Assert.AreNotEqual(ultra, mediumPipeline, "« Moyen » n'est plus le pipeline Ultra.");
            Assert.Less(mediumPipeline.msaaSampleCount, ultra.msaaSampleCount);
            var mediumAo = Ssao(mediumPipeline);
            Assert.IsTrue(mediumAo == null || !mediumAo.isActive || SsaoDownsampled(mediumAo), "Occlusion ambiante allégée en « Moyen ».");
            Assert.LessOrEqual(mediumPipeline.additionalLightsShadowmapResolution, ultra.additionalLightsShadowmapResolution);

            Assert.AreEqual(1, lowPipeline.msaaSampleCount);
            var lowAo = Ssao(lowPipeline);
            Assert.IsTrue(lowAo == null || !lowAo.isActive, "Pas d'occlusion ambiante en « Bas ».");
            Assert.IsFalse(lowPipeline.supportsAdditionalLightShadows, "Pas d'ombres de projecteur en « Bas ».");
            Assert.Less(lowPipeline.renderScale, 1f);

            int high = System.Array.IndexOf(QualitySettings.names, "High");
            var highPipeline = LevelPipeline(high);
            Assert.AreNotEqual(ultra, highPipeline);
            Assert.IsTrue(SsaoDownsampled(Ssao(highPipeline)), "« Élevé » : occlusion à mi-résolution (mesure : complète, il coûtait autant qu'Ultra).");
            Assert.Less(highPipeline.msaaSampleCount, ultra.msaaSampleCount);
        }
    }
}
