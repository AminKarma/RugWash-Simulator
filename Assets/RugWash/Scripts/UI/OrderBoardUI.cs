using System.Collections.Generic;
using System.Text;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Orders;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Application « Missions / Commandes » de l'ordinateur : liste avec difficulté, récompense, objectifs et état ;
    /// acceptation, livraison avec estimation de la note, abandon. Chaque bouton agit réellement. Ordre d'affichage :
    /// commande en cours, missions générées accessibles, contrats accessibles, offres verrouillées, contrats terminés.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrderBoardUI : MonoBehaviour, IModalPanel
    {
        static readonly Color RowColor = new Color(0.12f, 0.15f, 0.18f, 0.95f);
        static readonly Color RowLocked = new Color(0.1f, 0.1f, 0.11f, 0.9f);
        static readonly Color RowActive = new Color(0.1f, 0.26f, 0.32f, 0.95f);
        static readonly Color Ok = new Color(0.55f, 0.95f, 0.6f);
        static readonly Color Warn = new Color(1f, 0.75f, 0.35f);
        static readonly Color Muted = new Color(0.7f, 0.72f, 0.75f);

        [SerializeField] GameObject panel;
        [SerializeField] Text headerLabel;
        [SerializeField] Transform listRoot;
        [SerializeField] ListRowView rowTemplate;
        [SerializeField] GameObject activeSection;
        [SerializeField] Text activeLabel;
        [SerializeField] Button deliverButton;
        [SerializeField] Button abandonButton;
        [SerializeField] Text messageLabel;
        [SerializeField] Button closeButton;

        readonly List<ListRowView> rows = new List<ListRowView>();
        readonly List<OrderDefinition> display = new List<OrderDefinition>();
        GameStateManager gameState;
        OrderManager orders;
        WalletManager wallet;
        Cleaning.WorkSurface workSurface;

        /// <summary>Avertit, sans bloquer, qu'un nouveau tapis serait posé sur une aire de lavage sale.</summary>
        public void SetWorkSurface(Cleaning.WorkSurface surface) => workSurface = surface;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<ListRowView> Rows => rows;
        /// <summary>Commande affichée sur chaque ligne (même indice que <see cref="Rows"/>).</summary>
        public IReadOnlyList<OrderDefinition> DisplayedOrders => display;

        /// <summary>Ligne qui affiche cette commande (null si elle n'est pas affichée).</summary>
        public ListRowView RowOf(OrderDefinition order)
        {
            int index = display.IndexOf(order);
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }
        public Button DeliverButton => deliverButton;
        public Button AbandonButton => abandonButton;
        public string LastMessage => messageLabel.text;

        public bool IsConfigured(out string missing)
        {
            missing = panel == null ? nameof(panel) : headerLabel == null ? nameof(headerLabel) : listRoot == null ? nameof(listRoot)
                : rowTemplate == null ? nameof(rowTemplate) : activeSection == null ? nameof(activeSection) : activeLabel == null ? nameof(activeLabel)
                : deliverButton == null ? nameof(deliverButton) : abandonButton == null ? nameof(abandonButton)
                : messageLabel == null ? nameof(messageLabel) : closeButton == null ? nameof(closeButton) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state, OrderManager orderManager, WalletManager walletManager)
        {
            gameState = state;
            orders = orderManager;
            wallet = walletManager;
            rowTemplate.gameObject.SetActive(false);
            closeButton.onClick.AddListener(Close);
            deliverButton.onClick.AddListener(Deliver);
            abandonButton.onClick.AddListener(Abandon);
            orders.Changed += RefreshIfOpen;
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (orders != null) orders.Changed -= RefreshIfOpen;
        }

        public bool Open()
        {
            if (IsOpen || !gameState.IsGameplayActive) return false;
            messageLabel.text = string.Empty;
            panel.SetActive(true);
            IsOpen = true;
            gameState.PushModal();
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            IsOpen = false;
            gameState.PopModal();
        }

        void Update()
        {
            // Estimation en direct de la commande active pendant que le tableau est ouvert.
            if (IsOpen && orders.ActiveOrder != null && Time.frameCount % 15 == 0) RefreshActive();
        }

        void RefreshIfOpen()
        {
            if (IsOpen) Refresh();
        }

        void Refresh()
        {
            headerLabel.text = $"Solde : {WalletManager.Format(wallet.Balance)}     Réputation : {orders.Reputation}";
            if (orders.BoardReputation != orders.Reputation) headerLabel.text += $"     Tableau de l'hôte (réputation {orders.BoardReputation})";
            if (orders.Missions.IsInitialized || orders.IsReplica)
                headerLabel.text += $"     Missions : niveau {MissionRules.TierFor(orders.BoardReputation) + 1}";
            headerLabel.text += WorkAreaWarning();
            BuildDisplay();
            var list = display;
            while (rows.Count < list.Count)
            {
                var row = Instantiate(rowTemplate, listRoot);
                row.gameObject.SetActive(true);
                int index = rows.Count;
                row.ActionButton.onClick.AddListener(() => Accept(index));
                rows.Add(row);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < list.Count;
                rows[i].gameObject.SetActive(used);
                if (used) FillRow(rows[i], list[i]);
            }
            RefreshActive();
        }

        /// <summary>Ordre d'affichage (le carnet garde l'ordre du catalogue, suivi des missions générées).</summary>
        void BuildDisplay()
        {
            display.Clear();
            var book = orders.Book;
            int reputation = orders.BoardReputation;
            int Rank(OrderDefinition order)
            {
                var state = book.StateOf(order);
                if (state == OrderState.Active) return 0;
                if (state == OrderState.Completed) return 5;
                if (book.IsLocked(order, reputation)) return 3;
                return MissionKey.IsGenerated(order.id) ? 1 : 2;
            }
            for (int rank = 0; rank <= 5; rank++)
            {
                int start = display.Count;
                foreach (var order in book.Orders)
                    if (Rank(order) == rank) display.Add(order);
                // Offres verrouillées : la plus proche en premier.
                if (rank == 3) display.Sort(start, display.Count - start, Comparer<OrderDefinition>.Create((a, b) => a.requiredReputation.CompareTo(b.requiredReputation)));
            }
        }

        void FillRow(ListRowView row, OrderDefinition order)
        {
            var state = orders.Book.StateOf(order);
            bool locked = orders.Book.IsLocked(order, orders.BoardReputation);
            string stars = new string('★', order.difficulty) + new string('☆', Mathf.Max(0, 4 - order.difficulty));
            string kind = MissionKey.IsGenerated(order.id) ? "Mission" : "Contrat";
            string details = $"{kind} · {order.clientName} · {order.rug.displayName} · {stars} · {WalletManager.Format(order.baseReward)}\n{DescribeObjectives(order)}";

            string status, action = null;
            Color statusColor, background = RowColor;
            bool interactable = false;
            if (state == OrderState.Completed) { status = "Terminée"; statusColor = Ok; }
            else if (state == OrderState.Active) { status = "En cours"; statusColor = Warn; background = RowActive; }
            else if (locked) { status = $"Verrouillée (réputation {order.requiredReputation})"; statusColor = Muted; background = RowLocked; }
            else
            {
                bool areaReady = orders.WorkAreaAllowsNewOrder;
                status = areaReady ? "Disponible" : "Aire de lavage à nettoyer";
                statusColor = areaReady ? Ok : Warn;
                action = "Accepter";
                // Le bouton reste cliquable si seule l'aire bloque : le refus explique alors la raison.
                interactable = orders.ActiveOrder == null;
            }
            row.Set(order.title, details, status, statusColor, action, interactable, background);
        }

        /// <summary>État de l'aire de lavage, avec la raison d'un refus (propreté sous le seuil) ou d'une recontamination.</summary>
        string WorkAreaWarning()
        {
            if (workSurface == null || workSurface.Surface == null || workSurface.Surface.Grid == null) return string.Empty;
            var status = workSurface.Status;
            if (!workSurface.AllowsNewOrder)
                return $"\n⚠ Aire de lavage trop sale : {status.CleanlinessPercent} % propre, {workSurface.MinCleanlinessPercent} % requis " +
                       "— nettoyez-la (lance, balai) avant d'accepter une commande";
            if (workSurface.ContaminatesRug)
                return $"\n⚠ Aire de lavage sale ({status.CleanlinessPercent} % propre) : au-delà de {workSurface.DirtyThresholdPercent} % de saleté, " +
                       "le tapis posé dessus se salit";
            return status.IsClean ? string.Empty : $"\nAire de lavage : {status.CleanlinessPercent} % propre";
        }

        static string DescribeObjectives(OrderDefinition order)
        {
            var o = order.objectives;
            var builder = new StringBuilder($"Objectif : {o.targetCleanliness * 100f:0} % de saleté retirée");
            if (o.limitResidue) builder.Append($" · résidus ≤ {o.maxResidue * 100f:0} %");
            if (o.limitMoisture) builder.Append($" · humidité ≤ {o.maxMoisture * 100f:0} %");
            if (o.limitWear) builder.Append($" · fibres abîmées ≤ {o.maxWear * 100f:0.#} %");
            if (o.bonusTimeSeconds > 0f) builder.Append($" · bonus sous {OrderEvaluator.FormatTime(o.bonusTimeSeconds)}");
            // Avertissement issu des données réelles du tapis (fragilité) : brossage à sec et produits agressifs l'usent.
            if (order.rug != null && order.rug.fragility > 0f)
                builder.Append("\nTapis fragile (").Append(RugDefinition.FragilityLabel(order.rug.fragility))
                    .Append(") : brossez sur fibres humides ou savonneuses, évitez les produits agressifs");
            return builder.ToString();
        }

        void RefreshActive()
        {
            var active = orders.ActiveOrder;
            activeSection.SetActive(active != null);
            if (active == null) return;
            var preview = orders.Preview();
            activeLabel.text = $"En cours : {active.title} — {OrderEvaluator.FormatTime(orders.ElapsedSeconds)}\n" +
                               $"Note estimée : {OrderEvaluator.GradeLabel(preview.Grade)} ({preview.Score:0}/100) · " +
                               (preview.Accepted ? $"paiement estimé {WalletManager.Format(preview.Reward)}" : "le client refuserait le tapis en l'état");
        }

        /// <summary>Accepte la commande de la ligne <paramref name="index"/> (ordre d'affichage).</summary>
        public void Accept(int index)
        {
            if (index < 0 || index >= display.Count) return;
            var result = orders.TryStart(display[index]);
            messageLabel.text = OrderManager.StartResultMessage(result);
            if (result == OrderStartResult.Started || result == OrderStartResult.Requested) Close();
            else Refresh();
        }

        public void Deliver()
        {
            if (orders.ActiveOrder == null) return;
            Close();
            orders.Deliver();
        }

        public void Abandon()
        {
            if (orders.ActiveOrder == null) return;
            Close();
            orders.Abandon();
        }
    }
}
