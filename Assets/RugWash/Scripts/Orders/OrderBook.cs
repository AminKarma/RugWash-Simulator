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
        /// <summary>L'aire de lavage n'atteint pas la propreté minimale (25 % par défaut) : à nettoyer d'abord.</summary>
        WorkAreaTooDirty,
    }

    /// <summary>
    /// État des commandes (règles pures, testées) : une commande terminée ne peut pas être refaite, une récompense n'est
    /// versée qu'à la validation d'une commande active. Grande mise à jour : plusieurs commandes peuvent être actives à la
    /// fois (une par zone de lavage, plus celle de la machine à laver) ; le nombre maximal est fixé par l'appelant
    /// (<paramref name="maxActive"/>, une seule par défaut, comme avant).
    /// </summary>
    public sealed class OrderBook
    {
        readonly List<OrderDefinition> orders;
        readonly Dictionary<string, OrderState> states = new Dictionary<string, OrderState>();
        readonly List<OrderDefinition> active = new List<OrderDefinition>();

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
        /// <summary>Première commande active (compatibilité : la seule, tant qu'une seule zone est utilisée).</summary>
        public OrderDefinition Active => active.Count > 0 ? active[0] : null;
        /// <summary>Toutes les commandes actives (zones de lavage et machine à laver).</summary>
        public IReadOnlyList<OrderDefinition> ActiveOrders => active;

        public OrderState StateOf(OrderDefinition order) => states.TryGetValue(order.id, out var state) ? state : OrderState.Available;

        public bool IsLocked(OrderDefinition order, int reputation) => reputation < order.requiredReputation;

        public OrderStartResult CanStart(OrderDefinition order, int reputation, int maxActive = 1)
        {
            if (order == null || !states.ContainsKey(order.id)) return OrderStartResult.UnknownOrder;
            if (states[order.id] != OrderState.Available) return OrderStartResult.NotAvailable;
            if (active.Count >= Math.Max(1, maxActive)) return OrderStartResult.AnotherOrderActive;
            if (IsLocked(order, reputation)) return OrderStartResult.ReputationTooLow;
            return OrderStartResult.Started;
        }

        /// <summary>Ajoute une commande proposée en cours de partie (mission générée) ; sans effet si elle est déjà connue.</summary>
        public bool Add(OrderDefinition order)
        {
            if (order == null || states.ContainsKey(order.id)) return false;
            orders.Add(order);
            states[order.id] = OrderState.Available;
            return true;
        }

        /// <summary>Retire une commande qui n'est plus proposée (mission livrée ou remplacée) ; jamais une commande active.</summary>
        public bool Remove(OrderDefinition order)
        {
            if (order == null || active.Contains(order) || !orders.Remove(order)) return false;
            states.Remove(order.id);
            return true;
        }

        /// <summary>
        /// Multijoueur (invité) ou rechargement (machine à laver) : reproduit une commande active sans contrôle local.
        /// <paramref name="exclusive"/> : les autres commandes actives redeviennent disponibles (une seule zone).
        /// </summary>
        public void ForceActive(OrderDefinition order, bool exclusive = false)
        {
            if (order == null) return;
            Add(order);
            if (exclusive)
                foreach (var other in active.ToArray())
                    if (other != order) Release(other);
            if (!active.Contains(order)) active.Add(order);
            states[order.id] = OrderState.Active;
        }

        /// <summary>Multijoueur (invité) : plus de commande active (abandon ou livraison décidés par l'hôte).</summary>
        public void ClearActive()
        {
            foreach (var order in active.ToArray()) Release(order);
        }

        /// <summary>Une commande active redevient disponible (sans résultat).</summary>
        public bool ClearActive(OrderDefinition order) => order != null && active.Contains(order) && Release(order);

        bool Release(OrderDefinition order)
        {
            active.Remove(order);
            if (StateOf(order) == OrderState.Active) states[order.id] = OrderState.Available;
            return true;
        }

        public OrderStartResult Start(OrderDefinition order, int reputation, int maxActive = 1)
        {
            var check = CanStart(order, reputation, maxActive);
            if (check != OrderStartResult.Started) return check;
            states[order.id] = OrderState.Active;
            active.Add(order);
            return OrderStartResult.Started;
        }

        /// <summary>
        /// Clôt une commande active avec son résultat. Acceptée → Terminée ; refusée → redevient disponible.
        /// Retourne faux (et ne fait rien) si la commande n'est pas active : impossible d'être payé deux fois.
        /// </summary>
        public bool Complete(OrderResult result)
        {
            if (result == null || result.Order == null || !active.Contains(result.Order)) return false;
            states[result.Order.id] = result.Accepted ? OrderState.Completed : OrderState.Available;
            active.Remove(result.Order);
            return true;
        }

        /// <summary>Abandonne la première commande active (compatibilité).</summary>
        public bool Abandon() => Abandon(Active);

        public bool Abandon(OrderDefinition order)
        {
            if (order == null || !active.Contains(order)) return false;
            return Release(order);
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
