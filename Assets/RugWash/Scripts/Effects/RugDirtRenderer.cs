using RugWash.Cleaning;
using RugWash.Core;
using Unity.Collections;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Traduit la grille de nettoyage en quatre textures de contrôle lues par le shader « RugWash/Rug Surface » :
    ///   ControlA = (poussière, saleté fixée, saleté détachée, mousse)
    ///   ControlB = (couleur de la saleté RGB, humidité)
    ///   ControlC = (résidus de produit, usure des fibres, adhérence de la saleté fixée, -)
    ///   ControlD = (couleur de la salissure sèche RGB, aspect : voile, grains ou brins)
    /// Seules les cellules du rectangle modifié sont réencodées, directement dans la mémoire de la texture,
    /// et l'envoi au GPU est limité à <see cref="maxUploadsPerSecond"/>. Aucune texture n'est recréée en jeu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugDirtRenderer : MonoBehaviour
    {
        static readonly int ControlAId = Shader.PropertyToID("_ControlA");
        static readonly int ControlBId = Shader.PropertyToID("_ControlB");
        static readonly int ControlCId = Shader.PropertyToID("_ControlC");
        static readonly int ControlDId = Shader.PropertyToID("_ControlD");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int FiberNormalId = Shader.PropertyToID("_FiberNormal");
        static readonly int FiberTilingId = Shader.PropertyToID("_FiberTiling");
        static readonly int FiberStrengthId = Shader.PropertyToID("_FiberNormalStrength");
        static readonly int RugSizeId = Shader.PropertyToID("_RugSize");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int DrySmoothnessId = Shader.PropertyToID("_DrySmoothness");

        [SerializeField] MeshRenderer targetRenderer;
        [Tooltip("Matériau modèle utilisant le shader RugWash/Rug Surface (instancié par tapis).")]
        [SerializeField] Material rugMaterialTemplate;
        [SerializeField] Material fringeMaterialTemplate;
        [SerializeField, Range(5f, 60f)] float maxUploadsPerSecond = 30f;

        RugCleaningGrid grid;
        DirtTypeTable table;
        Texture2D controlA, controlB, controlC, controlD;
        Material rugMaterial, fringeMaterial;
        Color32[] stainColors;
        float lastUploadTime = float.NegativeInfinity;

        public int UploadCount { get; private set; }
        public Texture2D ControlA => controlA;
        public Material RugMaterial => rugMaterial;

        public bool IsConfigured(out string missing)
        {
            missing = targetRenderer == null ? nameof(targetRenderer)
                : rugMaterialTemplate == null ? nameof(rugMaterialTemplate)
                : fringeMaterialTemplate == null ? nameof(fringeMaterialTemplate) : null;
            return missing == null;
        }

        public void Bind(RugCleaningGrid newGrid, DirtTypeTable dirtTable, RugDefinition definition)
        {
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] RugDirtRenderer : référence manquante '{missing}'.", this);
                enabled = false;
                return;
            }

            grid = newGrid;
            table = dirtTable;
            EnsureTextures(grid.Width, grid.Height);
            EnsureMaterials(definition);
            CacheStainColors();
            grid.MarkAllDirty();
            UploadNow();
        }

        /// <summary>Force l'encodage et l'envoi immédiat (avant une capture « avant/après »).</summary>
        public void UploadNow()
        {
            if (grid == null || !grid.TryConsumeDirtyRect(out RectInt rect)) return;
            Encode(rect);
            controlA.Apply(false);
            controlB.Apply(false);
            controlC.Apply(false);
            controlD.Apply(false);
            lastUploadTime = Time.unscaledTime;
            UploadCount++;
        }

        void LateUpdate()
        {
            if (grid == null) return;
            if (Time.unscaledTime - lastUploadTime < 1f / maxUploadsPerSecond) return;
            UploadNow();
        }

        void Encode(RectInt rect)
        {
            NativeArray<Color32> a = controlA.GetPixelData<Color32>(0);
            NativeArray<Color32> b = controlB.GetPixelData<Color32>(0);
            NativeArray<Color32> c = controlC.GetPixelData<Color32>(0);
            NativeArray<Color32> d = controlD.GetPixelData<Color32>(0);
            int width = grid.Width;
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                RugCell cell = grid.GetCell(x, y);
                int i = y * width + x;
                a[i] = new Color32(ToByte(cell.Dust), ToByte(cell.Stain), ToByte(cell.Loosened), ToByte(cell.Foam));
                DirtType colorType = cell.StainType != DirtType.None ? cell.StainType : cell.DustType;
                Color32 tint = stainColors[(int)colorType];
                tint.a = ToByte(cell.Moisture);
                b[i] = tint;
                c[i] = new Color32(ToByte(cell.Residue), ToByte(cell.Wear), ToByte(cell.Adhesion), 0);
                // Salissure sèche : sa propre teinte, indépendante d'une tache dans la même cellule.
                Color32 dustTint = stainColors[(int)cell.DustType];
                dustTint.a = ToByte(DirtTypes.LooseLook(cell.DustType));
                d[i] = dustTint;
            }
        }

        void CacheStainColors()
        {
            stainColors = new Color32[DirtTypes.Count];
            for (int i = 0; i < DirtTypes.Count; i++)
            {
                // Cellule sans tache : teinte terreuse neutre, pour éviter un liseré noir à l'interpolation.
                Color c = i == 0 ? DirtTypeTable.DefaultColor(DirtType.None) : table.GetColor((DirtType)i);
                stainColors[i] = c.linear;
            }
        }

        void EnsureTextures(int width, int height)
        {
            if (controlA != null && controlA.width == width && controlA.height == height) return;
            ReleaseTextures();
            controlA = CreateControlTexture("RugControlA", width, height);
            controlB = CreateControlTexture("RugControlB", width, height);
            controlC = CreateControlTexture("RugControlC", width, height);
            controlD = CreateControlTexture("RugControlD", width, height);
        }

        static Texture2D CreateControlTexture(string name, int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

        void EnsureMaterials(RugDefinition definition)
        {
            if (rugMaterial == null)
                rugMaterial = new Material(rugMaterialTemplate) { name = "RugSurface (instance)", hideFlags = HideFlags.DontSave };
            // Éclat propre à la matière (soie) ; sinon celui du matériau modèle.
            rugMaterial.SetFloat(DrySmoothnessId, definition.sheen > 0f ? definition.sheen : rugMaterialTemplate.GetFloat(DrySmoothnessId));
            if (fringeMaterial == null)
                fringeMaterial = new Material(fringeMaterialTemplate) { name = "RugFringe (instance)", hideFlags = HideFlags.DontSave };

            rugMaterial.SetTexture(ControlAId, controlA);
            rugMaterial.SetTexture(ControlBId, controlB);
            rugMaterial.SetTexture(ControlCId, controlC);
            rugMaterial.SetTexture(ControlDId, controlD);
            if (definition.patternTexture != null) rugMaterial.SetTexture(BaseMapId, definition.patternTexture);
            if (definition.fiberNormal != null) rugMaterial.SetTexture(FiberNormalId, definition.fiberNormal);
            rugMaterial.SetFloat(FiberTilingId, definition.fiberTilesPerMeter);
            rugMaterial.SetFloat(FiberStrengthId, definition.fiberNormalStrength);
            rugMaterial.SetVector(RugSizeId, new Vector4(definition.sizeMeters.x, definition.sizeMeters.y, 0f, 0f));
            fringeMaterial.SetColor(BaseColorId, definition.fringeColor);

            targetRenderer.sharedMaterials = definition.hasFringe
                ? new[] { rugMaterial, fringeMaterial }
                : new[] { rugMaterial };
        }

        static byte ToByte(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        void ReleaseTextures()
        {
            if (controlA != null) UnityObjectUtility.SafeDestroy(controlA);
            if (controlB != null) UnityObjectUtility.SafeDestroy(controlB);
            if (controlC != null) UnityObjectUtility.SafeDestroy(controlC);
            if (controlD != null) UnityObjectUtility.SafeDestroy(controlD);
            controlA = controlB = controlC = controlD = null;
        }

        void OnDestroy()
        {
            ReleaseTextures();
            if (rugMaterial != null) UnityObjectUtility.SafeDestroy(rugMaterial);
            if (fringeMaterial != null) UnityObjectUtility.SafeDestroy(fringeMaterial);
        }
    }
}
