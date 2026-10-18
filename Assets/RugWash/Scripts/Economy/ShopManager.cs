using System;
using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Economy
{
    /// <summary>Boutique : achat validé par <see cref="ShopRules"/>, débit, puis application immédiate de l'effet.</summary>
    [DisallowMultipleComponent]
    public sealed class ShopManager : MonoBehaviour
    {
        [SerializeField] WalletManager wallet;
        [SerializeField] UpgradeManager upgrades;
        [Tooltip("Réputation de l'acheteur : certains articles l'exigent (optionnel).")]
        [SerializeField] ReputationManager reputation;
        [SerializeField] AudioManager audioManager;
        [Tooltip("Produits spécialisés vendus en flacons (optionnel).")]
        [SerializeField] ProductInventory products;

        public UpgradeManager Upgrades => upgrades;
        public ProductInventory Products => products;
        public WalletManager WalletManager => wallet;

        public event Action<UpgradeDefinition, PurchaseResult> PurchaseAttempted;

        /// <summary>Multijoueur, côté invité : les achats sont demandés à l'hôte (argent partagé de l'atelier). Null : solo ou hôte.</summary>
        public IWorkshopRequests Remote { get; set; }

        /// <summary>Réponse de l'hôte à un achat demandé par ce joueur (message, succès).</summary>
        public event Action<string, bool> RemoteResultReceived;

        public void NotifyRemoteResult(string message, bool success) => RemoteResultReceived?.Invoke(message, success);
        public event Action<ProductDefinition, ProductPurchaseResult> ProductPurchaseAttempted;
        /// <summary>Achat d'un bidon de détergent tenté (résultat).</summary>
        public event Action<ProductPurchaseResult> DetergentPurchaseAttempted;

        [Tooltip("Stock de détergent en bidons du joueur (optionnel).")]
        [SerializeField] DetergentSupply detergent;
        public DetergentSupply Detergent => detergent;
        [Tooltip("Inventaire porté : un objet ne se revend que rangé au casier (V0.2.5).")]
        [SerializeField] PlayerInventory inventory;

        /// <summary>Objet revendu (achat retiré, valeur créditée) : (objet, montant).</summary>
        public event Action<UpgradeDefinition, int> Sold;

        /// <summary>L'objet que débloque cet achat est-il porté (dans l'un des 4 emplacements) ?</summary>
        public bool IsCarried(UpgradeDefinition upgrade)
        {
            string item = InventoryRules.ItemIdOf(upgrade);
            if (item == null || inventory == null) return false;
            foreach (string carried in inventory.Carried)
                if (carried == item) return true;
            return false;
        }

        /// <summary>Revente possible ? (objet acheté qui se porte, possédé, rangé au casier.)</summary>
        public SaleResult CheckSale(UpgradeDefinition upgrade) => SessionEconomyRules.CheckSale(upgrade, upgrades.OwnedIds, IsCarried(upgrade));

        /// <summary>
        /// V0.2.5 — revend un objet à 50 % de son prix de base : il quitte les achats et la valeur est créditée une seule fois
        /// (en ligne : par l'hôte, dans l'argent de la session). Refusé si l'objet est porté, non revendable ou absent.
        /// </summary>
        public SaleResult TrySell(UpgradeDefinition upgrade)
        {
            var check = CheckSale(upgrade);
            if (check != SaleResult.Sold) return check;
            if (Remote != null)
            {
                Remote.RequestSale(upgrade.id);
                return SaleResult.Requested;
            }
            int value = SessionEconomyRules.ResaleValue(upgrade);
            var owned = new HashSet<string>(upgrades.OwnedIds);
            var result = SessionEconomyRules.TrySell(upgrade, owned, false, wallet.Wallet);
            if (result != SaleResult.Sold || !upgrades.Revoke(upgrade.id)) return result == SaleResult.Sold ? SaleResult.NotOwned : result;
            if (audioManager != null) audioManager.PlayInterface(audioManager.ConfirmClip);
            Sold?.Invoke(upgrade, value);
            return SaleResult.Sold;
        }

        public ProductPurchaseResult CheckDetergentCan() =>
            detergent == null ? ProductPurchaseResult.Unknown
            : DetergentRules.CheckCan(detergent.Stock, detergent.CanLiters, detergent.MaxStock, detergent.CanPrice, wallet.Balance);

        /// <summary>
        /// Achète un bidon de détergent de 40 L : débit puis ajout au stock, ou refus sans rien modifier.
        /// <paramref name="silent"/> : achat d'un invité appliqué par l'hôte (pas de son chez l'hôte).
        /// </summary>
        public ProductPurchaseResult TryBuyDetergentCan(bool silent = false)
        {
            var result = CheckDetergentCan();
            if (Remote != null && result == ProductPurchaseResult.Purchased)
            {
                Remote.RequestDetergentCan();
                return ProductPurchaseResult.Requested;
            }
            if (result == ProductPurchaseResult.Purchased)
            {
                if (!wallet.Wallet.TrySpend(detergent.CanPrice) || !detergent.AddCan()) result = ProductPurchaseResult.InsufficientFunds;
                else if (audioManager != null && !silent) audioManager.PlayInterface(audioManager.ConfirmClip);
            }
            DetergentPurchaseAttempted?.Invoke(result);
            return result;
        }

        public bool IsConfigured(out string missing)
        {
            missing = wallet == null ? nameof(wallet) : upgrades == null ? nameof(upgrades) : null;
            return missing == null;
        }

        /// <summary>
        /// Achat possible ? En ligne, côté invité (V0.2.5), les améliorations de l'atelier sont réservées à l'hôte ; l'hôte
        /// le vérifie aussi à la réception de la demande.
        /// </summary>
        public PurchaseResult Check(UpgradeDefinition upgrade)
        {
            if (Remote != null && upgrade != null && !SessionEconomyRules.IsPersonal(upgrade))
                return upgrades.OwnedIds.Contains(upgrade.id) ? PurchaseResult.AlreadyOwned : PurchaseResult.HostOnly;
            return ShopRules.Check(upgrade, upgrades.OwnedIds, wallet.Wallet, Reputation);
        }

        int Reputation => reputation != null ? reputation.Points : int.MaxValue;

        public ProductPurchaseResult CheckProduct(ProductDefinition product) =>
            products == null ? ProductPurchaseResult.Unknown : ShopRules.CheckProduct(product, products.Book.FreeSpace(product), wallet.Wallet);

        /// <summary>Achète un flacon : débit puis ajout au stock, ou refus sans rien modifier (<paramref name="silent"/> : achat d'un invité).</summary>
        public ProductPurchaseResult TryBuyProduct(ProductDefinition product, bool silent = false)
        {
            var result = CheckProduct(product);
            if (Remote != null && result == ProductPurchaseResult.Purchased)
            {
                Remote.RequestProduct(product.id);
                return ProductPurchaseResult.Requested;
            }
            if (result == ProductPurchaseResult.Purchased)
            {
                if (!wallet.Wallet.TrySpend(product.packPrice)) result = ProductPurchaseResult.InsufficientFunds;
                else
                {
                    products.Book.Add(product, product.packLiters);
                    if (audioManager != null && !silent) audioManager.PlayInterface(audioManager.ConfirmClip);
                }
            }
            ProductPurchaseAttempted?.Invoke(product, result);
            return result;
        }

        public PurchaseResult TryPurchase(UpgradeDefinition upgrade)
        {
            if (Remote != null)
            {
                var check = Check(upgrade);
                if (check != PurchaseResult.Purchased) return check;
                Remote.RequestUpgrade(upgrade.id);
                return PurchaseResult.Requested;
            }
            var result = ShopRules.TryPurchase(upgrade, upgrades.OwnedIds, wallet.Wallet, Reputation);
            if (result == PurchaseResult.Purchased)
            {
                upgrades.ApplyAll();
                if (audioManager != null) audioManager.PlayInterface(audioManager.ConfirmClip);
            }
            PurchaseAttempted?.Invoke(upgrade, result);
            return result;
        }
    }
}
