using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Diagnostics;
using RugWash.Economy;
using RugWash.Effects;
using RugWash.Orders;
using RugWash.Player;
using RugWash.Save;
using RugWash.UI;
using UnityEngine;

namespace RugWash.Core
{
    /// <summary>
    /// Racine de composition de la scène Atelier : vérifie les références (câblées par l'outil
    /// RugWashSceneBuilder), injecte les dépendances et route les entrées système (pause, inspection).
    /// Références explicites, sans localisateur de services ni singleton.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Noyau")]
        [SerializeField] GameStateManager gameState;
        [SerializeField] AudioManager audioManager;
        [SerializeField] SaveManager saveManager;
        [SerializeField] SettingsManager settingsManager;

        [Header("Joueur")]
        [SerializeField] PlayerInputReader input;
        [SerializeField] FirstPersonController playerController;
        [SerializeField] PlayerToolController toolController;
        [SerializeField] PlayerInteractor interactor;

        [Header("Tapis")]
        [SerializeField] RugCleaningController rug;
        [SerializeField] RugSnapshotService snapshots;
        [SerializeField] List<RugDefinition> availableRugs = new List<RugDefinition>();

        [Header("Commandes et économie")]
        [SerializeField] OrderManager orders;
        [SerializeField] WalletManager wallet;
        [SerializeField] ReputationManager reputation;
        [SerializeField] UpgradeManager upgrades;
        [SerializeField] ShopManager shop;

        [Header("Interface")]
        [SerializeField] HUDController hud;
        [SerializeField] ToolSelectionUI toolbar;
        [SerializeField] PauseMenuUI pauseMenu;
        [SerializeField] InspectionPanelUI inspection;
        [SerializeField] OrderBoardUI orderBoard;
        [SerializeField] ShopUI shopUI;
        [SerializeField] ResultsUI results;
        [SerializeField] SettingsUI settingsUI;
        [SerializeField] DebugOverlay debugOverlay;

        IModalPanel[] modalPanels;

        public GameStateManager GameState => gameState;
        public PlayerToolController ToolController => toolController;
        public FirstPersonController PlayerController => playerController;
        public RugCleaningController Rug => rug;
        public InspectionPanelUI Inspection => inspection;
        public OrderManager Orders => orders;
        public WalletManager Wallet => wallet;
        public ReputationManager Reputation => reputation;
        public UpgradeManager Upgrades => upgrades;
        public ShopManager Shop => shop;
        public OrderBoardUI OrderBoard => orderBoard;
        public ShopUI ShopUI => shopUI;
        public ResultsUI Results => results;
        public SettingsUI SettingsUI => settingsUI;
        public SaveManager SaveSystem => saveManager;
        public SettingsManager Settings => settingsManager;
        public HUDController Hud => hud;
        public bool IsReady { get; private set; }

        /// <summary>Vérifie toutes les références, y compris celles des composants enfants.</summary>
        public bool ValidateReferences(out string missing)
        {
            var problems = new List<string>();
            void Require(Object value, string label)
            {
                if (value == null) problems.Add(label);
            }

            Require(gameState, nameof(gameState));
            Require(audioManager, nameof(audioManager));
            Require(saveManager, nameof(saveManager));
            Require(settingsManager, nameof(settingsManager));
            Require(settingsUI, nameof(settingsUI));
            Require(input, nameof(input));
            Require(playerController, nameof(playerController));
            Require(toolController, nameof(toolController));
            Require(interactor, nameof(interactor));
            Require(rug, nameof(rug));
            Require(snapshots, nameof(snapshots));
            Require(orders, nameof(orders));
            Require(wallet, nameof(wallet));
            Require(reputation, nameof(reputation));
            Require(upgrades, nameof(upgrades));
            Require(shop, nameof(shop));
            Require(hud, nameof(hud));
            Require(toolbar, nameof(toolbar));
            Require(pauseMenu, nameof(pauseMenu));
            Require(inspection, nameof(inspection));
            Require(orderBoard, nameof(orderBoard));
            Require(shopUI, nameof(shopUI));
            Require(results, nameof(results));
            Require(debugOverlay, nameof(debugOverlay));
            if (availableRugs.Count == 0 || availableRugs.Contains(null)) problems.Add(nameof(availableRugs));

            string detail;
            if (playerController != null && !playerController.IsConfigured(out detail)) problems.Add($"playerController.{detail}");
            if (toolController != null && !toolController.IsConfigured(out detail)) problems.Add($"toolController.{detail}");
            if (interactor != null && !interactor.IsConfigured(out detail)) problems.Add($"interactor.{detail}");
            if (rug != null && !rug.IsConfigured(out detail)) problems.Add($"rug.{detail}");
            if (rug != null && rug.DirtRenderer != null && !rug.DirtRenderer.IsConfigured(out detail)) problems.Add($"rug.dirtRenderer.{detail}");
            if (snapshots != null && !snapshots.IsConfigured(out detail)) problems.Add($"snapshots.{detail}");
            if (orders != null && !orders.IsConfigured(out detail)) problems.Add($"orders.{detail}");
            if (upgrades != null && !upgrades.IsConfigured(out detail)) problems.Add($"upgrades.{detail}");
            if (shop != null && !shop.IsConfigured(out detail)) problems.Add($"shop.{detail}");
            if (hud != null && !hud.IsConfigured(out detail)) problems.Add($"hud.{detail}");
            if (toolbar != null && !toolbar.IsConfigured(out detail)) problems.Add($"toolbar.{detail}");
            if (pauseMenu != null && !pauseMenu.IsConfigured(out detail)) problems.Add($"pauseMenu.{detail}");
            if (inspection != null && !inspection.IsConfigured(out detail)) problems.Add($"inspection.{detail}");
            if (orderBoard != null && !orderBoard.IsConfigured(out detail)) problems.Add($"orderBoard.{detail}");
            if (shopUI != null && !shopUI.IsConfigured(out detail)) problems.Add($"shopUI.{detail}");
            if (results != null && !results.IsConfigured(out detail)) problems.Add($"results.{detail}");
            if (settingsUI != null && !settingsUI.IsConfigured(out detail)) problems.Add($"settingsUI.{detail}");
            if (saveManager != null && !saveManager.IsConfigured(out detail)) problems.Add($"saveManager.{detail}");
            if (debugOverlay != null && !debugOverlay.IsConfigured(out detail)) problems.Add($"debugOverlay.{detail}");

            missing = string.Join(", ", problems);
            return problems.Count == 0;
        }

        void Awake()
        {
            if (!ValidateReferences(out string missing))
            {
                Debug.LogError($"[RugWash] Scène incomplète, références manquantes : {missing}. " +
                               "Regénérez la scène via RugWash/Setup/Générer la Scène Atelier Complète.", this);
                enabled = false;
                return;
            }
            // Ordre = priorité de fermeture par Échap (les paramètres s'ouvrent par-dessus la pause).
            modalPanels = new IModalPanel[] { settingsUI, inspection, orderBoard, shopUI };
            IsReady = true;
        }

        void Start()
        {
            if (!IsReady) return;
            audioManager.Bind(gameState);
            hud.Bind(toolController, interactor, input, rug, gameState);
            hud.BindEconomy(wallet, reputation, orders);
            toolbar.Bind(toolController);
            pauseMenu.Bind(gameState, rug, orders, availableRugs, settingsUI, saveManager);
            settingsUI.Bind(settingsManager, saveManager, gameState);
            saveManager.Saved += OnSaved;
            orders.ObjectiveReached += OnObjectiveReached;
            inspection.Bind(gameState, rug, snapshots);
            orderBoard.Bind(gameState, orders, wallet);
            shopUI.Bind(gameState, shop);
            results.Bind(gameState, orders, snapshots, orderBoard);
            debugOverlay.Bind(gameState, rug, toolController, input);
            audioManager.RegisterButtons(pauseMenu);
            audioManager.RegisterButtons(inspection);
            audioManager.RegisterButtons(orderBoard);
            audioManager.RegisterButtons(shopUI);
            audioManager.RegisterButtons(results);
            audioManager.RegisterButtons(settingsUI);

            gameState.StateChanged += OnGameStateChanged;
            gameState.ModalsChanged += SyncGameplayInput;
            SyncGameplayInput();
        }

        void OnSaved(bool success, string message) => hud.ShowToast(message);

        void OnObjectiveReached(OrderDefinition order)
        {
            hud.ShowToast("Objectif de propreté atteint — livrez la commande au comptoir");
            audioManager.PlayInterface(audioManager.ObjectiveClip);
        }

        void OnDestroy()
        {
            if (saveManager != null) saveManager.Saved -= OnSaved;
            if (orders != null) orders.ObjectiveReached -= OnObjectiveReached;
            if (gameState == null) return;
            gameState.StateChanged -= OnGameStateChanged;
            gameState.ModalsChanged -= SyncGameplayInput;
        }

        void Update()
        {
            if (!IsReady) return;
            if (settingsUI.IsRebinding) return;

            if (input.PausePressed)
            {
                if (!CloseOpenPanel()) gameState.TogglePause();
            }
            else if (input.InspectPressed)
            {
                if (inspection.IsOpen) inspection.Close();
                else if (!AnyPanelOpen()) inspection.Open();
            }
        }

        bool AnyPanelOpen()
        {
            foreach (var panel in modalPanels)
                if (panel.IsOpen) return true;
            return false;
        }

        bool CloseOpenPanel()
        {
            foreach (var panel in modalPanels)
            {
                if (!panel.IsOpen) continue;
                panel.Close();
                return true;
            }
            return false;
        }

        void OnGameStateChanged(GameState previous, GameState next) => SyncGameplayInput();

        void SyncGameplayInput() => input.SetGameplayEnabled(gameState.IsGameplayActive);
    }
}
