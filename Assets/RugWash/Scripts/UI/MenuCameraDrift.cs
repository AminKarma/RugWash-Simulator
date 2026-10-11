using UnityEngine;

namespace RugWash.UI
{
    /// <summary>Lent travelling de la caméra du menu principal autour d'un point de l'atelier.</summary>
    public sealed class MenuCameraDrift : MonoBehaviour
    {
        [SerializeField] Vector3 focus = new Vector3(0f, 0.3f, 0.8f);
        [SerializeField] float radius = 4.2f;
        [SerializeField] float height = 2.1f;
        [SerializeField] float degreesPerSecond = 3f;
        [SerializeField] float startAngle = 200f;
        [SerializeField] float swing = 35f;

        float time;

        void LateUpdate()
        {
            time += Time.unscaledDeltaTime;
            float angle = (startAngle + Mathf.Sin(time * degreesPerSecond * Mathf.Deg2Rad) * swing) * Mathf.Deg2Rad;
            var position = focus + new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
        }
    }
}
