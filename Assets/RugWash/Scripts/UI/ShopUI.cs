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
        static readonly Color RowColor = UITheme.Row;
        static readonly Color RowOwned = UITheme.RowOwned;
        static readonly Color Ok = UITheme.Success;
        static readonly Color Warn = UITheme.Danger;
        static readonly Color Neutral = UITheme.Money;

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
        /// <summary>
        /// Article de chaque ligne : celui que le bouton achète (ou revend). Onglet « Améliorations » (V0.4.2) : une carte par
        /// outil, dont l'article est la prochaine amélioration à acheter ; les autres onglets : un article par ligne.
        /// </summary>
        readonly List<UpgradeDefinition> equipmentDisplay = new List<UpgradeDefinition>();
        /// <summary>Articles regroupés sur chaque ligne (un seul hors de l'onglet « Améliorations »).</summary>
        readonly List<List<UpgradeDefinition>> rowItems = new List<List<UpgradeDefinition>>();
        /// <summary>Clé de regroupement de chaque ligne (outil de la carte).</summary>
        readonly List<string> rowKeys = new List<string>();
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

        /// <summary>Ligne de l'onglet affiché qui présente cette amélioration (sa carte d'outil) ; null si elle n'est pas affichée.</summary>
        public ListRowView RowOf(UpgradeDefinition upgrade)
        {
            if (ShowingProducts) return null;
            for (int i = 0; i < rowItems.Count && i < rows.Count; i++)
                if (rowItems[i].Contains(upgrade)) return rows[i];
            return null;
        }

        /// <summary>Article qu'achète (ou revend) le bouton de cette ligne : la prochaine amélioration d'une carte d'outil.</summary>
        public UpgradeDefinition ItemOf(ListRowView row)
        {
            int index = rows.IndexOf(row);
            return !ShowingProducts && index >= 0 && index < equipmentDisplay.Count ? equipmentDisplay[index] : null;
        }

        /// <summary>Nombre d'articles présentés dans l'onglet affiché (toutes cartes confondues).</summary>
        public int ShownItemCount
        {
            get
            {
                if (ShowingProducts) return 0;
                int count = 0;
                foreach (var items in rowItems) count += items.Count;
                return count;
            }
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
            pendingSale = null;
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
            pendingSale = null;
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
            // V0.4.2 : les bottes (équipement porté) sont présentées avec les outils.
            bool tool = upgrade.effect == UpgradeEffect.UnlockTool || upgrade.effect == UpgradeEffect.FootprintTransfer
                        || upgrade.effect == UpgradeEffect.InventorySlot;
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
                    else if (index < equipmentDisplay.Count) Activate(equipmentDisplay[index]);
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
            var display = equipmentDisplay;
            display.Clear();
            // Atelier : décor (paliers dans l'ordre), puis machines et robot. Matériel : outils, puis améliorations.
            foreach (var upgrade in catalog)
                if (CurrentTab == Tab.Workshop ? upgrade.effect == UpgradeEffect.WorkshopTier
                    : CurrentTab == Tab.Equipment && upgrade.effect == UpgradeEffect.UnlockTool && InCurrentTab(upgrade)) display.Add(upgrade);
            foreach (var upgrade in catalog)
                if (!display.Contains(upgrade) && InCurrentTab(upgrade))
                    display.Add(upgrade);
            GroupRows(display);
            EnsureRows(equipmentDisplay.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < equipmentDisplay.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                if (rowItems[i].Count > 1)
                {
                    RefreshCard(rows[i], rowKeys[i], rowItems[i], equipmentDisplay[i]);
                    continue;
                }
                var upgrade = equipmentDisplay[i];
                var check = shop.Check(upgrade);
                bool owned = check == PurchaseResult.AlreadyOwned;
                // En ligne (V0.2.5) : un invité consulte l'atelier, mais seul l'hôte l'achète (vérifié aussi par l'hôte).
                bool hostOnly = check == PurchaseResult.HostOnly;
                // V0.2.5 : un objet acheté qui se porte (outil, gadget) se revend à la moitié de son prix de base.
                bool resellable = owned && SessionEconomyRules.IsResellable(upgrade);
                int resale = SessionEconomyRules.ResaleValue(upgrade);
                string details = $"{upgrade.description}\n{shop.Upgrades.DescribeEffect(upgrade)}";
                string status = owned ? (resellable ? $"Acheté · revente {WalletManager.Format(resale)}" : "Acheté")
                    : hostOnly ? $"{WalletManager.Format(upgrade.price)} · hôte seulement"
                    : check == PurchaseResult.ReputationTooLow ? $"Réputation {upgrade.requiredReputation}"
                    : WalletManager.Format(upgrade.price);
                Color statusColor = owned ? Ok : check == PurchaseResult.Purchased ? Neutral : Warn;
                string action = resellable ? (pendingSale == upgrade ? $"Confirmer +{resale} €" : "Revendre")
                    : owned || hostOnly ? null : "Acheter";
                // Le bouton reste cliquable si les fonds manquent : le refus est expliqué au joueur.
                rows[i].Set(upgrade.displayName, details, status, statusColor, action, true, owned ? RowOwned : RowColor);
                SetIcon(rows[i], IconOf(upgrade));
            }
        }

        /// <summary>
        /// V0.4.2 : regroupe les lignes. Onglet « Améliorations » : une carte par outil (puissance, largeur, consommation,
        /// accessoires et réservoir qu'il utilise), dans l'ordre où l'outil apparaît au catalogue ; ailleurs, un article par ligne.
        /// L'article de chaque carte est la prochaine amélioration à acheter (ou la dernière, tout étant acheté).
        /// </summary>
        void GroupRows(List<UpgradeDefinition> display)
        {
            var flat = new List<UpgradeDefinition>(display);
            display.Clear();
            rowItems.Clear();
            rowKeys.Clear();
            foreach (var upgrade in flat)
            {
                string key = CurrentTab == Tab.Upgrades ? CardOf(upgrade) : null;
                int index = key != null ? rowKeys.IndexOf(key) : -1;
                if (index >= 0)
                {
                    rowItems[index].Add(upgrade);
                    continue;
                }
                rowKeys.Add(key);
                rowItems.Add(new List<UpgradeDefinition> { upgrade });
            }
            foreach (var items in rowItems) display.Add(items.Count > 1 ? NextOf(items) : items[0]);
        }

        /// <summary>Carte d'une amélioration : l'outil ciblé, ou celui qui utilise le réservoir agrandi.</summary>
        static string CardOf(UpgradeDefinition upgrade)
        {
            switch (upgrade.effect)
            {
                case UpgradeEffect.WaterCapacity: return "rinse";
                case UpgradeEffect.SoapCapacity: return "soap";
                case UpgradeEffect.RecoveryCapacity: return "extractor";
            }
            return string.IsNullOrEmpty(upgrade.targetToolId) ? "item:" + upgrade.id : upgrade.targetToolId;
        }

        /// <summary>Prochaine amélioration d'une carte : la première non achetée dont la condition est remplie, sinon la première non achetée.</summary>
        UpgradeDefinition NextOf(List<UpgradeDefinition> items)
        {
            UpgradeDefinition blocked = null;
            foreach (var upgrade in items)
            {
                if (shop.Upgrades.IsOwned(upgrade)) continue;
                if (upgrade.prerequisite == null || shop.Upgrades.IsOwned(upgrade.prerequisite)) return upgrade;
                if (blocked == null) blocked = upgrade;
            }
            return blocked ?? items[items.Count - 1];
        }

        /// <summary>Carte d'un outil : progression, prochaine amélioration (effet réel avant → après), déjà acquises.</summary>
        void RefreshCard(ListRowView row, string key, List<UpgradeDefinition> items, UpgradeDefinition next)
        {
            int owned = 0;
            var acquired = new List<string>();
            var remaining = new List<string>();
            foreach (var upgrade in items)
            {
                bool has = shop.Upgrades.IsOwned(upgrade);
                if (has) owned++;
                (has ? acquired : remaining).Add(upgrade.displayName);
            }
            bool done = owned == items.Count;
            string name = shop.Upgrades.ToolDisplayName(key) ?? next.displayName;
            // Niveau actuel de l'outil (chaîne de niveaux de puissance), puis la progression de la carte.
            int maxLevel = shop.Upgrades.MaxToolLevel(key);
            string level = maxLevel > 0 ? $" · niveau {shop.Upgrades.ToolLevelFor(shop.Upgrades.OwnedIds, key)}/{maxLevel}" : string.Empty;
            string title = $"{name}{level} · {owned}/{items.Count} amélioration{(items.Count > 1 ? "s" : string.Empty)}";
            var check = done ? PurchaseResult.AlreadyOwned : shop.Check(next);
            bool hostOnly = check == PurchaseResult.HostOnly;
            // État de la prochaine amélioration : disponible, verrouillée (condition à remplir) ou tout acheté.
            string state = check == PurchaseResult.ReputationTooLow ? $"verrouillée : réputation {next.requiredReputation} requise"
                : check == PurchaseResult.MissingPrerequisite && next.prerequisite != null ? $"verrouillée : {next.prerequisite.displayName} requis"
                : hostOnly ? "achat par l'hôte" : "disponible";
            string details = done
                ? "Toutes les améliorations de cet outil sont achetées."
                : $"Prochaine ({state}) : {next.displayName} — {next.description}\n{shop.Upgrades.DescribeEffect(next)}";
            if (acquired.Count > 0) details += $"\nAchetées : {string.Join(", ", acquired)}";
            // Les niveaux suivants se révèlent au fur et à mesure : seul leur nombre est annoncé.
            if (!done && remaining.Count > 1) details += $"\nEncore {remaining.Count - 1} amélioration{(remaining.Count > 2 ? "s" : string.Empty)} à découvrir ensuite";
            string status = done ? "Tout acheté"
                : hostOnly ? $"{WalletManager.Format(next.price)} · hôte seulement"
                : check == PurchaseResult.ReputationTooLow ? $"Verrouillée · réputation {next.requiredReputation}"
                : check == PurchaseResult.MissingPrerequisite && next.prerequisite != null ? $"Verrouillée · {next.prerequisite.displayName}"
                : $"Disponible · {WalletManager.Format(next.price)}";
            Color statusColor = done ? Ok : check == PurchaseResult.Purchased ? Neutral : Warn;
            row.Set(title, details, status, statusColor, done || hostOnly ? null : "Acheter", true, done ? RowOwned : RowColor);
            SetIcon(row, key.StartsWith("item:") ? IconOf(next) : key);
        }

        bool HasDetergentRow => shop.Detergent != null;

        Effects.ToolIconRenderer icons;

        /// <summary>Icônes 3D des outils (grande mise à jour) : sur les outils à acheter et leurs améliorations.</summary>
        public void BindIcons(Effects.ToolIconRenderer renderer) => icons = renderer;

        static string IconOf(UpgradeDefinition upgrade)
        {
            if (upgrade != null && upgrade.effect == UpgradeEffect.FootprintTransfer) return Effects.ToolIconRenderer.BootsId;
            if (upgrade == null || string.IsNullOrEmpty(upgrade.targetToolId) || upgrade.IsWorkshopItem) return null;
            return upgrade.IsPrankItem ? $"prank:{upgrade.targetToolId}" : upgrade.targetToolId;
        }

        void SetIcon(ListRowView row, string id)
        {
            if (icons != null && !string.IsNullOrEmpty(id) && icons.TryGetIcon(id, out Rect uv)) row.SetIcon(icons.Atlas, uv);
            else row.ClearIcon();
        }

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
                rows[0].ClearIcon();
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
                rows[i].ClearIcon();
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

        UpgradeDefinition pendingSale;

        /// <summary>Revente annoncée, en attente de confirmation (V0.2.5).</summary>
        public UpgradeDefinition PendingSale => pendingSale;

        /// <summary>Ligne d'un article : achat, ou revente en deux temps pour un objet possédé.</summary>
        void Activate(UpgradeDefinition upgrade)
        {
            if (shop.Upgrades.IsOwned(upgrade) && SessionEconomyRules.IsResellable(upgrade))
            {
                if (pendingSale == upgrade) ConfirmSale();
                else AskSale(upgrade);
                return;
            }
            pendingSale = null;
            Buy(IndexInCatalog(upgrade));
        }

        /// <summary>
        /// Premier temps de la revente : le montant récupéré est annoncé (moitié du prix de base), sans rien vendre. Faux si
        /// l'objet ne peut pas être revendu maintenant (porté, non revendable…), avec la raison affichée.
        /// </summary>
        public bool AskSale(UpgradeDefinition upgrade)
        {
            var check = shop.CheckSale(upgrade);
            if (check != SaleResult.Sold)
            {
                pendingSale = null;
                messageLabel.text = $"Revente impossible : {SessionEconomyRules.Describe(check)}";
                messageLabel.color = Warn;
                Refresh();
                return false;
            }
            pendingSale = upgrade;
            messageLabel.text = $"Revendre {upgrade.displayName} pour {WalletManager.Format(SessionEconomyRules.ResaleValue(upgrade))} " +
                                $"(moitié du prix d'achat de {WalletManager.Format(upgrade.price)}) ? Appuyez sur « Confirmer ».";
            messageLabel.color = Neutral;
            Refresh();
            return true;
        }

        /// <summary>Second temps : la revente annoncée est exécutée (une seule fois).</summary>
        public SaleResult ConfirmSale()
        {
            var upgrade = pendingSale;
            pendingSale = null;
            if (upgrade == null) return SaleResult.Unknown;
            int value = SessionEconomyRules.ResaleValue(upgrade);
            var result = shop.TrySell(upgrade);
            messageLabel.text = result == SaleResult.Sold ? $"{upgrade.displayName} revendu : +{WalletManager.Format(value)}"
                : result == SaleResult.Requested ? SessionEconomyRules.Describe(result)
                : $"Revente refusée : {SessionEconomyRules.Describe(result)}";
            messageLabel.color = result == SaleResult.Sold || result == SaleResult.Requested ? Ok : Warn;
            Refresh();
            return result;
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
