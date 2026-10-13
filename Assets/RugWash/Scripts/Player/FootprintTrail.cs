using System.Collections.Generic;
using RugWash.Audio;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Traces de pas : en marchant dans l'eau sale, la mousse ou la boue du plan de travail, les semelles se chargent ;
    /// chaque pas suivant laisse une empreinte (de moins en moins marquée) sur le plan de travail. Ni les tapis, ni le sol
    /// de la salle (hors zone de nettoyage) ne sont marqués. Les empreintes sont de vraies données de surface, nettoyables.
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

        readonly Dictionary<Collider, RugCleaningController> cache = new Dictionary<Collider, RugCleaningController>();
        FootstepCadence cadence;
        bool leftFoot;
        DirtType loadType = DirtType.Soil;

        public float MudLoad { get; private set; }
        public float WaterLoad { get; private set; }
        public float FoamLoad { get; private set; }
        public int FootprintsLeft { get; private set; }

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
            if (surface == null || surface.Kind == SurfaceKind.Rug || !surface.AcceptsDirt) return;

            if (MudLoad + WaterLoad + FoamLoad > 0.02f)
            {
                var print = new DepositOperation
                {
                    Type = loadType,
                    Mud = MudLoad * deposit * 3f,
                    Water = WaterLoad * deposit * 3f,
                    Foam = FoamLoad * deposit * 3f,
                };
                surface.ApplyOperation(hit.point, printRadius, 6f, printAspect, forward, default, ref print);
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
