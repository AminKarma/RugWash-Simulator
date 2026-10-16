using System;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Plan de travail de l'aire de lavage (seule zone de nettoyage de la salle). Ce qui ruisselle du tapis (eau sale du
    /// rinçage, mousse évacuée, débris balayés) se dépose autour du tapis, du côté où le joueur travaille ; la surface se
    /// nettoie ensuite avec les outils habituels (lance, balai, extracteur).
    /// <para>Convention : saleté = part de la surface souillée ; propreté = 1 − saleté (voir <see cref="WorkSurfaceStatus"/>).
    /// Sous <see cref="CleaningTuning.zoneMinCleanlinessToStart"/> de propreté, aucune nouvelle commande ne démarre.
    /// Au-delà de <see cref="CleaningTuning.zoneDirtyThreshold"/> de saleté, le tapis posé dessus se salit : bordure
    /// recontaminée à la pose, puis saleté ajoutée en continu tant que l'aire reste trop sale (sans dépasser la saleté
    /// d'origine de la bordure, et jamais sur un tapis terminé).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkSurface : MonoBehaviour
    {
        [Tooltip("Surface nettoyable couvrant l'aire de lavage.")]
        [SerializeField] RugCleaningController surface;
        [Tooltip("Tapis des commandes, posé sur l'aire de lavage.")]
        [SerializeField] RugCleaningController orderRug;
        [SerializeField, Min(0.1f)] float statusInterval = 0.4f;

        float pendingDirt, pendingFoam, pendingWater, pendingWeight;
        bool pendingDry;
        DirtType pendingType;
        Vector3 pendingPoint;
        float flushTimer, statusTimer;
        int depositCount;
        int contaminationBand;
        int lastVersion = -1;
        readonly RectInt[] bands = new RectInt[4];

        public RugCleaningController Surface => surface;
        public WorkSurfaceStatus Status { get; private set; }
        public int DepositCount => depositCount;
        /// <summary>Saleté ajoutée à la bordure du dernier tapis posé (0 si le plan de travail était propre).</summary>
        public float LastContamination { get; private set; }

        public event Action<WorkSurfaceStatus> StatusChanged;
        /// <summary>Un tapis vient d'être posé sur un plan de travail sale (quantité par cellule de bordure).</summary>
        public event Action<float> Contaminated;

        /// <summary>Saleté ajoutée en continu à la bordure du tapis depuis son chargement (aire trop sale).</summary>
        public float ContinuousContamination { get; private set; }

        float MinCleanlinessToStart => surface != null && surface.Tuning != null ? surface.Tuning.zoneMinCleanlinessToStart : 0.25f;
        float DirtyThreshold => surface != null && surface.Tuning != null ? surface.Tuning.zoneDirtyThreshold : 0.4f;

        /// <summary>
        /// Une nouvelle commande peut démarrer (propreté ≥ seuil). Sans grille (scène incomplète, état illisible), la réponse
        /// est oui : une donnée ancienne ou invalide ne bloque jamais le joueur.
        /// </summary>
        public bool AllowsNewOrder => surface == null || surface.Grid == null || WorkSurfaceRules.AllowsNewOrder(Status, MinCleanlinessToStart);
        /// <summary>Le tapis posé sur l'aire se salit (saleté au-delà du seuil).</summary>
        public bool ContaminatesRug => surface != null && surface.Grid != null && WorkSurfaceRules.ContaminatesRug(Status, DirtyThreshold);
        public int MinCleanlinessPercent => Mathf.RoundToInt(MinCleanlinessToStart * 100f);
        public int DirtyThresholdPercent => Mathf.RoundToInt(DirtyThreshold * 100f);

        public bool IsConfigured(out string missing)
        {
            missing = surface == null ? nameof(surface) : orderRug == null ? nameof(orderRug) : null;
            return missing == null;
        }

        void OnEnable()
        {
            if (orderRug == null) return;
            orderRug.Runoff += OnRunoff;
            orderRug.RugLoaded += OnRugLoaded;
        }

        void OnDisable()
        {
            if (orderRug == null) return;
            orderRug.Runoff -= OnRunoff;
            orderRug.RugLoaded -= OnRugLoaded;
        }

        void Start() => RefreshStatus(force: true);

        void OnRunoff(RugCleaningController rug, RunoffEvent runoff)
        {
            if (surface != null && surface.IsReplica) return;
            float weight = runoff.Dirt + runoff.Foam + runoff.Water;
            if (weight <= 0f) return;
            pendingPoint = pendingWeight <= 0f ? runoff.Point : Vector3.Lerp(pendingPoint, runoff.Point, weight / (pendingWeight + weight));
            pendingWeight += weight;
            pendingDirt += runoff.Dirt;
            pendingFoam += runoff.Foam;
            pendingWater += runoff.Water;
            pendingDry = runoff.DirtIsDry;
            if (runoff.Type != DirtType.None) pendingType = runoff.Type;
        }

        void Update()
        {
            if (surface == null || surface.Grid == null) return;
            var tuning = surface.Tuning;
            flushTimer += Time.deltaTime;
            if (pendingWeight > 0f && tuning != null && flushTimer >= tuning.runoffFlushInterval)
            {
                flushTimer = 0f;
                FlushRunoff();
            }

            statusTimer += Time.unscaledDeltaTime;
            if (statusTimer >= statusInterval)
            {
                float elapsed = statusTimer;
                statusTimer = 0f;
                RefreshStatus(force: false);
                ContaminateResidentRug(elapsed);
            }
        }

        /// <summary>
        /// Aire trop sale : la bordure du tapis posé dessus reprend de la boue, proportionnellement à l'excès de saleté.
        /// Hôte ou solo uniquement (les invités reçoivent les cellules). Rien sur un tapis terminé.
        /// </summary>
        public void ContaminateResidentRug(float seconds)
        {
            if (orderRug == null || orderRug.Grid == null || orderRug.IsReplica || orderRug.IsFinished || seconds <= 0f) return;
            var tuning = surface.Tuning;
            float perSecond = tuning != null ? tuning.zoneContaminationPerSecond : 0.015f;
            float rate = WorkSurfaceRules.ContaminationRate(Status, DirtyThreshold, perSecond);
            if (rate <= 0f) return;
            var grid = orderRug.Grid;
            WorkSurfaceRules.BorderBands(grid.Width, grid.Height, bands);
            float before = grid.TotalDirt;
            // Mission de corrections (2.10) : un bord à la fois, à tour de rôle, avec la quantité des quatre (même moyenne).
            // Les quatre bords d'un coup formaient un rectangle modifié couvrant tout le tapis : en ligne, le tapis entier
            // était renvoyé aux invités et réencodé toutes les 0,4 s (à-coups de 5 à 10 ms quand l'aire de lavage est sale,
            // par exemple après le rinçage d'un tapis couvert de mousse).
            var mud = new BorderContaminationOperation { Amount = rate * seconds * bands.Length, Cap = WorkSurfaceRules.MaxContamination };
            grid.ApplyRect(bands[contaminationBand], 1f, ref mud);
            contaminationBand = (contaminationBand + 1) % bands.Length;
            ContinuousContamination += Mathf.Max(0f, grid.TotalDirt - before);
        }

        /// <summary>Dépose immédiatement le ruissellement en attente (utilisé aussi par les tests).</summary>
        public void FlushRunoff()
        {
            if (pendingWeight <= 0f || surface == null || surface.Grid == null || orderRug == null) return;
            long timer = Diagnostics.PerfStats.Begin();
            var tuning = surface.Tuning;
            float scale = tuning != null ? tuning.runoffScale : 1f;
            float radius = tuning != null ? tuning.runoffRadius : 0.22f;
            float offset = tuning != null ? tuning.runoffEdgeOffset : 0.2f;

            Vector3 local = orderRug.transform.InverseTransformPoint(pendingPoint);
            Vector2 half = orderRug.SizeMeters * 0.5f;
            // Répartition le long du bord (séquence déterministe) : les flaques s'étalent naturellement.
            float along = (Mathf.Repeat(depositCount * 0.618034f, 1f) - 0.5f) * 0.5f;
            Vector2 edge = WorkSurfaceRules.EdgeDepositPoint(new Vector2(local.x, local.z), half, offset, along);
            Vector3 world = orderRug.transform.TransformPoint(new Vector3(edge.x, 0f, edge.y));

            var deposit = new DepositOperation
            {
                Type = pendingType,
                Mud = pendingDry ? 0f : WorkSurfaceRules.PerCellAmount(pendingDirt * scale, radius),
                Debris = pendingDry ? WorkSurfaceRules.PerCellAmount(pendingDirt * scale, radius) : 0f,
                Foam = WorkSurfaceRules.PerCellAmount(pendingFoam * scale, radius),
                Water = WorkSurfaceRules.PerCellAmount(pendingWater * scale, radius),
            };
            surface.ApplyOperation(world, radius, 2f, ref deposit);
            depositCount++;
            pendingDirt = pendingFoam = pendingWater = pendingWeight = 0f;
            pendingType = DirtType.None;
            Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.Runoff, timer);
        }

        public void RefreshStatus(bool force)
        {
            if (surface == null || surface.Grid == null) return;
            if (!force && surface.Grid.Version == lastVersion) return;
            lastVersion = surface.Grid.Version;
            Status = WorkSurfaceRules.Evaluate(surface.Grid);
            StatusChanged?.Invoke(Status);
        }

        void OnRugLoaded(RugCleaningController rug)
        {
            LastContamination = 0f;
            ContinuousContamination = 0f;
            if (surface == null || surface.Grid == null || rug.Grid == null) return;
            // Invité : la bordure souillée arrive avec les cellules de l'hôte.
            if (rug.IsReplica) return;
            RefreshStatus(force: true);
            float amount = WorkSurfaceRules.ContaminationAmount(Status, DirtyThreshold);
            if (amount <= 0f) return;

            var grid = rug.Grid;
            WorkSurfaceRules.BorderBands(grid.Width, grid.Height, bands);
            var mud = new DepositOperation { Type = DirtType.Soil, Mud = amount };
            for (int i = 0; i < bands.Length; i++) grid.ApplyRect(bands[i], 1f, ref mud);
            // La bordure souillée fait partie du travail à faire : la propreté est mesurée à partir de cet état.
            grid.CaptureInitialState();
            LastContamination = amount;
            Contaminated?.Invoke(amount);
        }
    }
}
