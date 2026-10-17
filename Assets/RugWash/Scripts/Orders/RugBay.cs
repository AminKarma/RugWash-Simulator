using System;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Orders
{
    /// <summary>
    /// Zone de lavage (grande mise à jour) : un emplacement de tapis et son aire de lavage. La zone 1 est disponible dès le
    /// départ ; les suivantes sont débloquées par l'agrandissement de l'atelier. Chaque zone a sa commande, son chronomètre
    /// et sa propreté d'aire (voir <see cref="OrderManager"/>) : deux tapis peuvent être nettoyés en même temps.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RugBay : MonoBehaviour
    {
        [Tooltip("Numéro affiché (1, 2…).")]
        [SerializeField, Min(1)] int number = 1;
        [SerializeField] RugCleaningController rug;
        [SerializeField] WorkSurface workSurface;
        [Tooltip("Zone utilisable sans achat (zone 1). Sinon, débloquée par l'agrandissement de l'atelier.")]
        [SerializeField] bool unlockedByDefault = true;

        bool unlocked;
        bool initialized;

        public int Number => number;
        public RugCleaningController Rug => rug;
        public WorkSurface WorkSurface => workSurface;
        public string Label => $"Zone {number}";

        public bool IsUnlocked
        {
            get
            {
                if (!initialized) Initialize();
                return unlocked;
            }
        }

        public event Action<RugBay> UnlockedChanged;

        public bool IsConfigured(out string missing)
        {
            missing = rug == null ? nameof(rug) : workSurface == null ? nameof(workSurface) : null;
            return missing == null;
        }

        void Initialize()
        {
            initialized = true;
            unlocked = unlockedByDefault;
        }

        /// <summary>Débloque ou verrouille la zone (achat de l'agrandissement ; en ligne, atelier de l'hôte).</summary>
        public void SetUnlocked(bool value)
        {
            if (!initialized) Initialize();
            if (unlocked == value) return;
            unlocked = value;
            UnlockedChanged?.Invoke(this);
        }
    }
}
