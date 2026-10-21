using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Mouvement subtil de la caméra : balancier de marche et légère inclinaison en déplacement latéral.
    /// Appliqué à la caméra elle-même : le rayon de visée part toujours du centre de la caméra, donc le réticule,
    /// le point d'impact et la zone nettoyée restent alignés. Intensité réglable (0 = désactivé).
    /// </summary>
    // Après le déplacement du joueur (0), avant la visée des outils (50) : le rayon, la pose de l'outil et l'origine de ses
    // effets partent de la caméra telle qu'elle est affichée (en LateUpdate, les particules étaient déjà émises).
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class CameraMotion : MonoBehaviour
    {
        [SerializeField] FirstPersonController controller;
        [SerializeField] float bobFrequency = 1.8f;
        [SerializeField] float bobVertical = 0.012f;
        [SerializeField] float bobHorizontal = 0.006f;
        [SerializeField] float strafeRoll = 0.9f;
        [SerializeField] float smoothing = 8f;

        float intensity = 1f;
        float phase;
        float weight;
        float roll;
        Vector3 restPosition;
        Quaternion restRotation;

        /// <summary>0 = aucun mouvement, 1 = mouvement nominal.</summary>
        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Clamp01(value);
        }

        public Vector3 Offset => transform.localPosition - restPosition;
        /// <summary>Chute (ragdoll du joueur local) : descente verticale de la caméra (m) et inclinaison latérale (°).</summary>
        public float KnockdownDrop { get; set; }
        public float KnockdownRoll { get; set; }
        public float Roll => roll;

        float shakeAmplitude, shakeUntil, shakeSeconds;
        /// <summary>V0.4.2 : secousse en cours (tests).</summary>
        public bool IsShaking => Time.unscaledTime < shakeUntil;

        /// <summary>
        /// V0.4.2 : secousse brève (explosion) : décalage aléatoire décroissant, en temps réel (le ralenti ne la prolonge pas),
        /// dosée par le réglage « Balancement » ; une nouvelle secousse ne fait que remplacer une plus faible.
        /// </summary>
        public void Shake(float amplitude, float seconds)
        {
            float remaining = IsShaking ? shakeAmplitude * (shakeUntil - Time.unscaledTime) / Mathf.Max(0.01f, shakeSeconds) : 0f;
            if (amplitude < remaining) return;
            shakeAmplitude = Mathf.Clamp(amplitude, 0f, 0.12f);
            shakeSeconds = Mathf.Clamp(seconds, 0.05f, 1.5f);
            shakeUntil = Time.unscaledTime + shakeSeconds;
        }

        void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || controller == null) return;

            Vector3 velocity = controller.Velocity;
            Vector3 local = controller.transform.InverseTransformDirection(new Vector3(velocity.x, 0f, velocity.z));
            float speed = local.magnitude;
            bool grounded = controller.IsGrounded;

            weight = Mathf.MoveTowards(weight, grounded ? Mathf.Clamp01(speed / 3.2f) : 0f, dt * 4f);
            phase += speed * bobFrequency * dt;
            float targetRoll = -Mathf.Clamp(local.x / 3.2f, -1f, 1f) * strafeRoll;
            roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-smoothing * dt));

            var bob = new Vector3(Mathf.Sin(phase * Mathf.PI) * bobHorizontal, Mathf.Abs(Mathf.Cos(phase * Mathf.PI)) * bobVertical - bobVertical * 0.5f, 0f);
            // La chute s'ajoute verticalement dans le monde, quel que soit le tangage de la tête.
            Vector3 drop = transform.parent != null ? transform.parent.InverseTransformVector(Vector3.down * KnockdownDrop) : Vector3.down * KnockdownDrop;
            Vector3 shake = Vector3.zero;
            if (IsShaking)
            {
                float k = (shakeUntil - Time.unscaledTime) / shakeSeconds;
                float t = Time.unscaledTime * 38f;
                shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * shakeAmplitude * k * k * intensity);
            }
            transform.localPosition = restPosition + bob * (weight * intensity) + drop + shake;
            transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, roll * intensity + KnockdownRoll);
        }
    }
}
