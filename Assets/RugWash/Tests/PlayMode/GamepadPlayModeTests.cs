using System.Collections;
using System.IO;
using NUnit.Framework;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Player;
using RugWash.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RugWash.Tests
{
    /// <summary>
    /// Jalon 4 — parcours complet à la manette virtuelle (Input System) : déplacement, caméra, outils, interaction,
    /// commandes, pause, paramètres, reconfiguration, vibrations, changement de périphérique et menu principal.
    /// Aucune action n'est faite au clavier ou à la souris, sauf pour vérifier le retour au clavier/souris.
    /// </summary>
    public class GamepadPlayModeTests
    {
        GameBootstrap game;
        Gamepad pad;
        Mouse mouse;
        InputSettings originalInputSettings;
        GamepadState state;

        [UnitySetUp]
        public IEnumerator LoadWorkshop()
        {
            TestSaveSandbox.Reset();
            yield return Load("Workshop");
            if (game != null) TestRugs.LoadDefault(game);
        }

        IEnumerator Load(string scene)
        {
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;
            game = Object.FindAnyObjectByType<GameBootstrap>();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (game != null) game.ToolController.AutomationUseOverride = false;
            if (pad != null)
            {
                InputSystem.RemoveDevice(pad);
                pad = null;
            }
            if (mouse != null)
            {
                InputSystem.RemoveDevice(mouse);
                mouse = null;
            }
            if (originalInputSettings != null)
            {
                var temporary = InputSystem.settings;
                InputSystem.settings = originalInputSettings;
                originalInputSettings = null;
                Object.Destroy(temporary);
            }
            state = default;
            Time.timeScale = 1f;
            yield return null;
        }

        // ------------------------------------------------------------------ Manette virtuelle

        /// <summary>
        /// Manette virtuelle. En mode batch l'application n'a pas le focus : la gestion du focus est désactivée sur une
        /// copie des réglages (restaurés après le test).
        /// </summary>
        Gamepad Pad()
        {
            if (pad != null) return pad;
            IgnoreFocus();
            pad = InputSystem.AddDevice<Gamepad>("RugWashTestGamepad");
            return pad;
        }

        void IgnoreFocus()
        {
            if (originalInputSettings != null) return;
            originalInputSettings = InputSystem.settings;
            var settings = Object.Instantiate(originalInputSettings);
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = settings;
        }

        void Send() => InputSystem.QueueStateEvent(Pad(), state);

        IEnumerator Press(GamepadButton button)
        {
            state = state.WithButton(button, true);
            Send();
            yield return null;
            state = state.WithButton(button, false);
            Send();
            yield return null;
        }

        IEnumerator Stick(bool left, Vector2 value, float seconds)
        {
            if (left) state.leftStick = value;
            else state.rightStick = value;
            Send();
            yield return new WaitForSecondsRealtime(seconds);
            if (left) state.leftStick = Vector2.zero;
            else state.rightStick = Vector2.zero;
            Send();
            yield return null;
        }

        IEnumerator Trigger(float seconds)
        {
            state.rightTrigger = 1f;
            Send();
            yield return new WaitForSeconds(seconds);
            state.rightTrigger = 0f;
            Send();
            yield return null;
        }

        static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        void AimAtRug(Vector2 uv, float standoff = 0.9f)
        {
            Vector3 target = game.Rug.UVToWorld(uv);
            float pitch = Mathf.Atan2(1.68f - target.y, standoff) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(new Vector3(target.x, 0.06f, target.z - standoff), 0f, pitch);
        }

        /// <summary>Place le joueur face au bureau de l'ordinateur (rayon d'interaction sur son collider).</summary>
        void FaceOrderDesk()
        {
            PanelInteractable desk = null;
            foreach (var panel in Object.FindObjectsByType<PanelInteractable>())
                if (panel.Opens == PanelInteractable.Target.Computer) desk = panel;
            Assert.IsNotNull(desk);
            var bounds = desk.GetComponentInChildren<Collider>().bounds;
            Vector3 center = bounds.center;
            // Le comptoir est contre le mur du fond (z négatif) : le joueur se place devant, côté atelier.
            Vector3 position = new Vector3(center.x, 0.06f, bounds.max.z + 0.75f);
            Vector3 look = center - (position + Vector3.up * 1.68f);
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            game.PlayerController.Teleport(position, yaw, pitch);
        }

        IEnumerator SelectToolWithBumpers(int slot)
        {
            for (int i = 0; i < 10 && game.ToolController.ActiveTool.Definition.slot != slot; i++)
                yield return Press(GamepadButton.RightShoulder);
            Assert.AreEqual(slot, game.ToolController.ActiveTool.Definition.slot, "Outil atteint avec RB.");
        }

        // ------------------------------------------------------------------ Parcours

        [UnityTest]
        public IEnumerator Gamepad_MovesLooksSelectsCleansAndTakesAnOrder()
        {
            var monitor = game.InputMonitor;
            Assert.AreEqual(InputScheme.KeyboardMouse, monitor.Scheme);
            Assert.That(game.Hud.HintText, Does.Contain("Clic gauche"));

            // Déplacement au stick gauche : le périphérique actif devient la manette, les indications suivent.
            AimAtRug(new Vector2(0.5f, 0.5f), 1.6f);
            yield return null;
            Vector3 start = game.PlayerController.transform.position;
            yield return Stick(true, new Vector2(0f, 1f), 0.5f);
            Assert.Greater(Vector3.Distance(start, game.PlayerController.transform.position), 0.6f, "Le stick gauche déplace le joueur.");
            Assert.AreEqual(InputScheme.Gamepad, monitor.Scheme);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.Hud.HintText, Does.Contain("RT").And.Contain("LB / RB").And.Contain("A : interagir"));
            Assert.That(game.Hud.ScanText, Does.Contain("[Y]"));
            Assert.That(game.Hud.OrderText, Does.Contain("(A)"));

            // Caméra au stick droit, indépendante du framerate (degrés par seconde).
            float yaw = game.PlayerController.transform.eulerAngles.y;
            yield return Stick(false, new Vector2(1f, 0f), 0.3f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw, game.PlayerController.transform.eulerAngles.y)), 15f, "Le stick droit tourne la caméra.");

            // Changement d'outil (RB / LB) et utilisation à la gâchette droite.
            var first = game.ToolController.ActiveTool;
            yield return Press(GamepadButton.RightShoulder);
            Assert.AreNotSame(first, game.ToolController.ActiveTool, "RB : outil suivant.");
            yield return Press(GamepadButton.LeftShoulder);
            Assert.AreSame(first, game.ToolController.ActiveTool, "LB : outil précédent.");
            yield return SelectToolWithBumpers(1);
            AimAtRug(new Vector2(0.5f, 0.5f));
            yield return null;
            float before = game.Rug.Cleanliness01;
            yield return Trigger(1.2f);
            Assert.Greater(game.Rug.Cleanliness01, before + 0.002f, "RT : l'aspirateur nettoie réellement.");

            // Interaction (A) : l'ordinateur s'ouvre sur l'icône « Missions / Commandes », A l'ouvre, la liste est
            // déjà sélectionnée sur la première commande.
            FaceOrderDesk();
            yield return null;
            yield return null;
            Assert.IsNotNull(Object.FindAnyObjectByType<PlayerInteractor>().Current, "L'ordinateur est visé.");
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.Hud.PromptText, Does.StartWith("[A]"));
            yield return Press(GamepadButton.South);
            Assert.IsTrue(game.Computer.IsOpen, "A ouvre l'ordinateur.");
            yield return null;
            Assert.AreSame(game.Computer.MissionsButton.gameObject, Selected, "Focus sur l'icône « Missions / Commandes ».");
            yield return Press(GamepadButton.South);
            Assert.IsFalse(game.Computer.IsOpen);
            Assert.IsTrue(game.OrderBoard.IsOpen, "A sur l'icône ouvre la liste des commandes.");
            yield return null;
            Assert.AreSame(game.OrderBoard.Rows[0].ActionButton.gameObject, Selected, "Focus sur « Accepter » de la première commande.");
            Assert.IsFalse(Cursor.visible, "Manette : pas de curseur dans les menus.");

            // A accepte la commande sélectionnée ; la fenêtre se ferme, la sélection est vidée.
            yield return Press(GamepadButton.South);
            yield return null;
            Assert.IsNotNull(game.Orders.ActiveOrder, "Commande acceptée à la manette.");
            Assert.IsFalse(game.OrderBoard.IsOpen);
            Assert.IsNull(Selected, "Aucun bouton caché ne reste sélectionné en jeu.");
            Assert.IsTrue(game.GameState.IsGameplayActive);
        }

        [UnityTest]
        public IEnumerator Gamepad_PauseSettingsTabsAndBack_RestoreTheSelection()
        {
            yield return Stick(true, new Vector2(0.5f, 0f), 0.1f);
            yield return Press(GamepadButton.Start);
            Assert.AreEqual(GameState.Paused, game.GameState.State, "Menu : pause.");
            yield return null;
            Assert.AreSame(game.PauseMenu.ResumeButton.gameObject, Selected, "Focus sur « Reprendre ».");

            // Croix directionnelle jusqu'à « Paramètres », puis A.
            for (int i = 0; i < 10 && Selected != game.PauseMenu.SettingsButton.gameObject; i++)
                yield return Press(GamepadButton.DpadDown);
            Assert.AreSame(game.PauseMenu.SettingsButton.gameObject, Selected);
            yield return Press(GamepadButton.South);
            Assert.IsTrue(game.SettingsUI.IsOpen, "A ouvre les paramètres.");
            yield return null;
            Assert.IsTrue(game.SettingsUI.transform.GetComponentsInChildren<Transform>(false).Length > 0);
            Assert.IsNotNull(Selected);
            Assert.IsTrue(Selected.transform.IsChildOf(game.SettingsUI.transform), "Focus dans les paramètres, pas sur la pause recouverte.");

            Assert.AreEqual(0, game.SettingsUI.CurrentTab);
            yield return Press(GamepadButton.RightShoulder);
            Assert.AreEqual(1, game.SettingsUI.CurrentTab, "RB : onglet suivant.");
            yield return Press(GamepadButton.RightShoulder);
            Assert.AreEqual(2, game.SettingsUI.CurrentTab);
            yield return Press(GamepadButton.LeftShoulder);
            Assert.AreEqual(1, game.SettingsUI.CurrentTab, "LB : onglet précédent.");

            // Le réglage de la vitesse de caméra au stick est réellement appliqué.
            game.SettingsUI.GamepadLookSlider.value = 90f;
            Assert.AreEqual(90f, game.PlayerController.StickLookSpeed, 1e-3f);
            game.SettingsUI.DeadZoneSlider.value = 0.3f;
            Assert.AreEqual(0.3f, game.Input.StickDeadZone, 1e-3f);

            // B ferme les paramètres : la pause retrouve son bouton « Paramètres ».
            yield return Press(GamepadButton.East);
            Assert.IsFalse(game.SettingsUI.IsOpen, "B ferme la fenêtre du dessus.");
            Assert.AreEqual(GameState.Paused, game.GameState.State, "La pause reste ouverte.");
            yield return null;
            Assert.AreSame(game.PauseMenu.SettingsButton.gameObject, Selected, "Sélection restaurée.");

            // B reprend la partie.
            yield return Press(GamepadButton.East);
            Assert.AreNotEqual(GameState.Paused, game.GameState.State, "B reprend la partie.");
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Gamepad_RebindsScan_ToTheDpad_AndThePromptFollows()
        {
            yield return Stick(true, new Vector2(0.5f, 0f), 0.1f);
            Assert.IsTrue(game.SettingsUI.Open());
            game.SettingsUI.ShowTab(2);
            int scanRow = -1;
            for (int i = 0; i < game.SettingsUI.RebindRows.Count; i++)
                if (RugWash.Save.SettingsManager.RebindableActions[i].Label == "Scanner la saleté") scanRow = i;
            Assert.GreaterOrEqual(scanRow, 0);
            var row = game.SettingsUI.RebindRows[scanRow];
            Assert.AreEqual("Y", row.GamepadText);

            row.GamepadButton.onClick.Invoke();
            yield return null;
            yield return null;
            yield return Press(GamepadButton.DpadUp);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return null;
            Assert.IsFalse(game.SettingsUI.IsRebinding, "Saisie terminée.");
            Assert.AreEqual("Croix ↑", row.GamepadText, "Nouveau bouton affiché.");
            Assert.That(game.SettingsUI.Message, Does.Contain("modifiée"));
            game.SettingsUI.Close();
            yield return null;

            // En jeu : le nouveau bouton déclenche le scan et l'indication suit.
            AimAtRug(new Vector2(0.5f, 0.5f));
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.Hud.ScanText, Does.Contain("[Croix ↑]"));
            yield return Press(GamepadButton.DpadUp);
            Assert.IsTrue(game.DirtScanner.IsActive, "Le bouton reconfiguré scanne.");

            game.Settings.ResetBindings();
            Assert.AreEqual("Y", game.Input.Label("Scan"));
        }

        [UnityTest]
        public IEnumerator Rumble_FollowsTheToolInUse_AndStopsInMenusOrWhenDisabled()
        {
            yield return Stick(true, new Vector2(0.5f, 0f), 0.1f);
            game.ToolController.SelectSlot(4);
            AimAtRug(new Vector2(0.5f, 0.5f));
            yield return null;

            state.rightTrigger = 1f;
            Send();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(game.ToolController.ActiveTool.IsUsing);
            var rinse = game.ToolController.ActiveTool.Definition;
            Assert.AreEqual(new Vector2(rinse.rumbleLow, rinse.rumbleHigh), game.Rumble.Current, "Vibration propre à la lance.");
            Assert.Greater(game.Rumble.Current.x, 0f);

            Assert.IsTrue(game.GameState.Pause());
            yield return null;
            Assert.AreEqual(Vector2.zero, game.Rumble.Current, "Pause : vibrations coupées.");
            game.GameState.Resume();
            // Comme le clic gauche, une gâchette maintenue pendant la pause doit être ré-appuyée.
            state.rightTrigger = 0f;
            Send();
            yield return null;
            state.rightTrigger = 1f;
            Send();
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(game.Rumble.Current.x, 0f);

            game.Settings.SetVibration(0f);
            yield return null;
            Assert.AreEqual(Vector2.zero, game.Rumble.Current, "Vibrations désactivées dans les paramètres.");
            game.Settings.SetVibration(1f);
            state.rightTrigger = 0f;
            Send();
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(Vector2.zero, game.Rumble.Current, "Gâchette relâchée : plus de vibration.");
        }

        [UnityTest]
        public IEnumerator SprintToggle_AndDeviceSwitch_BackToMouse()
        {
            yield return Stick(true, new Vector2(0f, 0.2f), 0.05f);
            state.leftStick = new Vector2(0f, 1f);
            Send();
            yield return Press(GamepadButton.LeftStick);
            yield return null;
            Assert.IsTrue(game.Input.SprintHeld, "Clic du stick gauche : course.");
            state.leftStick = Vector2.zero;
            Send();
            yield return null;
            yield return null;
            Assert.IsFalse(game.Input.SprintHeld, "La course s'arrête quand le joueur s'immobilise.");
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.Hud.HintText, Does.Contain("RT"));

            // La souris reprend la main : indications clavier/souris.
            mouse = InputSystem.AddDevice<Mouse>("RugWashTestMouse");
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(40f, 0f));
            yield return null;
            InputSystem.QueueDeltaStateEvent(mouse.delta, Vector2.zero);
            yield return null;
            Assert.AreEqual(InputScheme.KeyboardMouse, game.InputMonitor.Scheme, "Mouvement de souris : retour au clavier/souris.");
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.Hud.HintText, Does.Contain("Clic gauche"));
            Assert.That(game.Hud.OrderText, Does.Contain("(E)"));
        }

        [UnityTest]
        public IEnumerator OrderBoard_ScrollsToTheSelectedRow()
        {
            game.Reputation.Restore(10);
            yield return Stick(true, new Vector2(0.5f, 0f), 0.1f);
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            var scroll = game.OrderBoard.Rows[0].GetComponentInParent<ScrollRect>();
            float startY = scroll.content.anchoredPosition.y;
            var last = game.OrderBoard.Rows[game.OrderBoard.Rows.Count - 1].ActionButton.gameObject;
            for (int i = 0; i < 20 && Selected != last; i++)
                yield return Press(GamepadButton.DpadDown);
            Assert.AreSame(last, Selected, "La croix descend jusqu'à la dernière commande.");
            yield return null;
            Assert.Greater(scroll.content.anchoredPosition.y, startY + 50f, "La liste a défilé.");
            var viewport = scroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, last.transform);
            Assert.GreaterOrEqual(bounds.min.y, viewport.rect.yMin - 1f, "La ligne sélectionnée est visible.");
            yield return Press(GamepadButton.East);
            Assert.IsFalse(game.OrderBoard.IsOpen);
        }

        [UnityTest]
        public IEnumerator MainMenu_IsFullyUsableWithAGamepad()
        {
            // Profil vierge : avec une partie commencée, « Nouvelle partie » demande d'abord une confirmation.
            TestSaveSandbox.Reset(legacyTools: false);
            yield return Load("MainMenu");
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsNotNull(menu);
            yield return Press(GamepadButton.DpadDown);
            yield return null;
            Assert.AreEqual(InputScheme.Gamepad, menu.InputMonitor.Scheme);
            Assert.IsFalse(Cursor.visible, "Manette : curseur masqué.");

            for (int i = 0; i < 9 && Selected != menu.RugDexButton.gameObject; i++)
                yield return Press(GamepadButton.DpadDown);
            Assert.AreSame(menu.RugDexButton.gameObject, Selected);
            yield return Press(GamepadButton.South);
            Assert.IsTrue(menu.RugDex.IsOpen, "A ouvre le RugDex.");
            yield return null;
            Assert.IsTrue(Selected.transform.IsChildOf(menu.RugDex.transform), "Focus dans le RugDex.");
            // La fiche suit le focus : passer à la carte voisine l'affiche.
            string detail = menu.RugDex.DetailText;
            yield return Press(GamepadButton.DpadRight);
            Assert.AreNotEqual(detail, menu.RugDex.DetailText, "Déplacer le focus change la fiche affichée.");
            yield return Press(GamepadButton.East);
            Assert.IsFalse(menu.RugDex.IsOpen, "B ferme le RugDex.");
            yield return null;
            Assert.AreSame(menu.RugDexButton.gameObject, Selected, "Retour sur le bouton RugDex.");

            for (int i = 0; i < 6 && Selected != menu.PlayButton.gameObject; i++)
                yield return Press(GamepadButton.DpadUp);
            Assert.AreSame(menu.PlayButton.gameObject, Selected);
            yield return Press(GamepadButton.South);
            Assert.IsTrue(menu.IsLoading, "A sur « Nouvelle partie » démarre le chargement.");
            float end = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().name != "Workshop" && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual("Workshop", SceneManager.GetActiveScene().name, "A sur « Nouvelle partie » lance l'atelier.");
        }
        // ------------------------------------------------------------------ Captures

        [UnityTest, Category("Capture")]
        public IEnumerator Capture_GamepadScreens()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Pas de GPU.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ci", "screens");
            Directory.CreateDirectory(folder);
            var camera = Camera.main;
            Canvas hud = null;
            foreach (var canvas in Object.FindObjectsByType<Canvas>())
                if (canvas.name == "HUD Canvas") hud = canvas;

            yield return Stick(true, new Vector2(0.5f, 0f), 0.1f);
            game.ToolController.SelectSlot(3);
            AimAtRug(new Vector2(0.5f, 0.5f));
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Capture(camera, hud, Path.Combine(folder, "50_hud_gamepad.png"));

            game.Reputation.Restore(10);
            Assert.IsTrue(game.OrderBoard.Open());
            yield return null;
            for (int i = 0; i < 7; i++) yield return Press(GamepadButton.DpadDown);
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "51_order_board_focus.png"));
            yield return Press(GamepadButton.East);

            yield return Press(GamepadButton.Start);
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "52_pause_gamepad.png"));
            for (int i = 0; i < 10 && Selected != game.PauseMenu.SettingsButton.gameObject; i++)
                yield return Press(GamepadButton.DpadDown);
            yield return Press(GamepadButton.South);
            yield return Press(GamepadButton.RightShoulder);
            yield return Press(GamepadButton.DpadDown);
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "53_settings_controls.png"));
            yield return Press(GamepadButton.RightShoulder);
            yield return null;
            yield return Capture(camera, hud, Path.Combine(folder, "54_settings_bindings.png"));
            yield return Press(GamepadButton.East);
            yield return Press(GamepadButton.East);
        }

        static IEnumerator Capture(Camera camera, Canvas canvas, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.2f;
            // Les couleurs de sélection apparaissent en fondu (0,05 s) : la capture montre l'état final.
            yield return new WaitForSecondsRealtime(0.12f);
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
