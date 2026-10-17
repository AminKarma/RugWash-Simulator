using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Ligne de la fenêtre « Joueurs » : nom et rôle, état de la voix, couper sa voix, expulser (hôte).</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRowView : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] Text nameLabel;
        [SerializeField] Text statusLabel;
        [SerializeField] Button muteButton;
        [SerializeField] Text muteLabel;
        [SerializeField] Button kickButton;

        public Button MuteButton => muteButton;
        public Button KickButton => kickButton;
        public string NameText => nameLabel.text;
        public string StatusText => statusLabel.text;
        public string MuteText => muteLabel.text;

        public void Set(string name, string status, Color statusColor, bool canMute, string muteText, bool canKick, Color color)
        {
            nameLabel.text = name;
            statusLabel.text = status;
            statusLabel.color = statusColor;
            muteButton.gameObject.SetActive(canMute);
            muteLabel.text = muteText;
            kickButton.gameObject.SetActive(canKick);
            if (background != null) background.color = color;
        }
    }
}
