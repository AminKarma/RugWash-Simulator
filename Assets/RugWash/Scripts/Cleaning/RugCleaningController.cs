using System;
using System.Collections.Generic;
using RugWash.Core;
using RugWash.Effects;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Ce qui ruisselle hors de la surface lors d'une opération : parts de la saleté et de la mousse retirées
    /// (réglages de l'outil) et eau ruisselante en « unités × m² » (calculée par l'outil à partir de l'eau projetée).
    /// </summary>
    public readonly struct RunoffProfile
    {
        public readonly float Dirt;
        public readonly float Foam;
        public readonly float Water;
        public readonly bool DirtIsDry;

        public RunoffProfile(float dirt, float foam, float water, bool dirtIsDry)
        {
            Dirt = dirt;
            Foam = foam;
            Water = water;
            DirtIsDry = dirtIsDry;
        }

        public bool IsNone => Dirt <= 0f && Foam <= 0f && Water <= 0f;
    }

    /// <summary>
    /// Ruissellement produit par une opération : quantités en « unités × m² » (quantité par cellule multipliée par la
    /// surface de la cellule), donc indépendantes de la résolution de la grille.
    /// </summary>
    public struct RunoffEvent
    {
        public Vector3 Point;
        public float Radius;
        public float Dirt;
        public float Foam;
        public float Water;
        public bool DirtIsDry;
        public DirtType Type;
    }

    /// <summary>
    /// Surface nettoyable présente dans la scène (tapis, plan de travail, sol). Possède sa grille (aucun état global),
    /// convertit les impacts monde → UV, fait évoluer lentement la surface (mousse qui travaille puis retombe, séchage),
    /// signale le ruissellement des opérations et délègue le visuel à <see cref="RugDirtRenderer"/>.
    /// Plusieurs surfaces coexistent : les outils agissent sur celle qui est visée.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugCleaningController : MonoBehaviour
    {
        [SerializeField] RugDefinition definition;
        [SerializeField] DirtTypeLibrary dirtLibrary;
        [SerializeField] RugDirtRenderer dirtRenderer;
        [SerializeField] MeshFilter meshFilter;
        [SerializeField] BoxCollider surfaceCollider;
        [Tooltip("Réglages de la mousse et du séchage (valeurs par défaut si absent).")]
        [SerializeField] CleaningTuning tuning;
        [Tooltip("Produits spécialisés reconnus dans la mousse (optionnel).")]
        [SerializeField] ProductLibrary productLibrary;
        [Tooltip("Charge la définition au démarrage (aire de lavage, sol). Faux pour le tapis des commandes : " +
                 "il n'apparaît qu'avec une commande (aucun tapis d'entraînement).")]
        [SerializeField] bool loadOnAwake = true;

        Mesh generatedMesh;
        readonly SurfaceAgingScheduler aging = new SurfaceAgingScheduler();

        public RugCleaningGrid Grid { get; private set; }
        /// <summary>Définition du tapis chargé ; null si aucun tapis n'est posé.</summary>
        public RugDefinition Definition => Grid != null ? definition : null;
        /// <summary>Un tapis (ou une surface) est chargé.</summary>
        public bool IsLoaded => Grid != null;
        /// <summary>
        /// Vrai : surface de la zone de nettoyage (tapis, aire de lavage). Faux : surface décorative (sol de la salle) qui
        /// n'accumule rien et n'est ni visée par les outils ni marquée par les pas.
        /// </summary>
        public bool AcceptsDirt => definition == null || definition.accumulatesDirt;
        /// <summary>
        /// Tapis terminé : il restait moins de <see cref="CleaningTuning.rugFinishedRemaining"/> de la saleté initiale ; le
        /// reliquat a été retiré (propreté 100 %). Remis à zéro à chaque chargement.
        /// </summary>
        public bool IsFinished { get; private set; }
        public DirtTypeTable DirtTable { get; private set; }
        /// <summary>Effets des produits spécialisés (toujours présent : au minimum le détergent standard).</summary>
        public ProductTable Products { get; private set; }
        public float Fragility => definition != null ? definition.fragility : 0f;
        public RugDirtRenderer DirtRenderer => dirtRenderer;
        public float Cleanliness01 => Grid != null ? Grid.Cleanliness01 : 0f;
        public SurfaceKind Kind => definition != null ? definition.surfaceKind : SurfaceKind.Rug;
        public CleaningTuning Tuning => tuning;
        /// <summary>Taille de la surface en mètres (X, Z locaux) ; zéro si aucun tapis n'est posé.</summary>
        public Vector2 SizeMeters => Definition != null ? definition.sizeMeters : Vector2.zero;

        /// <summary>Déclenché après chaque chargement ou réinitialisation de tapis.</summary>
        public event Action<RugCleaningController> RugLoaded;

        /// <summary>Le tapis a été retiré (commande livrée ou abandonnée) : plus aucun tapis n'est posé.</summary>
        public event Action<RugCleaningController> RugUnloaded;

        /// <summary>Le tapis vient d'être terminé (moins de 1 % de saleté restante, reliquat retiré).</summary>
        public event Action<RugCleaningController> Finished;

        /// <summary>Ce qu'une opération a fait ruisseler hors de la surface (eau sale, mousse, débris).</summary>
        public event Action<RugCleaningController, RunoffEvent> Runoff;

        /// <summary>Saleté fixée détachée par la mousse au repos depuis le chargement.</summary>
        public float FoamDwellLoosened { get; private set; }

        /// <summary>
        /// Multijoueur, côté invité : la surface est une réplique de celle de l'hôte. Les opérations sont transmises à
        /// l'hôte et prédites localement ; la simulation lente (mousse, séchage) et le ruissellement restent à l'hôte.
        /// Null (solo, hôte) : la surface fait autorité.
        /// </summary>
        public ISurfaceReplication Replication { get; set; }
        public bool IsReplica => Replication != null;

        public bool IsConfigured(out string missing)
        {
            missing = definition == null ? nameof(definition)
                : dirtLibrary == null ? nameof(dirtLibrary)
                : dirtRenderer == null ? nameof(dirtRenderer)
                : meshFilter == null ? nameof(meshFilter)
                : surfaceCollider == null ? nameof(surfaceCollider) : null;
            return missing == null;
        }

        void Awake()
        {
            if (tuning == null) tuning = CleaningTuning.CreateDefault();
            if (Grid == null && definition != null && loadOnAwake) LoadRug(definition);
            else if (Grid == null) Hide();
        }

        void Update()
        {
            if (Grid == null || Time.deltaTime <= 0f) return;
            // Invité : le reliquat est retiré par l'hôte ; la fin est constatée à l'arrivée des cellules.
            if (IsReplica && !IsFinished && Kind == SurfaceKind.Rug && Grid.InitialDirt > 1e-5f && Grid.TotalDirt <= 1e-4f) MarkFinished();

            // Simulation lente répartie sur les images. Elle est reproduite par chaque poste (hôte comme invité) : l'hôte
            // n'en diffuse le résultat qu'à basse fréquence, en correction (voir SurfaceSyncHost).
            var operation = AgingOperation(0f);
            bool hasWork = Grid.TotalFoam > 1e-4f || (operation.DryingPerSecond > 0f && Grid.TotalMoisture > 1e-4f);
            long timer = Diagnostics.PerfStats.Begin();
            int changed = aging.Advance(Grid, Time.deltaTime, tuning.agingInterval, hasWork, ref operation, slowReplication: true);
            if (aging.RowsLastAdvance > 0) Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.SurfaceAging, timer, changed);
            FoamDwellLoosened += operation.Loosened;
        }

        SurfaceAgingOperation AgingOperation(float seconds)
        {
            if (tuning == null) tuning = CleaningTuning.CreateDefault();
            return new SurfaceAgingOperation
            {
                Table = DirtTable,
                DeltaTime = seconds,
                FoamDecayPerSecond = tuning.foamDecayPerSecond,
                FoamDecayFloor = tuning.foamDecayFloor,
                FoamDwellPower = tuning.foamDwellPower,
                FoamToMoisture = tuning.foamToMoisture,
                DryingPerSecond = definition != null ? definition.dryingPerSecond : 0f,
                Products = Products,
                Fragility = Fragility,
            };
        }

        /// <summary>
        /// Simulation lente immédiate de toute la surface sur <paramref name="seconds"/> (tests, rattrapage) : la mousse détache
        /// les taches réactives au savon puis retombe ; l'humidité s'évapore si la surface sèche seule. Rien n'est calculé sur
        /// une surface sans mousse ni eau. En jeu, la simulation est répartie sur les images (<see cref="SurfaceAgingScheduler"/>).
        /// </summary>
        public void Age(float seconds)
        {
            if (Grid == null || seconds <= 0f) return;
            var operation = AgingOperation(seconds);
            bool dries = operation.DryingPerSecond > 0f && Grid.TotalMoisture > 1e-4f;
            if (Grid.TotalFoam <= 1e-4f && !dries) return;
            long timer = Diagnostics.PerfStats.Begin();
            int changed = Grid.ApplyAllTracked(ref operation, slowReplication: true);
            Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.SurfaceAging, timer, changed);
            FoamDwellLoosened += operation.Loosened;
        }

        /// <summary>Charge un tapis avec sa saleté par défaut ou une configuration fournie (commande).</summary>
        public void LoadRug(RugDefinition rug, IReadOnlyList<DirtLayer> dirtOverride = null, int? seedOverride = null)
        {
            if (rug == null) throw new ArgumentNullException(nameof(rug));
            definition = rug;
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] RugCleaningController '{name}' : référence manquante '{missing}'.", this);
                return;
            }

            DirtTable ??= dirtLibrary.BuildTable();
            Products ??= productLibrary != null ? productLibrary.BuildTable(DirtTable) : new ProductTable(null, DirtTable);

            Vector2Int resolution = rug.GridResolution;
            Grid = new RugCleaningGrid(resolution.x, resolution.y, rug.sizeMeters);
            // Surface décorative (sol de la salle) : jamais de saleté, même si la définition en déclare.
            if (rug.accumulatesDirt) DirtGenerator.Generate(Grid, dirtOverride ?? rug.defaultDirt, seedOverride ?? rug.dirtSeed, DirtTable);
            IsFinished = false;

            if (generatedMesh != null) UnityObjectUtility.SafeDestroy(generatedMesh);
            generatedMesh = RugMeshBuilder.Build(rug.sizeMeters, rug.thickness, rug.hasFringe);
            generatedMesh.hideFlags = HideFlags.DontSave;
            meshFilter.sharedMesh = generatedMesh;

            float colliderHeight = Mathf.Max(0.01f, rug.thickness);
            surfaceCollider.center = new Vector3(0f, rug.thickness - colliderHeight * 0.5f, 0f);
            surfaceCollider.size = new Vector3(rug.sizeMeters.x, colliderHeight, rug.sizeMeters.y);

            dirtRenderer.Bind(Grid, DirtTable, rug);
            FoamDwellLoosened = 0f;
            aging.Reset();
            surfaceCollider.enabled = true;
            if (dirtRenderer.TryGetComponent(out Renderer surfaceRenderer)) surfaceRenderer.enabled = true;
            RugLoaded?.Invoke(this);
        }

        /// <summary>Recharge le tapis posé (saleté par défaut). Sans tapis posé, rien ne se passe.</summary>
        public void ResetRug()
        {
            if (Grid != null && definition != null) LoadRug(definition);
        }

        /// <summary>
        /// Retire le tapis (fin ou abandon d'une commande) : plus de grille, plus de collision, plus de rendu. Les outils
        /// ne peuvent plus le viser ; la réplication réseau annonce une surface vide aux invités.
        /// </summary>
        public void Unload()
        {
            if (Grid == null) return;
            Grid = null;
            IsFinished = false;
            FoamDwellLoosened = 0f;
            Hide();
            RugUnloaded?.Invoke(this);
        }

        void Hide()
        {
            if (surfaceCollider != null) surfaceCollider.enabled = false;
            if (dirtRenderer != null && dirtRenderer.TryGetComponent(out Renderer surfaceRenderer)) surfaceRenderer.enabled = false;
        }

        /// <summary>
        /// Fin du tapis (autorité : solo ou hôte) : s'il reste moins de 1 % de la saleté initiale, le reliquat (cellules
        /// isolées, traces invisibles, arrondis) est retiré pour que la propreté atteigne exactement 100 % — le tapis ne
        /// reste jamais bloqué à 99 %. Le résultat est diffusé aux invités avec les cellules.
        /// </summary>
        void CheckFinished()
        {
            if (IsFinished || Grid == null || Kind != SurfaceKind.Rug || IsReplica) return;
            float threshold = tuning != null ? tuning.rugFinishedRemaining : 0.01f;
            if (!RugFinishRules.IsFinished(Grid.TotalDirt, Grid.InitialDirt, threshold)) return;
            var clear = new ClearDirtOperation();
            Grid.ApplyAll(ref clear);
            MarkFinished();
        }

        void MarkFinished()
        {
            IsFinished = true;
            Finished?.Invoke(this);
        }

        /// <summary>Convertit un point monde en UV du dessus du tapis. Faux si le point est hors du tapis.</summary>
        public bool TryWorldToUV(Vector3 worldPoint, out Vector2 uv)
        {
            uv = default;
            if (definition == null) return false;
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            uv = new Vector2(local.x / definition.sizeMeters.x + 0.5f, local.z / definition.sizeMeters.y + 0.5f);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        /// <summary>Lit la cellule située sous un point du monde (faux hors du tapis).</summary>
        public bool TrySampleCell(Vector3 worldPoint, out RugCell cell)
        {
            cell = default;
            if (Grid == null || !TryWorldToUV(worldPoint, out Vector2 uv)) return false;
            Vector2Int index = Grid.UVToCell(uv);
            cell = Grid.GetCell(index.x, index.y);
            return true;
        }

        public Vector3 UVToWorld(Vector2 uv)
        {
            float top = definition != null ? definition.thickness : 0f;
            Vector2 size = definition != null ? definition.sizeMeters : Vector2.one;
            return transform.TransformPoint(new Vector3((uv.x - 0.5f) * size.x, top, (uv.y - 0.5f) * size.y));
        }

        /// <summary>Applique une opération de nettoyage autour d'un point monde. Retourne le nombre de cellules touchées.</summary>
        public int ApplyOperation<T>(Vector3 worldPoint, float radiusMeters, float hardness, ref T operation)
            where T : struct, ICellOperation =>
            ApplyOperation(worldPoint, radiusMeters, hardness, 1f, Vector3.zero, default, ref operation);

        /// <summary>
        /// Applique une opération sur un disque (<paramref name="aspect"/> = 1) ou un éventail elliptique de même surface dont
        /// le grand axe suit <paramref name="spreadAxisWorld"/>, puis signale ce qui ruisselle selon <paramref name="runoff"/>.
        /// Sur une réplique (invité), l'opération est aussi transmise à l'hôte.
        /// </summary>
        public int ApplyOperation<T>(Vector3 worldPoint, float radiusMeters, float hardness, float aspect, Vector3 spreadAxisWorld,
            in RunoffProfile runoff, ref T operation) where T : struct, ICellOperation
        {
            if (Grid == null) return 0;
            var geometry = new SurfaceOpGeometry(worldPoint, radiusMeters, hardness, aspect, spreadAxisWorld, runoff);
            if (Replication != null) Replication.Forward(this, geometry, ref operation);
            return ApplyLocal(geometry, !IsReplica, ref operation);
        }

        /// <summary>
        /// Applique l'opération sans passer par le réseau : hôte (opération reçue d'un invité, avec ruissellement) ou
        /// invité (prédiction et rejeu, sans ruissellement : celui-ci est produit par l'hôte).
        /// </summary>
        public int ApplyLocal<T>(in SurfaceOpGeometry geometry, bool allowRunoff, ref T operation) where T : struct, ICellOperation
        {
            // Surface décorative : rien ne s'y dépose et rien n'y est nettoyé (aucune logique de salissure hors zone).
            if (Grid == null || !AcceptsDirt) return 0;
            long timer = Diagnostics.PerfStats.Begin();
            TryWorldToUV(geometry.Point, out Vector2 uv);
            DirtType type = DirtType.None;
            bool tracksRunoff = allowRunoff && !geometry.Runoff.IsNone && Runoff != null;
            if (tracksRunoff && TrySampleCell(geometry.Point, out RugCell sampled))
                type = sampled.StainType != DirtType.None ? sampled.StainType : sampled.DustType;
            float dirtBefore = Grid.TotalDirt, foamBefore = Grid.TotalFoam;

            int touched;
            if (geometry.Aspect <= 1.001f) touched = Grid.ApplyCircle(uv, geometry.Radius, geometry.Hardness, ref operation);
            else
            {
                Vector3 local = transform.InverseTransformDirection(geometry.Axis);
                float angle = Mathf.Atan2(local.z, local.x);
                float stretch = Mathf.Sqrt(geometry.Aspect);
                touched = Grid.ApplyEllipse(uv, geometry.Radius * stretch, geometry.Radius / stretch, angle, geometry.Hardness, ref operation);
            }

            if (touched > 0 && tracksRunoff)
            {
                float area = Grid.CellAreaM2;
                var runoff = geometry.Runoff;
                var runoffEvent = new RunoffEvent
                {
                    Point = geometry.Point,
                    Radius = geometry.Radius,
                    Dirt = Mathf.Max(0f, dirtBefore - Grid.TotalDirt) * runoff.Dirt * area,
                    Foam = Mathf.Max(0f, foamBefore - Grid.TotalFoam) * runoff.Foam * area,
                    Water = runoff.Water,
                    DirtIsDry = runoff.DirtIsDry,
                    Type = type,
                };
                if (runoffEvent.Dirt > 0f || runoffEvent.Foam > 0f || runoffEvent.Water > 0f) Runoff(this, runoffEvent);
            }
            if (touched > 0) CheckFinished();
            Diagnostics.PerfStats.End(Diagnostics.PerfStats.Slot.GridOps, timer, touched);
            return touched;
        }

        void OnDestroy()
        {
            if (generatedMesh != null) UnityObjectUtility.SafeDestroy(generatedMesh);
        }
    }
}
