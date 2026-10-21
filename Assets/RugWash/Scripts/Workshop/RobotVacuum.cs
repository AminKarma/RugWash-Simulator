using System;
using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using RugWash.Player;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>État sauvegardé du robot (position conservée s'il travaillait quand le joueur a quitté).</summary>
    [Serializable]
    public struct RobotState
    {
        public int mode;
        public float battery;
        public bool resumeAfterCharge;
        public float x;
        public float z;
        public float yaw;
    }

    /// <summary>
    /// Robot aspirateur de l'atelier. V0.4.2 : réservé à la zone de lavage 2 (annexe, débloquée par l'agrandissement de
    /// l'atelier) ; il travaille uniquement sur son aire (surface <see cref="floor"/>) et y traite toutes les salissures qui
    /// s'y déposent, avec les règles des outils : taches frottées (brosse douce), poussière et débris aspirés, eau sale, boue,
    /// mousse et savon ramassés (lame de sol dur). Avant la V0.4.2 : poussière, débris et traces sèches seulement.
    /// choisit les zones les plus sales, s'y rend par un chemin calculé sur une grille d'occupation qui couvre la zone et
    /// elle seule (bordures, objets et tapis posé évités ; la grille est recalculée quand un tapis arrive ou repart),
    /// s'arrête devant le joueur, rentre à sa base quand sa batterie faiblit ou quand on l'arrête, puis repart une fois
    /// rechargé. Voyant : vert = nettoyage, bleu = retour, ambre = charge, blanc = prêt, rouge = bloqué.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotVacuum : MonoBehaviour, IInteractable
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        enum Nav { Idle, Undocking, Following, Docking, Waiting }

        [Header("Références")]
        [Tooltip("Surface de travail du robot : la zone de nettoyage (aire de lavage). Il ne la quitte jamais.")]
        [SerializeField] RugCleaningController floor;
        [Tooltip("Tapis des commandes posé sur la zone : obstacle quand il est présent (grille recalculée à chaque pose/retrait).")]
        [SerializeField] RugCleaningController orderRug;
        [Tooltip("Pose de la base : position de recharge, axe Z vers la pièce.")]
        [SerializeField] Transform dock;
        [Tooltip("Racine des objets de l'atelier pris en compte comme obstacles au sol (même sans collider).")]
        [SerializeField] Transform obstacleRoot;
        [Tooltip("V0.4.2 : zone de lavage 2 (agrandissement) : le robot n'y travaille qu'une fois l'agrandissement acheté.")]
        [SerializeField] WorkshopExtension zone;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioSource motorSource;
        [SerializeField] AudioClip startClip;
        [SerializeField] AudioClip stopClip;
        [SerializeField] AudioClip bumpClip;
        [SerializeField] AudioClip dockedClip;
        [SerializeField] Renderer ledRenderer;
        [SerializeField] Transform sideBrush;
        [SerializeField] Transform turret;

        [Header("Déplacement")]
        [Tooltip("Vitesse (m/s) ; V0.4.2 : 0,55 (0,32 avant).")]
        [SerializeField] float speed = 0.55f;
        [SerializeField] float turnSpeed = 240f;
        [SerializeField] float radius = 0.17f;
        [SerializeField] float cellSize = 0.2f;
        [Tooltip("Couches des obstacles physiques (murs, meubles) et du joueur.")]
        [SerializeField] LayerMask obstacleMask = ~0;
        [SerializeField] LayerMask playerMask;

        [Header("Nettoyage")]
        [SerializeField] float cleanRadius = 0.16f;
        [SerializeField] float cleanPower = 1.2f;
        [Tooltip("V0.4.2 : frottage des taches fixées (fraction détachée par seconde à réponse nominale).")]
        [SerializeField] float scrubPower = 0.8f;
        [Tooltip("V0.4.2 : part de l'eau, de la boue, de la mousse et du savon ramassée par seconde sous le robot.")]
        [SerializeField, Range(0.1f, 6f)] float mopRate = 2.5f;
        [Tooltip("V0.4.2 : humidité apportée par seconde sur une case tachée (taches qui se brossent mal à sec).")]
        [SerializeField, Range(0f, 3f)] float sprayRate = 1.2f;
        [SerializeField, Range(0.01f, 0.3f)] float dirtTarget = 0.04f;
        [SerializeField] float revisitDelay = 25f;
        [Tooltip("Longueur de trajet préférée entre deux cibles (m) : le robot avance par longues lignes, comme un vrai robot.")]
        [SerializeField] float preferredLeg = 1.2f;

        readonly RobotVacuumBrain brain = new RobotVacuumBrain();
        readonly List<Vector2> path = new List<Vector2>();
        readonly Collider[] overlapBuffer = new Collider[16];
        readonly List<Bounds> floorObstacles = new List<Bounds>();
        OccupancyGrid occupancy;
        float[] lastVisit;
        Nav nav;
        int pathIndex;
        float waitTimer, stuckTimer, idleTimer;
        Vector3 stuckCheckPosition;
        int failedPlans;
        bool started, hasPendingState, bumped;
        RobotState pendingState;
        MaterialPropertyBlock ledBlock;
        Color ledColor = Color.clear;

        public RobotVacuumBrain Brain => brain;
        public RobotMode Mode => brain.Mode;
        public OccupancyGrid Occupancy => occupancy;
        public RugCleaningController Floor => floor;
        public float Speed => speed;
        /// <summary>V0.4.2 : la zone de lavage 2 est ouverte (agrandissement acheté) ; sans zone configurée, toujours vrai.</summary>
        public bool ZoneReady => zone == null || zone.IsUnlocked;
        /// <summary>Message quand le robot est acheté mais que la zone 2 n'est pas encore ouverte.</summary>
        public const string ZoneLockedMessage = "Robot aspirateur : il travaille dans la zone 2 (agrandissement de l'atelier à acheter)";
        public bool IsAtDock => (FlatPosition - DockFlat).magnitude < 0.06f && nav != Nav.Undocking;
        public bool IsWaitingForObstacle => nav == Nav.Waiting;
        /// <summary>Prochain point du chemin suivi (diagnostic, tests), ou null.</summary>
        public Vector2? NextWaypoint => nav == Nav.Following && pathIndex < path.Count ? path[pathIndex] : (Vector2?)null;
        /// <summary>Saleté totale aspirée depuis le chargement de la scène.</summary>
        public float TotalCleaned { get; private set; }
        /// <summary>V0.4.2 : eau et mousse ramassées par le robot (diagnostic, tests).</summary>
        public float LiquidCollected { get; private set; }
        /// <summary>V0.4.2 : eau seule ramassée par le robot (l'eau sèche aussi d'elle-même : les tests la mesurent ici).</summary>
        public float WaterCollected { get; private set; }
        /// <summary>V0.4.2 : eau apportée par le robot sur les taches (comptée à part de l'eau ramassée).</summary>
        public float WaterSprayed { get; private set; }

        /// <summary>V0.4.2 : humidification des seules cases tachées, plafonnée (jamais une flaque).</summary>
        struct RobotSprayOperation : ICellOperation
        {
            public float PerSecond, DeltaTime, Added;

            public void Apply(ref RugCell cell, float weight)
            {
                if (cell.Stain <= 0.01f || cell.Moisture >= 0.6f) return;
                float add = Mathf.Min(0.6f - cell.Moisture, PerSecond * DeltaTime * weight);
                cell.Moisture += add;
                Added += add;
            }
        }
        public int BumpCount { get; private set; }
        public string LastMessage { get; private set; }

        public event Action<string> Message;

        /// <summary>Message quand le robot n'a pas encore été acheté.</summary>
        public const string NotOwnedMessage = "Robot aspirateur à acheter sur l'ordinateur (Boutique)";

        bool owned = true;

        /// <summary>
        /// Faux tant que le robot n'a pas été acheté (v0.1.2) : il est caché et inerte, sa base indique où l'acheter.
        /// Solo et hôte : achat de l'hôte ; invité : état reçu de l'hôte (l'atelier est le sien).
        /// </summary>
        public bool IsOwned => owned;

        public void SetOwned(bool value)
        {
            if (owned == value) return;
            owned = value;
            if (!owned && started)
            {
                brain.Restore((int)RobotMode.Docked, brain.Battery01, false);
                SnapToDock();
            }
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            bool shown = owned && ZoneReady;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = shown;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = shown;
            if (!shown && motorSource != null && motorSource.isPlaying) motorSource.Stop();
        }

        /// <summary>V0.4.2 : la zone 2 s'ouvre ou se ferme : grille recalculée (murs et objets de l'annexe), robot sur sa base si elle ferme.</summary>
        void OnZoneChanged()
        {
            if (started)
            {
                BuildOccupancy();
                path.Clear();
                if (!ZoneReady)
                {
                    brain.Restore((int)RobotMode.Docked, brain.Battery01, false);
                    SnapToDock();
                }
                else if (nav == Nav.Following) nav = Nav.Idle;
            }
            ApplyVisibility();
        }

        /// <summary>
        /// Multijoueur, côté invité : le robot est simulé par l'hôte ; ici il suit la pose et l'état reçus, et un appui
        /// sur E envoie la demande à l'hôte. Null : solo ou hôte.
        /// </summary>
        public Core.IWorkshopRequests Remote { get; set; }
        public bool IsReplica => Remote != null;
        Vector3 remotePosition;
        float remoteYaw;
        bool hasRemotePose;

        /// <summary>Applique l'état diffusé par l'hôte (mode, batterie) et la cible de position, suivie en douceur.</summary>
        public void ApplyRemote(RobotState state)
        {
            if (!started) return;
            brain.Restore(state.mode, state.battery, state.resumeAfterCharge);
            remotePosition = new Vector3(state.x, FloorY, state.z);
            remoteYaw = state.yaw;
            if (!hasRemotePose) transform.SetPositionAndRotation(remotePosition, Quaternion.Euler(0f, remoteYaw, 0f));
            hasRemotePose = true;
            nav = Nav.Idle;
        }

        Vector2 FlatPosition => new Vector2(transform.position.x, transform.position.z);
        Vector2 DockFlat => new Vector2(dock.position.x, dock.position.z);
        Vector3 DockForward => Vector3.ProjectOnPlane(dock.forward, Vector3.up).normalized;
        Vector2 DockApproach => DockFlat + new Vector2(DockForward.x, DockForward.z) * 0.5f;
        float FloorY => floor != null ? floor.UVToWorld(new Vector2(0.5f, 0.5f)).y : 0f;

        public bool IsConfigured(out string missing)
        {
            missing = floor == null ? nameof(floor) : dock == null ? nameof(dock) : null;
            return missing == null;
        }

        void Awake()
        {
            ledBlock = new MaterialPropertyBlock();
            brain.ModeChanged += OnModeChanged;
            if (zone != null) zone.Changed += OnZoneChanged;
            if (orderRug != null)
            {
                orderRug.RugLoaded += OnRugChanged;
                orderRug.RugUnloaded += OnRugChanged;
            }
        }

        /// <summary>Un tapis arrive ou repart : la grille d'occupation est recalculée et le trajet en cours replanifié.</summary>
        void OnRugChanged(RugCleaningController rug)
        {
            if (!started) return;
            BuildOccupancy();
            path.Clear();
            if (nav == Nav.Following) nav = Nav.Idle;
        }

        void Start()
        {
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] Robot aspirateur : référence manquante '{missing}'.", this);
                enabled = false;
                return;
            }
            BuildOccupancy();
            started = true;
            if (hasPendingState) ApplyState(pendingState);
            else SnapToDock();
            ApplyVisibility();
        }

        void OnDestroy()
        {
            brain.ModeChanged -= OnModeChanged;
            if (zone != null) zone.Changed -= OnZoneChanged;
            if (orderRug != null)
            {
                orderRug.RugLoaded -= OnRugChanged;
                orderRug.RugUnloaded -= OnRugChanged;
            }
        }

        // ------------------------------------------------------------------ Carte des obstacles

        /// <summary>Construit la grille d'occupation : colliders physiques et objets posés au sol (gonflés du rayon du robot).</summary>
        public void BuildOccupancy()
        {
            Vector2 size = floor.SizeMeters;
            Vector3 center = floor.transform.position;
            var min = new Vector2(center.x - size.x * 0.5f, center.z - size.y * 0.5f);
            var max = new Vector2(center.x + size.x * 0.5f, center.z + size.y * 0.5f);
            occupancy = new OccupancyGrid(min, max, cellSize);
            CollectFloorObstacles();
            float y = FloorY;
            occupancy.Build(xz => IsBlockedAt(xz, y));
            lastVisit = new float[occupancy.Width * occupancy.Height];
            for (int i = 0; i < lastVisit.Length; i++) lastVisit[i] = float.NegativeInfinity;
            // La base et son accès restent libres même si un objet voisin déborde.
            ClearAround(DockFlat, 0.25f);
            ClearAround(DockApproach, 0.2f);
        }

        void CollectFloorObstacles()
        {
            floorObstacles.Clear();
            if (obstacleRoot == null) return;
            float y = FloorY;
            foreach (var renderer in obstacleRoot.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.transform.IsChildOf(transform) || renderer.transform.IsChildOf(dock)) continue;
                if (renderer.GetComponentInParent<RugCleaningController>() != null) continue;
                var bounds = renderer.bounds;
                if (bounds.min.y > y + 0.14f || bounds.max.y < y + 0.015f) continue;
                floorObstacles.Add(bounds);
            }
        }

        bool IsBlockedAt(Vector2 xz, float floorY)
        {
            float half = cellSize * 0.5f + radius;
            // Volume testé à partir de 5 mm au-dessus de la surface : un tapis fin (1 cm) est bien un obstacle.
            var center = new Vector3(xz.x, floorY + 0.065f, xz.y);
            var extents = new Vector3(half, 0.06f, half);
            int count = Physics.OverlapBoxNonAlloc(center, extents, overlapBuffer, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = overlapBuffer[i];
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(dock)) continue;
                if ((playerMask.value & (1 << hit.gameObject.layer)) != 0) continue;
                if (hit.GetComponentInParent<RugCleaningController>() == floor) continue;
                return true;
            }
            var box = new Bounds(center, extents * 2f);
            foreach (var bounds in floorObstacles)
                if (bounds.Intersects(box)) return true;
            return false;
        }

        void ClearAround(Vector2 xz, float radiusMeters)
        {
            var c = occupancy.WorldToCell(xz);
            int r = Mathf.CeilToInt(radiusMeters / cellSize);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                occupancy.SetBlocked(new Vector2Int(c.x + dx, c.y + dy), false);
        }

        // ------------------------------------------------------------------ Boucle

        void Update()
        {
            float dt = Time.deltaTime;
            if (!started || dt <= 0f || !owned || !ZoneReady) return;
            if (IsReplica)
            {
                if (hasRemotePose)
                {
                    float k = 1f - Mathf.Exp(-12f * dt);
                    transform.SetPositionAndRotation(Vector3.Lerp(transform.position, remotePosition, k),
                        Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, remoteYaw, 0f), k));
                }
                UpdatePresentation(dt);
                return;
            }

            brain.Tick(dt, nav == Nav.Idle && IsAtDock);
            switch (brain.Mode)
            {
                case RobotMode.Docked:
                    nav = Nav.Idle;
                    break;
                case RobotMode.Cleaning:
                    UpdateCleaning(dt);
                    break;
                case RobotMode.Returning:
                    UpdateReturning(dt);
                    break;
            }
            UpdatePresentation(dt);
        }

        void UpdateCleaning(float dt)
        {
            if (nav == Nav.Undocking)
            {
                if (MoveTowards(DockApproach, dt, avoidPlayer: true)) nav = Nav.Idle;
            }
            else if (nav == Nav.Idle || nav == Nav.Docking)
            {
                if (!PlanToNextDirtySpot()) return;
            }
            else FollowPath(dt);

            // V0.4.2 : toutes les salissures de l'aire, avec les règles des outils (rien n'est effacé directement) : les taches
            // fixées sont frottées (détachées), la poussière et les débris secs aspirés, puis l'eau sale, la boue détachée, la
            // mousse et le savon ramassés sous la lame et gardés dans la cuve du robot.
            // Comme une autolaveuse : un peu d'eau sur les taches fixées (elles se brossent mal à sec), ramassée ensuite.
            var spray = new RobotSprayOperation { PerSecond = sprayRate, DeltaTime = dt };
            floor.ApplyOperation(transform.position, cleanRadius, 4f, ref spray);
            WaterSprayed += spray.Added;
            var scrub = new BrushOperation { Table = floor.DirtTable, Power = scrubPower, DeltaTime = dt };
            floor.ApplyOperation(transform.position, cleanRadius, 4f, ref scrub);
            var vacuum = new VacuumOperation { Table = floor.DirtTable, Power = cleanPower, DeltaTime = dt };
            floor.ApplyOperation(transform.position, cleanRadius, 4f, ref vacuum);
            var mop = new GatherOperation { Efficiency = mopRate * dt };
            floor.ApplyOperation(transform.position, cleanRadius, 4f, ref mop);
            TotalCleaned += vacuum.Removed + mop.Total;
            LiquidCollected += mop.Water + mop.Foam;
            WaterCollected += mop.Water;
            MarkVisited(FlatPosition);
        }

        void UpdateReturning(float dt)
        {
            if (nav == Nav.Docking)
            {
                if (MoveTowards(DockFlat, dt, avoidPlayer: true))
                {
                    Face(-DockForward, 1000f);
                    nav = Nav.Idle;
                }
                return;
            }
            if (nav == Nav.Undocking || nav == Nav.Idle)
            {
                if ((FlatPosition - DockApproach).magnitude < 0.08f)
                {
                    nav = Nav.Docking;
                    return;
                }
                if (!PlanTo(DockApproach))
                {
                    // Aucun chemin (joueur ou objet déplacé) : nouvel essai un peu plus tard.
                    nav = Nav.Waiting;
                    waitTimer = 1f;
                }
                return;
            }
            if (FollowPath(dt)) nav = Nav.Docking;
        }

        bool PlanToNextDirtySpot()
        {
            if (!TryFindDirtySpot(out Vector2 target))
            {
                Notify("Robot aspirateur : aire de lavage propre, retour à la base");
                brain.FinishCleaning();
                return false;
            }
            if (PlanTo(target)) return true;
            MarkVisited(target, revisitDelay * 2f);
            return false;
        }

        /// <summary>
        /// Prochaine cible : zone sale (poussière et traces sèches) non visitée récemment, de préférence à environ
        /// <see cref="preferredLeg"/> mètres et dans la direction actuelle — le robot balaie la pièce par longues lignes
        /// et va en priorité là où il y a le plus à aspirer.
        /// </summary>
        public bool TryFindDirtySpot(out Vector2 target)
        {
            target = default;
            float best = float.MinValue;
            float now = Time.time;
            Vector2 here = FlatPosition;
            var forward = new Vector2(transform.forward.x, transform.forward.z).normalized;
            for (int y = 0; y < occupancy.Height; y++)
            for (int x = 0; x < occupancy.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (occupancy.IsBlocked(cell) || now - lastVisit[y * occupancy.Width + x] < revisitDelay) continue;
                Vector2 xz = occupancy.CellCenter(cell);
                Vector2 offset = xz - here;
                float distance = offset.magnitude;
                if (distance < 0.3f) continue;
                float dirt = VacuumableDirt(xz);
                if (dirt < dirtTarget) continue;
                float heading = Vector2.Dot(forward, offset / distance);
                float score = dirt * 3f - Mathf.Abs(distance - preferredLeg) * 0.25f + heading * 0.5f;
                if (score <= best) continue;
                best = score;
                target = xz;
            }
            return best > float.MinValue;
        }

        float VacuumableDirt(Vector2 xz)
        {
            if (!floor.TrySampleCell(new Vector3(xz.x, 0f, xz.y), out RugCell cell)) return 0f;
            return RobotDirt(cell);
        }

        /// <summary>
        /// V0.4.2 : ce que le robot sait traiter sur une case : poussière, débris, taches (frottées), boue détachée, mousse, savon
        /// et eau stagnante (au-delà d'une légère humidité, qui sèche seule).
        /// </summary>
        public static float RobotDirt(in RugCell cell) =>
            cell.Dust + cell.Loosened + cell.Stain + cell.Foam + cell.Residue + Mathf.Max(0f, cell.Moisture - 0.08f) * 0.5f;

        bool PlanTo(Vector2 target)
        {
            path.Clear();
            pathIndex = 0;
            if (!occupancy.TryNearestFree(occupancy.WorldToCell(FlatPosition), 3, out Vector2Int start)) return false;
            if (!occupancy.TryNearestFree(occupancy.WorldToCell(target), 3, out Vector2Int goal)) return false;
            var cells = GridPathfinder.Simplify(occupancy, GridPathfinder.FindPath(occupancy, start, goal));
            if (cells == null)
            {
                failedPlans++;
                return false;
            }
            for (int i = 1; i < cells.Count; i++) path.Add(occupancy.CellCenter(cells[i]));
            if (path.Count == 0) path.Add(target);
            failedPlans = 0;
            nav = Nav.Following;
            stuckTimer = 0f;
            stuckCheckPosition = transform.position;
            return true;
        }

        /// <returns>Vrai à l'arrivée du dernier point.</returns>
        bool FollowPath(float dt)
        {
            if (nav == Nav.Waiting)
            {
                // Obstacle imprévu (le joueur) : il attend, puis recalcule un chemin.
                waitTimer -= dt;
                if (waitTimer <= 0f) nav = Nav.Idle;
                return false;
            }
            if (pathIndex >= path.Count)
            {
                nav = Nav.Idle;
                return true;
            }

            if (MoveTowards(path[pathIndex], dt, avoidPlayer: true)) pathIndex++;
            if (nav == Nav.Waiting) return false;

            // Détection de blocage : s'il n'avance plus, il recalcule son chemin.
            stuckTimer += dt;
            if (stuckTimer > 2.5f)
            {
                if ((transform.position - stuckCheckPosition).magnitude < 0.03f) nav = Nav.Idle;
                stuckTimer = 0f;
                stuckCheckPosition = transform.position;
            }
            if (pathIndex >= path.Count)
            {
                nav = Nav.Idle;
                return true;
            }
            return false;
        }

        /// <summary>Tourne vers la cible puis avance ; s'arrête devant le joueur ou un obstacle imprévu.</summary>
        bool MoveTowards(Vector2 target, float dt, bool avoidPlayer)
        {
            Vector2 delta = target - FlatPosition;
            float distance = delta.magnitude;
            if (distance < 0.03f) return true;
            var direction = new Vector3(delta.x / distance, 0f, delta.y / distance);
            float angle = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            Face(direction, turnSpeed * dt);
            if (Mathf.Abs(angle) > 25f) return false;

            if (avoidPlayer && SomethingAhead(direction))
            {
                if (!bumped)
                {
                    bumped = true;
                    BumpCount++;
                    if (audioManager != null) audioManager.PlayAt(bumpClip, transform.position, 0.5f);
                }
                nav = Nav.Waiting;
                waitTimer = 1.2f;
                return false;
            }
            bumped = false;
            float step = Mathf.Min(distance, speed * dt);
            transform.position += direction * step;
            return step >= distance - 1e-4f;
        }

        /// <summary>
        /// Capteur avant : volume vertical juste devant le robot, sur toute la hauteur du joueur (une capsule de personnage
        /// est arrondie en bas : un test au ras du sol la manquerait). Le robot s'arrête à une vingtaine de centimètres.
        /// </summary>
        bool SomethingAhead(Vector3 direction)
        {
            Vector3 center = transform.position + direction * (radius + 0.1f);
            return Physics.CheckCapsule(center + Vector3.up * 0.1f, center + Vector3.up * 0.9f, radius * 0.8f, playerMask, QueryTriggerInteraction.Ignore);
        }

        void Face(Vector3 direction, float maxDegrees)
        {
            if (direction.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), maxDegrees);
        }

        void MarkVisited(Vector2 xz, float extraDelay = 0f)
        {
            var c = occupancy.WorldToCell(xz);
            if (!occupancy.InBounds(c)) return;
            lastVisit[c.y * occupancy.Width + c.x] = Time.time + extraDelay;
        }

        // ------------------------------------------------------------------ Modes, présentation

        void OnModeChanged(RobotMode previous, RobotMode next)
        {
            switch (next)
            {
                case RobotMode.Cleaning:
                    nav = previous == RobotMode.Docked ? Nav.Undocking : Nav.Idle;
                    if (audioManager != null) audioManager.PlayAt(startClip, transform.position, 0.7f);
                    break;
                case RobotMode.Returning:
                    nav = Nav.Idle;
                    if (audioManager != null) audioManager.PlayAt(stopClip, transform.position, 0.7f);
                    if (brain.ResumeAfterCharge) Notify("Robot aspirateur : batterie faible, retour en charge");
                    break;
                case RobotMode.Docked:
                    nav = Nav.Idle;
                    SnapToDock();
                    if (audioManager != null) audioManager.PlayAt(dockedClip, transform.position, 0.7f);
                    break;
            }
        }

        void UpdatePresentation(float dt)
        {
            bool moving = brain.Mode != RobotMode.Docked;
            if (sideBrush != null && brain.Mode == RobotMode.Cleaning) sideBrush.Rotate(0f, 720f * dt, 0f, Space.Self);
            if (turret != null && moving) turret.Rotate(0f, 240f * dt, 0f, Space.Self);

            if (motorSource != null)
            {
                float volume = moving ? (brain.Mode == RobotMode.Cleaning ? 0.55f : 0.35f) : 0f;
                float category = audioManager != null ? audioManager.VolumeFor(AudioCategory.Effects) : 1f;
                motorSource.volume = Mathf.MoveTowards(motorSource.volume, volume * category, dt * 2f);
                motorSource.pitch = nav == Nav.Waiting ? 0.9f : 1f;
                if (motorSource.volume > 0.001f && !motorSource.isPlaying) motorSource.Play();
                else if (motorSource.volume <= 0.001f && motorSource.isPlaying) motorSource.Stop();
            }

            if (ledRenderer == null) return;
            Color color;
            if (nav == Nav.Waiting) color = Mathf.Repeat(Time.time * 2f, 1f) < 0.5f ? new Color(1f, 0.15f, 0.1f) : new Color(0.3f, 0.05f, 0.03f);
            else if (brain.Mode == RobotMode.Cleaning) color = new Color(0.2f, 1f, 0.35f);
            else if (brain.Mode == RobotMode.Returning) color = new Color(0.25f, 0.55f, 1f);
            else if (brain.IsCharging) color = new Color(1f, 0.6f, 0.1f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 3f));
            else color = new Color(0.9f, 0.95f, 1f);
            if (color == ledColor) return;
            ledColor = color;
            ledBlock.SetColor(BaseColorId, color);
            ledBlock.SetColor(EmissionId, color * 2.5f);
            ledRenderer.SetPropertyBlock(ledBlock);
        }

        void SnapToDock()
        {
            if (dock == null) return;
            transform.SetPositionAndRotation(new Vector3(dock.position.x, FloorY, dock.position.z), Quaternion.LookRotation(-DockForward, Vector3.up));
            nav = Nav.Idle;
            path.Clear();
        }

        void Notify(string message)
        {
            LastMessage = message;
            Message?.Invoke(message);
        }

        // ------------------------------------------------------------------ Sauvegarde

        public RobotState CaptureState() => new RobotState
        {
            mode = (int)brain.Mode,
            battery = brain.Battery01,
            resumeAfterCharge = brain.ResumeAfterCharge,
            x = transform.position.x,
            z = transform.position.z,
            yaw = transform.eulerAngles.y,
        };

        /// <summary>
        /// Restaure l'état sauvegardé : sur la base, ou à sa position s'il travaillait. Une position invalide ou hors de la zone
        /// (ancienne sauvegarde : le robot circulait sur le sol de la salle) le ramène sur sa base.
        /// </summary>
        public void ApplyState(RobotState state)
        {
            if (!started)
            {
                pendingState = state;
                hasPendingState = true;
                return;
            }
            hasPendingState = false;
            brain.Restore(state.mode, state.battery, state.resumeAfterCharge);
            path.Clear();
            var cell = occupancy.WorldToCell(new Vector2(state.x, state.z));
            if (brain.Mode == RobotMode.Docked || occupancy.IsBlocked(cell))
            {
                SnapToDock();
                if (brain.Mode != RobotMode.Docked) nav = Nav.Undocking;
                return;
            }
            transform.SetPositionAndRotation(new Vector3(state.x, FloorY, state.z), Quaternion.Euler(0f, state.yaw, 0f));
            nav = Nav.Idle;
        }

        // ------------------------------------------------------------------ Interaction

        public string GetPrompt(PlayerInteractor interactor) => !owned ? $"Base du robot aspirateur — {NotOwnedMessage.ToLowerInvariant()}"
            : !ZoneReady ? ZoneLockedMessage
            : $"{(brain.Mode == RobotMode.Cleaning || brain.ResumeAfterCharge ? "Arrêter" : "Démarrer")} le robot aspirateur ({brain.Describe()})";

        public bool CanInteract(PlayerInteractor interactor) => started;

        public string Interact(PlayerInteractor interactor) => Toggle();

        /// <summary>Démarre ou arrête le robot ; retourne le message affiché au joueur.</summary>
        public string Toggle()
        {
            if (!owned) return NotOwnedMessage;
            if (!ZoneReady) return ZoneLockedMessage;
            if (Remote != null)
            {
                Remote.RequestRobotToggle();
                return "Demande envoyée à l'hôte";
            }
            switch (brain.Toggle())
            {
                case RobotToggleResult.Started: return "Robot aspirateur démarré";
                case RobotToggleResult.WillStartWhenCharged: return "Batterie trop faible : le robot partira une fois rechargé";
                default: return "Robot aspirateur arrêté : retour à la base";
            }
        }
    }
}
