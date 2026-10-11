using System.Collections;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Effects;
using RugWash.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>Intégration des outils de la phase 2 dans la vraie scène (aspirateur, savon, extracteur, sons, effets).</summary>
    public class ToolsPhase2PlayModeTests
    {
        GameBootstrap game;
        PlayerToolController tools;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            tools = game.ToolController;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (tools != null) tools.AutomationUseOverride = false;
            Time.timeScale = 1f;
            yield return null;
        }

        void AimAtRug(Vector2 uv)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, 1.1f) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - 1.1f), 0f, pitch);
        }

        RugCell CellAt(Vector2 uv)
        {
            var cell = game.Rug.Grid.UVToCell(uv);
            return game.Rug.Grid.GetCell(cell.x, cell.y);
        }

        IEnumerator Use(int slot, float seconds)
        {
            Assert.IsTrue(tools.SelectSlot(slot), $"Aucun outil sur la touche {slot}.");
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(seconds);
            tools.AutomationUseOverride = false;
            yield return null;
        }

        [Test]
        public void FiveTools_AreAvailable_OnSlots1To5()
        {
            Assert.AreEqual(5, tools.Tools.Count);
            Assert.IsInstanceOf<VacuumTool>(tools.Tools[0]);
            Assert.IsInstanceOf<SoapSprayerTool>(tools.Tools[1]);
            Assert.IsInstanceOf<BrushTool>(tools.Tools[2]);
            Assert.IsInstanceOf<WaterRinseTool>(tools.Tools[3]);
            Assert.IsInstanceOf<WaterExtractorTool>(tools.Tools[4]);
            for (int i = 0; i < 5; i++) Assert.AreEqual(i + 1, tools.Tools[i].Definition.slot);
        }

        [UnityTest]
        public IEnumerator Vacuum_RemovesDust_AtTarget()
        {
            var uv = new Vector2(0.4f, 0.5f);
            AimAtRug(uv);
            float dustBefore = CellAt(uv).Dust;
            Assert.Greater(dustBefore, 0f, "Le tapis de référence doit être poussiéreux.");
            yield return Use(1, 1.5f);
            Assert.Less(CellAt(uv).Dust, dustBefore * 0.5f);
            Assert.Greater(game.Rug.Cleanliness01, 0f);
        }

        [UnityTest]
        public IEnumerator Soap_ConsumesProduct_AddsFoam_AndIsBlockedWhenEmpty()
        {
            var uv = new Vector2(0.5f, 0.5f);
            AimAtRug(uv);
            var soap = tools.Equipment.Soap;
            float before = soap.Amount;
            yield return Use(2, 1f);
            Assert.Less(soap.Amount, before, "Le pulvérisateur consomme du détergent.");
            Assert.Greater(CellAt(uv).Foam, 0.3f);
            Assert.Greater(CellAt(uv).Residue, 0f);

            soap.Consume(soap.Capacity);
            tools.SelectSlot(2);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(tools.ActiveTool.IsUsing, "Impossible de savonner sans produit.");
            Assert.IsNotNull(tools.ActiveTool.GetBlockReason());
            tools.AutomationUseOverride = false;
        }

        [UnityTest]
        public IEnumerator Extractor_DriesRug_FillsRecovery_AndDrainEmptiesIt()
        {
            var uv = new Vector2(0.5f, 0.5f);
            AimAtRug(uv);
            yield return Use(4, 1.2f);
            float wet = CellAt(uv).Moisture;
            Assert.Greater(wet, 0.5f);

            yield return Use(5, 1.5f);
            Assert.Less(CellAt(uv).Moisture, wet * 0.5f, "L'extracteur doit sécher la zone visée.");
            var recovery = tools.Equipment.Recovery;
            Assert.Greater(recovery.Amount, 0f, "L'eau extraite remplit la cuve.");

            recovery.Add(recovery.Capacity);
            tools.SelectSlot(5);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(tools.ActiveTool.IsUsing, "Cuve pleine : extraction impossible.");
            tools.AutomationUseOverride = false;

            var drain = System.Array.Find(Object.FindObjectsByType<ResourceStation>(), s => s.Resource == ToolResource.Recovery);
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            Assert.IsTrue(drain.CanInteract(interactor));
            drain.Interact(interactor);
            Assert.IsTrue(recovery.IsEmpty);
        }

        [UnityTest]
        public IEnumerator SoapStation_Refills_Detergent()
        {
            var soap = tools.Equipment.Soap;
            soap.Consume(2f);
            var station = System.Array.Find(Object.FindObjectsByType<ResourceStation>(), s => s.Resource == ToolResource.Soap);
            var interactor = Object.FindAnyObjectByType<PlayerInteractor>();
            Assert.IsTrue(station.CanInteract(interactor));
            station.Interact(interactor);
            Assert.AreEqual(soap.Capacity, soap.Amount, 1e-4f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ToolSound_Plays_ThenStopsOnToolChangeAndPause()
        {
            AimAtRug(new Vector2(0.5f, 0.5f));
            tools.SelectSlot(1);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.5f);
            var vacuumAudio = tools.ActiveTool.ToolAudio;
            Assert.IsNotNull(vacuumAudio);
            Assert.IsTrue(vacuumAudio.IsAudible, "L'aspirateur doit produire un son en fonctionnement.");

            tools.SelectSlot(3);
            yield return null;
            Assert.IsFalse(vacuumAudio.IsAudible, "Le son de l'outil rangé doit s'arrêter.");

            yield return new WaitForSeconds(0.3f);
            var brushAudio = tools.ActiveTool.ToolAudio;
            game.GameState.Pause();
            yield return null;
            Assert.IsFalse(brushAudio.IsAudible, "Aucun son d'outil en pause.");
            Assert.IsTrue(AudioListener.pause);
            game.GameState.Resume();
            tools.AutomationUseOverride = false;
            yield return null;
            Assert.IsFalse(AudioListener.pause);
        }

        [UnityTest]
        public IEnumerator CleanFeedbackPool_StaysBounded()
        {
            var pool = Object.FindAnyObjectByType<CleaningVFXPool>();
            Assert.IsNotNull(pool);
            int accepted = 0;
            for (int i = 0; i < 200; i++)
            {
                if (pool.Spawn(Vector3.up, Vector3.up)) accepted++;
                yield return null;
            }
            Assert.Greater(accepted, 0);
            Assert.LessOrEqual(pool.ActiveCount, pool.Size);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FloorGetsWet_WhenRinsing_ThenDries()
        {
            var water = Object.FindAnyObjectByType<WaterEffectController>();
            Assert.IsNotNull(water);
            AimAtRug(new Vector2(0.5f, 0.5f));
            yield return Use(4, 1.5f);
            Assert.Greater(water.Wetness, 0.1f);
            float wet = water.Wetness;
            yield return new WaitForSeconds(1f);
            Assert.Less(water.Wetness, wet, "Le sol sèche quand on arrête de rincer.");
        }
    }
}
