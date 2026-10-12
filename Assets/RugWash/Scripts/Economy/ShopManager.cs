using System;
using RugWash.Audio;
using RugWash.Cleaning;
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
