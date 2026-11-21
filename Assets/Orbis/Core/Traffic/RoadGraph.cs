using System;
using System.Collections.Generic;
using Orbis.Core.Data;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Traffic
{
    /// <summary>Parametri del traffico (data/balance/traffic.json).</summary>
    public sealed class TrafficBalance
    {
        public struct ClassSpec
        {
            public double Kmh;
            public int Lanes;
            public double CapacityPerLane;
        }

        public double ZoneSizeM = 600, BprAlpha = 0.15, BprBeta = 4, BackgroundShare = 0.25, AccessKmh = 25, ParkingMinutes = 4;
        /// <summary>Margine attorno al comune entro cui tenere le strade (metri).</summary>
        public double AreaMarginM = 1000;
        public int OriginsPerStep = 40;
        public readonly Dictionary<RoadClass, ClassSpec> Classes = new Dictionary<RoadClass, ClassSpec>();

        public static TrafficBalance Parse(string json)
        {
            var r = Json.Parse(json);
            var b = new TrafficBalance
            {
                ZoneSizeM = r["zoneSizeM"].AsDouble(600),
                OriginsPerStep = Math.Max(1, r["originsPerStep"].AsInt(12)),
                AreaMarginM = r["areaMarginM"].AsDouble(1000),
                BprAlpha = r["bprAlpha"].AsDouble(0.15),
                BprBeta = r["bprBeta"].AsDouble(4),
                BackgroundShare = Math.Max(0, Math.Min(0.9, r["backgroundShare"].AsDouble(0.25))),
                AccessKmh = r["accessKmh"].AsDouble(25),
                ParkingMinutes = r["parkingMinutes"].AsDouble(4),
            };
            foreach (var k in r["classes"].Keys)
                if (Enum.TryParse<RoadClass>(k, out var cls))
                    b.Classes[cls] = new ClassSpec
                    {
                        Kmh = r["classes"][k]["kmh"].AsDouble(30),
                        Lanes = Math.Max(1, r["classes"][k]["lanes"].AsInt(1)),
                        CapacityPerLane = r["classes"][k]["capacityPerLane"].AsDouble(500),
                    };
            return b;
        }
    }

    /// <summary>
    /// Grafo stradale per le auto dalle strade vere dei tile (docs/03 §7): sensi unici, corsie, velocità e
    /// capacità per classe. Le catene di vertici senza incroci diventano un solo tratto (con la sua forma, per i
    /// veicoli visibili), così i percorsi costano poco anche sul telefono e nel browser.
    /// </summary>
    public sealed class RoadGraph
    {
        public int NodeCount;
        public float[] X, Y;
        /// <summary>Archi in forma compatta: quelli che partono dal nodo n sono [First[n], First[n+1]).</summary>
        public int[] First;
        public int EdgeCount;
        public int[] To;
        public float[] Length, FreeSeconds, Capacity;
        public long[] Road;
        /// <summary>Forma del tratto: punti [PointStart[e], PointStart[e+1]) in Points (x, y alternati).</summary>
        public int[] PointStart;
        public float[] Points;

        private Dictionary<long, List<int>> _grid;
        private const float GridCell = 100f;

        /// <summary>Costruisce il grafo dai tile (con l'angolo sud-ovest di ciascuno in metri della regione).</summary>
        /// <param name="area">riquadro (minX, minY, maxX, maxY) in metri della regione da tenere; null = tutto</param>
        public static RoadGraph Build(IEnumerable<(TileData Tile, double OriginX, double OriginY)> tiles, TrafficBalance b,
            (double MinX, double MinY, double MaxX, double MaxY)? area = null)
        {
            var raw = new RawGraph();
            foreach (var (tile, ox, oy) in tiles)
            {
                foreach (var r in tile.Roads)
                {
                    if (!b.Classes.TryGetValue(r.Class, out var spec) || r.Points.Length < 4) continue;
                    if (r.HasFlag(RoadFlag.Area)) continue;
                    double kmh = r.MaxSpeed > 0 ? Math.Min(r.MaxSpeed, spec.Kmh * 1.3) : spec.Kmh;
                    int lanes = r.Lanes > 0 ? (r.Oneway != 0 ? r.Lanes : Math.Max(1, r.Lanes / 2)) : spec.Lanes;
                    float capacity = (float)(lanes * spec.CapacityPerLane);
                    int prev = -1;
                    for (int i = 0; i + 1 < r.Points.Length; i += 2)
                    {
                        double px = ox + r.Points[i], py = oy + r.Points[i + 1];
                        if (area.HasValue && (px < area.Value.MinX || px > area.Value.MaxX || py < area.Value.MinY || py > area.Value.MaxY))
                        {
                            prev = -1; // fuori dall'area: la strada si interrompe qui
                            continue;
                        }
                        int node = raw.Node((float)px, (float)py);
                        if (prev >= 0 && prev != node)
                        {
                            if (r.Oneway >= 0) raw.Edge(prev, node, kmh, capacity, r.OsmId);
                            if (r.Oneway <= 0) raw.Edge(node, prev, kmh, capacity, r.OsmId);
                        }
                        prev = node;
                    }
                }
            }
            return raw.Contract();
        }

        /// <summary>Nodo più vicino al punto entro <paramref name="maxDistance"/> metri, -1 se non c'è.</summary>
        public int Nearest(float x, float y, float maxDistance = 600f)
        {
            if (_grid == null) BuildGrid();
            int cx = (int)Math.Floor(x / GridCell), cy = (int)Math.Floor(y / GridCell);
            int rings = (int)Math.Ceiling(maxDistance / GridCell);
            int best = -1;
            float bd = maxDistance * maxDistance;
            for (int r = 0; r <= rings; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        if (!_grid.TryGetValue(Key(cx + dx, cy + dy), out var list)) continue;
                        foreach (var n in list)
                        {
                            float ex = X[n] - x, ey = Y[n] - y, d = ex * ex + ey * ey;
                            if (d < bd)
                            {
                                bd = d;
                                best = n;
                            }
                        }
                    }
                // il ring successivo è almeno r × cella più lontano: se c'è già un nodo più vicino ci si ferma
                if (best >= 0 && bd <= (r * GridCell) * (r * GridCell)) break;
            }
            return best;
        }

        private void BuildGrid()
        {
            _grid = new Dictionary<long, List<int>>();
            for (int n = 0; n < NodeCount; n++)
            {
                long k = Key((int)Math.Floor(X[n] / GridCell), (int)Math.Floor(Y[n] / GridCell));
                if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>();
                l.Add(n);
            }
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        /// <summary>Grafo vertice per vertice durante la costruzione (prima di fondere le catene).</summary>
        private sealed class RawGraph
        {
            private readonly List<float> _x = new List<float>(), _y = new List<float>();
            private readonly Dictionary<long, int> _snap = new Dictionary<long, int>();
            private readonly List<(int From, int To, float Kmh, float Cap, long Road)> _edges = new List<(int, int, float, float, long)>();
            private readonly HashSet<long> _pairs = new HashSet<long>();

            // stesso punto (entro mezzo metro, anche tra tile vicini) = stesso nodo
            public int Node(float x, float y)
            {
                int kx = (int)Math.Round(x * 2), ky = (int)Math.Round(y * 2);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (_snap.TryGetValue(Key(kx + dx, ky + dy), out var n)
                            && Math.Abs(_x[n] - x) <= 0.5f && Math.Abs(_y[n] - y) <= 0.5f) return n;
                int id = _x.Count;
                _x.Add(x);
                _y.Add(y);
                _snap[Key(kx, ky)] = id;
                return id;
            }

            public void Edge(int from, int to, double kmh, float cap, long road)
            {
                if (!_pairs.Add(((long)from << 32) | (uint)to)) return; // stesso tratto da due tile o due strade
                _edges.Add((from, to, (float)kmh, cap, road));
            }

            public RoadGraph Contract()
            {
                int n = _x.Count;
                var outs = new List<int>[n];
                var ins = new List<int>[n];
                for (int i = 0; i < n; i++)
                {
                    outs[i] = new List<int>(2);
                    ins[i] = new List<int>(2);
                }
                for (int e = 0; e < _edges.Count; e++)
                {
                    outs[_edges[e].From].Add(e);
                    ins[_edges[e].To].Add(e);
                }
                // un vertice si può togliere se ha due soli vicini e il passaggio è coerente nei due sensi
                var keep = new bool[n];
                for (int v = 0; v < n; v++) keep[v] = !Passthrough(v, outs, ins);

                var map = new int[n];
                int count = 0;
                for (int v = 0; v < n; v++) map[v] = keep[v] ? count++ : -1;
                var g = new RoadGraph { NodeCount = count, X = new float[count], Y = new float[count] };
                for (int v = 0; v < n; v++)
                    if (keep[v])
                    {
                        g.X[map[v]] = _x[v];
                        g.Y[map[v]] = _y[v];
                    }

                var edges = new List<(int From, int To, float Len, float Sec, float Cap, long Road, int P0, int P1)>();
                var pts = new List<float>();
                for (int v = 0; v < n; v++)
                {
                    if (!keep[v]) continue;
                    foreach (var e0 in outs[v])
                    {
                        int p0 = pts.Count;
                        pts.Add(_x[v]);
                        pts.Add(_y[v]);
                        float len = 0, sec = 0, cap = float.MaxValue;
                        int cur = v, e = e0, guard = 0;
                        long road = _edges[e0].Road;
                        while (true)
                        {
                            var ed = _edges[e];
                            float dx = _x[ed.To] - _x[cur], dy = _y[ed.To] - _y[cur];
                            float l = (float)Math.Sqrt(dx * dx + dy * dy);
                            len += l;
                            sec += l / (ed.Kmh / 3.6f);
                            cap = Math.Min(cap, ed.Cap);
                            cur = ed.To;
                            pts.Add(_x[cur]);
                            pts.Add(_y[cur]);
                            if (keep[cur] || ++guard > n) break;
                            // prosegue sull'unica uscita che non torna indietro
                            int next = -1;
                            foreach (var o in outs[cur])
                                if (_edges[o].To != (ed.From)) next = o;
                            if (next < 0) break;
                            e = next;
                        }
                        if (!keep[cur]) continue; // anello senza incroci: si scarta
                        edges.Add((map[v], map[cur], len, sec, cap, road, p0, pts.Count));
                    }
                }
                edges.Sort((a, b) => a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));
                int m = edges.Count;
                g.EdgeCount = m;
                g.First = new int[count + 1];
                g.To = new int[m];
                g.Length = new float[m];
                g.FreeSeconds = new float[m];
                g.Capacity = new float[m];
                g.Road = new long[m];
                g.PointStart = new int[m + 1];
                var outPts = new List<float>(pts.Count);
                for (int i = 0; i < m; i++)
                {
                    var ed = edges[i];
                    g.First[ed.From + 1]++;
                    g.To[i] = ed.To;
                    g.Length[i] = ed.Len;
                    g.FreeSeconds[i] = Math.Max(0.5f, ed.Sec);
                    g.Capacity[i] = ed.Cap;
                    g.Road[i] = ed.Road;
                    g.PointStart[i] = outPts.Count;
                    for (int k = ed.P0; k < ed.P1; k++) outPts.Add(pts[k]);
                }
                g.PointStart[m] = outPts.Count;
                for (int i = 0; i < count; i++) g.First[i + 1] += g.First[i];
                g.Points = outPts.ToArray();
                return g;
            }

            private bool Passthrough(int v, List<int>[] outs, List<int>[] ins)
            {
                var nb = new HashSet<int>();
                foreach (var e in outs[v]) nb.Add(_edges[e].To);
                foreach (var e in ins[v]) nb.Add(_edges[e].From);
                if (nb.Count != 2) return false;
                // per ogni ingresso da un vicino c'è l'uscita verso l'altro, e viceversa (senso unico o doppio)
                foreach (var e in ins[v])
                {
                    bool ok = false;
                    foreach (var o in outs[v])
                        if (_edges[o].To != _edges[e].From) ok = true;
                    if (!ok) return false;
                }
                foreach (var o in outs[v])
                {
                    bool ok = false;
                    foreach (var e in ins[v])
                        if (_edges[e].From != _edges[o].To) ok = true;
                    if (!ok) return false;
                }
                return outs[v].Count > 0 && ins[v].Count > 0;
            }
        }
    }
}
