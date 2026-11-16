using System;
using System.Collections.Generic;

namespace Orbis.Core.World.Geometry
{
    /// <summary>
    /// Triangolazione di poligoni con buchi per "ear clipping": i buchi vengono collegati all'anello esterno
    /// con un ponte (metodo di Eberly), poi si tagliano le "orecchie". Adatta a sagome di edifici
    /// (fino a qualche centinaio di vertici).
    /// </summary>
    public static class Triangulator
    {
        /// <summary>
        /// Triangola. <paramref name="outer"/> antiorario, <paramref name="holes"/> orari (vedi Polygon2D.Clean).
        /// Restituisce i vertici (esterno poi buchi, nell'ordine dato) e i triangoli antiorari come indici.
        /// </summary>
        public static bool Triangulate(List<Vec2> outer, List<List<Vec2>> holes, List<Vec2> vertices, List<int> triangles)
        {
            vertices.Clear();
            triangles.Clear();
            if (outer == null || outer.Count < 3) return false;
            vertices.AddRange(outer);
            var ring = new List<int>(outer.Count + 16);
            for (int i = 0; i < outer.Count; i++) ring.Add(i);

            if (holes != null && holes.Count > 0)
            {
                // buchi in ordine di x massima decrescente
                var order = new List<(int index, float maxX, int maxI)>();
                for (int h = 0; h < holes.Count; h++)
                {
                    var hole = holes[h];
                    if (hole == null || hole.Count < 3) continue;
                    int mi = 0;
                    for (int k = 1; k < hole.Count; k++)
                        if (hole[k].X > hole[mi].X) mi = k;
                    order.Add((h, hole[mi].X, mi));
                }
                order.Sort((a, b) => b.maxX.CompareTo(a.maxX));
                foreach (var (index, _, maxI) in order)
                {
                    var hole = holes[index];
                    int baseIndex = vertices.Count;
                    vertices.AddRange(hole);
                    if (!Bridge(vertices, ring, baseIndex, hole.Count, maxI)) return false;
                }
            }
            return Clip(vertices, ring, triangles);
        }

        private static bool Bridge(List<Vec2> v, List<int> ring, int holeStart, int holeCount, int holeMax)
        {
            var m = v[holeStart + holeMax];
            // raggio verso +x da M: lato più vicino attraversato
            float bestX = float.MaxValue;
            int bestEdge = -1;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var a = v[ring[i]];
                var b = v[ring[(i + 1) % n]];
                if ((a.Y > m.Y) == (b.Y > m.Y) && !(a.Y == m.Y && b.Y == m.Y)) continue;
                if (a.Y == b.Y) continue;
                float t = (m.Y - a.Y) / (b.Y - a.Y);
                if (t < 0 || t > 1) continue;
                float x = a.X + t * (b.X - a.X);
                if (x >= m.X - 1e-6f && x < bestX)
                {
                    bestX = x;
                    bestEdge = i;
                }
            }
            if (bestEdge < 0) return false;
            var ia = ring[bestEdge];
            var ib = ring[(bestEdge + 1) % n];
            int p = v[ia].X > v[ib].X ? bestEdge : (bestEdge + 1) % n;
            var pv = v[ring[p]];
            var ip = new Vec2(bestX, m.Y);
            // vertici riflessi dentro il triangolo (M, I, P): si sceglie quello con l'angolo minore col raggio
            float bestAngle = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var q = v[ring[i]];
                if (ring[i] == ring[p]) continue;
                if (!IsReflex(v, ring, i)) continue;
                if (!PointInTriangle(q, m, ip, pv) && !PointInTriangle(q, m, pv, ip)) continue;
                var d = q - m;
                float ang = (float)Math.Atan2(Math.Abs(d.Y), d.X);
                if (d.X > 0 && ang < bestAngle)
                {
                    bestAngle = ang;
                    p = i;
                }
            }
            // ring: ... P, M, (buco a partire da M), M, P ...
            var insert = new List<int>(holeCount + 2);
            for (int k = 0; k <= holeCount; k++) insert.Add(holeStart + (holeMax + k) % holeCount);
            insert.Add(ring[p]);
            ring.InsertRange(p + 1, insert);
            return true;
        }

        private static bool IsReflex(List<Vec2> v, List<int> ring, int i)
        {
            int n = ring.Count;
            var a = v[ring[(i + n - 1) % n]];
            var b = v[ring[i]];
            var c = v[ring[(i + 1) % n]];
            return Vec2.Cross(b - a, c - b) < 0;
        }

        private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
        {
            float d1 = Vec2.Cross(b - a, p - a);
            float d2 = Vec2.Cross(c - b, p - b);
            float d3 = Vec2.Cross(a - c, p - c);
            return d1 >= 0 && d2 >= 0 && d3 >= 0;
        }

        private static bool Clip(List<Vec2> v, List<int> ring, List<int> tris)
        {
            var idx = new List<int>(ring);
            int guard = idx.Count * idx.Count + 16;
            int fails = 0;
            int i = 0;
            while (idx.Count > 3 && guard-- > 0)
            {
                int n = idx.Count;
                int ia = idx[(i + n - 1) % n], ib = idx[i % n], ic = idx[(i + 1) % n];
                var a = v[ia];
                var b = v[ib];
                var c = v[ic];
                float cross = Vec2.Cross(b - a, c - b);
                bool ear = cross > 1e-9f;
                if (ear)
                {
                    for (int k = 0; k < n; k++)
                    {
                        int q = idx[k];
                        if (q == ia || q == ib || q == ic) continue;
                        var p = v[q];
                        // i vertici duplicati dai ponti coincidono con quelli del triangolo: si ignorano
                        if (Same(p, a) || Same(p, b) || Same(p, c)) continue;
                        if (PointInTriangle(p, a, b, c))
                        {
                            ear = false;
                            break;
                        }
                    }
                }
                if (ear || fails >= n)
                {
                    if (cross > -1e-6f) // in caso di blocco si accettano solo triangoli non invertiti
                    {
                        tris.Add(ia);
                        tris.Add(ib);
                        tris.Add(ic);
                    }
                    idx.RemoveAt(i % n);
                    fails = 0;
                    if (i >= idx.Count) i = 0;
                    continue;
                }
                fails++;
                i = (i + 1) % n;
            }
            if (idx.Count == 3)
            {
                var a = v[idx[0]];
                var b = v[idx[1]];
                var c = v[idx[2]];
                if (Vec2.Cross(b - a, c - b) > 1e-9f)
                {
                    tris.Add(idx[0]);
                    tris.Add(idx[1]);
                    tris.Add(idx[2]);
                }
            }
            return tris.Count >= 3;
        }

        private static bool Same(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) < 1e-5f && Math.Abs(a.Y - b.Y) < 1e-5f;
    }
}
