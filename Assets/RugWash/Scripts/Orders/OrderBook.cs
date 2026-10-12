using System;
using System.Collections.Generic;

namespace RugWash.Orders
{
    public enum OrderState
    {
        Available,
        Active,
        Completed,
    }

    public enum OrderStartResult
    {
        /// <summary>Multijoueur (invité) : demande envoyée à l'hôte, qui décidera.</summary>
        Requested = 100,
        Started,
        UnknownOrder,
        AnotherOrderActive,
        NotAvailable,
        ReputationTooLow,
    }

    /// <summary>
    /// État des commandes (règles pures, testées) : une seule commande active, une commande terminée
    /// ne peut pas être refaite, une récompense n'est versée qu'à la validation d'une commande active.
    /// </summary>
    public sealed class OrderBook
    {
        readonly List<OrderDefinition> orders;
        readonly Dictionary<string, OrderState> states = new Dictionary<string, OrderState>();

        public OrderBook(IEnumerable<OrderDefinition> definitions)
        {
            orders = new List<OrderDefinition>();
            foreach (var order in definitions)
            {
                if (order == null || states.ContainsKey(order.id)) continue;
                orders.Add(order);
                states[order.id] = OrderState.Available;
            }
        }

        public IReadOnlyList<OrderDefinition> Orders => orders;
        public OrderDefinition Active { get; private set; }

        public OrderState StateOf(OrderDefinition order) => states.TryGetValue(order.id, out var state) ? state : OrderState.Available;

        public bool IsLocked(OrderDefinition order, int reputation) => reputation < order.requiredReputation;

        public OrderStartResult CanStart(OrderDefinition order, int reputation)
        {
            if (order == null || !states.ContainsKey(order.id)) return OrderStartResult.UnknownOrder;
            if (Active != null) return OrderStartResult.AnotherOrderActive;
            if (states[order.id] != OrderState.Available) return OrderStartResult.NotAvailable;
            if (IsLocked(order, reputation)) return OrderStartResult.ReputationTooLow;
            return OrderStartResult.Started;
        }

        /// <summary>Multijoueur (invité) : reproduit la commande démarrée par l'hôte, sans contrôle local.</summary>
        public void ForceActive(OrderDefinition order)
        {
            if (order == null) return;
            if (Active != null && Active != order) states[Active.id] = OrderState.Available;
            Active = order;
            states[order.id] = OrderState.Active;
        }

        /// <summary>Multijoueur (invité) : plus de commande active (abandon ou livraison décidés par l'hôte).</summary>
        public void ClearActive()
        {
            if (Active != null && StateOf(Active) == OrderState.Active) states[Active.id] = OrderState.Available;
            Active = null;
        }

        public OrderStartResult Start(OrderDefinition order, int reputation)
        {
            var check = CanStart(order, reputation);
            if (check != OrderStartResult.Started) return check;
            states[order.id] = OrderState.Active;
            Active = order;
            return OrderStartResult.Started;
        }

        /// <summary>
        /// Clôt la commande active avec son résultat. Acceptée → Terminée ; refusée → redevient disponible.
        /// Retourne faux (et ne fait rien) si aucune commande n'est active : impossible d'être payé deux fois.
        /// </summary>
        public bool Complete(OrderResult result)
        {
            if (Active == null || result == null || result.Order != Active) return false;
            states[Active.id] = result.Accepted ? OrderState.Completed : OrderState.Available;
            Active = null;
            return true;
        }

        public bool Abandon()
        {
            if (Active == null) return false;
            states[Active.id] = OrderState.Available;
            Active = null;
            return true;
        }

        /// <summary>Restaure des commandes terminées (sauvegarde, phase 4).</summary>
        public void MarkCompleted(IEnumerable<string> ids)
        {
            foreach (string id in ids)
                if (states.ContainsKey(id)) states[id] = OrderState.Completed;
        }

        public IEnumerable<string> CompletedIds()
        {
            foreach (var pair in states)
                if (pair.Value == OrderState.Completed) yield return pair.Key;
        }

        public OrderDefinition Find(string id) => orders.Find(o => string.Equals(o.id, id, StringComparison.Ordinal));
    }
}
