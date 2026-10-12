using System;
using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Accès des pulvérisateurs au produit spécialisé sélectionné.</summary>
    public interface IProductSupply
    {
        /// <summary>Produit ajouté au détergent, ou null pour le détergent standard.</summary>
        ProductDefinition Active { get; }

        /// <summary>Prélève jusqu'à <paramref name="liters"/> du produit actif ; retourne la quantité obtenue.</summary>
        float Consume(float liters);

        /// <summary>Stock restant d'un produit (L).</summary>
        float StockOf(ProductDefinition product);
    }

    /// <summary>
    /// Stock des produits spécialisés et produit actif (règle pure, testée). Le détergent standard n'est pas stocké ici :
    /// il se recharge gratuitement au poste produits. Un produit épuisé est désélectionné automatiquement.
    /// </summary>
    public sealed class ProductStockBook : IProductSupply
    {
        readonly List<ProductDefinition> catalog = new List<ProductDefinition>();
        readonly Dictionary<string, float> stock = new Dictionary<string, float>();

        public ProductStockBook(IEnumerable<ProductDefinition> products)
        {
            if (products == null) return;
            foreach (var product in products)
                if (product != null && !catalog.Contains(product)) catalog.Add(product);
        }

        public IReadOnlyList<ProductDefinition> Catalog => catalog;
        public ProductDefinition Active { get; private set; }

        /// <summary>Changement de stock ou de produit actif.</summary>
        public event Action Changed;
        /// <summary>Le produit actif vient d'être épuisé (retour au détergent standard).</summary>
        public event Action<ProductDefinition> Depleted;

        public float StockOf(ProductDefinition product) =>
            product != null && stock.TryGetValue(product.id, out float liters) ? liters : 0f;

        public float FreeSpace(ProductDefinition product) => product == null ? 0f : Mathf.Max(0f, product.maxStock - StockOf(product));

        /// <summary>Ajoute du produit, borné au stock maximal ; retourne la quantité réellement ajoutée.</summary>
        public float Add(ProductDefinition product, float liters)
        {
            if (product == null || liters <= 0f || !catalog.Contains(product)) return 0f;
            float added = Mathf.Min(liters, FreeSpace(product));
            if (added <= 0f) return 0f;
            stock[product.id] = StockOf(product) + added;
            Changed?.Invoke();
            return added;
        }

        public float Consume(float liters)
        {
            if (Active == null || liters <= 0f) return 0f;
            float have = StockOf(Active);
            float taken = Mathf.Min(have, liters);
            stock[Active.id] = have - taken;
            if (have - taken <= 1e-4f)
            {
                stock[Active.id] = 0f;
                var depleted = Active;
                Active = null;
                Depleted?.Invoke(depleted);
                Changed?.Invoke();
            }
            return taken;
        }

        /// <summary>Sélectionne un produit en stock (null = détergent standard). Faux si le produit n'est pas disponible.</summary>
        public bool Select(ProductDefinition product)
        {
            if (product != null && StockOf(product) <= 1e-4f) return false;
            if (product == Active) return true;
            Active = product;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Passe au produit suivant en stock, puis revient au détergent standard.</summary>
        public ProductDefinition SelectNext()
        {
            int start = Active == null ? -1 : catalog.IndexOf(Active);
            for (int i = start + 1; i < catalog.Count; i++)
                if (StockOf(catalog[i]) > 1e-4f)
                {
                    Select(catalog[i]);
                    return Active;
                }
            Select(null);
            return null;
        }

        /// <summary>Stocks à sauvegarder (produits non vides uniquement).</summary>
        public List<KeyValuePair<string, float>> Snapshot()
        {
            var list = new List<KeyValuePair<string, float>>();
            foreach (var product in catalog)
            {
                float liters = StockOf(product);
                if (liters > 1e-4f) list.Add(new KeyValuePair<string, float>(product.id, liters));
            }
            return list;
        }

        /// <summary>Restaure les stocks (identifiants inconnus ignorés, quantités bornées) et le produit actif s'il est en stock.</summary>
        public void Restore(IEnumerable<KeyValuePair<string, float>> entries, string activeId)
        {
            stock.Clear();
            Active = null;
            if (entries != null)
                foreach (var entry in entries)
                {
                    var product = catalog.Find(p => p.id == entry.Key);
                    if (product == null || float.IsNaN(entry.Value)) continue;
                    stock[product.id] = Mathf.Clamp(entry.Value, 0f, product.maxStock);
                }
            if (!string.IsNullOrEmpty(activeId)) Select(catalog.Find(p => p.id == activeId));
            Changed?.Invoke();
        }
    }
}
