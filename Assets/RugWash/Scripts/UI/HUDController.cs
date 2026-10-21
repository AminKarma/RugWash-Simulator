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
    /// HUD minimaliste (V0.3.0) : argent, objet en main et sa réserve, propreté du tapis (seulement avec un tapis), commande
    /// sur une ligne, interaction disponible près du viseur, scan, messages courts. Aucune liste de touches permanente : le
    /// rappel des commandes de l'objet en main s'affiche quelques secondes à chaque changement (outil ou périphérique) puis
    /// s'efface ; la liste complète est dans Réglages > Commandes. L'alerte de l'aire de lavage n'apparaît que si elle est sale.
    /// Les jauges suivent leur valeur réelle avec un léger lissage ; les paliers de propreté font « pulser » le panneau.
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
        [Tooltip("Optionnel : panneau de l'objet en main (masqué les mains vides).")]
        [SerializeField] GameObject toolPanel;
        [Tooltip("Durée d'affichage du rappel des commandes après un changement d'outil ou de périphérique (s).")]
        [SerializeField, Min(0f)] float hintSeconds = 5f;
        [SerializeField] Color gainColor = new Color(0.75f, 1f, 0.85f, 1f);
        [SerializeField] float barSmoothing = 10f;
        [SerializeField] float refreshInterval = 0.1f;
        [SerializeField] float toastDuration = 2.5f;

        PlayerToolController tools;
        PlayerInteractor interactor;
        PlayerInputReader input;
        RugCleaningController defaultRug;
        GameStateManager gameState;
        WalletManager wallet;
        ReputationManager reputation;
        OrderManager orders;
        DirtScanner scanner;
        WorkSurface workSurface, extraWorkSurface, lastAlertSurface;
        bool workSurfaceWasClean = true;
        int lastAlertKey = int.MinValue;
        Workshop.WashingMachine washer;
        readonly System.Text.StringBuilder otherWork = new System.Text.StringBuilder();
        RugCleaningController lastWearRug;

        /// <summary>
        /// Tapis suivi par le HUD : celui de la zone de lavage où se trouve le joueur (grande mise à jour : plusieurs zones),
        /// sinon le tapis de la zone 1.
        /// </summary>
        RugCleaningController CurrentRug => orders != null && orders.BayCount > 0 ? orders.CurrentRug : defaultRug;

        /// <summary>Aire de lavage de la zone courante.</summary>
        WorkSurface CurrentWorkSurface
        {
            get
            {
                if (orders == null || orders.BayCount < 2) return workSurface;
                var surface = orders.WorkSurfaceOf(orders.CurrentBay);
                return surface != null ? surface : workSurface;
            }
        }
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
        float hintUntil;
        string loadingMessage;
        int lastCleanPercent = -1;
        string lastPrompt, lastStatus, lastBlock;
        Pranks.PrankGadgetController gadgets;
        Pranks.PrankGadget lastGadget;
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
            defaultRug = rugController;
            gameState = state;

            interactor.Interacted += ShowToast;
            tools.LockedToolRequested += OnLockedToolRequested;
            if (input.Monitor != null) input.Monitor.SchemeChanged += OnSchemeChanged;
            gameState.StateChanged += OnStateChanged;
            gameState.ModalsChanged += RefreshVisibility;
            toastLabel.text = string.Empty;
            cleanlinessBaseColor = cleanlinessFill.color;
            displayedCleanliness = CurrentRug != null ? CurrentRug.Cleanliness01 : 0f;
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

        Save.SaveManager profileSource;

        /// <summary>Niveau du joueur (profil, commun à tous les emplacements) : affiché sous l'argent ; message à chaque gain.</summary>
        public void BindProfile(Save.SaveManager save)
        {
            profileSource = save;
            if (save == null) return;
            save.ExperienceGained += OnExperienceGained;
            RefreshEconomy();
        }

        void OnExperienceGained(int amount, int level, bool levelUp)
        {
            RefreshEconomy();
            ShowToast(levelUp ? $"Niveau {level} atteint ! (+{amount} XP)" : $"+{amount} XP");
        }

        /// <summary>Gadgets de farce (v0.1.2) : nom, recharge et coût à la place de l'outil quand un gadget est en main.</summary>
        public void BindGadgets(Pranks.PrankGadgetController controller)
        {
            gadgets = controller;
            RefreshAll(force: true);
        }

        Pranks.PrankGadget CurrentGadget => gadgets != null ? gadgets.Equipped : Pranks.PrankGadget.None;

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
        public void BindWorkSurface(WorkSurface surface, WorkSurface extra = null)
        {
            workSurface = surface;
            workSurface.StatusChanged += OnWorkSurfaceChanged;
            workSurface.Contaminated += OnContaminated;
            // Zone de lavage 2 (agrandissement) : l'alerte suit l'aire de la zone où se trouve le joueur.
            extraWorkSurface = extra;
            if (extraWorkSurface != null)
            {
                extraWorkSurface.StatusChanged += OnWorkSurfaceChanged;
                extraWorkSurface.Contaminated += OnContaminated;
            }
            RefreshWorkSurface(force: true);
        }

        /// <summary>Machine à laver (grande mise à jour) : temps restant rappelé sous la commande en cours.</summary>
        public void BindWasher(Workshop.WashingMachine machine) => washer = machine;

        void OnWorkSurfaceChanged(WorkSurfaceStatus status) => RefreshWorkSurface(force: false);

        /// <summary>Alerte de l'aire de la zone courante, réécrite seulement si son état affiché change.</summary>
        void RefreshWorkSurface(bool force)
        {
            var surface = CurrentWorkSurface;
            if (surface == null) return;
            var status = surface.Status;
            bool multiple = orders != null && orders.UnlockedBayCount > 1;
            int key = loadingMessage != null ? -1
                : (status.IsClean ? 1 : 0) | (surface.AllowsNewOrder ? 2 : 0) | (surface.ContaminatesRug ? 4 : 0) | (multiple ? 8 : 0)
                  | (status.CleanlinessPercent << 4) | (orders != null ? orders.BayOf(surface.Surface) << 12 : 0);
            if (!force && key == lastAlertKey && surface == lastAlertSurface) return;
            if (surface == lastAlertSurface && status.IsClean && !workSurfaceWasClean) ShowToast("Aire de lavage propre");
            lastAlertKey = key;
            lastAlertSurface = surface;
            workSurfaceWasClean = status.IsClean;
            workSurfaceLabel.text = DescribeWorkSurface(surface, status, multiple);
        }

        string DescribeWorkSurface(WorkSurface surface, in WorkSurfaceStatus status, bool multiple)
        {
            if (loadingMessage != null || surface == null || surface.Surface == null || surface.Surface.Grid == null) return string.Empty;
            string area = multiple && orders != null ? $"Aire de lavage ({OrderManager.BayLabel(orders.BayOf(surface.Surface)).ToLowerInvariant()})" : "Aire de lavage";
            if (status.IsClean) return string.Empty;
            const string how = "rincez puis raclez vers le caniveau";
            if (!surface.AllowsNewOrder)
                return $"{area} trop sale : {status.CleanlinessPercent} % propre ({surface.MinCleanlinessPercent} % requis) — {how}";
            if (surface.ContaminatesRug)
                return $"{area} sale : {status.CleanlinessPercent} % propre — elle salit le tapis";
            return $"{area} : {status.CleanlinessPercent} % propre";
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
            var watched = CurrentRug;
            if (watched == null || watched.Grid == null || watched.Fragility <= 0f) return;
            float wear = watched.Grid.AverageWear;
            // Changement de zone : la nouvelle usure sert de référence (pas d'alerte pour l'usure de l'autre tapis).
            if (watched != lastWearRug)
            {
                lastWearRug = watched;
                lastWear = wear;
                return;
            }
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
            RefreshWorkSurface(force: true);
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
        /// <summary>Remplissage affiché de la barre de réserve (0 à 1) et sa hauteur réelle à l'écran (tests).</summary>
        public float ResourceBarFill => resourceFill.fillAmount;
        public bool ResourceBarShown => resourceBar.activeInHierarchy;
        public float ResourceBarPixelHeight => ((RectTransform)resourceFill.transform).rect.height;
        public float CleanlinessBarPixelHeight => ((RectTransform)cleanlinessFill.transform).rect.height;

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
            hintUntil = Time.unscaledTime + hintSeconds;
            RefreshEconomy();
            RefreshAll(force: true);
        }

        void OnWalletChanged(int balance, int delta) => RefreshEconomy();
        void OnReputationChanged(int points) => RefreshEconomy();

        void RefreshEconomy()
        {
            if (wallet == null) return;
            // HUD minimaliste : l'argent seul (réputation et niveau dans le menu pause).
            economyLabel.text = WalletManager.Format(wallet.Balance);
            int bay = orders.CurrentBay;
            var order = orders.OrderOf(bay);
            string interact = input != null ? input.Label("Interact") : "E";
            var bayRug = orders.RugOf(bay);
            bool finished = bayRug != null && bayRug.IsFinished;
            string zone = orders.UnlockedBayCount > 1 ? $"{OrderManager.BayLabel(bay)} · " : string.Empty;
            orderLabel.text = (order == null
                ? $"{zone}Aucune commande · ordinateur ({interact})"
                : $"{zone}{order.title} · " + (finished ? $"terminé, à livrer sur l'ordinateur ({interact})" : $"objectif {order.objectives.targetCleanliness * 100f:0} %") +
                  $" · {OrderEvaluator.FormatTime(orders.ElapsedOf(bay))}") + OtherWork(bay);
        }

        /// <summary>Les autres zones occupées et la machine à laver, sur une ligne (vide s'il n'y a rien d'autre en cours).</summary>
        string OtherWork(int currentBay)
        {
            otherWork.Clear();
            for (int i = 0; i < orders.BayCount; i++)
            {
                var other = orders.OrderOf(i);
                if (i == currentBay || other == null) continue;
                var otherRug = orders.RugOf(i);
                otherWork.Append(otherWork.Length == 0 ? "\n" : " · ").Append(OrderManager.BayLabel(i)).Append(" : ").Append(other.title);
                if (otherRug != null && otherRug.Grid != null) otherWork.Append(' ').Append(Mathf.FloorToInt(otherRug.Cleanliness01 * 100f)).Append(" %");
            }
            if (washer != null && washer.State != Workshop.WasherState.Free)
                otherWork.Append(otherWork.Length == 0 ? "\n" : " · ").Append("Machine à laver : ")
                    .Append(washer.State == Workshop.WasherState.Done ? "terminé, à récupérer" : Workshop.WasherRules.Format(washer.Remaining));
            return otherWork.Length == 0 ? string.Empty : otherWork.ToString();
        }

        void OnDestroy()
        {
            if (input != null && input.Monitor != null) input.Monitor.SchemeChanged -= OnSchemeChanged;
            if (profileSource != null) profileSource.ExperienceGained -= OnExperienceGained;
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
            if (extraWorkSurface != null)
            {
                extraWorkSurface.StatusChanged -= OnWorkSurfaceChanged;
                extraWorkSurface.Contaminated -= OnContaminated;
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
            float cleanliness = CurrentRug != null ? CurrentRug.Cleanliness01 : 0f;
            if (cleanliness > displayedCleanliness + 0.0005f) gainFlash = Mathf.Max(gainFlash, 0.45f);
            displayedCleanliness = cleanliness < displayedCleanliness ? cleanliness : Mathf.Lerp(displayedCleanliness, cleanliness, smoothing);
            cleanlinessFill.fillAmount = displayedCleanliness;
            gainFlash = Mathf.MoveTowards(gainFlash, 0f, dt * 1.5f);
            cleanlinessFill.color = Color.Lerp(cleanlinessBaseColor, gainColor, gainFlash);

            pulse = Mathf.MoveTowards(pulse, 0f, dt / 0.4f);
            cleanlinessPanel.localScale = Vector3.one * (1f + 0.07f * Mathf.Sin(pulse * Mathf.PI));

            var tool = tools.ActiveTool;
            // Gadget en main : la jauge montre la recharge (pleine = prêt).
            float fraction = CurrentGadget != Pranks.PrankGadget.None ? gadgets.ReadyFraction : tool != null ? tool.GetResourceFraction() : -1f;
            if (fraction >= 0f)
            {
                // V0.4.2 : la barre montre la réserve réelle à l'instant (même valeur que le texte), bornée entre vide et plein.
                displayedResource = Mathf.Clamp01(fraction);
                resourceFill.fillAmount = displayedResource;
            }

            if (scanner != null)
            {
                scanFill.fillAmount = scanner.IsActive ? scanner.ActiveRemaining / scanner.Duration : scanner.Readiness01;
            }

            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = string.Empty;
            hintLabel.canvasRenderer.SetAlpha(Mathf.Clamp01((hintUntil - Time.unscaledTime) / 0.6f));

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
                // Chat vocal : qui parle (le joueur local d'après son micro, les autres d'après les trames reçues).
                bool speaking = avatar.IsOwner ? voiceSource != null && voiceSource.IsSpeaking : avatar.Voice != null && avatar.Voice.IsSpeaking;
                if (speaking) playersBuilder.Append(" « parle »");
            }
            var manager = workshop.NetworkManager;
            if (manager != null && !manager.IsServer && manager.NetworkConfig.NetworkTransport != null)
                playersBuilder.Append("   ·   ").Append(manager.NetworkConfig.NetworkTransport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId)).Append(" ms");
            string text = playersBuilder.ToString();
            if (text != playersLabel.text) playersLabel.text = text;
        }

        readonly System.Text.StringBuilder playersBuilder = new System.Text.StringBuilder();
        Network.VoiceChat voiceSource;

        /// <summary>Chat vocal du joueur local : indicateur « parle » dans la liste des joueurs.</summary>
        public void BindVoice(Network.VoiceChat voice) => voiceSource = voice;

        void RefreshAll(bool force)
        {
            RefreshEconomy();
            RefreshWorkSurface(force);
            RefreshPlayers();
            CheckFiberWear();
            var tool = tools.ActiveTool;

            bool hasRug = CurrentRug != null && CurrentRug.IsLoaded;
            int percent = loadingMessage != null ? -2 : hasRug ? Mathf.FloorToInt(CurrentRug.Cleanliness01 * 100f) : -1;
            if (force || percent != lastCleanPercent)
            {
                lastCleanPercent = percent;
                cleanlinessLabel.text = loadingMessage != null ? "Synchronisation avec l'hôte…"
                    : !hasRug ? "Aucun tapis"
                    : CurrentRug.IsFinished ? "Propreté 100 % — terminé" : $"Propreté {percent} %";
                cleanlinessPanel.gameObject.SetActive(hasRug || loadingMessage != null);
            }


            var gadget = CurrentGadget;
            if (force || tool != lastTool || gadget != lastGadget)
            {
                lastTool = tool;
                lastGadget = gadget;
                hintUntil = Time.unscaledTime + hintSeconds;
                if (toolPanel != null) toolPanel.SetActive(tool != null || gadget != Pranks.PrankGadget.None);
                if (gadget != Pranks.PrankGadget.None)
                {
                    toolNameLabel.text = $"{gadgets.Name(gadget)} (farce)";
                    resourceBar.SetActive(true);
                    displayedResource = -1f;
                    hintLabel.text = BuildGadgetHint();
                }
                else
                {
                    // Niveau affiché dès le premier niveau acheté ; « ★ » au niveau maximal (outil doré).
                    toolNameLabel.text = tool == null ? "Aucun outil"
                        : tool.Level > 0 ? $"{tool.Definition.displayName} · niv. {tool.Level}/{tool.MaxLevel}{(tool.IsGolden ? " ★" : string.Empty)}"
                        : tool.Definition.displayName;
                    resourceBar.SetActive(tool != null && tool.GetResourceFraction() >= 0f);
                    hintLabel.text = BuildHint(tool);
                }
            }

            if (scanner != null)
            {
                string key = input.GetBindingLabel("Scan");
                string scan = scanner.IsActive ? $"Scan · {Mathf.CeilToInt(scanner.ActiveRemaining)} s"
                    : scanner.IsReady ? $"[{key}] Scan" : $"Scan · {Mathf.CeilToInt(scanner.CooldownRemaining)} s";
                if (force || scan != lastScan)
                {
                    lastScan = scan;
                    scanLabel.text = scan;
                }
            }

            string status = gadget != Pranks.PrankGadget.None ? gadgets.StatusLine : tool?.GetStatusLine() ?? string.Empty;
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

        /// <summary>Tapis d'une commande (d'une zone de lavage), par opposition à l'aire ou au sol.</summary>
        bool IsOrderRug(RugCleaningController surface)
        {
            if (surface == null) return false;
            if (orders == null) return surface == defaultRug;
            return orders.RugOf(orders.BayOf(surface)) == surface;
        }

        string BuildPrompt(CleaningToolBase tool)
        {
            if (loadingMessage != null) return loadingMessage;
            if (interactor.Current != null)
                return $"[{input.GetBindingLabel("Interact")}] {interactor.Current.GetPrompt(interactor)}";
            if (CurrentGadget != Pranks.PrankGadget.None) return string.Empty;
            string block = tool?.GetBlockReason();
            if (block != null) return block;
            // « Trop loin » seulement en visant le tapis de la commande, ou en utilisant l'outil (le sol est aussi nettoyable).
            if (tool != null && tool.IsTargetOutOfRange && (tool.IsUsing || IsOrderRug(tools.CurrentAim.Rug))) return "Trop loin — rapprochez-vous";
            return string.Empty;
        }

        /// <summary>Rappel court avec un gadget de farce en main (affiché quelques secondes).</summary>
        string BuildGadgetHint()
        {
            string use = input.Scheme == InputScheme.Gamepad ? input.Label("Use") : "Clic gauche";
            return $"{use} : {gadgets.ActionVerb}   ·   {input.Label("Gadget")} : gadget suivant / ranger";
        }

        /// <summary>
        /// Rappel court de l'objet en main (affiché quelques secondes) : son action et le changement d'objet, selon le
        /// périphérique actif. Les autres commandes : invite contextuelle, Réglages > Commandes.
        /// </summary>
        string BuildHint(CleaningToolBase tool)
        {
            string verb = tool != null ? tool.ActionVerb : "utiliser";
            if (input.Scheme == InputScheme.Gamepad)
                return $"{input.Label("Use")} : {verb}   ·   {input.Label("ToolPrevious")} / {input.Label("ToolNext")} : outils";
            return $"Clic gauche : {verb}   ·   1-4 / molette : outils";
        }
    }
}
