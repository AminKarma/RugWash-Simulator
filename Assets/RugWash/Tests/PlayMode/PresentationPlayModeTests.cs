using System.Collections;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Effects;
using RugWash.Save;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Grande mise à jour, phase 6 : icônes 3D des outils (une caméra, un atlas), boutique dont les lignes s'adaptent au texte,
    /// liquide qui ballotte dans le flacon, options graphiques (préréglage, synchronisation verticale cohérente, post-traitement).
    /// </summary>
    public class PresentationPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            TestSaveSandbox.Reset(legacyTools: true);
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            if (game != null && game.Settings != null)
            {
                game.Settings.SetQualityPreset(3);
                game.Settings.SetVSync(true);
                game.Settings.SetFrameRateCap(0);
                game.Settings.SetPostProcessing(true);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ToolIcons_AreRenderedOnce_AndShownInTheToolbarLockerAndShop()
        {
            yield return null;
            var icons = Object.FindAnyObjectByType<ToolIconRenderer>();
            Assert.IsNotNull(icons);
            Assert.IsNotNull(icons.Atlas, "Atlas des icônes créé.");
            Assert.Greater(icons.Rendered, 5, "Une icône par outil possédé ou non.");
            Assert.IsTrue(icons.TryGetIcon("vacuum", out Rect uv));
            Assert.Greater(uv.width, 0f);
            Assert.IsTrue(icons.TryGetIcon("prank:bat", out _), "Les gadgets ont aussi leur icône.");

            game.Toolbar.Refresh();
            Assert.IsTrue(game.Toolbar.SlotHasIcon(0), "Barre d'outils : icône de l'objet porté.");

            Assert.IsTrue(game.Locker.Open());
            yield return null;
            Assert.IsTrue(game.Locker.CarriedRows[0].HasIcon, "Casier : icône des objets.");
            game.Locker.Close();

            Assert.IsTrue(game.ShopUI.Open());
            bool anyIcon = false, grew = false;
            foreach (var tab in new[] { RugWash.UI.ShopUI.Tab.Upgrades, RugWash.UI.ShopUI.Tab.Workshop, RugWash.UI.ShopUI.Tab.Pranks })
            {
                game.ShopUI.ShowTab(tab);
                yield return null;
                foreach (var row in game.ShopUI.Rows)
                {
                    if (!row.gameObject.activeSelf) continue;
                    if (tab == RugWash.UI.ShopUI.Tab.Upgrades) anyIcon |= row.HasIcon;
                    // Plus aucun texte ne sort de sa ligne : la ligne contient tout le texte des détails.
                    float top = -row.DetailsLabel.rectTransform.anchoredPosition.y;
                    Assert.GreaterOrEqual(row.PreferredHeight + 0.5f, top + row.DetailsLabel.preferredHeight, $"Texte contenu ({tab}) : {row.name}");
                    grew |= row.PreferredHeight > 96.5f;
                }
            }
            Assert.IsTrue(anyIcon, "Améliorations d'outil : icône de l'outil.");
            Assert.IsTrue(grew, "Une ligne au texte long (machine à laver…) s'agrandit au lieu de déborder.");
            game.ShopUI.Close();
        }

        [UnityTest]
        public IEnumerator Liquid_SettlesFlat_AndSloshesWhenTheToolMoves()
        {
            var soap = (Cleaning.CleaningToolBase)null;
            foreach (var tool in game.ToolController.Tools)
                if (tool.Definition.id == "soap") soap = tool;
            Assert.IsTrue(game.ToolController.SelectSlot(soap.Definition.slot));
            yield return new WaitForSeconds(0.5f);
            ContainerLevel level = null;
            foreach (var candidate in Object.FindObjectsByType<ContainerLevel>())
                if (candidate.isActiveAndEnabled && candidate.name == "Bottle") level = candidate;
            Assert.IsNotNull(level);
            var start = game.PlayerController.transform.position;
            game.PlayerController.Teleport(start + new Vector3(0.6f, 0f, 0f), 0f);
            yield return null;
            game.PlayerController.Teleport(start, 0f);
            yield return null;
            yield return null;
            Assert.Greater(level.Wobble.magnitude, 1e-4f, "Mouvement brusque : la surface du liquide penche.");
            yield return new WaitForSeconds(3f);
            Assert.Less(level.Wobble.magnitude, 0.01f, "Puis elle se calme.");
        }

        [UnityTest]
        public IEnumerator Graphics_PresetsVSyncAndPostProcessing_AreAppliedAndSaved()
        {
            var settings = game.Settings;
            settings.SetVSync(true);
            settings.SetQualityPreset(0);
            yield return null;
            Assert.AreEqual(SettingsRules.PresetLevels[0], QualitySettings.GetQualityLevel(), "Très bas appliqué.");
            Assert.AreEqual(1, QualitySettings.vSyncCount, "Le préréglage n'impose pas sa synchronisation : celle du joueur reste.");
            var pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(pipeline);
            Assert.Less(pipeline.renderScale, 0.8f);

            settings.SetVSync(false);
            settings.SetFrameRateCap(60);
            Assert.AreEqual(0, QualitySettings.vSyncCount);
            Assert.AreEqual(60, Application.targetFrameRate);
            settings.SetQualityPreset(2);
            Assert.AreEqual(0, QualitySettings.vSyncCount, "Changer de préréglage garde la synchronisation choisie.");
            Assert.AreEqual(60, Application.targetFrameRate);
            settings.SetVSync(true);
            Assert.AreEqual(-1, Application.targetFrameRate, "Synchronisation activée : la limite ne s'applique pas.");

            settings.SetPostProcessing(false);
            var camera = game.PlayerController.GetComponentInChildren<Camera>();
            Assert.IsFalse(camera.GetUniversalAdditionalCameraData().renderPostProcessing);

            Assert.IsTrue(game.SettingsUI.Open());
            game.SettingsUI.ShowTab(1);
            yield return null;
            StringAssert.Contains("Ray tracing", game.SettingsUI.GraphicsInfo, "État du ray tracing expliqué, sans faux bouton.");
            StringAssert.Contains("Moyen", game.SettingsUI.GraphicsInfo);
            game.SettingsUI.Close();

            Assert.IsTrue(game.SaveSystem.SaveNow("test graphismes"));
            var profile = new ProfileStore(TestSaveSandbox.Directory).Load(out _);
            Assert.AreEqual(SettingsRules.PresetLevels[2], profile.settings.qualityLevel);
            Assert.IsTrue(profile.settings.vSync);
            Assert.AreEqual(60, profile.settings.frameRateCap);
            Assert.IsFalse(profile.settings.postProcessing);
        }
    }
}
