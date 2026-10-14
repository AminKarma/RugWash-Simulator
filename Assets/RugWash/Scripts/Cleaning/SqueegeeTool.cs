using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Raclette de sol (v0.1.2) : sur l'aire de lavage, la lame ramasse l'eau, la mousse et la boue détachée sous elle et les
    /// redépose juste devant, dans le sens réel du geste (les vraies données de la surface se déplacent, le rendu de l'aire
    /// les affiche) ; ce qui est poussé dans le caniveau disparaît. Sans déplacement, rien ne bouge ; contre la bordure, la
    /// lame bloque. Sur un tapis, elle est sans effet : les fibres ne se raclent pas.
    /// </summary>
    public sealed class SqueegeeTool : CleaningToolBase
    {
        [Header("Raclette")]
        [Tooltip("Part de l'eau, de la mousse et de la boue détachée ramassée sous la lame à chaque image de raclage.")]
        [SerializeField, Range(0.05f, 1f)] float efficiency = 0.6f;
        [Tooltip("Vitesse minimale du point visé (m/s) pour que la lame pousse : immobile, elle ne fait rien.")]
        [SerializeField, Min(0f)] float minDragSpeed = 0.12f;
        [Tooltip("Caniveau de l'aire de lavage : ce qui y est poussé est évacué.")]
        [SerializeField] Collider drain;
        [Tooltip("Marge horizontale autour du caniveau (m).")]
        [SerializeField, Min(0f)] float drainMargin = 0.06f;

        [Header("Effets")]
        [SerializeField] ParticleSystem waterPush;
        [SerializeField] ParticleSystem drainSplash;
        [SerializeField] float particlesPerUnit = 2500f;
        [SerializeField] float maxRate = 60f;

        /// <summary>Reports successifs d'une vague trop chargée (cellules saturées) avant la bordure.</summary>
        const int MaxWaves = 6;

        Vector3 lastPoint;
        RugCleaningController lastSurface;
        bool hasLast;
        Vector3 lastAhead;
        Vector3 lastDirection = Vector3.forward;
        float strokePhase;
        float strokeBlend;

        /// <summary>Quantité déplacée (eau + mousse + boue) par seconde à la dernière image.</summary>
        public float LastMovedPerSecond { get; private set; }
        /// <summary>Quantité évacuée au caniveau par seconde à la dernière image.</summary>
        public float LastDrainedPerSecond { get; private set; }
        /// <summary>Total évacué au caniveau depuis le début de la partie.</summary>
        public float TotalDrained { get; private set; }
        /// <summary>Vrai si la lame vise un tapis (sans effet).</summary>
        public bool AimingAtRug { get; private set; }
        public Collider Drain { get => drain; set => drain = value; }
        public override string ActionVerb => "racler";

        protected override float ContactIntensity => IsAffectingRug ? Mathf.Clamp01(0.35f + (LastMovedPerSecond + LastDrainedPerSecond) * 4f) : 0f;

        protected override void OnUseStarted()
        {
            base.OnUseStarted();
            hasLast = false;
        }

        protected override int ApplyToRug(RugCleaningController surface, in ToolAim aim, float deltaTime, float supply)
        {
            LastMovedPerSecond = LastDrainedPerSecond = 0f;
            AimingAtRug = surface.Kind == SurfaceKind.Rug;
            if (AimingAtRug || !surface.AcceptsDirt)
            {
                hasLast = false;
                return 0;
            }
            Vector3 point = aim.Point;
            if (!hasLast || lastSurface != surface)
            {
                lastPoint = point;
                lastSurface = surface;
                hasLast = true;
                return 0;
            }
            Vector3 motion = Vector3.ProjectOnPlane(point - lastPoint, aim.Normal);
            lastPoint = point;
            float distance = motion.magnitude;
            // Saut de visée (autre zone) : pas un geste de raclage.
            if (distance < minDragSpeed * deltaTime || distance > 0.5f) return 0;

            Vector3 direction = motion / distance;
            lastDirection = direction;
            Vector3 across = Vector3.Cross(aim.Normal, direction).normalized;
            float aspect = Mathf.Max(1f, definition.spreadAspect);
            float radius = Radius;
            // La lame (ellipse allongée en travers du geste) dépose ce qu'elle pousse juste devant elle.
            float halfDepth = radius / Mathf.Sqrt(aspect);
            Vector3 ahead = point + direction * (halfDepth * 1.6f + distance);
            lastAhead = ahead;
            bool overDrain = InDrain(point);
            bool toDrain = overDrain || InDrain(ahead);
            // Contre la bordure de l'aire : la lame bloque, rien ne se déplace.
            if (!toDrain && !surface.TryWorldToUV(ahead, out _)) return 0;

            var gather = new GatherOperation { Efficiency = efficiency * supply };
            surface.ApplyOperation(point, radius, definition.falloffHardness, aspect, across, default, ref gather);
            if (gather.WeightSum <= 0f) return 0;
            float total = gather.Water + gather.Foam + gather.Mud;
            if (toDrain)
            {
                TotalDrained += total;
                LastDrainedPerSecond = total / deltaTime;
                return 0;
            }
            DirtType type = surface.TrySampleCell(point, out RugCell cell) && cell.StainType != DirtType.None ? cell.StainType : DirtType.Soil;
            float water = gather.Water, foam = gather.Foam, mud = gather.Mud;
            Vector3 at = ahead;
            // Vague devant la lame : ce qui ne tient plus dans des cellules saturées est reporté un peu plus loin (jusqu'au
            // caniveau), au lieu d'être écrêté par la grille.
            for (int wave = 0; wave < MaxWaves && water + foam + mud > 1e-5f; wave++)
            {
                if (wave > 0)
                {
                    at += direction * halfDepth * 2f;
                    if (InDrain(at))
                    {
                        TotalDrained += water + foam + mud;
                        LastDrainedPerSecond = (water + foam + mud) / deltaTime;
                        break;
                    }
                    // Contre la bordure : le surplus déborde de l'aire.
                    if (!surface.TryWorldToUV(at, out _)) break;
                }
                var push = new DepositOperation
                {
                    Type = type, Water = water / gather.WeightSum, Foam = foam / gather.WeightSum, Mud = mud / gather.WeightSum,
                };
                surface.ApplyOperation(at, radius, definition.falloffHardness, aspect, across, default, ref push);
                water = push.WaterOverflow;
                foam = push.FoamOverflow;
                mud = push.MudOverflow;
            }
            LastMovedPerSecond = total / deltaTime;
            return 0;
        }

        bool InDrain(Vector3 point)
        {
            if (drain == null) return false;
            var bounds = drain.bounds;
            return point.x >= bounds.min.x - drainMargin && point.x <= bounds.max.x + drainMargin
                && point.z >= bounds.min.z - drainMargin && point.z <= bounds.max.z + drainMargin;
        }

        public override string GetStatusLine() =>
            AimingAtRug && IsUsing ? "Raclette sans effet sur un tapis" : "Poussez l'eau vers le caniveau";

        protected override void UpdateEffects(in ToolAim aim, float deltaTime)
        {
            if (!IsAffectingRug)
            {
                LastMovedPerSecond = LastDrainedPerSecond = 0f;
                StopEmission(waterPush);
                StopEmission(drainSplash);
                return;
            }
            var rotation = Quaternion.LookRotation(lastDirection + aim.Normal * 0.6f);
            float moved = Mathf.Min(maxRate, LastMovedPerSecond * particlesPerUnit);
            float drained = Mathf.Min(maxRate, LastDrainedPerSecond * particlesPerUnit);
            if (moved > 0.5f) SetEmission(waterPush, moved, lastAhead + aim.Normal * 0.01f, rotation);
            else StopEmission(waterPush);
            if (drained > 0.5f) SetEmission(drainSplash, drained, lastAhead + aim.Normal * 0.01f, Quaternion.LookRotation(aim.Normal));
            else StopEmission(drainSplash);
        }

        protected override void AnimateViewModel(Transform model, Vector3 restPosition, Quaternion restRotation, float deltaTime)
        {
            if (model == null) return;
            strokeBlend = Mathf.MoveTowards(strokeBlend, IsUsing ? 1f : 0f, deltaTime * 6f);
            strokePhase += deltaTime * 1.6f * Mathf.PI * 2f * strokeBlend;
            float push = Mathf.Sin(strokePhase) * strokeBlend;
            // Va-et-vient le long du manche seulement : sur un manche télescopique, la moindre rotation enfoncerait la lame
            // dans le sol (2 m de bras de levier).
            model.localPosition = restPosition + new Vector3(0f, 0f, 0.015f * strokeBlend + push * 0.012f);
            model.localRotation = restRotation;
        }
    }
}
