using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Regroupe les définitions de saleté utilisées par le jeu.</summary>
    [CreateAssetMenu(menuName = "RugWash/Bibliothèque de saletés", fileName = "DirtTypeLibrary")]
    public sealed class DirtTypeLibrary : ScriptableObject
    {
        public List<DirtTypeDefinition> definitions = new List<DirtTypeDefinition>();

        /// <summary>Construit la table : valeurs de référence, surchargées par chaque définition présente.</summary>
        public DirtTypeTable BuildTable()
        {
            var table = new DirtTypeTable();
            foreach (var definition in definitions)
                if (definition != null && definition.type != DirtType.None)
                {
                    table.Set(definition.type, definition.response, definition.color);
                    table.SetBehaviour(definition.type, definition.adhesion, definition.productSensitivity);
                }
            return table;
        }
    }
}
