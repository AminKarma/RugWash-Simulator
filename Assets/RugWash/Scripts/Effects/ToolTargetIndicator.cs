using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>Cercle projeté sur le tapis indiquant la zone réellement traitée par l'outil (rayon exact).</summary>
    [DisallowMultipleComponent]
    public sealed class ToolTargetIndicator : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] MeshRenderer ringRenderer;
        [SerializeField] Color inRangeColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] Color activeColor = new Color(0.55f, 0.9f, 1f, 0.8f);
        [SerializeField] Color outOfRangeColor = new Color(1f, 0.45f, 0.35f, 0.45f);

        MaterialPropertyBlock block;
        Color currentColor;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            Hide();
        }

        public void Show(Vector3 point, Vector3 normal, float radius, bool inRange, bool active)
        {
            if (ringRenderer == null) return;
            transform.SetPositionAndRotation(point + normal * 0.004f, Quaternion.LookRotation(-normal));
            transform.localScale = Vector3.one * (radius * 2f);

            Color color = !inRange ? outOfRangeColor : active ? activeColor : inRangeColor;
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
