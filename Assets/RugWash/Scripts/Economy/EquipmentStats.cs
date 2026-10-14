using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Economy
{
    /// <summary>
    /// Caractéristiques effectives de l'équipement, calculées à partir des améliorations possédées
    /// (règle pure, testée). Ces valeurs sont réellement utilisées par les réservoirs et les outils.
    /// </summary>
    public sealed class EquipmentStats
    {
        public float WaterCapacity;
        public float SoapCapacity;
        public float RecoveryCapacity;
        readonly Dictionary<string, float> power = new Dictionary<string, float>();
        readonly Dictionary<string, float> consumption = new Dictionary<string, float>();
        readonly Dictionary<string, float> radius = new Dictionary<string, float>();
        readonly Dictionary<string, float> fiber = new Dictionary<string, float>();
        readonly HashSet<string> unlocked = new HashSet<string>();
        readonly HashSet<string> machines = new HashSet<string>();

        /// <summary>Part de la boue des semelles transférée à chaque pas (1 = chaussures ordinaires).</summary>
        public float FootprintTransfer = 1f;
        /// <summary>Palier de décor de l'atelier (0 = cagibi de départ).</summary>
        public int WorkshopTier;

        /// <summary>Multiplicateur de l'outil sur les poils, cheveux et peluches (accessoire).</summary>
        public float FiberMultiplier(string toolId) => toolId != null && fiber.TryGetValue(toolId, out float v) ? v : 1f;
        /// <summary>Vrai si l'équipement d'atelier (robot…) a été acheté.</summary>
        public bool HasMachine(string machineId) => machineId != null && machines.Contains(machineId);

        public float PowerMultiplier(string toolId) => toolId != null && power.TryGetValue(toolId, out float v) ? v : 1f;
        public float RadiusMultiplier(string toolId) => toolId != null && radius.TryGetValue(toolId, out float v) ? v : 1f;
        /// <summary>Vrai si l'outil a été débloqué par un achat (les outils sans déblocage sont toujours utilisables).</summary>
        public bool IsUnlocked(string toolId) => toolId != null && unlocked.Contains(toolId);
        public float ConsumptionMultiplier(string toolId) => toolId != null && consumption.TryGetValue(toolId, out float v) ? v : 1f;

        public static EquipmentStats Compute(float baseWater, float baseSoap, float baseRecovery, IEnumerable<UpgradeDefinition> owned)
        {
            var stats = new EquipmentStats { WaterCapacity = baseWater, SoapCapacity = baseSoap, RecoveryCapacity = baseRecovery };
            foreach (var upgrade in owned)
            {
                if (upgrade == null) continue;
                switch (upgrade.effect)
                {
                    case UpgradeEffect.WaterCapacity: stats.WaterCapacity += upgrade.value; break;
                    case UpgradeEffect.SoapCapacity: stats.SoapCapacity += upgrade.value; break;
                    case UpgradeEffect.RecoveryCapacity: stats.RecoveryCapacity += upgrade.value; break;
                    case UpgradeEffect.ToolPower: Multiply(stats.power, upgrade.targetToolId, upgrade.value); break;
                    case UpgradeEffect.ToolConsumption: Multiply(stats.consumption, upgrade.targetToolId, upgrade.value); break;
                    case UpgradeEffect.ToolRadius: Multiply(stats.radius, upgrade.targetToolId, upgrade.value); break;
                    case UpgradeEffect.UnlockTool:
                        if (!string.IsNullOrEmpty(upgrade.targetToolId)) stats.unlocked.Add(upgrade.targetToolId);
                        break;
                    case UpgradeEffect.FootprintTransfer: stats.FootprintTransfer *= Mathf.Clamp01(upgrade.value); break;
                    case UpgradeEffect.FiberPickup: Multiply(stats.fiber, upgrade.targetToolId, upgrade.value); break;
                    case UpgradeEffect.WorkshopEquipment:
                        if (!string.IsNullOrEmpty(upgrade.targetToolId)) stats.machines.Add(upgrade.targetToolId);
                        break;
                    case UpgradeEffect.WorkshopTier: stats.WorkshopTier = Mathf.Max(stats.WorkshopTier, Mathf.RoundToInt(upgrade.value)); break;
                }
            }
            return stats;
        }

        static void Multiply(Dictionary<string, float> table, string id, float value)
        {
            if (string.IsNullOrEmpty(id)) return;
            table[id] = (table.TryGetValue(id, out float current) ? current : 1f) * value;
        }
    }

    public enum PurchaseResult
    {
        Purchased,
        AlreadyOwned,
        MissingPrerequisite,
        InsufficientFunds,
        Unknown,
        /// <summary>Réputation inférieure à celle requise par l'article.</summary>
        ReputationTooLow,
        /// <summary>Multijoueur (invité) : demande envoyée à l'hôte.</summary>
        Requested = 100,
    }

    public enum ProductPurchaseResult
    {
        Purchased,
        /// <summary>Un flacon entier ne tient plus dans le stock transportable.</summary>
        StockFull,
        InsufficientFunds,
        Unknown,
        /// <summary>Multijoueur (invité) : demande envoyée à l'hôte.</summary>
        Requested = 100,
    }

    /// <summary>Règle d'achat pure : le solde n'est débité que si l'achat est entièrement valide.</summary>
    public static class ShopRules
    {
        /// <param name="reputation">Réputation de l'acheteur (sans valeur : aucune condition de réputation vérifiée).</param>
        public static PurchaseResult Check(UpgradeDefinition upgrade, ICollection<string> owned, Wallet wallet, int reputation = int.MaxValue)
        {
            if (upgrade == null) return PurchaseResult.Unknown;
            if (owned.Contains(upgrade.id)) return PurchaseResult.AlreadyOwned;
            if (upgrade.prerequisite != null && !owned.Contains(upgrade.prerequisite.id)) return PurchaseResult.MissingPrerequisite;
            if (reputation < upgrade.requiredReputation) return PurchaseResult.ReputationTooLow;
            if (!wallet.CanAfford(upgrade.price)) return PurchaseResult.InsufficientFunds;
            return PurchaseResult.Purchased;
        }

        public static PurchaseResult TryPurchase(UpgradeDefinition upgrade, ICollection<string> owned, Wallet wallet, int reputation = int.MaxValue)
        {
            var check = Check(upgrade, owned, wallet, reputation);
            if (check != PurchaseResult.Purchased) return check;
            if (!wallet.TrySpend(upgrade.price)) return PurchaseResult.InsufficientFunds;
            owned.Add(upgrade.id);
            return PurchaseResult.Purchased;
        }

        /// <summary>Achat d'un flacon de produit : il doit tenir entièrement dans le stock et être payable.</summary>
        public static ProductPurchaseResult CheckProduct(Cleaning.ProductDefinition product, float freeSpace, Wallet wallet)
        {
            if (product == null) return ProductPurchaseResult.Unknown;
            if (freeSpace + 1e-4f < product.packLiters) return ProductPurchaseResult.StockFull;
            if (!wallet.CanAfford(product.packPrice)) return ProductPurchaseResult.InsufficientFunds;
            return ProductPurchaseResult.Purchased;
        }

        public static string Describe(ProductPurchaseResult result)
        {
            switch (result)
            {
                case ProductPurchaseResult.Purchased: return "Achat effectué";
                case ProductPurchaseResult.StockFull: return "Stock plein";
                case ProductPurchaseResult.Requested: return "Demande envoyée à l'hôte…";
                case ProductPurchaseResult.InsufficientFunds: return "Fonds insuffisants";
                default: return "Article inconnu";
            }
        }

        public static string Describe(PurchaseResult result)
        {
            switch (result)
            {
                case PurchaseResult.Purchased: return "Achat effectué";
                case PurchaseResult.AlreadyOwned: return "Déjà acheté";
                case PurchaseResult.MissingPrerequisite: return "Amélioration précédente requise";
                case PurchaseResult.InsufficientFunds: return "Fonds insuffisants";
                case PurchaseResult.ReputationTooLow: return "Réputation insuffisante";
                case PurchaseResult.Requested: return "Demande envoyée à l'hôte…";
                default: return "Article inconnu";
            }
        }
    }
}
