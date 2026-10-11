using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Mouvement subtil de la caméra : balancier de marche et légère inclinaison en déplacement latéral.
    /// Appliqué à la caméra elle-même : le rayon de visée part toujours du centre de la caméra, donc le réticule,
    /// le point d'impact et la zone nettoyée restent alignés. Intensité réglable (0 = désactivé).
    /// </summary>
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
        public float Roll => roll;

        void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        void LateUpdate()
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
            transform.localPosition = restPosition + bob * (weight * intensity);
            transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, roll * intensity);
        }
    }
}
