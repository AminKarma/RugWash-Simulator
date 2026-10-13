using System;
using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Economy;
using RugWash.Effects;
using UnityEngine;

namespace RugWash.Orders
{
    /// <summary>
    /// Cycle d'une commande : acceptation → nettoyage (état ActiveOrder, chronomètre) → livraison
    /// (évaluation centralisée, paiement unique, réputation) → résultats → retour à l'atelier, sans tapis posé
    /// (il n'y a plus de tapis d'entraînement : un tapis n'apparaît qu'avec une commande). Une nouvelle commande ne démarre
    /// que si l'aire de lavage atteint la propreté minimale.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrderManager : MonoBehaviour
    {
        [SerializeField] List<OrderDefinition> orders = new List<OrderDefinition>();
        [SerializeField] RugCleaningController rug;
        [Tooltip("Aire de lavage : propreté minimale exigée avant une nouvelle commande (optionnel : sans elle, aucune condition).")]
        [SerializeField] WorkSurface workSurface;
        [SerializeField] RugSnapshotService snapshots;
        [SerializeField] GameStateManager gameState;
        [SerializeField] WalletManager wallet;
        [SerializeField] ReputationManager reputation;
        [SerializeField] AudioManager audioManager;
        [Tooltip("Son joué à la livraison d'une commande acceptée (sinon le son de confirmation).")]
        [SerializeField] AudioClip rewardClip;

        public OrderBook Book { get; private set; }

        /// <summary>
        /// Multijoueur, côté invité : les demandes (accepter, livrer, abandonner) partent vers l'hôte ; l'état revient par
        /// les méthodes « Remote ». L'évaluation et le paiement n'ont jamais lieu chez un invité. Null : solo ou hôte.
        /// </summary>
        public IWorkshopRequests Remote { get; set; }
        public bool IsReplica => Remote != null;
        public OrderDefinition ActiveOrder => Book?.Active;
        public float ElapsedSeconds { get; private set; }
        public OrderResult LastResult { get; private set; }
        public int Reputation => reputation.Points;

        public event Action<OrderDefinition> OrderStarted;
        public event Action<OrderResult> OrderFinished;
        /// <summary>Déclenché une seule fois par commande quand la propreté atteint l'objectif.</summary>
        public event Action<OrderDefinition> ObjectiveReached;

        bool objectiveNotified;
        public bool ObjectiveNotified => objectiveNotified;
        public event Action Changed;

        /// <summary>Aire de lavage utilisée pour la condition de démarrage (peut être nulle).</summary>
        public WorkSurface WorkSurface => workSurface;

        public bool IsConfigured(out string missing)
        {
            missing = orders.Count == 0 || orders.Contains(null) ? nameof(orders)
                : rug == null ? nameof(rug)
                : snapshots == null ? nameof(snapshots)
                : gameState == null ? nameof(gameState)
                : wallet == null ? nameof(wallet)
                : reputation == null ? nameof(reputation) : null;
            return missing == null;
        }

        void Awake() => Book = new OrderBook(orders);

        void OnEnable()
        {
            if (rug != null) rug.Finished += OnRugFinished;
        }

        void OnDisable()
        {
            if (rug != null) rug.Finished -= OnRugFinished;
        }

        /// <summary>Tapis terminé (moins de 1 % de saleté) : l'objectif de propreté est forcément atteint.</summary>
        void OnRugFinished(RugCleaningController finished)
        {
            if (ActiveOrder == null || objectiveNotified) return;
            objectiveNotified = true;
            ObjectiveReached?.Invoke(ActiveOrder);
        }

        /// <summary>Vrai si l'aire de lavage permet d'accepter une commande (toujours vrai sans aire configurée).</summary>
        public bool WorkAreaAllowsNewOrder => workSurface == null || workSurface.AllowsNewOrder;

        void Update()
        {
            if (ActiveOrder == null || gameState.State != GameState.ActiveOrder) return;
            ElapsedSeconds += Time.deltaTime;
            if (!objectiveNotified && rug.Grid != null && rug.Cleanliness01 >= ActiveOrder.objectives.targetCleanliness)
            {
                objectiveNotified = true;
                ObjectiveReached?.Invoke(ActiveOrder);
            }
        }

        public OrderStartResult TryStart(OrderDefinition order)
        {
            if (Remote != null)
            {
                var check = Book.CanStart(order, BoardReputation);
                if (check == OrderStartResult.Started && !WorkAreaAllowsNewOrder) check = OrderStartResult.WorkAreaTooDirty;
                if (check != OrderStartResult.Started) return check;
                Remote.RequestStartOrder(order.id);
                return OrderStartResult.Requested;
            }
            var allowed = Book.CanStart(order, BoardReputation);
            if (allowed == OrderStartResult.Started && !WorkAreaAllowsNewOrder) return OrderStartResult.WorkAreaTooDirty;
            var result = Book.Start(order, BoardReputation);
            if (result != OrderStartResult.Started) return result;

            ElapsedSeconds = 0f;
            LastResult = null;
            objectiveNotified = false;
            rug.LoadRug(order.rug, order.dirt, order.dirtSeed);
            gameState.SetState(GameState.ActiveOrder);
            OrderStarted?.Invoke(order);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Recommence la commande active (saleté régénérée, chronomètre remis à zéro).</summary>
        public bool RestartActive()
        {
            var order = ActiveOrder;
            if (order == null || IsReplica) return false;
            ElapsedSeconds = 0f;
            objectiveNotified = false;
            rug.LoadRug(order.rug, order.dirt, order.dirtSeed);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Estimation de la note actuelle (aucun effet sur l'état).</summary>
        public OrderResult Preview() =>
            ActiveOrder == null ? null : OrderEvaluator.Evaluate(ActiveOrder, rug.Grid.ComputeSummary(), ElapsedSeconds);

        /// <summary>Livre la commande active. Le paiement n'a lieu qu'une fois : sans commande active, rien ne se passe.</summary>
        public OrderResult Deliver()
        {
            var order = ActiveOrder;
            if (order == null) return null;
            if (Remote != null)
            {
                Remote.RequestDeliver();
                return null;
            }

            snapshots.CaptureAfter();
            var result = OrderEvaluator.Evaluate(order, rug.Grid.ComputeSummary(), ElapsedSeconds);
            if (!Book.Complete(result)) return null;

            if (result.Accepted) wallet.Wallet.Add(result.Reward);
            reputation.Add(result.ReputationDelta);
            LastResult = result;
            if (audioManager != null && result.Accepted) audioManager.PlayInterface(rewardClip != null ? rewardClip : audioManager.ConfirmClip);

            gameState.SetState(GameState.Results);
            OrderFinished?.Invoke(result);
            Changed?.Invoke();
            return result;
        }

        public bool Abandon()
        {
            if (Remote != null)
            {
                if (ActiveOrder == null) return false;
                Remote.RequestAbandon();
                return true;
            }
            if (!Book.Abandon()) return false;
            ReturnToWorkshop();
            return true;
        }

        /// <summary>Retour à l'atelier libre : le tapis livré ou abandonné est retiré (aucun tapis hors commande).</summary>
        public void ReturnToWorkshop()
        {
            if (IsReplica)
            {
                // Invité : le tapis affiché est celui de l'hôte (réplication) ; on quitte seulement l'écran de résultats.
                if (Book.Active == null && gameState.State != GameState.Workshop) gameState.SetState(GameState.Workshop);
                Changed?.Invoke();
                return;
            }
            rug.Unload();
            gameState.SetState(GameState.Workshop);
            Changed?.Invoke();
        }

        /// <summary>
        /// Réputation qui déverrouille les commandes du tableau. Solo et hôte : celle du joueur. Invité : celle de l'hôte,
        /// propriétaire du tableau de la session (fournie par la réplication), sans toucher à la réputation personnelle.
        /// </summary>
        public int BoardReputation => boardReputationOverride >= 0 ? boardReputationOverride : reputation.Points;

        int boardReputationOverride = -1;

        /// <summary>Invité : réputation de l'hôte pour le tableau de la session (-1 = celle du joueur).</summary>
        public void SetBoardReputation(int points)
        {
            boardReputationOverride = points;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Multijoueur (invité)

        /// <summary>Reproduit le démarrage décidé par l'hôte (le tapis est chargé par la réplication des surfaces).</summary>
        public void ApplyRemoteStart(OrderDefinition order, float elapsedSeconds)
        {
            if (order == null) return;
            Book.ForceActive(order);
            ElapsedSeconds = Mathf.Max(0f, elapsedSeconds);
            LastResult = null;
            objectiveNotified = false;
            if (gameState.State != GameState.ActiveOrder && gameState.State != GameState.Paused) gameState.SetState(GameState.ActiveOrder);
            OrderStarted?.Invoke(order);
            Changed?.Invoke();
        }

        /// <summary>Recale le chronomètre sur celui de l'hôte.</summary>
        public void ApplyRemoteElapsed(float elapsedSeconds) => ElapsedSeconds = Mathf.Max(0f, elapsedSeconds);

        /// <summary>
        /// Affiche la livraison validée par l'hôte : le résultat est recalculé avec les mesures de l'hôte (même règle pure),
        /// sans paiement local — l'argent et la réputation arrivent par la réplication de l'atelier.
        /// </summary>
        public OrderResult ApplyRemoteDelivery(OrderDefinition order, in RugCleaningSummary summary, float elapsedSeconds)
        {
            if (order == null) return null;
            snapshots.CaptureAfter();
            var result = OrderEvaluator.Evaluate(order, summary, elapsedSeconds);
            if (Book.Active != order) Book.ForceActive(order);
            Book.Complete(result);
            LastResult = result;
            if (audioManager != null && result.Accepted) audioManager.PlayInterface(rewardClip != null ? rewardClip : audioManager.ConfirmClip);
            gameState.SetState(GameState.Results);
            OrderFinished?.Invoke(result);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Retour à l'atelier libre décidé par l'hôte (abandon).</summary>
        public void ApplyRemoteReturn()
        {
            Book.ClearActive();
            if (gameState.State == GameState.ActiveOrder) gameState.SetState(GameState.Workshop);
            Changed?.Invoke();
        }

        /// <summary>Commandes terminées chez l'hôte (arrivée en cours de partie, livraison).</summary>
        public void ApplyRemoteCompleted(IEnumerable<string> ids)
        {
            Book.MarkCompleted(ids);
            Changed?.Invoke();
        }

        public static string StartResultMessage(OrderStartResult result)
        {
            switch (result)
            {
                case OrderStartResult.Requested: return "Demande envoyée à l'hôte…";
                case OrderStartResult.Started: return "Commande acceptée";
                case OrderStartResult.AnotherOrderActive: return "Terminez d'abord la commande en cours";
                case OrderStartResult.NotAvailable: return "Commande déjà terminée";
                case OrderStartResult.ReputationTooLow: return "Réputation insuffisante";
                case OrderStartResult.WorkAreaTooDirty: return "Aire de lavage trop sale : nettoyez-la avant d'accepter une commande";
                default: return "Commande inconnue";
            }
        }
    }
}
