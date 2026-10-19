using System.Collections.Generic;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Casier de l'atelier (grande mise à jour) : à gauche les quatre emplacements portés (« Ranger »), à droite ce qui attend
    /// au casier (« Prendre »). Un objet est toujours soit porté, soit rangé : rien ne se duplique. Le pipi n'occupe jamais
    /// d'emplacement. En ligne, chaque joueur voit SES objets (son inventaire et son casier, sauvegardés chez lui).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockerUI : MonoBehaviour, IModalPanel
    {
        static readonly Color RowCarried = UITheme.RowActive;
        static readonly Color RowEmpty = UITheme.Row;
        static readonly Color RowStored = UITheme.Row;
        static readonly Color Ok = UITheme.Success;
        static readonly Color Warn = UITheme.Danger;
        static readonly Color Muted = UITheme.TextSecondary;

        [SerializeField] GameObject panel;
        [SerializeField] List<ListRowView> carriedRows = new List<ListRowView>();
        [SerializeField] Transform storedRoot;
        [SerializeField] ListRowView storedTemplate;
        [SerializeField] Text storedHeader;
        [SerializeField] Text noteLabel;
        [SerializeField] Text messageLabel;
        [SerializeField] Button closeButton;

        readonly List<ListRowView> storedRows = new List<ListRowView>();
        readonly List<string> storedIds = new List<string>();
        PlayerInventory inventory;
        GameStateManager gameState;
        Effects.ToolIconRenderer icons;

        /// <summary>Icônes 3D des objets (grande mise à jour).</summary>
        public void BindIcons(Effects.ToolIconRenderer renderer) => icons = renderer;

        void SetIcon(ListRowView row, string id)
        {
            if (icons != null && !string.IsNullOrEmpty(id) && icons.TryGetIcon(id, out Rect uv)) row.SetIcon(icons.Atlas, uv);
            else row.ClearIcon();
        }

        public bool IsOpen { get; private set; }
        public IReadOnlyList<ListRowView> CarriedRows => carriedRows;
        public IReadOnlyList<ListRowView> StoredRows => storedRows;
        public string Message => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : carriedRows.Count != PlayerInventory.Capacity ? nameof(carriedRows)
                : storedRoot == null ? nameof(storedRoot) : storedTemplate == null ? nameof(storedTemplate) : storedHeader == null ? nameof(storedHeader)
                : noteLabel == null ? nameof(noteLabel) : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(PlayerInventory playerInventory, GameStateManager state)
        {
            inventory = playerInventory;
            gameState = state;
            for (int i = 0; i < carriedRows.Count; i++)
            {
                int index = i;
                carriedRows[i].ActionButton.onClick.AddListener(() => StoreAt(index));
            }
            closeButton.onClick.AddListener(Close);
            storedTemplate.gameObject.SetActive(false);
            if (inventory != null) inventory.Changed += OnChanged;
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (inventory != null) inventory.Changed -= OnChanged;
        }

        void OnChanged()
        {
            if (IsOpen) Refresh();
        }

        public bool Open()
        {
            if (IsOpen || inventory == null || (gameState != null && !gameState.IsGameplayActive)) return false;
            messageLabel.text = string.Empty;
            panel.SetActive(true);
            IsOpen = true;
            gameState?.PushModal();
            Refresh();
            carriedRows[0].ActionButton.Select();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            gameState?.PopModal();
        }

        public void Refresh()
        {
            for (int i = 0; i < carriedRows.Count; i++)
            {
                var item = inventory.ItemAt(i);
                string key = RugWashInputActions.SlotKeyLabel(i + 1);
                SetIcon(carriedRows[i], item.IsValid ? item.Id : null);
                if (!item.IsValid)
                {
                    carriedRows[i].Set($"Emplacement {key} — libre", "Prenez un objet au casier pour l'utiliser.", "Vide", Muted, null, false, RowEmpty);
                    continue;
                }
                string kind = item.IsTool ? "Outil de nettoyage" : "Gadget de farce";
                carriedRows[i].Set($"{key}  {item.DisplayName}", $"{kind} — sur vous (touche {key}).", "Porté", Ok, "Ranger", true, RowCarried);
            }

            storedIds.Clear();
            storedIds.AddRange(inventory.Stored());
            while (storedRows.Count < storedIds.Count)
            {
                var row = Instantiate(storedTemplate, storedRoot);
                int index = storedRows.Count;
                row.ActionButton.onClick.AddListener(() => TakeAt(index));
                storedRows.Add(row);
            }
            bool full = inventory.Carried.Count >= PlayerInventory.Capacity;
            for (int i = 0; i < storedRows.Count; i++)
            {
                bool used = i < storedIds.Count;
                storedRows[i].gameObject.SetActive(used);
                if (!used) continue;
                var item = inventory.Describe(storedIds[i]);
                SetIcon(storedRows[i], item.Id);
                storedRows[i].Set(item.DisplayName, item.IsTool ? "Outil de nettoyage" : "Gadget de farce", full ? "Inventaire plein" : "Au casier",
                    full ? Warn : Muted, "Prendre", !full, RowStored);
            }
            storedHeader.text = storedIds.Count == 0 ? "Casier vide" : $"Dans le casier ({storedIds.Count})";
            noteLabel.text = $"Vous portez {inventory.Carried.Count} / {PlayerInventory.Capacity} objets (outils et gadgets). " +
                             "Le pipi n'occupe pas d'emplacement : s'il est acheté, il reste toujours disponible (G).";
        }

        void StoreAt(int index)
        {
            var item = inventory.ItemAt(index);
            if (!item.IsValid) return;
            Show(inventory.Store(item.Id), item.DisplayName, $"{item.DisplayName} rangé au casier");
        }

        void TakeAt(int index)
        {
            if (index < 0 || index >= storedIds.Count) return;
            var item = inventory.Describe(storedIds[index]);
            Show(inventory.Carry(item.Id), item.DisplayName, $"{item.DisplayName} pris : touche {RugWashInputActions.SlotKeyLabel(inventory.Carried.Count)}");
        }

        void Show(InventoryResult result, string name, string success)
        {
            bool ok = result == InventoryResult.Done;
            messageLabel.text = ok ? success : InventoryRules.Describe(result, name);
            messageLabel.color = ok ? Ok : Warn;
            Refresh();
        }
    }
}
