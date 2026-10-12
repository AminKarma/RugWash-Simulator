using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Inventaire des produits spécialisés du joueur (achetés à la boutique, sauvegardés). Le produit actif est ajouté au
    /// détergent des pulvérisateurs ; la touche « Produit » (T) passe au suivant en stock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductInventory : MonoBehaviour, IProductSupply
    {
        [SerializeField] ProductLibrary library;

        ProductStockBook book;

        public ProductStockBook Book => book ??= new ProductStockBook(library != null ? library.products : null);
        public ProductLibrary Library => library;
        public ProductDefinition Active => Book.Active;

        public bool IsConfigured(out string missing)
        {
            missing = library == null ? nameof(library) : null;
            return missing == null;
        }

        public float Consume(float liters) => Book.Consume(liters);

        public float StockOf(ProductDefinition product) => Book.StockOf(product);

        public List<KeyValuePair<string, float>> Snapshot() => Book.Snapshot();

        public void Restore(IEnumerable<KeyValuePair<string, float>> entries, string activeId) => Book.Restore(entries, activeId);
    }
}
