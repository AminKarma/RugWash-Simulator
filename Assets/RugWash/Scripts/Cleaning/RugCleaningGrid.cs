using System;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Opération appliquée cellule par cellule (struct pour éviter toute allocation).</summary>
    public interface ICellOperation
    {
        /// <param name="cell">Cellule à modifier.</param>
        /// <param name="weight">Intensité locale [0..1] issue de l'atténuation radiale de l'outil.</param>
        void Apply(ref RugCell cell, float weight);
    }

    /// <summary>
    /// Grille de nettoyage d'un tapis : source de vérité du gameplay.
    /// Maintient des totaux incrémentaux (pas de parcours complet par frame) et un rectangle
    /// de cellules modifiées, consommé par le rendu pour des mises à jour localisées.
    /// </summary>
    public sealed class RugCleaningGrid
    {
        /// <summary>Seuil de saleté sous lequel une cellule est considérée propre.</summary>
        public const float CleanCellThreshold = 0.05f;

        readonly RugCell[] cells;

        double totalDirt, totalMoisture, totalFoam, totalResidue, totalWear;
        int dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY;
        bool hasDirtyRect;

        public int Width { get; }
        public int Height { get; }
        public Vector2 SizeMeters { get; }
        public int CellCount => cells.Length;
        public float CellAreaM2 => SizeMeters.x / Width * (SizeMeters.y / Height);
        public float InitialDirt { get; private set; }

        /// <summary>Incrémenté à chaque modification : permet aux observateurs de détecter un changement sans événement.</summary>
        public int Version { get; private set; }

        public RugCleaningGrid(int width, int height, Vector2 sizeMeters)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Résolution de grille invalide.");
            if (sizeMeters.x <= 0f || sizeMeters.y <= 0f) throw new ArgumentOutOfRangeException(nameof(sizeMeters), "Taille de tapis invalide.");
            Width = width;
            Height = height;
            SizeMeters = sizeMeters;
            cells = new RugCell[width * height];
        }

        public float TotalDirt => (float)totalDirt;
        public float TotalFoam => (float)totalFoam;
        public float TotalMoisture => (float)totalMoisture;
        public float AverageMoisture => (float)(totalMoisture / cells.Length);
        public float AverageFoam => (float)(totalFoam / cells.Length);
        public float AverageResidue => (float)(totalResidue / cells.Length);
        public float AverageWear => (float)(totalWear / cells.Length);

        /// <summary>Part de la saleté initiale retirée [0..1]. 1 si le tapis était propre au départ.</summary>
        public float Cleanliness01 => InitialDirt <= 1e-5f ? 1f : Mathf.Clamp01(1f - (float)totalDirt / InitialDirt);

        public RugCell GetCell(int x, int y) => cells[Index(x, y)];

        public void SetCell(int x, int y, RugCell value)
        {
            int i = Index(x, y);
            Accumulate(cells[i], -1);
            cells[i] = value;
            Accumulate(value, 1);
            MarkDirty(x, y, x, y);
        }

        /// <summary>Fixe l'état courant comme référence « avant nettoyage ».</summary>
        public void CaptureInitialState()
        {
            RecomputeTotals();
            InitialDirt = (float)totalDirt;
        }

        public void Clear()
        {
            Array.Clear(cells, 0, cells.Length);
            RecomputeTotals();
            InitialDirt = 0f;
            MarkAllDirty();
        }

        /// <summary>Recalcule les totaux depuis les cellules (corrige toute dérive flottante).</summary>
        public void RecomputeTotals()
        {
            totalDirt = totalMoisture = totalFoam = totalResidue = totalWear = 0;
            for (int i = 0; i < cells.Length; i++)
                Accumulate(cells[i], 1);
        }

        public RugCleaningSummary ComputeSummary()
        {
            RecomputeTotals();
            int clean = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Dirt < CleanCellThreshold) clean++;
            return new RugCleaningSummary(Cleanliness01, clean / (float)cells.Length, AverageMoisture, AverageFoam,
                AverageResidue, AverageWear, (float)totalDirt, InitialDirt);
        }

        /// <summary>Convertit des coordonnées UV [0..1] en cellule.</summary>
        public Vector2Int UVToCell(Vector2 uv) => new Vector2Int(
            Mathf.Clamp((int)(uv.x * Width), 0, Width - 1),
            Mathf.Clamp((int)(uv.y * Height), 0, Height - 1));

        /// <summary>
        /// Applique une opération sur un disque centré en <paramref name="uv"/>, de rayon en mètres.
        /// Atténuation : weight = 1 - (d/r)^hardness (hardness élevé = bord net).
        /// Retourne le nombre de cellules touchées. Aucune cellule hors du disque n'est modifiée.
        /// </summary>
        public int ApplyCircle<T>(Vector2 uv, float radiusMeters, float hardness, ref T operation) where T : struct, ICellOperation
        {
            if (radiusMeters <= 0f) return 0;

            float cellW = SizeMeters.x / Width, cellH = SizeMeters.y / Height;
            float cx = uv.x * Width, cy = uv.y * Height;
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radiusMeters / cellW));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + radiusMeters / cellW));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radiusMeters / cellH));
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + radiusMeters / cellH));
            if (minX > maxX || minY > maxY) return 0;

            float r2 = radiusMeters * radiusMeters;
            int touched = 0;
            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y + 0.5f - cy) * cellH;
                int row = y * Width;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f - cx) * cellW;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;

                    float t = Mathf.Sqrt(d2) / radiusMeters;
                    float weight = 1f - Mathf.Pow(t, hardness);
                    if (weight <= 0f) continue;

                    ref RugCell cell = ref cells[row + x];
                    RugCell before = cell;
                    operation.Apply(ref cell, weight);
                    Sanitize(ref cell);
                    Accumulate(before, -1);
                    Accumulate(cell, 1);
                    touched++;
                }
            }

            if (touched > 0) MarkDirty(minX, minY, maxX, maxY);
            return touched;
        }

        /// <summary>
        /// Applique une opération sur une ellipse (jet en éventail) : demi-axes en mètres, orientée de
        /// <paramref name="angleRadians"/> dans le plan de la grille (0 = grand axe le long de X).
        /// Même atténuation que le disque ; aucune cellule hors de l'ellipse n'est modifiée.
        /// </summary>
        public int ApplyEllipse<T>(Vector2 uv, float radiusXMeters, float radiusYMeters, float angleRadians, float hardness, ref T operation)
            where T : struct, ICellOperation
        {
            if (radiusXMeters <= 0f || radiusYMeters <= 0f) return 0;
            if (Mathf.Approximately(radiusXMeters, radiusYMeters)) return ApplyCircle(uv, radiusXMeters, hardness, ref operation);

            float extent = Mathf.Max(radiusXMeters, radiusYMeters);
            float cellW = SizeMeters.x / Width, cellH = SizeMeters.y / Height;
            float cx = uv.x * Width, cy = uv.y * Height;
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - extent / cellW));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + extent / cellW));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - extent / cellH));
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + extent / cellH));
            if (minX > maxX || minY > maxY) return 0;

            float cos = Mathf.Cos(angleRadians), sin = Mathf.Sin(angleRadians);
            float invX = 1f / radiusXMeters, invY = 1f / radiusYMeters;
            int touched = 0;
            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y + 0.5f - cy) * cellH;
                int row = y * Width;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f - cx) * cellW;
                    float u = (dx * cos + dy * sin) * invX;
                    float v = (-dx * sin + dy * cos) * invY;
                    float n2 = u * u + v * v;
                    if (n2 > 1f) continue;

                    float weight = 1f - Mathf.Pow(Mathf.Sqrt(n2), hardness);
                    if (weight <= 0f) continue;

                    ref RugCell cell = ref cells[row + x];
                    RugCell before = cell;
                    operation.Apply(ref cell, weight);
                    Sanitize(ref cell);
                    Accumulate(before, -1);
                    Accumulate(cell, 1);
                    touched++;
                }
            }

            if (touched > 0) MarkDirty(minX, minY, maxX, maxY);
            return touched;
        }

        /// <summary>Applique une opération avec un poids uniforme sur un rectangle de cellules (bornes incluses, rognées).</summary>
        public int ApplyRect<T>(RectInt cellRect, float weight, ref T operation) where T : struct, ICellOperation
        {
            int minX = Mathf.Max(0, cellRect.xMin), maxX = Mathf.Min(Width - 1, cellRect.xMax - 1);
            int minY = Mathf.Max(0, cellRect.yMin), maxY = Mathf.Min(Height - 1, cellRect.yMax - 1);
            if (minX > maxX || minY > maxY || weight <= 0f) return 0;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                ref RugCell cell = ref cells[y * Width + x];
                RugCell before = cell;
                operation.Apply(ref cell, weight);
                Sanitize(ref cell);
                Accumulate(before, -1);
                Accumulate(cell, 1);
            }
            MarkDirty(minX, minY, maxX, maxY);
            return (maxX - minX + 1) * (maxY - minY + 1);
        }

        /// <summary>
        /// Applique une opération à toutes les cellules en ne signalant au rendu que la zone réellement modifiée
        /// (simulation lente : mousse qui retombe, séchage). Retourne le nombre de cellules modifiées.
        /// </summary>
        public int ApplyAllTracked<T>(ref T operation) where T : struct, ICellOperation
        {
            int changed = 0, minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    ref RugCell cell = ref cells[row + x];
                    RugCell before = cell;
                    operation.Apply(ref cell, 1f);
                    Sanitize(ref cell);
                    if (Same(before, cell)) continue;
                    Accumulate(before, -1);
                    Accumulate(cell, 1);
                    changed++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (changed > 0) MarkDirty(minX, minY, maxX, maxY);
            return changed;
        }

        static bool Same(in RugCell a, in RugCell b) =>
            a.Dust == b.Dust && a.Stain == b.Stain && a.Loosened == b.Loosened && a.Foam == b.Foam && a.Moisture == b.Moisture
            && a.Residue == b.Residue && a.Wear == b.Wear && a.DustType == b.DustType && a.StainType == b.StainType;

        /// <summary>Applique une opération à toutes les cellules (génération, réinitialisation).</summary>
        public void ApplyAll<T>(ref T operation) where T : struct, ICellOperation
        {
            for (int i = 0; i < cells.Length; i++)
            {
                operation.Apply(ref cells[i], 1f);
                Sanitize(ref cells[i]);
            }
            RecomputeTotals();
            MarkAllDirty();
        }

        public void MarkAllDirty() => MarkDirty(0, 0, Width - 1, Height - 1);

        /// <summary>Récupère puis réinitialise la zone modifiée depuis le dernier appel.</summary>
        public bool TryConsumeDirtyRect(out RectInt rect)
        {
            if (!hasDirtyRect)
            {
                rect = default;
                return false;
            }
            rect = new RectInt(dirtyMinX, dirtyMinY, dirtyMaxX - dirtyMinX + 1, dirtyMaxY - dirtyMinY + 1);
            hasDirtyRect = false;
            return true;
        }

        int Index(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException($"Cellule ({x},{y}) hors de la grille {Width}x{Height}.");
            return y * Width + x;
        }

        void MarkDirty(int minX, int minY, int maxX, int maxY)
        {
            Version++;
            if (!hasDirtyRect)
            {
                dirtyMinX = minX; dirtyMinY = minY; dirtyMaxX = maxX; dirtyMaxY = maxY;
                hasDirtyRect = true;
                return;
            }
            dirtyMinX = Mathf.Min(dirtyMinX, minX);
            dirtyMinY = Mathf.Min(dirtyMinY, minY);
            dirtyMaxX = Mathf.Max(dirtyMaxX, maxX);
            dirtyMaxY = Mathf.Max(dirtyMaxY, maxY);
        }

        void Accumulate(in RugCell cell, int sign)
        {
            totalDirt += sign * (double)cell.Dirt;
            totalMoisture += sign * (double)cell.Moisture;
            totalFoam += sign * (double)cell.Foam;
            totalResidue += sign * (double)cell.Residue;
            totalWear += sign * (double)cell.Wear;
        }

        /// <summary>Garantit des valeurs finies et dans les bornes (aucune valeur négative ne peut créer de « propreté » fictive).</summary>
        static void Sanitize(ref RugCell c)
        {
            c.Dust = Clamp01Finite(c.Dust);
            c.Stain = Clamp01Finite(c.Stain);
            c.Loosened = Mathf.Clamp(float.IsNaN(c.Loosened) ? 0f : c.Loosened, 0f, 2f);
            c.Foam = Clamp01Finite(c.Foam);
            c.Moisture = Clamp01Finite(c.Moisture);
            c.Residue = Clamp01Finite(c.Residue);
            c.Wear = Clamp01Finite(c.Wear);
            if (c.Stain <= 0f && c.Loosened <= 0f) c.StainType = DirtType.None;
            if (c.Dust <= 0f) c.DustType = DirtType.None;
        }

        static float Clamp01Finite(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);
    }
}
