using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Player;
using RugWash.Pranks;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Barre d'outils : les emplacements de l'inventaire (grande mise à jour : quatre, touches 1 à 4) avec l'outil ou le
    /// gadget porté dans chacun ; l'objet en main est mis en évidence. Sans inventaire (scènes anciennes), un emplacement par
    /// outil possédé. Reconstruite à chaque achat, changement d'inventaire ou d'équipement (y compris après une reconnexion).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolSelectionUI : MonoBehaviour
    {
        [SerializeField] List<Image> slotBackgrounds = new List<Image>();
        [SerializeField] List<Text> slotLabels = new List<Text>();
        [Tooltip("Icône 3D de l'objet de chaque emplacement (grande mise à jour).")]
        [SerializeField] List<RawImage> slotIcons = new List<RawImage>();
        [SerializeField] Color normalColor = UITheme.SlotNormal;
        [SerializeField] Color activeColor = UITheme.SlotActive;
        [SerializeField] Color emptyColor = UITheme.SlotEmpty;

        PlayerToolController tools;
        PlayerInventory inventory;
        PrankGadgetController gadgets;
        Effects.ToolIconRenderer icons;

        /// <summary>Icônes 3D des outils et gadgets (atlas commun) ; la barre se reconstruit quand elles changent.</summary>
        public void BindIcons(Effects.ToolIconRenderer renderer)
        {
            icons = renderer;
            if (icons != null) icons.Changed += Rebuild;
            Rebuild();
        }

        void SetIcon(int index, string id)
        {
            if (index >= slotIcons.Count || slotIcons[index] == null) return;
            bool shown = icons != null && !string.IsNullOrEmpty(id) && icons.TryGetIcon(id, out _);
            slotIcons[index].gameObject.SetActive(shown);
            if (!shown) return;
            icons.TryGetIcon(id, out Rect uv);
            slotIcons[index].texture = icons.Atlas;
            slotIcons[index].uvRect = uv;
        }

        public bool SlotHasIcon(int index) => index < slotIcons.Count && slotIcons[index] != null && slotIcons[index].gameObject.activeSelf;

        public bool IsConfigured(out string missing)
        {
            missing = slotBackgrounds.Count == 0 ? nameof(slotBackgrounds)
                : slotLabels.Count != slotBackgrounds.Count ? nameof(slotLabels) : null;
            return missing == null;
        }

        public void Bind(PlayerToolController toolController, PlayerInventory playerInventory = null, PrankGadgetController gadgetController = null)
        {
            tools = toolController;
            inventory = playerInventory;
            gadgets = gadgetController;
            tools.ActiveToolChanged += OnActiveToolChanged;
            tools.SuspendedChanged += Rebuild;
            if (inventory != null) inventory.Changed += Rebuild;
            if (gadgets != null) gadgets.EquippedChanged += OnGadgetChanged;
            Rebuild();
        }

        void OnDestroy()
        {
            if (tools == null) return;
            tools.ActiveToolChanged -= OnActiveToolChanged;
            tools.SuspendedChanged -= Rebuild;
            if (inventory != null) inventory.Changed -= Rebuild;
            if (gadgets != null) gadgets.EquippedChanged -= OnGadgetChanged;
            if (icons != null) icons.Changed -= Rebuild;
        }

        void OnActiveToolChanged(CleaningToolBase _) => Rebuild();
        void OnGadgetChanged(PrankGadget _) => Rebuild();

        /// <summary>À appeler après un achat (outil débloqué).</summary>
        public void Refresh() => Rebuild();

        readonly List<CleaningToolBase> owned = new List<CleaningToolBase>();

        /// <summary>Outils affichés (portés, ou possédés sans inventaire), dans l'ordre de la barre.</summary>
        public IReadOnlyList<CleaningToolBase> Shown => owned;

        /// <summary>Couleur de fond de l'emplacement affiché n° <paramref name="index"/> (surbrillance de l'objet en main).</summary>
        public Color SlotColor(int index) => slotBackgrounds[index].color;

        /// <summary>Texte de l'emplacement n° <paramref name="index"/>.</summary>
        public string SlotText(int index) => slotLabels[index].text;

        void Rebuild()
        {
            if (tools == null) return;
            owned.Clear();
            if (inventory != null) RebuildInventory();
            else RebuildOwnedTools();
        }

        void RebuildInventory()
        {
            for (int i = 0; i < slotBackgrounds.Count; i++)
            {
                bool slotExists = i < PlayerInventory.Capacity;
                slotBackgrounds[i].gameObject.SetActive(slotExists);
                if (!slotExists) continue;
                var item = inventory.ItemAt(i);
                string key = RugWashInputActions.SlotKeyLabel(i + 1);
                SetIcon(i, item.IsValid ? item.Id : null);
                if (!item.IsValid)
                {
                    slotLabels[i].text = $"{key}  —";
                    slotLabels[i].color = new Color(1f, 1f, 1f, 0.45f);
                    slotBackgrounds[i].color = emptyColor;
                    continue;
                }
                if (item.IsTool) owned.Add(item.Tool);
                bool active = item.IsTool
                    ? item.Tool == tools.ActiveTool && !tools.Suspended
                    : gadgets != null && gadgets.Equipped == item.Gadget;
                bool gold = item.IsTool && item.Tool.IsGolden;
                slotLabels[i].text = gold ? $"{key}  {item.DisplayName} ★" : $"{key}  {item.DisplayName}";
                slotLabels[i].color = gold ? GoldText : Color.white;
                slotBackgrounds[i].color = active ? activeColor : normalColor;
            }
        }

        /// <summary>Outil doré (niveau maximal) : nom en or dans la barre.</summary>
        static readonly Color GoldText = UITheme.Gold;

        void RebuildOwnedTools()
        {
            foreach (var tool in tools.Tools)
                if (tool != null && tool.IsUnlocked) owned.Add(tool);
            for (int i = 0; i < slotBackgrounds.Count; i++)
            {
                bool used = i < owned.Count;
                slotBackgrounds[i].gameObject.SetActive(used);
                if (!used) continue;
                var tool = owned[i];
                SetIcon(i, tool.Definition.id);
                string key = RugWashInputActions.SlotKeyLabel(tool.Definition.slot);
                slotLabels[i].text = key.Length > 0 ? $"{key}  {tool.Definition.displayName}" : tool.Definition.displayName;
                slotLabels[i].color = Color.white;
                slotBackgrounds[i].color = tool == tools.ActiveTool && !tools.Suspended ? activeColor : normalColor;
            }
        }
    }
}
