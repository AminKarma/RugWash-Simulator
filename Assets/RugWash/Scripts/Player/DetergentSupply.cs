using System;
using RugWash.Core;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Stock de détergent du joueur, en bidons. Règle (v0.1.2) : un bidon de 40 L est un <b>stock de 40 litres consommé
    /// progressivement</b> — chaque passage au poste détergent remplit la réserve emportée (4 L, 6 L avec l'amélioration)
    /// avec ce qui manque, dans la limite du stock (remplissage partiel possible). Une seule unité partout : le litre.
    /// Le stock fait partie de la progression personnelle (sauvegardé, validé par l'hôte en ligne).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DetergentSupply : MonoBehaviour
    {
        [Tooltip("Contenance d'un bidon acheté (L).")]
        [SerializeField, Min(1f)] float canLiters = 40f;
        [Tooltip("Prix d'un bidon.")]
        [SerializeField, Min(0)] int canPrice = 45;
        [Tooltip("Stock maximal entreposé (L).")]
        [SerializeField, Min(1f)] float maxStock = 160f;

        float stock = Save.SaveMigration.StarterDetergentStock;

        public float Stock => stock;
        public float CanLiters => canLiters;
        public int CanPrice => canPrice;
        public float MaxStock => maxStock;

        PlayerEquipment equipment;

        /// <summary>Contenance de la réserve emportée que remplit ce stock (4 L, 6 L avec l'amélioration).</summary>
        public float ReserveCapacity
        {
            get
            {
                if (equipment == null) equipment = GetComponent<PlayerEquipment>();
                return equipment != null && equipment.Soap != null ? equipment.Soap.Capacity : 4f;
            }
        }

        /// <summary>
        /// Multijoueur, côté invité : achats de bidons et remplissages au poste sont demandés à l'hôte, qui débite SA carrière
        /// tenue pour ce joueur. Null : solo ou hôte.
        /// </summary>
        public IWorkshopRequests Remote { get; set; }

        public event Action Changed;

        public void Restore(float liters)
        {
            stock = Mathf.Clamp(float.IsNaN(liters) ? 0f : liters, 0f, maxStock);
            Changed?.Invoke();
        }

        /// <summary>Prélève jusqu'à <paramref name="liters"/> ; retourne la quantité réellement prélevée.</summary>
        public float Take(float liters)
        {
            float taken = Mathf.Clamp(liters, 0f, stock);
            if (taken <= 0f) return 0f;
            stock -= taken;
            if (stock < 1e-4f) stock = 0f;
            Changed?.Invoke();
            return taken;
        }

        /// <summary>Ajoute un bidon (après paiement) ; faux si le stock maximal serait dépassé.</summary>
        public bool AddCan()
        {
            if (stock + canLiters > maxStock + 1e-4f) return false;
            stock += canLiters;
            Changed?.Invoke();
            return true;
        }
    }
}
