using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Valeurs d'origine d'un système de particules (avant le budget d'effets).</summary>
    [DisallowMultipleComponent]
    public sealed class EffectBudgetOrigin : MonoBehaviour
    {
        public float Lifetime { get; private set; } = 1f;
        public int MaxParticles { get; private set; } = 100;
        public float MaxScreenSize { get; private set; } = 0.5f;

        public void Capture(ParticleSystem system)
        {
            var main = system.main;
            Lifetime = main.startLifetimeMultiplier;
            MaxParticles = main.maxParticles;
            MaxScreenSize = system.TryGetComponent(out ParticleSystemRenderer renderer) ? renderer.maxParticleSize : 0.5f;
        }
    }
}
