using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Paramètres d'un type de saleté : réactions aux outils (compatibilité, difficulté), couleur, adhérence par défaut
    /// (résistance des taches fixées, modulable par couche) et sensibilité aux produits spécialisés.
    /// </summary>
    [CreateAssetMenu(menuName = "RugWash/Type de saleté", fileName = "Dirt_")]
    public sealed class DirtTypeDefinition : ScriptableObject
    {
        public DirtType type = DirtType.Dust;
        public string displayName = "Poussière";
        public Color color = new Color(0.62f, 0.58f, 0.5f);
        public DirtResponse response = new DirtResponse(1f, 0.35f, 0.45f, 0.45f, 0.6f);
        [Tooltip("Adhérence par défaut des taches de ce type (0 = se détache facilement, 1 = très incrustée).")]
        [Range(0f, 1f)] public float adhesion;
        [Tooltip("Sensibilité aux produits spécialisés qui ciblent ce type (1 = nominale, 0 = insensible).")]
        [Range(0f, 2f)] public float productSensitivity = 1f;
    }
}
