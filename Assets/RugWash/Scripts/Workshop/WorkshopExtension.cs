using System;
using RugWash.Orders;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>
    /// Agrandissement de l'atelier (grande mise à jour) : une annexe derrière le mur droit, ouverte par l'achat. Effets
    /// concrets : une deuxième zone de lavage (deux commandes, deux tapis en même temps) et la place de la machine à laver.
    /// Avant l'achat, une cloison ferme le passage et l'annexe n'est pas rendue. En ligne, l'atelier affiché est celui de
    /// l'hôte (<see cref="Facilities"/> diffusé aux invités).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopExtension : MonoBehaviour
    {
        /// <summary>Identifiant d'équipement de l'agrandissement (amélioration « équipement d'atelier »).</summary>
        public const string ExtensionId = "extension";

        [Tooltip("Zone de lavage de l'annexe.")]
        [SerializeField] RugBay bay;
        [Tooltip("Cloison qui ferme le passage tant que l'agrandissement n'est pas acheté (avec son collider).")]
        [SerializeField] GameObject closedWall;
        [Tooltip("Annexe : murs, sol, plafond, lumières et aire de lavage (rendus seulement après l'achat).")]
        [SerializeField] GameObject annex;
        [SerializeField] WashingMachine washer;

        bool unlocked, washerOwned, fastProgram;
        bool applied;

        public bool IsUnlocked => unlocked;
        public RugBay Bay => bay;
        public WashingMachine Washer => washer;
        /// <summary>Agrandissement (bit 0), machine à laver (bit 1), programme rapide (bit 2).</summary>
        public byte Facilities => (byte)((unlocked ? 1 : 0) | (washerOwned ? 2 : 0) | (fastProgram ? 4 : 0));

        public event Action Changed;

        public bool IsConfigured(out string missing)
        {
            missing = bay == null ? nameof(bay) : closedWall == null ? nameof(closedWall) : annex == null ? nameof(annex) : washer == null ? nameof(washer) : null;
            return missing == null;
        }

        void Start()
        {
            if (!applied) Apply(false, false, false);
        }

        /// <summary>Applique les achats (solo, hôte) : la machine exige l'agrandissement, le programme rapide la machine.</summary>
        public void Apply(bool extension, bool washerBought, bool fastBought)
        {
            bool first = !applied;
            applied = true;
            bool washerReady = extension && washerBought;
            bool fastReady = washerReady && fastBought;
            if (!first && extension == unlocked && washerReady == washerOwned && fastReady == fastProgram) return;
            unlocked = extension;
            washerOwned = washerReady;
            fastProgram = fastReady;
            if (closedWall != null) closedWall.SetActive(!unlocked);
            if (annex != null) annex.SetActive(unlocked);
            if (bay != null) bay.SetUnlocked(unlocked);
            if (washer != null) washer.SetOwned(washerOwned, fastProgram);
            Changed?.Invoke();
        }

        /// <summary>Invité : atelier de l'hôte.</summary>
        public void ApplyFacilities(byte bits) => Apply((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
    }
}
