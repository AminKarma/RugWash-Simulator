using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>Barre d'outils : un emplacement par outil disponible, l'outil actif est mis en évidence.</summary>
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
            Rebuild();
        }

        void OnDestroy()
        {
            if (tools != null) tools.ActiveToolChanged -= OnActiveToolChanged;
        }

        void OnActiveToolChanged(CleaningToolBase _) => Rebuild();

        void Rebuild()
        {
            var list = tools.Tools;
            for (int i = 0; i < slotBackgrounds.Count; i++)
            {
                bool used = i < list.Count;
                slotBackgrounds[i].gameObject.SetActive(used);
                if (!used) continue;
                var tool = list[i];
                slotLabels[i].text = $"{tool.Definition.slot}  {tool.Definition.displayName}";
                slotBackgrounds[i].color = tool == tools.ActiveTool ? activeColor : normalColor;
            }
        }
    }
}
