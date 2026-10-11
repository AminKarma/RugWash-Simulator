using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Brosse : détache la saleté fixée et la poussière (elles deviennent « saleté détachée », à rincer).
    /// Plus efficace sur fibres humides ou savonneuses selon le type de saleté.
    /// </summary>
    public sealed class BrushTool : CleaningToolBase
    {
        [Header("Effets")]
        [SerializeField] ParticleSystem debrisParticles;
        [Tooltip("Particules émises par unité de saleté détachée par seconde.")]
        [SerializeField] float debrisPerLoosened = 900f;
        [SerializeField] float maxDebrisRate = 90f;
        [SerializeField] float scrubFrequency = 11f;
        [SerializeField] float scrubAmplitude = 0.035f;

        float loosenedPerSecond;
        Color? debrisDefaultColor;
        float scrubPhase;
        float scrubBlend;

        public float LastLoosenedPerSecond => loosenedPerSecond;
        public override string ActionVerb => "brosser";

        protected override float ContactIntensity => IsAffectingRug ? Mathf.Clamp01(0.4f + loosenedPerSecond * 6f) : 0f;

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            var operation = new BrushOperation
            {
                Table = rug.DirtTable,
                Power = EffectivePower * supply,
                DeltaTime = deltaTime,
            };
            rug.ApplyOperation(aim.Point, definition.radius, definition.falloffHardness, ref operation);
            loosenedPerSecond = operation.Loosened / deltaTime;
            return 0;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug) loosenedPerSecond = 0f;
            if (debrisParticles == null) return;

            if (IsAffectingRug)
            {
                // Les débris prennent la couleur de la saleté réellement détachée à cet endroit.
                var main = debrisParticles.main;
                debrisDefaultColor ??= main.startColor.color;
                main.startColor = TrySampleDirt(aim, out Color dirt, out _) ? Color.Lerp(debrisDefaultColor.Value, dirt, 0.85f) : debrisDefaultColor.Value;
                SetEmission(debrisParticles, Mathf.Min(maxDebrisRate, loosenedPerSecond * debrisPerLoosened),
                    aim.Point + aim.Normal * 0.01f, Quaternion.LookRotation(aim.Normal));
            }
            else StopEmission(debrisParticles);
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            scrubBlend = Mathf.MoveTowards(scrubBlend, IsUsing ? 1f : 0f, deltaTime * 8f);
            scrubPhase += deltaTime * scrubFrequency * Mathf.PI * 2f * scrubBlend;
            float push = IsAffectingRug ? 0.12f : 0.05f;
            model.localPosition = restPosition + new Vector3(Mathf.Sin(scrubPhase) * scrubAmplitude, -0.02f * scrubBlend, push * scrubBlend);
            model.localRotation = restRotation * Quaternion.Euler(18f * scrubBlend, 0f, Mathf.Sin(scrubPhase) * 6f * scrubBlend);
        }
    }
}
