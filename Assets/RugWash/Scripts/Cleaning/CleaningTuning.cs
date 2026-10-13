using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Réglages centralisés de la simulation lente des surfaces (mousse, séchage) et du ruissellement vers l'aire
    /// de lavage. Modifiables dans l'Inspector sans toucher au code ; valeurs par défaut documentées dans le README.
    /// </summary>
    [CreateAssetMenu(menuName = "RugWash/Réglages de nettoyage", fileName = "CleaningTuning")]
    public sealed class CleaningTuning : ScriptableObject
    {
        [Header("Mousse")]
        [Tooltip("Fraction de la mousse qui retombe par seconde.")]
        [Range(0f, 0.2f)] public float foamDecayPerSecond = 0.02f;
        [Tooltip("Retombée minimale par seconde : la mousse finit toujours par disparaître.")]
        [Range(0f, 0.05f)] public float foamDecayFloor = 0.004f;
        [Tooltip("Saleté fixée détachée par seconde sous une mousse pleine (taches réactives au savon).")]
        [Range(0f, 0.1f)] public float foamDwellPower = 0.012f;
        [Tooltip("Humidité produite par unité de mousse retombée.")]
        [Range(0f, 1f)] public float foamToMoisture = 0.25f;
        [Tooltip("Intervalle de simulation lente (s).")]
        [Range(0.05f, 1f)] public float agingInterval = 0.2f;

        [Header("Ruissellement vers le plan de travail")]
        [Tooltip("Distance du dépôt au-delà du bord du tapis (m).")]
        [Range(0.05f, 0.8f)] public float runoffEdgeOffset = 0.2f;
        [Tooltip("Rayon d'une flaque de ruissellement (m).")]
        [Range(0.05f, 0.6f)] public float runoffRadius = 0.22f;
        [Tooltip("Intervalle de regroupement des dépôts (s).")]
        [Range(0.05f, 1f)] public float runoffFlushInterval = 0.15f;
        [Tooltip("Multiplicateur global des quantités déposées (équilibrage).")]
        [Range(0f, 5f)] public float runoffScale = 1f;

        [Header("Aire de lavage (zone de nettoyage)")]
        [Tooltip("Propreté minimale de l'aire de lavage pour accepter une nouvelle commande (0..1). " +
                 "Propreté = part de la surface non souillée = 1 − saleté.")]
        [Range(0f, 1f)] public float zoneMinCleanlinessToStart = 0.25f;
        [Tooltip("Saleté de l'aire de lavage (part de la surface souillée) au-delà de laquelle le tapis posé dessus se salit.")]
        [Range(0f, 1f)] public float zoneDirtyThreshold = 0.4f;
        [Tooltip("Saleté déposée par seconde sur la bordure du tapis quand l'aire est entièrement souillée " +
                 "(proportionnelle à l'excès au-delà du seuil ; 0 = jamais en continu).")]
        [Range(0f, 0.2f)] public float zoneContaminationPerSecond = 0.015f;

        [Header("Fin du tapis")]
        [Tooltip("Le tapis est terminé quand la saleté restante passe sous cette part de la saleté initiale " +
                 "(0,01 = il reste moins de 1 %). Le reliquat est alors retiré : la propreté affiche 100 %.")]
        [Range(0f, 0.05f)] public float rugFinishedRemaining = 0.01f;

        /// <summary>Valeurs de référence utilisées si aucun réglage n'est assigné (tests, scènes minimales).</summary>
        public static CleaningTuning CreateDefault()
        {
            var tuning = CreateInstance<CleaningTuning>();
            tuning.hideFlags = HideFlags.DontSave;
            return tuning;
        }
    }
}
