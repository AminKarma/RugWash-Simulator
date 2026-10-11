using RugWash.Audio;
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
    /// (seulement si une sauvegarde avec progression existe), Paramètres, Quitter.
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

        public Button PlayButton => playButton;
        public Button ContinueButton => continueButton;
        public Button SettingsButton => settingsButton;
        public Button ConfirmYesButton => confirmYesButton;
        public Button ConfirmNoButton => confirmNoButton;
        public bool IsConfirmVisible => confirmPanel.activeSelf;
        public bool IsLoading { get; private set; }
        public SettingsUI Settings => settingsUI;
        public string Message => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = save == null ? nameof(save) : settings == null ? nameof(settings) : audioManager == null ? nameof(audioManager)
                : settingsUI == null ? nameof(settingsUI) : playButton == null ? nameof(playButton) : continueButton == null ? nameof(continueButton)
                : settingsButton == null ? nameof(settingsButton) : quitButton == null ? nameof(quitButton)
                : continueSummary == null ? nameof(continueSummary) : messageLabel == null ? nameof(messageLabel)
                : confirmPanel == null ? nameof(confirmPanel) : confirmYesButton == null ? nameof(confirmYesButton)
                : confirmNoButton == null ? nameof(confirmNoButton) : loadingPanel == null ? nameof(loadingPanel) : null;
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
            Cursor.visible = true;

            settingsUI.Bind(settings, save, null);
            playButton.onClick.AddListener(OnPlay);
            continueButton.onClick.AddListener(Continue);
            settingsButton.onClick.AddListener(() => settingsUI.Open());
            quitButton.onClick.AddListener(Quit);
            confirmYesButton.onClick.AddListener(StartNewGame);
            confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
            audioManager.RegisterButtons(this);
            audioManager.RegisterButtons(settingsUI);

            confirmPanel.SetActive(false);
            loadingPanel.SetActive(false);
            Refresh();
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

        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (settingsUI.IsRebinding) return;
            if (settingsUI.IsOpen) settingsUI.Close();
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
