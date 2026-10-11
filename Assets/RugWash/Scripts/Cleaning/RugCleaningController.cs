using System;
using System.Collections.Generic;
using RugWash.Core;
using RugWash.Effects;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Tapis nettoyable présent dans la scène. Possède sa grille (aucun état global),
    /// convertit les impacts monde → UV et délègue le visuel à <see cref="RugDirtRenderer"/>.
    /// Plusieurs tapis peuvent coexister (préparation d'un atelier multi-postes / coop).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugCleaningController : MonoBehaviour
    {
        [SerializeField] RugDefinition definition;
        [SerializeField] DirtTypeLibrary dirtLibrary;
        [SerializeField] RugDirtRenderer dirtRenderer;
        [SerializeField] MeshFilter meshFilter;
        [SerializeField] BoxCollider surfaceCollider;

        Mesh generatedMesh;

        public RugCleaningGrid Grid { get; private set; }
        public RugDefinition Definition => definition;
        public DirtTypeTable DirtTable { get; private set; }
        public RugDirtRenderer DirtRenderer => dirtRenderer;
        public float Cleanliness01 => Grid != null ? Grid.Cleanliness01 : 0f;

        /// <summary>Déclenché après chaque chargement ou réinitialisation de tapis.</summary>
        public event Action<RugCleaningController> RugLoaded;

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
            if (Grid == null && definition != null) LoadRug(definition);
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

        void OnDestroy()
        {
            if (generatedMesh != null) UnityObjectUtility.SafeDestroy(generatedMesh);
        }
    }
}
