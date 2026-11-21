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
            /// <summary>Strade del sindaco: larghezza della carreggiata, costo al metro (0 = non si può disegnare) e manutenzione.</summary>
            public double WidthM, CostPerM, MaintenancePerKmDay;
        }

        /// <summary>Regole del disegno delle strade del sindaco (blocco 4c).</summary>
        public double SnapM = 8, MinSegmentM = 4, MaxLengthM = 5000, BridgeCostFactor = 4;
        public int MaxPoints = 200;

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
                        WidthM = r["classes"][k]["widthM"].AsDouble(6),
                        CostPerM = r["classes"][k]["costPerM"].AsDouble(0),
                        MaintenancePerKmDay = r["classes"][k]["maintenancePerKmDay"].AsDouble(0),
                    };
            var c = r["cityRoads"];
            b.SnapM = c["snapM"].AsDouble(b.SnapM);
            b.MinSegmentM = c["minSegmentM"].AsDouble(b.MinSegmentM);
            b.MaxLengthM = c["maxLengthM"].AsDouble(b.MaxLengthM);
            b.MaxPoints = c["maxPoints"].AsInt(b.MaxPoints);
            b.BridgeCostFactor = c["bridgeCostFactor"].AsDouble(b.BridgeCostFactor);
            return b;
        }
    }

    /// <summary>Strada disegnata dal sindaco per il grafo (metri della regione).</summary>
    public struct ExtraRoad
    {
        public int Id;
        public RoadClass Class;
        public float[] Points;
        public int Oneway;
        /// <summary>Ponte: si collega solo agli estremi, senza incroci con le strade che sorvola.</summary>
        public bool Bridge;
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
        /// <param name="extra">strade del sindaco: gli estremi si agganciano alla rete, gli attraversamenti diventano incroci</param>
        public static RoadGraph Build(IEnumerable<(TileData Tile, double OriginX, double OriginY)> tiles, TrafficBalance b,
            (double MinX, double MinY, double MaxX, double MaxY)? area = null, IReadOnlyList<ExtraRoad> extra = null)
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
            if (extra != null)
                foreach (var r in extra)
                    raw.AddExtra(r, b);
            return raw.Contract();
        }

        /// <summary>Archi (indici) della strada del sindaco <paramref name="id"/> dopo la fusione delle catene.</summary>
        public List<int> EdgesOfExtra(int id)
        {
            var list = new List<int>();
            for (int e = 0; e < EdgeCount; e++)
                if (Road[e] == -id) list.Add(e);
            return list;
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
            private struct RawEdge
            {
                public int From, To;
                public float Kmh, Cap;
                public long Road;
                public bool Dead;
            }

            private readonly List<RawEdge> _edges = new List<RawEdge>();
            private readonly Dictionary<long, int> _pairs = new Dictionary<long, int>();
            // indice spaziale degli archi, costruito solo se ci sono strade del sindaco
            private Dictionary<long, List<int>> _edgeGrid;
            private const float EdgeCell = 50f;

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
                long pair = ((long)from << 32) | (uint)to;
                if (from == to || _pairs.ContainsKey(pair)) return; // stesso tratto da due tile o due strade
                _pairs[pair] = _edges.Count;
                _edges.Add(new RawEdge { From = from, To = to, Kmh = (float)kmh, Cap = cap, Road = road });
                if (_edgeGrid != null) Index(_edges.Count - 1);
            }

            // ---------------------------------------------------------- strade del sindaco

            private void Index(int e)
            {
                var ed = _edges[e];
                float x0 = Math.Min(_x[ed.From], _x[ed.To]), x1 = Math.Max(_x[ed.From], _x[ed.To]);
                float y0 = Math.Min(_y[ed.From], _y[ed.To]), y1 = Math.Max(_y[ed.From], _y[ed.To]);
                for (int cx = (int)Math.Floor(x0 / EdgeCell); cx <= (int)Math.Floor(x1 / EdgeCell); cx++)
                    for (int cy = (int)Math.Floor(y0 / EdgeCell); cy <= (int)Math.Floor(y1 / EdgeCell); cy++)
                    {
                        long k = Key(cx, cy);
                        if (!_edgeGrid.TryGetValue(k, out var l)) _edgeGrid[k] = l = new List<int>();
                        l.Add(e);
                    }
            }

            private void EnsureEdgeGrid()
            {
                if (_edgeGrid != null) return;
                _edgeGrid = new Dictionary<long, List<int>>();
                for (int e = 0; e < _edges.Count; e++) Index(e);
            }

            /// <summary>Archi vivi (uno per coppia di nodi, senza il verso) con il riquadro che tocca quello dato.</summary>
            private void EdgesNear(float x0, float y0, float x1, float y1, List<int> into)
            {
                into.Clear();
                var seen = new HashSet<long>();
                for (int cx = (int)Math.Floor(x0 / EdgeCell); cx <= (int)Math.Floor(x1 / EdgeCell); cx++)
                    for (int cy = (int)Math.Floor(y0 / EdgeCell); cy <= (int)Math.Floor(y1 / EdgeCell); cy++)
                    {
                        if (!_edgeGrid.TryGetValue(Key(cx, cy), out var l)) continue;
                        foreach (var e in l)
                        {
                            var ed = _edges[e];
                            if (ed.Dead) continue;
                            int a = Math.Min(ed.From, ed.To), b = Math.Max(ed.From, ed.To);
                            if (seen.Add(((long)a << 32) | (uint)b)) into.Add(e);
                        }
                    }
            }

            /// <summary>Divide l'arco (e quello opposto, se c'è) nel punto: ritorna il nodo nuovo.</summary>
            private int Split(int e, float x, float y)
            {
                var ed = _edges[e];
                int m = Node(x, y);
                if (m == ed.From || m == ed.To) return m;
                SplitOne(e, m);
                if (_pairs.TryGetValue(((long)ed.To << 32) | (uint)ed.From, out var back)) SplitOne(back, m);
                return m;
            }

            private void SplitOne(int e, int m)
            {
                var ed = _edges[e];
                ed.Dead = true;
                _edges[e] = ed;
                _pairs.Remove(((long)ed.From << 32) | (uint)ed.To);
                Edge(ed.From, m, ed.Kmh, ed.Cap, ed.Road);
                Edge(m, ed.To, ed.Kmh, ed.Cap, ed.Road);
            }

            /// <summary>Estremo di una strada nuova: si aggancia al nodo o al tratto più vicino entro <paramref name="snap"/> metri.</summary>
            private int Attach(float x, float y, float snap, List<int> near)
            {
                EdgesNear(x - snap, y - snap, x + snap, y + snap, near);
                int bestNode = -1, bestEdge = -1;
                float nodeD = snap, edgeD = snap, edgeT = 0;
                foreach (var e in near)
                {
                    var ed = _edges[e];
                    foreach (var n in new[] { ed.From, ed.To })
                    {
                        float d = (float)Math.Sqrt((_x[n] - x) * (_x[n] - x) + (_y[n] - y) * (_y[n] - y));
                        if (d <= nodeD)
                        {
                            nodeD = d;
                            bestNode = n;
                        }
                    }
                    float de = SegmentDistance(_x[ed.From], _y[ed.From], _x[ed.To], _y[ed.To], x, y, out var t);
                    if (de < edgeD)
                    {
                        edgeD = de;
                        bestEdge = e;
                        edgeT = t;
                    }
                }
                // un incrocio esistente vince se è vicino quasi quanto il tratto
                if (bestNode >= 0 && nodeD <= edgeD + 1f) return bestNode;
                if (bestEdge >= 0)
                {
                    var ed = _edges[bestEdge];
                    return Split(bestEdge, _x[ed.From] + (_x[ed.To] - _x[ed.From]) * edgeT, _y[ed.From] + (_y[ed.To] - _y[ed.From]) * edgeT);
                }
                return Node(x, y);
            }

            private static float SegmentDistance(float ax, float ay, float bx, float by, float px, float py, out float t)
            {
                float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                t = len2 > 1e-9f ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len2)) : 0;
                float qx = ax + dx * t - px, qy = ay + dy * t - py;
                return (float)Math.Sqrt(qx * qx + qy * qy);
            }

            /// <summary>
            /// Aggiunge una strada del sindaco: gli estremi si agganciano alla rete, ogni attraversamento di una strada
            /// esistente diventa un incrocio (tranne che per i ponti), poi i tratti nel verso (o nei due versi) scelto.
            /// </summary>
            public void AddExtra(ExtraRoad r, TrafficBalance b)
            {
                if (r.Points == null || r.Points.Length < 4 || !b.Classes.TryGetValue(r.Class, out var spec)) return;
                EnsureEdgeGrid();
                float cap = (float)(spec.Lanes * spec.CapacityPerLane);
                var near = new List<int>();
                var nodes = new List<int>();
                var hits = new List<(float T, int Node)>();
                int count = r.Points.Length / 2;
                float px = 0, py = 0;
                for (int i = 0; i < count; i++)
                {
                    float x = r.Points[i * 2], y = r.Points[i * 2 + 1];
                    int node = i == 0 || i == count - 1 ? Attach(x, y, (float)b.SnapM, near) : Node(x, y);
                    if (i > 0 && !r.Bridge)
                    {
                        // incroci con le strade attraversate dal tratto (prima di aggiungere i tratti nuovi)
                        hits.Clear();
                        EdgesNear(Math.Min(px, x), Math.Min(py, y), Math.Max(px, x), Math.Max(py, y), near);
                        float segLen = (float)Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                        foreach (var e in near)
                        {
                            var ed = _edges[e];
                            if (!Intersection(px, py, x, y, _x[ed.From], _y[ed.From], _x[ed.To], _y[ed.To], out var t, out var u)) continue;
                            if (t * segLen < 0.5f || (1 - t) * segLen < 0.5f) continue; // è un estremo del tratto: già agganciato
                            float ix = px + (x - px) * t, iy = py + (y - py) * t;
                            hits.Add((t, Split(e, ix, iy)));
                        }
                        hits.Sort((a, c) => a.T.CompareTo(c.T));
                        foreach (var h in hits) nodes.Add(h.Node);
                    }
                    nodes.Add(node);
                    px = x;
                    py = y;
                }
                for (int i = 1; i < nodes.Count; i++)
                {
                    int a = nodes[i - 1], c = nodes[i];
                    if (a == c) continue;
                    Edge(a, c, spec.Kmh, cap, -r.Id);
                    if (r.Oneway <= 0) Edge(c, a, spec.Kmh, cap, -r.Id);
                }
            }

            /// <summary>Incrocio tra i segmenti p0→p1 e q0→q1: parametri t (sul primo) e u (sul secondo) in [0, 1].</summary>
            private static bool Intersection(float p0x, float p0y, float p1x, float p1y, float q0x, float q0y, float q1x, float q1y, out float t, out float u)
            {
                t = u = 0;
                float rx = p1x - p0x, ry = p1y - p0y, sx = q1x - q0x, sy = q1y - q0y;
                float den = rx * sy - ry * sx;
                if (Math.Abs(den) < 1e-6f) return false; // paralleli
                float qpx = q0x - p0x, qpy = q0y - p0y;
                t = (qpx * sy - qpy * sx) / den;
                u = (qpx * ry - qpy * rx) / den;
                return t >= 0 && t <= 1 && u >= 0 && u <= 1;
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
                    if (_edges[e].Dead) continue;
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
