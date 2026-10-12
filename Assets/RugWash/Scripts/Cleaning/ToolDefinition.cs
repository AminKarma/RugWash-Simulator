using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    public enum ToolResource
    {
        None,
        /// <summary>Eau propre (consommée).</summary>
        Water,
        /// <summary>Détergent (consommé).</summary>
        Soap,
        /// <summary>Cuve de récupération d'eau sale (se remplit, à vider au caniveau).</summary>
        Recovery,
    }

    /// <summary>Réglages d'un outil de nettoyage, modifiables sans toucher au code.</summary>
    [CreateAssetMenu(menuName = "RugWash/Outil", fileName = "Tool_")]
    public sealed class ToolDefinition : ScriptableObject
    {
        public string id = "tool";
        public string displayName = "Outil";
        [TextArea] public string description;
        [Tooltip("Touche numérique de sélection (1-9).")]
        [Range(1, 9)] public int slot = 1;

        [Header("Action")]
        [Tooltip("Portée maximale de l'outil (m).")]
        public float range = 2.5f;
        [Tooltip("Rayon de la zone traitée (m).")]
        public float radius = 0.14f;
        [Tooltip("Netteté du bord de la zone (1 = très doux, 8 = très net).")]
        [Range(1f, 8f)] public float falloffHardness = 3f;
        [Tooltip("Fraction de saleté traitée par seconde à réponse nominale.")]
        public float power = 0.6f;
        [Tooltip("Humidité ajoutée par seconde (outils à eau).")]
        public float wettingPerSecond;

        [Tooltip("Forme de la zone traitée : 1 = disque, > 1 = éventail élargi perpendiculairement à la visée.")]
        [Range(1f, 4f)] public float spreadAspect = 1f;

        [Header("Consommation")]
        public ToolResource resource = ToolResource.None;
        [Tooltip("Unités de ressource consommées par seconde d'utilisation.")]
        public float resourcePerSecond;

        [Header("Ruissellement vers l'aire de lavage")]
        [Tooltip("Part de la saleté retirée du tapis qui finit sur le plan de travail autour du tapis.")]
        [Range(0f, 1f)] public float runoffDirt;
        [Tooltip("Part de la mousse retirée qui ruisselle sur le plan de travail.")]
        [Range(0f, 1f)] public float runoffFoam;
        [Tooltip("Part de l'eau consommée (litres) qui ruisselle sur le plan de travail.")]
        [Range(0f, 1f)] public float runoffWater;
        [Tooltip("La saleté évacuée est sèche (débris balayés) plutôt que de l'eau sale.")]
        public bool runoffIsDry;

        [Header("Boutique")]
        [Tooltip("L'outil doit être acheté (amélioration « déblocage ») avant d'être utilisable.")]
        public bool requiresUnlock;

        [Header("Vibrations (manette)")]
        [Tooltip("Moteur basse fréquence (grondement) pendant l'utilisation.")]
        [Range(0f, 1f)] public float rumbleLow = DefaultRumble;
        [Tooltip("Moteur haute fréquence (bourdonnement) pendant l'utilisation.")]
        [Range(0f, 1f)] public float rumbleHigh = DefaultRumble;

        public const float DefaultRumble = 0.1f;
    }
}
