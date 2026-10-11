using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Anime le support des outils tenus en main : balancement selon les mouvements de souris,
    /// léger balancier de marche, et montée de l'outil à chaque changement (l'outil précédent est rangé).
    /// Les outils animent leur propre modèle par-dessus (brossage, recul…).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ViewModelRig : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] FirstPersonController controller;
        [SerializeField] PlayerToolController tools;

        [Header("Balancement souris")]
        [SerializeField] float swayAmount = 0.0006f;
        [SerializeField] float swayMax = 0.03f;
        [SerializeField] float swayRotation = 0.08f;
        [SerializeField] float swaySmoothing = 10f;

        [Header("Marche")]
        [SerializeField] float bobFrequency = 1.7f;
        [SerializeField] float bobHorizontal = 0.006f;
        [SerializeField] float bobVertical = 0.008f;

        [Header("Changement d'outil")]
        [SerializeField] float equipDuration = 0.22f;
        [SerializeField] float equipDrop = 0.22f;
        [SerializeField] float equipTilt = 28f;

        Vector3 restPosition;
        Quaternion restRotation;
        Vector2 sway;
        float bobPhase;
        float bobWeight;
        float equipProgress = 1f;

        /// <summary>1 quand l'outil est complètement levé.</summary>
        public float EquipProgress => equipProgress;
        public Vector3 Offset => transform.localPosition - restPosition;

        public bool IsConfigured(out string missing)
        {
            missing = input == null ? nameof(input) : controller == null ? nameof(controller) : tools == null ? nameof(tools) : null;
            return missing == null;
        }

        void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            if (tools != null) tools.ActiveToolChanged += OnToolChanged;
        }

        void OnDestroy()
        {
            if (tools != null) tools.ActiveToolChanged -= OnToolChanged;
        }

        void OnToolChanged(CleaningToolBase tool) => equipProgress = 0f;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 look = input != null ? input.LookDelta : Vector2.zero;
            var targetSway = new Vector2(Mathf.Clamp(-look.x * swayAmount, -swayMax, swayMax), Mathf.Clamp(-look.y * swayAmount, -swayMax, swayMax));
            sway = Vector2.Lerp(sway, targetSway, 1f - Mathf.Exp(-swaySmoothing * dt));

            Vector3 velocity = controller != null ? controller.Velocity : Vector3.zero;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            bool grounded = controller != null && controller.IsGrounded;
            bobWeight = Mathf.MoveTowards(bobWeight, grounded ? Mathf.Clamp01(speed / 3.2f) : 0f, dt * 4f);
            bobPhase += speed * bobFrequency * dt;
            var bob = new Vector3(Mathf.Cos(bobPhase * Mathf.PI) * bobHorizontal, -Mathf.Abs(Mathf.Sin(bobPhase * Mathf.PI)) * bobVertical, 0f) * bobWeight;

            equipProgress = Mathf.MoveTowards(equipProgress, 1f, dt / Mathf.Max(0.01f, equipDuration));
            float hidden = 1f - EaseOut(equipProgress);

            transform.localPosition = restPosition + new Vector3(sway.x, sway.y, 0f) + bob + new Vector3(0f, -equipDrop * hidden, 0f);
            transform.localRotation = restRotation * Quaternion.Euler(sway.y * swayRotation * 100f + equipTilt * hidden, -sway.x * swayRotation * 100f, 0f);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
    }
}
