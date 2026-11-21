using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Core.Traffic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Game
{
    /// <summary>
    /// Strada disegnata dal sindaco (docs/04 §4): spezzata in metri della regione, classe (vicolo → autostrada),
    /// senso unico e ponte. Gli incroci con le strade che attraversa nascono da soli nel grafo del traffico.
    /// </summary>
    public sealed class CityRoad
    {
        public int Id;
        public RoadClass Class = RoadClass.Residential;
        /// <summary>Punti (x, y alternati) in metri della regione.</summary>
        public float[] Points = Array.Empty<float>();
        /// <summary>1 = senso unico nel verso del disegno, 0 = doppio senso.</summary>
        public int Oneway;
        /// <summary>Ponte: passa sopra le strade senza incrociarle (si collega solo agli estremi).</summary>
        public bool Bridge;
        public DateTime BuiltAt;

        public float Length => Infrastructure.Length(Points);

        public CityRoad Clone()
        {
            var c = (CityRoad)MemberwiseClone();
            c.Points = (float[])Points.Clone();
            return c;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Id);
            w.Write((int)Class);
            Infrastructure.WritePoints(w, Points);
            w.Write(Oneway);
            w.Write(Bridge);
            w.Write(BuiltAt.Ticks);
        }

        public static CityRoad Read(BinaryReader r) => new CityRoad
        {
            Id = r.ReadInt32(),
            Class = (RoadClass)r.ReadInt32(),
            Points = Infrastructure.ReadPoints(r),
            Oneway = r.ReadInt32(),
            Bridge = r.ReadBoolean(),
            BuiltAt = new DateTime(r.ReadInt64()),
        };
    }

    /// <summary>
    /// Linea del trasporto pubblico del comune (docs/03 §7): fermate in ordine (metri della regione), frequenza e
    /// biglietto scelti dal sindaco. Va nei due sensi; i mezzi necessari dipendono dalla durata del giro e dalla frequenza.
    /// </summary>
    public sealed class CityLine
    {
        public int Id;
        public string Ref = "";
        public TransitMode Mode = TransitMode.Bus;
        public uint Colour = 0xE53935;
        /// <summary>Fermate (x, y alternati) in metri della regione.</summary>
        public float[] Stops = Array.Empty<float>();
        public int HeadwayMinutes = 20;
        public int FareCents = 130;
        public DateTime OpenedAt;

        public int StopCount => Stops.Length / 2;

        public CityLine Clone()
        {
            var c = (CityLine)MemberwiseClone();
            c.Stops = (float[])Stops.Clone();
            return c;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Id);
            w.Write(Ref ?? "");
            w.Write((int)Mode);
            w.Write(Colour);
            Infrastructure.WritePoints(w, Stops);
            w.Write(HeadwayMinutes);
            w.Write(FareCents);
            w.Write(OpenedAt.Ticks);
        }

        public static CityLine Read(BinaryReader r) => new CityLine
        {
            Id = r.ReadInt32(),
            Ref = r.ReadString(),
            Mode = (TransitMode)r.ReadInt32(),
            Colour = r.ReadUInt32(),
            Stops = Infrastructure.ReadPoints(r),
            HeadwayMinutes = r.ReadInt32(),
            FareCents = r.ReadInt32(),
            OpenedAt = new DateTime(r.ReadInt64()),
        };
    }

    /// <summary>
    /// Regole, costi e geometria delle strade e delle linee del sindaco (blocco 4c). I parametri stanno in
    /// data/balance/traffic.json (strade) e transit.json (linee), letti in <see cref="Balance.Traffic"/> e <see cref="Balance.Transit"/>.
    /// </summary>
    public static class Infrastructure
    {
        /// <summary>Proprietario delle celle dei lotti occupate da una strada del sindaco.</summary>
        public static int RoadOwner(int roadId) => 20_000_000 + roadId;

        private static readonly uint[] LineColours = { 0xE53935, 0x1E88E5, 0x43A047, 0xFB8C00, 0x8E24AA, 0x00897B, 0xF4511E, 0x3949AB, 0xC0CA33, 0x6D4C41 };

        /// <summary>Colore proposto per una nuova linea.</summary>
        public static uint LineColour(int id) => LineColours[(Math.Max(1, id) - 1) % LineColours.Length];

        public static float Length(float[] pts)
        {
            double len = 0;
            for (int i = 2; i + 1 < pts.Length; i += 2)
            {
                double dx = pts[i] - pts[i - 2], dy = pts[i + 1] - pts[i - 1];
                len += Math.Sqrt(dx * dx + dy * dy);
            }
            return (float)len;
        }

        // ------------------------------------------------------------------ strade

        /// <summary>Costo di costruzione (euro); -1 se la classe non si può disegnare.</summary>
        public static long RoadCost(TrafficBalance b, RoadClass cls, float length, bool bridge)
        {
            if (b == null || !b.Classes.TryGetValue(cls, out var spec) || spec.CostPerM <= 0) return -1;
            return (long)Math.Round(length * spec.CostPerM * (bridge ? b.BridgeCostFactor : 1));
        }

        public static double RoadMaintenancePerDay(TrafficBalance b, CityRoad r) =>
            b != null && b.Classes.TryGetValue(r.Class, out var spec) ? r.Length / 1000.0 * spec.MaintenancePerKmDay * (r.Bridge ? 2 : 1) : 0;

        public static float RoadWidth(TrafficBalance b, RoadClass cls) =>
            b != null && b.Classes.TryGetValue(cls, out var spec) ? (float)spec.WidthM : 6f;

        /// <summary>Marciapiede per lato delle vie urbane (come le strade vere disegnate da <c>RoadMesher</c>).</summary>
        public const float SidewalkWidth = 1.6f;

        /// <summary>Le vie urbane hanno i marciapiedi; superstrade, autostrade e ponti no.</summary>
        public static bool HasSidewalks(RoadClass cls, bool bridge) =>
            !bridge && (cls == RoadClass.Residential || cls == RoadClass.LivingStreet || cls == RoadClass.Tertiary
                        || cls == RoadClass.Secondary || cls == RoadClass.Primary);

        /// <summary>Larghezza occupata a terra: carreggiata più marciapiedi.</summary>
        public static float RoadFootprint(TrafficBalance b, RoadClass cls, bool bridge) =>
            RoadWidth(b, cls) + (HasSidewalks(cls, bridge) ? 2 * SidewalkWidth : 0);

        /// <summary>
        /// Controlla una strada nuova: forma (punti, tratti, lunghezza), classe disegnabile e spazio libero (niente
        /// edifici reali non demoliti né elementi piazzati sotto la carreggiata). Le strade esistenti si possono
        /// attraversare: diventano incroci.
        /// </summary>
        public static BuildResult CheckRoad(GameContext ctx, CityRoad road)
        {
            var b = ctx.Balance.Traffic;
            if (b == null || RoadCost(b, road.Class, 1, false) < 0) return BuildResult.NotAllowed;
            var p = road.Points;
            if (p == null || p.Length < 4 || p.Length % 2 != 0 || p.Length / 2 > b.MaxPoints) return BuildResult.InvalidPath;
            for (int i = 2; i + 1 < p.Length; i += 2)
            {
                double dx = p[i] - p[i - 2], dy = p[i + 1] - p[i - 1];
                if (Math.Sqrt(dx * dx + dy * dy) < b.MinSegmentM) return BuildResult.InvalidPath;
            }
            if (Length(p) > b.MaxLengthM) return BuildResult.InvalidPath;
            return CheckRoadSpace(ctx, p, RoadFootprint(b, road.Class, road.Bridge));
        }

        /// <summary>La strada (carreggiata e marciapiedi, con mezzo metro di margine) non tocca edifici né elementi piazzati.</summary>
        public static BuildResult CheckRoadSpace(GameContext ctx, float[] p, float width)
        {
            if (ctx.World == null || ctx.Frame == null) return BuildResult.Ok; // senza mondo (prove) si controlla solo la forma
            float half = width / 2 + 0.5f;
            var tiles = new List<TileId>();
            for (int i = 2; i + 1 < p.Length; i += 2)
            {
                var quad = SegmentQuad(p[i - 2], p[i - 1], p[i], p[i + 1], half);
                TilesUnder(ctx, quad, tiles);
                foreach (var id in tiles)
                {
                    var tile = ctx.World.Tile(id.X, id.Y);
                    if (tile == null) return BuildResult.LotNotLoaded;
                    ctx.Frame.TileOrigin(id, out var ox, out var oy);
                    var local = Offset(quad, -ox, -oy);
                    var box = Bounds2.Of(local);
                    foreach (var bi in tile.Buildings)
                    {
                        if (!bi.Bounds.Intersects(box)) continue;
                        if (ctx.State.DemolishedOsm.ContainsKey(GameState.OsmKey(bi.OsmType, bi.OsmId))) continue;
                        if (Shapes.Intersect(local, bi.Outer)) return BuildResult.Occupied;
                    }
                    foreach (var o in ctx.State.Objects.Values)
                    {
                        if (o.Lot.TileX != id.X || o.Lot.TileY != id.Y) continue;
                        var item = ctx.Catalog.Get(o.ItemKey);
                        var lot = Placement.FindLot(tile, o.Lot.LotId);
                        if (item == null || lot == null) continue;
                        if (Shapes.Intersect(local, Placement.Footprint(lot, item, o.HalfX, o.HalfY, o.Rotation))) return BuildResult.Occupied;
                    }
                }
            }
            return BuildResult.Ok;
        }

        /// <summary>Rettangolo attorno al tratto (a → b) largo 2 × <paramref name="half"/>, anello antiorario.</summary>
        public static float[] SegmentQuad(float ax, float ay, float bx, float by, float half)
        {
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) len = 1e-4f;
            float nx = -dy / len * half, ny = dx / len * half;
            return new[] { ax - nx, ay - ny, bx - nx, by - ny, bx + nx, by + ny, ax + nx, ay + ny };
        }

        private static float[] Offset(float[] ring, double ox, double oy)
        {
            var r = new float[ring.Length];
            for (int i = 0; i + 1 < ring.Length; i += 2)
            {
                r[i] = (float)(ring[i] + ox);
                r[i + 1] = (float)(ring[i + 1] + oy);
            }
            return r;
        }

        /// <summary>Tile (z del mondo) toccati dal riquadro della figura (metri della regione).</summary>
        private static void TilesUnder(GameContext ctx, float[] ring, List<TileId> into)
        {
            into.Clear();
            var box = Bounds2.Of(ring);
            var a = ctx.Frame.TileAt(ctx.TileZoom, box.MinX, box.MinY);
            var c = ctx.Frame.TileAt(ctx.TileZoom, box.MaxX, box.MaxY);
            for (int x = Math.Min(a.X, c.X); x <= Math.Max(a.X, c.X); x++)
                for (int y = Math.Min(a.Y, c.Y); y <= Math.Max(a.Y, c.Y); y++)
                    into.Add(new TileId(ctx.TileZoom, x, y));
        }

        /// <summary>Occupa nella griglia del lotto le celle sotto le strade del sindaco (coordinate del tile).</summary>
        public static void OccupyRoads(GameContext ctx, LotKey key, Orbis.Core.World.Lots.LotGrid grid)
        {
            if (ctx.Frame == null || ctx.State.Roads.Count == 0) return;
            ctx.Frame.TileOrigin(new TileId(ctx.TileZoom, key.TileX, key.TileY), out var ox, out var oy);
            var lotBox = grid.Lot.Bounds;
            foreach (var r in ctx.State.Roads.Values)
            {
                float half = RoadFootprint(ctx.Balance.Traffic, r.Class, r.Bridge) / 2;
                var p = r.Points;
                for (int i = 2; i + 1 < p.Length; i += 2)
                {
                    var quad = Offset(SegmentQuad(p[i - 2], p[i - 1], p[i], p[i + 1], half), -ox, -oy);
                    if (!Bounds2.Of(quad).Intersects(lotBox)) continue;
                    grid.Occupy(quad, RoadOwner(r.Id));
                }
            }
        }

        /// <summary>Strade del sindaco per il grafo del traffico.</summary>
        public static List<ExtraRoad> GraphRoads(GameState s)
        {
            var list = new List<ExtraRoad>(s.Roads.Count);
            foreach (var r in s.Roads.Values)
                list.Add(new ExtraRoad { Id = r.Id, Class = r.Class, Points = r.Points, Oneway = r.Oneway, Bridge = r.Bridge });
            return list;
        }

        // ------------------------------------------------------------------ linee

        /// <summary>Minuti di viaggio da capolinea a capolinea (stessa stima delle linee reali).</summary>
        public static double LineMinutes(TransitBalance b, CityLine l)
        {
            double kmh = b != null && b.Modes.TryGetValue(l.Mode, out var spec) ? spec.Kmh : 18;
            var m = TransitNetwork.StopMinutes(l.Stops, kmh);
            return m.Length > 0 ? m[m.Length - 1] : 0;
        }

        /// <summary>Mezzi necessari: un giro completo (andata, ritorno e sosta ai capolinea) diviso la frequenza.</summary>
        public static int Vehicles(TransitBalance b, CityLine l)
        {
            if (l.StopCount < 2) return 0;
            double layover = b != null ? b.City.LayoverMinutes : 10;
            return Math.Max(1, (int)Math.Ceiling((2 * LineMinutes(b, l) + layover) / Math.Max(1, l.HeadwayMinutes)));
        }

        /// <summary>Costo giornaliero per il comune: ore di servizio dei mezzi e manutenzione delle fermate.</summary>
        public static double LineCostPerDay(TransitBalance b, CityLine l)
        {
            if (b == null) return 0;
            return Vehicles(b, l) * b.City.ServiceHours * b.City.CostPerVehicleHour + l.StopCount * b.City.StopMaintenancePerDay;
        }

        /// <summary>
        /// Spesa (positiva) o rimborso (negativo) per passare dalla linea <paramref name="old"/> alla <paramref name="next"/>
        /// (null = non c'è): fermate nuove e mezzi in più si comprano; chiudendo la linea se ne recupera una parte.
        /// </summary>
        public static long LineChangeCost(TransitBalance b, CityLine old, CityLine next)
        {
            var c = b.City;
            if (next == null)
            {
                if (old == null) return 0;
                return -(long)Math.Round((old.StopCount * c.StopCost + Vehicles(b, old) * c.VehicleCost) * c.RefundRatio);
            }
            int newStops = 0;
            for (int i = 0; i + 1 < next.Stops.Length; i += 2)
                if (old == null || !HasStop(old, next.Stops[i], next.Stops[i + 1])) newStops++;
            int moreVehicles = Math.Max(0, Vehicles(b, next) - (old != null ? Vehicles(b, old) : 0));
            return (long)Math.Round(newStops * c.StopCost + moreVehicles * c.VehicleCost);
        }

        private static bool HasStop(CityLine l, float x, float y)
        {
            for (int i = 0; i + 1 < l.Stops.Length; i += 2)
                if (Math.Abs(l.Stops[i] - x) < 1f && Math.Abs(l.Stops[i + 1] - y) < 1f) return true;
            return false;
        }

        /// <summary>
        /// Controlla una linea: mezzo, fermate (numero, distanza minima, vicino a una strada), frequenza e biglietto ammessi.
        /// Le fermate nei tile non caricati non si possono verificare e si accettano.
        /// </summary>
        public static BuildResult CheckLine(GameContext ctx, CityLine l)
        {
            var b = ctx.Balance.Transit;
            if (b == null || !b.City.Modes.Contains(l.Mode) || !b.Modes.ContainsKey(l.Mode)) return BuildResult.NotAllowed;
            if (Array.IndexOf(b.City.Headways, l.HeadwayMinutes) < 0) return BuildResult.NotAllowed;
            if (l.FareCents < (int)Math.Round(b.City.FareMin * 100) || l.FareCents > (int)Math.Round(b.City.FareMax * 100)) return BuildResult.NotAllowed;
            var s = l.Stops;
            if (s == null || s.Length < 4 || s.Length % 2 != 0 || s.Length / 2 > b.City.MaxStops) return BuildResult.InvalidPath;
            for (int i = 2; i + 1 < s.Length; i += 2)
            {
                double dx = s[i] - s[i - 2], dy = s[i + 1] - s[i - 1];
                if (Math.Sqrt(dx * dx + dy * dy) < b.City.MinStopSpacingM) return BuildResult.InvalidPath;
            }
            if (ctx.World == null || ctx.Frame == null) return BuildResult.Ok;
            for (int i = 0; i + 1 < s.Length; i += 2)
                if (!NearRoad(ctx, s[i], s[i + 1], (float)b.City.MaxStopFromRoadM)) return BuildResult.NotNearRoad;
            return BuildResult.Ok;
        }

        /// <summary>Il punto è entro <paramref name="max"/> metri da una strada per le auto (vera o del sindaco)?</summary>
        public static bool NearRoad(GameContext ctx, float x, float y, float max)
        {
            foreach (var r in ctx.State.Roads.Values)
                if (Shapes.DistanceToPolyline(r.Points, x, y) <= max) return true;
            var id = ctx.Frame.TileAt(ctx.TileZoom, x, y);
            var tile = ctx.World.Tile(id.X, id.Y);
            if (tile == null) return true; // tile non caricato: non si può verificare
            ctx.Frame.TileOrigin(id, out var ox, out var oy);
            float lx = (float)(x - ox), ly = (float)(y - oy);
            var classes = ctx.Balance.Traffic?.Classes;
            foreach (var r in tile.Roads)
            {
                if (r.HasFlag(RoadFlag.Area) || (classes != null ? !classes.ContainsKey(r.Class) : r.Class > RoadClass.Service)) continue;
                if (Shapes.DistanceToPolyline(r.Points, lx, ly) <= max + r.Width / 2) return true;
            }
            return false;
        }

        /// <summary>Linee del sindaco per la rete del trasporto pubblico.</summary>
        public static List<CityLineInput> NetworkLines(GameState s)
        {
            var list = new List<CityLineInput>(s.Lines.Count);
            foreach (var l in s.Lines.Values)
                list.Add(new CityLineInput
                {
                    Id = l.Id, Mode = l.Mode, Ref = l.Ref, Colour = l.Colour, Stops = l.Stops,
                    HeadwayMinutes = l.HeadwayMinutes, FareCents = l.FareCents,
                });
            return list;
        }

        // ------------------------------------------------------------------ salvataggio

        public static void WritePoints(BinaryWriter w, float[] p)
        {
            w.Write(p.Length);
            foreach (var v in p) w.Write(v);
        }

        public static float[] ReadPoints(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 1_000_000) throw new InvalidDataException("punti non validi: " + n);
            var p = new float[n];
            for (int i = 0; i < n; i++) p[i] = r.ReadSingle();
            return p;
        }
    }

    /// <summary>Piccola geometria piana per strade e fermate.</summary>
    public static class Shapes
    {
        /// <summary>Due poligoni semplici (anelli x, y alternati) si toccano? Lati che si incrociano o uno dentro l'altro.</summary>
        public static bool Intersect(float[] a, float[] b)
        {
            int na = a.Length / 2, nb = b.Length / 2;
            if (na < 3 || nb < 3) return false;
            for (int i = 0; i < na; i++)
            {
                int i2 = (i + 1) % na;
                for (int j = 0; j < nb; j++)
                {
                    int j2 = (j + 1) % nb;
                    if (SegmentsCross(a[i * 2], a[i * 2 + 1], a[i2 * 2], a[i2 * 2 + 1], b[j * 2], b[j * 2 + 1], b[j2 * 2], b[j2 * 2 + 1])) return true;
                }
            }
            return Orbis.Core.World.Geometry.Polygon2D.ContainsRing(b, a[0], a[1]) || Orbis.Core.World.Geometry.Polygon2D.ContainsRing(a, b[0], b[1]);
        }

        public static bool SegmentsCross(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
        {
            float d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
            float d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static float Cross(float ax, float ay, float bx, float by, float px, float py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        /// <summary>Distanza del punto dal segmento, con il parametro t (0–1) del punto più vicino.</summary>
        public static float DistanceToSegment(float ax, float ay, float bx, float by, float px, float py, out float t)
        {
            float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            t = len2 > 1e-9f ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len2)) : 0;
            float qx = ax + dx * t - px, qy = ay + dy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        public static float DistanceToPolyline(float[] p, float x, float y)
        {
            float best = float.MaxValue;
            for (int i = 2; i + 1 < p.Length; i += 2)
                best = Math.Min(best, DistanceToSegment(p[i - 2], p[i - 1], p[i], p[i + 1], x, y, out _));
            if (p.Length == 2) best = (float)Math.Sqrt((p[0] - x) * (p[0] - x) + (p[1] - y) * (p[1] - y));
            return best;
        }
    }
}
