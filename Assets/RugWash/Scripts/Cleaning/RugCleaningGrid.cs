using System;
using System.Collections.Generic;
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

    /// <summary>Opération qui sait reconnaître, sans rien copier, une cellule qu'elle ne modifierait pas (parcours rapides).</summary>
    public interface ICellSkip
    {
        bool Skips(in RugCell cell);
    }

    /// <summary>
    /// Grille de nettoyage d'un tapis : source de vérité du gameplay.
    /// Maintient des totaux incrémentaux (pas de parcours complet par frame) et trois rectangles de cellules modifiées :
    /// rendu, réplication réseau rapide (opérations des outils) et réplication lente (simulation lente : mousse qui retombe,
    /// séchage — reproduite par chaque poste, l'hôte ne la corrige qu'à basse fréquence). Pour le rendu, l'étendue modifiée
    /// est aussi suivie ligne par ligne (<see cref="ConsumeDirtyRows"/>) : la simulation lente avance ligne après ligne et
    /// repasse du bas au haut de la grille ; un seul rectangle englobant couvrait alors toute la grille (réencodage complet).
    /// </summary>
    public sealed class RugCleaningGrid
    {
        /// <summary>Seuil de saleté sous lequel une cellule est considérée propre.</summary>
        public const float CleanCellThreshold = 0.05f;

        readonly RugCell[] cells;

        double totalDirt, totalMoisture, totalFoam, totalResidue, totalWear;
        int dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY;
        bool hasDirtyRect;
        /// <summary>Rendu : colonnes modifiées de chaque ligne (min, max inclus) ; max &lt; 0 = ligne intacte.</summary>
        readonly int[] rowDirtyMin, rowDirtyMax;
        int netMinX, netMinY, netMaxX, netMaxY;
        bool hasNetDirtyRect;
        int slowMinX, slowMinY, slowMaxX, slowMaxY;
        bool hasSlowNetRect;
        RectInt clip;
        bool hasClip;

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
            rowDirtyMin = new int[height];
            rowDirtyMax = new int[height];
            for (int y = 0; y < height; y++) rowDirtyMax[y] = -1;
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

        /// <summary>Saleté de référence fixée par l'autorité réseau (réplique d'un client).</summary>
        public void SetInitialDirt(float value) => InitialDirt = Mathf.Max(0f, value);

        /// <summary>
        /// Limite les opérations sur disque et ellipse au rectangle donné (rejeu de la prédiction d'un client sur la
        /// seule zone que l'hôte vient de réécrire). <see cref="ClearClip"/> rétablit la grille entière.
        /// </summary>
        public void SetClip(RectInt cellRect)
        {
            clip = cellRect;
            hasClip = true;
        }

        public void ClearClip() => hasClip = false;

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
            ApplyClip(ref minX, ref minY, ref maxX, ref maxY);
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
            ApplyClip(ref minX, ref minY, ref maxX, ref maxY);
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
        public int ApplyAllTracked<T>(ref T operation, bool slowReplication = false) where T : struct, ICellOperation
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
            if (changed > 0) MarkDirty(minX, minY, maxX, maxY, slowReplication);
            return changed;
        }

        /// <summary>
        /// Comme <see cref="ApplyAllTracked{T}"/>, pour une seule ligne (simulation lente répartie sur les images).
        /// <paramref name="slowReplication"/> : la modification est reproduite localement par chaque poste ; elle ne part pas
        /// avec la réplication rapide.
        /// </summary>
        public int ApplyRowTracked<T>(int y, ref T operation, bool slowReplication) where T : struct, ICellOperation, ICellSkip
        {
            if ((uint)y >= (uint)Height) return 0;
            int changed = 0, minX = Width, maxX = -1;
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                ref RugCell cell = ref cells[row + x];
                // Cellule sans mousse ni eau : rien à simuler, aucune copie.
                if (operation.Skips(in cell)) continue;
                RugCell before = cell;
                operation.Apply(ref cell, 1f);
                Sanitize(ref cell);
                if (Same(before, cell)) continue;
                Accumulate(before, -1);
                Accumulate(cell, 1);
                changed++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
            if (changed > 0) MarkDirty(minX, y, maxX, y, slowReplication);
            return changed;
        }

        static bool Same(in RugCell a, in RugCell b) =>
            a.Dust == b.Dust && a.Stain == b.Stain && a.Loosened == b.Loosened && a.Foam == b.Foam && a.Moisture == b.Moisture
            && a.Residue == b.Residue && a.Wear == b.Wear && a.DustType == b.DustType && a.StainType == b.StainType
            && a.Adhesion == b.Adhesion && a.Product == b.Product;

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
            ClearDirtyRows(dirtyMinY, dirtyMaxY);
            return true;
        }

        /// <summary>Étendue modifiée d'une ligne, pour le rendu.</summary>
        public readonly struct RowSpan
        {
            public readonly int Y, XMin, XMax;

            public RowSpan(int y, int xMin, int xMax)
            {
                Y = y;
                XMin = xMin;
                XMax = xMax;
            }

            public int Width => XMax - XMin + 1;
        }

        /// <summary>
        /// Rendu : récupère puis réinitialise les étendues modifiées ligne par ligne (seules les cellules réellement touchées
        /// sont réencodées, même quand des lignes éloignées ont changé). Retourne le nombre de cellules concernées.
        /// </summary>
        public int ConsumeDirtyRows(List<RowSpan> spans)
        {
            spans.Clear();
            if (!hasDirtyRect) return 0;
            int count = 0;
            for (int y = dirtyMinY; y <= dirtyMaxY; y++)
            {
                if (rowDirtyMax[y] < 0) continue;
                var span = new RowSpan(y, rowDirtyMin[y], rowDirtyMax[y]);
                spans.Add(span);
                count += span.Width;
            }
            hasDirtyRect = false;
            ClearDirtyRows(dirtyMinY, dirtyMaxY);
            return count;
        }

        void ClearDirtyRows(int minY, int maxY)
        {
            for (int y = minY; y <= maxY; y++) rowDirtyMax[y] = -1;
        }

        /// <summary>Récupère puis réinitialise la zone modifiée depuis le dernier envoi réseau (indépendante du rendu).</summary>
        public bool TryConsumeNetworkDirtyRect(out RectInt rect)
        {
            if (!hasNetDirtyRect)
            {
                rect = default;
                return false;
            }
            rect = new RectInt(netMinX, netMinY, netMaxX - netMinX + 1, netMaxY - netMinY + 1);
            hasNetDirtyRect = false;
            return true;
        }

        void ApplyClip(ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            if (!hasClip) return;
            minX = Mathf.Max(minX, clip.xMin);
            minY = Mathf.Max(minY, clip.yMin);
            maxX = Mathf.Min(maxX, clip.xMax - 1);
            maxY = Mathf.Min(maxY, clip.yMax - 1);
        }

        int Index(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException($"Cellule ({x},{y}) hors de la grille {Width}x{Height}.");
            return y * Width + x;
        }

        /// <summary>Récupère puis réinitialise la zone modifiée par la seule simulation lente depuis le dernier envoi lent.</summary>
        public bool TryConsumeSlowNetworkRect(out RectInt rect)
        {
            if (!hasSlowNetRect)
            {
                rect = default;
                return false;
            }
            rect = new RectInt(slowMinX, slowMinY, slowMaxX - slowMinX + 1, slowMaxY - slowMinY + 1);
            hasSlowNetRect = false;
            return true;
        }

        void MarkDirty(int minX, int minY, int maxX, int maxY, bool slowReplication = false)
        {
            Version++;
            if (slowReplication)
            {
                if (!hasSlowNetRect)
                {
                    slowMinX = minX; slowMinY = minY; slowMaxX = maxX; slowMaxY = maxY;
                    hasSlowNetRect = true;
                }
                else
                {
                    slowMinX = Mathf.Min(slowMinX, minX);
                    slowMinY = Mathf.Min(slowMinY, minY);
                    slowMaxX = Mathf.Max(slowMaxX, maxX);
                    slowMaxY = Mathf.Max(slowMaxY, maxY);
                }
            }
            else if (!hasNetDirtyRect)
            {
                netMinX = minX; netMinY = minY; netMaxX = maxX; netMaxY = maxY;
                hasNetDirtyRect = true;
            }
            else
            {
                netMinX = Mathf.Min(netMinX, minX);
                netMinY = Mathf.Min(netMinY, minY);
                netMaxX = Mathf.Max(netMaxX, maxX);
                netMaxY = Mathf.Max(netMaxY, maxY);
            }
            for (int y = minY; y <= maxY; y++)
            {
                if (rowDirtyMax[y] < 0)
                {
                    rowDirtyMin[y] = minX;
                    rowDirtyMax[y] = maxX;
                }
                else
                {
                    if (minX < rowDirtyMin[y]) rowDirtyMin[y] = minX;
                    if (maxX > rowDirtyMax[y]) rowDirtyMax[y] = maxX;
                }
            }
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
            c.Adhesion = Clamp01Finite(c.Adhesion);
            if (c.Stain <= 0f) c.Adhesion = 0f;
            if (c.Foam <= 0f && c.Residue <= 0f) c.Product = 0;
            if (c.Stain <= 0f && c.Loosened <= 0f) c.StainType = DirtType.None;
            if (c.Dust <= 0f) c.DustType = DirtType.None;
        }

        static float Clamp01Finite(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);
    }
}
