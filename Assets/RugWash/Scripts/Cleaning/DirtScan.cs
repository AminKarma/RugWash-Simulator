using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Résultat d'un scan : nombre de cellules encore sales ou chargées de résidus de produit.</summary>
    public readonly struct DirtScanResult
    {
        public readonly int DirtyCells;
        public readonly int ResidueCells;
        public readonly int TotalCells;

        public DirtScanResult(int dirtyCells, int residueCells, int totalCells)
        {
            DirtyCells = dirtyCells;
            ResidueCells = residueCells;
            TotalCells = totalCells;
        }

        public float DirtyFraction => TotalCells > 0 ? DirtyCells / (float)TotalCells : 0f;
        public float ResidueFraction => TotalCells > 0 ? ResidueCells / (float)TotalCells : 0f;
        public bool IsSpotless => DirtyCells == 0 && ResidueCells == 0;
    }

    /// <summary>
    /// Règles du scan de saleté, partagées par le résumé chiffré et le rendu (mêmes seuils transmis au shader) :
    /// une cellule est signalée « sale » exactement quand elle n'est pas comptée propre par la grille.
    /// </summary>
    public static class DirtScan
    {
        /// <summary>Seuil de saleté : identique au seuil « cellule propre » de la grille.</summary>
        public const float DirtThreshold = RugCleaningGrid.CleanCellThreshold;

        /// <summary>Résidus de produit au-delà desquels une cellule propre est signalée « à rincer ».</summary>
        public const float DefaultResidueThreshold = 0.15f;

        public static bool IsDirty(in RugCell cell) => cell.Dirt >= DirtThreshold;

        public static bool HasResidue(in RugCell cell, float residueThreshold) => !IsDirty(cell) && cell.Residue >= residueThreshold;

        public static DirtScanResult Analyze(RugCleaningGrid grid, float residueThreshold = DefaultResidueThreshold)
        {
            if (grid == null) return default;
            int dirty = 0, residue = 0;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                if (IsDirty(cell)) dirty++;
                else if (cell.Residue >= residueThreshold) residue++;
            }
            return new DirtScanResult(dirty, residue, grid.CellCount);
        }

        /// <summary>Texte court pour le HUD.</summary>
        public static string Describe(in DirtScanResult result)
        {
            if (result.IsSpotless) return "Scan : aucune saleté détectée";
            string text = $"Scan : {Mathf.CeilToInt(result.DirtyFraction * 100f)} % de la surface encore sale";
            if (result.ResidueCells > 0) text += $" · {Mathf.CeilToInt(result.ResidueFraction * 100f)} % de résidus à rincer";
            return text;
        }
    }
}
