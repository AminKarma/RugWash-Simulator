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
    /// en commande → recommencer / abandonner la commande. Hors commande, aucun tapis n'est posé (plus d'entraînement).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenuUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button resumeButton;
        [SerializeField] Button resetRugButton;
        [SerializeField] Button abandonOrderButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button rugDexButton;
        [SerializeField] Button mainMenuButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text subtitleLabel;
        [SerializeField] Text resetRugLabel;
        [Tooltip("Libellé du bouton « Menu principal » (devient « Quitter la partie » en ligne).")]
        [SerializeField] Text mainMenuLabel;
        [Tooltip("En ligne : joueurs présents, couper une voix, expulser (hôte), ouvrir ou fermer la partie.")]
        [SerializeField] Button playersButton;

        GameStateManager gameState;
        RugCleaningController rug;
        OrderManager orders;
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
                : abandonOrderButton == null ? nameof(abandonOrderButton)
                : settingsButton == null ? nameof(settingsButton)
                : rugDexButton == null ? nameof(rugDexButton)
                : mainMenuButton == null ? nameof(mainMenuButton)
                : quitButton == null ? nameof(quitButton)
                : subtitleLabel == null ? nameof(subtitleLabel)
                : resetRugLabel == null ? nameof(resetRugLabel) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, RugCleaningController rugController, OrderManager orderManager,
            SettingsUI settingsWindow, SaveManager saveManager)
        {
            settingsUI = settingsWindow;
            save = saveManager;
            settingsButton.onClick.AddListener(() => settingsUI.Open());
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            gameState = state;
            rug = rugController;
            orders = orderManager;

            resumeButton.onClick.AddListener(gameState.Resume);
            resetRugButton.onClick.AddListener(ResetRug);
            abandonOrderButton.onClick.AddListener(AbandonOrder);
            quitButton.onClick.AddListener(Quit);

            gameState.StateChanged += OnStateChanged;
            panel.SetActive(gameState.State == GameState.Paused);
        }

        /// <summary>Bouton « Joueurs » (en ligne) : ouvre la fenêtre des joueurs par-dessus la pause.</summary>
        public void BindPlayers(PlayersUI players)
        {
            if (playersButton != null && players != null) playersButton.onClick.AddListener(() => players.Open());
        }

        public Button PlayersButton => playersButton;

        /// <summary>Bouton « RugDex » : ouvre la collection par-dessus la pause.</summary>
        public void BindRugDex(RugDex.RugDexUI rugDex) => rugDexButton.onClick.AddListener(() => rugDex.Open());

        Economy.ReputationManager reputation;

        /// <summary>V0.3.0 : réputation et niveau du joueur, retirés du HUD, rappelés dans la pause.</summary>
        public void BindProgress(Economy.ReputationManager reputationManager) => reputation = reputationManager;

        /// <summary>Ligne « Réputation · niveau » affichée sous la commande en cours.</summary>
        public string ProgressLine => (reputation != null ? $"Réputation {reputation.Points}" : string.Empty)
            + (save != null ? (reputation != null ? " · " : string.Empty) + $"niveau {save.Profile.Level}" : string.Empty);

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
            var session = Network.MultiplayerSession.Find();
            bool online = session != null && session.InSession;
            bool guest = online && session.IsGuest;
            string zone = inOrder && orders.UnlockedBayCount > 1 ? $" ({OrderManager.BayLabel(orders.CurrentBay).ToLowerInvariant()})" : string.Empty;
            string progress = ProgressLine;
            subtitleLabel.text = (inOrder ? $"Commande en cours{zone} : {orders.ActiveOrder.title}" : "Aucune commande en cours")
                + (progress.Length > 0 ? "\n" + progress : string.Empty)
                + (online ? (guest ? "\nPartie en ligne — le jeu continue pendant la pause" : "\nVous êtes l'hôte — le jeu continue pendant la pause") : string.Empty);
            resetRugLabel.text = "Recommencer la commande";
            // Invité : le tapis et la commande appartiennent à l'hôte. Hors commande, il n'y a pas de tapis à recommencer.
            resetRugButton.gameObject.SetActive(!guest && inOrder);
            abandonOrderButton.gameObject.SetActive(inOrder);
            if (mainMenuLabel != null) mainMenuLabel.text = online ? "Quitter la partie" : "Menu principal";
            if (playersButton != null) playersButton.gameObject.SetActive(online);
            resumeButton.Select();
        }

        /// <summary>Recommence la commande en cours (saleté régénérée). Sans commande, rien ne se passe.</summary>
        public void ResetRug()
        {
            if (rug.IsReplica || orders.ActiveOrder == null) return;
            orders.RestartActive();
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
            var session = Network.MultiplayerSession.Find();
            if (session != null && session.InSession)
            {
                // En ligne : chacun sauvegarde sa propre progression (l'hôte : son atelier et sa carrière ; l'invité : sa
                // carrière seule), puis la session se ferme (l'hôte qui part ramène ses invités au menu).
                save.SaveNow(session.IsHost ? "fin de la partie en ligne" : "départ de la partie en ligne");
                session.Leave();
                return;
            }
            // Toutes les zones : les tapis posés ne sont pas sauvegardés (celui de la machine à laver l'est, avec l'atelier).
            for (int bay = 0; bay < orders.BayCount; bay++)
                if (orders.OrderOf(bay) != null) orders.Abandon(bay);
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
