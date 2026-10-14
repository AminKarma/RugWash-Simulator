using UnityEngine;

namespace RugWash.Economy
{
    public enum UpgradeEffect
    {
        /// <summary>Capacité ajoutée au réservoir d'eau (L).</summary>
        WaterCapacity,
        /// <summary>Capacité ajoutée au réservoir de détergent (L).</summary>
        SoapCapacity,
        /// <summary>Capacité ajoutée à la cuve de récupération (L).</summary>
        RecoveryCapacity,
        /// <summary>Multiplicateur de consommation de l'outil ciblé (&lt; 1 = économie).</summary>
        ToolConsumption,
        /// <summary>Multiplicateur de puissance de l'outil ciblé.</summary>
        ToolPower,
        /// <summary>Rend utilisable un outil « à débloquer » (achat de l'outil).</summary>
        UnlockTool,
        /// <summary>Multiplicateur de la largeur (rayon) de la zone traitée par l'outil ciblé.</summary>
        ToolRadius,
        /// <summary>Multiplicateur de la boue transférée par les semelles à chaque pas (bottes, v0.1.2 : &lt; 1).</summary>
        FootprintTransfer,
        /// <summary>Multiplicateur de l'outil ciblé sur les poils, cheveux et peluches (accessoire, v0.1.2).</summary>
        FiberPickup,
        /// <summary>Équipement de l'atelier (machine) rendu disponible, identifié par <see cref="UpgradeDefinition.targetToolId"/>.</summary>
        WorkshopEquipment,
    }

    /// <summary>Amélioration achetable. Prix et valeurs modifiables dans l'Inspector.</summary>
    [CreateAssetMenu(menuName = "RugWash/Amélioration", fileName = "Upgrade_")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        public string id = "upgrade";
        public string displayName = "Amélioration";
        [TextArea] public string description;
        [Min(0)] public int price = 100;
        public UpgradeEffect effect;
        [Tooltip("Litres ajoutés (capacités) ou multiplicateur (puissance, consommation).")]
        public float value = 1f;
        [Tooltip("Identifiant de l'outil ciblé (ToolDefinition.id) pour les effets d'outil.")]
        public string targetToolId;
        [Tooltip("Amélioration à posséder avant celle-ci (optionnel).")]
        public UpgradeDefinition prerequisite;
    }
}
