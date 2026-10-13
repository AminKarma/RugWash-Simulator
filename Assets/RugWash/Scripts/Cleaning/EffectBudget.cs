using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Budget des effets visuels (particules) appliqué à tous les systèmes du jeu : densité (débit et nombre maximal de
    /// particules), durée de vie, et taille maximale d'une particule à l'écran. Ce dernier plafond évite qu'un jet ou une
    /// mousse émis juste devant la caméra couvre une grande partie de l'écran (surdessin coûteux pour le GPU) ; à distance,
    /// l'aspect ne change pas. Réglages dans <see cref="CleaningTuning"/> (section « Effets visuels »). Purement visuel :
    /// le nettoyage réel (grilles) n'en dépend jamais.
    /// </summary>
    public static class EffectBudget
    {
        public static float Density { get; private set; } = 1f;
        public static float Lifetime { get; private set; } = 1f;
        public static float MaxScreenSize { get; private set; } = 0.12f;

        public static void Apply(CleaningTuning tuning)
        {
            if (tuning == null) return;
            Density = Mathf.Clamp(tuning.effectDensity, 0.1f, 2f);
            Lifetime = Mathf.Clamp(tuning.effectLifetime, 0.25f, 2f);
            MaxScreenSize = Mathf.Clamp(tuning.maxParticleScreenSize, 0.01f, 1f);
        }

        /// <summary>Débit d'émission après application de la densité.</summary>
        public static float Rate(float rate) => rate * Density;

        /// <summary>
        /// Applique durée de vie, nombre maximal et taille maximale à l'écran à un système, à partir de ses valeurs d'origine
        /// (conservées : un nouvel appel après un changement de réglage repart toujours de l'original).
        /// </summary>
        public static void Configure(ParticleSystem system)
        {
            if (system == null) return;
            if (!system.TryGetComponent(out EffectBudgetOrigin origin))
            {
                origin = system.gameObject.AddComponent<EffectBudgetOrigin>();
                origin.Capture(system);
            }
            var main = system.main;
            main.startLifetimeMultiplier = origin.Lifetime * Lifetime;
            main.maxParticles = Mathf.Max(1, Mathf.CeilToInt(origin.MaxParticles * Density));
            if (system.TryGetComponent(out ParticleSystemRenderer renderer))
                renderer.maxParticleSize = Mathf.Min(origin.MaxScreenSize, MaxScreenSize);
        }
    }
}
