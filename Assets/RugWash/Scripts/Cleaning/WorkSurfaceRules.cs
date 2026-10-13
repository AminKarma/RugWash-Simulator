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

        /// <summary>
        /// Propreté de l'aire [0..1] = part de la surface non souillée. Convention : propreté = 1 − saleté
        /// (<see cref="SoiledFraction"/>) ; les deux ne sont jamais mélangées.
        /// </summary>
        public float Cleanliness01 => Mathf.Clamp01(1f - SoiledFraction);
        /// <summary>Propreté affichée, arrondie vers le bas (24,9 % s'affiche 24 % : cohérent avec un seuil de 25 %).</summary>
        public int CleanlinessPercent => Mathf.FloorToInt(Cleanliness01 * 100f + 1e-4f);
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

        /// <summary>Recontamination minimale (saleté par cellule de bordure) dès que le seuil de saleté est dépassé.</summary>
        public const float MinContamination = 0.05f;

        /// <summary>Une nouvelle commande peut démarrer : propreté de l'aire ≥ <paramref name="minCleanliness"/>.</summary>
        public static bool AllowsNewOrder(in WorkSurfaceStatus status, float minCleanliness) =>
            status.Cleanliness01 + 1e-4f >= minCleanliness;

        /// <summary>
        /// Part de l'excès de saleté au-delà du seuil [0..1] : 0 jusqu'au seuil (inclus), 1 pour une aire entièrement souillée.
        /// </summary>
        public static float DirtExcess(in WorkSurfaceStatus status, float dirtyThreshold)
        {
            if (status.SoiledFraction <= dirtyThreshold) return 0f;
            return dirtyThreshold >= 1f ? 0f : Mathf.Clamp01((status.SoiledFraction - dirtyThreshold) / (1f - dirtyThreshold));
        }

        /// <summary>Le tapis posé sur l'aire se salit : saleté de l'aire strictement supérieure au seuil.</summary>
        public static bool ContaminatesRug(in WorkSurfaceStatus status, float dirtyThreshold) => status.SoiledFraction > dirtyThreshold;

        /// <summary>
        /// Saleté déposée sur la bordure du tapis au moment où il est posé : nulle jusqu'au seuil, puis de
        /// <see cref="MinContamination"/> à <see cref="MaxContamination"/> selon l'excès.
        /// </summary>
        public static float ContaminationAmount(in WorkSurfaceStatus status, float dirtyThreshold) =>
            ContaminatesRug(status, dirtyThreshold) ? Mathf.Lerp(MinContamination, MaxContamination, DirtExcess(status, dirtyThreshold)) : 0f;

        /// <summary>Saleté ajoutée par seconde à la bordure d'un tapis resté sur une aire trop sale.</summary>
        public static float ContaminationRate(in WorkSurfaceStatus status, float dirtyThreshold, float perSecondAtFull) =>
            perSecondAtFull * DirtExcess(status, dirtyThreshold);

        /// <summary>Rectangles de la bordure recontaminée (largeur <see cref="ContaminationBand"/> de la plus petite dimension).</summary>
        public static void BorderBands(int width, int height, RectInt[] bands)
        {
            int band = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(width, height) * ContaminationBand));
            bands[0] = new RectInt(0, 0, width, band);
            bands[1] = new RectInt(0, height - band, width, band);
            bands[2] = new RectInt(0, band, band, height - 2 * band);
            bands[3] = new RectInt(width - band, band, band, height - 2 * band);
        }
    }

    /// <summary>
    /// Boue venue d'une aire de lavage trop sale, déposée sur une cellule de tapis sans jamais dépasser
    /// <see cref="Cap"/> de saleté (le tapis se salit, mais ne devient pas plus sale qu'à son arrivée).
    /// </summary>
    public struct BorderContaminationOperation : ICellOperation
    {
        public float Amount;
        public float Cap;

        public void Apply(ref RugCell cell, float weight)
        {
            float room = Cap - cell.Dirt;
            if (room <= 0f || Amount <= 0f || weight <= 0f) return;
            float added = Mathf.Min(room, Amount * weight);
            if (cell.StainType == DirtType.None || added > cell.Loosened) cell.StainType = DirtType.Soil;
            cell.Loosened += added;
        }
    }

    /// <summary>Retire toute la saleté d'une cellule (fin du tapis) ; l'eau, la mousse, les résidus et l'usure sont conservés.</summary>
    public struct ClearDirtOperation : ICellOperation
    {
        public void Apply(ref RugCell cell, float weight)
        {
            cell.Dust = 0f;
            cell.Stain = 0f;
            cell.Loosened = 0f;
            cell.DustType = DirtType.None;
            cell.StainType = DirtType.None;
        }
    }

    /// <summary>Règle de fin du tapis : il reste moins de <c>remainingFraction</c> de la saleté initiale.</summary>
    public static class RugFinishRules
    {
        public static bool IsFinished(float remainingDirt, float initialDirt, float remainingFraction) =>
            initialDirt > 1e-5f && remainingDirt < initialDirt * Mathf.Max(0f, remainingFraction);
    }
}
