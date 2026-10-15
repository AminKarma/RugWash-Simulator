using RugWash.Pranks;
using UnityEngine;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Gouttes sur l'écran du joueur local (farce « pipi » d'un coéquipier, v0.1.2) : grosses gouttes jaunes surtout vers
    /// les bords et léger voile, qui brouillent partiellement la vue sans la masquer (opacité plafonnée). Durée limitée,
    /// fondu de sortie ; effacées à la fin de la partie et quand le joueur est replacé. Une nouvelle salve ne s'empile pas :
    /// elle repart de l'intensité la plus forte.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenSplash : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [Tooltip("Gouttes (texture générée, transparente au centre).")]
        [SerializeField] RawImage droplets;
        [Tooltip("Voile léger plein écran.")]
        [SerializeField] Image haze;
        [Tooltip("Opacité maximale du voile, relative aux gouttes.")]
        [SerializeField, Range(0f, 0.6f)] float hazeShare = 0.16f;
        [Tooltip("Opacité maximale absolue : la vue n'est jamais masquée.")]
        [SerializeField, Range(0.2f, 0.9f)] float maxAlpha = 0.85f;

        float start = -1f, duration, fade, intensity;

        public bool IsActive => start >= 0f;
        /// <summary>Opacité actuelle des gouttes [0..1].</summary>
        public float Alpha { get; private set; }
        public float Remaining => IsActive ? Mathf.Max(0f, start + duration - Time.unscaledTime) : 0f;

        public bool IsConfigured(out string missing)
        {
            missing = group == null ? nameof(group) : droplets == null ? nameof(droplets) : haze == null ? nameof(haze) : null;
            return missing == null;
        }

        void Awake() => Apply(0f);

        public void Splash(float strength, float seconds, float fadeSeconds)
        {
            float current = IsActive ? Alpha : 0f;
            intensity = Mathf.Min(maxAlpha, Mathf.Max(Mathf.Clamp01(strength), current));
            duration = Mathf.Max(0.5f, seconds);
            fade = Mathf.Clamp(fadeSeconds, 0.1f, duration);
            start = Time.unscaledTime;
            // Miroir horizontal au hasard : deux salves ne se ressemblent pas (les coulures restent vers le bas).
            bool mirror = Random.value < 0.5f;
            if (droplets != null) droplets.uvRect = new Rect(mirror ? 1f : 0f, 0f, mirror ? -1f : 1f, 1f);
            enabled = true;
            Update();
        }

        public void Clear()
        {
            start = -1f;
            Apply(0f);
        }

        void OnDisable() => Clear();

        void Update()
        {
            if (!IsActive)
            {
                if (Alpha > 0f) Apply(0f);
                return;
            }
            float alpha = PrankRules.SplashAlpha(Time.unscaledTime - start, duration, fade, intensity);
            if (alpha <= 0f && Time.unscaledTime - start > 0.2f)
            {
                start = -1f;
                Apply(0f);
                return;
            }
            Apply(alpha);
        }

        void Apply(float alpha)
        {
            Alpha = alpha;
            if (group != null)
            {
                group.alpha = alpha > 0f ? 1f : 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            if (droplets != null) droplets.color = new Color(1f, 1f, 1f, alpha);
            if (haze != null)
            {
                var color = haze.color;
                color.a = alpha * hazeShare;
                haze.color = color;
            }
        }
    }
}
