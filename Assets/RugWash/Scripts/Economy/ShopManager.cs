using System;
using RugWash.Audio;
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

        public UpgradeManager Upgrades => upgrades;
        public WalletManager WalletManager => wallet;

        public event Action<UpgradeDefinition, PurchaseResult> PurchaseAttempted;

        public bool IsConfigured(out string missing)
        {
            missing = wallet == null ? nameof(wallet) : upgrades == null ? nameof(upgrades) : null;
            return missing == null;
        }

        public PurchaseResult Check(UpgradeDefinition upgrade) => ShopRules.Check(upgrade, upgrades.OwnedIds, wallet.Wallet);

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
