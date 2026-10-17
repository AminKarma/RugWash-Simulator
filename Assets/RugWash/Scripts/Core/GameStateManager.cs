using System;
using UnityEngine;

namespace RugWash.Core
{
    public enum GameState
    {
        MainMenu,
        Workshop,
        ActiveOrder,
        Results,
        Paused,
    }

    /// <summary>Règles pures de transition et de curseur (testables sans scène).</summary>
    public static class GameStateRules
    {
        public static bool IsGameplay(GameState state) => state == GameState.Workshop || state == GameState.ActiveOrder;

        public static bool CanPause(GameState state) => IsGameplay(state);

        /// <summary>Le curseur n'est verrouillé que pendant le gameplay, sans fenêtre modale ouverte.</summary>
        public static bool ShouldLockCursor(GameState state, int openModals) => IsGameplay(state) && openModals == 0;

        /// <summary>Curseur visible : menus et fenêtres, sauf quand on joue à la manette (navigation sans souris).</summary>
        public static bool ShouldShowCursor(bool locked, InputScheme scheme) => !locked && scheme != InputScheme.Gamepad;

        public static float TimeScaleFor(GameState state) => state == GameState.Paused ? 0f : 1f;

        /// <summary>En partie à plusieurs, la pause est locale (menu, entrées coupées) : le temps partagé ne s'arrête pas.</summary>
        public static float TimeScaleFor(GameState state, bool networked) => networked ? 1f : TimeScaleFor(state);
    }

    /// <summary>
    /// État de jeu explicite de la scène. Applique la politique de curseur et d'échelle de temps.
    /// Composant de scène (pas de singleton statique) : les dépendants le reçoivent par référence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameStateManager : MonoBehaviour
    {
        [SerializeField] GameState initialState = GameState.Workshop;
        [Tooltip("Optionnel : masque le curseur dans les menus quand la manette est utilisée.")]
        [SerializeField] InputDeviceMonitor inputMonitor;

        GameState stateBeforePause;
        int openModals;
        bool started;

        public GameState State { get; private set; }

        /// <summary>Partie en réseau : la pause n'arrête plus le temps (voir <see cref="GameStateRules.TimeScaleFor(GameState, bool)"/>).</summary>
        public bool Networked
        {
            get => networked;
            set
            {
                networked = value;
                Apply();
            }
        }

        bool networked;
        bool synchronizing;
        public int OpenModals => openModals;
        public bool IsPaused => State == GameState.Paused;

        /// <summary>
        /// Multijoueur, invité qui arrive : l'état autoritaire de l'hôte (atelier, tapis, carrière validée) n'est pas encore
        /// reçu. Le joueur ne contrôle pas encore son personnage et le HUD affiche un état de chargement.
        /// </summary>
        public bool Synchronizing
        {
            get => synchronizing;
            set
            {
                if (synchronizing == value) return;
                synchronizing = value;
                Apply();
                ModalsChanged?.Invoke();
            }
        }

        /// <summary>Vrai si le joueur contrôle son personnage (pas de pause, de fenêtre ouverte ni de synchronisation).</summary>
        public bool IsGameplayActive => GameStateRules.ShouldLockCursor(State, openModals) && !synchronizing;

        public event Action<GameState, GameState> StateChanged;
        public event Action ModalsChanged;

        void Awake()
        {
            State = initialState;
            stateBeforePause = initialState;
        }

        void Start()
        {
            started = true;
            if (inputMonitor != null) inputMonitor.SchemeChanged += OnSchemeChanged;
            Apply();
        }

        void OnSchemeChanged(InputScheme _) => Apply();

        public void SetState(GameState next)
        {
            if (next == State) return;
            GameState previous = State;
            if (next == GameState.Paused) stateBeforePause = previous;
            State = next;
            Apply();
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>
        /// Atelier libre ou commande en cours (zones de lavage, grande mise à jour) : appliqué tout de suite pendant le jeu ;
        /// pendant une pause, devient l'état de reprise ; l'écran de résultats et le menu ne sont pas interrompus.
        /// </summary>
        public void SetGameplayState(GameState next)
        {
            if (!GameStateRules.IsGameplay(next)) return;
            if (State == GameState.Paused) stateBeforePause = next;
            else if (GameStateRules.IsGameplay(State)) SetState(next);
        }

        public bool Pause()
        {
            if (!GameStateRules.CanPause(State)) return false;
            SetState(GameState.Paused);
            return true;
        }

        public void Resume()
        {
            if (State == GameState.Paused) SetState(stateBeforePause);
        }

        public void TogglePause()
        {
            if (State == GameState.Paused) Resume();
            else Pause();
        }

        /// <summary>Signale l'ouverture d'une fenêtre qui libère le curseur sans mettre le jeu en pause.</summary>
        public void PushModal()
        {
            openModals++;
            Apply();
            ModalsChanged?.Invoke();
        }

        public void PopModal()
        {
            if (openModals == 0)
            {
                Debug.LogWarning("[RugWash] PopModal sans PushModal correspondant.", this);
                return;
            }
            openModals--;
            Apply();
            ModalsChanged?.Invoke();
        }

        void Apply()
        {
            if (!started) return;
            bool locked = GameStateRules.ShouldLockCursor(State, openModals) && !synchronizing;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = GameStateRules.ShouldShowCursor(locked, inputMonitor != null ? inputMonitor.Scheme : InputScheme.KeyboardMouse);
            Time.timeScale = GameStateRules.TimeScaleFor(State, networked);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) Apply();
        }

        void OnDestroy()
        {
            if (inputMonitor != null) inputMonitor.SchemeChanged -= OnSchemeChanged;
            // Ne jamais laisser le jeu figé ou le curseur capturé après la fermeture de la scène.
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
