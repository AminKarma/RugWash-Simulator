using System;
using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    public enum DirtPattern
    {
        /// <summary>Voile régulier légèrement bruité (poussière).</summary>
        Uniform,
        /// <summary>Grandes plaques organiques (boue, terre).</summary>
        Blotches,
        /// <summary>Taches rondes irrégulières (boissons, graisse).</summary>
        Spots,
        /// <summary>Bande de passage dans l'axe long du tapis.</summary>
        TrafficLane,
        /// <summary>Accumulation sur les bords.</summary>
        Edges,
    }

    /// <summary>Couche de saleté procédurale, configurable dans l'éditeur.</summary>
    [Serializable]
    public struct DirtLayer
    {
        public DirtType type;
        public DirtPattern pattern;
        [Tooltip("Proportion de surface couverte (ou nombre relatif de taches).")]
        [Range(0f, 1f)] public float coverage;
        [Tooltip("Quantité de saleté au cœur des zones couvertes.")]
        [Range(0f, 1f)] public float intensity;
        [Tooltip("Taille caractéristique des motifs, en mètres.")]
        [Min(0.02f)] public float scale;
        [Tooltip("Adhérence ajoutée à celle du type (-1 à 1) : taches plus ou moins incrustées. 0 = valeur du type.")]
        [Range(-1f, 1f)] public float adhesionOffset;

        public DirtLayer(DirtType type, DirtPattern pattern, float coverage, float intensity, float scale, float adhesionOffset = 0f)
        {
            this.type = type;
            this.pattern = pattern;
            this.coverage = coverage;
            this.intensity = intensity;
            this.scale = scale;
            this.adhesionOffset = adhesionOffset;
        }
    }

    /// <summary>Génère un état sale initial déterministe (même graine = même tapis).</summary>
    public static class DirtGenerator
    {
        public static void Generate(RugCleaningGrid grid, IReadOnlyList<DirtLayer> layers, int seed, DirtTypeTable table = null)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            grid.Clear();
            if (layers != null)
            {
                var random = new System.Random(seed);
                for (int i = 0; i < layers.Count; i++)
                    ApplyLayer(grid, layers[i], random, AdhesionOf(layers[i], table));
            }
            grid.CaptureInitialState();
            grid.MarkAllDirty();
        }

        /// <summary>Adhérence effective d'une couche : celle du type, modulée par la couche (bornée à [0, 1]).</summary>
        public static float AdhesionOf(in DirtLayer layer, DirtTypeTable table)
        {
            float typeDefault = table != null ? table.GetAdhesion(layer.type) : DirtTypeTable.DefaultAdhesion(layer.type);
            return Mathf.Clamp01(typeDefault + layer.adhesionOffset);
        }

        static void ApplyLayer(RugCleaningGrid grid, DirtLayer layer, System.Random random, float adhesion)
        {
            if (layer.type == DirtType.None || layer.intensity <= 0f || layer.coverage <= 0f) return;

            float ox = (float)random.NextDouble() * 1000f, oy = (float)random.NextDouble() * 1000f;
            Vector2 size = grid.SizeMeters;
            float scale = Mathf.Max(0.02f, layer.scale);

            if (layer.pattern == DirtPattern.Spots)
            {
                ApplySpots(grid, layer, random, ox, oy, adhesion);
                return;
            }

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                float px = (x + 0.5f) / grid.Width * size.x;
                float py = (y + 0.5f) / grid.Height * size.y;
                float noise = Fbm(px / scale + ox, py / scale + oy);
                float amount;

                switch (layer.pattern)
                {
                    case DirtPattern.Uniform:
                        amount = layer.intensity * Mathf.Lerp(0.55f, 1f, noise) * layer.coverage;
                        break;
                    case DirtPattern.Blotches:
                    {
                        float threshold = 1f - layer.coverage;
                        amount = layer.intensity * Smooth(threshold - 0.08f, threshold + 0.12f, noise);
                        break;
                    }
                    case DirtPattern.TrafficLane:
                    {
                        float across = Mathf.Abs(py / size.y - 0.5f) * 2f;
                        float lane = 1f - Smooth(layer.coverage * 0.6f, layer.coverage * 0.6f + 0.35f, across + (noise - 0.5f) * 0.3f);
                        amount = layer.intensity * lane * Mathf.Lerp(0.6f, 1f, noise);
                        break;
                    }
                    case DirtPattern.Edges:
                    {
                        float edge = Mathf.Min(Mathf.Min(px, size.x - px), Mathf.Min(py, size.y - py));
                        float band = layer.coverage * 0.5f * Mathf.Min(size.x, size.y);
                        amount = layer.intensity * (1f - Smooth(band * 0.3f, band, edge + (noise - 0.5f) * band * 0.6f));
                        break;
                    }
                    default:
                        amount = 0f;
                        break;
                }

                if (amount > 0.002f) AddToCell(grid, x, y, layer.type, amount, adhesion);
            }
        }

        static void ApplySpots(RugCleaningGrid grid, DirtLayer layer, System.Random random, float ox, float oy, float adhesion)
        {
            Vector2 size = grid.SizeMeters;
            int count = Mathf.Max(1, Mathf.RoundToInt(layer.coverage * 24f));
            for (int s = 0; s < count; s++)
            {
                var center = new Vector2((float)random.NextDouble() * size.x, (float)random.NextDouble() * size.y);
                float radius = layer.scale * Mathf.Lerp(0.5f, 1.4f, (float)random.NextDouble());
                float strength = layer.intensity * Mathf.Lerp(0.7f, 1f, (float)random.NextDouble());

                int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - radius * 1.5f) / size.x * grid.Width));
                int maxX = Mathf.Min(grid.Width - 1, Mathf.CeilToInt((center.x + radius * 1.5f) / size.x * grid.Width));
                int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - radius * 1.5f) / size.y * grid.Height));
                int maxY = Mathf.Min(grid.Height - 1, Mathf.CeilToInt((center.y + radius * 1.5f) / size.y * grid.Height));

                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float px = (x + 0.5f) / grid.Width * size.x, py = (y + 0.5f) / grid.Height * size.y;
                    float d = Vector2.Distance(new Vector2(px, py), center);
                    float wobble = (Fbm(px / (radius * 0.6f) + ox + s * 13.1f, py / (radius * 0.6f) + oy) - 0.5f) * radius * 0.8f;
                    float amount = strength * (1f - Smooth(radius * 0.55f, radius, d + wobble));
                    if (amount > 0.002f) AddToCell(grid, x, y, layer.type, amount, adhesion);
                }
            }
        }

        static void AddToCell(RugCleaningGrid grid, int x, int y, DirtType type, float amount, float adhesion)
        {
            var cell = grid.GetCell(x, y);
            float stainBefore = cell.Stain;
            var op = new AddDirtOperation { Type = type, Amount = amount };
            op.Apply(ref cell, 1f);
            // L'adhérence suit la saleté fixée : moyenne pondérée des couches qui se superposent.
            if (!DirtTypes.IsLooseDry(type) && cell.Stain > 0f)
                cell.Adhesion = Mathf.Lerp(cell.Adhesion, adhesion, Mathf.Clamp01((cell.Stain - stainBefore) / cell.Stain));
            cell.Dust = Mathf.Clamp01(cell.Dust);
            cell.Stain = Mathf.Clamp01(cell.Stain);
            grid.SetCell(x, y, cell);
        }

        static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * Mathf.PerlinNoise(x, y);
                norm += amp;
                x *= 2.03f; y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
