using RugWash.Player;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>Base de recharge du robot : l'interaction démarre ou arrête le robot, comme sur le robot lui-même.</summary>
    [DisallowMultipleComponent]
    public sealed class RobotDock : MonoBehaviour, IInteractable
    {
        [SerializeField] RobotVacuum robot;

        public RobotVacuum Robot => robot;

        public string GetPrompt(PlayerInteractor interactor) => robot != null ? robot.GetPrompt(interactor) : "Base de recharge";
        public bool CanInteract(PlayerInteractor interactor) => robot != null && robot.CanInteract(interactor);
        public string Interact(PlayerInteractor interactor) => robot != null ? robot.Toggle() : null;
    }
}
