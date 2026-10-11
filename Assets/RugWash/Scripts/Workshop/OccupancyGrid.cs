using System;
using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>
    /// Grille d'occupation du sol (plan XZ) : chaque case est libre ou bloquée pour le robot (obstacles gonflés de son
    /// rayon). Construite une fois au démarrage à partir de la scène ; sert à la recherche de chemin.
    /// </summary>
    public sealed class OccupancyGrid
    {
        readonly bool[] blocked;

        public Vector2 Min { get; }
        public float CellSize { get; }
        public int Width { get; }
        public int Height { get; }
        public int FreeCount { get; private set; }

        public OccupancyGrid(Vector2 min, Vector2 max, float cellSize)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            Min = min;
            CellSize = cellSize;
            Width = Mathf.Max(1, Mathf.FloorToInt((max.x - min.x) / cellSize));
            Height = Mathf.Max(1, Mathf.FloorToInt((max.y - min.y) / cellSize));
            blocked = new bool[Width * Height];
            FreeCount = blocked.Length;
        }

        /// <summary>Détermine chaque case avec <paramref name="isBlocked"/> (centre de la case en XZ monde).</summary>
        public void Build(Func<Vector2, bool> isBlocked)
        {
            FreeCount = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool value = isBlocked(CellCenter(new Vector2Int(x, y)));
                blocked[y * Width + x] = value;
                if (!value) FreeCount++;
            }
        }

        public void SetBlocked(Vector2Int cell, bool value)
        {
            if (!InBounds(cell)) return;
            int i = cell.y * Width + cell.x;
            if (blocked[i] == value) return;
            blocked[i] = value;
            FreeCount += value ? -1 : 1;
        }

        public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        /// <summary>Hors de la grille = bloqué : le robot ne sort jamais de la zone autorisée.</summary>
        public bool IsBlocked(Vector2Int cell) => !InBounds(cell) || blocked[cell.y * Width + cell.x];

        public Vector2Int WorldToCell(Vector2 xz) => new Vector2Int(
            Mathf.FloorToInt((xz.x - Min.x) / CellSize), Mathf.FloorToInt((xz.y - Min.y) / CellSize));

        public Vector2 CellCenter(Vector2Int cell) => new Vector2(Min.x + (cell.x + 0.5f) * CellSize, Min.y + (cell.y + 0.5f) * CellSize);

        /// <summary>Case libre la plus proche (recherche en anneaux), dans un rayon donné en cases.</summary>
        public bool TryNearestFree(Vector2Int origin, int maxRadius, out Vector2Int free)
        {
            for (int r = 0; r <= maxRadius; r++)
            {
                float best = float.MaxValue;
                free = default;
                bool found = false;
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var cell = new Vector2Int(origin.x + dx, origin.y + dy);
                    if (IsBlocked(cell)) continue;
                    float d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d;
                        free = cell;
                        found = true;
                    }
                }
                if (found) return true;
            }
            free = default;
            return false;
        }

        /// <summary>Vrai si toutes les cases traversées par le segment sont libres (parcours fin, pas de coin coupé).</summary>
        public bool HasLineOfSight(Vector2Int a, Vector2Int b)
        {
            Vector2 from = new Vector2(a.x + 0.5f, a.y + 0.5f), to = new Vector2(b.x + 0.5f, b.y + 0.5f);
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 3f);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, steps == 0 ? 0f : i / (float)steps);
                if (IsBlocked(new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)))) return false;
            }
            return true;
        }

        public IEnumerable<Vector2Int> FreeCells()
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (!blocked[y * Width + x]) yield return new Vector2Int(x, y);
        }
    }
}
