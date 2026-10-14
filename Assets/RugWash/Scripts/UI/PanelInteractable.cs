using RugWash.Orders;
using RugWash.Player;
using UnityEngine;

namespace RugWash.UI
{
    /// <summary>
    /// Objet de l'atelier qui ouvre une fenêtre. Depuis la v0.1.2, l'atelier n'en a qu'un : l'ordinateur du bureau
    /// (missions et boutique). Les cibles directes (tableau, boutique) restent disponibles pour d'autres décors.
    /// </summary>
    public sealed class PanelInteractable : MonoBehaviour, IInteractable
    {
        public enum Target
        {
            OrderBoard,
            Shop,
            Computer,
        }

        [SerializeField] Target target;
        [SerializeField] OrderBoardUI orderBoard;
        [SerializeField] ShopUI shop;
        [SerializeField] ComputerUI computer;
        [SerializeField] OrderManager orders;

        public Target Opens => target;

        public string GetPrompt(PlayerInteractor interactor)
        {
            bool active = orders != null && orders.ActiveOrder != null;
            switch (target)
            {
                case Target.Shop: return "Boutique de matériel";
                case Target.Computer:
                    return active ? "Ordinateur : livrer la commande (Missions) ou Boutique" : "Ordinateur : missions et boutique";
                default: return active ? "Comptoir : livrer ou abandonner la commande" : "Comptoir : voir les commandes";
            }
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            target == Target.Shop ? shop != null : target == Target.Computer ? computer != null : orderBoard != null;

        public string Interact(PlayerInteractor interactor)
        {
            if (target == Target.Shop) shop.Open();
            else if (target == Target.Computer) computer.Open();
            else orderBoard.Open();
            return null;
        }
    }
}
