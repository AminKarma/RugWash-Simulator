using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Workshop
{
    /// <summary>
    /// Recherche de chemin A* sur une <see cref="OccupancyGrid"/> (8 directions, sans couper les coins d'obstacle),
    /// puis simplification du chemin par lignes de vue. Ne traverse jamais une case bloquée.
    /// </summary>
    public static class GridPathfinder
    {
        static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>Chemin de <paramref name="start"/> à <paramref name="goal"/> inclus, ou null s'il n'existe pas.</summary>
        public static List<Vector2Int> FindPath(OccupancyGrid grid, Vector2Int start, Vector2Int goal, int maxExpanded = 20000)
        {
            if (grid.IsBlocked(start) || grid.IsBlocked(goal)) return null;
            if (start == goal) return new List<Vector2Int> { start };

            int count = grid.Width * grid.Height;
            var gScore = new float[count];
            var cameFrom = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                gScore[i] = float.MaxValue;
                cameFrom[i] = -1;
            }

            var open = new MinHeap(256);
            int startIndex = Index(grid, start), goalIndex = Index(grid, goal);
            gScore[startIndex] = 0f;
            open.Push(startIndex, Heuristic(start, goal));
            int expanded = 0;

            while (open.Count > 0 && expanded < maxExpanded)
            {
                int current = open.Pop();
                if (closed[current]) continue;
                if (current == goalIndex) return Rebuild(grid, cameFrom, goalIndex);
                closed[current] = true;
                expanded++;
                var cell = new Vector2Int(current % grid.Width, current / grid.Width);

                foreach (var direction in Directions)
                {
                    var next = cell + direction;
                    if (grid.IsBlocked(next)) continue;
                    bool diagonal = direction.x != 0 && direction.y != 0;
                    // Pas de coin coupé : les deux cases orthogonales doivent être libres.
                    if (diagonal && (grid.IsBlocked(new Vector2Int(cell.x + direction.x, cell.y)) || grid.IsBlocked(new Vector2Int(cell.x, cell.y + direction.y))))
                        continue;
                    int nextIndex = Index(grid, next);
                    if (closed[nextIndex]) continue;
                    float tentative = gScore[current] + (diagonal ? 1.41421356f : 1f);
                    if (tentative >= gScore[nextIndex]) continue;
                    gScore[nextIndex] = tentative;
                    cameFrom[nextIndex] = current;
                    open.Push(nextIndex, tentative + Heuristic(next, goal));
                }
            }
            return null;
        }

        /// <summary>Supprime les points intermédiaires inutiles (ligne de vue libre entre les points conservés).</summary>
        public static List<Vector2Int> Simplify(OccupancyGrid grid, List<Vector2Int> path)
        {
            if (path == null || path.Count <= 2) return path;
            var result = new List<Vector2Int> { path[0] };
            int anchor = 0;
            for (int i = 2; i < path.Count; i++)
            {
                if (grid.HasLineOfSight(path[anchor], path[i])) continue;
                result.Add(path[i - 1]);
                anchor = i - 1;
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        static int Index(OccupancyGrid grid, Vector2Int cell) => cell.y * grid.Width + cell.x;

        static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            return (dx + dy) + (1.41421356f - 2f) * Mathf.Min(dx, dy);
        }

        static List<Vector2Int> Rebuild(OccupancyGrid grid, int[] cameFrom, int goalIndex)
        {
            var path = new List<Vector2Int>();
            for (int i = goalIndex; i >= 0; i = cameFrom[i])
                path.Add(new Vector2Int(i % grid.Width, i / grid.Width));
            path.Reverse();
            return path;
        }

        /// <summary>Tas binaire minimal (indice de case, priorité).</summary>
        sealed class MinHeap
        {
            int[] items;
            float[] priorities;

            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                items = new int[capacity];
                priorities = new float[capacity];
            }

            public void Push(int item, float priority)
            {
                if (Count == items.Length)
                {
                    System.Array.Resize(ref items, Count * 2);
                    System.Array.Resize(ref priorities, Count * 2);
                }
                int i = Count++;
                items[i] = item;
                priorities[i] = priority;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (priorities[parent] <= priorities[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = items[0];
                Count--;
                items[0] = items[Count];
                priorities[0] = priorities[Count];
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < Count && priorities[left] < priorities[smallest]) smallest = left;
                    if (right < Count && priorities[right] < priorities[smallest]) smallest = right;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (items[a], items[b]) = (items[b], items[a]);
                (priorities[a], priorities[b]) = (priorities[b], priorities[a]);
            }
        }
    }
}
