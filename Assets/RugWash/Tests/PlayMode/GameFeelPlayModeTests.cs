using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Effects;
using RugWash.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RugWash.Tests
{
    /// <summary>
    /// Jalon 1 (V2) dans la vraie scène : alignement des outils sur la cible, balancement réglable,
    /// scan de saleté, retours de progression et sons. Les déplacements passent par un clavier virtuel
    /// de l'Input System (même chemin que le vrai clavier).
    /// </summary>
    public class GameFeelPlayModeTests
    {
        GameBootstrap game;
        PlayerToolController tools;
        Keyboard keyboard;
        InputSettings originalInputSettings;

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
            TestRugs.LoadDefault(game);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (tools != null) tools.AutomationUseOverride = false;
            if (keyboard != null)
            {
                InputSystem.RemoveDevice(keyboard);
                keyboard = null;
            }
            if (originalInputSettings != null)
            {
                var temporary = InputSystem.settings;
                InputSystem.settings = originalInputSettings;
                originalInputSettings = null;
                Object.Destroy(temporary);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        /// <summary>Place le joueur face à un point du tapis, avec une orientation et un recul donnés.</summary>
        Vector3 AimFrom(Vector2 uv, float yaw, float standoff)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back * standoff;
            float pitch = Mathf.Atan2(1.68f - target.y, standoff) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x + back.x, 0.06f, target.z + back.z), yaw, pitch);
            return target;
        }

        /// <summary>
        /// Comme <see cref="AimFrom"/>, puis corrige l'inclinaison avec la hauteur réelle des yeux une fois le joueur posé
        /// (sol, plateau de lavage ou tapis) : le réticule vise alors exactement le point demandé.
        /// </summary>
        IEnumerator AimPrecisely(Vector2 uv, float yaw, float standoff)
        {
            Vector3 target = AimFrom(uv, yaw, standoff);
            yield return new WaitForSeconds(0.15f);
            var player = game.PlayerController.transform;
            Vector3 eye = Camera.main.transform.position;
            float horizontal = new Vector2(target.x - eye.x, target.z - eye.z).magnitude;
            float pitch = Mathf.Atan2(eye.y - target.y, horizontal) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(player.position, yaw, pitch);
        }

        /// <summary>
        /// Clavier virtuel de l'Input System. En mode batch l'application n'a pas le focus : les entrées seraient
        /// ignorées, donc la gestion du focus est désactivée sur une copie des réglages (restaurés après le test).
        /// </summary>
        Keyboard VirtualKeyboard()
        {
            if (keyboard != null) return keyboard;
            originalInputSettings = InputSystem.settings;
            var settings = Object.Instantiate(originalInputSettings);
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = settings;
            keyboard = InputSystem.AddDevice<Keyboard>("RugWashTestKeyboard");
            return keyboard;
        }

        IEnumerator Hold(Key key, float seconds, System.Action perFrame = null)
        {
            InputSystem.QueueStateEvent(VirtualKeyboard(), new KeyboardState(key));
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                perFrame?.Invoke();
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        // ------------------------------------------------------------------ 1.1 Alignement

        static readonly (Vector2 uv, float yaw, float standoff)[] Poses =
        {
            (new Vector2(0.5f, 0.5f), 0f, 1.1f),
            (new Vector2(0.25f, 0.3f), 0f, 1.6f),
            (new Vector2(0.75f, 0.7f), 0f, 0.9f),
            (new Vector2(0.5f, 0.5f), 90f, 1.3f),
            (new Vector2(0.4f, 0.6f), -90f, 1.0f),
            (new Vector2(0.6f, 0.4f), 180f, 1.4f),
            (new Vector2(0.5f, 0.5f), 35f, 2.0f),
        };

        [UnityTest]
        public IEnumerator EveryTool_PointsAtTheReticleTarget_FromAllAnglesAndDistances()
        {
            var camera = Camera.main;
            var report = new List<string>();
            for (int slot = 1; slot <= 5; slot++)
            {
                Assert.IsTrue(tools.SelectSlot(slot));
                var tool = tools.ActiveTool;
                Assert.IsNotNull(tool.AimTip, $"{tool.name} : extrémité active (aimTip) non câblée.");
                foreach (var pose in Poses)
                {
                    yield return AimPrecisely(pose.uv, pose.yaw, pose.standoff);
                    Vector3 target = game.Rug.UVToWorld(pose.uv);
                    yield return new WaitForSeconds(0.35f);

                    var aim = tools.CurrentAim;
                    Assert.IsTrue(aim.HasRug, $"{tool.name} : le tapis doit être visé ({pose.uv}, lacet {pose.yaw}).");
                    Assert.Less(Vector3.Distance(aim.Point, target), 0.06f, "Le réticule vise bien le point demandé.");
                    Vector3 viewport = camera.WorldToViewportPoint(aim.Point);
                    Assert.Less(new Vector2(viewport.x - 0.5f, viewport.y - 0.5f).magnitude, 0.002f,
                        "Le point traité est exactement sous le réticule.");
                    Assert.Less(tool.AimErrorDegrees, 1.5f,
                        $"{tool.name} vise à {tool.AimErrorDegrees:0.0}° de la cible (sans correction : {tool.UnalignedErrorDegrees:0.0}°), " +
                        $"pose {pose.uv} lacet {pose.yaw} recul {pose.standoff}.");
                    report.Add($"{tool.name} {pose.uv} lacet {pose.yaw} recul {pose.standoff} : sans correction {tool.UnalignedErrorDegrees:0.0}°, " +
                               $"corrigé {tool.AimErrorDegrees:0.00}°");
                }
            }
            Debug.Log("[RugWash] Alignement des outils :\n" + string.Join("\n", report));
        }

        [UnityTest]
        public IEnumerator ToolAlignment_IsStable_NoJitterWhenStill()
        {
            for (int slot = 1; slot <= 5; slot++)
            {
                tools.SelectSlot(slot);
                AimFrom(new Vector2(0.45f, 0.55f), 20f, 1.2f);
                yield return new WaitForSeconds(0.4f);
                var model = tools.ActiveTool.AimTip.parent;
                while (model.parent != null && model.parent.GetComponent<ViewModelRig>() == null) model = model.parent;
                Quaternion previous = model.rotation;
                float worst = 0f;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    worst = Mathf.Max(worst, Quaternion.Angle(previous, model.rotation));
                    previous = model.rotation;
                }
                Assert.Less(worst, 0.6f, $"{tools.ActiveTool.name} tremble ({worst:0.00}° entre deux images) alors que le joueur est immobile.");
            }
        }

        [UnityTest]
        public IEnumerator Brush_CleansUnderTheReticle_FromASideAngle()
        {
            var uv = new Vector2(0.35f, 0.62f);
            AimFrom(uv, 90f, 1.0f);
            tools.SelectSlot(3);
            yield return new WaitForSeconds(0.3f);
            var grid = game.Rug.Grid;
            var cellIndex = grid.UVToCell(uv);
            var farIndex = grid.UVToCell(uv + new Vector2(0.25f, 0f));
            var before = grid.GetCell(cellIndex.x, cellIndex.y);
            var farBefore = grid.GetCell(farIndex.x, farIndex.y);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.8f);
            tools.AutomationUseOverride = false;
            var after = grid.GetCell(cellIndex.x, cellIndex.y);
            var farAfter = grid.GetCell(farIndex.x, farIndex.y);
            Assert.Less(after.Stain + after.Dust, before.Stain + before.Dust, "La brosse détache la saleté sous le réticule.");
            Assert.AreEqual(farBefore.Stain, farAfter.Stain, 1e-6f, "Une zone éloignée du réticule n'est pas touchée.");
        }

        // ------------------------------------------------------------------ 1.2 Balancement

        [UnityTest]
        public IEnumerator Walking_MovesCameraAndTool_ButReticleStillMatchesCleaningPoint()
        {
            var cameraMotion = Object.FindAnyObjectByType<CameraMotion>();
            var rig = Object.FindAnyObjectByType<ViewModelRig>();
            Assert.IsNotNull(cameraMotion);
            Assert.IsNotNull(rig);
            game.Settings.SetMotionIntensity(1f);
            AimFrom(new Vector2(0.5f, 0.15f), 0f, 1.6f);
            yield return new WaitForSeconds(0.2f);

            float maxCamera = 0f, maxRig = 0f, maxReticleError = 0f, maxRoll = 0f;
            var camera = Camera.main;
            yield return Hold(Key.D, 0.9f, () =>
            {
                maxCamera = Mathf.Max(maxCamera, cameraMotion.Offset.magnitude);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(cameraMotion.Roll));
                maxRig = Mathf.Max(maxRig, rig.Offset.magnitude);
                if (tools.CurrentAim.HasHit)
                {
                    Vector3 v = camera.WorldToViewportPoint(tools.CurrentAim.Point);
                    maxReticleError = Mathf.Max(maxReticleError, new Vector2(v.x - 0.5f, v.y - 0.5f).magnitude);
                }
            });
            Assert.Greater(game.PlayerController.transform.position.x - game.Rug.UVToWorld(new Vector2(0.5f, 0.15f)).x, 0.5f,
                "Le clavier virtuel doit faire avancer le joueur (touche D).");
            Assert.Greater(maxCamera, 0.002f, "Balancier de caméra attendu en marchant.");
            Assert.Greater(maxRoll, 0.1f, "Légère inclinaison attendue en pas chassé.");
            Assert.Greater(maxRig, 0.002f, "Balancement de l'outil attendu en marchant.");
            Assert.Less(maxReticleError, 0.002f, "Le balancement ne doit jamais décaler la visée du réticule.");
        }

        [UnityTest]
        public IEnumerator MotionIntensityZero_DisablesCameraAndToolSway()
        {
            var cameraMotion = Object.FindAnyObjectByType<CameraMotion>();
            var rig = Object.FindAnyObjectByType<ViewModelRig>();
            game.Settings.SetMotionIntensity(0f);
            Assert.AreEqual(0f, cameraMotion.Intensity);
            Assert.AreEqual(0f, rig.Intensity);
            AimFrom(new Vector2(0.5f, 0.15f), 0f, 1.6f);
            yield return new WaitForSeconds(0.2f);

            float maxCamera = 0f, maxRig = 0f, maxRoll = 0f;
            yield return Hold(Key.D, 0.8f, () =>
            {
                maxCamera = Mathf.Max(maxCamera, cameraMotion.Offset.magnitude);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Vector3.SignedAngle(cameraMotion.transform.parent.up, cameraMotion.transform.up, cameraMotion.transform.forward)));
                maxRig = Mathf.Max(maxRig, rig.Offset.magnitude);
            });
            Assert.Less(maxCamera, 1e-5f);
            Assert.Less(maxRoll, 1e-3f);
            Assert.Less(maxRig, 1e-5f);
        }

        [UnityTest]
        public IEnumerator MotionIntensity_SetInSettings_IsAppliedSavedAndRestored()
        {
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsTrue(game.SettingsUI.Open());
            game.SettingsUI.MotionSlider.value = 0.25f;
            Assert.AreEqual(0.25f, Object.FindAnyObjectByType<CameraMotion>().Intensity, 1e-4f, "Appliqué immédiatement.");
            Assert.AreEqual(0.25f, Object.FindAnyObjectByType<ViewModelRig>().Intensity, 1e-4f);
            game.SettingsUI.CloseButton.onClick.Invoke();
            Assert.IsTrue(File.Exists(TestSaveSandbox.SavePath));
            game.GameState.Resume();

            SceneManager.LoadScene("Workshop", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
            Assert.AreEqual(0.25f, game.Settings.Current.motionIntensity, 1e-4f);
            Assert.AreEqual(0.25f, Object.FindAnyObjectByType<CameraMotion>().Intensity, 1e-4f, "Restauré au chargement.");
        }

        // ------------------------------------------------------------------ 1.3 Retours

        [UnityTest]
        public IEnumerator ToolSwitch_PlaysEquipSound_AndRaisesTool()
        {
            var audio = Object.FindAnyObjectByType<AudioManager>();
            var rig = Object.FindAnyObjectByType<ViewModelRig>();
            tools.SelectSlot(1);
            yield return new WaitForSeconds(0.3f);
            tools.SelectSlot(4);
            Assert.IsNotNull(audio.LastPlayedClip);
            StringAssert.Contains("tool_equip", audio.LastPlayedClip.name, "Un son de prise en main est joué au changement d'outil.");
            Assert.Less(rig.EquipProgress, 0.5f, "L'outil remonte progressivement.");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1f, rig.EquipProgress, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ContactWithRug_PlaysContactLayer_PulsesRing_AndOnlyWhileTouching()
        {
            var indicator = Object.FindAnyObjectByType<ToolTargetIndicator>();
            AimFrom(new Vector2(0.5f, 0.5f), 0f, 1.1f);
            tools.SelectSlot(4);
            yield return new WaitForSeconds(0.3f);
            int contacts = 0;
            tools.ActiveTool.ContactStarted += _ => contacts++;
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, contacts, "Un seul événement de contact au début du jet sur le tapis.");
            Assert.IsTrue(tools.ActiveTool.ToolAudio.IsContactAudible, "Le son de l'eau sur les fibres doit être audible.");
            Assert.AreEqual(ToolTargetIndicator.State.Active, indicator.CurrentState);

            // Visée hors du tapis (mur) : plus de contact, le son de contact s'éteint.
            game.PlayerController.Teleport(new Vector3(0f, 0.06f, 0f), 0f, -10f);
            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(tools.ActiveTool.IsAffectingRug);
            Assert.IsFalse(tools.ActiveTool.ToolAudio.IsContactAudible, "Pas de son de contact sans contact.");
            tools.AutomationUseOverride = false;
        }

        [UnityTest]
        public IEnumerator TargetRing_TurnsCleanColor_OverACleanZone()
        {
            var indicator = Object.FindAnyObjectByType<ToolTargetIndicator>();
            var uv = new Vector2(0.5f, 0.5f);
            AimFrom(uv, 0f, 1.1f);
            tools.SelectSlot(2);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(ToolTargetIndicator.State.Idle, indicator.CurrentState, "Le tapis de référence est sale au centre.");

            var grid = game.Rug.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                grid.SetCell(x, y, new RugCell());
            grid.RecomputeTotals();
            yield return null;
            yield return null;
            Assert.AreEqual(ToolTargetIndicator.State.Clean, indicator.CurrentState);
        }

        [UnityTest]
        public IEnumerator CleanlinessMilestone_PlaysSound_PulsesHud_AndShowsToast()
        {
            var audio = Object.FindAnyObjectByType<AudioManager>();
            float reachedValue = -1f;
            game.ProgressFeedback.MilestoneReached += value => reachedValue = value;

            var grid = game.Rug.Grid;
            Assert.Less(grid.Cleanliness01, 0.25f, "Tapis de référence sale au départ.");
            // Nettoie des rangées entières jusqu'à dépasser 50 % (une rangée ne peut pas faire sauter 25 %).
            for (int y = 0; y < grid.Height && grid.Cleanliness01 < 0.5f; y++)
            {
                for (int x = 0; x < grid.Width; x++) grid.SetCell(x, y, new RugCell());
                grid.RecomputeTotals();
            }
            Assert.That(grid.Cleanliness01, Is.InRange(0.5f, 0.74f));
            yield return null;
            yield return null;

            Assert.AreEqual(0.5f, reachedValue, "Palier 50 % annoncé (25 % franchi en même temps n'est pas répété).");
            StringAssert.Contains("milestone", audio.LastPlayedClip.name);
            Assert.Greater(game.Hud.MilestonePulse, 0f, "Le panneau de propreté « pulse ».");
        }

        [UnityTest]
        public IEnumerator ObjectiveReached_PlaysCelebration()
        {
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            game.OrderBoard.Rows[0].ActionButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(GameState.ActiveOrder, game.GameState.State);
            int before = game.ProgressFeedback.CelebrationsPlayed;
            GameLoopPlayModeTests.CleanThoroughly(game.Rug.Grid);
            yield return null;
            yield return null;
            Assert.AreEqual(before + 1, game.ProgressFeedback.CelebrationsPlayed, "Pluie d'éclats à l'objectif atteint.");
        }

        // ------------------------------------------------------------------ 1.4 Scan de saleté

        [UnityTest]
        public IEnumerator Scan_KeyR_HighlightsRealDirt_ThenRechargesAndRefusesMeanwhile()
        {
            var scanner = game.DirtScanner;
            Assert.IsTrue(scanner.IsReady);
            DirtScanResult? scanned = null;
            float deniedRemaining = -1f;
            scanner.Scanned += r => scanned = r;
            scanner.Denied += r => deniedRemaining = r;
            AimFrom(new Vector2(0.5f, 0.5f), 0f, 1.1f);

            yield return Hold(Key.R, 0.05f);
            Assert.IsTrue(scanned.HasValue, "La touche R (clavier virtuel) lance le scan.");
            Assert.IsTrue(scanner.IsActive);
            var expected = DirtScan.Analyze(game.Rug.Grid);
            Assert.AreEqual(expected.DirtyCells, scanned.Value.DirtyCells, "Le scan compte les cellules réellement sales.");
            Assert.Greater(scanned.Value.DirtyCells, 0);

            yield return new WaitForSeconds(0.5f);
            Vector4 parameters = Shader.GetGlobalVector("_RugScanParams");
            Assert.Greater(parameters.x, 0.9f, "Surbrillance envoyée au shader du tapis.");
            Assert.Greater(parameters.y, 1f, "L'onde se propage depuis le joueur.");
            Vector4 thresholds = Shader.GetGlobalVector("_RugScanThresholds");
            Assert.AreEqual(RugCleaningGrid.CleanCellThreshold, thresholds.x, 1e-6f, "Le shader utilise le seuil « propre » de la grille.");

            Assert.IsFalse(scanner.TryScan(), "Pas de second scan pendant le scan.");
            Assert.AreEqual(0f, deniedRemaining);

            yield return new WaitForSeconds(scanner.Duration);
            Assert.IsFalse(scanner.IsActive);
            Assert.AreEqual(0f, Shader.GetGlobalVector("_RugScanParams").x, 1e-5f, "La surbrillance disparaît après la durée.");
            Assert.Greater(scanner.CooldownRemaining, 0f);
            Assert.IsFalse(scanner.TryScan(), "Scan refusé pendant la recharge.");
            Assert.Greater(deniedRemaining, 0f, "Le refus indique le temps de recharge restant.");
        }

        [UnityTest]
        public IEnumerator Scan_OnSpotlessRug_FlagsNothing_AndIsBlockedInPause()
        {
            var grid = game.Rug.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                grid.SetCell(x, y, new RugCell());
            grid.RecomputeTotals();

            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsFalse(game.DirtScanner.TryScan(), "Pas de scan en pause.");
            game.GameState.Resume();
            yield return null;

            Assert.IsTrue(game.DirtScanner.TryScan());
            Assert.IsTrue(game.DirtScanner.LastResult.IsSpotless, "Un tapis propre ne signale aucune zone.");
        }
    
        // ------------------------------------------------------------------ Captures (vérification visuelle)

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_GameFeelGallery()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;
            Assert.IsNotNull(hud);

            // Avant correction : extrémité active retirée (réflexion, test uniquement) → pose fixe d'origine.
            Vector3 rugCenter = game.Rug.UVToWorld(new Vector2(0.5f, 0.5f));
            var aimTipField = typeof(CleaningToolBase).GetField("aimTip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var savedTips = new Transform[tools.Tools.Count];
            for (int i = 0; i < tools.Tools.Count; i++)
            {
                savedTips[i] = tools.Tools[i].AimTip;
                aimTipField.SetValue(tools.Tools[i], null);
            }
            for (int slot = 5; slot >= 1; slot--)
            {
                game.PlayerController.Teleport(new Vector3(rugCenter.x - 0.6f, 0.06f, rugCenter.z - 1.5f), 10f, 38f);
                tools.SelectSlot(slot);
                yield return new WaitForSeconds(0.45f);
                yield return Capture(camera, hud, Path.Combine(folder, $"19_tool_unaligned_{slot}.png"));
            }
            for (int i = 0; i < tools.Tools.Count; i++) aimTipField.SetValue(tools.Tools[i], savedTips[i]);

            // Après correction, même pose.
            for (int slot = 1; slot <= 5; slot++)
            {
                game.PlayerController.Teleport(new Vector3(rugCenter.x - 0.6f, 0.06f, rugCenter.z - 1.5f), 10f, 38f);
                tools.SelectSlot(slot);
                yield return new WaitForSeconds(0.45f);
                yield return Capture(camera, hud, Path.Combine(folder, $"20_tool_aligned_{slot}.png"));
            }

            // Outils en action : débris teintés par la saleté, eau trouble.
            yield return AimPrecisely(new Vector2(0.42f, 0.5f), 0f, 1.0f);
            tools.SelectSlot(3);
            tools.AutomationUseOverride = true;
            yield return new WaitForSeconds(0.5f);
            yield return Capture(camera, hud, Path.Combine(folder, "21_brush_debris.png"));
            tools.SelectSlot(4);
            yield return new WaitForSeconds(0.5f);
            yield return Capture(camera, hud, Path.Combine(folder, "22_rinse_muddy.png"));
            tools.AutomationUseOverride = false;
            yield return new WaitForSeconds(0.3f);

            // Scan : tiers gauche nettoyé, bande centrale propre mais chargée de résidus, reste sale.
            // Onde en cours de propagation, puis tapis entièrement révélé.
            var grid = game.Rug.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width * 0.55f; x++)
                grid.SetCell(x, y, x < grid.Width * 0.4f ? new RugCell() : new RugCell { Residue = 0.5f });
            game.PlayerController.Teleport(new Vector3(rugCenter.x, 0.06f, rugCenter.z - 2.3f), 0f, 34f);
            yield return new WaitForSeconds(0.3f);
            yield return Capture(camera, hud, Path.Combine(folder, "22b_before_scan.png"));
            Assert.IsTrue(game.DirtScanner.TryScan());
            yield return new WaitForSeconds(0.3f);
            yield return Capture(camera, hud, Path.Combine(folder, "23_scan_wave.png"));
            yield return new WaitForSeconds(1.2f);
            yield return Capture(camera, hud, Path.Combine(folder, "24_scan_full.png"));

            // Paramètres : réglage du balancement.
            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.IsTrue(game.SettingsUI.Open());
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "25_settings_motion.png"));
            game.SettingsUI.Close();
            game.GameState.Resume();
        }

        static IEnumerator Capture(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
