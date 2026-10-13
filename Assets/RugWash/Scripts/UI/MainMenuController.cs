using RugWash.Audio;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Menu principal : Nouvelle partie (avec confirmation si une progression existe), Continuer
    /// (seulement si une sauvegarde avec progression existe), Paramètres, RugDex, Quitter.
    /// Navigable à la souris, au clavier et à la manette (B / Rond ferme la fenêtre ouverte, curseur masqué à la manette).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        public const string WorkshopScene = "Workshop";

        [SerializeField] SaveManager save;
        [SerializeField] SettingsManager settings;
        [SerializeField] AudioManager audioManager;
        [SerializeField] SettingsUI settingsUI;
        [SerializeField] Button playButton;
        [SerializeField] Button continueButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text continueSummary;
        [SerializeField] Text messageLabel;
        [SerializeField] GameObject confirmPanel;
        [SerializeField] Button confirmYesButton;
        [SerializeField] Button confirmNoButton;
        [SerializeField] GameObject loadingPanel;
        [Tooltip("Périphérique actif : curseur masqué quand la manette est utilisée.")]
        [SerializeField] InputDeviceMonitor inputMonitor;

        [Header("Multijoueur")]
        [SerializeField] MultiplayerMenuUI multiplayerUI;
        [SerializeField] Button multiplayerButton;
        [SerializeField] Network.NetworkContent networkContent;

        [Header("RugDex (lecture de la progression sauvegardée)")]
        [SerializeField] RugDex.RugDexUI rugDexUI;
        [SerializeField] Button rugDexButton;
        [SerializeField] RugDex.RugCatalog rugCatalog;
        [SerializeField] Cleaning.DirtTypeLibrary dirtLibrary;
        [SerializeField] System.Collections.Generic.List<Orders.OrderDefinition> orders = new System.Collections.Generic.List<Orders.OrderDefinition>();

        readonly RugDex.RugDexBook rugDexBook = new RugDex.RugDexBook();

        public Button PlayButton => playButton;
        public Button ContinueButton => continueButton;
        public Button SettingsButton => settingsButton;
        public Button ConfirmYesButton => confirmYesButton;
        public Button ConfirmNoButton => confirmNoButton;
        public bool IsConfirmVisible => confirmPanel.activeSelf;
        public bool IsLoading { get; private set; }
        public SettingsUI Settings => settingsUI;
        public RugDex.RugDexUI RugDex => rugDexUI;
        public Button RugDexButton => rugDexButton;
        public MultiplayerMenuUI Multiplayer => multiplayerUI;
        public Button MultiplayerButton => multiplayerButton;
        public string Message => messageLabel.text;
        public InputDeviceMonitor InputMonitor => inputMonitor;

        public bool IsConfigured(out string missing)
        {
            missing = save == null ? nameof(save) : settings == null ? nameof(settings) : audioManager == null ? nameof(audioManager)
                : settingsUI == null ? nameof(settingsUI) : playButton == null ? nameof(playButton) : continueButton == null ? nameof(continueButton)
                : settingsButton == null ? nameof(settingsButton) : quitButton == null ? nameof(quitButton)
                : continueSummary == null ? nameof(continueSummary) : messageLabel == null ? nameof(messageLabel)
                : confirmPanel == null ? nameof(confirmPanel) : confirmYesButton == null ? nameof(confirmYesButton)
                : confirmNoButton == null ? nameof(confirmNoButton) : loadingPanel == null ? nameof(loadingPanel)
                : rugDexUI == null ? nameof(rugDexUI) : rugDexButton == null ? nameof(rugDexButton) : rugCatalog == null ? nameof(rugCatalog)
                : multiplayerUI == null ? nameof(multiplayerUI) : multiplayerButton == null ? nameof(multiplayerButton) : null;
            return missing == null;
        }

        void Awake()
        {
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] Menu principal incomplet : '{missing}'.", this);
                enabled = false;
            }
        }

        void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            ApplyCursor();
            if (inputMonitor != null) inputMonitor.SchemeChanged += OnSchemeChanged;

            settingsUI.Bind(settings, save, null);
            playButton.onClick.AddListener(OnPlay);
            continueButton.onClick.AddListener(Continue);
            settingsButton.onClick.AddListener(() => settingsUI.Open());
            quitButton.onClick.AddListener(Quit);
            confirmYesButton.onClick.AddListener(StartNewGame);
            confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
            rugDexBook.Restore(save.Current.rugDex);
            rugDexUI.Bind(rugCatalog, rugDexBook, orders, dirtLibrary, null);
            rugDexButton.onClick.AddListener(() => rugDexUI.Open());
            multiplayerButton.onClick.AddListener(() => multiplayerUI.Open());
            audioManager.RegisterButtons(multiplayerUI);
            audioManager.RegisterButtons(this);
            audioManager.RegisterButtons(settingsUI);
            audioManager.RegisterButtons(rugDexUI);

            confirmPanel.SetActive(false);
            loadingPanel.SetActive(false);
            Refresh();
            // Steam est initialisé dès le menu principal : invitations reçues et overlay disponibles sans ouvrir la
            // fenêtre Multijoueur. Sans client Steam lancé, l'échec est silencieux ici (expliqué dans la fenêtre Multijoueur).
            Network.MultiplayerSession.GetOrCreate(networkContent).EnsureSteam();
            ShowSessionOutcome();
        }

        /// <summary>
        /// Retour d'une partie en ligne (départ, hôte parti, erreur réseau) : le message est affiché ; s'il y avait une erreur,
        /// la fenêtre Multijoueur s'ouvre avec « Se reconnecter ». Invitation Steam acceptée (jeu fermé ou pendant une
        /// partie) : la fenêtre Multijoueur s'ouvre et montre la connexion au salon.
        /// </summary>
        void ShowSessionOutcome()
        {
            var session = Network.MultiplayerSession.Find();
            if (session == null) return;
            bool launchedByInvite = Network.SteamPlatform.LobbyFromCommandLine(System.Environment.GetCommandLineArgs()) != 0;
            bool joining = session.State == Network.SessionState.JoiningLobby || session.State == Network.SessionState.InLobby;
            if (!string.IsNullOrEmpty(session.Message)) messageLabel.text = session.Message;
            if (session.LastEndedWithError || launchedByInvite || joining) multiplayerUI.Open();
        }

        void Refresh()
        {
            var data = save.Current;
            bool canContinue = data.HasProgress && save.LastLoadStatus != LoadStatus.NewerVersion;
            continueButton.interactable = canContinue;
            continueSummary.text = canContinue
                ? $"{WalletManager.Format(data.money)} · réputation {data.reputation} · {data.ordersDelivered} commande(s) livrée(s)"
                : "Aucune partie en cours";
            messageLabel.text = save.LastLoadMessage ?? string.Empty;
            (canContinue ? continueButton : playButton).Select();
        }

        void OnDestroy()
        {
            if (inputMonitor != null) inputMonitor.SchemeChanged -= OnSchemeChanged;
        }

        void OnSchemeChanged(InputScheme _) => ApplyCursor();

        void ApplyCursor() => Cursor.visible = GameStateRules.ShouldShowCursor(false, inputMonitor != null ? inputMonitor.Scheme : InputScheme.KeyboardMouse);

        /// <summary>Retour : Échap au clavier, B / Rond à la manette (touches d'interface, non reconfigurables).</summary>
        static bool BackPressed() =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        void Update()
        {
            if (!BackPressed()) return;
            if (settingsUI.IsRebinding) return;
            if (settingsUI.IsOpen) settingsUI.Close();
            else if (multiplayerUI.IsOpen) multiplayerUI.Close();
            else if (rugDexUI.IsOpen) rugDexUI.Close();
            else if (confirmPanel.activeSelf) confirmPanel.SetActive(false);
        }

        void OnPlay()
        {
            if (save.Current.HasProgress) confirmPanel.SetActive(true);
            else StartNewGame();
        }

        public void StartNewGame()
        {
            confirmPanel.SetActive(false);
            if (!save.ResetProgress())
            {
                messageLabel.text = "Impossible de créer la nouvelle partie (sauvegarde protégée ou disque inaccessible).";
                return;
            }
            LoadWorkshop();
        }

        public void Continue()
        {
            if (!continueButton.interactable) return;
            LoadWorkshop();
        }

        void LoadWorkshop()
        {
            if (IsLoading) return;
            IsLoading = true;
            loadingPanel.SetActive(true);
            SceneManager.LoadSceneAsync(WorkshopScene);
        }

        void Quit()
        {
            save.SaveSettingsIfDirty();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
