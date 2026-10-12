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
        /// <summary>Multiplicateur sur la graisse et les taches incrustées (brosse de détail) ; 0 ou 1 = aucun.</summary>
        public float StubbornBonus;
        /// <summary>Mousse « montée » par seconde en brossant une zone savonneuse (0 = aucune).</summary>
        public float LatherPerSecond;
        /// <summary>Produits spécialisés présents dans la mousse (null = détergent standard partout).</summary>
        public ProductTable Products;

        /// <summary>Part de l'effort perdue sur une tache d'adhérence maximale.</summary>
        public const float AdhesionResistance = 0.75f;

        /// <summary>Statistique de sortie : saleté détachée pendant cette application.</summary>
        public float Loosened;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = Power * DeltaTime * weight;
            if (k <= 0f) return;

            // Brosser une zone savonneuse fait monter la mousse (elle se charge ensuite de la saleté détachée).
            if (LatherPerSecond > 0f && cell.Foam > 0.02f)
                cell.Foam += LatherPerSecond * DeltaTime * weight * (1f - cell.Foam) * Mathf.Clamp01(cell.Foam * 4f);

            if (cell.Stain > 0f)
            {
                bool stubborn = cell.StainType == DirtType.Grease || cell.StainType == DirtType.Embedded;
                float bonus = stubborn && StubbornBonus > 0f ? StubbornBonus : 1f;
                float grip = 1f - AdhesionResistance * cell.Adhesion;
                float response = ResponseFor(Table.GetResponse(cell.StainType), cell, ProductBoost(cell, cell.StainType));
                float amount = Mathf.Min(cell.Stain, k * bonus * grip * response);
                cell.Stain -= amount;
                cell.Loosened += amount;
                Loosened += amount;
            }

            if (cell.Dust > 0f)
            {
                float amount = Mathf.Min(cell.Dust, k * ResponseFor(Table.GetResponse(cell.DustType), cell, ProductBoost(cell, cell.DustType)));
                cell.Dust -= amount;
                cell.Loosened += amount;
                if (cell.StainType == DirtType.None) cell.StainType = cell.DustType;
                Loosened += amount;
            }

            // Tapis fragile : brosser à sec use les fibres (deux fois plus sur une zone déjà propre).
            if (FiberWearEnabled && cell.Moisture < 0.2f && cell.Foam < 0.05f)
                cell.Wear += WearPerSecond * DeltaTime * weight * (cell.Dirt < RugCleaningGrid.CleanCellThreshold ? 1f : 0.5f);
        }

        float ProductBoost(in RugCell cell, DirtType type) =>
            Products != null && cell.Foam > 0f && cell.Product != ProductTable.Standard ? Products.TargetBoost(cell.Product, type) : 1f;

        /// <summary>Réponse au brossage selon l'état des fibres : la mousse prime sur l'humidité, l'humidité sur le sec.</summary>
        public static float ResponseFor(DirtResponse r, in RugCell cell, float productBoost = 1f)
        {
            float wet = Mathf.Lerp(r.brushDry, Mathf.Max(r.brushDry, r.brushWet), Mathf.Clamp01(cell.Moisture * 2.5f));
            float soapy = Mathf.Lerp(wet, Mathf.Max(wet, r.brushSoap * productBoost), Mathf.Clamp01(cell.Foam * 3f));
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

            float stain = Mathf.Min(cell.Stain, k * 0.5f * Table.GetResponse(cell.StainType).rinse * (1f - BrushOperation.AdhesionResistance * cell.Adhesion));
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
        /// <summary>Résidus de produit déposés par unité de mousse (0 = valeur de référence 0,8 du détergent pur).</summary>
        public float ResiduePerFoam;
        /// <summary>Produit spécialisé ajouté au détergent (0 = standard).</summary>
        public byte Product;

        public float FoamAdded;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = DeltaTime * weight;
            if (k <= 0f) return;
            float foam = Mathf.Min(1f - cell.Foam, FoamPerSecond * k);
            // Le produit de la nouvelle mousse l'emporte dès qu'elle domine celle déjà présente.
            if (Product != cell.Product && (cell.Foam <= 0.01f || foam >= cell.Foam * 0.5f)) cell.Product = Product;
            cell.Foam += foam;
            cell.Residue += foam * (ResiduePerFoam > 0f ? ResiduePerFoam : 0.8f);
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

    /// <summary>
    /// Balayage : pousse hors de la surface les débris secs (poils, poussière) selon leur réponse au balayage,
    /// et la saleté détachée sèche. Sur un tapis, l'humidité colle les débris aux fibres (efficacité réduite)
    /// et les taches fixées ne bougent pas. Sur un sol dur, le balai pousse aussi l'eau sale et la mousse.
    /// </summary>
    public struct SweepOperation : ICellOperation
    {
        public DirtTypeTable Table;
        public float Power;
        public float DeltaTime;
        /// <summary>Surface dure (plan de travail, sol) : l'eau, la boue et la mousse sont poussées aussi.</summary>
        public bool HardFloor;

        public float Removed;
        public float LiquidRemoved;
        public int CellsCleaned;

        public void Apply(ref RugCell cell, float weight)
        {
            float k = Power * DeltaTime * weight;
            if (k <= 0f) return;
            bool wasDirty = cell.Dirt >= RugCleaningGrid.CleanCellThreshold;

            float dryness = HardFloor ? 1f : 1f - Mathf.Clamp01(cell.Moisture * 3f);
            float dust = Mathf.Min(cell.Dust, k * Table.GetResponse(cell.DustType).sweep * dryness);
            cell.Dust -= dust;

            float loose = Mathf.Min(cell.Loosened, k * (HardFloor ? 1f : 0.4f * dryness));
            cell.Loosened -= loose;
            Removed += dust + loose;

            if (HardFloor)
            {
                float water = Mathf.Min(cell.Moisture, k * 1.2f);
                float foam = Mathf.Min(cell.Foam, k * 1.2f);
                cell.Moisture -= water;
                cell.Foam -= foam;
                LiquidRemoved += water + foam;
            }
            if (wasDirty && cell.Dirt < RugCleaningGrid.CleanCellThreshold) CellsCleaned++;
        }
    }

    /// <summary>
    /// Dépôt sur une surface : ruissellement depuis le tapis (eau sale, mousse), débris projetés, empreintes.
    /// Les quantités sont données pour un poids de 1 et réparties selon l'atténuation de la zone.
    /// </summary>
    public struct DepositOperation : ICellOperation
    {
        /// <summary>Type (couleur) de la saleté transportée.</summary>
        public DirtType Type;
        public float Mud;
        public float Debris;
        public float Foam;
        public float Water;

        public void Apply(ref RugCell cell, float weight)
        {
            if (weight <= 0f) return;
            if (Mud > 0f)
            {
                float amount = Mud * weight;
                if (cell.StainType == DirtType.None || amount > cell.Loosened) cell.StainType = Type != DirtType.None ? Type : DirtType.Soil;
                cell.Loosened += amount;
            }
            if (Debris > 0f)
            {
                if (cell.DustType == DirtType.None) cell.DustType = DirtType.Dust;
                cell.Dust += Debris * weight;
            }
            if (Foam > 0f) cell.Foam = Mathf.Min(1f, cell.Foam + Foam * weight);
            if (Water > 0f) cell.Moisture += Water * weight;
        }
    }

    /// <summary>
    /// Évolution lente d'une surface, appliquée à intervalle régulier : la mousse « travaille » (détache peu à peu
    /// les taches qui réagissent au savon), puis retombe en eau ; l'humidité s'évapore sur les surfaces qui sèchent
    /// (plan de travail, sol). Aucun effet sur une cellule sans mousse ni humidité.
    /// </summary>
    public struct SurfaceAgingOperation : ICellOperation
    {
        public DirtTypeTable Table;
        public float DeltaTime;
        /// <summary>Fraction de la mousse qui retombe par seconde.</summary>
        public float FoamDecayPerSecond;
        /// <summary>Retombée minimale (unités par seconde) : la mousse finit toujours par disparaître.</summary>
        public float FoamDecayFloor;
        /// <summary>Saleté fixée détachée par seconde sous une mousse pleine, à réponse au savon nominale.</summary>
        public float FoamDwellPower;
        /// <summary>Humidité produite par unité de mousse retombée.</summary>
        public float FoamToMoisture;
        /// <summary>Humidité évaporée par seconde (0 = la surface ne sèche pas seule).</summary>
        public float DryingPerSecond;
        /// <summary>Produits présents dans la mousse (null = détergent standard).</summary>
        public ProductTable Products;
        /// <summary>Fragilité du tapis (0..1) : un produit agressif laissé en place use alors les fibres.</summary>
        public float Fragility;
        /// <summary>Usure par seconde sous une mousse pleine d'un produit d'agressivité 1 sur un tapis de fragilité 1.</summary>
        public const float HarshWearPerSecond = 0.02f;

        public float Loosened;
        public float FoamLost;

        public void Apply(ref RugCell cell, float weight)
        {
            if (DeltaTime <= 0f) return;
            if (cell.Foam > 0f)
            {
                if (cell.Stain > 0f && FoamDwellPower > 0f && Table != null)
                {
                    float reactivity = Table.GetResponse(cell.StainType).brushSoap;
                    if (Products != null) reactivity *= Products.DwellBoost(cell.Product, cell.StainType);
                    reactivity *= 1f - 0.5f * cell.Adhesion;
                    float amount = Mathf.Min(cell.Stain, FoamDwellPower * cell.Foam * reactivity * DeltaTime);
                    cell.Stain -= amount;
                    cell.Loosened += amount;
                    Loosened += amount;
                }
                if (Products != null && Fragility > 0f && cell.Product != ProductTable.Standard)
                    cell.Wear += Products.Harshness(cell.Product) * Fragility * cell.Foam * HarshWearPerSecond * DeltaTime;
                float lost = Mathf.Min(cell.Foam, (cell.Foam * FoamDecayPerSecond + FoamDecayFloor) * DeltaTime);
                cell.Foam -= lost;
                cell.Moisture += lost * FoamToMoisture;
                FoamLost += lost;
            }
            if (DryingPerSecond > 0f && cell.Moisture > 0f)
                cell.Moisture -= Mathf.Min(cell.Moisture, DryingPerSecond * DeltaTime);
        }
    }
}
