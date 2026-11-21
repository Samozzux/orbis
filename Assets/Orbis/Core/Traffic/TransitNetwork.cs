using System;
using System.Collections.Generic;
using Orbis.Core.Data;
using Orbis.Core.Population;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Traffic
{
    /// <summary>Parametri del trasporto pubblico (data/balance/transit.json).</summary>
    public sealed class TransitBalance
    {
        public struct ModeSpec
        {
            public double Kmh;
            public int HeadwayMinutes;
            public double Fare;
        }

        /// <summary>Linee del sindaco (blocco 4c): costi per il comune, regole del disegno, biglietto.</summary>
        public sealed class CityLineSpec
        {
            public readonly HashSet<TransitMode> Modes = new HashSet<TransitMode> { TransitMode.Bus };
            public double StopCost = 6000, StopMaintenancePerDay = 1, VehicleCost = 60000, CostPerVehicleHour = 30;
            public double ServiceHours = 16, LayoverMinutes = 10, FareMin, FareMax = 3, DefaultFare = 1.3, RefundRatio = 0.2;
            public int[] Headways = { 5, 10, 15, 20, 30, 60 };
            public int MaxStops = 80;
            public double MinStopSpacingM = 100, MaxStopFromRoadM = 30;
        }

        public double WalkToStopM = 600, WalkKmh = 4.5;
        public readonly Dictionary<TransitMode, ModeSpec> Modes = new Dictionary<TransitMode, ModeSpec>();
        public readonly HashSet<string> ExcludeOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public CityLineSpec City = new CityLineSpec();

        public static TransitBalance Parse(string json)
        {
            var r = Json.Parse(json);
            var b = new TransitBalance
            {
                WalkToStopM = r["walkToStopM"].AsDouble(600),
                WalkKmh = r["walkKmh"].AsDouble(4.5),
            };
            foreach (var k in r["modes"].Keys)
                if (Enum.TryParse<TransitMode>(k, out var mode))
                    b.Modes[mode] = new ModeSpec
                    {
                        Kmh = r["modes"][k]["kmh"].AsDouble(18),
                        HeadwayMinutes = Math.Max(1, r["modes"][k]["headwayMinutes"].AsInt(20)),
                        Fare = r["modes"][k]["fare"].AsDouble(1.3),
                    };
            foreach (var o in r["excludeOperators"].Items) b.ExcludeOperators.Add(o.AsString(""));
            var c = r["cityLines"];
            if (!c.IsNull)
            {
                var city = b.City;
                if (c["modes"].Count > 0)
                {
                    city.Modes.Clear();
                    foreach (var m in c["modes"].Items)
                        if (Enum.TryParse<TransitMode>(m.AsString(""), out var mode)) city.Modes.Add(mode);
                }
                city.StopCost = c["stopCost"].AsDouble(city.StopCost);
                city.StopMaintenancePerDay = c["stopMaintenancePerDay"].AsDouble(city.StopMaintenancePerDay);
                city.VehicleCost = c["vehicleCost"].AsDouble(city.VehicleCost);
                city.CostPerVehicleHour = c["costPerVehicleHour"].AsDouble(city.CostPerVehicleHour);
                city.ServiceHours = c["serviceHours"].AsDouble(city.ServiceHours);
                city.LayoverMinutes = c["layoverMinutes"].AsDouble(city.LayoverMinutes);
                city.FareMin = c["fareMin"].AsDouble(city.FareMin);
                city.FareMax = c["fareMax"].AsDouble(city.FareMax);
                city.DefaultFare = c["defaultFare"].AsDouble(city.DefaultFare);
                city.RefundRatio = c["refundRatio"].AsDouble(city.RefundRatio);
                city.MaxStops = c["maxStops"].AsInt(city.MaxStops);
                city.MinStopSpacingM = c["minStopSpacingM"].AsDouble(city.MinStopSpacingM);
                city.MaxStopFromRoadM = c["maxStopFromRoadM"].AsDouble(city.MaxStopFromRoadM);
                if (c["headways"].Count > 0)
                {
                    var list = new List<int>();
                    foreach (var h in c["headways"].Items) list.Add(Math.Max(1, h.AsInt(20)));
                    city.Headways = list.ToArray();
                }
            }
            return b;
        }
    }

    /// <summary>Linea con le fermate in ordine (metri della regione) e i minuti di viaggio dalla prima fermata.</summary>
    public sealed class TransitLine
    {
        public long OsmId;
        public TransitMode Mode;
        public string Ref, Name, Operator;
        public uint Colour;
        public float[] X, Y;
        public float[] Minutes;
        public int HeadwayMinutes;
        public int FareCents;
        /// <summary>Viaggi fatti dai cittadini su questa linea.</summary>
        public long Passengers;
        /// <summary>Linea del comune (ID della linea del sindaco, presente nei due sensi); 0 = linea reale di un'azienda.</summary>
        public int CityId;
    }

    /// <summary>Linea del sindaco da aggiungere alla rete (fermate in metri della regione).</summary>
    public struct CityLineInput
    {
        public int Id;
        public TransitMode Mode;
        public string Ref;
        public uint Colour;
        public float[] Stops;
        public int HeadwayMinutes;
        public int FareCents;
    }

    /// <summary>
    /// Trasporto pubblico dalle linee reali del pack v4 (docs/03 §7): a piedi fino alla fermata, attesa (metà
    /// della frequenza), viaggio sulla linea nel suo verso (le relazioni OSM hanno un verso), a piedi all'arrivo.
    /// </summary>
    public sealed class TransitNetwork : ITransit
    {
        public readonly List<TransitLine> Lines = new List<TransitLine>();
        private readonly TransitBalance _b;
        private readonly Dictionary<long, List<(int Line, int Stop)>> _grid = new Dictionary<long, List<(int, int)>>();
        private const float Cell = 200f;

        private TransitNetwork(TransitBalance b) => _b = b;

        /// <summary>Rete senza linee reali (solo quelle del comune, o nessuna).</summary>
        public static TransitNetwork Empty(TransitBalance b) => new TransitNetwork(b);

        /// <summary>Minuti dalla prima fermata a ciascuna: percorso stradale stimato (1,3 × linea d'aria) più mezzo minuto di sosta.</summary>
        public static float[] StopMinutes(float[] xy, double kmh)
        {
            int n = xy.Length / 2;
            var minutes = new float[n];
            for (int i = 1; i < n; i++)
            {
                float dx = xy[i * 2] - xy[i * 2 - 2], dy = xy[i * 2 + 1] - xy[i * 2 - 1];
                double km = Math.Sqrt(dx * dx + dy * dy) * 1.3 / 1000.0;
                minutes[i] = minutes[i - 1] + (float)(km / kmh * 60.0 + 0.5);
            }
            return minutes;
        }
        /// <param name="area">riquadro (metri della regione) che deve contenere almeno due fermate della linea</param>
        public static TransitNetwork Build(RegionInfo region, TransitBalance b, (double MinX, double MinY, double MaxX, double MaxY)? area = null)
        {
            var net = new TransitNetwork(b);
            var frame = region.CreateFrame();
            foreach (var r in region.TransitRoutes)
            {
                if (!b.Modes.TryGetValue(r.Mode, out var spec) || r.Stops.Length < 2) continue;
                if (b.ExcludeOperators.Contains(r.Operator ?? "") || b.ExcludeOperators.Contains(r.Network ?? "")) continue;
                var xs = new List<float>();
                var ys = new List<float>();
                int inside = 0;
                foreach (var s in r.Stops)
                {
                    var stop = region.TransitStops[s];
                    frame.FromLonLat(stop.Lon, stop.Lat, out var x, out var y);
                    // stessa fermata ripetuta di fila (fermata e banchina): una sola
                    if (xs.Count > 0 && Math.Abs(xs[xs.Count - 1] - x) < 5 && Math.Abs(ys[ys.Count - 1] - y) < 5) continue;
                    xs.Add((float)x);
                    ys.Add((float)y);
                    if (!area.HasValue || (x >= area.Value.MinX && x <= area.Value.MaxX && y >= area.Value.MinY && y <= area.Value.MaxY)) inside++;
                }
                if (xs.Count < 2 || inside < 2) continue;
                var xy = new float[xs.Count * 2];
                for (int i = 0; i < xs.Count; i++)
                {
                    xy[i * 2] = xs[i];
                    xy[i * 2 + 1] = ys[i];
                }
                var minutes = StopMinutes(xy, spec.Kmh);
                net.Add(new TransitLine
                {
                    OsmId = r.OsmId,
                    Mode = r.Mode,
                    Ref = r.Ref,
                    Name = r.Name,
                    Operator = r.Operator,
                    Colour = r.Colour,
                    X = xs.ToArray(),
                    Y = ys.ToArray(),
                    Minutes = minutes,
                    HeadwayMinutes = spec.HeadwayMinutes,
                    FareCents = (int)Math.Round(spec.Fare * 100),
                });
            }
            return net;
        }

        private void Add(TransitLine line)
        {
            int index = Lines.Count;
            Lines.Add(line);
            for (int i = 0; i < line.X.Length; i++)
            {
                long k = Key(line.X[i], line.Y[i]);
                if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<(int, int)>();
                l.Add((index, i));
            }
        }

        /// <summary>
        /// Sostituisce le linee del comune (dopo un disegno, un annulla, un caricamento). Ogni linea va nei due sensi;
        /// le linee reali restano com'erano, con i loro passeggeri.
        /// </summary>
        public void SetCityLines(IEnumerable<CityLineInput> lines)
        {
            var real = new List<TransitLine>();
            var riders = new Dictionary<long, long>(); // passeggeri già contati per linea e senso
            var seen = new HashSet<int>();
            foreach (var l in Lines)
            {
                if (l.CityId == 0) real.Add(l);
                else riders[(long)l.CityId * 2 + (seen.Add(l.CityId) ? 0 : 1)] = l.Passengers;
            }
            Lines.Clear();
            _grid.Clear();
            foreach (var l in real) Add(l);
            if (lines == null) return;
            foreach (var c in lines)
            {
                if (c.Stops == null || c.Stops.Length < 4 || !_b.Modes.TryGetValue(c.Mode, out var spec)) continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    var xy = dir == 0 ? c.Stops : Reverse(c.Stops);
                    int n = xy.Length / 2;
                    var xs = new float[n];
                    var ys = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        xs[i] = xy[i * 2];
                        ys[i] = xy[i * 2 + 1];
                    }
                    Add(new TransitLine
                    {
                        CityId = c.Id, Mode = c.Mode, Ref = c.Ref, Name = c.Ref, Colour = c.Colour,
                        X = xs, Y = ys, Minutes = StopMinutes(xy, spec.Kmh),
                        HeadwayMinutes = Math.Max(1, c.HeadwayMinutes), FareCents = Math.Max(0, c.FareCents),
                        Passengers = riders.TryGetValue((long)c.Id * 2 + dir, out var p) ? p : 0,
                    });
                }
            }
        }

        private static float[] Reverse(float[] xy)
        {
            int n = xy.Length / 2;
            var r = new float[xy.Length];
            for (int i = 0; i < n; i++)
            {
                r[i * 2] = xy[(n - 1 - i) * 2];
                r[i * 2 + 1] = xy[(n - 1 - i) * 2 + 1];
            }
            return r;
        }

        /// <summary>Passeggeri di una linea del comune (somma dei due sensi).</summary>
        public long CityPassengers(int cityId)
        {
            long n = 0;
            foreach (var l in Lines)
                if (l.CityId == cityId) n += l.Passengers;
            return n;
        }

        public bool IsCityLine(int line) => line >= 0 && line < Lines.Count && Lines[line].CityId != 0;

        private static long Key(float x, float y) => Key((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));
        private static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        private void Near(float x, float y, List<(int Line, int Stop, float Walk)> into)
        {
            into.Clear();
            int cx = (int)Math.Floor(x / Cell), cy = (int)Math.Floor(y / Cell);
            int rings = (int)Math.Ceiling(_b.WalkToStopM / Cell);
            float max2 = (float)(_b.WalkToStopM * _b.WalkToStopM);
            for (int dx = -rings; dx <= rings; dx++)
                for (int dy = -rings; dy <= rings; dy++)
                {
                    if (!_grid.TryGetValue(Key(cx + dx, cy + dy), out var list)) continue;
                    foreach (var (l, s) in list)
                    {
                        float ex = Lines[l].X[s] - x, ey = Lines[l].Y[s] - y, d2 = ex * ex + ey * ey;
                        if (d2 <= max2) into.Add((l, s, (float)(Math.Sqrt(d2) * 1.2 / 1000.0 / _b.WalkKmh * 60.0)));
                    }
                }
        }

        private readonly List<(int Line, int Stop, float Walk)> _from = new List<(int, int, float)>();
        private readonly List<(int Line, int Stop, float Walk)> _to = new List<(int, int, float)>();

        public int Minutes(float x0, float y0, float x1, float y1, out int line)
        {
            line = -1;
            if (Lines.Count == 0) return -1;
            Near(x0, y0, _from);
            if (_from.Count == 0) return -1;
            Near(x1, y1, _to);
            if (_to.Count == 0) return -1;
            float best = float.MaxValue;
            foreach (var a in _from)
                foreach (var z in _to)
                {
                    if (a.Line != z.Line || z.Stop <= a.Stop) continue;
                    var l = Lines[a.Line];
                    float t = a.Walk + l.HeadwayMinutes * 0.5f + (l.Minutes[z.Stop] - l.Minutes[a.Stop]) + z.Walk;
                    if (t < best)
                    {
                        best = t;
                        line = a.Line;
                    }
                }
            return line < 0 ? -1 : (int)Math.Ceiling(best);
        }

        public int FareCents(int line) => line >= 0 && line < Lines.Count ? Lines[line].FareCents : 0;

        public void Ride(int line)
        {
            if (line >= 0 && line < Lines.Count) Lines[line].Passengers++;
        }
    }
}
