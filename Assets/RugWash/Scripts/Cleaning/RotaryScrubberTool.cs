using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Monobrosse professionnelle (v0.1.2) : un disque rotatif qui frotte une large zone. Effets définis :
    /// <list type="bullet">
    /// <item>nettoyage : détache la saleté (taches, poussière, poils) comme la brosse, en plus fort et plus large ; elle
    /// ne retire rien — il faut toujours rincer et extraire ensuite ;</item>
    /// <item>efficacité liée à l'état des fibres : faible à sec, pleine sur fibres humides ou savonneuses ;</item>
    /// <item>mousse : fait monter fortement la mousse d'une zone savonneuse ; humidité : n'en ajoute ni n'en retire ;</item>
    /// <item>usure : à sec, elle use les fibres d'un tapis fragile deux fois plus vite que la brosse.</item>
    /// </list>
    /// Machine lourde : le joueur marche moins vite pendant qu'elle tourne. Sans consommable.
    /// </summary>
    public sealed class RotaryScrubberTool : CleaningToolBase
    {
        [Header("Monobrosse")]
        [Tooltip("Efficacité sur fibres sèches (1 = pleine efficacité, atteinte sur fibres humides ou savonneuses).")]
        [SerializeField, Range(0.05f, 1f)] float dryEfficiency = 0.35f;
        [Tooltip("Mousse montée par seconde sur une zone savonneuse (la brosse : 0,5).")]
        [SerializeField, Range(0f, 4f)] float latherPerSecond = 1.6f;
        [Tooltip("Usure par seconde à sec sur un tapis de fragilité 1 (la brosse : 0,06).")]
        [SerializeField, Range(0f, 0.5f)] float abrasiveness = 0.12f;
        [Tooltip("Vitesse de marche pendant l'utilisation (machine lourde).")]
        [SerializeField, Range(0.2f, 1f)] float moveSpeedWhileRunning = 0.55f;
        [Tooltip("Disque rotatif du modèle.")]
        [SerializeField] Transform spinner;
        [SerializeField, Min(0f)] float spinSpeed = 900f;

        [Header("Effets")]
        [SerializeField] ParticleSystem spray;
        [SerializeField] float sprayPerLoosened = 700f;
        [SerializeField] float maxRate = 70f;

        float loosenedPerSecond;
        float spin;
        float wobblePhase;
        Color? sprayDefaultColor;

        public float LastLoosenedPerSecond => loosenedPerSecond;
        /// <summary>Efficacité appliquée à la dernière image selon l'état des fibres (0..1).</summary>
        public float LastEfficiency { get; private set; }
        public float SpinSpeed => spin;
        public override string ActionVerb => "frotter";
        public override float MoveSpeedFactor => IsUsing ? moveSpeedWhileRunning : 1f;

        protected override float ContactIntensity => IsAffectingRug ? Mathf.Clamp01(0.5f + loosenedPerSecond * 5f) : 0f;

        /// <summary>Efficacité selon les fibres : sèches → <paramref name="dry"/>, humides ou savonneuses → 1.</summary>
        public static float Efficiency(in RugCell cell, float dry) =>
            Mathf.Lerp(dry, 1f, Mathf.Clamp01(cell.Moisture * 2.5f + cell.Foam * 3f));

        protected override int ApplyToRug(RugCleaningController rug, in ToolAim aim, float deltaTime, float supply)
        {
            LastEfficiency = rug.TrySampleCell(aim.Point, out RugCell cell) ? Efficiency(cell, dryEfficiency) : dryEfficiency;
            var operation = new BrushOperation
            {
                Table = rug.DirtTable,
                Power = EffectivePower * supply * LastEfficiency,
                DeltaTime = deltaTime,
                LatherPerSecond = latherPerSecond,
                Products = rug.Products,
                FiberWearEnabled = rug.Fragility > 0f,
                WearPerSecond = abrasiveness * rug.Fragility,
            };
            ApplyArea(rug, aim, ref operation);
            loosenedPerSecond = operation.Loosened / deltaTime;
            return 0;
        }

        public override string GetStatusLine() =>
            IsAffectingRug && LastEfficiency < 0.6f ? "Fibres sèches : peu efficace" : "Sur fibres humides, puis rincer";

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug) loosenedPerSecond = 0f;
            if (spray == null) return;
            if (IsAffectingRug)
            {
                var main = spray.main;
                sprayDefaultColor ??= main.startColor.color;
                main.startColor = TrySampleDirt(aim, out Color dirt, out _) ? Color.Lerp(sprayDefaultColor.Value, dirt, 0.6f) : sprayDefaultColor.Value;
                SetEmission(spray, Mathf.Min(maxRate, loosenedPerSecond * sprayPerLoosened), aim.Point + aim.Normal * 0.02f,
                    Quaternion.LookRotation(aim.Normal));
            }
            else StopEmission(spray);
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            spin = Mathf.MoveTowards(spin, IsUsing ? spinSpeed : 0f, deltaTime * spinSpeed * 1.5f);
            if (spinner != null) spinner.Rotate(0f, spin * deltaTime, 0f, Space.Self);
            if (model == null) return;
            // Le moteur fait vibrer la machine ; au contact, elle « tire » légèrement de côté.
            wobblePhase += deltaTime * 37f;
            float vibration = spin / Mathf.Max(1f, spinSpeed);
            float drift = IsAffectingRug ? Mathf.Sin(wobblePhase * 0.08f) * 0.015f : 0f;
            model.localPosition = restPosition + new Vector3(drift + Mathf.Sin(wobblePhase) * 0.002f * vibration, Mathf.Cos(wobblePhase * 1.3f) * 0.002f * vibration, 0f);
            model.localRotation = restRotation;
        }
    }
}
