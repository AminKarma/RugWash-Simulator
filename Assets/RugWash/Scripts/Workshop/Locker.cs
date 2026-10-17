using RugWash.Player;
using RugWash.UI;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>
    /// Casier de l'atelier (grande mise à jour) : E ouvre la fenêtre du casier (ranger et prendre des outils et gadgets). Le
    /// contenu appartient à chaque joueur (sauvegardé dans son emplacement) : en ligne, le casier de l'hôte montre à chacun
    /// ses propres objets, sans rien échanger avec les autres.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Locker : MonoBehaviour, IInteractable
    {
        [SerializeField] LockerUI ui;
        [Tooltip("Porte animée à l'ouverture (optionnelle).")]
        [SerializeField] Transform door;
        [SerializeField] float openAngle = -100f;

        Quaternion closed;
        float open;

        public bool IsConfigured(out string missing)
        {
            missing = ui == null ? nameof(ui) : null;
            return missing == null;
        }

        void Awake()
        {
            if (door != null) closed = door.localRotation;
        }

        public string GetPrompt(PlayerInteractor interactor) => "Ouvrir le casier (ranger / prendre des objets)";

        public bool CanInteract(PlayerInteractor interactor) => ui != null && !ui.IsOpen;

        public string Interact(PlayerInteractor interactor)
        {
            if (ui == null || !ui.Open()) return null;
            return null;
        }

        void Update()
        {
            if (door == null) return;
            open = Mathf.MoveTowards(open, ui != null && ui.IsOpen ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            door.localRotation = closed * Quaternion.Euler(0f, openAngle * Mathf.SmoothStep(0f, 1f, open), 0f);
        }
    }
}
