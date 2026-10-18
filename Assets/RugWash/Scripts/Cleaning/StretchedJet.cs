using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Jet de gouttes étirées selon leur vitesse (lance, seau, jet vu sur la silhouette d'un autre joueur). Une particule
    /// « étirée » est dessinée de sa position vers l'ARRIÈRE de sa trajectoire, sur une longueur proportionnelle à sa
    /// vitesse (≈ 0,5 m pour la lance à 14 m/s, jusqu'à 1,3 m pour le jet d'un autre joueur) : émises sur la buse, les gouttes
    /// semblaient sortir de la main ou du corps. L'émission est avancée de la traînée de la goutte la plus lente, le long de
    /// l'axe du jet : chaque goutte naît avec sa queue sur la buse (point d'attache du modèle, transform « Tip »).
    /// </summary>
    public static class StretchedJet
    {
        /// <summary>
        /// Aligne l'émission sur la buse. <paramref name="slowestSpeed"/> : vitesse de départ la plus faible (m/s), multiplicateur
        /// compris. Sans effet si les particules ne sont pas étirées.
        /// </summary>
        public static void AlignToNozzle(ParticleSystem system, ParticleSystemRenderer renderer, float slowestSpeed)
        {
            if (system == null || renderer == null || renderer.renderMode != ParticleSystemRenderMode.Stretch) return;
            float tail = TailLength(renderer.velocityScale, slowestSpeed);
            var shape = system.shape;
            if (Mathf.Abs(shape.position.z - tail) > 0.002f) shape.position = new Vector3(0f, 0f, tail);
        }

        /// <summary>Longueur de la traînée d'une goutte (m) : échelle de vitesse du rendu × vitesse.</summary>
        public static float TailLength(float velocityScale, float speed) => Mathf.Max(0f, velocityScale) * Mathf.Max(0f, speed);

        /// <summary>Vitesse de départ la plus faible définie sur le système (sans multiplicateur appliqué ensuite).</summary>
        public static float SlowestStartSpeed(ParticleSystem system)
        {
            if (system == null) return 0f;
            var speed = system.main.startSpeed;
            return speed.mode == ParticleSystemCurveMode.TwoConstants ? speed.constantMin : speed.constant;
        }
    }
}
