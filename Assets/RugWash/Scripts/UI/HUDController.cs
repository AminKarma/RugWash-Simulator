using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Effects;
using RugWash.Orders;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// HUD discret : outil actif, ressource, propreté (lue dans la grille), invite d'interaction,
    /// aide contextuelle, messages courts, état du scan de saleté. Les jauges suivent leur valeur réelle
    /// avec un léger lissage ; les paliers de propreté font « pulser » le panneau. Textes rafraîchis à fréquence limitée.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HUDController : MonoBehaviour
    {
        [SerializeField] CanvasGroup root;
        [SerializeField] Text toolNameLabel;
        [SerializeField] Text toolStatusLabel;
        [SerializeField] Image resourceFill;
        [SerializeField] GameObject resourceBar;
        [SerializeField] Text cleanlinessLabel;
        [SerializeField] Image cleanlinessFill;
        [SerializeField] Text promptLabel;
        [SerializeField] Text hintLabel;
        [SerializeField] Text toastLabel;
        [SerializeField] Text economyLabel;
        [SerializeField] Text orderLabel;
        [SerializeField] RectTransform cleanlinessPanel;
        [SerializeField] Text scanLabel;
        [SerializeField] Image scanFill;
        [SerializeField] Text workSurfaceLabel;
        [SerializeField] Color gainColor = new Color(0.75f, 1f, 0.85f, 1f);
        [SerializeField] float barSmoothing = 10f;
        [SerializeField] float refreshInterval = 0.1f;
        [SerializeField] float toastDuration = 2.5f;

        PlayerToolController tools;
        PlayerInteractor interactor;
        PlayerInputReader input;
        RugCleaningController rug;
        GameStateManager gameState;
        WalletManager wallet;
        ReputationManager reputation;
        OrderManager orders;
        DirtScanner scanner;
        WorkSurface workSurface;
        bool workSurfaceWasClean = true;
        CleaningProgressFeedback progress;
        Color cleanlinessBaseColor;
        float displayedCleanliness;
        float displayedResource = -1f;
        float gainFlash;
        float pulse;
        string lastScan;

        float nextRefresh;
        float toastUntil;
        int lastCleanPercent = -1;
        string lastPrompt, lastStatus, lastBlock;
        CleaningToolBase lastTool;

        public bool IsConfigured(out string missing)
        {
            missing = root == null ? nameof(root)
                : toolNameLabel == null ? nameof(toolNameLabel)
                : toolStatusLabel == null ? nameof(toolStatusLabel)
                : resourceFill == null ? nameof(resourceFill)
                : resourceBar == null ? nameof(resourceBar)
                : cleanlinessLabel == null ? nameof(cleanlinessLabel)
                : cleanlinessFill == null ? nameof(cleanlinessFill)
                : promptLabel == null ? nameof(promptLabel)
                : hintLabel == null ? nameof(hintLabel)
                : toastLabel == null ? nameof(toastLabel)
                : economyLabel == null ? nameof(economyLabel)
                : orderLabel == null ? nameof(orderLabel)
                : cleanlinessPanel == null ? nameof(cleanlinessPanel)
                : scanLabel == null ? nameof(scanLabel)
                : scanFill == null ? nameof(scanFill)
                : workSurfaceLabel == null ? nameof(workSurfaceLabel) : null;
            return missing == null;
        }

        public void Bind(PlayerToolController toolController, PlayerInteractor playerInteractor, PlayerInputReader inputReader,
            RugCleaningController rugController, GameStateManager state)
        {
            tools = toolController;
            interactor = playerInteractor;
            input = inputReader;
            rug = rugController;
            gameState = state;

            interactor.Interacted += ShowToast;
            tools.LockedToolRequested += OnLockedToolRequested;
            gameState.StateChanged += OnStateChanged;
            gameState.ModalsChanged += RefreshVisibility;
            toastLabel.text = string.Empty;
            cleanlinessBaseColor = cleanlinessFill.color;
            displayedCleanliness = rug != null ? rug.Cleanliness01 : 0f;
            RefreshVisibility();
            RefreshAll(force: true);
        }

        /// <summary>Affichage du solde, de la réputation et de la commande active.</summary>
        public void BindEconomy(WalletManager walletManager, ReputationManager reputationManager, OrderManager orderManager)
        {
            wallet = walletManager;
            reputation = reputationManager;
            orders = orderManager;
            wallet.Wallet.Changed += OnWalletChanged;
            reputation.Changed += OnReputationChanged;
            RefreshEconomy();
        }

        /// <summary>Scan de saleté et paliers de propreté.</summary>
        public void BindFeedback(DirtScanner dirtScanner, CleaningProgressFeedback cleaningProgress)
        {
            scanner = dirtScanner;
            progress = cleaningProgress;
            scanner.Scanned += OnScanned;
            scanner.Denied += OnScanDenied;
            progress.MilestoneReached += OnMilestone;
        }

        void OnScanned(DirtScanResult result) => ShowToast(DirtScan.Describe(result));

        void OnLockedToolRequested(CleaningToolBase tool) =>
            ShowToast($"{tool.Definition.displayName} : à acheter au meuble « Matériel & boutique »");

        /// <summary>État du plan de travail de l'aire de lavage (ligne d'alerte quand il est sale).</summary>
        public void BindWorkSurface(WorkSurface surface)
        {
            workSurface = surface;
            workSurface.StatusChanged += OnWorkSurfaceChanged;
            workSurface.Contaminated += OnContaminated;
            OnWorkSurfaceChanged(workSurface.Status);
        }

        void OnWorkSurfaceChanged(WorkSurfaceStatus status)
        {
            workSurfaceLabel.text = status.IsClean ? string.Empty
                : $"Aire de lavage sale ({status.SoiledPercent} %) — rincez (4) puis balayez vers le caniveau (7)";
            if (status.IsClean && !workSurfaceWasClean) ShowToast("Aire de lavage propre");
            workSurfaceWasClean = status.IsClean;
        }

        void OnContaminated(float amount) =>
            ShowToast("Tapis posé sur une aire de lavage sale : ses bords sont recontaminés");

        /// <summary>Texte de l'alerte du plan de travail (vide s'il est propre).</summary>
        public string WorkSurfaceAlert => workSurfaceLabel.text;

        void OnScanDenied(float remaining) =>
            ShowToast(remaining > 0f ? $"Scan en recharge — encore {Mathf.CeilToInt(remaining)} s" : "Scan déjà en cours");

        void OnMilestone(float reached)
        {
            pulse = 1f;
            gainFlash = 1f;
            ShowToast(reached >= 0.9f ? $"Propreté {reached * 100f:0} % — presque parfait !" : $"Propreté {reached * 100f:0} % — continuez !");
        }

        /// <summary>Intensité de l'animation de palier (1 au déclenchement, 0 au repos).</summary>
        public float MilestonePulse => pulse;

        void OnWalletChanged(int balance, int delta) => RefreshEconomy();
        void OnReputationChanged(int points) => RefreshEconomy();

        void RefreshEconomy()
        {
            if (wallet == null) return;
            economyLabel.text = $"{WalletManager.Format(wallet.Balance)}\nRéputation {reputation.Points}";
            var order = orders.ActiveOrder;
            orderLabel.text = order == null
                ? "Aucune commande — comptoir de réception (E)"
                : $"{order.title} · objectif {order.objectives.targetCleanliness * 100f:0} % · {OrderEvaluator.FormatTime(orders.ElapsedSeconds)}\n" +
                  "Livrer au comptoir de réception (E)";
        }

        void OnDestroy()
        {
            if (interactor != null) interactor.Interacted -= ShowToast;
            if (tools != null) tools.LockedToolRequested -= OnLockedToolRequested;
            if (workSurface != null)
            {
                workSurface.StatusChanged -= OnWorkSurfaceChanged;
                workSurface.Contaminated -= OnContaminated;
            }
            if (wallet != null && wallet.Wallet != null) wallet.Wallet.Changed -= OnWalletChanged;
            if (reputation != null) reputation.Changed -= OnReputationChanged;
            if (scanner != null)
            {
                scanner.Scanned -= OnScanned;
                scanner.Denied -= OnScanDenied;
            }
            if (progress != null) progress.MilestoneReached -= OnMilestone;
            if (gameState != null)
            {
                gameState.StateChanged -= OnStateChanged;
                gameState.ModalsChanged -= RefreshVisibility;
            }
        }

        void OnStateChanged(GameState previous, GameState next) => RefreshVisibility();

        public void ShowToast(string message)
        {
            toastLabel.text = message;
            toastUntil = Time.unscaledTime + toastDuration;
        }

        void RefreshVisibility()
        {
            if (gameState == null) return;
            root.alpha = gameState.IsGameplayActive ? 1f : 0.35f;
        }

        void Update()
        {
            if (tools == null) return;

            // Barres : mises à jour chaque frame (pas d'allocation), lissées vers la valeur réelle.
            float dt = Time.unscaledDeltaTime;
            float smoothing = 1f - Mathf.Exp(-barSmoothing * dt);
            float cleanliness = rug != null ? rug.Cleanliness01 : 0f;
            if (cleanliness > displayedCleanliness + 0.0005f) gainFlash = Mathf.Max(gainFlash, 0.45f);
            displayedCleanliness = cleanliness < displayedCleanliness ? cleanliness : Mathf.Lerp(displayedCleanliness, cleanliness, smoothing);
            cleanlinessFill.fillAmount = displayedCleanliness;
            gainFlash = Mathf.MoveTowards(gainFlash, 0f, dt * 1.5f);
            cleanlinessFill.color = Color.Lerp(cleanlinessBaseColor, gainColor, gainFlash);

            pulse = Mathf.MoveTowards(pulse, 0f, dt / 0.4f);
            cleanlinessPanel.localScale = Vector3.one * (1f + 0.07f * Mathf.Sin(pulse * Mathf.PI));

            var tool = tools.ActiveTool;
            float fraction = tool != null ? tool.GetResourceFraction() : -1f;
            if (fraction >= 0f)
            {
                displayedResource = tool != lastTool || displayedResource < 0f ? fraction : Mathf.Lerp(displayedResource, fraction, smoothing);
                resourceFill.fillAmount = displayedResource;
            }

            if (scanner != null)
            {
                scanFill.fillAmount = scanner.IsActive ? scanner.ActiveRemaining / scanner.Duration : scanner.Readiness01;
            }

            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = string.Empty;

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            RefreshAll(force: false);
        }

        void RefreshAll(bool force)
        {
            RefreshEconomy();
            var tool = tools.ActiveTool;

            int percent = Mathf.FloorToInt((rug != null ? rug.Cleanliness01 : 0f) * 100f);
            if (force || percent != lastCleanPercent)
            {
                lastCleanPercent = percent;
                cleanlinessLabel.text = $"Propreté du tapis : {percent} %";
            }

            if (force || tool != lastTool)
            {
                lastTool = tool;
                toolNameLabel.text = tool != null ? tool.Definition.displayName : "Aucun outil";
                resourceBar.SetActive(tool != null && tool.GetResourceFraction() >= 0f);
                hintLabel.text = BuildHint(tool);
            }

            if (scanner != null)
            {
                string key = input.GetBindingLabel("Scan");
                string scan = scanner.IsActive ? $"Scan actif · {Mathf.CeilToInt(scanner.ActiveRemaining)} s"
                    : scanner.IsReady ? $"[{key}] Scanner la saleté" : $"Scan : recharge {Mathf.CeilToInt(scanner.CooldownRemaining)} s";
                if (force || scan != lastScan)
                {
                    lastScan = scan;
                    scanLabel.text = scan;
                }
            }

            string status = tool?.GetStatusLine() ?? string.Empty;
            if (force || status != lastStatus)
            {
                lastStatus = status;
                toolStatusLabel.text = status;
            }

            string prompt = BuildPrompt(tool);
            if (force || prompt != lastPrompt || tool?.GetBlockReason() != lastBlock)
            {
                lastPrompt = prompt;
                lastBlock = tool?.GetBlockReason();
                promptLabel.text = prompt;
            }
        }

        string BuildPrompt(CleaningToolBase tool)
        {
            if (interactor.Current != null)
                return $"[{input.GetBindingLabel("Interact")}] {interactor.Current.GetPrompt(interactor)}";
            string block = tool?.GetBlockReason();
            if (block != null) return block;
            // « Trop loin » seulement en visant le tapis de la commande, ou en utilisant l'outil (le sol est aussi nettoyable).
            if (tool != null && tool.IsTargetOutOfRange && (tool.IsUsing || tools.CurrentAim.Rug == rug)) return "Trop loin — rapprochez-vous";
            return string.Empty;
        }

        string BuildHint(CleaningToolBase tool)
        {
            string verb = tool != null ? tool.ActionVerb : "utiliser";
            return $"Clic gauche : {verb}   ·   1-8 / molette : outils   ·   " +
                   $"{input.GetBindingLabel("Interact")} : interagir   ·   {input.GetBindingLabel("Scan")} : scanner   ·   " +
                   $"{input.GetBindingLabel("Inspect")} : inspecter   ·   Échap : pause";
        }
    }
}
