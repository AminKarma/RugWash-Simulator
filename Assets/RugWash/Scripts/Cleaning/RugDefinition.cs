using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    public enum RugPileType
    {
        ShortPile,
        Oriental,
        LongPile,
        /// <summary>Soie nouée main : tapis de luxe, fibres délicates et brillantes.</summary>
        Silk,
    }

    /// <summary>Nature d'une surface nettoyable : tapis à traiter, plan de travail de l'aire de lavage, sol de l'atelier.</summary>
    public enum SurfaceKind
    {
        Rug,
        WorkSurface,
        Floor,
    }

    /// <summary>
    /// Définition d'une surface nettoyable (apparence, dimensions, saleté par défaut). Sert aux tapis, et aussi au plan
    /// de travail et au sol de l'atelier, qui réutilisent la même grille, les mêmes outils et le même rendu.
    /// </summary>
    [CreateAssetMenu(menuName = "RugWash/Tapis", fileName = "Rug_")]
    public sealed class RugDefinition : ScriptableObject
    {
        public string id = "rug";
        public string displayName = "Tapis";
        [TextArea] public string description;
        public RugPileType pileType;
        [Tooltip("Tapis (commandes), plan de travail ou sol de l'atelier.")]
        public SurfaceKind surfaceKind = SurfaceKind.Rug;
        [Tooltip("Humidité évaporée par seconde (0 = ne sèche pas seule, cas des tapis).")]
        [Range(0f, 0.2f)] public float dryingPerSecond;

        [Header("Matière")]
        [Tooltip("Fragilité des fibres (0 = robuste, 1 = très délicate) : le brossage à sec et les produits agressifs les usent.")]
        [Range(0f, 1f)] public float fragility;
        [Tooltip("Éclat de la matière sèche (0 = valeur du matériau ; la soie brille).")]
        [Range(0f, 1f)] public float sheen;

        [Header("Dimensions")]
        public Vector2 sizeMeters = new Vector2(3f, 2f);
        [Tooltip("Épaisseur du tapis (hauteur des poils comprise).")]
        [Range(0.004f, 0.06f)] public float thickness = 0.012f;
        [Tooltip("Résolution de la grille de nettoyage (cellules par mètre).")]
        [Range(16, 128)] public int cellsPerMeter = 64;

        [Header("Apparence")]
        public Texture2D patternTexture;
        public Texture2D fiberNormal;
        [Tooltip("Répétitions de la texture de fibres par mètre.")]
        public float fiberTilesPerMeter = 6f;
        [Range(0f, 2f)] public float fiberNormalStrength = 1f;
        public bool hasFringe;
        public Color fringeColor = new Color(0.9f, 0.86f, 0.75f);

        [Header("Saleté par défaut (remplacée par la commande en phase 3)")]
        public int dirtSeed = 1;
        public List<DirtLayer> defaultDirt = new List<DirtLayer>();

        /// <summary>Catégorie affichée (RugDex, comptoir), issue du type de velours réel du tapis.</summary>
        public static string CategoryLabel(RugPileType pile)
        {
            switch (pile)
            {
                case RugPileType.ShortPile: return "Poils courts";
                case RugPileType.Oriental: return "Oriental noué";
                case RugPileType.LongPile: return "Poils longs";
                case RugPileType.Silk: return "Soie (luxe)";
                default: return pile.ToString();
            }
        }

        /// <summary>Fragilité en mots, à partir de la valeur réelle.</summary>
        public static string FragilityLabel(float fragility) =>
            fragility <= 0f ? "robuste" : fragility < 0.4f ? "faible" : fragility < 0.7f ? "moyenne" : "élevée";

        public Vector2Int GridResolution => new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(sizeMeters.x * cellsPerMeter), 8, 512),
            Mathf.Clamp(Mathf.RoundToInt(sizeMeters.y * cellsPerMeter), 8, 512));
    }
}
