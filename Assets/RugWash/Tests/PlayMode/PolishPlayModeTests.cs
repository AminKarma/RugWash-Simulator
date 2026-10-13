using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Audio;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>Animations, ambiance et retours de la phase 5, dans la vraie scène.</summary>
    public class PolishPlayModeTests
    {
        GameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.IsTrue(game != null && game.IsReady);
            TestRugs.LoadDefault(game);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null) game.ToolController.AutomationUseOverride = false;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ToolSwitch_PlaysEquipAnimation_ThenSettles()
        {
            var rig = Object.FindAnyObjectByType<ViewModelRig>();
            Assert.IsNotNull(rig);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1f, rig.EquipProgress, 1e-4f);
            game.ToolController.SelectSlot(4);
            // Vérifié avant toute frame : la durée d'une frame en mode batch est imprévisible.
            Assert.AreEqual(0f, rig.EquipProgress, "L'outil doit être relevé depuis le bas après un changement.");
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(1f, rig.EquipProgress, 1e-4f);
            Assert.Less(rig.Offset.magnitude, 0.02f, "Au repos, le support revient à sa position.");
        }

        [UnityTest]
        public IEnumerator ObjectiveReached_IsAnnouncedOnce()
        {
            int calls = 0;
            game.Orders.ObjectiveReached += _ => calls++;
            game.Orders.TryStart(game.Orders.Book.Orders[0]);
            yield return null;
            Assert.AreEqual(0, calls);
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            yield return null;
            Assert.AreEqual(1, calls);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, calls, "L'annonce ne doit pas se répéter.");
        }

        [UnityTest]
        public IEnumerator Ambience_Plays_AndWorkshopHasNoErrors()
        {
            yield return new WaitForSeconds(0.5f);
            var ambient = Object.FindAnyObjectByType<AmbientAudio>();
            Assert.IsNotNull(ambient);
            Assert.IsTrue(ambient.IsPlaying);
            Assert.IsNotNull(Object.FindAnyObjectByType<FootstepAudio>());
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Galerie de captures pour la validation visuelle de la phase 5 (GPU requis).</summary>
        [UnityTest, Category("Capture")]
        public IEnumerator Capture_PolishGallery()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = game.ToolController.GetComponentInChildren<Camera>();

            Vector3 rugCenter = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            for (int slot = 1; slot <= 5; slot++)
            {
                game.PlayerController.Teleport(new Vector3(rugCenter.x - 0.6f, 0.06f, rugCenter.z - 1.5f), 10f, 38f);
                game.ToolController.SelectSlot(slot);
                yield return new WaitForSeconds(0.45f);
                Save(camera, Path.Combine(folder, $"14_tool_{slot}.png"));
            }

            Shot(new Vector3(2.4f, 0.06f, 0.8f), 90f, 8f);
            yield return new WaitForSeconds(0.3f);
            Save(camera, Path.Combine(folder, "15_drying_area.png"));
            Shot(new Vector3(-2.5f, 0.06f, 0.6f), -90f, 5f);
            yield return new WaitForSeconds(0.3f);
            Save(camera, Path.Combine(folder, "16_stations.png"));
            Shot(new Vector3(1.5f, 0.06f, -1.6f), 127f, 6f);
            yield return new WaitForSeconds(0.3f);
            Save(camera, Path.Combine(folder, "17_order_desk.png"));
            Shot(new Vector3(-0.5f, 0.06f, 1.2f), 0f, -8f);
            yield return new WaitForSeconds(0.3f);
            Save(camera, Path.Combine(folder, "18_back_wall.png"));
        }

        void Shot(Vector3 position, float yaw, float pitch) => game.PlayerController.Teleport(position, yaw, pitch);

        static void Save(Camera camera, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
