using System;
using RugWash.Cleaning;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.RugDex
{
    /// <summary>
    /// Alimente le RugDex dans l'atelier : un tapis est découvert dès qu'il est posé sur l'aire de lavage (tapis
    /// d'entraînement ou tapis d'une commande acceptée) ; chaque livraison acceptée met à jour ses statistiques.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugDexService : MonoBehaviour
    {
        [SerializeField] RugCatalog catalog;
        [SerializeField] RugCleaningController rug;
        [SerializeField] OrderManager orders;

        readonly RugDexBook book = new RugDexBook();

        public RugDexBook Book => book;
        public RugCatalog Catalog => catalog;

        /// <summary>Nouveau tapis découvert (nom affiché).</summary>
        public event Action<RugDefinition> NewRugDiscovered;

        public bool IsConfigured(out string missing)
        {
            missing = catalog == null ? nameof(catalog) : rug == null ? nameof(rug) : orders == null ? nameof(orders) : null;
            return missing == null;
        }

        void OnEnable()
        {
            if (rug != null) rug.RugLoaded += OnRugLoaded;
            if (orders != null) orders.OrderFinished += OnOrderFinished;
        }

        void OnDisable()
        {
            if (rug != null) rug.RugLoaded -= OnRugLoaded;
            if (orders != null) orders.OrderFinished -= OnOrderFinished;
        }

        /// <summary>Après restauration de la sauvegarde : le tapis déjà posé est aussi découvert.</summary>
        public void DiscoverCurrentRug()
        {
            if (rug != null && rug.Definition != null) Discover(rug.Definition);
        }

        void OnRugLoaded(RugCleaningController loaded) => Discover(loaded.Definition);

        void Discover(RugDefinition definition)
        {
            if (definition == null || catalog == null || !catalog.rugs.Contains(definition)) return;
            if (book.Discover(definition.id)) NewRugDiscovered?.Invoke(definition);
        }

        void OnOrderFinished(OrderResult result)
        {
            if (result?.Order?.rug != null && result.Accepted) book.RecordDelivery(result.Order.rug.id, result.Grade);
        }
    }
}
