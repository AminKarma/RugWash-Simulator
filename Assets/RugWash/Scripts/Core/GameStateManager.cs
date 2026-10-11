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

        public static float TimeScaleFor(GameState state) => state == GameState.Paused ? 0f : 1f;
    }

    /// <summary>
    /// État de jeu explicite de la scène. Applique la politique de curseur et d'échelle de temps.
    /// Composant de scène (pas de singleton statique) : les dépendants le reçoivent par référence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameStateManager : MonoBehaviour
    {
        [SerializeField] GameState initialState = GameState.Workshop;

        GameState stateBeforePause;
        int openModals;
        bool started;

        public GameState State { get; private set; }
        public int OpenModals => openModals;
        public bool IsPaused => State == GameState.Paused;

        /// <summary>Vrai si le joueur contrôle son personnage (pas de pause ni de fenêtre ouverte).</summary>
        public bool IsGameplayActive => GameStateRules.ShouldLockCursor(State, openModals);

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
            Apply();
        }

        public void SetState(GameState next)
        {
            if (next == State) return;
            GameState previous = State;
            if (next == GameState.Paused) stateBeforePause = previous;
            State = next;
            Apply();
            StateChanged?.Invoke(previous, next);
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
            bool locked = GameStateRules.ShouldLockCursor(State, openModals);
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            Time.timeScale = GameStateRules.TimeScaleFor(State);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) Apply();
        }

        void OnDestroy()
        {
            // Ne jamais laisser le jeu figé ou le curseur capturé après la fermeture de la scène.
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
