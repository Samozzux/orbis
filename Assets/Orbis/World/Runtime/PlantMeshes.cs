using System;
using Orbis.Core.World.Meshing;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Modelli low-poly delle piante, generati da codice (niente asset esterni): latifoglia, conifera, palma,
    /// olivo, cipresso, pino domestico, albero da frutto, cespuglio, filare di vite (lungo 1 m sull'asse x).
    /// Altezza a scala 1 = VegetationScatter.BaseHeight. uv0 = (variazione di tono, peso del vento).
    /// </summary>
    public static class PlantMeshes
    {
        private static readonly uint Trunk = 0x6B4E36;

        public static Mesh[] CreateAll()
        {
            var meshes = new Mesh[(int)PlantKind.Count];
            for (int i = 0; i < meshes.Length; i++)
            {
                var mb = new MeshBuffer();
                Build((PlantKind)i, mb);
                meshes[i] = MeshUpload.ToMesh(mb, "Plant_" + (PlantKind)i);
            }
            return meshes;
        }

        private static void Build(PlantKind kind, MeshBuffer mb)
        {
            switch (kind)
            {
                case PlantKind.Broadleaf:
                    Prism(mb, 0, 0, 0, 0.28f, 0.2f, 3.6f, 6, Trunk, 0, 0);
                    Blob(mb, 0, 0, 6.2f, 3.1f, 3.4f, 0x5F8F45, 1);
                    Blob(mb, 1.1f, 0.6f, 5.1f, 2.0f, 2.2f, 0x6E9C4D, 1);
                    break;
                case PlantKind.Conifer:
                    Prism(mb, 0, 0, 0, 0.3f, 0.2f, 2.5f, 6, Trunk, 0, 0);
                    Cone(mb, 0, 0, 2.0f, 2.6f, 5.0f, 0x3E6B3E, 8, 0.6f);
                    Cone(mb, 0, 0, 4.6f, 2.0f, 4.6f, 0x447445, 8, 0.8f);
                    Cone(mb, 0, 0, 7.4f, 1.3f, 4.6f, 0x4A7B4A, 8, 1f);
                    break;
                case PlantKind.Palm:
                    Prism(mb, 0, 0, 0, 0.32f, 0.24f, 7.8f, 6, 0x8A6E52, 0, 0.3f);
                    for (int k = 0; k < 7; k++)
                    {
                        float a = k * Mathf.PI * 2 / 7;
                        Frond(mb, a, 7.8f, 3.6f, 0x5E8E3E);
                    }
                    break;
                case PlantKind.Olive:
                    Prism(mb, 0, 0, 0, 0.32f, 0.22f, 1.7f, 6, 0x5E4A3A, 0, 0);
                    Blob(mb, 0, 0, 3.2f, 2.6f, 1.9f, 0x8A9A6E, 1);
                    Blob(mb, 0.9f, -0.5f, 2.8f, 1.6f, 1.3f, 0x95A47A, 1);
                    break;
                case PlantKind.Cypress:
                    Prism(mb, 0, 0, 0, 0.22f, 0.18f, 1.0f, 6, Trunk, 0, 0);
                    Spindle(mb, 0, 0, 0.6f, 1.1f, 12.4f, 0x355E39);
                    break;
                case PlantKind.StonePine:
                    Prism(mb, 0, 0, 0, 0.32f, 0.22f, 9.5f, 6, 0x7A5A40, 0, 0.1f);
                    Blob(mb, 0, 0, 11.2f, 5.0f, 1.9f, 0x4D7A45, 1);
                    break;
                case PlantKind.Fruit:
                    Prism(mb, 0, 0, 0, 0.18f, 0.14f, 1.4f, 6, Trunk, 0, 0);
                    Blob(mb, 0, 0, 2.9f, 1.9f, 1.7f, 0x6E9E4C, 1);
                    break;
                case PlantKind.Shrub:
                    Blob(mb, 0, 0, 1.0f, 1.4f, 1.1f, 0x6C8E4C, 0.6f);
                    break;
                case PlantKind.VineRow:
                    // spalliera: siepe stretta su un filo, pali alle estremità
                    Box(mb, -0.5f, -0.2f, 0.55f, 0.5f, 0.2f, 1.6f, 0x6F9446, 1, 0.35f);
                    Box(mb, -0.5f, -0.04f, 0f, -0.46f, 0.04f, 1.75f, 0x8B7458, 0, 0);
                    break;
            }
        }

        // --- primitive (est, nord, quota), triangoli antiorari visti da fuori

        private static void Prism(MeshBuffer mb, float x, float y, float z0, float r0, float r1, float h, int sides, uint color, float tint, float wind)
        {
            uint c = MeshBuffer.Rgb(color);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                float am = (a0 + a1) / 2;
                float nx = Mathf.Cos(am), ny = Mathf.Sin(am);
                int b0 = mb.Add(x + Mathf.Cos(a0) * r0, y + Mathf.Sin(a0) * r0, z0, nx, ny, 0, c, tint, 0, 0, 0, 0, 0, 0, 0);
                int b1 = mb.Add(x + Mathf.Cos(a1) * r0, y + Mathf.Sin(a1) * r0, z0, nx, ny, 0, c, tint, 0, 0, 0, 0, 0, 0, 0);
                int t1 = mb.Add(x + Mathf.Cos(a1) * r1, y + Mathf.Sin(a1) * r1, z0 + h, nx, ny, 0, c, tint, wind, 0, 0, 0, 0, 0, 0);
                int t0 = mb.Add(x + Mathf.Cos(a0) * r1, y + Mathf.Sin(a0) * r1, z0 + h, nx, ny, 0, c, tint, wind, 0, 0, 0, 0, 0, 0);
                mb.Quad(b0, b1, t1, t0);
            }
        }

        /// <summary>Chioma low-poly: tre anelli irregolari tra due poli, centro (x, y, z).</summary>
        private static void Blob(MeshBuffer mb, float x, float y, float z, float r, float halfH, uint color, float wind)
        {
            const int sides = 7;
            var rings = new[] { (-0.55f, 0.78f), (0.15f, 1.0f), (0.72f, 0.62f) };
            var pts = new Vector3[rings.Length * sides];
            for (int k = 0; k < rings.Length; k++)
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                float rr = r * rings[k].Item2 * (0.88f + 0.24f * Mathf.Abs(Mathf.Sin(a * 3 + k * 1.7f)));
                pts[k * sides + i] = new Vector3(x + Mathf.Cos(a) * rr, y + Mathf.Sin(a) * rr, z + rings[k].Item1 * halfH);
            }
            var bottom = new Vector3(x, y, z - halfH);
            var top = new Vector3(x, y, z + halfH);
            var center = new Vector3(x, y, z);
            uint c = MeshBuffer.Rgb(color);
            float zMin = z - halfH, zMax = z + halfH;
            int Add(Vector3 p, Vector3 n) =>
                mb.Add(p.x, p.y, p.z, n.x, n.y, n.z, c, 1, wind * Mathf.InverseLerp(zMin, zMax, p.z), 0, 0, 0, 0, 0, 0);
            void Tri(Vector3 a, Vector3 b, Vector3 d)
            {
                var n = Vector3.Cross(b - a, d - a).normalized;
                var radial = ((a + b + d) / 3 - center).normalized;
                n = (n * 0.4f + radial * 0.6f).normalized; // ombreggiatura morbida
                mb.Triangle(Add(a, n), Add(b, n), Add(d, n));
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Tri(bottom, pts[j], pts[i]);
                for (int k = 0; k + 1 < rings.Length; k++)
                {
                    var a = pts[k * sides + i];
                    var b = pts[k * sides + j];
                    var cc = pts[(k + 1) * sides + j];
                    var d = pts[(k + 1) * sides + i];
                    Tri(a, b, cc);
                    Tri(a, cc, d);
                }
                int last = (rings.Length - 1) * sides;
                Tri(pts[last + i], pts[last + j], top);
            }
        }

        private static void Cone(MeshBuffer mb, float x, float y, float z0, float r, float h, uint color, int sides, float wind)
        {
            uint c = MeshBuffer.Rgb(color);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides, am = (a0 + a1) / 2;
                var n = new Vector3(Mathf.Cos(am), Mathf.Sin(am), r / h).normalized;
                int b0 = mb.Add(x + Mathf.Cos(a0) * r, y + Mathf.Sin(a0) * r, z0, n.x, n.y, n.z, c, 1, wind * 0.3f, 0, 0, 0, 0, 0, 0);
                int b1 = mb.Add(x + Mathf.Cos(a1) * r, y + Mathf.Sin(a1) * r, z0, n.x, n.y, n.z, c, 1, wind * 0.3f, 0, 0, 0, 0, 0, 0);
                int t = mb.Add(x, y, z0 + h, n.x, n.y, n.z, c, 1, wind, 0, 0, 0, 0, 0, 0);
                mb.Triangle(b0, b1, t);
                int m = mb.Add(x, y, z0, 0, 0, -1, c, 1, 0, 0, 0, 0, 0, 0, 0);
                int b0d = mb.Add(x + Mathf.Cos(a0) * r, y + Mathf.Sin(a0) * r, z0, 0, 0, -1, c, 1, 0, 0, 0, 0, 0, 0, 0);
                int b1d = mb.Add(x + Mathf.Cos(a1) * r, y + Mathf.Sin(a1) * r, z0, 0, 0, -1, c, 1, 0, 0, 0, 0, 0, 0, 0);
                mb.Triangle(m, b1d, b0d);
            }
        }

        private static void Spindle(MeshBuffer mb, float x, float y, float z0, float r, float h, uint color)
        {
            // fuso del cipresso: due coni con base in comune a 1/3 dell'altezza
            Cone(mb, x, y, z0 + h * 0.3f, r, h * 0.7f, color, 7, 1f);
            uint c = MeshBuffer.Rgb(color);
            const int sides = 7;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides, am = (a0 + a1) / 2;
                var n = new Vector3(Mathf.Cos(am), Mathf.Sin(am), -0.3f).normalized;
                int b = mb.Add(x, y, z0, n.x, n.y, n.z, c, 1, 0, 0, 0, 0, 0, 0, 0);
                int t0 = mb.Add(x + Mathf.Cos(a0) * r, y + Mathf.Sin(a0) * r, z0 + h * 0.3f, n.x, n.y, n.z, c, 1, 0.3f, 0, 0, 0, 0, 0, 0);
                int t1 = mb.Add(x + Mathf.Cos(a1) * r, y + Mathf.Sin(a1) * r, z0 + h * 0.3f, n.x, n.y, n.z, c, 1, 0.3f, 0, 0, 0, 0, 0, 0);
                mb.Triangle(b, t1, t0);
            }
        }

        private static void Frond(MeshBuffer mb, float angle, float z, float len, uint color)
        {
            uint c = MeshBuffer.Rgb(color);
            float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);
            float px = -dy * 0.45f, py = dx * 0.45f;
            var tip = new Vector3(dx * len, dy * len, z - 1.2f);
            var mid = new Vector3(dx * len * 0.5f, dy * len * 0.5f, z + 0.3f);
            // due facce (sopra e sotto) per vederla da ogni lato
            int a = mb.Add(0, 0, z, 0, 0, 1, c, 1, 0.6f, 0, 0, 0, 0, 0, 0);
            int l = mb.Add(mid.x + px, mid.y + py, mid.z, 0, 0, 1, c, 1, 0.8f, 0, 0, 0, 0, 0, 0);
            int r = mb.Add(mid.x - px, mid.y - py, mid.z, 0, 0, 1, c, 1, 0.8f, 0, 0, 0, 0, 0, 0);
            int t = mb.Add(tip.x, tip.y, tip.z, 0, 0, 1, c, 1, 1f, 0, 0, 0, 0, 0, 0);
            mb.Triangle(a, r, t);
            mb.Triangle(a, t, l);
            int a2 = mb.Add(0, 0, z, 0, 0, -1, c, 1, 0.6f, 0, 0, 0, 0, 0, 0);
            int l2 = mb.Add(mid.x + px, mid.y + py, mid.z, 0, 0, -1, c, 1, 0.8f, 0, 0, 0, 0, 0, 0);
            int r2 = mb.Add(mid.x - px, mid.y - py, mid.z, 0, 0, -1, c, 1, 0.8f, 0, 0, 0, 0, 0, 0);
            int t2 = mb.Add(tip.x, tip.y, tip.z, 0, 0, -1, c, 1, 1f, 0, 0, 0, 0, 0, 0);
            mb.Triangle(a2, t2, r2);
            mb.Triangle(a2, l2, t2);
        }

        private static void Box(MeshBuffer mb, float x0, float y0, float z0, float x1, float y1, float z1, uint color, float tint, float wind)
        {
            uint c = MeshBuffer.Rgb(color);
            void Face(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 n, float wa, float wb, float wd, float we)
            {
                int i0 = mb.Add(a.x, a.y, a.z, n.x, n.y, n.z, c, tint, wa, 0, 0, 0, 0, 0, 0);
                int i1 = mb.Add(b.x, b.y, b.z, n.x, n.y, n.z, c, tint, wb, 0, 0, 0, 0, 0, 0);
                int i2 = mb.Add(d.x, d.y, d.z, n.x, n.y, n.z, c, tint, wd, 0, 0, 0, 0, 0, 0);
                int i3 = mb.Add(e.x, e.y, e.z, n.x, n.y, n.z, c, tint, we, 0, 0, 0, 0, 0, 0);
                mb.Quad(i0, i1, i2, i3);
            }
            var p000 = new Vector3(x0, y0, z0); var p100 = new Vector3(x1, y0, z0);
            var p010 = new Vector3(x0, y1, z0); var p110 = new Vector3(x1, y1, z0);
            var p001 = new Vector3(x0, y0, z1); var p101 = new Vector3(x1, y0, z1);
            var p011 = new Vector3(x0, y1, z1); var p111 = new Vector3(x1, y1, z1);
            Face(p000, p100, p101, p001, new Vector3(0, -1, 0), 0, 0, wind, wind); // sud
            Face(p110, p010, p011, p111, new Vector3(0, 1, 0), 0, 0, wind, wind);  // nord
            Face(p100, p110, p111, p101, new Vector3(1, 0, 0), 0, 0, wind, wind);  // est
            Face(p010, p000, p001, p011, new Vector3(-1, 0, 0), 0, 0, wind, wind); // ovest
            Face(p001, p101, p111, p011, new Vector3(0, 0, 1), wind, wind, wind, wind); // sopra
        }
    }
}
