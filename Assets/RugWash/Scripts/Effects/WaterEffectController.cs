using RugWash.Cleaning;
using RugWash.Core;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Eau au sol : l'aire de lavage se mouille (plus sombre, plus brillante) quand le joueur
    /// pulvérise réellement de l'eau ou du produit, puis sèche lentement. Les matériaux sont
    /// instanciés une fois et mis à jour seulement quand l'humidité change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterEffectController : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        [SerializeField] Renderer[] wetSurfaces;
        [SerializeField] CleaningToolBase[] wettingTools;
        [SerializeField] float wettingPerSecond = 0.2f;
        [SerializeField] float dryingPerSecond = 0.012f;
        [SerializeField, Range(0f, 1f)] float wetDarkening = 0.22f;
        [SerializeField, Range(0f, 1f)] float wetSmoothness = 0.62f;

        Material[] instances;
        Color[] baseColors;
        float[] baseSmoothness;
        float appliedWetness = -1f;

        public float Wetness { get; private set; }

        void Awake()
        {
            instances = new Material[wetSurfaces.Length];
            baseColors = new Color[wetSurfaces.Length];
            baseSmoothness = new float[wetSurfaces.Length];
            for (int i = 0; i < wetSurfaces.Length; i++)
            {
                var source = wetSurfaces[i].sharedMaterial;
                instances[i] = new Material(source) { name = source.name + " (mouillé)", hideFlags = HideFlags.DontSave };
                wetSurfaces[i].sharedMaterial = instances[i];
                baseColors[i] = source.GetColor(BaseColorId);
                baseSmoothness[i] = source.GetFloat(SmoothnessId);
            }
        }

        void Update()
        {
            bool spraying = false;
            foreach (var tool in wettingTools)
                if (tool != null && tool.IsUsing) spraying = true;

            Wetness = Mathf.Clamp01(Wetness + (spraying ? wettingPerSecond : -dryingPerSecond) * Time.deltaTime);
            if (Mathf.Abs(Wetness - appliedWetness) < 0.005f) return;
            appliedWetness = Wetness;
            for (int i = 0; i < instances.Length; i++)
            {
                Color wet = baseColors[i] * Mathf.Lerp(1f, 1f - wetDarkening, Wetness);
                wet.a = baseColors[i].a;
                instances[i].SetColor(BaseColorId, wet);
                instances[i].SetFloat(SmoothnessId, Mathf.Lerp(baseSmoothness[i], wetSmoothness, Wetness));
            }
        }

        void OnDestroy()
        {
            if (instances == null) return;
            foreach (var instance in instances) UnityObjectUtility.SafeDestroy(instance);
        }
    }
}
