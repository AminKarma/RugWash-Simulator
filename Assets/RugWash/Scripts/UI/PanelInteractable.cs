using RugWash.Orders;
using RugWash.Player;
using UnityEngine;

namespace RugWash.UI
{
    /// <summary>Objet de l'atelier qui ouvre une fenêtre : comptoir des commandes ou boutique de matériel.</summary>
    public sealed class PanelInteractable : MonoBehaviour, IInteractable
    {
        public enum Target
        {
            OrderBoard,
            Shop,
        }

        [SerializeField] Target target;
        [SerializeField] OrderBoardUI orderBoard;
        [SerializeField] ShopUI shop;
        [SerializeField] OrderManager orders;

        public Target Opens => target;

        public string GetPrompt(PlayerInteractor interactor)
        {
            if (target == Target.Shop) return "Boutique de matériel";
            return orders != null && orders.ActiveOrder != null ? "Comptoir : livrer ou abandonner la commande" : "Comptoir : voir les commandes";
        }

        public bool CanInteract(PlayerInteractor interactor) => target == Target.Shop ? shop != null : orderBoard != null;

        public string Interact(PlayerInteractor interactor)
        {
            if (target == Target.Shop) shop.Open();
            else orderBoard.Open();
            return null;
        }
    }
}
