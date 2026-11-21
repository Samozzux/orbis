using System;
using Orbis.Core.Population;

namespace Orbis.Core.Traffic
{
    /// <summary>
    /// Traffico a livello L1 (docs/03 §7): la città è divisa in zone e i tempi in auto tra zona e zona si calcolano
    /// sul grafo stradale. I viaggi in auto dei cittadini caricano le strade del loro percorso; quando un giro di
    /// aggiornamento è completo, ogni tratto rallenta secondo la sua capacità (formula BPR). L'aggiornamento è
    /// distribuito: a ogni passo di 15 minuti si ricalcolano poche zone di partenza, mai un blocco lungo.
    /// </summary>
    public sealed class TrafficModel : ITravelTimes
    {
        public readonly RoadGraph Graph;
        private readonly TrafficBalance _b;
        private readonly float[] _seconds;   // tempo attuale per tratto
        private readonly float[] _flow;      // veicoli sul tratto dall'ultimo aggiornamento dei tempi
        private readonly double _x0, _y0;
        private readonly int _cols, _rows;
        private readonly int[] _zoneOfCell;
        private readonly int[] _zoneNode;
        public readonly int Zones;
        private readonly float[] _minutes;   // Zones × Zones
        private readonly int[] _od;          // viaggi da registrare sul percorso, per coppia di zone
        private int _next;
        private int _lastUpdate = -1;

        // memoria di lavoro di Dijkstra
        private readonly float[] _dist;
        private readonly int[] _pred;
        private readonly int[] _heap;
        private readonly int[] _heapPos;
        private int _heapSize;

        public TrafficModel(RoadGraph graph, TrafficBalance balance)
        {
            Graph = graph;
            _b = balance;
            _seconds = (float[])graph.FreeSeconds.Clone();
            _flow = new float[graph.EdgeCount];
            _dist = new float[graph.NodeCount];
            _pred = new int[graph.NodeCount];
            _heap = new int[graph.NodeCount];
            _heapPos = new int[graph.NodeCount];

            // zone: celle della griglia che contengono almeno un nodo
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int n = 0; n < graph.NodeCount; n++)
            {
                minX = Math.Min(minX, graph.X[n]);
                minY = Math.Min(minY, graph.Y[n]);
                maxX = Math.Max(maxX, graph.X[n]);
                maxY = Math.Max(maxY, graph.Y[n]);
            }
            if (graph.NodeCount == 0) minX = minY = maxX = maxY = 0;
            _x0 = minX;
            _y0 = minY;
            _cols = Math.Max(1, (int)Math.Ceiling((maxX - minX) / _b.ZoneSizeM) + 1);
            _rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / _b.ZoneSizeM) + 1);
            _zoneOfCell = new int[_cols * _rows];
            for (int i = 0; i < _zoneOfCell.Length; i++) _zoneOfCell[i] = -1;
            var nodes = new System.Collections.Generic.List<int>();
            for (int n = 0; n < graph.NodeCount; n++)
            {
                int cell = Cell(graph.X[n], graph.Y[n]);
                if (_zoneOfCell[cell] >= 0) continue;
                _zoneOfCell[cell] = nodes.Count;
                // nodo della zona: il più vicino al centro della cella
                float cx = (float)(_x0 + (cell % _cols + 0.5) * _b.ZoneSizeM), cy = (float)(_y0 + (cell / _cols + 0.5) * _b.ZoneSizeM);
                int near = graph.Nearest(cx, cy, (float)_b.ZoneSizeM);
                nodes.Add(near >= 0 ? near : n);
            }
            _zoneNode = nodes.ToArray();
            Zones = _zoneNode.Length;
            _minutes = new float[Zones * Zones];
            _od = new int[Zones * Zones];
            // prima stima in linea d'aria, poi i tempi veri arrivano zona per zona
            for (int a = 0; a < Zones; a++)
                for (int z = 0; z < Zones; z++)
                {
                    float dx = graph.X[_zoneNode[a]] - graph.X[_zoneNode[z]], dy = graph.Y[_zoneNode[a]] - graph.Y[_zoneNode[z]];
                    _minutes[a * Zones + z] = (float)(Math.Sqrt(dx * dx + dy * dy) * 1.4 / 1000.0 / 30.0 * 60.0);
                }
        }

        private int Cell(double x, double y)
        {
            int cx = Math.Max(0, Math.Min(_cols - 1, (int)((x - _x0) / _b.ZoneSizeM)));
            int cy = Math.Max(0, Math.Min(_rows - 1, (int)((y - _y0) / _b.ZoneSizeM)));
            return cy * _cols + cx;
        }

        /// <summary>Zona del punto, -1 se fuori dalla rete.</summary>
        public int Zone(float x, float y)
        {
            if (x < _x0 - _b.ZoneSizeM || y < _y0 - _b.ZoneSizeM || x > _x0 + _cols * _b.ZoneSizeM || y > _y0 + _rows * _b.ZoneSizeM) return -1;
            return _zoneOfCell[Cell(x, y)];
        }

        /// <summary>Minuti in auto: rete tra le zone, più accesso alla strada e parcheggio.</summary>
        public int CarMinutes(float x0, float y0, float x1, float y1)
        {
            int a = Zone(x0, y0), z = Zone(x1, y1);
            float dx = x1 - x0, dy = y1 - y0;
            double straight = Math.Sqrt(dx * dx + dy * dy) * 1.3 / 1000.0 / _b.AccessKmh * 60.0;
            if (a < 0 || z < 0 || a == z) return (int)Math.Ceiling(straight + _b.ParkingMinutes);
            double access = (Distance(x0, y0, _zoneNode[a]) + Distance(x1, y1, _zoneNode[z])) / 1000.0 / _b.AccessKmh * 60.0;
            return (int)Math.Ceiling(_minutes[a * Zones + z] + access + _b.ParkingMinutes);
        }

        public float ZoneMinutes(int a, int z) => _minutes[a * Zones + z];

        private double Distance(float x, float y, int node)
        {
            float dx = Graph.X[node] - x, dy = Graph.Y[node] - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public void RecordTrip(float x0, float y0, float x1, float y1)
        {
            int a = Zone(x0, y0), z = Zone(x1, y1);
            if (a >= 0 && z >= 0 && a != z) _od[a * Zones + z]++;
        }

        /// <summary>Ricalcola alcune zone di partenza; a giro completo aggiorna i tempi dei tratti col traffico.</summary>
        public void Tick(int minute)
        {
            if (Zones == 0) return;
            if (_lastUpdate < 0) _lastUpdate = minute;
            for (int k = 0; k < _b.OriginsPerStep && k < Zones; k++)
            {
                Refresh(_next);
                _next++;
                if (_next < Zones) continue;
                _next = 0;
                UpdateTimes(minute);
            }
        }

        /// <summary>Tutti i tempi subito (nei test).</summary>
        public void RefreshAll() => RefreshRange(0, Zones);

        /// <summary>Tempi a strada libera per un gruppo di zone di partenza (all'avvio, un po' per fotogramma).</summary>
        public void RefreshRange(int start, int count)
        {
            for (int a = start; a < Math.Min(Zones, start + count); a++) Refresh(a);
        }

        private void Refresh(int origin)
        {
            Dijkstra(_zoneNode[origin]);
            int row = origin * Zones;
            for (int z = 0; z < Zones; z++)
            {
                float d = _dist[_zoneNode[z]];
                if (!float.IsPositiveInfinity(d)) _minutes[row + z] = d / 60f;
                int trips = _od[row + z];
                if (trips == 0) continue;
                _od[row + z] = 0;
                // i viaggi registrati caricano i tratti del percorso più breve
                int n = _zoneNode[z], guard = 0;
                while (_pred[n] >= 0 && guard++ < Graph.NodeCount)
                {
                    int e = _pred[n];
                    _flow[e] += trips;
                    n = From(e);
                }
            }
        }

        private int From(int e)
        {
            // il nodo di partenza di un tratto: ricerca binaria su First
            int lo = 0, hi = Graph.NodeCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Graph.First[mid] <= e) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private void UpdateTimes(int minute)
        {
            double hours = Math.Max(0.25, (minute - _lastUpdate) / 60.0);
            _lastUpdate = minute;
            double scale = 1.0 / (1.0 - _b.BackgroundShare) / hours;
            for (int e = 0; e < Graph.EdgeCount; e++)
            {
                double v = _flow[e] * scale;
                double ratio = v / Math.Max(1, Graph.Capacity[e]);
                _seconds[e] = (float)(Graph.FreeSeconds[e] * (1 + _b.BprAlpha * Math.Pow(ratio, _b.BprBeta)));
                _flow[e] = 0;
            }
        }

        /// <summary>Rapporto tra tempo attuale e tempo a strada libera del tratto (1 = scorrevole), per le mappe del traffico.</summary>
        public float Congestion(int edge) => _seconds[edge] / Graph.FreeSeconds[edge];

        // ------------------------------------------------------------------ Dijkstra con heap binario

        private void Dijkstra(int source)
        {
            int n = Graph.NodeCount;
            for (int i = 0; i < n; i++)
            {
                _dist[i] = float.PositiveInfinity;
                _pred[i] = -1;
                _heapPos[i] = -1;
            }
            _heapSize = 0;
            _dist[source] = 0;
            Push(source);
            while (_heapSize > 0)
            {
                int u = Pop();
                float du = _dist[u];
                for (int e = Graph.First[u]; e < Graph.First[u + 1]; e++)
                {
                    int v = Graph.To[e];
                    float nd = du + _seconds[e];
                    if (nd >= _dist[v]) continue;
                    _dist[v] = nd;
                    _pred[v] = e;
                    if (_heapPos[v] < 0) Push(v);
                    else Up(_heapPos[v]);
                }
            }
        }

        private void Push(int v)
        {
            _heap[_heapSize] = v;
            _heapPos[v] = _heapSize;
            Up(_heapSize++);
        }

        private int Pop()
        {
            int top = _heap[0];
            _heapPos[top] = -2;
            _heapSize--;
            if (_heapSize > 0)
            {
                _heap[0] = _heap[_heapSize];
                _heapPos[_heap[0]] = 0;
                Down(0);
            }
            return top;
        }

        private void Up(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_dist[_heap[p]] <= _dist[_heap[i]]) break;
                Swap(i, p);
                i = p;
            }
        }

        private void Down(int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < _heapSize && _dist[_heap[l]] < _dist[_heap[m]]) m = l;
                if (r < _heapSize && _dist[_heap[r]] < _dist[_heap[m]]) m = r;
                if (m == i) return;
                Swap(i, m);
                i = m;
            }
        }

        private void Swap(int a, int b)
        {
            int t = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = t;
            _heapPos[_heap[a]] = a;
            _heapPos[_heap[b]] = b;
        }
    }
}
