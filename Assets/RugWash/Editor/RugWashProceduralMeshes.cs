using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// Générateurs de maillages pour les modèles de l'atelier et des outils (aucun modèle externe) :
    /// boîtes arrondies, révolutions (tours), tubes le long d'un chemin, panneaux de tissu, touffes de poils.
    /// Les UV sont en mètres quand c'est pertinent, pour une échelle de texture homogène.
    /// </summary>
    public static class RugWashProceduralMeshes
    {
        // ------------------------------------------------------------------ Boîte arrondie

        /// <summary>Boîte centrée aux arêtes arrondies (rayon <paramref name="radius"/>), normales lisses sur les arrondis.</summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int bevelSegments = 3)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.98f);
            Vector3 inner = h - Vector3.one * radius;

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();

            void Face(Vector3 normal, Vector3 right, Vector3 up)
            {
                float halfRight = Mathf.Abs(Vector3.Dot(h, right)), halfUp = Mathf.Abs(Vector3.Dot(h, up));
                float innerRight = Mathf.Abs(Vector3.Dot(inner, right)), innerUp = Mathf.Abs(Vector3.Dot(inner, up));
                float halfNormal = Mathf.Abs(Vector3.Dot(h, normal));
                var su = Samples(halfRight, innerRight, radius, bevelSegments);
                var sv = Samples(halfUp, innerUp, radius, bevelSegments);
                int start = v.Count;
                for (int j = 0; j < sv.Count; j++)
                for (int i = 0; i < su.Count; i++)
                {
                    Vector3 p = normal * halfNormal + right * su[i] + up * sv[j];
                    Vector3 core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - core;
                    Vector3 nrm = d.sqrMagnitude > 1e-12f ? d.normalized : normal;
                    Vector3 pos = d.sqrMagnitude > 1e-12f ? core + nrm * radius : p;
                    v.Add(pos);
                    n.Add(nrm);
                    uv.Add(new Vector2(Vector3.Dot(pos, right) + halfRight, Vector3.Dot(pos, up) + halfUp));
                }
                int cols = su.Count;
                for (int j = 0; j < sv.Count - 1; j++)
                for (int i = 0; i < cols - 1; i++)
                {
                    int bl = start + j * cols + i, br = bl + 1, tl = bl + cols, tr = tl + 1;
                    tris.AddRange(new[] { bl, tr, br, bl, tl, tr });
                }
            }

            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.back);
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.right, Vector3.forward, Vector3.up);
            return Finish(v, n, uv, tris, "RoundedBox");
        }

        /// <summary>Coordonnées le long d'un axe : arrondi côté négatif, (zone plate), arrondi côté positif.</summary>
        static List<float> Samples(float half, float inner, float radius, int segments)
        {
            var list = new List<float>();
            if (radius <= 1e-5f || segments < 1)
            {
                list.Add(-half);
                list.Add(half);
                return list;
            }
            for (int i = 0; i <= segments; i++) list.Add(-inner - radius * Mathf.Cos(i / (float)segments * Mathf.PI * 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float value = inner + radius * Mathf.Sin(i / (float)segments * Mathf.PI * 0.5f);
                if (i == 0 && Mathf.Approximately(value, list[list.Count - 1])) continue;
                list.Add(value);
            }
            return list;
        }

        // ------------------------------------------------------------------ Révolution

        /// <summary>
        /// Surface de révolution autour de l'axe Y. <paramref name="profile"/> : (rayon, hauteur) du bas vers le haut ;
        /// un profil qui redescend vers l'intérieur produit une paroi intérieure (seaux, bacs).
        /// </summary>
        public static Mesh Lathe(IList<Vector2> profile, int segments = 24, bool capBottom = true, bool capTop = true)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            int ring = segments + 1;

            float length = 0f;
            for (int k = 0; k < profile.Count; k++)
            {
                if (k > 0) length += Vector2.Distance(profile[k], profile[k - 1]);
                Vector2 prev = profile[Mathf.Max(0, k - 1)], next = profile[Mathf.Min(profile.Count - 1, k + 1)];
                Vector2 tangent = (next - prev).normalized;
                var normal2D = new Vector2(tangent.y, -tangent.x);
                for (int s = 0; s <= segments; s++)
                {
                    float angle = s / (float)segments * Mathf.PI * 2f;
                    float c = Mathf.Cos(angle), si = Mathf.Sin(angle);
                    v.Add(new Vector3(c * profile[k].x, profile[k].y, si * profile[k].x));
                    n.Add(new Vector3(c * normal2D.x, normal2D.y, si * normal2D.x).normalized);
                    uv.Add(new Vector2(s / (float)segments, length));
                }
            }
            for (int k = 0; k < profile.Count - 1; k++)
            for (int s = 0; s < segments; s++)
            {
                int bl = k * ring + s, br = bl + 1, tl = bl + ring, tr = tl + 1;
                tris.AddRange(new[] { bl, tr, br, bl, tl, tr });
            }

            if (capBottom && profile[0].x > 1e-4f) AddCap(v, n, uv, tris, profile[0], segments, top: false);
            if (capTop && profile[profile.Count - 1].x > 1e-4f) AddCap(v, n, uv, tris, profile[profile.Count - 1], segments, top: true);
            return Finish(v, n, uv, tris, "Lathe");
        }

        static void AddCap(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, Vector2 edge, int segments, bool top)
        {
            Vector3 normal = top ? Vector3.up : Vector3.down;
            int center = v.Count;
            v.Add(new Vector3(0f, edge.y, 0f));
            n.Add(normal);
            uv.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float angle = s / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(angle) * edge.x, edge.y, Mathf.Sin(angle) * edge.x));
                n.Add(normal);
                uv.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = center + 1 + s, b = a + 1;
                if (top) tris.AddRange(new[] { center, b, a });
                else tris.AddRange(new[] { center, a, b });
            }
        }

        /// <summary>Cylindre simple (axe Y, centré), via <see cref="Lathe"/>.</summary>
        public static Mesh Cylinder(float radius, float height, int segments = 20) =>
            Lathe(new[] { new Vector2(radius, -height * 0.5f), new Vector2(radius, height * 0.5f) }, segments);

        // ------------------------------------------------------------------ Tube

        /// <summary>Tube de section circulaire le long d'un chemin (tuyaux, flexibles), repère à transport parallèle.</summary>
        public static Mesh Tube(IList<Vector3> path, float radius, int radial = 10, bool caps = true)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            int ring = radial + 1;

            Vector3 tangent = (path[1] - path[0]).normalized;
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float length = 0f;
            var frames = new List<(Vector3 p, Vector3 t, Vector3 n)>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 t = i == 0 ? (path[1] - path[0]).normalized
                    : i == path.Count - 1 ? (path[i] - path[i - 1]).normalized
                    : (path[i + 1] - path[i - 1]).normalized;
                normal = Vector3.ProjectOnPlane(normal, t).normalized;
                frames.Add((path[i], t, normal));
            }

            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0) length += Vector3.Distance(frames[i].p, frames[i - 1].p);
                var (p, t, nrm) = frames[i];
                Vector3 b = Vector3.Cross(t, nrm);
                for (int s = 0; s <= radial; s++)
                {
                    float angle = s / (float)radial * Mathf.PI * 2f;
                    Vector3 dir = nrm * Mathf.Cos(angle) - b * Mathf.Sin(angle);
                    v.Add(p + dir * radius);
                    n.Add(dir);
                    uv.Add(new Vector2(s / (float)radial, length / (radius * Mathf.PI * 2f)));
                }
            }
            for (int i = 0; i < frames.Count - 1; i++)
            for (int s = 0; s < radial; s++)
            {
                int bl = i * ring + s, br = bl + 1, tl = bl + ring, tr = tl + 1;
                tris.AddRange(new[] { bl, tr, br, bl, tl, tr });
            }
            if (caps)
            {
                TubeCap(v, n, uv, tris, frames[0], radius, radial, start: true);
                TubeCap(v, n, uv, tris, frames[frames.Count - 1], radius, radial, start: false);
            }
            return Finish(v, n, uv, tris, "Tube");
        }

        static void TubeCap(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, (Vector3 p, Vector3 t, Vector3 n) frame,
            float radius, int radial, bool start)
        {
            Vector3 normal = start ? -frame.t : frame.t;
            Vector3 b = Vector3.Cross(frame.t, frame.n);
            int center = v.Count;
            v.Add(frame.p);
            n.Add(normal);
            uv.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= radial; s++)
            {
                float angle = s / (float)radial * Mathf.PI * 2f;
                v.Add(frame.p + (frame.n * Mathf.Cos(angle) - b * Mathf.Sin(angle)) * radius);
                n.Add(normal);
                uv.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }
            for (int s = 0; s < radial; s++)
            {
                int a = center + 1 + s, c = a + 1;
                if (start) tris.AddRange(new[] { center, c, a });
                else tris.AddRange(new[] { center, a, c });
            }
        }

        /// <summary>Chemin lissé (Catmull-Rom) passant par des points de contrôle.</summary>
        public static List<Vector3> Smooth(IList<Vector3> controls, int stepsPerSegment = 8)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < controls.Count - 1; i++)
            {
                Vector3 p0 = controls[Mathf.Max(0, i - 1)], p1 = controls[i], p2 = controls[i + 1], p3 = controls[Mathf.Min(controls.Count - 1, i + 2)];
                for (int s = 0; s < stepsPerSegment; s++)
                {
                    float t = s / (float)stepsPerSegment, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(controls[controls.Count - 1]);
            return result;
        }

        /// <summary>Hélice (flexible enroulé) autour de l'axe Y.</summary>
        public static List<Vector3> Helix(float radius, float pitch, float turns, int stepsPerTurn = 28)
        {
            var points = new List<Vector3>();
            int steps = Mathf.CeilToInt(turns * stepsPerTurn);
            for (int i = 0; i <= steps; i++)
            {
                float angle = i / (float)stepsPerTurn * Mathf.PI * 2f;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, pitch * angle / (Mathf.PI * 2f), Mathf.Sin(angle) * radius));
            }
            return points;
        }

        // ------------------------------------------------------------------ Tissu suspendu

        /// <summary>
        /// Panneau de tissu suspendu par son bord supérieur (origine), tombant vers -Y, avec plis et léger affaissement.
        /// Deux faces (recto/verso). UV 0..1 sur toute la surface : le motif d'un tapis s'y applique tel quel.
        /// </summary>
        public static Mesh HangingCloth(float width, float height, float foldDepth, int folds, int columns = 40, int rows = 24, int seed = 3)
        {
            var random = new System.Random(seed);
            float phase = (float)random.NextDouble() * Mathf.PI;
            var front = new List<Vector3>();
            var uvFront = new List<Vector2>();
            for (int r = 0; r <= rows; r++)
            for (int c = 0; c <= columns; c++)
            {
                float u = c / (float)columns, t = r / (float)rows;
                float x = (u - 0.5f) * width;
                float fold = Mathf.Sin(u * folds * Mathf.PI * 2f + phase) * foldDepth * Mathf.SmoothStep(0.15f, 1f, t);
                float sag = Mathf.Sin(u * Mathf.PI) * 0.02f * t;
                front.Add(new Vector3(x, -t * height + sag, fold + t * t * 0.03f));
                uvFront.Add(new Vector2(u, 1f - t));
            }

            var v = new List<Vector3>(front);
            var uv = new List<Vector2>(uvFront);
            var tris = new List<int>();
            int cols = columns + 1;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int tl = r * cols + c, tr = tl + 1, bl = tl + cols, br = bl + 1;
                tris.AddRange(new[] { bl, tr, br, bl, tl, tr });
            }
            var mesh = new Mesh { name = "HangingCloth" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var frontNormals = new List<Vector3>();
            mesh.GetNormals(frontNormals);

            // Verso : mêmes positions, normales opposées, ordre inversé.
            int offset = v.Count;
            var normals = new List<Vector3>(frontNormals);
            for (int i = 0; i < front.Count; i++)
            {
                v.Add(front[i]);
                uv.Add(uvFront[i]);
                normals.Add(-frontNormals[i]);
            }
            int frontTriangles = tris.Count;
            for (int i = 0; i < frontTriangles; i += 3)
                tris.AddRange(new[] { tris[i] + offset, tris[i + 2] + offset, tris[i + 1] + offset });

            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ Poils de brosse

        /// <summary>Bloc de touffes de poils (petits prismes) dirigées vers -Y, réparties sur une grille.</summary>
        public static Mesh BristleBlock(Vector2 area, float length, int columns, int rows, float tuftSize, int seed = 5)
        {
            var random = new System.Random(seed);
            var combine = new List<CombineInstance>();
            var tuft = RoundedBox(new Vector3(tuftSize, length, tuftSize), tuftSize * 0.3f, 1);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                float x = ((c + 0.5f) / columns - 0.5f) * area.x;
                float z = ((r + 0.5f) / rows - 0.5f) * area.y;
                float jitter = ((float)random.NextDouble() - 0.5f) * 0.004f;
                float lengthScale = 0.9f + (float)random.NextDouble() * 0.2f;
                combine.Add(new CombineInstance
                {
                    mesh = tuft,
                    transform = Matrix4x4.TRS(new Vector3(x + jitter, -length * lengthScale * 0.5f, z - jitter),
                        Quaternion.Euler(((float)random.NextDouble() - 0.5f) * 6f, 0f, ((float)random.NextDouble() - 0.5f) * 6f),
                        new Vector3(1f, lengthScale, 1f)),
                });
            }
            var mesh = new Mesh { name = "Bristles" };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            Object.DestroyImmediate(tuft);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ Assemblage

        /// <summary>Fusionne plusieurs maillages positionnés en un seul (un seul appel de rendu par matériau).</summary>
        public static Mesh Combine(string name, params (Mesh mesh, Matrix4x4 transform)[] parts)
        {
            var combine = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++) combine[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].transform };
            var mesh = new Mesh { name = name };
            if (CountVertices(parts) > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(combine, true, true);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static int CountVertices((Mesh mesh, Matrix4x4 transform)[] parts)
        {
            int total = 0;
            foreach (var part in parts) total += part.mesh.vertexCount;
            return total;
        }

        static Mesh Finish(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, string name)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
