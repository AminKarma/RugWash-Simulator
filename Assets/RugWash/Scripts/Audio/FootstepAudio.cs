using RugWash.Player;
using UnityEngine;

namespace RugWash.Audio
{
    /// <summary>Cadence des pas (règle pure, testée) : un pas par distance parcourue au sol, aucun à l'arrêt ou en l'air.</summary>
    public sealed class FootstepCadence
    {
        readonly float stride;
        float travelled;

        public FootstepCadence(float strideMeters) => stride = Mathf.Max(0.1f, strideMeters);

        /// <summary>Retourne vrai quand un pas doit être joué.</summary>
        public bool Advance(float horizontalSpeed, bool grounded, float deltaTime)
        {
            if (!grounded || horizontalSpeed < 0.3f)
            {
                travelled = Mathf.Min(travelled, stride * 0.5f);
                return false;
            }
            travelled += horizontalSpeed * deltaTime;
            if (travelled < stride) return false;
            travelled -= stride;
            return true;
        }
    }

    /// <summary>Bruits de pas du joueur (sons temporaires synthétisés, variantes aléatoires).</summary>
    [DisallowMultipleComponent]
    public sealed class FootstepAudio : MonoBehaviour
    {
        [SerializeField] FirstPersonController controller;
        [SerializeField] AudioManager audioManager;
        [SerializeField] AudioClip[] steps;
        [SerializeField] float strideMeters = 0.7f;
        [SerializeField, Range(0f, 1f)] float volume = 0.35f;

        FootstepCadence cadence;
        int lastIndex = -1;

        public int StepsPlayed { get; private set; }

        void Awake() => cadence = new FootstepCadence(strideMeters);

        void Update()
        {
            if (controller == null || audioManager == null || steps == null || steps.Length == 0) return;
            Vector3 velocity = controller.Velocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (!cadence.Advance(speed, controller.IsGrounded, Time.deltaTime)) return;

            int index = Random.Range(0, steps.Length);
            if (steps.Length > 1 && index == lastIndex) index = (index + 1) % steps.Length;
            lastIndex = index;
            audioManager.PlayAt(steps[index], transform.position, volume);
            StepsPlayed++;
        }
    }
}
