using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Canon à mousse : projette en éventail une mousse épaisse et diluée (moins de résidus que le détergent pur),
    /// sur une grande zone et de plus loin. Consomme le détergent du réservoir. Laissée en place, la mousse détache
    /// peu à peu les taches qui réagissent au savon (graisse, boissons, boue) ; le brossage la fait monter et la
    /// rend encore plus efficace ; le rinçage et l'extraction l'évacuent.
    /// </summary>
    public sealed class FoamCannonTool : CleaningToolBase
    {
        [Header("Mousse")]
        [Tooltip("Résidus de produit déposés par unité de mousse (le détergent pur en dépose 0,8).")]
        [SerializeField, Range(0.05f, 1f)] float residuePerFoam = 0.35f;

        [Header("Effets")]
        [SerializeField] ParticleSystem foamStream;
        [SerializeField] ParticleSystem impactPuffs;
        [SerializeField] float streamRate = 70f;
        [SerializeField] float impactRate = 60f;

        float pump;

        public float LastFoamPerSecond { get; private set; }
        public override string ActionVerb => "projeter la mousse";

        public override bool IsConfigured(out string missing)
        {
            if (!base.IsConfigured(out missing)) return false;
            if (definition.resource != ToolResource.Soap) missing = "definition.resource (doit être Soap)";
            return missing == null;
        }

        protected override float ContactIntensity => IsAffectingRug ? 1f : 0.5f;

        public override string GetStatusLine() => ProductStatusLine();

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            byte product = ConsumeActiveProduct(rug, deltaTime, out float residueFactor);
            var operation = new SoapOperation
            {
                FoamPerSecond = EffectivePower * supply,
                WettingPerSecond = definition.wettingPerSecond * supply,
                DeltaTime = deltaTime,
                ResiduePerFoam = residuePerFoam * residueFactor,
                Product = product,
            };
            ApplyArea(rug, aim, ref operation);
            LastFoamPerSecond = operation.FoamAdded / deltaTime;
            return 0;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug) LastFoamPerSecond = 0f;
            if (foamStream != null)
            {
                var emission = foamStream.emission;
                emission.rateOverTimeMultiplier = IsUsing ? streamRate : 0f;
                if (IsUsing)
                {
                    Vector3 target = aim.HasHit && aim.Distance <= Range ? aim.Point : aim.Ray.GetPoint(Range);
                    Vector3 direction = target - foamStream.transform.position;
                    if (direction.sqrMagnitude > 0.0001f) foamStream.transform.rotation = Quaternion.LookRotation(direction);
                    if (!foamStream.isPlaying) foamStream.Play();
                }
            }

            if (IsAffectingRug) SetEmission(impactPuffs, impactRate, aim.Point + aim.Normal * 0.015f, Quaternion.LookRotation(aim.Normal));
            else StopEmission(impactPuffs);
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            pump = Mathf.MoveTowards(pump, IsUsing ? 1f : 0f, deltaTime * 8f);
            float pulse = IsUsing ? Mathf.Sin(Time.time * 18f) * 0.0025f : 0f;
            model.localPosition = restPosition + new Vector3(0f, pulse, -0.018f * pump);
            model.localRotation = restRotation * Quaternion.Euler(-2.5f * pump, 0f, 0f);
        }
    }
}
