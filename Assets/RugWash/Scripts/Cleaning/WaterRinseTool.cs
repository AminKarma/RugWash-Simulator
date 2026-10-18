using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Outil de rinçage : consomme de l'eau en continu pendant l'utilisation (même hors du tapis), évacue la saleté
    /// détachée et la mousse, et mouille les fibres. Deux outils l'utilisent : le seau d'eau du kit de départ (versé de
    /// près, moins puissant) et la lance de rinçage haute pression (à acheter).
    /// </summary>
    public sealed class WaterRinseTool : CleaningToolBase
    {
        [Header("Effets")]
        [SerializeField] ParticleSystem jetParticles;
        [SerializeField] ParticleSystem splashParticles;
        [SerializeField] float jetRate = 260f;
        [SerializeField] float splashRate = 140f;
        [Tooltip("Seau : le modèle s'incline pour verser au lieu de reculer comme une lance.")]
        [SerializeField] bool pourMotion;
        [Header("Lance au niveau maximal (dorée, V0.2.5)")]
        [Tooltip("Jet tendu : vitesse des gouttes (m/s), sans retombée ni dispersion.")]
        [SerializeField, Range(10f, 40f)] float maxLevelJetSpeed = 24f;
        [Tooltip("Cœur du jet (poids de la zone traitée) où la mousse et les résidus partent d'un coup.")]
        [SerializeField, Range(0.05f, 1f)] float flushSoapAbove = 0.2f;
        [Tooltip("Débit de gouttes du jet tendu (× débit normal).")]
        [SerializeField, Range(1f, 3f)] float maxLevelRateBoost = 1.4f;

        float removedPerSecond;
        float recoil;
        ParticleSystemRenderer jetRenderer;
        ParticleSystem.MinMaxCurve baseJetSpeed;
        float baseJetGravity, baseJetAngle;
        bool jetCaptured, jetMaxLevel;

        /// <summary>
        /// Lance au niveau maximal (outil doré) : jet tendu dans l'axe de la lance, sans retombée, qui chasse la mousse et les
        /// résidus d'un seul passage. Le seau n'est jamais concerné.
        /// </summary>
        public bool IsMaxLevelJet => !pourMotion && IsGolden;
        public float FlushSoapAbove => flushSoapAbove;

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
                FlushSoapAbove = IsMaxLevelJet ? flushSoapAbove : 0f,
            };
            ApplyArea(rug, aim, ref operation);
            removedPerSecond = operation.Removed / deltaTime;
            return operation.CellsCleaned;
        }

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug) removedPerSecond = 0f;

            if (jetParticles != null)
            {
                ConfigureJet();
                var emission = jetParticles.emission;
                emission.rateOverTimeMultiplier = IsUsing ? EffectBudget.Rate(jetRate * (jetMaxLevel ? maxLevelRateBoost : 1f)) : 0f;
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
                emission.rateOverTimeMultiplier = splashing ? EffectBudget.Rate(splashRate) : 0f;
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

        /// <summary>
        /// Jet selon le niveau : réglages d'origine, ou jet tendu au niveau maximal (vitesse élevée, aucune gravité, cône
        /// fermé). Dans les deux cas, l'émission est avancée de la traînée des gouttes : le jet part de la buse.
        /// </summary>
        void ConfigureJet()
        {
            if (!jetCaptured)
            {
                jetCaptured = true;
                jetRenderer = jetParticles.GetComponent<ParticleSystemRenderer>();
                baseJetSpeed = jetParticles.main.startSpeed;
                baseJetGravity = jetParticles.main.gravityModifierMultiplier;
                baseJetAngle = jetParticles.shape.angle;
                jetMaxLevel = !IsMaxLevelJet;
            }
            if (jetMaxLevel == IsMaxLevelJet) return;
            jetMaxLevel = IsMaxLevelJet;
            var main = jetParticles.main;
            var shape = jetParticles.shape;
            if (jetMaxLevel)
            {
                main.startSpeed = new ParticleSystem.MinMaxCurve(maxLevelJetSpeed * 0.95f, maxLevelJetSpeed * 1.05f);
                main.gravityModifierMultiplier = 0f;
                shape.angle = Mathf.Min(baseJetAngle, 0.5f);
            }
            else
            {
                main.startSpeed = baseJetSpeed;
                main.gravityModifierMultiplier = baseJetGravity;
                shape.angle = baseJetAngle;
            }
            StretchedJet.AlignToNozzle(jetParticles, jetRenderer, StretchedJet.SlowestStartSpeed(jetParticles));
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            if (pourMotion)
            {
                recoil = Mathf.MoveTowards(recoil, IsUsing ? 1f : 0f, deltaTime * 4f);
                float slosh = IsUsing ? Mathf.Sin(Time.time * 9f) * 0.004f : 0f;
                model.localPosition = restPosition + new Vector3(0f, 0.03f * recoil + slosh, 0.02f * recoil);
                model.localRotation = restRotation * Quaternion.Euler(38f * recoil, 0f, 0f);
                return;
            }
            recoil = Mathf.MoveTowards(recoil, IsUsing ? 1f : 0f, deltaTime * 10f);
            float jitter = IsUsing ? (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 0.006f : 0f;
            model.localPosition = restPosition + new Vector3(jitter, jitter, -0.015f * recoil);
            model.localRotation = restRotation * Quaternion.Euler(-2f * recoil, 0f, 0f);
        }
    }
}
