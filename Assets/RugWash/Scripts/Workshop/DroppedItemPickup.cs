using System;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>
    /// Objet tombé au sol (V0.2.5) : un outil ou un gadget perdu par un joueur touché par la batte, en partie en ligne. Ce
    /// composant ne fait qu'afficher l'objet et transmettre la demande de ramassage (E) : c'est l'hôte qui décide (le premier
    /// qui le demande l'obtient ; l'objet n'existe qu'une fois, au sol ou chez un joueur).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroppedItemPickup : MonoBehaviour, IInteractable
    {
        Func<uint, string> pickup;
        string displayName = "Objet";
        int resaleValue;

        public uint DropId { get; private set; }
        public string DisplayName => displayName;

        public void Init(uint dropId, string name, int resale, Func<uint, string> onPickup)
        {
            DropId = dropId;
            displayName = string.IsNullOrEmpty(name) ? "Objet" : name;
            resaleValue = Mathf.Max(0, resale);
            pickup = onPickup;
        }

        public string GetPrompt(PlayerInteractor interactor) =>
            resaleValue > 0 ? $"Ramasser : {displayName} (revente {resaleValue} €)" : $"Ramasser : {displayName}";

        public bool CanInteract(PlayerInteractor interactor) => pickup != null;

        public string Interact(PlayerInteractor interactor) => pickup?.Invoke(DropId);
    }
}
