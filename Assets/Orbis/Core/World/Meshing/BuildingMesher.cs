using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>Quota del terreno nel punto (metri locali del tile).</summary>
    public delegate float GroundSampler(float x, float y);

    /// <summary>
    /// Genera la mesh di un edificio dalla sua sagoma reale (docs/02 §4, docs/04 §4):
    /// muri esattamente sul perimetro OSM e tetto calcolato come "minimo di piani" sopra la sagoma,
    /// così il modello non esce mai dall'impronta. Tetti: piano, a padiglione (scheletro esatto per le
    /// sagome convesse, sul rettangolo orientato per le altre), a capanna, a piramide, a falda unica.
    ///
    /// Dati per lo shader Orbis/Building:
    ///   uv0 = (posizione lungo il muro m, quota sopra la strada m, lunghezza del muro m, altezza della gronda m)
    ///         per i tetti: (coordinata lungo la falda, coordinata lungo la pendenza, 0, 0)
    ///   uv1 = (tipo di superficie: 0 muro, 1 tetto a falde, 2 tetto piano; seme 0..1; piani; categoria*2 + negozi)
    /// </summary>
    public sealed class BuildingMesher
    {
        public const float LevelHeight = 3.2f;

        public enum Category
        {
            Residential = 0, Commercial = 1, Industrial = 2, Religious = 3, Rural = 4, Glass = 5, Blind = 6,
        }

        private struct Plane
        {
            public float A, B, C; // h = A x + B y + C

            public float At(Vec2 p) => A * p.X + B * p.Y + C;
        }

        private readonly List<Plane> _planes = new List<Plane>(16);
        private readonly List<Vec2> _triVerts = new List<Vec2>(64);
        private readonly List<int> _tris = new List<int>(192);
        private readonly List<float> _breaks = new List<float>(16);
        private readonly List<int> _column = new List<int>(32);
        private readonly List<Vec2> _clip = new List<Vec2>(8);

        /// <summary>Altezza effettiva del tetto calcolata nell'ultima chiamata (per i test).</summary>
        /// <summary>
        /// Centro del campo per la tribuna che si sta per costruire (lo imposta chi costruisce il tile; null se non c'è un
        /// campo vicino): le tribune diventano gradinate rivolte verso il campo invece di blocchi con le finestre.
        /// </summary>
        public Vec2? FieldCenter;

        /// <summary>Altezza di una fila di sedili sulle gradinate (shader: tipo 18).</summary>
        public const float SeatRowHeight = 0.42f;

        public float LastRoofHeight { get; private set; }
        public RoofShape LastRoof { get; private set; }
        public float LastEave { get; private set; }
        public float LastBase { get; private set; }

        // facciata dell'ultimo edificio costruito: servono agli addobbi (BuildingDecorator) per allinearsi a finestre e vetrine
        public List<Vec2> LastOuter { get; private set; }
        public float LastGroundRef { get; private set; }
        public float LastWallHeight { get; private set; }
        public int LastLevels { get; private set; }
        public Category LastCategory { get; private set; }
        public bool LastShops { get; private set; }
        public bool LastFlatRoof { get; private set; }
        public uint LastWallColor { get; private set; }
        public uint LastRoofColor { get; private set; }
        /// <summary>Il tetto ha falde vere (non piano): ai bordi di gronda la falda parte dalla quota di gronda.</summary>
        public bool LastPitched { get; private set; }

        /// <summary>Quota del tetto nel punto (ultimo edificio): gronda + falda.</summary>
        public float RoofTopAt(Vec2 p) => LastEave + RoofAt(p);

        /// <summary>Larghezza delle campate della facciata per categoria (uguale allo shader degli edifici).</summary>
        public static float BayWidth(Category c)
        {
            switch (c)
            {
                case Category.Residential: return 3.3f;
                case Category.Commercial: return 3.8f;
                case Category.Industrial: return 6.5f;
                case Category.Religious: return 5.0f;
                default: return 3.6f;
            }
        }

        /// <summary>Altezza di un piano (uguale allo shader): parete divisa per i piani, tra 2,4 e 6 m.</summary>
        public static float FloorHeight(float wallH, int levels) => Math.Max(2.4f, Math.Min(6.0f, wallH / Math.Max(1, levels)));

        /// <param name="simple">LOD lontano: tetto piano e niente dettagli</param>
        public bool Build(BuildingInfo b, GroundSampler ground, MeshBuffer mb, bool hasShops, bool simple = false)
        {
            var outer = Polygon2D.Clean(b.Outer, true);
            if (outer.Count < 3 || Math.Abs(Polygon2D.SignedArea(outer)) < 0.5f) return false;
            List<List<Vec2>> holes = null;
            if (b.Holes != null && b.Holes.Length > 0)
            {
                holes = new List<List<Vec2>>(b.Holes.Length);
                foreach (var h in b.Holes)
                {
                    var r = Polygon2D.Clean(h, false);
                    if (r.Count >= 3 && Math.Abs(Polygon2D.SignedArea(r)) > 0.5f) holes.Add(r);
                }
            }

            // terreno: la base scende fino al punto più basso, l'altezza si misura dalla quota media
            float gMin = float.MaxValue, gSum = 0;
            for (int i = 0; i < outer.Count; i++)
            {
                float g = ground(outer[i].X, outer[i].Y);
                gSum += g;
                if (g < gMin) gMin = g;
            }
            float gRef = gSum / outer.Count;
            float height = Math.Max(2.5f, b.Height);
            bool part = (b.Flags & BuildingFlag.Part) != 0;
            float baseZ = b.MinHeight > 0.1f ? gRef + b.MinHeight : gMin - 0.6f;
            if (b.MinHeight > 0.1f && b.MinHeight >= height - 0.5f) return false;

            // tribune degli stadi (chi costruisce il tile imposta FieldCenter solo per loro): gradinata che sale allontanandosi dal campo
            if (FieldCenter.HasValue && !simple && b.MinHeight <= 0.1f &&
                BuildStand(b, outer, holes, gMin, gRef, height, FieldCenter.Value, mb))
                return true;

            var roof = simple ? RoofShape.Flat : b.Roof;
            float roofH = BuildPlanes(b, outer, holes, height, ref roof);
            float eave = gRef + height - roofH;
            LastRoofHeight = roofH;
            LastRoof = roof;
            LastEave = eave;
            LastBase = baseZ;

            float seed = StylePalette.Random01(b.OsmId, 5);
            int levels = b.Levels > 0 ? b.Levels : Math.Max(1, (int)Math.Round((height - roofH) / LevelHeight));
            float wallH = eave - gRef;
            float category = (int)CategoryOf(b.Use) * 2 + (hasShops || b.HasFlag(BuildingFlag.HasShops) ? 1 : 0);
            uint wall = MeshBuffer.Rgb(StylePalette.WallColor(b), (byte)b.Material);
            // tetto previsto a falde ma geometria piana (da lontano, cortili, sagome molto irregolari):
            // resta piano ma con i coppi, come appare dall'alto un centro storico
            bool pitchedIntent = b.Roof != RoofShape.Flat && b.Roof != RoofShape.Unknown;
            bool tiledFlat = _planes.Count == 0 && pitchedIntent;
            uint roofColor = MeshBuffer.Rgb(StylePalette.RoofColor(b, !pitchedIntent));

            LastOuter = outer;
            LastGroundRef = gRef;
            LastWallHeight = wallH;
            LastLevels = levels;
            LastCategory = CategoryOf(b.Use);
            LastShops = hasShops || b.HasFlag(BuildingFlag.HasShops);
            LastFlatRoof = _planes.Count == 0 && !pitchedIntent;
            LastWallColor = StylePalette.WallColor(b);
            LastRoofColor = StylePalette.RoofColor(b, !pitchedIntent);
            LastPitched = _planes.Count > 0;

            AddWalls(outer, mb, baseZ, eave, gRef, wallH, wall, seed, levels, category, simple);
            if (holes != null)
                foreach (var h in holes)
                    AddWalls(h, mb, baseZ, eave, gRef, wallH, wall, seed, levels, category, simple);
            AddRoof(outer, holes, mb, eave, roofColor, seed, levels, tiledFlat);
            return true;
        }

        public static Category CategoryOf(BuildingUse use)
        {
            switch (use)
            {
                case BuildingUse.Commercial:
                case BuildingUse.Retail:
                case BuildingUse.Office:
                case BuildingUse.Hotel:
                case BuildingUse.Hospital:
                case BuildingUse.School:
                case BuildingUse.University:
                case BuildingUse.Kindergarten:
                case BuildingUse.Civic:
                case BuildingUse.Government:
                case BuildingUse.TrainStation:
                case BuildingUse.Transportation:
                    return Category.Commercial;
                case BuildingUse.Industrial:
                case BuildingUse.Warehouse:
                case BuildingUse.Sports:
                case BuildingUse.Stadium:
                case BuildingUse.Service:
                    return Category.Industrial;
                case BuildingUse.Church:
                case BuildingUse.Cathedral:
                case BuildingUse.Chapel:
                case BuildingUse.Religious:
                case BuildingUse.Castle:
                case BuildingUse.Tower:
                case BuildingUse.Monument:
                case BuildingUse.Theatre:
                case BuildingUse.Museum:
                    return Category.Religious;
                case BuildingUse.Farm:
                case BuildingUse.FarmAuxiliary:
                case BuildingUse.Barn:
                case BuildingUse.Stable:
                case BuildingUse.Shed:
                case BuildingUse.Hut:
                    return Category.Rural;
                case BuildingUse.Greenhouse:
                    return Category.Glass;
                case BuildingUse.Garage:
                case BuildingUse.Garages:
                case BuildingUse.Roof:
                case BuildingUse.Ruins:
                case BuildingUse.Parking:
                case BuildingUse.Bunker:
                case BuildingUse.Toilets:
                case BuildingUse.Kiosk:
                case BuildingUse.Bridge:
                case BuildingUse.Construction:
                    return Category.Blind;
                default:
                    return Category.Residential;
            }
        }

        // ------------------------------------------------------------------ tetto

        /// <summary>Prepara i piani del tetto; restituisce l'altezza del tetto (0 = piano).</summary>
        private float BuildPlanes(BuildingInfo b, List<Vec2> outer, List<List<Vec2>> holes, float height, ref RoofShape roof)
        {
            _planes.Clear();
            if (roof == RoofShape.Flat || roof == RoofShape.Unknown || holes != null && holes.Count > 0)
            {
                roof = RoofShape.Flat;
                return 0;
            }
            var box = OrientedBox.Of(outer);
            if (box.HalfV < 0.8f)
            {
                roof = RoofShape.Flat;
                return 0;
            }
            float area = Math.Abs(Polygon2D.SignedArea(outer));
            float fill = area / (4 * box.HalfU * box.HalfV);
            bool convex = IsConvex(outer);

            float r = b.RoofHeight > 0.1f ? b.RoofHeight : Clamp(0.36f * box.HalfV, 0.6f, 5.5f);
            r = Math.Min(r, height * 0.45f);
            if (height - r < 2.2f) r = Math.Max(0, height - 2.2f);
            if (r < 0.3f)
            {
                roof = RoofShape.Flat;
                return 0;
            }

            switch (roof)
            {
                case RoofShape.Hipped:
                case RoofShape.HalfHipped:
                case RoofShape.Mansard:
                    if (convex && outer.Count <= 16)
                    {
                        // scheletro esatto di un poligono convesso: distanza dal lato più vicino
                        float s = r / box.HalfV;
                        for (int i = 0; i < outer.Count; i++)
                        {
                            var a = outer[i];
                            var d = (outer[(i + 1) % outer.Count] - a).Normalized;
                            var nl = new Vec2(-d.Y, d.X);
                            _planes.Add(new Plane { A = s * nl.X, B = s * nl.Y, C = -s * Vec2.Dot(a, nl) });
                        }
                        _planes.Add(new Plane { A = 0, B = 0, C = r });
                        roof = RoofShape.Hipped;
                        return r;
                    }
                    if (fill < 0.7f) break;
                    AddBoxPlanes(box, r / box.HalfV, r / box.HalfV, true);
                    roof = RoofShape.Hipped;
                    return r;
                case RoofShape.Gabled:
                case RoofShape.Gambrel:
                case RoofShape.Round:
                    if (fill < 0.6f) break;
                    AddBoxPlanes(box, r / box.HalfV, 0, false);
                    roof = RoofShape.Gabled;
                    return r;
                case RoofShape.Pyramidal:
                case RoofShape.Dome:
                case RoofShape.Onion:
                    if (fill < 0.6f) break;
                    AddBoxPlanes(box, r / box.HalfV, r / box.HalfU, true);
                    roof = RoofShape.Pyramidal;
                    return r;
                case RoofShape.Skillion:
                    if (fill < 0.6f) break;
                    {
                        float k = r / (2 * box.HalfV);
                        // sale da -V (gronda bassa) a +V
                        _planes.Add(PlaneOnBox(box, box.V, k, k * box.HalfV));
                        roof = RoofShape.Skillion;
                        return r;
                    }
            }
            roof = RoofShape.Flat;
            _planes.Clear();
            return 0;
        }

        /// <summary>Piani sui lati del rettangolo orientato: pendenza sv sui lati lunghi, su sulle testate.</summary>
        private void AddBoxPlanes(OrientedBox box, float sv, float su, bool ends)
        {
            // h = sv * (HalfV - |dv|) → due piani, e analogamente per le testate
            _planes.Add(PlaneOnBox(box, box.V, -sv, sv * box.HalfV));
            _planes.Add(PlaneOnBox(box, box.V, sv, sv * box.HalfV));
            if (ends)
            {
                _planes.Add(PlaneOnBox(box, box.U, -su, su * box.HalfU));
                _planes.Add(PlaneOnBox(box, box.U, su, su * box.HalfU));
            }
        }

        /// <summary>Piano h = k * ((p - centro) · asse) + k0.</summary>
        private static Plane PlaneOnBox(OrientedBox box, Vec2 axis, float k, float k0) =>
            new Plane { A = k * axis.X, B = k * axis.Y, C = k0 - k * Vec2.Dot(box.Center, axis) };

        private float RoofAt(Vec2 p)
        {
            if (_planes.Count == 0) return 0;
            float m = float.MaxValue;
            for (int i = 0; i < _planes.Count; i++)
            {
                float v = _planes[i].At(p);
                if (v < m) m = v;
            }
            return m < 0 ? 0 : m;
        }

        // ------------------------------------------------------------------ tribune

        private readonly List<(Vec2 a, Vec2 b, int side)> _standEdges = new List<(Vec2, Vec2, int)>(64);

        /// <summary>
        /// Tribuna come gradinata: i lati rivolti verso il campo sono il fronte basso (parapetto), quelli opposti il retro
        /// alto; in mezzo la quota cresce con la distanza dal fronte. Sopra le file di sedili (tipo 18), attorno muri in cemento.
        /// </summary>
        private bool BuildStand(BuildingInfo b, List<Vec2> outer, List<List<Vec2>> holes, float gMin, float gRef, float height, Vec2 field, MeshBuffer mb)
        {
            _standEdges.Clear();
            int fronts = 0, backs = 0;
            void Classify(List<Vec2> ring)
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var c = ring[(i + 1) % ring.Count];
                    var d = c - a;
                    float len = d.Length;
                    if (len < 0.05f) continue;
                    // normale verso l'esterno dell'edificio (anello esterno antiorario, buchi orari)
                    var n = new Vec2(d.Y / len, -d.X / len);
                    var toField = field - (a + c) * 0.5f;
                    float tl = toField.Length;
                    float facing = tl < 1e-3f ? 0 : (n.X * toField.X + n.Y * toField.Y) / tl;
                    // fronte e retro solo se guardano chiaramente verso il campo o via (entro ~45°): i lati corti sono fianchi
                    int side = facing > 0.7f ? 0 : (facing < -0.7f ? 1 : 2);
                    if (side == 0) fronts++;
                    if (side == 1) backs++;
                    _standEdges.Add((a, c, side));
                }
            }
            Classify(outer);
            if (holes != null)
                foreach (var h in holes) Classify(h);
            if (fronts == 0 || backs == 0) return false;
            // una tribuna corre lungo il campo: fronte e retro più lunghi dei fianchi (non un edificio messo "di punta")
            float frontBack = 0, sides = 0;
            foreach (var e in _standEdges)
            {
                float l = (e.b - e.a).Length;
                if (e.side == 2) sides += l;
                else frontBack += l;
            }
            if (frontBack < sides) return false;
            if (!Triangulator.Triangulate(outer, holes, _triVerts, _tris)) return false;

            // profondità della tribuna (distanza massima dal fronte): la gradinata non supera i ~35° di una vera
            float depth = 0;
            foreach (var p in _triVerts) depth = Math.Max(depth, FrontDistance(p));
            float zFront = gRef + 1.1f;
            float zBack = Math.Min(gRef + Math.Max(4f, height), zFront + Math.Max(2.5f, depth * 0.7f));
            // la gradinata sale con pendenza costante dal fronte (~35°) fino alla cima: dove la tribuna è stretta resta
            // bassa, dove è profonda in cima c'è un piano (passeggiata)
            float Z(Vec2 p) => zFront + Math.Min(zBack - zFront, FrontDistance(p) * 0.7f);

            float seed = StylePalette.Random01(b.OsmId, 5);
            uint seats = MeshBuffer.Rgb(StylePalette.SeatColor(b.OsmId));
            uint concrete = MeshBuffer.Rgb(0xC6C2BA);
            // gradinata: vertici condivisi con la normale media dei triangoli attorno (niente faccette scure)
            int count = _triVerts.Count;
            var z = new float[count];
            var nrm = new float[count * 3];
            for (int i = 0; i < count; i++) z[i] = Z(_triVerts[i]);
            for (int t = 0; t < _tris.Count; t += 3)
            {
                int a = _tris[t], b2 = _tris[t + 1], c = _tris[t + 2];
                var pa = _triVerts[a];
                var pb = _triVerts[b2];
                var pc = _triVerts[c];
                float ux = pb.X - pa.X, uy = pb.Y - pa.Y, uz = z[b2] - z[a];
                float vx = pc.X - pa.X, vy = pc.Y - pa.Y, vz = z[c] - z[a];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; }
                // pesata con l'area del triangolo (il prodotto vettoriale non normalizzato)
                foreach (int k in new[] { a, b2, c })
                {
                    nrm[3 * k] += nx;
                    nrm[3 * k + 1] += ny;
                    nrm[3 * k + 2] += nz;
                }
            }
            int start = mb.VertexCount;
            for (int i = 0; i < count; i++)
            {
                var p = _triVerts[i];
                float nx = nrm[3 * i], ny = nrm[3 * i + 1], nz = nrm[3 * i + 2];
                float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-6f) { nx = 0; ny = 0; nz = 1; nl = 1; }
                mb.Add(p.X, p.Y, z[i], nx / nl, ny / nl, nz / nl, seats, p.X, p.Y, 0, 0, BuildingDecorator.KindSeats, seed, SeatRowHeight, 0);
            }
            for (int t = 0; t < _tris.Count; t += 3) mb.Triangle(start + _tris[t], start + _tris[t + 1], start + _tris[t + 2]);
            // muri dal terreno fino alla gradinata (retro alto, parapetto basso sul fronte), senza finestre
            float z0 = gMin - 0.6f;
            foreach (var e in _standEdges)
            {
                var d = e.b - e.a;
                float len = d.Length;
                var n = new Vec2(d.Y / len, -d.X / len);
                float ha = Z(e.a) - z0, hb = Z(e.b) - z0;
                int i0 = mb.Add(e.a.X, e.a.Y, z0, n.X, n.Y, 0, concrete, 0, 0, len, ha, 0, seed, 1, 12);
                int i1 = mb.Add(e.b.X, e.b.Y, z0, n.X, n.Y, 0, concrete, len, 0, len, hb, 0, seed, 1, 12);
                int i2 = mb.Add(e.b.X, e.b.Y, z0 + hb, n.X, n.Y, 0, concrete, len, hb, len, hb, 0, seed, 1, 12);
                int i3 = mb.Add(e.a.X, e.a.Y, z0 + ha, n.X, n.Y, 0, concrete, 0, ha, len, ha, 0, seed, 1, 12);
                mb.Quad(i0, i1, i2, i3);
            }
            LastOuter = outer;
            LastGroundRef = gRef;
            LastWallHeight = zBack - gRef;
            LastLevels = 1;
            LastCategory = Category.Industrial;
            LastShops = false;
            LastFlatRoof = false;
            LastPitched = false;
            LastWallColor = 0xC6C2BA;
            LastRoofColor = StylePalette.SeatColor(b.OsmId);
            LastRoofHeight = 0;
            LastRoof = RoofShape.Flat;
            LastEave = zBack;
            LastBase = z0;
            _planes.Clear();
            return true;
        }

        /// <summary>Distanza dal fronte della tribuna (lati rivolti verso il campo).</summary>
        private float FrontDistance(Vec2 p)
        {
            float d = float.MaxValue;
            foreach (var e in _standEdges)
                if (e.side == 0) d = Math.Min(d, SegmentDistance(p, e.a, e.b));
            return d;
        }

        private static float SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            float l2 = ab.X * ab.X + ab.Y * ab.Y;
            float t = l2 < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2));
            var q = a + ab * t;
            return (p - q).Length;
        }

        private void AddRoof(List<Vec2> outer, List<List<Vec2>> holes, MeshBuffer mb, float eave, uint color, float seed, int levels, bool tiledFlat)
        {
            if (!Triangulator.Triangulate(outer, holes, _triVerts, _tris)) return;
            if (_planes.Count == 0)
            {
                int start = mb.VertexCount;
                float kind = tiledFlat ? 1 : 2;
                for (int i = 0; i < _triVerts.Count; i++)
                {
                    var p = _triVerts[i];
                    mb.Add(p.X, p.Y, eave, 0, 0, 1, color, p.X, p.Y, 0, 0, kind, seed, levels, 0);
                }
                for (int i = 0; i < _tris.Count; i += 3) mb.Triangle(start + _tris[i], start + _tris[i + 1], start + _tris[i + 2]);
                return;
            }
            for (int t = 0; t < _tris.Count; t += 3)
            {
                var a = _triVerts[_tris[t]];
                var b = _triVerts[_tris[t + 1]];
                var c = _triVerts[_tris[t + 2]];
                for (int i = 0; i < _planes.Count; i++)
                {
                    var pi = _planes[i];
                    _clip.Clear();
                    _clip.Add(a);
                    _clip.Add(b);
                    _clip.Add(c);
                    var poly = _clip;
                    for (int j = 0; j < _planes.Count && poly.Count >= 3; j++)
                    {
                        if (j == i) continue;
                        var pj = _planes[j];
                        // regione in cui il piano i è il più basso: hi - hj ≤ 0
                        poly = Polygon2D.ClipHalfPlane(poly, new Vec2(pi.A - pj.A, pi.B - pj.B), pi.C - pj.C);
                    }
                    if (poly.Count < 3 || Math.Abs(Polygon2D.SignedArea(poly)) < 1e-4f) continue;
                    // normale della falda e direzioni per i coppi
                    float nx = -pi.A, ny = -pi.B, nz = 1;
                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx /= len;
                    ny /= len;
                    nz /= len;
                    var grad = new Vec2(pi.A, pi.B);
                    var down = grad.Length > 1e-5f ? grad.Normalized : new Vec2(0, 1);
                    var along = new Vec2(-down.Y, down.X);
                    float stretch = (float)Math.Sqrt(1 + pi.A * pi.A + pi.B * pi.B);
                    int start = mb.VertexCount;
                    for (int k = 0; k < poly.Count; k++)
                    {
                        var p = poly[k];
                        float h = Math.Max(0, pi.At(p));
                        mb.Add(p.X, p.Y, eave + h, nx, ny, nz, color,
                            Vec2.Dot(p, along), Vec2.Dot(p, down) * stretch, 0, 0, 1, seed, levels, 0);
                    }
                    for (int k = 1; k + 1 < poly.Count; k++) mb.Triangle(start, start + k, start + k + 1);
                }
            }
        }

        // ------------------------------------------------------------------ muri

        private void AddWalls(List<Vec2> ring, MeshBuffer mb, float baseZ, float eave, float gRef, float wallH, uint color,
            float seed, int levels, float category, bool simple)
        {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % n];
                var d = q - p;
                float len = d.Length;
                if (len < 0.05f) continue;
                var nrm = new Vec2(d.Y / len, -d.X / len); // a destra = esterno

                _breaks.Clear();
                _breaks.Add(0);
                if (!simple && _planes.Count > 1)
                {
                    for (int a = 0; a < _planes.Count; a++)
                    for (int b = a + 1; b < _planes.Count; b++)
                    {
                        // piano a e piano b si incrociano lungo il lato in t
                        float fa0 = _planes[a].At(p), fa1 = _planes[a].At(q);
                        float fb0 = _planes[b].At(p), fb1 = _planes[b].At(q);
                        float den = (fa1 - fa0) - (fb1 - fb0);
                        if (Math.Abs(den) < 1e-6f) continue;
                        float t = (fb0 - fa0) / den;
                        if (t > 0.001f && t < 0.999f) _breaks.Add(t);
                    }
                    _breaks.Sort();
                }
                _breaks.Add(1);

                _column.Clear();
                float lastT = -1;
                for (int k = 0; k < _breaks.Count; k++)
                {
                    float t = _breaks[k];
                    if (t - lastT < 0.0005f) continue;
                    lastT = t;
                    var pt = Vec2.Lerp(p, q, t);
                    float top = eave + (simple ? 0 : RoofAt(pt));
                    float u = t * len;
                    int bottom = mb.Add(pt.X, pt.Y, baseZ, nrm.X, nrm.Y, 0, color,
                        u, baseZ - gRef, len, wallH, 0, seed, levels, category);
                    mb.Add(pt.X, pt.Y, top, nrm.X, nrm.Y, 0, color,
                        u, top - gRef, len, wallH, 0, seed, levels, category);
                    _column.Add(bottom);
                }
                for (int k = 0; k + 1 < _column.Count; k++)
                {
                    int b0 = _column[k], b1 = _column[k + 1];
                    mb.Quad(b0, b1, b1 + 1, b0 + 1);
                }
            }
        }

        private static bool IsConvex(List<Vec2> ring)
        {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % n];
                var c = ring[(i + 2) % n];
                if (Vec2.Cross(b - a, c - b) < -1e-4f) return false;
            }
            return true;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
