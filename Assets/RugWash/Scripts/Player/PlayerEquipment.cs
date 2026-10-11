using RugWash.Cleaning;
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

        public ResourceTank Water => water;
        public ResourceTank Soap => soap;
        public ResourceTank Recovery => recovery;

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
