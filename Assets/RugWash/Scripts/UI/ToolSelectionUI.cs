using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Barre d'outils : un emplacement par outil, l'outil actif est mis en évidence, les outils pas encore achetés
    /// sont grisés avec la mention « boutique ».
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolSelectionUI : MonoBehaviour
    {
        [SerializeField] List<Image> slotBackgrounds = new List<Image>();
        [SerializeField] List<Text> slotLabels = new List<Text>();
        [SerializeField] Color normalColor = new Color(0.08f, 0.1f, 0.12f, 0.72f);
        [SerializeField] Color activeColor = new Color(0.16f, 0.55f, 0.72f, 0.92f);
        [SerializeField] Color lockedColor = new Color(0.05f, 0.05f, 0.06f, 0.5f);
        [SerializeField] Color lockedTextColor = new Color(1f, 1f, 1f, 0.45f);

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
            Rebuild();
        }

        void OnDestroy()
        {
            if (tools != null) tools.ActiveToolChanged -= OnActiveToolChanged;
        }

        void OnActiveToolChanged(CleaningToolBase _) => Rebuild();

        /// <summary>À appeler après un achat (outil débloqué).</summary>
        public void Refresh() => Rebuild();

        void Rebuild()
        {
            var list = tools.Tools;
            for (int i = 0; i < slotBackgrounds.Count; i++)
            {
                bool used = i < list.Count;
                slotBackgrounds[i].gameObject.SetActive(used);
                if (!used) continue;
                var tool = list[i];
                string key = Player.RugWashInputActions.SlotKeyLabel(tool.Definition.slot);
                string name = key.Length > 0 ? $"{key}  {tool.Definition.displayName}" : tool.Definition.displayName;
                slotLabels[i].text = tool.IsUnlocked ? name : $"{name} (boutique)";
                slotLabels[i].color = tool.IsUnlocked ? Color.white : lockedTextColor;
                slotBackgrounds[i].color = !tool.IsUnlocked ? lockedColor : tool == tools.ActiveTool ? activeColor : normalColor;
            }
        }
    }
}
