using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Lance de rinçage : consomme de l'eau en continu pendant l'utilisation (même hors du tapis),
    /// évacue la saleté détachée et la mousse, et mouille les fibres.
    /// </summary>
    public sealed class WaterRinseTool : CleaningToolBase
    {
        [Header("Effets")]
        [SerializeField] ParticleSystem jetParticles;
        [SerializeField] ParticleSystem splashParticles;
        [SerializeField] float jetRate = 260f;
        [SerializeField] float splashRate = 140f;

        float removedPerSecond;
        float recoil;

        public float LastRemovedPerSecond => removedPerSecond;
        public override string ActionVerb => "rincer";

        public override bool IsConfigured(out string missing)
        {
            if (!base.IsConfigured(out missing)) return false;
            if (definition.resource != ToolResource.Water) missing = "definition.resource (doit être Water)";
            return missing == null;
        }

        protected override float ContactIntensity => IsAffectingRug ? 1f : 0.3f;

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            var operation = new RinseOperation
            {
                Table = rug.DirtTable,
                Power = EffectivePower * supply,
                WettingPerSecond = definition.wettingPerSecond * supply,
                DeltaTime = deltaTime,
            };
            rug.ApplyOperation(aim.Point, definition.radius, definition.falloffHardness, ref operation);
            removedPerSecond = operation.Removed / deltaTime;
            return operation.CellsCleaned;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug) removedPerSecond = 0f;

            if (jetParticles != null)
            {
                var emission = jetParticles.emission;
                emission.rateOverTimeMultiplier = IsUsing ? jetRate : 0f;
                if (IsUsing)
                {
                    Vector3 target = aim.HasHit && aim.Distance <= Range ? aim.Point : aim.Ray.GetPoint(Range);
                    Vector3 direction = target - jetParticles.transform.position;
                    if (direction.sqrMagnitude > 0.0001f) jetParticles.transform.rotation = Quaternion.LookRotation(direction);
                    if (!jetParticles.isPlaying) jetParticles.Play();
                }
            }

            if (splashParticles != null)
            {
                bool splashing = IsUsing && aim.HasHit && aim.Distance <= Range;
                var emission = splashParticles.emission;
                emission.rateOverTimeMultiplier = splashing ? splashRate : 0f;
                if (splashing)
                {
                    // Eau trouble tant qu'elle emporte de la saleté détachée, claire sur une zone propre.
                    var main = splashParticles.main;
                    float muddy = TrySampleDirt(aim, out Color dirt, out RugCell cell) ? Mathf.Clamp01(cell.Loosened * 3f + cell.Stain) : 0f;
                    main.startColor = Color.Lerp(Color.white, dirt * 1.35f, muddy * 0.8f);
                    splashParticles.transform.SetPositionAndRotation(aim.Point + aim.Normal * 0.01f, Quaternion.LookRotation(aim.Normal));
                    if (!splashParticles.isPlaying) splashParticles.Play();
                }
            }
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            recoil = Mathf.MoveTowards(recoil, IsUsing ? 1f : 0f, deltaTime * 10f);
            float jitter = IsUsing ? (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 0.006f : 0f;
            model.localPosition = restPosition + new Vector3(jitter, jitter, -0.015f * recoil);
            model.localRotation = restRotation * Quaternion.Euler(-2f * recoil, 0f, 0f);
        }
    }
}
