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
    /// <para>Grande mise à jour : plusieurs zones de lavage (la zone 1, puis celles de l'agrandissement, <see cref="RugBay"/>),
    /// chacune avec son tapis, son aire, sa commande et son chronomètre — deux tapis se nettoient en même temps.
    /// <see cref="ActiveOrder"/>, <see cref="Deliver()"/> et <see cref="Abandon()"/> visent la zone courante (celle où se
    /// trouve le joueur si elle a une commande, sinon la première zone occupée). Une commande peut aussi être tenue hors
    /// zone (tapis dans la machine à laver) : elle reste active dans le carnet, son chronomètre est suspendu.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrderManager : MonoBehaviour
    {
        [SerializeField] List<OrderDefinition> orders = new List<OrderDefinition>();
        [SerializeField] RugCleaningController rug;
        [Tooltip("Aire de lavage : propreté minimale exigée avant une nouvelle commande (optionnel : sans elle, aucune condition).")]
        [SerializeField] WorkSurface workSurface;
        [Tooltip("Zones de lavage supplémentaires (agrandissement de l'atelier) ; la zone 1 est le tapis et l'aire ci-dessus.")]
        [SerializeField] List<RugBay> extraBays = new List<RugBay>();
        [SerializeField] RugSnapshotService snapshots;
        [SerializeField] GameStateManager gameState;
        [SerializeField] WalletManager wallet;
        [SerializeField] ReputationManager reputation;
        [SerializeField] AudioManager audioManager;
        [Tooltip("Son joué à la livraison d'une commande acceptée (sinon le son de confirmation).")]
        [SerializeField] AudioClip rewardClip;
        [Tooltip("Missions générées sans fin en plus des contrats du catalogue (v0.1.2).")]
        [SerializeField] bool generatedMissions = true;
        [Tooltip("Outils achetés : un objectif n'est proposé que si l'équipement permet de l'atteindre (optionnel).")]
        [SerializeField] UpgradeManager upgrades;

        /// <summary>Une zone de lavage et sa commande.</summary>
        sealed class Bay
        {
            public int Index;
            public RugBay Source;
            public RugCleaningController Rug;
            public WorkSurface Surface;
            public OrderDefinition Order;
            public float Elapsed;
            public bool ObjectiveNotified;
            /// <summary>Commande livrée : le tapis reste posé jusqu'au retour à l'atelier (écran de résultats).</summary>
            public bool PendingUnload;
            public Action<RugCleaningController> OnFinished;
            public bool IsUnlocked => Source == null || Source.IsUnlocked;
            public bool IsFree => Order == null && !PendingUnload;
        }

        readonly List<Bay> bays = new List<Bay>();
        /// <summary>Commandes actives tenues hors zone (tapis dans la machine à laver).</summary>
        readonly List<OrderDefinition> held = new List<OrderDefinition>();
        readonly List<string> activeIds = new List<string>();

        public OrderBook Book { get; private set; }
        /// <summary>Offres de missions générées (graine, numéro, offres) : solo et hôte ; un invité reçoit celles de l'hôte.</summary>
        public MissionBoard Missions { get; } = new MissionBoard();
        readonly Dictionary<string, OrderDefinition> generated = new Dictionary<string, OrderDefinition>();
        List<RugDefinition> missionRugs = new List<RugDefinition>();
        bool remoteOffers;

        /// <summary>
        /// Multijoueur, côté invité : les demandes (accepter, livrer, abandonner) partent vers l'hôte ; l'état revient par
        /// les méthodes « Remote ». L'évaluation et le paiement n'ont jamais lieu chez un invité. Null : solo ou hôte.
        /// </summary>
        public IWorkshopRequests Remote { get; set; }
        public bool IsReplica => Remote != null;
        /// <summary>Position du joueur local : désigne la zone courante (la plus proche).</summary>
        public Transform Viewer { get; set; }
        /// <summary>Difficulté des prochaines missions générées (paramètres du joueur ; solo et hôte).</summary>
        public Func<int> DifficultySource { get; set; }
        public int CurrentMissionDifficulty => DifficultySource != null ? DifficultySource() : (int)Orders.MissionDifficulty.Normal;

        /// <summary>Commande de la zone courante (null si aucune zone n'a de commande).</summary>
        public OrderDefinition ActiveOrder => Book != null && bays.Count > 0 ? bays[CurrentBay].Order : null;
        public float ElapsedSeconds => bays.Count > 0 ? bays[CurrentBay].Elapsed : 0f;
        public OrderResult LastResult { get; private set; }
        public int Reputation => reputation.Points;

        public event Action<OrderDefinition> OrderStarted;
        public event Action<OrderResult> OrderFinished;
        /// <summary>Déclenché une seule fois par commande quand la propreté atteint l'objectif.</summary>
        public event Action<OrderDefinition> ObjectiveReached;

        public bool ObjectiveNotified => bays.Count > 0 && bays[CurrentBay].ObjectiveNotified;
        public event Action Changed;

        /// <summary>Aire de lavage de la zone 1 (peut être nulle).</summary>
        public WorkSurface WorkSurface => workSurface;

        public bool IsConfigured(out string missing)
        {
            missing = orders.Count == 0 || orders.Contains(null) ? nameof(orders)
                : rug == null ? nameof(rug)
                : snapshots == null ? nameof(snapshots)
                : gameState == null ? nameof(gameState)
                : wallet == null ? nameof(wallet)
                : reputation == null ? nameof(reputation)
                : extraBays.Contains(null) ? nameof(extraBays) : null;
            if (missing == null)
                foreach (var bay in extraBays)
                    if (!bay.IsConfigured(out string detail))
                    {
                        missing = $"{nameof(extraBays)}.{bay.name}.{detail}";
                        break;
                    }
            return missing == null;
        }

        void Awake()
        {
            Book = new OrderBook(orders);
            missionRugs = MissionGenerator.RugsFrom(orders);
            bays.Clear();
            bays.Add(new Bay { Index = 0, Rug = rug, Surface = workSurface });
            foreach (var extra in extraBays)
                if (extra != null && extra.Rug != null)
                    bays.Add(new Bay { Index = bays.Count, Source = extra, Rug = extra.Rug, Surface = extra.WorkSurface });
            foreach (var bay in bays)
            {
                var captured = bay;
                bay.OnFinished = _ => OnRugFinished(captured);
            }
        }

        void Start()
        {
            // Sans sauvegarde (scène de test) : nouvelle graine. Avec sauvegarde, SaveManager a déjà restauré les offres.
            if (generatedMissions && !Missions.IsInitialized && !IsReplica && !remoteOffers) RestoreMissions(0, 0, null, null);
        }

        void OnDestroy()
        {
            foreach (var order in generated.Values)
                if (order != null) Destroy(order);
            generated.Clear();
        }

        void OnEnable()
        {
            foreach (var bay in bays)
            {
                if (bay.Rug != null) bay.Rug.Finished += bay.OnFinished;
                if (bay.Source != null) bay.Source.UnlockedChanged += OnBayUnlockedChanged;
            }
        }

        void OnDisable()
        {
            foreach (var bay in bays)
            {
                if (bay.Rug != null) bay.Rug.Finished -= bay.OnFinished;
                if (bay.Source != null) bay.Source.UnlockedChanged -= OnBayUnlockedChanged;
            }
        }

        void OnBayUnlockedChanged(RugBay bay) => Changed?.Invoke();

        // ---------------------------------------------------------------- Zones de lavage

        public int BayCount => bays.Count;
        public int UnlockedBayCount
        {
            get
            {
                int count = 0;
                foreach (var bay in bays)
                    if (bay.IsUnlocked) count++;
                return count;
            }
        }

        bool Valid(int index) => index >= 0 && index < bays.Count;
        public bool IsBayUnlocked(int index) => Valid(index) && bays[index].IsUnlocked;
        public RugCleaningController RugOf(int index) => Valid(index) ? bays[index].Rug : null;
        public WorkSurface WorkSurfaceOf(int index) => Valid(index) ? bays[index].Surface : null;
        public OrderDefinition OrderOf(int index) => Valid(index) ? bays[index].Order : null;
        public float ElapsedOf(int index) => Valid(index) ? bays[index].Elapsed : 0f;
        public bool IsBayFree(int index) => Valid(index) && bays[index].IsUnlocked && bays[index].IsFree;
        public static string BayLabel(int index) => $"Zone {index + 1}";

        /// <summary>Zone qui porte cette commande, ou -1 (machine à laver, commande inconnue).</summary>
        public int BayOf(OrderDefinition order)
        {
            if (order == null) return -1;
            for (int i = 0; i < bays.Count; i++)
                if (bays[i].Order == order) return i;
            return -1;
        }

        /// <summary>Zone de ce tapis (ou de cette aire de lavage), ou -1.</summary>
        public int BayOf(RugCleaningController surface)
        {
            if (surface == null) return -1;
            for (int i = 0; i < bays.Count; i++)
                if (bays[i].Rug == surface || (bays[i].Surface != null && bays[i].Surface.Surface == surface)) return i;
            return -1;
        }

        /// <summary>Commande du tapis posé sur cette surface (machines d'appoint de la zone 1), ou null.</summary>
        public OrderDefinition OrderOn(RugCleaningController surface) => OrderOf(BayOf(surface));

        /// <summary>Nombre de zones qui portent une commande.</summary>
        public int ActiveBayCount
        {
            get
            {
                int count = 0;
                foreach (var bay in bays)
                    if (bay.Order != null) count++;
                return count;
            }
        }

        bool AnyBayOrder => ActiveBayCount > 0;

        /// <summary>Une zone débloquée est libre (une commande peut y être acceptée).</summary>
        public bool HasFreeBay
        {
            get
            {
                foreach (var bay in bays)
                    if (bay.IsUnlocked && bay.IsFree) return true;
                return false;
            }
        }

        /// <summary>Zone débloquée la plus proche du joueur local (zone 1 sans repère).</summary>
        public int FocusBay
        {
            get
            {
                if (Viewer == null || bays.Count < 2) return 0;
                Vector3 at = Viewer.position;
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < bays.Count; i++)
                {
                    if (!bays[i].IsUnlocked || bays[i].Rug == null) continue;
                    Vector3 delta = bays[i].Rug.transform.position - at;
                    float distance = delta.x * delta.x + delta.z * delta.z;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }
                return best;
            }
        }

        /// <summary>Zone courante : celle du joueur si elle a une commande, sinon la première zone occupée, sinon celle du joueur.</summary>
        public int CurrentBay
        {
            get
            {
                if (bays.Count == 0) return 0;
                int focus = FocusBay;
                if (bays[focus].Order != null) return focus;
                for (int i = 0; i < bays.Count; i++)
                    if (bays[i].Order != null) return i;
                return focus;
            }
        }

        /// <summary>Tapis de la zone courante (inspection, scan, HUD).</summary>
        public RugCleaningController CurrentRug => bays.Count > 0 ? bays[CurrentBay].Rug : rug;

        /// <summary>Commandes actives tenues hors zone (tapis dans la machine à laver).</summary>
        public IReadOnlyList<OrderDefinition> HeldOrders => held;
        public bool IsHeld(OrderDefinition order) => order != null && held.Contains(order);

        // ---------------------------------------------------------------- Missions

        /// <summary>Capacités d'équipement utilisées pour générer de nouvelles missions.</summary>
        public int MissionCapabilities => upgrades != null ? MissionRules.CapabilitiesFrom(id => upgrades.Stats.IsUnlocked(id)) : 0;

        /// <summary>Reprend les missions sauvegardées (graine 0 : nouvelle partie) puis complète les offres.</summary>
        public void RestoreMissions(int seed, int sequence, IEnumerable<string> offers, string lastCompleted)
        {
            if (!generatedMissions) return;
            Missions.Restore(seed, sequence, offers, lastCompleted);
            RefillMissions();
            Changed?.Invoke();
        }

        void RefillMissions()
        {
            if (!generatedMissions || IsReplica || remoteOffers || !Missions.IsInitialized) return;
            activeIds.Clear();
            foreach (var order in Book.ActiveOrders) activeIds.Add(order.id);
            Missions.Difficulty = CurrentMissionDifficulty;
            Missions.RefillKeeping(reputation.Points, MissionCapabilities, key => MissionGenerator.Signature(key, missionRugs), activeIds);
            var ids = new List<string>(Missions.Offers.Count);
            foreach (var offer in Missions.Offers) ids.Add(offer.Id);
            SyncGeneratedOffers(ids);
        }

        /// <summary>Le carnet propose exactement ces missions générées (les commandes actives sont conservées).</summary>
        void SyncGeneratedOffers(IReadOnlyCollection<string> ids)
        {
            var keep = new HashSet<string>(ids);
            for (int i = Book.Orders.Count - 1; i >= 0; i--)
            {
                var order = Book.Orders[i];
                if (MissionKey.IsGenerated(order.id) && !keep.Contains(order.id)) Book.Remove(order);
            }
            foreach (string id in ids)
                if (Book.Find(id) == null)
                {
                    var order = Resolve(id);
                    if (order != null) Book.Add(order);
                }
        }

        /// <summary>
        /// Commande désignée par un identifiant : contrat du catalogue, mission proposée, ou mission régénérée depuis sa clé
        /// (invité en ligne, livraison reçue après le remplacement de l'offre). Null si l'identifiant est inconnu ou invalide.
        /// </summary>
        public OrderDefinition Resolve(string id)
        {
            if (string.IsNullOrEmpty(id) || Book == null) return null;
            var found = Book.Find(id);
            if (found != null) return found;
            if (generated.TryGetValue(id, out var cached) && cached != null) return cached;
            if (!MissionKey.TryParse(id, out var key)) return null;
            var order = MissionGenerator.Create(key, missionRugs);
            if (order != null) generated[id] = order;
            return order;
        }

        /// <summary>Multijoueur (invité) : missions proposées sur le tableau de l'hôte.</summary>
        public void ApplyRemoteOffers(IEnumerable<string> ids)
        {
            remoteOffers = true;
            var list = new List<string>();
            if (ids != null)
                foreach (string id in ids)
                    if (MissionKey.IsGenerated(id) && !list.Contains(id) && list.Count < MissionBoard.MaxOffers) list.Add(id);
            SyncGeneratedOffers(list);
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Déroulement

        /// <summary>Tapis terminé (moins de 1 % de saleté) : l'objectif de propreté est forcément atteint.</summary>
        void OnRugFinished(Bay bay)
        {
            if (bay.Order == null || bay.ObjectiveNotified) return;
            bay.ObjectiveNotified = true;
            ObjectiveReached?.Invoke(bay.Order);
        }

        bool AreaAllows(int index) => bays[index].Surface == null || bays[index].Surface.AllowsNewOrder;

        /// <summary>
        /// Vrai si une zone libre a une aire assez propre pour accepter une commande (toujours vrai sans aire configurée, et
        /// quand aucune zone n'est libre : la raison du refus est alors l'occupation).
        /// </summary>
        public bool WorkAreaAllowsNewOrder
        {
            get
            {
                bool anyFree = false;
                for (int i = 0; i < bays.Count; i++)
                {
                    if (!IsBayFree(i)) continue;
                    anyFree = true;
                    if (AreaAllows(i)) return true;
                }
                return !anyFree;
            }
        }

        void Update()
        {
            if (gameState.State != GameState.ActiveOrder) return;
            float dt = Time.deltaTime;
            foreach (var bay in bays)
            {
                if (bay.Order == null) continue;
                bay.Elapsed += dt;
                if (!bay.ObjectiveNotified && bay.Rug.Grid != null && bay.Rug.Cleanliness01 >= bay.Order.objectives.targetCleanliness)
                {
                    bay.ObjectiveNotified = true;
                    ObjectiveReached?.Invoke(bay.Order);
                }
            }
        }

        /// <summary>
        /// Zone où démarrer une commande : la zone voulue si elle est libre et son aire assez propre, sinon une autre zone
        /// libre. -1 avec la raison : toutes les zones occupées, ou aires libres trop sales.
        /// </summary>
        int PickBay(int preferred, out OrderStartResult problem)
        {
            problem = OrderStartResult.AnotherOrderActive;
            bool anyFree = false;
            if (IsBayFree(preferred))
            {
                anyFree = true;
                if (AreaAllows(preferred))
                {
                    problem = OrderStartResult.Started;
                    return preferred;
                }
            }
            for (int i = 0; i < bays.Count; i++)
            {
                if (i == preferred || !IsBayFree(i)) continue;
                anyFree = true;
                if (!AreaAllows(i)) continue;
                problem = OrderStartResult.Started;
                return i;
            }
            if (anyFree) problem = OrderStartResult.WorkAreaTooDirty;
            return -1;
        }

        /// <summary>Accepte une commande dans la zone du joueur si elle est libre, sinon dans une autre zone libre.</summary>
        public OrderStartResult TryStart(OrderDefinition order) => TryStart(order, -1);

        /// <summary>Accepte une commande ; <paramref name="preferredBay"/> = -1 : zone du joueur.</summary>
        public OrderStartResult TryStart(OrderDefinition order, int preferredBay)
        {
            var check = Book.CanStart(order, BoardReputation, int.MaxValue);
            if (check != OrderStartResult.Started) return check;
            int bay = PickBay(Valid(preferredBay) ? preferredBay : FocusBay, out var problem);
            if (bay < 0) return problem;
            if (Remote != null)
            {
                Remote.RequestStartOrder(order.id, bay);
                return OrderStartResult.Requested;
            }
            return StartOn(order, bay);
        }

        OrderStartResult StartOn(OrderDefinition order, int index)
        {
            var result = Book.Start(order, BoardReputation, int.MaxValue);
            if (result != OrderStartResult.Started) return result;
            var bay = bays[index];
            bay.Order = order;
            bay.Elapsed = 0f;
            bay.ObjectiveNotified = false;
            bay.PendingUnload = false;
            LastResult = null;
            bay.Rug.LoadRug(order.rug, order.dirt, order.dirtSeed);
            SyncGameplayState();
            OrderStarted?.Invoke(order);
            Changed?.Invoke();
            return result;
        }

        /// <summary>
        /// Atelier libre ou commande en cours selon les zones occupées. Une pause garde son état de reprise à jour ; l'écran
        /// de résultats n'est quitté que par le retour à l'atelier.
        /// </summary>
        void SyncGameplayState() => gameState.SetGameplayState(AnyBayOrder ? GameState.ActiveOrder : GameState.Workshop);

        /// <summary>Recommence la commande de la zone courante (saleté régénérée, chronomètre remis à zéro).</summary>
        public bool RestartActive()
        {
            var bay = bays[CurrentBay];
            if (bay.Order == null || IsReplica) return false;
            bay.Elapsed = 0f;
            bay.ObjectiveNotified = false;
            bay.Rug.LoadRug(bay.Order.rug, bay.Order.dirt, bay.Order.dirtSeed);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Estimation de la note actuelle de la zone courante (aucun effet sur l'état).</summary>
        public OrderResult Preview() => Preview(CurrentBay);

        public OrderResult Preview(int index)
        {
            if (!Valid(index)) return null;
            var bay = bays[index];
            return bay.Order == null || bay.Rug.Grid == null ? null : OrderEvaluator.Evaluate(bay.Order, bay.Rug.Grid.ComputeSummary(), bay.Elapsed);
        }

        /// <summary>Livre la commande de la zone courante.</summary>
        public OrderResult Deliver() => Deliver(CurrentBay);

        /// <summary>Livre la commande d'une zone. Le paiement n'a lieu qu'une fois : sans commande active, rien ne se passe.</summary>
        public OrderResult Deliver(int index)
        {
            if (!Valid(index)) return null;
            var bay = bays[index];
            var order = bay.Order;
            if (order == null) return null;
            if (Remote != null)
            {
                Remote.RequestDeliver(index);
                return null;
            }
            if (bay.Rug.Grid == null) return null;

            snapshots.CaptureAfter(bay.Rug);
            var result = OrderEvaluator.Evaluate(order, bay.Rug.Grid.ComputeSummary(), bay.Elapsed);
            result.Bay = index;
            if (!Book.Complete(result)) return null;
            bay.Order = null;
            bay.PendingUnload = true;

            if (result.Accepted) wallet.Wallet.Add(result.Reward);
            reputation.Add(result.ReputationDelta);
            // Mission générée livrée : remplacée par la suivante avant la sauvegarde (déclenchée par OrderFinished).
            if (result.Accepted && MissionKey.IsGenerated(order.id) && Missions.Retire(order.id)) Book.Remove(order);
            RefillMissions();
            LastResult = result;
            if (audioManager != null && result.Accepted) audioManager.PlayInterface(rewardClip != null ? rewardClip : audioManager.ConfirmClip);

            gameState.SetState(GameState.Results);
            OrderFinished?.Invoke(result);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Abandonne la commande de la zone courante.</summary>
        public bool Abandon() => Abandon(CurrentBay);

        public bool Abandon(int index)
        {
            if (!Valid(index) || bays[index].Order == null) return false;
            if (Remote != null)
            {
                Remote.RequestAbandon(index);
                return true;
            }
            var bay = bays[index];
            if (!Book.Abandon(bay.Order)) return false;
            bay.Order = null;
            bay.PendingUnload = false;
            bay.Rug.Unload();
            // Comme avant : un abandon (aussi depuis la pause) ramène au jeu, sauf pendant l'écran de résultats.
            if (gameState.State != GameState.Results) gameState.SetState(AnyBayOrder ? GameState.ActiveOrder : GameState.Workshop);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Retour à l'atelier après les résultats : le tapis livré est retiré (aucun tapis hors commande) ; les autres zones
        /// gardent leur commande.
        /// </summary>
        public void ReturnToWorkshop()
        {
            foreach (var bay in bays)
            {
                if (!bay.PendingUnload) continue;
                bay.PendingUnload = false;
                // Invité : le tapis affiché est celui de l'hôte (réplication) ; on quitte seulement l'écran de résultats.
                if (!IsReplica && bay.Order == null) bay.Rug.Unload();
            }
            if (gameState.State == GameState.Results || GameStateRules.IsGameplay(gameState.State))
                gameState.SetState(AnyBayOrder ? GameState.ActiveOrder : GameState.Workshop);
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Machine à laver (autorité : solo, hôte)

        /// <summary>
        /// Machine à laver : le tapis de la zone part en machine. La commande reste active (tenue par la machine), son
        /// chronomètre est suspendu ; la zone est libérée pour une autre commande.
        /// </summary>
        public bool TryHoldForMachine(int index, out OrderDefinition order, out float elapsed)
        {
            order = null;
            elapsed = 0f;
            if (IsReplica || !Valid(index)) return false;
            var bay = bays[index];
            if (bay.Order == null || bay.PendingUnload || bay.Rug.Grid == null) return false;
            order = bay.Order;
            elapsed = bay.Elapsed;
            held.Add(order);
            bay.Order = null;
            bay.Elapsed = 0f;
            bay.ObjectiveNotified = false;
            snapshots.Park(bay.Rug, order.id);
            bay.Rug.Unload();
            SyncGameplayState();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Machine à laver : le tapis lavé revient dans une zone libre avec sa commande et son chronomètre ;
        /// <paramref name="restore"/> reproduit son état (grille sauvegardée à l'entrée, puis lavage).
        /// </summary>
        public bool TryPlaceFromMachine(OrderDefinition order, float elapsed, int index, Action<RugCleaningController> restore)
        {
            if (IsReplica || order == null || !held.Contains(order) || !IsBayFree(index)) return false;
            var bay = bays[index];
            held.Remove(order);
            bay.Order = order;
            bay.Elapsed = Mathf.Max(0f, elapsed);
            bay.ObjectiveNotified = false;
            bay.PendingUnload = false;
            snapshots.Unpark(bay.Rug, order.id);
            bay.Rug.LoadRug(order.rug, order.dirt, order.dirtSeed);
            restore?.Invoke(bay.Rug);
            SyncGameplayState();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Abandon d'une commande tenue par la machine : elle redevient disponible.</summary>
        public bool AbandonHeld(OrderDefinition order)
        {
            if (IsReplica || order == null || !held.Remove(order)) return false;
            snapshots.Forget(order.id);
            Book.Abandon(order);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Chargement d'une sauvegarde : la commande dont le tapis est dans la machine redevient active.</summary>
        public bool RestoreHeld(OrderDefinition order)
        {
            if (order == null || Book.StateOf(order) == OrderState.Completed || BayOf(order) >= 0) return false;
            Book.ForceActive(order);
            if (!held.Contains(order)) held.Add(order);
            RefillMissions();
            Changed?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- Réputation du tableau

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

        /// <summary>La commande n'est plus portée nulle part (ni zone, ni machine) : elle quitte les commandes actives.</summary>
        void ReleaseRemote(OrderDefinition order)
        {
            if (order != null && BayOf(order) < 0 && !held.Contains(order)) Book.ClearActive(order);
        }

        /// <summary>Reproduit le démarrage décidé par l'hôte (le tapis est chargé par la réplication des surfaces).</summary>
        public void ApplyRemoteStart(OrderDefinition order, float elapsedSeconds, int index = 0)
        {
            if (order == null || !Valid(index)) return;
            var bay = bays[index];
            if (bay.Order == order)
            {
                bay.Elapsed = Mathf.Max(0f, elapsedSeconds);
                return;
            }
            var previous = bay.Order;
            int other = BayOf(order);
            if (other >= 0) bays[other].Order = null;
            held.Remove(order);
            Book.ForceActive(order);
            bay.Order = order;
            ReleaseRemote(previous);
            bay.Elapsed = Mathf.Max(0f, elapsedSeconds);
            bay.ObjectiveNotified = false;
            bay.PendingUnload = false;
            LastResult = null;
            SyncGameplayState();
            OrderStarted?.Invoke(order);
            Changed?.Invoke();
        }

        /// <summary>Recale le chronomètre d'une zone sur celui de l'hôte.</summary>
        public void ApplyRemoteElapsed(float elapsedSeconds, int index = 0)
        {
            if (Valid(index)) bays[index].Elapsed = Mathf.Max(0f, elapsedSeconds);
        }

        /// <summary>
        /// Affiche la livraison validée par l'hôte : le résultat est recalculé avec les mesures de l'hôte (même règle pure),
        /// sans paiement local — l'argent et la réputation arrivent par la réplication de l'atelier.
        /// </summary>
        public OrderResult ApplyRemoteDelivery(OrderDefinition order, in RugCleaningSummary summary, float elapsedSeconds, int index = 0)
        {
            if (order == null) return null;
            snapshots.CaptureAfter(Valid(index) ? bays[index].Rug : rug);
            var result = OrderEvaluator.Evaluate(order, summary, elapsedSeconds);
            result.Bay = index;
            Book.ForceActive(order);
            Book.Complete(result);
            int bay = BayOf(order);
            if (bay >= 0) bays[bay].Order = null;
            if (Valid(index)) bays[index].PendingUnload = true;
            held.Remove(order);
            // L'hôte remplace toujours une mission générée livrée : elle quitte aussi le tableau de l'invité.
            if (result.Accepted && MissionKey.IsGenerated(order.id)) Book.Remove(order);
            LastResult = result;
            if (audioManager != null && result.Accepted) audioManager.PlayInterface(rewardClip != null ? rewardClip : audioManager.ConfirmClip);
            gameState.SetState(GameState.Results);
            OrderFinished?.Invoke(result);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Plus de commande dans cette zone, décidé par l'hôte (abandon, tapis parti en machine).</summary>
        public void ApplyRemoteReturn(int index = 0)
        {
            if (!Valid(index)) return;
            var bay = bays[index];
            var order = bay.Order;
            bay.Order = null;
            ReleaseRemote(order);
            SyncGameplayState();
            Changed?.Invoke();
        }

        /// <summary>Invité : commande tenue par la machine à laver de l'hôte (null : machine vide).</summary>
        public void ApplyRemoteHeld(OrderDefinition order)
        {
            bool changed = false;
            for (int i = held.Count - 1; i >= 0; i--)
            {
                if (held[i] == order) continue;
                var previous = held[i];
                held.RemoveAt(i);
                ReleaseRemote(previous);
                changed = true;
            }
            // Ajoutée même si sa zone n'a pas encore été libérée (mises à jour reçues dans le désordre) : quand la zone se
            // libère, la commande reste active, tenue par la machine.
            if (order != null && !held.Contains(order))
            {
                held.Add(order);
                Book.ForceActive(order);
                changed = true;
            }
            if (changed) Changed?.Invoke();
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
                case OrderStartResult.AnotherOrderActive: return "Toutes les zones de lavage sont occupées : terminez d'abord une commande en cours";
                case OrderStartResult.NotAvailable: return "Commande déjà terminée";
                case OrderStartResult.ReputationTooLow: return "Réputation insuffisante";
                case OrderStartResult.WorkAreaTooDirty: return "Aire de lavage trop sale : nettoyez-la avant d'accepter une commande";
                default: return "Commande inconnue";
            }
        }
    }
}
