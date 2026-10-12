using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Ligne de reconfiguration : action, touche clavier (« Modifier »), bouton de manette (« Modifier »).</summary>
    [DisallowMultipleComponent]
    public sealed class RebindRowView : MonoBehaviour
    {
        [SerializeField] Text actionLabel;
        [SerializeField] Text bindingLabel;
        [SerializeField] Button changeButton;
        [SerializeField] Text gamepadLabel;
        [SerializeField] Button gamepadButton;

        public Button ChangeButton => changeButton;
        public Button GamepadButton => gamepadButton;
        public string BindingText => bindingLabel.text;
        public string GamepadText => gamepadLabel != null ? gamepadLabel.text : string.Empty;

        public void Set(string action, string binding)
        {
            actionLabel.text = action;
            bindingLabel.text = binding;
        }

        public void SetGamepad(string binding)
        {
            if (gamepadLabel != null) gamepadLabel.text = binding;
        }

        /// <summary>Masque un bouton « Modifier » quand la liaison n'est pas reconfigurable (clic gauche, stick).</summary>
        public void SetRebindable(bool keyboard, bool gamepad)
        {
            changeButton.gameObject.SetActive(keyboard);
            if (gamepadButton != null) gamepadButton.gameObject.SetActive(gamepad);
        }
    }
}
