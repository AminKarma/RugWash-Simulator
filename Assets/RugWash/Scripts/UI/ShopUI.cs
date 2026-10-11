using System.Collections.Generic;
using RugWash.Core;
using RugWash.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Boutique de matériel : prix, effet réel (valeur actuelle → après achat), statut et solde.
    /// Un achat impossible est refusé avec un message, sans modifier le solde.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopUI : MonoBehaviour, IModalPanel
    {
        static readonly Color RowColor = new Color(0.12f, 0.15f, 0.18f, 0.95f);
        static readonly Color RowOwned = new Color(0.1f, 0.22f, 0.16f, 0.95f);
        static readonly Color Ok = new Color(0.55f, 0.95f, 0.6f);
        static readonly Color Warn = new Color(1f, 0.6f, 0.45f);
        static readonly Color Neutral = new Color(0.95f, 0.85f, 0.5f);

        [SerializeField] GameObject panel;
        [SerializeField] Text balanceLabel;
        [SerializeField] Transform listRoot;
        [SerializeField] ListRowView rowTemplate;
        [SerializeField] Text messageLabel;
        [SerializeField] Button closeButton;

        readonly List<ListRowView> rows = new List<ListRowView>();
        GameStateManager gameState;
        ShopManager shop;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<ListRowView> Rows => rows;
        public string LastMessage => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : balanceLabel == null ? nameof(balanceLabel) : listRoot == null ? nameof(listRoot)
                : rowTemplate == null ? nameof(rowTemplate) : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, ShopManager shopManager)
        {
            gameState = state;
            shop = shopManager;
            rowTemplate.gameObject.SetActive(false);
            closeButton.onClick.AddListener(Close);
            shop.WalletManager.Wallet.Changed += OnWalletChanged;
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (shop != null && shop.WalletManager != null && shop.WalletManager.Wallet != null)
                shop.WalletManager.Wallet.Changed -= OnWalletChanged;
        }

        void OnWalletChanged(int balance, int delta)
        {
            if (IsOpen) Refresh();
        }

        public bool Open()
        {
            if (IsOpen || !gameState.IsGameplayActive) return false;
            messageLabel.text = string.Empty;
            panel.SetActive(true);
            IsOpen = true;
            gameState.PushModal();
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            gameState.PopModal();
        }

        void Refresh()
        {
            balanceLabel.text = $"Solde : {WalletManager.Format(shop.WalletManager.Balance)}";
            var catalog = shop.Upgrades.Catalog;
            while (rows.Count < catalog.Count)
            {
                var row = Instantiate(rowTemplate, listRoot);
                row.gameObject.SetActive(true);
                int index = rows.Count;
                row.ActionButton.onClick.AddListener(() => Buy(index));
                rows.Add(row);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < catalog.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var upgrade = catalog[i];
                var check = shop.Check(upgrade);
                bool owned = check == PurchaseResult.AlreadyOwned;
                string details = $"{upgrade.description}\n{shop.Upgrades.DescribeEffect(upgrade)}";
                string status = owned ? "Acheté" : WalletManager.Format(upgrade.price);
                Color statusColor = owned ? Ok : check == PurchaseResult.Purchased ? Neutral : Warn;
                // Le bouton reste cliquable si les fonds manquent : le refus est expliqué au joueur.
                rows[i].Set(upgrade.displayName, details, status, statusColor, owned ? null : "Acheter", true, owned ? RowOwned : RowColor);
            }
        }

        public PurchaseResult Buy(int index)
        {
            var catalog = shop.Upgrades.Catalog;
            if (index < 0 || index >= catalog.Count) return PurchaseResult.Unknown;
            var upgrade = catalog[index];
            var result = shop.TryPurchase(upgrade);
            messageLabel.text = result == PurchaseResult.Purchased
                ? $"{upgrade.displayName} acheté — {WalletManager.Format(upgrade.price)} débités"
                : $"Achat refusé : {ShopRules.Describe(result)}";
            messageLabel.color = result == PurchaseResult.Purchased ? Ok : Warn;
            Refresh();
            return result;
        }
    }
}
