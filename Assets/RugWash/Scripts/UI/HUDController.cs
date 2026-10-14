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
        [Tooltip("Optionnel : joueurs présents en partie en ligne.")]
        [SerializeField] Text playersLabel;
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
        ProductStockBook products;
        ProductDefinition lastProduct;
        float lastWear;
        float nextWearWarning;
        CleaningProgressFeedback progress;
        Color cleanlinessBaseColor;
        float displayedCleanliness;
        float displayedResource = -1f;
        float gainFlash;
        float pulse;
        string lastScan;

        float nextRefresh;
        float toastUntil;
        string loadingMessage;
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
            if (input.Monitor != null) input.Monitor.SchemeChanged += OnSchemeChanged;
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
            progress.RugFinished += OnRugFinished;
        }

        void OnScanned(DirtScanResult result) => ShowToast(DirtScan.Describe(result));

        void OnLockedToolRequested(CleaningToolBase tool) =>
            ShowToast($"{tool.Definition.displayName} : à acheter sur l'ordinateur (Boutique)");

        /// <summary>
        /// Indicateur permanent de l'aire de lavage (zone de nettoyage) : propreté, recontamination du tapis au-delà du seuil
        /// de saleté, et blocage des nouvelles commandes sous la propreté minimale.
        /// </summary>
        public void BindWorkSurface(WorkSurface surface)
        {
            workSurface = surface;
            workSurface.StatusChanged += OnWorkSurfaceChanged;
            workSurface.Contaminated += OnContaminated;
            OnWorkSurfaceChanged(workSurface.Status);
        }

        void OnWorkSurfaceChanged(WorkSurfaceStatus status)
        {
            workSurfaceLabel.text = DescribeWorkSurface(status);
            if (status.IsClean && !workSurfaceWasClean) ShowToast("Aire de lavage propre");
            workSurfaceWasClean = status.IsClean;
        }

        string DescribeWorkSurface(in WorkSurfaceStatus status)
        {
            if (loadingMessage != null || workSurface == null || workSurface.Surface == null || workSurface.Surface.Grid == null) return string.Empty;
            if (status.IsClean) return "Aire de lavage : propre";
            const string how = "rincez (outil 4) puis balayez vers le caniveau (outil 7)";
            if (!workSurface.AllowsNewOrder)
                return $"Aire de lavage trop sale : {status.CleanlinessPercent} % propre ({workSurface.MinCleanlinessPercent} % requis " +
                       $"pour une nouvelle commande) — {how}";
            if (workSurface.ContaminatesRug)
                return $"Aire de lavage sale : {status.CleanlinessPercent} % propre — le tapis posé dessus se salit ! {how}";
            return $"Aire de lavage : {status.CleanlinessPercent} % propre — {how}";
        }

        void OnContaminated(float amount) =>
            ShowToast("Tapis posé sur une aire de lavage sale : ses bords sont recontaminés");

        void OnRugFinished()
        {
            pulse = 1f;
            gainFlash = 1f;
            string deliver = input != null ? input.Label("Interact") : "E";
            ShowToast($"Tapis terminé : plus aucune saleté ! Séchez-le si besoin puis livrez-le sur l'ordinateur ({deliver})");
        }

        /// <summary>Produits spécialisés : message au changement de produit et à l'épuisement.</summary>
        public void BindProducts(ProductStockBook book)
        {
            products = book;
            lastProduct = products.Active;
            products.Changed += OnProductsChanged;
            products.Depleted += OnProductDepleted;
        }

        void OnProductsChanged()
        {
            if (products.Active == lastProduct) return;
            lastProduct = products.Active;
            ShowToast(products.Active == null
                ? "Produit : détergent standard"
                : $"Produit : {products.Active.displayName} ({products.StockOf(products.Active):0.0} L en stock)");
        }

        void OnProductDepleted(ProductDefinition product) =>
            ShowToast($"{product.displayName} épuisé — retour au détergent standard (boutique pour en racheter)");

        /// <summary>
        /// Tapis fragile : avertit (au plus toutes les 6 s) quand l'usure des fibres augmente réellement
        /// (brossage à sec, produit agressif laissé en place).
        /// </summary>
        void CheckFiberWear()
        {
            if (rug == null || rug.Grid == null || rug.Fragility <= 0f) return;
            float wear = rug.Grid.AverageWear;
            if (wear > lastWear + 0.0005f && Time.unscaledTime >= nextWearWarning)
            {
                ShowToast($"Attention : fibres abîmées ({wear * 100f:0.#} %) — humidifiez ou savonnez avant de brosser");
                nextWearWarning = Time.unscaledTime + 6f;
            }
            lastWear = wear;
        }

        /// <summary>Texte de l'alerte du plan de travail (vide s'il est propre).</summary>
        /// <summary>
        /// Multijoueur (invité) : message affiché tant que l'état de l'hôte n'est pas reçu ; null quand tout est prêt. Pendant
        /// le chargement, aucune valeur provisoire (propreté, aire de lavage) n'est affichée.
        /// </summary>
        public void SetLoading(string message)
        {
            loadingMessage = string.IsNullOrEmpty(message) ? null : message;
            RefreshVisibility();
            if (workSurface != null) workSurfaceLabel.text = DescribeWorkSurface(workSurface.Status);
            lastCleanPercent = int.MinValue;
            if (tools != null) RefreshAll(force: true);
        }

        public bool IsLoading => loadingMessage != null;
        public string WorkSurfaceAlert => workSurfaceLabel.text;
        public string EconomyText => economyLabel.text;
        public string CleanlinessText => cleanlinessLabel.text;
        public string ToastText => toastLabel.text;
        public string PlayersText => playersLabel != null ? playersLabel.text : string.Empty;
        public string HintText => hintLabel.text;
        public string PromptText => promptLabel.text;
        public string OrderText => orderLabel.text;
        public string ScanText => scanLabel.text;
        public string ToolStatusText => toolStatusLabel.text;

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

        /// <summary>Clavier ↔ manette : toutes les indications de touches sont réécrites.</summary>
        void OnSchemeChanged(InputScheme _)
        {
            lastTool = null;
            RefreshEconomy();
            RefreshAll(force: true);
        }

        void OnWalletChanged(int balance, int delta) => RefreshEconomy();
        void OnReputationChanged(int points) => RefreshEconomy();

        void RefreshEconomy()
        {
            if (wallet == null) return;
            economyLabel.text = $"{WalletManager.Format(wallet.Balance)}\nRéputation {reputation.Points}";
            var order = orders.ActiveOrder;
            string interact = input != null ? input.Label("Interact") : "E";
            bool finished = rug != null && rug.IsFinished;
            orderLabel.text = order == null
                ? $"Aucune commande — missions sur l'ordinateur ({interact})"
                : $"{order.title} · " + (finished ? "tapis terminé" : $"objectif {order.objectives.targetCleanliness * 100f:0} %") +
                  $" · {OrderEvaluator.FormatTime(orders.ElapsedSeconds)}\n" +
                  $"Livrer sur l'ordinateur, application Missions ({interact})";
        }

        void OnDestroy()
        {
            if (input != null && input.Monitor != null) input.Monitor.SchemeChanged -= OnSchemeChanged;
            if (interactor != null) interactor.Interacted -= ShowToast;
            if (tools != null) tools.LockedToolRequested -= OnLockedToolRequested;
            if (products != null)
            {
                products.Changed -= OnProductsChanged;
                products.Depleted -= OnProductDepleted;
            }
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
            if (progress != null)
            {
                progress.MilestoneReached -= OnMilestone;
                progress.RugFinished -= OnRugFinished;
            }
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
            root.alpha = gameState.IsGameplayActive || loadingMessage != null ? 1f : 0.35f;
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

        /// <summary>Partie en ligne : joueurs présents dans l'atelier (et latence vers l'hôte pour un invité).</summary>
        void RefreshPlayers()
        {
            if (playersLabel == null) return;
            var workshop = Network.NetworkWorkshop.Instance;
            if (workshop == null || !workshop.IsSpawned)
            {
                if (playersLabel.text.Length > 0) playersLabel.text = string.Empty;
                return;
            }
            var avatars = FindObjectsByType<Network.NetworkPlayerAvatar>();
            int count = 0;
            foreach (var avatar in avatars)
                if (avatar.IsSpawned) count++;
            playersBuilder.Clear();
            playersBuilder.Append("Joueurs (").Append(count).Append(") : ");
            bool first = true;
            foreach (var avatar in avatars)
            {
                if (!avatar.IsSpawned) continue;
                if (!first) playersBuilder.Append(" · ");
                first = false;
                playersBuilder.Append(avatar.DisplayName);
                if (avatar.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId) playersBuilder.Append(" (hôte)");
            }
            var manager = workshop.NetworkManager;
            if (manager != null && !manager.IsServer && manager.NetworkConfig.NetworkTransport != null)
                playersBuilder.Append("   ·   ").Append(manager.NetworkConfig.NetworkTransport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId)).Append(" ms");
            string text = playersBuilder.ToString();
            if (text != playersLabel.text) playersLabel.text = text;
        }

        readonly System.Text.StringBuilder playersBuilder = new System.Text.StringBuilder();

        void RefreshAll(bool force)
        {
            RefreshEconomy();
            RefreshPlayers();
            CheckFiberWear();
            var tool = tools.ActiveTool;

            bool hasRug = rug != null && rug.IsLoaded;
            int percent = loadingMessage != null ? -2 : hasRug ? Mathf.FloorToInt(rug.Cleanliness01 * 100f) : -1;
            if (force || percent != lastCleanPercent)
            {
                lastCleanPercent = percent;
                cleanlinessLabel.text = loadingMessage != null ? "Synchronisation avec l'hôte…"
                    : !hasRug ? "Aucun tapis — acceptez une mission sur l'ordinateur"
                    : rug.IsFinished ? "Propreté du tapis : 100 % — terminé" : $"Propreté du tapis : {percent} %";
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
            if (loadingMessage != null) return loadingMessage;
            if (interactor.Current != null)
                return $"[{input.GetBindingLabel("Interact")}] {interactor.Current.GetPrompt(interactor)}";
            string block = tool?.GetBlockReason();
            if (block != null) return block;
            // « Trop loin » seulement en visant le tapis de la commande, ou en utilisant l'outil (le sol est aussi nettoyable).
            if (tool != null && tool.IsTargetOutOfRange && (tool.IsUsing || tools.CurrentAim.Rug == rug)) return "Trop loin — rapprochez-vous";
            return string.Empty;
        }

        /// <summary>Rappel des commandes, selon le périphérique actif (clavier/souris ou manette).</summary>
        string BuildHint(CleaningToolBase tool)
        {
            string verb = tool != null ? tool.ActionVerb : "utiliser";
            string common = $"{input.Label("Interact")} : interagir   ·   {input.Label("Scan")} : scanner   ·   {input.Label("Product")} : produit   ·   " +
                            $"{input.Label("Inspect")} : inspecter   ·   ";
            if (input.Scheme == InputScheme.Gamepad)
                return $"{input.Label("Use")} : {verb}   ·   {input.Label("ToolPrevious")} / {input.Label("ToolNext")} : outils   ·   " +
                       common + $"{input.Label("Pause")} : pause";
            return $"Clic gauche : {verb}   ·   1-0 / molette : outils   ·   " + common + "Échap : pause";
        }
    }
}
