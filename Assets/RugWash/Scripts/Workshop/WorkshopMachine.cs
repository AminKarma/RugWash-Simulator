using System;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Workshop
{
    public enum MachineKind
    {
        /// <summary>Souffleur de séchage : marche / arrêt, sèche le tapis de la commande tant qu'il tourne.</summary>
        Blower,
        /// <summary>Plaque vibrante de dépoussiérage : un cycle par commande, fait tomber la poussière sèche.</summary>
        DustPlate,
        /// <summary>Rampe de pré-mouillage : un cycle par commande, humidifie et fait tremper les taches.</summary>
        PreWet,
    }

    /// <summary>
    /// Machine fonctionnelle de l'atelier (v0.1.2) : achetée à la boutique (équipement d'atelier), cachée avant l'achat,
    /// actionnée par E. Elle agit réellement sur le tapis de la commande posé sur l'aire de lavage, sans jamais le
    /// nettoyer entièrement (voir <see cref="MachineKind"/>). En ligne, l'hôte décide (achat, démarrage) ; chaque poste
    /// reproduit l'effet pendant que la machine tourne et l'hôte corrige les écarts (comme la simulation lente).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopMachine : MonoBehaviour, IInteractable
    {
        [SerializeField] MachineKind kind;
        [Tooltip("Identifiant d'équipement (amélioration « équipement d'atelier »).")]
        [SerializeField] string machineId = "blower";
        [SerializeField] string displayName = "Souffleur de séchage";
        [SerializeField] RugCleaningController rug;
        [SerializeField] OrderManager orders;
        [Tooltip("Parties visibles et colliders de la machine (cachées tant qu'elle n'est pas achetée).")]
        [SerializeField] GameObject body;
        [SerializeField] ParticleSystem effect;
        [SerializeField] AudioSource loopSource;
        [Tooltip("Pièce animée (pales, plateau) pendant le fonctionnement, optionnelle.")]
        [SerializeField] Transform spinner;
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float spinSpeed = 720f;

        [Header("Réglages")]
        [Tooltip("Intervalle entre deux applications de l'effet (s).")]
        [SerializeField, Min(0.05f)] float tickSeconds = 0.25f;
        [Tooltip("Durée d'un cycle (plaque, rampe).")]
        [SerializeField, Min(1f)] float cycleSeconds = 8f;
        [Tooltip("Souffleur : humidité retirée par seconde.")]
        [SerializeField, Range(0f, 0.2f)] float dryingPerSecond = 0.03f;
        [Tooltip("Plaque : part de la poussière sèche retirée sur un cycle complet.")]
        [SerializeField, Range(0f, 1f)] float dustRemovedPerCycle = 0.55f;
        [Tooltip("Rampe : humidité visée.")]
        [SerializeField, Range(0f, 1f)] float wetTarget = 0.55f;
        [Tooltip("Rampe : part des taches qui trempe sur un cycle complet (selon leur réponse à l'eau).")]
        [SerializeField, Range(0f, 1f)] float soakPerCycle = 0.2f;

        bool owned;
        bool running;
        float elapsed, tickTimer;
        string usedForOrder;

        public MachineKind Kind => kind;
        public string MachineId => machineId;
        public string DisplayName => displayName;
        public bool IsOwned => owned;
        public bool IsRunning => running;
        /// <summary>Effet total produit depuis le chargement (humidité retirée, poussière tombée ou taches trempées).</summary>
        public float TotalEffect { get; private set; }
        /// <summary>Multijoueur, côté invité : demandes envoyées à l'hôte. Null : solo ou hôte.</summary>
        public IWorkshopRequests Remote { get; set; }
        public event Action<string> Message;
        /// <summary>Marche, arrêt ou achat : l'hôte publie l'état aux invités.</summary>
        public event Action StateChanged;

        public bool IsConfigured(out string missing)
        {
            missing = rug == null ? nameof(rug) : body == null ? nameof(body) : string.IsNullOrEmpty(machineId) ? nameof(machineId) : null;
            return missing == null;
        }

        void Start() => ApplyVisibility();

        public void SetOwned(bool value)
        {
            if (owned == value) return;
            owned = value;
            if (!owned) SetRunning(false);
            ApplyVisibility();
            StateChanged?.Invoke();
        }

        void ApplyVisibility()
        {
            if (body == null) return;
            foreach (var renderer in body.GetComponentsInChildren<Renderer>(true)) renderer.enabled = owned;
            foreach (var collider in body.GetComponentsInChildren<Collider>(true)) collider.enabled = owned;
        }

        /// <summary>Invité : état décidé par l'hôte.</summary>
        public void ApplyRemoteState(bool isOwned, bool isRunning)
        {
            SetOwned(isOwned);
            if (isRunning != running) SetRunning(isRunning);
        }

        void SetRunning(bool value)
        {
            if (running == value) return;
            running = value;
            elapsed = tickTimer = 0f;
            if (effect != null)
            {
                if (value) effect.Play();
                else effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (loopSource != null)
            {
                if (value && !loopSource.isPlaying) loopSource.Play();
                else if (!value) loopSource.Stop();
            }
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ Interaction

        public string GetPrompt(PlayerInteractor interactor)
        {
            if (!owned) return string.Empty;
            switch (kind)
            {
                case MachineKind.Blower: return running ? $"Éteindre le {Lower} (sèche le tapis)" : $"Allumer le {Lower} (sèche le tapis)";
                case MachineKind.DustPlate:
                    return running ? $"{displayName} : cycle en cours ({Mathf.CeilToInt(cycleSeconds - elapsed)} s)" : $"Lancer la {Lower} ({cycleSeconds:0} s)";
                default:
                    return running ? $"{displayName} : cycle en cours ({Mathf.CeilToInt(cycleSeconds - elapsed)} s)" : $"Lancer la {Lower} ({cycleSeconds:0} s)";
            }
        }

        string Lower => displayName.ToLowerInvariant();

        public bool CanInteract(PlayerInteractor interactor) => owned;

        public string Interact(PlayerInteractor interactor) => Activate();

        /// <summary>Allume / éteint (souffleur) ou lance un cycle ; retourne le message affiché.</summary>
        public string Activate()
        {
            if (!owned) return $"{displayName} : à acheter sur l'ordinateur (Boutique)";
            if (Remote != null)
            {
                Remote.RequestMachine(machineId);
                return "Demande envoyée à l'hôte";
            }
            string message = ActivateLocal();
            Message?.Invoke(message);
            return message;
        }

        /// <summary>Décision locale (solo, hôte) : conditions d'utilisation et démarrage.</summary>
        public string ActivateLocal()
        {
            if (!owned) return $"{displayName} : à acheter sur l'ordinateur (Boutique)";
            if (kind == MachineKind.Blower)
            {
                SetRunning(!running);
                return running ? $"{displayName} allumé" : $"{displayName} éteint";
            }
            if (running) return $"{displayName} : cycle déjà en cours";
            if (rug == null || !rug.IsLoaded) return $"{displayName} : aucun tapis sur l'aire de lavage";
            // Machine d'appoint de la zone 1 : la commande du tapis posé sur son aire (pas celle de la zone 2).
            var current = orders != null ? orders.OrderOn(rug) : null;
            string order = current != null ? current.id : null;
            if (order == null) return $"{displayName} : acceptez d'abord une commande";
            if (order == usedForOrder) return $"{displayName} : déjà utilisée pour cette commande (un cycle par commande)";
            usedForOrder = order;
            SetRunning(true);
            return $"{displayName} : cycle lancé ({cycleSeconds:0} s)";
        }

        // ------------------------------------------------------------------ Effet

        void Update()
        {
            if (!running) return;
            float dt = Time.deltaTime;
            if (spinner != null) spinner.Rotate(spinAxis, spinSpeed * dt, Space.Self);
            tickTimer += dt;
            if (kind != MachineKind.Blower) elapsed += dt;
            while (tickTimer >= tickSeconds)
            {
                tickTimer -= tickSeconds;
                ApplyTick(tickSeconds);
            }
            // Fin de cycle : décidée par l'hôte (en ligne, l'invité reçoit l'arrêt).
            if (kind != MachineKind.Blower && elapsed >= cycleSeconds && Remote == null)
            {
                SetRunning(false);
                Message?.Invoke($"{displayName} : cycle terminé");
            }
        }

        /// <summary>Applique l'effet d'un intervalle sur le tapis chargé (test : appel direct possible).</summary>
        public void ApplyTick(float seconds)
        {
            if (rug == null || !rug.IsLoaded) return;
            var grid = rug.Grid;
            switch (kind)
            {
                case MachineKind.Blower:
                {
                    var op = new MachineDryOperation { Amount = dryingPerSecond * seconds };
                    grid.ApplyAllTracked(ref op, slowReplication: true);
                    TotalEffect += op.Removed;
                    break;
                }
                case MachineKind.DustPlate:
                {
                    var op = new ShakeOperation { Fraction = PerTick(dustRemovedPerCycle, seconds) };
                    grid.ApplyAllTracked(ref op, slowReplication: true);
                    TotalEffect += op.Removed;
                    break;
                }
                default:
                {
                    var op = new SoakOperation
                    {
                        Table = rug.DirtTable, WetTarget = wetTarget, WettingStep = wetTarget * seconds / (cycleSeconds * 0.5f),
                        Loosen = PerTick(soakPerCycle, seconds),
                    };
                    grid.ApplyAllTracked(ref op, slowReplication: true);
                    TotalEffect += op.Loosened;
                    break;
                }
            }
        }

        /// <summary>Part par intervalle telle que le cumul sur un cycle complet vaille <paramref name="perCycle"/>.</summary>
        float PerTick(float perCycle, float seconds) => 1f - Mathf.Pow(1f - Mathf.Clamp01(perCycle), seconds / cycleSeconds);
    }
}
