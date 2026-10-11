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

        Mesh generatedMesh;
        float agingTimer;

        public RugCleaningGrid Grid { get; private set; }
        public RugDefinition Definition => definition;
        public DirtTypeTable DirtTable { get; private set; }
        public RugDirtRenderer DirtRenderer => dirtRenderer;
        public float Cleanliness01 => Grid != null ? Grid.Cleanliness01 : 0f;
        public SurfaceKind Kind => definition != null ? definition.surfaceKind : SurfaceKind.Rug;
        public CleaningTuning Tuning => tuning;
        /// <summary>Taille de la surface en mètres (X, Z locaux).</summary>
        public Vector2 SizeMeters => definition != null ? definition.sizeMeters : Vector2.zero;

        /// <summary>Déclenché après chaque chargement ou réinitialisation de tapis.</summary>
        public event Action<RugCleaningController> RugLoaded;

        /// <summary>Ce qu'une opération a fait ruisseler hors de la surface (eau sale, mousse, débris).</summary>
        public event Action<RugCleaningController, RunoffEvent> Runoff;

        /// <summary>Saleté fixée détachée par la mousse au repos depuis le chargement.</summary>
        public float FoamDwellLoosened { get; private set; }

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
            if (Grid == null && definition != null) LoadRug(definition);
        }

        void Update()
        {
            if (Grid == null || Time.deltaTime <= 0f) return;
            agingTimer += Time.deltaTime;
            if (agingTimer < tuning.agingInterval) return;
            Age(agingTimer);
            agingTimer = 0f;
        }

        /// <summary>
        /// Simulation lente sur <paramref name="seconds"/> : la mousse détache les taches réactives au savon puis retombe ;
        /// l'humidité s'évapore si la surface sèche seule. Rien n'est calculé sur une surface sans mousse ni eau.
        /// </summary>
        public void Age(float seconds)
        {
            if (Grid == null || seconds <= 0f) return;
            if (tuning == null) tuning = CleaningTuning.CreateDefault();
            float drying = definition != null ? definition.dryingPerSecond : 0f;
            bool hasFoam = Grid.TotalFoam > 1e-4f;
            bool dries = drying > 0f && Grid.TotalMoisture > 1e-4f;
            if (!hasFoam && !dries) return;
            var operation = new SurfaceAgingOperation
            {
                Table = DirtTable,
                DeltaTime = seconds,
                FoamDecayPerSecond = tuning.foamDecayPerSecond,
                FoamDecayFloor = tuning.foamDecayFloor,
                FoamDwellPower = tuning.foamDwellPower,
                FoamToMoisture = tuning.foamToMoisture,
                DryingPerSecond = drying,
            };
            Grid.ApplyAllTracked(ref operation);
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

            Vector2Int resolution = rug.GridResolution;
            Grid = new RugCleaningGrid(resolution.x, resolution.y, rug.sizeMeters);
            DirtGenerator.Generate(Grid, dirtOverride ?? rug.defaultDirt, seedOverride ?? rug.dirtSeed);

            if (generatedMesh != null) UnityObjectUtility.SafeDestroy(generatedMesh);
            generatedMesh = RugMeshBuilder.Build(rug.sizeMeters, rug.thickness, rug.hasFringe);
            generatedMesh.hideFlags = HideFlags.DontSave;
            meshFilter.sharedMesh = generatedMesh;

            float colliderHeight = Mathf.Max(0.01f, rug.thickness);
            surfaceCollider.center = new Vector3(0f, rug.thickness - colliderHeight * 0.5f, 0f);
            surfaceCollider.size = new Vector3(rug.sizeMeters.x, colliderHeight, rug.sizeMeters.y);

            dirtRenderer.Bind(Grid, DirtTable, rug);
            FoamDwellLoosened = 0f;
            agingTimer = 0f;
            RugLoaded?.Invoke(this);
        }

        public void ResetRug()
        {
            if (definition != null) LoadRug(definition);
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
            where T : struct, ICellOperation
        {
            if (Grid == null) return 0;
            TryWorldToUV(worldPoint, out Vector2 uv);
            // Un impact juste au bord touche encore les cellules du bord ; au-delà du rayon, rien.
            return Grid.ApplyCircle(uv, radiusMeters, hardness, ref operation);
        }

        /// <summary>
        /// Applique une opération sur un disque (<paramref name="aspect"/> = 1) ou un éventail elliptique de même surface dont
        /// le grand axe suit <paramref name="spreadAxisWorld"/>, puis signale ce qui ruisselle selon <paramref name="runoff"/>.
        /// </summary>
        public int ApplyOperation<T>(Vector3 worldPoint, float radiusMeters, float hardness, float aspect, Vector3 spreadAxisWorld,
            in RunoffProfile runoff, ref T operation) where T : struct, ICellOperation
        {
            if (Grid == null) return 0;
            TryWorldToUV(worldPoint, out Vector2 uv);
            DirtType type = DirtType.None;
            bool tracksRunoff = !runoff.IsNone && Runoff != null;
            if (tracksRunoff && TrySampleCell(worldPoint, out RugCell sampled))
                type = sampled.StainType != DirtType.None ? sampled.StainType : sampled.DustType;
            float dirtBefore = Grid.TotalDirt, foamBefore = Grid.TotalFoam;

            int touched;
            if (aspect <= 1.001f) touched = Grid.ApplyCircle(uv, radiusMeters, hardness, ref operation);
            else
            {
                Vector3 local = transform.InverseTransformDirection(spreadAxisWorld);
                float angle = Mathf.Atan2(local.z, local.x);
                float stretch = Mathf.Sqrt(aspect);
                touched = Grid.ApplyEllipse(uv, radiusMeters * stretch, radiusMeters / stretch, angle, hardness, ref operation);
            }

            if (touched > 0 && tracksRunoff)
            {
                float area = Grid.CellAreaM2;
                var runoffEvent = new RunoffEvent
                {
                    Point = worldPoint,
                    Radius = radiusMeters,
                    Dirt = Mathf.Max(0f, dirtBefore - Grid.TotalDirt) * runoff.Dirt * area,
                    Foam = Mathf.Max(0f, foamBefore - Grid.TotalFoam) * runoff.Foam * area,
                    Water = runoff.Water,
                    DirtIsDry = runoff.DirtIsDry,
                    Type = type,
                };
                if (runoffEvent.Dirt > 0f || runoffEvent.Foam > 0f || runoffEvent.Water > 0f) Runoff(this, runoffEvent);
            }
            return touched;
        }

        void OnDestroy()
        {
            if (generatedMesh != null) UnityObjectUtility.SafeDestroy(generatedMesh);
        }
    }
}
