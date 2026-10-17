using System;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// État de nettoyage d'une cellule du tapis. Toutes les quantités sont normalisées [0..1].
    /// Ces données sont indépendantes du rendu : le visuel est dérivé d'elles, jamais l'inverse.
    /// </summary>
    [Serializable]
    public struct RugCell
    {
        /// <summary>Poussière et débris secs non fixés (retirés par aspiration, brossage, rinçage).</summary>
        public float Dust;
        /// <summary>Saleté fixée dans les fibres (taches, boue séchée, graisse…). Doit être détachée par brossage.</summary>
        public float Stain;
        /// <summary>Saleté détachée, en suspension dans les fibres : doit être rincée.</summary>
        public float Loosened;
        /// <summary>Mousse de détergent visible.</summary>
        public float Foam;
        /// <summary>Humidité des fibres.</summary>
        public float Moisture;
        /// <summary>Résidus de produit restés dans les fibres.</summary>
        public float Residue;
        /// <summary>Usure des fibres : tapis fragiles brossés à sec ou traités avec un produit agressif.</summary>
        public float Wear;
        /// <summary>Adhérence de la saleté fixée (0..1) : ralentit brossage, rinçage et action de la mousse.</summary>
        public float Adhesion;
        /// <summary>Produit présent dans la mousse (0 = détergent standard, sinon indice dans la bibliothèque de produits).</summary>
        public byte Product;

        public DirtType DustType;
        public DirtType StainType;

        /// <summary>Saleté totale restante dans la cellule.</summary>
        public float Dirt => Dust + Stain + Loosened;
    }

    /// <summary>Résumé chiffré de l'état d'un tapis, calculé à partir de la grille.</summary>
    public readonly struct RugCleaningSummary
    {
        public readonly float Cleanliness01;
        public readonly float CleanCellRatio01;
        public readonly float AverageMoisture01;
        public readonly float AverageFoam01;
        public readonly float AverageResidue01;
        public readonly float AverageWear01;
        public readonly float RemainingDirt;
        public readonly float InitialDirt;

        public RugCleaningSummary(float cleanliness, float cleanCells, float moisture, float foam, float residue,
            float wear, float remainingDirt, float initialDirt)
        {
            Cleanliness01 = cleanliness;
            CleanCellRatio01 = cleanCells;
            AverageMoisture01 = moisture;
            AverageFoam01 = foam;
            AverageResidue01 = residue;
            AverageWear01 = wear;
            RemainingDirt = remainingDirt;
            InitialDirt = initialDirt;
        }
    }

    /// <summary>Table de réponses des types de saleté, utilisée par les opérations de nettoyage.</summary>
    public sealed class DirtTypeTable
    {
        readonly DirtResponse[] responses = new DirtResponse[DirtTypes.Count];
        readonly Color[] colors = new Color[DirtTypes.Count];
        readonly float[] adhesion = new float[DirtTypes.Count];
        readonly float[] sensitivity = new float[DirtTypes.Count];

        public DirtTypeTable()
        {
            for (int i = 0; i < DirtTypes.Count; i++)
            {
                responses[i] = DefaultResponse((DirtType)i);
                colors[i] = DefaultColor((DirtType)i);
                adhesion[i] = DefaultAdhesion((DirtType)i);
                sensitivity[i] = 1f;
            }
        }

        public DirtResponse GetResponse(DirtType type) => responses[(int)type];
        public Color GetColor(DirtType type) => colors[(int)type];
        /// <summary>Adhérence par défaut des taches de ce type.</summary>
        public float GetAdhesion(DirtType type) => adhesion[(int)type];
        /// <summary>Sensibilité aux produits spécialisés qui ciblent ce type.</summary>
        public float GetProductSensitivity(DirtType type) => sensitivity[(int)type];

        public void SetBehaviour(DirtType type, float defaultAdhesion, float productSensitivity)
        {
            adhesion[(int)type] = Mathf.Clamp01(defaultAdhesion);
            sensitivity[(int)type] = Mathf.Clamp(productSensitivity, 0f, 2f);
        }

        /// <summary>Adhérence de référence : les taches incrustées et la graisse tiennent plus que la boue humide.</summary>
        public static float DefaultAdhesion(DirtType type)
        {
            switch (type)
            {
                case DirtType.Embedded: return 0.5f;
                case DirtType.Grime: return 0.45f;
                case DirtType.Grease: return 0.25f;
                case DirtType.Drink: return 0.15f;
                case DirtType.DriedMud: return 0.2f;
                case DirtType.FakeBlood: return 0.1f;
                case DirtType.Urine: return 0.05f;
                default: return 0f;
            }
        }

        public void Set(DirtType type, DirtResponse response, Color color)
        {
            responses[(int)type] = response;
            colors[(int)type] = color;
        }

        /// <summary>Valeurs de référence (documentées dans le README) utilisées si aucune définition n'est fournie.</summary>
        public static DirtResponse DefaultResponse(DirtType type)
        {
            switch (type)
            {
                case DirtType.Dust: return new DirtResponse(1.00f, 0.35f, 0.45f, 0.45f, 0.60f, 0.55f);
                case DirtType.HairDebris: return new DirtResponse(0.80f, 0.50f, 0.30f, 0.30f, 0.30f, 1.20f);
                case DirtType.Mud: return new DirtResponse(0.15f, 0.30f, 0.90f, 1.00f, 0.25f, 0.10f);
                case DirtType.Soil: return new DirtResponse(0.30f, 0.40f, 0.70f, 0.80f, 0.20f, 0.30f);
                case DirtType.Drink: return new DirtResponse(0.00f, 0.10f, 0.60f, 1.00f, 0.35f, 0.00f);
                case DirtType.Grease: return new DirtResponse(0.00f, 0.05f, 0.15f, 1.00f, 0.02f, 0.00f);
                case DirtType.Embedded: return new DirtResponse(0.00f, 0.05f, 0.20f, 0.45f, 0.00f, 0.00f);
                case DirtType.DustClumps: return new DirtResponse(0.55f, 0.50f, 0.40f, 0.40f, 0.35f, 0.90f);
                case DirtType.DriedMud: return new DirtResponse(0.10f, 0.90f, 0.50f, 0.60f, 0.05f, 0.20f);
                case DirtType.PetHair: return new DirtResponse(0.45f, 0.90f, 0.40f, 0.40f, 0.15f, 1.30f);
                case DirtType.Lint: return new DirtResponse(1.30f, 0.30f, 0.30f, 0.30f, 0.30f, 0.80f);
                case DirtType.Urine: return new DirtResponse(0.00f, 0.10f, 0.80f, 1.10f, 0.60f, 0.00f);
                case DirtType.FakeBlood: return new DirtResponse(0.00f, 0.05f, 0.55f, 0.95f, 0.30f, 0.00f);
                case DirtType.Grime: return new DirtResponse(0.00f, 0.08f, 0.30f, 0.70f, 0.10f, 0.00f);
                default: return new DirtResponse(0f, 0f, 0f, 0f, 0f);
            }
        }

        public static Color DefaultColor(DirtType type)
        {
            switch (type)
            {
                case DirtType.Dust: return new Color(0.62f, 0.58f, 0.50f);
                case DirtType.HairDebris: return new Color(0.45f, 0.40f, 0.35f);
                case DirtType.Mud: return new Color(0.30f, 0.21f, 0.13f);
                case DirtType.Soil: return new Color(0.40f, 0.30f, 0.20f);
                case DirtType.Drink: return new Color(0.36f, 0.13f, 0.10f);
                case DirtType.Grease: return new Color(0.36f, 0.32f, 0.17f);
                case DirtType.Embedded: return new Color(0.20f, 0.16f, 0.12f);
                case DirtType.DustClumps: return new Color(0.52f, 0.49f, 0.44f);
                case DirtType.DriedMud: return new Color(0.47f, 0.37f, 0.25f);
                case DirtType.PetHair: return new Color(0.58f, 0.47f, 0.36f);
                case DirtType.Lint: return new Color(0.78f, 0.76f, 0.73f);
                case DirtType.Urine: return new Color(0.80f, 0.68f, 0.16f);
                case DirtType.FakeBlood: return new Color(0.66f, 0.04f, 0.10f);
                case DirtType.Grime: return new Color(0.06f, 0.055f, 0.05f);
                default: return new Color(0.35f, 0.27f, 0.18f);
            }
        }
    }
}
