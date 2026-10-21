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
        [Tooltip("Musique de fond de l'atelier (lecteur unique conservé entre les scènes).")]
        [SerializeField] AudioClip workshopMusic;
        [SerializeField] SaveManager saveManager;
        [SerializeField] SettingsManager settingsManager;

        [Header("Joueur")]
        [SerializeField] PlayerInputReader input;
        [SerializeField] InputDeviceMonitor inputMonitor;
        [SerializeField] UINavigator navigator;
        [SerializeField] GamepadRumble rumble;
        [SerializeField] FirstPersonController playerController;
        [SerializeField] PlayerToolController toolController;
        [SerializeField] PlayerInteractor interactor;

        [Header("Tapis")]
        [SerializeField] RugCleaningController rug;
        [SerializeField] RugSnapshotService snapshots;
        [SerializeField] DirtScanner dirtScanner;
        [SerializeField] CleaningProgressFeedback progressFeedback;
        [SerializeField] WorkSurface workSurface;
        [SerializeField] Workshop.RobotVacuum robot;
        [Tooltip("Machines fonctionnelles de l'atelier (v0.1.2).")]
        [SerializeField] List<Workshop.WorkshopMachine> machines = new List<Workshop.WorkshopMachine>();
        [Tooltip("Décor de l'atelier par paliers (v0.1.2).")]
        [SerializeField] Workshop.WorkshopDecor decor;
        [Tooltip("Agrandissement de l'atelier (grande mise à jour) : annexe, zone de lavage 2 et machine à laver.")]
        [SerializeField] Workshop.WorkshopExtension extension;
        [SerializeField] WorkshopStateService workshopState;
        [Tooltip("Farces entre coéquipiers (v0.1.2) : autorité et gadgets du joueur local.")]
        [SerializeField] Pranks.PrankService pranks;
        [SerializeField] Pranks.PrankGadgetController gadgets;
        [SerializeField] RugDex.RugDexService rugDex;
        [SerializeField] RugDex.RugDexUI rugDexUI;
        [SerializeField] ProductInventory productInventory;
        [SerializeField] DirtTypeLibrary dirtLibrary;
        [Tooltip("Définitions de tapis connues de la scène (réplication réseau : un invité recharge le tapis annoncé par l'hôte).")]
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
        [Tooltip("Ordinateur de l'atelier : seul accès aux missions et à la boutique.")]
        [SerializeField] ComputerUI computer;
        [SerializeField] ResultsUI results;
        [SerializeField] SettingsUI settingsUI;
        [SerializeField] DebugOverlay debugOverlay;
        [Header("Inventaire et casier (grande mise à jour)")]
        [SerializeField] PlayerInventory inventory;
        [SerializeField] LockerUI lockerUI;
        [SerializeField] WashingMachineUI washerUI;
        [Header("En ligne (grande mise à jour)")]
        [SerializeField] ChatUI chatUI;
        [SerializeField] PlayersUI playersUI;
        [SerializeField] Network.VoiceChat voice;
        [Tooltip("Icônes 3D des outils et gadgets (barre d'outils, casier, boutique).")]
        [SerializeField] Effects.ToolIconRenderer toolIcons;

        IModalPanel[] modalPanels;

        public GameStateManager GameState => gameState;
        /// <summary>Modèles et icônes 3D des objets (aussi copies posées au sol, V0.2.5) ; null si absent.</summary>
        public Effects.ToolIconRenderer ToolIcons => toolIcons;
        public PlayerToolController ToolController => toolController;
        /// <summary>Stock de détergent en bidons du joueur local.</summary>
        public DetergentSupply Detergent => toolController.Equipment.Detergent;
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
        public ComputerUI Computer => computer;
        public ResultsUI Results => results;
        public SettingsUI SettingsUI => settingsUI;
        public SaveManager SaveSystem => saveManager;
        public PlayerInventory Inventory => inventory;
        public ToolSelectionUI Toolbar => toolbar;
        public LockerUI Locker => lockerUI;
        public SettingsManager Settings => settingsManager;
        public HUDController Hud => hud;
        public DirtScanner DirtScanner => dirtScanner;
        public CleaningProgressFeedback ProgressFeedback => progressFeedback;
        public WorkSurface WorkSurface => workSurface;
        public Workshop.RobotVacuum Robot => robot;
        public IReadOnlyList<Workshop.WorkshopMachine> Machines => machines;
        public Workshop.WorkshopDecor Decor => decor;
        /// <summary>Agrandissement de l'atelier : zone de lavage 2 et machine à laver.</summary>
        public Workshop.WorkshopExtension Extension => extension;
        public Workshop.WashingMachine Washer => extension != null ? extension.Washer : null;
        public WashingMachineUI WasherUI => washerUI;
        public ChatUI Chat => chatUI;
        public PlayersUI Players => playersUI;
        /// <summary>Micro du chat vocal du joueur local (actif seulement en ligne).</summary>
        public Network.VoiceChat Voice => voice;
        /// <summary>Autorité des farces (solo, hôte).</summary>
        public Pranks.PrankService Pranks => pranks;
        /// <summary>Gadgets de farce du joueur local.</summary>
        public Pranks.PrankGadgetController Gadgets => gadgets;

        /// <summary>
        /// Invité en ligne : l'atelier affiché (décor, machines, robot) est celui de l'hôte et arrive par le réseau ;
        /// les achats de ce joueur restent dans sa carrière sans changer l'atelier affiché.
        /// </summary>
        public bool WorkshopFromHost { get; set; }
        PlayerKnockdown knockdown;
        /// <summary>Chute du joueur local (ragdoll, v0.1.2).</summary>
        public PlayerKnockdown Knockdown => knockdown;

        public Workshop.WorkshopMachine FindMachine(string machineId)
        {
            foreach (var machine in machines)
                if (machine != null && machine.MachineId == machineId) return machine;
            return null;
        }
        public RugDex.RugDexService RugDexService => rugDex;
        public RugDex.RugDexUI RugDexUI => rugDexUI;
        public ProductInventory Products => productInventory;
        public PlayerInputReader Input => input;
        public IReadOnlyList<RugDefinition> AvailableRugs => availableRugs;
        /// <summary>Sol de la salle : surface décorative hors zone de nettoyage (ne se salit jamais).</summary>
        public RugCleaningController RoomFloor => workshopState != null ? workshopState.RoomFloor : null;
        public InputDeviceMonitor InputMonitor => inputMonitor;
        public UINavigator Navigator => navigator;
        public GamepadRumble Rumble => rumble;
        public PauseMenuUI PauseMenu => pauseMenu;
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
            Require(inputMonitor, nameof(inputMonitor));
            Require(navigator, nameof(navigator));
            Require(rumble, nameof(rumble));
            Require(playerController, nameof(playerController));
            Require(toolController, nameof(toolController));
            Require(interactor, nameof(interactor));
            Require(rug, nameof(rug));
            Require(snapshots, nameof(snapshots));
            Require(dirtScanner, nameof(dirtScanner));
            Require(progressFeedback, nameof(progressFeedback));
            Require(workSurface, nameof(workSurface));
            Require(robot, nameof(robot));
            Require(workshopState, nameof(workshopState));
            Require(pranks, nameof(pranks));
            Require(gadgets, nameof(gadgets));
            Require(rugDex, nameof(rugDex));
            Require(rugDexUI, nameof(rugDexUI));
            Require(productInventory, nameof(productInventory));
            Require(dirtLibrary, nameof(dirtLibrary));
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
            Require(computer, nameof(computer));
            Require(results, nameof(results));
            Require(debugOverlay, nameof(debugOverlay));
            Require(inventory, nameof(inventory));
            Require(lockerUI, nameof(lockerUI));
            Require(extension, nameof(extension));
            Require(washerUI, nameof(washerUI));
            Require(chatUI, nameof(chatUI));
            Require(playersUI, nameof(playersUI));
            Require(voice, nameof(voice));
            Require(toolIcons, nameof(toolIcons));
            if (chatUI != null && !chatUI.IsConfigured(out string chatMissing)) problems.Add($"chatUI.{chatMissing}");
            if (playersUI != null && !playersUI.IsConfigured(out string playersMissing)) problems.Add($"playersUI.{playersMissing}");
            if (extension != null && !extension.IsConfigured(out string extensionMissing)) problems.Add($"extension.{extensionMissing}");
            if (extension != null && extension.Washer != null && !extension.Washer.IsConfigured(out string washerMissing)) problems.Add($"extension.washer.{washerMissing}");
            if (washerUI != null && !washerUI.IsConfigured(out string washerUIMissing)) problems.Add($"washerUI.{washerUIMissing}");
            if (inventory != null && !inventory.IsConfigured(out string inventoryMissing)) problems.Add($"inventory.{inventoryMissing}");
            if (lockerUI != null && !lockerUI.IsConfigured(out string lockerMissing)) problems.Add($"lockerUI.{lockerMissing}");
            if (availableRugs.Count == 0 || availableRugs.Contains(null)) problems.Add(nameof(availableRugs));

            string detail;
            if (playerController != null && !playerController.IsConfigured(out detail)) problems.Add($"playerController.{detail}");
            if (toolController != null && !toolController.IsConfigured(out detail)) problems.Add($"toolController.{detail}");
            if (interactor != null && !interactor.IsConfigured(out detail)) problems.Add($"interactor.{detail}");
            if (rug != null && !rug.IsConfigured(out detail)) problems.Add($"rug.{detail}");
            if (rug != null && rug.DirtRenderer != null && !rug.DirtRenderer.IsConfigured(out detail)) problems.Add($"rug.dirtRenderer.{detail}");
            if (snapshots != null && !snapshots.IsConfigured(out detail)) problems.Add($"snapshots.{detail}");
            if (dirtScanner != null && !dirtScanner.IsConfigured(out detail)) problems.Add($"dirtScanner.{detail}");
            if (progressFeedback != null && !progressFeedback.IsConfigured(out detail)) problems.Add($"progressFeedback.{detail}");
            if (workSurface != null && !workSurface.IsConfigured(out detail)) problems.Add($"workSurface.{detail}");
            if (robot != null && !robot.IsConfigured(out detail)) problems.Add($"robot.{detail}");
            if (workshopState != null && !workshopState.IsConfigured(out detail)) problems.Add($"workshopState.{detail}");
            if (pranks != null && !pranks.IsConfigured(out detail)) problems.Add($"pranks.{detail}");
            if (gadgets != null && !gadgets.IsConfigured(out detail)) problems.Add($"gadgets.{detail}");
            if (rugDex != null && !rugDex.IsConfigured(out detail)) problems.Add($"rugDex.{detail}");
            if (rugDexUI != null && !rugDexUI.IsConfigured(out detail)) problems.Add($"rugDexUI.{detail}");
            if (productInventory != null && !productInventory.IsConfigured(out detail)) problems.Add($"productInventory.{detail}");
            if (orders != null && !orders.IsConfigured(out detail)) problems.Add($"orders.{detail}");
            if (upgrades != null && !upgrades.IsConfigured(out detail)) problems.Add($"upgrades.{detail}");
            if (shop != null && !shop.IsConfigured(out detail)) problems.Add($"shop.{detail}");
            if (hud != null && !hud.IsConfigured(out detail)) problems.Add($"hud.{detail}");
            if (toolbar != null && !toolbar.IsConfigured(out detail)) problems.Add($"toolbar.{detail}");
            if (pauseMenu != null && !pauseMenu.IsConfigured(out detail)) problems.Add($"pauseMenu.{detail}");
            if (inspection != null && !inspection.IsConfigured(out detail)) problems.Add($"inspection.{detail}");
            if (orderBoard != null && !orderBoard.IsConfigured(out detail)) problems.Add($"orderBoard.{detail}");
            if (shopUI != null && !shopUI.IsConfigured(out detail)) problems.Add($"shopUI.{detail}");
            if (computer != null && !computer.IsConfigured(out detail)) problems.Add($"computer.{detail}");
            if (results != null && !results.IsConfigured(out detail)) problems.Add($"results.{detail}");
            if (settingsUI != null && !settingsUI.IsConfigured(out detail)) problems.Add($"settingsUI.{detail}");
            if (saveManager != null && !saveManager.IsConfigured(out detail)) problems.Add($"saveManager.{detail}");
            if (debugOverlay != null && !debugOverlay.IsConfigured(out detail)) problems.Add($"debugOverlay.{detail}");
            if (rumble != null && !rumble.IsConfigured(out detail)) problems.Add($"rumble.{detail}");
            if (input != null && input.Monitor == null) problems.Add("input.monitor");

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
            modalPanels = new IModalPanel[] { chatUI, settingsUI, rugDexUI, inspection, orderBoard, shopUI, computer, lockerUI, washerUI, playersUI };
            // V0.4.2 : modèles des icônes et des objets au sol déclarés dès Awake. L'atelier réseau peut apparaître avant le
            // Start de la scène (arrivée en cours de partie) : les objets déjà au sol étaient alors créés sans modèle, donc
            // invisibles chez le nouvel arrivant (seul le ramassage restait possible).
            foreach (var tool in toolController.Tools)
                if (tool != null && tool.Definition != null) toolIcons.Register(tool.Definition.id, tool.ViewModel);
            toolIcons.Register(InventoryRules.GadgetId(RugWash.Pranks.PrankGadget.Bat), gadgets.BatModel);
            toolIcons.Register(InventoryRules.GadgetId(RugWash.Pranks.PrankGadget.Blaster), gadgets.BlasterModel);
            toolIcons.Register(InventoryRules.GadgetId(RugWash.Pranks.PrankGadget.Dynamite), gadgets.DynamiteModel);
            IsReady = true;
        }

        void Start()
        {
            if (!IsReady) return;
            // Budget des effets visuels appliqué une fois à tous les systèmes de particules de la scène (créés à la
            // génération, jamais instanciés en jeu).
            EffectBudget.Apply(rug.Tuning);
            foreach (var system in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include)) EffectBudget.Configure(system);
            audioManager.Bind(gameState);
            hud.Bind(toolController, interactor, input, rug, gameState);
            hud.BindEconomy(wallet, reputation, orders);
            hud.BindProfile(saveManager);
            hud.BindFeedback(dirtScanner, progressFeedback);
            // Zones de lavage (grande mise à jour) : la zone courante est celle où se trouve le joueur local.
            orders.Viewer = playerController.transform;
            gadgets.Orders = orders;
            hud.BindWorkSurface(workSurface, orders.WorkSurfaceOf(1));
            hud.BindWasher(Washer);
            washerUI.Bind(gameState);
            chatUI.Bind(gameState);
            playersUI.Bind(gameState);
            pauseMenu.BindPlayers(playersUI);
            // Icônes 3D : modèles déclarés dans Awake, photographiés une fois les achats appliqués (outils dorés compris).
            toolbar.BindIcons(toolIcons);
            lockerUI.BindIcons(toolIcons);
            shopUI.BindIcons(toolIcons);
            settingsUI.BindVoice(voice);
            StartCoroutine(RenderIconsWhenReady());
            voice.CanTalk = () => gameState.IsGameplayActive;
            hud.BindVoice(voice);
            ApplyVoiceSettings();
            settingsManager.Changed += ApplyVoiceSettings;
            if (Washer != null) Washer.Message += hud.ShowToast;
            toolbar.Bind(toolController, inventory, gadgets);
            upgrades.Changed += toolbar.Refresh;
            lockerUI.Bind(inventory, gameState);
            inventory.Message += hud.ShowToast;
            robot.Message += hud.ShowToast;
            // Robot aspirateur, machines et décor : selon les achats (en ligne, l'invité suit l'atelier de l'hôte).
            SyncRobotOwnership();
            upgrades.Changed += SyncRobotOwnership;
            foreach (var machine in machines)
                if (machine != null) machine.Message += hud.ShowToast;
            knockdown = toolController.GetComponent<PlayerKnockdown>();
            if (knockdown != null) knockdown.Message += hud.ShowToast;
            gadgets.Message += hud.ShowToast;
            hud.BindGadgets(gadgets);
            orderBoard.SetWorkSurface(workSurface);
            hud.BindProducts(productInventory.Book);
            rugDexUI.Bind(rugDex.Catalog, rugDex.Book, orders.Book.Orders, dirtLibrary, gameState);
            pauseMenu.BindRugDex(rugDexUI);
            rugDex.NewRugDiscovered += OnNewRugDiscovered;
            pauseMenu.Bind(gameState, rug, orders, settingsUI, saveManager);
            pauseMenu.BindProgress(reputation);
            settingsUI.Bind(settingsManager, saveManager, gameState);
            saveManager.Saved += OnSaved;
            orders.ObjectiveReached += OnObjectiveReached;
            inspection.Bind(gameState, rug, snapshots, orders);
            orderBoard.Bind(gameState, orders, wallet);
            shopUI.Bind(gameState, shop);
            computer.Bind(gameState, orderBoard, shopUI, orders, wallet, toolController.Equipment.Detergent);
            results.Bind(gameState, orders, snapshots, orderBoard);
            debugOverlay.Bind(gameState, rug, toolController, input);
            rumble.Bind(orders, progressFeedback, dirtScanner);
            audioManager.RegisterButtons(pauseMenu);
            audioManager.RegisterButtons(inspection);
            audioManager.RegisterButtons(orderBoard);
            audioManager.RegisterButtons(shopUI);
            audioManager.RegisterButtons(computer);
            audioManager.RegisterButtons(results);
            audioManager.RegisterButtons(settingsUI);
            audioManager.RegisterButtons(rugDexUI);
            audioManager.RegisterButtons(washerUI);
            audioManager.RegisterButtons(playersUI);

            gameState.StateChanged += OnGameStateChanged;
            var music = MusicPlayer.Ensure();
            music.Ducked = false;
            music.Play(workshopMusic);
            gameState.ModalsChanged += SyncGameplayInput;
            SyncGameplayInput();

            // Invitation Steam acceptée pendant une partie solo : sortie propre par le menu pause (sauvegarde, menu).
            session = Network.MultiplayerSession.Find();
            if (session != null) session.SoloExitRequested += OnSoloExitRequested;
        }

        Network.MultiplayerSession session;

        void SyncRobotOwnership()
        {
            if (WorkshopFromHost) return;
            if (robot != null && !robot.IsReplica) robot.SetOwned(upgrades.OwnsRobot);
            foreach (var machine in machines)
                if (machine != null) machine.SetOwned(upgrades.OwnsMachine(machine.MachineId));
            if (decor != null) decor.Apply(upgrades.WorkshopTier);
            if (extension != null)
                extension.Apply(upgrades.OwnsMachine(Workshop.WorkshopExtension.ExtensionId), upgrades.OwnsMachine(Workshop.WashingMachine.MachineId),
                    upgrades.OwnsMachine(Workshop.WashingMachine.FastProgramId));
        }

        int iconGoldenMask = -1;

        System.Collections.IEnumerator RenderIconsWhenReady()
        {
            // Une image : sauvegarde et achats appliqués (Start des autres systèmes), outils dorés compris.
            yield return null;
            RefreshToolIcons();
            upgrades.Changed += RefreshToolIcons;
        }

        /// <summary>Icônes rephotographiées seulement si un outil change d'aspect (doré).</summary>
        void RefreshToolIcons()
        {
            int mask = upgrades.GoldenMaskFor(upgrades.OwnedIds);
            if (mask == iconGoldenMask) return;
            iconGoldenMask = mask;
            toolIcons.RenderAll();
        }

        /// <summary>Paramètres du chat vocal : mode du micro et volume des voix des autres joueurs.</summary>
        void ApplyVoiceSettings()
        {
            var current = settingsManager.Current;
            voice.Mode = (Network.VoiceMode)Mathf.Clamp(current.voiceMode, 0, 2);
            voice.PreferredDevice = current.voiceInputDevice;
            Network.VoicePlayback.GlobalVolume = Mathf.Clamp01(current.voiceVolume);
        }

        void OnSoloExitRequested()
        {
            if (session != null && session.InSession) return;
            pauseMenu.ReturnToMainMenu();
        }

        void OnSaved(bool success, string message) => hud.ShowToast(message);

        void OnNewRugDiscovered(RugDefinition rugDefinition) => hud.ShowToast($"Nouveau tapis dans le RugDex : {rugDefinition.displayName}");

        void OnObjectiveReached(OrderDefinition order)
        {
            // Tapis terminé d'un coup (moins de 1 % restant) : le HUD affiche déjà « Tapis terminé ».
            var bayRug = orders.RugOf(orders.BayOf(order));
            if (bayRug == null || !bayRug.IsFinished)
                hud.ShowToast(orders.UnlockedBayCount > 1 && orders.BayOf(order) >= 0
                    ? $"Objectif atteint ({OrderManager.BayLabel(orders.BayOf(order)).ToLowerInvariant()}) — livrez la commande sur l'ordinateur"
                    : "Objectif de propreté atteint — livrez la commande sur l'ordinateur");
            audioManager.PlayInterface(audioManager.ObjectiveClip);
        }

        void OnDestroy()
        {
            if (saveManager != null) saveManager.Saved -= OnSaved;
            if (orders != null) orders.ObjectiveReached -= OnObjectiveReached;
            if (upgrades != null && toolbar != null) upgrades.Changed -= toolbar.Refresh;
            if (robot != null && hud != null) robot.Message -= hud.ShowToast;
            if (upgrades != null) upgrades.Changed -= SyncRobotOwnership;
            if (hud != null)
                foreach (var machine in machines)
                    if (machine != null) machine.Message -= hud.ShowToast;
            if (rugDex != null) rugDex.NewRugDiscovered -= OnNewRugDiscovered;
            if (gadgets != null && hud != null) gadgets.Message -= hud.ShowToast;
            if (Washer != null && hud != null) Washer.Message -= hud.ShowToast;
            if (settingsManager != null) settingsManager.Changed -= ApplyVoiceSettings;
            if (upgrades != null) upgrades.Changed -= RefreshToolIcons;
            if (session != null) session.SoloExitRequested -= OnSoloExitRequested;
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
            else if (input.BackPressed)
            {
                // Manette (B / Rond) : ferme la fenêtre du dessus, ou reprend la partie depuis la pause.
                if (!CloseOpenPanel() && gameState.IsPaused) gameState.Resume();
            }
            else if (input.InspectPressed)
            {
                if (inspection.IsOpen) inspection.Close();
                else if (!AnyPanelOpen()) inspection.Open();
            }
            else if (input.DropPressed && (knockdown == null || !knockdown.IsDown))
            {
                // V0.4.2 : l'objet tenu tombe devant le joueur ; les objets au sol sont partagés par la partie en ligne.
                var workshop = Network.NetworkWorkshop.Instance;
                if (workshop != null && workshop.IsSpawned) workshop.RequestDropHeld();
                else hud.ShowToast("Lâcher un objet : en partie en ligne seulement (en solo, rangez-le au casier)");
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

        void OnGameStateChanged(GameState previous, GameState next)
        {
            SyncGameplayInput();
            // Pause : musique atténuée (elle ne s'arrête pas net).
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.Ducked = gameState.IsPaused;
        }

        void SyncGameplayInput() => input.SetGameplayEnabled(gameState.IsGameplayActive);
    }
}
