using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>État chiffré du plan de travail (aire de lavage), calculé sur sa grille.</summary>
    public readonly struct WorkSurfaceStatus
    {
        public readonly float SoiledFraction;
        public readonly float AverageMud;
        public readonly float AverageFoam;
        public readonly float AverageWater;
        public readonly float AverageDebris;

        public WorkSurfaceStatus(float soiledFraction, float mud, float foam, float water, float debris)
        {
            SoiledFraction = soiledFraction;
            AverageMud = mud;
            AverageFoam = foam;
            AverageWater = water;
            AverageDebris = debris;
        }

        /// <summary>Propre : moins de <see cref="WorkSurfaceRules.CleanFraction"/> de la surface souillée (l'eau claire ne compte pas).</summary>
        public bool IsClean => SoiledFraction <= WorkSurfaceRules.CleanFraction;
        public int SoiledPercent => Mathf.CeilToInt(SoiledFraction * 100f);
    }

    /// <summary>
    /// Règles pures du plan de travail : ce qui le salit, quand il est propre, où ruisselle ce qui quitte le tapis,
    /// et combien un plan de travail sale recontamine le tapis suivant (testées en EditMode).
    /// </summary>
    public static class WorkSurfaceRules
    {
        /// <summary>Part maximale de surface souillée pour considérer le plan de travail propre.</summary>
        public const float CleanFraction = 0.01f;
        /// <summary>Mousse au-delà de laquelle une cellule est considérée souillée.</summary>
        public const float FoamThreshold = 0.08f;
        /// <summary>Recontamination maximale (saleté par cellule) sur la bordure du tapis suivant.</summary>
        public const float MaxContamination = 0.35f;
        /// <summary>Largeur de la bordure recontaminée, en part de la plus petite dimension du tapis.</summary>
        public const float ContaminationBand = 0.08f;

        /// <summary>Eau sale, débris ou mousse : l'eau claire seule ne salit pas (elle s'évapore).</summary>
        public static bool IsSoiled(in RugCell cell) => cell.Dirt >= RugCleaningGrid.CleanCellThreshold || cell.Foam >= FoamThreshold;

        public static WorkSurfaceStatus Evaluate(RugCleaningGrid grid)
        {
            if (grid == null || grid.CellCount == 0) return default;
            int soiled = 0;
            double mud = 0, foam = 0, water = 0, debris = 0;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid.GetCell(x, y);
                if (IsSoiled(cell)) soiled++;
                mud += cell.Loosened + cell.Stain;
                foam += cell.Foam;
                water += cell.Moisture;
                debris += cell.Dust;
            }
            float n = grid.CellCount;
            return new WorkSurfaceStatus(soiled / n, (float)(mud / n), (float)(foam / n), (float)(water / n), (float)(debris / n));
        }

        /// <summary>
        /// Point de dépôt du ruissellement, dans le repère du tapis (X, Z en mètres) : projection du point d'impact sur le
        /// bord le plus proche, décalée vers l'extérieur de <paramref name="offset"/> et le long du bord de <paramref name="along"/>.
        /// </summary>
        public static Vector2 EdgeDepositPoint(Vector2 localPoint, Vector2 halfSize, float offset, float along)
        {
            float x = Mathf.Clamp(localPoint.x, -halfSize.x, halfSize.x);
            float z = Mathf.Clamp(localPoint.y, -halfSize.y, halfSize.y);
            float toRight = halfSize.x - x, toLeft = x + halfSize.x, toTop = halfSize.y - z, toBottom = z + halfSize.y;
            float nearest = Mathf.Min(Mathf.Min(toRight, toLeft), Mathf.Min(toTop, toBottom));
            if (nearest == toRight) return new Vector2(halfSize.x + offset, Mathf.Clamp(z + along, -halfSize.y, halfSize.y));
            if (nearest == toLeft) return new Vector2(-halfSize.x - offset, Mathf.Clamp(z + along, -halfSize.y, halfSize.y));
            if (nearest == toTop) return new Vector2(Mathf.Clamp(x + along, -halfSize.x, halfSize.x), halfSize.y + offset);
            return new Vector2(Mathf.Clamp(x + along, -halfSize.x, halfSize.x), -halfSize.y - offset);
        }

        /// <summary>
        /// Quantité par cellule (au centre de la flaque) qui répartit <paramref name="amount"/> (unités × m²) sur un disque
        /// de rayon <paramref name="radius"/> dont l'atténuation moyenne vaut environ 0,5.
        /// </summary>
        public static float PerCellAmount(float amount, float radius)
        {
            float area = Mathf.PI * radius * radius * 0.5f;
            return area > 0f ? amount / area : 0f;
        }

        /// <summary>Saleté déposée sur la bordure du tapis suivant quand il est posé sur un plan de travail sale.</summary>
        public static float ContaminationAmount(in WorkSurfaceStatus status) =>
            status.IsClean ? 0f : Mathf.Clamp(status.SoiledFraction * 2.5f, 0.05f, MaxContamination);
    }
}
