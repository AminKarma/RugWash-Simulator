using System.Collections.Generic;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.RugDex
{
    /// <summary>
    /// Liste des tapis présentés dans le RugDex, dans l'ordre de numérotation. Ajouter un tapis = l'ajouter ici :
    /// l'interface se construit à partir de cette liste et des données réelles de chaque <see cref="RugDefinition"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "RugWash/Catalogue du RugDex", fileName = "RugCatalog")]
    public sealed class RugCatalog : ScriptableObject
    {
        public List<RugDefinition> rugs = new List<RugDefinition>();
    }
}
