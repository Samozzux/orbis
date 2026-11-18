using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Game
{
    /// <summary>
    /// Modelli procedurali degli elementi del catalogo, per famiglia, fatti come quelli veri (docs/08 §3: ogni edificio
    /// del catalogo deve somigliare a un edificio reale del suo tipo, con le sue parti tipiche). Tutto resta dentro la
    /// sagoma esatta dell'elemento (docs/04 §4). Coordinate del tile (est, nord, quota); il fronte è il lato v = 0.
    /// Le piante (alberi, uliveti, vigneti…) escono come istanze da disegnare con i modelli delle piante.
    /// File divisi per gruppi: Town (case e negozi), Public (servizi e cultura), Industry (industria, campagna,
    /// energia), Leisure (sport, trasporti, arredo e natura).
    /// </summary>
    public sealed partial class ObjectMesher
    {
        private readonly BuildingMesher _building = new BuildingMesher();
        private readonly BuildingDecorator _decor = new BuildingDecorator();

        // telaio dell'oggetto: origine nell'angolo (0,0) della sagoma ruotata, assi U (larghezza) e V (profondità)
        private Vec2 _o, _u, _v;
        private float _w, _d;
        private GroundSampler _ground;
        private MeshBuffer _mb;
        // piano d'appoggio dell'elemento: quota più alta della sagoma (sui pendii niente parti sotto terra)
        private float _z0;
        private readonly HashSet<long> _plinths = new HashSet<long>();
        private List<PlantInstance> _plants;
        private long _seed;
        // livello 1…5 (gli addobbi crescono col livello)
        private int _level;
        // muri in comune (case a schiera): ascisse U dove due unità si toccano, lì niente addobbi
        private float[] _party;

        private static readonly uint[] CarColors = { 0xF2F2F0, 0xB8BCC0, 0x6D7278, 0x2A2D31, 0xB3261E, 0x1F4E8C, 0x2F5D3A, 0xE0D6C2, 0x8C1C2C };

        /// <summary>Costruisce il modello. <paramref name="corner"/> è l'angolo della sagoma, <paramref name="angle"/> la direzione di U.</summary>
        public void Build(CatalogItem item, int level, string variant, Vec2 corner, float angle, int rotation, GroundSampler ground,
            MeshBuffer mb, List<PlantInstance> plants, long seed)
        {
            bool swap = (rotation & 1) == 1;
            _w = swap ? item.Depth : item.Width;
            _d = swap ? item.Width : item.Depth;
            _u = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
            _v = new Vec2(-_u.Y, _u.X);
            _o = corner;
            _ground = ground;
            _mb = mb;
            _plants = plants;
            _seed = seed;
            _level = Math.Max(1, Math.Min(5, level));
            _party = null;
            _plinths.Clear();
            Range(0, 0, _w, _d, out _, out _z0);
            var v = item.Variant(variant);
            uint wall = v != null && v.WallColor != 0 ? v.WallColor : 0;
            uint roof = v != null && v.RoofColor != 0 ? v.RoofColor : 0;
            float lv = Math.Max(0, level - 1);
            switch (item.Family)
            {
                case "house": House(item, wall, roof, lv); break;
                case "shop": Shop(item, wall, roof, lv); break;
                case "civic": Civic(item, wall, roof); break;
                case "church": Church(item, wall); break;
                case "industrial": Industrial(item, wall, roof); break;
                case "farmhouse": Farmhouse(item, wall, roof); break;
                case "barn": Barn(item, wall); break;
                case "silo": Silos(item); break;
                case "greenhouse": Greenhouse(); break;
                case "wind": WindTurbine(_w / 2, _d / 2, item.Height, Math.Min(_w, _d) / 2); break;
                case "windfarm":
                    Ground(0, 0, _w, _d, 0x19CB46E, 0.05f, BuildingDecorator.KindPlants, 0.0f);
                    for (int k = 0; k < 4; k++) WindTurbine(_w * (0.25f + 0.5f * (k & 1)), _d * (0.25f + 0.5f * (k >> 1)), item.Height, Math.Min(_w, _d) / 4);
                    break;
                case "solar": SolarField(); break;
                case "solarbuilding": SolarCondo(item, wall); break;
                case "hydro": Hydro(item); break;
                case "thermal": Thermal(item); break;
                case "nuclear": Nuclear(item, lv); break;
                case "substation": Substation(); break;
                case "pylon": Pylon(item.Height); break;
                case "field": Field(item.Param("crop", "wheat")); break;
                case "pasture": Pasture(); break;
                case "vineyard": Vineyard(); break;
                case "olivegrove": Ground(0, 0, _w, _d, 0x1B2AC80, 0.06f, BuildingDecorator.KindPlants, 0.02f); Grid(PlantKind.Olive, 7f); break;
                case "orchard": Ground(0, 0, _w, _d, 0x196AC66, 0.06f, BuildingDecorator.KindPlants, 0.04f); Grid(PlantKind.Fruit, 5.5f); break;
                case "beehives": Beehives(); break;
                case "pitch": Pitch(item.Param("sport", "football")); break;
                case "pool": Pool(); break;
                case "hall": Hall(item, wall); break;
                case "track": Track(); break;
                case "stadium": Stadium(item, lv); break;
                case "amphitheatre": Amphitheatre(item); break;
                case "funfair": Funfair(item); break;
                case "parking": Parking(); break;
                case "parkinggarage": ParkingGarage(item); break;
                case "busstop": BusStop(); break;
                case "station": BusStation(item); break;
                case "path": Footpath(); break;
                case "paving": Piazza(); break;
                case "helipad": Helipad(); break;
                case "tree": Plant(PlantKindOf(item.Param("kind", "broadleaf")), _w / 2, _d / 2, item.Height); break;
                case "grove": Ground(0, 0, _w, _d, 0x18BB669, 0.05f, BuildingDecorator.KindPlants, 0.05f); Scatter(PlantKind.Broadleaf, 5f); break;
                case "hedge": Hedge(item.Height); break;
                case "flowerbed": Flowerbed(item.Param("color", "mixed")); break;
                case "lawn": Lawn(0, 0, _w, _d); break;
                case "shrubs": Scatter(PlantKind.Shrub, 1.4f); break;
                case "pond": Pond(); break;
                case "bench": Bench(0, 0, _w, _d); break;
                case "lamp": StreetLamp(_w / 2, _d / 2, item.Height, item.Param("style", "modern") == "historic"); break;
                case "fountain": Fountain(item.Height, item.Param("size", "small") == "large"); break;
                case "statue": Statue(item.Height, item.Param("size", "small") == "large"); break;
                case "planter": Planter(); break;
                case "bin": Bin(_w / 2, _d / 2); break;
                case "gazebo": Gazebo(item.Height); break;
                case "kiosk": Kiosk(item.Height); break;
                case "playground": Playground(); break;
                case "terrace": Terrace(); break;
                case "flag": FlagPole(0.25f, _d / 2, item.Height, Math.Min(_w - 0.4f, 1.8f)); break;
                case "bikerack": BikeRack(); break;
                default:
                    {
                        float g = Base(0, 0, _w, _d);
                        Box(0, 0, _w, _d, g, g + item.Height, wall != 0 ? wall : 0x1D0C8B8u, 0);
                        break;
                    }
            }
        }

        public static PlantKind PlantKindOf(string kind)
        {
            switch (kind)
            {
                case "olive": return PlantKind.Olive;
                case "stonepine": return PlantKind.StonePine;
                case "cypress": return PlantKind.Cypress;
                case "palm": return PlantKind.Palm;
                case "conifer": return PlantKind.Conifer;
                case "fruit": return PlantKind.Fruit;
                case "shrub": return PlantKind.Shrub;
                default: return PlantKind.Broadleaf;
            }
        }

        private static int ParseInt(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;

        // ------------------------------------------------------------------ piante

        private void Plant(PlantKind kind, float u, float v, float height)
        {
            var p = World(u, v);
            float scale = height / VegetationScatter.BaseHeight(kind);
            _plants?.Add(new PlantInstance { Kind = kind, X = p.X, Y = p.Y, Z = _ground(p.X, p.Y), Scale = scale, Rotation = (_seed % 628) / 100f + u * 0.7f });
        }

        /// <summary>Albero di altezza tipica (con un po' di varietà) nel punto.</summary>
        private void Tree(PlantKind kind, float u, float v, int salt) =>
            Plant(kind, u, v, VegetationScatter.BaseHeight(kind) * (0.8f + 0.35f * Frac(salt * 13 + 5)));

        private void Grid(PlantKind kind, float gap)
        {
            int k = 0;
            for (float u = gap / 2; u <= _w - gap / 2 + 0.01f; u += gap)
                for (float v = gap / 2; v <= _d - gap / 2 + 0.01f; v += gap)
                    Plant(kind, u, v, VegetationScatter.BaseHeight(kind) * (0.85f + 0.3f * Frac(++k)));
        }

        private void Scatter(PlantKind kind, float gap)
        {
            int k = 0;
            for (float u = gap / 2; u < _w; u += gap)
                for (float v = gap / 2; v < _d; v += gap)
                {
                    k++;
                    float du = (Frac(k * 3) - 0.5f) * gap * 0.5f, dv = (Frac(k * 7) - 0.5f) * gap * 0.5f;
                    Plant(kind, Math.Max(0.5f, Math.Min(_w - 0.5f, u + du)), Math.Max(0.5f, Math.Min(_d - 0.5f, v + dv)),
                        VegetationScatter.BaseHeight(kind) * (0.8f + 0.4f * Frac(k * 11)));
                }
        }

        /// <summary>Filari lungo U, distanziati di <paramref name="gap"/> lungo V.</summary>
        private void Rows(PlantKind kind, float gap)
        {
            float rot = (float)Math.Atan2(_u.Y, _u.X);
            for (float v = gap / 2; v <= _d - gap / 2 + 0.01f; v += gap)
            {
                for (float u0 = 0.5f; u0 < _w - 0.5f; u0 += 10f)
                {
                    float seg = Math.Min(10f, _w - 0.5f - u0);
                    var p = World(u0 + seg / 2, v);
                    _plants?.Add(new PlantInstance { Kind = kind, X = p.X, Y = p.Y, Z = _ground(p.X, p.Y), Scale = 1, Rotation = rot, Length = seg });
                }
            }
        }

        private float Frac(long k)
        {
            unchecked
            {
                uint h = (uint)(k * 2654435761u ^ _seed * 40503u);
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private uint Pick(uint[] list, long salt) => list[(int)(Frac(salt) * list.Length) % list.Length];

        // ------------------------------------------------------------------ terreno e quote (coordinate dell'oggetto)

        private Vec2 World(float u, float v) => _o + _u * u + _v * v;

        private float Ground(float u, float v)
        {
            var p = World(u, v);
            return _ground(p.X, p.Y);
        }

        /// <summary>Quota minima e massima del terreno sul rettangolo (angoli, centri dei lati e centro).</summary>
        private void Range(float u0, float v0, float u1, float v1, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;
            for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++)
            {
                float g = Ground(u0 + (u1 - u0) * i / 2, v0 + (v1 - v0) * j / 2);
                if (g < lo) lo = g;
                if (g > hi) hi = g;
            }
        }

        /// <summary>
        /// Piano d'appoggio di un volume: la quota più alta del rettangolo, così sui pendii niente entra nel terreno;
        /// sotto, verso valle, un basamento in pietra fino al terreno (come gli edifici in collina).
        /// </summary>
        private float Base(float u0, float v0, float u1, float v1)
        {
            Range(u0, v0, u1, v1, out var lo, out var hi);
            if (hi - lo > 0.12f)
            {
                long key = ((long)Math.Round(u0 * 20) * 73856093L) ^ ((long)Math.Round(v0 * 20) * 19349663L) ^ ((long)Math.Round(u1 * 20) * 83492791L) ^ (long)Math.Round(v1 * 20);
                if (_plinths.Add(key)) Box(u0, v0, u1, v1, lo - 0.3f, hi + 0.02f, 0x1B5AA96, 6 * 2);
            }
            return hi;
        }

        /// <summary>Quota media del rettangolo (oggetti bassi e lunghi che seguono il terreno: filari, pannelli a terra).</summary>
        private float Mid(float u0, float v0, float u1, float v1)
        {
            Range(u0, v0, u1, v1, out var lo, out var hi);
            return (lo + hi) / 2;
        }

        private static RoofShape ParseRoof(string s)
        {
            switch (s)
            {
                case "gabled": return RoofShape.Gabled;
                case "flat": return RoofShape.Flat;
                case "pyramidal": return RoofShape.Pyramidal;
                default: return RoofShape.Hipped;
            }
        }

        // ------------------------------------------------------------------ edifici veri (muri con finestre, tetti, addobbi)

        /// <summary>Edificio procedurale (muri con finestre, tetto) sul rettangolo [u0,u1]×[v0,v1], con eventuale cortile.</summary>
        private BuildingInfo Building(float u0, float v0, float u1, float v1, float height, RoofShape roof, uint wall, uint roofColor, int levels,
            BuildingUse use, bool shops = false, float minZ = float.NaN, float[] courtyard = null, WallMaterial material = WallMaterial.Unknown)
        {
            var p0 = World(u0, v0);
            var p1 = World(u1, v0);
            var p2 = World(u1, v1);
            var p3 = World(u0, v1);
            var outer = new[] { p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y };
            if (Polygon2D.SignedArea(outer) < 0) outer = new[] { p0.X, p0.Y, p3.X, p3.Y, p2.X, p2.Y, p1.X, p1.Y };
            var holes = Array.Empty<float[]>();
            if (courtyard != null)
            {
                // cortile [cu0, cv0, cu1, cv1]: anello orario
                var c0 = World(courtyard[0], courtyard[1]);
                var c1 = World(courtyard[2], courtyard[1]);
                var c2 = World(courtyard[2], courtyard[3]);
                var c3 = World(courtyard[0], courtyard[3]);
                var hole = new[] { c0.X, c0.Y, c3.X, c3.Y, c2.X, c2.Y, c1.X, c1.Y };
                if (Polygon2D.SignedArea(hole) > 0) hole = new[] { c0.X, c0.Y, c1.X, c1.Y, c2.X, c2.Y, c3.X, c3.Y };
                holes = new[] { hole };
            }
            var b = new BuildingInfo
            {
                OsmId = _seed * 31 + (long)(u0 * 7 + v0 * 13), Outer = outer, Holes = holes, Height = height,
                Roof = roof, Levels = levels, Use = use, WallColor = wall, RoofColor = roofColor, Bounds = Bounds2.Of(outer),
                Flags = shops ? BuildingFlag.HasShops : 0, Material = material,
            };
            // in piano alla quota più alta della sagoma (basamento verso valle), come gli altri volumi
            float top = Base(u0, v0, u1, v1);
            GroundSampler level = (x, y) => top;
            if (!float.IsNaN(minZ))
            {
                // volume sospeso (tetto di gazebo, torre sopra un podio): base fissa sopra il piano
                b.MinHeight = Math.Max(0.2f, minZ - top);
                b.Height = b.MinHeight + height;
            }
            _building.Build(b, level, _mb, shops);
            return b;
        }

        /// <summary>
        /// Addobbi dell'ultimo edificio costruito (tende, insegne, dehors, balconi, gronde, comignoli, impianti sul tetto),
        /// gli stessi degli edifici veri della città. L'ingresso è sul lato v = 0 (o nel punto indicato).
        /// </summary>
        private void Dress(BuildingInfo b, DressKind kind, float entranceU = float.NaN)
        {
            if (kind == DressKind.None) return;
            _decor.Lot = LotSpace;
            var entrance = World(float.IsNaN(entranceU) ? _w / 2 : entranceU, -2f);
            _decor.Decorate(b, _building, kind, _level, entrance, null, _ground, _mb, _plants);
        }

        /// <summary>Spazio libero davanti a un lato dell'edificio fino al bordo della sagoma; il fronte su strada è il lato v = 0.</summary>
        private float LotSpace(Vec2 mid, Vec2 normal, out bool street)
        {
            var r = mid - _o;
            float u = Vec2.Dot(r, _u), v = Vec2.Dot(r, _v);
            float nu = Vec2.Dot(normal, _u), nv = Vec2.Dot(normal, _v);
            street = nv < -0.7f;
            if (_party != null && Math.Abs(nu) > 0.7f)
                foreach (float pu in _party)
                    if (Math.Abs(u - pu) < 0.05f) return 0;
            if (nv < -0.7f) return v;
            if (nv > 0.7f) return _d - v;
            if (nu < -0.7f) return u;
            if (nu > 0.7f) return _w - u;
            return 0.4f;
        }

        /// <summary>Quote dei piani dell'ultimo edificio (allineate alle finestre dello shader).</summary>
        private float FloorZ(int floor) => _building.LastGroundRef + floor * BuildingMesher.FloorHeight(_building.LastWallHeight, _building.LastLevels);

        private float FloorH => BuildingMesher.FloorHeight(_building.LastWallHeight, _building.LastLevels);

        /// <summary>
        /// Muro con le finestre dello shader da a a b (normale a destra di a→b, cioè verso fuori per i lati presi in senso
        /// antiorario). cat = categoria*2 (+1 con le vetrine): 0 case, 2 uffici, 4 industria, 6 chiese, 8 rurale,
        /// 10 vetrate continue, 12 cieco. <paramref name="floorZ"/> = quota del piano terra.
        /// </summary>
        private void Wall(float ua, float va, float ub, float vb, float z0, float z1, float floorZ, uint color, float cat, int levels)
        {
            var a = World(ua, va);
            var b = World(ub, vb);
            var d = b - a;
            float len = d.Length;
            if (len < 1e-3f) return;
            var n = new Vec2(d.Y / len, -d.X / len);
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            float wallH = Math.Max(z1 - floorZ, 0.5f);
            float seed = 0.13f + 0.7f * Frac((long)(ua * 17 + va * 31));
            int i0 = _mb.Add(a.X, a.Y, z0, n.X, n.Y, 0, c, 0, z0 - floorZ, len, wallH, 0, seed, levels, cat);
            int i1 = _mb.Add(b.X, b.Y, z0, n.X, n.Y, 0, c, len, z0 - floorZ, len, wallH, 0, seed, levels, cat);
            int i2 = _mb.Add(b.X, b.Y, z1, n.X, n.Y, 0, c, len, z1 - floorZ, len, wallH, 0, seed, levels, cat);
            int i3 = _mb.Add(a.X, a.Y, z1, n.X, n.Y, 0, c, 0, z1 - floorZ, len, wallH, 0, seed, levels, cat);
            _mb.Quad(i0, i1, i2, i3);
        }

        /// <summary>
        /// Volume a tetto piano con una categoria di facciata per lato (sud, est, nord, ovest): capannoni, supermercati,
        /// cinema. Restituisce la quota del piano terra.
        /// </summary>
        private float Block(float u0, float v0, float u1, float v1, float height, uint color, float catS, float catE, float catN, float catW,
            int levels, uint roofColor = 0x1A9A69E, bool parapet = true)
        {
            float g = Base(u0, v0, u1, v1);
            float top = g + height;
            Wall(u0, v0, u1, v0, g - 0.3f, top, g, color, catS, levels);
            Wall(u1, v0, u1, v1, g - 0.3f, top, g, color, catE, levels);
            Wall(u1, v1, u0, v1, g - 0.3f, top, g, color, catN, levels);
            Wall(u0, v1, u0, v0, g - 0.3f, top, g, color, catW, levels);
            Flat(u0, v0, u1, v1, top, roofColor, 2);
            if (parapet) Parapet(u0, v0, u1, v1, top, 0.9f, Lighten(color, 0.08f));
            return g;
        }

        /// <summary>Parapetto sul bordo di un tetto piano (faccia esterna, cima, faccia interna).</summary>
        private void Parapet(float u0, float v0, float u1, float v1, float z, float h, uint color)
        {
            const float t = 0.22f;
            PaintedBox(u0, v0, u1, v0 + t, z, z + h, color);
            PaintedBox(u0, v1 - t, u1, v1, z, z + h, color);
            PaintedBox(u0, v0 + t, u0 + t, v1 - t, z, z + h, color);
            PaintedBox(u1 - t, v0 + t, u1, v1 - t, z, z + h, color);
        }

        /// <summary>Vetrata applicata davanti a un muro (ingressi, foyer): una lastra con i montanti dello shader.</summary>
        private void Glazing(float ua, float va, float ub, float vb, float z0, float z1, int levels, uint frame = 0x1595F66)
        {
            var a = World(ua, va);
            var b = World(ub, vb);
            var d = b - a;
            float len = d.Length;
            if (len < 1e-3f) return;
            var n = new Vec2(d.Y / len, -d.X / len) * 0.05f;
            float u0 = Vec2.Dot(a + n - _o, _u), v0 = Vec2.Dot(a + n - _o, _v);
            float u1 = Vec2.Dot(b + n - _o, _u), v1 = Vec2.Dot(b + n - _o, _v);
            Wall(u0, v0, u1, v1, z0, z1, z0, frame, 10, levels);
        }

        /// <summary>Insegna sul lato a→b (rivolta a destra), staccata di 6 cm dal muro: fondo colorato con le "lettere" accese la sera.</summary>
        private void Sign(float ua, float va, float ub, float vb, float z0, float z1, uint color, bool text = true)
        {
            var a = World(ua, va);
            var b = World(ub, vb);
            var d = b - a;
            float len = d.Length;
            if (len < 1e-3f) return;
            var dir = d * (1f / len);
            var n = new Vec2(dir.Y, -dir.X);
            var a1 = a + n * 0.06f;
            var b1 = b + n * 0.06f;
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            float h = z1 - z0, k = BuildingDecorator.KindSign, p = text ? 1 : 0;
            int i0 = _mb.Add(a1.X, a1.Y, z0, n.X, n.Y, 0, c, 0, 0, len, h, k, 0.3f + 0.5f * Frac((long)(ua * 11)), p, 0);
            int i1 = _mb.Add(b1.X, b1.Y, z0, n.X, n.Y, 0, c, len, 0, len, h, k, 0.3f, p, 0);
            int i2 = _mb.Add(b1.X, b1.Y, z1, n.X, n.Y, 0, c, len, h, len, h, k, 0.3f, p, 0);
            int i3 = _mb.Add(a1.X, a1.Y, z1, n.X, n.Y, 0, c, 0, h, len, h, k, 0.3f, p, 0);
            _mb.Quad(i0, i1, i2, i3);
            // spessore del pannello: bordo scuro in cima e ai lati
            var a2 = a + n * 0.01f;
            var b2 = b + n * 0.01f;
            QuadW(new Vec3(a2.X, a2.Y, z1), new Vec3(b2.X, b2.Y, z1), new Vec3(b1.X, b1.Y, z1), new Vec3(a1.X, a1.Y, z1), new Vec3(0, 0, 1),
                MeshBuffer.Rgb(0x2B2B2B), BuildingDecorator.KindProp, 0);
        }

        // ------------------------------------------------------------------ primitive (coordinate dell'oggetto)

        /// <summary>Parallelepipedo tra le quote assolute z0 e z1. kind = categoria*2 per i muri (12 = senza finestre).</summary>
        private void Box(float u0, float v0, float u1, float v1, float z0, float z1, uint color, int kind)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var p = new[] { World(u0, v0), World(u1, v0), World(u1, v1), World(u0, v1) };
            if (Polygon2D.SignedArea(new List<Vec2>(p)) < 0) Array.Reverse(p);
            float cat = kind == 0 ? 12 : kind;
            for (int i = 0; i < 4; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % 4];
                var e = b - a;
                float len = e.Length;
                if (len < 1e-4f) continue;
                var n = new Vec2(e.Y / len, -e.X / len);
                int i0 = _mb.Add(a.X, a.Y, z0, n.X, n.Y, 0, c, 0, 0, len, z1 - z0, 0, 0.5f, 1, cat);
                int i1 = _mb.Add(b.X, b.Y, z0, n.X, n.Y, 0, c, len, 0, len, z1 - z0, 0, 0.5f, 1, cat);
                int i2 = _mb.Add(b.X, b.Y, z1, n.X, n.Y, 0, c, len, z1 - z0, len, z1 - z0, 0, 0.5f, 1, cat);
                int i3 = _mb.Add(a.X, a.Y, z1, n.X, n.Y, 0, c, 0, z1 - z0, len, z1 - z0, 0, 0.5f, 1, cat);
                _mb.Quad(i0, i1, i2, i3);
            }
            Flat4(p, z1, c, 2);
        }

        private void Flat4(Vec2[] p, float z, uint c, float kind)
        {
            int t0 = _mb.Add(p[0].X, p[0].Y, z, 0, 0, 1, c, p[0].X, p[0].Y, 0, 0, kind, 0.5f, 1, 0);
            int t1 = _mb.Add(p[1].X, p[1].Y, z, 0, 0, 1, c, p[1].X, p[1].Y, 0, 0, kind, 0.5f, 1, 0);
            int t2 = _mb.Add(p[2].X, p[2].Y, z, 0, 0, 1, c, p[2].X, p[2].Y, 0, 0, kind, 0.5f, 1, 0);
            int t3 = _mb.Add(p[3].X, p[3].Y, z, 0, 0, 1, c, p[3].X, p[3].Y, 0, 0, kind, 0.5f, 1, 0);
            _mb.Quad(t0, t1, t2, t3);
        }

        /// <summary>
        /// Superficie orizzontale piana; kind 8 = campo con linee (uv0 in metri del rettangolo, param = sport),
        /// 9 = parcheggio con gli stalli, 10 = pavimentazione, 11 = acqua, 13 = tinta piena, 17 = prato.
        /// </summary>
        private void Flat(float u0, float v0, float u1, float v1, float z, uint color, float kind, float param = 1)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var corners = new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };
            var idx = new int[4];
            var pts = new Vec2[4];
            for (int i = 0; i < 4; i++) pts[i] = World(corners[i].Item1, corners[i].Item2);
            bool ccw = Polygon2D.SignedArea(new List<Vec2>(pts)) > 0;
            for (int i = 0; i < 4; i++)
            {
                var (u, v) = corners[i];
                idx[i] = _mb.Add(pts[i].X, pts[i].Y, z, 0, 0, 1, c, u - u0, v - v0, u1 - u0, v1 - v0, kind, 0.5f, param, 0);
            }
            if (ccw) _mb.Quad(idx[0], idx[1], idx[2], idx[3]);
            else _mb.Quad(idx[0], idx[3], idx[2], idx[1]);
        }

        /// <summary>Copertura del terreno che segue le pendenze (griglia da ~4 m), poco sopra il suolo.</summary>
        private void Ground(float u0, float v0, float u1, float v1, uint color, float lift, float kind = 2, float param = 1)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            int nu = Math.Max(1, (int)Math.Ceiling((u1 - u0) / 4f)), nv = Math.Max(1, (int)Math.Ceiling((v1 - v0) / 4f));
            int start = _mb.VertexCount;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float u = u0 + (u1 - u0) * i / nu, v = v0 + (v1 - v0) * j / nv;
                    var p = World(u, v);
                    _mb.Add(p.X, p.Y, _ground(p.X, p.Y) + lift, 0, 0, 1, c, u - u0, v - v0, u1 - u0, v1 - v0, kind, 0.5f, param, 0);
                }
            bool ccw = Vec2.Cross(_u, _v) > 0;
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = start + j * (nu + 1) + i, b = a + 1, d = a + nu + 1, e = d + 1;
                    if (ccw) _mb.Quad(a, b, e, d);
                    else _mb.Quad(a, d, e, b);
                }
        }

        /// <summary>Prato (fogliame fine dello shader, qualche fiore).</summary>
        private void Lawn(float u0, float v0, float u1, float v1, float lift = 0.05f) =>
            Ground(u0, v0, u1, v1, 0x186A85A, lift, BuildingDecorator.KindPlants, 0.012f);

        /// <summary>Marciapiede o cortile in pietra.</summary>
        private void Paving(float u0, float v0, float u1, float v1, uint color = 0x1CFC6B4, float lift = 0.08f) =>
            Ground(u0, v0, u1, v1, color, lift, 10);

        /// <summary>Asfalto liscio (piazzali, strade interne).</summary>
        private void Asphalt(float u0, float v0, float u1, float v1, float lift = 0.07f) =>
            Ground(u0, v0, u1, v1, 0x1686B70, lift, BuildingDecorator.KindProp);

        /// <summary>Siepe potata tra due punti dell'oggetto (alta h, spessa t).</summary>
        private void HedgeRow(float u0, float v0, float u1, float v1, float h)
        {
            Range(u0, v0, u1, v1, out var lo, out var hi);
            Foliage(u0, v0, u1, v1, lo - 0.1f, (lo + hi) / 2 + h, 0x1477A3A, 0.0f);
        }

        /// <summary>Parallelepipedo di fogliame (siepi, aiuole, filari): tipo 17 dello shader, param = densità dei fiori.</summary>
        private void Foliage(float u0, float v0, float u1, float v1, float z0, float z1, uint color, float flowers)
        {
            float k = BuildingDecorator.KindPlants;
            Poly(new[] { u0, v0, z0, u1, v0, z0, u1, v0, z1, u0, v0, z1 }, 0, -1, 0, color, k, flowers);
            Poly(new[] { u1, v0, z0, u1, v1, z0, u1, v1, z1, u1, v0, z1 }, 1, 0, 0, color, k, flowers);
            Poly(new[] { u1, v1, z0, u0, v1, z0, u0, v1, z1, u1, v1, z1 }, 0, 1, 0, color, k, flowers);
            Poly(new[] { u0, v1, z0, u0, v0, z0, u0, v0, z1, u0, v1, z1 }, -1, 0, 0, color, k, flowers);
            Poly(new[] { u0, v0, z1, u1, v0, z1, u1, v1, z1, u0, v1, z1 }, 0, 0, 1, Lighten(color, 0.06f), k, flowers);
        }

        private void Cylinder(float cu, float cv, float r, float z0, float z1, int sides, uint color, float bottomScale = 1f, float topScale = 1f,
            float kind = 0, float cat = 12)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            int tc = _mb.Add(center.X, center.Y, z1, 0, 0, 1, c, 0, 0, 0, 0, kind < 0.5f ? 2 : kind, 0.5f, 1, 0);
            float slope = (r * bottomScale - r * topScale) / Math.Max(0.01f, z1 - z0);
            float nl = (float)Math.Sqrt(1 + slope * slope);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                var d0 = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
                var d1 = new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1));
                var nm = new Vec2((float)Math.Cos(am), (float)Math.Sin(am));
                var b0 = center + d0 * (r * bottomScale);
                var b1 = center + d1 * (r * bottomScale);
                var t0 = center + d0 * (r * topScale);
                var t1 = center + d1 * (r * topScale);
                float len = (b1 - b0).Length;
                float nx = nm.X / nl, ny = nm.Y / nl, nz = slope / nl;
                int i0 = _mb.Add(b0.X, b0.Y, z0, nx, ny, nz, c, i * len, 0, len * sides, z1 - z0, kind, 0.5f, 1, cat);
                int i1 = _mb.Add(b1.X, b1.Y, z0, nx, ny, nz, c, (i + 1) * len, 0, len * sides, z1 - z0, kind, 0.5f, 1, cat);
                int i2 = _mb.Add(t1.X, t1.Y, z1, nx, ny, nz, c, (i + 1) * len, z1 - z0, len * sides, z1 - z0, kind, 0.5f, 1, cat);
                int i3 = _mb.Add(t0.X, t0.Y, z1, nx, ny, nz, c, i * len, z1 - z0, len * sides, z1 - z0, kind, 0.5f, 1, cat);
                _mb.Quad(i0, i1, i2, i3);
                int ta = _mb.Add(t0.X, t0.Y, z1, 0, 0, 1, c, 0, 0, 0, 0, kind < 0.5f ? 2 : kind, 0.5f, 1, 0);
                int tb = _mb.Add(t1.X, t1.Y, z1, 0, 0, 1, c, 0, 0, 0, 0, kind < 0.5f ? 2 : kind, 0.5f, 1, 0);
                _mb.Triangle(tc, ta, tb);
            }
        }

        /// <summary>Cilindro verniciato (serbatoi, pali, tubi verticali): tinta piena senza finestre.</summary>
        private void Pipe(float cu, float cv, float r, float z0, float z1, uint color, int sides = 10, float topScale = 1f) =>
            Cylinder(cu, cv, r, z0, z1, sides, color, 1f, topScale, BuildingDecorator.KindProp, 0);

        /// <summary>Serbatoio verticale con il tetto a cono basso (silos, vasche del vino, cisterne).</summary>
        private void Tank(float cu, float cv, float r, float z0, float h, uint color, float roofH = -1)
        {
            Pipe(cu, cv, r, z0, z0 + h, color, 18);
            Cone(cu, cv, r * 1.02f, z0 + h, roofH < 0 ? r * 0.45f : roofH, Darken(color, 0.06f), BuildingDecorator.KindProp, 18);
        }

        /// <summary>
        /// Poligono (3 o 4 vertici: u, v, quota assoluta) orientato verso (ou, ov, oz) in coordinate dell'oggetto.
        /// kind 0 = muro (cat = categoria muro, 12 senza finestre); per gli altri tipi param va in uv1.z.
        /// </summary>
        private void Poly(float[] uvz, float ou, float ov, float oz, uint color, float kind, float param, float cat = 0)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            int n = uvz.Length / 3;
            var p = new (float x, float y, float z)[n];
            for (int i = 0; i < n; i++)
            {
                var w = World(uvz[3 * i], uvz[3 * i + 1]);
                p[i] = (w.X, w.Y, uvz[3 * i + 2]);
            }
            float ax = p[1].x - p[0].x, ay = p[1].y - p[0].y, az = p[1].z - p[0].z;
            float bx = p[2].x - p[0].x, by = p[2].y - p[0].y, bz = p[2].z - p[0].z;
            float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-6f) return;
            nx /= nl;
            ny /= nl;
            nz /= nl;
            // direzione desiderata nel mondo
            var o = _u * ou + _v * ov;
            if (nx * o.X + ny * o.Y + nz * oz < 0)
            {
                Array.Reverse(p);
                nx = -nx;
                ny = -ny;
                nz = -nz;
            }
            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var q in p)
            {
                zMin = Math.Min(zMin, q.z);
                zMax = Math.Max(zMax, q.z);
            }
            float total = 0;
            for (int i = 1; i < n; i++) total = Math.Max(total, (float)Math.Sqrt((p[i].x - p[0].x) * (p[i].x - p[0].x) + (p[i].y - p[0].y) * (p[i].y - p[0].y)));
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (kind < 0.5f)
                {
                    float along = (float)Math.Sqrt((p[i].x - p[0].x) * (p[i].x - p[0].x) + (p[i].y - p[0].y) * (p[i].y - p[0].y));
                    idx[i] = _mb.Add(p[i].x, p[i].y, p[i].z, nx, ny, nz, c, along, p[i].z - zMin, total, zMax - zMin, 0, 0.5f, 1, cat);
                }
                else idx[i] = _mb.Add(p[i].x, p[i].y, p[i].z, nx, ny, nz, c, p[i].x, p[i].y, 0, 0, kind, 0.5f, param, cat);
            }
            if (n == 3) _mb.Triangle(idx[0], idx[1], idx[2]);
            else _mb.Quad(idx[0], idx[1], idx[2], idx[3]);
        }

        /// <summary>Parallelepipedo verniciato (arredo, cartelloni, telai): tinta piena, senza finestre.</summary>
        private void PaintedBox(float u0, float v0, float u1, float v1, float z0, float z1, uint color, float kind = 13, float param = 0)
        {
            Poly(new[] { u0, v0, z0, u1, v0, z0, u1, v0, z1, u0, v0, z1 }, 0, -1, 0, color, kind, param);
            Poly(new[] { u1, v0, z0, u1, v1, z0, u1, v1, z1, u1, v0, z1 }, 1, 0, 0, color, kind, param);
            Poly(new[] { u1, v1, z0, u0, v1, z0, u0, v1, z1, u1, v1, z1 }, 0, 1, 0, color, kind, param);
            Poly(new[] { u0, v1, z0, u0, v0, z0, u0, v0, z1, u0, v1, z1 }, -1, 0, 0, color, kind, param);
            Poly(new[] { u0, v0, z1, u1, v0, z1, u1, v1, z1, u0, v1, z1 }, 0, 0, 1, color, kind, param);
        }

        private void Cone(float cu, float cv, float r, float z0, float h, uint color, float kind = 1, int sides = 12, float param = 1)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                var p0 = center + new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0)) * r;
                var p1 = center + new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1)) * r;
                float nx = (float)Math.Cos(am), ny = (float)Math.Sin(am), nz = r / Math.Max(0.01f, h);
                float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                float seg = (p1 - p0).Length;
                int i0 = _mb.Add(p0.X, p0.Y, z0, nx / nl, ny / nl, nz / nl, c, i * seg, 0, seg * sides, h, kind, 0.5f, param, 0);
                int i1 = _mb.Add(p1.X, p1.Y, z0, nx / nl, ny / nl, nz / nl, c, (i + 1) * seg, 0, seg * sides, h, kind, 0.5f, param, 0);
                int t = _mb.Add(center.X, center.Y, z0 + h, nx / nl, ny / nl, nz / nl, c, (i + 0.5f) * seg, h, seg * sides, h, kind, 0.5f, param, 0);
                _mb.Triangle(i0, i1, t);
            }
        }

        private void Dome(float cu, float cv, float r, float z0, uint color, float kind = 2, float squash = 1f)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            const int sides = 16, rings = 5;
            for (int k = 0; k < rings; k++)
            {
                double b0 = Math.PI / 2 * k / rings, b1 = Math.PI / 2 * (k + 1) / rings;
                for (int i = 0; i < sides; i++)
                {
                    double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides;
                    Vec3 P(double a, double b) => new Vec3(center.X + (float)(Math.Cos(a) * Math.Cos(b) * r), center.Y + (float)(Math.Sin(a) * Math.Cos(b) * r), z0 + (float)(Math.Sin(b) * r * squash));
                    var q0 = P(a0, b0);
                    var q1 = P(a1, b0);
                    var q2 = P(a1, b1);
                    var q3 = P(a0, b1);
                    int Add(Vec3 q) => _mb.Add(q.X, q.Y, q.Z, (q.X - center.X) / r, (q.Y - center.Y) / r, (q.Z - z0) / (r * squash), c, 0, 0, 0, 0, kind, 0.5f, kind > 12.5f ? 0 : 1, 0);
                    _mb.Quad(Add(q0), Add(q1), Add(q2), Add(q3));
                }
            }
        }

        /// <summary>Sfera piena (teste delle statue, lampioni a globo, serbatoi sferici).</summary>
        private void Sphere(float cu, float cv, float cz, float r, uint color, float kind = 13)
        {
            Dome(cu, cv, r, cz, color, kind);
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            const int sides = 12, rings = 4;
            for (int k = 0; k < rings; k++)
            {
                double b0 = -Math.PI / 2 * k / rings, b1 = -Math.PI / 2 * (k + 1) / rings;
                for (int i = 0; i < sides; i++)
                {
                    double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides;
                    Vec3 P(double a, double b) => new Vec3(center.X + (float)(Math.Cos(a) * Math.Cos(b) * r), center.Y + (float)(Math.Sin(a) * Math.Cos(b) * r), cz + (float)(Math.Sin(b) * r));
                    int Add(Vec3 q) => _mb.Add(q.X, q.Y, q.Z, (q.X - center.X) / r, (q.Y - center.Y) / r, (q.Z - cz) / r, c, 0, 0, 0, 0, kind, 0.5f, 0, 0);
                    // sotto: giro inverso perché la latitudine scende
                    _mb.Quad(Add(P(a0, b0)), Add(P(a0, b1)), Add(P(a1, b1)), Add(P(a1, b0)));
                }
            }
        }

        /// <summary>Quadrilatero in coordinate del mondo rivolto verso <paramref name="facing"/> (colore già convertito).</summary>
        private void QuadW(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 facing, uint col, float kind, float param, float w = 1, float h = 1, float cat = 0)
        {
            var n = Vec3.Cross(b - a, c - a);
            float l = n.Length;
            if (l < 1e-7f) return;
            n = n * (1f / l);
            bool flip = Vec3.Dot(n, facing) < 0;
            if (flip) n = n * -1f;
            int i0 = _mb.Add(a.X, a.Y, a.Z, n.X, n.Y, n.Z, col, 0, 0, w, h, kind, 0.5f, param, cat);
            int i1 = _mb.Add(b.X, b.Y, b.Z, n.X, n.Y, n.Z, col, w, 0, w, h, kind, 0.5f, param, cat);
            int i2 = _mb.Add(c.X, c.Y, c.Z, n.X, n.Y, n.Z, col, w, h, w, h, kind, 0.5f, param, cat);
            int i3 = _mb.Add(d.X, d.Y, d.Z, n.X, n.Y, n.Z, col, 0, h, w, h, kind, 0.5f, param, cat);
            if (flip) _mb.Quad(i0, i3, i2, i1);
            else _mb.Quad(i0, i1, i2, i3);
        }

        /// <summary>Lastra sottile a due facce (reti, teli, pannelli di vetro): da a a b, tra le quote z0 e z1.</summary>
        private void Panel(float ua, float va, float ub, float vb, float z0, float z1, uint color, float kind = 13, float param = 0)
        {
            var a = World(ua, va);
            var b = World(ub, vb);
            var d = b - a;
            float len = d.Length;
            if (len < 1e-4f) return;
            var n = new Vec3(d.Y / len, -d.X / len, 0);
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var p0 = new Vec3(a.X, a.Y, z0);
            var p1 = new Vec3(b.X, b.Y, z0);
            var p2 = new Vec3(b.X, b.Y, z1);
            var p3 = new Vec3(a.X, a.Y, z1);
            QuadW(p0, p1, p2, p3, n, c, kind, param, len, z1 - z0);
            QuadW(p0, p1, p2, p3, n * -1f, MeshBuffer.Rgb(Darken(color & 0xFFFFFF, 0.15f)), kind, param, len, z1 - z0);
        }

        /// <summary>Trave a sezione quadrata tra due punti (u, v, quota): tralicci, ruote panoramiche, ringhiere, telai.</summary>
        private void Beam(float ua, float va, float za, float ub, float vb, float zb, float t, uint color, float kind = 13, float param = 0)
        {
            var A2 = World(ua, va);
            var B2 = World(ub, vb);
            var A = new Vec3(A2.X, A2.Y, za);
            var B = new Vec3(B2.X, B2.Y, zb);
            var axis = B - A;
            float len = axis.Length;
            if (len < 1e-4f) return;
            axis = axis * (1f / len);
            var helper = Math.Abs(axis.Z) > 0.9f ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
            var p = Vec3.Cross(axis, helper);
            p = p * (1f / p.Length);
            var q = Vec3.Cross(p, axis);
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var offs = new[] { p + q, q - p, p * -1f - q, p - q };
            for (int k = 0; k < 4; k++)
            {
                var o0 = offs[k] * t;
                var o1 = offs[(k + 1) % 4] * t;
                var facing = offs[k] + offs[(k + 1) % 4];
                QuadW(A + o0, A + o1, B + o1, B + o0, facing, c, kind, param, len, t);
            }
        }

        /// <summary>Parallelepipedo orientato: centro (cu, cv), asse lungo a <paramref name="angle"/> radianti da U, mezze misure.</summary>
        private void OBox(float cu, float cv, float angle, float hu, float hv, float z0, float z1, uint color, float kind = 13, float param = 0, bool top = true)
        {
            var c = World(cu, cv);
            var ax = _u * (float)Math.Cos(angle) + _v * (float)Math.Sin(angle);
            var sd = new Vec2(-ax.Y, ax.X);
            var p0 = c - ax * hu - sd * hv;
            var p1 = c + ax * hu - sd * hv;
            var p2 = c + ax * hu + sd * hv;
            var p3 = c - ax * hu + sd * hv;
            uint col = MeshBuffer.Rgb(color & 0xFFFFFF);
            var ring = new[] { p0, p1, p2, p3 };
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 4];
                var e = b - a;
                float len = e.Length;
                var n = new Vec3(e.Y / len, -e.X / len, 0);
                QuadW(new Vec3(a.X, a.Y, z0), new Vec3(b.X, b.Y, z0), new Vec3(b.X, b.Y, z1), new Vec3(a.X, a.Y, z1), n, col, kind, param, len, z1 - z0);
            }
            if (top)
                QuadW(new Vec3(p0.X, p0.Y, z1), new Vec3(p1.X, p1.Y, z1), new Vec3(p2.X, p2.Y, z1), new Vec3(p3.X, p3.Y, z1), new Vec3(0, 0, 1), col, kind, param, 2 * hu, 2 * hv);
        }

        // ------------------------------------------------------------------ veicoli e oggetti ricorrenti

        /// <summary>Automobile ferma (carrozzeria, vetri, tetto, ruote): lunga 4,3 m, orientata ad <paramref name="angle"/> da U.</summary>
        private void CarBody(float cu, float cv, float angle, uint color, float baseZ = float.NaN)
        {
            float g = float.IsNaN(baseZ) ? Ground(cu, cv) : baseZ;
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu, cv, angle, 1.55f, 0.86f, g, g + 0.42f, 0x1C1C1E);                       // ruote e sottoscocca
            OBox(cu, cv, angle, 2.15f, 0.88f, g + 0.3f, g + 0.92f, color);                    // carrozzeria
            OBox(cu - ca * 0.2f, cv - sa * 0.2f, angle, 1.2f, 0.8f, g + 0.92f, g + 1.38f, 0x26303A);   // vetri
            OBox(cu - ca * 0.2f, cv - sa * 0.2f, angle, 1.05f, 0.78f, g + 1.38f, g + 1.45f, color);    // tetto
        }

        /// <summary>Automobile di un colore comune scelto col seme (parcheggi, cortili).</summary>
        private void Car(float cu, float cv, float angle, long salt, float baseZ = float.NaN) => CarBody(cu, cv, angle, Pick(CarColors, salt), baseZ);

        /// <summary>Furgone (ambulanze, consegne).</summary>
        private void Van(float cu, float cv, float angle, uint color, uint stripe)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu, cv, angle, 1.9f, 0.95f, g, g + 0.45f, 0x1C1C1E);
            OBox(cu - ca * 0.35f, cv - sa * 0.35f, angle, 2.25f, 1.0f, g + 0.35f, g + 2.4f, color);
            OBox(cu + ca * 2.15f, cv + sa * 2.15f, angle, 0.45f, 0.98f, g + 0.35f, g + 1.3f, color);
            OBox(cu + ca * 1.95f, cv + sa * 1.95f, angle, 0.12f, 0.92f, g + 1.3f, g + 2.1f, 0x26303A);
            if (stripe != 0) OBox(cu - ca * 0.35f, cv - sa * 0.35f, angle, 2.27f, 1.02f, g + 1.05f, g + 1.35f, stripe, 13, 0, false);
        }

        /// <summary>Autoarticolato: trattore e semirimorchio (lungo 16 m), orientato ad <paramref name="angle"/>; la cabina è in avanti.</summary>
        private void Truck(float cu, float cv, float angle, uint cab, uint trailer)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu - ca * 1.6f, cv - sa * 1.6f, angle, 6.2f, 1.2f, g, g + 1.1f, 0x2A2A2C);
            OBox(cu - ca * 1.6f, cv - sa * 1.6f, angle, 6.3f, 1.25f, g + 1.1f, g + 4.0f, trailer);
            OBox(cu + ca * 6.1f, cv + sa * 6.1f, angle, 1.3f, 1.2f, g + 0.4f, g + 3.2f, cab);
            OBox(cu + ca * 7.35f, cv + sa * 7.35f, angle, 0.06f, 1.05f, g + 1.9f, g + 2.9f, 0x26303A);
            OBox(cu + ca * 6.1f, cv + sa * 6.1f, angle, 1.0f, 1.1f, g + 3.2f, g + 3.7f, cab);
        }

        /// <summary>Autobus di linea (12 m): fascia dei finestrini, porte, tetto con i climatizzatori.</summary>
        private void Bus(float cu, float cv, float angle, uint color)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu, cv, angle, 5.6f, 1.2f, g, g + 0.5f, 0x1C1C1E);
            OBox(cu, cv, angle, 6.0f, 1.27f, g + 0.35f, g + 3.0f, color);
            OBox(cu, cv, angle, 6.02f, 1.29f, g + 1.25f, g + 2.55f, 0x24303A, 13, 0, false);
            OBox(cu - ca * 1.5f, cv - sa * 1.5f, angle, 1.6f, 0.8f, g + 3.0f, g + 3.35f, 0xD8DADC);
        }

        /// <summary>Lampione stradale: palo, braccio e testa accesa la sera (la testa sta dentro 0,25 m dal palo).</summary>
        private void StreetLamp(float cu, float cv, float h, bool historic = false)
        {
            float g = Ground(cu, cv);
            if (historic)
            {
                // lampione in ghisa: basamento, fusto, lanterna a quattro vetri con il cappello
                Pipe(cu, cv, 0.17f, g, g + 0.7f, 0x1F2422, 10);
                Pipe(cu, cv, 0.07f, g + 0.7f, g + h - 0.7f, 0x1F2422, 8);
                Pipe(cu, cv, 0.11f, g + h - 0.75f, g + h - 0.6f, 0x1F2422, 8);
                OBox(cu, cv, 0.785f, 0.17f, 0.17f, g + h - 0.6f, g + h - 0.12f, 0xFFE0A0, BuildingDecorator.KindLight, 1);
                Cone(cu, cv, 0.27f, g + h - 0.14f, 0.2f, 0x1F2422, BuildingDecorator.KindProp, 8);
                return;
            }
            Pipe(cu, cv, 0.08f, g, g + h - 0.15f, 0x5A6066, 8, 0.7f);
            OBox(cu, cv, 0, 0.24f, 0.13f, g + h - 0.2f, g + h, 0x4A4F55);
            // vetro della lampada sotto la testa
            var a = World(cu - 0.2f, cv - 0.1f);
            var b = World(cu + 0.2f, cv - 0.1f);
            var c = World(cu + 0.2f, cv + 0.1f);
            var d = World(cu - 0.2f, cv + 0.1f);
            float z = g + h - 0.21f;
            QuadW(new Vec3(a.X, a.Y, z), new Vec3(b.X, b.Y, z), new Vec3(c.X, c.Y, z), new Vec3(d.X, d.Y, z), new Vec3(0, 0, -1),
                MeshBuffer.Rgb(0xFFE6B0), BuildingDecorator.KindLight, 1);
        }

        /// <summary>Asta con il tricolore italiano (o un colore solo); il telo va verso +U per <paramref name="flagLen"/> metri.</summary>
        private void FlagPole(float cu, float cv, float h, float flagLen, uint single = 0)
        {
            float g = Ground(cu, cv);
            Pipe(cu, cv, 0.18f, g, g + 0.3f, 0xBFC3C7, 8);
            Pipe(cu, cv, 0.05f, g, g + h, 0xD8DCE0, 6);
            Sphere(cu, cv, g + h + 0.05f, 0.08f, 0xD4B24C);
            float fh = flagLen * 0.66f, z1 = g + h - 0.25f, z0 = z1 - fh;
            uint[] cols = single != 0 ? new[] { single, single, single } : new uint[] { 0x009246, 0xF1F2F1, 0xCE2B37 };
            for (int k = 0; k < 3; k++)
            {
                float a = cu + 0.06f + flagLen * k / 3, b = cu + 0.06f + flagLen * (k + 1) / 3;
                // leggera onda: le strisce non sono tutte nello stesso piano
                float wa = 0.12f * (float)Math.Sin(k * 1.9f), wb = 0.12f * (float)Math.Sin((k + 1) * 1.9f);
                Poly(new[] { a, cv + wa, z0, b, cv + wb, z0, b, cv + wb, z1, a, cv + wa, z1 }, 0, -1, 0, cols[k], BuildingDecorator.KindCanvas, 0);
                Poly(new[] { a, cv + wa, z0, b, cv + wb, z0, b, cv + wb, z1, a, cv + wa, z1 }, 0, 1, 0, Darken(cols[k], 0.12f), BuildingDecorator.KindCanvas, 0);
            }
        }

        /// <summary>Recinzione metallica a pannelli: paletti ogni 2,5 m, due correnti e la rete (tinta scura) fino a h.</summary>
        private void MetalFence(float ua, float va, float ub, float vb, float h, uint color = 0x2E3B33, bool mesh = true, float meshH = -1)
        {
            float len = (float)Math.Sqrt((ub - ua) * (ub - ua) + (vb - va) * (vb - va));
            if (len < 0.2f) return;
            int n = Math.Max(1, (int)Math.Ceiling(len / 2.5f));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float u = ua + (ub - ua) * t, v = va + (vb - va) * t;
                float g = Ground(u, v);
                Beam(u, v, g - 0.2f, u, v, g + h, 0.04f, color);
            }
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                float u0 = ua + (ub - ua) * t0, v0 = va + (vb - va) * t0, u1 = ua + (ub - ua) * t1, v1 = va + (vb - va) * t1;
                float g0 = Ground(u0, v0), g1 = Ground(u1, v1), gm = Math.Min(g0, g1);
                Beam(u0, v0, g0 + h - 0.03f, u1, v1, g1 + h - 0.03f, 0.03f, color);
                // telo frangivento o pannello basso: la rete vera è trasparente, si disegna solo fino a meshH
                if (mesh) Panel(u0, v0, u1, v1, gm + 0.05f, gm + (meshH > 0 ? Math.Min(meshH, h) : h) - 0.06f, Lighten(color, 0.05f), BuildingDecorator.KindProp, 0);
                if (meshH > 0 && meshH < h - 0.3f) Beam(u0, v0, g0 + meshH, u1, v1, g1 + meshH, 0.025f, color);
            }
        }

        /// <summary>Recinzione metallica lungo il bordo della sagoma (rientrata di 0,2 m), con un varco sul fronte largo <paramref name="gate"/>.</summary>
        private void LotFence(float h, float gate, uint color = 0x2E3B33, bool mesh = false, float inset = 0.2f, float meshH = -1)
        {
            float i = inset;
            if (gate > 0.1f)
            {
                MetalFence(i, i, _w / 2 - gate / 2, i, h, color, mesh, meshH);
                MetalFence(_w / 2 + gate / 2, i, _w - i, i, h, color, mesh, meshH);
            }
            else MetalFence(i, i, _w - i, i, h, color, mesh, meshH);
            MetalFence(_w - i, i, _w - i, _d - i, h, color, mesh, meshH);
            MetalFence(_w - i, _d - i, i, _d - i, h, color, mesh, meshH);
            MetalFence(i, _d - i, i, i, h, color, mesh, meshH);
        }

        /// <summary>Panchina in legno con le gambe in ghisa sul rettangolo (lato lungo lungo U, schienale verso +V).</summary>
        private void Bench(float u0, float v0, float u1, float v1)
        {
            float g = Base(u0, v0, u1, v1);
            float dv = v1 - v0;
            for (int k = 0; k < 3; k++)
                PaintedBox(u0 + 0.05f, v0 + 0.05f + k * (dv - 0.2f) / 3, u1 - 0.05f, v0 + 0.05f + k * (dv - 0.2f) / 3 + (dv - 0.2f) / 3.6f, g + 0.42f, g + 0.47f, 0x1A0744C);
            PaintedBox(u0 + 0.05f, v1 - 0.12f, u1 - 0.05f, v1 - 0.06f, g + 0.55f, g + 0.68f, 0x1A0744C);
            PaintedBox(u0 + 0.05f, v1 - 0.12f, u1 - 0.05f, v1 - 0.06f, g + 0.74f, g + 0.86f, 0x1A0744C);
            foreach (float u in new[] { u0 + 0.12f, u1 - 0.18f })
            {
                PaintedBox(u, v0 + 0.08f, u + 0.06f, v1 - 0.06f, g, g + 0.42f, 0x1303438);
                PaintedBox(u, v1 - 0.14f, u + 0.06f, v1 - 0.06f, g + 0.42f, g + 0.88f, 0x1303438);
            }
        }

        /// <summary>Cestino dei rifiuti con il coperchio.</summary>
        private void Bin(float cu, float cv)
        {
            float g = Ground(cu, cv);
            Pipe(cu, cv, 0.22f, g - 0.05f, g + 0.85f, 0x2F4F3A, 10);
            Pipe(cu, cv, 0.24f, g + 0.85f, g + 0.95f, 0x26302A, 10);
        }

        // ------------------------------------------------------------------ colori

        private static uint Lighten(uint rgb, float k)
        {
            uint flag = rgb & 0xFF000000;
            uint r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            r = (uint)Math.Min(255, r + (255 - r) * k);
            g = (uint)Math.Min(255, g + (255 - g) * k);
            b = (uint)Math.Min(255, b + (255 - b) * k);
            return flag | (r << 16) | (g << 8) | b;
        }

        private static uint Darken(uint rgb, float k)
        {
            uint flag = rgb & 0xFF000000;
            uint r = (uint)(((rgb >> 16) & 0xFF) * (1 - k)), g = (uint)(((rgb >> 8) & 0xFF) * (1 - k)), b = (uint)((rgb & 0xFF) * (1 - k));
            return flag | (r << 16) | (g << 8) | b;
        }

        private readonly struct Vec3
        {
            public readonly float X, Y, Z;

            public Vec3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec3 operator *(Vec3 a, float k) => new Vec3(a.X * k, a.Y * k, a.Z * k);
            public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
    }
}
