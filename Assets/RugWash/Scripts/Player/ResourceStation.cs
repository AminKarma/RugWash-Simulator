using RugWash.Audio;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Poste de l'atelier lié à une ressource : remplit l'eau ou le détergent, ou vide la cuve
    /// de récupération de l'extracteur (caniveau).
    /// </summary>
    public sealed class ResourceStation : MonoBehaviour, IInteractable
    {
        [SerializeField] ToolResource resource = ToolResource.Water;
        [SerializeField] ParticleSystem effect;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip sound;

        public ToolResource Resource => resource;

        bool Drains => resource == ToolResource.Recovery;

        public string GetPrompt(PlayerInteractor interactor)
        {
            var tank = interactor.Equipment.Get(resource);
            if (Drains)
                return tank.Amount <= 0.05f ? "Cuve de récupération vide" : $"Vider la cuve de récupération ({tank.Amount:0.0} {tank.Unit})";
            return tank.Amount >= tank.Capacity - 0.05f
                ? $"{tank.DisplayName} : plein"
                : $"Remplir : {tank.DisplayName} ({tank.Amount:0.0}/{tank.Capacity:0} {tank.Unit})";
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            var tank = interactor.Equipment.Get(resource);
            return Drains ? tank.Amount > 0.05f : tank.Amount < tank.Capacity - 0.05f;
        }

        public string Interact(PlayerInteractor interactor)
        {
            var tank = interactor.Equipment.Get(resource);
            float moved = Drains ? tank.Amount : tank.Capacity - tank.Amount;
            if (Drains) tank.Empty();
            else tank.Refill();
            if (effect != null) effect.Play();
            if (audioManager != null) audioManager.PlayAt(sound, transform.position, 0.8f);
            return Drains
                ? $"Cuve vidée ({moved:0.0} {tank.Unit} d'eau sale)"
                : $"{tank.DisplayName} rechargé (+{moved:0.0} {tank.Unit})";
        }
    }
}
