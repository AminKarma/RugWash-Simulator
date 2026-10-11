using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Paramètres d'un type de saleté (réactions aux outils, couleur).</summary>
    [CreateAssetMenu(menuName = "RugWash/Type de saleté", fileName = "Dirt_")]
    public sealed class DirtTypeDefinition : ScriptableObject
    {
        public DirtType type = DirtType.Dust;
        public string displayName = "Poussière";
        public Color color = new Color(0.62f, 0.58f, 0.5f);
        public DirtResponse response = new DirtResponse(1f, 0.35f, 0.45f, 0.45f, 0.6f);
    }
}
