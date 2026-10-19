using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Orders;
using RugWash.Workshop;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Fenêtre de la machine à laver (grande mise à jour) : état et temps restant, une ligne par zone de lavage (« Mettre en
    /// machine » le tapis de sa commande), puis « Récupérer le tapis » (vers une zone libre) ou « Abandonner la commande ».
    /// En ligne, les boutons envoient la demande à l'hôte, qui décide ; l'état revient par la réplication.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WashingMachineUI : MonoBehaviour, IModalPanel
    {
        static readonly Color RowReady = UITheme.RowActive;
        static readonly Color RowIdle = UITheme.Row;
        static readonly Color Ok = UITheme.Success;
        static readonly Color Warn = UITheme.Warning;
        static readonly Color Muted = UITheme.TextSecondary;

        [SerializeField] GameObject panel;
        [SerializeField] Text statusLabel;
        [SerializeField] Image progressFill;
        [SerializeField] List<ListRowView> bayRows = new List<ListRowView>();
        [SerializeField] Button collectButton;
        [SerializeField] Text collectLabel;
        [SerializeField] Button abandonButton;
        [SerializeField] Text noteLabel;
        [SerializeField] Text messageLabel;
        [SerializeField] Button closeButton;

        WashingMachine machine;
        GameStateManager gameState;
        float refreshTimer;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<ListRowView> BayRows => bayRows;
        public Button CollectButton => collectButton;
        public Button AbandonButton => abandonButton;
        public string Status => statusLabel.text;
        public string Message => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : statusLabel == null ? nameof(statusLabel) : progressFill == null ? nameof(progressFill)
                : bayRows.Count == 0 || bayRows.Contains(null) ? nameof(bayRows) : collectButton == null ? nameof(collectButton)
                : collectLabel == null ? nameof(collectLabel) : abandonButton == null ? nameof(abandonButton) : noteLabel == null ? nameof(noteLabel)
                : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state)
        {
            gameState = state;
            for (int i = 0; i < bayRows.Count; i++)
            {
                int bay = i;
                bayRows[i].ActionButton.onClick.AddListener(() => Act(WasherAction.Load, bay));
            }
            collectButton.onClick.AddListener(() => Act(WasherAction.Collect, machine != null ? machine.Orders.FocusBay : 0));
            abandonButton.onClick.AddListener(() => Act(WasherAction.Abandon, 0));
            closeButton.onClick.AddListener(Close);
            panel.SetActive(false);
        }

        public bool Open(WashingMachine washer)
        {
            if (IsOpen || washer == null || (gameState != null && !gameState.IsGameplayActive)) return false;
            if (machine != null) machine.StateChanged -= Refresh;
            machine = washer;
            machine.StateChanged += Refresh;
            machine.Orders.Changed += Refresh;
            messageLabel.text = string.Empty;
            panel.SetActive(true);
            IsOpen = true;
            gameState?.PushModal();
            Refresh();
            SelectFirst();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            if (machine != null)
            {
                machine.StateChanged -= Refresh;
                machine.Orders.Changed -= Refresh;
            }
            gameState?.PopModal();
        }

        void OnDestroy()
        {
            if (machine == null) return;
            machine.StateChanged -= Refresh;
            if (machine.Orders != null) machine.Orders.Changed -= Refresh;
        }

        void Update()
        {
            if (!IsOpen) return;
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < 0.25f) return;
            refreshTimer = 0f;
            Refresh();
        }

        void SelectFirst()
        {
            if (collectButton.gameObject.activeInHierarchy && collectButton.interactable) collectButton.Select();
            else
                foreach (var row in bayRows)
                    if (row.gameObject.activeInHierarchy && row.ActionButton.interactable)
                    {
                        row.ActionButton.Select();
                        return;
                    }
        }

        void Act(WasherAction action, int bay)
        {
            if (machine == null) return;
            string message = machine.Request(action, bay);
            messageLabel.text = message;
            messageLabel.color = machine.IsReplica ? Muted : Ok;
            Refresh();
        }

        public void Refresh()
        {
            if (machine == null) return;
            var orders = machine.Orders;
            var state = machine.State;
            string title = machine.Order != null ? $"« {machine.Order.title} »" : string.Empty;
            statusLabel.text = state == WasherState.Washing ? $"Lavage en cours {title} — encore {WasherRules.Format(machine.Remaining)}"
                : state == WasherState.Done ? $"Lavage terminé {title} : récupérez le tapis"
                : $"Machine libre — cycle de {WasherRules.Format(machine.NextCycleSeconds)}" + (machine.HasFastProgram ? " (programme rapide)" : string.Empty);
            statusLabel.color = state == WasherState.Done ? Ok : state == WasherState.Washing ? Warn : Color.white;
            progressFill.fillAmount = machine.Progress01;

            for (int i = 0; i < bayRows.Count; i++)
            {
                var row = bayRows[i];
                bool exists = i < orders.BayCount && orders.IsBayUnlocked(i);
                row.gameObject.SetActive(exists);
                if (!exists) continue;
                var order = orders.OrderOf(i);
                var rug = orders.RugOf(i);
                string label = OrderManager.BayLabel(i);
                if (order == null || rug == null || rug.Grid == null)
                {
                    row.Set($"{label} — aucun tapis", "Acceptez une commande sur l'ordinateur pour y poser un tapis.", "Libre", Muted, "Mettre en machine", false, RowIdle);
                    continue;
                }
                bool canLoad = state == WasherState.Free;
                string fragile = rug.Fragility >= 0.5f ? " · tapis fragile : la machine use ses fibres" : string.Empty;
                row.Set($"{label} — {order.title}", $"{order.rug.displayName} · propreté {rug.Cleanliness01 * 100f:0} %{fragile}",
                    canLoad ? "Prêt" : "Machine occupée", canLoad ? Ok : Warn, "Mettre en machine", canLoad, canLoad ? RowReady : RowIdle);
            }

            bool done = state == WasherState.Done;
            collectButton.gameObject.SetActive(state != WasherState.Free);
            collectButton.interactable = done && orders.HasFreeBay;
            collectLabel.text = done && !orders.HasFreeBay ? "Aucune zone libre" : "Récupérer le tapis";
            abandonButton.gameObject.SetActive(state != WasherState.Free);
            noteLabel.text = "La machine lave à fond (poussière, saleté détachée, la plupart des taches) pendant que vous travaillez " +
                             "un autre tapis ; le chronomètre de la commande est suspendu. Le tapis ressort trempé et les taches tenaces " +
                             "restent à traiter. Le temps n'avance que pendant le jeu (sauvegardé avec l'atelier).";
        }
    }
}
