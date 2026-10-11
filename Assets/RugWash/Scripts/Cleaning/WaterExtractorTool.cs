using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Extracteur d'eau : aspire l'humidité des fibres (avec une partie de la saleté détachée et des
    /// résidus qu'elle contient) et la stocke dans la cuve de récupération, à vider au caniveau.
    /// Abstraction : l'eau extraite est calculée à partir de l'humidité retirée, convertie en litres
    /// (<see cref="litersPerSquareMeter"/> = eau contenue par m² de tapis saturé).
    /// </summary>
    public sealed class WaterExtractorTool : CleaningToolBase
    {
        [Tooltip("Litres d'eau contenus dans 1 m² de tapis saturé (humidité = 1).")]
        [SerializeField] float litersPerSquareMeter = 3f;

        [Header("Effets")]
        [SerializeField] ParticleSystem suctionDroplets;
        [SerializeField] Transform nozzle;
        [SerializeField] float dropletsPerLiter = 900f;
        [SerializeField] float maxDropletRate = 140f;

        float litersPerSecond;
        float hum;

        public float LastLitersPerSecond => litersPerSecond;
        public override string ActionVerb => "extraire l'eau";

        public override bool IsConfigured(out string missing)
        {
            if (!base.IsConfigured(out missing)) return false;
            if (definition.resource != ToolResource.Recovery) missing = "definition.resource (doit être Recovery)";
            return missing == null;
        }

        protected override float ContactIntensity => IsAffectingRug ? Mathf.Clamp01(0.3f + litersPerSecond) : 0f;

        /// <summary>La cuve pleine bloque l'extraction.</summary>
        protected override bool CanOperate() => Resources != null && !Resources.Recovery.IsFull;

        protected override float ConsumeResources(float deltaTime) => Resources.Recovery.IsFull ? 0f : 1f;

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            var operation = new ExtractOperation { Power = EffectivePower * supply, DeltaTime = deltaTime };
            ApplyArea(rug, aim, ref operation);
            float liters = operation.MoistureRemoved * rug.Grid.CellAreaM2 * litersPerSquareMeter;
            Resources.Recovery.Add(liters);
            litersPerSecond = liters / deltaTime;
            return operation.CellsCleaned;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug || litersPerSecond <= 0.0001f)
            {
                if (!IsAffectingRug) litersPerSecond = 0f;
                StopEmission(suctionDroplets);
                return;
            }
            Vector3 origin = aim.Point + aim.Normal * 0.01f;
            Vector3 towardNozzle = nozzle != null ? nozzle.position - origin : aim.Normal;
            SetEmission(suctionDroplets, Mathf.Min(maxDropletRate, litersPerSecond * dropletsPerLiter), origin,
                Quaternion.LookRotation(towardNozzle.sqrMagnitude > 0.0001f ? towardNozzle : aim.Normal));
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            hum = Mathf.MoveTowards(hum, IsUsing ? 1f : 0f, deltaTime * 6f);
            float pull = IsAffectingRug ? Mathf.Sin(Time.time * 3f) * 0.04f : 0f;
            model.localPosition = restPosition + new Vector3(0f, hum * 0.002f * Mathf.Sin(Time.time * 70f), 0.05f * hum + pull);
            model.localRotation = restRotation * Quaternion.Euler(12f * hum, 0f, 0f);
        }

        public override string GetBlockReason() =>
            Resources != null && Resources.Recovery.IsFull ? "Cuve de récupération pleine — videz-la au caniveau (E)" : null;
    }
}
