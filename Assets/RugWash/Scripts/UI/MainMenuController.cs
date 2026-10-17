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
    /// Menu principal : Jouer (choix d'un des trois emplacements solo), Continuer (dernier emplacement solo joué, s'il a
    /// une progression), Multijoueur (emplacements multijoueur séparés), Personnage, RugDex, Paramètres, Quitter.
    /// Navigable à la souris, au clavier et à la manette (B / Rond ferme la fenêtre ouverte, curseur masqué à la manette).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        public const string WorkshopScene = "Workshop";

        [SerializeField] SaveManager save;
        [SerializeField] SettingsManager settings;
        [SerializeField] AudioManager audioManager;
        [Tooltip("Musique de fond du menu (lecteur unique conservé entre les scènes).")]
        [SerializeField] AudioClip menuMusic;
        [SerializeField] SettingsUI settingsUI;
        [SerializeField] Button playButton;
        [SerializeField] Button continueButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text continueSummary;
        [SerializeField] Text messageLabel;
        [SerializeField] SaveSlotsUI saveSlotsUI;
        [SerializeField] GameObject loadingPanel;
        [Tooltip("Périphérique actif : curseur masqué quand la manette est utilisée.")]
        [SerializeField] InputDeviceMonitor inputMonitor;

        [Header("Multijoueur")]
        [SerializeField] MultiplayerMenuUI multiplayerUI;
        [SerializeField] Button multiplayerButton;
        [SerializeField] Network.NetworkContent networkContent;

        [Header("RugDex (lecture de la progression sauvegardée)")]
        [SerializeField] RugDex.RugDexUI rugDexUI;
        [Header("Personnage (v0.1.2)")]
        [SerializeField] CharacterMenuUI characterUI;
        [SerializeField] Button characterButton;
        [SerializeField] Button rugDexButton;
        [SerializeField] RugDex.RugCatalog rugCatalog;
        [SerializeField] Cleaning.DirtTypeLibrary dirtLibrary;
        [SerializeField] System.Collections.Generic.List<Orders.OrderDefinition> orders = new System.Collections.Generic.List<Orders.OrderDefinition>();

        readonly RugDex.RugDexBook rugDexBook = new RugDex.RugDexBook();

        public Button PlayButton => playButton;
        public Button ContinueButton => continueButton;
        public Button SettingsButton => settingsButton;
        public SaveSlotsUI SaveSlots => saveSlotsUI;
        public bool IsLoading { get; private set; }
        public SettingsUI Settings => settingsUI;
        public RugDex.RugDexUI RugDex => rugDexUI;
        public Button RugDexButton => rugDexButton;
        public CharacterMenuUI Character => characterUI;
        public Button CharacterButton => characterButton;
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
                : saveSlotsUI == null ? nameof(saveSlotsUI) : loadingPanel == null ? nameof(loadingPanel)
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
            var music = MusicPlayer.Ensure();
            music.Ducked = false;
            music.Play(menuMusic);
            if (inputMonitor != null) inputMonitor.SchemeChanged += OnSchemeChanged;

            settingsUI.Bind(settings, save, null);
            playButton.onClick.AddListener(OnPlay);
            continueButton.onClick.AddListener(Continue);
            settingsButton.onClick.AddListener(() => settingsUI.Open());
            quitButton.onClick.AddListener(Quit);
            saveSlotsUI.Bind(save);
            saveSlotsUI.SlotChosen += OnSlotChosen;
            saveSlotsUI.SlotsChanged += Refresh;
            audioManager.RegisterButtons(saveSlotsUI);
            multiplayerUI.BindSaves(save, saveSlotsUI);
            rugDexBook.Restore(save.Current.rugDex);
            rugDexUI.Bind(rugCatalog, rugDexBook, orders, dirtLibrary, null);
            rugDexButton.onClick.AddListener(() => rugDexUI.Open());
            if (characterUI != null)
            {
                characterUI.Bind(save);
                characterButton.onClick.AddListener(() => characterUI.Open());
                audioManager.RegisterButtons(characterUI);
            }
            multiplayerButton.onClick.AddListener(() => multiplayerUI.Open());
            audioManager.RegisterButtons(multiplayerUI);
            audioManager.RegisterButtons(this);
            audioManager.RegisterButtons(settingsUI);
            audioManager.RegisterButtons(rugDexUI);

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

        /// <summary>Dernier emplacement solo joué (profil), lu sur le disque.</summary>
        public SlotSummary LastSolo => Save.SaveSlots.ReadSummary(save.Profile.LastSlot(SaveMode.Solo));

        void Refresh()
        {
            var last = LastSolo;
            bool canContinue = last.HasProgress;
            continueButton.interactable = canContinue;
            continueSummary.text = canContinue
                ? $"{last.Slot.DisplayName} · {WalletManager.Format(last.Data.money)} · réputation {last.Data.reputation} · niveau {save.Profile.Level}"
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
            else if (saveSlotsUI.IsConfirmVisible) saveSlotsUI.ConfirmNoButton.onClick.Invoke();
            else if (saveSlotsUI.IsOpen) saveSlotsUI.Close();
            else if (multiplayerUI.IsOpen) multiplayerUI.Close();
            else if (rugDexUI.IsOpen) rugDexUI.Close();
            else if (characterUI != null && characterUI.IsOpen) characterUI.Close();
        }

        void OnPlay() => saveSlotsUI.Open(SaveMode.Solo);

        void OnSlotChosen(SaveSlot slot, SlotSummary summary)
        {
            if (slot.Mode == SaveMode.Multiplayer)
            {
                multiplayerUI.SetSlot(slot);
                saveSlotsUI.Close();
                return;
            }
            PlaySlot(slot);
        }

        /// <summary>
        /// Lance l'atelier sur un emplacement solo : sa progression s'il en a une, sinon une nouvelle partie (kit de départ).
        /// Le profil (niveau, paramètres) n'est jamais remis à zéro.
        /// </summary>
        public bool PlaySlot(SaveSlot slot)
        {
            if (slot.Mode != SaveMode.Solo || IsLoading) return false;
            var summary = Save.SaveSlots.ReadSummary(slot);
            if (summary.Exists && !summary.Readable)
            {
                messageLabel.text = $"{slot.DisplayName} : {summary.Problem}.";
                return false;
            }
            if (!summary.HasProgress && !save.ResetSlot(slot))
            {
                messageLabel.text = "Impossible de créer la nouvelle partie (sauvegarde protégée ou disque inaccessible).";
                return false;
            }
            Save.SaveSlots.Selected = slot;
            saveSlotsUI.Close();
            LoadWorkshop();
            return true;
        }

        /// <summary>Nouvelle partie sur un emplacement solo (progression effacée, profil conservé), puis l'atelier.</summary>
        public bool StartNewGame(SaveSlot slot)
        {
            if (slot.Mode != SaveMode.Solo || !save.ResetSlot(slot)) return false;
            return PlaySlot(slot);
        }

        public void Continue()
        {
            if (!continueButton.interactable) return;
            PlaySlot(save.Profile.LastSlot(SaveMode.Solo));
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
