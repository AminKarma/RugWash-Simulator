using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Orders
{
    /// <summary>Objectifs de qualité d'une commande. Un critère désactivé n'est jamais pénalisé.</summary>
    [System.Serializable]
    public struct OrderObjectives
    {
        [Tooltip("Part de la saleté initiale à retirer (0-1).")]
        [Range(0.1f, 1f)] public float targetCleanliness;

        [Tooltip("Exiger un niveau maximal de résidus de produit.")]
        public bool limitResidue;
        [Range(0f, 1f)] public float maxResidue;

        [Tooltip("Exiger un tapis suffisamment sec (extraction).")]
        public bool limitMoisture;
        [Range(0f, 1f)] public float maxMoisture;

        [Tooltip("Durée visée en secondes pour le bonus de rapidité (0 = pas de bonus).")]
        [Min(0f)] public float bonusTimeSeconds;

        [Tooltip("Exiger des fibres intactes (tapis fragiles : usure moyenne maximale).")]
        public bool limitWear;
        [Range(0f, 1f)] public float maxWear;
    }

    /// <summary>Commande d'un client : tapis, saleté, difficulté, récompense et objectifs. L'état (disponible, active, terminée) est géré à l'exécution.</summary>
    [CreateAssetMenu(menuName = "RugWash/Commande", fileName = "Order_")]
    public sealed class OrderDefinition : ScriptableObject
    {
        public string id = "order";
        public string clientName = "Client";
        public string title = "Commande";
        [TextArea] public string description;

        [Header("Tapis")]
        public RugDefinition rug;
        public int dirtSeed = 1;
        [Tooltip("Saleté de la commande (remplace la saleté par défaut du tapis).")]
        public List<DirtLayer> dirt = new List<DirtLayer>();

        [Header("Difficulté et paiement")]
        [Range(1, 4)] public int difficulty = 1;
        [Min(0)] public int baseReward = 100;
        [Tooltip("Réputation nécessaire pour accepter la commande.")]
        [Min(0)] public int requiredReputation;

        [Header("Objectifs")]
        public OrderObjectives objectives = new OrderObjectives { targetCleanliness = 0.8f };
    }
}
