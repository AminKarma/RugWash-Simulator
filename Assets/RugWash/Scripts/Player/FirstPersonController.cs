using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Contrôleur FPS : CharacterController (collisions, marches), accélération/décélération,
    /// gravité, orientation souris en degrés par pixel et stick droit en degrés par seconde (indépendantes du framerate).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] Transform cameraPivot;

        [Header("Déplacement")]
        [SerializeField] float walkSpeed = 3.2f;
        [SerializeField] float sprintSpeed = 5.2f;
        [SerializeField] float acceleration = 14f;
        [SerializeField] float deceleration = 18f;
        [SerializeField] float gravity = -20f;

        [Header("Caméra")]
        [Tooltip("Degrés par pixel de déplacement souris.")]
        [SerializeField, Range(0.01f, 0.5f)] float lookSensitivity = 0.1f;
        [Tooltip("Vitesse de rotation au stick droit poussé à fond (degrés par seconde).")]
        [SerializeField, Range(30f, 360f)] float stickLookSpeed = 150f;
        [SerializeField] bool invertY;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        CharacterController controller;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;

        public float LookSensitivity
        {
            get => lookSensitivity;
            set => lookSensitivity = Mathf.Clamp(value, 0.01f, 0.5f);
        }

        public float StickLookSpeed
        {
            get => stickLookSpeed;
            set => stickLookSpeed = Mathf.Clamp(value, 30f, 360f);
        }

        public bool InvertY
        {
            get => invertY;
            set => invertY = value;
        }

        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
        /// <summary>Inclinaison de la caméra en degrés (positif = vers le bas).</summary>
        public float Pitch => pitch;
        /// <summary>Vitesse de marche réduite par l'outil en cours (monobrosse : machine lourde) ; 1 = normale.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        public bool IsGrounded => controller != null && controller.isGrounded;
        /// <summary>Vrai pendant une course (touche maintenue et déplacement).</summary>
        public bool IsSprinting { get; private set; }
        /// <summary>Chute (ragdoll) : ni regard ni déplacement commandés ; l'élan reçu s'amortit, la gravité s'applique.</summary>
        public bool ControlLocked { get; set; }

        /// <summary>Élan horizontal imposé (chute) ; il s'amortit ensuite comme un arrêt.</summary>
        public void Push(Vector3 horizontal) => horizontalVelocity = new Vector3(horizontal.x, 0f, horizontal.z);

        public bool IsConfigured(out string missing)
        {
            missing = input == null ? nameof(input) : cameraPivot == null ? nameof(cameraPivot) : null;
            return missing == null;
        }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] FirstPersonController : référence manquante '{missing}'.", this);
                enabled = false;
                return;
            }
            pitch = NormalizePitch(cameraPivot.localEulerAngles.x);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (ControlLocked)
            {
                Move(Vector2.zero, false, dt);
                return;
            }
            Look(input.LookDelta * lookSensitivity + input.LookStick * (stickLookSpeed * dt));
            Move(input.Move, input.SprintHeld, dt);
        }

        /// <summary>Rotation en degrés (x : lacet, y : tangage).</summary>
        void Look(Vector2 degrees)
        {
            if (degrees.sqrMagnitude <= 0f) return;
            transform.Rotate(0f, degrees.x, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch + (invertY ? degrees.y : -degrees.y), minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move(Vector2 moveInput, bool sprint, float dt)
        {
            Vector3 wish = transform.right * moveInput.x + transform.forward * moveInput.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            IsSprinting = sprint && wish.sqrMagnitude > 0.01f;
            Vector3 target = wish * (sprint ? sprintSpeed : walkSpeed) * SpeedMultiplier;
            float rate = target.sqrMagnitude > 0.0001f ? acceleration : deceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, rate * dt);

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * dt;

            CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
        }

        /// <summary>Place le joueur (spawn, tests) sans conflit avec le CharacterController.</summary>
        public void Teleport(Vector3 position, float yaw, float newPitch = 0f)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = true;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        static float NormalizePitch(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
