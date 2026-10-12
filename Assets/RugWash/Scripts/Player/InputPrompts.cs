using RugWash.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Player
{
    /// <summary>Libellés des touches et boutons (règles pures, testées).</summary>
    public static class InputPrompts
    {
        /// <summary>Libellé de la première liaison de l'action appartenant au groupe du schéma (« ? » si aucune).</summary>
        public static string Label(InputAction action, InputScheme scheme, GamepadStyle style)
        {
            if (action == null) return "?";
            int index = BindingIndex(action, InputSchemes.GroupOf(scheme));
            if (index < 0) return "?";
            if (scheme == InputScheme.Gamepad) return GamepadControl(action.bindings[index].effectivePath, style);
            return action.GetBindingDisplayString(index);
        }

        /// <summary>Index de la première liaison simple (hors composite) du groupe ; -1 si aucune.</summary>
        public static int BindingIndex(InputAction action, string group)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                if (InGroup(binding, group)) return i;
            }
            return -1;
        }

        public static bool InGroup(InputBinding binding, string group) =>
            !string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(group);

        /// <summary>Nom affiché d'une commande de manette (« &lt;Gamepad&gt;/buttonSouth » → « A » ou « Croix »).</summary>
        public static string GamepadControl(string path, GamepadStyle style)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            bool ps = style == GamepadStyle.PlayStation;
            int slash = path.IndexOf('/');
            string control = slash >= 0 ? path.Substring(slash + 1) : path;
            switch (control)
            {
                case "buttonSouth": return ps ? "Croix" : "A";
                case "buttonEast": return ps ? "Rond" : "B";
                case "buttonWest": return ps ? "Carré" : "X";
                case "buttonNorth": return ps ? "Triangle" : "Y";
                case "leftShoulder": return ps ? "L1" : "LB";
                case "rightShoulder": return ps ? "R1" : "RB";
                case "leftTrigger": return ps ? "L2" : "LT";
                case "rightTrigger": return ps ? "R2" : "RT";
                case "start": return ps ? "Options" : "Menu";
                case "select": return ps ? "Partager" : "Vue";
                case "leftStickPress": return ps ? "L3" : "LS";
                case "rightStickPress": return ps ? "R3" : "RS";
                case "leftStick": return "Stick gauche";
                case "rightStick": return "Stick droit";
                case "dpad/up": return "Croix ↑";
                case "dpad/down": return "Croix ↓";
                case "dpad/left": return "Croix ←";
                case "dpad/right": return "Croix →";
                default: return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            }
        }

        /// <summary>Touche « retour » de l'interface (non reconfigurable) : Échap ou B / Rond.</summary>
        public static string Back(InputScheme scheme, GamepadStyle style) =>
            scheme == InputScheme.Gamepad ? GamepadControl("<Gamepad>/buttonEast", style) : "Échap";

        /// <summary>Touche « valider » de l'interface : clic / Entrée ou A / Croix.</summary>
        public static string Submit(InputScheme scheme, GamepadStyle style) =>
            scheme == InputScheme.Gamepad ? GamepadControl("<Gamepad>/buttonSouth", style) : "Entrée";
    }

    /// <summary>Réponse des sticks : zone morte radiale, puis courbe (précision au centre, pleine vitesse au bord).</summary>
    public static class StickResponse
    {
        public static Vector2 Apply(Vector2 raw, float deadZone, float exponent)
        {
            float magnitude = raw.magnitude;
            deadZone = Mathf.Clamp(deadZone, 0f, 0.95f);
            if (magnitude <= deadZone || magnitude <= 0f) return Vector2.zero;
            float t = Mathf.Clamp01((Mathf.Min(magnitude, 1f) - deadZone) / (1f - deadZone));
            return raw / magnitude * Mathf.Pow(t, Mathf.Max(0.1f, exponent));
        }
    }
}
