using System;
using System.Collections.Generic;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Economy
{
    /// <summary>
    /// Améliorations possédées et application de leurs effets réels : capacités des réservoirs
    /// et multiplicateurs de puissance / consommation des outils.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeManager : MonoBehaviour
    {
        [SerializeField] List<UpgradeDefinition> catalog = new List<UpgradeDefinition>();
        [SerializeField] PlayerEquipment equipment;
        [SerializeField] PlayerToolController tools;

        readonly HashSet<string> owned = new HashSet<string>();
        float baseWater, baseSoap, baseRecovery;
        bool baseCaptured;

        public IReadOnlyList<UpgradeDefinition> Catalog => catalog;
        public ICollection<string> OwnedIds => owned;
        public EquipmentStats Stats { get; private set; }

        public event Action Changed;

        public bool IsConfigured(out string missing)
        {
            missing = equipment == null ? nameof(equipment) : tools == null ? nameof(tools)
                : catalog.Count == 0 || catalog.Contains(null) ? nameof(catalog) : null;
            return missing == null;
        }

        void Awake() => CaptureBase();

        void CaptureBase()
        {
            if (baseCaptured || equipment == null) return;
            baseWater = equipment.Water.Capacity;
            baseSoap = equipment.Soap.Capacity;
            baseRecovery = equipment.Recovery.Capacity;
            baseCaptured = true;
        }

        void Start() => ApplyAll();

        public bool IsOwned(UpgradeDefinition upgrade) => upgrade != null && owned.Contains(upgrade.id);

        /// <summary>Calcule et applique les effets de toutes les améliorations possédées.</summary>
        public void ApplyAll()
        {
            CaptureBase();
            var ownedDefinitions = new List<UpgradeDefinition>();
            foreach (var upgrade in catalog)
                if (upgrade != null && owned.Contains(upgrade.id)) ownedDefinitions.Add(upgrade);

            Stats = EquipmentStats.Compute(baseWater, baseSoap, baseRecovery, ownedDefinitions);
            equipment.Water.SetCapacity(Stats.WaterCapacity);
            equipment.Soap.SetCapacity(Stats.SoapCapacity);
            equipment.Recovery.SetCapacity(Stats.RecoveryCapacity);
            foreach (var tool in tools.Tools)
            {
                tool.PowerMultiplier = Stats.PowerMultiplier(tool.Definition.id);
                tool.ConsumptionMultiplier = Stats.ConsumptionMultiplier(tool.Definition.id);
            }
            Changed?.Invoke();
        }

        /// <summary>Restaure des achats (sauvegarde, phase 4). Les identifiants inconnus sont ignorés.</summary>
        public void Restore(IEnumerable<string> ids)
        {
            owned.Clear();
            foreach (string id in ids)
                if (catalog.Exists(u => u != null && u.id == id)) owned.Add(id);
            ApplyAll();
        }

        /// <summary>Description de l'effet réel, avec la valeur actuelle → la valeur après achat.</summary>
        public string DescribeEffect(UpgradeDefinition upgrade)
        {
            CaptureBase();
            var stats = Stats ?? EquipmentStats.Compute(baseWater, baseSoap, baseRecovery, Array.Empty<UpgradeDefinition>());
            switch (upgrade.effect)
            {
                case UpgradeEffect.WaterCapacity:
                    return $"Réservoir d'eau : {stats.WaterCapacity:0} L → {stats.WaterCapacity + upgrade.value:0} L";
                case UpgradeEffect.SoapCapacity:
                    return $"Réservoir de détergent : {stats.SoapCapacity:0.#} L → {stats.SoapCapacity + upgrade.value:0.#} L";
                case UpgradeEffect.RecoveryCapacity:
                    return $"Cuve de récupération : {stats.RecoveryCapacity:0} L → {stats.RecoveryCapacity + upgrade.value:0} L";
                case UpgradeEffect.ToolPower:
                {
                    float current = stats.PowerMultiplier(upgrade.targetToolId);
                    return $"Puissance {ToolName(upgrade.targetToolId)} : ×{current:0.##} → ×{current * upgrade.value:0.##}";
                }
                case UpgradeEffect.ToolConsumption:
                {
                    float current = stats.ConsumptionMultiplier(upgrade.targetToolId);
                    return $"Consommation {ToolName(upgrade.targetToolId)} : ×{current:0.##} → ×{current * upgrade.value:0.##}";
                }
                default:
                    return string.Empty;
            }
        }

        string ToolName(string toolId)
        {
            foreach (var tool in tools.Tools)
                if (tool.Definition.id == toolId) return tool.Definition.displayName.ToLowerInvariant();
            return toolId;
        }
    }
}
