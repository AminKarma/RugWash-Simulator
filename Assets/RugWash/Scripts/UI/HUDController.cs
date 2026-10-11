using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using RugWash.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// HUD discret : outil actif, ressource, propreté (lue dans la grille), invite d'interaction,
    /// aide contextuelle et messages courts. Les textes sont rafraîchis à fréquence limitée.
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
                : orderLabel == null ? nameof(orderLabel) : null;
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
            gameState.StateChanged += OnStateChanged;
            gameState.ModalsChanged += RefreshVisibility;
            toastLabel.text = string.Empty;
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
            if (wallet != null && wallet.Wallet != null) wallet.Wallet.Changed -= OnWalletChanged;
            if (reputation != null) reputation.Changed -= OnReputationChanged;
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

            // Barres : mises à jour chaque frame (pas d'allocation).
            float cleanliness = rug != null ? rug.Cleanliness01 : 0f;
            cleanlinessFill.fillAmount = cleanliness;
            var tool = tools.ActiveTool;
            float fraction = tool != null ? tool.GetResourceFraction() : -1f;
            if (fraction >= 0f) resourceFill.fillAmount = fraction;

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
            if (tool != null && tool.IsTargetOutOfRange) return "Trop loin — rapprochez-vous du tapis";
            return string.Empty;
        }

        string BuildHint(CleaningToolBase tool)
        {
            string verb = tool != null ? tool.ActionVerb : "utiliser";
            return $"Clic gauche : {verb}   ·   1-5 / molette : outils   ·   " +
                   $"{input.GetBindingLabel("Interact")} : interagir   ·   {input.GetBindingLabel("Inspect")} : inspecter   ·   Échap : pause";
        }
    }
}
