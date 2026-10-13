using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Economy
{
    /// <summary>
    /// Progression de carrière d'un joueur (argent, réputation, améliorations, produits spécialisés). En partie en ligne,
    /// l'hôte tient une carrière par invité : il y applique les achats, les consommations et les récompenses que lui seul
    /// valide, puis renvoie le résultat à l'invité, qui la sauvegarde chez lui. La carrière de l'hôte n'est jamais copiée
    /// chez un invité, et inversement.
    /// </summary>
    public sealed class PlayerCareer
    {
        public int Money;
        public int Reputation;
        public readonly HashSet<string> Upgrades = new HashSet<string>();
        public readonly Dictionary<string, float> Products = new Dictionary<string, float>();
        public string ActiveProduct = string.Empty;

        public PlayerCareer Clone()
        {
            var copy = new PlayerCareer { Money = Money, Reputation = Reputation, ActiveProduct = ActiveProduct };
            copy.Upgrades.UnionWith(Upgrades);
            foreach (var pair in Products) copy.Products[pair.Key] = pair.Value;
            return copy;
        }

        public float StockOf(string productId) => productId != null && Products.TryGetValue(productId, out float liters) ? liters : 0f;
    }

    /// <summary>Règles pures appliquées par l'autorité (hôte) à la carrière d'un joueur (testées en EditMode).</summary>
    public static class CareerRules
    {
        public const int MaxReputation = 1_000_000;

        /// <summary>
        /// Carrière annoncée par un invité à son arrivée (lue dans SA sauvegarde) : bornée et filtrée sur le catalogue de
        /// l'hôte (identifiants inconnus ignorés, stocks ramenés à la capacité). Pendant la partie, seul l'hôte la modifie.
        /// </summary>
        public static PlayerCareer Sanitize(PlayerCareer raw, IReadOnlyList<UpgradeDefinition> upgrades, IReadOnlyList<ProductDefinition> products)
        {
            var clean = new PlayerCareer();
            if (raw == null) return clean;
            clean.Money = Mathf.Clamp(raw.Money, 0, Save.SaveValidator.MaxMoney);
            clean.Reputation = Mathf.Clamp(raw.Reputation, 0, MaxReputation);
            if (upgrades != null)
                foreach (var upgrade in upgrades)
                    if (upgrade != null && raw.Upgrades.Contains(upgrade.id)) clean.Upgrades.Add(upgrade.id);
            if (products != null)
                foreach (var product in products)
                {
                    if (product == null) continue;
                    float liters = Mathf.Clamp(raw.StockOf(product.id), 0f, product.maxStock);
                    if (liters > 0f) clean.Products[product.id] = liters;
                    if (raw.ActiveProduct == product.id) clean.ActiveProduct = product.id;
                }
            return clean;
        }

        /// <summary>Achat d'une amélioration avec l'argent de cette carrière (mêmes règles que la boutique).</summary>
        public static PurchaseResult TryBuyUpgrade(PlayerCareer career, UpgradeDefinition upgrade)
        {
            if (career == null || upgrade == null) return PurchaseResult.Unknown;
            var wallet = new Wallet(career.Money);
            var result = ShopRules.TryPurchase(upgrade, career.Upgrades, wallet);
            if (result == PurchaseResult.Purchased) career.Money = wallet.Balance;
            return result;
        }

        /// <summary>Achat d'un flacon : il doit tenir dans le stock de cette carrière et être payable.</summary>
        public static ProductPurchaseResult TryBuyProduct(PlayerCareer career, ProductDefinition product)
        {
            if (career == null || product == null) return ProductPurchaseResult.Unknown;
            var wallet = new Wallet(career.Money);
            var result = ShopRules.CheckProduct(product, Mathf.Max(0f, product.maxStock - career.StockOf(product.id)), wallet);
            if (result != ProductPurchaseResult.Purchased || !wallet.TrySpend(product.packPrice)) return result == ProductPurchaseResult.Purchased
                ? ProductPurchaseResult.InsufficientFunds : result;
            career.Money = wallet.Balance;
            career.Products[product.id] = Mathf.Min(product.maxStock, career.StockOf(product.id) + product.packLiters);
            return ProductPurchaseResult.Purchased;
        }

        /// <summary>Retire du stock ce que le joueur a réellement pulvérisé ; retourne la quantité prélevée.</summary>
        public static float Consume(PlayerCareer career, string productId, float liters)
        {
            if (career == null || liters <= 0f) return 0f;
            float stock = career.StockOf(productId);
            float taken = Mathf.Min(stock, liters);
            if (taken <= 0f) return 0f;
            if (stock - taken <= 1e-4f) career.Products.Remove(productId);
            else career.Products[productId] = stock - taken;
            return taken;
        }

        /// <summary>
        /// Récompense d'une commande livrée (même résultat pour tous les participants) : paiement si le client accepte le
        /// tapis, variation de réputation dans tous les cas (bornée à zéro).
        /// </summary>
        public static void Reward(PlayerCareer career, OrderResult result)
        {
            if (career == null || result == null) return;
            if (result.Accepted) career.Money = Mathf.Min(Save.SaveValidator.MaxMoney, career.Money + Mathf.Max(0, result.Reward));
            career.Reputation = Mathf.Clamp(career.Reputation + result.ReputationDelta, 0, MaxReputation);
        }
    }
}
