using NUnit.Framework;
using RugWash.Save;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phases 6 et 7 : quatre préréglages graphiques qui changent vraiment le rendu (Très bas, Bas, Moyen,
    /// Ultra), synchronisation verticale indépendante du préréglage, limite d'images cohérente avec elle.
    /// </summary>
    public class GraphicsSettingsTests
    {
        [Test]
        public void Presets_MapToFourDistinctLevels_AndOldLevelsBecomeUltra()
        {
            Assert.AreEqual(4, SettingsRules.PresetLevels.Length);
            Assert.AreEqual(SettingsRules.PresetLevels.Length, SettingsRules.PresetNames.Length);
            Assert.AreEqual(SettingsRules.PresetLevels.Length, SettingsRules.PresetDescriptions.Length);
            Assert.AreEqual(0, SettingsRules.PresetOf(0));
            Assert.AreEqual(1, SettingsRules.PresetOf(1));
            Assert.AreEqual(2, SettingsRules.PresetOf(2));
            Assert.AreEqual(3, SettingsRules.PresetOf(5));
            Assert.AreEqual(3, SettingsRules.PresetOf(3), "Ancien « Élevé » : rangé avec Ultra (même coût mesuré).");
            Assert.AreEqual(3, SettingsRules.PresetOf(4));
            Assert.AreEqual(0, SettingsRules.PresetOf(-1));
            for (int i = 0; i < SettingsRules.PresetLevels.Length; i++)
                Assert.Less(SettingsRules.PresetLevels[i], QualitySettings.names.Length, "Chaque préréglage existe dans le projet.");
        }

        [Test]
        public void FrameRate_IsCoherentWithVSync()
        {
            Assert.AreEqual(-1, SettingsRules.TargetFrameRate(true, 60), "Synchronisation : la limite est ignorée.");
            Assert.AreEqual(60, SettingsRules.TargetFrameRate(false, 60));
            Assert.AreEqual(-1, SettingsRules.TargetFrameRate(false, 0), "Illimitée.");
            Assert.AreEqual("Réglée par la synchronisation", SettingsRules.FrameRateLabel(true, 144));
            Assert.AreEqual("144 images/s", SettingsRules.FrameRateLabel(false, 144));
            Assert.AreEqual(30, SettingsRules.StepCap(0, 1));
            Assert.AreEqual(240, SettingsRules.StepCap(0, -1), "La liste boucle.");
            Assert.AreEqual(30, SettingsRules.StepCap(57, 1), "Valeur inconnue : reprise au début.");
            var data = new SettingsData { frameRateCap = 57, voiceVolume = 3f };
            SaveValidator.ClampSettings(data);
            Assert.AreEqual(0, data.frameRateCap, "Limite modifiée à la main : illimitée.");
            Assert.AreEqual(1f, data.voiceVolume);
            // V0.2.5 : plus de difficulté dans les paramètres ; un ancien profil qui en contient une se lit sans erreur.
            var legacy = JsonUtility.FromJson<SettingsData>("{\"missionDifficulty\":2,\"voiceVolume\":0.5}");
            Assert.AreEqual(0.5f, legacy.voiceVolume, 1e-4f);
        }

        static UniversalRenderPipelineAsset LevelPipeline(int level) => QualitySettings.GetRenderPipelineAssetAt(level) as UniversalRenderPipelineAsset;

        [Test]
        public void VeryLow_IsReallyLighterThanLow()
        {
            var veryLow = LevelPipeline(SettingsRules.PresetLevels[0]);
            var low = LevelPipeline(SettingsRules.PresetLevels[1]);
            Assert.IsNotNull(veryLow);
            Assert.AreNotEqual(low, veryLow, "Très bas a son propre pipeline.");
            Assert.Less(veryLow.renderScale, low.renderScale, "Rendu à plus basse résolution.");
            Assert.IsFalse(veryLow.supportsMainLightShadows, "Aucune ombre.");
            Assert.IsFalse(veryLow.supportsAdditionalLightShadows);
            Assert.IsFalse(veryLow.supportsHDR, "HDR coupé.");
            Assert.AreEqual(1, veryLow.msaaSampleCount);
            Assert.AreEqual(4, LevelPipeline(SettingsRules.PresetLevels[3]).msaaSampleCount, "Ultra garde son MSAA 4×.");
        }
    }
}
