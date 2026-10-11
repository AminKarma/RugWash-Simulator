using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Ligne de liste générique (commande, amélioration) : titre, détails, statut et bouton d'action.</summary>
    [DisallowMultipleComponent]
    public sealed class ListRowView : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] Text titleLabel;
        [SerializeField] Text detailsLabel;
        [SerializeField] Text statusLabel;
        [SerializeField] Button actionButton;
        [SerializeField] Text actionLabel;

        public Button ActionButton => actionButton;

        public void Set(string title, string details, string status, Color statusColor, string action, bool actionInteractable, Color background)
        {
            titleLabel.text = title;
            detailsLabel.text = details;
            statusLabel.text = status;
            statusLabel.color = statusColor;
            this.background.color = background;
            actionButton.gameObject.SetActive(!string.IsNullOrEmpty(action));
            actionLabel.text = action ?? string.Empty;
            actionButton.interactable = actionInteractable;
        }
    }
}
