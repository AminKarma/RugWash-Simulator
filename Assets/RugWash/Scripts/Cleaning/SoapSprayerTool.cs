using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Pulvérisateur de détergent : consomme du produit, dépose de la mousse et des résidus.
    /// Ne nettoie pas seul : il rend le brossage efficace sur graisse, boissons et taches incrustées.
    /// </summary>
    public sealed class SoapSprayerTool : CleaningToolBase
    {
        [Header("Effets")]
        [SerializeField] ParticleSystem sprayParticles;
        [SerializeField] ParticleSystem foamPuffs;
        [SerializeField] float sprayRate = 180f;
        [SerializeField] float foamPuffRate = 40f;

        float pump;

        public override string ActionVerb => "savonner";

        public override bool IsConfigured(out string missing)
        {
            if (!base.IsConfigured(out missing)) return false;
            if (definition.resource != ToolResource.Soap) missing = "definition.resource (doit être Soap)";
            return missing == null;
        }

        protected override float ContactIntensity => IsAffectingRug ? 1f : 0.4f;

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            var operation = new SoapOperation
            {
                FoamPerSecond = EffectivePower * supply,
                WettingPerSecond = definition.wettingPerSecond * supply,
                DeltaTime = deltaTime,
            };
            rug.ApplyOperation(aim.Point, definition.radius, definition.falloffHardness, ref operation);
            return 0;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (sprayParticles != null)
            {
                var emission = sprayParticles.emission;
                emission.rateOverTimeMultiplier = IsUsing ? sprayRate : 0f;
                if (IsUsing)
                {
                    Vector3 target = aim.HasHit && aim.Distance <= Range ? aim.Point : aim.Ray.GetPoint(Range);
                    Vector3 direction = target - sprayParticles.transform.position;
                    if (direction.sqrMagnitude > 0.0001f) sprayParticles.transform.rotation = Quaternion.LookRotation(direction);
                    if (!sprayParticles.isPlaying) sprayParticles.Play();
                }
            }

            if (IsAffectingRug) SetEmission(foamPuffs, foamPuffRate, aim.Point + aim.Normal * 0.01f, Quaternion.LookRotation(aim.Normal));
            else StopEmission(foamPuffs);
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            pump = Mathf.MoveTowards(pump, IsUsing ? 1f : 0f, deltaTime * 12f);
            model.localPosition = restPosition + new Vector3(0f, 0f, -0.012f * pump);
            model.localRotation = restRotation * Quaternion.Euler(-3f * pump, 0f, 0f);
        }
    }
}
