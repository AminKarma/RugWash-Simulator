using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Brossage : détache la saleté fixée (Stain → Loosened) et met en suspension la poussière.
    /// Ne retire rien du tapis : la saleté détachée doit ensuite être rincée.
    /// L'efficacité dépend du type de saleté et de l'état des fibres (sèches, humides, savonneuses).
    /// La quantité transférée est bornée par la saleté présente : aucune progression sur une zone propre.
    /// </summary>
    public struct BrushOperation : ICellOperation
    {
        public DirtTypeTable Table;
        /// <summary>Fraction de saleté détachée par seconde à réponse nominale.</summary>
        public float Power;
        public float DeltaTime;
        public bool FiberWearEnabled;
        public float WearPerSecond;

        /// <summary>Statistique de sortie : saleté détachée pendant cette application.</summary>
        public float Loosened;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = Power * DeltaTime * weight;
            if (k <= 0f) return;

            if (cell.Stain > 0f)
            {
                float amount = Mathf.Min(cell.Stain, k * ResponseFor(Table.GetResponse(cell.StainType), cell));
                cell.Stain -= amount;
                cell.Loosened += amount;
                Loosened += amount;
            }

            if (cell.Dust > 0f)
            {
                float amount = Mathf.Min(cell.Dust, k * ResponseFor(Table.GetResponse(cell.DustType), cell));
                cell.Dust -= amount;
                cell.Loosened += amount;
                if (cell.StainType == DirtType.None) cell.StainType = cell.DustType;
                Loosened += amount;
            }

            if (FiberWearEnabled && cell.Dirt < RugCleaningGrid.CleanCellThreshold && cell.Moisture < 0.2f)
                cell.Wear += WearPerSecond * DeltaTime * weight;
        }

        /// <summary>Réponse au brossage selon l'état des fibres : la mousse prime sur l'humidité, l'humidité sur le sec.</summary>
        public static float ResponseFor(DirtResponse r, in RugCell cell)
        {
            float wet = Mathf.Lerp(r.brushDry, Mathf.Max(r.brushDry, r.brushWet), Mathf.Clamp01(cell.Moisture * 2.5f));
            float soapy = Mathf.Lerp(wet, Mathf.Max(wet, r.brushSoap), Mathf.Clamp01(cell.Foam * 3f));
            return soapy;
        }
    }

    /// <summary>
    /// Rinçage : évacue la saleté détachée, la mousse et une partie des résidus ; mouille les fibres.
    /// Retire directement une faible part des taches selon leur réponse à l'eau.
    /// </summary>
    public struct RinseOperation : ICellOperation
    {
        public DirtTypeTable Table;
        /// <summary>Fraction évacuée par seconde.</summary>
        public float Power;
        /// <summary>Humidité ajoutée par seconde.</summary>
        public float WettingPerSecond;
        public float DeltaTime;

        /// <summary>Statistique de sortie : saleté évacuée du tapis.</summary>
        public float Removed;
        /// <summary>Statistique de sortie : cellules devenues propres pendant cette application.</summary>
        public int CellsCleaned;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = Power * DeltaTime * weight;
            if (k <= 0f) return;

            bool wasDirty = cell.Dirt >= RugCleaningGrid.CleanCellThreshold;
            float loose = Mathf.Min(cell.Loosened, k);
            cell.Loosened -= loose;

            float dust = Mathf.Min(cell.Dust, k * Table.GetResponse(cell.DustType).rinse);
            cell.Dust -= dust;

            float stain = Mathf.Min(cell.Stain, k * 0.5f * Table.GetResponse(cell.StainType).rinse);
            cell.Stain -= stain;

            Removed += loose + dust + stain;
            if (wasDirty && cell.Dirt < RugCleaningGrid.CleanCellThreshold) CellsCleaned++;

            cell.Foam -= Mathf.Min(cell.Foam, k * 1.5f);
            cell.Residue -= Mathf.Min(cell.Residue, k * 0.35f);
            cell.Moisture += WettingPerSecond * DeltaTime * weight;
        }
    }

    /// <summary>Ajoute des quantités fixes (génération de saleté, tests).</summary>
    public struct AddDirtOperation : ICellOperation
    {
        public DirtType Type;
        public float Amount;

        public void Apply(ref RugCell cell, float weight)
        {
            float amount = Amount * weight;
            if (amount <= 0f || Type == DirtType.None) return;

            if (DirtTypes.IsLooseDry(Type))
            {
                if (amount > cell.Dust || cell.DustType == DirtType.None) cell.DustType = Type;
                cell.Dust += amount;
            }
            else
            {
                if (amount > cell.Stain || cell.StainType == DirtType.None) cell.StainType = Type;
                cell.Stain += amount;
            }
        }
    }

    /// <summary>
    /// Aspiration : retire la poussière et les débris secs selon leur réponse à l'aspiration.
    /// Aspire aussi la saleté déjà détachée, mais seulement sur fibres sèches (l'humidité la colle).
    /// N'agit pas sur la saleté incrustée.
    /// </summary>
    public struct VacuumOperation : ICellOperation
    {
        public DirtTypeTable Table;
        public float Power;
        public float DeltaTime;

        public float Removed;
        public int CellsCleaned;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = Power * DeltaTime * weight;
            if (k <= 0f) return;
            bool wasDirty = cell.Dirt >= RugCleaningGrid.CleanCellThreshold;

            float dust = Mathf.Min(cell.Dust, k * Table.GetResponse(cell.DustType).vacuum);
            cell.Dust -= dust;

            float dryness = 1f - Mathf.Clamp01(cell.Moisture * 4f);
            float loose = Mathf.Min(cell.Loosened, k * 0.6f * dryness);
            cell.Loosened -= loose;

            Removed += dust + loose;
            if (wasDirty && cell.Dirt < RugCleaningGrid.CleanCellThreshold) CellsCleaned++;
        }
    }

    /// <summary>
    /// Pulvérisation de détergent : ajoute de la mousse et du produit (résidus) dans les fibres,
    /// humidifie légèrement. Ne retire aucune saleté : il prépare le brossage des taches réactives au savon.
    /// </summary>
    public struct SoapOperation : ICellOperation
    {
        /// <summary>Mousse ajoutée par seconde.</summary>
        public float FoamPerSecond;
        public float WettingPerSecond;
        public float DeltaTime;

        public float FoamAdded;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = DeltaTime * weight;
            if (k <= 0f) return;
            float foam = Mathf.Min(1f - cell.Foam, FoamPerSecond * k);
            cell.Foam += foam;
            cell.Residue += foam * 0.8f;
            cell.Moisture += WettingPerSecond * k;
            FoamAdded += foam;
        }
    }

    /// <summary>
    /// Extraction : aspire l'eau des fibres. L'eau aspirée emporte, proportionnellement à l'humidité
    /// présente, une partie de la saleté détachée et des résidus de produit. Sans humidité, aucun effet.
    /// </summary>
    public struct ExtractOperation : ICellOperation
    {
        /// <summary>Fraction d'humidité retirée par seconde.</summary>
        public float Power;
        public float DeltaTime;

        /// <summary>Humidité retirée (somme normalisée sur les cellules) — convertie en litres par l'outil.</summary>
        public float MoistureRemoved;
        public float DirtRemoved;
        public float ResidueRemoved;
        public int CellsCleaned;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = Power * DeltaTime * weight;
            if (k <= 0f || cell.Moisture <= 0f) return;
            bool wasDirty = cell.Dirt >= RugCleaningGrid.CleanCellThreshold;

            float wetness = Mathf.Clamp01(cell.Moisture);
            float water = Mathf.Min(cell.Moisture, k);
            cell.Moisture -= water;
            MoistureRemoved += water;

            float loose = Mathf.Min(cell.Loosened, k * 0.5f * wetness);
            cell.Loosened -= loose;
            DirtRemoved += loose;

            float residue = Mathf.Min(cell.Residue, k * 0.6f * wetness);
            cell.Residue -= residue;
            ResidueRemoved += residue;

            cell.Foam -= Mathf.Min(cell.Foam, k * 0.8f);
            if (wasDirty && cell.Dirt < RugCleaningGrid.CleanCellThreshold) CellsCleaned++;
        }
    }
}
