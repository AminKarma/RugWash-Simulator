using RugWash.Core;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Texte contenant des touches d'interface, mis à jour selon le périphérique actif :
    /// « {back} » → Échap ou B / Rond, « {submit} » → Entrée ou A / Croix (ex. « Fermer ({back}) »).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class InputPromptText : MonoBehaviour
    {
        [SerializeField] InputDeviceMonitor monitor;
        [SerializeField] string template = "Fermer ({back})";

        Text text;

        public string Template
        {
            get => template;
            set
            {
                template = value;
                Refresh();
            }
        }

        void OnEnable()
        {
            Refresh();
            if (monitor != null) monitor.SchemeChanged += OnSchemeChanged;
        }

        void OnDisable()
        {
            if (monitor != null) monitor.SchemeChanged -= OnSchemeChanged;
        }

        void OnSchemeChanged(InputScheme _) => Refresh();

        public void Refresh()
        {
            if (text == null) text = GetComponent<Text>();
            var scheme = monitor != null ? monitor.Scheme : InputScheme.KeyboardMouse;
            var style = monitor != null ? monitor.Style : GamepadStyle.Xbox;
            text.text = Format(template, scheme, style);
        }

        public static string Format(string template, InputScheme scheme, GamepadStyle style) =>
            (template ?? string.Empty)
                .Replace("{back}", InputPrompts.Back(scheme, style))
                .Replace("{submit}", InputPrompts.Submit(scheme, style));
    }
}
