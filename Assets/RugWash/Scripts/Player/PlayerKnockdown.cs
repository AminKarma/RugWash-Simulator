using System;
using RugWash.Cleaning;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>Origine d'une chute (valeurs figées : réseau).</summary>
    public enum KnockCause : byte
    {
        /// <summary>Glissade sur la mousse.</summary>
        Fall = 0,
        /// <summary>Coup de batte (farce).</summary>
        Bat = 1,
        /// <summary>Fusil de farces : KO cartoon, la tête éclate (silhouette vue par les autres).</summary>
        Blaster = 2,
    }

    /// <summary>
    /// Chute contrôlée du joueur local (v0.1.2) : contrôles coupés, le corps glisse selon l'élan reçu (borné), la caméra
    /// tombe au sol en s'inclinant, puis le joueur se relève. La capsule de collision reste debout : impossible de passer
    /// sous le sol ou de rester coincé, et le relevé est garanti (durée bornée). Les autres joueurs voient un vrai ragdoll
    /// de la silhouette (<see cref="AvatarRagdoll"/>), piloté par l'état « à terre » seulement.
    /// Déclencheurs : glissade en courant sur une mousse épaisse (réglable), et appel direct (gadgets, phase 4).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerKnockdown : MonoBehaviour
    {
        enum Phase
        {
            Up,
            Falling,
            Down,
            GettingUp,
        }

        [SerializeField] FirstPersonController controller;
        [SerializeField] CameraMotion cameraMotion;
        [SerializeField] PlayerToolController tools;
        [SerializeField] PlayerInteractor interactor;
        [Tooltip("Entrées : « Interagir » relève le joueur plus tôt (optionnel).")]
        [SerializeField] PlayerInputReader input;

        [Header("Chute")]
        [Tooltip("Élan maximal transmis au corps (m/s) : pas de projection excessive.")]
        [SerializeField, Min(0f)] float maxImpulse = 10f;
        [SerializeField, Range(0.5f, 5f)] float minDuration = 1f;
        [Tooltip("Durée maximale d'une chute (le KO du fusil dure 5 s) : jamais d'état bloqué.")]
        [SerializeField, Range(0.5f, 8f)] float maxDuration = 6f;
        [SerializeField, Range(0.1f, 1f)] float fallSeconds = 0.35f;
        [SerializeField, Range(0.2f, 1.5f)] float getUpSeconds = 0.6f;
        [Tooltip("Hauteur des yeux une fois à terre.")]
        [SerializeField, Range(0.15f, 1f)] float groundEyeHeight = 0.32f;
        [SerializeField, Range(0f, 90f)] float groundRoll = 70f;
        [Tooltip("À terre depuis au moins ce délai, « Interagir » relève le joueur aussitôt (s).")]
        [SerializeField, Range(0.2f, 3f)] float quickRecoverAfter = 0.5f;

        [Header("Glissade sur la mousse")]
        [SerializeField] bool slipOnFoam = true;
        [SerializeField, Range(0.05f, 1f)] float slipFoam = 0.45f;
        [SerializeField, Min(0f)] float slipMinSpeed = 4f;
        [SerializeField, Min(0f)] float slipCooldown = 30f;
        [SerializeField, Range(0.5f, 4f)] float slipDuration = 1.6f;
        [SerializeField] LayerMask surfaceMask = ~0;

        Phase phase = Phase.Up;
        float timer, duration, rollSign = 1f, slipReadyAt;
        float eyeDrop = 1.3f;

        public bool IsDown => phase != Phase.Up;
        /// <summary>Origine de la chute en cours (ou de la dernière).</summary>
        public KnockCause Cause { get; private set; }
        /// <summary>Avancement visuel de la chute (0 debout, 1 à terre).</summary>
        public float FallAmount { get; private set; }
        public int KnockdownCount { get; private set; }
        public event Action<Vector3, float> KnockedDown;
        public event Action Recovered;
        public event Action<string> Message;

        /// <summary>Rappel affiché à la chute : comment se relever plus vite.</summary>
        public string QuickRecoverHint => input != null ? $"{input.Label("Interact")} pour vous relever" : "relevé dans un instant";

        /// <summary>Règle du relevé rapide (pure) : à terre (chute finie) depuis assez longtemps.</summary>
        public static bool CanQuickRecover(bool onGround, float secondsDown, float minimum) => onGround && secondsDown >= minimum;

        /// <summary>Relève le joueur plus tôt (« Interagir ») ; faux si c'est trop tôt.</summary>
        public bool TryQuickRecover()
        {
            // KO du fusil : relevé automatique à la fin du délai (pas de relevé anticipé).
            if (Cause == KnockCause.Blaster) return false;
            if (!CanQuickRecover(phase == Phase.Down, timer + fallSeconds, quickRecoverAfter)) return false;
            Next(Phase.GettingUp);
            return true;
        }

        void Start()
        {
            if (cameraMotion != null && controller != null)
                eyeDrop = Mathf.Max(0.2f, cameraMotion.transform.position.y - controller.transform.position.y - groundEyeHeight);
        }

        /// <summary>Fait chuter le joueur ; faux s'il est déjà à terre. Élan et durée sont bornés.</summary>
        public bool Knockdown(Vector3 impulse, float seconds, string message = null, KnockCause cause = KnockCause.Fall)
        {
            if (IsDown || controller == null) return false;
            Cause = cause;
            impulse = Vector3.ClampMagnitude(impulse, maxImpulse);
            duration = Mathf.Clamp(seconds, minDuration, maxDuration);
            var horizontal = new Vector3(impulse.x, 0f, impulse.z);
            rollSign = Vector3.Dot(horizontal, controller.transform.right) >= 0f ? -1f : 1f;
            controller.ControlLocked = true;
            controller.Push(horizontal);
            if (tools != null) tools.Locked = true;
            if (interactor != null) interactor.enabled = false;
            phase = Phase.Falling;
            timer = 0f;
            KnockdownCount++;
            KnockedDown?.Invoke(impulse, duration);
            if (!string.IsNullOrEmpty(message)) Message?.Invoke(message);
            return true;
        }

        /// <summary>Relève immédiatement le joueur (fin de partie, téléportation, réapparition).</summary>
        public void ForceRecover()
        {
            if (!IsDown) return;
            phase = Phase.Up;
            FallAmount = 0f;
            ApplyCamera();
            Unlock();
        }

        void Unlock()
        {
            if (controller != null) controller.ControlLocked = false;
            if (tools != null) tools.Locked = false;
            if (interactor != null) interactor.enabled = true;
            Recovered?.Invoke();
        }

        void OnDisable() => ForceRecover();

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            switch (phase)
            {
                case Phase.Up:
                    CheckSlip();
                    return;
                case Phase.Falling:
                    timer += dt;
                    FallAmount = Ease(Mathf.Clamp01(timer / fallSeconds));
                    if (timer >= fallSeconds) Next(Phase.Down);
                    break;
                case Phase.Down:
                    timer += dt;
                    FallAmount = 1f;
                    if (timer >= duration) Next(Phase.GettingUp);
                    else if (input != null && input.InteractPressed) TryQuickRecover();
                    break;
                case Phase.GettingUp:
                    timer += dt;
                    FallAmount = 1f - Ease(Mathf.Clamp01(timer / getUpSeconds));
                    if (timer >= getUpSeconds)
                    {
                        phase = Phase.Up;
                        FallAmount = 0f;
                        Unlock();
                    }
                    break;
            }
            ApplyCamera();
        }

        void Next(Phase next)
        {
            phase = next;
            timer = 0f;
        }

        void ApplyCamera()
        {
            if (cameraMotion == null) return;
            cameraMotion.KnockdownDrop = eyeDrop * FallAmount;
            cameraMotion.KnockdownRoll = groundRoll * rollSign * FallAmount;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Règle de glissade (pure) : en courant, au sol, assez vite, sur une mousse assez épaisse.</summary>
        public static bool ShouldSlip(bool sprinting, bool grounded, float speed, float foam, float minSpeed, float foamThreshold) =>
            sprinting && grounded && speed >= minSpeed && foam >= foamThreshold;

        void CheckSlip()
        {
            if (!slipOnFoam || controller == null || Time.time < slipReadyAt || !controller.IsSprinting || !controller.IsGrounded) return;
            var velocity = controller.Velocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            if (horizontal.magnitude < slipMinSpeed) return;
            var origin = controller.transform.position + Vector3.up * 0.3f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.8f, surfaceMask, QueryTriggerInteraction.Ignore)) return;
            var surface = hit.collider.GetComponentInParent<RugCleaningController>();
            if (surface == null || !surface.TrySampleCell(hit.point, out RugCell cell)) return;
            if (!ShouldSlip(true, true, horizontal.magnitude, cell.Foam, slipMinSpeed, slipFoam)) return;
            slipReadyAt = Time.time + slipCooldown;
            // Réaction exagérée (mission de corrections) : les pieds partent, le corps est projeté ; élan borné par maxImpulse.
            Knockdown(horizontal * 1.4f + Vector3.up * 2f, slipDuration, "Glissade sur la mousse ! (évitez de courir dessus)", KnockCause.Fall);
        }
    }
}
