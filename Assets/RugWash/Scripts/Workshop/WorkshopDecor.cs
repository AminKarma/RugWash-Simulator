using System;
using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>
    /// Décor de l'atelier par paliers (v0.1.2), purement visuel : du cagibi encombré et mal éclairé de départ au penthouse
    /// avec vue sur Los Angeles. Chaque palier active ou retire de vrais éléments (rangement, mobilier, revêtements,
    /// éclairage, baies vitrées) ; les machines et le gameplay n'en dépendent pas. En ligne, l'atelier affiché est celui
    /// de l'hôte (palier reçu) ; le palier de chaque joueur reste dans SA carrière.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopDecor : MonoBehaviour
    {
        public const int MaxTier = 4;

        [Serializable]
        public struct TierGroup
        {
            public GameObject root;
            [Range(0, MaxTier)] public int minTier;
            [Range(0, MaxTier)] public int maxTier;
        }

        static readonly string[] Names =
        {
            "Cagibi", "Atelier rangé", "Atelier professionnel", "Loft industriel", "Penthouse — vue sur Los Angeles",
        };

        [SerializeField] List<TierGroup> groups = new List<TierGroup>();
        [Tooltip("Lumières de la salle dont l'intensité suit le palier (cagibi sombre, penthouse lumineux).")]
        [SerializeField] List<Light> roomLights = new List<Light>();
        [SerializeField] float[] lightScaleByTier = { 0.6f, 1f, 1.05f, 1.1f, 1.15f };

        float[] baseIntensity;

        public int Tier { get; private set; } = -1;
        public IReadOnlyList<TierGroup> Groups => groups;
        public static string NameOf(int tier) => Names[Mathf.Clamp(tier, 0, MaxTier)];
        public event Action<int> TierChanged;

        public void Apply(int tier)
        {
            tier = Mathf.Clamp(tier, 0, MaxTier);
            if (tier == Tier) return;
            if (baseIntensity == null)
            {
                baseIntensity = new float[roomLights.Count];
                for (int i = 0; i < roomLights.Count; i++) baseIntensity[i] = roomLights[i] != null ? roomLights[i].intensity : 0f;
            }
            Tier = tier;
            foreach (var group in groups)
                if (group.root != null) group.root.SetActive(group.minTier <= tier && tier <= group.maxTier);
            float scale = lightScaleByTier != null && tier < lightScaleByTier.Length ? lightScaleByTier[tier] : 1f;
            for (int i = 0; i < roomLights.Count; i++)
                if (roomLights[i] != null) roomLights[i].intensity = baseIntensity[i] * scale;
            TierChanged?.Invoke(tier);
        }

        /// <summary>Éléments actifs à ce palier (diagnostic, tests).</summary>
        public int ActiveGroupCount()
        {
            int count = 0;
            foreach (var group in groups)
                if (group.root != null && group.root.activeSelf) count++;
            return count;
        }
    }
}
