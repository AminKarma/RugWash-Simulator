using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Ligne de liste générique (commande, amélioration) : titre, détails, statut et bouton d'action. Grande mise à jour :
    /// icône 3D facultative (outils) et hauteur ajustée au texte des détails (plus aucun texte qui déborde de la ligne).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ListRowView : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] Text titleLabel;
        [SerializeField] Text detailsLabel;
        [SerializeField] Text statusLabel;
        [SerializeField] Button actionButton;
        [SerializeField] Text actionLabel;
        [Tooltip("Icône de l'outil (facultative) ; sans icône, le texte reprend sa place.")]
        [SerializeField] RawImage icon;
        [SerializeField] float iconIndent = 92f;
        [Tooltip("Hauteur ajustée au texte des détails (au moins la hauteur de départ).")]
        [SerializeField] bool fitHeight;
        [SerializeField] LayoutElement layout;

        float baseHeight = -1f, titleX, detailsX, titleWidth, detailsWidth;

        public Button ActionButton => actionButton;
        public string StatusText => statusLabel.text;
        public bool HasIcon => icon != null && icon.gameObject.activeSelf;
        public float PreferredHeight => layout != null ? layout.preferredHeight : ((RectTransform)transform).rect.height;
        public Text DetailsLabel => detailsLabel;

        void CaptureLayout()
        {
            if (baseHeight >= 0f) return;
            baseHeight = layout != null ? layout.preferredHeight : ((RectTransform)transform).rect.height;
            titleX = titleLabel.rectTransform.anchoredPosition.x;
            detailsX = detailsLabel.rectTransform.anchoredPosition.x;
            titleWidth = titleLabel.rectTransform.sizeDelta.x;
            detailsWidth = detailsLabel.rectTransform.sizeDelta.x;
        }

        /// <summary>Icône 3D (case de l'atlas des icônes) ; null : pas d'icône.</summary>
        public void SetIcon(Texture texture, Rect uv)
        {
            if (icon == null) return;
            CaptureLayout();
            bool shown = texture != null;
            icon.gameObject.SetActive(shown);
            icon.texture = texture;
            icon.uvRect = uv;
            float indent = shown ? iconIndent : 0f;
            // Le texte se décale ET se rétrécit d'autant : il ne passe jamais sous le statut ni sous le bouton.
            var title = titleLabel.rectTransform;
            title.anchoredPosition = new Vector2(titleX + indent, title.anchoredPosition.y);
            title.sizeDelta = new Vector2(titleWidth - indent, title.sizeDelta.y);
            var details = detailsLabel.rectTransform;
            details.anchoredPosition = new Vector2(detailsX + indent, details.anchoredPosition.y);
            details.sizeDelta = new Vector2(detailsWidth - indent, details.sizeDelta.y);
            FitHeight();
        }

        public void ClearIcon() => SetIcon(null, default);

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
            FitHeight();
        }

        /// <summary>La ligne grandit pour contenir tout le texte des détails (largeur de leur cadre).</summary>
        void FitHeight()
        {
            if (!fitHeight || layout == null) return;
            CaptureLayout();
            float top = -detailsLabel.rectTransform.anchoredPosition.y;
            float needed = top + detailsLabel.preferredHeight + 12f;
            float height = Mathf.Max(baseHeight, Mathf.Ceil(needed));
            layout.preferredHeight = height;
            layout.minHeight = height;
            var details = detailsLabel.rectTransform;
            details.sizeDelta = new Vector2(details.sizeDelta.x, height - top - 6f);
        }
    }
}
