using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;

namespace RugWash.Core
{
    /// <summary>Périphérique de jeu actif : détermine les indications de touches affichées et le curseur.</summary>
    public enum InputScheme
    {
        KeyboardMouse,
        Gamepad,
    }

    /// <summary>Famille de manette, pour nommer ses boutons (A/B/X/Y ou Croix/Rond/Carré/Triangle).</summary>
    public enum GamepadStyle
    {
        Xbox,
        PlayStation,
    }

    /// <summary>Fournit le libellé de la touche d'une action, selon le périphérique actif.</summary>
    public interface IInputPrompts
    {
        /// <summary>Libellé court (« E », « A », « RT »…) de l'action pour le périphérique actif.</summary>
        string Label(string actionName);
    }

    /// <summary>Règles pures de détection (testables sans scène).</summary>
    public static class InputSchemes
    {
        /// <summary>Groupes de liaisons utilisés par les actions du jeu.</summary>
        public const string KeyboardMouseGroup = "Keyboard&Mouse";
        public const string GamepadGroup = "Gamepad";

        /// <summary>Schéma correspondant à un périphérique ; null s'il n'en change pas (périphérique inconnu).</summary>
        public static InputScheme? SchemeOf(InputDevice device)
        {
            if (device is Gamepad) return InputScheme.Gamepad;
            if (device is Keyboard || device is Mouse) return InputScheme.KeyboardMouse;
            return null;
        }

        public static GamepadStyle StyleOf(InputDevice device) => device is DualShockGamepad ? GamepadStyle.PlayStation : GamepadStyle.Xbox;

        public static string GroupOf(InputScheme scheme) => scheme == InputScheme.Gamepad ? GamepadGroup : KeyboardMouseGroup;
    }

    /// <summary>
    /// Suit le dernier périphérique réellement utilisé (toutes les actions du jeu et de l'interface) et signale
    /// les changements : clavier/souris ↔ manette, en cours de partie comme dans les menus. Composant de scène
    /// (sur l'EventSystem), transmis par référence à ceux qui en ont besoin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputDeviceMonitor : MonoBehaviour
    {
        [Tooltip("Déplacement minimal de la souris (pixels par frame) pour repasser au clavier/souris : ignore les micro-mouvements.")]
        [SerializeField, Min(0f)] float mouseMoveThreshold = 3f;

        public InputScheme Scheme { get; private set; } = InputScheme.KeyboardMouse;
        public GamepadStyle Style { get; private set; } = GamepadStyle.Xbox;
        public bool IsGamepad => Scheme == InputScheme.Gamepad;

        public event Action<InputScheme> SchemeChanged;

        void OnEnable() => InputSystem.onActionChange += OnActionChange;

        void OnDisable() => InputSystem.onActionChange -= OnActionChange;

        void OnActionChange(object target, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed && change != InputActionChange.ActionStarted) return;
            if (!(target is InputAction action)) return;
            var control = action.activeControl;
            if (control == null) return;
            var device = control.device;
            if (device is Mouse mouse && !(control is ButtonControl) && mouse.delta.ReadValue().sqrMagnitude < mouseMoveThreshold * mouseMoveThreshold)
                return;
            var scheme = InputSchemes.SchemeOf(device);
            if (scheme == null) return;
            if (scheme == InputScheme.Gamepad) Style = InputSchemes.StyleOf(device);
            SetScheme(scheme.Value);
        }

        /// <summary>Change le schéma actif (appelé par la détection ; public pour les cas sans action, ex. tests).</summary>
        public void SetScheme(InputScheme scheme)
        {
            if (scheme == Scheme) return;
            Scheme = scheme;
            SchemeChanged?.Invoke(scheme);
        }
    }
}
