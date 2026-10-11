using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    public enum RugPileType
    {
        ShortPile,
        Oriental,
        LongPile,
    }

    /// <summary>Définition d'un modèle de tapis (apparence, dimensions, saleté par défaut).</summary>
    [CreateAssetMenu(menuName = "RugWash/Tapis", fileName = "Rug_")]
    public sealed class RugDefinition : ScriptableObject
    {
        public string id = "rug";
        public string displayName = "Tapis";
        [TextArea] public string description;
        public RugPileType pileType;

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

        public Vector2Int GridResolution => new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(sizeMeters.x * cellsPerMeter), 8, 512),
            Mathf.Clamp(Mathf.RoundToInt(sizeMeters.y * cellsPerMeter), 8, 512));
    }
}
