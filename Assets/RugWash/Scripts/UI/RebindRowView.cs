using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Ligne de reconfiguration d'une touche : action, touche actuelle, bouton « Modifier ».</summary>
    [DisallowMultipleComponent]
    public sealed class RebindRowView : MonoBehaviour
    {
        [SerializeField] Text actionLabel;
        [SerializeField] Text bindingLabel;
        [SerializeField] Button changeButton;

        public Button ChangeButton => changeButton;
        public string BindingText => bindingLabel.text;

        public void Set(string action, string binding)
        {
            actionLabel.text = action;
            bindingLabel.text = binding;
        }
    }
}
