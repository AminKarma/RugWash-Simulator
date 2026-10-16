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
        [SerializeField] Button equipmentTabButton;
        [SerializeField] Button productsTabButton;
        [Tooltip("Onglet « Atelier » : machines, robot et décor de l'atelier (v0.1.2), optionnel.")]
        [SerializeField] Button workshopTabButton;
        [Tooltip("Onglet « Farces » : gadgets de sabotage humoristique (v0.1.2), optionnel.")]
        [SerializeField] Button pranksTabButton;
        [Tooltip("Onglet « Améliorations » (réservoirs, puissance, accessoires), séparé de l'onglet « Outils », optionnel.")]
        [SerializeField] Button upgradesTabButton;

        public enum Tab
        {
            /// <summary>Outils à acheter (onglet « Outils » ; « Matériel » sans onglet Améliorations).</summary>
            Equipment,
            Workshop,
            Products,
            Pranks,
            /// <summary>Améliorations du matériel (réservoirs, puissance, accessoires).</summary>
            Upgrades,
        }

        /// <summary>Onglet affiché.</summary>
        public Tab CurrentTab { get; private set; }

        readonly List<ListRowView> rows = new List<ListRowView>();
        /// <summary>Onglet « Matériel » : outils à acheter d'abord, puis les améliorations (ordre du catalogue).</summary>
        readonly List<UpgradeDefinition> equipmentDisplay = new List<UpgradeDefinition>();
        GameStateManager gameState;
        ShopManager shop;

        public bool IsOpen { get; private set; }
        /// <summary>Onglet « Produits » affiché (sinon « Matériel »).</summary>
        public bool ShowingProducts { get; private set; }

        // Manette : LB / RB passent d'un onglet à l'autre (Matériel, Produits).
        void Update()
        {
            if (!IsOpen) return;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null || shop == null) return;
            var order = TabOrder();
            int count = order.Count;
            int index = Mathf.Max(0, order.IndexOf(CurrentTab));
            if (pad.rightShoulder.wasPressedThisFrame) ShowTab(order[(index + 1) % count]);
            else if (pad.leftShoulder.wasPressedThisFrame) ShowTab(order[(index + count - 1) % count]);
        }
        public IReadOnlyList<ListRowView> Rows => rows;
        public string LastMessage => messageLabel.text;

        /// <summary>Ligne de l'onglet « Matériel » qui affiche cette amélioration (null si elle n'est pas affichée).</summary>
        public ListRowView RowOf(UpgradeDefinition upgrade)
        {
            int index = ShowingProducts ? -1 : equipmentDisplay.IndexOf(upgrade);
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : balanceLabel == null ? nameof(balanceLabel) : listRoot == null ? nameof(listRoot)
                : rowTemplate == null ? nameof(rowTemplate) : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton)
                : equipmentTabButton == null ? nameof(equipmentTabButton) : productsTabButton == null ? nameof(productsTabButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, ShopManager shopManager)
        {
            gameState = state;
            shop = shopManager;
            rowTemplate.gameObject.SetActive(false);
            closeButton.onClick.AddListener(Close);
            shop.RemoteResultReceived += OnRemoteResult;
            equipmentTabButton.onClick.AddListener(() => ShowTab(Tab.Equipment));
            productsTabButton.onClick.AddListener(() => ShowTab(Tab.Products));
            if (workshopTabButton != null) workshopTabButton.onClick.AddListener(() => ShowTab(Tab.Workshop));
            if (pranksTabButton != null) pranksTabButton.onClick.AddListener(() => ShowTab(Tab.Pranks));
            if (upgradesTabButton != null) upgradesTabButton.onClick.AddListener(() => ShowTab(Tab.Upgrades));
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

        /// <summary>Affiche l'onglet « Matériel » (améliorations) ou « Produits » (flacons de produits spécialisés).</summary>
        /// <summary>Multijoueur (invité) : l'hôte a répondu à un achat demandé depuis cette boutique.</summary>
        void OnRemoteResult(string message, bool success)
        {
            if (!IsOpen) return;
            messageLabel.text = message;
            messageLabel.color = success ? Ok : Warn;
            Refresh();
        }

        public void ShowTab(bool products) => ShowTab(products ? Tab.Products : Tab.Equipment);

        public void ShowTab(Tab tab)
        {
            if (tab == Tab.Products && shop.Products == null) tab = Tab.Equipment;
            if (tab == Tab.Workshop && workshopTabButton == null) tab = Tab.Equipment;
            if (tab == Tab.Pranks && pranksTabButton == null) tab = Tab.Equipment;
            if (tab == Tab.Upgrades && upgradesTabButton == null) tab = Tab.Equipment;
            CurrentTab = tab;
            ShowingProducts = tab == Tab.Products;
            messageLabel.text = string.Empty;
            Refresh();
        }

        // Ordre des onglets à l'écran : Outils, Améliorations, Atelier, Farces, Produits (onglets présents seulement).
        readonly List<Tab> tabOrder = new List<Tab>();

        List<Tab> TabOrder()
        {
            tabOrder.Clear();
            tabOrder.Add(Tab.Equipment);
            if (upgradesTabButton != null) tabOrder.Add(Tab.Upgrades);
            if (workshopTabButton != null) tabOrder.Add(Tab.Workshop);
            if (pranksTabButton != null) tabOrder.Add(Tab.Pranks);
            if (shop.Products != null) tabOrder.Add(Tab.Products);
            return tabOrder;
        }

        /// <summary>L'article appartient-il à l'onglet affiché (hors produits) ?</summary>
        bool InCurrentTab(UpgradeDefinition upgrade)
        {
            bool workshop = workshopTabButton != null && upgrade.IsWorkshopItem;
            bool prank = pranksTabButton != null && upgrade.IsPrankItem;
            bool tool = upgrade.effect == UpgradeEffect.UnlockTool;
            bool split = upgradesTabButton != null;
            switch (CurrentTab)
            {
                case Tab.Workshop: return workshop;
                case Tab.Pranks: return prank;
                case Tab.Upgrades: return !workshop && !prank && !tool;
                default: return !workshop && !prank && (!split || tool);
            }
        }

        void EnsureRows(int count)
        {
            while (rows.Count < count)
            {
                var row = Instantiate(rowTemplate, listRoot);
                row.gameObject.SetActive(true);
                int index = rows.Count;
                row.ActionButton.onClick.AddListener(() =>
                {
                    // Onglet « Produits » : la première ligne est le bidon de détergent, puis les produits spécialisés.
                    if (ShowingProducts && HasDetergentRow && index == 0) BuyDetergentCan();
                    else if (ShowingProducts) BuyProduct(index - (HasDetergentRow ? 1 : 0));
                    else if (index < equipmentDisplay.Count) Buy(IndexInCatalog(equipmentDisplay[index]));
                });
                rows.Add(row);
            }
        }

        void Refresh()
        {
            balanceLabel.text = $"Solde : {WalletManager.Format(shop.WalletManager.Balance)}";
            equipmentTabButton.interactable = CurrentTab != Tab.Equipment;
            productsTabButton.interactable = CurrentTab != Tab.Products && shop.Products != null;
            if (workshopTabButton != null) workshopTabButton.interactable = CurrentTab != Tab.Workshop;
            if (pranksTabButton != null) pranksTabButton.interactable = CurrentTab != Tab.Pranks;
            if (upgradesTabButton != null) upgradesTabButton.interactable = CurrentTab != Tab.Upgrades;
            if (ShowingProducts)
            {
                RefreshProducts();
                return;
            }
            var catalog = shop.Upgrades.Catalog;
            equipmentDisplay.Clear();
            // Atelier : décor (paliers dans l'ordre), puis machines et robot. Matériel : outils, puis améliorations.
            foreach (var upgrade in catalog)
                if (CurrentTab == Tab.Workshop ? upgrade.effect == UpgradeEffect.WorkshopTier
                    : CurrentTab == Tab.Equipment && upgrade.effect == UpgradeEffect.UnlockTool && InCurrentTab(upgrade)) equipmentDisplay.Add(upgrade);
            foreach (var upgrade in catalog)
                if (!equipmentDisplay.Contains(upgrade) && InCurrentTab(upgrade))
                    equipmentDisplay.Add(upgrade);
            EnsureRows(equipmentDisplay.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < equipmentDisplay.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var upgrade = equipmentDisplay[i];
                var check = shop.Check(upgrade);
                bool owned = check == PurchaseResult.AlreadyOwned;
                string details = $"{upgrade.description}\n{shop.Upgrades.DescribeEffect(upgrade)}";
                string status = owned ? "Acheté"
                    : check == PurchaseResult.ReputationTooLow ? $"Réputation {upgrade.requiredReputation}"
                    : WalletManager.Format(upgrade.price);
                Color statusColor = owned ? Ok : check == PurchaseResult.Purchased ? Neutral : Warn;
                // Le bouton reste cliquable si les fonds manquent : le refus est expliqué au joueur.
                rows[i].Set(upgrade.displayName, details, status, statusColor, owned ? null : "Acheter", true, owned ? RowOwned : RowColor);
            }
        }

        bool HasDetergentRow => shop.Detergent != null;

        void RefreshProducts()
        {
            var book = shop.Products.Book;
            var catalog = book.Catalog;
            int offset = HasDetergentRow ? 1 : 0;
            EnsureRows(catalog.Count + offset);
            if (HasDetergentRow)
            {
                var supply = shop.Detergent;
                var check = shop.CheckDetergentCan();
                rows[0].gameObject.SetActive(true);
                rows[0].Set($"Bidon de détergent {supply.CanLiters:0} L",
                    $"Le détergent de base : la réserve emportée ({supply.ReserveCapacity:0.#} L) se remplit au poste détergent à partir de ce stock.\n" +
                    $"Stock : {supply.Stock:0.#} / {supply.MaxStock:0} L · un bidon = {supply.CanLiters:0} L " +
                    $"(≈ {supply.CanLiters / Mathf.Max(0.1f, supply.ReserveCapacity):0} remplissages de {supply.ReserveCapacity:0.#} L)",
                    WalletManager.Format(supply.CanPrice), check == ProductPurchaseResult.Purchased ? Neutral : Warn, "Acheter", true, RowColor);
            }
            for (int i = offset; i < rows.Count; i++)
            {
                bool used = i - offset < catalog.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var product = catalog[i - offset];
                var check = shop.CheckProduct(product);
                string details = $"{product.description}\n{RugWash.Cleaning.ProductTable.Describe(product)}\n" +
                                 $"Flacon de {product.packLiters:0.#} L · en stock : {book.StockOf(product):0.0} / {product.maxStock:0.#} L";
                Color statusColor = check == ProductPurchaseResult.Purchased ? Neutral : Warn;
                rows[i].Set(product.displayName, details, WalletManager.Format(product.packPrice), statusColor, "Acheter", true, RowColor);
            }
        }

        /// <summary>Achat d'un bidon de détergent depuis l'onglet « Produits ».</summary>
        public ProductPurchaseResult BuyDetergentCan()
        {
            var result = shop.TryBuyDetergentCan();
            var supply = shop.Detergent;
            messageLabel.text = result == ProductPurchaseResult.Purchased
                ? $"Bidon de détergent : +{supply.CanLiters:0} L en stock ({supply.Stock:0.#} L) — {WalletManager.Format(supply.CanPrice)} débités"
                : result == ProductPurchaseResult.Requested ? ShopRules.Describe(result) : $"Achat refusé : {ShopRules.Describe(result)}";
            messageLabel.color = result == ProductPurchaseResult.Purchased || result == ProductPurchaseResult.Requested ? Ok : Warn;
            Refresh();
            return result;
        }

        public ProductPurchaseResult BuyProduct(int index)
        {
            var catalog = shop.Products != null ? shop.Products.Book.Catalog : null;
            if (catalog == null || index < 0 || index >= catalog.Count) return ProductPurchaseResult.Unknown;
            var product = catalog[index];
            var result = shop.TryBuyProduct(product);
            messageLabel.text = result == ProductPurchaseResult.Purchased
                ? $"{product.displayName} : +{product.packLiters:0.#} L — {WalletManager.Format(product.packPrice)} débités (T pour le sélectionner)"
                : $"Achat refusé : {ShopRules.Describe(result)}";
            messageLabel.color = result == ProductPurchaseResult.Purchased ? Ok : Warn;
            Refresh();
            return result;
        }

        int IndexInCatalog(UpgradeDefinition upgrade)
        {
            var catalog = shop.Upgrades.Catalog;
            for (int i = 0; i < catalog.Count; i++)
                if (catalog[i] == upgrade) return i;
            return -1;
        }

        /// <summary>Achat de l'amélioration d'indice <paramref name="index"/> dans le catalogue.</summary>
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
