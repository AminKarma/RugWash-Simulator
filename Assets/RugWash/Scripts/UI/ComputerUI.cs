using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Écran de l'ordinateur de l'atelier : exactement deux icônes, « Missions / Commandes » et « Boutique », qui ouvrent
    /// les applications existantes (<see cref="OrderBoardUI"/>, <see cref="ShopUI"/>). C'est le seul accès aux achats et
    /// aux missions. Souris, clavier et manette (navigation, B / Échap pour fermer).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComputerUI : MonoBehaviour, IModalPanel
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button missionsButton;
        [SerializeField] Text missionsStatus;
        [SerializeField] Button shopButton;
        [SerializeField] Text shopStatus;
        [SerializeField] Button closeButton;

        GameStateManager gameState;
        OrderBoardUI board;
        ShopUI shop;
        OrderManager orders;
        WalletManager wallet;
        DetergentSupply detergent;

        public bool IsOpen { get; private set; }
        public Button MissionsButton => missionsButton;
        public Button ShopButton => shopButton;
        public string MissionsStatus => missionsStatus.text;
        public string ShopStatus => shopStatus.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : missionsButton == null ? nameof(missionsButton) : missionsStatus == null ? nameof(missionsStatus)
                : shopButton == null ? nameof(shopButton) : shopStatus == null ? nameof(shopStatus) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, OrderBoardUI orderBoard, ShopUI shopUI, OrderManager orderManager, WalletManager walletManager,
            DetergentSupply detergentSupply)
        {
            gameState = state;
            board = orderBoard;
            shop = shopUI;
            orders = orderManager;
            wallet = walletManager;
            detergent = detergentSupply;
            missionsButton.onClick.AddListener(OpenMissions);
            shopButton.onClick.AddListener(OpenShop);
            closeButton.onClick.AddListener(Close);
            panel.SetActive(false);
        }

        public bool Open()
        {
            if (IsOpen || !gameState.IsGameplayActive) return false;
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

        /// <summary>Icône « Missions / Commandes » : ouvre le tableau des commandes (acceptation, livraison, abandon).</summary>
        public void OpenMissions()
        {
            if (!IsOpen) return;
            Close();
            board.Open();
        }

        /// <summary>Icône « Boutique » : ouvre la boutique (outils, améliorations, détergent, produits).</summary>
        public void OpenShop()
        {
            if (!IsOpen) return;
            Close();
            shop.Open();
        }

        /// <summary>Résumé tiré des données réelles du jeu sous chaque icône.</summary>
        void Refresh()
        {
            var active = orders.ActiveOrder;
            int open = 0;
            foreach (var order in orders.Book.Orders)
                if (orders.Book.StateOf(order) == OrderState.Available && !orders.Book.IsLocked(order, orders.BoardReputation)) open++;
            int running = orders.ActiveBayCount + orders.HeldOrders.Count;
            missionsStatus.text = running > 1
                ? $"{running} commandes en cours\nLivrer ou abandonner ici"
                : active != null
                ? $"En cours : {active.title}\nLivrer ou abandonner ici"
                : open == 0 ? "Aucune commande disponible" : $"{open} commande{(open > 1 ? "s" : string.Empty)} disponible{(open > 1 ? "s" : string.Empty)}";
            string stock = detergent != null ? $"\nDétergent en stock : {detergent.Stock:0.#} L" : string.Empty;
            shopStatus.text = $"Solde : {WalletManager.Format(wallet.Balance)}{stock}";
        }
    }
}
