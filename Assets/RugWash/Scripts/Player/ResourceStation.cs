using RugWash.Audio;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Poste de l'atelier lié à une ressource : remplit l'eau (gratuite), remplit la réserve de détergent à partir du stock
    /// en bidons du joueur (rien sans stock : il faut acheter un bidon), ou vide la cuve de récupération (caniveau).
    /// </summary>
    public sealed class ResourceStation : MonoBehaviour, IInteractable
    {
        [SerializeField] ToolResource resource = ToolResource.Water;
        [SerializeField] ParticleSystem effect;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip sound;

        public ToolResource Resource => resource;

        bool Drains => resource == ToolResource.Recovery;

        DetergentSupply Supply(PlayerInteractor interactor) => resource == ToolResource.Soap ? interactor.Equipment.Detergent : null;

        public string GetPrompt(PlayerInteractor interactor)
        {
            var tank = interactor.Equipment.Get(resource);
            var supply = Supply(interactor);
            if (supply != null && tank.Amount < tank.Capacity - 0.05f)
                return supply.Stock <= 0.05f
                    ? "Plus de détergent en stock — achetez un bidon de 40 L (ordinateur, Boutique)"
                    : $"Remplir la réserve de détergent ({tank.Amount:0.0}/{tank.Capacity:0.#} L) · stock {supply.Stock:0.#} L";
            if (Drains)
                return tank.Amount <= 0.05f ? "Cuve de récupération vide" : $"Vider la cuve de récupération ({tank.Amount:0.0} {tank.Unit})";
            return tank.Amount >= tank.Capacity - 0.05f
                ? $"{tank.DisplayName} : plein"
                : $"Remplir : {tank.DisplayName} ({tank.Amount:0.0}/{tank.Capacity:0} {tank.Unit})";
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            var tank = interactor.Equipment.Get(resource);
            var supply = Supply(interactor);
            if (supply != null) return tank.Amount < tank.Capacity - 0.05f && supply.Stock > 0.05f;
            return Drains ? tank.Amount > 0.05f : tank.Amount < tank.Capacity - 0.05f;
        }

        public string Interact(PlayerInteractor interactor)
        {
            var tank = interactor.Equipment.Get(resource);
            var supply = Supply(interactor);
            if (supply != null) return RefillDetergent(tank, supply);
            float moved = Drains ? tank.Amount : tank.Capacity - tank.Amount;
            if (Drains) tank.Empty();
            else tank.Refill();
            if (effect != null) effect.Play();
            if (audioManager != null) audioManager.PlayAt(sound, transform.position, 0.8f);
            return Drains
                ? $"Cuve vidée ({moved:0.0} {tank.Unit} d'eau sale)"
                : $"{tank.DisplayName} rechargé (+{moved:0.0} {tank.Unit})";
        }

        /// <summary>Réserve remplie avec ce qui manque, dans la limite du stock (en ligne : prélèvement validé par l'hôte).</summary>
        string RefillDetergent(ResourceTank tank, DetergentSupply supply)
        {
            float wanted = tank.Capacity - tank.Amount;
            if (supply.Remote != null)
            {
                supply.Remote.RequestDetergentRefill(wanted);
                return "Remplissage demandé à l'hôte…";
            }
            float taken = supply.Take(wanted);
            if (taken <= 0f) return "Plus de détergent en stock — achetez un bidon de 40 L (ordinateur, Boutique)";
            tank.Add(taken);
            if (effect != null) effect.Play();
            if (audioManager != null) audioManager.PlayAt(sound, transform.position, 0.8f);
            return $"Réserve de détergent : +{taken:0.0} L ({tank.Amount:0.0}/{tank.Capacity:0.#} L) · stock restant {supply.Stock:0.#} L";
        }

        /// <summary>Multijoueur (invité) : l'hôte a accordé <paramref name="liters"/> litres prélevés sur sa carrière.</summary>
        public static string ApplyGrantedDetergent(ResourceTank tank, float liters) =>
            liters <= 0f
                ? "Plus de détergent en stock — achetez un bidon de 40 L (ordinateur, Boutique)"
                : $"Réserve de détergent : +{tank.Add(liters):0.0} L ({tank.Amount:0.0}/{tank.Capacity:0.#} L)";
    }
}
