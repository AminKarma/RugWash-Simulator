using System;
using System.Collections.Generic;
using RugWash.Core;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>Objet de l'atelier avec lequel le joueur peut interagir (touche E).</summary>
    public interface IInteractable
    {
        /// <summary>Texte de l'action, ex. « Remplir le réservoir d'eau ».</summary>
        string GetPrompt(PlayerInteractor interactor);
        bool CanInteract(PlayerInteractor interactor);
        /// <summary>Exécute l'action et retourne un message de confirmation (ou null).</summary>
        string Interact(PlayerInteractor interactor);
    }

    /// <summary>Détecte l'objet interactif visé et déclenche l'interaction.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerEquipment equipment;
        [SerializeField] GameStateManager gameState;
        [SerializeField] float reach = 2.6f;
        [SerializeField] LayerMask mask = ~0;

        readonly Dictionary<Collider, IInteractable> cache = new Dictionary<Collider, IInteractable>();

        public PlayerEquipment Equipment => equipment;
        public IInteractable Current { get; private set; }

        /// <summary>Message après une interaction réussie (affiché par le HUD).</summary>
        public event Action<string> Interacted;

        public bool IsConfigured(out string missing)
        {
            missing = playerCamera == null ? nameof(playerCamera)
                : input == null ? nameof(input)
                : equipment == null ? nameof(equipment)
                : gameState == null ? nameof(gameState) : null;
            return missing == null;
        }

        void Awake()
        {
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] PlayerInteractor : référence manquante '{missing}'.", this);
                enabled = false;
            }
        }

        void Update()
        {
            if (!gameState.IsGameplayActive)
            {
                Current = null;
                return;
            }

            Current = FindTarget();
            if (Current != null && input.InteractPressed && Current.CanInteract(this))
            {
                string message = Current.Interact(this);
                if (!string.IsNullOrEmpty(message)) Interacted?.Invoke(message);
            }
        }

        IInteractable FindTarget()
        {
            var ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Collide)) return null;
            if (!cache.TryGetValue(hit.collider, out var interactable))
            {
                interactable = hit.collider.GetComponentInParent<IInteractable>();
                cache[hit.collider] = interactable;
            }
            return interactable;
        }
    }
}
