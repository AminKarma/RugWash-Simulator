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
        /// <summary>Gadget de farce acheté (identifiant de l'amélioration qui le débloque).</summary>
        public bool OwnsGadget(string upgradeId) => !string.IsNullOrEmpty(upgradeId) && owned.Contains(upgradeId);

        /// <summary>Vrai si l'identifiant correspond à une amélioration du catalogue.</summary>
        public bool IsKnown(string id) => id != null && catalog.Exists(u => u != null && u.id == id);

        /// <summary>Définition du catalogue de cet identifiant, ou null.</summary>
        public UpgradeDefinition Find(string id) => id == null ? null : catalog.Find(u => u != null && u.id == id);

        /// <summary>Définition de l'objet porté (outil acheté ou gadget de farce) de cet identifiant d'inventaire, ou null (kit de départ).</summary>
        public UpgradeDefinition ItemUpgrade(string itemId) => catalog.Find(u => u != null && Player.InventoryRules.ItemIdOf(u) == itemId && itemId != null);

        /// <summary>
        /// V0.2.5 — l'objet quitte les achats (revendu, perdu sous un coup de batte). Les niveaux d'amélioration de l'outil
        /// restent acquis : ils resservent si l'outil est racheté ou ramassé. Faux si l'objet n'était pas possédé.
        /// </summary>
        public bool Revoke(string id)
        {
            if (string.IsNullOrEmpty(id) || !owned.Remove(id)) return false;
            ApplyAll();
            return true;
        }

        /// <summary>V0.2.5 — objet obtenu sans achat (ramassé au sol). Faux s'il est inconnu ou déjà possédé.</summary>
        public bool Grant(string id)
        {
            if (!IsKnown(id) || !owned.Add(id)) return false;
            ApplyAll();
            return true;
        }

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
            equipment.FootprintTransfer = Stats.FootprintTransfer;
            foreach (var tool in tools.Tools)
            {
                tool.FiberMultiplier = Stats.FiberMultiplier(tool.Definition.id);
                tool.PowerMultiplier = Stats.PowerMultiplier(tool.Definition.id);
                tool.ConsumptionMultiplier = Stats.ConsumptionMultiplier(tool.Definition.id);
                tool.RadiusMultiplier = Stats.RadiusMultiplier(tool.Definition.id);
                tool.IsUnlocked = !tool.Definition.requiresUnlock || Stats.IsUnlocked(tool.Definition.id);
                tool.Level = Stats.ToolLevel(tool.Definition.id);
                tool.MaxLevel = MaxToolLevel(tool.Definition.id);
                tool.IsGolden = EquipmentStats.IsGolden(tool.Level, tool.MaxLevel);
            }
            tools.EnsureUsableToolSelected();
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

        /// <summary>Dernier niveau proposé pour cet outil dans le catalogue (0 : aucune amélioration par niveaux).</summary>
        public int MaxToolLevel(string toolId)
        {
            int max = 0;
            foreach (var upgrade in catalog)
                if (upgrade != null && upgrade.level > max && upgrade.targetToolId == toolId) max = upgrade.level;
            return max;
        }

        /// <summary>Niveau de l'outil pour ces achats (ceux d'un autre joueur en ligne, tenus par l'hôte).</summary>
        public int ToolLevelFor(ICollection<string> ownedIds, string toolId)
        {
            int level = 0;
            foreach (var upgrade in catalog)
                if (upgrade != null && upgrade.level > level && upgrade.targetToolId == toolId && ownedIds.Contains(upgrade.id)) level = upgrade.level;
            return level;
        }

        /// <summary>
        /// Outils dorés de ces achats, un bit par emplacement d'outil (bit 0 = emplacement 1) : l'hôte le calcule pour chaque
        /// joueur à partir de la carrière qu'il tient et le diffuse sur sa silhouette.
        /// </summary>
        public int GoldenMaskFor(ICollection<string> ownedIds)
        {
            int mask = 0;
            foreach (var tool in tools.Tools)
            {
                if (tool == null || tool.Definition == null) continue;
                int slot = tool.Definition.slot;
                if (slot < 1 || slot > 31) continue;
                string id = tool.Definition.id;
                if (EquipmentStats.IsGolden(ToolLevelFor(ownedIds, id), MaxToolLevel(id))) mask |= 1 << (slot - 1);
            }
            return mask;
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
                    string power = $"Puissance {ToolName(upgrade.targetToolId)} : ×{current:0.##} → ×{current * upgrade.value:0.##}";
                    if (upgrade.level <= 0) return power;
                    int max = MaxToolLevel(upgrade.targetToolId);
                    int currentLevel = stats.ToolLevel(upgrade.targetToolId);
                    float reachNow = EquipmentStats.LevelReach(currentLevel), reachNext = EquipmentStats.LevelReach(Mathf.Max(currentLevel, upgrade.level));
                    if (reachNext > reachNow + 1e-3f) power += $" · largeur ×{reachNow:0.##} → ×{reachNext:0.##}";
                    return $"Niveau {upgrade.level}/{max} (actuel : {currentLevel}) · {power}" +
                           (EquipmentStats.IsGolden(upgrade.level, max) ? " · outil doré, visible par tous" : string.Empty) + Requirement(upgrade);
                }
                case UpgradeEffect.ToolConsumption:
                {
                    float current = stats.ConsumptionMultiplier(upgrade.targetToolId);
                    return $"Consommation {ToolName(upgrade.targetToolId)} : ×{current:0.##} → ×{current * upgrade.value:0.##}";
                }
                case UpgradeEffect.ToolRadius:
                {
                    float current = stats.RadiusMultiplier(upgrade.targetToolId);
                    return $"Largeur {ToolName(upgrade.targetToolId)} : ×{current:0.##} → ×{current * upgrade.value:0.##}";
                }
                case UpgradeEffect.UnlockTool:
                {
                    var tool = FindTool(upgrade.targetToolId);
                    if (tool == null) return "Nouvel outil";
                    // Inventaire à 4 emplacements : l'outil acheté est porté s'il reste une place, sinon rangé (rappel du casier ;
                    // V0.3.0 : plus répété sur chaque ligne de la boutique, la description de l'outil suffit).
                    return $"Nouvel outil : {tool.Definition.displayName}";
                }
                case UpgradeEffect.FootprintTransfer:
                    return $"Boue laissée par vos pas : {stats.FootprintTransfer * 100f:0} % → {stats.FootprintTransfer * Mathf.Clamp01(upgrade.value) * 100f:0} %";
                case UpgradeEffect.FiberPickup:
                {
                    float current = stats.FiberMultiplier(upgrade.targetToolId);
                    return $"Poils et peluches ({ToolName(upgrade.targetToolId)}) : ×{current:0.##} → ×{current * upgrade.value:0.##}";
                }
                case UpgradeEffect.WorkshopEquipment:
                    switch (upgrade.targetToolId)
                    {
                        case RobotMachineId: return "Équipement de l'atelier : robot aspirateur sur sa base (aire de lavage)";
                        case Workshop.WorkshopExtension.ExtensionId:
                            return $"Zones de lavage : {(stats.HasMachine(Workshop.WorkshopExtension.ExtensionId) ? 2 : 1)} → 2 · place pour la machine à laver{Requirement(upgrade)}";
                        case Workshop.WashingMachine.MachineId:
                            return $"Machine à laver dans l'annexe : la zone de lavage se libère pendant le cycle{Requirement(upgrade)}";
                        case Workshop.WashingMachine.FastProgramId:
                            return $"Machine à laver : cycle raccourci (programme rapide){Requirement(upgrade)}";
                        default: return $"Machine de l'atelier{Requirement(upgrade)}";
                    }
                case UpgradeEffect.WorkshopTier:
                    return $"Décor de l'atelier : palier {Mathf.RoundToInt(upgrade.value)} / {MaxWorkshopTier} (actuel : {stats.WorkshopTier}){Requirement(upgrade)}";
                case UpgradeEffect.PrankGadget:
                    return $"Farce (touche G) · aucun dégât{Requirement(upgrade)}";
                case UpgradeEffect.InventorySlot:
                {
                    int now = Player.PlayerInventory.CapacityFor(stats.ExtraInventorySlots);
                    int next = Player.PlayerInventory.CapacityFor(stats.ExtraInventorySlots + Mathf.RoundToInt(upgrade.value));
                    return $"Inventaire : {now} → {next} emplacements (touche {Player.RugWashInputActions.SlotKeyLabel(next)}){Requirement(upgrade)}";
                }
                default:
                    return string.Empty;
            }
        }

        /// <summary>Identifiant d'équipement du robot aspirateur (amélioration « WorkshopEquipment »).</summary>
        public const string RobotMachineId = "robot";
        /// <summary>Dernier palier de décor (penthouse avec vue sur Los Angeles).</summary>
        public const int MaxWorkshopTier = 4;

        static string Requirement(UpgradeDefinition upgrade) =>
            upgrade.requiredReputation > 0 ? $" · réputation {upgrade.requiredReputation} requise" : string.Empty;

        /// <summary>Vrai si la machine d'atelier a été achetée.</summary>
        public bool OwnsMachine(string machineId) => Stats != null && Stats.HasMachine(machineId);
        /// <summary>Palier de décor de l'atelier de ce joueur.</summary>
        public int WorkshopTier => Stats != null ? Stats.WorkshopTier : 0;

        /// <summary>Vrai si le robot aspirateur a été acheté.</summary>
        public bool OwnsRobot => Stats != null && Stats.HasMachine(RobotMachineId);

        /// <summary>V0.4.2 : nom de l'outil tel qu'affiché (carte d'améliorations de la boutique) ; null s'il est inconnu.</summary>
        public string ToolDisplayName(string toolId)
        {
            var tool = FindTool(toolId);
            return tool != null ? tool.Definition.displayName : null;
        }

        string ToolName(string toolId)
        {
            var tool = FindTool(toolId);
            return tool != null ? tool.Definition.displayName.ToLowerInvariant() : toolId;
        }

        Cleaning.CleaningToolBase FindTool(string toolId)
        {
            foreach (var tool in tools.Tools)
                if (tool.Definition.id == toolId) return tool;
            return null;
        }
    }
}
