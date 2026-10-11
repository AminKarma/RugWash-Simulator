using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Aspirateur : retire poussière et débris secs (et la saleté détachée sur fibres sèches).
    /// Sans effet sur les taches incrustées. Électrique : aucun consommable.
    /// </summary>
    public sealed class VacuumTool : CleaningToolBase
    {
        [Header("Effets")]
        [Tooltip("Poussière aspirée, émise au point visé et dirigée vers la buse.")]
        [SerializeField] ParticleSystem suctionParticles;
        [SerializeField] Transform nozzle;
        [SerializeField] float particlesPerRemoved = 1600f;
        [SerializeField] float maxParticleRate = 120f;

        float removedPerSecond;
        float hum;

        public float LastRemovedPerSecond => removedPerSecond;
        public override string ActionVerb => "aspirer";

        protected override float ContactIntensity => IsAffectingRug ? Mathf.Clamp01(0.5f + removedPerSecond * 4f) : 0f;

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            var operation = new VacuumOperation { Table = rug.DirtTable, Power = EffectivePower * supply, DeltaTime = deltaTime };
            rug.ApplyOperation(aim.Point, definition.radius, definition.falloffHardness, ref operation);
            removedPerSecond = operation.Removed / deltaTime;
            return operation.CellsCleaned;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug)
            {
                removedPerSecond = 0f;
                StopEmission(suctionParticles);
                return;
            }
            Vector3 origin = aim.Point + aim.Normal * 0.01f;
            Vector3 towardNozzle = nozzle != null ? (nozzle.position - origin) : aim.Normal;
            SetEmission(suctionParticles, Mathf.Min(maxParticleRate, removedPerSecond * particlesPerRemoved), origin,
                Quaternion.LookRotation(towardNozzle.sqrMagnitude > 0.0001f ? towardNozzle : aim.Normal));
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            hum = Mathf.MoveTowards(hum, IsUsing ? 1f : 0f, deltaTime * 6f);
            float vibration = hum * 0.0025f * Mathf.Sin(Time.time * 90f);
            float sweep = IsAffectingRug ? Mathf.Sin(Time.time * 4f) * 0.03f : 0f;
            model.localPosition = restPosition + new Vector3(sweep, vibration, 0.04f * hum);
            model.localRotation = restRotation * Quaternion.Euler(10f * hum, 0f, 0f);
        }
    }
}
