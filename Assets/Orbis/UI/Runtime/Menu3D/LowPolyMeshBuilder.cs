using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.UI.Menu3D
{
    /// <summary>
    /// Accumula geometria low-poly (facce piatte) divisa per materiale, poi crea una mesh con una
    /// sotto-mesh per materiale: tutta la città diventa pochi draw call.
    /// </summary>
    public sealed class LowPolyMeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(16384);
        private readonly List<Vector3> _normals = new List<Vector3>(16384);
        private readonly Dictionary<int, List<int>> _triangles = new Dictionary<int, List<int>>();

        public int VertexCount => _vertices.Count;

        /// <summary>Triangolo con normale piatta (vertici in senso orario visti da fuori).</summary>
        public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            if (!_triangles.TryGetValue(material, out var list))
                _triangles[material] = list = new List<int>(4096);
            var i = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            _normals.Add(n); _normals.Add(n); _normals.Add(n);
            list.Add(i); list.Add(i + 1); list.Add(i + 2);
        }

        public void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(material, a, b, c);
            Triangle(material, a, c, d);
        }

        /// <summary>Parallelepipedo appoggiato su <paramref name="baseCenter"/>, ruotato di <paramref name="yaw"/> gradi.</summary>
        public void Box(int material, Vector3 baseCenter, Vector3 size, float yaw, bool bottom = false)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            var hx = size.x * 0.5f;
            var hz = size.z * 0.5f;
            Vector3 P(float x, float y, float z) => baseCenter + q * new Vector3(x, y, z);
            var b0 = P(-hx, 0, -hz); var b1 = P(hx, 0, -hz); var b2 = P(hx, 0, hz); var b3 = P(-hx, 0, hz);
            var t0 = P(-hx, size.y, -hz); var t1 = P(hx, size.y, -hz); var t2 = P(hx, size.y, hz); var t3 = P(-hx, size.y, hz);
            Quad(material, t0, t3, t2, t1);          // sopra
            Quad(material, b0, t0, t1, b1);          // davanti (-z)
            Quad(material, b1, t1, t2, b2);          // destra
            Quad(material, b2, t2, t3, b3);          // dietro
            Quad(material, b3, t3, t0, b0);          // sinistra
            if (bottom) Quad(material, b0, b1, b2, b3);
        }

        /// <summary>Tetto a padiglione (4 falde) sopra un rettangolo, con piccola sporgenza.</summary>
        public void HipRoof(int material, Vector3 baseCenter, Vector3 size, float height, float yaw, float overhang = 0.15f)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            var hx = size.x * 0.5f + overhang;
            var hz = size.z * 0.5f + overhang;
            var ridge = Mathf.Max(0f, hx - hz);
            Vector3 P(float x, float y, float z) => baseCenter + q * new Vector3(x, y, z);
            var b0 = P(-hx, 0, -hz); var b1 = P(hx, 0, -hz); var b2 = P(hx, 0, hz); var b3 = P(-hx, 0, hz);
            var r0 = P(-ridge, height, 0); var r1 = P(ridge, height, 0);
            Quad(material, b0, r0, r1, b1);
            Quad(material, b2, r1, r0, b3);
            Triangle(material, b1, r1, b2);
            Triangle(material, b3, r0, b0);
            Quad(material, b0, b1, b2, b3); // sotto la sporgenza
        }

        /// <summary>Piramide a base quadrata (guglie, campanili).</summary>
        public void Pyramid(int material, Vector3 baseCenter, float width, float height, float yaw)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            var h = width * 0.5f;
            Vector3 P(float x, float y, float z) => baseCenter + q * new Vector3(x, y, z);
            var b0 = P(-h, 0, -h); var b1 = P(h, 0, -h); var b2 = P(h, 0, h); var b3 = P(-h, 0, h);
            var top = P(0, height, 0);
            Triangle(material, b0, top, b1);
            Triangle(material, b1, top, b2);
            Triangle(material, b2, top, b3);
            Triangle(material, b3, top, b0);
        }

        /// <summary>Cilindro o tronco di cono (facce laterali piatte).</summary>
        public void Cylinder(int material, Vector3 baseCenter, float radiusBottom, float radiusTop, float height, int sides, bool cap = true)
        {
            for (var i = 0; i < sides; i++)
            {
                var a0 = i * Mathf.PI * 2f / sides;
                var a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var p0 = baseCenter + d0 * radiusBottom;
                var p1 = baseCenter + d1 * radiusBottom;
                var p2 = baseCenter + d1 * radiusTop + Vector3.up * height;
                var p3 = baseCenter + d0 * radiusTop + Vector3.up * height;
                if (radiusTop > 0.001f) Quad(material, p0, p3, p2, p1);
                else Triangle(material, p0, p3, p1);
                if (cap && radiusTop > 0.001f)
                    Triangle(material, baseCenter + Vector3.up * height, p2, p3);
            }
        }

        /// <summary>Sfera low-poly (icosaedro suddiviso una volta), per chiome e nuvole.</summary>
        public void Blob(int material, Vector3 center, Vector3 radius, int seed)
        {
            var rng = new System.Random(seed);
            var verts = IcoVertices;
            var jitter = new Vector3[verts.Length];
            for (var i = 0; i < verts.Length; i++)
            {
                var j = 0.88f + (float)rng.NextDouble() * 0.24f;
                jitter[i] = center + Vector3.Scale(verts[i] * j, radius);
            }
            for (var i = 0; i < IcoFaces.Length; i += 3)
                Triangle(material, jitter[IcoFaces[i]], jitter[IcoFaces[i + 1]], jitter[IcoFaces[i + 2]]);
        }

        public Mesh Build(string name, out int[] materialOrder)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            var keys = new List<int>(_triangles.Keys);
            keys.Sort();
            mesh.subMeshCount = keys.Count;
            for (var i = 0; i < keys.Count; i++)
                mesh.SetTriangles(_triangles[keys[i]], i, false);
            mesh.RecalculateBounds();
            materialOrder = keys.ToArray();
            return mesh;
        }

        // icosaedro suddiviso una volta: 42 vertici, 80 facce
        private static readonly Vector3[] IcoVertices;
        private static readonly int[] IcoFaces;

        static LowPolyMeshBuilder()
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (var i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out var idx)) return idx;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                cache[key] = v.Count - 1;
                return v.Count - 1;
            }
            var f2 = new List<int>();
            for (var i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                f2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            IcoVertices = v.ToArray();
            // con questo ordine Cross(b - a, c - a) punta verso l'esterno: in Unity è il verso giusto (senso orario)
            IcoFaces = f2.ToArray();
        }
    }
}
