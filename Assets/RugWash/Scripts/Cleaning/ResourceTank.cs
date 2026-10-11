using System;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>Réservoir de consommable (eau, produit). Ne peut jamais fournir plus que son contenu.</summary>
    [Serializable]
    public sealed class ResourceTank
    {
        [SerializeField] string displayName = "Eau";
        [SerializeField] string unit = "L";
        [SerializeField, Min(0.1f)] float capacity = 40f;
        [SerializeField, Min(0f)] float amount = 40f;

        public ResourceTank() { }

        public ResourceTank(string displayName, string unit, float capacity, float initialAmount)
        {
            this.displayName = displayName;
            this.unit = unit;
            this.capacity = Mathf.Max(0.1f, capacity);
            amount = Mathf.Clamp(initialAmount, 0f, this.capacity);
        }

        public string DisplayName => displayName;
        public string Unit => unit;
        public float Capacity => capacity;
        public float Amount => amount;
        public float Fraction01 => amount / capacity;
        public bool IsEmpty => amount <= 1e-4f;

        public event Action<ResourceTank> Changed;

        /// <summary>Retire jusqu'à <paramref name="requested"/> et retourne la quantité réellement fournie.</summary>
        public float Consume(float requested)
        {
            if (requested <= 0f || IsEmpty) return 0f;
            float provided = Mathf.Min(requested, amount);
            amount -= provided;
            if (amount < 1e-4f) amount = 0f;
            Changed?.Invoke(this);
            return provided;
        }

        /// <summary>Ajoute jusqu'à <paramref name="offered"/> dans la limite de la capacité ; retourne la quantité acceptée.</summary>
        public float Add(float offered)
        {
            if (offered <= 0f) return 0f;
            float accepted = Mathf.Min(offered, capacity - amount);
            if (accepted <= 0f) return 0f;
            amount += accepted;
            Changed?.Invoke(this);
            return accepted;
        }

        public bool IsFull => amount >= capacity - 1e-4f;

        public void Empty()
        {
            if (amount <= 0f) return;
            amount = 0f;
            Changed?.Invoke(this);
        }

        public void Refill()
        {
            if (Mathf.Approximately(amount, capacity)) return;
            amount = capacity;
            Changed?.Invoke(this);
        }

        /// <summary>Modifie la capacité (améliorations) en conservant le contenu dans les bornes.</summary>
        public void SetCapacity(float newCapacity)
        {
            capacity = Mathf.Max(0.1f, newCapacity);
            amount = Mathf.Min(amount, capacity);
            Changed?.Invoke(this);
        }
    }
}
