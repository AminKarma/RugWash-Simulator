using RugWash.Cleaning;
using RugWash.Core;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>Consommables portés par le joueur. Données propres au joueur, séparées de la scène.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : MonoBehaviour, IToolResources
    {
        [SerializeField] ResourceTank water = new ResourceTank("Eau", "L", 40f, 40f);
        [SerializeField] ResourceTank soap = new ResourceTank("Détergent", "L", 4f, 4f);
        [SerializeField] ResourceTank recovery = new ResourceTank("Cuve de récupération", "L", 12f, 0f);
        [Tooltip("Produits spécialisés achetés (optionnel).")]
        [SerializeField] ProductInventory productInventory;
        [Tooltip("Libellés des touches dans les indications des outils (optionnel).")]
        [SerializeField] PlayerInputReader input;
        [Tooltip("Stock de détergent en bidons (remplit la réserve au poste détergent).")]
        [SerializeField] DetergentSupply detergent;

        public ResourceTank Water => water;
        public ResourceTank Soap => soap;
        public ResourceTank Recovery => recovery;
        public ProductInventory Inventory => productInventory;
        public IProductSupply Products => productInventory;
        public IInputPrompts Prompts => input;
        public DetergentSupply Detergent => detergent;
        public float DetergentStock => detergent != null ? detergent.Stock : -1f;

        public ResourceTank Get(ToolResource resource)
        {
            switch (resource)
            {
                case ToolResource.Water: return water;
                case ToolResource.Soap: return soap;
                case ToolResource.Recovery: return recovery;
                default: return null;
            }
        }
    }
}
