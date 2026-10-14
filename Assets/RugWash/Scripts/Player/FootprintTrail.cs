using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Traces de pas : en marchant dans l'eau sale, la mousse ou la boue de l'aire de lavage, les semelles se chargent ;
    /// chaque pas suivant laisse une empreinte (de moins en moins marquée) sur l'aire. Des semelles boueuses marquent aussi
    /// le tapis de la commande (boue seulement, moins que sur un sol dur). Le sol de la salle (hors zone de nettoyage)
    /// n'est jamais marqué. Les empreintes sont de vraies données de surface (aucun objet créé), nettoyables ; les bottes
    /// réduisent la boue transférée sans l'annuler.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootprintTrail : MonoBehaviour
    {
        [SerializeField] FirstPersonController controller;
        [SerializeField] LayerMask surfaceMask = ~0;
        [SerializeField] float strideMeters = 0.7f;
        [SerializeField] float footSpacing = 0.11f;
        [SerializeField] float printRadius = 0.09f;
        [SerializeField, Range(1f, 4f)] float printAspect = 2.4f;
        [Tooltip("Part de la saleté de la cellule reprise par la semelle à chaque pas.")]
        [SerializeField, Range(0f, 1f)] float pickup = 0.5f;
        [Tooltip("Part de la charge de la semelle déposée à chaque pas.")]
        [SerializeField, Range(0f, 1f)] float deposit = 0.35f;
        [Tooltip("Part de la boue déposée sur un tapis, par rapport à un sol dur (les fibres en retiennent moins).")]
        [SerializeField, Range(0f, 1f)] float rugDepositScale = 0.5f;
        [Tooltip("Équipement du joueur : les bottes réduisent la boue transférée (optionnel).")]
        [SerializeField] PlayerEquipment equipment;

        readonly Dictionary<Collider, RugCleaningController> cache = new Dictionary<Collider, RugCleaningController>();
        FootstepCadence cadence;
        bool leftFoot;
        DirtType loadType = DirtType.Soil;

        public float MudLoad { get; private set; }
        public float WaterLoad { get; private set; }
        public float FoamLoad { get; private set; }
        public int FootprintsLeft { get; private set; }
        /// <summary>Empreintes boueuses laissées sur un tapis.</summary>
        public int RugFootprintsLeft { get; private set; }
        /// <summary>Boue réellement déposée (cumul) : sert à mesurer l'effet des bottes.</summary>
        public float MudDeposited { get; private set; }
        /// <summary>Part de la boue des semelles transférée à chaque pas (bottes : moins de 1).</summary>
        public float Transfer => equipment != null ? Mathf.Clamp01(equipment.FootprintTransfer) : 1f;

        /// <summary>Remet les semelles propres (réapparition, changement de scène, tests).</summary>
        public void CleanSoles() => MudLoad = WaterLoad = FoamLoad = 0f;

        void Awake() => cadence = new FootstepCadence(strideMeters);

        void Update()
        {
            if (controller == null) return;
            Vector3 velocity = controller.Velocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            if (!cadence.Advance(horizontal.magnitude, controller.IsGrounded, Time.deltaTime)) return;
            Step(horizontal.sqrMagnitude > 1e-4f ? horizontal.normalized : transform.forward);
        }

        /// <summary>Pose un pied : dépose la charge actuelle de la semelle, puis reprend un peu de ce qui est au sol.</summary>
        public void Step(Vector3 forward)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 foot = transform.position + right * (leftFoot ? -footSpacing : footSpacing);
            leftFoot = !leftFoot;
            if (!Physics.Raycast(foot + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.7f, surfaceMask, QueryTriggerInteraction.Ignore)) return;
            var surface = Resolve(hit.collider);
            if (surface == null || !surface.AcceptsDirt) return;
            bool onRug = surface.Kind == SurfaceKind.Rug;
            float transfer = Transfer;

            if (onRug)
            {
                // Tapis : seule la boue marque les fibres (l'eau et la mousse des semelles s'y perdent sans trace).
                if (MudLoad * transfer > 0.01f)
                {
                    var print = new DepositOperation { Type = loadType, Mud = MudLoad * deposit * 3f * transfer * rugDepositScale };
                    surface.ApplyOperation(hit.point, printRadius, 6f, printAspect, forward, default, ref print);
                    MudDeposited += print.Mud;
                    RugFootprintsLeft++;
                }
                MudLoad *= 1f - deposit;
                WaterLoad *= 1f - deposit;
                FoamLoad *= 1f - deposit;
                return;
            }

            if (MudLoad + WaterLoad + FoamLoad > 0.02f)
            {
                var print = new DepositOperation
                {
                    Type = loadType,
                    Mud = MudLoad * deposit * 3f * transfer,
                    Water = WaterLoad * deposit * 3f * transfer,
                    Foam = FoamLoad * deposit * 3f * transfer,
                };
                surface.ApplyOperation(hit.point, printRadius, 6f, printAspect, forward, default, ref print);
                MudDeposited += print.Mud;
                MudLoad *= 1f - deposit;
                WaterLoad *= 1f - deposit;
                FoamLoad *= 1f - deposit;
                FootprintsLeft++;
            }

            if (surface.TrySampleCell(hit.point, out RugCell cell))
            {
                if (cell.Loosened > 0.02f && cell.StainType != DirtType.None) loadType = cell.StainType;
                MudLoad = Mathf.Min(1f, MudLoad + cell.Loosened * pickup);
                WaterLoad = Mathf.Min(1f, WaterLoad + cell.Moisture * pickup);
                FoamLoad = Mathf.Min(1f, FoamLoad + cell.Foam * pickup * 0.5f);
            }
        }

        RugCleaningController Resolve(Collider collider)
        {
            if (!cache.TryGetValue(collider, out var surface))
            {
                surface = collider.GetComponentInParent<RugCleaningController>();
                cache[collider] = surface;
            }
            return surface;
        }
    }
}
