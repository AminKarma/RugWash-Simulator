using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Cercle (ou ellipse pour les outils en éventail) projeté sur la surface visée : la zone réellement traitée.
    /// Couleur : rouge hors de portée, verte si la zone visée est déjà propre (lue dans la grille),
    /// bleue pendant l'utilisation, blanche sinon. Une courte impulsion marque le premier contact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolTargetIndicator : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] MeshRenderer ringRenderer;
        [SerializeField] Color inRangeColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] Color activeColor = new Color(0.55f, 0.9f, 1f, 0.8f);
        [SerializeField] Color outOfRangeColor = new Color(1f, 0.45f, 0.35f, 0.45f);
        [SerializeField] Color cleanColor = new Color(0.5f, 1f, 0.6f, 0.6f);
        [SerializeField] float pulseScale = 0.18f;
        [SerializeField] float pulseDuration = 0.18f;

        MaterialPropertyBlock block;
        Color currentColor;
        float pulse;

        public enum State { OutOfRange, Clean, Active, Idle }

        public State CurrentState { get; private set; }

        /// <summary>Joue une courte impulsion (agrandissement) : premier contact avec le tapis.</summary>
        public void Pulse() => pulse = 1f;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            Hide();
        }

        public void Show(Vector3 point, Vector3 normal, float radius, bool inRange, bool active, bool targetClean = false)
            => Show(point, normal, radius, 1f, Vector3.right, inRange, active, targetClean);

        /// <param name="aspect">Rapport largeur / profondeur (1 = cercle), même surface qu'un disque de rayon <paramref name="radius"/>.</param>
        /// <param name="spreadAxis">Grand axe de l'ellipse, dans le plan de la surface.</param>
        public void Show(Vector3 point, Vector3 normal, float radius, float aspect, Vector3 spreadAxis, bool inRange, bool active, bool targetClean)
        {
            if (ringRenderer == null) return;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime / Mathf.Max(0.01f, pulseDuration));
            // Axe X local du quad = grand axe : LookRotation(-n, axe × n) donne X = axe.
            Vector3 up = Vector3.Cross(spreadAxis, normal);
            var rotation = up.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-normal, up) : Quaternion.LookRotation(-normal);
            transform.SetPositionAndRotation(point + normal * 0.004f, rotation);
            float stretch = Mathf.Sqrt(Mathf.Max(1f, aspect));
            float size = radius * 2f * (1f + pulseScale * Mathf.Sin(pulse * Mathf.PI));
            transform.localScale = new Vector3(size * stretch, size / stretch, 1f);

            CurrentState = !inRange ? State.OutOfRange : targetClean ? State.Clean : active ? State.Active : State.Idle;
            Color color = CurrentState == State.OutOfRange ? outOfRangeColor : CurrentState == State.Clean ? cleanColor
                : CurrentState == State.Active ? activeColor : inRangeColor;
            if (color != currentColor)
            {
                currentColor = color;
                block.SetColor(ColorId, color);
                ringRenderer.SetPropertyBlock(block);
            }
            if (!ringRenderer.enabled) ringRenderer.enabled = true;
        }

        public void Hide()
        {
            if (ringRenderer != null && ringRenderer.enabled) ringRenderer.enabled = false;
        }
    }
}
