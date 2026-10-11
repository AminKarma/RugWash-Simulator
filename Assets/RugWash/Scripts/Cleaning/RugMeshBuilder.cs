using System.Collections.Generic;
using UnityEngine;

namespace RugWash.Cleaning
{
    /// <summary>
    /// Construit le maillage d'un tapis : dessus (UV 0..1 = grille de nettoyage), chants, dessous,
    /// et franges optionnelles (sous-maillage 1). Origine au centre, posé sur y = 0, axe long = X.
    /// </summary>
    public static class RugMeshBuilder
    {
        public static Mesh Build(Vector2 size, float thickness, bool fringe, int seed = 7)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tangents = new List<Vector4>();
            var rugTriangles = new List<int>();
            var fringeTriangles = new List<int>();

            float hx = size.x * 0.5f, hz = size.y * 0.5f;

            // Dessus subdivisé : l'éclairage par sommet et les ombres restent stables.
            const int segX = 24, segZ = 16;
            int start = vertices.Count;
            for (int z = 0; z <= segZ; z++)
            for (int x = 0; x <= segX; x++)
            {
                float u = x / (float)segX, v = z / (float)segZ;
                vertices.Add(new Vector3(Mathf.Lerp(-hx, hx, u), thickness, Mathf.Lerp(-hz, hz, v)));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(u, v));
                tangents.Add(new Vector4(1f, 0f, 0f, -1f));
            }
            for (int z = 0; z < segZ; z++)
            for (int x = 0; x < segX; x++)
            {
                int i = start + z * (segX + 1) + x;
                rugTriangles.AddRange(new[] { i, i + segX + 1, i + 1, i + 1, i + segX + 1, i + segX + 2 });
            }

            // Chants : échantillonnent le bord de la texture.
            AddQuad(vertices, normals, uvs, tangents, rugTriangles,
                new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, thickness, -hz), new Vector3(-hx, thickness, -hz),
                Vector3.back, new Vector2(0, 0), new Vector2(1, 0.004f));
            AddQuad(vertices, normals, uvs, tangents, rugTriangles,
                new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz), new Vector3(-hx, thickness, hz), new Vector3(hx, thickness, hz),
                Vector3.forward, new Vector2(0, 0.996f), new Vector2(1, 1));
            AddQuad(vertices, normals, uvs, tangents, rugTriangles,
                new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), new Vector3(-hx, thickness, -hz), new Vector3(-hx, thickness, hz),
                Vector3.left, new Vector2(0, 0), new Vector2(0.004f, 1));
            AddQuad(vertices, normals, uvs, tangents, rugTriangles,
                new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(hx, thickness, hz), new Vector3(hx, thickness, -hz),
                Vector3.right, new Vector2(0.996f, 0), new Vector2(1, 1));

            if (fringe)
            {
                var random = new System.Random(seed);
                AddFringe(vertices, normals, uvs, tangents, fringeTriangles, -hx, -1f, hz, random);
                AddFringe(vertices, normals, uvs, tangents, fringeTriangles, hx, 1f, hz, random);
            }

            var mesh = new Mesh { name = "RugMesh" };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTangents(tangents);
            mesh.subMeshCount = fringe ? 2 : 1;
            mesh.SetTriangles(rugTriangles, 0);
            if (fringe) mesh.SetTriangles(fringeTriangles, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddFringe(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> t, List<int> tris,
            float edgeX, float direction, float hz, System.Random random)
        {
            const float spacing = 0.012f, strandWidth = 0.005f;
            int count = Mathf.FloorToInt(hz * 2f / spacing);
            for (int i = 0; i < count; i++)
            {
                float z = -hz + spacing * (i + 0.5f);
                float length = 0.06f + (float)random.NextDouble() * 0.025f;
                float drift = ((float)random.NextDouble() - 0.5f) * 0.012f;
                var root = new Vector3(edgeX, 0.002f, z);
                var tip = new Vector3(edgeX + direction * length, 0.0015f, z + drift);
                var side = Vector3.Cross(Vector3.up, (tip - root).normalized) * (strandWidth * 0.5f);
                float shade = 0.85f + (float)random.NextDouble() * 0.15f;
                AddQuad(v, n, uv, t, tris, root - side, tip - side, tip + side, root + side, Vector3.up,
                    new Vector2(shade, 0f), new Vector2(shade, 1f));
            }
        }

        static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> t, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 uvMin, Vector2 uvMax)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++)
            {
                n.Add(normal);
                t.Add(new Vector4(1f, 0f, 0f, -1f));
            }
            uv.Add(new Vector2(uvMin.x, uvMin.y));
            uv.Add(new Vector2(uvMax.x, uvMin.y));
            uv.Add(new Vector2(uvMax.x, uvMax.y));
            uv.Add(new Vector2(uvMin.x, uvMax.y));
            // Ordre horaire vu depuis la normale (convention Unity).
            tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
    }
}
