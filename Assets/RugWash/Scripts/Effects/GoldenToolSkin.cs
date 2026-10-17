using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Effects
{
    /// <summary>
    /// Finition dorée d'un outil arrivé au niveau maximal (grande mise à jour) : les pièces de l'outil (plastiques, métaux,
    /// bois) passent à l'or ; gants, poils, tuyaux, vitres et liquides gardent leur matériau. Utilisé par le modèle à la
    /// première personne et par celui de la silhouette vue par les autres joueurs (état diffusé par l'hôte).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GoldenToolSkin : MonoBehaviour
    {
        [SerializeField] List<Renderer> renderers = new List<Renderer>();
        [SerializeField] Material gold;
        [Tooltip("Matériaux remplacés par l'or ; les autres restent tels quels.")]
        [SerializeField] List<Material> gilded = new List<Material>();

        Material[][] original, golden;
        bool isGolden;

        public bool IsGolden => isGolden;
        public int RendererCount => renderers.Count;

        public bool IsConfigured(out string missing)
        {
            missing = gold == null ? nameof(gold) : renderers.Count == 0 || renderers.Contains(null) ? nameof(renderers) : null;
            return missing == null;
        }

        void Capture()
        {
            if (original != null) return;
            original = new Material[renderers.Count][];
            golden = new Material[renderers.Count][];
            for (int i = 0; i < renderers.Count; i++)
            {
                var materials = renderers[i] != null ? renderers[i].sharedMaterials : new Material[0];
                original[i] = materials;
                golden[i] = new Material[materials.Length];
                for (int m = 0; m < materials.Length; m++)
                    golden[i][m] = materials[m] != null && gilded.Contains(materials[m]) ? gold : materials[m];
            }
        }

        /// <summary>Or (niveau maximal) ou matériaux d'origine.</summary>
        public void SetGolden(bool value)
        {
            Capture();
            if (value == isGolden) return;
            isGolden = value;
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].sharedMaterials = value ? golden[i] : original[i];
        }
    }
}
