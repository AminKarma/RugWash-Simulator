using RugWash.Audio;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Anime le support des outils tenus en main : balancement selon les mouvements de souris,
    /// léger balancier de marche, inertie lors des changements de direction, et montée de l'outil (avec son)
    /// à chaque changement. L'intensité du balancement est réglable (0 = désactivé) ; elle ne touche pas la visée.
    /// Les outils animent leur propre modèle par-dessus (brossage, recul…).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ViewModelRig : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] FirstPersonController controller;
        [SerializeField] PlayerToolController tools;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip equipClip;

        [Header("Balancement souris")]
        [SerializeField] float swayAmount = 0.0006f;
        [SerializeField] float swayMax = 0.03f;
        [SerializeField] float swayRotation = 0.08f;
        [SerializeField] float swaySmoothing = 10f;

        [Header("Marche")]
        [SerializeField] float bobFrequency = 1.7f;
        [SerializeField] float bobHorizontal = 0.006f;
        [SerializeField] float bobVertical = 0.008f;

        [Header("Inertie (changements de direction)")]
        [SerializeField] float inertiaAmount = 0.004f;
        [SerializeField] float inertiaMax = 0.02f;
        [SerializeField] float inertiaSmoothing = 6f;

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
        float intensity = 1f;
        Vector3 inertia;
        Vector3 previousLocalVelocity;

        /// <summary>0 = aucun balancement (le changement d'outil reste animé), 1 = nominal.</summary>
        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Clamp01(value);
        }

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

        bool hasEquippedOnce;

        void OnToolChanged(CleaningToolBase tool)
        {
            equipProgress = 0f;
            // Pas de son pour l'outil équipé au chargement de la scène.
            if (hasEquippedOnce && audioManager != null) audioManager.PlayEffect(equipClip, 0.7f);
            hasEquippedOnce = true;
        }

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

            // Inertie : l'outil « traîne » légèrement à l'opposé de l'accélération (démarrage, arrêt, virage).
            Vector3 localVelocity = controller != null ? controller.transform.InverseTransformDirection(new Vector3(velocity.x, 0f, velocity.z)) : Vector3.zero;
            Vector3 acceleration = (localVelocity - previousLocalVelocity) / dt;
            previousLocalVelocity = localVelocity;
            var targetInertia = Vector3.ClampMagnitude(new Vector3(-acceleration.x, 0f, -acceleration.z) * inertiaAmount, inertiaMax);
            inertia = Vector3.Lerp(inertia, targetInertia, 1f - Mathf.Exp(-inertiaSmoothing * dt));

            equipProgress = Mathf.MoveTowards(equipProgress, 1f, dt / Mathf.Max(0.01f, equipDuration));
            float hidden = 1f - EaseOut(equipProgress);

            Vector3 motion = (new Vector3(sway.x, sway.y, 0f) + bob + inertia) * intensity;
            transform.localPosition = restPosition + motion + new Vector3(0f, -equipDrop * hidden, 0f);
            transform.localRotation = restRotation * Quaternion.Euler((sway.y * swayRotation * 100f) * intensity + equipTilt * hidden,
                (-sway.x * swayRotation * 100f) * intensity, 0f);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
    }
}
