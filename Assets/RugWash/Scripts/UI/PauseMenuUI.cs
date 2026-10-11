using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Menu pause (Échap). Les boutons proposés dépendent du contexte et exécutent tous une action réelle :
    /// en commande → recommencer / abandonner la commande ; hors commande → recommencer / changer de tapis d'entraînement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenuUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button resumeButton;
        [SerializeField] Button resetRugButton;
        [SerializeField] Button nextRugButton;
        [SerializeField] Button abandonOrderButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text subtitleLabel;
        [SerializeField] Text resetRugLabel;

        GameStateManager gameState;
        RugCleaningController rug;
        OrderManager orders;
        IReadOnlyList<RugDefinition> availableRugs;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel)
                : resumeButton == null ? nameof(resumeButton)
                : resetRugButton == null ? nameof(resetRugButton)
                : nextRugButton == null ? nameof(nextRugButton)
                : abandonOrderButton == null ? nameof(abandonOrderButton)
                : quitButton == null ? nameof(quitButton)
                : subtitleLabel == null ? nameof(subtitleLabel)
                : resetRugLabel == null ? nameof(resetRugLabel) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, RugCleaningController rugController, OrderManager orderManager, IReadOnlyList<RugDefinition> rugs)
        {
            gameState = state;
            rug = rugController;
            orders = orderManager;
            availableRugs = rugs;

            resumeButton.onClick.AddListener(gameState.Resume);
            resetRugButton.onClick.AddListener(ResetRug);
            nextRugButton.onClick.AddListener(NextRug);
            abandonOrderButton.onClick.AddListener(AbandonOrder);
            quitButton.onClick.AddListener(Quit);

            gameState.StateChanged += OnStateChanged;
            panel.SetActive(gameState.State == GameState.Paused);
        }

        void OnDestroy()
        {
            if (gameState != null) gameState.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(GameState previous, GameState next)
        {
            bool paused = next == GameState.Paused;
            panel.SetActive(paused);
            if (!paused) return;

            bool inOrder = orders.ActiveOrder != null;
            subtitleLabel.text = inOrder
                ? $"Commande en cours : {orders.ActiveOrder.title}"
                : rug.Definition != null ? $"Entraînement : {rug.Definition.displayName}" : string.Empty;
            resetRugLabel.text = inOrder ? "Recommencer la commande" : "Recommencer ce tapis";
            nextRugButton.gameObject.SetActive(!inOrder && availableRugs != null && availableRugs.Count > 1);
            abandonOrderButton.gameObject.SetActive(inOrder);
            resumeButton.Select();
        }

        public void ResetRug()
        {
            if (orders.ActiveOrder != null) orders.RestartActive();
            else rug.ResetRug();
            gameState.Resume();
        }

        /// <summary>Tapis d'entraînement suivant (indisponible pendant une commande).</summary>
        public void NextRug()
        {
            if (orders.ActiveOrder != null || availableRugs == null || availableRugs.Count == 0) return;
            int index = 0;
            for (int i = 0; i < availableRugs.Count; i++)
                if (availableRugs[i] == rug.Definition) index = (i + 1) % availableRugs.Count;
            rug.LoadRug(availableRugs[index]);
            gameState.Resume();
        }

        public void AbandonOrder()
        {
            if (orders.ActiveOrder == null) return;
            orders.Abandon();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
