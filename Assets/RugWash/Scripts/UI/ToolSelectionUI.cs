using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Barre d'outils : un emplacement par outil réellement possédé (déblocages de la sauvegarde, ou de l'atelier de l'hôte
    /// en ligne), dans l'ordre des touches ; l'outil actif est mis en évidence. Les outils pas encore achetés n'y figurent
    /// pas : elle est reconstruite à chaque achat ou changement d'équipement (y compris après une reconnexion).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolSelectionUI : MonoBehaviour
    {
        [SerializeField] List<Image> slotBackgrounds = new List<Image>();
        [SerializeField] List<Text> slotLabels = new List<Text>();
        [SerializeField] Color normalColor = new Color(0.08f, 0.1f, 0.12f, 0.72f);
        [SerializeField] Color activeColor = new Color(0.16f, 0.55f, 0.72f, 0.92f);


        PlayerToolController tools;

        public bool IsConfigured(out string missing)
        {
            missing = slotBackgrounds.Count == 0 ? nameof(slotBackgrounds)
                : slotLabels.Count != slotBackgrounds.Count ? nameof(slotLabels) : null;
            return missing == null;
        }

        public void Bind(PlayerToolController toolController)
        {
            tools = toolController;
            tools.ActiveToolChanged += OnActiveToolChanged;
            tools.SuspendedChanged += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (tools == null) return;
            tools.ActiveToolChanged -= OnActiveToolChanged;
            tools.SuspendedChanged -= Rebuild;
        }

        void OnActiveToolChanged(CleaningToolBase _) => Rebuild();

        /// <summary>À appeler après un achat (outil débloqué).</summary>
        public void Refresh() => Rebuild();

        readonly List<CleaningToolBase> owned = new List<CleaningToolBase>();

        /// <summary>Outils affichés (possédés), dans l'ordre de la barre.</summary>
        public IReadOnlyList<CleaningToolBase> Shown => owned;

        /// <summary>Couleur de fond de l'emplacement affiché n° <paramref name="index"/> (surbrillance de l'outil actif).</summary>
        public Color SlotColor(int index) => slotBackgrounds[index].color;

        void Rebuild()
        {
            owned.Clear();
            foreach (var tool in tools.Tools)
                if (tool != null && tool.IsUnlocked) owned.Add(tool);
            for (int i = 0; i < slotBackgrounds.Count; i++)
            {
                bool used = i < owned.Count;
                slotBackgrounds[i].gameObject.SetActive(used);
                if (!used) continue;
                var tool = owned[i];
                string key = Player.RugWashInputActions.SlotKeyLabel(tool.Definition.slot);
                slotLabels[i].text = key.Length > 0 ? $"{key}  {tool.Definition.displayName}" : tool.Definition.displayName;
                slotLabels[i].color = Color.white;
                slotBackgrounds[i].color = tool == tools.ActiveTool && !tools.Suspended ? activeColor : normalColor;
            }
        }
    }
}
