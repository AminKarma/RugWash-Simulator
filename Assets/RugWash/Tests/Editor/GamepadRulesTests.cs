using System.Collections.Generic;
using NUnit.Framework;
using RugWash.Core;
using RugWash.Effects;
using RugWash.Player;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Tests
{
    /// <summary>Jalon 4 — manette : règles pures (sticks, liaisons, libellés, vibrations, paramètres).</summary>
    public class GamepadRulesTests
    {
        InputActionAsset asset;

        [SetUp]
        public void CreateActions() => asset = RugWashInputActions.Create();

        [TearDown]
        public void DestroyActions() => Object.DestroyImmediate(asset);

        // ------------------------------------------------------------------ Sticks

        [Test]
        public void StickResponse_IgnoresTheDeadZone_KeepsDirection_AndReachesFullSpeed()
        {
            Assert.AreEqual(Vector2.zero, StickResponse.Apply(new Vector2(0.1f, 0.05f), 0.15f, 1f), "Sous la zone morte : rien.");
            Assert.AreEqual(Vector2.zero, StickResponse.Apply(Vector2.zero, 0.15f, 2f));
            Vector2 full = StickResponse.Apply(new Vector2(0f, 1f), 0.15f, 2f);
            Assert.AreEqual(1f, full.magnitude, 1e-4f, "Stick à fond : pleine vitesse malgré la zone morte.");
            Vector2 diagonal = StickResponse.Apply(new Vector2(0.6f, 0.6f), 0.15f, 1f);
            Assert.AreEqual(0f, Vector2.Angle(diagonal, Vector2.one), 1e-3f, "Direction conservée.");
            Assert.LessOrEqual(StickResponse.Apply(new Vector2(1f, 1f), 0.15f, 1f).magnitude, 1f + 1e-5f, "Jamais plus que 1.");
            // À mi-course utile, la courbe quadratique donne un quart de la vitesse (précision au centre).
            float half = 0.15f + 0.85f * 0.5f;
            Assert.AreEqual(0.25f, StickResponse.Apply(new Vector2(half, 0f), 0.15f, 2f).x, 1e-4f);
            Assert.AreEqual(0.5f, StickResponse.Apply(new Vector2(half, 0f), 0.15f, 1f).x, 1e-4f);
            Assert.Greater(StickResponse.Apply(new Vector2(0.3f, 0f), 0.05f, 1f).x, StickResponse.Apply(new Vector2(0.3f, 0f), 0.25f, 1f).x,
                "Une zone morte plus grande réduit la réponse.");
        }

        // ------------------------------------------------------------------ Liaisons

        [Test]
        public void EveryGameplayAction_IsReachableWithAGamepad()
        {
            string[] actions = { "MoveStick", "LookStick", "Sprint", "Use", "Interact", "Scan", "Product", "ToolNext", "ToolPrevious", "Pause", "Inspect", "Back" };
            foreach (var name in actions)
            {
                var action = asset.FindAction(name, true);
                Assert.GreaterOrEqual(InputPrompts.BindingIndex(action, InputSchemes.GamepadGroup), 0, $"{name} : aucune liaison manette.");
            }
        }

        [Test]
        public void DefaultGamepadBindings_AreUnique()
        {
            var seen = new Dictionary<string, string>();
            foreach (var action in asset)
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (!InputPrompts.InGroup(binding, InputSchemes.GamepadGroup)) continue;
                    Assert.IsFalse(seen.ContainsKey(binding.path), $"{binding.path} utilisé par {action.name} et {(seen.ContainsKey(binding.path) ? seen[binding.path] : "")}");
                    seen[binding.path] = action.name;
                }
            Assert.GreaterOrEqual(seen.Count, 12);
        }

        [Test]
        public void KeyboardBindingIndices_AreUnchanged_SoSavedRebindsStillApply()
        {
            // Index des liaisons clavier avant le jalon 4 (les touches reconfigurées sauvegardées les désignent).
            Assert.AreEqual("<Keyboard>/w", asset.FindAction("Move").bindings[1].path);
            Assert.AreEqual("<Keyboard>/upArrow", asset.FindAction("Move").bindings[6].path);
            foreach (var (name, path) in new[] { ("Sprint", "<Keyboard>/leftShift"), ("Use", "<Mouse>/leftButton"), ("Interact", "<Keyboard>/e"),
                         ("Scan", "<Keyboard>/r"), ("Product", "<Keyboard>/t"), ("Pause", "<Keyboard>/escape"), ("Inspect", "<Keyboard>/f"),
                         ("Look", "<Mouse>/delta"), ("ToolScroll", "<Mouse>/scroll/y"), ("Tool1", "<Keyboard>/1") })
                Assert.AreEqual(path, asset.FindAction(name).bindings[0].path, name);

            var saved = new BindingOverrideSet();
            saved.entries.Add(new BindingOverrideSet.Entry { action = "Interact", binding = 0, path = "<Keyboard>/g" });
            Assert.AreEqual(1, saved.ApplyTo(asset));
            Assert.AreEqual("<Keyboard>/g", asset.FindAction("Interact").bindings[0].effectivePath);
            Assert.AreEqual("<Gamepad>/buttonSouth", asset.FindAction("Interact").bindings[1].effectivePath, "La liaison manette n'est pas touchée.");
        }

        [Test]
        public void RebindableRows_HaveTheirBindings_InBothColumns()
        {
            var rows = SettingsManager.RebindableActions;
            Assert.AreEqual("Avancer", rows[0].Label, "Les lignes existantes gardent leur position.");
            Assert.AreEqual("Inspecter", rows[8].Label);
            foreach (var row in rows)
            {
                int keyboard = SettingsManager.FindBindingIndex(asset.FindAction(row.ActionName), row);
                if (row.KeyboardRebindable) Assert.GreaterOrEqual(keyboard, 0, $"{row.Label} : liaison clavier.");
                else Assert.IsFalse(string.IsNullOrEmpty(row.KeyboardHint), $"{row.Label} : indication clavier fixe.");
                int pad = SettingsManager.FindBindingIndex(asset.FindAction(row.GamepadActionName), row, InputSchemes.GamepadGroup);
                Assert.GreaterOrEqual(pad, 0, $"{row.Label} : liaison manette affichée.");
            }
            var forward = rows[0];
            Assert.AreEqual(-1, SettingsManager.FindBindingIndex(asset.FindAction("Move"), forward, InputSchemes.GamepadGroup));
            Assert.IsFalse(forward.GamepadRebindable, "Le stick de déplacement n'est pas reconfigurable par direction.");
            Assert.IsFalse(rows[9].KeyboardRebindable, "Le clic gauche reste fixe.");
            Assert.IsTrue(rows[9].GamepadRebindable, "La gâchette d'utilisation est reconfigurable.");
        }

        // ------------------------------------------------------------------ Libellés

        [Test]
        public void GamepadLabels_FollowTheControllerFamily()
        {
            Assert.AreEqual("A", InputPrompts.GamepadControl("<Gamepad>/buttonSouth", GamepadStyle.Xbox));
            Assert.AreEqual("Croix", InputPrompts.GamepadControl("<Gamepad>/buttonSouth", GamepadStyle.PlayStation));
            Assert.AreEqual("B", InputPrompts.GamepadControl("<Gamepad>/buttonEast", GamepadStyle.Xbox));
            Assert.AreEqual("Rond", InputPrompts.GamepadControl("<Gamepad>/buttonEast", GamepadStyle.PlayStation));
            Assert.AreEqual("RT", InputPrompts.GamepadControl("<Gamepad>/rightTrigger", GamepadStyle.Xbox));
            Assert.AreEqual("R2", InputPrompts.GamepadControl("<Gamepad>/rightTrigger", GamepadStyle.PlayStation));
            Assert.AreEqual("LB", InputPrompts.GamepadControl("<Gamepad>/leftShoulder", GamepadStyle.Xbox));
            Assert.AreEqual("Menu", InputPrompts.GamepadControl("<Gamepad>/start", GamepadStyle.Xbox));
            Assert.AreEqual("Options", InputPrompts.GamepadControl("<Gamepad>/start", GamepadStyle.PlayStation));
            Assert.AreEqual("Croix ↑", InputPrompts.GamepadControl("<Gamepad>/dpad/up", GamepadStyle.Xbox));
            Assert.IsFalse(string.IsNullOrEmpty(InputPrompts.GamepadControl("<Gamepad>/unknownThing", GamepadStyle.Xbox)));

            Assert.AreEqual("A", InputPrompts.Label(asset.FindAction("Interact"), InputScheme.Gamepad, GamepadStyle.Xbox));
            Assert.AreEqual("E", InputPrompts.Label(asset.FindAction("Interact"), InputScheme.KeyboardMouse, GamepadStyle.Xbox));
            Assert.AreEqual("Y", InputPrompts.Label(asset.FindAction("Scan"), InputScheme.Gamepad, GamepadStyle.Xbox));
            Assert.AreEqual("Triangle", InputPrompts.Label(asset.FindAction("Scan"), InputScheme.Gamepad, GamepadStyle.PlayStation));
            Assert.AreEqual("?", InputPrompts.Label(asset.FindAction("ToolNext"), InputScheme.KeyboardMouse, GamepadStyle.Xbox), "Pas de liaison clavier.");

            Assert.AreEqual("Fermer (Échap)", InputPromptText.Format("Fermer ({back})", InputScheme.KeyboardMouse, GamepadStyle.Xbox));
            Assert.AreEqual("Fermer (B)", InputPromptText.Format("Fermer ({back})", InputScheme.Gamepad, GamepadStyle.Xbox));
            Assert.AreEqual("Croix : valider", InputPromptText.Format("{submit} : valider", InputScheme.Gamepad, GamepadStyle.PlayStation));
        }

        [Test]
        public void Labels_FollowARebind()
        {
            var scan = asset.FindAction("Scan");
            scan.ApplyBindingOverride(InputPrompts.BindingIndex(scan, InputSchemes.GamepadGroup), "<Gamepad>/dpad/up");
            Assert.AreEqual("Croix ↑", InputPrompts.Label(scan, InputScheme.Gamepad, GamepadStyle.Xbox));
        }

        [Test]
        public void DeviceScheme_IsDetectedFromTheDevice()
        {
            var pad = InputSystem.AddDevice<Gamepad>("RugWashRulesGamepad");
            var keyboard = InputSystem.AddDevice<Keyboard>("RugWashRulesKeyboard");
            var mouse = InputSystem.AddDevice<Mouse>("RugWashRulesMouse");
            var dualShock = InputSystem.AddDevice<UnityEngine.InputSystem.DualShock.DualShock4GamepadHID>("RugWashRulesDualShock");
            try
            {
                Assert.AreEqual(InputScheme.Gamepad, InputSchemes.SchemeOf(pad));
                Assert.AreEqual(InputScheme.KeyboardMouse, InputSchemes.SchemeOf(keyboard));
                Assert.AreEqual(InputScheme.KeyboardMouse, InputSchemes.SchemeOf(mouse));
                Assert.IsNull(InputSchemes.SchemeOf(null));
                Assert.AreEqual(GamepadStyle.Xbox, InputSchemes.StyleOf(pad));
                Assert.AreEqual(GamepadStyle.PlayStation, InputSchemes.StyleOf(dualShock));
            }
            finally
            {
                InputSystem.RemoveDevice(pad);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(dualShock);
            }
        }

        // ------------------------------------------------------------------ Curseur, vibrations, paramètres

        [Test]
        public void Cursor_IsHiddenInMenus_OnlyWhenPlayingWithAGamepad()
        {
            Assert.IsTrue(GameStateRules.ShouldShowCursor(false, InputScheme.KeyboardMouse));
            Assert.IsFalse(GameStateRules.ShouldShowCursor(false, InputScheme.Gamepad));
            Assert.IsFalse(GameStateRules.ShouldShowCursor(true, InputScheme.KeyboardMouse), "Curseur verrouillé en jeu.");
        }

        [Test]
        public void Rumble_OnlyWithAGamepad_InGameplay_AndScaledByTheSetting()
        {
            var tool = new Vector2(0.2f, 0.1f);
            var pulse = new Vector2(0.5f, 0.05f);
            Assert.AreEqual(Vector2.zero, RumbleRules.Target(false, true, 1f, true, tool, pulse), "Clavier/souris : aucune vibration.");
            Assert.AreEqual(Vector2.zero, RumbleRules.Target(true, false, 1f, true, tool, pulse), "Pause ou menu : aucune vibration.");
            Assert.AreEqual(Vector2.zero, RumbleRules.Target(true, true, 0f, true, tool, pulse), "Vibrations désactivées.");
            Assert.AreEqual(Vector2.zero, RumbleRules.Target(true, true, 1f, false, tool, Vector2.zero), "Outil au repos.");
            Assert.AreEqual(tool, RumbleRules.Target(true, true, 1f, true, tool, Vector2.zero));
            Assert.AreEqual(new Vector2(0.5f, 0.1f), RumbleRules.Target(true, true, 1f, true, tool, pulse), "Maximum moteur par moteur.");
            Vector2 half = RumbleRules.Target(true, true, 0.5f, true, tool, Vector2.zero);
            Assert.AreEqual(0.1f, half.x, 1e-5f);
            Assert.AreEqual(0.05f, half.y, 1e-5f);
            Assert.LessOrEqual(RumbleRules.Target(true, true, 1f, true, new Vector2(3f, 3f), Vector2.zero).x, 1f);
        }

        [Test]
        public void GamepadSettings_AreClamped_AndOldSettingsGetDefaults()
        {
            var old = JsonUtility.FromJson<SettingsData>("{\"masterVolume\":0.5,\"mouseSensitivity\":0.2}");
            Assert.AreEqual(150f, old.gamepadLookSpeed, "Ancienne sauvegarde : vitesse par défaut.");
            Assert.AreEqual(0.15f, old.stickDeadZone, 1e-6f);
            Assert.AreEqual(1f, old.vibration);

            var broken = new SettingsData { gamepadLookSpeed = 5000f, stickDeadZone = -1f, vibration = 7f };
            SaveValidator.ClampSettings(broken);
            Assert.AreEqual(360f, broken.gamepadLookSpeed);
            Assert.AreEqual(0.02f, broken.stickDeadZone, 1e-6f);
            Assert.AreEqual(1f, broken.vibration);
        }
    }
}
