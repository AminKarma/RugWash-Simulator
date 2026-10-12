using System;
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

        public bool IsConfigured(out string missing)
        {
            missing = wallet == null ? nameof(wallet) : upgrades == null ? nameof(upgrades) : null;
            return missing == null;
        }

        public PurchaseResult Check(UpgradeDefinition upgrade) => ShopRules.Check(upgrade, upgrades.OwnedIds, wallet.Wallet);

        public ProductPurchaseResult CheckProduct(ProductDefinition product) =>
            products == null ? ProductPurchaseResult.Unknown : ShopRules.CheckProduct(product, products.Book.FreeSpace(product), wallet.Wallet);

        /// <summary>Achète un flacon : débit puis ajout au stock, ou refus sans rien modifier.</summary>
        public ProductPurchaseResult TryBuyProduct(ProductDefinition product)
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
                    if (audioManager != null) audioManager.PlayInterface(audioManager.ConfirmClip);
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
            var result = ShopRules.TryPurchase(upgrade, upgrades.OwnedIds, wallet.Wallet);
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
