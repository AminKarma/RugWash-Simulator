using System;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Plan de travail de l'aire de lavage. Ce qui ruisselle du tapis (eau sale du rinçage, mousse évacuée, débris balayés)
    /// se dépose autour du tapis, du côté où le joueur travaille ; la surface se nettoie ensuite avec les outils habituels
    /// (lance, balai, extracteur). Un plan de travail resté sale ne bloque jamais une commande : il recontamine la bordure
    /// du tapis suivant, ce qui est annoncé au comptoir et dans le HUD.
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
        int lastVersion = -1;

        public RugCleaningController Surface => surface;
        public WorkSurfaceStatus Status { get; private set; }
        public int DepositCount => depositCount;
        /// <summary>Saleté ajoutée à la bordure du dernier tapis posé (0 si le plan de travail était propre).</summary>
        public float LastContamination { get; private set; }

        public event Action<WorkSurfaceStatus> StatusChanged;
        /// <summary>Un tapis vient d'être posé sur un plan de travail sale (quantité par cellule de bordure).</summary>
        public event Action<float> Contaminated;

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
                statusTimer = 0f;
                RefreshStatus(force: false);
            }
        }

        /// <summary>Dépose immédiatement le ruissellement en attente (utilisé aussi par les tests).</summary>
        public void FlushRunoff()
        {
            if (pendingWeight <= 0f || surface == null || surface.Grid == null || orderRug == null) return;
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
            if (surface == null || surface.Grid == null || rug.Grid == null) return;
            RefreshStatus(force: true);
            float amount = WorkSurfaceRules.ContaminationAmount(Status);
            if (amount <= 0f) return;

            var grid = rug.Grid;
            int band = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(grid.Width, grid.Height) * WorkSurfaceRules.ContaminationBand));
            var mud = new DepositOperation { Type = DirtType.Soil, Mud = amount };
            grid.ApplyRect(new RectInt(0, 0, grid.Width, band), 1f, ref mud);
            grid.ApplyRect(new RectInt(0, grid.Height - band, grid.Width, band), 1f, ref mud);
            grid.ApplyRect(new RectInt(0, band, band, grid.Height - 2 * band), 1f, ref mud);
            grid.ApplyRect(new RectInt(grid.Width - band, band, band, grid.Height - 2 * band), 1f, ref mud);
            // La bordure souillée fait partie du travail à faire : la propreté est mesurée à partir de cet état.
            grid.CaptureInitialState();
            LastContamination = amount;
            Contaminated?.Invoke(amount);
        }
    }
}
