using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Balai : large et sans consommable. Sur un tapis sec, il chasse les poils et les débris (et un peu de poussière)
    /// hors du tapis, sur le plan de travail : à passer avant le produit. L'humidité colle les débris aux fibres et les
    /// taches fixées ne bougent pas. Sur une surface dure (plan de travail, sol), il pousse aussi l'eau sale, la boue
    /// et la mousse vers l'évacuation : c'est l'outil de remise au propre de l'aire de lavage.
    /// </summary>
    public sealed class BroomTool : CleaningToolBase
    {
        [Header("Effets")]
        [SerializeField] ParticleSystem dustPuffs;
        [SerializeField] ParticleSystem waterPush;
        [SerializeField] float dustPerUnit = 1800f;
        [SerializeField] float waterPerUnit = 1500f;
        [SerializeField] float maxRate = 80f;
        [SerializeField] float sweepFrequency = 2.4f;
        [SerializeField] float sweepAngle = 16f;

        static readonly Color DustColor = new Color(0.66f, 0.62f, 0.55f);

        float removedPerSecond;
        float liquidPerSecond;
        float sweepPhase;
        float sweepBlend;

        public float LastRemovedPerSecond => removedPerSecond;
        public float LastLiquidPerSecond => liquidPerSecond;
        public override string ActionVerb => "balayer";

        protected override float ContactIntensity => IsAffectingRug ? Mathf.Clamp01(0.45f + (removedPerSecond + liquidPerSecond) * 8f) : 0f;

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            var operation = new SweepOperation
            {
                Table = rug.DirtTable,
                Power = EffectivePower * supply,
                DeltaTime = deltaTime,
                HardFloor = rug.Kind != SurfaceKind.Rug,
            };
            ApplyArea(rug, aim, ref operation);
            removedPerSecond = operation.Removed / deltaTime;
            liquidPerSecond = operation.LiquidRemoved / deltaTime;
            return operation.CellsCleaned;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug)
            {
                removedPerSecond = liquidPerSecond = 0f;
                StopEmission(dustPuffs);
                StopEmission(waterPush);
                return;
            }
            var rotation = Quaternion.LookRotation(aim.Normal);
            Vector3 origin = aim.Point + aim.Normal * 0.01f;
            float dust = Mathf.Min(maxRate, removedPerSecond * dustPerUnit);
            float water = Mathf.Min(maxRate, liquidPerSecond * waterPerUnit);
            if (dustPuffs != null && TrySampleDirt(aim, out Color dirt, out _))
            {
                var main = dustPuffs.main;
                main.startColor = Color.Lerp(DustColor, dirt, 0.6f);
            }
            if (dust > 0.5f) SetEmission(dustPuffs, dust, origin, rotation);
            else StopEmission(dustPuffs);
            if (water > 0.5f) SetEmission(waterPush, water, origin, rotation);
            else StopEmission(waterPush);
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            sweepBlend = Mathf.MoveTowards(sweepBlend, IsUsing ? 1f : 0f, deltaTime * 6f);
            sweepPhase += deltaTime * sweepFrequency * Mathf.PI * 2f * sweepBlend;
            float swing = Mathf.Sin(sweepPhase) * sweepBlend;
            model.localPosition = restPosition + new Vector3(swing * 0.06f, -0.015f * sweepBlend, 0.05f * sweepBlend);
            model.localRotation = restRotation * Quaternion.Euler(6f * sweepBlend, swing * sweepAngle, swing * -4f);
        }
    }
}
