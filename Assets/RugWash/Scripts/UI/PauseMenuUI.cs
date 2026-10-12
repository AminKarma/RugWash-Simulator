using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Save;
using UnityEngine.SceneManagement;
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
        [SerializeField] Button settingsButton;
        [SerializeField] Button rugDexButton;
        [SerializeField] Button mainMenuButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text subtitleLabel;
        [SerializeField] Text resetRugLabel;

        GameStateManager gameState;
        RugCleaningController rug;
        OrderManager orders;
        IReadOnlyList<RugDefinition> availableRugs;
        SettingsUI settingsUI;
        SaveManager save;

        public const string MainMenuScene = "MainMenu";

        public Button ResumeButton => resumeButton;
        public Button SettingsButton => settingsButton;
        public Button RugDexButton => rugDexButton;
        public bool IsVisible => panel != null && panel.activeSelf;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel)
                : resumeButton == null ? nameof(resumeButton)
                : resetRugButton == null ? nameof(resetRugButton)
                : nextRugButton == null ? nameof(nextRugButton)
                : abandonOrderButton == null ? nameof(abandonOrderButton)
                : settingsButton == null ? nameof(settingsButton)
                : rugDexButton == null ? nameof(rugDexButton)
                : mainMenuButton == null ? nameof(mainMenuButton)
                : quitButton == null ? nameof(quitButton)
                : subtitleLabel == null ? nameof(subtitleLabel)
                : resetRugLabel == null ? nameof(resetRugLabel) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, RugCleaningController rugController, OrderManager orderManager, IReadOnlyList<RugDefinition> rugs,
            SettingsUI settingsWindow, SaveManager saveManager)
        {
            settingsUI = settingsWindow;
            save = saveManager;
            settingsButton.onClick.AddListener(() => settingsUI.Open());
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
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

        /// <summary>Bouton « RugDex » : ouvre la collection par-dessus la pause.</summary>
        public void BindRugDex(RugDex.RugDexUI rugDex) => rugDexButton.onClick.AddListener(() => rugDex.Open());

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

        /// <summary>Retour au menu principal : une commande en cours est abandonnée (non sauvegardée), puis la progression est sauvegardée.</summary>
        public void ReturnToMainMenu()
        {
            if (orders.ActiveOrder != null) orders.Abandon();
            save.SaveNow("retour au menu principal");
            SceneManager.LoadScene(MainMenuScene);
        }

        public void Quit()
        {
            save.SaveNow("fermeture du jeu");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
