using System;
using UnityEngine;

namespace RugWash.Economy
{
    /// <summary>
    /// Réputation de l'atelier : gagnée selon la note des commandes, elle débloque les commandes
    /// plus exigeantes (OrderDefinition.requiredReputation). Jamais négative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReputationManager : MonoBehaviour
    {
        public int Points { get; private set; }

        public event Action<int> Changed;

        public void Add(int delta)
        {
            int next = Mathf.Max(0, Points + delta);
            if (next == Points) return;
            Points = next;
            Changed?.Invoke(Points);
        }

        public void Restore(int points)
        {
            Points = Mathf.Max(0, points);
            Changed?.Invoke(Points);
        }
    }
}
