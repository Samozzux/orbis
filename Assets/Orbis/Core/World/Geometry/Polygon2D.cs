using System;
using System.Collections.Generic;

namespace Orbis.Core.World.Geometry
{
    /// <summary>Utilità per poligoni 2D (anelli come liste piatte [x0, y0, x1, y1…] o liste di Vec2).</summary>
    public static class Polygon2D
    {
        public static Vec2[] ToPoints(float[] flat)
        {
            var r = new Vec2[flat.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Vec2(flat[2 * i], flat[2 * i + 1]);
            return r;
        }

        /// <summary>Area con segno (positiva se antiorario).</summary>
        public static float SignedArea(IReadOnlyList<Vec2> ring)
        {
            double a = 0;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                a += (double)ring[j].X * ring[i].Y - (double)ring[i].X * ring[j].Y;
            return (float)(a * 0.5);
        }

        public static float SignedArea(float[] flat)
        {
            double a = 0;
            int n = flat.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
                a += (double)flat[2 * j] * flat[2 * i + 1] - (double)flat[2 * i] * flat[2 * j + 1];
            return (float)(a * 0.5);
        }

        public static bool ContainsRing(float[] ring, float x, float y)
        {
            bool inside = false;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = ring[2 * i], yi = ring[2 * i + 1], xj = ring[2 * j], yj = ring[2 * j + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>Il punto è dentro l'anello esterno e fuori da tutti i buchi.</summary>
        public static bool Contains(float[] outer, float[][] holes, float x, float y)
        {
            if (!ContainsRing(outer, x, y)) return false;
            if (holes != null)
                for (int h = 0; h < holes.Length; h++)
                    if (ContainsRing(holes[h], x, y)) return false;
            return true;
        }

        /// <summary>Toglie punti doppi e quasi allineati; restituisce l'anello nell'orientamento richiesto.</summary>
        public static List<Vec2> Clean(float[] flat, bool ccw, float minEdge = 0.05f)
        {
            var pts = new List<Vec2>(flat.Length / 2);
            for (int i = 0; i + 1 < flat.Length; i += 2)
            {
                var p = new Vec2(flat[i], flat[i + 1]);
                if (pts.Count > 0 && (p - pts[pts.Count - 1]).Length < minEdge) continue;
                pts.Add(p);
            }
            while (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).Length < minEdge) pts.RemoveAt(pts.Count - 1);
            // punti allineati
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count > 3; i++)
                {
                    var a = pts[(i + pts.Count - 1) % pts.Count];
                    var b = pts[i];
                    var c = pts[(i + 1) % pts.Count];
                    var ab = b - a;
                    var bc = c - b;
                    float cross = Vec2.Cross(ab, bc);
                    float len = ab.Length * bc.Length;
                    if (len < 1e-8f || Math.Abs(cross) / len < 0.002f && Vec2.Dot(ab, bc) > 0)
                    {
                        pts.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }
            }
            if (pts.Count >= 3 && (SignedArea(pts) > 0) != ccw) pts.Reverse();
            return pts;
        }

        public static Vec2 Centroid(IReadOnlyList<Vec2> ring)
        {
            double a = 0, cx = 0, cy = 0;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double f = (double)ring[j].X * ring[i].Y - (double)ring[i].X * ring[j].Y;
                a += f;
                cx += (ring[j].X + ring[i].X) * f;
                cy += (ring[j].Y + ring[i].Y) * f;
            }
            if (Math.Abs(a) < 1e-9)
            {
                float sx = 0, sy = 0;
                for (int i = 0; i < n; i++)
                {
                    sx += ring[i].X;
                    sy += ring[i].Y;
                }
                return new Vec2(sx / Math.Max(1, n), sy / Math.Max(1, n));
            }
            return new Vec2((float)(cx / (3 * a)), (float)(cy / (3 * a)));
        }

        /// <summary>Taglia un poligono convesso con il semipiano a·p + b ≤ 0 (Sutherland–Hodgman).</summary>
        public static List<Vec2> ClipHalfPlane(List<Vec2> poly, Vec2 a, float b)
        {
            var r = new List<Vec2>(poly.Count + 2);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % n];
                float dp = Vec2.Dot(a, p) + b;
                float dq = Vec2.Dot(a, q) + b;
                bool pin = dp <= 0, qin = dq <= 0;
                if (pin) r.Add(p);
                if (pin != qin)
                {
                    float t = dp / (dp - dq);
                    r.Add(Vec2.Lerp(p, q, t));
                }
            }
            return r;
        }
    }

    /// <summary>Rettangolo orientato di area minima (assi U lungo, V corto).</summary>
    public struct OrientedBox
    {
        public Vec2 Center;
        public Vec2 U;
        public Vec2 V;
        public float HalfU;
        public float HalfV;

        public float ToU(Vec2 p) => Vec2.Dot(p - Center, U);
        public float ToV(Vec2 p) => Vec2.Dot(p - Center, V);

        public static OrientedBox Of(IReadOnlyList<Vec2> pts)
        {
            var best = new OrientedBox();
            float bestArea = float.MaxValue;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var e = (pts[(i + 1) % n] - pts[i]).Normalized;
                if (e.X == 0 && e.Y == 0) continue;
                var f = new Vec2(-e.Y, e.X);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                for (int k = 0; k < n; k++)
                {
                    float u = Vec2.Dot(pts[k], e), v = Vec2.Dot(pts[k], f);
                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }
                float area = (maxU - minU) * (maxV - minV);
                if (area < bestArea - 1e-4f)
                {
                    bestArea = area;
                    float cu = (minU + maxU) / 2, cv = (minV + maxV) / 2;
                    best.Center = e * cu + f * cv;
                    if (maxU - minU >= maxV - minV)
                    {
                        best.U = e; best.V = f; best.HalfU = (maxU - minU) / 2; best.HalfV = (maxV - minV) / 2;
                    }
                    else
                    {
                        best.U = f; best.V = new Vec2(-e.X, -e.Y); // V a sinistra di U
                        best.HalfU = (maxV - minV) / 2; best.HalfV = (maxU - minU) / 2;
                    }
                }
            }
            return best;
        }
    }
}
